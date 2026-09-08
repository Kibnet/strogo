#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "usage: $0 --package /path --expected-manifest 64hex --run-dir /new/path --dotnet /path/to/dotnet [--harness-dotnet /path/to/dotnet] --expected-runtime-closure 64hex [--repo-root /path]" >&2
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
package="$(readlink -f "$package")"
repo_root="$(readlink -f "$repo_root")"
run_dir="$(realpath -m "$run_dir")"
[[ ! -e "$run_dir" ]] || { echo "run directory already exists: $run_dir" >&2; exit 65; }
mkdir -p "$run_dir"
harness_dotnet="${harness_dotnet:-$dotnet}"
[[ -x "$harness_dotnet" ]] || usage
dotnet="$(readlink -f "$dotnet")"
harness_dotnet="$(readlink -f "$harness_dotnet")"
[[ "$("$harness_dotnet" --version)" == '10.0.400' ]] || { echo '.NET harness SDK version mismatch' >&2; exit 70; }

runtime_inventory="$run_dir/runtime-inventory.json"
runtime_report="$run_dir/environment.json"
if ! "$harness_dotnet" run --project "$repo_root/tests/Strogo.Modules.Portability.PackageHarness" -c Release --no-build -- runtime \
  --dotnet-executable "$dotnet" \
  --runtime-version 10.0.11 \
  --os linux \
  --arch x64 \
  --inventory "$runtime_inventory" \
  --report "$runtime_report" \
  --expected-runtime-closure-digest "$expected_runtime" >"$run_dir/runtime.log" 2>&1; then
  cat "$run_dir/runtime.log" >&2
  exit 70
fi
grep -Eq '^PASS dotnet runtime closure ' "$run_dir/runtime.log"

staged="$run_dir/validated/strogo.portable.v01.dll"
validation_report="$run_dir/package-validation.json"
"$harness_dotnet" run --project "$repo_root/tests/Strogo.Modules.Portability.PackageHarness" -c Release --no-build -- validate \
  --package "$package" \
  --expected-manifest-digest "$expected" \
  --staged-artifact "$staged" \
  --report "$validation_report" >"$run_dir/package-validation.log" 2>&1
grep -Eq '^PASS package validation ' "$run_dir/package-validation.log"

jit_plan="$run_dir/jit-plan.json"
"$harness_dotnet" run --project "$repo_root/tests/Strogo.Modules.Portability.PackageHarness" -c Release --no-build -- jit-plan \
  --package "$package" \
  --expected-manifest-digest "$expected" \
  --output "$jit_plan" >"$run_dir/jit-plan.log" 2>&1
readarray -t symbols < <(python3 - "$jit_plan" <<'PY'
import json, sys
plan=json.load(open(sys.argv[1], encoding='utf-8'))
if plan.get('schemaVersion') != 'strogo.dotnet-jit-plan.v0.1' or plan.get('calls') != '50000':
    raise SystemExit('JIT plan contract mismatch')
print(plan['entrySymbol'])
print(plan['candidateSymbol'])
PY
)
[[ "${#symbols[@]}" == 2 ]] || { echo 'JIT plan symbols unavailable' >&2; exit 71; }

consumer="$run_dir/consumer"
mkdir -p "$consumer"
cp "$repo_root/tests/fixtures/portability-consumers/csharp-jit/JitConsumer.csproj" "$consumer/JitConsumer.csproj"
cp "$repo_root/tests/fixtures/portability-consumers/csharp-jit/Program.cs" "$consumer/Program.cs"
"$harness_dotnet" build "$consumer/JitConsumer.csproj" -c Release \
  -p:ImportDirectoryBuildProps=false \
  -p:ImportDirectoryBuildTargets=false \
  -p:PortableAssemblyPath="$staged" >"$run_dir/consumer-build.log" 2>&1
consumer_dll="$consumer/bin/Release/net10.0/JitConsumer.dll"
[[ -f "$consumer_dll" ]] || { echo 'JIT consumer assembly unavailable' >&2; exit 71; }

jit_log="$run_dir/jit.log"
consumer_output="$run_dir/consumer.log"
consumer_error="$run_dir/consumer.stderr.log"
DOTNET_ReadyToRun=0 \
DOTNET_TieredCompilation=0 \
DOTNET_JitNoInline=1 \
DOTNET_JitDisasm="${symbols[0]} ${symbols[1]}" \
DOTNET_JitDisasmDiffable=1 \
DOTNET_JitDisasmTesting=1 \
DOTNET_JitStdOutFile="$jit_log" \
"$dotnet" "$consumer_dll" >"$consumer_output" 2>"$consumer_error" &
consumer_pid=$!
(
  sleep 180
  kill -TERM "$consumer_pid" 2>/dev/null || exit 0
  sleep 5
  kill -KILL "$consumer_pid" 2>/dev/null || true
) &
watchdog_pid=$!
set +e
wait "$consumer_pid"
consumer_exit=$?
set -e
kill "$watchdog_pid" 2>/dev/null || true
wait "$watchdog_pid" 2>/dev/null || true
[[ "$consumer_exit" == 0 ]] || { echo "JIT consumer failed with exit $consumer_exit" >&2; cat "$consumer_error" >&2; exit 71; }
[[ ! -s "$consumer_error" ]] || { echo 'JIT consumer wrote stderr' >&2; exit 71; }
grep -Fxq "PASS dotnet JIT diagnostic pid=$consumer_pid calls=50000" "$consumer_output" || { echo 'JIT consumer process receipt mismatch' >&2; exit 71; }
[[ -f "$jit_log" ]] || { echo 'JIT log unavailable' >&2; exit 71; }
[[ "$(stat -c %s "$jit_log")" -le 1048576 ]] || { echo 'JIT log exceeded 1 MiB' >&2; exit 71; }

receipt="$run_dir/jit-receipt.json"
"$harness_dotnet" run --project "$repo_root/tests/Strogo.Modules.Portability.PackageHarness" -c Release --no-build -- jit-receipt \
  --package "$package" \
  --expected-manifest-digest "$expected" \
  --runtime-report "$runtime_report" \
  --consumer-output "$consumer_output" \
  --jit-log "$jit_log" \
  --os linux \
  --arch x64 \
  --output "$receipt" >"$run_dir/jit-receipt.log" 2>&1
grep -Eq '^PASS dotnet JIT receipt os=linux events=2 calls=50000 process=' "$run_dir/jit-receipt.log"

echo 'PASS dotnet JIT diagnostic events=2 calls=50000'
