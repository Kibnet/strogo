using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Strogo.Modules;

internal static class ContainedLauncher
{
    internal static async Task Run(string requestPath, string resultPath)
    {
        var request = JsonSerializer.Deserialize<Request>(File.ReadAllBytes(requestPath)) ?? throw new Exception("launch request");
        var start = new ProcessStartInfo(request.FileName) { WorkingDirectory = request.WorkingDirectory, UseShellExecute = false };
        foreach (var argument in request.Arguments) start.ArgumentList.Add(argument);
        start.Environment.Clear();
        foreach (var pair in request.Environment) start.Environment[pair.Key] = pair.Value;
        using var child = G02ContainedProcess.Start(start);
        async Task<byte[]> Read(Stream stream)
        {
            try
            {
                using var bytes = new MemoryStream(); var buffer = new byte[8192];
                while (true)
                {
                    var count = await stream.ReadAsync(buffer);
                    if (count == 0) return bytes.ToArray();
                    if (bytes.Length + count > 1048576) throw new InvalidDataException("SignedObserverOutputLimitExceeded");
                    bytes.Write(buffer, 0, count);
                }
            }
            catch { child.Kill(); throw; }
        }
        var stdout = Read(child.Stdout); var stderr = Read(child.Stderr);
        var timedOut = !child.Process.WaitForExit(900000);
        try
        {
            if (timedOut) await child.TerminateAndDrainAsync();
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5));
            var utf8 = new UTF8Encoding(false, true);
            var result = new { pid = child.Process.Id, exitCode = child.Process.ExitCode, timedOut,
                stdout = utf8.GetString(await stdout), stderr = utf8.GetString(await stderr) };
            // Also remove descendants that closed their pipes before their parent exited.
            await child.TerminateAndDrainAsync();
            File.WriteAllBytes(resultPath, JsonSerializer.SerializeToUtf8Bytes(result));
        }
        catch
        {
            await child.TerminateAndDrainAsync();
            try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            throw;
        }
    }
    private sealed record Request(string FileName, string WorkingDirectory, string[] Arguments, Dictionary<string,string?> Environment);
}
