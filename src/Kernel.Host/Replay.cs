using System.Globalization;
using System.Text;
using System.Text.Json;
using Kernel.Core;
using Microsoft.Data.Sqlite;

namespace Kernel.Host;

public sealed partial class KernelHost
{
    internal ReplayResult Replay(string principal, string receiptId)
    {
        EnsureAlive(); using var c = storage.Open(); HostStorage.Execute(c, "BEGIN");
        try
        {
            var current = RequiredResource(c); var currentPolicy = HostStorage.Policy(c, current);
            Authorize(currentPolicy, current, principal, current.ResourceId, HostRights.ReadResource, HostRights.ReplayResource);
            var receiptJson = HostStorage.Scalar(c, "SELECT json FROM event_receipts WHERE receipt_id=$id AND resource_id=$r", ("$id", receiptId), ("$r", current.ResourceId)) as string;
            if (receiptJson is null) throw Error("authorization", "AccessDenied");
            var requested = HostJson.Read<EventReceipt>(receiptJson);
            var chain = new List<EventReceipt>(); var visited = new HashSet<string>(StringComparer.Ordinal);
            var cursor = requested;
            while (true)
            {
                if (!visited.Add(cursor.CommittedStateRevision) || cursor.ResourceId != current.ResourceId) throw Error("replay", "ReplayMismatch");
                VerifyReceiptArtifact(c, cursor, "ReplayUnavailable");
                CheckTransition(c, cursor);
                chain.Add(cursor);
                if (cursor.PreviousStateRevision == current.GenesisRevision) break;
                var previous = HostStorage.Scalar(c, "SELECT e.json FROM transitions t JOIN event_receipts e ON e.receipt_id=t.receipt_id WHERE t.revision=$r", ("$r", cursor.PreviousStateRevision)) as string;
                if (previous is null) throw Error("replay", "ReplayUnavailable", details: new { reason = "MissingPredecessor" });
                cursor = HostJson.Read<EventReceipt>(previous);
            }
            var genesis = HostStorage.GetArtifact(c, current.GenesisRevision, "ReplayUnavailable");
            var genesisPayload = new { resourceId = current.ResourceId, initialAvailable = current.InitialAvailable };
            if (genesis.Kind != "genesis" || genesis.ResourceId != current.ResourceId || genesis.Json != HostJson.Text(genesisPayload) ||
                CanonicalJson.Hash("genesis", genesisPayload) != current.GenesisRevision || current.InitialAvailable < 0) throw Error("replay", "ReplayMismatch");
            var previousRevision = current.GenesisRevision; var available = current.InitialAvailable;
            foreach (var receipt in chain.AsEnumerable().Reverse())
            {
                ReplayOne(c, receipt, previousRevision, available);
                previousRevision = receipt.CommittedStateRevision; available = receipt.Output.Available;
            }
            HostStorage.Execute(c, "COMMIT"); return new("Replayed", requested, requested.Output, requested.FuelUsed, requested.TraceRef);
        }
        catch (KernelException ex)
        {
            HostStorage.Rollback(c);
            if (ex.Error.Code is "AccessDenied" or "ReplayUnavailable" or "ReplayMismatch") throw;
            throw Error("replay", "ReplayMismatch", details: new { reason = ex.Error.Code });
        }
        catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or InvalidOperationException)
        { HostStorage.Rollback(c); throw Error("replay", "ReplayMismatch"); }
        catch { HostStorage.Rollback(c); throw; }
    }
    private static void CheckTransition(SqliteConnection c, EventReceipt receipt)
    {
        using var command = HostStorage.Command(c, "SELECT resource_id,previous_revision,receipt_id FROM transitions WHERE revision=$r", ("$r", receipt.CommittedStateRevision));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw Error("replay", "ReplayUnavailable", details: new { reason = "MissingTransition" });
        if (reader.GetString(0) != receipt.ResourceId || reader.GetString(1) != receipt.PreviousStateRevision || reader.GetString(2) != receipt.ReceiptId)
            throw Error("replay", "ReplayMismatch");
    }
    private void ReplayOne(SqliteConnection c, EventReceipt receipt, string previousRevision, long previousAvailable)
    {
        if (receipt.PreviousStateRevision != previousRevision || receipt.SemanticsVersion != KernelVersions.Semantics)
        {
            if (receipt.SemanticsVersion != KernelVersions.Semantics) throw Error("replay", "ReplayUnavailable", details: new { reason = "HistoricalRuntimeUnavailable" });
            throw Error("replay", "ReplayMismatch");
        }
        var eventArtifact = ReadKind(c, receipt.EventDigest, "event", receipt.ResourceId);
        if (receipt.EventDigest != EventDigest(receipt.Event) || eventArtifact != HostJson.Text(EventPayload(receipt.Event)) || receipt.Event.ResourceId != receipt.ResourceId)
            throw Error("replay", "ReplayMismatch");
        ValidateEvent(receipt.Event);
        var programJson = ReadKind(c, receipt.ProgramRef, "program", receipt.ResourceId);
        var irJson = ReadKind(c, receipt.IrRef, "ir", receipt.ResourceId);
        var policy = HostJson.Read<HostPolicy>(ReadKind(c, receipt.PolicyRef, "policy", receipt.ResourceId));
        var manifest = HostJson.Read<HostManifest>(ReadKind(c, receipt.ManifestRef, "manifest", receipt.ResourceId));
        var admission = HostJson.Read<AdmissionArtifact>(ReadKind(c, receipt.AdmissionRef, "admission", receipt.ResourceId));
        try { CheckManifest(manifest); }
        catch (KernelException) { throw Error("replay", "ReplayUnavailable", details: new { reason = "HistoricalRuntimeUnavailable" }); }
        if (receipt.RuntimeIdentity != RuntimeIdentity) throw Error("replay", "ReplayUnavailable", details: new { reason = "HistoricalRuntimeUnavailable" });
        if (admission.ProgramRevision != receipt.ProgramRef || admission.IrRevision != receipt.IrRef || admission.PolicyRevision != receipt.PolicyRef ||
            admission.ManifestRevision != receipt.ManifestRef || admission.RuntimeIdentity != receipt.RuntimeIdentity || receipt.FuelLimit != policy.EffectiveLimits.Fuel)
            throw Error("replay", "ReplayMismatch");
        var evidence = ReadEvidence(admission); ValidateEvidence(evidence, admission);
        if (evidence.Solver != solver.Identity) throw Error("replay", "ReplayUnavailable", details: new { reason = "HistoricalSolverIdentityUnavailable" });
        var graph = GraphValidator.Validate(ProgramCodec.Parse(programJson, policy.EffectiveLimits), policy.EffectiveLimits);
        var ir = IrCodec.Parse(irJson);
        if (graph.Revision != receipt.ProgramRef || ir.ProgramRevision != graph.Revision || IrCodec.Revision(Lowerer.Lower(graph)) != receipt.IrRef)
            throw Error("replay", "ReplayMismatch");
        var previous = new ResourceRow(receipt.ResourceId, previousAvailable, previousRevision, "", 0, receipt.ProgramRef, receipt.PolicyRef, receipt.ManifestRef, receipt.AdmissionRef);
        var inputJson = ReadKind(c, receipt.InputRef, "input", receipt.ResourceId);
        if (inputJson != HostJson.Text(InputPayload(previous, receipt.Event))) throw Error("replay", "ReplayMismatch");
        var input = new ReserveInput(previousAvailable, receipt.Event.Quantity);
        if (ReserveContract.CheckInput(input) is not null) throw Error("replay", "ReplayMismatch");
        var result = ReferenceInterpreter.Evaluate(graph, input, receipt.FuelLimit);
        if (!result.Success || result.Output != receipt.Output || result.UsedFuel != receipt.FuelUsed ||
            ReserveContract.CheckOutput(input, receipt.Output) is not null) throw Error("replay", "ReplayMismatch");
        var traceJson = Encoding.UTF8.GetString(ExecutionCodec.TraceBytes(result));
        if (ReadKind(c, receipt.TraceRef, "trace", receipt.ResourceId) != traceJson || CanonicalJson.Hash("trace", HostJson.Element(traceJson)) != receipt.TraceRef)
            throw Error("replay", "ReplayMismatch");
        var statePayload = StatePayload(previous, receipt.EventDigest, receipt.Output);
        if (CanonicalJson.Hash("state", statePayload) != receipt.CommittedStateRevision ||
            ReadKind(c, receipt.CommittedStateRevision, "state", receipt.ResourceId) != HostJson.Text(statePayload)) throw Error("replay", "ReplayMismatch");
    }
    private static string ReadKind(SqliteConnection c, string reference, string kind, string resourceId)
    {
        var artifact = HostStorage.GetArtifact(c, reference, "ReplayUnavailable");
        if (artifact.Kind != kind || artifact.ResourceId != resourceId) throw Error("replay", "ReplayMismatch");
        return artifact.Json;
    }

    internal Explanation Explain(string principal, string artifactId)
    {
        EnsureAlive(); using var c = storage.Open(); HostStorage.Execute(c, "BEGIN");
        try
        {
            var row = RequiredResource(c); var policy = HostStorage.Policy(c, row);
            Authorize(policy, row, principal, row.ResourceId, HostRights.ReadResource);
            PreparedRecord? prepared; lock (pendingLock) pending.TryGetValue(artifactId, out prepared);
            if (prepared is not null)
            {
                if (prepared.Principal != principal) throw Error("authorization", "AccessDenied");
                var committed = ExistingEvent(c, prepared.Event, prepared.EventDigest);
                if (committed is not null)
                {
                    var explanation = CommittedExplanation(c, artifactId, committed);
                    HostStorage.Execute(c, "COMMIT"); return explanation;
                }
                var preview = Preview(prepared);
                var text = $"Ресурс {preview.ResourceId}. Событие {preview.EventId}. Резерв: {preview.Output.Reserved}. Остаток: {preview.OldAvailable} → {preview.Output.Available}.\nКонтракт reserve.v0: проверен для kernel.v0; гарантии условны корректностью доверенного ядра. Внешние действия: 0. Вычисление: {preview.FuelUsed}/{preview.FuelLimit}.\nСостояние: подготовлено, запись ещё не выполнена.";
                HostStorage.Execute(c, "COMMIT");
                return new(artifactId, "Prepared", text, new { preview, capability = new { kind = "StateWrite", resourceId = row.ResourceId }, effects = Array.Empty<object>() });
            }
            var exists = HostStorage.Scalar(c, "SELECT 1 FROM artifacts WHERE ref=$id AND resource_id=$r", ("$id", artifactId), ("$r", row.ResourceId));
            if (exists is null) throw Error("authorization", "AccessDenied");
            var eventReceiptJson = HostStorage.Scalar(c, "SELECT json FROM event_receipts WHERE receipt_id=$id AND resource_id=$r", ("$id", artifactId), ("$r", row.ResourceId)) as string;
            if (eventReceiptJson is not null)
            {
                var receipt = HostJson.Read<EventReceipt>(eventReceiptJson); VerifyReceiptArtifact(c, receipt, "StorageCorrupt");
                var explanation = CommittedExplanation(c, artifactId, receipt);
                HostStorage.Execute(c, "COMMIT");
                return explanation;
            }
            Authorize(policy, row, principal, row.ResourceId, HostRights.ReadProgram);
            var artifact = HostStorage.GetArtifact(c, artifactId);
            if (artifact.Kind == "program")
            {
                var graph = ProgramCodec.Parse(artifact.Json);
                var details = new { programJson = artifact.Json, programRevision = artifactId,
                    nodes = graph.Nodes.Select(n => new { nodeId = n.Id, nodeRevision = ProgramCodec.NodeRevision(n) }).ToArray(),
                    outputsDigest = ProgramCodec.OutputsDigest(graph.Outputs), editScope = policy.EditScope,
                    policyRevision = row.PolicyRevision, manifestRevision = row.ManifestRevision, policy, manifest = HostStorage.Manifest(c, row), effects = Array.Empty<object>() };
                HostStorage.Execute(c, "COMMIT");
                return new(artifactId, row.ProgramRevision == artifactId ? "Active" : "Historical", $"Программа reserve. Узлов: {graph.Nodes.Length}. Ревизия {artifactId}.\nКонтракт reserve.v0 принадлежит host. Внешние действия: 0.", details);
            }
            var patchReceiptJson = HostStorage.Scalar(c, "SELECT json FROM patch_receipts WHERE receipt_id=$id", ("$id", artifactId)) as string;
            if (patchReceiptJson is not null)
            {
                var receipt = HostJson.Read<PatchReceipt>(patchReceiptJson);
                HostStorage.GetArtifact(c, receipt.PatchDigest);
                var beforeGraph = ProgramCodec.Parse(HostStorage.GetArtifact(c, receipt.PreviousProgramRevision).Json);
                var afterGraph = ProgramCodec.Parse(HostStorage.GetArtifact(c, receipt.ProgramRevision).Json);
                var beforeNodes = beforeGraph.Nodes.ToDictionary(n => n.Id, n => ProgramCodec.NodeRevision(n), StringComparer.Ordinal);
                var afterNodes = afterGraph.Nodes.ToDictionary(n => n.Id, n => ProgramCodec.NodeRevision(n), StringComparer.Ordinal);
                var changedNodeIds = beforeNodes.Keys.Union(afterNodes.Keys, StringComparer.Ordinal).Where(id =>
                    !beforeNodes.TryGetValue(id, out var oldRevision) || !afterNodes.TryGetValue(id, out var newRevision) || oldRevision != newRevision)
                    .Order(StringComparer.Ordinal).ToArray();
                var outputChanged = ProgramCodec.OutputsDigest(beforeGraph.Outputs) != ProgramCodec.OutputsDigest(afterGraph.Outputs);
                var provenance = HostStorage.ReadProvenance(c, receipt.ReceiptId);
                if (artifact.Json != HostJson.Text(PatchReceiptPayload(receipt))) throw Error("storage", "StorageCorrupt");
                HostStorage.Execute(c, "COMMIT");
                return new(artifactId, receipt.Status, $"Изменение программы {receipt.PatchId}: {receipt.Status}. Изменённых узлов: {changedNodeIds.Length}.\nРевизия {receipt.PreviousProgramRevision} → {receipt.ProgramRevision}. Внешние действия: 0.",
                    new { receipt, provenance, changedNodeIds, outputChanged, effects = Array.Empty<object>() });
            }
            HostStorage.Execute(c, "COMMIT");
            return new(artifactId, "Artifact", $"Артефакт {artifact.Kind}; идентификатор {artifactId}.", HostJson.Element(artifact.Json));
        }
        catch { HostStorage.Rollback(c); throw; }
    }
    private static Explanation CommittedExplanation(SqliteConnection c, string artifactId, EventReceipt receipt)
    {
        var provenance = HostStorage.ReadProvenance(c, receipt.ReceiptId);
        return new(artifactId, "Committed", $"Ресурс {receipt.ResourceId}. Событие {receipt.Event.EventId}. Резерв: {receipt.Output.Reserved}. Остаток: {receipt.Output.Available}.\nВнешние действия: 0. Вычисление: {receipt.FuelUsed}/{receipt.FuelLimit}.\nЗаписано; ревизия {receipt.CommittedStateRevision}.",
            new { receipt, provenance, effects = Array.Empty<object>(), capability = new { kind = "StateWrite", resourceId = receipt.ResourceId } });
    }
}
