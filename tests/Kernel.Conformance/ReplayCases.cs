using System.Text.Json;
using Kernel.Core;
using Kernel.Host;

namespace Kernel.Conformance;

internal static class ReplayCases
{
    public static void Register(List<ConformanceCase> cases)
    {
        cases.Add(new("host", "replay-persisted-history-and-fuel", ["AC9", "AC12"], Historical));
        cases.Add(new("host", "replay-missing-and-tampered-artifacts", ["AC9"], Artifacts));
        cases.Add(new("host", "replay-predecessor-and-state-chain", ["AC9"], Chain));
    }

    private static async Task Historical(TestContext c)
    {
        var f = await HostFixture.CreateAsync(c);
        var first = (await f.ReserveAsync("evt-history-1", 3)).Receipt;
        var second = (await f.ReserveAsync("evt-history-2", 2)).Receipt;
        await f.ReopenAsync();
        await f.Host.SetPolicyAsync(f.Policy with { Limits = new CoreLimits(Fuel: 5) });
        string before = f.SnapshotDatabase();
        foreach (var receipt in new[] { first, second })
        {
            var replay = f.Client.Replay(receipt.ReceiptId);
            c.Equal(receipt.Output, replay.Output, "historical replay output after restart");
            c.Equal(receipt.FuelUsed, replay.FuelUsed, "replay uses historical fuel accounting");
            c.Equal(6, replay.FuelUsed, "current lower limit five does not redefine historical run");
            c.Equal(receipt.TraceRef, replay.TraceRef, "replay trace bytes identical");
            c.Equal(receipt.ReceiptId, replay.Receipt.ReceiptId, "replay returns original receipt");
            c.Equal(before, f.SnapshotDatabase(), "replay does not mutate any database table");
            AssertArtifacts(c, f, receipt);
        }
        c.Equal(first.CommittedStateRevision, second.PreviousStateRevision, "state chain linked");
        c.Equal(5L, f.Client.Snapshot().Available, "historical replay does not restore old business state");
        c.Evidence["firstReceipt"] = first;
        c.Evidence["secondReceipt"] = second;
    }

    private static void AssertArtifacts(TestContext c, HostFixture f, EventReceipt receipt)
    {
        (string Kind, string Ref)[] references = [("program", receipt.ProgramRef), ("ir", receipt.IrRef), ("policy", receipt.PolicyRef),
            ("manifest", receipt.ManifestRef), ("admission", receipt.AdmissionRef), ("input", receipt.InputRef), ("output", receipt.OutputRef), ("trace", receipt.TraceRef)];
        foreach (var (kind, reference) in references)
        {
            string text = f.Artifact(reference);
            using var json = JsonDocument.Parse(text);
            c.Equal(reference, CanonicalJson.Hash(kind, json.RootElement), "persisted " + kind + " digest verifies");
            c.Equal(reference.Length, 64, kind + " SHA-256 width");
        }
        using var payload = JsonDocument.Parse(f.Artifact(receipt.ReceiptId));
        c.Equal(receipt.ReceiptId, CanonicalJson.Hash("receipt", payload.RootElement), "receipt hash covers normative payload");
        foreach (string property in new[] { "resourceId", "event", "eventDigest", "previousStateRevision", "committedStateRevision", "inputRef", "outputRef", "programRef", "irRef", "policyRef", "manifestRef", "admissionRef", "semanticsVersion", "runtimeIdentity", "fuelUsed", "fuelLimit", "traceRef" })
            c.True(payload.RootElement.TryGetProperty(property, out _), "normative receipt field present: " + property);
        c.True(!payload.RootElement.TryGetProperty("receiptId", out _), "receipt ID excluded from its own digest payload");
    }

    private static async Task Artifacts(TestContext c)
    {
        foreach (string kind in new[] { "program", "ir", "policy", "manifest", "admission", "input", "output", "trace" })
        {
            foreach (bool tamper in new[] { false, true })
            {
                var f = await HostFixture.CreateAsync(c);
                var receipt = (await f.ReserveAsync("evt-artifact", 3)).Receipt;
                string reference = kind switch
                {
                    "program" => receipt.ProgramRef, "ir" => receipt.IrRef, "policy" => receipt.PolicyRef, "manifest" => receipt.ManifestRef,
                    "admission" => receipt.AdmissionRef, "input" => receipt.InputRef, "output" => receipt.OutputRef, _ => receipt.TraceRef
                };
                // Detach current authority from historical artifacts so read authorization remains valid.
                if (kind == "policy") await f.Host.SetPolicyAsync(f.Policy with { Limits = new CoreLimits(Fuel: 127) });
                if (kind == "manifest") await f.Host.SetManifestAsync(new(RuntimeIdentityTag: "current.manifest"));
                if (tamper) f.Sql("UPDATE artifacts SET json=$json WHERE ref=$ref", ("$json", "{\"tampered\":true}"), ("$ref", reference));
                // Deliberate corruption is performed only by this trusted harness connection.
                else f.Sql("PRAGMA foreign_keys=OFF; DELETE FROM artifacts WHERE ref=$ref", ("$ref", reference));
                string before = f.SnapshotDatabase();
                Fixtures.Error(c, tamper ? "ReplayMismatch" : "ReplayUnavailable", () => f.Client.Replay(receipt.ReceiptId), $"{kind} {(tamper ? "tamper" : "missing")} refuses replay");
                c.Equal(before, f.SnapshotDatabase(), "failed replay has no live fallback/write");
            }
        }
    }

    private static async Task Chain(TestContext c)
    {
        var missing = await HostFixture.CreateAsync(c);
        var first = (await missing.ReserveAsync("evt-before", 1)).Receipt;
        var second = (await missing.ReserveAsync("evt-after", 1)).Receipt;
        missing.Sql("DELETE FROM transitions WHERE revision=$ref", ("$ref", first.CommittedStateRevision));
        string before = missing.SnapshotDatabase();
        Fixtures.Error(c, "ReplayUnavailable", () => missing.Client.Replay(second.ReceiptId), "missing predecessor transition refused");
        c.Equal(before, missing.SnapshotDatabase(), "missing predecessor never triggers live action");

        var link = await HostFixture.CreateAsync(c);
        var one = (await link.ReserveAsync("evt-one", 1)).Receipt;
        var two = (await link.ReserveAsync("evt-two", 1)).Receipt;
        link.Sql("UPDATE transitions SET previous_revision=$wrong WHERE revision=$current", ("$wrong", one.PreviousStateRevision), ("$current", two.CommittedStateRevision));
        before = link.SnapshotDatabase();
        Fixtures.Error(c, "ReplayMismatch", () => link.Client.Replay(two.ReceiptId), "different existing predecessor cannot replace correct chain link");
        c.Equal(before, link.SnapshotDatabase(), "chain mismatch no mutation");

        var state = await HostFixture.CreateAsync(c);
        var stateReceipt = (await state.ReserveAsync("evt-state", 1)).Receipt;
        state.Sql("UPDATE artifacts SET json=$json WHERE ref=$ref", ("$json", "{\"resourceId\":\"item-001\",\"tampered\":true}"), ("$ref", stateReceipt.CommittedStateRevision));
        Fixtures.Error(c, "ReplayMismatch", () => state.Client.Replay(stateReceipt.ReceiptId), "committed state revision artifact is verified");

        var epoch = await HostFixture.CreateAsync(c);
        var historical = (await epoch.ReserveAsync("evt-old-runtime", 1)).Receipt;
        await epoch.ReopenAsync(new(RuntimeIdentityTag: "unavailable.old.interpreter"));
        Fixtures.Error(c, "ReplayUnavailable", () => epoch.Client.Replay(historical.ReceiptId), "historical incompatible runtime explicitly refused");
    }
}
