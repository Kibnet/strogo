using System.Text;
using System.Text.Json;

namespace Strogo.Modules;

/// <summary>Stored proof file binding only; it cannot establish a fresh verifier result.</summary>
internal static class G02StoredProofArtifacts
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Check(G02PackageSnapshot snapshot, JsonElement proof)
    {
        var sources = snapshot.Manifest.Files.Where(file => file.Role == "proof-source")
            .OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        foreach (var source in sources)
        {
            var bytes = snapshot.ReadHeld(source.Path);
            try
            {
                var text = StrictUtf8.GetString(bytes);
                if (bytes.Length == 0 || bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ||
                    text.Contains('\r') || text.Contains('\0'))
                    throw Refuse("PackageProofSourceNonCanonical");
            }
            catch (DecoderFallbackException)
            {
                throw Refuse("PackageProofSourceNonCanonical");
            }
        }
        var sourceInventory = OwnerAdmissionWire.Canonical(JsonSerializer.SerializeToElement(sources.Select(file => new
        {
            path = file.Path, sha256 = file.Sha256, length = file.Length, role = file.Role
        }).ToArray()));
        if (OwnerAdmissionWire.Digest(proof, "proofSourcesDigest") !=
            OwnerAdmissionWire.Hash("strogo.proof.v0.2/proof-sources", sourceInventory))
            throw Refuse("PackageProofSourcesDigestMismatch");

        var transcriptBytes = ReadRole(snapshot, "proof-transcript");
        using var transcript = ParsePackageArtifact(transcriptBytes);
        var transcriptRoot = transcript.RootElement;
        OwnerAdmissionWire.Fields(transcriptRoot, "schemaVersion", "toolchainDigest", "proofSourcesDigest",
            "outcome", "records");
        OwnerAdmissionWire.Equal(transcriptRoot, "schemaVersion", "strogo.proof-transcript.v0.2");
        if (OwnerAdmissionWire.Digest(transcriptRoot, "toolchainDigest") !=
                OwnerAdmissionWire.Digest(proof, "toolchainDigest") ||
            OwnerAdmissionWire.Digest(transcriptRoot, "proofSourcesDigest") !=
                OwnerAdmissionWire.Digest(proof, "proofSourcesDigest") ||
            OwnerAdmissionWire.NonEmpty(transcriptRoot, "outcome") != OwnerAdmissionWire.NonEmpty(proof, "outcome"))
            throw Refuse("PackageProofTranscriptIdentityMismatch");
        if (OwnerAdmissionWire.Digest(proof, "transcriptDigest") !=
            OwnerAdmissionWire.Hash("strogo.proof.v0.2/transcript", transcriptBytes))
            throw Refuse("PackageProofTranscriptDigestMismatch");
        var records = transcriptRoot.GetProperty("records");
        var obligations = proof.GetProperty("obligations");
        if (records.ValueKind != JsonValueKind.Array || records.GetArrayLength() != obligations.GetArrayLength())
            throw Refuse("PackageProofTranscriptVectorMismatch");
        for (var index = 0; index < records.GetArrayLength(); index++)
        {
            var record = records[index];
            var obligation = obligations[index];
            OwnerAdmissionWire.Fields(record, "obligationId", "kind", "status", "diagnosticCode", "evidenceDigest");
            if (OwnerAdmissionWire.NonEmpty(record, "diagnosticCode") != "verified")
                throw Refuse("PackageProofTranscriptDiagnosticInvalid");
            if (OwnerAdmissionWire.Id(record, "obligationId") != OwnerAdmissionWire.Id(obligation, "obligationId") ||
                OwnerAdmissionWire.Id(record, "kind") != OwnerAdmissionWire.Id(obligation, "kind") ||
                OwnerAdmissionWire.NonEmpty(record, "status") != OwnerAdmissionWire.NonEmpty(obligation, "status") ||
                OwnerAdmissionWire.Digest(record, "evidenceDigest") != OwnerAdmissionWire.Digest(obligation, "evidenceDigest"))
                throw Refuse("PackageProofTranscriptVectorMismatch");
        }

        var sourceMapBytes = ReadRole(snapshot, "source-map");
        using var sourceMap = ParsePackageArtifact(sourceMapBytes);
        if (OwnerAdmissionWire.Digest(proof, "sourceMapDigest") !=
            OwnerAdmissionWire.Hash("strogo.proof.v0.2/source-map", sourceMapBytes))
            throw Refuse("PackageProofSourceMapDigestMismatch");
    }

    private static JsonDocument ParsePackageArtifact(byte[] bytes)
    {
        try { return OwnerAdmissionWire.Parse(bytes); }
        catch (ModuleException error) when (error.Stage == "admission")
        {
            throw Refuse(error.Code);
        }
    }

    private static byte[] ReadRole(G02PackageSnapshot snapshot, string role)
        => snapshot.ReadHeld(snapshot.Manifest.Files.Single(file => file.Role == role).Path);

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("package", code);
}
