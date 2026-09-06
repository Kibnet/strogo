using System.Text;
using System.Text.Json;
using Strogo.Modules;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var fixtureDir = Path.Combine(root, "fixtures", "modules-v0.2");
var reportDir = Path.Combine(root, "artifacts", "local-validation", "e05", "modules-v0.2");
Directory.CreateDirectory(reportDir);

var reportPath = Path.Combine(reportDir, "conformance.json");
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

    try
    {
        _ = ModulesParser.ParseModule(bytes);
        throw new Exception($"Expected rejection: {file}");
    }
    catch (ModuleException ex)
    {
        Check(expectedCodes.Length == 0 || expectedCodes.Contains(ex.Code),
            $"Unexpected code for {file}: {ex.Code}. Expected {string.Join(',', expectedCodes)}");
        reports.Add(new { kind = "negative", fixture = file, code = ex.Code, stage = ex.Stage, detail = ex.DetailsJson });
    }
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

    reports.Add(new { kind = "positive", fixture = "math-add-valid.json", sourceDigest = valid.SourceDigest, irSourceSchema = ir.SourceSchema, functionCount = ir.Functions.Length });

    ExpectParserReject("math-invalid-op.json", "UnsupportedOpcode");
    ExpectParserReject("math-invalid-return-mismatch.json", "ReturnTypeMismatch");

    await File.WriteAllBytesAsync(reportPath, JsonSerializer.SerializeToUtf8Bytes(new
    {
        passed = true,
        checks,
        report = new { timestampUtc = DateTime.UtcNow, checks, fixtures = reports.Count }
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
