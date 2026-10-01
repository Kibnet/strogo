#requires -Version 7.0
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'Windows x64 required'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $repo ('artifacts/local-validation/g02/native-observer-'+[Guid]::NewGuid().ToString('N'))
$bin='C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64'
$setup='C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat'
$headers='C:\Program Files (x86)\Windows Kits\NETFXSDK\4.8\Include\um'
$cmd='C:\Windows\System32\cmd.exe'
$dotnet=Join-Path $repo '.tools/dotnet-sdk-10.0.400/dotnet.exe'
foreach($p in @($out,$bin,$setup,$headers,$PSScriptRoot)){if($p -match '["%!^&|<>\r\n]|[^\x00-\x7f]'){throw 'Unsupported cmd path'}}
foreach($p in @($cmd,$dotnet,$setup,"$bin\cl.exe","$bin\ml64.exe","$headers\corprof.h")){if(!(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing input: $p"}}
New-Item -ItemType Directory $out|Out-Null
@('@echo off',('call "'+$setup+'"'),'if errorlevel 1 exit /b %errorlevel%',
 ('"'+$bin+'\ml64.exe" /nologo /c /Fo"'+$out+'\Enter.obj" "'+$PSScriptRoot+'\Enter.asm"'),
 'if errorlevel 1 exit /b %errorlevel%',
 ('"'+$bin+'\cl.exe" /nologo /std:c++17 /EHsc /W4 /WX /O2 /MD /LD /I"'+$headers+'" /Fo"'+$out+'\Observer.obj" /Fe"'+$out+'\Observer.dll" "'+$PSScriptRoot+'\Observer.cpp" "'+$out+'\Enter.obj" /link /WX /DEF:"'+$PSScriptRoot+'\Observer.def" /IMPLIB:"'+$out+'\Observer.lib"'),
 'exit /b %errorlevel%')|Set-Content (Join-Path $out 'build.cmd') -Encoding ascii
& $cmd /d /c (Join-Path $out 'build.cmd') 2>&1|Tee-Object (Join-Path $out 'native-build.txt')
if($LASTEXITCODE -ne 0){throw 'Native build failed'}
$consumer=Join-Path $out 'consumer';New-Item -ItemType Directory $consumer|Out-Null
Copy-Item -LiteralPath "$PSScriptRoot\Probe\Program.cs","$PSScriptRoot\Probe\Probe.csproj","$PSScriptRoot\Probe\NuGet.Config" -Destination $consumer
& $dotnet build "$consumer\Probe.csproj" -c Release --configfile "$consumer\NuGet.Config" -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false 2>&1|Tee-Object (Join-Path $out 'managed-build.txt')
if($LASTEXITCODE -ne 0){throw 'Managed probe build failed'}
$results=@()
foreach($mode in @('call','no-call','parallel','overflow')) {
 $trace=Join-Path $out ($mode+'-trace.jsonl')
 $s=[Diagnostics.ProcessStartInfo]::new($dotnet);$s.UseShellExecute=$false;$s.RedirectStandardOutput=$true;$s.RedirectStandardError=$true
 $s.ArgumentList.Add("$consumer\bin\Release\net10.0\Probe.dll");$s.ArgumentList.Add($mode)
 $s.Environment.Clear();foreach($k in @('SystemRoot','WINDIR','TEMP','TMP')){$s.Environment[$k]=[Environment]::GetEnvironmentVariable($k)}
 $s.Environment['CORECLR_ENABLE_PROFILING']='1';$s.Environment['CORECLR_PROFILER']='{486D81E7-F308-4E40-9142-58290B2A6E11}';$s.Environment['CORECLR_PROFILER_PATH']="$out\Observer.dll"
 $s.Environment['STROGO_OBSERVER_TRACE']=$trace;$s.Environment['STROGO_OBSERVER_TYPE']='Probe';$s.Environment['STROGO_OBSERVER_METHOD']='Target';$s.Environment['DOTNET_ReadyToRun']='0'
 $p=[Diagnostics.Process]::Start($s);$stdout=$p.StandardOutput.ReadToEndAsync();$stderr=$p.StandardError.ReadToEndAsync()
 if(!$p.WaitForExit(20000)){$p.Kill($true);$null=$p.WaitForExit(5000);$p.Dispose();throw "Child timeout: $mode"}
 if(![Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdout,$stderr)).Wait(5000)){$p.Dispose();throw "Output timeout: $mode"}
 $result=[ordered]@{mode=$mode;pid=$p.Id;exitCode=$p.ExitCode;stdout=$stdout.Result;stderr=$stderr.Result}
 $p.Dispose();$result|ConvertTo-Json|Set-Content "$out\$mode-process.json"
 $rows=@(Get-Content -LiteralPath $trace|ForEach-Object {$_|ConvertFrom-Json})
 $init=@($rows|Where-Object event -eq initialize);$stop=@($rows|Where-Object event -eq shutdown);$enter=@($rows|Where-Object event -eq enter);$map=@($rows|Where-Object event -eq map)
 $expected=@{call=1;'no-call'=0;parallel=1024;overflow=4096}[$mode]
 if($result.stdout.Trim() -ne (@{call="42";'no-call'="42";parallel="1024";overflow="4097"}[$mode])){throw "Invalid output: $mode"}
 if($result.exitCode -ne 0 -or $result.stderr -ne '' -or $init.Count -ne 1 -or $init[0].hresult -ne 0 -or $stop.Count -ne 1 -or $stop[0].entries -ne $expected){throw "Invalid process/trace: $mode"}
 if(@($rows|Where-Object pid -ne $result.pid).Count -ne 0){throw 'PID mismatch'}
 if($mode -eq 'overflow'){if(!$stop[0].traceFailed -or $enter.Count -ne 0){throw 'Overflow did not refuse'}}
 else {if($stop[0].traceFailed -or $enter.Count -ne $expected){throw 'Entry mismatch'};foreach($entry in $enter){if(@($map|Where-Object functionId -eq $entry.functionId).Count -ne 1){throw 'Unmapped entry'}}}
 if($mode -eq 'no-call' -and $map.Count -ne 0){throw 'Decoy mapped'}
 $result.traceFailed=$stop[0].traceFailed;$result.entries=$stop[0].entries;$results+=$result
}
$hashes=@();foreach($p in @("$out\Observer.dll","$consumer\bin\Release\net10.0\Probe.dll",$dotnet,"$bin\cl.exe","$bin\ml64.exe",$cmd,"$headers\corprof.h","$headers\cor.h")+@(Get-ChildItem -LiteralPath $PSScriptRoot -File|ForEach-Object FullName)+@(Get-ChildItem -LiteralPath "$PSScriptRoot\Probe" -File|ForEach-Object FullName)){$hashes+=@{name=[IO.Path]::GetFileName($p);sha256=(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}}
[ordered]@{purpose='diagnostic-native-profiler-startup';results=$results;hashes=$hashes;limits=@('No generated module digest or source-map binding','No public admission','Forced JIT only','Partial toolchain identities, no immutable closure')}|ConvertTo-Json -Depth 8|Set-Content "$out\receipt.json"
Write-Output "Receipt: $out\receipt.json"


