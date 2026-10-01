#requires -Version 7.0
param([Parameter(Mandatory)][string]$ScalarDescriptor,[Parameter(Mandatory)][string]$AllocationDescriptor,[Parameter(Mandatory)][string]$ObserverDirectory)
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'Windows x64 required'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $repo ('artifacts/local-validation/g02/signed-native-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out|Out-Null
$dotnet=Join-Path $repo '.tools/dotnet-sdk-10.0.400/dotnet.exe'
$observer=Join-Path ([IO.Path]::GetFullPath($ObserverDirectory)) 'Observer.dll'
$cache=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages'
if('Strogo.SignedObserver.BoundedOutput' -as [type]){throw 'Fresh PowerShell process required for bounded reader'}
Add-Type -Path (Join-Path $PSScriptRoot 'BoundedProcessOutput.cs')
$held=@();$inputs=@()
function Hold([string]$path,[long]$maximum) {
 $path=[IO.Path]::GetFullPath($path)
 $file=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 if($file.Length -gt $maximum){$file.Dispose();throw 'Input limit'}
 $script:held+=@{path=$path;stream=$file}
 $script:inputs+=@{path=[IO.Path]::GetRelativePath($repo,$path).Replace('\','/');sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant();length=$file.Length}
 return $path
}
function DomainHash([string]$domain,[string]$path) {
 $hash=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
 try{$hash.AppendData([Text.Encoding]::UTF8.GetBytes($domain+"`n"));$hash.AppendData([IO.File]::ReadAllBytes($path));return [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()}finally{$hash.Dispose()}
}
try {
 $observer=Hold $observer 16777216
 & $dotnet build "$PSScriptRoot/SignedObserverFixture/SignedObserverFixture.csproj" --configfile "$PSScriptRoot/SignedObserverFixture/NuGet.Config" -p:RestoreLockedMode=true 2>&1|Tee-Object "$out/build.txt"
 if($LASTEXITCODE -ne 0){throw 'Consumer build failed'}
 $consumer="$PSScriptRoot/SignedObserverFixture/bin/Debug/net10.0/Strogo.Modules.ObserverFixture.dll"
 $results=@()
 foreach($fixture in @(@{shape='scalar';descriptor=$ScalarDescriptor},@{shape='allocation';descriptor=$AllocationDescriptor})) {
  $descriptorPath=Hold $fixture.descriptor 65536
  $descriptor=Get-Content -LiteralPath $descriptorPath -Raw|ConvertFrom-Json
  if($descriptor.schemaVersion -ne 'strogo.signed-observer-fixture.v0.1' -or $descriptor.purpose -ne 'disposable-test-key-not-public-admission'){throw 'Fixture descriptor purpose'}
  $configPath=Hold $descriptor.operatorConfigPath 65536;$config=Get-Content -LiteralPath $configPath -Raw|ConvertFrom-Json
  $null=Hold $config.publicKeyPath 16384
  foreach($field in @('releasePath','badReleasePath','initialStatePath','nextStatePath')){$null=Hold $descriptor.$field 65536}
  if((Get-FileHash -LiteralPath (Join-Path $config.ownerStateStore 'owner-state.json')).Hash -ne (Get-FileHash -LiteralPath $descriptor.initialStatePath).Hash){throw 'Fixture state is not initial; create fresh fixture inputs'}
  $package=[IO.Path]::GetFullPath($descriptor.packagePath)
  $manifestPath=Hold (Join-Path $package 'build-manifest.json') 1048576;$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
  function RolePath([string]$role){$row=@($manifest.files|Where-Object role -eq $role);if($row.Count -ne 1){throw 'Manifest role'};return Join-Path $package $row[0].path}
  $entryPath=Hold (RolePath 'entry-assembly') 16777216;$mapPath=Hold (RolePath 'source-map') 1048576;$modulePath=Hold (RolePath 'module') 1048576
  $module=Get-Content -LiteralPath $modulePath -Raw|ConvertFrom-Json;$map=Get-Content -LiteralPath $mapPath -Raw|ConvertFrom-Json
  if(@($module.exports).Count -ne 1 -or $manifest.bundleDigest -ne $descriptor.bundleDigest){throw 'Export/bundle profile'}
  $functionId=$module.exports[0];$function=@($module.functions|Where-Object id -eq $functionId)
  if($function.Count -ne 1 -or @($function[0].parameters).Count -ne $(if($fixture.shape -eq 'scalar'){1}else{2})){throw 'Function shape'}
  $f=@($map|Where-Object entityId -eq ('function/'+$functionId));$q=@($map|Where-Object entityId -eq ('owner/contract/'+$function[0].contractRef+'/requires'))
  if($f.Count -ne 1 -or $q.Count -ne 1 -or $f[0].generatedName -notmatch '^F[0-9]+$' -or $q[0].generatedName -notmatch '^Q[0-9]+$'){throw 'Source map Q/F'}
  $entrySha=(Get-FileHash -LiteralPath $entryPath).Hash.ToLowerInvariant();$mapSha=(Get-FileHash -LiteralPath $mapPath).Hash.ToLowerInvariant()
  $buildDigest=DomainHash 'strogo.build-manifest.v0.2/artifact' $manifestPath;$releaseDigest=DomainHash 'strogo.admission.v0.2/artifact' $descriptor.releasePath
  $shape=$fixture.shape;$childRoot=Join-Path $out $shape;New-Item -ItemType Directory -Path $childRoot|Out-Null;$trace=Join-Path $childRoot 'trace.jsonl'
  $s=[Diagnostics.ProcessStartInfo]::new($dotnet);$s.UseShellExecute=$false;$s.RedirectStandardOutput=$true;$s.RedirectStandardError=$true
  foreach($a in @($consumer,$repo,$descriptorPath,$observer,$childRoot,$cache)){$s.ArgumentList.Add($a)}
  $s.Environment.Clear();foreach($k in @('SystemRoot','WINDIR','TEMP','TMP')){$s.Environment[$k]=[Environment]::GetEnvironmentVariable($k)}
  $s.Environment['DOTNET_ROOT']=Split-Path $dotnet;$s.Environment['DOTNET_ReadyToRun']='0'
  $s.Environment['CORECLR_ENABLE_PROFILING']='1';$s.Environment['CORECLR_PROFILER']='{486D81E7-F308-4E40-9142-58290B2A6E11}';$s.Environment['CORECLR_PROFILER_PATH']=$observer
  $s.Environment['STROGO_OBSERVER_TRACE']=$trace;$s.Environment['STROGO_OBSERVER_TYPE']='Candidate.__default';$s.Environment['STROGO_OBSERVER_METHOD']=$f[0].generatedName;$s.Environment['STROGO_OBSERVER_METHOD_2']=$q[0].generatedName;$s.Environment['STROGO_OBSERVER_FILE_IDENTITY']='1'
  $requestPath=Join-Path $childRoot 'launch-request.json';$processPath=Join-Path $childRoot 'contained-process.json'
  $environment=@{};foreach($pair in $s.Environment.GetEnumerator()){$environment[$pair.Key]=$pair.Value}
  @{FileName=$dotnet;WorkingDirectory=$repo;Arguments=@($s.ArgumentList);Environment=$environment}|ConvertTo-Json -Depth 8|Set-Content $requestPath
  & $dotnet $consumer --contained-launch $requestPath $processPath 2>&1|Tee-Object "$childRoot/launcher.txt"
  if($LASTEXITCODE -ne 0){throw 'Contained launch failed'}
  $process=Get-Content -LiteralPath $processPath -Raw|ConvertFrom-Json
  $process|ConvertTo-Json -Depth 8|Set-Content "$childRoot/process.json"
  if($process.timedOut -or $process.exitCode -ne 0 -or $process.stderr -ne '' -or $process.stdout.Length -gt 1048576){throw "Child failed: $shape"}
  $report=$process.stdout|ConvertFrom-Json
  if($report.purpose -ne 'signed-fixture-native-observer-not-public-admission' -or !$report.validationOnly -or $report.functionId -ne $functionId -or $report.packageDigest -ne $manifest.packageDigest -or $report.buildManifestDigest -ne $buildDigest -or $report.contractApprovalDigest -ne $manifest.contractApprovalDigest -or $report.releaseAdmissionDigest -ne $releaseDigest -or $report.entrySha256 -ne $entrySha -or $report.sourceMapSha256 -ne $mapSha -or $report.entryLength -ne (Get-Item -LiteralPath $entryPath).Length -or $report.generatedLoads -ne 1){throw 'Signed/native identity mismatch'}
  $traceRows=@(Get-Content -LiteralPath $trace|ForEach-Object {$_|ConvertFrom-Json});$init=@($traceRows|Where-Object event -eq initialize);$stop=@($traceRows|Where-Object event -eq shutdown);$maps=@($traceRows|Where-Object event -eq map);$files=@($traceRows|Where-Object event -eq 'module-file');$enters=@($traceRows|Where-Object event -eq enter)
  if($init.Count -ne 1 -or $init[0].hresult -ne 0 -or $stop.Count -ne 1 -or $stop[0].traceFailed -or @($traceRows|Where-Object pid -ne $process.pid).Count -ne 0 -or $stop[0].entries -ne $enters.Count -or $report.finalNativeCount -ne $enters.Count){throw 'Trace terminal identity'}
  $methods=@($report.methods);if($methods.Count -ne 2 -or @($methods|Where-Object {$_.kind -eq 'Q' -and $_.symbol -eq $q[0].generatedName}).Count -ne 1 -or @($methods|Where-Object {$_.kind -eq 'F' -and $_.symbol -eq $f[0].generatedName}).Count -ne 1 -or $maps.Count -ne 2 -or $files.Count -ne 2){throw 'Metadata descriptors'}
  $joined=@{};$moduleIds=@()
  foreach($mapped in $maps){
   $method=@($methods|Where-Object {$_.mvid -eq $mapped.mvid -and $_.token -eq $mapped.token});$file=@($files|Where-Object {$_.moduleId -eq $mapped.moduleId -and $_.functionId -eq $mapped.functionId})
   if($method.Count -ne 1 -or $file.Count -ne 1 -or !$file[0].available -or $file[0].sha256 -ne $entrySha -or $file[0].length -ne $report.entryLength -or $joined.ContainsKey([string]$mapped.functionId)){throw 'Native module-file binding'}
   $joined[[string]$mapped.functionId]=$method[0].kind;$moduleIds+=@($mapped.moduleId)
  }
  if(@($moduleIds|Sort-Object -Unique).Count -ne 1){throw 'Mixed candidate modules'}
  $observed=@();foreach($entered in $enters){if(!$joined.ContainsKey([string]$entered.functionId)){throw 'Unmapped entry'};$observed+=@($joined[[string]$entered.functionId])}
  $ids=@('fresh-replay','bad-release-before-load','open','valid','malformed','typed-invalid','requires-invalid','provider-failure','runtime-mismatch','release-expired','restored','epoch','rollback','disposed','disposed-malformed')
  $codes=@{malformed='SchemaInvalid';'typed-invalid'='InputTypeMismatch';'requires-invalid'='CompiledPreconditionFailed';'provider-failure'='OwnerStateUnavailable';'runtime-mismatch'='RuntimeBindingMismatch';'release-expired'='ReleaseAdmissionExpired';epoch='ApprovalEpochMismatch';rollback='OwnerStateRollbackDetected';disposed='CompiledFixtureDisposed';'disposed-malformed'='CompiledFixtureDisposed'}
  $calls=@($report.rows);if($calls.Count -ne $ids.Count){throw 'Missing scenario'};$cursor=0
  for($i=0;$i -lt $calls.Count;$i++){
   $row=$calls[$i];$id=$ids[$i];$expected=@();if($id -in @('valid','restored')){$expected=@('Q','F')}elseif($id -eq 'requires-invalid'){$expected=@('Q')}
   if($row.id -ne $id -or $row.before -ne $cursor -or $row.after -lt $row.before -or $row.after -gt $enters.Count -or $row.after-$row.before -ne $expected.Count -or (@($row.expectedNativeKinds)-join ',') -ne ($expected-join ',')){throw 'Interval partition/expected kinds'}
   $slice=@();for($j=[int]$row.before;$j -lt [int]$row.after;$j++){$slice+=@($observed[$j])}
   if(($slice-join ',') -ne ($expected-join ',')){throw 'Native interval order mismatch'}
   if($id -eq 'bad-release-before-load'){if($row.refusal -ne 'InvalidOwnerSignature' -or $row.stage -ne 'admission' -or $row.assemblyLoads -ne 0 -or $row.providerReadsDelta -ne 1){throw 'Bad admission control'}}
   elseif($id -eq 'open'){if($row.assemblyLoads -ne 1 -or $row.location -ne $entryPath -or $row.providerReadsDelta -ne 1){throw 'Open control'}}
   elseif($id -ne 'fresh-replay'){
    $response=$row.response;$nested=$response.result
    if($response.packageDigest -ne $manifest.packageDigest -or $response.buildManifestDigest -ne $buildDigest -or $response.contractApprovalDigest -ne $manifest.contractApprovalDigest -or $response.releaseAdmissionDigest -ne $releaseDigest -or !$response.validationOnly){throw 'Response identity'}
    if($codes.ContainsKey($id)){$stage=$(if($id -in @('malformed','typed-invalid')){'invoke'}elseif($id -in @('requires-invalid','runtime-mismatch','disposed','disposed-malformed')){'package'}else{'admission'});if($nested.status -ne 'Refused' -or $nested.error.code -ne $codes[$id] -or $nested.error.stage -ne $stage){throw 'Refusal response'}}elseif($nested.status -ne 'Returned'){throw 'Return response'}
    $reads=$(if($id -in @('malformed','typed-invalid','disposed','disposed-malformed')){0}else{1});$attempts=$(if($expected.Count -gt 0){1}else{0})
    $runtimeReads=$(if($expected.Count -gt 0 -or $id -eq 'runtime-mismatch'){1}else{0});if($row.providerReadsDelta -ne $reads -or $row.dispatchAttemptsDelta -ne $attempts -or $row.runtimeReadsDelta -ne $runtimeReads){throw 'Provider/dispatch delta'}
   }
   $cursor=[int]$row.after
  }
  if($cursor -ne $enters.Count -or $cursor -ne 5){throw 'Extra native entries'}
  $results+=@{shape=$shape;accepted=$true;packageDigest=$manifest.packageDigest;buildManifestDigest=$buildDigest;contractApprovalDigest=$manifest.contractApprovalDigest;releaseAdmissionDigest=$releaseDigest;entrySha256=$entrySha;sourceMapSha256=$mapSha;rows=$calls.Count;nativeEntries=$cursor;observed=$observed}
 }
 foreach($item in $inputs){$path=Join-Path $repo $item.path;if((Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $item.sha256){throw 'Immutable input drift'}}
 $sources=foreach($path in @($PSCommandPath,"$repo/tests/Strogo.Modules.Admission.Conformance/G02SignedSessionChecks.cs","$repo/src/Strogo.Modules/G02SignedFixtureSession.cs","$repo/src/Strogo.Modules/G02CompiledFixture.cs","$repo/src/Strogo.Modules/OwnerHostContext.cs","$PSScriptRoot/Observer.cpp","$PSScriptRoot/Enter.asm","$PSScriptRoot/BoundedProcessOutput.cs")+@(Get-ChildItem -LiteralPath "$PSScriptRoot/SignedObserverFixture" -File|ForEach-Object FullName)){@{path=[IO.Path]::GetRelativePath($repo,$path).Replace('\','/');sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}}
 $binaries=foreach($path in @($observer)+@(Get-ChildItem -LiteralPath (Split-Path $consumer) -File|ForEach-Object FullName)){@{path=[IO.Path]::GetRelativePath($repo,$path).Replace('\','/');sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}}
 @{purpose='signed-fixture-native-file-and-interval-binding-not-public-admission';results=$results;inputs=$inputs;sources=@($sources);binaries=@($binaries);limits=@('Disposable test keys and fixed test clock','Forced JIT and CLR-reported immutable file artifact, not mapped/R2R instruction digest','Synthetic provider/runtime/expiry controls; actual fixture state File.Replace epoch/rollback','No public closure/human release/D02/G05/G06 claim','Timeout/kill branch not exercised')}|ConvertTo-Json -Depth 14|Set-Content "$out/receipt.json"
 Write-Output "Receipt: $out/receipt.json"
}finally{foreach($item in $held){$item.stream.Dispose()}}


