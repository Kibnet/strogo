using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Kernel.Core;
using Strogo.Modules;

namespace Strogo.Modules.Portability;

public static class PortabilityVersions
{
    public const string ValidationModuleId = "portable.validation.v01";
    public const string ValidationOwnerBundleId = "portable.validation.owner-v01";
    public const string PublicApiSchema = "strogo.public-api.v0.1";
    public const string RequestSchema = "strogo.invoke.v0.1";
    public const string ResultSchema = "strogo.invoke-result.v0.1";
    public const string DotNetProfile = "dotnet-managed.v1";
    public const string JvmProfile = "jvm-java17.v1";
}

public sealed class PortabilityContractException(string code, string locus, object? details = null)
    : Exception($"{code} at {locus}")
{
    public string Code { get; } = code;
    public string Locus { get; } = locus;
    public object Details { get; } = details ?? new { };
}

public sealed class PortabilityBinding
{
    private readonly byte[] publicApiBytes;

    internal PortabilityBinding(ModuleIr module, OwnerBundleV04 ownerBundle, OwnerContractBindingV04 ownerBinding, byte[] publicApiBytes, string publicApiDigest)
    {
        Module = module;
        OwnerBundle = ownerBundle;
        OwnerBinding = ownerBinding;
        this.publicApiBytes = publicApiBytes.ToArray();
        PublicApiDigest = publicApiDigest;
    }

    public ModuleIr Module { get; }
    public OwnerBundleV04 OwnerBundle { get; }
    public OwnerContractBindingV04 OwnerBinding { get; }
    public byte[] PublicApiBytes => publicApiBytes.ToArray();
    public string PublicApiDigest { get; }
}

public sealed record PortabilityOracleResult(
    string Kind,
    ModuleValue? Value,
    string? Code,
    string? Locus,
    object Details)
{
    public static PortabilityOracleResult Success(ModuleValue value) => new("success", value, null, null, new { });
    public static PortabilityOracleResult Refusal(string code, string locus, object? details = null) => new("refusal", null, code, locus, details ?? new { });
}

public static class PortabilityContract
{
    private static readonly ImmutableArray<string> FunctionOrder = ["adjust", "echoSummary", "headOrZero", "increment", "summarize"];
    private static readonly ImmutableArray<string> RefusalPriority =
    [
        "NullRequest", "InvalidUnicode", "InputTooLarge", "MalformedJson", "TransportDepthLimitExceeded",
        "TransportValueLimitExceeded", "DuplicateProperty", "UnknownProperty", "MissingProperty",
        "UnsupportedInvokeSchema", "InvalidRoot", "UnknownWireKind", "InvalidI64Encoding",
        "NonCanonicalTransport", "UnknownFunction", "ArityMismatch", "RuntimeTypeMismatch", "OwnerPreconditionFailed"
    ];

    public static bool IsExecutionProfile(string value) => value is PortabilityVersions.DotNetProfile or PortabilityVersions.JvmProfile;

    public static PortabilityBinding Bind(ModuleIr module, OwnerBundleV04 ownerBundle)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(ownerBundle);
        Require(module.ModuleId == PortabilityVersions.ValidationModuleId, "ValidationModuleMismatch", "module/moduleId");
        Require(ownerBundle.BundleId == PortabilityVersions.ValidationOwnerBundleId, "ValidationOwnerMismatch", "owner/bundleId");
        Require(module.Imports.IsEmpty, "UnsupportedPortableImport", "module/imports");
        Require(ownerBundle.EntryContracts.All(entry => entry.Effects.IsEmpty), "UnsupportedPortableEffect", "owner/entryContracts");

        var binding = OwnerContractBinderV04.Bind(module, ownerBundle);
        ValidateTypeClosure(module);
        ValidateFunctions(module);
        ValidateStructure(module);

        var publicApi = CreatePublicApiBytes(module, ownerBundle);
        return new PortabilityBinding(module, ownerBundle, binding, publicApi, DomainHash("strogo.public-api.v0.1/artifact", publicApi));
    }

    public static PortabilityOracleResult InvokeOwner(PortabilityBinding binding, string functionId, IReadOnlyList<ModuleValue> arguments)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(functionId);
        ArgumentNullException.ThrowIfNull(arguments);
        var entry = binding.OwnerBinding.Entries.SingleOrDefault(item => item.Function.Id == functionId);
        if (entry is null) return PortabilityOracleResult.Refusal("UnknownFunction", "$/functionId", new { functionId });
        if (arguments.Count != entry.Function.Parameters.Length)
            return PortabilityOracleResult.Refusal("ArityMismatch", "$/arguments", new { expected = entry.Function.Parameters.Length.ToString(), actual = arguments.Count.ToString() });

        for (var index = 0; index < arguments.Count; index++)
        {
            var mismatch = TypeMismatch(arguments[index], entry.Function.Parameters[index].Type, binding.Module.Types, $"$/arguments/{index}");
            if (mismatch is not null) return PortabilityOracleResult.Refusal("RuntimeTypeMismatch", mismatch, new { });
        }

        var environment = entry.Function.Parameters.Select((parameter, index) => KeyValuePair.Create(parameter.Id, arguments[index]))
            .ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (!OwnerProofEvaluator.EvaluateBoolean(entry.Contract.Requires, environment, binding.OwnerBundle.Types, binding.OwnerBundle.Limits.MaxProofEvaluationSteps))
            return PortabilityOracleResult.Refusal("OwnerPreconditionFailed", $"function/{functionId}/requires", new { });
        return PortabilityOracleResult.Success(OwnerModelEvaluatorV04.Evaluate(entry.Model.Body, environment, binding.OwnerBundle.Types));
    }

    public static string DomainHash(string tag, ReadOnlySpan<byte> bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(tag + "\n"));
        hash.AppendData(bytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void ValidateTypeClosure(ModuleIr module)
    {
        Require(module.Types.Length == 1 && module.Types[0].Id == "Summary", "UnsupportedPortableAbi", "module/types");
        var fields = module.Types[0].Fields.OrderBy(field => field.Id, StringComparer.Ordinal).ToArray();
        Require(fields.Length == 3, "UnsupportedPortableAbi", "module/types/Summary/fields");
        Require(fields[0].Id == "echo" && IsSequence(fields[0].Type, 8), "UnsupportedPortableAbi", "module/types/Summary/field/echo");
        Require(fields[1].Id == "negativeCount" && fields[1].Type.Kind == "I64", "UnsupportedPortableAbi", "module/types/Summary/field/negativeCount");
        Require(fields[2].Id == "sum" && fields[2].Type.Kind == "I64", "UnsupportedPortableAbi", "module/types/Summary/field/sum");
    }

    private static void ValidateFunctions(ModuleIr module)
    {
        Require(module.Exports.Order(StringComparer.Ordinal).SequenceEqual(FunctionOrder, StringComparer.Ordinal), "UnsupportedPortableAbi", "module/exports");
        Require(module.Functions.Select(function => function.Id).Order(StringComparer.Ordinal).SequenceEqual(FunctionOrder, StringComparer.Ordinal), "UnsupportedPortableAbi", "module/functions");
        var functions = module.Functions.ToImmutableDictionary(function => function.Id, StringComparer.Ordinal);
        Signature(functions["summarize"], [("items", Seq(8))], Record(), "Summary");
        Signature(functions["increment"], [("x", I64())], I64(), "I64");
        Signature(functions["adjust"], [("increase", Bool()), ("x", I64())], I64(), "I64");
        Signature(functions["headOrZero"], [("items", Seq(1))], I64(), "I64");
        Signature(functions["echoSummary"], [("summary", Record())], Record(), "Summary");
    }

    private static void ValidateStructure(ModuleIr module)
    {
        var functions = module.Functions.ToImmutableDictionary(function => function.Id, StringComparer.Ordinal);
        Require(Count(functions["summarize"], "fold") == 1, "ValidationWorkloadMismatch", "function/summarize/fold");
        Require(Count(functions["headOrZero"], "if") == 1 && Count(functions["headOrZero"], "seq.get") == 1, "ValidationWorkloadMismatch", "function/headOrZero");
        Require(Count(functions["adjust"], "call") == 1 && Calls(functions["adjust"], "increment"), "ValidationWorkloadMismatch", "function/adjust/call");
        Require(!module.Functions.Any(ContainsCallInFold), "CallInsideFoldStep", "module/functions");
    }

    private static bool ContainsCallInFold(FunctionIr function)
        => ContainsCallInFold(new RegionIr(function.Parameters, function.Instructions, function.ResultIndex), false);

    private static bool ContainsCallInFold(RegionIr region, bool insideFold)
    {
        foreach (var instruction in region.Instructions)
        {
            if (insideFold && instruction.Op == "call") return true;
            if (instruction.ThenRegion is not null && ContainsCallInFold(instruction.ThenRegion, insideFold)) return true;
            if (instruction.ElseRegion is not null && ContainsCallInFold(instruction.ElseRegion, insideFold)) return true;
            if (instruction.Fold is not null && ContainsCallInFold(instruction.Fold.Step, true)) return true;
        }
        return false;
    }

    private static bool Calls(FunctionIr function, string functionId) => AllInstructions(function).Any(instruction => instruction.Op == "call" && instruction.Metadata.FunctionRef == functionId);
    private static int Count(FunctionIr function, string op) => AllInstructions(function).Count(instruction => instruction.Op == op);
    private static IEnumerable<IrInstruction> AllInstructions(FunctionIr function) => AllInstructions(new RegionIr(function.Parameters, function.Instructions, function.ResultIndex));
    private static IEnumerable<IrInstruction> AllInstructions(RegionIr region)
    {
        foreach (var instruction in region.Instructions)
        {
            yield return instruction;
            if (instruction.ThenRegion is not null) foreach (var nested in AllInstructions(instruction.ThenRegion)) yield return nested;
            if (instruction.ElseRegion is not null) foreach (var nested in AllInstructions(instruction.ElseRegion)) yield return nested;
            if (instruction.Fold is not null) foreach (var nested in AllInstructions(instruction.Fold.Step)) yield return nested;
        }
    }

    private static byte[] CreatePublicApiBytes(ModuleIr module, OwnerBundleV04 ownerBundle)
    {
        var types = module.Types.OrderBy(type => type.Id, StringComparer.Ordinal).Select(type => new
        {
            typeId = type.Id,
            fields = type.Fields.OrderBy(field => field.Id, StringComparer.Ordinal).Select(field => new { fieldId = field.Id, type = TypeId(field.Type) }).ToArray()
        }).ToArray();
        var functions = module.Functions.OrderBy(function => function.Id, StringComparer.Ordinal).Select(function => new
        {
            functionId = function.Id,
            parameters = function.Parameters.Select(parameter => new { parameterId = parameter.Id, type = TypeId(parameter.Type) }).ToArray(),
            resultType = TypeId(function.ReturnType),
            contractId = ownerBundle.EntryContracts.Single(entry => entry.FunctionRef == function.Id).Id
        }).ToArray();
        return CanonicalJson.Encode(new
        {
            schemaVersion = PortabilityVersions.PublicApiSchema,
            moduleId = module.ModuleId,
            ownerBundleId = ownerBundle.BundleId,
            targetOperations = new
            {
                csharp = "Strogo.Portable.V01.ModuleApi.Invoke(string)->string",
                java = "strogo.portable.v01.ModuleApi.invoke(String)->String"
            },
            functions,
            types,
            wireKinds = new[] { "bool", "i64", "record", "sequence" },
            refusalPriority = RefusalPriority,
            limits = new { inputUtf8Bytes = "65536", jsonDepth = "32", jsonValues = "2048", sequenceCapacity = "8" }
        });
    }

    private static string TypeId(TypeRef type) => type.Kind switch
    {
        "I64" or "Bool" => type.Kind,
        "Record" => type.Name ?? throw new PortabilityContractException("UnsupportedPortableAbi", "type"),
        "Seq" when type.Element?.Kind == "I64" && type.Capacity is not null => $"Seq<I64,{type.Capacity.Value}>",
        _ => throw new PortabilityContractException("UnsupportedPortableAbi", "type", new { actual = type.ToString() })
    };

    private static string? TypeMismatch(ModuleValue value, TypeRef expected, ImmutableArray<TypeDecl> declarations, string locus)
    {
        if (expected.Kind == "I64") return value is ModuleI64 ? null : locus;
        if (expected.Kind == "Bool") return value is ModuleBool ? null : locus;
        if (expected.Kind == "Seq" && expected.Element is not null && expected.Capacity is not null)
        {
            if (value is not ModuleSequence sequence || !SameType(sequence.ElementType, expected.Element) || sequence.Capacity != expected.Capacity || sequence.Items.Length > expected.Capacity) return locus;
            for (var index = 0; index < sequence.Items.Length; index++)
            {
                var nested = TypeMismatch(sequence.Items[index], expected.Element, declarations, $"{locus}/items/{index}");
                if (nested is not null) return nested;
            }
            return null;
        }
        if (expected.Kind == "Record" && expected.Name is not null)
        {
            if (value is not ModuleRecord record || record.RecordTypeId != expected.Name) return locus;
            var declaration = declarations.Single(item => item.Id == expected.Name);
            if (!record.Fields.Keys.SequenceEqual(declaration.Fields.Select(field => field.Id).Order(StringComparer.Ordinal), StringComparer.Ordinal)) return locus;
            foreach (var field in declaration.Fields.OrderBy(field => field.Id, StringComparer.Ordinal))
            {
                var nested = TypeMismatch(record.Fields[field.Id], field.Type, declarations, $"{locus}/fields/{field.Id}");
                if (nested is not null) return nested;
            }
            return null;
        }
        return locus;
    }

    private static void Signature(FunctionIr actual, (string Id, TypeRef Type)[] parameters, TypeRef result, string resultLocus)
    {
        Require(actual.Parameters.Length == parameters.Length, "UnsupportedPortableAbi", $"function/{actual.Id}/parameters");
        for (var index = 0; index < parameters.Length; index++)
            Require(actual.Parameters[index].Id == parameters[index].Id && SameType(actual.Parameters[index].Type, parameters[index].Type), "UnsupportedPortableAbi", $"function/{actual.Id}/parameter/{index}");
        Require(SameType(actual.ReturnType, result), "UnsupportedPortableAbi", $"function/{actual.Id}/return/{resultLocus}");
    }

    private static TypeRef I64() => new("I64");
    private static TypeRef Bool() => new("Bool");
    private static TypeRef Record() => new("Record", Name: "Summary");
    private static TypeRef Seq(int capacity) => new("Seq", Element: I64(), Capacity: capacity);
    private static bool IsSequence(TypeRef type, int capacity) => SameType(type, Seq(capacity));
    private static bool SameType(TypeRef left, TypeRef right) => left.Kind == right.Kind && left.Name == right.Name && left.Capacity == right.Capacity && (left.Element is null ? right.Element is null : right.Element is not null && SameType(left.Element, right.Element));
    private static void Require(bool condition, string code, string locus)
    {
        if (!condition) throw new PortabilityContractException(code, locus);
    }
}
