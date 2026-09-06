using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Kernel.Conformance;

internal static class CliCases
{
    public static void Register(List<ConformanceCase> cases)
    {
        cases.Add(new("cli", "demo_and_restart_replay", ["AC10", "AC12"], DemoAsync));
        cases.Add(new("cli", "jsonl_closed_boundary_and_commit", ["AC1", "AC4", "AC6", "AC7", "AC10", "AC12"], SessionAsync));
        cases.Add(new("cli", "usage_errors_are_structured", ["AC10", "AC12"], UsageAsync));
    }

    private static string NewDirectory(TestContext context, string label)
    {
        string path = Path.Combine(Toolchain.Root(), "artifacts", "cli-cases", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        context.Evidence["directory"] = path;
        return path;
    }

    private static async Task DemoAsync(TestContext context)
    {
        string directory = NewDirectory(context, "demo");
        var first = await RunAsync("demo", "--directory", directory, "--json");
        context.Equal(0, first.ExitCode, "demo process succeeds: " + first.Stderr);
        using var doc = JsonDocument.Parse(first.Stdout);
        var result = doc.RootElement;
        context.Equal("Completed", result.GetProperty("status").GetString(), "demo reaches final outcome");
        context.Equal("10", result.GetProperty("before").GetProperty("available").GetString(), "initial quantity uses exact decimal string");
        context.Equal("7", result.GetProperty("after").GetProperty("available").GetString(), "one reservation reduces ten to seven");
        context.Equal("7", result.GetProperty("replay").GetProperty("output").GetProperty("available").GetString(), "replay returns same result");
        string receipt = result.GetProperty("receiptId").GetString()!;
        context.Equal(receipt, result.GetProperty("repeated").GetProperty("receipt").GetProperty("receiptId").GetString(), "retry returns original receipt");
        context.Equal(0, result.GetProperty("externalEffects").GetArrayLength(), "no external effect dispatch");
        context.True(File.Exists(Path.Combine(directory, "program.json")), "canonical source exported");
        context.True(File.Exists(Path.Combine(directory, "program.ir.json")), "derived IR exported");
        using var ir = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "program.ir.json")));
        context.Equal(6, ir.RootElement.GetProperty("instructions").GetArrayLength(), "six source nodes become six IR instructions");
        var second = await RunAsync("replay", "--directory", directory, "--event", "evt-001", "--json");
        context.Equal(0, second.ExitCode, "restart replay succeeds: " + second.Stderr);
        using var repeated = JsonDocument.Parse(second.Stdout);
        context.Equal(receipt, repeated.RootElement.GetProperty("receipt").GetProperty("receiptId").GetString(), "restart resolves persisted receipt");
        var third = await RunAsync("demo", "--directory", directory);
        context.Equal(0, third.ExitCode, "repeated demo does not try to reinitialize");
        context.True(third.Stdout.Contains("Состояние не сбрасывалось", StringComparison.Ordinal), "human output explains no reset");
        context.Evidence["receiptId"] = receipt;
        context.Evidence["jsonStdout"] = result.Clone();
    }

    private static async Task SessionAsync(TestContext context)
    {
        string directory = NewDirectory(context, "session");
        var init = await RunAsync("initialize", "--directory", directory, "--json");
        context.Equal(0, init.ExitCode, "trusted initialization succeeds: " + init.Stderr);
        using var process = Start("serve", "--directory", directory);
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            async Task<JsonElement> SendRaw(string json)
            {
                await process.StandardInput.WriteLineAsync(json);
                await process.StandardInput.FlushAsync();
                string? line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
                context.True(line is not null, "one response for every request");
                using var doc = JsonDocument.Parse(line!);
                return doc.RootElement.Clone();
            }
            Task<JsonElement> Send(string method, object arguments) => SendRaw(JsonSerializer.Serialize(new { schemaVersion = "kernel.v0", method, arguments }));
            static JsonElement Result(JsonElement envelope) => envelope.GetProperty("result");
            static string ErrorCode(JsonElement envelope) => envelope.GetProperty("error").GetProperty("code").GetString()!;

            var first = await Send("Snapshot", new { resourceId = "item-001" });
            context.Equal("Ok", first.GetProperty("status").GetString(), "snapshot succeeds");
            var snapshot = Result(first);
            context.Equal("10", snapshot.GetProperty("available").GetString(), "stock at genesis");
            string state = snapshot.GetProperty("stateRevision").GetString()!;
            string program = snapshot.GetProperty("programRevision").GetString()!;
            string policy = snapshot.GetProperty("policyRevision").GetString()!;
            var explain = await Send("Explain", new { artifactId = program });
            context.Equal("Ok", explain.GetProperty("status").GetString(), "agent can inspect program through authorized Explain");
            context.True(Result(explain).GetProperty("text").GetString()!.Length > 0, "computed human projection exists");

            var duplicate = await SendRaw("{\"schemaVersion\":\"kernel.v0\",\"method\":\"Snapshot\",\"method\":\"Commit\",\"arguments\":{\"resourceId\":\"item-001\"}}");
            context.Equal("DuplicateField", ErrorCode(duplicate), "duplicate field not last-wins");
            var authority = await SendRaw("{\"schemaVersion\":\"kernel.v0\",\"method\":\"Snapshot\",\"principal\":\"owner\",\"arguments\":{\"resourceId\":\"item-001\"}}");
            context.Equal("SchemaInvalid", ErrorCode(authority), "caller cannot provide identity");
            var hidden = await Send("RawCommit", new { newState = "0" });
            context.Equal("UnsupportedMethod", ErrorCode(hidden), "no arbitrary state write surface");

            object Envelope(object quantity, string id = "evt-wire") => new
            {
                @event = new { eventId = id, resourceId = "item-001", kind = "reserve", quantity },
                expectedStateRevision = state, expectedProgramRevision = program, expectedPolicyRevision = policy
            };
            var numeric = await Send("Prepare", Envelope(3));
            context.Equal("SchemaInvalid", ErrorCode(numeric), "I64 JSON numbers rejected");
            var negativeZero = await Send("Prepare", Envelope("-0"));
            context.Equal("InvalidI64", ErrorCode(negativeZero), "noncanonical negative zero rejected");
            var oversized = await SendRaw(new string(' ', 65537));
            context.Equal("TransportLimitExceeded", ErrorCode(oversized), "oversized line rejected without losing session");
            var prepared = await Send("Prepare", Envelope("3"));
            context.Equal("Ok", prepared.GetProperty("status").GetString(), "prepare succeeds");
            var preview = Result(prepared).GetProperty("prepared");
            string token = preview.GetProperty("prepareId").GetString()!;
            context.Equal("7", preview.GetProperty("output").GetProperty("available").GetString(), "plan computes seven");
            var beforeCommit = Result(await Send("Snapshot", new { resourceId = "item-001" }));
            context.Equal("10", beforeCommit.GetProperty("available").GetString(), "prepare does not write state");
            var commit = await Send("Commit", new { prepareId = token });
            context.Equal("Ok", commit.GetProperty("status").GetString(), "commit succeeds");
            var receipt = Result(commit).GetProperty("receipt");
            string receiptId = receipt.GetProperty("receiptId").GetString()!;
            var retry = await Send("Prepare", Envelope("3"));
            context.Equal(receiptId, Result(retry).GetProperty("receipt").GetProperty("receiptId").GetString(), "event retry with stale expectations returns original receipt");
            var conflict = await Send("Prepare", Envelope("4"));
            context.Equal("EventIdConflict", ErrorCode(conflict), "same ID with new payload refused");
            var final = Result(await Send("Snapshot", new { resourceId = "item-001" }));
            context.Equal("7", final.GetProperty("available").GetString(), "rejected requests and repeat did not debit again");
            var replay = await Send("Replay", new { receiptId });
            context.Equal("7", Result(replay).GetProperty("output").GetProperty("available").GetString(), "wire replay reconstructs result");
            context.Evidence["receiptId"] = receiptId;
            context.Evidence["finalSnapshot"] = final.Clone();
            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            context.Equal(0, process.ExitCode, "normal EOF exits serve cleanly");
            context.Equal(string.Empty, await stderr, "no unstructured server stderr");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static async Task UsageAsync(TestContext context)
    {
        var bad = await RunAsync("demo", "--solver-flags", "--no-contracts");
        context.Equal(2, bad.ExitCode, "unsupported setup flags fail");
        using var doc = JsonDocument.Parse(bad.Stderr);
        context.Equal("SetupOrUsageError", doc.RootElement.GetProperty("error").GetProperty("code").GetString(), "usage refusal structured");
        var help = await RunAsync("--help");
        context.Equal(0, help.ExitCode, "help does not need initialization");
        context.True(help.Stdout.Contains("JSON Lines", StringComparison.Ordinal), "help exposes actual agent transport");
    }

    private static Process Start(params string[] args)
    {
        string dll = Path.Combine(Toolchain.Root(), "src", "Kernel.Cli", "bin", "Release", "net10.0", "Kernel.Cli.dll");
        if (!File.Exists(dll)) throw new ConformanceException("Build the entire solution in Release before CLI conformance");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Toolchain.Root(), UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        start.ArgumentList.Add(dll);
        foreach (string arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new ConformanceException("Cannot start CLI process");
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(params string[] args)
    {
        using var process = Start(args);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            return (process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
