using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kernel.Core;
using Strogo.Modules;

internal static class G02SignedSessionChecks
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now = now;
        internal Action? BeforeRead;
        public override DateTimeOffset GetUtcNow() { BeforeRead?.Invoke(); return Now; }
    }

    internal static int Run(string directory, string sourcePackage, G02VerifiedFixtureBuild verified,
        RSA owner, RSA impostor, byte[] publicKey, string keyId, byte[] state0, byte[] state1,
        byte[] policyDriftState, OwnerContractBindingV04 binding, DateTimeOffset now, G02RuntimeBinding runtime)
    {
        var root = Path.Combine(directory, "session-package");
        Directory.CreateDirectory(root);
        foreach (var file in Directory.GetFiles(sourcePackage, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(root, Path.GetRelativePath(sourcePackage, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
        G02BuildManifest manifest;
        using (var snapshot = G02PackageSnapshot.OpenStructural(root)) manifest = snapshot.Manifest;
        var checks = 0; var rows = new List<object>();
        void Check(bool condition, string id) { if (!condition) throw new Exception("session " + id); checks++; }
        byte[] Release(RSA? signer = null, string? packageDigest = null, DateTimeOffset? until = null)
        {
            using var state = JsonDocument.Parse(state0);
            var fields = new SortedDictionary<string, object>(StringComparer.Ordinal) {
                ["schemaVersion"]="strogo.admission.v0.2", ["admissionId"]="fixture.session",
                ["approvedBy"]="fixture", ["issuedAt"]=now.AddMinutes(-2).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
                ["validUntil"]=(until??now.AddMinutes(10)).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
                ["contractApprovalDigest"]=manifest.ContractApprovalDigest, ["proofDigest"]=manifest.ProofDigest,
                ["buildManifestDigest"]=manifest.ArtifactDigest, ["toolchainDigest"]=manifest.ToolchainDigest,
                ["policyDigest"]=state.RootElement.GetProperty("policyDigest").GetString()!, ["approvalEpoch"]=0L,
                ["packageDigest"]=packageDigest??manifest.PackageDigest, ["signatureAlgorithm"]="rsa-pss-sha256", ["keyId"]=keyId };
            // OwnerAdmissionWire preserves numeric epoch; CanonicalJson.Encode would turn numbers into strings.
            var payload = JsonSerializer.SerializeToUtf8Bytes(fields);
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes("strogo.admission.v0.2/payload\n").Concat(payload).ToArray());
            var message = Encoding.UTF8.GetBytes("strogo.admission.v0.2/signature\n").Concat(digest).ToArray();
            fields["signature"] = Convert.ToBase64String((signer??owner).SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)).TrimEnd('=').Replace('+','-').Replace('/','_');
            return JsonSerializer.SerializeToUtf8Bytes(fields);
        }
        var entry = binding.Entries[0]; var witness = entry.Witnesses[0];
        var byId = witness.Arguments.ToDictionary(value=>value.ParameterId,value=>value.Value);
        var request = CanonicalJson.Encode(new { schemaVersion=G02InvocationCodec.RequestVersion, functionId=entry.Function.Id,
            arguments=entry.Function.Parameters.Select(parameter=>new { parameterId=parameter.Id, value=OwnerBundleCodec.ValuePayload(byId[parameter.Id],parameter.Type) }).ToArray() });
        var loads = 0;
        using var pe = new PEReader(new MemoryStream(verified.Build.AssemblyBytes));
        var assemblyName = pe.GetMetadataReader().GetString(pe.GetMetadataReader().GetAssemblyDefinition().Name);
        AssemblyLoadEventHandler observer = (_, args) => { if (args.LoadedAssembly.GetName().Name==assemblyName) loads++; };
        AppDomain.CurrentDomain.AssemblyLoad += observer;
        void ExclusiveReopen(string id)
        {
            using var reopened = File.Open(Path.Combine(root, manifest.EntryAssemblyPath), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Check(reopened.CanWrite,id);
        }
        try
        {
            foreach (var bad in new[] { (Id:"signature", Bytes:Release(impostor), Code:"InvalidOwnerSignature"),
                (Id:"digest", Bytes:Release(packageDigest:new string('a',64)), Code:"ReleaseAdmissionDigestMismatch"),
                (Id:"expiry", Bytes:Release(until:now.AddMinutes(-1)), Code:"ReleaseAdmissionExpired") })
            {
                using var trust = new OwnerTrust(publicKey,keyId); var before=loads; string? code=null;
                try { using var unexpected=G02SignedFixtureSession.Open(root,verified,trust,()=>state0,bad.Bytes,binding.Bundle.BundleDigest,new Clock(now),runtime); }
                catch (ModuleException error) { code=error.Code; }
                Check(code==bad.Code && loads==before,"no-load-"+bad.Id); ExclusiveReopen("cleanup-"+bad.Id);
                rows.Add(new { id="no-load-"+bad.Id,code,assemblyLoads=loads-before,exclusiveReopen=true });
            }
            using (var trust = new OwnerTrust(publicKey,keyId))
            {
                var before=loads;string? code=null;
                try { using var unexpected=G02SignedFixtureSession.Open(root,verified,trust,()=>state1,Release(),binding.Bundle.BundleDigest,new Clock(now),runtime); }
                catch (ModuleException error) { code=error.Code; }
                Check(code=="ApprovalEpochMismatch" && loads==before,"no-load-epoch");ExclusiveReopen("cleanup-epoch");
                rows.Add(new { id="no-load-epoch",code,assemblyLoads=loads-before,exclusiveReopen=true });
            }
            using (var trust = new OwnerTrust(publicKey,keyId))
            {
                var state=state0.ToArray();var clock=new Clock(now);var reads=0;
                byte[] Provider() { reads++; return state; }
                var release=Release();var releaseIdentity=OwnerAdmissionWire.Hash("strogo.admission.v0.2/artifact",release);var before=loads;
                clock.BeforeRead=()=>state[0]=0; // Mutation after provider copy, before both signature checks.
                using var session=G02SignedFixtureSession.Open(root,verified,trust,Provider,release,binding.Bundle.BundleDigest,clock,runtime);
                Check(reads==1 && loads==before+1,"open-fresh-copy");
                Array.Clear(release); state=state0.ToArray();
                var success=session.InvokeJson(request);
                using (var result=JsonDocument.Parse(success))
                {
                    var envelope=result.RootElement;var nested=envelope.GetProperty("result");
                    var reference=ModulesReferenceEvaluator.Invoke(binding.Module,entry.Function.Id,entry.Function.Parameters.Select(parameter=>byId[parameter.Id]).ToArray()).Value;
                    Check(envelope.GetProperty("validationOnly").GetBoolean() && envelope.GetProperty("packageDigest").GetString()==manifest.PackageDigest &&
                        envelope.GetProperty("buildManifestDigest").GetString()==manifest.ArtifactDigest && envelope.GetProperty("contractApprovalDigest").GetString()==manifest.ContractApprovalDigest &&
                        envelope.GetProperty("releaseAdmissionDigest").GetString()==releaseIdentity && nested.GetProperty("status").GetString()=="Returned","identity");
                    Check(CanonicalJson.Encode(nested.GetProperty("value")).SequenceEqual(CanonicalJson.Encode(OwnerBundleCodec.ValuePayload(witness.ModelResult,entry.Function.ReturnType))) && OwnerContractEvaluator.StructuralEquals(reference,witness.ModelResult),"owner-reference-output");
                }
                Check(reads==2 && session.DispatchAttempts==1,"invoke-fresh-copy-and-immutable-release");
                rows.Add(new { id="valid-json", response=Encoding.UTF8.GetString(success),providerReads=reads,dispatchAttempts=session.DispatchAttempts });
                clock.BeforeRead=null;state=state0.ToArray();
                void Refused(byte[] input,string expected,bool readState=true)
                {
                    var attempts=session.DispatchAttempts;var priorReads=reads;var bytes=session.InvokeJson(input);
                    using var result=JsonDocument.Parse(bytes);var nested=result.RootElement.GetProperty("result");
                    Check(nested.GetProperty("status").GetString()=="Refused" && nested.GetProperty("error").GetProperty("code").GetString()==expected &&
                        session.DispatchAttempts==attempts && reads==priorReads+(readState?1:0),expected);
                    rows.Add(new { id=expected,response=Encoding.UTF8.GetString(bytes),providerReadsDelta=reads-priorReads,dispatchAttemptsDelta=session.DispatchAttempts-attempts });
                }
                Refused(Encoding.UTF8.GetBytes("{}"),"SchemaInvalid",false);
                clock.Now=now.AddMinutes(11);Refused(request,"ReleaseAdmissionExpired");
                clock.Now=now.AddHours(2);Refused(request,"ContractApprovalExpired");
                clock.Now=now;state=null!;Refused(request,"OwnerStateUnavailable");
                state=new byte[65537];Refused(request,"OwnerStateLimitExceeded");
                state=policyDriftState;Refused(request,"OwnerStateRollbackDetected");
                state=state1;Refused(request,"ApprovalEpochMismatch");
                state=state0;Refused(request,"OwnerStateRollbackDetected");
                var loadCount=loads;string? newSessionCode=null;
                try { using var unexpected=G02SignedFixtureSession.Open(root,verified,trust,Provider,Release(),binding.Bundle.BundleDigest,clock,runtime); }
                catch(ModuleException error){newSessionCode=error.Code;}
                Check(newSessionCode=="OwnerStateRollbackDetected" && loads==loadCount,"shared-high-water");
                rows.Add(new { id="shared-high-water",code=newSessionCode,assemblyLoads=loads-loadCount });
                session.Dispose(); Refused(request,"CompiledFixtureDisposed",false); Refused(Encoding.UTF8.GetBytes("{}"),"CompiledFixtureDisposed",false); ExclusiveReopen("dispose-cleanup");
            }
            using (var trust = new OwnerTrust(publicKey,keyId))
            {
                var failed=false;var reads=0;byte[] Provider(){reads++;if(failed)throw new IOException("fixture unavailable");return state0;}
                using var session=G02SignedFixtureSession.Open(root,verified,trust,Provider,Release(),binding.Bundle.BundleDigest,new Clock(now),runtime);
                failed=true;using var result=JsonDocument.Parse(session.InvokeJson(request));
                Check(reads==2 && session.DispatchAttempts==0 && result.RootElement.GetProperty("result").GetProperty("error").GetProperty("code").GetString()=="OwnerStateUnavailable","io-before-dispatch");
                rows.Add(new { id="io-unavailable",response=result.RootElement.Clone(),providerReads=reads,dispatchAttempts=session.DispatchAttempts });
            }
            foreach (var failure in new (string Id, Func<Exception> Create)[] {
                ("invalid-operation", () => new InvalidOperationException("provider unavailable")),
                ("disposed", () => new ObjectDisposedException("provider")),
                ("io", () => new IOException("provider unavailable")),
                ("forged-module", () => ModulesExceptionFactory.Error("admission", "ApprovalEpochMismatch")) })
            {
                using var trust = new OwnerTrust(publicKey,keyId);
                var reads=0;var broken=true;
                byte[] Provider(){reads++;if(broken)throw failure.Create();return state0;}
                var before=loads;string? code=null;
                try { using var unexpected=G02SignedFixtureSession.Open(root,verified,trust,Provider,Release(),binding.Bundle.BundleDigest,new Clock(now),runtime); }
                catch(ModuleException error){code=error.Code;}
                Check(code=="OwnerStateUnavailable" && loads==before && reads==1,"provider-no-load-"+failure.Id);
                ExclusiveReopen("provider-no-load-cleanup-"+failure.Id);
                rows.Add(new { id="provider-no-load-"+failure.Id,code,assemblyLoads=loads-before,providerReads=reads,exclusiveReopen=true });
                broken=false;
                using(var session=G02SignedFixtureSession.Open(root,verified,trust,Provider,Release(),binding.Bundle.BundleDigest,new Clock(now),runtime))
                {
                    broken=true;var priorReads=reads;
                    using(var result=JsonDocument.Parse(session.InvokeJson(request)))
                    {
                        var nested=result.RootElement.GetProperty("result");
                        Check(nested.GetProperty("status").GetString()=="Refused" &&
                            nested.GetProperty("error").GetProperty("stage").GetString()=="admission" &&
                            nested.GetProperty("error").GetProperty("code").GetString()=="OwnerStateUnavailable" &&
                            reads==priorReads+1 && session.DispatchAttempts==0,"provider-late-"+failure.Id);
                        rows.Add(new { id="provider-late-"+failure.Id,response=result.RootElement.Clone(),providerReadsDelta=reads-priorReads,dispatchAttempts=session.DispatchAttempts });
                    }
                    priorReads=reads;
                    using(var malformed=JsonDocument.Parse(session.InvokeJson(Encoding.UTF8.GetBytes("{}"))))
                        Check(malformed.RootElement.GetProperty("result").GetProperty("error").GetProperty("code").GetString()=="SchemaInvalid" && reads==priorReads && session.DispatchAttempts==0,"provider-codec-first-"+failure.Id);
                    broken=false;priorReads=reads;
                    using(var restored=JsonDocument.Parse(session.InvokeJson(request)))
                    {
                        var nested=restored.RootElement.GetProperty("result");
                        var reference=ModulesReferenceEvaluator.Invoke(binding.Module,entry.Function.Id,entry.Function.Parameters.Select(parameter=>byId[parameter.Id]).ToArray()).Value;
                        Check(nested.GetProperty("status").GetString()=="Returned" && reads==priorReads+1 && session.DispatchAttempts==1 &&
                            CanonicalJson.Encode(nested.GetProperty("value")).SequenceEqual(CanonicalJson.Encode(OwnerBundleCodec.ValuePayload(witness.ModelResult,entry.Function.ReturnType))) &&
                            OwnerContractEvaluator.StructuralEquals(reference,witness.ModelResult),"provider-restored-"+failure.Id);
                        rows.Add(new { id="provider-restored-"+failure.Id,response=restored.RootElement.Clone(),providerReadsDelta=reads-priorReads,dispatchAttempts=session.DispatchAttempts,ownerReferenceMatch=true });
                    }
                }
                ExclusiveReopen("provider-session-cleanup-"+failure.Id);
            }
            var liveRuntime = G02RuntimeBinding.CaptureLive(); var runtimeObservation = liveRuntime; var captureFailure=false;
            var controlledRuntime = runtime.ObserveFixtureForChecks(() => captureFailure ? throw new IOException("runtime capture unavailable") : runtimeObservation);
            using (var trust = new OwnerTrust(publicKey,keyId))
            {
                runtimeObservation=liveRuntime with { CoreLibPath=Path.Combine(directory,"System.Private.CoreLib.dll") };
                var before=loads;string? runtimeCode=null;
                try { using var unexpected=G02SignedFixtureSession.Open(root,verified,trust,()=>state0,Release(),binding.Bundle.BundleDigest,new Clock(now),controlledRuntime); }
                catch(ModuleException error){runtimeCode=error.Code;}
                Check(runtimeCode=="RuntimeBindingMismatch" && loads==before,"runtime-before-load"); ExclusiveReopen("runtime-load-cleanup");
                rows.Add(new { id="runtime-before-load",code=runtimeCode,assemblyLoads=loads-before,observationSource="synthetic-host-control" });
                runtimeObservation=liveRuntime;
                var wrongSdk=verified with { Build=verified.Build with { SdkClosureDigest=new string('a',64) } };
                string? sdkCode=null;
                try { using var unexpected=G02SignedFixtureSession.Open(root,wrongSdk,trust,()=>state0,Release(),binding.Bundle.BundleDigest,new Clock(now),runtime); }
                catch(ModuleException error){sdkCode=error.Code;}
                Check(sdkCode=="RuntimeBindingMismatch" && loads==before,"sdk-binding-before-load"); ExclusiveReopen("sdk-load-cleanup");
                rows.Add(new { id="sdk-binding-before-load",code=sdkCode,assemblyLoads=loads-before });
            }
            using (var trust = new OwnerTrust(publicKey,keyId))
            {
                var reads=0; byte[] Provider(){reads++;return state0;}
                using var session=G02SignedFixtureSession.Open(root,verified,trust,Provider,Release(),binding.Bundle.BundleDigest,new Clock(now),controlledRuntime);
                using(var positive=JsonDocument.Parse(session.InvokeJson(request)))
                    Check(positive.RootElement.GetProperty("result").GetProperty("status").GetString()=="Returned" && session.DispatchAttempts==1,"runtime-positive-dispatch");
                runtimeObservation=liveRuntime with { CoreLibPath=Path.Combine(directory,"System.Private.CoreLib.dll") };
                using(var late=JsonDocument.Parse(session.InvokeJson(request)))
                    Check(late.RootElement.GetProperty("result").GetProperty("error").GetProperty("code").GetString()=="RuntimeBindingMismatch" && session.DispatchAttempts==1,"runtime-late-before-dispatch");
                rows.Add(new { id="runtime-late-before-dispatch",code="RuntimeBindingMismatch",dispatchAttemptsDelta=0,observationSource="synthetic-host-control" });
                runtimeObservation=liveRuntime;captureFailure=true;
                using(var failed=JsonDocument.Parse(session.InvokeJson(request)))
                    Check(failed.RootElement.GetProperty("result").GetProperty("error").GetProperty("code").GetString()=="RuntimeBindingUnavailable" && session.DispatchAttempts==1,"runtime-unavailable-before-dispatch");
                rows.Add(new { id="runtime-unavailable-before-dispatch",code="RuntimeBindingUnavailable",dispatchAttemptsDelta=0 });
                var priorReads=reads;
                using(var malformed=JsonDocument.Parse(session.InvokeJson(Encoding.UTF8.GetBytes("{}"))))
                    Check(malformed.RootElement.GetProperty("result").GetProperty("error").GetProperty("code").GetString()=="SchemaInvalid" && reads==priorReads && session.DispatchAttempts==1,"codec-before-runtime");
                captureFailure=false;
            }
            ExclusiveReopen("final-cleanup");
        }
        finally { AppDomain.CurrentDomain.AssemblyLoad -= observer; }
        File.WriteAllBytes(Path.Combine(directory,"signed-session.json"),CanonicalJson.Encode(new { purpose="disposable-test-key-session-not-public-admission",checks,rows }));
        return checks;

    }
}
