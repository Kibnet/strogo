using System.Collections.Immutable;

namespace Strogo.Modules;

internal static class OwnerContractSemantics
{
    internal static void Validate(
        ImmutableArray<TypeDecl> types,
        ImmutableArray<OwnerEntryContract> entries,
        ImmutableArray<OwnerModel> models)
    {
        var typeById = types.ToImmutableDictionary(type => type.Id, StringComparer.Ordinal);
        var modelById = models.ToImmutableDictionary(model => model.Id, StringComparer.Ordinal);
        ValidateExactTypeClosure(types, entries, models, typeById);

        foreach (var model in models)
        {
            var parameterTypes = model.Parameters.ToImmutableDictionary(parameter => parameter.Id, parameter => parameter.Type, StringComparer.Ordinal);
            var actual = ValidateExpression(model.Body, parameterTypes, typeById, $"model/{model.Id}/body");
            EnsureSameType(model.ReturnType, actual, $"model/{model.Id}/body");
        }

        var referencedModels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var parameterTypes = entry.Parameters.ToImmutableDictionary(parameter => parameter.Id, parameter => parameter.Type, StringComparer.Ordinal);
            EnsureKind("Bool", ValidateExpression(entry.Requires, parameterTypes, typeById, $"contract/{entry.Id}/requires"), $"contract/{entry.Id}/requires");
            if (!modelById.TryGetValue(entry.ModelRef, out var model))
                throw ModulesExceptionFactory.Error("owner-parse", "UnknownOwnerModel", entry.Id, new { entry.ModelRef });
            if (!referencedModels.Add(entry.ModelRef))
                throw ModulesExceptionFactory.Error("owner-parse", "OwnerModelReuseNotSupported", entry.Id, new { entry.ModelRef });
            EnsureSignature(entry.Parameters, entry.ReturnType, model.Parameters, model.ReturnType, $"contract/{entry.Id}");

            foreach (var witness in entry.Witnesses)
            {
                var environment = witness.Arguments.ToImmutableDictionary(argument => argument.ParameterId, argument => argument.Value, StringComparer.Ordinal);
                if (!OwnerContractEvaluator.EvaluateBoolean(entry.Requires, environment, types, $"contract/{entry.Id}/witness/{witness.Id}/requires"))
                    throw ModulesExceptionFactory.Error("owner-evaluate", "RequiresWitnessRejected", $"contract/{entry.Id}/witness/{witness.Id}");
                try
                {
                    _ = OwnerContractEvaluator.Evaluate(model.Body, environment, types, $"contract/{entry.Id}/witness/{witness.Id}/model");
                }
                catch (ModuleException exception) when (exception.Code is "SequenceIndexOutOfRange" or "SequenceCapacityExceeded" or "ModelUndefinedAtWitness")
                {
                    throw ModulesExceptionFactory.Error("owner-evaluate", "ModelUndefinedAtWitness", $"contract/{entry.Id}/witness/{witness.Id}/model");
                }
            }
        }

        var unusedModels = modelById.Keys.Except(referencedModels, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (unusedModels.Length != 0)
            throw ModulesExceptionFactory.Error("owner-parse", "UnusedOwnerModel", unusedModels[0], new { models = unusedModels });
    }

    private static void ValidateExactTypeClosure(
        ImmutableArray<TypeDecl> types,
        ImmutableArray<OwnerEntryContract> entries,
        ImmutableArray<OwnerModel> models,
        IReadOnlyDictionary<string, TypeDecl> typeById)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        void Visit(TypeRef type, string locus)
        {
            if (type.Kind is "I64" or "Bool") return;
            if (type.Kind == "Seq" && type.Element is not null)
            {
                Visit(type.Element, locus);
                return;
            }
            if (type.Kind != "Record" || type.Name is null || !typeById.TryGetValue(type.Name, out var declaration))
                throw ModulesExceptionFactory.Error("owner-parse", "OwnerTypeClosureMissing", type.Name ?? locus);
            if (visiting.Contains(type.Name))
                throw ModulesExceptionFactory.Error("owner-parse", "RecursiveType", type.Name);
            if (!reachable.Add(type.Name)) return;
            visiting.Add(type.Name);
            foreach (var field in declaration.Fields.OrderBy(field => field.Id, StringComparer.Ordinal))
                Visit(field.Type, $"type/{declaration.Id}/field/{field.Id}");
            visiting.Remove(type.Name);
        }

        void VisitExpression(OwnerExpression expression, string locus)
        {
            Visit(expression.Type, locus);
            if (expression.ElementType is not null) Visit(expression.ElementType, locus);
            if (expression.RecordType is not null) Visit(new TypeRef("Record", Name: expression.RecordType), locus);
            for (var index = 0; index < expression.Args.Length; index++)
                VisitExpression(expression.Args[index], $"{locus}/arg/{index}");
        }

        foreach (var entry in entries.OrderBy(entry => entry.Id, StringComparer.Ordinal))
        {
            foreach (var parameter in entry.Parameters) Visit(parameter.Type, $"contract/{entry.Id}/parameter/{parameter.Id}");
            Visit(entry.ReturnType, $"contract/{entry.Id}/returnType");
            VisitExpression(entry.Requires, $"contract/{entry.Id}/requires");
            foreach (var witness in entry.Witnesses.OrderBy(witness => witness.Id, StringComparer.Ordinal))
                foreach (var argument in witness.Arguments.OrderBy(argument => argument.ParameterId, StringComparer.Ordinal))
                    Visit(entry.Parameters.Single(parameter => parameter.Id == argument.ParameterId).Type, $"contract/{entry.Id}/witness/{witness.Id}/{argument.ParameterId}");
        }
        foreach (var model in models.OrderBy(model => model.Id, StringComparer.Ordinal))
        {
            foreach (var parameter in model.Parameters) Visit(parameter.Type, $"model/{model.Id}/parameter/{parameter.Id}");
            Visit(model.ReturnType, $"model/{model.Id}/returnType");
            VisitExpression(model.Body, $"model/{model.Id}/body");
        }

        var extraneous = types.Select(type => type.Id).Except(reachable, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (extraneous.Length != 0)
            throw ModulesExceptionFactory.Error("owner-parse", "OwnerTypeClosureExtraneous", extraneous[0], new { types = extraneous });
    }

    private static TypeRef ValidateExpression(
        OwnerExpression expression,
        IReadOnlyDictionary<string, TypeRef> parameters,
        IReadOnlyDictionary<string, TypeDecl> types,
        string locus)
    {
        TypeRef actual;
        switch (expression.Op)
        {
            case "param":
                if (expression.ReferenceId is null || !parameters.TryGetValue(expression.ReferenceId, out actual!))
                    throw ModulesExceptionFactory.Error("owner-parse", "UnknownContractParameter", locus, new { parameterId = expression.ReferenceId });
                break;
            case "i64.const": actual = new TypeRef("I64"); break;
            case "bool.const": actual = new TypeRef("Bool"); break;
            case "i64.add" or "i64.sub":
                RequireArity(expression, 2, locus);
                RequireArguments(expression, parameters, types, locus, new TypeRef("I64"), new TypeRef("I64"));
                actual = new TypeRef("I64");
                break;
            case "i64.le":
                RequireArity(expression, 2, locus);
                RequireArguments(expression, parameters, types, locus, new TypeRef("I64"), new TypeRef("I64"));
                actual = new TypeRef("Bool");
                break;
            case "eq":
                RequireArity(expression, 2, locus);
                var left = ValidateExpression(expression.Args[0], parameters, types, $"{locus}/arg/0");
                var right = ValidateExpression(expression.Args[1], parameters, types, $"{locus}/arg/1");
                EnsureSameType(left, right, locus);
                actual = new TypeRef("Bool");
                break;
            case "bool.not":
                RequireArity(expression, 1, locus);
                RequireArguments(expression, parameters, types, locus, new TypeRef("Bool"));
                actual = new TypeRef("Bool");
                break;
            case "bool.and" or "bool.or":
                RequireArity(expression, 2, locus);
                RequireArguments(expression, parameters, types, locus, new TypeRef("Bool"), new TypeRef("Bool"));
                actual = new TypeRef("Bool");
                break;
            case "if":
                RequireArity(expression, 3, locus);
                EnsureKind("Bool", ValidateExpression(expression.Args[0], parameters, types, $"{locus}/arg/0"), $"{locus}/arg/0");
                var thenType = ValidateExpression(expression.Args[1], parameters, types, $"{locus}/arg/1");
                var elseType = ValidateExpression(expression.Args[2], parameters, types, $"{locus}/arg/2");
                EnsureSameType(thenType, elseType, locus);
                actual = thenType;
                break;
            case "record.make":
                if (expression.RecordType is null || !types.TryGetValue(expression.RecordType, out var declaration))
                    throw ModulesExceptionFactory.Error("owner-parse", "OwnerTypeClosureMissing", expression.RecordType ?? locus);
                var expectedIds = declaration.Fields.Select(field => field.Id).Order(StringComparer.Ordinal).ToArray();
                var actualIds = expression.FieldIds.Order(StringComparer.Ordinal).ToArray();
                if (expression.Args.Length != expression.FieldIds.Length || !actualIds.SequenceEqual(expectedIds, StringComparer.Ordinal))
                    throw ModulesExceptionFactory.Error("owner-parse", "RecordFieldMismatch", locus, new { expected = expectedIds, actual = actualIds });
                foreach (var pair in expression.FieldIds.Select((fieldId, index) => (fieldId, index)).OrderBy(pair => pair.fieldId, StringComparer.Ordinal))
                {
                    var argumentType = ValidateExpression(expression.Args[pair.index], parameters, types, $"{locus}/arg/{pair.index}");
                    EnsureSameType(declaration.Fields.Single(field => field.Id == pair.fieldId).Type, argumentType, $"{locus}/arg/{pair.index}");
                }
                actual = new TypeRef("Record", Name: expression.RecordType);
                break;
            case "record.get":
                RequireArity(expression, 1, locus);
                var recordType = ValidateExpression(expression.Args[0], parameters, types, $"{locus}/arg/0");
                if (recordType.Kind != "Record" || recordType.Name is null || !types.TryGetValue(recordType.Name, out var recordDecl))
                    throw ModulesExceptionFactory.Error("owner-parse", "ContractTypeMismatch", locus);
                var field = recordDecl.Fields.SingleOrDefault(field => field.Id == expression.ReferenceId);
                if (field is null)
                    throw ModulesExceptionFactory.Error("owner-parse", "UnknownRecordField", locus, new { fieldId = expression.ReferenceId });
                actual = field.Type;
                break;
            case "seq.empty":
                RequireArity(expression, 0, locus);
                if (expression.ElementType is null || expression.Capacity is null)
                    throw ModulesExceptionFactory.Error("owner-parse", "SchemaInvalid", locus);
                actual = new TypeRef("Seq", Element: expression.ElementType, Capacity: expression.Capacity);
                break;
            case "seq.length":
                RequireArity(expression, 1, locus);
                EnsureSequence(ValidateExpression(expression.Args[0], parameters, types, $"{locus}/arg/0"), $"{locus}/arg/0");
                actual = new TypeRef("I64");
                break;
            case "seq.get":
                RequireArity(expression, 2, locus);
                var sequenceType = EnsureSequence(ValidateExpression(expression.Args[0], parameters, types, $"{locus}/arg/0"), $"{locus}/arg/0");
                EnsureKind("I64", ValidateExpression(expression.Args[1], parameters, types, $"{locus}/arg/1"), $"{locus}/arg/1");
                actual = sequenceType.Element!;
                break;
            case "seq.append":
                RequireArity(expression, 2, locus);
                var appendType = EnsureSequence(ValidateExpression(expression.Args[0], parameters, types, $"{locus}/arg/0"), $"{locus}/arg/0");
                EnsureSameType(appendType.Element!, ValidateExpression(expression.Args[1], parameters, types, $"{locus}/arg/1"), $"{locus}/arg/1");
                actual = appendType;
                break;
            default:
                throw ModulesExceptionFactory.Error("owner-parse", "UnsupportedContractOpcode", locus, new { expression.Op });
        }
        EnsureSameType(expression.Type, actual, locus);
        return actual;
    }

    private static TypeRef EnsureSequence(TypeRef type, string locus)
        => type.Kind == "Seq" && type.Element is not null && type.Capacity is not null
            ? type
            : throw ModulesExceptionFactory.Error("owner-parse", "ContractTypeMismatch", locus, new { expected = "Seq", actual = type.ToString() });

    private static void RequireArguments(OwnerExpression expression, IReadOnlyDictionary<string, TypeRef> parameters, IReadOnlyDictionary<string, TypeDecl> types, string locus, params TypeRef[] expected)
    {
        for (var index = 0; index < expected.Length; index++)
            EnsureSameType(expected[index], ValidateExpression(expression.Args[index], parameters, types, $"{locus}/arg/{index}"), $"{locus}/arg/{index}");
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
        if (expectedParameters.Length != actualParameters.Length
            || expectedParameters.Where((parameter, index) => parameter.Id != actualParameters[index].Id || !ModulesParser.TypesEquivalent(parameter.Type, actualParameters[index].Type)).Any()
            || !ModulesParser.TypesEquivalent(expectedReturn, actualReturn))
            throw ModulesExceptionFactory.Error("owner-bind", "OwnerSignatureMismatch", locus);
    }

    internal static void EnsureSameType(TypeRef expected, TypeRef actual, string locus)
    {
        if (!ModulesParser.TypesEquivalent(expected, actual))
            throw ModulesExceptionFactory.Error("owner-parse", "ContractTypeMismatch", locus, new { expected = expected.ToString(), actual = actual.ToString() });
    }

    private static void EnsureKind(string expected, TypeRef actual, string locus)
    {
        if (expected != actual.Kind)
            throw ModulesExceptionFactory.Error("owner-parse", "ContractTypeMismatch", locus, new { expected, actual = actual.Kind });
    }
}

public static class OwnerContractEvaluator
{
    public static ModuleValue Evaluate(
        OwnerExpression expression,
        IReadOnlyDictionary<string, ModuleValue> environment,
        IEnumerable<TypeDecl> types,
        string locus = "expression")
    {
        var typeById = types.ToImmutableDictionary(type => type.Id, StringComparer.Ordinal);
        try
        {
            var result = expression.Op switch
            {
                "param" when expression.ReferenceId is { } parameterId && environment.TryGetValue(parameterId, out var value) => value,
                "param" => throw ModulesExceptionFactory.Error("owner-evaluate", "UnknownContractParameter", locus, new { parameterId = expression.ReferenceId }),
                "i64.const" => new ModuleI64(expression.I64Value!.Value),
                "bool.const" => new ModuleBool(expression.BoolValue!.Value),
                "i64.add" => new ModuleI64(checked(AsI64(Evaluate(expression.Args[0], environment, typeById.Values, $"{locus}/arg/0"), $"{locus}/arg/0") + AsI64(Evaluate(expression.Args[1], environment, typeById.Values, $"{locus}/arg/1"), $"{locus}/arg/1"))),
                "i64.sub" => new ModuleI64(checked(AsI64(Evaluate(expression.Args[0], environment, typeById.Values, $"{locus}/arg/0"), $"{locus}/arg/0") - AsI64(Evaluate(expression.Args[1], environment, typeById.Values, $"{locus}/arg/1"), $"{locus}/arg/1"))),
                "i64.le" => new ModuleBool(AsI64(Evaluate(expression.Args[0], environment, typeById.Values, $"{locus}/arg/0"), $"{locus}/arg/0") <= AsI64(Evaluate(expression.Args[1], environment, typeById.Values, $"{locus}/arg/1"), $"{locus}/arg/1")),
                "eq" => new ModuleBool(Equal(Evaluate(expression.Args[0], environment, typeById.Values, $"{locus}/arg/0"), Evaluate(expression.Args[1], environment, typeById.Values, $"{locus}/arg/1"))),
                "bool.not" => new ModuleBool(!AsBool(Evaluate(expression.Args[0], environment, typeById.Values, $"{locus}/arg/0"), $"{locus}/arg/0")),
                "bool.and" => EvaluateStrictBoolean(expression, environment, typeById.Values, locus, static (left, right) => left && right),
                "bool.or" => EvaluateStrictBoolean(expression, environment, typeById.Values, locus, static (left, right) => left || right),
                "if" => AsBool(Evaluate(expression.Args[0], environment, typeById.Values, $"{locus}/arg/0"), $"{locus}/arg/0")
                    ? Evaluate(expression.Args[1], environment, typeById.Values, $"{locus}/arg/1")
                    : Evaluate(expression.Args[2], environment, typeById.Values, $"{locus}/arg/2"),
                "record.make" => MakeRecord(expression, environment, typeById, locus),
                "record.get" => GetRecord(expression, environment, typeById, locus),
                "seq.empty" => new ModuleSequence(expression.ElementType!, expression.Capacity!.Value, ImmutableArray<ModuleValue>.Empty),
                "seq.length" => new ModuleI64(AsSequence(Evaluate(expression.Args[0], environment, typeById.Values, $"{locus}/arg/0"), $"{locus}/arg/0").Items.Length),
                "seq.get" => GetSequence(expression, environment, typeById, locus),
                "seq.append" => AppendSequence(expression, environment, typeById, locus),
                _ => throw ModulesExceptionFactory.Error("owner-evaluate", "UnsupportedEvaluationOpcode", locus, new { expression.Op })
            };
            EnsureValueType(result, expression.Type, typeById, locus);
            return result;
        }
        catch (OverflowException)
        {
            throw ModulesExceptionFactory.Error("owner-evaluate", "ModelUndefinedAtWitness", locus);
        }
    }

    public static bool EvaluateBoolean(OwnerExpression expression, IReadOnlyDictionary<string, ModuleValue> environment, IEnumerable<TypeDecl> types, string locus = "expression")
        => AsBool(Evaluate(expression, environment, types, locus), locus);

    public static bool StructuralEquals(ModuleValue left, ModuleValue right) => Equal(left, right);

    internal static void EnsureValueType(ModuleValue value, TypeRef expected, IReadOnlyDictionary<string, TypeDecl> types, string locus)
    {
        switch (expected.Kind)
        {
            case "I64" when value is ModuleI64:
            case "Bool" when value is ModuleBool:
                return;
            case "Seq" when value is ModuleSequence sequence && expected.Element is not null && expected.Capacity is not null:
                if (!ModulesParser.TypesEquivalent(sequence.ElementType, expected.Element) || sequence.Capacity != expected.Capacity || sequence.Items.Length > sequence.Capacity)
                    break;
                for (var index = 0; index < sequence.Items.Length; index++) EnsureValueType(sequence.Items[index], expected.Element, types, $"{locus}/item/{index}");
                return;
            case "Record" when value is ModuleRecord record && expected.Name is not null && record.RecordTypeId == expected.Name && types.TryGetValue(expected.Name, out var declaration):
                var expectedFields = declaration.Fields.Select(field => field.Id).Order(StringComparer.Ordinal).ToArray();
                if (!record.Fields.Keys.SequenceEqual(expectedFields, StringComparer.Ordinal)) break;
                foreach (var field in declaration.Fields) EnsureValueType(record.Fields[field.Id], field.Type, types, $"{locus}/field/{field.Id}");
                return;
        }
        throw ModulesExceptionFactory.Error("owner-evaluate", "RuntimeContractTypeMismatch", locus, new { expected = expected.ToString(), actual = value.GetType().Name });
    }

    private static ModuleValue MakeRecord(OwnerExpression expression, IReadOnlyDictionary<string, ModuleValue> environment, IReadOnlyDictionary<string, TypeDecl> types, string locus)
        => new ModuleRecord(expression.RecordType!, expression.FieldIds.Select((fieldId, index) =>
            KeyValuePair.Create(fieldId, Evaluate(expression.Args[index], environment, types.Values, $"{locus}/arg/{index}"))));

    private static ModuleValue GetRecord(OwnerExpression expression, IReadOnlyDictionary<string, ModuleValue> environment, IReadOnlyDictionary<string, TypeDecl> types, string locus)
    {
        var record = Evaluate(expression.Args[0], environment, types.Values, $"{locus}/arg/0") as ModuleRecord
            ?? throw ModulesExceptionFactory.Error("owner-evaluate", "RuntimeContractTypeMismatch", locus);
        return record.Fields.TryGetValue(expression.ReferenceId!, out var value)
            ? value
            : throw ModulesExceptionFactory.Error("owner-evaluate", "UnknownRecordField", locus, new { fieldId = expression.ReferenceId });
    }

    private static ModuleValue GetSequence(OwnerExpression expression, IReadOnlyDictionary<string, ModuleValue> environment, IReadOnlyDictionary<string, TypeDecl> types, string locus)
    {
        var sequence = AsSequence(Evaluate(expression.Args[0], environment, types.Values, $"{locus}/arg/0"), $"{locus}/arg/0");
        var index = AsI64(Evaluate(expression.Args[1], environment, types.Values, $"{locus}/arg/1"), $"{locus}/arg/1");
        if (index < 0 || index >= sequence.Items.Length)
            throw ModulesExceptionFactory.Error("owner-evaluate", "SequenceIndexOutOfRange", locus, new { index, length = sequence.Items.Length });
        return sequence.Items[(int)index];
    }

    private static ModuleValue AppendSequence(OwnerExpression expression, IReadOnlyDictionary<string, ModuleValue> environment, IReadOnlyDictionary<string, TypeDecl> types, string locus)
    {
        var sequence = AsSequence(Evaluate(expression.Args[0], environment, types.Values, $"{locus}/arg/0"), $"{locus}/arg/0");
        var item = Evaluate(expression.Args[1], environment, types.Values, $"{locus}/arg/1");
        if (sequence.Items.Length >= sequence.Capacity)
            throw ModulesExceptionFactory.Error("owner-evaluate", "SequenceCapacityExceeded", locus, new { length = sequence.Items.Length, capacity = sequence.Capacity });
        return sequence with { Items = sequence.Items.Add(item) };
    }

    private static ModuleBool EvaluateStrictBoolean(OwnerExpression expression, IReadOnlyDictionary<string, ModuleValue> environment, IEnumerable<TypeDecl> types, string locus, Func<bool, bool, bool> operation)
    {
        var left = AsBool(Evaluate(expression.Args[0], environment, types, $"{locus}/arg/0"), $"{locus}/arg/0");
        var right = AsBool(Evaluate(expression.Args[1], environment, types, $"{locus}/arg/1"), $"{locus}/arg/1");
        return new ModuleBool(operation(left, right));
    }

    private static bool Equal(ModuleValue left, ModuleValue right) => (left, right) switch
    {
        (ModuleI64 a, ModuleI64 b) => a.Value == b.Value,
        (ModuleBool a, ModuleBool b) => a.Value == b.Value,
        (ModuleSequence a, ModuleSequence b) => a.Capacity == b.Capacity
            && ModulesParser.TypesEquivalent(a.ElementType, b.ElementType)
            && a.Items.Length == b.Items.Length
            && a.Items.Zip(b.Items).All(pair => Equal(pair.First, pair.Second)),
        (ModuleRecord a, ModuleRecord b) => a.RecordTypeId == b.RecordTypeId
            && a.Fields.Count == b.Fields.Count
            && a.Fields.All(pair => b.Fields.TryGetValue(pair.Key, out var value) && Equal(pair.Value, value)),
        _ => false
    };

    private static long AsI64(ModuleValue value, string locus)
        => value is ModuleI64 integer ? integer.Value : throw ModulesExceptionFactory.Error("owner-evaluate", "RuntimeContractTypeMismatch", locus, new { expected = "I64" });

    private static bool AsBool(ModuleValue value, string locus)
        => value is ModuleBool boolean ? boolean.Value : throw ModulesExceptionFactory.Error("owner-evaluate", "RuntimeContractTypeMismatch", locus, new { expected = "Bool" });

    private static ModuleSequence AsSequence(ModuleValue value, string locus)
        => value as ModuleSequence ?? throw ModulesExceptionFactory.Error("owner-evaluate", "RuntimeContractTypeMismatch", locus, new { expected = "Seq" });
}

public static class OwnerContractBinder
{
    public static OwnerContractBinding Bind(ModuleIr module, OwnerBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(bundle);
        if (module.Imports.Length != 0)
            throw ModulesExceptionFactory.Error("owner-bind", "UnsupportedOwnerImports", details: new { imports = module.Imports.Length });
        var exported = module.Exports.ToImmutableHashSet(StringComparer.Ordinal);
        var helpers = module.Functions.Where(function => !exported.Contains(function.Id)).Select(function => function.Id).Order(StringComparer.Ordinal).ToArray();
        if (helpers.Length != 0)
            throw ModulesExceptionFactory.Error("owner-bind", "UnsupportedOwnerHelpers", helpers[0], new { helpers });

        var moduleTypes = module.Types.ToImmutableDictionary(type => type.Id, StringComparer.Ordinal);
        foreach (var ownerType in bundle.Types)
        {
            if (!moduleTypes.TryGetValue(ownerType.Id, out var moduleType) || !TypeDeclarationEquals(ownerType, moduleType))
                throw ModulesExceptionFactory.Error("owner-bind", "OwnerTypeClosureMismatch", ownerType.Id);
        }

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
                if (!OwnerContractEvaluator.EvaluateBoolean(contract.Requires, environment, bundle.Types, $"contract/{contract.Id}/witness/{witness.Id}/requires"))
                    throw ModulesExceptionFactory.Error("owner-evaluate", "RequiresWitnessRejected", witness.Id);
                return new EvaluatedOwnerWitness(witness.Id, witness.Arguments,
                    OwnerContractEvaluator.Evaluate(model.Body, environment, bundle.Types, $"contract/{contract.Id}/witness/{witness.Id}/model"));
            }).ToImmutableArray();
            entries.Add(new BoundOwnerEntry(function, contract, model, witnesses));
        }
        return new OwnerContractBinding(module, bundle, entries.ToImmutable());
    }

    private static bool TypeDeclarationEquals(TypeDecl left, TypeDecl right)
        => left.Id == right.Id
            && left.Fields.Length == right.Fields.Length
            && left.Fields.OrderBy(field => field.Id, StringComparer.Ordinal)
                .Zip(right.Fields.OrderBy(field => field.Id, StringComparer.Ordinal))
                .All(pair => pair.First.Id == pair.Second.Id && ModulesParser.TypesEquivalent(pair.First.Type, pair.Second.Type));
}

public static class OwnerContractReplay
{
    public static OwnerWitnessReplayResult Replay(OwnerContractBinding binding, ModuleEvaluationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var checkedWitnesses = 0;
        foreach (var entry in binding.Entries.OrderBy(entry => entry.Contract.Id, StringComparer.Ordinal))
        {
            foreach (var witness in entry.Witnesses.OrderBy(witness => witness.Id, StringComparer.Ordinal))
            {
                checkedWitnesses++;
                var byId = witness.Arguments.ToImmutableDictionary(argument => argument.ParameterId, argument => argument.Value, StringComparer.Ordinal);
                var arguments = entry.Function.Parameters.Select(parameter => byId[parameter.Id]).ToArray();
                try
                {
                    var actual = ModulesReferenceEvaluator.Invoke(binding.Module, entry.Function.Id, arguments, limits).Value;
                    if (!OwnerContractEvaluator.StructuralEquals(witness.ModelResult, actual))
                        return new OwnerWitnessReplayResult("Counterexample", checkedWitnesses,
                            new OwnerWitnessCounterexample(entry.Contract.Id, witness.Id, witness.ModelResult, actual, null));
                }
                catch (ModuleException exception)
                {
                    return new OwnerWitnessReplayResult("Counterexample", checkedWitnesses,
                        new OwnerWitnessCounterexample(entry.Contract.Id, witness.Id, witness.ModelResult, null, exception.Code));
                }
            }
        }
        return new OwnerWitnessReplayResult("Pass", checkedWitnesses, null);
    }
}
