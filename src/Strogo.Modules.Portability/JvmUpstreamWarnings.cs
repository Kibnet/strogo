using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Kernel.Core;

namespace Strogo.Modules.Portability;

public sealed record JvmWarningBaselineIdentity(
    string FixtureModuleDigest,
    string DafnySourceDigest,
    string TranslatorDigest,
    string JavacClosureDigest,
    string ProbeCommandDigest,
    string ValidatorDigest,
    string HarnessDigest);

public sealed record JvmSourceInventoryEntry(string Path, string Sha256, string Bytes);

public sealed record JvmWarningDiagnostic(
    string Path,
    string Line,
    string Column,
    string Category,
    string Message,
    string ContextDigest);

public sealed class JvmWarningBaselineCandidate
{
    private readonly byte[] bytes;
    private readonly byte[] canonicalDiagnostics;

    internal JvmWarningBaselineCandidate(
        byte[] bytes,
        byte[] canonicalDiagnostics,
        string baselineDigest,
        string normalizedDiagnosticsDigest,
        string sourceInventoryDigest,
        ImmutableArray<JvmWarningDiagnostic> warnings)
    {
        this.bytes = bytes.ToArray();
        this.canonicalDiagnostics = canonicalDiagnostics.ToArray();
        BaselineDigest = baselineDigest;
        NormalizedDiagnosticsDigest = normalizedDiagnosticsDigest;
        SourceInventoryDigest = sourceInventoryDigest;
        Warnings = warnings;
    }

    public byte[] Bytes => bytes.ToArray();
    public byte[] CanonicalDiagnostics => canonicalDiagnostics.ToArray();
    public string BaselineDigest { get; }
    public string NormalizedDiagnosticsDigest { get; }
    public string SourceInventoryDigest { get; }
    public ImmutableArray<JvmWarningDiagnostic> Warnings { get; }
}

public static class JvmUpstreamWarnings
{
    public const string SchemaVersion = "strogo.jvm-upstream-warning-baseline.v0.1";
    public const string ValidatorIdentity = "strogo.jvm-upstream-warning-validator.v0.1";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex PrimaryPattern = new("^(?<path>.+):(?<line>[1-9][0-9]*): warning: \\[(?<category>[a-z][a-z0-9-]*)\\] (?<message>[^\\r\\n]+)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex SummaryPattern = new("^(?<count>[1-9][0-9]*) warnings?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly ImmutableDictionary<string, int> ExpectedCategories = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["cast"] = 35,
        ["rawtypes"] = 67,
        ["serial"] = 1,
        ["varargs"] = 3
    }.ToImmutableDictionary(StringComparer.Ordinal);

    public static JvmWarningBaselineCandidate CreateCandidate(
        JvmWarningBaselineIdentity identity,
        IEnumerable<(string Path, byte[] Bytes)> sources,
        ReadOnlySpan<byte> stderr)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(sources);
        ValidateIdentity(identity);
        var inventory = BuildInventory(sources);
        var parsed = ParseDiagnostics(stderr, inventory.Select(item => item.Path).ToImmutableHashSet(StringComparer.Ordinal));
        ValidateExpectedCategories(parsed.Warnings);

        var inventoryBytes = CanonicalJson.Encode(inventory);
        var sourceInventoryDigest = PortabilityContract.DomainHash($"{SchemaVersion}/sources", inventoryBytes);
        var normalizedDiagnosticsDigest = PortabilityContract.DomainHash($"{SchemaVersion}/diagnostics", parsed.CanonicalBytes);
        var categories = parsed.Warnings
            .GroupBy(item => item.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new { category = group.Key, count = group.Count().ToString(CultureInfo.InvariantCulture) })
            .ToArray();
        var warningPayload = parsed.Warnings.Select(item => new
        {
            path = item.Path,
            line = item.Line,
            column = item.Column,
            category = item.Category,
            message = item.Message,
            contextDigest = item.ContextDigest
        }).ToArray();
        var baselineBytes = CanonicalJson.Encode(new
        {
            schemaVersion = SchemaVersion,
            profileId = PortabilityVersions.JvmProfile,
            fixtureModuleDigest = identity.FixtureModuleDigest,
            dafnySourceDigest = identity.DafnySourceDigest,
            translatorDigest = identity.TranslatorDigest,
            javacClosureDigest = identity.JavacClosureDigest,
            sourceInventoryDigest,
            probeCommandDigest = identity.ProbeCommandDigest,
            validatorDigest = identity.ValidatorDigest,
            harnessDigest = identity.HarnessDigest,
            normalizedDiagnosticsDigest,
            categories,
            warnings = warningPayload
        });
        var baselineDigest = PortabilityContract.DomainHash($"{SchemaVersion}/artifact", baselineBytes);
        return new(baselineBytes, parsed.CanonicalBytes, baselineDigest, normalizedDiagnosticsDigest, sourceInventoryDigest, parsed.Warnings);
    }

    public static JvmWarningBaselineCandidate Validate(
        ReadOnlySpan<byte> approvedBaseline,
        string ownerApprovedExpectedDigest,
        JvmWarningBaselineIdentity identity,
        IEnumerable<(string Path, byte[] Bytes)> sources,
        ReadOnlySpan<byte> stderr)
    {
        RequireDigest(ownerApprovedExpectedDigest, "$/ownerApprovedExpectedDigest");
        var actualApprovedDigest = PortabilityContract.DomainHash($"{SchemaVersion}/artifact", approvedBaseline);
        if (!string.Equals(actualApprovedDigest, ownerApprovedExpectedDigest, StringComparison.Ordinal))
            Reject("UpstreamWarningBaselineMismatch", "$/baselineDigest");
        var observed = CreateCandidate(identity, sources, stderr);
        if (!approvedBaseline.SequenceEqual(observed.Bytes))
            Reject("UpstreamWarningBaselineMismatch", "$/baseline");
        return observed;
    }

    private static ImmutableArray<JvmSourceInventoryEntry> BuildInventory(IEnumerable<(string Path, byte[] Bytes)> sources)
    {
        var entries = sources.Select(source =>
        {
            ArgumentNullException.ThrowIfNull(source.Bytes);
            var path = CanonicalPath(source.Path, "$/sources/path");
            return new JvmSourceInventoryEntry(path, CanonicalJson.RawDigest(source.Bytes), source.Bytes.LongLength.ToString(CultureInfo.InvariantCulture));
        }).OrderBy(item => item.Path, StringComparer.Ordinal).ToImmutableArray();
        if (entries.IsEmpty || entries.Select(item => item.Path).Distinct(StringComparer.Ordinal).Count() != entries.Length)
            Reject("UnexpectedUpstreamDiagnostic", "$/sources");
        return entries;
    }

    private static (byte[] CanonicalBytes, ImmutableArray<JvmWarningDiagnostic> Warnings) ParseDiagnostics(
        ReadOnlySpan<byte> stderr,
        ImmutableHashSet<string> sourcePaths)
    {
        string text;
        try { text = StrictUtf8.GetString(stderr); }
        catch (DecoderFallbackException) { Reject("UnexpectedUpstreamDiagnostic", "$/stderr/utf8"); throw; }
        if (text.Length == 0 || text.Contains('\0', StringComparison.Ordinal) || (!text.EndsWith('\n') && !text.EndsWith('\r')))
            Reject("UnexpectedUpstreamDiagnostic", "$/stderr/termination");
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length < 2 || lines[^1].Length != 0 || lines[^2].Length == 0)
            Reject("UnexpectedUpstreamDiagnostic", "$/stderr/terminalLf");
        Array.Resize(ref lines, lines.Length - 1);

        var blocks = new List<(string Primary, string Path, string Line, string Category, string Message, List<string> Context)>();
        int summaryCount = -1;
        for (var index = 0; index < lines.Length; index++)
        {
            var summary = SummaryPattern.Match(lines[index]);
            if (summary.Success)
            {
                if (index != lines.Length - 1) Reject("UnexpectedUpstreamDiagnostic", "$/stderr/summaryPosition");
                if (!int.TryParse(summary.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out summaryCount) || summaryCount <= 0)
                    Reject("UnexpectedUpstreamDiagnostic", "$/stderr/summaryCount");
                if ((summaryCount == 1) != lines[index].EndsWith(" warning", StringComparison.Ordinal))
                    Reject("UnexpectedUpstreamDiagnostic", "$/stderr/summaryGrammar");
                continue;
            }
            var primary = PrimaryPattern.Match(lines[index]);
            if (primary.Success)
            {
                var path = CanonicalPath(primary.Groups["path"].Value, "$/stderr/path");
                if (!sourcePaths.Contains(path)) Reject("UnexpectedUpstreamDiagnostic", "$/stderr/path");
                var line = primary.Groups["line"].Value;
                _ = ParseLine(line);
                var canonicalPrimary = $"{path}:{line}: warning: [{primary.Groups["category"].Value}] {primary.Groups["message"].Value}";
                blocks.Add((canonicalPrimary, path, line, primary.Groups["category"].Value, primary.Groups["message"].Value, []));
                continue;
            }
            if (blocks.Count == 0) Reject("UnexpectedUpstreamDiagnostic", "$/stderr/unconsumed");
            blocks[^1].Context.Add(lines[index]);
        }
        if (summaryCount < 0 || summaryCount != blocks.Count)
            Reject("UnexpectedUpstreamDiagnostic", "$/stderr/summaryCount");

        var canonicalLines = new List<string>();
        var warnings = blocks.Select(block =>
        {
            canonicalLines.Add(block.Primary);
            canonicalLines.AddRange(block.Context);
            var contextBytes = StrictUtf8.GetBytes(block.Context.Count == 0 ? "" : string.Join('\n', block.Context) + "\n");
            return new JvmWarningDiagnostic(
                block.Path,
                block.Line,
                "0",
                block.Category,
                block.Message,
                PortabilityContract.DomainHash($"{SchemaVersion}/context", contextBytes));
        }).OrderBy(item => item.Path, StringComparer.Ordinal)
          .ThenBy(item => item.Line, StringComparer.Ordinal)
          .ThenBy(item => item.Category, StringComparer.Ordinal)
          .ThenBy(item => item.Message, StringComparer.Ordinal)
          .ThenBy(item => item.ContextDigest, StringComparer.Ordinal)
          .ToImmutableArray();
        canonicalLines.Add(summaryCount == 1 ? "1 warning" : $"{summaryCount.ToString(CultureInfo.InvariantCulture)} warnings");
        return (StrictUtf8.GetBytes(string.Join('\n', canonicalLines) + "\n"), warnings);
    }

    private static string CanonicalPath(string path, string locus)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith('/') || path.StartsWith('\\') || path.Contains(':', StringComparison.Ordinal)) Reject("UnexpectedUpstreamDiagnostic", locus);
        var canonical = path.Replace('\\', '/');
        var segments = canonical.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.Any(character => character < 32 || character > 126)))
            Reject("UnexpectedUpstreamDiagnostic", locus);
        return canonical;
    }

    private static void ValidateExpectedCategories(ImmutableArray<JvmWarningDiagnostic> warnings)
    {
        var actual = warnings.GroupBy(item => item.Category, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        if (warnings.Length != 106 || actual.Count != ExpectedCategories.Count || ExpectedCategories.Any(pair => !actual.TryGetValue(pair.Key, out var count) || count != pair.Value))
            Reject("UpstreamWarningBaselineMismatch", "$/categories");
    }

    private static long ParseLine(string value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var line) || line <= 0)
            Reject("UnexpectedUpstreamDiagnostic", "$/stderr/line");
        return line;
    }

    private static void ValidateIdentity(JvmWarningBaselineIdentity identity)
    {
        RequireDigest(identity.FixtureModuleDigest, "$/fixtureModuleDigest");
        RequireDigest(identity.DafnySourceDigest, "$/dafnySourceDigest");
        RequireDigest(identity.TranslatorDigest, "$/translatorDigest");
        RequireDigest(identity.JavacClosureDigest, "$/javacClosureDigest");
        RequireDigest(identity.ProbeCommandDigest, "$/probeCommandDigest");
        RequireDigest(identity.ValidatorDigest, "$/validatorDigest");
        RequireDigest(identity.HarnessDigest, "$/harnessDigest");
    }

    private static void RequireDigest(string digest, string locus)
    {
        if (!DigestPattern.IsMatch(digest)) Reject("UpstreamWarningBaselineMismatch", locus);
    }

    private static void Reject(string reason, string locus)
        => throw new PortabilityContractException("TargetBuildRejected", locus, new { reason });
}
