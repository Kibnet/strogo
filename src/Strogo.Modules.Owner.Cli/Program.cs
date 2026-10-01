using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Strogo.Modules;

return Run(args);

static int Run(string[] args)
{
    try
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("approve-contract --trust-config <host-owned.json> --signer-config <operator-owned.json> --bundle <bundle.json> --approved-by <id> --provenance <provenance.json> --valid-until <UTC> --out <contract-approval.json>");
            Console.WriteLine("state advance-epoch --trust-config <host-owned.json> --signer-config <operator-owned.json> --expected-epoch <N>");
            return 0;
        }
        if (args.Length > 1 && args[0] == "state" && args[1] == "advance-epoch")
            return AdvanceEpoch(args);
        if (args.Length == 0 || args[0] != "approve-contract") return Refusal("UnknownOwnerCommand");
        var options = Options(args, 1, "trust-config", "signer-config", "bundle", "approved-by",
            "provenance", "valid-until", "out");
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
            return Refusal("InteractiveTerminalRequired");

        var output = Path.GetFullPath(options["out"]);
        if (File.Exists(output) || Directory.Exists(output)) return Refusal("OutputAlreadyExists");
        using var hostContext = OwnerHostContext.Open(options["trust-config"]);
        var trust = hostContext.Trust;
        var signerConfig = Config(options["signer-config"], "schemaVersion", "keyId", "publicKeyDigest", "encryptedPrivateKeyPath");
        Required(signerConfig, "schemaVersion", "strogo.owner-signer-config.v0.1");
        Required(signerConfig, "keyId", trust.KeyId);
        Required(signerConfig, "publicKeyDigest", trust.KeyId);

        var provenanceDocument = Config(options["provenance"], "kind", "reference", "digest");
        var provenance = new OwnerDecisionProvenance(String(provenanceDocument, "kind"),
            String(provenanceDocument, "reference"), String(provenanceDocument, "digest"));
        VerifyProvenance(provenance);
        var validUntilText = options["valid-until"];
        if (!DateTimeOffset.TryParseExact(validUntilText, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var validUntil) || validUntil.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) != validUntilText)
            return Refusal("InvalidTimestamp");

        var approvalId = Guid.NewGuid().ToString("N");
        var stateBytes = hostContext.ReadCurrentState();
        var bundleBytes = File.ReadAllBytes(Path.GetFullPath(options["bundle"]));
        var issuedAt = new DateTimeOffset(DateTimeOffset.UtcNow.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        var proposal = OwnerContractApprovalProposal.Prepare(trust, stateBytes, bundleBytes,
            approvalId, options["approved-by"], provenance, issuedAt, validUntil);
        Console.WriteLine(proposal.Projection);
        Console.WriteLine("payloadDigest=" + proposal.PayloadDigest);
        Console.Write("Введите полный payloadDigest для подтверждения: ");
        var entered = Console.ReadLine();
        if (entered is null || entered != proposal.PayloadDigest) return Refusal("OwnerConfirmationMismatch");
        Console.Write("Пароль зашифрованного ключа: ");
        var password = ReadPassword();
        try
        {
            VerifyProvenance(provenance);
            using var signer = RSA.Create();
            signer.ImportFromEncryptedPem(File.ReadAllText(AbsolutePath(signerConfig, "encryptedPrivateKeyPath"), new UTF8Encoding(false, true)), password);
            var artifact = proposal.Sign(signer, entered, hostContext.ReadCurrentState(), DateTimeOffset.UtcNow);
            WriteNewAtomically(output, artifact);
            Console.WriteLine("contractApprovalArtifactDigest=" + DomainHash("strogo.contract-approval.v0.2/artifact", artifact));
            return 0;
        }
        finally { Array.Clear(password); }
    }
    catch (ModuleException error) { return Refusal(error.Code); }
    catch (CryptographicException) { return Refusal("SignerUnlockFailed"); }
    catch (Win32Exception) { return Refusal("DecisionProvenanceGitUnavailable"); }
    catch (Exception error) when (error is JsonException or FormatException or ArgumentException or DecoderFallbackException)
    {
        return Refusal("OwnerConfigurationInvalid");
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
        return Refusal("OwnerOperationFailed");
    }
}

static int AdvanceEpoch(string[] args)
{
    var options = Options(args, 2, "trust-config", "signer-config", "expected-epoch");
    if (Console.IsInputRedirected || Console.IsOutputRedirected)
        return Refusal("InteractiveTerminalRequired");
    if (!long.TryParse(options["expected-epoch"], NumberStyles.None, CultureInfo.InvariantCulture,
            out var expectedEpoch) || expectedEpoch.ToString(CultureInfo.InvariantCulture) != options["expected-epoch"])
        return Refusal("InvalidExpectedEpoch");
    using var hostContext = OwnerHostContext.Open(options["trust-config"]);
    var stateDirectory = hostContext.OwnerStateDirectory;
    var statePath = Path.Combine(stateDirectory, "owner-state.json");
    var trust = hostContext.Trust;
    var signerConfig = Config(options["signer-config"], "schemaVersion", "keyId", "publicKeyDigest", "encryptedPrivateKeyPath");
    Required(signerConfig, "schemaVersion", "strogo.owner-signer-config.v0.1");
    Required(signerConfig, "keyId", trust.KeyId);
    Required(signerConfig, "publicKeyDigest", trust.KeyId);

    // All cooperating state writers acquire this host-owned lock before reading the snapshot.
    using var stateLock = new FileStream(Path.Combine(stateDirectory, "owner-state.lock"),
        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    var stateBytes = hostContext.ReadCurrentState();
    var issuedAt = new DateTimeOffset(DateTimeOffset.UtcNow.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
    var proposal = OwnerStateEpochProposal.Prepare(trust, stateBytes, expectedEpoch, issuedAt);
    Console.WriteLine(proposal.Projection);
    Console.WriteLine("payloadDigest=" + proposal.PayloadDigest);
    Console.Write("Введите полный payloadDigest для повышения эпохи: ");
    var entered = Console.ReadLine();
    if (entered is null || entered != proposal.PayloadDigest) return Refusal("OwnerConfirmationMismatch");
    Console.Write("Пароль зашифрованного ключа: ");
    var password = ReadPassword();
    try
    {
        using var signer = RSA.Create();
        signer.ImportFromEncryptedPem(File.ReadAllText(AbsolutePath(signerConfig, "encryptedPrivateKeyPath"), new UTF8Encoding(false, true)), password);
        var next = proposal.Sign(signer, entered, hostContext.ReadCurrentState());
        ReplaceAtomically(statePath, next);
        Console.WriteLine("ownerStateArtifactDigest=" + DomainHash("strogo.owner-state.v0.2/artifact", next));
        return 0;
    }
    finally { Array.Clear(password); }
}

static Dictionary<string, string> Options(string[] args, int offset, params string[] names)
{
    var allowed = new HashSet<string>(names, StringComparer.Ordinal);
    if (args.Length != offset + allowed.Count * 2) throw new ArgumentException("Owner option count");
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = offset; index < args.Length; index += 2)
    {
        var name = args[index];
        if (!name.StartsWith("--", StringComparison.Ordinal) || !allowed.Contains(name[2..]) ||
            !result.TryAdd(name[2..], args[index + 1]) || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException("Unknown, duplicate or empty owner option");
    }
    return result;
}

static JsonElement Config(string path, params string[] expected)
{
    var bytes = File.ReadAllBytes(Path.GetFullPath(path));
    if (bytes.Length is 0 or > 65536) throw new ArgumentException("Invalid config size");
    using var document = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(bytes));
    var root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != expected.Length ||
        root.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != expected.Length ||
        root.EnumerateObject().Any(p => !expected.Contains(p.Name, StringComparer.Ordinal)))
        throw new ArgumentException("Invalid config fields");
    return root.Clone();
}

static string String(JsonElement root, string field)
{
    var value = root.GetProperty(field);
    if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        throw new ArgumentException("Invalid config string");
    return value.GetString()!;
}

static void Required(JsonElement root, string field, string expected)
{
    if (String(root, field) != expected) throw new ArgumentException("Config identity mismatch");
}

static string AbsolutePath(JsonElement root, string field)
{
    var path = String(root, field);
    if (!Path.IsPathFullyQualified(path))
        throw ModulesExceptionFactory.Error("owner", "OperatorPathInvalid");
    return Path.GetFullPath(path);
}

static char[] ReadPassword()
{
    var password = new List<char>(128);
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (password.Count > 0) password.RemoveAt(password.Count - 1);
            continue;
        }
        if (key.KeyChar == '\0' || char.IsControl(key.KeyChar) || password.Count >= 1024)
            throw new ArgumentException("Invalid password input");
        password.Add(key.KeyChar);
    }
    Console.WriteLine();
    var result = password.ToArray();
    for (var index = 0; index < password.Count; index++) password[index] = '\0';
    password.Clear();
    return result;
}

static void WriteNewAtomically(string path, byte[] artifact)
{
    var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("Output directory missing");
    if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
    var temporary = Path.Combine(directory, ".strogo-owner-" + Guid.NewGuid().ToString("N") + ".tmp");
    try
    {
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(artifact);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: false);
    }
    finally
    {
        if (File.Exists(temporary)) File.Delete(temporary);
    }
}

static void ReplaceAtomically(string path, byte[] artifact)
{
    var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("State directory missing");
    var temporary = Path.Combine(directory, ".strogo-owner-state-" + Guid.NewGuid().ToString("N") + ".tmp");
    try
    {
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(artifact);
            stream.Flush(flushToDisk: true);
        }
        File.Replace(temporary, path, null);
    }
    finally
    {
        if (File.Exists(temporary)) File.Delete(temporary);
    }
}

static string DomainHash(string tag, byte[] bytes) =>
    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tag + "\n").Concat(bytes).ToArray()));

static void VerifyProvenance(OwnerDecisionProvenance provenance)
{
    if (provenance.Kind != "git.commit-path-blob")
        throw ModulesExceptionFactory.Error("owner", "UnsupportedDecisionProvenance");
    var parts = provenance.Reference.Split(':');
    if (parts.Length != 3 || !IsGitId(parts[0]) || !IsGitId(parts[2]) ||
        !parts[1].StartsWith("specs/", StringComparison.Ordinal) ||
        parts[1].Split('/').Any(segment => segment.Length == 0 ||
            segment.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-')) ||
            segment is "." or ".."))
        throw ModulesExceptionFactory.Error("owner", "DecisionProvenanceReferenceInvalid");
    var repository = Path.GetFullPath(Directory.GetCurrentDirectory());
    var root = GitText(repository, "rev-parse", "--show-toplevel");
    if (!Path.GetFullPath(root).Equals(repository, StringComparison.OrdinalIgnoreCase))
        throw ModulesExceptionFactory.Error("owner", "DecisionProvenanceRepositoryMismatch");
    if (GitText(repository, "rev-parse", "--verify", parts[0] + "^{commit}") != parts[0] ||
        GitText(repository, "rev-parse", "--verify", parts[0] + ":" + parts[1]) != parts[2])
        throw ModulesExceptionFactory.Error("owner", "DecisionProvenanceBlobMismatch");
    var sizeText = GitText(repository, "cat-file", "-s", parts[2]);
    if (!long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var size) ||
        size is < 1 or > 4_194_304)
        throw ModulesExceptionFactory.Error("owner", "DecisionProvenanceSizeInvalid");
    var bytes = GitBytes(repository, checked((int)size + 1), "cat-file", "blob", parts[2]);
    if (bytes.Length != size || Convert.ToHexStringLower(SHA256.HashData(bytes)) != provenance.Digest)
        throw ModulesExceptionFactory.Error("owner", "DecisionProvenanceDigestMismatch");
}

static bool IsGitId(string value) => value.Length == 40 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

static string GitText(string repository, params string[] arguments) =>
    Encoding.UTF8.GetString(GitBytes(repository, 4096, arguments)).Trim();

static byte[] GitBytes(string repository, int maxBytes, params string[] arguments)
{
    var start = new ProcessStartInfo("git")
    {
        WorkingDirectory = repository,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    start.ArgumentList.Add("-C");
    start.ArgumentList.Add(repository);
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw ModulesExceptionFactory.Error("owner", "DecisionProvenanceGitUnavailable");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    using var output = new MemoryStream();
    var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
    var buffer = new byte[8192];
    try
    {
        while (true)
        {
            var count = process.StandardOutput.BaseStream.ReadAsync(buffer, timeout.Token).AsTask().GetAwaiter().GetResult();
            if (count == 0) break;
            if (output.Length + count > maxBytes)
                throw ModulesExceptionFactory.Error("owner", "DecisionProvenanceSizeInvalid");
            output.Write(buffer, 0, count);
        }
        process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
        _ = errorTask.GetAwaiter().GetResult();
    }
    catch (OperationCanceledException)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        throw ModulesExceptionFactory.Error("owner", "DecisionProvenanceGitTimeout");
    }
    catch
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        throw;
    }
    if (process.ExitCode != 0)
        throw ModulesExceptionFactory.Error("owner", "DecisionProvenanceGitRefused");
    return output.ToArray();
}

static int Refusal(string code)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "Refused", stage = "owner", code }));
    return 2;
}
