#requires -Version 7.0
param([Parameter(Mandatory)][string]$PackageRoot,[Parameter(Mandatory)][string]$ObserverDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $repo ('artifacts/local-validation/g02/generated-observer-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $out|Out-Null
$package=[IO.Path]::GetFullPath($PackageRoot);$observer=[IO.Path]::GetFullPath($ObserverDirectory)
# Retained trusted fixtures only: no public package admission is performed here.
$dll=Join-Path $package 'content/generated.dll';$mapPath=Join-Path $package 'content/source-map.json'
$held=[IO.File]::Open($dll,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
$heldMap=[IO.File]::Open($mapPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try {
 $map=Get-Content -Raw -LiteralPath $mapPath|ConvertFrom-Json
 $symbolRows=@($map|Where-Object entityId -eq 'function/addOne');if($symbolRows.Count -ne 1 -or $symbolRows[0].generatedName -notmatch '^F[0-9]+$'){throw 'FixtureSymbolMissing'}
 $symbol=$symbolRows[0].generatedName
 $dllHash=(Get-FileHash -LiteralPath $dll).Hash.ToLowerInvariant();$mapHash=(Get-FileHash -LiteralPath $mapPath).Hash.ToLowerInvariant()
 $consumer=Join-Path $out 'consumer';New-Item -ItemType Directory $consumer|Out-Null
 Copy-Item -LiteralPath "$PSScriptRoot/GeneratedProbe/Program.cs","$PSScriptRoot/GeneratedProbe/GeneratedProbe.csproj","$PSScriptRoot/GeneratedProbe/NuGet.Config" -Destination $consumer
 $dotnet=Join-Path $repo '.tools/dotnet-sdk-10.0.400/dotnet.exe'
 & $dotnet build "$consumer/GeneratedProbe.csproj" -c Release --configfile "$consumer/NuGet.Config" -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false 2>&1|Tee-Object "$out/managed-build.txt"
 if($LASTEXITCODE -ne 0){throw 'Build failed'}
 $results=@()
 foreach($mode in @('call','no-call','decoy')) {
  $trace="$out/$mode-trace.jsonl";$s=[Diagnostics.ProcessStartInfo]::new($dotnet);$s.UseShellExecute=$false;$s.RedirectStandardOutput=$true;$s.RedirectStandardError=$true
  foreach($a in @("$consumer/bin/Release/net10.0/GeneratedProbe.dll",$dll,$symbol,$mode)){$s.ArgumentList.Add($a)}
  $s.Environment.Clear();foreach($k in @('SystemRoot','WINDIR','TEMP','TMP')){$s.Environment[$k]=[Environment]::GetEnvironmentVariable($k)}
  $s.Environment['CORECLR_ENABLE_PROFILING']='1';$s.Environment['CORECLR_PROFILER']='{486D81E7-F308-4E40-9142-58290B2A6E11}';$s.Environment['CORECLR_PROFILER_PATH']=(Join-Path $observer 'Observer.dll')
  $s.Environment['STROGO_OBSERVER_TRACE']=$trace;$s.Environment['STROGO_OBSERVER_TYPE']='Candidate.__default';$s.Environment['STROGO_OBSERVER_METHOD']=$symbol;$s.Environment['DOTNET_ReadyToRun']='0'
  $p=[Diagnostics.Process]::Start($s);$stdout=$p.StandardOutput.ReadToEndAsync();$stderr=$p.StandardError.ReadToEndAsync()
  if(!$p.WaitForExit(20000)){$p.Kill($true);$null=$p.WaitForExit(5000);$p.Dispose();throw 'Child timeout'}
  if(![Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdout,$stderr)).Wait(5000)){$p.Dispose();throw 'Output timeout'}
  $process=[ordered]@{mode=$mode;pid=$p.Id;exitCode=$p.ExitCode;stdout=$stdout.Result;stderr=$stderr.Result};$p.Dispose()
  $process|ConvertTo-Json|Set-Content "$out/$mode-process.json"
  if($process.exitCode -ne 0 -or $process.stderr -ne ''){throw "Child failed: $mode"}
  $expected=$process.stdout|ConvertFrom-Json;if($expected.sha256 -ne $dllHash -or $expected.output -ne 42){throw 'Consumer mismatch'}
  $rows=@(Get-Content -LiteralPath $trace|ForEach-Object {$_|ConvertFrom-Json});$init=@($rows|Where-Object event -eq initialize);$stop=@($rows|Where-Object event -eq shutdown);$maps=@($rows|Where-Object event -eq map);$enters=@($rows|Where-Object event -eq enter)
  if($init.Count -ne 1 -or $init[0].hresult -ne 0 -or $stop.Count -ne 1 -or $stop[0].traceFailed -or @($rows|Where-Object pid -ne $process.pid).Count -ne 0){throw 'Invalid trace'}
  $accepted=@($maps|Where-Object {$_.mvid -eq $expected.mvid -and $_.token -eq $expected.token})
  if($mode -eq 'no-call'){if($maps.Count -ne 0 -or $enters.Count -ne 0 -or $stop[0].entries -ne 0){throw 'No-call falsely observed'}}
  else {
   if($maps.Count -ne 1 -or $enters.Count -ne 1 -or $stop[0].entries -ne 1 -or $maps[0].functionId -ne $enters[0].functionId){throw 'Entry join mismatch'}
   if($mode -eq 'call' -and $accepted.Count -ne 1){throw 'GeneratedMetadataMismatch'}
   if($mode -eq 'decoy' -and $accepted.Count -ne 0){throw 'Decoy falsely accepted'}
  }
  $wrongMvid=@($maps|Where-Object {$_.mvid -eq '00000000-0000-0000-0000-000000000000' -and $_.token -eq $expected.token})
  $wrongToken=@($maps|Where-Object {$_.mvid -eq $expected.mvid -and $_.token -eq ($expected.token -bxor 1)})
  if($wrongMvid.Count -ne 0 -or $wrongToken.Count -ne 0){throw 'Wrong metadata accepted'}
  $results+=@{mode=$mode;output=$expected.output;expectedMvid=$expected.mvid;expectedToken=$expected.token;observedMaps=$maps;metadataAccepted=($accepted.Count -eq 1);wrongMvidAccepted=$wrongMvid.Count;wrongTokenAccepted=$wrongToken.Count}
 }
 if((Get-FileHash -LiteralPath $dll).Hash.ToLowerInvariant() -ne $dllHash -or (Get-FileHash -LiteralPath $mapPath).Hash.ToLowerInvariant() -ne $mapHash){throw 'Held fixture drift'}
 $sources=@();foreach($file in @($PSCommandPath,"$PSScriptRoot/Observer.cpp","$PSScriptRoot/Enter.asm")+@(Get-ChildItem "$PSScriptRoot/GeneratedProbe" -File|ForEach-Object FullName)){$sources+=@{name=[IO.Path]::GetFileName($file);sha256=(Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant()}}
 [ordered]@{purpose='generated-scalar-profiler-metadata-join';entrySha256=$dllHash;sourceMapSha256=$mapHash;entity='function/addOne';symbol=$symbol;observerSha256=(Get-FileHash (Join-Path $observer 'Observer.dll')).Hash.ToLowerInvariant();consumerSha256=(Get-FileHash "$consumer/bin/Release/net10.0/GeneratedProbe.dll").Hash.ToLowerInvariant();sources=$sources;results=$results;limits=@('Fixture only, no admission or Q dispatch','MVID/token are not a loaded-image byte digest','Source map provenance belongs to prior trusted replay','Forced JIT, not native R2R evidence','Scalar fixture, not full ABI generality')}|ConvertTo-Json -Depth 10|Set-Content "$out/receipt.json"
 Write-Output "Receipt: $out/receipt.json"
} finally { $heldMap.Dispose();$held.Dispose() }
