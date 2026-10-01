#requires -Version 7.0
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Native observer preflight requires Windows x64.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskOutput = Join-Path $repoRoot ('artifacts/local-validation/g02/native-preflight-' + [Guid]::NewGuid().ToString('N'))
$compiler = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64\cl.exe'
$setup = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat'
$headers = 'C:\Program Files (x86)\Windows Kits\NETFXSDK\4.8\Include\um'
$source = Join-Path $PSScriptRoot 'Preflight.cpp'
$commandShell = 'C:\Windows\System32\cmd.exe'
foreach ($path in @($commandShell, $compiler, $setup, $source, (Join-Path $headers 'corprof.h'), (Join-Path $headers 'cor.h'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing preflight input: $path" }
}
# This script passes paths through cmd; reject expansion/control characters even inside quotes.
foreach ($path in @($taskOutput, $compiler, $setup, $headers, $source)) {
    if ($path -match '["%!^&|<>\r\n]') { throw 'Unsupported shell metacharacter in preflight path.' }
    if ($path -match '[^\x00-\x7f]') { throw 'The ASCII build command requires ASCII preflight paths.' }
}
New-Item -ItemType Directory -Path $taskOutput | Out-Null
$batchPath = Join-Path $taskOutput 'build.cmd'
@(
    '@echo off'
    ('call "' + $setup + '"')
    'if errorlevel 1 exit /b %errorlevel%'
    ('"' + $compiler + '" /nologo /std:c++17 /EHsc /W4 /WX /O2 /MD /I"' + $headers +
        '" /Fo"' + $taskOutput + '\Preflight.obj" /Fe"' + $taskOutput + '\Preflight.exe" "' + $source + '"')
    'exit /b %errorlevel%'
) | Set-Content -LiteralPath $batchPath -Encoding ascii
& $commandShell /d /c $batchPath 2>&1 | Tee-Object -FilePath (Join-Path $taskOutput 'build-stdout.txt')
$buildExit = $LASTEXITCODE
if ($buildExit -ne 0) { throw "Native preflight compile failed: $buildExit; diagnostics: $taskOutput" }
& (Join-Path $taskOutput 'Preflight.exe') | Tee-Object -FilePath (Join-Path $taskOutput 'probe-stdout.txt')
$probeExit = $LASTEXITCODE
if ($probeExit -ne 0) { throw "Native preflight execution failed: $probeExit; diagnostics: $taskOutput" }
[ordered]@{
    purpose = 'declaration-preflight-only'
    outputDirectory = $taskOutput
    buildExitCode = $buildExit
    probeExitCode = $probeExit
    compilerSha256 = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash.ToLowerInvariant()
    commandShellSha256 = (Get-FileHash -LiteralPath $commandShell -Algorithm SHA256).Hash.ToLowerInvariant()
    sourceSha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    corHeaderSha256 = (Get-FileHash -LiteralPath (Join-Path $headers 'cor.h') -Algorithm SHA256).Hash.ToLowerInvariant()
    corprofHeaderSha256 = (Get-FileHash -LiteralPath (Join-Path $headers 'corprof.h') -Algorithm SHA256).Hash.ToLowerInvariant()
    executableSha256 = (Get-FileHash -LiteralPath (Join-Path $taskOutput 'Preflight.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
    limits = @('No CLR loaded', 'Not a profiler', 'Not a complete compiler/header/runtime closure inventory')
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskOutput 'receipt.json') -Encoding utf8
Write-Output ('Receipt: ' + (Join-Path $taskOutput 'receipt.json'))
