using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        "param", "i64.const", "bool.const", "i64.add", "i64.sub", "i64.le", "eq",
        "bool.not", "bool.and", "bool.or", "if", "record.make", "record.get",
        "seq.empty", "seq.length", "seq.get", "seq.append");

    public static OwnerBundle Parse(string source) => Parse(Encoding.UTF8.GetBytes(source));

    public static OwnerBundle Parse(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var document = ParseDocument(source, "owner-parse");
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var schemaValue))
            throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", "bundle", new { expected = "object with schemaVersion" });
        var schemaVersion = RequireString(schemaValue);
        if (schemaVersion != OwnerBundleVersions.SchemaVersion)
        {
            var code = schemaVersion == OwnerBundleVersions.PreviousSchemaVersion ? "OwnerBundleMigrationRequired" : "SchemaVersionMismatch";
            throw ModulesExceptionFactory.Error("owner-parse", code, details: new { expected = OwnerBundleVersions.SchemaVersion, actual = schemaVersion });
        }
        CheckObject(root, "bundle", ["schemaVersion", "bundleId", "types", "entryContracts", "models", "limits"]);

        var bundleId = RequireId(root.GetProperty("bundleId"));
        var limits = ParseLimits(root.GetProperty("limits"));
        var types = ParseTypes(root.GetProperty("types"));
        var expressionNodes = 0;
        var models = ParseModels(root.GetProperty("models"), limits, ref expressionNodes);
        var witnessValueNodes = 0;
        var entries = ParseEntries(root.GetProperty("entryContracts"), types, limits, ref expressionNodes, ref witnessValueNodes);
        if (entries.Length == 0)
            throw ModulesExceptionFactory.Error("owner-parse", "EntryContractRequired");
        if (models.Length == 0)
            throw ModulesExceptionFactory.Error("owner-parse", "OwnerModelRequired");

        OwnerContractSemantics.Validate(types, entries, models);
        var canonical = OwnerBundleCodec.Canonicalize(schemaVersion, bundleId, types, entries, models, limits);
        return new OwnerBundle(schemaVersion, bundleId, types, entries, models, limits, canonical);
    }

    internal static JsonDocument ParseDocument(byte[] source, string stage)
    {
        if (source.Length > TransportMaximum)
            throw ModulesExceptionFactory.Error(stage, "TransportLimitExceeded", details: new { actual = source.Length, max = TransportMaximum });
        try
        {
            return CanonicalJson.ParseStrict(Encoding.UTF8.GetString(source), new CoreLimits(
                MaxTransportBytes: TransportMaximum,
                MaxJsonDepth: JsonDepthMaximum));
        }
        catch (KernelException exception)
        {
            throw ModulesExceptionFactory.Error(stage, exception.Error.Code,
                details: new { kernelStage = exception.Error.Stage, kernelDetails = exception.Error.Details });
        }
    }

    private static OwnerBundleLimits ParseLimits(JsonElement value)
    {
        CheckObject(value, "limits", ["maxExpressionNodes", "maxExpressionDepth", "maxWitnessesPerEntry", "maxWitnessValueNodes"]);
        return new OwnerBundleLimits(
            RequireBoundedUnsigned(value.GetProperty("maxExpressionNodes"), "maxExpressionNodes", 1, OwnerBundleLimits.ExpressionNodesHardMaximum),
            RequireBoundedUnsigned(value.GetProperty("maxExpressionDepth"), "maxExpressionDepth", 1, OwnerBundleLimits.ExpressionDepthHardMaximum),
            RequireBoundedUnsigned(value.GetProperty("maxWitnessesPerEntry"), "maxWitnessesPerEntry", 1, OwnerBundleLimits.WitnessesPerEntryHardMaximum),
            RequireBoundedUnsigned(value.GetProperty("maxWitnessValueNodes"), "maxWitnessValueNodes", 1, OwnerBundleLimits.WitnessValueNodesHardMaximum));
    }

    private static ImmutableArray<TypeDecl> ParseTypes(JsonElement value)
    {
        var items = RequireArray(value, "types").ToArray();
        if (items.Length > StrogoLimits.TypesHardMaximum)
            throw ModulesExceptionFactory.Error("owner-parse", "TypeLimitExceeded", details: new { actual = items.Length, max = StrogoLimits.TypesHardMaximum });
        PreflightNamedItems(items, "type", "id", ["id", "fields"], "DuplicateTypeId");
        var result = ImmutableArray.CreateBuilder<TypeDecl>(items.Length);
        foreach (var item in OrderById(items))
        {
            var id = RequireId(item.GetProperty("id"));
            var fields = RequireArray(item.GetProperty("fields"), $"type/{id}/fields").ToArray();
            PreflightNamedItems(fields, $"type/{id}/field", "id", ["id", "type"], "DuplicateFieldId");
            result.Add(new TypeDecl(id, fields.OrderBy(field => field.GetProperty("id").GetString(), StringComparer.Ordinal)
                .Select(field => new RecordField(RequireId(field.GetProperty("id")), ParseType(field.GetProperty("type"), 1, $"type/{id}")))
                .ToImmutableArray()));
        }
        return result.ToImmutable();
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
            result.Add(new OwnerModel(
                id,
                ParseParameters(item.GetProperty("parameters"), $"model/{id}/parameters"),
                ParseType(item.GetProperty("returnType"), 1, $"model/{id}/returnType"),
                ParseExpression(item.GetProperty("body"), $"model/{id}/body", 1, limits, ref expressionNodes)));
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<OwnerEntryContract> ParseEntries(
        JsonElement value,
        ImmutableArray<TypeDecl> types,
        OwnerBundleLimits limits,
        ref int expressionNodes,
        ref int witnessValueNodes)
    {
        var items = RequireArray(value, "entryContracts").ToArray();
        if (items.Length > StrogoLimits.FunctionsHardMaximum)
            throw ModulesExceptionFactory.Error("owner-parse", "EntryContractLimitExceeded", details: new { actual = items.Length, max = StrogoLimits.FunctionsHardMaximum });
        PreflightNamedItems(items, "entryContract", "id", ["id", "functionRef", "parameters", "returnType", "requires", "effects", "witnesses", "modelRef"], "DuplicateEntryContractId");
        var functions = new HashSet<string>(StringComparer.Ordinal);
        var result = ImmutableArray.CreateBuilder<OwnerEntryContract>(items.Length);
        foreach (var item in OrderById(items))
        {
            var id = RequireId(item.GetProperty("id"));
            var functionRef = RequireId(item.GetProperty("functionRef"));
            if (!functions.Add(functionRef))
                throw ModulesExceptionFactory.Error("owner-parse", "DuplicateEntryFunction", functionRef);
            var parameters = ParseParameters(item.GetProperty("parameters"), $"contract/{id}/parameters");
            var effects = RequireArray(item.GetProperty("effects"), $"contract/{id}/effects").ToArray();
            if (effects.Length != 0)
                throw ModulesExceptionFactory.Error("owner-parse", "EffectsNotSupported", id, new { count = effects.Length });
            result.Add(new OwnerEntryContract(
                id,
                functionRef,
                parameters,
                ParseType(item.GetProperty("returnType"), 1, $"contract/{id}/returnType"),
                ParseExpression(item.GetProperty("requires"), $"contract/{id}/requires", 1, limits, ref expressionNodes),
                ImmutableArray<string>.Empty,
                ParseWitnesses(item.GetProperty("witnesses"), parameters, types, id, limits, ref witnessValueNodes),
                RequireId(item.GetProperty("modelRef"))));
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<FunctionParameter> ParseParameters(JsonElement value, string locus)
    {
        var items = RequireArray(value, locus).ToArray();
        if (items.Length > StrogoLimits.NodesPerFunctionHardMaximum)
            throw ModulesExceptionFactory.Error("owner-parse", "OwnerParameterLimitExceeded", locus, new { actual = items.Length, max = StrogoLimits.NodesPerFunctionHardMaximum });
        PreflightNamedItems(items, locus, "id", ["id", "type"], "DuplicateParameterId", id => $"{locus}/{id}");
        return items.Select(item => new FunctionParameter(
            RequireId(item.GetProperty("id")),
            ParseType(item.GetProperty("type"), 1, locus))).ToImmutableArray();
    }

    private static ImmutableArray<OwnerWitness> ParseWitnesses(
        JsonElement value,
        ImmutableArray<FunctionParameter> parameters,
        ImmutableArray<TypeDecl> types,
        string contractId,
        OwnerBundleLimits limits,
        ref int witnessValueNodes)
    {
        var items = RequireArray(value, $"contract/{contractId}/witnesses").ToArray();
        if (items.Length == 0)
            throw ModulesExceptionFactory.Error("owner-parse", "RequiresWitnessRequired", contractId);
        if (items.Length > limits.MaxWitnessesPerEntry)
            throw ModulesExceptionFactory.Error("owner-parse", "WitnessLimitExceeded", contractId, new { actual = items.Length, max = limits.MaxWitnessesPerEntry });
        PreflightNamedItems(items, "witness", "id", ["id", "arguments"], "DuplicateWitnessId", id => $"contract/{contractId}/witness/{id}");
        var typeById = types.ToImmutableDictionary(type => type.Id, StringComparer.Ordinal);
        var parameterById = parameters.ToImmutableDictionary(parameter => parameter.Id, StringComparer.Ordinal);
        var result = ImmutableArray.CreateBuilder<OwnerWitness>(items.Length);
        foreach (var item in OrderById(items))
        {
            var id = RequireId(item.GetProperty("id"));
            var argumentItems = RequireArray(item.GetProperty("arguments"), $"contract/{contractId}/witness/{id}/arguments").ToArray();
            PreflightNamedItems(argumentItems, "witnessArgument", "parameterId", ["parameterId", "value"], "DuplicateWitnessArgument");
            var arguments = ImmutableArray.CreateBuilder<OwnerWitnessArgument>(argumentItems.Length);
            foreach (var argument in argumentItems.OrderBy(item => item.GetProperty("parameterId").GetString(), StringComparer.Ordinal))
            {
                var parameterId = RequireId(argument.GetProperty("parameterId"));
                if (!parameterById.TryGetValue(parameterId, out var parameter))
                    throw ModulesExceptionFactory.Error("owner-parse", "UnknownWitnessParameter", parameterId);
                var locus = $"contract/{contractId}/witness/{id}/{parameterId}";
                arguments.Add(new OwnerWitnessArgument(parameterId,
                    ParseValue(argument.GetProperty("value"), parameter.Type, typeById, locus, limits, ref witnessValueNodes)));
            }
            var actualIds = arguments.Select(argument => argument.ParameterId).ToImmutableHashSet(StringComparer.Ordinal);
            var missing = parameterById.Keys.Except(actualIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (missing.Length != 0 || arguments.Count != parameters.Length)
                throw ModulesExceptionFactory.Error("owner-parse", "WitnessArgumentsMismatch", $"contract/{contractId}/witness/{id}", new { missing });
            result.Add(new OwnerWitness(id, arguments.ToImmutable()));
        }
        return result.ToImmutable();
    }

    private static ModuleValue ParseValue(
        JsonElement value,
        TypeRef expectedType,
        IReadOnlyDictionary<string, TypeDecl> types,
        string locus,
        OwnerBundleLimits limits,
        ref int witnessValueNodes)
    {
        witnessValueNodes++;
        if (witnessValueNodes > limits.MaxWitnessValueNodes)
            throw ModulesExceptionFactory.Error("owner-parse", "WitnessValueNodeLimitExceeded", locus, new { actual = witnessValueNodes, max = limits.MaxWitnessValueNodes });
        CheckObject(value, locus, ["type", "value"]);
        var actualType = ParseType(value.GetProperty("type"), 1, locus);
        EnsureType(expectedType, actualType, locus);
        var payload = value.GetProperty("value");
        return expectedType.Kind switch
        {
            "I64" => new ModuleI64(RequireI64(payload, locus)),
            "Bool" => new ModuleBool(RequireBool(payload, locus)),
            "Seq" when expectedType.Element is not null && expectedType.Capacity is not null =>
                ParseSequenceValue(payload, expectedType, types, locus, limits, ref witnessValueNodes),
            "Record" when expectedType.Name is not null =>
                ParseRecordValue(payload, expectedType.Name, types, locus, limits, ref witnessValueNodes),
            _ => throw ModulesExceptionFactory.Error("owner-parse", "UnsupportedOwnerType", locus, new { type = expectedType.ToString() })
        };
    }

    private static ModuleValue ParseSequenceValue(
        JsonElement payload,
        TypeRef type,
        IReadOnlyDictionary<string, TypeDecl> types,
        string locus,
        OwnerBundleLimits limits,
        ref int witnessValueNodes)
    {
        var items = RequireArray(payload, locus).ToArray();
        if (items.Length > type.Capacity!.Value)
            throw ModulesExceptionFactory.Error("owner-parse", "SequenceCapacityExceeded", locus, new { actual = items.Length, capacity = type.Capacity.Value });
        var values = ImmutableArray.CreateBuilder<ModuleValue>(items.Length);
        for (var index = 0; index < items.Length; index++)
            values.Add(ParseValue(items[index], type.Element!, types, $"{locus}/item/{index}", limits, ref witnessValueNodes));
        return new ModuleSequence(type.Element!, type.Capacity.Value, values.ToImmutable());
    }

    private static ModuleValue ParseRecordValue(
        JsonElement payload,
        string recordType,
        IReadOnlyDictionary<string, TypeDecl> types,
        string locus,
        OwnerBundleLimits limits,
        ref int witnessValueNodes)
    {
        if (!types.TryGetValue(recordType, out var declaration))
            throw ModulesExceptionFactory.Error("owner-parse", "OwnerTypeClosureMissing", recordType);
        var items = RequireArray(payload, locus).ToArray();
        PreflightNamedItems(items, $"{locus}/field", "fieldId", ["fieldId", "value"], "DuplicateWitnessField");
        var fieldById = declaration.Fields.ToImmutableDictionary(field => field.Id, StringComparer.Ordinal);
        var actualIds = items.Select(item => RequireId(item.GetProperty("fieldId"))).ToImmutableHashSet(StringComparer.Ordinal);
        var missing = fieldById.Keys.Except(actualIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var extra = actualIds.Except(fieldById.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length != 0 || extra.Length != 0)
            throw ModulesExceptionFactory.Error("owner-parse", "RecordFieldMismatch", locus, new { missing, extra });
        var values = ImmutableArray.CreateBuilder<KeyValuePair<string, ModuleValue>>(items.Length);
        foreach (var item in items.OrderBy(item => item.GetProperty("fieldId").GetString(), StringComparer.Ordinal))
        {
            var fieldId = RequireId(item.GetProperty("fieldId"));
            values.Add(KeyValuePair.Create(fieldId,
                ParseValue(item.GetProperty("value"), fieldById[fieldId].Type, types, $"{locus}/field/{fieldId}", limits, ref witnessValueNodes)));
        }
        return new ModuleRecord(recordType, values.ToImmutable());
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
        var type = ParseType(value.GetProperty("type"), 1, locus);
        var args = op is "param" or "i64.const" or "bool.const"
            ? ImmutableArray<OwnerExpression>.Empty
            : ParseExpressionArray(value.GetProperty("args"), locus, depth, limits, ref expressionNodes);
        return op switch
        {
            "param" => new OwnerExpression(op, type, args, ReferenceId: RequireId(value.GetProperty("id"))),
            "i64.const" => new OwnerExpression(op, type, args, I64Value: RequireI64(value.GetProperty("value"), locus)),
            "bool.const" => new OwnerExpression(op, type, args, BoolValue: RequireBool(value.GetProperty("value"), locus)),
            "record.make" => new OwnerExpression(op, type, args,
                RecordType: RequireId(value.GetProperty("recordType")),
                FieldIds: ParseIds(value.GetProperty("fieldIds"), locus)),
            "record.get" => new OwnerExpression(op, type, args, ReferenceId: RequireId(value.GetProperty("fieldId"))),
            "seq.empty" => new OwnerExpression(op, type, args,
                ElementType: ParseType(value.GetProperty("elementType"), 1, locus),
                Capacity: RequireBoundedUnsigned(value.GetProperty("capacity"), "capacity", 0, 256)),
            _ => new OwnerExpression(op, type, args)
        };
    }

    private static ImmutableArray<OwnerExpression> ParseExpressionArray(JsonElement value, string locus, int depth, OwnerBundleLimits limits, ref int expressionNodes)
    {
        var result = ImmutableArray.CreateBuilder<OwnerExpression>();
        var index = 0;
        foreach (var item in RequireArray(value, $"{locus}/args"))
            result.Add(ParseExpression(item, $"{locus}/arg/{index++}", depth + 1, limits, ref expressionNodes));
        return result.ToImmutable();
    }

    private static string[] ExpressionFields(string op) => op switch
    {
        "param" => ["op", "type", "id"],
        "i64.const" or "bool.const" => ["op", "type", "value"],
        "record.make" => ["op", "type", "recordType", "fieldIds", "args"],
        "record.get" => ["op", "type", "fieldId", "args"],
        "seq.empty" => ["op", "type", "elementType", "capacity", "args"],
        _ => ["op", "type", "args"]
    };

    private static ImmutableArray<string> ParseIds(JsonElement value, string locus)
    {
        var result = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in RequireArray(value, $"{locus}/fieldIds"))
        {
            var id = RequireId(item);
            if (!seen.Add(id))
                throw ModulesExceptionFactory.Error("owner-parse", "DuplicateFieldId", id);
            result.Add(id);
        }
        return result.ToImmutable();
    }

    internal static TypeRef ParseType(JsonElement value, int depth, string locus)
    {
        if (depth > StrogoLimits.TypeDepthHardMaximum)
            throw ModulesExceptionFactory.Error("owner-parse", "TypeDepthExceeded", locus, new { depth, max = StrogoLimits.TypeDepthHardMaximum });
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = RequireId(value);
            return text is "I64" or "Bool" ? new TypeRef(text) : new TypeRef("Record", Name: text);
        }
        CheckObject(value, locus, ["kind", "elementType", "capacity"]);
        if (RequireString(value.GetProperty("kind")) != "Seq")
            throw ModulesExceptionFactory.Error("owner-parse", "UnsupportedTypeKind", locus);
        var element = ParseType(value.GetProperty("elementType"), depth + 1, locus);
        if (element.Kind == "Seq")
            throw ModulesExceptionFactory.Error("owner-parse", "NestedSequenceNotSupported", locus);
        return new TypeRef("Seq", Element: element, Capacity: RequireBoundedUnsigned(value.GetProperty("capacity"), "capacity", 0, 256));
    }

    internal static void EnsureType(TypeRef expected, TypeRef actual, string locus)
    {
        if (!ModulesParser.TypesEquivalent(expected, actual))
            throw ModulesExceptionFactory.Error("owner-parse", "ContractTypeMismatch", locus, new { expected = expected.ToString(), actual = actual.ToString() });
    }

    internal static void CheckObject(JsonElement obj, string locus, IReadOnlyCollection<string> requiredFields)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", locus, new { expected = "object" });
        var actual = obj.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        var expected = requiredFields.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", locus, new { expected, actual });
    }

    private static string RequireString(JsonElement value)
        => value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", details: new { expected = "string" });

    private static string RequireId(JsonElement value)
    {
        var id = RequireString(value);
        if (!IdPattern.IsMatch(id))
            throw ModulesExceptionFactory.Error("owner-parse", "InvalidId", details: new { value = id });
        return id;
    }

    private static JsonElement.ArrayEnumerator RequireArray(JsonElement value, string locus)
        => value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", locus, new { expected = "array" });

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

    private static void PreflightNamedItems(
        JsonElement[] items,
        string locus,
        string idField,
        IReadOnlyCollection<string> fields,
        string duplicateCode,
        Func<string, string>? entityId = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.OrderBy(StructuralSortKey, StringComparer.Ordinal))
        {
            CheckObject(item, locus, fields);
            var id = RequireId(item.GetProperty(idField));
            if (!seen.Add(id))
                throw ModulesExceptionFactory.Error("owner-parse", duplicateCode, entityId?.Invoke(id) ?? id);
        }
    }

    private static IEnumerable<JsonElement> OrderById(IEnumerable<JsonElement> items)
        => items.OrderBy(item => item.GetProperty("id").GetString(), StringComparer.Ordinal);

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
                    AppendKeyPart(builder, StructuralSortKey(property.Value));
                }
                break;
            case JsonValueKind.Array:
                builder.Append('a');
                foreach (var item in value.EnumerateArray()) AppendKeyPart(builder, StructuralSortKey(item));
                break;
            case JsonValueKind.String:
                builder.Append('s');
                AppendKeyPart(builder, value.GetString()!);
                break;
            case JsonValueKind.Number:
                builder.Append('n');
                AppendKeyPart(builder, value.GetRawText());
                break;
            case JsonValueKind.True: builder.Append('t'); break;
            case JsonValueKind.False: builder.Append('f'); break;
            case JsonValueKind.Null: builder.Append('z'); break;
            default: builder.Append('u'); break;
        }
    }

    private static void AppendKeyPart(StringBuilder builder, string value)
        => builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
}

public static class OwnerBundleMigrator
{
    public static byte[] MigrateV02ToV03(ReadOnlySpan<byte> source)
    {
        var bytes = source.ToArray();
        using var document = OwnerBundleParser.ParseDocument(bytes, "owner-migrate");
        var rootElement = document.RootElement;
        OwnerBundleParser.CheckObject(rootElement, "bundle", ["schemaVersion", "bundleId", "entryContracts", "models", "limits"]);
        var schema = rootElement.GetProperty("schemaVersion").GetString();
        if (schema != OwnerBundleVersions.PreviousSchemaVersion)
            throw ModulesExceptionFactory.Error("owner-migrate", "SchemaVersionMismatch", details: new { expected = OwnerBundleVersions.PreviousSchemaVersion, actual = schema });

        var valueNodes = 0;
        foreach (var entry in rootElement.GetProperty("entryContracts").EnumerateArray())
            foreach (var witness in entry.GetProperty("witnesses").EnumerateArray())
                foreach (var argument in witness.GetProperty("arguments").EnumerateArray())
                    valueNodes += CountLegacyScalarValue(argument.GetProperty("value"));
        if (valueNodes > OwnerBundleLimits.WitnessValueNodesHardMaximum)
            throw ModulesExceptionFactory.Error("owner-migrate", "MigrationWitnessValueLimitExceeded", details: new { actual = valueNodes, max = OwnerBundleLimits.WitnessValueNodesHardMaximum });

        var root = JsonNode.Parse(bytes)!.AsObject();
        root["schemaVersion"] = OwnerBundleVersions.SchemaVersion;
        root["types"] = new JsonArray();
        foreach (var entry in root["entryContracts"]!.AsArray().Select(node => node!.AsObject()))
        {
            var modelRef = ExtractLegacyModelRef(entry);
            entry.Remove("ensures");
            entry["modelRef"] = modelRef;
        }
        root["limits"]!.AsObject()["maxWitnessValueNodes"] = OwnerBundleLimits.WitnessValueNodesHardMaximum.ToString(CultureInfo.InvariantCulture);
        return OwnerBundleParser.Parse(Encoding.UTF8.GetBytes(root.ToJsonString())).CanonicalBytes;
    }

    private static int CountLegacyScalarValue(JsonElement value)
    {
        OwnerBundleParser.CheckObject(value, "legacy/value", ["type", "value"]);
        if (value.GetProperty("type").ValueKind != JsonValueKind.String || value.GetProperty("type").GetString() is not ("I64" or "Bool"))
            throw ModulesExceptionFactory.Error("owner-migrate", "UnsupportedOwnerType", "legacy/value");
        return 1;
    }

    private static string ExtractLegacyModelRef(JsonObject entry)
    {
        var ensures = entry["ensures"]?.AsObject() ?? throw ModulesExceptionFactory.Error("owner-migrate", "ExactOutcomeRequired");
        EnsureNodeFields(ensures, ["op", "type", "args"]);
        var args = ensures["args"]?.AsArray();
        if (ensures["op"]?.GetValue<string>() != "eq" || ensures["type"]?.GetValue<string>() != "Bool" || args is null || args.Count != 2)
            throw ModulesExceptionFactory.Error("owner-migrate", "ExactOutcomeRequired");
        var result = args[0]?.AsObject();
        var call = args[1]?.AsObject();
        if (result is null || call is null)
            throw ModulesExceptionFactory.Error("owner-migrate", "ExactOutcomeRequired");
        EnsureNodeFields(result, ["op", "type"]);
        EnsureNodeFields(call, ["op", "type", "modelRef", "args"]);
        var returnType = entry["returnType"];
        if (result["op"]?.GetValue<string>() != "result" || !JsonNode.DeepEquals(result["type"], returnType)
            || call["op"]?.GetValue<string>() != "model.call" || !JsonNode.DeepEquals(call["type"], returnType))
            throw ModulesExceptionFactory.Error("owner-migrate", "ExactOutcomeRequired");
        var parameters = entry["parameters"]!.AsArray();
        var callArgs = call["args"]?.AsArray();
        if (callArgs is null || callArgs.Count != parameters.Count)
            throw ModulesExceptionFactory.Error("owner-migrate", "ExactOutcomeArgumentsMismatch");
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index]!.AsObject();
            var argument = callArgs[index]!.AsObject();
            EnsureNodeFields(argument, ["op", "type", "id"]);
            if (argument["op"]?.GetValue<string>() != "param"
                || argument["id"]?.GetValue<string>() != parameter["id"]?.GetValue<string>()
                || !JsonNode.DeepEquals(argument["type"], parameter["type"]))
                throw ModulesExceptionFactory.Error("owner-migrate", "ExactOutcomeArgumentsMismatch");
        }
        return call["modelRef"]?.GetValue<string>()
            ?? throw ModulesExceptionFactory.Error("owner-migrate", "ExactOutcomeRequired");
    }

    private static void EnsureNodeFields(JsonObject value, IEnumerable<string> expected)
    {
        var actualFields = value.Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
        var expectedFields = expected.Order(StringComparer.Ordinal).ToArray();
        if (!actualFields.SequenceEqual(expectedFields, StringComparer.Ordinal))
            throw ModulesExceptionFactory.Error("owner-migrate", "ExactOutcomeRequired");
    }
}
