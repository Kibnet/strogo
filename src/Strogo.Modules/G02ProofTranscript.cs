using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Strogo.Modules;

internal sealed record G02VerifiedTranscript(byte[] TranscriptBytes, byte[] ObligationVectorBytes,
    int VerifiedUnits, string EvidenceDigest);

/// <summary>Normalizes a complete Dafny verification result; raw process output is never proof metadata.</summary>
internal static partial class G02ProofTranscript
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    [GeneratedRegex(@"\A(?:\r?\n)?Dafny program verifier finished with ([1-9][0-9]*) verified, 0 errors\r?\n\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex VerifiedSummary();

    internal static G02VerifiedTranscript NormalizeVerified(int exitCode, bool timedOut, bool outputExceeded,
        ReadOnlySpan<byte> stdout, ReadOnlySpan<byte> stderr, string toolchainDigest, string proofSourcesDigest,
        ImmutableArray<DafnyProofObligation> obligations)
    {
        if (exitCode != 0 || timedOut || outputExceeded) throw Refuse("VerifierFailed");
        if (!stderr.IsEmpty) throw Refuse("VerifierOutputUnrecognized");
        string text;
        try { text = StrictUtf8.GetString(stdout); }
        catch (DecoderFallbackException) { throw Refuse("VerifierOutputUnrecognized"); }
        var match = VerifiedSummary().Match(text);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var units) || units < 1)
            throw Refuse("VerifierOutputUnrecognized");
        if (!OwnerAdmissionWire.IsDigest(toolchainDigest) || !OwnerAdmissionWire.IsDigest(proofSourcesDigest))
            throw Refuse("VerifierIdentityInvalid");
        if (obligations.IsDefaultOrEmpty || obligations.Length > 4096 ||
            obligations.Any(item => string.IsNullOrEmpty(item.Id) || string.IsNullOrEmpty(item.Kind)) ||
            obligations.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != obligations.Length)
            throw Refuse("ProofObligationsInvalid");

        var evidenceBytes = Canonical(new { verifiedUnits = units });
        var evidenceDigest = OwnerAdmissionWire.Hash("strogo.proof.v0.2/evidence", evidenceBytes);
        var mapped = obligations.Select(item => (Id: WireObligationId(item.Id), item.Kind)).ToArray();
        try { foreach (var item in mapped) OwnerAdmissionWire.RequireId(item.Kind); }
        catch (ModuleException) { throw Refuse("ProofObligationsInvalid"); }
        if (mapped.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != mapped.Length)
            throw Refuse("ProofObligationsInvalid");
        var ordered = mapped.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var records = ordered.Select(item => new
        {
            obligationId = item.Id, kind = item.Kind, status = "Verified",
            diagnosticCode = "verified", evidenceDigest
        }).ToArray();
        var vector = ordered.Select(item => new
        {
            obligationId = item.Id, kind = item.Kind, status = "Verified", evidenceDigest
        }).ToArray();
        var transcript = Canonical(new
        {
            schemaVersion = "strogo.proof-transcript.v0.2", toolchainDigest,
            proofSourcesDigest, outcome = "Verified", records
        });
        return new(transcript, Canonical(vector), units, evidenceDigest);
    }

    internal static void VerifyHeld(G02PackageSnapshot snapshot, G02VerifiedTranscript fresh)
    {
        var transcriptPath = snapshot.Manifest.Files.Single(file => file.Role == "proof-transcript").Path;
        if (!snapshot.ReadHeld(transcriptPath).AsSpan().SequenceEqual(fresh.TranscriptBytes))
            throw Refuse("ProofReplayTranscriptMismatch");
        var proofPath = snapshot.Manifest.Files.Single(file => file.Role == "proof").Path;
        using var proof = OwnerAdmissionWire.Parse(snapshot.ReadHeld(proofPath));
        if (!OwnerAdmissionWire.Canonical(proof.RootElement.GetProperty("obligations"))
                .AsSpan().SequenceEqual(fresh.ObligationVectorBytes))
            throw Refuse("ProofReplayObligationsMismatch");
    }

    internal static byte[] Canonical(object value)
        => OwnerAdmissionWire.Canonical(JsonSerializer.SerializeToElement(value));

    internal static string WireObligationId(string rawId)
    {
        if (string.IsNullOrEmpty(rawId)) throw Refuse("ProofObligationsInvalid");
        try { return OwnerAdmissionWire.Hash("strogo.proof.v0.2/obligation-id", StrictUtf8.GetBytes(rawId)); }
        catch (EncoderFallbackException) { throw Refuse("ProofObligationsInvalid"); }
    }

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("package", code);
}
