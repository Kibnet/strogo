using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Kernel.Core;

namespace Strogo.Modules;

/// <summary>Internal transport only. Caller supplies trusted IR and dispatch; no admission or identity is established.</summary>
internal static class G02InvocationCodec
{
    internal const string RequestVersion = "strogo.invoke.v0.1";
    internal const string ResultVersion = "strogo.invoke-result.v0.1";
    internal const int MaxBytes = 65536;
    private const int MaxDepth = 32;
    private const int MaxValues = 4096;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly OwnerBundleLimits ValueLimits = new(1024, MaxDepth, 16, MaxValues);

    internal sealed record Request(string FunctionId, ImmutableArray<ModuleValue> Arguments, byte[] CanonicalBytes);

    internal static Request Parse(ModuleIr module, byte[] source)
        => Parse(module, source, out _);

    private static Request Parse(ModuleIr module, byte[] source, out string? functionId)
    {
        functionId = null;
        if (source is null) throw Error("SchemaInvalid");
        if (source.Length > MaxBytes) throw Error("TransportLimitExceeded");
        try
        {
            using var doc = CanonicalJson.ParseStrict(Utf8.GetString(source), new CoreLimits { MaxTransportBytes = MaxBytes, MaxJsonDepth = MaxDepth });
            var root = doc.RootElement;
            OwnerBundleParser.CheckObject(root, "request", ["schemaVersion", "functionId", "arguments"]);
            if (OwnerBundleParser.RequireString(root.GetProperty("schemaVersion")) != RequestVersion) throw Error("SchemaVersionMismatch");
            functionId = OwnerBundleParser.RequireId(root.GetProperty("functionId"));
            if (!module.Exports.Contains(functionId, StringComparer.Ordinal)) throw Error("CompiledExportMissing", functionId);
            var function = module.Functions.Single(value => value.Id == root.GetProperty("functionId").GetString());
            var raw = root.GetProperty("arguments");
            if (raw.ValueKind != JsonValueKind.Array) throw Error("SchemaInvalid", "arguments");
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var item in raw.EnumerateArray())
            {
                OwnerBundleParser.CheckObject(item, "argument", ["parameterId", "value"]);
                var id = OwnerBundleParser.RequireId(item.GetProperty("parameterId"));
                if (!values.TryAdd(id, item.GetProperty("value"))) throw Error("DuplicateArgument", id);
            }
            if (values.Count != function.Parameters.Length || function.Parameters.Any(parameter => !values.ContainsKey(parameter.Id)))
                throw Error("ArgumentsMismatch");
            var types = module.Types.ToDictionary(value => value.Id, StringComparer.Ordinal);
            var count = 0;
            var parsed = ImmutableArray.CreateBuilder<ModuleValue>();
            foreach (var parameter in function.Parameters)
                parsed.Add(OwnerBundleParser.ParseValue(values[parameter.Id], parameter.Type, types, parameter.Id, ValueLimits, ref count));
            var arguments = parsed.ToImmutable();
            var canonical = CanonicalJson.Encode(new { schemaVersion = RequestVersion, functionId,
                arguments = function.Parameters.Select((parameter, index) => new { parameterId = parameter.Id, value = OwnerBundleCodec.ValuePayload(arguments[index], parameter.Type) }).OrderBy(value => value.parameterId, StringComparer.Ordinal).ToArray() });
            if (canonical.Length > MaxBytes) throw Error("TransportLimitExceeded");
            return new Request(functionId, arguments, canonical);
        }
        catch (DecoderFallbackException) { throw Error("InvalidUtf8"); }
        catch (KernelException error) { throw Error(error.Error.Code); }
        catch (ModuleException error) when (error.Stage == "owner-parse") { throw Error(InputCode(error.Code), error.EntityId); }
    }

    internal static byte[] Invoke(ModuleIr module, byte[] source, Func<string, IReadOnlyList<ModuleValue>, ModuleValue> dispatch)
    {
        string? functionId = null;
        try
        {
            var request = Parse(module, source, out functionId);
            var result = dispatch(request.FunctionId, request.Arguments);
            var type = module.Functions.Single(value => value.Id == request.FunctionId).ReturnType;
            var types = module.Types.ToDictionary(value => value.Id, StringComparer.Ordinal);
            var count = 0;
            ValidateOutput(result, type, types, 2, ref count); // Result root=1, typed value object=2.
            var bytes = CanonicalJson.Encode(new { schemaVersion = ResultVersion, functionId, status = "Returned", value = OwnerBundleCodec.ValuePayload(result, type) });
            if (bytes.Length > MaxBytes) throw Error("OutputLimitExceeded");
            return bytes;
        }
        catch (ModuleException error)
        {
            return CanonicalJson.Encode(new { schemaVersion = ResultVersion, functionId, status = "Refused", error = new { error.Stage, error.Code, error.EntityId } });
        }
    }

    private static void ValidateOutput(ModuleValue? value, TypeRef type, IReadOnlyDictionary<string, TypeDecl> types, int depth, ref int count)
    {
        if (depth > MaxDepth || ++count > MaxValues) throw Error("OutputLimitExceeded");
        switch (value)
        {
            case ModuleI64 when type.Kind == "I64": return;
            case ModuleBool when type.Kind == "Bool": return;
            case ModuleSequence sequence when type.Kind == "Seq" && type.Element is not null && type.Capacity is >= 0 and <= 256:
                if (sequence.Items.IsDefault || sequence.ElementType is null || sequence.Capacity != type.Capacity || !ModulesParser.TypesEquivalent(sequence.ElementType, type.Element) || sequence.Items.Length > sequence.Capacity) throw Error("CompiledOutputMismatch");
                if (depth + 1 > MaxDepth) throw Error("OutputLimitExceeded");
                foreach (var item in sequence.Items) ValidateOutput(item, type.Element, types, depth + 2, ref count);
                return;
            case ModuleRecord record when type.Kind == "Record" && type.Name is not null && types.TryGetValue(type.Name, out var declaration):
                if (record.RecordTypeId != type.Name || record.Fields is null || record.Fields.Count != declaration.Fields.Length || declaration.Fields.Any(field => !record.Fields.ContainsKey(field.Id))) throw Error("CompiledOutputMismatch");
                if (depth + 1 > MaxDepth) throw Error("OutputLimitExceeded");
                foreach (var field in declaration.Fields) ValidateOutput(record.Fields[field.Id], field.Type, types, depth + 3, ref count);
                return;
            default: throw Error("CompiledOutputMismatch");
        }
    }

    private static string InputCode(string code) => code switch
    {
        "ContractTypeMismatch" => "InputTypeMismatch",
        "WitnessValueNodeLimitExceeded" => "ValueLimitExceeded",
        "DuplicateWitnessField" => "DuplicateRecordField",
        "InvalidOwnerLimit" => "InvalidCapacity",
        _ => code
    };
    private static ModuleException Error(string code, string? entityId = null) => ModulesExceptionFactory.Error("invoke", code, entityId);
}
