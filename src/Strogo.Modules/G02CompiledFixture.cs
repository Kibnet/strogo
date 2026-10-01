using System.Runtime.Loader;

namespace Strogo.Modules;

/// <summary>Trusted composed-build fixtures only. No public admission, runtime closure or concurrent lifetime.</summary>
internal sealed class G02CompiledFixture : IDisposable
{
    private readonly AssemblyLoadContext context;
    private readonly G02CompiledDispatch dispatch;
    private bool disposed;

    private G02CompiledFixture(G02PackageSnapshot snapshot, G02VerifiedFixtureBuild verified, bool forObserver)
    {
        G02PackageProofReplay.VerifyEntryArtifactsFixture(snapshot, verified.Build);
        G02ProofTranscript.VerifyHeld(snapshot, verified.Proof);
        _ = G02ProofSourceRegenerator.Verify(snapshot);
        var module = ModulesCompiler.Compile(ModulesParser.ParseModule(snapshot.ReadHeld(
            snapshot.Manifest.Files.Single(file => file.Role == "module").Path)));
        var bundle = OwnerBundleV04Parser.Parse(snapshot.ReadHeld(
            snapshot.Manifest.Files.Single(file => file.Role == "bundle").Path));
        context = new AssemblyLoadContext("strogo-fixture-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            using var bytes = new MemoryStream(verified.Build.AssemblyBytes, writable: false);
            var assembly = forObserver
                ? context.LoadFromAssemblyPath(snapshot.EntryAssemblyPathForObserver())
                : context.LoadFromStream(bytes);
            if (forObserver) snapshot.Revalidate();
            dispatch = new G02CompiledDispatch(assembly, module, bundle);
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    internal static G02CompiledFixture Open(G02PackageSnapshot snapshot, G02VerifiedFixtureBuild verified)
        => new(snapshot, verified, forObserver: false);

    // File mappings may survive Dispose; observer hosts must use a dedicated process lifetime.
    internal static G02CompiledFixture OpenForObserver(G02PackageSnapshot snapshot, G02VerifiedFixtureBuild verified)
        => new(snapshot, verified, forObserver: true);

    internal ModuleValue Invoke(string functionId, IReadOnlyList<ModuleValue> arguments)
        => dispatch.Invoke(functionId, arguments);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        dispatch.Dispose();
        context.Unload();
    }
}
