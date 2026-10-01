# G02 — File-backed диагностическая сессия с общими проверками допуска

## 0. Метаданные
Expanded SPEC, high-risk runtime/admission boundary; ветка main, исходная ревизия dfd8deb. Центральный QUEST/review stack и локальный AGENTS.md применяются. Профиль .NET library/testing, поверхность Codex PowerShell Windows; model-specific eval не применим: меняется механизм runtime, не prompting. SDK10.0.400/runtime10.0.11 win-x64 закреплены. Связи: [родитель G02](2026-09-29-g02-dotnet-r2r-admitted-modules-v0.1.md), [эксперимент lifetime](../docs/g02-loader-lifetime.md). Подтверждение механизмов в пределах цели дано владельцем; нормативная public closure identity и D02 human bundle не входят в это разрешение.

## 1. Цель и outcome contract
Следующий результат G02 AC3/AC5 — наблюдать реальные Q/F входы в подписанном fixture runtime. Данная зависимая часть предоставляет file-backed диагностический вход с теми же owner/release/package/runtime gates. Success: диагностическая сессия возвращает фактический результат и отказывает до загрузки/dispatch при нарушении тех же проверок; обычный stream-loader сохраняет поведение. Артефакт: внутренние factory, проверка двух форм fixture и отчёт. Это не завершение signed observer, public admission, G02 или всей цели. Stop: не обходить verifier ради Location, не менять публичную модель допуска; на несовпадении gate/результата исправлять механизм.

## 2. AS-IS
G02SignedFixtureSession.Open вызывает общий constructor: VerifyFresh, snapshot.Revalidate, runtime.Verify, затем G02CompiledFixture.Open. Последний проверяет entry/proof/source regeneration и LoadFromStream. InvokeJson повторяет fresh checks до DispatchAttempts. Snapshot держит FILE_SHARE_READ файлы и directory identity. G02CompiledDispatch сохраняет Assembly/MethodInfo после Dispose. Эксперимент dfd8deb показал file-backed lock при живых ссылках; потоковый путь допускает exclusive reopen после освобождения собственных handles. OwnerHostContext и fresh proof replay существуют, но пока не связаны с native observer.

## 3. Проблема
Native observer GetModuleInfo2 требует физическое имя DLL, которого LoadFromStream не предоставляет. Замена обычного loader нарушает текущий lifecycle contract.

## 4. Цели дизайна
Одна реализация verifier gates, внутренний явно названный диагностический вход, отсутствие выбора loader в module/request/public API; reuse actual dispatch и typed refusals. Сохранить обычный Open и JSON response schema.

## 5. Non-goals
Новый public run/check/admit; owner human approval; физический closure digest; mapped-memory attestation; native callbacks в этом checkpoint; G05/G06. Не менять требования пользователя. Не обещать synchronous CLR unmap.

## 6. TO-BE
### 6.1 Ответственность
G02PackageSnapshot: internal путь entry через held root/validated manifest, без caller-selected filename.
G02CompiledFixture: existing Open остаётся stream; internal OpenForObserver загружает entry из snapshot по LoadFromAssemblyPath. Оба используют один constructor и одинаковые VerifyEntryArtifactsFixture/VerifyHeld/source regeneration/IR/bundle шаги. snapshot удерживает immutable file/path на всём lifetime; повторная Revalidate перед file load, после load перед dispatch creation. На ошибке Unload; caller закрывает snapshot.
G02SignedFixtureSession: existing Open и internal OpenForObserver используют единую private OpenCore и constructor с private диагностическим выбором, после одинаковых VerifyFresh/snapshot/runtime gates выбирают fixture factory. Module/request не управляет режимом. InvokeJson/VerifyFresh/response/Dispose не ветвятся по loader.
Conformance: отдельная копия package для diagnostic tests, чтобы не загрязнить существующие exclusive-reopen checks. Не хранить Assembly в event observer; сохранять только scalar Location/count.

### 6.2 Контракты
Не добавлять public bool или fallback. Внутренний physical path строится только из rootDirectory и Manifest.EntryAssemblyPath, уже проверенных native snapshot. Путь absolute; snapshot.Revalidate сверяет held path/root identity. Загрузка не копирует DLL в непроверенную директорию. File-backed diagnostic Dispose закрывает owned snapshot handles, запрещает новые вызовы, запрашивает Unload; CLR lock может сохраняться до collection/выхода процесса. Будущий observer запускается в отдельном холодном процессе, завершение которого — граница CLR ресурсов. Текущие тесты не используют этот factory в обычном CLI. Нет гарантий sandbox/in-process malicious-code isolation.
UI/video не применимы: backend library.

### 6.3 Сценарии и AC
|AC|Trigger|Результат|Evidence|
|---|---|---|---|
|1|bad signature/package digest/expired release/epoch/provider failure/runtime mismatch до OpenForObserver|typed refusal, zero assembly loads; handles можно открыть exclusively|actual load event/count и reopen в отдельном root|
|2|valid diagnostic Open и JSON witness на scalar/allocation|одна загрузка, Location совпадает с held entry path; actual output совпадает с owner/reference и response identities|JSON report + scalar event path|
|3|malformed JSON после Open|SchemaInvalid, provider не читается, dispatch count unchanged|read/attempt delta|
|4|late runtime mismatch/provider failure/release expiry/epoch update|refusal before dispatch; успешное восстановление возможно лишь без rollback нарушения Trust|response и count delta|
|5|diagnostic Dispose с rooted session|disposed refusal; не утверждать exclusive write/CLR unload; stream checks прежние|Dispose response, ordinary full suite|
|6|regression и documentation|обычный путь проходит прежние проверки; новый internal factory не public bypass|pinned solution build + full Admission apphost + review|

### 6.4 Матрица состояний
Closed → valid Open → Loaded; invalid Open → Closed/refused без load. Loaded → valid invoke → Loaded/result. Loaded → stale state/runtime/release → Loaded/refusal без dispatch. Loaded → Dispose → Disposed; Disposed → any invoke → refusal без state read. Nonconcurrent: как существующая fixture session; новая concurrency semantics не вводится.

### 6.5 Decision ledger
File-backed режим выбран только для диагностики, по evidence K-G02-042. Общий constructor снижает риск расхождения gates. Обычный loader сохраняется, так как его cleanup contract уже проверен. Не использовать сериализованный caller build record как public admission. Значимое расширение signed observer tooling — следующая проверяемая часть родительского G02, не скрытая реализация здесь.

## 7. Риски и TCB
TCB прежний: pinned SDK/CLR/native snapshot/verified fixture build/trust/provider/clock и actual dispatch. Diagnostic factory internal/friend, не граница против произвольного trusted-host caller. File-backed mapping живёт дольше Dispose; тестовый root сохраняется до завершения процесса, нет Delete/retry/GC обещания. Проверить package/path immutability перед load. На no-load controls использовать свежий diagnostic root прежде первого положительного load. Event handler не удерживает Assembly. Новые тесты не засчитывать как proof всех compiler свойств.

## 8. Acceptance/test plan
AC1–5 добавить к существующему G02SignedSessionChecks отдельно от stream root, по одному diagnostic root на fixture. Release подписывается disposable test key, не human-owned release. Runtime negative через существующий internal observation provider, с пометкой synthetic. В negative до load проверять count unchanged и exclusive reopen. Positive сравнивает actual response value с owner witness и reference. Перед dispose сохранить JSON evidence; disposed invoke count/provider delta проверять явно. AC6 pinned solution build0/0 и full Admission apphost; сохранить fresh raw результаты отдельно. Не считать старые444 текущим прогоном. Native observation будет отдельным запуском, adapter count здесь не native evidence.

## 9. Delivery/rollback
Планируемые файлы: snapshot, compiled fixture, signed session, signed checks, руководство/evidence/knowledge log, Obsidian. Rollback: убрать internal factories и новые tests/docs, ordinary signatures unchanged, миграций нет. Commits/push разрешены ранее. Не публиковать private keys/личные task validation. Новый результат Unlimotion записывать лишь после валидного graph gate; цель остаётся активной.

## 10. Review и журнал
Post-SPEC independent review обязателен по central expanded high-risk rules; после implementation full post-EXEC source/evidence review. Depth: scope/AC/output, gates до load/dispatch, path and lifecycle, unsupported claims, regression, private evidence, public identity gates. Architect/security/validation/delivery применимы, UI N/A. Expected objections: «это public admission?» — нет; «Dispose освобождает DLL?» — only owned handles; «proof пропущен?» — shared fixture proof checks unchanged; «count native?» — нет. Линтер/rubric не заменяют фактическое review и проверки.
2026-10-01: current sources inspected; production edits не начаты; спецификация подготовлена для независимого review в рамках заранее подтверждённой цели.

Post-SPEC independent review PASS: no B/H/M/L findings; common gates, manifest-only entry path, separate root/no-load-first controls and diagnostic lifecycle reviewed. Read-only behavioral under danger-full-access. EXEC started under active-goal mechanism approval. Ancestors/operator namespace remain existing trusted host assumptions; Revalidate is serialized checkpoint, not atomic hostile namespace protection.

## 11. Expanded audit
|Linter block|Пункты|Verdict|Основание|
|---|---|---|---|
|Completeness|1–5|PASS|outcome dependency, inspected AS-IS, problem/design/non-goals|
|Design|6–10|PASS|shared factories, state/provider semantics, typed errors, no performance change claim|
|Safety|11–13|PASS|no public schema/migration; operator TCB/lifetime explicit; rollback source-only|
|Testability|14–16|PASS|AC1–6, actual load/path/output and late deltas, full apphost|
|Execution readiness|17–19|PASS|review→EXEC→fresh validation→audit, exact goals gates separate, expanded high-risk|
|Profile|20|PASS|pinned .NET library, nonconcurrent host lifecycle, disposable keys|
Rubric: clarity5 (named outcome/non-goals), AS-IS5 (three actual source files/experimental lock evidence), design5 (common constructor and explicit factory), safety5 (TCB/rollback/no human admission), testing5 (AC matrix and ordinary regression), readiness5 (goal authorizes mechanism, independent review done). 30/30 planning assessment, not runtime proof or completion.
Role-based review: architect/security PASS common gates/path namespace boundary; validation PASS separate-root no-load-first, actual outputs and deltas; domain/workflow PASS dependent G02 mechanism, no contract semantics change; delivery PASS local ephemeral artifacts, no private keys/public identity choice; UX N/A backend. Scope/Evidence, Contract and Adversarial post-SPEC passes completed by independent reviewer over SPEC/current source; no substantive findings. No-findings justification: manifest entry binding inspected, held snapshot gates and runtime ordering shared, lifetime promise bounded, actual AC observations planned. Manual challenge: assembly event is not native callback, provider synthetic controls not real external drift, file path is not mapped-memory digest. Depth covers scope/AC/evidence/claims/regression/comments/API/lifetime/private delivery; post-EXEC static PASS same boundaries, runtime evidence pending. No unresolved question blocks this internal mechanism; public closure and D02 choices stay outside.

Первый full apphost session15565 завершился отказом теста observer-physical-load. Ожидаемый diagnosticPath строился Path.Combine с manifest forward-slash entry; loader нормализует separators. Исправлен тест через Path.GetFullPath; добавлен observer-load.json перед assertion с actual/expected path/count/reads. Runtime gates не изменены. Первый failed log сохранён отдельно observer-session-468afe2755444abdbac9940814d3b826; fresh build0/0 и full rerun session12712 выполняется.

### Свежая runtime проверка
Полный Admission apphost session12712 завершился exit0:492 checks; solution build0warnings/0errors. Scalar/allocation signed-session каждый82checks/49rows, диагностическая часть16rows/6no-load controls; actual Location совпал с normalised manifest entry, load count1/state reads1. Actual returned witness/reference и signed identities сверены; late refusals имеют zero dispatch delta, disposed имеет zero state read. [Evidence](../docs/evidence/g02-observer-session-20261001/README.md):4source/4binary hashes,10exactcopies с failed history. Native trace ещё не запускался.


Финальный независимый source/evidence audit: PASS, новых B/H/M/L нет. Сверены4/4current source hashes,4/4current binaries и10/10exact copies с raw originals. Fresh exit0/build0/0/Admission492PASS. Обе формы82checks/49rows:16diagnosticrows,6zero-load/exclusive-reopen controls; actual Location совпал с normalized manifest path, load1/read1; positive actual values совпали с исходными compiled owner/reference/output vectors и stream result. Package/approval identities сверены с manifest, release/build проверены исходным тестом. Malformed/provider/runtime/expiry/epoch/rollback/disposed deltas соответствуют AC; recovery Returned. Failed history сохранена, Path.GetFullPath и pre-assert load observation проверены. Adapter не native callback, public admission не выполнен, CLR unmap не обещан; ancestor namespace TCB и следующий native integration явно ограничены. Audit behavioral read-only при danger-full-access, без consumer rerun. Unlimotion global validate isValid=false, новый result не записан; цель активна.
