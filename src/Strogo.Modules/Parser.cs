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
        "record.make", "record.get", "seq.empty", "seq.length", "seq.get", "seq.append", "call");

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
            totalNodes = checked(totalNodes + body.Nodes.Length);
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
        var bodyParameters = ParseIdArray(function.Body.GetProperty("parameters"), "function.body.parameters", "DuplicateParameterId");
        var functionParameters = function.Parameters.Select(parameter => parameter.Id).ToImmutableArray();
        if (!bodyParameters.SequenceEqual(functionParameters))
            throw ModulesExceptionFactory.Error("parse", "BodyParametersMismatch", details: new { declared = functionParameters, body = bodyParameters });

        var nodesElement = function.Body.GetProperty("nodes");
        RequireArray(nodesElement, "function.body.nodes");
        var nodeValues = nodesElement.EnumerateArray().ToArray();
        if (nodeValues.Length > limits.MaxNodesPerFunction)
            throw ModulesExceptionFactory.Error("parse", "FunctionNodeLimitExceeded", details: new { actual = nodeValues.Length, max = limits.MaxNodesPerFunction });

        var nodes = ImmutableArray.CreateBuilder<FunctionNode>(nodeValues.Length);
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nodeValue in OrderByEntityId(nodeValues))
        {
            if (nodeValue.ValueKind != JsonValueKind.Object)
                throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", details: new { path = "function.body.nodes[]", expected = "object" });
            var op = RequireString(RequireProperty(nodeValue, "op", "function.body.nodes[]"));
            if (!SupportedOpcodes.Contains(op))
                throw ModulesExceptionFactory.Error("parse", "UnsupportedOpcode", new { opcode = op });
            CheckObject(nodeValue, "function.body.nodes[]", ExpectedNodeFields(op));
            var nodeId = RequireId(nodeValue.GetProperty("id"));
            if (functionParameters.Contains(nodeId) || !nodeIds.Add(nodeId))
                throw ModulesExceptionFactory.Error("parse", "DuplicateValueId", nodeId);
            var nodeType = ParseType(nodeValue.GetProperty("type"), 0, limits);
            EnsureTypeRef(nodeType, typeIds, $"node.{nodeId}.type");
            nodes.Add(new FunctionNode(nodeId, op, nodeType, ParseIdArray(nodeValue.GetProperty("args"), "function.node.args", null), ParseNodeMetadata(nodeValue, nodeId, op, limits)));
        }

        var nodeArray = nodes.ToImmutable();
        var parameterTypes = function.Parameters.ToDictionary(parameter => parameter.Id, parameter => parameter.Type, StringComparer.Ordinal);
        var nodeTypes = nodeArray.ToDictionary(node => node.Id, node => node.Type, StringComparer.Ordinal);
        foreach (var node in nodeArray.OrderBy(node => node.Id, StringComparer.Ordinal))
            EnsureTypeRules(node, parameterTypes, nodeTypes, functions, typeById);

        var resultId = RequireId(function.Body.GetProperty("result"));
        if (!parameterTypes.ContainsKey(resultId) && !nodeTypes.ContainsKey(resultId))
            throw ModulesExceptionFactory.Error("parse", "UnknownResultValue", resultId);
        EnsureDAG(nodeArray, functionParameters);
        EnsureReachable(nodeArray, functionParameters, resultId);
        var resultType = parameterTypes.TryGetValue(resultId, out var parameterType) ? parameterType : nodeTypes[resultId];
        if (!TypesEquivalent(resultType, function.ReturnType))
            throw ModulesExceptionFactory.Error("parse", "ReturnTypeMismatch", details: new { functionReturnType = function.ReturnType, bodyResultType = resultType, resultId });
        return new FunctionBody(functionParameters, nodeArray, resultId);
    }

    private static IReadOnlyCollection<string> ExpectedNodeFields(string op) => op switch
    {
        "i64.const" or "bool.const" => ["id", "op", "type", "args", "value"],
        "record.make" => ["id", "op", "type", "args", "recordType", "fieldIds"],
        "record.get" => ["id", "op", "type", "args", "fieldId"],
        "seq.empty" => ["id", "op", "type", "args", "elementType", "capacity"],
        "call" => ["id", "op", "type", "args", "functionRef"],
        _ => ["id", "op", "type", "args"]
    };

    private static NodeMetadata ParseNodeMetadata(JsonElement node, string nodeId, string op, StrogoLimits limits)
    {
        if (op == "i64.const")
        {
            var literal = node.GetProperty("value");
            var text = literal.ValueKind == JsonValueKind.String
                ? literal.GetString()!
                : throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", new { node = nodeId, property = "value", reason = "ExpectedString" });
            if (!CanonicalI64Pattern.IsMatch(text) || !long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                throw ModulesExceptionFactory.Error("parse", "InvalidI64", nodeId, new { value = text });
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
            return NodeMetadata.Empty with { RecordType = RequireId(node.GetProperty("recordType")), FieldIds = ParseIdArray(node.GetProperty("fieldIds"), $"node.{nodeId}.fieldIds", "DuplicateFieldId") };
        if (op == "record.get")
            return NodeMetadata.Empty with { FieldId = RequireId(node.GetProperty("fieldId")) };
        if (op == "seq.empty")
            return NodeMetadata.Empty with { ElementType = ParseType(node.GetProperty("elementType"), 1, limits), Capacity = RequireInt32(node.GetProperty("capacity"), $"node.{nodeId}.capacity") };
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
        IReadOnlyDictionary<string, TypeRef> parameterTypes,
        IReadOnlyDictionary<string, TypeRef> nodeTypes,
        IReadOnlyDictionary<string, FunctionHeader> functions,
        IReadOnlyDictionary<string, TypeDecl> typeById)
    {
        TypeRef ArgType(int index)
        {
            var id = node.Args[index];
            if (parameterTypes.TryGetValue(id, out var parameterType)) return parameterType;
            if (nodeTypes.TryGetValue(id, out var nodeType)) return nodeType;
            throw ModulesExceptionFactory.Error("parse", "DanglingNodeArg", node.Id, new { reference = id });
        }
        void Arity(int expected)
        {
            if (node.Args.Length != expected)
                throw ModulesExceptionFactory.Error("parse", "ArityMismatch", node.Id, new { op = node.Op, expected, actual = node.Args.Length });
        }
        void TypeIs(TypeRef actual, TypeRef expected, string role)
        {
            if (!TypesEquivalent(actual, expected))
                throw ModulesExceptionFactory.Error("parse", "TypeMismatch", node.Id, new { op = node.Op, role, expected = expected.ToString(), actual = actual.ToString() });
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
            case "record.make": ValidateRecordMake(node, ArgType, TypeIs, typeById); return;
            case "record.get":
                Arity(1);
                var record = ArgType(0);
                if (record.Kind != "Record" || record.Name is null || !typeById.TryGetValue(record.Name, out var declaration))
                    throw ModulesExceptionFactory.Error("parse", "TypeMismatch", node.Id, new { op = node.Op, expected = "Record", actual = record.ToString() });
                var field = declaration.Fields.SingleOrDefault(candidate => candidate.Id == node.Metadata.FieldId);
                if (field is null)
                    throw ModulesExceptionFactory.Error("parse", "UnknownRecordField", node.Id, new { recordType = record.Name, fieldId = node.Metadata.FieldId });
                TypeIs(node.Type, field.Type, "result"); return;
            case "seq.empty":
                Arity(0); TypeIs(node.Type, new TypeRef("Seq", Element: node.Metadata.ElementType, Capacity: node.Metadata.Capacity), "result"); return;
            case "seq.length":
                Arity(1);
                if (ArgType(0).Kind != "Seq") throw ModulesExceptionFactory.Error("parse", "TypeMismatch", node.Id, new { op = node.Op, expected = "Seq", actual = ArgType(0).ToString() });
                TypeIs(node.Type, new TypeRef("I64"), "result"); return;
            case "seq.get":
                Arity(2);
                var sequence = ArgType(0);
                if (sequence.Kind != "Seq" || sequence.Element is null) throw ModulesExceptionFactory.Error("parse", "TypeMismatch", node.Id, new { op = node.Op, expected = "Seq", actual = sequence.ToString() });
                TypeIs(ArgType(1), new TypeRef("I64"), "index"); TypeIs(node.Type, sequence.Element, "result"); return;
            case "seq.append":
                Arity(2);
                var source = ArgType(0);
                if (source.Kind != "Seq" || source.Element is null) throw ModulesExceptionFactory.Error("parse", "TypeMismatch", node.Id, new { op = node.Op, expected = "Seq", actual = source.ToString() });
                TypeIs(ArgType(1), source.Element, "element"); TypeIs(node.Type, source, "result"); return;
            case "call":
                if (node.Metadata.FunctionRef is null || !functions.TryGetValue(node.Metadata.FunctionRef, out var callee))
                    throw ModulesExceptionFactory.Error("parse", "UnknownFunction", node.Id, new { functionRef = node.Metadata.FunctionRef });
                Arity(callee.Parameters.Length);
                for (var index = 0; index < callee.Parameters.Length; index++) TypeIs(ArgType(index), callee.Parameters[index].Type, $"arg{index}");
                TypeIs(node.Type, callee.ReturnType, "result"); return;
            default: throw ModulesExceptionFactory.Error("parse", "UnsupportedOpcode", new { node.Id, node.Op });
        }
    }

    private static void ValidateRecordMake(FunctionNode node, Func<int, TypeRef> argType, Action<TypeRef, TypeRef, string> typeIs, IReadOnlyDictionary<string, TypeDecl> typeById)
    {
        if (node.Type.Kind != "Record" || node.Type.Name is null || node.Metadata.RecordType != node.Type.Name || !typeById.TryGetValue(node.Type.Name, out var declaration))
            throw ModulesExceptionFactory.Error("parse", "TypeMismatch", node.Id, new { op = node.Op, expected = node.Metadata.RecordType, actual = node.Type.ToString() });
        var expectedIds = declaration.Fields.Select(field => field.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var actualIds = node.Metadata.FieldIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (node.Args.Length != node.Metadata.FieldIds.Length || !actualIds.SequenceEqual(expectedIds, StringComparer.Ordinal))
            throw ModulesExceptionFactory.Error("parse", "RecordFieldMismatch", node.Id, new { recordType = declaration.Id, expected = expectedIds, actual = actualIds });
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
        var calls = functions.ToDictionary(function => function.Id, function => function.Body.Nodes.Where(node => node.Op == "call")
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

    private static void EnsureDAG(ImmutableArray<FunctionNode> nodes, ImmutableArray<string> parameters)
    {
        var nodeIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var inDegree = nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        var users = nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var node in nodes)
            foreach (var arg in node.Args.Distinct(StringComparer.Ordinal))
            {
                if (!parameters.Contains(arg) && !nodeIds.Contains(arg)) throw ModulesExceptionFactory.Error("parse", "DanglingNodeArg", node.Id, new { reference = arg });
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
        if (count != nodes.Length) throw ModulesExceptionFactory.Error("parse", "CycleDetected");
    }

    private static void EnsureReachable(ImmutableArray<FunctionNode> nodes, ImmutableArray<string> parameters, string resultId)
    {
        var byId = nodes.ToDictionary(node => node.Id, node => node, StringComparer.Ordinal);
        var pending = new Stack<string>([resultId]);
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryPop(out var id))
        {
            if (parameters.Contains(id) || !reachable.Add(id)) continue;
            if (!byId.TryGetValue(id, out var node)) throw ModulesExceptionFactory.Error("parse", "UnknownResultValue", resultId);
            foreach (var arg in node.Args) pending.Push(arg);
        }
        var unreachable = byId.Keys.Where(id => !reachable.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (unreachable.Length > 0) throw ModulesExceptionFactory.Error("parse", "UnreachableNode", new { result = resultId, unreachable });
    }

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
            || limits.MaxTypeDepth is <= 0 or > StrogoLimits.TypeDepthHardMaximum)
            throw ModulesExceptionFactory.Error("parse", "InvalidLimits");
    }
}
