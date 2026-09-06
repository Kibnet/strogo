using System.Diagnostics;
using System.Text.Json;
using Kernel.Core;
using Kernel.Host;

namespace Kernel.Conformance;

internal static class HostCases
{
    public static void Register(List<ConformanceCase> cases)
    {
        Add("business-decisions-retry-restart", ["AC6", "AC7", "AC12"], Business);
        Add("forged-input-capability-and-token-owner", ["AC4", "AC11"], Trust);
        Add("sqlite-write-failures-roll-back", ["AC7"], AtomicFailures);
        Add("process-stop-before-after-commit", ["AC7", "AC12"], ProcessStops);
        Add("concurrent-cas-and-same-event", ["AC7", "AC10", "AC11"], Concurrency);
        Add("commit-rechecks-program-policy-manifest", ["AC11"], Drift);
        Add("snapshot-consistency-during-competing-commit", ["AC11"], SnapshotRace);
        Add("acl-update-serialized-with-commit", ["AC11"], AuthorizationBarrier);
        Add("revoked-authorization-on-every-read", ["AC4", "AC11"], RevokedReads);
        Add("worker-timeout-eviction-and-epoch", ["AC8", "AC11"], WorkerAndEpoch);
        Add("runtime-checks-detect-false-verification", ["AC3", "AC4"], FalseVerification);
        Add("provenance-original-actor-survives-retry", ["AC7", "AC10"], Provenance);
        PatchCases.Register(cases);
        ReplayCases.Register(cases);
        void Add(string name, string[] criteria, Func<TestContext, Task> run) => cases.Add(new("host", name, criteria, run));
    }

    internal static async Task<KernelError> Error(TestContext c, string code, Func<Task> action, string message)
    {
        var error = await c.ThrowsAsync<KernelException>(action, message);
        c.Equal(code, error.Error.Code, message + " code");
        return error.Error;
    }

    private static async Task Business(TestContext c)
    {
        var f = await HostFixture.CreateAsync(c);
        var original = f.Client.Snapshot();
        string beforePrepare = f.SnapshotDatabase();
        var envelope = f.Request("evt-first", 3, original);
        var prepared = await f.Client.PrepareAsync(envelope);
        c.Equal("Prepared", prepared.Status, "prepare status");
        c.Equal(new ReserveOutput(true, 7, 3), prepared.Prepared?.Output, "prepared independent business output");
        c.Equal(beforePrepare, f.SnapshotDatabase(), "prepare does not write any database table");
        var committed = await f.Client.CommitAsync(prepared.Prepared!.PrepareId);
        c.Equal("Committed", committed.Status, "commit status");
        Fixtures.Error(c, "AccessDenied", () => f.Client.Explain(prepared.Prepared.PrepareId), "committed token cannot still project uncommitted Prepared state");
        var committedExplanation = f.Client.Explain(committed.Receipt.ReceiptId);
        c.Equal("Committed", committedExplanation.Status, "receipt explanation is committed");
        c.True(!committedExplanation.Text.Contains("запись ещё не выполнена", StringComparison.Ordinal), "committed projection never claims write is pending");
        c.Equal(7L, f.Client.Snapshot().Available, "stock persisted");
        c.Equal(1L, f.Count("event_receipts"), "one event receipt");
        c.Equal(1L, f.Count("transitions"), "one state transition");
        var firstReceipt = committed.Receipt;
        c.True(firstReceipt.PreviousStateRevision != firstReceipt.CommittedStateRevision, "state revision advanced");
        c.Equal(6, firstReceipt.FuelUsed, "receipt actual fuel");
        c.Equal(128, firstReceipt.FuelLimit, "receipt historical limit");
        string after = f.SnapshotDatabase();
        var retry = await f.Client.PrepareAsync(envelope);
        c.Equal("AlreadyCommitted", retry.Status, "stale exact retry precedes revision checks");
        c.Equal(firstReceipt.ReceiptId, retry.Receipt?.ReceiptId, "exact retry returns original receipt");
        c.Equal(after, f.SnapshotDatabase(), "retry does not write");
        await Error(c, "EventIdConflict", () => f.Client.PrepareAsync(envelope with { Event = envelope.Event with { Quantity = 4 } }), "same ID different intent");
        c.Equal(after, f.SnapshotDatabase(), "event ID conflict does not write");
        await f.ReopenAsync();
        var restarted = await f.Client.PrepareAsync(envelope);
        c.Equal(firstReceipt.ReceiptId, restarted.Receipt?.ReceiptId, "restart retry durable");
        await f.Client.ProposePatchAsync(PatchFixture.Equivalent("patch-after-event", f.Client.Snapshot()));
        var afterProgramChange = await f.Client.PrepareAsync(envelope);
        c.Equal(firstReceipt.ReceiptId, afterProgramChange.Receipt?.ReceiptId, "retry survives active program drift");
        c.Equal(7L, f.Client.Snapshot().Available, "program change does not change stock");

        foreach (long invalidQuantity in new[] { 0L, -1L })
        {
            string before = f.SnapshotDatabase();
            await Error(c, "PreconditionFailed", () => f.Client.PrepareAsync(f.Request("evt-invalid", invalidQuantity)), "invalid quantity");
            c.Equal(before, f.SnapshotDatabase(), "invalid quantity occupies no event ID");
        }
        var corrected = await f.ReserveAsync("evt-invalid", 1);
        c.Equal(6L, corrected.Receipt.Output.Available, "event ID remains usable after invalid request");
        var denied = await f.ReserveAsync("evt-denied", 11);
        c.Equal(new ReserveOutput(false, 6, 0), denied.Receipt.Output, "insufficient stock commits rejected business decision");
        c.Equal(3L, f.Count("event_receipts"), "rejected decision has durable receipt");
        c.True(denied.Receipt.PreviousStateRevision != denied.Receipt.CommittedStateRevision, "denied event advances revision");
        var deniedRetry = await f.Client.PrepareAsync(f.Request("evt-denied", 11));
        c.Equal(denied.Receipt.ReceiptId, deniedRetry.Receipt?.ReceiptId, "rejected decision is idempotent");

        var max = await HostFixture.CreateAsync(c, long.MaxValue);
        c.Equal(new ReserveOutput(true, 0, long.MaxValue), (await max.ReserveAsync("evt-max", long.MaxValue)).Receipt.Output, "Max stock/quantity exact");
        var zero = await HostFixture.CreateAsync(c, 0);
        c.Equal(new ReserveOutput(false, 0, 0), (await zero.ReserveAsync("evt-zero", 1)).Receipt.Output, "zero stock rejection");
        c.Evidence["firstReceipt"] = firstReceipt;
    }

    private static async Task Trust(TestContext c)
    {
        var policy = HostPolicy.Default() with { Grants = [new("agent", HostRights.All), new("other", HostRights.All)] };
        var f = await HostFixture.CreateAsync(c, policy: policy);
        var prepared = await f.Client.PrepareAsync(f.Request("evt-owner", 3));
        string before = f.SnapshotDatabase();
        await Error(c, "AccessDenied", () => f.Host.Bind("other").CommitAsync(prepared.Prepared!.PrepareId), "other principal cannot use known token");
        await Error(c, "PrepareExpired", () => f.Client.CommitAsync(new string('f', 64)), "forged prepare token");
        foreach (string field in new[] { "verified", "ir", "output", "policy", "manifest", "profileId", "principal" })
        {
            string forged = PatchFixture.ChangeJson(PatchFixture.NoChange("patch-forged", f.Client.Snapshot()), root => root[field] = true);
            var failure = await c.ThrowsAsync<KernelException>(() => f.Client.ProposePatchAsync(forged), "forged patch property " + field);
            c.True(failure.Error.Code is "SchemaInvalid" or "AccessDenied" or "PatchInvalid", "forged metadata rejected by protocol");
            c.Equal(before, f.SnapshotDatabase(), "forged metadata no persistent changes");
        }
        var noCap = await HostFixture.CreateAsync(c, policy: HostPolicy.Default() with { StateWrite = false });
        string noCapBefore = noCap.SnapshotDatabase();
        var denied = await c.ThrowsAsync<KernelException>(() => noCap.Client.PrepareAsync(noCap.Request("evt-no-cap", 1)), "missing capability blocks prepare");
        c.True(denied.Error.Code is "AccessDenied" or "CapabilityDenied", "missing capability classified");
        c.Equal(noCapBefore, noCap.SnapshotDatabase(), "missing capability writes nothing");
        c.True(denied.Error.AllowedRepairs.All(r => !r.Contains("Disable", StringComparison.OrdinalIgnoreCase) && !r.Contains("Grant", StringComparison.OrdinalIgnoreCase)), "repairs do not grant authority");
    }

    private static async Task AtomicFailures(TestContext c)
    {
        foreach (string point in new[] { "AfterStateWrite", "BeforeCommitSql" })
        {
            var hooks = new HostTestHooks();
            var f = await HostFixture.CreateAsync(c, options: new(Hooks: hooks));
            var prepared = await f.Client.PrepareAsync(f.Request("evt-failure", 3));
            string before = f.SnapshotDatabase();
            hooks.OnPointAsync = actual => actual == point ? Task.FromException(new IOException("injected write failure " + point)) : Task.CompletedTask;
            await Error(c, "StorageFailure", () => f.Client.CommitAsync(prepared.Prepared!.PrepareId), point + " storage failure");
            hooks.OnPointAsync = null;
            c.Equal(before, f.SnapshotDatabase(), point + " rolls back all tables");
            await f.ReopenAsync();
            c.Equal(before, f.SnapshotDatabase(), point + " rollback survives reopen");
            c.Equal(new ReserveOutput(true, 7, 3), (await f.ReserveAsync("evt-failure", 3)).Receipt.Output, "failed write did not consume event ID");
        }
        var afterHooks = new HostTestHooks();
        var after = await HostFixture.CreateAsync(c, options: new(Hooks: afterHooks));
        var plan = await after.Client.PrepareAsync(after.Request("evt-unknown", 3));
        afterHooks.OnPointAsync = actual => actual == "AfterCommitSql" ? Task.FromException(new IOException("response lost")) : Task.CompletedTask;
        await Error(c, "OutcomeUnknown", () => after.Client.CommitAsync(plan.Prepared!.PrepareId), "post-COMMIT lost response");
        afterHooks.OnPointAsync = null;
        await after.ReopenAsync();
        var recovered = await after.Client.PrepareAsync(after.Request("evt-unknown", 3));
        c.Equal("AlreadyCommitted", recovered.Status, "ambiguous outcome resolved by durable event receipt");
        c.Equal(7L, after.Client.Snapshot().Available, "lost response debits only once");
        c.Equal(1L, after.Count("event_receipts"), "lost response one receipt");
    }

    private static async Task Concurrency(TestContext c)
    {
        var casHooks = new HostTestHooks();
        var f = await HostFixture.CreateAsync(c, options: new(Hooks: casHooks));
        var snapshot = f.Client.Snapshot();
        var first = await f.Client.PrepareAsync(f.Request("evt-a", 3, snapshot));
        var second = await f.Client.PrepareAsync(f.Request("evt-b", 4, snapshot));
        int casCaptured = 0;
        var casBoth = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var casRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        casHooks.OnPointAsync = async point =>
        {
            if (point != "BeforeCommitTransaction") return;
            if (Interlocked.Increment(ref casCaptured) == 2) casBoth.TrySetResult();
            await casRelease.Task;
        };
        var casFirst = Capture(() => f.Client.CommitAsync(first.Prepared!.PrepareId));
        var casSecond = Capture(() => f.Client.CommitAsync(second.Prepared!.PrepareId));
        await casBoth.Task.WaitAsync(TimeSpan.FromSeconds(10));
        casRelease.TrySetResult();
        var results = await Task.WhenAll(casFirst, casSecond);
        casHooks.OnPointAsync = null;
        c.Equal(2, casCaptured, "both different-event commits reached barrier before either wrote");
        c.Equal(1, results.Count(r => r.Result is not null), "one different-event plan commits from shared revision");
        c.Equal(1, results.Count(r => r.Error?.Code == "StateConflict"), "other different-event plan is stale");
        c.Equal(1L, f.Count("event_receipts"), "one receipt under CAS race");
        c.Equal(1L, f.Count("transitions"), "one transition under CAS race");
        string loserId = results[0].Result is null ? "evt-a" : "evt-b";
        long loserQty = loserId == "evt-a" ? 3 : 4;
        await f.ReserveAsync(loserId, loserQty);
        c.Equal(3L, f.Client.Snapshot().Available, "stale event can reprepare on fresh state");

        var sameHooks = new HostTestHooks();
        var same = await HostFixture.CreateAsync(c, options: new(Hooks: sameHooks));
        var p = await same.Client.PrepareAsync(same.Request("evt-same", 3));
        int captured = 0;
        var bothCaptured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sameHooks.OnPointAsync = async point =>
        {
            if (point != "BeforeCommitTransaction") return;
            if (Interlocked.Increment(ref captured) == 2) bothCaptured.TrySetResult();
            await release.Task;
        };
        var sameFirst = Capture(() => same.Client.CommitAsync(p.Prepared!.PrepareId));
        var sameSecond = Capture(() => same.Client.CommitAsync(p.Prepared!.PrepareId));
        await bothCaptured.Task.WaitAsync(TimeSpan.FromSeconds(10));
        release.TrySetResult();
        var sameResults = await Task.WhenAll(sameFirst, sameSecond);
        c.True(sameResults.All(r => r.Result is not null), "concurrent same-token calls return receipts");
        c.Equal(sameResults[0].Result!.Receipt.ReceiptId, sameResults[1].Result!.Receipt.ReceiptId, "same-token receipt identity");
        c.Equal(1L, same.Count("event_receipts"), "same-token one durable receipt");
        c.Equal(7L, same.Client.Snapshot().Available, "same-token one debit");
        c.Equal(2, captured, "both concurrent calls captured token before commit");

        var duplicatePlans = await HostFixture.CreateAsync(c);
        var eventEnvelope = duplicatePlans.Request("evt-two-tokens", 3);
        var tokenA = await duplicatePlans.Client.PrepareAsync(eventEnvelope);
        var tokenB = await duplicatePlans.Client.PrepareAsync(eventEnvelope);
        var decision = await duplicatePlans.Client.CommitAsync(tokenA.Prepared!.PrepareId);
        Fixtures.Error(c, "AccessDenied", () => duplicatePlans.Client.Explain(tokenB.Prepared!.PrepareId), "other completed-event token is purged and cannot project Prepared");
        await Error(c, "PrepareExpired", () => duplicatePlans.Client.CommitAsync(tokenB.Prepared!.PrepareId), "other completed-event token is ephemeral");
        var durableRetry = await duplicatePlans.Client.PrepareAsync(eventEnvelope);
        c.Equal("AlreadyCommitted", durableRetry.Status, "event identity recovers outcome after all tokens expire");
        c.Equal(decision.Receipt.ReceiptId, durableRetry.Receipt?.ReceiptId, "expired token recovered through original durable event receipt");
        c.Equal(1L, duplicatePlans.Count("event_receipts"), "two tokens create one durable decision");
    }

    private static async Task Drift(TestContext c)
    {
        foreach (string kind in new[] { "program", "policy", "manifest" })
        {
            var f = await HostFixture.CreateAsync(c);
            var p = await f.Client.PrepareAsync(f.Request("evt-drift", 3));
            if (kind == "program") await f.Client.ProposePatchAsync(PatchFixture.Equivalent("patch-drift", f.Client.Snapshot()));
            if (kind == "policy") await f.Host.SetPolicyAsync(f.Policy with { StateWrite = false });
            if (kind == "manifest") await f.Host.SetManifestAsync(new HostManifest(RuntimeIdentityTag: "changed.manifest"));
            string before = f.SnapshotDatabase();
            await Error(c, kind switch { "program" => "ProgramChanged", "policy" => "PolicyChanged", _ => "AdmissionInvalidated" },
                () => f.Client.CommitAsync(p.Prepared!.PrepareId), kind + " rechecked under commit");
            c.Equal(before, f.SnapshotDatabase(), "drift refusal no writes");
            c.Equal(10L, f.Client.Snapshot().Available, "drift refusal unchanged stock");
        }
    }

    private static async Task SnapshotRace(TestContext c)
    {
        var hooks = new HostTestHooks();
        var f = await HostFixture.CreateAsync(c, options: new(Hooks: hooks));
        var fast = await f.Client.PrepareAsync(f.Request("evt-fast", 3));
        var barrier = new AsyncBarrier("AfterPrepareSnapshot");
        hooks.OnPointAsync = barrier.Hook;
        var slowTask = f.Client.PrepareAsync(f.Request("evt-slow", 4));
        await barrier.WaitAsync();
        await f.Client.CommitAsync(fast.Prepared!.PrepareId);
        barrier.Release();
        var slow = await slowTask;
        hooks.OnPointAsync = null;
        c.Equal(10L, slow.Prepared?.OldAvailable, "prepare captured old stock with old revision");
        c.Equal(6L, slow.Prepared?.Output.Available, "prepare did not mix new stock with old revision");
        string before = f.SnapshotDatabase();
        await Error(c, "StateConflict", () => f.Client.CommitAsync(slow.Prepared!.PrepareId), "consistent old snapshot cannot overwrite new state");
        c.Equal(before, f.SnapshotDatabase(), "stale snapshot no writes");

        var input = await HostFixture.CreateAsync(c);
        var plan = await input.Client.PrepareAsync(input.Request("evt-input", 3));
        input.Sql("UPDATE resources SET available='9' WHERE resource_id='item-001'");
        var mismatch = await c.ThrowsAsync<KernelException>(() => input.Client.CommitAsync(plan.Prepared!.PrepareId), "same revision with altered stock refused");
        c.Equal("PreparedInputMismatch", mismatch.Error.Code, "exact input checked in addition to revision");
        c.Equal(0L, input.Count("event_receipts"), "mixed input creates no receipt");
    }

    private static async Task AuthorizationBarrier(TestContext c)
    {
        var hooks = new HostTestHooks();
        var f = await HostFixture.CreateAsync(c, options: new(Hooks: hooks));
        var prepared = await f.Client.PrepareAsync(f.Request("evt-serialized", 3));
        var barrier = new AsyncBarrier("AfterCommitChecks");
        hooks.OnPointAsync = barrier.Hook;
        var commit = f.Client.CommitAsync(prepared.Prepared!.PrepareId);
        await barrier.WaitAsync();
        var revoke = f.Host.SetPolicyAsync(f.Policy with { Grants = [] });
        c.True(!revoke.IsCompleted, "policy mutation waits while commit owns authority snapshot");
        barrier.Release();
        var committed = await commit;
        await revoke;
        c.Equal("Committed", committed.Status, "commit ordered before queued revocation");
        Fixtures.Error(c, "AccessDenied", () => f.Client.Snapshot(), "next read observes revocation");
        c.Equal(1L, f.Count("event_receipts"), "serialized commit has one receipt");
    }

    private static async Task RevokedReads(TestContext c)
    {
        var f = await HostFixture.CreateAsync(c);
        var receipt = (await f.ReserveAsync("evt-visible", 1)).Receipt;
        var pending = await f.Client.PrepareAsync(f.Request("evt-pending", 1));
        var snap = f.Client.Snapshot();
        var patch = PatchFixture.NoChange("patch-readable", snap);
        await f.Client.ProposePatchAsync(patch);
        var retry = f.Request("evt-visible", 1, snap);
        await f.Host.SetPolicyAsync(f.Policy with { Grants = [] });
        string before = f.SnapshotDatabase();
        var reads = new Dictionary<string, Func<Task>>
        {
            ["Snapshot"] = () => { f.Client.Snapshot(); return Task.CompletedTask; },
            ["unknown-Snapshot"] = () => { f.Client.Snapshot("other-resource"); return Task.CompletedTask; },
            ["Prepare-receipt"] = () => f.Client.PrepareAsync(retry),
            ["Commit-token"] = () => f.Client.CommitAsync(pending.Prepared!.PrepareId),
            ["Replay"] = () => { f.Client.Replay(receipt.ReceiptId); return Task.CompletedTask; },
            ["Explain-receipt"] = () => { f.Client.Explain(receipt.ReceiptId); return Task.CompletedTask; },
            ["Explain-prepare"] = () => { f.Client.Explain(pending.Prepared!.PrepareId); return Task.CompletedTask; },
            ["Explain-program"] = () => { f.Client.Explain(snap.ProgramRevision); return Task.CompletedTask; },
            ["Patch-receipt"] = () => f.Client.ProposePatchAsync(patch)
        };
        foreach (var (name, action) in reads)
        {
            var error = await Error(c, "AccessDenied", action, "revocation blocks " + name);
            c.True(error.ProgramRevision is null && error.StateRevision is null && error.PolicyRevision is null && error.Witness is null, name + " does not leak protected hashes or witness");
            c.Equal(before, f.SnapshotDatabase(), name + " read refusal does not write");
        }
        c.Evidence["readMethods"] = reads.Keys.ToArray();
    }

    private static async Task WorkerAndEpoch(TestContext c)
    {
        var hooks = new HostTestHooks();
        var f = await HostFixture.CreateAsync(c, options: new(Hooks: hooks, WorkerTimeoutMilliseconds: 200, MaxPrepared: 2));
        var barrier = new AsyncBarrier("BeforeEvaluation");
        hooks.OnPointAsync = barrier.Hook;
        string before = f.SnapshotDatabase();
        var evaluation = f.Client.PrepareAsync(f.Request("evt-slow-worker", 3));
        await barrier.WaitAsync();
        var failure = await c.ThrowsAsync<KernelException>(async () => await evaluation.WaitAsync(TimeSpan.FromSeconds(5)), "stalled worker stops before commit");
        c.True(failure.Error.Code is "WorkerTimeout" or "EvaluationTimeout" or "RuntimeTimeout", "worker watchdog has explicit timeout code");
        barrier.Release();
        hooks.OnPointAsync = null;
        c.Equal(before, f.SnapshotDatabase(), "timeout and late worker result do not write");
        var p1 = await f.Client.PrepareAsync(f.Request("evt-evict-1", 1));
        await f.Client.PrepareAsync(f.Request("evt-evict-2", 1));
        await f.Client.PrepareAsync(f.Request("evt-evict-3", 1));
        await Error(c, "PrepareExpired", () => f.Client.CommitAsync(p1.Prepared!.PrepareId), "oldest pending plan evicted");
        var retry = await f.Client.PrepareAsync(f.Request("evt-evict-1", 1));
        c.Equal("Prepared", retry.Status, "eviction does not occupy event ID");
        await f.ReopenAsync();
        await Error(c, "PrepareExpired", () => f.Client.CommitAsync(retry.Prepared!.PrepareId), "restart expires tokens");
        var fresh = await f.Client.PrepareAsync(f.Request("evt-after-restart", 1));
        c.Equal("Prepared", fresh.Status, "same TCB restart can reprepare");
        await f.ReopenAsync(new(RuntimeIdentityTag: "new.tcb.epoch"));
        await Error(c, "PrepareExpired", () => f.Client.CommitAsync(fresh.Prepared!.PrepareId), "new TCB epoch expires token");
        await Error(c, "AdmissionInvalidated", () => f.Client.PrepareAsync(f.Request("evt-new-tcb", 1)), "new TCB invalidates old admission");

        var cache = await HostFixture.CreateAsync(c, options: new(MaxPrepared: 2));
        var waiting = await cache.Client.PrepareAsync(cache.Request("evt-waiting", 1));
        var finishing = await cache.Client.PrepareAsync(cache.Request("evt-finishing", 1));
        await cache.Client.CommitAsync(finishing.Prepared!.PrepareId);
        await cache.Client.PrepareAsync(cache.Request("evt-new-pending", 1));
        await Error(c, "StateConflict", () => cache.Client.CommitAsync(waiting.Prepared!.PrepareId), "completed token must not consume cache slot or evict pending token");
    }

    private static async Task<(CommitResult? Result, KernelError? Error)> Capture(Func<Task<CommitResult>> action)
    {
        try { return (await action(), null); } catch (KernelException ex) { return (null, ex.Error); }
    }

    private static async Task FalseVerification(TestContext c)
    {
        var strictReserve = Fixtures.Reserve();
        strictReserve = strictReserve with { Nodes = strictReserve.Nodes.AddRange(new KernelNode[] {
            Fixtures.I64("n.one", 1), Fixtures.Bool("n.false", false),
            Fixtures.Op("n.overflow", "i64.add_checked", KernelType.I64, "n.available", "n.one"),
            Fixtures.Op("n.safezero", "select", KernelType.I64, "n.false", "n.overflow", "n.zero") }) };
        strictReserve = Fixtures.Replace(strictReserve, Fixtures.Op("n.debit", "select", KernelType.I64, "n.enough", "n.quantity", "n.safezero"));
        foreach (var (name, graph, available, quantity) in new[] {
                     ("wrong-output", Fixtures.BadAdd(), 10L, 3L), ("overflow", strictReserve, long.MaxValue, 1L) })
        {
            var liar = new StubSolver(query => new(0, query.StartsWith("; obligation:domain", StringComparison.Ordinal) ? "sat\n" : "unsat\n", ""));
            var f = await HostFixture.CreateAsync(c, available: available, trustedTestSolver: liar, programJson: Fixtures.Text(graph));
            c.True(liar.Queries.Count >= 2, "trusted fake admitted graph through normal verification interface");
            string before = f.SnapshotDatabase();
            await Error(c, "VerifierMismatch", () => f.Client.PrepareAsync(f.Request("evt-false-proof", quantity)), name + " runtime disagrees with static verified");
            c.Equal(before, f.SnapshotDatabase(), "false verification yields no plan/receipt/state write");
            c.Equal(0L, f.Count("event_receipts"), "false verification event never committed");
            c.Equal(available, f.Client.Snapshot().Available, "false verification leaves state intact");
        }
    }

    private static async Task Provenance(TestContext c)
    {
        var policy = HostPolicy.Default() with { Grants = [new("agent", HostRights.All), new("other", HostRights.All)] };
        var f = await HostFixture.CreateAsync(c, policy: policy);
        var snapshot = f.Client.Snapshot();
        string patch = PatchFixture.NoChange("patch-origin", snapshot);
        var patchReceipt = await f.Client.ProposePatchAsync(patch);
        var eventReceipt = (await f.ReserveAsync("evt-origin", 3)).Receipt;
        string before = f.SnapshotDatabase();
        var other = f.Host.Bind("other");
        c.Equal(patchReceipt.ReceiptId, (await other.ProposePatchAsync(patch)).ReceiptId, "other authorized principal gets original patch receipt");
        c.Equal(eventReceipt.ReceiptId, (await other.PrepareAsync(f.Request("evt-origin", 3, snapshot))).Receipt?.ReceiptId, "other authorized principal gets original event receipt");
        c.Equal(before, f.SnapshotDatabase(), "retry by second actor never rewrites provenance");
        await f.ReopenAsync();
        foreach (var (id, kind) in new[] { (patchReceipt.ReceiptId, "program.patch"), (eventReceipt.ReceiptId, "event.commit") })
        {
            var details = JsonSerializer.SerializeToElement(f.Host.Bind("other").Explain(id).Details, CanonicalJson.SerializerOptions);
            var provenance = details.GetProperty("provenance");
            c.Equal("agent", provenance.GetProperty("principal").GetString(), "original actor persists through retry and restart");
            c.Equal(kind, provenance.GetProperty("operationKind").GetString(), "provenance distinguishes operation kind");
            c.True(DateTimeOffset.TryParse(provenance.GetProperty("recordedAtUtc").GetString(), out var time) && time.Offset == TimeSpan.Zero, "provenance contains UTC recording time");
            using var semantic = JsonDocument.Parse(f.Artifact(id));
            c.True(!semantic.RootElement.TryGetProperty("principal", out _) && !semantic.RootElement.TryGetProperty("recordedAtUtc", out _), "operational provenance excluded from semantic receipt hash");
            c.Equal(id, CanonicalJson.Hash("receipt", semantic.RootElement), "receipt hash remains exact after retries");
        }
        c.Equal(before, f.SnapshotDatabase(), "provenance inspection is read-only");
    }

    private static async Task ProcessStops(TestContext c)
    {
        foreach (string point in new[] { "BeforeCommitSql", "AfterCommitSql" })
        {
            var f = await HostFixture.CreateAsync(c);
            string before = f.SnapshotDatabase();
            var start = SelfProcess(["--child", "commit-kill", "--db", f.Database, "--point", point, "--z3", c.RequireZ3()]);
            using var child = Process.Start(start) ?? throw new ConformanceException("Child process did not start");
            var stderrTask = child.StandardError.ReadToEndAsync();
            string? line = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            try
            {
                c.Equal("READY " + point, line, "child reached exact fault barrier");
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
            }
            await f.ReopenAsync();
            if (point == "BeforeCommitSql")
            {
                c.Equal(before, f.SnapshotDatabase(), "killed uncommitted transaction fully rolled back");
                c.Equal(10L, f.Client.Snapshot().Available, "precommit process stop keeps stock");
                var p = await f.Client.PrepareAsync(f.Request("evt-child", 3));
                c.Equal("Prepared", p.Status, "precommit process stop leaves event free");
            }
            else
            {
                c.Equal(7L, f.Client.Snapshot().Available, "postcommit process stop preserves debit");
                var p = await f.Client.PrepareAsync(f.Request("evt-child", 3));
                c.Equal("AlreadyCommitted", p.Status, "postcommit process stop resolved by event retry");
                c.Equal(1L, f.Count("event_receipts"), "postcommit process stop leaves exactly one receipt");
            }
            c.Evidence[point] = new { childExitCode = child.ExitCode, stderr = await stderrTask, database = f.Database };
        }
    }

    internal static ProcessStartInfo SelfProcess(IEnumerable<string> arguments)
    {
        string assembly = typeof(Program).Assembly.Location;
        string current = Environment.ProcessPath ?? throw new ConformanceException("No current process executable");
        var start = new ProcessStartInfo(current) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(current).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(assembly);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    public static async Task<int> RunChildAsync(string[] args)
    {
        if (args.FirstOrDefault() != "commit-kill") throw new ArgumentException("Unknown child command");
        string db = Program.Option(args, "--db") ?? throw new ArgumentException("--db required");
        string point = Program.Option(args, "--point") ?? throw new ArgumentException("--point required");
        var c = new TestContext(Program.Option(args, "--z3"));
        var solver = await Toolchain.SolverAsync(c);
        var hooks = new HostTestHooks { OnPointAsync = async actual =>
        {
            if (actual != point) return;
            Console.WriteLine("READY " + point); await Console.Out.FlushAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan);
        } };
        var host = await KernelHost.OpenAsync(db, solver, new(Hooks: hooks));
        var client = host.Bind("agent"); var snapshot = client.Snapshot();
        var prepared = await client.PrepareAsync(new(new("evt-child", snapshot.ResourceId, "reserve", 3), snapshot.StateRevision, snapshot.ProgramRevision, snapshot.PolicyRevision));
        await client.CommitAsync(prepared.Prepared!.PrepareId);
        throw new ConformanceException("Fault barrier did not stop child");
    }
}
