using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kernel.Core;

namespace Strogo.Modules.Portability;

public sealed record PortabilityValidationProofObligation(
    string ObligationId,
    string Kind,
    string Status,
    string EvidenceDigest);

public sealed record PortabilityValidationProofDefinition(
    string ModuleDigest,
    string BundleDigest,
    string ToolchainDigest,
    string ClosureDigest,
    string ProofSourcesDigest,
    string TranscriptDigest,
    string SourceMapDigest,
    string Outcome,
    IReadOnlyList<PortabilityValidationProofObligation> Obligations);

public sealed class PortabilityValidationProofArtifact
{
    private readonly byte[] bytes;

    internal PortabilityValidationProofArtifact(byte[] bytes, string digest)
    {
        this.bytes = bytes.ToArray();
        Digest = digest;
    }

    public byte[] Bytes => bytes.ToArray();
    public string Digest { get; }
}

public static partial class PortabilityValidationProof
{
    public const string SchemaVersion = "strogo.validation-proof.v0.1";

    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex DecimalPattern = new("^(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex SegmentPattern = new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly ImmutableHashSet<string> ObligationKinds = ImmutableHashSet.Create(StringComparer.Ordinal,
        "assertion", "call-contract", "decreases", "loop-invariant", "postcondition", "total-wrapper", "wire-success");
    private static readonly ImmutableHashSet<string> InventoryRoles = ImmutableHashSet.Create(StringComparer.Ordinal,
        "archive", "configuration", "executable", "runtime", "source");

    public static PortabilityValidationProofArtifact Build(PortabilityValidationProofDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var obligations = NormalizeObligations(definition.Obligations);
        RequireDigest(definition.ModuleDigest, "moduleDigest");
        RequireDigest(definition.BundleDigest, "bundleDigest");
        RequireDigest(definition.ToolchainDigest, "toolchainDigest");
        RequireDigest(definition.ClosureDigest, "closureDigest");
        RequireDigest(definition.ProofSourcesDigest, "proofSourcesDigest");
        RequireDigest(definition.TranscriptDigest, "transcriptDigest");
        RequireDigest(definition.SourceMapDigest, "sourceMapDigest");
        if (definition.Outcome != "Verified") Reject("OutcomeInvalid", "$/outcome");

        var bytes = CanonicalJson.Encode(new
        {
            schemaVersion = SchemaVersion,
            contractStatus = PortabilityPackageVersions.ContractStatus,
            moduleDigest = definition.ModuleDigest,
            bundleDigest = definition.BundleDigest,
            toolchainDigest = definition.ToolchainDigest,
            closureDigest = definition.ClosureDigest,
            proofSourcesDigest = definition.ProofSourcesDigest,
            transcriptDigest = definition.TranscriptDigest,
            sourceMapDigest = definition.SourceMapDigest,
            outcome = definition.Outcome,
            obligations = obligations.Select(item => new
            {
                obligationId = item.ObligationId,
                kind = item.Kind,
                status = item.Status,
                evidenceDigest = item.EvidenceDigest
            }).ToArray()
        });
        return new(bytes, Digest(bytes));
    }

    public static string Digest(ReadOnlySpan<byte> proofBytes)
        => PortabilityContract.DomainHash("strogo.validation-proof.v0.1/artifact", proofBytes);

    public static string ProofSourcesDigest(IEnumerable<PortabilityPackageFile> proofSources)
    {
        ArgumentNullException.ThrowIfNull(proofSources);
        var files = proofSources.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        if (files.Length == 0 || files.Any(file => file.Role != "proof-source")) Reject("ProofSourcesInvalid", "$/proofSourcesDigest");
        var bytes = CanonicalJson.Encode(files.Select(file => new
        {
            path = file.Path,
            sha256 = file.Sha256,
            length = file.Length,
            role = file.Role
        }).ToArray());
        return PortabilityContract.DomainHash("strogo.validation-proof.v0.1/proof-sources", bytes);
    }

    public static string TranscriptDigest(ReadOnlySpan<byte> transcriptBytes)
        => PortabilityContract.DomainHash("strogo.validation-proof.v0.1/transcript", transcriptBytes);

    public static string ToolchainDigest(ReadOnlySpan<byte> inventoryBytes)
        => PortabilityContract.DomainHash("strogo.validation-proof.v0.1/toolchain", inventoryBytes);

    public static string ClosureDigest(ReadOnlySpan<byte> inventoryBytes)
        => PortabilityContract.DomainHash("strogo.validation-proof.v0.1/closure", inventoryBytes);

    public static string SourceMapDigest(ReadOnlySpan<byte> sourceMapBytes)
        => PortabilityContract.DomainHash("strogo.validation-proof.v0.1/source-map", sourceMapBytes);

    public static string EvidenceDigest(string kind, ReadOnlySpan<byte> evidenceBytes)
    {
        if (!ObligationKinds.Contains(kind)) Reject("ObligationKindInvalid", "$/obligations/kind");
        return PortabilityContract.DomainHash($"strogo.validation-proof.v0.1/evidence/{kind}", evidenceBytes);
    }

    internal static void Validate(
        ReadOnlySpan<byte> proofBytes,
        string expectedProofDigest,
        string expectedModuleDigest,
        string expectedBundleDigest,
        string expectedProofSourcesDigest,
        string expectedSourceMapDigest,
        ReadOnlySpan<byte> toolchainInventoryBytes,
        ReadOnlySpan<byte> closureInventoryBytes,
        ReadOnlySpan<byte> transcriptBytes)
    {
        if (Digest(proofBytes) != expectedProofDigest) Reject("ProofDigestMismatch", "$/proofDigest");
        using var document = CanonicalJson.ParseStrict(Encoding.UTF8.GetString(proofBytes));
        if (!CanonicalJson.Encode(document.RootElement).SequenceEqual(proofBytes.ToArray())) Reject("ProofMetadataInvalid", "content/proof.json");
        var root = document.RootElement;
        Fields(root, "schemaVersion", "contractStatus", "moduleDigest", "bundleDigest", "toolchainDigest", "closureDigest", "proofSourcesDigest", "transcriptDigest", "sourceMapDigest", "outcome", "obligations");
        if (String(root, "schemaVersion") != SchemaVersion) Reject("ProofSchemaMismatch", "$/proofDigest");
        if (String(root, "contractStatus") != PortabilityPackageVersions.ContractStatus) Reject("ProofContractStatusMismatch", "$/proofDigest");
        if (DigestField(root, "moduleDigest") != expectedModuleDigest) Reject("ProofModuleMismatch", "$/proofDigest");
        if (DigestField(root, "bundleDigest") != expectedBundleDigest) Reject("ProofBundleMismatch", "$/proofDigest");
        ValidateInventory(toolchainInventoryBytes, "content/proof-toolchain.json");
        ValidateInventory(closureInventoryBytes, "content/proof-closure.json");
        ValidateCanonicalJson(transcriptBytes, "content/proof-transcript.json");
        if (DigestField(root, "toolchainDigest") != ToolchainDigest(toolchainInventoryBytes)) Reject("ProofToolchainMismatch", "$/proofDigest");
        if (DigestField(root, "closureDigest") != ClosureDigest(closureInventoryBytes)) Reject("ProofClosureMismatch", "$/proofDigest");
        if (DigestField(root, "proofSourcesDigest") != expectedProofSourcesDigest) Reject("ProofSourcesMismatch", "$/proofDigest");
        if (DigestField(root, "transcriptDigest") != TranscriptDigest(transcriptBytes)) Reject("ProofTranscriptMismatch", "$/proofDigest");
        if (DigestField(root, "sourceMapDigest") != expectedSourceMapDigest) Reject("ProofSourceMapMismatch", "$/proofDigest");
        if (String(root, "outcome") != "Verified") Reject("ProofOutcomeInvalid", "$/proofDigest");

        if (!root.TryGetProperty("obligations", out var obligationsElement) || obligationsElement.ValueKind != JsonValueKind.Array)
            Reject("ProofObligationsInvalid", "$/proofDigest");
        var obligations = obligationsElement.EnumerateArray().Select(item =>
        {
            Fields(item, "obligationId", "kind", "status", "evidenceDigest");
            return new PortabilityValidationProofObligation(
                String(item, "obligationId"),
                String(item, "kind"),
                String(item, "status"),
                DigestField(item, "evidenceDigest"));
        }).ToArray();
        _ = NormalizeObligations(obligations, requireInputOrder: true);
    }

    private static void ValidateInventory(ReadOnlySpan<byte> inventoryBytes, string locus)
    {
        using var document = ValidateCanonicalJson(inventoryBytes, locus);
        var root = document.RootElement;
        Fields(root, "files", "versions");
        if (root.GetProperty("files").ValueKind != JsonValueKind.Array || root.GetProperty("versions").ValueKind != JsonValueKind.Array) Reject("ProofInventoryInvalid", locus);
        var previousPath = "";
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var foldedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in root.GetProperty("files").EnumerateArray())
        {
            Fields(file, "path", "sha256", "length", "role");
            var path = String(file, "path");
            ValidateInventoryPath(path, locus);
            if ((previousPath.Length > 0 && string.CompareOrdinal(previousPath, path) >= 0) || !paths.Add(path) || !foldedPaths.Add(path.ToUpperInvariant())) Reject("ProofInventoryInvalid", locus);
            previousPath = path;
            _ = DigestField(file, "sha256");
            var lengthText = String(file, "length");
            if (!DecimalPattern.IsMatch(lengthText) || !long.TryParse(lengthText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)) Reject("ProofInventoryInvalid", locus);
            if (!InventoryRoles.Contains(String(file, "role"))) Reject("ProofInventoryInvalid", locus);
        }
        var previousId = "";
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var version in root.GetProperty("versions").EnumerateArray())
        {
            Fields(version, "id", "value");
            var id = String(version, "id");
            if (!SegmentPattern.IsMatch(id) || (previousId.Length > 0 && string.CompareOrdinal(previousId, id) >= 0) || !ids.Add(id) || String(version, "value").Length == 0) Reject("ProofInventoryInvalid", locus);
            previousId = id;
        }
    }

    private static JsonDocument ValidateCanonicalJson(ReadOnlySpan<byte> bytes, string locus)
    {
        var document = CanonicalJson.ParseStrict(Encoding.UTF8.GetString(bytes));
        if (!CanonicalJson.Encode(document.RootElement).SequenceEqual(bytes.ToArray()))
        {
            document.Dispose();
            Reject("ProofArtifactNonCanonical", locus);
        }
        return document;
    }

    private static void ValidateInventoryPath(string path, string locus)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || path.Contains(':') || path.StartsWith('/')) Reject("ProofInventoryInvalid", locus);
        var segments = path.Split('/');
        if (segments.Any(segment => segment is "." or ".." || !SegmentPattern.IsMatch(segment))) Reject("ProofInventoryInvalid", locus);
    }

    private static ImmutableArray<PortabilityValidationProofObligation> NormalizeObligations(
        IReadOnlyList<PortabilityValidationProofObligation>? obligations,
        bool requireInputOrder = false)
    {
        if (obligations is null || obligations.Count == 0) Reject("ProofObligationsInvalid", "$/obligations");
        var nonNullObligations = obligations!;
        var normalized = nonNullObligations.OrderBy(item => item.ObligationId, StringComparer.Ordinal).ToImmutableArray();
        if (requireInputOrder && !nonNullObligations.SequenceEqual(normalized)) Reject("ProofObligationOrderInvalid", "$/obligations");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in normalized)
        {
            if (string.IsNullOrWhiteSpace(item.ObligationId) || item.ObligationId.Any(char.IsControl) || !ids.Add(item.ObligationId)) Reject("ProofObligationIdInvalid", "$/obligations");
            if (!ObligationKinds.Contains(item.Kind)) Reject("ProofObligationKindInvalid", "$/obligations");
            if (item.Status != "Verified") Reject("ProofObligationStatusInvalid", "$/obligations");
            RequireDigest(item.EvidenceDigest, "$/obligations/evidenceDigest");
        }
        return normalized;
    }

    private static void Fields(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) Reject("ExpectedObject", "$json");
        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != names.Length || actual.Distinct(StringComparer.Ordinal).Count() != actual.Length || actual.Any(name => !names.Contains(name, StringComparer.Ordinal))) Reject("UnexpectedFields", "$json");
    }

    private static string String(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) Reject("ExpectedString", "$" + property);
        return value.GetString()!;
    }

    private static string DigestField(JsonElement element, string property)
    {
        var value = String(element, property);
        RequireDigest(value, "$" + property);
        return value;
    }

    private static void RequireDigest(string digest, string locus)
    {
        if (!DigestPattern.IsMatch(digest)) Reject("InvalidDigest", locus);
    }

    private static void Reject(string reason, string locus)
        => throw new PortabilityContractException("PortabilityPackageRejected", locus, new { reason });
}
