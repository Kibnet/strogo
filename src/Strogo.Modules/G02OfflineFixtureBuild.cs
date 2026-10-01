using System.Diagnostics;
using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace Strogo.Modules;

internal sealed record G02FixtureBuild(byte[] AssemblyBytes, byte[] DepsBytes, string SdkClosureDigest);

/// <summary>Offline fixture compilation only. This is not a public producer, package or admission.</summary>
internal static class G02OfflineFixtureBuild
{
    internal static ImmutableArray<(string Name, string Sha256)> Packs { get; } =
    [
        ("microsoft.aspnetcore.app.runtime.win-x64", "40613e5e573ebc816744cf9020b1d96100a9ddf4939596e6ab635c16b13c51f7"),
        ("microsoft.netcore.app.crossgen2.win-x64", "edccdab06b7dde8a6325b85040dac511134ab139dd5f09407b233b92218ed904"),
        ("microsoft.netcore.app.runtime.win-x64", "822b247bddbd88a290a8072be89815ffeac26e5aac0d41b1eab95fda5279f443"),
        ("microsoft.windowsdesktop.app.runtime.win-x64", "3aaa3c2ac928a0da1d677caeee6405d1cdd0ae1ccd7a23a8e293a8cb6f009669")
    ];

    private const string Project = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Library</OutputType><TargetFramework>net10.0</TargetFramework>
            <EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>enable</ImplicitUsings>
            <Nullable>disable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><NoWarn>CS8981</NoWarn>
            <AssemblyName>strogo.generated</AssemblyName><RuntimeIdentifier>win-x64</RuntimeIdentifier>
            <RuntimeFrameworkVersion>10.0.11</RuntimeFrameworkVersion><TargetLatestRuntimePatch>false</TargetLatestRuntimePatch>
            <SelfContained>true</SelfContained><PublishReadyToRun>true</PublishReadyToRun><PublishReadyToRunComposite>false</PublishReadyToRunComposite>
            <Deterministic>true</Deterministic><CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>
            <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile><NuGetAudit>false</NuGetAudit>
            <PathMap>$(MSBuildProjectDirectory)=/_/strogo/g02</PathMap>
            <Version>0.2.0</Version><AssemblyVersion>0.2.0.0</AssemblyVersion><FileVersion>0.2.0.0</FileVersion>
            <InformationalVersion>0.2.0</InformationalVersion><IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
            <UseSharedCompilation>false</UseSharedCompilation>
          </PropertyGroup>
          <ItemGroup><Compile Include="Generated.cs" /></ItemGroup>
        </Project>
        """;
    private const string NuGetConfig = """
        <configuration><packageSources><clear/><add key="held-offline" value="feed" /></packageSources>
        <fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>
        """;

    internal static async Task<G02FixtureBuild> RunAsync(G02DotNetToolchain sdk, G02FixtureReplay translation,
        string hostPackageCache, string diagnosticDirectory)
    {
        // Inputs and paths are supplied by a trusted fixture caller, never a public candidate API.
        var generated = translation.GeneratedSourceBytes?.ToArray() ?? throw Refuse("VerifiedTranslationMissing");
        if (generated.Length is < 1 or > 8_388_608) throw Refuse("GeneratedSourceSizeInvalid");
        var diagnostics = Path.GetFullPath(diagnosticDirectory);
        try { Directory.CreateDirectory(diagnostics); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw Refuse("BuildIoRefused"); }
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var segment = "sgb-" + Guid.NewGuid().ToString("N");
        var work = Path.Combine(tempRoot, segment);
        var retained = Path.Combine(diagnostics, segment);
        if (Path.Combine(work, "cache", "microsoft.netcore.app.crossgen2.win-x64", "10.0.11", "tools", "crossgen2.exe").Length >= 260)
            throw Refuse("BuildWorkPathTooLong");
        if (Directory.Exists(work) || Directory.Exists(retained)) throw Refuse("BuildDirectoryAlreadyExists");
        var held = new List<FileStream>();
        var completed = false;
        try
        {
            Directory.CreateDirectory(work);
            foreach (var name in new[] { "feed", "cache", "home", "temp" }) Directory.CreateDirectory(Path.Combine(work, name));
            foreach (var pack in Packs)
            {
                var name = pack.Name + ".10.0.11.nupkg";
                using var archive = new FileStream(Path.Combine(hostPackageCache, pack.Name, "10.0.11", name),
                    FileMode.Open, FileAccess.Read, FileShare.Read);
                if (archive.Length > 41_943_040) throw Refuse("BuildPackSizeInvalid");
                if (Hash(archive) != pack.Sha256) throw Refuse("BuildPackDigestMismatch");
                var destination = Path.Combine(work, "feed", name);
                using (var writer = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await archive.CopyToAsync(writer);
                var copy = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
                held.Add(copy);
                if (Hash(copy) != pack.Sha256) throw Refuse("BuildPackDigestMismatch");
            }
            foreach (var (name, bytes) in new[]
            {
                ("Generated.cs", generated), ("Generated.csproj", Encoding.UTF8.GetBytes(Project)),
                ("NuGet.Config", Encoding.UTF8.GetBytes(NuGetConfig)),
                ("global.json", Encoding.UTF8.GetBytes("{\"sdk\":{\"version\":\"10.0.400\",\"rollForward\":\"disable\",\"allowPrerelease\":false}}"))
            })
            {
                var path = Path.Combine(work, name);
                await File.WriteAllBytesAsync(path, bytes);
                var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                held.Add(handle);
                if (Hash(handle) != Convert.ToHexStringLower(SHA256.HashData(bytes))) throw Refuse("BuildInputDigestMismatch");
            }
            var common = new[] { "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false",
                "-p:ImportDirectoryPackagesProps=false", "-p:UseSharedCompilation=false", "-nr:false", "-v:minimal" };
            foreach (var step in new[] { "restore", "locked-restore", "publish" })
            {
                sdk.Revalidate();
                var start = new ProcessStartInfo(sdk.ExecutablePath) { WorkingDirectory = work };
                foreach (var argument in step == "publish"
                    ? new[] { "publish", "Generated.csproj", "-c", "Release", "--no-restore", "-o", "publish" }
                    : step == "locked-restore"
                        ? new[] { "restore", "Generated.csproj", "--configfile", "NuGet.Config", "--locked-mode" }
                        : new[] { "restore", "Generated.csproj", "--configfile", "NuGet.Config" })
                    start.ArgumentList.Add(argument);
                foreach (var argument in common) start.ArgumentList.Add(argument);
                G02VerifierProcess.ConfigureEnvironment(start, sdk.DirectoryPath, Path.Combine(work, "temp"));
                start.Environment["NUGET_PACKAGES"] = Path.Combine(work, "cache");
                start.Environment["DOTNET_CLI_HOME"] = Path.Combine(work, "home");
                start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
                start.Environment["DOTNET_NOLOGO"] = "true";
                start.Environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
                start.Environment["NUGET_CERT_REVOCATION_MODE"] = "offline";
                start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
                start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
                // NuGet computes Windows configuration/cache defaults even with --configfile.
                // Supply private paths rather than inheriting the user's configuration locations.
                foreach (var variable in new[] { "ProgramFiles", "ProgramFiles(x86)", "APPDATA", "LOCALAPPDATA", "USERPROFILE" })
                    start.Environment[variable] = Path.Combine(work, "home");
                var output = await G02VerifierProcess.RunAsync(start);
                await File.WriteAllBytesAsync(Path.Combine(work, step + "-stdout.bin"), output.Stdout);
                await File.WriteAllBytesAsync(Path.Combine(work, step + "-stderr.bin"), output.Stderr);
                await File.WriteAllBytesAsync(Path.Combine(work, step + "-process.json"), G02ProofTranscript.Canonical(new
                { arguments = start.ArgumentList.ToArray(), exitCode = output.ExitCode }));
                sdk.Revalidate();
                if (output.ExitCode != 0 || output.Stderr.Length != 0) throw Refuse("BuildProcessFailed");
                if (step == "restore") held.Add(new FileStream(Path.Combine(work, "packages.lock.json"),
                    FileMode.Open, FileAccess.Read, FileShare.Read));
            }
            var assembly = ReadBounded(Path.Combine(work, "publish", "strogo.generated.dll"), 16_777_216);
            var deps = ReadBounded(Path.Combine(work, "publish", "strogo.generated.deps.json"), 1_048_576);
            using var pe = new PEReader(new MemoryStream(assembly, writable: false));
            var native = pe.PEHeaders.CorHeader?.ManagedNativeHeaderDirectory ?? default;
            if (native.Size < 4 || native.RelativeVirtualAddress == 0 ||
                !pe.GetSectionData(native.RelativeVirtualAddress).GetContent(0, 4).AsSpan().SequenceEqual("RTR\0"u8))
                throw Refuse("BuildReadyToRunMissing");
            await File.WriteAllBytesAsync(Path.Combine(work, "receipt.json"), G02ProofTranscript.Canonical(new
            {
                purpose = "fixture-only-not-admitted", sdkClosureDigest = sdk.Digest,
                transcriptSha256 = Convert.ToHexStringLower(SHA256.HashData(translation.Verified.TranscriptBytes)),
                generatedSourceSha256 = Convert.ToHexStringLower(SHA256.HashData(generated)),
                assemblySha256 = Convert.ToHexStringLower(SHA256.HashData(assembly)),
                packs = Packs.Select(pack => new { name = pack.Name, sha256 = pack.Sha256 }).ToArray()
            }));
            completed = true;
            return new(assembly, deps, sdk.Digest);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw Refuse("BuildIoRefused"); }
        catch (BadImageFormatException) { throw Refuse("BuildArtifactInvalid"); }
        finally
        {
            foreach (var file in held) file.Dispose();
            // Only move this invocation's freshly created single segment to the explicit diagnostic root.
            if (Path.GetDirectoryName(work) != Path.TrimEndingDirectorySeparator(tempRoot) ||
                Path.GetDirectoryName(retained) != Path.TrimEndingDirectorySeparator(diagnostics))
                throw Refuse("BuildDiagnosticPathInvalid");
            try { if (Directory.Exists(work)) Directory.Move(work, retained); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Retain a primary build refusal; a successful build cannot lose its diagnostic evidence silently.
                if (completed) throw Refuse("BuildDiagnosticsRetentionFailed");
            }
        }
        // Research outputs are retained outside executable package metadata. No production ACL claim.
    }

    private static byte[] ReadBounded(string path, long maximum)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is < 1 || file.Length > maximum) throw Refuse("BuildArtifactSizeInvalid");
        using var bytes = new MemoryStream();
        file.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static string Hash(FileStream file)
    {
        file.Position = 0;
        var hash = Convert.ToHexStringLower(SHA256.HashData(file));
        file.Position = 0;
        return hash;
    }
    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("package", code);
}
