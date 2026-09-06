using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kernel.Core;

namespace Strogo.Modules;

public static class ModulesParser
{
    private static readonly Regex IdPattern = new("^[A-Za-z][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly ImmutableHashSet<string> SupportedOpcodes = ImmutableHashSet.Create(StringComparer.Ordinal,
        "i64.const", "bool.const", "i64.add", "i64.sub", "i64.le", "i64.eq", "bool.not", "bool.and", "bool.or");

    public static ModuleParseResult ParseModule(string source, StrogoLimits? limits = null)
        => ParseModule(Encoding.UTF8.GetBytes(source), limits);

    public static ModuleParseResult ParseModule(byte[] source, StrogoLimits? limits = null)
    {
        limits ??= new StrogoLimits();
        ValidateLimits(limits);

        var lexed = ModuleLexer.Lex(source, limits.MaxTransportBytes);

        using var doc = CanonicalJson.ParseStrict(
            Encoding.UTF8.GetString(source),
            new CoreLimits
            {
                MaxTransportBytes = limits.MaxTransportBytes,
                MaxJsonDepth = Math.Min(limits.MaxJsonDepth, 32),
                MaxNodes = Math.Min(limits.MaxNodesPerFunction, 128),
                SolverTimeoutMilliseconds = 5000,
                AdmissionTimeoutMilliseconds = 15000
            });

        var module = ParseModule(doc.RootElement, limits);
        var canonical = ModulesCodec.Canonicalize(module);
        var digest = ModulesCodec.SourceDigest(canonical);

        return new ModuleParseResult(module, lexed, canonical, digest, "parse");
    }

    private static ModuleSource ParseModule(JsonElement root, StrogoLimits limits)
    {
        CheckObject(root, "module", ["schemaVersion", "moduleId", "types", "imports", "functions", "exports"]);

        var schemaVersion = RequireString(root.GetProperty("schemaVersion"));
        if (schemaVersion != StrogoVersions.SchemaVersion)
        {
            throw ModulesExceptionFactory.Error("parse", "SchemaVersionMismatch",
                details: new { expected = StrogoVersions.SchemaVersion, actual = schemaVersion });
        }

        var moduleId = RequireId(root.GetProperty("moduleId"));
        var types = ParseTypes(root.GetProperty("types"), limits);
        var typeIds = types.Select(type => type.Id).ToImmutableHashSet(StringComparer.Ordinal);

        var imports = ParseImports(root.GetProperty("imports"));
        var functions = ParseFunctions(root.GetProperty("functions"), typeIds, limits);
        var exports = ParseExports(root.GetProperty("exports"),
            functions.Select(function => function.Id).ToImmutableHashSet(StringComparer.Ordinal), limits);

        if (imports.Length > limits.MaxFunctions)
            throw ModulesExceptionFactory.Error("parse", "ImportLimitExceeded", details: new { actual = imports.Length, max = limits.MaxFunctions });

        if (functions.Length > limits.MaxFunctions)
            throw ModulesExceptionFactory.Error("parse", "FunctionLimitExceeded", details: new { actual = functions.Length, max = limits.MaxFunctions });

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

        foreach (var item in raw)
        {
            CheckObject(item, "type", ["id", "fields"]);
            var id = RequireId(item.GetProperty("id"));

            if (!ids.Add(id))
            {
                throw ModulesExceptionFactory.Error("parse", "DuplicateTypeId", id);
            }

            var fieldsElement = item.GetProperty("fields");
            if (fieldsElement.ValueKind != JsonValueKind.Array)
                throw ModulesExceptionFactory.Error("parse", "SchemaInvalid",
                    details: new { path = "type.fields", reason = "ExpectedArray" });

            var fieldValues = fieldsElement.EnumerateArray().ToArray();
            var fields = ImmutableArray.CreateBuilder<RecordField>(fieldValues.Length);
            var fieldIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var field in fieldValues)
            {
                CheckObject(field, "type.field", ["id", "type"]);

                var fieldId = RequireId(field.GetProperty("id"));
                if (!fieldIds.Add(fieldId))
                    throw ModulesExceptionFactory.Error("parse", "DuplicateFieldId", fieldId,
                        new { type = id, field = fieldId });

                var fieldType = ParseType(field.GetProperty("type"), 1, limits);
                fields.Add(new RecordField(fieldId, fieldType));
            }

            result.Add(new TypeDecl(id, fields.ToImmutable()));
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<ImportDecl> ParseImports(JsonElement value)
    {
        RequireArray(value, "imports");

        var result = ImmutableArray.CreateBuilder<ImportDecl>();
        foreach (var importValue in value.EnumerateArray())
        {
            CheckObject(importValue, "import", ["moduleId", "sourceDigest", "contractDigest"]);

            var moduleId = RequireId(importValue.GetProperty("moduleId"));
            var sourceDigest = RequireDigest(importValue.GetProperty("sourceDigest"));
            var contractDigest = RequireDigest(importValue.GetProperty("contractDigest"));

            result.Add(new ImportDecl(moduleId, sourceDigest, contractDigest));
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<FunctionDecl> ParseFunctions(JsonElement value, ImmutableHashSet<string> typeIds, StrogoLimits limits)
    {
        RequireArray(value, "functions");

        var values = value.EnumerateArray().ToArray();
        if (values.Length > limits.MaxFunctions)
            throw ModulesExceptionFactory.Error("parse", "FunctionLimitExceeded", details: new { actual = values.Length, max = limits.MaxFunctions });

        var result = ImmutableArray.CreateBuilder<FunctionDecl>(values.Length);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in values)
        {
            CheckObject(item, "function", ["id", "parameters", "returnType", "contractRef", "body"]);

            var id = RequireId(item.GetProperty("id"));
            if (!ids.Add(id))
                throw ModulesExceptionFactory.Error("parse", "DuplicateFunctionId", id);

            var parameters = ParseParameters(item.GetProperty("parameters"), limits);
            var parameterTypes = parameters.ToDictionary(p => p.Id, p => p.Type, StringComparer.Ordinal);
            var returnType = ParseType(item.GetProperty("returnType"), 0, limits);
            EnsureTypeRef(returnType, typeIds, $"function.{id}.returnType");

            var contractRef = RequireId(item.GetProperty("contractRef"));
            var bodyElement = item.GetProperty("body");
            CheckObject(bodyElement, "function.body", ["parameters", "nodes", "result"]);

            var body = ParseFunctionBody(bodyElement, parameters, returnType, parameterTypes, typeIds, limits);
            result.Add(new FunctionDecl(id, parameters, returnType, contractRef, body));
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<FunctionParameter> ParseParameters(JsonElement value, StrogoLimits limits)
    {
        RequireArray(value, "function.parameters");

        var values = value.EnumerateArray().ToArray();
        if (values.Length > limits.MaxNodesPerFunction)
            throw ModulesExceptionFactory.Error("parse", "FunctionNodeLimitExceeded",
                details: new { actual = values.Length, max = limits.MaxNodesPerFunction });

        var result = ImmutableArray.CreateBuilder<FunctionParameter>(values.Length);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in values)
        {
            CheckObject(item, "function.parameter", ["id", "type"]);

            var id = RequireId(item.GetProperty("id"));
            if (!ids.Add(id))
                throw ModulesExceptionFactory.Error("parse", "DuplicateParameterId", id);

            var type = ParseType(item.GetProperty("type"), 0, limits);
            result.Add(new FunctionParameter(id, type));
        }

        return result.ToImmutable();
    }

    private static FunctionBody ParseFunctionBody(
        JsonElement value,
        ImmutableArray<FunctionParameter> parameters,
        TypeRef returnType,
        IReadOnlyDictionary<string, TypeRef> parameterTypes,
        ImmutableHashSet<string> typeIds,
        StrogoLimits limits)
    {
        var bodyParameters = ParseBodyParameters(value.GetProperty("parameters"));
        var functionParameters = parameters.Select(parameter => parameter.Id).ToImmutableArray();

        if (!bodyParameters.SequenceEqual(functionParameters))
            throw ModulesExceptionFactory.Error("parse", "BodyParametersMismatch",
                details: new { declared = functionParameters, body = bodyParameters });

        var nodesElement = value.GetProperty("nodes");
        RequireArray(nodesElement, "function.body.nodes");

        var nodeValues = nodesElement.EnumerateArray().ToArray();
        if (nodeValues.Length > limits.MaxNodesPerFunction)
            throw ModulesExceptionFactory.Error("parse", "FunctionNodeLimitExceeded",
                details: new { actual = nodeValues.Length, max = limits.MaxNodesPerFunction });

        var nodes = ImmutableArray.CreateBuilder<FunctionNode>(nodeValues.Length);
        var nodeTypeById = new Dictionary<string, TypeRef>(StringComparer.Ordinal);

        foreach (var node in nodeValues)
        {
            CheckObject(node, "function.body.nodes[]", ["id", "op", "type", "args", "value"]);

            var nodeId = RequireId(node.GetProperty("id"));
            if (!nodeTypeById.TryAdd(nodeId, null!))
                throw ModulesExceptionFactory.Error("parse", "DuplicateNodeId", nodeId);

            var op = RequireString(node.GetProperty("op"));
            if (!SupportedOpcodes.Contains(op))
                throw ModulesExceptionFactory.Error("parse", "UnsupportedOpcode", new { opcode = op, node = nodeId });

            var nodeType = ParseType(node.GetProperty("type"), 0, limits);
            EnsureTypeRef(nodeType, typeIds, $"node.{nodeId}.type");
            ValidateNodeType(nodeId, op, nodeType);

            var args = ParseNodeArgs(node.GetProperty("args"), functionParameters, nodeTypeById);
            EnsureTypeRules(nodeId, op, args, parameterTypes, nodeTypeById);

            var nodeValue = ParseNodeValue(nodeId, op, node.GetProperty("value"));

            nodeTypeById[nodeId] = nodeType;
            nodes.Add(new FunctionNode(nodeId, op, nodeType, args, nodeValue));
        }

        var resultId = RequireId(value.GetProperty("result"));
        if (!nodeTypeById.ContainsKey(resultId))
            throw ModulesExceptionFactory.Error("parse", "UnknownResultNode", resultId);

        var nodeArray = nodes.ToImmutable();
        EnsureDAG(nodeArray, functionParameters, resultId);
        EnsureReachable(nodeArray, functionParameters, resultId);

        var lastNode = nodeArray.First(node => node.Id == resultId);
        if (!TypesEquivalent(lastNode.Type, returnType))
            throw ModulesExceptionFactory.Error("parse", "ReturnTypeMismatch",
                details: new { functionReturnType = returnType, bodyResultType = lastNode.Type, resultNodeId = resultId });

        return new FunctionBody(functionParameters, nodeArray, resultId);
    }

    private static ImmutableArray<string> ParseBodyParameters(JsonElement value)
    {
        RequireArray(value, "function.body.parameters");
        var array = value.EnumerateArray().ToArray();
        var result = ImmutableArray.CreateBuilder<string>(array.Length);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array)
        {
            var id = RequireId(item);
            if (!ids.Add(id))
                throw ModulesExceptionFactory.Error("parse", "DuplicateParameterId", id);

            result.Add(id);
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<string> ParseNodeArgs(JsonElement value, ImmutableArray<string> bodyParameters, Dictionary<string, TypeRef> nodeTypeById)
    {
        RequireArray(value, "function.node.args");

        var values = value.EnumerateArray().ToArray();
        var result = ImmutableArray.CreateBuilder<string>(values.Length);

        foreach (var arg in values)
        {
            var argId = RequireId(arg);
            if (!bodyParameters.Contains(argId) && !nodeTypeById.ContainsKey(argId))
                throw ModulesExceptionFactory.Error("parse", "DanglingNodeArg", new { reference = argId });

            result.Add(argId);
        }

        return result.ToImmutable();
    }

    private static string? ParseNodeValue(string nodeId, string op, JsonElement value)
    {
        if (op is "i64.const")
        {
            var text = value.ValueKind == JsonValueKind.String
                ? value.GetString()!
                : throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", new { node = nodeId, property = "value", reason = "ExpectedString" });

            if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                throw ModulesExceptionFactory.Error("parse", "InvalidI64", nodeId, new { value = text });

            return text;
        }

        if (op is "bool.const")
        {
            if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", new { node = nodeId, property = "value", reason = "ExpectedBool" });

            return value.ValueKind == JsonValueKind.True ? "true" : "false";
        }

        if (value.ValueKind != JsonValueKind.Null)
            throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", new { node = nodeId, property = "value", reason = "NotExpected" });

        return null;
    }

    private static ImmutableArray<string> ParseExports(JsonElement value, ImmutableHashSet<string> functionIds, StrogoLimits limits)
    {
        RequireArray(value, "exports");

        var values = value.EnumerateArray().ToArray();
        if (values.Length > limits.MaxFunctions)
            throw ModulesExceptionFactory.Error("parse", "ExportLimitExceeded",
                details: new { actual = values.Length, max = limits.MaxFunctions });

        var result = ImmutableArray.CreateBuilder<string>(values.Length);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in values)
        {
            var id = RequireId(item);
            if (!ids.Add(id))
                throw ModulesExceptionFactory.Error("parse", "DuplicateExport", id);

            if (!functionIds.Contains(id))
                throw ModulesExceptionFactory.Error("parse", "ExportedFunctionMissing", id);

            result.Add(id);
        }

        return result.ToImmutable();
    }

    private static void EnsureTypeRef(TypeRef type, ImmutableHashSet<string> typeIds, string path)
    {
        if (type.Kind == "Record")
        {
            if (type.Name is null || !typeIds.Contains(type.Name))
                throw ModulesExceptionFactory.Error("parse", "UnknownType", new { path, type = type.Name });
        }

        if (type.Kind == "Seq")
        {
            if (type.Element is null)
                throw ModulesExceptionFactory.Error("parse", "InvalidType", new { path, reason = "MissingElementType" });

            if (type.Capacity is null or > 256 or < 0)
                throw ModulesExceptionFactory.Error("parse", "InvalidSeqCapacity", new { path, capacity = type.Capacity ?? -1 });

            if (type.Element.Kind == "Seq")
                throw ModulesExceptionFactory.Error("parse", "NestedSequenceNotSupported", new { path });

            EnsureTypeRef(type.Element, typeIds, path);
        }
    }

    private static void EnsureTypeRules(
        string nodeId,
        string op,
        ImmutableArray<string> args,
        IReadOnlyDictionary<string, TypeRef> parameterTypes,
        IReadOnlyDictionary<string, TypeRef> nodeTypes)
    {
        var inputExpected = op switch
        {
            "i64.const" => "",
            "bool.const" => "",
            "i64.add" => "I64",
            "i64.sub" => "I64",
            "i64.le" => "I64",
            "i64.eq" => "I64",
            "bool.not" => "Bool",
            "bool.and" => "Bool",
            "bool.or" => "Bool",
            _ => throw ModulesExceptionFactory.Error("parse", "UnsupportedOpcode", new { nodeId, op })
        };

        var arity = op switch
        {
            "i64.const" or "bool.const" => 0,
            "bool.not" => 1,
            _ => 2
        };

        if (args.Length != arity)
            throw ModulesExceptionFactory.Error("parse", "ArityMismatch", nodeId,
                new { op, expected = arity, actual = args.Length });

        if (arity == 0)
            return;

        foreach (var arg in args)
        {
            var argType = parameterTypes.TryGetValue(arg, out var parameterType)
                ? parameterType
                : nodeTypes[arg];

            if (argType.Kind != inputExpected)
                throw ModulesExceptionFactory.Error("parse", "TypeMismatch", nodeId,
                    new { op, argument = arg, expected = inputExpected, actual = argType.Kind });
        }
    }

    private static void ValidateNodeType(string nodeId, string op, TypeRef nodeType)
    {
        var expected = op switch
        {
            "i64.const" or "i64.add" or "i64.sub" => "I64",
            "i64.le" or "i64.eq" => "Bool",
            "bool.const" or "bool.not" or "bool.and" or "bool.or" => "Bool",
            _ => throw ModulesExceptionFactory.Error("parse", "UnsupportedOpcode", new { nodeId, op })
        };

        if (nodeType.Kind != expected)
            throw ModulesExceptionFactory.Error("parse", "TypeMismatch", nodeId,
                new { op, expected, actual = nodeType.Kind });
    }

    private static void EnsureDAG(ImmutableArray<FunctionNode> nodes, ImmutableArray<string> parameters, string resultId)
    {
        var nodeIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var inDegree = nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        var users = nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            foreach (var arg in node.Args.Distinct(StringComparer.Ordinal))
            {
                if (!parameters.Contains(arg) && !nodeIds.Contains(arg))
                    throw ModulesExceptionFactory.Error("parse", "DanglingArg", new { node = node.Id, arg });

                if (nodeIds.Contains(arg))
                {
                    inDegree[node.Id]++;
                    users[arg].Add(node.Id);
                }
            }
        }

        var ready = new SortedSet<string>(inDegree.Where(item => item.Value == 0).Select(item => item.Key), StringComparer.Ordinal);
        var ordered = new HashSet<string>(StringComparer.Ordinal);

        while (ready.Count > 0)
        {
            var current = ready.Min!;
            ready.Remove(current);
            ordered.Add(current);

            foreach (var user in users[current])
            {
                if (--inDegree[user] == 0)
                    ready.Add(user);
            }
        }

        if (ordered.Count != nodes.Length)
            throw ModulesExceptionFactory.Error("parse", "CycleDetected");

        if (!ordered.Contains(resultId))
            throw ModulesExceptionFactory.Error("parse", "UnknownResultNode", resultId);
    }

    private static void EnsureReachable(ImmutableArray<FunctionNode> nodes, ImmutableArray<string> parameters, string resultId)
    {
        var byId = nodes.ToDictionary(node => node.Id, node => node, StringComparer.Ordinal);
        var pending = new Stack<string>([resultId]);
        var reachable = new HashSet<string>(StringComparer.Ordinal);

        while (pending.TryPop(out var id))
        {
            if (!reachable.Add(id))
                continue;

            if (!byId.TryGetValue(id, out var node))
                throw ModulesExceptionFactory.Error("parse", "UnknownResultNode", resultId);

            foreach (var arg in node.Args)
            {
                if (!parameters.Contains(arg))
                    pending.Push(arg);
            }
        }

        var unreachable = byId.Keys.Where(id => !reachable.Contains(id)).ToArray();
        if (unreachable.Length > 0)
            throw ModulesExceptionFactory.Error("parse", "UnreachableNode", new { result = resultId, unreachable });
    }

    private static TypeRef ParseType(JsonElement value, int depth, StrogoLimits limits)
    {
        if (depth > limits.MaxTypeDepth)
            throw ModulesExceptionFactory.Error("parse", "TypeDepthExceeded", new { depth, max = limits.MaxTypeDepth });

        if (value.ValueKind == JsonValueKind.String)
        {
            var typeName = RequireString(value);
            return typeName switch
            {
                "I64" or "Bool" => new TypeRef(typeName),
                _ => new TypeRef("Record", Name: typeName)
            };
        }

        CheckObject(value, "type", ["kind", "elementType", "capacity"]);

        var kind = RequireString(value.GetProperty("kind"));
        if (kind != "Seq")
            throw ModulesExceptionFactory.Error("parse", "UnsupportedTypeKind", new { kind });

        var capacityValue = value.GetProperty("capacity");
        if (capacityValue.ValueKind != JsonValueKind.Number)
            throw ModulesExceptionFactory.Error("parse", "SchemaInvalid", new { path = "type.capacity", reason = "ExpectedNumber" });

        var capacity = capacityValue.GetInt32();
        if (capacity < 0 || capacity > 256)
            throw ModulesExceptionFactory.Error("parse", "SeqCapacityExceeded", new { capacity, max = 256 });

        var elementType = ParseType(value.GetProperty("elementType"), depth + 1, limits);
        if (elementType.Kind == "Seq")
            throw ModulesExceptionFactory.Error("parse", "NestedSequenceNotSupported");

        return new TypeRef("Seq", Element: elementType, Capacity: capacity);
    }

    private static bool TypesEquivalent(TypeRef left, TypeRef right)
    {
        if (left.Kind != right.Kind)
            return false;

        if (left.Kind == "Record")
            return left.Name == right.Name;

        if (left.Kind == "Seq")
            return left.Capacity == right.Capacity && TypesEquivalent(left.Element!, right.Element!);

        return true;
    }

    private static void CheckObject(JsonElement obj, string path, IReadOnlyCollection<string> requiredFields)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            throw ModulesExceptionFactory.Error("parse", "SchemaInvalid",
                new { path, expected = "object", actual = obj.ValueKind.ToString() });

        var actual = obj.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var expected = requiredFields.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        if (!actual.SequenceEqual(expected))
            throw ModulesExceptionFactory.Error("parse", "SchemaInvalid",
                new { path, reason = "UnexpectedOrMissingFields", expected, actual });
    }

    private static string RequireId(JsonElement value)
    {
        var text = RequireString(value);
        if (!IdPattern.IsMatch(text))
            throw ModulesExceptionFactory.Error("parse", "InvalidId", new { value = text });

        return text;
    }

    private static string RequireDigest(JsonElement value)
    {
        var text = RequireString(value);
        if (!DigestPattern.IsMatch(text))
            throw ModulesExceptionFactory.Error("parse", "InvalidDigest", new { value = text });

        return text;
    }

    private static void RequireArray(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw ModulesExceptionFactory.Error("parse", "SchemaInvalid",
                new { path, expected = "array", actual = value.ValueKind.ToString() });
    }

    private static string RequireString(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw ModulesExceptionFactory.Error("parse", "SchemaInvalid",
                new { reason = "ExpectedString", actual = value.ValueKind.ToString() });

        return value.GetString()!;
    }

    private static void ValidateLimits(StrogoLimits limits)
    {
        if (limits.MaxTransportBytes <= 0
            || limits.MaxJsonDepth <= 0
            || limits.MaxFunctions <= 0
            || limits.MaxNodesPerFunction <= 0
            || limits.MaxTypes <= 0
            || limits.MaxTypeDepth <= 0
            || limits.MaxTypeDepth > 64)
        {
            throw ModulesExceptionFactory.Error("parse", "InvalidLimits");
        }
    }
}
