using System.Text;
using System.Text.Json;
using Kernel.Core;
using Strogo.Experiments;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

var corpus = CalibrationCorpus.Create();
Check(corpus.Length == 12, "12 positive vectors");
var report = CalibrationEvaluator.Run();
Check(report.Status == "Accepted", "paired report accepted");
Check(report.Pairs.Length == 12 && report.Pairs.All(p => p.Equivalent), "all pairs equivalent");
Check(report.NegativeCases >= 20, "negative corpus >=20");
Check(report.NegativeCategories.Distinct().Count() == report.NegativeCategories.Length, "typed negative categories");

var opcodes = corpus.SelectMany(c => c.Program.Nodes).Select(n => n.Op).ToHashSet(StringComparer.Ordinal);
foreach (var op in GraphValidator.Opcodes) Check(opcodes.Contains(op) || op == "input", $"opcode coverage {op}");
Check(corpus.Single(c => c.Id == "R12-order").Program.Nodes.Select(n => n.Id).SequenceEqual(corpus[0].Program.Nodes.Select(n => n.Id)), "declaration order identity");
Check(corpus.Any(c => c.Id == "R10-min" && c.Inputs.Any(i => i.Available == long.MaxValue)), "i64 boundary row");
Check(corpus.Any(c => c.Id == "R11-max" && c.Inputs.Any(i => i.Available == long.MaxValue && i.Quantity == 1)), "overflow boundary row");

byte[] first = CalibrationEvaluator.ReportBytes(report); byte[] second = CalibrationEvaluator.ReportBytes(CalibrationEvaluator.Run());
Check(first.SequenceEqual(second), "deterministic report bytes");
foreach (var c in corpus)
foreach (var arm in new[] { "graph-json", "strogo-notation" })
{
    var bundle = Exporter.Export(c, arm); var expected = ProgramCodec.CanonicalBytes(c.Program);
    Check(Exporter.ValidateStarter(bundle, c, out var starterCode) && starterCode.Length == 0, $"starter fail-closed {c.Id}/{arm}");
    Check(!bundle.CandidateBytes.AsSpan().SequenceEqual(expected), $"starter differs {c.Id}/{arm}");
    Check(!Encoding.UTF8.GetString(bundle.CandidateBytes).Contains(CanonicalJson.RawDigest(expected), StringComparison.Ordinal), $"no expected digest leak {c.Id}/{arm}");
    Check(bundle.Manifest.Mode == "job-starter" && bundle.Manifest.DataOrigin == "scripted-fixture", $"manifest provenance {c.Id}/{arm}");
}

// Negative/refusal probes: parser and validator must fail closed before evaluation.
var malformed = CalibrationEvaluator.Evaluate(corpus[0] with { NotationSource = "{ // comment\n }" }, "strogo-notation");
Check(malformed.Status == "Refused", "comment refusal");
var invalidUtf8 = Strogo.Notation.NotationCompiler.Compile(new byte[] { 0xff });
Check(!invalidUtf8.Accepted && invalidUtf8.ErrorCode == "SourceEncodingInvalid", "invalid utf8 refusal");
var unknown = CalibrationEvaluator.Evaluate(corpus[0], "unknown-arm");
Check(unknown.Status == "Refused" && unknown.Stages.All(s => s.Stage != "graph-validator"), "unknown arm stops before graph");

var outPath = args.FirstOrDefault(a => a.StartsWith("--report=", StringComparison.Ordinal))?.Split('=', 2)[1];
if (outPath is not null) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!); File.WriteAllBytes(outPath, first); }
if (failures.Count > 0) { Console.Error.WriteLine(string.Join("\n", failures)); return 1; }
Console.WriteLine($"E09 PASS: {report.PositiveCases} positive pairs, {report.NegativeCases} negative categories, {first.Length} report bytes");
return 0;
