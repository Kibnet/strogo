using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kernel.Core;

namespace Kernel.Conformance;

internal static class Toolchain
{
    public static string Root()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(Path.Combine(directory.FullName, "src", "Kernel.Core")))
                    return directory.FullName;
        throw new ConformanceException("Cannot locate prototype root with global.json and src/Kernel.Core");
    }

    public static string PinnedZ3Path()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "tools", "z3.json")));
        return Path.GetFullPath(config.RootElement.GetProperty("executable").GetString()!, Root());
    }

    public static async Task<ISolver> SolverAsync(TestContext context)
    {
        string path = context.RequireZ3();
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "tools", "z3.json")));
        string digest = config.RootElement.GetProperty("sha256").GetString()!;
        string version = config.RootElement.GetProperty("version").GetString()!;
        context.Equal(digest, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant(), "Z3 binary matches trusted manifest");
        return await Z3Solver.CreateAsync(path, digest, version);
    }
}

internal static class Fixtures
{
    public const string ReserveJson = """
        {
          "schemaVersion":"kernel.v0","programId":"reserve","profileId":"reserve.v0",
          "nodes":[
            {"id":"n.available","op":"input","type":"I64","args":[],"fieldId":"state.available"},
            {"id":"n.quantity","op":"input","type":"I64","args":[],"fieldId":"event.quantity"},
            {"id":"n.zero","op":"i64.const","type":"I64","args":[],"value":"0"},
            {"id":"n.enough","op":"i64.le","type":"Bool","args":["n.quantity","n.available"]},
            {"id":"n.debit","op":"select","type":"I64","args":["n.enough","n.quantity","n.zero"]},
            {"id":"n.remaining","op":"i64.sub_checked","type":"I64","args":["n.available","n.debit"]}
          ],
          "outputs":{"accepted":"n.enough","available":"n.remaining","reserved":"n.debit"}
        }
        """;

    public static KernelProgram Reserve() => ProgramCodec.Parse(ReserveJson);
    public static KernelNode I64(string id, long value) => new(id, "i64.const", KernelType.I64, [], I64Value: value);
    public static KernelNode Bool(string id, bool value) => new(id, "bool.const", KernelType.Bool, [], BoolValue: value);
    public static KernelNode Input(string id, string field) => new(id, "input", KernelType.I64, [], FieldId: field);
    public static KernelNode Op(string id, string op, KernelType type, params string[] args) => new(id, op, type, [.. args]);
    public static KernelProgram Graph(IEnumerable<KernelNode> nodes, OutputRefs outputs) => new(KernelVersions.Schema, "reserve", "reserve.v0", [.. nodes], outputs);
    public static KernelProgram Replace(KernelProgram program, KernelNode node) => program with { Nodes = [.. program.Nodes.Select(n => n.Id == node.Id ? node : n)] };
    public static KernelProgram BadAdd() => Replace(Reserve(), Op("n.remaining", "i64.add_checked", KernelType.I64, "n.available", "n.debit"));
    public static KernelProgram Equivalent()
    {
        var program = Reserve();
        program = program with { Nodes = program.Nodes.Add(Op("n.notenough", "bool.not", KernelType.Bool, "n.enough")) };
        return Replace(program, Op("n.debit", "select", KernelType.I64, "n.notenough", "n.zero", "n.quantity"));
    }

    public static KernelProgram Arithmetic(string opcode, long left, long right)
        => Graph([I64("a", left), I64("b", right), Op("c", opcode, KernelType.I64, "a", "b"), Bool("yes", true)], new("yes", "c", "a"));

    public static KernelProgram StrictOverflow() => Graph([
        I64("a", long.MaxValue), I64("b", 1), Op("overflow", "i64.add_checked", KernelType.I64, "a", "b"),
        Bool("false", false), I64("zero", 0), Op("selection", "select", KernelType.I64, "false", "overflow", "zero")],
        new("false", "selection", "zero"));

    public static ReserveOutput BusinessOracle(long available, long quantity) => quantity <= available
        ? new(true, checked(available - quantity), quantity) : new(false, available, 0);

    public static string Text(KernelProgram program) => Encoding.UTF8.GetString(ProgramCodec.CanonicalBytes(program));
    public static string IndependentHash(string domain, byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"kernel.v0/{domain}\n").Concat(bytes).ToArray())).ToLowerInvariant();

    public static void Error(TestContext context, string expectedCode, Action action, string message)
    {
        var exception = context.Throws<KernelException>(action, message);
        context.Equal(expectedCode, exception.Error.Code, message + " code");
    }
}

internal sealed class StubSolver(Func<string, SolverResponse> answer) : ISolver
{
    public SolverIdentity Identity { get; } = new("test.fake", new string('a', 64));
    public List<string> Queries { get; } = [];
    public Task<SolverResponse> SolveAsync(string query, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Queries.Add(query);
        return Task.FromResult(answer(query));
    }
}
