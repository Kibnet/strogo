using System.Collections.Immutable;

namespace Strogo.Modules.Portability;

public sealed class PortabilityPlatformStatus
{
    public PortabilityPlatformStatus(
        string os,
        string arch,
        string status,
        IEnumerable<string>? reasonCodes = null,
        string? portabilityManifestDigest = null,
        string? packageDigest = null,
        string? artifactDigest = null,
        string? runtimeClosureDigest = null)
        : this(os, arch, status, reasonCodes, portabilityManifestDigest, packageDigest, artifactDigest, runtimeClosureDigest, synthesized: false)
    {
    }

    private PortabilityPlatformStatus(
        string os,
        string arch,
        string status,
        IEnumerable<string>? reasonCodes,
        string? portabilityManifestDigest,
        string? packageDigest,
        string? artifactDigest,
        string? runtimeClosureDigest,
        bool synthesized)
    {
        ArgumentNullException.ThrowIfNull(os);
        ArgumentNullException.ThrowIfNull(arch);
        ArgumentNullException.ThrowIfNull(status);
        Os = os;
        Arch = arch;
        Status = status;
        ReasonCodes = (reasonCodes ?? []).ToImmutableArray();
        PortabilityManifestDigest = portabilityManifestDigest;
        PackageDigest = packageDigest;
        ArtifactDigest = artifactDigest;
        RuntimeClosureDigest = runtimeClosureDigest;
        Synthesized = synthesized;
    }

    public string Os { get; }
    public string Arch { get; }
    public string Status { get; }
    public ImmutableArray<string> ReasonCodes { get; }
    public string? PortabilityManifestDigest { get; }
    public string? PackageDigest { get; }
    public string? ArtifactDigest { get; }
    public string? RuntimeClosureDigest { get; }
    public bool Synthesized { get; }

    internal static PortabilityPlatformStatus Missing(string os, string arch)
        => new(os, arch, "Unavailable", ["EnvironmentUnavailable", "RowUnavailable"], null, null, null, null, synthesized: true);

    internal PortabilityPlatformStatus Normalize(IEnumerable<string> reasons)
        => new(Os, Arch, Status, reasons, PortabilityManifestDigest, PackageDigest, ArtifactDigest, RuntimeClosureDigest, Synthesized);
}

public static class PortabilityReportMatrix
{
    private static readonly ImmutableArray<(string Os, string Arch)> RequiredRows =
    [
        ("linux", "x64"),
        ("windows", "x64")
    ];

    private static readonly ImmutableHashSet<string> AllowedStatuses =
        ImmutableHashSet.Create(StringComparer.Ordinal, "Passed", "Failed", "Unavailable");

    private static readonly ImmutableHashSet<string> AllowedReasons =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "ArtifactIdentityMismatch",
            "BackendSemanticMismatch",
            "BuildNotReproducible",
            "ConsumerFailed",
            "EnvironmentUnavailable",
            "JitEvidenceMissing",
            "NonReproducibleBuild",
            "OracleMismatch",
            "RowFailed",
            "RowUnavailable",
            "TargetBuildRejected",
            "TargetExecutionFailed",
            "UnsupportedPortableAbi");

    public static ImmutableArray<PortabilityPlatformStatus> Complete(
        string profileId,
        IEnumerable<PortabilityPlatformStatus> providedRows)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(providedRows);
        if (!PortabilityContract.IsExecutionProfile(profileId)) Reject("$/profileId", "UnknownProfile");

        var byKey = new Dictionary<(string Os, string Arch), PortabilityPlatformStatus>();
        foreach (var row in providedRows)
        {
            if (row is null) Reject("$/platforms", "NullPlatformRow");
            var key = (row.Os, row.Arch);
            if (!RequiredRows.Contains(key)) Reject("$/platforms", "UnknownPlatformRow");
            if (!byKey.TryAdd(key, row)) Reject("$/platforms", "DuplicatePlatformRow");
            if (!AllowedStatuses.Contains(row.Status)) Reject("$/platforms", "UnknownRowStatus");
            if (row.ReasonCodes.Any(reason => !AllowedReasons.Contains(reason))) Reject("$/platforms", "UnknownReasonCode");
            if (row.ReasonCodes.Distinct(StringComparer.Ordinal).Count() != row.ReasonCodes.Length) Reject("$/platforms", "DuplicateReasonCode");
        }

        var completed = RequiredRows.Select(key => byKey.TryGetValue(key, out var row) ? Normalize(row) : PortabilityPlatformStatus.Missing(key.Os, key.Arch)).ToImmutableArray();
        var passed = completed.Where(row => row.Status == "Passed").ToArray();
        if (passed.Length > 1 && (passed.Select(row => row.PortabilityManifestDigest).Distinct(StringComparer.Ordinal).Count() != 1 ||
            passed.Select(row => row.PackageDigest).Distinct(StringComparer.Ordinal).Count() != 1 ||
            passed.Select(row => row.ArtifactDigest).Distinct(StringComparer.Ordinal).Count() != 1))
            Reject("$/platforms", "PlatformArtifactIdentityMismatch");
        return completed;
    }

    private static PortabilityPlatformStatus Normalize(PortabilityPlatformStatus row)
    {
        var reasons = row.ReasonCodes.ToHashSet(StringComparer.Ordinal);
        switch (row.Status)
        {
            case "Passed":
                if (reasons.Count != 0) Reject("$/platforms", "PassedRowHasReasons");
                if (!IsDigest(row.PortabilityManifestDigest) || !IsDigest(row.PackageDigest) || !IsDigest(row.ArtifactDigest) || !IsDigest(row.RuntimeClosureDigest))
                    Reject("$/platforms", "PassedRowEvidenceMissing");
                break;
            case "Failed":
                reasons.Add("RowFailed");
                break;
            case "Unavailable":
                reasons.Add("RowUnavailable");
                if (!reasons.Contains("EnvironmentUnavailable")) Reject("$/platforms", "UnavailableRowHasNoEnvironmentReason");
                break;
        }
        return row.Normalize(reasons.Order(StringComparer.Ordinal));
    }

    private static bool IsDigest(string? value)
        => value is not null && value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Reject(string locus, string reason)
        => throw new PortabilityContractException("PortabilityReportRejected", locus, new { reason });
}
