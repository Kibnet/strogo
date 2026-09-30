using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Strogo.Modules;

/// <summary>Creates a Windows process atomically inside a non-breakaway, kill-on-close job.</summary>
internal sealed class G02ContainedProcess : IDisposable
{
    private readonly SafeFileHandle job;
    internal Process Process { get; }
    internal Stream Stdout { get; }
    internal Stream Stderr { get; }

    private G02ContainedProcess(SafeFileHandle job, Process process, Stream stdout, Stream stderr)
        => (this.job, Process, Stdout, Stderr) = (job, process, stdout, stderr);

    internal static G02ContainedProcess Start(ProcessStartInfo start)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw Refuse("VerifierPlatformUnsupported");
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid) { job.Dispose(); throw Refuse("VerifierContainmentFailed"); }
        SafeFileHandle? stdoutRead = null, stdoutWrite = null, stderrRead = null, stderrWrite = null, input = null;
        IntPtr attributes = IntPtr.Zero, jobs = IntPtr.Zero, inherited = IntPtr.Zero, limits = IntPtr.Zero;
        var initialized = false;
        ProcessInformation info = default;
        Process? acquiredProcess = null;
        Stream? acquiredStdout = null, acquiredStderr = null;
        try
        {
            // Windows x64 JOBOBJECT_EXTENDED_LIMIT_INFORMATION: LimitFlags at byte 16.
            limits = Marshal.AllocHGlobal(144);
            Marshal.Copy(new byte[144], 0, limits, 144);
            Marshal.WriteInt32(limits, 16, 0x2000); // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE; no breakaway flags.
            if (!SetInformationJobObject(job, 9, limits, 144)) throw Refuse("VerifierContainmentFailed");
            var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = true };
            if (!CreatePipe(out stdoutRead, out stdoutWrite, ref security, 0) ||
                !CreatePipe(out stderrRead, out stderrWrite, ref security, 0)) throw Refuse("VerifierContainmentFailed");
            if (!SetHandleInformation(stdoutRead, 1, 0) || !SetHandleInformation(stderrRead, 1, 0))
                throw Refuse("VerifierContainmentFailed");
            input = CreateFileW("NUL", 0x80000000, 3, ref security, 3, 0, IntPtr.Zero);
            if (input.IsInvalid) throw Refuse("VerifierContainmentFailed");
            nuint size = 0;
            InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref size);
            attributes = Marshal.AllocHGlobal(checked((int)size));
            if (!InitializeProcThreadAttributeList(attributes, 2, 0, ref size)) throw Refuse("VerifierContainmentFailed");
            initialized = true;
            jobs = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(jobs, job.DangerousGetHandle());
            inherited = Marshal.AllocHGlobal(IntPtr.Size * 3);
            Marshal.WriteIntPtr(inherited, 0, input.DangerousGetHandle());
            Marshal.WriteIntPtr(inherited, IntPtr.Size, stdoutWrite.DangerousGetHandle());
            Marshal.WriteIntPtr(inherited, IntPtr.Size * 2, stderrWrite.DangerousGetHandle());
            if (!UpdateProcThreadAttribute(attributes, 0, 0x2000D, jobs, (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero) ||
                !UpdateProcThreadAttribute(attributes, 0, 0x20002, inherited, (nuint)(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero))
                throw Refuse("VerifierContainmentFailed");
            var startup = new StartupInfoEx
            {
                Info = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>(), Flags = 0x100,
                    Input = input.DangerousGetHandle(), Output = stdoutWrite.DangerousGetHandle(), Error = stderrWrite.DangerousGetHandle() },
                Attributes = attributes
            };
            var command = new StringBuilder(Quote(start.FileName));
            foreach (var argument in start.ArgumentList) command.Append(' ').Append(Quote(argument));
            var environment = string.Join('\0', start.Environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key + "=" + pair.Value)) + "\0\0";
            if (!CreateProcessW(start.FileName, command, IntPtr.Zero, IntPtr.Zero, true,
                    0x80000 | 0x400 | 0x8000000, environment, start.WorkingDirectory, ref startup, out info))
                throw Refuse("VerifierContainmentFailed");
            // The native handle remains live until GetProcessById acquires its own handle.
            acquiredProcess = Process.GetProcessById(checked((int)info.Id));
            _ = acquiredProcess.Handle;
            acquiredStdout = new FileStream(stdoutRead, FileAccess.Read);
            stdoutRead = null;
            acquiredStderr = new FileStream(stderrRead, FileAccess.Read);
            stderrRead = null;
            return new(job, acquiredProcess, acquiredStdout, acquiredStderr);
        }
        catch
        {
            // Closing the sole job handle terminates any process already created in it.
            job.Dispose();
            acquiredStdout?.Dispose(); acquiredStderr?.Dispose(); acquiredProcess?.Dispose();
            throw;
        }
        finally
        {
            if (info.Thread != IntPtr.Zero) CloseHandle(info.Thread);
            if (info.Process != IntPtr.Zero) CloseHandle(info.Process);
            stdoutRead?.Dispose(); stderrRead?.Dispose(); stdoutWrite?.Dispose(); stderrWrite?.Dispose(); input?.Dispose();
            if (initialized) DeleteProcThreadAttributeList(attributes);
            foreach (var allocation in new[] { attributes, jobs, inherited, limits })
                if (allocation != IntPtr.Zero) Marshal.FreeHGlobal(allocation);
        }
    }

    internal void Kill()
    {
        if (!TerminateJobObject(job, 1)) throw Refuse("VerifierTerminationFailed");
    }

    internal async Task TerminateAndDrainAsync()
    {
        Kill();
        var timer = Stopwatch.StartNew();
        var accounting = Marshal.AllocHGlobal(48);
        try
        {
            while (true)
            {
                // Windows x64 JOBOBJECT_BASIC_ACCOUNTING_INFORMATION.ActiveProcesses.
                if (!QueryInformationJobObject(job, 1, accounting, 48, IntPtr.Zero))
                    throw Refuse("VerifierTerminationFailed");
                if (Marshal.ReadInt32(accounting, 40) == 0) return;
                if (timer.Elapsed >= TimeSpan.FromSeconds(5)) throw Refuse("VerifierTerminationFailed");
                await Task.Delay(10);
            }
        }
        finally { Marshal.FreeHGlobal(accounting); }
    }

    public void Dispose()
    {
        job.Dispose(); // Also kills descendants when the original parent has already exited.
        Stdout.Dispose(); Stderr.Dispose(); Process.Dispose();
    }

    private static string Quote(string argument)
    {
        var output = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { slashes++; continue; }
            output.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            output.Append(character); slashes = 0;
        }
        return output.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static ModuleException Refuse(string code) => ModulesExceptionFactory.Error("package", code);
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { internal int Length; internal IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] internal bool Inherit; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        internal int Size; internal IntPtr Reserved, Desktop, Title;
        internal int X, Y, XSize, YSize, XCount, YCount, Fill, Flags;
        internal short Show, ReservedSize; internal IntPtr ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { internal StartupInfo Info; internal IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { internal IntPtr Process, Thread; internal uint Id, ThreadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObjectW(IntPtr security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int type, IntPtr information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(SafeFileHandle job, int type, IntPtr information, uint length, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes security, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, ref SecurityAttributes security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, string environment, string directory, ref StartupInfoEx startup, out ProcessInformation information);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
