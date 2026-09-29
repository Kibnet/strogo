# D02: проверяемое применение утверждённого изменения заметки

## 0. Метаданные

- Статус: рабочая SPEC, **не утверждена**; EXEC запрещён до точной фразы владельца «Спеку подтверждаю» для этой ревизии.
- Тип: `delivery-task`, QUEST, `product-system-design`; масштаб large (новый домен, контракт, ограниченный эффект).
- Владелец смысла: Kibnet; автор проекта SPEC: Codex. Ветка: `main` репозитория Strogo. Unlimotion: задача `b1f8d27c-854b-4b99-8ed0-35027e96a7ed`, родитель `3295d388-0d24-45f8-b8be-b0eaf24e2cc5`.
- Instruction stack: central `AGENTS.md`, `creator-vibe-lens`, `model-behavior-baseline`, `tool-execution-baseline`, `collaboration-baseline`, `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `product-system-design`, `spec-linter`, `spec-rubric`, `review-loops`, локальный `AGENTS.md`. Canonical template: центральный `templates/specs/_template.md`. Full `creator-vibe` неприменим: фиксируется технический контракт, не авторский текст/UX.
- Целевое семейство: GPT-6 Astra как каталоговый baseline; фактический model ID/reasoning этого SPEC-хода клиентом не засвидетельствован. Для будущего G05 потребуется отдельная фиксация runtime.
- Baseline: E04 `TaskGraph` — закрытый чистый pipeline; E05 `Strogo.Modules v0.2` — `I64`, `Bool`, records, bounded sequences, if/fold, owner contracts и .NET backend; E10 live pilot `EvidenceQuarantined/InvalidPolicy`, сравнение не состоялось; G03 — каталог и synthetic-only scorer. Ссылки: [замысел](../docs/project-intent.md), [модули](../docs/modules-v0.2.md), [E04](../docs/task-graph-v1.md), [E10](../docs/evidence/e10-live-paired-pilot.json), [G03](../docs/g03-error-catalog-v0.1.md).
- Runtime: закреплённый repo .NET SDK 10.0.400 и существующий Dafny toolchain. Новый исполнимый артефакт проверяется минимум на одной уже поддерживаемой .NET платформе; другие платформы не обещаются этой SPEC.

## 1. Overview / Цель

Проверить, может ли агент написать на существующей общей нотации Strogo исполнимый модуль для реального рабочего сценария: после явного утверждения человеком конкретного изменения заметки система применяет **именно это** изменение ровно один раз. Модуль решает, допустим ли переход; доверенный host отвечает за право, привязку утверждения, конкурентный commit и запись. Пользователь получает проекцию: что было предложено, какой digest утверждён, какой эффект произойдёт/произошёл и почему отказано.

Успех означает отдельный от Reserve домен с версионированной человеческой спецификацией, owner contract, agent-writable модулем, proof/admission, компиляцией, исполнением на изолированном хранилище и проверенными отказами. Домен готов для будущей регистрации в G03 и сравнения в G05, но эти эксперименты не входят в данный этап. Остановка: любой недоказанный контракт, неполный binding approval, непрозрачный эффект, расхождение backend/host или недоступный безопасный rollback запрещают заявление об успешном D02.

## 2. AS-IS

`Strogo.Modules` поддерживает чистые проверяемые функции и owner bundle; `Kernel.Host` имеет capabilities и prepare/commit, но связан с Reserve (`programId=reserve`, `ReserveEvent`, quantity/state). E04 преобразует граф задач чистым предзаданным pipeline, без host-применения patch. Для домена утверждения заметки нет отдельной модели, контракта, исполнимого Strogo-модуля и допуска эффекта. Существующие receipts не должны автоматически становиться утверждениями нового смысла.

E10 показал не выигрыш/проигрыш языка, а уязвимость процедуры сравнения: первый exposed Strogo run вернул `InvalidPolicy`, остальные arms не запускались; ошибочный учёт transport error как tool call исправлен лишь в будущем parser. Поэтому D02 не использует данные E10 как меру времени, а сохраняет явные invocation/attempt identities и отказ без подмены результата.

## 3. Корневая проблема

Текущие эксперименты не показывают, способен ли общий агентский язык представить рабочий процесс, в котором правильность зависит одновременно от последовательности состояний, решения человека, точного scope capability и ограниченного побочного эффекта. Копия Reserve с другими именами не ответит на этот вопрос.

## 4. Цели дизайна

1. Отделить agent-written вычисление решения от доверенной авторизации и атомарного применения.
2. Сохранять ровно один способ сериализовать состояние, событие, решение и эффект, со стабильными ID и digest.
3. Проверять универсальные переходы owner contract, а интеграционными сценариями проверять trusted boundary.
4. Повторно использовать E05 notation/backend; новые core-opcodes добавлять только при доказанной необходимости. В первой версии finite state и command кодируются owner-ограниченными `I64`, а не создаётся общий `enum`/`effect` syntax. Это компромисс, который должен быть видим в гарантиях G03.
5. Оставить существующие профили и receipts совместимыми: D02 имеет отдельные schema/profile/version и артефакты.

## 5. Non-Goals

Не менять реальную базу Obsidian, Unlimotion или Git; не отправлять наружу; не реализовывать текстовый diff editor, синхронизацию облака, конфликтное слияние, произвольный filesystem API, новую платформу, генерацию approval агентом, произвольные политики доступа или package ecosystem. Не объявлять G03/G05/G06 достигнутыми. Не превращать integration corpus в доказательство корректности .NET/Dafny/ОС или человеческой спецификации. Лимиты не доказывают верхнюю границу RAM/latency без измерения.

## 6. TO-BE

### 6.1 Ответственности

| Компонент | Ответственность |
| --- | --- |
| Owner domain contract D02 | Нормативная таблица переходов, инварианты, допустимые решения и эффект. Человек утверждает эту семантику в SPEC, а позднее отдельно утверждает exact owner bundle identity до допуска candidate. |
| Agent module `Decide` на `Strogo.Modules v0.2` | Чистая функция над валидным состоянием, командой и тремя host-computed Bool facts, выдающая typed `Decision`. Не читает approval store и не пишет заметку. |
| D02 adapter/host | Строгий transport, bind approved digest, scope capability, compare-and-swap по revision, одноразовый apply, audit/receipt и replay. Любой недостающий guard — отказ. |
| Existing Dafny/.NET pipeline | Проверка модуля и компиляция без предметного shortcut в verifier. |
| D02 conformance + отдельный oracle | Независимая таблица ожидаемых переходов и fault injection для trust boundary. |

### 6.2 Контракт данных и потока

В изолированном хранилище живёт `ChangeRecord`: стабильные `changeId`, `noteId`, `baseNoteDigest`, `replacementDigest`, `approvalDigest` (пусто до утверждения), `stateRevision`, `state` и append-only receipt references. Содержимое исходной заметки и предложенная **полная замена** — отдельные immutable content-addressed UTF-8 blobs; diff/patch-интерпретатора нет. `noteId` выбирается из заранее созданного fixture namespace; path не передаётся agent-модулю. Approval является отдельным доверенно зарегистрированным **host assertion** с человеком-владельцем, exact `changeId`, `noteId`, `baseNoteDigest`, `replacementDigest` и одноразовым approval ID. В прототипе допустим локальный owner-injected approval fixture; это не криптографическая подпись и нельзя выдавать его за живое подтверждение владельца.

Закрытые состояния: `Proposed=0`, `Approved=1`, `Applied=2`, `Rejected=3`; команды `Approve=0`, `Reject=1`, `Apply=2`. Эти числа — транспортные коды, а не свободный `I64`: adapter отвергает любой другой код до вызова модуля. `Decide(state:I64, command:I64, approvalValid:Bool, baseMatches:Bool, capabilityAllows:Bool)` получает три **host-computed** факта, не слова агента. `Decision` содержит `nextState`, `effectKind` (`None=0`, `RecordTransition=1`, `ReplaceNote=2`) и `errorCode` (`None=0`, `InvalidTransition=1`, `ApprovalMissing=2`, `StaleBase=3`, `CapabilityDenied=4`, `AlreadyApplied=5`), также с закрытым range. `RecordTransition` означает запись только state/receipt, `ReplaceNote` — атомарную запись note/state/receipt; `None` означает отсутствие мутации. Отказ возвращает исходный state, `None` и ненулевой errorCode; успех — указанный nextState/effect и `errorCode=0`. Public result строго различает `Accepted`, `Rejected`, `Committed`, `AlreadyCommitted`, `OutcomeUnknown`; errorCode не подменяет host failure.

Общий путь: validate envelope → authorize read и выбранную команду → проверить существующий `eventId`/digest → load current snapshot and expected revisions → вычислить approval/base/capability facts из trusted store → `Decide` в допущенном compiled artifact → validate typed decision against owner model → при `None` вернуть отказ без мутации → при `RecordTransition` атомарно записать state+receipt → при `ReplaceNote` проверить scoped capability и exact binding повторно под lock → атомарно заменить note bytes по `replacementDigest` и записать state+receipt либо отказать без частичного изменения. Каждое успешное действие имеет idempotency key `eventId` и canonical receipt; тот же ID+digest возвращает тот же receipt, тот же ID с иным содержимым — `EventIdConflict`. На чужой resource без `ReadChange` host отказывает до раскрытия его состояния; отозванное право на команду не разрешает даже replay её receipt.

Два режима исполнения: `check` выдаёт preview без записи; `apply` по отдельной команде host совершает эффект. `check` не выдаёт capability на запись. CLI/SDK принимает только заранее сохранённые fixture paths внутри отдельного D02 sandbox root; произвольный путь пользователя не интерпретируется как note target. E05 source не расширяется полями effect/capability; binding фиксируется в отдельном owner-controlled D02 manifest с digest module/contract, разрешённым note namespace, действиями и trusted approval store. Неподтверждённый manifest не допускается.

Отчёт/projection включает module/owner/manifest/artifact digests, исходное и конечное состояние, effectKind, capability scope, approval binding, отказ/receipt, TCB и statement уровня гарантии. Никогда не сообщает `verified` без assumptions. Raw секреты, тело заметки и приватные пути не копируются в публичный report.

### 6.3 Наблюдаемые сценарии

| Scenario | Trigger | Видимый результат | Evidence | AC |
| --- | --- | --- | --- | --- |
| Утверждённая правка | Человек утвердил exact replacement digest, затем host `apply` | Один новый note digest, `Applied`, receipt с approval/replacement binding | Изолированный snapshot до/после, replay receipt | AC1–4 |
| Нет утверждения | Агент вызывает `apply` для `Proposed` | Типизированный отказ, note bytes/revision неизменны | Negative fixture и snapshot | AC2, AC4 |
| Подмена замены/ресурса | Тот же approval с другим blob/noteId | Отказ до эффекта | Tamper fixture | AC3, AC4 |
| Повтор или гонка | Повтор event либо два конкурентных apply | Один commit; другой exact replay/конфликт, без второго эффекта | Fault/concurrency fixture | AC4, AC5 |
| Preview | Агент вызывает `check` | Решение/эффект показаны, но note не изменён | CLI transcript и snapshots | AC1, AC4 |

### 6.4 State / interaction matrix

| Current | Command | Required trusted fact | Next / effect | Refusal |
| --- | --- | --- | --- | --- |
| Proposed | Approve | exact owner approval | Approved / RecordTransition | ApprovalMissing, StaleBase |
| Proposed | Reject | authorized owner command | Rejected / RecordTransition | CapabilityDenied |
| Approved | Apply | approval+base+replacement+capability valid | Applied / ReplaceNote | StaleBase, CapabilityDenied |
| Applied | Apply с новым event ID | — | unchanged / None | AlreadyApplied |
| Applied | Approve или Reject | — | unchanged / None | InvalidTransition |
| Rejected | Любая | — | unchanged / None | InvalidTransition |
| Approved | Approve или Reject | — | unchanged / None | InvalidTransition |
| Proposed | Apply | — | unchanged / None | InvalidTransition |

Тот же event ID обрабатывается **host до `Decide`**: matching digest возвращает прежний receipt, mismatch — `EventIdConflict`; эти два host outcomes не входят в owner proof чистой функции. Для новых events порядок `Decide` таков: `capabilityAllows=false` → `CapabilityDenied`; затем матрица state/command (недопустимый переход → указанный отказ); для допустимых `Approve`/`Apply` `approvalValid=false` → `ApprovalMissing`, затем `baseMatches=false` → `StaleBase`, иначе успех. Для `Reject` после capability/transition сразу успех. При отказе все остальные факты игнорируются. `Approve`/`Reject` сами меняют лишь D02 record, не note content. Если host выдаёт `OutcomeUnknown` после начала commit, caller делает read-back exact event/receipt; автоматического повторного apply нет. Preexisting note drift даёт `StaleBase`, никогда silent merge.

### 6.5 Decision ledger

| Decision | Owner | Chosen | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Предметная область | agent proposes, user confirms by SPEC | Утверждённая правка заметки, изолированный fixture store | 0.8 | Может оказаться недостаточно репрезентативной для live работы | Нет: явный выбор включён в это утверждение |
| Exact owner bundle | user | После реализации модели человек сверяет human projection, нормативную таблицу и digest bundle; exact approval обязателен до генерации/допуска agent candidate | 0.98 | Self-authored contract может принять ошибочную реализацию | Нет для старта EXEC; отдельный gate внутри EXEC до candidate generation |
| Формат состояний | agent | Closed I64 transport + owner model; без нового enum opcode | 0.75 | G03 type-exclusion для invalid state недоступен; только adapter refusal | Нет |
| Место эффекта | agent | Trusted D02 host по exact approved replacement, модуль чистый | 0.9 | TCB больше, чем язык | Нет |
| Настоящие заметки | user boundary | Не трогать; только sandbox | 0.99 | Нет live usefulness evidence | Нет |
| Дальнейшее G03/G05 | user, отдельный gate | Registry/evaluation plan позднее | 0.95 | Нельзя заявлять majority/time win в этом этапе | Нет |

### 6.6 Runtime / config / data matrix

| Area | Source of truth | Expected change | Compatibility | Verification |
| --- | --- | --- | --- | --- |
| Module syntax | `docs/modules-v0.2.md`, parser | Без изменения core schema v0.2 | Старые modules byte-semantic stable | Existing conformance |
| Owner contract | §6.4 и §7 этой SPEC, затем отдельно approved D02 bundle | Exact transition table/proof | Отдельный digest; E05 unchanged | Owner review exact digest + Dafny admission + mutation refusals |
| State/effect store | новый sandbox D02 profile | Closed schema, CAS, atomic receipt | Нет миграции существующего хранилища | restart/replay/fault tests |
| Capabilities | D02 manifest+host | `ReadChange`, `ApproveChange`, `RejectChange`, `ReplaceApprovedNote` scoped by noteId/changeId | Никакого ambient FS access | denial tests |
| Build/runtime | pinned repo toolchain | compiled .NET artifact D02 | Reserve/E04/E05 unchanged | build, ordinary CLI run |

## 7. Нормативные инварианты

1. `note` меняется **iff** разрешён `Applied` и effect `ReplaceNote`; новое содержимое byte-equal immutable blob `replacementDigest`, без свободного пути/diff от модуля. `replacementDigest == baseNoteDigest` запрещён уже при создании предложения как `NoOpReplacement`.
2. `Applied` возможен только из `Approved` и только для exact approval binding; approval не может быть синтезирован агентским input.
3. `Rejected` и `Applied` терминальны, кроме чтения/replay; ни один event не возвращает `Proposed` или `Approved` из терминального состояния.
4. На один `changeId` может существовать не более одного успешного apply receipt; любой ответ о commit связан с фактически читаемым note digest и state revision. `RecordTransition` никогда не изменяет note bytes.
5. Отказ до commit оставляет bytes заметки, state revision и receipt set неизменными. Неоднозначность после commit не выдаётся за отказ без эффекта.
6. Отсутствующее/лишнее поле, неизвестный код, неполный approval, stale revision, несоответствие digest, недостаточная capability и verification failure приводят к типизированному отказу до эффекта.
7. Входы и выходы `Decide` имеют bounded size, замкнутые поля и owner preconditions; проверка доказывает решение для всех 4×3×2³ допустимых наборов кодов и host-фактов, а не только fixtures. `eventId`, фактическая истинность host-фактов и commit не входят в эту чистую теорему; host/effect gates проверяются отдельно и входят в TCB.

## 8. Интеграция и триггеры

Изолированный CLI или публичный D02 adapter предоставляет `check` и `apply` поверх скомпилированного D02 модуля. Нет внедрения в продукционный Obsidian/Unlimotion. Точное имя команд и файлы определяются EXEC внутри этого output contract; documented ordinary path обязан запускаться без test-only API. `Approve` в fixture создает доверенный owner assertion отдельным setup path, который не доступен агентскому клиенту.

## 9. Данные и состояние

Новые данные — только versioned D02 sandbox: notes/blobs/change records/approval assertions/receipts. Все IDs и digests содержательно связываются canonical serialization; note/replacement digest — SHA-256 сырых UTF-8 bytes. Content-addressed blob immutable; `stateRevision` — целочисленный счётчик, увеличиваемый на 1 при каждом подтверждённом переходе, и его ожидаемое значение входит в event envelope. Note bytes, state и receipt живут в одной SQLite transaction; file+DB dual-write не применяется. На v0.1 лимиты: один `noteId` и один `changeId` на fixture run, note/replacement blob ≤64 KiB каждый, event envelope ≤16 KiB, не более 16 сохранённых receipts. Превышение лимита — отказ до мутации; меньшие лимиты допустимы, лишь если все §6.3 scenarios остаются исполнимыми.

## 10. Миграция / rollout / rollback

D02 — opt-in профиль с отдельным sandbox path, не читает/пишет Reserve/E04/E05 базы и receipts. Rollback до публикации — удалить только disposable D02 fixture store после сохранения evidence; rollback конкретного apply в протоколе невозможен как скрытое удаление: исправление создаёт новое предложение/approval и отдельный trace. Никакого production rollout этим этапом нет.

## 11. Acceptance и проверка

- **AC1**: агентский модуль использует общий E05 AST без hardcoded единственной реализации, owner bundle и .NET compilation/admission; альтернативные корректные тела допустимы, семантические мутации отвергаются.
- **AC2**: после отдельного human approval exact owner bundle его model и agent module доказывают решение для всех 96 допустимых наборов `state×command×facts`; проверка host gates, истинности фактов и commit — отдельные AC3–5. Машинный report перечисляет assumptions, TCB и статус, не называет integration tests universal proof.
- **AC3**: capability и approval привязаны к exact change/note/base/replacement; misbinding, drift, missing approval, no-op replacement и unknown code дают отказ до эффекта.
- **AC4**: ordinary `check`/`apply` в sandbox выполняют сценарии §6.3, bytes/revision/receipt соответствуют модельному oracle; эффекты отсутствуют в отказах и preview.
- **AC5**: повтор, конфликт и crash/timeout boundary не приводят ко второму применению; `OutcomeUnknown` разрешается read-back.
- **AC6**: прежние conformance suites проходят, D02 evidence сохранено отдельно; один clean commit проверен на pinned .NET runtime, а docs/knowledge-log получает все существенные выводы, включая отрицательные.

| AC | Automated / manual check | Evidence artifact | If not tested |
| --- | --- | --- | --- |
| AC1 | E05 parser/admission, second valid implementation, mutation negatives | D02 admission receipts | Блокирует завершение |
| AC2 | Exact owner-bundle approval receipt, Dafny proof + independent 96-case table oracle, report inspection | approval digest, proof/result map | Блокирует candidate generation/завершение |
| AC3 | scope/tamper/invalid-code tests | typed refusals + before/after digests | Блокирует завершение |
| AC4 | documented CLI scenario, restart/read-back | sanitized D02 run report | Блокирует завершение |
| AC5 | replay/concurrency/crash injection | receipts and exact state snapshots | Блокирует завершение |
| AC6 | solution build, Modules/Kernel/Graph relevant conformance, diff review | test summary, commit SHA, knowledge log | Блокирует завершение |

Команды EXEC используют repo `.tools/dotnet-sdk-10.0.400/dotnet.exe`, locked restore и существующие conformance entrypoints; конкретная D02 CLI команда появится вместе с API и будет проверена как обычный путь. Если verification или crash recovery не реализованы, D02 остаётся экспериментом без статуса «готово», даже если happy path прошёл. UI/visual/video: не применимо, изменения CLI и файлового sandbox не добавляют UI. G05 performance baseline: не применимо как claim этой SPEC; можно сохранить диагностическое время сборки, но не сравнивать arms.

## 12. Риски и ожидаемые возражения

| Likely objection | Why likely | Mitigation | Status |
| --- | --- | --- | --- |
| «Это снова Reserve с другими именами» | Один transition сам по себе тривиален | Approval binding, терминальные состояния, idempotency, capability и bounded note effect; §6.4, §7 | mitigated |
| «Почему не применяешь в моей базе?» | Сценарий взят из работы с заметками | Изолированный store нужен для проверки языка без риска потери реальных данных; live integration — отдельное решение | accepted-risk |
| «Где гарантии, если статусы I64?» | Тип не исключает все invalid значения | Adapter отказывает до вызова; owner proof охватывает admitted domain; G03 учитывает только доказанную категорию, не приписывает type exclusion | mitigated |
| «Host может нарушить контракт» | TCB не доказан Dafny proof модуля | Exact effect validator, transactional store, fault injection и честная граница гарантий в report | accepted-risk до проверки |
| «Даст ли это экономию времени агента?» | Это одна из главных целей проекта | D02 создаёт второй домен, но G05 нужен отдельный paired protocol и valid runs | accepted-risk |

Риски: неверно связанный owner assertion, race между check и apply, partial dual-write, самосоставленные evidence receipts, overfit узкой функции, чрезмерная инфраструктура. Митигировать schema/digest binding, транзакцией, независимым oracle и adversarial review. Если реализация потребует общих enum/effect opcodes или доступа к реальной базе, остановить EXEC и обновить SPEC с новым подтверждением.

Rework prevention: сценарии/эффекты перечислены §6.3; решения §6.5; AC→evidence §11; возражения выше; role review §19; ordinary CLI и negative checks обязательны.

## 13. План выполнения

1. По утверждённой SPEC построить D02 owner bundle и human projection, сравнить exact model с §6.4/§7, пройти review и заморозить digest. Получить отдельное явное approval владельца **этого exact bundle** до agent candidate generation/admission; при содержательной правке bundle повторить gate.
2. Построить agent module на существующем E05 AST; проверить proof/admission и хотя бы два разных правильных тела.
3. Реализовать bounded D02 host/sandbox, capability checks, transaction, receipt и replay; связать compiled artifact.
4. Пройти happy, refusal, tamper, replay, concurrency/crash corpus с независимым oracle; сохранить очищенные evidence.
5. Сверить требования/документацию, провести post-EXEC review, checkpoint commits и разрешённый периодический push; завершить Unlimotion child только по read-back всех AC.

## 14. Открытые вопросы

Блокирующих проектирование вопросов нет. Утверждение этой SPEC означает выбор именно D02 sandbox сценария и ограничений; оно **не** утверждает ещё не созданный exact owner bundle, конкретное содержимое реальной заметки, live доступ, G03 domain registry или G05 plan. Exact bundle approval — отдельный обязательный gate §6.5/§13, без него зависимая часть EXEC останавливается.

## 15. Профиль

`product-system-design`: цели/non-goals §4–5; границы и публичный API §6, §8; совместимость §6.6/§10; security/capabilities §6.2/§7; фактические доказательства требуются §11. `testing-dotnet` и testing baseline применяются на EXEC к changed behavior. Нет UI.

## 16. Планируемые файлы

| Область | Изменение | Причина |
| --- | --- | --- |
| `specs/2026-09-29-d02-approved-knowledge-change-v0.1.md` | Этот owner-reviewed контракт | Граница смысла/approval |
| `src/Strogo.Modules*`, `formal/`, `examples/` | Минимальный D02 owner bundle, module и admission glue, без изменения E05 schema если возможно | Reuse языка/backend |
| Новый отдельный D02 host/CLI и fixtures | Capability, transactional effect, ordinary run | Исполнимый сценарий |
| `tests/*Conformance` | Proof/integration/negative/fault evidence | Проверка TCB |
| `docs/knowledge-log.md`, отдельное руководство D02, `artifacts/local-validation/d02/` | Выводы, границы, отчёты | Аудит; исторические evidence не перезаписывать |

Точные имена новых файлов выбираются в EXEC; любые изменения публичной E05 schema/semantics требуют повторного gate.

## 17. Было → станет

| Область | Было | Станет |
| --- | --- | --- |
| Домен | Reserve и чистый TaskGraph/E05 examples | Отдельная approved-note-change state machine |
| Эффект | Предметный Reserve host или отсутствие эффекта | Изолированный `RecordTransition`/`ReplaceNote` с exact approval/capability |
| Гарантия | E05 proof чистой функции | E05 proof + явно доверенный D02 effect boundary и интеграционные evidence |
| Измерение | E10 pilot invalid, G03 synthetic controls | D02 corpus/evidence, без G03/G05 вывода |

## 18. Альтернативы

- Переиспользовать E04 TaskGraph: дешевле, но нет состояния утверждения/ограниченного эффекта; не проверяет запрос задачи.
- Сразу добавить enum/ADT/effect syntax в core: улучшает статическую исключаемость, но существенно расширяет язык до необходимости. Выбрана минимальная E05 нотация с закрытым adapter и owner proof; слабость явно учитывается в G03.
- Напрямую применять реальный Obsidian patch: реалистичнее, но риск side effects и ложного вывода выше; выбран sandbox с теми же semantic bindings.

## 19. Quality gate / review

### SPEC Linter Result

| № / блок | Статус | Проверяемое основание |
| --- | --- | --- |
| 1 / A результат | PASS | §1 и §6.3 называют обычный check/apply и видимые исходы. |
| 2 / A AS-IS | PASS | §2 отделяет E04/E05/E10 и предметную связанность Reserve host. |
| 3 / A проблема | PASS | §3 фиксирует отсутствие state+approval+effect второго домена. |
| 4 / A дизайн | PASS | §4 объясняет reuse core и proof/host split. |
| 5 / A границы | PASS | §5 исключает live vault, G03/G05/G06 claims и новые общие opcodes. |
| 6 / B ответственности | PASS | §6.1 отделяет owner, agent, host, toolchain, oracle. |
| 7 / B интеграция | PASS | §6.2 и §8 задают adapter `check`/`apply` и sandbox. |
| 8 / B правила | PASS | §6.4, §7 закрывают 4×3×2³ чистых случаев и эффект. |
| 9 / B ошибки | PASS | §6.2/§6.4 определяют приоритет, replay, conflict, OutcomeUnknown. |
| 10 / B ресурсы | PASS | §9 фиксирует blob/event/receipt bounds; G06 отдельно. |
| 11 / C данные | PASS | §6.2/§9 фиксируют records, digests и транзакцию. |
| 12 / C совместимость | PASS | §6.6/§10 D02 opt-in, E05 schema unchanged. |
| 13 / C rollback | PASS | §10 исключает скрытый откат apply и live rollout. |
| 14 / D AC | PASS | §11 AC1–6 имеют observable success/refusal conditions. |
| 15 / D coverage | PASS | §11 AC→evidence, negative/tamper/replay/crash. |
| 16 / D команды/stop | PASS | §8/§11: ordinary CLI обязан быть документирован и запущен; no proof/recovery → stop. Точная команда появляется с API. |
| 17 / E план | PASS | §13 ставит exact bundle approval до candidate generation. |
| 18 / E решения | PASS | §6.5/§14: блокирующего решения перед SPEC approval нет; отдельный owner gate внутри EXEC. |
| 19 / E форма | PASS | Large expanded SPEC по multi-module/effect/security риску. |
| 20 / F профиль | PASS | §15 и §6.6 покрывают API/security/compatibility `product-system-design`. |

Итог: **ГОТОВО к утверждению проектной SPEC**, не к candidate generation: exact owner-bundle gate §13 ещё впереди.

### SPEC Rubric Result

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Цель и границы | 5 | §1, §3–5 и наблюдаемые сценарии. |
| AS-IS | 5 | E04/E05/Reserve/E10 различены в §2. |
| Конкретность дизайна | 5 | Входы/коды/матрица/host effects §6–7. |
| Безопасность/миграция/rollback | 5 | Sandbox, scoped grant, transaction, §10. |
| Проверяемость | 5 | AC1–6, corpus и evidence §11. |
| Автономность решений | 2 | Owner bundle требует отдельного human gate; до него можно готовить только доверенный контракт. |

Итог **27/30, готово к автономной подготовке owner bundle после утверждения SPEC**, но не к полному EXEC без следующего gate.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | Да | Достаточен ли approval binding и terminal semantics? | PASS | §6.2, §6.4: exact replacement, terminal states, no-op refusal; owner bundle отдельно утверждается. |
| UX / designer | Нет, UI отсутствует | CLI projection проверяется tester | Не применимо | — |
| Tester / validation | Да | Сопоставлены ли proof и effect evidence? | PASS | §7.7, AC2–5 разделяют 96-case proof и host/fault checks. |
| Developer / architect | Да | Реально ли reuse E05 без скрытой новой семантики? | PASS для дизайна | §4/§6.6 запрещают core schema change без revision; EXEC докажет feasibility. |
| Delivery / operations / security | Да | Нет ли доступа к реальной базе и partial write? | PASS для дизайна | §5/§9: отдельная SQLite transaction и sandbox; реальный store запрещён. |

### Post-SPEC Review

**Статус: PASS для запроса утверждения этой проектной SPEC.** Scope reviewed: этот файл, repo `AGENTS.md`, central `quest-mode`, `quest-governance`, `spec-linter`, `spec-rubric`, `review-loops`, `product-system-design`, `docs/project-intent.md`, `docs/modules-v0.2.md`, `docs/task-graph-v1.md`, G03 SPEC/registry rule, E10 terminal report, `Kernel.Host` Reserve API/patch API и inventory `Strogo.Modules`. Planned files — §16; open questions — §14. На фазе SPEC изменён только этот файл.

- Scope/evidence pass: D02 отличается от Reserve business state, transition graph, event и effect; E04 TaskGraph остаётся чистым закрытым pipeline. G03 registration и prevalence не объявлены результатом. E10 invalid pilot не принят как baseline преимущества.
- Contract pass: §6.2/§6.4/§7/AC1–6 проверены на distinction pure proof / trusted host / human owner approval; согласие с этой SPEC не утверждает ещё не созданный owner bundle.
- Adversarial pass: advisory reviewer нашёл пять контрактных контрпримеров; их disposition ниже. Самопроверка обнаружила право на replay после revoke и неясный тип state revision, исправленные до freeze.
- Role-based pass: таблица выше. UI не меняется; CLI projection покрыт tester.
- Fix and re-review: повторно проверены affected §§6.1–6.5, §7, §9, §11, §13–14; advisory reviewer не нашёл новых BLOCKER/HIGH/MEDIUM в исправленном контракте. Финальная самопроверка сверила порядок авторизации перед replay и revision counter.
- Stop decision: можно запрашивать exact «Спеку подтверждаю» **для этой revision**; код, owner bundle и candidate пока не изменяются.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | owner trust | SPEC approval не подтверждает будущий owner bundle | Отдельный exact digest/human projection gate до candidate | fixed в §6.5/§13 |
| HIGH | proof boundary | `Decide` без фактов не доказывает host errors | Явные Bool host facts; proof только 96 pure cases | fixed в §6.2/§7 |
| HIGH | replay | Module не видит eventId/receipt | Host preflight и отдельные AC | fixed в §6.2/§6.4 |
| HIGH | determinism | `InvalidTransition или AlreadyApplied` | Полная state×command таблица и priority | fixed в §6.4 |
| MEDIUM | no-op | Равные digests нарушают обещание изменения | `NoOpReplacement` на создании предложения | fixed в §7 |
| LOW | naming | `patch` после перехода на full replacement | Exact `replacementDigest` terminology | fixed в §6/§11 |

Depth checklist: scope drift — только SPEC, `git status` подтверждается при freeze; AC — §11; validation evidence до EXEC — план проверок, не тест-результат; unsupported claims — G03/G05/G06 явно исключены; regression/edge — no-op, replay, stale, race/crash; docs/changelog — план §16, changelog не нужен до поведения; hidden API/operations change — E05 schema не меняется; manual-review challenge — проверить, что future owner bundle действительно соответствует утверждённой матрице, и что `ReadChange`/command auth происходит до replay. No-findings justification для re-review: исправленные случаи имеют один наблюдаемый ответ и проверку, но реальные тесты/owner approval пока не выполнялись.

Independent-review boundary: вызван agent с ролью `independent-reviewer`, но фактический sandbox `danger-full-access`, не технически read-only. Он делал только чтение; по `review-loops` это **advisory adversarial fallback**, не независимый read-only PASS. Остаточные риски: human acceptance exact owner bundle ещё предстоит; реальная база заметок не тронута; D02 может оказаться слабым G05 workload и это решается только отдельным экспериментом.

### Post-EXEC Review

Не выполнен: EXEC не разрешён.

## Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| RESEARCH | После G03 выбран D02, не копирующий Reserve и E04 | E05 capability inventory; E10 `InvalidPolicy`, G03 anti-collision rule; Unlimotion child claimed | Подготовить и проверить SPEC | G03 approval не распространяется на D02 | Этот SPEC |
| SPEC draft | Предложен owner-approved note change в изолированном store; язык v0.2 без новых opcodes | §§6–12; proof/effect split | Post-SPEC review, fix, freeze | Ожидается | Этот SPEC |
| SPEC review | Контрпримеры owner-bundle, missing host facts, replay, ambiguity, no-op исправлены; advisory review не был read-only sandbox | §19 findings и re-review; реальные EXEC checks ещё не выполнялись | Заморозить SPEC commit/hash, запросить exact approval | Ожидается | Этот SPEC |
