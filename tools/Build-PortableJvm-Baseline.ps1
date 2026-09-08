param(
    [Parameter(Mandatory = $true)][string]$RunDirectory,
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$DotNetPath = "dotnet",
    [string]$DafnyPath = (Join-Path (Split-Path -Parent $PSScriptRoot) ".tools\dafny\dafny\dafny.exe"),
    [string]$JavacPath = "C:\Program Files\Zulu\zulu-17\bin\javac.exe",
    [string]$GitPath = (Get-Command git -ErrorAction Stop).Source
)

$ErrorActionPreference = "Stop"
$repo = [IO.Path]::GetFullPath($RepoRoot)
$run = [IO.Path]::GetFullPath($RunDirectory)
$dafny = [IO.Path]::GetFullPath($DafnyPath)
$javac = [IO.Path]::GetFullPath($JavacPath)
$git = [IO.Path]::GetFullPath($GitPath)
$dotnetCommand = Get-Command $DotNetPath -ErrorAction Stop
$dotnet = [IO.Path]::GetFullPath($dotnetCommand.Source)
$dotnetEnvironmentToRemove = @(
    "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86", "DOTNET_ROOT_ARM64",
    "DOTNET_STARTUP_HOOKS", "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE",
    "DOTNET_ROLL_FORWARD", "DOTNET_ROLL_FORWARD_TO_PRERELEASE", "DOTNET_HOST_PATH",
    "MSBuildSDKsPath", "MSBUILD_EXE_PATH"
)
foreach ($name in $dotnetEnvironmentToRemove) { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
if (Test-Path -LiteralPath $run) { throw "run directory already exists: $run" }
if (-not (Test-Path -LiteralPath $dafny -PathType Leaf)) { throw "Dafny unavailable: $dafny" }
if (-not (Test-Path -LiteralPath $javac -PathType Leaf)) { throw "javac unavailable: $javac" }
if (-not (Test-Path -LiteralPath $git -PathType Leaf)) { throw "git unavailable: $git" }
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw ".NET unavailable: $dotnet" }
if ((& $dotnet --version) -ne "10.0.400") { throw ".NET SDK version mismatch" }

& $dotnet build (Join-Path $repo "tests\Strogo.Modules.Portability.JvmHarness\Strogo.Modules.Portability.JvmHarness.csproj") -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "JVM harness build failed" }

$harness = Join-Path $repo "tests\Strogo.Modules.Portability.JvmHarness\bin\Release\net10.0\Strogo.Modules.Portability.JvmHarness.dll"
if (-not (Test-Path -LiteralPath $harness -PathType Leaf)) { throw "JVM harness output unavailable: $harness" }
& $dotnet $harness `
    candidate `
    --repo-root $repo `
    --run-root $run `
    --dafny $dafny `
    --javac $javac `
    --git $git `
    --dotnet $dotnet
if ($LASTEXITCODE -ne 0) { throw "JVM baseline candidate run failed" }
