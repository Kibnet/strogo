# Linux Dafny semantic matrix — commit `8b53cdd`

## Result

On Ubuntu 24.04.2 WSL2 x86_64, Linux .NET SDK 10.0.400 first generated 19 Dafny candidates from clean git archive `8b53cddb6d143cc1517ba07d6e7597255b876894` and passed Modules conformance with 276 checks / 65 fixtures. Linux Dafny `4.11.0+fcb2042d6d043a2634f0854338c08feeaaaf4ae2` then produced all 19 expected outcomes:

- 10 successful verifications/translations with exit `0` and `0 errors`;
- 9 expected proof refusals with exact exit `4`, a completed verifier summary and the case-specific diagnostic.

The raw outcome table is [results.tsv](results.tsv). Per-case output is retained under [logs](logs), and the exact generated inputs are under [sources](sources). [modules-v0.2.json](modules-v0.2.json) is the preceding conformance report.

## Tool provenance

The official GitHub release asset and API metadata are retained in [dafny-asset.json](dafny-asset.json). Downloaded archive SHA-256 `a46a9ff7cdd720f7955854c78e95df13f4cfe6b80691b05f8654fe19e8267179` matched the API digest. The environment snapshot is [environment.txt](environment.txt); local host and temporary-directory names are normalized.

## Reproduction

From a Linux checkout containing commit `8b53cdd`:

```bash
export EVIDENCE_DIR="$PWD/artifacts/local-validation/e05/linux-dafny-replay"
bash artifacts/e05/linux-dafny-wsl2-8b53cdd/reproduce.sh
```

[reproduce.sh](reproduce.sh) preserves the executed generation and Dafny command matrix while parameterizing the original absolute repository/output paths. Its reusable refusal oracle is deliberately stricter than the first local driver: each negative case requires exact Dafny exit `4`, its exact completed verifier summary and its expected diagnostic, while known operational/model-parser markers are forbidden. This prevents a timeout such as exit `124` or a model parsing failure accompanying a proof diagnostic from being mistaken for an expected refusal. The known archive SHA-256 is enforced even if GitHub Releases API omits its optional digest; a nonempty API digest is checked as a second source.

## Integrity and boundary

[sha256.txt](sha256.txt) binds the retained public package. The original local `results.tsv` SHA-256 is `62901739cb6dde4c8d4bf833303cc3136af814973b2138643d9f69279e13c72d`; the Modules report SHA-256 is `7fd86486be41dcda91c6c784b7b97b3f1e2c52278dfbb530655f78908b4c1617`.

This evidence covers generated Dafny proof/translation semantics under WSL2. It does not cover a native Linux host, CI, portable process-tree containment, execution of generated consumers or Linux ReadyToRun. Generated C# files include the Dafny runtime repeatedly and are omitted; every successful `translate cs` command and its completed verifier output can be reproduced from the retained source and driver.
