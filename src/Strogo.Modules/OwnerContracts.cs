using System.Collections.Immutable;

namespace Strogo.Modules;

public static class OwnerBundleVersions
{
    public const string SchemaVersion = "strogo.owner-bundle.v0.2";
}

public sealed record OwnerBundleLimits(
    int MaxExpressionNodes,
    int MaxExpressionDepth,
    int MaxWitnessesPerEntry)
{
    public const int ExpressionNodesHardMaximum = 1024;
    public const int ExpressionDepthHardMaximum = 32;
    public const int WitnessesPerEntryHardMaximum = 32;
}

public sealed record OwnerExpression(
    string Op,
    TypeRef Type,
    ImmutableArray<OwnerExpression> Args,
    string? ReferenceId = null,
    long? I64Value = null,
    bool? BoolValue = null);

public sealed record OwnerModel(
    string Id,
    ImmutableArray<FunctionParameter> Parameters,
    TypeRef ReturnType,
    OwnerExpression Body);

public readonly record struct OwnerScalarValue(string Type, long I64, bool Bool)
{
    public static OwnerScalarValue FromI64(long value) => new("I64", value, false);
    public static OwnerScalarValue FromBool(bool value) => new("Bool", 0, value);
}

public sealed record OwnerWitnessArgument(string ParameterId, OwnerScalarValue Value);

public sealed record OwnerWitness(
    string Id,
    ImmutableArray<OwnerWitnessArgument> Arguments);

public sealed record OwnerEntryContract(
    string Id,
    string FunctionRef,
    ImmutableArray<FunctionParameter> Parameters,
    TypeRef ReturnType,
    OwnerExpression Requires,
    OwnerExpression Ensures,
    ImmutableArray<string> Effects,
    ImmutableArray<OwnerWitness> Witnesses,
    string ModelRef);

public sealed class OwnerBundle
{
    private readonly byte[] canonicalBytes;

    internal OwnerBundle(
        string schemaVersion,
        string bundleId,
        ImmutableArray<OwnerEntryContract> entryContracts,
        ImmutableArray<OwnerModel> models,
        OwnerBundleLimits limits,
        byte[] canonicalBytes)
    {
        SchemaVersion = schemaVersion;
        BundleId = bundleId;
        EntryContracts = entryContracts;
        Models = models;
        Limits = limits;
        this.canonicalBytes = canonicalBytes.ToArray();
        BundleDigest = OwnerBundleCodec.BundleDigest(this.canonicalBytes);
    }

    public string SchemaVersion { get; }
    public string BundleId { get; }
    public ImmutableArray<OwnerEntryContract> EntryContracts { get; }
    public ImmutableArray<OwnerModel> Models { get; }
    public OwnerBundleLimits Limits { get; }
    public byte[] CanonicalBytes => canonicalBytes.ToArray();
    public string BundleDigest { get; }
}

public sealed record EvaluatedOwnerWitness(
    string Id,
    ImmutableArray<OwnerWitnessArgument> Arguments,
    OwnerScalarValue ModelResult);

public sealed record BoundOwnerEntry(
    FunctionIr Function,
    OwnerEntryContract Contract,
    OwnerModel Model,
    ImmutableArray<EvaluatedOwnerWitness> Witnesses);

public sealed record OwnerContractBinding(
    ModuleIr Module,
    OwnerBundle Bundle,
    ImmutableArray<BoundOwnerEntry> Entries);
