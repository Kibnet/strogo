using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kernel.Core;

namespace Strogo.Experiments;

public sealed class G03RefusalException(string code, string stage, string artifactDigest) : Exception(code)
{
    public string Code { get; } = code;
    public string Stage { get; } = stage;
    public string ArtifactDigest { get; } = artifactDigest;
    public byte[] OutputBytes => CanonicalJson.Encode(new { status = "Refused", code = Code, stage = Stage, artifactDigest = ArtifactDigest });
}

public sealed record G03ValidationResult(
    string SourceRowManifestDigest, string CatalogDigest, string DomainRegistryDigest, string PlanDigest,
    string SourceRowCount, string FamilyCount, string QualifyingDomainCount, string GlobalThreshold,
    JsonElement Manifest, JsonElement Catalog, JsonElement Registry, JsonElement Plan)
{
    public byte[] OutputBytes => CanonicalJson.Encode(new
    {
        status = "EvaluationInputsAccepted", catalogVersion = "0.1", SourceRowManifestDigest,
        CatalogDigest, DomainRegistryDigest, PlanDigest, SourceRowCount, FamilyCount,
        QualifyingDomainCount, GlobalThreshold
    });
}

public sealed record G03ScoreResult(string TerminalResult, string ReportDigest, byte[] ReportBytes)
{
    public byte[] OutputBytes => CanonicalJson.Encode(new { status = TerminalResult, ReportDigest });
    public int ExitCode => TerminalResult == "EvaluationIncomplete" ? 1 : 0;
}

public static class G03Catalog
{
    private const string ManifestDigest = "b809f086225737024e91b9527291bcd8ad30006b80b5f26fe9b43beeb3811a82";
    private const string CatalogDigest = "38364febcc77a98b19775ab5853c3d98ee8a240f8f9c1faf40d918604b4a5e12";
    private const string SyntheticRegistryDigest = "c4d8f9f689374fe42954dd6a1486172ba81db7f0b9c3e8762bf6277030177b40";
    private const string SyntheticPlanDigest = "62a6e799cccb8e91e3e71a247f1f149e5a4a7ac8497e59cb83319e3303a26e3d";
    private const string SyntheticFloorRegistryDigest = "e7d262a692c3cd5c3ea444e903ddaf4cc1fe9f3c91afbfc105b8880d0a876c80";
    private const string SyntheticFloorPlanDigest = "0b010fa6f32595ec9a11e5b7230ae5f3567b460519fb9c4af072bd2d7e4de706";
    private const int FamilyCount = 12;
    private static readonly string[] SourceIds = Enumerable.Range(1, 8).Select(i => $"S{i:00}").ToArray();
    private static readonly int[] SourceCounts = [8, 16, 15, 7, 25, 4, 30, 12];
    private static readonly string[] FamilyIds = Enumerable.Range(1, 12).Select(i => $"G03-F{i:00}").ToArray();
    private static readonly string[] Precedence = ["G03-F09", "G03-F11", "G03-F12", "G03-F08", "G03-F07", "G03-F10", "G03-F05", "G03-F06", "G03-F01", "G03-F03", "G03-F04", "G03-F02"];
    private static readonly string[] NegativeOutcomes = ["ExcludedByConstruction", "RejectedPreExecution", "RuntimeDetected", "AdmittedFault"];
    private static readonly string[] PositiveOutcomes = ["AcceptedEquivalent", "FalseRejection", "UnsupportedUsefulTask"];
    private static readonly string[] GeneralizationStatuses = ["VerifiedGeneralization", "MissingGeneralization", "RefutedGeneralization"];
    private static readonly string[] GeneralizationForms = ["GrammarOrTypeExclusion", "AdmissionInvariant", "UniversalContractObligation", "FiniteDomainExhaustion"];
    private static readonly string[] InvalidReasons = ["SchemaMismatch", "DigestMismatch", "ProvenanceMismatch", "InfrastructureFailure", "HarnessDefect", "OracleAmbiguous", "EquivalentSpecimen", "MultiFaultSpecimen", "AmbiguousPrimaryFamily", "DomainCollision", "IncompleteCohort"];
    private static readonly Dictionary<string, string> SchemaDigests = new(StringComparer.Ordinal)
    {
        ["source-row-manifest.schema.json"] = "d0ccda8d69b2ea4b21ce7eb508af8f5c70edd232bd1fba1650bffe0f9d3ca3b0",
        ["catalog.schema.json"] = "cf129a39f57e28816bcc9f71f59222dd115ea207690b689f3d4ec4fdb93802cd",
        ["domain-registry.schema.json"] = "d7940efaf75e4a39af10f57b2b4e30a9c1e79fe70f630990eab6658659c0ebeb",
        ["evaluation-plan.schema.json"] = "362becd2983aff36d39fe4a29db30d7187ef93a222357359d23c45c4d1babb36",
        ["observations.schema.json"] = "cc0c89c3c58d37f2e41239f1d1e5b190f990dbbc514767f2504a6e7020c3b115",
        ["report.schema.json"] = "4af5fa809640b00755570295b3193db881a94792844fd710c6d71f6d46bab68b"
    };

    public static G03ValidationResult Validate(string manifestPath, string catalogPath, string registryPath, string planPath)
    {
        string schemaDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(catalogPath))!, "schemas");
        var manifest = Read(manifestPath, "SourceRowManifest", "source-row-manifest.schema.json", schemaDirectory);
        ValidateManifest(manifest);
        var catalog = Read(catalogPath, "Catalog", "catalog.schema.json", schemaDirectory);
        ValidateCatalog(catalog, manifest);
        var registry = Read(registryPath, "DomainRegistry", "domain-registry.schema.json", schemaDirectory);
        ValidateRegistry(registry, manifest, catalog);
        var plan = Read(planPath, "EvaluationPlan", "evaluation-plan.schema.json", schemaDirectory);
        ValidatePlan(plan, manifest, catalog, registry);
        return new(manifest.Digest, catalog.Digest, registry.Digest, plan.Digest,
            "117", "12", GetArray(registry.Root, "domains").Length.ToString(), "7",
            manifest.Root.Clone(), catalog.Root.Clone(), registry.Root.Clone(), plan.Root.Clone());
    }

    public static G03ScoreResult Score(G03ValidationResult validated, string observationsPath, string catalogPath)
    {
        string schemaDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(catalogPath))!, "schemas");
        var observations = Read(observationsPath, "Observations", "observations.schema.json", schemaDirectory);
        bool syntheticOnly = S(validated.Registry, "approvalReceipt") == "SYNTHETIC-CONTROL-ONLY"
            && S(validated.Plan, "approvalReceipt") == "SYNTHETIC-CONTROL-ONLY"
            && ((validated.DomainRegistryDigest == SyntheticRegistryDigest && validated.PlanDigest == SyntheticPlanDigest)
                || (validated.DomainRegistryDigest == SyntheticFloorRegistryDigest && validated.PlanDigest == SyntheticFloorPlanDigest));
        if (!syntheticOnly) throw CrossEvidenceRefusal(validated, observations);
        var conflictingRows = ValidateObservations(observations, validated, observationsPath);
        return BuildReport(observations, validated, schemaDirectory, conflictingRows);
    }

    private sealed record Artifact(string Stage, string Digest, JsonElement Root, byte[] Bytes);

    private static Artifact Read(string path, string stage, string schemaName, string schemaDirectory)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new G03RefusalException("G03SchemaInvalid", stage, EmptyDigest());
        }
        string digest = CanonicalJson.RawDigest(bytes);
        JsonDocument document;
        try
        {
            string text = new UTF8Encoding(false, true).GetString(bytes);
            document = CanonicalJson.ParseStrict(text, new CoreLimits(MaxTransportBytes: 4_194_304));
            if (!CanonicalJson.Encode(document.RootElement).AsSpan().SequenceEqual(bytes))
                throw new InvalidOperationException("Noncanonical JSON");
        }
        catch (Exception e) when (e is DecoderFallbackException or JsonException or KernelException or InvalidOperationException)
        {
            throw new G03RefusalException("G03SchemaInvalid", stage, digest);
        }
        var root = document.RootElement.Clone();
        document.Dispose();
        ValidateSchema(root, stage, digest, schemaName, schemaDirectory);
        return new(stage, digest, root, bytes);
    }

    private static void ValidateSchema(JsonElement value, string stage, string digest, string name, string directory)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(Path.Combine(directory, name)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new G03RefusalException("G03SchemaInvalid", stage, digest);
        }
        if (CanonicalJson.RawDigest(bytes) != SchemaDigests[name])
            throw new G03RefusalException("G03SchemaInvalid", stage, digest);
        using var schema = JsonDocument.Parse(bytes);
        if (!MatchesSchema(value, schema.RootElement))
            throw new G03RefusalException("G03SchemaInvalid", stage, digest);
    }

    private static bool MatchesSchema(JsonElement value, JsonElement schema)
    {
        if (schema.TryGetProperty("anyOf", out var alternatives))
            return alternatives.EnumerateArray().Any(candidate => MatchesSchema(value, candidate));
        if (schema.TryGetProperty("type", out var type))
        {
            bool correct = type.GetString() switch
            {
                "object" => value.ValueKind == JsonValueKind.Object,
                "array" => value.ValueKind == JsonValueKind.Array,
                "string" => value.ValueKind == JsonValueKind.String,
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "null" => value.ValueKind == JsonValueKind.Null,
                _ => false
            };
            if (!correct) return false;
        }
        if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(choice => choice.GetRawText() == value.GetRawText())) return false;
        if (value.ValueKind == JsonValueKind.String)
        {
            string s = value.GetString()!;
            if (schema.TryGetProperty("minLength", out var minimum) && s.Length < minimum.GetInt32()) return false;
            if (schema.TryGetProperty("pattern", out var pattern) && !Regex.IsMatch(s, pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) return false;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            int size = value.GetArrayLength();
            if (schema.TryGetProperty("minItems", out var minimum) && size < minimum.GetInt32()) return false;
            if (schema.TryGetProperty("maxItems", out var maximum) && size > maximum.GetInt32()) return false;
            if (schema.TryGetProperty("items", out var items) && value.EnumerateArray().Any(item => !MatchesSchema(item, items))) return false;
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required) && required.EnumerateArray().Any(p => !value.TryGetProperty(p.GetString()!, out _))) return false;
            if (schema.TryGetProperty("properties", out var properties))
            {
                foreach (var property in value.EnumerateObject())
                {
                    if (!properties.TryGetProperty(property.Name, out var sub))
                    {
                        if (schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False) return false;
                    }
                    else if (!MatchesSchema(property.Value, sub)) return false;
                }
            }
        }
        return true;
    }

    private static string EmptyDigest() => CanonicalJson.RawDigest([]);
    private static string S(JsonElement root, string name) => root.GetProperty(name).GetString()!;
    private static JsonElement[] GetArray(JsonElement root, string name) => root.GetProperty(name).EnumerateArray().ToArray();
    private static bool Eq(JsonElement root, string name, string value) => S(root, name) == value;
    private static void Require(bool condition, string code, Artifact artifact)
    {
        if (!condition) throw new G03RefusalException(code, artifact.Stage, artifact.Digest);
    }
    private static void CrossRequire(bool condition, Artifact[] artifacts)
    {
        if (condition) return;
        var map = artifacts.OrderBy(a => a.Stage, StringComparer.Ordinal).ToDictionary(a => a.Stage, a => a.Digest, StringComparer.Ordinal);
        throw new G03RefusalException("G03EvidenceInvalid", "CrossArtifact", CanonicalJson.RawDigest(CanonicalJson.Encode(map)));
    }

    private static G03RefusalException CrossEvidenceRefusal(G03ValidationResult inputs, Artifact observations)
    {
        var readDigests = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SourceRowManifest"] = inputs.SourceRowManifestDigest, ["Catalog"] = inputs.CatalogDigest,
            ["DomainRegistry"] = inputs.DomainRegistryDigest, ["EvaluationPlan"] = inputs.PlanDigest,
            ["Observations"] = observations.Digest
        };
        return new("G03EvidenceInvalid", "CrossArtifact", CanonicalJson.RawDigest(CanonicalJson.Encode(readDigests)));
    }

    private static void ValidateManifest(Artifact manifest)
    {
        var root = manifest.Root;
        Require(manifest.Digest == ManifestDigest, "G03SourceInventoryInvalid", manifest);
        Require(GetArray(root, "sourceIds").Select(x => x.GetString()).SequenceEqual(SourceIds), "G03SourceInventoryInvalid", manifest);
        var sources = GetArray(root, "sources");
        Require(sources.Length == 8, "G03SourceInventoryInvalid", manifest);
        int count = 0;
        for (int i = 0; i < sources.Length; i++)
        {
            var source = sources[i];
            var rows = GetArray(source, "rows");
            Require(S(source, "sourceId") == SourceIds[i] && rows.Length == SourceCounts[i], "G03SourceInventoryInvalid", manifest);
            Require(S(source, "rowCount") == rows.Length.ToString(), "G03SourceInventoryInvalid", manifest);
            Require(rows.Select((row, index) => S(row, "id") == $"{SourceIds[i]}-{index + 1:00}").All(x => x), "G03SourceInventoryInvalid", manifest);
            var projection = new { sourceId = S(source, "sourceId"), revision = S(source, "revision"), snapshotUrl = S(source, "snapshotUrl"), rows = source.GetProperty("rows") };
            Require(S(source, "inventoryDigest") == CanonicalJson.RawDigest(CanonicalJson.Encode(projection)), "G03SourceInventoryInvalid", manifest);
            count += rows.Length;
        }
        Require(count == 117 && S(root, "totalRowCount") == "117", "G03SourceInventoryInvalid", manifest);
    }

    private static void ValidateCatalog(Artifact catalog, Artifact manifest)
    {
        var root = catalog.Root;
        CrossRequire(S(root, "sourceRowManifestDigest") == manifest.Digest, [manifest, catalog]);
        Require(GetArray(root, "sourceIds").Select(x => x.GetString()).SequenceEqual(SourceIds), "G03CatalogInvalid", catalog);
        var families = GetArray(root, "families");
        Require(families.Select(f => S(f, "id")).SequenceEqual(FamilyIds), "G03CatalogInvalid", catalog);
        foreach (var family in families)
        {
            string id = S(family, "id");
            var negatives = GetArray(family, "negativeShapes");
            var positives = GetArray(family, "positiveControlKinds");
            Require(negatives.Length >= 3 && negatives.Select(x => S(x, "id")).Distinct(StringComparer.Ordinal).Count() == negatives.Length,
                "G03CatalogInvalid", catalog);
            Require(positives.Length >= 2 && positives.Select(x => S(x, "id")).Distinct(StringComparer.Ordinal).Count() == positives.Length,
                "G03CatalogInvalid", catalog);
            Require(negatives.Select((x, i) => S(x, "id") == $"{id}-N{i + 1}").All(x => x), "G03CatalogInvalid", catalog);
            Require(positives.Select((x, i) => S(x, "id") == $"{id}-P{i + 1}").All(x => x), "G03CatalogInvalid", catalog);
            Require(GetArray(family, "evidenceIds").All(e => SourceIds.Contains(e.GetString(), StringComparer.Ordinal)), "G03CatalogInvalid", catalog);
        }
        var rawIds = GetArray(manifest.Root, "sources").SelectMany(s => GetArray(s, "rows").Select(r => S(r, "id"))).ToArray();
        var decisions = GetArray(root, "candidateDecisions");
        Require(decisions.Select(d => S(d, "sourceRowId")).SequenceEqual(rawIds), "G03SourceInventoryInvalid", catalog);
        foreach (var decision in decisions)
        {
            string code = S(decision, "decision");
            var family = decision.GetProperty("familyId");
            Require((code is "IncludedAsFamily" or "MergedIntoFamily") == (family.ValueKind == JsonValueKind.String), "G03CatalogInvalid", catalog);
            if (family.ValueKind == JsonValueKind.String) Require(FamilyIds.Contains(family.GetString(), StringComparer.Ordinal), "G03CatalogInvalid", catalog);
        }
        Require(decisions.Where(d => Eq(d, "decision", "IncludedAsFamily")).Select(d => S(d, "familyId")).Order(StringComparer.Ordinal).SequenceEqual(FamilyIds), "G03CatalogInvalid", catalog);
        Require(GetArray(root, "classificationPrecedence").Select(x => x.GetString()).SequenceEqual(Precedence), "G03ClassificationInvalid", catalog);
        Require(Sequence(root, "negativeOutcomes", NegativeOutcomes) && Sequence(root, "positiveOutcomes", PositiveOutcomes)
            && Sequence(root, "generalizationStatuses", GeneralizationStatuses) && Sequence(root, "generalizationForms", GeneralizationForms)
            && Sequence(root, "invalidEvidenceReasons", InvalidReasons)
            && Sequence(root, "evidenceValidity", ["Valid", "Invalid"])
            && Sequence(root, "applicabilityStatuses", ["Applicable", "NotApplicable"])
            && Sequence(root, "terminalResults", ["PriorityCatalogueMajoritySupported", "PriorityCatalogueMajorityNotSupported", "EvaluationIncomplete"]), "G03CatalogInvalid", catalog);
        var scoring = root.GetProperty("scoring");
        Require(S(scoring, "familyCount") == "12" && S(scoring, "globalThreshold") == "7"
            && S(scoring, "minimumQualifyingDomains") == "2" && S(scoring, "minimumApplicablePerDomain") == "8"
            && S(scoring, "minimumNegativeShapes") == "3" && S(scoring, "minimumPositiveKinds") == "2"
            && scoring.GetProperty("everyApplicableDomainRequired").GetBoolean()
            && scoring.GetProperty("everyMandatorySpecimenRequired").GetBoolean()
            && scoring.GetProperty("verifiedGeneralizationRequired").GetBoolean()
            && !scoring.GetProperty("runtimeDetectionCounts").GetBoolean(), "G03CatalogInvalid", catalog);
        Require(catalog.Digest == CatalogDigest, "G03CatalogInvalid", catalog);
    }

    private static bool Sequence(JsonElement root, string name, string[] expected) =>
        GetArray(root, name).Select(x => x.GetString()).SequenceEqual(expected);

    private static void ValidateRegistry(Artifact registry, Artifact manifest, Artifact catalog)
    {
        CrossRequire(S(registry.Root, "catalogDigest") == catalog.Digest, [manifest, catalog, registry]);
        var domains = GetArray(registry.Root, "domains");
        Require(domains.Length >= 2 && domains.Select(d => S(d, "id")).Distinct(StringComparer.Ordinal).Count() == domains.Length,
            "G03DomainRegistryInvalid", registry);
        foreach (var domain in domains)
        {
            var mappings = GetArray(domain, "applicability");
            Require(mappings.Select(m => S(m, "familyId")).SequenceEqual(FamilyIds), "G03DomainRegistryInvalid", registry);
            Require(mappings.Count(m => Eq(m, "status", "Applicable")) >= 8, "G03DomainRegistryInvalid", registry);
        }
        for (int i = 0; i < domains.Length; i++)
        for (int j = i + 1; j < domains.Length; j++)
        {
            Require(S(domains[i], "stateModel") != S(domains[j], "stateModel"), "G03DomainRegistryInvalid", registry);
            int distinctDimensions = new[] { "inputEventShape", "stateTransitionGraph", "collectionBehavior", "effectFailureSurface" }
                .Count(name => S(domains[i], name) != S(domains[j], name));
            Require(distinctDimensions >= 2, "G03DomainRegistryInvalid", registry);
        }
    }

    private static void ValidatePlan(Artifact plan, Artifact manifest, Artifact catalog, Artifact registry)
    {
        CrossRequire(S(plan.Root, "catalogDigest") == catalog.Digest && S(plan.Root, "domainRegistryDigest") == registry.Digest,
            [manifest, catalog, registry, plan]);
        var retry = plan.Root.GetProperty("retryPolicy");
        Require(Eq(retry, "policy", "PreExposureSingleWholeCohortRetry") && Eq(retry, "maxPreExposureRetries", "1")
            && Eq(retry, "postExposureRetries", "0") && !retry.GetProperty("allowRowReplacement").GetBoolean()
            && !retry.GetProperty("allowBestReportSelection").GetBoolean(), "G03PlanInvalid", plan);
        var expected = GetArray(registry.Root, "domains")
            .SelectMany(d => GetArray(d, "applicability").Where(a => Eq(a, "status", "Applicable"))
                .Select(a => (Domain: S(d, "id"), Family: S(a, "familyId"))))
            .ToHashSet();
        var cells = GetArray(plan.Root, "cells");
        Require(cells.Select(c => (Domain: S(c, "domainId"), Family: S(c, "familyId"))).ToHashSet().SetEquals(expected)
            && cells.Length == expected.Count, "G03PlanInvalid", plan);
        var families = GetArray(catalog.Root, "families").ToDictionary(f => S(f, "id"), f => f);
        var allSpecimenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cell in cells)
        {
            string familyId = S(cell, "familyId");
            var family = families[familyId];
            var negativeShapes = GetArray(family, "negativeShapes").Select(x => S(x, "id")).ToHashSet(StringComparer.Ordinal);
            var positiveKinds = GetArray(family, "positiveControlKinds").Select(x => S(x, "id")).ToHashSet(StringComparer.Ordinal);
            var negatives = GetArray(cell, "negatives");
            var positives = GetArray(cell, "positives");
            Require(negatives.Length >= 3 && positives.Length >= 3 && positives.Length == negatives.Length,
                "G03PlanInvalid", plan);
            Require(negatives.Select(n => S(n, "shapeId")).ToHashSet(StringComparer.Ordinal).SetEquals(negativeShapes), "G03PlanInvalid", plan);
            Require(positives.Select(p => S(p, "kindId")).Distinct(StringComparer.Ordinal).Count() >= 2
                && positives.All(p => positiveKinds.Contains(S(p, "kindId"))), "G03PlanInvalid", plan);
            Require(negatives.Any(n => S(n, "kind") is "Semantic" or "Replay"), "G03PlanInvalid", plan);
            Require(positives.Select(p => S(p, "id")).Distinct(StringComparer.Ordinal).Count() == positives.Length,
                "G03PlanInvalid", plan);
            var positiveMap = positives.ToDictionary(p => S(p, "id"), p => p, StringComparer.Ordinal);
            foreach (var negative in negatives)
            {
                Require(positiveMap.TryGetValue(S(negative, "matchedPositiveId"), out var positive), "G03PlanInvalid", plan);
                Require(S(positive, "matchedNegativeId") == S(negative, "id")
                    && S(positive, "contractDigest") == S(negative, "contractDigest")
                    && S(positive, "inputDigest") == S(negative, "inputDigest")
                    && S(positive, "oracleDigest") == S(negative, "oracleDigest")
                    && S(positive, "requiredOperation") == S(negative, "requiredOperation")
                    && S(positive, "candidateDigest") != S(negative, "candidateDigest"), "G03PlanInvalid", plan);
                Require(allSpecimenIds.Add(S(negative, "id")), "G03PlanInvalid", plan);
            }
            foreach (var positive in positives) Require(allSpecimenIds.Add(S(positive, "id")), "G03PlanInvalid", plan);
            var generalization = cell.GetProperty("generalization");
            Require(allSpecimenIds.Add(S(generalization, "id")), "G03PlanInvalid", plan);
            bool finite = Eq(generalization, "form", "FiniteDomainExhaustion");
            Require(finite ? Eq(generalization, "coverageBasis", "MachineCheckedFullDomain")
                             && generalization.GetProperty("finiteDomainCompletenessDigest").ValueKind == JsonValueKind.String
                           : Eq(generalization, "coverageBasis", "UniversalRule")
                             && generalization.GetProperty("finiteDomainCompletenessDigest").ValueKind == JsonValueKind.Null,
                "G03PlanInvalid", plan);
        }
    }

    private static HashSet<string> ValidateObservations(Artifact observations, G03ValidationResult inputs, string observationsPath)
    {
        var root = observations.Root;
        bool identities = S(root, "sourceRowManifestDigest") == inputs.SourceRowManifestDigest
            && S(root, "catalogDigest") == inputs.CatalogDigest
            && S(root, "domainRegistryDigest") == inputs.DomainRegistryDigest
            && S(root, "planDigest") == inputs.PlanDigest
            && S(root, "cohortId") == S(inputs.Plan, "cohortId");
        foreach (string name in new[] { "repositoryCommit", "sourceClosureDigest", "compilerIdentity", "verifierIdentity", "runtimeIdentity", "backendIdentity", "evaluatorIdentity" })
            identities &= S(root, name) == S(inputs.Plan, name);
        if (!identities) throw CrossEvidenceRefusal(inputs, observations);
        foreach (string name in new[] { "sourceClosureDigest", "generationInputsDigest", "armIdentitiesDigest", "oracleSetDigest", "candidateSetDigest" })
            RawEvidence(observationsPath, S(inputs.Plan, name), observations);
        foreach (var domain in GetArray(inputs.Registry, "domains"))
        {
            RawEvidence(observationsPath, S(domain, "distinctnessEvidenceDigest"), observations);
            foreach (var applicability in GetArray(domain, "applicability"))
                RawEvidence(observationsPath, S(applicability, "evidenceDigest"), observations);
        }
        foreach (var cell in GetArray(inputs.Plan, "cells"))
        foreach (var specimen in GetArray(cell, "negatives").Concat(GetArray(cell, "positives")))
        foreach (string name in new[] { "candidateDigest", "contractDigest", "inputDigest", "oracleDigest" })
            RawEvidence(observationsPath, S(specimen, name), observations);
        var lineage = GetArray(root, "lineage");
        Require(lineage.Length <= 2, "G03EvidenceInvalid", observations);
        Require(lineage.Select(x => S(x, "attemptId")).Distinct(StringComparer.Ordinal).Count() == lineage.Length, "G03EvidenceInvalid", observations);
        for (int i = 0; i < lineage.Length; i++)
        {
            var entry = lineage[i];
            Require(S(entry, "cohortId") == S(root, "cohortId"), "G03EvidenceInvalid", observations);
            Require(!Eq(entry, "status", "Completed") || Eq(entry, "exposureStatus", "Exposed"),
                "G03EvidenceInvalid", observations);
            Require(S(entry, "candidateDigest") == S(inputs.Plan, "candidateSetDigest")
                && S(entry, "generationInputsDigest") == S(inputs.Plan, "generationInputsDigest")
                && S(entry, "armIdentitiesDigest") == S(inputs.Plan, "armIdentitiesDigest")
                && S(entry, "oracleSetDigest") == S(inputs.Plan, "oracleSetDigest"),
                "G03EvidenceInvalid", observations);
            Require(Eq(entry, "status", "Completed")
                ? entry.GetProperty("failureReason").ValueKind == JsonValueKind.Null
                : entry.GetProperty("failureReason").ValueKind == JsonValueKind.String,
                "G03EvidenceInvalid", observations);
            Require(i == 0 ? entry.GetProperty("priorAttemptId").ValueKind == JsonValueKind.Null
                           : S(entry, "priorAttemptId") == S(lineage[i - 1], "attemptId")
                             && Eq(lineage[0], "exposureStatus", "PreExposure")
                             && Eq(lineage[0], "status", "Incomplete")
                              && Eq(lineage[0], "failureReason", "InfrastructureFailure")
                             && S(entry, "candidateDigest") == S(lineage[0], "candidateDigest"),
                "G03EvidenceInvalid", observations);
            byte[] attempt = RawEvidence(observationsPath, S(entry, "rawReceiptDigest"), observations);
            byte[] expectedAttempt = CanonicalJson.Encode(new
            {
                schemaVersion = "g03-attempt-receipt-v0.1", cohortId = S(entry, "cohortId"),
                attemptId = S(entry, "attemptId"), priorAttemptId = entry.GetProperty("priorAttemptId"),
                exposureStatus = S(entry, "exposureStatus"), status = S(entry, "status"),
                failureReason = entry.GetProperty("failureReason"), candidateDigest = S(entry, "candidateDigest"),
                generationInputsDigest = S(entry, "generationInputsDigest"),
                armIdentitiesDigest = S(entry, "armIdentitiesDigest"),
                oracleSetDigest = S(entry, "oracleSetDigest")
            });
            Require(attempt.AsSpan().SequenceEqual(expectedAttempt), "G03EvidenceInvalid", observations);
        }
        var planned = GetArray(inputs.Plan, "cells").SelectMany(c =>
            GetArray(c, "negatives").Select(n => (Id: S(n, "id"), Kind: "Negative", Specimen: n))
            .Concat(GetArray(c, "positives").Select(p => (Id: S(p, "id"), Kind: "Positive", Specimen: p)))
            .Append((Id: S(c.GetProperty("generalization"), "id"), Kind: "Generalization", Specimen: c.GetProperty("generalization"))))
            .ToDictionary(x => x.Id, x => (x.Kind, x.Specimen), StringComparer.Ordinal);
        var rows = GetArray(root, "rows");
        var conflicts = new HashSet<string>(StringComparer.Ordinal);
        Require(!Eq(lineage[^1], "exposureStatus", "PreExposure") || rows.Length == 0,
            "G03EvidenceInvalid", observations);
        Require(rows.Select(r => S(r, "id")).Distinct(StringComparer.Ordinal).Count() == rows.Length, "G03EvidenceInvalid", observations);
        foreach (var row in rows)
        {
            Require(S(row, "attemptId") == S(lineage[^1], "attemptId"), "G03EvidenceInvalid", observations);
            Require(planned.TryGetValue(S(row, "id"), out var required) && Eq(row, "rowKind", required.Kind), "G03EvidenceInvalid", observations);
            bool valid = Eq(row, "evidenceValidity", "Valid");
            var reason = row.GetProperty("invalidReason");
            var outcome = row.GetProperty("outcome");
            Require(valid ? reason.ValueKind == JsonValueKind.Null && outcome.ValueKind == JsonValueKind.String
                          : reason.ValueKind == JsonValueKind.String && outcome.ValueKind == JsonValueKind.Null,
                "G03SchemaInvalid", observations);
            if (valid)
            {
                string result = outcome.GetString()!;
                string[] allowed = required.Kind switch { "Negative" => NegativeOutcomes, "Positive" => PositiveOutcomes, _ => GeneralizationStatuses };
                Require(allowed.Contains(result, StringComparer.Ordinal), "G03SchemaInvalid", observations);
                if (required.Kind == "Generalization" && result == "VerifiedGeneralization")
                    Require(row.GetProperty("proofReceiptDigest").ValueKind == JsonValueKind.String
                        && S(row, "proofReceiptDigest") == S(required.Specimen, "proofDigest"), "G03EvidenceInvalid", observations);
            }
            byte[] raw = RawEvidence(observationsPath, S(row, "rawEvidenceDigest"), observations);
            byte[] expected = CanonicalJson.Encode(new
            {
                schemaVersion = "g03-specimen-receipt-v0.1", cohortId = S(root, "cohortId"),
                attemptId = S(row, "attemptId"), evaluatorIdentity = S(root, "evaluatorIdentity"),
                plannedSpecimen = required.Specimen,
                observation = new { id = S(row, "id"), rowKind = S(row, "rowKind"),
                    evidenceValidity = S(row, "evidenceValidity"), invalidReason = reason,
                    outcome, proofReceiptDigest = row.GetProperty("proofReceiptDigest") }
            });
            if (!raw.AsSpan().SequenceEqual(expected)) conflicts.Add(S(row, "id"));
            if (row.GetProperty("proofReceiptDigest").ValueKind == JsonValueKind.String)
            {
                byte[] proof = RawEvidence(observationsPath, S(row, "proofReceiptDigest"), observations);
                var g = required.Specimen;
                byte[] expectedProof = CanonicalJson.Encode(new
                {
                    schemaVersion = "g03-proof-receipt-v0.1", id = S(g, "id"), form = S(g, "form"),
                    statement = S(g, "statement"), scope = S(g, "scope"), assumptions = g.GetProperty("assumptions"),
                    checkerIdentity = S(g, "checkerIdentity"), trustedBase = g.GetProperty("trustedBase"),
                    coverageBasis = S(g, "coverageBasis"),
                    finiteDomainCompletenessDigest = g.GetProperty("finiteDomainCompletenessDigest")
                });
                if (!proof.AsSpan().SequenceEqual(expectedProof)) conflicts.Add(S(row, "id"));
            }
        }
        return conflicts;
    }

    private static byte[] RawEvidence(string observationsPath, string digest, Artifact observations)
    {
        string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(observationsPath))!, "raw", "sha256", digest);
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new G03RefusalException("G03EvidenceInvalid", "Observations", observations.Digest);
        }
        Require(CanonicalJson.RawDigest(bytes) == digest, "G03EvidenceInvalid", observations);
        return bytes;
    }

    private static G03ScoreResult BuildReport(Artifact observations, G03ValidationResult inputs, string schemaDirectory, HashSet<string> conflicts)
    {
        var planCells = GetArray(inputs.Plan, "cells");
        var rows = GetArray(observations.Root, "rows").ToDictionary(r => S(r, "id"), r => r, StringComparer.Ordinal);
        bool incomplete = conflicts.Count > 0 || planCells.Any(c => GetArray(c, "negatives").Any(n => !rows.ContainsKey(S(n, "id")))
            || GetArray(c, "positives").Any(p => !rows.ContainsKey(S(p, "id")))
            || !rows.ContainsKey(S(c.GetProperty("generalization"), "id"))
            || GetArray(c, "negatives").Any(n => rows.TryGetValue(S(n, "id"), out var row) && Eq(row, "evidenceValidity", "Invalid"))
            || GetArray(c, "positives").Any(p => rows.TryGetValue(S(p, "id"), out var row) && Eq(row, "evidenceValidity", "Invalid"))
            || (rows.TryGetValue(S(c.GetProperty("generalization"), "id"), out var generalization) && Eq(generalization, "evidenceValidity", "Invalid")));
        incomplete |= GetArray(observations.Root, "lineage").Any(e => Eq(e, "status", "Incomplete") && Eq(e, "exposureStatus", "Exposed"));
        incomplete |= Eq(GetArray(observations.Root, "lineage")[^1], "status", "Incomplete");
        var cells = planCells.Select(c =>
        {
            var neg = GetArray(c, "negatives").Select(n => Outcome(rows, S(n, "id"), conflicts)).ToArray();
            var pos = GetArray(c, "positives").Select(p => Outcome(rows, S(p, "id"), conflicts)).ToArray();
            string gen = Outcome(rows, S(c.GetProperty("generalization"), "id"), conflicts);
            bool covered = gen == "VerifiedGeneralization"
                && neg.All(x => x is "ExcludedByConstruction" or "RejectedPreExecution")
                && pos.All(x => x == "AcceptedEquivalent");
            return new { domainId = S(c, "domainId"), familyId = S(c, "familyId"),
                status = covered ? "Covered" : "NotCovered", negativeOutcomes = neg,
                positiveOutcomes = pos, generalizationStatus = gen };
        }).ToArray();
        var domains = GetArray(inputs.Registry, "domains").Select(d =>
        {
            string id = S(d, "id");
            int applicable = GetArray(d, "applicability").Count(a => Eq(a, "status", "Applicable"));
            int covered = cells.Count(c => c.domainId == id && c.status == "Covered");
            int threshold = applicable / 2 + 1;
            return new { domainId = id, applicableCount = applicable.ToString(), coveredCount = covered.ToString(),
                strictMajorityThreshold = threshold.ToString(), passed = covered >= threshold };
        }).ToArray();
        var families = FamilyIds.Select(id =>
        {
            var applicable = cells.Where(c => c.familyId == id).ToArray();
            return new { familyId = id, covered = applicable.Length > 0 && applicable.All(c => c.status == "Covered") };
        }).ToArray();
        int globalCovered = families.Count(f => f.covered);
        string terminal = incomplete ? "EvaluationIncomplete"
            : globalCovered >= 7 && domains.All(d => d.passed) ? "PriorityCatalogueMajoritySupported"
            : "PriorityCatalogueMajorityNotSupported";
        var outcomes = rows.Values.Where(r => Eq(r, "evidenceValidity", "Valid") && !conflicts.Contains(S(r, "id")))
            .Select(r => S(r, "outcome")).ToArray();
        string Count(string outcome) => outcomes.Count(x => x == outcome).ToString();
        var metrics = new { excludedByConstruction = Count("ExcludedByConstruction"),
            rejectedPreExecution = Count("RejectedPreExecution"), runtimeDetected = Count("RuntimeDetected"),
            admittedFault = Count("AdmittedFault"), falseRejection = Count("FalseRejection"),
            unsupportedUsefulTask = Count("UnsupportedUsefulTask"), acceptedEquivalent = Count("AcceptedEquivalent") };
        var skeleton = new { schemaVersion = "g03-report-v0.1", evidenceScope = "SyntheticControlOnly", terminalResult = terminal,
            sourceRowManifestDigest = inputs.SourceRowManifestDigest, catalogDigest = inputs.CatalogDigest,
            domainRegistryDigest = inputs.DomainRegistryDigest, planDigest = inputs.PlanDigest,
            observationsDigest = observations.Digest, cohortId = S(observations.Root, "cohortId"),
            globalCoveredCount = globalCovered.ToString(), globalThreshold = "7", cellResults = cells,
            domainResults = domains, familyResults = families, secondaryMetrics = metrics,
            schemaDigests = SchemaDigests,
            lineage = observations.Root.GetProperty("lineage") };
        string reportDigest = CanonicalJson.RawDigest(CanonicalJson.Encode(skeleton));
        byte[] report = CanonicalJson.Encode(new { skeleton.schemaVersion, skeleton.evidenceScope, skeleton.terminalResult,
            skeleton.sourceRowManifestDigest, skeleton.catalogDigest, skeleton.domainRegistryDigest,
            skeleton.planDigest, skeleton.observationsDigest, skeleton.cohortId, skeleton.globalCoveredCount,
            skeleton.globalThreshold, skeleton.cellResults, skeleton.domainResults, skeleton.familyResults,
            skeleton.secondaryMetrics, skeleton.schemaDigests, skeleton.lineage, reportDigest });
        using var reportDoc = JsonDocument.Parse(report);
        ValidateSchema(reportDoc.RootElement, "CrossArtifact", reportDigest, "report.schema.json", schemaDirectory);
        return new(terminal, reportDigest, report);
    }

    private static string Outcome(Dictionary<string, JsonElement> rows, string id, HashSet<string> conflicts) =>
        !conflicts.Contains(id) && rows.TryGetValue(id, out var row) && Eq(row, "evidenceValidity", "Valid")
            ? S(row, "outcome") : "NotObserved";
}
