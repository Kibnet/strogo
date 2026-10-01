#requires -Version 7.0
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $repo ('artifacts/local-validation/g02/output-bound-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out|Out-Null
$dotnet=Join-Path $repo '.tools/dotnet-sdk-10.0.400/dotnet.exe'
& $dotnet build "$PSScriptRoot/SignedObserverFixture/SignedObserverFixture.csproj" --configfile "$PSScriptRoot/SignedObserverFixture/NuGet.Config" -p:RestoreLockedMode=true 2>&1|Tee-Object "$out/build.txt"
if($LASTEXITCODE -ne 0){throw 'Probe build failed'}
if('Strogo.SignedObserver.BoundedOutput' -as [type]){throw 'Fresh PowerShell process required'}
Add-Type -Path (Join-Path $PSScriptRoot 'BoundedProcessOutput.cs')
$consumer="$PSScriptRoot/SignedObserverFixture/bin/Debug/net10.0/Strogo.Modules.ObserverFixture.dll"
$rows=@()
foreach($mode in @('normal','stdout','stderr')) {
 $s=[Diagnostics.ProcessStartInfo]::new($dotnet);$s.UseShellExecute=$false;$s.RedirectStandardOutput=$true;$s.RedirectStandardError=$true
 foreach($a in @($consumer,'--output-probe',$mode)){$s.ArgumentList.Add($a)}
 $s.Environment.Clear();foreach($k in @('SystemRoot','WINDIR','TEMP','TMP')){$s.Environment[$k]=[Environment]::GetEnvironmentVariable($k)};$s.Environment['DOTNET_ROOT']=Split-Path $dotnet
 $p=$null
 try {
  $p=[Diagnostics.Process]::Start($s)
  $stdout=[Strogo.SignedObserver.BoundedOutput]::ReadAsync($p,$p.StandardOutput.BaseStream,65536)
  $stderr=[Strogo.SignedObserver.BoundedOutput]::ReadAsync($p,$p.StandardError.BaseStream,65536)
  $timedOut=!$p.WaitForExit(10000);if($timedOut){$p.Kill($true);if(!$p.WaitForExit(5000)){throw 'Kill timeout'}}
  try{$null=[Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdout,$stderr)).Wait(5000)}catch{if(!$stdout.IsCompleted -or !$stderr.IsCompleted){throw 'Reader drain failed'}}
  if(!$stdout.IsCompleted -or !$stderr.IsCompleted){throw 'Reader incomplete'}
  $stdoutLimit=$stdout.IsFaulted -and $stdout.Exception.ToString().Contains('SignedObserverOutputLimitExceeded')
  $stderrLimit=$stderr.IsFaulted -and $stderr.Exception.ToString().Contains('SignedObserverOutputLimitExceeded')
  $row=@{mode=$mode;pid=$p.Id;exitCode=$p.ExitCode;timedOut=$timedOut;stdoutLimit=$stdoutLimit;stderrLimit=$stderrLimit;limitBytes=65536}
  if($mode -eq 'normal') {
   if($timedOut -or $p.ExitCode -ne 0 -or $stdout.IsFaulted -or $stderr.IsFaulted -or [Text.Encoding]::UTF8.GetString($stdout.Result) -ne 'ok' -or $stderr.Result.Length -ne 0){throw 'Normal reader failed'}
  }elseif($timedOut -or $p.ExitCode -eq 0 -or ($mode -eq 'stdout' -and (!$stdoutLimit -or $stderrLimit)) -or ($mode -eq 'stderr' -and (!$stderrLimit -or $stdoutLimit))){throw 'Output bound did not refuse/kill'}
  $rows+=@($row)
 }finally{if($null -ne $p){$p.Dispose()}}
}
$sources=foreach($path in @($PSCommandPath,"$PSScriptRoot/BoundedProcessOutput.cs","$PSScriptRoot/SignedObserverFixture/Program.cs")){@{path=[IO.Path]::GetRelativePath($repo,$path).Replace('\','/');sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}}
@{purpose='bounded-output-reader-65536-byte-controls-not-admission';rows=$rows;sources=@($sources)}|ConvertTo-Json -Depth 8|Set-Content "$out/receipt.json"
Write-Output "Receipt: $out/receipt.json"
