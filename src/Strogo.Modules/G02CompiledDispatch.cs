using System.Collections;
using System.Reflection;

namespace Strogo.Modules;

/// <summary>Binding and codec only. Caller must establish assembly/IR/owner trust; this is not admission.</summary>
internal sealed class G02CompiledDispatch : IDisposable
{
    private readonly Assembly assembly;
    private readonly ModuleIr module;
    private readonly Dictionary<string, string> symbols;
    private readonly Dictionary<string, (FunctionIr Function, MethodInfo Requires, MethodInfo Entry)> exports;
    private bool disposed;

    internal G02CompiledDispatch(Assembly assembly, ModuleIr module, OwnerBundleV04 bundle)
    {
        this.assembly = assembly;
        this.module = module;
        symbols = ModulesDafnyLowerer.Lower(module, bundle).SourceMap
            .ToDictionary(entry => entry.EntityId, entry => entry.GeneratedName, StringComparer.Ordinal);
        exports = new(StringComparer.Ordinal);
        var target = assembly.GetType("Candidate.__default") ?? throw Refuse("CompiledBindingMismatch");
        foreach (var id in module.Exports)
        {
            var function = module.Functions.Single(value => value.Id == id);
            var contract = bundle.EntryContracts.Single(value => value.Id == function.ContractRef);
            var signature = function.Parameters.Select(parameter => RuntimeType(parameter.Type)).ToArray();
            var entry = Method(target, Symbol("function/" + id), signature, RuntimeType(function.ReturnType));
            var requires = Method(target, Symbol("owner/contract/" + contract.Id + "/requires"), signature, typeof(bool));
            exports.Add(id, (function, requires, entry));
        }
    }

    internal ModuleValue Invoke(string functionId, IReadOnlyList<ModuleValue> arguments)
    {
        if (disposed) throw Refuse("CompiledFixtureDisposed");
        if (functionId is null) throw Refuse("CompiledExportMissing");
        if (arguments is null) throw Refuse("CompiledInputMismatch");
        if (!exports.TryGetValue(functionId, out var export)) throw Refuse("CompiledExportMissing");
        if (arguments.Count != export.Function.Parameters.Length) throw Refuse("CompiledInputMismatch");
        var inputs = arguments.Select((value, index) => Encode(value, export.Function.Parameters[index].Type)).ToArray();
        try
        {
            if (export.Requires.Invoke(null, inputs) is not true) throw Refuse("CompiledPreconditionFailed");
            var output = export.Entry.Invoke(null, inputs) ?? throw Refuse("CompiledOutputMismatch");
            return Decode(output, export.Function.ReturnType);
        }
        catch (TargetInvocationException) { throw Refuse("CompiledInvocationFailed"); }
    }

    private string Symbol(string entity)
        => symbols.TryGetValue(entity, out var symbol) ? symbol : throw Refuse("CompiledBindingMismatch");

    private Type RuntimeType(TypeRef type) => type.Kind switch
    {
        "I64" => typeof(long),
        "Bool" => typeof(bool),
        "Seq" when type.Capacity is >= 0 and <= 256 =>
            (assembly.GetType("Dafny.ISequence`1") ?? throw Refuse("CompiledBindingMismatch"))
                .MakeGenericType(RuntimeType(type.Element!)),
        "Record" => assembly.GetType("Candidate._I" + Symbol("type/" + type.Name))
            ?? throw Refuse("CompiledBindingMismatch"),
        _ => throw Refuse("CompiledTypeUnsupported")
    };

    private static MethodInfo Method(Type target, string name, Type[] parameters, Type result)
    {
        var method = target.GetMethod(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            binder: null, types: parameters, modifiers: null);
        if (method is null || method.IsGenericMethod || method.ReturnType != result)
            throw Refuse("CompiledBindingMismatch");
        return method;
    }

    private object Encode(ModuleValue value, TypeRef type)
    {
        switch (type.Kind)
        {
            case "I64" when value is ModuleI64 number: return number.Value;
            case "Bool" when value is ModuleBool boolean: return boolean.Value;
            case "Seq" when value is ModuleSequence sequence && !sequence.Items.IsDefault &&
                sequence.ElementType == type.Element && sequence.Capacity == type.Capacity &&
                sequence.Items.Length <= type.Capacity:
            {
                var elementType = RuntimeType(type.Element!);
                var array = Array.CreateInstance(elementType, sequence.Items.Length);
                for (var index = 0; index < sequence.Items.Length; index++)
                    array.SetValue(Encode(sequence.Items[index], type.Element!), index);
                var target = (assembly.GetType("Dafny.Sequence`1") ?? throw Refuse("CompiledBindingMismatch"))
                    .MakeGenericType(elementType);
                return Method(target, "FromArray", [array.GetType()], RuntimeType(type)).Invoke(null, [array])
                    ?? throw Refuse("CompiledBindingMismatch");
            }
            case "Record" when value is ModuleRecord record && record.RecordTypeId == type.Name && record.Fields is not null:
            {
                var fields = module.Types.Single(item => item.Id == type.Name).Fields;
                if (record.Fields.Count != fields.Length || fields.Any(field => !record.Fields.ContainsKey(field.Id)))
                    throw Refuse("CompiledInputMismatch");
                var target = assembly.GetType("Candidate." + Symbol("type/" + type.Name))
                    ?? throw Refuse("CompiledBindingMismatch");
                var constructor = Method(target, "create", fields.Select(field => RuntimeType(field.Type)).ToArray(), RuntimeType(type));
                return constructor.Invoke(null, fields.Select(field => Encode(record.Fields[field.Id], field.Type)).ToArray())
                    ?? throw Refuse("CompiledBindingMismatch");
            }
            default: throw Refuse("CompiledInputMismatch");
        }
    }

    private ModuleValue Decode(object value, TypeRef type)
    {
        if (!RuntimeType(type).IsInstanceOfType(value)) throw Refuse("CompiledOutputMismatch");
        switch (type.Kind)
        {
            case "I64": return new ModuleI64((long)value);
            case "Bool": return new ModuleBool((bool)value);
            case "Seq":
            {
                var items = new List<ModuleValue>();
                foreach (var item in (IEnumerable)value)
                {
                    if (items.Count >= type.Capacity || item is null) throw Refuse("CompiledOutputMismatch");
                    items.Add(Decode(item, type.Element!));
                }
                return new ModuleSequence(type.Element!, type.Capacity!.Value, items);
            }
            case "Record":
            {
                var fields = module.Types.Single(item => item.Id == type.Name).Fields;
                return new ModuleRecord(type.Name!, fields.Select(field =>
                {
                    var property = RuntimeType(type).GetProperty("dtor_" + Symbol("type/" + type.Name + "/field/" + field.Id));
                    if (property is null || property.PropertyType != RuntimeType(field.Type))
                        throw Refuse("CompiledBindingMismatch");
                    var fieldValue = property.GetValue(value) ?? throw Refuse("CompiledOutputMismatch");
                    return new KeyValuePair<string, ModuleValue>(field.Id, Decode(fieldValue, field.Type));
                }));
            }
            default: throw Refuse("CompiledTypeUnsupported");
        }
    }

    public void Dispose() => disposed = true;

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("package", code);
}
