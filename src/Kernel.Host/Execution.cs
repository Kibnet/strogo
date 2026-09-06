using System.Text;
using Kernel.Core;
using Microsoft.Data.Sqlite;

namespace Kernel.Host;

public sealed partial class KernelHost
{
    internal async Task<PrepareResult> PrepareAsync(string principal, PrepareRequest request, CancellationToken token)
    {
        EnsureAlive(); ResourceRow row; HostPolicy policy; AdmissionArtifact admission; IrProgram ir;
        var e = request.Event;
        string eventDigest;
        using (var c = storage.Open())
        {
            HostStorage.Execute(c, "BEGIN");
            try
            {
                row = RequiredResource(c); policy = HostStorage.Policy(c, row);
                Authorize(policy, row, principal, e.ResourceId, HostRights.ReadResource, HostRights.ExecuteResource);
                ValidateEvent(e); eventDigest = EventDigest(e);
                var existing = ExistingEvent(c, e, eventDigest);
                if (existing is not null) { HostStorage.Execute(c, "COMMIT"); return new("AlreadyCommitted", null, existing); }
                ExpectedDigest(request.ExpectedStateRevision); ExpectedDigest(request.ExpectedProgramRevision); ExpectedDigest(request.ExpectedPolicyRevision);
                if (row.StateRevision != request.ExpectedStateRevision) throw Error("prepare", "StateConflict", e.EventId);
                if (row.ProgramRevision != request.ExpectedProgramRevision) throw Error("prepare", "ProgramChanged");
                if (row.PolicyRevision != request.ExpectedPolicyRevision) throw Error("prepare", "PolicyChanged");
                StateWrite(policy, row, principal); admission = CheckAdmission(c, row);
                ir = IrCodec.Parse(HostStorage.GetArtifact(c, admission.IrRevision).Json);
                var invalid = ReserveContract.CheckInput(new ReserveInput(row.Available, e.Quantity));
                if (invalid is not null) throw new KernelException(invalid);
                HostStorage.Execute(c, "COMMIT");
            }
            catch { HostStorage.Rollback(c); throw; }
        }
        await Point("AfterPrepareSnapshot").ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        var workerToken = linked.Token;
        EvaluationResult evaluated;
        try
        {
            var worker = Task.Run(async () =>
            {
                await Point("BeforeEvaluation").ConfigureAwait(false);
                workerToken.ThrowIfCancellationRequested();
                var result = IrInterpreter.Evaluate(ir, new ReserveInput(row.Available, e.Quantity), policy.EffectiveLimits.Fuel);
                await Point("AfterEvaluation").ConfigureAwait(false);
                return result;
            }, workerToken);
            evaluated = await worker.WaitAsync(TimeSpan.FromMilliseconds(options.WorkerTimeoutMilliseconds), workerToken).ConfigureAwait(false);
        }
        catch (TimeoutException) { linked.Cancel(); throw Error("evaluation", "WorkerTimeout"); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw Error("prepare", "PrepareExpired"); }
        EnsureAlive(); token.ThrowIfCancellationRequested();
        if (!evaluated.Success) throw RuntimeFailure(evaluated.Error ?? KernelError.Create("evaluation", "EvaluationFailed"), new(row.Available, e.Quantity), row, "evaluation");
        var output = evaluated.Output!;
        var contractFailure = ReserveContract.CheckOutput(new ReserveInput(row.Available, e.Quantity), output);
        if (contractFailure is not null) throw RuntimeFailure(contractFailure, new(row.Available, e.Quantity), row, "evaluation");
        var prepared = new PreparedRecord(Guid.NewGuid().ToString("N"), principal, epoch, e, eventDigest, row, output,
            HostJson.Text(InputPayload(row, e)), Encoding.UTF8.GetString(ExecutionCodec.OutputBytes(output)),
            Encoding.UTF8.GetString(ExecutionCodec.TraceBytes(evaluated)), evaluated.UsedFuel, policy.EffectiveLimits.Fuel,
            admission.IrRevision, row.AdmissionRef, admission.RuntimeIdentity, Interlocked.Increment(ref sequence));
        lock (pendingLock)
        {
            EnsureAlive();
            while (pending.Count >= options.MaxPrepared)
            {
                var oldest = pending.Values.MinBy(p => p.Sequence)!; pending.Remove(oldest.Id);
            }
            pending.Add(prepared.Id, prepared);
        }
        return new("Prepared", Preview(prepared), null);
    }
    private static PreparedPreview Preview(PreparedRecord p) => new(p.Id, p.Event.ResourceId, p.Event.EventId,
        p.Snapshot.Available, p.Output, p.Snapshot.StateRevision, p.Snapshot.ProgramRevision,
        p.Snapshot.PolicyRevision, p.Snapshot.ManifestRevision, p.FuelUsed, p.FuelLimit);

    internal async Task<CommitResult> CommitAsync(string principal, string prepareId, CancellationToken token)
    {
        EnsureAlive(); PreparedRecord? prepared;
        lock (pendingLock) pending.TryGetValue(prepareId, out prepared);
        // Unknown tokens disclose nothing until the caller's current resource access has been established.
        if (prepared is null)
        {
            using var read = storage.Open(); var current = RequiredResource(read);
            Authorize(HostStorage.Policy(read, current), current, principal, current.ResourceId, HostRights.ReadResource, HostRights.ExecuteResource);
            throw Error("commit", "PrepareExpired");
        }
        if (prepared.Principal != principal) throw Error("authorization", "AccessDenied");
        await Point("BeforeCommitTransaction").ConfigureAwait(false);
        await EnterWriter(token).ConfigureAwait(false);
        try
        {
            using var c = storage.Open(); bool commitAttempted = false;
            try
            {
                HostStorage.Execute(c, "BEGIN IMMEDIATE");
                var row = RequiredResource(c); var policy = HostStorage.Policy(c, row);
                Authorize(policy, row, principal, prepared.Event.ResourceId, HostRights.ReadResource, HostRights.ExecuteResource);
                var existing = ExistingEvent(c, prepared.Event, prepared.EventDigest);
                if (existing is not null)
                {
                    HostStorage.Execute(c, "ROLLBACK");
                    RemoveCommittedPlans(prepared);
                    return new("AlreadyCommitted", existing);
                }
                if (prepared.Epoch != epoch || lifetime.IsCancellationRequested) throw Error("commit", "PrepareExpired");
                CheckTrustPointers(prepared.Snapshot, row);
                if (row.StateRevision != prepared.Snapshot.StateRevision) throw Error("commit", "StateConflict", prepared.Event.EventId);
                if (row.Available != prepared.Snapshot.Available || row.ResourceId != prepared.Snapshot.ResourceId ||
                    prepared.InputJson != HostJson.Text(InputPayload(row, prepared.Event)) || prepared.EventDigest != EventDigest(prepared.Event))
                    throw Error("commit", "PreparedInputMismatch");
                var admission = CheckAdmission(c, row);
                if (prepared.AdmissionRef != row.AdmissionRef || prepared.IrRef != admission.IrRevision || prepared.RuntimeIdentity != RuntimeIdentity ||
                    prepared.FuelLimit != policy.EffectiveLimits.Fuel || prepared.FuelUsed > prepared.FuelLimit) throw Error("commit", "AdmissionInvalidated");
                StateWrite(policy, row, principal);
                var invalidInput = ReserveContract.CheckInput(new ReserveInput(row.Available, prepared.Event.Quantity));
                var invalidOutput = ReserveContract.CheckOutput(new ReserveInput(row.Available, prepared.Event.Quantity), prepared.Output);
                if (invalidInput is not null) throw new KernelException(invalidInput);
                if (invalidOutput is not null) throw RuntimeFailure(invalidOutput, new(row.Available, prepared.Event.Quantity), row, "commit");
                await Point("AfterCommitChecks").ConfigureAwait(false);
                token.ThrowIfCancellationRequested(); EnsureAlive();
                var inputRef = CanonicalJson.Hash("input", HostJson.Element(prepared.InputJson));
                var outputRef = CanonicalJson.Hash("output", HostJson.Element(prepared.OutputJson));
                var traceRef = CanonicalJson.Hash("trace", HostJson.Element(prepared.TraceJson));
                var newStatePayload = StatePayload(row, prepared.EventDigest, prepared.Output);
                var stateRevision = CanonicalJson.Hash("state", newStatePayload);
                var receipt = new EventReceipt("", row.ResourceId, prepared.Event, prepared.EventDigest, row.StateRevision,
                    stateRevision, inputRef, outputRef, row.ProgramRevision, prepared.IrRef, row.PolicyRevision,
                    row.ManifestRevision, row.AdmissionRef, KernelVersions.Semantics, prepared.RuntimeIdentity,
                    prepared.FuelUsed, prepared.FuelLimit, traceRef, prepared.Output);
                receipt = receipt with { ReceiptId = CanonicalJson.Hash("receipt", ReceiptPayload(receipt)) };
                HostStorage.Artifact(c, prepared.EventDigest, "event", HostJson.Text(EventPayload(prepared.Event)), row.ResourceId);
                HostStorage.Artifact(c, inputRef, "input", prepared.InputJson, row.ResourceId);
                HostStorage.Artifact(c, outputRef, "output", prepared.OutputJson, row.ResourceId);
                HostStorage.Artifact(c, traceRef, "trace", prepared.TraceJson, row.ResourceId);
                HostStorage.Artifact(c, stateRevision, "state", HostJson.Text(newStatePayload), row.ResourceId);
                HostStorage.Artifact(c, receipt.ReceiptId, "receipt", HostJson.Text(ReceiptPayload(receipt)), row.ResourceId);
                HostStorage.Execute(c, "UPDATE resources SET available=$a,state_revision=$s WHERE resource_id=$r AND state_revision=$old",
                    ("$a", HostJson.Number(prepared.Output.Available)), ("$s", stateRevision), ("$r", row.ResourceId), ("$old", row.StateRevision));
                await Point("AfterStateWrite").ConfigureAwait(false);
                HostStorage.Execute(c, "INSERT INTO transitions(revision,resource_id,previous_revision,receipt_id) VALUES($s,$r,$p,$id)",
                    ("$s", stateRevision), ("$r", row.ResourceId), ("$p", row.StateRevision), ("$id", receipt.ReceiptId));
                HostStorage.Execute(c, "INSERT INTO event_receipts(resource_id,event_id,event_digest,receipt_id,json) VALUES($r,$e,$d,$id,$j)",
                    ("$r", row.ResourceId), ("$e", prepared.Event.EventId), ("$d", prepared.EventDigest), ("$id", receipt.ReceiptId), ("$j", HostJson.Text(receipt)));
                HostStorage.AddProvenance(c, receipt.ReceiptId, principal, "event.commit");
                await Point("BeforeCommitSql").ConfigureAwait(false);
                token.ThrowIfCancellationRequested(); EnsureAlive();
                CommitSql(c, () => commitAttempted = true);
                RemoveCommittedPlans(prepared);
                await Point("AfterCommitSql").ConfigureAwait(false);
                // Concurrent callers that captured this immutable record still deduplicate in the transaction.
                return new("Committed", receipt);
            }
            catch (Exception exception)
            {
                HostStorage.Rollback(c);
                if (commitAttempted) throw Error("commit", "OutcomeUnknown", prepared.Event.EventId);
                if (exception is KernelException) throw;
                if (exception is OperationCanceledException) throw;
                throw Error("storage", "StorageFailure", details: new { reason = exception is SqliteException ? "SqliteWriteFailed" : "TrustedHookFailed" });
            }
        }
        finally { writer.Release(); }
    }

    private static KernelException RuntimeFailure(KernelError failure, ReserveInput input, ResourceRow row, string stage)
    {
        if (failure.Code == "BudgetExceeded") return new(failure);
        return new(KernelError.Create(stage, "VerifierMismatch", failure.EntityId,
            new { underlyingCode = failure.Code, underlyingDetails = failure.Details }, row.ProgramRevision, input)
            with { StateRevision = row.StateRevision, PolicyRevision = row.PolicyRevision });
    }
    private void RemoveCommittedPlans(PreparedRecord completed)
    {
        lock (pendingLock)
            foreach (var id in pending.Values.Where(p => p.EventDigest == completed.EventDigest && p.Event.ResourceId == completed.Event.ResourceId).Select(p => p.Id).ToArray())
                pending.Remove(id);
    }

    private static object ReceiptPayload(EventReceipt r) => new { resourceId = r.ResourceId, @event = EventPayload(r.Event),
        eventDigest = r.EventDigest, previousStateRevision = r.PreviousStateRevision, committedStateRevision = r.CommittedStateRevision,
        inputRef = r.InputRef, outputRef = r.OutputRef, programRef = r.ProgramRef, irRef = r.IrRef, policyRef = r.PolicyRef,
        manifestRef = r.ManifestRef, admissionRef = r.AdmissionRef, semanticsVersion = r.SemanticsVersion,
        runtimeIdentity = r.RuntimeIdentity, fuelUsed = r.FuelUsed, fuelLimit = r.FuelLimit, traceRef = r.TraceRef };
    private static void VerifyReceiptArtifact(SqliteConnection c, EventReceipt receipt, string missingCode)
    {
        var artifact = HostStorage.GetArtifact(c, receipt.ReceiptId, missingCode);
        var mismatchCode = missingCode == "ReplayUnavailable" ? "ReplayMismatch" : "StorageCorrupt";
        if (artifact.Kind != "receipt" || artifact.ResourceId != receipt.ResourceId ||
            artifact.Json != HostJson.Text(ReceiptPayload(receipt)) || CanonicalJson.Hash("receipt", ReceiptPayload(receipt)) != receipt.ReceiptId)
            throw Error("replay", mismatchCode);
        var output = HostStorage.GetArtifact(c, receipt.OutputRef, missingCode);
        if (output.Kind != "output" || output.Json != Encoding.UTF8.GetString(ExecutionCodec.OutputBytes(receipt.Output))) throw Error("replay", mismatchCode);
    }

    /// <summary>Fault-injection seam for trusted conformance only; never routed by KernelClient.</summary>
    public void TamperPreparedInputForTest(string prepareId, long available)
    {
        lock (pendingLock)
        {
            if (!pending.TryGetValue(prepareId, out var p)) throw Error("commit", "PrepareExpired");
            pending[prepareId] = p with { Snapshot = p.Snapshot with { Available = available } };
        }
    }
}
