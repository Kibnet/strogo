using System.Collections.Immutable;
using System.Text.Json;

namespace Strogo.Modules;

public sealed record G02ManifestFile(string Path, string Sha256, long Length, string Role);

/// <summary>Structural manifest identity only. This does not verify content, proof or admission.</summary>
public sealed class G02BuildManifest
{
    private static readonly HashSet<string> Roles = new(StringComparer.Ordinal)
    {
        "module", "bundle", "contract-approval", "proof", "proof-source", "proof-transcript",
        "source-map", "entry-assembly", "deps", "runtime-dependency"
    };
    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"
    };

    private G02BuildManifest(string moduleDigest, string bundleDigest, string contractApprovalDigest,
        string proofDigest, string toolchainDigest, string closureDigest, string entryAssemblyPath,
        string packageDigest, string artifactDigest, ImmutableArray<G02ManifestFile> files)
    {
        ModuleDigest = moduleDigest;
        BundleDigest = bundleDigest;
        ContractApprovalDigest = contractApprovalDigest;
        ProofDigest = proofDigest;
        ToolchainDigest = toolchainDigest;
        ClosureDigest = closureDigest;
        EntryAssemblyPath = entryAssemblyPath;
        PackageDigest = packageDigest;
        ArtifactDigest = artifactDigest;
        Files = files;
    }

    public string ModuleDigest { get; }
    public string BundleDigest { get; }
    public string ContractApprovalDigest { get; }
    public string ProofDigest { get; }
    public string ToolchainDigest { get; }
    public string ClosureDigest { get; }
    public string EntryAssemblyPath { get; }
    public string PackageDigest { get; }
    public string ArtifactDigest { get; }
    public ImmutableArray<G02ManifestFile> Files { get; }

    public static G02BuildManifest Parse(ReadOnlySpan<byte> manifestBytes)
    {
        try { return ParseCore(manifestBytes); }
        catch (ModuleException error) when (error.Stage == "admission")
        {
            throw Refuse(error.Code);
        }
    }

    private static G02BuildManifest ParseCore(ReadOnlySpan<byte> manifestBytes)
    {
        using var document = OwnerAdmissionWire.Parse(manifestBytes);
        var root = document.RootElement;
        OwnerAdmissionWire.Fields(root, "schemaVersion", "moduleDigest", "bundleDigest", "contractApprovalDigest",
            "proofDigest", "toolchainDigest", "closureDigest", "runtimeIdentifier", "entryAssemblyPath",
            "files", "packageDigest");
        OwnerAdmissionWire.Equal(root, "schemaVersion", "strogo.build-manifest.v0.2");
        OwnerAdmissionWire.Equal(root, "runtimeIdentifier", "win-x64");
        var moduleDigest = OwnerAdmissionWire.Digest(root, "moduleDigest");
        var bundleDigest = OwnerAdmissionWire.Digest(root, "bundleDigest");
        var approvalDigest = OwnerAdmissionWire.Digest(root, "contractApprovalDigest");
        var proofDigest = OwnerAdmissionWire.Digest(root, "proofDigest");
        var toolchainDigest = OwnerAdmissionWire.Digest(root, "toolchainDigest");
        var closureDigest = OwnerAdmissionWire.Digest(root, "closureDigest");
        var entryPath = OwnerAdmissionWire.NonEmpty(root, "entryAssemblyPath");
        ValidatePath(entryPath);
        var filesElement = root.GetProperty("files");
        if (filesElement.ValueKind != JsonValueKind.Array || filesElement.GetArrayLength() is < 1 or > 1024)
            throw Refuse("ManifestFilesInvalid");
        var files = ImmutableArray.CreateBuilder<G02ManifestFile>(filesElement.GetArrayLength());
        string? previous = null;
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in filesElement.EnumerateArray())
        {
            OwnerAdmissionWire.Fields(item, "path", "sha256", "length", "role");
            var path = OwnerAdmissionWire.NonEmpty(item, "path");
            ValidatePath(path);
            if (previous is not null && string.CompareOrdinal(previous, path) >= 0)
                throw Refuse("ManifestFileOrderInvalid");
            if (!folded.Add(path)) throw Refuse("ManifestPathCollision");
            previous = path;
            var role = OwnerAdmissionWire.NonEmpty(item, "role");
            if (!Roles.Contains(role)) throw Refuse("ManifestRoleInvalid");
            files.Add(new(path, OwnerAdmissionWire.Digest(item, "sha256"),
                OwnerAdmissionWire.NonNegative(item, "length"), role));
        }
        var list = files.MoveToImmutable();
        foreach (var role in new[] { "module", "bundle", "contract-approval", "proof", "proof-transcript",
                     "source-map", "entry-assembly", "deps" })
            if (list.Count(file => file.Role == role) != 1)
                throw Refuse("ManifestRoleCardinalityInvalid");
        if (!list.Any(file => file.Role == "proof-source") ||
            list.Single(file => file.Role == "entry-assembly").Path != entryPath)
            throw Refuse("ManifestEntryInvalid");
        var packageDigest = OwnerAdmissionWire.Digest(root, "packageDigest");
        if (OwnerAdmissionWire.Hash("strogo.package.v0.2/manifest-payload",
                OwnerAdmissionWire.CanonicalWithout(root, "packageDigest")) != packageDigest)
            throw Refuse("PackageDigestMismatch");
        return new(moduleDigest, bundleDigest, approvalDigest, proofDigest, toolchainDigest, closureDigest,
            entryPath, packageDigest,
            OwnerAdmissionWire.Hash("strogo.build-manifest.v0.2/artifact", manifestBytes), list);
    }

    public static void ValidatePath(string path)
    {
        if (!path.StartsWith("content/", StringComparison.Ordinal) || path.Length <= "content/".Length)
            throw Refuse("ManifestPathInvalid");
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." ||
                !(segment[0] is >= 'a' and <= 'z' or >= '0' and <= '9') ||
                segment.Skip(1).Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-')) ||
                DeviceNames.Contains(segment.Split('.')[0]))
                throw Refuse("ManifestPathInvalid");
        }
    }

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("package", code);
}
