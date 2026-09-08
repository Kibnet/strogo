#nullable enable
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Strogo.Portable.V01;

public static class ModuleApi
{
    private const int MaximumInputBytes = 65536;
    private const int MaximumDepth = 32;
    private const int MaximumValues = 2048;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex CanonicalI64 = new("^(0|-?[1-9][0-9]*)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly string[] RefusalPriority =
    [
        "MalformedJson", "TransportDepthLimitExceeded", "TransportValueLimitExceeded", "DuplicateProperty",
        "UnknownProperty", "MissingProperty", "UnsupportedInvokeSchema", "InvalidRoot", "UnknownWireKind",
        "InvalidI64Encoding", "NonCanonicalTransport"
    ];

    public static string Invoke(string canonicalRequestJson)
    {
        if (canonicalRequestJson is null) return RefusalJson("NullRequest", "$", []);
        if (!HasOnlyUnicodeScalars(canonicalRequestJson)) return RefusalJson("InvalidUnicode", "$", []);
        var utf8Length = StrictUtf8.GetByteCount(canonicalRequestJson);
        if (utf8Length > MaximumInputBytes)
            return RefusalJson("InputTooLarge", "$", [("actual", utf8Length.ToString(CultureInfo.InvariantCulture)), ("max", MaximumInputBytes.ToString(CultureInfo.InvariantCulture))]);
        var utf8 = StrictUtf8.GetBytes(canonicalRequestJson);

        var syntax = InspectSyntax(utf8);
        if (syntax.Malformed) return RefusalJson("MalformedJson", "$", []);
        if (syntax.Depth > MaximumDepth)
            return RefusalJson("TransportDepthLimitExceeded", "$", [("actual", syntax.Depth.ToString(CultureInfo.InvariantCulture)), ("max", MaximumDepth.ToString(CultureInfo.InvariantCulture))]);
        if (syntax.Values > MaximumValues)
            return RefusalJson("TransportValueLimitExceeded", "$", [("actual", syntax.Values.ToString(CultureInfo.InvariantCulture)), ("max", MaximumValues.ToString(CultureInfo.InvariantCulture))]);

        using var document = JsonDocument.Parse(utf8, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = MaximumInputBytes });
        var findings = new List<Finding>();
        ValidateRequest(document.RootElement, findings);
        if (findings.Count > 0)
        {
            var finding = findings.OrderBy(item => Array.IndexOf(RefusalPriority, item.Code)).ThenBy(item => item.Locus, StringComparer.Ordinal).First();
            return RefusalJson(finding.Code, finding.Locus, finding.Details);
        }

        var root = document.RootElement;
        var canonical = CanonicalRequestBytes(root);
        if (!utf8.AsSpan().SequenceEqual(canonical)) return RefusalJson("NonCanonicalTransport", "$", []);
        var functionId = root.GetProperty("functionId").GetString()!;
        var arguments = root.GetProperty("arguments").EnumerateArray().Select(BuildWireValue).ToArray();
        var outcome = PortableWrapper.__default.Invoke(DafnyText(functionId), Dafny.Sequence<PortableWrapper._IWireValue>.FromArray(arguments));
        return OutcomeJson(outcome);
    }

    private static bool HasOnlyUnicodeScalars(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsHighSurrogate(current))
            {
                if (++index >= value.Length || !char.IsLowSurrogate(value[index])) return false;
            }
            else if (char.IsLowSurrogate(current)) return false;
        }
        return true;
    }

    private static SyntaxInspection InspectSyntax(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = MaximumInputBytes });
        var depth = 0;
        var values = 0;
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray or JsonTokenType.String or JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False or JsonTokenType.Null)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        values++;
                        depth = Math.Max(depth, reader.CurrentDepth + 1);
                    }
                }
            }
            return new(values == 0, depth, values);
        }
        catch (JsonException)
        {
            return new(true, depth, values);
        }
    }

    private static void ValidateRequest(JsonElement root, List<Finding> findings)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            findings.Add(InvalidRoot("$", root));
            return;
        }
        ValidateObject(root, "$", ["schema", "functionId", "arguments"], findings);
        if (TryUnique(root, "schema", out var schema))
        {
            var actual = schema.ValueKind == JsonValueKind.String ? schema.GetString()! : schema.GetRawText();
            if (actual != "strogo.invoke.v0.1") findings.Add(new("UnsupportedInvokeSchema", "$/schema", [("expected", "strogo.invoke.v0.1"), ("actual", actual)]));
        }
        if (TryUnique(root, "functionId", out var functionId) && functionId.ValueKind != JsonValueKind.String)
            findings.Add(new("NonCanonicalTransport", "$/functionId", []));
        if (TryUnique(root, "arguments", out var arguments))
        {
            if (arguments.ValueKind != JsonValueKind.Array) findings.Add(new("NonCanonicalTransport", "$/arguments", []));
            else
            {
                var index = 0;
                foreach (var argument in arguments.EnumerateArray()) ValidateWireValue(argument, $"$/arguments/{index++}", findings);
            }
        }
    }

    private static void ValidateWireValue(JsonElement value, string locus, List<Finding> findings)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            findings.Add(InvalidRoot(locus, value));
            return;
        }
        var kind = TryUnique(value, "kind", out var kindValue) && kindValue.ValueKind == JsonValueKind.String ? kindValue.GetString() : null;
        var fields = kind switch
        {
            "i64" or "bool" => new[] { "kind", "value" },
            "sequence" => new[] { "kind", "items" },
            "record" => new[] { "kind", "typeId", "fields" },
            _ => new[] { "kind" }
        };
        ValidateObject(value, locus, fields, findings);
        if (kind is null || kind is not ("i64" or "bool" or "sequence" or "record"))
        {
            var kindText = kindValue.ValueKind switch { JsonValueKind.String => kindValue.GetString()!, JsonValueKind.Undefined => "", _ => kindValue.GetRawText() };
            findings.Add(new("UnknownWireKind", locus + "/kind", [("kind", kindText)]));
            return;
        }
        if (kind == "i64" && TryUnique(value, "value", out var i64))
        {
            var text = i64.ValueKind == JsonValueKind.String ? i64.GetString()! : i64.GetRawText();
            if (i64.ValueKind != JsonValueKind.String || !CanonicalI64.IsMatch(text) || !BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                findings.Add(new("InvalidI64Encoding", locus + "/value", [("value", text)]));
        }
        else if (kind == "bool" && TryUnique(value, "value", out var boolean) && boolean.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            findings.Add(new("NonCanonicalTransport", locus + "/value", []));
        else if (kind == "sequence" && TryUnique(value, "items", out var items))
        {
            if (items.ValueKind != JsonValueKind.Array) findings.Add(new("NonCanonicalTransport", locus + "/items", []));
            else
            {
                var index = 0;
                foreach (var item in items.EnumerateArray()) ValidateWireValue(item, $"{locus}/items/{index++}", findings);
            }
        }
        else if (kind == "record")
        {
            if (TryUnique(value, "typeId", out var typeId) && typeId.ValueKind != JsonValueKind.String)
                findings.Add(new("NonCanonicalTransport", locus + "/typeId", []));
            if (TryUnique(value, "fields", out var recordFields))
            {
                if (recordFields.ValueKind != JsonValueKind.Array) findings.Add(new("NonCanonicalTransport", locus + "/fields", []));
                else
                {
                    var index = 0;
                    foreach (var field in recordFields.EnumerateArray()) ValidateWireField(field, $"{locus}/fields/{index++}", findings);
                }
            }
        }
    }

    private static void ValidateWireField(JsonElement field, string locus, List<Finding> findings)
    {
        if (field.ValueKind != JsonValueKind.Object)
        {
            findings.Add(InvalidRoot(locus, field));
            return;
        }
        ValidateObject(field, locus, ["fieldId", "value"], findings);
        if (TryUnique(field, "fieldId", out var fieldId) && fieldId.ValueKind != JsonValueKind.String)
            findings.Add(new("NonCanonicalTransport", locus + "/fieldId", []));
        if (TryUnique(field, "value", out var value)) ValidateWireValue(value, locus + "/value", findings);
    }

    private static void ValidateObject(JsonElement element, string locus, string[] expected, List<Finding> findings)
    {
        var properties = element.EnumerateObject().ToArray();
        foreach (var group in properties.GroupBy(property => property.Name, StringComparer.Ordinal).Where(group => group.Count() > 1))
            findings.Add(new("DuplicateProperty", locus, [("property", group.Key)]));
        foreach (var property in properties.Where(property => !expected.Contains(property.Name, StringComparer.Ordinal)))
            findings.Add(new("UnknownProperty", locus, [("property", property.Name)]));
        foreach (var property in expected.Where(name => properties.All(property => property.Name != name)))
            findings.Add(new("MissingProperty", locus, [("property", property)]));
    }

    private static bool TryUnique(JsonElement element, string name, out JsonElement value)
    {
        var matches = element.EnumerateObject().Where(property => property.Name == name).Select(property => property.Value).Take(2).ToArray();
        value = matches.Length == 1 ? matches[0] : default;
        return matches.Length == 1;
    }

    private static Finding InvalidRoot(string locus, JsonElement actual)
        => new("InvalidRoot", locus, [("expected", "object"), ("actual", KindName(actual.ValueKind))]);

    private static string KindName(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Object => "object", JsonValueKind.Array => "array", JsonValueKind.String => "string",
        JsonValueKind.Number => "number", JsonValueKind.True or JsonValueKind.False => "boolean", JsonValueKind.Null => "null", _ => "undefined"
    };

    private static PortableWrapper._IWireValue BuildWireValue(JsonElement value)
    {
        return value.GetProperty("kind").GetString() switch
        {
            "i64" => PortableWrapper.WireValue.create_WI64(BigInteger.Parse(value.GetProperty("value").GetString()!, CultureInfo.InvariantCulture)),
            "bool" => PortableWrapper.WireValue.create_WBool(value.GetProperty("value").GetBoolean()),
            "sequence" => PortableWrapper.WireValue.create_WSeq(Dafny.Sequence<PortableWrapper._IWireValue>.FromArray(value.GetProperty("items").EnumerateArray().Select(BuildWireValue).ToArray())),
            "record" => PortableWrapper.WireValue.create_WRecord(DafnyText(value.GetProperty("typeId").GetString()!), Dafny.Sequence<PortableWrapper._IWireField>.FromArray(value.GetProperty("fields").EnumerateArray().Select(field => PortableWrapper.WireField.create(DafnyText(field.GetProperty("fieldId").GetString()!), BuildWireValue(field.GetProperty("value")))).ToArray())),
            _ => throw new UnreachableException()
        };
    }

    private static byte[] CanonicalRequestBytes(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = Writer(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", root.GetProperty("schema").GetString());
            writer.WriteString("functionId", root.GetProperty("functionId").GetString());
            writer.WritePropertyName("arguments");
            writer.WriteStartArray();
            foreach (var argument in root.GetProperty("arguments").EnumerateArray()) WriteCanonicalWire(writer, argument);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WriteCanonicalWire(Utf8JsonWriter writer, JsonElement value)
    {
        var kind = value.GetProperty("kind").GetString()!;
        writer.WriteStartObject();
        writer.WriteString("kind", kind);
        if (kind == "i64") writer.WriteString("value", value.GetProperty("value").GetString());
        else if (kind == "bool") writer.WriteBoolean("value", value.GetProperty("value").GetBoolean());
        else if (kind == "sequence")
        {
            writer.WritePropertyName("items"); writer.WriteStartArray();
            foreach (var item in value.GetProperty("items").EnumerateArray()) WriteCanonicalWire(writer, item);
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteString("typeId", value.GetProperty("typeId").GetString());
            writer.WritePropertyName("fields"); writer.WriteStartArray();
            foreach (var field in value.GetProperty("fields").EnumerateArray())
            {
                writer.WriteStartObject(); writer.WriteString("fieldId", field.GetProperty("fieldId").GetString());
                writer.WritePropertyName("value"); WriteCanonicalWire(writer, field.GetProperty("value")); writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }

    private static string OutcomeJson(PortableWrapper._IWireOutcome outcome)
    {
        using var stream = new MemoryStream();
        using (var writer = Writer(stream))
        {
            writer.WriteStartObject(); writer.WriteString("schema", "strogo.invoke-result.v0.1");
            if (outcome.is_Success)
            {
                writer.WriteString("kind", "success"); writer.WritePropertyName("value"); WriteWire(writer, outcome.dtor_value);
            }
            else
            {
                writer.WriteString("kind", "refusal"); writer.WriteString("code", Text(outcome.dtor_code)); writer.WriteString("locus", Text(outcome.dtor_locus));
                writer.WritePropertyName("details"); writer.WriteStartObject();
                for (var index = 0; index < outcome.dtor_details.Count; index++)
                {
                    var detail = outcome.dtor_details.Select(new BigInteger(index));
                    writer.WritePropertyName(Text(detail.dtor_key));
                    writer.WriteStringValue(detail.dtor_detailValue.is_DText ? Text(detail.dtor_detailValue.dtor_text) : detail.dtor_detailValue.dtor_value.ToString(CultureInfo.InvariantCulture));
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return StrictUtf8.GetString(stream.ToArray());
    }

    private static string RefusalJson(string code, string locus, IReadOnlyList<(string Key, string Value)> details)
    {
        using var stream = new MemoryStream();
        using (var writer = Writer(stream))
        {
            writer.WriteStartObject(); writer.WriteString("schema", "strogo.invoke-result.v0.1"); writer.WriteString("kind", "refusal");
            writer.WriteString("code", code); writer.WriteString("locus", locus); writer.WritePropertyName("details"); writer.WriteStartObject();
            foreach (var detail in details) writer.WriteString(detail.Key, detail.Value);
            writer.WriteEndObject(); writer.WriteEndObject();
        }
        return StrictUtf8.GetString(stream.ToArray());
    }

    private static void WriteWire(Utf8JsonWriter writer, PortableWrapper._IWireValue value)
    {
        writer.WriteStartObject();
        if (value.is_WI64) { writer.WriteString("kind", "i64"); writer.WriteString("value", value.dtor_i64Value.ToString(CultureInfo.InvariantCulture)); }
        else if (value.is_WBool) { writer.WriteString("kind", "bool"); writer.WriteBoolean("value", value.dtor_boolValue); }
        else if (value.is_WSeq)
        {
            writer.WriteString("kind", "sequence"); writer.WritePropertyName("items"); writer.WriteStartArray();
            for (var index = 0; index < value.dtor_sequenceItems.Count; index++) WriteWire(writer, value.dtor_sequenceItems.Select(new BigInteger(index)));
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteString("kind", "record"); writer.WriteString("typeId", Text(value.dtor_recordTypeId)); writer.WritePropertyName("fields"); writer.WriteStartArray();
            for (var index = 0; index < value.dtor_recordFields.Count; index++)
            {
                var field = value.dtor_recordFields.Select(new BigInteger(index)); writer.WriteStartObject(); writer.WriteString("fieldId", Text(field.dtor_fieldId));
                writer.WritePropertyName("value"); WriteWire(writer, field.dtor_fieldValue); writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }

    private static Utf8JsonWriter Writer(Stream stream) => new(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    private static Dafny.ISequence<Dafny.Rune> DafnyText(string value) => Dafny.Sequence<Dafny.Rune>.UnicodeFromString(value);
    private static string Text(Dafny.ISequence<Dafny.Rune> value) => value.ToVerbatimString(false);

    private sealed record Finding(string Code, string Locus, IReadOnlyList<(string Key, string Value)> Details);
    private readonly record struct SyntaxInspection(bool Malformed, int Depth, int Values);
}
