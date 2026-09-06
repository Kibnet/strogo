using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kernel.Core;
using Microsoft.Data.Sqlite;

namespace Kernel.Host;

internal sealed record AdmissionArtifact(string ProgramRevision, string IrRevision, string PolicyRevision,
    string ManifestRevision, string RuntimeIdentity, string EvidenceBase64);
internal sealed record PreparedRecord(string Id, string Principal, string Epoch, ReserveEvent Event, string EventDigest,
    ResourceRow Snapshot, ReserveOutput Output, string InputJson, string OutputJson, string TraceJson, int FuelUsed,
    int FuelLimit, string IrRef, string AdmissionRef, string RuntimeIdentity, long Sequence);

/// <summary>Trusted local host. Bind returns the sole agent surface; setup/admin methods are harness-only.</summary>
public sealed partial class KernelHost : IDisposable, IAsyncDisposable
{
    private readonly HostStorage storage;
    private readonly ISolver solver;
    private readonly SolverIdentity solverIdentity;
    private readonly HostOptions options;
    private readonly string coreAssemblyDigest;
    private readonly string hostAssemblyDigest;
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly object pendingLock = new();
    private readonly object epochLock = new();
    private readonly Dictionary<string, PreparedRecord> pending = new(StringComparer.Ordinal);
    private readonly string epoch = Guid.NewGuid().ToString("N");
    private long sequence;
    private volatile bool disposed;
    private static readonly Regex IdPattern = new("^[a-z][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private KernelHost(string path, ISolver solver, HostOptions options)
    {
        if (options.MaxPrepared <= 0 || options.WorkerTimeoutMilliseconds <= 0 || options.CommitWaitMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        this.solver = solver; solverIdentity = solver.Identity; this.options = options;
        coreAssemblyDigest = AssemblyDigest(typeof(KernelProgram).Assembly.Location);
        hostAssemblyDigest = AssemblyDigest(typeof(KernelHost).Assembly.Location);
        DatabasePath = Path.GetFullPath(path); storage = new(DatabasePath, options.CommitWaitMilliseconds);
    }
    public string DatabasePath { get; }
    public static Task<KernelHost> OpenAsync(string databasePath, ISolver solver, HostOptions? options = null)
        => Task.FromResult(new KernelHost(databasePath, solver, options ?? new()));
    public KernelClient Bind(string principal)
    { EnsureAlive(); if (string.IsNullOrEmpty(principal)) throw Error("authorization", "AccessDenied"); return new(this, principal); }
    public void Dispose()
    {
        lock (epochLock) { if (disposed) return; disposed = true; lifetime.Cancel(); }
        lock (pendingLock) pending.Clear();
        // Pending workers hold no mutable store reference; their late results are discarded by epoch cancellation.
    }
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    private void EnsureAlive() { if (disposed || lifetime.IsCancellationRequested) throw Error("host", "AdmissionInvalidated"); }
    private static string AssemblyDigest(string path)
    {
        if (string.IsNullOrEmpty(path)) throw Error("host", "RuntimeIdentityUnavailable");
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
    }
    private void CommitSql(SqliteConnection connection, Action? beforeExecute = null)
    {
        // Epoch retirement and the commit linearization point cannot pass each other unchecked.
        lock (epochLock) { EnsureAlive(); beforeExecute?.Invoke(); HostStorage.Execute(connection, "COMMIT"); }
    }
    internal static KernelException Error(string stage, string code, string? id = null, object? details = null,
        string? program = null, string? state = null, string? policy = null)
    {
        var repairs = code switch
        {
            "EventIdConflict" => ImmutableArray.Create("UseNewEventIdForDifferentIntent"),
            "StateConflict" or "ProgramChanged" or "ProgramConflict" or "PolicyChanged" or "PrepareExpired" or "OutcomeUnknown" => ImmutableArray.Create("RetryWithFreshSnapshot"),
            "EditScopeDenied" => ImmutableArray.Create("EditAllowedNodes"),
            _ => ImmutableArray<string>.Empty
        };
        return new(KernelError.Create(stage, code, id, details, program) with { StateRevision = state, PolicyRevision = policy, AllowedRepairs = repairs });
    }
    private string RuntimeIdentity => HostJson.Text(new { semanticsVersion = KernelVersions.Semantics,
        encoderVersion = KernelVersions.Encoder, referenceVersion = KernelVersions.Reference,
        interpreterVersion = KernelVersions.Interpreter, loweringVersion = KernelVersions.Lowering,
        solver = solverIdentity, coreAssemblyDigest, hostAssemblyDigest, runtimeIdentityTag = options.RuntimeIdentityTag });
    private Task Point(string name) => options.Hooks?.Point(name) ?? Task.CompletedTask;
    private async Task EnterWriter(CancellationToken token)
    {
        EnsureAlive();
        if (!await writer.WaitAsync(options.CommitWaitMilliseconds, token).ConfigureAwait(false)) throw Error("storage", "CommitBusy");
    }
    private static HostPolicy Normalize(HostPolicy p)
    {
        if (p.ResourceId != "item-001" || p.EffectiveLimits.Fuel <= 0 || p.EffectiveLimits.MaxNodes <= 0 ||
            p.EffectiveLimits.MaxTransportBytes <= 0 || p.EffectiveLimits.MaxJsonDepth <= 0 ||
            p.EffectiveLimits.SolverTimeoutMilliseconds <= 0 || p.EffectiveLimits.AdmissionTimeoutMilliseconds <= 0)
            throw Error("policy", "PolicyInvalid");
        if (p.Grants.Select(g => g.Principal).Distinct(StringComparer.Ordinal).Count() != p.Grants.Length ||
            p.Grants.Any(g => string.IsNullOrEmpty(g.Principal) || g.Rights.Any(r => !HostRights.All.Contains(r)))) throw Error("policy", "PolicyInvalid");
        if (string.IsNullOrEmpty(p.EditScope.AddNodePrefix) || p.EditScope.ExistingNodeIds.Any(id => !IdPattern.IsMatch(id))) throw Error("policy", "PolicyInvalid");
        return p with { Limits = p.EffectiveLimits,
            Grants = p.Grants.OrderBy(g => g.Principal, StringComparer.Ordinal).Select(g => g with { Rights = g.Rights.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray() }).ToImmutableArray(),
            EditScope = p.EditScope with { ExistingNodeIds = p.EditScope.ExistingNodeIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray() } };
    }
    private static bool Has(HostPolicy policy, string principal, string right) => policy.Grants.Any(g => g.Principal == principal && g.Rights.Contains(right));
    private static void Authorize(HostPolicy policy, ResourceRow row, string principal, string resourceId, params string[] rights)
    {
        if (row.ResourceId != resourceId || policy.ResourceId != resourceId || rights.Any(r => !Has(policy, principal, r))) throw Error("authorization", "AccessDenied");
    }
    private static ResourceRow RequiredResource(SqliteConnection c) => HostStorage.Resource(c) ?? throw Error("authorization", "AccessDenied");
    private static void StateWrite(HostPolicy policy, ResourceRow row, string principal)
    {
        Authorize(policy, row, principal, row.ResourceId, HostRights.StateWrite);
        if (!policy.StateWrite) throw Error("authorization", "CapabilityDenied");
    }
    private static object EventPayload(ReserveEvent e) => new { eventId = e.EventId, resourceId = e.ResourceId, kind = e.Kind, quantity = e.Quantity };
    private static string EventDigest(ReserveEvent e) => CanonicalJson.Hash("event", EventPayload(e));
    private static object InputPayload(ResourceRow s, ReserveEvent e) => new { state = new { resourceId = s.ResourceId, stateRevision = s.StateRevision, available = s.Available }, @event = EventPayload(e) };
    private static object StatePayload(ResourceRow row, string eventDigest, ReserveOutput output) => new { resourceId = row.ResourceId,
        previousRevision = row.StateRevision, eventDigest, programRevision = row.ProgramRevision,
        policyRevision = row.PolicyRevision, manifestRevision = row.ManifestRevision, output };
    private static void ValidateEvent(ReserveEvent e)
    {
        if (!IdPattern.IsMatch(e.EventId) || !IdPattern.IsMatch(e.ResourceId) || e.Kind != "reserve") throw Error("schema", "SchemaInvalid");
    }
    private static void ExpectedDigest(string d) { if (!DigestPattern.IsMatch(d)) throw Error("schema", "InvalidRevision"); }
    private static EventReceipt? ExistingEvent(SqliteConnection c, ReserveEvent e, string digest)
    {
        var receipt = HostStorage.Receipt(c, e.ResourceId, e.EventId);
        if (receipt is null) return null;
        if (receipt.EventDigest != digest) throw Error("commit", "EventIdConflict", e.EventId);
        VerifyReceiptArtifact(c, receipt, "StorageCorrupt");
        return receipt;
    }
    private void CheckManifest(HostManifest manifest)
    {
        if (solver.Identity != solverIdentity) throw Error("admission", "AdmissionInvalidated");
        if (manifest.ProfileId != "reserve.v0" || manifest.SemanticsVersion != KernelVersions.Semantics ||
            manifest.EncoderVersion != KernelVersions.Encoder || manifest.ReferenceVersion != KernelVersions.Reference ||
            manifest.InterpreterVersion != KernelVersions.Interpreter || manifest.LoweringVersion != KernelVersions.Lowering ||
            manifest.RuntimeIdentityTag != options.RuntimeIdentityTag) throw Error("admission", "AdmissionInvalidated");
    }
    private AdmissionArtifact CheckAdmission(SqliteConnection c, ResourceRow row)
    {
        CheckManifest(HostStorage.Manifest(c, row));
        var artifact = HostStorage.GetArtifact(c, row.AdmissionRef);
        if (artifact.Kind != "admission") throw Error("admission", "AdmissionInvalidated");
        var admission = HostJson.Read<AdmissionArtifact>(artifact.Json);
        if (admission.ProgramRevision != row.ProgramRevision || admission.PolicyRevision != row.PolicyRevision ||
            admission.ManifestRevision != row.ManifestRevision || admission.RuntimeIdentity != RuntimeIdentity)
            throw Error("admission", "AdmissionInvalidated");
        var evidence = ReadEvidence(admission);
        ValidateEvidence(evidence, admission);
        if (evidence.Solver != solverIdentity) throw Error("admission", "AdmissionInvalidated");
        var irArtifact = HostStorage.GetArtifact(c, admission.IrRevision);
        if (irArtifact.Kind != "ir" || IrCodec.Parse(irArtifact.Json).ProgramRevision != row.ProgramRevision) throw Error("admission", "AdmissionInvalidated");
        return admission;
    }
    private static VerificationEvidence ReadEvidence(AdmissionArtifact admission)
    {
        try { return HostJson.Read<VerificationEvidence>(Encoding.UTF8.GetString(Convert.FromBase64String(admission.EvidenceBase64))); }
        catch (Exception e) when (e is FormatException or JsonException) { throw Error("admission", "AdmissionInvalidated"); }
    }
    private static void ValidateEvidence(VerificationEvidence e, AdmissionArtifact a)
    {
        if (e.ProgramRevision != a.ProgramRevision || e.IrRevision != a.IrRevision || e.PolicyRevision != a.PolicyRevision ||
            e.ManifestRevision != a.ManifestRevision || e.SemanticsVersion != KernelVersions.Semantics ||
            e.EncoderVersion != KernelVersions.Encoder || e.ReferenceVersion != KernelVersions.Reference ||
            e.InterpreterVersion != KernelVersions.Interpreter || e.LoweringVersion != KernelVersions.Lowering ||
            e.Obligations.Length != 2 || e.Obligations.Count(o => o.Name == "domain" && o.Status == "sat") != 1 || e.Obligations.Count(o => o.Name == "counterexample" && o.Status == "unsat") != 1 ||
            e.Obligations.Any(o => o.ExitCode != 0 || CanonicalJson.RawDigest(Encoding.UTF8.GetBytes(o.Query)) != o.QueryDigest))
            throw Error("admission", "AdmissionInvalidated");
    }
    private async Task<(ValidatedProgram Program, VerificationResult Verification, AdmissionArtifact Admission)> VerifyAsync(
        string json, HostPolicy policy, HostManifest manifest, CancellationToken token)
    {
        CheckManifest(manifest);
        var graph = GraphValidator.Validate(ProgramCodec.Parse(json, policy.EffectiveLimits), policy.EffectiveLimits);
        var policyRef = CanonicalJson.Hash("policy", policy); var manifestRef = CanonicalJson.Hash("manifest", manifest);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        VerificationResult result;
        try
        {
            var task = new AdmissionVerifier(solver, policy.EffectiveLimits).VerifyAsync(graph, new(policyRef, manifestRef), linked.Token);
            result = await task.WaitAsync(TimeSpan.FromMilliseconds(policy.EffectiveLimits.AdmissionTimeoutMilliseconds), linked.Token).ConfigureAwait(false);
        }
        catch (TimeoutException) { linked.Cancel(); throw Error("admission", "VerificationTimeout"); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw Error("admission", "AdmissionInvalidated"); }
        EnsureAlive(); token.ThrowIfCancellationRequested();
        if (!result.IsVerified) throw new KernelException(result.Error ?? KernelError.Create("admission", "VerificationFailed", details: new { status = result.Status.ToString() }));
        var evidenceBytes = JsonSerializer.SerializeToUtf8Bytes(result.Evidence, CanonicalJson.SerializerOptions);
        var admission = new AdmissionArtifact(graph.Revision, IrCodec.Revision(result.Ir), policyRef, manifestRef,
            RuntimeIdentity, Convert.ToBase64String(evidenceBytes));
        ValidateEvidence(result.Evidence, admission);
        if (result.Evidence.Solver != solverIdentity || solver.Identity != solverIdentity) throw Error("admission", "AdmissionInvalidated");
        return (graph, result, admission);
    }
    private static string PersistAdmission(SqliteConnection c, string resourceId, HostPolicy policy, HostManifest manifest,
        ValidatedProgram graph, VerificationResult verification, AdmissionArtifact admission)
    {
        var programJson = Encoding.UTF8.GetString(ProgramCodec.CanonicalBytes(graph.Program));
        var irJson = Encoding.UTF8.GetString(IrCodec.CanonicalBytes(verification.Ir));
        HostStorage.Artifact(c, graph.Revision, "program", programJson, resourceId);
        HostStorage.Execute(c, "INSERT OR IGNORE INTO programs(ref,json) VALUES($r,$j)", ("$r", graph.Revision), ("$j", programJson));
        HostStorage.Artifact(c, admission.IrRevision, "ir", irJson, resourceId);
        HostStorage.Artifact(c, admission.PolicyRevision, "policy", HostJson.Text(policy), resourceId);
        HostStorage.Artifact(c, admission.ManifestRevision, "manifest", HostJson.Text(manifest), resourceId);
        var admissionRef = CanonicalJson.Hash("admission", admission);
        HostStorage.Artifact(c, admissionRef, "admission", HostJson.Text(admission), resourceId);
        HostStorage.Execute(c, "INSERT OR IGNORE INTO admissions(ref,program_ref,ir_ref,policy_ref,manifest_ref,runtime_identity) VALUES($a,$p,$i,$y,$m,$r)",
            ("$a", admissionRef), ("$p", graph.Revision), ("$i", admission.IrRevision), ("$y", admission.PolicyRevision), ("$m", admission.ManifestRevision), ("$r", admission.RuntimeIdentity));
        return admissionRef;
    }

    public async Task InitializeAsync(string programJson, long initialAvailable = 10, HostPolicy? policy = null)
    {
        EnsureAlive();
        if (initialAvailable < 0) throw Error("input", "StateInvariantFailed");
        var p = Normalize(policy ?? HostPolicy.Default()); var manifest = new HostManifest(RuntimeIdentityTag: options.RuntimeIdentityTag);
        using (var check = storage.Open()) if (HostStorage.Resource(check) is not null) throw Error("storage", "AlreadyInitialized");
        var verified = await VerifyAsync(programJson, p, manifest, lifetime.Token).ConfigureAwait(false);
        await EnterWriter(lifetime.Token).ConfigureAwait(false);
        try
        {
            using var c = storage.Open(); HostStorage.Execute(c, "BEGIN IMMEDIATE");
            try
            {
                EnsureAlive();
                if (HostStorage.Resource(c) is not null) throw Error("storage", "AlreadyInitialized");
                var admissionRef = PersistAdmission(c, p.ResourceId, p, manifest, verified.Program, verified.Verification, verified.Admission);
                var genesis = new { resourceId = p.ResourceId, initialAvailable };
                var genesisRef = CanonicalJson.Hash("genesis", genesis);
                HostStorage.Artifact(c, genesisRef, "genesis", HostJson.Text(genesis), p.ResourceId);
                HostStorage.Execute(c, "INSERT INTO resources(resource_id,available,state_revision,genesis_revision,initial_available,program_revision,policy_revision,manifest_revision,admission_ref) VALUES($id,$s,$g,$g,$s,$p,$y,$m,$a)",
                    ("$id", p.ResourceId), ("$s", HostJson.Number(initialAvailable)), ("$g", genesisRef), ("$p", verified.Program.Revision),
                    ("$y", verified.Admission.PolicyRevision), ("$m", verified.Admission.ManifestRevision), ("$a", admissionRef));
                CommitSql(c);
            }
            catch { HostStorage.Rollback(c); throw; }
        }
        finally { writer.Release(); }
    }
    public async Task SetPolicyAsync(HostPolicy policy, CancellationToken token = default)
    {
        var normalized = Normalize(policy); await EnterWriter(token).ConfigureAwait(false);
        try
        {
            using var c = storage.Open(); HostStorage.Execute(c, "BEGIN IMMEDIATE");
            try
            {
                EnsureAlive(); var row = RequiredResource(c);
                if (normalized.ResourceId != row.ResourceId) throw Error("policy", "PolicyInvalid");
                var reference = CanonicalJson.Hash("policy", normalized);
                HostStorage.Artifact(c, reference, "policy", HostJson.Text(normalized), row.ResourceId);
                HostStorage.Execute(c, "UPDATE resources SET policy_revision=$p WHERE resource_id=$r", ("$p", reference), ("$r", row.ResourceId));
                CommitSql(c);
            }
            catch { HostStorage.Rollback(c); throw; }
        }
        finally { writer.Release(); }
    }
    public async Task SetManifestAsync(HostManifest manifest, CancellationToken token = default)
    {
        await EnterWriter(token).ConfigureAwait(false);
        try
        {
            using var c = storage.Open(); HostStorage.Execute(c, "BEGIN IMMEDIATE");
            try
            {
                EnsureAlive(); var row = RequiredResource(c); var reference = CanonicalJson.Hash("manifest", manifest);
                HostStorage.Artifact(c, reference, "manifest", HostJson.Text(manifest), row.ResourceId);
                HostStorage.Execute(c, "UPDATE resources SET manifest_revision=$m WHERE resource_id=$r", ("$m", reference), ("$r", row.ResourceId));
                CommitSql(c);
            }
            catch { HostStorage.Rollback(c); throw; }
        }
        finally { writer.Release(); }
    }
    public async Task ReadmitAsync(CancellationToken token = default)
    {
        ResourceRow before; HostPolicy policy; HostManifest manifest; string program;
        using (var c = storage.Open())
        {
            HostStorage.Execute(c, "BEGIN"); before = RequiredResource(c); policy = HostStorage.Policy(c, before);
            manifest = HostStorage.Manifest(c, before); program = HostStorage.GetArtifact(c, before.ProgramRevision).Json; HostStorage.Execute(c, "COMMIT");
        }
        var verified = await VerifyAsync(program, policy, manifest, token).ConfigureAwait(false);
        await EnterWriter(token).ConfigureAwait(false);
        try
        {
            using var c = storage.Open(); HostStorage.Execute(c, "BEGIN IMMEDIATE");
            try
            {
                EnsureAlive(); var current = RequiredResource(c); CheckTrustPointers(before, current);
                var reference = PersistAdmission(c, current.ResourceId, policy, manifest, verified.Program, verified.Verification, verified.Admission);
                HostStorage.Execute(c, "UPDATE resources SET admission_ref=$a WHERE resource_id=$r", ("$a", reference), ("$r", current.ResourceId));
                CommitSql(c);
            }
            catch { HostStorage.Rollback(c); throw; }
        }
        finally { writer.Release(); }
    }
    private static void CheckTrustPointers(ResourceRow before, ResourceRow current)
    {
        if (before.PolicyRevision != current.PolicyRevision) throw Error("commit", "PolicyChanged");
        if (before.ProgramRevision != current.ProgramRevision) throw Error("commit", "ProgramChanged");
        if (before.ManifestRevision != current.ManifestRevision) throw Error("commit", "AdmissionInvalidated");
    }
    internal ResourceSnapshot Snapshot(string principal, string resourceId)
    {
        EnsureAlive(); using var c = storage.Open(); HostStorage.Execute(c, "BEGIN");
        try
        {
            var row = RequiredResource(c); Authorize(HostStorage.Policy(c, row), row, principal, resourceId, HostRights.ReadResource);
            HostStorage.Execute(c, "COMMIT"); return row.Snapshot;
        }
        catch { HostStorage.Rollback(c); throw; }
    }
}
