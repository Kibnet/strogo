# Ядро языка для агента v0: результат реализации

Утверждённый v0 реализован и проверен локально. Агент может предложить типизированное изменение программы; host проверяет граф и контракт, компилирует его в IR, исполняет и атомарно применяет допустимый результат. Финальный conformance: **29/29 сценариев, 10 904 assertions, 0 failures**. Сборка Release: **0 предупреждений, 0 ошибок**. Post-EXEC review: **PASS** после исправлений.

Демонстрация резервирует 3 из 10 и получает остаток 7. Повтор события возвращает исходный receipt без повторного списания. Replay в отдельном процессе воспроизводит результат без записи состояния. Это проверка работающего механизма ограничений. Влияние языка на качество работы LLM пока не измерялось.

## Что создано

| Исходная задача | Реализация |
| --- | --- |
| Структура исходного кода | Типизированный DAG с устойчивыми ID узлов и canonical content revisions |
| Формальная нотация | Закрытый JSON-формат, I64/Bool, 11 операций, строгие правила вычисления и фиксированный Reserve-контракт |
| Лексер и парсер | Стандартный JSON reader и собственные строгие schema/domain checks; повторные поля, неизвестные конструкции и неканонические числа отвергаются |
| Компилятор в промежуточный код | Детерминированное lowering в линейный SSA IR с привязкой инструкций к source IDs |
| Исполнение с ограничениями | Отдельные graph/IR interpreters, SMT admission, ACL/capabilities, typed patches, Prepare/Commit/Replay, SQLite и provenance |

Навигация: [спецификация](specs/2026-09-04-agent-language-kernel-v0.md), [инструкция запуска](README.md), [пример программы](fixtures/reserve.json), [полученный IR](artifacts/demo/program.ir.json), [проекция перед commit](artifacts/demo/preview.txt). Реализация: [Core](src/Kernel.Core/CoreApi.cs), [Host API](src/Kernel.Host/HostApi.cs), [JSONL protocol](src/Kernel.Cli/Protocol.cs).

Человек видит вычисленную проекцию: какой остаток изменится, какой контракт применён, сколько израсходовано fuel, есть ли внешние действия и выполнена ли запись. Агент получает шесть методов: Snapshot, Explain, ProposePatch, Prepare, Commit, Replay. Политика, principal, solver options и готовое новое state через этот протокол не принимаются.

## Проверенный запуск

Рабочая папка: `<original-checkout>`.

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet run --project tests/Kernel.Conformance -c Release --no-build -- --suite all --report artifacts/conformance-results.json
dotnet run --project src/Kernel.Cli -c Release --no-build -- demo --directory artifacts/demo
dotnet run --project src/Kernel.Cli -c Release --no-build -- replay --directory artifacts/demo --event evt-001
```

Все команды завершились с кодом 0. Последний полный suite проверял итоговые бинарники; после него исходники runtime и tests не менялись. Locked restore, финальный demo на новой базе и replay выполнены дополнительно. При переносе на новую машину сначала выполните установку закреплённого Z3 из README. Повторный demo использует сохранённый receipt; для нового эксперимента нужна другая пустая папка.

Окружение: Windows x64, SDK **10.0.400**, .NET **10.0.11**, Microsoft.Data.Sqlite **10.0.11**, Z3 **5.1.0**. SDK закреплён в global.json, NuGet зависимости в четырёх lockfiles. Архив и executable Z3 проверены по SHA-256; runtime identity содержит также SHA-256 фактических Core/Host DLL. [Окружение и pins](artifacts/environment.json), [бинарники и результаты завершающих команд](artifacts/completion-evidence.json), [опись исходников](artifacts/source-manifest.json).

Итоговая база использует schema2. Промежуточные DB schema1 из первого smoke не мигрируются и явно отвергаются. Для пользователя подготовлена новая база в `artifacts/demo`.

## Evidence по критериям приёмки

Первичный источник результатов — [conformance-results.json](artifacts/conformance-results.json): имя каждого case, AC, assertions, время, фактическое evidence и failure. Core: 8 cases; Host: 18; CLI: 3.

| AC | Что подтверждено | Cases в финальном отчёте |
| --- | --- | --- |
| AC1 | Строгий parse, независимость hash от transport-порядка, отсутствие неявных I64 conversions | canonical-bytes-and-identity; strict-schema-and-graph-rejections; jsonl_closed_boundary_and_commit |
| AC2 | Checked overflow, границы I64, строгий select; graph/IR совпадают с независимым oracle | checked-arithmetic-and-strict-select; seeded-200-dags-independent-oracle |
| AC3 | Реальный Z3 допускает корректный Reserve, возвращает воспроизводимые witnesses; unknown/timeout/missing solver не допускают программу | real-z3-reserve-and-counterexample; solver-fail-closed-statuses; runtime-checks-detect-false-verification |
| AC4 | Нельзя подменить policy, capabilities, principal, IR, verified, output или чужой token; отказ не пишет state | strict-schema-and-graph-rejections; forged-input-capability-and-token-owner; revoked-authorization-on-every-read; typed-patch-rejection-and-tombstones; JSONL case |
| AC5 | Typed patch атомарен, ID сохраняются, tombstones не переиспользуются, retry устойчив; drift не активирует stale программу | typed-patch-atomic-identity-retries; typed-patch-rejection-and-tombstones; patch-admission-trust-snapshot-races |
| AC6 | Корректные success/refusal/invalid-input и граничные business outputs | reserve-independent-business-grid; business-decisions-retry-restart; JSONL case |
| AC7 | State и receipt атомарны, retries/conflicting payload различаются; write error/process stop и concurrent CAS проверены | business-decisions-retry-restart; sqlite-write-failures-roll-back; process-stop-before-after-commit; concurrent-cas-and-same-event; provenance-original-actor-survives-retry |
| AC8 | Fuel, размер/depth/node limits, timeout и epoch не допускают позднюю запись | strict-schema-and-graph-rejections; checked-arithmetic-and-strict-select; worker-timeout-eviction-and-epoch |
| AC9 | Исторический replay использует persisted artifacts/fuel и проверяет trace/цепочку; corruption/missing artifacts дают отказ | seeded-200-dags-independent-oracle; ir-validation-rejects-invalid-input; replay-persisted-history-and-fuel; replay-missing-and-tampered-artifacts; replay-predecessor-and-state-chain |
| AC10 | Структурированные ошибки, правдивые diff/status, provenance и проекция; завершённый event не остаётся ложным Prepared | concurrent-cas-and-same-event; provenance-original-actor-survives-retry; три CLI cases |
| AC11 | Согласованный snapshot, повторная проверка trust context и прав, отзыв доступа, сериализация ACL/commit | commit-rechecks-program-policy-manifest; snapshot-consistency-during-competing-commit; acl-update-serialized-with-commit; revoked-authorization-on-every-read; worker-timeout-eviction-and-epoch; patch-admission-trust-snapshot-races |
| AC12 | Закреплённый локальный toolchain, restart/demo/replay и ненулевой exit при провале теста | canonical-bytes-and-identity; real-z3-reserve-and-counterexample; process-stop-before-after-commit; replay-persisted-history-and-fuel; CLI cases; negative runner below |

Независимый вычислительный набор содержит **200 сгенерированных DAG × 6 входов** с фиксированным seed 20260904 и всеми 11 операциями. Отдельный бизнес-набор содержит 42 вектора. Graph reference использует BigInteger, IR interpreter — checked long, тестовый oracle задаёт ожидаемое поведение отдельно. Эти проверки дополняют друг друга, но не заменяют доказательства корректности компилятора.

В SMT границы промежуточного I64-результата являются обязанностью доказательства, а не предпосылкой, исключающей overflow. Регрессия `x+1 > x` при `x=Max` обнаруживается. Дополнительный граф с правильными Reserve outputs и overflow в невыбранном операнде проверяет основной encoder и строгую семантику select, а не только вручную написанную формулу.

Проверка самого test runner:

```powershell
dotnet run --project tests/Kernel.Conformance -c Release --no-build -- --suite core --z3 tools/does-not-exist.exe --report artifacts/conformance-negative.json
```

Получен **ожидаемый exit1**, `passed=false`, 7/8 cases: case с реальным solver отказал из-за явно отсутствующего файла. Это намеренная отрицательная проверка, отдельно от зелёного финального suite. [Её результат](artifacts/conformance-negative.json).

## Пользовательский результат

| Сценарий из SPEC | Фактическая проверка |
| --- | --- |
| S1 Резерв и повтор | [Финальный demo](artifacts/demo/demo.json): 10→7, один receipt, replay Replayed |
| S2 Ошибка add вместо sub | real-z3-reserve-and-counterexample и typed-patch-rejection-and-tombstones: witness, active revision не меняется |
| S3 Запрещённое изменение | Schema/ACL/patch/JSONL cases: конкретный отказ, отсутствие записи |
| S4 Два события на одной revision | concurrent-cas-and-same-event: один commit, конфликт второго |
| S5 Недостаточный budget | Fuel6 для корректной программы, отказ при fuel5; watchdog/epoch cases |
| S6 Replay после restart | CLI отдельный процесс и persisted-history cases: output тот же, state не записывается |
| S7 Explain до commit | preview.txt и CLI assertions: вычисленный delta, cap scope, effects0, статус «подготовлено» |

Возражения из SPEC закрыты в заявленном объёме: программа имеет собственное представление, типы, семантику и IR; контракт принадлежит host; один opcode задаёт одну операцию, но эквивалентные алгоритмы могут иметь разные graphs. Доказательство улучшения работы LLM остаётся следующей исследовательской задачей.

## Post-EXEC review

**PASS. Открытых findings нет.** Проверены утверждённая SPEC, Core/Host/CLI, conformance с oracle, actual receipts, README, dependency pins и scope файлов. Выполнены Scope/Evidence, Contract, Adversarial, Role-Based, Fix/re-review passes. Источники и тесты перечитал также reviewer kernel_spec_review; сборку и запуски выполнял root.

| Severity | Area | Исправленное замечание | Результат |
| --- | --- | --- | --- |
| MEDIUM | SMT coverage | Не хватало проверки actual encoder на overflow в невыбранной ветви | Новый real-Z3 regression case проходит |
| MEDIUM | Prepare lifecycle | Второй token завершённого event мог показывать Prepared и занимать cache | Matching tokens удаляются; durable outcome проверяется |
| MEDIUM | Projection | NoChange мог показывать requested operations как фактический diff | Diff вычисляется по before/after, неизменённые IDs не выдаются за изменённые |
| MEDIUM | Runtime diagnostics | Ошибка admitted-программы не становилась VerifierMismatch | Сохраняются underlyingCode/witness, commit запрещён |
| MEDIUM | Provenance | Не сохранялись автор и время изменения | Atomic schema2 record; restart/retry сохраняют исходного автора |
| — | Финальный re-review | Нет находок | PASS |

Новые regression tests запускались против **старого Host binary**: [12/18 cases, 6 ожидаемых провалов](artifacts/review-regression-red.json). [Baseline digest](artifacts/review-regression-baseline.json) сохранён. После пересборки итоговый suite прошёл 29/29. Это проверяет способность новых тестов обнаружить исправленные дефекты. В ходе интеграции отдельно исправлены harness issues: ожидаемый код отказа при ACL drift, отключение FK только в fixture намеренной порчи artifact и JSON naming options в assertion provenance. Production проверки ради зелёного отчёта не ослаблялись.

Role-based pass: бизнес — Reserve/refusal/retry; UX — truthful status/diff/preview; validation — oracle, boundaries, fault points и реальный solver; архитектура — source→IR→SMT и TCB binding; operations/security — права, конфигурация и SQLite commit. User-observable completion gate S1–S7/AC1–AC12 закрыт.

Effective sandbox reviewer: `danger-full-access`, filesystem `unrestricted`, approval `never`. Технической read-only изоляции не было; фактические действия reviewer были только чтением. Это **adversarial fallback в writable-среде**, не технически изолированный аудит. Reviewer сверил SHA текущих Core/Host DLL с RuntimeIdentity фактического receipt.

Depth/stop decision: исходники лежат в новой отдельной папке; в vault появился только текущий spec, unrelated changes не обнаружены. Git repository/remote в prototype не создавались. Contract, docs и пользовательские сценарии согласованы; скрытого расширения языка, эффектов и API нет. No-findings justification относится к повторному review после перечисленных fixes и полного зелёного запуска. Обязательных незавершённых исправлений нет.

## Границы результата и следующий эксперимент

Verified означает проверку конкретного графа относительно фиксированного Reserve-контракта при условии корректности trusted computing base: encoder, Z3, interpreters, host и зависимостей. Вся реализация не верифицирована механически. Согласованная ошибка TCB остаётся наиболее сильным возражением ручного review; разные arithmetic backends и независимые vectors лишь снижают этот риск.

Прототип допускает один локальный ресурс, I64/Bool и ациклическое вычисление. Fuel ограничивает операции графа; watchdog не является доказательством лимита RAM/CPU ОС. Transaction tests инъектируют IOException через hooks в точках записи; настоящий отказ SQLite engine не воспроизводился. Process-kill и transaction tests не сертифицируют все аппаратные сбои. Replay проверяет сохранённые артефакты и отказывает при несовместимом runtime. Произвольный доступ агента к файлам host находится вне модели угроз. Внешние эффекты, production deployment и вызовы моделей в этом этапе отсутствовали.

Следующий содержательный шаг — сравнить реализацию одинаковых задач агентом через этот граф и через C# при одинаковом защищённом host API, проверках и бюджете. Измерять стоит завершение задачи, недопустимые попытки/допуски, циклы исправления и стоимость. Такой эксперимент позволит отделить вклад языка от вклада host. Расширять backend или набор операций разумно после появления конкретного ограничения на этих задачах. Сам benchmark и OS sandbox в этот EXEC не входили.

## Дополнение E04, 2026-09-06

Исторический отчёт v0 выше сохранён. Для нового ограниченного TaskGraph profile см. [отчёт E04](artifacts/e04/REPORT.md): 141 checks, Dafny/C#/ReadyToRun win-x64, итоговые границы и review. Проект теперь находится в Git; checkpoints фиксируются локальными commits. G05/G06 не измерены.
