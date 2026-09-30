using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kernel.Core;
using Strogo.Modules;

if (args is ["--g02-contained-child"])
{
    Thread.Sleep(TimeSpan.FromMinutes(1));
    return;
}
if (args is ["--g02-contained-parent"])
{
    using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
    {
        UseShellExecute = false, ArgumentList = { "--g02-contained-child" }
    })!;
    Console.WriteLine(child.Id);
    return;
}
if (args is ["--g02-process-probe", var helperMode, var helperSpyPath])
{
    if (helperMode == "hang")
    {
        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, ArgumentList = { "--g02-contained-child" } })!;
        File.WriteAllText(helperSpyPath, $"{Environment.ProcessId}\n{child.Id}");
        Thread.Sleep(TimeSpan.FromMinutes(1));
    }
    else if (helperMode is "stdout-overflow" or "stderr-overflow" or "boundary")
    {
        File.WriteAllText(helperSpyPath, Environment.ProcessId.ToString());
        using var stream = helperMode == "stderr-overflow" ? Console.OpenStandardError() : Console.OpenStandardOutput();
        stream.Write(new byte[helperMode == "boundary" ? 1_048_576 : 1_048_577]);
        stream.Flush();
        if (helperMode != "boundary") Thread.Sleep(TimeSpan.FromMinutes(1));
    }
    else if (helperMode == "environment")
        Console.Write(JsonSerializer.Serialize(Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value)));
    return;
}

using var owner = RSA.Create(2048);
using var impostor = RSA.Create(2048);
var publicKey = owner.ExportSubjectPublicKeyInfo();
var keyId = Hex(SHA256.HashData(publicKey));
using var trust = new OwnerTrust(publicKey, keyId);
var now = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
var bundleDigest = new string('a', 64);
var checks = 0;

if (OperatingSystem.IsWindows())
{
    foreach (var explicitKill in new[] { true, false })
    {
        var probeStart = new ProcessStartInfo(Environment.ProcessPath!)
        {
            WorkingDirectory = Environment.CurrentDirectory,
            ArgumentList = { "--g02-contained-parent" }
        };
        var contained = G02ContainedProcess.Start(probeStart);
        Process? descendant = null;
        try
        {
            using var reader = new StreamReader(contained.Stdout, leaveOpen: true);
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            descendant = Process.GetProcessById(int.Parse(line!));
            await contained.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(!descendant.HasExited, "process-spy observes live descendant after parent exit");
            if (explicitKill) await contained.TerminateAndDrainAsync();
            else contained.Dispose();
            await descendant.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(descendant.HasExited, "job kill/disposal terminates descendant after parent exit");
        }
        finally
        {
            contained.Dispose();
            descendant?.Dispose();
        }
    }
    var probeDirectory = Path.Combine(Path.GetTempPath(), "strogo-g02-probes-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(probeDirectory);
    var probesCompleted = false;
    try
    {
        foreach (var mode in new[] { "hang", "stdout-overflow", "stderr-overflow", "boundary", "environment" })
        {
            var spyPath = Path.Combine(probeDirectory, mode + ".txt");
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            { WorkingDirectory = probeDirectory, ArgumentList = { "--g02-process-probe", mode, spyPath } };
            start.Environment["DOTNET_ROOT"] = "untrusted-dotnet";
            start.Environment["DAFNY_PROBE"] = "injected";
            start.Environment["G02_PARENT_POISON"] = "injected";
            G02VerifierProcess.ConfigureEnvironment(start, Path.GetDirectoryName(Environment.ProcessPath!)!, probeDirectory);
            var watch = Stopwatch.StartNew();
            if (mode is "hang" or "stdout-overflow" or "stderr-overflow")
            {
                RefusePackage(() => G02VerifierProcess.RunAsync(start,
                    TimeSpan.FromSeconds(mode == "hang" ? 2 : 10)).GetAwaiter().GetResult(),
                    mode == "hang" ? "VerifierFailed" : "VerifierOutputLimitExceeded");
                Check(watch.Elapsed < TimeSpan.FromSeconds(mode == "hang" ? 8 : 16), "process refusal stays within execution plus drain budget");
                foreach (var pid in File.ReadAllLines(spyPath).Select(int.Parse))
                {
                    var alive = false;
                    try { using var observed = Process.GetProcessById(pid); alive = !observed.HasExited; }
                    catch (ArgumentException) { }
                    Check(!alive, "failed verifier leaves no process-spy parent or descendant alive");
                }
            }
            else
            {
                var output = await G02VerifierProcess.RunAsync(start, TimeSpan.FromSeconds(10));
                Check(output.ExitCode == 0 && output.Stderr.Length == 0, "bounded helper completes successfully");
                if (mode == "boundary") Check(output.Stdout.Length == 1_048_576, "exact output limit is accepted");
                else
                {
                    var environment = JsonSerializer.Deserialize<Dictionary<string, string?>>(output.Stdout)!;
                    Check(environment["TEMP"] == probeDirectory && environment["TMP"] == probeDirectory &&
                        environment["SystemRoot"] == Environment.GetFolderPath(Environment.SpecialFolder.Windows) &&
                        environment["WINDIR"] == environment["SystemRoot"] &&
                        environment["PATH"] == Path.GetDirectoryName(Environment.ProcessPath!) + Path.PathSeparator +
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32") &&
                        environment.Keys.Order(StringComparer.Ordinal).SequenceEqual(
                            new[] { "PATH", "SystemRoot", "TEMP", "TMP", "WINDIR" }.Order(StringComparer.Ordinal)) &&
                        new[] { "DOTNET_ROOT", "HOME", "USERPROFILE", "DAFNY_PROBE", "G02_PARENT_POISON" }
                            .All(name => !environment.ContainsKey(name)), "child receives trusted explicit environment without injected or inherited variables");
                }
            }
        }
        probesCompleted = true;
    }
    finally
    {
        try { Directory.Delete(probeDirectory, recursive: true); }
        catch (Exception error) when (!probesCompleted && (error is IOException or UnauthorizedAccessException)) { }
    }
}

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

var replayObligations = ImmutableArray.Create(
    new DafnyProofObligation("entry.2", "function.2", "postcondition", 7),
    new DafnyProofObligation("entry.1", "function.1", "postcondition", 5));
var replayStdout = Encoding.UTF8.GetBytes("\r\nDafny program verifier finished with 4 verified, 0 errors\r\n");
var normalized = G02ProofTranscript.NormalizeVerified(0, false, false, replayStdout, [],
    new string('e', 64), new string('f', 64), replayObligations);
using var normalizedVectorDocument = JsonDocument.Parse(normalized.ObligationVectorBytes);
Check(normalized.VerifiedUnits == 4 && normalized.EvidenceDigest ==
      "efa441ff0a8b21a1f432303197a08e3794e0f32da63cf5763680ad3247e45572" &&
      normalizedVectorDocument.RootElement.EnumerateArray()
          .Select(item => item.GetProperty("obligationId").GetString()!).SequenceEqual(
              replayObligations.Select(item => G02ProofTranscript.WireObligationId(item.Id)).Order(StringComparer.Ordinal)),
    "real Windows Dafny summary normalizes with independently fixed evidence digest and ordered obligations");
var lfNormalized = G02ProofTranscript.NormalizeVerified(0, false, false,
    Encoding.UTF8.GetBytes("\nDafny program verifier finished with 4 verified, 0 errors\n"), [],
    new string('e', 64), new string('f', 64), replayObligations);
Check(normalized.TranscriptBytes.SequenceEqual(lfNormalized.TranscriptBytes),
    "CRLF and LF framing produce identical normative transcript");
var changedUnits = G02ProofTranscript.NormalizeVerified(0, false, false,
    Encoding.UTF8.GetBytes("\r\nDafny program verifier finished with 5 verified, 0 errors\r\n"), [],
    new string('e', 64), new string('f', 64), replayObligations);
Check(!normalized.TranscriptBytes.SequenceEqual(changedUnits.TranscriptBytes),
    "verified unit count changes the normative transcript");
RefusePackage(() => G02ProofTranscript.NormalizeVerified(0, false, false,
    Encoding.UTF8.GetBytes("warning\r\nDafny program verifier finished with 4 verified, 0 errors\r\n"), [],
    new string('e', 64), new string('f', 64), replayObligations), "VerifierOutputUnrecognized");
RefusePackage(() => G02ProofTranscript.NormalizeVerified(0, false, false,
    Encoding.UTF8.GetBytes("Dafny program verifier finished with 0 verified, 0 errors\r\n"), [],
    new string('e', 64), new string('f', 64), replayObligations), "VerifierOutputUnrecognized");
RefusePackage(() => G02ProofTranscript.NormalizeVerified(0, false, false, replayStdout,
    Encoding.UTF8.GetBytes("warning"), new string('e', 64), new string('f', 64), replayObligations),
    "VerifierOutputUnrecognized");
RefusePackage(() => G02ProofTranscript.NormalizeVerified(1, false, false, replayStdout, [],
    new string('e', 64), new string('f', 64), replayObligations), "VerifierFailed");
RefusePackage(() => G02ProofTranscript.NormalizeVerified(0, true, false, replayStdout, [],
    new string('e', 64), new string('f', 64), replayObligations), "VerifierFailed");
RefusePackage(() => G02ProofTranscript.NormalizeVerified(0, false, false, replayStdout, [],
    new string('e', 64), new string('f', 64), ImmutableArray<DafnyProofObligation>.Empty),
    "ProofObligationsInvalid");

foreach (var (rawId, wireId) in new[]
{
    ("candidate-postcondition-addOne", "7776a82e85daee1a37321b77e98935487d3f03be8617fe9787be0528521c8f99"),
    ("range-addOne-sum", "a0539ccdd1ce04a5b6fb2992470ab29c170712a055ddd5e110eda03891fe75f2"),
    ("candidate-postcondition-addone", "124a7d37b9fa14dbb51f93bde4c4cbcffa6299bef9442174c549244537db44fa"),
    ("candidate-postcondition", "811905e8890e80ecf094b4c174f34bc1e2eb1bcfe4c4fa656ecfcdd1c2b10dfe")
}) Check(G02ProofTranscript.WireObligationId(rawId) == wireId, "independent obligation ID fixed vector");
var caseOrdered = G02ProofTranscript.NormalizeVerified(0, false, false, replayStdout, [],
    new string('e', 64), new string('f', 64), ImmutableArray.Create(
        new DafnyProofObligation("candidate-postcondition-addOne", "upper", "postcondition", 1),
        new DafnyProofObligation("candidate-postcondition-addone", "lower", "postcondition", 2)));
using var caseVector = JsonDocument.Parse(caseOrdered.ObligationVectorBytes);
Check(caseVector.RootElement.EnumerateArray().Select(item => item.GetProperty("obligationId").GetString()).SequenceEqual(new[]
{
    "124a7d37b9fa14dbb51f93bde4c4cbcffa6299bef9442174c549244537db44fa",
    "7776a82e85daee1a37321b77e98935487d3f03be8617fe9787be0528521c8f99"
}), "wire ordering follows independent mapped IDs rather than case-sensitive raw ordering");
Check(G02ProofTranscript.WireObligationId(new string('X', 100)).Length == 64, "long raw ID fits exact E05 bound");
Check(G02ProofTranscript.WireObligationId(new string('X', 100)) !=
      G02ProofTranscript.WireObligationId(new string('X', 99) + "Y"), "distinct long raw IDs remain distinct wire identities");
RefusePackage(() => G02ProofTranscript.WireObligationId("\ud800"), "ProofObligationsInvalid");
RefusePackage(() => G02ProofTranscript.NormalizeVerified(0, false, false, replayStdout, [],
    new string('e', 64), new string('f', 64), ImmutableArray.Create(replayObligations[0], replayObligations[0])), "ProofObligationsInvalid");
RefusePackage(() => G02ProofTranscript.NormalizeVerified(0, false, false, replayStdout, [],
    new string('e', 64), new string('f', 64), ImmutableArray.Create(
        new DafnyProofObligation("raw", "locus", "InvalidKind", 1))), "ProofObligationsInvalid");

var state0 = State(0);
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
if (OperatingSystem.IsWindows())
{
    var sdkInventoryBytes = File.ReadAllBytes(Path.Combine(repoRoot, "tools", "dotnet-sdk-files.json"));
    RefusePackage(() => G02DotNetToolchain.Open(Path.Combine(Path.GetTempPath(), "g02-missing-" + Guid.NewGuid().ToString("N"))),
        "BuildToolchainIoRefused");
    var sdkInventory = G02DotNetToolchain.ParsePinnedInventory(sdkInventoryBytes);
    Check(sdkInventory.Length == 5577 && sdkInventory.Select(file => file.Path)
        .SequenceEqual(sdkInventory.Select(file => file.Path).Order(StringComparer.Ordinal)),
        "SDK closure uses the archive-derived ordinal inventory");
    RefusePackage(() => G02DotNetToolchain.CheckInventoryDigest(new string('0', 64)),
        "BuildToolchainInventoryMismatch");
    byte[] ChangedSdkInventory(bool duplicate) => JsonSerializer.SerializeToUtf8Bytes(sdkInventory.Select((file, index) =>
        new { path = duplicate && index == 1 ? sdkInventory[0].Path : file.Path,
            sha256 = !duplicate && index == 0 ? new string('0', 64) : file.Sha256 }));
    RefusePackage(() => G02DotNetToolchain.ParsePinnedInventory(ChangedSdkInventory(false)),
        "BuildToolchainInventoryMismatch");
    RefusePackage(() => G02DotNetToolchain.ParsePinnedInventory(ChangedSdkInventory(true)),
        "BuildToolchainInventoryMismatch");
    var sdkDocument = Path.Combine(repoRoot, ".tools", "dotnet-sdk-10.0.400", "ThirdPartyNotices.txt");
    using (var permitted = new FileStream(sdkDocument, FileMode.Open, FileAccess.Write, FileShare.Read))
        Check(permitted.CanWrite, "SDK document write-open is permitted before holding closure (no bytes written)");
    using var sdk = G02DotNetToolchain.Open(repoRoot);
    var writeDenied = false;
    try { using var writer = new FileStream(sdkDocument, FileMode.Open, FileAccess.Write, FileShare.Read); }
    catch (IOException) { writeDenied = true; }
    catch (UnauthorizedAccessException) { writeDenied = true; }
    Check(writeDenied, "held SDK closure denies the previously permitted document write-open");
    var extraDirectory = Path.Combine(sdk.DirectoryPath, "g02-extra-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(extraDirectory);
    try { RefusePackage(sdk.Revalidate, "BuildToolchainInventoryMismatch"); }
    finally { Directory.Delete(extraDirectory); }
    var metadataProbe = Path.Combine(sdk.DirectoryPath, "metadata", "g02-unlisted-" + Guid.NewGuid().ToString("N"));
    File.WriteAllText(metadataProbe, "not-part-of-sdk");
    try { RefusePackage(sdk.Revalidate, "BuildToolchainInventoryMismatch"); }
    finally { File.Delete(metadataProbe); }
    var sdkProbe = Path.Combine(repoRoot, "artifacts", "local-validation", "g02", "sdk-probe-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(sdkProbe);
    var start = new ProcessStartInfo(sdk.ExecutablePath) { WorkingDirectory = sdkProbe, ArgumentList = { "--version" } };
    G02VerifierProcess.ConfigureEnvironment(start, sdk.DirectoryPath, sdkProbe);
    var output = await G02VerifierProcess.RunAsync(start);
    File.WriteAllBytes(Path.Combine(sdkProbe, "identity.json"), sdk.IdentityBytes);
    File.WriteAllBytes(Path.Combine(sdkProbe, "stdout.log"), output.Stdout);
    File.WriteAllBytes(Path.Combine(sdkProbe, "stderr.log"), output.Stderr);
    Check(output.ExitCode == 0 && output.Stderr.Length == 0 &&
        Encoding.UTF8.GetString(output.Stdout).Trim() == G02DotNetToolchain.SdkVersion,
        "actual pinned SDK runs while its complete closure is held");
    sdk.Revalidate();
    Check(sdk.Digest == Hex(Hash("strogo.build.v0.2/sdk-closure", sdk.IdentityBytes)),
        "private SDK closure diagnostic identity remains stable after execution");
}
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

    var foldModule = ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(repoRoot, "fixtures",
        "modules-v0.2", "fold-sum-valid.json")));
    var lowering = ModulesDafnyLowerer.Lower(ModulesCompiler.Compile(foldModule), parsedBundle);
    identityContent["content/module.json"] = foldModule.CanonicalSource;
    identityContent["content/approval.json"] = signedProposal;
    identityContent["content/candidate.dfy"] = lowering.SourceBytes;
    identityContent["content/source-map.json"] = CanonicalJson.Encode(lowering.SourceMap);
    identities["moduleDigest"] = foldModule.SourceDigest;
    identities["contractApprovalDigest"] = approvalDigest;
    identityContent["content/transcript.json"] = Transcript(new string('4', 64));
    identityContent["content/proof.json"] = Proof(foldModule.SourceDigest, approvalDigest);
    WriteIdentityPackage();
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
    {
        _ = G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0, parsedBundle.BundleDigest, now);
        var regenerated = G02ProofSourceRegenerator.Verify(identitySnapshot);
        Check(regenerated.SourceBytes.SequenceEqual(lowering.SourceBytes) &&
              regenerated.SourceMapBytes.SequenceEqual(identityContent["content/source-map.json"]) &&
              regenerated.Obligations.SequenceEqual(lowering.Obligations),
            "proof source and source map regenerate from held module and owner bytes");
        var pinned = G02DafnyToolchain.ParsePinnedInventory(File.ReadAllBytes(Path.Combine(repoRoot, "tools", "dafny-files.json")));
        Check(pinned.Length == 290 && pinned[0].Path == "Boogie.AbstractInterpretation.dll" &&
              pinned[^1].Path == "z3/bin/z3-4.14.1.exe", "pinned inventory uses exact ordinal path ordering");
        RefusePackage(() => G02DafnyToolchain.CheckInventoryDigest(
            "f992c066b3d27e34a59d1cb18ce8c899260e9c446722d9dc533704d3f328980e"), "ToolchainInventoryMismatch");
        RefusePackage(() => G02DafnyToolchain.CheckInventoryDigest(
            "b61281cb0111a05b0554e395c3839b7150731c257629eeafafeeeec56c50b83e"), "ToolchainInventoryMismatch");
        using var tool = G02DafnyToolchain.Open(repoRoot);
        var toolWriteDenied = false;
        try { using var writer = new FileStream(Path.Combine(tool.DirectoryPath, pinned[0].Path), FileMode.Open, FileAccess.Write); }
        catch (IOException) { toolWriteDenied = true; }
        Check(toolWriteDenied, "held tool closure denies file writes before and during replay");
        var extraToolDirectory = Path.Combine(tool.DirectoryPath, "g02-extra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extraToolDirectory);
        try { RefusePackage(tool.Revalidate, "ToolchainInventoryMismatch"); }
        finally { Directory.Delete(extraToolDirectory); }
        var replayEvidence = Path.Combine(repoRoot, "artifacts", "local-validation", "g02", "fresh-replay-" + Guid.NewGuid().ToString("N"));
        RefusePackage(() => G02DafnyReplay.RunFixtureAsync(tool, regenerated,
            Path.Combine(replayEvidence, "unproven-fold")).GetAwaiter().GetResult(), "VerifierFailed");
        var scalarOwner = OwnerBundleV04Parser.Parse(OwnerBundleMigrator.MigrateV03ToV04(
            File.ReadAllBytes(Path.Combine(repoRoot, "fixtures", "modules-v0.2", "owner-add-one-valid.json"))));
        var scalarLowering = ModulesDafnyLowerer.Lower(ModulesCompiler.Compile(ModulesParser.ParseModule(
            File.ReadAllBytes(Path.Combine(repoRoot, "fixtures", "modules-v0.2", "math-add-valid.json")))), scalarOwner);
        var allocationOwner = OwnerBundleV04Parser.Parse(File.ReadAllBytes(Path.Combine(repoRoot,
            "fixtures", "modules-v0.2", "owner-fold-allocation-v0.4.json")));
        var allocationLowering = ModulesDafnyLowerer.Lower(ModulesCompiler.Compile(ModulesParser.ParseModule(
            File.ReadAllBytes(Path.Combine(repoRoot, "fixtures", "modules-v0.2", "fold-allocation-primary.json")))), allocationOwner);
        foreach (var (shape, positive) in new[] { ("scalar", scalarLowering), ("allocation", allocationLowering) })
        {
        var positiveInputs = new G02RegeneratedProofInputs(positive.SourceBytes,
            CanonicalJson.Encode(positive.SourceMap), positive.Obligations);
        var firstReplay = await G02DafnyReplay.RunFixtureAsync(tool, positiveInputs, Path.Combine(replayEvidence, shape, "first"));
        var secondReplay = await G02DafnyReplay.RunFixtureAsync(tool, positiveInputs, Path.Combine(replayEvidence, shape, "second"));
        Check(firstReplay.Verified.TranscriptBytes.SequenceEqual(secondReplay.Verified.TranscriptBytes) &&
              firstReplay.Verified.ObligationVectorBytes.SequenceEqual(secondReplay.Verified.ObligationVectorBytes),
            "two fresh pinned Dafny fixture runs produce byte-equal normative transcript and obligations");
        Directory.CreateDirectory(replayEvidence);
        File.WriteAllBytes(Path.Combine(replayEvidence, "toolchain.json"), tool.IdentityBytes);
        File.WriteAllBytes(Path.Combine(replayEvidence, shape, "transcript.json"), firstReplay.Verified.TranscriptBytes);
        File.WriteAllBytes(Path.Combine(replayEvidence, shape, "candidate.dfy"), positiveInputs.SourceBytes);
        File.WriteAllBytes(Path.Combine(replayEvidence, shape, "source-map.json"), positiveInputs.SourceMapBytes);
        File.WriteAllBytes(Path.Combine(replayEvidence, shape, "obligations.json"), firstReplay.Verified.ObligationVectorBytes);
        var packageRoot = Path.Combine(replayEvidence, shape, "held-package");
        Directory.CreateDirectory(Path.Combine(packageRoot, "content"));
        var positiveOwner = shape == "scalar" ? scalarOwner : allocationOwner;
        var positiveModule = ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(repoRoot, "fixtures",
            "modules-v0.2", shape == "scalar" ? "math-add-valid.json" : "fold-allocation-primary.json")));
        using var signingTrust = new OwnerTrust(publicKey, keyId);
        var positiveProposal = OwnerContractApprovalProposal.Prepare(signingTrust, state0,
            positiveOwner.CanonicalBytes, "review.fixture", "owner", provenance, now, now.AddHours(1));
        var positiveApproval = positiveProposal.Sign(owner, positiveProposal.PayloadDigest, state0, now);
        var replayContent = identityContent.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        replayContent["content/module.json"] = positiveModule.CanonicalSource;
        replayContent["content/bundle.json"] = positiveOwner.CanonicalBytes;
        replayContent["content/approval.json"] = positiveApproval;
        replayContent["content/candidate.dfy"] = positiveInputs.SourceBytes;
        replayContent["content/source-map.json"] = positiveInputs.SourceMapBytes;
        replayContent["content/transcript.json"] = firstReplay.Verified.TranscriptBytes;
        var packageIds = new Dictionary<string, string>(identities, StringComparer.Ordinal)
        {
            ["moduleDigest"] = positiveModule.SourceDigest, ["bundleDigest"] = positiveOwner.BundleDigest,
            ["contractApprovalDigest"] = Hex(Hash("strogo.contract-approval.v0.2/artifact", positiveApproval)),
            ["toolchainDigest"] = tool.Digest
        };
        var vectorBytes = firstReplay.Verified.ObligationVectorBytes;
        void WriteReplayPackage()
        {
            using var transcriptDocument = JsonDocument.Parse(replayContent["content/transcript.json"]);
            using var vectorDocument = JsonDocument.Parse(vectorBytes);
            replayContent["content/proof.json"] = Encode(Fields(
                ("schemaVersion", "strogo.proof.v0.2"), ("moduleDigest", packageIds["moduleDigest"]),
                ("bundleDigest", packageIds["bundleDigest"]), ("contractApprovalDigest", packageIds["contractApprovalDigest"]),
                ("toolchainDigest", packageIds["toolchainDigest"]), ("closureDigest", packageIds["closureDigest"]),
                ("proofSourcesDigest", transcriptDocument.RootElement.GetProperty("proofSourcesDigest").GetString()!),
                ("transcriptDigest", Hex(Hash("strogo.proof.v0.2/transcript", replayContent["content/transcript.json"]))),
                ("sourceMapDigest", Hex(Hash("strogo.proof.v0.2/source-map", replayContent["content/source-map.json"]))),
                ("outcome", "Verified"), ("obligations", vectorDocument.RootElement.Clone())));
            packageIds["proofDigest"] = Hex(Hash("strogo.proof.v0.2/artifact", replayContent["content/proof.json"]));
            foreach (var (path, bytes) in replayContent)
                File.WriteAllBytes(Path.Combine(packageRoot, path.Replace('/', Path.DirectorySeparatorChar)), bytes);
            File.WriteAllBytes(Path.Combine(packageRoot, "build-manifest.json"), Manifest(manifestFiles, content: replayContent, identities: packageIds));
        }
        WriteReplayPackage();
        var toolOpens = 0;
        G02DafnyToolchain OpenTool() { toolOpens++; return G02DafnyToolchain.Open(repoRoot); }
        using (var snapshot = G02PackageSnapshot.OpenStructural(packageRoot))
        using (var packageTrust = new OwnerTrust(publicKey, keyId))
        {
            var actual = await G02PackageProofReplay.VerifyFixtureAsync(snapshot, packageTrust, state0,
                positiveOwner.BundleDigest, now, OpenTool, Path.Combine(replayEvidence, shape, "composed"));
            Check(actual.TranscriptBytes.SequenceEqual(firstReplay.Verified.TranscriptBytes) && toolOpens == 1,
                "held signed fixture package passes composed regeneration and two real replay comparisons");
        }
        foreach (var negative in new[] { "expired", "epoch", "signature" })
        {
            replayContent["content/approval.json"] = negative == "signature"
                ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(positiveApproval).Replace("\"approvedBy\":\"owner\"", "\"approvedBy\":\"agent\"", StringComparison.Ordinal))
                : positiveApproval;
            WriteReplayPackage();
            toolOpens = 0;
            using var snapshot = G02PackageSnapshot.OpenStructural(packageRoot);
            using var packageTrust = new OwnerTrust(publicKey, keyId);
            Refuse(() => G02PackageProofReplay.VerifyFixtureAsync(snapshot, packageTrust,
                negative == "epoch" ? state1 : state0, positiveOwner.BundleDigest,
                negative == "expired" ? now.AddHours(2) : now, OpenTool,
                Path.Combine(replayEvidence, shape, "refused-" + negative)).GetAwaiter().GetResult(),
                negative == "epoch" ? "ApprovalEpochMismatch" : negative == "expired" ? "ContractApprovalExpired" : "InvalidOwnerSignature");
            Check(toolOpens == 0, "invalid owner gate refuses before verifier closure is opened");
        }
        replayContent["content/approval.json"] = positiveApproval;
        replayContent["content/transcript.json"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(firstReplay.Verified.TranscriptBytes)
            .Replace(firstReplay.Verified.EvidenceDigest, new string('6', 64), StringComparison.Ordinal));
        vectorBytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(firstReplay.Verified.ObligationVectorBytes)
            .Replace(firstReplay.Verified.EvidenceDigest, new string('6', 64), StringComparison.Ordinal));
        WriteReplayPackage();
        var forgedRoot = Path.Combine(replayEvidence, shape, "forged-package");
        Directory.CreateDirectory(Path.Combine(forgedRoot, "content"));
        foreach (var (path, bytes) in replayContent)
            File.WriteAllBytes(Path.Combine(forgedRoot, path.Replace('/', Path.DirectorySeparatorChar)), bytes);
        File.Copy(Path.Combine(packageRoot, "build-manifest.json"), Path.Combine(forgedRoot, "build-manifest.json"));
        using (var snapshot = G02PackageSnapshot.OpenStructural(packageRoot))
        using (var packageTrust = new OwnerTrust(publicKey, keyId))
        {
            _ = G02StoredIdentityVerifier.Verify(snapshot, packageTrust, state0, positiveOwner.BundleDigest, now);
            Check(true, "self-consistent forged transcript and vector pass stored identity checks");
            RefusePackage(() => G02PackageProofReplay.VerifyFixtureAsync(snapshot, packageTrust, state0,
                positiveOwner.BundleDigest, now, OpenTool, Path.Combine(replayEvidence, shape, "forged"))
                .GetAwaiter().GetResult(), "ProofReplayTranscriptMismatch");
        }
        vectorBytes = firstReplay.Verified.ObligationVectorBytes;
        replayContent["content/transcript.json"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(firstReplay.Verified.TranscriptBytes)
            .Replace(tool.Digest, new string('0', 64), StringComparison.Ordinal));
        packageIds["toolchainDigest"] = new string('0', 64);
        WriteReplayPackage();
        toolOpens = 0;
        var wrongToolDiagnostics = Path.Combine(replayEvidence, shape, "wrong-tool");
        using (var snapshot = G02PackageSnapshot.OpenStructural(packageRoot))
        using (var packageTrust = new OwnerTrust(publicKey, keyId))
        {
            RefusePackage(() => G02PackageProofReplay.VerifyFixtureAsync(snapshot, packageTrust, state0,
                positiveOwner.BundleDigest, now, OpenTool, wrongToolDiagnostics).GetAwaiter().GetResult(),
                "PackageToolchainDigestMismatch");
            Check(toolOpens == 1 && !Directory.Exists(wrongToolDiagnostics),
                "self-consistent wrong package toolchain is refused before any replay diagnostics");
        }
        var alternateRoot = Path.Combine(replayEvidence, shape, "alternate-source-path");
        Directory.CreateDirectory(Path.Combine(alternateRoot, "content"));
        var alternateContent = replayContent.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        alternateContent["content/other.dfy"] = alternateContent["content/candidate.dfy"];
        alternateContent.Remove("content/candidate.dfy");
        var alternateFiles = manifestFiles.Select(file =>
            (Path: file.Path == "content/candidate.dfy" ? "content/other.dfy" : file.Path, file.Role))
            .OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        foreach (var (path, bytes) in alternateContent)
            File.WriteAllBytes(Path.Combine(alternateRoot, path.Replace('/', Path.DirectorySeparatorChar)), bytes);
        File.WriteAllBytes(Path.Combine(alternateRoot, "build-manifest.json"),
            Manifest(alternateFiles, content: alternateContent, identities: packageIds));
        using (var snapshot = G02PackageSnapshot.OpenStructural(alternateRoot))
            RefusePackage(() => G02ProofSourceRegenerator.Verify(snapshot), "ProofSourcePathUnsupported");
        packageIds["toolchainDigest"] = tool.Digest;
        replayContent["content/transcript.json"] = firstReplay.Verified.TranscriptBytes;
        WriteReplayPackage(); // Retain the original positive package alongside each separate negative artifact.
        }
    }
    identityContent["content/candidate.dfy"] = Encoding.UTF8.GetBytes("method Fake() {}\n");
    identityContent["content/transcript.json"] = Transcript(new string('4', 64));
    identityContent["content/proof.json"] = Proof(foldModule.SourceDigest, approvalDigest);
    WriteIdentityPackage();
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
    {
        _ = G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0, parsedBundle.BundleDigest, now);
        RefusePackage(() => G02ProofSourceRegenerator.Verify(identitySnapshot), "ProofSourceGenerationMismatch");
    }
    identityContent["content/candidate.dfy"] = lowering.SourceBytes;
    identityContent["content/source-map.json"] = Encode(Fields(("entries", Array.Empty<object>())));
    identityContent["content/transcript.json"] = Transcript(new string('4', 64));
    identityContent["content/proof.json"] = Proof(foldModule.SourceDigest, approvalDigest);
    WriteIdentityPackage();
    using (var identitySnapshot = G02PackageSnapshot.OpenStructural(identityRoot))
    using (var identityTrust = new OwnerTrust(publicKey, keyId))
    {
        _ = G02StoredIdentityVerifier.Verify(identitySnapshot, identityTrust, state0, parsedBundle.BundleDigest, now);
        RefusePackage(() => G02ProofSourceRegenerator.Verify(identitySnapshot), "ProofSourceMapGenerationMismatch");
    }
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
