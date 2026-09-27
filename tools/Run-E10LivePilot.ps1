[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,
    [string] $DotNetPath,
    [string] $CodexNodePath,
    [string] $CodexEntryPath,
    [switch] $PreflightOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not ('E10LimitedMemoryStream' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public sealed class E10LimitedMemoryStream : MemoryStream
{
    private readonly long limit;
    public E10LimitedMemoryStream(long limit) { this.limit = limit; }
    private void Check(int count) { if (Length + count > limit) throw new IOException("OutputLimitExceeded"); }
    public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) { Check(count); return base.WriteAsync(buffer, offset, count, token); }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token) { Check(buffer.Length); return base.WriteAsync(buffer, token); }
}
'@
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$expectedDotNetPath = [IO.Path]::GetFullPath((Join-Path $repoRoot '.tools/dotnet-sdk-10.0.400/dotnet.exe'))
if (-not $DotNetPath) { $DotNetPath = $expectedDotNetPath }
if (-not $CodexNodePath) { $CodexNodePath = (Get-Command node.exe -ErrorAction Stop).Source }
if (-not $CodexEntryPath) { $CodexEntryPath = Join-Path $env:APPDATA 'npm/node_modules/@openai/codex/bin/codex.js' }
$DotNetPath = (Resolve-Path $DotNetPath).Path
if (-not $DotNetPath.Equals($expectedDotNetPath, [StringComparison]::OrdinalIgnoreCase)) { throw 'E10 requires the repository-local .tools/dotnet-sdk-10.0.400 SDK' }
$CodexNodePath = (Resolve-Path $CodexNodePath).Path
$CodexEntryPath = (Resolve-Path $CodexEntryPath).Path
$cliDll = (Resolve-Path (Join-Path $repoRoot 'src/Strogo.ExperimentCli/bin/Release/net10.0/Strogo.ExperimentCli.dll')).Path
$fixtureRoot = (Resolve-Path (Join-Path $repoRoot 'fixtures/e10-live-pilot')).Path
$image = 'mcr.microsoft.com/dotnet/sdk@sha256:1aabdb4843de1c426d3676bf1220bc040e540f82a765320b3eb2c693e8d0a7dd'
$environmentNames = @('APPDATA', 'CODEX_HOME', 'ComSpec', 'LOCALAPPDATA', 'PATH', 'SystemRoot', 'TEMP', 'TMP', 'USERPROFILE')
$originalCodexHome = [Environment]::GetEnvironmentVariable('CODEX_HOME')
$isolatedCodexHome = $null

function Invoke-Checked {
    param([string] $File, [string[]] $Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code ${LASTEXITCODE}: $File" }
}

function Get-Sha256File {
    param([string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-ImplementationIdentity {
    param([object] $PilotManifest)
    $binRoot = Split-Path -Parent $cliDll
    if ((Get-Sha256File $DotNetPath) -ne $PilotManifest.implementationIdentity.dotNetExecutableDigest -or
        (Get-Sha256File $CodexNodePath) -ne $PilotManifest.implementationIdentity.codexNodeDigest -or
        (Get-Sha256File $CodexEntryPath) -ne $PilotManifest.implementationIdentity.codexEntryDigest -or
        (Get-Sha256File $cliDll) -ne $PilotManifest.implementationIdentity.experimentCliAssemblyDigest -or
        (Get-Sha256File (Join-Path $binRoot 'Strogo.Experiments.dll')) -ne $PilotManifest.implementationIdentity.experimentsAssemblyDigest -or
        (Get-Sha256File (Join-Path $binRoot 'Strogo.Notation.dll')) -ne $PilotManifest.implementationIdentity.notationAssemblyDigest -or
        (Get-Sha256File (Join-Path $binRoot 'Kernel.Core.dll')) -ne $PilotManifest.implementationIdentity.coreAssemblyDigest) {
        throw 'Implementation executable identity changed after preparation'
    }
}

function Invoke-ImplementationIdentityCheck {
    Invoke-Checked $DotNetPath @(
        $cliDll, 'pilot', 'identity-check', '--directory', $outputRoot,
        '--repository-root', $repoRoot, '--dotnet', $DotNetPath, '--dotnet-version', '10.0.400',
        '--codex-node', $CodexNodePath, '--codex-entry', $CodexEntryPath
    )
}

function Set-EvidenceAcl {
    param([string] $Path)
    $currentSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    $systemSid = [System.Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $security = [System.Security.AccessControl.DirectorySecurity]::new()
    $security.SetAccessRuleProtection($true, $false)
    $inheritance = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
    $propagation = [System.Security.AccessControl.PropagationFlags]::None
    $rights = [System.Security.AccessControl.FileSystemRights]::FullControl
    $allow = [System.Security.AccessControl.AccessControlType]::Allow
    $security.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($currentSid, $rights, $inheritance, $propagation, $allow))
    $security.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($systemSid, $rights, $inheritance, $propagation, $allow))
    [System.IO.FileSystemAclExtensions]::SetAccessControl((Get-Item -LiteralPath $Path), $security)
    $actual = [System.IO.FileSystemAclExtensions]::GetAccessControl((Get-Item -LiteralPath $Path))
    if (-not $actual.AreAccessRulesProtected) { throw 'Evidence ACL inheritance remains enabled' }
    return $currentSid.Value
}

function Start-CapturedProcess {
    param(
        [string] $File,
        [string[]] $Arguments,
        [string] $WorkingDirectory,
        [AllowNull()][string] $StandardInput,
        [int] $TimeoutSeconds,
        [AllowNull()][string] $AdditionalOutputPath
    )
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $File
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.RedirectStandardInput = $null -ne $StandardInput
    foreach ($argument in $Arguments) { [void]$info.ArgumentList.Add($argument) }
    $info.Environment.Clear()
    foreach ($name in $environmentNames) {
        $value = [System.Environment]::GetEnvironmentVariable($name)
        if ($name -eq 'CODEX_HOME' -and [string]::IsNullOrWhiteSpace($value)) { $value = Join-Path $env:USERPROFILE '.codex' }
        if (-not [string]::IsNullOrWhiteSpace($value)) { $info.Environment[$name] = $value }
    }
    $stdoutFile = [E10LimitedMemoryStream]::new(16MB)
    $stderrFile = [E10LimitedMemoryStream]::new(16MB)
    $startedAt = [DateTimeOffset]::UtcNow
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $info
    if (-not $process.Start()) { throw "Could not start $File" }
    $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
    $stderrTask = $process.StandardError.BaseStream.CopyToAsync($stderrFile)
    if ($null -ne $StandardInput) { $process.StandardInput.Write($StandardInput); $process.StandardInput.Close() }
    $timedOut = $false
    $outputLimitExceeded = $false
    while (-not $process.HasExited) {
        if ($stdoutTask.IsFaulted -or $stderrTask.IsFaulted -or ($null -ne $AdditionalOutputPath -and (Test-Path -LiteralPath $AdditionalOutputPath) -and (Get-Item -LiteralPath $AdditionalOutputPath).Length -gt 16MB)) { $outputLimitExceeded = $true; break }
        if ($timer.Elapsed -gt [TimeSpan]::FromSeconds($TimeoutSeconds)) { $timedOut = $true; break }
        Start-Sleep -Milliseconds 25
    }
    if ($timedOut -or $outputLimitExceeded) {
        try { $process.Kill($true) } catch { }
    }
    $process.WaitForExit()
    if ($stdoutTask.IsFaulted -or $stderrTask.IsFaulted) { $outputLimitExceeded = $true }
    try { [System.Threading.Tasks.Task]::WaitAll(@($stdoutTask, $stderrTask)) } catch { if (-not $outputLimitExceeded) { throw } }
    $stdout = [Text.Encoding]::UTF8.GetString($stdoutFile.ToArray())
    $stderr = [Text.Encoding]::UTF8.GetString($stderrFile.ToArray())
    $stdoutFile.Dispose(); $stderrFile.Dispose()
    $timer.Stop()
    $endedAt = [DateTimeOffset]::UtcNow
    $exitCode = if ($timedOut -or $outputLimitExceeded) { -1 } else { $process.ExitCode }
    $process.Dispose()
    return [pscustomobject]@{
        ExitCode = $exitCode
        TimedOut = $timedOut
        OutputLimitExceeded = $outputLimitExceeded
        Stdout = $stdout
        Stderr = $stderr
        StartedAt = $startedAt.ToString('O')
        EndedAt = $endedAt.ToString('O')
        WallMilliseconds = $timer.ElapsedMilliseconds
    }
}

Push-Location $repoRoot
try {
    $gitStatus = (& git status --porcelain=v1 --untracked-files=all) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect repository worktree' }
    if (-not [string]::IsNullOrWhiteSpace($gitStatus)) { throw "E10 requires a clean committed worktree.`n$gitStatus" }
    if (& $DotNetPath --version 2>$null | Where-Object { $_ -ne '10.0.400' }) { throw 'Exact .NET SDK 10.0.400 is required' }
    if ($LASTEXITCODE -ne 0) { throw 'Exact .NET SDK 10.0.400 is unavailable' }
    $codexVersion = (& $CodexNodePath $CodexEntryPath --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $codexVersion -ne 'codex-cli 0.154.0') { throw "Unexpected Codex CLI: $codexVersion" }
    $dockerImageId = (docker image inspect --format '{{.Id}}' $image).Trim()
    if ($LASTEXITCODE -ne 0 -or $dockerImageId -ne 'sha256:1aabdb4843de1c426d3676bf1220bc040e540f82a765320b3eb2c693e8d0a7dd') { throw 'Pinned Docker image identity mismatch' }
    $commit = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not resolve repository commit' }
    Invoke-Checked $DotNetPath @('restore', (Join-Path $repoRoot 'Kernel.slnx'), '--locked-mode')
    Invoke-Checked $DotNetPath @('build', (Join-Path $repoRoot 'Kernel.slnx'), '-c', 'Release', '--no-restore')
    $postBuildStatus = (& git status --porcelain=v1 --untracked-files=all) -join "`n"
    if (-not [string]::IsNullOrWhiteSpace($postBuildStatus)) { throw "Repository changed during exact build.`n$postBuildStatus" }

    $outputRoot = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)
    $repoBoundary = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if ($outputRoot.Equals($repoBoundary, [StringComparison]::OrdinalIgnoreCase) -or $outputRoot.StartsWith($repoBoundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputDirectory must be outside the repository tree'
    }
    if (Test-Path -LiteralPath $outputRoot) {
        if (Get-ChildItem -LiteralPath $outputRoot -Force | Select-Object -First 1) { throw 'OutputDirectory must be absent or empty' }
    } else { New-Item -ItemType Directory -Path $outputRoot | Out-Null }
    [void](Set-EvidenceAcl -Path $outputRoot)
    $sourceCodexHome = if ([string]::IsNullOrWhiteSpace($originalCodexHome)) { Join-Path $env:USERPROFILE '.codex' } else { $originalCodexHome }
    $isolatedCodexHome = Join-Path ([IO.Path]::GetTempPath()) ("strogo-e10-codexhome-{0}" -f [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $isolatedCodexHome | Out-Null
    [void](Set-EvidenceAcl -Path $isolatedCodexHome)
    $authSource = Join-Path $sourceCodexHome 'auth.json'
    if (-not (Test-Path -LiteralPath $authSource)) { throw 'Codex auth.json is required for isolated live execution' }
    Copy-Item -LiteralPath $authSource -Destination (Join-Path $isolatedCodexHome 'auth.json')
    $env:CODEX_HOME = $isolatedCodexHome
    $pilotId = 'e10-{0}-{1}' -f ([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')), ([guid]::NewGuid().ToString('N').Substring(0, 8))
    $createdAt = [DateTimeOffset]::UtcNow.ToString('O')
    Invoke-Checked $DotNetPath @($cliDll, 'pilot', 'prepare', '--directory', $outputRoot, '--fixtures', $fixtureRoot, '--pilot-id', $pilotId, '--created-at', $createdAt, '--commit', $commit, '--cli-version', $codexVersion, '--docker-image-id', $dockerImageId, '--repository-root', $repoRoot, '--dotnet', $DotNetPath, '--dotnet-version', '10.0.400', '--codex-node', $CodexNodePath, '--codex-entry', $CodexEntryPath)
    $pilotManifest = Get-Content -LiteralPath (Join-Path $outputRoot 'pilot-manifest.json') -Raw -Encoding utf8 | ConvertFrom-Json
    Assert-ImplementationIdentity $pilotManifest
    Invoke-ImplementationIdentityCheck

    foreach ($runId in @('S1', 'C1', 'C2', 'S2')) {
        Assert-ImplementationIdentity $pilotManifest
        Invoke-ImplementationIdentityCheck
        $raw = Join-Path $outputRoot "runs/$runId"
        $session = Join-Path $outputRoot "sessions/$runId"
        New-Item -ItemType Directory -Path $raw,$session -Force | Out-Null
        $prompt = Get-Content -LiteralPath (Join-Path $outputRoot "packages/$runId/prompt.txt") -Raw -Encoding utf8
        $debugArgs = @($CodexEntryPath, 'debug', 'prompt-input', '-c', 'project_doc_max_bytes=0', '-c', 'sandbox_mode="read-only"', '-c', 'shell_environment_policy.inherit=none', '-c', 'model="gpt-6-astra"', '-c', 'model_reasoning_effort="medium"', $prompt)
        $debug = Start-CapturedProcess -File $CodexNodePath -Arguments $debugArgs -WorkingDirectory $session -StandardInput $null -TimeoutSeconds 120 -AdditionalOutputPath $null
        [IO.File]::WriteAllText((Join-Path $raw 'prompt-input.txt'), $debug.Stdout, [Text.UTF8Encoding]::new($false))
        if ($debug.ExitCode -ne 0 -or $debug.TimedOut) { throw "prompt-input preflight failed for $runId`: $($debug.Stderr)" }
    }
    Invoke-Checked $DotNetPath @($cliDll, 'pilot', 'prompt-check', '--directory', $outputRoot)
    if ($PreflightOnly) {
        Write-Output "E10 preflight completed without live model calls: $outputRoot"
        return
    }

    foreach ($runId in @('S1', 'C1', 'C2', 'S2')) {
        Assert-ImplementationIdentity $pilotManifest
        Invoke-ImplementationIdentityCheck
        $raw = Join-Path $outputRoot "runs/$runId"
        $session = Join-Path $outputRoot "sessions/$runId"
        $package = Join-Path $outputRoot "packages/$runId"
        $prompt = Get-Content -LiteralPath (Join-Path $package 'prompt.txt') -Raw -Encoding utf8
        $schema = Join-Path $package 'response.schema.json'
        $response = Join-Path $raw 'response.json'
        $execArgs = @(
            $CodexEntryPath, 'exec', '--ephemeral', '--ignore-user-config', '--ignore-rules', '--skip-git-repo-check',
            '--sandbox', 'read-only', '--output-schema', $schema, '--json', '--output-last-message', $response,
            '--model', 'gpt-6-astra', '--cd', $session,
            '-c', 'project_doc_max_bytes=0', '-c', 'shell_environment_policy.inherit=none', '-c', 'model_reasoning_effort="medium"', '-'
        )
        $runManifest = Get-Content -LiteralPath (Join-Path $package 'run-manifest.json') -Raw -Encoding utf8 | ConvertFrom-Json
        $actualInvocationArgs = @($execArgs[1..($execArgs.Length - 1)])
        $expectedInvocationArgs = @($runManifest.invocationIdentity.arguments)
        if (($actualInvocationArgs | ConvertTo-Json -Compress) -ne ($expectedInvocationArgs | ConvertTo-Json -Compress)) { throw "Invocation identity mismatch for $runId" }
        $run = Start-CapturedProcess -File $CodexNodePath -Arguments $execArgs -WorkingDirectory $session -StandardInput $prompt -TimeoutSeconds 1200 -AdditionalOutputPath $response
        [IO.File]::WriteAllText((Join-Path $raw 'stdout.jsonl'), $run.Stdout, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $raw 'stderr.txt'), $run.Stderr, [Text.UTF8Encoding]::new($false))
        if (-not (Test-Path -LiteralPath $response)) { [IO.File]::WriteAllText($response, '', [Text.UTF8Encoding]::new($false)) }
        if ((Get-Item -LiteralPath $response).Length -gt 16MB) { $run.OutputLimitExceeded = $true }
        Assert-ImplementationIdentity $pilotManifest
        Invoke-ImplementationIdentityCheck
        Invoke-Checked $DotNetPath @(
            $cliDll, 'pilot', 'receipt', '--directory', $outputRoot, '--run', $runId,
            '--started-at', $run.StartedAt, '--ended-at', $run.EndedAt, '--wall-ms', $run.WallMilliseconds.ToString([Globalization.CultureInfo]::InvariantCulture),
            '--exit-code', $run.ExitCode.ToString([Globalization.CultureInfo]::InvariantCulture), '--timed-out', $run.TimedOut.ToString().ToLowerInvariant(),
            '--output-limit-exceeded', $run.OutputLimitExceeded.ToString().ToLowerInvariant()
        )
        & $DotNetPath $cliDll pilot evaluate --directory $outputRoot --fixtures $fixtureRoot --run $runId --repository-root $repoRoot --dotnet $DotNetPath --dotnet-version 10.0.400 --codex-node $CodexNodePath --codex-entry $CodexEntryPath
        if ($LASTEXITCODE -ne 0) {
            Invoke-Checked $DotNetPath @(
                $cliDll, 'pilot', 'abort-remaining', '--directory', $outputRoot, '--after-run', $runId,
                '--repository-root', $repoRoot, '--dotnet', $DotNetPath, '--dotnet-version', '10.0.400',
                '--codex-node', $CodexNodePath, '--codex-entry', $CodexEntryPath
            )
            & $DotNetPath $cliDll pilot report --directory $outputRoot --output (Join-Path $outputRoot 'pilot-report.json')
            if (-not (Test-Path -LiteralPath (Join-Path $outputRoot 'pilot-report.json'))) { throw "Run $runId failed and canonical terminal report was not created." }
            throw "Run $runId reached a non-success terminal evaluation; remaining runs are marked NotRunDueToPriorFailure and no automatic retry was attempted."
        }
    }
    Invoke-Checked $DotNetPath @($cliDll, 'pilot', 'report', '--directory', $outputRoot, '--output', (Join-Path $outputRoot 'pilot-report.json'))
    Write-Output "E10 pilot completed: $outputRoot"
}
finally {
    if ([string]::IsNullOrWhiteSpace($originalCodexHome)) { Remove-Item Env:CODEX_HOME -ErrorAction SilentlyContinue } else { $env:CODEX_HOME = $originalCodexHome }
    if ($null -ne $isolatedCodexHome -and (Test-Path -LiteralPath $isolatedCodexHome)) {
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        $resolvedIsolated = [IO.Path]::GetFullPath($isolatedCodexHome)
        if (-not $resolvedIsolated.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or -not ([IO.Path]::GetFileName($resolvedIsolated)).StartsWith('strogo-e10-codexhome-', [StringComparison]::Ordinal)) { throw 'Refusing unsafe isolated CODEX_HOME cleanup path' }
        Remove-Item -LiteralPath $resolvedIsolated -Recurse -Force
    }
    Pop-Location
}
