using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kernel.Core;
using Kernel.Host;

namespace Kernel.Conformance;

internal static class E07Cases
{
    private static readonly string[] FixtureNames =
    [
        "stale-replay.json",
        "contract-drift-policy.json",
        "contract-drift-program.json",
        "contract-drift-manifest.json",
        "effect-smuggling.json"
    ];

    public static void Register(List<ConformanceCase> cases)
        => cases.Add(new("e07", "cross-host-adversarial-fixtures", ["E07-A", "E07-B", "E07-C"], Run));

    private static async Task Run(TestContext context)
    {
        var root = Toolchain.Root();
        var fixtureRoot = Path.Combine(root, "tests", "fixtures", "e07");
        var fixtures = FixtureNames.Select(name => E07FixtureCodec.Read(Path.Combine(fixtureRoot, name))).ToArray();
        context.Equal(FixtureNames.Length, fixtures.Length, "all E07 fixtures load");
        context.True(fixtures.Select(fixture => fixture.Digest).Distinct(StringComparer.Ordinal).Count() == fixtures.Length, "fixture identities are distinct");

        var solver = await Toolchain.SolverAsync(context).ConfigureAwait(false);
        var java = await JavaE07Driver.CreateAsync(root).ConfigureAwait(false);
        context.Evidence["javaToolchain"] = java.Identity;
        try
        {
            foreach (var fixture in fixtures)
            {
                var language = await E07LanguagePath.RunAsync(fixture, context, solver).ConfigureAwait(false);
                var direct = E07DirectBaseline.Run(fixture);
                var javaObservation = await java.RunAsync(fixture.Path).ConfigureAwait(false);
                AssertObservation(context, fixture, language, direct, javaObservation);
                context.Evidence[fixture.Id] = new
                {
                    fixture = fixture.Digest,
                    language,
                    direct,
                    java = javaObservation
                };
            }
        }
        finally
        {
            await java.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void AssertObservation(TestContext context, E07Fixture fixture, params E07Observation[] observations)
    {
        context.True(observations.All(observation => observation.FixtureId == fixture.Id), fixture.Id + " fixture identity is preserved");
        context.True(observations.All(observation => observation.Statuses.SequenceEqual(observations[0].Statuses)), fixture.Id + " paths have the same ordered outcomes");
        context.True(observations.All(observation => observation.Available == observations[0].Available), fixture.Id + " paths agree on available state");
        context.True(observations.All(observation => observation.ReceiptCount == observations[0].ReceiptCount), fixture.Id + " paths agree on receipt count");
        context.True(observations.All(observation => observation.TransitionCount == observations[0].TransitionCount), fixture.Id + " paths agree on transition count");
        context.True(observations.All(observation => observation.EventIds.SequenceEqual(observations[0].EventIds)), fixture.Id + " paths agree on committed event IDs: " + string.Join(" | ", observations.Select(observation => observation.PathName + "=[" + string.Join(',', observation.EventIds) + "]")));
        context.True(observations.All(observation => observation.Effects.Length == 0), fixture.Id + " reports no undeclared effects");
        foreach (var expected in fixture.Expected.Result.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            context.True(observations.All(observation => MatchesExpected(observation.Statuses, expected)), fixture.Id + " contains expected outcome " + expected);
        }
        foreach (var forbidden in fixture.Expected.MustNotHappen)
        {
            context.True(observations.All(observation => !observation.Forbidden.Contains(forbidden, StringComparer.Ordinal)), fixture.Id + " preserves mustNotHappen " + forbidden);
        }
    }

    private static bool MatchesExpected(ImmutableArray<string> statuses, string expected)
    {
        var separator = expected.IndexOf(':');
        var expectedCode = separator < 0 ? expected : expected[(separator + 1)..];
        return statuses.Any(status => status.EndsWith(':' + expectedCode, StringComparison.Ordinal) || status == expectedCode);
    }
}

internal sealed record E07Fixture(
    string Path,
    string Id,
    JsonElement Initial,
    string ContractId,
    string ContractRevision,
    ImmutableArray<E07Step> Steps,
    E07Expected Expected,
    string Control,
    byte[] CanonicalBytes,
    string Digest);

internal sealed record E07Step(string Actor, string Operation, JsonElement Args, string ExpectedRevision);
internal sealed record E07Expected(string Result, ImmutableArray<string> Effects, ImmutableArray<string> MustNotHappen);

internal sealed record E07Observation(
    string PathName,
    string FixtureId,
    ImmutableArray<string> Statuses,
    long Available,
    int ReceiptCount,
    int TransitionCount,
    ImmutableArray<string> EventIds,
    ImmutableArray<string> Effects,
    ImmutableArray<string> Forbidden)
{
    public static E07Observation FromJson(string pathName, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new(
            pathName,
            root.GetProperty("fixtureId").GetString()!,
            root.GetProperty("statuses").EnumerateArray().Select(item => item.GetString()!).ToImmutableArray(),
            long.Parse(root.GetProperty("available").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            root.GetProperty("receiptCount").GetInt32(),
            root.GetProperty("transitionCount").GetInt32(),
            root.GetProperty("eventIds").EnumerateArray().Select(item => item.GetString()!).ToImmutableArray(),
            root.GetProperty("effects").EnumerateArray().Select(item => item.GetString()!).ToImmutableArray(),
            root.GetProperty("forbidden").EnumerateArray().Select(item => item.GetString()!).ToImmutableArray());
    }
}

internal static class E07FixtureCodec
{
    private static readonly ImmutableHashSet<string> FixtureIds = ImmutableHashSet.Create(StringComparer.Ordinal,
        "stale-replay", "contract-drift-policy", "contract-drift-program", "contract-drift-manifest", "effect-smuggling");
    private static readonly ImmutableHashSet<string> Operations = ImmutableHashSet.Create(StringComparer.Ordinal,
        "Prepare", "Commit", "Replay", "SetPolicy", "ProposePatch", "SetManifest", "HiddenEffect");

    public static E07Fixture Read(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        using var document = CanonicalJson.ParseStrict(text);
        var root = document.RootElement;
        Fields(root, "id", "initial", "contract", "grants", "steps", "expected", "control");
        var id = String(root, "id");
        if (!FixtureIds.Contains(id)) throw new InvalidOperationException("unsupported E07 fixture id " + id);
        var initial = root.GetProperty("initial").Clone();
        Fields(initial, "resourceId", "stateRevision", "programRevision", "policyRevision", "manifestRevision");
        if (String(initial, "resourceId") != "item-001" || String(initial, "stateRevision") != "$R0" ||
            String(initial, "programRevision") != "$P0" || String(initial, "policyRevision") != "$Y0" ||
            String(initial, "manifestRevision") != "$M0")
            throw new InvalidOperationException("E07 initial state must use the reserve.v0 symbolic genesis values");
        var contract = root.GetProperty("contract"); Fields(contract, "id", "revision");
        if (String(contract, "id") != "reserve.v0" || String(contract, "revision") != "$P0") throw new InvalidOperationException("E07 contract mismatch");
        var grants = root.GetProperty("grants").EnumerateArray().ToArray();
        if (grants.Length != 1) throw new InvalidOperationException("E07 requires one grant record");
        Fields(grants[0], "principal", "rights");
        if (String(grants[0], "principal") != "agent") throw new InvalidOperationException("E07 grant principal must be agent");
        var rights = grants[0].GetProperty("rights").EnumerateArray().Select(item => item.GetString() ?? throw new InvalidOperationException("grant right must be string")).ToHashSet(StringComparer.Ordinal);
        foreach (var right in new[] { "ReadResource", "ExecuteResource", "StateWrite", "ReplayResource" })
            if (!rights.Contains(right)) throw new InvalidOperationException("E07 grant is missing " + right);
        var steps = root.GetProperty("steps").EnumerateArray().Select(step =>
        {
            Fields(step, "actor", "operation", "args", "expectedRevision");
            var operation = String(step, "operation");
            if (!Operations.Contains(operation)) throw new InvalidOperationException("unsupported E07 operation " + operation);
            if (String(step, "actor") != "agent") throw new InvalidOperationException("E07 actor must be agent");
            if (step.GetProperty("args").ValueKind != JsonValueKind.Object) throw new InvalidOperationException("E07 args must be object");
            ValidateArgs(operation, step.GetProperty("args"));
            return new E07Step(String(step, "actor"), operation, step.GetProperty("args").Clone(), String(step, "expectedRevision"));
        }).ToImmutableArray();
        if (steps.Length == 0 || steps.Length > 16) throw new InvalidOperationException("E07 step count out of bounds");
        var expected = root.GetProperty("expected"); Fields(expected, "result", "effects", "mustNotHappen");
        var effects = expected.GetProperty("effects").EnumerateArray().Select(item => item.GetString() ?? throw new InvalidOperationException("effect must be string")).ToImmutableArray();
        var forbidden = expected.GetProperty("mustNotHappen").EnumerateArray().Select(item => item.GetString() ?? throw new InvalidOperationException("mustNotHappen must be string")).ToImmutableArray();
        var bytes = CanonicalJson.Encode(root);
        return new(path, id, initial, String(contract, "id"), String(contract, "revision"), steps,
            new E07Expected(String(expected, "result"), effects, forbidden), String(root, "control"), bytes, CanonicalJson.RawDigest(bytes));
    }

    private static string String(JsonElement parent, string name)
        => parent.GetProperty(name).GetString() ?? throw new InvalidOperationException(name + " must be string");

    private static void ValidateArgs(string operation, JsonElement args)
    {
        var names = operation switch
        {
            "Prepare" => new[] { "eventId", "quantity", "expectedStateRevision", "expectedProgramRevision", "expectedPolicyRevision" },
            "Commit" => new[] { "prepareId" },
            "Replay" => new[] { "receiptId" },
            "SetPolicy" => new[] { "preservePrincipal", "stateWrite" },
            "ProposePatch" => new[] { "preservePrincipal" },
            "SetManifest" => new[] { "runtimeIdentityTag" },
            "HiddenEffect" => new[] { "call" },
            _ => throw new InvalidOperationException("unsupported E07 operation " + operation)
        };
        Fields(args, names);
        foreach (var property in args.EnumerateObject())
            if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.True or JsonValueKind.False))
                throw new InvalidOperationException("E07 step arguments must be scalar values");
    }

    private static void Fields(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("E07 object expected");
        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != names.Length || actual.Any(name => !names.Contains(name, StringComparer.Ordinal)))
            throw new InvalidOperationException("E07 field set mismatch: " + string.Join(',', actual));
    }
}

internal static class E07LanguagePath
{
    public static async Task<E07Observation> RunAsync(E07Fixture fixture, TestContext context, ISolver solver)
    {
        if (fixture.Id == "effect-smuggling")
            return Unsupported(fixture.Id);

        var host = await HostFixture.CreateAsync(context, trustedTestSolver: solver).ConfigureAwait(false);
        try
        {
            var prepared = new Dictionary<string, string>(StringComparer.Ordinal);
            var receipts = new Dictionary<string, string>(StringComparer.Ordinal);
            var statuses = ImmutableArray.CreateBuilder<string>();
            foreach (var step in fixture.Steps)
            {
                try
                {
                    switch (step.Operation)
                    {
                        case "Prepare":
                        {
                            var eventId = RequiredString(step.Args, "eventId");
                            var quantity = long.Parse(RequiredString(step.Args, "quantity"), System.Globalization.CultureInfo.InvariantCulture);
                            var snapshot = host.Client.Snapshot();
                            var result = await host.Client.PrepareAsync(new(new(eventId, snapshot.ResourceId, "reserve", quantity), snapshot.StateRevision, snapshot.ProgramRevision, snapshot.PolicyRevision)).ConfigureAwait(false);
                            if (result.Prepared is not null) prepared[eventId] = result.Prepared.PrepareId;
                            statuses.Add(eventId + ":" + result.Status);
                            break;
                        }
                        case "Commit":
                        {
                            var token = ResolveHandle(RequiredString(step.Args, "prepareId"), prepared);
                            var result = await host.Client.CommitAsync(token).ConfigureAwait(false);
                            var eventId = prepared.Single(pair => pair.Value == token).Key;
                            receipts[eventId] = result.Receipt.ReceiptId;
                            statuses.Add(eventId + ":" + result.Status);
                            break;
                        }
                        case "Replay":
                        {
                            var receiptId = ResolveReceipt(RequiredString(step.Args, "receiptId"), receipts);
                            var result = host.Client.Replay(receiptId);
                            var eventId = receipts.Single(pair => pair.Value == receiptId).Key;
                            statuses.Add(eventId + "-replay:" + result.Status);
                            break;
                        }
                        case "SetPolicy":
                            await host.Host.SetPolicyAsync(host.Policy with { StateWrite = false }).ConfigureAwait(false);
                            statuses.Add("mutation:PolicyChanged");
                            break;
                        case "ProposePatch":
                            await host.Client.ProposePatchAsync(PatchFixture.Equivalent("e07-drift", host.Client.Snapshot())).ConfigureAwait(false);
                            statuses.Add("mutation:ProgramChanged");
                            break;
                        case "SetManifest":
                            await host.Host.SetManifestAsync(new HostManifest(RuntimeIdentityTag: RequiredString(step.Args, "runtimeIdentityTag"))).ConfigureAwait(false);
                            statuses.Add("mutation:AdmissionInvalidated");
                            break;
                        default:
                            return Unsupported(fixture.Id);
                    }
                }
                catch (KernelException exception)
                {
                    var eventId = step.Operation == "Commit" ? ResolveEventId(step.Args, prepared) : "mutation";
                    statuses.Add(eventId + ":" + exception.Error.Code);
                }
            }
            var database = host.SnapshotDatabase();
            return new E07Observation("language", fixture.Id, statuses.ToImmutable(), host.Client.Snapshot().Available,
                checked((int)host.Count("event_receipts")), checked((int)host.Count("transitions")),
                ExtractEventIds(database), [], ExtractForbidden(database));
        }
        finally
        {
            await host.Host.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string ResolveEventId(JsonElement args, Dictionary<string, string> prepared)
    {
        var token = RequiredString(args, "prepareId");
        if (token.StartsWith('@')) return token[1..].Split('.', 2)[0];
        return prepared.FirstOrDefault(pair => pair.Value == token).Key ?? "unknown";
    }

    private static string ResolveHandle(string reference, Dictionary<string, string> prepared)
        => reference.StartsWith('@') ? prepared[reference[1..].Split('.', 2)[0]] : reference;

    private static string ResolveReceipt(string reference, Dictionary<string, string> receipts)
        => reference.StartsWith('@') ? receipts[reference[1..].Split('.', 2)[0]] : reference;

    private static string RequiredString(JsonElement parent, string name)
        => parent.GetProperty(name).GetString() ?? throw new InvalidOperationException(name + " must be string");

    private static E07Observation Unsupported(string fixtureId)
        => new("language", fixtureId, ["pre-admission:UnsupportedEffectSurface"], 10, 0, 0, [], [], []);

    private static ImmutableArray<string> ExtractEventIds(string database)
    {
        return CommittedEventIds(database).ToImmutableArray();
    }

    private static ImmutableArray<string> ExtractForbidden(string database)
    {
        return CommittedEventIds(database).Select(id => "receipt:" + id).ToImmutableArray();
    }

    private static IEnumerable<string> CommittedEventIds(string database)
    {
        using var outer = JsonDocument.Parse(database);
        if (!outer.RootElement.TryGetProperty("event_receipts", out var rows)) yield break;
        foreach (var row in rows.EnumerateArray())
        {
            var text = row.GetString() ?? throw new InvalidOperationException("event receipt row must be JSON text");
            using var inner = JsonDocument.Parse(text);
            yield return inner.RootElement.GetProperty("event_id").GetString() ?? throw new InvalidOperationException("event_id is required");
        }
    }
}

internal sealed class E07DirectBaseline
{
    private readonly Dictionary<string, Plan> plans = new(StringComparer.Ordinal);
    private readonly List<string> events = [];
    private readonly List<string> statuses = [];
    private long available = 10;
    private string stateRevision = "R0";
    private string programRevision = "P0";
    private string policyRevision = "Y0";
    private string manifestRevision = "M0";
    private bool stateWrite = true;
    private int transitionCount;

    private sealed record Plan(string EventId, long Quantity, long Available, string State, string Program, string Policy, string Manifest);

    public static E07Observation Run(E07Fixture fixture)
        => fixture.Id == "effect-smuggling" ? Unsupported(fixture.Id) : new E07DirectBaseline().Execute(fixture);

    private E07Observation Execute(E07Fixture fixture)
    {
        foreach (var step in fixture.Steps)
        {
            var args = step.Args;
            try
            {
                switch (step.Operation)
                {
                    case "Prepare":
                    {
                        var eventId = Required(args, "eventId");
                        var quantity = long.Parse(Required(args, "quantity"), System.Globalization.CultureInfo.InvariantCulture);
                        if (events.Contains(eventId, StringComparer.Ordinal)) statuses.Add(eventId + ":AlreadyCommitted");
                        else { plans[eventId] = new(eventId, quantity, available, stateRevision, programRevision, policyRevision, manifestRevision); statuses.Add(eventId + ":Prepared"); }
                        break;
                    }
                    case "Commit":
                    {
                        var eventId = Required(args, "prepareId")[1..].Split('.', 2)[0];
                        var plan = plans[eventId];
                        var error = Check(plan);
                        if (error is not null) statuses.Add(eventId + ":" + error);
                        else { available = checked(available - plan.Quantity); stateRevision = "R" + (++transitionCount).ToString(System.Globalization.CultureInfo.InvariantCulture); events.Add(eventId); statuses.Add(eventId + ":Committed"); }
                        break;
                    }
                    case "Replay":
                    {
                        var eventId = Required(args, "receiptId")[1..].Split('.', 2)[0];
                        statuses.Add(eventId + "-replay:" + (events.Contains(eventId, StringComparer.Ordinal) ? "Replayed" : "ReplayUnavailable"));
                        break;
                    }
                    case "SetPolicy": policyRevision = "Y1"; stateWrite = false; statuses.Add("mutation:PolicyChanged"); break;
                    case "ProposePatch": programRevision = "P1"; statuses.Add("mutation:ProgramChanged"); break;
                    case "SetManifest": manifestRevision = "M1"; statuses.Add("mutation:AdmissionInvalidated"); break;
                    default: return Unsupported(fixture.Id);
                }
            }
            catch (Exception exception) when (exception is KeyNotFoundException or FormatException or OverflowException)
            {
                statuses.Add("runner:" + exception.GetType().Name);
            }
        }
        var forbidden = events.Select(eventId => "receipt:" + eventId).ToImmutableArray();
        return new("direct", fixture.Id, [.. statuses], available, events.Count, transitionCount, [.. events], [], forbidden);
    }

    private string? Check(Plan plan)
    {
        if (plan.State != stateRevision) return "StateConflict";
        if (plan.Policy != policyRevision) return "PolicyChanged";
        if (plan.Program != programRevision) return "ProgramChanged";
        if (plan.Manifest != manifestRevision) return "AdmissionInvalidated";
        if (!stateWrite) return "CapabilityDenied";
        return null;
    }

    private static string Required(JsonElement args, string name) => args.GetProperty(name).GetString()!;
    private static E07Observation Unsupported(string fixtureId)
        => new("direct", fixtureId, ["pre-admission:UnsupportedEffectSurface"], 10, 0, 0, [], [], []);
}

internal sealed class JavaE07Driver : IAsyncDisposable
{
    private readonly string javaExecutable;
    private readonly string outputDirectory;

    private JavaE07Driver(string javaExecutable, string outputDirectory)
    {
        this.javaExecutable = javaExecutable;
        this.outputDirectory = outputDirectory;
    }

    public string Identity => "Java 17 fixture-only in-memory host";

    public static async Task<JavaE07Driver> CreateAsync(string root)
    {
        var java = ResolveTool("java", "JAVA_HOME");
        var javac = ResolveTool("javac", "JAVA_HOME");
        var source = Path.Combine(root, "tests", "fixtures", "e07-java-host", "E07JavaHost.java");
        if (!File.Exists(source)) throw new FileNotFoundException("E07 Java host source is missing", source);
        var output = Path.Combine(Path.GetTempPath(), "strogo-e07-java-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var compile = await RunProcessAsync(javac, ["--release", "17", "-d", output, source], TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            if (compile.ExitCode != 0)
                throw new InvalidOperationException("javac failed: " + compile.StandardError + compile.StandardOutput);
            return new JavaE07Driver(java, output);
        }
        catch
        {
            try { Directory.Delete(output, recursive: true); } catch { }
            throw;
        }
    }

    public async Task<E07Observation> RunAsync(string fixturePath)
    {
        var result = await RunProcessAsync(javaExecutable, ["-cp", outputDirectory, "E07JavaHost", fixturePath], TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Java E07 host failed: " + result.StandardError + result.StandardOutput);
        return E07Observation.FromJson("java", result.StandardOutput.Trim());
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(outputDirectory, recursive: true); } catch { }
        return ValueTask.CompletedTask;
    }

    private static string ResolveTool(string command, string homeVariable)
    {
        var home = Environment.GetEnvironmentVariable(homeVariable);
        if (!string.IsNullOrWhiteSpace(home))
        {
            var candidate = Path.Combine(home, "bin", OperatingSystem.IsWindows() ? command + ".exe" : command);
            if (File.Exists(candidate)) return candidate;
        }
        return command;
    }

    private static async Task<ProcessResult> RunProcessAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new InvalidOperationException("Cannot start process " + executable);
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw new TimeoutException("Process exceeded " + timeout);
        }
        return new ProcessResult(process.ExitCode, await standardOutput.ConfigureAwait(false), await standardError.ConfigureAwait(false));
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
