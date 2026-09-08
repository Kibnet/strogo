using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Strogo.Modules;

public static class OwnerBundleV04Parser
{
    private static readonly ImmutableHashSet<string> ModelOps = ImmutableHashSet.Create(StringComparer.Ordinal,
        "param", "i64.const", "bool.const", "i64.add", "i64.sub", "i64.le", "eq",
        "bool.not", "bool.and", "bool.or", "if", "record.make", "record.get",
        "seq.empty", "seq.length", "seq.get", "seq.append", "fold");
    private static readonly ImmutableHashSet<string> ProofOps = ImmutableHashSet.Create(StringComparer.Ordinal,
        "param", "proof.bound", "i64.const", "bool.const", "math.const", "eq", "i64.add", "i64.sub", "i64.le", "math.le",
        "bool.not", "bool.and", "bool.or", "if", "math.from_i64", "math.add", "math.sub", "record.make", "record.get",
        "seq.empty", "seq.length", "seq.get", "seq.append", "seq.sum_i64", "seq.prefix_sum_i64", "forall.sequence");

    public static OwnerBundleV04 Parse(string source) => Parse(Encoding.UTF8.GetBytes(source));

    public static OwnerBundleV04 Parse(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var document = OwnerBundleParser.ParseDocument(source, "owner-parse");
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var schemaValue))
            throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", "bundle", new { expected = "object with schemaVersion" });
        var schema = OwnerBundleParser.RequireString(schemaValue);
        if (schema != OwnerBundleVersions.SchemaVersionV04)
        {
            var code = schema == OwnerBundleVersions.SchemaVersion ? "OwnerBundleMigrationRequired" : "SchemaVersionMismatch";
            throw ModulesExceptionFactory.Error("owner-parse", code, details: new { expected = OwnerBundleVersions.SchemaVersionV04, actual = schema });
        }
        OwnerBundleParser.CheckObject(root, "bundle", ["schemaVersion", "bundleId", "types", "entryContracts", "models", "limits"]);

        var bundleId = OwnerBundleParser.RequireId(root.GetProperty("bundleId"));
        var limits = ParseLimits(root.GetProperty("limits"));
        var legacyLimits = new OwnerBundleLimits(limits.MaxExpressionNodes, limits.MaxExpressionDepth, limits.MaxWitnessesPerEntry, limits.MaxWitnessValueNodes);
        var types = OwnerBundleParser.ParseTypes(root.GetProperty("types"));
        if (types.Any(type => type.Id == "MathInt"))
            throw ModulesExceptionFactory.Error("owner-parse", "ReservedTypeId", "MathInt");
        var expressionNodes = 0;
        var models = ParseModels(root.GetProperty("models"), limits, ref expressionNodes);
        var witnessValueNodes = 0;
        var entries = ParseEntries(root.GetProperty("entryContracts"), types, limits, legacyLimits, ref expressionNodes, ref witnessValueNodes);
        if (entries.IsEmpty) throw ModulesExceptionFactory.Error("owner-parse", "EntryContractRequired");
        if (models.IsEmpty) throw ModulesExceptionFactory.Error("owner-parse", "OwnerModelRequired");

        OwnerV04Semantics.Validate(types, entries, models, limits);
        var canonical = OwnerBundleV04Codec.Canonicalize(bundleId, types, entries, models, limits);
        return new OwnerBundleV04(bundleId, types, entries, models, limits, canonical);
    }

    private static OwnerBundleLimitsV04 ParseLimits(JsonElement value)
    {
        OwnerBundleParser.CheckObject(value, "limits", ["maxExpressionNodes", "maxExpressionDepth", "maxWitnessesPerEntry", "maxWitnessValueNodes", "maxProofEvaluationSteps"]);
        return new OwnerBundleLimitsV04(
            OwnerBundleParser.RequireBoundedUnsigned(value.GetProperty("maxExpressionNodes"), "maxExpressionNodes", 1, OwnerBundleLimits.ExpressionNodesHardMaximum),
            OwnerBundleParser.RequireBoundedUnsigned(value.GetProperty("maxExpressionDepth"), "maxExpressionDepth", 1, OwnerBundleLimits.ExpressionDepthHardMaximum),
            OwnerBundleParser.RequireBoundedUnsigned(value.GetProperty("maxWitnessesPerEntry"), "maxWitnessesPerEntry", 1, OwnerBundleLimits.WitnessesPerEntryHardMaximum),
            OwnerBundleParser.RequireBoundedUnsigned(value.GetProperty("maxWitnessValueNodes"), "maxWitnessValueNodes", 1, OwnerBundleLimits.WitnessValueNodesHardMaximum),
            OwnerBundleParser.RequireBoundedUnsigned(value.GetProperty("maxProofEvaluationSteps"), "maxProofEvaluationSteps", 1, OwnerBundleLimitsV04.ProofEvaluationStepsHardMaximum));
    }

    private static ImmutableArray<OwnerModelV04> ParseModels(JsonElement value, OwnerBundleLimitsV04 limits, ref int nodes)
    {
        var items = OwnerBundleParser.RequireArray(value, "models").ToArray();
        if (items.Length > StrogoLimits.FunctionsHardMaximum)
            throw ModulesExceptionFactory.Error("owner-parse", "ModelLimitExceeded", details: new { actual = items.Length, max = StrogoLimits.FunctionsHardMaximum });
        OwnerBundleParser.PreflightNamedItems(items, "model", "id", ["id", "parameters", "returnType", "body"], "DuplicateModelId");
        var result = ImmutableArray.CreateBuilder<OwnerModelV04>();
        foreach (var item in OwnerBundleParser.OrderById(items))
        {
            var id = OwnerBundleParser.RequireId(item.GetProperty("id"));
            result.Add(new OwnerModelV04(
                id,
                OwnerBundleParser.ParseParameters(item.GetProperty("parameters"), $"model/{id}/parameters"),
                ParseExecutableType(item.GetProperty("returnType"), $"model/{id}/returnType"),
                ParseModelExpression(item.GetProperty("body"), $"model/{id}/body", 1, limits, ref nodes, allowFold: true)));
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<OwnerEntryContractV04> ParseEntries(
        JsonElement value,
        ImmutableArray<TypeDecl> types,
        OwnerBundleLimitsV04 limits,
        OwnerBundleLimits legacyLimits,
        ref int expressionNodes,
        ref int witnessValueNodes)
    {
        var items = OwnerBundleParser.RequireArray(value, "entryContracts").ToArray();
        if (items.Length > StrogoLimits.FunctionsHardMaximum)
            throw ModulesExceptionFactory.Error("owner-parse", "EntryContractLimitExceeded", details: new { actual = items.Length, max = StrogoLimits.FunctionsHardMaximum });
        OwnerBundleParser.PreflightNamedItems(items, "entryContract", "id", ["id", "functionRef", "parameters", "returnType", "requires", "effects", "witnesses", "modelRef"], "DuplicateEntryContractId");
        var functions = new HashSet<string>(StringComparer.Ordinal);
        var result = ImmutableArray.CreateBuilder<OwnerEntryContractV04>();
        foreach (var item in OwnerBundleParser.OrderById(items))
        {
            var id = OwnerBundleParser.RequireId(item.GetProperty("id"));
            var functionRef = OwnerBundleParser.RequireId(item.GetProperty("functionRef"));
            if (!functions.Add(functionRef)) throw ModulesExceptionFactory.Error("owner-parse", "DuplicateEntryFunction", functionRef);
            var parameters = OwnerBundleParser.ParseParameters(item.GetProperty("parameters"), $"contract/{id}/parameters");
            var effects = OwnerBundleParser.RequireArray(item.GetProperty("effects"), $"contract/{id}/effects").ToArray();
            if (effects.Length != 0) throw ModulesExceptionFactory.Error("owner-parse", "EffectsNotSupported", id, new { count = effects.Length });
            var requires = ParseProofExpression(item.GetProperty("requires"), $"contract/{id}/requires", 1, limits, ref expressionNodes);
            result.Add(new OwnerEntryContractV04(
                id,
                functionRef,
                parameters,
                ParseExecutableType(item.GetProperty("returnType"), $"contract/{id}/returnType"),
                requires,
                ImmutableArray<string>.Empty,
                OwnerBundleParser.ParseWitnesses(item.GetProperty("witnesses"), parameters, types, id, legacyLimits, ref witnessValueNodes),
                OwnerBundleParser.RequireId(item.GetProperty("modelRef"))));
        }
        return result.ToImmutable();
    }

    private static OwnerExpression ParseModelExpression(JsonElement value, string locus, int depth, OwnerBundleLimitsV04 limits, ref int nodes, bool allowFold)
    {
        CountExpression(locus, depth, limits, ref nodes);
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("op", out var opValue))
            throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", locus, new { expected = "expression object with op" });
        var op = OwnerBundleParser.RequireString(opValue);
        if (!ModelOps.Contains(op)) throw ModulesExceptionFactory.Error("owner-parse", "UnsupportedContractOpcode", locus, new { op });
        if (op == "fold" && !allowFold) throw ModulesExceptionFactory.Error("owner-parse", "NestedFoldNotSupported", locus);
        OwnerBundleParser.CheckObject(value, locus, ModelFields(op));
        var type = ParseExecutableType(value.GetProperty("type"), locus);
        var args = op is "param" or "i64.const" or "bool.const"
            ? ImmutableArray<OwnerExpression>.Empty
            : ParseModelArgs(value.GetProperty("args"), locus, depth, limits, ref nodes);
        return op switch
        {
            "param" => new OwnerExpression(op, type, args, ReferenceId: OwnerBundleParser.RequireId(value.GetProperty("id"))),
            "i64.const" => new OwnerExpression(op, type, args, I64Value: OwnerBundleParser.RequireI64(value.GetProperty("value"), locus)),
            "bool.const" => new OwnerExpression(op, type, args, BoolValue: OwnerBundleParser.RequireBool(value.GetProperty("value"), locus)),
            "record.make" => new OwnerExpression(op, type, args, RecordType: OwnerBundleParser.RequireId(value.GetProperty("recordType")), FieldIds: ParseIds(value.GetProperty("fieldIds"), locus)),
            "record.get" => new OwnerExpression(op, type, args, ReferenceId: OwnerBundleParser.RequireId(value.GetProperty("fieldId"))),
            "seq.empty" => new OwnerExpression(op, type, args, ElementType: ParseExecutableType(value.GetProperty("elementType"), locus), Capacity: OwnerBundleParser.RequireBoundedUnsigned(value.GetProperty("capacity"), "capacity", 0, 256)),
            "fold" => new OwnerExpression(op, type, args, FoldStep: ParseFoldStep(value.GetProperty("step"), locus, depth, limits, ref nodes)),
            _ => new OwnerExpression(op, type, args)
        };
    }

    private static OwnerFoldStep ParseFoldStep(JsonElement value, string locus, int depth, OwnerBundleLimitsV04 limits, ref int nodes)
    {
        OwnerBundleParser.CheckObject(value, $"{locus}/step", ["parameters", "body"]);
        var parameters = OwnerBundleParser.ParseParameters(value.GetProperty("parameters"), $"{locus}/step/parameters");
        var body = ParseModelExpression(value.GetProperty("body"), $"{locus}/step/body", depth + 1, limits, ref nodes, allowFold: false);
        return new OwnerFoldStep(parameters, body);
    }

    private static ImmutableArray<OwnerExpression> ParseModelArgs(JsonElement value, string locus, int depth, OwnerBundleLimitsV04 limits, ref int nodes)
    {
        var result = ImmutableArray.CreateBuilder<OwnerExpression>();
        var index = 0;
        foreach (var item in OwnerBundleParser.RequireArray(value, $"{locus}/args"))
            result.Add(ParseModelExpression(item, $"{locus}/arg/{index++}", depth + 1, limits, ref nodes, allowFold: false));
        return result.ToImmutable();
    }

    private static ProofExpression ParseProofExpression(JsonElement value, string locus, int depth, OwnerBundleLimitsV04 limits, ref int nodes)
    {
        CountExpression(locus, depth, limits, ref nodes);
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("op", out var opValue))
            throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", locus, new { expected = "proof expression object with op" });
        var op = OwnerBundleParser.RequireString(opValue);
        if (!ProofOps.Contains(op)) throw ModulesExceptionFactory.Error("owner-parse", "UnsupportedProofOpcode", locus, new { opcode = op });
        OwnerBundleParser.CheckObject(value, locus, ProofFields(op));
        var type = ParseProofType(value.GetProperty("type"), locus);
        var args = op is "param" or "proof.bound" or "i64.const" or "bool.const" or "math.const"
            ? ImmutableArray<ProofExpression>.Empty
            : op == "forall.sequence" ? ImmutableArray<ProofExpression>.Empty : ParseProofArgs(value.GetProperty("args"), locus, depth, limits, ref nodes);
        return op switch
        {
            "param" => new ProofExpression(op, type, args, ReferenceId: OwnerBundleParser.RequireId(value.GetProperty("id"))),
            "proof.bound" => new ProofExpression(op, type, args, BinderId: OwnerBundleParser.RequireId(value.GetProperty("binderId"))),
            "i64.const" => new ProofExpression(op, type, args, NumberValue: OwnerBundleParser.RequireI64(value.GetProperty("value"), locus).ToString(CultureInfo.InvariantCulture)),
            "math.const" => new ProofExpression(op, type, args, NumberValue: ParseMathInteger(value.GetProperty("value"), locus)),
            "bool.const" => new ProofExpression(op, type, args, BoolValue: OwnerBundleParser.RequireBool(value.GetProperty("value"), locus)),
            "record.make" => new ProofExpression(op, type, args, RecordType: OwnerBundleParser.RequireId(value.GetProperty("recordType")), FieldIds: ParseIds(value.GetProperty("fieldIds"), locus)),
            "record.get" => new ProofExpression(op, type, args, ReferenceId: OwnerBundleParser.RequireId(value.GetProperty("fieldId"))),
            "seq.empty" => new ProofExpression(op, type, args, ElementType: ParseExecutableType(value.GetProperty("elementType"), locus), Capacity: OwnerBundleParser.RequireBoundedUnsigned(value.GetProperty("capacity"), "capacity", 0, 256)),
            "forall.sequence" => new ProofExpression(op, type, args,
                BinderId: OwnerBundleParser.RequireId(value.GetProperty("binderId")),
                Sequence: ParseProofExpression(value.GetProperty("sequence"), $"{locus}/sequence", depth + 1, limits, ref nodes),
                Body: ParseProofExpression(value.GetProperty("body"), $"{locus}/body", depth + 1, limits, ref nodes)),
            _ => new ProofExpression(op, type, args)
        };
    }

    private static ImmutableArray<ProofExpression> ParseProofArgs(JsonElement value, string locus, int depth, OwnerBundleLimitsV04 limits, ref int nodes)
    {
        var result = ImmutableArray.CreateBuilder<ProofExpression>();
        var index = 0;
        foreach (var item in OwnerBundleParser.RequireArray(value, $"{locus}/args"))
            result.Add(ParseProofExpression(item, $"{locus}/arg/{index++}", depth + 1, limits, ref nodes));
        return result.ToImmutable();
    }

    private static void CountExpression(string locus, int depth, OwnerBundleLimitsV04 limits, ref int nodes)
    {
        if (depth > limits.MaxExpressionDepth)
            throw ModulesExceptionFactory.Error("owner-parse", "ExpressionDepthExceeded", locus, new { depth, max = limits.MaxExpressionDepth });
        nodes++;
        if (nodes > limits.MaxExpressionNodes)
            throw ModulesExceptionFactory.Error("owner-parse", "ExpressionNodeLimitExceeded", locus, new { actual = nodes, max = limits.MaxExpressionNodes });
    }

    internal static int ProofWorstCaseCost(ProofExpression expression)
    {
        const int saturation = OwnerBundleLimitsV04.ProofEvaluationStepsHardMaximum + 1;
        int Add(int left, int right) => left >= saturation - right ? saturation : left + right;
        int Multiply(int left, int right) => left == 0 || right == 0 ? 0 : left > saturation / right ? saturation : left * right;
        var cost = 1;
        foreach (var argument in expression.Args) cost = Add(cost, ProofWorstCaseCost(argument));
        if (expression.Sequence is not null) cost = Add(cost, ProofWorstCaseCost(expression.Sequence));
        if (expression.Body is not null && expression.Op != "forall.sequence") cost = Add(cost, ProofWorstCaseCost(expression.Body));
        if (expression.Op is "seq.sum_i64" or "seq.prefix_sum_i64") cost = Add(cost, expression.Args.FirstOrDefault()?.Type.Capacity ?? 0);
        if (expression.Op == "forall.sequence")
        {
            var capacity = expression.Sequence!.Type.Capacity ?? 0;
            cost = Add(cost, Multiply(capacity, Add(1, ProofWorstCaseCost(expression.Body!))));
        }
        return cost;
    }

    private static TypeRef ParseExecutableType(JsonElement value, string locus)
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString() == "MathInt")
            throw ModulesExceptionFactory.Error("owner-parse", "UnsupportedOwnerType", locus, new { type = "MathInt" });
        return OwnerBundleParser.ParseType(value, 1, locus);
    }

    private static TypeRef ParseProofType(JsonElement value, string locus)
        => value.ValueKind == JsonValueKind.String && value.GetString() == "MathInt"
            ? new TypeRef("MathInt")
            : OwnerBundleParser.ParseType(value, 1, locus);

    private static string ParseMathInteger(JsonElement value, string locus)
    {
        var text = OwnerBundleParser.RequireString(value);
        if (text.Length == 0 || text == "-0" || text[0] == '+' || text.Length > 1 && text[0] == '0' || text[0] == '-' && (text.Length == 1 || text[1] == '0') || !BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", locus, new { reason = "InvalidCanonicalInteger", value = text });
        return text;
    }

    private static ImmutableArray<string> ParseIds(JsonElement value, string locus)
    {
        var result = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in OwnerBundleParser.RequireArray(value, $"{locus}/fieldIds"))
        {
            var id = OwnerBundleParser.RequireId(item);
            if (!seen.Add(id)) throw ModulesExceptionFactory.Error("owner-parse", "DuplicateFieldId", id);
            result.Add(id);
        }
        return result.ToImmutable();
    }

    private static string[] ModelFields(string op) => op switch
    {
        "param" => ["op", "type", "id"],
        "i64.const" or "bool.const" => ["op", "type", "value"],
        "record.make" => ["op", "type", "recordType", "fieldIds", "args"],
        "record.get" => ["op", "type", "fieldId", "args"],
        "seq.empty" => ["op", "type", "elementType", "capacity", "args"],
        "fold" => ["op", "type", "args", "step"],
        _ => ["op", "type", "args"]
    };

    private static string[] ProofFields(string op) => op switch
    {
        "param" => ["op", "type", "id"],
        "proof.bound" => ["op", "type", "binderId"],
        "i64.const" or "math.const" or "bool.const" => ["op", "type", "value"],
        "record.make" => ["op", "type", "recordType", "fieldIds", "args"],
        "record.get" => ["op", "type", "fieldId", "args"],
        "seq.empty" => ["op", "type", "elementType", "capacity", "args"],
        "forall.sequence" => ["op", "type", "binderId", "sequence", "body"],
        _ => ["op", "type", "args"]
    };
}
