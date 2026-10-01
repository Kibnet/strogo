using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Strogo.Modules;

/// <summary>Closed file inventory with retained read handles; it does not establish proof or admission.</summary>
public sealed class G02PackageSnapshot : IDisposable
{
    private readonly Dictionary<string, FileStream> handles;
    private readonly List<SafeFileHandle> directoryHandles;
    private readonly string rootDirectory;
    private readonly string finalRoot;
    private bool disposed;

    private G02PackageSnapshot(G02BuildManifest manifest, Dictionary<string, FileStream> handles,
        List<SafeFileHandle> directoryHandles, string rootDirectory, string finalRoot)
    {
        Manifest = manifest;
        this.handles = handles;
        this.directoryHandles = directoryHandles;
        this.rootDirectory = rootDirectory;
        this.finalRoot = finalRoot;
    }

    public G02BuildManifest Manifest { get; }

    public static G02PackageSnapshot OpenStructural(string packageDirectory)
        => OpenStructural(packageDirectory, null);

    internal static G02PackageSnapshot OpenStructural(string packageDirectory, Action? afterInventory)
    {
        if (!OperatingSystem.IsWindows()) throw Refuse("UnsupportedPackagePlatform");
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        var root = Path.GetFullPath(packageDirectory);
        var handles = new Dictionary<string, FileStream>(StringComparer.Ordinal);
        var directoryHandles = new List<SafeFileHandle>();
        try
        {
            var rootHandle = G02WindowsHeldHandle.OpenDirectory(root, null);
            directoryHandles.Add(rootHandle.Handle);
            var finalRoot = rootHandle.FinalPath;
            var rootNames = Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName)
                .Order(StringComparer.Ordinal).ToArray();
            if (!rootNames.SequenceEqual(new[] { "build-manifest.json", "content" }, StringComparer.Ordinal))
                throw Refuse("PackageRootInvalid");
            var contentRoot = Path.Combine(root, "content");
            directoryHandles.Add(G02WindowsHeldHandle.OpenDirectory(contentRoot,
                finalRoot + "\\content").Handle);
            var manifestPath = Path.Combine(root, "build-manifest.json");
            var manifestStream = G02WindowsHeldHandle.OpenFile(manifestPath,
                finalRoot + "\\build-manifest.json");
            handles.Add("build-manifest.json", manifestStream);
            var manifestBytes = ReadBounded(manifestStream, 1_048_576);
            var manifest = G02BuildManifest.Parse(manifestBytes);

            var actual = new List<string>();
            var actualDirectories = new List<string> { "content" };
            Enumerate(contentRoot, root, finalRoot, actual, actualDirectories, directoryHandles);
            actual.Sort(StringComparer.Ordinal);
            var declared = manifest.Files.Select(file => file.Path).ToArray();
            if (!actual.SequenceEqual(declared, StringComparer.Ordinal))
                throw Refuse("PackageInventoryMismatch");
            var neededDirectories = new HashSet<string>(StringComparer.Ordinal) { "content" };
            foreach (var file in declared)
            {
                var segments = file.Split('/');
                for (var count = 2; count < segments.Length; count++)
                    neededDirectories.Add(string.Join('/', segments.Take(count)));
            }
            if (!actualDirectories.Order(StringComparer.Ordinal)
                    .SequenceEqual(neededDirectories.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw Refuse("PackageDirectoryClosureInvalid");

            afterInventory?.Invoke();

            foreach (var file in manifest.Files)
            {
                var path = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));
                var stream = G02WindowsHeldHandle.OpenFile(path,
                    finalRoot + "\\" + file.Path.Replace('/', '\\'));
                handles.Add(file.Path, stream);
                if (stream.Length != file.Length || HashHeld(stream) != file.Sha256)
                    throw Refuse("PackageContentDigestMismatch");
            }
            // Persistent additions between enumeration and file holding must not leave a stale closed inventory.
            CheckClosedInventory(root, finalRoot, manifest, directoryHandles);
            return new(manifest, handles, directoryHandles, root, finalRoot);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            foreach (var handle in handles.Values) handle.Dispose();
            foreach (var handle in directoryHandles) handle.Dispose();
            throw Refuse("PackageIoRefused");
        }
        catch
        {
            foreach (var handle in handles.Values) handle.Dispose();
            foreach (var handle in directoryHandles) handle.Dispose();
            throw;
        }
    }

    public byte[] ReadHeld(string manifestPath)
    {
        if (disposed) throw Refuse("PackageSnapshotDisposed");
        if (!handles.TryGetValue(manifestPath, out var stream)) throw Refuse("PackageFileNotListed");
        if (stream.Length > 64 * 1024 * 1024) throw Refuse("PackageFileTooLargeForMemory");
        return ReadBounded(stream, 64 * 1024 * 1024);
    }

    /// <summary>Serialized-caller checkpoint only; not a concurrent lease, host ACL or admission check.</summary>
    public void Revalidate()
    {
        if (disposed) throw Refuse("PackageSnapshotDisposed");
        try
        {
            using var current = OpenStructural(rootDirectory);
            if (!current.finalRoot.Equals(finalRoot, StringComparison.OrdinalIgnoreCase))
                throw Refuse("PackageRootIdentityMismatch");
            if (current.Manifest.ArtifactDigest != Manifest.ArtifactDigest)
                throw Refuse("PackageManifestChanged");
            foreach (var file in Manifest.Files)
                if (handles[file.Path].Length != file.Length || HashHeld(handles[file.Path]) != file.Sha256)
                    throw Refuse("PackageContentDigestMismatch");
            if (disposed) throw Refuse("PackageSnapshotDisposed");
        }
        catch (ObjectDisposedException) { throw Refuse("PackageSnapshotDisposed"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw Refuse("PackageIoRefused"); }
    }

    private static void CheckClosedInventory(string root, string finalRoot, G02BuildManifest manifest,
        List<SafeFileHandle> directoryHandles)
    {
        var rootNames = Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName).Order(StringComparer.Ordinal);
        if (!rootNames.SequenceEqual(new[] { "build-manifest.json", "content" }, StringComparer.Ordinal))
            throw Refuse("PackageRootInvalid");
        var files = new List<string>();
        var directories = new List<string> { "content" };
        Enumerate(Path.Combine(root, "content"), root, finalRoot, files, directories, directoryHandles);
        if (!files.Order(StringComparer.Ordinal).SequenceEqual(manifest.Files.Select(file => file.Path), StringComparer.Ordinal))
            throw Refuse("PackageInventoryMismatch");
        var expected = new HashSet<string>(StringComparer.Ordinal) { "content" };
        foreach (var file in manifest.Files)
        {
            var segments = file.Path.Split('/');
            for (var count = 2; count < segments.Length; count++) expected.Add(string.Join('/', segments.Take(count)));
        }
        if (!directories.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw Refuse("PackageDirectoryClosureInvalid");
    }

    private static void Enumerate(string directory, string root, string finalRoot, List<string> files,
        List<string> directories, List<SafeFileHandle> directoryHandles)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
            G02BuildManifest.ValidatePath(relative);
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw Refuse("PackageReparsePointRejected");
            G02WindowsStreamGuard.Check(entry, (attributes & FileAttributes.Directory) != 0);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                directoryHandles.Add(G02WindowsHeldHandle.OpenDirectory(entry,
                    finalRoot + "\\" + relative.Replace('/', '\\')).Handle);
                directories.Add(relative);
                Enumerate(entry, root, finalRoot, files, directories, directoryHandles);
            }
            else files.Add(relative);
        }
    }

    private static string HashHeld(FileStream stream)
    {
        stream.Position = 0;
        var digest = Convert.ToHexStringLower(SHA256.HashData(stream));
        stream.Position = 0;
        return digest;
    }

    private static byte[] ReadBounded(FileStream stream, int limit)
    {
        if (stream.Length < 0 || stream.Length > limit) throw Refuse("PackageFileTooLargeForMemory");
        stream.Position = 0;
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        stream.Position = 0;
        return bytes;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var handle in handles.Values) handle.Dispose();
        foreach (var handle in directoryHandles) handle.Dispose();
        handles.Clear();
        directoryHandles.Clear();
    }

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("package", code);
}

internal static class G02WindowsHeldHandle
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint OpenExisting = 3;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint BackupSemantics = 0x02000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint Attributes;
        public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerial, SizeHigh, SizeLow, LinkCount, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder buffer, uint size, uint flags);

    internal static (SafeFileHandle Handle, string FinalPath) OpenDirectory(string path, string? expectedFinalPath)
    {
        var handle = Open(path, 0, true, expectedFinalPath);
        return (handle, FinalPath(handle));
    }

    internal static FileStream OpenFile(string path, string expectedFinalPath)
    {
        var handle = Open(path, GenericRead, false, expectedFinalPath);
        try { return new FileStream(handle, FileAccess.Read); }
        catch { handle.Dispose(); throw; }
    }

    private static SafeFileHandle Open(string path, uint access, bool directory, string? expectedFinalPath)
    {
        var handle = CreateFile(path, access, FileShareRead, IntPtr.Zero, OpenExisting,
            OpenReparsePoint | (directory ? BackupSemantics : 0), IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw ModulesExceptionFactory.Error("package", "PackageHandleOpenFailed");
        }
        try
        {
            if (!GetFileInformationByHandle(handle, out var info) ||
                (info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
                ((info.Attributes & (uint)FileAttributes.Directory) != 0) != directory)
                throw ModulesExceptionFactory.Error("package", "PackageHandleTypeInvalid");
            if (expectedFinalPath is not null && !FinalPath(handle).Equals(expectedFinalPath,
                    StringComparison.OrdinalIgnoreCase))
                throw ModulesExceptionFactory.Error("package", "PackageHandlePathMismatch");
            G02WindowsStreamGuard.Check(path, directory);
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    private static string FinalPath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity)
            throw ModulesExceptionFactory.Error("package", "PackageHandlePathUnavailable");
        return buffer.ToString();
    }
}

internal static class G02WindowsStreamGuard
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StreamData
    {
        public long Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
        public string Name;
    }

    [DllImport("kernel32.dll", EntryPoint = "FindFirstStreamW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstStream(string fileName, int infoLevel, out StreamData data, int flags);

    [DllImport("kernel32.dll", EntryPoint = "FindNextStreamW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextStream(IntPtr handle, out StreamData data);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr handle);

    internal static void Check(string path, bool directory)
    {
        var handle = FindFirstStream(path, 0, out var data, 0);
        if (handle == new IntPtr(-1))
        {
            if (directory && Marshal.GetLastWin32Error() == 38) return;
            throw ModulesExceptionFactory.Error("package", "PackageStreamEnumerationFailed");
        }
        try
        {
            do
            {
                if (data.Name != "::$DATA")
                    throw ModulesExceptionFactory.Error("package", "PackageAlternateStreamRejected");
            } while (FindNextStream(handle, out data));
            if (Marshal.GetLastWin32Error() != 38)
                throw ModulesExceptionFactory.Error("package", "PackageStreamEnumerationFailed");
        }
        finally { FindClose(handle); }
    }
}
