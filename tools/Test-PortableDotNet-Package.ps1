param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{64}$')][string]$ExpectedManifestDigest,
    [Parameter(Mandatory = $true)][string]$RunDirectory,
    [string]$DotNetPath = "dotnet",
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$repo = [IO.Path]::GetFullPath($RepoRoot)
$package = [IO.Path]::GetFullPath($PackageDirectory)
$run = [IO.Path]::GetFullPath($RunDirectory)
if (-not (Test-Path -LiteralPath $package -PathType Container)) { throw "package unavailable: $package" }
if (Test-Path -LiteralPath $run) { throw "run directory already exists: $run" }
if ((& $DotNetPath --version) -ne "10.0.400") { throw ".NET SDK version mismatch" }
$runtimeLine = & $DotNetPath --list-runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App 10\.0\.11 ' } | Select-Object -First 1
if ($null -eq $runtimeLine) { throw ".NET runtime 10.0.11 unavailable" }
$runtime = ($runtimeLine -split ' \[')[0]

New-Item -ItemType Directory -Path $run | Out-Null
$staged = Join-Path $run "validated\strogo.portable.v01.dll"
$validationReport = Join-Path $run "package-validation.json"
$validationOutput = & $DotNetPath run --project (Join-Path $repo "tests\Strogo.Modules.Portability.PackageHarness") -c Release --no-build -- validate `
    --package $package `
    --expected-manifest-digest $ExpectedManifestDigest `
    --staged-artifact $staged `
    --report $validationReport 2>&1
if ($LASTEXITCODE -ne 0) { throw "package validation failed`n$($validationOutput -join "`n")" }
if ($validationOutput -notmatch '^PASS package validation ') { throw "package validation PASS marker unavailable" }

$consumer = Join-Path $run "consumer"
New-Item -ItemType Directory -Path $consumer | Out-Null
Copy-Item -LiteralPath (Join-Path $repo "tests\fixtures\portability-consumers\csharp\Consumer.csproj") -Destination (Join-Path $consumer "Consumer.csproj")
Copy-Item -LiteralPath (Join-Path $repo "tests\fixtures\portability-consumers\csharp\Program.cs") -Destination (Join-Path $consumer "Program.cs")
$vectors = Join-Path $repo "fixtures\portability-v0.1\invoke-vectors.jsonl"
$consumerOutput = & $DotNetPath run --project (Join-Path $consumer "Consumer.csproj") -c Release "-p:PortableAssemblyPath=$staged" -- $vectors 2>&1
[IO.File]::WriteAllLines((Join-Path $run "consumer.log"), [string[]]$consumerOutput, [Text.UTF8Encoding]::new($false))
if ($LASTEXITCODE -ne 0) { throw "consumer failed`n$($consumerOutput -join "`n")" }
if ($consumerOutput -notcontains "PASS standalone C# consumer cases=8 transport=24 additional=1") { throw "consumer PASS marker unavailable" }
if ($consumerOutput -notcontains "PASS standalone C# invoke vectors=13") { throw "invoke vector PASS marker unavailable" }

$validation = Get-Content -LiteralPath $validationReport -Raw | ConvertFrom-Json
$report = [ordered]@{
    schemaVersion = "strogo.dotnet-package-platform-run.v0.1"
    status = "Passed"
    profileId = "dotnet-managed.v1"
    os = "windows"
    arch = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    runtime = $runtime
    portabilityManifestDigest = $validation.portabilityManifestDigest
    packageDigest = $validation.packageDigest
    artifactDigest = $validation.artifactDigest
    stagedArtifactSha256 = $validation.stagedArtifactSha256
    validationOutcome = "Passed"
    consumerOutcome = "Passed"
}
[IO.File]::WriteAllText((Join-Path $run "report.json"), (($report | ConvertTo-Json -Compress) + "`n"), [Text.UTF8Encoding]::new($false))
Write-Output "PASS dotnet package windows manifest=$($validation.portabilityManifestDigest) consumer=8+24+1+13"
