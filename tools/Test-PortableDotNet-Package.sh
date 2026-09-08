#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "usage: $0 --package /path --expected-manifest 64hex --run-dir /new/path --dotnet /path/to/dotnet [--harness-dotnet /path/to/dotnet] [--expected-runtime-closure 64hex] [--repo-root /path]" >&2
  exit 64
}

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package=""
expected=""
run_dir=""
dotnet=""
harness_dotnet=""
expected_runtime=""
while (($#)); do
  case "$1" in
    --package) package="$2"; shift 2 ;;
    --expected-manifest) expected="$2"; shift 2 ;;
    --run-dir) run_dir="$2"; shift 2 ;;
    --dotnet) dotnet="$2"; shift 2 ;;
    --harness-dotnet) harness_dotnet="$2"; shift 2 ;;
    --expected-runtime-closure) expected_runtime="$2"; shift 2 ;;
    --repo-root) repo_root="$2"; shift 2 ;;
    *) usage ;;
  esac
done

[[ -d "$package" && "$expected" =~ ^[0-9a-f]{64}$ && "$expected_runtime" =~ ^[0-9a-f]{64}$ && -n "$run_dir" && -x "$dotnet" ]] || usage
[[ ! -e "$run_dir" ]] || { echo "run directory already exists: $run_dir" >&2; exit 65; }
mkdir -p "$run_dir"
harness_dotnet="${harness_dotnet:-$dotnet}"
[[ -x "$harness_dotnet" ]] || usage
dotnet="$(readlink -f "$dotnet")"
harness_dotnet="$(readlink -f "$harness_dotnet")"
runtime_args=(
  --dotnet-executable "$dotnet"
  --runtime-version 10.0.11
  --os linux
  --arch x64
  --inventory "$run_dir/runtime-inventory.json"
  --report "$run_dir/environment.json"
)
runtime_args+=(--expected-runtime-closure-digest "$expected_runtime")
if ! "$harness_dotnet" run --project "$repo_root/tests/Strogo.Modules.Portability.PackageHarness" -c Release --no-build -- runtime "${runtime_args[@]}" >"$run_dir/runtime.log" 2>&1; then
  cat "$run_dir/runtime.log" >&2
  exit 70
fi
grep -Eq '^PASS dotnet runtime closure ' "$run_dir/runtime.log"
[[ "$("$harness_dotnet" --version)" == '10.0.400' ]] || { echo '.NET harness SDK version mismatch' >&2; exit 70; }
runtime="$("$dotnet" --list-runtimes | grep -E '^Microsoft\.NETCore\.App 10\.0\.11 ' | head -1 | sed 's/ \[.*$//')"
[[ -n "$runtime" ]] || { echo '.NET runtime 10.0.11 unavailable after closure validation' >&2; exit 70; }

staged="$run_dir/validated/strogo.portable.v01.dll"
validation_report="$run_dir/package-validation.json"
"$harness_dotnet" run --project "$repo_root/tests/Strogo.Modules.Portability.PackageHarness" -c Release --no-build -- validate \
  --package "$package" \
  --expected-manifest-digest "$expected" \
  --staged-artifact "$staged" \
  --report "$validation_report" >"$run_dir/package-validation.log" 2>&1
grep -Eq '^PASS package validation ' "$run_dir/package-validation.log"

consumer="$run_dir/consumer"
mkdir -p "$consumer"
cp "$repo_root/tests/fixtures/portability-consumers/csharp/Consumer.csproj" "$consumer/Consumer.csproj"
cp "$repo_root/tests/fixtures/portability-consumers/csharp/Program.cs" "$consumer/Program.cs"
"$harness_dotnet" build "$consumer/Consumer.csproj" -c Release -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -p:PortableAssemblyPath="$staged" >"$run_dir/consumer-build.log" 2>&1
consumer_dll="$consumer/bin/Release/net10.0/Consumer.dll"
[[ -f "$consumer_dll" ]] || { echo 'consumer assembly unavailable' >&2; exit 71; }
"$dotnet" "$consumer_dll" "$repo_root/fixtures/portability-v0.1/invoke-vectors.jsonl" >"$run_dir/consumer.log" 2>&1
grep -Fxq 'PASS standalone C# consumer cases=8 transport=24 additional=1' "$run_dir/consumer.log"
grep -Fxq 'PASS standalone C# invoke vectors=13' "$run_dir/consumer.log"

python3 - "$run_dir" "$runtime" <<'PY'
import json, pathlib, platform, sys
run, runtime = pathlib.Path(sys.argv[1]), sys.argv[2]
validation=json.loads((run/'package-validation.json').read_text(encoding='utf-8'))
environment=json.loads((run/'environment.json').read_text(encoding='utf-8'))
report={
  'schemaVersion':'strogo.dotnet-package-platform-run.v0.1',
  'status':'Passed',
  'profileId':'dotnet-managed.v1',
  'os':'linux',
  'arch':'x64',
  'runtime':runtime,
  'runtimeVendor':environment['runtimeVendor'],
  'runtimeVersion':environment['runtimeVersion'],
  'runtimeClosureDigest':environment['runtimeClosureDigest'],
  'launcherDigest':environment['launcherDigest'],
  'harnessDigest':environment['harnessDigest'],
  'portabilityManifestDigest':validation['portabilityManifestDigest'],
  'packageDigest':validation['packageDigest'],
  'artifactDigest':validation['artifactDigest'],
  'stagedArtifactSha256':validation['stagedArtifactSha256'],
  'validationOutcome':'Passed',
  'consumerOutcome':'Passed'
}
(run/'report.json').write_text(json.dumps(report,ensure_ascii=True,separators=(',',':'))+'\n',encoding='utf-8',newline='\n')
PY

manifest="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["portabilityManifestDigest"])' "$validation_report")"
echo "PASS dotnet package linux manifest=$manifest consumer=8+24+1+13"
