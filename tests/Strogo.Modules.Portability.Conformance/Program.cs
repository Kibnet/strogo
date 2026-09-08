using System.Collections.Immutable;
using System.Text.Json;
using Kernel.Core;
using Strogo.Modules;
using Strogo.Modules.Portability;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var fixtureDir = Path.Combine(root, "fixtures", "portability-v0.1");
var reportOption = Array.IndexOf(args, "--report");
if (reportOption >= 0 && reportOption + 1 >= args.Length) throw new ArgumentException("--report requires a path");
var publicApiOption = Array.IndexOf(args, "--public-api-out");
if (publicApiOption >= 0 && publicApiOption + 1 >= args.Length) throw new ArgumentException("--public-api-out requires a path");
var dafnyOption = Array.IndexOf(args, "--dafny-out");
if (dafnyOption >= 0 && dafnyOption + 1 >= args.Length) throw new ArgumentException("--dafny-out requires a path");
var weakDafnyOption = Array.IndexOf(args, "--weak-dafny-out");
if (weakDafnyOption >= 0 && weakDafnyOption + 1 >= args.Length) throw new ArgumentException("--weak-dafny-out requires a path");
var reportPath = reportOption >= 0
    ? Path.GetFullPath(args[reportOption + 1], root)
    : Path.Combine(root, "artifacts", "local-validation", "e06", "contract-v0.1.json");
var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}

var moduleBytes = File.ReadAllBytes(Path.Combine(fixtureDir, "module.json"));
var ownerBytes = File.ReadAllBytes(Path.Combine(fixtureDir, "owner.json"));
var weakModuleBytes = File.ReadAllBytes(Path.Combine(fixtureDir, "module-weak-invariant.json"));
var parsed = ModulesParser.ParseModule(moduleBytes);
var module = ModulesCompiler.Compile(parsed);
var owner = OwnerBundleV04Parser.Parse(ownerBytes);
var binding = PortabilityContract.Bind(module, owner);
var repeated = PortabilityContract.Bind(ModulesCompiler.Compile(ModulesParser.ParseModule(moduleBytes)), OwnerBundleV04Parser.Parse(ownerBytes));
var lowering = ModulesDafnyLowerer.Lower(module, owner);
var repeatedLowering = ModulesDafnyLowerer.Lower(repeated.Module, repeated.OwnerBundle);
var weakBinding = PortabilityContract.Bind(ModulesCompiler.Compile(ModulesParser.ParseModule(weakModuleBytes)), OwnerBundleV04Parser.Parse(ownerBytes));
var weakLowering = ModulesDafnyLowerer.Lower(weakBinding.Module, weakBinding.OwnerBundle);
if (publicApiOption >= 0)
{
    var output = Path.GetFullPath(args[publicApiOption + 1], root);
    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? throw new InvalidOperationException("public API output path has no directory"));
    File.WriteAllBytes(output, binding.PublicApiBytes);
}
var expectedPublicApi = File.ReadAllBytes(Path.Combine(fixtureDir, "public-api.json"));
if (dafnyOption >= 0)
{
    var output = Path.GetFullPath(args[dafnyOption + 1], root);
    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? throw new InvalidOperationException("Dafny output path has no directory"));
    File.WriteAllBytes(output, lowering.SourceBytes);
    File.WriteAllBytes(Path.ChangeExtension(output, ".source-map.json"), CanonicalJson.Encode(lowering.SourceMap));
    File.WriteAllBytes(Path.ChangeExtension(output, ".obligations.json"), CanonicalJson.Encode(lowering.Obligations));
}
if (weakDafnyOption >= 0)
{
    var output = Path.GetFullPath(args[weakDafnyOption + 1], root);
    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? throw new InvalidOperationException("weak Dafny output path has no directory"));
    File.WriteAllBytes(output, weakLowering.SourceBytes);
    File.WriteAllBytes(Path.ChangeExtension(output, ".source-map.json"), CanonicalJson.Encode(weakLowering.SourceMap));
    File.WriteAllBytes(Path.ChangeExtension(output, ".obligations.json"), CanonicalJson.Encode(weakLowering.Obligations));
}

Check(binding.Module.SourceDigest == repeated.Module.SourceDigest, "module digest is deterministic");
Check(binding.OwnerBundle.BundleDigest == repeated.OwnerBundle.BundleDigest, "owner digest is deterministic");
Check(binding.PublicApiDigest == repeated.PublicApiDigest && binding.PublicApiBytes.SequenceEqual(repeated.PublicApiBytes), "public API is deterministic");
Check(binding.PublicApiBytes.SequenceEqual(expectedPublicApi), "public API bytes match the frozen fixture");
var mutableApiCopy = binding.PublicApiBytes;
mutableApiCopy[0] ^= 0xff;
Check(binding.PublicApiBytes.SequenceEqual(expectedPublicApi), "public API bytes are defensively copied");
Check(ModulesDafnyLowerer.MixedOwnerToolchainIdentity == "strogo.owner-dafny-lowering.v0.5", "mixed owner lowering version is fixed");
Check(lowering.SourceBytes.SequenceEqual(repeatedLowering.SourceBytes) && lowering.SourceDigest == repeatedLowering.SourceDigest, "mixed owner lowering is deterministic");
Check(lowering.ProofIdentity == repeatedLowering.ProofIdentity, "mixed owner proof identity is deterministic");
Check(lowering.SourceDigest != weakLowering.SourceDigest && binding.Module.SourceDigest != weakBinding.Module.SourceDigest, "strong and weak invariant identities differ");
Check(binding.PublicApiBytes.SequenceEqual(weakBinding.PublicApiBytes), "invariant strength does not change the public ABI");
Check(lowering.Obligations.Any(obligation => obligation.Kind == "call-contract" && obligation.Id == "call-adjust-incremented"), "adjust to increment call contract is an explicit obligation");
Check(lowering.SourceMap.Any(item => item.EntityId == "function/adjust/body/node/result/then/node/incremented"), "call source map entry is retained");
Check(PortabilityContract.IsExecutionProfile(PortabilityVersions.DotNetProfile), "dotnet profile is closed");
Check(PortabilityContract.IsExecutionProfile(PortabilityVersions.JvmProfile), "JVM profile is closed");
Check(!PortabilityContract.IsExecutionProfile("native"), "unknown profile is rejected");

var replay = OwnerContractReplayV04.Replay(binding.OwnerBinding);
Check(replay.Status == "Pass" && replay.CheckedWitnesses == 8, $"owner witnesses: {replay.Status}/{replay.CheckedWitnesses}");

using var vectorsDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtureDir, "vectors.json")));
var vectors = vectorsDocument.RootElement;
Fields(vectors, "schemaVersion", "moduleId", "ownerBundleId", "valid", "ownerRefusals", "transportCaseIds", "mutationIds");
Check(vectors.GetProperty("schemaVersion").GetString() == "strogo.portability-vectors.v0.1", "vector schema is fixed");
Check(vectors.GetProperty("moduleId").GetString() == module.ModuleId, "vectors bind module id");
Check(vectors.GetProperty("ownerBundleId").GetString() == owner.BundleId, "vectors bind owner id");

var validCount = 0;
foreach (var vector in vectors.GetProperty("valid").EnumerateArray())
{
    Fields(vector, "id", "functionId", "arguments", "expected");
    var arguments = vector.GetProperty("arguments").EnumerateArray().Select(value => ParseValue(value, module.Types)).ToArray();
    var expected = ParseValue(vector.GetProperty("expected"), module.Types);
    var actual = PortabilityContract.InvokeOwner(binding, RequiredString(vector, "functionId"), arguments);
    Check(actual.Kind == "success" && actual.Value is not null && Equal(actual.Value, expected), $"valid vector failed: {RequiredString(vector, "id")}");
    var candidate = ModulesReferenceEvaluator.Invoke(module, RequiredString(vector, "functionId"), arguments).Value;
    Check(Equal(candidate, expected), $"reference candidate diverged: {RequiredString(vector, "id")}");
    validCount++;
}
Check(validCount == 10, "all ten mandatory valid vectors are frozen");

var refusalCount = 0;
foreach (var vector in vectors.GetProperty("ownerRefusals").EnumerateArray())
{
    Fields(vector, "id", "functionId", "arguments", "code", "locus");
    var arguments = vector.GetProperty("arguments").EnumerateArray().Select(value => ParseValue(value, module.Types)).ToArray();
    var actual = PortabilityContract.InvokeOwner(binding, RequiredString(vector, "functionId"), arguments);
    Check(actual.Kind == "refusal" && actual.Code == RequiredString(vector, "code") && actual.Locus == RequiredString(vector, "locus"), $"owner refusal failed: {RequiredString(vector, "id")} actual={actual.Kind}/{actual.Code}/{actual.Locus}");
    refusalCount++;
}
Check(refusalCount == 3, "all three mandatory owner refusals are frozen");

var transportIds = vectors.GetProperty("transportCaseIds").EnumerateArray().Select(item => item.GetString()!).ToArray();
Check(transportIds.Length == 24 && transportIds.Distinct(StringComparer.Ordinal).Count() == 24, "transport cases are complete and unique");
var mutationIds = vectors.GetProperty("mutationIds").EnumerateArray().Select(item => item.GetString()!).ToArray();
Check(mutationIds.SequenceEqual(new[] { "sum-sign", "reverse-fold-input", "eager-head-branch", "refusal-code" }, StringComparer.Ordinal), "mutation set is frozen");

var wrongType = PortabilityContract.InvokeOwner(binding, "increment", [new ModuleBool(true)]);
Check(wrongType.Code == "RuntimeTypeMismatch" && wrongType.Locus == "$/arguments/0", "type mismatch is fail closed");
var wrongArity = PortabilityContract.InvokeOwner(binding, "increment", []);
Check(wrongArity.Code == "ArityMismatch" && wrongArity.Locus == "$/arguments", "arity mismatch is fail closed");
var unknown = PortabilityContract.InvokeOwner(binding, "missing", []);
Check(unknown.Code == "UnknownFunction" && unknown.Locus == "$/functionId", "unknown function is fail closed");

using (var api = JsonDocument.Parse(binding.PublicApiBytes))
{
    Fields(api.RootElement, "functions", "limits", "moduleId", "ownerBundleId", "refusalPriority", "schemaVersion", "targetOperations", "types", "wireKinds");
    Check(api.RootElement.GetProperty("schemaVersion").GetString() == PortabilityVersions.PublicApiSchema, "public API schema is fixed");
    Check(api.RootElement.GetProperty("functions").GetArrayLength() == 5, "public API contains five functions");
}

Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? throw new InvalidOperationException("report path has no directory"));
var report = CanonicalJson.Encode(new
{
    schemaVersion = "strogo.portability-contract-check.v0.1",
    status = "Passed",
    checks,
    validVectors = validCount,
    ownerRefusals = refusalCount,
    transportCases = transportIds.Length,
    mutations = mutationIds.Length,
    moduleDigest = binding.Module.SourceDigest,
    ownerBundleDigest = binding.OwnerBundle.BundleDigest,
    publicApiDigest = binding.PublicApiDigest,
    dafnySourceDigest = lowering.SourceDigest,
    proofIdentity = lowering.ProofIdentity,
    proofObligations = lowering.Obligations.Length,
    weakDafnySourceDigest = weakLowering.SourceDigest
});
File.WriteAllBytes(reportPath, report);
Console.WriteLine($"PASS portability contract checks={checks} valid={validCount} refusals={refusalCount} transport={transportIds.Length} mutations={mutationIds.Length}");

static void Fields(JsonElement element, params string[] expected)
{
    if (element.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("expected object");
    var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
    if (actual.Distinct(StringComparer.Ordinal).Count() != actual.Length || !actual.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        throw new InvalidOperationException($"unexpected fields: {string.Join(',', actual)}");
}

static string RequiredString(JsonElement element, string property)
    => element.GetProperty(property).GetString() ?? throw new InvalidOperationException($"{property} must be string");

static ModuleValue ParseValue(JsonElement element, ImmutableArray<TypeDecl> declarations)
{
    Fields(element, "type", "value");
    var type = ParseType(element.GetProperty("type"));
    var payload = element.GetProperty("value");
    return type.Kind switch
    {
        "I64" => new ModuleI64(long.Parse(payload.GetString()!, System.Globalization.CultureInfo.InvariantCulture)),
        "Bool" => new ModuleBool(payload.GetBoolean()),
        "Seq" => new ModuleSequence(type.Element!, type.Capacity!.Value, payload.EnumerateArray().Select(item => ParseValue(item, declarations))),
        "Record" => new ModuleRecord(type.Name!, payload.EnumerateArray().Select(item =>
        {
            Fields(item, "fieldId", "value");
            return KeyValuePair.Create(RequiredString(item, "fieldId"), ParseValue(item.GetProperty("value"), declarations));
        })),
        _ => throw new InvalidOperationException($"unsupported fixture value: {type}")
    };
}

static TypeRef ParseType(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.String)
    {
        var id = element.GetString()!;
        return id is "I64" or "Bool" ? new TypeRef(id) : new TypeRef("Record", Name: id);
    }
    Fields(element, "kind", "elementType", "capacity");
    if (RequiredString(element, "kind") != "Seq") throw new InvalidOperationException("only Seq composite fixture type is supported");
    return new TypeRef("Seq", Element: ParseType(element.GetProperty("elementType")), Capacity: int.Parse(RequiredString(element, "capacity"), System.Globalization.CultureInfo.InvariantCulture));
}

static bool Equal(ModuleValue left, ModuleValue right) => (left, right) switch
{
    (ModuleI64 a, ModuleI64 b) => a.Value == b.Value,
    (ModuleBool a, ModuleBool b) => a.Value == b.Value,
    (ModuleSequence a, ModuleSequence b) => a.Capacity == b.Capacity && a.Items.Length == b.Items.Length && a.Items.Zip(b.Items).All(pair => Equal(pair.First, pair.Second)),
    (ModuleRecord a, ModuleRecord b) => a.RecordTypeId == b.RecordTypeId && a.Fields.Count == b.Fields.Count && a.Fields.All(pair => b.Fields.TryGetValue(pair.Key, out var value) && Equal(pair.Value, value)),
    _ => false
};
