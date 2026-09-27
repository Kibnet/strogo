using System.Collections.Immutable;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Kernel.Core;

namespace Strogo.Experiments;

public static class LivePilotAcl
{
    public static void Restrict(string directory)
    {
        if (!OperatingSystem.IsWindows()) return;
        var currentSid = WindowsIdentity.GetCurrent().User ?? throw LivePilotJson.Failure("AclCurrentUserMissing");
        var systemSid = new SecurityIdentifier("S-1-5-18");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(currentSid, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(Path.GetFullPath(directory)), security);
    }

    public static AclReceipt Inspect(string directory, PilotManifest pilot)
    {
        if (!OperatingSystem.IsWindows()) return new(pilot.Protocol, pilot.PilotId, false, string.Empty, "S-1-5-18", false);
        DirectorySecurity security = FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(Path.GetFullPath(directory)));
        string currentSid = WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
        bool currentAllowed = false, systemAllowed = false, unexpectedAllowed = false;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            string sid = rule.IdentityReference.Value;
            if (sid == currentSid) currentAllowed = true;
            else if (sid == "S-1-5-18") systemAllowed = true;
            else unexpectedAllowed = true;
        }
        bool verified = security.AreAccessRulesProtected && currentAllowed && systemAllowed && !unexpectedAllowed;
        return new(pilot.Protocol, pilot.PilotId, security.AreAccessRulesProtected, currentSid, "S-1-5-18", verified);
    }
}

public static class LivePilotEvidence
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "evaluation.json", "raw-inventory.json" };

    public static EvidenceInventory Seal(string rawDirectory, string pilotId, string runId)
    {
        string root = Path.GetFullPath(rawDirectory);
        Directory.CreateDirectory(root);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !Excluded.Contains(Path.GetRelativePath(root, path).Replace('\\', '/')))
            .OrderBy(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal)
            .Select(path => Inspect(root, path))
            .ToImmutableArray();
        var skeleton = new EvidenceInventory(LivePilotIdentity.Protocol, pilotId, runId, files, string.Empty);
        var inventory = skeleton with { InventoryDigest = CanonicalJson.RawDigest(CanonicalJson.Encode(skeleton)) };
        string path = Path.Combine(root, "raw-inventory.json");
        if (File.Exists(path))
        {
            var existing = LivePilotJson.ReadCanonical<EvidenceInventory>(path);
            if (!CanonicalJson.Encode(existing).AsSpan().SequenceEqual(CanonicalJson.Encode(inventory))) throw LivePilotJson.Failure("RawEvidenceMutated");
            return existing;
        }
        LivePilotJson.WriteCanonical(path, inventory);
        return inventory;
    }

    public static EvidenceInventory Verify(string rawDirectory, string pilotId, string runId)
    {
        string path = Path.Combine(rawDirectory, "raw-inventory.json");
        if (!File.Exists(path)) throw LivePilotJson.Failure("EvidenceInventoryMissing");
        var inventory = LivePilotJson.ReadCanonical<EvidenceInventory>(path);
        if (inventory.Protocol != LivePilotIdentity.Protocol || inventory.PilotId != pilotId || inventory.RunId != runId)
            throw LivePilotJson.Failure("EvidenceInventoryIdentityMismatch");
        string digest = CanonicalJson.RawDigest(CanonicalJson.Encode(inventory with { InventoryDigest = string.Empty }));
        if (digest != inventory.InventoryDigest) throw LivePilotJson.Failure("EvidenceInventoryDigestMismatch");
        var actual = inventory.Files.Select(item => Inspect(Path.GetFullPath(rawDirectory), Path.Combine(rawDirectory, item.Path.Replace('/', Path.DirectorySeparatorChar)))).ToImmutableArray();
        if (!actual.SequenceEqual(inventory.Files)) throw LivePilotJson.Failure("RawEvidenceMutated");
        var expectedPaths = Directory.EnumerateFiles(rawDirectory, "*", SearchOption.AllDirectories)
            .Select(pathValue => Path.GetRelativePath(rawDirectory, pathValue).Replace('\\', '/'))
            .Where(relative => !Excluded.Contains(relative))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!expectedPaths.SequenceEqual(inventory.Files.Select(x => x.Path))) throw LivePilotJson.Failure("RawEvidenceMutated");
        return inventory;
    }

    public static ImmutableArray<string> ScanSensitive(string rawDirectory, params string[] allowedPathPrefixes)
    {
        string[] patterns = ["-----BEGIN PRIVATE KEY-----", "-----BEGIN OPENSSH PRIVATE KEY-----", "\"access_token\"", "\"refresh_token\"", "authorization: bearer "];
        var findings = ImmutableArray.CreateBuilder<string>();
        foreach (string file in Directory.EnumerateFiles(rawDirectory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(rawDirectory, file).Replace('\\', '/');
            if (relative is "evaluation.json" or "raw-inventory.json") continue;
            try
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) { findings.Add($"reparse:{relative}"); continue; }
                if (new FileInfo(file).Length > LivePilotIdentity.OutputCapBytes) { findings.Add($"oversize:{relative}"); continue; }
                string text = File.ReadAllText(file, new System.Text.UTF8Encoding(false, true));
                if (patterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase))) findings.Add($"secret:{relative}");
                string pathText = text.Replace("\\\\", "\\", StringComparison.Ordinal);
                var pathMatches = Regex.Matches(pathText, @"(?<![A-Za-z0-9_])[A-Za-z]:[\\/][^\""\r\n<>;,]+", RegexOptions.CultureInvariant)
                    .Cast<Match>()
                    .Concat(Regex.Matches(text, @"(?<![A-Za-z0-9_:\\])\\{2,4}[^\\/\s\""<>]+[\\/]{1,2}[^\""\r\n<>;,]+", RegexOptions.CultureInvariant).Cast<Match>());
                foreach (Match match in pathMatches)
                {
                    string candidate = match.Value.TrimEnd('`', '\'', ' ', '}', ']');
                    bool allowed = allowedPathPrefixes.Any(prefix => IsWithinAllowedRoot(candidate, prefix));
                    if (!allowed) findings.Add($"private-path:{relative}");
                }
            }
            catch { findings.Add($"unreadable:{relative}"); }
        }
        return findings.ToImmutable();
    }

    private static bool IsWithinAllowedRoot(string candidate, string prefix)
    {
        try
        {
            string separatorsNormalized = candidate.Replace('/', '\\');
            bool unc = separatorsNormalized.StartsWith("\\\\", StringComparison.Ordinal);
            string body = unc ? separatorsNormalized.TrimStart('\\') : separatorsNormalized;
            while (body.Contains("\\\\", StringComparison.Ordinal)) body = body.Replace("\\\\", "\\", StringComparison.Ordinal);
            string full = Path.GetFullPath(unc ? "\\\\" + body : body).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = Path.GetFullPath(prefix).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full.Equals(root, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static EvidenceFile Inspect(string root, string path)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw LivePilotJson.Failure("RawEvidenceReadFailure");
        byte[] bytes = File.ReadAllBytes(full);
        return new(Path.GetRelativePath(root, full).Replace('\\', '/'), CanonicalJson.RawDigest(bytes), bytes.LongLength);
    }
}
