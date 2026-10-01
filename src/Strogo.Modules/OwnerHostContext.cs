using System.Text;
using System.Text.Json;

namespace Strogo.Modules;

/// <summary>Operator-owned bootstrap, not package admission. The host owns ACLs and a nonconcurrent lifetime.</summary>
public sealed class OwnerHostContext : IDisposable
{
    private readonly OwnerTrust trust;
    private readonly string stateDirectory;
    private bool disposed;

    private OwnerHostContext(OwnerTrust trust, string stateDirectory)
    {
        this.trust = trust;
        this.stateDirectory = stateDirectory;
    }

    /// <summary>Borrowed trust; keep this context alive until all consumers are closed.</summary>
    public OwnerTrust Trust { get { RequireLive(); return trust; } }
    public string OwnerStateDirectory { get { RequireLive(); return stateDirectory; } }

    public static OwnerHostContext Open(string operatorConfigPath)
    {
        try
        {
            var bytes = ReadBounded(Path.GetFullPath(operatorConfigPath), 65536,
                "OwnerConfigurationLimitExceeded", "OwnerConfigurationUnavailable");
            using var document = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(bytes),
                new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            string[] expected = ["schemaVersion", "keyId", "publicKeyPath", "ownerStateStore"];
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != expected.Length ||
                root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != expected.Length ||
                root.EnumerateObject().Any(property => !expected.Contains(property.Name, StringComparer.Ordinal)))
                throw Refuse("OwnerConfigurationInvalid");
            string Text(string field)
            {
                var value = root.GetProperty(field);
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    throw Refuse("OwnerConfigurationInvalid");
                return value.GetString()!;
            }
            string Absolute(string field)
            {
                var value = Text(field);
                if (!Path.IsPathFullyQualified(value)) throw Refuse("OperatorPathInvalid");
                return Path.GetFullPath(value);
            }
            if (Text("schemaVersion") != "strogo.owner-trust-config.v0.1")
                throw Refuse("OwnerConfigurationInvalid");
            var keyId = Text("keyId");
            var keyPath = Absolute("publicKeyPath");
            var stateDirectory = Absolute("ownerStateStore");
            if (!Directory.Exists(stateDirectory)) throw Refuse("OwnerConfigurationUnavailable");
            var publicKey = ReadBounded(keyPath, 16384,
                "OwnerPublicKeyLimitExceeded", "OwnerConfigurationUnavailable");
            return new(new OwnerTrust(publicKey, keyId), stateDirectory);
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or ArgumentException)
        {
            throw Refuse("OwnerConfigurationInvalid");
        }
    }

    /// <summary>Fresh bounded bytes, not a verified state. The caller must verify with this context's Trust.</summary>
    public byte[] ReadCurrentState()
    {
        RequireLive();
        return ReadBounded(Path.Combine(stateDirectory, "owner-state.json"), 65536,
            "OwnerStateLimitExceeded", "OwnerStateUnavailable");
    }

    private static byte[] ReadBounded(string path, int maximum, string limitCode, string unavailableCode)
    {
        try
        {
            // Sharing delete permits atomic epoch replacement; each open reads one file snapshot.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > maximum) throw Refuse(limitCode);
            var buffer = new byte[maximum + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = stream.Read(buffer, count, buffer.Length - count);
                if (read == 0) break;
                count += read;
            }
            if (count > maximum) throw Refuse(limitCode);
            return buffer.AsSpan(0, count).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw Refuse(unavailableCode);
        }
    }

    private void RequireLive()
    {
        if (disposed) throw Refuse("OwnerHostDisposed");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        trust.Dispose();
    }

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("owner", code);
}
