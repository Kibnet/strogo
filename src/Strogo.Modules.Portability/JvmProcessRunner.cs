using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Kernel.Core;

namespace Strogo.Modules.Portability;

public enum JvmProcessKind { DafnyProbe, JavacProbe, GitProbe, DotNetProbe, ConsumerExecution, JarPackaging }

public sealed record JvmProcessRequest(
    string Executable,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> EnvironmentVariablesToRemove,
    TimeSpan Timeout,
    int OutputByteLimit,
    TimeSpan CleanupTimeout,
    JvmProcessKind Kind);

public sealed record JvmProcessResult(
    int ExitCode,
    int ProcessId,
    long ElapsedMilliseconds,
    byte[] Stdout,
    byte[] Stderr,
    string StdoutDigest,
    string StderrDigest,
    long StdoutBytes,
    long StderrBytes);

public static class JvmProcessRunner
{
    public static JvmProcessResult Run(JvmProcessRequest request)
    {
        try { return RunAsync(request).GetAwaiter().GetResult(); }
        catch (PortabilityContractException) { throw; }
        catch (Exception exception)
        {
            var contract = TryContract(request?.Kind);
            throw new PortabilityContractException("TargetBuildRejected", "$/process", new
            {
                reason = contract?.FailureReason ?? "ProcessContractInvalid",
                failure = exception.GetType().Name,
                message = exception.Message
            });
        }
    }

    private static async Task<JvmProcessResult> RunAsync(JvmProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Path.IsPathFullyQualified(request.Executable) || !File.Exists(request.Executable)) throw new ArgumentException("executable must be an existing absolute file", nameof(request));
        if (!Path.IsPathFullyQualified(request.WorkingDirectory) || !Directory.Exists(request.WorkingDirectory)) throw new ArgumentException("working directory must be an existing absolute directory", nameof(request));
        if (request.Timeout <= TimeSpan.Zero || request.CleanupTimeout <= TimeSpan.Zero || request.OutputByteLimit <= 0) throw new ArgumentOutOfRangeException(nameof(request));
        var contract = Contract(request.Kind);

        var startInfo = new ProcessStartInfo(request.Executable)
        {
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in request.Arguments) startInfo.ArgumentList.Add(argument);
        foreach (var name in request.EnvironmentVariablesToRemove.Distinct(StringComparer.Ordinal)) startInfo.Environment.Remove(name);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"failed to start {request.Executable}");
        try
        {
        using var drainCancellation = new CancellationTokenSource();
        var started = Stopwatch.StartNew();
        var overflow = new TaskCompletionSource<StreamOverflow>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stdout = new StreamCapture("stdout", request.OutputByteLimit, overflow);
        using var stderr = new StreamCapture("stderr", request.OutputByteLimit, overflow);
        var stdoutTask = DrainAsync(process.StandardOutput.BaseStream, stdout, drainCancellation.Token);
        var stderrTask = DrainAsync(process.StandardError.BaseStream, stderr, drainCancellation.Token);
        var exitTask = process.WaitForExitAsync();
        var completionTask = Task.WhenAll(exitTask, stdoutTask, stderrTask);
        using var monitorCancellation = new CancellationTokenSource();
        var knownProcesses = new Dictionary<(int Id, long StartTimeUtcTicks), KnownProcess>();
        var rootIdentity = new KnownProcess(process.Id, 0, process.StartTime.ToUniversalTime().Ticks);
        knownProcesses[(rootIdentity.Id, rootIdentity.StartTimeUtcTicks)] = rootIdentity;
        var monitorTask = MonitorTreeAsync(process.Id, knownProcesses, monitorCancellation.Token);
        var timeoutTask = Task.Delay(request.Timeout);
        var first = await Task.WhenAny(completionTask, timeoutTask, overflow.Task).ConfigureAwait(false);
        monitorCancellation.Cancel();
        Exception? monitorFailure = null;
        try { await monitorTask.ConfigureAwait(false); }
        catch (Exception exception) { monitorFailure = exception; }
        string? failureReason = null;
        object? failureDetails = null;
        if (first == timeoutTask)
        {
            failureReason = $"{contract.Prefix}Timeout";
            failureDetails = new { timeoutMilliseconds = ((long)request.Timeout.TotalMilliseconds).ToString() };
        }
        else if (first == overflow.Task)
        {
            var value = await overflow.Task.ConfigureAwait(false);
            failureReason = $"{contract.Prefix}OutputLimitExceeded";
            failureDetails = new { stream = value.Stream, actualAtDetection = value.ActualAtDetection.ToString(), max = request.OutputByteLimit.ToString() };
        }
        else if (completionTask.IsFaulted || monitorFailure is not null)
        {
            failureReason = contract.FailureReason;
            failureDetails = new
            {
                completionFailure = completionTask.Exception?.GetBaseException().GetType().Name,
                monitorFailure = monitorFailure?.GetType().Name
            };
        }

        Remember(knownProcesses, ProcessTree.Snapshot(process.Id));
        if (failureReason is not null)
        {
            var cleanup = await CleanupAsync(process, completionTask, request.CleanupTimeout, knownProcesses.Values).ConfigureAwait(false);
            Remember(knownProcesses, cleanup.Known);
            if (!cleanup.Passed)
            {
                drainCancellation.Cancel();
                process.StandardOutput.Close();
                process.StandardError.Close();
                failureDetails = new { original = failureDetails, drainsCompleted = stdoutTask.IsCompleted && stderrTask.IsCompleted };
                RejectWithCaptures($"{contract.Prefix}ProcessCleanupFailed", process.Id, failureReason, failureDetails, stdout.Snapshot(), stderr.Snapshot(), knownProcesses.Values, cleanup.Live, "Failed");
            }
        }
        else
        {
            await completionTask.ConfigureAwait(false);
            Remember(knownProcesses, ProcessTree.Snapshot(process.Id));
            var residual = knownProcesses.Values.Where(item => item.Id != process.Id && ProcessTree.IsAlive(item)).ToImmutableArray();
            if (!residual.IsEmpty)
            {
                var cleanup = await CleanupAsync(process, Task.CompletedTask, request.CleanupTimeout, knownProcesses.Values).ConfigureAwait(false);
                Remember(knownProcesses, cleanup.Known);
                RejectWithCaptures($"{contract.Prefix}ProcessCleanupFailed", process.Id, "ResidualDescendant", new { }, stdout.Snapshot(), stderr.Snapshot(), knownProcesses.Values, cleanup.Live, cleanup.Passed ? "RecoveredResidual" : "Failed");
            }
        }
        started.Stop();
        var stdoutResult = stdout.Snapshot();
        var stderrResult = stderr.Snapshot();
        var lateOverflow = stdoutResult.BytesRead > request.OutputByteLimit
            ? new StreamOverflow("stdout", request.OutputByteLimit + 1L)
            : stderrResult.BytesRead > request.OutputByteLimit
                ? new StreamOverflow("stderr", request.OutputByteLimit + 1L)
                : null;
        if (failureReason is null && lateOverflow is not null)
        {
            failureReason = $"{contract.Prefix}OutputLimitExceeded";
            failureDetails = new { stream = lateOverflow.Stream, actualAtDetection = lateOverflow.ActualAtDetection.ToString(), max = request.OutputByteLimit.ToString() };
        }
        if (failureReason is not null)
            RejectWithCaptures(failureReason, process.Id, null, failureDetails, stdoutResult, stderrResult, knownProcesses.Values, [], "Passed");
        return new(
            process.ExitCode,
            process.Id,
            started.ElapsedMilliseconds,
            stdoutResult.Prefix,
            stderrResult.Prefix,
            stdoutResult.Digest,
            stderrResult.Digest,
            stdoutResult.BytesRead,
            stderrResult.BytesRead);
        }
        catch (PortabilityContractException) { throw; }
        catch (Exception exception)
        {
            var known = ProcessTree.Snapshot(process.Id);
            var completion = process.HasExited ? Task.CompletedTask : process.WaitForExitAsync();
            var cleanup = await CleanupAsync(process, completion, request.CleanupTimeout, known).ConfigureAwait(false);
            throw new PortabilityContractException("TargetBuildRejected", "$/process", new
            {
                reason = cleanup.Passed ? contract.FailureReason : $"{contract.Prefix}ProcessCleanupFailed",
                originalReason = contract.FailureReason,
                failure = exception.GetType().Name,
                message = exception.Message,
                cleanup = cleanup.Passed ? "Passed" : "Failed",
                evidenceUnavailable = "stream capture lifecycle failed",
                knownProcessTree = cleanup.Known.OrderBy(item => item.Id).Select(KnownEvidence).ToArray(),
                liveProcessIds = cleanup.Live.OrderBy(item => item.Id).Select(item => item.Id.ToString()).ToArray()
            });
        }
    }

    private static ProcessContract Contract(JvmProcessKind kind)
        => kind switch
        {
            JvmProcessKind.DafnyProbe => new("Dafny", "DafnyProbeFailed"),
            JvmProcessKind.JavacProbe => new("Javac", "JavacProbeFailed"),
            JvmProcessKind.GitProbe => new("Git", "GitProbeFailed"),
            JvmProcessKind.DotNetProbe => new("DotNet", "DotNetProbeFailed"),
            JvmProcessKind.ConsumerExecution => new("Consumer", "ConsumerCompilationFailed"),
            JvmProcessKind.JarPackaging => new("Jar", "JarInvocationFailed"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static ProcessContract? TryContract(JvmProcessKind? kind)
    {
        if (kind is null) return null;
        try { return Contract(kind.Value); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private sealed record ProcessContract(string Prefix, string FailureReason);

    private static async Task DrainAsync(Stream stream, StreamCapture capture, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                capture.Append(buffer.AsSpan(0, read));
            }
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested && exception is OperationCanceledException or IOException or ObjectDisposedException) { }
    }

    private static async Task MonitorTreeAsync(int rootProcessId, Dictionary<(int, long), KnownProcess> known, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Remember(known, ProcessTree.Snapshot(rootProcessId));
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static void Remember(Dictionary<(int, long), KnownProcess> known, IEnumerable<KnownProcess> observed)
    {
        foreach (var item in observed) known[(item.Id, item.StartTimeUtcTicks)] = item;
    }

    private static async Task<CleanupResult> CleanupAsync(Process root, Task completionTask, TimeSpan timeout, IEnumerable<KnownProcess> seed)
    {
        var known = seed.ToDictionary(item => (item.Id, item.StartTimeUtcTicks));
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout)
        {
            Remember(known, ProcessTree.Snapshot(root.Id));
            foreach (var item in known.Values.OrderByDescending(item => item.Id)) ProcessTree.TryKill(item, item.Id == root.Id);
            var live = known.Values.Where(ProcessTree.IsAlive).ToImmutableArray();
            if (live.IsEmpty && completionTask.IsCompleted)
            {
                try { await completionTask.ConfigureAwait(false); } catch { }
                return new(true, known.Values.ToImmutableArray(), []);
            }
            await Task.Delay(20).ConfigureAwait(false);
        }
        return new(false, known.Values.ToImmutableArray(), known.Values.Where(ProcessTree.IsAlive).ToImmutableArray());
    }

    private static void RejectWithCaptures(
        string reason,
        int rootProcessId,
        string? originalReason,
        object? failureDetails,
        DrainResult stdout,
        DrainResult stderr,
        IEnumerable<KnownProcess> known,
        IEnumerable<KnownProcess> live,
        string cleanup)
        => throw new PortabilityContractException("TargetBuildRejected", "$/process", new
        {
            reason,
            originalReason,
            failureDetails,
            cleanup,
            rootProcessId = rootProcessId.ToString(),
            stdout = Evidence(stdout),
            stderr = Evidence(stderr),
            knownProcessTree = known.DistinctBy(item => (item.Id, item.StartTimeUtcTicks)).OrderBy(item => item.Id).Select(KnownEvidence).ToArray(),
            liveProcessIds = live.Select(item => item.Id.ToString()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
        });

    private static object KnownEvidence(KnownProcess item)
        => new
        {
            processId = item.Id.ToString(),
            parentProcessId = item.ParentId.ToString(),
            startTimeUtcTicks = item.StartTimeUtcTicks.ToString()
        };

    private static object Evidence(DrainResult value)
    {
        var retained = value.Prefix.AsSpan(0, Math.Min(value.Prefix.Length, 4096)).ToArray();
        return new
        {
            bytesRead = value.BytesRead.ToString(),
            digest = value.Digest,
            prefixBase64 = Convert.ToBase64String(retained),
            prefixBytes = retained.Length.ToString(),
            prefixTruncated = retained.Length < value.BytesRead
        };
    }

    private sealed record StreamOverflow(string Stream, long ActualAtDetection);
    private sealed record DrainResult(byte[] Prefix, string Digest, long BytesRead);
    private sealed record CleanupResult(bool Passed, ImmutableArray<KnownProcess> Known, ImmutableArray<KnownProcess> Live);

    private sealed class StreamCapture(string name, int limit, TaskCompletionSource<StreamOverflow> overflow) : IDisposable
    {
        private readonly object gate = new();
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly MemoryStream prefix = new(limit);
        private long bytesRead;

        public void Append(ReadOnlySpan<byte> bytes)
        {
            lock (gate)
            {
                var previous = bytesRead;
                bytesRead += bytes.Length;
                hash.AppendData(bytes);
                if (previous < limit) prefix.Write(bytes[..(int)Math.Min(bytes.Length, limit - previous)]);
                if (previous <= limit && bytesRead > limit) overflow.TrySetResult(new(name, limit + 1L));
            }
        }

        public DrainResult Snapshot()
        {
            lock (gate) return new(prefix.ToArray(), Convert.ToHexStringLower(hash.GetCurrentHash()), bytesRead);
        }

        public void Dispose()
        {
            hash.Dispose();
            prefix.Dispose();
        }
    }

    private sealed record KnownProcess(int Id, int ParentId, long StartTimeUtcTicks);

    private static class ProcessTree
    {
        private const uint SnapshotProcesses = 0x00000002;

        public static ImmutableArray<KnownProcess> Snapshot(int rootProcessId)
        {
            var pairs = OperatingSystem.IsWindows() ? SnapshotWindows() : OperatingSystem.IsLinux() ? SnapshotLinux() : [];
            var ids = new HashSet<int> { rootProcessId };
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var pair in pairs)
                    if (ids.Contains(pair.Parent) && ids.Add(pair.Id)) changed = true;
            }
            var parents = pairs.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First().Parent);
            return ids.Select(id => TryKnown(id, parents.GetValueOrDefault(id))).Where(item => item is not null).Cast<KnownProcess>().OrderBy(item => item.Id).ToImmutableArray();
        }

        public static bool IsAlive(KnownProcess known)
        {
            try
            {
                using var process = Process.GetProcessById(known.Id);
                return !process.HasExited && (known.StartTimeUtcTicks == 0 || process.StartTime.ToUniversalTime().Ticks == known.StartTimeUtcTicks);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
        }

        public static void TryKill(KnownProcess known, bool entireTree)
        {
            if (!IsAlive(known)) return;
            try
            {
                using var process = Process.GetProcessById(known.Id);
                process.Kill(entireTree);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        }

        private static KnownProcess? TryKnown(int processId, int parentId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return new(processId, parentId, process.StartTime.ToUniversalTime().Ticks);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
        }

        private static List<(int Id, int Parent)> SnapshotWindows()
        {
            var result = new List<(int, int)>();
            var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
            if (snapshot == new IntPtr(-1)) return result;
            try
            {
                var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
                if (!Process32First(snapshot, ref entry)) return result;
                do
                {
                    result.Add(((int)entry.ProcessId, (int)entry.ParentProcessId));
                    entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
                } while (Process32Next(snapshot, ref entry));
                return result;
            }
            finally { CloseHandle(snapshot); }
        }

        private static List<(int Id, int Parent)> SnapshotLinux()
        {
            var result = new List<(int, int)>();
            foreach (var directory in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(directory), out var processId)) continue;
                try
                {
                    var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                    var close = stat.LastIndexOf(')');
                    if (close < 0) continue;
                    var fields = stat[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length >= 2 && int.TryParse(fields[1], out var parentId)) result.Add((processId, parentId));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            return result;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry32
        {
            public uint Size;
            public uint Usage;
            public uint ProcessId;
            public IntPtr DefaultHeapId;
            public uint ModuleId;
            public uint Threads;
            public uint ParentProcessId;
            public int BasePriority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string? ExecutableFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32FirstW")] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32NextW")] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}

public static class JvmQuarantine
{
    public static T Execute<T>(string runRoot, string target, Func<T> action, string reason = "ProbeCleanupFailed")
    {
        ArgumentNullException.ThrowIfNull(action);
        Exception? operationFailure = null;
        T? result = default;
        try
        {
            result = action();
        }
        catch (Exception exception)
        {
            operationFailure = exception;
        }
        try
        {
            Delete(runRoot, target, reason);
        }
        catch (PortabilityContractException cleanupFailure)
        {
            throw new PortabilityContractException("TargetBuildRejected", "$/cleanup", new
            {
                reason,
                cleanupFailure = cleanupFailure.Details,
                originalFailure = FailureEvidence(operationFailure)
            });
        }
        if (operationFailure is PortabilityContractException portabilityFailure)
        {
            var originalReason = TryReason(portabilityFailure.Details);
            throw new PortabilityContractException(portabilityFailure.Code, portabilityFailure.Locus, new
            {
                reason = originalReason,
                cleanup = "Passed",
                originalDetails = portabilityFailure.Details
            });
        }
        if (operationFailure is not null) ExceptionDispatchInfo.Capture(operationFailure).Throw();
        return result!;
    }

    public static void Delete(string runRoot, string target, string reason = "ProbeCleanupFailed")
    {
        try
        {
            DeleteCore(runRoot, target, reason);
        }
        catch (PortabilityContractException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new PortabilityContractException("TargetBuildRejected", "$/cleanup", new
            {
                reason,
                residualPath = target,
                failure = exception.GetType().Name
            });
        }
    }

    private static void DeleteCore(string runRoot, string target, string reason)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(runRoot));
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        var relative = Path.GetRelativePath(root, candidate);
        if (relative is "." or "" || Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment == ".."))
            Reject(reason, candidate);
        for (var current = candidate; !string.Equals(current, root, StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current) ?? "")
        {
            if (current.Length == 0) Reject(reason, candidate);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                Reject(reason, current);
        }
        if (Directory.Exists(candidate)) Directory.Delete(candidate, recursive: true);
        else if (File.Exists(candidate)) File.Delete(candidate);
        if (Directory.Exists(candidate) || File.Exists(candidate)) Reject(reason, candidate);
    }

    private static void Reject(string reason, string path)
        => throw new PortabilityContractException("TargetBuildRejected", "$/cleanup", new { reason, residualPath = path });

    private static string TryReason(object details)
    {
        using var document = JsonDocument.Parse(CanonicalJson.Encode(details));
        return document.RootElement.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
            ? reason.GetString() ?? "OperationFailed"
            : "OperationFailed";
    }

    private static object? FailureEvidence(Exception? exception)
        => exception switch
        {
            null => null,
            PortabilityContractException portability => new { type = exception.GetType().Name, code = portability.Code, locus = portability.Locus, details = portability.Details },
            _ => new { type = exception.GetType().Name, message = exception.Message }
        };
}
