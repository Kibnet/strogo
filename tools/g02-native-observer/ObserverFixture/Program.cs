using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Strogo.Modules;

if(args.Length!=3) throw new ArgumentException("packageRoot, mode, functionId required");
using var snapshot=G02PackageSnapshot.OpenStructural(args[0]);
_ = G02ProofSourceRegenerator.Verify(snapshot); // Regeneration only, not proof or admission.
byte[] Role(string role)=>snapshot.ReadHeld(snapshot.Manifest.Files.Single(file=>file.Role==role).Path);
var module=ModulesCompiler.Compile(ModulesParser.ParseModule(Role("module")));
var bundle=OwnerBundleV04Parser.Parse(Role("bundle"));
var binding=OwnerContractBinderV04.Bind(module,bundle);
var entry=binding.Entries.Single(value=>value.Function.Id==args[2]);
var sourceMap=ModulesDafnyLowerer.Lower(module,bundle).SourceMap.ToDictionary(value=>value.EntityId,value=>value.GeneratedName);
var entryPath=Path.GetFullPath(Path.Combine(args[0],snapshot.Manifest.EntryAssemblyPath.Replace('/',Path.DirectorySeparatorChar)));
var context=new AssemblyLoadContext("dispatch-observer",true);
try {
 var assembly=context.LoadFromAssemblyPath(entryPath);
 using var dispatch=new G02CompiledDispatch(assembly,module,bundle);
 var target=assembly.GetType("Candidate.__default")??throw new Exception("type");
 object Descriptor(string kind,string symbol) {var method=target.GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly).Single(value=>value.Name==symbol);return new{kind,symbol,token=method.MetadataToken,mvid=method.Module.ModuleVersionId};}
 var f=sourceMap["function/"+entry.Function.Id];var q=sourceMap["owner/contract/"+entry.Contract.Id+"/requires"];
 var methods=new[]{Descriptor("Q",q),Descriptor("F",f)};
 var vectors=new List<(string Id,ModuleValue[] Input,ModuleValue? Expected,string? Refusal)>();
 foreach(var witness in entry.Witnesses) {var byId=witness.Arguments.ToDictionary(value=>value.ParameterId,value=>value.Value);vectors.Add((witness.Id,entry.Function.Parameters.Select(value=>byId[value.Id]).ToArray(),witness.ModelResult,null));}
 if(args[1]=="typed-invalid") {var bad=(ModuleValue[])vectors[0].Input.Clone();bad[0]=new ModuleBool(true);vectors.Clear();vectors.Add(("typed-invalid",bad,null,"CompiledInputMismatch"));}
 else if(args[1]=="requires-invalid") {
  ModuleValue[] bad=entry.Function.Parameters.Length==1?[new ModuleI64(long.MaxValue)]:[new ModuleSequence(new TypeRef("I64"),256,Array.Empty<ModuleValue>()),new ModuleI64(-1)];
  vectors.Clear();vectors.Add(("requires-invalid",bad,null,"CompiledPreconditionFailed"));
 }
 else if(args[1]=="valid") {
  if(entry.Function.Parameters.Length==1)foreach(var value in new[]{long.MinValue,0L,long.MaxValue-1})vectors.Add(("boundary-"+value,[new ModuleI64(value)],new ModuleI64(value+1),null));
  else foreach(var length in new[]{0,256}) {ModuleValue[] input=[new ModuleSequence(new TypeRef("I64"),256,Enumerable.Repeat<ModuleValue>(new ModuleI64(1),length)),new ModuleI64(length)];vectors.Add(("sequence-"+length,input,ModulesReferenceEvaluator.Invoke(module,entry.Function.Id,input).Value,null));}
 } else throw new ArgumentException("mode");
 var rows=new List<object>();var expectedEnters=new List<string>();
 foreach(var vector in vectors) {
  ModuleValue? result=null;string? refusal=null;
  try{result=dispatch.Invoke(entry.Function.Id,vector.Input);}catch(ModuleException error){refusal=error.Code;}
  if(refusal!=vector.Refusal)throw new Exception("refusal mismatch");
  if(vector.Refusal is null) {
   var reference=ModulesReferenceEvaluator.Invoke(module,entry.Function.Id,vector.Input).Value;
   if(result is null||vector.Expected is null||!OwnerContractEvaluator.StructuralEquals(result,vector.Expected)||!OwnerContractEvaluator.StructuralEquals(result,reference))throw new Exception("oracle mismatch");
   expectedEnters.Add("Q");expectedEnters.Add("F");
  }else if(vector.Refusal=="CompiledPreconditionFailed")expectedEnters.Add("Q");
  rows.Add(new{vector.Id,refusal,input=vector.Input.Select((value,index)=>OwnerBundleCodec.ValuePayload(value,value is ModuleBool?new TypeRef("Bool"):entry.Function.Parameters[index].Type)).ToArray(),output=result is null?null:OwnerBundleCodec.ValuePayload(result,entry.Function.ReturnType),expected=vector.Expected is null?null:OwnerBundleCodec.ValuePayload(vector.Expected,entry.Function.ReturnType)});
 }
 snapshot.Revalidate();
 Console.WriteLine(JsonSerializer.Serialize(new{purpose="retained-fixture-dispatch-not-admitted",mode=args[1],functionId=entry.Function.Id,entrySha256=Convert.ToHexStringLower(SHA256.HashData(Role("entry-assembly"))),methods,expectedEnters,rows}));
} finally {context.Unload();}
