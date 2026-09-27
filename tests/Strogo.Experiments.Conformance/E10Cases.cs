using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Kernel.Core;
using Strogo.Experiments;

internal static class E10Cases
{
    private const string ImageId = "sha256:1aabdb4843de1c426d3676bf1220bc040e540f82a765320b3eb2c693e8d0a7dd";
    private static readonly string ValidStrogo = "{ long available = input.resourceAvailable; long quantity = input.requestedQuantity; bool enough = quantity <= available; long zero = 0L; long debit = Select(enough, quantity, zero); long remaining = checked(available - debit); return (accepted: enough, available: remaining, reserved: debit); }";
    private static readonly string ValidCSharp = "public static class Candidate { public static CandidateResult Execute(long resourceAvailable, long requestedQuantity) { bool accepted = requestedQuantity <= resourceAvailable; return accepted ? new CandidateResult(true, checked(resourceAvailable - requestedQuantity), requestedQuantity) : new CandidateResult(false, resourceAvailable, 0); } }";

    public static void Run(List<string> failures)
    {
        void Check(bool condition, string name) { if (!condition) failures.Add($"E10 {name}"); }
        string fixtures = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "fixtures", "e10-live-pilot"));
        string root = Path.Combine(Path.GetTempPath(), $"strogo-e10-conformance-{Guid.NewGuid():N}");
        string invalidRoot = Path.Combine(Path.GetTempPath(), $"strogo-e10-invalid-{Guid.NewGuid():N}");
        var additionalRoots = new List<string>();
        int failuresBeforeE10 = failures.Count;
        try
        {
            var pilot = Prepare(root, fixtures, "e10-conformance");
            Check(pilot.RunOrder.SequenceEqual(LivePilotIdentity.RunOrder), "fixed run order");
            Check(pilot.Runs.Length == 4 && pilot.Runs.Select(x => x.Arm).SequenceEqual(["strogo-notation", "csharp-dotnet", "csharp-dotnet", "strogo-notation"]), "four arm manifests");
            Check(pilot.HiddenCorpusDigest == LivePilotCorpus.HiddenDigest && LivePilotCorpus.Hidden.Length >= 30, "hidden corpus frozen");
            Check(pilot.Runs.Single(x => x.RunId == "S1").PackageDigest == pilot.Runs.Single(x => x.RunId == "S2").PackageDigest, "strogo package parity");
            Check(pilot.Runs.Single(x => x.RunId == "C1").PackageDigest == pilot.Runs.Single(x => x.RunId == "C2").PackageDigest, "csharp package parity");
            Check(ThrowsCode(() => Prepare(root, fixtures, "e10-conformance"), "DestinationNotEmpty"), "prepare no overwrite");
            var firstRun = LivePilotPreparation.LoadAndVerifyRun(Path.Combine(root, "packages", "S1", "run-manifest.json"), pilot);
            var mutatedInvocation = firstRun.InvocationIdentity with { Arguments = firstRun.InvocationIdentity.Arguments.SetItem(0, "tampered") };
            Check(ThrowsCode(() => LivePilotProvenance.VerifyInvocation(mutatedInvocation, root, "S1", pilot.ImplementationIdentity), "InvocationIdentityMismatch"), "invocation mutation refused");
            var implementation = LivePilotProvenance.CreateImplementation(Environment.CurrentDirectory, Environment.ProcessPath!, Environment.Version.ToString(), Environment.ProcessPath!, Environment.ProcessPath!, Assembly.GetExecutingAssembly().Location);
            Check(implementation.SourceClosureDigest.Length == 64 && implementation.CodexNodeDigest.Length == 64, "implementation closure identity");
            Check(ThrowsCode(() => LivePilotProvenance.VerifyImplementation(implementation with { CoreAssemblyDigest = new string('0', 64) }, implementation), "ImplementationIdentityMismatch"), "implementation mutation refused");

            foreach (string runId in pilot.RunOrder)
            {
                bool strogo = runId.StartsWith('S');
                WriteFakeRun(root, pilot, runId, strogo ? ValidStrogo : ValidCSharp, forbiddenTool: false);
                var evaluation = LivePilotEvaluation.Evaluate(root, runId, fixtures);
                string dockerDiagnostic = File.Exists(Path.Combine(root, "runs", runId, "docker-stderr.txt")) ? File.ReadAllText(Path.Combine(root, "runs", runId, "docker-stderr.txt")) : string.Empty;
                Check(evaluation.Status == "BenchmarkAccepted", $"valid candidate {runId}: {evaluation.Code}: {dockerDiagnostic}");
                Check(evaluation.AssuranceLevel == "FiniteCorpusEvaluation", $"finite assurance {runId}");
                Check(evaluation.Metrics.ToolCalls == 0 && evaluation.Metrics.AttemptCount == 1, $"zero-tool metrics {runId}");
                Check(CanonicalJson.Encode(LivePilotEvaluation.Evaluate(root, runId, fixtures)).AsSpan().SequenceEqual(CanonicalJson.Encode(evaluation)), $"idempotent evaluation {runId}");
            }

            var firstReport = LivePilotReporting.Build(root);
            var secondReport = LivePilotReporting.Build(root);
            Check(firstReport.Status == "Completed", "completed report");
            Check(firstReport.ClaimBoundary == "PilotOnlyNoG05", "claim boundary");
            Check(firstReport.EffectiveModelEvidence == "NotReportedByRuntime", "missing backend explicit");
            Check(firstReport.Pairs.Length == 2 && firstReport.Pairs.All(x => x.Comparable), "paired deltas");
            Check(CanonicalJson.Encode(firstReport).SequenceEqual(CanonicalJson.Encode(secondReport)), "deterministic aggregation");
            string reportPath = Path.Combine(root, "report.json");
            LivePilotReporting.Write(root, reportPath);
            LivePilotReporting.Write(root, reportPath);

            var invalidPilot = Prepare(invalidRoot, fixtures, "e10-invalid-policy");
            WriteFakeRun(invalidRoot, invalidPilot, "S1", ValidStrogo, forbiddenTool: true);
            var invalid = LivePilotEvaluation.Evaluate(invalidRoot, "S1", fixtures);
            Check(invalid.Status == "Invalid" && invalid.Code == "InvalidPolicy", "forbidden tool precedes candidate");
            RunTerminalAbortRegression(fixtures, additionalRoots, "S1", Check);
            RunTerminalAbortRegression(fixtures, additionalRoots, "C1", Check);

            Check(EvaluatePolicyVariant(fixtures, additionalRoots, "unknown-event", "{\"type\":\"future.event\"}\n", false, 0).Code == "InvalidPolicy", "unknown top-level event fail closed");
            Check(EvaluatePolicyVariant(fixtures, additionalRoots, "unknown-item", "{\"type\":\"item.started\",\"item\":{\"type\":\"future_tool\"}}\n", false, 0).Code == "InvalidPolicy", "unknown item fail closed");
            Check(EvaluatePolicyVariant(fixtures, additionalRoots, "tool-timeout", "{\"type\":\"item.started\",\"item\":{\"type\":\"command_execution\"}}\n", true, -1).Code == "InvalidPolicy", "policy violation precedes timeout");
            Check(EvaluatePolicyVariant(fixtures, additionalRoots, "tool-exit", "{\"type\":\"item.started\",\"item\":{\"type\":\"command_execution\"}}\n", false, 17).Code == "InvalidPolicy", "policy violation precedes exit");
            Check(EvaluatePolicyVariant(fixtures, additionalRoots, "event-order", "{\"type\":\"turn.completed\"}\n", false, 0).Code == "InvalidEventSequence", "event sequence fail closed");
            Check(EvaluatePolicyVariant(fixtures, additionalRoots, "item-missing", "{\"type\":\"item.updated\"}\n", false, 0).Code == "InvalidEventSequence", "item event requires structured item");
            var runtimeErrorItem = EvaluatePolicyVariant(fixtures, additionalRoots, "runtime-error-item", "{\"type\":\"item.completed\",\"item\":{\"id\":\"runtime-error\",\"type\":\"error\",\"message\":\"transport fallback\"}}\n", false, 0);
            Check(runtimeErrorItem.Code == "InvalidEventSequence" && runtimeErrorItem.Metrics.ToolCalls == 0, "runtime error item is not a model tool call");
            Check(EvaluateOutputLimitVariant(fixtures, additionalRoots).Code == "OutputLimitExceeded", "agent output limit typed");

            string missingRoot = Path.Combine(Path.GetTempPath(), $"strogo-e10-missing-raw-{Guid.NewGuid():N}");
            additionalRoots.Add(missingRoot);
            var missingPilot = Prepare(missingRoot, fixtures, "e10-missing-raw");
            foreach (string runId in missingPilot.RunOrder)
            {
                Directory.CreateDirectory(Path.Combine(missingRoot, "runs", runId));
                Check(LivePilotEvaluation.Evaluate(missingRoot, runId, fixtures).Code == "RawEvidenceMissing", $"missing raw typed {runId}");
            }
            Check(LivePilotReporting.Build(missingRoot).Status == "Invalid", "missing raw report remains canonical");

            string promptPath = Path.Combine(root, "packages", "S1", "prompt.txt");
            byte[] originalPrompt = File.ReadAllBytes(promptPath);
            File.AppendAllText(promptPath, "mutation", new UTF8Encoding(false));
            Check(ThrowsCode(() => LivePilotPreparation.LoadAndVerify(root), "PreparationInventoryMismatch"), "package mutation refused");
            File.WriteAllBytes(promptPath, originalPrompt);
            _ = LivePilotPreparation.LoadAndVerify(root);

            string promptInputPath = Path.Combine(root, "runs", "S1", "prompt-input.txt");
            byte[] promptInputOriginal = File.ReadAllBytes(promptInputPath);
            string normalizedBefore = PromptInputInspector.NormalizeDigest(promptInputPath, File.ReadAllBytes(promptPath), Path.Combine(root, "sessions", "S1"));
            File.WriteAllText(promptInputPath, File.ReadAllText(promptInputPath).Replace("shared developer context", "mutated developer context", StringComparison.Ordinal), new UTF8Encoding(false));
            string normalizedAfter = PromptInputInspector.NormalizeDigest(promptInputPath, File.ReadAllBytes(promptPath), Path.Combine(root, "sessions", "S1"));
            Check(normalizedBefore != normalizedAfter, "prompt normalizer preserves nonvolatile fields");
            File.WriteAllBytes(promptInputPath, promptInputOriginal);
            foreach (string invalidCreateTime in new[] { "{}", "[]" })
            {
                string mutatedCreateTime = new System.Text.RegularExpressions.Regex("\\\"create_time\\\":[0-9.]+", System.Text.RegularExpressions.RegexOptions.CultureInvariant).Replace(File.ReadAllText(promptInputPath), $"\"create_time\":{invalidCreateTime}", 1);
                File.WriteAllText(promptInputPath, mutatedCreateTime, new UTF8Encoding(false));
                Check(ThrowsCode(() => PromptInputInspector.NormalizeDigest(promptInputPath, File.ReadAllBytes(promptPath), Path.Combine(root, "sessions", "S1")), "PromptInputInvalid"), $"create_time {invalidCreateTime} schema drift refused");
                File.WriteAllBytes(promptInputPath, promptInputOriginal);
            }
            string duplicateCreateTime = File.ReadAllText(promptInputPath).Replace("\"create_time\":", "\"create_time\":0,\"create_time\":", StringComparison.Ordinal);
            File.WriteAllText(promptInputPath, duplicateCreateTime, new UTF8Encoding(false));
            Check(ThrowsCode(() => PromptInputInspector.NormalizeDigest(promptInputPath, File.ReadAllBytes(promptPath), Path.Combine(root, "sessions", "S1")), "PromptInputInvalid"), "duplicate prompt field refused");
            File.WriteAllBytes(promptInputPath, promptInputOriginal);

            File.WriteAllText(promptInputPath, File.ReadAllText(promptInputPath).Replace("shared developer context", pilot.HiddenCorpusDigest, StringComparison.Ordinal), new UTF8Encoding(false));
            Check(ThrowsCode(() => LivePilotReporting.ValidatePromptInputs(root), "TrustedContentLeak"), "hidden digest leak refused before live");
            File.WriteAllBytes(promptInputPath, promptInputOriginal);
            string reformattedExpected = ValidCSharp.Replace(" ", "  ", StringComparison.Ordinal);
            File.WriteAllText(promptInputPath, File.ReadAllText(promptInputPath).Replace("shared developer context", reformattedExpected, StringComparison.Ordinal), new UTF8Encoding(false));
            Check(ThrowsCode(() => LivePilotReporting.ValidatePromptInputs(root), "TrustedContentLeak"), "normalized expected candidate leak refused before live");
            File.WriteAllBytes(promptInputPath, promptInputOriginal);
            foreach (string partialLeak in new[] { "hidden-boundary-equal-one", "hidden-seeded-00", "BuildHidden" })
            {
                File.WriteAllText(promptInputPath, File.ReadAllText(promptInputPath).Replace("shared developer context", partialLeak, StringComparison.Ordinal), new UTF8Encoding(false));
                Check(ThrowsCode(() => LivePilotReporting.ValidatePromptInputs(root), "TrustedContentLeak"), $"partial hidden leak refused: {partialLeak}");
                File.WriteAllBytes(promptInputPath, promptInputOriginal);
            }
            foreach (var hiddenRow in new[] { LivePilotCorpus.Hidden.First(x => x.Set == "boundary"), LivePilotCorpus.Hidden.First(x => x.Set == "seeded") })
            {
                string canonicalRow = Encoding.UTF8.GetString(CanonicalJson.Encode(hiddenRow));
                CheckPromptLeak(root, promptInputPath, promptInputOriginal, canonicalRow, Check, $"canonical hidden row refused: {hiddenRow.Id}");
            }
            CheckPromptLeak(root, promptInputPath, promptInputOriginal, "state ^= state << 13", Check, "hidden PRNG source fragment refused");

            string forbiddenInventoryPath = Path.Combine(root, "forbidden-prompt-inventory.json");
            byte[] forbiddenInventoryOriginal = File.ReadAllBytes(forbiddenInventoryPath);
            var forbiddenInventory = LivePilotJson.ReadCanonical<ForbiddenPromptInventory>(forbiddenInventoryPath);
            LivePilotJson.WriteCanonical(forbiddenInventoryPath, forbiddenInventory with { ExactNeedles = forbiddenInventory.ExactNeedles.SetItem(0, "tampered") }, overwrite: true);
            Check(ThrowsCode(() => LivePilotPreparation.LoadAndVerify(root), "ForbiddenPromptInventoryMismatch"), "forbidden inventory mutation refused");
            File.WriteAllBytes(forbiddenInventoryPath, forbiddenInventoryOriginal);

            string parityPath = Path.Combine(root, "parity-review.json");
            byte[] parityOriginal = File.ReadAllBytes(parityPath);
            var parity = LivePilotJson.ReadCanonical<ParityReview>(parityPath);
            LivePilotJson.WriteCanonical(parityPath, parity with { Status = "tampered" }, overwrite: true);
            Check(ThrowsCode(() => LivePilotPreparation.LoadAndVerify(root), "ParityReviewMismatch"), "parity mutation refused");
            File.WriteAllBytes(parityPath, parityOriginal);
            File.Delete(parityPath);
            Check(ThrowsCode(() => LivePilotPreparation.LoadAndVerify(root), "ParityReviewMissing"), "parity deletion refused");
            File.WriteAllBytes(parityPath, parityOriginal);

            string preparationInventoryPath = Path.Combine(root, "inventory.json");
            byte[] preparationInventoryOriginal = File.ReadAllBytes(preparationInventoryPath);
            var preparationInventory = LivePilotJson.ReadCanonical<PackageInventory>(preparationInventoryPath);
            LivePilotJson.WriteCanonical(preparationInventoryPath, preparationInventory with { Files = preparationInventory.Files.SetItem(0, preparationInventory.Files[0] with { Digest = new string('0', 64) }) }, overwrite: true);
            Check(ThrowsCode(() => LivePilotPreparation.LoadAndVerify(root), "PreparationInventoryMismatch"), "preparation inventory mutation refused");
            File.WriteAllBytes(preparationInventoryPath, preparationInventoryOriginal);
            File.Delete(preparationInventoryPath);
            Check(ThrowsCode(() => LivePilotPreparation.LoadAndVerify(root), "PreparationInventoryMissing"), "preparation inventory deletion refused");
            File.WriteAllBytes(preparationInventoryPath, preparationInventoryOriginal);

            string dockerStderr = Path.Combine(root, "runs", "C1", "docker-stderr.txt");
            byte[] dockerStderrOriginal = File.ReadAllBytes(dockerStderr);
            File.AppendAllText(dockerStderr, "mutation", new UTF8Encoding(false));
            Check(ThrowsCode(() => LivePilotReporting.Build(root), "RawEvidenceMutated"), "docker evidence mutation refused");
            File.WriteAllBytes(dockerStderr, dockerStderrOriginal);
            string dockerReceipt = Path.Combine(root, "runs", "C1", "docker-receipt.json");
            byte[] dockerReceiptOriginal = File.ReadAllBytes(dockerReceipt);
            File.Delete(dockerReceipt);
            Check(ThrowsCode(() => LivePilotReporting.Build(root), "RawEvidenceReadFailure"), "docker evidence deletion refused");
            File.WriteAllBytes(dockerReceipt, dockerReceiptOriginal);

            string evaluationPath = Path.Combine(root, "runs", "S1", "evaluation.json");
            byte[] evaluationOriginal = File.ReadAllBytes(evaluationPath);
            var evaluationMutation = LivePilotJson.ReadCanonical<CandidateEvaluation>(evaluationPath) with { Code = "mutation" };
            LivePilotJson.WriteCanonical(evaluationPath, evaluationMutation, overwrite: true);
            Check(ThrowsCode(() => LivePilotEvaluation.Evaluate(root, "S1", fixtures), "EvaluationIdentityMismatch"), "evaluation mutation refused");
            File.WriteAllBytes(evaluationPath, evaluationOriginal);

            string secretRoot = Path.Combine(Path.GetTempPath(), $"strogo-e10-secret-{Guid.NewGuid():N}");
            additionalRoots.Add(secretRoot);
            Directory.CreateDirectory(Path.Combine(secretRoot, "nested"));
            File.WriteAllText(Path.Combine(secretRoot, "nested", "leak.txt"), "authorization: bearer example", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(secretRoot, "nested", "forward.txt"), "C:/Obsidian/private/file.txt", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(secretRoot, "nested", "escaped.json"), "{\"path\":\"C:\\\\Private\\\\file.txt\"}", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(secretRoot, "nested", "unc.txt"), @"\\server\share\private.txt", new UTF8Encoding(false));
            string allowedRoot = Path.Combine(secretRoot, "allowed");
            Directory.CreateDirectory(allowedRoot);
            File.WriteAllText(Path.Combine(secretRoot, "nested", "allowed.txt"), Path.Combine(allowedRoot, "file.txt"), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(secretRoot, "nested", "neighbor.txt"), allowedRoot + "-other\\secret.txt", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(secretRoot, "nested", "traversal.txt"), Path.Combine(allowedRoot, "..", "private", "secret.txt"), new UTF8Encoding(false));
            var sensitiveFindings = LivePilotEvidence.ScanSensitive(secretRoot, allowedRoot);
            Check(sensitiveFindings.Contains("secret:nested/leak.txt"), "recursive secret finding");
            Check(sensitiveFindings.Contains("private-path:nested/forward.txt") && sensitiveFindings.Contains("private-path:nested/escaped.json") && sensitiveFindings.Contains("private-path:nested/unc.txt"), "forward escaped and UNC private paths found");
            Check(!sensitiveFindings.Contains("private-path:nested/allowed.txt") && sensitiveFindings.Contains("private-path:nested/neighbor.txt") && sensitiveFindings.Contains("private-path:nested/traversal.txt"), "allowed path boundary and traversal enforced");
            string quarantined = LivePilotReporting.Quarantine(secretRoot);
            additionalRoots.Add(quarantined);
            Check(!Directory.Exists(secretRoot) && File.Exists(Path.Combine(quarantined, "nested", "leak.txt")), "secret evidence moved to quarantine");
            Check(LivePilotProvenance.GitStatusIsClean(string.Empty) && !LivePilotProvenance.GitStatusIsClean("?? untracked"), "dirty worktree guard");
            string syntheticRepository = Path.Combine(Path.GetTempPath(), $"strogo-e10-root-{Guid.NewGuid():N}", "repo");
            Check(!LivePilotProvenance.EvidenceRootIsOutsideRepository(syntheticRepository, syntheticRepository), "evidence root rejects repository root");
            Check(!LivePilotProvenance.EvidenceRootIsOutsideRepository(syntheticRepository, Path.Combine(syntheticRepository, "artifacts", "pilot")), "evidence root rejects repository descendant");
            Check(!LivePilotProvenance.EvidenceRootIsOutsideRepository(syntheticRepository, Path.Combine(syntheticRepository, "child", "..", "pilot")), "evidence root resolves traversal into repository");
            Check(LivePilotProvenance.EvidenceRootIsOutsideRepository(syntheticRepository, syntheticRepository + "-evidence"), "evidence root accepts external sibling");

            RunMaliciousMatrix(fixtures, root, pilot.DockerIdentity, Check);
        }
        finally
        {
            if (failures.Count == failuresBeforeE10)
            {
                TryDelete(root);
                TryDelete(invalidRoot);
                foreach (string additional in additionalRoots) TryDelete(additional);
            }
            else failures.Add($"E10 diagnostics retained at {root} and {invalidRoot}");
        }
    }

    private static PilotManifest Prepare(string root, string fixtures, string pilotId)
    {
        var pilot = LivePilotPreparation.Prepare(new(
            root,
            fixtures,
            pilotId,
            "2026-09-27T00:00:00.0000000+00:00",
            new string('a', 40),
            "codex-cli 0.154.0",
            ImageId,
            new ImplementationIdentity(new string('1', 64), new string('2', 64), new string('3', 64), new string('4', 64), new string('5', 64), new string('6', 64), "10.0.400", new string('7', 64), new string('8', 64))));
        LivePilotAcl.Restrict(root);
        return pilot;
    }

    private static CandidateEvaluation EvaluatePolicyVariant(string fixtures, List<string> roots, string name, string extraEvent, bool timedOut, int exitCode)
    {
        string root = Path.Combine(Path.GetTempPath(), $"strogo-e10-{name}-{Guid.NewGuid():N}");
        roots.Add(root);
        var pilot = Prepare(root, fixtures, $"e10-{name}");
        WriteFakeRun(root, pilot, "S1", ValidStrogo, false, extraEvent, timedOut, exitCode);
        return LivePilotEvaluation.Evaluate(root, "S1", fixtures);
    }

    private static CandidateEvaluation EvaluateOutputLimitVariant(string fixtures, List<string> roots)
    {
        string root = Path.Combine(Path.GetTempPath(), $"strogo-e10-output-limit-{Guid.NewGuid():N}");
        roots.Add(root);
        var pilot = Prepare(root, fixtures, "e10-output-limit");
        WriteFakeRun(root, pilot, "S1", ValidStrogo, false, outputLimitExceeded: true);
        return LivePilotEvaluation.Evaluate(root, "S1", fixtures);
    }

    private static void RunTerminalAbortRegression(string fixtures, List<string> roots, string failedRunId, Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), $"strogo-e10-abort-{failedRunId}-{Guid.NewGuid():N}");
        roots.Add(root);
        var pilot = Prepare(root, fixtures, $"e10-abort-{failedRunId.ToLowerInvariant()}");
        foreach (string runId in pilot.RunOrder) WriteFakePromptInput(root, runId);
        foreach (string runId in pilot.RunOrder.TakeWhile(runId => runId != failedRunId))
        {
            WriteFakeRun(root, pilot, runId, runId.StartsWith('S') ? ValidStrogo : ValidCSharp, false);
            check(LivePilotEvaluation.Evaluate(root, runId, fixtures).Status == "BenchmarkAccepted", $"abort setup accepted {failedRunId}/{runId}");
        }
        WriteFakeRun(root, pilot, failedRunId, failedRunId.StartsWith('S') ? ValidStrogo : ValidCSharp, true);
        var failure = LivePilotEvaluation.Evaluate(root, failedRunId, fixtures);
        check(failure.Status == "Invalid", $"abort trigger invalid {failedRunId}");
        var skipped = LivePilotEvaluation.RecordRemainingNotRun(root, failedRunId);
        check(skipped.All(x => x.Status == "Invalid" && x.Code == "NotRunDueToPriorFailure"), $"abort remaining typed {failedRunId}");
        var report = LivePilotReporting.Build(root);
        check(report.Status == "Invalid" && report.Runs.Length == 4 && report.Runs.SkipWhile(x => x.RunId != failedRunId).Skip(1).All(x => x.Code == "NotRunDueToPriorFailure"), $"abort canonical report {failedRunId}");
    }

    private static void WriteFakeRun(string root, PilotManifest pilot, string runId, string candidate, bool forbiddenTool, string extraEvent = "", bool timedOut = false, int exitCode = 0, bool outputLimitExceeded = false)
    {
        var run = LivePilotPreparation.LoadAndVerifyRun(Path.Combine(root, "packages", runId, "run-manifest.json"), pilot);
        string raw = Path.Combine(root, "runs", runId);
        Directory.CreateDirectory(raw);
        string events = "{\"type\":\"thread.started\",\"thread_id\":\"fixture\"}\n" +
            "{\"type\":\"turn.started\"}\n" +
            (forbiddenTool ? "{\"type\":\"item.started\",\"item\":{\"id\":\"tool-1\",\"type\":\"command_execution\",\"command\":\"forbidden\"}}\n" : string.Empty) +
            extraEvent +
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"message-1\",\"type\":\"agent_message\",\"text\":\"candidate returned\"}}\n" +
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":100,\"output_tokens\":50,\"cached_input_tokens\":0,\"reasoning_tokens\":10}}\n";
        File.WriteAllText(Path.Combine(raw, "stdout.jsonl"), events, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(raw, "stderr.txt"), string.Empty, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(raw, "response.json"), $"{{\"status\":\"Candidate\",\"candidateSource\":{System.Text.Json.JsonSerializer.Serialize(candidate)},\"question\":\"\"}}", new UTF8Encoding(false));
        WriteFakePromptInput(root, runId);
        var acl = LivePilotAcl.Inspect(root, pilot);
        LivePilotJson.WriteCanonical(Path.Combine(raw, "acl-receipt.json"), acl);
        var retention = new RetentionReceipt(pilot.Protocol, pilot.PilotId, pilot.RetentionUntilUtc, "ExplicitOnly");
        LivePilotJson.WriteCanonical(Path.Combine(raw, "retention-receipt.json"), retention);
        var envValues = run.InvocationIdentity.EnvironmentVariables.Select(name => new EnvironmentValueDigest(name, true, CanonicalJson.RawDigest(Encoding.UTF8.GetBytes($"fixture:{name}")))).ToImmutableArray();
        var envSkeleton = new EnvironmentReceipt(pilot.Protocol, pilot.PilotId, run.InvocationIdentity.EnvironmentVariables, envValues, string.Empty);
        var environment = envSkeleton with { Digest = CanonicalJson.RawDigest(CanonicalJson.Encode(envSkeleton)) };
        string envPath = Path.Combine(raw, "environment-receipt.json");
        LivePilotJson.WriteCanonical(envPath, environment);
        var receipt = new ProcessReceipt(
            pilot.Protocol,
            pilot.PilotId,
            runId,
            run.ProcessAttemptId,
            run.InvocationIdentity,
            run.RequestedRuntime,
            run.PromptDigest,
            run.ResponseSchemaDigest,
            "2026-09-27T00:00:00.0000000+00:00",
            "2026-09-27T00:00:01.0000000+00:00",
            1000,
            exitCode,
            timedOut,
            outputLimitExceeded,
            Digest(raw, "stdout.jsonl"),
            Digest(raw, "stderr.txt"),
            Digest(raw, "response.json"),
            Digest(raw, "prompt-input.txt"),
            CanonicalJson.RawDigest(File.ReadAllBytes(envPath)));
        LivePilotJson.WriteCanonical(Path.Combine(raw, "process-receipt.json"), receipt);
    }

    private static void WriteFakePromptInput(string root, string runId)
    {
        string raw = Path.Combine(root, "runs", runId);
        Directory.CreateDirectory(raw);
        string prompt = File.ReadAllText(Path.Combine(root, "packages", runId, "prompt.txt"), new UTF8Encoding(false, true));
        string session = Path.Combine(root, "sessions", runId);
        string promptInput = System.Text.Json.JsonSerializer.Serialize(new object[]
        {
            new { type = "message", id = $"volatile-{runId}", role = "developer", content = new[] { new { type = "input_text", text = "shared developer context" } }, internal_chat_message_metadata_passthrough = new { create_time = runId.StartsWith('S') ? 1790521515.7801986 : 1790521515.7804544, unicode_context = "контекст — shared" } },
            new { type = "message", id = $"environment-{runId}", role = "user", content = new[] { new { type = "input_text", text = $"cwd={session}; workspace={session}" } } },
            new { type = "message", id = $"prompt-{runId}", role = "user", content = new[] { new { type = "input_text", text = prompt } } }
        });
        File.WriteAllText(Path.Combine(raw, "prompt-input.txt"), promptInput, new UTF8Encoding(false));
    }

    private static void RunMaliciousMatrix(string fixtures, string root, DockerIdentity docker, Action<bool, string> check)
    {
        var candidates = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["filesystem"] = "public static class Candidate { public static CandidateResult Execute(long a,long q) { System.IO.File.WriteAllText(\"/tmp/e10-contained\",\"x\"); try { System.IO.File.WriteAllText(\"/input/host-marker\",\"x\"); } catch { } bool ok=q<=a; return ok?new(true,checked(a-q),q):new(false,a,0); } }",
            ["network"] = "public static class Candidate { public static CandidateResult Execute(long a,long q) { try { using var c=new System.Net.Sockets.TcpClient(); c.Connect(\"1.1.1.1\",80); } catch { } bool ok=q<=a; return ok?new(true,checked(a-q),q):new(false,a,0); } }",
            ["process"] = "public static class Candidate { public static CandidateResult Execute(long a,long q) { try { System.Diagnostics.Process.Start(\"/bin/true\")?.WaitForExit(); } catch { } bool ok=q<=a; return ok?new(true,checked(a-q),q):new(false,a,0); } }",
            ["reflection"] = "public static class Candidate { public static CandidateResult Execute(long a,long q) { _=typeof(Candidate).Assembly.GetTypes(); bool ok=q<=a; return ok?new(true,checked(a-q),q):new(false,a,0); } }",
            ["static-initializer"] = "public static class Candidate { static readonly int X=Init(); static int Init(){System.IO.File.WriteAllText(\"/tmp/static-contained\",\"x\");return 1;} public static CandidateResult Execute(long a,long q){_=X;bool ok=q<=a;return ok?new(true,checked(a-q),q):new(false,a,0);} }",
            ["unchecked"] = "public static class Candidate { public static CandidateResult Execute(long a,long q){bool ok=q<=a;long v=ok?unchecked(a-q):a;return new(ok,v,ok?q:0);} }",
            ["infinite-loop"] = "public static class Candidate { public static CandidateResult Execute(long a,long q){while(true){} } }",
            ["environment-exit"] = "public static class Candidate { public static CandidateResult Execute(long a,long q){System.Environment.Exit(17);return new(false,0,0);} }",
            ["oversized-output"] = "public static class Candidate { public static CandidateResult Execute(long a,long q){System.Console.Write(new string('x', 2097152));bool ok=q<=a;return ok?new(true,checked(a-q),q):new(false,a,0);} }"
            ,["fast-stdout-exit"] = "public static class Candidate { public static CandidateResult Execute(long a,long q){System.Console.Write(new string('x', 1048577));System.Environment.Exit(0);return new(false,0,0);} }"
            ,["fast-stderr-exit"] = "public static class Candidate { public static CandidateResult Execute(long a,long q){System.Console.Error.Write(new string('x', 1048577));System.Environment.Exit(0);return new(false,0,0);} }"
        };
        foreach (var item in candidates)
        {
            string raw = Path.Combine(root, "malicious", item.Key);
            Directory.CreateDirectory(raw);
            var result = DockerCandidateRunner.Run(Encoding.UTF8.GetBytes(item.Value), [new("probe", 10, 3, "malicious")], docker, raw, fixtures, timeoutSeconds: item.Key == "infinite-loop" ? 20 : 90);
            if (item.Key.StartsWith("fast-", StringComparison.Ordinal)) check(result.Code == "OutputLimitExceeded", $"malicious {item.Key} fast-exit output limit");
            else check(result.Success || result.Code is "CandidateBuildOrRuntimeFailure" or "CandidateTimeout" or "CandidateOutputInvalid" or "OutputLimitExceeded", $"malicious {item.Key} typed containment");
            check(!File.Exists(Path.Combine(raw, "docker-input", "host-marker")), $"malicious {item.Key} no host marker");
        }
    }

    private static string Digest(string directory, string file) => CanonicalJson.RawDigest(File.ReadAllBytes(Path.Combine(directory, file)));
    private static void CheckPromptLeak(string root, string promptInputPath, byte[] original, string leak, Action<bool, string> check, string name)
    {
        string json = new UTF8Encoding(false, true).GetString(original);
        string replacement = System.Text.Json.JsonSerializer.Serialize(leak);
        File.WriteAllText(promptInputPath, json.Replace("\"shared developer context\"", replacement, StringComparison.Ordinal), new UTF8Encoding(false));
        check(ThrowsCode(() => LivePilotReporting.ValidatePromptInputs(root), "TrustedContentLeak"), name);
        File.WriteAllBytes(promptInputPath, original);
    }
    private static bool ThrowsCode(Action action, string code)
    {
        try { action(); return false; }
        catch (KernelException error) { return error.Error.Code == code; }
    }
    private static void TryDelete(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { } }
}
