using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Kernel.Core;

namespace Strogo.Modules;

public sealed record StrogoLimits
{
    public const int TransportBytesHardMaximum = 1_048_576;
    public const int JsonDepthHardMaximum = 64;
    public const int TypesHardMaximum = 256;
    public const int ImportsHardMaximum = 31;
    public const int FunctionsHardMaximum = 128;
    public const int NodesPerFunctionHardMaximum = 512;
    public const int TotalNodesHardMaximum = 4096;
    public const int TypeDepthHardMaximum = 16;
    public const int RegionDepthHardMaximum = 16;

    public int MaxTransportBytes { get; init; } = TransportBytesHardMaximum;
    public int MaxJsonDepth { get; init; } = JsonDepthHardMaximum;
    public int MaxTypes { get; init; } = TypesHardMaximum;
    public int MaxImports { get; init; } = ImportsHardMaximum;
    public int MaxFunctions { get; init; } = FunctionsHardMaximum;
    public int MaxNodesPerFunction { get; init; } = NodesPerFunctionHardMaximum;
    public int MaxTotalNodes { get; init; } = TotalNodesHardMaximum;
    public int MaxTypeDepth { get; init; } = TypeDepthHardMaximum;
    public int MaxRegionDepth { get; init; } = RegionDepthHardMaximum;
}

public static class StrogoVersions
{
    public const string SchemaVersion = "strogo.module.v0.2";
    public const string IrVersion = "strogo.module.ir.v0.2";
}

public sealed record ModuleSource(
    string SchemaVersion,
    string ModuleId,
    ImmutableArray<TypeDecl> Types,
    ImmutableArray<ImportDecl> Imports,
    ImmutableArray<FunctionDecl> Functions,
    ImmutableArray<string> Exports);

public sealed record TypeDecl(
    string Id,
    ImmutableArray<RecordField> Fields);

public sealed record RecordField(
    string Id,
    TypeRef Type);

public sealed record ImportDecl(
    string ModuleId,
    string SourceDigest,
    string ContractDigest);

public sealed record TypeRef(string Kind, string? Name = null, TypeRef? Element = null, int? Capacity = null)
{
    public bool IsBuiltin => Kind is "I64" or "Bool";
    public bool IsSeq => Kind == "Seq";
    public bool IsRecord => Kind == "Record";

    public sealed override string ToString()
    {
        return Kind switch
        {
            "I64" or "Bool" when Name is null => Kind,
            "Record" => Name ?? "<missing>",
            "Seq" => $"Seq<{Element},{Capacity}>",
            _ => Kind
        };
    }
}

public sealed record FunctionDecl(
    string Id,
    ImmutableArray<FunctionParameter> Parameters,
    TypeRef ReturnType,
    string ContractRef,
    FunctionBody Body);

public sealed record FunctionParameter(
    string Id,
    TypeRef Type);

public sealed record FunctionBody(
    ImmutableArray<string> Parameters,
    ImmutableArray<FunctionNode> Nodes,
    string Result);

public sealed record FunctionNode(
    string Id,
    string Op,
    TypeRef Type,
    ImmutableArray<string> Args,
    NodeMetadata Metadata,
    FunctionBody? ThenRegion = null,
    FunctionBody? ElseRegion = null,
    FunctionBody? StepRegion = null,
    ProofExpression? Invariant = null);

public sealed record ProofExpression(
    string Op,
    TypeRef Type,
    ImmutableArray<ProofExpression> Args,
    string? ReferenceId = null,
    string? NumberValue = null,
    bool? BoolValue = null,
    string? RecordType = null,
    ImmutableArray<string> FieldIds = default,
    TypeRef? ElementType = null,
    int? Capacity = null,
    int? Position = null,
    string? BinderId = null,
    ProofExpression? Sequence = null,
    ProofExpression? Body = null);

public sealed record NodeMetadata(
    string? Value,
    string? RecordType,
    ImmutableArray<string> FieldIds,
    string? FieldId,
    TypeRef? ElementType,
    int? Capacity,
    string? FunctionRef)
{
    public static NodeMetadata Empty { get; } = new(
        null,
        null,
        ImmutableArray<string>.Empty,
        null,
        null,
        null,
        null);
}

public sealed class ModuleIr
{
    private readonly byte[] canonicalBytes;

    internal ModuleIr(
        string sourceSchema,
        string moduleId,
        string sourceDigest,
        ImmutableArray<TypeDecl> types,
        ImmutableArray<ImportDecl> imports,
        ImmutableArray<FunctionIr> functions,
        ImmutableArray<string> exports,
        byte[] canonicalBytes)
    {
        SourceSchema = sourceSchema;
        ModuleId = moduleId;
        SourceDigest = sourceDigest;
        Types = types;
        Imports = imports;
        Functions = functions;
        Exports = exports;
        this.canonicalBytes = canonicalBytes.ToArray();
    }

    public string SourceSchema { get; }
    public string ModuleId { get; }
    public string SourceDigest { get; }
    public ImmutableArray<TypeDecl> Types { get; }
    public ImmutableArray<ImportDecl> Imports { get; }
    public ImmutableArray<FunctionIr> Functions { get; }
    public ImmutableArray<string> Exports { get; }
    public byte[] CanonicalBytes => canonicalBytes.ToArray();
    internal ReadOnlySpan<byte> CanonicalBytesSpan => canonicalBytes;
    public string FunctionCountDigest => CanonicalJson.RawDigest(
        Encoding.UTF8.GetBytes($"{StrogoVersions.IrVersion}/function-count\n{Functions.Length.ToString(CultureInfo.InvariantCulture)}"));

    internal ModuleIr WithCanonicalBytes(byte[] bytes) => new(
        SourceSchema, ModuleId, SourceDigest, Types, Imports, Functions, Exports, bytes);
}

public sealed record FunctionIr(
    string Id,
    ImmutableArray<FunctionParameter> Parameters,
    TypeRef ReturnType,
    string ContractRef,
    ImmutableArray<IrInstruction> Instructions,
    int ResultIndex);

public sealed record IrInstruction(
    int DestinationIndex,
    string OriginNodeId,
    string Op,
    ImmutableArray<int> OperandIndices,
    TypeRef Type,
    NodeMetadata Metadata,
    RegionIr? ThenRegion = null,
    RegionIr? ElseRegion = null,
    FoldRegionIr? Fold = null);

public sealed record FoldRegionIr(
    RegionIr Step,
    ProofExpression Invariant);

public sealed record RegionIr(
    ImmutableArray<FunctionParameter> Parameters,
    ImmutableArray<IrInstruction> Instructions,
    int ResultIndex);

public sealed class ModuleParseResult
{
    private readonly byte[] canonicalSource;

    internal ModuleParseResult(
        ModuleSource source,
        ImmutableArray<ModuleToken> tokens,
        byte[] canonicalSource,
        string sourceDigest,
        string stage)
    {
        Source = source;
        Tokens = tokens;
        this.canonicalSource = canonicalSource.ToArray();
        SourceDigest = sourceDigest;
        Stage = stage;
    }

    public ModuleSource Source { get; }
    public ImmutableArray<ModuleToken> Tokens { get; }
    public byte[] CanonicalSource => canonicalSource.ToArray();
    internal ReadOnlySpan<byte> CanonicalSourceBytes => canonicalSource;
    public string SourceDigest { get; }
    public string Stage { get; }
}

public sealed record ModuleCompileResult(
    ModuleIr Ir,
    string CompilerVersion,
    ImmutableArray<ModuleCompileError> Diagnostics);

public sealed record ModuleCompileError(string Stage, string Code, string? EntityId, string Message);

public enum ModuleCompileStatus { Accepted, Rejected }
