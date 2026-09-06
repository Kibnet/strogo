using System.Diagnostics;
using System.Text.Json;

namespace Kernel.Conformance;

internal static class Program
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.FirstOrDefault() == "--child")
                return await HostCases.RunChildAsync(args.Skip(1).ToArray());

            string suite = Option(args, "--suite") ?? "all";
            if (suite is not ("all" or "core" or "host" or "cli"))
                throw new ArgumentException("--suite must be core, host, cli, or all");
            string? reportPath = Option(args, "--report");
            string? z3 = Option(args, "--z3");
            var cases = new List<ConformanceCase>();
            if (suite is "all" or "core") CoreCases.Register(cases);
            if (suite is "all" or "host") HostCases.Register(cases);
            if (suite is "all" or "cli") CliCases.Register(cases);
            if (cases.Count == 0) throw new InvalidOperationException("No conformance cases registered");

            var results = new List<CaseResult>();
            foreach (var test in cases)
            {
                var context = new TestContext(z3);
                var timer = Stopwatch.StartNew();
                string? failure = null;
                try { await test.Run(context); }
                catch (Exception ex) { failure = ex.ToString(); }
                timer.Stop();
                bool passed = failure is null && context.Assertions > 0;
                if (context.Assertions == 0 && failure is null) failure = "Case executed no assertions";
                var result = new CaseResult(test.Suite, test.Name, test.AcceptanceCriteria, passed,
                    context.Assertions, timer.Elapsed.TotalMilliseconds, context.Evidence, failure);
                results.Add(result);
                Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {test.Suite}/{test.Name} ({context.Assertions} assertions)");
                if (!passed) Console.Error.WriteLine(failure);
            }

            var report = new
            {
                schemaVersion = "kernel.conformance.v0", suite,
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                operatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                z3Path = z3,
                generatedAtUtc = DateTimeOffset.UtcNow,
                passed = results.All(r => r.Passed), testCount = results.Count,
                assertionCount = results.Sum(r => r.Assertions),
                passedCount = results.Count(r => r.Passed), failedCount = results.Count(r => !r.Passed),
                results
            };
            if (reportPath is not null)
            {
                string fullPath = Path.GetFullPath(reportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(report, JsonOptions));
            }
            Console.WriteLine($"{results.Count(r => r.Passed)}/{results.Count} cases; {results.Sum(r => r.Assertions)} assertions");
            return report.passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    internal static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Value required for {name}");
        return args[index + 1];
    }
}

internal sealed record ConformanceCase(string Suite, string Name, string[] AcceptanceCriteria, Func<TestContext, Task> Run);
internal sealed record CaseResult(string Suite, string Name, string[] AcceptanceCriteria, bool Passed, int Assertions,
    double ElapsedMilliseconds, Dictionary<string, object?> Evidence, string? Failure);

internal sealed class TestContext(string? z3Path)
{
    public string? Z3Path { get; } = z3Path;
    public int Assertions { get; private set; }
    public Dictionary<string, object?> Evidence { get; } = new(StringComparer.Ordinal);

    public void True(bool value, string message)
    {
        Assertions++;
        if (!value) throw new ConformanceException(message);
    }

    public void Equal<T>(T expected, T actual, string message)
    {
        Assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new ConformanceException($"{message}: expected {expected}, actual {actual}");
    }

    public void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    {
        Assertions++;
        if (!expected.SequenceEqual(actual))
            throw new ConformanceException($"{message}: expected [{string.Join(",", expected)}], actual [{string.Join(",", actual)}]");
    }

    public TException Throws<TException>(Action action, string message) where TException : Exception
    {
        Assertions++;
        try { action(); }
        catch (TException ex) { return ex; }
        catch (Exception ex) { throw new ConformanceException($"{message}: expected {typeof(TException).Name}, actual {ex.GetType().Name}", ex); }
        throw new ConformanceException($"{message}: did not throw {typeof(TException).Name}");
    }

    public async Task<TException> ThrowsAsync<TException>(Func<Task> action, string message) where TException : Exception
    {
        Assertions++;
        try { await action(); }
        catch (TException ex) { return ex; }
        catch (Exception ex) { throw new ConformanceException($"{message}: expected {typeof(TException).Name}, actual {ex.GetType().Name}", ex); }
        throw new ConformanceException($"{message}: did not throw {typeof(TException).Name}");
    }

    public string RequireZ3()
    {
        if (Z3Path is null)
            return Toolchain.PinnedZ3Path();
        if (string.IsNullOrWhiteSpace(Z3Path) || !File.Exists(Z3Path))
            throw new ConformanceException("Explicit --z3 path does not name an existing file");
        return Z3Path;
    }
}

internal sealed class ConformanceException(string message, Exception? inner = null) : Exception(message, inner);
