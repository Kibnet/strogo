using System.Collections.Immutable;
using System.Security.Cryptography;

namespace Strogo.Modules;

internal sealed record G02PublishedFile(string Name, long Length, string Sha256);

/// <summary>Held private physical publish inventory. This does not define public closureDigest or package roles.</summary>
internal static class G02PublishInventory
{
    internal static ImmutableArray<G02PublishedFile> CaptureFixture(string publishDirectory, List<FileStream> held)
    {
        var root = G02WindowsHeldHandle.OpenDirectory(publishDirectory, null);
        using var directory = root.Handle;
        var before = Directory.EnumerateFileSystemEntries(publishDirectory).Order(StringComparer.Ordinal).ToArray();
        if (before.Length is < 2 or > 1024) throw Refuse("BuildPublishInventorySizeInvalid");
        var files = ImmutableArray.CreateBuilder<G02PublishedFile>(before.Length);
        foreach (var path in before)
        {
            if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw Refuse("BuildPublishLayoutInvalid");
            var originalName = Path.GetFileName(path);
            if (originalName.Any(character => character > 127)) throw Refuse("BuildPublishNameInvalid");
            var name = originalName.ToLowerInvariant();
            ValidateName(name);
            var file = G02WindowsHeldHandle.OpenFile(path, root.FinalPath + "\\" + originalName);
            held.Add(file);
            if (file.Length > 67_108_864) throw Refuse("BuildPublishFileSizeInvalid");
            var length = file.Length;
            var hash = Convert.ToHexStringLower(SHA256.HashData(file));
            file.Position = 0;
            if (file.Length != length) throw Refuse("BuildPublishFileChanged");
            files.Add(new(name, length, hash));
        }
        var inventory = Validate(files.ToImmutable());
        if (!before.SequenceEqual(Directory.EnumerateFileSystemEntries(publishDirectory).Order(StringComparer.Ordinal),
                StringComparer.Ordinal)) throw Refuse("BuildPublishInventoryChanged");
        return inventory;
    }

    internal static string DiagnosticDigest(IEnumerable<G02PublishedFile> files)
    {
        var inventory = Validate(files);
        var payload = G02ProofTranscript.Canonical(new
        {
            schemaVersion = "strogo.publish-inventory.v0.2", runtimeIdentifier = "win-x64",
            files = inventory.Select(file => new { name = file.Name, sha256 = file.Sha256, length = file.Length }).ToArray()
        });
        return OwnerAdmissionWire.Hash("strogo.build.v0.2/publish-inventory", payload);
    }

    private static ImmutableArray<G02PublishedFile> Validate(IEnumerable<G02PublishedFile> files)
    {
        var inventory = files.OrderBy(file => file.Name, StringComparer.Ordinal).ToImmutableArray();
        if (inventory.Length is < 2 or > 1024) throw Refuse("BuildPublishInventorySizeInvalid");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in inventory)
        {
            ValidateName(file.Name);
            if (!names.Add(file.Name)) throw Refuse("BuildPublishInventoryDuplicate");
            if (file.Length is < 0 or > 67_108_864 || file.Sha256.Length != 64 ||
                file.Sha256.Any(character => !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
                throw Refuse("BuildPublishIdentityInvalid");
        }
        if (!names.Contains("strogo.generated.dll") || !names.Contains("strogo.generated.deps.json"))
            throw Refuse("BuildPublishEntryMissing");
        return inventory;
    }

    private static void ValidateName(string name)
    {
        if (name != name.ToLowerInvariant() || name.IndexOfAny(['/', '\\']) >= 0)
            throw Refuse("BuildPublishNameInvalid");
        G02BuildManifest.ValidatePath("content/" + name);
    }

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("package", code);
}
