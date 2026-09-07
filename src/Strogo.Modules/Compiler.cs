using System.Collections.Immutable;

namespace Strogo.Modules;

public static class ModulesCompiler
{
    public static ModuleIr Compile(ModuleParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        var canonicalSource = ModulesCodec.Canonicalize(parseResult.Source);
        var sourceDigest = ModulesCodec.SourceDigest(canonicalSource);
        if (!canonicalSource.AsSpan().SequenceEqual(parseResult.CanonicalSourceBytes) || sourceDigest != parseResult.SourceDigest)
            throw ModulesExceptionFactory.Error("compile", "ValidatedSourceMismatch");

        var source = parseResult.Source;
        var functionIr = new List<FunctionIr>(source.Functions.Length);
        foreach (var function in source.Functions.OrderBy(function => function.Id, StringComparer.Ordinal))
        {
            functionIr.Add(CompileFunction(function));
        }

        var ir = new ModuleIr(
            source.SchemaVersion,
            source.ModuleId,
            parseResult.SourceDigest,
            source.Types.OrderBy(type => type.Id, StringComparer.Ordinal).ToImmutableArray(),
            source.Imports.OrderBy(importValue => importValue.ModuleId, StringComparer.Ordinal).ToImmutableArray(),
            functionIr.ToImmutableArray(),
            source.Exports.OrderBy(id => id, StringComparer.Ordinal).ToImmutableArray(),
            canonicalBytes: Array.Empty<byte>());
        var canonicalIr = ModulesCodec.Canonicalize(ir);
        return ir.WithCanonicalBytes(canonicalIr);
    }

    private static FunctionIr CompileFunction(FunctionDecl function)
    {
        var region = CompileRegion(function.Body, function.Parameters);
        return new FunctionIr(function.Id, function.Parameters, function.ReturnType, function.ContractRef, region.Instructions, region.ResultIndex);
    }

    private static RegionIr CompileRegion(FunctionBody body, ImmutableArray<FunctionParameter> parameters)
    {
        var parameterIndices = parameters.Select((parameter, index) => (parameter.Id, -1 - index)).ToImmutableDictionary(v => v.Id, v => v.Item2, StringComparer.Ordinal);
        var nodeIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var nodeTypes = body.Nodes.ToDictionary(node => node.Id, node => node.Type, StringComparer.Ordinal);
        var parameterTypes = parameters.ToDictionary(parameter => parameter.Id, parameter => parameter.Type, StringComparer.Ordinal);
        var instructions = ImmutableArray.CreateBuilder<IrInstruction>(body.Nodes.Length);
        var orderedNodes = TopologicallySort(body.Parameters, body.Nodes);

        foreach (var node in orderedNodes)
        {
            var orderedArgs = node.Op == "record.make"
                ? node.Metadata.FieldIds
                    .Select((fieldId, index) => (fieldId, arg: node.Args[index]))
                    .OrderBy(pair => pair.fieldId, StringComparer.Ordinal)
                    .Select(pair => pair.arg)
                : node.Args;
            var operands = orderedArgs.Select(arg => parameterIndices.TryGetValue(arg, out var paramIndex)
                ? paramIndex
                : nodeIndices[arg]).ToImmutableArray();
            var metadata = node.Op == "record.make"
                ? node.Metadata with { FieldIds = node.Metadata.FieldIds.OrderBy(id => id, StringComparer.Ordinal).ToImmutableArray() }
                : node.Metadata;
            RegionIr? thenRegion = null;
            RegionIr? elseRegion = null;
            if (node.Op == "if")
            {
                var environmentTypes = node.Args.Skip(1).Select(ValueType).ToImmutableArray();
                thenRegion = CompileRegion(node.ThenRegion!, BindParameters(node.ThenRegion!, environmentTypes));
                elseRegion = CompileRegion(node.ElseRegion!, BindParameters(node.ElseRegion!, environmentTypes));
            }
            var instruction = new IrInstruction(instructions.Count, node.Id, node.Op, operands, node.Type, metadata, thenRegion, elseRegion);
            instructions.Add(instruction);
            nodeIndices[node.Id] = instruction.DestinationIndex;
        }

        var resultIndex = parameterIndices.TryGetValue(body.Result, out var parameterIndex)
            ? parameterIndex
            : nodeIndices[body.Result];
        return new RegionIr(parameters, instructions.ToImmutable(), resultIndex);

        TypeRef ValueType(string id) => parameterTypes.TryGetValue(id, out var parameterType) ? parameterType : nodeTypes[id];
        static ImmutableArray<FunctionParameter> BindParameters(FunctionBody region, ImmutableArray<TypeRef> types)
            => region.Parameters.Select((id, index) => new FunctionParameter(id, types[index])).ToImmutableArray();
    }

    private static ImmutableArray<FunctionNode> TopologicallySort(ImmutableArray<string> parameters, ImmutableArray<FunctionNode> nodes)
    {
        var nodeLookup = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var dependencies = nodeLookup.ToDictionary(item => item.Key, _ => new HashSet<string>(StringComparer.Ordinal));
        var dependants = nodeLookup.ToDictionary(item => item.Key, _ => new HashSet<string>(StringComparer.Ordinal));
        var inDegree = nodeLookup.ToDictionary(item => item.Key, _ => 0);

        foreach (var node in nodes)
        {
            foreach (var arg in node.Args.Distinct(StringComparer.Ordinal))
            {
                if (!nodeLookup.TryGetValue(arg, out _)) continue;
                dependencies[node.Id].Add(arg);
                dependants[arg].Add(node.Id);
                inDegree[node.Id]++;
            }
        }

        var ready = new SortedSet<string>(nodeLookup.Where(item => inDegree[item.Key] == 0).Select(item => item.Key), StringComparer.Ordinal);
        var ordered = ImmutableArray.CreateBuilder<FunctionNode>(nodeLookup.Count);
        while (ready.Count > 0)
        {
            var id = ready.Min!;
            ready.Remove(id);
            ordered.Add(nodeLookup[id]);

            foreach (var dependent in dependants[id])
            {
                if (--inDegree[dependent] == 0) ready.Add(dependent);
            }
        }

        if (ordered.Count != nodes.Length)
            throw ModulesExceptionFactory.Error("compile", "CycleDetected", details: new { function = "compile" });

        return ordered.ToImmutable();
    }
}
