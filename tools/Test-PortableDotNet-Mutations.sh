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
for tool in cp grep mkdir python3 sha256sum; do
  command -v "$tool" >/dev/null || { echo "required tool unavailable: $tool" >&2; exit 69; }
done

build_driver="$repo_root/tools/Build-PortableDotNet.sh"
mutation_driver="$repo_root/tools/Apply-PortableDotNet-Mutation.py"
adapter="$repo_root/targets/dotnet-managed-v1/ModuleApi.cs"
project="$repo_root/targets/dotnet-managed-v1/Strogo.Portable.V01.csproj"
consumer_project="$repo_root/tests/fixtures/portability-consumers/csharp-mutation/MutationConsumer.csproj"
consumer_program="$repo_root/tests/fixtures/portability-consumers/csharp-mutation/Program.cs"
[[ -x "$build_driver" && -f "$mutation_driver" && -f "$adapter" && -f "$project" && -f "$consumer_project" && -f "$consumer_program" ]] || { echo 'mutation inputs unavailable' >&2; exit 69; }

mkdir -p "$run_dir/logs" "$run_dir/mutations"
"$build_driver" --dafny "$dafny" --dotnet "$dotnet" --source "$source_file" --run-dir "$run_dir/baseline" --repo-root "$repo_root" >"$run_dir/logs/baseline-build.log" 2>&1
baseline_dll="$run_dir/baseline/artifact/strogo.portable.v01.dll"
baseline_candidate="$run_dir/baseline/a/Candidate.cs"

baseline_consumer="$run_dir/baseline-consumer"
mkdir -p "$baseline_consumer"
cp "$consumer_project" "$baseline_consumer/MutationConsumer.csproj"
cp "$consumer_program" "$baseline_consumer/Program.cs"
"$dotnet" run --project "$baseline_consumer/MutationConsumer.csproj" -c Release -p:PortableAssemblyPath="$baseline_dll" -- baseline all >"$run_dir/logs/baseline-probes.log" 2>&1
[[ "$(grep -c '^BASELINE_PASS mutation=' "$run_dir/logs/baseline-probes.log")" == '4' ]] || { cat "$run_dir/logs/baseline-probes.log" >&2; exit 72; }

mutations=(flip-sum-sign reverse-input-sequence force-eager-head alter-refusal-code)
for mutation in "${mutations[@]}"; do
  if "$dotnet" run --project "$baseline_consumer/MutationConsumer.csproj" -c Release -p:PortableAssemblyPath="$baseline_dll" -- mutant "$mutation" >"$run_dir/logs/negative-control-$mutation.log" 2>&1; then
    echo "negative control incorrectly detected unchanged target: $mutation" >&2
    exit 72
  fi
  grep -Fq "mutation survived comparison: $mutation" "$run_dir/logs/negative-control-$mutation.log" || { cat "$run_dir/logs/negative-control-$mutation.log" >&2; exit 72; }
done

for mutation in "${mutations[@]}"; do
  lane="$run_dir/mutations/$mutation"
  mkdir -p "$lane"
  cp "$baseline_candidate" "$lane/Candidate.cs"
  cp "$adapter" "$lane/ModuleApi.cs"
  cp "$project" "$lane/Strogo.Portable.V01.csproj"
  python3 "$mutation_driver" --mutation "$mutation" --candidate "$lane/Candidate.cs" --adapter "$lane/ModuleApi.cs" --report "$lane/mutation.json"
  "$dotnet" build "$lane/Strogo.Portable.V01.csproj" -c Release -p:PathMap="$lane=/_/" >"$run_dir/logs/build-$mutation.log" 2>&1
  grep -Fq '0 Warning(s)' "$run_dir/logs/build-$mutation.log" || { cat "$run_dir/logs/build-$mutation.log" >&2; exit 71; }
  grep -Fq '0 Error(s)' "$run_dir/logs/build-$mutation.log" || { cat "$run_dir/logs/build-$mutation.log" >&2; exit 71; }

  mutant_dll="$lane/bin/Release/net10.0/Strogo.Portable.V01.dll"
  probe="$lane/probe"
  mkdir -p "$probe"
  cp "$consumer_project" "$probe/MutationConsumer.csproj"
  cp "$consumer_program" "$probe/Program.cs"
  "$dotnet" run --project "$probe/MutationConsumer.csproj" -c Release -p:PortableAssemblyPath="$mutant_dll" -- mutant "$mutation" >"$run_dir/logs/probe-$mutation.log" 2>&1
  if [[ "$mutation" == 'force-eager-head' ]]; then
    grep -Eq "^MUTATION_DETECTED mutation=$mutation mode=exception exception=" "$run_dir/logs/probe-$mutation.log" || { cat "$run_dir/logs/probe-$mutation.log" >&2; exit 72; }
  else
    grep -Fxq "MUTATION_DETECTED mutation=$mutation mode=mismatch" "$run_dir/logs/probe-$mutation.log" || { cat "$run_dir/logs/probe-$mutation.log" >&2; exit 72; }
  fi
done

python3 - "$run_dir" "$repo_root" "$baseline_dll" <<'PY'
import hashlib, json, pathlib, subprocess, sys
run, repo, baseline = map(pathlib.Path, sys.argv[1:])
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
mutations = []
for mutation_id in ('flip-sum-sign', 'reverse-input-sequence', 'force-eager-head', 'alter-refusal-code'):
    lane = run / 'mutations' / mutation_id
    mutation = json.loads((lane / 'mutation.json').read_text(encoding='utf-8'))
    log = (run / 'logs' / f'probe-{mutation_id}.log').read_text(encoding='utf-8')
    detection = next(line for line in log.splitlines() if line.startswith('MUTATION_DETECTED '))
    mutations.append({
        'mutationId': mutation_id,
        'target': mutation['target'],
        'mutatedArtifactSha256': sha(lane / 'bin/Release/net10.0/Strogo.Portable.V01.dll'),
        'detection': detection,
        'status': 'Detected',
    })
report = {
    'schemaVersion': 'strogo.dotnet-mutation-run.v0.1',
    'status': 'Passed',
    'profileId': 'dotnet-managed.v1',
    'repositoryRevision': subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip(),
    'repositoryDirty': bool(subprocess.check_output(['git', '-C', str(repo), 'status', '--porcelain'], text=True).strip()),
    'baselineArtifactSha256': sha(baseline),
    'baselineProbeCount': 4,
    'unchangedTargetRejectionCount': 4,
    'detectedMutationCount': len(mutations),
    'mutations': mutations,
}
(run / 'report.json').write_text(json.dumps(report, ensure_ascii=True, separators=(',', ':')) + '\n', encoding='utf-8', newline='\n')
PY

echo 'PASS dotnet-managed mutations baseline=4 detected=4 compiled=4'
