param([switch]$VerifyOnly)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$m=Get-Content (Join-Path $PSScriptRoot 'dafny.json') -Raw | ConvertFrom-Json
$archive=Join-Path $root '.tools/downloads/dafny-4.11.0.zip'
$exe=Join-Path $root '.tools/dafny/dafny/dafny.exe'
if (-not $VerifyOnly) {
    New-Item -ItemType Directory -Force (Split-Path $archive) | Out-Null
    if (-not (Test-Path $archive)) { Invoke-WebRequest $m.url -OutFile $archive }
    if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $m.sha256) { throw 'Archive digest mismatch' }
    if (-not (Test-Path $exe)) { Expand-Archive $archive -DestinationPath (Join-Path $root '.tools/dafny') }
}
if (-not(Test-Path $archive) -or (Get-FileHash $archive).Hash.ToLowerInvariant() -cne $m.sha256) { throw 'Archive missing/mismatch' }
if (-not(Test-Path $exe) -or (Get-FileHash $exe).Hash.ToLowerInvariant() -cne $m.executableSha256) { throw 'Executable missing/mismatch' }
$version=& $exe --version
if($LASTEXITCODE -ne 0 -or -not $version.StartsWith($m.version+'+')) { throw 'Dafny version mismatch' }
[pscustomobject]@{version=$version;archiveSha256=$m.sha256;executableSha256=$m.executableSha256;path=$exe}|ConvertTo-Json
