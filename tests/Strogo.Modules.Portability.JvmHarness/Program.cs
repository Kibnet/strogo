using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Kernel.Core;
using Strogo.Modules;
using Strogo.Modules.Portability;

try
{
    if (args.Length == 0 || args[0] != "candidate") throw new ArgumentException("command must be candidate");
    var options = ParseOptions(args.Skip(1).ToArray());
    BuildCandidate(options);
}
catch (PortabilityContractException exception)
{
    Console.Error.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Encode(new
    {
        status = "Rejected",
        code = exception.Code,
        locus = exception.Locus,
        details = exception.Details
    })));
    Environment.ExitCode = 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Encode(new
    {
        status = "Rejected",
        code = "JvmHarnessRejected",
        locus = "$",
        details = new { reason = exception.Message, exception = exception.GetType().Name }
    })));
    Environment.ExitCode = 1;
}

static void BuildCandidate(IReadOnlyDictionary<string, string> options)
{
    var repo = FullPath(Required(options, "--repo-root"));
    var runRoot = FullPath(Required(options, "--run-root"));
    var dafny = FullPath(Required(options, "--dafny"));
    var javac = FullPath(Required(options, "--javac"));
    var git = FullPath(Required(options, "--git"));
    var dotnet = FullPath(Required(options, "--dotnet"));
    if (Directory.Exists(runRoot) || File.Exists(runRoot)) throw new InvalidOperationException("run root already exists");
    RequireFile(dafny);
    RequireFile(javac);
    RequireFile(git);
    RequireFile(dotnet);
    RequireDirectChild(javac, Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(javac))!, "bin"), "javac");
    Directory.CreateDirectory(runRoot);

    var moduleBytes = File.ReadAllBytes(Path.Combine(repo, "fixtures", "portability-v0.1", "module.json"));
    var ownerBytes = File.ReadAllBytes(Path.Combine(repo, "fixtures", "portability-v0.1", "owner.json"));
    var module = ModulesCompiler.Compile(ModulesParser.ParseModule(moduleBytes));
    var owner = OwnerBundleV04Parser.Parse(ownerBytes);
    var binding = PortabilityContract.Bind(module, owner);
    var lowering = ModulesDafnyLowerer.LowerPortableValidation(module, owner);

    var validatorPath = Path.Combine(repo, "src", "Strogo.Modules.Portability", "JvmUpstreamWarnings.cs");
    var portabilityContractPath = Path.Combine(repo, "src", "Strogo.Modules.Portability", "PortabilityContract.cs");
    var canonicalJsonPath = Path.Combine(repo, "src", "Kernel.Core", "Codec.cs");
    var runnerPath = Path.Combine(repo, "src", "Strogo.Modules.Portability", "JvmProcessRunner.cs");
    var harnessPath = Path.Combine(repo, "tests", "Strogo.Modules.Portability.JvmHarness", "Program.cs");
    var harnessProject = Path.Combine(repo, "tests", "Strogo.Modules.Portability.JvmHarness", "Strogo.Modules.Portability.JvmHarness.csproj");
    var harnessLock = Path.Combine(repo, "tests", "Strogo.Modules.Portability.JvmHarness", "packages.lock.json");
    var wrapperPath = Path.Combine(repo, "tools", "Build-PortableJvm-Baseline.ps1");
    foreach (var file in new[] { validatorPath, portabilityContractPath, canonicalJsonPath, runnerPath, harnessPath, harnessProject, harnessLock, wrapperPath }) RequireFile(file);
    var validatorInventory = SourceInventory(repo, [validatorPath, portabilityContractPath, canonicalJsonPath]);
    var validatorDigest = PortabilityContract.DomainHash($"{JvmUpstreamWarnings.SchemaVersion}/validator", CanonicalJson.Encode(validatorInventory));
    var harnessInventory = SourceInventory(repo, [runnerPath, harnessPath, harnessProject, harnessLock, wrapperPath]);

    var cleanEnvironment = new[] { "CLASSPATH", "JDK_JAVAC_OPTIONS", "JDK_JAVA_OPTIONS", "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS" };
    var cleanDotNetEnvironment = new[] { "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86", "DOTNET_ROOT_ARM64", "DOTNET_STARTUP_HOOKS", "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE", "DOTNET_ROLL_FORWARD", "DOTNET_ROLL_FORWARD_TO_PRERELEASE", "DOTNET_HOST_PATH", "MSBuildSDKsPath", "MSBUILD_EXE_PATH" };
    if (cleanDotNetEnvironment.Any(name => Environment.GetEnvironmentVariable(name) is not null)) throw new InvalidOperationException(".NET host environment is not clean");
    if (Environment.GetEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP") != "0") throw new InvalidOperationException(".NET multilevel lookup is not disabled");
    var limits = new { timeoutSeconds = "180", outputBytesPerStream = "1048576", cleanupSeconds = "10" };
    var dafnyVersion = Text(Run(dafny, repo, ["--version"], cleanEnvironment, "Dafny").Stdout).Trim();
    if (!dafnyVersion.StartsWith("4.11.0+", StringComparison.Ordinal)) throw new InvalidOperationException("Dafny version mismatch");
    var javacVersionRun = Run(javac, repo, ["-version"], cleanEnvironment, "Javac");
    var javacVersion = (Text(javacVersionRun.Stdout) + Text(javacVersionRun.Stderr)).Trim();
    if (javacVersion != "javac 17.0.19") throw new InvalidOperationException($"javac version mismatch: {javacVersion}");
    var dotnetVersion = Text(Run(dotnet, repo, ["--version"], cleanEnvironment, "DotNet").Stdout).Trim();
    if (dotnetVersion != "10.0.400") throw new InvalidOperationException($".NET SDK version mismatch: {dotnetVersion}");

    var dafnyRoot = Path.GetDirectoryName(dafny) ?? throw new InvalidOperationException("Dafny root unavailable");
    var jdkRoot = Path.GetDirectoryName(Path.GetDirectoryName(javac)) ?? throw new InvalidOperationException("JDK root unavailable");
    var dotnetRoot = Path.GetDirectoryName(dotnet) ?? throw new InvalidOperationException(".NET root unavailable");
    var executingHost = FullPath(Environment.ProcessPath ?? throw new InvalidOperationException("executing .NET host unavailable"));
    if (!string.Equals(executingHost, dotnet, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"executing .NET host mismatch: {executingHost}");
    var runtimeVersion = Environment.Version.ToString();
    var sdkRoot = Path.Combine(dotnetRoot, "sdk", dotnetVersion);
    var runtimeRoot = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App", runtimeVersion);
    var hostFxrRoot = Path.Combine(dotnetRoot, "host", "fxr", runtimeVersion);
    foreach (var directory in new[] { sdkRoot, runtimeRoot, hostFxrRoot })
        if (!Directory.Exists(directory)) throw new InvalidOperationException($".NET closure directory unavailable: {directory}");
    var loadedRuntimeRoot = Path.TrimEndingDirectorySeparator(RuntimeEnvironment.GetRuntimeDirectory());
    if (!string.Equals(loadedRuntimeRoot, Path.TrimEndingDirectorySeparator(runtimeRoot), StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"loaded .NET runtime is outside the pinned closure: {loadedRuntimeRoot}");
    var dotnetClosureDigest = MultiRootClosureDigest(
        [("host", dotnetRoot, new[] { dotnet }), ("sdk", sdkRoot, Directory.GetFiles(sdkRoot, "*", SearchOption.AllDirectories)), ("runtime", runtimeRoot, Directory.GetFiles(runtimeRoot, "*", SearchOption.AllDirectories)), ("hostfxr", hostFxrRoot, Directory.GetFiles(hostFxrRoot, "*", SearchOption.AllDirectories))],
        $"sdk={dotnetVersion};runtime={runtimeVersion}",
        $"{JvmUpstreamWarnings.SchemaVersion}/dotnet-closure");
    var harnessBinaryInventory = BinaryInventory(AppContext.BaseDirectory);
    var harnessBinaryDigest = PortabilityContract.DomainHash($"{JvmUpstreamWarnings.SchemaVersion}/harness-binaries", CanonicalJson.Encode(harnessBinaryInventory));
    var harnessDigest = PortabilityContract.DomainHash($"{JvmUpstreamWarnings.SchemaVersion}/harness", CanonicalJson.Encode(new
    {
        sources = harnessInventory,
        dotnetClosureDigest,
        harnessBinaryDigest
    }));
    var translatorDigest = ClosureDigest(dafnyRoot, dafnyVersion, $"{JvmUpstreamWarnings.SchemaVersion}/translator");
    var javacClosureDigest = ClosureDigest(jdkRoot, javacVersion, $"{JvmUpstreamWarnings.SchemaVersion}/javac-closure");
    var logicalProbeArguments = new[]
    {
        "-J-Duser.language=en", "-J-Duser.country=US", "-J-Dfile.encoding=UTF-8",
        "--release", "17", "-encoding", "UTF-8", "-proc:none", "-implicit:none",
        "--class-path", "empty-classpath", "--source-path", "empty-sourcepath",
        "-Xlint:all", "-Xmaxwarns", "10000", "-d", ".quarantine/probe-classes", "@translator-sources.argfile"
    };
    var probeCommandDigest = PortabilityContract.DomainHash($"{JvmUpstreamWarnings.SchemaVersion}/command", CanonicalJson.Encode(new
    {
        executable = "jdk/bin/javac",
        arguments = logicalProbeArguments,
        absentEnvironment = cleanEnvironment.Order(StringComparer.Ordinal).ToArray(),
        limits
    }));
    var identity = new JvmWarningBaselineIdentity(
        binding.Module.SourceDigest,
        lowering.SourceDigest,
        translatorDigest,
        javacClosureDigest,
        probeCommandDigest,
        validatorDigest,
        harnessDigest);

    var laneA = BuildLane("a", runRoot, repo, dafny, javac, lowering.SourceBytes, cleanEnvironment, logicalProbeArguments, identity);
    var laneB = BuildLane("b", runRoot, repo, dafny, javac, lowering.SourceBytes, cleanEnvironment, logicalProbeArguments, identity);
    if (!laneA.Candidate.Bytes.SequenceEqual(laneB.Candidate.Bytes)) throw new InvalidOperationException("two baseline candidates differ");
    if (!laneA.SourceTreeDigest.Equals(laneB.SourceTreeDigest, StringComparison.Ordinal)) throw new InvalidOperationException("two translated source trees differ");

    var candidatePath = Path.Combine(runRoot, "baseline-candidate.json");
    File.WriteAllBytes(candidatePath, laneA.Candidate.Bytes);
    var revision = Text(Run(git, repo, ["-C", repo, "rev-parse", "HEAD"], [], "Git").Stdout).Trim();
    var dirty = Text(Run(git, repo, ["-C", repo, "status", "--porcelain"], [], "Git").Stdout).Length != 0;
    File.WriteAllBytes(Path.Combine(runRoot, "report.json"), CanonicalJson.Encode(new
    {
        schemaVersion = "strogo.jvm-warning-candidate-run.v0.1",
        status = "CandidateOnly",
        phase2Allowed = false,
        profileId = PortabilityVersions.JvmProfile,
        repositoryRevision = revision,
        repositoryDirty = dirty,
        baselineDigest = laneA.Candidate.BaselineDigest,
        normalizedDiagnosticsDigest = laneA.Candidate.NormalizedDiagnosticsDigest,
        sourceInventoryDigest = laneA.Candidate.SourceInventoryDigest,
        translatedSourceTreeDigest = laneA.SourceTreeDigest,
        warningCount = laneA.Candidate.Warnings.Length.ToString(CultureInfo.InvariantCulture),
        categories = laneA.Candidate.Warnings.GroupBy(item => item.Category, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => new { category = group.Key, count = group.Count().ToString(CultureInfo.InvariantCulture) }).ToArray(),
        toolchain = new { dafnyVersion, translatorDigest, javacVersion, javacClosureDigest, dotnetVersion, runtimeVersion, dotnetClosureDigest, harnessBinaryDigest, dotnetEnvironmentAbsent = cleanDotNetEnvironment.Order(StringComparer.Ordinal).ToArray(), dotnetMultilevelLookup = "0", probeCommandDigest, validatorDigest, harnessDigest },
        lanes = new[] { laneA.Receipt, laneB.Receipt },
        ownerApproval = "Pending"
    }));
    Console.WriteLine($"PASS JVM baseline candidate warnings={laneA.Candidate.Warnings.Length} baseline={laneA.Candidate.BaselineDigest} phase2=Blocked");
}

static LaneResult BuildLane(
    string lane,
    string runRoot,
    string repo,
    string dafny,
    string javac,
    byte[] dafnySource,
    string[] cleanEnvironment,
    string[] probeArguments,
    JvmWarningBaselineIdentity identity)
{
    var root = Path.Combine(runRoot, lane);
    Directory.CreateDirectory(root);
    File.WriteAllBytes(Path.Combine(root, "candidate.dfy"), dafnySource);
    var translation = Run(dafny, root,
    [
        "translate", "java", "candidate.dfy", "--no-verify", "--enforce-determinism", "--include-runtime",
        "--output", "Candidate", "--translation-record-output", "translation-record.dtr"
    ], cleanEnvironment, "Dafny");
    if (translation.ExitCode != 0) throw new InvalidOperationException($"Dafny translation failed in lane {lane}");
    var sourceRoot = Path.Combine(root, "Candidate-java");
    var sourceFiles = Directory.GetFiles(sourceRoot, "*.java", SearchOption.AllDirectories)
        .Select(path => (Path: Path.GetRelativePath(sourceRoot, path).Replace(Path.DirectorySeparatorChar, '/'), Bytes: File.ReadAllBytes(path)))
        .OrderBy(item => item.Path, StringComparer.Ordinal)
        .ToArray();
    if (sourceFiles.Length == 0) throw new InvalidOperationException("Dafny emitted no Java sources");
    var sourceTreeDigest = PortabilityContract.DomainHash($"{JvmUpstreamWarnings.SchemaVersion}/translated-source-tree", CanonicalJson.Encode(sourceFiles.Select(item => new
    {
        path = item.Path,
        sha256 = CanonicalJson.RawDigest(item.Bytes),
        bytes = item.Bytes.LongLength.ToString(CultureInfo.InvariantCulture)
    }).ToArray()));
    File.WriteAllText(Path.Combine(sourceRoot, "translator-sources.argfile"), string.Join('\n', sourceFiles.Select(item => item.Path)) + "\n", new UTF8Encoding(false));
    Directory.CreateDirectory(Path.Combine(sourceRoot, "empty-classpath"));
    Directory.CreateDirectory(Path.Combine(sourceRoot, "empty-sourcepath"));
    var quarantineRoot = Path.Combine(sourceRoot, ".quarantine");
    var quarantine = Path.Combine(quarantineRoot, "probe-classes");
    Directory.CreateDirectory(quarantine);
    var outcome = JvmQuarantine.Execute(root, quarantine, () =>
    {
        var probe = Run(javac, sourceRoot, probeArguments, cleanEnvironment, "Javac");
        File.WriteAllBytes(Path.Combine(root, "javac.stdout.bin"), probe.Stdout);
        File.WriteAllBytes(Path.Combine(root, "javac.stderr.bin"), probe.Stderr);
        if (probe.ExitCode != 0 || probe.StdoutBytes != 0) RejectProbe(lane, probe);
        var candidate = JvmUpstreamWarnings.CreateCandidate(identity, sourceFiles, probe.Stderr);
        File.WriteAllBytes(Path.Combine(root, "canonical-diagnostics.txt"), candidate.CanonicalDiagnostics);
        return (Probe: probe, Candidate: candidate);
    });
    var probe = outcome.Probe;
    var candidate = outcome.Candidate;
    var receipt = new
    {
        lane,
        translationExitCode = translation.ExitCode.ToString(CultureInfo.InvariantCulture),
        translationStdoutDigest = translation.StdoutDigest,
        translationStderrDigest = translation.StderrDigest,
        javacExitCode = probe.ExitCode.ToString(CultureInfo.InvariantCulture),
        javacStdoutBytes = probe.StdoutBytes.ToString(CultureInfo.InvariantCulture),
        javacStderrBytes = probe.StderrBytes.ToString(CultureInfo.InvariantCulture),
        javacStdoutDigest = probe.StdoutDigest,
        javacStderrDigest = probe.StderrDigest,
        javacElapsedMilliseconds = probe.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
        normalizedDiagnosticsDigest = candidate.NormalizedDiagnosticsDigest,
        baselineDigest = candidate.BaselineDigest,
        translatedSourceTreeDigest = sourceTreeDigest,
        probeCleanup = "Passed"
    };
    File.WriteAllBytes(Path.Combine(root, "receipt.json"), CanonicalJson.Encode(receipt));
    return new(candidate, sourceTreeDigest, receipt);
}

static void RejectProbe(string lane, JvmProcessResult probe)
{
    static object Evidence(byte[] prefix, string digest, long bytes)
    {
        var retained = prefix.AsSpan(0, Math.Min(prefix.Length, 4096)).ToArray();
        return new
        {
            bytesRead = bytes.ToString(CultureInfo.InvariantCulture),
            digest,
            prefixBase64 = Convert.ToBase64String(retained),
            prefixBytes = retained.Length.ToString(CultureInfo.InvariantCulture),
            prefixTruncated = retained.LongLength < bytes
        };
    }
    throw new PortabilityContractException("TargetBuildRejected", "$/process", new
    {
        reason = "JavacProbeFailed",
        lane,
        exitCode = probe.ExitCode.ToString(CultureInfo.InvariantCulture),
        processId = probe.ProcessId.ToString(CultureInfo.InvariantCulture),
        stdout = Evidence(probe.Stdout, probe.StdoutDigest, probe.StdoutBytes),
        stderr = Evidence(probe.Stderr, probe.StderrDigest, probe.StderrBytes)
    });
}

static JvmProcessResult Run(string executable, string workingDirectory, IReadOnlyList<string> arguments, IReadOnlyList<string> environment, string prefix)
    => JvmProcessRunner.Run(new(
        Path.GetFullPath(executable),
        Path.GetFullPath(workingDirectory),
        arguments,
        environment,
        TimeSpan.FromSeconds(180),
        1024 * 1024,
        TimeSpan.FromSeconds(10),
        prefix switch
        {
            "Dafny" => JvmProcessKind.DafnyProbe,
            "Javac" => JvmProcessKind.JavacProbe,
            "Git" => JvmProcessKind.GitProbe,
            "DotNet" => JvmProcessKind.DotNetProbe,
            _ => throw new ArgumentOutOfRangeException(nameof(prefix))
        }));

static string ClosureDigest(string root, string version, string tag)
{
    var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(path =>
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException($"closure contains reparse point: {path}");
        var bytes = File.ReadAllBytes(path);
        return new
        {
            path = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
            sha256 = CanonicalJson.RawDigest(bytes),
            bytes = bytes.LongLength.ToString(CultureInfo.InvariantCulture)
        };
    }).OrderBy(item => item.path, StringComparer.Ordinal).ToArray();
    return PortabilityContract.DomainHash(tag, CanonicalJson.Encode(new { version, files }));
}

static string MultiRootClosureDigest(IEnumerable<(string LogicalRoot, string PhysicalRoot, IEnumerable<string> Files)> roots, string version, string tag)
{
    var files = roots.SelectMany(root => root.Files.Select(path =>
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException($"closure contains reparse point: {path}");
        var bytes = File.ReadAllBytes(path);
        return new
        {
            path = $"{root.LogicalRoot}/{Path.GetRelativePath(root.PhysicalRoot, path).Replace(Path.DirectorySeparatorChar, '/')}",
            sha256 = CanonicalJson.RawDigest(bytes),
            bytes = bytes.LongLength.ToString(CultureInfo.InvariantCulture)
        };
    })).OrderBy(item => item.path, StringComparer.Ordinal).ToArray();
    return PortabilityContract.DomainHash(tag, CanonicalJson.Encode(new { version, files }));
}

static SourceFileIdentity[] BinaryInventory(string root)
    => Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly)
        .Where(path => Path.GetExtension(path) is ".dll" or ".exe" or ".json")
        .Select(path =>
        {
            var bytes = File.ReadAllBytes(path);
            return new SourceFileIdentity(Path.GetFileName(path), CanonicalJson.RawDigest(bytes), bytes.LongLength.ToString(CultureInfo.InvariantCulture));
        }).OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();

static SourceFileIdentity[] SourceInventory(string repo, IEnumerable<string> paths)
    => paths.Select(path =>
    {
        var bytes = File.ReadAllBytes(path);
        return new SourceFileIdentity(
            Path.GetRelativePath(repo, path).Replace(Path.DirectorySeparatorChar, '/'),
            CanonicalJson.RawDigest(bytes),
            bytes.LongLength.ToString(CultureInfo.InvariantCulture));
    }).OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();

static Dictionary<string, string> ParseOptions(string[] values)
{
    if (values.Length % 2 != 0) throw new ArgumentException("options must be name/value pairs");
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < values.Length; index += 2)
        if (!values[index].StartsWith("--", StringComparison.Ordinal) || !result.TryAdd(values[index], values[index + 1])) throw new ArgumentException($"invalid option: {values[index]}");
    return result;
}

static string Required(IReadOnlyDictionary<string, string> options, string name)
    => options.TryGetValue(name, out var value) ? value : throw new ArgumentException($"missing option: {name}");
static string FullPath(string value) => Path.GetFullPath(value);
static string Text(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes);
static void RequireFile(string path) { if (!File.Exists(path)) throw new FileNotFoundException("required file unavailable", path); }
static void RequireDirectChild(string file, string parent, string label)
{
    if (!string.Equals(Path.GetDirectoryName(file), Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)), StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"{label} must be a direct child of the expected directory");
}

sealed record LaneResult(JvmWarningBaselineCandidate Candidate, string SourceTreeDigest, object Receipt);
sealed record SourceFileIdentity(string Path, string Sha256, string Bytes);
