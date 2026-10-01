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
  byte[] bytes=File.ReadAllBytes(args[0]);string symbol=args[1];string mode=args[2];
  using var pe=new PEReader(new MemoryStream(bytes,false));var md=pe.GetMetadataReader();
  var mvid=md.GetGuid(md.GetModuleDefinition().Mvid);int token=0;
  foreach(var h in md.TypeDefinitions){var t=md.GetTypeDefinition(h);if(md.GetString(t.Namespace)!="Candidate"||md.GetString(t.Name)!="__default")continue;foreach(var mh in t.GetMethods()){if(md.GetString(md.GetMethodDefinition(mh).Name)==symbol){if(token!=0)throw new Exception("ambiguous symbol");token=System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(mh);}}}
  if(token==0)throw new Exception("missing symbol");long output=42;
  if(mode=="call") {var ctx=new AssemblyLoadContext("generated-probe",true);using var stream=new MemoryStream(bytes,false);var assembly=ctx.LoadFromStream(stream);var method=assembly.GetType("Candidate.__default").GetMethod(symbol,BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(long)},null);if(method==null||method.ReturnType!=typeof(long)||method.MetadataToken!=token||method.Module.ModuleVersionId!=mvid)throw new Exception("binding");output=(long)method.Invoke(null,new object[]{41L});ctx.Unload();}
  else if(mode=="decoy")output=Candidate.__default.F000(41);
  else if(mode!="no-call")throw new Exception("mode");
  if(output!=42)throw new Exception("output");Console.WriteLine(JsonSerializer.Serialize(new{mode,output,mvid,token,sha256=Convert.ToHexStringLower(SHA256.HashData(bytes))}));
 }
}
namespace Candidate { public static class __default {
 [MethodImpl(MethodImplOptions.NoInlining)] public static long F000(long x)=>x+1;
}}
