using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Kernel.Core;

namespace Strogo.Modules;

public static class OwnerBundleCodec
{
    public const string DigestDomain = "strogo.owner-bundle.v0.2/bundle";

    public static byte[] Canonicalize(
        string schemaVersion,
        string bundleId,
        IEnumerable<OwnerEntryContract> entries,
        IEnumerable<OwnerModel> models,
        OwnerBundleLimits limits)
        => CanonicalJson.Encode(new
        {
            schemaVersion,
            bundleId,
            entryContracts = entries.OrderBy(entry => entry.Id, StringComparer.Ordinal).Select(EntryPayload).ToArray(),
            models = models.OrderBy(model => model.Id, StringComparer.Ordinal).Select(ModelPayload).ToArray(),
            limits = new
            {
                maxExpressionNodes = limits.MaxExpressionNodes.ToString(CultureInfo.InvariantCulture),
                maxExpressionDepth = limits.MaxExpressionDepth.ToString(CultureInfo.InvariantCulture),
                maxWitnessesPerEntry = limits.MaxWitnessesPerEntry.ToString(CultureInfo.InvariantCulture)
            }
        });

    public static string BundleDigest(ReadOnlySpan<byte> canonicalBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{DigestDomain}\n"));
        hash.AppendData(canonicalBytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static object EntryPayload(OwnerEntryContract entry) => new
    {
        id = entry.Id,
        functionRef = entry.FunctionRef,
        parameters = entry.Parameters.Select(ParameterPayload).ToArray(),
        returnType = entry.ReturnType.Kind,
        requires = ExpressionPayload(entry.Requires),
        ensures = ExpressionPayload(entry.Ensures),
        effects = entry.Effects,
        witnesses = entry.Witnesses.OrderBy(witness => witness.Id, StringComparer.Ordinal).Select(witness => new
        {
            id = witness.Id,
            arguments = witness.Arguments.OrderBy(argument => argument.ParameterId, StringComparer.Ordinal).Select(argument => new
            {
                parameterId = argument.ParameterId,
                value = ScalarPayload(argument.Value)
            }).ToArray()
        }).ToArray()
    };

    private static object ModelPayload(OwnerModel model) => new
    {
        id = model.Id,
        parameters = model.Parameters.Select(ParameterPayload).ToArray(),
        returnType = model.ReturnType.Kind,
        body = ExpressionPayload(model.Body)
    };

    private static object ParameterPayload(FunctionParameter parameter) => new
    {
        id = parameter.Id,
        type = parameter.Type.Kind
    };

    private static object ScalarPayload(OwnerScalarValue value) => value.Type switch
    {
        "I64" => new Dictionary<string, object?> { ["type"] = "I64", ["value"] = value.I64.ToString(CultureInfo.InvariantCulture) },
        "Bool" => new Dictionary<string, object?> { ["type"] = "Bool", ["value"] = value.Bool },
        _ => throw new InvalidOperationException($"Unsupported scalar type: {value.Type}")
    };

    private static object ExpressionPayload(OwnerExpression expression)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["op"] = expression.Op,
            ["type"] = expression.Type.Kind
        };
        switch (expression.Op)
        {
            case "param": payload["id"] = expression.ReferenceId; break;
            case "i64.const": payload["value"] = expression.I64Value!.Value.ToString(CultureInfo.InvariantCulture); break;
            case "bool.const": payload["value"] = expression.BoolValue!.Value; break;
            case "model.call": payload["modelRef"] = expression.ReferenceId; payload["args"] = expression.Args.Select(ExpressionPayload).ToArray(); break;
            case "result": break;
            default: payload["args"] = expression.Args.Select(ExpressionPayload).ToArray(); break;
        }
        return payload;
    }
}
