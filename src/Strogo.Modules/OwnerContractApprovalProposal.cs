using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Strogo.Modules;

public sealed record OwnerDecisionProvenance(string Kind, string Reference, string Digest);

/// <summary>Immutable projection prepared before the operator unlocks the signing key.</summary>
public sealed class OwnerContractApprovalProposal
{
    private readonly OwnerTrust trust;
    private readonly byte[] payload;
    private readonly string stateDigest;
    private readonly string bundleDigest;

    private OwnerContractApprovalProposal(OwnerTrust trust, byte[] payload, string stateDigest,
        string bundleDigest, string projection)
    {
        this.trust = trust;
        this.payload = payload;
        this.stateDigest = stateDigest;
        this.bundleDigest = bundleDigest;
        Projection = projection;
        PayloadDigest = OwnerAdmissionWire.Hash("strogo.contract-approval.v0.2/payload", payload);
    }

    public string PayloadDigest { get; }
    public string Projection { get; }

    public static OwnerContractApprovalProposal Prepare(OwnerTrust trust, ReadOnlySpan<byte> ownerStateBytes,
        ReadOnlySpan<byte> ownerBundleBytes, string approvalId, string approvedBy,
        OwnerDecisionProvenance provenance, DateTimeOffset issuedAt, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(provenance);
        var state = trust.VerifyOwnerState(ownerStateBytes);
        OwnerAdmissionWire.RequireId(approvalId);
        OwnerAdmissionWire.RequireId(approvedBy);
        OwnerAdmissionWire.RequireId(provenance.Kind);
        if (string.IsNullOrWhiteSpace(provenance.Reference) || !OwnerAdmissionWire.IsDigest(provenance.Digest))
            throw Refuse("DecisionProvenanceInvalid");
        if (issuedAt.Offset != TimeSpan.Zero || validUntil.Offset != TimeSpan.Zero ||
            issuedAt.Millisecond * TimeSpan.TicksPerMillisecond != issuedAt.Ticks % TimeSpan.TicksPerSecond ||
            validUntil.Millisecond * TimeSpan.TicksPerMillisecond != validUntil.Ticks % TimeSpan.TicksPerSecond)
            throw Refuse("InvalidTimestamp");
        if (validUntil <= issuedAt || (validUntil - issuedAt).TotalSeconds > state.MaxContractLifetimeSeconds)
            throw Refuse("ContractApprovalExpired");

        var bundle = OwnerBundleV04Parser.Parse(ownerBundleBytes.ToArray());
        var canonicalBundle = bundle.CanonicalBytes;
        var timestamp = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
        var source = new
        {
            schemaVersion = "strogo.contract-approval.v0.2",
            approvalId,
            approvedBy,
            issuedAt = issuedAt.ToString(timestamp, CultureInfo.InvariantCulture),
            validUntil = validUntil.ToString(timestamp, CultureInfo.InvariantCulture),
            bundleDigest = bundle.BundleDigest,
            policyDigest = state.PolicyDigest,
            approvalEpoch = state.ApprovalEpoch,
            decisionProvenance = new
            {
                kind = provenance.Kind,
                reference = provenance.Reference,
                digest = provenance.Digest
            },
            signatureAlgorithm = "rsa-pss-sha256",
            keyId = trust.KeyId
        };
        var payload = OwnerAdmissionWire.Canonical(JsonSerializer.SerializeToElement(source));
        var projection = "ownerBundleCanonical=" + Encoding.UTF8.GetString(canonicalBundle) + "\n" +
            "contractApprovalPayload=" + Encoding.UTF8.GetString(payload);
        return new(trust, payload, state.ArtifactDigest, bundle.BundleDigest, projection);
    }

    public byte[] Sign(RSA signer, string enteredFullPayloadDigest, ReadOnlySpan<byte> currentOwnerStateBytes,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(signer);
        if (enteredFullPayloadDigest != PayloadDigest)
            throw Refuse("OwnerConfirmationMismatch");
        if (Convert.ToHexStringLower(SHA256.HashData(signer.ExportSubjectPublicKeyInfo())) != trust.KeyId)
            throw Refuse("SignerKeyMismatch");
        var current = trust.VerifyOwnerState(currentOwnerStateBytes);
        if (current.ArtifactDigest != stateDigest)
            throw Refuse("OwnerStateChanged");
        using var payloadDocument = OwnerAdmissionWire.Parse(payload);
        var root = payloadDocument.RootElement;
        var issuedAt = OwnerAdmissionWire.Timestamp(root, "issuedAt");
        var validUntil = OwnerAdmissionWire.Timestamp(root, "validUntil");
        if (issuedAt > now || validUntil < now)
            throw Refuse("ContractApprovalExpired");

        var signatureTag = Encoding.UTF8.GetBytes("strogo.contract-approval.v0.2/signature\n");
        var payloadHash = OwnerAdmissionWire.HashBytes("strogo.contract-approval.v0.2/payload", payload);
        var message = signatureTag.Concat(payloadHash).ToArray();
        var signature = signer.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var encoded = Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var artifact = OwnerAdmissionWire.CanonicalWithString(root, "signature", encoded);
        trust.VerifyContractApproval(artifact, currentOwnerStateBytes, bundleDigest, now);
        return artifact;
    }

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("admission", code);
}
