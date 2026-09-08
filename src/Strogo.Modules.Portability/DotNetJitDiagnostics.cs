using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kernel.Core;

namespace Strogo.Modules.Portability;

public sealed record DotNetJitDiagnosticPlan(
    string ProfileId,
    string ModuleDigest,
    string DafnySourceDigest,
    string PortabilityManifestDigest,
    string PackageDigest,
    string ArtifactDigest,
    string EntryAssemblyDigest,
    string SourceMapDigest,
    string PublicApiDigest,
    string EntrySymbol,
    string CandidateEntityId,
    string CandidateGeneratedName,
    string CandidateSymbol);

public sealed class DotNetJitDiagnosticReceipt
{
    private readonly byte[] bytes;

    internal DotNetJitDiagnosticReceipt(
        DotNetJitDiagnosticPlan plan,
        string os,
        string arch,
        string runtimeVendor,
        string runtimeVersion,
        string runtimeClosureDigest,
        string processId,
        string calls,
        string consumerOutputDigest,
        string jitLogDigest,
        ImmutableArray<string> compilationEvents,
        ReadOnlySpan<byte> bytes)
    {
        Plan = plan;
        Os = os;
        Arch = arch;
        RuntimeVendor = runtimeVendor;
        RuntimeVersion = runtimeVersion;
        RuntimeClosureDigest = runtimeClosureDigest;
        ProcessId = processId;
        Calls = calls;
        ConsumerOutputDigest = consumerOutputDigest;
        JitLogDigest = jitLogDigest;
        CompilationEvents = compilationEvents;
        this.bytes = bytes.ToArray();
    }

    public DotNetJitDiagnosticPlan Plan { get; }
    public string Os { get; }
    public string Arch { get; }
    public string RuntimeVendor { get; }
    public string RuntimeVersion { get; }
    public string RuntimeClosureDigest { get; }
    public string ProcessId { get; }
    public string Calls { get; }
    public string ConsumerOutputDigest { get; }
    public string JitLogDigest { get; }
    public ImmutableArray<string> CompilationEvents { get; }
    public byte[] Bytes => bytes.ToArray();
}

public static partial class DotNetJitDiagnostics
{
    public const string PlanSchemaVersion = "strogo.dotnet-jit-plan.v0.1";
    public const string SchemaVersion = "strogo.dotnet-jit-receipt.v0.1";
    public const int RequiredCalls = 50_000;
    public const int MaximumJitLogBytes = 1_048_576;
    public const int MaximumConsumerOutputBytes = 4_096;
    public const string EntrySymbol = "Strogo.Portable.V01.ModuleApi:Invoke";
    public const string CandidateEntityId = "function/summarize";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex ProcessMarker = ProcessMarkerRegex();

    public static DotNetJitDiagnosticPlan Bind(string packageDirectory, string expectedPortabilityManifestDigest)
    {
        var package = PortabilityPackage.Validate(packageDirectory, expectedPortabilityManifestDigest);
        if (package.ProfileId != PortabilityVersions.DotNetProfile)
            Reject("ProfileMismatch", "$/profileId");

        var root = Path.GetFullPath(packageDirectory);
        using var manifest = CanonicalJson.ParseStrict(File.ReadAllText(Path.Combine(root, "portability-manifest.json"), StrictUtf8));
        var manifestRoot = manifest.RootElement;
        var moduleDigest = Digest(manifestRoot, "moduleDigest");
        var dafnySourceDigest = Digest(manifestRoot, "dafnySourceDigest");
        var publicApiDigest = Digest(manifestRoot, "publicApiDigest");

        var sourceMapFile = package.Files.SingleOrDefault(file => file.Role == "source-map")
            ?? throw new PortabilityContractException("JitEvidenceMissing", "$/sourceMap", new { reason = "SourceMapUnavailable" });
        var entryFile = package.Files.SingleOrDefault(file => file.Role == "entry-artifact")
            ?? throw new PortabilityContractException("JitEvidenceMissing", "$/entryArtifact", new { reason = "EntryArtifactUnavailable" });
        var publicApiFile = package.Files.SingleOrDefault(file => file.Path == "content/public-api.json")
            ?? throw new PortabilityContractException("JitEvidenceMissing", "$/publicApi", new { reason = "PublicApiUnavailable" });

        using var sourceMap = CanonicalJson.ParseStrict(File.ReadAllText(PhysicalPath(root, sourceMapFile.Path), StrictUtf8));
        if (sourceMap.RootElement.ValueKind != JsonValueKind.Array)
            Reject("SourceMapInvalid", "$/sourceMap");
        var matches = sourceMap.RootElement.EnumerateArray().Where(item =>
        {
            Fields(item, "entityId", "generatedName", "line");
            return String(item, "entityId") == CandidateEntityId;
        }).ToArray();
        if (matches.Length != 1)
            throw new PortabilityContractException("JitEvidenceMissing", "$/sourceMap/function/summarize", new { reason = "CandidateSymbolUnavailable" });
        var generatedName = String(matches[0], "generatedName");
        if (!GeneratedNameRegex().IsMatch(generatedName))
            Reject("CandidateSymbolInvalid", "$/sourceMap/function/summarize/generatedName");

        var wrapperMatches = sourceMap.RootElement.EnumerateArray()
            .Where(item => String(item, "entityId") == "wire/wrapper/invoke" && String(item, "generatedName") == "Invoke")
            .Count();
        if (wrapperMatches != 1)
            throw new PortabilityContractException("JitEvidenceMissing", "$/sourceMap/wire/wrapper/invoke", new { reason = "WrapperSymbolUnavailable" });

        using var publicApi = CanonicalJson.ParseStrict(File.ReadAllText(PhysicalPath(root, publicApiFile.Path), StrictUtf8));
        var targetOperations = publicApi.RootElement.GetProperty("targetOperations");
        if (String(targetOperations, "csharp") != "Strogo.Portable.V01.ModuleApi.Invoke(string)->string")
            Reject("PublicEntrySymbolMismatch", "$/publicApi/targetOperations/csharp");
        if (PortabilityContract.DomainHash("strogo.public-api.v0.1/artifact", File.ReadAllBytes(PhysicalPath(root, publicApiFile.Path))) != publicApiDigest)
            Reject("PublicApiIdentityMismatch", "$/publicApiDigest");

        return new(
            package.ProfileId,
            moduleDigest,
            dafnySourceDigest,
            package.PortabilityManifestDigest,
            package.PackageDigest,
            package.ArtifactDigest,
            entryFile.Sha256,
            sourceMapFile.Sha256,
            publicApiDigest,
            EntrySymbol,
            CandidateEntityId,
            generatedName,
            $"Candidate.__default:{generatedName}");
    }

    public static byte[] PlanBytes(DotNetJitDiagnosticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return CanonicalJson.Encode(new
        {
            schemaVersion = PlanSchemaVersion,
            profileId = plan.ProfileId,
            moduleDigest = plan.ModuleDigest,
            dafnySourceDigest = plan.DafnySourceDigest,
            portabilityManifestDigest = plan.PortabilityManifestDigest,
            packageDigest = plan.PackageDigest,
            artifactDigest = plan.ArtifactDigest,
            entryAssemblyDigest = plan.EntryAssemblyDigest,
            sourceMapDigest = plan.SourceMapDigest,
            publicApiDigest = plan.PublicApiDigest,
            entrySymbol = plan.EntrySymbol,
            candidateEntityId = plan.CandidateEntityId,
            candidateGeneratedName = plan.CandidateGeneratedName,
            candidateSymbol = plan.CandidateSymbol,
            calls = RequiredCalls.ToString(CultureInfo.InvariantCulture)
        });
    }

    public static DotNetJitDiagnosticReceipt Validate(
        DotNetJitDiagnosticPlan plan,
        ReadOnlySpan<byte> runtimeReportBytes,
        ReadOnlySpan<byte> consumerOutputBytes,
        ReadOnlySpan<byte> jitLogBytes,
        string expectedOs,
        string expectedArch)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (consumerOutputBytes.Length is 0 or > MaximumConsumerOutputBytes)
            Reject("ConsumerOutputBounds", "$/consumerOutput");
        if (jitLogBytes.Length is 0 or > MaximumJitLogBytes)
            Reject("JitLogBounds", "$/jitLog");
        if (expectedOs is not ("windows" or "linux") || expectedArch != "x64")
            Reject("PlatformMismatch", "$/platform");

        using var runtime = CanonicalJson.ParseStrict(StrictUtf8.GetString(runtimeReportBytes));
        var runtimeRoot = runtime.RootElement;
        Fields(runtimeRoot, "schemaVersion", "status", "reasonCode", "profileId", "os", "arch", "runtimeVendor", "runtimeVersion", "runtimeClosureDigest", "launcherDigest", "harnessDigest", "runtimeFiles");
        Require(runtimeRoot, "schemaVersion", "strogo.environment-report.v0.1", "$/runtime/schemaVersion");
        Require(runtimeRoot, "status", "Passed", "$/runtime/status");
        Require(runtimeRoot, "reasonCode", "None", "$/runtime/reasonCode");
        Require(runtimeRoot, "profileId", plan.ProfileId, "$/runtime/profileId");
        Require(runtimeRoot, "os", expectedOs, "$/runtime/os");
        Require(runtimeRoot, "arch", expectedArch, "$/runtime/arch");
        Require(runtimeRoot, "runtimeVendor", "Microsoft", "$/runtime/runtimeVendor");
        Require(runtimeRoot, "runtimeVersion", "10.0.11", "$/runtime/runtimeVersion");

        var output = StrictUtf8.GetString(consumerOutputBytes).TrimEnd('\r', '\n');
        var marker = ProcessMarker.Match(output);
        if (!marker.Success)
            throw new PortabilityContractException("JitEvidenceMissing", "$/consumerOutput", new { reason = "ProcessReceiptMissing" });
        var processId = marker.Groups["pid"].Value;
        if (!int.TryParse(processId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedProcessId) || parsedProcessId <= 0)
            Reject("ProcessIdInvalid", "$/consumerOutput/processId");
        var calls = marker.Groups["calls"].Value;
        if (calls != RequiredCalls.ToString(CultureInfo.InvariantCulture))
            throw new PortabilityContractException("JitEvidenceMissing", "$/consumerOutput/calls", new { reason = "InsufficientDiagnosticCalls", expected = RequiredCalls.ToString(CultureInfo.InvariantCulture), actual = calls });

        var jitLog = StrictUtf8.GetString(jitLogBytes);
        var entryEvent = FindCompilationEvent(jitLog, plan.EntrySymbol, "$/events/entry");
        var candidateEvent = FindCompilationEvent(jitLog, plan.CandidateSymbol, "$/events/candidate");
        var events = ImmutableArray.Create(entryEvent, candidateEvent);
        var consumerDigest = CanonicalJson.RawDigest(consumerOutputBytes);
        var jitDigest = CanonicalJson.RawDigest(jitLogBytes);
        var runtimeClosureDigest = Digest(runtimeRoot, "runtimeClosureDigest");
        var bytes = CanonicalJson.Encode(new
        {
            schemaVersion = SchemaVersion,
            status = "Passed",
            profileId = plan.ProfileId,
            os = expectedOs,
            arch = expectedArch,
            runtimeVendor = String(runtimeRoot, "runtimeVendor"),
            runtimeVersion = String(runtimeRoot, "runtimeVersion"),
            runtimeClosureDigest,
            portabilityManifestDigest = plan.PortabilityManifestDigest,
            packageDigest = plan.PackageDigest,
            artifactDigest = plan.ArtifactDigest,
            entryAssemblyDigest = plan.EntryAssemblyDigest,
            moduleDigest = plan.ModuleDigest,
            dafnySourceDigest = plan.DafnySourceDigest,
            sourceMapDigest = plan.SourceMapDigest,
            publicApiDigest = plan.PublicApiDigest,
            processId,
            calls,
            flags = new[]
            {
                new { id = "DOTNET_JitDisasm", value = $"{plan.EntrySymbol} {plan.CandidateSymbol}" },
                new { id = "DOTNET_JitDisasmDiffable", value = "1" },
                new { id = "DOTNET_JitDisasmTesting", value = "1" },
                new { id = "DOTNET_JitNoInline", value = "1" },
                new { id = "DOTNET_ReadyToRun", value = "0" },
                new { id = "DOTNET_TieredCompilation", value = "0" },
                new { id = "DOTNET_JitStdOutFile", value = "jit.log" }
            },
            compilationEvents = events,
            consumerOutputDigest = consumerDigest,
            jitLogDigest = jitDigest,
            assertionBoundary = "CompilationEventsOnly"
        });
        return new(plan, expectedOs, expectedArch, String(runtimeRoot, "runtimeVendor"), String(runtimeRoot, "runtimeVersion"), runtimeClosureDigest, processId, calls, consumerDigest, jitDigest, events, bytes);
    }

    private static string FindCompilationEvent(string log, string symbol, string locus)
    {
        var prefix = $"; Assembly listing for method {symbol}(";
        var matches = log.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith(" (FullOpts)", StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
            throw new PortabilityContractException("JitEvidenceMissing", locus, new { reason = "CompilationEventMissingOrAmbiguous", symbol, actual = matches.Length.ToString(CultureInfo.InvariantCulture) });
        var signature = matches[0]["; Assembly listing for method ".Length..^" (FullOpts)".Length];
        if (!log.Contains($"; BEGIN METHOD {signature}", StringComparison.Ordinal) || !log.Contains($"; END METHOD {signature}", StringComparison.Ordinal))
            throw new PortabilityContractException("JitEvidenceMissing", locus, new { reason = "CompilationEventIncomplete", symbol });
        return signature;
    }

    private static string PhysicalPath(string root, string logicalPath)
        => Path.Combine(root, logicalPath.Replace('/', Path.DirectorySeparatorChar));

    private static void Require(JsonElement element, string property, string expected, string locus)
    {
        if (String(element, property) != expected) Reject("IdentityMismatch", locus);
    }

    private static string String(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new PortabilityContractException("JitEvidenceRejected", $"$/{property}", new { reason = "StringExpected" });

    private static string Digest(JsonElement element, string property)
    {
        var value = String(element, property);
        if (!DigestRegex().IsMatch(value)) Reject("DigestInvalid", $"$/{property}");
        return value;
    }

    private static void Fields(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) Reject("ObjectExpected", "$/.kind");
        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Distinct(StringComparer.Ordinal).Count() != actual.Length || !actual.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            Reject("UnexpectedFields", "$/.fields");
    }

    private static void Reject(string reason, string locus)
        => throw new PortabilityContractException("JitEvidenceRejected", locus, new { reason });

    [GeneratedRegex("^[A-Z][0-9]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedNameRegex();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestRegex();

    [GeneratedRegex("^PASS dotnet JIT diagnostic pid=(?<pid>[1-9][0-9]*) calls=(?<calls>[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ProcessMarkerRegex();
}
