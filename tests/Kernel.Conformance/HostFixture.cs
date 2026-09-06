using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Core;
using Kernel.Host;
using Microsoft.Data.Sqlite;

namespace Kernel.Conformance;

internal sealed class HostFixture
{
    private HostFixture(string directory, string database, ISolver solver, KernelHost host, HostPolicy policy)
    {
        Directory = directory; Database = database; Solver = solver; Host = host; Policy = policy;
    }
    public string Directory { get; }
    public string Database { get; }
    public ISolver Solver { get; }
    public KernelHost Host { get; private set; }
    public HostPolicy Policy { get; }
    public KernelClient Client => Host.Bind("agent");

    public static async Task<HostFixture> CreateAsync(TestContext c, long available = 10, HostOptions? options = null, HostPolicy? policy = null,
        ISolver? trustedTestSolver = null, string? programJson = null)
    {
        string directory = Path.Combine(Toolchain.Root(), "artifacts", "conformance-data", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        string database = Path.Combine(directory, "kernel.sqlite");
        var solver = trustedTestSolver ?? await Toolchain.SolverAsync(c);
        var host = await KernelHost.OpenAsync(database, solver, options);
        var actualPolicy = policy ?? HostPolicy.Default();
        await host.InitializeAsync(programJson ?? Fixtures.ReserveJson, available, actualPolicy);
        c.Evidence["database"] = database;
        return new HostFixture(directory, database, solver, host, actualPolicy);
    }

    public async Task ReopenAsync(HostOptions? options = null)
    {
        await Host.DisposeAsync();
        Host = await KernelHost.OpenAsync(Database, Solver, options);
    }

    public PrepareRequest Request(string eventId, long quantity, ResourceSnapshot? snapshot = null)
    {
        var s = snapshot ?? Client.Snapshot();
        return new(new(eventId, s.ResourceId, "reserve", quantity), s.StateRevision, s.ProgramRevision, s.PolicyRevision);
    }

    public async Task<CommitResult> ReserveAsync(string eventId, long quantity)
    {
        var prepared = await Client.PrepareAsync(Request(eventId, quantity));
        if (prepared.Prepared is null) throw new ConformanceException("Expected fresh prepare");
        return await Client.CommitAsync(prepared.Prepared.PrepareId);
    }

    public string SnapshotDatabase()
    {
        using var connection = OpenSql();
        var image = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        using var names = connection.CreateCommand();
        names.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        var tables = new List<string>();
        using (var reader = names.ExecuteReader()) while (reader.Read()) tables.Add(reader.GetString(0));
        foreach (string table in tables)
        {
            using var query = connection.CreateCommand();
            query.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\"";
            using var reader = query.ExecuteReader();
            var rows = new List<string>();
            while (reader.Read())
            {
                var row = new SortedDictionary<string, object?>(StringComparer.Ordinal);
                for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(JsonSerializer.Serialize(row));
            }
            rows.Sort(StringComparer.Ordinal);
            image[table] = rows;
        }
        return JsonSerializer.Serialize(image);
    }

    public long Count(string table)
    {
        if (table is not ("event_receipts" or "transitions" or "patch_receipts" or "retired_node_ids" or "artifacts")) throw new ArgumentException(nameof(table));
        using var connection = OpenSql();
        using var query = connection.CreateCommand(); query.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)query.ExecuteScalar()!;
    }

    public void Sql(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = OpenSql();
        using var query = connection.CreateCommand(); query.CommandText = sql;
        foreach (var p in parameters) query.Parameters.AddWithValue(p.Name, p.Value);
        query.ExecuteNonQuery();
    }

    public string Artifact(string reference)
    {
        using var connection = OpenSql(); using var query = connection.CreateCommand();
        query.CommandText = "SELECT json FROM artifacts WHERE ref=$ref"; query.Parameters.AddWithValue("$ref", reference);
        return (string?)query.ExecuteScalar() ?? throw new ConformanceException("Artifact not found " + reference);
    }

    private SqliteConnection OpenSql()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        connection.Open(); return connection;
    }
}

internal static class PatchFixture
{
    public static object Wire(KernelNode node)
    {
        var wire = new Dictionary<string, object?> { ["id"] = node.Id, ["op"] = node.Op, ["type"] = node.Type.ToString(), ["args"] = node.Args };
        if (node.FieldId is not null) wire["fieldId"] = node.FieldId;
        if (node.I64Value is not null) wire["value"] = node.I64Value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (node.BoolValue is not null) wire["value"] = node.BoolValue.Value;
        return wire;
    }

    public static string Patch(string id, ResourceSnapshot snapshot, params object[] operations) => JsonSerializer.Serialize(new
    {
        schemaVersion = "kernel.v0", patchId = id, programId = "reserve", baseProgramRevision = snapshot.ProgramRevision,
        expectedPolicyRevision = snapshot.PolicyRevision, operations
    });
    public static object Add(KernelNode node) => new { op = "AddNode", node = Wire(node) };
    public static object Replace(KernelNode oldNode, KernelNode node) => new { op = "ReplaceNode", nodeId = oldNode.Id, expectedNodeRevision = ProgramCodec.NodeRevision(oldNode), node = Wire(node) };
    public static object Remove(KernelNode node) => new { op = "RemoveNode", nodeId = node.Id, expectedNodeRevision = ProgramCodec.NodeRevision(node) };
    public static object Outputs(OutputRefs oldRefs, OutputRefs refs) => new { op = "SetOutputs", expectedOutputsDigest = ProgramCodec.OutputsDigest(oldRefs), outputs = new { accepted = refs.Accepted, available = refs.Available, reserved = refs.Reserved } };

    public static string Equivalent(string id, ResourceSnapshot snapshot)
    {
        var original = Fixtures.Reserve(); var equivalent = Fixtures.Equivalent();
        return Patch(id, snapshot, Add(equivalent.Nodes.Single(n => n.Id == "n.notenough")),
            Replace(original.Nodes.Single(n => n.Id == "n.debit"), equivalent.Nodes.Single(n => n.Id == "n.debit")));
    }
    public static string NoChange(string id, ResourceSnapshot snapshot, KernelProgram? program = null)
    {
        var node = (program ?? Fixtures.Reserve()).Nodes.Single(n => n.Id == "n.zero");
        return Patch(id, snapshot, Replace(node, node));
    }

    public static string ChangeJson(string json, Action<JsonObject> change)
    {
        var root = JsonNode.Parse(json)!.AsObject(); change(root); return root.ToJsonString();
    }
}

internal sealed class AsyncBarrier(string point)
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Entered => entered.Task;
    public async Task Hook(string actual)
    {
        if (actual != point) return;
        entered.TrySetResult();
        await released.Task;
    }
    public void Release() => released.TrySetResult();
    public async Task WaitAsync() => await Entered.WaitAsync(TimeSpan.FromSeconds(15));
}
