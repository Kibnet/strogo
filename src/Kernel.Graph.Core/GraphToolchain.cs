using System.Text;
using System.Text.Json;
using System.Reflection.PortableExecutable;
using System.Security;
namespace Kernel.Graph;
public sealed record ArtifactFile(string Path,string Sha256);
public sealed record Approval(string SchemaVersion,string ApprovedSpecCommit,string ApprovedSpecBlob,string ContractDigest,string Origin);
public sealed record Admission(string ReceiptVersion,string ProgramDigest,string ContractDigest,string ApprovalDigest,string GeneratedSourceDigest,
    string SupportDigest,string ToolDigest,string DafnyVersion,string RuntimeIdentifier,string DotnetSdkVersion,string BuildInputsDigest,
    string ArtifactId,ArtifactFile[] Files,ProcessEvidence Verification,ProcessEvidence Publication,string VerificationOutcome,
    bool DeterminismEnforced,bool ReadyToRun,DateTimeOffset CreatedAtUtc) {
    public string CanonicalAstDigest => ProgramDigest;
    public string ContractApprovalDigest => ApprovalDigest;
    public string DafnyArchiveDigest => "3653f05a111ca21e234709ea7b25ce96083fd6c6f10484256ba54110dbc0654d";
    public string DafnyExecutableDigest => "aebb6e5ea4aa1a8ba5432ee214c47255fd7aa5a583f5cac8e83d27d9992c4dd0";
    public string[] VerifierArguments => GraphToolchain.VerifierArguments;
    public string CompiledArtifactDigest => Programs.Hash(Json.Bytes(Files));
    public long VerificationDurationMs => Verification.ElapsedMs;
    public string Provenance => "E04; human-approved SPEC 8a9342e8b0458c55ce1eb5096dc1b16901bec6ba; owner admission registry";
}

public sealed class GraphToolchain(string root) {
    public static string[] VerifierArguments => ["translate","cs","Contract.dfy","Candidate.dfy","--include-runtime","--enforce-determinism",
        "--cores","2","--verification-time-limit","15","--output","Generated.cs"];
    public string Root {get;}=Path.GetFullPath(root);
    string At(string relative)=>Path.Combine(Root,relative);
    string Support => At("verification/task-graph-v1");
    public string ContractDigest => Programs.Hash(File.ReadAllBytes(Path.Combine(Support,"Contract.dfy")));
    public string SupportDigest => Programs.Hash(Json.Bytes(new[]{
        new ArtifactFile("adapter",Programs.Hash(File.ReadAllBytes(Path.Combine(Support,"RuntimeAdapter/Program.cs")))),
        new ArtifactFile("core",Programs.Hash(File.ReadAllBytes(typeof(GraphToolchain).Assembly.Location)))
    }));
    public Approval Approve() => new("kernel.graph-approval.v1","8a9342e8b0458c55ce1eb5096dc1b16901bec6ba",
        "2cfed958a7fa6c6717f4183cc9fb3c232489bae7",ContractDigest,"human-spec-approval");
    public object Projection() => new {
        profile=Programs.Profile,contractDigest=ContractDigest,approvedSpec=Approve().ApprovedSpecCommit,
        domain="До 16 задач, 8 критериев на задачу, по 64 ребра; strict JSON <=65536 байт, checked I64",
        outcome="Корректный вход обязательно Accepted с точной копией. Иначе первый нормативный Rejected без patch.",
        invariants=new[]{"Точное containment-замыкание","Одна копия каждого достижимого узла","Точные bijective ID maps",
            "Свежие ID не пересекаются с существующими","Payload/repeat сохраняются","Полные criteria, payload сохранён, completion/status/history сброшены",
            "Точный checked-сдвиг дат","Все внутренние рёбра переназначены","Внешние рёбра не копируются","Исходный граф не меняется",
            "Целостность ссылок, уникальность ID, DAG","Канонический wire order","Отказ без patch","Завершение"},
        effects="Только возврат additions; storage/сеть/файлы не доступны программе",
        errors=new[]{"TransportInvalid","SchemaInvalid","LimitExceeded","DuplicateEntityId","DanglingRelation","DuplicateRelation",
            "SourceRootMissing","ContainmentCycle","TaskIdMapDomainMismatch","CriterionIdMapDomainMismatch","FreshIdInvalid","FreshIdCollision","DateOverflow"},
        limits="verify 60s; publish 120s; execution 10s; process output <=1 MiB",
        trust="Owner contract, codec/canonicalizer/lowerer, Dafny/Boogie/Z3, C# adapter, SDK/runtime/R2R, ОС. Filesystem administrator вне модели угроз.",
        notMeasured=new[]{"G05","G06","Другие платформы","Полный AOT","Корректность всей TCB"}
    };
    string VerifyTool(){
        using var m=JsonDocument.Parse(File.ReadAllBytes(At("tools/dafny.json")));var e=m.RootElement;
        var archive=At(".tools/downloads/dafny-4.11.0.zip");var exe=At(".tools/dafny/dafny/dafny.exe");
        if(Programs.Hash(File.ReadAllBytes(archive))!=e.GetProperty("sha256").GetString()||
           Programs.Hash(File.ReadAllBytes(exe))!=e.GetProperty("executableSha256").GetString())throw new GraphException("toolchain","ToolMismatch");
        // Pin dependencies as well as the apphost. A forged Dafny.dll must not pass the executable hash.
        var inventory=JsonSerializer.Deserialize<ArtifactFile[]>(File.ReadAllBytes(At("tools/dafny-files.json")),Json.Options)!;
        foreach(var f in inventory)if(Programs.Hash(File.ReadAllBytes(At(".tools/dafny/dafny/"+f.Path)))!=f.Sha256)throw new GraphException("toolchain","ToolMismatch");
        return Programs.Hash(Json.Bytes(inventory));
    }
    public static void CheckSource(string source){
        if(System.Text.RegularExpressions.Regex.IsMatch(source,@"\b(assume|include|extern|axiom)\b|\{:\s*(verify|compile|axiom|extern)|decreases\s+\*"))
            throw new GraphException("verification","ProofBypass");
    }
    public async Task<Admission> Compile(byte[] source,byte[] approvalBytes){
        var p=Programs.Parse(source); var approval=JsonSerializer.Deserialize<Approval>(approvalBytes,Json.Options);
        if(approval!=Approve())throw new GraphException("approval","ContractNotApproved");
        var toolHash=VerifyTool(); var code=Programs.Emit(p);var contract=File.ReadAllText(Path.Combine(Support,"Contract.dfy"));
        CheckSource(code);CheckSource(contract);
        var id=p.Digest+"-"+Guid.NewGuid().ToString("N");
        var dir=At("artifacts/e04/work/"+id);Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir,"program.json"),p.CanonicalBytes);
        await File.WriteAllBytesAsync(Path.Combine(dir,"source-map.json"),Json.Bytes(Programs.SourceMap(p)));
        await File.WriteAllTextAsync(Path.Combine(dir,"Contract.dfy"),contract,new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(dir,"Candidate.dfy"),code,new UTF8Encoding(false));
        var args=VerifierArguments;
        var proof=await Processes.Run(At(".tools/dafny/dafny/dafny.exe"),args,dir,60);
        await File.WriteAllBytesAsync(Path.Combine(dir,"verification.json"),Json.Bytes(proof));
        if(proof.ExitCode!=0||proof.TimedOut||proof.OutputExceeded||
           !System.Text.RegularExpressions.Regex.IsMatch(proof.Stdout,@"finished with [1-9][0-9]* verified, 0 errors\s*$")||!File.Exists(Path.Combine(dir,"Generated.cs")))throw new GraphException("verification","ProofNotEstablished");
        await File.WriteAllTextAsync(Path.Combine(dir,"Program.cs"),File.ReadAllText(Path.Combine(Support,"RuntimeAdapter/Program.cs")));
        File.Copy(typeof(GraphToolchain).Assembly.Location,Path.Combine(dir,"Kernel.Graph.Core.dll"));
        var core="Kernel.Graph.Core.dll";
        var proj=$"""
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>disable</Nullable><ImplicitUsings>enable</ImplicitUsings>
 <TreatWarningsAsErrors>false</TreatWarningsAsErrors><CheckForOverflowUnderflow>false</CheckForOverflowUnderflow>
 <EnableDefaultCompileItems>false</EnableDefaultCompileItems><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
 <AssemblyName>GraphModule</AssemblyName></PropertyGroup>
 <ItemGroup><Compile Include="Generated.cs"/><Compile Include="Program.cs"/>
 <Reference Include="Kernel.Graph.Core"><HintPath>{core}</HintPath></Reference></ItemGroup>
</Project>
""";
        await File.WriteAllTextAsync(Path.Combine(dir,"Runner.csproj"),proj);
        var restore=await Processes.Run("dotnet",["restore","Runner.csproj","-r","win-x64","-p:PublishReadyToRun=true"],dir,120);
        await File.WriteAllBytesAsync(Path.Combine(dir,"restore.json"),Json.Bytes(restore));
        if(restore.ExitCode!=0||restore.TimedOut||restore.OutputExceeded)throw new GraphException("build","RestoreFailed");
        var locked=await Processes.Run("dotnet",["restore","Runner.csproj","-r","win-x64","-p:PublishReadyToRun=true","--locked-mode"],dir,120);
        await File.WriteAllBytesAsync(Path.Combine(dir,"locked-restore.json"),Json.Bytes(locked));
        if(locked.ExitCode!=0||locked.TimedOut||locked.OutputExceeded)throw new GraphException("build","RestoreFailed");
        var build=await Processes.Run("dotnet",["publish","Runner.csproj","-c","Release","-r","win-x64","--self-contained","false",
            "--no-restore","-p:PublishReadyToRun=true","-o","publish"],dir,120);
        await File.WriteAllBytesAsync(Path.Combine(dir,"publication.json"),Json.Bytes(build));
        if(build.ExitCode!=0||build.TimedOut||build.OutputExceeded)throw new GraphException("build","PublishFailed");
        var pub=Path.Combine(dir,"publish");var dll=Path.Combine(pub,"GraphModule.dll");
        using var stream=File.OpenRead(dll);using var pe=new PEReader(stream);
        bool r2r=pe.PEHeaders.CorHeader?.ManagedNativeHeaderDirectory.Size>0;
        if(!r2r)throw new GraphException("build","NativeCodeMissing");
        var files=Directory.GetFiles(pub).Order(StringComparer.Ordinal).Select(f=>new ArtifactFile(Path.GetFileName(f),Programs.Hash(File.ReadAllBytes(f)))).ToArray();
        var sdk=await Processes.Run("dotnet",["--version"],Root,10);
        if(sdk.ExitCode!=0||sdk.TimedOut||sdk.OutputExceeded||sdk.Stdout.Trim()!="10.0.400")throw new GraphException("build","ToolMismatch");
        var inputsHash=Programs.Hash(Json.Bytes(new{p.Digest,contract=ContractDigest,support=SupportDigest,toolHash,sdk=sdk.Stdout.Trim(),target="win-x64",
            project=Programs.Hash(Encoding.UTF8.GetBytes(proj)),packages=Programs.Hash(File.ReadAllBytes(Path.Combine(dir,"packages.lock.json"))),verifierArguments=args}));
        var receipt=new Admission("kernel.graph-admission.v1",p.Digest,ContractDigest,Programs.Hash(Json.Bytes(approval)),Programs.Hash(Encoding.UTF8.GetBytes(code)),
            SupportDigest,toolHash,"4.11.0","win-x64",sdk.Stdout.Trim(),inputsHash,id,files,proof,build,"Verified",true,r2r,DateTimeOffset.UtcNow);
        var bytes=Json.Bytes(receipt);await File.WriteAllBytesAsync(Path.Combine(dir,"receipt.json"),bytes);
        Directory.CreateDirectory(At(".tools/admissions"));await File.WriteAllTextAsync(At(".tools/admissions/"+id),Programs.Hash(bytes));
        return receipt;
    }
    public string ReceiptPath(Admission a)=>At("artifacts/e04/work/"+a.ArtifactId+"/receipt.json");
    public async Task<Admission> ValidateAdmission(string receiptPath){
      try {
        if(new FileInfo(receiptPath).Length>1048576)throw new GraphException("receipt","ArtifactMismatch");
        var bytes=await File.ReadAllBytesAsync(receiptPath);var a=JsonSerializer.Deserialize<Admission>(bytes,Json.Options)??throw new GraphException("receipt","ArtifactMismatch");
        if(!System.Text.RegularExpressions.Regex.IsMatch(a.ArtifactId,@"\A[0-9a-f]{64}-[0-9a-f]{32}\z"))throw new GraphException("receipt","ArtifactMismatch");
        var registered=At(".tools/admissions/"+a.ArtifactId);
        if(!File.Exists(registered)||File.ReadAllText(registered)!=Programs.Hash(bytes)||a.VerificationOutcome!="Verified"||
            !a.DeterminismEnforced||!a.ReadyToRun||a.ReceiptVersion!="kernel.graph-admission.v1"||a.ContractDigest!=ContractDigest||a.SupportDigest!=SupportDigest||a.ToolDigest!=VerifyTool())
            throw new GraphException("receipt","ArtifactMismatch");
        var dir=At("artifacts/e04/work/"+a.ArtifactId+"/publish");
        foreach(var f in a.Files){if(Path.GetFileName(f.Path)!=f.Path||Programs.Hash(File.ReadAllBytes(Path.Combine(dir,f.Path)))!=f.Sha256)throw new GraphException("receipt","ArtifactMismatch");}
        return a;
      }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException){
        throw new GraphException("receipt","ArtifactMismatch",e);
      }
    }
    public async Task<object> Explain(string? receiptPath=null,byte[]? outcome=null){
        if(receiptPath!=null){
            var a=await ValidateAdmission(receiptPath);
            return new{contract=Projection(),receiptStatus="Verified",approvalStatus="BoundToApprovedSpec",artifact=new{
                a.ArtifactId,a.RuntimeIdentifier,a.ContractDigest,a.ContractApprovalDigest,a.ProgramDigest,a.CompiledArtifactDigest,a.BuildInputsDigest,a.ReadyToRun}};
        }
        if(outcome!=null){var result=RuntimeValidation.ReadOutcome(outcome);return new{contract=Projection(),receiptStatus="NotProvided",approvalStatus="NotEstablishedByOutcome",outcome=result};}
        return new{contract=Projection(),receiptStatus="NotProvided",approvalStatus="NoArtifactSelected"};
    }
    public async Task<byte[]> Run(string receiptPath,byte[] input){
        var a=await ValidateAdmission(receiptPath);
        var dir=At("artifacts/e04/work/"+a.ArtifactId+"/publish");
        // Direct bytes preserve decoder failure behavior; no oracle is executed here.
        var run=await Processes.Run("dotnet",["GraphModule.dll"],dir,10,input);
        if(run.ExitCode!=0||run.TimedOut||run.OutputExceeded)throw new GraphException("runtime","RuntimeFailure");
        return Json.Bytes(RuntimeValidation.ReadOutcome(Encoding.UTF8.GetBytes(run.Stdout)));
    }
}
