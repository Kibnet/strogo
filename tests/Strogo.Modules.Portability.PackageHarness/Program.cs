using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Kernel.Core;
using Strogo.Modules;
using Strogo.Modules.Portability;

try
{
    if (args.Length == 0) Fail("command is required: build, validate, runtime, matrix, jit-plan or jit-receipt");
    var options = ParseOptions(args.Skip(1).ToArray());
    switch (args[0])
    {
        case "build":
            BuildPackage(options);
            break;
        case "validate":
            ValidatePackage(options);
            break;
        case "runtime":
            InspectRuntime(options);
            break;
        case "matrix":
            BuildMatrixCheck(options);
            break;
        case "jit-receipt":
            BuildJitReceipt(options);
            break;
        case "jit-plan":
            BuildJitPlan(options);
            break;
        default:
            Fail($"unknown command: {args[0]}");
            break;
    }
}
catch (PortabilityContractException exception)
{
    Console.Error.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Encode(new
    {
        status = "Rejected",
        code = exception.Code,
        locus = exception.Locus,
        details = exception.Details
    })));
    Environment.ExitCode = 1;
}
catch (PackageHarnessException exception)
{
    Console.Error.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Encode(new
    {
        status = "Rejected",
        code = "PackageHarnessRejected",
        locus = "$",
        details = new { reason = exception.Message }
    })));
    Environment.ExitCode = 1;
}

static void BuildPackage(IReadOnlyDictionary<string, string> options)
{
    if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64)
        Fail("authoritative dotnet validation package build requires Linux x64");
    var repo = FullPath(Required(options, "--repo-root"));
    var proofRun = FullPath(Required(options, "--proof-run"));
    var buildRun = FullPath(Required(options, "--build-run"));
    var dafnyRoot = FullPath(Required(options, "--dafny-root"));
    var dafny = FullPath(Required(options, "--dafny-executable"));
    var dotnet = FullPath(Required(options, "--dotnet-executable"));
    var outputA = FullPath(Required(options, "--output-a"));
    var outputB = FullPath(Required(options, "--output-b"));
    var receiptPath = FullPath(Required(options, "--receipt"));
    RequireFile(dafny);
    RequireFile(dotnet);
    RequireDirectory(dafnyRoot);
    if (!string.Equals(Path.GetDirectoryName(dafny), Path.TrimEndingDirectorySeparator(dafnyRoot), StringComparison.Ordinal))
        Fail("Dafny executable must be a direct child of the exact --dafny-root");
    if (Directory.Exists(outputA) || File.Exists(outputA) || Directory.Exists(outputB) || File.Exists(outputB)) Fail("package output already exists");
    if (File.Exists(receiptPath)) Fail("receipt already exists");

    var fixture = Path.Combine(repo, "fixtures", "portability-v0.1");
    var moduleBytes = File.ReadAllBytes(Path.Combine(fixture, "module.json"));
    var ownerBytes = File.ReadAllBytes(Path.Combine(fixture, "owner.json"));
    var parsed = ModulesParser.ParseModule(moduleBytes);
    var module = ModulesCompiler.Compile(parsed);
    var owner = OwnerBundleV04Parser.Parse(ownerBytes);
    var binding = PortabilityContract.Bind(module, owner);
    var lowering = ModulesDafnyLowerer.LowerPortableValidation(module, owner);
    var canonicalOwnerBytes = OwnerBundleV04Codec.Canonicalize(owner.BundleId, owner.Types, owner.EntryContracts, owner.Models, owner.Limits);

    var proofSource = Path.Combine(proofRun, "a", "candidate.dfy");
    var proofSourceMap = Path.Combine(proofRun, "a", "candidate.source-map.json");
    var proofObligations = Path.Combine(proofRun, "a", "candidate.obligations.json");
    RequireBytes(proofSource, lowering.SourceBytes, "proof source");
    var sourceMapBytes = CanonicalJson.Encode(lowering.SourceMap);
    RequireBytes(proofSourceMap, sourceMapBytes, "proof source map");
    RequireBytes(proofObligations, CanonicalJson.Encode(lowering.Obligations), "proof obligations");

    using var proofReport = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(proofRun, "report.json")));
    var proofRoot = proofReport.RootElement;
    RequireValue(proofRoot, "schemaVersion", "strogo.portability-proof-run.v0.1");
    RequireValue(proofRoot, "status", "Passed");
    RequireBoolean(proofRoot, "repositoryDirty", false);
    RequireValue(proofRoot, "moduleDigest", binding.Module.SourceDigest);
    RequireValue(proofRoot, "ownerBundleDigest", binding.OwnerBundle.BundleDigest);
    RequireValue(proofRoot, "dafnySourceDigest", lowering.SourceDigest);
    RequireValue(proofRoot, "proofIdentity", lowering.ProofIdentity);
    RequireValue(proofRoot, "proofObligations", lowering.Obligations.Length.ToString(CultureInfo.InvariantCulture));
    RequireValue(proofRoot, "twoCleanGeneration", "ByteEqual");
    var proofRevision = String(proofRoot, "repositoryRevision");
    RequireValue(proofRoot.GetProperty("environment"), "system", "Linux");
    RequireValue(proofRoot.GetProperty("environment"), "machine", "x86_64");
    var strongRuns = proofRoot.GetProperty("strongRuns").EnumerateArray().ToArray();
    if (strongRuns.Length != 2) Fail("proof report must contain two strong runs");
    foreach (var lane in new[] { "a", "b" })
    {
        var run = strongRuns.Single(item => String(item, "lane") == lane);
        RequireValue(run, "outcome", "Verified");
        RequireValue(run, "verified", "42");
        RequireValue(run, "errors", "0");
        RequireValue(run, "logSha256", RawDigest(Path.Combine(proofRun, "logs", $"strong-{lane}.log")));
    }
    var proofToolchain = proofRoot.GetProperty("toolchain");
    var dafnyVersion = String(proofToolchain, "dafnyVersion");
    var dafnyExecutableDigest = RawDigest(dafny);
    RequireValue(proofToolchain, "dafnyExecutableSha256", dafnyExecutableDigest);
    RequireValue(proofToolchain, "dotnetSdk", Run(dotnet, repo, "--version"));
    if (Run(dafny, repo, "--version") != dafnyVersion) Fail("Dafny version differs from proof report");

    using var buildReport = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(buildRun, "report.json")));
    var buildRoot = buildReport.RootElement;
    RequireValue(buildRoot, "schemaVersion", "strogo.dotnet-build-run.v0.1");
    RequireValue(buildRoot, "status", "Passed");
    RequireValue(buildRoot, "profileId", PortabilityVersions.DotNetProfile);
    RequireBoolean(buildRoot, "repositoryDirty", false);
    RequireValue(buildRoot, "dafnySourceDigest", lowering.SourceDigest);
    RequireValue(buildRoot, "twoCleanTranslation", "ByteEqual");
    RequireValue(buildRoot, "twoCleanBuild", "ByteEqual");
    RequireValue(buildRoot, "directoryBuildImports", "Disabled");
    RequireValue(buildRoot, "overflowChecks", "Enabled");
    RequireValue(buildRoot, "consumerOutcome", "Passed");
    var buildRevision = String(buildRoot, "repositoryRevision");
    var currentRevision = Run("git", repo, "-C", repo, "rev-parse", "HEAD");
    if (Run("git", repo, "-C", repo, "status", "--porcelain").Length != 0) Fail("package harness requires a clean repository");
    if (proofRevision != currentRevision || buildRevision != currentRevision) Fail("proof, build and package harness must use the same clean repository revision");

    var candidateCs = Path.Combine(buildRun, "a", "Candidate.cs");
    var translationRecord = Path.Combine(buildRun, "a", "translation-record.dtr");
    var artifact = Path.Combine(buildRun, "artifact", "strogo.portable.v01.dll");
    var adapter = Path.Combine(repo, "targets", "dotnet-managed-v1", "ModuleApi.cs");
    var project = Path.Combine(repo, "targets", "dotnet-managed-v1", "Strogo.Portable.V01.csproj");
    foreach (var file in new[] { candidateCs, translationRecord, artifact, adapter, project }) RequireFile(file);
    RequireValue(buildRoot, "translatedSourceDigest", RawDigest(candidateCs));
    RequireValue(buildRoot, "translationRecordDigest", RawDigest(translationRecord));
    RequireValue(buildRoot, "artifactDigest", RawDigest(artifact));
    RequireValue(buildRoot, "adapterDigest", RawDigest(adapter));
    RequireValue(buildRoot, "projectDigest", RawDigest(project));
    RequireValue(buildRoot.GetProperty("toolchain"), "dafnyExecutableSha256", dafnyExecutableDigest);
    RequireValue(buildRoot.GetProperty("toolchain"), "dafnyVersion", dafnyVersion);
    var dotnetSdk = String(buildRoot.GetProperty("toolchain"), "dotnetSdk");
    if (Run(dotnet, repo, "--version") != dotnetSdk) Fail("dotnet SDK differs from build report");

    var proofCommandBytes = CanonicalJson.Encode(new
    {
        executable = "tools/dafny",
        arguments = new[] { "verify", "candidate.dfy", "--enforce-determinism", "--cores", "2", "--verification-time-limit", "30" },
        timeoutSeconds = "180"
    });
    var proofToolchainBytes = ProofInventory(
        [InventoryBytes("commands/verify.json", proofCommandBytes, "configuration"), Inventory("tools/dafny", dafny, "executable")],
        [("dafny", dafnyVersion), ("portable-wire-lowering", ModulesDafnyLowerer.PortableWireToolchainIdentity)]);
    var closureFiles = EnumerateClosedFiles(dafnyRoot).Select(path =>
    {
        var relative = Path.GetRelativePath(dafnyRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        var encoded = Convert.ToHexString(Encoding.UTF8.GetBytes(relative)).ToLowerInvariant();
        return Inventory($"closure/{encoded}.bin", path, path == dafny ? "executable" : "runtime");
    }).ToArray();
    var proofClosureBytes = ProofInventory(closureFiles, [("dafny", dafnyVersion), ("platform", "linux-x64")]);
    var transcriptBytes = CanonicalJson.Encode(new
    {
        schemaVersion = "strogo.validation-proof-transcript.v0.1",
        loweringProofIdentity = lowering.ProofIdentity,
        outcome = "Verified",
        runs = strongRuns.OrderBy(item => String(item, "lane"), StringComparer.Ordinal).Select(item => new
        {
            lane = String(item, "lane"),
            outcome = String(item, "outcome"),
            verified = String(item, "verified"),
            errors = String(item, "errors"),
            logSha256 = String(item, "logSha256")
        }).ToArray(),
        twoCleanReplay = "ByteEqual"
    });

    var proofSourceFile = new PortabilityPackageFile("content/candidate.dfy", RawDigest(proofSource), Length(proofSource), "proof-source");
    var transcriptDigest = PortabilityValidationProof.TranscriptDigest(transcriptBytes);
    var proofArtifact = PortabilityValidationProof.Build(new(
        binding.Module.SourceDigest,
        binding.OwnerBundle.BundleDigest,
        PortabilityValidationProof.ToolchainDigest(proofToolchainBytes),
        PortabilityValidationProof.ClosureDigest(proofClosureBytes),
        PortabilityValidationProof.ProofSourcesDigest([proofSourceFile]),
        transcriptDigest,
        PortabilityValidationProof.SourceMapDigest(sourceMapBytes),
        "Verified",
        lowering.Obligations.Select(obligation => new PortabilityValidationProofObligation(
            obligation.Id,
            obligation.Kind,
            "Verified",
            PortabilityValidationProof.EvidenceDigest(obligation.Kind, CanonicalJson.Encode(new
            {
                obligationId = obligation.Id,
                entityId = obligation.EntityId,
                line = obligation.Line.ToString(CultureInfo.InvariantCulture),
                transcriptDigest
            })))).ToArray()));

    var translatorInventoryBytes = ToolInventory(
        PortabilityVersions.DotNetProfile,
        [
            Inventory("input/candidate.dfy", proofSource, "source"),
            Inventory("output/candidate.cs", candidateCs, "source"),
            Inventory("output/translation-record.dtr", translationRecord, "configuration"),
            Inventory("tools/dafny", dafny, "executable")
        ],
        [("dafny", dafnyVersion), ("portable-wire-lowering", ModulesDafnyLowerer.PortableWireToolchainIdentity)]);
    var buildToolchainInventoryBytes = ToolInventory(
        PortabilityVersions.DotNetProfile,
        [
            Inventory("input/candidate.cs", candidateCs, "source"),
            Inventory("input/module-api.cs", adapter, "source"),
            Inventory("input/strogo.portable.v01.csproj", project, "configuration"),
            Inventory("output/strogo.portable.v01.dll", artifact, "archive"),
            Inventory("tools/dotnet", dotnet, "executable")
        ],
        [("directory-build-imports", "disabled"), ("dotnet-sdk", dotnetSdk), ("overflow-checks", "enabled"), ("target-framework", "net10.0")]);
    var runtimeRequirementBytes = File.ReadAllBytes(Path.Combine(fixture, "dotnet-runtime-requirement.json"));
    var content = new List<PortabilityPackageContent>
    {
        new("content/module.json", "module", parsed.CanonicalSource),
        new("content/owner.json", "bundle", canonicalOwnerBytes),
        new("content/proof.json", "proof", proofArtifact.Bytes),
        new("content/candidate.dfy", "proof-source", File.ReadAllBytes(proofSource)),
        new("content/source-map.json", "source-map", sourceMapBytes),
        new("content/proof-command.json", "metadata", proofCommandBytes),
        new("content/proof-toolchain.json", "metadata", proofToolchainBytes),
        new("content/proof-closure.json", "metadata", proofClosureBytes),
        new("content/proof-transcript.json", "metadata", transcriptBytes),
        new("content/strogo.portable.v01.dll", "entry-artifact", File.ReadAllBytes(artifact)),
        new("content/module-api.cs", "adapter", File.ReadAllBytes(adapter)),
        new("content/public-api.json", "metadata", binding.PublicApiBytes),
        new("content/runtime-requirement.json", "metadata", runtimeRequirementBytes),
        new("content/translator-inventory.json", "metadata", translatorInventoryBytes),
        new("content/build-toolchain-inventory.json", "metadata", buildToolchainInventoryBytes)
    };
    var definition = new PortabilityPackageDefinition(
        PortabilityVersions.DotNetProfile,
        binding.Module.SourceDigest,
        binding.OwnerBundle.BundleDigest,
        proofArtifact.Digest,
        lowering.SourceDigest,
        PortabilityContract.DomainHash($"strogo.portability.v0.1/translator/{PortabilityVersions.DotNetProfile}", translatorInventoryBytes),
        PortabilityContract.DomainHash($"strogo.portability.v0.1/build-toolchain/{PortabilityVersions.DotNetProfile}", buildToolchainInventoryBytes),
        PortabilityContract.DomainHash($"strogo.portability.v0.1/runtime-requirement/{PortabilityVersions.DotNetProfile}", runtimeRequirementBytes),
        binding.PublicApiDigest,
        "content/strogo.portable.v01.dll",
        content);

    var receiptA = PortabilityPackage.Build(outputA, definition);
    var receiptB = PortabilityPackage.Build(outputB, definition);
    if (receiptA.ProfileId != receiptB.ProfileId ||
        receiptA.ArtifactDigest != receiptB.ArtifactDigest ||
        receiptA.PackageDigest != receiptB.PackageDigest ||
        receiptA.PortabilityManifestDigest != receiptB.PortabilityManifestDigest ||
        !receiptA.Files.SequenceEqual(receiptB.Files) ||
        !EqualTrees(outputA, outputB)) Fail("two package roots are not byte equal");
    Directory.CreateDirectory(Path.GetDirectoryName(receiptPath) ?? throw new InvalidOperationException("receipt path has no directory"));
    File.WriteAllBytes(receiptPath, CanonicalJson.Encode(new
    {
        schemaVersion = "strogo.dotnet-validation-package-build.v0.1",
        status = "Passed",
        profileId = receiptA.ProfileId,
        repositoryRevision = currentRevision,
        proofRunRevision = proofRevision,
        buildRunRevision = buildRevision,
        proofClosureFiles = closureFiles.Length.ToString(CultureInfo.InvariantCulture),
        artifactDigest = receiptA.ArtifactDigest,
        packageDigest = receiptA.PackageDigest,
        portabilityManifestDigest = receiptA.PortabilityManifestDigest,
        proofDigest = proofArtifact.Digest,
        translatorDigest = definition.TranslatorDigest,
        buildToolchainDigest = definition.BuildToolchainDigest,
        twoPackageRoots = "ByteEqual"
    }));
    Console.WriteLine($"PASS actual dotnet package files={receiptA.Files.Length} closure={closureFiles.Length} manifest={receiptA.PortabilityManifestDigest}");
}

static void ValidatePackage(IReadOnlyDictionary<string, string> options)
{
    var package = FullPath(Required(options, "--package"));
    var expected = Required(options, "--expected-manifest-digest");
    var stagedArtifact = FullPath(Required(options, "--staged-artifact"));
    var reportPath = FullPath(Required(options, "--report"));
    if (File.Exists(stagedArtifact) || Directory.Exists(stagedArtifact)) Fail("staged artifact path already exists");
    if (File.Exists(reportPath)) Fail("validation report already exists");
    var receipt = PortabilityPackage.Validate(package, expected);
    var entry = receipt.Files.Single(file => file.Role == "entry-artifact");
    var source = Path.Combine(package, entry.Path.Replace('/', Path.DirectorySeparatorChar));
    var bytes = File.ReadAllBytes(source);
    if (CanonicalJson.RawDigest(bytes) != entry.Sha256) Fail("entry artifact changed after package validation");
    Directory.CreateDirectory(Path.GetDirectoryName(stagedArtifact) ?? throw new InvalidOperationException("staged artifact path has no directory"));
    using (var stream = new FileStream(stagedArtifact, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes);
    if (RawDigest(stagedArtifact) != entry.Sha256) Fail("staged artifact digest mismatch");
    Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? throw new InvalidOperationException("report path has no directory"));
    File.WriteAllBytes(reportPath, CanonicalJson.Encode(new
    {
        schemaVersion = "strogo.dotnet-validation-package-check.v0.1",
        status = "Passed",
        profileId = receipt.ProfileId,
        artifactDigest = receipt.ArtifactDigest,
        packageDigest = receipt.PackageDigest,
        portabilityManifestDigest = receipt.PortabilityManifestDigest,
        entryArtifactSha256 = entry.Sha256,
        stagedArtifactSha256 = RawDigest(stagedArtifact)
    }));
    Console.WriteLine($"PASS package validation manifest={receipt.PortabilityManifestDigest} staged={entry.Sha256}");
}

static void InspectRuntime(IReadOnlyDictionary<string, string> options)
{
    var dotnet = FullPath(Required(options, "--dotnet-executable"));
    var os = Required(options, "--os");
    var arch = Required(options, "--arch");
    var runtimeVersion = Required(options, "--runtime-version");
    var inventoryPath = FullPath(Required(options, "--inventory"));
    var reportPath = FullPath(Required(options, "--report"));
    var expected = options.TryGetValue("--expected-runtime-closure-digest", out var expectedValue) ? expectedValue : null;
    if (File.Exists(inventoryPath) || Directory.Exists(inventoryPath)) Fail("runtime inventory path already exists");
    if (File.Exists(reportPath) || Directory.Exists(reportPath)) Fail("environment report path already exists");

    try
    {
        var runtimes = RunEnvironment(dotnet, "--list-runtimes");
        if (!runtimes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => line.StartsWith($"Microsoft.NETCore.App {runtimeVersion} [", StringComparison.Ordinal)))
            throw new PortabilityContractException("EnvironmentUnavailable", "$/runtimeVersion", new { reason = "RuntimeVersionUnavailable", runtimeVersion });
        var receipt = DotNetRuntimeClosure.Capture(dotnet, os, arch, runtimeVersion, expected);
        Directory.CreateDirectory(Path.GetDirectoryName(inventoryPath) ?? throw new InvalidOperationException("runtime inventory path has no directory"));
        File.WriteAllBytes(inventoryPath, receipt.InventoryBytes);
        WriteEnvironmentReport(reportPath, new
        {
            schemaVersion = "strogo.environment-report.v0.1",
            status = "Passed",
            reasonCode = "None",
            profileId = PortabilityVersions.DotNetProfile,
            os,
            arch,
            runtimeVendor = "Microsoft",
            runtimeVersion,
            runtimeClosureDigest = receipt.RuntimeClosureDigest,
            launcherDigest = receipt.LauncherDigest,
            harnessDigest = RawDigest(typeof(Program).Assembly.Location),
            runtimeFiles = receipt.Files.Length.ToString(CultureInfo.InvariantCulture)
        });
        Console.WriteLine($"PASS dotnet runtime closure os={os} arch={arch} files={receipt.Files.Length} digest={receipt.RuntimeClosureDigest}");
    }
    catch (PortabilityContractException exception) when (exception.Code == "EnvironmentUnavailable")
    {
        WriteEnvironmentReport(reportPath, new
        {
            schemaVersion = "strogo.environment-report.v0.1",
            status = "Unavailable",
            reasonCode = exception.Code,
            profileId = PortabilityVersions.DotNetProfile,
            os,
            arch,
            runtimeVendor = "Microsoft",
            runtimeVersion,
            locus = exception.Locus,
            details = exception.Details,
            harnessDigest = RawDigest(typeof(Program).Assembly.Location)
        });
        throw;
    }
}

static void WriteEnvironmentReport(string path, object report)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("environment report path has no directory"));
    File.WriteAllBytes(path, CanonicalJson.Encode(report));
}

static void BuildMatrixCheck(IReadOnlyDictionary<string, string> options)
{
    var profileId = Required(options, "--profile-id");
    var output = FullPath(Required(options, "--output"));
    if (File.Exists(output) || Directory.Exists(output)) Fail("matrix output path already exists");
    var rows = new List<PortabilityPlatformStatus>();
    foreach (var option in new[] { "--linux-report", "--windows-report" })
        if (options.TryGetValue(option, out var path)) rows.Add(ReadPlatformStatus(FullPath(path), profileId));
    var completed = PortabilityReportMatrix.Complete(profileId, rows);
    var reasons = completed.SelectMany(row => row.ReasonCodes).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    var matrixStatus = completed.All(row => row.Status == "Passed") ? "Passed" : "NotPassed";
    var identity = completed.FirstOrDefault(row => row.Status == "Passed");
    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? throw new InvalidOperationException("matrix output path has no directory"));
    File.WriteAllBytes(output, CanonicalJson.Encode(new
    {
        schemaVersion = "strogo.portability-matrix-check.v0.1",
        profileId,
        matrixStatus,
        portabilityManifestDigest = identity?.PortabilityManifestDigest,
        packageDigest = identity?.PackageDigest,
        artifactDigest = identity?.ArtifactDigest,
        reasonCodes = reasons,
        platforms = completed.Select(row => new
        {
            os = row.Os,
            arch = row.Arch,
            status = row.Status,
            reasonCodes = row.ReasonCodes,
            portabilityManifestDigest = row.PortabilityManifestDigest,
            packageDigest = row.PackageDigest,
            artifactDigest = row.ArtifactDigest,
            runtimeClosureDigest = row.RuntimeClosureDigest,
            synthesized = row.Synthesized
        }).ToArray()
    }));
    Console.WriteLine($"PASS portability matrix profile={profileId} status={matrixStatus} rows={completed.Length}");
}

static void BuildJitReceipt(IReadOnlyDictionary<string, string> options)
{
    var package = FullPath(Required(options, "--package"));
    var expectedManifestDigest = Required(options, "--expected-manifest-digest");
    var runtimeReport = FullPath(Required(options, "--runtime-report"));
    var consumerOutput = FullPath(Required(options, "--consumer-output"));
    var jitLog = FullPath(Required(options, "--jit-log"));
    var os = Required(options, "--os");
    var arch = Required(options, "--arch");
    var output = FullPath(Required(options, "--output"));
    RequireFile(runtimeReport);
    RequireFile(consumerOutput);
    RequireFile(jitLog);
    if (File.Exists(output) || Directory.Exists(output)) Fail("JIT receipt output path already exists");

    var plan = DotNetJitDiagnostics.Bind(package, expectedManifestDigest);
    var receipt = DotNetJitDiagnostics.Validate(
        plan,
        File.ReadAllBytes(runtimeReport),
        File.ReadAllBytes(consumerOutput),
        File.ReadAllBytes(jitLog),
        os,
        arch);
    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? throw new InvalidOperationException("JIT receipt path has no directory"));
    File.WriteAllBytes(output, receipt.Bytes);
    Console.WriteLine($"PASS dotnet JIT receipt os={os} events={receipt.CompilationEvents.Length} calls={receipt.Calls} process={receipt.ProcessId}");
}

static void BuildJitPlan(IReadOnlyDictionary<string, string> options)
{
    var package = FullPath(Required(options, "--package"));
    var expectedManifestDigest = Required(options, "--expected-manifest-digest");
    var output = FullPath(Required(options, "--output"));
    if (File.Exists(output) || Directory.Exists(output)) Fail("JIT plan output path already exists");
    var plan = DotNetJitDiagnostics.Bind(package, expectedManifestDigest);
    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? throw new InvalidOperationException("JIT plan path has no directory"));
    File.WriteAllBytes(output, DotNetJitDiagnostics.PlanBytes(plan));
    Console.WriteLine($"PASS dotnet JIT plan entry={plan.EntrySymbol} candidate={plan.CandidateSymbol} calls={DotNetJitDiagnostics.RequiredCalls}");
}

static PortabilityPlatformStatus ReadPlatformStatus(string path, string profileId)
{
    RequireFile(path);
    using var document = JsonDocument.Parse(File.ReadAllBytes(path));
    var root = document.RootElement;
    RequireValue(root, "schemaVersion", "strogo.dotnet-package-platform-run.v0.1");
    RequireValue(root, "profileId", profileId);
    var status = String(root, "status");
    var reasons = root.TryGetProperty("reasonCodes", out var reasonCodes)
        ? reasonCodes.EnumerateArray().Select(item => item.GetString() ?? throw new InvalidOperationException("reason code must be string")).ToArray()
        : Array.Empty<string>();
    return new PortabilityPlatformStatus(
        String(root, "os"),
        String(root, "arch"),
        status,
        reasons,
        portabilityManifestDigest: OptionalString(root, "portabilityManifestDigest"),
        packageDigest: OptionalString(root, "packageDigest"),
        artifactDigest: OptionalString(root, "artifactDigest"),
        runtimeClosureDigest: OptionalString(root, "runtimeClosureDigest"));
}

static Dictionary<string, string> ParseOptions(string[] input)
{
    if (input.Length % 2 != 0) Fail("options must be name/value pairs");
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < input.Length; index += 2)
        if (!input[index].StartsWith("--", StringComparison.Ordinal) || !result.TryAdd(input[index], input[index + 1])) Fail($"invalid or duplicate option: {input[index]}");
    return result;
}

static string Required(IReadOnlyDictionary<string, string> options, string name)
    => options.TryGetValue(name, out var value) && value.Length > 0 ? value : throw new InvalidOperationException($"missing option: {name}");

static string FullPath(string path) => Path.GetFullPath(path);
static void RequireFile(string path) { if (!File.Exists(path)) Fail($"file unavailable: {path}"); }
static void RequireDirectory(string path) { if (!Directory.Exists(path)) Fail($"directory unavailable: {path}"); }
static string RawDigest(string path) { RequireFile(path); return CanonicalJson.RawDigest(File.ReadAllBytes(path)); }
static string Length(string path) => new FileInfo(path).Length.ToString(CultureInfo.InvariantCulture);

static void RequireBytes(string path, byte[] expected, string description)
{
    RequireFile(path);
    if (!File.ReadAllBytes(path).SequenceEqual(expected)) Fail($"{description} differs from current canonical generation");
}

static void RequireValue(JsonElement element, string property, string expected)
{
    if (String(element, property) != expected) Fail($"{property} mismatch");
}

static void RequireBoolean(JsonElement element, string property, bool expected)
{
    if (!element.TryGetProperty(property, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || value.GetBoolean() != expected) Fail($"{property} mismatch");
}

static string String(JsonElement element, string property)
    => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new InvalidOperationException($"{property} must be string");

static string? OptionalString(JsonElement element, string property)
    => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

static InventoryFile Inventory(string logicalPath, string physicalPath, string role)
    => new(logicalPath, RawDigest(physicalPath), Length(physicalPath), role);

static InventoryFile InventoryBytes(string logicalPath, byte[] bytes, string role)
    => new(logicalPath, CanonicalJson.RawDigest(bytes), bytes.LongLength.ToString(CultureInfo.InvariantCulture), role);

static byte[] ProofInventory(IEnumerable<InventoryFile> files, IEnumerable<(string Id, string Value)> versions)
    => CanonicalJson.Encode(new
    {
        files = files.OrderBy(file => file.Path, StringComparer.Ordinal).Select(file => new { path = file.Path, sha256 = file.Sha256, length = file.Length, role = file.Role }).ToArray(),
        versions = versions.OrderBy(item => item.Id, StringComparer.Ordinal).Select(item => new { id = item.Id, value = item.Value }).ToArray()
    });

static byte[] ToolInventory(string profileId, IEnumerable<InventoryFile> files, IEnumerable<(string Component, string Version)> versions)
    => CanonicalJson.Encode(new
    {
        schemaVersion = "strogo.tool-inventory.v0.1",
        profileId,
        files = files.OrderBy(file => file.Path, StringComparer.Ordinal).Select(file => new { path = file.Path, sha256 = file.Sha256, length = file.Length, role = file.Role }).ToArray(),
        versions = versions.OrderBy(item => item.Component, StringComparer.Ordinal).Select(item => new { component = item.Component, version = item.Version }).ToArray()
    });

static string[] EnumerateClosedFiles(string root)
{
    var files = new List<string>();
    var pending = new Stack<string>();
    pending.Push(root);
    while (pending.Count > 0)
    {
        var directory = pending.Pop();
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) Fail($"Dafny closure contains a reparse point: {entry}");
            if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry); else files.Add(entry);
        }
    }
    return files.Order(StringComparer.Ordinal).ToArray();
}

static bool EqualTrees(string left, string right)
{
    var leftFiles = Directory.GetFiles(left, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(left, path).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal).ToArray();
    var rightFiles = Directory.GetFiles(right, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(right, path).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal).ToArray();
    return leftFiles.SequenceEqual(rightFiles, StringComparer.Ordinal) && leftFiles.All(relative => File.ReadAllBytes(Path.Combine(left, relative.Replace('/', Path.DirectorySeparatorChar))).SequenceEqual(File.ReadAllBytes(Path.Combine(right, relative.Replace('/', Path.DirectorySeparatorChar)))));
}

static string Run(string executable, string workingDirectory, params string[] arguments)
{
    var startInfo = new ProcessStartInfo(executable)
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"failed to start {executable}");
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) Fail($"{executable} failed: {error}");
    return output.Trim();
}

static string RunEnvironment(string executable, params string[] arguments)
{
    try
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo);
        if (process is null) throw new InvalidOperationException("runtime probe did not start");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new PortabilityContractException("EnvironmentUnavailable", "$/launcher", new { reason = "RuntimeProbeFailed", exitCode = process.ExitCode.ToString(CultureInfo.InvariantCulture), stderrDigest = CanonicalJson.RawDigest(Encoding.UTF8.GetBytes(error)) });
        return output.Trim();
    }
    catch (PortabilityContractException)
    {
        throw;
    }
    catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
    {
        throw new PortabilityContractException("EnvironmentUnavailable", "$/launcher", new { reason = "RuntimeProbeFailed", exception = exception.GetType().FullName });
    }
}

[System.Diagnostics.CodeAnalysis.DoesNotReturn]
static void Fail(string message) => throw new PackageHarnessException(message);

internal sealed record InventoryFile(string Path, string Sha256, string Length, string Role);
internal sealed class PackageHarnessException(string message) : Exception(message);
