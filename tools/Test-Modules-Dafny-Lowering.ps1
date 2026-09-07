param(
    [string]$RunDirectory = "artifacts/local-validation/e05/modules-dafny-lowering"
)

$ErrorActionPreference = 'Stop'
$processTimeoutMilliseconds = 180000
$processOutputByteLimit = 1MB

if (-not ('Strogo.Tools.BoundedProcessRunner' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Strogo.Tools
{
    public sealed class BoundedProcessException : Exception
    {
        public string Code { get; }
        public string Executable { get; }
        public int TimeoutMilliseconds { get; }
        public int OutputByteLimit { get; }
        public string CapturedOutput { get; }

        public BoundedProcessException(string code, string executable, int timeoutMilliseconds, int outputByteLimit, string capturedOutput)
            : base(code + ": " + executable)
        {
            Code = code;
            Executable = executable;
            TimeoutMilliseconds = timeoutMilliseconds;
            OutputByteLimit = outputByteLimit;
            CapturedOutput = capturedOutput;
        }
    }

    public sealed class BoundedProcessResult
    {
        public int ExitCode { get; }
        public string Output { get; }
        public long ElapsedMilliseconds { get; }

        public BoundedProcessResult(int exitCode, string output, long elapsedMilliseconds)
        {
            ExitCode = exitCode;
            Output = output;
            ElapsedMilliseconds = elapsedMilliseconds;
        }
    }

    internal sealed class BoundedCapture
    {
        private readonly object gate = new object();
        private readonly MemoryStream stdout = new MemoryStream();
        private readonly MemoryStream stderr = new MemoryStream();
        private int remaining;

        public bool Overflow { get; private set; }

        public BoundedCapture(int limit) { remaining = limit; }

        public void Append(bool isError, byte[] buffer, int count)
        {
            lock (gate)
            {
                var accepted = Math.Min(remaining, count);
                if (accepted > 0)
                    (isError ? stderr : stdout).Write(buffer, 0, accepted);
                remaining -= accepted;
                if (accepted != count)
                    Overflow = true;
            }
        }

        public string Text()
        {
            lock (gate)
            {
                var encoding = new UTF8Encoding(false, false);
                var standard = encoding.GetString(stdout.ToArray());
                var error = encoding.GetString(stderr.ToArray());
                return string.IsNullOrEmpty(error) ? standard : standard + error;
            }
        }
    }

    public static class BoundedProcessRunner
    {
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private const uint CreateSuspended = 0x00000004;
        private const uint CreateNoWindow = 0x08000000;
        private const uint StartfUseStdHandles = 0x00000100;
        private const uint WaitObject0 = 0x00000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimitInformation
        {
            public BasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public uint cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public ushort wShowWindow;
            public ushort cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, int informationClass, ref ExtendedLimitInformation information, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(string applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);

        public static BoundedProcessResult Run(string executable, string[] arguments, string workingDirectory, int timeoutMilliseconds, int outputByteLimit)
        {
            if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            if (outputByteLimit <= 0) throw new ArgumentOutOfRangeException(nameof(outputByteLimit));

            var job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
                throw new BoundedProcessException("ProcessContainmentFailed", executable, timeoutMilliseconds, outputByteLimit, string.Empty);
            var processInfo = new ProcessInformation();
            try
            {
                var limits = new ExtendedLimitInformation();
                limits.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
                if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimitInformation>()))
                    throw new BoundedProcessException("ProcessContainmentFailed", executable, timeoutMilliseconds, outputByteLimit, string.Empty);

                using var standardInput = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
                using var standardOutput = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
                using var standardError = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
                var startup = new StartupInfo
                {
                    cb = (uint)Marshal.SizeOf<StartupInfo>(),
                    dwFlags = StartfUseStdHandles,
                    hStdInput = standardInput.ClientSafePipeHandle.DangerousGetHandle(),
                    hStdOutput = standardOutput.ClientSafePipeHandle.DangerousGetHandle(),
                    hStdError = standardError.ClientSafePipeHandle.DangerousGetHandle()
                };
                var commandLine = new StringBuilder(BuildCommandLine(executable, arguments));
                if (!CreateProcessW(executable, commandLine, IntPtr.Zero, IntPtr.Zero, true, CreateSuspended | CreateNoWindow, IntPtr.Zero, workingDirectory, ref startup, out processInfo))
                    throw new BoundedProcessException("ProcessStartFailed", executable, timeoutMilliseconds, outputByteLimit, string.Empty);
                standardInput.DisposeLocalCopyOfClientHandle();
                standardOutput.DisposeLocalCopyOfClientHandle();
                standardError.DisposeLocalCopyOfClientHandle();

                if (!AssignProcessToJobObject(job, processInfo.hProcess))
                {
                    TerminateProcess(processInfo.hProcess, 1);
                    throw new BoundedProcessException("ProcessContainmentFailed", executable, timeoutMilliseconds, outputByteLimit, string.Empty);
                }
                if (ResumeThread(processInfo.hThread) == uint.MaxValue)
                {
                    TerminateJobObject(job, 1);
                    throw new BoundedProcessException("ProcessResumeFailed", executable, timeoutMilliseconds, outputByteLimit, string.Empty);
                }
                CloseHandle(processInfo.hThread);
                processInfo.hThread = IntPtr.Zero;
                standardInput.Dispose();

                using var cancellation = new CancellationTokenSource();
                var capture = new BoundedCapture(outputByteLimit);
                var stdout = ReadAsync(standardOutput, capture, false, cancellation.Token);
                var stderr = ReadAsync(standardError, capture, true, cancellation.Token);
                var stopwatch = Stopwatch.StartNew();
                while (!HasExited(processInfo.hProcess) || !stdout.IsCompleted || !stderr.IsCompleted)
                {
                    if (capture.Overflow)
                        FailBoundedProcess(job, processInfo.hProcess, cancellation, stdout, stderr, "ProcessOutputLimitExceeded", executable, timeoutMilliseconds, outputByteLimit, capture.Text());
                    if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds)
                        FailBoundedProcess(job, processInfo.hProcess, cancellation, stdout, stderr, "ProcessTimeout", executable, timeoutMilliseconds, outputByteLimit, capture.Text());
                    Thread.Sleep(20);
                }

                Task.WaitAll(stdout, stderr);
                if (capture.Overflow)
                    FailBoundedProcess(job, processInfo.hProcess, cancellation, stdout, stderr, "ProcessOutputLimitExceeded", executable, timeoutMilliseconds, outputByteLimit, capture.Text());
                if (!GetExitCodeProcess(processInfo.hProcess, out var exitCode))
                    throw new BoundedProcessException("ProcessExitCodeUnavailable", executable, timeoutMilliseconds, outputByteLimit, capture.Text());
                return new BoundedProcessResult(unchecked((int)exitCode), capture.Text(), stopwatch.ElapsedMilliseconds);
            }
            finally
            {
                if (processInfo.hThread != IntPtr.Zero) CloseHandle(processInfo.hThread);
                if (processInfo.hProcess != IntPtr.Zero) CloseHandle(processInfo.hProcess);
                CloseHandle(job);
            }
        }

        private static async Task ReadAsync(Stream stream, BoundedCapture capture, bool isError, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            try
            {
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    capture.Append(isError, buffer, read);
                    if (capture.Overflow) return;
                }
            }
            catch (OperationCanceledException) { }
        }

        private static bool HasExited(IntPtr process)
            => WaitForSingleObject(process, 0) == WaitObject0;

        private static void FailBoundedProcess(IntPtr job, IntPtr process, CancellationTokenSource cancellation, Task stdout, Task stderr, string code, string executable, int timeoutMilliseconds, int outputByteLimit, string capturedOutput)
        {
            TerminateJobObject(job, 1);
            TerminateProcess(process, 1);
            cancellation.Cancel();
            try { Task.WaitAll(new[] { stdout, stderr }, 2000); }
            catch (AggregateException) { }
            throw new BoundedProcessException(code, executable, timeoutMilliseconds, outputByteLimit, capturedOutput);
        }

        private static string BuildCommandLine(string executable, string[] arguments)
        {
            var builder = new StringBuilder(QuoteArgument(executable));
            foreach (var argument in arguments)
                builder.Append(' ').Append(QuoteArgument(argument));
            return builder.ToString();
        }

        private static string QuoteArgument(string argument)
        {
            if (argument.Length != 0 && argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
                return argument;
            var builder = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (character == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1).Append('"');
                    backslashes = 0;
                    continue;
                }
                builder.Append('\\', backslashes).Append(character);
                backslashes = 0;
            }
            return builder.Append('\\', backslashes * 2).Append('"').ToString();
        }
    }
}
'@
}

function Invoke-BoundedProcess {
    param(
        [Parameter(Mandatory)] [string] $Executable,
        [string[]] $Arguments = @(),
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [int] $TimeoutMilliseconds = $processTimeoutMilliseconds,
        [int] $OutputByteLimit = $processOutputByteLimit
    )

    $resolvedExecutable = (Get-Command -Name $Executable -CommandType Application -ErrorAction Stop).Source
    return [Strogo.Tools.BoundedProcessRunner]::Run(
        $resolvedExecutable,
        $Arguments,
        $WorkingDirectory,
        $TimeoutMilliseconds,
        $OutputByteLimit)
}

function Assert-BoundedFailure {
    param(
        [Parameter(Mandatory)] [scriptblock] $Action,
        [Parameter(Mandatory)] [string] $ExpectedCode
    )

    try {
        & $Action | Out-Null
        throw "Expected bounded process failure: $ExpectedCode"
    }
    catch {
        $candidate = $_.Exception
        while ($null -ne $candidate -and $candidate -isnot [Strogo.Tools.BoundedProcessException]) {
            $candidate = $candidate.InnerException
        }
        if ($null -eq $candidate -or $candidate.Code -ne $ExpectedCode) { throw }
        return $candidate
    }
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $RunDirectory))
$dafny = Join-Path $repoRoot '.tools/dafny/dafny/dafny.exe'
$shellExecutable = (Get-Process -Id $PID).Path
$timeoutSelfTestFailure = Assert-BoundedFailure -ExpectedCode 'ProcessTimeout' -Action {
    Invoke-BoundedProcess -Executable $shellExecutable -WorkingDirectory $repoRoot -TimeoutMilliseconds 100 -OutputByteLimit 1024 -Arguments @('-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 5')
}
$outputLimitSelfTestFailure = Assert-BoundedFailure -ExpectedCode 'ProcessOutputLimitExceeded' -Action {
    Invoke-BoundedProcess -Executable $shellExecutable -WorkingDirectory $repoRoot -TimeoutMilliseconds 10000 -OutputByteLimit 1024 -Arguments @('-NoProfile', '-NonInteractive', '-Command', "[Console]::Out.Write('x' * 65536)")
}
$encodedChildCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes('Start-Sleep -Seconds 30'))
$escapedShellExecutable = $shellExecutable.Replace("'", "''")
$orphanSpawner = "`$child = Start-Process -FilePath '$escapedShellExecutable' -ArgumentList @('-NoProfile','-NonInteractive','-EncodedCommand','$encodedChildCommand') -NoNewWindow -PassThru; [Console]::Out.WriteLine('childPid=' + `$child.Id)"
$orphanSelfTestFailure = Assert-BoundedFailure -ExpectedCode 'ProcessTimeout' -Action {
    Invoke-BoundedProcess -Executable $shellExecutable -WorkingDirectory $repoRoot -TimeoutMilliseconds 2000 -OutputByteLimit 4096 -Arguments @('-NoProfile', '-NonInteractive', '-Command', $orphanSpawner)
}
if ($orphanSelfTestFailure.CapturedOutput -notmatch 'childPid=([1-9][0-9]*)') {
    throw "Bounded orphan self-test did not report a child PID:`n$($orphanSelfTestFailure.CapturedOutput)"
}
$orphanChildPid = [int]$Matches[1]
Start-Sleep -Milliseconds 100
if ($null -ne (Get-Process -Id $orphanChildPid -ErrorAction SilentlyContinue)) {
    throw "Bounded orphan self-test left child process $orphanChildPid running"
}
$dafnyManifest = Get-Content (Join-Path $repoRoot 'tools/dafny.json') -Raw | ConvertFrom-Json
$dafnyArchive = Join-Path $repoRoot '.tools/downloads/dafny-4.11.0.zip'
if (-not (Test-Path -LiteralPath $dafnyArchive) -or (Get-FileHash -LiteralPath $dafnyArchive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $dafnyManifest.sha256) {
    throw 'Dafny archive missing or digest mismatch'
}
if (-not (Test-Path -LiteralPath $dafny) -or (Get-FileHash -LiteralPath $dafny -Algorithm SHA256).Hash.ToLowerInvariant() -cne $dafnyManifest.executableSha256) {
    throw 'Dafny executable missing or digest mismatch'
}
$dafnyVersionRun = Invoke-BoundedProcess -Executable $dafny -WorkingDirectory $repoRoot -Arguments @('--version')
$dafnyVersion = $dafnyVersionRun.Output.Trim()
if ($dafnyVersionRun.ExitCode -ne 0 -or -not $dafnyVersion.StartsWith($dafnyManifest.version + '+', [StringComparison]::Ordinal)) {
    throw "Dafny version mismatch:`n$dafnyVersion"
}
$dafnyToolEvidence = [ordered]@{
    version = $dafnyVersion
    archiveSha256 = $dafnyManifest.sha256
    executableSha256 = $dafnyManifest.executableSha256
}

New-Item -ItemType Directory -Force -Path $runPath | Out-Null
$moduleReport = Join-Path $runPath 'modules-v0.2.json'
$safeSource = Join-Path $runPath 'Candidate.dfy'
$callSource = Join-Path $runPath 'CallCandidate.dfy'
$unsafeSource = Join-Path $runPath 'UnsafeCandidate.dfy'
$scalarSource = Join-Path $runPath 'ScalarCandidate.dfy'
$compositeSource = Join-Path $runPath 'CompositeCandidate.dfy'
$unsafeCompositeSource = Join-Path $runPath 'UnsafeCompositeCandidate.dfy'
$ownerSource = Join-Path $runPath 'OwnerCandidate.dfy'
$weakOwnerSource = Join-Path $runPath 'WeakOwnerCandidate.dfy'
$alternativeOwnerSource = Join-Path $runPath 'AlternativeOwnerCandidate.dfy'
$wrongOwnerSource = Join-Path $runPath 'WrongOwnerCandidate.dfy'
$compositeOwnerSource = Join-Path $runPath 'CompositeOwnerCandidate.dfy'
$alternativeCompositeOwnerSource = Join-Path $runPath 'AlternativeCompositeOwnerCandidate.dfy'
$wrongCompositeOwnerSource = Join-Path $runPath 'WrongCompositeOwnerCandidate.dfy'
$partialCompositeOwnerSource = Join-Path $runPath 'PartialCompositeOwnerCandidate.dfy'
$safeGenerated = Join-Path $runPath 'Generated.cs'
$callGenerated = Join-Path $runPath 'CallGenerated.cs'
$scalarGenerated = Join-Path $runPath 'ScalarGenerated.cs'
$ownerGenerated = Join-Path $runPath 'OwnerGenerated.cs'
$alternativeOwnerGenerated = Join-Path $runPath 'AlternativeOwnerGenerated.cs'
$compositeOwnerGenerated = Join-Path $runPath 'CompositeOwnerGenerated.cs'
$alternativeCompositeOwnerGenerated = Join-Path $runPath 'AlternativeCompositeOwnerGenerated.cs'
$summaryPath = Join-Path $runPath 'dafny-lowering.json'
Remove-Item -LiteralPath $summaryPath -ErrorAction SilentlyContinue

Push-Location $repoRoot
try {
    $conformanceRun = Invoke-BoundedProcess -Executable 'dotnet' -WorkingDirectory $repoRoot -Arguments @('run', '--project', 'tests/Strogo.Modules.Conformance/Strogo.Modules.Conformance.csproj', '-c', 'Release', '--', '--report', $moduleReport, '--dafny-out', $safeSource, '--dafny-call-out', $callSource, '--dafny-unsafe-out', $unsafeSource, '--dafny-scalar-out', $scalarSource, '--dafny-composite-out', $compositeSource, '--dafny-composite-unsafe-out', $unsafeCompositeSource, '--dafny-owner-out', $ownerSource, '--dafny-owner-weak-out', $weakOwnerSource, '--dafny-owner-alternative-out', $alternativeOwnerSource, '--dafny-owner-wrong-out', $wrongOwnerSource, '--dafny-owner-composite-out', $compositeOwnerSource, '--dafny-owner-composite-alternative-out', $alternativeCompositeOwnerSource, '--dafny-owner-composite-wrong-out', $wrongCompositeOwnerSource, '--dafny-owner-composite-partial-out', $partialCompositeOwnerSource)
    if ($conformanceRun.ExitCode -ne 0) { throw "Modules conformance failed:`n$($conformanceRun.Output)" }

    $safeRun = Invoke-BoundedProcess -Executable $dafny -WorkingDirectory $repoRoot -Arguments @('translate', 'cs', $safeSource, '--include-runtime', '--enforce-determinism', '--cores', '2', '--verification-time-limit', '15', '--output', $safeGenerated)
    $safeVerification = $safeRun.Output.Trim()
    $safeExitCode = $safeRun.ExitCode
    if ($safeExitCode -ne 0 -or $safeVerification -notmatch '(?m)^Dafny program verifier finished with 2 verified, 0 errors\r?$') { throw "Safe selector verification failed:`n$safeVerification" }

    $callRun = Invoke-BoundedProcess -Executable $dafny -WorkingDirectory $repoRoot -Arguments @('translate', 'cs', $callSource, '--include-runtime', '--enforce-determinism', '--cores', '2', '--verification-time-limit', '15', '--output', $callGenerated)
    $callVerification = $callRun.Output.Trim()
    $callExitCode = $callRun.ExitCode
    if ($callExitCode -ne 0 -or $callVerification -notmatch '(?m)^Dafny program verifier finished with 3 verified, 0 errors\r?$') { throw "Safe call verification failed:`n$callVerification" }

    $scalarRun = Invoke-BoundedProcess -Executable $dafny -WorkingDirectory $repoRoot -Arguments @('translate', 'cs', $scalarSource, '--include-runtime', '--enforce-determinism', '--cores', '2', '--verification-time-limit', '15', '--output', $scalarGenerated)
    $scalarVerification = $scalarRun.Output.Trim()
    $scalarExitCode = $scalarRun.ExitCode
    if ($scalarExitCode -ne 0 -or $scalarVerification -notmatch '(?m)^Dafny program verifier finished with 2 verified, 0 errors\r?$') { throw "Safe scalar verification failed:`n$scalarVerification" }

    $compositeRun = Invoke-BoundedProcess -Executable $dafny -WorkingDirectory $repoRoot -Arguments @('verify', $compositeSource, '--enforce-determinism', '--cores', '2', '--verification-time-limit', '15')
    $compositeVerification = $compositeRun.Output.Trim()
    $compositeExitCode = $compositeRun.ExitCode
    if ($compositeExitCode -ne 0 -or $compositeVerification -notmatch '(?m)^Dafny program verifier finished with 4 verified, 0 errors\r?$') { throw "Safe composite verification failed:`n$compositeVerification" }

    $unsafeCompositeRun = Invoke-BoundedProcess -Executable $dafny -WorkingDirectory $repoRoot -Arguments @('verify', $unsafeCompositeSource, '--enforce-determinism', '--cores', '2', '--verification-time-limit', '15')
    $unsafeCompositeVerification = $unsafeCompositeRun.Output.Trim()
    $unsafeCompositeExitCode = $unsafeCompositeRun.ExitCode
    $unsafeCompositeRangeDiagnostics = [regex]::Matches($unsafeCompositeVerification, 'assertion might not hold').Count
    if ($unsafeCompositeExitCode -eq 0 -or $unsafeCompositeRangeDiagnostics -lt 3) {
        throw "Unsafe composite candidate did not fail its capacity/index obligations:`n$unsafeCompositeVerification"
    }

    $unsafeRun = Invoke-BoundedProcess -Executable $dafny -WorkingDirectory $repoRoot -Arguments @('verify', $unsafeSource, '--enforce-determinism', '--cores', '2', '--verification-time-limit', '15')
    $unsafeVerification = $unsafeRun.Output.Trim()
    $unsafeExitCode = $unsafeRun.ExitCode
    if ($unsafeExitCode -eq 0 -or $unsafeVerification -notmatch "might violate newtype constraint for 'I64'") {
        throw "Unsafe arithmetic did not fail with the required range obligation:`n$unsafeVerification"
    }

    $ownerRun = Invoke-BoundedProcess -Executable $dafny -WorkingDirectory $repoRoot -Arguments @('translate', 'cs', $ownerSource, '--include-runtime', '--enforce-determinism', '--cores', '2', '--verification-time-limit', '15', '--output', $ownerGenerated)
    $ownerVerification = $ownerRun.Output.Trim()
    $ownerExitCode = $ownerRun.ExitCode
    if ($ownerExitCode -ne 0 -or $ownerVerification -notmatch '(?m)^Dafny program verifier finished with 4 verified, 0 errors\r?$') { throw "Owner contract verification failed:`n$ownerVerification" }

    $compositeOwnerRun = Invoke-BoundedProcess -Executable $dafny -WorkingDirectory $repoRoot -Arguments @('translate', 'cs', $compositeOwnerSource, '--include-runtime', '--enforce-determinism', '--cores', '2', '--verification-time-limit', '15', '--output', $compositeOwnerGenerated)
    $compositeOwnerVerification = $compositeOwnerRun.Output.Trim()
    $compositeOwnerExitCode = $compositeOwnerRun.ExitCode
    if ($compositeOwnerExitCode -ne 0 -or $compositeOwnerVerification -notmatch '(?m)^Dafny program verifier finished with 5 verified, 0 errors\r?$') { throw "Composite owner contract verification failed:`n$compositeOwnerVerification" }

    $alternativeCompositeOwnerRun = Invoke-BoundedProcess -Executable $dafny -WorkingDirectory $repoRoot -Arguments @('translate', 'cs', $alternativeCompositeOwnerSource, '--include-runtime', '--enforce-determinism', '--cores', '2', '--verification-time-limit', '15', '--output', $alternativeCompositeOwnerGenerated)
    $alternativeCompositeOwnerVerification = $alternativeCompositeOwnerRun.Output.Trim()
    $alternativeCompositeOwnerExitCode = $alternativeCompositeOwnerRun.ExitCode
    if ($alternativeCompositeOwnerExitCode -ne 0 -or $alternativeCompositeOwnerVerification -notmatch '(?m)^Dafny program verifier finished with 5 verified, 0 errors\r?$') { throw "Alternative composite owner verification failed:`n$alternativeCompositeOwnerVerification" }

    $wrongCompositeOwnerRun = Invoke-BoundedProcess -Executable $dafny -WorkingDirectory $repoRoot -Arguments @('verify', $wrongCompositeOwnerSource, '--enforce-determinism', '--cores', '2', '--verification-time-limit', '15')
    $wrongCompositeOwnerVerification = $wrongCompositeOwnerRun.Output.Trim()
    $wrongCompositeOwnerExitCode = $wrongCompositeOwnerRun.ExitCode
    if ($wrongCompositeOwnerExitCode -eq 0 -or $wrongCompositeOwnerVerification -notmatch 'a postcondition could not be proved') { throw "Wrong composite owner implementation did not fail exact outcome:`n$wrongCompositeOwnerVerification" }

    $partialCompositeOwnerRun = Invoke-BoundedProcess -Executable $dafny -WorkingDirectory $repoRoot -Arguments @('verify', $partialCompositeOwnerSource, '--enforce-determinism', '--cores', '2', '--verification-time-limit', '15')
    $partialCompositeOwnerVerification = $partialCompositeOwnerRun.Output.Trim()
    $partialCompositeOwnerExitCode = $partialCompositeOwnerRun.ExitCode
    if ($partialCompositeOwnerExitCode -eq 0 -or $partialCompositeOwnerVerification -notmatch 'index out of range') { throw "Partial composite owner model did not fail its range obligation:`n$partialCompositeOwnerVerification" }

    $weakOwnerRun = Invoke-BoundedProcess -Executable $dafny -WorkingDirectory $repoRoot -Arguments @('verify', $weakOwnerSource, '--enforce-determinism', '--cores', '2', '--verification-time-limit', '15')
    $weakOwnerVerification = $weakOwnerRun.Output.Trim()
    $weakOwnerExitCode = $weakOwnerRun.ExitCode
    $weakOwnerRangeDiagnostics = [regex]::Matches($weakOwnerVerification, "result of operation might violate newtype constraint for 'I64'").Count
    if ($weakOwnerExitCode -eq 0 -or $weakOwnerRangeDiagnostics -lt 2) {
        throw "Weak owner precondition did not fail both model and candidate range obligations:`n$weakOwnerVerification"
    }

    $alternativeOwnerRun = Invoke-BoundedProcess -Executable $dafny -WorkingDirectory $repoRoot -Arguments @('translate', 'cs', $alternativeOwnerSource, '--include-runtime', '--enforce-determinism', '--cores', '2', '--verification-time-limit', '15', '--output', $alternativeOwnerGenerated)
    $alternativeOwnerVerification = $alternativeOwnerRun.Output.Trim()
    $alternativeOwnerExitCode = $alternativeOwnerRun.ExitCode
    if ($alternativeOwnerExitCode -ne 0 -or $alternativeOwnerVerification -notmatch '(?m)^Dafny program verifier finished with 4 verified, 0 errors\r?$') { throw "Alternative owner implementation verification failed:`n$alternativeOwnerVerification" }

    $wrongOwnerRun = Invoke-BoundedProcess -Executable $dafny -WorkingDirectory $repoRoot -Arguments @('verify', $wrongOwnerSource, '--enforce-determinism', '--cores', '2', '--verification-time-limit', '15')
    $wrongOwnerVerification = $wrongOwnerRun.Output.Trim()
    $wrongOwnerExitCode = $wrongOwnerRun.ExitCode
    if ($wrongOwnerExitCode -eq 0 -or $wrongOwnerVerification -notmatch 'a postcondition could not be proved') {
        throw "Wrong owner implementation did not fail its exact outcome obligation:`n$wrongOwnerVerification"
    }

    $generatedProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Library</OutputType>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AssemblyName>Strogo.Generated.SafeSelect</AssemblyName>
    <Nullable>disable</Nullable>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup><Compile Include="Generated.cs" /></ItemGroup>
</Project>
'@
    $consumerProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
    <ProjectReference Include="Generated.csproj" />
  </ItemGroup>
</Project>
'@
    $consumerProgram = @'
using System.Reflection.PortableExecutable;

var firstValue = Candidate.__default.F000(true, long.MaxValue, 11L);
var secondValue = Candidate.__default.F000(true, 2L, 11L);
var thirdValue = Candidate.__default.F000(false, long.MaxValue, 11L);
using var stream = File.OpenRead(typeof(Candidate.__default).Assembly.Location);
using var pe = new PEReader(stream);
var nativeHeaderSize = pe.PEHeaders.CorHeader?.ManagedNativeHeaderDirectory.Size ?? 0;
Console.WriteLine($"outcomes={firstValue},{secondValue},{thirdValue};nativeHeader={nativeHeaderSize}");
return firstValue == long.MaxValue && secondValue == 3L && thirdValue == 11L && nativeHeaderSize > 0 ? 0 : 1;
'@
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText((Join-Path $runPath 'Generated.csproj'), $generatedProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'Consumer.csproj'), $consumerProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'Program.cs'), $consumerProgram, $utf8)

    $scalarGeneratedProject = $generatedProject.Replace('Strogo.Generated.SafeSelect', 'Strogo.Generated.Scalar').Replace('Generated.cs', 'ScalarGenerated.cs')
    $scalarConsumerProject = $consumerProject.Replace('Program.cs', 'ScalarProgram.cs').Replace('Generated.csproj', 'ScalarGenerated.csproj')
    $scalarConsumerProgram = @'
var firstValue = Candidate.__default.F000(false, false);
var secondValue = Candidate.__default.F000(true, false);
var thirdValue = Candidate.__default.F000(true, true);
Console.WriteLine($"scalarOutcomes={firstValue},{secondValue},{thirdValue}");
return firstValue && !secondValue && thirdValue ? 0 : 1;
'@
    [IO.File]::WriteAllText((Join-Path $runPath 'ScalarGenerated.csproj'), $scalarGeneratedProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'ScalarConsumer.csproj'), $scalarConsumerProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'ScalarProgram.cs'), $scalarConsumerProgram, $utf8)

    $callGeneratedProject = $generatedProject.Replace('Strogo.Generated.SafeSelect', 'Strogo.Generated.SafeCall').Replace('Generated.cs', 'CallGenerated.cs')
    $callConsumerProject = $consumerProject.Replace('Program.cs', 'CallProgram.cs').Replace('Generated.csproj', 'CallGenerated.csproj')
    $callConsumerProgram = @'
var value = Candidate.__default.F001(7L);
Console.WriteLine($"callOutcome={value}");
return value == 7L ? 0 : 1;
'@
    [IO.File]::WriteAllText((Join-Path $runPath 'CallGenerated.csproj'), $callGeneratedProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'CallConsumer.csproj'), $callConsumerProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'CallProgram.cs'), $callConsumerProgram, $utf8)

    $ownerGeneratedProject = $generatedProject.Replace('Strogo.Generated.SafeSelect', 'Strogo.Generated.OwnerContract').Replace('Generated.cs', 'OwnerGenerated.cs')
    $ownerConsumerProject = $consumerProject.Replace('Program.cs', 'OwnerProgram.cs').Replace('Generated.csproj', 'OwnerGenerated.csproj')
    $ownerConsumerProgram = @'
var minimum = Candidate.__default.F000(long.MinValue);
var ordinary = Candidate.__default.F000(41L);
var maximum = Candidate.__default.F000(long.MaxValue - 1L);
Console.WriteLine($"ownerOutcomes={minimum},{ordinary},{maximum}");
return minimum == long.MinValue + 1L && ordinary == 42L && maximum == long.MaxValue ? 0 : 1;
'@
    [IO.File]::WriteAllText((Join-Path $runPath 'OwnerGenerated.csproj'), $ownerGeneratedProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'OwnerConsumer.csproj'), $ownerConsumerProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'OwnerProgram.cs'), $ownerConsumerProgram, $utf8)

    $compositeOwnerGeneratedProject = $generatedProject.Replace('Strogo.Generated.SafeSelect', 'Strogo.Generated.CompositeOwnerContract').Replace('Generated.cs', 'CompositeOwnerGenerated.cs')
    $compositeOwnerConsumerProject = $consumerProject.Replace('Program.cs', 'CompositeOwnerProgram.cs').Replace('Generated.csproj', 'CompositeOwnerGenerated.csproj')
    $compositeOwnerConsumerProgram = @'
var zero = Candidate.__default.F000(0L);
var fortyOne = Candidate.__default.F000(41L);
Console.WriteLine($"compositeOwnerOutcomes={zero};{fortyOne}");
return zero.dtor_R000F000 == 2L && zero.dtor_R000F001.LongCount == 2L
    && fortyOne.dtor_R000F000 == 2L && fortyOne.dtor_R000F001.LongCount == 2L ? 0 : 1;
'@
    [IO.File]::WriteAllText((Join-Path $runPath 'CompositeOwnerGenerated.csproj'), $compositeOwnerGeneratedProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'CompositeOwnerConsumer.csproj'), $compositeOwnerConsumerProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'CompositeOwnerProgram.cs'), $compositeOwnerConsumerProgram, $utf8)

    $alternativeCompositeOwnerGeneratedProject = $generatedProject.Replace('Strogo.Generated.SafeSelect', 'Strogo.Generated.AlternativeCompositeOwnerContract').Replace('Generated.cs', 'AlternativeCompositeOwnerGenerated.cs')
    $alternativeCompositeOwnerConsumerProject = $consumerProject.Replace('Program.cs', 'AlternativeCompositeOwnerProgram.cs').Replace('Generated.csproj', 'AlternativeCompositeOwnerGenerated.csproj')
    [IO.File]::WriteAllText((Join-Path $runPath 'AlternativeCompositeOwnerGenerated.csproj'), $alternativeCompositeOwnerGeneratedProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'AlternativeCompositeOwnerConsumer.csproj'), $alternativeCompositeOwnerConsumerProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'AlternativeCompositeOwnerProgram.cs'), $compositeOwnerConsumerProgram, $utf8)

    $alternativeOwnerGeneratedProject = $generatedProject.Replace('Strogo.Generated.SafeSelect', 'Strogo.Generated.AlternativeOwnerContract').Replace('Generated.cs', 'AlternativeOwnerGenerated.cs')
    $alternativeOwnerConsumerProject = $consumerProject.Replace('Program.cs', 'AlternativeOwnerProgram.cs').Replace('Generated.csproj', 'AlternativeOwnerGenerated.csproj')
    [IO.File]::WriteAllText((Join-Path $runPath 'AlternativeOwnerGenerated.csproj'), $alternativeOwnerGeneratedProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'AlternativeOwnerConsumer.csproj'), $alternativeOwnerConsumerProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'AlternativeOwnerProgram.cs'), $ownerConsumerProgram, $utf8)

    $publishPath = Join-Path $runPath 'publish'
    $publishRun = Invoke-BoundedProcess -Executable 'dotnet' -WorkingDirectory $repoRoot -Arguments @('publish', (Join-Path $runPath 'Consumer.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', '-p:PublishReadyToRun=true', '-o', $publishPath)
    if ($publishRun.ExitCode -ne 0) { throw "ReadyToRun publish failed:`n$($publishRun.Output)" }
    $consumerRun = Invoke-BoundedProcess -Executable (Join-Path $publishPath 'Consumer.exe') -WorkingDirectory $repoRoot -Arguments @()
    $consumerOutput = $consumerRun.Output.Trim()
    if ($consumerRun.ExitCode -ne 0 -or $consumerOutput -notmatch '^outcomes=9223372036854775807,3,11;nativeHeader=([1-9][0-9]*)$') { throw "Generated consumer failed:`n$consumerOutput" }
    $nativeHeaderSize = [int]$Matches[1]

    $scalarConsumerRun = Invoke-BoundedProcess -Executable 'dotnet' -WorkingDirectory $repoRoot -Arguments @('run', '--project', (Join-Path $runPath 'ScalarConsumer.csproj'), '-c', 'Release')
    $scalarConsumerOutput = $scalarConsumerRun.Output.Trim()
    if ($scalarConsumerRun.ExitCode -ne 0 -or $scalarConsumerOutput -notmatch '(?m)^scalarOutcomes=True,False,True\r?$') { throw "Generated scalar consumer failed:`n$scalarConsumerOutput" }
    $scalarOutcomeLine = $Matches[0].Trim()

    $callConsumerRun = Invoke-BoundedProcess -Executable 'dotnet' -WorkingDirectory $repoRoot -Arguments @('run', '--project', (Join-Path $runPath 'CallConsumer.csproj'), '-c', 'Release')
    $callConsumerOutput = $callConsumerRun.Output.Trim()
    if ($callConsumerRun.ExitCode -ne 0 -or $callConsumerOutput -notmatch '(?m)^callOutcome=7\r?$') { throw "Generated call consumer failed:`n$callConsumerOutput" }
    $callOutcomeLine = $Matches[0].Trim()

    $ownerConsumerRun = Invoke-BoundedProcess -Executable 'dotnet' -WorkingDirectory $repoRoot -Arguments @('run', '--project', (Join-Path $runPath 'OwnerConsumer.csproj'), '-c', 'Release')
    $ownerConsumerOutput = $ownerConsumerRun.Output.Trim()
    if ($ownerConsumerRun.ExitCode -ne 0 -or $ownerConsumerOutput -notmatch '(?m)^ownerOutcomes=-9223372036854775807,42,9223372036854775807\r?$') { throw "Generated owner-contract consumer failed:`n$ownerConsumerOutput" }
    $ownerOutcomeLine = $Matches[0].Trim()

    $compositeOwnerConsumerRun = Invoke-BoundedProcess -Executable 'dotnet' -WorkingDirectory $repoRoot -Arguments @('run', '--project', (Join-Path $runPath 'CompositeOwnerConsumer.csproj'), '-c', 'Release')
    $compositeOwnerConsumerOutput = $compositeOwnerConsumerRun.Output.Trim()
    if ($compositeOwnerConsumerRun.ExitCode -ne 0 -or $compositeOwnerConsumerOutput -notmatch '(?m)^compositeOwnerOutcomes=Candidate\.R000\.C000\(2, \[0, 1\]\);Candidate\.R000\.C000\(2, \[41, 42\]\)\r?$') { throw "Generated composite owner consumer failed:`n$compositeOwnerConsumerOutput" }
    $compositeOwnerOutcomeLine = $Matches[0].Trim()

    $alternativeCompositeOwnerConsumerRun = Invoke-BoundedProcess -Executable 'dotnet' -WorkingDirectory $repoRoot -Arguments @('run', '--project', (Join-Path $runPath 'AlternativeCompositeOwnerConsumer.csproj'), '-c', 'Release')
    $alternativeCompositeOwnerConsumerOutput = $alternativeCompositeOwnerConsumerRun.Output.Trim()
    if ($alternativeCompositeOwnerConsumerRun.ExitCode -ne 0 -or $alternativeCompositeOwnerConsumerOutput -notmatch '(?m)^compositeOwnerOutcomes=Candidate\.R000\.C000\(2, \[0, 1\]\);Candidate\.R000\.C000\(2, \[41, 42\]\)\r?$') { throw "Generated alternative composite owner consumer failed:`n$alternativeCompositeOwnerConsumerOutput" }
    $alternativeCompositeOwnerOutcomeLine = $Matches[0].Trim()

    $alternativeOwnerConsumerRun = Invoke-BoundedProcess -Executable 'dotnet' -WorkingDirectory $repoRoot -Arguments @('run', '--project', (Join-Path $runPath 'AlternativeOwnerConsumer.csproj'), '-c', 'Release')
    $alternativeOwnerConsumerOutput = $alternativeOwnerConsumerRun.Output.Trim()
    if ($alternativeOwnerConsumerRun.ExitCode -ne 0 -or $alternativeOwnerConsumerOutput -notmatch '(?m)^ownerOutcomes=-9223372036854775807,42,9223372036854775807\r?$') { throw "Generated alternative owner-contract consumer failed:`n$alternativeOwnerConsumerOutput" }
    $alternativeOwnerOutcomeLine = $Matches[0].Trim()

    $generatedDll = Join-Path $publishPath 'Strogo.Generated.SafeSelect.dll'
    $dllInfo = Get-Item -LiteralPath $generatedDll
    $summary = [ordered]@{
        schemaVersion = 'strogo.modules-dafny-lowering.validation.v1'
        generatedAtUtc = [DateTime]::UtcNow.ToString('o')
        dafnyTool = [ordered]@{
            version = $dafnyToolEvidence.version
            archiveSha256 = $dafnyToolEvidence.archiveSha256
            executableSha256 = $dafnyToolEvidence.executableSha256
        }
        processBoundary = [ordered]@{
            timeoutMilliseconds = $processTimeoutMilliseconds
            outputByteLimit = $processOutputByteLimit
            killTree = $true
            timeoutSelfTest = $timeoutSelfTestFailure.Code
            outputLimitSelfTest = $outputLimitSelfTestFailure.Code
            orphanedChildSelfTest = [ordered]@{
                failure = $orphanSelfTestFailure.Code
                childPid = $orphanChildPid
                childAliveAfterGrace = $false
            }
        }
        moduleReport = [ordered]@{
            path = 'modules-v0.2.json'
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $moduleReport).Hash.ToLowerInvariant()
        }
        safeSelector = [ordered]@{
            exitCode = $safeExitCode
            verification = $safeVerification
            generatedMethod = 'Candidate.__default.F000(bool,long,long):long'
            consumer = $consumerOutput
        }
        safeLocalCall = [ordered]@{
            exitCode = $callExitCode
            verification = $callVerification
            generatedMethods = @('Candidate.__default.F000(long):long', 'Candidate.__default.F001(long):long')
            consumer = $callOutcomeLine
        }
        safeScalarOperators = [ordered]@{
            exitCode = $scalarExitCode
            verification = $scalarVerification
            generatedMethod = 'Candidate.__default.F000(bool,bool):bool'
            consumer = $scalarOutcomeLine
        }
        safeCompositeValues = [ordered]@{
            status = 'Verified'
            exitCode = $compositeExitCode
            verification = $compositeVerification
            source = 'CompositeCandidate.dfy'
            sourceSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $compositeSource).Hash.ToLowerInvariant()
            runtimeScope = 'reference evaluator only; generated composite consumer remains pending'
        }
        unsafeCompositeWithoutRangeContract = [ordered]@{
            status = 'Unproven'
            exitCode = $unsafeCompositeExitCode
            expectedAssertionDiagnostics = 3
            actualAssertionDiagnostics = $unsafeCompositeRangeDiagnostics
            verification = $unsafeCompositeVerification
            source = 'UnsafeCompositeCandidate.dfy'
        }
        missingOwnerPrecondition = [ordered]@{
            status = 'Unproven'
            exitCode = $unsafeExitCode
            expectedDiagnostic = "result of operation might violate newtype constraint for 'I64'"
            verification = $unsafeVerification
        }
        ownerContract = [ordered]@{
            status = 'Verified'
            exitCode = $ownerExitCode
            verification = $ownerVerification
            generatedMethod = 'Candidate.__default.F000(long):long'
            consumer = $ownerOutcomeLine
        }
        compositeOwnerContract = [ordered]@{
            status = 'Verified'
            exitCode = $compositeOwnerExitCode
            verification = $compositeOwnerVerification
            generatedMethod = 'Candidate.__default.F000(long):Candidate._IR000'
            consumer = $compositeOwnerOutcomeLine
        }
        alternativeCompositeOwnerImplementation = [ordered]@{
            status = 'Verified'
            exitCode = $alternativeCompositeOwnerExitCode
            verification = $alternativeCompositeOwnerVerification
            generatedMethod = 'Candidate.__default.F000(long):Candidate._IR000'
            consumer = $alternativeCompositeOwnerOutcomeLine
        }
        wrongCompositeOwnerImplementation = [ordered]@{
            status = 'Counterexample'
            replayedWitness = 'empty-shape-v1'
            exitCode = $wrongCompositeOwnerExitCode
            expectedDiagnostic = 'a postcondition could not be proved'
            verification = $wrongCompositeOwnerVerification
        }
        partialCompositeOwnerModel = [ordered]@{
            status = 'Unproven'
            exitCode = $partialCompositeOwnerExitCode
            expectedDiagnostic = 'index out of range'
            verification = $partialCompositeOwnerVerification
        }
        weakOwnerContract = [ordered]@{
            status = 'Unproven'
            exitCode = $weakOwnerExitCode
            expectedRangeDiagnostics = 2
            actualRangeDiagnostics = $weakOwnerRangeDiagnostics
            verification = $weakOwnerVerification
        }
        alternativeOwnerImplementation = [ordered]@{
            status = 'Verified'
            exitCode = $alternativeOwnerExitCode
            verification = $alternativeOwnerVerification
            generatedMethod = 'Candidate.__default.F000(long):long'
            consumer = $alternativeOwnerOutcomeLine
        }
        wrongOwnerImplementation = [ordered]@{
            status = 'Unproven'
            exitCode = $wrongOwnerExitCode
            expectedDiagnostic = 'a postcondition could not be proved'
            verification = $wrongOwnerVerification
        }
        readyToRun = [ordered]@{
            runtimeIdentifier = 'win-x64'
            assembly = 'Strogo.Generated.SafeSelect.dll'
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $generatedDll).Hash.ToLowerInvariant()
            bytes = $dllInfo.Length
            nativeHeaderSize = $nativeHeaderSize
        }
    }
    [IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 8), $utf8)
    Write-Host "PASS modules Dafny lowering; $consumerOutput"
    Write-Host "Evidence: $summaryPath"
}
finally {
    Pop-Location
}
