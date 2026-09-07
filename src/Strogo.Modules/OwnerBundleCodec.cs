using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Kernel.Core;

namespace Strogo.Modules;

public static class OwnerBundleCodec
{
    public const string DigestDomain = "strogo.owner-bundle.v0.3/bundle";

    public static byte[] Canonicalize(
        string schemaVersion,
        string bundleId,
        IEnumerable<TypeDecl> types,
        IEnumerable<OwnerEntryContract> entries,
        IEnumerable<OwnerModel> models,
        OwnerBundleLimits limits)
        => CanonicalJson.Encode(new
        {
            schemaVersion,
            bundleId,
            types = types.OrderBy(type => type.Id, StringComparer.Ordinal).Select(TypeDeclarationPayload).ToArray(),
            entryContracts = entries.OrderBy(entry => entry.Id, StringComparer.Ordinal).Select(EntryPayload).ToArray(),
            models = models.OrderBy(model => model.Id, StringComparer.Ordinal).Select(ModelPayload).ToArray(),
            limits = new
            {
                maxExpressionNodes = limits.MaxExpressionNodes.ToString(CultureInfo.InvariantCulture),
                maxExpressionDepth = limits.MaxExpressionDepth.ToString(CultureInfo.InvariantCulture),
                maxWitnessesPerEntry = limits.MaxWitnessesPerEntry.ToString(CultureInfo.InvariantCulture),
                maxWitnessValueNodes = limits.MaxWitnessValueNodes.ToString(CultureInfo.InvariantCulture)
            }
        });

    public static string BundleDigest(ReadOnlySpan<byte> canonicalBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{DigestDomain}\n"));
        hash.AppendData(canonicalBytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static object TypePayload(TypeRef type) => type.Kind switch
    {
        "I64" or "Bool" => type.Kind,
        "Record" when type.Name is not null => type.Name,
        "Seq" when type.Element is not null && type.Capacity is not null => new
        {
            kind = "Seq",
            elementType = TypePayload(type.Element),
            capacity = type.Capacity.Value.ToString(CultureInfo.InvariantCulture)
        },
        _ => throw new InvalidOperationException($"Unsupported owner type: {type}")
    };

    internal static object ValuePayload(ModuleValue value, TypeRef type) => value switch
    {
        ModuleI64 integer when type.Kind == "I64" => new Dictionary<string, object?>
        {
            ["type"] = TypePayload(type), ["value"] = integer.Value.ToString(CultureInfo.InvariantCulture)
        },
        ModuleBool boolean when type.Kind == "Bool" => new Dictionary<string, object?>
        {
            ["type"] = TypePayload(type), ["value"] = boolean.Value
        },
        ModuleRecord record when type.Kind == "Record" => new Dictionary<string, object?>
        {
            ["type"] = TypePayload(type),
            ["value"] = record.Fields.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new
            {
                fieldId = pair.Key,
                value = UntypedValuePayload(pair.Value)
            }).ToArray()
        },
        ModuleSequence sequence when type.Kind == "Seq" => new Dictionary<string, object?>
        {
            ["type"] = TypePayload(type),
            ["value"] = sequence.Items.Select(UntypedValuePayload).ToArray()
        },
        _ => throw new InvalidOperationException($"Value does not match owner type {type}")
    };

    private static object UntypedValuePayload(ModuleValue value) => value switch
    {
        ModuleI64 integer => new Dictionary<string, object?> { ["type"] = "I64", ["value"] = integer.Value.ToString(CultureInfo.InvariantCulture) },
        ModuleBool boolean => new Dictionary<string, object?> { ["type"] = "Bool", ["value"] = boolean.Value },
        ModuleRecord record => new Dictionary<string, object?>
        {
            ["type"] = record.RecordTypeId,
            ["value"] = record.Fields.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new { fieldId = pair.Key, value = UntypedValuePayload(pair.Value) }).ToArray()
        },
        ModuleSequence sequence => new Dictionary<string, object?>
        {
            ["type"] = TypePayload(new TypeRef("Seq", Element: sequence.ElementType, Capacity: sequence.Capacity)),
            ["value"] = sequence.Items.Select(UntypedValuePayload).ToArray()
        },
        _ => throw new InvalidOperationException("Unsupported owner value")
    };

    private static object TypeDeclarationPayload(TypeDecl type) => new
    {
        id = type.Id,
        fields = type.Fields.OrderBy(field => field.Id, StringComparer.Ordinal).Select(field => new
        {
            id = field.Id,
            type = TypePayload(field.Type)
        }).ToArray()
    };

    private static object EntryPayload(OwnerEntryContract entry) => new
    {
        id = entry.Id,
        functionRef = entry.FunctionRef,
        parameters = entry.Parameters.Select(ParameterPayload).ToArray(),
        returnType = TypePayload(entry.ReturnType),
        requires = ExpressionPayload(entry.Requires),
        effects = entry.Effects,
        witnesses = entry.Witnesses.OrderBy(witness => witness.Id, StringComparer.Ordinal).Select(witness => new
        {
            id = witness.Id,
            arguments = witness.Arguments.OrderBy(argument => argument.ParameterId, StringComparer.Ordinal).Select(argument => new
            {
                parameterId = argument.ParameterId,
                value = UntypedValuePayload(argument.Value)
            }).ToArray()
        }).ToArray(),
        modelRef = entry.ModelRef
    };

    private static object ModelPayload(OwnerModel model) => new
    {
        id = model.Id,
        parameters = model.Parameters.Select(ParameterPayload).ToArray(),
        returnType = TypePayload(model.ReturnType),
        body = ExpressionPayload(model.Body)
    };

    private static object ParameterPayload(FunctionParameter parameter) => new
    {
        id = parameter.Id,
        type = TypePayload(parameter.Type)
    };

    private static object ExpressionPayload(OwnerExpression expression)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["op"] = expression.Op,
            ["type"] = TypePayload(expression.Type)
        };
        switch (expression.Op)
        {
            case "param":
                payload["id"] = expression.ReferenceId;
                break;
            case "i64.const":
                payload["value"] = expression.I64Value!.Value.ToString(CultureInfo.InvariantCulture);
                break;
            case "bool.const":
                payload["value"] = expression.BoolValue!.Value;
                break;
            case "record.make":
                payload["recordType"] = expression.RecordType;
                var pairs = expression.FieldIds.Select((fieldId, index) => (fieldId, argument: expression.Args[index]))
                    .OrderBy(pair => pair.fieldId, StringComparer.Ordinal).ToArray();
                payload["fieldIds"] = pairs.Select(pair => pair.fieldId).ToArray();
                payload["args"] = pairs.Select(pair => ExpressionPayload(pair.argument)).ToArray();
                break;
            case "record.get":
                payload["fieldId"] = expression.ReferenceId;
                payload["args"] = expression.Args.Select(ExpressionPayload).ToArray();
                break;
            case "seq.empty":
                payload["elementType"] = TypePayload(expression.ElementType!);
                payload["capacity"] = expression.Capacity!.Value.ToString(CultureInfo.InvariantCulture);
                payload["args"] = Array.Empty<object>();
                break;
            default:
                payload["args"] = expression.Args.Select(ExpressionPayload).ToArray();
                break;
        }
        return payload;
    }
}
