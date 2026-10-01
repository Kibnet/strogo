using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using Strogo.Modules;

internal static class G02RuntimeBindingChecks
{
    internal static int Run(string repoRoot, string directory)
    {
        Directory.CreateDirectory(directory);
        using var sdk = G02DotNetToolchain.Open(repoRoot);
        var checks = 0; var rows = new List<object>();
        void Check(bool condition, string id) { if (!condition) throw new Exception("runtime " + id); checks++; }
        var live = G02RuntimeBinding.CaptureLive();
        Console.WriteLine("Runtime evidence: " + directory);
        File.WriteAllText(Path.Combine(directory, "actual-observation.json"), JsonSerializer.Serialize(live, new JsonSerializerOptions { WriteIndented = true }));
        var current = live;
        var binding = G02RuntimeBinding.OpenFixtureForChecks(sdk, () => current);
        binding.Verify(sdk.Digest);
        Check(sdk.RuntimeInventory().Length == 189, "pinned-runtime189");
        var actual = G02RuntimeBinding.Open(sdk); actual.Verify(sdk.Digest);
        Check(live.Windows && live.Architecture == Architecture.X64, "actual-windows-x64");
        void Refuse(string id, G02RuntimeObservation observation, string expected = "RuntimeBindingMismatch")
        {
            current = observation; string? code = null;
            try { binding.Verify(sdk.Digest); } catch (ModuleException error) { code = error.Code; }
            Check(code == expected, id); rows.Add(new { id, code, observationSource = "synthetic-conformance" });
        }
        var core = live.CoreLibPath;
        var foreign = Path.Combine(directory, "System.Private.CoreLib.dll");
        Refuse("runtime-root", live with { RuntimeDirectory = directory });
        Refuse("corelib-path", live with { CoreLibPath = foreign });
        Refuse("tpa-shadow", live with { TrustedAssemblyPaths = live.TrustedAssemblyPaths.Select(path => path.Equals(core, StringComparison.OrdinalIgnoreCase) ? foreign : path).ToImmutableArray() });
        current=live with { TrustedAssemblyPaths=live.TrustedAssemblyPaths.Add(core) }; binding.Verify(sdk.Digest);
        Check(true,"tpa-identical-pinned-repeat");
        Refuse("tpa-duplicate-foreign-path", live with { TrustedAssemblyPaths = live.TrustedAssemblyPaths.Add(foreign) });
        Refuse("tpa-case-collision", live with { TrustedAssemblyPaths = live.TrustedAssemblyPaths.Add(foreign.ToUpperInvariant()) });
        Refuse("tpa-missing", live with { TrustedAssemblyPaths = live.TrustedAssemblyPaths.Where(path => !path.Equals(core, StringComparison.OrdinalIgnoreCase)).ToImmutableArray() });
        Refuse("managed-shadow", live with { ManagedAssemblies = live.ManagedAssemblies.Add(new("System.Private.CoreLib", foreign, true)) });
        Refuse("managed-context", live with { ManagedAssemblies = live.ManagedAssemblies.Select(item => item.Name == "System.Private.CoreLib" ? item with { DefaultContext = false } : item).ToImmutableArray() });
        Refuse("managed-memory-only", live with { ManagedAssemblies = live.ManagedAssemblies.Select(item => item.Name == "System.Private.CoreLib" ? item with { Location = "" } : item).ToImmutableArray() });
        foreach (var name in new[] { "coreclr.dll", "clrjit.dll", "hostpolicy.dll" })
        {
            Refuse("native-shadow-" + name, live with { NativeModulePaths = live.NativeModulePaths.Select(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase) ? Path.Combine(directory, name) : path).ToImmutableArray() });
            Refuse("native-missing-" + name, live with { NativeModulePaths = live.NativeModulePaths.Where(path => !Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase)).ToImmutableArray() });
        }
        Refuse("hostfxr-shadow", live with { NativeModulePaths = live.NativeModulePaths.Where(path => !Path.GetFileName(path).Equals("hostfxr.dll", StringComparison.OrdinalIgnoreCase)).Append(Path.Combine(directory, "hostfxr.dll")).ToImmutableArray() });
        Refuse("null-observation", null!, "RuntimeBindingUnavailable");
        Refuse("empty-tpa", live with { TrustedAssemblyPaths = [] }, "RuntimeBindingUnavailable");
        Refuse("omitted-managed", live with { ManagedAssemblies = default }, "RuntimeBindingUnavailable");
        Refuse("empty-native", live with { NativeModulePaths = [] }, "RuntimeBindingUnavailable");
        Refuse("missing-runtime-dir", live with { RuntimeDirectory = "" }, "RuntimeBindingUnavailable");
        Refuse("wrong-architecture", live with { Architecture = Architecture.Arm64 }, "UnsupportedRuntimeBindingPlatform");
        current = live; string? digestCode = null;
        try { binding.Verify(new string('a', 64)); } catch (ModuleException error) { digestCode = error.Code; }
        Check(digestCode == "RuntimeBindingMismatch", "verified-build-sdk-digest");
        string? captureCode = null;
        try { _ = G02RuntimeBinding.OpenFixtureForChecks(sdk, () => throw new InvalidOperationException("capture unavailable")); }
        catch (ModuleException error) { captureCode = error.Code; }
        Check(captureCode == "RuntimeBindingUnavailable", "capture-failure-no-fallback");
        bool writeDenied = false;
        try { using var write = File.Open(core, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }
        catch (IOException) { writeDenied = true; }
        catch (UnauthorizedAccessException) { writeDenied = true; }
        Check(writeDenied, "actual-held-corelib-write-refused");
        sdk.Dispose(); string? disposedCode = null;
        try { actual.Verify(sdk.Digest); } catch (ModuleException error) { disposedCode = error.Code; }
        Check(disposedCode == "RuntimeBindingUnavailable", "borrowed-sdk-disposed");
        File.WriteAllText(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(new { purpose = "validation-only-no-human-admission", checks, sdkDigest = sdk.Digest,
            actualObservation = live, rows, digestCode, captureCode, writeDenied, disposedCode,
            boundary = "Observed paths and retained pinned files, not memory/native attestation or production admission." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS runtime binding checks=" + checks + " evidence=" + directory);
        return checks;
    }
}
