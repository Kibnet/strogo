#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "usage: $0 --dafny /absolute/path/to/dafny --run-dir /absolute/path/to/new-directory [--repo-root /absolute/path]" >&2
  exit 64
}

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dafny=""
run_dir=""
while (($#)); do
  case "$1" in
    --dafny) dafny="$2"; shift 2 ;;
    --run-dir) run_dir="$2"; shift 2 ;;
    --repo-root) repo_root="$2"; shift 2 ;;
    *) usage ;;
  esac
done

[[ -n "$dafny" && -x "$dafny" && -n "$run_dir" ]] || usage
[[ ! -e "$run_dir" ]] || { echo "run directory already exists: $run_dir" >&2; exit 65; }
mkdir -p "$run_dir/fold" "$run_dir/fold-repeat" "$run_dir/logs"

for tool in cmp dotnet python3 timeout sha256sum; do
  command -v "$tool" >/dev/null || { echo "required tool unavailable: $tool" >&2; exit 69; }
done

dotnet run --project "$repo_root/tests/Strogo.Modules.Conformance/Strogo.Modules.Conformance.csproj" -c Release -- \
  --report "$run_dir/modules.json" --fold-output "$run_dir/fold" >"$run_dir/logs/conformance.log" 2>&1
grep -Eq '^PASS conformance checks=[1-9][0-9]*$' "$run_dir/logs/conformance.log"
dotnet run --project "$repo_root/tests/Strogo.Modules.Conformance/Strogo.Modules.Conformance.csproj" -c Release -- \
  --report "$run_dir/modules-repeat.json" --fold-output "$run_dir/fold-repeat" >"$run_dir/logs/conformance-repeat.log" 2>&1
grep -Eq '^PASS conformance checks=[1-9][0-9]*$' "$run_dir/logs/conformance-repeat.log"
for file in candidate-a.dfy candidate-b.dfy candidate-c.dfy allocation-primary.dfy allocation-alternative.dfy manifest.json; do
  cmp "$run_dir/fold/$file" "$run_dir/fold-repeat/$file"
done

forbidden='Prover error:|Model parsing error|Could not parse any models|Unhandled exception|Internal error|Fatal error|ToolError'
run_dafny() {
  local name="$1" mode="$2" expected_exit="$3" expected_summary="$4" source="$5" output="${6:-}"
  local log="$run_dir/logs/$name.log" exit_code=0
  local args=("$mode" "$source" --enforce-determinism --cores 2 --verification-time-limit 15)
  if [[ "$mode" == translate ]]; then args=(translate cs "$source" --include-runtime --enforce-determinism --cores 2 --verification-time-limit 15 --output "$output"); fi
  timeout 180s "$dafny" "${args[@]}" >"$log" 2>&1 || exit_code=$?
  [[ $(wc -c <"$log") -le 1048576 ]] || { echo "$name exceeded output limit" >&2; exit 70; }
  [[ "$exit_code" == "$expected_exit" ]] || { cat "$log" >&2; echo "$name exit $exit_code, expected $expected_exit" >&2; exit 71; }
  ! grep -Eq "$forbidden" "$log" || { cat "$log" >&2; echo "$name emitted an operational failure marker" >&2; exit 72; }
  grep -Fxq "$expected_summary" "$log" || { cat "$log" >&2; echo "$name summary mismatch" >&2; exit 73; }
}

run_dafny sum-a verify 0 'Dafny program verifier finished with 21 verified, 0 errors' "$run_dir/fold/candidate-a.dfy"
run_dafny sum-b verify 0 'Dafny program verifier finished with 22 verified, 0 errors' "$run_dir/fold/candidate-b.dfy"
run_dafny sum-c verify 4 'Dafny program verifier finished with 20 verified, 1 error' "$run_dir/fold/candidate-c.dfy"

initial_line="$(python3 - "$run_dir/fold/candidate-c.obligations.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1], encoding='utf-8'))
print(next(item['Line'] for item in data['Obligations'] if item['Id']=='owner-prefix-initial'))
PY
)"
grep -Eq "candidate-c\.dfy\(${initial_line},[0-9]+\): Error: assertion might not hold" "$run_dir/logs/sum-c.log" || {
  cat "$run_dir/logs/sum-c.log" >&2
  echo "sum-c did not fail at owner-prefix-initial line $initial_line" >&2
  exit 74
}

run_dafny allocation-primary translate 0 'Dafny program verifier finished with 18 verified, 0 errors' "$run_dir/fold/allocation-primary.dfy" "$run_dir/FoldPrimaryGenerated.cs"
run_dafny allocation-alternative translate 0 'Dafny program verifier finished with 18 verified, 0 errors' "$run_dir/fold/allocation-alternative.dfy" "$run_dir/FoldAlternativeGenerated.cs"

run_unproven() {
  local name="$1" source="$2" needle="$3" exit_code=0
  local log="$run_dir/logs/$name.log"
  timeout 180s "$dafny" verify "$source" --enforce-determinism --cores 2 --verification-time-limit 15 >"$log" 2>&1 || exit_code=$?
  [[ "$exit_code" == 4 ]] || { cat "$log" >&2; echo "$name exit $exit_code, expected 4" >&2; exit 75; }
  [[ $(wc -c <"$log") -le 1048576 ]] || { echo "$name exceeded output limit" >&2; exit 76; }
  ! grep -Eq "$forbidden" "$log" || { cat "$log" >&2; echo "$name emitted an operational failure marker" >&2; exit 77; }
  grep -Fq "$needle" "$log" || { cat "$log" >&2; echo "$name missing expected diagnostic: $needle" >&2; exit 78; }
  grep -Eq '^Dafny program verifier finished with [0-9]+ verified, [1-9][0-9]* errors?$' "$log"
}

run_unproven allocation-weak-invariant "$run_dir/fold/allocation-weak-invariant.dfy" 'subset constraints'
for position in 0 1 2; do
  obligation_file="$run_dir/fold/allocation-input-$position-mutation.obligations.json"
  line="$(python3 - "$obligation_file" "$position" <<'PY'
import json, sys
data=json.load(open(sys.argv[1],encoding='utf-8')); wanted='input-equivalence-'+sys.argv[2]
print(next(item['Line'] for item in data['Obligations'] if item['Id']==wanted))
PY
)"
  run_unproven "allocation-input-$position" "$run_dir/fold/allocation-input-$position-mutation.dfy" "allocation-input-$position-mutation.dfy($line,"
done

consumer_program='using Dafny;
var empty = Candidate.__default.F000(Sequence<long>.Empty, 5L);
var ordered = Candidate.__default.F000(Sequence<long>.FromElements(4L, 3L, 1L), 5L);
var maximum = Candidate.__default.F000(Sequence<long>.FromElements(long.MaxValue), long.MaxValue);
var allocations = string.Join(",", ordered.dtor_R000F000.Select(x => x.ToString()));
Console.WriteLine($"empty={empty.dtor_R000F001}:{empty.dtor_R000F000.LongCount};ordered={ordered.dtor_R000F001}:[{allocations}];maximum={maximum.dtor_R000F001}:{maximum.dtor_R000F000.Select(x => x).Single()}");
return empty.dtor_R000F001 == 5L && empty.dtor_R000F000.LongCount == 0
    && ordered.dtor_R000F001 == 0L && allocations == "4,0,1"
    && maximum.dtor_R000F001 == 0L && maximum.dtor_R000F000.Select(x => x).Single() == long.MaxValue ? 0 : 1;'

for variant in Primary Alternative; do
  project="$run_dir/consumer-$variant"
  mkdir -p "$project"
  cp "$run_dir/Fold${variant}Generated.cs" "$project/Generated.cs"
  printf '%s\n' "$consumer_program" >"$project/Program.cs"
  cat >"$project/Consumer.csproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <Nullable>disable</Nullable>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup><Compile Include="Generated.cs" /><Compile Include="Program.cs" /></ItemGroup>
</Project>
XML
  dotnet run --project "$project/Consumer.csproj" -c Release >"$run_dir/logs/consumer-$variant.log" 2>&1
  grep -Fxq 'empty=5:0;ordered=0:[4,0,1];maximum=0:9223372036854775807' "$run_dir/logs/consumer-$variant.log"
done

python3 - "$run_dir" "$dafny" <<'PY'
import hashlib, json, pathlib, subprocess, sys
root=pathlib.Path(sys.argv[1]); dafny=pathlib.Path(sys.argv[2])
manifest=json.loads((root/'fold/manifest.json').read_text(encoding='utf-8'))
result={
  'schemaVersion':'strogo.fold-discriminator.validation.v0.1',
  'toolchainIdentity':manifest['toolchainIdentity'],
  'toolchainDigest':manifest['toolchainDigest'],
  'ownerDigest':manifest['ownerDigest'],
  'dafnyVersion':subprocess.check_output([str(dafny),'--version'],text=True).strip(),
  'dafnyExecutableSha256':hashlib.sha256(dafny.read_bytes()).hexdigest(),
  'sum':{'a':'Verified','b':'Verified','c':'Unproven(initial)'},
  'allocation':{'primary':'Verified','alternative':'Verified','weakInvariant':'Unproven','inputEquivalenceMutations':'3/3 Unproven','generatedDotnetConsumer':'Pass'},
  'variants':manifest['variants'],
}
(root/'fold-validation.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
PY

echo "PASS fold discriminator and allocation: $run_dir/fold-validation.json"
