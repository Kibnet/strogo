using System.Collections.Immutable;
using System.Text;
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

static void CreateFakeDotNetRuntime(string root, string executable)
{
    Directory.CreateDirectory(Path.Combine(root, "host", "fxr", "10.0.11"));
    Directory.CreateDirectory(Path.Combine(root, "shared", "Microsoft.NETCore.App", "10.0.11"));
    File.WriteAllText(Path.Combine(root, executable), "launcher-v1", Encoding.ASCII);
    File.WriteAllText(Path.Combine(root, "host", "fxr", "10.0.11", "hostfxr.bin"), "hostfxr-v1", Encoding.ASCII);
    File.WriteAllText(Path.Combine(root, "shared", "Microsoft.NETCore.App", "10.0.11", "coreclr.bin"), "runtime-v1", Encoding.ASCII);
}

static bool EqualTrees(string left, string right)
{
    var leftFiles = Directory.GetFiles(left, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(left, path).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal).ToArray();
    var rightFiles = Directory.GetFiles(right, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(right, path).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal).ToArray();
    return leftFiles.SequenceEqual(rightFiles, StringComparer.Ordinal) && leftFiles.All(relative => File.ReadAllBytes(Path.Combine(left, relative.Replace('/', Path.DirectorySeparatorChar))).SequenceEqual(File.ReadAllBytes(Path.Combine(right, relative.Replace('/', Path.DirectorySeparatorChar)))));
}
