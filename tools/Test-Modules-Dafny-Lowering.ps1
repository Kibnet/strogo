param(
    [string]$RunDirectory = "artifacts/local-validation/e05/modules-dafny-lowering"
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $RunDirectory))
$dafny = Join-Path $repoRoot '.tools/dafny/dafny/dafny.exe'
$dafnyInstaller = Join-Path $repoRoot 'tools/Install-Dafny.ps1'
$dafnyToolEvidence = (& $dafnyInstaller -VerifyOnly | Out-String) | ConvertFrom-Json

New-Item -ItemType Directory -Force -Path $runPath | Out-Null
$moduleReport = Join-Path $runPath 'modules-v0.2.json'
$safeSource = Join-Path $runPath 'Candidate.dfy'
$callSource = Join-Path $runPath 'CallCandidate.dfy'
$unsafeSource = Join-Path $runPath 'UnsafeCandidate.dfy'
$scalarSource = Join-Path $runPath 'ScalarCandidate.dfy'
$safeGenerated = Join-Path $runPath 'Generated.cs'
$callGenerated = Join-Path $runPath 'CallGenerated.cs'
$scalarGenerated = Join-Path $runPath 'ScalarGenerated.cs'
$summaryPath = Join-Path $runPath 'dafny-lowering.json'
Remove-Item -LiteralPath $summaryPath -ErrorAction SilentlyContinue

Push-Location $repoRoot
try {
    & dotnet run --project tests/Strogo.Modules.Conformance/Strogo.Modules.Conformance.csproj -c Release -- --report $moduleReport --dafny-out $safeSource --dafny-call-out $callSource --dafny-unsafe-out $unsafeSource --dafny-scalar-out $scalarSource
    if ($LASTEXITCODE -ne 0) { throw 'Modules conformance failed' }

    $safeVerification = (& $dafny translate cs $safeSource --include-runtime --enforce-determinism --cores 2 --verification-time-limit 15 --output $safeGenerated 2>&1 | Out-String).Trim()
    $safeExitCode = $LASTEXITCODE
    if ($safeExitCode -ne 0 -or $safeVerification -notmatch '(?m)^Dafny program verifier finished with 2 verified, 0 errors\r?$') { throw "Safe selector verification failed:`n$safeVerification" }

    $callVerification = (& $dafny translate cs $callSource --include-runtime --enforce-determinism --cores 2 --verification-time-limit 15 --output $callGenerated 2>&1 | Out-String).Trim()
    $callExitCode = $LASTEXITCODE
    if ($callExitCode -ne 0 -or $callVerification -notmatch '(?m)^Dafny program verifier finished with 3 verified, 0 errors\r?$') { throw "Safe call verification failed:`n$callVerification" }

    $scalarVerification = (& $dafny translate cs $scalarSource --include-runtime --enforce-determinism --cores 2 --verification-time-limit 15 --output $scalarGenerated 2>&1 | Out-String).Trim()
    $scalarExitCode = $LASTEXITCODE
    if ($scalarExitCode -ne 0 -or $scalarVerification -notmatch '(?m)^Dafny program verifier finished with 2 verified, 0 errors\r?$') { throw "Safe scalar verification failed:`n$scalarVerification" }

    $unsafeVerification = (& $dafny verify $unsafeSource --enforce-determinism --cores 2 --verification-time-limit 15 2>&1 | Out-String).Trim()
    $unsafeExitCode = $LASTEXITCODE
    if ($unsafeExitCode -eq 0 -or $unsafeVerification -notmatch "might violate newtype constraint for 'I64'") {
        throw "Unsafe arithmetic did not fail with the required range obligation:`n$unsafeVerification"
    }

    $generatedProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Library</OutputType>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AssemblyName>Strogo.Generated.SafeSelect</AssemblyName>
    <Nullable>disable</Nullable>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup><Compile Include="Generated.cs" /></ItemGroup>
</Project>
'@
    $consumerProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
    <ProjectReference Include="Generated.csproj" />
  </ItemGroup>
</Project>
'@
    $consumerProgram = @'
using System.Reflection.PortableExecutable;

var firstValue = Candidate.__default.F000(true, long.MaxValue, 11L);
var secondValue = Candidate.__default.F000(true, 2L, 11L);
var thirdValue = Candidate.__default.F000(false, long.MaxValue, 11L);
using var stream = File.OpenRead(typeof(Candidate.__default).Assembly.Location);
using var pe = new PEReader(stream);
var nativeHeaderSize = pe.PEHeaders.CorHeader?.ManagedNativeHeaderDirectory.Size ?? 0;
Console.WriteLine($"outcomes={firstValue},{secondValue},{thirdValue};nativeHeader={nativeHeaderSize}");
return firstValue == long.MaxValue && secondValue == 3L && thirdValue == 11L && nativeHeaderSize > 0 ? 0 : 1;
'@
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText((Join-Path $runPath 'Generated.csproj'), $generatedProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'Consumer.csproj'), $consumerProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'Program.cs'), $consumerProgram, $utf8)

    $scalarGeneratedProject = $generatedProject.Replace('Strogo.Generated.SafeSelect', 'Strogo.Generated.Scalar').Replace('Generated.cs', 'ScalarGenerated.cs')
    $scalarConsumerProject = $consumerProject.Replace('Program.cs', 'ScalarProgram.cs').Replace('Generated.csproj', 'ScalarGenerated.csproj')
    $scalarConsumerProgram = @'
var firstValue = Candidate.__default.F000(false, false);
var secondValue = Candidate.__default.F000(true, false);
var thirdValue = Candidate.__default.F000(true, true);
Console.WriteLine($"scalarOutcomes={firstValue},{secondValue},{thirdValue}");
return firstValue && !secondValue && thirdValue ? 0 : 1;
'@
    [IO.File]::WriteAllText((Join-Path $runPath 'ScalarGenerated.csproj'), $scalarGeneratedProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'ScalarConsumer.csproj'), $scalarConsumerProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'ScalarProgram.cs'), $scalarConsumerProgram, $utf8)

    $callGeneratedProject = $generatedProject.Replace('Strogo.Generated.SafeSelect', 'Strogo.Generated.SafeCall').Replace('Generated.cs', 'CallGenerated.cs')
    $callConsumerProject = $consumerProject.Replace('Program.cs', 'CallProgram.cs').Replace('Generated.csproj', 'CallGenerated.csproj')
    $callConsumerProgram = @'
var value = Candidate.__default.F001(7L);
Console.WriteLine($"callOutcome={value}");
return value == 7L ? 0 : 1;
'@
    [IO.File]::WriteAllText((Join-Path $runPath 'CallGenerated.csproj'), $callGeneratedProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'CallConsumer.csproj'), $callConsumerProject, $utf8)
    [IO.File]::WriteAllText((Join-Path $runPath 'CallProgram.cs'), $callConsumerProgram, $utf8)

    $publishPath = Join-Path $runPath 'publish'
    & dotnet publish (Join-Path $runPath 'Consumer.csproj') -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o $publishPath
    if ($LASTEXITCODE -ne 0) { throw 'ReadyToRun publish failed' }
    $consumerOutput = (& (Join-Path $publishPath 'Consumer.exe') 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $consumerOutput -notmatch '^outcomes=9223372036854775807,3,11;nativeHeader=([1-9][0-9]*)$') { throw "Generated consumer failed:`n$consumerOutput" }
    $nativeHeaderSize = [int]$Matches[1]

    $scalarConsumerOutput = (& dotnet run --project (Join-Path $runPath 'ScalarConsumer.csproj') -c Release 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $scalarConsumerOutput -notmatch '(?m)^scalarOutcomes=True,False,True\r?$') { throw "Generated scalar consumer failed:`n$scalarConsumerOutput" }
    $scalarOutcomeLine = $Matches[0].Trim()

    $callConsumerOutput = (& dotnet run --project (Join-Path $runPath 'CallConsumer.csproj') -c Release 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $callConsumerOutput -notmatch '(?m)^callOutcome=7\r?$') { throw "Generated call consumer failed:`n$callConsumerOutput" }
    $callOutcomeLine = $Matches[0].Trim()

    $generatedDll = Join-Path $publishPath 'Strogo.Generated.SafeSelect.dll'
    $dllInfo = Get-Item -LiteralPath $generatedDll
    $summary = [ordered]@{
        schemaVersion = 'strogo.modules-dafny-lowering.validation.v1'
        generatedAtUtc = [DateTime]::UtcNow.ToString('o')
        dafnyTool = [ordered]@{
            version = $dafnyToolEvidence.version
            archiveSha256 = $dafnyToolEvidence.archiveSha256
            executableSha256 = $dafnyToolEvidence.executableSha256
        }
        moduleReport = [ordered]@{
            path = 'modules-v0.2.json'
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $moduleReport).Hash.ToLowerInvariant()
        }
        safeSelector = [ordered]@{
            exitCode = $safeExitCode
            verification = $safeVerification
            generatedMethod = 'Candidate.__default.F000(bool,long,long):long'
            consumer = $consumerOutput
        }
        safeLocalCall = [ordered]@{
            exitCode = $callExitCode
            verification = $callVerification
            generatedMethods = @('Candidate.__default.F000(long):long', 'Candidate.__default.F001(long):long')
            consumer = $callOutcomeLine
        }
        safeScalarOperators = [ordered]@{
            exitCode = $scalarExitCode
            verification = $scalarVerification
            generatedMethod = 'Candidate.__default.F000(bool,bool):bool'
            consumer = $scalarOutcomeLine
        }
        missingOwnerPrecondition = [ordered]@{
            status = 'Unproven'
            exitCode = $unsafeExitCode
            expectedDiagnostic = "result of operation might violate newtype constraint for 'I64'"
            verification = $unsafeVerification
        }
        readyToRun = [ordered]@{
            runtimeIdentifier = 'win-x64'
            assembly = 'Strogo.Generated.SafeSelect.dll'
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $generatedDll).Hash.ToLowerInvariant()
            bytes = $dllInfo.Length
            nativeHeaderSize = $nativeHeaderSize
        }
    }
    [IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 8), $utf8)
    Write-Host "PASS modules Dafny lowering; $consumerOutput"
    Write-Host "Evidence: $summaryPath"
}
finally {
    Pop-Location
}
