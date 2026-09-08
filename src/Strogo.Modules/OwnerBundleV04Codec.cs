using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Kernel.Core;

namespace Strogo.Modules;

public static class OwnerBundleV04Codec
{
    public const string DigestDomain = "strogo.owner-bundle.v0.4/bundle";

    public static byte[] Canonicalize(
        string bundleId,
        IEnumerable<TypeDecl> types,
        IEnumerable<OwnerEntryContractV04> entries,
        IEnumerable<OwnerModelV04> models,
        OwnerBundleLimitsV04 limits)
        => CanonicalJson.Encode(new
        {
            schemaVersion = OwnerBundleVersions.SchemaVersionV04,
            bundleId,
            types = types.OrderBy(type => type.Id, StringComparer.Ordinal).Select(TypeDeclarationPayload).ToArray(),
            entryContracts = entries.OrderBy(entry => entry.Id, StringComparer.Ordinal).Select(EntryPayload).ToArray(),
            models = models.OrderBy(model => model.Id, StringComparer.Ordinal).Select(ModelPayload).ToArray(),
            limits = new
            {
                maxExpressionNodes = limits.MaxExpressionNodes.ToString(CultureInfo.InvariantCulture),
                maxExpressionDepth = limits.MaxExpressionDepth.ToString(CultureInfo.InvariantCulture),
                maxWitnessesPerEntry = limits.MaxWitnessesPerEntry.ToString(CultureInfo.InvariantCulture),
                maxWitnessValueNodes = limits.MaxWitnessValueNodes.ToString(CultureInfo.InvariantCulture),
                maxProofEvaluationSteps = limits.MaxProofEvaluationSteps.ToString(CultureInfo.InvariantCulture)
            }
        });

    public static string BundleDigest(ReadOnlySpan<byte> canonicalBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{DigestDomain}\n"));
        hash.AppendData(canonicalBytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static object ProofTypePayload(TypeRef type)
        => type.Kind == "MathInt" ? "MathInt" : OwnerBundleCodec.TypePayload(type);

    internal static object ProofExpressionPayload(ProofExpression expression)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["op"] = expression.Op,
            ["type"] = ProofTypePayload(expression.Type)
        };
        switch (expression.Op)
        {
            case "param": payload["id"] = expression.ReferenceId; break;
            case "proof.bound": payload["binderId"] = expression.BinderId; break;
            case "fold.environment": payload["position"] = expression.Position!.Value.ToString(CultureInfo.InvariantCulture); break;
            case "i64.const":
            case "math.const": payload["value"] = expression.NumberValue; break;
            case "bool.const": payload["value"] = expression.BoolValue; break;
            case "record.make":
                var pairs = expression.FieldIds.Select((fieldId, index) => (fieldId, argument: expression.Args[index]))
                    .OrderBy(pair => pair.fieldId, StringComparer.Ordinal).ToArray();
                payload["recordType"] = expression.RecordType;
                payload["fieldIds"] = pairs.Select(pair => pair.fieldId).ToArray();
                payload["args"] = pairs.Select(pair => ProofExpressionPayload(pair.argument)).ToArray();
                break;
            case "record.get":
                payload["fieldId"] = expression.ReferenceId;
                payload["args"] = expression.Args.Select(ProofExpressionPayload).ToArray();
                break;
            case "seq.empty":
                payload["elementType"] = OwnerBundleCodec.TypePayload(expression.ElementType!);
                payload["capacity"] = expression.Capacity!.Value.ToString(CultureInfo.InvariantCulture);
                payload["args"] = Array.Empty<object>();
                break;
            case "forall.sequence":
                payload["binderId"] = expression.BinderId;
                payload["sequence"] = ProofExpressionPayload(expression.Sequence!);
                payload["body"] = ProofExpressionPayload(expression.Body!);
                break;
            case "fold.prefixLength":
            case "fold.sequence":
            case "fold.initialAccumulator":
            case "fold.accumulator":
                break;
            default:
                payload["args"] = expression.Args.Select(ProofExpressionPayload).ToArray();
                break;
        }
        return payload;
    }

    internal static object ModelExpressionPayload(OwnerExpression expression)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["op"] = expression.Op,
            ["type"] = OwnerBundleCodec.TypePayload(expression.Type)
        };
        switch (expression.Op)
        {
            case "param": payload["id"] = expression.ReferenceId; break;
            case "i64.const": payload["value"] = expression.I64Value!.Value.ToString(CultureInfo.InvariantCulture); break;
            case "bool.const": payload["value"] = expression.BoolValue; break;
            case "record.make":
                var pairs = expression.FieldIds.Select((fieldId, index) => (fieldId, argument: expression.Args[index]))
                    .OrderBy(pair => pair.fieldId, StringComparer.Ordinal).ToArray();
                payload["recordType"] = expression.RecordType;
                payload["fieldIds"] = pairs.Select(pair => pair.fieldId).ToArray();
                payload["args"] = pairs.Select(pair => ModelExpressionPayload(pair.argument)).ToArray();
                break;
            case "record.get":
                payload["fieldId"] = expression.ReferenceId;
                payload["args"] = expression.Args.Select(ModelExpressionPayload).ToArray();
                break;
            case "seq.empty":
                payload["elementType"] = OwnerBundleCodec.TypePayload(expression.ElementType!);
                payload["capacity"] = expression.Capacity!.Value.ToString(CultureInfo.InvariantCulture);
                payload["args"] = Array.Empty<object>();
                break;
            case "fold":
                payload["args"] = expression.Args.Select(ModelExpressionPayload).ToArray();
                payload["step"] = new
                {
                    parameters = expression.FoldStep!.Parameters.Select(ParameterPayload).ToArray(),
                    body = ModelExpressionPayload(expression.FoldStep.Body)
                };
                break;
            default:
                payload["args"] = expression.Args.Select(ModelExpressionPayload).ToArray();
                break;
        }
        return payload;
    }

    private static object TypeDeclarationPayload(TypeDecl type) => new
    {
        id = type.Id,
        fields = type.Fields.OrderBy(field => field.Id, StringComparer.Ordinal).Select(field => new
        {
            id = field.Id,
            type = OwnerBundleCodec.TypePayload(field.Type)
        }).ToArray()
    };

    private static object EntryPayload(OwnerEntryContractV04 entry) => new
    {
        id = entry.Id,
        functionRef = entry.FunctionRef,
        parameters = entry.Parameters.Select(ParameterPayload).ToArray(),
        returnType = OwnerBundleCodec.TypePayload(entry.ReturnType),
        requires = ProofExpressionPayload(entry.Requires),
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

    private static object ModelPayload(OwnerModelV04 model) => new
    {
        id = model.Id,
        parameters = model.Parameters.Select(ParameterPayload).ToArray(),
        returnType = OwnerBundleCodec.TypePayload(model.ReturnType),
        body = ModelExpressionPayload(model.Body)
    };

    private static object ParameterPayload(FunctionParameter parameter) => new
    {
        id = parameter.Id,
        type = OwnerBundleCodec.TypePayload(parameter.Type)
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
            ["type"] = OwnerBundleCodec.TypePayload(new TypeRef("Seq", Element: sequence.ElementType, Capacity: sequence.Capacity)),
            ["value"] = sequence.Items.Select(UntypedValuePayload).ToArray()
        },
        _ => throw new InvalidOperationException("Unsupported owner value")
    };
}
