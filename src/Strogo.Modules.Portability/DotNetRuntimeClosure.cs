using System.Collections.Immutable;
using System.Globalization;
using Kernel.Core;

namespace Strogo.Modules.Portability;

public sealed record RuntimeClosureFile(string Path, string Sha256, string Length, string Role);

public sealed class DotNetRuntimeClosureReceipt
{
    private readonly byte[] inventoryBytes;

    internal DotNetRuntimeClosureReceipt(
        string os,
        string arch,
        string runtimeVersion,
        ImmutableArray<RuntimeClosureFile> files,
        byte[] inventoryBytes,
        string runtimeClosureDigest,
        string launcherDigest)
    {
        Os = os;
        Arch = arch;
        RuntimeVersion = runtimeVersion;
        Files = files;
        this.inventoryBytes = inventoryBytes.ToArray();
        RuntimeClosureDigest = runtimeClosureDigest;
        LauncherDigest = launcherDigest;
    }

    public string Os { get; }
    public string Arch { get; }
    public string RuntimeVersion { get; }
    public ImmutableArray<RuntimeClosureFile> Files { get; }
    public byte[] InventoryBytes => inventoryBytes.ToArray();
    public string RuntimeClosureDigest { get; }
    public string LauncherDigest { get; }
}

public static class DotNetRuntimeClosure
{
    public const string SchemaVersion = "strogo.runtime-inventory.v0.1";

    public static DotNetRuntimeClosureReceipt Capture(
        string dotnetExecutable,
        string os,
        string arch,
        string runtimeVersion,
        string? expectedRuntimeClosureDigest = null)
    {
        ArgumentNullException.ThrowIfNull(dotnetExecutable);
        ArgumentNullException.ThrowIfNull(os);
        ArgumentNullException.ThrowIfNull(arch);
        ArgumentNullException.ThrowIfNull(runtimeVersion);

        if (os is not ("windows" or "linux")) Unavailable("$/os", "UnsupportedPlatform");
        if (arch != "x64") Unavailable("$/arch", "UnsupportedArchitecture");
        if ((OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unsupported") != os)
            Unavailable("$/os", "HostPlatformMismatch");
        if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64)
            Unavailable("$/arch", "HostArchitectureMismatch");
        if (!IsVersion(runtimeVersion)) Unavailable("$/runtimeVersion", "InvalidRuntimeVersion");
        if (expectedRuntimeClosureDigest is not null && !IsDigest(expectedRuntimeClosureDigest))
            Unavailable("$/expectedRuntimeClosureDigest", "InvalidExpectedDigest");

        var executable = Path.GetFullPath(dotnetExecutable);
        var expectedExecutableName = os == "windows" ? "dotnet.exe" : "dotnet";
        if (!string.Equals(Path.GetFileName(executable), expectedExecutableName, StringComparison.Ordinal))
            Unavailable("$/launcher", "UnexpectedLauncherName");
        RequireRegularFile(executable, "launcher");
        var root = Path.GetDirectoryName(executable) ?? throw new InvalidOperationException("dotnet executable has no parent directory");
        RequireDirectory(root, "root");

        RequireOnlyVersion(Path.Combine(root, "host", "fxr"), runtimeVersion, "hostfxr-selector");
        RequireOnlyVersion(Path.Combine(root, "shared", "Microsoft.NETCore.App"), runtimeVersion, "shared-runtime-selector");
        var hostFxr = Path.Combine(root, "host", "fxr", runtimeVersion);
        var sharedRuntime = Path.Combine(root, "shared", "Microsoft.NETCore.App", runtimeVersion);
        RequireDirectory(hostFxr, "hostfxr");
        RequireDirectory(sharedRuntime, "shared-runtime");

        var files = new List<RuntimeClosureFile>
        {
            InventoryFile("launcher/" + expectedExecutableName, executable, "launcher")
        };
        AddDirectory(files, hostFxr, $"host/fxr/{runtimeVersion}", "host-component");
        AddDirectory(files, sharedRuntime, $"shared/Microsoft.NETCore.App/{runtimeVersion}", "runtime-component");
        var ordered = files.OrderBy(file => file.Path, StringComparer.Ordinal).ToImmutableArray();
        if (ordered.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() != ordered.Length ||
            ordered.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != ordered.Length)
            Unavailable("$/files", "RuntimePathCollision");

        var inventoryBytes = CanonicalJson.Encode(new
        {
            schemaVersion = SchemaVersion,
            profileId = PortabilityVersions.DotNetProfile,
            os,
            arch,
            files = ordered.Select(file => new { path = file.Path, sha256 = file.Sha256, length = file.Length, role = file.Role }).ToArray(),
            versions = new[]
            {
                new { component = "dotnet-hostfxr", version = runtimeVersion },
                new { component = "dotnet-runtime", version = runtimeVersion }
            }
        });
        var digest = PortabilityContract.DomainHash(
            $"strogo.portability.v0.1/runtime-closure/{PortabilityVersions.DotNetProfile}/{os}/{arch}",
            inventoryBytes);
        if (expectedRuntimeClosureDigest is not null && digest != expectedRuntimeClosureDigest)
            throw new PortabilityContractException("EnvironmentUnavailable", "$/runtimeClosureDigest", new { reason = "RuntimeClosureDigestMismatch", expected = expectedRuntimeClosureDigest, actual = digest });

        return new DotNetRuntimeClosureReceipt(
            os,
            arch,
            runtimeVersion,
            ordered,
            inventoryBytes,
            digest,
            CanonicalJson.RawDigest(File.ReadAllBytes(executable)));
    }

    private static void AddDirectory(List<RuntimeClosureFile> files, string physicalRoot, string logicalRoot, string role)
    {
        var initialCount = files.Count;
        var pending = new Stack<string>();
        pending.Push(physicalRoot);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            RequireDirectory(directory, "runtime-component");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Unavailable("$/files", "RuntimeEntryUnreadable");
                    throw;
                }
                if ((attributes & FileAttributes.ReparsePoint) != 0) Unavailable("$/files", "RuntimeReparsePoint");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }
                var relative = Path.GetRelativePath(physicalRoot, entry).Replace(Path.DirectorySeparatorChar, '/');
                files.Add(InventoryFile(logicalRoot + "/" + relative, entry, role));
            }
        }
        if (files.Count == initialCount) UnavailableComponent("$/files", "MissingRuntimeComponent", logicalRoot);
    }

    private static RuntimeClosureFile InventoryFile(string logicalPath, string physicalPath, string role)
    {
        RequireRegularFile(physicalPath, role);
        byte[] bytes;
        try { bytes = File.ReadAllBytes(physicalPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Unavailable("$/files", "RuntimeFileUnreadable");
            throw;
        }
        return new RuntimeClosureFile(
            logicalPath,
            CanonicalJson.RawDigest(bytes),
            bytes.LongLength.ToString(CultureInfo.InvariantCulture),
            role);
    }

    private static void RequireDirectory(string path, string component)
    {
        if (!Directory.Exists(path)) UnavailableComponent("$/files", "MissingRuntimeComponent", component);
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            UnavailableComponent("$/files", "RuntimeEntryUnreadable", component);
            throw;
        }
        if ((attributes & FileAttributes.ReparsePoint) != 0) UnavailableComponent("$/files", "RuntimeReparsePoint", component);
    }

    private static void RequireOnlyVersion(string selectorRoot, string runtimeVersion, string component)
    {
        RequireDirectory(selectorRoot, component);
        string[] entries;
        try { entries = Directory.EnumerateFileSystemEntries(selectorRoot).ToArray(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            UnavailableComponent("$/files", "RuntimeEntryUnreadable", component);
            throw;
        }
        if (entries.Length == 0) UnavailableComponent("$/files", "MissingRuntimeComponent", component);
        if (entries.Length != 1 || !string.Equals(Path.GetFileName(entries[0]), runtimeVersion, StringComparison.Ordinal) || !Directory.Exists(entries[0]))
            UnavailableComponent("$/files", "AmbiguousRuntimeSelection", component);
    }

    private static void RequireRegularFile(string path, string component)
    {
        if (!File.Exists(path)) UnavailableComponent("$/files", "MissingRuntimeComponent", component);
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            UnavailableComponent("$/files", "RuntimeEntryUnreadable", component);
            throw;
        }
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            UnavailableComponent("$/files", "UnsafeRuntimeComponent", component);
    }

    private static bool IsVersion(string value)
    {
        var parts = value.Split('.');
        return parts.Length == 3 && parts.All(part => part.Length > 0 && part.All(character => character is >= '0' and <= '9'));
    }

    private static bool IsDigest(string value) => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Unavailable(string locus, string reason)
        => throw new PortabilityContractException("EnvironmentUnavailable", locus, new { reason });

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void UnavailableComponent(string locus, string reason, string component)
        => throw new PortabilityContractException("EnvironmentUnavailable", locus, new { reason, component });
}
