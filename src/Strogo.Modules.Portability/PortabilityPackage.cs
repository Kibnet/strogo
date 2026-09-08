using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kernel.Core;

namespace Strogo.Modules.Portability;

public static class PortabilityPackageVersions
{
    public const string ManifestSchema = "strogo.portability-manifest.v0.1";
    public const string RuntimeRequirementSchema = "strogo.runtime-requirement.v0.1";
    public const string Purpose = "validation-only";
    public const string ContractStatus = "validation-fixture";
}

public sealed class PortabilityPackageContent
{
    private readonly byte[] bytes;

    public PortabilityPackageContent(string path, string role, ReadOnlySpan<byte> bytes)
    {
        Path = path;
        Role = role;
        this.bytes = bytes.ToArray();
    }

    public string Path { get; }
    public string Role { get; }
    public byte[] Bytes => bytes.ToArray();
}

public sealed record PortabilityPackageDefinition(
    string ProfileId,
    string ModuleDigest,
    string OwnerBundleDigest,
    string ProofDigest,
    string DafnySourceDigest,
    string TranslatorDigest,
    string BuildToolchainDigest,
    string RuntimeRequirementDigest,
    string PublicApiDigest,
    string EntryArtifactPath,
    IReadOnlyList<PortabilityPackageContent> Content);

public sealed record PortabilityPackageReceipt(
    string ProfileId,
    string ArtifactDigest,
    string PackageDigest,
    string PortabilityManifestDigest,
    ImmutableArray<PortabilityPackageFile> Files);

public sealed record PortabilityPackageFile(string Path, string Sha256, string Length, string Role);

public static class PortabilityPackage
{
    private static readonly Regex SegmentPattern = new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex DecimalPattern = new("^(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly ImmutableHashSet<string> Roles = ImmutableHashSet.Create(StringComparer.Ordinal,
        "module", "bundle", "proof", "proof-source", "source-map", "entry-artifact", "adapter", "runtime-dependency", "metadata");
    private static readonly ImmutableHashSet<string> InventoryRoles = ImmutableHashSet.Create(StringComparer.Ordinal,
        "archive", "configuration", "executable", "runtime", "source");
    private static readonly ImmutableHashSet<string> DeviceNames = CreateDeviceNames();

    public static PortabilityPackageReceipt Build(string packageDirectory, PortabilityPackageDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentNullException.ThrowIfNull(definition);
        if (Directory.Exists(packageDirectory) || File.Exists(packageDirectory)) Reject("PackageRootExists", "$package");

        var prepared = Prepare(definition);
        Directory.CreateDirectory(Path.Combine(packageDirectory, "content"));
        foreach (var file in prepared.Content)
        {
            var absolute = PhysicalPath(packageDirectory, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute) ?? throw new InvalidOperationException("content path has no directory"));
            File.WriteAllBytes(absolute, file.Bytes);
        }
        File.WriteAllBytes(Path.Combine(packageDirectory, "portability-manifest.json"), prepared.ManifestBytes);
        var expectedManifestDigest = PortabilityContract.DomainHash("strogo.portability.v0.1/manifest", prepared.ManifestBytes);
        return Validate(packageDirectory, expectedManifestDigest);
    }

    public static PortabilityPackageReceipt Validate(string packageDirectory, string expectedPortabilityManifestDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPortabilityManifestDigest);
        RequireDigest(expectedPortabilityManifestDigest, "expectedPortabilityManifestDigest");
        var root = Path.GetFullPath(packageDirectory);
        if (!Directory.Exists(root)) Reject("PackageRootMissing", "$package");
        RejectReparse(root, "$package");

        var rootEntries = Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        if (!rootEntries.SequenceEqual(new[] { "content", "portability-manifest.json" }, StringComparer.Ordinal))
            Reject("UnexpectedRootEntry", "$package");
        var contentRoot = Path.Combine(root, "content");
        var manifestPath = Path.Combine(root, "portability-manifest.json");
        if (!Directory.Exists(contentRoot) || !File.Exists(manifestPath)) Reject("PackageTreeInvalid", "$package");
        RejectReparse(contentRoot, "content");
        RejectReparse(manifestPath, "portability-manifest.json");

        var manifestBytes = File.ReadAllBytes(manifestPath);
        using var document = CanonicalJson.ParseStrict(Encoding.UTF8.GetString(manifestBytes));
        if (!CanonicalJson.Encode(document.RootElement).SequenceEqual(manifestBytes)) Reject("NonCanonicalManifest", "portability-manifest.json");
        var rootElement = document.RootElement;
        Fields(rootElement, "schemaVersion", "purpose", "contractStatus", "profileId", "moduleDigest", "ownerBundleDigest", "proofDigest", "dafnySourceDigest", "translatorDigest", "buildToolchainDigest", "runtimeRequirementDigest", "publicApiDigest", "entryArtifactPath", "files", "artifactDigest", "packageDigest");

        var definition = new PortabilityPackageDefinition(
            String(rootElement, "profileId"),
            Digest(rootElement, "moduleDigest"),
            Digest(rootElement, "ownerBundleDigest"),
            Digest(rootElement, "proofDigest"),
            Digest(rootElement, "dafnySourceDigest"),
            Digest(rootElement, "translatorDigest"),
            Digest(rootElement, "buildToolchainDigest"),
            Digest(rootElement, "runtimeRequirementDigest"),
            Digest(rootElement, "publicApiDigest"),
            String(rootElement, "entryArtifactPath"),
            []);
        if (String(rootElement, "schemaVersion") != PortabilityPackageVersions.ManifestSchema) Reject("SchemaMismatch", "$/schemaVersion");
        if (String(rootElement, "purpose") != PortabilityPackageVersions.Purpose) Reject("PurposeMismatch", "$/purpose");
        if (String(rootElement, "contractStatus") != PortabilityPackageVersions.ContractStatus) Reject("ContractStatusMismatch", "$/contractStatus");
        if (!PortabilityContract.IsExecutionProfile(definition.ProfileId)) Reject("UnknownProfile", "$/profileId");

        var declaredFiles = ParseFiles(rootElement.GetProperty("files"));
        ValidateFileSet(declaredFiles, definition);
        var actualTree = EnumerateContent(root, contentRoot);
        if (!actualTree.Files.Select(file => file.Path).SequenceEqual(declaredFiles.Select(file => file.Path), StringComparer.Ordinal))
            Reject("ContentInventoryMismatch", "$/files");
        for (var index = 0; index < declaredFiles.Length; index++)
        {
            var declared = declaredFiles[index];
            var actual = actualTree.Files[index];
            if (declared.Sha256 != actual.Sha256 || declared.Length != actual.Length)
                Reject("ContentDigestMismatch", $"$/files/{index}");
        }
        ValidateDirectoryClosure(actualTree.Directories, declaredFiles);
        ValidateBoundMetadata(root, definition, declaredFiles);

        var artifactDigest = PortabilityContract.DomainHash("strogo.portability.v0.1/artifact-set", CanonicalFilesBytes(declaredFiles));
        if (Digest(rootElement, "artifactDigest") != artifactDigest) Reject("ArtifactIdentityMismatch", "$/artifactDigest");
        var packageDigest = PortabilityContract.DomainHash("strogo.portability.v0.1/package", ManifestBytes(definition, declaredFiles, artifactDigest, null));
        if (Digest(rootElement, "packageDigest") != packageDigest) Reject("ArtifactIdentityMismatch", "$/packageDigest");
        var manifestDigest = PortabilityContract.DomainHash("strogo.portability.v0.1/manifest", manifestBytes);
        if (manifestDigest != expectedPortabilityManifestDigest) Reject("ArtifactIdentityMismatch", "portability-manifest.json");
        return new(definition.ProfileId, artifactDigest, packageDigest, manifestDigest, declaredFiles);
    }

    private static PreparedPackage Prepare(PortabilityPackageDefinition definition)
    {
        if (!PortabilityContract.IsExecutionProfile(definition.ProfileId)) Reject("UnknownProfile", "profileId");
        foreach (var digest in new[] { definition.ModuleDigest, definition.OwnerBundleDigest, definition.ProofDigest, definition.DafnySourceDigest, definition.TranslatorDigest, definition.BuildToolchainDigest, definition.RuntimeRequirementDigest, definition.PublicApiDigest })
            RequireDigest(digest, "definition");
        var inputContent = definition.Content;
        if (inputContent is null || inputContent.Count == 0) Reject("ContentMissing", "content");

        var content = inputContent.Select(file => new PortabilityPackageContent(file.Path, file.Role, file.Bytes)).OrderBy(file => file.Path, StringComparer.Ordinal).ToImmutableArray();
        var files = content.Select(file => new PortabilityPackageFile(file.Path, CanonicalJson.RawDigest(file.Bytes), file.Bytes.LongLength.ToString(CultureInfo.InvariantCulture), file.Role)).ToImmutableArray();
        ValidateFileSet(files, definition);
        ValidateBoundMetadata(content, definition, files);
        var artifactDigest = PortabilityContract.DomainHash("strogo.portability.v0.1/artifact-set", CanonicalFilesBytes(files));
        var packageDigest = PortabilityContract.DomainHash("strogo.portability.v0.1/package", ManifestBytes(definition, files, artifactDigest, null));
        var manifestBytes = ManifestBytes(definition, files, artifactDigest, packageDigest);
        return new(content, manifestBytes);
    }

    private static void ValidateFileSet(ImmutableArray<PortabilityPackageFile> files, PortabilityPackageDefinition definition)
    {
        if (files.IsEmpty) Reject("ContentMissing", "$/files");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var folded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            ValidatePath(file.Path);
            if (!paths.Add(file.Path) || !folded.Add(file.Path.ToUpperInvariant())) Reject("DuplicateContentPath", "$/files");
            if (!Roles.Contains(file.Role)) Reject("UnknownContentRole", $"$/files/{file.Path}/role");
            RequireDigest(file.Sha256, $"$/files/{file.Path}/sha256");
            if (!DecimalPattern.IsMatch(file.Length) || !long.TryParse(file.Length, NumberStyles.None, CultureInfo.InvariantCulture, out _)) Reject("InvalidContentLength", $"$/files/{file.Path}/length");
        }
        if (!files.Select(file => file.Path).SequenceEqual(files.Select(file => file.Path).Order(StringComparer.Ordinal), StringComparer.Ordinal)) Reject("ContentOrderMismatch", "$/files");
        var entryFiles = files.Where(file => file.Role == "entry-artifact").ToArray();
        if (entryFiles.Length != 1 || entryFiles[0].Path != definition.EntryArtifactPath) Reject("EntryArtifactMismatch", "$/entryArtifactPath");
        var expectedEntry = definition.ProfileId == PortabilityVersions.DotNetProfile ? "content/strogo.portable.v01.dll" : "content/strogo-portable-v01.jar";
        if (definition.EntryArtifactPath != expectedEntry) Reject("ProfileArtifactMismatch", "$/entryArtifactPath");
        if (files.Count(file => file.Path == "content/public-api.json" && file.Role == "metadata") != 1) Reject("PublicApiMissing", "$/files");
        if (files.Count(file => file.Path == "content/runtime-requirement.json" && file.Role == "metadata") != 1) Reject("RuntimeRequirementMissing", "$/files");
        if (files.Count(file => file.Path == "content/translator-inventory.json" && file.Role == "metadata") != 1) Reject("TranslatorInventoryMissing", "$/files");
        if (files.Count(file => file.Path == "content/build-toolchain-inventory.json" && file.Role == "metadata") != 1) Reject("BuildToolchainInventoryMissing", "$/files");
        if (files.Count(file => file.Path == "content/proof-toolchain.json" && file.Role == "metadata") != 1) Reject("ProofToolchainInventoryMissing", "$/files");
        if (files.Count(file => file.Path == "content/proof-closure.json" && file.Role == "metadata") != 1) Reject("ProofClosureInventoryMissing", "$/files");
        if (files.Count(file => file.Path == "content/proof-transcript.json" && file.Role == "metadata") != 1) Reject("ProofTranscriptMissing", "$/files");
    }

    private static void ValidateBoundMetadata(ImmutableArray<PortabilityPackageContent> content, PortabilityPackageDefinition definition, ImmutableArray<PortabilityPackageFile> files)
    {
        var byPath = content.ToDictionary(file => file.Path, file => file.Bytes, StringComparer.Ordinal);
        ValidateBoundMetadata(path => byPath[path], definition, files);
    }

    private static void ValidateBoundMetadata(string root, PortabilityPackageDefinition definition, ImmutableArray<PortabilityPackageFile> files)
    {
        ValidateBoundMetadata(path => File.ReadAllBytes(PhysicalPath(root, path)), definition, files);
    }

    private static void ValidateBoundMetadata(Func<string, byte[]> read, PortabilityPackageDefinition definition, ImmutableArray<PortabilityPackageFile> files)
    {
        var module = SingleRole(files, "module");
        var bundle = SingleRole(files, "bundle");
        var proof = SingleRole(files, "proof");
        var proofSources = files.Where(file => file.Role == "proof-source").ToImmutableArray();
        if (proofSources.Length == 0) Reject("ContentRoleCardinality", "$/files/proof-source");
        var sourceMap = SingleRole(files, "source-map");
        _ = SingleRole(files, "adapter");
        if (module.Sha256 != definition.ModuleDigest) Reject("ModuleDigestMismatch", "$/moduleDigest");
        var bundleBytes = read(bundle.Path);
        using (var bundleDocument = CanonicalJson.ParseStrict(Encoding.UTF8.GetString(bundleBytes)))
            if (!CanonicalJson.Encode(bundleDocument.RootElement).SequenceEqual(bundleBytes)) Reject("OwnerBundleInvalid", bundle.Path);
        if (OwnerBundleV04Codec.BundleDigest(bundleBytes) != definition.OwnerBundleDigest) Reject("OwnerBundleDigestMismatch", "$/ownerBundleDigest");
        if (proofSources.Length != 1 || proofSources[0].Sha256 != definition.DafnySourceDigest) Reject("DafnySourceDigestMismatch", "$/dafnySourceDigest");

        var proofBytes = read(proof.Path);
        PortabilityValidationProof.Validate(
            proofBytes,
            definition.ProofDigest,
            definition.ModuleDigest,
            definition.OwnerBundleDigest,
            PortabilityValidationProof.ProofSourcesDigest(proofSources),
            PortabilityValidationProof.SourceMapDigest(read(sourceMap.Path)),
            read("content/proof-toolchain.json"),
            read("content/proof-closure.json"),
            read("content/proof-transcript.json"));

        var api = read("content/public-api.json");
        var runtime = read("content/runtime-requirement.json");
        var translator = read("content/translator-inventory.json");
        var buildToolchain = read("content/build-toolchain-inventory.json");
        if (PortabilityContract.DomainHash("strogo.public-api.v0.1/artifact", api) != definition.PublicApiDigest) Reject("PublicApiDigestMismatch", "$/publicApiDigest");
        if (PortabilityContract.DomainHash($"strogo.portability.v0.1/runtime-requirement/{definition.ProfileId}", runtime) != definition.RuntimeRequirementDigest) Reject("RuntimeRequirementDigestMismatch", "$/runtimeRequirementDigest");
        if (PortabilityContract.DomainHash($"strogo.portability.v0.1/translator/{definition.ProfileId}", translator) != definition.TranslatorDigest) Reject("TranslatorDigestMismatch", "$/translatorDigest");
        if (PortabilityContract.DomainHash($"strogo.portability.v0.1/build-toolchain/{definition.ProfileId}", buildToolchain) != definition.BuildToolchainDigest) Reject("BuildToolchainDigestMismatch", "$/buildToolchainDigest");
        using var apiDocument = CanonicalJson.ParseStrict(Encoding.UTF8.GetString(api));
        if (!CanonicalJson.Encode(apiDocument.RootElement).SequenceEqual(api) || String(apiDocument.RootElement, "schemaVersion") != PortabilityVersions.PublicApiSchema) Reject("PublicApiInvalid", "content/public-api.json");
        using var runtimeDocument = CanonicalJson.ParseStrict(Encoding.UTF8.GetString(runtime));
        if (!CanonicalJson.Encode(runtimeDocument.RootElement).SequenceEqual(runtime)) Reject("RuntimeRequirementInvalid", "content/runtime-requirement.json");
        Fields(runtimeDocument.RootElement, "schemaVersion", "profileId", "vmFamily", "vmMajor", "dynamicFeatures", "nativeFeatures");
        if (String(runtimeDocument.RootElement, "schemaVersion") != PortabilityPackageVersions.RuntimeRequirementSchema || String(runtimeDocument.RootElement, "profileId") != definition.ProfileId) Reject("RuntimeRequirementInvalid", "content/runtime-requirement.json");
        var expectedVmFamily = definition.ProfileId == PortabilityVersions.DotNetProfile ? "dotnet" : "java";
        var expectedVmMajor = definition.ProfileId == PortabilityVersions.DotNetProfile ? "10" : "17";
        var expectedDynamic = definition.ProfileId == PortabilityVersions.DotNetProfile ? new[] { "jit", "managed-loader" } : new[] { "class-loader", "jit" };
        if (String(runtimeDocument.RootElement, "vmFamily") != expectedVmFamily || String(runtimeDocument.RootElement, "vmMajor") != expectedVmMajor || !StringArray(runtimeDocument.RootElement, "dynamicFeatures").SequenceEqual(expectedDynamic, StringComparer.Ordinal) || StringArray(runtimeDocument.RootElement, "nativeFeatures").Length != 0)
            Reject("RuntimeRequirementInvalid", "content/runtime-requirement.json");
        ValidateInventory(translator, definition.ProfileId, "content/translator-inventory.json");
        ValidateInventory(buildToolchain, definition.ProfileId, "content/build-toolchain-inventory.json");
    }

    private static void ValidateInventory(byte[] bytes, string profileId, string locus)
    {
        using var document = CanonicalJson.ParseStrict(Encoding.UTF8.GetString(bytes));
        if (!CanonicalJson.Encode(document.RootElement).SequenceEqual(bytes)) Reject("ToolInventoryInvalid", locus);
        Fields(document.RootElement, "schemaVersion", "profileId", "files", "versions");
        if (String(document.RootElement, "schemaVersion") != "strogo.tool-inventory.v0.1" || String(document.RootElement, "profileId") != profileId) Reject("ToolInventoryInvalid", locus);
        var filesElement = document.RootElement.GetProperty("files");
        var versionsElement = document.RootElement.GetProperty("versions");
        if (filesElement.ValueKind != JsonValueKind.Array || versionsElement.ValueKind != JsonValueKind.Array) Reject("ToolInventoryInvalid", locus);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var foldedPaths = new HashSet<string>(StringComparer.Ordinal);
        var previousPath = "";
        foreach (var file in filesElement.EnumerateArray())
        {
            Fields(file, "path", "sha256", "length", "role");
            var path = String(file, "path");
            ValidateInventoryPath(path);
            if ((previousPath.Length > 0 && string.CompareOrdinal(previousPath, path) >= 0) || !paths.Add(path) || !foldedPaths.Add(path.ToUpperInvariant())) Reject("ToolInventoryInvalid", locus);
            previousPath = path;
            _ = Digest(file, "sha256");
            var length = String(file, "length");
            if (!DecimalPattern.IsMatch(length) || !long.TryParse(length, NumberStyles.None, CultureInfo.InvariantCulture, out _)) Reject("ToolInventoryInvalid", locus);
            if (!InventoryRoles.Contains(String(file, "role"))) Reject("ToolInventoryInvalid", locus);
        }
        var components = new HashSet<string>(StringComparer.Ordinal);
        var previousComponent = "";
        foreach (var version in versionsElement.EnumerateArray())
        {
            Fields(version, "component", "version");
            var component = String(version, "component");
            if (!SegmentPattern.IsMatch(component) || (previousComponent.Length > 0 && string.CompareOrdinal(previousComponent, component) >= 0) || !components.Add(component)) Reject("ToolInventoryInvalid", locus);
            previousComponent = component;
            if (String(version, "version").Length == 0) Reject("ToolInventoryInvalid", locus);
        }
    }

    private static PortabilityPackageFile SingleRole(ImmutableArray<PortabilityPackageFile> files, string role)
    {
        var matches = files.Where(file => file.Role == role).ToArray();
        if (matches.Length != 1) Reject("ContentRoleCardinality", "$/files/" + role);
        return matches[0];
    }

    private static ImmutableArray<PortabilityPackageFile> ParseFiles(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array) Reject("FilesInvalid", "$/files");
        return element.EnumerateArray().Select((file, index) =>
        {
            Fields(file, "path", "sha256", "length", "role");
            return new PortabilityPackageFile(String(file, "path"), Digest(file, "sha256"), String(file, "length"), String(file, "role"));
        }).ToImmutableArray();
    }

    private static ActualTree EnumerateContent(string root, string contentRoot)
    {
        var files = ImmutableArray.CreateBuilder<PortabilityPackageFile>();
        var directories = ImmutableArray.CreateBuilder<string>();
        var pending = new Stack<string>();
        pending.Push(contentRoot);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                ValidatePath(relative);
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) Reject("ReparsePointRejected", relative);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(relative);
                    pending.Push(path);
                }
                else
                {
                    var bytes = File.ReadAllBytes(path);
                    files.Add(new(relative, CanonicalJson.RawDigest(bytes), bytes.LongLength.ToString(CultureInfo.InvariantCulture), ""));
                }
            }
        }
        return new(files.OrderBy(file => file.Path, StringComparer.Ordinal).ToImmutableArray(), directories.Order(StringComparer.Ordinal).ToImmutableArray());
    }

    private static void ValidateDirectoryClosure(ImmutableArray<string> actualDirectories, ImmutableArray<PortabilityPackageFile> files)
    {
        var expected = files.SelectMany(file => ParentPaths(file.Path)).Where(path => path != "content").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!actualDirectories.SequenceEqual(expected, StringComparer.Ordinal)) Reject("UnexpectedDirectory", "content");
    }

    private static IEnumerable<string> ParentPaths(string path)
    {
        var segments = path.Split('/');
        for (var count = 1; count < segments.Length; count++) yield return string.Join('/', segments.Take(count));
    }

    private static byte[] CanonicalFilesBytes(IEnumerable<PortabilityPackageFile> files) => CanonicalJson.Encode(files.Select(file => new { path = file.Path, sha256 = file.Sha256, length = file.Length, role = file.Role }).ToArray());

    private static byte[] ManifestBytes(PortabilityPackageDefinition definition, ImmutableArray<PortabilityPackageFile> files, string artifactDigest, string? packageDigest)
    {
        var withoutPackage = new
        {
            schemaVersion = PortabilityPackageVersions.ManifestSchema,
            purpose = PortabilityPackageVersions.Purpose,
            contractStatus = PortabilityPackageVersions.ContractStatus,
            profileId = definition.ProfileId,
            moduleDigest = definition.ModuleDigest,
            ownerBundleDigest = definition.OwnerBundleDigest,
            proofDigest = definition.ProofDigest,
            dafnySourceDigest = definition.DafnySourceDigest,
            translatorDigest = definition.TranslatorDigest,
            buildToolchainDigest = definition.BuildToolchainDigest,
            runtimeRequirementDigest = definition.RuntimeRequirementDigest,
            publicApiDigest = definition.PublicApiDigest,
            entryArtifactPath = definition.EntryArtifactPath,
            files = files.Select(file => new { path = file.Path, sha256 = file.Sha256, length = file.Length, role = file.Role }).ToArray(),
            artifactDigest
        };
        if (packageDigest is null) return CanonicalJson.Encode(withoutPackage);
        return CanonicalJson.Encode(new
        {
            withoutPackage.schemaVersion, withoutPackage.purpose, withoutPackage.contractStatus, withoutPackage.profileId,
            withoutPackage.moduleDigest, withoutPackage.ownerBundleDigest, withoutPackage.proofDigest, withoutPackage.dafnySourceDigest,
            withoutPackage.translatorDigest, withoutPackage.buildToolchainDigest, withoutPackage.runtimeRequirementDigest,
            withoutPackage.publicApiDigest, withoutPackage.entryArtifactPath, withoutPackage.files, withoutPackage.artifactDigest,
            packageDigest
        });
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || path.Contains(':') || path.StartsWith('/') || !path.StartsWith("content/", StringComparison.Ordinal)) Reject("InvalidContentPath", path);
        var segments = path.Split('/');
        if (segments.Length < 2 || segments.Any(segment => segment is "." or ".." || !SegmentPattern.IsMatch(segment))) Reject("InvalidContentPath", path);
        foreach (var segment in segments)
        {
            var stem = segment.Split('.')[0];
            if (DeviceNames.Contains(stem)) Reject("InvalidContentPath", path);
        }
    }

    private static void ValidateInventoryPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || path.Contains(':') || path.StartsWith('/')) Reject("InvalidInventoryPath", path);
        var segments = path.Split('/');
        if (segments.Any(segment => segment is "." or ".." || !SegmentPattern.IsMatch(segment))) Reject("InvalidInventoryPath", path);
        foreach (var segment in segments)
            if (DeviceNames.Contains(segment.Split('.')[0])) Reject("InvalidInventoryPath", path);
    }

    private static string PhysicalPath(string root, string relative)
    {
        ValidatePath(relative);
        var rootFull = Path.GetFullPath(root);
        var result = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)) Reject("InvalidContentPath", relative);
        return result;
    }

    private static void RejectReparse(string path, string locus)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) Reject("ReparsePointRejected", locus);
    }

    private static void Fields(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) Reject("ExpectedObject", "$json");
        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != names.Length || actual.Distinct(StringComparer.Ordinal).Count() != actual.Length || actual.Any(name => !names.Contains(name, StringComparer.Ordinal))) Reject("UnexpectedFields", "$json");
    }

    private static string String(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) Reject("ExpectedString", "$" + property);
        return value.GetString()!;
    }

    private static string Digest(JsonElement element, string property)
    {
        var value = String(element, property);
        RequireDigest(value, "$" + property);
        return value;
    }

    private static string[] StringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array) Reject("ExpectedArray", "$" + property);
        var result = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : throw new PortabilityContractException("PortabilityPackageRejected", "$" + property, new { reason = "ExpectedString" })).ToArray();
        if (result.Distinct(StringComparer.Ordinal).Count() != result.Length || !result.SequenceEqual(result.Order(StringComparer.Ordinal), StringComparer.Ordinal)) Reject("InvalidFeatureSet", "$" + property);
        return result;
    }

    private static void RequireDigest(string digest, string locus)
    {
        if (!DigestPattern.IsMatch(digest)) Reject("InvalidDigest", locus);
    }

    private static ImmutableHashSet<string> CreateDeviceNames()
    {
        var names = new[] { "CON", "PRN", "AUX", "NUL" }.Concat(Enumerable.Range(1, 9).SelectMany(index => new[] { $"COM{index}", $"LPT{index}" }));
        return names.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }

    [DoesNotReturn]
    private static void Reject(string reason, string locus) => throw new PortabilityContractException("PortabilityPackageRejected", locus, new { reason });

    private sealed record PreparedPackage(ImmutableArray<PortabilityPackageContent> Content, byte[] ManifestBytes);
    private sealed record ActualTree(ImmutableArray<PortabilityPackageFile> Files, ImmutableArray<string> Directories);
}
