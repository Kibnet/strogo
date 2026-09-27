using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Kernel.Core;
using Strogo.Notation;

namespace Strogo.Experiments;

public sealed record PilotPreparationOptions(
    string Directory,
    string FixtureDirectory,
    string PilotId,
    string CreatedAtUtc,
    string RepositoryCommit,
    string CliVersion,
    string DockerImageId,
    ImplementationIdentity ImplementationIdentity);

public sealed record PackageInventoryEntry(string Path, string Digest, long Bytes);
public sealed record PackageInventory(string Protocol, string PilotId, ImmutableArray<PackageInventoryEntry> Files, string InventoryDigest);
public sealed record ParityReview(string Protocol, string PilotId, ImmutableArray<string> SharedInputs, ImmutableArray<string> AllowedDifferences, string Status, string Digest);
public sealed record ForbiddenPromptInventory(
    string Protocol,
    string PilotId,
    ImmutableArray<string> ExactNeedles,
    ImmutableArray<string> NormalizedNeedles,
    string Digest);

public static class LivePilotPreparation
{
    private static readonly string[] RequiredFixtures =
    [
        "TASK.md", "response.schema.json", "strogo-arm.md", "csharp-arm.md", "starter.strogo", "Candidate.cs",
        "csharp-runner/Runner.csproj", "csharp-runner/Program.cs"
    ];

    public static PilotManifest Prepare(PilotPreparationOptions options)
    {
        ValidateIdentifier(options.PilotId, "PilotIdInvalid");
        if (options.RepositoryCommit.Length is not (40 or 64) || !options.RepositoryCommit.All(char.IsAsciiHexDigit) || string.IsNullOrWhiteSpace(options.CliVersion) || !options.DockerImageId.StartsWith("sha256:", StringComparison.Ordinal))
            throw LivePilotJson.Failure("PreparationIdentityMissing");
        string[] implementationDigests =
        [
            options.ImplementationIdentity.SourceClosureDigest, options.ImplementationIdentity.ExperimentCliAssemblyDigest,
            options.ImplementationIdentity.ExperimentsAssemblyDigest, options.ImplementationIdentity.NotationAssemblyDigest,
            options.ImplementationIdentity.CoreAssemblyDigest, options.ImplementationIdentity.DotNetExecutableDigest,
            options.ImplementationIdentity.CodexNodeDigest, options.ImplementationIdentity.CodexEntryDigest
        ];
        if (implementationDigests.Any(value => value.Length != 64 || !value.All(char.IsAsciiHexDigit)) || options.ImplementationIdentity.DotNetSdkVersion != "10.0.400")
            throw LivePilotJson.Failure("ImplementationIdentityMissing");
        if (!DateTimeOffset.TryParse(options.CreatedAtUtc, out var created)) throw LivePilotJson.Failure("CreatedAtInvalid");

        string destination = Path.GetFullPath(options.Directory);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw LivePilotJson.Failure("DestinationNotEmpty");
        string fixtureRoot = Path.GetFullPath(options.FixtureDirectory);
        ValidateFrozenFixtures(fixtureRoot);

        byte[] task = ReadFixture(fixtureRoot, "TASK.md");
        byte[] schema = ReadFixture(fixtureRoot, "response.schema.json");
        ValidateResponseSchema(schema);
        string taskDigest = CanonicalJson.RawDigest(task);
        string schemaDigest = CanonicalJson.RawDigest(schema);
        var runtime = new RequestedRuntime(
            LivePilotIdentity.RequestedModel,
            LivePilotIdentity.RequestedReasoning,
            options.CliVersion,
            "read-only",
            true,
            true,
            true,
            false,
            LivePilotIdentity.TimeoutSeconds,
            LivePilotIdentity.OutputCapBytes);
        var docker = new DockerIdentity(
            LivePilotIdentity.DockerPlatform,
            LivePilotIdentity.DockerReference,
            LivePilotIdentity.DockerManifestListDigest,
            LivePilotIdentity.DockerChildDigest,
            options.DockerImageId);

        Directory.CreateDirectory(destination);
        string packagesRoot = Path.Combine(destination, "packages");
        Directory.CreateDirectory(packagesRoot);
        var runRefs = ImmutableArray.CreateBuilder<PilotRunRef>();
        var inventory = ImmutableArray.CreateBuilder<PackageInventoryEntry>();

        for (int position = 0; position < LivePilotIdentity.RunOrder.Length; position++)
        {
            string runId = LivePilotIdentity.RunOrder[position];
            bool strogo = runId.StartsWith('S');
            string arm = strogo ? "strogo-notation" : "csharp-dotnet";
            string armFile = strogo ? "strogo-arm.md" : "csharp-arm.md";
            string starterFile = strogo ? "starter.strogo" : "Candidate.cs";
            string target = strogo ? "candidate.strogo" : "Candidate.cs";
            byte[] armBytes = ReadFixture(fixtureRoot, armFile);
            byte[] starterBytes = ReadFixture(fixtureRoot, starterFile);
            if (strogo && NotationCompiler.Compile(starterBytes).Accepted) throw LivePilotJson.Failure("StarterUnexpectedlyValid");
            byte[] prompt = BuildPrompt(task, armBytes, starterBytes, target);
            ScanPrompt(prompt);
            string packageDigest = LivePilotJson.Digest(
                ("TASK.md", task),
                ("arm.md", armBytes),
                ("starter.txt", starterBytes),
                ("prompt.txt", prompt),
                ("response.schema.json", schema));
            var invocation = LivePilotProvenance.CreateInvocation(destination, runId, options.ImplementationIdentity);
            var skeleton = new RunManifest(
                LivePilotIdentity.Protocol,
                options.PilotId,
                runId,
                arm,
                position,
                $"{options.PilotId}-{runId}-attempt-01",
                target,
                taskDigest,
                schemaDigest,
                CanonicalJson.RawDigest(armBytes),
                CanonicalJson.RawDigest(starterBytes),
                CanonicalJson.RawDigest(prompt),
                packageDigest,
                runtime,
                docker,
                invocation,
                string.Empty);
            string runDigest = CanonicalJson.RawDigest(CanonicalJson.Encode(skeleton));
            var manifest = skeleton with { ManifestDigest = runDigest };
            string packageDirectory = Path.Combine(packagesRoot, runId);
            Directory.CreateDirectory(packageDirectory);
            Write(packageDirectory, "TASK.md", task, inventory, destination);
            Write(packageDirectory, "arm.md", armBytes, inventory, destination);
            Write(packageDirectory, "starter.txt", starterBytes, inventory, destination);
            Write(packageDirectory, "prompt.txt", prompt, inventory, destination);
            Write(packageDirectory, "response.schema.json", schema, inventory, destination);
            string runManifestPath = Path.Combine(packageDirectory, "run-manifest.json");
            LivePilotJson.WriteCanonical(runManifestPath, manifest);
            AddInventory(runManifestPath, destination, inventory);
            runRefs.Add(new(runId, arm, position, runDigest, packageDigest));
        }

        var provisionalPilot = new PilotManifest(
            LivePilotIdentity.Protocol,
            options.PilotId,
            created.ToUniversalTime().ToString("O"),
            created.ToUniversalTime().AddDays(90).ToString("O"),
            options.RepositoryCommit,
            LivePilotIdentity.ClaimBoundary,
            LivePilotIdentity.AssuranceLevel,
            LivePilotIdentity.RunOrder,
            taskDigest,
            schemaDigest,
            LivePilotCorpus.HiddenDigest,
            runtime,
            docker,
            options.ImplementationIdentity,
            string.Empty,
            string.Empty,
            string.Empty,
            runRefs.ToImmutable(),
            string.Empty);

        string forbiddenPath = Path.Combine(destination, "forbidden-prompt-inventory.json");
        var forbidden = LivePilotLeakage.CreateInventory(provisionalPilot);
        LivePilotJson.WriteCanonical(forbiddenPath, forbidden);
        AddInventory(forbiddenPath, destination, inventory);

        var parity = CreateParityReview(options.PilotId);
        string parityPath = Path.Combine(destination, "parity-review.json");
        LivePilotJson.WriteCanonical(parityPath, parity);
        AddInventory(parityPath, destination, inventory);

        var orderedInventory = inventory.OrderBy(x => x.Path, StringComparer.Ordinal).ToImmutableArray();
        var inventorySkeleton = new PackageInventory(LivePilotIdentity.Protocol, options.PilotId, orderedInventory, string.Empty);
        var inventoryRecord = inventorySkeleton with { InventoryDigest = CanonicalJson.RawDigest(CanonicalJson.Encode(inventorySkeleton)) };
        LivePilotJson.WriteCanonical(Path.Combine(destination, "inventory.json"), inventoryRecord);

        var pilotSkeleton = provisionalPilot with
        {
            ForbiddenPromptInventoryDigest = forbidden.Digest,
            ParityReviewDigest = parity.Digest,
            PackageInventoryDigest = inventoryRecord.InventoryDigest
        };
        string pilotDigest = CanonicalJson.RawDigest(CanonicalJson.Encode(pilotSkeleton));
        var pilot = pilotSkeleton with { ManifestDigest = pilotDigest };
        LivePilotJson.WriteCanonical(Path.Combine(destination, "pilot-manifest.json"), pilot);
        return pilot;
    }

    public static PilotManifest LoadAndVerify(string directory)
    {
        string root = Path.GetFullPath(directory);
        var pilot = LivePilotJson.ReadCanonical<PilotManifest>(Path.Combine(root, "pilot-manifest.json"));
        if (pilot.Protocol != LivePilotIdentity.Protocol || pilot.ClaimBoundary != LivePilotIdentity.ClaimBoundary || pilot.AssuranceLevel != LivePilotIdentity.AssuranceLevel)
            throw LivePilotJson.Failure("PilotIdentityMismatch");
        string digest = CanonicalJson.RawDigest(CanonicalJson.Encode(pilot with { ManifestDigest = string.Empty }));
        if (digest != pilot.ManifestDigest) throw LivePilotJson.Failure("PilotManifestDigestMismatch");
        if (!pilot.RunOrder.SequenceEqual(LivePilotIdentity.RunOrder) || pilot.Runs.Length != LivePilotIdentity.RunOrder.Length)
            throw LivePilotJson.Failure("RunOrderMismatch");
        if (pilot.HiddenCorpusDigest != LivePilotCorpus.HiddenDigest) throw LivePilotJson.Failure("HiddenCorpusDigestMismatch");
        VerifyPreparationArtifacts(root, pilot);
        foreach (var reference in pilot.Runs)
        {
            string path = Path.Combine(root, "packages", reference.RunId, "run-manifest.json");
            var run = LoadAndVerifyRun(path, pilot);
            if (run.ManifestDigest != reference.RunManifestDigest || run.PackageDigest != reference.PackageDigest)
                throw LivePilotJson.Failure("RunReferenceMismatch");
        }
        return pilot;
    }

    private static ParityReview CreateParityReview(string pilotId)
    {
        var skeleton = new ParityReview(
            LivePilotIdentity.Protocol,
            pilotId,
            ["TASK.md", "response.schema.json", "requestedRuntime", "resourceLimits", "responseContract", "publicExamples"],
            ["arm", "targetFilename", "syntaxOrInterface", "invalidStarter", "trustedAdapter"],
            "ReviewedBeforeExposure",
            string.Empty);
        return skeleton with { Digest = CanonicalJson.RawDigest(CanonicalJson.Encode(skeleton)) };
    }

    private static void VerifyPreparationArtifacts(string root, PilotManifest pilot)
    {
        var forbidden = LivePilotLeakage.LoadAndVerifyInventory(root, pilot);
        if (forbidden.Digest != pilot.ForbiddenPromptInventoryDigest) throw LivePilotJson.Failure("ForbiddenPromptInventoryMismatch");

        string parityPath = Path.Combine(root, "parity-review.json");
        if (!File.Exists(parityPath)) throw LivePilotJson.Failure("ParityReviewMissing");
        var parity = LivePilotJson.ReadCanonical<ParityReview>(parityPath);
        var expectedParity = CreateParityReview(pilot.PilotId);
        if (parity.Digest != pilot.ParityReviewDigest || !CanonicalJson.Encode(parity).AsSpan().SequenceEqual(CanonicalJson.Encode(expectedParity)))
            throw LivePilotJson.Failure("ParityReviewMismatch");

        string inventoryPath = Path.Combine(root, "inventory.json");
        if (!File.Exists(inventoryPath)) throw LivePilotJson.Failure("PreparationInventoryMissing");
        var inventory = LivePilotJson.ReadCanonical<PackageInventory>(inventoryPath);
        string inventoryDigest = CanonicalJson.RawDigest(CanonicalJson.Encode(inventory with { InventoryDigest = string.Empty }));
        if (inventory.Protocol != pilot.Protocol || inventory.PilotId != pilot.PilotId || inventory.InventoryDigest != inventoryDigest || inventory.InventoryDigest != pilot.PackageInventoryDigest)
            throw LivePilotJson.Failure("PreparationInventoryMismatch");
        var actual = Directory.EnumerateFiles(Path.Combine(root, "packages"), "*", SearchOption.AllDirectories)
            .Append(Path.Combine(root, "forbidden-prompt-inventory.json"))
            .Append(parityPath)
            .OrderBy(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal)
            .Select(path => new PackageInventoryEntry(Path.GetRelativePath(root, path).Replace('\\', '/'), CanonicalJson.RawDigest(File.ReadAllBytes(path)), new FileInfo(path).Length))
            .ToImmutableArray();
        if (!actual.SequenceEqual(inventory.Files)) throw LivePilotJson.Failure("PreparationInventoryMismatch");
    }

    public static RunManifest LoadAndVerifyRun(string path, PilotManifest pilot)
    {
        var run = LivePilotJson.ReadCanonical<RunManifest>(path);
        string digest = CanonicalJson.RawDigest(CanonicalJson.Encode(run with { ManifestDigest = string.Empty }));
        if (digest != run.ManifestDigest) throw LivePilotJson.Failure("RunManifestDigestMismatch");
        if (run.Protocol != pilot.Protocol || run.PilotId != pilot.PilotId || run.RequestedRuntime != pilot.RequestedRuntime || run.DockerIdentity != pilot.DockerIdentity)
            throw LivePilotJson.Failure("RunManifestIdentityMismatch");
        string pilotRoot = Directory.GetParent(Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName)!.FullName;
        LivePilotProvenance.VerifyInvocation(run.InvocationIdentity, pilotRoot, run.RunId, pilot.ImplementationIdentity);
        string packageDirectory = Path.GetDirectoryName(path)!;
        byte[] task = File.ReadAllBytes(Path.Combine(packageDirectory, "TASK.md"));
        byte[] arm = File.ReadAllBytes(Path.Combine(packageDirectory, "arm.md"));
        byte[] starter = File.ReadAllBytes(Path.Combine(packageDirectory, "starter.txt"));
        byte[] prompt = File.ReadAllBytes(Path.Combine(packageDirectory, "prompt.txt"));
        byte[] schema = File.ReadAllBytes(Path.Combine(packageDirectory, "response.schema.json"));
        string packageDigest = LivePilotJson.Digest(("TASK.md", task), ("arm.md", arm), ("starter.txt", starter), ("prompt.txt", prompt), ("response.schema.json", schema));
        if (packageDigest != run.PackageDigest || CanonicalJson.RawDigest(prompt) != run.PromptDigest || CanonicalJson.RawDigest(task) != run.TaskDigest || CanonicalJson.RawDigest(schema) != run.ResponseSchemaDigest)
            throw LivePilotJson.Failure("PackageDigestMismatch");
        return run;
    }

    public static void ValidateFrozenFixtures(string fixtureRoot)
    {
        foreach (string fixture in RequiredFixtures)
        {
            string path = Path.Combine(fixtureRoot, fixture.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) throw LivePilotJson.Failure("FixtureMissing");
            if (!LivePilotIdentity.FrozenFixtureDigests.TryGetValue(fixture, out string? expected) || CanonicalJson.RawDigest(File.ReadAllBytes(path)) != expected)
                throw LivePilotJson.Failure("FixtureDigestMismatch");
        }
    }

    private static byte[] BuildPrompt(byte[] task, byte[] arm, byte[] starter, string target)
    {
        using var stream = new MemoryStream();
        void Section(string heading, byte[] bytes)
        {
            byte[] prefix = Encoding.UTF8.GetBytes($"## {heading}\n\n");
            stream.Write(prefix);
            stream.Write(bytes);
            stream.Write(Encoding.UTF8.GetBytes("\n\n"));
        }
        stream.Write(Encoding.UTF8.GetBytes("You are producing one source artifact for a frozen benchmark. Do not call tools, read files, browse, execute code, or ask for hidden evaluator feedback. Return exactly one JSON object that satisfies the supplied response schema.\n\n"));
        Section("Shared task", task);
        Section("Arm contract", arm);
        Section("Intentionally invalid starter", starter);
        stream.Write(Encoding.UTF8.GetBytes($"## Response contract\n\nUse status Candidate with candidateSource containing the complete contents of {target} and an empty question. Use NeedsHuman only when the task is insufficient, with empty candidateSource and a non-empty question. Use Refused with both strings empty. Do not use Markdown fences.\n"));
        return stream.ToArray();
    }

    private static void ScanPrompt(byte[] prompt)
    {
        string text = new UTF8Encoding(false, true).GetString(prompt);
        if (text.Length > 64 * 1024) throw LivePilotJson.Failure("PromptLimitExceeded");
        string[] forbidden = ["CalibrationCorpus", "ReserveOracle", "e09-reserve.v0.1", "docs/evidence/e09", "R01-baseline"];
        if (forbidden.Any(text.Contains)) throw LivePilotJson.Failure("TrustedContentLeak");
    }

    private static void ValidateResponseSchema(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        if (!root.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.False)
            throw LivePilotJson.Failure("ResponseSchemaNotClosed");
        if (!root.TryGetProperty("required", out var required) || !required.EnumerateArray().Select(x => x.GetString()).SequenceEqual(["status", "candidateSource", "question"]))
            throw LivePilotJson.Failure("ResponseSchemaRequiredMismatch");
    }

    private static byte[] ReadFixture(string root, string relative)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        _ = new UTF8Encoding(false, true).GetString(bytes);
        return bytes;
    }

    private static void Write(string directory, string name, byte[] bytes, ImmutableArray<PackageInventoryEntry>.Builder inventory, string root)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, bytes);
        AddInventory(path, root, inventory);
    }

    private static void AddInventory(string path, string root, ImmutableArray<PackageInventoryEntry>.Builder inventory)
    {
        byte[] bytes = File.ReadAllBytes(path);
        inventory.Add(new(Path.GetRelativePath(root, path).Replace('\\', '/'), CanonicalJson.RawDigest(bytes), bytes.LongLength));
    }

    private static void ValidateIdentifier(string value, string code)
    {
        if (value.Length is < 1 or > 64 || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) throw LivePilotJson.Failure(code);
    }
}
