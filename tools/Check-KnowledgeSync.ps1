param([Parameter(Mandatory)][string]$Manifest,[Parameter(Mandatory)][string]$KnowledgeBase)
$ErrorActionPreference='Stop'
$m=Get-Content -LiteralPath $Manifest -Raw|ConvertFrom-Json
if($m.schemaVersion -cne 'kernel.knowledge-sync.v1'){throw 'Unknown schema'}
if($null -eq $m.entries -or @($m.entries).Count -eq 0){throw 'Empty knowledge manifest'}
$root=Split-Path $PSScriptRoot -Parent
$kbRoot=(git -C $KnowledgeBase rev-parse --show-toplevel)
$ids=@{}
foreach($e in $m.entries){
 if($e.id -notmatch '^K-E04-[0-9]{3}$' -or $ids.ContainsKey($e.id)){throw 'Invalid/duplicate knowledge ID'}
 $ids[$e.id]=$true
 $p=[IO.Path]::GetFullPath((Join-Path $KnowledgeBase $e.file))
 $base=[IO.Path]::GetFullPath($KnowledgeBase).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
 if(-not $p.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)){throw 'Path outside knowledge base'}
 $text=Get-Content -LiteralPath $p -Raw
 $match=[regex]::Match($text,'(?ms)^## '+[regex]::Escape($e.id)+'\r?\n(.*?)(?=^## |\z)')
 if(-not $match.Success){throw "Missing anchor $($e.id)"}
 $classification=[regex]::Match($match.Value,'(?m)^- Тип / статус: ([^/]+)/([^\.]+)\.\r?$')
 $kind=$classification.Groups[1].Value.Trim(); $status=$classification.Groups[2].Value.Trim()
 if($kind -cnotin @('Hypothesis','Confirmation','Refutation','Qualification','Insight','Decision','Failure','Constraint','Procedure','Source')){throw 'Invalid knowledge type'}
 if($status -cnotin @('Proposed','Testing','Confirmed','Refuted','Qualified','Inconclusive','Accepted')){throw 'Invalid knowledge status'}
 $claim=[regex]::Match($match.Value,'(?m)^- Утверждение: (.+)\r?$').Groups[1].Value.TrimEnd()
 $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($claim))).ToLowerInvariant()
 if($hash -cne $e.claimSha256){throw "Claim mismatch $($e.id)"}
 foreach($pair in @(@($root,$e.projectCommit),@($kbRoot,$e.knowledgeBaseCommit))){
   git -C $pair[0] cat-file -e "$($pair[1])^{commit}"
   if($LASTEXITCODE -ne 0){throw 'Unresolvable source commit'}
 }
 if($null -eq $e.evidenceRefs -or @($e.evidenceRefs).Count -eq 0){throw 'Missing evidence references'}
 foreach($ref in $e.evidenceRefs){
   if([string]::IsNullOrWhiteSpace($ref) -or [IO.Path]::IsPathRooted($ref) -or $ref -match '(^|[/\\])\.\.([/\\]|$)'){throw 'Invalid evidence path'}
   git -C $root cat-file -e "$($e.projectCommit):$ref"
   if($LASTEXITCODE -ne 0){throw "Unresolvable evidence $ref"}
 }
 $rel=[IO.Path]::GetRelativePath($kbRoot,$p).Replace('\','/')
 $saved=git -C $kbRoot show "$($e.knowledgeBaseCommit):$rel"
 if($LASTEXITCODE -ne 0 -or -not ($saved -join [char]10).Contains($claim)){throw 'KB commit does not contain claim'}
}
$journal=Get-Content -LiteralPath (Join-Path $KnowledgeBase '10 — Журнал знаний E04.md') -Raw
foreach($a in [regex]::Matches($journal,'(?m)^## (K-E04-[0-9]{3})')){
 if(-not $ids.ContainsKey($a.Groups[1].Value)){throw "Unmapped knowledge $($a.Groups[1].Value)"}
}
[pscustomobject]@{passed=$true;entries=$ids.Count;semanticCompleteness='requires content review'}|ConvertTo-Json
