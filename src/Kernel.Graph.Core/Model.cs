using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kernel.Graph;
public sealed record Criterion(string Id, string Payload, bool Completed);
public sealed record TaskNode(string Id, string Payload, long? StartMinute, long? DueMinute, string Status,
    Criterion[] Criteria, string? RepeatRule, string[] History);
public sealed record Edge(string From, string To);
public sealed record Binding(string OldId, string FreshId);
public sealed record TaskGraph(TaskNode[] Tasks, Edge[] Contains, Edge[] Blocks);
public sealed record CloneInput(TaskGraph Graph, string SourceRootId, long DateDeltaMinutes, Binding[] TaskIdMap, Binding[] CriterionIdMap);
public sealed record TaskGraphPatch(TaskNode[] AddTasks, Edge[] AddContains, Edge[] AddBlocks);
public sealed record GraphError(string Stage, string Code, string[] InvolvedIds, object? Witness, string[] AllowedRepairs);
public sealed record CloneOutcome(string Status, TaskGraphPatch? Patch, GraphError? Error) {
    public static CloneOutcome Reject(string stage, string code, params string[] ids) =>
        new("Rejected", null, new(stage, code, ids.Order(StringComparer.Ordinal).Distinct().ToArray(), null, [Repair(code)]));
    public static string Repair(string c) => c switch {
        "TransportInvalid" => "FixEncoding", "SchemaInvalid" => "FixSchema", "LimitExceeded" => "ReduceInput",
        "SourceRootMissing" => "SelectExistingRoot", "TaskIdMapDomainMismatch" => "SupplyExactTaskMap",
        "CriterionIdMapDomainMismatch" => "SupplyExactCriterionMap", "FreshIdInvalid" => "SupplyValidFreshIds",
        "FreshIdCollision" => "SupplyDisjointFreshIds", "DateOverflow" => "AdjustDateDelta",
        "ArtifactMismatch" => "RecompileArtifact",
        "ToolMismatch" or "RestoreFailed" or "PublishFailed" or "NativeCodeMissing" or "RuntimeFailure" or "InfrastructureFailure" or "InternalInvariantViolation" => "RetryToolchain",
        "PipelineInvalid" or "PolicyInvalid" or "ProofBypass" or "ProofNotEstablished" => "FixSchema",
        "ContractNotApproved" => "RecompileArtifact", _ => "RepairGraph"
    };
}
public sealed class GraphException(string stage, string code,Exception? cause=null) : Exception(code,cause) {
    public string Stage { get; } = stage;
    public string Code { get; } = code;
    public CloneOutcome Outcome => CloneOutcome.Reject(Stage, Code);
}
public static class Json {
    public static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false,
        Converters = { new LongConverter(), new NullableLongConverter() }
    };
    public static byte[] Bytes<T>(T x) => Canonical(JsonSerializer.SerializeToUtf8Bytes(x,Options));
    public static string Text<T>(T x) => System.Text.Encoding.UTF8.GetString(Bytes(x));
    public static byte[] Canonical(byte[] source) {
        using var d = JsonDocument.Parse(source);
        using var ms = new MemoryStream(); using(var w = new Utf8JsonWriter(ms)) Write(w,d.RootElement);
        return ms.ToArray();
    }
    private static void Write(Utf8JsonWriter w, JsonElement e) {
        if(e.ValueKind == JsonValueKind.Object) { w.WriteStartObject(); foreach(var p in e.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal)){w.WritePropertyName(p.Name);Write(w,p.Value);} w.WriteEndObject(); }
        else if(e.ValueKind == JsonValueKind.Array){w.WriteStartArray();foreach(var i in e.EnumerateArray())Write(w,i);w.WriteEndArray();}
        else e.WriteTo(w);
    }
    private sealed class LongConverter : JsonConverter<long> {
        public override long Read(ref Utf8JsonReader r,Type t,JsonSerializerOptions o)=>long.Parse(r.GetString()!,System.Globalization.CultureInfo.InvariantCulture);
        public override void Write(Utf8JsonWriter w,long v,JsonSerializerOptions o)=>w.WriteStringValue(v.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    private sealed class NullableLongConverter : JsonConverter<long?> {
        public override long? Read(ref Utf8JsonReader r,Type t,JsonSerializerOptions o)=>r.TokenType==JsonTokenType.Null?null:long.Parse(r.GetString()!,System.Globalization.CultureInfo.InvariantCulture);
        public override void Write(Utf8JsonWriter w,long? v,JsonSerializerOptions o){if(v is null)w.WriteNullValue();else w.WriteStringValue(v.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));}
    }
}
