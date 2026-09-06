using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kernel.Core;

/// <summary>Host-owned configuration, never an agent message. The executable digest is a required external trust pin.</summary>
public sealed record TrustedSolverConfiguration(string ExecutablePath, string Version, string Sha256)
{
    public static TrustedSolverConfiguration Load(string configPath)
    {
        string absolute = Path.GetFullPath(configPath);
        using var document = CanonicalJson.ParseStrict(File.ReadAllText(absolute));
        var root = document.RootElement;
        Wire.Fields(root, "version", "executable", "sha256", "archiveSha256", "source");
        string version = Wire.String(root.GetProperty("version"));
        string executable = Wire.String(root.GetProperty("executable"));
        string digest = Wire.String(root.GetProperty("sha256")); Wire.RequireDigest(digest);
        Wire.RequireDigest(Wire.String(root.GetProperty("archiveSha256"))); Wire.String(root.GetProperty("source"));
        // The pinned repository format stores paths from the prototype root, one level above tools/z3.json.
        string prototypeRoot = Path.GetDirectoryName(Path.GetDirectoryName(absolute)) ?? throw new ArgumentException("Config must be under tools", nameof(configPath));
        return new(Path.GetFullPath(executable, prototypeRoot), version, digest);
    }
}

public sealed class Z3Solver : ISolver
{
    private readonly string executable;
    public SolverIdentity Identity { get; }
    private Z3Solver(string executable, string digest, string version)
    { this.executable = executable; Identity = new(version, digest); }

    public static async Task<Z3Solver> CreateFromConfigAsync(string configPath, CancellationToken cancellationToken = default)
    {
        var config = TrustedSolverConfiguration.Load(configPath);
        return await CreateAsync(config.ExecutablePath, config.Sha256, config.Version, cancellationToken);
    }

    public static async Task<Z3Solver> CreateAsync(string executablePath, string expectedDigest, string expectedVersion = "5.1.0", CancellationToken cancellationToken = default)
    {
        Wire.RequireDigest(expectedDigest); CanonicalJson.CheckAscii(expectedVersion);
        if (expectedVersion != "5.1.0") throw new KernelException(KernelError.Create("configuration", "SolverVersionMismatch"));
        var solver = new Z3Solver(Path.GetFullPath(executablePath), expectedDigest, expectedVersion);
        await solver.CheckDigestAsync(cancellationToken);
        var response = await solver.RunAsync(["-version"], null, TimeSpan.FromSeconds(5), cancellationToken);
        string actual = response.Stdout.Trim();
        if (response.TimedOut || response.ExitCode != 0 || !string.IsNullOrWhiteSpace(response.Stderr) ||
            !(actual == $"Z3 version {expectedVersion}" || actual.StartsWith($"Z3 version {expectedVersion} - ", StringComparison.Ordinal)))
            throw new KernelException(KernelError.Create("configuration", "SolverVersionMismatch", details: new { expectedVersion }));
        return solver;
    }

    private async Task CheckDigestAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var file = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            string digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
            if (digest != Identity.BinaryDigest) throw new KernelException(KernelError.Create("configuration", "SolverDigestMismatch"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new KernelException(KernelError.Create("configuration", "SolverUnavailable")); }
    }

    public async Task<SolverResponse> SolveAsync(string query, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(5)) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (Encoding.UTF8.GetByteCount(query) > 1024 * 1024) throw new ArgumentException("Query exceeds trusted encoder limit", nameof(query));
        await CheckDigestAsync(cancellationToken);
        return await RunAsync(["-in", "-smt2"], query, timeout, cancellationToken);
    }

    private async Task<SolverResponse> RunAsync(string[] arguments, string? input, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            if (!process.Start()) return new(-1, "", "StartFailure");
            Task<string> stdout = ReadBoundedAsync(process.StandardOutput, deadline.Token);
            Task<string> stderr = ReadBoundedAsync(process.StandardError, deadline.Token);
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            // Read tasks run concurrently: neither output pipe can block the solver while waiting for exit.
            await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr);
            return new(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            Kill(process); return new(-1, "", "", true);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Kill(process); return new(-1, "", exception.GetType().Name);
        }
    }
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096]; var result = new StringBuilder();
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0) return result.ToString();
            if (result.Length + count > 1024 * 1024) throw new IOException("SolverOutputLimitExceeded");
            result.Append(buffer, 0, count);
        }
    }
}
