namespace Strogo.Modules;

/// <summary>Composes held proof checks for fixtures; this does not admit or load executable code.</summary>
internal static class G02PackageProofReplay
{
    internal static async Task<G02VerifiedTranscript> VerifyFixtureAsync(G02PackageSnapshot snapshot,
        OwnerTrust trust, ReadOnlyMemory<byte> currentState, string expectedBundleDigest, DateTimeOffset now,
        Func<G02DafnyToolchain> openTrustedTool, string diagnosticDirectory)
    {
        // Check owner approval and held proof metadata before opening the host verifier closure.
        _ = G02StoredIdentityVerifier.Verify(snapshot, trust, currentState.Span, expectedBundleDigest, now);
        var inputs = G02ProofSourceRegenerator.Verify(snapshot);
        using var tool = openTrustedTool();
        if (snapshot.Manifest.ToolchainDigest != tool.Digest)
            throw ModulesExceptionFactory.Error("package", "PackageToolchainDigestMismatch");
        var first = await G02DafnyReplay.RunFixtureAsync(tool, inputs, Path.Combine(diagnosticDirectory, "first"));
        G02ProofTranscript.VerifyHeld(snapshot, first.Verified);
        var second = await G02DafnyReplay.RunFixtureAsync(tool, inputs, Path.Combine(diagnosticDirectory, "second"));
        if (!first.Verified.TranscriptBytes.AsSpan().SequenceEqual(second.Verified.TranscriptBytes) ||
            !first.Verified.ObligationVectorBytes.AsSpan().SequenceEqual(second.Verified.ObligationVectorBytes))
            throw ModulesExceptionFactory.Error("package", "NonDeterministicVerifierOutput");
        G02ProofTranscript.VerifyHeld(snapshot, second.Verified);
        return second.Verified;
    }
}
