# E06 .NET target mutations на `590d3dd`

Это санитизированное свидетельство выполнения A7 для текущей части `dotnet-managed.v1`. Оно не является итоговым portability package и не переводит весь E06 в `Portable`.

## Результат

- Публичный clean commit: `590d3dd0cf41e9b4a8b7dc96c8aab26f93ba18b8`.
- Baseline DLL: SHA-256 `37cb02dc8be37fe005d6515421aaba97b069168e70f52d9e8ba2ab019fe314fc`; две translation/build lanes снова byte-equal, полный baseline consumer прошёл `8 + 24 + 1 + 13` случаев.
- Четыре baseline probes прошли owner oracle; те же четыре probes в режиме mutant отвергли неизменённую DLL, поэтому сам probe не может выдать ложный `Detected` для baseline.
- Все четыре отдельные mutated DLL собрались с `0 warnings / 0 errors`.
- `flip-sum-sign`, `reverse-input-sequence` и `alter-refusal-code` дали exact `BackendSemanticMismatch` на закреплённых vector/locus.
- `force-eager-head` дал exact `TargetExecutionFailed` на `head-empty`, locus `function/headOrZero/result`, exception `System.IndexOutOfRangeException`.

`report.json` связывает каждый результат с digest mutated DLL. `mutations/*.json` связывают каждую мутацию с exact before/after digest generated candidate либо adapter и требуют ровно одно совпадение source anchor. Большие generated sources и DLL мутантов не дублируются: их воспроизводят tracked `tools/Apply-PortableDotNet-Mutation.py` и `tools/Test-PortableDotNet-Mutations.sh` из pinned source `artifacts/e06/wire-proof-9f0b2e3/a/candidate.dfy`.

## Воспроизведение

Из clean checkout `590d3dd` под Ubuntu 24.04 WSL2 с pinned Dafny 4.11.0 и .NET SDK 10.0.400:

```bash
tools/Test-PortableDotNet-Mutations.sh \
  --dafny /path/to/pinned/dafny \
  --dotnet /path/to/pinned/dotnet \
  --source artifacts/e06/wire-proof-9f0b2e3/a/candidate.dfy \
  --run-dir /new/absolute/evidence-directory
```

Driver fail closed проверяет версии/digests baseline toolchain и source через общий build driver, требует новый каталог, успешную строгую сборку каждого мутанта, точный changed outcome или exception и отрицательные проверки неизменённой DLL.

## Граница утверждения

Этот пакет закрывает .NET-исполнение четырёх обязательных target mutations. JVM-мутации, canonical portability manifest/package, runtime closure digests, unavailable/corrupt-runtime cases, performance и JIT diagnostics остаются открыты. Mutations показывают различительную способность frozen oracle на четырёх известных дефектах; они не доказывают отсутствие других дефектов compiler, adapter, runtime или JIT.
