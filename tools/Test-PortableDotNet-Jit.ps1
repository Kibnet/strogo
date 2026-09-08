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
    --dotnet-executable $targetDotNet `
    --runtime-version "10.0.11" `
    --os "windows" `
    --arch "x64" `
    --inventory $runtimeInventory `
    --report $runtimeReport `
    --expected-runtime-closure-digest $ExpectedRuntimeClosureDigest 2>&1
if ($LASTEXITCODE -ne 0) {
    foreach ($line in $runtimeOutput) { [Console]::Error.WriteLine($line) }
    exit 70
}
if ($runtimeOutput -notmatch '^PASS dotnet runtime closure ') { throw "runtime closure PASS marker unavailable" }

$staged = Join-Path $run "validated\strogo.portable.v01.dll"
$validationReport = Join-Path $run "package-validation.json"
$validationOutput = & $harnessDotNet run --project (Join-Path $repo "tests\Strogo.Modules.Portability.PackageHarness") -c Release --no-build -- validate `
    --package $package `
    --expected-manifest-digest $ExpectedManifestDigest `
    --staged-artifact $staged `
    --report $validationReport 2>&1
if ($LASTEXITCODE -ne 0) { throw "package validation failed`n$($validationOutput -join "`n")" }
if ($validationOutput -notmatch '^PASS package validation ') { throw "package validation PASS marker unavailable" }

$jitPlanPath = Join-Path $run "jit-plan.json"
$jitPlanOutput = & $harnessDotNet run --project (Join-Path $repo "tests\Strogo.Modules.Portability.PackageHarness") -c Release --no-build -- jit-plan `
    --package $package `
    --expected-manifest-digest $ExpectedManifestDigest `
    --output $jitPlanPath 2>&1
if ($LASTEXITCODE -ne 0) { throw "JIT plan failed`n$($jitPlanOutput -join "`n")" }
$jitPlan = Get-Content -LiteralPath $jitPlanPath -Raw | ConvertFrom-Json
if ($jitPlan.schemaVersion -ne "strogo.dotnet-jit-plan.v0.1" -or $jitPlan.calls -ne "50000") { throw "JIT plan contract mismatch" }

$consumer = Join-Path $run "consumer"
New-Item -ItemType Directory -Path $consumer | Out-Null
Copy-Item -LiteralPath (Join-Path $repo "tests\fixtures\portability-consumers\csharp-jit\JitConsumer.csproj") -Destination (Join-Path $consumer "JitConsumer.csproj")
Copy-Item -LiteralPath (Join-Path $repo "tests\fixtures\portability-consumers\csharp-jit\Program.cs") -Destination (Join-Path $consumer "Program.cs")
$consumerBuildOutput = & $harnessDotNet build (Join-Path $consumer "JitConsumer.csproj") -c Release "-p:ImportDirectoryBuildProps=false" "-p:ImportDirectoryBuildTargets=false" "-p:PortableAssemblyPath=$staged" 2>&1
[IO.File]::WriteAllLines((Join-Path $run "consumer-build.log"), [string[]]$consumerBuildOutput, [Text.UTF8Encoding]::new($false))
if ($LASTEXITCODE -ne 0) { throw "JIT consumer build failed`n$($consumerBuildOutput -join "`n")" }
$consumerDll = Join-Path $consumer "bin\Release\net10.0\JitConsumer.dll"
if (-not (Test-Path -LiteralPath $consumerDll -PathType Leaf)) { throw "JIT consumer assembly unavailable" }

$jitLog = Join-Path $run "jit.log"
$consumerOutput = Join-Path $run "consumer.log"
$consumerError = Join-Path $run "consumer.stderr.log"
$startInfo = [Diagnostics.ProcessStartInfo]::new($targetDotNet)
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.ArgumentList.Add($consumerDll)
$startInfo.Environment["DOTNET_ReadyToRun"] = "0"
$startInfo.Environment["DOTNET_TieredCompilation"] = "0"
$startInfo.Environment["DOTNET_JitNoInline"] = "1"
$startInfo.Environment["DOTNET_JitDisasm"] = "$($jitPlan.entrySymbol) $($jitPlan.candidateSymbol)"
$startInfo.Environment["DOTNET_JitDisasmDiffable"] = "1"
$startInfo.Environment["DOTNET_JitDisasmTesting"] = "1"
$startInfo.Environment["DOTNET_JitStdOutFile"] = $jitLog
$process = [Diagnostics.Process]::Start($startInfo)
if ($null -eq $process) { throw "JIT consumer process did not start" }
$stdoutTask = $process.StandardOutput.ReadToEndAsync()
$stderrTask = $process.StandardError.ReadToEndAsync()
if (-not $process.WaitForExit(180000)) {
    $process.Kill($true)
    $process.WaitForExit()
    throw "JIT consumer exceeded 180 second deadline"
}
$stdout = $stdoutTask.GetAwaiter().GetResult()
$stderr = $stderrTask.GetAwaiter().GetResult()
[IO.File]::WriteAllText($consumerOutput, $stdout, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($consumerError, $stderr, [Text.UTF8Encoding]::new($false))
if ($process.ExitCode -ne 0) { throw "JIT consumer failed with exit $($process.ExitCode): $stderr" }
if ($stderr.Length -ne 0) { throw "JIT consumer wrote stderr" }
if ($stdout.TrimEnd("`r", "`n") -ne "PASS dotnet JIT diagnostic pid=$($process.Id) calls=50000") { throw "JIT consumer process receipt mismatch" }
if (-not (Test-Path -LiteralPath $jitLog -PathType Leaf)) { throw "JIT log unavailable" }
if ((Get-Item -LiteralPath $jitLog).Length -gt 1MB) { throw "JIT log exceeded 1 MiB" }

$receipt = Join-Path $run "jit-receipt.json"
$receiptOutput = & $harnessDotNet run --project (Join-Path $repo "tests\Strogo.Modules.Portability.PackageHarness") -c Release --no-build -- jit-receipt `
    --package $package `
    --expected-manifest-digest $ExpectedManifestDigest `
    --runtime-report $runtimeReport `
    --consumer-output $consumerOutput `
    --jit-log $jitLog `
    --os "windows" `
    --arch "x64" `
    --output $receipt 2>&1
if ($LASTEXITCODE -ne 0) { throw "JIT receipt validation failed`n$($receiptOutput -join "`n")" }
if ($receiptOutput -notmatch '^PASS dotnet JIT receipt os=windows events=2 calls=50000 process=') { throw "JIT receipt PASS marker unavailable" }

Write-Output "PASS dotnet JIT diagnostic events=2 calls=50000"
