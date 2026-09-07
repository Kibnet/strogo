using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Strogo.Modules;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var fixtureDir = Path.Combine(root, "fixtures", "modules-v0.2");
var reportOption = Array.IndexOf(args, "--report");
if (reportOption >= 0 && reportOption + 1 >= args.Length)
    throw new ArgumentException("--report requires a path");
var dafnyOutOption = Array.IndexOf(args, "--dafny-out");
if (dafnyOutOption >= 0 && dafnyOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-out requires a path");
var dafnyCallOutOption = Array.IndexOf(args, "--dafny-call-out");
if (dafnyCallOutOption >= 0 && dafnyCallOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-call-out requires a path");
var dafnyUnsafeOutOption = Array.IndexOf(args, "--dafny-unsafe-out");
if (dafnyUnsafeOutOption >= 0 && dafnyUnsafeOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-unsafe-out requires a path");
var dafnyScalarOutOption = Array.IndexOf(args, "--dafny-scalar-out");
if (dafnyScalarOutOption >= 0 && dafnyScalarOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-scalar-out requires a path");
var dafnyCompositeOutOption = Array.IndexOf(args, "--dafny-composite-out");
if (dafnyCompositeOutOption >= 0 && dafnyCompositeOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-composite-out requires a path");
var dafnyCompositeUnsafeOutOption = Array.IndexOf(args, "--dafny-composite-unsafe-out");
if (dafnyCompositeUnsafeOutOption >= 0 && dafnyCompositeUnsafeOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-composite-unsafe-out requires a path");
var dafnyOwnerOutOption = Array.IndexOf(args, "--dafny-owner-out");
if (dafnyOwnerOutOption >= 0 && dafnyOwnerOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-owner-out requires a path");
var dafnyOwnerWeakOutOption = Array.IndexOf(args, "--dafny-owner-weak-out");
if (dafnyOwnerWeakOutOption >= 0 && dafnyOwnerWeakOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-owner-weak-out requires a path");
var dafnyOwnerAlternativeOutOption = Array.IndexOf(args, "--dafny-owner-alternative-out");
if (dafnyOwnerAlternativeOutOption >= 0 && dafnyOwnerAlternativeOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-owner-alternative-out requires a path");
var dafnyOwnerWrongOutOption = Array.IndexOf(args, "--dafny-owner-wrong-out");
if (dafnyOwnerWrongOutOption >= 0 && dafnyOwnerWrongOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-owner-wrong-out requires a path");

var reportPath = reportOption >= 0
    ? Path.GetFullPath(args[reportOption + 1], root)
    : Path.Combine(root, "artifacts", "local-validation", "e05", "modules-v0.2", "conformance.json");
var dafnyOutPath = dafnyOutOption >= 0 ? Path.GetFullPath(args[dafnyOutOption + 1], root) : null;
var dafnyCallOutPath = dafnyCallOutOption >= 0 ? Path.GetFullPath(args[dafnyCallOutOption + 1], root) : null;
var dafnyUnsafeOutPath = dafnyUnsafeOutOption >= 0 ? Path.GetFullPath(args[dafnyUnsafeOutOption + 1], root) : null;
var dafnyScalarOutPath = dafnyScalarOutOption >= 0 ? Path.GetFullPath(args[dafnyScalarOutOption + 1], root) : null;
var dafnyCompositeOutPath = dafnyCompositeOutOption >= 0 ? Path.GetFullPath(args[dafnyCompositeOutOption + 1], root) : null;
var dafnyCompositeUnsafeOutPath = dafnyCompositeUnsafeOutOption >= 0 ? Path.GetFullPath(args[dafnyCompositeUnsafeOutOption + 1], root) : null;
var dafnyOwnerOutPath = dafnyOwnerOutOption >= 0 ? Path.GetFullPath(args[dafnyOwnerOutOption + 1], root) : null;
var dafnyOwnerWeakOutPath = dafnyOwnerWeakOutOption >= 0 ? Path.GetFullPath(args[dafnyOwnerWeakOutOption + 1], root) : null;
var dafnyOwnerAlternativeOutPath = dafnyOwnerAlternativeOutOption >= 0 ? Path.GetFullPath(args[dafnyOwnerAlternativeOutOption + 1], root) : null;
var dafnyOwnerWrongOutPath = dafnyOwnerWrongOutOption >= 0 ? Path.GetFullPath(args[dafnyOwnerWrongOutOption + 1], root) : null;
var reportDir = Path.GetDirectoryName(reportPath) ?? throw new InvalidOperationException("Report path has no directory");
Directory.CreateDirectory(reportDir);
var reports = new List<object>();
var checks = 0;

void Check(bool condition, string message)
{
    checks++;
    if (!condition)
        throw new Exception(message);
}

void ExpectParserReject(string file, params string[] expectedCodes)
{
    var bytes = File.ReadAllBytes(Path.Combine(fixtureDir, file));

    ExpectParserRejectBytes(bytes, file, expectedCodes);
}

void ExpectParserRejectBytes(byte[] bytes, string caseName, params string[] expectedCodes)
{

    try
    {
        _ = ModulesParser.ParseModule(bytes);
        throw new Exception($"Expected rejection: {caseName}");
    }
    catch (ModuleException ex)
    {
        Check(expectedCodes.Length == 0 || expectedCodes.Contains(ex.Code),
            $"Unexpected code for {caseName}: {ex.Code}. Expected {string.Join(',', expectedCodes)}");
        reports.Add(new { kind = "negative", fixture = caseName, code = ex.Code, stage = ex.Stage, detail = ex.DetailsJson });
    }
}

ModuleException CaptureParserReject(string source)
{
    try
    {
        _ = ModulesParser.ParseModule(Encoding.UTF8.GetBytes(source));
        throw new Exception("Expected parser rejection");
    }
    catch (ModuleException exception)
    {
        return exception;
    }
}

ModuleException CaptureEvaluationReject(Func<ModuleEvaluationResult> evaluate)
{
    try
    {
        _ = evaluate();
        throw new Exception("Expected evaluation rejection");
    }
    catch (ModuleException exception)
    {
        return exception;
    }
}

ModuleException CaptureLoweringReject(Func<DafnyLoweringResult> lower)
{
    try
    {
        _ = lower();
        throw new Exception("Expected lowering rejection");
    }
    catch (ModuleException exception)
    {
        return exception;
    }
}

ModuleException CaptureOwnerReject(Action action)
{
    try
    {
        action();
        throw new Exception("Expected owner contract rejection");
    }
    catch (ModuleException exception)
    {
        return exception;
    }
}

void ExpectParserRejectWithLimits(string file, StrogoLimits limits, params string[] expectedCodes)
{
    var bytes = File.ReadAllBytes(Path.Combine(fixtureDir, file));
    ExpectParserRejectWithLimitsBytes(bytes, file, limits, expectedCodes);
}

void ExpectParserRejectWithLimitsBytes(byte[] bytes, string caseName, StrogoLimits limits, params string[] expectedCodes)
{
    try
    {
        _ = ModulesParser.ParseModule(bytes, limits);
        throw new Exception($"Expected rejection: {caseName}");
    }
    catch (ModuleException ex)
    {
        Check(expectedCodes.Contains(ex.Code), $"Unexpected code for {caseName}: {ex.Code}. Expected {string.Join(',', expectedCodes)}");
        reports.Add(new { kind = "negative", fixture = caseName, code = ex.Code, stage = ex.Stage, detail = ex.DetailsJson });
    }
}

string NestedIfRegion(int nestedIfCount)
{
    const string leaf = "{\"parameters\":[\"c\",\"x\"],\"nodes\":[],\"result\":\"x\"}";
    if (nestedIfCount == 0) return leaf;
    var child = NestedIfRegion(nestedIfCount - 1);
    return "{\"parameters\":[\"c\",\"x\"],\"nodes\":[{\"id\":\"branch\",\"op\":\"if\",\"type\":\"I64\",\"args\":[\"c\",\"c\",\"x\"],\"thenRegion\":" + child + ",\"elseRegion\":" + leaf + "}],\"result\":\"branch\"}";
}

try
{
    var validBytes = File.ReadAllBytes(Path.Combine(fixtureDir, "math-add-valid.json"));
    var valid = ModulesParser.ParseModule(validBytes);
    Check(valid.Stage == "parse", "Parsed fixture stage");
    Check(valid.Source.SchemaVersion == "strogo.module.v0.2", "schemaVersion");
    Check(valid.Source.Exports.Length == 1 && valid.Source.Exports[0] == "addOne", "exports");

    var canonical = ModulesCodec.Canonicalize(valid.Source);
    var reparse = ModulesParser.ParseModule(canonical);
    Check(valid.SourceDigest == reparse.SourceDigest, "canonical roundtrip digest");
    Check(reparse.Tokens.Length > 0, "tokens");

    var ir = ModulesCompiler.Compile(valid);
    Check(ir.Functions.Length == 1, "ir function count");

    var ownerBytes = File.ReadAllBytes(Path.Combine(fixtureDir, "owner-add-one-valid.json"));
    var owner = OwnerBundleParser.Parse(ownerBytes);
    Check(owner.SchemaVersion == "strogo.owner-bundle.v0.2", "owner bundle schemaVersion");
    Check(owner.EntryContracts.Length == 1 && owner.Models.Length == 1, "owner bundle entries and models parsed");
    Check(owner.BundleDigest.Length == 64, "owner bundle has canonical digest");
    Check(owner.BundleDigest == OwnerBundleCodec.BundleDigest(owner.CanonicalBytes), "owner bundle digest uses the versioned artifact domain");
    Check(owner.BundleDigest != Kernel.Core.CanonicalJson.RawDigest(owner.CanonicalBytes), "owner bundle digest cannot be confused with raw content SHA-256");
    var ownerRoundtrip = OwnerBundleParser.Parse(owner.CanonicalBytes);
    Check(ownerRoundtrip.BundleDigest == owner.BundleDigest, "owner bundle canonical roundtrip digest");
    var ownerBytesCopy = owner.CanonicalBytes;
    ownerBytesCopy[0] ^= 0x01;
    Check(OwnerBundleParser.Parse(owner.CanonicalBytes).BundleDigest == owner.BundleDigest, "owner canonical bytes are defensively copied");
    Check(typeof(OwnerBundle).GetConstructors().Length == 0, "validated owner bundle cannot be publicly forged");
    var ownerBinding = OwnerContractBinder.Bind(ir, owner);
    Check(ownerBinding.Entries.Length == 1 && ownerBinding.Entries[0].Function.Id == "addOne", "owner contract binds exact module entry");
    Check(ownerBinding.Entries[0].Witnesses[0].ModelResult.I64 == 1, "owner witness is independently evaluated through the model");
    var contractedLowering = ModulesDafnyLowerer.Lower(ir, owner);
    Check(contractedLowering.Source.Contains("requires (p000 <= 9223372036854775806)", StringComparison.Ordinal), "owner requires is emitted into Dafny");
    Check(contractedLowering.Source.Contains("ensures result == M000(p000)", StringComparison.Ordinal), "exact owner outcome is emitted into Dafny");
    Check(contractedLowering.Source.Contains("function M000", StringComparison.Ordinal), "owner model is emitted separately from candidate body");
    Check(!contractedLowering.Source.Contains("addOne.model-v1", StringComparison.Ordinal) && !contractedLowering.Source.Contains("contractA", StringComparison.Ordinal), "owner IDs cannot become generated Dafny syntax");
    Check(contractedLowering.SourceMap.Any(entry => entry.EntityId == "owner/model/addOne.model-v1") && contractedLowering.SourceMap.Any(entry => entry.EntityId == "owner/contract/contractA/ensures"), "owner model and obligations retain stable source-map identities");
    Check(contractedLowering.SourceMap.Any(entry => entry.EntityId == "owner/model/addOne.model-v1/parameter/x" && entry.GeneratedName == "p000") && contractedLowering.SourceMap.Any(entry => entry.EntityId == "owner/contract/contractA/parameter/x" && entry.GeneratedName == "p000"), "owner parameter identities map to generated symbols");
    if (dafnyOwnerOutPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dafnyOwnerOutPath) ?? throw new InvalidOperationException("Dafny owner output path has no directory"));
        File.WriteAllBytes(dafnyOwnerOutPath, contractedLowering.SourceBytes);
    }
    var weakOwner = OwnerBundleParser.Parse(File.ReadAllBytes(Path.Combine(fixtureDir, "owner-add-one-weak.json")));
    var weakOwnerLowering = ModulesDafnyLowerer.Lower(ir, weakOwner);
    if (dafnyOwnerWeakOutPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dafnyOwnerWeakOutPath) ?? throw new InvalidOperationException("Dafny weak owner output path has no directory"));
        File.WriteAllBytes(dafnyOwnerWeakOutPath, weakOwnerLowering.SourceBytes);
    }
    var alternativeIr = ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "math-add-alternative.json"))));
    var alternativeOwnerLowering = ModulesDafnyLowerer.Lower(alternativeIr, owner);
    Check(alternativeIr.SourceDigest != ir.SourceDigest && alternativeOwnerLowering.SourceDigest != contractedLowering.SourceDigest, "structurally different implementations retain distinct identities");
    if (dafnyOwnerAlternativeOutPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dafnyOwnerAlternativeOutPath) ?? throw new InvalidOperationException("Dafny alternative owner output path has no directory"));
        File.WriteAllBytes(dafnyOwnerAlternativeOutPath, alternativeOwnerLowering.SourceBytes);
    }
    var wrongIr = ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "math-add-wrong.json"))));
    var wrongOwnerLowering = ModulesDafnyLowerer.Lower(wrongIr, owner);
    if (dafnyOwnerWrongOutPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dafnyOwnerWrongOutPath) ?? throw new InvalidOperationException("Dafny wrong owner output path has no directory"));
        File.WriteAllBytes(dafnyOwnerWrongOutPath, wrongOwnerLowering.SourceBytes);
    }

    var ownerText = Encoding.UTF8.GetString(ownerBytes);
    string DuplicateModelOwner(bool malformedFirst)
    {
        var rootNode = JsonNode.Parse(ownerText)!.AsObject();
        var validModel = rootNode["models"]![0]!.DeepClone();
        var malformedModel = validModel.DeepClone();
        malformedModel["body"]!["unexpected"] = true;
        rootNode["models"] = malformedFirst
            ? new JsonArray(malformedModel, validModel)
            : new JsonArray(validModel, malformedModel);
        return rootNode.ToJsonString();
    }

    string DuplicateEntryOwner(bool malformedFirst)
    {
        var rootNode = JsonNode.Parse(ownerText)!.AsObject();
        var validEntry = rootNode["entryContracts"]![0]!.DeepClone();
        var malformedEntry = validEntry.DeepClone();
        malformedEntry["requires"]!["unexpected"] = true;
        rootNode["entryContracts"] = malformedFirst
            ? new JsonArray(malformedEntry, validEntry)
            : new JsonArray(validEntry, malformedEntry);
        return rootNode.ToJsonString();
    }

    string DuplicateWitnessOwner(bool malformedFirst)
    {
        var rootNode = JsonNode.Parse(ownerText)!.AsObject();
        var entry = rootNode["entryContracts"]![0]!;
        var validWitness = entry["witnesses"]![0]!.DeepClone();
        var malformedWitness = validWitness.DeepClone();
        malformedWitness["arguments"] = "malformed";
        entry["witnesses"] = malformedFirst
            ? new JsonArray(malformedWitness, validWitness)
            : new JsonArray(validWitness, malformedWitness);
        return rootNode.ToJsonString();
    }

    var duplicateModelFirst = CaptureOwnerReject(() => OwnerBundleParser.Parse(DuplicateModelOwner(malformedFirst: true)));
    var duplicateModelLast = CaptureOwnerReject(() => OwnerBundleParser.Parse(DuplicateModelOwner(malformedFirst: false)));
    Check(duplicateModelFirst.Code == "DuplicateModelId" && duplicateModelFirst.EntityId == "addOne.model-v1", "owner parser preflights duplicate model IDs before model bodies");
    Check(duplicateModelFirst.Code == duplicateModelLast.Code && duplicateModelFirst.EntityId == duplicateModelLast.EntityId && duplicateModelFirst.DetailsJson == duplicateModelLast.DetailsJson, "reordered duplicate owner models return identical diagnostics");
    var duplicateEntryFirst = CaptureOwnerReject(() => OwnerBundleParser.Parse(DuplicateEntryOwner(malformedFirst: true)));
    var duplicateEntryLast = CaptureOwnerReject(() => OwnerBundleParser.Parse(DuplicateEntryOwner(malformedFirst: false)));
    Check(duplicateEntryFirst.Code == "DuplicateEntryContractId" && duplicateEntryFirst.EntityId == "contractA", "owner parser preflights duplicate entry IDs before entry bodies");
    Check(duplicateEntryFirst.Code == duplicateEntryLast.Code && duplicateEntryFirst.EntityId == duplicateEntryLast.EntityId && duplicateEntryFirst.DetailsJson == duplicateEntryLast.DetailsJson, "reordered duplicate owner entries return identical diagnostics");
    var duplicateWitnessFirst = CaptureOwnerReject(() => OwnerBundleParser.Parse(DuplicateWitnessOwner(malformedFirst: true)));
    var duplicateWitnessLast = CaptureOwnerReject(() => OwnerBundleParser.Parse(DuplicateWitnessOwner(malformedFirst: false)));
    Check(duplicateWitnessFirst.Code == "DuplicateWitnessId" && duplicateWitnessFirst.EntityId == "contract/contractA/witness/zero-v1", "owner parser preflights duplicate witness IDs before witness bodies");
    Check(duplicateWitnessFirst.Code == duplicateWitnessLast.Code && duplicateWitnessFirst.EntityId == duplicateWitnessLast.EntityId && duplicateWitnessFirst.DetailsJson == duplicateWitnessLast.DetailsJson, "reordered duplicate owner witnesses return identical diagnostics");
    string InvalidModelIdKindOwner(bool numberFirst)
    {
        var rootNode = JsonNode.Parse(ownerText)!.AsObject();
        var numericIdModel = rootNode["models"]![0]!.DeepClone();
        numericIdModel["id"] = 1;
        var stringIdModel = rootNode["models"]![0]!.DeepClone();
        stringIdModel["id"] = "1";
        rootNode["models"] = numberFirst
            ? new JsonArray(numericIdModel, stringIdModel)
            : new JsonArray(stringIdModel, numericIdModel);
        return rootNode.ToJsonString();
    }

    var invalidModelIdKindFirst = CaptureOwnerReject(() => OwnerBundleParser.Parse(InvalidModelIdKindOwner(numberFirst: true)));
    var invalidModelIdKindLast = CaptureOwnerReject(() => OwnerBundleParser.Parse(InvalidModelIdKindOwner(numberFirst: false)));
    Check(invalidModelIdKindFirst.Code == "SchemaInvalid", "invalid-input structural ordering distinguishes a JSON number from a string");
    Check(invalidModelIdKindFirst.Code == invalidModelIdKindLast.Code && invalidModelIdKindFirst.EntityId == invalidModelIdKindLast.EntityId && invalidModelIdKindFirst.DetailsJson == invalidModelIdKindLast.DetailsJson, "colliding artifact encodings return identical diagnostics after model reordering");
    string UnknownWitnessArgumentsOwner(bool zFirst)
    {
        var rootNode = JsonNode.Parse(ownerText)!.AsObject();
        var arguments = rootNode["entryContracts"]![0]!["witnesses"]![0]!["arguments"]!.AsArray();
        var yArgument = arguments[0]!.DeepClone();
        yArgument["parameterId"] = "y";
        var zArgument = arguments[0]!.DeepClone();
        zArgument["parameterId"] = "z";
        arguments.Clear();
        if (zFirst)
        {
            arguments.Add(zArgument);
            arguments.Add(yArgument);
        }
        else
        {
            arguments.Add(yArgument);
            arguments.Add(zArgument);
        }
        return rootNode.ToJsonString();
    }

    var unknownArgumentZFirst = CaptureOwnerReject(() => OwnerBundleParser.Parse(UnknownWitnessArgumentsOwner(zFirst: true)));
    var unknownArgumentYFirst = CaptureOwnerReject(() => OwnerBundleParser.Parse(UnknownWitnessArgumentsOwner(zFirst: false)));
    Check(unknownArgumentZFirst.Code == "UnknownWitnessParameter" && unknownArgumentZFirst.EntityId == "y", "owner parser chooses the lowest stable unknown witness parameter ID");
    Check(unknownArgumentZFirst.Code == unknownArgumentYFirst.Code && unknownArgumentZFirst.EntityId == unknownArgumentYFirst.EntityId && unknownArgumentZFirst.DetailsJson == unknownArgumentYFirst.DetailsJson, "reordered unknown witness arguments return identical diagnostics");
    var invalidWitness = CaptureOwnerReject(() => OwnerBundleParser.Parse(Encoding.UTF8.GetBytes(ownerText.Replace("\"value\": \"0\"", "\"value\": \"9223372036854775807\"", StringComparison.Ordinal))));
    Check(invalidWitness.Code == "RequiresWitnessRejected", "owner requires needs a concrete satisfying witness");
    var missingWitnesses = CaptureOwnerReject(() => OwnerBundleParser.Parse(Encoding.UTF8.GetBytes(ownerText.Replace("\"witnesses\": [", "\"witnessesRemoved\": [", StringComparison.Ordinal))));
    Check(missingWitnesses.Code == "SchemaInvalid", "owner witness field is mandatory");
    var emptyWitnessNode = JsonNode.Parse(ownerText)!.AsObject();
    emptyWitnessNode["entryContracts"]![0]!["witnesses"] = new JsonArray();
    var emptyWitnessArray = CaptureOwnerReject(() => OwnerBundleParser.Parse(emptyWitnessNode.ToJsonString()));
    Check(emptyWitnessArray.Code == "RequiresWitnessRequired", "owner bundle rejects an empty witness array");
    var nonExactEnsures = CaptureOwnerReject(() => OwnerBundleParser.Parse(Encoding.UTF8.GetBytes(ownerText.Replace("\"op\": \"model.call\"", "\"op\": \"param\"", StringComparison.Ordinal))));
    Check(nonExactEnsures.Code is "SchemaInvalid" or "ExactOutcomeRequired", "owner ensures cannot omit exact model call");
    var reversedEnsuresNode = JsonNode.Parse(ownerText)!.AsObject();
    var reversedEnsuresArgs = reversedEnsuresNode["entryContracts"]![0]!["ensures"]!["args"]!.AsArray();
    var originalResult = reversedEnsuresArgs[0]!.DeepClone();
    var originalModelCall = reversedEnsuresArgs[1]!.DeepClone();
    reversedEnsuresArgs[0] = originalModelCall;
    reversedEnsuresArgs[1] = originalResult;
    var reversedEnsures = CaptureOwnerReject(() => OwnerBundleParser.Parse(reversedEnsuresNode.ToJsonString()));
    Check(reversedEnsures.Code == "ExactOutcomeRequired", "owner exact outcome has one canonical operand order");
    var effectfulOwner = CaptureOwnerReject(() => OwnerBundleParser.Parse(Encoding.UTF8.GetBytes(ownerText.Replace("\"effects\": []", "\"effects\": [\"network\"]", StringComparison.Ordinal))));
    Check(effectfulOwner.Code == "EffectsNotSupported", "scalar owner bundle rejects effects");
    string EffectfulOwner(bool networkFirst)
    {
        var rootNode = JsonNode.Parse(ownerText)!.AsObject();
        rootNode["entryContracts"]![0]!["effects"] = networkFirst
            ? new JsonArray("network", "filesystem")
            : new JsonArray("filesystem", "network");
        return rootNode.ToJsonString();
    }

    var effectOrderOne = CaptureOwnerReject(() => OwnerBundleParser.Parse(EffectfulOwner(networkFirst: true)));
    var effectOrderTwo = CaptureOwnerReject(() => OwnerBundleParser.Parse(EffectfulOwner(networkFirst: false)));
    Check(effectOrderOne.Code == effectOrderTwo.Code && effectOrderOne.EntityId == effectOrderTwo.EntityId && effectOrderOne.DetailsJson == effectOrderTwo.DetailsJson, "unsupported owner effects return transport-order-independent diagnostics");
    var arithmeticRequiresNode = JsonNode.Parse(ownerText)!.AsObject();
    arithmeticRequiresNode["entryContracts"]![0]!["requires"] = JsonNode.Parse("{\"op\":\"eq\",\"type\":\"Bool\",\"args\":[{\"op\":\"i64.add\",\"type\":\"I64\",\"args\":[{\"op\":\"param\",\"type\":\"I64\",\"id\":\"x\"},{\"op\":\"i64.const\",\"type\":\"I64\",\"value\":\"1\"}]},{\"op\":\"i64.const\",\"type\":\"I64\",\"value\":\"1\"}]}");
    var arithmeticRequires = CaptureOwnerReject(() => OwnerBundleParser.Parse(arithmeticRequiresNode.ToJsonString()));
    Check(arithmeticRequires.Code == "ArithmeticInRequiresNotSupported", "scalar owner requires fails closed outside its total predicate subset");
    var booleanArithmeticModelNode = JsonNode.Parse(ownerText)!.AsObject();
    booleanArithmeticModelNode["entryContracts"]![0]!["returnType"] = "Bool";
    booleanArithmeticModelNode["entryContracts"]![0]!["ensures"]!["args"]![0]!["type"] = "Bool";
    booleanArithmeticModelNode["entryContracts"]![0]!["ensures"]!["args"]![1]!["type"] = "Bool";
    booleanArithmeticModelNode["models"]![0]!["returnType"] = "Bool";
    booleanArithmeticModelNode["models"]![0]!["body"] = JsonNode.Parse("{\"op\":\"bool.or\",\"type\":\"Bool\",\"args\":[{\"op\":\"bool.const\",\"type\":\"Bool\",\"value\":true},{\"op\":\"eq\",\"type\":\"Bool\",\"args\":[{\"op\":\"i64.add\",\"type\":\"I64\",\"args\":[{\"op\":\"param\",\"type\":\"I64\",\"id\":\"x\"},{\"op\":\"i64.const\",\"type\":\"I64\",\"value\":\"1\"}]},{\"op\":\"i64.const\",\"type\":\"I64\",\"value\":\"0\"}]}]}");
    var booleanArithmeticModel = CaptureOwnerReject(() => OwnerBundleParser.Parse(booleanArithmeticModelNode.ToJsonString()));
    Check(booleanArithmeticModel.Code == "ArithmeticInBooleanContractNotSupported", "Boolean models cannot hide undefined I64 arithmetic behind backend short-circuit evaluation");
    var undefinedModelText = ownerText.Replace("9223372036854775806", "9223372036854775807", StringComparison.Ordinal)
        .Replace("\"value\": \"0\"", "\"value\": \"9223372036854775807\"", StringComparison.Ordinal);
    var undefinedModel = CaptureOwnerReject(() => OwnerBundleParser.Parse(Encoding.UTF8.GetBytes(undefinedModelText)));
    Check(undefinedModel.Code == "ModelUndefinedAtWitness", "owner model must be defined for its accepted witness");
    var mismatchedOwner = OwnerBundleParser.Parse(Encoding.UTF8.GetBytes(ownerText.Replace("\"functionRef\": \"addOne\"", "\"functionRef\": \"other\"", StringComparison.Ordinal)));
    var mismatch = CaptureOwnerReject(() => OwnerContractBinder.Bind(ir, mismatchedOwner));
    Check(mismatch.Code == "OwnerEntryMismatch", "owner contract cannot bind a different function");
    var contractRefMismatchIr = ModulesCompiler.Compile(ModulesParser.ParseModule(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(validBytes).Replace("\"contractRef\": \"contractA\"", "\"contractRef\": \"otherContract\"", StringComparison.Ordinal))));
    var contractRefMismatch = CaptureOwnerReject(() => OwnerContractBinder.Bind(contractRefMismatchIr, owner));
    Check(contractRefMismatch.Code == "OwnerContractRefMismatch", "agent module cannot substitute an owner contract reference");
    var renamedParameterText = Encoding.UTF8.GetString(validBytes)
        .Replace("\"id\": \"x\", \"type\": \"I64\"", "\"id\": \"y\", \"type\": \"I64\"", StringComparison.Ordinal)
        .Replace("\"parameters\": [\"x\"]", "\"parameters\": [\"y\"]", StringComparison.Ordinal)
        .Replace("[\"x\", \"one\"]", "[\"y\", \"one\"]", StringComparison.Ordinal);
    var renamedParameterIr = ModulesCompiler.Compile(ModulesParser.ParseModule(renamedParameterText));
    var signatureMismatch = CaptureOwnerReject(() => OwnerContractBinder.Bind(renamedParameterIr, owner));
    Check(signatureMismatch.Code == "OwnerSignatureMismatch", "owner binding includes stable parameter identities, not only parameter types");
    var helperMismatch = CaptureOwnerReject(() => OwnerContractBinder.Bind(ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "call-safe.json")))), owner));
    Check(helperMismatch.Code == "UnsupportedOwnerHelpers", "scalar owner checkpoint fails closed for helper contracts");
    reports.Add(new { kind = "owner-contract", fixture = "owner-add-one-valid.json", owner.BundleDigest, contractedLowering.SourceDigest, alternativeSourceDigest = alternativeOwnerLowering.SourceDigest, wrongSourceDigest = wrongOwnerLowering.SourceDigest, witnesses = ownerBinding.Entries[0].Witnesses.Length });

    var first = ir.Functions[0];
    Check(first.Instructions.Length == valid.Source.Functions[0].Body.Nodes.Length, "ir instruction count");

    Check(first.Instructions[0].DestinationIndex == 0, "first instruction index");
    Check(first.ResultIndex == 1, "result index");

    var scalarBytes = File.ReadAllBytes(Path.Combine(fixtureDir, "scalar-ops-valid.json"));
    var scalar = ModulesParser.ParseModule(scalarBytes);
    var scalarIr = ModulesCompiler.Compile(scalar);
    var scalarOpcodes = scalarIr.Functions.Single().Instructions.Select(instruction => instruction.Op).ToHashSet(StringComparer.Ordinal);
    foreach (var opcode in new[] { "i64.sub", "i64.le", "i64.eq", "bool.const", "bool.not", "bool.and", "bool.or" })
        Check(scalarOpcodes.Contains(opcode), $"{opcode} lowered");

    var composite = ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "composite-valid.json")));
    var shuffled = ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "composite-valid-shuffled.json")));
    Check(composite.SourceDigest == shuffled.SourceDigest, "declaration and node order do not change canonical source digest");
    var compositeRoundtrip = ModulesParser.ParseModule(composite.CanonicalSource);
    Check(composite.SourceDigest == compositeRoundtrip.SourceDigest, "composite canonical source roundtrip digest");

    var compositeIr = ModulesCompiler.Compile(composite);
    var shuffledIr = ModulesCompiler.Compile(shuffled);
    Check(ModulesCodec.IrDigest(compositeIr.CanonicalBytes) == ModulesCodec.IrDigest(shuffledIr.CanonicalBytes), "equivalent composite modules compile to the same IR digest");
    Check(compositeIr.Functions.Length == 3, "composite function count");
    Check(compositeIr.FunctionCountDigest.Length == 64, "function count digest uses a valid Strogo domain");
    Check(compositeIr.Types.Length == 1 && compositeIr.Types[0].Id == "Summary", "record declarations preserved in IR");
    Check(compositeIr.Imports.Length == 0, "imports preserved in IR");
    Check(compositeIr.Exports.SequenceEqual(["countValues"]), "exports preserved in IR");

    var makeSummary = compositeIr.Functions.Single(function => function.Id == "makeSummary");
    Check(makeSummary.Parameters[0].Id == "x" && makeSummary.Parameters[0].Type.Kind == "I64", "parameter signature preserved in IR");
    Check(makeSummary.ContractRef == "makeSummaryContract", "contract binding preserved in IR");
    Check(makeSummary.Instructions.Any(instruction => instruction.Op == "record.make"), "record.make lowered");
    Check(makeSummary.Instructions.Any(instruction => instruction.Op == "seq.append"), "seq.append lowered");
    Check(makeSummary.Instructions.Any(instruction => instruction.Op == "seq.get"), "seq.get lowered");
    Check(makeSummary.Instructions.Any(instruction => instruction.Op == "call"), "call lowered");

    var branching = ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "if-valid.json")));
    var branchingShuffled = ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "if-valid-shuffled.json")));
    Check(branching.SourceDigest == branchingShuffled.SourceDigest, "nested region node order does not change canonical source digest");
    Check(branching.Source.Functions.Single().Body.Nodes.Single().ThenRegion is not null, "if branch regions are present in validated source AST");
    var branchingRoundtrip = ModulesParser.ParseModule(branching.CanonicalSource);
    Check(branching.SourceDigest == branchingRoundtrip.SourceDigest, "nested region canonical source roundtrip digest");
    var branchingIr = ModulesCompiler.Compile(branching);
    var branchingShuffledIr = ModulesCompiler.Compile(branchingShuffled);
    Check(ModulesCodec.IrDigest(branchingIr.CanonicalBytes) == ModulesCodec.IrDigest(branchingShuffledIr.CanonicalBytes), "nested regions compile to the same canonical IR digest");
    var branchInstruction = branchingIr.Functions.Single().Instructions.Single(instruction => instruction.Op == "if");
    Check(branchInstruction.ThenRegion is not null && branchInstruction.ElseRegion is not null, "if preserves both regions in typed IR");
    var thenRegion = branchInstruction.ThenRegion ?? throw new Exception("Missing then region");
    var elseRegion = branchInstruction.ElseRegion ?? throw new Exception("Missing else region");
    Check(thenRegion.Parameters.Select(parameter => parameter.Type.Kind).SequenceEqual(["I64", "I64"]), "if environment types are explicit in typed IR");
    Check(thenRegion.Instructions.Length == 2 && elseRegion.Instructions.Length == 2, "if branches are lowered as nested regions instead of eager outer instructions");

    var trueBranch = ModulesReferenceEvaluator.Invoke(branchingIr, "choosePlusOne", [new ModuleBool(true), new ModuleI64(2), new ModuleI64(9)]);
    var falseBranch = ModulesReferenceEvaluator.Invoke(branchingIr, "choosePlusOne", [new ModuleBool(false), new ModuleI64(2), new ModuleI64(9)]);
    Check(trueBranch.Value is ModuleI64 { Value: 3 }, "reference evaluator selects the then branch");
    Check(falseBranch.Value is ModuleI64 { Value: 10 }, "reference evaluator selects the else branch");
    Check(trueBranch.Steps == 3 && falseBranch.Steps == 3, "only the selected branch contributes evaluation steps");
    var scalarResult = ModulesReferenceEvaluator.Invoke(scalarIr, "compare", [new ModuleI64(5)]);
    Check(scalarResult.Value is ModuleBool { Value: true } && scalarResult.Steps == 8, "all scalar opcodes have executable reference semantics");
    var scalarReference = ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "scalar-reference-valid.json"))));
    long EvalI64(string function, params long[] values) => ((ModuleI64)ModulesReferenceEvaluator.Invoke(scalarReference, function, values.Select(value => (ModuleValue)new ModuleI64(value)).ToArray()).Value).Value;
    bool EvalBool(string function, params ModuleValue[] values) => ((ModuleBool)ModulesReferenceEvaluator.Invoke(scalarReference, function, values).Value).Value;
    Check(EvalI64("subtract", 7, 2) == 5, "i64.sub preserves operand order");
    Check(EvalI64("subtract", -3, 2) == -5, "i64.sub preserves negative exact results");
    Check(EvalBool("lessOrEqual", new ModuleI64(2), new ModuleI64(2)), "i64.le includes equality");
    Check(!EvalBool("lessOrEqual", new ModuleI64(3), new ModuleI64(2)), "i64.le has an observable false case");
    Check(EvalBool("equal", new ModuleI64(2), new ModuleI64(2)), "i64.eq has an observable true case");
    Check(!EvalBool("equal", new ModuleI64(2), new ModuleI64(3)), "i64.eq has an observable false case");
    Check(!EvalBool("negate", new ModuleBool(true)), "bool.not maps true to false");
    Check(EvalBool("negate", new ModuleBool(false)), "bool.not maps false to true");
    foreach (var left in new[] { false, true })
        foreach (var right in new[] { false, true })
        {
            Check(EvalBool("conjunction", new ModuleBool(left), new ModuleBool(right)) == (left && right), $"bool.and truth table {left}/{right}");
            Check(EvalBool("disjunction", new ModuleBool(left), new ModuleBool(right)) == (left || right), $"bool.or truth table {left}/{right}");
        }
    reports.Add(new { kind = "runtime", fixture = "scalar-reference-valid.json", semanticChecks = 16 });

    var lazyModule = ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "if-lazy-overflow.json"))));
    var safeMaximum = ModulesReferenceEvaluator.Invoke(lazyModule, "safeSelect", [new ModuleBool(false), new ModuleI64(long.MaxValue)]);
    Check(safeMaximum.Value is ModuleI64 { Value: long.MaxValue }, "unused overflowing branch is not evaluated");
    Check(safeMaximum.Steps == 1, "empty selected branch does not consume hidden evaluation steps");
    var overflow = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(lazyModule, "safeSelect", [new ModuleBool(true), new ModuleI64(long.MaxValue)]));
    Check(overflow.Code == "ArithmeticOverflow" && overflow.EntityId == "function/safeSelect/body/node/chosen/then/node/overflow", "selected overflowing branch fails at its stable node locus");
    var wrongRuntimeType = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(lazyModule, "safeSelect", [new ModuleI64(0), new ModuleI64(1)]));
    Check(wrongRuntimeType.Code == "RuntimeTypeMismatch" && wrongRuntimeType.EntityId == "function/safeSelect/parameter/condition", "runtime input types are checked before evaluation");
    var stepLimit = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(branchingIr, "choosePlusOne", [new ModuleBool(true), new ModuleI64(2), new ModuleI64(9)], new ModuleEvaluationLimits { MaxSteps = 2 }));
    Check(stepLimit.Code == "EvaluationStepLimitExceeded", "reference evaluation fails closed at its explicit step limit");
    var invalidEvaluationLimits = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(branchingIr, "choosePlusOne", [new ModuleBool(true), new ModuleI64(2), new ModuleI64(9)], new ModuleEvaluationLimits { MaxSteps = 0 }));
    Check(invalidEvaluationLimits.Code == "InvalidEvaluationLimits", "reference evaluator rejects a zero step budget");
    var excessiveEvaluationLimits = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(branchingIr, "choosePlusOne", [new ModuleBool(true), new ModuleI64(2), new ModuleI64(9)], new ModuleEvaluationLimits { MaxSteps = ModuleEvaluationLimits.StepsHardMaximum + 1 }));
    Check(excessiveEvaluationLimits.Code == "InvalidEvaluationLimits", "reference evaluator rejects a step budget above its hard maximum");
    var scalarCalls = ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "call-scalar-valid.json"))));
    var twice = ModulesReferenceEvaluator.Invoke(scalarCalls, "twice", [new ModuleI64(2)]);
    Check(twice.Value is ModuleI64 { Value: 4 } && twice.Steps == 6, "local scalar calls use the same checked reference semantics and shared step budget");
    var nestedCallLimit = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(scalarCalls, "twice", [new ModuleI64(2)], new ModuleEvaluationLimits { MaxSteps = 5 }));
    Check(nestedCallLimit.Code == "EvaluationStepLimitExceeded" && nestedCallLimit.EntityId == "function/addOne/body/node/sum", "one step budget is enforced across the complete local call tree");
    var internalCall = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(scalarCalls, "addOne", [new ModuleI64(2)]));
    Check(internalCall.Code == "FunctionNotExported", "public reference invocation cannot bypass module exports");
    var wrongArity = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(scalarCalls, "twice", Array.Empty<ModuleValue>()));
    Check(wrongArity.Code == "ArgumentCountMismatch", "runtime arity is checked before evaluation");
    var compositeResult = ModulesReferenceEvaluator.Invoke(compositeIr, "countValues", [new ModuleI64(2)]);
    Check(compositeResult.Value is ModuleI64 { Value: 4 } && compositeResult.Steps == 13, "reference evaluator executes record and sequence composition through local calls");

    var compositeRuntime = ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "composite-runtime-valid.json"))));
    var oneItem = new ModuleSequence(new TypeRef("I64"), 2, [new ModuleI64(7)]);
    var twoItems = new ModuleSequence(new TypeRef("I64"), 2, [new ModuleI64(7), new ModuleI64(9)]);
    var appended = ModulesReferenceEvaluator.Invoke(compositeRuntime, "append", [oneItem, new ModuleI64(9)]);
    Check(appended.Value is ModuleSequence { Items: var appendedItems } && appendedItems.Select(item => ((ModuleI64)item).Value).SequenceEqual([7L, 9L]), "seq.append preserves order and returns a new bounded sequence");
    Check(ModulesReferenceEvaluator.Invoke(compositeRuntime, "size", [twoItems]).Value is ModuleI64 { Value: 2 }, "seq.length returns the exact bounded length");
    Check(ModulesReferenceEvaluator.Invoke(compositeRuntime, "getAt", [twoItems, new ModuleI64(1)]).Value is ModuleI64 { Value: 9 }, "seq.get preserves zero-based order");
    var pairResult = ModulesReferenceEvaluator.Invoke(compositeRuntime, "makePair", [new ModuleI64(5)]);
    Check(pairResult.Value is ModuleRecord { RecordTypeId: "Pair" } pair
        && pair.Fields["first"] is ModuleI64 { Value: 5 }
        && pair.Fields["items"] is ModuleSequence { Items.Length: 1 }, "record.make preserves typed fields including a nested sequence");
    var pairInput = new ModuleRecord("Pair", new Dictionary<string, ModuleValue>
    {
        ["first"] = new ModuleI64(11),
        ["items"] = oneItem
    });
    Check(ModulesReferenceEvaluator.Invoke(compositeRuntime, "readFirst", [pairInput]).Value is ModuleI64 { Value: 11 }, "record.get accepts a recursively validated record input");
    var reverseFields = ImmutableSortedDictionary.CreateRange(
        Comparer<string>.Create((left, right) => StringComparer.Ordinal.Compare(right, left)),
        new Dictionary<string, ModuleValue> { ["first"] = new ModuleI64(12), ["items"] = oneItem });
    Check(ModulesReferenceEvaluator.Invoke(compositeRuntime, "readFirst", [new ModuleRecord("Pair", reverseFields)]).Value is ModuleI64 { Value: 12 }, "record input identity does not depend on the caller dictionary comparer");

    var fullAppend = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(compositeRuntime, "append", [twoItems, new ModuleI64(10)]));
    Check(fullAppend.Code == "SequenceCapacityExceeded" && fullAppend.EntityId == "function/append/body/node/result", "seq.append rejects capacity overflow at the stable node locus");
    var badIndex = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(compositeRuntime, "getAt", [oneItem, new ModuleI64(1)]));
    Check(badIndex.Code == "SequenceIndexOutOfRange" && badIndex.EntityId == "function/getAt/body/node/result", "seq.get rejects an out-of-range index at the stable node locus");
    var negativeIndex = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(compositeRuntime, "getAt", [oneItem, new ModuleI64(-1)]));
    Check(negativeIndex.Code == "SequenceIndexOutOfRange" && negativeIndex.EntityId == "function/getAt/body/node/result", "seq.get rejects a negative index at the stable node locus");
    var wrongCapacity = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(compositeRuntime, "size", [new ModuleSequence(new TypeRef("I64"), 3, [new ModuleI64(7)])]));
    Check(wrongCapacity.Code == "RuntimeTypeMismatch" && wrongCapacity.EntityId == "function/size/parameter/items", "runtime sequence input must match the declared capacity");
    var missingRecordField = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(compositeRuntime, "readFirst", [new ModuleRecord("Pair", new Dictionary<string, ModuleValue> { ["first"] = new ModuleI64(1) })]));
    Check(missingRecordField.Code == "RuntimeTypeMismatch" && missingRecordField.EntityId == "function/readFirst/parameter/pair", "runtime record input must contain exactly the declared fields");
    var wrongNestedItem = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(compositeRuntime, "readFirst", [new ModuleRecord("Pair", new Dictionary<string, ModuleValue>
    {
        ["first"] = new ModuleI64(1),
        ["items"] = new ModuleSequence(new TypeRef("I64"), 2, [new ModuleBool(true)])
    })]));
    Check(wrongNestedItem.Code == "RuntimeTypeMismatch" && wrongNestedItem.EntityId == "function/readFirst/parameter/pair/field/items/item/0", "runtime composite validation reports the nested value locus");
    reports.Add(new { kind = "runtime", fixture = "composite-runtime-valid.json", semanticChecks = 12 });
    var importedBranchingText = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(fixtureDir, "if-valid.json")))
        .Replace("\"imports\": []", "\"imports\": [{\"moduleId\":\"external.sample\",\"sourceDigest\":\"0000000000000000000000000000000000000000000000000000000000000000\",\"contractDigest\":\"1111111111111111111111111111111111111111111111111111111111111111\"}]", StringComparison.Ordinal);
    var importedBranching = ModulesCompiler.Compile(ModulesParser.ParseModule(Encoding.UTF8.GetBytes(importedBranchingText)));
    var unsupportedImports = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(importedBranching, "choosePlusOne", [new ModuleBool(true), new ModuleI64(2), new ModuleI64(9)]));
    Check(unsupportedImports.Code == "UnsupportedRuntimeImports", "reference evaluator does not silently ignore an unresolved import closure");

    var safeSelectBytes = File.ReadAllBytes(Path.Combine(fixtureDir, "if-nested-safe.json"));
    var safeSelectIr = ModulesCompiler.Compile(ModulesParser.ParseModule(safeSelectBytes));
    var safeSelectFirst = ModulesReferenceEvaluator.Invoke(safeSelectIr, "select.nested-safe-v1", [new ModuleBool(true), new ModuleI64(long.MaxValue), new ModuleI64(11)]);
    var safeSelectSecond = ModulesReferenceEvaluator.Invoke(safeSelectIr, "select.nested-safe-v1", [new ModuleBool(true), new ModuleI64(2), new ModuleI64(11)]);
    var safeSelectThird = ModulesReferenceEvaluator.Invoke(safeSelectIr, "select.nested-safe-v1", [new ModuleBool(false), new ModuleI64(long.MaxValue), new ModuleI64(11)]);
    Check(safeSelectFirst.Value is ModuleI64 { Value: long.MaxValue }, "nested lowering fixture does not evaluate overflowing inner else branch at I64.MAX");
    Check(safeSelectSecond.Value is ModuleI64 { Value: 3 }, "nested lowering fixture evaluates guarded increment away from I64.MAX");
    Check(safeSelectThird.Value is ModuleI64 { Value: 11 }, "nested lowering fixture reference outcome selects outer else value");
    var safeLowering = ModulesDafnyLowerer.Lower(safeSelectIr);
    if (dafnyOutPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dafnyOutPath) ?? throw new InvalidOperationException("Dafny output path has no directory"));
        File.WriteAllBytes(dafnyOutPath, safeLowering.SourceBytes);
    }
    var safeCalls = ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "call-safe.json"))));
    var callLowering = ModulesDafnyLowerer.Lower(safeCalls);
    if (dafnyCallOutPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dafnyCallOutPath) ?? throw new InvalidOperationException("Dafny call output path has no directory"));
        File.WriteAllBytes(dafnyCallOutPath, callLowering.SourceBytes);
    }
    var unsafeLowering = ModulesDafnyLowerer.Lower(ir);
    if (dafnyUnsafeOutPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dafnyUnsafeOutPath) ?? throw new InvalidOperationException("Dafny unsafe output path has no directory"));
        File.WriteAllBytes(dafnyUnsafeOutPath, unsafeLowering.SourceBytes);
    }
    var scalarLoweringIr = ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "scalar-lowering-safe.json"))));
    var scalarLowering = ModulesDafnyLowerer.Lower(scalarLoweringIr);
    if (dafnyScalarOutPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dafnyScalarOutPath) ?? throw new InvalidOperationException("Dafny scalar output path has no directory"));
        File.WriteAllBytes(dafnyScalarOutPath, scalarLowering.SourceBytes);
    }
    var scalarGeneratedTrue = ModulesReferenceEvaluator.Invoke(scalarLoweringIr, "scalar.safe-v1", [new ModuleBool(false), new ModuleBool(false)]);
    var scalarGeneratedFalse = ModulesReferenceEvaluator.Invoke(scalarLoweringIr, "scalar.safe-v1", [new ModuleBool(true), new ModuleBool(false)]);
    Check(scalarGeneratedTrue.Value is ModuleBool { Value: true } && scalarGeneratedFalse.Value is ModuleBool { Value: false }, "safe scalar lowering fixture has distinct reference outcomes");
    Check(new[] { " - ", " <= ", " == ", "!", " && ", " || " }.All(token => scalarLowering.Source.Contains(token, StringComparison.Ordinal)), "Dafny lowering emits every non-add scalar operator in the safe candidate");
    var repeatedLowering = ModulesDafnyLowerer.Lower(safeSelectIr);
    Check(safeLowering.SourceDigest == repeatedLowering.SourceDigest && safeLowering.SourceBytes.SequenceEqual(repeatedLowering.SourceBytes), "Dafny lowering is deterministic for one typed IR");
    Check(safeLowering.Source.Contains("newtype {:nativeType \"long\"} I64", StringComparison.Ordinal), "Dafny lowering requests native signed 64-bit I64");
    Check(safeLowering.Source.Split("if ", StringSplitOptions.None).Length - 1 == 2, "Dafny lowering preserves two levels of conditional control flow");
    Check(!safeLowering.Source.Contains("select.nested-safe-v1", StringComparison.Ordinal) && !safeLowering.Source.Contains("inner.branch-v1", StringComparison.Ordinal), "source identifiers are not inserted into generated Dafny syntax");
    Check(safeLowering.SourceMap.Any(entry => entry.EntityId == "function/select.nested-safe-v1" && entry.GeneratedName == "F000"), "source map binds function identity to generated symbol");
    var firstParameterMap = safeLowering.SourceMap.Single(entry => entry.EntityId == "function/select.nested-safe-v1/parameter/outer.condition-v1");
    var parameterLine = safeLowering.Source.Split('\n')[firstParameterMap.Line - 1];
    Check(firstParameterMap.GeneratedName == "p000" && parameterLine.Contains("p000: bool", StringComparison.Ordinal), "source map binds a parameter to its exact generated method line");
    var conditionalMap = safeLowering.SourceMap.Single(entry => entry.EntityId == "function/select.nested-safe-v1/body/node/outer.branch-v1/then/node/inner.branch-v1");
    var conditionalLine = safeLowering.Source.Split('\n')[conditionalMap.Line - 1];
    Check(conditionalLine.Contains($"var {conditionalMap.GeneratedName}: I64;", StringComparison.Ordinal), "source map binds conditional node to its exact generated declaration line");
    Check(callLowering.Source.Contains("F000(p000)", StringComparison.Ordinal), "Dafny lowering binds a local call to its deterministic generated symbol");
    Check(!callLowering.Source.Contains("identity.safe-v1", StringComparison.Ordinal) && !callLowering.Source.Contains("called.safe-v1", StringComparison.Ordinal), "local-call identifiers are not inserted into generated Dafny syntax");
    Check(callLowering.SourceMap.Any(entry => entry.EntityId == "function/through.safe-v1/body/node/called.safe-v1" && entry.GeneratedName == "v000"), "source map binds a local call result to its generated variable");
    Check(unsafeLowering.Source.Contains("p000 + v000", StringComparison.Ordinal), "Dafny lowering emits proof obligations for potentially overflowing I64 arithmetic");
    var loweringCopy = safeLowering.SourceBytes;
    loweringCopy[0] ^= 0x01;
    Check(ModulesDafnyLowerer.Lower(safeSelectIr).SourceDigest == safeLowering.SourceDigest, "Dafny source bytes are defensively copied");
    var alternateSafeSelect = Encoding.UTF8.GetString(safeSelectBytes).Replace("\"value\": \"1\"", "\"value\": \"2\"", StringComparison.Ordinal);
    var alternateLowering = ModulesDafnyLowerer.Lower(ModulesCompiler.Compile(ModulesParser.ParseModule(Encoding.UTF8.GetBytes(alternateSafeSelect))));
    Check(alternateLowering.SourceDigest != safeLowering.SourceDigest, "semantic branch mutation changes generated Dafny candidate");
    var compositeLoweringIr = ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "composite-lowering-safe.json"))));
    var compositeLowering = ModulesDafnyLowerer.Lower(compositeLoweringIr);
    if (dafnyCompositeOutPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dafnyCompositeOutPath) ?? throw new InvalidOperationException("Dafny composite output path has no directory"));
        File.WriteAllBytes(dafnyCompositeOutPath, compositeLowering.SourceBytes);
    }
    Check(compositeLowering.Source.Contains("datatype R000 = C000(", StringComparison.Ordinal), "Dafny lowering emits an immutable generated record declaration");
    Check(compositeLowering.Source.Contains("type S000 = s: seq<I64> | |s| <= 2 witness []", StringComparison.Ordinal), "Dafny lowering emits a bounded sequence subset type");
    Check(compositeLowering.Source.Contains("assert |", StringComparison.Ordinal) && compositeLowering.Source.Contains("assert 0 <=", StringComparison.Ordinal), "Dafny lowering emits sequence append and index proof obligations");
    Check(compositeLowering.Source.Contains(" + [", StringComparison.Ordinal) && compositeLowering.Source.Contains(".R000F", StringComparison.Ordinal), "Dafny lowering emits sequence append and generated record field access");
    Check(!compositeLowering.Source.Contains("Summary", StringComparison.Ordinal) && !compositeLowering.Source.Contains("makeSummary", StringComparison.Ordinal), "composite source identifiers are not inserted into generated Dafny syntax");
    Check(compositeLowering.SourceMap.Any(entry => entry.EntityId == "type/Summary" && entry.GeneratedName == "R000"), "source map binds record identity to its generated type symbol");
    Check(compositeLowering.SourceMap.Any(entry => entry.EntityId == "type/Summary/field/count"), "source map binds record field identity to its generated field symbol");
    var repeatedCompositeLowering = ModulesDafnyLowerer.Lower(compositeLoweringIr);
    Check(repeatedCompositeLowering.SourceDigest == compositeLowering.SourceDigest && repeatedCompositeLowering.SourceBytes.SequenceEqual(compositeLowering.SourceBytes), "composite Dafny lowering is deterministic for one typed IR");
    var unsafeCompositeLowering = ModulesDafnyLowerer.Lower(compositeRuntime);
    if (dafnyCompositeUnsafeOutPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dafnyCompositeUnsafeOutPath) ?? throw new InvalidOperationException("Dafny unsafe composite output path has no directory"));
        File.WriteAllBytes(dafnyCompositeUnsafeOutPath, unsafeCompositeLowering.SourceBytes);
    }
    Check(unsafeCompositeLowering.Source.Contains("assert |p000| < 2;", StringComparison.Ordinal), "composite lowering preserves the caller obligation for append capacity");
    Check(unsafeCompositeLowering.Source.Contains("assert 0 <= (p001 as int) < |p000|;", StringComparison.Ordinal), "composite lowering preserves the caller obligation for sequence index bounds");
    var unusedRecordText = Encoding.UTF8.GetString(safeSelectBytes).Replace("\"types\": []", "\"types\": [{\"id\":\"Unused.Record-v1\",\"fields\":[{\"id\":\"value-v1\",\"type\":\"I64\"}]}]", StringComparison.Ordinal);
    var unusedRecordIr = ModulesCompiler.Compile(ModulesParser.ParseModule(Encoding.UTF8.GetBytes(unusedRecordText)));
    var unusedRecordLowering = ModulesDafnyLowerer.Lower(unusedRecordIr);
    Check(unusedRecordLowering.Source.Contains("datatype R000 = C000(", StringComparison.Ordinal) && unusedRecordLowering.SourceMap.Any(entry => entry.EntityId == "type/Unused.Record-v1"), "Dafny lowering preserves an unused composite declaration in generated source and source map");
    var unsupportedImportLowering = CaptureLoweringReject(() => ModulesDafnyLowerer.Lower(importedBranching));
    Check(unsupportedImportLowering.Code == "UnsupportedLoweringImports", "Dafny lowering does not ignore unresolved imports");
    reports.Add(new { kind = "lowering", fixture = "if-nested-safe.json", safeLowering.SourceDigest, sourceMapEntries = safeLowering.SourceMap.Length });
    reports.Add(new { kind = "lowering", fixture = "call-safe.json", callLowering.SourceDigest, sourceMapEntries = callLowering.SourceMap.Length });
    reports.Add(new { kind = "lowering", fixture = "math-add-valid.json", unsafeLowering.SourceDigest, sourceMapEntries = unsafeLowering.SourceMap.Length, expectedVerification = "reject-without-owner-precondition" });
    reports.Add(new { kind = "lowering", fixture = "scalar-lowering-safe.json", scalarLowering.SourceDigest, sourceMapEntries = scalarLowering.SourceMap.Length });
    reports.Add(new { kind = "lowering", fixture = "composite-lowering-safe.json", compositeLowering.SourceDigest, sourceMapEntries = compositeLowering.SourceMap.Length, expectedVerification = "verified-with-sequence-obligations" });
    reports.Add(new { kind = "lowering", fixture = "composite-runtime-valid.json", unsafeCompositeLowering.SourceDigest, sourceMapEntries = unsafeCompositeLowering.SourceMap.Length, expectedVerification = "unproven-without-caller-range-contract" });
    reports.Add(new { kind = "runtime", fixture = "if-lazy-overflow.json", safe = long.MaxValue.ToString(), overflow = overflow.Code, overflow.EntityId, safeSteps = safeMaximum.Steps });

    Check(typeof(ModuleParseResult).GetConstructors().Length == 0, "validated parse result cannot be publicly forged");
    Check(typeof(ModuleIr).GetConstructors().Length == 0, "canonical IR cannot be publicly forged");
    var sourceCopy = composite.CanonicalSource;
    sourceCopy[0] ^= 0x01;
    Check(ModulesCompiler.Compile(composite).SourceDigest == composite.SourceDigest, "canonical source bytes are defensively copied");
    var originalIrDigest = ModulesCodec.IrDigest(compositeIr.CanonicalBytes);
    var irCopy = compositeIr.CanonicalBytes;
    irCopy[0] ^= 0x01;
    Check(ModulesCodec.IrDigest(compositeIr.CanonicalBytes) == originalIrDigest, "canonical IR bytes are defensively copied");

    reports.Add(new { kind = "positive", fixture = "math-add-valid.json", sourceDigest = valid.SourceDigest, irSourceSchema = ir.SourceSchema, functionCount = ir.Functions.Length });
    reports.Add(new { kind = "positive", fixture = "scalar-ops-valid.json", sourceDigest = scalar.SourceDigest, irDigest = ModulesCodec.IrDigest(scalarIr.CanonicalBytes), functionCount = scalarIr.Functions.Length });
    reports.Add(new { kind = "positive", fixture = "composite-valid.json", sourceDigest = composite.SourceDigest, irDigest = ModulesCodec.IrDigest(compositeIr.CanonicalBytes), functionCount = compositeIr.Functions.Length });

    ExpectParserReject("math-invalid-op.json", "UnsupportedOpcode");
    ExpectParserReject("math-invalid-return-mismatch.json", "ReturnTypeMismatch");
    ExpectParserReject("math-invalid-extra-value.json", "SchemaInvalid");
    ExpectParserReject("composite-invalid-record.json", "RecordFieldMismatch");
    ExpectParserReject("composite-invalid-call-cycle.json", "CallCycleDetected");
    ExpectParserReject("composite-invalid-seq-element.json", "TypeMismatch");
    ExpectParserReject("composite-invalid-recursive-type.json", "RecursiveType");
    ExpectParserReject("composite-invalid-numeric-capacity.json", "SchemaInvalid");
    ExpectParserReject("if-invalid-hidden-capture.json", "DanglingNodeArg");
    ExpectParserReject("if-invalid-branch-type.json", "RegionResultTypeMismatch");
    ExpectParserReject("if-invalid-nested-call-cycle.json", "CallCycleDetected");
    var branchingText = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(fixtureDir, "if-valid.json")));
    var branchTypeError = CaptureParserReject(File.ReadAllText(Path.Combine(fixtureDir, "if-invalid-branch-type.json")));
    Check(branchTypeError.EntityId == "function/choose/body/node/chosen/else", "branch result diagnostic identifies the exact region locus");
    var collidingBranchIds = branchingText.Replace("thenSum", "sum", StringComparison.Ordinal).Replace("elseSum", "sum", StringComparison.Ordinal);
    var invalidThen = CaptureParserReject(collidingBranchIds.Replace("\"args\": [\"thenLeft\", \"thenOne\"]", "\"args\": [\"missing\", \"thenOne\"]", StringComparison.Ordinal));
    var invalidElse = CaptureParserReject(collidingBranchIds.Replace("\"args\": [\"elseRight\", \"elseOne\"]", "\"args\": [\"missing\", \"elseOne\"]", StringComparison.Ordinal));
    Check(invalidThen.EntityId == "function/choosePlusOne/body/node/chosen/then/node/sum", "same local node ID is qualified to then region");
    Check(invalidElse.EntityId == "function/choosePlusOne/body/node/chosen/else/node/sum", "same local node ID is qualified to else region");
    Check(invalidThen.EntityId != invalidElse.EntityId, "colliding local node IDs have distinct repair loci");
    var siblingCollision = branchingText.Replace("\"id\": \"chosen\"", "\"id\": \"chosen.thenRegion\"", StringComparison.Ordinal)
        .Replace("\"result\": \"chosen\"", "\"result\": \"chosen.thenRegion\"", StringComparison.Ordinal)
        .Replace("\"args\": [\"condition\", \"left\", \"right\"]", "\"args\": [\"left\", \"left\", \"right\"]", StringComparison.Ordinal);
    var siblingCollisionError = CaptureParserReject(siblingCollision);
    var branchLocusError = CaptureParserReject(branchingText.Replace("\"parameters\": [\"thenLeft\", \"thenRight\"]", "\"parameters\": [\"thenLeft\"]", StringComparison.Ordinal));
    Check(siblingCollisionError.EntityId == "function/choosePlusOne/body/node/chosen.thenRegion", "node IDs containing dots remain one unambiguous path segment");
    Check(branchLocusError.EntityId == "function/choosePlusOne/body/node/chosen/then", "branch region uses a slash-delimited path segment");
    Check(siblingCollisionError.EntityId != branchLocusError.EntityId, "sibling node ID cannot collide with a branch region locus");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(branchingText.Replace("\"args\": [\"condition\", \"left\", \"right\"]", "\"args\": [\"left\", \"left\", \"right\"]", StringComparison.Ordinal)), "if-condition-type", "TypeMismatch");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(branchingText.Replace("\"parameters\": [\"thenLeft\", \"thenRight\"]", "\"parameters\": [\"thenLeft\"]", StringComparison.Ordinal)), "if-environment-arity", "RegionParametersMismatch");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(branchingText.Replace("\"thenRegion\":", "\"unexpectedRegion\":", StringComparison.Ordinal)), "if-missing-region", "SchemaInvalid");
    ExpectParserRejectWithLimitsBytes(Encoding.UTF8.GetBytes(branchingText), "if-nested-node-function-limit", new StrogoLimits { MaxNodesPerFunction = 4 }, "FunctionNodeLimitExceeded");
    ExpectParserRejectWithLimitsBytes(Encoding.UTF8.GetBytes(branchingText), "if-nested-node-module-limit", new StrogoLimits { MaxTotalNodes = 4 }, "ModuleNodeLimitExceeded");
    var nestedIfModule = "{\"schemaVersion\":\"strogo.module.v0.2\",\"moduleId\":\"branch.depth\",\"types\":[],\"imports\":[],\"functions\":[{\"id\":\"choose\",\"parameters\":[{\"id\":\"c\",\"type\":\"Bool\"},{\"id\":\"x\",\"type\":\"I64\"}],\"returnType\":\"I64\",\"contractRef\":\"chooseContract\",\"body\":" + NestedIfRegion(2) + "}],\"exports\":[\"choose\"]}";
    ExpectParserRejectWithLimitsBytes(Encoding.UTF8.GetBytes(nestedIfModule), "if-region-depth", new StrogoLimits { MaxRegionDepth = 1 }, "RegionDepthExceeded");
    ExpectParserRejectWithLimits("composite-invalid-type-depth.json", new StrogoLimits { MaxTypeDepth = 1 }, "TypeDepthExceeded");

    var mathText = Encoding.UTF8.GetString(validBytes);
    ExpectParserReject("math-invalid-i64-plus.json", "InvalidI64");
    ExpectParserReject("math-invalid-i64-leading-zero.json", "InvalidI64");
    ExpectParserReject("math-invalid-i64-negative-zero.json", "InvalidI64");

    ExpectParserRejectBytes("{"u8.ToArray(), "malformed-json", "InvalidJson");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes("{\"schemaVersion\":\"strogo.module.v0.2\",\"schemaVersion\":\"strogo.module.v0.2\"}"), "duplicate-field", "DuplicateField");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(mathText.Replace("math.add", "math.пример", StringComparison.Ordinal)), "non-ascii", "SchemaInvalid");
    ExpectParserRejectBytes([0x7b, 0x22, 0x78, 0x22, 0x3a, 0x22, 0xff, 0x22, 0x7d], "invalid-utf8", "InvalidJson");

    var compositeText = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(fixtureDir, "composite-valid.json")));
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(compositeText.Replace("\"functionRef\": \"addOne\"", "\"functionRef\": \"missing\"", StringComparison.Ordinal)), "call-unknown", "UnknownFunction");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(compositeText.Replace("\"args\": [\"x\"], \"functionRef\": \"addOne\"", "\"args\": [], \"functionRef\": \"addOne\"", StringComparison.Ordinal)), "call-arity", "ArityMismatch");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(compositeText.Replace("\"args\": [\"x\"], \"functionRef\": \"addOne\"", "\"args\": [\"empty\"], \"functionRef\": \"addOne\"", StringComparison.Ordinal)), "call-argument-type", "TypeMismatch");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(compositeText.Replace("\"args\": [\"second\", \"zero\"]", "\"args\": [\"second\", \"empty\"]", StringComparison.Ordinal)), "seq-get-index-type", "TypeMismatch");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(compositeText.Replace("\"functionRef\": \"addOne\"", "\"functionRef\": \"addOne\", \"fieldId\": \"count\"", StringComparison.Ordinal)), "opcode-extra-metadata", "SchemaInvalid");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(compositeText.Replace(", \"functionRef\": \"addOne\"", "", StringComparison.Ordinal)), "opcode-missing-metadata", "SchemaInvalid");

    ExpectParserRejectWithLimits("math-add-valid.json", new StrogoLimits { MaxTotalNodes = 1 }, "ModuleNodeLimitExceeded");
    var importedComposite = compositeText.Replace("\"imports\": []", "\"imports\": [{\"moduleId\":\"dependency\",\"sourceDigest\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"contractDigest\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"}]", StringComparison.Ordinal);
    ExpectParserRejectWithLimitsBytes(Encoding.UTF8.GetBytes(importedComposite), "import-limit", new StrogoLimits { MaxImports = 0 }, "ImportLimitExceeded");
    ExpectParserRejectWithLimitsBytes(validBytes, "json-depth-limit", new StrogoLimits { MaxJsonDepth = 2 }, "InvalidJson");

    const string seqRecordDepth = "{\"schemaVersion\":\"strogo.module.v0.2\",\"moduleId\":\"depth.sample\",\"types\":[{\"id\":\"Inner\",\"fields\":[{\"id\":\"value\",\"type\":\"I64\"}]},{\"id\":\"Outer\",\"fields\":[{\"id\":\"items\",\"type\":{\"kind\":\"Seq\",\"elementType\":\"Inner\",\"capacity\":\"1\"}}]}],\"imports\":[],\"functions\":[],\"exports\":[]}";
    ExpectParserRejectWithLimitsBytes(Encoding.UTF8.GetBytes(seqRecordDepth), "seq-record-depth", new StrogoLimits { MaxTypeDepth = 2 }, "TypeDepthExceeded");

    const string invalidNodeA = "{\"id\":\"a\",\"op\":\"i64.add\",\"type\":\"I64\",\"args\":[]}";
    const string invalidNodeZ = "{\"id\":\"z\",\"op\":\"i64.add\",\"type\":\"I64\",\"args\":[]}";
    string InvalidOrder(string first, string second) => "{\"schemaVersion\":\"strogo.module.v0.2\",\"moduleId\":\"order.sample\",\"types\":[],\"imports\":[],\"functions\":[{\"id\":\"f\",\"parameters\":[],\"returnType\":\"I64\",\"contractRef\":\"c\",\"body\":{\"parameters\":[],\"nodes\":[" + first + "," + second + "],\"result\":\"a\"}}],\"exports\":[\"f\"]}";
    var orderOne = CaptureParserReject(InvalidOrder(invalidNodeZ, invalidNodeA));
    var orderTwo = CaptureParserReject(InvalidOrder(invalidNodeA, invalidNodeZ));
    Check(orderOne.Code == "ArityMismatch" && orderOne.EntityId == "function/f/body/node/a", "diagnostic chooses lowest stable qualified node id");
    Check(orderOne.Code == orderTwo.Code && orderOne.EntityId == orderTwo.EntityId && orderOne.DetailsJson == orderTwo.DetailsJson, "reordered invalid nodes return identical diagnostics");

    string InvalidExports(string exports) => "{\"schemaVersion\":\"strogo.module.v0.2\",\"moduleId\":\"exports.sample\",\"types\":[],\"imports\":[],\"functions\":[],\"exports\":" + exports + "}";
    var exportsOne = CaptureParserReject(InvalidExports("[\"z\",\"a\"]"));
    var exportsTwo = CaptureParserReject(InvalidExports("[\"a\",\"z\"]"));
    Check(exportsOne.Code == "ExportedFunctionMissing" && exportsOne.EntityId == "a" && exportsOne.DetailsJson == exportsTwo.DetailsJson, "reordered invalid exports return identical diagnostics");

    const string validRecordMake = "{ \"id\": \"summary\", \"op\": \"record.make\", \"type\": \"Summary\", \"args\": [\"total\", \"second\"], \"recordType\": \"Summary\", \"fieldIds\": [\"count\", \"values\"] }";
    var recordOrderOne = CaptureParserReject(compositeText.Replace(validRecordMake, "{ \"id\": \"summary\", \"op\": \"record.make\", \"type\": \"Summary\", \"args\": [\"total\", \"second\"], \"recordType\": \"Summary\", \"fieldIds\": [\"z\", \"a\"] }", StringComparison.Ordinal));
    var recordOrderTwo = CaptureParserReject(compositeText.Replace(validRecordMake, "{ \"id\": \"summary\", \"op\": \"record.make\", \"type\": \"Summary\", \"args\": [\"second\", \"total\"], \"recordType\": \"Summary\", \"fieldIds\": [\"a\", \"z\"] }", StringComparison.Ordinal));
    Check(recordOrderOne.Code == "RecordFieldMismatch" && recordOrderOne.EntityId == "function/makeSummary/body/node/summary" && recordOrderOne.DetailsJson == recordOrderTwo.DetailsJson, "reordered invalid record pairs return identical diagnostics");

    var recordTypesOne = CaptureParserReject(compositeText.Replace(validRecordMake, "{ \"id\": \"summary\", \"op\": \"record.make\", \"type\": \"Summary\", \"args\": [\"second\", \"total\"], \"recordType\": \"Summary\", \"fieldIds\": [\"count\", \"values\"] }", StringComparison.Ordinal));
    var recordTypesTwo = CaptureParserReject(compositeText.Replace(validRecordMake, "{ \"id\": \"summary\", \"op\": \"record.make\", \"type\": \"Summary\", \"args\": [\"total\", \"second\"], \"recordType\": \"Summary\", \"fieldIds\": [\"values\", \"count\"] }", StringComparison.Ordinal));
    Check(recordTypesOne.Code == "TypeMismatch" && recordTypesOne.EntityId == "function/makeSummary/body/node/summary" && recordTypesOne.DetailsJson == recordTypesTwo.DetailsJson, "reordered invalid record types return identical diagnostics");

    var raisedHardLimits = new StrogoLimits[]
    {
        new() { MaxTransportBytes = StrogoLimits.TransportBytesHardMaximum + 1 },
        new() { MaxJsonDepth = StrogoLimits.JsonDepthHardMaximum + 1 },
        new() { MaxTypes = StrogoLimits.TypesHardMaximum + 1 },
        new() { MaxImports = StrogoLimits.ImportsHardMaximum + 1 },
        new() { MaxFunctions = StrogoLimits.FunctionsHardMaximum + 1 },
        new() { MaxNodesPerFunction = StrogoLimits.NodesPerFunctionHardMaximum + 1 },
        new() { MaxTotalNodes = StrogoLimits.TotalNodesHardMaximum + 1 },
        new() { MaxTypeDepth = StrogoLimits.TypeDepthHardMaximum + 1 },
        new() { MaxRegionDepth = StrogoLimits.RegionDepthHardMaximum + 1 }
    };
    foreach (var raisedLimits in raisedHardLimits)
        ExpectParserRejectWithLimits("math-add-valid.json", raisedLimits, "InvalidLimits");

    var scalarText = Encoding.UTF8.GetString(scalarBytes);
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(scalarText.Replace("\"id\": \"truth\", \"op\": \"bool.const\", \"type\": \"Bool\"", "\"id\": \"truth\", \"op\": \"bool.const\", \"type\": \"I64\"", StringComparison.Ordinal)), "bool-const-result-type", "TypeMismatch");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(scalarText.Replace("\"id\": \"difference\", \"op\": \"i64.sub\", \"type\": \"I64\", \"args\": [\"x\", \"one\"]", "\"id\": \"difference\", \"op\": \"i64.sub\", \"type\": \"I64\", \"args\": [\"x\", \"truth\"]", StringComparison.Ordinal)), "i64-sub-argument-type", "TypeMismatch");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(scalarText.Replace("\"id\": \"within\", \"op\": \"i64.le\", \"type\": \"Bool\", \"args\": [\"difference\", \"x\"]", "\"id\": \"within\", \"op\": \"i64.le\", \"type\": \"Bool\", \"args\": [\"difference\"]", StringComparison.Ordinal)), "i64-le-arity", "ArityMismatch");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(scalarText.Replace("\"id\": \"same\", \"op\": \"i64.eq\", \"type\": \"Bool\"", "\"id\": \"same\", \"op\": \"i64.eq\", \"type\": \"I64\"", StringComparison.Ordinal)), "i64-eq-result-type", "TypeMismatch");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(scalarText.Replace("\"id\": \"falsehood\", \"op\": \"bool.not\", \"type\": \"Bool\", \"args\": [\"truth\"]", "\"id\": \"falsehood\", \"op\": \"bool.not\", \"type\": \"Bool\", \"args\": []", StringComparison.Ordinal)), "bool-not-arity", "ArityMismatch");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(scalarText.Replace("\"id\": \"both\", \"op\": \"bool.and\", \"type\": \"Bool\", \"args\": [\"within\", \"same\"]", "\"id\": \"both\", \"op\": \"bool.and\", \"type\": \"Bool\", \"args\": [\"within\", \"x\"]", StringComparison.Ordinal)), "bool-and-argument-type", "TypeMismatch");
    ExpectParserRejectBytes(Encoding.UTF8.GetBytes(scalarText.Replace("\"id\": \"result\", \"op\": \"bool.or\", \"type\": \"Bool\", \"args\": [\"both\", \"falsehood\"]", "\"id\": \"result\", \"op\": \"bool.or\", \"type\": \"Bool\", \"args\": [\"both\"]", StringComparison.Ordinal)), "bool-or-arity", "ArityMismatch");

    await File.WriteAllBytesAsync(reportPath, JsonSerializer.SerializeToUtf8Bytes(new
    {
        passed = true,
        checks,
        report = new { timestampUtc = DateTime.UtcNow, checks, fixtures = reports.Count },
        results = reports
    }));

    Console.WriteLine($"PASS conformance checks={checks}");
    return 0;
}
catch (Exception ex)
{
    await File.WriteAllBytesAsync(reportPath, JsonSerializer.SerializeToUtf8Bytes(new
    {
        passed = false,
        checks,
        error = ex.ToString(),
        report = reports
    }));

    Console.Error.WriteLine(ex);
    return 1;
}
