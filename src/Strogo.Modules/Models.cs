using System.Collections.Immutable;
using Kernel.Core;

namespace Strogo.Modules;

public sealed record StrogoLimits
{
    public int MaxTransportBytes { get; init; } = 1_048_576;
    public int MaxJsonDepth { get; init; } = 64;
    public int MaxTypes { get; init; } = 256;
    public int MaxFunctions { get; init; } = 128;
    public int MaxNodesPerFunction { get; init; } = 512;
    public int MaxTypeDepth { get; init; } = 16;
    public int MaxIdLength { get; init; } = 64;
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
    string? Value);

public sealed record ModuleIr(string SourceSchema, string ModuleId, string SourceDigest, ImmutableArray<FunctionIr> Functions, byte[] CanonicalBytes)
{
    public string FunctionCountDigest => CanonicalJson.Hash("module-ir-function-count", Functions.Length.ToString());
}

public sealed record FunctionIr(
    string Id,
    ImmutableArray<string> Parameters,
    TypeRef ReturnType,
    ImmutableArray<IrInstruction> Instructions,
    int ResultIndex);

public sealed record IrInstruction(
    int DestinationIndex,
    string OriginNodeId,
    string Op,
    ImmutableArray<int> OperandIndices,
    TypeRef Type,
    string? Value);

public sealed record ModuleParseResult(
    ModuleSource Source,
    ImmutableArray<ModuleToken> Tokens,
    byte[] CanonicalSource,
    string SourceDigest,
    string Stage);

public sealed record ModuleCompileResult(
    ModuleIr Ir,
    string CompilerVersion,
    ImmutableArray<ModuleCompileError> Diagnostics);

public sealed record ModuleCompileError(string Stage, string Code, string? EntityId, string Message);

public enum ModuleCompileStatus { Accepted, Rejected }
