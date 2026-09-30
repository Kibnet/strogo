using System.Diagnostics;

namespace Strogo.Modules;

internal sealed record G02ProcessOutput(int ExitCode, byte[] Stdout, byte[] Stderr);

/// <summary>Shared bounded process execution for pinned replay and adversarial conformance probes.</summary>
internal static class G02VerifierProcess
{
    internal static void ConfigureEnvironment(ProcessStartInfo start, string toolDirectory, string scratch)
    {
        start.Environment.Clear();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        start.Environment["SystemRoot"] = windows;
        start.Environment["WINDIR"] = windows;
        start.Environment["TEMP"] = scratch;
        start.Environment["TMP"] = scratch;
        start.Environment["PATH"] = toolDirectory + Path.PathSeparator + Path.Combine(windows, "System32");
    }

    internal static async Task<G02ProcessOutput> RunAsync(ProcessStartInfo start, TimeSpan? shorterBudget = null)
    {
        var budget = shorterBudget ?? TimeSpan.FromSeconds(55);
        if (budget <= TimeSpan.Zero || budget > TimeSpan.FromSeconds(55))
            throw new ArgumentOutOfRangeException(nameof(shorterBudget));
        using var contained = G02ContainedProcess.Start(start);
        using var deadline = new CancellationTokenSource(budget);
        var stdoutTask = ReadBounded(contained.Stdout, contained.Kill, deadline.Token);
        var stderrTask = ReadBounded(contained.Stderr, contained.Kill, deadline.Token);
        try
        {
            await Task.WhenAll(contained.Process.WaitForExitAsync(deadline.Token), stdoutTask, stderrTask)
                .WaitAsync(deadline.Token);
        }
        catch
        {
            await contained.TerminateAndDrainAsync();
            contained.Stdout.Dispose();
            contained.Stderr.Dispose();
            if (deadline.IsCancellationRequested) throw Refuse("VerifierFailed");
            throw;
        }
        var output = new G02ProcessOutput(contained.Process.ExitCode, await stdoutTask, await stderrTask);
        await contained.TerminateAndDrainAsync();
        return output;
    }

    private static async Task<byte[]> ReadBounded(Stream stream, Action kill, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0) return output.ToArray();
            if (output.Length + count > 1_048_576)
            {
                kill();
                throw Refuse("VerifierOutputLimitExceeded");
            }
            output.Write(buffer, 0, count);
        }
    }

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("package", code);
}
