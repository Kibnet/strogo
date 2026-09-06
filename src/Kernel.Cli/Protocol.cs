using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Kernel.Core;
using Kernel.Host;

namespace Kernel.Cli;

/// <summary>The data-only agent boundary. Setup, policy, solver and database options never appear here.</summary>
internal static partial class Protocol
{
    internal static readonly JsonSerializerOptions OutputOptions = CreateOutputOptions();

    private static JsonSerializerOptions CreateOutputOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ExactLongConverter());
        options.Converters.Add(new ExactIntConverter());
        return options;
    }

    internal static async Task<object> DispatchAsync(KernelClient client, string json)
    {
        using var document = CanonicalJson.ParseStrict(json);
        var request = document.RootElement;
        Fields(request, "schemaVersion", "method", "arguments");
        if (String(request, "schemaVersion") != KernelVersions.Schema) throw Error("UnsupportedSchema");
        var method = String(request, "method");
        var arguments = request.GetProperty("arguments");
        switch (method)
        {
            case "Snapshot":
                Fields(arguments, "resourceId");
                return client.Snapshot(String(arguments, "resourceId"));
            case "ProposePatch":
                Fields(arguments, "patch");
                return await client.ProposePatchAsync(arguments.GetProperty("patch").GetRawText());
            case "Prepare":
                Fields(arguments, "event", "expectedStateRevision", "expectedProgramRevision", "expectedPolicyRevision");
                var ev = arguments.GetProperty("event");
                Fields(ev, "eventId", "resourceId", "kind", "quantity");
                return await client.PrepareAsync(new PrepareRequest(
                    new ReserveEvent(String(ev, "eventId"), String(ev, "resourceId"), String(ev, "kind"), I64(ev, "quantity")),
                    String(arguments, "expectedStateRevision"), String(arguments, "expectedProgramRevision"), String(arguments, "expectedPolicyRevision")));
            case "Commit":
                Fields(arguments, "prepareId");
                return await client.CommitAsync(String(arguments, "prepareId"));
            case "Replay":
                Fields(arguments, "receiptId");
                return client.Replay(String(arguments, "receiptId"));
            case "Explain":
                Fields(arguments, "artifactId");
                return client.Explain(String(arguments, "artifactId"));
            default:
                throw Error("UnsupportedMethod");
        }
    }

    internal static string Serialize(object? value) => JsonSerializer.Serialize(value, OutputOptions);
    internal static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error("SchemaInvalid");
        var actual = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (actual.Length != names.Length || actual.Any(n => !names.Contains(n, StringComparer.Ordinal)))
            throw Error("SchemaInvalid");
    }

    internal static string String(JsonElement value, string name)
    {
        var property = value.GetProperty(name);
        if (property.ValueKind != JsonValueKind.String) throw Error("SchemaInvalid");
        return property.GetString()!;
    }

    internal static long I64(JsonElement value, string name)
    {
        var text = String(value, name);
        if (!DecimalPattern().IsMatch(text) ||
            !long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result))
            throw Error("InvalidI64");
        return result;
    }

    internal static KernelException Error(string code) => new(KernelError.Create("protocol", code));

    [GeneratedRegex("^(0|-?[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalPattern();

    private sealed class ExactLongConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("Output-only converter");
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class ExactIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("Output-only converter");
        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
