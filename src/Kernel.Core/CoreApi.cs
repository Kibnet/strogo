using System.Collections.Immutable;
using System.Text.Json;

namespace Kernel.Core;

public static class KernelVersions
{
    public const string Schema = "kernel.v0";
    public const string Semantics = "kernel.v0";
    public const string Encoder = "kernel.v0.encoder.1";
    public const string Reference = "kernel.v0.reference.1";
    public const string Interpreter = "kernel.v0.ir-interpreter.1";
    public const string Lowering = "kernel.v0.lowering.1";
}

public sealed record CoreLimits(int MaxTransportBytes = 65536, int MaxJsonDepth = 32,
    int MaxNodes = 128, int Fuel = 128, int SolverTimeoutMilliseconds = 5000,
    int AdmissionTimeoutMilliseconds = 15000);

public enum KernelType { I64, Bool }

public sealed record KernelNode(string Id, string Op, KernelType Type,
    ImmutableArray<string> Args, string? FieldId = null, long? I64Value = null,
    bool? BoolValue = null);

public sealed record OutputRefs(string Accepted, string Available, string Reserved);
public sealed record KernelProgram(string SchemaVersion, string ProgramId, string ProfileId,
    ImmutableArray<KernelNode> Nodes, OutputRefs Outputs);

public sealed class ValidatedProgram
{
    internal ValidatedProgram(KernelProgram program, ImmutableArray<KernelNode> order)
    {
        Program = program;
        TopologicalNodes = order;
        Revision = ProgramCodec.Revision(program);
    }
    public KernelProgram Program { get; }
    public ImmutableArray<KernelNode> TopologicalNodes { get; }
    public string Revision { get; }
}

public sealed record ReserveInput(long Available, long Quantity);
public sealed record ReserveOutput(bool Accepted, long Available, long Reserved);

public readonly record struct KernelValue(KernelType Type, long Number, bool Boolean)
{
    public static KernelValue I64(long value) => new(KernelType.I64, value, false);
    public static KernelValue Bool(bool value) => new(KernelType.Bool, 0, value);
}

public sealed record TraceEntry(string NodeId, string Opcode, ImmutableArray<KernelValue> Operands,
    KernelValue? Value, string? ErrorCode = null);

public sealed record KernelError(string SchemaVersion, string Stage, string Code,
    string? EntityId, string? ProgramRevision, string? StateRevision, string? PolicyRevision,
    JsonElement Details, ReserveInput? Witness, ImmutableArray<string> AllowedRepairs)
{
    public static KernelError Create(string stage, string code, string? entityId = null,
        object? details = null, string? programRevision = null, ReserveInput? witness = null) =>
        new(KernelVersions.Schema, stage, code, entityId, programRevision, null, null,
            JsonSerializer.SerializeToElement(details ?? new { }, CanonicalJson.SerializerOptions),
            witness, ImmutableArray<string>.Empty);
}

public sealed class KernelException : Exception
{
    public KernelException(KernelError error) : base($"{error.Stage}: {error.Code} ({error.EntityId ?? "-"})") => Error = error;
    public KernelError Error { get; }
}

public sealed record EvaluationResult(ReserveOutput? Output, ImmutableArray<TraceEntry> Trace,
    int UsedFuel, KernelError? Error)
{
    public bool Success => Error is null && Output is not null;
}

public sealed record IrInstruction(int DestinationIndex, string OriginNodeId, string Opcode,
    ImmutableArray<int> OperandIndices, KernelType Type, string? FieldId = null,
    long? I64Value = null, bool? BoolValue = null);
public sealed record IrOutputRefs(int Accepted, int Available, int Reserved);
public sealed record IrProgram(string SemanticsVersion, string ProgramRevision,
    ImmutableArray<IrInstruction> Instructions, IrOutputRefs Outputs);

public sealed record SolverIdentity(string Version, string BinaryDigest);
public sealed record SolverResponse(int ExitCode, string Stdout, string Stderr, bool TimedOut = false);

/// <summary>Trusted host/test seam; never deserialize an implementation or solver options from agent input.</summary>
public interface ISolver
{
    SolverIdentity Identity { get; }
    Task<SolverResponse> SolveAsync(string query, TimeSpan timeout, CancellationToken cancellationToken = default);
}

public enum VerificationStatus { Verified, Counterexample, Unknown, Timeout, Error }
public sealed record VerificationContext(string PolicyRevision, string ManifestRevision);
public sealed record ObligationEvidence(string Name, string QueryDigest, string Status,
    string Query, string Stdout, string Stderr, int ExitCode);
public sealed record VerificationEvidence(string SemanticsVersion, string ProgramRevision,
    string IrRevision, string PolicyRevision, string ManifestRevision, string EncoderVersion,
    string ReferenceVersion, string InterpreterVersion, string LoweringVersion,
    SolverIdentity Solver, ImmutableArray<ObligationEvidence> Obligations);
public sealed record VerificationResult(VerificationStatus Status, IrProgram Ir,
    VerificationEvidence Evidence, KernelError? Error = null, ReserveInput? Witness = null,
    EvaluationResult? WitnessReplay = null)
{
    public bool IsVerified => Status == VerificationStatus.Verified && Error is null;
}

/// <summary>Trusted arithmetic conformance fixture, outside the public Reserve program schema.</summary>
public sealed record ArithmeticProbeResult(VerificationStatus Status, long? Witness,
    ImmutableArray<ObligationEvidence> Obligations, KernelError? Error = null);
