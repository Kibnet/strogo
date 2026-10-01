using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class Probe
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ReadCount(out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ReadPointer(nint pointer);
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Target(int value) => value + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Decoy(int value) => value + 1;

    private static void Main(string[] args)
    {
        if (args.Length != 2 || !Path.IsPathFullyQualified(args[1]) || !OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new ArgumentException("mode, absolute observer DLL; Windows x64 required");
        var mode = args[0];
        if (mode is not ("sequential" or "no-call" or "parallel" or "overflow" or "inactive")) throw new ArgumentException("mode");
        var library = NativeLibrary.Load(Path.GetFullPath(args[1]));
        try
        {
            var export = NativeLibrary.GetExport(library, "StrogoObserverReadEntryCount");
            var read = Marshal.GetDelegateForFunctionPointer<ReadCount>(export);
            var rows = new List<object>();
            void Snapshot(string id, int expectedResult, uint expectedCount)
            {
                var hr = read(out var count);
                rows.Add(new { id, hresult = hr, count });
                if (hr != expectedResult || count != expectedCount) throw new Exception("count " + id);
            }
            var calls = 0;
            if (mode == "inactive") Snapshot("inactive", unchecked((int)0x8000ffff), 0);
            else
            {
                var nullRead = Marshal.GetDelegateForFunctionPointer<ReadPointer>(export);
                var nullResult = nullRead(0);
                if (nullResult != unchecked((int)0x80004003)) throw new Exception("null pointer");
                rows.Add(new { id = "null-pointer", hresult = nullResult, count = (uint?)null });
                Snapshot("before", 0, 0);
                void Call() { if (Target(41) != 42) throw new Exception("result"); }
                if (mode == "sequential")
                {
                    Call(); calls++; Snapshot("first", 0, 1);
                    Call(); calls++; Snapshot("second", 0, 2);
                }
                else if (mode == "no-call")
                {
                    if (Decoy(41) != 42) throw new Exception("decoy result");
                    Snapshot("decoy", 0, 0);
                }
                else if (mode == "parallel")
                {
                    Parallel.For(0, 1024, _ => Call()); calls = 1024;
                    Snapshot("joined", 0, 1024);
                }
                else
                {
                    for (var index = 0; index < 4096; index++) Call();
                    calls = 4096; Snapshot("capacity", 0, 4096);
                    Call(); calls++; Snapshot("overflow", unchecked((int)0x80004005), 0);
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(new { purpose = "native-count-prefix-not-admission", mode, targetCalls = calls, rows }));
        }
        finally { NativeLibrary.Free(library); }
    }
}
