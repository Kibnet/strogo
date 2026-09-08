param(
    [Parameter(Mandatory = $true)][string]$ArtifactPath,
    [Parameter(Mandatory = $true)][string]$RunDirectory,
    [string]$DotNetPath = "dotnet",
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$repo = [IO.Path]::GetFullPath($RepoRoot)
$artifact = [IO.Path]::GetFullPath($ArtifactPath)
$run = [IO.Path]::GetFullPath($RunDirectory)
if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) { throw "artifact unavailable: $artifact" }
if (Test-Path -LiteralPath $run) { throw "run directory already exists: $run" }
if ((& $DotNetPath --version) -ne "10.0.400") { throw ".NET SDK version mismatch" }
$runtimes = & $DotNetPath --list-runtimes
$runtime = $runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App 10\.0\.11 ' } | Select-Object -First 1
if ($null -eq $runtime) { throw ".NET runtime 10.0.11 unavailable" }

New-Item -ItemType Directory -Path $run | Out-Null
$consumer = Join-Path $run "consumer"
New-Item -ItemType Directory -Path $consumer | Out-Null
Copy-Item -LiteralPath (Join-Path $repo "tests\fixtures\portability-consumers\csharp\Consumer.csproj") -Destination (Join-Path $consumer "Consumer.csproj")
Copy-Item -LiteralPath (Join-Path $repo "tests\fixtures\portability-consumers\csharp\Program.cs") -Destination (Join-Path $consumer "Program.cs")

$output = & $DotNetPath run --project (Join-Path $consumer "Consumer.csproj") -c Release "-p:PortableAssemblyPath=$artifact" 2>&1
$exitCode = $LASTEXITCODE
[IO.File]::WriteAllLines((Join-Path $run "consumer-windows.log"), [string[]]$output, [Text.UTF8Encoding]::new($false))
if ($exitCode -ne 0) { throw "consumer exit $exitCode`n$($output -join "`n")" }
if ($output -notcontains "PASS standalone C# consumer cases=8 transport=24") { throw "consumer PASS marker unavailable" }

$report = [ordered]@{
    schemaVersion = "strogo.dotnet-platform-run.v0.1"
    status = "Passed"
    profileId = "dotnet-managed.v1"
    os = "windows"
    arch = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    osIdentity = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    runtime = $runtime
    artifactDigest = (Get-FileHash -Algorithm SHA256 -LiteralPath $artifact).Hash.ToLowerInvariant()
    consumerOutcome = "Passed"
    repositoryRevision = (git -C $repo rev-parse HEAD)
    repositoryDirty = [bool](git -C $repo status --porcelain)
}
[IO.File]::WriteAllText((Join-Path $run "report.json"), (($report | ConvertTo-Json -Compress) + "`n"), [Text.UTF8Encoding]::new($false))
Write-Output "PASS dotnet-managed windows artifact=$($report.artifactDigest) consumer=8 transport=24"
