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

consumer="$run_dir/consumer"
mkdir -p "$consumer"
cp "$repo_root/tests/fixtures/portability-consumers/csharp-performance/PerformanceConsumer.csproj" "$consumer/PerformanceConsumer.csproj"
cp "$repo_root/tests/fixtures/portability-consumers/csharp-performance/Program.cs" "$consumer/Program.cs"
"$harness_dotnet" build "$consumer/PerformanceConsumer.csproj" -c Release \
  -p:ImportDirectoryBuildProps=false \
  -p:ImportDirectoryBuildTargets=false \
  -p:PortableAssemblyPath="$staged" >"$run_dir/consumer-build.log" 2>&1
consumer_dll="$consumer/bin/Release/net10.0/PerformanceConsumer.dll"
[[ -f "$consumer_dll" ]] || { echo 'performance consumer assembly unavailable' >&2; exit 71; }

python3 - "$dotnet" "$consumer_dll" "$runtime_report" "$validation_report" "$package/portability-manifest.json" "$staged" "$package" "$run_dir/performance-report.json" <<'PY'
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import time

dotnet, consumer_dll, runtime_path, validation_path, manifest_path, staged_path, package_path, output_path = sys.argv[1:]

def load(path):
    with open(path, encoding="utf-8") as stream:
        return json.load(stream)

def positive(value, label):
    if not isinstance(value, str) or not value.isascii() or not value.isdigit() or int(value) <= 0:
        raise SystemExit(f"invalid positive decimal string: {label}")

def observe(mode):
    environment = os.environ.copy()
    for name in ("DOTNET_ReadyToRun", "DOTNET_TieredCompilation", "DOTNET_JitNoInline", "DOTNET_JitDisasm", "DOTNET_JitDisasmDiffable", "DOTNET_JitDisasmTesting", "DOTNET_JitStdOutFile"):
        environment.pop(name, None)
    started = time.monotonic_ns()
    process = subprocess.Popen([dotnet, consumer_dll, mode], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8", env=environment)
    try:
        stdout, stderr = process.communicate(timeout=180)
    except subprocess.TimeoutExpired:
        process.kill()
        process.communicate()
        raise SystemExit("performance process exceeded 180 second deadline")
    elapsed = time.monotonic_ns() - started
    if process.returncode != 0 or stderr:
        raise SystemExit(f"performance process failed: {stderr}")
    try:
        payload = json.loads(stdout)
    except json.JSONDecodeError as error:
        raise SystemExit(f"performance process returned invalid JSON: {error}") from error
    if payload.get("status") != "Passed" or payload.get("mode") != mode or payload.get("processId") != str(process.pid):
        raise SystemExit("performance process receipt mismatch")
    positive(str(elapsed), f"{mode}.elapsedNanoseconds")
    positive(payload.get("peakWorkingSetBytes"), f"{mode}.peakWorkingSetBytes")
    return {"processId": str(process.pid), "elapsedNanoseconds": str(elapsed), "peakWorkingSetBytes": payload["peakWorkingSetBytes"], "observation": payload}

startup = [observe("startup") for _ in range(5)]
if len({item["processId"] for item in startup}) != 5 or any(item["observation"].get("calls") != "1" for item in startup):
    raise SystemExit("performance report requires five distinct cold starts")
throughput = observe("throughput")
observation = throughput["observation"]
if (observation.get("warmupCalls"), observation.get("repeats"), observation.get("callsPerRepeat")) != ("5000", "5", "10000"):
    raise SystemExit("throughput settings receipt mismatch")
durations = observation.get("durationsNanoseconds")
rates = observation.get("operationsPerSecond")
if not isinstance(durations, list) or not isinstance(rates, list) or len(durations) != 5 or len(rates) != 5:
    raise SystemExit("invalid throughput observation")
for index, value in enumerate(durations):
    positive(value, f"throughput.durationsNanoseconds[{index}]")
for index, value in enumerate(rates):
    positive(value, f"throughput.operationsPerSecond[{index}]")

runtime = load(runtime_path)
validation = load(validation_path)
manifest = load(manifest_path)
package_tree_bytes = sum(path.stat().st_size for path in Path(package_path).rglob("*") if path.is_file())
artifact_set_bytes = sum(int(item["length"]) for item in manifest["files"])
report = {
    "schemaVersion": "strogo.dotnet-performance-report.v0.1",
    "status": "Passed",
    "assertionBoundary": "DiagnosticOnlyNoG06",
    "profileId": "dotnet-managed.v1",
    "os": "linux",
    "arch": "x64",
    "runtimeVendor": runtime["runtimeVendor"],
    "runtimeVersion": runtime["runtimeVersion"],
    "runtimeClosureDigest": runtime["runtimeClosureDigest"],
    "portabilityManifestDigest": validation["portabilityManifestDigest"],
    "packageDigest": validation["packageDigest"],
    "artifactDigest": validation["artifactDigest"],
    "controller": {"kind": "CPython", "version": platform.python_version(), "clock": "time.monotonic_ns"},
    "commands": {"startup": "dotnet PerformanceConsumer.dll startup", "throughput": "dotnet PerformanceConsumer.dll throughput"},
    "settings": {"coldStartRepeats": "5", "warmupCalls": "5000", "throughputRepeats": "5", "callsPerRepeat": "10000", "jitDiagnosticOverrides": []},
    "startup": startup,
    "throughput": throughput,
    "sizes": {"entryAssemblyBytes": str(Path(staged_path).stat().st_size), "artifactSetBytes": str(artifact_set_bytes), "packageTreeBytes": str(package_tree_bytes)},
}
Path(output_path).write_text(json.dumps(report, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
PY

echo 'PASS dotnet performance diagnostic cold=5 warmup=5000 repeats=5 calls=10000'
