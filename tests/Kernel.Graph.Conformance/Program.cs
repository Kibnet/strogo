using Kernel.Graph;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
var tool=new GraphToolchain(root);var reports=new List<object>();int checks=0;
void Check(bool yes,string message){checks++;if(!yes)throw new Exception(message);}
var fixture=Path.Combine(root,"fixtures/task-graph-v1");var evidence=Path.Combine(root,"artifacts/e04");
Directory.CreateDirectory(evidence);
var source=File.ReadAllBytes(Path.Combine(fixture,"program.accepted.json"));
var c01=Codec.Input(File.ReadAllBytes(Path.Combine(fixture,"c01.json")));
var watch=System.Diagnostics.Stopwatch.StartNew();
try {
 // All data-model scenarios use one verified, published program.
 var cliBuild=await Processes.Run("dotnet",["build","src/Kernel.Graph.Cli","-c","Release","--no-restore"],root,60);
 Check(cliBuild.ExitCode==0,"CLI build");
 var cli=Path.Combine(root,"src/Kernel.Graph.Cli/bin/Release/net10.0/Kernel.Graph.Cli.dll");
 async Task<ProcessEvidence> Cli(params string[] argv)=>await Processes.Run("dotnet",new[]{cli}.Concat(argv),root,420);
 var approvalPath=Path.Combine(evidence,"contract-approval.json");
 var contract=await Cli("contract","--approval",approvalPath);Check(contract.ExitCode==0,"CLI contract");
 await File.WriteAllTextAsync(Path.Combine(evidence,"contract-projection.json"),contract.Stdout);
 var compile=await Cli("compile","--program",Path.Combine(fixture,"program.accepted.json"),"--approval",approvalPath);
 Check(compile.ExitCode==0,"CLI compile: "+compile.Stdout+compile.Stderr);
 var rp=JsonDocument.Parse(compile.Stdout).RootElement.GetProperty("receipt").GetString()!;
 var a=JsonSerializer.Deserialize<Admission>(File.ReadAllBytes(rp),Json.Options)!;
 var cliRun=await Cli("run","--receipt",rp,"--input",Path.Combine(fixture,"c01.json"));
 Check(cliRun.ExitCode==0&&Json.Canonical(Encoding.UTF8.GetBytes(cliRun.Stdout)).SequenceEqual(Json.Bytes(GraphReference.Evaluate(c01))),"CLI run");
 var outcomePath=Path.Combine(evidence,"cli-outcome.json");await File.WriteAllTextAsync(outcomePath,cliRun.Stdout);
 var explain=await Cli("explain","--receipt",rp);var view=JsonDocument.Parse(explain.Stdout).RootElement;
 Check(explain.ExitCode==0&&view.GetProperty("receiptStatus").GetString()=="Verified"&&view.GetProperty("artifact").GetProperty("runtimeIdentifier").GetString()=="win-x64"&&
     view.GetProperty("contract").GetProperty("invariants").GetArrayLength()==14,"CLI artifact explain");
 var explainOutcome=await Cli("explain","--outcome",outcomePath);Check(explainOutcome.ExitCode==0,"CLI outcome explain");
 var missing=await Cli("explain","--receipt",Path.Combine(evidence,"work","absent-receipt.json"));
 Check(missing.ExitCode!=0&&JsonDocument.Parse(missing.Stdout).RootElement.GetProperty("error").GetProperty("code").GetString()=="ArtifactMismatch","CLI missing receipt");
 reports.Add(new{kind="public-cli",contract,compile,cliRun,explain,explainOutcome,missing});
 reports.Add(new{kind="admission",receipt=rp,a.ReadyToRun,a.Verification});
 var inputs=new List<(string,CloneInput)> {("C01",c01)};
 var n=c01.Graph.Tasks[1] with{Id="shared",Criteria=[]};
 var branch=c01.Graph.Tasks[1] with{Id="branch",Criteria=[]};
 var c02=c01 with{Graph=new([..c01.Graph.Tasks,n,branch],[new("root","child"),new("root","branch"),new("child","shared"),new("branch","shared")],[]),
    TaskIdMap=[..c01.TaskIdMap,new("shared","new.shared"),new("branch","new.branch")]};
 inputs.Add(("C02",c02));
 var outside=n with{Id="outside"};
 inputs.Add(("C03",c02 with{Graph=c02.Graph with{Tasks=[..c02.Graph.Tasks,outside],Blocks=[new("child","shared"),new("child","outside"),new("outside","root")]}}));
 inputs.Add(("C04",c02 with{Graph=c02.Graph with{Contains=[..c02.Graph.Contains,new("shared","root")]}}));
 inputs.Add(("C05-missing",c01 with{TaskIdMap=[c01.TaskIdMap[0]]}));
 inputs.Add(("C05-duplicate-fresh",c01 with{TaskIdMap=[new("root","new.x"),new("child","new.x")]}));
 inputs.Add(("C05-existing",c01 with{TaskIdMap=[new("root","root"),c01.TaskIdMap[1]]}));
 inputs.Add(("missing-root-before-cycle",inputs[3].Item2 with{SourceRootId="absent"}));
 inputs.Add(("criterion-missing",c01 with{CriterionIdMap=[]}));
 inputs.Add(("map-duplicate-domain",c01 with{TaskIdMap=[c01.TaskIdMap[0],c01.TaskIdMap[0]]}));
 inputs.Add(("fresh-invalid",c01 with{TaskIdMap=[new("root","INVALID"),c01.TaskIdMap[1]]}));
 inputs.Add(("overflow",c01 with{DateDeltaMinutes=long.MaxValue}));
 inputs.Add(("reverse-fresh-order",c01 with{TaskIdMap=[new("root","a"),new("child","z")],CriterionIdMap=[new("root.criterion","z.c"),new("child.criterion","a.c")]}));
 inputs.Add(("empty",c01 with{Graph=new([],[],[]),TaskIdMap=[],CriterionIdMap=[]}));
 var rng=new Random(20260906);
 for(int i=0;i<32;i++){
  int size=i==31?16:rng.Next(1,10);
  var ns=Enumerable.Range(0,size).Select(j=>new TaskNode("n"+j,"payload 🧠 "+j,j%3==0?null:rng.Next(-100,100),j%2==0?null:200,
    "Completed",Enumerable.Range(0,i==31?8:rng.Next(0,3)).Select(k=>new Criterion("n"+j+".c"+k,"cp"+k,true)).ToArray(),
    j%2==0?"opaque":null,["old"])).ToArray();
  var es=new List<Edge>();for(int j=1;j<size;j++){es.Add(new("n"+rng.Next(j),"n"+j));if(j>1&&rng.Next(2)==0){var e=new Edge("n0","n"+j);if(!es.Contains(e))es.Add(e);}}
  var tm=ns.Select((v,j)=>new Binding(v.Id,"fresh"+(size-j))).ToArray();
  var cm=ns.SelectMany(v=>v.Criteria).Select(v=>new Binding(v.Id,"fresh."+v.Id)).ToArray();
  inputs.Add(("generated-"+i,new(new(ns,es.ToArray(),es.Take(3).ToArray()),"n0",rng.Next(-100,100),tm,cm)));
 }
 foreach(var(name,x)in inputs){
  var bytes=Json.Bytes(x);var before=Programs.Hash(bytes);var expected=Json.Text(GraphReference.Evaluate(x));
  var actual=Encoding.UTF8.GetString(await tool.Run(rp,bytes));
  Check(actual==expected,name+" mismatch\n"+actual+"\n"+expected);
  Check(Programs.Hash(Json.Bytes(x))==before,name+" mutated input");
  reports.Add(new{kind="runtime",name,outcome=JsonSerializer.Deserialize<JsonElement>(actual)});
  if(name.StartsWith("C0"))await File.WriteAllBytesAsync(Path.Combine(fixture,name.ToLowerInvariant()+".json"),bytes);
  Console.WriteLine("PASS "+name);
 }
 // Decoder failures cross the actual compiled-process boundary.
 var invalid=new[]{("duplicate-field",Encoding.UTF8.GetBytes("{\"graph\":{},\"graph\":{}}")),
   ("invalid-utf8",new byte[]{0xff}),("surrogate",Encoding.UTF8.GetBytes("{\"s\":\"\\uD800\"}")),("limit",new byte[Codec.MaxBytes+1]),
   ("numeric-date",Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Json.Bytes(c01)).Replace("\"dateDeltaMinutes\":\"60\"","\"dateDeltaMinutes\":60")))};
 foreach(var(name,bytes)in invalid){
   var actual=JsonDocument.Parse(await tool.Run(rp,bytes));Check(actual.RootElement.GetProperty("status").GetString()=="Rejected",name);
   reports.Add(new{kind="decoder",name,outcome=actual.RootElement.Clone()});Console.WriteLine("PASS "+name);
 }
 for(int i=0;i<6;i++){
  var ast=JsonNode.Parse(source)!.AsObject();ast["steps"]![i]!["op"]="invalid.operation";
  try{Programs.Parse(Encoding.UTF8.GetBytes(ast.ToJsonString()));throw new Exception("Invalid AST accepted");}
  catch(GraphException e){Check(e.Code=="PipelineInvalid","AST stage");}
 }
 var poly=JsonNode.Parse(source)!.AsObject();poly["steps"]![2]!["policy"]!["resetHistory"]=false;
 try{Programs.Parse(Encoding.UTF8.GetBytes(poly.ToJsonString()));throw new Exception("Policy accepted");}
 catch(GraphException e){Check(e.Code=="PolicyInvalid","policy");}
 Check(Programs.Parse(Json.Canonical(source)).Digest==Programs.Parse(source).Digest,"Canonical digest");
 // Genuine solver mutations, isolated from the public source API.
 var mutations=new Dictionary<string,string>{
  ["always-reject"]=Programs.Candidate.Replace("result := Accepted(s05);","result := Rejected(7);"),
  ["selective-reject"]=Programs.Candidate.Replace("result := Accepted(s05);","result := if g.delta == 123 then Rejected(7) else Accepted(s05);"),
  ["omit-tasks"]=Programs.Candidate.Replace("Patch(s02,s03,s04)","Patch([],s03,s04)"),
  ["duplicate-tasks"]=Programs.Candidate.Replace("Patch(s02,s03,s04)","Patch(s02+s02,s03,s04)"),
  ["omit-contains"]=Programs.Candidate.Replace("Patch(s02,s03,s04)","Patch(s02,[],s04)"),
  ["omit-blocks"]=Programs.Candidate.Replace("Patch(s02,s03,s04)","Patch(s02,s03,[])"),
  ["swap-relations"]=Programs.Candidate.Replace("Patch(s02,s03,s04)","Patch(s02,s04,s03)"),
  ["wrong-date"]=Programs.Candidate.Replace("g.criterionIds,g.delta)","g.criterionIds,g.delta+1)"),
  ["source-instead-copy"]=Programs.Candidate.Replace("CloneNodes(Selected(g.nodes,s00),s01,g.criterionIds,g.delta)","Selected(g.nodes,s00)"),
  ["wrong-rejection"]=Programs.Candidate.Replace("Rejected(error)","Rejected(error+1)"),
  ["external-edge"]=Programs.Candidate.Replace("var s04 := Remap(Internal(g.blocks,s00),s01);","var s04 := g.blocks;"),
  ["omit-criteria"]=Programs.Candidate.Replace("var s05 := Patch(s02,s03,s04);","var bad := seq(|s02|, i requires 0 <= i < |s02| => Node(s02[i].id,s02[i].payload,s02[i].start,s02[i].due,s02[i].status,[],s02[i].repeatRule,s02[i].history)); var s05 := Patch(bad,s03,s04);")
 };
 foreach(var(name,code)in mutations){
  var dir=Path.Combine(evidence,"work","mutation-"+name);Directory.CreateDirectory(dir);
  File.Copy(Path.Combine(root,"verification/task-graph-v1/Contract.dfy"),Path.Combine(dir,"Contract.dfy"),true);
  await File.WriteAllTextAsync(Path.Combine(dir,"Candidate.dfy"),code);
  var proof=await Processes.Run(Path.Combine(root,".tools/dafny/dafny/dafny.exe"),
    ["verify","Contract.dfy","Candidate.dfy","--enforce-determinism","--cores","2","--verification-time-limit","10","--filter-symbol","Candidate"],dir,60);
  Check(proof.ExitCode!=0&&!proof.TimedOut&&!proof.OutputExceeded&&
    (proof.Stdout.Contains("postcondition")||proof.Stdout.Contains("precondition")),name+" was not a semantic verification failure\n"+proof.Stdout);
  reports.Add(new{kind="proof-mutation",name,proof});Console.WriteLine("PASS proof "+name);
 }
 foreach(var bypass in new[]{"assume true;","{:axiom}","{:verify false}","include \"other.dfy\"","decreases *"}){
  try{GraphToolchain.CheckSource(bypass);throw new Exception("Bypass accepted");}catch(GraphException){checks++;}
 }
 var forged=JsonSerializer.Deserialize<Admission>(File.ReadAllBytes(rp),Json.Options)! with {VerificationOutcome="Unverified"};
 var fake=Path.Combine(evidence,"work/forged.json");await File.WriteAllBytesAsync(fake,Json.Bytes(forged));
 try{await tool.Run(fake,Json.Bytes(c01));throw new Exception("Receipt forgery accepted");}catch(GraphException e){Check(e.Code=="ArtifactMismatch","forgery");}
 await File.WriteAllTextAsync(fake,"{broken");
 try{await tool.Run(fake,Json.Bytes(c01));throw new Exception("Malformed receipt accepted");}catch(GraphException e){Check(e.Code=="ArtifactMismatch","malformed receipt");}
 var wrongApproval=tool.Approve() with {ContractDigest=new string('0',64)};
 try{await tool.Compile(source,Json.Bytes(wrongApproval));throw new Exception("Wrong approval accepted");}
 catch(GraphException e){Check(e.Code=="ContractNotApproved","approval binding");}
 var binary=Path.Combine(Path.GetDirectoryName(rp)!,"publish","GraphModule.dll");var originalBinary=File.ReadAllBytes(binary);
 try {
  var corrupt=(byte[])originalBinary.Clone();corrupt[^1]^=1;await File.WriteAllBytesAsync(binary,corrupt);
  try{await tool.Run(rp,Json.Bytes(c01));throw new Exception("Artifact tampering accepted");}catch(GraphException e){Check(e.Code=="ArtifactMismatch","artifact hash");}
  File.Delete(binary);
  try{await tool.Run(rp,Json.Bytes(c01));throw new Exception("Missing artifact accepted");}catch(GraphException e){Check(e.Code=="ArtifactMismatch","missing artifact");}
 } finally {await File.WriteAllBytesAsync(binary,originalBinary);}
 var proc=await Processes.Run("pwsh",["-NoProfile","-NonInteractive","-Command","Start-Sleep -Seconds 10"],root,1);
 Check(proc.TimedOut,"timeout not enforced");reports.Add(new{kind="timeout",proc});
 var child=await Processes.Run("pwsh",["-NoProfile","-NonInteractive","-Command",
    "$child=Start-Process pwsh -WindowStyle Hidden -PassThru -ArgumentList '-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 60'; [Console]::WriteLine($child.Id); Start-Sleep -Seconds 60"],root,4);
 Check(child.TimedOut&&int.TryParse(child.Stdout.Trim(),out _),"child watchdog fixture");
 var childId=int.Parse(child.Stdout.Trim());bool alive;
 try{using var p=System.Diagnostics.Process.GetProcessById(childId);alive=!p.HasExited;}catch(ArgumentException){alive=false;}
 Check(!alive,"watchdog left child alive");reports.Add(new{kind="process-tree",childId,alive,child});
 var good=GraphReference.Evaluate(c01);var badOutput=good with {Patch=good.Patch! with {AddTasks=good.Patch.AddTasks.Select(n=>n with{Payload="wrong"}).ToArray()}};
 try{RuntimeValidation.AgainstInput(c01,badOutput);throw new Exception("Adapter corruption accepted");}
 catch(GraphException e){Check(e.Code=="InternalInvariantViolation","runtime conversion fault");}
 var proc2=await Processes.Run("pwsh",["-NoProfile","-NonInteractive","-Command","[Console]::Out.Write(('x' * 1100000))"],root,10);
 Check(proc2.OutputExceeded,"output limit not enforced");reports.Add(new{kind="output-limit",proc2.ExitCode,proc2.OutputExceeded});
 var b=await tool.Compile(source,Json.Bytes(tool.Approve()));
 Check(a.ProgramDigest==b.ProgramDigest&&a.GeneratedSourceDigest==b.GeneratedSourceDigest&&a.BuildInputsDigest==b.BuildInputsDigest,"semantic compilation nondeterminism");
 var replay1=await tool.Run(tool.ReceiptPath(b),Json.Bytes(c01));var replay2=await tool.Run(rp,Json.Bytes(c01));
 Check(replay1.SequenceEqual(replay2),"runtime replay mismatch");
 reports.Add(new{kind="determinism",semanticPassed=true,binaryEqual=a.Files.SequenceEqual(b.Files),first=rp,second=tool.ReceiptPath(b)});
 await File.WriteAllBytesAsync(Path.Combine(evidence,"conformance.json"),Json.Bytes(new{passed=true,checks,elapsedMs=watch.ElapsedMilliseconds,reports}));
 Console.WriteLine("PASS all: "+checks);return 0;
}catch(Exception e){
 await File.WriteAllBytesAsync(Path.Combine(evidence,"conformance.json"),Json.Bytes(new{passed=false,checks,error=e.ToString(),reports}));
 Console.Error.WriteLine(e);return 1;
}
