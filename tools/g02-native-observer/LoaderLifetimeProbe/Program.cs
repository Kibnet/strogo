using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Strogo.Modules;

if (args.Length != 4) throw new ArgumentException("repoRoot packageRoot copyDirectory mode required");
var repoRoot=Path.GetFullPath(args[0]);var mode=args[3];
if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture!=Architecture.X64)
    throw new Exception("Windows x64 probe required");
var runtimeDirectory=RuntimeEnvironment.GetRuntimeDirectory();
var pinnedRuntime=Path.Combine(repoRoot,".tools","dotnet-sdk-10.0.400","shared","Microsoft.NETCore.App","10.0.11");
if(!Path.TrimEndingDirectorySeparator(runtimeDirectory).Equals(pinnedRuntime,StringComparison.OrdinalIgnoreCase))
    throw new Exception("Probe runtime path mismatch");
using var snapshot=G02PackageSnapshot.OpenStructural(args[1]);
_ = G02ProofSourceRegenerator.Verify(snapshot);
byte[] Role(string role)=>snapshot.ReadHeld(snapshot.Manifest.Files.Single(file=>file.Role==role).Path);
var module=ModulesCompiler.Compile(ModulesParser.ParseModule(Role("module")));
var bundle=OwnerBundleV04Parser.Parse(Role("bundle"));
var binding=OwnerContractBinderV04.Bind(module,bundle);
var entry=binding.Entries[0];var witness=entry.Witnesses[0];
var byId=witness.Arguments.ToDictionary(value=>value.ParameterId,value=>value.Value);
var input=entry.Function.Parameters.Select(value=>byId[value.Id]).ToArray();
var reference=ModulesReferenceEvaluator.Invoke(module,entry.Function.Id,input).Value;
if(!OwnerContractEvaluator.StructuralEquals(reference,witness.ModelResult))throw new Exception("Fixture oracle mismatch");
var bytes=Role("entry-assembly");var originalSha=Convert.ToHexStringLower(SHA256.HashData(bytes));
Directory.CreateDirectory(args[2]);var copyPath=Path.GetFullPath(Path.Combine(args[2],"generated.dll"));
using(var output=new FileStream(copyPath,FileMode.CreateNew,FileAccess.Write,FileShare.None))output.Write(bytes);
Observation observation;var history=new List<object>();
if(mode=="stream" || mode=="file-retained")
    observation=Probe.Retained(copyPath,mode,module,bundle,entry.Function.Id,input,witness.ModelResult);
else if(mode=="file-released")
{
    var released=Probe.Released(copyPath,module,bundle,entry.Function.Id,input,witness.ModelResult);
    observation=released.Observation;
    for(var iteration=0;iteration<8;iteration++)
    {
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        var collected=!released.Context.IsAlive;var reopen=Probe.Exclusive(copyPath);
        history.Add(new {iteration,collected,reopen});
        if(collected && reopen.Success)break;
    }
}
else throw new ArgumentException("Unknown lifetime mode");
snapshot.Revalidate();
if(Convert.ToHexStringLower(SHA256.HashData(Role("entry-assembly")))!=originalSha ||
   Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(copyPath)))!=originalSha)
    throw new Exception("Source/copy drift");
Console.WriteLine(JsonSerializer.Serialize(new {
    purpose="loader-lifetime-physics-not-proof-or-admission",mode,runtimeDirectory,
    functionId=entry.Function.Id,entrySha256=originalSha,packageDigest=snapshot.Manifest.PackageDigest,
    observation,history,expectedOwner=OwnerBundleCodec.ValuePayload(witness.ModelResult,entry.Function.ReturnType),
    expectedReference=OwnerBundleCodec.ValuePayload(reference,entry.Function.ReturnType),
    ownerReferenceMatch=true,sourceUnchanged=true,copyUnchanged=true }));

internal sealed record Reopen(bool Success,int? HResult);
internal sealed record Observation(string Location,bool OwnerMatch,Reopen WhileOwnHandle,Reopen AfterDispose,Reopen? AfterGcWithReferences);
internal static class Probe
{
    internal static Reopen Exclusive(string path)
    {
        try {using var file=File.Open(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);return new(file.CanWrite,null);}
        catch(IOException error){return new(false,error.HResult);}
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Observation Retained(string path,string mode,ModuleIr module,OwnerBundleV04 bundle,
        string functionId,ModuleValue[] input,ModuleValue expected)
    {
        using var held=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        var context=new AssemblyLoadContext("lifetime-retained",true);
        var assembly=mode=="stream"?context.LoadFromStream(held):context.LoadFromAssemblyPath(path);
        using var dispatch=new G02CompiledDispatch(assembly,module,bundle);
        var result=dispatch.Invoke(functionId,input);
        if(!OwnerContractEvaluator.StructuralEquals(result,expected))throw new Exception("Generated output mismatch");
        var location=assembly.Location;
        if(mode=="stream" && location.Length!=0 || mode!="stream" && !location.Equals(path,StringComparison.OrdinalIgnoreCase))
            throw new Exception("Unexpected loader Location");
        var own=Exclusive(path);if(own.Success)throw new Exception("Held read handle was not exclusive-protected");
        dispatch.Dispose();context.Unload();held.Dispose();
        var after=Exclusive(path);
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        var afterGc=Exclusive(path);
        GC.KeepAlive(dispatch);GC.KeepAlive(assembly);GC.KeepAlive(context);
        if(mode=="stream" && (!after.Success || !afterGc.Success))throw new Exception("Stream copy handle leaked");
        return new(location,true,own,after,afterGc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static (WeakReference Context,Observation Observation) Released(string path,ModuleIr module,
        OwnerBundleV04 bundle,string functionId,ModuleValue[] input,ModuleValue expected)
    {
        using var held=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        var context=new AssemblyLoadContext("lifetime-released",true);var weak=new WeakReference(context);
        var assembly=context.LoadFromAssemblyPath(path);
        using var dispatch=new G02CompiledDispatch(assembly,module,bundle);
        var result=dispatch.Invoke(functionId,input);
        if(!OwnerContractEvaluator.StructuralEquals(result,expected))throw new Exception("Generated output mismatch");
        var location=assembly.Location;if(!location.Equals(path,StringComparison.OrdinalIgnoreCase))throw new Exception("File location mismatch");
        var own=Exclusive(path);if(own.Success)throw new Exception("Held read handle not protected");
        dispatch.Dispose();context.Unload();held.Dispose();
        var after=Exclusive(path);
        GC.KeepAlive(dispatch);GC.KeepAlive(assembly);GC.KeepAlive(context);
        return (weak,new(location,true,own,after,null));
    }
}
