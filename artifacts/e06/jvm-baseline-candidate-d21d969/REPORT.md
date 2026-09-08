# E06A Stage 1 JVM baseline candidate

- Revision: `d21d969ee4f0cdfcc942e741be04b2fb6e13011b`
- Repository state at capture: clean
- Status: `CandidateOnly`; `phase2Allowed=false`; owner approval pending
- Baseline digest: `cafa0caad40e22d1d2ff3803ea350dea95f01c99f099ada75b16ef228035c0e1`
- Normalized diagnostics digest: `c18b3cf91a4d2a47f022738a8b807a30463c5548847c1f6e7eed87571f369d2c`
- Translated source-tree digest: `3d49fd994bee96529ee9dae2ed9536809f8cfb9821d865b4066dd2c156947eed`
- Warnings: `106` (`cast:35`, `rawtypes:67`, `serial:1`, `varargs:3`)
- Raw javac stderr: `51878` bytes, SHA-256 `4bb25969a539e43fee1c1993d13bf1f27c7b824bf36760fb04698f6edc353640` in both lanes
- Raw javac stdout: empty in both lanes
- Probe cleanup: passed in both lanes; no `probe-classes` or `.class` remained
- Tools: Dafny `4.11.0+fcb2042d6d043a2634f0854338c08feeaaaf4ae2`, javac `17.0.19`, .NET SDK `10.0.400`, .NET runtime `10.0.11`
- Validator digest: `94e86850fb5e1e206da5b4974b7ede4a3f36bac8c6688162d39fe87d8cfa3402`
- Harness digest: `2bf8736f8422e9a5936b68c657f99455a14bf8ad0caf8db37cd40f5e25ec296a`

`report.json` and both lane receipts are retained here without physical paths. The exact candidate bytes remain local and untracked at `artifacts/local-validation/e06/jvm-baseline-d21d969/baseline-candidate.json` until the owner approves this digest with the required phrase `Baseline подтверждаю`.

This checkpoint establishes only the Stage 1 observation and identity gate. It does not approve the baseline, run phase 2, create a JAR, establish a JVM runtime row, or claim portability.
