using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Strogo.Modules;

/// <summary>A single monotonic owner-state transition prepared before the signer is unlocked.</summary>
public sealed class OwnerStateEpochProposal
{
    private readonly OwnerTrust trust;
    private readonly byte[] payload;
    private readonly string previousStateDigest;

    private OwnerStateEpochProposal(OwnerTrust trust, byte[] payload, string previousStateDigest)
    {
        this.trust = trust;
        this.payload = payload;
        this.previousStateDigest = previousStateDigest;
        Projection = "nextOwnerStatePayload=" + Encoding.UTF8.GetString(payload);
        PayloadDigest = OwnerAdmissionWire.Hash("strogo.owner-state.v0.2/payload", payload);
    }

    public string Projection { get; }
    public string PayloadDigest { get; }

    public static OwnerStateEpochProposal Prepare(OwnerTrust trust, ReadOnlySpan<byte> currentStateBytes,
        long expectedEpoch, DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(trust);
        var current = trust.VerifyOwnerState(currentStateBytes);
        if (expectedEpoch < 0 || current.ApprovalEpoch != expectedEpoch)
            throw Refuse("ApprovalEpochMismatch");
        if (expectedEpoch == long.MaxValue)
            throw Refuse("ApprovalEpochOverflow");
        if (issuedAt.Offset != TimeSpan.Zero || issuedAt.Ticks % TimeSpan.TicksPerMillisecond != 0)
            throw Refuse("InvalidTimestamp");
        using var document = OwnerAdmissionWire.Parse(currentStateBytes);
        var root = document.RootElement;
        if (issuedAt < OwnerAdmissionWire.Timestamp(root, "issuedAt"))
            throw Refuse("OwnerClockRollbackDetected");
        var next = new
        {
            schemaVersion = "strogo.owner-state.v0.2",
            keyId = trust.KeyId,
            approvalEpoch = expectedEpoch + 1,
            supportedAlgorithms = new[] { "rsa-pss-sha256" },
            maxContractLifetimeSeconds = current.MaxContractLifetimeSeconds,
            maxAdmissionLifetimeSeconds = current.MaxAdmissionLifetimeSeconds,
            issuedAt = issuedAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            policyDigest = current.PolicyDigest,
            signatureAlgorithm = "rsa-pss-sha256"
        };
        return new(trust, OwnerAdmissionWire.Canonical(JsonSerializer.SerializeToElement(next)), current.ArtifactDigest);
    }

    public byte[] Sign(RSA signer, string enteredFullPayloadDigest, ReadOnlySpan<byte> currentStateBytes)
    {
        ArgumentNullException.ThrowIfNull(signer);
        if (enteredFullPayloadDigest != PayloadDigest)
            throw Refuse("OwnerConfirmationMismatch");
        if (Convert.ToHexStringLower(SHA256.HashData(signer.ExportSubjectPublicKeyInfo())) != trust.KeyId)
            throw Refuse("SignerKeyMismatch");
        var current = trust.VerifyOwnerState(currentStateBytes);
        if (current.ArtifactDigest != previousStateDigest)
            throw Refuse("OwnerStateChanged");
        var digest = OwnerAdmissionWire.HashBytes("strogo.owner-state.v0.2/payload", payload);
        var message = Encoding.UTF8.GetBytes("strogo.owner-state.v0.2/signature\n").Concat(digest).ToArray();
        var signature = signer.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var encoded = Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var payloadDocument = OwnerAdmissionWire.Parse(payload);
        var artifact = OwnerAdmissionWire.CanonicalWithString(payloadDocument.RootElement, "signature", encoded);
        using var independentTrust = new OwnerTrust(signer.ExportSubjectPublicKeyInfo(), trust.KeyId);
        if (independentTrust.VerifyOwnerState(artifact).ApprovalEpoch != current.ApprovalEpoch + 1)
            throw Refuse("OwnerStateTransitionInvalid");
        return artifact;
    }

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("admission", code);
}
