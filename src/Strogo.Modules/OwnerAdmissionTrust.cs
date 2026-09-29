using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Strogo.Modules;

public sealed class VerifiedOwnerState
{
    internal VerifiedOwnerState(long approvalEpoch, string policyDigest, string artifactDigest,
        long maxContractLifetimeSeconds, long maxAdmissionLifetimeSeconds)
    {
        ApprovalEpoch = approvalEpoch;
        PolicyDigest = policyDigest;
        ArtifactDigest = artifactDigest;
        MaxContractLifetimeSeconds = maxContractLifetimeSeconds;
        MaxAdmissionLifetimeSeconds = maxAdmissionLifetimeSeconds;
    }

    public long ApprovalEpoch { get; }
    public string PolicyDigest { get; }
    public string ArtifactDigest { get; }
    public long MaxContractLifetimeSeconds { get; }
    public long MaxAdmissionLifetimeSeconds { get; }
}

public sealed class VerifiedContractApproval
{
    internal VerifiedContractApproval(string approvalId, string bundleDigest, string artifactDigest,
        long approvalEpoch, DateTimeOffset validUntil, OwnerTrust verifier)
    {
        ApprovalId = approvalId;
        BundleDigest = bundleDigest;
        ArtifactDigest = artifactDigest;
        ApprovalEpoch = approvalEpoch;
        ValidUntil = validUntil;
        Verifier = verifier;
    }

    public string ApprovalId { get; }
    public string BundleDigest { get; }
    public string ArtifactDigest { get; }
    public long ApprovalEpoch { get; }
    public DateTimeOffset ValidUntil { get; }
    internal OwnerTrust Verifier { get; }
}

public sealed class VerifiedReleaseAdmission
{
    internal VerifiedReleaseAdmission(string admissionId, string packageDigest, string artifactDigest,
        long approvalEpoch, DateTimeOffset validUntil)
    {
        AdmissionId = admissionId;
        PackageDigest = packageDigest;
        ArtifactDigest = artifactDigest;
        ApprovalEpoch = approvalEpoch;
        ValidUntil = validUntil;
    }

    public string AdmissionId { get; }
    public string PackageDigest { get; }
    public string ArtifactDigest { get; }
    public long ApprovalEpoch { get; }
    public DateTimeOffset ValidUntil { get; }
}

/// <summary>Host-owned trust root. No package or invocation field may construct this value.</summary>
public sealed class OwnerTrust : IDisposable
{
    private readonly RSA key;
    private readonly object stateGate = new();
    private long highestEpoch = -1;
    private string? highestStateDigest;

    public string KeyId { get; }

    public OwnerTrust(ReadOnlySpan<byte> pinnedSubjectPublicKeyInfo, string expectedKeyId)
    {
        key = RSA.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(pinnedSubjectPublicKeyInfo, out var consumed);
            if (consumed != pinnedSubjectPublicKeyInfo.Length)
                throw Refuse("InvalidPublicKey");
            KeyId = Convert.ToHexStringLower(SHA256.HashData(pinnedSubjectPublicKeyInfo));
            if (!OwnerAdmissionWire.IsDigest(expectedKeyId) || KeyId != expectedKeyId)
                throw Refuse("PinnedKeyMismatch");
        }
        catch (CryptographicException)
        {
            key.Dispose();
            throw Refuse("InvalidPublicKey");
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    public VerifiedOwnerState VerifyOwnerState(ReadOnlySpan<byte> stateBytes)
    {
        using var state = OwnerAdmissionWire.Parse(stateBytes);
        var root = state.RootElement;
        OwnerAdmissionWire.Fields(root, "schemaVersion", "keyId", "approvalEpoch", "supportedAlgorithms",
            "maxContractLifetimeSeconds", "maxAdmissionLifetimeSeconds", "issuedAt", "policyDigest",
            "signatureAlgorithm", "signature");
        OwnerAdmissionWire.Equal(root, "schemaVersion", "strogo.owner-state.v0.2");
        OwnerAdmissionWire.Equal(root, "keyId", KeyId);
        OwnerAdmissionWire.Equal(root, "signatureAlgorithm", "rsa-pss-sha256");
        OwnerAdmissionWire.Algorithms(root.GetProperty("supportedAlgorithms"));
        var epoch = OwnerAdmissionWire.NonNegative(root, "approvalEpoch");
        var contractLifetime = OwnerAdmissionWire.NonNegative(root, "maxContractLifetimeSeconds");
        var admissionLifetime = OwnerAdmissionWire.NonNegative(root, "maxAdmissionLifetimeSeconds");
        OwnerAdmissionWire.Timestamp(root, "issuedAt");
        var policyDigest = OwnerAdmissionWire.Digest(root, "policyDigest");
        VerifySignature(root, "strogo.owner-state.v0.2");
        var policy = OwnerAdmissionWire.CanonicalSubset(root, "schemaVersion", "keyId", "supportedAlgorithms",
            "maxContractLifetimeSeconds", "maxAdmissionLifetimeSeconds");
        if (policyDigest != OwnerAdmissionWire.Hash("strogo.owner-policy.v0.2/payload", policy))
            throw Refuse("PolicyDigestMismatch");
        var artifactDigest = OwnerAdmissionWire.Hash("strogo.owner-state.v0.2/artifact", stateBytes);
        lock (stateGate)
        {
            if (epoch < highestEpoch || (epoch == highestEpoch && highestStateDigest != artifactDigest))
                throw Refuse("OwnerStateRollbackDetected");
            highestEpoch = epoch;
            highestStateDigest = artifactDigest;
        }
        return new(epoch, policyDigest, artifactDigest, contractLifetime, admissionLifetime);
    }

    public VerifiedContractApproval VerifyContractApproval(ReadOnlySpan<byte> approvalBytes,
        ReadOnlySpan<byte> currentStateBytes, string expectedBundleDigest, DateTimeOffset now)
    {
        using var approval = OwnerAdmissionWire.Parse(approvalBytes);
        var root = approval.RootElement;
        OwnerAdmissionWire.Fields(root, "schemaVersion", "approvalId", "approvedBy", "issuedAt", "validUntil",
            "bundleDigest", "policyDigest", "approvalEpoch", "decisionProvenance", "signatureAlgorithm",
            "keyId", "signature");
        OwnerAdmissionWire.Equal(root, "schemaVersion", "strogo.contract-approval.v0.2");
        var approvalId = OwnerAdmissionWire.Id(root, "approvalId");
        OwnerAdmissionWire.Id(root, "approvedBy");
        OwnerAdmissionWire.Equal(root, "keyId", KeyId);
        OwnerAdmissionWire.Equal(root, "signatureAlgorithm", "rsa-pss-sha256");
        var provenance = root.GetProperty("decisionProvenance");
        OwnerAdmissionWire.Fields(provenance, "kind", "reference", "digest");
        OwnerAdmissionWire.Id(provenance, "kind");
        OwnerAdmissionWire.NonEmpty(provenance, "reference");
        OwnerAdmissionWire.Digest(provenance, "digest");
        var bundleDigest = OwnerAdmissionWire.Digest(root, "bundleDigest");
        var policyDigest = OwnerAdmissionWire.Digest(root, "policyDigest");
        var epoch = OwnerAdmissionWire.NonNegative(root, "approvalEpoch");
        var issuedAt = OwnerAdmissionWire.Timestamp(root, "issuedAt");
        var validUntil = OwnerAdmissionWire.Timestamp(root, "validUntil");
        var current = VerifyOwnerState(currentStateBytes);
        VerifySignature(root, "strogo.contract-approval.v0.2");
        if (validUntil <= issuedAt || validUntil < now || issuedAt > now ||
            (validUntil - issuedAt).TotalSeconds > current.MaxContractLifetimeSeconds)
            throw Refuse("ContractApprovalExpired");
        if (epoch != current.ApprovalEpoch)
            throw Refuse("ApprovalEpochMismatch");
        if (policyDigest != current.PolicyDigest || bundleDigest != expectedBundleDigest ||
            !OwnerAdmissionWire.IsDigest(expectedBundleDigest))
            throw Refuse("ContractApprovalDigestMismatch");
        return new(approvalId, bundleDigest,
            OwnerAdmissionWire.Hash("strogo.contract-approval.v0.2/artifact", approvalBytes), epoch, validUntil, this);
    }

    public VerifiedReleaseAdmission VerifyReleaseAdmission(ReadOnlySpan<byte> admissionBytes,
        ReadOnlySpan<byte> currentStateBytes, VerifiedContractApproval contractApproval,
        string expectedProofDigest, string expectedBuildManifestDigest, string expectedToolchainDigest,
        string expectedPackageDigest, DateTimeOffset now)
    {
        if (!ReferenceEquals(contractApproval.Verifier, this))
            throw Refuse("ContractApprovalTrustMismatch");
        using var admission = OwnerAdmissionWire.Parse(admissionBytes);
        var root = admission.RootElement;
        OwnerAdmissionWire.Fields(root, "schemaVersion", "admissionId", "approvedBy", "issuedAt", "validUntil",
            "contractApprovalDigest", "proofDigest", "buildManifestDigest", "toolchainDigest", "policyDigest",
            "approvalEpoch", "packageDigest", "signatureAlgorithm", "keyId", "signature");
        OwnerAdmissionWire.Equal(root, "schemaVersion", "strogo.admission.v0.2");
        var admissionId = OwnerAdmissionWire.Id(root, "admissionId");
        OwnerAdmissionWire.Id(root, "approvedBy");
        OwnerAdmissionWire.Equal(root, "keyId", KeyId);
        OwnerAdmissionWire.Equal(root, "signatureAlgorithm", "rsa-pss-sha256");
        var approvalDigest = OwnerAdmissionWire.Digest(root, "contractApprovalDigest");
        var proofDigest = OwnerAdmissionWire.Digest(root, "proofDigest");
        var manifestDigest = OwnerAdmissionWire.Digest(root, "buildManifestDigest");
        var toolchainDigest = OwnerAdmissionWire.Digest(root, "toolchainDigest");
        var packageDigest = OwnerAdmissionWire.Digest(root, "packageDigest");
        var policyDigest = OwnerAdmissionWire.Digest(root, "policyDigest");
        var epoch = OwnerAdmissionWire.NonNegative(root, "approvalEpoch");
        var issuedAt = OwnerAdmissionWire.Timestamp(root, "issuedAt");
        var validUntil = OwnerAdmissionWire.Timestamp(root, "validUntil");
        var current = VerifyOwnerState(currentStateBytes);
        VerifySignature(root, "strogo.admission.v0.2");
        if (validUntil <= issuedAt || validUntil < now || issuedAt > now ||
            (validUntil - issuedAt).TotalSeconds > current.MaxAdmissionLifetimeSeconds ||
            contractApproval.ValidUntil < now)
            throw Refuse("ReleaseAdmissionExpired");
        if (epoch != current.ApprovalEpoch || epoch != contractApproval.ApprovalEpoch)
            throw Refuse("AdmissionEpochMismatch");
        if (policyDigest != current.PolicyDigest || approvalDigest != contractApproval.ArtifactDigest ||
            proofDigest != expectedProofDigest || manifestDigest != expectedBuildManifestDigest ||
            toolchainDigest != expectedToolchainDigest || packageDigest != expectedPackageDigest ||
            !OwnerAdmissionWire.IsDigest(expectedProofDigest) || !OwnerAdmissionWire.IsDigest(expectedBuildManifestDigest) ||
            !OwnerAdmissionWire.IsDigest(expectedToolchainDigest) || !OwnerAdmissionWire.IsDigest(expectedPackageDigest))
            throw Refuse("ReleaseAdmissionDigestMismatch");
        return new(admissionId, packageDigest,
            OwnerAdmissionWire.Hash("strogo.admission.v0.2/artifact", admissionBytes), epoch, validUntil);
    }

    private void VerifySignature(JsonElement root, string schema)
    {
        var encoded = OwnerAdmissionWire.NonEmpty(root, "signature");
        var signature = OwnerAdmissionWire.Base64Url(encoded);
        var payload = OwnerAdmissionWire.CanonicalWithout(root, "signature");
        var digest = OwnerAdmissionWire.HashBytes($"{schema}/payload", payload);
        var tag = Encoding.UTF8.GetBytes($"{schema}/signature\n");
        var message = new byte[tag.Length + digest.Length];
        tag.CopyTo(message, 0);
        digest.CopyTo(message, tag.Length);
        try
        {
            if (!key.VerifyData(message, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                throw Refuse("InvalidOwnerSignature");
        }
        catch (CryptographicException) { throw Refuse("InvalidOwnerSignature"); }
    }

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("admission", code);

    public void Dispose() => key.Dispose();
}

internal static class OwnerAdmissionWire
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const int MaxArtifactBytes = 1_048_576;

    internal static JsonDocument Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxArtifactBytes)
            throw Refuse("ArtifactSizeInvalid");
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(StrictUtf8.GetString(bytes), new JsonDocumentOptions { MaxDepth = 64 });
            Check(document.RootElement);
            if (!Canonical(document.RootElement).AsSpan().SequenceEqual(bytes))
                throw Refuse("ArtifactNotCanonical");
            return document;
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or FormatException or OverflowException)
        {
            document?.Dispose();
            throw Refuse("ArtifactSchemaInvalid");
        }
        catch
        {
            document?.Dispose();
            throw;
        }
    }

    private static void Check(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Refuse("DuplicateField");
                Check(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) Check(child);
        else if (element.ValueKind == JsonValueKind.Number)
        {
            var text = element.GetRawText();
            if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ||
                number.ToString(CultureInfo.InvariantCulture) != text)
                throw Refuse("InvalidInteger");
        }
    }

    internal static byte[] Canonical(JsonElement value) => Write(value, null, null);
    internal static byte[] CanonicalWithout(JsonElement root, string removed) => Write(root, removed, null);
    internal static byte[] CanonicalSubset(JsonElement root, params string[] fields) => Write(root, null, fields);

    private static byte[] Write(JsonElement value, string? removed, string[]? fields)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteValue(writer, value, removed, fields);
        return stream.ToArray();
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonElement value, string? removed = null, string[]? fields = null)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (property.Name == removed || (fields is not null && !fields.Contains(property.Name, StringComparer.Ordinal))) continue;
                    writer.WritePropertyName(property.Name);
                    WriteValue(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var child in value.EnumerateArray()) WriteValue(writer, child);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String: writer.WriteStringValue(value.GetString()); break;
            case JsonValueKind.Number: writer.WriteNumberValue(value.GetInt64()); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw Refuse("ArtifactSchemaInvalid");
        }
    }

    internal static void Fields(JsonElement root, params string[] expected)
    {
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != expected.Length ||
            root.EnumerateObject().Any(p => !expected.Contains(p.Name, StringComparer.Ordinal)))
            throw Refuse("ArtifactFieldsInvalid");
    }

    internal static string NonEmpty(JsonElement root, string field)
    {
        var value = root.GetProperty(field);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
            throw Refuse("ArtifactFieldInvalid");
        return value.GetString()!;
    }

    internal static void Equal(JsonElement root, string field, string expected)
    {
        if (NonEmpty(root, field) != expected) throw Refuse("ArtifactFieldMismatch");
    }

    internal static string Id(JsonElement root, string field)
    {
        var value = NonEmpty(root, field);
        if (value.Length > 64 || !(value[0] is >= 'a' and <= 'z' or >= '0' and <= '9') ||
            value.Skip(1).Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-')))
            throw Refuse("InvalidId");
        return value;
    }

    internal static bool IsDigest(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static string Digest(JsonElement root, string field)
    {
        var value = NonEmpty(root, field);
        if (!IsDigest(value)) throw Refuse("InvalidDigest");
        return value;
    }

    internal static long NonNegative(JsonElement root, string field)
    {
        var value = root.GetProperty(field);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number) || number < 0)
            throw Refuse("InvalidInteger");
        return number;
    }

    internal static DateTimeOffset Timestamp(JsonElement root, string field)
    {
        var value = NonEmpty(root, field);
        if (!DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result) ||
            result.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) != value)
            throw Refuse("InvalidTimestamp");
        return result;
    }

    internal static void Algorithms(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 1 ||
            value[0].ValueKind != JsonValueKind.String || value[0].GetString() != "rsa-pss-sha256")
            throw Refuse("UnsupportedAlgorithm");
    }

    internal static byte[] Base64Url(string value)
    {
        if (value.Contains('=') || value.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
            throw Refuse("InvalidSignatureEncoding");
        try
        {
            var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
            if (Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != value)
                throw Refuse("InvalidSignatureEncoding");
            return bytes;
        }
        catch (FormatException) { throw Refuse("InvalidSignatureEncoding"); }
    }

    internal static byte[] HashBytes(string domain, ReadOnlySpan<byte> bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(domain + "\n"));
        hash.AppendData(bytes);
        return hash.GetHashAndReset();
    }

    internal static string Hash(string domain, ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(HashBytes(domain, bytes));
    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("admission", code);
}
