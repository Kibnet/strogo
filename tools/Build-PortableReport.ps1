[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts/local-validation/e06/e06r"
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\")).Path
$output = [IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory))
$report = Join-Path $output "report.json"
$log = Join-Path $output "run.log"

New-Item -ItemType Directory -Force -Path $output | Out-Null
Push-Location $repo
try {
    & dotnet run --project tests/Strogo.Modules.Portability.Conformance -c Release --no-restore -- --e06r-report $report 2>&1 | Tee-Object -FilePath $log
    if ($LASTEXITCODE -ne 0) { throw "E06R conformance failed with exit code $LASTEXITCODE" }
    if (-not (Test-Path -LiteralPath $report -PathType Leaf)) { throw "E06R report was not written" }

    $document = Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
    if ($document.schemaVersion -ne "strogo.portability-report.v0.1" -or
        $document.purpose -ne "validation-only" -or
        $document.contractStatus -ne "validation-fixture" -or
        $document.admissionStatus -ne "NotAdmittable" -or
        $document.profiles.Count -ne 2) {
        throw "E06R report boundary or profile count is invalid"
    }
    $sha = (Get-FileHash -Algorithm SHA256 -LiteralPath $report).Hash.ToLowerInvariant()
    [pscustomobject]@{
        report = $report
        reportSha256 = $sha
        schemaVersion = $document.schemaVersion
        comparisonStatus = $document.comparisonStatus
        profiles = $document.profiles.Count
        noJvmExecutionBeforeBaselineApproval = $true
    } | ConvertTo-Json -Compress
}
finally {
    Pop-Location
}
