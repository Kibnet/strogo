using System.Collections.Immutable;
using System.Globalization;

namespace Strogo.Modules;

public abstract record ModuleValue
{
    private protected ModuleValue() { }

    internal abstract string TypeKind { get; }
}

public sealed record ModuleI64(long Value) : ModuleValue
{
    internal override string TypeKind => "I64";
}

public sealed record ModuleBool(bool Value) : ModuleValue
{
    internal override string TypeKind => "Bool";
}

public sealed record ModuleSequence(
    TypeRef ElementType,
    int Capacity,
    ImmutableArray<ModuleValue> Items) : ModuleValue
{
    public ModuleSequence(TypeRef elementType, int capacity, IEnumerable<ModuleValue> items)
        : this(elementType, capacity, items.ToImmutableArray())
    {
    }

    internal override string TypeKind => "Seq";
}

public sealed record ModuleRecord(
    string RecordTypeId,
    ImmutableSortedDictionary<string, ModuleValue> Fields) : ModuleValue
{
    public ModuleRecord(string recordTypeId, IEnumerable<KeyValuePair<string, ModuleValue>> fields)
        : this(recordTypeId, fields.ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
    {
    }

    internal override string TypeKind => "Record";
}

public sealed record ModuleEvaluationResult(ModuleValue Value, int Steps);

public sealed record ModuleEvaluationLimits
{
    public const int StepsHardMaximum = 1_000_000;

    public int MaxSteps { get; init; } = 100_000;
}

public static class ModulesReferenceEvaluator
{
    public static ModuleEvaluationResult Invoke(
        ModuleIr module,
        string functionId,
        IReadOnlyList<ModuleValue> arguments,
        ModuleEvaluationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(functionId);
        ArgumentNullException.ThrowIfNull(arguments);
        limits ??= new ModuleEvaluationLimits();
        if (limits.MaxSteps is < 1 or > ModuleEvaluationLimits.StepsHardMaximum)
            throw ModulesExceptionFactory.Error("evaluation", "InvalidEvaluationLimits", details: new
            {
                limits.MaxSteps,
                hardMaximum = ModuleEvaluationLimits.StepsHardMaximum
            });
        if (module.Imports.Length != 0)
            throw ModulesExceptionFactory.Error("evaluation", "UnsupportedRuntimeImports", details: new
            {
                imports = module.Imports.Select(importValue => importValue.ModuleId).Order(StringComparer.Ordinal).ToArray()
            });

        var functions = module.Functions.ToImmutableDictionary(function => function.Id, StringComparer.Ordinal);
        if (!functions.TryGetValue(functionId, out var function))
            throw ModulesExceptionFactory.Error("evaluation", "FunctionMissing", functionId);
        if (!module.Exports.Contains(functionId, StringComparer.Ordinal))
            throw ModulesExceptionFactory.Error("evaluation", "FunctionNotExported", functionId);

        var types = module.Types.ToImmutableDictionary(type => type.Id, StringComparer.Ordinal);
        var state = new EvaluationState(functions, types, limits.MaxSteps);
        var value = state.EvaluateFunction(function, arguments, 0);
        return new ModuleEvaluationResult(value, state.Steps);
    }

    private sealed class EvaluationState(
        ImmutableDictionary<string, FunctionIr> functions,
        ImmutableDictionary<string, TypeDecl> types,
        int maxSteps)
    {
        public int Steps { get; private set; }

        public ModuleValue EvaluateFunction(FunctionIr function, IReadOnlyList<ModuleValue> arguments, int callDepth)
        {
            var functionLocus = $"function/{function.Id}";
            if (callDepth > functions.Count)
                throw ModulesExceptionFactory.Error("evaluation", "InternalCallCycle", functionLocus);
            if (arguments.Count != function.Parameters.Length)
                throw ModulesExceptionFactory.Error("evaluation", "ArgumentCountMismatch", functionLocus, new
                {
                    expected = function.Parameters.Length,
                    actual = arguments.Count
                });

            for (var index = 0; index < arguments.Count; index++)
                EnsureValueType(arguments[index], function.Parameters[index].Type, $"{functionLocus}/parameter/{function.Parameters[index].Id}");

            var body = new RegionIr(function.Parameters, function.Instructions, function.ResultIndex);
            var result = EvaluateRegion(body, arguments, $"{functionLocus}/body", callDepth);
            EnsureValueType(result, function.ReturnType, $"{functionLocus}/result");
            return result;
        }

        private ModuleValue EvaluateRegion(RegionIr region, IReadOnlyList<ModuleValue> parameters, string regionLocus, int callDepth)
        {
            if (parameters.Count != region.Parameters.Length)
                throw ModulesExceptionFactory.Error("evaluation", "InternalInvariantViolation", regionLocus, new { reason = "RegionParameterCount" });
            for (var index = 0; index < parameters.Count; index++)
                EnsureValueType(parameters[index], region.Parameters[index].Type, $"{regionLocus}/parameter/{region.Parameters[index].Id}");

            var values = new ModuleValue[region.Instructions.Length];
            foreach (var instruction in region.Instructions)
            {
                var nodeLocus = $"{regionLocus}/node/{instruction.OriginNodeId}";
                ConsumeStep(nodeLocus);
                var operands = instruction.OperandIndices.Select(index => Resolve(index, parameters, values, nodeLocus)).ToArray();
                ModuleValue result;
                try
                {
                    result = instruction.Op switch
                    {
                        "i64.const" => new ModuleI64(long.Parse(instruction.Metadata.Value!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
                        "bool.const" => new ModuleBool(bool.Parse(instruction.Metadata.Value!)),
                        "i64.add" => new ModuleI64(checked(I64(operands[0], nodeLocus) + I64(operands[1], nodeLocus))),
                        "i64.sub" => new ModuleI64(checked(I64(operands[0], nodeLocus) - I64(operands[1], nodeLocus))),
                        "i64.le" => new ModuleBool(I64(operands[0], nodeLocus) <= I64(operands[1], nodeLocus)),
                        "i64.eq" => new ModuleBool(I64(operands[0], nodeLocus) == I64(operands[1], nodeLocus)),
                        "bool.not" => new ModuleBool(!Bool(operands[0], nodeLocus)),
                        "bool.and" => new ModuleBool(Bool(operands[0], nodeLocus) && Bool(operands[1], nodeLocus)),
                        "bool.or" => new ModuleBool(Bool(operands[0], nodeLocus) || Bool(operands[1], nodeLocus)),
                        "record.make" => MakeRecord(instruction, operands, nodeLocus),
                        "record.get" => GetRecordField(instruction, operands, nodeLocus),
                        "seq.empty" => MakeEmptySequence(instruction, nodeLocus),
                        "seq.length" => new ModuleI64(Sequence(operands[0], nodeLocus).Items.Length),
                        "seq.get" => GetSequenceItem(operands, nodeLocus),
                        "seq.append" => AppendSequence(operands, nodeLocus),
                        "if" => EvaluateIf(instruction, operands, nodeLocus, callDepth),
                        "fold" => EvaluateFold(instruction, operands, nodeLocus, callDepth),
                        "call" => EvaluateCall(instruction, operands, nodeLocus, callDepth),
                        _ => throw ModulesExceptionFactory.Error("evaluation", "UnsupportedRuntimeOpcode", nodeLocus, new { instruction.Op })
                    };
                }
                catch (OverflowException)
                {
                    throw ModulesExceptionFactory.Error("evaluation", "ArithmeticOverflow", nodeLocus, new { instruction.Op });
                }

                EnsureValueType(result, instruction.Type, nodeLocus);
                if (instruction.DestinationIndex < 0 || instruction.DestinationIndex >= values.Length || values[instruction.DestinationIndex] is not null)
                    throw ModulesExceptionFactory.Error("evaluation", "InternalInvariantViolation", nodeLocus, new { reason = "DestinationIndex" });
                values[instruction.DestinationIndex] = result;
            }

            return Resolve(region.ResultIndex, parameters, values, regionLocus);
        }

        private ModuleValue EvaluateIf(IrInstruction instruction, ModuleValue[] operands, string nodeLocus, int callDepth)
        {
            if (instruction.ThenRegion is null || instruction.ElseRegion is null || operands.Length == 0)
                throw ModulesExceptionFactory.Error("evaluation", "InternalInvariantViolation", nodeLocus, new { reason = "InvalidIf" });
            var condition = Bool(operands[0], nodeLocus);
            var selected = condition ? instruction.ThenRegion : instruction.ElseRegion;
            var role = condition ? "then" : "else";
            return EvaluateRegion(selected, operands[1..], $"{nodeLocus}/{role}", callDepth);
        }

        private ModuleValue EvaluateCall(IrInstruction instruction, ModuleValue[] operands, string nodeLocus, int callDepth)
        {
            if (instruction.Metadata.FunctionRef is null || !functions.TryGetValue(instruction.Metadata.FunctionRef, out var callee))
                throw ModulesExceptionFactory.Error("evaluation", "InternalInvariantViolation", nodeLocus, new { reason = "UnknownCallee" });
            return EvaluateFunction(callee, operands, callDepth + 1);
        }

        private ModuleValue EvaluateFold(IrInstruction instruction, ModuleValue[] operands, string nodeLocus, int callDepth)
        {
            if (instruction.Fold is null || operands.Length < 2)
                throw ModulesExceptionFactory.Error("evaluation", "InternalInvariantViolation", nodeLocus, new { reason = "InvalidFold" });

            var sequence = Sequence(operands[0], nodeLocus);
            var accumulator = operands[1];
            var environment = operands.Skip(2).ToArray();
            for (var index = 0; index < sequence.Items.Length; index++)
            {
                ConsumeStep($"{nodeLocus}/iteration/{index.ToString(CultureInfo.InvariantCulture)}");
                var stepArguments = new ModuleValue[3 + environment.Length];
                stepArguments[0] = new ModuleI64(index);
                stepArguments[1] = sequence.Items[index];
                stepArguments[2] = accumulator;
                Array.Copy(environment, 0, stepArguments, 3, environment.Length);
                accumulator = EvaluateRegion(instruction.Fold.Step, stepArguments, $"{nodeLocus}/step/{index.ToString(CultureInfo.InvariantCulture)}", callDepth);
            }
            return accumulator;
        }

        private ModuleValue MakeRecord(IrInstruction instruction, ModuleValue[] operands, string nodeLocus)
        {
            if (instruction.Type.Kind != "Record"
                || instruction.Type.Name is null
                || instruction.Metadata.RecordType != instruction.Type.Name
                || instruction.Metadata.FieldIds.Length != operands.Length)
                throw ModulesExceptionFactory.Error("evaluation", "InternalInvariantViolation", nodeLocus, new { reason = "InvalidRecordMake" });

            return new ModuleRecord(
                instruction.Type.Name,
                instruction.Metadata.FieldIds.Select((fieldId, index) => new KeyValuePair<string, ModuleValue>(fieldId, operands[index])));
        }

        private static ModuleValue GetRecordField(IrInstruction instruction, ModuleValue[] operands, string nodeLocus)
        {
            if (instruction.Metadata.FieldId is null || operands.Length != 1 || operands[0] is not ModuleRecord record)
                throw ModulesExceptionFactory.Error("evaluation", "InternalInvariantViolation", nodeLocus, new { reason = "InvalidRecordGet" });
            if (!record.Fields.TryGetValue(instruction.Metadata.FieldId, out var value))
                throw ModulesExceptionFactory.Error("evaluation", "InternalInvariantViolation", nodeLocus, new { reason = "MissingRecordField", fieldId = instruction.Metadata.FieldId });
            return value;
        }

        private static ModuleValue MakeEmptySequence(IrInstruction instruction, string nodeLocus)
        {
            if (instruction.Metadata.ElementType is null || instruction.Metadata.Capacity is null)
                throw ModulesExceptionFactory.Error("evaluation", "InternalInvariantViolation", nodeLocus, new { reason = "InvalidSequenceMetadata" });
            return new ModuleSequence(instruction.Metadata.ElementType, instruction.Metadata.Capacity.Value, ImmutableArray<ModuleValue>.Empty);
        }

        private static ModuleValue GetSequenceItem(ModuleValue[] operands, string nodeLocus)
        {
            var sequence = Sequence(operands[0], nodeLocus);
            var index = I64(operands[1], nodeLocus);
            if (index < 0 || index >= sequence.Items.Length)
                throw ModulesExceptionFactory.Error("evaluation", "SequenceIndexOutOfRange", nodeLocus, new { index, length = sequence.Items.Length });
            return sequence.Items[(int)index];
        }

        private static ModuleValue AppendSequence(ModuleValue[] operands, string nodeLocus)
        {
            var sequence = Sequence(operands[0], nodeLocus);
            if (sequence.Items.Length >= sequence.Capacity)
                throw ModulesExceptionFactory.Error("evaluation", "SequenceCapacityExceeded", nodeLocus, new { length = sequence.Items.Length, capacity = sequence.Capacity });
            return sequence with { Items = sequence.Items.Add(operands[1]) };
        }

        private void ConsumeStep(string nodeLocus)
        {
            if (Steps >= maxSteps)
                throw ModulesExceptionFactory.Error("evaluation", "EvaluationStepLimitExceeded", nodeLocus, new { consumed = Steps, max = maxSteps });
            Steps++;
        }

        private static ModuleValue Resolve(int index, IReadOnlyList<ModuleValue> parameters, ModuleValue[] values, string locus)
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
            throw ModulesExceptionFactory.Error("evaluation", "InternalInvariantViolation", locus, new { reason = "OperandIndex", index });
        }

        private static long I64(ModuleValue value, string locus)
            => value is ModuleI64 integer
                ? integer.Value
                : throw ModulesExceptionFactory.Error("evaluation", "InternalInvariantViolation", locus, new { reason = "ExpectedI64" });

        private static bool Bool(ModuleValue value, string locus)
            => value is ModuleBool boolean
                ? boolean.Value
                : throw ModulesExceptionFactory.Error("evaluation", "InternalInvariantViolation", locus, new { reason = "ExpectedBool" });

        private static ModuleSequence Sequence(ModuleValue value, string locus)
            => value as ModuleSequence
                ?? throw ModulesExceptionFactory.Error("evaluation", "InternalInvariantViolation", locus, new { reason = "ExpectedSequence" });

        private void EnsureValueType(ModuleValue? value, TypeRef expected, string locus)
        {
            if (value is null || value.TypeKind != expected.Kind)
                throw ModulesExceptionFactory.Error("evaluation", "RuntimeTypeMismatch", locus, new
                {
                    expected = expected.ToString(),
                    actual = value?.TypeKind ?? "null"
                });

            switch (expected.Kind)
            {
                case "I64":
                case "Bool":
                    return;
                case "Seq" when expected.Element is not null && expected.Capacity is not null && value is ModuleSequence sequence:
                    if (sequence.ElementType is null
                        || sequence.Items.IsDefault
                        || sequence.Capacity < 0
                        || !TypesEquivalent(sequence.ElementType, expected.Element)
                        || sequence.Capacity != expected.Capacity.Value
                        || sequence.Items.Length > sequence.Capacity)
                        throw ModulesExceptionFactory.Error("evaluation", "RuntimeTypeMismatch", locus, new
                        {
                            expected = expected.ToString(),
                            actual = $"Seq<{sequence.ElementType},{sequence.Capacity}>[{sequence.Items.Length}]"
                        });
                    for (var index = 0; index < sequence.Items.Length; index++)
                        EnsureValueType(sequence.Items[index], expected.Element, $"{locus}/item/{index.ToString(CultureInfo.InvariantCulture)}");
                    return;
                case "Record" when expected.Name is not null && value is ModuleRecord record:
                    if (record.RecordTypeId != expected.Name || record.Fields is null || !types.TryGetValue(expected.Name, out var declaration))
                        throw ModulesExceptionFactory.Error("evaluation", "RuntimeTypeMismatch", locus, new { expected = expected.ToString(), actual = record.RecordTypeId });
                    var expectedFields = declaration.Fields.Select(field => field.Id).Order(StringComparer.Ordinal).ToArray();
                    var actualFields = record.Fields.Keys.Order(StringComparer.Ordinal).ToArray();
                    if (!actualFields.SequenceEqual(expectedFields, StringComparer.Ordinal))
                        throw ModulesExceptionFactory.Error("evaluation", "RuntimeTypeMismatch", locus, new { expected = expectedFields, actual = actualFields });
                    foreach (var field in declaration.Fields)
                        EnsureValueType(record.Fields[field.Id], field.Type, $"{locus}/field/{field.Id}");
                    return;
                default:
                    throw ModulesExceptionFactory.Error("evaluation", "UnsupportedRuntimeType", locus, new { expected = expected.ToString() });
            }
        }

        private static bool TypesEquivalent(TypeRef left, TypeRef right)
            => left.Kind == right.Kind
                && left.Name == right.Name
                && left.Capacity == right.Capacity
                && (left.Element is null ? right.Element is null : right.Element is not null && TypesEquivalent(left.Element, right.Element));
    }
}
