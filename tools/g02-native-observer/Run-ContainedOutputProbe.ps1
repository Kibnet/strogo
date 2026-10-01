#requires -Version 7.0
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $repo ('artifacts/local-validation/g02/contained-output-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory $out|Out-Null
$dotnet=Join-Path $repo '.tools/dotnet-sdk-10.0.400/dotnet.exe';$consumer=Join-Path $PSScriptRoot 'SignedObserverFixture/bin/Debug/net10.0/Strogo.Modules.ObserverFixture.dll'
$rows=@()
foreach($mode in @('normal','stdout','stderr','orphan')) {
 $dir=Join-Path $out $mode;New-Item -ItemType Directory $dir|Out-Null;$request=Join-Path $dir 'request.json';$result=Join-Path $dir 'result.json';$pidFile=Join-Path $dir 'descendant.pid'
 $arguments=@($consumer,'--output-probe',$mode);if($mode -eq 'orphan'){$arguments=@($consumer,'--orphan-parent',$pidFile)}
 $environment=@{};foreach($k in @('SystemRoot','WINDIR','TEMP','TMP')){$environment[$k]=[Environment]::GetEnvironmentVariable($k)};$environment['DOTNET_ROOT']=Split-Path $dotnet
 @{FileName=$dotnet;WorkingDirectory=$repo;Arguments=$arguments;Environment=$environment}|ConvertTo-Json -Depth 8|Set-Content $request
 $start=[Diagnostics.ProcessStartInfo]::new($dotnet);$start.UseShellExecute=$false;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 foreach($a in @($consumer,'--contained-launch',$request,$result)){$start.ArgumentList.Add($a)}
 $launcher=[Diagnostics.Process]::Start($start);$stdout=$launcher.StandardOutput.ReadToEndAsync();$stderr=$launcher.StandardError.ReadToEndAsync();$live=$false;$descendant=$null
 try {
  if($mode -eq 'orphan') {
   $timer=[Diagnostics.Stopwatch]::StartNew();while(!(Test-Path -LiteralPath $pidFile)){if($timer.Elapsed.TotalSeconds -gt 3){throw 'No descendant pid'};Start-Sleep -Milliseconds 20}
   $descendant=[Diagnostics.Process]::GetProcessById([int](Get-Content -LiteralPath $pidFile));$parentId=(Get-CimInstance Win32_Process -Filter "ProcessId = $($descendant.Id)").ParentProcessId
   $parentExited=$true;try{$parent=[Diagnostics.Process]::GetProcessById([int]$parentId);try{$parentExited=$parent.WaitForExit(2000)}finally{$parent.Dispose()}}catch [ArgumentException]{}
   if(!$parentExited){throw 'Original parent still live'}
   $live=!$descendant.HasExited
   if(!$live){throw 'Descendant not live'}
  }
  if(!$launcher.WaitForExit(15000)){$launcher.Kill($true);throw 'Probe timeout'}
  $stdout.Result|Set-Content "$dir/stdout.txt";$stderr.Result|Set-Content "$dir/stderr.txt"
  if($mode -eq 'normal'){if($launcher.ExitCode -ne 0 -or (Get-Content $result -Raw|ConvertFrom-Json).stdout -ne 'ok'){throw 'Normal failure'}}
  else {if($launcher.ExitCode -eq 0 -or (Test-Path $result)){throw 'Expected refusal missing'};$expectedError=if($mode -eq 'orphan'){'System.TimeoutException'}else{'SignedObserverOutputLimitExceeded'};if(!$stderr.Result.Contains($expectedError)){throw 'Unexpected refusal reason'}}
  if($mode -eq 'orphan'){if(!$descendant.WaitForExit(5000)){throw 'Orphan survived job termination'}}
  $rows+=@{mode=$mode;exitCode=$launcher.ExitCode;descendantObservedLive=$live;originalParentExited=$(if($mode -eq 'orphan'){$parentExited}else{$null});descendantTerminated=$(if($descendant){$descendant.HasExited}else{$null});accepted=$true}
 }finally{if($descendant){$descendant.Dispose()};$launcher.Dispose()}
}
@{purpose='contained-output-controls';rows=$rows;sources=@(foreach($p in @($PSCommandPath,(Join-Path $PSScriptRoot 'SignedObserverFixture/ContainedLauncher.cs'),(Join-Path $PSScriptRoot 'SignedObserverFixture/Program.cs'))){@{path=$p;sha256=(Get-FileHash $p).Hash.ToLowerInvariant()}})}|ConvertTo-Json -Depth 8|Set-Content "$out/receipt.json"
Write-Output "Receipt: $out/receipt.json"
