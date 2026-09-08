# E05 fold v0.1 — evidence for implementation `134856f`

## Result

The root-only bounded left fold passed the frozen proof/runtime matrix under Ubuntu 24.04 WSL2 x86_64 with Linux .NET SDK 10.0.400 and official Dafny 4.11.0:

- Modules conformance: `PASS conformance checks=362`, repeated twice;
- sum A: `21 verified, 0 errors`;
- sum B: `22 verified, 0 errors`;
- false control C: `20 verified, 1 error` at the mapped `owner-prefix-initial` obligation;
- primary and alternative `AllocationState` candidates: each `18 verified, 0 errors`;
- weak allocation invariant and all three type-correct input mutations: expected `Unproven` outcomes with case-specific diagnostics;
- generated .NET consumers for both allocation candidates: empty=`5/[]`, ordered=`0/[4,0,1]`, maximum=`0/[I64.MAX]`.

The exact v0.2 summary is [fold-validation.json](fold-validation.json). It binds sum and allocation to their separate owner digests and records candidate/proof/source identities for each allocation case. Generated Dafny sources, the two family manifests and obligation maps are retained under [sources](sources); strict per-case outputs are under [logs](logs). [modules.json](modules.json) is the conformance report from the first of the two byte-comparison runs.

## Interpretation

A and B both verified while C failed at the required initial obligation. This is one of the outcomes fixed in the approved experiment: the full semantic invariant B was not required to obtain `Verified` for this sum fixture on this frozen lowering revision. The observation does not measure solver cost, proof stability or the value of individual B conjuncts.

The allocation proof exercises a record accumulator containing a bounded sequence and confirms that the lowerer is not limited to scalar sum. It still relies on the first-slice restriction that candidate and owner use the same accumulator representation.

## Identity and determinism

The implementation of the fold lowerer is public commit `134856f489a4e5f34ac0149d0c789b3021e43692`; the strengthened evidence harness ran from public revision `c7d02c674d12532d8a7768857976e65910b58ed4`. Toolchain identity is `strogo.fold-dafny-lowering.v0.4`; its identity digest is `bf4c767508b6a4fc44986e7d1fdd9681cd74fa6982b196ed671640af3b4f9898`. The distinct compiler source-tree digest is `4b4764eff5a77ffa37bf560b5d6de6d3edbed06d1b8188846bec5757c5c2a36a`, the harness/input source-tree digest is `198374219fd307c8114a2c87578cb3334fb3bef569051428534300c858cc665a`, and both relevant source sets were clean. Dafny executable SHA-256 is `e540b4826363afb87c326446239a682d45086905425fa6299c103eca9693846d`.

The harness generated the complete output tree twice and compared every source, normalized source, obligation map and manifest byte-for-byte. A/B/C have distinct source and proof identities but one normalized source digest after replacement of all invariant-dependent spans.

## Reproduction

Install .NET SDK 10.0.400 and official Dafny 4.11.0, then run from a Linux checkout:

```bash
export DOTNET_ROOT=/path/to/dotnet-sdk-10.0.400
export PATH="$DOTNET_ROOT:$PATH"
bash tools/Test-Fold-Discriminator.sh \
  --dafny /path/to/dafny-4.11.0/dafny \
  --run-dir "$PWD/artifacts/local-validation/e05/fold-replay"
```

`--run-dir` may point outside the repository. The generated consumer project declares its required SDK settings explicitly; a full run in `/tmp/strogo-fold-external-134856f-implicit-usings` passed after this portability property was reviewed.

## Boundary

This package proves the recorded outcomes for one WSL2 environment and pinned toolchain. It is not CI or native-Linux evidence. It does not implement or validate nested folds, imports/helper contracts, relational accumulator representations, admission/package binding, a public runtime precondition facade, G05 development-cost advantage or G06 performance parity. Generated C# and `bin/obj` are omitted because they are reproducible and substantially larger than the source evidence.
