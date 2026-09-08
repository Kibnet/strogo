# .NET JIT diagnostic checkpoint `ca5ae50`

Статус checkpoint: **A12 подтверждён для `dotnet-managed.v1` в Windows x64 и Linux x64 под WSL2**.

На clean public commit `ca5ae50478d29bba31ac862e5ef3a571068f9ada` один ранее закреплённый package triple исполнялся через две точные .NET `10.0.11` runtime closures:

- Windows: `9db719dc1268bb7ae99fef88ba65591ba058ccbd8865493ff486298d2ea2cac8`;
- Linux: `9349ea1375f117bad1c2f43fb14f399a7cbf47c7ce1067dabcf0ac47e5aba8c0`.

До запуска каждый runner проверил package и построил `strogo.dotnet-jit-plan.v0.1`. План связал public API и source map с `moduleDigest`/`dafnySourceDigest`, затем выбрал два exact symbols:

- `Strogo.Portable.V01.ModuleApi:Invoke`;
- `Candidate.__default:F004`, где `F004` получен из source-map entity `function/summarize`.

Standalone consumer в каждом процессе выполнил `50000` вызовов exact `summarize([4,-3,1])`. При `DOTNET_ReadyToRun=0`, `DOTNET_TieredCompilation=0`, `DOTNET_JitNoInline=1` и bounded method-filtered `DOTNET_JitDisasm` каждый receipt сохранил ровно два `FullOpts` compilation events с полными signatures. PID из consumer output совпал с PID, запущенным runner; deadline каждого процесса — 180 секунд; JIT logs меньше 1 MiB.

Conformance прошёл `131` проверку. Self-tests на synthetic log требуют `JitEvidenceMissing` для decoy, отсутствующего/неполного/неоднозначного event и недостаточного call count. Дополнительно два actual-log negative cases заменили candidate symbol на `F004Decoy` либо удалили все candidate lines: оба завершились exit `1`, не создали receipt, оставили пустой stdout и вернули `JitEvidenceMissing` на `$/events/candidate`.

Граница вывода: evidence подтверждает факт JIT compilation этих двух методов в конкретных process/runtime closures. Оно не доказывает корректность или byte stability машинного кода, не является performance measurement и не закрывает JVM-половину A12. Linux row — фактическое выполнение в Ubuntu WSL2 на той же машине, а не независимый hardware/operations runner.
