using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;

namespace Strogo.Modules;

internal static class OwnerV04Semantics
{
    internal static void Validate(
        ImmutableArray<TypeDecl> types,
        ImmutableArray<OwnerEntryContractV04> entries,
        ImmutableArray<OwnerModelV04> models,
        OwnerBundleLimitsV04 limits)
    {
        var typeById = types.ToImmutableDictionary(type => type.Id, StringComparer.Ordinal);
        var modelById = models.ToImmutableDictionary(model => model.Id, StringComparer.Ordinal);
        ValidateExactTypeClosure(types, entries, models, typeById);

        foreach (var model in models)
        {
            var parameters = model.Parameters.ToImmutableDictionary(parameter => parameter.Id, parameter => parameter.Type, StringComparer.Ordinal);
            var actual = ValidateModelExpression(model.Body, parameters, typeById, $"model/{model.Id}/body");
            EnsureType(model.ReturnType, actual, $"model/{model.Id}/body");
        }

        var referencedModels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var parameters = entry.Parameters.ToImmutableDictionary(parameter => parameter.Id, parameter => parameter.Type, StringComparer.Ordinal);
            EnsureType(new TypeRef("Bool"), ValidateProofExpression(entry.Requires, parameters, typeById, ImmutableDictionary<string, TypeRef>.Empty, false, $"contract/{entry.Id}/requires"), $"contract/{entry.Id}/requires");
            var proofCost = OwnerBundleV04Parser.ProofWorstCaseCost(entry.Requires);
            if (proofCost > limits.MaxProofEvaluationSteps)
                throw ModulesExceptionFactory.Error("owner-parse", "ProofWorstCaseCostExceeded", $"contract/{entry.Id}/requires", new
                {
                    actual = proofCost > OwnerBundleLimitsV04.ProofEvaluationStepsHardMaximum ? $">{OwnerBundleLimitsV04.ProofEvaluationStepsHardMaximum}" : proofCost.ToString(CultureInfo.InvariantCulture),
                    max = limits.MaxProofEvaluationSteps.ToString(CultureInfo.InvariantCulture)
                });
            if (!modelById.TryGetValue(entry.ModelRef, out var model))
                throw ModulesExceptionFactory.Error("owner-parse", "UnknownOwnerModel", entry.Id, new { entry.ModelRef });
            if (!referencedModels.Add(entry.ModelRef))
                throw ModulesExceptionFactory.Error("owner-parse", "OwnerModelReuseNotSupported", entry.Id, new { entry.ModelRef });
            OwnerContractSemantics.EnsureSignature(entry.Parameters, entry.ReturnType, model.Parameters, model.ReturnType, $"contract/{entry.Id}");

            foreach (var witness in entry.Witnesses)
            {
                var environment = witness.Arguments.ToImmutableDictionary(argument => argument.ParameterId, argument => argument.Value, StringComparer.Ordinal);
                if (!OwnerProofEvaluator.EvaluateBoolean(entry.Requires, environment, types, limits.MaxProofEvaluationSteps, null, $"contract/{entry.Id}/witness/{witness.Id}/requires"))
                    throw ModulesExceptionFactory.Error("owner-evaluate", "RequiresWitnessRejected", $"contract/{entry.Id}/witness/{witness.Id}");
                try
                {
                    _ = OwnerModelEvaluatorV04.Evaluate(model.Body, environment, types, $"contract/{entry.Id}/witness/{witness.Id}/model");
                }
                catch (ModuleException exception) when (exception.Code is "SequenceIndexOutOfRange" or "SequenceCapacityExceeded" or "ModelUndefinedAtWitness")
                {
                    throw ModulesExceptionFactory.Error("owner-evaluate", "ModelUndefinedAtWitness", $"contract/{entry.Id}/witness/{witness.Id}/model");
                }
            }
        }

        var unused = modelById.Keys.Except(referencedModels, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (unused.Length != 0) throw ModulesExceptionFactory.Error("owner-parse", "UnusedOwnerModel", unused[0], new { models = unused });
    }

    private static TypeRef ValidateModelExpression(OwnerExpression expression, IReadOnlyDictionary<string, TypeRef> parameters, IReadOnlyDictionary<string, TypeDecl> types, string locus)
    {
        if (expression.Op != "fold") return OwnerContractSemantics.ValidateExpression(expression, parameters, types, locus);
        if (expression.Args.Length < 2 || expression.FoldStep is null)
            throw ModulesExceptionFactory.Error("owner-parse", "ContractArityMismatch", locus, new { expectedAtLeast = 2, actual = expression.Args.Length });
        var argumentTypes = expression.Args.Select((argument, index) => OwnerContractSemantics.ValidateExpression(argument, parameters, types, $"{locus}/arg/{index}")).ToImmutableArray();
        var sequence = RequireSequence(argumentTypes[0], $"{locus}/arg/0");
        var accumulator = argumentTypes[1];
        EnsureType(accumulator, expression.Type, locus);
        var expectedStepTypes = ImmutableArray.CreateBuilder<TypeRef>(argumentTypes.Length + 1);
        expectedStepTypes.Add(new TypeRef("I64"));
        expectedStepTypes.Add(sequence.Element!);
        expectedStepTypes.Add(accumulator);
        expectedStepTypes.AddRange(argumentTypes.Skip(2));
        if (expression.FoldStep.Parameters.Length != expectedStepTypes.Count)
            throw ModulesExceptionFactory.Error("owner-parse", "RegionParametersMismatch", $"{locus}/step", new { expected = expectedStepTypes.Count, actual = expression.FoldStep.Parameters.Length });
        for (var index = 0; index < expectedStepTypes.Count; index++) EnsureType(expectedStepTypes[index], expression.FoldStep.Parameters[index].Type, $"{locus}/step/parameter/{index}");
        var stepParameters = expression.FoldStep.Parameters.ToImmutableDictionary(parameter => parameter.Id, parameter => parameter.Type, StringComparer.Ordinal);
        var stepResult = OwnerContractSemantics.ValidateExpression(expression.FoldStep.Body, stepParameters, types, $"{locus}/step/body");
        EnsureType(accumulator, stepResult, $"{locus}/step/body");
        return accumulator;
    }

    private static TypeRef ValidateProofExpression(
        ProofExpression expression,
        IReadOnlyDictionary<string, TypeRef> parameters,
        IReadOnlyDictionary<string, TypeDecl> types,
        ImmutableDictionary<string, TypeRef> binders,
        bool insideQuantifier,
        string locus)
    {
        TypeRef Arg(int index) => ValidateProofExpression(expression.Args[index], parameters, types, binders, insideQuantifier, $"{locus}/arg/{index}");
        void Arity(int expected)
        {
            if (expression.Args.Length != expected) throw ModulesExceptionFactory.Error("owner-parse", "ContractArityMismatch", locus, new { expected, actual = expression.Args.Length });
        }
        TypeRef actual;
        switch (expression.Op)
        {
            case "param":
                if (expression.ReferenceId is null || !parameters.TryGetValue(expression.ReferenceId, out var parameterType))
                    throw ModulesExceptionFactory.Error("owner-parse", "UnknownContractParameter", locus, new { parameterId = expression.ReferenceId });
                actual = parameterType;
                break;
            case "proof.bound":
                if (expression.BinderId is null || !binders.TryGetValue(expression.BinderId, out var binderType))
                    throw ModulesExceptionFactory.Error("owner-parse", "UnknownProofBinder", locus, new { binderId = expression.BinderId });
                actual = binderType;
                break;
            case "fold.prefixLength" or "fold.sequence" or "fold.initialAccumulator" or "fold.accumulator" or "fold.environment":
                throw ModulesExceptionFactory.Error("owner-parse", "UnsupportedProofOpcode", locus, new { opcode = expression.Op });
            case "i64.const": actual = new TypeRef("I64"); break;
            case "bool.const": actual = new TypeRef("Bool"); break;
            case "math.const": actual = new TypeRef("MathInt"); break;
            case "eq":
                Arity(2); var left = Arg(0); EnsureType(left, Arg(1), $"{locus}/arg/1"); actual = new TypeRef("Bool"); break;
            case "i64.add" or "i64.sub":
                Arity(2); EnsureType(new TypeRef("I64"), Arg(0), $"{locus}/arg/0"); EnsureType(new TypeRef("I64"), Arg(1), $"{locus}/arg/1"); actual = new TypeRef("I64"); break;
            case "i64.le":
                Arity(2); EnsureType(new TypeRef("I64"), Arg(0), $"{locus}/arg/0"); EnsureType(new TypeRef("I64"), Arg(1), $"{locus}/arg/1"); actual = new TypeRef("Bool"); break;
            case "math.le":
                Arity(2); EnsureType(new TypeRef("MathInt"), Arg(0), $"{locus}/arg/0"); EnsureType(new TypeRef("MathInt"), Arg(1), $"{locus}/arg/1"); actual = new TypeRef("Bool"); break;
            case "math.from_i64":
                Arity(1); EnsureType(new TypeRef("I64"), Arg(0), $"{locus}/arg/0"); actual = new TypeRef("MathInt"); break;
            case "math.add" or "math.sub":
                Arity(2); EnsureType(new TypeRef("MathInt"), Arg(0), $"{locus}/arg/0"); EnsureType(new TypeRef("MathInt"), Arg(1), $"{locus}/arg/1"); actual = new TypeRef("MathInt"); break;
            case "bool.not":
                Arity(1); EnsureType(new TypeRef("Bool"), Arg(0), $"{locus}/arg/0"); actual = new TypeRef("Bool"); break;
            case "bool.and" or "bool.or":
                Arity(2); EnsureType(new TypeRef("Bool"), Arg(0), $"{locus}/arg/0"); EnsureType(new TypeRef("Bool"), Arg(1), $"{locus}/arg/1"); actual = new TypeRef("Bool"); break;
            case "if":
                Arity(3); EnsureType(new TypeRef("Bool"), Arg(0), $"{locus}/arg/0"); var thenType = Arg(1); EnsureType(thenType, Arg(2), $"{locus}/arg/2"); actual = thenType; break;
            case "record.make":
                if (expression.RecordType is null || !types.TryGetValue(expression.RecordType, out var declaration))
                    throw ModulesExceptionFactory.Error("owner-parse", "OwnerTypeClosureMissing", expression.RecordType ?? locus);
                var expectedFields = declaration.Fields.OrderBy(field => field.Id, StringComparer.Ordinal).ToArray();
                var actualFields = expression.FieldIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
                if (expression.Args.Length != expression.FieldIds.Length || !actualFields.SequenceEqual(expectedFields.Select(field => field.Id), StringComparer.Ordinal))
                    throw ModulesExceptionFactory.Error("owner-parse", "RecordFieldMismatch", locus);
                foreach (var field in expectedFields)
                {
                    var index = expression.FieldIds.IndexOf(field.Id);
                    EnsureType(field.Type, Arg(index), $"{locus}/arg/{index}");
                }
                actual = new TypeRef("Record", Name: expression.RecordType);
                break;
            case "record.get":
                Arity(1); var record = Arg(0);
                if (record.Kind != "Record" || record.Name is null || !types.TryGetValue(record.Name, out var recordDeclaration))
                    throw ModulesExceptionFactory.Error("owner-parse", "ContractTypeMismatch", locus);
                actual = recordDeclaration.Fields.SingleOrDefault(field => field.Id == expression.ReferenceId)?.Type
                    ?? throw ModulesExceptionFactory.Error("owner-parse", "UnknownRecordField", locus, new { fieldId = expression.ReferenceId });
                break;
            case "seq.empty":
                Arity(0);
                if (expression.ElementType is null || expression.Capacity is null) throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", locus);
                actual = new TypeRef("Seq", Element: expression.ElementType, Capacity: expression.Capacity);
                break;
            case "seq.length":
                Arity(1); _ = RequireSequence(Arg(0), $"{locus}/arg/0"); actual = new TypeRef("I64"); break;
            case "seq.get":
                Arity(2); var getSequence = RequireSequence(Arg(0), $"{locus}/arg/0"); EnsureType(new TypeRef("I64"), Arg(1), $"{locus}/arg/1"); actual = getSequence.Element!; break;
            case "seq.append":
                Arity(2); var appendSequence = RequireSequence(Arg(0), $"{locus}/arg/0"); EnsureType(appendSequence.Element!, Arg(1), $"{locus}/arg/1"); actual = appendSequence; break;
            case "seq.sum_i64":
                Arity(1); var sumSequence = RequireSequence(Arg(0), $"{locus}/arg/0"); EnsureType(new TypeRef("I64"), sumSequence.Element!, $"{locus}/arg/0"); actual = new TypeRef("MathInt"); break;
            case "seq.prefix_sum_i64":
                Arity(2); var prefixSequence = RequireSequence(Arg(0), $"{locus}/arg/0"); EnsureType(new TypeRef("I64"), prefixSequence.Element!, $"{locus}/arg/0"); EnsureType(new TypeRef("I64"), Arg(1), $"{locus}/arg/1"); actual = new TypeRef("MathInt"); break;
            case "forall.sequence":
                if (insideQuantifier) throw ModulesExceptionFactory.Error("owner-parse", "NestedProofQuantifierNotSupported", locus);
                if (expression.BinderId is null || expression.Sequence is null || expression.Body is null) throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", locus);
                if (binders.ContainsKey(expression.BinderId)) throw ModulesExceptionFactory.Error("owner-parse", "DuplicateProofBinder", locus, new { expression.BinderId });
                var quantifiedSequence = RequireSequence(ValidateProofExpression(expression.Sequence, parameters, types, binders, false, $"{locus}/sequence"), $"{locus}/sequence");
                _ = quantifiedSequence;
                EnsureType(new TypeRef("Bool"), ValidateProofExpression(expression.Body, parameters, types, binders.Add(expression.BinderId, new TypeRef("I64")), true, $"{locus}/body"), $"{locus}/body");
                actual = new TypeRef("Bool");
                break;
            default:
                throw ModulesExceptionFactory.Error("owner-parse", "UnsupportedProofOpcode", locus, new { opcode = expression.Op });
        }
        EnsureType(expression.Type, actual, locus);
        return actual;
    }

    private static void ValidateExactTypeClosure(
        ImmutableArray<TypeDecl> types,
        ImmutableArray<OwnerEntryContractV04> entries,
        ImmutableArray<OwnerModelV04> models,
        IReadOnlyDictionary<string, TypeDecl> typeById)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        void Visit(TypeRef type, string locus)
        {
            if (type.Kind is "I64" or "Bool" or "MathInt") return;
            if (type.Kind == "Seq" && type.Element is not null) { Visit(type.Element, locus); return; }
            if (type.Kind != "Record" || type.Name is null || !typeById.TryGetValue(type.Name, out var declaration))
                throw ModulesExceptionFactory.Error("owner-parse", "OwnerTypeClosureMissing", type.Name ?? locus);
            if (visiting.Contains(type.Name)) throw ModulesExceptionFactory.Error("owner-parse", "RecursiveType", type.Name);
            if (!reachable.Add(type.Name)) return;
            visiting.Add(type.Name);
            foreach (var field in declaration.Fields) Visit(field.Type, $"type/{declaration.Id}/field/{field.Id}");
            visiting.Remove(type.Name);
        }
        void VisitModel(OwnerExpression expression, string locus)
        {
            Visit(expression.Type, locus);
            if (expression.ElementType is not null) Visit(expression.ElementType, locus);
            if (expression.RecordType is not null) Visit(new TypeRef("Record", Name: expression.RecordType), locus);
            for (var index = 0; index < expression.Args.Length; index++) VisitModel(expression.Args[index], $"{locus}/arg/{index}");
            if (expression.FoldStep is not null)
            {
                foreach (var parameter in expression.FoldStep.Parameters) Visit(parameter.Type, $"{locus}/step/parameter/{parameter.Id}");
                VisitModel(expression.FoldStep.Body, $"{locus}/step/body");
            }
        }
        void VisitProof(ProofExpression expression, string locus)
        {
            Visit(expression.Type, locus);
            if (expression.ElementType is not null) Visit(expression.ElementType, locus);
            if (expression.RecordType is not null) Visit(new TypeRef("Record", Name: expression.RecordType), locus);
            for (var index = 0; index < expression.Args.Length; index++) VisitProof(expression.Args[index], $"{locus}/arg/{index}");
            if (expression.Sequence is not null) VisitProof(expression.Sequence, $"{locus}/sequence");
            if (expression.Body is not null) VisitProof(expression.Body, $"{locus}/body");
        }
        foreach (var entry in entries)
        {
            foreach (var parameter in entry.Parameters) Visit(parameter.Type, $"contract/{entry.Id}/parameter/{parameter.Id}");
            Visit(entry.ReturnType, $"contract/{entry.Id}/returnType");
            VisitProof(entry.Requires, $"contract/{entry.Id}/requires");
        }
        foreach (var model in models)
        {
            foreach (var parameter in model.Parameters) Visit(parameter.Type, $"model/{model.Id}/parameter/{parameter.Id}");
            Visit(model.ReturnType, $"model/{model.Id}/returnType");
            VisitModel(model.Body, $"model/{model.Id}/body");
        }
        var extra = types.Select(type => type.Id).Except(reachable, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (extra.Length != 0) throw ModulesExceptionFactory.Error("owner-parse", "OwnerTypeClosureExtraneous", extra[0], new { types = extra });
    }

    internal static void EnsureType(TypeRef expected, TypeRef actual, string locus)
    {
        if (!ModulesParser.TypesEquivalent(expected, actual))
            throw ModulesExceptionFactory.Error("owner-parse", "ContractTypeMismatch", locus, new { expected = expected.ToString(), actual = actual.ToString() });
    }

    internal static TypeRef RequireSequence(TypeRef type, string locus)
        => type.Kind == "Seq" && type.Element is not null && type.Capacity is not null
            ? type
            : throw ModulesExceptionFactory.Error("owner-parse", "ContractTypeMismatch", locus, new { expected = "Seq", actual = type.ToString() });
}

public static class OwnerModelEvaluatorV04
{
    public static ModuleValue Evaluate(OwnerExpression expression, IReadOnlyDictionary<string, ModuleValue> environment, IEnumerable<TypeDecl> types, string locus = "expression")
    {
        if (expression.Op != "fold") return OwnerContractEvaluator.Evaluate(expression, environment, types, locus);
        if (expression.FoldStep is null || expression.Args.Length < 2)
            throw ModulesExceptionFactory.Error("owner-evaluate", "InternalInvariantViolation", locus, new { reason = "InvalidOwnerFold" });
        var arguments = expression.Args.Select((argument, index) => OwnerContractEvaluator.Evaluate(argument, environment, types, $"{locus}/arg/{index}")).ToArray();
        var sequence = arguments[0] as ModuleSequence
            ?? throw ModulesExceptionFactory.Error("owner-evaluate", "RuntimeContractTypeMismatch", $"{locus}/arg/0");
        var accumulator = arguments[1];
        var captured = arguments.Skip(2).ToArray();
        for (var index = 0; index < sequence.Items.Length; index++)
        {
            var stepValues = new ModuleValue[3 + captured.Length];
            stepValues[0] = new ModuleI64(index);
            stepValues[1] = sequence.Items[index];
            stepValues[2] = accumulator;
            Array.Copy(captured, 0, stepValues, 3, captured.Length);
            var stepEnvironment = expression.FoldStep.Parameters.Select((parameter, parameterIndex) => KeyValuePair.Create(parameter.Id, stepValues[parameterIndex]))
                .ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            accumulator = OwnerContractEvaluator.Evaluate(expression.FoldStep.Body, stepEnvironment, types, $"{locus}/step/{index}");
        }
        return accumulator;
    }
}

public static class OwnerProofEvaluator
{
    public static ProofValue Evaluate(
        ProofExpression expression,
        IReadOnlyDictionary<string, ModuleValue> environment,
        IEnumerable<TypeDecl> types,
        int maxSteps,
        FoldProofContext? foldContext = null,
        string locus = "expression")
    {
        if (maxSteps is < 1 or > OwnerBundleLimitsV04.ProofEvaluationStepsHardMaximum)
            throw ModulesExceptionFactory.Error("owner-evaluate", "InvalidProofEvaluationLimit", locus, new { max = maxSteps });
        _ = types;
        var state = new ProofEvaluationState(environment, maxSteps, foldContext);
        return state.Evaluate(expression, ImmutableDictionary<string, long>.Empty, locus);
    }

    public static bool EvaluateBoolean(ProofExpression expression, IReadOnlyDictionary<string, ModuleValue> environment, IEnumerable<TypeDecl> types, int maxSteps, FoldProofContext? foldContext = null, string locus = "expression")
        => AsBool(Evaluate(expression, environment, types, maxSteps, foldContext, locus), locus);

    private sealed class ProofEvaluationState(
        IReadOnlyDictionary<string, ModuleValue> environment,
        int maxSteps,
        FoldProofContext? foldContext)
    {
        private int steps;

        public ProofValue Evaluate(ProofExpression expression, ImmutableDictionary<string, long> binders, string locus)
        {
            Consume(locus);
            try
            {
                return expression.Op switch
                {
                    "param" when expression.ReferenceId is { } id && environment.TryGetValue(id, out var value) => Wrap(value),
                    "param" => throw ModulesExceptionFactory.Error("owner-evaluate", "UnknownContractParameter", locus),
                    "proof.bound" when expression.BinderId is { } binder && binders.TryGetValue(binder, out var index) => new ProofI64(index),
                    "proof.bound" => throw ModulesExceptionFactory.Error("owner-evaluate", "UnknownProofBinder", locus),
                    "fold.prefixLength" when foldContext is not null => new ProofI64(foldContext.PrefixLength),
                    "fold.sequence" when foldContext is not null => new ProofModuleValue(foldContext.Sequence),
                    "fold.initialAccumulator" when foldContext is not null => Wrap(foldContext.InitialAccumulator),
                    "fold.accumulator" when foldContext is not null => Wrap(foldContext.Accumulator),
                    "fold.environment" when foldContext is not null && expression.Position is >= 0 && expression.Position < foldContext.Environment.Length => Wrap(foldContext.Environment[expression.Position.Value]),
                    "fold.prefixLength" or "fold.sequence" or "fold.initialAccumulator" or "fold.accumulator" or "fold.environment" => throw ModulesExceptionFactory.Error("owner-evaluate", "MissingFoldProofContext", locus),
                    "i64.const" => new ProofI64(long.Parse(expression.NumberValue!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
                    "math.const" => new ProofMathInt(BigInteger.Parse(expression.NumberValue!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
                    "bool.const" => new ProofBool(expression.BoolValue!.Value),
                    "eq" => new ProofBool(Equal(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), Evaluate(expression.Args[1], binders, $"{locus}/arg/1"))),
                    "i64.add" => new ProofI64(checked(AsI64(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), locus) + AsI64(Evaluate(expression.Args[1], binders, $"{locus}/arg/1"), locus))),
                    "i64.sub" => new ProofI64(checked(AsI64(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), locus) - AsI64(Evaluate(expression.Args[1], binders, $"{locus}/arg/1"), locus))),
                    "i64.le" => new ProofBool(AsI64(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), locus) <= AsI64(Evaluate(expression.Args[1], binders, $"{locus}/arg/1"), locus)),
                    "math.le" => new ProofBool(AsMath(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), locus) <= AsMath(Evaluate(expression.Args[1], binders, $"{locus}/arg/1"), locus)),
                    "math.from_i64" => new ProofMathInt(AsI64(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), locus)),
                    "math.add" => new ProofMathInt(AsMath(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), locus) + AsMath(Evaluate(expression.Args[1], binders, $"{locus}/arg/1"), locus)),
                    "math.sub" => new ProofMathInt(AsMath(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), locus) - AsMath(Evaluate(expression.Args[1], binders, $"{locus}/arg/1"), locus)),
                    "bool.not" => new ProofBool(!AsBool(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), locus)),
                    "bool.and" => StrictBoolean(expression, binders, locus, static (left, right) => left && right),
                    "bool.or" => StrictBoolean(expression, binders, locus, static (left, right) => left || right),
                    "if" => AsBool(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), locus)
                        ? Evaluate(expression.Args[1], binders, $"{locus}/arg/1")
                        : Evaluate(expression.Args[2], binders, $"{locus}/arg/2"),
                    "record.make" => MakeRecord(expression, binders, locus),
                    "record.get" => GetRecord(expression, binders, locus),
                    "seq.empty" => new ProofModuleValue(new ModuleSequence(expression.ElementType!, expression.Capacity!.Value, ImmutableArray<ModuleValue>.Empty)),
                    "seq.length" => new ProofI64(AsSequence(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), locus).Items.Length),
                    "seq.get" => GetSequence(expression, binders, locus),
                    "seq.append" => AppendSequence(expression, binders, locus),
                    "seq.sum_i64" => SumSequence(expression, binders, locus, prefix: null),
                    "seq.prefix_sum_i64" => PrefixSumSequence(expression, binders, locus),
                    "forall.sequence" => ForAll(expression, binders, locus),
                    _ => throw ModulesExceptionFactory.Error("owner-evaluate", "UnsupportedEvaluationOpcode", locus, new { expression.Op })
                };
            }
            catch (OverflowException)
            {
                throw ModulesExceptionFactory.Error("owner-evaluate", "ProofExpressionUndefined", locus, new { expression.Op });
            }
        }

        private ProofValue StrictBoolean(ProofExpression expression, ImmutableDictionary<string, long> binders, string locus, Func<bool, bool, bool> operation)
        {
            var left = AsBool(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), locus);
            var right = AsBool(Evaluate(expression.Args[1], binders, $"{locus}/arg/1"), locus);
            return new ProofBool(operation(left, right));
        }

        private ProofValue ForAll(ProofExpression expression, ImmutableDictionary<string, long> binders, string locus)
        {
            var sequence = AsSequence(Evaluate(expression.Sequence!, binders, $"{locus}/sequence"), locus);
            var result = true;
            for (var index = 0; index < sequence.Items.Length; index++)
            {
                Consume($"{locus}/iteration/{index}");
                result &= AsBool(Evaluate(expression.Body!, binders.Add(expression.BinderId!, index), $"{locus}/body"), locus);
            }
            return new ProofBool(result);
        }

        private ProofValue PrefixSumSequence(ProofExpression expression, ImmutableDictionary<string, long> binders, string locus)
        {
            var sequence = AsSequence(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), locus);
            var prefix = AsI64(Evaluate(expression.Args[1], binders, $"{locus}/arg/1"), locus);
            return SumSequenceValue(sequence, prefix, locus);
        }

        private ProofValue SumSequence(ProofExpression expression, ImmutableDictionary<string, long> binders, string locus, long? prefix)
        {
            var sequence = AsSequence(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), locus);
            var length = prefix ?? sequence.Items.Length;
            return SumSequenceValue(sequence, length, locus);
        }

        private ProofValue SumSequenceValue(ModuleSequence sequence, long length, string locus)
        {
            if (length < 0 || length > sequence.Items.Length)
                throw ModulesExceptionFactory.Error("owner-evaluate", "ProofPrefixLengthOutOfRange", locus, new { prefixLength = length, sequenceLength = sequence.Items.Length });
            var result = BigInteger.Zero;
            for (var index = 0; index < length; index++)
            {
                Consume($"{locus}/item/{index}");
                result += ((ModuleI64)sequence.Items[index]).Value;
            }
            return new ProofMathInt(result);
        }

        private ProofValue MakeRecord(ProofExpression expression, ImmutableDictionary<string, long> binders, string locus)
            => new ProofModuleValue(new ModuleRecord(expression.RecordType!, expression.FieldIds.Select((field, index) => KeyValuePair.Create(field, Unwrap(Evaluate(expression.Args[index], binders, $"{locus}/arg/{index}"))))));

        private ProofValue GetRecord(ProofExpression expression, ImmutableDictionary<string, long> binders, string locus)
        {
            var record = Unwrap(Evaluate(expression.Args[0], binders, $"{locus}/arg/0")) as ModuleRecord
                ?? throw ModulesExceptionFactory.Error("owner-evaluate", "RuntimeContractTypeMismatch", locus);
            return Wrap(record.Fields[expression.ReferenceId!]);
        }

        private ProofValue GetSequence(ProofExpression expression, ImmutableDictionary<string, long> binders, string locus)
        {
            var sequence = AsSequence(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), locus);
            var index = AsI64(Evaluate(expression.Args[1], binders, $"{locus}/arg/1"), locus);
            if (index < 0 || index >= sequence.Items.Length) throw ModulesExceptionFactory.Error("owner-evaluate", "SequenceIndexOutOfRange", locus, new { index, length = sequence.Items.Length });
            return Wrap(sequence.Items[(int)index]);
        }

        private ProofValue AppendSequence(ProofExpression expression, ImmutableDictionary<string, long> binders, string locus)
        {
            var sequence = AsSequence(Evaluate(expression.Args[0], binders, $"{locus}/arg/0"), locus);
            var item = Unwrap(Evaluate(expression.Args[1], binders, $"{locus}/arg/1"));
            if (sequence.Items.Length >= sequence.Capacity) throw ModulesExceptionFactory.Error("owner-evaluate", "SequenceCapacityExceeded", locus);
            return new ProofModuleValue(sequence with { Items = sequence.Items.Add(item) });
        }

        private void Consume(string locus)
        {
            if (steps >= maxSteps) throw ModulesExceptionFactory.Error("owner-evaluate", "ProofEvaluationStepLimitExceeded", locus, new { consumed = steps, max = maxSteps });
            steps++;
        }
    }

    private static ProofValue Wrap(ModuleValue value) => value switch
    {
        ModuleI64 integer => new ProofI64(integer.Value),
        ModuleBool boolean => new ProofBool(boolean.Value),
        _ => new ProofModuleValue(value)
    };

    private static ModuleValue Unwrap(ProofValue value) => value switch
    {
        ProofI64 integer => new ModuleI64(integer.Value),
        ProofBool boolean => new ModuleBool(boolean.Value),
        ProofModuleValue module => module.Value,
        _ => throw ModulesExceptionFactory.Error("owner-evaluate", "RuntimeContractTypeMismatch")
    };

    private static long AsI64(ProofValue value, string locus) => value is ProofI64 integer ? integer.Value : throw ModulesExceptionFactory.Error("owner-evaluate", "RuntimeContractTypeMismatch", locus);
    private static bool AsBool(ProofValue value, string locus) => value is ProofBool boolean ? boolean.Value : throw ModulesExceptionFactory.Error("owner-evaluate", "RuntimeContractTypeMismatch", locus);
    private static BigInteger AsMath(ProofValue value, string locus) => value is ProofMathInt integer ? integer.Value : throw ModulesExceptionFactory.Error("owner-evaluate", "RuntimeContractTypeMismatch", locus);
    private static ModuleSequence AsSequence(ProofValue value, string locus) => Unwrap(value) as ModuleSequence ?? throw ModulesExceptionFactory.Error("owner-evaluate", "RuntimeContractTypeMismatch", locus);
    private static bool Equal(ProofValue left, ProofValue right) => (left, right) switch
    {
        (ProofI64 a, ProofI64 b) => a.Value == b.Value,
        (ProofBool a, ProofBool b) => a.Value == b.Value,
        (ProofMathInt a, ProofMathInt b) => a.Value == b.Value,
        (ProofModuleValue a, ProofModuleValue b) => OwnerContractEvaluator.StructuralEquals(a.Value, b.Value),
        _ => false
    };
}

public static class OwnerContractBinderV04
{
    public static OwnerContractBindingV04 Bind(ModuleIr module, OwnerBundleV04 bundle)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(bundle);
        if (module.Imports.Length != 0) throw ModulesExceptionFactory.Error("owner-bind", "UnsupportedOwnerImports");
        var exported = module.Exports.ToImmutableHashSet(StringComparer.Ordinal);
        var helpers = module.Functions.Where(function => !exported.Contains(function.Id)).Select(function => function.Id).Order(StringComparer.Ordinal).ToArray();
        if (helpers.Length != 0) throw ModulesExceptionFactory.Error("owner-bind", "UnsupportedOwnerHelpers", helpers[0], new { helpers });

        var moduleTypes = module.Types.ToImmutableDictionary(type => type.Id, StringComparer.Ordinal);
        foreach (var ownerType in bundle.Types)
            if (!moduleTypes.TryGetValue(ownerType.Id, out var moduleType) || !TypeDeclarationEquals(ownerType, moduleType))
                throw ModulesExceptionFactory.Error("owner-bind", "OwnerTypeClosureMismatch", ownerType.Id);

        var contractByFunction = bundle.EntryContracts.ToImmutableDictionary(entry => entry.FunctionRef, StringComparer.Ordinal);
        var missing = exported.Except(contractByFunction.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var extra = contractByFunction.Keys.Except(exported, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length != 0 || extra.Length != 0) throw ModulesExceptionFactory.Error("owner-bind", "OwnerEntryMismatch", details: new { missing, extra });
        var modelById = bundle.Models.ToImmutableDictionary(model => model.Id, StringComparer.Ordinal);
        var entries = ImmutableArray.CreateBuilder<BoundOwnerEntryV04>();
        foreach (var function in module.Functions.OrderBy(function => function.Id, StringComparer.Ordinal))
        {
            var contract = contractByFunction[function.Id];
            if (function.ContractRef != contract.Id) throw ModulesExceptionFactory.Error("owner-bind", "OwnerContractRefMismatch", function.Id);
            OwnerContractSemantics.EnsureSignature(function.Parameters, function.ReturnType, contract.Parameters, contract.ReturnType, $"function/{function.Id}");
            var model = modelById[contract.ModelRef];
            var candidateFold = function.Instructions.SingleOrDefault(instruction => instruction.Op == "fold");
            if ((candidateFold is null) != (model.Body.Op != "fold"))
                throw ModulesExceptionFactory.Error("owner-bind", "OwnerFoldPairingMismatch", function.Id);
            if (candidateFold is not null) ValidateFoldPair(function, candidateFold, model, bundle.Limits);
            var witnesses = contract.Witnesses.OrderBy(witness => witness.Id, StringComparer.Ordinal).Select(witness =>
            {
                var environment = witness.Arguments.ToImmutableDictionary(argument => argument.ParameterId, argument => argument.Value, StringComparer.Ordinal);
                if (!OwnerProofEvaluator.EvaluateBoolean(contract.Requires, environment, bundle.Types, bundle.Limits.MaxProofEvaluationSteps, null, $"contract/{contract.Id}/witness/{witness.Id}/requires"))
                    throw ModulesExceptionFactory.Error("owner-evaluate", "RequiresWitnessRejected", witness.Id);
                return new EvaluatedOwnerWitnessV04(witness.Id, witness.Arguments, OwnerModelEvaluatorV04.Evaluate(model.Body, environment, bundle.Types, $"contract/{contract.Id}/witness/{witness.Id}/model"));
            }).ToImmutableArray();
            entries.Add(new BoundOwnerEntryV04(function, contract, model, candidateFold, witnesses));
        }
        return new OwnerContractBindingV04(module, bundle, entries.ToImmutable());
    }

    private static void ValidateFoldPair(FunctionIr function, IrInstruction candidate, OwnerModelV04 model, OwnerBundleLimitsV04 limits)
    {
        if (candidate.Fold is null || model.Body.FoldStep is null) throw ModulesExceptionFactory.Error("owner-bind", "OwnerFoldPairingMismatch", function.Id);
        var region = new RegionIr(function.Parameters, function.Instructions, function.ResultIndex);
        var candidateTypes = candidate.OperandIndices.Select(index => ResolveType(index, region, function.Id)).ToArray();
        if (candidateTypes.Length != model.Body.Args.Length) throw ModulesExceptionFactory.Error("owner-bind", "OwnerFoldInputMismatch", function.Id);
        for (var index = 0; index < candidateTypes.Length; index++)
            if (!ModulesParser.TypesEquivalent(candidateTypes[index], model.Body.Args[index].Type)) throw ModulesExceptionFactory.Error("owner-bind", "OwnerFoldInputMismatch", function.Id, new { position = index });
        var invariantCost = OwnerBundleV04Parser.ProofWorstCaseCost(candidate.Fold.Invariant);
        if (invariantCost > limits.MaxProofEvaluationSteps)
            throw ModulesExceptionFactory.Error("owner-bind", "ProofWorstCaseCostExceeded", function.Id, new
            {
                actual = invariantCost > OwnerBundleLimitsV04.ProofEvaluationStepsHardMaximum ? $">{OwnerBundleLimitsV04.ProofEvaluationStepsHardMaximum}" : invariantCost.ToString(CultureInfo.InvariantCulture),
                max = limits.MaxProofEvaluationSteps.ToString(CultureInfo.InvariantCulture)
            });
    }

    private static TypeRef ResolveType(int index, RegionIr region, string locus)
    {
        if (index < 0) return region.Parameters[-1 - index].Type;
        if (index < region.Instructions.Length) return region.Instructions[index].Type;
        throw ModulesExceptionFactory.Error("owner-bind", "InternalInvariantViolation", locus);
    }

    private static bool TypeDeclarationEquals(TypeDecl left, TypeDecl right)
        => left.Id == right.Id && left.Fields.Length == right.Fields.Length
            && left.Fields.OrderBy(field => field.Id, StringComparer.Ordinal).Zip(right.Fields.OrderBy(field => field.Id, StringComparer.Ordinal))
                .All(pair => pair.First.Id == pair.Second.Id && ModulesParser.TypesEquivalent(pair.First.Type, pair.Second.Type));
}

public static class OwnerContractReplayV04
{
    public static OwnerWitnessReplayResult Replay(OwnerContractBindingV04 binding, ModuleEvaluationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        OwnerContractBindingV04 trusted;
        try { trusted = OwnerContractBinderV04.Bind(binding.Module, binding.Bundle); }
        catch (ModuleException exception) { return new OwnerWitnessReplayResult("ToolError", 0, null, exception.Code); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or NullReferenceException)
        {
            return new OwnerWitnessReplayResult("ToolError", 0, null, "InvalidOwnerContractBinding");
        }
        var checkedWitnesses = 0;
        foreach (var entry in trusted.Entries.OrderBy(entry => entry.Contract.Id, StringComparer.Ordinal))
            foreach (var witness in entry.Witnesses.OrderBy(witness => witness.Id, StringComparer.Ordinal))
            {
                checkedWitnesses++;
                var byId = witness.Arguments.ToImmutableDictionary(argument => argument.ParameterId, argument => argument.Value, StringComparer.Ordinal);
                var arguments = entry.Function.Parameters.Select(parameter => byId[parameter.Id]).ToArray();
                try
                {
                    var actual = ModulesReferenceEvaluator.Invoke(trusted.Module, entry.Function.Id, arguments, limits).Value;
                    if (!OwnerContractEvaluator.StructuralEquals(witness.ModelResult, actual))
                        return new OwnerWitnessReplayResult("Counterexample", checkedWitnesses, new OwnerWitnessCounterexample(entry.Contract.Id, witness.Id, witness.ModelResult, actual));
                }
                catch (ModuleException exception)
                {
                    return new OwnerWitnessReplayResult(exception.Code == "EvaluationStepLimitExceeded" ? "Timeout" : "CandidateError", checkedWitnesses, null, exception.Code);
                }
            }
        return new OwnerWitnessReplayResult("Pass", checkedWitnesses, null);
    }
}
