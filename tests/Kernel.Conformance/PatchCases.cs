using System.Text.Json.Nodes;
using Kernel.Core;
using Kernel.Host;

namespace Kernel.Conformance;

internal static class PatchCases
{
    public static void Register(List<ConformanceCase> cases)
    {
        cases.Add(new("host", "typed-patch-atomic-identity-retries", ["AC4", "AC5"], IdentityAndRetries));
        cases.Add(new("host", "typed-patch-rejection-and-tombstones", ["AC4", "AC5"], InvalidChanges));
        cases.Add(new("host", "patch-admission-trust-snapshot-races", ["AC5", "AC11"], AdmissionRaces));
    }

    private static async Task IdentityAndRetries(TestContext c)
    {
        var f = await HostFixture.CreateAsync(c);
        var initial = f.Client.Snapshot();
        var originalGraph = Fixtures.Reserve();
        var sameNode = originalGraph.Nodes.Single(n => n.Id == "n.zero");
        string unchangedPatch = PatchFixture.Patch("patch-nochange", initial, PatchFixture.Replace(sameNode, sameNode),
            PatchFixture.Outputs(originalGraph.Outputs, originalGraph.Outputs));
        var noChange = await f.Client.ProposePatchAsync(unchangedPatch);
        c.Equal("NoChange", noChange.Status, "same canonical graph returns NoChange");
        c.Equal(initial.ProgramRevision, f.Client.Snapshot().ProgramRevision, "NoChange does not advance program");
        c.Equal(1L, f.Count("patch_receipts"), "NoChange receipt is durable");
        var noChangeExplanation = System.Text.Json.JsonSerializer.SerializeToElement(f.Client.Explain(noChange.ReceiptId).Details);
        c.Equal(0, noChangeExplanation.GetProperty("changedNodeIds").GetArrayLength(), "NoChange projection computes empty actual node diff");
        c.Equal(false, noChangeExplanation.GetProperty("outputChanged").GetBoolean(), "NoChange SetOutputs does not claim actual output change");
        string equivalent = PatchFixture.Equivalent("patch-equivalent", initial);
        var applied = await f.Client.ProposePatchAsync(equivalent);
        var active = f.Client.Snapshot();
        c.True(active.ProgramRevision != initial.ProgramRevision, "equivalent syntax change has new content identity");
        c.Equal(ProgramCodec.Revision(Fixtures.Equivalent()), active.ProgramRevision, "active graph is exact approved candidate");
        c.Equal(10L, active.Available, "program changes do not debit stock");
        c.Equal(initial.StateRevision, active.StateRevision, "program changes leave business revision unchanged");
        var current = ProgramCodec.Parse(f.Artifact(active.ProgramRevision));
        c.Equal("n.debit", current.Nodes.Single(n => n.Id == "n.debit").Id, "replaced entity retains stable ID");
        c.True(ProgramCodec.NodeRevision(current.Nodes.Single(n => n.Id == "n.debit")) != ProgramCodec.NodeRevision(Fixtures.Reserve().Nodes.Single(n => n.Id == "n.debit")), "replaced entity content revision changes");
        var reserve = await f.ReserveAsync("evt-equivalent", 3);
        c.Equal(new ReserveOutput(true, 7, 3), reserve.Receipt.Output, "new graph retains exact domain behavior");
        string beforeRetry = f.SnapshotDatabase();
        var retry = await f.Client.ProposePatchAsync(equivalent);
        c.Equal(applied.ReceiptId, retry.ReceiptId, "exact patch retry precedes stale base check");
        var noChangeRetry = await f.Client.ProposePatchAsync(unchangedPatch);
        c.Equal(noChange.ReceiptId, noChangeRetry.ReceiptId, "NoChange retry survives other patch");
        c.Equal(beforeRetry, f.SnapshotDatabase(), "patch retries do not write");
        await f.ReopenAsync();
        c.Equal(noChange.ReceiptId, (await f.Client.ProposePatchAsync(unchangedPatch)).ReceiptId, "NoChange receipt survives restart");
        string altered = PatchFixture.ChangeJson(unchangedPatch, obj => obj["baseProgramRevision"] = active.ProgramRevision);
        await HostCases.Error(c, "PatchIdConflict", () => f.Client.ProposePatchAsync(altered), "same patch ID different canonical payload");
        c.Equal(beforeRetry, f.SnapshotDatabase(), "patch ID conflict does not mutate");
        c.Evidence["noChangeReceipt"] = noChange;
        c.Evidence["appliedReceipt"] = applied;
    }

    private static async Task InvalidChanges(TestContext c)
    {
        var basePolicy = HostPolicy.Default();
        var policy = basePolicy with { EditScope = basePolicy.EditScope with { ExistingNodeIds = basePolicy.EditScope.ExistingNodeIds.Add("n.notenough") } };
        var f = await HostFixture.CreateAsync(c, policy: policy);
        var original = Fixtures.Reserve(); var snapshot = f.Client.Snapshot();
        var zero = original.Nodes.Single(n => n.Id == "n.zero");
        var debit = original.Nodes.Single(n => n.Id == "n.debit");
        var mutations = new Dictionary<string, string>
        {
            ["stale-base"] = PatchFixture.ChangeJson(PatchFixture.NoChange("patch-negative", snapshot), p => p["baseProgramRevision"] = new string('f', 64)),
            ["stale-node"] = PatchFixture.ChangeJson(PatchFixture.NoChange("patch-negative", snapshot), p => p["operations"]![0]!["expectedNodeRevision"] = new string('f', 64)),
            ["duplicate-operation"] = PatchFixture.Patch("patch-negative", snapshot, PatchFixture.Replace(zero, zero), PatchFixture.Replace(zero, zero)),
            ["remove-referenced"] = PatchFixture.Patch("patch-negative", snapshot, PatchFixture.Remove(zero)),
            ["outside-prefix"] = PatchFixture.Patch("patch-negative", snapshot, PatchFixture.Add(Fixtures.I64("other.zero", 0))),
            ["foreign-node"] = PatchFixture.Patch("patch-negative", snapshot, PatchFixture.Remove(Fixtures.I64("foreign", 0))),
            ["unknown-opcode"] = PatchFixture.Patch("patch-negative", snapshot, PatchFixture.Replace(debit, Fixtures.Op("n.debit", "http.send", KernelType.I64, "n.quantity"))),
            ["changed-contract"] = PatchFixture.Patch("patch-negative", snapshot, PatchFixture.Replace(original.Nodes.Single(n => n.Id == "n.remaining"), Fixtures.BadAdd().Nodes.Single(n => n.Id == "n.remaining"))),
            ["invalid-final-batch"] = PatchFixture.Patch("patch-negative", snapshot, PatchFixture.Add(Fixtures.Op("n.notenough", "bool.not", KernelType.Bool, "n.enough")), PatchFixture.Remove(zero)),
            ["changed-id"] = PatchFixture.Patch("patch-negative", snapshot, PatchFixture.Replace(zero, zero with { Id = "n.changed" })),
            ["stale-outputs"] = PatchFixture.ChangeJson(PatchFixture.Patch("patch-negative", snapshot, PatchFixture.Outputs(original.Outputs, original.Outputs)), p => p["operations"]![0]!["expectedOutputsDigest"] = new string('f', 64))
        };
        string before = f.SnapshotDatabase();
        var errors = new Dictionary<string, string>();
        var expectedCodes = new Dictionary<string, string>
        {
            ["stale-base"] = "ProgramConflict", ["stale-node"] = "NodeConflict", ["duplicate-operation"] = "DuplicatePatchTarget",
            ["remove-referenced"] = "DanglingReference", ["outside-prefix"] = "EditScopeDenied", ["foreign-node"] = "EditScopeDenied",
            ["unknown-opcode"] = "UnsupportedOpcode", ["changed-contract"] = "Counterexample", ["invalid-final-batch"] = "DanglingReference",
            ["changed-id"] = "StableIdRequired", ["stale-outputs"] = "OutputsConflict"
        };
        foreach (var (name, patch) in mutations)
        {
            var ex = await c.ThrowsAsync<KernelException>(() => f.Client.ProposePatchAsync(patch), name);
            c.True(!string.IsNullOrWhiteSpace(ex.Error.Code), name + " structured error");
            c.Equal(expectedCodes[name], ex.Error.Code, name + " exact rejection stage");
            c.Equal(before, f.SnapshotDatabase(), name + " no program/admission/receipt/retired-ID write");
            errors[name] = ex.Error.Code;
        }
        var recovered = await f.Client.ProposePatchAsync(PatchFixture.NoChange("patch-negative", snapshot));
        c.Equal("NoChange", recovered.Status, "failed patches never consume patch ID");
        await f.Client.ProposePatchAsync(PatchFixture.Equivalent("patch-add", f.Client.Snapshot()));
        var equivalent = Fixtures.Equivalent();
        var notEnough = equivalent.Nodes.Single(n => n.Id == "n.notenough");
        string restore = PatchFixture.Patch("patch-restore", f.Client.Snapshot(),
            PatchFixture.Replace(equivalent.Nodes.Single(n => n.Id == "n.debit"), debit), PatchFixture.Remove(notEnough));
        await f.Client.ProposePatchAsync(restore);
        c.Equal(ProgramCodec.Revision(original), f.Client.Snapshot().ProgramRevision, "new valid patch restores old meaning/content");
        c.Equal(1L, f.Count("retired_node_ids"), "removed ID tombstone persisted");
        string beforeReuse = f.SnapshotDatabase();
        var reuse = await c.ThrowsAsync<KernelException>(() => f.Client.ProposePatchAsync(PatchFixture.Equivalent("patch-reuse", f.Client.Snapshot())), "retired ID cannot return");
        c.Equal("RetiredNodeId", reuse.Error.Code, "retired ID refusal is explicit");
        c.Equal(beforeReuse, f.SnapshotDatabase(), "retired ID refusal atomic");
        await f.ReopenAsync();
        c.Throws<KernelException>(() => f.Client.ProposePatchAsync(PatchFixture.Equivalent("patch-reuse", f.Client.Snapshot())).GetAwaiter().GetResult(), "tombstone survives restart");
        c.Evidence["rejections"] = errors;
    }

    private static async Task AdmissionRaces(TestContext c)
    {
        foreach (string drift in new[] { "program", "policy", "manifest", "acl", "scope", "epoch" })
        {
            var hooks = new HostTestHooks();
            var f = await HostFixture.CreateAsync(c, options: new(Hooks: hooks));
            var initial = f.Client.Snapshot();
            var barrier = new AsyncBarrier("BeforePatchActivation");
            hooks.OnPointAsync = barrier.Hook;
            var pending = f.Client.ProposePatchAsync(PatchFixture.Equivalent("patch-pending", initial));
            await barrier.WaitAsync();
            hooks.OnPointAsync = null;
            if (drift == "program") await f.Client.ProposePatchAsync(PatchFixture.Equivalent("patch-other", initial));
            if (drift == "policy") await f.Host.SetPolicyAsync(f.Policy with { StateWrite = false });
            if (drift == "manifest") await f.Host.SetManifestAsync(new(RuntimeIdentityTag: "changed.manifest"));
            if (drift == "acl") await f.Host.SetPolicyAsync(f.Policy with { Grants = [] });
            if (drift == "scope") await f.Host.SetPolicyAsync(f.Policy with { EditScope = f.Policy.EditScope with { ExistingNodeIds = ["n.zero"] } });
            if (drift == "epoch") await f.ReopenAsync(new(RuntimeIdentityTag: "new.epoch"));
            string expected = f.SnapshotDatabase();
            barrier.Release();
            var error = await c.ThrowsAsync<KernelException>(() => pending, "pending patch invalidated by " + drift);
            string expectedCode = drift switch { "program" => "ProgramChanged", "policy" => "PolicyChanged", "acl" => "AccessDenied", "scope" => "EditScopeDenied", _ => "AdmissionInvalidated" };
            c.Equal(expectedCode, error.Error.Code, drift + " exact trust drift classification");
            c.Equal(expected, f.SnapshotDatabase(), drift + " pending candidate is not activated or persisted");
            c.Equal(drift == "program" ? 1L : 0L, f.Count("patch_receipts"), drift + " no stale patch receipt");
        }
    }
}
