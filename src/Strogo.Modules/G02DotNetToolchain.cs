using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Strogo.Modules;

internal sealed record G02SdkFile(string Path, string Sha256);

/// <summary>Retains and checks the pinned SDK closure. This is not a complete build recipe or release admission.</summary>
internal sealed class G02DotNetToolchain : IDisposable
{
    internal const string InventoryDigest = "cb488e756dab841e56cd23f28550fa59d3c443540537648321dfc15a6af9e9d6";
    internal const string ArchiveDigest = "83fbf0699a984d08063579b06edfc8c327af62234b1aaeeadc5d7ae7f5a0514a";
    internal const string ExecutableDigest = "ab1b71fd3dd71062e074c9fab8312081a81b7f2b3e0327c48c4d249c8d1a3135";
    internal const string SdkVersion = "10.0.400";
    internal const string RuntimeVersion = "10.0.11";
    // The installed SDK has this empty directory in addition to ZIP-derived file parents.
    // It is explicit and held; arbitrary empty directories and all unlisted files still refuse.
    private static readonly string[] InstallationEmptyDirectories = ["metadata"];

    private readonly Dictionary<string, FileStream> files = new(StringComparer.Ordinal);
    private readonly List<SafeFileHandle> directories = [];
    private readonly G02SdkFile[] inventory;
    private string finalRoot = "";
    private bool disposed;

    private G02DotNetToolchain(string directory, G02SdkFile[] inventory)
    {
        DirectoryPath = directory;
        this.inventory = inventory;
    }

    internal string DirectoryPath { get; }
    internal string ExecutablePath => System.IO.Path.Combine(DirectoryPath, "dotnet.exe");
    internal string Digest { get; private set; } = "";
    internal byte[] IdentityBytes { get; private set; } = [];

    internal static G02DotNetToolchain Open(string repositoryRoot)
    {
        if (!OperatingSystem.IsWindows()) throw Refuse("UnsupportedBuildToolchainPlatform");
        var root = System.IO.Path.GetFullPath(repositoryRoot);
        G02DotNetToolchain? tool = null;
        try
        {
            var inventory = ParsePinnedInventory(File.ReadAllBytes(System.IO.Path.Combine(root, "tools", "dotnet-sdk-files.json")));
            tool = new G02DotNetToolchain(System.IO.Path.Combine(root, ".tools", "dotnet-sdk-10.0.400"), inventory);
            var heldRoot = G02WindowsHeldHandle.OpenDirectory(tool.DirectoryPath, null);
            tool.finalRoot = heldRoot.FinalPath;
            tool.directories.Add(heldRoot.Handle);
            var needed = inventory.SelectMany(file => Parents(file.Path)).Concat(InstallationEmptyDirectories).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            foreach (var relative in needed)
                tool.directories.Add(G02WindowsHeldHandle.OpenDirectory(tool.At(relative),
                    heldRoot.FinalPath + "\\" + relative.Replace('/', '\\')).Handle);
            // Open every file before checking its bytes, closing the check-to-open race.
            foreach (var file in inventory)
                tool.files.Add(file.Path, G02WindowsHeldHandle.OpenFile(tool.At(file.Path),
                    heldRoot.FinalPath + "\\" + file.Path.Replace('/', '\\')));
            tool.Revalidate();
            using (var archive = new FileStream(System.IO.Path.Combine(root, ".tools", "downloads", "dotnet-sdk-10.0.400-win-x64.zip"),
                       FileMode.Open, FileAccess.Read, FileShare.Read))
                if (Hash(archive) != ArchiveDigest) throw Refuse("BuildToolchainArchiveMismatch");
            if (inventory.Single(file => file.Path == "dotnet.exe").Sha256 != ExecutableDigest)
                throw Refuse("BuildToolchainExecutableMismatch");
            tool.IdentityBytes = G02ProofTranscript.Canonical(new
            {
                schemaVersion = "strogo.sdk-closure.v0.2", sdkVersion = SdkVersion,
                runtimeVersion = RuntimeVersion, runtimeIdentifier = "win-x64",
                sdkArchiveSha256 = ArchiveDigest,
                installationEmptyDirectories = InstallationEmptyDirectories,
                sdkFiles = inventory.Select(file => new { path = file.Path, sha256 = file.Sha256 }).ToArray()
            });
            tool.Digest = OwnerAdmissionWire.Hash("strogo.build.v0.2/sdk-closure", tool.IdentityBytes);
            return tool;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            tool?.Dispose();
            throw Refuse("BuildToolchainIoRefused");
        }
        catch { tool?.Dispose(); throw; }
    }

    internal static G02SdkFile[] ParsePinnedInventory(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() != 5577)
                throw Refuse("BuildToolchainInventoryMismatch");
            var result = doc.RootElement.EnumerateArray().Select(item =>
            {
                OwnerAdmissionWire.Fields(item, "path", "sha256");
                var path = OwnerAdmissionWire.NonEmpty(item, "path");
                if (path.Contains('\\') || path.Contains(':') || path.Split('/').Any(segment =>
                        segment.Length == 0 || segment is "." or ".." ||
                        segment.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))))
                    throw Refuse("BuildToolchainInventoryMismatch");
                return new G02SdkFile(path, OwnerAdmissionWire.Digest(item, "sha256"));
            }).OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
            if (result.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length)
                throw Refuse("BuildToolchainInventoryMismatch");
            CheckInventoryDigest(OwnerAdmissionWire.Hash("strogo.build.v0.2/sdk-files",
                G02ProofTranscript.Canonical(result.Select(file => new { path = file.Path, sha256 = file.Sha256 }).ToArray())));
            return result;
        }
        catch (Exception error) when (error is JsonException || error is ModuleException { Stage: "admission" })
        { throw Refuse("BuildToolchainInventoryMismatch"); }
    }

    internal static void CheckInventoryDigest(string digest)
    {
        if (digest != InventoryDigest) throw Refuse("BuildToolchainInventoryMismatch");
    }

    internal void Revalidate()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try { RevalidateCore(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw Refuse("BuildToolchainIoRefused"); }
    }

    private void RevalidateCore()
    {
        using var currentRoot = G02WindowsHeldHandle.OpenDirectory(DirectoryPath, finalRoot).Handle;
        var actual = new List<string>();
        var actualDirectories = new List<string>();
        Enumerate(DirectoryPath, actual, actualDirectories);
        if (!actual.Order(StringComparer.Ordinal).SequenceEqual(inventory.Select(file => file.Path), StringComparer.Ordinal))
            throw Refuse("BuildToolchainInventoryMismatch");
        if (!actualDirectories.Order(StringComparer.Ordinal).SequenceEqual(
                inventory.SelectMany(file => Parents(file.Path)).Concat(InstallationEmptyDirectories)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)))
            throw Refuse("BuildToolchainInventoryMismatch");
        foreach (var file in inventory)
        {
            using var currentPath = G02WindowsHeldHandle.OpenFile(At(file.Path),
                finalRoot + "\\" + file.Path.Replace('/', '\\'));
            if (Hash(currentPath) != file.Sha256) throw Refuse("BuildToolchainFileMismatch");
            if (Hash(files[file.Path]) != file.Sha256) throw Refuse("BuildToolchainFileMismatch");
        }
    }

    private void Enumerate(string directory, List<string> actual, List<string> actualDirectories)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw Refuse("BuildToolchainReparsePointRejected");
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

