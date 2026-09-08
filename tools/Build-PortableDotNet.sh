#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "usage: $0 --dafny /absolute/path/to/dafny --dotnet /absolute/path/to/dotnet --source /absolute/path/to/candidate.dfy --run-dir /absolute/path/to/new-directory [--repo-root /absolute/path]" >&2
  exit 64
}

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dafny=""
dotnet=""
source_file=""
run_dir=""
while (($#)); do
  case "$1" in
    --dafny) dafny="$2"; shift 2 ;;
    --dotnet) dotnet="$2"; shift 2 ;;
    --source) source_file="$2"; shift 2 ;;
    --run-dir) run_dir="$2"; shift 2 ;;
    --repo-root) repo_root="$2"; shift 2 ;;
    *) usage ;;
  esac
done

[[ -x "$dafny" && -x "$dotnet" && -f "$source_file" && -n "$run_dir" ]] || usage
[[ ! -e "$run_dir" ]] || { echo "run directory already exists: $run_dir" >&2; exit 65; }
for tool in cmp cp git grep mkdir python3 sha256sum; do
  command -v "$tool" >/dev/null || { echo "required tool unavailable: $tool" >&2; exit 69; }
done

expected_dafny_sha='e540b4826363afb87c326446239a682d45086905425fa6299c103eca9693846d'
expected_source_sha='1a6076f89ec5d233df9eeaf6b472d11c8a10407e9e8b5ceb09bc4b1fbda3bdcf'
[[ "$(sha256sum "$dafny" | cut -d' ' -f1)" == "$expected_dafny_sha" ]] || { echo 'Dafny executable digest mismatch' >&2; exit 70; }
[[ "$("$dafny" --version)" == '4.11.0+fcb2042d6d043a2634f0854338c08feeaaaf4ae2' ]] || { echo 'Dafny version mismatch' >&2; exit 70; }
[[ "$("$dotnet" --version)" == '10.0.400' ]] || { echo '.NET SDK version mismatch' >&2; exit 70; }
[[ "$(sha256sum "$source_file" | cut -d' ' -f1)" == "$expected_source_sha" ]] || { echo 'verified source digest mismatch' >&2; exit 70; }

adapter="$repo_root/targets/dotnet-managed-v1/ModuleApi.cs"
project="$repo_root/targets/dotnet-managed-v1/Strogo.Portable.V01.csproj"
consumer_project="$repo_root/tests/fixtures/portability-consumers/csharp/Consumer.csproj"
consumer_program="$repo_root/tests/fixtures/portability-consumers/csharp/Program.cs"
[[ -f "$adapter" && -f "$project" && -f "$consumer_project" && -f "$consumer_program" ]] || { echo 'target inputs unavailable' >&2; exit 69; }

mkdir -p "$run_dir/logs" "$run_dir/artifact"
for lane_name in a b; do
  lane="$run_dir/$lane_name"
  mkdir -p "$lane"
  cp "$source_file" "$lane/candidate.dfy"
  cp "$adapter" "$lane/ModuleApi.cs"
  cp "$project" "$lane/Strogo.Portable.V01.csproj"
  (cd "$lane" && "$dafny" translate cs candidate.dfy --no-verify --enforce-determinism --include-runtime --output Candidate.cs --translation-record-output translation-record.dtr) >"$run_dir/logs/translate-$lane_name.log" 2>&1
  "$dotnet" build "$lane/Strogo.Portable.V01.csproj" -c Release -p:PathMap="$lane=/_/" >"$run_dir/logs/build-$lane_name.log" 2>&1
  grep -Fq '0 Warning(s)' "$run_dir/logs/build-$lane_name.log" || { cat "$run_dir/logs/build-$lane_name.log" >&2; exit 71; }
  grep -Fq '0 Error(s)' "$run_dir/logs/build-$lane_name.log" || { cat "$run_dir/logs/build-$lane_name.log" >&2; exit 71; }
done

cmp "$run_dir/a/Candidate.cs" "$run_dir/b/Candidate.cs"
cmp "$run_dir/a/translation-record.dtr" "$run_dir/b/translation-record.dtr"
cmp "$run_dir/a/bin/Release/net10.0/Strogo.Portable.V01.dll" "$run_dir/b/bin/Release/net10.0/Strogo.Portable.V01.dll"
cp "$run_dir/a/bin/Release/net10.0/Strogo.Portable.V01.dll" "$run_dir/artifact/strogo.portable.v01.dll"

consumer="$run_dir/consumer"
mkdir -p "$consumer"
cp "$consumer_project" "$consumer/Consumer.csproj"
cp "$consumer_program" "$consumer/Program.cs"
"$dotnet" run --project "$consumer/Consumer.csproj" -c Release -p:PortableAssemblyPath="$run_dir/artifact/strogo.portable.v01.dll" >"$run_dir/logs/consumer-linux.log" 2>&1
grep -Fxq 'PASS standalone C# consumer cases=8 transport=24' "$run_dir/logs/consumer-linux.log" || { cat "$run_dir/logs/consumer-linux.log" >&2; exit 72; }

python3 - "$run_dir" "$repo_root" "$source_file" "$adapter" "$project" "$dafny" "$dotnet" <<'PY'
import hashlib, json, pathlib, platform, subprocess, sys
run, repo, source, adapter, project, dafny, dotnet = map(pathlib.Path, sys.argv[1:])
sha=lambda path: hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()
report={
  'schemaVersion':'strogo.dotnet-build-run.v0.1',
  'status':'Passed',
  'profileId':'dotnet-managed.v1',
  'repositoryRevision':subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip(),
  'repositoryDirty':bool(subprocess.check_output(['git','-C',str(repo),'status','--porcelain'],text=True).strip()),
  'dafnySourceDigest':sha(source),
  'adapterDigest':sha(adapter),
  'projectDigest':sha(project),
  'translatedSourceDigest':sha(run/'a/Candidate.cs'),
  'translationRecordDigest':sha(run/'a/translation-record.dtr'),
  'artifactDigest':sha(run/'artifact/strogo.portable.v01.dll'),
  'twoCleanTranslation':'ByteEqual',
  'twoCleanBuild':'ByteEqual',
  'consumerOutcome':'Passed',
  'toolchain':{'dafnyVersion':subprocess.check_output([str(dafny),'--version'],text=True).strip(),'dafnyExecutableSha256':sha(dafny),'dotnetSdk':subprocess.check_output([str(dotnet),'--version'],text=True).strip()},
  'environment':{'system':platform.system(),'release':platform.release(),'machine':platform.machine()}
}
(run/'report.json').write_text(json.dumps(report,ensure_ascii=True,separators=(',',':'))+'\n',encoding='utf-8',newline='\n')
PY

echo 'PASS dotnet-managed build translations=byte-equal artifacts=byte-equal consumer=8 transport=24'
