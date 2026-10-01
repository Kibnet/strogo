using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kernel.Core;
using Strogo.Modules;

if (args is ["--contained-launch", var launchRequest, var launchResult]) { await ContainedLauncher.Run(launchRequest, launchResult); return; }
if (args is ["--orphan-parent", var pidPath])
{
    var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
    start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location); start.ArgumentList.Add("--orphan-descendant");
    using var descendant = System.Diagnostics.Process.Start(start)!;
    File.WriteAllText(pidPath, descendant.Id.ToString(CultureInfo.InvariantCulture));
    Console.Write("parent-exited"); return;
}
if (args is ["--orphan-descendant"]) { Thread.Sleep(TimeSpan.FromMinutes(1)); return; }
if (args is ["--output-probe", var outputMode])
{
    if (outputMode == "normal") { Console.Write("ok"); return; }
    if (outputMode is not ("stdout" or "stderr")) throw new ArgumentException("output mode");
    var output = outputMode == "stdout" ? Console.OpenStandardOutput() : Console.OpenStandardError();
    var chunk = Enumerable.Repeat((byte)'x', 8192).ToArray();
    for (var index = 0; index < 256; index++) output.Write(chunk);
    Thread.Sleep(TimeSpan.FromMinutes(1)); // The bounded parent must terminate this diagnostic writer.
    return;
}
if (args.Length != 5 || args.Any(value => !Path.IsPathFullyQualified(value))) throw new ArgumentException("absolute repo, descriptor, observer DLL, evidence directory, host package cache required");
byte[] Read(string path)
{
    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    if (file.Length > 65536) throw new Exception("host fixture file limit");
    var bytes = new byte[(int)file.Length]; file.ReadExactly(bytes); return bytes;
}
using var descriptor = JsonDocument.Parse(Read(args[1]));
var input = descriptor.RootElement;
string[] fields = ["schemaVersion", "purpose", "now", "bundleDigest", "packagePath", "operatorConfigPath", "releasePath", "badReleasePath", "initialStatePath", "nextStatePath"];
if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Count() != fields.Length ||
    input.EnumerateObject().Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length ||
    input.EnumerateObject().Any(value => !fields.Contains(value.Name, StringComparer.Ordinal) || value.Value.ValueKind != JsonValueKind.String)) throw new Exception("fixture schema");
string Text(string name) => input.GetProperty(name).GetString()!;
string Absolute(string name) => Path.IsPathFullyQualified(Text(name)) ? Path.GetFullPath(Text(name)) : throw new Exception("fixture path");
if (Text("schemaVersion") != "strogo.signed-observer-fixture.v0.1" || Text("purpose") != "disposable-test-key-not-public-admission") throw new Exception("fixture purpose");
var now = DateTimeOffset.ParseExact(Text("now"), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
var clock = new FixtureClock(now);
using var host = OwnerHostContext.Open(Absolute("operatorConfigPath"));
using var snapshot = G02PackageSnapshot.OpenStructural(Absolute("packagePath"));
using var sdk = G02DotNetToolchain.Open(args[0]);
var actualRuntime = G02RuntimeBinding.Open(sdk);
var syntheticRuntimeMismatch = false; var runtimeReads = 0;
var runtime = actualRuntime.ObserveFixtureForChecks(() =>
{
    runtimeReads++;
    var live = G02RuntimeBinding.CaptureLive();
    return syntheticRuntimeMismatch ? live with { CoreLibPath = Path.Combine(args[3], "System.Private.CoreLib.dll") } : live;
});
using var counter = new NativeCounter(args[2]);
var rows = new List<object>();
var beforeProof = counter.Read();
if (beforeProof != 0) throw new Exception("entries before proof");
var verified = await G02PackageProofReplay.VerifyBuildFixtureAsync(snapshot, host.Trust, host.ReadCurrentState(), Text("bundleDigest"), now,
    () => G02DafnyToolchain.Open(args[0]), () => G02DotNetToolchain.Open(args[0]), args[4], Path.Combine(args[3], "fresh-composed"));
var afterProof = counter.Read();
if (afterProof != beforeProof) throw new Exception("proof entered candidate");
rows.Add(new { id = "fresh-replay", before = beforeProof, after = afterProof, expectedNativeKinds = Array.Empty<string>() });
byte[] Role(string role) => snapshot.ReadHeld(snapshot.Manifest.Files.Single(value => value.Role == role).Path);
var module = ModulesCompiler.Compile(ModulesParser.ParseModule(Role("module")));
var bundle = OwnerBundleV04Parser.Parse(Role("bundle"));
var binding = OwnerContractBinderV04.Bind(module, bundle);
if (binding.Entries.Length != 1) throw new Exception("one export profile");
var entry = binding.Entries[0]; var witness = entry.Witnesses[0];
var byId = witness.Arguments.ToDictionary(value => value.ParameterId, value => value.Value);
var arguments = entry.Function.Parameters.Select(value => byId[value.Id]).ToArray();
var reference = ModulesReferenceEvaluator.Invoke(module, entry.Function.Id, arguments).Value;
if (!OwnerContractEvaluator.StructuralEquals(reference, witness.ModelResult)) throw new Exception("owner/reference mismatch");
byte[] Request(ModuleValue[] values) => CanonicalJson.Encode(new { schemaVersion = G02InvocationCodec.RequestVersion, functionId = entry.Function.Id,
    arguments = values.Select((value, index) => new { parameterId = entry.Function.Parameters[index].Id,
        value = OwnerBundleCodec.ValuePayload(value, value is ModuleBool ? new TypeRef("Bool") : entry.Function.Parameters[index].Type) }).Reverse().ToArray() });
var request = Request(arguments); var release = Read(Absolute("releasePath"));
var releaseDigest = OwnerAdmissionWire.Hash("strogo.admission.v0.2/artifact", release);
var providerReads = 0; var brokenProvider = false;
byte[] Provider() { providerReads++; if (brokenProvider) throw new IOException("synthetic fixture provider failure"); return host.ReadCurrentState(); }
var generatedLoads = 0;
using var pe = new PEReader(new MemoryStream(Role("entry-assembly")));
var metadata = pe.GetMetadataReader(); var assemblyName = metadata.GetString(metadata.GetAssemblyDefinition().Name);
AssemblyLoadEventHandler observer = (_, value) => { if (value.LoadedAssembly.GetName().Name == assemblyName) generatedLoads++; };
AppDomain.CurrentDomain.AssemblyLoad += observer;
try
{
    var before = counter.Read(); var reads = providerReads; string? code = null; string? stage = null;
    try { using var refused = G02SignedFixtureSession.OpenForObserver(Absolute("packagePath"), verified, host.Trust, Provider,
        Read(Absolute("badReleasePath")), Text("bundleDigest"), clock, runtime); }
    catch (ModuleException error) { code = error.Code; stage = error.Stage; }
    var after = counter.Read();
    if (code != "InvalidOwnerSignature" || stage != "admission" || before != after || generatedLoads != 0 || providerReads != reads + 1) throw new Exception("bad release gate");
    rows.Add(new { id = "bad-release-before-load", before, after, expectedNativeKinds = Array.Empty<string>(), refusal = code, stage,
        assemblyLoads = generatedLoads, providerReadsDelta = providerReads - reads });
    before = counter.Read(); reads = providerReads;
    using var session = G02SignedFixtureSession.OpenForObserver(Absolute("packagePath"), verified, host.Trust, Provider, release, Text("bundleDigest"), clock, runtime);
    after = counter.Read();
    var entryPath = Path.GetFullPath(Path.Combine(Absolute("packagePath"), snapshot.Manifest.EntryAssemblyPath));
    var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(value => !value.IsDynamic && string.Equals(value.Location, entryPath, StringComparison.OrdinalIgnoreCase));
    if (generatedLoads != 1 || before != after || providerReads != reads + 1) throw new Exception("open gate");
    rows.Add(new { id = "open", before, after, expectedNativeKinds = Array.Empty<string>(), assemblyLoads = generatedLoads, location = assembly.Location,
        providerReadsDelta = providerReads - reads });
    var map = ModulesDafnyLowerer.Lower(module, bundle).SourceMap.ToDictionary(value => value.EntityId, value => value.GeneratedName);
    var target = assembly.GetType("Candidate.__default") ?? throw new Exception("candidate type");
    object Method(string kind, string symbol)
    {
        var method = target.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Single(value => value.Name == symbol);
        return new { kind, symbol, mvid = method.Module.ModuleVersionId, token = method.MetadataToken };
    }
    var methods = new[] { Method("Q", map["owner/contract/" + entry.Contract.Id + "/requires"]), Method("F", map["function/" + entry.Function.Id]) };
    void Call(string id, byte[] bytes, string? refusal, string[] expectedKinds, bool readState = true)
    {
        var first = counter.Read(); var priorReads = providerReads; var priorRuntimeReads = runtimeReads; var attempts = session.DispatchAttempts;
        using var response = JsonDocument.Parse(session.InvokeJson(bytes)); var last = counter.Read();
        var envelope = response.RootElement; var result = envelope.GetProperty("result");
        if (envelope.GetProperty("packageDigest").GetString() != snapshot.Manifest.PackageDigest || envelope.GetProperty("buildManifestDigest").GetString() != snapshot.Manifest.ArtifactDigest ||
            envelope.GetProperty("contractApprovalDigest").GetString() != snapshot.Manifest.ContractApprovalDigest || envelope.GetProperty("releaseAdmissionDigest").GetString() != releaseDigest ||
            !envelope.GetProperty("validationOnly").GetBoolean()) throw new Exception("identity " + id);
        if (refusal is null)
        {
            if (result.GetProperty("status").GetString() != "Returned" || !CanonicalJson.Encode(result.GetProperty("value")).SequenceEqual(CanonicalJson.Encode(OwnerBundleCodec.ValuePayload(witness.ModelResult, entry.Function.ReturnType)))) throw new Exception("result " + id);
        }
        else
        {
            var stage = refusal is "SchemaInvalid" or "InputTypeMismatch" ? "invoke"
                : refusal is "CompiledPreconditionFailed" or "RuntimeBindingMismatch" or "CompiledFixtureDisposed" ? "package" : "admission";
            if (result.GetProperty("status").GetString() != "Refused" || result.GetProperty("error").GetProperty("code").GetString() != refusal || result.GetProperty("error").GetProperty("stage").GetString() != stage) throw new Exception("refusal " + id);
        }
        var dispatchDelta = refusal is null || refusal == "CompiledPreconditionFailed" ? 1 : 0;
        var runtimeDelta = expectedKinds.Length > 0 || id == "runtime-mismatch" ? 1 : 0;
        if (last - first != expectedKinds.Length || providerReads - priorReads != (readState ? 1 : 0) || session.DispatchAttempts - attempts != dispatchDelta || runtimeReads - priorRuntimeReads != runtimeDelta) throw new Exception("interval " + id);
        rows.Add(new { id, before = first, after = last, expectedNativeKinds = expectedKinds, refusal, response = envelope.Clone(),
            providerReadsDelta = providerReads - priorReads, runtimeReadsDelta = runtimeReads - priorRuntimeReads, dispatchAttemptsDelta = session.DispatchAttempts - attempts,
            synthetic = id is "provider-failure" or "runtime-mismatch" or "release-expired" });
    }
    Call("valid", request, null, ["Q", "F"]);
    Call("malformed", Encoding.UTF8.GetBytes("{}"), "SchemaInvalid", [], false);
    var wrongType = (ModuleValue[])arguments.Clone(); wrongType[0] = new ModuleBool(true);
    Call("typed-invalid", Request(wrongType), "InputTypeMismatch", [], false);
    ModuleValue[] invalidPrecondition = entry.Function.Parameters.Length == 1 ? [new ModuleI64(long.MaxValue)]
        : [new ModuleSequence(new TypeRef("I64"), 256, Array.Empty<ModuleValue>()), new ModuleI64(-1)];
    Call("requires-invalid", Request(invalidPrecondition), "CompiledPreconditionFailed", ["Q"]);
    brokenProvider = true; Call("provider-failure", request, "OwnerStateUnavailable", []); brokenProvider = false;
    syntheticRuntimeMismatch = true;
    Call("runtime-mismatch", request, "RuntimeBindingMismatch", []); syntheticRuntimeMismatch = false;
    clock.Now = now.AddMinutes(11); Call("release-expired", request, "ReleaseAdmissionExpired", []); clock.Now = now;
    Call("restored", request, null, ["Q", "F"]);
    void ReplaceState(string path)
    {
        var temporary = Path.Combine(host.OwnerStateDirectory, "fixture-replace-" + Guid.NewGuid().ToString("N") + ".json");
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(Read(path)); file.Flush(true); }
        File.Replace(temporary, Path.Combine(host.OwnerStateDirectory, "owner-state.json"), null);
    }
    ReplaceState(Absolute("nextStatePath")); Call("epoch", request, "ApprovalEpochMismatch", []);
    ReplaceState(Absolute("initialStatePath")); Call("rollback", request, "OwnerStateRollbackDetected", []);
    session.Dispose(); Call("disposed", request, "CompiledFixtureDisposed", [], false);
    Call("disposed-malformed", Encoding.UTF8.GetBytes("{}"), "CompiledFixtureDisposed", [], false);
    snapshot.Revalidate(); sdk.Revalidate();
    Console.WriteLine(JsonSerializer.Serialize(new { purpose = "signed-fixture-native-observer-not-public-admission", validationOnly = true,
        functionId = entry.Function.Id, packageDigest = snapshot.Manifest.PackageDigest, buildManifestDigest = snapshot.Manifest.ArtifactDigest,
        contractApprovalDigest = snapshot.Manifest.ContractApprovalDigest, releaseAdmissionDigest = releaseDigest,
        entrySha256 = Convert.ToHexStringLower(SHA256.HashData(Role("entry-assembly"))), entryLength = Role("entry-assembly").Length,
        sourceMapSha256 = Convert.ToHexStringLower(SHA256.HashData(Role("source-map"))), methods, rows, finalNativeCount = counter.Read(),
        generatedLoads, proofReplayDirectory = Path.Combine(args[3], "fresh-composed") }));
    GC.KeepAlive(session);
}
finally { AppDomain.CurrentDomain.AssemblyLoad -= observer; }

internal sealed class FixtureClock(DateTimeOffset now) : TimeProvider
{
    internal DateTimeOffset Now = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
internal sealed class NativeCounter : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ReadCount(out uint count);
    private readonly nint library; private readonly ReadCount read;
    internal NativeCounter(string path)
    {
        library = NativeLibrary.Load(Path.GetFullPath(path));
        try { read = Marshal.GetDelegateForFunctionPointer<ReadCount>(NativeLibrary.GetExport(library, "StrogoObserverReadEntryCount")); }
        catch { NativeLibrary.Free(library); throw; }
    }
    internal uint Read() => read(out var count) == 0 ? count : throw new Exception("native prefix unavailable");
    public void Dispose() => NativeLibrary.Free(library);
}
