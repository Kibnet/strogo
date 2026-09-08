using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kernel.Core;

namespace Strogo.Modules;

public static class ModulesParser
{
    private sealed record FunctionHeader(string Id, ImmutableArray<FunctionParameter> Parameters, TypeRef ReturnType, string ContractRef, JsonElement Body);

    private static readonly Regex IdPattern = new("^[A-Za-z][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex CanonicalI64Pattern = new("^(0|-?[1-9][0-9]*)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly ImmutableHashSet<string> SupportedOpcodes = ImmutableHashSet.Create(StringComparer.Ordinal,
        "i64.const", "bool.const", "i64.add", "i64.sub", "i64.le", "i64.eq", "bool.not", "bool.and", "bool.or",
        "record.make", "record.get", "seq.empty", "seq.length", "seq.get", "seq.append", "if", "call", "fold");
    private static readonly ImmutableHashSet<string> SupportedProofOpcodes = ImmutableHashSet.Create(StringComparer.Ordinal,
        "param", "fold.prefixLength", "fold.sequence", "fold.initialAccumulator", "fold.accumulator", "fold.environment", "proof.bound",
        "i64.const", "bool.const", "math.const", "eq", "i64.add", "i64.sub", "i64.le", "math.le", "bool.not", "bool.and", "bool.or", "if",
        "math.from_i64", "math.add", "math.sub", "record.make", "record.get", "seq.empty", "seq.length", "seq.get", "seq.append",
        "seq.sum_i64", "seq.prefix_sum_i64", "forall.sequence");
    private const int ProofExpressionNodesHardMaximum = 1024;
    private const int ProofExpressionDepthHardMaximum = 32;
    private const int ProofEvaluationStepsHardMaximum = 262144;

    public static ModuleParseResult ParseModule(string source, StrogoLimits? limits = null)
        => ParseModule(Encoding.UTF8.GetBytes(source), limits);

    public static ModuleParseResult ParseModule(byte[] source, StrogoLimits? limits = null)
    {
        limits ??= new StrogoLimits();
        ValidateLimits(limits);
        var lexed = ModuleLexer.Lex(source, limits.MaxTransportBytes, limits.MaxJsonDepth);
        JsonDocument doc;
        try
        {
            doc = CanonicalJson.ParseStrict(Encoding.UTF8.GetString(source), new CoreLimits
            {
                MaxTransportBytes = limits.MaxTransportBytes,
                MaxJsonDepth = limits.MaxJsonDepth,
                MaxNodes = Math.Min(limits.MaxTotalNodes, 128),
                SolverTimeoutMilliseconds = 5000,
                AdmissionTimeoutMilliseconds = 15000
            });
        }
        catch (KernelException exception)
        {
            throw ModulesExceptionFactory.Error("parse", exception.Error.Code,
                details: new { kernelStage = exception.Error.Stage, kernelDetails = exception.Error.Details });
        }
        using (doc)
        {
            var module = ParseModule(doc.RootElement, limits);
            var canonical = ModulesCodec.Canonicalize(module);
            return new ModuleParseResult(module, lexed, canonical, ModulesCodec.SourceDigest(canonical), "parse");
        }
    }

    private static ModuleSource ParseModule(JsonElement root, StrogoLimits limits)
    {
        CheckObject(root, "module", ["schemaVersion", "moduleId", "types", "imports", "functions", "exports"]);
        var schemaVersion = RequireString(root.GetProperty("schemaVersion"));
        if (schemaVersion != StrogoVersions.SchemaVersion)
            throw ModulesExceptionFactory.Error("parse", "SchemaVersionMismatch", details: new { expected = StrogoVersions.SchemaVersion, actual = schemaVersion });

        var moduleId = RequireId(root.GetProperty("moduleId"));
        var types = ParseTypes(root.GetProperty("types"), limits);
        var typeById = types.ToImmutableDictionary(type => type.Id, StringComparer.Ordinal);
        ValidateTypeDeclarations(types, typeById, limits);
        var imports = ParseImports(root.GetProperty("imports"), limits);
        var functions = ParseFunctions(root.GetProperty("functions"), typeById, limits);
        var exports = ParseExports(root.GetProperty("exports"), functions.Select(function => function.Id).ToImmutableHashSet(StringComparer.Ordinal), limits);
        return new ModuleSource(schemaVersion, moduleId, types, imports, functions, exports);
    }

    private static ImmutableArray<TypeDecl> ParseTypes(JsonElement value, StrogoLimits limits)
    {
        RequireArray(value, "types");
        var raw = value.EnumerateArray().ToArray();
        if (raw.Length > limits.MaxTypes)
            throw ModulesExceptionFactory.Error("parse", "TypeLimitExceeded", details: new { actual = raw.Length, max = limits.MaxTypes });

        var result = ImmutableArray.CreateBuilder<TypeDecl>(raw.Length);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in OrderByEntityId(raw))
        {
            CheckObject(item, "type", ["id", "fields"]);
            var id = RequireId(item.GetProperty("id"));
            if (!ids.Add(id))
                throw ModulesExceptionFactory.Error("parse", "DuplicateTypeId", id);

            var fieldsElement = item.GetProperty("fields");
            RequireArray(fieldsElement, $"type.{id}.fields");
            var fields = ImmutableArray.CreateBuilder<RecordField>();
            var fieldIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in OrderByEntityId(fieldsElement.EnumerateArray()))
            {
                CheckObject(field, "type.field", ["id", "type"]);
                var fieldId = RequireId(field.GetProperty("id"));
                if (!fieldIds.Add(fieldId))
                    throw ModulesExceptionFactory.Error("parse", "DuplicateFieldId", fieldId, new { type = id, field = fieldId });
                fields.Add(new RecordField(fieldId, ParseType(field.GetProperty("type"), 1, limits)));
            }
            result.Add(new TypeDecl(id, fields.ToImmutable()));
        }
        return result.ToImmutable();
    }

    private static void ValidateTypeDeclarations(ImmutableArray<TypeDecl> types, IReadOnlyDictionary<string, TypeDecl> typeById, StrogoLimits limits)
    {
        var typeIds = typeById.Keys.ToImmutableHashSet(StringComparer.Ordinal);
        foreach (var type in types)
            foreach (var field in type.Fields)
                EnsureTypeRef(field.Type, typeIds, $"type.{type.Id}.{field.Id}");

        var states = new Dictionary<string, int>(StringComparer.Ordinal);
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var type in types.OrderBy(type => type.Id, StringComparer.Ordinal))
            Visit(type.Id);

        int Visit(string id)
        {
            if (states.TryGetValue(id, out var state))
            {
                if (state == 1)
                    throw ModulesExceptionFactory.Error("parse", "RecursiveType", id);
                return depths[id];
            }
            states[id] = 1;
            var depth = 1;
            foreach (var dependency in RecordDependencies(typeById[id]).OrderBy(value => value.Id, StringComparer.Ordinal))
                depth = Math.Max(depth, checked(dependency.EdgeDepth + Visit(dependency.Id)));
            if (depth > limits.MaxTypeDepth)
                throw ModulesExceptionFactory.Error("parse", "TypeDepthExceeded", id, new { depth, max = limits.MaxTypeDepth });
            states[id] = 2;
            depths[id] = depth;
            return depth;
        }
    }

    private static IEnumerable<(string Id, int EdgeDepth)> RecordDependencies(TypeDecl declaration)
    {
        foreach (var field in declaration.Fields)
        {
            var type = field.Type.Kind == "Seq" ? field.Type.Element! : field.Type;
            if (type.Kind == "Record" && type.Name is not null)
                yield return (type.Name, field.Type.Kind == "Seq" ? 2 : 1);
        }
    }

    private static ImmutableArray<ImportDecl> ParseImports(JsonElement value, StrogoLimits limits)
    {
        RequireArray(value, "imports");
        var values = value.EnumerateArray().ToArray();
        if (values.Length > limits.MaxImports)
            throw ModulesExceptionFactory.Error("parse", "ImportLimitExceeded", details: new { actual = values.Length, max = limits.MaxImports });

        var result = ImmutableArray.CreateBuilder<ImportDecl>(values.Length);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var importValue in OrderByEntityId(values, "moduleId"))
        {
            CheckObject(importValue, "import", ["moduleId", "sourceDigest", "contractDigest"]);
            var moduleId = RequireId(importValue.GetProperty("moduleId"));
            if (!ids.Add(moduleId))
                throw ModulesExceptionFactory.Error("parse", "DuplicateImport", moduleId);
            result.Add(new ImportDecl(moduleId, RequireDigest(importValue.GetProperty("sourceDigest")), RequireDigest(importValue.GetProperty("contractDigest"))));
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<FunctionDecl> ParseFunctions(JsonElement value, IReadOnlyDictionary<string, TypeDecl> typeById, StrogoLimits limits)
    {
        RequireArray(value, "functions");
        var values = value.EnumerateArray().ToArray();
        if (values.Length > limits.MaxFunctions)
            throw ModulesExceptionFactory.Error("parse", "FunctionLimitExceeded", details: new { actual = values.Length, max = limits.MaxFunctions });

        var typeIds = typeById.Keys.ToImmutableHashSet(StringComparer.Ordinal);
        var headers = ImmutableArray.CreateBuilder<FunctionHeader>(values.Length);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in OrderByEntityId(values))
        {
            CheckObject(item, "function", ["id", "parameters", "returnType", "contractRef", "body"]);
            var id = RequireId(item.GetProperty("id"));
            if (!ids.Add(id))
                throw ModulesExceptionFactory.Error("parse", "DuplicateFunctionId", id);
            var parameters = ParseParameters(item.GetProperty("parameters"), typeIds, limits);
            var returnType = ParseType(item.GetProperty("returnType"), 0, limits);
            EnsureTypeRef(returnType, typeIds, $"function.{id}.returnType");
            var body = item.GetProperty("body");
            CheckObject(body, "function.body", ["parameters", "nodes", "result"]);
            headers.Add(new FunctionHeader(id, parameters, returnType, RequireId(item.GetProperty("contractRef")), body));
        }

        var headerById = headers.ToImmutableDictionary(header => header.Id, StringComparer.Ordinal);
        var result = ImmutableArray.CreateBuilder<FunctionDecl>(headers.Count);
        var totalNodes = 0;
        foreach (var header in headers.OrderBy(header => header.Id, StringComparer.Ordinal))
        {
            var body = ParseFunctionBody(header, headerById, typeById, typeIds, limits);
            totalNodes = checked(totalNodes + CountNodes(body));
            if (totalNodes > limits.MaxTotalNodes)
                throw ModulesExceptionFactory.Error("parse", "ModuleNodeLimitExceeded", details: new { actual = totalNodes, max = limits.MaxTotalNodes });
            result.Add(new FunctionDecl(header.Id, header.Parameters, header.ReturnType, header.ContractRef, body));
        }

        var functions = result.ToImmutable();
        ValidateCallGraph(functions);
        return functions;
    }

    private static ImmutableArray<FunctionParameter> ParseParameters(JsonElement value, ImmutableHashSet<string> typeIds, StrogoLimits limits)
    {
        RequireArray(value, "function.parameters");
        var values = value.EnumerateArray().ToArray();
        if (values.Length > limits.MaxNodesPerFunction)
            throw ModulesExceptionFactory.Error("parse", "FunctionNodeLimitExceeded", details: new { actual = values.Length, max = limits.MaxNodesPerFunction });

        var result = ImmutableArray.CreateBuilder<FunctionParameter>(values.Length);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in values)
        {
            CheckObject(item, "function.parameter", ["id", "type"]);
            var id = RequireId(item.GetProperty("id"));
            if (!ids.Add(id))
                throw ModulesExceptionFactory.Error("parse", "DuplicateParameterId", id);
            var type = ParseType(item.GetProperty("type"), 0, limits);
            EnsureTypeRef(type, typeIds, $"function.parameter.{id}");
            result.Add(new FunctionParameter(id, type));
        }
        return result.ToImmutable();
    }

    private static FunctionBody ParseFunctionBody(
        FunctionHeader function,
        IReadOnlyDictionary<string, FunctionHeader> functions,
        IReadOnlyDictionary<string, TypeDecl> typeById,
        ImmutableHashSet<string> typeIds,
        StrogoLimits limits)
    {
        var bodyLocus = $"function/{function.Id}/body";
        var body = ParseRegionSyntax(function.Body, bodyLocus, 0, typeIds, limits);
        var functionNodeCount = CountNodes(body);
        if (functionNodeCount > limits.MaxNodesPerFunction)
            throw ModulesExceptionFactory.Error("parse", "FunctionNodeLimitExceeded", function.Id,
                new { actual = functionNodeCount, max = limits.MaxNodesPerFunction });
        var bodyParameters = body.Parameters;
        var functionParameters = function.Parameters.Select(parameter => parameter.Id).ToImmutableArray();
        if (!bodyParameters.SequenceEqual(functionParameters))
            throw ModulesExceptionFactory.Error("parse", "BodyParametersMismatch", details: new { declared = functionParameters, body = bodyParameters });

        ValidateRegion(body, function.Parameters, function.ReturnType, "ReturnTypeMismatch", bodyLocus, functions, typeById);
        return body;
    }

    private static FunctionBody ParseRegionSyntax(
        JsonElement region,
        string path,
        int depth,
        ImmutableHashSet<string> typeIds,
        StrogoLimits limits)
    {
        if (depth > limits.MaxRegionDepth)
            throw ModulesExceptionFactory.Error("parse", "RegionDepthExceeded", details: new { depth, max = limits.MaxRegionDepth });
        CheckObject(region, path, ["parameters", "nodes", "result"]);
        var regionParameters = ParseIdArray(region.GetProperty("parameters"), $"{path}.parameters", "DuplicateParameterId");

        var nodesElement = region.GetProperty("nodes");
        RequireArray(nodesElement, $"{path}.nodes");
        var nodeValues = nodesElement.EnumerateArray().ToArray();
        if (nodeValues.Length > limits.MaxNodesPerFunction)
            throw ModulesExceptionFactory.Error("parse", "FunctionNodeLimitExceeded", details: new { actual = nodeValues.Length, max = limits.MaxNodesPerFunction });

        var nodes = ImmutableArray.CreateBuilder<FunctionNode>(nodeValues.Length);
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nodeValue in OrderByEntityId(nodeValues))
        {
            if (nodeValue.ValueKind != JsonValueKind.Object)
                throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", details: new { path = $"{path}.nodes[]", expected = "object" });
            var op = RequireString(RequireProperty(nodeValue, "op", $"{path}.nodes[]"));
            if (!SupportedOpcodes.Contains(op))
                throw ModulesExceptionFactory.Error("parse", "UnsupportedOpcode", new { opcode = op });
            if (op == "fold" && depth != 0)
                throw ModulesExceptionFactory.Error("parse", "NestedFoldNotSupported", $"{path}/node/{SortId(nodeValue, "id")}");
            if (op == "fold" && !nodeValue.TryGetProperty("invariant", out _))
                throw ModulesExceptionFactory.Error("parse", "InvariantRequired", $"{path}/node/{SortId(nodeValue, "id")}");
            CheckObject(nodeValue, $"{path}.nodes[]", ExpectedNodeFields(op));
            var nodeId = RequireId(nodeValue.GetProperty("id"));
            var nodeEntityId = $"{path}/node/{nodeId}";
            if (regionParameters.Contains(nodeId) || !nodeIds.Add(nodeId))
                throw ModulesExceptionFactory.Error("parse", "DuplicateValueId", nodeEntityId);
            var nodeType = ParseType(nodeValue.GetProperty("type"), 0, limits);
            EnsureTypeRef(nodeType, typeIds, $"{nodeEntityId}/type");
            var thenRegion = op == "if" ? ParseRegionSyntax(nodeValue.GetProperty("thenRegion"), $"{nodeEntityId}/then", depth + 1, typeIds, limits) : null;
            var elseRegion = op == "if" ? ParseRegionSyntax(nodeValue.GetProperty("elseRegion"), $"{nodeEntityId}/else", depth + 1, typeIds, limits) : null;
            var stepRegion = op == "fold" ? ParseRegionSyntax(nodeValue.GetProperty("stepRegion"), $"{nodeEntityId}/step", depth + 1, typeIds, limits) : null;
            ProofExpression? invariant = null;
            if (op == "fold")
            {
                var proofNodes = 0;
                invariant = ParseProofExpression(nodeValue.GetProperty("invariant"), $"{nodeEntityId}/invariant", 1, typeIds, limits, ref proofNodes);
            }
            nodes.Add(new FunctionNode(nodeId, op, nodeType, ParseIdArray(nodeValue.GetProperty("args"), $"{nodeEntityId}/args", null), ParseNodeMetadata(nodeValue, nodeId, nodeEntityId, op, limits), thenRegion, elseRegion, stepRegion, invariant));
        }

        var nodeArray = nodes.ToImmutable();
        var resultId = RequireId(region.GetProperty("result"));
        return new FunctionBody(regionParameters, nodeArray, resultId);
    }

    private static void ValidateRegion(
        FunctionBody region,
        ImmutableArray<FunctionParameter> parameters,
        TypeRef expectedResultType,
        string resultMismatchCode,
        string regionLocus,
        IReadOnlyDictionary<string, FunctionHeader> functions,
        IReadOnlyDictionary<string, TypeDecl> typeById)
    {
        var parameterTypes = parameters.ToDictionary(parameter => parameter.Id, parameter => parameter.Type, StringComparer.Ordinal);
        var nodeTypes = region.Nodes.ToDictionary(node => node.Id, node => node.Type, StringComparer.Ordinal);
        foreach (var node in region.Nodes.OrderBy(node => node.Id, StringComparer.Ordinal))
        {
            EnsureTypeRules(node, regionLocus, parameterTypes, nodeTypes, functions, typeById);
            if (node.Op == "if")
            {
                var nodeLocus = $"{regionLocus}/node/{node.Id}";
                var environmentTypes = node.Args.Skip(1).Select(id => ResolveValueType(id, parameterTypes, nodeTypes, nodeLocus)).ToImmutableArray();
                ValidateBranch(node, node.ThenRegion!, "then", environmentTypes);
                ValidateBranch(node, node.ElseRegion!, "else", environmentTypes);
            }
            else if (node.Op == "fold")
            {
                ValidateFoldNode(node, regionLocus, parameterTypes, nodeTypes, functions, typeById);
            }
        }

        var folds = region.Nodes.Where(node => node.Op == "fold").ToArray();
        if (folds.Length > 1)
            throw ModulesExceptionFactory.Error("parse", "NestedFoldNotSupported", regionLocus, new { actual = folds.Length, max = 1 });
        if (folds.Length == 1 && region.Result != folds[0].Id)
            throw ModulesExceptionFactory.Error("parse", "FoldMustBeFunctionResult", $"{regionLocus}/node/{folds[0].Id}");

        var resultId = region.Result;
        if (!parameterTypes.ContainsKey(resultId) && !nodeTypes.ContainsKey(resultId))
            throw ModulesExceptionFactory.Error("parse", "UnknownResultValue", regionLocus, new { resultId });
        EnsureDAG(region.Nodes, region.Parameters, regionLocus);
        EnsureReachable(region.Nodes, region.Parameters, resultId, regionLocus);
        var resultType = parameterTypes.TryGetValue(resultId, out var parameterType) ? parameterType : nodeTypes[resultId];
        if (!TypesEquivalent(resultType, expectedResultType))
            throw ModulesExceptionFactory.Error("parse", resultMismatchCode, regionLocus, new { expected = expectedResultType.ToString(), actual = resultType.ToString(), resultId });

        void ValidateBranch(FunctionNode owner, FunctionBody branch, string role, ImmutableArray<TypeRef> environmentTypes)
        {
            if (branch.Parameters.Length != environmentTypes.Length)
                throw ModulesExceptionFactory.Error("parse", "RegionParametersMismatch", $"{regionLocus}/node/{owner.Id}/{role}",
                    new { branch = role, expected = environmentTypes.Length, actual = branch.Parameters.Length });
            var branchParameters = branch.Parameters.Select((id, index) => new FunctionParameter(id, environmentTypes[index])).ToImmutableArray();
            ValidateRegion(branch, branchParameters, owner.Type, "RegionResultTypeMismatch", $"{regionLocus}/node/{owner.Id}/{role}", functions, typeById);
        }
    }

    private static void ValidateFoldNode(
        FunctionNode node,
        string regionLocus,
        IReadOnlyDictionary<string, TypeRef> parameterTypes,
        IReadOnlyDictionary<string, TypeRef> nodeTypes,
        IReadOnlyDictionary<string, FunctionHeader> functions,
        IReadOnlyDictionary<string, TypeDecl> typeById)
    {
        var locus = $"{regionLocus}/node/{node.Id}";
        TypeRef Resolve(string id) => ResolveValueType(id, parameterTypes, nodeTypes, locus);
        if (node.Args.Length < 2)
            throw ModulesExceptionFactory.Error("parse", "ArityMismatch", locus, new { op = "fold", expectedAtLeast = 2, actual = node.Args.Length });
        var sequence = Resolve(node.Args[0]);
        if (sequence.Kind != "Seq" || sequence.Element is null)
            throw ModulesExceptionFactory.Error("parse", "TypeMismatch", locus, new { op = "fold", role = "sequence", expected = "Seq", actual = sequence.ToString() });
        var accumulator = Resolve(node.Args[1]);
        if (!TypesEquivalent(node.Type, accumulator))
            throw ModulesExceptionFactory.Error("parse", "TypeMismatch", locus, new { op = "fold", role = "result", expected = accumulator.ToString(), actual = node.Type.ToString() });
        if (node.StepRegion is null)
            throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", locus, new { reason = "MissingStepRegion" });
        if (node.Invariant is null)
            throw ModulesExceptionFactory.Error("parse", "InvariantRequired", locus);

        var environment = node.Args.Skip(2).Select(Resolve).ToImmutableArray();
        var stepTypes = ImmutableArray.CreateBuilder<TypeRef>(3 + environment.Length);
        stepTypes.Add(new TypeRef("I64"));
        stepTypes.Add(sequence.Element);
        stepTypes.Add(accumulator);
        stepTypes.AddRange(environment);
        if (node.StepRegion.Parameters.Length != stepTypes.Count)
            throw ModulesExceptionFactory.Error("parse", "RegionParametersMismatch", $"{locus}/step", new { branch = "step", expected = stepTypes.Count, actual = node.StepRegion.Parameters.Length });
        var stepParameters = node.StepRegion.Parameters.Select((id, index) => new FunctionParameter(id, stepTypes[index])).ToImmutableArray();
        EnsureFoldStepRestrictions(node.StepRegion, $"{locus}/step");
        ValidateRegion(node.StepRegion, stepParameters, accumulator, "RegionResultTypeMismatch", $"{locus}/step", functions, typeById);

        var proofType = ValidateProofExpression(node.Invariant, sequence, accumulator, environment, typeById, ImmutableDictionary<string, TypeRef>.Empty, insideQuantifier: false, locus: $"{locus}/invariant");
        if (!TypesEquivalent(proofType, new TypeRef("Bool")))
            throw ModulesExceptionFactory.Error("parse", "TypeMismatch", $"{locus}/invariant", new { expected = "Bool", actual = proofType.ToString() });
        var worstCaseCost = ProofWorstCaseCost(node.Invariant);
        if (worstCaseCost > ProofEvaluationStepsHardMaximum)
            throw ModulesExceptionFactory.Error("parse", "ProofWorstCaseCostExceeded", $"{locus}/invariant", new { actual = ">262144", max = "262144" });
    }

    private static void EnsureFoldStepRestrictions(FunctionBody body, string locus)
    {
        foreach (var node in body.Nodes)
        {
            if (node.Op == "fold") throw ModulesExceptionFactory.Error("parse", "NestedFoldNotSupported", $"{locus}/node/{node.Id}");
            if (node.Op == "call") throw ModulesExceptionFactory.Error("parse", "FoldStepCallNotSupported", $"{locus}/node/{node.Id}");
            if (node.ThenRegion is not null) EnsureFoldStepRestrictions(node.ThenRegion, $"{locus}/node/{node.Id}/then");
            if (node.ElseRegion is not null) EnsureFoldStepRestrictions(node.ElseRegion, $"{locus}/node/{node.Id}/else");
        }
    }

    private static TypeRef ResolveValueType(string id, IReadOnlyDictionary<string, TypeRef> parameterTypes, IReadOnlyDictionary<string, TypeRef> nodeTypes, string ownerId)
        => parameterTypes.TryGetValue(id, out var parameterType) ? parameterType
            : nodeTypes.TryGetValue(id, out var nodeType) ? nodeType
            : throw ModulesExceptionFactory.Error("parse", "DanglingNodeArg", ownerId, new { reference = id });

    private static int CountNodes(FunctionBody region)
        => checked(region.Nodes.Length + region.Nodes.Sum(node =>
            (node.ThenRegion is null ? 0 : CountNodes(node.ThenRegion))
            + (node.ElseRegion is null ? 0 : CountNodes(node.ElseRegion))
            + (node.StepRegion is null ? 0 : CountNodes(node.StepRegion))));

    private static IReadOnlyCollection<string> ExpectedNodeFields(string op) => op switch
    {
        "i64.const" or "bool.const" => ["id", "op", "type", "args", "value"],
        "record.make" => ["id", "op", "type", "args", "recordType", "fieldIds"],
        "record.get" => ["id", "op", "type", "args", "fieldId"],
        "seq.empty" => ["id", "op", "type", "args", "elementType", "capacity"],
        "if" => ["id", "op", "type", "args", "thenRegion", "elseRegion"],
        "fold" => ["id", "op", "type", "args", "stepRegion", "invariant"],
        "call" => ["id", "op", "type", "args", "functionRef"],
        _ => ["id", "op", "type", "args"]
    };

    private static NodeMetadata ParseNodeMetadata(JsonElement node, string nodeId, string nodeEntityId, string op, StrogoLimits limits)
    {
        if (op == "i64.const")
        {
            var literal = node.GetProperty("value");
            var text = literal.ValueKind == JsonValueKind.String
                ? literal.GetString()!
                : throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", new { node = nodeId, property = "value", reason = "ExpectedString" });
            if (!CanonicalI64Pattern.IsMatch(text) || !long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                throw ModulesExceptionFactory.Error("parse", "InvalidI64", nodeEntityId, new { value = text });
            return NodeMetadata.Empty with { Value = text };
        }

        if (op == "bool.const")
        {
            var value = node.GetProperty("value");
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", new { node = nodeId, property = "value", reason = "ExpectedBool" });
            return NodeMetadata.Empty with { Value = value.ValueKind == JsonValueKind.True ? "true" : "false" };
        }

        if (op == "record.make")
            return NodeMetadata.Empty with { RecordType = RequireId(node.GetProperty("recordType")), FieldIds = ParseIdArray(node.GetProperty("fieldIds"), $"{nodeEntityId}.fieldIds", "DuplicateFieldId") };
        if (op == "record.get")
            return NodeMetadata.Empty with { FieldId = RequireId(node.GetProperty("fieldId")) };
        if (op == "seq.empty")
            return NodeMetadata.Empty with { ElementType = ParseType(node.GetProperty("elementType"), 1, limits), Capacity = RequireInt32(node.GetProperty("capacity"), $"{nodeEntityId}.capacity") };
        if (op == "call")
            return NodeMetadata.Empty with { FunctionRef = RequireId(node.GetProperty("functionRef")) };
        return NodeMetadata.Empty;
    }

    private static ImmutableArray<string> ParseIdArray(JsonElement value, string path, string? duplicateCode)
    {
        RequireArray(value, path);
        var result = ImmutableArray.CreateBuilder<string>();
        HashSet<string>? ids = duplicateCode is null ? null : new(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            var id = RequireId(item);
            if (ids is not null && !ids.Add(id))
                throw ModulesExceptionFactory.Error("parse", duplicateCode!, id, new { path });
            result.Add(id);
        }
        return result.ToImmutable();
    }

    private static void EnsureTypeRules(
        FunctionNode node,
        string regionLocus,
        IReadOnlyDictionary<string, TypeRef> parameterTypes,
        IReadOnlyDictionary<string, TypeRef> nodeTypes,
        IReadOnlyDictionary<string, FunctionHeader> functions,
        IReadOnlyDictionary<string, TypeDecl> typeById)
    {
        var entityId = $"{regionLocus}/node/{node.Id}";
        TypeRef ArgType(int index)
        {
            var id = node.Args[index];
            if (parameterTypes.TryGetValue(id, out var parameterType)) return parameterType;
            if (nodeTypes.TryGetValue(id, out var nodeType)) return nodeType;
            throw ModulesExceptionFactory.Error("parse", "DanglingNodeArg", entityId, new { reference = id });
        }
        void Arity(int expected)
        {
            if (node.Args.Length != expected)
                throw ModulesExceptionFactory.Error("parse", "ArityMismatch", entityId, new { op = node.Op, expected, actual = node.Args.Length });
        }
        void TypeIs(TypeRef actual, TypeRef expected, string role)
        {
            if (!TypesEquivalent(actual, expected))
                throw ModulesExceptionFactory.Error("parse", "TypeMismatch", entityId, new { op = node.Op, role, expected = expected.ToString(), actual = actual.ToString() });
        }

        switch (node.Op)
        {
            case "i64.const": Arity(0); TypeIs(node.Type, new TypeRef("I64"), "result"); return;
            case "bool.const": Arity(0); TypeIs(node.Type, new TypeRef("Bool"), "result"); return;
            case "i64.add":
            case "i64.sub": Arity(2); TypeIs(ArgType(0), new TypeRef("I64"), "arg0"); TypeIs(ArgType(1), new TypeRef("I64"), "arg1"); TypeIs(node.Type, new TypeRef("I64"), "result"); return;
            case "i64.le":
            case "i64.eq": Arity(2); TypeIs(ArgType(0), new TypeRef("I64"), "arg0"); TypeIs(ArgType(1), new TypeRef("I64"), "arg1"); TypeIs(node.Type, new TypeRef("Bool"), "result"); return;
            case "bool.not": Arity(1); TypeIs(ArgType(0), new TypeRef("Bool"), "arg0"); TypeIs(node.Type, new TypeRef("Bool"), "result"); return;
            case "bool.and":
            case "bool.or": Arity(2); TypeIs(ArgType(0), new TypeRef("Bool"), "arg0"); TypeIs(ArgType(1), new TypeRef("Bool"), "arg1"); TypeIs(node.Type, new TypeRef("Bool"), "result"); return;
            case "record.make": ValidateRecordMake(node, entityId, ArgType, TypeIs, typeById); return;
            case "record.get":
                Arity(1);
                var record = ArgType(0);
                if (record.Kind != "Record" || record.Name is null || !typeById.TryGetValue(record.Name, out var declaration))
                    throw ModulesExceptionFactory.Error("parse", "TypeMismatch", entityId, new { op = node.Op, expected = "Record", actual = record.ToString() });
                var field = declaration.Fields.SingleOrDefault(candidate => candidate.Id == node.Metadata.FieldId);
                if (field is null)
                    throw ModulesExceptionFactory.Error("parse", "UnknownRecordField", entityId, new { recordType = record.Name, fieldId = node.Metadata.FieldId });
                TypeIs(node.Type, field.Type, "result"); return;
            case "seq.empty":
                Arity(0); TypeIs(node.Type, new TypeRef("Seq", Element: node.Metadata.ElementType, Capacity: node.Metadata.Capacity), "result"); return;
            case "seq.length":
                Arity(1);
                if (ArgType(0).Kind != "Seq") throw ModulesExceptionFactory.Error("parse", "TypeMismatch", entityId, new { op = node.Op, expected = "Seq", actual = ArgType(0).ToString() });
                TypeIs(node.Type, new TypeRef("I64"), "result"); return;
            case "seq.get":
                Arity(2);
                var sequence = ArgType(0);
                if (sequence.Kind != "Seq" || sequence.Element is null) throw ModulesExceptionFactory.Error("parse", "TypeMismatch", entityId, new { op = node.Op, expected = "Seq", actual = sequence.ToString() });
                TypeIs(ArgType(1), new TypeRef("I64"), "index"); TypeIs(node.Type, sequence.Element, "result"); return;
            case "seq.append":
                Arity(2);
                var source = ArgType(0);
                if (source.Kind != "Seq" || source.Element is null) throw ModulesExceptionFactory.Error("parse", "TypeMismatch", entityId, new { op = node.Op, expected = "Seq", actual = source.ToString() });
                TypeIs(ArgType(1), source.Element, "element"); TypeIs(node.Type, source, "result"); return;
            case "if":
                if (node.Args.Length < 1)
                    throw ModulesExceptionFactory.Error("parse", "ArityMismatch", entityId, new { op = node.Op, expectedAtLeast = 1, actual = node.Args.Length });
                TypeIs(ArgType(0), new TypeRef("Bool"), "condition");
                if (node.ThenRegion is null || node.ElseRegion is null)
                    throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", entityId, new { reason = "MissingBranchRegion" });
                return;
            case "fold":
                if (node.Args.Length < 2)
                    throw ModulesExceptionFactory.Error("parse", "ArityMismatch", entityId, new { op = node.Op, expectedAtLeast = 2, actual = node.Args.Length });
                for (var index = 0; index < node.Args.Length; index++) _ = ArgType(index);
                return;
            case "call":
                if (node.Metadata.FunctionRef is null || !functions.TryGetValue(node.Metadata.FunctionRef, out var callee))
                    throw ModulesExceptionFactory.Error("parse", "UnknownFunction", entityId, new { functionRef = node.Metadata.FunctionRef });
                Arity(callee.Parameters.Length);
                for (var index = 0; index < callee.Parameters.Length; index++) TypeIs(ArgType(index), callee.Parameters[index].Type, $"arg{index}");
                TypeIs(node.Type, callee.ReturnType, "result"); return;
            default: throw ModulesExceptionFactory.Error("parse", "UnsupportedOpcode", new { node.Id, node.Op });
        }
    }

    private static void ValidateRecordMake(FunctionNode node, string entityId, Func<int, TypeRef> argType, Action<TypeRef, TypeRef, string> typeIs, IReadOnlyDictionary<string, TypeDecl> typeById)
    {
        if (node.Type.Kind != "Record" || node.Type.Name is null || node.Metadata.RecordType != node.Type.Name || !typeById.TryGetValue(node.Type.Name, out var declaration))
            throw ModulesExceptionFactory.Error("parse", "TypeMismatch", entityId, new { op = node.Op, expected = node.Metadata.RecordType, actual = node.Type.ToString() });
        var expectedIds = declaration.Fields.Select(field => field.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var actualIds = node.Metadata.FieldIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (node.Args.Length != node.Metadata.FieldIds.Length || !actualIds.SequenceEqual(expectedIds, StringComparer.Ordinal))
            throw ModulesExceptionFactory.Error("parse", "RecordFieldMismatch", entityId, new { recordType = declaration.Id, expected = expectedIds, actual = actualIds });
        var pairs = node.Metadata.FieldIds
            .Select((fieldId, index) => (FieldId: fieldId, ArgumentIndex: index))
            .OrderBy(pair => pair.FieldId, StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            var field = declaration.Fields.Single(candidate => candidate.Id == pair.FieldId);
            typeIs(argType(pair.ArgumentIndex), field.Type, field.Id);
        }
    }

    private static void ValidateCallGraph(ImmutableArray<FunctionDecl> functions)
    {
        var calls = functions.ToDictionary(function => function.Id, function => EnumerateNodes(function.Body).Where(node => node.Op == "call")
            .Select(node => node.Metadata.FunctionRef!).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var states = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var function in functions.OrderBy(function => function.Id, StringComparer.Ordinal)) Visit(function.Id);
        void Visit(string id)
        {
            if (states.TryGetValue(id, out var state))
            {
                if (state == 1) throw ModulesExceptionFactory.Error("parse", "CallCycleDetected", id);
                return;
            }
            states[id] = 1;
            foreach (var callee in calls[id]) Visit(callee);
            states[id] = 2;
        }
    }

    private static IEnumerable<FunctionNode> EnumerateNodes(FunctionBody region)
    {
        foreach (var node in region.Nodes)
        {
            yield return node;
            if (node.ThenRegion is not null)
                foreach (var nested in EnumerateNodes(node.ThenRegion)) yield return nested;
            if (node.ElseRegion is not null)
                foreach (var nested in EnumerateNodes(node.ElseRegion)) yield return nested;
            if (node.StepRegion is not null)
                foreach (var nested in EnumerateNodes(node.StepRegion)) yield return nested;
        }
    }

    private static ImmutableArray<string> ParseExports(JsonElement value, ImmutableHashSet<string> functionIds, StrogoLimits limits)
    {
        RequireArray(value, "exports");
        var values = value.EnumerateArray().ToArray();
        if (values.Length > limits.MaxFunctions) throw ModulesExceptionFactory.Error("parse", "ExportLimitExceeded", details: new { actual = values.Length, max = limits.MaxFunctions });
        var result = ImmutableArray.CreateBuilder<string>(values.Length);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in values.OrderBy(ScalarSortId, StringComparer.Ordinal).ThenBy(item => item.GetRawText(), StringComparer.Ordinal))
        {
            var id = RequireId(item);
            if (!ids.Add(id)) throw ModulesExceptionFactory.Error("parse", "DuplicateExport", id);
            if (!functionIds.Contains(id)) throw ModulesExceptionFactory.Error("parse", "ExportedFunctionMissing", id);
            result.Add(id);
        }
        return result.ToImmutable();
    }

    private static void EnsureTypeRef(TypeRef type, ImmutableHashSet<string> typeIds, string path)
    {
        if (type.Kind == "Record" && (type.Name is null || !typeIds.Contains(type.Name)))
            throw ModulesExceptionFactory.Error("parse", "UnknownType", new { path, type = type.Name });
        if (type.Kind != "Seq") return;
        if (type.Element is null) throw ModulesExceptionFactory.Error("parse", "InvalidType", new { path, reason = "MissingElementType" });
        if (type.Capacity is null or > 256 or < 0) throw ModulesExceptionFactory.Error("parse", "InvalidSeqCapacity", new { path, capacity = type.Capacity ?? -1 });
        if (type.Element.Kind == "Seq") throw ModulesExceptionFactory.Error("parse", "NestedSequenceNotSupported", new { path });
        EnsureTypeRef(type.Element, typeIds, path);
    }

    private static void EnsureDAG(ImmutableArray<FunctionNode> nodes, ImmutableArray<string> parameters, string regionLocus)
    {
        var nodeIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var inDegree = nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        var users = nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var node in nodes)
            foreach (var arg in node.Args.Distinct(StringComparer.Ordinal))
            {
                if (!parameters.Contains(arg) && !nodeIds.Contains(arg)) throw ModulesExceptionFactory.Error("parse", "DanglingNodeArg", $"{regionLocus}/node/{node.Id}", new { reference = arg });
                if (!nodeIds.Contains(arg)) continue;
                inDegree[node.Id]++;
                users[arg].Add(node.Id);
            }
        var ready = new SortedSet<string>(inDegree.Where(item => item.Value == 0).Select(item => item.Key), StringComparer.Ordinal);
        var count = 0;
        while (ready.Count > 0)
        {
            var current = ready.Min!;
            ready.Remove(current);
            count++;
            foreach (var user in users[current]) if (--inDegree[user] == 0) ready.Add(user);
        }
        if (count != nodes.Length) throw ModulesExceptionFactory.Error("parse", "CycleDetected", regionLocus);
    }

    private static void EnsureReachable(ImmutableArray<FunctionNode> nodes, ImmutableArray<string> parameters, string resultId, string regionLocus)
    {
        var byId = nodes.ToDictionary(node => node.Id, node => node, StringComparer.Ordinal);
        var pending = new Stack<string>([resultId]);
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryPop(out var id))
        {
            if (parameters.Contains(id) || !reachable.Add(id)) continue;
            if (!byId.TryGetValue(id, out var node)) throw ModulesExceptionFactory.Error("parse", "UnknownResultValue", regionLocus, new { resultId });
            foreach (var arg in node.Args) pending.Push(arg);
        }
        var unreachable = byId.Keys.Where(id => !reachable.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (unreachable.Length > 0) throw ModulesExceptionFactory.Error("parse", "UnreachableNode", regionLocus, new { result = resultId, unreachable });
    }

    private static TypeRef ValidateProofExpression(
        ProofExpression expression,
        TypeRef foldSequence,
        TypeRef accumulator,
        ImmutableArray<TypeRef> environment,
        IReadOnlyDictionary<string, TypeDecl> typeById,
        ImmutableDictionary<string, TypeRef> boundTypes,
        bool insideQuantifier,
        string locus)
    {
        void Arity(int expected)
        {
            if (expression.Args.Length != expected)
                throw ModulesExceptionFactory.Error("parse", "ArityMismatch", locus, new { op = expression.Op, expected, actual = expression.Args.Length });
        }
        TypeRef Arg(int index) => ValidateProofExpression(expression.Args[index], foldSequence, accumulator, environment, typeById, boundTypes, insideQuantifier, $"{locus}/arg/{index}");
        void Require(TypeRef actual, TypeRef expected, string role)
        {
            if (!TypesEquivalent(actual, expected))
                throw ModulesExceptionFactory.Error("parse", "TypeMismatch", locus, new { op = expression.Op, role, expected = expected.ToString(), actual = actual.ToString() });
        }
        TypeRef inferred;
        switch (expression.Op)
        {
            case "param":
                throw ModulesExceptionFactory.Error("parse", "UnsupportedProofOpcode", locus, new { opcode = expression.Op });
            case "fold.prefixLength": inferred = new TypeRef("I64"); break;
            case "fold.sequence": inferred = foldSequence; break;
            case "fold.initialAccumulator":
            case "fold.accumulator": inferred = accumulator; break;
            case "fold.environment":
                if (expression.Position is null || expression.Position < 0 || expression.Position >= environment.Length)
                    throw ModulesExceptionFactory.Error("parse", "FoldEnvironmentOutOfRange", locus, new { position = expression.Position ?? -1, count = environment.Length });
                inferred = environment[expression.Position.Value];
                break;
            case "proof.bound":
                if (expression.BinderId is null || !boundTypes.TryGetValue(expression.BinderId, out var boundType))
                    throw ModulesExceptionFactory.Error("parse", "UnknownProofBinder", locus, new { binderId = expression.BinderId });
                inferred = boundType;
                break;
            case "i64.const": inferred = new TypeRef("I64"); break;
            case "math.const": inferred = new TypeRef("MathInt"); break;
            case "bool.const": inferred = new TypeRef("Bool"); break;
            case "eq":
                Arity(2);
                var equalityLeft = Arg(0);
                var equalityRight = Arg(1);
                Require(equalityRight, equalityLeft, "arg1");
                inferred = new TypeRef("Bool");
                break;
            case "i64.add":
            case "i64.sub":
                Arity(2); Require(Arg(0), new TypeRef("I64"), "arg0"); Require(Arg(1), new TypeRef("I64"), "arg1"); inferred = new TypeRef("I64"); break;
            case "i64.le":
                Arity(2); Require(Arg(0), new TypeRef("I64"), "arg0"); Require(Arg(1), new TypeRef("I64"), "arg1"); inferred = new TypeRef("Bool"); break;
            case "math.le":
                Arity(2); Require(Arg(0), new TypeRef("MathInt"), "arg0"); Require(Arg(1), new TypeRef("MathInt"), "arg1"); inferred = new TypeRef("Bool"); break;
            case "math.from_i64":
                Arity(1); Require(Arg(0), new TypeRef("I64"), "arg0"); inferred = new TypeRef("MathInt"); break;
            case "math.add":
            case "math.sub":
                Arity(2); Require(Arg(0), new TypeRef("MathInt"), "arg0"); Require(Arg(1), new TypeRef("MathInt"), "arg1"); inferred = new TypeRef("MathInt"); break;
            case "bool.not":
                Arity(1); Require(Arg(0), new TypeRef("Bool"), "arg0"); inferred = new TypeRef("Bool"); break;
            case "bool.and":
            case "bool.or":
                Arity(2); Require(Arg(0), new TypeRef("Bool"), "arg0"); Require(Arg(1), new TypeRef("Bool"), "arg1"); inferred = new TypeRef("Bool"); break;
            case "if":
                Arity(3); Require(Arg(0), new TypeRef("Bool"), "condition"); var thenType = Arg(1); Require(Arg(2), thenType, "else"); inferred = thenType; break;
            case "record.make":
                if (expression.RecordType is null || !typeById.TryGetValue(expression.RecordType, out var declaration))
                    throw ModulesExceptionFactory.Error("parse", "UnknownType", locus, new { type = expression.RecordType });
                var expectedFields = declaration.Fields.OrderBy(field => field.Id, StringComparer.Ordinal).ToArray();
                var actualFields = expression.FieldIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
                if (expression.Args.Length != expectedFields.Length || !actualFields.SequenceEqual(expectedFields.Select(field => field.Id), StringComparer.Ordinal))
                    throw ModulesExceptionFactory.Error("parse", "RecordFieldMismatch", locus, new { recordType = declaration.Id });
                var argumentByField = expression.FieldIds.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
                foreach (var field in expectedFields) Require(Arg(argumentByField[field.Id]), field.Type, field.Id);
                inferred = new TypeRef("Record", Name: declaration.Id);
                break;
            case "record.get":
                Arity(1);
                var recordType = Arg(0);
                if (recordType.Kind != "Record" || recordType.Name is null || !typeById.TryGetValue(recordType.Name, out var recordDeclaration))
                    throw ModulesExceptionFactory.Error("parse", "TypeMismatch", locus, new { expected = "Record", actual = recordType.ToString() });
                var fieldDeclaration = recordDeclaration.Fields.SingleOrDefault(field => field.Id == expression.ReferenceId)
                    ?? throw ModulesExceptionFactory.Error("parse", "UnknownRecordField", locus, new { recordType = recordType.Name, fieldId = expression.ReferenceId });
                inferred = fieldDeclaration.Type;
                break;
            case "seq.empty":
                Arity(0);
                if (expression.ElementType is null || expression.Capacity is null or < 0 or > 256)
                    throw ModulesExceptionFactory.Error("parse", "InvalidSeqCapacity", locus);
                inferred = new TypeRef("Seq", Element: expression.ElementType, Capacity: expression.Capacity);
                break;
            case "seq.length":
                Arity(1); var lengthSequence = Arg(0); if (lengthSequence.Kind != "Seq") throw ModulesExceptionFactory.Error("parse", "TypeMismatch", locus, new { expected = "Seq", actual = lengthSequence.ToString() }); inferred = new TypeRef("I64"); break;
            case "seq.get":
                Arity(2); var getSequence = Arg(0); if (getSequence.Kind != "Seq" || getSequence.Element is null) throw ModulesExceptionFactory.Error("parse", "TypeMismatch", locus, new { expected = "Seq", actual = getSequence.ToString() }); Require(Arg(1), new TypeRef("I64"), "index"); inferred = getSequence.Element; break;
            case "seq.append":
                Arity(2); var appendSequence = Arg(0); if (appendSequence.Kind != "Seq" || appendSequence.Element is null) throw ModulesExceptionFactory.Error("parse", "TypeMismatch", locus, new { expected = "Seq", actual = appendSequence.ToString() }); Require(Arg(1), appendSequence.Element, "element"); inferred = appendSequence; break;
            case "seq.sum_i64":
                Arity(1); var sumSequence = Arg(0); if (sumSequence.Kind != "Seq" || sumSequence.Element?.Kind != "I64") throw ModulesExceptionFactory.Error("parse", "TypeMismatch", locus, new { expected = "Seq<I64,N>", actual = sumSequence.ToString() }); inferred = new TypeRef("MathInt"); break;
            case "seq.prefix_sum_i64":
                Arity(2); var prefixSequence = Arg(0); if (prefixSequence.Kind != "Seq" || prefixSequence.Element?.Kind != "I64") throw ModulesExceptionFactory.Error("parse", "TypeMismatch", locus, new { expected = "Seq<I64,N>", actual = prefixSequence.ToString() }); Require(Arg(1), new TypeRef("I64"), "prefixLength"); inferred = new TypeRef("MathInt"); break;
            case "forall.sequence":
                if (insideQuantifier) throw ModulesExceptionFactory.Error("parse", "NestedProofQuantifierNotSupported", locus);
                if (expression.BinderId is null || expression.Sequence is null || expression.Body is null)
                    throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", locus);
                if (boundTypes.ContainsKey(expression.BinderId)) throw ModulesExceptionFactory.Error("parse", "DuplicateProofBinder", locus, new { expression.BinderId });
                var quantifiedSequence = ValidateProofExpression(expression.Sequence, foldSequence, accumulator, environment, typeById, boundTypes, false, $"{locus}/sequence");
                if (quantifiedSequence.Kind != "Seq") throw ModulesExceptionFactory.Error("parse", "TypeMismatch", locus, new { expected = "Seq", actual = quantifiedSequence.ToString() });
                var quantifiedBody = ValidateProofExpression(expression.Body, foldSequence, accumulator, environment, typeById, boundTypes.Add(expression.BinderId, new TypeRef("I64")), true, $"{locus}/body");
                Require(quantifiedBody, new TypeRef("Bool"), "body");
                inferred = new TypeRef("Bool");
                break;
            default:
                throw ModulesExceptionFactory.Error("parse", "UnsupportedProofOpcode", locus, new { opcode = expression.Op });
        }
        if (!TypesEquivalent(expression.Type, inferred))
            throw ModulesExceptionFactory.Error("parse", "TypeMismatch", locus, new { op = expression.Op, role = "result", expected = inferred.ToString(), actual = expression.Type.ToString() });
        return inferred;
    }

    private static int ProofWorstCaseCost(ProofExpression expression)
    {
        const int saturation = ProofEvaluationStepsHardMaximum + 1;
        int Add(int left, int right) => left >= saturation - right ? saturation : left + right;
        int Multiply(int left, int right) => left == 0 || right == 0 ? 0 : left > saturation / right ? saturation : left * right;
        var cost = 1;
        foreach (var argument in expression.Args) cost = Add(cost, ProofWorstCaseCost(argument));
        if (expression.Sequence is not null) cost = Add(cost, ProofWorstCaseCost(expression.Sequence));
        if (expression.Body is not null && expression.Op != "forall.sequence") cost = Add(cost, ProofWorstCaseCost(expression.Body));
        if (expression.Op is "seq.sum_i64" or "seq.prefix_sum_i64") cost = Add(cost, expression.Args[0].Type.Capacity ?? 0);
        if (expression.Op == "forall.sequence")
        {
            var capacity = expression.Sequence!.Type.Capacity ?? 0;
            cost = Add(cost, Multiply(capacity, Add(1, ProofWorstCaseCost(expression.Body!))));
        }
        return cost;
    }

    private static ProofExpression ParseProofExpression(
        JsonElement value,
        string locus,
        int depth,
        ImmutableHashSet<string> typeIds,
        StrogoLimits limits,
        ref int nodes)
    {
        if (depth > ProofExpressionDepthHardMaximum)
            throw ModulesExceptionFactory.Error("parse", "ProofExpressionDepthExceeded", locus, new { depth, max = ProofExpressionDepthHardMaximum });
        nodes++;
        if (nodes > ProofExpressionNodesHardMaximum)
            throw ModulesExceptionFactory.Error("parse", "ProofExpressionNodeLimitExceeded", locus, new { actual = nodes, max = ProofExpressionNodesHardMaximum });
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("op", out var opValue))
            throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", locus, new { expected = "proof object" });
        var op = RequireString(opValue);
        if (!SupportedProofOpcodes.Contains(op))
            throw ModulesExceptionFactory.Error("parse", "UnsupportedProofOpcode", locus, new { opcode = op });
        CheckObject(value, locus, ProofExpressionFields(op));
        var type = ParseProofType(value.GetProperty("type"), typeIds, limits);

        return op switch
        {
            "param" => new ProofExpression(op, type, ImmutableArray<ProofExpression>.Empty, ReferenceId: RequireId(value.GetProperty("id"))),
            "proof.bound" => new ProofExpression(op, type, ImmutableArray<ProofExpression>.Empty, BinderId: RequireId(value.GetProperty("binderId"))),
            "fold.environment" => new ProofExpression(op, type, ImmutableArray<ProofExpression>.Empty, Position: RequireInt32(value.GetProperty("position"), $"{locus}/position")),
            "i64.const" => ParseProofNumber(op, type, value.GetProperty("value"), locus, requireI64: true),
            "math.const" => ParseProofNumber(op, type, value.GetProperty("value"), locus, requireI64: false),
            "bool.const" => value.GetProperty("value").ValueKind is JsonValueKind.True or JsonValueKind.False
                ? new ProofExpression(op, type, ImmutableArray<ProofExpression>.Empty, BoolValue: value.GetProperty("value").GetBoolean())
                : throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", locus, new { reason = "ExpectedBool" }),
            "record.make" => new ProofExpression(op, type, ParseProofArguments(value.GetProperty("args"), locus, depth, typeIds, limits, ref nodes), RecordType: RequireId(value.GetProperty("recordType")), FieldIds: ParseIdArray(value.GetProperty("fieldIds"), $"{locus}/fieldIds", "DuplicateFieldId")),
            "record.get" => new ProofExpression(op, type, ParseProofArguments(value.GetProperty("args"), locus, depth, typeIds, limits, ref nodes), ReferenceId: RequireId(value.GetProperty("fieldId"))),
            "seq.empty" => ParseProofEmptySequence(op, type, value, locus, depth, typeIds, limits, ref nodes),
            "forall.sequence" => new ProofExpression(op, type, ImmutableArray<ProofExpression>.Empty,
                BinderId: RequireId(value.GetProperty("binderId")),
                Sequence: ParseProofExpression(value.GetProperty("sequence"), $"{locus}/sequence", depth + 1, typeIds, limits, ref nodes),
                Body: ParseProofExpression(value.GetProperty("body"), $"{locus}/body", depth + 1, typeIds, limits, ref nodes)),
            "fold.prefixLength" or "fold.sequence" or "fold.initialAccumulator" or "fold.accumulator"
                => new ProofExpression(op, type, ImmutableArray<ProofExpression>.Empty),
            _ => new ProofExpression(op, type, ParseProofArguments(value.GetProperty("args"), locus, depth, typeIds, limits, ref nodes))
        };
    }

    private static ProofExpression ParseProofEmptySequence(
        string op,
        TypeRef type,
        JsonElement value,
        string locus,
        int depth,
        ImmutableHashSet<string> typeIds,
        StrogoLimits limits,
        ref int nodes)
    {
        var elementType = ParseType(value.GetProperty("elementType"), 0, limits);
        EnsureTypeRef(elementType, typeIds, $"{locus}/elementType");
        return new ProofExpression(
            op,
            type,
            ParseProofArguments(value.GetProperty("args"), locus, depth, typeIds, limits, ref nodes),
            ElementType: elementType,
            Capacity: RequireInt32(value.GetProperty("capacity"), $"{locus}/capacity"));
    }

    private static ImmutableArray<ProofExpression> ParseProofArguments(
        JsonElement value,
        string locus,
        int depth,
        ImmutableHashSet<string> typeIds,
        StrogoLimits limits,
        ref int nodes)
    {
        RequireArray(value, $"{locus}/args");
        var result = ImmutableArray.CreateBuilder<ProofExpression>();
        var index = 0;
        foreach (var item in value.EnumerateArray())
            result.Add(ParseProofExpression(item, $"{locus}/arg/{index++}", depth + 1, typeIds, limits, ref nodes));
        return result.ToImmutable();
    }

    private static ProofExpression ParseProofNumber(string op, TypeRef type, JsonElement value, string locus, bool requireI64)
    {
        var text = RequireString(value);
        if (!CanonicalI64Pattern.IsMatch(text) || (requireI64 && !long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)))
            throw ModulesExceptionFactory.Error("parse", requireI64 ? "InvalidI64" : "SchemaInvalid", locus, new { value = text, reason = "InvalidCanonicalInteger" });
        return new ProofExpression(op, type, ImmutableArray<ProofExpression>.Empty, NumberValue: text);
    }

    private static TypeRef ParseProofType(JsonElement value, ImmutableHashSet<string> typeIds, StrogoLimits limits)
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString() == "MathInt") return new TypeRef("MathInt");
        var type = ParseType(value, 0, limits);
        EnsureTypeRef(type, typeIds, "proof.type");
        if (ContainsMathInt(type)) throw ModulesExceptionFactory.Error("parse", "TypeMismatch", details: new { expected = "executable proof type", actual = type.ToString() });
        return type;

        static bool ContainsMathInt(TypeRef candidate)
            => candidate.Kind == "MathInt" || candidate.Element is not null && ContainsMathInt(candidate.Element);
    }

    private static IReadOnlyCollection<string> ProofExpressionFields(string op) => op switch
    {
        "param" => ["op", "type", "id"],
        "proof.bound" => ["op", "type", "binderId"],
        "fold.environment" => ["op", "type", "position"],
        "i64.const" or "math.const" or "bool.const" => ["op", "type", "value"],
        "record.make" => ["op", "type", "args", "recordType", "fieldIds"],
        "record.get" => ["op", "type", "args", "fieldId"],
        "seq.empty" => ["op", "type", "args", "elementType", "capacity"],
        "forall.sequence" => ["op", "type", "binderId", "sequence", "body"],
        "fold.prefixLength" or "fold.sequence" or "fold.initialAccumulator" or "fold.accumulator" => ["op", "type"],
        _ => ["op", "type", "args"]
    };

    private static TypeRef ParseType(JsonElement value, int depth, StrogoLimits limits)
    {
        if (depth > limits.MaxTypeDepth) throw ModulesExceptionFactory.Error("parse", "TypeDepthExceeded", new { depth, max = limits.MaxTypeDepth });
        if (value.ValueKind == JsonValueKind.String)
        {
            var typeName = RequireString(value);
            return typeName switch { "I64" or "Bool" => new TypeRef(typeName), _ => new TypeRef("Record", Name: typeName) };
        }
        CheckObject(value, "type", ["kind", "elementType", "capacity"]);
        var kind = RequireString(value.GetProperty("kind"));
        if (kind != "Seq") throw ModulesExceptionFactory.Error("parse", "UnsupportedTypeKind", new { kind });
        var capacity = RequireInt32(value.GetProperty("capacity"), "type.capacity");
        if (capacity < 0 || capacity > 256) throw ModulesExceptionFactory.Error("parse", "SeqCapacityExceeded", new { capacity, max = 256 });
        var elementType = ParseType(value.GetProperty("elementType"), depth + 1, limits);
        if (elementType.Kind == "Seq") throw ModulesExceptionFactory.Error("parse", "NestedSequenceNotSupported");
        return new TypeRef("Seq", Element: elementType, Capacity: capacity);
    }

    internal static bool TypesEquivalent(TypeRef left, TypeRef right)
    {
        if (left.Kind != right.Kind) return false;
        if (left.Kind == "Record") return left.Name == right.Name;
        if (left.Kind == "Seq") return left.Capacity == right.Capacity && left.Element is not null && right.Element is not null && TypesEquivalent(left.Element, right.Element);
        return true;
    }

    private static JsonElement RequireProperty(JsonElement obj, string name, string path)
    {
        if (!obj.TryGetProperty(name, out var value)) throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", new { path, reason = "MissingField", field = name });
        return value;
    }

    private static void CheckObject(JsonElement obj, string path, IReadOnlyCollection<string> requiredFields)
    {
        if (obj.ValueKind != JsonValueKind.Object) throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", new { path, expected = "object", actual = obj.ValueKind.ToString() });
        var actual = obj.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var expected = requiredFields.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected)) throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", new { path, reason = "UnexpectedOrMissingFields", expected, actual });
    }

    private static string RequireId(JsonElement value)
    {
        var text = RequireString(value);
        if (!IdPattern.IsMatch(text)) throw ModulesExceptionFactory.Error("parse", "InvalidId", new { value = text });
        return text;
    }

    private static string RequireDigest(JsonElement value)
    {
        var text = RequireString(value);
        if (!DigestPattern.IsMatch(text)) throw ModulesExceptionFactory.Error("parse", "InvalidDigest", new { value = text });
        return text;
    }

    private static int RequireInt32(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", new { path, reason = "ExpectedCanonicalNaturalString" });
        var text = value.GetString()!;
        if ((text.Length > 1 && text[0] == '0') || text.Length == 0 || text.Any(character => character is < '0' or > '9') || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", new { path, reason = "InvalidCanonicalNaturalString", value = text });
        return number;
    }

    private static void RequireArray(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array) throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", new { path, expected = "array", actual = value.ValueKind.ToString() });
    }

    private static string RequireString(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", new { reason = "ExpectedString", actual = value.ValueKind.ToString() });
        return value.GetString()!;
    }

    private static IEnumerable<JsonElement> OrderByEntityId(IEnumerable<JsonElement> values, string propertyName = "id")
        => values.OrderBy(value => SortId(value, propertyName), StringComparer.Ordinal)
            .ThenBy(value => value.GetRawText(), StringComparer.Ordinal);

    private static string SortId(JsonElement value, string propertyName)
        => value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(propertyName, out var id)
            && id.ValueKind == JsonValueKind.String
                ? id.GetString()!
                : "\uFFFF";

    private static string ScalarSortId(JsonElement value)
        => value.ValueKind == JsonValueKind.String ? value.GetString()! : "\uFFFF";

    private static void ValidateLimits(StrogoLimits limits)
    {
        if (limits.MaxTransportBytes is <= 0 or > StrogoLimits.TransportBytesHardMaximum
            || limits.MaxJsonDepth is <= 0 or > StrogoLimits.JsonDepthHardMaximum
            || limits.MaxTypes is <= 0 or > StrogoLimits.TypesHardMaximum
            || limits.MaxImports is < 0 or > StrogoLimits.ImportsHardMaximum
            || limits.MaxFunctions is <= 0 or > StrogoLimits.FunctionsHardMaximum
            || limits.MaxNodesPerFunction is <= 0 or > StrogoLimits.NodesPerFunctionHardMaximum
            || limits.MaxTotalNodes is <= 0 or > StrogoLimits.TotalNodesHardMaximum
            || limits.MaxTypeDepth is <= 0 or > StrogoLimits.TypeDepthHardMaximum
            || limits.MaxRegionDepth is <= 0 or > StrogoLimits.RegionDepthHardMaximum)
            throw ModulesExceptionFactory.Error("parse", "InvalidLimits");
    }
}
