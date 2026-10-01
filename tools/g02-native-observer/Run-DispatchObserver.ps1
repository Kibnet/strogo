#requires -Version 7.0
param([Parameter(Mandatory)][string]$ScalarPackageRoot,[Parameter(Mandatory)][string]$AllocationPackageRoot,[Parameter(Mandatory)][string]$ObserverDirectory,[switch]$JsonTransport)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $repo ('artifacts/local-validation/g02/dispatch-observer-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $out|Out-Null
$dotnet=Join-Path $repo '.tools/dotnet-sdk-10.0.400/dotnet.exe'
& $dotnet build "$PSScriptRoot/ObserverFixture/ObserverFixture.csproj" -c Debug --configfile "$PSScriptRoot/ObserverFixture/NuGet.Config" 2>&1|Tee-Object "$out/managed-build.txt"
if($LASTEXITCODE -ne 0){throw 'Consumer build failed'}
$consumer="$PSScriptRoot/ObserverFixture/bin/Debug/net10.0/Strogo.Modules.ObserverFixture.dll"
$observer=Join-Path ([IO.Path]::GetFullPath($ObserverDirectory)) 'Observer.dll'
$results=@()
foreach($fixture in @(@{name='scalar';root=$ScalarPackageRoot},@{name='allocation';root=$AllocationPackageRoot})) {
 $package=[IO.Path]::GetFullPath($fixture.root)
 $dll=Join-Path $package 'content/generated.dll';$mapPath=Join-Path $package 'content/source-map.json'
 $held=[IO.File]::Open($dll,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read);$heldMap=$null
 try {
  $heldMap=[IO.File]::Open($mapPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
  $module=Get-Content -Raw "$package/content/module.json"|ConvertFrom-Json
  if(@($module.exports).Count -ne 1){throw 'Diagnostic vector profile requires one export'}
  $functionId=$module.exports[0];$function=@($module.functions|Where-Object id -eq $functionId)
  if($function.Count -ne 1){throw 'Export missing'}
  $map=Get-Content -Raw $mapPath|ConvertFrom-Json
  $f=@($map|Where-Object entityId -eq ('function/'+$functionId));$q=@($map|Where-Object entityId -eq ('owner/contract/'+$function[0].contractRef+'/requires'))
  if($f.Count -ne 1 -or $q.Count -ne 1 -or $f[0].generatedName -notmatch '^F[0-9]+$' -or $q[0].generatedName -notmatch '^Q[0-9]+$'){throw 'Q/F source map missing'}
  $dllHash=(Get-FileHash $dll).Hash.ToLowerInvariant();$mapHash=(Get-FileHash $mapPath).Hash.ToLowerInvariant()
  foreach($mode in @('valid','requires-invalid','typed-invalid')) {
   $name=$fixture.name+'-'+$mode;$trace="$out/$name-trace.jsonl"
   $s=[Diagnostics.ProcessStartInfo]::new($dotnet);$s.UseShellExecute=$false;$s.RedirectStandardOutput=$true;$s.RedirectStandardError=$true
   foreach($a in @($consumer,$package,$mode,$functionId)){$s.ArgumentList.Add($a)}
   if($JsonTransport){$s.ArgumentList.Add('json')}
   $s.Environment.Clear();foreach($k in @('SystemRoot','WINDIR','TEMP','TMP')){$s.Environment[$k]=[Environment]::GetEnvironmentVariable($k)}
   $s.Environment['CORECLR_ENABLE_PROFILING']='1';$s.Environment['CORECLR_PROFILER']='{486D81E7-F308-4E40-9142-58290B2A6E11}';$s.Environment['CORECLR_PROFILER_PATH']=$observer
   $s.Environment['STROGO_OBSERVER_TRACE']=$trace;$s.Environment['STROGO_OBSERVER_TYPE']='Candidate.__default';$s.Environment['STROGO_OBSERVER_METHOD']=$f[0].generatedName;$s.Environment['STROGO_OBSERVER_METHOD_2']=$q[0].generatedName
   $s.Environment['STROGO_OBSERVER_FILE_IDENTITY']='1';$s.Environment['DOTNET_ReadyToRun']='0'
   $timer=[Diagnostics.Stopwatch]::StartNew();$p=[Diagnostics.Process]::Start($s);$stdout=$p.StandardOutput.ReadToEndAsync();$stderr=$p.StandardError.ReadToEndAsync()
   $timedOut=!$p.WaitForExit(20000);if($timedOut){$p.Kill($true);if(!$p.WaitForExit(5000)){$p.Dispose();throw 'Kill timeout'}}
   if(![Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdout,$stderr)).Wait(5000)){$p.Dispose();throw 'Output timeout'}
   $process=[ordered]@{fixture=$fixture.name;mode=$mode;pid=$p.Id;exitCode=$p.ExitCode;stdout=$stdout.Result;stderr=$stderr.Result;timedOut=$timedOut;elapsedMs=$timer.ElapsedMilliseconds};$p.Dispose()
   $process|ConvertTo-Json|Set-Content "$out/$name-process.json"
   if($timedOut -or $process.exitCode -ne 0 -or $process.stderr -ne ''){throw "Child failed: $name"}
   $expected=$process.stdout|ConvertFrom-Json
   if($expected.purpose -ne 'retained-fixture-dispatch-not-admitted' -or $expected.entrySha256 -ne $dllHash -or $expected.functionId -ne $functionId -or $expected.mode -ne $mode -or $expected.transport -ne $(if($JsonTransport){'json'}else{'typed'})){throw 'Consumer identity mismatch'}
   $descriptors=@($expected.methods);if($descriptors.Count -ne 2 -or @($descriptors|Where-Object {$_.kind -eq 'Q' -and $_.symbol -eq $q[0].generatedName}).Count -ne 1 -or @($descriptors|Where-Object {$_.kind -eq 'F' -and $_.symbol -eq $f[0].generatedName}).Count -ne 1){throw 'Descriptor mismatch'}
   $sequence=@();switch($mode){valid {foreach($row in $expected.rows){if($null -ne $row.refusal){throw 'Valid input refused'};$sequence+=@('Q','F')}} requires-invalid {$sequence=@('Q');if(@($expected.rows).Count -ne 1 -or $expected.rows[0].refusal -ne 'CompiledPreconditionFailed'){throw 'Precondition control mismatch'}} typed-invalid {if(@($expected.rows).Count -ne 1 -or $expected.rows[0].refusal -ne $(if($JsonTransport){'InputTypeMismatch'}else{'CompiledInputMismatch'})){throw 'Type control mismatch'}}}
   if(($sequence -join ',') -ne (@($expected.expectedEnters) -join ',')){throw 'Consumer sequence mismatch'}
   $rows=@(Get-Content $trace|ForEach-Object {$_|ConvertFrom-Json});$init=@($rows|Where-Object event -eq initialize);$stop=@($rows|Where-Object event -eq shutdown);$maps=@($rows|Where-Object event -eq map);$files=@($rows|Where-Object event -eq 'module-file');$enters=@($rows|Where-Object event -eq enter)
   if($init.Count -ne 1 -or $init[0].hresult -ne 0 -or $stop.Count -ne 1 -or $stop[0].traceFailed -or @($rows|Where-Object pid -ne $process.pid).Count -ne 0 -or $stop[0].entries -ne $enters.Count){throw 'Invalid trace'}
   $joined=@{};$moduleIds=@()
   foreach($mapped in $maps) {
    $descriptor=@($descriptors|Where-Object {$_.mvid -eq $mapped.mvid -and $_.token -eq $mapped.token})
    if($descriptor.Count -ne 1 -or $joined.ContainsKey([string]$mapped.functionId)){throw 'Unknown or duplicate mapping'}
    $file=@($files|Where-Object {$_.moduleId -eq $mapped.moduleId -and $_.functionId -eq $mapped.functionId})
    if($file.Count -ne 1 -or !$file[0].available -or $file[0].sha256 -ne $dllHash -or $file[0].length -ne $held.Length){throw 'File identity mismatch'}
    $joined[[string]$mapped.functionId]=$descriptor[0].kind;$moduleIds+=@($mapped.moduleId)
   }
   if($files.Count -ne $maps.Count -or @($moduleIds|Sort-Object -Unique).Count -gt 1 -or $maps.Count -ne @($sequence|Sort-Object -Unique).Count){throw 'Mixed or missing mappings'}
   $observed=@();foreach($entered in $enters){if(!$joined.ContainsKey([string]$entered.functionId)){throw 'Unmapped entry'};$observed+=@($joined[[string]$entered.functionId])}
   if(($observed -join ',') -ne ($sequence -join ',')){throw 'Q/F entry sequence mismatch'}
   $results+=@{fixture=$fixture.name;mode=$mode;vectors=@($expected.rows).Count;entrySha256=$dllHash;sourceMapSha256=$mapHash;expected=$sequence;observed=$observed;accepted=$true}
  }
  if((Get-FileHash $dll).Hash.ToLowerInvariant() -ne $dllHash -or (Get-FileHash $mapPath).Hash.ToLowerInvariant() -ne $mapHash){throw 'Fixture drift'}
 } finally {if($heldMap){$heldMap.Dispose()};$held.Dispose()}
}
$sources=@();foreach($file in @($PSCommandPath,"$PSScriptRoot/Observer.cpp","$PSScriptRoot/Enter.asm","$PSScriptRoot/ModuleFile.inc")+@(Get-ChildItem "$PSScriptRoot/ObserverFixture" -File|ForEach-Object FullName)+@("$repo/src/Strogo.Modules/G02CompiledDispatch.cs","$repo/src/Strogo.Modules/G02CompiledFixture.cs","$repo/src/Strogo.Modules/Properties/AssemblyInfo.cs","$repo/src/Strogo.Modules/G02InvocationCodec.cs","$repo/src/Strogo.Modules/OwnerBundleParser.cs")){$sources+=@{path=[IO.Path]::GetRelativePath($repo,$file).Replace('\','/');sha256=(Get-FileHash $file).Hash.ToLowerInvariant()}}
$binaries=@();foreach($file in @($observer)+@(Get-ChildItem (Split-Path $consumer) -File|ForEach-Object FullName)){$binaries+=@{name=[IO.Path]::GetFileName($file);sha256=(Get-FileHash $file).Hash.ToLowerInvariant()}}
@{purpose='shared-dispatch-Q-before-F-retained-fixtures';transport=$(if($JsonTransport){'json'}else{'typed'});results=$results;sources=$sources;binaries=$binaries;limits=@('Structural/source regeneration over retained fixtures; no fresh proof or public admission in this consumer','Two fixture vector profiles; no full language/ABI generality','Forced JIT; held CLR-reported file identity, not native R2R or mapped-memory identity')}|ConvertTo-Json -Depth 10|Set-Content "$out/receipt.json"
Write-Output "Receipt: $out/receipt.json"
