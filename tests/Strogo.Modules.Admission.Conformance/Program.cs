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

void RefusePackage(Action action, string code)
{
    try { action(); }
    catch (ModuleException error) when (error.Stage == "package" && error.Code == code)
    {
        checks++;
        return;
    }
    throw new Exception("Expected package refusal: " + code);
}

var state0 = State(0);
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var bundleBytes = File.ReadAllBytes(Path.Combine(repoRoot, "fixtures", "modules-v0.2", "owner-fold-sum-valid-v0.4.json"));
var parsedBundle = OwnerBundleV04Parser.Parse(bundleBytes);
var provenance = new OwnerDecisionProvenance("spec", "specs/g02.md", new string('b', 64));
var proposal = OwnerContractApprovalProposal.Prepare(trust, state0, bundleBytes, "review.2", "owner",
    provenance, now, now.AddHours(1));
Check(proposal.Projection.Contains(parsedBundle.BundleDigest, StringComparison.Ordinal) &&
    proposal.PayloadDigest.Length == 64, "canonical owner projection and payload identity");
var secondProposal = OwnerContractApprovalProposal.Prepare(trust, state0, bundleBytes, "review.2", "owner",
    provenance, now, now.AddHours(1));
Check(secondProposal.PayloadDigest == proposal.PayloadDigest, "same inputs give same pre-signature identity");
Refuse(() => proposal.Sign(owner, new string('0', 64), state0, now), "OwnerConfirmationMismatch");
Refuse(() => proposal.Sign(impostor, proposal.PayloadDigest, state0, now), "SignerKeyMismatch");
var signedProposal = proposal.Sign(owner, proposal.PayloadDigest, state0, now);
Check(trust.VerifyContractApproval(signedProposal, state0, parsedBundle.BundleDigest, now).BundleDigest ==
    parsedBundle.BundleDigest, "fixture-key approval is independently verified");
using (var issuedDocument = JsonDocument.Parse(signedProposal))
    Check(!issuedDocument.RootElement.TryGetProperty("candidateDigest", out _) &&
        !issuedDocument.RootElement.TryGetProperty("proofDigest", out _), "semantic approval excludes candidate and proof identities");
Refuse(() => OwnerContractApprovalProposal.Prepare(trust, state0, bundleBytes, "Review.2", "owner",
    provenance, now, now.AddHours(1)), "InvalidId");
Refuse(() => OwnerContractApprovalProposal.Prepare(trust, state0, bundleBytes, "review.3", "owner",
    provenance, now, now.AddHours(3)), "ContractApprovalExpired");
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
Refuse(() => proposal.Sign(owner, proposal.PayloadDigest, state1, now), "OwnerStateChanged");
using (var epochTrust = new OwnerTrust(publicKey, keyId))
{
    var epochProposal = OwnerStateEpochProposal.Prepare(epochTrust, state0, 0, now);
    Refuse(() => OwnerStateEpochProposal.Prepare(epochTrust, state0, 1, now), "ApprovalEpochMismatch");
    Refuse(() => epochProposal.Sign(owner, epochProposal.PayloadDigest[..^1] +
        (epochProposal.PayloadDigest[^1] == '0' ? "1" : "0"), state0), "OwnerConfirmationMismatch");
    Refuse(() => epochProposal.Sign(impostor, epochProposal.PayloadDigest, state0), "SignerKeyMismatch");
    var advanced = epochProposal.Sign(owner, epochProposal.PayloadDigest, state0);
    Check(epochTrust.VerifyOwnerState(advanced).ApprovalEpoch == 1, "signed state advances epoch exactly once");
    Refuse(() => epochTrust.VerifyOwnerState(state0), "OwnerStateRollbackDetected");
    Refuse(() => epochTrust.VerifyContractApproval(approval0, advanced, bundleDigest, now), "ApprovalEpochMismatch");
    using var oldDocument = JsonDocument.Parse(state0);
    using var nextDocument = JsonDocument.Parse(advanced);
    Check(oldDocument.RootElement.GetProperty("policyDigest").GetString() ==
          nextDocument.RootElement.GetProperty("policyDigest").GetString(), "epoch advance preserves policy");
}
var manifestFiles = new (string Path, string Role)[]
{
    ("content/approval.json", "contract-approval"),
    ("content/bundle.json", "bundle"),
    ("content/candidate.dfy", "proof-source"),
    ("content/generated.deps.json", "deps"),
    ("content/generated.dll", "entry-assembly"),
    ("content/module.json", "module"),
    ("content/proof.json", "proof"),
    ("content/source-map.json", "source-map"),
    ("content/transcript.json", "proof-transcript")
};
byte[] Manifest((string Path, string Role)[] files, string rid = "win-x64",
    IReadOnlyDictionary<string, byte[]>? content = null, IReadOnlyDictionary<string, string>? identities = null)
{
    string Identity(string field, char fallback) => identities is not null && identities.TryGetValue(field, out var value)
        ? value : new string(fallback, 64);
    var declared = files.Select(file => Fields(("path", file.Path),
        ("sha256", content is null ? new string('a', 64) : Hex(SHA256.HashData(content[file.Path]))),
        ("length", content is null ? 1L : content[file.Path].LongLength), ("role", file.Role))).ToArray();
    var payload = Fields(("schemaVersion", "strogo.build-manifest.v0.2"),
        ("moduleDigest", Identity("moduleDigest", 'a')), ("bundleDigest", Identity("bundleDigest", 'b')),
        ("contractApprovalDigest", Identity("contractApprovalDigest", 'c')), ("proofDigest", Identity("proofDigest", 'd')),
        ("toolchainDigest", Identity("toolchainDigest", 'e')), ("closureDigest", Identity("closureDigest", 'f')),
        ("runtimeIdentifier", rid), ("entryAssemblyPath", "content/generated.dll"), ("files", declared));
    payload.Add("packageDigest", Hex(Hash("strogo.package.v0.2/manifest-payload", Encode(payload))));
    return Encode(payload);
}
var goodManifestBytes = Manifest(manifestFiles);
var goodManifest = G02BuildManifest.Parse(goodManifestBytes);
Check(goodManifest.Files.Length == manifestFiles.Length &&
      goodManifest.EntryAssemblyPath == "content/generated.dll" &&
      goodManifest.ArtifactDigest == Hex(Hash("strogo.build-manifest.v0.2/artifact", goodManifestBytes)),
    "strict G02 manifest records only structural identity");
RefusePackage(() => G02BuildManifest.Parse(Manifest(manifestFiles, "linux-x64")), "ArtifactFieldMismatch");
RefusePackage(() => G02BuildManifest.Parse(Manifest(manifestFiles.Reverse().ToArray())), "ManifestFileOrderInvalid");
RefusePackage(() => G02BuildManifest.Parse(Manifest(manifestFiles.Select(file =>
    file.Path == "content/module.json" ? ("content/con.txt", file.Role) : file).OrderBy(file => file.Item1, StringComparer.Ordinal).ToArray())),
    "ManifestPathInvalid");
RefusePackage(() => G02BuildManifest.Parse(Manifest(manifestFiles.Where(file => file.Role != "proof").ToArray())),
    "ManifestRoleCardinalityInvalid");
var wrongPackageDigest = Encoding.UTF8.GetString(goodManifestBytes).Replace(goodManifest.PackageDigest,
    new string('0', 64), StringComparison.Ordinal);
RefusePackage(() => G02BuildManifest.Parse(Encoding.UTF8.GetBytes(wrongPackageDigest)), "PackageDigestMismatch");
var duplicatedManifestField = Encoding.UTF8.GetString(goodManifestBytes).Replace(
    "\"schemaVersion\":", "\"schemaVersion\":\"strogo.build-manifest.v0.2\",\"schemaVersion\":", StringComparison.Ordinal);
RefusePackage(() => G02BuildManifest.Parse(Encoding.UTF8.GetBytes(duplicatedManifestField)), "DuplicateField");
RefusePackage(() => G02BuildManifest.Parse(Encoding.UTF8.GetBytes(" " + Encoding.UTF8.GetString(goodManifestBytes))),
    "ArtifactNotCanonical");
if (OperatingSystem.IsWindows())
{
    var snapshotRoot = Path.Combine(repoRoot, "artifacts", "local-validation", "g02",
        "package-snapshot-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(snapshotRoot, "content"));
    var content = manifestFiles.ToDictionary(file => file.Path,
        file => Encoding.UTF8.GetBytes(file.Role), StringComparer.Ordinal);
    foreach (var (path, _) in manifestFiles)
        File.WriteAllBytes(Path.Combine(snapshotRoot, path.Replace('/', Path.DirectorySeparatorChar)), content[path]);
    File.WriteAllBytes(Path.Combine(snapshotRoot, "build-manifest.json"), Manifest(manifestFiles, content: content));
    using (var snapshot = G02PackageSnapshot.OpenStructural(snapshotRoot))
    {
        Check(snapshot.Manifest.Files.Length == manifestFiles.Length &&
              snapshot.ReadHeld("content/module.json").SequenceEqual(content["content/module.json"]),
            "package snapshot retains exact listed bytes");
        var writeDenied = false;
        try { File.WriteAllText(Path.Combine(snapshotRoot, "content", "module.json"), "tampered"); }
        catch (IOException) { writeDenied = true; }
        catch (UnauthorizedAccessException) { writeDenied = true; }
        Check(writeDenied, "held package handle denies content mutation");
    }
    File.WriteAllText(Path.Combine(snapshotRoot, "content", "module.json"), "tampered");
    RefusePackage(() => G02PackageSnapshot.OpenStructural(snapshotRoot), "PackageContentDigestMismatch");
    File.WriteAllBytes(Path.Combine(snapshotRoot, "content", "module.json"), content["content/module.json"]);
    File.WriteAllText(Path.Combine(snapshotRoot, "content", "unlisted.txt"), "x");
    RefusePackage(() => G02PackageSnapshot.OpenStructural(snapshotRoot), "PackageInventoryMismatch");
    File.Delete(Path.Combine(snapshotRoot, "content", "unlisted.txt"));
    File.WriteAllText(Path.Combine(snapshotRoot, "content", "module.json") + ":sidecar", "hidden");
    RefusePackage(() => G02PackageSnapshot.OpenStructural(snapshotRoot), "PackageAlternateStreamRejected");
    File.Delete(Path.Combine(snapshotRoot, "content", "module.json") + ":sidecar");
    File.CreateSymbolicLink(Path.Combine(snapshotRoot, "content", "link.txt"),
        Path.Combine(snapshotRoot, "content", "module.json"));
    RefusePackage(() => G02PackageSnapshot.OpenStructural(snapshotRoot), "PackageReparsePointRejected");

    var raceRoot = Path.Combine(repoRoot, "artifacts", "local-validation", "g02",
        "package-race-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(raceRoot, "content"));
    foreach (var (path, _) in manifestFiles)
        File.WriteAllBytes(Path.Combine(raceRoot, path.Replace('/', Path.DirectorySeparatorChar)), content[path]);
    File.WriteAllBytes(Path.Combine(raceRoot, "build-manifest.json"), Manifest(manifestFiles, content: content));
    var outside = Path.Combine(repoRoot, "artifacts", "local-validation", "g02",
        "race-target-" + Guid.NewGuid().ToString("N") + ".json");
    File.WriteAllBytes(outside, content["content/module.json"]);
    RefusePackage(() => G02PackageSnapshot.OpenStructural(raceRoot, () =>
    {
        var path = Path.Combine(raceRoot, "content", "module.json");
        File.Move(path, path + ".original");
        File.CreateSymbolicLink(path, outside);
    }), "PackageHandleTypeInvalid");

    var identityRoot = Path.Combine(repoRoot, "artifacts", "local-validation", "g02",
        "package-identity-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(identityRoot, "content"));
    var canonicalModule = ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(repoRoot, "fixtures",
        "modules-v0.2", "owner-empty-sequence-module.json")));
    var identityContent = manifestFiles.ToDictionary(file => file.Path,
        file => Encoding.UTF8.GetBytes(file.Role), StringComparer.Ordinal);
    identityContent["content/candidate.dfy"] = Encoding.UTF8.GetBytes("method M() {}\n");
    identityContent["content/module.json"] = canonicalModule.CanonicalSource;
    identityContent["content/bundle.json"] = parsedBundle.CanonicalBytes;
    identityContent["content/approval.json"] = signedProposal;
    var approvalDigest = Hex(Hash("strogo.contract-approval.v0.2/artifact", signedProposal));
    var identities = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["moduleDigest"] = canonicalModule.SourceDigest,
        ["bundleDigest"] = parsedBundle.BundleDigest,
        ["contractApprovalDigest"] = approvalDigest,
        ["toolchainDigest"] = new string('e', 64),
        ["closureDigest"] = new string('f', 64)
    };
    string ProofSourcesDigest() => Hex(Hash("strogo.proof.v0.2/proof-sources", JsonSerializer.SerializeToUtf8Bytes(manifestFiles
        .Where(file => file.Role == "proof-source")
        .Select(file => Fields(("path", file.Path),
            ("sha256", Hex(SHA256.HashData(identityContent[file.Path]))),
            ("length", identityContent[file.Path].LongLength), ("role", file.Role))).ToArray())));
    byte[] Transcript(string evidenceDigest, string diagnosticCode = "verified") => Encode(Fields(
        ("schemaVersion", "strogo.proof-transcript.v0.2"),
        ("toolchainDigest", identities["toolchainDigest"]),
        ("proofSourcesDigest", ProofSourcesDigest()), ("outcome", "Verified"),
        ("records", new[] { Fields(("obligationId", "entry.1"), ("kind", "postcondition"),
            ("status", "Verified"), ("diagnosticCode", diagnosticCode),
            ("evidenceDigest", evidenceDigest)) })));
    identityContent["content/source-map.json"] = Encode(Fields(("entries", Array.Empty<object>())));
    identityContent["content/transcript.json"] = Transcript(new string('4', 64));
    Check(ProofSourcesDigest() == "9fe0504c0d0a1224314d5320fb631110dd4b52412e78869c6dc94d0a6edc16ee" &&
          Hex(Hash("strogo.proof.v0.2/transcript", identityContent["content/transcript.json"])) ==
          "f32a13a6389d316e75630a1a7dfde7f7e4f2623e2adc86f03ead65149fd0b895" &&
          Hex(Hash("strogo.proof.v0.2/source-map", identityContent["content/source-map.json"])) ==
          "9636615613cc4707412b5541f6d321630b31ef96ea05d4c721aca264198bc87e",
          "independently calculated frozen G02 proof artifact digest vectors");
    byte[] Proof(string moduleDigest, string contractDigest) => Encode(Fields(
        ("schemaVersion", "strogo.proof.v0.2"), ("moduleDigest", moduleDigest),
        ("bundleDigest", parsedBundle.BundleDigest), ("contractApprovalDigest", contractDigest),
        ("toolchainDigest", identities["toolchainDigest"]), ("closureDigest", identities["closureDigest"]),
        ("proofSourcesDigest", ProofSourcesDigest()),
        ("transcriptDigest", Hex(Hash("strogo.proof.v0.2/transcript", identityContent["content/transcript.json"]))),
        ("sourceMapDigest", Hex(Hash("strogo.proof.v0.2/source-map", identityContent["content/source-map.json"]))),
        ("outcome", "Verified"),
        ("obligations", new[] { Fields(("obligationId", "entry.1"), ("kind", "postcondition"),
            ("status", "Verified"), ("evidenceDigest", new string('4', 64))) })));
    void WriteIdentityPackage()
    {
        foreach (var (path, bytes) in identityContent)
            File.WriteAllBytes(Path.Combine(identityRoot, path.Replace('/', Path.DirectorySeparatorChar)), bytes);
        identities["proofDigest"] = Hex(Hash("strogo.proof.v0.2/artifact", identityContent["content/proof.json"]));
        File.WriteAllBytes(Path.Combine(identityRoot, "build-manifest.json"),
            Manifest(manifestFiles, content: identityContent, identities: identities));
    }
    identityContent["content/proof.json"] = Proof(canonicalModule.SourceDigest, approvalDigest);
    WriteIdentityPackage();
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
        Check(G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0,
            parsedBundle.BundleDigest, now).ContractApproval.ArtifactDigest == approvalDigest,
            "held module, bundle, approval, proof sources, transcript and source map bind to one manifest");
    var originalProof = identityContent["content/proof.json"];
    var originalSource = identityContent["content/candidate.dfy"];
    identityContent["content/candidate.dfy"] = Encoding.UTF8.GetBytes("changed proof source\n");
    WriteIdentityPackage();
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
        RefusePackage(() => G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0,
            parsedBundle.BundleDigest, now), "PackageProofSourcesDigestMismatch");
    identityContent["content/candidate.dfy"] = originalSource;
    identityContent["content/candidate.dfy"] = Encoding.UTF8.GetBytes("method M() {}\r\n");
    identityContent["content/transcript.json"] = Transcript(new string('4', 64));
    identityContent["content/proof.json"] = Proof(canonicalModule.SourceDigest, approvalDigest);
    WriteIdentityPackage();
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
        RefusePackage(() => G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0,
            parsedBundle.BundleDigest, now), "PackageProofSourceNonCanonical");
    identityContent["content/candidate.dfy"] = originalSource;
    identityContent["content/proof.json"] = originalProof;
    identityContent["content/transcript.json"] = Transcript(new string('4', 64));
    var originalTranscript = identityContent["content/transcript.json"];
    identityContent["content/transcript.json"] = Transcript(new string('4', 64), "changed");
    WriteIdentityPackage();
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
        RefusePackage(() => G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0,
            parsedBundle.BundleDigest, now), "PackageProofTranscriptDigestMismatch");
    identityContent["content/proof.json"] = Proof(canonicalModule.SourceDigest, approvalDigest);
    WriteIdentityPackage();
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
        RefusePackage(() => G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0,
            parsedBundle.BundleDigest, now), "PackageProofTranscriptDiagnosticInvalid");
    identityContent["content/transcript.json"] = Transcript(new string('5', 64));
    identityContent["content/proof.json"] = Proof(canonicalModule.SourceDigest, approvalDigest);
    WriteIdentityPackage();
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
        RefusePackage(() => G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0,
            parsedBundle.BundleDigest, now), "PackageProofTranscriptVectorMismatch");
    identityContent["content/transcript.json"] = originalTranscript;
    identityContent["content/proof.json"] = originalProof;
    var originalSourceMap = identityContent["content/source-map.json"];
    identityContent["content/source-map.json"] = Encode(Fields(("entries", new[] { "forged" })));
    WriteIdentityPackage();
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
        RefusePackage(() => G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0,
            parsedBundle.BundleDigest, now), "PackageProofSourceMapDigestMismatch");
    identityContent["content/source-map.json"] = originalSourceMap;
    WriteIdentityPackage();
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
        Refuse(() => G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0,
            parsedBundle.BundleDigest, now.AddHours(2)), "ContractApprovalExpired");
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
        Refuse(() => G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state1,
            parsedBundle.BundleDigest, now), "ApprovalEpochMismatch");
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
    {
        identityTrust.VerifyOwnerState(state1);
        Refuse(() => G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0,
            parsedBundle.BundleDigest, now), "OwnerStateRollbackDetected");
    }
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
        Refuse(() => G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0,
            new string('0', 64), now), "ContractApprovalDigestMismatch");
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
        Refuse(() => G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state1,
            new string('0', 64), now), "ApprovalEpochMismatch");
    identities["moduleDigest"] = new string('0', 64);
    WriteIdentityPackage();
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
        RefusePackage(() => G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0,
            parsedBundle.BundleDigest, now), "PackageModuleDigestMismatch");
    identities["moduleDigest"] = canonicalModule.SourceDigest;
    identityContent["content/proof.json"] = Proof(new string('0', 64), approvalDigest);
    WriteIdentityPackage();
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
        RefusePackage(() => G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0,
            parsedBundle.BundleDigest, now), "PackageProofIdentityMismatch");
    identityContent["content/proof.json"] = Proof(canonicalModule.SourceDigest, approvalDigest);
    identityContent["content/approval.json"] = approval0;
    identities["contractApprovalDigest"] = Hex(Hash("strogo.contract-approval.v0.2/artifact", approval0));
    WriteIdentityPackage();
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
        Refuse(() => G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0,
            parsedBundle.BundleDigest, now), "ContractApprovalDigestMismatch");
}
if (args.Length == 2 && args[0] == "--prepare-cli-fixture")
{
    var directory = Path.GetFullPath(args[1]);
    if (Directory.Exists(directory)) throw new Exception("CLI fixture directory exists");
    Directory.CreateDirectory(directory);
    var keyPath = Path.Combine(directory, "owner-key.encrypted.pem");
    var publicPath = Path.Combine(directory, "owner-public.spki");
    var stateStore = Path.Combine(directory, "state-store");
    Directory.CreateDirectory(stateStore);
    File.WriteAllText(keyPath, owner.ExportEncryptedPkcs8PrivateKeyPem("disposable-test-passphrase",
        new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000)));
    File.WriteAllBytes(publicPath, publicKey);
    var initialState = State(0);
    File.WriteAllBytes(Path.Combine(stateStore, "owner-state.json"), initialState);
    File.WriteAllBytes(Path.Combine(directory, "initial-owner-state.json"), initialState);
    File.WriteAllText(Path.Combine(directory, "host-owned.json"), JsonSerializer.Serialize(new
    {
        schemaVersion = "strogo.owner-trust-config.v0.1", keyId,
        publicKeyPath = publicPath, ownerStateStore = stateStore
    }));
    File.WriteAllText(Path.Combine(directory, "operator-owned.json"), JsonSerializer.Serialize(new
    {
        schemaVersion = "strogo.owner-signer-config.v0.1", keyId, publicKeyDigest = keyId,
        encryptedPrivateKeyPath = keyPath
    }));
    File.WriteAllText(Path.Combine(directory, "provenance.json"), JsonSerializer.Serialize(new
    {
        kind = "git.commit-path-blob",
        reference = "5869f592666f3107a74ca64f10d0ac10f47e11cf:specs/2026-09-29-g02-dotnet-r2r-admitted-modules-v0.1.md:b89624a70bd9ea48b4600640b5d8d60255ba700a",
        digest = "82726a76d3d61ea6f00203a6bf1c3965c096eefbf69bfd533be852fb7b0ac216"
    }));
    File.WriteAllText(Path.Combine(directory, "valid-until.txt"), DateTimeOffset.UtcNow.AddHours(1)
        .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"));
    Console.WriteLine("CLI fixture directory=" + directory);
}
if (args.Length == 2 && args[0] == "--verify-cli-fixture")
{
    var directory = Path.GetFullPath(args[1]);
    var approvalBytes = File.ReadAllBytes(Path.Combine(directory, "contract-approval.json"));
    var stateBytes = File.ReadAllBytes(Path.Combine(directory, "state-store", "owner-state.json"));
    var fixturePublicKey = File.ReadAllBytes(Path.Combine(directory, "owner-public.spki"));
    using var independentTrust = new OwnerTrust(fixturePublicKey, Hex(SHA256.HashData(fixturePublicKey)));
    var receipt = independentTrust.VerifyContractApproval(approvalBytes, stateBytes, parsedBundle.BundleDigest, DateTimeOffset.UtcNow);
    Check(receipt.BundleDigest == parsedBundle.BundleDigest && receipt.ApprovalEpoch == 0,
        "interactive CLI artifact independently verifies against fixture owner bundle");
    Console.WriteLine("CLI contract-approval verified digest=" + receipt.ArtifactDigest);
}
if (args.Length == 2 && args[0] == "--verify-epoch-fixture")
{
    var directory = Path.GetFullPath(args[1]);
    var fixturePublicKey = File.ReadAllBytes(Path.Combine(directory, "owner-public.spki"));
    var oldState = File.ReadAllBytes(Path.Combine(directory, "initial-owner-state.json"));
    var nextState = File.ReadAllBytes(Path.Combine(directory, "state-store", "owner-state.json"));
    using var independentTrust = new OwnerTrust(fixturePublicKey, Hex(SHA256.HashData(fixturePublicKey)));
    var oldReceipt = independentTrust.VerifyOwnerState(oldState);
    var nextReceipt = independentTrust.VerifyOwnerState(nextState);
    Check(oldReceipt.ApprovalEpoch == 0 && nextReceipt.ApprovalEpoch == 1 &&
          oldReceipt.PolicyDigest == nextReceipt.PolicyDigest &&
          oldReceipt.ArtifactDigest != nextReceipt.ArtifactDigest,
        "interactive CLI advanced signed state and preserved policy");
    Refuse(() => independentTrust.VerifyOwnerState(oldState), "OwnerStateRollbackDetected");
    Console.WriteLine("CLI owner-state advanced digest=" + nextReceipt.ArtifactDigest);
}
Console.WriteLine($"PASS owner admission trust checks={checks}");
