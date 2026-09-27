using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Kernel.Core;
using Strogo.Notation;

namespace Strogo.Experiments;

public static class LivePilotEvaluation
{
    private static readonly string[] RawFiles = ["acl-receipt.json", "environment-receipt.json", "process-receipt.json", "prompt-input.txt", "response.json", "retention-receipt.json", "stderr.txt", "stdout.jsonl"];

    public static CandidateEvaluation Evaluate(string directory, string runId, string fixtureDirectory)
    {
        var stopwatch = Stopwatch.StartNew();
        string root = Path.GetFullPath(directory);
        var pilot = LivePilotPreparation.LoadAndVerify(root);
        var reference = pilot.Runs.SingleOrDefault(x => x.RunId == runId) ?? throw LivePilotJson.Failure("RunUnknown");
        string packageDirectory = Path.Combine(root, "packages", runId);
        var run = LivePilotPreparation.LoadAndVerifyRun(Path.Combine(packageDirectory, "run-manifest.json"), pilot);
        var forbidden = LivePilotLeakage.LoadAndVerifyInventory(root, pilot);
        string rawDirectory = Path.Combine(root, "runs", runId);
        string evaluationPath = Path.Combine(rawDirectory, "evaluation.json");
        if (File.Exists(evaluationPath))
        {
            _ = LivePilotEvidence.Verify(rawDirectory, pilot.PilotId, run.RunId);
            return LoadAndVerify(evaluationPath, pilot);
        }
        foreach (string name in RawFiles)
            if (!File.Exists(Path.Combine(rawDirectory, name))) return Write(Early("Invalid", "RawEvidenceMissing", [], EmptyMetrics(stopwatch.ElapsedMilliseconds), string.Empty), rawDirectory, pilot.PilotId, run.RunId);
        ProcessReceipt receipt;
        RunEventSummary events;
        try
        {
            receipt = LivePilotJson.ReadCanonical<ProcessReceipt>(Path.Combine(rawDirectory, "process-receipt.json"));
            ValidateReceipt(receipt, pilot, run, rawDirectory);
            events = CodexEventParser.Parse(Path.Combine(rawDirectory, "stdout.jsonl"));
            _ = PromptInputInspector.NormalizeDigest(
                Path.Combine(rawDirectory, "prompt-input.txt"),
                File.ReadAllBytes(Path.Combine(packageDirectory, "prompt.txt")),
                Path.Combine(root, "sessions", runId));
            LivePilotLeakage.ValidatePromptInput(Path.Combine(rawDirectory, "prompt-input.txt"), forbidden);
        }
        catch (KernelException e)
        {
            return Write(Early("Invalid", e.Error.Code, [], EmptyMetrics(stopwatch.ElapsedMilliseconds), string.Empty), rawDirectory, pilot.PilotId, run.RunId);
        }
        catch (Exception)
        {
            return Write(Early("InfrastructureFailure", "RawEvidenceReadFailure", [], EmptyMetrics(stopwatch.ElapsedMilliseconds), string.Empty), rawDirectory, pilot.PilotId, run.RunId);
        }

        var metrics = Metrics(receipt, events, stopwatch.ElapsedMilliseconds, false);
        if (events.ToolCalls != 0 || events.CandidateWrites != 0 || events.UnknownEvents != 0)
            return Write(Early("Invalid", "InvalidPolicy", [], metrics, string.Empty), rawDirectory, pilot.PilotId, run.RunId);
        if (receipt.OutputLimitExceeded) return Write(Early("InfrastructureFailure", "OutputLimitExceeded", [], metrics, string.Empty), rawDirectory, pilot.PilotId, run.RunId);
        if (receipt.TimedOut) return Write(Early("InfrastructureFailure", events.TurnStarted ? "RunTimeoutAfterExposure" : "PreExposureInfrastructureFailure", [], metrics, string.Empty), rawDirectory, pilot.PilotId, run.RunId);
        if (receipt.ExitCode != 0) return Write(Early("InfrastructureFailure", events.TurnStarted ? "AgentProcessFailure" : "PreExposureInfrastructureFailure", [], metrics, string.Empty), rawDirectory, pilot.PilotId, run.RunId);
        if (!events.TurnStarted) return Write(Early("Invalid", "TurnStartedMissing", [], metrics, string.Empty), rawDirectory, pilot.PilotId, run.RunId);
        if (!events.TurnCompleted || events.ModelTurns != 1 || events.AgentMessages != 1 || events.ErrorEvents != 0 || events.SequenceViolations != 0)
            return Write(Early("Invalid", "InvalidEventSequence", [], metrics, string.Empty), rawDirectory, pilot.PilotId, run.RunId);
        if (events.EffectiveModel is not null && events.EffectiveModel != run.RequestedRuntime.Model)
            return Write(Early("Invalid", "EffectiveModelMismatch", [], metrics, string.Empty), rawDirectory, pilot.PilotId, run.RunId);
        AgentResponse response;
        try { response = ParseAgentResponse(Path.Combine(rawDirectory, "response.json")); }
        catch (KernelException e) { return Write(Early("Invalid", e.Error.Code, [], metrics, string.Empty), rawDirectory, pilot.PilotId, run.RunId); }

        if (response.Status == "NeedsHuman")
        {
            metrics = metrics with { HumanActiveSeconds = null, HumanWaitSeconds = null };
            return Write(Early("NeedsHuman", "AgentQuestion", [], metrics, string.Empty), rawDirectory, pilot.PilotId, run.RunId);
        }
        if (response.Status == "Refused") return Write(Early("Refused", "AgentRefused", [], metrics, string.Empty), rawDirectory, pilot.PilotId, run.RunId);

        byte[] candidate = new UTF8Encoding(false, true).GetBytes(response.CandidateSource);
        if (candidate.Length is 0 or > 128 * 1024 || candidate.AsSpan().Contains((byte)0))
            return Write(Early("Refused", "CandidateLimitExceeded", [], metrics, string.Empty), rawDirectory, pilot.PilotId, run.RunId);
        string candidateDigest = CanonicalJson.RawDigest(candidate);
        File.WriteAllBytes(Path.Combine(rawDirectory, run.TargetFilename), candidate);

        var stages = ImmutableArray.CreateBuilder<CandidateStage>();
        string status;
        string? code;
        string adapterEvidenceDigest;
        int publicCount = LivePilotCorpus.Public.Length;
        int boundaryCount = LivePilotCorpus.Hidden.Count(x => x.Set == "boundary");
        int seededCount = LivePilotCorpus.Hidden.Count(x => x.Set == "seeded");
        try
        {
            (bool accepted, string? refusal, string adapterDigest) = run.Arm switch
            {
                "strogo-notation" => EvaluateStrogo(candidate, stages),
                "csharp-dotnet" => EvaluateCSharp(candidate, stages, pilot, run, rawDirectory, fixtureDirectory),
                _ => throw LivePilotJson.Failure("ArmUnknown")
            };
            status = accepted ? "BenchmarkAccepted" : "Refused";
            code = refusal;
            adapterEvidenceDigest = adapterDigest;
        }
        catch (KernelException e) { status = "Refused"; code = e.Error.Code; adapterEvidenceDigest = string.Empty; stages.Add(new("candidate-evaluator", "Refused", code)); }
        catch (Exception) { status = "InfrastructureFailure"; code = "EvaluatorException"; adapterEvidenceDigest = string.Empty; stages.Add(new("candidate-evaluator", "InfrastructureFailure", code)); }
        stopwatch.Stop();
        metrics = Metrics(receipt, events, stopwatch.ElapsedMilliseconds, response.Status == "NeedsHuman");
        var skeleton = new CandidateEvaluation(
            LivePilotIdentity.EvaluationSchema,
            LivePilotIdentity.Protocol,
            pilot.PilotId,
            run.RunId,
            run.Arm,
            status,
            code,
            candidateDigest,
            LivePilotIdentity.AssuranceLevel,
            publicCount,
            boundaryCount,
            seededCount,
            stages.ToImmutable(),
            metrics,
            adapterEvidenceDigest,
            string.Empty,
            string.Empty);
        return Write(skeleton, rawDirectory, pilot.PilotId, run.RunId);

        CandidateEvaluation Early(string earlyStatus, string earlyCode, ImmutableArray<CandidateStage> earlyStages, RunMetrics earlyMetrics, string evidenceDigest) =>
            new(LivePilotIdentity.EvaluationSchema, LivePilotIdentity.Protocol, pilot.PilotId, run.RunId, run.Arm, earlyStatus, earlyCode, string.Empty,
                LivePilotIdentity.AssuranceLevel, 0, 0, 0, earlyStages, earlyMetrics, string.Empty, evidenceDigest, string.Empty);
    }

    public static CandidateEvaluation LoadAndVerify(string path, PilotManifest pilot)
    {
        var evaluation = LivePilotJson.ReadCanonical<CandidateEvaluation>(path);
        string digest = CanonicalJson.RawDigest(CanonicalJson.Encode(evaluation with { EvaluationDigest = string.Empty }));
        if (evaluation.SchemaVersion != LivePilotIdentity.EvaluationSchema || evaluation.Protocol != pilot.Protocol || evaluation.PilotId != pilot.PilotId || digest != evaluation.EvaluationDigest)
            throw LivePilotJson.Failure("EvaluationIdentityMismatch");
        return evaluation;
    }

    public static ImmutableArray<CandidateEvaluation> RecordRemainingNotRun(string directory, string priorRunId)
    {
        string root = Path.GetFullPath(directory);
        var pilot = LivePilotPreparation.LoadAndVerify(root);
        int priorIndex = pilot.RunOrder.IndexOf(priorRunId);
        if (priorIndex < 0) throw LivePilotJson.Failure("RunUnknown");
        string priorPath = Path.Combine(root, "runs", priorRunId, "evaluation.json");
        if (!File.Exists(priorPath)) throw LivePilotJson.Failure("PriorEvaluationMissing");
        var prior = LoadAndVerify(priorPath, pilot);
        if (prior.Status is not ("Invalid" or "InfrastructureFailure")) throw LivePilotJson.Failure("PriorEvaluationNotTerminalFailure");

        var results = ImmutableArray.CreateBuilder<CandidateEvaluation>();
        for (int index = priorIndex + 1; index < pilot.RunOrder.Length; index++)
        {
            string runId = pilot.RunOrder[index];
            string rawDirectory = Path.Combine(root, "runs", runId);
            Directory.CreateDirectory(rawDirectory);
            string evaluationPath = Path.Combine(rawDirectory, "evaluation.json");
            if (File.Exists(evaluationPath)) throw LivePilotJson.Failure("RunAlreadyTerminal");
            var run = pilot.Runs.Single(x => x.RunId == runId);
            var receipt = new NotRunReceipt(pilot.Protocol, pilot.PilotId, runId, priorRunId, prior.Status, "NotRunDueToPriorFailure");
            LivePilotJson.WriteCanonical(Path.Combine(rawDirectory, "not-run-receipt.json"), receipt);
            var skeleton = new CandidateEvaluation(
                LivePilotIdentity.EvaluationSchema,
                pilot.Protocol,
                pilot.PilotId,
                runId,
                run.Arm,
                prior.Status,
                receipt.Code,
                string.Empty,
                LivePilotIdentity.AssuranceLevel,
                0,
                0,
                0,
                [new("orchestration", "NotRun", receipt.Code)],
                EmptyMetrics(0),
                string.Empty,
                string.Empty,
                string.Empty);
            results.Add(Write(skeleton, rawDirectory, pilot.PilotId, runId));
        }
        return results.ToImmutable();
    }

    public static string ComputeRawDigest(string rawDirectory, string pilotId, string runId) => LivePilotEvidence.Verify(rawDirectory, pilotId, runId).InventoryDigest;

    private static CandidateEvaluation Write(CandidateEvaluation skeleton, string rawDirectory, string pilotId, string runId)
    {
        var inventory = LivePilotEvidence.Seal(rawDirectory, pilotId, runId);
        var withEvidence = skeleton with { RawEvidenceDigest = inventory.InventoryDigest, EvaluationDigest = string.Empty };
        var evaluation = withEvidence with { EvaluationDigest = CanonicalJson.RawDigest(CanonicalJson.Encode(withEvidence)) };
        string path = Path.Combine(rawDirectory, "evaluation.json");
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(CanonicalJson.Encode(evaluation))) throw LivePilotJson.Failure("EvaluationExistsMismatch");
            return evaluation;
        }
        LivePilotJson.WriteCanonical(path, evaluation);
        return evaluation;
    }

    private static (bool Accepted, string? Code, string AdapterDigest) EvaluateStrogo(byte[] candidate, ImmutableArray<CandidateStage>.Builder stages)
    {
        var compiled = NotationCompiler.Compile(candidate);
        if (!compiled.Accepted) { stages.Add(new("arm-adapter", "Refused", compiled.ErrorCode)); return (false, compiled.ErrorCode, CanonicalJson.RawDigest(candidate)); }
        stages.Add(new("arm-adapter", "Accepted", null));
        var validated = GraphValidator.Validate(compiled.Program!);
        stages.Add(new("graph-validator", "Accepted", null));
        var ir = Lowerer.Lower(validated);
        stages.Add(new("lowerer", "Accepted", null));
        foreach (var vector in LivePilotCorpus.Public.Concat(LivePilotCorpus.Hidden))
        {
            var input = new ReserveInput(vector.ResourceAvailable, vector.RequestedQuantity);
            var expected = ReserveOracle.Evaluate(input, out string? oracleError);
            var reference = ReferenceInterpreter.Evaluate(validated, input);
            var machine = IrInterpreter.Evaluate(ir, input);
            if (!Matches(reference, expected, oracleError) || !Matches(machine, expected, oracleError) || reference.Output != machine.Output || reference.Error?.Code != machine.Error?.Code)
            {
                stages.Add(new("oracle-compare", "Refused", "OutcomeMismatch"));
                return (false, "OutcomeMismatch", LivePilotJson.Digest(("candidate", candidate), ("program", CanonicalJson.Encode(compiled.Program!)), ("ir", CanonicalJson.Encode(ir))));
            }
        }
        stages.Add(new("public-vectors", "Accepted", null));
        stages.Add(new("hidden-boundary-vectors", "Accepted", null));
        stages.Add(new("seeded-vectors", "Accepted", null));
        stages.Add(new("oracle-compare", "Equivalent", null));
        return (true, null, LivePilotJson.Digest(("candidate", candidate), ("program", CanonicalJson.Encode(compiled.Program!)), ("ir", CanonicalJson.Encode(ir))));
    }

    private static (bool Accepted, string? Code, string AdapterDigest) EvaluateCSharp(byte[] candidate, ImmutableArray<CandidateStage>.Builder stages, PilotManifest pilot, RunManifest run, string rawDirectory, string fixtureDirectory)
    {
        stages.Add(new("source-limit", "Accepted", null));
        string resolvedImageId = DockerCandidateRunner.InspectImageId(run.DockerIdentity.ImageReference);
        if (resolvedImageId != run.DockerIdentity.ResolvedImageId) throw LivePilotJson.Failure("DockerImageIdentityMismatch");
        stages.Add(new("docker-image-identity", "Accepted", null));
        var vectors = LivePilotCorpus.Public.Concat(LivePilotCorpus.Hidden).ToImmutableArray();
        var execution = DockerCandidateRunner.Run(candidate, vectors, run.DockerIdentity, rawDirectory, fixtureDirectory, pilot.PilotId, run.RunId);
        stages.Add(new("docker-build-execute", execution.Success ? "Accepted" : "Refused", execution.Code));
        if (!execution.Success) return (false, execution.Code, execution.ReceiptDigest);
        if (execution.Outputs.Length != vectors.Length) return (false, "OutputCountMismatch", execution.ReceiptDigest);
        for (int i = 0; i < vectors.Length; i++)
        {
            var vector = vectors[i];
            var expected = ReserveOracle.Evaluate(new(vector.ResourceAvailable, vector.RequestedQuantity), out string? error);
            if (error is not null || expected is null || execution.Outputs[i] != expected)
            {
                stages.Add(new("oracle-compare", "Refused", "OutcomeMismatch"));
                return (false, "OutcomeMismatch", execution.ReceiptDigest);
            }
        }
        stages.Add(new("public-vectors", "Accepted", null));
        stages.Add(new("hidden-boundary-vectors", "Accepted", null));
        stages.Add(new("seeded-vectors", "Accepted", null));
        stages.Add(new("oracle-compare", "Equivalent", null));
        return (true, null, execution.ReceiptDigest);
    }

    private static bool Matches(EvaluationResult actual, ReserveOutput? expected, string? error) => expected is null ? actual.Error?.Code == error : actual.Success && actual.Output == expected;

    private static AgentResponse ParseAgentResponse(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > LivePilotIdentity.OutputCapBytes) throw LivePilotJson.Failure("InvalidResponse");
        string json;
        try { json = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw LivePilotJson.Failure("InvalidResponse"); }
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 8 }); }
        catch (JsonException) { throw LivePilotJson.Failure("InvalidResponse"); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw LivePilotJson.Failure("InvalidResponse");
            string[] names = root.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToArray();
            if (!names.SequenceEqual(new[] { "candidateSource", "question", "status" })) throw LivePilotJson.Failure("InvalidResponse");
            if (root.GetProperty("status").ValueKind != JsonValueKind.String || root.GetProperty("candidateSource").ValueKind != JsonValueKind.String || root.GetProperty("question").ValueKind != JsonValueKind.String)
                throw LivePilotJson.Failure("InvalidResponse");
            var response = new AgentResponse(root.GetProperty("status").GetString()!, root.GetProperty("candidateSource").GetString()!, root.GetProperty("question").GetString()!);
            bool valid = response.Status switch
            {
                "Candidate" => response.CandidateSource.Length > 0 && response.Question.Length == 0,
                "NeedsHuman" => response.CandidateSource.Length == 0 && response.Question.Length > 0,
                "Refused" => response.CandidateSource.Length == 0 && response.Question.Length == 0,
                _ => false
            };
            if (!valid) throw LivePilotJson.Failure("InvalidResponse");
            return response;
        }
    }

    private static void ValidateReceipt(ProcessReceipt receipt, PilotManifest pilot, RunManifest run, string rawDirectory)
    {
        bool invocationMatches = CanonicalJson.Encode(receipt.InvocationIdentity).AsSpan().SequenceEqual(CanonicalJson.Encode(run.InvocationIdentity));
        if (receipt.Protocol != pilot.Protocol || receipt.PilotId != pilot.PilotId || receipt.RunId != run.RunId || receipt.ProcessAttemptId != run.ProcessAttemptId || !invocationMatches || receipt.RequestedRuntime != run.RequestedRuntime || receipt.PromptDigest != run.PromptDigest || receipt.ResponseSchemaDigest != run.ResponseSchemaDigest)
            throw LivePilotJson.Failure("RuntimeReceiptMismatch");
        if (receipt.StdoutDigest != FileDigest(rawDirectory, "stdout.jsonl") || receipt.StderrDigest != FileDigest(rawDirectory, "stderr.txt") || receipt.ResponseDigest != FileDigest(rawDirectory, "response.json") || receipt.PromptInputDigest != FileDigest(rawDirectory, "prompt-input.txt") || receipt.EnvironmentDigest != FileDigest(rawDirectory, "environment-receipt.json"))
            throw LivePilotJson.Failure("RawDigestMismatch");
        var acl = LivePilotJson.ReadCanonical<AclReceipt>(Path.Combine(rawDirectory, "acl-receipt.json"));
        var retention = LivePilotJson.ReadCanonical<RetentionReceipt>(Path.Combine(rawDirectory, "retention-receipt.json"));
        var environment = LivePilotJson.ReadCanonical<EnvironmentReceipt>(Path.Combine(rawDirectory, "environment-receipt.json"));
        if (acl.Protocol != pilot.Protocol || acl.PilotId != pilot.PilotId || acl.SystemSid != "S-1-5-18") throw LivePilotJson.Failure("AclReceiptInvalid");
        if (OperatingSystem.IsWindows())
        {
            string pilotRoot = Directory.GetParent(Directory.GetParent(rawDirectory)!.FullName)!.FullName;
            var currentAcl = LivePilotAcl.Inspect(pilotRoot, pilot);
            if (!acl.InheritanceDisabled || !acl.Verified || !CanonicalJson.Encode(acl).AsSpan().SequenceEqual(CanonicalJson.Encode(currentAcl)))
                throw LivePilotJson.Failure("AclReceiptInvalid");
        }
        if (retention.Protocol != pilot.Protocol || retention.PilotId != pilot.PilotId || retention.RetentionUntilUtc != pilot.RetentionUntilUtc || retention.CleanupMode != "ExplicitOnly") throw LivePilotJson.Failure("RetentionReceiptInvalid");
        string environmentDigest = CanonicalJson.RawDigest(CanonicalJson.Encode(environment with { Digest = string.Empty }));
        bool valuesValid = environment.Values.Select(x => x.Name).SequenceEqual(environment.Variables)
            && environment.Values.All(x => x.Present ? x.ValueDigest.Length == 64 && x.ValueDigest.All(char.IsAsciiHexDigit) : x.ValueDigest.Length == 0);
        if (environment.Protocol != pilot.Protocol || environment.PilotId != pilot.PilotId || environment.Digest != environmentDigest || !environment.Variables.SequenceEqual(run.InvocationIdentity.EnvironmentVariables) || !valuesValid)
            throw LivePilotJson.Failure("EnvironmentReceiptInvalid");
    }

    private static string FileDigest(string directory, string name) => CanonicalJson.RawDigest(File.ReadAllBytes(Path.Combine(directory, name)));
    private static RunMetrics Metrics(ProcessReceipt receipt, RunEventSummary events, long evaluationMilliseconds, bool needsHuman) =>
        new(receipt.WallMilliseconds, receipt.WallMilliseconds, evaluationMilliseconds, events.InputTokens, events.OutputTokens, events.CachedInputTokens,
            events.ReasoningTokens, events.ModelTurns, events.ToolCalls, events.CandidateWrites, 0, 1,
            needsHuman ? null : 0, needsHuman ? null : 0, null, null, "NotReportedBySubscriptionRuntime");

    private static RunMetrics EmptyMetrics(long evaluationMilliseconds) => new(0, 0, evaluationMilliseconds, null, null, null, null, 0, 0, 0, 0, 1, 0, 0, null, null, "NotReportedBySubscriptionRuntime");
}

public sealed record DockerExecution(bool Success, string? Code, ImmutableArray<ReserveOutput> Outputs, long Milliseconds, string StdoutDigest, string StderrDigest, string ReceiptDigest);

public static class DockerCandidateRunner
{
    public static DockerExecution Run(byte[] candidate, ImmutableArray<PilotVector> vectors, DockerIdentity identity, string rawDirectory, string fixtureDirectory, string pilotId = "standalone", string runId = "standalone", int timeoutSeconds = 120)
    {
        LivePilotPreparation.ValidateFrozenFixtures(Path.GetFullPath(fixtureDirectory));
        string input = Path.Combine(rawDirectory, "docker-input");
        if (Directory.Exists(input)) Directory.Delete(input, recursive: true);
        Directory.CreateDirectory(input);
        File.WriteAllBytes(Path.Combine(input, "Candidate.cs"), candidate);
        File.Copy(Path.Combine(fixtureDirectory, "csharp-runner", "Runner.csproj"), Path.Combine(input, "Runner.csproj"));
        File.Copy(Path.Combine(fixtureDirectory, "csharp-runner", "Program.cs"), Path.Combine(input, "Program.cs"));
        string vectorText = string.Join('\n', vectors.Select(x => $"{x.ResourceAvailable.ToString(CultureInfo.InvariantCulture)},{x.RequestedQuantity.ToString(CultureInfo.InvariantCulture)}")) + "\n";
        File.WriteAllText(Path.Combine(input, "vectors.csv"), vectorText, new UTF8Encoding(false));

        string containerName = $"strogo-e10-{Guid.NewGuid():N}";
        var arguments = new List<string>
        {
            "run", "--rm", "--name", containerName, "--platform", identity.Platform,
            "--network", "none", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
            "--user", "65532:65532", "--pids-limit", "64", "--memory", "512m", "--cpus", "1",
            "--tmpfs", "/tmp:rw,nosuid,nodev,mode=1777,size=384m",
            "--mount", $"type=bind,source={input},target=/input,readonly",
            "--env", "DOTNET_CLI_HOME=/tmp/dotnet", "--env", "NUGET_PACKAGES=/tmp/nuget", "--env", "DOTNET_NOLOGO=1", "--env", "DOTNET_CLI_TELEMETRY_OPTOUT=1",
            identity.ImageReference,
            "/bin/sh", "-c",
            "set -eu; mkdir -p /tmp/build; cp /input/Runner.csproj /input/Program.cs /input/Candidate.cs /tmp/build/; dotnet restore /tmp/build/Runner.csproj --ignore-failed-sources 1>&2; dotnet build /tmp/build/Runner.csproj -c Release --no-restore -p:UseAppHost=false 1>&2; while IFS=, read -r available quantity; do dotnet /tmp/build/bin/Release/net10.0/Runner.dll \"$available\" \"$quantity\"; done </input/vectors.csv"
        };
        string inputDigest = LivePilotJson.Digest(Directory.EnumerateFiles(input).Select(path => (Path.GetFileName(path), File.ReadAllBytes(path))).ToArray());
        var result = RunProcess("docker", arguments, timeoutSeconds, containerName);
        string stdoutPath = Path.Combine(rawDirectory, "docker-stdout.txt");
        string stderrPath = Path.Combine(rawDirectory, "docker-stderr.txt");
        File.WriteAllText(stdoutPath, result.Stdout, new UTF8Encoding(false));
        File.WriteAllText(stderrPath, result.Stderr, new UTF8Encoding(false));
        var outputs = ImmutableArray.CreateBuilder<ReserveOutput>();
        string? code = result.OutputLimitExceeded ? "OutputLimitExceeded" : result.TimedOut ? "CandidateTimeout" : result.ExitCode != 0 ? "CandidateBuildOrRuntimeFailure" : null;
        if (code is null) foreach (string line in result.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('|');
            if (fields.Length != 3 || !bool.TryParse(fields[0], out bool accepted) || !long.TryParse(fields[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long available) || !long.TryParse(fields[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long reserved))
            { code = "CandidateOutputInvalid"; outputs.Clear(); break; }
            outputs.Add(new(accepted, available, reserved));
        }
        string stdoutDigest = CanonicalJson.RawDigest(Encoding.UTF8.GetBytes(result.Stdout));
        string stderrDigest = CanonicalJson.RawDigest(Encoding.UTF8.GetBytes(result.Stderr));
        var outputArray = outputs.ToImmutable();
        var receiptSkeleton = new DockerReceipt(
            LivePilotIdentity.Protocol, pilotId, runId, identity, arguments.ToImmutableArray(), inputDigest,
            result.StartedAtUtc, result.EndedAtUtc, result.Milliseconds, result.ExitCode, result.TimedOut, result.OutputLimitExceeded,
            stdoutDigest, stderrDigest, CanonicalJson.RawDigest(CanonicalJson.Encode(outputArray)), string.Empty);
        var receipt = receiptSkeleton with { ReceiptDigest = CanonicalJson.RawDigest(CanonicalJson.Encode(receiptSkeleton)) };
        LivePilotJson.WriteCanonical(Path.Combine(rawDirectory, "docker-receipt.json"), receipt);
        return new(code is null, code, outputArray, result.Milliseconds, stdoutDigest, stderrDigest, receipt.ReceiptDigest);
    }

    public static string InspectImageId(string imageReference)
    {
        var result = RunProcess("docker", ["image", "inspect", "--format", "{{.Id}}", imageReference], 30, null);
        if (result.ExitCode != 0 || result.TimedOut || result.OutputLimitExceeded) throw LivePilotJson.Failure("DockerImageUnavailable");
        return result.Stdout.Trim();
    }

    private sealed record ProcessResult(int ExitCode, bool TimedOut, bool OutputLimitExceeded, string Stdout, string Stderr, long Milliseconds, string StartedAtUtc, string EndedAtUtc);

    private static ProcessResult RunProcess(string fileName, IEnumerable<string> arguments, int timeoutSeconds, string? containerName)
    {
        var info = new ProcessStartInfo(fileName) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        string startedAt = DateTimeOffset.UtcNow.ToString("O");
        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(info) ?? throw LivePilotJson.Failure("ProcessStartFailed");
        using var stdout = new LimitedMemoryStream(1024 * 1024);
        using var stderr = new LimitedMemoryStream(1024 * 1024);
        Task stdoutCopy = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        Task stderrCopy = process.StandardError.BaseStream.CopyToAsync(stderr);
        bool timedOut = false, outputLimitExceeded = false;
        while (!process.HasExited)
        {
            if (stdoutCopy.IsFaulted || stderrCopy.IsFaulted) { outputLimitExceeded = true; break; }
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(timeoutSeconds)) { timedOut = true; break; }
            Thread.Sleep(20);
        }
        if (timedOut || outputLimitExceeded)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            if (containerName is not null) try { _ = RunProcess("docker", ["rm", "-f", containerName], 30, null); } catch { }
        }
        process.WaitForExit();
        if (stdoutCopy.IsFaulted || stderrCopy.IsFaulted) outputLimitExceeded = true;
        try { Task.WaitAll(stdoutCopy, stderrCopy); } catch when (outputLimitExceeded) { }
        stopwatch.Stop();
        string outText = Encoding.UTF8.GetString(stdout.ToArray()), errText = Encoding.UTF8.GetString(stderr.ToArray());
        return new(timedOut || outputLimitExceeded ? -1 : process.ExitCode, timedOut, outputLimitExceeded, outText, errText, stopwatch.ElapsedMilliseconds, startedAt, DateTimeOffset.UtcNow.ToString("O"));
    }

    private sealed class LimitedMemoryStream(long limit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Length + count > limit) throw new IOException("OutputLimitExceeded");
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Length + buffer.Length > limit) throw new IOException("OutputLimitExceeded");
            base.Write(buffer);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Length + buffer.Length > limit) return ValueTask.FromException(new IOException("OutputLimitExceeded"));
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (Length + count > limit) return Task.FromException(new IOException("OutputLimitExceeded"));
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }
    }
}
