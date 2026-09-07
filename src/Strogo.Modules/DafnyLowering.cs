using System.Collections.Immutable;
using System.Globalization;
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
        => LowerCore(module, null);

    public static DafnyLoweringResult Lower(ModuleIr module, OwnerBundle ownerBundle)
    {
        ArgumentNullException.ThrowIfNull(ownerBundle);
        return LowerCore(module, OwnerContractBinder.Bind(module, ownerBundle));
    }

    private static DafnyLoweringResult LowerCore(ModuleIr module, OwnerContractBinding? binding)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (module.Imports.Length != 0)
            throw ModulesExceptionFactory.Error("lowering", "UnsupportedLoweringImports", details: new
            {
                imports = module.Imports.Select(importValue => importValue.ModuleId).Order(StringComparer.Ordinal).ToArray()
            });
        var functions = module.Functions.OrderBy(function => function.Id, StringComparer.Ordinal).ToArray();
        var typeSymbols = new TypeLoweringSymbols(module, binding);
        var symbols = functions.Select((function, index) => (function.Id, Name: $"F{index:D3}"))
            .ToImmutableDictionary(pair => pair.Id, pair => pair.Name, StringComparer.Ordinal);
        var modelSymbols = binding?.Entries.Select((entry, index) => (entry.Model.Id, Name: $"M{index:D3}"))
            .ToImmutableDictionary(pair => pair.Id, pair => pair.Name, StringComparer.Ordinal)
            ?? ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);
        var writer = new SourceWriter();
        var map = ImmutableArray.CreateBuilder<DafnySourceMapEntry>();
        writer.Add("module Candidate {");
        writer.Add(I64Declaration);
        typeSymbols.EmitDeclarations(writer, map);

        if (binding is not null)
        {
            writer.Add("  function StrictAnd(left: bool, right: bool): bool { left && right }");
            writer.Add("  function StrictOr(left: bool, right: bool): bool { left || right }");
            foreach (var entry in binding.Entries)
            {
                var modelName = modelSymbols[entry.Model.Id];
                var parameters = entry.Model.Parameters.Select((parameter, index) => $"p{index:D3}: {typeSymbols.DafnyType(parameter.Type)}");
                var line = writer.Add($"  function {modelName}({string.Join(", ", parameters)}): {typeSymbols.DafnyType(entry.Model.ReturnType)}");
                map.Add(new DafnySourceMapEntry($"owner/model/{entry.Model.Id}", modelName, line));
                for (var parameterIndex = 0; parameterIndex < entry.Model.Parameters.Length; parameterIndex++)
                    map.Add(new DafnySourceMapEntry(
                        $"owner/model/{entry.Model.Id}/parameter/{entry.Model.Parameters[parameterIndex].Id}",
                        $"p{parameterIndex:D3}",
                        line));
                writer.Add($"    requires {EmitOwnerExpression(entry.Contract.Requires, entry.Model.Parameters, typeSymbols)}");
                writer.Add("  {");
                writer.Add($"    {EmitOwnerExpression(entry.Model.Body, entry.Model.Parameters, typeSymbols)}");
                writer.Add("  }");
            }
        }

        foreach (var function in functions)
        {
            typeSymbols.EnsureType(function.ReturnType, $"function/{function.Id}/result");
            foreach (var parameter in function.Parameters)
                typeSymbols.EnsureType(parameter.Type, $"function/{function.Id}/parameter/{parameter.Id}");

            var functionName = symbols[function.Id];
            var parameters = function.Parameters.Select((parameter, index) => $"p{index:D3}: {typeSymbols.DafnyType(parameter.Type)}");
            var line = writer.Add($"  method {functionName}({string.Join(", ", parameters)}) returns (result: {typeSymbols.DafnyType(function.ReturnType)})");
            map.Add(new DafnySourceMapEntry($"function/{function.Id}", functionName, line));
            for (var parameterIndex = 0; parameterIndex < function.Parameters.Length; parameterIndex++)
                map.Add(new DafnySourceMapEntry(
                    $"function/{function.Id}/parameter/{function.Parameters[parameterIndex].Id}",
                    $"p{parameterIndex:D3}",
                    line));
            if (binding is not null)
            {
                var entry = binding.Entries.Single(bound => bound.Function.Id == function.Id);
                for (var parameterIndex = 0; parameterIndex < entry.Contract.Parameters.Length; parameterIndex++)
                    map.Add(new DafnySourceMapEntry(
                        $"owner/contract/{entry.Contract.Id}/parameter/{entry.Contract.Parameters[parameterIndex].Id}",
                        $"p{parameterIndex:D3}",
                        line));
                var requiresLine = writer.Add($"    requires {EmitOwnerExpression(entry.Contract.Requires, entry.Contract.Parameters, typeSymbols)}");
                map.Add(new DafnySourceMapEntry($"owner/contract/{entry.Contract.Id}/requires", functionName, requiresLine));
                var ensuresLine = writer.Add($"    ensures result == {modelSymbols[entry.Model.Id]}({string.Join(", ", function.Parameters.Select((_, index) => $"p{index:D3}"))})");
                map.Add(new DafnySourceMapEntry($"owner/contract/{entry.Contract.Id}/ensures", functionName, ensuresLine));
            }
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
                symbols,
                typeSymbols);
            writer.Add($"    result := {resultExpression};");
            writer.Add("  }");
        }

        writer.Add("}");
        return new DafnyLoweringResult(Encoding.UTF8.GetBytes(writer.Text), map.ToImmutable());
    }

    private static string EmitOwnerExpression(
        OwnerExpression expression,
        ImmutableArray<FunctionParameter> parameters,
        TypeLoweringSymbols typeSymbols)
    {
        var parameterSymbols = parameters.Select((parameter, index) => (parameter.Id, Symbol: $"p{index:D3}"))
            .ToImmutableDictionary(pair => pair.Id, pair => pair.Symbol, StringComparer.Ordinal);
        var temporaryCounter = 0;
        return Emit(expression);

        string Emit(OwnerExpression current) => current.Op switch
        {
            "param" when current.ReferenceId is { } parameterId && parameterSymbols.TryGetValue(parameterId, out var parameter) => parameter,
            "i64.const" => current.I64Value!.Value.ToString(CultureInfo.InvariantCulture),
            "bool.const" => current.BoolValue!.Value ? "true" : "false",
            "i64.add" => EmitCheckedI64(current, "+"),
            "i64.sub" => EmitCheckedI64(current, "-"),
            "i64.le" => $"({Emit(current.Args[0])} <= {Emit(current.Args[1])})",
            "eq" => $"({Emit(current.Args[0])} == {Emit(current.Args[1])})",
            "bool.not" => $"(!{Emit(current.Args[0])})",
            "bool.and" => $"StrictAnd({Emit(current.Args[0])}, {Emit(current.Args[1])})",
            "bool.or" => $"StrictOr({Emit(current.Args[0])}, {Emit(current.Args[1])})",
            "if" => $"(if {Emit(current.Args[0])} then {Emit(current.Args[1])} else {Emit(current.Args[2])})",
            "record.make" when current.RecordType is { } recordType =>
                typeSymbols.ConstructRecord(recordType, current.FieldIds.Select((fieldId, index) => (fieldId, value: Emit(current.Args[index])))
                    .OrderBy(pair => pair.fieldId, StringComparer.Ordinal).Select(pair => pair.value).ToArray()),
            "record.get" when current.ReferenceId is { } fieldId && current.Args[0].Type.Name is { } recordType =>
                $"{Emit(current.Args[0])}.{typeSymbols.RecordField(recordType, fieldId)}",
            "seq.empty" => "[]",
            "seq.length" => $"(|{Emit(current.Args[0])}| as I64)",
            "seq.get" => $"{Emit(current.Args[0])}[{Emit(current.Args[1])} as int]",
            "seq.append" => EmitCheckedSequenceAppend(current),
            _ => throw ModulesExceptionFactory.Error("lowering", "InternalInvariantViolation", details: new { reason = "OwnerExpression", current.Op })
        };

        string EmitCheckedI64(OwnerExpression current, string operation)
        {
            var left = Emit(current.Args[0]);
            var right = Emit(current.Args[1]);
            var temporary = $"e{temporaryCounter++:D3}";
            return $"(var {temporary}: I64 := ({left} {operation} {right}); {temporary})";
        }

        string EmitCheckedSequenceAppend(OwnerExpression current)
        {
            var source = Emit(current.Args[0]);
            var item = Emit(current.Args[1]);
            var temporary = $"e{temporaryCounter++:D3}";
            return $"(var {temporary}: {typeSymbols.DafnyType(current.Type)} := ({source} + [{item}]); {temporary})";
        }
    }

    private static string EmitRegion(
        RegionIr region,
        IReadOnlyList<string> parameterExpressions,
        string regionLocus,
        string indent,
        ref int variableCounter,
        SourceWriter writer,
        ImmutableArray<DafnySourceMapEntry>.Builder map,
        ImmutableDictionary<string, string> functionSymbols,
        TypeLoweringSymbols typeSymbols)
    {
        if (parameterExpressions.Count != region.Parameters.Length)
            throw ModulesExceptionFactory.Error("lowering", "InternalInvariantViolation", regionLocus, new { reason = "RegionParameterCount" });

        var values = new string[region.Instructions.Length];
        foreach (var instruction in region.Instructions)
        {
            var locus = $"{regionLocus}/node/{instruction.OriginNodeId}";
            typeSymbols.EnsureType(instruction.Type, locus);
            var operands = instruction.OperandIndices.Select(index => Resolve(index, parameterExpressions, values, locus)).ToArray();
            var operandTypes = instruction.OperandIndices.Select(index => ResolveType(index, region, locus)).ToArray();
            var variable = $"v{variableCounter++:D3}";
            var line = writer.Line + 1;

            if (instruction.Op == "if")
            {
                if (instruction.ThenRegion is null || instruction.ElseRegion is null || operands.Length == 0)
                    throw ModulesExceptionFactory.Error("lowering", "InternalInvariantViolation", locus, new { reason = "InvalidIf" });
                writer.Add($"{indent}var {variable}: {typeSymbols.DafnyType(instruction.Type)};");
                map.Add(new DafnySourceMapEntry(locus, variable, line));
                writer.Add($"{indent}if {operands[0]} {{");
                var thenResult = EmitRegion(instruction.ThenRegion, operands[1..], $"{locus}/then", indent + "  ", ref variableCounter, writer, map, functionSymbols, typeSymbols);
                writer.Add($"{indent}  {variable} := {thenResult};");
                writer.Add($"{indent}}} else {{");
                var elseResult = EmitRegion(instruction.ElseRegion, operands[1..], $"{locus}/else", indent + "  ", ref variableCounter, writer, map, functionSymbols, typeSymbols);
                writer.Add($"{indent}  {variable} := {elseResult};");
                writer.Add($"{indent}}}");
            }
            else
            {
                if (instruction.Op == "seq.get")
                    writer.Add($"{indent}assert 0 <= ({operands[1]} as int) < |{operands[0]}|;");
                if (instruction.Op == "seq.append")
                    writer.Add($"{indent}assert |{operands[0]}| < {instruction.Type.Capacity!.Value.ToString(CultureInfo.InvariantCulture)};");
                var expression = Expression(instruction, operands, operandTypes, locus, functionSymbols, typeSymbols);
                writer.Add($"{indent}var {variable}: {typeSymbols.DafnyType(instruction.Type)} := {expression};");
                map.Add(new DafnySourceMapEntry(locus, variable, line));
            }

            if (instruction.DestinationIndex < 0 || instruction.DestinationIndex >= values.Length || values[instruction.DestinationIndex] is not null)
                throw ModulesExceptionFactory.Error("lowering", "InternalInvariantViolation", locus, new { reason = "DestinationIndex" });
            values[instruction.DestinationIndex] = variable;
        }

        return Resolve(region.ResultIndex, parameterExpressions, values, regionLocus);
    }

    private static TypeRef ResolveType(int index, RegionIr region, string locus)
    {
        if (index < 0)
        {
            var parameterIndex = -1 - index;
            if (parameterIndex >= 0 && parameterIndex < region.Parameters.Length)
                return region.Parameters[parameterIndex].Type;
        }
        else if (index < region.Instructions.Length)
        {
            return region.Instructions[index].Type;
        }
        throw ModulesExceptionFactory.Error("lowering", "InternalInvariantViolation", locus, new { reason = "OperandTypeIndex", index });
    }

    private static string Expression(
        IrInstruction instruction,
        string[] operands,
        TypeRef[] operandTypes,
        string locus,
        ImmutableDictionary<string, string> functionSymbols,
        TypeLoweringSymbols typeSymbols)
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
            "record.make" when instruction.Type.Name is { } recordType =>
                typeSymbols.ConstructRecord(recordType, operands),
            "record.get" when operandTypes.Length == 1 && operandTypes[0].Name is { } recordType =>
                $"{operands[0]}.{typeSymbols.RecordField(recordType, instruction.Metadata.FieldId!)}",
            "seq.empty" => "[]",
            "seq.length" => $"|{operands[0]}| as I64",
            "seq.get" => $"{operands[0]}[{operands[1]} as int]",
            "seq.append" => $"{operands[0]} + [{operands[1]}]",
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

    private sealed class TypeLoweringSymbols
    {
        private readonly ImmutableArray<TypeDecl> records;
        private readonly ImmutableDictionary<string, string> recordSymbols;
        private readonly ImmutableDictionary<string, string> constructorSymbols;
        private readonly ImmutableDictionary<string, string> fieldSymbols;
        private readonly ImmutableArray<(TypeRef Type, string Key, string Symbol)> sequences;
        private readonly ImmutableDictionary<string, string> sequenceSymbols;

        public TypeLoweringSymbols(ModuleIr module, OwnerContractBinding? binding)
        {
            records = module.Types.OrderBy(type => type.Id, StringComparer.Ordinal).ToImmutableArray();
            recordSymbols = records.Select((type, index) => (type.Id, Symbol: $"R{index:D3}"))
                .ToImmutableDictionary(pair => pair.Id, pair => pair.Symbol, StringComparer.Ordinal);
            constructorSymbols = records.Select((type, index) => (type.Id, Symbol: $"C{index:D3}"))
                .ToImmutableDictionary(pair => pair.Id, pair => pair.Symbol, StringComparer.Ordinal);
            fieldSymbols = records.SelectMany((type, typeIndex) => type.Fields
                    .OrderBy(field => field.Id, StringComparer.Ordinal)
                    .Select((field, fieldIndex) => (Key: FieldKey(type.Id, field.Id), Symbol: $"R{typeIndex:D3}F{fieldIndex:D3}")))
                .ToImmutableDictionary(pair => pair.Key, pair => pair.Symbol, StringComparer.Ordinal);

            sequences = EnumerateModuleTypes(module).Concat(EnumerateOwnerTypes(binding))
                .Where(type => type.Kind == "Seq")
                .GroupBy(TypeKey, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select((group, index) => (group.First(), group.Key, $"S{index:D3}"))
                .ToImmutableArray();
            sequenceSymbols = sequences.ToImmutableDictionary(item => item.Key, item => item.Symbol, StringComparer.Ordinal);
        }

        public void EmitDeclarations(SourceWriter writer, ImmutableArray<DafnySourceMapEntry>.Builder map)
        {
            foreach (var sequence in sequences)
            {
                var line = writer.Add($"  type {sequence.Symbol} = s: seq<{DafnyType(sequence.Type.Element!)}> | |s| <= {sequence.Type.Capacity!.Value.ToString(CultureInfo.InvariantCulture)} witness []");
                map.Add(new DafnySourceMapEntry($"type/{sequence.Key}", sequence.Symbol, line));
            }

            foreach (var record in records)
            {
                var recordName = recordSymbols[record.Id];
                var constructor = constructorSymbols[record.Id];
                var fields = record.Fields.OrderBy(field => field.Id, StringComparer.Ordinal)
                    .Select(field => $"{RecordField(record.Id, field.Id)}: {DafnyType(field.Type)}")
                    .ToArray();
                var declaration = fields.Length == 0
                    ? $"  datatype {recordName} = {constructor}"
                    : $"  datatype {recordName} = {constructor}({string.Join(", ", fields)})";
                var line = writer.Add(declaration);
                map.Add(new DafnySourceMapEntry($"type/{record.Id}", recordName, line));
                foreach (var field in record.Fields)
                    map.Add(new DafnySourceMapEntry($"type/{record.Id}/field/{field.Id}", RecordField(record.Id, field.Id), line));
            }
        }

        public void EnsureType(TypeRef type, string locus)
        {
            switch (type.Kind)
            {
                case "I64":
                case "Bool":
                    return;
                case "Record" when type.Name is not null && recordSymbols.ContainsKey(type.Name):
                    return;
                case "Seq" when type.Element is not null && type.Capacity is >= 0 && sequenceSymbols.ContainsKey(TypeKey(type)):
                    EnsureType(type.Element, locus);
                    return;
                default:
                    throw ModulesExceptionFactory.Error("lowering", "UnsupportedLoweringType", locus, new { type = type.ToString() });
            }
        }

        public string DafnyType(TypeRef type) => type.Kind switch
        {
            "I64" => "I64",
            "Bool" => "bool",
            "Record" when type.Name is not null && recordSymbols.TryGetValue(type.Name, out var symbol) => symbol,
            "Seq" when sequenceSymbols.TryGetValue(TypeKey(type), out var symbol) => symbol,
            _ => throw new InvalidOperationException("Type must be validated before emission")
        };

        public string RecordConstructor(string typeId)
            => constructorSymbols.TryGetValue(typeId, out var symbol)
                ? symbol
                : throw new InvalidOperationException("Record type must be validated before emission");

        public string ConstructRecord(string typeId, IReadOnlyList<string> operands)
        {
            var constructor = RecordConstructor(typeId);
            return operands.Count == 0 ? constructor : $"{constructor}({string.Join(", ", operands)})";
        }

        public string RecordField(string typeId, string fieldId)
            => fieldSymbols.TryGetValue(FieldKey(typeId, fieldId), out var symbol)
                ? symbol
                : throw new InvalidOperationException("Record field must be validated before emission");

        private static IEnumerable<TypeRef> EnumerateModuleTypes(ModuleIr module)
        {
            foreach (var type in module.Types)
                foreach (var field in type.Fields)
                    foreach (var item in Expand(field.Type))
                        yield return item;
            foreach (var function in module.Functions)
            {
                foreach (var parameter in function.Parameters)
                    foreach (var item in Expand(parameter.Type))
                        yield return item;
                foreach (var item in Expand(function.ReturnType))
                    yield return item;
                foreach (var item in EnumerateRegionTypes(new RegionIr(function.Parameters, function.Instructions, function.ResultIndex)))
                    yield return item;
            }
        }

        private static IEnumerable<TypeRef> EnumerateRegionTypes(RegionIr region)
        {
            foreach (var parameter in region.Parameters)
                foreach (var item in Expand(parameter.Type))
                    yield return item;
            foreach (var instruction in region.Instructions)
            {
                foreach (var item in Expand(instruction.Type))
                    yield return item;
                if (instruction.ThenRegion is not null)
                    foreach (var item in EnumerateRegionTypes(instruction.ThenRegion))
                        yield return item;
                if (instruction.ElseRegion is not null)
                    foreach (var item in EnumerateRegionTypes(instruction.ElseRegion))
                        yield return item;
            }
        }

        private static IEnumerable<TypeRef> EnumerateOwnerTypes(OwnerContractBinding? binding)
        {
            if (binding is null) yield break;
            foreach (var entry in binding.Entries)
            {
                foreach (var parameter in entry.Contract.Parameters)
                    foreach (var item in Expand(parameter.Type)) yield return item;
                foreach (var item in Expand(entry.Contract.ReturnType)) yield return item;
                foreach (var item in EnumerateOwnerExpressionTypes(entry.Contract.Requires)) yield return item;
                foreach (var parameter in entry.Model.Parameters)
                    foreach (var item in Expand(parameter.Type)) yield return item;
                foreach (var item in Expand(entry.Model.ReturnType)) yield return item;
                foreach (var item in EnumerateOwnerExpressionTypes(entry.Model.Body)) yield return item;
            }
        }

        private static IEnumerable<TypeRef> EnumerateOwnerExpressionTypes(OwnerExpression expression)
        {
            foreach (var item in Expand(expression.Type)) yield return item;
            if (expression.ElementType is not null)
                foreach (var item in Expand(expression.ElementType)) yield return item;
            foreach (var argument in expression.Args)
                foreach (var item in EnumerateOwnerExpressionTypes(argument)) yield return item;
        }

        private static IEnumerable<TypeRef> Expand(TypeRef type)
        {
            yield return type;
            if (type.Element is not null)
                foreach (var item in Expand(type.Element))
                    yield return item;
        }

        private static string TypeKey(TypeRef type) => type.Kind switch
        {
            "I64" or "Bool" => type.Kind,
            "Record" => $"Record:{type.Name}",
            "Seq" => $"Seq:{type.Capacity}:{TypeKey(type.Element!)}",
            _ => type.ToString()
        };

        private static string FieldKey(string typeId, string fieldId) => $"{typeId}\n{fieldId}";
    }

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
