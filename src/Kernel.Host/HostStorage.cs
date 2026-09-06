using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kernel.Core;
using Microsoft.Data.Sqlite;

namespace Kernel.Host;

internal static class HostJson
{
    internal static readonly JsonSerializerOptions ReadOptions = new(CanonicalJson.SerializerOptions)
    { NumberHandling = JsonNumberHandling.AllowReadingFromString };
    internal static string Text(object value) => Encoding.UTF8.GetString(CanonicalJson.Encode(value));
    internal static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, ReadOptions)
        ?? throw KernelHost.Error("storage", "StorageCorrupt");
    internal static JsonElement Element(string json) { using var d = JsonDocument.Parse(json); return d.RootElement.Clone(); }
    internal static string Number(long n) => n.ToString(CultureInfo.InvariantCulture);
}

internal sealed record ResourceRow(string ResourceId, long Available, string StateRevision, string GenesisRevision,
    long InitialAvailable, string ProgramRevision, string PolicyRevision, string ManifestRevision, string AdmissionRef)
{
    internal ResourceSnapshot Snapshot => new(ResourceId, Available, StateRevision, ProgramRevision, PolicyRevision, ManifestRevision);
}

internal sealed class HostStorage
{
    private readonly string connectionString;
    private readonly int waitMilliseconds;
    internal HostStorage(string path, int waitMilliseconds)
    {
        this.waitMilliseconds = waitMilliseconds;
        var absolute = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = absolute, Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false, DefaultTimeout = Math.Max(1, (waitMilliseconds + 999) / 1000) }.ToString();
        using var c = Open();
        var version = Convert.ToInt32(Scalar(c, "PRAGMA user_version"), CultureInfo.InvariantCulture);
        if (version != 0 && version != 2) throw KernelHost.Error("storage", "SchemaVersionUnsupported");
        if (version == 0)
        {
            if (Convert.ToInt64(Scalar(c, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"), CultureInfo.InvariantCulture) != 0)
                throw KernelHost.Error("storage", "SchemaVersionUnsupported");
            Execute(c, "BEGIN IMMEDIATE");
            try
            {
                Execute(c, """
                    CREATE TABLE artifacts(ref TEXT PRIMARY KEY, kind TEXT NOT NULL, json TEXT NOT NULL, resource_id TEXT NOT NULL);
                    CREATE TABLE programs(ref TEXT PRIMARY KEY REFERENCES artifacts(ref), json TEXT NOT NULL);
                    CREATE TABLE admissions(ref TEXT PRIMARY KEY REFERENCES artifacts(ref), program_ref TEXT NOT NULL REFERENCES artifacts(ref), ir_ref TEXT NOT NULL REFERENCES artifacts(ref), policy_ref TEXT NOT NULL REFERENCES artifacts(ref), manifest_ref TEXT NOT NULL REFERENCES artifacts(ref), runtime_identity TEXT NOT NULL);
                    CREATE TABLE resources(resource_id TEXT PRIMARY KEY, available TEXT NOT NULL, state_revision TEXT NOT NULL REFERENCES artifacts(ref), genesis_revision TEXT NOT NULL REFERENCES artifacts(ref), initial_available TEXT NOT NULL, program_revision TEXT NOT NULL REFERENCES programs(ref), policy_revision TEXT NOT NULL REFERENCES artifacts(ref), manifest_revision TEXT NOT NULL REFERENCES artifacts(ref), admission_ref TEXT NOT NULL REFERENCES admissions(ref));
                    CREATE TABLE transitions(revision TEXT PRIMARY KEY REFERENCES artifacts(ref), resource_id TEXT NOT NULL REFERENCES resources(resource_id), previous_revision TEXT NOT NULL REFERENCES artifacts(ref), receipt_id TEXT NOT NULL UNIQUE REFERENCES artifacts(ref));
                    CREATE TABLE event_receipts(resource_id TEXT NOT NULL REFERENCES resources(resource_id), event_id TEXT NOT NULL, event_digest TEXT NOT NULL, receipt_id TEXT NOT NULL UNIQUE REFERENCES artifacts(ref), json TEXT NOT NULL, PRIMARY KEY(resource_id,event_id));
                    CREATE TABLE patch_receipts(program_id TEXT NOT NULL, patch_id TEXT NOT NULL, patch_digest TEXT NOT NULL, receipt_id TEXT NOT NULL UNIQUE REFERENCES artifacts(ref), json TEXT NOT NULL, PRIMARY KEY(program_id,patch_id));
                    CREATE TABLE retired_node_ids(program_id TEXT NOT NULL, node_id TEXT NOT NULL, PRIMARY KEY(program_id,node_id));
                    CREATE TABLE provenance(receipt_id TEXT PRIMARY KEY REFERENCES artifacts(ref), principal TEXT NOT NULL, recorded_at_utc TEXT NOT NULL, operation_kind TEXT NOT NULL);
                    PRAGMA user_version=2;
                    """);
                Execute(c, "COMMIT");
            }
            catch { Rollback(c); throw; }
        }
        Execute(c, "PRAGMA journal_mode=WAL");
        using var check = Command(c, "PRAGMA foreign_key_check");
        using var reader = check.ExecuteReader();
        if (reader.Read()) throw KernelHost.Error("storage", "StorageCorrupt", details: new { reason = "ForeignKeyViolation" });
    }

    internal SqliteConnection Open()
    {
        var c = new SqliteConnection(connectionString); c.Open();
        Execute(c, "PRAGMA foreign_keys=ON");
        Execute(c, "PRAGMA synchronous=FULL");
        Execute(c, $"PRAGMA busy_timeout={waitMilliseconds.ToString(CultureInfo.InvariantCulture)}");
        if (Convert.ToInt32(Scalar(c, "PRAGMA foreign_keys"), CultureInfo.InvariantCulture) != 1)
        { c.Dispose(); throw KernelHost.Error("storage", "StorageCorrupt"); }
        return c;
    }
    internal static SqliteCommand Command(SqliteConnection c, string sql, params (string Name, object? Value)[] p)
    {
        var command = c.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in p) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    internal static int Execute(SqliteConnection c, string sql, params (string Name, object? Value)[] p)
    { using var command = Command(c, sql, p); return command.ExecuteNonQuery(); }
    internal static object? Scalar(SqliteConnection c, string sql, params (string Name, object? Value)[] p)
    { using var command = Command(c, sql, p); return command.ExecuteScalar(); }
    internal static void Rollback(SqliteConnection c) { try { Execute(c, "ROLLBACK"); } catch (SqliteException) { } }
    internal static ResourceRow? Resource(SqliteConnection c)
    {
        using var command = Command(c, "SELECT resource_id,available,state_revision,genesis_revision,initial_available,program_revision,policy_revision,manifest_revision,admission_ref FROM resources LIMIT 1");
        using var r = command.ExecuteReader();
        return !r.Read() ? null : new(r.GetString(0), long.Parse(r.GetString(1), CultureInfo.InvariantCulture), r.GetString(2), r.GetString(3), long.Parse(r.GetString(4), CultureInfo.InvariantCulture), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8));
    }
    internal static void Artifact(SqliteConnection c, string reference, string kind, string json, string resourceId)
    {
        if (CanonicalJson.Hash(kind, HostJson.Element(json)) != reference) throw KernelHost.Error("storage", "ArtifactDigestMismatch");
        Execute(c, "INSERT OR IGNORE INTO artifacts(ref,kind,json,resource_id) VALUES($r,$k,$j,$id)", ("$r", reference), ("$k", kind), ("$j", json), ("$id", resourceId));
        var existing = GetArtifact(c, reference);
        if (existing.Kind != kind || existing.Json != json || existing.ResourceId != resourceId) throw KernelHost.Error("storage", "ArtifactDigestMismatch");
    }
    internal static (string Kind, string Json, string ResourceId) GetArtifact(SqliteConnection c, string reference, string missingCode = "StorageCorrupt")
    {
        using var command = Command(c, "SELECT kind,json,resource_id FROM artifacts WHERE ref=$r", ("$r", reference));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw KernelHost.Error("storage", missingCode, details: new { reason = "MissingArtifact" });
        var kind = reader.GetString(0); var json = reader.GetString(1); var id = reader.GetString(2);
        try
        {
            if (CanonicalJson.Hash(kind, HostJson.Element(json)) != reference) throw KernelHost.Error("replay", missingCode == "ReplayUnavailable" ? "ReplayMismatch" : "ArtifactDigestMismatch");
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        { throw KernelHost.Error("replay", missingCode == "ReplayUnavailable" ? "ReplayMismatch" : "ArtifactDigestMismatch"); }
        return (kind, json, id);
    }
    internal static HostPolicy Policy(SqliteConnection c, ResourceRow r) => HostJson.Read<HostPolicy>(GetArtifact(c, r.PolicyRevision).Json);
    internal static HostManifest Manifest(SqliteConnection c, ResourceRow r) => HostJson.Read<HostManifest>(GetArtifact(c, r.ManifestRevision).Json);
    internal static EventReceipt? Receipt(SqliteConnection c, string resourceId, string eventId)
    {
        var value = Scalar(c, "SELECT json FROM event_receipts WHERE resource_id=$r AND event_id=$e", ("$r", resourceId), ("$e", eventId));
        return value is string json ? HostJson.Read<EventReceipt>(json) : null;
    }
    internal static void AddProvenance(SqliteConnection c, string receiptId, string principal, string operationKind)
        => Execute(c, "INSERT INTO provenance(receipt_id,principal,recorded_at_utc,operation_kind) VALUES($id,$p,$t,$k)",
            ("$id", receiptId), ("$p", principal), ("$t", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)), ("$k", operationKind));
    internal static Provenance ReadProvenance(SqliteConnection c, string receiptId)
    {
        using var command = Command(c, "SELECT principal,recorded_at_utc,operation_kind FROM provenance WHERE receipt_id=$id", ("$id", receiptId));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw KernelHost.Error("storage", "StorageCorrupt", details: new { reason = "MissingProvenance" });
        return new(reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }
}
