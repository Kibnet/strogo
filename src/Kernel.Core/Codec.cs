using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Kernel.Core;

public static class CanonicalJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    { "program", "node", "outputs", "patch", "event", "ir", "policy", "manifest", "admission", "input", "output", "trace", "receipt", "state", "genesis" };

    public static JsonDocument ParseStrict(string json, CoreLimits? limits = null)
    {
        limits ??= new();
        if (Encoding.UTF8.GetByteCount(json) > limits.MaxTransportBytes)
            throw Failure("TransportLimitExceeded", new { limit = limits.MaxTransportBytes });
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = limits.MaxJsonDepth, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow }); }
        catch (JsonException) { throw Failure("SchemaInvalid", new { reason = "InvalidJsonOrDepthExceeded" }); }
        try { Check(doc.RootElement); return doc; }
        catch { doc.Dispose(); throw; }
    }

    private static void Check(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                CheckAscii(property.Name);
                if (!names.Add(property.Name)) throw Failure("DuplicateField", new { field = property.Name });
                Check(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) Check(item);
        else if (element.ValueKind == JsonValueKind.String) CheckAscii(element.GetString()!);
    }

    public static void CheckAscii(string value)
    {
        if (value.Any(c => c < 32 || c > 126)) throw Failure("SchemaInvalid", new { reason = "NonPrintableAscii" });
    }
    public static byte[] Encode(object? value)
    {
        var element = value is JsonElement existing ? existing : JsonSerializer.SerializeToElement(value, SerializerOptions);
        Check(element);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) Write(writer, element);
        return stream.ToArray();
    }
    public static string Hash(string kind, object? payload) => HashBytes(kind, Encode(payload));
    public static string HashBytes(string kind, ReadOnlySpan<byte> canonicalPayload)
    {
        if (!Kinds.Contains(kind)) throw new ArgumentException("Unknown content domain", nameof(kind));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"kernel.v0/{kind}\n"));
        hash.AppendData(canonicalPayload);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    public static string RawDigest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var p in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)) { writer.WritePropertyName(p.Name); Write(writer, p.Value); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) Write(writer, item); writer.WriteEndArray(); break;
            case JsonValueKind.String: writer.WriteStringValue(element.GetString()); break;
            case JsonValueKind.Number:
                if (!BigInteger.TryParse(element.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) throw Failure("SchemaInvalid", new { reason = "NonIntegerNumberInArtifact" });
                writer.WriteStringValue(n.ToString(CultureInfo.InvariantCulture)); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw Failure("SchemaInvalid", new { reason = "UndefinedValue" });
        }
    }
    internal static KernelException Failure(string code, object? details = null, string? id = null) => new(KernelError.Create("schema", code, id, details));
}

internal static class Wire
{
    internal static readonly Regex IdPattern = new("^[a-z][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex DecimalPattern = new("^(0|-?[1-9][0-9]*)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    internal static void Fields(JsonElement e, params string[] names)
    {
        if (e.ValueKind != JsonValueKind.Object) throw CanonicalJson.Failure("SchemaInvalid", new { reason = "ExpectedObject" });
        var actual = e.EnumerateObject().Select(p => p.Name).ToArray();
        if (actual.Distinct(StringComparer.Ordinal).Count() != actual.Length) throw CanonicalJson.Failure("DuplicateField");
        if (actual.Length != names.Length || actual.Any(n => !names.Contains(n, StringComparer.Ordinal)))
            throw CanonicalJson.Failure("SchemaInvalid", new { reason = "UnexpectedOrMissingFields", expected = names, actual });
    }
    internal static string String(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.String) throw CanonicalJson.Failure("SchemaInvalid", new { reason = "ExpectedString" });
        return e.GetString()!;
    }
    internal static string Id(JsonElement e) { var s = String(e); RequireId(s); return s; }
    internal static void RequireId(string s) { if (!IdPattern.IsMatch(s)) throw CanonicalJson.Failure("InvalidId", new { value = s }); }
    internal static void RequireDigest(string s) { if (!DigestPattern.IsMatch(s)) throw CanonicalJson.Failure("InvalidRevision", new { value = s }); }
    internal static long I64(JsonElement e)
    {
        var text = String(e);
        if (!DecimalPattern.IsMatch(text) || !long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
            throw CanonicalJson.Failure("InvalidI64", new { value = text });
        return value;
    }
    internal static int Index(JsonElement e) { long n = I64(e); if (n < 0 || n > int.MaxValue) throw CanonicalJson.Failure("InvalidIndex"); return (int)n; }
    internal static bool Bool(JsonElement e) => e.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw CanonicalJson.Failure("SchemaInvalid", new { reason = "ExpectedBool" }) };
    internal static KernelType Type(JsonElement e) => String(e) switch { "I64" => KernelType.I64, "Bool" => KernelType.Bool, _ => throw CanonicalJson.Failure("UnsupportedType") };
    internal static IEnumerable<JsonElement> Array(JsonElement e)
    { if (e.ValueKind != JsonValueKind.Array) throw CanonicalJson.Failure("SchemaInvalid", new { reason = "ExpectedArray" }); return e.EnumerateArray(); }
}

public static class ProgramCodec
{
    public static KernelProgram Parse(string json, CoreLimits? limits = null)
    {
        using var doc = CanonicalJson.ParseStrict(json, limits);
        var root = doc.RootElement;
        Wire.Fields(root, "schemaVersion", "programId", "profileId", "nodes", "outputs");
        return new(Wire.String(root.GetProperty("schemaVersion")), Wire.Id(root.GetProperty("programId")), Wire.Id(root.GetProperty("profileId")),
            [.. Wire.Array(root.GetProperty("nodes")).Select(ParseNode)], ParseOutputs(root.GetProperty("outputs")));
    }
    public static KernelNode ParseNode(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("op", out var opValue)) throw CanonicalJson.Failure("SchemaInvalid", new { reason = "MissingOpcode" });
        var op = Wire.String(opValue);
        if (!GraphValidator.Opcodes.Contains(op)) throw CanonicalJson.Failure("UnsupportedOpcode", new { opcode = op });
        Wire.Fields(node, op == "input" ? ["id", "op", "type", "args", "fieldId"] : op.EndsWith(".const", StringComparison.Ordinal) ? ["id", "op", "type", "args", "value"] : ["id", "op", "type", "args"]);
        return new(Wire.Id(node.GetProperty("id")), op, Wire.Type(node.GetProperty("type")),
            [.. Wire.Array(node.GetProperty("args")).Select(Wire.Id)],
            op == "input" ? Wire.String(node.GetProperty("fieldId")) : null,
            op == "i64.const" ? Wire.I64(node.GetProperty("value")) : null,
            op == "bool.const" ? Wire.Bool(node.GetProperty("value")) : null);
    }
    public static OutputRefs ParseOutputs(JsonElement e)
    { Wire.Fields(e, "accepted", "available", "reserved"); return new(Wire.Id(e.GetProperty("accepted")), Wire.Id(e.GetProperty("available")), Wire.Id(e.GetProperty("reserved"))); }
    public static object NodePayload(KernelNode node)
    {
        var data = new Dictionary<string, object?> { ["id"] = node.Id, ["op"] = node.Op, ["type"] = node.Type.ToString(), ["args"] = node.Args };
        if (node.Op == "input") data["fieldId"] = node.FieldId;
        if (node.Op == "i64.const") data["value"] = node.I64Value?.ToString(CultureInfo.InvariantCulture);
        if (node.Op == "bool.const") data["value"] = node.BoolValue;
        return data;
    }
    public static byte[] CanonicalBytes(KernelProgram program) => CanonicalJson.Encode(new
    { program.SchemaVersion, program.ProgramId, program.ProfileId, nodes = program.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal).Select(NodePayload).ToArray(), program.Outputs });
    public static string Revision(KernelProgram program) => CanonicalJson.HashBytes("program", CanonicalBytes(program));
    public static string NodeRevision(KernelNode node) => CanonicalJson.Hash("node", NodePayload(node));
    public static string OutputsDigest(OutputRefs outputs) => CanonicalJson.Hash("outputs", outputs);
}

public static class IrCodec
{
    public static byte[] CanonicalBytes(IrProgram ir) => CanonicalJson.Encode(new
    {
        ir.SemanticsVersion, ir.ProgramRevision,
        instructions = ir.Instructions.Select(i => new { i.DestinationIndex, i.OriginNodeId, i.Opcode, i.OperandIndices, type = i.Type.ToString(), i.FieldId,
            value = i.Opcode == "i64.const" ? (object?)i.I64Value?.ToString(CultureInfo.InvariantCulture) : i.Opcode == "bool.const" ? i.BoolValue : null }).ToArray(), ir.Outputs
    });
    public static string Revision(IrProgram ir) => CanonicalJson.HashBytes("ir", CanonicalBytes(ir));
    public static IrProgram Parse(string json)
    {
        using var doc = CanonicalJson.ParseStrict(json);
        var r = doc.RootElement;
        Wire.Fields(r, "semanticsVersion", "programRevision", "instructions", "outputs");
        var instructions = Wire.Array(r.GetProperty("instructions")).Select(i =>
        {
            Wire.Fields(i, "destinationIndex", "originNodeId", "opcode", "operandIndices", "type", "fieldId", "value");
            var op = Wire.String(i.GetProperty("opcode"));
            var field = i.GetProperty("fieldId"); var value = i.GetProperty("value");
            if (op != "input" && field.ValueKind != JsonValueKind.Null || op != "i64.const" && op != "bool.const" && value.ValueKind != JsonValueKind.Null)
                throw CanonicalJson.Failure("InvalidIr", new { reason = "ExtraneousLiteralOrField" });
            return new IrInstruction(Wire.Index(i.GetProperty("destinationIndex")), Wire.Id(i.GetProperty("originNodeId")), op,
                [.. Wire.Array(i.GetProperty("operandIndices")).Select(Wire.Index)], Wire.Type(i.GetProperty("type")),
                field.ValueKind == JsonValueKind.Null ? null : Wire.String(field),
                op == "i64.const" ? Wire.I64(value) : null, op == "bool.const" ? Wire.Bool(value) : null);
        }).ToImmutableArray();
        var o = r.GetProperty("outputs"); Wire.Fields(o, "accepted", "available", "reserved");
        var result = new IrProgram(Wire.String(r.GetProperty("semanticsVersion")), Wire.String(r.GetProperty("programRevision")), instructions,
            new(Wire.Index(o.GetProperty("accepted")), Wire.Index(o.GetProperty("available")), Wire.Index(o.GetProperty("reserved"))));
        IrValidator.Validate(result); return result;
    }
}

public static class ExecutionCodec
{
    public static object ValuePayload(KernelValue value) => new { type = value.Type.ToString(), value = value.Type == KernelType.I64 ? (object)value.Number.ToString(CultureInfo.InvariantCulture) : value.Boolean };
    public static byte[] OutputBytes(ReserveOutput output) => CanonicalJson.Encode(output);
    public static byte[] TraceBytes(EvaluationResult evaluation) => TraceBytes(evaluation.Trace);
    public static byte[] TraceBytes(IEnumerable<TraceEntry> trace) => CanonicalJson.Encode(trace.Select(t =>
    {
        var d = new Dictionary<string, object?> { ["nodeId"] = t.NodeId, ["opcode"] = t.Opcode, ["operands"] = t.Operands.Select(ValuePayload).ToArray(), ["value"] = t.Value is { } v ? ValuePayload(v) : null };
        if (t.ErrorCode is not null) d["errorCode"] = t.ErrorCode;
        return d;
    }).ToArray());
}
