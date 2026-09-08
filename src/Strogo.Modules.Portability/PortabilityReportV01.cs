using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kernel.Core;

namespace Strogo.Modules.Portability;

/// <summary>
/// Closed, already validated evidence supplied to the report builder. The
/// constructors are internal on purpose: callers cannot self declare a
/// passing status or digest through the public report API.
/// </summary>
public sealed class PortabilityReportEvidenceSet
{
    internal PortabilityReportEvidenceSet(
        string expectedSourceRevision,
        string moduleDigest,
        string ownerBundleDigest,
        string proofDigest,
        string dafnySourceDigest,
        IEnumerable<PortabilityReportProfileEvidence> profiles)
    {
        ExpectedSourceRevision = expectedSourceRevision;
        ModuleDigest = moduleDigest;
        OwnerBundleDigest = ownerBundleDigest;
        ProofDigest = proofDigest;
        DafnySourceDigest = dafnySourceDigest;
        Profiles = profiles?.ToImmutableArray() ?? throw new ArgumentNullException(nameof(profiles));
    }

    public string ExpectedSourceRevision { get; }
    public string ModuleDigest { get; }
    public string OwnerBundleDigest { get; }
    public string ProofDigest { get; }
    public string DafnySourceDigest { get; }
    public ImmutableArray<PortabilityReportProfileEvidence> Profiles { get; }
}

public sealed class PortabilityReportProfileEvidence
{
    internal PortabilityReportProfileEvidence(
        string profileId,
        string translatorDigest,
        string buildToolchainDigest,
        string runtimeRequirementDigest,
        string? upstreamWarningBaselineDigest,
        string? upstreamWarningApprovalDigest,
        PortabilityReportBuildEvidence build,
        IEnumerable<PortabilityReportPlatformEvidence> platforms)
    {
        ProfileId = profileId;
        TranslatorDigest = translatorDigest;
        BuildToolchainDigest = buildToolchainDigest;
        RuntimeRequirementDigest = runtimeRequirementDigest;
        UpstreamWarningBaselineDigest = upstreamWarningBaselineDigest;
        UpstreamWarningApprovalDigest = upstreamWarningApprovalDigest;
        Build = build ?? throw new ArgumentNullException(nameof(build));
        Platforms = platforms?.ToImmutableArray() ?? throw new ArgumentNullException(nameof(platforms));
    }

    public string ProfileId { get; }
    public string TranslatorDigest { get; }
    public string BuildToolchainDigest { get; }
    public string RuntimeRequirementDigest { get; }
    public string? UpstreamWarningBaselineDigest { get; }
    public string? UpstreamWarningApprovalDigest { get; }
    public PortabilityReportBuildEvidence Build { get; }
    public ImmutableArray<PortabilityReportPlatformEvidence> Platforms { get; }
}

public sealed class PortabilityReportBuildEvidence
{
    private readonly byte[] rawReceiptBytes;
    internal PortabilityReportBuildEvidence(
        string status,
        IEnumerable<string> directReasons,
        byte[] rawReceiptBytes,
        string? portabilityManifestDigest,
        string? packageDigest,
        string? artifactDigest,
        string? runtimeClosureDigest,
        string? twoRootInventoryDigest,
        string? harnessDigest = null)
    {
        Status = status;
        DirectReasons = directReasons?.ToImmutableArray() ?? throw new ArgumentNullException(nameof(directReasons));
        this.rawReceiptBytes = rawReceiptBytes?.ToArray() ?? throw new ArgumentNullException(nameof(rawReceiptBytes));
        PortabilityManifestDigest = portabilityManifestDigest;
        PackageDigest = packageDigest;
        ArtifactDigest = artifactDigest;
        RuntimeClosureDigest = runtimeClosureDigest;
        TwoRootInventoryDigest = twoRootInventoryDigest;
        HarnessDigest = harnessDigest;
    }

    public string Status { get; }
    public ImmutableArray<string> DirectReasons { get; }
    public byte[] RawReceiptBytes => rawReceiptBytes.ToArray();
    public string? PortabilityManifestDigest { get; }
    public string? PackageDigest { get; }
    public string? ArtifactDigest { get; }
    public string? RuntimeClosureDigest { get; }
    public string? TwoRootInventoryDigest { get; }
    public string? HarnessDigest { get; }
}

public sealed class PortabilityReportPlatformEvidence
{
    internal PortabilityReportPlatformEvidence(
        string os,
        string arch,
        string? osIdentity,
        string? kernelIdentity,
        string? runtimeVendor,
        string? runtimeVersion,
        string? runtimeClosureDigest,
        string? launcherDigest,
        string? harnessDigest,
        string? portabilityManifestDigest,
        string? packageDigest,
        string? artifactDigest,
        PortabilityReportGateEvidence consumer,
        PortabilityReportGateEvidence jit,
        PortabilityReportGateEvidence oracle,
        string? performanceReceiptDigest,
        IEnumerable<PortabilityReportVector> vectors,
        IEnumerable<PortabilityReportOutcomeRow> outcomes)
    {
        Os = os;
        Arch = arch;
        OsIdentity = osIdentity;
        KernelIdentity = kernelIdentity;
        RuntimeVendor = runtimeVendor;
        RuntimeVersion = runtimeVersion;
        RuntimeClosureDigest = runtimeClosureDigest;
        LauncherDigest = launcherDigest;
        HarnessDigest = harnessDigest;
        PortabilityManifestDigest = portabilityManifestDigest;
        PackageDigest = packageDigest;
        ArtifactDigest = artifactDigest;
        Consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        Jit = jit ?? throw new ArgumentNullException(nameof(jit));
        Oracle = oracle ?? throw new ArgumentNullException(nameof(oracle));
        PerformanceReceiptDigest = performanceReceiptDigest;
        Vectors = vectors?.ToImmutableArray() ?? throw new ArgumentNullException(nameof(vectors));
        Outcomes = outcomes?.ToImmutableArray() ?? throw new ArgumentNullException(nameof(outcomes));
    }

    public string Os { get; }
    public string Arch { get; }
    public string? OsIdentity { get; }
    public string? KernelIdentity { get; }
    public string? RuntimeVendor { get; }
    public string? RuntimeVersion { get; }
    public string? RuntimeClosureDigest { get; }
    public string? LauncherDigest { get; }
    public string? HarnessDigest { get; }
    public string? PortabilityManifestDigest { get; }
    public string? PackageDigest { get; }
    public string? ArtifactDigest { get; }
    public PortabilityReportGateEvidence Consumer { get; }
    public PortabilityReportGateEvidence Jit { get; }
    public PortabilityReportGateEvidence Oracle { get; }
    public string? PerformanceReceiptDigest { get; }
    public ImmutableArray<PortabilityReportVector> Vectors { get; }
    public ImmutableArray<PortabilityReportOutcomeRow> Outcomes { get; }
}

public sealed record PortabilityReportGateEvidence
{
    private readonly byte[] rawReceiptBytes;
    internal PortabilityReportGateEvidence(
        string kind,
        string status,
        IEnumerable<string> directReasons,
        byte[] rawReceiptBytes,
        string? publicApiDigest,
        string? entrySymbol,
        string? candidateSymbol,
        string? calls,
        IEnumerable<string>? eventKinds,
        string? ownerDomainRuleDigest)
    {
        Kind = kind;
        Status = status;
        DirectReasons = directReasons?.ToImmutableArray() ?? throw new ArgumentNullException(nameof(directReasons));
        this.rawReceiptBytes = rawReceiptBytes?.ToArray() ?? throw new ArgumentNullException(nameof(rawReceiptBytes));
        PublicApiDigest = publicApiDigest;
        EntrySymbol = entrySymbol;
        CandidateSymbol = candidateSymbol;
        Calls = calls;
        EventKinds = (eventKinds ?? []).ToImmutableArray();
        OwnerDomainRuleDigest = ownerDomainRuleDigest;
    }

    public string Kind { get; }
    public string Status { get; }
    public ImmutableArray<string> DirectReasons { get; }
    public byte[] RawReceiptBytes => rawReceiptBytes.ToArray();
    public string? PublicApiDigest { get; }
    public string? EntrySymbol { get; }
    public string? CandidateSymbol { get; }
    public string? Calls { get; }
    public ImmutableArray<string> EventKinds { get; }
    public string? OwnerDomainRuleDigest { get; }
}

public sealed record PortabilityReportVector(string VectorId, string InputDigest);

public sealed record PortabilityReportOutcomeRow(
    string VectorId,
    string InputDigest,
    string Classification,
    string? OwnerClauseDigest,
    string Termination,
    PortabilityReportOutcome Outcome);

public sealed record PortabilityReportOutcome
{
    private PortabilityReportOutcome(string kind, JsonElement? value, string? code, string? locus, JsonElement? details)
    {
        Kind = kind;
        Value = value;
        Code = code;
        Locus = locus;
        Details = details;
    }

    public string Kind { get; }
    public JsonElement? Value { get; }
    public string? Code { get; }
    public string? Locus { get; }
    public JsonElement? Details { get; }

    internal static PortabilityReportOutcome FromJson(string kind, object? value, string? code, string? locus, object? details)
    {
        JsonElement? Encode(object? item)
        {
            if (item is null) return null;
            using var document = JsonDocument.Parse(CanonicalJson.Encode(item));
            return document.RootElement.Clone();
        }
        return new(kind, Encode(value), code, locus, Encode(details));
    }
}

public sealed class PortabilityReportV01
{
    public const string SchemaVersion = "strogo.portability-report.v0.1";
    public const string Purpose = "validation-only";
    public const string ContractStatus = "validation-fixture";
    public const string AdmissionStatus = "NotAdmittable";

    private static readonly string[] ProfileOrder = [PortabilityVersions.DotNetProfile, PortabilityVersions.JvmProfile];
    private static readonly (string Os, string Arch)[] RequiredPlatforms = [("linux", "x64"), ("windows", "x64")];
    private static readonly ImmutableHashSet<string> AllowedReasons = ImmutableHashSet.Create(StringComparer.Ordinal,
        "ArtifactIdentityMismatch", "BackendSemanticMismatch", "BuildNotReproducible", "ConsumerFailed",
        "EnvironmentUnavailable", "JitEvidenceMissing", "NonReproducibleBuild", "OracleMismatch",
        "RowFailed", "RowUnavailable", "TargetBuildRejected", "TargetExecutionFailed", "UnsupportedPortableAbi");
    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex RevisionPattern = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly byte[] canonicalBytes;
    private readonly byte[] semanticBytes;

    private PortabilityReportV01(
        string sourceRevision,
        string moduleDigest,
        string ownerBundleDigest,
        string proofDigest,
        string dafnySourceDigest,
        ImmutableArray<PortabilityReportProfile> profiles,
        string comparisonStatus,
        ImmutableArray<string> reasons,
        string semanticDigest,
        string createdAtUtc,
        byte[] canonicalBytes,
        byte[] semanticBytes)
    {
        SourceRevision = sourceRevision;
        ModuleDigest = moduleDigest;
        OwnerBundleDigest = ownerBundleDigest;
        ProofDigest = proofDigest;
        DafnySourceDigest = dafnySourceDigest;
        Profiles = profiles;
        ComparisonStatus = comparisonStatus;
        Reasons = reasons;
        SemanticDigest = semanticDigest;
        CreatedAtUtc = createdAtUtc;
        this.canonicalBytes = canonicalBytes.ToArray();
        this.semanticBytes = semanticBytes.ToArray();
    }

    public string SourceRevision { get; }
    public string ModuleDigest { get; }
    public string OwnerBundleDigest { get; }
    public string ProofDigest { get; }
    public string DafnySourceDigest { get; }
    public ImmutableArray<PortabilityReportProfile> Profiles { get; }
    public string ComparisonStatus { get; }
    public ImmutableArray<string> Reasons { get; }
    public string SemanticDigest { get; }
    public string CreatedAtUtc { get; }

    public static PortabilityReportV01 Build(PortabilityReportEvidenceSet evidence, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var state = CreateState(evidence, createdAtUtc);
        return state.Report;
    }

    public static PortabilityReportV01 Validate(ReadOnlySpan<byte> reportBytes, PortabilityReportEvidenceSet evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var expected = Build(evidence, ParseTimestamp(reportBytes));
        if (!expected.canonicalBytes.AsSpan().SequenceEqual(reportBytes)) Reject("CanonicalReportMismatch", "$/report");
        return expected;
    }

    public byte[] CanonicalJsonBytes() => canonicalBytes.ToArray();
    public byte[] SemanticProjectionBytes() => semanticBytes.ToArray();

    public byte[] MarkdownBytes(string canonicalJsonRelativePath)
    {
        ValidateRelativePath(canonicalJsonRelativePath);
        return BuildMarkdown(canonicalJsonRelativePath);
    }

    private byte[] BuildMarkdown(string canonicalJsonRelativePath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Strogo portability report v0.1");
        builder.AppendLine();
        builder.AppendLine($"- Schema: `{SchemaVersion}`");
        builder.AppendLine($"- Purpose: `{Purpose}`");
        builder.AppendLine($"- Contract status: `{ContractStatus}`");
        builder.AppendLine($"- Admission status: `{AdmissionStatus}`");
        builder.AppendLine($"- Source revision: `{SourceRevision}`");
        builder.AppendLine($"- Comparison: `{ComparisonStatus}`");
        builder.AppendLine($"- Semantic digest: `{SemanticDigest}`");
        builder.AppendLine($"- Canonical JSON: [{canonicalJsonRelativePath}]({canonicalJsonRelativePath})");
        builder.AppendLine();
        builder.AppendLine("This is a validation-only fixed-workload fixture. It is not production admission and does not establish G05 or G06.");
        builder.AppendLine();
        builder.AppendLine($"Reasons: {(Reasons.Length == 0 ? "none" : string.Join(", ", Reasons))}");
        foreach (var profile in Profiles)
        {
            builder.AppendLine();
            builder.AppendLine($"## {profile.ProfileId}");
            builder.AppendLine($"Status: `{profile.Status}`; reasons: {(profile.ReasonCodes.Length == 0 ? "none" : string.Join(", ", profile.ReasonCodes))}");
            builder.AppendLine();
            builder.AppendLine("| OS | Arch | Status | Reasons | Vector set | Outcomes |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var row in profile.Platforms)
                builder.AppendLine($"| {row.Os} | {row.Arch} | {row.Status} | {(row.ReasonCodes.Length == 0 ? "none" : string.Join(", ", row.ReasonCodes))} | {row.VectorSetDigest ?? "null"} | {row.OutcomeDigest ?? "null"} |");
        }
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static BuildState CreateState(PortabilityReportEvidenceSet evidence, DateTimeOffset createdAtUtc)
    {
        ValidateEvidenceAnchors(evidence);
        var timestamp = FormatTimestamp(createdAtUtc);
        var profiles = evidence.Profiles.OrderBy(profile => profile.ProfileId, StringComparer.Ordinal).Select(profile => BuildProfile(evidence, profile)).ToImmutableArray();
        if (!profiles.Select(profile => profile.ProfileId).SequenceEqual(ProfileOrder, StringComparer.Ordinal)) Reject("MissingProfile", "$/profiles");

        var crossMismatch = profiles.SelectMany(profile => profile.Platforms.Where(row => row.Status == "Passed").Select(row => (profile.ProfileId, row.VectorSetDigest, row.OutcomeDigest)))
            .GroupBy(item => item.ProfileId, StringComparer.Ordinal).Select(group => group.Select(item => (item.VectorSetDigest, item.OutcomeDigest)).Distinct().Count() > 1).Any(value => value);
        var dotnet = profiles.Single(profile => profile.ProfileId == PortabilityVersions.DotNetProfile);
        var jvm = profiles.Single(profile => profile.ProfileId == PortabilityVersions.JvmProfile);
        if (dotnet.Portable && jvm.Portable &&
            (dotnet.CommonVectorSetDigest != jvm.CommonVectorSetDigest || dotnet.CommonOutcomeDigest != jvm.CommonOutcomeDigest))
        {
            crossMismatch = true;
            profiles = profiles.Select(profile => profile with
            {
                Status = "NotPortable",
                Portable = false,
                ReasonCodes = profile.ReasonCodes.Append("OracleMismatch").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray()
            }).ToImmutableArray();
        }
        var comparisonStatus = profiles.All(profile => profile.Portable) ? "Portable" : "NotPortable";
        var reportReasonsBuilder = profiles.SelectMany(profile => profile.ReasonCodes).ToHashSet(StringComparer.Ordinal);
        if (crossMismatch) reportReasonsBuilder.Add("OracleMismatch");
        var reportReasons = reportReasonsBuilder.Order(StringComparer.Ordinal).ToImmutableArray();
        if (comparisonStatus == "NotPortable" && reportReasons.Length == 0) Reject("NotPortableWithoutReason", "$/reasons");
        var semanticPayload = SemanticPayload(evidence, profiles, comparisonStatus, reportReasons);
        var semanticBytes = CanonicalJson.Encode(semanticPayload);
        var semanticDigest = PortabilityContract.DomainHash("strogo.portability-report.v0.1/semantic", semanticBytes);
        var canonicalPayload = CanonicalPayload(evidence, profiles, comparisonStatus, reportReasons, semanticDigest, timestamp);
        var canonicalBytes = CanonicalJson.Encode(canonicalPayload);
        var report = new PortabilityReportV01(evidence.ExpectedSourceRevision, evidence.ModuleDigest, evidence.OwnerBundleDigest, evidence.ProofDigest, evidence.DafnySourceDigest,
            profiles, comparisonStatus, reportReasons, semanticDigest, timestamp, canonicalBytes, semanticBytes);
        return new(report);
    }

    private static PortabilityReportProfile BuildProfile(PortabilityReportEvidenceSet set, PortabilityReportProfileEvidence evidence)
    {
        RequireProfile(evidence.ProfileId);
        ValidateReceiptRevision(evidence.Build.RawReceiptBytes, set.ExpectedSourceRevision, $"$/profiles/{evidence.ProfileId}/build");
        RequireDigest(evidence.TranslatorDigest, $"$/profiles/{evidence.ProfileId}/translatorDigest");
        RequireDigest(evidence.BuildToolchainDigest, $"$/profiles/{evidence.ProfileId}/buildToolchainDigest");
        RequireDigest(evidence.RuntimeRequirementDigest, $"$/profiles/{evidence.ProfileId}/runtimeRequirementDigest");
        if (evidence.ProfileId == PortabilityVersions.DotNetProfile)
        {
            if (evidence.UpstreamWarningBaselineDigest is not null || evidence.UpstreamWarningApprovalDigest is not null) Reject("BaselineNotApplicableExpected", $"$/profiles/{evidence.ProfileId}");
        }
        else
        {
            if (evidence.Build.Status == "Passed")
            {
                RequireDigest(evidence.UpstreamWarningBaselineDigest, $"$/profiles/{evidence.ProfileId}/upstreamWarningBaselineDigest");
                RequireDigest(evidence.UpstreamWarningApprovalDigest, $"$/profiles/{evidence.ProfileId}/upstreamWarningApprovalDigest");
            }
            else if (evidence.UpstreamWarningBaselineDigest is not null || evidence.UpstreamWarningApprovalDigest is not null)
                Reject("UpstreamWarningBaselineMismatch", $"$/profiles/{evidence.ProfileId}");
        }

        var build = evidence.Build;
        ValidateBuild(build, evidence.ProfileId);
        var rows = BuildPlatforms(set, evidence);
        var profileReasons = build.DirectReasons.ToHashSet(StringComparer.Ordinal);
        var hasPassedRows = rows.Any(row => row.Status == "Passed");
        if (build.Status == "Passed" && rows.Any(row => row.Status != "Passed")) profileReasons.Add("RowFailed");
        if (build.Status == "TargetBuildRejected" && profileReasons.Count == 0) profileReasons.Add("TargetBuildRejected");
        if (build.Status == "NonReproducible") profileReasons.Add("BuildNotReproducible");
        foreach (var row in rows) profileReasons.UnionWith(row.ReasonCodes.Where(reason => reason is "OracleMismatch"));
        var portable = build.Status == "Passed" && hasPassedRows && rows.All(row => row.Status == "Passed") &&
            rows.Select(row => row.VectorSetDigest).Distinct(StringComparer.Ordinal).Count() == 1 &&
            rows.Select(row => row.OutcomeDigest).Distinct(StringComparer.Ordinal).Count() == 1;
        if (build.Status == "Passed" && rows.All(row => row.Status == "Passed") &&
            (rows.Select(row => row.VectorSetDigest).Distinct(StringComparer.Ordinal).Count() > 1 ||
             rows.Select(row => row.OutcomeDigest).Distinct(StringComparer.Ordinal).Count() > 1))
            profileReasons.Add("OracleMismatch");
        var status = portable ? "Portable" : "NotPortable";
        if (!portable && profileReasons.Count == 0) profileReasons.Add("RowFailed");
        return new PortabilityReportProfile(evidence.ProfileId, evidence.TranslatorDigest, evidence.BuildToolchainDigest, evidence.RuntimeRequirementDigest,
            evidence.UpstreamWarningBaselineDigest, evidence.UpstreamWarningApprovalDigest, BuildQuartet(build), build.Status == "Passed" ? true : build.Status == "NonReproducible" ? false : null,
            BuildGateDigest(set, evidence), status,
            profileReasons.Order(StringComparer.Ordinal).ToImmutableArray(), rows, portable,
            rows.Select(row => row.VectorSetDigest).FirstOrDefault(digest => digest is not null), rows.Select(row => row.OutcomeDigest).FirstOrDefault(digest => digest is not null));
    }

    private static ImmutableArray<PortabilityReportPlatform> BuildPlatforms(PortabilityReportEvidenceSet set, PortabilityReportProfileEvidence profile)
    {
        var provided = new Dictionary<(string Os, string Arch), PortabilityReportPlatformEvidence>();
        foreach (var row in profile.Platforms)
        {
            if (!RequiredPlatforms.Contains((row.Os, row.Arch))) Reject("UnknownPlatform", $"$/profiles/{profile.ProfileId}/platforms");
            if (!provided.TryAdd((row.Os, row.Arch), row)) Reject("DuplicatePlatform", $"$/profiles/{profile.ProfileId}/platforms");
        }
        if (profile.Build.Status != "Passed")
            return RequiredPlatforms.Select(key => FailedBuildPlatform(profile, key.Os, key.Arch)).ToImmutableArray();
        return RequiredPlatforms.Select(key => provided.TryGetValue(key, out var row) ? BuildPlatform(set, profile, row) : MissingPlatform(profile, key.Os, key.Arch)).ToImmutableArray();
    }

    private static PortabilityReportPlatform FailedBuildPlatform(PortabilityReportProfileEvidence profile, string os, string arch)
    {
        var reason = profile.Build.Status == "NonReproducible" ? "NonReproducibleBuild" : profile.Build.DirectReasons.FirstOrDefault() ?? "TargetBuildRejected";
        return new(os, arch, null, null, null, null, null, null, null, null, null, null, "Failed", new[] { reason, "RowFailed" }.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
            null, null, null, null, null, new(null, null, null, null), ImmutableArray<PortabilityReportVector>.Empty, ImmutableArray<PortabilityReportOutcomeRow>.Empty);
    }

    private static PortabilityReportPlatform BuildPlatform(PortabilityReportEvidenceSet set, PortabilityReportProfileEvidence profile, PortabilityReportPlatformEvidence evidence)
    {
        if (!RequiredPlatforms.Contains((evidence.Os, evidence.Arch))) Reject("UnknownPlatform", $"$/profiles/{profile.ProfileId}/platforms");
        ValidateOptionalDigest(evidence.RuntimeClosureDigest, "runtimeClosureDigest");
        ValidateOptionalDigest(evidence.LauncherDigest, "launcherDigest");
        ValidateOptionalDigest(evidence.HarnessDigest, "harnessDigest");
        ValidateOptionalDigest(evidence.PortabilityManifestDigest, "portabilityManifestDigest");
        ValidateOptionalDigest(evidence.PackageDigest, "packageDigest");
        ValidateOptionalDigest(evidence.ArtifactDigest, "artifactDigest");
        if (evidence.Consumer.Status is not ("Passed" or "Failed" or "Unavailable") || evidence.Jit.Status is not ("Passed" or "Failed" or "Unavailable") || evidence.Oracle.Status is not ("Passed" or "Failed" or "Unavailable"))
            Reject("InvalidGateStatus", $"$/profiles/{profile.ProfileId}/platforms/{evidence.Os}-{evidence.Arch}");
        foreach (var gate in new[] { evidence.Consumer, evidence.Jit, evidence.Oracle })
        {
            if (gate.DirectReasons.Any(reason => !AllowedReasons.Contains(reason))) Reject("UnknownReason", $"$/profiles/{profile.ProfileId}/platforms/{evidence.Os}-{evidence.Arch}/{gate.Kind}");
            if (gate.Status == "Passed" && gate.RawReceiptBytes.Length == 0) Reject("MissingGateReceipt", $"$/profiles/{profile.ProfileId}/platforms/{evidence.Os}-{evidence.Arch}/{gate.Kind}");
            ValidateReceiptRevision(gate.RawReceiptBytes, set.ExpectedSourceRevision, $"$/profiles/{profile.ProfileId}/platforms/{evidence.Os}-{evidence.Arch}/{gate.Kind}");
        }
        if (profile.Build.Status == "Passed")
        {
            if (evidence.PortabilityManifestDigest != profile.Build.PortabilityManifestDigest || evidence.PackageDigest != profile.Build.PackageDigest || evidence.ArtifactDigest != profile.Build.ArtifactDigest)
                Reject("ArtifactIdentityMismatch", $"$/profiles/{profile.ProfileId}/platforms/{evidence.Os}-{evidence.Arch}");
            if (evidence.HarnessDigest is null) Reject("MissingHarnessIdentity", $"$/profiles/{profile.ProfileId}/platforms/{evidence.Os}-{evidence.Arch}");
            RequireText(evidence.OsIdentity, "osIdentity"); RequireText(evidence.KernelIdentity, "kernelIdentity"); RequireText(evidence.RuntimeVendor, "runtimeVendor"); RequireText(evidence.RuntimeVersion, "runtimeVersion");
            RequireDigest(evidence.RuntimeClosureDigest, "runtimeClosureDigest"); RequireDigest(evidence.LauncherDigest, "launcherDigest"); RequireDigest(evidence.HarnessDigest, "harnessDigest");
        }
        var vectors = NormalizeVectors(evidence.Vectors, profile.ProfileId, evidence.Os);
        var outcomes = NormalizeOutcomes(evidence.Outcomes, vectors, profile.ProfileId, evidence.Os);
        var vectorDigest = vectors.Length == 0 ? null : PortabilityContract.DomainHash("strogo.portability-report.v0.1/vector-set", CanonicalJson.Encode(vectors.Select(vector => new { vectorId = vector.VectorId, inputDigest = vector.InputDigest }).ToArray()));
        var outcomeDigest = outcomes.Length == 0 ? null : PortabilityContract.DomainHash("strogo.portability-report.v0.1/domain-outcomes", CanonicalJson.Encode(outcomes.Select(OutcomePayload).ToArray()));
        var consumerDigest = GateDigest(set, profile, evidence, evidence.Consumer, vectorDigest, outcomeDigest);
        var jitDigest = GateDigest(set, profile, evidence, evidence.Jit, vectorDigest, outcomeDigest);
        var oracleDigest = GateDigest(set, profile, evidence, evidence.Oracle, vectorDigest, outcomeDigest);
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        var passed = evidence.Consumer.Status == "Passed" && evidence.Jit.Status == "Passed" && evidence.Oracle.Status == "Passed" &&
            evidence.Consumer.DirectReasons.Length == 0 && evidence.Jit.DirectReasons.Length == 0 && evidence.Oracle.DirectReasons.Length == 0 &&
            vectorDigest is not null && outcomeDigest is not null;
        if (!passed)
        {
            reasons.UnionWith(evidence.Consumer.DirectReasons);
            reasons.UnionWith(evidence.Jit.DirectReasons);
            reasons.UnionWith(evidence.Oracle.DirectReasons);
            if (evidence.Consumer.Status != "Passed") reasons.Add("ConsumerFailed");
            if (evidence.Jit.Status != "Passed") reasons.Add("JitEvidenceMissing");
            if (evidence.Oracle.Status != "Passed") reasons.Add("OracleMismatch");
            reasons.Add("RowFailed");
        }
        var status = passed ? "Passed" : "Failed";
        return new PortabilityReportPlatform(evidence.Os, evidence.Arch, evidence.OsIdentity, evidence.KernelIdentity, evidence.RuntimeVendor, evidence.RuntimeVersion,
            evidence.RuntimeClosureDigest, evidence.LauncherDigest, evidence.HarnessDigest, evidence.PortabilityManifestDigest, evidence.PackageDigest,
            evidence.ArtifactDigest, status, reasons.Order(StringComparer.Ordinal).ToImmutableArray(), vectorDigest, outcomeDigest, consumerDigest, jitDigest, oracleDigest,
            new PortabilityReportDiagnostics(CanonicalJson.RawDigest(evidence.Consumer.RawReceiptBytes), CanonicalJson.RawDigest(evidence.Jit.RawReceiptBytes), null, evidence.PerformanceReceiptDigest),
            vectors, outcomes);
    }

    private static PortabilityReportPlatform MissingPlatform(PortabilityReportProfileEvidence profile, string os, string arch)
        => new(os, arch, null, null, null, null, profile.Build.RuntimeClosureDigest, null, profile.Build.HarnessDigest, profile.Build.PortabilityManifestDigest, profile.Build.PackageDigest, profile.Build.ArtifactDigest,
            "Unavailable", ["EnvironmentUnavailable", "RowUnavailable"], null, null, null, null, null, new(null, null, null, null), [], []);

    private static ImmutableArray<PortabilityReportVector> NormalizeVectors(IEnumerable<PortabilityReportVector> input, string profileId, string os)
    {
        var vectors = input.ToImmutableArray();
        if (vectors.Any(vector => vector is null)) Reject("InvalidVector", $"$/profiles/{profileId}/platforms/{os}/vectors");
        foreach (var vector in vectors) { if (string.IsNullOrWhiteSpace(vector.VectorId)) Reject("InvalidVector", "$/vectors/vectorId"); RequireDigest(vector.InputDigest, "$/vectors/inputDigest"); }
        if (vectors.Select(vector => vector.VectorId).Distinct(StringComparer.Ordinal).Count() != vectors.Length) Reject("OutcomeCoverageMismatch", "$/vectors");
        return vectors.OrderBy(vector => vector.VectorId, StringComparer.Ordinal).ToImmutableArray();
    }

    private static ImmutableArray<PortabilityReportOutcomeRow> NormalizeOutcomes(IEnumerable<PortabilityReportOutcomeRow> input, ImmutableArray<PortabilityReportVector> vectors, string profileId, string os)
    {
        var outcomes = input.ToImmutableArray();
        if (outcomes.Select(row => row.VectorId).Distinct(StringComparer.Ordinal).Count() != outcomes.Length) Reject("OutcomeCoverageMismatch", $"$/profiles/{profileId}/platforms/{os}/outcomes");
        if (outcomes.Length != vectors.Length) Reject("OutcomeCoverageMismatch", $"$/profiles/{profileId}/platforms/{os}/outcomes");
        var vectorMap = vectors.ToDictionary(vector => vector.VectorId, StringComparer.Ordinal);
        foreach (var row in outcomes)
        {
            if (!vectorMap.TryGetValue(row.VectorId, out var vector) || vector.InputDigest != row.InputDigest) Reject("OutcomeCoverageMismatch", $"$/outcomes/{row.VectorId}");
            if (row.Classification is not ("OwnerInDomain" or "OwnerOutsideDomain" or "TransportOnly")) Reject("InvalidOutcomeClassification", $"$/outcomes/{row.VectorId}");
            if (row.Classification == "TransportOnly" && row.OwnerClauseDigest is not null || row.Classification != "TransportOnly" && row.OwnerClauseDigest is null) Reject("InvalidOwnerClause", $"$/outcomes/{row.VectorId}");
            if (row.OwnerClauseDigest is not null) RequireDigest(row.OwnerClauseDigest, $"$/outcomes/{row.VectorId}/ownerClauseDigest");
            if (row.Termination is not ("Returned" or "Refused")) Reject("InvalidTermination", $"$/outcomes/{row.VectorId}");
            ValidateOutcome(row.Outcome, $"$/outcomes/{row.VectorId}/outcome");
            if ((row.Outcome.Kind == "success" && row.Termination != "Returned") || (row.Outcome.Kind == "refusal" && row.Termination != "Refused"))
                Reject("TerminationOutcomeMismatch", $"$/outcomes/{row.VectorId}");
        }
        return outcomes.OrderBy(row => row.VectorId, StringComparer.Ordinal).ToImmutableArray();
    }

    private static void ValidateOutcome(PortabilityReportOutcome outcome, string locus)
    {
        if (outcome is null) Reject("InvalidOutcome", locus);
        if (outcome.Kind == "success") { if (outcome.Value is null || outcome.Code is not null || outcome.Locus is not null || outcome.Details is not null) Reject("InvalidOutcome", locus); }
        else if (outcome.Kind == "refusal") { if (outcome.Value is not null || string.IsNullOrEmpty(outcome.Code) || string.IsNullOrEmpty(outcome.Locus) || outcome.Details is null) Reject("InvalidOutcome", locus); }
        else Reject("InvalidOutcome", locus);
    }

    private static object OutcomePayload(PortabilityReportOutcomeRow row)
    {
        object outcome = row.Outcome.Kind == "success"
            ? new { kind = "success", value = row.Outcome.Value!.Value }
            : new { kind = "refusal", code = row.Outcome.Code!, locus = row.Outcome.Locus!, details = row.Outcome.Details!.Value };
        return new
        {
            row.VectorId,
            row.InputDigest,
            domain = new { classification = row.Classification, ownerClauseDigest = row.OwnerClauseDigest },
            row.Termination,
            outcome
        };
    }

    private static string GateDigest(PortabilityReportEvidenceSet set, PortabilityReportProfileEvidence profile, PortabilityReportPlatformEvidence platform,
        PortabilityReportGateEvidence gate, string? vectorDigest, string? outcomeDigest)
    {
        var payload = new
        {
            schemaVersion = SchemaVersion, sourceRevision = set.ExpectedSourceRevision, profileId = profile.ProfileId, os = platform.Os, arch = platform.Arch,
            moduleDigest = set.ModuleDigest, ownerBundleDigest = set.OwnerBundleDigest, proofDigest = set.ProofDigest, dafnySourceDigest = set.DafnySourceDigest,
            portabilityManifestDigest = platform.PortabilityManifestDigest, packageDigest = platform.PackageDigest, artifactDigest = platform.ArtifactDigest,
            runtimeClosureDigest = platform.RuntimeClosureDigest, harnessDigest = platform.HarnessDigest, gateStatus = gate.Status,
            directGateReasons = gate.DirectReasons.Order(StringComparer.Ordinal).ToArray(), kind = gate.Kind, publicApiDigest = gate.PublicApiDigest,
            vectorSetDigest = vectorDigest, outcomeDigest, entrySymbol = gate.EntrySymbol, candidateSymbol = gate.CandidateSymbol, calls = gate.Calls,
            eventKinds = gate.EventKinds.Order(StringComparer.Ordinal).ToArray(), ownerDomainRuleDigest = gate.OwnerDomainRuleDigest
        };
        return PortabilityContract.DomainHash($"strogo.portability-report.v0.1/gate/{profile.ProfileId}/{platform.Os}/{platform.Arch}/{gate.Kind}", CanonicalJson.Encode(payload));
    }

    private static string BuildGateDigest(PortabilityReportEvidenceSet set, PortabilityReportProfileEvidence profile)
        => PortabilityContract.DomainHash($"strogo.portability-report.v0.1/gate/{profile.ProfileId}/profile/profile/build", CanonicalJson.Encode(new
        {
            sourceRevision = set.ExpectedSourceRevision, profileId = profile.ProfileId, translatorDigest = profile.TranslatorDigest,
            buildToolchainDigest = profile.BuildToolchainDigest, runtimeRequirementDigest = profile.RuntimeRequirementDigest,
            portabilityManifestDigest = profile.Build.PortabilityManifestDigest, packageDigest = profile.Build.PackageDigest, artifactDigest = profile.Build.ArtifactDigest,
            runtimeClosureDigest = profile.Build.RuntimeClosureDigest, twoRootInventoryDigest = profile.Build.TwoRootInventoryDigest,
            gateStatus = profile.Build.Status, directGateReasons = profile.Build.DirectReasons.Order(StringComparer.Ordinal).ToArray()
        }));

    private static PortabilityReportBuildIdentity BuildQuartet(PortabilityReportBuildEvidence build)
        => new(build.PortabilityManifestDigest, build.PackageDigest, build.ArtifactDigest, build.RuntimeClosureDigest);

    private static object CanonicalPayload(PortabilityReportEvidenceSet set, ImmutableArray<PortabilityReportProfile> profiles, string comparisonStatus,
        ImmutableArray<string> reasons, string semanticDigest, string timestamp) => new
    {
        schemaVersion = SchemaVersion, purpose = Purpose, contractStatus = ContractStatus, admissionStatus = AdmissionStatus,
        sourceRevision = set.ExpectedSourceRevision, moduleDigest = set.ModuleDigest, ownerBundleDigest = set.OwnerBundleDigest, proofDigest = set.ProofDigest,
        dafnySourceDigest = set.DafnySourceDigest, profiles = profiles.Select(ProfilePayload).ToArray(), comparisonStatus, reasons = reasons.ToArray(), semanticDigest, createdAtUtc = timestamp
    };

    private static object SemanticPayload(PortabilityReportEvidenceSet set, ImmutableArray<PortabilityReportProfile> profiles, string comparisonStatus, ImmutableArray<string> reasons) => new
    {
        schemaVersion = SchemaVersion, purpose = Purpose, contractStatus = ContractStatus, admissionStatus = AdmissionStatus,
        sourceRevision = set.ExpectedSourceRevision, moduleDigest = set.ModuleDigest, ownerBundleDigest = set.OwnerBundleDigest, proofDigest = set.ProofDigest,
        dafnySourceDigest = set.DafnySourceDigest, profiles = profiles.Select(profile => new
        {
            profileId = profile.ProfileId, profile.TranslatorDigest, profile.BuildToolchainDigest, profile.RuntimeRequirementDigest,
            profile.UpstreamWarningBaselineDigest, profile.UpstreamWarningApprovalDigest, profile.PortabilityManifestDigest, profile.PackageDigest,
            profile.ArtifactDigest, profile.BuildReproducible, profile.BuildGateDigest, profile.Status, reasonCodes = profile.ReasonCodes.ToArray(),
            platforms = profile.Platforms.Select(row => new
            {
                row.Os, row.Arch, row.OsIdentity, row.KernelIdentity, row.RuntimeVendor, row.RuntimeVersion, row.RuntimeClosureDigest, row.LauncherDigest,
                row.HarnessDigest, row.PortabilityManifestDigest, row.PackageDigest, row.ArtifactDigest, row.Status, reasonCodes = row.ReasonCodes.ToArray(),
                row.VectorSetDigest, row.OutcomeDigest, row.ConsumerGateDigest, row.JitGateDigest, row.OracleGateDigest,
                outcomes = row.Outcomes.Select(OutcomePayload).ToArray()
            }).ToArray()
        }).ToArray(), comparisonStatus, reasons = reasons.ToArray()
    };

    private static object ProfilePayload(PortabilityReportProfile profile) => new
    {
        profileId = profile.ProfileId, profile.TranslatorDigest, profile.BuildToolchainDigest, profile.RuntimeRequirementDigest,
        profile.UpstreamWarningBaselineDigest, profile.UpstreamWarningApprovalDigest, profile.PortabilityManifestDigest, profile.PackageDigest,
        profile.ArtifactDigest, buildReproducible = profile.BuildReproducible, profile.BuildGateDigest, profile.Status,
        reasonCodes = profile.ReasonCodes.ToArray(), platforms = profile.Platforms.Select(PlatformPayload).ToArray()
    };

    private static object PlatformPayload(PortabilityReportPlatform row) => new
    {
        row.Os, row.Arch, row.OsIdentity, row.KernelIdentity, row.RuntimeVendor, row.RuntimeVersion, row.RuntimeClosureDigest, row.LauncherDigest,
        row.HarnessDigest, row.PortabilityManifestDigest, row.PackageDigest, row.ArtifactDigest, row.Status, reasonCodes = row.ReasonCodes.ToArray(),
        row.VectorSetDigest, row.OutcomeDigest, row.ConsumerGateDigest, row.JitGateDigest, row.OracleGateDigest,
        diagnostics = new { row.Diagnostics.ConsumerReceiptDigest, row.Diagnostics.JitReceiptDigest, row.Diagnostics.StderrDigest, row.Diagnostics.PerformanceReceiptDigest }
    };

    private static DateTimeOffset ParseTimestamp(ReadOnlySpan<byte> bytes)
    {
        try
        {
            using var document = CanonicalJson.ParseStrict(Encoding.UTF8.GetString(bytes));
            var text = document.RootElement.GetProperty("createdAtUtc").GetString();
            var timestamp = default(DateTimeOffset);
            if (text is null || !DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp)) Reject("InvalidCreatedAtUtc", "$/createdAtUtc");
            return timestamp;
        }
        catch (PortabilityContractException) { throw; }
        catch { Reject("InvalidCreatedAtUtc", "$/createdAtUtc"); return default; }
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        var text = utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        if (!DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _)) Reject("InvalidCreatedAtUtc", "$/createdAtUtc");
        return text;
    }

    private static void ValidateEvidenceAnchors(PortabilityReportEvidenceSet set)
    {
        if (!RevisionPattern.IsMatch(set.ExpectedSourceRevision)) Reject("InvalidSourceRevision", "$/sourceRevision");
        RequireDigest(set.ModuleDigest, "$/moduleDigest"); RequireDigest(set.OwnerBundleDigest, "$/ownerBundleDigest"); RequireDigest(set.ProofDigest, "$/proofDigest"); RequireDigest(set.DafnySourceDigest, "$/dafnySourceDigest");
        if (set.Profiles.Length != 2 || set.Profiles.Select(profile => profile.ProfileId).Distinct(StringComparer.Ordinal).Count() != set.Profiles.Length) Reject("MissingProfile", "$/profiles");
    }

    private static void ValidateBuild(PortabilityReportBuildEvidence build, string profileId)
    {
        if (build.Status is not ("Passed" or "TargetBuildRejected" or "NonReproducible")) Reject("InvalidBuildStatus", $"$/profiles/{profileId}/build");
        if (build.DirectReasons.Any(reason => !AllowedReasons.Contains(reason))) Reject("UnknownReason", $"$/profiles/{profileId}/build");
        if (build.Status == "Passed")
        {
            if (build.DirectReasons.Length != 0) Reject("PassedBuildHasReasons", $"$/profiles/{profileId}/build");
            RequireDigest(build.PortabilityManifestDigest, $"$/profiles/{profileId}/portabilityManifestDigest"); RequireDigest(build.PackageDigest, $"$/profiles/{profileId}/packageDigest");
            RequireDigest(build.ArtifactDigest, $"$/profiles/{profileId}/artifactDigest"); RequireDigest(build.RuntimeClosureDigest, $"$/profiles/{profileId}/runtimeClosureDigest"); RequireDigest(build.TwoRootInventoryDigest, $"$/profiles/{profileId}/buildGate"); RequireDigest(build.HarnessDigest, $"$/profiles/{profileId}/harnessDigest");
        }
        else if (build.Status == "NonReproducible")
        {
            if (build.DirectReasons.All(reason => reason != "NonReproducibleBuild")) Reject("BuildReasonMissing", $"$/profiles/{profileId}/build");
            if (build.PortabilityManifestDigest is not null || build.PackageDigest is not null || build.ArtifactDigest is not null || build.RuntimeClosureDigest is not null) Reject("InvalidPartialBuild", $"$/profiles/{profileId}/build");
        }
        else if (build.PortabilityManifestDigest is not null || build.PackageDigest is not null || build.ArtifactDigest is not null || build.RuntimeClosureDigest is not null || build.TwoRootInventoryDigest is not null || build.HarnessDigest is not null)
            Reject("InvalidPartialBuild", $"$/profiles/{profileId}/build");
    }

    private static void ValidateOptionalDigest(string? value, string name) { if (value is not null) RequireDigest(value, name); }
    private static void RequireText(string? value, string name) { if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) Reject("MissingPlatformIdentity", $"$/platforms/{name}"); }
    private static void ValidateReceiptRevision(byte[] bytes, string expectedRevision, string locus)
    {
        if (bytes.Length == 0) Reject("MissingReceipt", locus);
        try
        {
            using var document = CanonicalJson.ParseStrict(Encoding.UTF8.GetString(bytes));
            if (!CanonicalJson.Encode(document.RootElement).SequenceEqual(bytes) || document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("sourceRevision", out var revision) || revision.ValueKind != JsonValueKind.String || revision.GetString() != expectedRevision)
                Reject("SourceRevisionMismatch", locus);
        }
        catch (PortabilityContractException) { throw; }
        catch { Reject("InvalidReceipt", locus); }
    }
    private static void RequireDigest(string? value, string locus) { if (value is null || !DigestPattern.IsMatch(value)) Reject("InvalidDigest", locus); }
    private static void RequireProfile(string profileId) { if (!ProfileOrder.Contains(profileId, StringComparer.Ordinal)) Reject("UnknownProfile", "$/profiles/profileId"); }
    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Split('/').Any(segment => segment is "" or "." or "..")) Reject("InvalidMarkdownPath", "$/canonicalJsonRelativePath");
    }
    [DoesNotReturn]
    private static void Reject(string reason, string locus) => throw new PortabilityContractException("PortabilityReportRejected", locus, new { reason });

    private sealed record BuildState(PortabilityReportV01 Report);
}

public sealed record PortabilityReportProfile(
    string ProfileId,
    string TranslatorDigest,
    string BuildToolchainDigest,
    string RuntimeRequirementDigest,
    string? UpstreamWarningBaselineDigest,
    string? UpstreamWarningApprovalDigest,
    PortabilityReportBuildIdentity BuildIdentity,
    bool? BuildReproducible,
    string BuildGateDigest,
    string Status,
    ImmutableArray<string> ReasonCodes,
    ImmutableArray<PortabilityReportPlatform> Platforms,
    bool Portable,
    string? CommonVectorSetDigest,
    string? CommonOutcomeDigest)
{
    public string? PortabilityManifestDigest => BuildIdentity.PortabilityManifestDigest;
    public string? PackageDigest => BuildIdentity.PackageDigest;
    public string? ArtifactDigest => BuildIdentity.ArtifactDigest;
}

public sealed record PortabilityReportBuildIdentity(
    string? PortabilityManifestDigest,
    string? PackageDigest,
    string? ArtifactDigest,
    string? RuntimeClosureDigest);

public sealed record PortabilityReportPlatform(
    string Os,
    string Arch,
    string? OsIdentity,
    string? KernelIdentity,
    string? RuntimeVendor,
    string? RuntimeVersion,
    string? RuntimeClosureDigest,
    string? LauncherDigest,
    string? HarnessDigest,
    string? PortabilityManifestDigest,
    string? PackageDigest,
    string? ArtifactDigest,
    string Status,
    ImmutableArray<string> ReasonCodes,
    string? VectorSetDigest,
    string? OutcomeDigest,
    string? ConsumerGateDigest,
    string? JitGateDigest,
    string? OracleGateDigest,
    PortabilityReportDiagnostics Diagnostics,
    ImmutableArray<PortabilityReportVector> Vectors,
    ImmutableArray<PortabilityReportOutcomeRow> Outcomes);

public sealed record PortabilityReportDiagnostics(string? ConsumerReceiptDigest, string? JitReceiptDigest, string? StderrDigest, string? PerformanceReceiptDigest);

internal sealed record PortabilityReportWriterFaultPlan(
    int? FailWriteOrdinal = null,
    int? FailReadOrdinal = null,
    int? FailHashOrdinal = null,
    bool FailCleanup = false);

public sealed class PortabilityReportWriterException : Exception
{
    public PortabilityReportWriterException(string stage, string stagingDirectory, Exception innerException, Exception? cleanupException = null)
        : base($"portability report writer failed during {stage}", innerException)
    {
        Stage = stage;
        StagingDirectory = stagingDirectory;
        CleanupException = cleanupException;
    }

    public string Stage { get; }
    public string StagingDirectory { get; }
    public Exception? CleanupException { get; }
}

public static class PortabilityReportWriter
{
    public static void WriteNewDirectory(PortabilityReportV01 report, PortabilityReportEvidenceSet evidence, string finalDirectory)
        => WriteNewDirectory(report, evidence, finalDirectory, faultPlan: null);

    internal static void WriteNewDirectory(PortabilityReportV01 report, PortabilityReportEvidenceSet evidence, string finalDirectory, PortabilityReportWriterFaultPlan? faultPlan)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(evidence);
        if (string.IsNullOrWhiteSpace(finalDirectory)) throw new ArgumentException("Directory is required", nameof(finalDirectory));
        _ = PortabilityReportV01.Validate(report.CanonicalJsonBytes(), evidence);
        var finalPath = Path.GetFullPath(finalDirectory);
        var parent = Directory.GetParent(finalPath)?.FullName ?? throw new PortabilityContractException("PortabilityReportRejected", "$/finalDirectory", new { reason = "InvalidParent" });
        if (Directory.Exists(finalPath) || File.Exists(finalPath)) throw new PortabilityContractException("PortabilityReportRejected", "$/finalDirectory", new { reason = "DestinationExists" });
        Directory.CreateDirectory(parent);
        RejectReparse(parent, "$/finalDirectory/parent");
        var staging = finalPath + ".staging-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var faults = new FaultController(faultPlan);
        string stage = "create-staging";
        try
        {
            Directory.CreateDirectory(staging);
            RejectReparse(staging, "$/staging");
            stage = "write-report";
            faults.Write(Path.Combine(staging, "report.json"), report.CanonicalJsonBytes());
            faults.Write(Path.Combine(staging, "REPORT.md"), report.MarkdownBytes("report.json"));
            var evidenceRoot = Path.Combine(staging, "evidence");
            Directory.CreateDirectory(evidenceRoot);
            var index = new List<EvidenceIndexEntry>();
            foreach (var profile in evidence.Profiles.OrderBy(profile => profile.ProfileId, StringComparer.Ordinal))
            {
                var profileRoot = Path.Combine(evidenceRoot, profile.ProfileId, "profile"); Directory.CreateDirectory(profileRoot);
                var reportProfile = report.Profiles.Single(item => item.ProfileId == profile.ProfileId);
                WriteReceipt(profileRoot, "build.json", profile.Build.RawReceiptBytes, index, $"evidence/{profile.ProfileId}/profile/build.json", "build", reportProfile.BuildGateDigest, faults);
                foreach (var platform in profile.Platforms.OrderBy(platform => platform.Os, StringComparer.Ordinal))
                {
                    var platformRoot = Path.Combine(evidenceRoot, profile.ProfileId, $"{platform.Os}-{platform.Arch}"); Directory.CreateDirectory(platformRoot);
                    var reportRow = reportProfile.Platforms.Single(item => item.Os == platform.Os && item.Arch == platform.Arch);
                    WriteReceipt(platformRoot, "consumer.json", platform.Consumer.RawReceiptBytes, index, $"evidence/{profile.ProfileId}/{platform.Os}-{platform.Arch}/consumer.json", "consumer", reportRow.ConsumerGateDigest, faults);
                    WriteReceipt(platformRoot, "jit.json", platform.Jit.RawReceiptBytes, index, $"evidence/{profile.ProfileId}/{platform.Os}-{platform.Arch}/jit.json", "jit", reportRow.JitGateDigest, faults);
                    WriteReceipt(platformRoot, "oracle.json", platform.Oracle.RawReceiptBytes, index, $"evidence/{profile.ProfileId}/{platform.Os}-{platform.Arch}/oracle.json", "oracle", reportRow.OracleGateDigest, faults);
                }
            }
            faults.Write(Path.Combine(staging, "evidence-index.json"), Kernel.Core.CanonicalJson.Encode(index.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray()));
            stage = "read-back-validation";
            _ = PortabilityReportV01.Validate(faults.Read(Path.Combine(staging, "report.json")), evidence);
            stage = "hash-completion-marker";
            var marker = new StringBuilder();
            foreach (var path in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(staging, path).Replace(Path.DirectorySeparatorChar, '/');
                marker.Append(faults.Hash(faults.Read(path))).Append("  ").AppendLine(relative);
            }
            stage = "write-completion-marker";
            faults.Write(Path.Combine(staging, "sha256.txt"), new UTF8Encoding(false).GetBytes(marker.ToString()));
            stage = "publish-rename";
            Directory.Move(staging, finalPath);
        }
        catch (Exception exception)
        {
            if (!Directory.Exists(staging)) throw new PortabilityReportWriterException(stage, staging, exception);
            try
            {
                if (faults.FailCleanup) throw new IOException("injected cleanup failure");
                Directory.Delete(staging, recursive: true);
            }
            catch (Exception cleanupException)
            {
                throw new PortabilityReportWriterException("cleanup", staging, exception, cleanupException);
            }
            throw new PortabilityReportWriterException(stage, staging, exception);
        }
    }

    private static void WriteReceipt(string directory, string name, byte[] bytes, List<EvidenceIndexEntry> index, string relative, string role, string? gateDigest, FaultController faults)
    {
        faults.Write(Path.Combine(directory, name), bytes);
        index.Add(new EvidenceIndexEntry(role, relative, CanonicalJson.RawDigest(bytes), gateDigest));
    }

    private static void RejectReparse(string path, string locus)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new PortabilityContractException("PortabilityReportRejected", locus, new { reason = "ReparsePointRejected" });
    }

    private sealed record EvidenceIndexEntry(string Role, string Path, string RawReceiptDigest, string? GateDigest);

    private sealed class FaultController
    {
        private readonly PortabilityReportWriterFaultPlan _plan;
        private int _writes;
        private int _reads;
        private int _hashes;

        public FaultController(PortabilityReportWriterFaultPlan? plan) => _plan = plan ?? new();
        public bool FailCleanup => _plan.FailCleanup;

        public void Write(string path, byte[] bytes)
        {
            if (++_writes == _plan.FailWriteOrdinal) throw new IOException("injected write failure");
            File.WriteAllBytes(path, bytes);
        }

        public byte[] Read(string path)
        {
            if (++_reads == _plan.FailReadOrdinal) throw new IOException("injected read failure");
            return File.ReadAllBytes(path);
        }

        public string Hash(byte[] bytes)
        {
            if (++_hashes == _plan.FailHashOrdinal) throw new IOException("injected hash failure");
            return CanonicalJson.RawDigest(bytes);
        }
    }
}
