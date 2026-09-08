#!/usr/bin/env bash
set -euo pipefail

commit=8b53cddb6d143cc1517ba07d6e7597255b876894
repo_source="${REPO_SOURCE:-$(git rev-parse --show-toplevel)}"
evidence_dir="${EVIDENCE_DIR:?set EVIDENCE_DIR to an absolute output path}"
work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

mkdir -p "$evidence_dir/sources" "$evidence_dir/logs" "$evidence_dir/generated"
exec > >(tee "$evidence_dir/console.log") 2>&1

curl -fsSL https://api.github.com/repos/dafny-lang/dafny/releases/tags/v4.11.0 -o "$evidence_dir/dafny-release.json"
asset_url=https://github.com/dafny-lang/dafny/releases/download/v4.11.0/dafny-4.11.0-x64-ubuntu-22.04.zip
curl -fL "$asset_url" -o "$work_dir/dafny.zip"
archive_sha256="$(sha256sum "$work_dir/dafny.zip" | cut -d' ' -f1)"
api_digest="$(python3 - "$evidence_dir/dafny-release.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding='utf-8') as f:
    release = json.load(f)
name = 'dafny-4.11.0-x64-ubuntu-22.04.zip'
matches = [asset for asset in release['assets'] if asset['name'] == name]
if len(matches) != 1:
    raise SystemExit(f'expected one {name} asset, found {len(matches)}')
print(matches[0].get('digest') or '')
PY
)"
if [[ -n "$api_digest" && "$api_digest" != "sha256:$archive_sha256" ]]; then
  echo "Dafny asset digest mismatch: api=$api_digest local=sha256:$archive_sha256" >&2
  exit 1
fi
unzip -q "$work_dir/dafny.zip" -d "$work_dir/dafny"
dafny="$work_dir/dafny/dafny/dafny"
chmod +x "$dafny"
dafny_version="$($dafny --version)"

curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$work_dir/dotnet-install.sh"
bash "$work_dir/dotnet-install.sh" --version 10.0.400 --install-dir "$work_dir/dotnet" --no-path
export DOTNET_ROOT="$work_dir/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

mkdir -p "$work_dir/repo"
git -C "$repo_source" archive "$commit" | tar -x -C "$work_dir/repo"
cd "$work_dir/repo"

dotnet run --project tests/Strogo.Modules.Conformance/Strogo.Modules.Conformance.csproj -c Release -- \
  --report "$evidence_dir/modules-v0.2.json" \
  --dafny-out "$evidence_dir/sources/Candidate.dfy" \
  --dafny-call-out "$evidence_dir/sources/CallCandidate.dfy" \
  --dafny-unsafe-out "$evidence_dir/sources/UnsafeCandidate.dfy" \
  --dafny-scalar-out "$evidence_dir/sources/ScalarCandidate.dfy" \
  --dafny-composite-out "$evidence_dir/sources/CompositeCandidate.dfy" \
  --dafny-composite-unsafe-out "$evidence_dir/sources/UnsafeCompositeCandidate.dfy" \
  --dafny-owner-out "$evidence_dir/sources/OwnerCandidate.dfy" \
  --dafny-owner-weak-out "$evidence_dir/sources/WeakOwnerCandidate.dfy" \
  --dafny-owner-alternative-out "$evidence_dir/sources/AlternativeOwnerCandidate.dfy" \
  --dafny-owner-wrong-out "$evidence_dir/sources/WrongOwnerCandidate.dfy" \
  --dafny-owner-composite-out "$evidence_dir/sources/CompositeOwnerCandidate.dfy" \
  --dafny-owner-composite-alternative-out "$evidence_dir/sources/AlternativeCompositeOwnerCandidate.dfy" \
  --dafny-owner-composite-wrong-out "$evidence_dir/sources/WrongCompositeOwnerCandidate.dfy" \
  --dafny-owner-composite-partial-out "$evidence_dir/sources/PartialCompositeOwnerCandidate.dfy" \
  --dafny-owner-strict-and-out "$evidence_dir/sources/StrictAndOwnerCandidate.dfy" \
  --dafny-owner-strict-or-out "$evidence_dir/sources/StrictOrOwnerCandidate.dfy" \
  --dafny-owner-guarded-false-out "$evidence_dir/sources/GuardedFalseOwnerCandidate.dfy" \
  --dafny-owner-guarded-true-out "$evidence_dir/sources/GuardedTrueOwnerCandidate.dfy" \
  --dafny-owner-append-partial-out "$evidence_dir/sources/AppendPartialOwnerCandidate.dfy"

printf 'case\tmode\texit\texpectation\n' > "$evidence_dir/results.tsv"

run_case() {
  local name="$1" mode="$2" file="$3" expectation="$4"
  local expected_summary="$5"
  local log="$evidence_dir/logs/$name.log"
  local generated="$evidence_dir/generated/$name.cs"
  local exit_code=0
  if [[ "$mode" == translate ]]; then
    timeout 180 "$dafny" translate cs "$evidence_dir/sources/$file" --include-runtime --enforce-determinism --cores 2 --verification-time-limit 15 --output "$generated" >"$log" 2>&1 || exit_code=$?
  else
    timeout 180 "$dafny" verify "$evidence_dir/sources/$file" --enforce-determinism --cores 2 --verification-time-limit 15 >"$log" 2>&1 || exit_code=$?
  fi
  grep -Fxq "$expected_summary" "$log"
  case "$expectation" in
    verified:*)
      [[ "$exit_code" -eq 0 ]]
      ;;
    reject:*)
      local needle="${expectation#reject:}"
      [[ "$exit_code" -eq 4 ]]
      grep -Fq "$needle" "$log"
      ;;
    reject3:*)
      local needle="${expectation#reject3:}"
      [[ "$exit_code" -eq 4 ]]
      [[ "$(grep -Fc "$needle" "$log")" -ge 3 ]]
      ;;
    reject2:*)
      local needle="${expectation#reject2:}"
      [[ "$exit_code" -eq 4 ]]
      [[ "$(grep -Fc "$needle" "$log")" -ge 2 ]]
      ;;
    *) echo "unknown expectation: $expectation" >&2; return 1 ;;
  esac
  printf '%s\t%s\t%s\t%s\n' "$name" "$mode" "$exit_code" "$expectation" >> "$evidence_dir/results.tsv"
  echo "PASS $name exit=$exit_code expectation=$expectation"
}

run_case safe-selector translate Candidate.dfy verified:2 'Dafny program verifier finished with 2 verified, 0 errors'
run_case safe-call translate CallCandidate.dfy verified:3 'Dafny program verifier finished with 3 verified, 0 errors'
run_case safe-scalar translate ScalarCandidate.dfy verified:2 'Dafny program verifier finished with 2 verified, 0 errors'
run_case safe-composite verify CompositeCandidate.dfy verified:4 'Dafny program verifier finished with 4 verified, 0 errors'
run_case unsafe-composite verify UnsafeCompositeCandidate.dfy 'reject3:assertion might not hold' 'Dafny program verifier finished with 5 verified, 3 errors'
run_case unsafe-arithmetic verify UnsafeCandidate.dfy "reject:might violate newtype constraint for 'I64'" 'Dafny program verifier finished with 1 verified, 1 error'
run_case owner translate OwnerCandidate.dfy verified:4 'Dafny program verifier finished with 4 verified, 0 errors'
run_case composite-owner translate CompositeOwnerCandidate.dfy verified:5 'Dafny program verifier finished with 5 verified, 0 errors'
run_case alternative-composite-owner translate AlternativeCompositeOwnerCandidate.dfy verified:5 'Dafny program verifier finished with 5 verified, 0 errors'
run_case wrong-composite-owner verify WrongCompositeOwnerCandidate.dfy 'reject:a postcondition could not be proved' 'Dafny program verifier finished with 4 verified, 1 error'
run_case partial-composite-owner verify PartialCompositeOwnerCandidate.dfy 'reject:index out of range' 'Dafny program verifier finished with 3 verified, 2 errors'
run_case strict-and verify StrictAndOwnerCandidate.dfy "reject:might violate newtype constraint for 'I64'" 'Dafny program verifier finished with 3 verified, 1 error'
run_case strict-or verify StrictOrOwnerCandidate.dfy "reject:might violate newtype constraint for 'I64'" 'Dafny program verifier finished with 3 verified, 1 error'
run_case guarded-false verify GuardedFalseOwnerCandidate.dfy verified:4 'Dafny program verifier finished with 4 verified, 0 errors'
run_case guarded-true verify GuardedTrueOwnerCandidate.dfy verified:4 'Dafny program verifier finished with 4 verified, 0 errors'
run_case append-partial verify AppendPartialOwnerCandidate.dfy 'reject:subset constraints' 'Dafny program verifier finished with 4 verified, 1 error'
run_case weak-owner verify WeakOwnerCandidate.dfy "reject2:result of operation might violate newtype constraint for 'I64'" 'Dafny program verifier finished with 2 verified, 2 errors'
run_case alternative-owner translate AlternativeOwnerCandidate.dfy verified:4 'Dafny program verifier finished with 4 verified, 0 errors'
run_case wrong-owner verify WrongOwnerCandidate.dfy 'reject:a postcondition could not be proved' 'Dafny program verifier finished with 3 verified, 1 error'

cat > "$evidence_dir/environment.txt" <<EOF
commit=$commit
uname=$(uname -a)
os_release=$(tr '\n' ' ' < /etc/os-release)
dotnet_sdk=$(dotnet --version)
dotnet_runtime=$(dotnet --list-runtimes | tr '\n' ';')
dafny_version=$dafny_version
dafny_asset_url=$asset_url
dafny_archive_sha256=$archive_sha256
dafny_api_digest=$api_digest
EOF

find "$evidence_dir" -type f ! -name sha256.txt -print0 | sort -z | xargs -0 sha256sum > "$evidence_dir/sha256.txt"
echo 'PASS linux Dafny semantic matrix: 19/19 cases'
