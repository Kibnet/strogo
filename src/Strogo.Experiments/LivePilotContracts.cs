using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kernel.Core;

namespace Strogo.Experiments;

public static class LivePilotIdentity
{
    public const string Protocol = "strogo.e10.live-paired-pilot.v0.1";
    public const string ResponseSchema = "strogo.e10.agent-response.v0.1";
    public const string EvaluationSchema = "strogo.e10.candidate-evaluation.v0.1";
    public const string ReportSchema = "strogo.e10.pilot-report.v0.1";
    public const string ClaimBoundary = "PilotOnlyNoG05";
    public const string AssuranceLevel = "FiniteCorpusEvaluation";
    public const string RequestedModel = "gpt-6-astra";
    public const string RequestedReasoning = "medium";
    public const string DockerPlatform = "linux/amd64";
    public const string DockerReference = "mcr.microsoft.com/dotnet/sdk@sha256:1aabdb4843de1c426d3676bf1220bc040e540f82a765320b3eb2c693e8d0a7dd";
    public const string DockerManifestListDigest = "sha256:4beef5b8919dcaa2dc924233bd069257e883cc7a061e09088a97d152d6a48510";
    public const string DockerChildDigest = "sha256:1aabdb4843de1c426d3676bf1220bc040e540f82a765320b3eb2c693e8d0a7dd";
    public const long OutputCapBytes = 16L * 1024 * 1024;
    public const int TimeoutSeconds = 20 * 60;
    public static readonly ImmutableArray<string> RunOrder = ["S1", "C1", "C2", "S2"];
    public static readonly ImmutableDictionary<string, string> FrozenFixtureDigests = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["TASK.md"] = "db05bfbd07f98a5e147dcdeebad721e879031ddbfd76bfa2e8c168258f7587fa",
        ["response.schema.json"] = "6d9a94c0b4815bb872afedbb5229b97eeecc5039641843d262eb608da0ecde0a",
        ["strogo-arm.md"] = "c587f75cbfb80530cb70927a1bef25fdae2f473615238c08c4516c085c0e29d5",
        ["csharp-arm.md"] = "ed819a01b10ac851cc1594ea610a548ebd4288cbf6d0956adcaa775d13143566",
        ["starter.strogo"] = "3f8a539ffcbaa43676092d98f5b8438ab6fbf28de8cf2dc1c17e7438e9e64992",
        ["Candidate.cs"] = "2b7e964927217997e838eb7c6979fcd7067f915bec0f35890e2e00131eb5f845",
        ["csharp-runner/Runner.csproj"] = "90bfca2f69a3b8433b8b3bf4ece812ed23a716e935495f449fae254e84e7ff64",
        ["csharp-runner/Program.cs"] = "e7dfe7c6138503f8f574c0059bf9d95a40ef18a75072f2d811874d91cc101451"
    }.ToImmutableDictionary(StringComparer.Ordinal);
}

public sealed record RequestedRuntime(
    string Model,
    string Reasoning,
    string CliVersion,
    string Sandbox,
    bool Ephemeral,
    bool IgnoreUserConfig,
    bool IgnoreRules,
    bool InheritShellEnvironment,
    int TimeoutSeconds,
    long OutputCapBytes);

public sealed record DockerIdentity(
    string Platform,
    string ImageReference,
    string ManifestListDigest,
    string ChildDigest,
    string ResolvedImageId);

public sealed record ImplementationIdentity(
    string SourceClosureDigest,
    string ExperimentCliAssemblyDigest,
    string ExperimentsAssemblyDigest,
    string NotationAssemblyDigest,
    string CoreAssemblyDigest,
    string DotNetExecutableDigest,
    string DotNetSdkVersion,
    string CodexNodeDigest,
    string CodexEntryDigest);

public sealed record InvocationIdentity(
    string CodexNodeDigest,
    string CodexEntryDigest,
    ImmutableArray<string> Arguments,
    string WorkingDirectory,
    ImmutableArray<string> EnvironmentVariables,
    string StandardInput,
    string Digest);

public sealed record PilotRunRef(
    string RunId,
    string Arm,
    int Position,
    string RunManifestDigest,
    string PackageDigest);

public sealed record PilotManifest(
    string Protocol,
    string PilotId,
    string CreatedAtUtc,
    string RetentionUntilUtc,
    string RepositoryCommit,
    string ClaimBoundary,
    string AssuranceLevel,
    ImmutableArray<string> RunOrder,
    string TaskDigest,
    string ResponseSchemaDigest,
    string HiddenCorpusDigest,
    RequestedRuntime RequestedRuntime,
    DockerIdentity DockerIdentity,
    ImplementationIdentity ImplementationIdentity,
    string ForbiddenPromptInventoryDigest,
    string ParityReviewDigest,
    string PackageInventoryDigest,
    ImmutableArray<PilotRunRef> Runs,
    string ManifestDigest);

public sealed record RunManifest(
    string Protocol,
    string PilotId,
    string RunId,
    string Arm,
    int Position,
    string ProcessAttemptId,
    string TargetFilename,
    string TaskDigest,
    string ResponseSchemaDigest,
    string ArmInstructionDigest,
    string StarterDigest,
    string PromptDigest,
    string PackageDigest,
    RequestedRuntime RequestedRuntime,
    DockerIdentity DockerIdentity,
    InvocationIdentity InvocationIdentity,
    string ManifestDigest);

public sealed record ProcessReceipt(
    string Protocol,
    string PilotId,
    string RunId,
    string ProcessAttemptId,
    InvocationIdentity InvocationIdentity,
    RequestedRuntime RequestedRuntime,
    string PromptDigest,
    string ResponseSchemaDigest,
    string StartedAtUtc,
    string EndedAtUtc,
    long WallMilliseconds,
    int ExitCode,
    bool TimedOut,
    bool OutputLimitExceeded,
    string StdoutDigest,
    string StderrDigest,
    string ResponseDigest,
    string PromptInputDigest,
    string EnvironmentDigest);

public sealed record AclReceipt(string Protocol, string PilotId, bool InheritanceDisabled, string CurrentUserSid, string SystemSid, bool Verified);
public sealed record RetentionReceipt(string Protocol, string PilotId, string RetentionUntilUtc, string CleanupMode);
public sealed record NotRunReceipt(string Protocol, string PilotId, string RunId, string PriorRunId, string Status, string Code);
public sealed record EnvironmentValueDigest(string Name, bool Present, string ValueDigest);
public sealed record EnvironmentReceipt(string Protocol, string PilotId, ImmutableArray<string> Variables, ImmutableArray<EnvironmentValueDigest> Values, string Digest);

public sealed record AgentResponse(string Status, string CandidateSource, string Question);

public sealed record RunMetrics(
    long WallMilliseconds,
    long AgentProcessMilliseconds,
    long EvaluationMilliseconds,
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens,
    long? ReasoningTokens,
    int ModelTurns,
    int ToolCalls,
    int CandidateWrites,
    int PublicValidatorCalls,
    int AttemptCount,
    long? HumanActiveSeconds,
    long? HumanWaitSeconds,
    string? BillableCost,
    string? Currency,
    string CostAvailability);

public sealed record CandidateStage(string Stage, string Status, string? Code);

public sealed record CandidateEvaluation(
    string SchemaVersion,
    string Protocol,
    string PilotId,
    string RunId,
    string Arm,
    string Status,
    string? Code,
    string CandidateDigest,
    string AssuranceLevel,
    int PublicVectors,
    int HiddenVectors,
    int SeededVectors,
    ImmutableArray<CandidateStage> Stages,
    RunMetrics Metrics,
    string AdapterEvidenceDigest,
    string RawEvidenceDigest,
    string EvaluationDigest);

public sealed record EvidenceFile(string Path, string Digest, long Bytes);
public sealed record EvidenceInventory(string Protocol, string PilotId, string RunId, ImmutableArray<EvidenceFile> Files, string InventoryDigest);
public sealed record DockerReceipt(
    string Protocol,
    string PilotId,
    string RunId,
    DockerIdentity DockerIdentity,
    ImmutableArray<string> Arguments,
    string InputDigest,
    string StartedAtUtc,
    string EndedAtUtc,
    long WallMilliseconds,
    int ExitCode,
    bool TimedOut,
    bool OutputLimitExceeded,
    string StdoutDigest,
    string StderrDigest,
    string OutputsDigest,
    string ReceiptDigest);

public sealed record PairDelta(string PairId, string StrogoRunId, string CSharpRunId, bool Comparable, long? WallMillisecondsDelta, long? EvaluationMillisecondsDelta);
public sealed record ArmSummary(string Arm, int Samples, int BenchmarkAccepted, long MedianWallMilliseconds, long MedianEvaluationMilliseconds);

public sealed record PilotReport(
    string SchemaVersion,
    string Protocol,
    string PilotId,
    string Status,
    string ClaimBoundary,
    string EffectiveModelEvidence,
    string RepositoryCommit,
    string ManifestDigest,
    RequestedRuntime RequestedRuntime,
    DockerIdentity DockerIdentity,
    ImplementationIdentity ImplementationIdentity,
    string PromptInputSharedDigest,
    ImmutableArray<string> RunOrder,
    ImmutableArray<CandidateEvaluation> Runs,
    ImmutableArray<ArmSummary> Arms,
    ImmutableArray<PairDelta> Pairs,
    ImmutableArray<string> Limitations,
    string ReportDigest);

public sealed record PilotVector(string Id, long ResourceAvailable, long RequestedQuantity, string Set);

public static class LivePilotCorpus
{
    public static ImmutableArray<PilotVector> Public { get; } =
    [
        new("public-accepted", 10, 3, "public"),
        new("public-insufficient", 3, 5, "public"),
        new("public-zero-available", 0, 1, "public"),
        new("public-int64-max", long.MaxValue, 1, "public")
    ];

    public static ImmutableArray<PilotVector> Hidden { get; } = BuildHidden();
    public static string HiddenDigest { get; } = CanonicalJson.RawDigest(CanonicalJson.Encode(Hidden));

    private static ImmutableArray<PilotVector> BuildHidden()
    {
        var rows = ImmutableArray.CreateBuilder<PilotVector>();
        rows.Add(new("hidden-boundary-equal-zero", 0, 0 + 1, "boundary"));
        rows.Add(new("hidden-boundary-equal-one", 1, 1, "boundary"));
        rows.Add(new("hidden-boundary-one-short", 1, 2, "boundary"));
        rows.Add(new("hidden-boundary-max-equal", long.MaxValue, long.MaxValue, "boundary"));
        rows.Add(new("hidden-boundary-max-short", long.MaxValue - 1, long.MaxValue, "boundary"));
        rows.Add(new("hidden-boundary-max-half", long.MaxValue, long.MaxValue / 2, "boundary"));

        ulong state = 0x4d595df4d0f33173UL;
        for (int i = 0; i < 32; i++)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            long available = (long)(state & 0x7fff_ffff_ffff_ffffUL);
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            long quantity = (long)(state & 0x7fff_ffff_ffff_ffffUL);
            if (quantity == 0) quantity = 1;
            rows.Add(new($"hidden-seeded-{i.ToString("D2", CultureInfo.InvariantCulture)}", available, quantity, "seeded"));
        }
        return rows.ToImmutable();
    }
}

public static class LivePilotJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(CanonicalJson.SerializerOptions)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static T ReadCanonical<T>(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > LivePilotIdentity.OutputCapBytes) throw Failure("ArtifactLimitExceeded");
        string json = new UTF8Encoding(false, true).GetString(bytes);
        using var _ = CanonicalJson.ParseStrict(json);
        T value;
        try { value = JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? throw Failure("ArtifactNull"); }
        catch (JsonException) { throw Failure("ArtifactSchemaInvalid"); }
        byte[] canonical = CanonicalJson.Encode(value);
        if (!bytes.AsSpan().SequenceEqual(canonical)) throw Failure("ArtifactNotCanonical");
        return value;
    }

    public static void WriteCanonical(string path, object value, bool overwrite = false)
    {
        if (!overwrite && File.Exists(path)) throw Failure("ArtifactExists");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, CanonicalJson.Encode(value));
    }

    public static string Digest(params (string Name, byte[] Bytes)[] parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var part in parts.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(part.Name));
            hash.AppendData([0]);
            hash.AppendData(Encoding.UTF8.GetBytes(part.Bytes.Length.ToString(CultureInfo.InvariantCulture)));
            hash.AppendData([0]);
            hash.AppendData(part.Bytes);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static KernelException Failure(string code) => new(KernelError.Create("e10", code));
}

public sealed record RunEventSummary(
    bool TurnStarted,
    bool TurnCompleted,
    int ModelTurns,
    int AgentMessages,
    int ToolCalls,
    int CandidateWrites,
    int ErrorEvents,
    int UnknownEvents,
    int SequenceViolations,
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens,
    long? ReasoningTokens,
    string? EffectiveModel,
    string? EffectiveBackend);

public static class CodexEventParser
{
    private static readonly HashSet<string> AllowedEventTypes = new(StringComparer.Ordinal)
    {
        "thread.started", "turn.started", "item.started", "item.updated", "item.completed", "turn.completed", "turn.failed", "error"
    };
    private static readonly HashSet<string> AllowedItemTypes = new(StringComparer.Ordinal)
    {
        "agent_message", "reasoning", "todo_list"
    };

    public static RunEventSummary Parse(string path)
    {
        bool turnStarted = false, turnCompleted = false;
        int turns = 0, agentMessages = 0, tools = 0, writes = 0, errors = 0, unknown = 0, sequenceViolations = 0, phase = 0;
        long? input = null, output = null, cached = null, reasoning = null;
        string? model = null, backend = null;
        var startedItems = new HashSet<string>(StringComparer.Ordinal);
        var completedItems = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(path, new UTF8Encoding(false, true)))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { throw LivePilotJson.Failure("RawEventInvalidJson"); }
            using (doc)
            {
                var root = doc.RootElement;
                string type = GetString(root, "type") ?? string.Empty;
                bool isItemEvent = type is "item.started" or "item.updated" or "item.completed";
                bool hasItem = root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object;
                string? itemId = hasItem ? GetString(item, "id") : null;
                if (!AllowedEventTypes.Contains(type)) unknown++;
                switch (type)
                {
                    case "thread.started": if (phase == 0 && !string.IsNullOrWhiteSpace(GetString(root, "thread_id"))) phase = 1; else sequenceViolations++; break;
                    case "turn.started": if (phase == 1) { phase = 2; turnStarted = true; turns++; } else sequenceViolations++; break;
                    case "item.started" or "item.updated" or "item.completed": if (phase != 2 || !hasItem || string.IsNullOrWhiteSpace(itemId)) sequenceViolations++; break;
                    case "turn.completed": if (phase == 2) { phase = 3; turnCompleted = true; } else sequenceViolations++; break;
                    case "turn.failed": if (phase == 2) phase = 3; else sequenceViolations++; break;
                }
                if (type is "turn.failed" or "error") errors++;
                if (isItemEvent && hasItem && itemId is not null)
                {
                    if (type == "item.started" && (!startedItems.Add(itemId) || completedItems.Contains(itemId))) sequenceViolations++;
                    if (type == "item.updated" && !startedItems.Contains(itemId)) sequenceViolations++;
                    if (type == "item.completed" && !completedItems.Add(itemId)) sequenceViolations++;
                }
                if (hasItem)
                {
                    string itemType = GetString(item, "type") ?? string.Empty;
                    if (type == "item.completed" && itemType == "agent_message") agentMessages++;
                    if (!AllowedItemTypes.Contains(itemType))
                    {
                        tools++;
                        if (itemType == "file_change") writes++;
                    }
                }
                if (root.TryGetProperty("usage", out var usage))
                {
                    input = ReadLong(usage, "input_tokens") ?? input;
                    output = ReadLong(usage, "output_tokens") ?? output;
                    cached = ReadLong(usage, "cached_input_tokens") ?? cached;
                    reasoning = ReadLong(usage, "reasoning_tokens") ?? reasoning;
                }
                model = GetString(root, "effective_model") ?? GetString(root, "model") ?? model;
                backend = GetString(root, "backend") ?? backend;
            }
        }
        return new(turnStarted, turnCompleted, turns, agentMessages, tools, writes, errors, unknown, sequenceViolations, input, output, cached, reasoning, model, backend);
    }

    private static string? GetString(JsonElement value, string property) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var result) && result.ValueKind == JsonValueKind.String ? result.GetString() : null;
    private static long? ReadLong(JsonElement value, string property)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(property, out var result)) return null;
        if (result.ValueKind == JsonValueKind.Number && result.TryGetInt64(out long number)) return number;
        if (result.ValueKind == JsonValueKind.String && long.TryParse(result.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }
}

public static class PromptInputInspector
{
    public static string NormalizeDigest(string path, byte[] expectedPrompt, string sessionDirectory)
    {
        string json = File.ReadAllText(path, new UTF8Encoding(false, true));
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw LivePilotJson.Failure("PromptInputInvalid");
        string expected = new UTF8Encoding(false, true).GetString(expectedPrompt);
        int promptReplacements = 0;
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output)) WriteNormalized(writer, doc.RootElement, null, expected, sessionDirectory, ref promptReplacements);
        if (promptReplacements != 1) throw LivePilotJson.Failure("PromptInputPromptMismatch");
        return CanonicalJson.RawDigest(output.ToArray());
    }

    private static void WriteNormalized(Utf8JsonWriter writer, JsonElement value, string? propertyName, string expectedPrompt, string sessionDirectory, ref int promptReplacements)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    if (!names.Add(property.Name)) throw LivePilotJson.Failure("PromptInputInvalid");
                    writer.WritePropertyName(property.Name);
                    if (property.Name == "create_time")
                    {
                        if (property.Value.ValueKind is not (JsonValueKind.Number or JsonValueKind.String)) throw LivePilotJson.Failure("PromptInputInvalid");
                        writer.WriteStringValue("<VOLATILE_CREATE_TIME>");
                    }
                    else WriteNormalized(writer, property.Value, property.Name, expectedPrompt, sessionDirectory, ref promptReplacements);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteNormalized(writer, item, propertyName, expectedPrompt, sessionDirectory, ref promptReplacements);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                string text = value.GetString()!;
                if (propertyName is "id" or "turn_id" or "create_time") text = $"<VOLATILE_{propertyName.ToUpperInvariant()}>";
                else if (text == expectedPrompt) { text = "<E10_USER_PROMPT>"; promptReplacements++; }
                else text = NormalizePath(text, sessionDirectory);
                writer.WriteStringValue(text);
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static string NormalizePath(string value, string path)
    {
        string full = Path.GetFullPath(path);
        return value.Replace(full, "<E10_EMPTY_CWD>", StringComparison.OrdinalIgnoreCase)
            .Replace(full.Replace('\\', '/'), "<E10_EMPTY_CWD>", StringComparison.OrdinalIgnoreCase);
    }
}
