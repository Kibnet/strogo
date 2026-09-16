using System.Text;
using Strogo.Experiments;

if (args.Length == 0 || args[0] == "calibrate")
{
    string? output = args.Skip(1).FirstOrDefault(a => a.StartsWith("--report=", StringComparison.Ordinal))?.Split('=', 2)[1];
    var report = CalibrationEvaluator.Run();
    if (output is null) Console.WriteLine(Encoding.UTF8.GetString(CalibrationEvaluator.ReportBytes(report)));
    else { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); File.WriteAllBytes(output, CalibrationEvaluator.ReportBytes(report)); Console.WriteLine($"{report.Status}: {report.PositiveCases} positive, {report.NegativeCases} negative"); }
    return report.Status == "Accepted" ? 0 : 1;
}

if (args[0] == "export")
{
    string? caseId = args.FirstOrDefault(a => a.StartsWith("--case=", StringComparison.Ordinal))?.Split('=', 2)[1];
    string arm = args.FirstOrDefault(a => a.StartsWith("--arm=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? "graph-json";
    string directory = args.FirstOrDefault(a => a.StartsWith("--directory=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? "e09-export";
    var c = CalibrationCorpus.Create().FirstOrDefault(x => x.Id == caseId) ?? throw new ArgumentException("Unknown case");
    var bundle = Exporter.Export(c, arm); Directory.CreateDirectory(directory);
    File.WriteAllBytes(Path.Combine(directory, "job-starter.bin"), bundle.CandidateBytes);
    File.WriteAllBytes(Path.Combine(directory, "manifest.json"), Kernel.Core.CanonicalJson.Encode(bundle.Manifest));
    Console.WriteLine($"exported {c.Id} ({arm})"); return 0;
}

Console.Error.WriteLine("Usage: calibrate [--report=PATH] | export --case=R01-baseline [--arm=graph-json|strogo-notation] [--directory=PATH]");
return 2;
