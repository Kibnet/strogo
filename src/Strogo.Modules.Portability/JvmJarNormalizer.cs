using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Kernel.Core;

namespace Strogo.Modules.Portability;

public sealed record JvmJarExpectedEntry(string Path, string Role, string Sha256, string Length);

public sealed record JvmJarInventoryEntry(string Path, string Role, string Sha256, string Length);

public sealed record JvmJarInventory(ImmutableArray<JvmJarInventoryEntry> Entries)
{
    public byte[] CanonicalBytes() => CanonicalJson.Encode(Entries.Select(entry => new
    {
        path = entry.Path,
        role = entry.Role,
        sha256 = entry.Sha256,
        length = entry.Length
    }).ToArray());
}

public sealed record JvmJarNormalizationRequest(
    string InputRoot,
    string RunRoot,
    string FinalPath,
    string JarExecutable,
    string ProfileId,
    string SourceRevision,
    string ToolchainIdentity,
    IReadOnlyList<JvmJarExpectedEntry> ExpectedEntries,
    TimeSpan Timeout,
    int OutputByteLimit,
    TimeSpan CleanupTimeout);

public sealed record JvmJarBuildReceipt(
    string SchemaVersion,
    string ProfileId,
    string SourceRevision,
    string ToolchainIdentity,
    string ManifestDigest,
    string ArgfileDigest,
    string JarDigest,
    string JarExitCode,
    string JarElapsedMilliseconds,
    string StdoutDigest,
    string StderrDigest,
    JvmJarInventory InputInventory,
    JvmJarInventory Inventory)
{
    public byte[] CanonicalBytes() => CanonicalJson.Encode(new
    {
        schemaVersion = SchemaVersion,
        profileId = ProfileId,
        sourceRevision = SourceRevision,
        toolchainIdentity = ToolchainIdentity,
        manifestDigest = ManifestDigest,
        argfileDigest = ArgfileDigest,
        jarDigest = JarDigest,
        jarExitCode = JarExitCode,
        jarElapsedMilliseconds = JarElapsedMilliseconds,
        stdoutDigest = StdoutDigest,
        stderrDigest = StderrDigest,
        inputInventory = InputInventory.Entries.Select(entry => new
        {
            path = entry.Path,
            role = entry.Role,
            sha256 = entry.Sha256,
            length = entry.Length
        }).ToArray(),
        inventory = Inventory.Entries.Select(entry => new
        {
            path = entry.Path,
            role = entry.Role,
            sha256 = entry.Sha256,
            length = entry.Length
        }).ToArray()
    });
}

public static class JvmJarNormalizer
{
    public const string SchemaVersion = "strogo.jvm-jar.v0.1";
    public const string ManifestPath = "META-INF/MANIFEST.MF";
    public const string ManifestText = "Manifest-Version: 1.0\r\nAutomatic-Module-Name: strogo.portable.v01\r\n\r\n";
    public const string FixedTimestamp = "1980-01-01T00:00:02Z";
    public const int MaximumEntries = 4096;
    public const long MaximumEntryBytes = 16 * 1024 * 1024;
    public const long MaximumAggregateBytes = 64 * 1024 * 1024;

    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const uint CentralDirectorySignature = 0x02014b50;
    private const uint LocalFileSignature = 0x04034b50;
    private const ushort Utf8Flag = 0x0800;
    private static readonly byte[] JarMarker = [0xfe, 0xca, 0x00, 0x00];
    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex SourceRevisionPattern = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex DecimalPattern = new("^(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex SegmentPattern = new("^[A-Za-z0-9_$][A-Za-z0-9._$-]*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly byte[] ManifestBytes = Encoding.UTF8.GetBytes(ManifestText);
    private static readonly ImmutableHashSet<string> InputRoles = ImmutableHashSet.Create(StringComparer.Ordinal, "class", "adapter", "runtime-dependency");

    public static JvmJarInventory ValidateJar(ReadOnlySpan<byte> bytes, IReadOnlyList<JvmJarExpectedEntry> expectedEntries)
    {
        ArgumentNullException.ThrowIfNull(expectedEntries);
        ValidateExpectedEntries(expectedEntries);
        if (expectedEntries.Count + 1 > MaximumEntries) Reject("EntryCountExceeded", "$/entries");
        var expectedArchive = new[] { new JvmJarExpectedEntry(ManifestPath, "metadata", Digest(ManifestBytes), Decimal(ManifestBytes.Length)) }
            .Concat(expectedEntries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
            .ToArray();
        try
        {
            return new(ValidateArchive(bytes.ToArray(), expectedArchive));
        }
        catch (PortabilityContractException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Reject("ZipMalformed", "$/jar", new { failure = exception.GetType().Name });
            throw;
        }
    }

    public static JvmJarBuildReceipt Normalize(JvmJarNormalizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var inputRoot = Path.GetFullPath(request.InputRoot);
        var runRoot = Path.GetFullPath(request.RunRoot);
        var finalPath = Path.GetFullPath(request.FinalPath);
        var staging = Path.Combine(runRoot, "staging");
        var quarantine = Path.Combine(runRoot, "candidate.jar");
        var receiptPath = Path.Combine(runRoot, "receipt.json");
        var promoted = false;

        try
        {
            Directory.CreateDirectory(runRoot);
            Directory.CreateDirectory(staging);
            var input = ReadInputInventory(inputRoot, request.ExpectedEntries);
            var inputInventory = new JvmJarInventory(input.Select(item => new JvmJarInventoryEntry(item.Expected.Path, item.Expected.Role, item.Expected.Sha256, item.Expected.Length)).ToImmutableArray());
            File.WriteAllBytes(Path.Combine(runRoot, "input-inventory.json"), inputInventory.CanonicalBytes());
            var expectedArchive = new[] { new JvmJarExpectedEntry(ManifestPath, "metadata", Digest(ManifestBytes), Decimal(ManifestBytes.Length)) }
                .Concat(input.Select(item => item.Expected).OrderBy(entry => entry.Path, StringComparer.Ordinal))
                .ToArray();
            WriteStaging(staging, input);
            var argfileBytes = WriteArgfile(staging, expectedArchive);

            var process = JvmProcessRunner.Run(new JvmProcessRequest(
                request.JarExecutable,
                staging,
                ["--create", "--file", quarantine, "--no-compress", $"--date={FixedTimestamp}", "--no-manifest", "@entries.argfile"],
                ["CLASSPATH", "JDK_JAVAC_OPTIONS", "JDK_JAVA_OPTIONS", "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS"],
                request.Timeout,
                request.OutputByteLimit,
                request.CleanupTimeout,
                JvmProcessKind.JarPackaging));
            if (process.ExitCode != 0)
                Reject("JarInvocationFailed", "$/process/exitCode", new { exitCode = process.ExitCode.ToString(CultureInfo.InvariantCulture) });
            if (process.StdoutBytes != 0 || process.StderrBytes != 0)
                Reject("JarDiagnostics", "$/process/diagnostics", new { stdoutBytes = process.StdoutBytes.ToString(CultureInfo.InvariantCulture), stderrBytes = process.StderrBytes.ToString(CultureInfo.InvariantCulture) });
            if (!File.Exists(quarantine)) Reject("JarOutputMissing", "$/jar");

            var jarBytes = File.ReadAllBytes(quarantine);
            var inventory = ValidateArchive(jarBytes, expectedArchive);
            var finalInventory = new JvmJarInventory(inventory);
            File.WriteAllBytes(Path.Combine(runRoot, "final-inventory.json"), finalInventory.CanonicalBytes());
            var receipt = new JvmJarBuildReceipt(
                SchemaVersion,
                request.ProfileId,
                request.SourceRevision,
                request.ToolchainIdentity,
                Digest(ManifestBytes),
                Digest(argfileBytes),
                Digest(jarBytes),
                process.ExitCode.ToString(CultureInfo.InvariantCulture),
                process.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
                process.StdoutDigest,
                process.StderrDigest,
                inputInventory,
                finalInventory);
            File.WriteAllBytes(receiptPath, receipt.CanonicalBytes());

            DeleteContained(staging, runRoot);
            if (Directory.Exists(staging)) throw new IOException("staging cleanup left residual directory");
            if (File.Exists(finalPath) || Directory.Exists(finalPath)) Reject("FinalPathExists", "$/finalPath");
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? throw new InvalidOperationException("final path has no parent"));
            File.Move(quarantine, finalPath, false);
            promoted = true;
            return receipt;
        }
        catch (PortabilityContractException)
        {
            CleanupFailure(runRoot, staging, quarantine, promoted);
            throw;
        }
        catch (Exception exception)
        {
            CleanupFailure(runRoot, staging, quarantine, promoted);
            throw new PortabilityContractException("TargetBuildRejected", "$/jar", new { reason = "NormalizerFailure", failure = exception.GetType().Name, message = exception.Message });
        }
    }

    private static void ValidateRequest(JvmJarNormalizationRequest request)
    {
        if (!Path.IsPathFullyQualified(request.InputRoot) || !Directory.Exists(request.InputRoot)) Reject("InputRootMissing", "$/inputRoot");
        if (!Path.IsPathFullyQualified(request.RunRoot) || Directory.Exists(request.RunRoot) || File.Exists(request.RunRoot)) Reject("RunRootExists", "$/runRoot");
        if (!Path.IsPathFullyQualified(request.FinalPath)) Reject("FinalPathInvalid", "$/finalPath");
        if (File.Exists(request.FinalPath) || Directory.Exists(request.FinalPath)) Reject("FinalPathExists", "$/finalPath");
        if (!Path.IsPathFullyQualified(request.JarExecutable) || !File.Exists(request.JarExecutable)) Reject("JarExecutableMissing", "$/jarExecutable");
        if (request.ProfileId != PortabilityVersions.JvmProfile) Reject("ProfileMismatch", "$/profileId");
        if (!SourceRevisionPattern.IsMatch(request.SourceRevision) || string.IsNullOrEmpty(request.ToolchainIdentity) || request.ToolchainIdentity.Any(character => character < 0x21 || character > 0x7e)) Reject("IdentityInvalid", "$/identity");
        if (request.Timeout <= TimeSpan.Zero || request.CleanupTimeout <= TimeSpan.Zero || request.OutputByteLimit <= 0) Reject("ProcessLimitsInvalid", "$/limits");
        if (request.ExpectedEntries is null || request.ExpectedEntries.Count == 0 || request.ExpectedEntries.Count + 1 > MaximumEntries) Reject("EntryCountExceeded", "$/entries");
        if (PathStartsWithin(request.FinalPath, request.RunRoot)) Reject("FinalPathInsideRunRoot", "$/finalPath");
        var finalParent = Path.GetDirectoryName(Path.GetFullPath(request.FinalPath));
        if (finalParent is null || !Directory.Exists(finalParent)) Reject("FinalParentMissing", "$/finalPath");
        RejectReparse(finalParent!, "$/finalPath/parent");
        ValidateExpectedEntries(request.ExpectedEntries!);
    }

    private static void ValidateExpectedEntries(IReadOnlyList<JvmJarExpectedEntry> entries)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            ValidateLogicalPath(entry.Path, "$/entries/path");
            if (entry.Path == ManifestPath) Reject("ManifestInputForbidden", "$/entries/path");
            if (!paths.Add(entry.Path)) Reject("DuplicateEntry", "$/entries/path");
            if (!folded.Add(entry.Path)) Reject("CaseFoldDuplicateEntry", "$/entries/path");
            ValidateRole(entry.Role, "$/entries/role");
            RequireDigest(entry.Sha256, "$/entries/sha256");
            RequireLength(entry.Length, "$/entries/length");
        }
    }

    private static InputEntry[] ReadInputInventory(string inputRoot, IReadOnlyList<JvmJarExpectedEntry> expected)
    {
        RejectReparse(inputRoot, "$/inputRoot");
        foreach (var directory in Directory.EnumerateDirectories(inputRoot, "*", SearchOption.AllDirectories))
        {
            RejectReparse(directory, "$/inputRoot");
            if (!Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any()) Reject("EmptyDirectory", "$/inputRoot");
        }
        var actualBuilder = new List<InputEntry>();
        long aggregate = 0;
        foreach (var path in Directory.EnumerateFiles(inputRoot, "*", SearchOption.AllDirectories))
        {
            if (actualBuilder.Count + 1 > MaximumEntries) Reject("EntryCountExceeded", "$/inputRoot");
            RejectReparse(path, "$/inputRoot");
            var relative = Path.GetRelativePath(inputRoot, path).Replace('\\', '/');
            ValidateLogicalPath(relative, "$/inputRoot/path");
            var bytes = File.ReadAllBytes(path);
            if (bytes.LongLength > MaximumEntryBytes) Reject("EntrySizeLimitExceeded", $"$/inputRoot/{relative}");
            aggregate = checked(aggregate + bytes.LongLength);
            if (aggregate > MaximumAggregateBytes) Reject("AggregateEntryLimitExceeded", "$/inputRoot");
            actualBuilder.Add(new InputEntry(new JvmJarExpectedEntry(relative, "class", Digest(bytes), Decimal(bytes.LongLength)), bytes));
        }
        var actual = actualBuilder.OrderBy(entry => entry.Expected.Path, StringComparer.Ordinal).ToArray();
        var expectedByPath = expected.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        if (actual.Length != expected.Count || actual.Any(item => !expectedByPath.TryGetValue(item.Expected.Path, out var wanted) || wanted.Sha256 != item.Expected.Sha256 || wanted.Length != item.Expected.Length))
            Reject("InputInventoryMismatch", "$/entries");
        return actual.Select(item => new InputEntry(expectedByPath[item.Expected.Path], item.Bytes)).ToArray();
    }

    private static void WriteStaging(string staging, IReadOnlyList<InputEntry> entries)
    {
        foreach (var item in entries)
        {
            var destination = PhysicalPath(staging, item.Expected.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("entry has no parent"));
            File.WriteAllBytes(destination, item.Bytes);
        }
        Directory.CreateDirectory(Path.Combine(staging, "META-INF"));
        File.WriteAllBytes(Path.Combine(staging, "META-INF", "MANIFEST.MF"), ManifestBytes);
    }

    private static byte[] WriteArgfile(string staging, IReadOnlyList<JvmJarExpectedEntry> entries)
    {
        var text = string.Join("\n", new[] { ManifestPath }.Concat(entries.Where(entry => entry.Path != ManifestPath).Select(entry => entry.Path))) + "\n";
        var bytes = new UTF8Encoding(false).GetBytes(text);
        File.WriteAllBytes(Path.Combine(staging, "entries.argfile"), bytes);
        return bytes;
    }

    private static ImmutableArray<JvmJarInventoryEntry> ValidateArchive(byte[] bytes, IReadOnlyList<JvmJarExpectedEntry> expected)
    {
        if (bytes.Length < 22 || ReadUInt32(bytes, bytes.Length - 22) != EndOfCentralDirectorySignature) Reject("ZipEndRecordInvalid", "$/jar/eocd");
        var eocd = bytes.Length - 22;
        var disk = ReadUInt16(bytes, eocd + 4);
        var centralDisk = ReadUInt16(bytes, eocd + 6);
        var entriesOnDisk = ReadUInt16(bytes, eocd + 8);
        var entriesTotal = ReadUInt16(bytes, eocd + 10);
        var centralSize = ReadUInt32(bytes, eocd + 12);
        var centralOffset = ReadUInt32(bytes, eocd + 16);
        var commentLength = ReadUInt16(bytes, eocd + 20);
        if (disk != 0 || centralDisk != 0 || entriesOnDisk != entriesTotal || commentLength != 0 || entriesTotal != expected.Count || centralOffset + centralSize != (uint)eocd)
            Reject("ZipStructureMismatch", "$/jar/eocd");
        if (centralOffset > (uint)bytes.Length || centralSize > (uint)bytes.Length - centralOffset) Reject("ZipBounds", "$/jar/centralDirectory");

        var byPath = expected.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var central = new List<ArchiveEntry>(entriesTotal);
        var position = checked((int)centralOffset);
        for (var index = 0; index < entriesTotal; index++)
        {
            if (position + 46 > eocd || ReadUInt32(bytes, position) != CentralDirectorySignature) Reject("ZipCentralHeaderInvalid", $"$/jar/central/{index}");
            var madeBy = ReadUInt16(bytes, position + 4);
            var needed = ReadUInt16(bytes, position + 6);
            var flags = ReadUInt16(bytes, position + 8);
            var method = ReadUInt16(bytes, position + 10);
            var time = ReadUInt16(bytes, position + 12);
            var date = ReadUInt16(bytes, position + 14);
            var crc = ReadUInt32(bytes, position + 16);
            var compressed = ReadUInt32(bytes, position + 20);
            var uncompressed = ReadUInt32(bytes, position + 24);
            var nameLength = ReadUInt16(bytes, position + 28);
            var extraLength = ReadUInt16(bytes, position + 30);
            var comment = ReadUInt16(bytes, position + 32);
            var diskStart = ReadUInt16(bytes, position + 34);
            var internalAttributes = ReadUInt16(bytes, position + 36);
            var externalAttributes = ReadUInt32(bytes, position + 38);
            var localOffset = ReadUInt32(bytes, position + 42);
            var recordLength = checked(46 + nameLength + extraLength + comment);
            if (position + recordLength > eocd || madeBy != 10 || needed != 10 || flags != Utf8Flag || method != 0 || time != 1 || date != 33 || comment != 0 || diskStart != 0 || internalAttributes != 0 || externalAttributes != 0 || compressed != uncompressed || compressed > MaximumEntryBytes || uncompressed > MaximumEntryBytes)
                Reject("ZipMetadataMismatch", $"$/jar/central/{index}");
            var nameBytes = bytes.AsSpan(position + 46, nameLength).ToArray();
            var name = DecodeName(nameBytes, $"$/jar/central/{index}/name");
            ValidateLogicalPath(name, $"$/jar/central/{index}/name");
            if (!names.Add(name)) Reject("DuplicateEntry", $"$/jar/central/{index}/name");
            if (!folded.Add(name)) Reject("CaseFoldDuplicateEntry", $"$/jar/central/{index}/name");
            var extra = bytes.AsSpan(position + 46 + nameLength, extraLength);
            if (name == ManifestPath)
            {
                if (!extra.SequenceEqual(JarMarker)) Reject("ManifestExtraFieldMismatch", "$/jar/manifest/extra");
            }
            else if (extraLength != 0) Reject("UnexpectedExtraField", $"$/jar/central/{index}/extra");
            if (!byPath.TryGetValue(name, out var wanted) || wanted is null) Reject("ExtraEntry", $"$/jar/central/{index}/name");
            central.Add(new ArchiveEntry(name, nameBytes, crc, compressed, localOffset, position, wanted!));
            position += recordLength;
        }
        if (position != eocd) Reject("CentralDirectoryBounds", "$/jar/centralDirectory");
        var canonicalNames = expected.Select(entry => entry.Path).ToArray();
        if (!central.Select(entry => entry.Name).SequenceEqual(canonicalNames, StringComparer.Ordinal)) Reject("EntryOrderMismatch", "$/jar/centralDirectory/order");

        var ranges = new List<(uint Start, uint End)>();
        var result = ImmutableArray.CreateBuilder<JvmJarInventoryEntry>(central.Count);
        long aggregate = 0;
        foreach (var entry in central)
        {
            var local = checked((int)entry.LocalOffset);
            if (local + 30 > centralOffset || ReadUInt32(bytes, local) != LocalFileSignature) Reject("ZipLocalHeaderInvalid", $"$/jar/local/{entry.Name}");
            var localNeeded = ReadUInt16(bytes, local + 4);
            var localFlags = ReadUInt16(bytes, local + 6);
            var localMethod = ReadUInt16(bytes, local + 8);
            var localTime = ReadUInt16(bytes, local + 10);
            var localDate = ReadUInt16(bytes, local + 12);
            var localCrc = ReadUInt32(bytes, local + 14);
            var localCompressed = ReadUInt32(bytes, local + 18);
            var localUncompressed = ReadUInt32(bytes, local + 22);
            var localNameLength = ReadUInt16(bytes, local + 26);
            var localExtraLength = ReadUInt16(bytes, local + 28);
            var localNameEnd = checked(local + 30 + localNameLength + localExtraLength);
            if (localNeeded != 10 || localFlags != Utf8Flag || localMethod != 0 || localTime != 1 || localDate != 33 || localCrc != entry.Crc || localCompressed != entry.Compressed || localUncompressed != entry.Compressed || localNameEnd > centralOffset)
                Reject("ZipLocalMetadataMismatch", $"$/jar/local/{entry.Name}");
            var localExtra = bytes.AsSpan(local + 30 + localNameLength, localExtraLength);
            if (entry.Name == ManifestPath)
            {
                if (!localExtra.SequenceEqual(JarMarker)) Reject("ManifestExtraFieldMismatch", "$/jar/manifest/local-extra");
            }
            else if (localExtraLength != 0) Reject("UnexpectedExtraField", $"$/jar/local/{entry.Name}/extra");
            var localName = bytes.AsSpan(local + 30, localNameLength);
            if (!localName.SequenceEqual(entry.NameBytes)) Reject("LocalCentralNameMismatch", $"$/jar/local/{entry.Name}/name");
            var dataStart = localNameEnd;
            var dataEnd = checked((uint)dataStart + entry.Compressed);
            if (dataEnd > centralOffset) Reject("ZipDataBounds", $"$/jar/local/{entry.Name}/data");
            var data = bytes.AsSpan(dataStart, checked((int)entry.Compressed));
            if (Crc32(data) != entry.Crc) Reject("ZipCrcMismatch", $"$/jar/local/{entry.Name}/crc");
            var digest = Digest(data);
            if (digest != entry.Expected.Sha256 || Decimal(entry.Compressed) != entry.Expected.Length) Reject("JarInventoryMismatch", $"$/jar/entries/{entry.Name}");
            aggregate = checked(aggregate + entry.Compressed);
            if (aggregate > MaximumAggregateBytes) Reject("AggregateEntryLimitExceeded", "$/jar/entries");
            ranges.Add((entry.LocalOffset, dataEnd));
            result.Add(new JvmJarInventoryEntry(entry.Name, entry.Expected.Role, digest, Decimal(entry.Compressed)));
        }
        foreach (var pair in ranges.OrderBy(pair => pair.Start).Select((pair, index) => (pair, index)))
        {
            if (pair.index == 0 && pair.pair.Start != 0) Reject("ZipGap", "$/jar/local");
            if (pair.index > 0 && pair.pair.Start != ranges.OrderBy(value => value.Start).ElementAt(pair.index - 1).End) Reject("ZipOverlapOrGap", "$/jar/local");
        }
        if (ranges.Count > 0 && ranges.OrderBy(pair => pair.Start).Last().End != centralOffset) Reject("ZipTrailingData", "$/jar/centralDirectory");
        return result.MoveToImmutable();
    }

    private static void CleanupFailure(string runRoot, string staging, string quarantine, bool promoted)
    {
        if (promoted) return;
        try
        {
            DeleteContained(quarantine, runRoot);
            DeleteContained(staging, runRoot);
            if (File.Exists(quarantine) || Directory.Exists(quarantine) || Directory.Exists(staging))
                throw new IOException("normalizer cleanup left residual output");
        }
        catch (Exception exception)
        {
            throw new PortabilityContractException("TargetBuildRejected", "$/cleanup", new { reason = "CleanupFailed", failure = exception.GetType().Name, message = exception.Message, runRoot });
        }
    }

    private static void DeleteContained(string path, string root)
    {
        if (!PathStartsWithin(path, root)) throw new IOException("cleanup path escapes run root");
        if (File.Exists(path)) File.Delete(path);
        else if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    private static bool PathStartsWithin(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || string.Equals(fullPath, fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static string PhysicalPath(string root, string logical)
    {
        var path = Path.GetFullPath(Path.Combine(root, logical.Replace('/', Path.DirectorySeparatorChar)));
        if (!PathStartsWithin(path, root)) Reject("UnsafePath", "$/path");
        return path;
    }

    private static void ValidateLogicalPath(string path, string locus)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || path.StartsWith('/') || path.Contains(':', StringComparison.Ordinal)) Reject("UnsafePath", locus);
        var segments = path.Split('/');
        if (segments.Any(segment => string.IsNullOrEmpty(segment) || segment is "." or ".." || !SegmentPattern.IsMatch(segment))) Reject("UnsafePath", locus);
    }

    private static void ValidateRole(string role, string locus)
    {
        if (!InputRoles.Contains(role)) Reject("RoleInvalid", locus);
    }

    private static string DecodeName(byte[] bytes, string locus)
    {
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { Reject("InvalidUtf8Name", locus); return ""; }
    }

    private static void RequireDigest(string value, string name)
    {
        if (!DigestPattern.IsMatch(value)) Reject("DigestInvalid", name);
    }

    private static void RequireLength(string value, string name)
    {
        if (!DecimalPattern.IsMatch(value) || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length > MaximumEntryBytes) Reject("LengthInvalid", name);
    }

    private static string Decimal(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xffffffff;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xedb88320u & unchecked((uint)-(int)(crc & 1)));
        }
        return ~crc;
    }

    private sealed record InputEntry(JvmJarExpectedEntry Expected, byte[] Bytes);
    private sealed record ArchiveEntry(string Name, byte[] NameBytes, uint Crc, uint Compressed, uint LocalOffset, int CentralOffset, JvmJarExpectedEntry Expected);
    private static void Reject(string reason, string locus, object? details = null) => throw new PortabilityContractException("TargetBuildRejected", locus, new { reason, details });
    private static void RejectReparse(string path, string locus)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) Reject("ReparsePath", locus);
    }
}
