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

        var state = new EvaluationState(functions, limits.MaxSteps);
        var value = state.EvaluateFunction(function, arguments, 0);
        return new ModuleEvaluationResult(value, state.Steps);
    }

    private sealed class EvaluationState(
        ImmutableDictionary<string, FunctionIr> functions,
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
                        "if" => EvaluateIf(instruction, operands, nodeLocus, callDepth),
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

        private static void EnsureValueType(ModuleValue? value, TypeRef expected, string locus)
        {
            if (expected.Kind is not ("I64" or "Bool"))
                throw ModulesExceptionFactory.Error("evaluation", "UnsupportedRuntimeType", locus, new { expected = expected.ToString() });
            if (value is null || value.TypeKind != expected.Kind)
                throw ModulesExceptionFactory.Error("evaluation", "RuntimeTypeMismatch", locus, new
                {
                    expected = expected.ToString(),
                    actual = value?.TypeKind ?? "null"
                });
        }
    }
}
