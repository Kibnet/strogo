using System.Collections.Immutable;
using Kernel.Core;
using System.Text.Json.Serialization;

namespace Kernel.Host;

public static class HostRights
{
    public const string ReadResource = "ReadResource", ExecuteResource = "ExecuteResource",
        StateWrite = "StateWrite", ReplayResource = "ReplayResource", ReadProgram = "ReadProgram", EditProgram = "EditProgram";
    public static readonly ImmutableArray<string> All = [ReadResource, ExecuteResource, StateWrite, ReplayResource, ReadProgram, EditProgram];
}

public sealed record PrincipalGrant(string Principal, ImmutableArray<string> Rights);
public sealed record EditScope(ImmutableArray<string> ExistingNodeIds, string AddNodePrefix = "n.", bool AllowOutputs = true);
public sealed record HostPolicy(string ResourceId, ImmutableArray<PrincipalGrant> Grants, EditScope EditScope,
    bool StateWrite = true, CoreLimits? Limits = null)
{
    [JsonIgnore] public CoreLimits EffectiveLimits => Limits ?? new CoreLimits();
    public static HostPolicy Default(string principal = "agent") => new("item-001",
        [new(principal, HostRights.All)], new(["n.available", "n.quantity", "n.zero", "n.enough", "n.debit", "n.remaining"]));
}

public sealed record HostManifest(string ProfileId = "reserve.v0", string SemanticsVersion = KernelVersions.Semantics,
    string EncoderVersion = KernelVersions.Encoder, string ReferenceVersion = KernelVersions.Reference,
    string InterpreterVersion = KernelVersions.Interpreter, string LoweringVersion = KernelVersions.Lowering,
    string RuntimeIdentityTag = "kernel.v0.host.1");

/// <summary>Trusted harness seam. No member is accepted by the agent JSON protocol.</summary>
public sealed class HostTestHooks
{
    public Func<string, Task>? OnPointAsync { get; set; }
    internal Task Point(string name) => OnPointAsync?.Invoke(name) ?? Task.CompletedTask;
}

public sealed record HostOptions(HostTestHooks? Hooks = null, int MaxPrepared = 128,
    int WorkerTimeoutMilliseconds = 2000, int CommitWaitMilliseconds = 2000,
    string RuntimeIdentityTag = "kernel.v0.host.1");

public sealed record ReserveEvent(string EventId, string ResourceId, string Kind, long Quantity);
public sealed record PrepareRequest(ReserveEvent Event, string ExpectedStateRevision,
    string ExpectedProgramRevision, string ExpectedPolicyRevision);
public sealed record ResourceSnapshot(string ResourceId, long Available, string StateRevision,
    string ProgramRevision, string PolicyRevision, string ManifestRevision);
public sealed record PreparedPreview(string PrepareId, string ResourceId, string EventId, long OldAvailable,
    ReserveOutput Output, string BaseStateRevision, string ProgramRevision, string PolicyRevision,
    string ManifestRevision, int FuelUsed, int FuelLimit);
public sealed record PrepareResult(string Status, PreparedPreview? Prepared, EventReceipt? Receipt);
public sealed record CommitResult(string Status, EventReceipt Receipt);

public sealed record EventReceipt(string ReceiptId, string ResourceId, ReserveEvent Event, string EventDigest,
    string PreviousStateRevision, string CommittedStateRevision, string InputRef, string OutputRef,
    string ProgramRef, string IrRef, string PolicyRef, string ManifestRef, string AdmissionRef,
    string SemanticsVersion, string RuntimeIdentity, int FuelUsed, int FuelLimit, string TraceRef,
    ReserveOutput Output);
public sealed record PatchReceipt(string ReceiptId, string PatchId, string ProgramId, string PatchDigest,
    string PreviousProgramRevision, string ProgramRevision, string PolicyRevision, string ManifestRevision,
    string AdmissionRef, string Status);
public sealed record ReplayResult(string Status, EventReceipt Receipt, ReserveOutput Output, int FuelUsed, string TraceRef);
public sealed record Explanation(string ArtifactId, string Status, string Text, object Details);
public sealed record Provenance(string Principal, string RecordedAtUtc, string OperationKind);

/// <summary>The complete agent capability surface. The principal is transport-owned.</summary>
public sealed class KernelClient
{
    private readonly KernelHost host;
    private readonly string principal;
    internal KernelClient(KernelHost host, string principal) { this.host = host; this.principal = principal; }
    public ResourceSnapshot Snapshot(string resourceId = "item-001") => host.Snapshot(principal, resourceId);
    public Task<PatchReceipt> ProposePatchAsync(string patchJson, CancellationToken cancellationToken = default) => host.ProposePatchAsync(principal, patchJson, cancellationToken);
    public Task<PrepareResult> PrepareAsync(PrepareRequest request, CancellationToken cancellationToken = default) => host.PrepareAsync(principal, request, cancellationToken);
    public Task<CommitResult> CommitAsync(string prepareId, CancellationToken cancellationToken = default) => host.CommitAsync(principal, prepareId, cancellationToken);
    public ReplayResult Replay(string receiptId) => host.Replay(principal, receiptId);
    public Explanation Explain(string artifactId) => host.Explain(principal, artifactId);
}
