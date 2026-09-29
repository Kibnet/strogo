using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Core;
using Strogo.Experiments;

internal static class G03Cases
{
    public static void Run(List<string> failures)
    {
        void Check(bool condition, string name) { if (!condition) failures.Add($"G03 {name}"); }
        string root = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "fixtures", "g03-error-catalog", "v0.1"));
        string controls = Path.Combine(root, "controls");
        string manifest = Path.Combine(root, "source-row-manifest.json");
        string catalog = Path.Combine(root, "catalog.json");
        string registry = Path.Combine(controls, "registry.json");
        string plan = Path.Combine(controls, "plan.json");
        var input = G03Catalog.Validate(manifest, catalog, registry, plan);
        Check(input.SourceRowCount == "117" && input.FamilyCount == "12" && input.GlobalThreshold == "7", "frozen inventory and threshold");
        Check(input.OutputBytes.SequenceEqual(G03Catalog.Validate(manifest, catalog, registry, plan).OutputBytes), "deterministic validation bytes");
        string projection = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "docs", "g03-error-catalog-v0.1.md"));
        foreach (var family in input.Catalog.GetProperty("families").EnumerateArray())
        {
            Check(projection.Contains(family.GetProperty("id").GetString()!, StringComparison.Ordinal)
                && projection.Contains(family.GetProperty("name").GetString()!, StringComparison.Ordinal), "doc family projection");
            foreach (var shape in family.GetProperty("negativeShapes").EnumerateArray())
                Check(projection.Contains(shape.GetProperty("kind").GetString()!, StringComparison.Ordinal), "doc negative shape projection");
            foreach (var kind in family.GetProperty("positiveControlKinds").EnumerateArray())
                Check(projection.Contains(kind.GetProperty("kind").GetString()!, StringComparison.Ordinal), "doc positive kind projection");
        }
        foreach (string vocabulary in new[] { "negativeOutcomes", "positiveOutcomes", "generalizationStatuses",
                     "invalidEvidenceReasons", "terminalResults", "evidenceValidity", "applicabilityStatuses" })
            foreach (var code in input.Catalog.GetProperty(vocabulary).EnumerateArray())
                Check(projection.Contains($"`{code.GetString()}`", StringComparison.Ordinal), $"doc {vocabulary} projection");
        Check(projection.Contains("7 из 12", StringComparison.Ordinal), "doc threshold projection");

        foreach (var (name, terminal, covered, exit) in new[]
        {
            ("7of12", "PriorityCatalogueMajoritySupported", "7", 0),
            ("6of12", "PriorityCatalogueMajorityNotSupported", "6", 0),
            ("domain-floor", "PriorityCatalogueMajorityNotSupported", "7", 0),
            ("runtime-only", "PriorityCatalogueMajorityNotSupported", "6", 0),
            ("runtime-all", "PriorityCatalogueMajorityNotSupported", "0", 0),
            ("unsupported", "PriorityCatalogueMajorityNotSupported", "6", 0),
            ("missing-generalization", "PriorityCatalogueMajorityNotSupported", "6", 0),
            ("refuted-generalization", "PriorityCatalogueMajorityNotSupported", "6", 0),
            ("invalid-evidence", "EvaluationIncomplete", "6", 1),
            ("incomplete", "EvaluationIncomplete", "0", 1),
            ("valid-retry", "PriorityCatalogueMajoritySupported", "7", 0)
        })
        {
            string observation = Path.Combine(controls, $"observations-{name}.json");
            var caseInput = name == "domain-floor"
                ? G03Catalog.Validate(manifest, catalog, Path.Combine(controls, "registry-domain-floor.json"), Path.Combine(controls, "plan-domain-floor.json"))
                : input;
            var result = G03Catalog.Score(caseInput, observation, catalog);
            Check(result.TerminalResult == terminal && result.ExitCode == exit, $"{name} terminal/exit");
            using var report = JsonDocument.Parse(result.ReportBytes);
            Check(report.RootElement.GetProperty("globalCoveredCount").GetString() == covered, $"{name} covered count");
            Check(result.ReportBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(controls, $"report-{name}.json"))), $"{name} golden report");
            Check(result.ReportBytes.SequenceEqual(G03Catalog.Score(caseInput, observation, catalog).ReportBytes), $"{name} deterministic report");
            Check(report.RootElement.GetProperty("schemaDigests").EnumerateObject().Count() == 6,
                $"{name} schema provenance closure");
            Check(report.RootElement.GetProperty("evidenceScope").GetString() == "SyntheticControlOnly",
                $"{name} synthetic-only claim boundary");
            if (name == "domain-floor")
                Check(report.RootElement.GetProperty("domainResults").EnumerateArray().Any(d => !d.GetProperty("passed").GetBoolean()),
                    "global seven cannot hide failed domain majority");
        }
        using (var report = JsonDocument.Parse(G03Catalog.Score(input, Path.Combine(controls, "observations-runtime-only.json"), catalog).ReportBytes))
            Check(report.RootElement.GetProperty("secondaryMetrics").GetProperty("runtimeDetected").GetString() == "1", "runtime detection recorded but not counted");
        using (var report = JsonDocument.Parse(G03Catalog.Score(input, Path.Combine(controls, "observations-runtime-all.json"), catalog).ReportBytes))
            Check(report.RootElement.GetProperty("secondaryMetrics").GetProperty("runtimeDetected").GetString() == "72", "all runtime detection remains secondary");
        using (var report = JsonDocument.Parse(G03Catalog.Score(input, Path.Combine(controls, "observations-unsupported.json"), catalog).ReportBytes))
            Check(report.RootElement.GetProperty("secondaryMetrics").GetProperty("unsupportedUsefulTask").GetString() == "1", "unsupported task recorded but not counted");

        string temporary = Path.Combine(Path.GetTempPath(), $"strogo-g03-conformance-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(temporary);
            string copiedRoot = Path.Combine(temporary, "fixture");
            CopyTree(root, copiedRoot);
            string cm = Path.Combine(copiedRoot, "source-row-manifest.json");
            string cc = Path.Combine(copiedRoot, "catalog.json");
            string cr = Path.Combine(copiedRoot, "controls", "registry.json");
            string cp = Path.Combine(copiedRoot, "controls", "plan.json");
            string co = Path.Combine(copiedRoot, "controls", "observations-7of12.json");
            var copiedInput = G03Catalog.Validate(cm, cc, cr, cp);

            Mutate(cm, x => x["sources"]![0]!["rows"]!.AsArray().RemoveAt(0));
            Check(Refuses(() => G03Catalog.Validate(cm, cc, cr, cp), "G03SourceInventoryInvalid"), "source row omission refused");
            File.Copy(manifest, cm, true);

            Mutate(cm, x => x["sources"]![0]!["rows"]![0]!["id"] = "S01-99");
            Check(Refuses(() => G03Catalog.Validate(cm, cc, cr, cp), "G03SourceInventoryInvalid"), "unknown source row refused");
            File.Copy(manifest, cm, true);

            File.WriteAllBytes(cm, [0xff, 0xfe]);
            Check(Refuses(() => G03Catalog.Validate(cm, cc, cr, cp), "G03SchemaInvalid"), "invalid UTF-8 refused");
            File.Copy(manifest, cm, true);

            File.WriteAllText(cm, File.ReadAllText(cm) + " ");
            Check(Refuses(() => G03Catalog.Validate(cm, cc, cr, cp), "G03SchemaInvalid"), "noncanonical bytes refused");
            File.Copy(manifest, cm, true);

            Mutate(cc, x => x["classificationPrecedence"]![0] = "G03-F01");
            Check(Refuses(() => G03Catalog.Validate(cm, cc, cr, cp), "G03ClassificationInvalid"), "classification drift refused");
            File.Copy(catalog, cc, true);

            Mutate(cc, x => x["scoring"]!["globalThreshold"] = "6");
            Check(Refuses(() => G03Catalog.Validate(cm, cc, cr, cp), "G03CatalogInvalid"), "threshold six refused");
            File.Copy(catalog, cc, true);

            Mutate(cc, x => x["families"]!.AsArray().Add(x["families"]![0]!.DeepClone()));
            Check(Refuses(() => G03Catalog.Validate(cm, cc, cr, cp), "G03SchemaInvalid"), "duplicate family refused");
            File.Copy(catalog, cc, true);

            Mutate(cr, x => x["catalogDigest"] = new string('0', 64));
            Check(Refuses(() => G03Catalog.Validate(cm, cc, cr, cp), "G03EvidenceInvalid"), "registry identity refused");
            File.Copy(registry, cr, true);

            Mutate(cr, x => x["domains"]!.AsArray().RemoveAt(1));
            Check(Refuses(() => G03Catalog.Validate(cm, cc, cr, cp), "G03SchemaInvalid"), "one-domain registry refused");
            File.Copy(registry, cr, true);

            Mutate(cp, x => x["cells"]!.AsArray().RemoveAt(0));
            Check(Refuses(() => G03Catalog.Validate(cm, cc, cr, cp), "G03PlanInvalid"), "missing mandatory cell refused");
            File.Copy(plan, cp, true);

            Mutate(cp, x => x["cells"]![0]!["positives"]![0]!["contractDigest"] = new string('1', 64));
            Check(Refuses(() => G03Catalog.Validate(cm, cc, cr, cp), "G03PlanInvalid"), "mismatched positive refused");
            File.Copy(plan, cp, true);

            Mutate(cp, x => x["cells"]![0]!["positives"]![1]!["id"] = x["cells"]![0]!["positives"]![0]!["id"]!.GetValue<string>());
            Check(Refuses(() => G03Catalog.Validate(cm, cc, cr, cp), "G03PlanInvalid"), "duplicate positive ID refused canonically");
            File.Copy(plan, cp, true);

            Mutate(cp, x => x["cells"]![0]!["generalization"]!["coverageBasis"] = "SampledVectors");
            Check(Refuses(() => G03Catalog.Validate(cm, cc, cr, cp), "G03PlanInvalid"), "sampled generalization refused");
            File.Copy(plan, cp, true);

            Mutate(cp, x => x["planVersion"] = "unapproved-real-attempt");
            var structurallyValidNewPlan = G03Catalog.Validate(cm, cc, cr, cp);
            Check(Refuses(() => G03Catalog.Score(structurallyValidNewPlan, co, cc), "G03EvidenceInvalid"),
                "unapproved real plan cannot produce terminal report");
            File.Copy(plan, cp, true);

            Mutate(cp, x => { x["cells"]![0]!["generalization"]!["form"] = "FiniteDomainExhaustion";
                x["cells"]![0]!["generalization"]!["coverageBasis"] = "SampledVectors"; });
            Check(Refuses(() => G03Catalog.Validate(cm, cc, cr, cp), "G03PlanInvalid"), "sampled finite-domain exhaustion refused");
            File.Copy(plan, cp, true);

            Mutate(cr, x => { var extra = x["domains"]![0]!.DeepClone();
                extra["id"] = "synthetic-third"; extra["stateModel"] = "ThirdState";
                extra["inputEventShape"] = "ThirdInput"; extra["stateTransitionGraph"] = "ThirdTransition";
                x["domains"]!.AsArray().Add(extra); });
            string addedRegistryDigest = CanonicalJson.RawDigest(File.ReadAllBytes(cr));
            Mutate(cp, x => x["domainRegistryDigest"] = addedRegistryDigest);
            Check(Refuses(() => G03Catalog.Validate(cm, cc, cr, cp), "G03PlanInvalid"), "unplanned third domain refused");
            File.Copy(registry, cr, true);
            File.Copy(plan, cp, true);

            Mutate(co, x => x["planDigest"] = new string('0', 64));
            string expectedCrossDigest = CanonicalJson.RawDigest(CanonicalJson.Encode(new Dictionary<string, string>
            {
                ["SourceRowManifest"] = copiedInput.SourceRowManifestDigest,
                ["Catalog"] = copiedInput.CatalogDigest,
                ["DomainRegistry"] = copiedInput.DomainRegistryDigest,
                ["EvaluationPlan"] = copiedInput.PlanDigest,
                ["Observations"] = CanonicalJson.RawDigest(File.ReadAllBytes(co))
            }));
            Check(Refuses(() => G03Catalog.Score(copiedInput, co, cc), "G03EvidenceInvalid", expectedCrossDigest),
                "observation identity refusal includes all read digests");
            File.Copy(Path.Combine(controls, "observations-7of12.json"), co, true);

            Mutate(co, x => x["rows"]![0]!["outcome"] = "ExcludedByConstruction");
            var conflict = G03Catalog.Score(copiedInput, co, cc);
            Check(conflict.TerminalResult == "EvaluationIncomplete" && conflict.ExitCode == 1,
                "derived success conflicting with raw receipt cannot be counted");
            File.Copy(Path.Combine(controls, "observations-7of12.json"), co, true);

            Mutate(co, x => x["lineage"]![0]!["rawReceiptDigest"] = new string('0', 64));
            Check(Refuses(() => G03Catalog.Score(copiedInput, co, cc), "G03EvidenceInvalid"), "raw evidence digest refused");
            File.Copy(Path.Combine(controls, "observations-7of12.json"), co, true);

            Mutate(co, x => x["rows"]![0]!["rawEvidenceDigest"] = new string('0', 64));
            Check(Refuses(() => G03Catalog.Score(copiedInput, co, cc), "G03EvidenceInvalid"), "missing content-addressed evidence refused");
            File.Copy(Path.Combine(controls, "observations-7of12.json"), co, true);

            Mutate(co, x => x["lineage"]!.AsArray().Add(x["lineage"]![0]!.DeepClone()));
            Check(Refuses(() => G03Catalog.Score(copiedInput, co, cc), "G03EvidenceInvalid"), "post-exposure retry refused");
            File.Copy(Path.Combine(controls, "observations-7of12.json"), co, true);

            File.Copy(Path.Combine(controls, "observations-valid-retry.json"), co, true);
            Mutate(co, x => x["rows"]![0]!["attemptId"] = "attempt-1");
            Check(Refuses(() => G03Catalog.Score(copiedInput, co, cc), "G03EvidenceInvalid"), "row replacement across retry refused");
            File.Copy(Path.Combine(controls, "observations-7of12.json"), co, true);

            File.Copy(Path.Combine(controls, "observations-valid-retry.json"), co, true);
            Mutate(co, x => x["lineage"]![0]!["failureReason"] = "OracleAmbiguous");
            Check(Refuses(() => G03Catalog.Score(copiedInput, co, cc), "G03EvidenceInvalid"), "retry after non-infrastructure failure refused");
            File.Copy(Path.Combine(controls, "observations-valid-retry.json"), co, true);
            Mutate(co, x => x["lineage"]![1]!["armIdentitiesDigest"] = new string('0', 64));
            Check(Refuses(() => G03Catalog.Score(copiedInput, co, cc), "G03EvidenceInvalid"), "retry arm drift refused");
            File.Copy(Path.Combine(controls, "observations-7of12.json"), co, true);

            Mutate(co, x => x["rows"]![0]!["outcome"] = "UnknownOutcome");
            Check(Refuses(() => G03Catalog.Score(copiedInput, co, cc), "G03SchemaInvalid"), "unknown outcome refused");
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    private static bool Refuses(Action action, string code, string? digest = null)
    {
        try { action(); return false; }
        catch (G03RefusalException error) { return error.Code == code && (digest is null || error.ArtifactDigest == digest); }
    }

    private static void Mutate(string path, Action<JsonNode> change)
    {
        var node = JsonNode.Parse(File.ReadAllBytes(path))!;
        change(node);
        using var document = JsonDocument.Parse(node.ToJsonString());
        File.WriteAllBytes(path, CanonicalJson.Encode(document.RootElement));
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
