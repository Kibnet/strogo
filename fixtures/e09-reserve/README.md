# E09 reserve calibration corpus

The trusted corpus is materialized by `Strogo.Experiments.CalibrationCorpus`.
It contains exactly twelve positive equivalence vectors (`R01`–`R12`) and four
fixed reserve inputs per vector, including accepted, rejected and I64 boundary
rows. The expected graph is kept inside the evaluator fixture and is never
written into an exported `job-starter` bundle. Exported starters are deliberate
invalid candidates with scripted provenance.

The fixture is offline and deterministic. It contains no model responses,
provider credentials, repository paths or benchmark claims.
