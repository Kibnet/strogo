using Kernel.Graph;
static string Option(string[] args,string key) { int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:throw new ArgumentException("Missing "+key); }
static string Root(){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d!=null&&!File.Exists(Path.Combine(d.FullName,"Kernel.slnx")))d=d.Parent;return d?.FullName??throw new InvalidOperationException("Project root missing");}
static async Task<byte[]> ReadBounded(string path,int maximum=Codec.MaxBytes){
 using var f=File.OpenRead(path);var data=new byte[maximum+1];int count=0,n;
 while(count<data.Length&&(n=await f.ReadAsync(data.AsMemory(count)))>0)count+=n;
 if(count>maximum)throw new GraphException("decode","LimitExceeded");return data[..count];
}
try {
 var tool=new GraphToolchain(Root()); var command=args.FirstOrDefault();
 switch(command){
  case "contract":
    var approval=Option(args,"--approval");Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(approval))!);
    await File.WriteAllBytesAsync(approval,Json.Bytes(tool.Approve()));Console.WriteLine(Json.Text(tool.Projection()));return 0;
  case "compile":
    var receipt=await tool.Compile(await ReadBounded(Option(args,"--program")),await ReadBounded(Option(args,"--approval")));
    Console.WriteLine(Json.Text(new{status="Verified",receipt=tool.ReceiptPath(receipt),receipt.ProgramDigest,receipt.ReadyToRun}));return 0;
  case "run":
    var output=await tool.Run(Option(args,"--receipt"),await ReadBounded(Option(args,"--input")));
    Console.WriteLine(System.Text.Encoding.UTF8.GetString(output));return 0;
  case "explain":
    if(args.Contains("--receipt")&&args.Contains("--outcome"))throw new GraphException("decode","SchemaInvalid");
    Console.WriteLine(Json.Text(await tool.Explain(args.Contains("--receipt")?Option(args,"--receipt"):null,
        args.Contains("--outcome")?await ReadBounded(Option(args,"--outcome"),1048576):null)));return 0;
  default: Console.Error.WriteLine("Команды: contract --approval FILE; compile --program FILE --approval FILE; run --receipt FILE --input FILE; explain");return 2;
 }
}catch(GraphException e){if(e.InnerException!=null)Console.Error.WriteLine(Json.Text(new{stage=e.Stage,diagnostic=e.InnerException.GetType().Name}));Console.WriteLine(Json.Text(e.Outcome));return 1;}
catch(ArgumentException){Console.WriteLine(Json.Text(CloneOutcome.Reject("decode","SchemaInvalid")));return 2;}
catch(Exception e){Console.Error.WriteLine(Json.Text(new{stage="infrastructure",diagnostic=e.GetType().Name}));Console.WriteLine(Json.Text(CloneOutcome.Reject("infrastructure","InfrastructureFailure")));return 2;}
