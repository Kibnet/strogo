namespace Strogo.Modules;

internal sealed record G02VerifiedFixtureBuild(G02VerifiedTranscript Proof, G02FixtureBuild Build);

/// <summary>Composes held proof checks for fixtures; this does not admit or load executable code.</summary>
internal static class G02PackageProofReplay
{
    internal static async Task<G02VerifiedFixtureBuild> VerifyBuildFixtureAsync(G02PackageSnapshot snapshot,
        OwnerTrust trust, ReadOnlyMemory<byte> currentState, string expectedBundleDigest, DateTimeOffset now,
        Func<G02DafnyToolchain> openTrustedTool, Func<G02DotNetToolchain> openTrustedSdk,
        string hostPackageCache, string diagnosticDirectory)
    {
        var proof = await VerifyFixtureAsync(snapshot, trust, currentState, expectedBundleDigest, now,
            openTrustedTool, Path.Combine(diagnosticDirectory, "proof"));
        var inputs = G02ProofSourceRegenerator.Verify(snapshot);
        using var tool = openTrustedTool();
        if (snapshot.Manifest.ToolchainDigest != tool.Digest)
            throw ModulesExceptionFactory.Error("package", "PackageToolchainDigestMismatch");
        var translation = await G02DafnyReplay.TranslateFixtureAsync(tool, inputs,
            Path.Combine(diagnosticDirectory, "translation"));
        snapshot.Revalidate();
        G02ProofTranscript.VerifyHeld(snapshot, translation.Verified);
        if (!proof.TranscriptBytes.AsSpan().SequenceEqual(translation.Verified.TranscriptBytes) ||
            !proof.ObligationVectorBytes.AsSpan().SequenceEqual(translation.Verified.ObligationVectorBytes))
            throw ModulesExceptionFactory.Error("package", "NonDeterministicVerifierOutput");
        using var sdk = openTrustedSdk();
        var build = await G02OfflineFixtureBuild.RunAsync(sdk, translation, hostPackageCache,
            Path.Combine(diagnosticDirectory, "build"));
        snapshot.Revalidate();
        VerifyEntryArtifactsFixture(snapshot, build);
        return new(proof, build);
    }

    // Internal trusted-record helper for isolated fixture tamper checks, never an admission API.
    // It covers entry assembly/deps only, not the runtime-dependency closure or closureDigest.
    internal static void VerifyEntryArtifactsFixture(G02PackageSnapshot snapshot, G02FixtureBuild regenerated)
    {
        snapshot.Revalidate();
        if (!snapshot.ReadHeld(snapshot.Manifest.EntryAssemblyPath).AsSpan().SequenceEqual(regenerated.AssemblyBytes))
            throw ModulesExceptionFactory.Error("package", "CompiledEntryReplayMismatch");
        var deps = snapshot.Manifest.Files.Single(file => file.Role == "deps");
        if (!snapshot.ReadHeld(deps.Path).AsSpan().SequenceEqual(regenerated.DepsBytes))
            throw ModulesExceptionFactory.Error("package", "CompiledDepsReplayMismatch");
    }

    internal static async Task<G02VerifiedTranscript> VerifyFixtureAsync(G02PackageSnapshot snapshot,
        OwnerTrust trust, ReadOnlyMemory<byte> currentState, string expectedBundleDigest, DateTimeOffset now,
        Func<G02DafnyToolchain> openTrustedTool, string diagnosticDirectory)
    {
        // Check owner approval and held proof metadata before opening the host verifier closure.
        snapshot.Revalidate();
        _ = G02StoredIdentityVerifier.Verify(snapshot, trust, currentState.Span, expectedBundleDigest, now);
        var inputs = G02ProofSourceRegenerator.Verify(snapshot);
        using var tool = openTrustedTool();
        if (snapshot.Manifest.ToolchainDigest != tool.Digest)
            throw ModulesExceptionFactory.Error("package", "PackageToolchainDigestMismatch");
        var first = await G02DafnyReplay.RunFixtureAsync(tool, inputs, Path.Combine(diagnosticDirectory, "first"));
        snapshot.Revalidate();
        G02ProofTranscript.VerifyHeld(snapshot, first.Verified);
        var second = await G02DafnyReplay.RunFixtureAsync(tool, inputs, Path.Combine(diagnosticDirectory, "second"));
        snapshot.Revalidate();
        if (!first.Verified.TranscriptBytes.AsSpan().SequenceEqual(second.Verified.TranscriptBytes) ||
            !first.Verified.ObligationVectorBytes.AsSpan().SequenceEqual(second.Verified.ObligationVectorBytes))
            throw ModulesExceptionFactory.Error("package", "NonDeterministicVerifierOutput");
        G02ProofTranscript.VerifyHeld(snapshot, second.Verified);
        return second.Verified;
    }
}
