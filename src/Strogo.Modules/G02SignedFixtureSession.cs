using System.Text.Json;
using Kernel.Core;

namespace Strogo.Modules;

/// <summary>Internal test-key session, not public admission. Host owns trust/provider/clock; lifecycle is nonconcurrent.</summary>
internal sealed class G02SignedFixtureSession : IDisposable
{
    private readonly G02PackageSnapshot snapshot;
    private readonly OwnerTrust trust;
    private readonly Func<byte[]> stateProvider;
    private readonly byte[] releaseBytes;
    private readonly string bundleDigest;
    private readonly TimeProvider clock;
    private readonly ModuleIr module;
    private readonly G02CompiledFixture fixture;
    private readonly G02RuntimeBinding runtime;
    private readonly string sdkDigest;
    private readonly string contractApprovalDigest;
    private readonly string releaseAdmissionDigest;
    private bool disposed;
    internal int DispatchAttempts { get; private set; } // Diagnostic adapter count, not native-entry evidence.

    private G02SignedFixtureSession(G02PackageSnapshot snapshot, G02VerifiedFixtureBuild verified,
        OwnerTrust trust, Func<byte[]> stateProvider, byte[] releaseBytes, string bundleDigest, TimeProvider clock, G02RuntimeBinding runtime, bool forObserver)
    {
        this.snapshot = snapshot;
        this.trust = trust;
        this.stateProvider = stateProvider;
        this.releaseBytes = releaseBytes;
        this.bundleDigest = bundleDigest;
        this.clock = clock;
        this.runtime = runtime;
        sdkDigest = verified.Build.SdkClosureDigest;
        var (semantic, release) = VerifyFresh();
        contractApprovalDigest = semantic.ArtifactDigest;
        releaseAdmissionDigest = release.ArtifactDigest;
        snapshot.Revalidate();
        runtime.Verify(sdkDigest);
        module = ModulesCompiler.Compile(ModulesParser.ParseModule(snapshot.ReadHeld(snapshot.Manifest.Files.Single(value => value.Role == "module").Path)));
        fixture = forObserver ? G02CompiledFixture.OpenForObserver(snapshot, verified) : G02CompiledFixture.Open(snapshot, verified);
    }

    internal static G02SignedFixtureSession Open(string packagePath, G02VerifiedFixtureBuild verified,
        OwnerTrust trust, Func<byte[]> stateProvider, ReadOnlySpan<byte> releaseBytes, string bundleDigest, TimeProvider clock, G02RuntimeBinding runtime)
        => OpenCore(packagePath, verified, trust, stateProvider, releaseBytes, bundleDigest, clock, runtime, forObserver: false);

    /// <summary>Same fixture gates; CLR file mapping lifetime belongs to the diagnostic process.</summary>
    internal static G02SignedFixtureSession OpenForObserver(string packagePath, G02VerifiedFixtureBuild verified,
        OwnerTrust trust, Func<byte[]> stateProvider, ReadOnlySpan<byte> releaseBytes, string bundleDigest, TimeProvider clock, G02RuntimeBinding runtime)
        => OpenCore(packagePath, verified, trust, stateProvider, releaseBytes, bundleDigest, clock, runtime, forObserver: true);

    private static G02SignedFixtureSession OpenCore(string packagePath, G02VerifiedFixtureBuild verified,
        OwnerTrust trust, Func<byte[]> stateProvider, ReadOnlySpan<byte> releaseBytes, string bundleDigest, TimeProvider clock, G02RuntimeBinding runtime, bool forObserver)
    {
        ArgumentNullException.ThrowIfNull(verified);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(stateProvider);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(runtime);
        if (releaseBytes.Length > 65536) throw Refuse("ReleaseAdmissionLimitExceeded");
        var immutableRelease = releaseBytes.ToArray();
        var snapshot = G02PackageSnapshot.OpenStructural(packagePath);
        try { return new(snapshot, verified, trust, stateProvider, immutableRelease, bundleDigest, clock, runtime, forObserver); }
        catch { snapshot.Dispose(); throw; }
    }

    internal byte[] InvokeJson(byte[] request)
    {
        var bytes = disposed ? G02InvocationCodec.Refusal(null, ModulesExceptionFactory.Error("package", "CompiledFixtureDisposed")) : G02InvocationCodec.Invoke(module, request, (functionId, arguments) =>
        {
            if (disposed) throw ModulesExceptionFactory.Error("package", "CompiledFixtureDisposed");
            _ = VerifyFresh();
            snapshot.Revalidate();
            runtime.Verify(sdkDigest);
            DispatchAttempts++;
            return fixture.Invoke(functionId, arguments);
        });
        using var result = JsonDocument.Parse(bytes);
        return CanonicalJson.Encode(new { schemaVersion = "strogo.fixture-invoke.v0.1", validationOnly = true,
            packageDigest = snapshot.Manifest.PackageDigest, buildManifestDigest = snapshot.Manifest.ArtifactDigest,
            contractApprovalDigest, releaseAdmissionDigest, result = result.RootElement });
    }

    private (VerifiedContractApproval Semantic, VerifiedReleaseAdmission Release) VerifyFresh()
    {
        byte[] supplied;
        try
        {
            supplied = stateProvider();
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            throw Refuse("OwnerStateUnavailable");
        }
        if (supplied is null) throw Refuse("OwnerStateUnavailable");
        if (supplied.Length > 65536) throw Refuse("OwnerStateLimitExceeded");
        var state = supplied.ToArray();
        var now = clock.GetUtcNow();
        var identity = G02StoredIdentityVerifier.Verify(snapshot, trust, state, bundleDigest, now);
        var manifest = snapshot.Manifest;
        var release = trust.VerifyReleaseAdmission(releaseBytes, state, identity.ContractApproval,
            manifest.ProofDigest, manifest.ArtifactDigest, manifest.ToolchainDigest, manifest.PackageDigest, now);
        return (identity.ContractApproval, release);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { fixture.Dispose(); }
        finally { snapshot.Dispose(); }
    }

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("admission", code);
}
