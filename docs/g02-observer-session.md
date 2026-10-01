# Диагностическая file-backed сессия G02

Внутренний `G02SignedFixtureSession.OpenForObserver` предназначен для следующего этапа наблюдения native Q/F входов. Он использует тот же `OpenCore`, owner state/release verifier, held package snapshot и runtime binding, что обычный `Open`. Перед загрузкой выполняются VerifyFresh, snapshot.Revalidate и runtime.Verify. Оба варианта compiled fixture повторно проверяют entry/proof/source regeneration. Диагностический путь загружает manifest-derived entry с диска и повторно проверяет snapshot после загрузки; обычный путь остаётся LoadFromStream.

Режим выбирает trusted diagnostic host. В module, invocation request и public API переключателя нет. Это механизм disposable test-key fixture, а не human-approved публичный допуск. Проверки build/proof входят в общую fixture цепочку; новый вход не создаёт новый публичный способ доверять caller-supplied build record.

InvokeJson одинаков для обоих путей: codec-first, затем fresh owner state/release, snapshot и runtime до увеличения adapter DispatchAttempts и фактического dispatch. Envelope сохраняет package/build/contract/release identities. Adapter count не доказывает native вход.

## Жизненный цикл и предположения

Dispose запрещает новые вызовы, закрывает owned snapshot handles и запрашивает ALC.Unload. CLR может удерживать file mapping при живых ссылках, как показал [lifetime эксперимент](g02-loader-lifetime.md). Диагностический host должен использовать отдельный процесс; его завершение является границей CLR ресурсов. Немедленное эксклюзивное открытие DLL после diagnostic Dispose не обещается. Обычный stream-loader сохраняет свой прежний cleanup contract.

Host namespace/ancestors, trust, provider, clock, pinned SDK/CLR и verified fixture build остаются доверенными. Snapshot Revalidate — checkpoint сериализованного caller, не atomic lease против concurrent hostile namespace/host. Процесс не объявляется sandbox для произвольного вредоносного кода.

## Проверяемый результат

Спецификация: [observer-session-v0.1](../specs/2026-10-01-g02-observer-session-v0.1.md). Conformance использует отдельный package root для каждой формы; все no-load negatives выполняются до первой file-backed загрузки. AssemblyLoad observer сохраняет только count и строку Location. Положительный ответ сравнивается с owner witness и независимым reference evaluator. Late provider/runtime/expiry/epoch/rollback и disposed refusals сверяются с provider/dispatch delta.

Native observer, его callback trace и соединение PID/MVID/token/file SHA с signed response остаются следующим шагом. Наличие этого входа не завершает G02 и не доказывает G05/G06.

### Свежая runtime проверка
Полный Admission apphost session12712 завершился exit0:492 checks; solution build0warnings/0errors. Scalar/allocation signed-session каждый82checks/49rows, диагностическая часть16rows/6no-load controls; actual Location совпал с normalised manifest entry, load count1/state reads1. Actual returned witness/reference и signed identities сверены; late refusals имеют zero dispatch delta, disposed имеет zero state read. [Evidence](evidence/g02-observer-session-20261001/README.md):4source/4binary hashes,10exactcopies с failed history. Native trace ещё не запускался.

Финальный независимый source/evidence audit: PASS, новых B/H/M/L нет. Сверены4/4current source hashes,4/4current binaries и10/10exact copies с raw originals. Fresh exit0/build0/0/Admission492PASS. Обе формы82checks/49rows:16diagnosticrows,6zero-load/exclusive-reopen controls; actual Location совпал с normalized manifest path, load1/read1; positive actual values совпали с исходными compiled owner/reference/output vectors и stream result. Package/approval identities сверены с manifest, release/build проверены исходным тестом. Malformed/provider/runtime/expiry/epoch/rollback/disposed deltas соответствуют AC; recovery Returned. Failed history сохранена, Path.GetFullPath и pre-assert load observation проверены. Adapter не native callback, public admission не выполнен, CLR unmap не обещан; ancestor namespace TCB и следующий native integration явно ограничены. Audit behavioral read-only при danger-full-access, без consumer rerun. Unlimotion global validate isValid=false, новый result не записан; цель активна.
