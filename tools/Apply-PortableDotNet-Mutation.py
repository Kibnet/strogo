#!/usr/bin/env python3
import argparse
import hashlib
import json
from pathlib import Path


EXPECTED_CANDIDATE_SHA256 = "7893378a45cdd13d05296b80a623ba9553f42c2f2a7d1fc4a5def1f30ffd344e"
EXPECTED_ADAPTER_SHA256 = "0a5bc2634ae8a9ce4511fcfd030b8d446b7403195f2472333a4a3704b9f497e1"

MUTATIONS = {
    "flip-sum-sign": (
        "candidate",
        "_10_v009 = (_8_v007) + ((p000).Select(_5_i004));",
        "_10_v009 = (_8_v007) - ((p000).Select(_5_i004));",
    ),
    "reverse-input-sequence": (
        "candidate",
        "_9_v008 = Dafny.Sequence<long>.Concat(_6_v005, Dafny.Sequence<long>.FromElements((p000).Select(_5_i004)));",
        "_9_v008 = Dafny.Sequence<long>.Concat(Dafny.Sequence<long>.FromElements((p000).Select(_5_i004)), _6_v005);",
    ),
    "force-eager-head": (
        "candidate",
        "if (_2_v002) {",
        "if ((_0_v000) < 0L) {",
    ),
    "alter-refusal-code": (
        "adapter",
        'findings.Add(new("InvalidI64Encoding", locus + "/value", [("value", text)]));',
        'findings.Add(new("InvalidI64EncodingMutant", locus + "/value", [("value", text)]));',
    ),
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--mutation", required=True, choices=sorted(MUTATIONS))
    parser.add_argument("--candidate", required=True, type=Path)
    parser.add_argument("--adapter", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    args = parser.parse_args()

    candidate_before = sha256(args.candidate)
    adapter_before = sha256(args.adapter)
    if candidate_before != EXPECTED_CANDIDATE_SHA256:
        raise SystemExit(f"candidate digest mismatch: {candidate_before}")
    if adapter_before != EXPECTED_ADAPTER_SHA256:
        raise SystemExit(f"adapter digest mismatch: {adapter_before}")

    target_name, old, new = MUTATIONS[args.mutation]
    target = args.candidate if target_name == "candidate" else args.adapter
    text = target.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"mutation anchor count for {args.mutation}: expected 1, actual {count}")
    target.write_text(text.replace(old, new), encoding="utf-8", newline="\n")

    candidate_after = sha256(args.candidate)
    adapter_after = sha256(args.adapter)
    if target_name == "candidate" and adapter_after != adapter_before:
        raise SystemExit("candidate mutation changed adapter")
    if target_name == "adapter" and candidate_after != candidate_before:
        raise SystemExit("adapter mutation changed candidate")

    report = {
        "schemaVersion": "strogo.dotnet-target-mutation.v0.1",
        "mutationId": args.mutation,
        "target": target_name,
        "anchorMatches": count,
        "candidateBeforeSha256": candidate_before,
        "candidateAfterSha256": candidate_after,
        "adapterBeforeSha256": adapter_before,
        "adapterAfterSha256": adapter_after,
    }
    args.report.write_text(
        json.dumps(report, ensure_ascii=True, separators=(",", ":")) + "\n",
        encoding="utf-8",
        newline="\n",
    )


if __name__ == "__main__":
    main()
