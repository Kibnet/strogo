using System.Collections.Immutable;
using System.Text;
using Kernel.Core;

namespace Strogo.Modules;

public sealed record DafnySourceMapEntry(string EntityId, string GeneratedName, int Line);

public sealed class DafnyLoweringResult
{
    private readonly byte[] sourceBytes;

    internal DafnyLoweringResult(byte[] sourceBytes, ImmutableArray<DafnySourceMapEntry> sourceMap)
    {
        this.sourceBytes = sourceBytes.ToArray();
        SourceMap = sourceMap;
        SourceDigest = CanonicalJson.RawDigest(this.sourceBytes);
    }

    public byte[] SourceBytes => sourceBytes.ToArray();
    public string Source => Encoding.UTF8.GetString(sourceBytes);
    public string SourceDigest { get; }
    public ImmutableArray<DafnySourceMapEntry> SourceMap { get; }
}

public static class ModulesDafnyLowerer
{
    private const string I64Declaration = "  newtype {:nativeType \"long\"} I64 = x: int | -9223372036854775808 <= x <= 9223372036854775807 witness 0";

    public static DafnyLoweringResult Lower(ModuleIr module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (module.Imports.Length != 0)
            throw ModulesExceptionFactory.Error("lowering", "UnsupportedLoweringImports", details: new
            {
                imports = module.Imports.Select(importValue => importValue.ModuleId).Order(StringComparer.Ordinal).ToArray()
            });
        if (module.Types.Length != 0)
            throw ModulesExceptionFactory.Error("lowering", "UnsupportedLoweringTypes", details: new
            {
                types = module.Types.Select(type => type.Id).Order(StringComparer.Ordinal).ToArray()
            });

        var functions = module.Functions.OrderBy(function => function.Id, StringComparer.Ordinal).ToArray();
        var symbols = functions.Select((function, index) => (function.Id, Name: $"F{index:D3}"))
            .ToImmutableDictionary(pair => pair.Id, pair => pair.Name, StringComparer.Ordinal);
        var writer = new SourceWriter();
        var map = ImmutableArray.CreateBuilder<DafnySourceMapEntry>();
        writer.Add("module Candidate {");
        writer.Add(I64Declaration);

        foreach (var function in functions)
        {
            EnsureType(function.ReturnType, $"function/{function.Id}/result");
            foreach (var parameter in function.Parameters)
                EnsureType(parameter.Type, $"function/{function.Id}/parameter/{parameter.Id}");

            var functionName = symbols[function.Id];
            var parameters = function.Parameters.Select((parameter, index) => $"p{index:D3}: {DafnyType(parameter.Type)}");
            var line = writer.Add($"  method {functionName}({string.Join(", ", parameters)}) returns (result: {DafnyType(function.ReturnType)})");
            map.Add(new DafnySourceMapEntry($"function/{function.Id}", functionName, line));
            for (var parameterIndex = 0; parameterIndex < function.Parameters.Length; parameterIndex++)
                map.Add(new DafnySourceMapEntry(
                    $"function/{function.Id}/parameter/{function.Parameters[parameterIndex].Id}",
                    $"p{parameterIndex:D3}",
                    line));
            writer.Add("  {");
            var variableCounter = 0;
            var parameterExpressions = function.Parameters.Select((_, index) => $"p{index:D3}").ToArray();
            var resultExpression = EmitRegion(
                new RegionIr(function.Parameters, function.Instructions, function.ResultIndex),
                parameterExpressions,
                $"function/{function.Id}/body",
                "    ",
                ref variableCounter,
                writer,
                map,
                symbols);
            writer.Add($"    result := {resultExpression};");
            writer.Add("  }");
        }

        writer.Add("}");
        return new DafnyLoweringResult(Encoding.UTF8.GetBytes(writer.Text), map.ToImmutable());
    }

    private static string EmitRegion(
        RegionIr region,
        IReadOnlyList<string> parameterExpressions,
        string regionLocus,
        string indent,
        ref int variableCounter,
        SourceWriter writer,
        ImmutableArray<DafnySourceMapEntry>.Builder map,
        ImmutableDictionary<string, string> functionSymbols)
    {
        if (parameterExpressions.Count != region.Parameters.Length)
            throw ModulesExceptionFactory.Error("lowering", "InternalInvariantViolation", regionLocus, new { reason = "RegionParameterCount" });

        var values = new string[region.Instructions.Length];
        foreach (var instruction in region.Instructions)
        {
            var locus = $"{regionLocus}/node/{instruction.OriginNodeId}";
            EnsureType(instruction.Type, locus);
            var operands = instruction.OperandIndices.Select(index => Resolve(index, parameterExpressions, values, locus)).ToArray();
            var variable = $"v{variableCounter++:D3}";
            var line = writer.Line + 1;

            if (instruction.Op == "if")
            {
                if (instruction.ThenRegion is null || instruction.ElseRegion is null || operands.Length == 0)
                    throw ModulesExceptionFactory.Error("lowering", "InternalInvariantViolation", locus, new { reason = "InvalidIf" });
                writer.Add($"{indent}var {variable}: {DafnyType(instruction.Type)};");
                map.Add(new DafnySourceMapEntry(locus, variable, line));
                writer.Add($"{indent}if {operands[0]} {{");
                var thenResult = EmitRegion(instruction.ThenRegion, operands[1..], $"{locus}/then", indent + "  ", ref variableCounter, writer, map, functionSymbols);
                writer.Add($"{indent}  {variable} := {thenResult};");
                writer.Add($"{indent}}} else {{");
                var elseResult = EmitRegion(instruction.ElseRegion, operands[1..], $"{locus}/else", indent + "  ", ref variableCounter, writer, map, functionSymbols);
                writer.Add($"{indent}  {variable} := {elseResult};");
                writer.Add($"{indent}}}");
            }
            else
            {
                var expression = Expression(instruction, operands, locus, functionSymbols);
                writer.Add($"{indent}var {variable}: {DafnyType(instruction.Type)} := {expression};");
                map.Add(new DafnySourceMapEntry(locus, variable, line));
            }

            if (instruction.DestinationIndex < 0 || instruction.DestinationIndex >= values.Length || values[instruction.DestinationIndex] is not null)
                throw ModulesExceptionFactory.Error("lowering", "InternalInvariantViolation", locus, new { reason = "DestinationIndex" });
            values[instruction.DestinationIndex] = variable;
        }

        return Resolve(region.ResultIndex, parameterExpressions, values, regionLocus);
    }

    private static string Expression(
        IrInstruction instruction,
        string[] operands,
        string locus,
        ImmutableDictionary<string, string> functionSymbols)
        => instruction.Op switch
        {
            "i64.const" => instruction.Metadata.Value!,
            "bool.const" => instruction.Metadata.Value!,
            "i64.add" => $"{operands[0]} + {operands[1]}",
            "i64.sub" => $"{operands[0]} - {operands[1]}",
            "i64.le" => $"{operands[0]} <= {operands[1]}",
            "i64.eq" => $"{operands[0]} == {operands[1]}",
            "bool.not" => $"!{operands[0]}",
            "bool.and" => $"{operands[0]} && {operands[1]}",
            "bool.or" => $"{operands[0]} || {operands[1]}",
            "call" when instruction.Metadata.FunctionRef is { } functionId && functionSymbols.TryGetValue(functionId, out var symbol)
                => $"{symbol}({string.Join(", ", operands)})",
            "call" => throw ModulesExceptionFactory.Error("lowering", "InternalInvariantViolation", locus, new { reason = "UnknownCallee" }),
            _ => throw ModulesExceptionFactory.Error("lowering", "UnsupportedLoweringOpcode", locus, new { instruction.Op })
        };

    private static string Resolve(int index, IReadOnlyList<string> parameters, string[] values, string locus)
    {
        if (index < 0)
        {
            var parameterIndex = -1 - index;
            if (parameterIndex >= 0 && parameterIndex < parameters.Count) return parameters[parameterIndex];
        }
        else if (index < values.Length && values[index] is { } value)
        {
            return value;
        }
        throw ModulesExceptionFactory.Error("lowering", "InternalInvariantViolation", locus, new { reason = "OperandIndex", index });
    }

    private static void EnsureType(TypeRef type, string locus)
    {
        if (type.Kind is not ("I64" or "Bool"))
            throw ModulesExceptionFactory.Error("lowering", "UnsupportedLoweringType", locus, new { type = type.ToString() });
    }

    private static string DafnyType(TypeRef type) => type.Kind switch
    {
        "I64" => "I64",
        "Bool" => "bool",
        _ => throw new InvalidOperationException("Type must be validated before emission")
    };

    private sealed class SourceWriter
    {
        private readonly StringBuilder builder = new();

        public int Line { get; private set; }
        public string Text => builder.ToString();

        public int Add(string line)
        {
            builder.Append(line).Append('\n');
            return ++Line;
        }
    }
}
