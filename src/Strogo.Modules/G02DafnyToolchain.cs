using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Kernel.Core;

namespace Strogo.Modules;

internal sealed record G02DafnyFile(string Path, string Sha256);

/// <summary>Retains and checks the pinned verifier closure. This is not a release admission.</summary>
internal sealed class G02DafnyToolchain : IDisposable
{
    internal const string InventoryDigest = "a1fcd83db15c8806190a5df8376142fb4c288e4b1d86a8e40b9eae3579fcba32";
    internal const string ArchiveDigest = "3653f05a111ca21e234709ea7b25ce96083fd6c6f10484256ba54110dbc0654d";
    internal const string ExecutableDigest = "aebb6e5ea4aa1a8ba5432ee214c47255fd7aa5a583f5cac8e83d27d9992c4dd0";
    internal static string[] VerifierArguments => ["verify", "candidate.dfy", "--enforce-determinism",
        "--cores", "2", "--verification-time-limit", "15"];

    private readonly Dictionary<string, FileStream> files = new(StringComparer.Ordinal);
    private readonly List<SafeFileHandle> directories = [];
    private readonly G02DafnyFile[] inventory;
    private string finalRoot = "";
    private bool disposed;

    private G02DafnyToolchain(string directory, G02DafnyFile[] inventory)
    {
        DirectoryPath = directory;
        this.inventory = inventory;
    }

    internal string DirectoryPath { get; }
    internal string ExecutablePath => System.IO.Path.Combine(DirectoryPath, "Dafny.exe");
    internal string Digest { get; private set; } = "";
    internal byte[] IdentityBytes { get; private set; } = [];

    internal static G02DafnyToolchain Open(string repositoryRoot)
    {
        if (!OperatingSystem.IsWindows()) throw Refuse("UnsupportedToolchainPlatform");
        var root = System.IO.Path.GetFullPath(repositoryRoot);
        var inventory = ParsePinnedInventory(File.ReadAllBytes(System.IO.Path.Combine(root, "tools", "dafny-files.json")));
        var tool = new G02DafnyToolchain(System.IO.Path.Combine(root, ".tools", "dafny", "dafny"), inventory);
        try
        {
            var heldRoot = G02WindowsHeldHandle.OpenDirectory(tool.DirectoryPath, null);
            tool.finalRoot = heldRoot.FinalPath;
            tool.directories.Add(heldRoot.Handle);
            var needed = inventory.SelectMany(file => Parents(file.Path)).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            foreach (var relative in needed)
                tool.directories.Add(G02WindowsHeldHandle.OpenDirectory(tool.At(relative),
                    heldRoot.FinalPath + "\\" + relative.Replace('/', '\\')).Handle);
            // Open every file before checking its bytes, closing the check-to-open race.
            foreach (var file in inventory)
                tool.files.Add(file.Path, G02WindowsHeldHandle.OpenFile(tool.At(file.Path),
                    heldRoot.FinalPath + "\\" + file.Path.Replace('/', '\\')));
            tool.Revalidate();
            using (var archive = new FileStream(System.IO.Path.Combine(root, ".tools", "downloads", "dafny-4.11.0.zip"),
                       FileMode.Open, FileAccess.Read, FileShare.Read))
                if (Hash(archive) != ArchiveDigest) throw Refuse("ToolchainArchiveMismatch");
            if (inventory.Single(file => file.Path == "Dafny.exe").Sha256 != ExecutableDigest)
                throw Refuse("ToolchainExecutableMismatch");
            tool.IdentityBytes = G02ProofTranscript.Canonical(new
            {
                schemaVersion = "strogo.proof-toolchain.v0.2", dafnyVersion = "4.11.0",
                dafnyArchiveSha256 = ArchiveDigest,
                dafnyFiles = inventory.Select(file => new { path = file.Path, sha256 = file.Sha256 }).ToArray(),
                verifierArguments = VerifierArguments,
                modulesAssemblySha256 = HashFile(typeof(ModulesDafnyLowerer).Assembly.Location),
                kernelCoreAssemblySha256 = HashFile(typeof(CanonicalJson).Assembly.Location)
            });
            tool.Digest = OwnerAdmissionWire.Hash("strogo.proof.v0.2/toolchain", tool.IdentityBytes);
            return tool;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            tool.Dispose();
            throw Refuse("ToolchainIoRefused");
        }
        catch { tool.Dispose(); throw; }
    }

    internal static G02DafnyFile[] ParsePinnedInventory(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() != 290)
                throw Refuse("ToolchainInventoryMismatch");
            var result = doc.RootElement.EnumerateArray().Select(item =>
            {
                OwnerAdmissionWire.Fields(item, "path", "sha256");
                var path = OwnerAdmissionWire.NonEmpty(item, "path");
                if (path.Contains('\\') || path.Contains(':') || path.Split('/').Any(segment =>
                        segment.Length == 0 || segment is "." or ".." ||
                        segment.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))))
                    throw Refuse("ToolchainInventoryMismatch");
                return new G02DafnyFile(path, OwnerAdmissionWire.Digest(item, "sha256"));
            }).OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
            if (result.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length)
                throw Refuse("ToolchainInventoryMismatch");
            CheckInventoryDigest(OwnerAdmissionWire.Hash("strogo.proof.v0.2/dafny-files",
                G02ProofTranscript.Canonical(result.Select(file => new { path = file.Path, sha256 = file.Sha256 }).ToArray())));
            return result;
        }
        catch (Exception error) when (error is JsonException || error is ModuleException { Stage: "admission" })
        { throw Refuse("ToolchainInventoryMismatch"); }
    }

    internal static void CheckInventoryDigest(string digest)
    {
        if (digest != InventoryDigest) throw Refuse("ToolchainInventoryMismatch");
    }

    internal void Revalidate()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var currentRoot = G02WindowsHeldHandle.OpenDirectory(DirectoryPath, finalRoot).Handle;
        var actual = new List<string>();
        var actualDirectories = new List<string>();
        Enumerate(DirectoryPath, actual, actualDirectories);
        if (!actual.Order(StringComparer.Ordinal).SequenceEqual(inventory.Select(file => file.Path), StringComparer.Ordinal))
            throw Refuse("ToolchainInventoryMismatch");
        if (!actualDirectories.Order(StringComparer.Ordinal).SequenceEqual(
                inventory.SelectMany(file => Parents(file.Path)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)))
            throw Refuse("ToolchainInventoryMismatch");
        foreach (var file in inventory)
        {
            using var currentPath = G02WindowsHeldHandle.OpenFile(At(file.Path),
                finalRoot + "\\" + file.Path.Replace('/', '\\'));
            if (Hash(currentPath) != file.Sha256) throw Refuse("ToolchainFileMismatch");
            if (Hash(files[file.Path]) != file.Sha256) throw Refuse("ToolchainFileMismatch");
        }
    }

    private void Enumerate(string directory, List<string> actual, List<string> actualDirectories)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw Refuse("ToolchainReparsePointRejected");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                actualDirectories.Add(System.IO.Path.GetRelativePath(DirectoryPath, entry).Replace('\\', '/'));
                Enumerate(entry, actual, actualDirectories);
            }
            else actual.Add(System.IO.Path.GetRelativePath(DirectoryPath, entry).Replace('\\', '/'));
        }
    }

    private string At(string path) => System.IO.Path.Combine(DirectoryPath, path.Replace('/', '\\'));
    private static IEnumerable<string> Parents(string path)
    {
        var parts = path.Split('/');
        for (var index = 1; index < parts.Length; index++) yield return string.Join('/', parts.Take(index));
    }
    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Hash(stream);
    }
    private static string Hash(FileStream stream)
    {
        stream.Position = 0;
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        stream.Position = 0;
        return hash;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var file in files.Values) file.Dispose();
        foreach (var directory in directories) directory.Dispose();
    }
    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("package", code);
}
