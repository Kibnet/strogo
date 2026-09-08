# .NET runtime closure checkpoint `c254468`

Статус checkpoint: **две фактические runtime rows и два A10 runtime-отказа подтверждены; весь A10 ещё не закрыт из-за отсутствующей проверки absent platform row**.

На clean public commit `c254468bb13d118856073279dc5e6ce966e2f6f2` один validation package из checkpoint `aaf2dc2` был проверен до invocation, после чего заранее собранный standalone `Consumer.dll` запущен напрямую target runtime:

- Windows x64: isolated .NET `10.0.11`, `191` closure files, digest `9db719dc1268bb7ae99fef88ba65591ba058ccbd8865493ff486298d2ea2cac8`;
- Linux x64: .NET `10.0.11`, `191` closure files, digest `9349ea1375f117bad1c2f43fb14f399a7cbf47c7ce1067dabcf0ac47e5aba8c0`;
- обе строки использовали manifest `f947db0e3437cb98babe422c6831fa37570b04fd18a8847e12b4620a0119bb8d`, package `4dd1a36f3b430835a0b13241e7bb98a60df451a19befbbbde1801de128d8704e` и artifact set `002c89b04032d9416894f4555ff0bf279be60392ad7ee31b2521ab67840fb08a`;
- consumer outcome в обеих строках: `8+24+1+13`;
- conformance: `107` checks.

Отрицательные Windows-пробы на той же ревизии завершились exit `70` до создания runtime inventory и consumer directory:

- изменённый `System.Collections.dll`: `EnvironmentUnavailable` / `RuntimeClosureDigestMismatch`;
- отсутствующий `Microsoft.NETCore.App/10.0.11`: `EnvironmentUnavailable` / `RuntimeVersionUnavailable`;
- системный multiversion root: `EnvironmentUnavailable` / `AmbiguousRuntimeSelection`.

Consumer build выполнялся отдельно pinned harness SDK `10.0.400`; target invocation имел форму `dotnet Consumer.dll ...` и не требовал SDK в target root. Runtime closure связывает launcher, selected `hostfxr` и `Microsoft.NETCore.App`; ambient OS native libraries остаются явно доверенной platform boundary.

Linux evidence получено в WSL2 на том же Windows host. Это не independent Linux CI, другой physical host, JIT diagnostic evidence, performance result, absent-row aggregation или JVM profile. Профиль всё ещё не получает итоговый статус `Portable`.
