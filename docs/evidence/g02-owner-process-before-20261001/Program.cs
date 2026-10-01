using System.Security.Cryptography;
using System.Text.Json;
using Strogo.Modules;
var fixture = JsonDocument.Parse(File.ReadAllBytes(args[0])).RootElement;
var originalConfig = JsonDocument.Parse(File.ReadAllBytes(fixture.GetProperty("operatorConfigPath").GetString()!)).RootElement;
var root = Path.GetFullPath(args[1]); var store = Path.Combine(root,"store"); Directory.CreateDirectory(store);
var key = File.ReadAllBytes(originalConfig.GetProperty("publicKeyPath").GetString()!);
var state0 = File.ReadAllBytes(fixture.GetProperty("initialStatePath").GetString()!);
var state1 = File.ReadAllBytes(fixture.GetProperty("nextStatePath").GetString()!);
var keyPath = Path.Combine(root,"public-key.der"); File.WriteAllBytes(keyPath,key);
var statePath = Path.Combine(store,"owner-state.json"); File.WriteAllBytes(statePath,state0);
var configPath = Path.Combine(root,"host.json");
File.WriteAllBytes(configPath, JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion="strogo.owner-trust-config.v0.1",keyId=Convert.ToHexStringLower(SHA256.HashData(key)),publicKeyPath=keyPath,ownerStateStore=store }));
void Replace(byte[] bytes) { var temporary=Path.Combine(store,Guid.NewGuid().ToString("N")+".tmp");File.WriteAllBytes(temporary,bytes);File.Move(temporary,statePath,true); }
var rows=new List<object>();
using(var hostA=OwnerHostContext.Open(configPath)) {
 var initial=hostA.Trust.VerifyOwnerState(hostA.ReadCurrentState());
 Replace(state1);var advanced=hostA.Trust.VerifyOwnerState(hostA.ReadCurrentState());
 if(initial.ApprovalEpoch!=0 || advanced.ApprovalEpoch!=1)throw new Exception("signed advance failed");
 Replace(state0);
 string? sameHostCode=null;try{hostA.Trust.VerifyOwnerState(hostA.ReadCurrentState());}catch(ModuleException error){sameHostCode=error.Code;}
 if(sameHostCode!="OwnerStateRollbackDetected")throw new Exception("same host rollback not rejected");
 using var hostB=OwnerHostContext.Open(configPath);
 string? otherHostCode=null;long? otherHostEpoch=null;try{otherHostEpoch=hostB.Trust.VerifyOwnerState(hostB.ReadCurrentState()).ApprovalEpoch;}catch(ModuleException error){otherHostCode=error.Code;}
 rows.Add(new { id="live-two-hosts", sameHostCode, otherHostCode,otherHostEpoch,gapReproduced=otherHostEpoch==0 });
}
using(var hostC=OwnerHostContext.Open(configPath)) {
 string? code=null;long? epoch=null;try{epoch=hostC.Trust.VerifyOwnerState(hostC.ReadCurrentState()).ApprovalEpoch;}catch(ModuleException error){code=error.Code;}
 rows.Add(new {id="after-dispose-new-host",code,epoch,gapReproduced=epoch==0});
}
var report=new {purpose="before-fix-process-local-owner-high-water-reproduction",pid=Environment.ProcessId,rows,state0Sha256=Convert.ToHexStringLower(SHA256.HashData(state0)),state1Sha256=Convert.ToHexStringLower(SHA256.HashData(state1)),privateKeysExported=false,originalFixtureUntouched=true};
File.WriteAllBytes(Path.Combine(root,"report.json"),JsonSerializer.SerializeToUtf8Bytes(report));Console.WriteLine(JsonSerializer.Serialize(report));
