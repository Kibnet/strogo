#requires -Version 7.0
param([Parameter(Mandatory)][string]$ObserverDirectory)
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'Windows x64 required'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $repo ('artifacts/local-validation/g02/native-count-'+[Guid]::NewGuid().ToString('N'))
$observer=Join-Path ([IO.Path]::GetFullPath($ObserverDirectory)) 'Observer.dll'
$dotnet=Join-Path $repo '.tools/dotnet-sdk-10.0.400/dotnet.exe'
New-Item -ItemType Directory -Path "$out/consumer"|Out-Null
$held=[IO.File]::Open($observer,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try {
 $hash=(Get-FileHash -LiteralPath $observer).Hash.ToLowerInvariant()
 Copy-Item -LiteralPath "$PSScriptRoot/CountProbe/Program.cs","$PSScriptRoot/CountProbe/CountProbe.csproj","$PSScriptRoot/CountProbe/NuGet.Config" -Destination "$out/consumer"
 & $dotnet build "$out/consumer/CountProbe.csproj" -c Release --configfile "$out/consumer/NuGet.Config" -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false 2>&1|Tee-Object "$out/build.txt"
 if($LASTEXITCODE -ne 0){throw 'Count probe build failed'}
 $results=@()
 foreach($mode in @('sequential','no-call','parallel','overflow','inactive')) {
  $trace="$out/$mode-trace.jsonl"
  $s=[Diagnostics.ProcessStartInfo]::new($dotnet);$s.UseShellExecute=$false;$s.RedirectStandardOutput=$true;$s.RedirectStandardError=$true
  foreach($a in @("$out/consumer/bin/Release/net10.0/CountProbe.dll",$mode,$observer)){$s.ArgumentList.Add($a)}
  $s.Environment.Clear();foreach($k in @('SystemRoot','WINDIR','TEMP','TMP')){$s.Environment[$k]=[Environment]::GetEnvironmentVariable($k)}
  $s.Environment['DOTNET_ROOT']=Split-Path $dotnet;$s.Environment['DOTNET_ReadyToRun']='0'
  if($mode -ne 'inactive') {
   $s.Environment['CORECLR_ENABLE_PROFILING']='1';$s.Environment['CORECLR_PROFILER']='{486D81E7-F308-4E40-9142-58290B2A6E11}';$s.Environment['CORECLR_PROFILER_PATH']=$observer
   $s.Environment['STROGO_OBSERVER_TRACE']=$trace;$s.Environment['STROGO_OBSERVER_TYPE']='Probe';$s.Environment['STROGO_OBSERVER_METHOD']='Target'
  }
  $p=$null
  try {
   $p=[Diagnostics.Process]::Start($s);$stdout=$p.StandardOutput.ReadToEndAsync();$stderr=$p.StandardError.ReadToEndAsync()
   $timedOut=!$p.WaitForExit(20000)
   if($timedOut){$p.Kill($true);if(!$p.WaitForExit(5000)){throw 'Kill timeout'}}
   if(![Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdout,$stderr)).Wait(5000)){throw 'Output timeout'}
   $process=[ordered]@{mode=$mode;pid=$p.Id;exitCode=$p.ExitCode;timedOut=$timedOut;stdout=$stdout.Result;stderr=$stderr.Result}
  }finally{if($null -ne $p){$p.Dispose()}}
  $process|ConvertTo-Json -Depth 5|Set-Content "$out/$mode-process.json"
  if($process.timedOut -or $process.exitCode -ne 0 -or $process.stderr -ne ''){throw "Count child failed: $mode"}
  $report=$process.stdout|ConvertFrom-Json
  $calls=@{sequential=2;'no-call'=0;parallel=1024;overflow=4097;inactive=0}[$mode]
  if($report.purpose -ne 'native-count-prefix-not-admission' -or $report.mode -ne $mode -or $report.targetCalls -ne $calls){throw 'Report identity mismatch'}
  $expected=@()
  if($mode -eq 'inactive'){$expected+=@{id='inactive';hr=-2147418113;count=0}}
  else {
   $expected+=@{id='null-pointer';hr=-2147467261;count=$null};$expected+=@{id='before';hr=0;count=0}
   switch($mode) {
    sequential {$expected+=@{id='first';hr=0;count=1};$expected+=@{id='second';hr=0;count=2}}
    'no-call' {$expected+=@{id='decoy';hr=0;count=0}}
    parallel {$expected+=@{id='joined';hr=0;count=1024}}
    overflow {$expected+=@{id='capacity';hr=0;count=4096};$expected+=@{id='overflow';hr=-2147467259;count=0}}
   }
  }
  $reads=@($report.rows)
  if($reads.Count -ne $expected.Count){throw 'Read row count mismatch'}
  for($i=0;$i -lt $reads.Count;$i++){if($reads[$i].id -ne $expected[$i].id -or $reads[$i].hresult -ne $expected[$i].hr -or $null -eq $reads[$i].PSObject.Properties['count'] -or $reads[$i].count -ne $expected[$i].count){throw 'Count snapshot mismatch'}}
  if($mode -eq 'inactive') {if(Test-Path -LiteralPath $trace){throw 'Inactive profiler unexpectedly started'};$results+=@{mode=$mode;traceAccepted=$false;controlPassed=$true;reads=$reads};continue}
  $rows=@(Get-Content -LiteralPath $trace|ForEach-Object {$_|ConvertFrom-Json})
  $init=@($rows|Where-Object event -eq initialize);$stop=@($rows|Where-Object event -eq shutdown);$enters=@($rows|Where-Object event -eq enter);$maps=@($rows|Where-Object event -eq map)
  if($init.Count -ne 1 -or $init[0].hresult -ne 0 -or $stop.Count -ne 1 -or @($rows|Where-Object pid -ne $process.pid).Count -ne 0){throw 'Invalid trace identity'}
  $expectedMaps=$(if($calls -eq 0){0}else{1});$expectedEntries=[Math]::Min($calls,4096)
  if($maps.Count -ne $expectedMaps -or $stop[0].entries -ne $expectedEntries){throw 'Trace count mismatch'}
  if($mode -eq 'overflow'){if(!$stop[0].traceFailed -or $enters.Count -ne 0){throw 'Overflow accepted partial trace'}}
  else {
   if($stop[0].traceFailed -or $enters.Count -ne $calls -or $reads[-1].count -ne $enters.Count){throw 'Prefix/final trace mismatch'}
   foreach($entry in $enters){if(@($maps|Where-Object functionId -eq $entry.functionId).Count -ne 1){throw 'Unmapped entry'}}
  }
  $results+=@{mode=$mode;traceAccepted=($mode -ne 'overflow');controlPassed=$true;reads=$reads;entries=$stop[0].entries}
 }
 if((Get-FileHash -LiteralPath $observer).Hash.ToLowerInvariant() -ne $hash){throw 'Observer DLL drift'}
 $sources=foreach($path in @($PSCommandPath,"$PSScriptRoot/Observer.cpp","$PSScriptRoot/Enter.asm","$PSScriptRoot/Observer.def")+@(Get-ChildItem -LiteralPath "$PSScriptRoot/CountProbe" -File|ForEach-Object FullName)){@{path=[IO.Path]::GetRelativePath($repo,$path).Replace('\','/');sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}}
 $binaries=foreach($path in @($observer)+@(Get-ChildItem -LiteralPath "$out/consumer/bin/Release/net10.0" -File|ForEach-Object FullName)){@{path=[IO.Path]::GetRelativePath($repo,$path).Replace('\','/');sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}}
 @{purpose='quiescent-native-entry-count-not-signed-admission';results=$results;sources=@($sources);binaries=@($binaries);limits=@('No signed fixture session/native file identity in this probe','Snapshot, not concurrent lease','Final trace failure invalidates earlier successful counts','Timeout/Shutdown race branches not exercised')}|ConvertTo-Json -Depth 12|Set-Content "$out/receipt.json"
 Write-Output "Receipt: $out/receipt.json"
}finally{$held.Dispose()}
