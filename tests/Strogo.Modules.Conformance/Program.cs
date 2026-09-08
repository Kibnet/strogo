using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
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
var dafnyOwnerCompositeOutOption = Array.IndexOf(args, "--dafny-owner-composite-out");
if (dafnyOwnerCompositeOutOption >= 0 && dafnyOwnerCompositeOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-owner-composite-out requires a path");
var dafnyOwnerCompositeAlternativeOutOption = Array.IndexOf(args, "--dafny-owner-composite-alternative-out");
if (dafnyOwnerCompositeAlternativeOutOption >= 0 && dafnyOwnerCompositeAlternativeOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-owner-composite-alternative-out requires a path");
var dafnyOwnerCompositeWrongOutOption = Array.IndexOf(args, "--dafny-owner-composite-wrong-out");
if (dafnyOwnerCompositeWrongOutOption >= 0 && dafnyOwnerCompositeWrongOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-owner-composite-wrong-out requires a path");
var dafnyOwnerCompositePartialOutOption = Array.IndexOf(args, "--dafny-owner-composite-partial-out");
if (dafnyOwnerCompositePartialOutOption >= 0 && dafnyOwnerCompositePartialOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-owner-composite-partial-out requires a path");
var dafnyOwnerStrictAndOutOption = Array.IndexOf(args, "--dafny-owner-strict-and-out");
if (dafnyOwnerStrictAndOutOption >= 0 && dafnyOwnerStrictAndOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-owner-strict-and-out requires a path");
var dafnyOwnerStrictOrOutOption = Array.IndexOf(args, "--dafny-owner-strict-or-out");
if (dafnyOwnerStrictOrOutOption >= 0 && dafnyOwnerStrictOrOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-owner-strict-or-out requires a path");
var dafnyOwnerGuardedFalseOutOption = Array.IndexOf(args, "--dafny-owner-guarded-false-out");
if (dafnyOwnerGuardedFalseOutOption >= 0 && dafnyOwnerGuardedFalseOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-owner-guarded-false-out requires a path");
var dafnyOwnerGuardedTrueOutOption = Array.IndexOf(args, "--dafny-owner-guarded-true-out");
if (dafnyOwnerGuardedTrueOutOption >= 0 && dafnyOwnerGuardedTrueOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-owner-guarded-true-out requires a path");
var dafnyOwnerAppendPartialOutOption = Array.IndexOf(args, "--dafny-owner-append-partial-out");
if (dafnyOwnerAppendPartialOutOption >= 0 && dafnyOwnerAppendPartialOutOption + 1 >= args.Length)
    throw new ArgumentException("--dafny-owner-append-partial-out requires a path");
var foldOutputOption = Array.IndexOf(args, "--fold-output");
if (foldOutputOption >= 0 && foldOutputOption + 1 >= args.Length)
    throw new ArgumentException("--fold-output requires a path");

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
var dafnyOwnerCompositeOutPath = dafnyOwnerCompositeOutOption >= 0 ? Path.GetFullPath(args[dafnyOwnerCompositeOutOption + 1], root) : null;
var dafnyOwnerCompositeAlternativeOutPath = dafnyOwnerCompositeAlternativeOutOption >= 0 ? Path.GetFullPath(args[dafnyOwnerCompositeAlternativeOutOption + 1], root) : null;
var dafnyOwnerCompositeWrongOutPath = dafnyOwnerCompositeWrongOutOption >= 0 ? Path.GetFullPath(args[dafnyOwnerCompositeWrongOutOption + 1], root) : null;
var dafnyOwnerCompositePartialOutPath = dafnyOwnerCompositePartialOutOption >= 0 ? Path.GetFullPath(args[dafnyOwnerCompositePartialOutOption + 1], root) : null;
var dafnyOwnerStrictAndOutPath = dafnyOwnerStrictAndOutOption >= 0 ? Path.GetFullPath(args[dafnyOwnerStrictAndOutOption + 1], root) : null;
var dafnyOwnerStrictOrOutPath = dafnyOwnerStrictOrOutOption >= 0 ? Path.GetFullPath(args[dafnyOwnerStrictOrOutOption + 1], root) : null;
var dafnyOwnerGuardedFalseOutPath = dafnyOwnerGuardedFalseOutOption >= 0 ? Path.GetFullPath(args[dafnyOwnerGuardedFalseOutOption + 1], root) : null;
var dafnyOwnerGuardedTrueOutPath = dafnyOwnerGuardedTrueOutOption >= 0 ? Path.GetFullPath(args[dafnyOwnerGuardedTrueOutOption + 1], root) : null;
var dafnyOwnerAppendPartialOutPath = dafnyOwnerAppendPartialOutOption >= 0 ? Path.GetFullPath(args[dafnyOwnerAppendPartialOutOption + 1], root) : null;
var foldOutputPath = foldOutputOption >= 0
    ? Path.GetFullPath(args[foldOutputOption + 1], root)
    : Path.Combine(root, "artifacts", "local-validation", "e05", "fold-discriminator");
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
    Check(owner.SchemaVersion == "strogo.owner-bundle.v0.3", "owner bundle schemaVersion");
    var migratedOwnerBytes = OwnerBundleMigrator.MigrateV02ToV03(File.ReadAllBytes(Path.Combine(fixtureDir, "owner-add-one-valid-v0.2.json")));
    Check(migratedOwnerBytes.SequenceEqual(owner.CanonicalBytes), "v0.2 exact scalar owner migrates to the canonical v0.3 artifact");
    Check(owner.BundleDigest == "b0153dbe71b173c266d4786de5d8e60cc1d913b838f2f94f0191129681a5fbe6", "migrated scalar owner matches the checked-in v0.3 golden digest");
    var oldOwnerDirect = CaptureOwnerReject(() => OwnerBundleParser.Parse(File.ReadAllBytes(Path.Combine(fixtureDir, "owner-add-one-valid-v0.2.json"))));
    Check(oldOwnerDirect.Code == "OwnerBundleMigrationRequired", "v0.2 owner requires explicit migration");
    var nonExactLegacyNode = JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixtureDir, "owner-add-one-valid-v0.2.json")))!.AsObject();
    var legacyEnsuresArgs = nonExactLegacyNode["entryContracts"]![0]!["ensures"]!["args"]!.AsArray();
    var legacyResult = legacyEnsuresArgs[0]!.DeepClone();
    legacyEnsuresArgs[0] = legacyEnsuresArgs[1]!.DeepClone();
    legacyEnsuresArgs[1] = legacyResult;
    Check(CaptureOwnerReject(() => OwnerBundleMigrator.MigrateV02ToV03(Encoding.UTF8.GetBytes(nonExactLegacyNode.ToJsonString()))).Code == "ExactOutcomeRequired", "migration rejects a non-exact legacy ensures shape without output");
    var malformedLegacyNode = JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixtureDir, "owner-add-one-valid-v0.2.json")))!.AsObject();
    malformedLegacyNode["entryContracts"]![0]!.AsObject().Remove("witnesses");
    Check(CaptureOwnerReject(() => OwnerBundleMigrator.MigrateV02ToV03(Encoding.UTF8.GetBytes(malformedLegacyNode.ToJsonString()))).Code == "SchemaInvalid", "migration turns malformed legacy structure into a typed refusal");
    var arithmeticLegacyNode = JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixtureDir, "owner-add-one-valid-v0.2.json")))!.AsObject();
    arithmeticLegacyNode["entryContracts"]![0]!["requires"] = JsonNode.Parse("{\"op\":\"eq\",\"type\":\"Bool\",\"args\":[{\"op\":\"i64.add\",\"type\":\"I64\",\"args\":[{\"op\":\"param\",\"type\":\"I64\",\"id\":\"x\"},{\"op\":\"i64.const\",\"type\":\"I64\",\"value\":\"1\"}]},{\"op\":\"i64.const\",\"type\":\"I64\",\"value\":\"1\"}]}");
    Check(CaptureOwnerReject(() => OwnerBundleMigrator.MigrateV02ToV03(Encoding.UTF8.GetBytes(arithmeticLegacyNode.ToJsonString()))).Code == "ArithmeticInRequiresNotSupported", "migration preserves the strict v0.2 arithmetic-in-requires restriction");
    var undercountedLegacyNode = JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixtureDir, "owner-add-one-valid-v0.2.json")))!.AsObject();
    undercountedLegacyNode["limits"]!["maxExpressionNodes"] = "4";
    Check(CaptureOwnerReject(() => OwnerBundleMigrator.MigrateV02ToV03(Encoding.UTF8.GetBytes(undercountedLegacyNode.ToJsonString()))).Code == "ExpressionNodeLimitExceeded", "migration counts the removed legacy ensures against the v0.2 expression budget");

    string LargeLegacyOwner(bool exceedValueLimit)
    {
        static (JsonObject Entry, JsonObject Model) Pair(string suffix, int parameterCount, int witnessCount)
        {
            var parameters = new JsonArray();
            var modelParameters = new JsonArray();
            var callArguments = new JsonArray();
            for (var parameterIndex = 0; parameterIndex < parameterCount; parameterIndex++)
            {
                var parameterId = $"p{parameterIndex:D3}";
                parameters.Add(new JsonObject { ["id"] = parameterId, ["type"] = "I64" });
                modelParameters.Add(new JsonObject { ["id"] = parameterId, ["type"] = "I64" });
                callArguments.Add(new JsonObject { ["op"] = "param", ["type"] = "I64", ["id"] = parameterId });
            }
            var witnesses = new JsonArray();
            for (var witnessIndex = 0; witnessIndex < witnessCount; witnessIndex++)
            {
                var arguments = new JsonArray();
                for (var parameterIndex = 0; parameterIndex < parameterCount; parameterIndex++)
                    arguments.Add(new JsonObject
                    {
                        ["parameterId"] = $"p{parameterIndex:D3}",
                        ["value"] = new JsonObject { ["type"] = "I64", ["value"] = "0" }
                    });
                witnesses.Add(new JsonObject { ["id"] = $"w{witnessIndex:D3}", ["arguments"] = arguments });
            }
            var modelId = $"large.model{suffix}";
            var entry = new JsonObject
            {
                ["id"] = $"largeContract{suffix}",
                ["functionRef"] = $"largeFunction{suffix}",
                ["parameters"] = parameters,
                ["returnType"] = "I64",
                ["requires"] = new JsonObject { ["op"] = "bool.const", ["type"] = "Bool", ["value"] = true },
                ["ensures"] = new JsonObject
                {
                    ["op"] = "eq", ["type"] = "Bool",
                    ["args"] = new JsonArray(
                        new JsonObject { ["op"] = "result", ["type"] = "I64" },
                        new JsonObject { ["op"] = "model.call", ["type"] = "I64", ["modelRef"] = modelId, ["args"] = callArguments })
                },
                ["effects"] = new JsonArray(),
                ["witnesses"] = witnesses
            };
            var model = new JsonObject
            {
                ["id"] = modelId,
                ["parameters"] = modelParameters,
                ["returnType"] = "I64",
                ["body"] = new JsonObject { ["op"] = "param", ["type"] = "I64", ["id"] = "p000" }
            };
            return (entry, model);
        }

        var mainPair = Pair("A", 512, 8);
        var entries = new JsonArray(mainPair.Entry);
        var models = new JsonArray(mainPair.Model);
        if (exceedValueLimit)
        {
            var extraPair = Pair("B", 1, 1);
            entries.Add(extraPair.Entry);
            models.Add(extraPair.Model);
        }
        return new JsonObject
        {
            ["schemaVersion"] = "strogo.owner-bundle.v0.2",
            ["bundleId"] = exceedValueLimit ? "migration.values4097" : "migration.values4096",
            ["entryContracts"] = entries,
            ["models"] = models,
            ["limits"] = new JsonObject
            {
                ["maxExpressionNodes"] = "1024",
                ["maxExpressionDepth"] = "32",
                ["maxWitnessesPerEntry"] = "8"
            }
        }.ToJsonString();
    }
    var migrated4096 = OwnerBundleParser.Parse(OwnerBundleMigrator.MigrateV02ToV03(Encoding.UTF8.GetBytes(LargeLegacyOwner(exceedValueLimit: false))));
    Check(migrated4096.Limits.MaxWitnessValueNodes == 4096 && migrated4096.EntryContracts[0].Witnesses.Length == 8, "migration accepts exactly 4096 scalar witness value nodes and adds the fixed limit");
    var migration4097 = CaptureOwnerReject(() => OwnerBundleMigrator.MigrateV02ToV03(Encoding.UTF8.GetBytes(LargeLegacyOwner(exceedValueLimit: true))));
    Check(migration4097.Code == "MigrationWitnessValueLimitExceeded", "migration rejects 4097 witness value nodes before producing output");
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
    Check(ownerBinding.Entries[0].Witnesses[0].ModelResult is ModuleI64 { Value: 1 }, "owner witness is independently evaluated through the model");
    var forgedEmptyBindingReplay = OwnerContractReplay.Replay(new OwnerContractBinding(ir, owner, ImmutableArray<BoundOwnerEntry>.Empty));
    Check(forgedEmptyBindingReplay is { Status: "Pass", CheckedWitnesses: 1, Counterexample: null, FailureCode: null }, "replay rebuilds a trusted binding and cannot skip owner witnesses through a forged empty entry set");
    var invalidLimitReplay = OwnerContractReplay.Replay(ownerBinding, new ModuleEvaluationLimits { MaxSteps = 0 });
    Check(invalidLimitReplay is { Status: "ToolError", Counterexample: null, FailureCode: "InvalidEvaluationLimits" }, "invalid replay limits are an operational ToolError, never a counterexample");
    var exhaustedReplay = OwnerContractReplay.Replay(ownerBinding, new ModuleEvaluationLimits { MaxSteps = 1 });
    Check(exhaustedReplay is { Status: "Timeout", Counterexample: null, FailureCode: "EvaluationStepLimitExceeded" }, "replay fuel exhaustion is Timeout, never a counterexample");
    var overflowReplayNode = JsonNode.Parse(ownerBytes)!.AsObject();
    overflowReplayNode["entryContracts"]![0]!["requires"] = JsonNode.Parse("{\"op\":\"bool.const\",\"type\":\"Bool\",\"value\":true}");
    overflowReplayNode["entryContracts"]![0]!["witnesses"]![0]!["arguments"]![0]!["value"]!["value"] = long.MaxValue.ToString(CultureInfo.InvariantCulture);
    overflowReplayNode["models"]![0]!["body"] = JsonNode.Parse("{\"op\":\"param\",\"type\":\"I64\",\"id\":\"x\"}");
    var overflowReplay = OwnerContractReplay.Replay(OwnerContractBinder.Bind(ir, OwnerBundleParser.Parse(overflowReplayNode.ToJsonString())));
    Check(overflowReplay is { Status: "CandidateError", Counterexample: null, FailureCode: "ArithmeticOverflow" }, "candidate runtime failure at a valid owner witness is typed separately from a value counterexample");
    var unsupportedInstruction = ir.Functions[0].Instructions[^1] with { Op = "unsupported.toolchain-fixture" };
    var internallyInvalidFunction = ir.Functions[0] with { Instructions = ir.Functions[0].Instructions.SetItem(ir.Functions[0].Instructions.Length - 1, unsupportedInstruction) };
    var moduleIrConstructor = typeof(ModuleIr).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
    var internallyInvalidIr = (ModuleIr)moduleIrConstructor.Invoke([
        ir.SourceSchema,
        ir.ModuleId,
        ir.SourceDigest,
        ir.Types,
        ir.Imports,
        ImmutableArray.Create(internallyInvalidFunction),
        ir.Exports,
        ir.CanonicalBytes
    ]);
    var internalFailureReplay = OwnerContractReplay.Replay(new OwnerContractBinding(internallyInvalidIr, owner, ImmutableArray<BoundOwnerEntry>.Empty));
    Check(internalFailureReplay is { Status: "ToolError", Counterexample: null, FailureCode: "UnsupportedRuntimeOpcode" }, "unknown or internal evaluator failures are ToolError rather than candidate blame");
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
    var forbiddenEnsuresNode = JsonNode.Parse(ownerText)!.AsObject();
    forbiddenEnsuresNode["entryContracts"]![0]!["ensures"] = JsonNode.Parse("{\"op\":\"bool.const\",\"type\":\"Bool\",\"value\":true}");
    var forbiddenEnsures = CaptureOwnerReject(() => OwnerBundleParser.Parse(forbiddenEnsuresNode.ToJsonString()));
    Check(forbiddenEnsures.Code == "SchemaInvalid", "v0.3 owner schema has no free ensures field");
    var unknownModelNode = JsonNode.Parse(ownerText)!.AsObject();
    unknownModelNode["entryContracts"]![0]!["modelRef"] = "missing.model";
    var unknownModel = CaptureOwnerReject(() => OwnerBundleParser.Parse(unknownModelNode.ToJsonString()));
    Check(unknownModel.Code == "UnknownOwnerModel", "v0.3 exact outcome uses one explicit modelRef");
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
    var arithmeticRequires = OwnerBundleParser.Parse(arithmeticRequiresNode.ToJsonString());
    Check(arithmeticRequires.EntryContracts[0].Requires.Op == "eq", "v0.3 requires accepts the closed executable expression set");
    var booleanArithmeticModelNode = JsonNode.Parse(ownerText)!.AsObject();
    booleanArithmeticModelNode["entryContracts"]![0]!["returnType"] = "Bool";
    booleanArithmeticModelNode["entryContracts"]![0]!["requires"] = JsonNode.Parse("{\"op\":\"bool.const\",\"type\":\"Bool\",\"value\":true}");
    booleanArithmeticModelNode["entryContracts"]![0]!["witnesses"]![0]!["arguments"]![0]!["value"]!["value"] = long.MaxValue.ToString(CultureInfo.InvariantCulture);
    booleanArithmeticModelNode["models"]![0]!["returnType"] = "Bool";
    booleanArithmeticModelNode["models"]![0]!["body"] = JsonNode.Parse("{\"op\":\"bool.or\",\"type\":\"Bool\",\"args\":[{\"op\":\"bool.const\",\"type\":\"Bool\",\"value\":true},{\"op\":\"eq\",\"type\":\"Bool\",\"args\":[{\"op\":\"i64.add\",\"type\":\"I64\",\"args\":[{\"op\":\"param\",\"type\":\"I64\",\"id\":\"x\"},{\"op\":\"i64.const\",\"type\":\"I64\",\"value\":\"1\"}]},{\"op\":\"i64.const\",\"type\":\"I64\",\"value\":\"0\"}]}]}");
    var booleanArithmeticModel = CaptureOwnerReject(() => OwnerBundleParser.Parse(booleanArithmeticModelNode.ToJsonString()));
    Check(booleanArithmeticModel.Code == "ModelUndefinedAtWitness", "strict owner bool operations cannot hide undefined I64 arithmetic");
    var partialEquality = JsonNode.Parse("{\"op\":\"eq\",\"type\":\"Bool\",\"args\":[{\"op\":\"i64.add\",\"type\":\"I64\",\"args\":[{\"op\":\"param\",\"type\":\"I64\",\"id\":\"x\"},{\"op\":\"i64.const\",\"type\":\"I64\",\"value\":\"1\"}]},{\"op\":\"i64.const\",\"type\":\"I64\",\"value\":\"0\"}]}")!;
    JsonObject BooleanOwner(JsonNode body, long witnessValue)
    {
        var node = JsonNode.Parse(ownerText)!.AsObject();
        node["entryContracts"]![0]!["returnType"] = "Bool";
        node["entryContracts"]![0]!["requires"] = JsonNode.Parse("{\"op\":\"bool.const\",\"type\":\"Bool\",\"value\":true}");
        node["entryContracts"]![0]!["witnesses"]![0]!["arguments"]![0]!["value"]!["value"] = witnessValue.ToString();
        node["models"]![0]!["returnType"] = "Bool";
        node["models"]![0]!["body"] = body.DeepClone();
        return node;
    }
    ModuleIr BooleanCandidate(bool value)
    {
        var node = JsonNode.Parse(validBytes)!.AsObject();
        node["moduleId"] = value ? "strict.bool.true" : "strict.bool.false";
        node["functions"]![0]!["returnType"] = "Bool";
        node["functions"]![0]!["body"]!["nodes"] = new JsonArray(new JsonObject
        {
            ["id"] = "constant", ["op"] = "bool.const", ["type"] = "Bool", ["args"] = new JsonArray(), ["value"] = value
        });
        node["functions"]![0]!["body"]!["result"] = "constant";
        return ModulesCompiler.Compile(ModulesParser.ParseModule(node.ToJsonString()));
    }
    var strictAndBody = new JsonObject { ["op"] = "bool.and", ["type"] = "Bool", ["args"] = new JsonArray(new JsonObject { ["op"] = "bool.const", ["type"] = "Bool", ["value"] = false }, partialEquality.DeepClone()) };
    var strictOrBody = new JsonObject { ["op"] = "bool.or", ["type"] = "Bool", ["args"] = new JsonArray(new JsonObject { ["op"] = "bool.const", ["type"] = "Bool", ["value"] = true }, partialEquality.DeepClone()) };
    Check(CaptureOwnerReject(() => OwnerBundleParser.Parse(BooleanOwner(strictAndBody, long.MaxValue).ToJsonString())).Code == "ModelUndefinedAtWitness", "false and partial remains partial in the independent owner evaluator");
    Check(CaptureOwnerReject(() => OwnerBundleParser.Parse(BooleanOwner(strictOrBody, long.MaxValue).ToJsonString())).Code == "ModelUndefinedAtWitness", "true or partial remains partial in the independent owner evaluator");
    var guardedFalseBody = new JsonObject { ["op"] = "if", ["type"] = "Bool", ["args"] = new JsonArray(new JsonObject { ["op"] = "bool.const", ["type"] = "Bool", ["value"] = false }, partialEquality.DeepClone(), new JsonObject { ["op"] = "bool.const", ["type"] = "Bool", ["value"] = false }) };
    var guardedTrueBody = new JsonObject { ["op"] = "if", ["type"] = "Bool", ["args"] = new JsonArray(new JsonObject { ["op"] = "bool.const", ["type"] = "Bool", ["value"] = true }, new JsonObject { ["op"] = "bool.const", ["type"] = "Bool", ["value"] = true }, partialEquality.DeepClone()) };
    _ = OwnerBundleParser.Parse(BooleanOwner(guardedFalseBody, long.MaxValue).ToJsonString());
    _ = OwnerBundleParser.Parse(BooleanOwner(guardedTrueBody, long.MaxValue).ToJsonString());
    Check(true, "lazy owner if does not evaluate either partial unselected branch");
    var falseCandidate = BooleanCandidate(false);
    var trueCandidate = BooleanCandidate(true);
    var strictAndLowering = ModulesDafnyLowerer.Lower(falseCandidate, OwnerBundleParser.Parse(BooleanOwner(strictAndBody, 0).ToJsonString()));
    var strictOrLowering = ModulesDafnyLowerer.Lower(trueCandidate, OwnerBundleParser.Parse(BooleanOwner(strictOrBody, 0).ToJsonString()));
    var guardedFalseLowering = ModulesDafnyLowerer.Lower(falseCandidate, OwnerBundleParser.Parse(BooleanOwner(guardedFalseBody, long.MaxValue).ToJsonString()));
    var guardedTrueLowering = ModulesDafnyLowerer.Lower(trueCandidate, OwnerBundleParser.Parse(BooleanOwner(guardedTrueBody, long.MaxValue).ToJsonString()));
    foreach (var output in new[]
    {
        (dafnyOwnerStrictAndOutPath, strictAndLowering.SourceBytes),
        (dafnyOwnerStrictOrOutPath, strictOrLowering.SourceBytes),
        (dafnyOwnerGuardedFalseOutPath, guardedFalseLowering.SourceBytes),
        (dafnyOwnerGuardedTrueOutPath, guardedTrueLowering.SourceBytes)
    })
    {
        if (output.Item1 is null) continue;
        Directory.CreateDirectory(Path.GetDirectoryName(output.Item1) ?? throw new InvalidOperationException("Dafny strict-definedness output path has no directory"));
        File.WriteAllBytes(output.Item1, output.Item2);
    }
    var strictRequiresNode = JsonNode.Parse(ownerText)!.AsObject();
    var originalRequires = strictRequiresNode["entryContracts"]![0]!["requires"]!.DeepClone();
    strictRequiresNode["entryContracts"]![0]!["requires"] = new JsonObject
    {
        ["op"] = "bool.and", ["type"] = "Bool",
        ["args"] = new JsonArray(originalRequires, new JsonObject { ["op"] = "bool.const", ["type"] = "Bool", ["value"] = true })
    };
    var strictRequiresOwner = OwnerBundleParser.Parse(strictRequiresNode.ToJsonString());
    var strictRequiresLowering = ModulesDafnyLowerer.Lower(ir, strictRequiresOwner);
    Check(strictRequiresLowering.Source.Contains("requires StrictAnd(", StringComparison.Ordinal), "Dafny owner lowering routes strict bool.and through a generated helper call");

    foreach (var forbiddenOwnerOp in new[] { "result", "model.call", "call" })
    {
        var forbiddenOpNode = JsonNode.Parse(ownerText)!.AsObject();
        forbiddenOpNode["models"]![0]!["body"] = new JsonObject { ["op"] = forbiddenOwnerOp, ["type"] = "I64" };
        Check(CaptureOwnerReject(() => OwnerBundleParser.Parse(forbiddenOpNode.ToJsonString())).Code == "UnsupportedContractOpcode", $"owner v0.3 rejects forbidden expression op {forbiddenOwnerOp}");
        var nestedForbiddenOpNode = JsonNode.Parse(ownerText)!.AsObject();
        nestedForbiddenOpNode["models"]![0]!["body"]!["args"]![0] = new JsonObject { ["op"] = forbiddenOwnerOp, ["type"] = "I64" };
        Check(CaptureOwnerReject(() => OwnerBundleParser.Parse(nestedForbiddenOpNode.ToJsonString())).Code == "UnsupportedContractOpcode", $"owner v0.3 rejects nested forbidden expression op {forbiddenOwnerOp}");
        var requiresForbiddenOpNode = JsonNode.Parse(ownerText)!.AsObject();
        requiresForbiddenOpNode["entryContracts"]![0]!["requires"]!["args"]![0] = new JsonObject { ["op"] = forbiddenOwnerOp, ["type"] = "I64" };
        Check(CaptureOwnerReject(() => OwnerBundleParser.Parse(requiresForbiddenOpNode.ToJsonString())).Code == "UnsupportedContractOpcode", $"owner v0.3 rejects forbidden requires op {forbiddenOwnerOp}");
    }
    var forbiddenReferenceFieldNode = JsonNode.Parse(ownerText)!.AsObject();
    forbiddenReferenceFieldNode["models"]![0]!["body"]!["args"]![0]!["functionRef"] = "helper";
    Check(CaptureOwnerReject(() => OwnerBundleParser.Parse(forbiddenReferenceFieldNode.ToJsonString())).Code == "SchemaInvalid", "owner v0.3 rejects helper reference metadata in nested expression positions");
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

    var compositeOwnerBytes = File.ReadAllBytes(Path.Combine(fixtureDir, "owner-composite-valid.json"));
    var compositeOwner = OwnerBundleParser.Parse(compositeOwnerBytes);
    Check(compositeOwner.Types.Length == 1 && compositeOwner.Types[0].Id == "Summary", "owner v0.3 parses its exact composite type closure");
    Check(compositeOwner.EntryContracts[0].Witnesses[0].Arguments[0].Value is ModuleI64 { Value: 0 }, "owner v0.3 stores witnesses in the shared immutable value algebra");
    var compositeCandidateIr = ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "owner-composite-module.json"))));
    var compositeBinding = OwnerContractBinder.Bind(compositeCandidateIr, compositeOwner);
    var compositeModelResult = compositeBinding.Entries[0].Witnesses[0].ModelResult;
    var compositeCandidateResult = ModulesReferenceEvaluator.Invoke(compositeCandidateIr, "makeSummary", [new ModuleI64(0)]).Value;
    Check(compositeModelResult is ModuleRecord && OwnerContractEvaluator.StructuralEquals(compositeModelResult, compositeCandidateResult), "composite owner model and candidate agree structurally on the owner witness");
    var compositeReplay = OwnerContractReplay.Replay(compositeBinding);
    Check(compositeReplay.Status == "Pass" && compositeReplay.CheckedWitnesses == 1 && compositeReplay.Counterexample is null, "matching composite candidate passes independent owner-witness replay");
    var compositeOwnerLowering = ModulesDafnyLowerer.Lower(compositeCandidateIr, compositeOwner);
    Check(compositeOwnerLowering.Source.Contains("function M000", StringComparison.Ordinal)
        && compositeOwnerLowering.Source.Contains("C000(2", StringComparison.Ordinal)
        && compositeOwnerLowering.Source.Contains("var e000: S000 := ([] + [p000])", StringComparison.Ordinal)
        && compositeOwnerLowering.Source.Contains("var e001: I64 := (p000 + 1)", StringComparison.Ordinal),
        "owner composite model lowers through the module type-symbol table");
    Check(compositeOwnerLowering.Source.Contains("ensures result == M000(p000)", StringComparison.Ordinal), "composite candidate receives the automatic exact model postcondition");
    if (dafnyOwnerCompositeOutPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dafnyOwnerCompositeOutPath) ?? throw new InvalidOperationException("Dafny composite owner output path has no directory"));
        File.WriteAllBytes(dafnyOwnerCompositeOutPath, compositeOwnerLowering.SourceBytes);
    }
    var alternativeCompositeIr = ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "owner-composite-alternative.json"))));
    var alternativeCompositeBinding = OwnerContractBinder.Bind(alternativeCompositeIr, compositeOwner);
    Check(OwnerContractReplay.Replay(alternativeCompositeBinding).Status == "Pass", "structurally different composite candidate passes the same owner witness replay");
    var alternativeCompositeLowering = ModulesDafnyLowerer.Lower(alternativeCompositeIr, compositeOwner);
    if (dafnyOwnerCompositeAlternativeOutPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dafnyOwnerCompositeAlternativeOutPath) ?? throw new InvalidOperationException("Dafny alternative composite owner output path has no directory"));
        File.WriteAllBytes(dafnyOwnerCompositeAlternativeOutPath, alternativeCompositeLowering.SourceBytes);
    }
    var wrongCompositeIr = ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "owner-composite-wrong.json"))));
    var wrongCompositeBinding = OwnerContractBinder.Bind(wrongCompositeIr, compositeOwner);
    var wrongCompositeReplay = OwnerContractReplay.Replay(wrongCompositeBinding);
    Check(wrongCompositeReplay.Status == "Counterexample" && wrongCompositeReplay.Counterexample is { WitnessId: "empty-shape-v1", Actual: not null } && wrongCompositeReplay.FailureCode is null, "wrong composite candidate produces a replayed owner-witness counterexample");
    var wrongCompositeLowering = ModulesDafnyLowerer.Lower(wrongCompositeIr, compositeOwner);
    if (dafnyOwnerCompositeWrongOutPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dafnyOwnerCompositeWrongOutPath) ?? throw new InvalidOperationException("Dafny wrong composite owner output path has no directory"));
        File.WriteAllBytes(dafnyOwnerCompositeWrongOutPath, wrongCompositeLowering.SourceBytes);
    }
    var partialCompositeOwner = OwnerBundleParser.Parse(File.ReadAllBytes(Path.Combine(fixtureDir, "owner-composite-partial.json")));
    var partialCompositeLowering = ModulesDafnyLowerer.Lower(compositeCandidateIr, partialCompositeOwner);
    if (dafnyOwnerCompositePartialOutPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dafnyOwnerCompositePartialOutPath) ?? throw new InvalidOperationException("Dafny partial composite owner output path has no directory"));
        File.WriteAllBytes(dafnyOwnerCompositePartialOutPath, partialCompositeLowering.SourceBytes);
    }

    var capacityFiveOwner = OwnerBundleParser.Parse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(compositeOwnerBytes).Replace("\"capacity\": \"4\"", "\"capacity\": \"5\"", StringComparison.Ordinal)));
    Check(capacityFiveOwner.BundleDigest != compositeOwner.BundleDigest, "reachable sequence capacity changes owner digest");
    var shuffledCompositeOwnerNode = JsonNode.Parse(compositeOwnerBytes)!.AsObject();
    var shuffledFields = shuffledCompositeOwnerNode["types"]![0]!["fields"]!.AsArray();
    var firstOwnerField = shuffledFields[0]!.DeepClone();
    shuffledFields[0] = shuffledFields[1]!.DeepClone();
    shuffledFields[1] = firstOwnerField;
    var shuffledCompositeOwner = OwnerBundleParser.Parse(shuffledCompositeOwnerNode.ToJsonString());
    Check(shuffledCompositeOwner.BundleDigest == compositeOwner.BundleDigest && shuffledCompositeOwner.CanonicalBytes.SequenceEqual(compositeOwner.CanonicalBytes), "set-like owner field order canonicalizes to one digest");
    var compositeOwnerNode = JsonNode.Parse(compositeOwnerBytes)!.AsObject();
    compositeOwnerNode["types"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"Unused\",\"fields\":[]}"));
    var extraneousOwnerType = CaptureOwnerReject(() => OwnerBundleParser.Parse(compositeOwnerNode.ToJsonString()));
    Check(extraneousOwnerType.Code == "OwnerTypeClosureExtraneous" && extraneousOwnerType.EntityId == "Unused", "owner bundle rejects declarations outside exact semantic closure");

    var emptyClosureBytes = File.ReadAllBytes(Path.Combine(fixtureDir, "owner-empty-sequence-closure.json"));
    var emptyClosureOwner = OwnerBundleParser.Parse(emptyClosureBytes);
    Check(emptyClosureOwner.Types.Single().Id == "Item", "declared Seq<Item,4> reaches Item even when witness sequence is empty");
    var missingClosureNode = JsonNode.Parse(emptyClosureBytes)!.AsObject();
    missingClosureNode["types"] = new JsonArray();
    var missingClosureType = CaptureOwnerReject(() => OwnerBundleParser.Parse(missingClosureNode.ToJsonString()));
    Check(missingClosureType.Code == "OwnerTypeClosureMissing" && missingClosureType.EntityId == "Item", "empty witness cannot hide a missing declared element type");
    var missingTransitiveNode = JsonNode.Parse(emptyClosureBytes)!.AsObject();
    missingTransitiveNode["types"]![0]!["fields"]![0]!["type"] = "U";
    var missingTransitive = CaptureOwnerReject(() => OwnerBundleParser.Parse(missingTransitiveNode.ToJsonString()));
    Check(missingTransitive.Code == "OwnerTypeClosureMissing" && missingTransitive.EntityId == "U", "new transitive owner type edge fails closed when its declaration is absent");
    missingTransitiveNode["types"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"U\",\"fields\":[]}"));
    var transitiveClosureOwner = OwnerBundleParser.Parse(missingTransitiveNode.ToJsonString());
    Check(transitiveClosureOwner.Types.Length == 2 && transitiveClosureOwner.BundleDigest != emptyClosureOwner.BundleDigest, "declared transitive owner type enters the exact closure and digest");
    var emptyClosureModuleText = File.ReadAllText(Path.Combine(fixtureDir, "owner-empty-sequence-module.json"));
    var emptyClosureModule = ModulesCompiler.Compile(ModulesParser.ParseModule(emptyClosureModuleText));
    _ = OwnerContractBinder.Bind(emptyClosureModule, emptyClosureOwner);
    var privateTypeModuleNode = JsonNode.Parse(emptyClosureModuleText)!.AsObject();
    privateTypeModuleNode["types"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"PrivateU\",\"fields\":[]}"));
    var privateTypeModule = ModulesCompiler.Compile(ModulesParser.ParseModule(privateTypeModuleNode.ToJsonString()));
    _ = OwnerContractBinder.Bind(privateTypeModule, emptyClosureOwner);
    Check(privateTypeModule.SourceDigest != emptyClosureModule.SourceDigest, "private module type changes module/proof identity while owner digest stays fixed");
    var baseEmptyLowering = ModulesDafnyLowerer.Lower(emptyClosureModule, emptyClosureOwner);
    var repeatedEmptyLowering = ModulesDafnyLowerer.Lower(emptyClosureModule, emptyClosureOwner);
    var privateTypeLowering = ModulesDafnyLowerer.Lower(privateTypeModule, emptyClosureOwner);
    Check(baseEmptyLowering.SourceBytes.SequenceEqual(repeatedEmptyLowering.SourceBytes), "two clean lowerings of one module and owner bundle are byte-identical");
    Check(privateTypeLowering.SourceDigest != baseEmptyLowering.SourceDigest && emptyClosureOwner.BundleDigest == OwnerBundleParser.Parse(emptyClosureBytes).BundleDigest, "private type drift changes proof identity without changing owner identity");
    var appendPartialOwnerNode = JsonNode.Parse(emptyClosureBytes)!.AsObject();
    appendPartialOwnerNode["bundleId"] = "closure.append-partial-v1";
    appendPartialOwnerNode["entryContracts"]![0]!["returnType"] = "I64";
    appendPartialOwnerNode["models"]![0]!["returnType"] = "I64";
    appendPartialOwnerNode["models"]![0]!["body"] = JsonNode.Parse("""
        {"op":"seq.length","type":"I64","args":[
          {"op":"seq.append","type":{"kind":"Seq","elementType":"Item","capacity":"4"},"args":[
            {"op":"param","type":{"kind":"Seq","elementType":"Item","capacity":"4"},"id":"items"},
            {"op":"record.make","type":"Item","recordType":"Item","fieldIds":["value"],"args":[
              {"op":"i64.const","type":"I64","value":"0"}
            ]}
          ]}
        ]}
        """);
    var appendPartialOwner = OwnerBundleParser.Parse(appendPartialOwnerNode.ToJsonString());
    var appendCandidateNode = JsonNode.Parse(emptyClosureModuleText)!.AsObject();
    appendCandidateNode["moduleId"] = "closure.append-partial-candidate";
    appendCandidateNode["functions"]![0]!["returnType"] = "I64";
    appendCandidateNode["functions"]![0]!["body"]!["nodes"] = JsonNode.Parse("""
        [
          {"id":"length","op":"seq.length","type":"I64","args":["items"]},
          {"id":"one","op":"i64.const","type":"I64","args":[],"value":"1"},
          {"id":"result","op":"i64.add","type":"I64","args":["length","one"]}
        ]
        """);
    appendCandidateNode["functions"]![0]!["body"]!["result"] = "result";
    var appendPartialIr = ModulesCompiler.Compile(ModulesParser.ParseModule(appendCandidateNode.ToJsonString()));
    var appendPartialLowering = ModulesDafnyLowerer.Lower(appendPartialIr, appendPartialOwner);
    Check(appendPartialLowering.Source.Contains("var e000: S000 :=", StringComparison.Ordinal), "nested owner seq.append materializes a typed bounded temporary");
    if (dafnyOwnerAppendPartialOutPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dafnyOwnerAppendPartialOutPath) ?? throw new InvalidOperationException("Dafny append-partial output path has no directory"));
        File.WriteAllBytes(dafnyOwnerAppendPartialOutPath, appendPartialLowering.SourceBytes);
    }
    var mismatchedClosureModule = ModulesCompiler.Compile(ModulesParser.ParseModule(emptyClosureModuleText.Replace("\"id\": \"value\", \"type\": \"I64\"", "\"id\": \"value\", \"type\": \"Bool\"", StringComparison.Ordinal)));
    var closureMismatch = CaptureOwnerReject(() => OwnerContractBinder.Bind(mismatchedClosureModule, emptyClosureOwner));
    Check(closureMismatch.Code == "OwnerTypeClosureMismatch" && closureMismatch.EntityId == "Item", "binder rejects drift in an owner-reachable module type");

    string EmptySequenceOwnerWithItems(params JsonNode[] items)
    {
        var node = JsonNode.Parse(emptyClosureBytes)!.AsObject();
        var value = node["entryContracts"]![0]!["witnesses"]![0]!["arguments"]![0]!["value"]!["value"]!.AsArray();
        value.Clear();
        foreach (var item in items) value.Add(item.DeepClone());
        return node.ToJsonString();
    }
    var validItem = JsonNode.Parse("{\"type\":\"Item\",\"value\":[{\"fieldId\":\"value\",\"value\":{\"type\":\"I64\",\"value\":\"7\"}}]}")!;
    _ = OwnerBundleParser.Parse(EmptySequenceOwnerWithItems(validItem));
    var missingFieldItem = validItem.DeepClone();
    missingFieldItem["value"] = new JsonArray();
    Check(CaptureOwnerReject(() => OwnerBundleParser.Parse(EmptySequenceOwnerWithItems(missingFieldItem))).Code == "RecordFieldMismatch", "composite witness rejects a missing record field");
    var extraFieldItem = validItem.DeepClone();
    extraFieldItem["value"]!.AsArray().Add(JsonNode.Parse("{\"fieldId\":\"extra\",\"value\":{\"type\":\"I64\",\"value\":\"0\"}}"));
    Check(CaptureOwnerReject(() => OwnerBundleParser.Parse(EmptySequenceOwnerWithItems(extraFieldItem))).Code == "RecordFieldMismatch", "composite witness rejects an extra record field");
    var duplicateFieldItem = validItem.DeepClone();
    duplicateFieldItem["value"]!.AsArray().Add(duplicateFieldItem["value"]![0]!.DeepClone());
    Check(CaptureOwnerReject(() => OwnerBundleParser.Parse(EmptySequenceOwnerWithItems(duplicateFieldItem))).Code == "DuplicateWitnessField", "composite witness rejects a duplicate record field");
    var wrongItem = JsonNode.Parse("{\"type\":\"I64\",\"value\":\"7\"}")!;
    Check(CaptureOwnerReject(() => OwnerBundleParser.Parse(EmptySequenceOwnerWithItems(wrongItem))).Code == "ContractTypeMismatch", "composite witness rejects a wrong sequence item type");
    Check(CaptureOwnerReject(() => OwnerBundleParser.Parse(EmptySequenceOwnerWithItems(validItem, validItem, validItem, validItem, validItem))).Code == "SequenceCapacityExceeded", "composite witness rejects sequence length above declared capacity");
    var wrongCapacityOwnerNode = JsonNode.Parse(EmptySequenceOwnerWithItems())!.AsObject();
    wrongCapacityOwnerNode["entryContracts"]![0]!["witnesses"]![0]!["arguments"]![0]!["value"]!["type"]!["capacity"] = "3";
    Check(CaptureOwnerReject(() => OwnerBundleParser.Parse(wrongCapacityOwnerNode.ToJsonString())).Code == "ContractTypeMismatch", "composite witness rejects a mismatched nested capacity");

    var changedModelNode = JsonNode.Parse(compositeOwnerBytes)!.AsObject();
    changedModelNode["models"]![0]!["body"]!["args"]![0]!["value"] = "3";
    var changedModelOwner = OwnerBundleParser.Parse(changedModelNode.ToJsonString());
    Check(changedModelOwner.BundleDigest != compositeOwner.BundleDigest, "exact owner model change changes owner digest");
    var changedModelReplay = OwnerContractReplay.Replay(OwnerContractBinder.Bind(compositeCandidateIr, changedModelOwner));
    Check(changedModelReplay.Status == "Counterexample" && changedModelReplay.Counterexample?.WitnessId == "empty-shape-v1", "changed exact model is distinguished by independent witness replay");
    reports.Add(new { kind = "owner-composite", fixture = "owner-composite-valid.json", compositeDigest = compositeOwner.BundleDigest, emptyClosureDigest = emptyClosureOwner.BundleDigest, modelCandidateEqual = true });

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

    var foldBytes = File.ReadAllBytes(Path.Combine(fixtureDir, "fold-sum-valid.json"));
    var foldParsed = ModulesParser.ParseModule(foldBytes);
    var foldIr = ModulesCompiler.Compile(foldParsed);
    var foldCanonical = ModulesCodec.Canonicalize(foldParsed.Source);
    Check(ModulesParser.ParseModule(foldCanonical).SourceDigest == foldParsed.SourceDigest, "fold source survives canonical roundtrip");
    var foldRoot = JsonNode.Parse(foldBytes)!.AsObject();
    var shuffledFoldRoot = new JsonObject();
    foreach (var property in foldRoot.Reverse()) shuffledFoldRoot[property.Key] = property.Value?.DeepClone();
    Check(ModulesParser.ParseModule(Encoding.UTF8.GetBytes(shuffledFoldRoot.ToJsonString())).SourceDigest == foldParsed.SourceDigest, "fold digest ignores JSON object property order");
    Check(foldIr.Functions.All(function => function.Instructions.Single().Fold is not null), "compiler retains typed fold step and invariant in IR");
    Check(Encoding.UTF8.GetString(foldIr.CanonicalBytes).Contains("\"invariant\"", StringComparison.Ordinal), "canonical IR commits to the invariant subtree");

    var foldItems = new ModuleSequence(new TypeRef("I64"), 4, [new ModuleI64(1), new ModuleI64(2), new ModuleI64(3)]);
    var emptyFoldItems = new ModuleSequence(new TypeRef("I64"), 4, Array.Empty<ModuleValue>());
    var foldSum = ModulesReferenceEvaluator.Invoke(foldIr, "sum", [foldItems, new ModuleI64(10)]);
    var foldEmpty = ModulesReferenceEvaluator.Invoke(foldIr, "sum", [emptyFoldItems, new ModuleI64(10)]);
    var foldBiased = ModulesReferenceEvaluator.Invoke(foldIr, "sumWithBias", [foldItems, new ModuleI64(10), new ModuleI64(2)]);
    Check(foldSum.Value is ModuleI64 { Value: 16 } && foldSum.Steps == 7, "fold executes a deterministic left traversal with one dispatch and one iteration step");
    Check(foldEmpty.Value is ModuleI64 { Value: 10 } && foldEmpty.Steps == 1, "empty fold returns the initial accumulator without executing step");
    Check(foldBiased.Value is ModuleI64 { Value: 22 } && foldBiased.Steps == 10, "fold binds ordered explicit environment values on every step");
    Check(foldItems.Items.Select(item => ((ModuleI64)item).Value).SequenceEqual([1L, 2L, 3L]), "fold leaves its immutable sequence input unchanged");
    Check(ModulesReferenceEvaluator.Invoke(foldIr, "sum", [foldItems, new ModuleI64(10)], new ModuleEvaluationLimits { MaxSteps = 7 }).Value is ModuleI64 { Value: 16 }, "fold accepts an exact inclusive seven-step budget");
    Check(ModulesReferenceEvaluator.Invoke(foldIr, "sum", [emptyFoldItems, new ModuleI64(10)], new ModuleEvaluationLimits { MaxSteps = 1 }).Value is ModuleI64 { Value: 10 }, "empty fold consumes exactly its dispatch step");
    var foldLimit = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(foldIr, "sum", [foldItems, new ModuleI64(10)], new ModuleEvaluationLimits { MaxSteps = 5 }));
    Check(foldLimit.Code == "EvaluationStepLimitExceeded" && foldLimit.EntityId == "function/sum/body/node/result/iteration/2", "fold shares the evaluation budget and refuses before a partial iteration");
    var foldOneBelow = CaptureEvaluationReject(() => ModulesReferenceEvaluator.Invoke(foldIr, "sum", [foldItems, new ModuleI64(10)], new ModuleEvaluationLimits { MaxSteps = 6 }));
    Check(foldOneBelow.Code == "EvaluationStepLimitExceeded" && foldOneBelow.EntityId == "function/sum/body/node/result/step/2/node/next", "fold rejects one below exact cost at the last step node");

    JsonObject FoldWithCapacity(int capacity)
    {
        var clone = JsonNode.Parse(foldBytes)!.AsObject();
        void Rewrite(JsonNode? value)
        {
            if (value is JsonObject obj)
            {
                if (obj["capacity"] is JsonValue) obj["capacity"] = capacity.ToString(CultureInfo.InvariantCulture);
                foreach (var property in obj.ToArray()) Rewrite(property.Value);
            }
            else if (value is JsonArray array)
                foreach (var item in array) Rewrite(item);
        }
        Rewrite(clone);
        return clone;
    }
    var capacityZeroFold = ModulesCompiler.Compile(ModulesParser.ParseModule(FoldWithCapacity(0).ToJsonString()));
    Check(ModulesReferenceEvaluator.Invoke(capacityZeroFold, "sum", [new ModuleSequence(new TypeRef("I64"), 0, []), new ModuleI64(0)]).Value is ModuleI64 { Value: 0 }, "fold accepts capacity zero and executes no step");
    var capacityOneFold = ModulesCompiler.Compile(ModulesParser.ParseModule(FoldWithCapacity(1).ToJsonString()));
    Check(ModulesReferenceEvaluator.Invoke(capacityOneFold, "sum", [new ModuleSequence(new TypeRef("I64"), 1, [new ModuleI64(long.MinValue)]), new ModuleI64(0)]).Value is ModuleI64 { Value: long.MinValue }, "fold preserves I64.MIN at capacity one");
    Check(ModulesReferenceEvaluator.Invoke(capacityOneFold, "sum", [new ModuleSequence(new TypeRef("I64"), 1, [new ModuleI64(long.MaxValue)]), new ModuleI64(0)]).Value is ModuleI64 { Value: long.MaxValue }, "fold preserves I64.MAX at capacity one");

    JsonObject MutateFold(Action<JsonObject, JsonObject, JsonObject> mutation)
    {
        var rootNode = JsonNode.Parse(foldBytes)!.AsObject();
        var function = rootNode["functions"]![0]!.AsObject();
        var fold = function["body"]!["nodes"]![0]!.AsObject();
        mutation(rootNode, function, fold);
        return rootNode;
    }
    ModuleException RejectFold(string name, Action<JsonObject, JsonObject, JsonObject> mutation)
    {
        var failure = CaptureParserReject(MutateFold(mutation).ToJsonString());
        reports.Add(new { kind = "negative", fixture = name, code = failure.Code, stage = failure.Stage, detail = failure.DetailsJson });
        return failure;
    }

    Check(RejectFold("fold-missing-invariant", (_, _, fold) => fold.Remove("invariant")).Code == "InvariantRequired", "fold requires an explicit invariant");
    Check(RejectFold("fold-environment-out-of-range", (_, _, fold) => fold["invariant"] = JsonNode.Parse("{\"op\":\"fold.environment\",\"type\":\"I64\",\"position\":\"0\"}")).Code == "FoldEnvironmentOutOfRange", "fold invariant cannot reference an absent environment value");
    Check(RejectFold("fold-param-in-invariant", (_, _, fold) => fold["invariant"] = JsonNode.Parse("{\"op\":\"param\",\"type\":\"I64\",\"id\":\"initial\"}")).Code == "UnsupportedProofOpcode", "fold invariant is closed over explicit fold roles");
    Check(RejectFold("fold-hidden-step-capture", (_, _, fold) => fold["stepRegion"]!["nodes"]![0]!["args"]![1] = "initial").Code == "DanglingNodeArg", "fold step cannot capture a function parameter implicitly");
    Check(RejectFold("fold-nested", (_, _, fold) => fold["stepRegion"]!["nodes"]![0]!["op"] = "fold").Code == "NestedFoldNotSupported", "fold step cannot contain another fold");
    Check(RejectFold("fold-step-call", (_, _, fold) => fold["stepRegion"]!["nodes"]![0] = JsonNode.Parse("{\"id\":\"next\",\"op\":\"call\",\"type\":\"I64\",\"args\":[\"accumulator\",\"element\"],\"functionRef\":\"sum\"}")).Code == "FoldStepCallNotSupported", "fold step cannot contain a call");
    Check(RejectFold("fold-not-function-result", (_, function, _) =>
    {
        function["body"]!["nodes"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"zero\",\"op\":\"i64.const\",\"type\":\"I64\",\"args\":[],\"value\":\"0\"}"));
        function["body"]!["result"] = "zero";
    }).Code == "FoldMustBeFunctionResult", "the reachable function result must be its single fold");
    Check(RejectFold("fold-prefix-arity", (_, _, fold) => fold["invariant"]!["args"]![0]!["args"]![1]!["args"]!.AsArray().RemoveAt(1)).Code == "ArityMismatch", "prefix sum has one canonical arity");
    Check(RejectFold("fold-prefix-type", (_, _, fold) => fold["invariant"]!["args"]![0]!["args"]![1]!["args"]![1]!["type"] = "Bool").Code == "TypeMismatch", "prefix sum length must be I64");
    var quantifiedFold = MutateFold((_, _, fold) => fold["invariant"] = JsonNode.Parse("{\"op\":\"forall.sequence\",\"type\":\"Bool\",\"binderId\":\"i\",\"sequence\":{\"op\":\"fold.sequence\",\"type\":{\"kind\":\"Seq\",\"elementType\":\"I64\",\"capacity\":\"4\"}},\"body\":{\"op\":\"eq\",\"type\":\"Bool\",\"args\":[{\"op\":\"proof.bound\",\"type\":\"I64\",\"binderId\":\"i\"},{\"op\":\"proof.bound\",\"type\":\"I64\",\"binderId\":\"i\"}]}}")).ToJsonString();
    Check(ModulesParser.ParseModule(quantifiedFold).Source.Functions.Length == 2, "fold proof supports one bounded sequence quantifier and scoped binder");
    JsonNode ExpensiveProofLeaf() => JsonNode.Parse("{\"op\":\"math.le\",\"type\":\"Bool\",\"args\":[{\"op\":\"seq.sum_i64\",\"type\":\"MathInt\",\"args\":[{\"op\":\"fold.sequence\",\"type\":{\"kind\":\"Seq\",\"elementType\":\"I64\",\"capacity\":\"256\"}}]},{\"op\":\"math.const\",\"type\":\"MathInt\",\"value\":\"9223372036854775807\"}]}")!;
    JsonNode ExpensiveProofTree(int leaves)
    {
        if (leaves == 1) return ExpensiveProofLeaf();
        var left = leaves / 2;
        return new JsonObject
        {
            ["op"] = "bool.and",
            ["type"] = "Bool",
            ["args"] = new JsonArray(ExpensiveProofTree(left), ExpensiveProofTree(leaves - left))
        };
    }
    var excessiveProofFold = FoldWithCapacity(256);
    excessiveProofFold["functions"]![0]!["body"]!["nodes"]![0]!["invariant"] = new JsonObject
    {
        ["op"] = "forall.sequence",
        ["type"] = "Bool",
        ["binderId"] = "i",
        ["sequence"] = JsonNode.Parse("{\"op\":\"fold.sequence\",\"type\":{\"kind\":\"Seq\",\"elementType\":\"I64\",\"capacity\":\"256\"}}"),
        ["body"] = ExpensiveProofTree(8)
    };
    var excessiveProofFailure = CaptureParserReject(excessiveProofFold.ToJsonString());
    var excessiveProofDetails = JsonNode.Parse(excessiveProofFailure.DetailsJson!)!.AsObject();
    Check(excessiveProofFailure.Code == "ProofWorstCaseCostExceeded" && excessiveProofDetails["actual"]!.GetValue<string>() == ">262144" && excessiveProofDetails["max"]!.GetValue<string>() == "262144", "candidate parser saturates proof cost above the 262144 hard maximum");
    reports.Add(new { kind = "fold", fixture = "fold-sum-valid.json", sourceDigest = foldParsed.SourceDigest, irDigest = ModulesCodec.IrDigest(foldIr.CanonicalBytes), semanticChecks = 17 });

    var ownerFoldBytes = File.ReadAllBytes(Path.Combine(fixtureDir, "owner-fold-sum-valid-v0.4.json"));
    var ownerFold = OwnerBundleV04Parser.Parse(ownerFoldBytes);
    Check(ownerFold.SchemaVersion == "strogo.owner-bundle.v0.4" && ownerFold.Limits.MaxProofEvaluationSteps == 128, "owner v0.4 parses the explicit proof budget");
    Check(OwnerBundleV04Parser.Parse(ownerFold.CanonicalBytes).BundleDigest == ownerFold.BundleDigest, "owner v0.4 canonical bytes roundtrip to the same digest");
    Check(CaptureOwnerReject(() => OwnerBundleParser.Parse(ownerFoldBytes)).Code == "SchemaVersionMismatch", "owner v0.3 parser rejects v0.4 bytes");
    Check(CaptureOwnerReject(() => OwnerBundleV04Parser.Parse(ownerBytes)).Code == "OwnerBundleMigrationRequired", "owner v0.4 parser requires explicit migration from v0.3");
    var migratedV04Bytes = OwnerBundleMigrator.MigrateV03ToV04(ownerBytes);
    var migratedV04 = OwnerBundleV04Parser.Parse(migratedV04Bytes);
    Check(migratedV04.Limits.MaxExpressionNodes == owner.Limits.MaxExpressionNodes
        && migratedV04.Limits.MaxExpressionDepth == owner.Limits.MaxExpressionDepth
        && migratedV04.Limits.MaxWitnessesPerEntry == owner.Limits.MaxWitnessesPerEntry
        && migratedV04.Limits.MaxWitnessValueNodes == owner.Limits.MaxWitnessValueNodes
        && migratedV04.Limits.MaxProofEvaluationSteps == OwnerBundleLimitsV04.ProofEvaluationStepsHardMaximum, "v0.3 to v0.4 migration preserves old limits and adds the hard proof budget");
    using (var oldCanonicalOwner = JsonDocument.Parse(owner.CanonicalBytes))
    using (var newCanonicalOwner = JsonDocument.Parse(migratedV04.CanonicalBytes))
        Check(oldCanonicalOwner.RootElement.GetProperty("entryContracts")[0].GetProperty("requires").GetRawText()
            == newCanonicalOwner.RootElement.GetProperty("entryContracts")[0].GetProperty("requires").GetRawText(), "v0.3 requires remains byte-identical as a canonical v0.4 subtree");
    var migratedV04Binding = OwnerContractBinderV04.Bind(ir, migratedV04);
    Check(OwnerContractReplayV04.Replay(migratedV04Binding).Status == "Pass" && migratedV04Binding.Entries[0].CandidateFold is null, "migrated non-fold v0.3 owner behavior remains replayable in v0.4");
    var allLegacyOpsBytes = File.ReadAllBytes(Path.Combine(fixtureDir, "owner-v03-proof-all-ops.json"));
    var allLegacyOps = OwnerBundleParser.Parse(allLegacyOpsBytes);
    var allLegacyOpsMigrated = OwnerBundleV04Parser.Parse(OwnerBundleMigrator.MigrateV03ToV04(allLegacyOpsBytes));
    using (var oldAllOpsDocument = JsonDocument.Parse(allLegacyOps.CanonicalBytes))
    using (var newAllOpsDocument = JsonDocument.Parse(allLegacyOpsMigrated.CanonicalBytes))
    {
        var oldRequires = oldAllOpsDocument.RootElement.GetProperty("entryContracts")[0].GetProperty("requires");
        var newRequires = newAllOpsDocument.RootElement.GetProperty("entryContracts")[0].GetProperty("requires");
        Check(oldRequires.GetRawText() == newRequires.GetRawText(), "v0.3 to v0.4 migration preserves the full inherited proof expression subtree byte-for-byte");
        var inheritedOps = new HashSet<string>(StringComparer.Ordinal);
        void CollectOps(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (value.TryGetProperty("op", out var op)) inheritedOps.Add(op.GetString()!);
                foreach (var property in value.EnumerateObject()) CollectOps(property.Value);
            }
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray()) CollectOps(item);
        }
        CollectOps(oldRequires);
        Check(inheritedOps.IsSupersetOf(["param", "i64.const", "bool.const", "i64.add", "i64.sub", "i64.le", "eq", "bool.not", "bool.and", "bool.or", "if", "record.make", "record.get", "seq.empty", "seq.length", "seq.get", "seq.append"]), "migration golden exercises every inherited v0.3 proof opcode");
    }

    var foldBinding = OwnerContractBinderV04.Bind(foldIr, ownerFold);
    Check(foldBinding.Entries.Length == 2 && foldBinding.Entries.All(entry => entry.CandidateFold?.Fold is not null), "owner binder pairs each candidate fold with one owner fold");
    var foldReplay = OwnerContractReplayV04.Replay(foldBinding);
    Check(foldReplay.Status == "Pass" && foldReplay.CheckedWitnesses == 2, "owner v0.4 witness replay matches candidate fold outcomes");
    Check(foldBinding.Entries.Single(entry => entry.Contract.Id == "sumContract").Witnesses[0].ModelResult is ModuleI64 { Value: 6 }, "owner fold model executes the exact left sum");
    Check(foldBinding.Entries.Single(entry => entry.Contract.Id == "sumWithBiasContract").Witnesses[0].ModelResult is ModuleI64 { Value: 22 }, "owner fold model binds ordered environment values");
    var sumEntry = ownerFold.EntryContracts.Single(entry => entry.Id == "sumContract");
    var sumEnvironment = sumEntry.Witnesses[0].Arguments.ToImmutableDictionary(argument => argument.ParameterId, argument => argument.Value, StringComparer.Ordinal);
    Check(OwnerProofEvaluator.EvaluateBoolean(sumEntry.Requires, sumEnvironment, ownerFold.Types, ownerFold.Limits.MaxProofEvaluationSteps), "owner proof evaluator handles bounded quantification and unbounded sum");
    Check(OwnerProofEvaluator.EvaluateBoolean(sumEntry.Requires, sumEnvironment, ownerFold.Types, ownerFold.Limits.MaxProofEvaluationSteps), "each proof evaluation receives a fresh independent step counter");
    Check(CaptureOwnerReject(() => OwnerProofEvaluator.EvaluateBoolean(sumEntry.Requires, sumEnvironment, ownerFold.Types, 1)).Code == "ProofEvaluationStepLimitExceeded", "proof evaluator fails closed at its own explicit step budget");
    var invalidPrefix = new ProofExpression("seq.prefix_sum_i64", new TypeRef("MathInt"),
        [new ProofExpression("param", new TypeRef("Seq", Element: new TypeRef("I64"), Capacity: 4), ImmutableArray<ProofExpression>.Empty, ReferenceId: "items"),
         new ProofExpression("i64.const", new TypeRef("I64"), ImmutableArray<ProofExpression>.Empty, NumberValue: "4")]);
    var shortEnvironment = new Dictionary<string, ModuleValue> { ["items"] = new ModuleSequence(new TypeRef("I64"), 4, [new ModuleI64(1)]) };
    Check(CaptureOwnerReject(() => OwnerProofEvaluator.Evaluate(invalidPrefix, shortEnvironment, Array.Empty<TypeDecl>(), 16)).Code == "ProofPrefixLengthOutOfRange", "proof evaluator rejects prefix lengths beyond the actual sequence");
    ProofExpression PrefixAt(string length) => new("seq.prefix_sum_i64", new TypeRef("MathInt"),
        [new ProofExpression("param", new TypeRef("Seq", Element: new TypeRef("I64"), Capacity: 4), ImmutableArray<ProofExpression>.Empty, ReferenceId: "items"),
         new ProofExpression("i64.const", new TypeRef("I64"), ImmutableArray<ProofExpression>.Empty, NumberValue: length)]);
    Check(OwnerProofEvaluator.Evaluate(PrefixAt("0"), shortEnvironment, Array.Empty<TypeDecl>(), 16) is ProofMathInt { Value: var prefixZero } && prefixZero.IsZero, "prefix sum accepts zero length");
    Check(OwnerProofEvaluator.Evaluate(PrefixAt("1"), shortEnvironment, Array.Empty<TypeDecl>(), 16) is ProofMathInt { Value: var prefixOne } && prefixOne == 1, "prefix sum accepts exact sequence length");
    Check(CaptureOwnerReject(() => OwnerProofEvaluator.Evaluate(PrefixAt("-1"), shortEnvironment, Array.Empty<TypeDecl>(), 16)).Code == "ProofPrefixLengthOutOfRange", "prefix sum rejects negative length");
    var emptySequenceProof = new ProofExpression("seq.empty", new TypeRef("Seq", Element: new TypeRef("I64"), Capacity: 1), ImmutableArray<ProofExpression>.Empty, ElementType: new TypeRef("I64"), Capacity: 1);
    var outOfRangeItemProof = new ProofExpression("seq.get", new TypeRef("I64"),
        [emptySequenceProof, new ProofExpression("i64.const", new TypeRef("I64"), ImmutableArray<ProofExpression>.Empty, NumberValue: "0")]);
    var boundProof = new ProofExpression("proof.bound", new TypeRef("I64"), ImmutableArray<ProofExpression>.Empty, BinderId: "i");
    var latePartialBody = new ProofExpression("if", new TypeRef("Bool"),
        [new ProofExpression("eq", new TypeRef("Bool"), [boundProof, new ProofExpression("i64.const", new TypeRef("I64"), ImmutableArray<ProofExpression>.Empty, NumberValue: "0")]),
         new ProofExpression("bool.const", new TypeRef("Bool"), ImmutableArray<ProofExpression>.Empty, BoolValue: false),
         new ProofExpression("eq", new TypeRef("Bool"), [outOfRangeItemProof, new ProofExpression("i64.const", new TypeRef("I64"), ImmutableArray<ProofExpression>.Empty, NumberValue: "0")])]);
    var strictForAll = new ProofExpression("forall.sequence", new TypeRef("Bool"), ImmutableArray<ProofExpression>.Empty,
        BinderId: "i",
        Sequence: new ProofExpression("param", new TypeRef("Seq", Element: new TypeRef("I64"), Capacity: 4), ImmutableArray<ProofExpression>.Empty, ReferenceId: "items"),
        Body: latePartialBody);
    var twoItemEnvironment = new Dictionary<string, ModuleValue> { ["items"] = new ModuleSequence(new TypeRef("I64"), 4, [new ModuleI64(1), new ModuleI64(2)]) };
    Check(CaptureOwnerReject(() => OwnerProofEvaluator.Evaluate(strictForAll, twoItemEnvironment, Array.Empty<TypeDecl>(), 64)).Code == "SequenceIndexOutOfRange", "forall evaluates every body instance to preserve totality after an earlier false result");
    var prefixOrderProof = new ProofExpression("seq.prefix_sum_i64", new TypeRef("MathInt"),
        [new ProofExpression("seq.get", new TypeRef("Seq", Element: new TypeRef("I64"), Capacity: 1), [emptySequenceProof, new ProofExpression("i64.const", new TypeRef("I64"), ImmutableArray<ProofExpression>.Empty, NumberValue: "0")]),
         new ProofExpression("i64.add", new TypeRef("I64"), [new ProofExpression("i64.const", new TypeRef("I64"), ImmutableArray<ProofExpression>.Empty, NumberValue: long.MaxValue.ToString(CultureInfo.InvariantCulture)), new ProofExpression("i64.const", new TypeRef("I64"), ImmutableArray<ProofExpression>.Empty, NumberValue: "1")])]);
    Check(CaptureOwnerReject(() => OwnerProofEvaluator.Evaluate(prefixOrderProof, new Dictionary<string, ModuleValue>(), Array.Empty<TypeDecl>(), 64)).Code == "SequenceIndexOutOfRange", "prefix sum evaluates sequence before prefix length");
    var fullSequenceEnvironment = new Dictionary<string, ModuleValue> { ["full"] = new ModuleSequence(new TypeRef("I64"), 1, [new ModuleI64(1)]) };
    var strictAppend = new ProofExpression("seq.append", new TypeRef("Seq", Element: new TypeRef("I64"), Capacity: 1),
        [new ProofExpression("param", new TypeRef("Seq", Element: new TypeRef("I64"), Capacity: 1), ImmutableArray<ProofExpression>.Empty, ReferenceId: "full"), outOfRangeItemProof]);
    Check(CaptureOwnerReject(() => OwnerProofEvaluator.Evaluate(strictAppend, fullSequenceEnvironment, Array.Empty<TypeDecl>(), 64)).Code == "SequenceIndexOutOfRange", "sequence append evaluates its item before checking capacity");
    var partialBoolean = new ProofExpression("eq", new TypeRef("Bool"), [outOfRangeItemProof, new ProofExpression("i64.const", new TypeRef("I64"), ImmutableArray<ProofExpression>.Empty, NumberValue: "0")]);
    var falseProof = new ProofExpression("bool.const", new TypeRef("Bool"), ImmutableArray<ProofExpression>.Empty, BoolValue: false);
    var trueProof = new ProofExpression("bool.const", new TypeRef("Bool"), ImmutableArray<ProofExpression>.Empty, BoolValue: true);
    Check(CaptureOwnerReject(() => OwnerProofEvaluator.Evaluate(new ProofExpression("bool.and", new TypeRef("Bool"), [falseProof, partialBoolean]), new Dictionary<string, ModuleValue>(), Array.Empty<TypeDecl>(), 64)).Code == "SequenceIndexOutOfRange", "false and partial remains partial in proof evaluation");
    Check(CaptureOwnerReject(() => OwnerProofEvaluator.Evaluate(new ProofExpression("bool.or", new TypeRef("Bool"), [trueProof, partialBoolean]), new Dictionary<string, ModuleValue>(), Array.Empty<TypeDecl>(), 64)).Code == "SequenceIndexOutOfRange", "true or partial remains partial in proof evaluation");
    Check(OwnerProofEvaluator.EvaluateBoolean(new ProofExpression("if", new TypeRef("Bool"), [falseProof, partialBoolean, trueProof]), new Dictionary<string, ModuleValue>(), Array.Empty<TypeDecl>(), 64), "proof if evaluates only its selected branch");
    var emptyForAll = new ProofExpression("forall.sequence", new TypeRef("Bool"), ImmutableArray<ProofExpression>.Empty, BinderId: "i", Sequence: emptySequenceProof, Body: partialBoolean);
    Check(OwnerProofEvaluator.EvaluateBoolean(emptyForAll, new Dictionary<string, ModuleValue>(), Array.Empty<TypeDecl>(), 64), "empty forall sequence skips its body");
    ProofExpression BalancedTrue(int leaves)
    {
        if (leaves == 1) return trueProof;
        var left = leaves / 2;
        return new ProofExpression("bool.and", new TypeRef("Bool"), [BalancedTrue(left), BalancedTrue(leaves - left)]);
    }
    var costlyProof = new ProofExpression("forall.sequence", new TypeRef("Bool"), ImmutableArray<ProofExpression>.Empty,
        BinderId: "i",
        Sequence: new ProofExpression("param", new TypeRef("Seq", Element: new TypeRef("I64"), Capacity: 256), ImmutableArray<ProofExpression>.Empty, ReferenceId: "items"),
        Body: BalancedTrue(390));
    var fullProofSequence = new ModuleSequence(new TypeRef("I64"), 256, Enumerable.Range(0, 256).Select(_ => (ModuleValue)new ModuleI64(1)).ToArray());
    var fullProofEnvironment = new Dictionary<string, ModuleValue> { ["items"] = fullProofSequence };
    Check(OwnerProofEvaluator.EvaluateBoolean(costlyProof, fullProofEnvironment, Array.Empty<TypeDecl>(), 200000), "first 200000-step proof witness fits its independent budget");
    Check(OwnerProofEvaluator.EvaluateBoolean(costlyProof, fullProofEnvironment, Array.Empty<TypeDecl>(), 200000), "second 200000-step proof witness receives a reset counter");

    JsonObject MutateOwnerFold(Action<JsonObject> mutation)
    {
        var node = JsonNode.Parse(ownerFoldBytes)!.AsObject();
        mutation(node);
        return node;
    }
    Check(CaptureOwnerReject(() => OwnerBundleV04Parser.Parse(MutateOwnerFold(node => node["limits"]!["maxProofEvaluationSteps"] = "1").ToJsonString())).Code == "ProofWorstCaseCostExceeded", "owner parser rejects a proof whose static worst-case cost exceeds the approved budget");
    Check(OwnerBundleV04Parser.Parse(MutateOwnerFold(node => node["limits"]!["maxProofEvaluationSteps"] = "35").ToJsonString()).Limits.MaxProofEvaluationSteps == 35, "owner parser accepts the exact worst-case proof cost");
    var proofCostOneBelow = CaptureOwnerReject(() => OwnerBundleV04Parser.Parse(MutateOwnerFold(node => node["limits"]!["maxProofEvaluationSteps"] = "34").ToJsonString()));
    Check(proofCostOneBelow.Code == "ProofWorstCaseCostExceeded" && proofCostOneBelow.DetailsJson?.Contains("\"actual\":\"35\"", StringComparison.Ordinal) == true && proofCostOneBelow.DetailsJson.Contains("\"max\":\"34\"", StringComparison.Ordinal), "proof cost one below returns exact canonical actual and max");
    Check(CaptureOwnerReject(() => OwnerBundleV04Parser.Parse(MutateOwnerFold(node => node["entryContracts"]![0]!["requires"] = JsonNode.Parse("{\"op\":\"fold.accumulator\",\"type\":\"I64\"}")).ToJsonString())).Code == "UnsupportedProofOpcode", "owner requires cannot reference candidate fold roles");
    Check(CaptureOwnerReject(() => OwnerBundleV04Parser.Parse(MutateOwnerFold(node => node["models"]![0]!["body"]!["step"]!["body"]!["op"] = "fold").ToJsonString())).Code == "NestedFoldNotSupported", "owner model step cannot contain a nested fold");
    Check(CaptureOwnerReject(() => OwnerBundleV04Parser.Parse(MutateOwnerFold(node => node["models"]![0]!["body"]!["step"]!["parameters"]![1]!["type"] = "Bool").ToJsonString())).Code == "ContractTypeMismatch", "owner fold step positional types are exact");
    Check(CaptureOwnerReject(() => OwnerBundleV04Parser.Parse(MutateOwnerFold(node => node["types"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"MathInt\",\"fields\":[]}"))).ToJsonString())).Code == "ReservedTypeId", "MathInt cannot be shadowed by an executable record type");
    reports.Add(new { kind = "owner-v0.4", fixture = "owner-fold-sum-valid-v0.4.json", ownerFold.BundleDigest, entries = foldBinding.Entries.Length, witnesses = foldReplay.CheckedWitnesses });

    var allocationOwner = OwnerBundleV04Parser.Parse(File.ReadAllBytes(Path.Combine(fixtureDir, "owner-fold-allocation-v0.4.json")));
    var allocationOwnerRoot = JsonNode.Parse(allocationOwner.CanonicalBytes)!.AsObject();
    var shuffledAllocationOwnerRoot = new JsonObject();
    foreach (var property in allocationOwnerRoot.Reverse()) shuffledAllocationOwnerRoot[property.Key] = property.Value?.DeepClone();
    Check(OwnerBundleV04Parser.Parse(shuffledAllocationOwnerRoot.ToJsonString()).BundleDigest == allocationOwner.BundleDigest, "owner v0.4 digest ignores set-like object property order");
    var allocationPrimary = ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "fold-allocation-primary.json"))));
    var allocationAlternative = ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, "fold-allocation-alternative.json"))));
    Check(allocationPrimary.SourceDigest != allocationAlternative.SourceDigest, "alternative allocation candidate has a distinct source identity");
    var allocationPrimaryBinding = OwnerContractBinderV04.Bind(allocationPrimary, allocationOwner);
    var allocationAlternativeBinding = OwnerContractBinderV04.Bind(allocationAlternative, allocationOwner);
    Check(OwnerContractReplayV04.Replay(allocationPrimaryBinding).Status == "Pass", "primary allocation candidate matches every owner witness");
    Check(OwnerContractReplayV04.Replay(allocationAlternativeBinding).Status == "Pass", "alternative allocation candidate matches every owner witness");
    var allocationRequests = new ModuleSequence(new TypeRef("I64"), 256, [new ModuleI64(4), new ModuleI64(3), new ModuleI64(1)]);
    foreach (var candidate in new[] { allocationPrimary, allocationAlternative })
    {
        var result = ModulesReferenceEvaluator.Invoke(candidate, "allocate", [allocationRequests, new ModuleI64(5)]).Value as ModuleRecord;
        Check(result?.Fields["remaining"] is ModuleI64 { Value: 0 }
            && result.Fields["allocations"] is ModuleSequence { Items: var items }
            && items.Cast<ModuleI64>().Select(item => item.Value).SequenceEqual([4L, 0L, 1L]), "allocation fold preserves exact left-to-right request semantics");
    }
    var allocationPrimaryLowering = ModulesDafnyLowerer.Lower(allocationPrimary, allocationOwner);
    var allocationAlternativeLowering = ModulesDafnyLowerer.Lower(allocationAlternative, allocationOwner);
    Check(allocationPrimaryLowering.SourceDigest == ModulesDafnyLowerer.Lower(allocationPrimary, allocationOwner).SourceDigest, "allocation lowering is byte-deterministic for one candidate");
    Check(allocationPrimaryLowering.ProofIdentity != allocationAlternativeLowering.ProofIdentity, "allocation proof identity commits to the alternative step DAG");
    Directory.CreateDirectory(foldOutputPath);
    File.WriteAllBytes(Path.Combine(foldOutputPath, "allocation-primary.dfy"), allocationPrimaryLowering.SourceBytes);
    File.WriteAllBytes(Path.Combine(foldOutputPath, "allocation-alternative.dfy"), allocationAlternativeLowering.SourceBytes);
    JsonObject MutateAllocation(Action<JsonObject, JsonObject, JsonObject> mutation)
    {
        var rootNode = JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixtureDir, "fold-allocation-primary.json")))!.AsObject();
        var body = rootNode["functions"]![0]!["body"]!.AsObject();
        var foldNode = body["nodes"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "result")!.AsObject();
        mutation(rootNode, body, foldNode);
        return rootNode;
    }
    var wrongBranchAllocation = MutateAllocation((_, _, foldNode) =>
    {
        var allocationNode = foldNode["stepRegion"]!["nodes"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "allocation")!;
        allocationNode["thenRegion"]!["result"] = "thenZero";
    });
    var wrongBranchIr = ModulesCompiler.Compile(ModulesParser.ParseModule(wrongBranchAllocation.ToJsonString()));
    var wrongBranchReplay = OwnerContractReplayV04.Replay(OwnerContractBinderV04.Bind(wrongBranchIr, allocationOwner));
    Check(wrongBranchReplay.Status == "Counterexample" && wrongBranchReplay.Counterexample?.WitnessId == "maximum-v1", "wrong allocation branch is rejected by the first canonical distinguishing owner witness");

    var weakInvariantIr = ModulesCompiler.Compile(ModulesParser.ParseModule(MutateAllocation((_, _, foldNode) => foldNode["invariant"] = JsonNode.Parse("{\"op\":\"bool.const\",\"type\":\"Bool\",\"value\":true}")).ToJsonString()));
    var weakInvariantLowering = ModulesDafnyLowerer.Lower(weakInvariantIr, allocationOwner);
    Check(weakInvariantLowering.ProofIdentity != allocationPrimaryLowering.ProofIdentity, "proof identity changes with the allocation invariant subtree");
    File.WriteAllBytes(Path.Combine(foldOutputPath, "allocation-weak-invariant.dfy"), weakInvariantLowering.SourceBytes);

    ModuleIr InputMutation(int position) => ModulesCompiler.Compile(ModulesParser.ParseModule(MutateAllocation((_, body, foldNode) =>
    {
        if (position == 0)
        {
            foldNode["args"]![0] = "empty";
            return;
        }
        body["nodes"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"outerZero\",\"op\":\"i64.const\",\"type\":\"I64\",\"args\":[],\"value\":\"0\"}"));
        if (position == 1)
        {
            var initialNode = body["nodes"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "initial")!;
            initialNode["args"]![1] = "outerZero";
        }
        else
            foldNode["args"]![2] = "outerZero";
    }).ToJsonString()));
    var inputMutationProofIdentities = new HashSet<string>(StringComparer.Ordinal);
    for (var position = 0; position < 3; position++)
    {
        var mutationLowering = ModulesDafnyLowerer.Lower(InputMutation(position), allocationOwner);
        inputMutationProofIdentities.Add(mutationLowering.ProofIdentity);
        Check(mutationLowering.Obligations.Any(obligation => obligation.Id == $"input-equivalence-{position}" && obligation.EntityId.EndsWith($"/input-equivalence/{position}", StringComparison.Ordinal)), $"input mutation {position} has its exact equivalence obligation");
        File.WriteAllBytes(Path.Combine(foldOutputPath, $"allocation-input-{position}-mutation.dfy"), mutationLowering.SourceBytes);
        File.WriteAllBytes(Path.Combine(foldOutputPath, $"allocation-input-{position}-mutation.obligations.json"), JsonSerializer.SerializeToUtf8Bytes(new { mutationLowering.ProofIdentity, mutationLowering.Obligations }));
    }
    Check(inputMutationProofIdentities.Count == 3 && !inputMutationProofIdentities.Contains(allocationPrimaryLowering.ProofIdentity), "each ordered fold argument mutation changes proof identity");
    reports.Add(new { kind = "fold-allocation", ownerDigest = allocationOwner.BundleDigest, primarySourceDigest = allocationPrimaryLowering.SourceDigest, alternativeSourceDigest = allocationAlternativeLowering.SourceDigest });

    var discriminatorOwner = OwnerBundleV04Parser.Parse(File.ReadAllBytes(Path.Combine(fixtureDir, "owner-fold-discriminator-v0.4.json")));
    var discriminatorModules = new Dictionary<string, ModuleIr>(StringComparer.Ordinal);
    var discriminatorLowerings = new Dictionary<string, DafnyFoldLoweringResult>(StringComparer.Ordinal);
    var discriminatorOutput = foldOutputPath;
    Directory.CreateDirectory(discriminatorOutput);
    foreach (var variant in new[] { "a", "b", "c" })
    {
        var module = ModulesCompiler.Compile(ModulesParser.ParseModule(File.ReadAllBytes(Path.Combine(fixtureDir, $"fold-discriminator-{variant}.json"))));
        discriminatorModules[variant] = module;
        discriminatorLowerings[variant] = ModulesDafnyLowerer.Lower(module, discriminatorOwner);
        File.WriteAllBytes(Path.Combine(discriminatorOutput, $"candidate-{variant}.dfy"), discriminatorLowerings[variant].SourceBytes);
        File.WriteAllBytes(Path.Combine(discriminatorOutput, $"candidate-{variant}.normalized.dfy"), discriminatorLowerings[variant].NormalizedSourceBytes);
        var outcome = ModulesReferenceEvaluator.Invoke(module, "sum", [new ModuleSequence(new TypeRef("I64"), 4, [new ModuleI64(1), new ModuleI64(2), new ModuleI64(3)])]);
        Check(outcome.Value is ModuleI64 { Value: 6 }, $"fold discriminator {variant} preserves the shared executable outcome");
    }
    Check(discriminatorModules.Values.Select(module => module.SourceDigest).Distinct(StringComparer.Ordinal).Count() == 3, "A/B/C candidate identity changes with the invariant subtree");
    Check(discriminatorLowerings.Values.Select(lowering => lowering.NormalizedSourceDigest).Distinct(StringComparer.Ordinal).Count() == 1, "A/B/C generated Dafny is byte-identical after replacing invariant-dependent spans");
    Check(discriminatorLowerings.Values.All(lowering => lowering.InvariantSpans.Length == 4), "each discriminator variant identifies every invariant-dependent generated line");
    Check(discriminatorLowerings.Values.All(lowering => lowering.Obligations.Select(obligation => obligation.Id).ToHashSet(StringComparer.Ordinal).IsSupersetOf([
        "input-equivalence-0", "input-equivalence-1", "owner-prefix-initial", "owner-prefix-preservation", "candidate-loop-invariant", "candidate-loop-preservation", "candidate-postcondition"])), "fold lowering publishes the stable obligation map");
    Check(discriminatorLowerings.Values.Select(lowering => lowering.ProofIdentity).Distinct(StringComparer.Ordinal).Count() == 3, "proof identity commits to each candidate while sharing one owner bundle and toolchain");
    foreach (var loweringPair in discriminatorLowerings)
    {
        File.WriteAllBytes(Path.Combine(discriminatorOutput, $"candidate-{loweringPair.Key}.obligations.json"), JsonSerializer.SerializeToUtf8Bytes(new { loweringPair.Value.ProofIdentity, loweringPair.Value.SourceDigest, loweringPair.Value.NormalizedSourceDigest, loweringPair.Value.InvariantSpans, loweringPair.Value.Obligations }));
    }
    File.WriteAllBytes(Path.Combine(discriminatorOutput, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(new
    {
        toolchainIdentity = ModulesDafnyLowerer.FoldToolchainIdentity,
        toolchainDigest = ModulesDafnyLowerer.FoldToolchainDigest,
        ownerDigest = discriminatorOwner.BundleDigest,
        variants = discriminatorLowerings.OrderBy(item => item.Key).Select(item => new { variant = item.Key, item.Value.ProofIdentity, item.Value.SourceDigest, item.Value.NormalizedSourceDigest })
    }));
    reports.Add(new { kind = "fold-lowering", fixture = "fold-discriminator-a/b/c", toolchainDigest = ModulesDafnyLowerer.FoldToolchainDigest, ownerDigest = discriminatorOwner.BundleDigest, normalizedDigest = discriminatorLowerings["a"].NormalizedSourceDigest });

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
    reports.Add(new { kind = "runtime", fixture = "if-lazy-overflow.json", safe = long.MaxValue.ToString(CultureInfo.InvariantCulture), overflow = overflow.Code, overflow.EntityId, safeSteps = safeMaximum.Steps });

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
