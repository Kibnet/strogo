using System.Collections.Immutable;

namespace Strogo.Modules;

internal static class OwnerContractSemantics
{
    internal static ImmutableArray<OwnerEntryContract> Validate(
        ImmutableArray<OwnerEntryContract> entries,
        ImmutableArray<OwnerModel> models)
    {
        var modelById = models.ToImmutableDictionary(model => model.Id, StringComparer.Ordinal);
        foreach (var model in models)
        {
            var parameterTypes = model.Parameters.ToImmutableDictionary(parameter => parameter.Id, parameter => parameter.Type, StringComparer.Ordinal);
            var actual = ValidateExpression(model.Body, parameterTypes, null, modelById, allowResult: false, allowModelCall: false, $"model/{model.Id}/body");
            EnsureSameType(model.ReturnType, actual, $"model/{model.Id}/body");
            EnsureBooleanExpressionsUseTotalScalarOperands(model.Body, $"model/{model.Id}/body");
        }

        var validated = ImmutableArray.CreateBuilder<OwnerEntryContract>(entries.Length);
        var referencedModels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var parameterTypes = entry.Parameters.ToImmutableDictionary(parameter => parameter.Id, parameter => parameter.Type, StringComparer.Ordinal);
            var requiresType = ValidateExpression(entry.Requires, parameterTypes, null, modelById, allowResult: false, allowModelCall: false, $"contract/{entry.Id}/requires");
            EnsureKind("Bool", requiresType, $"contract/{entry.Id}/requires");
            EnsureNoExecutableArithmetic(entry.Requires, $"contract/{entry.Id}/requires");
            var ensuresType = ValidateExpression(entry.Ensures, parameterTypes, entry.ReturnType, modelById, allowResult: true, allowModelCall: true, $"contract/{entry.Id}/ensures");
            EnsureKind("Bool", ensuresType, $"contract/{entry.Id}/ensures");
            var modelRef = RequireExactOutcome(entry, modelById);
            if (!referencedModels.Add(modelRef))
                throw ModulesExceptionFactory.Error("owner-parse", "OwnerModelReuseNotSupported", entry.Id, new { modelRef });
            var model = modelById[modelRef];
            EnsureSignature(entry.Parameters, entry.ReturnType, model.Parameters, model.ReturnType, $"contract/{entry.Id}");

            foreach (var witness in entry.Witnesses)
            {
                var environment = witness.Arguments.ToImmutableDictionary(argument => argument.ParameterId, argument => argument.Value, StringComparer.Ordinal);
                if (!OwnerContractEvaluator.EvaluateBoolean(entry.Requires, environment, $"contract/{entry.Id}/witness/{witness.Id}/requires"))
                    throw ModulesExceptionFactory.Error("owner-evaluate", "RequiresWitnessRejected", $"contract/{entry.Id}/witness/{witness.Id}");
                _ = OwnerContractEvaluator.Evaluate(model.Body, environment, $"contract/{entry.Id}/witness/{witness.Id}/model");
            }
            validated.Add(entry with { ModelRef = modelRef });
        }

        var unusedModels = modelById.Keys.Except(referencedModels, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (unusedModels.Length != 0)
            throw ModulesExceptionFactory.Error("owner-parse", "UnusedOwnerModel", unusedModels[0], new { models = unusedModels });
        return validated.ToImmutable();
    }

    private static void EnsureNoExecutableArithmetic(OwnerExpression expression, string locus)
    {
        if (expression.Op is "i64.add" or "i64.sub")
            throw ModulesExceptionFactory.Error("owner-parse", "ArithmeticInRequiresNotSupported", locus);
        for (var index = 0; index < expression.Args.Length; index++)
            EnsureNoExecutableArithmetic(expression.Args[index], $"{locus}/arg/{index}");
    }

    private static void EnsureBooleanExpressionsUseTotalScalarOperands(OwnerExpression expression, string locus)
    {
        if (expression.Type.Kind == "Bool" && ContainsExecutableArithmetic(expression))
            throw ModulesExceptionFactory.Error("owner-parse", "ArithmeticInBooleanContractNotSupported", locus);
        for (var index = 0; index < expression.Args.Length; index++)
            EnsureBooleanExpressionsUseTotalScalarOperands(expression.Args[index], $"{locus}/arg/{index}");
    }

    private static bool ContainsExecutableArithmetic(OwnerExpression expression)
        => expression.Op is "i64.add" or "i64.sub" || expression.Args.Any(ContainsExecutableArithmetic);

    private static string RequireExactOutcome(OwnerEntryContract entry, IReadOnlyDictionary<string, OwnerModel> models)
    {
        var ensures = entry.Ensures;
        if (ensures.Op != "eq" || ensures.Args.Length != 2 || ensures.Args[0].Op != "result" || ensures.Args[1].Op != "model.call")
            throw ModulesExceptionFactory.Error("owner-parse", "ExactOutcomeRequired", entry.Id);
        var call = ensures.Args[1];
        if (call.ReferenceId is null || !models.ContainsKey(call.ReferenceId))
            throw ModulesExceptionFactory.Error("owner-parse", "UnknownOwnerModel", entry.Id, new { modelRef = call.ReferenceId });
        if (call.Args.Length != entry.Parameters.Length || call.Args.Where((argument, index) =>
                argument.Op != "param" || argument.ReferenceId != entry.Parameters[index].Id || argument.Type.Kind != entry.Parameters[index].Type.Kind).Any())
            throw ModulesExceptionFactory.Error("owner-parse", "ExactOutcomeArgumentsMismatch", entry.Id);
        return call.ReferenceId;
    }

    private static TypeRef ValidateExpression(
        OwnerExpression expression,
        IReadOnlyDictionary<string, TypeRef> parameters,
        TypeRef? resultType,
        IReadOnlyDictionary<string, OwnerModel> models,
        bool allowResult,
        bool allowModelCall,
        string locus)
    {
        TypeRef actual;
        switch (expression.Op)
        {
            case "param":
                if (expression.ReferenceId is null || !parameters.TryGetValue(expression.ReferenceId, out actual!))
                    throw ModulesExceptionFactory.Error("owner-parse", "UnknownContractParameter", locus, new { parameterId = expression.ReferenceId });
                break;
            case "result":
                if (!allowResult || resultType is null)
                    throw ModulesExceptionFactory.Error("owner-parse", "ResultOutsideEnsures", locus);
                actual = resultType;
                break;
            case "i64.const": actual = new TypeRef("I64"); break;
            case "bool.const": actual = new TypeRef("Bool"); break;
            case "i64.add" or "i64.sub":
                RequireArity(expression, 2, locus);
                RequireArguments(expression, parameters, resultType, models, allowResult, allowModelCall, locus, "I64", "I64");
                actual = new TypeRef("I64");
                break;
            case "i64.le":
                RequireArity(expression, 2, locus);
                RequireArguments(expression, parameters, resultType, models, allowResult, allowModelCall, locus, "I64", "I64");
                actual = new TypeRef("Bool");
                break;
            case "eq":
                RequireArity(expression, 2, locus);
                var left = ValidateExpression(expression.Args[0], parameters, resultType, models, allowResult, allowModelCall, $"{locus}/arg/0");
                var right = ValidateExpression(expression.Args[1], parameters, resultType, models, allowResult, allowModelCall, $"{locus}/arg/1");
                EnsureSameType(left, right, locus);
                actual = new TypeRef("Bool");
                break;
            case "bool.not":
                RequireArity(expression, 1, locus);
                RequireArguments(expression, parameters, resultType, models, allowResult, allowModelCall, locus, "Bool");
                actual = new TypeRef("Bool");
                break;
            case "bool.and" or "bool.or":
                RequireArity(expression, 2, locus);
                RequireArguments(expression, parameters, resultType, models, allowResult, allowModelCall, locus, "Bool", "Bool");
                actual = new TypeRef("Bool");
                break;
            case "if":
                RequireArity(expression, 3, locus);
                var condition = ValidateExpression(expression.Args[0], parameters, resultType, models, allowResult, allowModelCall, $"{locus}/arg/0");
                EnsureKind("Bool", condition, $"{locus}/arg/0");
                var thenType = ValidateExpression(expression.Args[1], parameters, resultType, models, allowResult, allowModelCall, $"{locus}/arg/1");
                var elseType = ValidateExpression(expression.Args[2], parameters, resultType, models, allowResult, allowModelCall, $"{locus}/arg/2");
                EnsureSameType(thenType, elseType, locus);
                actual = thenType;
                break;
            case "model.call":
                if (!allowModelCall || expression.ReferenceId is null || !models.TryGetValue(expression.ReferenceId, out var model))
                    throw ModulesExceptionFactory.Error("owner-parse", "UnknownOrDisallowedModelCall", locus, new { modelRef = expression.ReferenceId });
                RequireArity(expression, model.Parameters.Length, locus);
                for (var index = 0; index < expression.Args.Length; index++)
                {
                    var argumentType = ValidateExpression(expression.Args[index], parameters, resultType, models, allowResult, allowModelCall, $"{locus}/arg/{index}");
                    EnsureSameType(model.Parameters[index].Type, argumentType, $"{locus}/arg/{index}");
                }
                actual = model.ReturnType;
                break;
            default:
                throw ModulesExceptionFactory.Error("owner-parse", "UnsupportedContractOpcode", locus, new { expression.Op });
        }
        EnsureSameType(expression.Type, actual, locus);
        return actual;
    }

    private static void RequireArguments(
        OwnerExpression expression,
        IReadOnlyDictionary<string, TypeRef> parameters,
        TypeRef? resultType,
        IReadOnlyDictionary<string, OwnerModel> models,
        bool allowResult,
        bool allowModelCall,
        string locus,
        params string[] expectedKinds)
    {
        for (var index = 0; index < expectedKinds.Length; index++)
        {
            var actual = ValidateExpression(expression.Args[index], parameters, resultType, models, allowResult, allowModelCall, $"{locus}/arg/{index}");
            EnsureKind(expectedKinds[index], actual, $"{locus}/arg/{index}");
        }
    }

    private static void RequireArity(OwnerExpression expression, int expected, string locus)
    {
        if (expression.Args.Length != expected)
            throw ModulesExceptionFactory.Error("owner-parse", "ContractArityMismatch", locus, new { expected, actual = expression.Args.Length });
    }

    internal static void EnsureSignature(
        ImmutableArray<FunctionParameter> expectedParameters,
        TypeRef expectedReturn,
        ImmutableArray<FunctionParameter> actualParameters,
        TypeRef actualReturn,
        string locus)
    {
        if (expectedParameters.Length != actualParameters.Length ||
            expectedParameters.Where((parameter, index) => parameter.Id != actualParameters[index].Id || parameter.Type.Kind != actualParameters[index].Type.Kind).Any() ||
            expectedReturn.Kind != actualReturn.Kind)
            throw ModulesExceptionFactory.Error("owner-bind", "OwnerSignatureMismatch", locus);
    }

    private static void EnsureSameType(TypeRef expected, TypeRef actual, string locus)
    {
        if (expected.Kind != actual.Kind)
            throw ModulesExceptionFactory.Error("owner-parse", "ContractTypeMismatch", locus, new { expected = expected.Kind, actual = actual.Kind });
    }

    private static void EnsureKind(string expected, TypeRef actual, string locus)
    {
        if (expected != actual.Kind)
            throw ModulesExceptionFactory.Error("owner-parse", "ContractTypeMismatch", locus, new { expected, actual = actual.Kind });
    }
}

public static class OwnerContractEvaluator
{
    public static OwnerScalarValue Evaluate(OwnerExpression expression, IReadOnlyDictionary<string, OwnerScalarValue> environment, string locus = "expression")
    {
        try
        {
            var result = expression.Op switch
            {
                "param" when expression.ReferenceId is { } parameterId && environment.TryGetValue(parameterId, out var value) => value,
                "param" => throw ModulesExceptionFactory.Error("owner-evaluate", "UnknownContractParameter", locus, new { parameterId = expression.ReferenceId }),
                "i64.const" => OwnerScalarValue.FromI64(expression.I64Value!.Value),
                "bool.const" => OwnerScalarValue.FromBool(expression.BoolValue!.Value),
                "i64.add" => OwnerScalarValue.FromI64(checked(AsI64(Evaluate(expression.Args[0], environment, $"{locus}/arg/0"), $"{locus}/arg/0") + AsI64(Evaluate(expression.Args[1], environment, $"{locus}/arg/1"), $"{locus}/arg/1"))),
                "i64.sub" => OwnerScalarValue.FromI64(checked(AsI64(Evaluate(expression.Args[0], environment, $"{locus}/arg/0"), $"{locus}/arg/0") - AsI64(Evaluate(expression.Args[1], environment, $"{locus}/arg/1"), $"{locus}/arg/1"))),
                "i64.le" => OwnerScalarValue.FromBool(AsI64(Evaluate(expression.Args[0], environment, $"{locus}/arg/0"), $"{locus}/arg/0") <= AsI64(Evaluate(expression.Args[1], environment, $"{locus}/arg/1"), $"{locus}/arg/1")),
                "eq" => OwnerScalarValue.FromBool(Equal(Evaluate(expression.Args[0], environment, $"{locus}/arg/0"), Evaluate(expression.Args[1], environment, $"{locus}/arg/1"))),
                "bool.not" => OwnerScalarValue.FromBool(!AsBool(Evaluate(expression.Args[0], environment, $"{locus}/arg/0"), $"{locus}/arg/0")),
                "bool.and" => EvaluateStrictBooleanBinary(expression, environment, locus, static (left, right) => left && right),
                "bool.or" => EvaluateStrictBooleanBinary(expression, environment, locus, static (left, right) => left || right),
                "if" => AsBool(Evaluate(expression.Args[0], environment, $"{locus}/arg/0"), $"{locus}/arg/0")
                    ? Evaluate(expression.Args[1], environment, $"{locus}/arg/1")
                    : Evaluate(expression.Args[2], environment, $"{locus}/arg/2"),
                _ => throw ModulesExceptionFactory.Error("owner-evaluate", "UnsupportedEvaluationOpcode", locus, new { expression.Op })
            };
            if (result.Type != expression.Type.Kind)
                throw ModulesExceptionFactory.Error("owner-evaluate", "RuntimeContractTypeMismatch", locus, new { expected = expression.Type.Kind, actual = result.Type });
            return result;
        }
        catch (OverflowException)
        {
            throw ModulesExceptionFactory.Error("owner-evaluate", "ModelUndefinedAtWitness", locus);
        }
    }

    public static bool EvaluateBoolean(OwnerExpression expression, IReadOnlyDictionary<string, OwnerScalarValue> environment, string locus = "expression")
    {
        var result = Evaluate(expression, environment, locus);
        if (result.Type != "Bool") throw ModulesExceptionFactory.Error("owner-evaluate", "ExpectedBoolean", locus);
        return result.Bool;
    }

    private static bool Equal(OwnerScalarValue left, OwnerScalarValue right)
        => left.Type == right.Type && (left.Type == "I64" ? left.I64 == right.I64 : left.Bool == right.Bool);

    private static OwnerScalarValue EvaluateStrictBooleanBinary(
        OwnerExpression expression,
        IReadOnlyDictionary<string, OwnerScalarValue> environment,
        string locus,
        Func<bool, bool, bool> operation)
    {
        var left = AsBool(Evaluate(expression.Args[0], environment, $"{locus}/arg/0"), $"{locus}/arg/0");
        var right = AsBool(Evaluate(expression.Args[1], environment, $"{locus}/arg/1"), $"{locus}/arg/1");
        return OwnerScalarValue.FromBool(operation(left, right));
    }

    private static long AsI64(OwnerScalarValue value, string locus)
        => value.Type == "I64" ? value.I64 : throw ModulesExceptionFactory.Error("owner-evaluate", "RuntimeContractTypeMismatch", locus, new { expected = "I64", actual = value.Type });

    private static bool AsBool(OwnerScalarValue value, string locus)
        => value.Type == "Bool" ? value.Bool : throw ModulesExceptionFactory.Error("owner-evaluate", "RuntimeContractTypeMismatch", locus, new { expected = "Bool", actual = value.Type });
}

public static class OwnerContractBinder
{
    public static OwnerContractBinding Bind(ModuleIr module, OwnerBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(bundle);
        if (module.Imports.Length != 0 || module.Types.Length != 0)
            throw ModulesExceptionFactory.Error("owner-bind", "UnsupportedOwnerCompositeModule", details: new { imports = module.Imports.Length, types = module.Types.Length });
        var exported = module.Exports.ToImmutableHashSet(StringComparer.Ordinal);
        var helpers = module.Functions.Where(function => !exported.Contains(function.Id)).Select(function => function.Id).Order(StringComparer.Ordinal).ToArray();
        if (helpers.Length != 0)
            throw ModulesExceptionFactory.Error("owner-bind", "UnsupportedOwnerHelpers", helpers[0], new { helpers });

        var contractByFunction = bundle.EntryContracts.ToImmutableDictionary(entry => entry.FunctionRef, StringComparer.Ordinal);
        var missing = exported.Except(contractByFunction.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var extra = contractByFunction.Keys.Except(exported, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length != 0 || extra.Length != 0)
            throw ModulesExceptionFactory.Error("owner-bind", "OwnerEntryMismatch", details: new { missing, extra });

        var modelById = bundle.Models.ToImmutableDictionary(model => model.Id, StringComparer.Ordinal);
        var entries = ImmutableArray.CreateBuilder<BoundOwnerEntry>();
        foreach (var function in module.Functions.OrderBy(function => function.Id, StringComparer.Ordinal))
        {
            var contract = contractByFunction[function.Id];
            if (function.ContractRef != contract.Id)
                throw ModulesExceptionFactory.Error("owner-bind", "OwnerContractRefMismatch", function.Id, new { module = function.ContractRef, owner = contract.Id });
            OwnerContractSemantics.EnsureSignature(function.Parameters, function.ReturnType, contract.Parameters, contract.ReturnType, $"function/{function.Id}");
            var model = modelById[contract.ModelRef];
            var witnesses = contract.Witnesses.Select(witness =>
            {
                var environment = witness.Arguments.ToImmutableDictionary(argument => argument.ParameterId, argument => argument.Value, StringComparer.Ordinal);
                if (!OwnerContractEvaluator.EvaluateBoolean(contract.Requires, environment, $"contract/{contract.Id}/witness/{witness.Id}/requires"))
                    throw ModulesExceptionFactory.Error("owner-evaluate", "RequiresWitnessRejected", witness.Id);
                return new EvaluatedOwnerWitness(witness.Id, witness.Arguments, OwnerContractEvaluator.Evaluate(model.Body, environment, $"contract/{contract.Id}/witness/{witness.Id}/model"));
            }).ToImmutableArray();
            entries.Add(new BoundOwnerEntry(function, contract, model, witnesses));
        }
        return new OwnerContractBinding(module, bundle, entries.ToImmutable());
    }
}
