using System.Diagnostics;
using System.Security.Cryptography;

namespace Strogo.Modules;

internal sealed record G02FixtureReplay(G02VerifiedTranscript Verified, byte[] Stdout, byte[] Stderr,
    byte[]? GeneratedSourceBytes = null);

/// <summary>Real verifier replay for fixtures. Production admission still requires host-owned ACL validation.</summary>
internal static class G02DafnyReplay
{
    internal static Task<G02FixtureReplay> RunFixtureAsync(G02DafnyToolchain tool,
        G02RegeneratedProofInputs inputs, string diagnosticDirectory)
        => RunCoreAsync(tool, inputs, diagnosticDirectory, translate: false);

    internal static Task<G02FixtureReplay> TranslateFixtureAsync(G02DafnyToolchain tool,
        G02RegeneratedProofInputs inputs, string diagnosticDirectory)
        => RunCoreAsync(tool, inputs, diagnosticDirectory, translate: true);

    private static async Task<G02FixtureReplay> RunCoreAsync(G02DafnyToolchain tool,
        G02RegeneratedProofInputs inputs, string diagnosticDirectory, bool translate)
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var work = Path.Combine(tempRoot, "strogo-g02-replay-" + Guid.NewGuid().ToString("N"));
        var scratch = work + "-temp";
        var completed = false;
        try
        {
            Directory.CreateDirectory(work);
            Directory.CreateDirectory(scratch);
            var source = Path.Combine(work, "candidate.dfy");
            await File.WriteAllBytesAsync(source, inputs.SourceBytes);
            var heldDirectory = G02WindowsHeldHandle.OpenDirectory(work, null);
            using var directoryHandle = heldDirectory.Handle;
            using var sourceHandle = G02WindowsHeldHandle.OpenFile(source, heldDirectory.FinalPath + "\\candidate.dfy");
            if (sourceHandle.Length != inputs.SourceBytes.Length ||
                Convert.ToHexStringLower(SHA256.HashData(sourceHandle)) !=
                Convert.ToHexStringLower(SHA256.HashData(inputs.SourceBytes)))
                throw Refuse("ProofSourceGenerationMismatch");
            tool.Revalidate();
            var start = new ProcessStartInfo(tool.ExecutablePath)
            {
                WorkingDirectory = work, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            string[] arguments = translate
                ? ["translate", "cs", "candidate.dfy", "--output", "Generated.cs", "--include-runtime",
                    "--enforce-determinism", "--cores", "2", "--verification-time-limit", "15"]
                : G02DafnyToolchain.VerifierArguments;
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            G02VerifierProcess.ConfigureEnvironment(start, tool.DirectoryPath, scratch);
            var output = await G02VerifierProcess.RunAsync(start);
            var stdout = output.Stdout;
            var stderr = output.Stderr;
            Directory.CreateDirectory(diagnosticDirectory);
            await File.WriteAllBytesAsync(Path.Combine(diagnosticDirectory, "stdout.bin"), stdout);
            await File.WriteAllBytesAsync(Path.Combine(diagnosticDirectory, "stderr.bin"), stderr);
            await File.WriteAllBytesAsync(Path.Combine(diagnosticDirectory, "process.json"),
                G02ProofTranscript.Canonical(new { arguments, exitCode = output.ExitCode, timeout = false }));
            tool.Revalidate();
            string[] expectedFiles = translate ? ["candidate.dfy", "Generated.cs", "Generated-cs.dtr"] : ["candidate.dfy"];
            if (!Directory.EnumerateFileSystemEntries(work).Select(Path.GetFileName)
                    .Order(StringComparer.Ordinal).SequenceEqual(expectedFiles.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw Refuse("VerifierWorkDirectoryMismatch");
            var inventory = G02ProofTranscript.Canonical(new[] { new
            {
                length = (long)inputs.SourceBytes.Length, path = "content/candidate.dfy", role = "proof-source",
                sha256 = Convert.ToHexStringLower(SHA256.HashData(inputs.SourceBytes))
            } });
            var sourcesDigest = OwnerAdmissionWire.Hash("strogo.proof.v0.2/proof-sources", inventory);
            var verified = G02ProofTranscript.NormalizeVerified(output.ExitCode, false, false,
                stdout, stderr, tool.Digest, sourcesDigest, inputs.Obligations);
            byte[]? generated = null;
            if (translate)
            {
                using var generatedHandle = G02WindowsHeldHandle.OpenFile(Path.Combine(work, "Generated.cs"),
                    heldDirectory.FinalPath + "\\Generated.cs");
                if (generatedHandle.Length is < 1 or > 8_388_608) throw Refuse("GeneratedSourceSizeInvalid");
                using var bytes = new MemoryStream();
                await generatedHandle.CopyToAsync(bytes);
                generated = bytes.ToArray();
                if (generated.Length is < 1 or > 8_388_608) throw Refuse("GeneratedSourceSizeInvalid");
                await File.WriteAllBytesAsync(Path.Combine(diagnosticDirectory, "Generated.cs"), generated);
                using var recordHandle = G02WindowsHeldHandle.OpenFile(Path.Combine(work, "Generated-cs.dtr"),
                    heldDirectory.FinalPath + "\\Generated-cs.dtr");
                if (recordHandle.Length > 1_048_576) throw Refuse("TranslationRecordSizeInvalid");
                using var recordBytes = new MemoryStream();
                await recordHandle.CopyToAsync(recordBytes);
                await File.WriteAllBytesAsync(Path.Combine(diagnosticDirectory, "translation-record.dtr"), recordBytes.ToArray());
            }
            completed = true;
            return new(verified, stdout, stderr, generated);
        }
        finally
        {
            // This directory was created here from a fixed temp root and a random single segment.
            if (Path.GetDirectoryName(work) == tempRoot.TrimEnd(Path.DirectorySeparatorChar))
            {
                var cleanupFailed = false;
                foreach (var directory in new[] { work, scratch })
                {
                    try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    { cleanupFailed = true; }
                }
                // Preserve an existing proof refusal; cleanup failure also prevents a successful receipt.
                if (cleanupFailed && completed) throw Refuse("VerifierCleanupFailed");
            }
        }
    }

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("package", code);
}
