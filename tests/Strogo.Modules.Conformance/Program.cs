using System.Text;
using System.Text.Json;
using Strogo.Modules;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var fixtureDir = Path.Combine(root, "fixtures", "modules-v0.2");
var reportOption = Array.IndexOf(args, "--report");
if (reportOption >= 0 && reportOption + 1 >= args.Length)
    throw new ArgumentException("--report requires a path");

var reportPath = reportOption >= 0
    ? Path.GetFullPath(args[reportOption + 1], root)
    : Path.Combine(root, "artifacts", "local-validation", "e05", "modules-v0.2", "conformance.json");
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
