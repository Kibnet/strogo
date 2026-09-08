using System.Collections.Immutable;
using System.Numerics;

namespace Strogo.Modules;

public static class OwnerBundleVersions
{
    public const string SchemaVersion = "strogo.owner-bundle.v0.3";
    public const string PreviousSchemaVersion = "strogo.owner-bundle.v0.2";
    public const string SchemaVersionV04 = "strogo.owner-bundle.v0.4";
}

public sealed record OwnerBundleLimits(
    int MaxExpressionNodes,
    int MaxExpressionDepth,
    int MaxWitnessesPerEntry,
    int MaxWitnessValueNodes)
{
    public const int ExpressionNodesHardMaximum = 1024;
    public const int ExpressionDepthHardMaximum = 32;
    public const int WitnessesPerEntryHardMaximum = 32;
    public const int WitnessValueNodesHardMaximum = 4096;
}

public sealed record OwnerExpression(
    string Op,
    TypeRef Type,
    ImmutableArray<OwnerExpression> Args,
    string? ReferenceId = null,
    long? I64Value = null,
    bool? BoolValue = null,
    string? RecordType = null,
    ImmutableArray<string> FieldIds = default,
    TypeRef? ElementType = null,
    int? Capacity = null,
    OwnerFoldStep? FoldStep = null);

public sealed record OwnerFoldStep(
    ImmutableArray<FunctionParameter> Parameters,
    OwnerExpression Body);

public sealed record OwnerModel(
    string Id,
    ImmutableArray<FunctionParameter> Parameters,
    TypeRef ReturnType,
    OwnerExpression Body);

public sealed record OwnerWitnessArgument(string ParameterId, ModuleValue Value);

public sealed record OwnerWitness(
    string Id,
    ImmutableArray<OwnerWitnessArgument> Arguments);

public sealed record OwnerEntryContract(
    string Id,
    string FunctionRef,
    ImmutableArray<FunctionParameter> Parameters,
    TypeRef ReturnType,
    OwnerExpression Requires,
    ImmutableArray<string> Effects,
    ImmutableArray<OwnerWitness> Witnesses,
    string ModelRef);

public sealed class OwnerBundle
{
    private readonly byte[] canonicalBytes;

    internal OwnerBundle(
        string schemaVersion,
        string bundleId,
        ImmutableArray<TypeDecl> types,
        ImmutableArray<OwnerEntryContract> entryContracts,
        ImmutableArray<OwnerModel> models,
        OwnerBundleLimits limits,
        byte[] canonicalBytes)
    {
        SchemaVersion = schemaVersion;
        BundleId = bundleId;
        Types = types;
        EntryContracts = entryContracts;
        Models = models;
        Limits = limits;
        this.canonicalBytes = canonicalBytes.ToArray();
        BundleDigest = OwnerBundleCodec.BundleDigest(this.canonicalBytes);
    }

    public string SchemaVersion { get; }
    public string BundleId { get; }
    public ImmutableArray<TypeDecl> Types { get; }
    public ImmutableArray<OwnerEntryContract> EntryContracts { get; }
    public ImmutableArray<OwnerModel> Models { get; }
    public OwnerBundleLimits Limits { get; }
    public byte[] CanonicalBytes => canonicalBytes.ToArray();
    public string BundleDigest { get; }
}

public sealed record EvaluatedOwnerWitness(
    string Id,
    ImmutableArray<OwnerWitnessArgument> Arguments,
    ModuleValue ModelResult);

public sealed record BoundOwnerEntry(
    FunctionIr Function,
    OwnerEntryContract Contract,
    OwnerModel Model,
    ImmutableArray<EvaluatedOwnerWitness> Witnesses);

public sealed record OwnerContractBinding(
    ModuleIr Module,
    OwnerBundle Bundle,
    ImmutableArray<BoundOwnerEntry> Entries);

public sealed record OwnerWitnessCounterexample(
    string ContractId,
    string WitnessId,
    ModuleValue Expected,
    ModuleValue Actual);

public sealed record OwnerWitnessReplayResult(
    string Status,
    int CheckedWitnesses,
    OwnerWitnessCounterexample? Counterexample,
    string? FailureCode = null);

public sealed record OwnerBundleLimitsV04(
    int MaxExpressionNodes,
    int MaxExpressionDepth,
    int MaxWitnessesPerEntry,
    int MaxWitnessValueNodes,
    int MaxProofEvaluationSteps)
{
    public const int ProofEvaluationStepsHardMaximum = 262144;
}

public sealed record OwnerModelV04(
    string Id,
    ImmutableArray<FunctionParameter> Parameters,
    TypeRef ReturnType,
    OwnerExpression Body);

public sealed record OwnerEntryContractV04(
    string Id,
    string FunctionRef,
    ImmutableArray<FunctionParameter> Parameters,
    TypeRef ReturnType,
    ProofExpression Requires,
    ImmutableArray<string> Effects,
    ImmutableArray<OwnerWitness> Witnesses,
    string ModelRef);

public sealed class OwnerBundleV04
{
    private readonly byte[] canonicalBytes;

    internal OwnerBundleV04(
        string bundleId,
        ImmutableArray<TypeDecl> types,
        ImmutableArray<OwnerEntryContractV04> entryContracts,
        ImmutableArray<OwnerModelV04> models,
        OwnerBundleLimitsV04 limits,
        byte[] canonicalBytes)
    {
        BundleId = bundleId;
        Types = types;
        EntryContracts = entryContracts;
        Models = models;
        Limits = limits;
        this.canonicalBytes = canonicalBytes.ToArray();
        BundleDigest = OwnerBundleV04Codec.BundleDigest(this.canonicalBytes);
    }

    public string SchemaVersion => OwnerBundleVersions.SchemaVersionV04;
    public string BundleId { get; }
    public ImmutableArray<TypeDecl> Types { get; }
    public ImmutableArray<OwnerEntryContractV04> EntryContracts { get; }
    public ImmutableArray<OwnerModelV04> Models { get; }
    public OwnerBundleLimitsV04 Limits { get; }
    public byte[] CanonicalBytes => canonicalBytes.ToArray();
    public string BundleDigest { get; }
}

public abstract record ProofValue;
public sealed record ProofI64(long Value) : ProofValue;
public sealed record ProofBool(bool Value) : ProofValue;
public sealed record ProofMathInt(BigInteger Value) : ProofValue;
public sealed record ProofModuleValue(ModuleValue Value) : ProofValue;

public sealed record FoldProofContext(
    long PrefixLength,
    ModuleSequence Sequence,
    ModuleValue InitialAccumulator,
    ModuleValue Accumulator,
    ImmutableArray<ModuleValue> Environment);

public sealed record EvaluatedOwnerWitnessV04(
    string Id,
    ImmutableArray<OwnerWitnessArgument> Arguments,
    ModuleValue ModelResult);

public sealed record BoundOwnerEntryV04(
    FunctionIr Function,
    OwnerEntryContractV04 Contract,
    OwnerModelV04 Model,
    IrInstruction? CandidateFold,
    ImmutableArray<EvaluatedOwnerWitnessV04> Witnesses);

public sealed record OwnerContractBindingV04(
    ModuleIr Module,
    OwnerBundleV04 Bundle,
    ImmutableArray<BoundOwnerEntryV04> Entries);
