using System.Collections.Immutable;
using System.Text;
using Kernel.Core;

namespace Strogo.Experiments;

public static class LivePilotReporting
{
    public static PilotReport Build(string directory)
    {
        string root = Path.GetFullPath(directory);
        var pilot = LivePilotPreparation.LoadAndVerify(root);
        var evaluations = ImmutableArray.CreateBuilder<CandidateEvaluation>();
        var effectiveModels = new List<string?>();
        var effectiveBackends = new List<string?>();
        bool evidenceQuarantined = false;
        foreach (string runId in pilot.RunOrder)
        {
            string runDirectory = Path.Combine(root, "runs", runId);
            var evaluation = LivePilotEvaluation.LoadAndVerify(Path.Combine(runDirectory, "evaluation.json"), pilot);
            if (evaluation.RawEvidenceDigest != LivePilotEvaluation.ComputeRawDigest(runDirectory, pilot.PilotId, runId)) throw LivePilotJson.Failure("RawEvidenceMutated");
            var run = pilot.Runs.Single(x => x.RunId == runId);
            if (evaluation.CandidateDigest.Length > 0)
            {
                string target = run.Arm == "strogo-notation" ? "candidate.strogo" : "Candidate.cs";
                string candidatePath = Path.Combine(runDirectory, target);
                if (!File.Exists(candidatePath) || CanonicalJson.RawDigest(File.ReadAllBytes(candidatePath)) != evaluation.CandidateDigest)
                    throw LivePilotJson.Failure("CandidateDigestMismatch");
            }
            if (run.Arm == "csharp-dotnet" && evaluation.AdapterEvidenceDigest.Length > 0)
            {
                var docker = LivePilotJson.ReadCanonical<DockerReceipt>(Path.Combine(runDirectory, "docker-receipt.json"));
                string dockerDigest = CanonicalJson.RawDigest(CanonicalJson.Encode(docker with { ReceiptDigest = string.Empty }));
                if (docker.ReceiptDigest != dockerDigest || docker.ReceiptDigest != evaluation.AdapterEvidenceDigest) throw LivePilotJson.Failure("DockerReceiptMismatch");
            }
            string eventsPath = Path.Combine(runDirectory, "stdout.jsonl");
            if (File.Exists(eventsPath))
            {
                var events = CodexEventParser.Parse(eventsPath);
                effectiveModels.Add(events.EffectiveModel);
                effectiveBackends.Add(events.EffectiveBackend);
            }
            else
            {
                effectiveModels.Add(null);
                effectiveBackends.Add(null);
            }
            string codexAgents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "agents");
            evidenceQuarantined |= LivePilotEvidence.ScanSensitive(runDirectory, root, codexAgents).Length > 0;
            evaluations.Add(evaluation);
        }

        string effectiveEvidence = EffectiveEvidence(effectiveModels, effectiveBackends, pilot.RequestedRuntime.Model, out bool runtimeInvalid);
        string promptInputSharedDigest;
        bool promptInputMismatch;
        try { promptInputSharedDigest = ValidatePromptInputs(root, pilot); promptInputMismatch = false; }
        catch (Exception) { promptInputSharedDigest = string.Empty; promptInputMismatch = true; }
        var runs = evaluations.ToImmutable();
        string status = evidenceQuarantined ? "EvidenceQuarantined"
            : runtimeInvalid || promptInputMismatch || runs.Any(x => x.Status == "Invalid") ? "Invalid"
            : runs.Any(x => x.Status == "InfrastructureFailure") ? "InfrastructureFailure"
            : "Completed";
        var arms = runs.GroupBy(x => x.Arm, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(group => new ArmSummary(
                group.Key,
                group.Count(),
                group.Count(x => x.Status == "BenchmarkAccepted"),
                Median(group.Select(x => x.Metrics.WallMilliseconds)),
                Median(group.Select(x => x.Metrics.EvaluationMilliseconds))))
            .ToImmutableArray();
        var pairs = ImmutableArray.Create(
            Pair("pair-1", "S1", "C1", runs),
            Pair("pair-2", "S2", "C2", runs));
        var limitations = ImmutableArray.CreateBuilder<string>();
        limitations.Add("PilotOnlyNoG05");
        limitations.Add("FiniteCorpusEvaluation");
        limitations.Add("FourRunsNoStatisticalInference");
        limitations.Add("OutputOnlyNoToolFeedback");
        limitations.Add("NoAutomaticRetry");
        limitations.Add("BillableCostNotReportedBySubscriptionRuntime");
        if (effectiveEvidence == "NotReportedByRuntime") limitations.Add("EffectiveBackendNotProven");
        if (promptInputMismatch) limitations.Add("PromptInputSharedContextMismatch");
        limitations.Add("DockerEngineAndPinnedImageTrustedBase");
        var skeleton = new PilotReport(
            LivePilotIdentity.ReportSchema,
            LivePilotIdentity.Protocol,
            pilot.PilotId,
            status,
            LivePilotIdentity.ClaimBoundary,
            effectiveEvidence,
            pilot.RepositoryCommit,
            pilot.ManifestDigest,
            pilot.RequestedRuntime,
            pilot.DockerIdentity,
            pilot.ImplementationIdentity,
            promptInputSharedDigest,
            pilot.RunOrder,
            runs,
            arms,
            pairs,
            limitations.ToImmutable(),
            string.Empty);
        return skeleton with { ReportDigest = CanonicalJson.RawDigest(CanonicalJson.Encode(skeleton)) };
    }

    public static void Write(string directory, string outputPath)
    {
        var report = Build(directory);
        byte[] bytes = CanonicalJson.Encode(report);
        string full = Path.GetFullPath(outputPath);
        if (File.Exists(full))
        {
            if (!File.ReadAllBytes(full).AsSpan().SequenceEqual(bytes)) throw LivePilotJson.Failure("ReportOverwriteMismatch");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    public static string Quarantine(string directory)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string destination = root + ".quarantine";
        if (Directory.Exists(destination) || File.Exists(destination)) throw LivePilotJson.Failure("QuarantineDestinationExists");
        Directory.Move(root, destination);
        return destination;
    }

    public static string ValidatePromptInputs(string directory)
    {
        string root = Path.GetFullPath(directory);
        return ValidatePromptInputs(root, LivePilotPreparation.LoadAndVerify(root));
    }

    private static string ValidatePromptInputs(string root, PilotManifest pilot)
    {
        var forbidden = LivePilotLeakage.LoadAndVerifyInventory(root, pilot);
        var digests = pilot.RunOrder.Select(runId =>
        {
            string promptInput = Path.Combine(root, "runs", runId, "prompt-input.txt");
            LivePilotLeakage.ValidatePromptInput(promptInput, forbidden);
            return PromptInputInspector.NormalizeDigest(
                promptInput,
                File.ReadAllBytes(Path.Combine(root, "packages", runId, "prompt.txt")),
                Path.Combine(root, "sessions", runId));
        }).ToArray();
        if (digests.Distinct(StringComparer.Ordinal).Count() != 1) throw LivePilotJson.Failure("PromptInputSharedContextMismatch");
        return digests[0];
    }

    private static PairDelta Pair(string id, string strogoId, string csharpId, ImmutableArray<CandidateEvaluation> runs)
    {
        var strogo = runs.Single(x => x.RunId == strogoId);
        var csharp = runs.Single(x => x.RunId == csharpId);
        bool comparable = strogo.Status is not ("Invalid" or "InfrastructureFailure") && csharp.Status is not ("Invalid" or "InfrastructureFailure");
        return new(id, strogoId, csharpId, comparable,
            comparable ? strogo.Metrics.WallMilliseconds - csharp.Metrics.WallMilliseconds : null,
            comparable ? strogo.Metrics.EvaluationMilliseconds - csharp.Metrics.EvaluationMilliseconds : null);
    }

    private static long Median(IEnumerable<long> values)
    {
        long[] ordered = values.Order().ToArray();
        if (ordered.Length == 0) return 0;
        return ordered.Length % 2 == 1 ? ordered[ordered.Length / 2] : checked((ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2);
    }

    private static string EffectiveEvidence(IReadOnlyList<string?> models, IReadOnlyList<string?> backends, string requestedModel, out bool invalid)
    {
        invalid = false;
        bool anyModel = models.Any(x => x is not null), allModel = models.All(x => x is not null);
        bool anyBackend = backends.Any(x => x is not null), allBackend = backends.All(x => x is not null);
        if (!anyModel && !anyBackend) return "NotReportedByRuntime";
        if (anyModel != allModel || anyBackend != allBackend) { invalid = true; return "PartiallyReported"; }
        if (allModel && models.Any(x => x != requestedModel) || allModel && models.Distinct(StringComparer.Ordinal).Count() != 1 || allBackend && backends.Distinct(StringComparer.Ordinal).Count() != 1)
        { invalid = true; return "Mismatch"; }
        return "Reported";
    }

}
