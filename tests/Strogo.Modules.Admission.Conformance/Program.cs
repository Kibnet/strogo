using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Strogo.Modules;

using var owner = RSA.Create(2048);
using var impostor = RSA.Create(2048);
var publicKey = owner.ExportSubjectPublicKeyInfo();
var keyId = Hex(SHA256.HashData(publicKey));
using var trust = new OwnerTrust(publicKey, keyId);
var now = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
var bundleDigest = new string('a', 64);
var checks = 0;

string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
byte[] Encode(SortedDictionary<string, object> value) => JsonSerializer.SerializeToUtf8Bytes(value);
SortedDictionary<string, object> Fields(params (string Name, object Value)[] pairs)
    => new(pairs.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal), StringComparer.Ordinal);
byte[] Hash(string domain, byte[] bytes) => SHA256.HashData(Encoding.UTF8.GetBytes(domain + "\n").Concat(bytes).ToArray());
string Timestamp(int hour) => now.AddHours(hour).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

byte[] Sign(SortedDictionary<string, object> fields, string schema, RSA signer)
{
    var digest = Hash(schema + "/payload", Encode(fields));
    var message = Encoding.UTF8.GetBytes(schema + "/signature\n").Concat(digest).ToArray();
    fields.Add("signature", Base64Url(signer.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)));
    return Encode(fields);
}

byte[] State(long epoch, RSA? signer = null, int maxLifetime = 7200, string? policyOverride = null)
{
    var policy = Fields(("schemaVersion", "strogo.owner-state.v0.2"), ("keyId", keyId),
        ("supportedAlgorithms", new[] { "rsa-pss-sha256" }),
        ("maxContractLifetimeSeconds", maxLifetime), ("maxAdmissionLifetimeSeconds", 7200));
    var state = Fields(("schemaVersion", "strogo.owner-state.v0.2"), ("keyId", keyId),
        ("approvalEpoch", epoch), ("supportedAlgorithms", new[] { "rsa-pss-sha256" }),
        ("maxContractLifetimeSeconds", maxLifetime), ("maxAdmissionLifetimeSeconds", 7200),
        ("issuedAt", Timestamp(-1)), ("policyDigest", policyOverride ?? Hex(Hash("strogo.owner-policy.v0.2/payload", Encode(policy)))),
        ("signatureAlgorithm", "rsa-pss-sha256"));
    return Sign(state, "strogo.owner-state.v0.2", signer ?? owner);
}

byte[] Approval(byte[] state, long epoch, int validHours = 1, RSA? signer = null)
{
    using var document = JsonDocument.Parse(state);
    var policyDigest = document.RootElement.GetProperty("policyDigest").GetString()!;
    var approval = Fields(("schemaVersion", "strogo.contract-approval.v0.2"), ("approvalId", "review.1"),
        ("approvedBy", "owner"), ("issuedAt", Timestamp(0)), ("validUntil", Timestamp(validHours)),
        ("bundleDigest", bundleDigest), ("policyDigest", policyDigest), ("approvalEpoch", epoch),
        ("decisionProvenance", Fields(("kind", "spec"), ("reference", "specs/g02.md"),
            ("digest", new string('b', 64)))), ("signatureAlgorithm", "rsa-pss-sha256"), ("keyId", keyId));
    return Sign(approval, "strogo.contract-approval.v0.2", signer ?? owner);
}

byte[] Admission(byte[] state, byte[] approval, long epoch, RSA? signer = null)
{
    using var document = JsonDocument.Parse(state);
    var policyDigest = document.RootElement.GetProperty("policyDigest").GetString()!;
    var release = Fields(("schemaVersion", "strogo.admission.v0.2"), ("admissionId", "release.1"),
        ("approvedBy", "owner"), ("issuedAt", Timestamp(0)), ("validUntil", Timestamp(1)),
        ("contractApprovalDigest", Hex(Hash("strogo.contract-approval.v0.2/artifact", approval))),
        ("proofDigest", new string('1', 64)), ("buildManifestDigest", new string('2', 64)),
        ("toolchainDigest", new string('3', 64)), ("policyDigest", policyDigest),
        ("approvalEpoch", epoch), ("packageDigest", new string('4', 64)),
        ("signatureAlgorithm", "rsa-pss-sha256"), ("keyId", keyId));
    return Sign(release, "strogo.admission.v0.2", signer ?? owner);
}

void Check(bool condition, string label)
{
    if (!condition) throw new Exception("Failed: " + label);
    checks++;
}

void Refuse(Action action, string code)
{
    try { action(); }
    catch (ModuleException error) when (error.Stage == "admission" && error.Code == code)
    {
        checks++;
        return;
    }
    throw new Exception("Expected admission refusal: " + code);
}

var state0 = State(0);
var approval0 = Approval(state0, 0);
var verified = trust.VerifyContractApproval(approval0, state0, bundleDigest, now);
Check(verified.BundleDigest == bundleDigest && verified.ApprovalEpoch == 0, "approved bundle identity");
var admission0 = Admission(state0, approval0, 0);
VerifiedReleaseAdmission VerifyRelease(byte[] bytes, byte[] state, VerifiedContractApproval approval,
    string packageDigest = "4444444444444444444444444444444444444444444444444444444444444444")
    => trust.VerifyReleaseAdmission(bytes, state, approval, new string('1', 64), new string('2', 64),
        new string('3', 64), packageDigest, now);
Check(VerifyRelease(admission0, state0, verified).PackageDigest == new string('4', 64), "signed release identity");
using (var otherTrust = new OwnerTrust(publicKey, keyId))
    Refuse(() => otherTrust.VerifyReleaseAdmission(admission0, state0, verified, new string('1', 64),
        new string('2', 64), new string('3', 64), new string('4', 64), now), "ContractApprovalTrustMismatch");
Refuse(() => VerifyRelease(Admission(state0, approval0, 0, impostor), state0, verified), "InvalidOwnerSignature");
Refuse(() => VerifyRelease(admission0, state0, verified, new string('5', 64)), "ReleaseAdmissionDigestMismatch");
var otherApproval = Approval(state0, 0);
var otherVerified = trust.VerifyContractApproval(otherApproval, state0, bundleDigest, now);
Refuse(() => VerifyRelease(admission0, state0, otherVerified), "ReleaseAdmissionDigestMismatch");
Check(trust.VerifyOwnerState(state0).ApprovalEpoch == 0, "signed state identity");
Refuse(() => new OwnerTrust(publicKey, new string('0', 64)), "PinnedKeyMismatch");
Refuse(() => trust.VerifyOwnerState(State(0, impostor)), "InvalidOwnerSignature");
Refuse(() => trust.VerifyOwnerState(State(0, maxLifetime: 3600)), "OwnerStateRollbackDetected");
using var stateDocument = JsonDocument.Parse(state0);
var originalPolicyDigest = stateDocument.RootElement.GetProperty("policyDigest").GetString()!;
var wrongPolicy = Encoding.UTF8.GetString(state0).Replace(originalPolicyDigest, new string('f', 64), StringComparison.Ordinal);
Refuse(() => trust.VerifyOwnerState(Encoding.UTF8.GetBytes(wrongPolicy)), "InvalidOwnerSignature");
Refuse(() => trust.VerifyOwnerState(State(0, policyOverride: new string('f', 64))), "PolicyDigestMismatch");
Refuse(() => trust.VerifyContractApproval(Approval(state0, 0, signer: impostor), state0, bundleDigest, now), "InvalidOwnerSignature");
Refuse(() => trust.VerifyContractApproval(approval0, state0, new string('c', 64), now), "ContractApprovalDigestMismatch");
Refuse(() => trust.VerifyContractApproval(approval0, state0, bundleDigest, now.AddHours(2)), "ContractApprovalExpired");
Refuse(() => trust.VerifyContractApproval(Approval(state0, 0, validHours: 3), state0, bundleDigest, now), "ContractApprovalExpired");

var altered = Encoding.UTF8.GetString(approval0).Replace("\"approvedBy\":\"owner\"", "\"approvedBy\":\"agent\"", StringComparison.Ordinal);
Refuse(() => trust.VerifyContractApproval(Encoding.UTF8.GetBytes(altered), state0, bundleDigest, now), "InvalidOwnerSignature");
Refuse(() => trust.VerifyContractApproval(Encoding.UTF8.GetBytes(" " + Encoding.UTF8.GetString(approval0)), state0, bundleDigest, now), "ArtifactNotCanonical");
var duplicate = Encoding.UTF8.GetString(approval0).Replace("\"approvalId\":\"review.1\"", "\"approvalId\":\"review.1\",\"approvalId\":\"review.1\"", StringComparison.Ordinal);
Refuse(() => trust.VerifyContractApproval(Encoding.UTF8.GetBytes(duplicate), state0, bundleDigest, now), "DuplicateField");
var extra = JsonSerializer.Deserialize<SortedDictionary<string, JsonElement>>(approval0)!;
extra.Add("candidateDigest", JsonSerializer.SerializeToElement(bundleDigest));
Refuse(() => trust.VerifyContractApproval(JsonSerializer.SerializeToUtf8Bytes(extra), state0, bundleDigest, now), "ArtifactFieldsInvalid");

var state1 = State(1);
Check(trust.VerifyOwnerState(state1).ApprovalEpoch == 1, "epoch advance");
Refuse(() => trust.VerifyOwnerState(state0), "OwnerStateRollbackDetected");
Refuse(() => trust.VerifyContractApproval(approval0, state1, bundleDigest, now), "ApprovalEpochMismatch");
Refuse(() => VerifyRelease(admission0, state1, verified), "AdmissionEpochMismatch");
Console.WriteLine($"PASS owner admission trust checks={checks}");
