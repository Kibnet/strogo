using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Strogo.Modules;

internal sealed record G02LoadedPlatformAssembly(string Name, string Location, bool DefaultContext);
internal sealed record G02RuntimeObservation(bool Windows, Architecture Architecture, string RuntimeDirectory,
    string CoreLibPath, ImmutableArray<string> TrustedAssemblyPaths,
    ImmutableArray<G02LoadedPlatformAssembly> ManagedAssemblies, ImmutableArray<string> NativeModulePaths);

/// <summary>Host-owned pinned file/path binding. Borrowed SDK, nonconcurrent; not memory attestation or admission.</summary>
internal sealed class G02RuntimeBinding
{
    private readonly G02DotNetToolchain sdk;
    private readonly Func<G02RuntimeObservation> observe;
    private readonly Dictionary<string, string> managed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> dlls = new(StringComparer.OrdinalIgnoreCase);
    private readonly string runtimeDirectory;
    private readonly string coreLib;
    private readonly string hostFxr;

    private G02RuntimeBinding(G02DotNetToolchain sdk, Func<G02RuntimeObservation> observe)
    {
        this.sdk = sdk; this.observe = observe;
        runtimeDirectory = At(G02DotNetToolchain.RuntimePrefix);
        coreLib = At(G02DotNetToolchain.RuntimePrefix + "System.Private.CoreLib.dll");
        hostFxr = At(G02DotNetToolchain.HostFxrPath);
        // Read pinned metadata before any live snapshot. SDK already holds/verified every file.
        foreach (var file in sdk.RuntimeInventory().Where(file => file.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            var path = At(file.Path);
            if (!dlls.TryAdd(Path.GetFileName(path), path)) throw Refuse("RuntimeBindingUnavailable");
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) continue;
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly) throw Refuse("RuntimeBindingUnavailable");
            var name = metadata.GetString(metadata.GetAssemblyDefinition().Name);
            if (string.IsNullOrEmpty(name) || !managed.TryAdd(name, path)) throw Refuse("RuntimeBindingUnavailable");
        }
        Verify(sdk.Digest);
    }

    internal static G02RuntimeBinding Open(G02DotNetToolchain sdk) => OpenFixtureForChecks(sdk, CaptureLive);
    internal G02RuntimeBinding ObserveFixtureForChecks(Func<G02RuntimeObservation> provider) => OpenFixtureForChecks(sdk, provider);

    // Internal trusted conformance seam. No public factory accepts observation from package/input.
    internal static G02RuntimeBinding OpenFixtureForChecks(G02DotNetToolchain sdk, Func<G02RuntimeObservation> provider)
    {
        ArgumentNullException.ThrowIfNull(sdk); ArgumentNullException.ThrowIfNull(provider);
        try { return new(sdk, provider); }
        catch (ModuleException error) when (error.Code is "RuntimeBindingMismatch" or "RuntimeBindingUnavailable" or "UnsupportedRuntimeBindingPlatform") { throw; }
        catch (Exception error) when (Recoverable(error)) { throw Refuse("RuntimeBindingUnavailable"); }
    }

    internal void Verify(string expectedSdkDigest)
    {
        if (expectedSdkDigest != sdk.Digest) throw Refuse("RuntimeBindingMismatch");
        try { sdk.RevalidateRuntimeFiles(); }
        catch (ModuleException error) when (error.Code is "BuildToolchainInventoryMismatch" or "BuildToolchainFileMismatch" or
            "PackageHandlePathMismatch" or "PackageHandleTypeInvalid" or "PackageAlternateStreamRejected")
        { throw Refuse("RuntimeBindingMismatch"); }
        catch (Exception error) when (Recoverable(error)) { throw Refuse("RuntimeBindingUnavailable"); }
        try { Validate(observe()); }
        catch (ModuleException error) when (error.Code is "RuntimeBindingMismatch" or "RuntimeBindingUnavailable" or "UnsupportedRuntimeBindingPlatform") { throw; }
        catch (Exception error) when (Recoverable(error)) { throw Refuse("RuntimeBindingUnavailable"); }
    }

    private void Validate(G02RuntimeObservation value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.RuntimeDirectory) || string.IsNullOrWhiteSpace(value.CoreLibPath) ||
            value.TrustedAssemblyPaths.IsDefaultOrEmpty || value.ManagedAssemblies.IsDefaultOrEmpty || value.NativeModulePaths.IsDefaultOrEmpty ||
            value.TrustedAssemblyPaths.Any(string.IsNullOrWhiteSpace) || value.NativeModulePaths.Any(string.IsNullOrWhiteSpace) ||
            value.ManagedAssemblies.Any(item => item is null || string.IsNullOrWhiteSpace(item.Name)))
            throw Refuse("RuntimeBindingUnavailable");
        if (!value.Windows || value.Architecture != Architecture.X64) throw Refuse("UnsupportedRuntimeBindingPlatform");
        if (!SamePath(value.RuntimeDirectory, runtimeDirectory) || !SamePath(value.CoreLibPath, coreLib)) throw Refuse("RuntimeBindingMismatch");
        var platform = value.TrustedAssemblyPaths.Where(path => managed.ContainsKey(Path.GetFileNameWithoutExtension(path)))
            .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key!, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var (name, path) in managed)
            if (!platform.TryGetValue(name, out var paths) || paths.Length == 0 || paths.Any(actual => !SamePath(actual, path))) throw Refuse("RuntimeBindingMismatch");
        foreach (var assembly in value.ManagedAssemblies)
            if (managed.TryGetValue(assembly.Name, out var path) && (!assembly.DefaultContext || !SamePath(assembly.Location, path)))
                throw Refuse("RuntimeBindingMismatch");
        if (value.ManagedAssemblies.Count(item => item.Name.Equals("System.Private.CoreLib", StringComparison.OrdinalIgnoreCase)) != 1)
            throw Refuse("RuntimeBindingMismatch");
        foreach (var path in value.NativeModulePaths)
        {
            var name = Path.GetFileName(path);
            if (dlls.TryGetValue(name, out var expected) && !SamePath(path, expected)) throw Refuse("RuntimeBindingMismatch");
            if (name.Equals("hostfxr.dll", StringComparison.OrdinalIgnoreCase) && !SamePath(path, hostFxr)) throw Refuse("RuntimeBindingMismatch");
        }
        foreach (var name in new[] { "coreclr.dll", "clrjit.dll", "hostpolicy.dll" })
            if (value.NativeModulePaths.Count(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase)) != 1)
                throw Refuse("RuntimeBindingMismatch");
    }

    internal static G02RuntimeObservation CaptureLive()
    {
        // Prewarm observation helpers BEFORE final managed snapshot; no PE parsing occurs afterwards.
        var windows = OperatingSystem.IsWindows(); var architecture = RuntimeInformation.ProcessArchitecture;
        var runtime = RuntimeEnvironment.GetRuntimeDirectory(); var core = typeof(object).Assembly.Location;
        var context = AssemblyLoadContext.Default;
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToImmutableArray() ?? default;
        using var process = Process.GetCurrentProcess();
        var native = process.Modules.Cast<ProcessModule>().Select(module => module.FileName).ToImmutableArray();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => new G02LoadedPlatformAssembly(
            assembly.GetName().Name ?? "", assembly.IsDynamic ? "" : assembly.Location,
            ReferenceEquals(AssemblyLoadContext.GetLoadContext(assembly), context))).ToImmutableArray();
        return new(windows, architecture, runtime, core, tpa, assemblies, native);
    }

    private string At(string path) => Path.GetFullPath(Path.Combine(sdk.DirectoryPath, path.Replace('/', Path.DirectorySeparatorChar)));
    private static bool SamePath(string actual, string expected) => !string.IsNullOrWhiteSpace(actual) &&
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)), StringComparison.OrdinalIgnoreCase);
    private static bool Recoverable(Exception error) => error is not (OutOfMemoryException or AccessViolationException);
    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("package", code);
}
