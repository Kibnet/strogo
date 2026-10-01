#requires -Version 7.0
param([Parameter(Mandatory)][string]$ScalarPackageRoot,[Parameter(Mandatory)][string]$AllocationPackageRoot)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $repo ('artifacts/local-validation/g02/loader-lifetime-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out|Out-Null
$dotnet=Join-Path $repo '.tools/dotnet-sdk-10.0.400/dotnet.exe'
$project=Join-Path $PSScriptRoot 'LoaderLifetimeProbe/LoaderLifetimeProbe.csproj'
& $dotnet build $project --configfile "$PSScriptRoot/LoaderLifetimeProbe/NuGet.Config" 2>&1|Tee-Object "$out/build.txt"
if($LASTEXITCODE -ne 0){throw 'Probe build failed'}
$consumer=Join-Path $PSScriptRoot 'LoaderLifetimeProbe/bin/Debug/net10.0/Strogo.Modules.ObserverFixture.dll'
$results=@()
foreach($fixture in @(@{name='scalar';root=$ScalarPackageRoot},@{name='allocation';root=$AllocationPackageRoot})){
 $package=[IO.Path]::GetFullPath($fixture.root)
 $source=Join-Path $package 'content/generated.dll'
 $held=[IO.File]::Open($source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 try{
  $sha=(Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant()
  foreach($mode in @('stream','file-retained','file-released')){
   $name=$fixture.name+'-'+$mode;$copy=Join-Path $out $name
   $start=[Diagnostics.ProcessStartInfo]::new($dotnet)
   $start.UseShellExecute=$false;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
   foreach($arg in @($consumer,$repo,$package,$copy,$mode)){$start.ArgumentList.Add($arg)}
   $start.Environment.Clear()
   foreach($key in @('SystemRoot','WINDIR','TEMP','TMP')){$start.Environment[$key]=[Environment]::GetEnvironmentVariable($key)}
   $start.Environment['DOTNET_ROOT']=Split-Path $dotnet
   $p=[Diagnostics.Process]::Start($start)
   try {
   $stdout=$p.StandardOutput.ReadToEndAsync();$stderr=$p.StandardError.ReadToEndAsync()
   $timedOut=!$p.WaitForExit(20000)
   if($timedOut){$p.Kill($true);if(!$p.WaitForExit(5000)){throw 'Kill timeout'}}
   if(![Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdout,$stderr)).Wait(5000)){throw 'Drain timeout'}
   $process=@{fixture=$fixture.name;mode=$mode;pid=$p.Id;exitCode=$p.ExitCode;timedOut=$timedOut;stdout=$stdout.Result;stderr=$stderr.Result}
   } finally { $p.Dispose() }
   $process|ConvertTo-Json -Depth 5|Set-Content "$out/$name-process.json" -Encoding utf8
   if($timedOut -or $process.exitCode -ne 0 -or $process.stderr -ne ''){throw "Probe failed: $name"}
   $report=$process.stdout|ConvertFrom-Json
   if($report.purpose -ne 'loader-lifetime-physics-not-proof-or-admission' -or $report.mode -ne $mode -or $report.entrySha256 -ne $sha -or !$report.ownerReferenceMatch -or !$report.sourceUnchanged -or !$report.copyUnchanged){throw 'Observation identity mismatch'}
   $results+=@{fixture=$fixture.name;mode=$mode;sha256=$sha;report=$report}
  }
  if((Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant() -ne $sha){throw 'Source fixture changed'}
 }finally{$held.Dispose()}
}
$sources=@()
foreach($file in @($PSCommandPath)+@(Get-ChildItem "$PSScriptRoot/LoaderLifetimeProbe" -File|ForEach-Object FullName)+@("$repo/src/Strogo.Modules/G02CompiledDispatch.cs","$repo/src/Strogo.Modules/G02CompiledFixture.cs","$repo/src/Strogo.Modules/G02PackageSnapshot.cs")){
 $sources+=@{path=[IO.Path]::GetRelativePath($repo,$file).Replace('\','/');sha256=(Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant()}
}
$binaries=@(Get-ChildItem (Split-Path $consumer) -File|ForEach-Object{@{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
@{purpose='isolated-loader-lifetime-preflight';results=$results;sources=$sources;binaries=$binaries}|ConvertTo-Json -Depth 15|Set-Content "$out/receipt.json" -Encoding utf8
Write-Output "Receipt: $out/receipt.json"
