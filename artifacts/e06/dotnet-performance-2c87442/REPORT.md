# .NET performance diagnostic checkpoint `2c87442`

Статус checkpoint: **A11 подтверждён для `dotnet-managed.v1` в Windows x64 и Linux x64 под WSL2 как диагностическое наблюдение без вывода G06**.

На clean public commit `2c874422037ce143b25fb231af4e61a9c62ea528` один canonical package из `aaf2dc2a48e99c01cace4ab9d12b4f90a83ffcea` был проверен до каждого измерения. Обе строки использовали один manifest/package/artifact triple:

- manifest: `f947db0e3437cb98babe422c6831fa37570b04fd18a8847e12b4620a0119bb8d`;
- package: `4dd1a36f3b430835a0b13241e7bb98a60df451a19befbbbde1801de128d8704e`;
- artifact set: `002c89b04032d9416894f4555ff0bf279be60392ad7ee31b2521ab67840fb08a`;
- entry assembly: `189440` bytes; artifact set: `312421` bytes; package tree: `315714` bytes.

Standalone consumer вызывает публичный `ModuleApi.Invoke` с exact JSON request `summarize([4,-3,1])` и проверяет exact success response при каждом вызове. Поэтому throughput включает JSON transport и boundary validation, доступные внешнему потребителю. Generated candidate отдельно не измерялся.

Каждая OS row содержит пять отдельных cold-start процессов и один отдельный throughput process: `5000` warmup calls, затем `5 × 10000` measured calls. Все процессы завершились раньше 180-second deadline с пустым stderr; PID в output совпал с запущенным PID. JIT diagnostic overrides перед каждым процессом удалялись.

| Наблюдение | Windows x64 | Linux x64 под WSL2 |
| --- | ---: | ---: |
| Runtime | .NET `10.0.11`, closure `9db719…cac8` | .NET `10.0.11`, closure `9349ea…a8c0` |
| Controller / clock | PowerShell `7.6.5` / `Stopwatch` | CPython `3.12.3` / `monotonic_ns` |
| Cold-start wall time, ms | `688.6, 484.0, 408.5, 426.7, 540.9` | `573.3, 692.5, 514.0, 454.5, 633.5` |
| Throughput, operations/s | `7054, 22725, 30983, 32553, 32764` | `9088, 26102, 35471, 31798, 27928` |
| Throughput process peak working set | `52756480` bytes | `67354624` bytes |

Portability conformance прошёл `138` проверок. Отчёты имеют `assertionBoundary: DiagnosticOnlyNoG06`; raw timing и memory не входят в semantic digest и не присваивают profile статус `Portable`.

Граница вывода: это по одному последовательному run на общей физической Windows-машине. Linux выполнен в Ubuntu 24.04 WSL2, поэтому строки не независимы по hardware/operations. Нет baseline на человекоориентированном языке, статистической серии между машинами, нормализации CPU/power state или проверки production workload. Evidence закрывает формат и фактическое выполнение A11 для .NET profile, но не доказывает G06 и не закрывает JVM profile.
