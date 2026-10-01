using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Core;
using Strogo.Modules;
using IrInstruction = Strogo.Modules.IrInstruction;

internal static class G02GeneralityChecks
{
    internal static async Task RunAsync(string repoRoot)
    {
        var corpus = Path.Combine(repoRoot, "fixtures", "g02-generality");
        var evidence = Path.Combine(repoRoot, "artifacts", "local-validation", "g02", "generality-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        Console.WriteLine("Evidence: " + evidence);
        var checks = 0; var rows = new List<object>(); var loads = 0; var buildCalls = 0;
        void Check(bool condition, string id) { if (!condition) throw new Exception("generality " + id); checks++; }
        byte[] Read(string name) => File.ReadAllBytes(Path.Combine(corpus, name));
        using (var frozen = JsonDocument.Parse(Read("frozen.json")))
        {
            Check(frozen.RootElement.GetProperty("purpose").GetString() == "validation-only-no-human-admission", "frozen-purpose");
            foreach (var file in frozen.RootElement.GetProperty("files").EnumerateArray())
                Check(Convert.ToHexStringLower(SHA256.HashData(Read(file.GetProperty("path").GetString()!))) == file.GetProperty("sha256").GetString(), "frozen-hash");
        }
        foreach (var shape in new[] { "scalar", "allocation" })
        {
            var owner = OwnerBundleV04Parser.Parse(Read(shape + "-owner.json"));
            foreach (var variant in new[] { "primary", "alternative", "wrong" })
                _ = ModulesDafnyLowerer.Lower(Compile(Read(shape + "-" + variant + ".json")), owner);
        }
        using var tool = G02DafnyToolchain.Open(repoRoot);
        using var sdk = G02DotNetToolchain.Open(repoRoot);
        foreach (var shape in new[] { "scalar", "allocation" })
        {
            var moduleName = shape == "scalar" ? "math-add-valid.json" : "fold-allocation-primary.json";
            var ownerBytes = File.ReadAllBytes(Path.Combine(repoRoot, "fixtures", "modules-v0.2", shape == "scalar" ? "owner-add-one-valid.json" : "owner-fold-allocation-v0.4.json"));
            var originalOwner = OwnerBundleV04Parser.Parse(shape == "scalar" ? OwnerBundleMigrator.MigrateV03ToV04(ownerBytes) : ownerBytes);
            var original = ModulesDafnyLowerer.Lower(Compile(File.ReadAllBytes(Path.Combine(repoRoot, "fixtures", "modules-v0.2", moduleName))), originalOwner);
            using var previous = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repoRoot, "docs", "evidence", "g02-signed-session-20261001", shape, "obligations.json")));
            var expected = previous.RootElement.EnumerateArray().Select(value => (Id: value.GetProperty("obligationId").GetString(), Kind: value.GetProperty("kind").GetString())).OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();
            var actual = original.Obligations.Select(value => (Id: (string?)G02ProofTranscript.WireObligationId(value.Id), Kind: (string?)value.Kind)).OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();
            Check(actual.SequenceEqual(expected), "prior-unambiguous-obligation-vector-" + shape);
        }
        AssemblyLoadEventHandler observer = (_, args) => { if (args.LoadedAssembly.GetName().Name == "strogo.generated") loads++; };
        AppDomain.CurrentDomain.AssemblyLoad += observer;
        try
        {
            foreach (var shape in new[] { "scalar", "allocation" })
            {
                var owner = OwnerBundleV04Parser.Parse(Read(shape + "-owner.json"));
                var primary = Compile(Read(shape + "-primary.json"));
                var alternative = Compile(Read(shape + "-alternative.json"));
                var first = ModulesDafnyLowerer.Lower(primary, owner);
                var second = ModulesDafnyLowerer.Lower(alternative, owner);
                Check(primary.SourceDigest != alternative.SourceDigest && !first.SourceBytes.SequenceEqual(second.SourceBytes), "distinct-bodies-" + shape);
                if (shape == "scalar")
                {
                    Check(primary.Exports.Length == 2 && primary.Functions.Length == 2 && alternative.Exports.Length == 2, "contracted-exports");
                    var p = primary.Functions.Single(function => function.Id == "choose.beta-v2");
                    var a = alternative.Functions.Single(function => function.Id == "choose.beta-v2");
                    var branch = p.Instructions.Single(instruction => instruction.Op == "if");
                    Check(branch.ThenRegion!.Instructions.Any(IsHelperCall) && branch.ElseRegion!.Instructions.Any(IsHelperCall) &&
                          a.Instructions.Any(IsHelperCall) && a.Instructions.Single(instruction => instruction.Op == "if").ThenRegion!.Instructions.IsEmpty,
                        "branch-local-call-vs-call-after-branch");
                    var branchCalls = first.Obligations.Where(item => item.Kind == "call-contract").ToArray();
                    Check(branchCalls.Length == 2 && branchCalls.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == 2 &&
                          branchCalls.All(item => item.Id.StartsWith("qualified.", StringComparison.Ordinal)) &&
                          branchCalls.Any(item => item.EntityId.Contains("/then/", StringComparison.Ordinal)) && branchCalls.Any(item => item.EntityId.Contains("/else/", StringComparison.Ordinal)), "qualified-branch-obligations");
                    var repeated = new[] { branchCalls[0], branchCalls[0] }.ToImmutableArray();
                    var duplicate = new DafnyFoldLoweringResult(first.SourceBytes, first.NormalizedSourceBytes, first.SourceMap, repeated, first.InvariantSpans, first.ProofIdentity);
                    string? duplicateCode = null;
                    try { _ = G02ProofTranscript.NormalizeVerified(0, false, false, Encoding.UTF8.GetBytes("Dafny program verifier finished with 1 verified, 0 errors\n"), [], new string('a',64), new string('b',64), duplicate.Obligations); }
                    catch (ModuleException error) { duplicateCode = error.Code; }
                    Check(duplicateCode == "ProofObligationsInvalid", "duplicate-provenance-still-refused");
                    Check(primary.Functions.Single(function => function.Id == "advance.alpha-v2").Instructions.Any(instruction => instruction.Op == "i64.add") &&
                          alternative.Functions.Single(function => function.Id == "advance.alpha-v2").Instructions.Any(instruction => instruction.Op == "i64.sub"), "different-helper-bodies");
                }
                else Check(primary.Functions[0].Instructions.Any(instruction => instruction.Op == "fold") && alternative.Functions[0].Instructions.Any(instruction => instruction.Op == "fold"), "two-fold-bodies");

                var wrong = ModulesDafnyLowerer.Lower(Compile(Read(shape + "-wrong.json")), owner);
                var wrongDir = Path.Combine(evidence, shape + "-wrong"); var beforeLoads = loads; var beforeBuilds = buildCalls; string? code = null;
                try { _ = await G02DafnyReplay.RunFixtureAsync(tool, Inputs(wrong), wrongDir); }
                catch (ModuleException error) { code = error.Code; }
                Check(code == "VerifierFailed" && loads == beforeLoads && buildCalls == beforeBuilds, "wrong-proof-before-build-load");
                var failureText = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(wrongDir, "stdout.bin")));
                Check(shape == "scalar" ? failureText.Contains("postcondition", StringComparison.OrdinalIgnoreCase) :
                    failureText.Contains("assertion might not hold", StringComparison.OrdinalIgnoreCase) &&
                    wrong.Obligations.Where(item => item.EntityId.EndsWith("/candidate-preservation", StringComparison.Ordinal)).Any(item => failureText.Contains($"candidate.dfy({item.Line},", StringComparison.Ordinal)), "wrong-owner-semantics-not-parser");
                File.WriteAllBytes(Path.Combine(wrongDir, "candidate.dfy"), wrong.SourceBytes);
                rows.Add(new { id = shape + "-wrong", code, assemblyLoads = loads - beforeLoads, buildCalls = buildCalls - beforeBuilds,
                    moduleDigest = Compile(Read(shape + "-wrong.json")).SourceDigest, bundleDigest = owner.BundleDigest });

                foreach (var variant in new[] { "primary", "alternative" })
                {
                    Console.WriteLine("Checking " + shape + "/" + variant);
                    var moduleBytes = Read(shape + "-" + variant + ".json");
                    var module = Compile(moduleBytes); var lowering = ModulesDafnyLowerer.Lower(module, owner);
                    var permuted = Compile(PermuteDeclarations(moduleBytes)); var permutedLowering = ModulesDafnyLowerer.Lower(permuted, owner);
                    Check(module.SourceDigest == permuted.SourceDigest && lowering.SourceBytes.SequenceEqual(permutedLowering.SourceBytes) &&
                          CanonicalJson.Encode(lowering.SourceMap).SequenceEqual(CanonicalJson.Encode(permutedLowering.SourceMap)) &&
                          CanonicalJson.Encode(lowering.Obligations).SequenceEqual(CanonicalJson.Encode(permutedLowering.Obligations)), "declaration-permutation");
                    var directory = Path.Combine(evidence, shape + "-" + variant); Directory.CreateDirectory(directory);
                    File.WriteAllBytes(Path.Combine(directory, "candidate.dfy"), lowering.SourceBytes);
                    File.WriteAllBytes(Path.Combine(directory, "source-map.json"), CanonicalJson.Encode(lowering.SourceMap));
                    var proof = await G02DafnyReplay.RunFixtureAsync(tool, Inputs(lowering), Path.Combine(directory, "proof"));
                    var translated = await G02DafnyReplay.TranslateFixtureAsync(tool, Inputs(lowering), Path.Combine(directory, "translation"));
                    Check(proof.Verified.TranscriptBytes.SequenceEqual(translated.Verified.TranscriptBytes) &&
                          proof.Verified.ObligationVectorBytes.SequenceEqual(translated.Verified.ObligationVectorBytes), "proof-translation-equality");
                    File.WriteAllBytes(Path.Combine(directory, "transcript.json"), translated.Verified.TranscriptBytes);
                    File.WriteAllBytes(Path.Combine(directory, "obligations.json"), translated.Verified.ObligationVectorBytes);
                    buildCalls++;
                    var build = await G02OfflineFixtureBuild.RunAsync(sdk, translated,
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"), Path.Combine(directory, "compiled"));
                    Check(build.PublishedFiles.Length == 190 && build.PublishInventoryDigest == G02PublishInventory.DiagnosticDigest(build.PublishedFiles), "r2r-publish-inventory");
                    var buildRoot = Directory.GetDirectories(Path.Combine(directory, "compiled")).Single();
                    using var receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(buildRoot, "receipt.json")));
                    Check(receipt.RootElement.GetProperty("projectSha256").GetString() == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(buildRoot, "Generated.csproj")))), "held-project-receipt-hash");
                    var context = new AssemblyLoadContext("strogo-generality-" + Guid.NewGuid().ToString("N"), true);
                    var vectors = new List<object>();
                    try
                    {
                        using var stream = new MemoryStream(build.AssemblyBytes, writable: false);
                        var assembly = context.LoadFromStream(stream);
                        using var dispatch = new G02CompiledDispatch(assembly, module, owner);
                        var binding = OwnerContractBinderV04.Bind(module, owner);
                        var firstEntry = binding.Entries[0];
                        var witnessById = firstEntry.Witnesses[0].Arguments.ToDictionary(argument => argument.ParameterId, argument => argument.Value);
                        var typedRequest = JsonNode.Parse(Request(firstEntry.Function, firstEntry.Function.Parameters.Select(parameter => witnessById[parameter.Id]).ToArray()))!;
                        typedRequest["arguments"]![0]!["value"] = new JsonObject { ["type"] = "Bool", ["value"] = true };
                        foreach (var bad in new[] { (Id: "malformed", Input: Encoding.UTF8.GetBytes("{}")), (Id: "wrong-type", Input: Encoding.UTF8.GetBytes(typedRequest.ToJsonString())) })
                        {
                            var callbacks = 0;
                            var response = G02InvocationCodec.Invoke(module, bad.Input, (id, arguments) => { callbacks++; return dispatch.Invoke(id, arguments); });
                            using var parsed = JsonDocument.Parse(response);
                            Check(callbacks == 0 && parsed.RootElement.GetProperty("status").GetString() == "Refused" &&
                                  response.SequenceEqual(dispatch.InvokeJson(bad.Input)), "malformed-before-shared-callback");
                            vectors.Add(new { id = bad.Id, request = Encoding.UTF8.GetString(bad.Input), response = Encoding.UTF8.GetString(response), callbacks });
                        }
                        foreach (var entry in binding.Entries)
                        {
                            foreach (var witness in entry.Witnesses)
                            {
                                var byId = witness.Arguments.ToDictionary(value => value.ParameterId, value => value.Value);
                                InvokeAndCompare(entry, witness.Id, entry.Function.Parameters.Select(parameter => byId[parameter.Id]).ToArray(), witness.ModelResult);
                            }
                            if (shape == "allocation")
                                foreach (var length in new[] { 0, 256 })
                                {
                                    ModuleValue[] args = [new ModuleSequence(new TypeRef("I64"), 256, System.Collections.Immutable.ImmutableArray.CreateRange<ModuleValue>(Enumerable.Repeat(new ModuleI64(1), length))), new ModuleI64(256)];
                                    var environment = entry.Function.Parameters.Select((parameter, index) => (parameter.Id, args[index])).ToDictionary(pair => pair.Id, pair => pair.Item2);
                                    var expected = OwnerModelEvaluatorV04.Evaluate(entry.Model.Body, environment.ToImmutableDictionary(), owner.Types, "generality-boundary");
                                    InvokeAndCompare(entry, "length-" + length, args, expected);
                                }
                            var invalid = entry.Function.Parameters.Select(parameter => parameter.Type.Kind == "Bool" ? (ModuleValue)new ModuleBool(true) : parameter.Type.Kind == "Seq" ? new ModuleSequence(parameter.Type.Element!, parameter.Type.Capacity!.Value, System.Collections.Immutable.ImmutableArray<ModuleValue>.Empty) : new ModuleI64(shape == "scalar" ? long.MaxValue : -1)).ToArray();
                            var refused = dispatch.InvokeJson(Request(entry.Function, invalid));
                            using var parsed = JsonDocument.Parse(refused);
                            Check(parsed.RootElement.GetProperty("status").GetString() == "Refused" && parsed.RootElement.GetProperty("error").GetProperty("code").GetString() == "CompiledPreconditionFailed", "requires-refusal");
                            vectors.Add(new { id = "requires-invalid-" + entry.Function.Id, response = Encoding.UTF8.GetString(refused) });
                        }
                        void InvokeAndCompare(BoundOwnerEntryV04 entry, string id, ModuleValue[] args, ModuleValue ownerValue)
                        {
                            var reference = ModulesReferenceEvaluator.Invoke(module, entry.Function.Id, args).Value;
                            var request = Request(entry.Function, args); var response = dispatch.InvokeJson(request);
                            using var parsed = JsonDocument.Parse(response);
                            var expected = OwnerBundleCodec.ValuePayload(ownerValue, entry.Function.ReturnType);
                            Check(parsed.RootElement.GetProperty("status").GetString() == "Returned" &&
                                  CanonicalJson.Encode(parsed.RootElement.GetProperty("value")).SequenceEqual(CanonicalJson.Encode(expected)) && OwnerContractEvaluator.StructuralEquals(ownerValue, reference), "compiled-owner-reference");
                            vectors.Add(new { id, functionId = entry.Function.Id, request = Encoding.UTF8.GetString(request), response = Encoding.UTF8.GetString(response),
                                expectedOwner = expected, expectedReference = OwnerBundleCodec.ValuePayload(reference, entry.Function.ReturnType) });
                        }
                    }
                    finally { context.Unload(); }
                    File.WriteAllBytes(Path.Combine(directory, "invocations.json"), CanonicalJson.Encode(vectors));
                    rows.Add(new { id = shape + "-" + variant, moduleDigest = module.SourceDigest, bundleDigest = owner.BundleDigest,
                        publishInventoryDigest = build.PublishInventoryDigest, entrySha256 = Convert.ToHexStringLower(SHA256.HashData(build.AssemblyBytes)), vectors = vectors.Count, disposition = "validation-only" });
                }
            }
            Check(loads == 4 && buildCalls == 4, "four-real-builds-and-loads");
            File.WriteAllBytes(Path.Combine(evidence, "report.json"), CanonicalJson.Encode(new { purpose = "validation-only-no-human-admission", checks, buildCalls, assemblyLoads = loads, rows }));
            Console.WriteLine($"PASS compiler/ABI generality checks={checks} builds={buildCalls} loads={loads}");
        }
        finally { AppDomain.CurrentDomain.AssemblyLoad -= observer; }
    }

    private static bool IsHelperCall(IrInstruction instruction) => instruction.Op == "call" && instruction.Metadata.FunctionRef == "advance.alpha-v2";
    private static ModuleIr Compile(byte[] bytes) => ModulesCompiler.Compile(ModulesParser.ParseModule(bytes));
    private static G02RegeneratedProofInputs Inputs(DafnyFoldLoweringResult lowering) => new(lowering.SourceBytes, CanonicalJson.Encode(lowering.SourceMap), lowering.Obligations);
    private static byte[] Request(FunctionIr function, ModuleValue[] values) => CanonicalJson.Encode(new { schemaVersion = G02InvocationCodec.RequestVersion, functionId = function.Id,
        arguments = function.Parameters.Select((parameter, index) => new { parameterId = parameter.Id, value = OwnerBundleCodec.ValuePayload(values[index], parameter.Type) }).Reverse().ToArray() });
    private static byte[] PermuteDeclarations(byte[] source)
    {
        var root = JsonNode.Parse(source)!;
        void Visit(JsonNode? value)
        {
            if (value is JsonObject obj)
                foreach (var pair in obj.ToArray())
                {
                    if (pair.Key is "types" or "functions" or "exports" or "nodes" && pair.Value is JsonArray array)
                    { var items = array.Select(item => item!.DeepClone()).Reverse().ToArray(); array.Clear(); foreach (var item in items) array.Add(item); }
                    Visit(pair.Value);
                }
            else if (value is JsonArray array) foreach (var item in array) Visit(item);
        }
        Visit(root); return Encoding.UTF8.GetBytes(root.ToJsonString());
    }
}
