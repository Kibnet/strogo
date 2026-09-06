using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Kernel.Core;
using Microsoft.Data.Sqlite;

namespace Kernel.Host;

internal sealed record TypedOperation(string Op, string? NodeId, string? ExpectedNodeRevision,
    KernelNode? Node, string? ExpectedOutputsDigest, OutputRefs? Outputs);
internal sealed record TypedPatch(string PatchId, string ProgramId, string BaseProgramRevision,
    string ExpectedPolicyRevision, ImmutableArray<TypedOperation> Operations, string Digest, string CanonicalJson);

public sealed partial class KernelHost
{
    private static void Fields(JsonElement e, params string[] names)
    {
        if (e.ValueKind != JsonValueKind.Object) throw Error("schema", "SchemaInvalid");
        var fields = e.EnumerateObject().Select(p => p.Name).ToArray();
        if (fields.Length != names.Length || fields.Any(f => !names.Contains(f, StringComparer.Ordinal)))
            throw Error("schema", "SchemaInvalid", details: new { reason = "UnexpectedOrMissingFields" });
    }
    private static string TextField(JsonElement e, string name)
    { var v = e.GetProperty(name); if (v.ValueKind != JsonValueKind.String) throw Error("schema", "SchemaInvalid"); return v.GetString()!; }
    private static TypedPatch ParsePatch(string json, CoreLimits limits)
    {
        using var d = CanonicalJson.ParseStrict(json, limits); var e = d.RootElement;
        Fields(e, "schemaVersion", "patchId", "programId", "baseProgramRevision", "expectedPolicyRevision", "operations");
        var patchId = TextField(e, "patchId"); var programId = TextField(e, "programId");
        if (TextField(e, "schemaVersion") != KernelVersions.Schema || programId != "reserve" || !IdPattern.IsMatch(patchId)) throw Error("schema", "SchemaInvalid");
        var baseRevision = TextField(e, "baseProgramRevision"); var expectedPolicy = TextField(e, "expectedPolicyRevision");
        ExpectedDigest(baseRevision); ExpectedDigest(expectedPolicy);
        var rawOperations = e.GetProperty("operations");
        if (rawOperations.ValueKind != JsonValueKind.Array) throw Error("schema", "SchemaInvalid");
        if (rawOperations.GetArrayLength() > 256) throw Error("patch", "BudgetExceeded");
        var operations = ImmutableArray.CreateBuilder<TypedOperation>(); var touched = new HashSet<string>(StringComparer.Ordinal); bool outputsSeen = false;
        foreach (var raw in rawOperations.EnumerateArray())
        {
            if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("op", out var opValue) || opValue.ValueKind != JsonValueKind.String) throw Error("schema", "SchemaInvalid");
            var op = opValue.GetString()!; string? nodeId = null; string? nodeRevision = null; KernelNode? node = null; string? outputsDigest = null; OutputRefs? outputs = null;
            switch (op)
            {
                case "AddNode":
                    Fields(raw, "op", "node"); node = ParsePatchNode(raw.GetProperty("node"), limits); nodeId = node.Id; break;
                case "ReplaceNode":
                    Fields(raw, "op", "nodeId", "expectedNodeRevision", "node"); nodeId = TextField(raw, "nodeId");
                    nodeRevision = TextField(raw, "expectedNodeRevision"); ExpectedDigest(nodeRevision);
                    node = ParsePatchNode(raw.GetProperty("node"), limits); if (node.Id != nodeId) throw Error("patch", "StableIdRequired", nodeId); break;
                case "RemoveNode":
                    Fields(raw, "op", "nodeId", "expectedNodeRevision"); nodeId = TextField(raw, "nodeId"); nodeRevision = TextField(raw, "expectedNodeRevision"); ExpectedDigest(nodeRevision); break;
                case "SetOutputs":
                    Fields(raw, "op", "expectedOutputsDigest", "outputs"); outputsDigest = TextField(raw, "expectedOutputsDigest"); ExpectedDigest(outputsDigest);
                    var o = raw.GetProperty("outputs"); Fields(o, "accepted", "available", "reserved");
                    outputs = new(TextField(o, "accepted"), TextField(o, "available"), TextField(o, "reserved"));
                    if (new[] { outputs.Accepted, outputs.Available, outputs.Reserved }.Any(id => !IdPattern.IsMatch(id))) throw Error("schema", "InvalidId");
                    if (outputsSeen) throw Error("patch", "DuplicatePatchTarget"); outputsSeen = true; break;
                default: throw Error("patch", "UnsupportedPatchOperation");
            }
            if (nodeId is not null)
            {
                if (!IdPattern.IsMatch(nodeId)) throw Error("schema", "InvalidId");
                if (!touched.Add(nodeId)) throw Error("patch", "DuplicatePatchTarget", nodeId);
            }
            operations.Add(new(op, nodeId, nodeRevision, node, outputsDigest, outputs));
        }
        return new(patchId, programId, baseRevision, expectedPolicy, operations.ToImmutable(), CanonicalJson.Hash("patch", e), HostJson.Text(e));
    }
    private static KernelNode ParsePatchNode(JsonElement node, CoreLimits limits)
    {
        var wrapper = new { schemaVersion = KernelVersions.Schema, programId = "reserve", profileId = "reserve.v0", nodes = new[] { node },
            outputs = new { accepted = "n.placeholder", available = "n.placeholder", reserved = "n.placeholder" } };
        // Preserve the transport's JSON number/string distinction until the node codec validates it.
        return ProgramCodec.Parse(JsonSerializer.Serialize(wrapper, CanonicalJson.SerializerOptions), limits).Nodes.Single();
    }
    private static void CheckEditScope(HostPolicy policy, TypedPatch patch)
    {
        foreach (var operation in patch.Operations)
        {
            if (operation.Op == "SetOutputs")
            { if (!policy.EditScope.AllowOutputs) throw Error("patch", "EditScopeDenied"); }
            else if (operation.Op == "AddNode")
            { if (!operation.NodeId!.StartsWith(policy.EditScope.AddNodePrefix, StringComparison.Ordinal)) throw Error("patch", "EditScopeDenied"); }
            else if (!policy.EditScope.ExistingNodeIds.Contains(operation.NodeId!)) throw Error("patch", "EditScopeDenied");
        }
    }
    private static PatchReceipt? ExistingPatch(SqliteConnection c, TypedPatch patch)
    {
        var value = HostStorage.Scalar(c, "SELECT json FROM patch_receipts WHERE program_id=$p AND patch_id=$id", ("$p", patch.ProgramId), ("$id", patch.PatchId));
        if (value is not string json) return null;
        var receipt = HostJson.Read<PatchReceipt>(json);
        if (receipt.PatchDigest != patch.Digest) throw Error("patch", "PatchIdConflict", patch.PatchId);
        var artifact = HostStorage.GetArtifact(c, receipt.ReceiptId);
        if (artifact.Kind != "receipt" || artifact.Json != HostJson.Text(PatchReceiptPayload(receipt))) throw Error("storage", "StorageCorrupt");
        return receipt;
    }
    internal async Task<PatchReceipt> ProposePatchAsync(string principal, string patchJson, CancellationToken token)
    {
        EnsureAlive(); ResourceRow before; HostPolicy policy; HostManifest manifest; TypedPatch patch; KernelProgram candidate;
        using (var c = storage.Open())
        {
            HostStorage.Execute(c, "BEGIN");
            try
            {
                before = RequiredResource(c); policy = HostStorage.Policy(c, before);
                Authorize(policy, before, principal, before.ResourceId, HostRights.ReadProgram, HostRights.EditProgram);
                patch = ParsePatch(patchJson, policy.EffectiveLimits); CheckEditScope(policy, patch);
                var existing = ExistingPatch(c, patch);
                if (existing is not null) { HostStorage.Execute(c, "COMMIT"); return existing; }
                if (patch.BaseProgramRevision != before.ProgramRevision) throw Error("patch", "ProgramConflict");
                if (patch.ExpectedPolicyRevision != before.PolicyRevision) throw Error("patch", "PolicyChanged");
                manifest = HostStorage.Manifest(c, before); CheckManifest(manifest);
                var original = ProgramCodec.Parse(HostStorage.GetArtifact(c, before.ProgramRevision).Json, policy.EffectiveLimits);
                var nodes = original.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal); var outputs = original.Outputs;
                foreach (var operation in patch.Operations)
                {
                    switch (operation.Op)
                    {
                        case "AddNode":
                            if (nodes.ContainsKey(operation.NodeId!)) throw Error("patch", "NodeAlreadyExists", operation.NodeId);
                            if (HostStorage.Scalar(c, "SELECT 1 FROM retired_node_ids WHERE program_id=$p AND node_id=$n", ("$p", patch.ProgramId), ("$n", operation.NodeId)) is not null)
                                throw Error("patch", "RetiredNodeId", operation.NodeId);
                            nodes.Add(operation.NodeId!, operation.Node!); break;
                        case "ReplaceNode":
                        case "RemoveNode":
                            if (!nodes.TryGetValue(operation.NodeId!, out var previous) || ProgramCodec.NodeRevision(previous) != operation.ExpectedNodeRevision)
                                throw Error("patch", "NodeConflict", operation.NodeId);
                            if (operation.Op == "ReplaceNode") nodes[operation.NodeId!] = operation.Node!;
                            else nodes.Remove(operation.NodeId!); break;
                        case "SetOutputs":
                            if (ProgramCodec.OutputsDigest(outputs) != operation.ExpectedOutputsDigest) throw Error("patch", "OutputsConflict");
                            outputs = operation.Outputs!; break;
                    }
                }
                candidate = original with { Nodes = nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToImmutableArray(), Outputs = outputs };
                HostStorage.Execute(c, "COMMIT");
            }
            catch { HostStorage.Rollback(c); throw; }
        }
        var verified = await VerifyAsync(Encoding.UTF8.GetString(ProgramCodec.CanonicalBytes(candidate)), policy, manifest, token).ConfigureAwait(false);
        await Point("BeforePatchActivation").ConfigureAwait(false);
        await EnterWriter(token).ConfigureAwait(false);
        try
        {
            using var c = storage.Open(); bool commitAttempted = false;
            try
            {
                HostStorage.Execute(c, "BEGIN IMMEDIATE"); EnsureAlive(); token.ThrowIfCancellationRequested();
                var current = RequiredResource(c); var currentPolicy = HostStorage.Policy(c, current);
                Authorize(currentPolicy, current, principal, current.ResourceId, HostRights.ReadProgram, HostRights.EditProgram); CheckEditScope(currentPolicy, patch);
                var existing = ExistingPatch(c, patch);
                if (existing is not null) { HostStorage.Execute(c, "ROLLBACK"); return existing; }
                CheckTrustPointers(before, current); CheckManifest(HostStorage.Manifest(c, current));
                if (verified.Admission.RuntimeIdentity != RuntimeIdentity) throw Error("admission", "AdmissionInvalidated");
                var admissionRef = PersistAdmission(c, current.ResourceId, policy, manifest, verified.Program, verified.Verification, verified.Admission);
                var status = current.ProgramRevision == verified.Program.Revision ? "NoChange" : "Activated";
                var receipt = new PatchReceipt("", patch.PatchId, patch.ProgramId, patch.Digest, current.ProgramRevision,
                    verified.Program.Revision, current.PolicyRevision, current.ManifestRevision, admissionRef, status);
                receipt = receipt with { ReceiptId = CanonicalJson.Hash("receipt", PatchReceiptPayload(receipt)) };
                HostStorage.Artifact(c, patch.Digest, "patch", patch.CanonicalJson, current.ResourceId);
                HostStorage.Artifact(c, receipt.ReceiptId, "receipt", HostJson.Text(PatchReceiptPayload(receipt)), current.ResourceId);
                foreach (var removed in patch.Operations.Where(o => o.Op == "RemoveNode"))
                    HostStorage.Execute(c, "INSERT INTO retired_node_ids(program_id,node_id) VALUES($p,$n)", ("$p", patch.ProgramId), ("$n", removed.NodeId));
                HostStorage.Execute(c, "UPDATE resources SET program_revision=$p,admission_ref=$a WHERE resource_id=$r",
                    ("$p", verified.Program.Revision), ("$a", admissionRef), ("$r", current.ResourceId));
                HostStorage.Execute(c, "INSERT INTO patch_receipts(program_id,patch_id,patch_digest,receipt_id,json) VALUES($p,$id,$d,$r,$j)",
                    ("$p", patch.ProgramId), ("$id", patch.PatchId), ("$d", patch.Digest), ("$r", receipt.ReceiptId), ("$j", HostJson.Text(receipt)));
                HostStorage.AddProvenance(c, receipt.ReceiptId, principal, "program.patch");
                CommitSql(c, () => commitAttempted = true); return receipt;
            }
            catch (Exception ex)
            {
                HostStorage.Rollback(c);
                if (commitAttempted) throw Error("patch", "OutcomeUnknown", patch.PatchId);
                if (ex is KernelException or OperationCanceledException) throw;
                throw Error("storage", "StorageFailure");
            }
        }
        finally { writer.Release(); }
    }
    private static object PatchReceiptPayload(PatchReceipt r) => new { patchId = r.PatchId, programId = r.ProgramId,
        patchDigest = r.PatchDigest, previousProgramRevision = r.PreviousProgramRevision, programRevision = r.ProgramRevision,
        policyRevision = r.PolicyRevision, manifestRevision = r.ManifestRevision, admissionRef = r.AdmissionRef, status = r.Status };
}
