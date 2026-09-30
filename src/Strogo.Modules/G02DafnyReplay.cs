using System.Diagnostics;
using System.Security.Cryptography;

namespace Strogo.Modules;

internal sealed record G02FixtureReplay(G02VerifiedTranscript Verified, byte[] Stdout, byte[] Stderr);

/// <summary>Real verifier replay for fixtures. Production admission still requires host-owned ACL validation.</summary>
internal static class G02DafnyReplay
{
    internal static async Task<G02FixtureReplay> RunFixtureAsync(G02DafnyToolchain tool,
        G02RegeneratedProofInputs inputs, string diagnosticDirectory)
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
            foreach (var argument in G02DafnyToolchain.VerifierArguments) start.ArgumentList.Add(argument);
            G02VerifierProcess.ConfigureEnvironment(start, tool.DirectoryPath, scratch);
            var output = await G02VerifierProcess.RunAsync(start);
            var stdout = output.Stdout;
            var stderr = output.Stderr;
            Directory.CreateDirectory(diagnosticDirectory);
            await File.WriteAllBytesAsync(Path.Combine(diagnosticDirectory, "stdout.bin"), stdout);
            await File.WriteAllBytesAsync(Path.Combine(diagnosticDirectory, "stderr.bin"), stderr);
            await File.WriteAllBytesAsync(Path.Combine(diagnosticDirectory, "process.json"),
                G02ProofTranscript.Canonical(new { exitCode = output.ExitCode, timeout = false }));
            tool.Revalidate();
            if (!Directory.EnumerateFileSystemEntries(work).Select(Path.GetFileName)
                    .SequenceEqual(new[] { "candidate.dfy" }, StringComparer.Ordinal))
                throw Refuse("VerifierWorkDirectoryMismatch");
            var inventory = G02ProofTranscript.Canonical(new[] { new
            {
                length = (long)inputs.SourceBytes.Length, path = "content/candidate.dfy", role = "proof-source",
                sha256 = Convert.ToHexStringLower(SHA256.HashData(inputs.SourceBytes))
            } });
            var sourcesDigest = OwnerAdmissionWire.Hash("strogo.proof.v0.2/proof-sources", inventory);
            var verified = G02ProofTranscript.NormalizeVerified(output.ExitCode, false, false,
                stdout, stderr, tool.Digest, sourcesDigest, inputs.Obligations);
            completed = true;
            return new(verified, stdout, stderr);
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
