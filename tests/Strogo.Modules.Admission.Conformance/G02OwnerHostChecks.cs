using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Strogo.Modules;

internal static class G02OwnerHostChecks
{
    internal static int Run(string repoRoot, string directory)
    {
        Directory.CreateDirectory(directory);
        var checks=0;var rows=new List<object>();
        void Check(bool condition,string id) { if(!condition)throw new Exception("owner-host "+id);checks++; }
        void Refused(Action action,string code,string id)
        {
            string? actual=null;try{action();}catch(ModuleException error){actual=error.Code;}
            Check(actual==code,id);rows.Add(new {id,code=actual});
        }
        using var owner=RSA.Create(3072);using var impostor=RSA.Create(3072);
        var key=owner.ExportSubjectPublicKeyInfo();var keyId=Convert.ToHexStringLower(SHA256.HashData(key));
        byte[] State(long epoch,RSA? signer=null)
        {
            var policy=new SortedDictionary<string,object>(StringComparer.Ordinal) {
                ["schemaVersion"]="strogo.owner-state.v0.2",["keyId"]=keyId,
                ["supportedAlgorithms"]=new[]{"rsa-pss-sha256"},["maxContractLifetimeSeconds"]=3600L,["maxAdmissionLifetimeSeconds"]=3600L };
            var fields=new SortedDictionary<string,object>(policy,StringComparer.Ordinal) {
                ["approvalEpoch"]=epoch,["issuedAt"]="2026-10-01T00:00:00.000Z",
                ["policyDigest"]=OwnerAdmissionWire.Hash("strogo.owner-policy.v0.2/payload",JsonSerializer.SerializeToUtf8Bytes(policy)),
                ["signatureAlgorithm"]="rsa-pss-sha256" };
            var digest=OwnerAdmissionWire.HashBytes("strogo.owner-state.v0.2/payload",JsonSerializer.SerializeToUtf8Bytes(fields));
            var message=Encoding.UTF8.GetBytes("strogo.owner-state.v0.2/signature\n").Concat(digest).ToArray();
            fields["signature"]=Convert.ToBase64String((signer??owner).SignData(message,HashAlgorithmName.SHA256,RSASignaturePadding.Pss)).TrimEnd('=').Replace('+','-').Replace('/','_');
            return JsonSerializer.SerializeToUtf8Bytes(fields);
        }
        var state0=State(0);var state1=State(1);
        var store=Path.Combine(directory,"store");var otherStore=Path.Combine(directory,"other-store");
        Directory.CreateDirectory(store);Directory.CreateDirectory(otherStore);
        var statePath=Path.Combine(store,"owner-state.json");
        var keyPath=Path.Combine(directory,"owner.spki");var configPath=Path.Combine(directory,"host.json");
        File.WriteAllBytes(keyPath,key);File.WriteAllBytes(statePath,state0);
        Dictionary<string,object> Config() => new(StringComparer.Ordinal) {
            ["schemaVersion"]="strogo.owner-trust-config.v0.1",["keyId"]=keyId,
            ["publicKeyPath"]=keyPath,["ownerStateStore"]=store };
        var configBytes=JsonSerializer.SerializeToUtf8Bytes(Config(),new JsonSerializerOptions {WriteIndented=true});
        File.WriteAllBytes(configPath,configBytes);
        using(var host=OwnerHostContext.Open(configPath))
        {
            Check(ReferenceEquals(host.Trust,host.Trust) && host.Trust.KeyId==keyId && host.OwnerStateDirectory==store,"host-pin");
            var first=host.ReadCurrentState();Check(first.SequenceEqual(state0) && host.Trust.VerifyOwnerState(first).ApprovalEpoch==0,"initial-state");
            Array.Clear(first);Check(host.ReadCurrentState().SequenceEqual(state0),"fresh-owned-bytes");
            var redirected=Config();redirected["ownerStateStore"]=otherStore;redirected["keyId"]=new string('f',64);
            File.WriteAllBytes(configPath,JsonSerializer.SerializeToUtf8Bytes(redirected));File.WriteAllBytes(keyPath,impostor.ExportSubjectPublicKeyInfo());
            Check(host.Trust.KeyId==keyId && host.OwnerStateDirectory==store && host.ReadCurrentState().SequenceEqual(state0),"frozen-anchor-and-store");
            var next=Path.Combine(store,"next.tmp");File.WriteAllBytes(next,state1);File.Replace(next,statePath,null);
            Check(host.ReadCurrentState().SequenceEqual(state1) && host.Trust.VerifyOwnerState(host.ReadCurrentState()).ApprovalEpoch==1,"atomic-epoch-replacement");
            File.WriteAllBytes(statePath,state0);
            Refused(()=>host.Trust.VerifyOwnerState(host.ReadCurrentState()),"OwnerStateRollbackDetected","shared-high-water");
            File.WriteAllBytes(statePath,State(2,impostor));
            Refused(()=>host.Trust.VerifyOwnerState(host.ReadCurrentState()),"InvalidOwnerSignature","wrong-state-signature");
            File.WriteAllBytes(statePath,[]);Check(host.ReadCurrentState().Length==0,"empty-state-raw");
            Refused(()=>host.Trust.VerifyOwnerState(host.ReadCurrentState()),"ArtifactSizeInvalid","empty-state-verifier");
            File.WriteAllBytes(statePath,new byte[65536]);Check(host.ReadCurrentState().Length==65536,"state-boundary");
            File.WriteAllBytes(statePath,new byte[65537]);Refused(()=>host.ReadCurrentState(),"OwnerStateLimitExceeded","state-oversize");
            File.Delete(statePath);Refused(()=>host.ReadCurrentState(),"OwnerStateUnavailable","state-missing");
            File.WriteAllBytes(statePath,state1);
            host.Dispose();host.Dispose();
            Refused(()=>host.ReadCurrentState(),"OwnerHostDisposed","disposed-state");
            Refused(()=>{_=host.Trust;},"OwnerHostDisposed","disposed-trust");
            Refused(()=>{_=host.OwnerStateDirectory;},"OwnerHostDisposed","disposed-directory");
        }
        File.WriteAllBytes(keyPath,key);File.WriteAllBytes(configPath,configBytes);
        void BadConfig(Dictionary<string,object> fields,string code,string id)
        {
            File.WriteAllBytes(configPath,JsonSerializer.SerializeToUtf8Bytes(fields));
            Refused(()=>{using var host=OwnerHostContext.Open(configPath);},code,id);
        }
        var bad=Config();bad["schemaVersion"]="wrong";BadConfig(bad,"OwnerConfigurationInvalid","config-version");
        bad=Config();bad["extra"]="x";BadConfig(bad,"OwnerConfigurationInvalid","config-unknown");
        bad=Config();bad.Remove("keyId");BadConfig(bad,"OwnerConfigurationInvalid","config-missing-field");
        bad=Config();bad["keyId"]=42;BadConfig(bad,"OwnerConfigurationInvalid","config-type");
        bad=Config();bad["publicKeyPath"]="relative.spki";BadConfig(bad,"OperatorPathInvalid","relative-key-path");
        bad=Config();bad["ownerStateStore"]="relative-store";BadConfig(bad,"OperatorPathInvalid","relative-state-path");
        bad=Config();bad["ownerStateStore"]=Path.Combine(directory,"absent");BadConfig(bad,"OwnerConfigurationUnavailable","missing-store");
        bad=Config();bad["keyId"]=new string('f',64);BadConfig(bad,"PinnedKeyMismatch","wrong-key-pin");
        var duplicate=Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(Config())).Replace("{","{\"keyId\":\"x\",",StringComparison.Ordinal);
        File.WriteAllText(configPath,duplicate,new UTF8Encoding(false));Refused(()=>{using var host=OwnerHostContext.Open(configPath);},"OwnerConfigurationInvalid","config-duplicate");
        File.WriteAllBytes(configPath,[0xff]);Refused(()=>{using var host=OwnerHostContext.Open(configPath);},"OwnerConfigurationInvalid","config-invalid-utf8");
        File.WriteAllBytes(configPath,new byte[]{0xef,0xbb,0xbf}.Concat(configBytes).ToArray());Refused(()=>{using var host=OwnerHostContext.Open(configPath);},"OwnerConfigurationInvalid","config-bom");
        File.WriteAllBytes(configPath,[]);Refused(()=>{using var host=OwnerHostContext.Open(configPath);},"OwnerConfigurationInvalid","config-empty");
        var padded=configBytes.Concat(Enumerable.Repeat((byte)' ',65536-configBytes.Length)).ToArray();
        File.WriteAllBytes(configPath,padded);using(var boundary=OwnerHostContext.Open(configPath))Check(boundary.Trust.KeyId==keyId,"config-boundary");
        File.WriteAllBytes(configPath,padded.Append((byte)' ').ToArray());Refused(()=>{using var host=OwnerHostContext.Open(configPath);},"OwnerConfigurationLimitExceeded","config-oversize");
        File.Delete(configPath);Refused(()=>{using var host=OwnerHostContext.Open(configPath);},"OwnerConfigurationUnavailable","config-missing-file");
        File.WriteAllBytes(configPath,configBytes);File.Delete(keyPath);
        Refused(()=>{using var host=OwnerHostContext.Open(configPath);},"OwnerConfigurationUnavailable","key-missing-file");
        File.WriteAllBytes(keyPath,new byte[16385]);Refused(()=>{using var host=OwnerHostContext.Open(configPath);},"OwnerPublicKeyLimitExceeded","key-oversize");
        File.WriteAllBytes(keyPath,new byte[16384]);Refused(()=>{using var host=OwnerHostContext.Open(configPath);},"InvalidPublicKey","key-boundary-invalid-der");
        File.WriteAllBytes(keyPath,[]);Refused(()=>{using var host=OwnerHostContext.Open(configPath);},"InvalidPublicKey","key-empty");
        File.WriteAllBytes(keyPath,key);
        using(var exclusive=File.Open(keyPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(exclusive.CanWrite,"failed-open-cleanup");
        var cliPath=Path.Combine(repoRoot,"src","Strogo.Modules.Owner.Cli","bin","Debug","net10.0","Strogo.Modules.Owner.Cli.exe");
        foreach(var command in new[]{new[]{"approve-contract"},new[]{"state","advance-epoch"}})
        {
            var outputPath=Path.Combine(directory,"never-issued.json");
            var start=new ProcessStartInfo(cliPath) {UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in command)start.ArgumentList.Add(arg);
            var options=command.Length==1?new[]{"trust-config",configPath,"signer-config",Path.Combine(directory,"no-signer.json"),"bundle","absent","approved-by","fixture","provenance","absent","valid-until","absent","out",outputPath}:
                new[]{"trust-config",configPath,"signer-config","absent","expected-epoch","0"};
            for(var i=0;i<options.Length;i+=2){start.ArgumentList.Add("--"+options[i]);start.ArgumentList.Add(options[i+1]);}
            using var process=Process.Start(start)!;process.StandardInput.Close();
            var stdoutTask=process.StandardOutput.ReadToEndAsync();var stderrTask=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(10000)){process.Kill(entireProcessTree:true);throw new Exception("owner CLI gate timeout");}
            var stdout=stdoutTask.GetAwaiter().GetResult();var stderr=stderrTask.GetAwaiter().GetResult();
            using var refusal=JsonDocument.Parse(stderr.Trim());
            Check(process.ExitCode==2 && stdout.Length==0 && refusal.RootElement.GetProperty("code").GetString()=="InteractiveTerminalRequired" && !File.Exists(outputPath),"cli-tty-"+command[^1]);
            rows.Add(new {id="cli-tty-"+command[^1],exit=process.ExitCode,stdout,stderr,outputCreated=false});
        }
        File.WriteAllText(Path.Combine(directory,"report.json"),JsonSerializer.Serialize(new {purpose="host-context-fixture-not-human-admission",checks,rows},new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine($"PASS owner host context checks={checks} evidence={directory}");
        return checks;
    }
}
