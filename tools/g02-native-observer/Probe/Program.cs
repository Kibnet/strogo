using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
class Probe {
 [MethodImpl(MethodImplOptions.NoInlining)] public static int Target(int x) => x + 1;
 [MethodImpl(MethodImplOptions.NoInlining)] public static int Decoy(int x) => x + 1;
 static void Main(string[] args) {
  var mode=args.Length==0?"call":args[0];
  if(mode=="overflow") { for(int i=0;i<4097;i++) if(Target(41)!=42) throw new Exception("result"); Console.WriteLine(4097); }
  else if(mode=="parallel") { Parallel.For(0,1024,i=>{if(Target(41)!=42)throw new Exception("result");}); Console.WriteLine(1024); }
  else Console.WriteLine(mode=="no-call"?Decoy(41):Target(41));
 }
}
