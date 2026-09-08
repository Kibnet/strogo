using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Kernel.Core;
using Strogo.Modules;
using Strogo.Modules.Portability;

if (args.Length > 0 && args[0] == "--jvm-process-fixture")
{
    RunJvmProcessFixture(args.Skip(1).ToArray());
    return;
}

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
var e06rReportOption = Array.IndexOf(args, "--e06r-report");
if (e06rReportOption >= 0 && e06rReportOption + 1 >= args.Length) throw new ArgumentException("--e06r-report requires a path");
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
var lowering = ModulesDafnyLowerer.LowerPortableValidation(module, owner);
var repeatedLowering = ModulesDafnyLowerer.LowerPortableValidation(repeated.Module, repeated.OwnerBundle);
var weakBinding = PortabilityContract.Bind(ModulesCompiler.Compile(ModulesParser.ParseModule(weakModuleBytes)), OwnerBundleV04Parser.Parse(ownerBytes));
var weakLowering = ModulesDafnyLowerer.LowerPortableValidation(weakBinding.Module, weakBinding.OwnerBundle);
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
Check(ModulesDafnyLowerer.PortableWireToolchainIdentity == "strogo.portable-wire-dafny-lowering.v0.1", "portable wire lowering version is fixed");
Check(lowering.SourceBytes.SequenceEqual(repeatedLowering.SourceBytes) && lowering.SourceDigest == repeatedLowering.SourceDigest, "mixed owner lowering is deterministic");
Check(lowering.ProofIdentity == repeatedLowering.ProofIdentity, "mixed owner proof identity is deterministic");
Check(lowering.SourceDigest != weakLowering.SourceDigest && binding.Module.SourceDigest != weakBinding.Module.SourceDigest, "strong and weak invariant identities differ");
Check(binding.PublicApiBytes.SequenceEqual(weakBinding.PublicApiBytes), "invariant strength does not change the public ABI");
Check(lowering.Obligations.Any(obligation => obligation.Kind == "call-contract" && obligation.Id == "call-adjust-incremented"), "adjust to increment call contract is an explicit obligation");
Check(lowering.Obligations.Count(obligation => obligation.Kind == "wire-success") == 5, "each portable success branch is linked to its owner model");
Check(lowering.Obligations.Any(obligation => obligation.Kind == "total-wrapper" && obligation.EntityId == "wire/wrapper/invoke"), "the requires-true wire wrapper is an explicit obligation");
Check(lowering.Source.Contains("method Invoke(functionId: string, arguments: seq<WireValue>)", StringComparison.Ordinal), "total wire wrapper is generated");
Check(lowering.Source.Contains("requires true", StringComparison.Ordinal), "wire wrapper requires true");
Check(lowering.SourceMap.Any(item => item.EntityId == "function/adjust/body/node/result/then/node/incremented"), "call source map entry is retained");
Check(PortabilityContract.IsExecutionProfile(PortabilityVersions.DotNetProfile), "dotnet profile is closed");
Check(PortabilityContract.IsExecutionProfile(PortabilityVersions.JvmProfile), "JVM profile is closed");
Check(!PortabilityContract.IsExecutionProfile("native"), "unknown profile is rejected");

var performanceProject = File.ReadAllText(Path.Combine(root, "tests", "fixtures", "portability-consumers", "csharp-performance", "PerformanceConsumer.csproj"));
var performanceProgram = File.ReadAllText(Path.Combine(root, "tests", "fixtures", "portability-consumers", "csharp-performance", "Program.cs"));
var performanceDriver = File.ReadAllText(Path.Combine(root, "tools", "Test-PortableDotNet-Performance.ps1"));
var performanceLinuxDriver = File.ReadAllText(Path.Combine(root, "tools", "Test-PortableDotNet-Performance.sh"));
Check(performanceProject.Contains("<Reference Include=\"Strogo.Portable.V01\">", StringComparison.Ordinal) && !performanceProject.Contains("ProjectReference", StringComparison.Ordinal), "performance consumer uses only the public packaged assembly");
Check(performanceProgram.Contains("const int warmupCalls = 5_000;", StringComparison.Ordinal) && performanceProgram.Contains("const int repeats = 5;", StringComparison.Ordinal) && performanceProgram.Contains("const int callsPerRepeat = 10_000;", StringComparison.Ordinal), "performance workload fixes warmup, repetitions, and calls");
Check(performanceProgram.Contains("ModuleApi.Invoke(request)", StringComparison.Ordinal) && performanceProgram.Contains("operationsPerSecond", StringComparison.Ordinal), "performance workload measures the public JSON ABI and reports throughput");
Check(performanceDriver.Contains("WaitForExit(180000)", StringComparison.Ordinal) && performanceDriver.Contains("assertionBoundary='DiagnosticOnlyNoG06'", StringComparison.Ordinal), "performance process is bounded and cannot assert G06");
Check(performanceDriver.Contains("$null=$startInfo.Environment.Remove($name)", StringComparison.Ordinal) && performanceDriver.Contains("-expected-runtime-closure-digest $ExpectedRuntimeClosureDigest", StringComparison.OrdinalIgnoreCase), "performance driver clears diagnostic JIT overrides and verifies the exact runtime closure");
Check(performanceLinuxDriver.Contains("environment.pop(name, None)", StringComparison.Ordinal) && performanceLinuxDriver.Contains("--expected-runtime-closure-digest \"$expected_runtime\"", StringComparison.Ordinal), "Linux performance driver clears diagnostic JIT overrides and verifies the exact runtime closure");
Check(performanceLinuxDriver.Contains("time.monotonic_ns()", StringComparison.Ordinal) && performanceLinuxDriver.Contains("communicate(timeout=180)", StringComparison.Ordinal) && performanceLinuxDriver.Contains("DiagnosticOnlyNoG06", StringComparison.Ordinal), "Linux performance process is measured, bounded, and cannot assert G06");

var javaAdapter = File.ReadAllText(Path.Combine(root, "targets", "jvm-java17-v1", "strogo", "portable", "v01", "ModuleApi.java"));
var javaConsumer = File.ReadAllText(Path.Combine(root, "tests", "fixtures", "portability-consumers", "java", "Consumer.java"));
Check(javaAdapter.Contains("public static String invoke(String canonicalRequestJson)", StringComparison.Ordinal) && javaAdapter.Contains("PortableWrapper.__default.Invoke", StringComparison.Ordinal), "Java public ABI delegates to the verified total wrapper");
Check(javaAdapter.Contains("MAXIMUM_INPUT_BYTES = 65536", StringComparison.Ordinal) && javaAdapter.Contains("MAXIMUM_DEPTH = 32", StringComparison.Ordinal) && javaAdapter.Contains("MAXIMUM_VALUES = 2048", StringComparison.Ordinal), "Java adapter fixes all transport resource limits");
Check(javaAdapter.Contains("SyntaxInspector.inspect", StringComparison.Ordinal) && javaAdapter.Contains("if (!canonicalRequestJson.equals(canonical))", StringComparison.Ordinal), "Java adapter separates bounded syntax inspection from canonical transport validation");
Check(!new[] { "com.fasterxml", "org.json", "javax.json", "java.lang.reflect", "ServiceLoader", "System.load" }.Any(javaAdapter.Contains), "Java adapter has no external JSON, reflection, service loading, or JNI dependency");
Check(javaConsumer.Contains("PASS standalone Java consumer cases=", StringComparison.Ordinal) && javaConsumer.Contains("transport=", StringComparison.Ordinal) && javaConsumer.Contains("expected 13 vectors", StringComparison.Ordinal), "standalone Java consumer covers ABI, transport, and owner vectors");

var jvmIdentity = new JvmWarningBaselineIdentity(new string('1', 64), new string('2', 64), new string('3', 64), new string('4', 64), new string('5', 64), new string('6', 64), new string('7', 64));
var jvmSources = new[] { (Path: "generated/Generated.java", Bytes: Encoding.ASCII.GetBytes("final class Generated {}\n")) };
var jvmLf = SyntheticJvmDiagnostics("\n", '/');
var jvmCrLf = SyntheticJvmDiagnostics("\r\n", '\\');
var jvmCandidate = JvmUpstreamWarnings.CreateCandidate(jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf));
var jvmWindowsCandidate = JvmUpstreamWarnings.CreateCandidate(jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmCrLf));
Check(jvmCandidate.Bytes.SequenceEqual(jvmWindowsCandidate.Bytes) && jvmCandidate.CanonicalDiagnostics.SequenceEqual(jvmWindowsCandidate.CanonicalDiagnostics), "JVM warning baseline normalizes CRLF and relative Windows paths");
Check(jvmCandidate.Warnings.Length == 106 && jvmCandidate.Warnings.Count(item => item.Category == "cast") == 35 && jvmCandidate.Warnings.Count(item => item.Category == "rawtypes") == 67 && jvmCandidate.Warnings.Count(item => item.Category == "serial") == 1 && jvmCandidate.Warnings.Count(item => item.Category == "varargs") == 3, "JVM warning baseline freezes all 106 categories");
var jvmWarningKeys = jvmCandidate.Warnings.Select(item => $"{item.Path}\0{item.Line}\0{item.Category}\0{item.Message}\0{item.ContextDigest}").ToArray();
Check(jvmWarningKeys.SequenceEqual(jvmWarningKeys.Order(StringComparer.Ordinal), StringComparer.Ordinal), "JVM warning entries use canonical ordinal tuple order");
Check(JvmUpstreamWarnings.Validate(jvmCandidate.Bytes, jvmCandidate.BaselineDigest, jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf)).BaselineDigest == jvmCandidate.BaselineDigest, "owner-approved JVM baseline digest validates exact observation");
var approvedBaselinePath = Path.Combine(root, "targets", "jvm-java17-v1", "upstream-warning-baseline.json");
var approvedBaselineBytes = File.ReadAllBytes(approvedBaselinePath);
Check(PortabilityContract.DomainHash($"{JvmUpstreamWarnings.SchemaVersion}/artifact", approvedBaselineBytes) == "cafa0caad40e22d1d2ff3803ea350dea95f01c99f099ada75b16ef228035c0e1", "tracked JVM baseline is bound to the owner-approved digest");
foreach (var mutation in new[]
{
    (Name: "fixture", Identity: jvmIdentity with { FixtureModuleDigest = new string('8', 64) }),
    (Name: "Dafny source", Identity: jvmIdentity with { DafnySourceDigest = new string('8', 64) }),
    (Name: "translator", Identity: jvmIdentity with { TranslatorDigest = new string('8', 64) }),
    (Name: "javac closure", Identity: jvmIdentity with { JavacClosureDigest = new string('8', 64) }),
    (Name: "command", Identity: jvmIdentity with { ProbeCommandDigest = new string('8', 64) }),
    (Name: "validator", Identity: jvmIdentity with { ValidatorDigest = new string('8', 64) }),
    (Name: "harness", Identity: jvmIdentity with { HarnessDigest = new string('8', 64) })
})
    Check(RejectsTargetBuild(() => JvmUpstreamWarnings.Validate(jvmCandidate.Bytes, jvmCandidate.BaselineDigest, mutation.Identity, jvmSources, Encoding.UTF8.GetBytes(jvmLf)), "UpstreamWarningBaselineMismatch"), $"JVM {mutation.Name} identity drift cannot reuse the approved baseline");
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.Validate(jvmCandidate.Bytes, new string('9', 64), jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf)), "UpstreamWarningBaselineMismatch"), "self-generated JVM baseline digest cannot replace the owner-approved digest");
var coordinatedIdentity = jvmIdentity with { ValidatorDigest = new string('8', 64), HarnessDigest = new string('9', 64) };
var coordinatedCandidate = JvmUpstreamWarnings.CreateCandidate(coordinatedIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf));
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.Validate(coordinatedCandidate.Bytes, jvmCandidate.BaselineDigest, coordinatedIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf)), "UpstreamWarningBaselineMismatch"), "coordinated baseline and validator/harness rewrite cannot replace the external owner digest");
var jvmSourceMutation = new[] { (Path: "generated/Generated.java", Bytes: Encoding.ASCII.GetBytes("final class Generated { int changed; }\n")) };
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.Validate(jvmCandidate.Bytes, jvmCandidate.BaselineDigest, jvmIdentity, jvmSourceMutation, Encoding.UTF8.GetBytes(jvmLf)), "UpstreamWarningBaselineMismatch"), "JVM translated source mutation cannot reuse the approved baseline");
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.Validate(jvmCandidate.Bytes, jvmCandidate.BaselineDigest, jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf.Replace("source context 1", "changed context 1", StringComparison.Ordinal))), "UpstreamWarningBaselineMismatch"), "JVM warning context mutation is rejected by the approved baseline");
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.Validate(jvmCandidate.Bytes, jvmCandidate.BaselineDigest, jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf.Replace("warning 1\n", "changed warning 1\n", StringComparison.Ordinal))), "UpstreamWarningBaselineMismatch"), "JVM primary warning mutation is rejected by the approved baseline");
var jvmDiagnosticLines = jvmLf.Split('\n').ToList();
jvmDiagnosticLines.RemoveRange(0, 3);
jvmDiagnosticLines[^2] = "105 warnings";
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.CreateCandidate(jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(string.Join('\n', jvmDiagnosticLines))), "UpstreamWarningBaselineMismatch"), "removed JVM primary warning is rejected");
var jvmAddedLines = jvmLf.Split('\n').ToList();
jvmAddedLines.InsertRange(jvmAddedLines.Count - 2, jvmLf.Split('\n').Take(3));
jvmAddedLines[^2] = "107 warnings";
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.CreateCandidate(jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(string.Join('\n', jvmAddedLines))), "UpstreamWarningBaselineMismatch"), "added JVM primary warning is rejected");
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.CreateCandidate(jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf.Replace("106 warnings\n", "105 warnings\n", StringComparison.Ordinal))), "UnexpectedUpstreamDiagnostic"), "JVM warning summary mismatch is rejected");
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.CreateCandidate(jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf + "only showing the first 100 warnings\n")), "UnexpectedUpstreamDiagnostic"), "JVM warning truncation text is rejected");
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.CreateCandidate(jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf.Replace("generated/Generated.java:1", "C:/Generated.java:1", StringComparison.Ordinal))), "UnexpectedUpstreamDiagnostic"), "absolute JVM diagnostic path is rejected");
Check(JvmUpstreamWarnings.CreateCandidate(jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(SyntheticJvmDiagnostics("\r", '/'))).Bytes.SequenceEqual(jvmCandidate.Bytes), "standalone CR diagnostics normalize canonically");
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.Validate(jvmCandidate.Bytes, jvmCandidate.BaselineDigest, jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf.Replace("  ^\n", "  ^~~~\n", StringComparison.Ordinal))), "UpstreamWarningBaselineMismatch"), "caret mutation is rejected by the approved JVM baseline");
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.Validate(jvmCandidate.Bytes, jvmCandidate.BaselineDigest, jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf.Replace("  source context 1\n", "  source context 1\n  continuation\n", StringComparison.Ordinal))), "UpstreamWarningBaselineMismatch"), "continuation mutation is rejected by the approved JVM baseline");
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.CreateCandidate(jvmIdentity, jvmSources, Encoding.UTF8.GetBytes("unconsumed\n" + jvmLf)), "UnexpectedUpstreamDiagnostic"), "unconsumed diagnostic prefix is rejected");
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.CreateCandidate(jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf.Replace("generated/Generated.java:1", "generated/../Generated.java:1", StringComparison.Ordinal))), "UnexpectedUpstreamDiagnostic"), "parent path segment is rejected");
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.CreateCandidate(jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf.Replace("generated/Generated.java:1", "/generated/Generated.java:1", StringComparison.Ordinal))), "UnexpectedUpstreamDiagnostic"), "rooted Unix diagnostic path is rejected");
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.CreateCandidate(jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf.TrimEnd('\n'))), "UnexpectedUpstreamDiagnostic"), "unterminated diagnostics are rejected");
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.CreateCandidate(jvmIdentity, jvmSources, [0xff, 0xfe]), "UnexpectedUpstreamDiagnostic"), "invalid UTF-8 diagnostics are rejected");
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.CreateCandidate(jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf.Replace("106 warnings", new string('9', 128) + " warnings", StringComparison.Ordinal))), "UnexpectedUpstreamDiagnostic"), "oversized warning count is a typed rejection");
Check(RejectsTargetBuild(() => JvmUpstreamWarnings.CreateCandidate(jvmIdentity, jvmSources, Encoding.UTF8.GetBytes(jvmLf.Replace("Generated.java:1:", $"Generated.java:{new string('9', 128)}:", StringComparison.Ordinal))), "UnexpectedUpstreamDiagnostic"), "oversized source line is a typed rejection");

var processExecutable = Environment.ProcessPath ?? throw new InvalidOperationException("process executable unavailable");
var processRoot = Path.Combine(Path.GetTempPath(), "strogo-jvm-runner-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(processRoot);
try
{
    Check(RejectsTargetBuild(() => JvmProcessRunner.Run(ProcessRequest(Path.Combine(processRoot, "missing-javac.exe"), processRoot, [], 1024, 5000)), "JavacProbeFailed"), "JVM runner maps unexpected probe startup failure to the closed stage reason");
    var stdoutOverflow = CaptureTargetBuild(() => JvmProcessRunner.Run(ProcessRequest(processExecutable, processRoot, ["--jvm-process-fixture", "stdout"], 1024, 5000)));
    Check(stdoutOverflow.Reason == "JavacOutputLimitExceeded", "JVM runner rejects stdout overflow");
    var stdoutOverflowDetails = stdoutOverflow.Details;
    Check(stdoutOverflowDetails.GetProperty("failureDetails").GetProperty("actualAtDetection").GetString() == "1025" && stdoutOverflowDetails.GetProperty("cleanup").GetString() == "Passed", "JVM overflow evidence fixes first excess byte and cleanup result");
    Check(stdoutOverflowDetails.GetProperty("stdout").GetProperty("bytesRead").GetString() == "2048" && stdoutOverflowDetails.GetProperty("stdout").GetProperty("prefixBytes").GetString() == "1024" && stdoutOverflowDetails.GetProperty("stdout").GetProperty("digest").GetString()!.Length == 64, "JVM overflow evidence retains bounded prefix, byte count, and digest");
    Check(stdoutOverflowDetails.GetProperty("knownProcessTree").GetArrayLength() >= 1 && stdoutOverflowDetails.GetProperty("liveProcessIds").GetArrayLength() == 0, "JVM overflow evidence records process identity and no live residual");
    Check(RejectsTargetBuild(() => JvmProcessRunner.Run(ProcessRequest(processExecutable, processRoot, ["--jvm-process-fixture", "stderr"], 1024, 5000)), "JavacOutputLimitExceeded"), "JVM runner rejects stderr overflow");
    var childPidFile = Path.Combine(processRoot, "child.pid");
    Check(RejectsTargetBuild(() => JvmProcessRunner.Run(ProcessRequest(processExecutable, processRoot, ["--jvm-process-fixture", "tree", childPidFile], 4096, 500)), "JavacTimeout"), "JVM runner rejects root and child timeout");
    var childPid = int.Parse(File.ReadAllText(childPidFile), System.Globalization.CultureInfo.InvariantCulture);
    Check(!ProcessAlive(childPid), "JVM runner reaps the timeout child process");
    var floodChildPidFile = Path.Combine(processRoot, "flood-child.pid");
    Check(RejectsTargetBuild(() => JvmProcessRunner.Run(ProcessRequest(processExecutable, processRoot, ["--jvm-process-fixture", "tree-flood", floodChildPidFile], 1024, 5000)), "JavacOutputLimitExceeded"), "JVM runner rejects descendant output overflow");
    var floodChildPid = int.Parse(File.ReadAllText(floodChildPidFile), System.Globalization.CultureInfo.InvariantCulture);
    Check(!ProcessAlive(floodChildPid), "JVM runner reaps the overflowing descendant process");
    var quarantine = Path.Combine(processRoot, "run", "probe");
    Directory.CreateDirectory(quarantine);
    File.WriteAllText(Path.Combine(quarantine, "partial.class"), "partial", Encoding.ASCII);
    var quarantineSibling = Path.Combine(processRoot, "run", "unrelated");
    Directory.CreateDirectory(quarantineSibling);
    File.WriteAllText(Path.Combine(quarantineSibling, "keep.txt"), "keep", Encoding.ASCII);
    JvmQuarantine.Delete(Path.Combine(processRoot, "run"), quarantine);
    Check(!Directory.Exists(quarantine) && File.Exists(Path.Combine(quarantineSibling, "keep.txt")), "JVM quarantine deletes only the exact contained output target");
    foreach (var failureReason in new[] { "JavacProbeFailed", "UpstreamWarningBaselineMismatch", "JavacTimeout", "JavacOutputLimitExceeded" })
    {
        var failedQuarantine = Path.Combine(processRoot, "run", "failed-" + failureReason);
        Check(RejectsTargetBuild(() => JvmQuarantine.Execute<bool>(Path.Combine(processRoot, "run"), failedQuarantine, () =>
        {
            Directory.CreateDirectory(failedQuarantine);
            File.WriteAllText(Path.Combine(failedQuarantine, "partial.class"), "partial", Encoding.ASCII);
            throw new PortabilityContractException("TargetBuildRejected", "$/fixture", new { reason = failureReason });
        }), failureReason), $"JVM quarantine preserves typed {failureReason} after successful cleanup");
        Check(!Directory.Exists(failedQuarantine), $"JVM quarantine removes partial output after {failureReason}");
    }
    Check(RejectsTargetBuild(() => JvmQuarantine.Delete(Path.Combine(processRoot, "run"), processRoot), "ProbeCleanupFailed"), "JVM quarantine rejects cleanup outside the run root");
    if (OperatingSystem.IsWindows())
    {
        var lockedQuarantine = Path.Combine(processRoot, "run", "locked-probe");
        Directory.CreateDirectory(lockedQuarantine);
        var readyFile = Path.Combine(processRoot, "cwd-lock.ready");
        var lockProcess = StartJvmFixture(processExecutable, lockedQuarantine, "cwd-lock", false, readyFile);
        try
        {
            for (var attempt = 0; attempt < 100 && !File.Exists(readyFile); attempt++) Thread.Sleep(20);
            Check(File.Exists(readyFile), "JVM cleanup failure fixture entered the quarantine directory");
            var cleanupFailure = CaptureTargetBuild(() => JvmQuarantine.Execute<bool>(Path.Combine(processRoot, "run"), lockedQuarantine, () => throw new PortabilityContractException("TargetBuildRejected", "$/fixture", new { reason = "JavacTimeout" })));
            Check(cleanupFailure.Reason == "ProbeCleanupFailed", "JVM quarantine gives cleanup failure priority over the original failure");
            Check(cleanupFailure.Details.GetProperty("cleanupFailure").GetProperty("residualPath").GetString() == lockedQuarantine && cleanupFailure.Details.GetProperty("originalFailure").GetProperty("details").GetProperty("reason").GetString() == "JavacTimeout", "JVM cleanup failure evidence retains residual path and original reason");
            Check(Directory.Exists(lockedQuarantine), "JVM quarantine cleanup failure retains the residual path as evidence");
        }
        finally
        {
            if (!lockProcess.HasExited) lockProcess.Kill(entireProcessTree: true);
            lockProcess.WaitForExit();
            lockProcess.Dispose();
            if (Directory.Exists(lockedQuarantine)) JvmQuarantine.Delete(Path.Combine(processRoot, "run"), lockedQuarantine);
        }
    }
}
finally
{
    if (Directory.Exists(processRoot)) JvmQuarantine.Delete(Path.GetTempPath(), processRoot);
}

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

var runtimeRequirementBytes = File.ReadAllBytes(Path.Combine(fixtureDir, "dotnet-runtime-requirement.json"));
var runtimeRequirementDigest = PortabilityContract.DomainHash($"strogo.portability.v0.1/runtime-requirement/{PortabilityVersions.DotNetProfile}", runtimeRequirementBytes);
var translatorInventoryBytes = CanonicalJson.Encode(new
{
    schemaVersion = "strogo.tool-inventory.v0.1",
    profileId = PortabilityVersions.DotNetProfile,
    files = new[] { new { path = "tools/dafny", sha256 = new string('a', 64), length = "1", role = "executable" } },
    versions = new[] { new { component = "dafny", version = "4.11.0" } }
});
var buildToolchainInventoryBytes = CanonicalJson.Encode(new
{
    schemaVersion = "strogo.tool-inventory.v0.1",
    profileId = PortabilityVersions.DotNetProfile,
    files = new[] { new { path = "tools/dotnet", sha256 = new string('b', 64), length = "1", role = "executable" } },
    versions = new[] { new { component = "dotnet-sdk", version = "10.0.400" } }
});
var canonicalModuleBytes = parsed.CanonicalSource;
var canonicalOwnerBytes = OwnerBundleV04Codec.Canonicalize(owner.BundleId, owner.Types, owner.EntryContracts, owner.Models, owner.Limits);
var sourceMapBytes = CanonicalJson.Encode(lowering.SourceMap);
var proofToolchainBytes = CanonicalJson.Encode(new
{
    files = new[] { new { path = "tools/dafny", sha256 = new string('c', 64), length = "1", role = "executable" } },
    versions = new[] { new { id = "dafny", value = "4.11.0" } }
});
var proofClosureBytes = CanonicalJson.Encode(new
{
    files = new[] { new { path = "closure/dafny", sha256 = new string('d', 64), length = "1", role = "runtime" } },
    versions = new[] { new { id = "dafny-closure", value = "4.11.0-linux-x64" } }
});
var proofSourceFile = new PortabilityPackageFile(
    "content/candidate.dfy",
    CanonicalJson.RawDigest(lowering.SourceBytes),
    lowering.SourceBytes.LongLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
    "proof-source");
var proofTranscriptBytes = CanonicalJson.Encode(new
{
    schemaVersion = "strogo.validation-proof-transcript.v0.1",
    outcome = "Verified",
    runs = new[]
    {
        new { lane = "a", verified = "42", errors = "0" },
        new { lane = "b", verified = "42", errors = "0" }
    },
    twoCleanReplay = "ByteEqual"
});
var validationProof = PortabilityValidationProof.Build(new(
    binding.Module.SourceDigest,
    binding.OwnerBundle.BundleDigest,
    PortabilityValidationProof.ToolchainDigest(proofToolchainBytes),
    PortabilityValidationProof.ClosureDigest(proofClosureBytes),
    PortabilityValidationProof.ProofSourcesDigest([proofSourceFile]),
    PortabilityValidationProof.TranscriptDigest(proofTranscriptBytes),
    PortabilityValidationProof.SourceMapDigest(sourceMapBytes),
    "Verified",
    lowering.Obligations.Select(obligation => new PortabilityValidationProofObligation(
        obligation.Id,
        obligation.Kind,
        "Verified",
        PortabilityValidationProof.EvidenceDigest(obligation.Kind, CanonicalJson.Encode(new
        {
            obligationId = obligation.Id,
            entityId = obligation.EntityId,
            line = obligation.Line.ToString(System.Globalization.CultureInfo.InvariantCulture)
        })))).ToArray()));
Check(validationProof.Digest != lowering.ProofIdentity, "validation proof artifact digest is distinct from lowering proof identity");
using (var validationProofDocument = JsonDocument.Parse(validationProof.Bytes))
    Fields(validationProofDocument.RootElement, "schemaVersion", "contractStatus", "moduleDigest", "bundleDigest", "toolchainDigest", "closureDigest", "proofSourcesDigest", "transcriptDigest", "sourceMapDigest", "outcome", "obligations");
var packageContent = new List<PortabilityPackageContent>
{
    new("content/module.json", "module", canonicalModuleBytes),
    new("content/owner.json", "bundle", canonicalOwnerBytes),
    new("content/proof.json", "proof", validationProof.Bytes),
    new("content/candidate.dfy", "proof-source", lowering.SourceBytes),
    new("content/source-map.json", "source-map", sourceMapBytes),
    new("content/proof-toolchain.json", "metadata", proofToolchainBytes),
    new("content/proof-closure.json", "metadata", proofClosureBytes),
    new("content/proof-transcript.json", "metadata", proofTranscriptBytes),
    new("content/strogo.portable.v01.dll", "entry-artifact", Encoding.ASCII.GetBytes("deterministic-test-assembly")),
    new("content/module-api.cs", "adapter", Encoding.ASCII.GetBytes("namespace Strogo.Portable.V01;")),
    new("content/public-api.json", "metadata", binding.PublicApiBytes),
    new("content/runtime-requirement.json", "metadata", runtimeRequirementBytes),
    new("content/translator-inventory.json", "metadata", translatorInventoryBytes),
    new("content/build-toolchain-inventory.json", "metadata", buildToolchainInventoryBytes)
};
var packageDefinition = new PortabilityPackageDefinition(
    PortabilityVersions.DotNetProfile,
    binding.Module.SourceDigest,
    binding.OwnerBundle.BundleDigest,
    validationProof.Digest,
    lowering.SourceDigest,
    PortabilityContract.DomainHash($"strogo.portability.v0.1/translator/{PortabilityVersions.DotNetProfile}", translatorInventoryBytes),
    PortabilityContract.DomainHash($"strogo.portability.v0.1/build-toolchain/{PortabilityVersions.DotNetProfile}", buildToolchainInventoryBytes),
    runtimeRequirementDigest,
    binding.PublicApiDigest,
    "content/strogo.portable.v01.dll",
    packageContent);
var contentCopy = packageContent[0].Bytes;
contentCopy[0] ^= 0xff;
Check(packageContent[0].Bytes.SequenceEqual(canonicalModuleBytes), "package content bytes are defensively copied");
var packageTemp = Path.Combine(Path.GetTempPath(), "strogo-portability-package-" + Guid.NewGuid().ToString("N"));
try
{
    var packageA = Path.Combine(packageTemp, "root-a", "package");
    var packageB = Path.Combine(packageTemp, "root-b", "unrelated", "package");
    var receiptA = PortabilityPackage.Build(packageA, packageDefinition);
    var receiptB = PortabilityPackage.Build(packageB, packageDefinition);
    var validatedA = PortabilityPackage.Validate(packageA, receiptA.PortabilityManifestDigest);
    Check(receiptA.ProfileId == validatedA.ProfileId && receiptA.ArtifactDigest == validatedA.ArtifactDigest && receiptA.PackageDigest == validatedA.PackageDigest && receiptA.PortabilityManifestDigest == validatedA.PortabilityManifestDigest && receiptA.Files.SequenceEqual(validatedA.Files), "package validation receipt is stable");
    Check(receiptA.ArtifactDigest == receiptB.ArtifactDigest && receiptA.PackageDigest == receiptB.PackageDigest && receiptA.PortabilityManifestDigest == receiptB.PortabilityManifestDigest, "package digests exclude physical root");
    Check(EqualTrees(packageA, packageB), "package trees are byte equal across physical roots");
    Check(Directory.EnumerateFileSystemEntries(packageA).Select(Path.GetFileName).Order(StringComparer.Ordinal).SequenceEqual(new[] { "content", "portability-manifest.json" }, StringComparer.Ordinal), "package root is closed");

    var entryArtifact = Path.Combine(packageA, "content", "strogo.portable.v01.dll");
    var entryBytes = File.ReadAllBytes(entryArtifact);
    File.WriteAllBytes(entryArtifact, [.. entryBytes, (byte)0]);
    Check(RejectsPackage(() => PortabilityPackage.Validate(packageA, receiptA.PortabilityManifestDigest)), "entry artifact tamper is rejected");
    File.WriteAllBytes(entryArtifact, entryBytes);

    var extraRoot = Path.Combine(packageA, "admission.json");
    File.WriteAllText(extraRoot, "{}", Encoding.ASCII);
    Check(RejectsPackage(() => PortabilityPackage.Validate(packageA, receiptA.PortabilityManifestDigest)), "extra root file is rejected");
    File.Delete(extraRoot);

    var emptyDirectory = Path.Combine(packageA, "content", "unexpected");
    Directory.CreateDirectory(emptyDirectory);
    Check(RejectsPackage(() => PortabilityPackage.Validate(packageA, receiptA.PortabilityManifestDigest)), "unlisted directory is rejected");
    Directory.Delete(emptyDirectory);

    if (!OperatingSystem.IsWindows())
    {
        var symbolicLink = Path.Combine(packageA, "content", "linked-artifact.dll");
        File.CreateSymbolicLink(symbolicLink, entryArtifact);
        Check(RejectsPackage(() => PortabilityPackage.Validate(packageA, receiptA.PortabilityManifestDigest)), "symbolic link is rejected before content read");
        File.Delete(symbolicLink);
    }

    var badProfile = packageDefinition with { ProfileId = PortabilityVersions.JvmProfile };
    Check(RejectsPackage(() => PortabilityPackage.Build(Path.Combine(packageTemp, "bad-profile"), badProfile)), "profile and entry artifact swap is rejected");
    var traversal = packageDefinition with { Content = [.. packageContent, new PortabilityPackageContent("content/../escape", "metadata", [])] };
    Check(RejectsPackage(() => PortabilityPackage.Build(Path.Combine(packageTemp, "traversal"), traversal)), "path traversal is rejected before write");
    var duplicate = packageDefinition with { Content = [.. packageContent, new PortabilityPackageContent("content/module.json", "metadata", [])] };
    Check(RejectsPackage(() => PortabilityPackage.Build(Path.Combine(packageTemp, "duplicate"), duplicate)), "duplicate path is rejected before write");
    var uppercase = packageDefinition with { Content = [.. packageContent.Where(file => file.Path != "content/module-api.cs"), new PortabilityPackageContent("content/Module-Api.cs", "adapter", [])] };
    Check(RejectsPackage(() => PortabilityPackage.Build(Path.Combine(packageTemp, "uppercase"), uppercase)), "non-lowercase path is rejected before write");
    var missingSourceMap = packageDefinition with { Content = packageContent.Where(file => file.Role != "source-map").ToArray() };
    Check(RejectsPackage(() => PortabilityPackage.Build(Path.Combine(packageTemp, "missing-source-map"), missingSourceMap)), "missing required role is rejected before write");
    var unknownRole = packageDefinition with { Content = packageContent.Select(file => file.Role == "adapter" ? new PortabilityPackageContent(file.Path, "candidate-code", file.Bytes) : file).ToArray() };
    Check(RejectsPackage(() => PortabilityPackage.Build(Path.Combine(packageTemp, "unknown-role"), unknownRole)), "unknown content role is rejected before write");

    var wrongDigest = new string('0', 64);
    var identityMutations = new (string Id, PortabilityPackageDefinition Definition)[]
    {
        ("module", packageDefinition with { ModuleDigest = wrongDigest }),
        ("owner", packageDefinition with { OwnerBundleDigest = wrongDigest }),
        ("proof", packageDefinition with { ProofDigest = wrongDigest }),
        ("dafny", packageDefinition with { DafnySourceDigest = wrongDigest }),
        ("translator", packageDefinition with { TranslatorDigest = wrongDigest }),
        ("build-toolchain", packageDefinition with { BuildToolchainDigest = wrongDigest }),
        ("runtime-requirement", packageDefinition with { RuntimeRequirementDigest = wrongDigest }),
        ("public-api", packageDefinition with { PublicApiDigest = wrongDigest })
    };
    foreach (var identityMutation in identityMutations)
        Check(RejectsPackage(() => PortabilityPackage.Build(Path.Combine(packageTemp, "wrong-" + identityMutation.Id), identityMutation.Definition)), $"{identityMutation.Id} identity mismatch is rejected before write");

    var changedAdapterContent = packageContent.Select(file => file.Path == "content/module-api.cs" ? new PortabilityPackageContent(file.Path, file.Role, Encoding.ASCII.GetBytes("namespace Strogo.Portable.Mutated;")) : file).ToArray();
    var changedAdapterPackage = Path.Combine(packageTemp, "changed-adapter");
    var changedAdapterReceipt = PortabilityPackage.Build(changedAdapterPackage, packageDefinition with { Content = changedAdapterContent });
    Check(changedAdapterReceipt.PortabilityManifestDigest != receiptA.PortabilityManifestDigest && PortabilityPackage.Validate(changedAdapterPackage, changedAdapterReceipt.PortabilityManifestDigest).PortabilityManifestDigest == changedAdapterReceipt.PortabilityManifestDigest, "self-consistent changed package has a distinct identity");
    Check(RejectsPackage(() => PortabilityPackage.Validate(changedAdapterPackage, receiptA.PortabilityManifestDigest)), "changed package is rejected against expected manifest identity");

    var legacyProofBytes = CanonicalJson.Encode(new { schemaVersion = PortabilityValidationProof.SchemaVersion, proofIdentity = lowering.ProofIdentity });
    var legacyProofContent = packageContent.Select(file => file.Path == "content/proof.json" ? new PortabilityPackageContent(file.Path, file.Role, legacyProofBytes) : file).ToArray();
    var legacyProofDefinition = packageDefinition with
    {
        ProofDigest = PortabilityValidationProof.Digest(legacyProofBytes),
        Content = legacyProofContent
    };
    Check(RejectsPackage(() => PortabilityPackage.Build(Path.Combine(packageTemp, "legacy-proof"), legacyProofDefinition)), "legacy proof identity surrogate is rejected even with recomputed outer digest");

    var changedSourceMapContent = packageContent.Select(file => file.Path == "content/source-map.json" ? new PortabilityPackageContent(file.Path, file.Role, CanonicalJson.Encode(Array.Empty<object>())) : file).ToArray();
    Check(RejectsPackage(() => PortabilityPackage.Build(Path.Combine(packageTemp, "changed-source-map"), packageDefinition with { Content = changedSourceMapContent })), "proof rejects a changed source map even when package hashes are recomputed");

    var changedProofToolchainBytes = CanonicalJson.Encode(new
    {
        files = new[] { new { path = "tools/dafny", sha256 = new string('e', 64), length = "1", role = "executable" } },
        versions = new[] { new { id = "dafny", value = "4.11.0" } }
    });
    var changedProofToolchainContent = packageContent.Select(file => file.Path == "content/proof-toolchain.json" ? new PortabilityPackageContent(file.Path, file.Role, changedProofToolchainBytes) : file).ToArray();
    Check(RejectsPackage(() => PortabilityPackage.Build(Path.Combine(packageTemp, "changed-proof-toolchain"), packageDefinition with { Content = changedProofToolchainContent })), "proof rejects a changed verifier inventory even when package hashes are recomputed");

    var wrongRuntimeBytes = CanonicalJson.Encode(new { schemaVersion = PortabilityPackageVersions.RuntimeRequirementSchema, profileId = PortabilityVersions.JvmProfile, vmFamily = "java", vmMajor = "17", dynamicFeatures = new[] { "jit" }, nativeFeatures = Array.Empty<string>() });
    var wrongRuntimeContent = packageContent.Select(file => file.Path == "content/runtime-requirement.json" ? new PortabilityPackageContent(file.Path, file.Role, wrongRuntimeBytes) : file).ToArray();
    var wrongRuntime = packageDefinition with
    {
        RuntimeRequirementDigest = PortabilityContract.DomainHash($"strogo.portability.v0.1/runtime-requirement/{PortabilityVersions.DotNetProfile}", wrongRuntimeBytes),
        Content = wrongRuntimeContent
    };
    Check(RejectsPackage(() => PortabilityPackage.Build(Path.Combine(packageTemp, "wrong-runtime"), wrongRuntime)), "runtime requirement profile swap is rejected");

    var packageMethods = typeof(PortabilityPackage).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly).Select(method => method.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    Check(packageMethods.SequenceEqual(new[] { "Build", "Validate" }, StringComparer.Ordinal), "package API exposes only build and validation operations");
    var dotnetTargetProject = File.ReadAllText(Path.Combine(root, "targets", "dotnet-managed-v1", "Strogo.Portable.V01.csproj"));
    Check(dotnetTargetProject.Contains("<GenerateMSBuildEditorConfigFile>false</GenerateMSBuildEditorConfigFile>", StringComparison.Ordinal), "dotnet target excludes physical ProjectDir from generated compiler inputs");
    Check(dotnetTargetProject.Contains("<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>", StringComparison.Ordinal), "dotnet target pins overflow checks independently of ancestor props");
    var dotnetBuildDriver = File.ReadAllText(Path.Combine(root, "tools", "Build-PortableDotNet.sh"));
    Check(dotnetBuildDriver.Contains("-p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false", StringComparison.Ordinal), "dotnet build excludes ancestor Directory.Build imports");
    var dotnetLinuxPackageDriver = File.ReadAllText(Path.Combine(root, "tools", "Test-PortableDotNet-Package.sh"));
    Check(dotnetLinuxPackageDriver.Contains("-p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -p:PortableAssemblyPath=", StringComparison.Ordinal), "linux package consumer excludes ancestor Directory.Build imports");
    Check(dotnetLinuxPackageDriver.Contains("--expected-runtime-closure-digest", StringComparison.Ordinal) && dotnetLinuxPackageDriver.Contains("'runtimeClosureDigest':environment['runtimeClosureDigest']", StringComparison.Ordinal), "linux package row binds the expected runtime closure");
    Check(dotnetLinuxPackageDriver.Contains("\"$dotnet\" \"$consumer_dll\"", StringComparison.Ordinal) && !dotnetLinuxPackageDriver.Contains("\"$dotnet\" run --project \"$consumer/Consumer.csproj\"", StringComparison.Ordinal), "linux target runtime executes the built consumer without the SDK run command");
    var dotnetWindowsPackageDriver = File.ReadAllText(Path.Combine(root, "tools", "Test-PortableDotNet-Package.ps1"));
    Check(dotnetWindowsPackageDriver.Contains("\"-p:ImportDirectoryBuildProps=false\" \"-p:ImportDirectoryBuildTargets=false\" \"-p:PortableAssemblyPath=", StringComparison.Ordinal), "windows package consumer excludes ancestor Directory.Build imports");
    Check(dotnetWindowsPackageDriver.Contains("--expected-runtime-closure-digest", StringComparison.Ordinal) && dotnetWindowsPackageDriver.Contains("runtimeClosureDigest = $environment.runtimeClosureDigest", StringComparison.Ordinal), "windows package row binds the expected runtime closure");
    Check(dotnetWindowsPackageDriver.Contains("& $targetDotNet $consumerDll $vectors", StringComparison.Ordinal) && !dotnetWindowsPackageDriver.Contains("& $targetDotNet run --project", StringComparison.Ordinal), "windows target runtime executes the built consumer without the SDK run command");
    var dotnetWindowsJitDriver = File.ReadAllText(Path.Combine(root, "tools", "Test-PortableDotNet-Jit.ps1"));
    Check(dotnetWindowsJitDriver.Contains("$jitPlan.entrySymbol", StringComparison.Ordinal) && dotnetWindowsJitDriver.Contains("$jitPlan.candidateSymbol", StringComparison.Ordinal) && !dotnetWindowsJitDriver.Contains("Candidate.__default:F004", StringComparison.Ordinal), "windows JIT driver resolves both symbols from the pre-run package plan");
    Check(dotnetWindowsJitDriver.Contains("WaitForExit(180000)", StringComparison.Ordinal) && dotnetWindowsJitDriver.Contains("pid=$($process.Id)", StringComparison.Ordinal) && dotnetWindowsJitDriver.Contains("DOTNET_JitNoInline", StringComparison.Ordinal), "windows JIT diagnostic is bounded, process-bound and disables inlining");
    var dotnetLinuxJitDriver = File.ReadAllText(Path.Combine(root, "tools", "Test-PortableDotNet-Jit.sh"));
    Check(dotnetLinuxJitDriver.Contains("${symbols[0]} ${symbols[1]}", StringComparison.Ordinal) && !dotnetLinuxJitDriver.Contains("Candidate.__default:F004", StringComparison.Ordinal), "linux JIT driver resolves both symbols from the pre-run package plan");
    Check(dotnetLinuxJitDriver.Contains("sleep 180", StringComparison.Ordinal) && dotnetLinuxJitDriver.Contains("pid=$consumer_pid", StringComparison.Ordinal) && dotnetLinuxJitDriver.Contains("DOTNET_JitNoInline=1", StringComparison.Ordinal), "linux JIT diagnostic is bounded, process-bound and disables inlining");
    var packageHarnessSource = File.ReadAllText(Path.Combine(root, "tests", "Strogo.Modules.Portability.PackageHarness", "Program.cs"));
    Check(packageHarnessSource.Contains("Path.GetDirectoryName(dafny), Path.TrimEndingDirectorySeparator(dafnyRoot)", StringComparison.Ordinal), "package harness fixes the proof closure root at the Dafny executable parent");
    Check(packageHarnessSource.Contains("code = \"PackageHarnessRejected\"", StringComparison.Ordinal) && packageHarnessSource.Contains("throw new PackageHarnessException(message)", StringComparison.Ordinal), "package harness preflight failures are structured");
    var portabilitySources = Directory.GetFiles(Path.Combine(root, "src", "Strogo.Modules.Portability"), "*.cs", SearchOption.AllDirectories).SelectMany(File.ReadAllLines).ToArray();
    Check(!portabilitySources.Any(line => line.Contains("TrustedModuleRuntime", StringComparison.Ordinal)), "portability project does not reference production runtime");

    var runtimeOs = OperatingSystem.IsWindows() ? "windows" : "linux";
    var runtimeExecutable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    var runtimeA = Path.Combine(packageTemp, "runtime-a");
    var runtimeB = Path.Combine(packageTemp, "runtime-b", "unrelated");
    CreateFakeDotNetRuntime(runtimeA, runtimeExecutable);
    CreateFakeDotNetRuntime(runtimeB, runtimeExecutable);
    var runtimeReceiptA = DotNetRuntimeClosure.Capture(Path.Combine(runtimeA, runtimeExecutable), runtimeOs, "x64", "10.0.11");
    var runtimeReceiptB = DotNetRuntimeClosure.Capture(Path.Combine(runtimeB, runtimeExecutable), runtimeOs, "x64", "10.0.11", runtimeReceiptA.RuntimeClosureDigest);
    Check(runtimeReceiptA.RuntimeClosureDigest == runtimeReceiptB.RuntimeClosureDigest && runtimeReceiptA.InventoryBytes.SequenceEqual(runtimeReceiptB.InventoryBytes), "runtime closure identity excludes physical root");
    Check(runtimeReceiptA.Files.Length == 3 && runtimeReceiptA.Files.Select(file => file.Path).SequenceEqual(runtimeReceiptA.Files.Select(file => file.Path).Order(StringComparer.Ordinal), StringComparer.Ordinal), "runtime closure has a complete ordered fake inventory");
    using (var runtimeInventory = JsonDocument.Parse(runtimeReceiptA.InventoryBytes))
        Fields(runtimeInventory.RootElement, "schemaVersion", "profileId", "os", "arch", "files", "versions");
    var mutableRuntimeInventory = runtimeReceiptA.InventoryBytes;
    mutableRuntimeInventory[0] ^= 0xff;
    Check(runtimeReceiptA.InventoryBytes[0] != mutableRuntimeInventory[0], "runtime inventory bytes are defensively copied");

    File.AppendAllText(Path.Combine(runtimeB, "shared", "Microsoft.NETCore.App", "10.0.11", "coreclr.bin"), "corrupt", Encoding.ASCII);
    Check(RejectsEnvironment(() => DotNetRuntimeClosure.Capture(Path.Combine(runtimeB, runtimeExecutable), runtimeOs, "x64", "10.0.11", runtimeReceiptA.RuntimeClosureDigest), "RuntimeClosureDigestMismatch"), "corrupt runtime closure is unavailable against expected identity");
    File.Delete(Path.Combine(runtimeA, "host", "fxr", "10.0.11", "hostfxr.bin"));
    Check(RejectsEnvironment(() => DotNetRuntimeClosure.Capture(Path.Combine(runtimeA, runtimeExecutable), runtimeOs, "x64", "10.0.11"), "MissingRuntimeComponent"), "missing runtime component is unavailable");
    Directory.Delete(Path.Combine(runtimeA, "host", "fxr", "10.0.11"));
    Check(RejectsEnvironment(() => DotNetRuntimeClosure.Capture(Path.Combine(runtimeA, runtimeExecutable), runtimeOs, "x64", "10.0.11"), "MissingRuntimeComponent"), "missing runtime version is unavailable");
    Directory.CreateDirectory(Path.Combine(runtimeB, "host", "fxr", "10.0.12"));
    File.WriteAllText(Path.Combine(runtimeB, "host", "fxr", "10.0.12", "hostfxr.bin"), "hostfxr-v2", Encoding.ASCII);
    Check(RejectsEnvironment(() => DotNetRuntimeClosure.Capture(Path.Combine(runtimeB, runtimeExecutable), runtimeOs, "x64", "10.0.11"), "AmbiguousRuntimeSelection"), "multiple selectable hostfxr versions are unavailable");
    Check(RejectsEnvironment(() => DotNetRuntimeClosure.Capture(Path.Combine(runtimeB, runtimeExecutable), runtimeOs, "arm64", "10.0.11"), "UnsupportedArchitecture"), "unsupported runtime architecture is unavailable");
    Check(RejectsEnvironment(() => DotNetRuntimeClosure.Capture(Path.Combine(runtimeB, runtimeExecutable), runtimeOs, "x64", "10.0.11", "INVALID"), "InvalidExpectedDigest"), "invalid expected runtime identity is unavailable");

    var windowsOnly = PortabilityReportMatrix.Complete(PortabilityVersions.DotNetProfile,
    [
        PassedRow("windows")
    ]);
    Check(windowsOnly.Length == 2 && windowsOnly[0].Os == "linux" && windowsOnly[1].Os == "windows", "matrix rows are complete and ordinal-sorted");
    Check(windowsOnly[0].Status == "Unavailable" && windowsOnly[0].Synthesized && windowsOnly[0].ReasonCodes.SequenceEqual(new[] { "EnvironmentUnavailable", "RowUnavailable" }, StringComparer.Ordinal), "absent mandatory platform row becomes unavailable");
    Check(windowsOnly[1].Status == "Passed" && !windowsOnly[1].Synthesized && windowsOnly[1].ReasonCodes.IsEmpty, "provided passing platform row is preserved");
    var bothRows = PortabilityReportMatrix.Complete(PortabilityVersions.DotNetProfile,
    [
        PassedRow("windows"),
        PassedRow("linux")
    ]);
    Check(bothRows.All(row => row.Status == "Passed" && !row.Synthesized), "complete passing matrix has no synthesized row");
    var unavailableRow = PortabilityReportMatrix.Complete(PortabilityVersions.DotNetProfile,
    [
        new PortabilityPlatformStatus("linux", "x64", "Unavailable", ["EnvironmentUnavailable"])
    ])[0];
    Check(unavailableRow.ReasonCodes.SequenceEqual(new[] { "EnvironmentUnavailable", "RowUnavailable" }, StringComparer.Ordinal), "explicit unavailable row receives aggregate reason");
    var failedRow = PortabilityReportMatrix.Complete(PortabilityVersions.DotNetProfile,
    [
        new PortabilityPlatformStatus("linux", "x64", "Failed", ["ConsumerFailed"])
    ])[0];
    Check(failedRow.ReasonCodes.SequenceEqual(new[] { "ConsumerFailed", "RowFailed" }, StringComparer.Ordinal), "failed row receives aggregate reason");
    Check(RejectsReport(() => PortabilityReportMatrix.Complete(PortabilityVersions.DotNetProfile, [PassedRow("windows"), PassedRow("windows")]), "DuplicatePlatformRow"), "duplicate platform row is rejected");
    Check(RejectsReport(() => PortabilityReportMatrix.Complete(PortabilityVersions.DotNetProfile, [PassedRow("macos")]), "UnknownPlatformRow"), "unknown platform row is rejected");
    Check(RejectsReport(() => PortabilityReportMatrix.Complete(PortabilityVersions.DotNetProfile, [new PortabilityPlatformStatus("windows", "x64", "Skipped")]), "UnknownRowStatus"), "unknown platform status is rejected");
    Check(RejectsReport(() => PortabilityReportMatrix.Complete(PortabilityVersions.DotNetProfile, [new PortabilityPlatformStatus("windows", "x64", "Passed", ["RowFailed"])]), "PassedRowHasReasons"), "passing row cannot retain failure reasons");
    Check(RejectsReport(() => PortabilityReportMatrix.Complete(PortabilityVersions.DotNetProfile, [new PortabilityPlatformStatus("windows", "x64", "Unavailable")]), "UnavailableRowHasNoEnvironmentReason"), "unavailable row requires an environment reason");
    Check(RejectsReport(() => PortabilityReportMatrix.Complete(PortabilityVersions.DotNetProfile, [new PortabilityPlatformStatus("windows", "x64", "Failed", ["Unknown"])]), "UnknownReasonCode"), "unknown reason code is rejected");
    Check(RejectsReport(() => PortabilityReportMatrix.Complete(PortabilityVersions.DotNetProfile, [new PortabilityPlatformStatus("windows", "x64", "Passed")]), "PassedRowEvidenceMissing"), "passing row requires exact evidence identities");
    Check(RejectsReport(() => PortabilityReportMatrix.Complete(PortabilityVersions.DotNetProfile, [PassedRow("windows"), PassedRow("linux", 'b')]), "PlatformArtifactIdentityMismatch"), "passing rows from different package identities are rejected");

    var jitPlan = DotNetJitDiagnostics.Bind(packageA, receiptA.PortabilityManifestDigest);
    Check(jitPlan.EntrySymbol == DotNetJitDiagnostics.EntrySymbol && jitPlan.CandidateEntityId == "function/summarize" && jitPlan.CandidateGeneratedName == "F004" && jitPlan.CandidateSymbol == "Candidate.__default:F004", "JIT symbols are derived from the bound public API and source map");
    using (var planDocument = JsonDocument.Parse(DotNetJitDiagnostics.PlanBytes(jitPlan)))
        Fields(planDocument.RootElement, "schemaVersion", "profileId", "moduleDigest", "dafnySourceDigest", "portabilityManifestDigest", "packageDigest", "artifactDigest", "entryAssemblyDigest", "sourceMapDigest", "publicApiDigest", "entrySymbol", "candidateEntityId", "candidateGeneratedName", "candidateSymbol", "calls");
    var jitRuntimeReport = CanonicalJson.Encode(new
    {
        schemaVersion = "strogo.environment-report.v0.1",
        status = "Passed",
        reasonCode = "None",
        profileId = PortabilityVersions.DotNetProfile,
        os = runtimeOs,
        arch = "x64",
        runtimeVendor = "Microsoft",
        runtimeVersion = "10.0.11",
        runtimeClosureDigest = new string('e', 64),
        launcherDigest = new string('f', 64),
        harnessDigest = new string('1', 64),
        runtimeFiles = "3"
    });
    var jitConsumerOutput = Encoding.UTF8.GetBytes("PASS dotnet JIT diagnostic pid=1234 calls=50000\n");
    var entrySignature = $"{jitPlan.EntrySymbol}(System.String):System.String";
    var candidateSignature = $"{jitPlan.CandidateSymbol}(Dafny.ISequence`1[long]):Candidate._IR000";
    var jitLog = Encoding.UTF8.GetBytes(string.Join('\n', new[]
    {
        $"; Assembly listing for method {entrySignature} (FullOpts)",
        $"; BEGIN METHOD {entrySignature}",
        $"; END METHOD {entrySignature}",
        $"; Assembly listing for method {candidateSignature} (FullOpts)",
        $"; BEGIN METHOD {candidateSignature}",
        $"; END METHOD {candidateSignature}",
        ""
    }));
    var jitReceipt = DotNetJitDiagnostics.Validate(jitPlan, jitRuntimeReport, jitConsumerOutput, jitLog, runtimeOs, "x64");
    Check(jitReceipt.Calls == "50000" && jitReceipt.ProcessId == "1234" && jitReceipt.CompilationEvents.SequenceEqual(new[] { entrySignature, candidateSignature }, StringComparer.Ordinal), "JIT receipt binds two exact compilation events to one process marker");
    using (var jitReceiptDocument = JsonDocument.Parse(jitReceipt.Bytes))
        Fields(jitReceiptDocument.RootElement, "schemaVersion", "status", "profileId", "os", "arch", "runtimeVendor", "runtimeVersion", "runtimeClosureDigest", "portabilityManifestDigest", "packageDigest", "artifactDigest", "entryAssemblyDigest", "moduleDigest", "dafnySourceDigest", "sourceMapDigest", "publicApiDigest", "processId", "calls", "flags", "compilationEvents", "consumerOutputDigest", "jitLogDigest", "assertionBoundary");
    var mutableJitReceipt = jitReceipt.Bytes;
    mutableJitReceipt[0] ^= 0xff;
    Check(jitReceipt.Bytes[0] != mutableJitReceipt[0], "JIT receipt bytes are defensively copied");
    var decoyJitLog = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(jitLog).Replace(jitPlan.CandidateSymbol, jitPlan.CandidateSymbol + "Decoy", StringComparison.Ordinal));
    Check(RejectsJit(() => DotNetJitDiagnostics.Validate(jitPlan, jitRuntimeReport, jitConsumerOutput, decoyJitLog, runtimeOs, "x64"), "$/events/candidate"), "decoy candidate symbol cannot satisfy JIT evidence");
    var missingCandidateJitLog = Encoding.UTF8.GetBytes(string.Join('\n', Encoding.UTF8.GetString(jitLog).Split('\n').Where(line => !line.Contains(jitPlan.CandidateSymbol, StringComparison.Ordinal))));
    Check(RejectsJit(() => DotNetJitDiagnostics.Validate(jitPlan, jitRuntimeReport, jitConsumerOutput, missingCandidateJitLog, runtimeOs, "x64"), "$/events/candidate"), "removed candidate compilation event is rejected");
    var insufficientCalls = Encoding.UTF8.GetBytes("PASS dotnet JIT diagnostic pid=1234 calls=49999\n");
    Check(RejectsJit(() => DotNetJitDiagnostics.Validate(jitPlan, jitRuntimeReport, insufficientCalls, jitLog, runtimeOs, "x64"), "$/consumerOutput/calls"), "JIT diagnostic requires the fixed call count");

    var reportEvidence = CreateReportEvidence("0123456789abcdef0123456789abcdef01234567");
    var reportTimestamp = new DateTimeOffset(2026, 9, 8, 12, 34, 56, TimeSpan.FromHours(3));
    var portabilityReport = PortabilityReportV01.Build(reportEvidence, reportTimestamp);
    if (e06rReportOption >= 0)
    {
        var output = Path.GetFullPath(args[e06rReportOption + 1], root);
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? throw new InvalidOperationException("E06R report path has no directory"));
        File.WriteAllBytes(output, portabilityReport.CanonicalJsonBytes());
    }
    using (var reportDocument = JsonDocument.Parse(portabilityReport.CanonicalJsonBytes()))
        Fields(reportDocument.RootElement, "schemaVersion", "purpose", "contractStatus", "admissionStatus", "sourceRevision", "moduleDigest", "ownerBundleDigest", "proofDigest", "dafnySourceDigest", "profiles", "comparisonStatus", "reasons", "semanticDigest", "createdAtUtc");
    Check(portabilityReport.ComparisonStatus == "Portable" && portabilityReport.Profiles.Length == 2 && portabilityReport.Profiles.All(profile => profile.Status == "Portable"), "full typed evidence produces a portable validation-only report");
    Check(portabilityReport.CreatedAtUtc == "2026-09-08T09:34:56.0000000Z" && portabilityReport.SemanticDigest.Length == 64, "report timestamp and semantic digest are canonical");
    Check(PortabilityContract.DomainHash("strogo.portability-report.v0.1/semantic", portabilityReport.SemanticProjectionBytes()) == portabilityReport.SemanticDigest, "semantic projection hash is self-consistent");
    Check(RejectsReport(() => portabilityReport.MarkdownBytes("../report.json"), "InvalidMarkdownPath"), "Markdown link rejects parent traversal");
    var rebuiltReport = PortabilityReportV01.Validate(portabilityReport.CanonicalJsonBytes(), reportEvidence);
    Check(rebuiltReport.CanonicalJsonBytes().SequenceEqual(portabilityReport.CanonicalJsonBytes()), "report validation rebuilds exact canonical bytes");
    var offsetEquivalent = PortabilityReportV01.Build(reportEvidence, new DateTimeOffset(2026, 9, 8, 9, 34, 56, TimeSpan.Zero));
    Check(offsetEquivalent.CanonicalJsonBytes().SequenceEqual(portabilityReport.CanonicalJsonBytes()), "offset-equivalent timestamps canonicalize identically");
    var diagnosticMutation = PortabilityReportV01.Build(CreateReportEvidence("0123456789abcdef0123456789abcdef01234567", rawMarker: "diagnostic-b"), reportTimestamp);
    Check(!diagnosticMutation.CanonicalJsonBytes().SequenceEqual(portabilityReport.CanonicalJsonBytes()) && diagnosticMutation.SemanticDigest == portabilityReport.SemanticDigest, "raw receipt mutation changes report bytes but not semantic digest");
    var outcomeMutation = PortabilityReportV01.Build(CreateReportEvidence("0123456789abcdef0123456789abcdef01234567", changeOutcome: true), reportTimestamp);
    Check(outcomeMutation.SemanticDigest != portabilityReport.SemanticDigest, "owner outcome mutation changes semantic digest");
    var crossOsMismatch = PortabilityReportV01.Build(CreateReportEvidence("0123456789abcdef0123456789abcdef01234567", changeWindowsVector: true), reportTimestamp);
    Check(crossOsMismatch.ComparisonStatus == "NotPortable" && crossOsMismatch.Profiles.All(profile => profile.ReasonCodes.Contains("OracleMismatch")), "cross-OS vector mismatch becomes a typed OracleMismatch");
    var crossProfileMismatch = PortabilityReportV01.Build(CreateReportEvidence("0123456789abcdef0123456789abcdef01234567", changeJvmVector: true), reportTimestamp);
    Check(crossProfileMismatch.ComparisonStatus == "NotPortable" && crossProfileMismatch.Profiles.All(profile => profile.ReasonCodes.Contains("OracleMismatch")), $"cross-profile vector mismatch marks both profiles OracleMismatch (status={crossProfileMismatch.ComparisonStatus}; profiles={string.Join("|", crossProfileMismatch.Profiles.Select(profile => profile.ProfileId + ":" + profile.Status + ":" + string.Join(',', profile.ReasonCodes) + ":" + profile.CommonVectorSetDigest))})");
    var twoVectorEvidence = CreateReportEvidence("0123456789abcdef0123456789abcdef01234567", twoVectors: true);
    var twoVectorReport = PortabilityReportV01.Build(twoVectorEvidence, reportTimestamp);
    var permutedTwoVectorReport = PortabilityReportV01.Build(CreateReportEvidence("0123456789abcdef0123456789abcdef01234567", twoVectors: true, permuteOutcomes: true), reportTimestamp);
    Check(permutedTwoVectorReport.CanonicalJsonBytes().SequenceEqual(twoVectorReport.CanonicalJsonBytes()), "whole outcome-row permutation is canonicalized without changing the report");
    Check(RejectsReport(() => PortabilityReportV01.Build(CreateReportEvidence("0123456789abcdef0123456789abcdef01234567", twoVectors: true, swapOutcomeDigest: true), reportTimestamp), "OutcomeCoverageMismatch", "$/outcomes/v-001"), "swapped digest across two vectors is rejected at the vector outcome locus");
    var unavailableReport = PortabilityReportV01.Build(CreateReportEvidence("0123456789abcdef0123456789abcdef01234567", omitLinux: true), reportTimestamp);
    Check(unavailableReport.Profiles.All(profile => profile.Platforms[0].Status == "Unavailable" && profile.Platforms[0].ReasonCodes.SequenceEqual(new[] { "EnvironmentUnavailable", "RowUnavailable" })), "missing mandatory OS row is synthesized as unavailable");
    var tamperedReportBytes = portabilityReport.CanonicalJsonBytes();
    var tamperIndex = Array.IndexOf(tamperedReportBytes, (byte)'a');
    tamperedReportBytes[tamperIndex] = (byte)'b';
    Check(RejectsReport(() => PortabilityReportV01.Validate(tamperedReportBytes, reportEvidence), "CanonicalReportMismatch"), "self-declared canonical report mutation is rejected by rebuild");
    Check(RejectsReport(() => PortabilityReportV01.Build(CreateReportEvidence("0123456789abcdef0123456789abcdef01234567", omitOutcome: true), reportTimestamp), "OutcomeCoverageMismatch"), "outcome subset is rejected before oracle gate");
    Check(RejectsReport(() => PortabilityReportV01.Build(CreateReportEvidence("0123456789abcdef0123456789abcdef01234567", omitJvm: true), reportTimestamp), "MissingProfile"), "missing mandatory profile is rejected without report output");
    Check(RejectsReport(() => PortabilityReportV01.Build(CreateReportEvidence("0123456789abcdef0123456789abcdef01234567", mixedReceiptRevision: "fedcba9876543210fedcba9876543210fedcba98"), reportTimestamp), "SourceRevisionMismatch"), "mixed source revision is rejected before report construction");
    var reportTemp = Path.Combine(Path.GetTempPath(), "strogo-report-" + Guid.NewGuid().ToString("N"));
    try
    {
        PortabilityReportWriter.WriteNewDirectory(portabilityReport, reportEvidence, reportTemp);
        Check(File.Exists(Path.Combine(reportTemp, "report.json")) && File.Exists(Path.Combine(reportTemp, "REPORT.md")) && File.Exists(Path.Combine(reportTemp, "sha256.txt")), "report writer publishes report, projection and completion marker");
        Check(File.Exists(Path.Combine(reportTemp, "evidence-index.json")) && File.Exists(Path.Combine(reportTemp, "evidence", PortabilityVersions.DotNetProfile, "profile", "build.json")), "report writer retains evidence receipts and index");
        Check(RejectsReport(() => PortabilityReportWriter.WriteNewDirectory(portabilityReport, reportEvidence, reportTemp), "DestinationExists"), "report writer refuses overwrite");
    }
    finally
    {
        if (Directory.Exists(reportTemp)) Directory.Delete(reportTemp, recursive: true);
    }

    foreach (var injected in new[]
    {
        ("write", new PortabilityReportWriterFaultPlan(FailWriteOrdinal: 1), "write-report"),
        ("read", new PortabilityReportWriterFaultPlan(FailReadOrdinal: 1), "read-back-validation"),
        ("hash", new PortabilityReportWriterFaultPlan(FailHashOrdinal: 1), "hash-completion-marker")
    })
    {
        var failureDirectory = Path.Combine(Path.GetTempPath(), "strogo-report-failure-" + injected.Item1 + "-" + Guid.NewGuid().ToString("N"));
        try
        {
            var failure = CaptureWriterFailure(() => PortabilityReportWriter.WriteNewDirectory(portabilityReport, reportEvidence, failureDirectory, injected.Item2));
            Check(failure.Stage == injected.Item3 && !Directory.Exists(failureDirectory) && !File.Exists(Path.Combine(failureDirectory, "sha256.txt")), $"injected {injected.Item1} failure leaves no final directory or completion marker");
            Check(failure.CleanupException is null && failure.StagingDirectory.Length > 0, $"injected {injected.Item1} failure retains bounded staging diagnostic");
        }
        finally
        {
            if (Directory.Exists(failureDirectory)) Directory.Delete(failureDirectory, recursive: true);
            foreach (var staging in Directory.EnumerateDirectories(Path.GetDirectoryName(failureDirectory)!, Path.GetFileName(failureDirectory) + ".staging-*"))
                Directory.Delete(staging, recursive: true);
        }
    }

    var cleanupFailureDirectory = Path.Combine(Path.GetTempPath(), "strogo-report-failure-cleanup-" + Guid.NewGuid().ToString("N"));
    try
    {
        var cleanupFailure = CaptureWriterFailure(() => PortabilityReportWriter.WriteNewDirectory(portabilityReport, reportEvidence, cleanupFailureDirectory, new PortabilityReportWriterFaultPlan(FailWriteOrdinal: 1, FailCleanup: true)));
        Check(cleanupFailure.Stage == "cleanup" && cleanupFailure.CleanupException is not null && !Directory.Exists(cleanupFailureDirectory) && !File.Exists(Path.Combine(cleanupFailureDirectory, "sha256.txt")), "cleanup failure keeps final directory and completion marker absent while retaining cleanup diagnostic");
        Check(Directory.Exists(cleanupFailure.StagingDirectory), "cleanup failure retains residual staging path for bounded local diagnosis");
    }
    finally
    {
        if (Directory.Exists(cleanupFailureDirectory)) Directory.Delete(cleanupFailureDirectory, recursive: true);
        foreach (var staging in Directory.EnumerateDirectories(Path.GetDirectoryName(cleanupFailureDirectory)!, Path.GetFileName(cleanupFailureDirectory) + ".staging-*"))
            Directory.Delete(staging, recursive: true);
    }
}
finally
{
    if (Directory.Exists(packageTemp)) Directory.Delete(packageTemp, recursive: true);
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

static bool RejectsPackage(Action action)
{
    try
    {
        action();
        return false;
    }
    catch (PortabilityContractException exception)
    {
        return exception.Code == "PortabilityPackageRejected";
    }
}

static bool RejectsEnvironment(Action action, string reason)
{
    try
    {
        action();
        return false;
    }
    catch (PortabilityContractException exception) when (exception.Code == "EnvironmentUnavailable")
    {
        using var details = JsonDocument.Parse(CanonicalJson.Encode(exception.Details));
        return details.RootElement.GetProperty("reason").GetString() == reason;
    }
}

static bool RejectsReport(Action action, string reason, string? locus = null)
{
    try
    {
        action();
        return false;
    }
    catch (PortabilityContractException exception) when (exception.Code == "PortabilityReportRejected")
    {
        using var details = JsonDocument.Parse(CanonicalJson.Encode(exception.Details));
        return details.RootElement.GetProperty("reason").GetString() == reason && (locus is null || exception.Locus == locus);
    }
}

static PortabilityReportWriterException CaptureWriterFailure(Action action)
{
    try
    {
        action();
        throw new InvalidOperationException("expected PortabilityReportWriterException");
    }
    catch (PortabilityReportWriterException exception)
    {
        return exception;
    }
}

static bool RejectsJit(Action action, string locus)
{
    try
    {
        action();
        return false;
    }
    catch (PortabilityContractException exception) when (exception.Code == "JitEvidenceMissing")
    {
        return exception.Locus == locus;
    }
}

static void CreateFakeDotNetRuntime(string root, string executable)
{
    Directory.CreateDirectory(Path.Combine(root, "host", "fxr", "10.0.11"));
    Directory.CreateDirectory(Path.Combine(root, "shared", "Microsoft.NETCore.App", "10.0.11"));
    File.WriteAllText(Path.Combine(root, executable), "launcher-v1", Encoding.ASCII);
    File.WriteAllText(Path.Combine(root, "host", "fxr", "10.0.11", "hostfxr.bin"), "hostfxr-v1", Encoding.ASCII);
    File.WriteAllText(Path.Combine(root, "shared", "Microsoft.NETCore.App", "10.0.11", "coreclr.bin"), "runtime-v1", Encoding.ASCII);
}

static PortabilityReportEvidenceSet CreateReportEvidence(string sourceRevision, string rawMarker = "receipt-a", bool changeOutcome = false, bool omitJvm = false, bool omitOutcome = false, bool changeWindowsVector = false, bool changeJvmVector = false, string? mixedReceiptRevision = null, bool omitLinux = false, bool twoVectors = false, bool swapOutcomeDigest = false, bool permuteOutcomes = false)
{
    var digest = new string('a', 64);
    var profiles = new List<PortabilityReportProfileEvidence>
    {
        CreateReportProfile(PortabilityVersions.DotNetProfile, sourceRevision, digest, null, null, rawMarker, changeOutcome, omitOutcome, changeWindowsVector, false, mixedReceiptRevision, omitLinux, twoVectors, swapOutcomeDigest, permuteOutcomes)
    };
    if (!omitJvm)
        profiles.Add(CreateReportProfile(PortabilityVersions.JvmProfile, sourceRevision, digest, new string('b', 64), new string('c', 64), rawMarker, changeOutcome, omitOutcome, changeWindowsVector, changeJvmVector, mixedReceiptRevision, omitLinux, twoVectors, swapOutcomeDigest, permuteOutcomes));
    return new PortabilityReportEvidenceSet(sourceRevision, digest, new string('d', 64), new string('e', 64), new string('f', 64), profiles);
}

static PortabilityReportProfileEvidence CreateReportProfile(string profileId, string sourceRevision, string digest, string? baseline, string? approval, string rawMarker, bool changeOutcome, bool omitOutcome, bool changeWindowsVector, bool changeAllVectors = false, string? mixedReceiptRevision = null, bool omitLinux = false, bool twoVectors = false, bool swapOutcomeDigest = false, bool permuteOutcomes = false)
{
    var receiptRevision = mixedReceiptRevision ?? sourceRevision;
    var build = new PortabilityReportBuildEvidence("Passed", [], CanonicalJson.Encode(new { sourceRevision = receiptRevision, kind = "build", marker = rawMarker }), digest, digest, digest, digest, digest, digest);
    var platforms = new List<PortabilityReportPlatformEvidence>();
    if (!omitLinux) platforms.Add(CreateReportPlatform("linux", receiptRevision, digest, rawMarker, changeOutcome, omitOutcome, changeAllVectors, twoVectors, swapOutcomeDigest, permuteOutcomes));
    platforms.Add(CreateReportPlatform("windows", receiptRevision, digest, rawMarker, changeOutcome, omitOutcome, changeWindowsVector || changeAllVectors, twoVectors, swapOutcomeDigest, permuteOutcomes));
    return new PortabilityReportProfileEvidence(profileId, digest, digest, digest, baseline, approval, build, platforms);
}

static PortabilityReportPlatformEvidence CreateReportPlatform(string os, string sourceRevision, string digest, string rawMarker, bool changeOutcome, bool omitOutcome, bool changeVector, bool twoVectors = false, bool swapOutcomeDigest = false, bool permuteOutcomes = false)
{
    var vectorId = changeVector ? "v-002" : "v-001";
    var vectors = twoVectors
        ? new[] { new PortabilityReportVector("v-001", digest), new PortabilityReportVector("v-002", new string('b', 64)) }
        : new[] { new PortabilityReportVector(vectorId, digest) };
    var outcome = PortabilityReportOutcome.FromJson("success", new { type = "I64", value = changeOutcome ? "8" : "7" }, null, null, null);
    var outcomes = omitOutcome
        ? Array.Empty<PortabilityReportOutcomeRow>()
        : twoVectors
            ? (permuteOutcomes
                ? new[]
                {
                    new PortabilityReportOutcomeRow("v-002", swapOutcomeDigest ? digest : new string('b', 64), "OwnerInDomain", digest, "Returned", outcome),
                    new PortabilityReportOutcomeRow("v-001", swapOutcomeDigest ? new string('b', 64) : digest, "OwnerInDomain", digest, "Returned", outcome)
                }
                : new[]
                {
                    new PortabilityReportOutcomeRow("v-001", swapOutcomeDigest ? new string('b', 64) : digest, "OwnerInDomain", digest, "Returned", outcome),
                    new PortabilityReportOutcomeRow("v-002", new string('b', 64), "OwnerInDomain", digest, "Returned", outcome)
                })
            : new[] { new PortabilityReportOutcomeRow(vectorId, digest, "OwnerInDomain", digest, "Returned", outcome) };
    var consumer = new PortabilityReportGateEvidence("consumer", "Passed", [], CanonicalJson.Encode(new { sourceRevision, kind = "consumer", os, marker = rawMarker }), digest, null, null, null, [], null);
    var jit = new PortabilityReportGateEvidence("jit", "Passed", [], CanonicalJson.Encode(new { sourceRevision, kind = "jit", os, marker = rawMarker }), null, "entry", "candidate", "50000", ["FullOpts"], null);
    var oracle = new PortabilityReportGateEvidence("oracle", "Passed", [], CanonicalJson.Encode(new { sourceRevision, kind = "oracle", os, marker = rawMarker }), null, null, null, null, [], digest);
    return new PortabilityReportPlatformEvidence(os, "x64", $"{os}-identity", $"{os}-kernel", "fixture", "1", digest, digest, digest, digest, digest, digest, consumer, jit, oracle, null, vectors, outcomes);
}

static PortabilityPlatformStatus PassedRow(string os, char identity = 'a')
    => new(
        os,
        "x64",
        "Passed",
        portabilityManifestDigest: new string(identity, 64),
        packageDigest: new string(identity, 64),
        artifactDigest: new string(identity, 64),
        runtimeClosureDigest: new string(os == "windows" ? 'c' : 'd', 64));

static bool EqualTrees(string left, string right)
{
    var leftFiles = Directory.GetFiles(left, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(left, path).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal).ToArray();
    var rightFiles = Directory.GetFiles(right, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(right, path).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal).ToArray();
    return leftFiles.SequenceEqual(rightFiles, StringComparer.Ordinal) && leftFiles.All(relative => File.ReadAllBytes(Path.Combine(left, relative.Replace('/', Path.DirectorySeparatorChar))).SequenceEqual(File.ReadAllBytes(Path.Combine(right, relative.Replace('/', Path.DirectorySeparatorChar)))));
}

static string SyntheticJvmDiagnostics(string newline, char pathSeparator)
{
    var path = $"generated{pathSeparator}Generated.java";
    var categories = Enumerable.Repeat("cast", 35)
        .Concat(Enumerable.Repeat("rawtypes", 67))
        .Concat(Enumerable.Repeat("serial", 1))
        .Concat(Enumerable.Repeat("varargs", 3))
        .ToArray();
    var lines = new List<string>();
    for (var index = 0; index < categories.Length; index++)
    {
        var line = index + 1;
        lines.Add($"{path}:{line}: warning: [{categories[index]}] warning {line}");
        lines.Add($"  source context {line}");
        lines.Add("  ^");
    }
    lines.Add("106 warnings");
    return string.Join(newline, lines) + newline;
}

static JvmProcessRequest ProcessRequest(string executable, string workingDirectory, IReadOnlyList<string> arguments, int outputLimit, int timeoutMilliseconds)
    => new(
        executable,
        workingDirectory,
        arguments,
        ["CLASSPATH", "JDK_JAVAC_OPTIONS", "JDK_JAVA_OPTIONS", "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS"],
        TimeSpan.FromMilliseconds(timeoutMilliseconds),
        outputLimit,
        TimeSpan.FromSeconds(5),
        JvmProcessKind.JavacProbe);

static void RunJvmProcessFixture(string[] fixtureArgs)
{
    if (fixtureArgs.Length == 0) throw new ArgumentException("JVM process fixture mode is required");
    switch (fixtureArgs[0])
    {
        case "stdout":
            Console.OpenStandardOutput().Write(new byte[2048]);
            return;
        case "stderr":
            Console.OpenStandardError().Write(new byte[2048]);
            return;
        case "child":
            Thread.Sleep(TimeSpan.FromMinutes(1));
            return;
        case "cwd-lock" when fixtureArgs.Length == 2:
            File.WriteAllText(fixtureArgs[1], Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.ASCII);
            Thread.Sleep(TimeSpan.FromMinutes(1));
            return;
        case "flood":
            while (true)
            {
                Console.OpenStandardOutput().Write(new byte[8192]);
                Thread.Sleep(5);
            }
        case "tree" when fixtureArgs.Length == 2:
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("process executable unavailable");
            using (var child = StartJvmFixture(executable, Environment.CurrentDirectory, "child"))
                File.WriteAllText(fixtureArgs[1], child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.ASCII);
            Thread.Sleep(TimeSpan.FromMinutes(1));
            return;
        case "tree-flood" when fixtureArgs.Length == 2:
            var floodExecutable = Environment.ProcessPath ?? throw new InvalidOperationException("process executable unavailable");
            using (var child = StartJvmFixture(floodExecutable, Environment.CurrentDirectory, "flood", redirectStandardOutput: true))
            {
                File.WriteAllText(fixtureArgs[1], child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.ASCII);
                child.StandardOutput.BaseStream.CopyTo(Console.OpenStandardOutput());
            }
            return;
        default:
            throw new ArgumentException("unknown JVM process fixture mode");
    }
}

static Process StartJvmFixture(string executable, string workingDirectory, string mode, bool redirectStandardOutput = false, params string[] extraArguments)
{
    var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workingDirectory, RedirectStandardOutput = redirectStandardOutput, RedirectStandardError = redirectStandardOutput };
    startInfo.ArgumentList.Add("--jvm-process-fixture");
    startInfo.ArgumentList.Add(mode);
    foreach (var argument in extraArguments) startInfo.ArgumentList.Add(argument);
    return Process.Start(startInfo) ?? throw new InvalidOperationException("child process unavailable");
}

static bool ProcessAlive(int processId)
{
    for (var attempt = 0; attempt < 100; attempt++)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited) return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        Thread.Sleep(20);
    }
    return true;
}

static bool RejectsTargetBuild(Action action, string reason)
{
    try
    {
        action();
        return false;
    }
    catch (PortabilityContractException exception) when (exception.Code == "TargetBuildRejected")
    {
        using var details = JsonDocument.Parse(CanonicalJson.Encode(exception.Details));
        var actual = details.RootElement.GetProperty("reason").GetString();
        if (actual != reason) Console.Error.WriteLine($"expected TargetBuildRejected/{reason}, observed {actual} at {exception.Locus}");
        return actual == reason;
    }
}

static (string Reason, string Locus, JsonElement Details) CaptureTargetBuild(Action action)
{
    try
    {
        action();
        throw new InvalidOperationException("expected TargetBuildRejected");
    }
    catch (PortabilityContractException exception) when (exception.Code == "TargetBuildRejected")
    {
        using var details = JsonDocument.Parse(CanonicalJson.Encode(exception.Details));
        var clone = details.RootElement.Clone();
        return (clone.GetProperty("reason").GetString() ?? "", exception.Locus, clone);
    }
}
