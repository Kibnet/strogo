using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
class Program {
 static void Main(string[] args) {
  using var held=File.Open(args[0],FileMode.Open,FileAccess.Read,FileShare.Read);
  using var copy=new MemoryStream();held.CopyTo(copy);byte[] bytes=copy.ToArray();string symbol=args[1];string mode=args[2];
  using var pe=new PEReader(new MemoryStream(bytes,false));var md=pe.GetMetadataReader();
  var mvid=md.GetGuid(md.GetModuleDefinition().Mvid);int token=0;
  foreach(var h in md.TypeDefinitions){var t=md.GetTypeDefinition(h);if(md.GetString(t.Namespace)!="Candidate"||md.GetString(t.Name)!="__default")continue;foreach(var mh in t.GetMethods()){if(md.GetString(md.GetMethodDefinition(mh).Name)==symbol){if(token!=0)throw new Exception("ambiguous symbol");token=System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(mh);}}}
  if(token==0)throw new Exception("missing symbol");long output=42;
  if(mode=="file-many") {var contexts=new System.Collections.Generic.List<AssemblyLoadContext>();for(int i=0;i<33;i++){var ctx=new AssemblyLoadContext("file-many-"+i,true);contexts.Add(ctx);var assembly=ctx.LoadFromAssemblyPath(Path.GetFullPath(args[0]));var method=assembly.GetType("Candidate.__default").GetMethod(symbol,BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(long)},null);if(method==null||method.MetadataToken!=token||method.Module.ModuleVersionId!=mvid||(long)method.Invoke(null,new object[]{41L})!=42)throw new Exception("many binding");}foreach(var ctx in contexts)ctx.Unload();}
  else if(mode=="call"||mode=="file-call"||mode=="hang") {var ctx=new AssemblyLoadContext("generated-probe",true);using var stream=new MemoryStream(bytes,false);var assembly=mode=="call"?ctx.LoadFromStream(stream):ctx.LoadFromAssemblyPath(Path.GetFullPath(args[0]));var method=assembly.GetType("Candidate.__default").GetMethod(symbol,BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(long)},null);if(method==null||method.ReturnType!=typeof(long)||method.MetadataToken!=token||method.Module.ModuleVersionId!=mvid)throw new Exception("binding");output=(long)method.Invoke(null,new object[]{41L});ctx.Unload();}
  else if(mode=="decoy"||mode=="file-decoy")output=Candidate.__default.F000(41);
  else if(mode!="no-call"&&mode!="file-no-call")throw new Exception("mode");
  if(output!=42)throw new Exception("output");Console.WriteLine(JsonSerializer.Serialize(new{mode,output,mvid,token,sha256=Convert.ToHexStringLower(SHA256.HashData(bytes))}));
  if(mode=="hang")System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
 }
}
namespace Candidate { public static class __default {
 [MethodImpl(MethodImplOptions.NoInlining)] public static long F000(long x)=>x+1;
}}
