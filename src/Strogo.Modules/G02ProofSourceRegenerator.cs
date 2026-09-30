using System.Collections.Immutable;
using Kernel.Core;

namespace Strogo.Modules;

internal sealed class G02RegeneratedProofInputs
{
    internal G02RegeneratedProofInputs(byte[] sourceBytes, byte[] sourceMapBytes,
        ImmutableArray<DafnyProofObligation> obligations)
    {
        SourceBytes = sourceBytes;
        SourceMapBytes = sourceMapBytes;
        Obligations = obligations;
    }

    internal byte[] SourceBytes { get; }
    internal byte[] SourceMapBytes { get; }
    internal ImmutableArray<DafnyProofObligation> Obligations { get; }
}

/// <summary>Rebuilds the verifier input from held module and owner bytes; this does not run Dafny.</summary>
internal static class G02ProofSourceRegenerator
{
    internal static G02RegeneratedProofInputs Verify(G02PackageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var sources = snapshot.Manifest.Files.Where(file => file.Role == "proof-source").ToArray();
        if (sources.Length != 1) throw Refuse("ProofSourceCountUnsupported");
        // The approved producer/replay profile binds exactly this one manifest path.
        if (sources[0].Path != "content/candidate.dfy") throw Refuse("ProofSourcePathUnsupported");
        var moduleBytes = ReadRole(snapshot, "module");
        var bundleBytes = ReadRole(snapshot, "bundle");
        DafnyFoldLoweringResult lowering;
        try
        {
            var module = ModulesCompiler.Compile(ModulesParser.ParseModule(moduleBytes));
            var bundle = OwnerBundleV04Parser.Parse(bundleBytes);
            // Lower verifies owner contract type closure and pairs every export with an owner entry.
            lowering = ModulesDafnyLowerer.Lower(module, bundle);
        }
        catch (ModuleException error) when (error.Stage != "package")
        {
            throw Refuse("ProofSourceRegenerationFailed");
        }
        if (!lowering.SourceBytes.AsSpan().SequenceEqual(snapshot.ReadHeld(sources[0].Path)))
            throw Refuse("ProofSourceGenerationMismatch");
        var sourceMapBytes = CanonicalJson.Encode(lowering.SourceMap);
        if (!sourceMapBytes.AsSpan().SequenceEqual(ReadRole(snapshot, "source-map")))
            throw Refuse("ProofSourceMapGenerationMismatch");
        return new(lowering.SourceBytes, sourceMapBytes, lowering.Obligations);
    }

    private static byte[] ReadRole(G02PackageSnapshot snapshot, string role)
        => snapshot.ReadHeld(snapshot.Manifest.Files.Single(file => file.Role == role).Path);

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("package", code);
}
