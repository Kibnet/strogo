using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using Kernel.Core;
using Strogo.Experiments;

try
{
    return Run(args);
}
catch (G03RefusalException error)
{
    Console.Error.WriteLine(Encoding.UTF8.GetString(error.OutputBytes));
    return 1;
}
catch (KernelException error)
{
    Console.Error.WriteLine(error.Error.Code);
    return 1;
}
catch (Exception error)
{
    Console.Error.WriteLine($"InfrastructureFailure: {error.GetType().Name}: {error.Message}");
    return 1;
}

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] == "calibrate")
    {
        string? output = Option(args, "report");
        var report = CalibrationEvaluator.Run();
        if (output is null) Console.WriteLine(Encoding.UTF8.GetString(CalibrationEvaluator.ReportBytes(report)));
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllBytes(output, CalibrationEvaluator.ReportBytes(report));
            Console.WriteLine($"{report.Status}: {report.PositiveCases} positive, {report.NegativeCases} negative");
        }
        return report.Status == "Accepted" ? 0 : 1;
    }

    if (args[0] == "export")
    {
        string? caseId = Option(args, "case");
        string arm = Option(args, "arm") ?? "graph-json";
        string directory = Option(args, "directory") ?? "e09-export";
        var c = CalibrationCorpus.Create().FirstOrDefault(x => x.Id == caseId) ?? throw new ArgumentException("Unknown case");
        var bundle = Exporter.Export(c, arm);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "job-starter.bin"), bundle.CandidateBytes);
        File.WriteAllBytes(Path.Combine(directory, "manifest.json"), CanonicalJson.Encode(bundle.Manifest));
        Console.WriteLine($"exported {c.Id} ({arm})");
        return 0;
    }

    if (args[0] == "pilot" && args.Length >= 2)
    {
        return args[1] switch
        {
            "prepare" => Prepare(args),
            "identity-check" => IdentityCheck(args),
            "receipt" => Receipt(args),
            "evaluate" => Evaluate(args),
            "abort-remaining" => AbortRemaining(args),
            "prompt-check" => PromptCheck(args),
            "report" => Report(args),
            _ => Usage()
        };
    }
    if (args[0] == "g03")
    {
        if (args.Length < 2) throw G03InvocationFailure();
        return args[1] switch
        {
            "validate" => G03Validate(args),
            "score" => G03Score(args),
            _ => throw G03InvocationFailure()
        };
    }
    return Usage();
}

static G03ValidationResult G03Inputs(string[] args) => G03Catalog.Validate(
    G03Required(args, "source-row-manifest"), G03Required(args, "catalog"),
    G03Required(args, "domain-registry"), G03Required(args, "evaluation-plan"));

static G03RefusalException G03InvocationFailure() => new("G03SchemaInvalid", "Invocation", CanonicalJson.RawDigest([]));
static string G03Required(string[] args, string name) => Option(args, name) is { Length: > 0 } value ? value : throw G03InvocationFailure();

static int G03Validate(string[] args)
{
    var result = G03Inputs(args);
    Console.WriteLine(Encoding.UTF8.GetString(result.OutputBytes));
    return 0;
}

static int G03Score(string[] args)
{
    string output = G03Required(args, "report");
    string observations = G03Required(args, "observations");
    var inputs = G03Inputs(args);
    var result = G03Catalog.Score(inputs, observations, G03Required(args, "catalog"));
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    File.WriteAllBytes(output, result.ReportBytes);
    Console.WriteLine(Encoding.UTF8.GetString(result.OutputBytes));
    return result.ExitCode;
}

static int Prepare(string[] args)
{
    string directory = Path.GetFullPath(Required(args, "directory"));
    string repositoryRoot = Required(args, "repository-root");
    if (!LivePilotProvenance.EvidenceRootIsOutsideRepository(repositoryRoot, directory)) throw LivePilotJson.Failure("EvidenceRootInsideRepository");
    string fixtures = Option(args, "fixtures") ?? Path.Combine(Environment.CurrentDirectory, "fixtures", "e10-live-pilot");
    var implementation = LivePilotProvenance.CreateImplementation(
        repositoryRoot,
        Required(args, "dotnet"),
        Required(args, "dotnet-version"),
        Required(args, "codex-node"),
        Required(args, "codex-entry"),
        Assembly.GetExecutingAssembly().Location);
    var manifest = LivePilotPreparation.Prepare(new(
        directory,
        fixtures,
        Required(args, "pilot-id"),
        Option(args, "created-at") ?? DateTimeOffset.UtcNow.ToString("O"),
        Required(args, "commit"),
        Required(args, "cli-version"),
        Required(args, "docker-image-id"),
        implementation));
    Console.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Encode(new { status = "Prepared", manifest.PilotId, manifest.ManifestDigest, runs = manifest.RunOrder })));
    return 0;
}

static int IdentityCheck(string[] args)
{
    string directory = Path.GetFullPath(Required(args, "directory"));
    var pilot = LivePilotPreparation.LoadAndVerify(directory);
    VerifyImplementation(args, pilot);
    Console.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Encode(new { status = "ImplementationIdentityAccepted", pilot.PilotId })));
    return 0;
}

static int Receipt(string[] args)
{
    string root = Path.GetFullPath(Required(args, "directory"));
    string runId = Required(args, "run");
    var pilot = LivePilotPreparation.LoadAndVerify(root);
    var run = LivePilotPreparation.LoadAndVerifyRun(Path.Combine(root, "packages", runId, "run-manifest.json"), pilot);
    string raw = Path.Combine(root, "runs", runId);
    Directory.CreateDirectory(raw);
    var acl = LivePilotAcl.Inspect(root, pilot);
    LivePilotJson.WriteCanonical(Path.Combine(raw, "acl-receipt.json"), acl);
    var retention = new RetentionReceipt(pilot.Protocol, pilot.PilotId, pilot.RetentionUntilUtc, "ExplicitOnly");
    LivePilotJson.WriteCanonical(Path.Combine(raw, "retention-receipt.json"), retention);
    ImmutableArray<string> variables = run.InvocationIdentity.EnvironmentVariables;
    var values = variables.Select(name =>
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (name == "CODEX_HOME" && string.IsNullOrWhiteSpace(value)) value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        return new EnvironmentValueDigest(name, !string.IsNullOrWhiteSpace(value), string.IsNullOrWhiteSpace(value) ? string.Empty : CanonicalJson.RawDigest(Encoding.UTF8.GetBytes(value)));
    }).ToImmutableArray();
    var envSkeleton = new EnvironmentReceipt(pilot.Protocol, pilot.PilotId, variables, values, string.Empty);
    var environment = envSkeleton with { Digest = CanonicalJson.RawDigest(CanonicalJson.Encode(envSkeleton)) };
    string environmentPath = Path.Combine(raw, "environment-receipt.json");
    LivePilotJson.WriteCanonical(environmentPath, environment);
    var receipt = new ProcessReceipt(
        pilot.Protocol,
        pilot.PilotId,
        run.RunId,
        run.ProcessAttemptId,
        run.InvocationIdentity,
        run.RequestedRuntime,
        run.PromptDigest,
        run.ResponseSchemaDigest,
        Required(args, "started-at"),
        Required(args, "ended-at"),
        RequiredLong(args, "wall-ms"),
        RequiredInt(args, "exit-code"),
        RequiredBool(args, "timed-out"),
        RequiredBool(args, "output-limit-exceeded"),
        Digest(Path.Combine(raw, "stdout.jsonl")),
        Digest(Path.Combine(raw, "stderr.txt")),
        Digest(Path.Combine(raw, "response.json")),
        Digest(Path.Combine(raw, "prompt-input.txt")),
        Digest(environmentPath));
    LivePilotJson.WriteCanonical(Path.Combine(raw, "process-receipt.json"), receipt);
    Console.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Encode(new { status = "Recorded", runId, receipt.ProcessAttemptId })));
    return 0;
}

static int Evaluate(string[] args)
{
    string directory = Path.GetFullPath(Required(args, "directory"));
    string fixtures = Option(args, "fixtures") ?? Path.Combine(Environment.CurrentDirectory, "fixtures", "e10-live-pilot");
    VerifyImplementation(args, LivePilotPreparation.LoadAndVerify(directory));
    var result = LivePilotEvaluation.Evaluate(directory, Required(args, "run"), fixtures);
    Console.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Encode(result)));
    return result.Status is "Invalid" or "InfrastructureFailure" ? 1 : 0;
}

static int AbortRemaining(string[] args)
{
    string directory = Path.GetFullPath(Required(args, "directory"));
    var pilot = LivePilotPreparation.LoadAndVerify(directory);
    VerifyImplementation(args, pilot);
    var skipped = LivePilotEvaluation.RecordRemainingNotRun(directory, Required(args, "after-run"));
    Console.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Encode(new { status = "RemainingRunsNotRun", runs = skipped.Select(x => x.RunId).ToArray() })));
    return 0;
}

static void VerifyImplementation(string[] args, PilotManifest pilot)
{
    var actual = LivePilotProvenance.CreateImplementation(
        Required(args, "repository-root"),
        Required(args, "dotnet"),
        Required(args, "dotnet-version"),
        Required(args, "codex-node"),
        Required(args, "codex-entry"),
        Assembly.GetExecutingAssembly().Location);
    LivePilotProvenance.VerifyImplementation(actual, pilot.ImplementationIdentity);
}

static int Report(string[] args)
{
    string directory = Path.GetFullPath(Required(args, "directory"));
    var report = LivePilotReporting.Build(directory);
    LivePilotReporting.Write(directory, Required(args, "output"));
    string? quarantineDirectory = report.Status == "EvidenceQuarantined" ? LivePilotReporting.Quarantine(directory) : null;
    Console.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Encode(new { report.Status, report.PilotId, report.ReportDigest, quarantineDirectory })));
    return report.Status == "Completed" ? 0 : 1;
}

static int PromptCheck(string[] args)
{
    string digest = LivePilotReporting.ValidatePromptInputs(Required(args, "directory"));
    Console.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Encode(new { status = "PromptInputAccepted", sharedDigest = digest })));
    return 0;
}

static string? Option(string[] args, string name)
{
    string prefix = $"--{name}=";
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith(prefix, StringComparison.Ordinal)) return args[i][prefix.Length..];
        if (args[i] == $"--{name}" && i + 1 < args.Length) return args[i + 1];
    }
    return null;
}

static string Required(string[] args, string name) => Option(args, name) is { Length: > 0 } value ? value : throw new ArgumentException($"Missing --{name}");
static int RequiredInt(string[] args, string name) => int.TryParse(Required(args, name), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : throw new ArgumentException($"Invalid --{name}");
static long RequiredLong(string[] args, string name) => long.TryParse(Required(args, name), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value) ? value : throw new ArgumentException($"Invalid --{name}");
static bool RequiredBool(string[] args, string name) => bool.TryParse(Required(args, name), out bool value) ? value : throw new ArgumentException($"Invalid --{name}");
static string Digest(string path) => File.Exists(path) ? CanonicalJson.RawDigest(File.ReadAllBytes(path)) : throw LivePilotJson.Failure("RawEvidenceMissing");

static int Usage()
{
    Console.Error.WriteLine("Usage: calibrate [--report PATH] | export --case R01-baseline [--arm graph-json|strogo-notation] [--directory PATH] | pilot prepare|identity-check|receipt|evaluate|abort-remaining|prompt-check|report ... | g03 validate|score --source-row-manifest PATH --catalog PATH --domain-registry PATH --evaluation-plan PATH [--observations PATH --report PATH]");
    return 2;
}
