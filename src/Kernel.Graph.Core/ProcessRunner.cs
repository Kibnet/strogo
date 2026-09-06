using System.Diagnostics;
using System.Text;
namespace Kernel.Graph;
public sealed record ProcessEvidence(int ExitCode,string Stdout,string Stderr,long ElapsedMs,bool TimedOut,bool OutputExceeded);
public static class Processes {
    public static async Task<ProcessEvidence> Run(string file,IEnumerable<string> args,string cwd,int timeoutSeconds,byte[]? input=null){
        using var p=new Process(); var start=p.StartInfo;start.FileName=file;start.WorkingDirectory=cwd;
        start.UseShellExecute=false;start.CreateNoWindow=true;start.RedirectStandardOutput=true;start.RedirectStandardError=true;start.RedirectStandardInput=true;
        foreach(var a in args)start.ArgumentList.Add(a);
        var keep=new[]{"SystemRoot","SystemDrive","ProgramData","ALLUSERSPROFILE","WINDIR","PATH","TEMP","TMP","USERPROFILE","APPDATA","LOCALAPPDATA","ProgramFiles","ProgramFiles(x86)","ProgramW6432","DOTNET_ROOT"};
        var env=keep.ToDictionary(k=>k,Environment.GetEnvironmentVariable);start.Environment.Clear();foreach(var(k,v)in env)if(v!=null)start.Environment[k]=v;
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"]="1";start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"]="1";
        var watch=Stopwatch.StartNew();p.Start();int captured=0;bool overflow=false,timeout=false;
        using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        void Kill(){try{p.Kill(true);}catch(InvalidOperationException){}}
        async Task<string> Drain(Stream stream){
            var buffer=new byte[4096];using var data=new MemoryStream();
            try {
                int n;while((n=await stream.ReadAsync(buffer,cts.Token))>0){
                    if(Interlocked.Add(ref captured,n)>1048576){overflow=true;Kill();cts.Cancel();break;}
                    await data.WriteAsync(buffer.AsMemory(0,n));
                }
            }catch(OperationCanceledException){}catch(IOException){}
            return Encoding.UTF8.GetString(data.ToArray());
        }
        var so=Drain(p.StandardOutput.BaseStream);var se=Drain(p.StandardError.BaseStream);
        var send=Task.Run(async()=>{try{if(input!=null)await p.StandardInput.BaseStream.WriteAsync(input,cts.Token);p.StandardInput.Close();}catch(IOException){}catch(OperationCanceledException){}});
        try{await Task.WhenAll(p.WaitForExitAsync(cts.Token),send,so,se).WaitAsync(cts.Token);}
        catch(OperationCanceledException){timeout=!overflow;Kill();}
        // Drains use the same cancellation deadline even if a child inherits the pipes.
        await Task.WhenAll(send,so,se);
        if(!p.HasExited){try{await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));}catch(TimeoutException){}}
        return new(p.HasExited?p.ExitCode:-1,await so,await se,watch.ElapsedMilliseconds,timeout,overflow);
    }
}
