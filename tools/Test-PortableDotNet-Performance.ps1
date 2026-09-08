param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{64}$')][string]$ExpectedManifestDigest,
    [Parameter(Mandatory = $true)][string]$RunDirectory,
    [string]$DotNetPath = "dotnet",
    [string]$HarnessDotNetPath = "dotnet",
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{64}$')][string]$ExpectedRuntimeClosureDigest,
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$repo = [IO.Path]::GetFullPath($RepoRoot)
$package = [IO.Path]::GetFullPath($PackageDirectory)
$run = [IO.Path]::GetFullPath($RunDirectory)
if (-not (Test-Path -LiteralPath $package -PathType Container)) { throw "package unavailable: $package" }
if (Test-Path -LiteralPath $run) { throw "run directory already exists: $run" }
New-Item -ItemType Directory -Path $run | Out-Null
$targetDotNet = (Get-Command -Name $DotNetPath -CommandType Application -ErrorAction Stop).Source
$harnessDotNet = (Get-Command -Name $HarnessDotNetPath -CommandType Application -ErrorAction Stop).Source
if ((& $harnessDotNet --version) -ne "10.0.400") { throw ".NET harness SDK version mismatch" }

$runtimeInventory = Join-Path $run "runtime-inventory.json"
$runtimeReport = Join-Path $run "environment.json"
$runtimeOutput = & $harnessDotNet run --project (Join-Path $repo "tests\Strogo.Modules.Portability.PackageHarness") -c Release --no-build -- runtime `
    --dotnet-executable $targetDotNet --runtime-version "10.0.11" --os "windows" --arch "x64" `
    --inventory $runtimeInventory --report $runtimeReport --expected-runtime-closure-digest $ExpectedRuntimeClosureDigest 2>&1
if ($LASTEXITCODE -ne 0) { foreach ($line in $runtimeOutput) { [Console]::Error.WriteLine($line) }; exit 70 }

$staged = Join-Path $run "validated\strogo.portable.v01.dll"
$validationReport = Join-Path $run "package-validation.json"
$validationOutput = & $harnessDotNet run --project (Join-Path $repo "tests\Strogo.Modules.Portability.PackageHarness") -c Release --no-build -- validate `
    --package $package --expected-manifest-digest $ExpectedManifestDigest --staged-artifact $staged --report $validationReport 2>&1
if ($LASTEXITCODE -ne 0) { throw "package validation failed`n$($validationOutput -join "`n")" }

$consumer = Join-Path $run "consumer"
New-Item -ItemType Directory -Path $consumer | Out-Null
Copy-Item (Join-Path $repo "tests\fixtures\portability-consumers\csharp-performance\PerformanceConsumer.csproj") (Join-Path $consumer "PerformanceConsumer.csproj")
Copy-Item (Join-Path $repo "tests\fixtures\portability-consumers\csharp-performance\Program.cs") (Join-Path $consumer "Program.cs")
$buildOutput = & $harnessDotNet build (Join-Path $consumer "PerformanceConsumer.csproj") -c Release "-p:ImportDirectoryBuildProps=false" "-p:ImportDirectoryBuildTargets=false" "-p:PortableAssemblyPath=$staged" 2>&1
[IO.File]::WriteAllLines((Join-Path $run "consumer-build.log"),[string[]]$buildOutput,[Text.UTF8Encoding]::new($false))
if ($LASTEXITCODE -ne 0) { throw "performance consumer build failed" }
$consumerDll = Join-Path $consumer "bin\Release\net10.0\PerformanceConsumer.dll"

function Invoke-Observation([string]$Mode) {
    $startInfo=[Diagnostics.ProcessStartInfo]::new($targetDotNet)
    $startInfo.UseShellExecute=$false; $startInfo.CreateNoWindow=$true
    $startInfo.RedirectStandardOutput=$true; $startInfo.RedirectStandardError=$true
    $startInfo.ArgumentList.Add($consumerDll); $startInfo.ArgumentList.Add($Mode)
    foreach($name in @('DOTNET_ReadyToRun','DOTNET_TieredCompilation','DOTNET_JitNoInline','DOTNET_JitDisasm','DOTNET_JitDisasmDiffable','DOTNET_JitDisasmTesting','DOTNET_JitStdOutFile')) { $null=$startInfo.Environment.Remove($name) }
    $timer=[Diagnostics.Stopwatch]::StartNew()
    $process=[Diagnostics.Process]::Start($startInfo)
    if($null -eq $process){throw 'performance process did not start'}
    $stdoutTask=$process.StandardOutput.ReadToEndAsync(); $stderrTask=$process.StandardError.ReadToEndAsync()
    if(-not $process.WaitForExit(180000)){ $process.Kill($true); $process.WaitForExit(); throw 'performance process exceeded 180 second deadline' }
    $timer.Stop(); $stdout=$stdoutTask.GetAwaiter().GetResult(); $stderr=$stderrTask.GetAwaiter().GetResult()
    if($process.ExitCode -ne 0 -or $stderr.Length -ne 0){throw "performance process failed: $stderr"}
    $payload=$stdout|ConvertFrom-Json
    if($payload.status -ne 'Passed' -or $payload.mode -ne $Mode -or $payload.processId -ne $process.Id.ToString()){throw 'performance process receipt mismatch'}
    $elapsedNanoseconds=([long]($timer.Elapsed.Ticks * 100)).ToString([Globalization.CultureInfo]::InvariantCulture)
    $peakWorkingSetBytes=$payload.peakWorkingSetBytes
    [ordered]@{processId=$payload.processId;elapsedNanoseconds=$elapsedNanoseconds;peakWorkingSetBytes=$peakWorkingSetBytes;observation=$payload}
}

$startup=@(); foreach($index in 1..5){$startup += Invoke-Observation 'startup'}
$throughput=Invoke-Observation 'throughput'
if($startup.Count -ne 5){throw 'performance report requires exactly five cold starts'}
foreach($observation in $startup){
    if($observation.observation.calls -ne '1' -or [long]$observation.elapsedNanoseconds -le 0 -or [long]$observation.peakWorkingSetBytes -le 0){throw 'invalid cold-start observation'}
}
if($throughput.observation.warmupCalls -ne '5000' -or $throughput.observation.repeats -ne '5' -or $throughput.observation.callsPerRepeat -ne '10000'){throw 'throughput settings receipt mismatch'}
if($throughput.observation.durationsNanoseconds.Count -ne 5 -or $throughput.observation.operationsPerSecond.Count -ne 5 -or [long]$throughput.peakWorkingSetBytes -le 0){throw 'invalid throughput observation'}
foreach($value in @($throughput.observation.durationsNanoseconds)+@($throughput.observation.operationsPerSecond)){if([long]$value -le 0){throw 'throughput observations must be positive'}}
$manifest=Get-Content -Raw (Join-Path $package 'portability-manifest.json')|ConvertFrom-Json
$environment=Get-Content -Raw $runtimeReport|ConvertFrom-Json
$validation=Get-Content -Raw $validationReport|ConvertFrom-Json
$packageTreeBytes=[long](Get-ChildItem -LiteralPath $package -File -Recurse|Measure-Object Length -Sum).Sum
$artifactSetBytes=[long]($manifest.files|Measure-Object {[long]$_.length} -Sum).Sum
$report=[ordered]@{
    schemaVersion='strogo.dotnet-performance-report.v0.1'; status='Passed'; assertionBoundary='DiagnosticOnlyNoG06'
    profileId='dotnet-managed.v1'; os='windows'; arch='x64'; runtimeVendor=$environment.runtimeVendor; runtimeVersion=$environment.runtimeVersion
    runtimeClosureDigest=$environment.runtimeClosureDigest; portabilityManifestDigest=$validation.portabilityManifestDigest; packageDigest=$validation.packageDigest; artifactDigest=$validation.artifactDigest
    commands=[ordered]@{startup='dotnet PerformanceConsumer.dll startup';throughput='dotnet PerformanceConsumer.dll throughput'}
    settings=[ordered]@{coldStartRepeats='5';warmupCalls='5000';throughputRepeats='5';callsPerRepeat='10000';jitDiagnosticOverrides=@()}
    startup=$startup; throughput=$throughput
    sizes=[ordered]@{entryAssemblyBytes=(Get-Item $staged).Length.ToString();artifactSetBytes=$artifactSetBytes.ToString();packageTreeBytes=$packageTreeBytes.ToString()}
}
[IO.File]::WriteAllText((Join-Path $run 'performance-report.json'),(($report|ConvertTo-Json -Depth 8 -Compress)+"`n"),[Text.UTF8Encoding]::new($false))
Write-Output 'PASS dotnet performance diagnostic cold=5 warmup=5000 repeats=5 calls=10000'
