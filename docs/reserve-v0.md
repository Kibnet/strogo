# Ядро языка для агента v0

Общий замысел и согласованные цели: [Цель проекта](project-intent.md). Этот документ описывает существующий прототип Reserve v0.

Локальный эксперимент: агент меняет типизированный граф, а доверенный host допускает программу и применяет её результат по защищённому контракту. Сценарий v0 — резервирование количества одного ресурса. Это исполняемое исследование механизма ограничений; сравнительный эксперимент качества LLM ещё не проводился.

Исходный граф проходит строгую схему, проверку типов/DAG и SMT, компилируется в линейный IR и исполняется отдельно от эталонного интерпретатора. Host дополнительно проверяет фактический результат перед атомарной записью в SQLite. Код агента не исполняется как C#, SQL или shell.

## Запуск

Поддержанная сборка этого прототипа: Windows x64, .NET SDK **10.0.400**, Microsoft.Data.Sqlite **10.0.11**, Z3 **5.1.0**. SDK закреплён в global.json, NuGet зависимости — в packages.lock.json каждого проекта. Z3 находится внутри tools, его архив и бинарник закреплены SHA-256 в tools/z3.json. Никаких платных API для проверки не требуется.

```powershell
pwsh -File tools/Install-Z3.ps1
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet run --project tests/Kernel.Conformance -c Release --no-build -- --suite all --report artifacts/local-validation/reserve-v0-conformance.json
dotnet run --project src/Kernel.Cli -c Release --no-build -- demo --directory artifacts/demo-run
dotnet run --project src/Kernel.Cli -c Release --no-build -- replay --directory artifacts/demo-run --event evt-001
```

Для повторного запуска demo используется прежний receipt и replay. Данные не сбрасываются. Для новой демонстрации укажите другую пустую папку. `initialize` и `demo` — команды владельца локального harness, не методы интерфейса агента.

В Git папка `artifacts/demo` хранит исторический снимок проверки без локальной SQLite-базы. Команды выше используют отдельную игнорируемую папку `artifacts/demo-run`, чтобы первый запуск после checkout создавал полный набор состояния и receipt.

После demo в указанной папке доступны `program.json`, `program.ir.json`, `preview.txt`, `demo.json` и `kernel.db`. Demo резервирует 3 из 10, получает 7, повторяет тот же event без нового списания и воспроизводит результат. `demo.json` включает версии, preview, receipts, повтор и replay. Историческая revision receipt не является текущим head.

## Интерфейс агента

Создайте отдельную сессию командой владельца:

```powershell
dotnet run --project src/Kernel.Cli -c Release --no-build -- initialize --directory artifacts/session
dotnet run --project src/Kernel.Cli -c Release --no-build -- serve --directory artifacts/session
```

`serve` читает по одному JSON-запросу на строку и выдаёт один JSON-ответ. Процесс держит подготовленные планы в памяти. Пример первой строки:

```json
{"schemaVersion":"kernel.v0","method":"Snapshot","arguments":{"resourceId":"item-001"}}
```

Методы строго ограничены:

| Method | arguments |
| --- | --- |
| Snapshot | `resourceId` |
| Explain | `artifactId`: program revision, prepareId или receiptId |
| ProposePatch | `patch`: типизированный объект patch |
| Prepare | `event`, `expectedStateRevision`, `expectedProgramRevision`, `expectedPolicyRevision` |
| Commit | `prepareId` |
| Replay | `receiptId` |

`Explain(programRevision)` возвращает материал для редактирования: canonical source, ревизии узлов, outputs digest и scope. Пример Prepare с ревизиями из Snapshot:

```json
{"schemaVersion":"kernel.v0","method":"Prepare","arguments":{"event":{"eventId":"evt-001","resourceId":"item-001","kind":"reserve","quantity":"3"},"expectedStateRevision":"<snapshot.stateRevision>","expectedProgramRevision":"<snapshot.programRevision>","expectedPolicyRevision":"<snapshot.policyRevision>"}}
```

Подставьте реальные SHA-256 строки. Ответ Prepared содержит opaque prepareId. Передайте его в Commit **тому же процессу**. После перезапуска повторите Prepare с исходным event: существующий committed receipt вернётся до stale-check, а незаписанное событие потребует актуальный snapshot. Новый payload под занятым eventId отклоняется. Подготовка сама ничего не резервирует.

После commit host удаляет все незавершённые планы того же события. Для последующего Explain используйте receiptId: prepareId имеет ограниченный срок жизни, а receipt сохраняется. При потере ответа повтор Prepare исходного события восстанавливает результат.

Формат patch использует теги AddNode, ReplaceNode, RemoveNode и SetOutputs. Например, замена существующего узла (ревизии берутся из Snapshot и Explain):

```json
{"schemaVersion":"kernel.v0","patchId":"patch-001","programId":"reserve","baseProgramRevision":"<programRevision>","expectedPolicyRevision":"<policyRevision>","operations":[{"op":"ReplaceNode","nodeId":"n.remaining","expectedNodeRevision":"<nodeRevision>","node":{"id":"n.remaining","op":"i64.add_checked","type":"I64","args":["n.available","n.debit"]}}]}
```

Поместите объект в arguments.patch метода ProposePatch. Этот намеренно ошибочный пример **должен быть отклонён с контрпримером**, поскольку сложение нарушает Reserve. Невалидная ревизия не активируется. NoChange также получает устойчивый receipt; Explain показывает фактический diff, а не список запрошенных операций.

Неизвестные/лишние/повторные поля запрещены. Все I64 передаются десятичными строками, без `+`, `-0`, экспоненты и преобразования через double. `principal`, policy, solver flags, `verified`, произвольные IR и новое state отсутствуют в агентском протоколе. Principal `agent` выдаётся harness из доверенного транспорта; удалённая аутентификация не реализуется.

## Семантика языка

Два типа: I64 и Bool. Один DAG, три выхода: accepted, available, reserved. Узлы имеют стабильные ID, версия программы определяется canonical hash. Порядок ключей и узлов в transport не влияет на hash; различные алгоритмы не объявляются семантически канонически эквивалентными.

Операции: input, i64.const, bool.const, i64.add_checked, i64.sub_checked, i64.le, i64.eq, bool.not, bool.and, bool.or, select. Все достижимые узлы вычисляются ровно один раз. Select/and/or строгие: ошибка в невыбранном операнде тоже останавливает вычисление. Неиспользуемые узлы, циклы и неявные преобразования запрещены.

Reference interpreter считает через BigInteger с проверкой диапазона I64. IR interpreter использует checked long. SMT ищет вход, на котором хотя бы один узел не определён или нарушен фиксированный контракт. Диапазон промежуточного результата является проверяемой обязанностью, а не предположением, которое исключает переполнение из рассмотрения. Unknown, timeout, ошибочный ответ solver и отсутствие обязательного результата запрещают admission.

Контракт: available ≥0, quantity >0. При достаточном остатке reserved=quantity и available'=available−quantity, иначе reserved=0 и stock неизменен. Недостаточный остаток — committed business decision с receipt и новой revision. Невалидный вход/ошибка исполнения не занимает eventId. Runtime-контракт нельзя отключить.

## Границы гарантий

- Эффекты программы пусты. Только host пишет один локальный ресурс с разрешением StateWrite. Внешних отправок/сети/платежей/отката внешних действий нет.
- SQLite атомарно хранит state и receipt. После неизвестного исхода commit следует повторить исходный event, а не создавать новый eventId.
- Replay проверяет сохранённые artifacts, исторические policy/fuel, predecessor, trace и revision, не выполняя новую запись.
- Автор и время принятого patch/event сохраняются атомарно в отдельном provenance record. Повтор запроса не переписывает автора исходного изменения; actor/time не входят в семантические hashes.
- Admission связан с версиями и digest фактических DLL ядра/host и бинарника solver. После изменения runtime старые допуски требуют повторной проверки; replay неподдерживаемого исторического runtime явно отказывает.
- Fuel ограничивает число узлов. Watchdog — операционный timeout; он не доказывает лимит RAM/CPU на уровне ОС.
- `verified` условен корректностью encoder, solver и runtime. Conformance не является механизированным доказательством всей TCB.
- Агент с произвольным доступом к файловой системе host находится вне модели угроз. Реальная OS-изоляция живого coding agent — отдельная работа.

## Структура

`src/Kernel.Core` — source codec, DAG/types, reference, IR, SMT. `src/Kernel.Host` — policies, admission, patches, SQLite, prepare/commit/replay. `src/Kernel.Cli` — пользовательское демо и закрытый JSONL-протокол. `tests/Kernel.Conformance` — исполняемые проверки; хотя бы один провал даёт ненулевой exit. `fixtures` — корректный Reserve и ошибочная версия с add вместо sub. `artifacts` — фактические результаты запусков, `REPORT.md` — их итоговая интерпретация после проверки.

Утверждённая спецификация: [kernel v0](../specs/2026-09-04-agent-language-kernel-v0.md). Отдельные поведенческие ограничения, такие как strict select и persistent rejected receipt, намеренны и покрываются conformance.

## Профиль TaskGraph (E04)

Реализован отдельный bounded profile со строгим AST, формальной проверкой Dafny и реальным исполнением ReadyToRun win-x64. [Запуск и карта инвариантов](task-graph-v1.md), [результат и границы](../artifacts/e04/REPORT.md). Обязательные знания ведутся в базе знаний и связаны artifacts/e04/knowledge-sync.json.
