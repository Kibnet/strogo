using System.Text.Json;

namespace Strogo.Modules;

/// <summary>Checks held package identities. This receipt is not a proof replay or release admission.</summary>
public sealed record G02StoredIdentity(G02BuildManifest Manifest, VerifiedContractApproval ContractApproval);

public static class G02StoredIdentityVerifier
{
    public static G02StoredIdentity Verify(G02PackageSnapshot snapshot, OwnerTrust trust,
        ReadOnlySpan<byte> currentStateBytes, string expectedBundleDigest, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(trust);
        var manifest = snapshot.Manifest;
        var moduleBytes = ReadRole(snapshot, "module");
        var bundleBytes = ReadRole(snapshot, "bundle");
        var approvalBytes = ReadRole(snapshot, "contract-approval");
        var proofBytes = ReadRole(snapshot, "proof");
        // Check current owner state and approval before the remaining digest chain.
        var approval = trust.VerifyContractApproval(approvalBytes, currentStateBytes, expectedBundleDigest, now);
        if (manifest.BundleDigest != expectedBundleDigest)
            throw Refuse("PackageBundleDigestMismatch");
        if (approval.ArtifactDigest != manifest.ContractApprovalDigest)
            throw Refuse("PackageContractApprovalDigestMismatch");
        try
        {
            var module = ModulesParser.ParseModule(moduleBytes);
            if (!module.CanonicalSourceBytes.SequenceEqual(moduleBytes) || module.SourceDigest != manifest.ModuleDigest)
                throw Refuse("PackageModuleDigestMismatch");
        }
        catch (ModuleException error) when (error.Stage != "package")
        {
            throw Refuse("PackageModuleInvalid");
        }
        try
        {
            var bundle = OwnerBundleV04Parser.Parse(bundleBytes);
            if (!bundle.CanonicalBytes.AsSpan().SequenceEqual(bundleBytes) || bundle.BundleDigest != manifest.BundleDigest)
                throw Refuse("PackageBundleDigestMismatch");
        }
        catch (ModuleException error) when (error.Stage != "package")
        {
            throw Refuse("PackageBundleInvalid");
        }

        try { CheckProofMetadata(proofBytes, manifest); }
        catch (ModuleException error) when (error.Stage != "package")
        {
            throw Refuse("PackageProofMetadataInvalid");
        }
        return new(manifest, approval);
    }

    private static byte[] ReadRole(G02PackageSnapshot snapshot, string role)
        => snapshot.ReadHeld(snapshot.Manifest.Files.Single(file => file.Role == role).Path);

    private static void CheckProofMetadata(byte[] bytes, G02BuildManifest manifest)
    {
        using var document = OwnerAdmissionWire.Parse(bytes);
        var root = document.RootElement;
        OwnerAdmissionWire.Fields(root, "schemaVersion", "moduleDigest", "bundleDigest", "contractApprovalDigest",
            "toolchainDigest", "closureDigest", "proofSourcesDigest", "transcriptDigest", "sourceMapDigest",
            "outcome", "obligations");
        OwnerAdmissionWire.Equal(root, "schemaVersion", "strogo.proof.v0.2");
        if (OwnerAdmissionWire.Digest(root, "moduleDigest") != manifest.ModuleDigest ||
            OwnerAdmissionWire.Digest(root, "bundleDigest") != manifest.BundleDigest ||
            OwnerAdmissionWire.Digest(root, "contractApprovalDigest") != manifest.ContractApprovalDigest ||
            OwnerAdmissionWire.Digest(root, "toolchainDigest") != manifest.ToolchainDigest ||
            OwnerAdmissionWire.Digest(root, "closureDigest") != manifest.ClosureDigest)
            throw Refuse("PackageProofIdentityMismatch");
        OwnerAdmissionWire.Digest(root, "proofSourcesDigest");
        OwnerAdmissionWire.Digest(root, "transcriptDigest");
        OwnerAdmissionWire.Digest(root, "sourceMapDigest");
        if (OwnerAdmissionWire.NonEmpty(root, "outcome") != "Verified")
            throw Refuse("PackageProofNotVerified");
        var obligations = root.GetProperty("obligations");
        if (obligations.ValueKind != JsonValueKind.Array || obligations.GetArrayLength() > 4096)
            throw Refuse("PackageProofObligationsInvalid");
        string? previous = null;
        foreach (var item in obligations.EnumerateArray())
        {
            OwnerAdmissionWire.Fields(item, "obligationId", "kind", "status", "evidenceDigest");
            var id = OwnerAdmissionWire.Id(item, "obligationId");
            OwnerAdmissionWire.Id(item, "kind");
            OwnerAdmissionWire.Digest(item, "evidenceDigest");
            if (previous is not null && string.CompareOrdinal(previous, id) >= 0)
                throw Refuse("PackageProofObligationsInvalid");
            if (OwnerAdmissionWire.NonEmpty(item, "status") != "Verified")
                throw Refuse("PackageProofNotVerified");
            previous = id;
        }
        if (OwnerAdmissionWire.Hash("strogo.proof.v0.2/artifact", bytes) != manifest.ProofDigest)
            throw Refuse("PackageProofDigestMismatch");
    }

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("package", code);
}
