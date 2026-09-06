[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$prototypeRoot = Split-Path -Parent $PSScriptRoot
$config = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'z3.json') -Raw | ConvertFrom-Json
$binary = Join-Path $prototypeRoot $config.executable
if (Test-Path -LiteralPath $binary) {
    if ((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $config.sha256) {
        throw 'Installed Z3 digest mismatch. Inspect tools directory before replacing any files.'
    }
    & $binary --version
    exit $LASTEXITCODE
}
$downloads = Join-Path $PSScriptRoot 'downloads'
New-Item -ItemType Directory -Path $downloads -Force | Out-Null
$archive = Join-Path $downloads 'z3-5.1.0-x64-win.zip'
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri $config.source -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $config.archiveSha256) {
    throw 'Z3 archive does not match the pinned release digest.'
}
Expand-Archive -LiteralPath $archive -DestinationPath $PSScriptRoot
if ((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $config.sha256) {
    throw 'Extracted Z3 binary digest mismatch.'
}
& $binary --version
exit $LASTEXITCODE
