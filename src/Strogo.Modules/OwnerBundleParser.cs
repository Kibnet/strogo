using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kernel.Core;

namespace Strogo.Modules;

public static class OwnerBundleParser
{
    private const int TransportMaximum = StrogoLimits.TransportBytesHardMaximum;
    private const int JsonDepthMaximum = StrogoLimits.JsonDepthHardMaximum;
    private static readonly Regex IdPattern = new("^[A-Za-z][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex CanonicalUnsignedPattern = new("^(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex CanonicalI64Pattern = new("^(0|-?[1-9][0-9]*)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly ImmutableHashSet<string> SupportedExpressionOps = ImmutableHashSet.Create(StringComparer.Ordinal,
        "param", "result", "i64.const", "bool.const", "i64.add", "i64.sub", "i64.le", "eq",
        "bool.not", "bool.and", "bool.or", "if", "model.call");

    public static OwnerBundle Parse(string source) => Parse(Encoding.UTF8.GetBytes(source));

    public static OwnerBundle Parse(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > TransportMaximum)
            throw ModulesExceptionFactory.Error("owner-parse", "TransportLimitExceeded", details: new { actual = source.Length, max = TransportMaximum });
        JsonDocument document;
        try
        {
            document = CanonicalJson.ParseStrict(Encoding.UTF8.GetString(source), new CoreLimits(
                MaxTransportBytes: TransportMaximum,
                MaxJsonDepth: JsonDepthMaximum));
        }
        catch (KernelException exception)
        {
            throw ModulesExceptionFactory.Error("owner-parse", exception.Error.Code,
                details: new { kernelStage = exception.Error.Stage, kernelDetails = exception.Error.Details });
        }

        using (document)
        {
            var root = document.RootElement;
            CheckObject(root, "bundle", ["schemaVersion", "bundleId", "entryContracts", "models", "limits"]);
            var schemaVersion = RequireString(root.GetProperty("schemaVersion"));
            if (schemaVersion != OwnerBundleVersions.SchemaVersion)
                throw ModulesExceptionFactory.Error("owner-parse", "SchemaVersionMismatch", details: new { expected = OwnerBundleVersions.SchemaVersion, actual = schemaVersion });
            var bundleId = RequireId(root.GetProperty("bundleId"));
            var limits = ParseLimits(root.GetProperty("limits"));
            var expressionNodes = 0;
            var models = ParseModels(root.GetProperty("models"), limits, ref expressionNodes);
            var entries = ParseEntries(root.GetProperty("entryContracts"), limits, ref expressionNodes);
            if (entries.Length == 0)
                throw ModulesExceptionFactory.Error("owner-parse", "EntryContractRequired");
            if (models.Length == 0)
                throw ModulesExceptionFactory.Error("owner-parse", "OwnerModelRequired");

            var validatedEntries = OwnerContractSemantics.Validate(entries, models);
            var canonical = OwnerBundleCodec.Canonicalize(schemaVersion, bundleId, validatedEntries, models, limits);
            return new OwnerBundle(schemaVersion, bundleId, validatedEntries, models, limits, canonical);
        }
    }

    private static OwnerBundleLimits ParseLimits(JsonElement value)
    {
        CheckObject(value, "limits", ["maxExpressionNodes", "maxExpressionDepth", "maxWitnessesPerEntry"]);
        var nodes = RequireBoundedUnsigned(value.GetProperty("maxExpressionNodes"), "maxExpressionNodes", 1, OwnerBundleLimits.ExpressionNodesHardMaximum);
        var depth = RequireBoundedUnsigned(value.GetProperty("maxExpressionDepth"), "maxExpressionDepth", 1, OwnerBundleLimits.ExpressionDepthHardMaximum);
        var witnesses = RequireBoundedUnsigned(value.GetProperty("maxWitnessesPerEntry"), "maxWitnessesPerEntry", 1, OwnerBundleLimits.WitnessesPerEntryHardMaximum);
        return new OwnerBundleLimits(nodes, depth, witnesses);
    }

    private static ImmutableArray<OwnerModel> ParseModels(JsonElement value, OwnerBundleLimits limits, ref int expressionNodes)
    {
        var items = RequireArray(value, "models").ToArray();
        if (items.Length > StrogoLimits.FunctionsHardMaximum)
            throw ModulesExceptionFactory.Error("owner-parse", "ModelLimitExceeded", details: new { actual = items.Length, max = StrogoLimits.FunctionsHardMaximum });
        PreflightNamedItems(items, "model", "id", ["id", "parameters", "returnType", "body"], "DuplicateModelId");
        var result = ImmutableArray.CreateBuilder<OwnerModel>(items.Length);
        foreach (var item in OrderById(items))
        {
            var id = RequireId(item.GetProperty("id"));
            var parameters = ParseParameters(item.GetProperty("parameters"), $"model/{id}/parameters");
            var returnType = ParseScalarType(item.GetProperty("returnType"), $"model/{id}/returnType");
            var body = ParseExpression(item.GetProperty("body"), $"model/{id}/body", 1, limits, ref expressionNodes);
            result.Add(new OwnerModel(id, parameters, returnType, body));
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<OwnerEntryContract> ParseEntries(JsonElement value, OwnerBundleLimits limits, ref int expressionNodes)
    {
        var items = RequireArray(value, "entryContracts").ToArray();
        if (items.Length > StrogoLimits.FunctionsHardMaximum)
            throw ModulesExceptionFactory.Error("owner-parse", "EntryContractLimitExceeded", details: new { actual = items.Length, max = StrogoLimits.FunctionsHardMaximum });
        PreflightNamedItems(items, "entryContract", "id", ["id", "functionRef", "parameters", "returnType", "requires", "ensures", "effects", "witnesses"], "DuplicateEntryContractId");
        var result = ImmutableArray.CreateBuilder<OwnerEntryContract>(items.Length);
        var functions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in OrderById(items))
        {
            var id = RequireId(item.GetProperty("id"));
            var functionRef = RequireId(item.GetProperty("functionRef"));
            if (!functions.Add(functionRef)) throw ModulesExceptionFactory.Error("owner-parse", "DuplicateEntryFunction", functionRef);
            var parameters = ParseParameters(item.GetProperty("parameters"), $"contract/{id}/parameters");
            var returnType = ParseScalarType(item.GetProperty("returnType"), $"contract/{id}/returnType");
            var requires = ParseExpression(item.GetProperty("requires"), $"contract/{id}/requires", 1, limits, ref expressionNodes);
            var ensures = ParseExpression(item.GetProperty("ensures"), $"contract/{id}/ensures", 1, limits, ref expressionNodes);
            var effectItems = RequireArray(item.GetProperty("effects"), $"contract/{id}/effects").ToArray();
            if (effectItems.Length != 0)
                throw ModulesExceptionFactory.Error("owner-parse", "EffectsNotSupported", id, new { count = effectItems.Length });
            var effects = ImmutableArray<string>.Empty;
            var witnesses = ParseWitnesses(item.GetProperty("witnesses"), parameters, id, limits);
            result.Add(new OwnerEntryContract(id, functionRef, parameters, returnType, requires, ensures, effects, witnesses, string.Empty));
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<FunctionParameter> ParseParameters(JsonElement value, string locus)
    {
        var items = RequireArray(value, locus).ToArray();
        if (items.Length > StrogoLimits.NodesPerFunctionHardMaximum)
            throw ModulesExceptionFactory.Error("owner-parse", "OwnerParameterLimitExceeded", locus, new { actual = items.Length, max = StrogoLimits.NodesPerFunctionHardMaximum });
        PreflightNamedItems(items, locus, "id", ["id", "type"], "DuplicateParameterId", id => $"{locus}/{id}");
        var result = ImmutableArray.CreateBuilder<FunctionParameter>();
        foreach (var item in items)
        {
            var id = RequireId(item.GetProperty("id"));
            result.Add(new FunctionParameter(id, ParseScalarType(item.GetProperty("type"), $"{locus}/{id}")));
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<OwnerWitness> ParseWitnesses(JsonElement value, ImmutableArray<FunctionParameter> parameters, string contractId, OwnerBundleLimits limits)
    {
        var items = RequireArray(value, $"contract/{contractId}/witnesses").ToArray();
        if (items.Length == 0)
            throw ModulesExceptionFactory.Error("owner-parse", "RequiresWitnessRequired", contractId);
        if (items.Length > limits.MaxWitnessesPerEntry)
            throw ModulesExceptionFactory.Error("owner-parse", "WitnessLimitExceeded", contractId, new { actual = items.Length, max = limits.MaxWitnessesPerEntry });
        PreflightNamedItems(items, "witness", "id", ["id", "arguments"], "DuplicateWitnessId", id => $"contract/{contractId}/witness/{id}");
        var result = ImmutableArray.CreateBuilder<OwnerWitness>(items.Length);
        var parameterById = parameters.ToImmutableDictionary(parameter => parameter.Id, StringComparer.Ordinal);
        foreach (var item in OrderById(items))
        {
            var id = RequireId(item.GetProperty("id"));
            var argumentItems = RequireArray(item.GetProperty("arguments"), $"contract/{contractId}/witness/{id}/arguments").ToArray();
            PreflightNamedItems(argumentItems, "witnessArgument", "parameterId", ["parameterId", "value"], "DuplicateWitnessArgument");
            var arguments = ImmutableArray.CreateBuilder<OwnerWitnessArgument>();
            var argumentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var argument in argumentItems.OrderBy(
                         value => value.GetProperty("parameterId").GetString(),
                         StringComparer.Ordinal))
            {
                var parameterId = RequireId(argument.GetProperty("parameterId"));
                argumentIds.Add(parameterId);
                if (!parameterById.TryGetValue(parameterId, out var parameter))
                    throw ModulesExceptionFactory.Error("owner-parse", "UnknownWitnessParameter", parameterId);
                arguments.Add(new OwnerWitnessArgument(parameterId, ParseScalarValue(argument.GetProperty("value"), parameter.Type, $"contract/{contractId}/witness/{id}/{parameterId}")));
            }
            var missing = parameterById.Keys.Except(argumentIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (missing.Length != 0 || arguments.Count != parameters.Length)
                throw ModulesExceptionFactory.Error("owner-parse", "WitnessArgumentsMismatch", $"contract/{contractId}/witness/{id}", new { missing });
            result.Add(new OwnerWitness(id, arguments.OrderBy(argument => argument.ParameterId, StringComparer.Ordinal).ToImmutableArray()));
        }
        return result.ToImmutable();
    }

    private static OwnerScalarValue ParseScalarValue(JsonElement value, TypeRef expectedType, string locus)
    {
        CheckObject(value, locus, ["type", "value"]);
        var actualType = ParseScalarType(value.GetProperty("type"), locus);
        if (actualType.Kind != expectedType.Kind)
            throw ModulesExceptionFactory.Error("owner-parse", "TypeMismatch", locus, new { expected = expectedType.Kind, actual = actualType.Kind });
        return actualType.Kind switch
        {
            "I64" => OwnerScalarValue.FromI64(RequireI64(value.GetProperty("value"), locus)),
            "Bool" => OwnerScalarValue.FromBool(RequireBool(value.GetProperty("value"), locus)),
            _ => throw new InvalidOperationException("Scalar type was validated")
        };
    }

    private static OwnerExpression ParseExpression(JsonElement value, string locus, int depth, OwnerBundleLimits limits, ref int expressionNodes)
    {
        if (depth > limits.MaxExpressionDepth)
            throw ModulesExceptionFactory.Error("owner-parse", "ExpressionDepthExceeded", locus, new { depth, max = limits.MaxExpressionDepth });
        expressionNodes++;
        if (expressionNodes > limits.MaxExpressionNodes)
            throw ModulesExceptionFactory.Error("owner-parse", "ExpressionNodeLimitExceeded", locus, new { actual = expressionNodes, max = limits.MaxExpressionNodes });
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("op", out var opValue))
            throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", locus, new { expected = "expression object with op" });
        var op = RequireString(opValue);
        if (!SupportedExpressionOps.Contains(op))
            throw ModulesExceptionFactory.Error("owner-parse", "UnsupportedContractOpcode", locus, new { op });
        CheckObject(value, locus, ExpressionFields(op));
        var type = ParseScalarType(value.GetProperty("type"), locus);
        string? referenceId = null;
        long? i64 = null;
        bool? boolean = null;
        ImmutableArray<OwnerExpression> args = ImmutableArray<OwnerExpression>.Empty;
        switch (op)
        {
            case "param": referenceId = RequireId(value.GetProperty("id")); break;
            case "model.call":
                referenceId = RequireId(value.GetProperty("modelRef"));
                args = ParseExpressionArray(value.GetProperty("args"), locus, depth, limits, ref expressionNodes);
                break;
            case "i64.const": i64 = RequireI64(value.GetProperty("value"), locus); break;
            case "bool.const": boolean = RequireBool(value.GetProperty("value"), locus); break;
            case "result": break;
            default: args = ParseExpressionArray(value.GetProperty("args"), locus, depth, limits, ref expressionNodes); break;
        }
        return new OwnerExpression(op, type, args, referenceId, i64, boolean);
    }

    private static ImmutableArray<OwnerExpression> ParseExpressionArray(JsonElement value, string locus, int depth, OwnerBundleLimits limits, ref int expressionNodes)
    {
        var builder = ImmutableArray.CreateBuilder<OwnerExpression>();
        var index = 0;
        foreach (var item in RequireArray(value, $"{locus}/args"))
            builder.Add(ParseExpression(item, $"{locus}/arg/{index++}", depth + 1, limits, ref expressionNodes));
        return builder.ToImmutable();
    }

    private static string[] ExpressionFields(string op) => op switch
    {
        "param" => ["op", "type", "id"],
        "result" => ["op", "type"],
        "i64.const" or "bool.const" => ["op", "type", "value"],
        "model.call" => ["op", "type", "modelRef", "args"],
        _ => ["op", "type", "args"]
    };

    private static TypeRef ParseScalarType(JsonElement value, string locus)
    {
        var type = RequireString(value);
        return type switch
        {
            "I64" or "Bool" => new TypeRef(type),
            _ => throw ModulesExceptionFactory.Error("owner-parse", "UnsupportedOwnerType", locus, new { type })
        };
    }

    private static int RequireBoundedUnsigned(JsonElement value, string field, int minimum, int maximum)
    {
        var text = RequireString(value);
        if (!CanonicalUnsignedPattern.IsMatch(text) || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < minimum || parsed > maximum)
            throw ModulesExceptionFactory.Error("owner-parse", "InvalidOwnerLimit", field, new { value = text, minimum, maximum });
        return parsed;
    }

    private static long RequireI64(JsonElement value, string locus)
    {
        var text = RequireString(value);
        if (!CanonicalI64Pattern.IsMatch(text) || !long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
            throw ModulesExceptionFactory.Error("owner-parse", "InvalidI64", locus, new { value = text });
        return parsed;
    }

    private static bool RequireBool(JsonElement value, string locus) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", locus, new { expected = "bool" })
    };

    private static string RequireString(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", details: new { expected = "string" });
        return value.GetString()!;
    }

    private static string RequireId(JsonElement value)
    {
        var id = RequireString(value);
        if (!IdPattern.IsMatch(id))
            throw ModulesExceptionFactory.Error("owner-parse", "InvalidId", details: new { value = id });
        return id;
    }

    private static IEnumerable<JsonElement> RequireArray(JsonElement value, string locus)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", locus, new { expected = "array" });
        return value.EnumerateArray();
    }

    private static IEnumerable<JsonElement> OrderById(IEnumerable<JsonElement> values)
        => values.OrderBy(value => value.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : string.Empty, StringComparer.Ordinal);

    private static void PreflightNamedItems(
        IEnumerable<JsonElement> values,
        string locus,
        string idField,
        IReadOnlyCollection<string> fields,
        string duplicateCode,
        Func<string, string>? entityId = null)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values.OrderBy(StructuralSortKey, StringComparer.Ordinal))
        {
            CheckObject(value, locus, fields);
            var id = RequireId(value.GetProperty(idField));
            if (!ids.Add(id))
                throw ModulesExceptionFactory.Error("owner-parse", duplicateCode, entityId?.Invoke(id) ?? id);
        }
    }

    private static string StructuralSortKey(JsonElement value)
    {
        var builder = new StringBuilder();
        AppendStructuralKey(builder, value);
        return builder.ToString();
    }

    private static void AppendStructuralKey(StringBuilder builder, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('o');
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    AppendKeyPart(builder, property.Name);
                    var child = StructuralSortKey(property.Value);
                    AppendKeyPart(builder, child);
                }
                break;
            case JsonValueKind.Array:
                builder.Append('a');
                foreach (var item in value.EnumerateArray())
                {
                    var child = StructuralSortKey(item);
                    AppendKeyPart(builder, child);
                }
                break;
            case JsonValueKind.String:
                builder.Append('s');
                AppendKeyPart(builder, value.GetString()!);
                break;
            case JsonValueKind.Number:
                builder.Append('n');
                AppendKeyPart(builder, value.GetRawText());
                break;
            case JsonValueKind.True:
                builder.Append('t');
                break;
            case JsonValueKind.False:
                builder.Append('f');
                break;
            case JsonValueKind.Null:
                builder.Append('z');
                break;
            default:
                builder.Append('u');
                break;
        }
    }

    private static void AppendKeyPart(StringBuilder builder, string value)
        => builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);

    private static void CheckObject(JsonElement value, string locus, IReadOnlyCollection<string> expected)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", locus, new { expected = "object" });
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != expected.Count || actual.Any(name => !expected.Contains(name, StringComparer.Ordinal)))
            throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", locus, new { expected, actual = actual.Order(StringComparer.Ordinal).ToArray() });
    }
}
