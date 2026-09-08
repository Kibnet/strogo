# .NET platform matrix checkpoint `e222523`

Статус checkpoint: **A10 missing/corrupt runtime и absent mandatory row подтверждены**.

На clean public commit `e222523f6a3ba0f1cb30f7f747207a8090d8d677`:

- conformance прошёл `121` проверку;
- input только с retained Windows row создал две ordered строки: Windows `Passed`, отсутствующая Linux `Unavailable`/`synthesized=true` с `EnvironmentUnavailable` и `RowUnavailable`; `matrixStatus=NotPassed`;
- retained Windows+Linux rows из runtime checkpoint `c254468` дали `matrixStatus=Passed` и один manifest/package/artifact triple;
- изменение только Linux `packageDigest` привело к `PortabilityReportRejected` / `PlatformArtifactIdentityMismatch`, exit `1`, output отсутствует.

Этот `strogo.portability-matrix-check.v0.1` проверяет completeness и identity binding будущего report. `matrixStatus=Passed` не равен profile status `Portable`: JIT, oracle comparison, build reproducibility, JVM profile и общий `strogo.portability-report.v0.1` должны применить дополнительные gates.
