> Историческая спецификация из исходного проекта. Личные пути в этой публикационной копии заменены; original commit/blob IDs относятся к исходной локальной истории. [Происхождение публикации](../docs/publication.md).

# Ограниченный профиль преобразования графа задач v1

## 0. Метаданные

- Статус: EXEC реализован и проверен 2026-09-06; финальная механическая сверка AC13 фиксируется knowledge-check.json. Начат по точной фразе владельца «Спеку подтверждаю». Утверждена версия commit 8a9342e8b0458c55ce1eb5096dc1b16901bec6ba, blob 2cfed958a7fa6c6717f4183cc9fb3c232489bae7. Исторический дизайн ниже сохранён; уточнения реализации перечислены в журнале EXEC.
- Тип (профиль): `delivery-task`, `product-system-design`, QUEST.
- Владелец: Kibnet; автор рабочей SPEC: Codex.
- Масштаб: large — новый проверяемый профиль, формальный backend и путь компиляции до нативного кода.
- Целевое семейство / behavior baseline: центральный baseline GPT-5.6; выбор модели не влияет на семантику профиля. Эффект для LLM в этом этапе не измеряется.
- Поверхность: Codex и локальный CLI.
- Effective runtime: текущий Codex; точный model ID/reasoning не предоставлен средой и не является входом компилятора. Целевая среда артефакта — `win-x64`, .NET SDK из закреплённого `global.json`.
- Eval baseline / evidence: проверенный v0 — 29/29 conformance-сценариев и 10 904 assertions; корпус C01–C05 из реальной задачи `CloneRecurringSubtree`. Эти данные являются baseline и корпусом, но ещё не evidence нового профиля.
- Целевой релиз / ветка: локальная ветка `main`, этап E04; remote/push/release не входят в задачу.
- Ограничения: максимум 16 задач, 8 критериев на задачу, 64 containment-рёбер, 64 blocking-рёбер, 65 536 байт JSON; только чистое преобразование конечного графа; внешние эффекты выполняет host.
- Связанные ссылки:
  - Замысел и цель (исторический локальный источник)
  - Требуемые свойства (исторический локальный источник)
  - Корпус C01–C12 (исторический локальный источник)
  - [SPEC v0](2026-09-04-agent-language-kernel-v0.md)
  - [Неутверждённая SPEC v0.1](2026-09-04-controlled-language-experiment-v0.1.md)
  - [Dafny Reference](https://dafny.org/dafny/DafnyRef/DafnyRef)
  - [Dafny installation and compilation](https://dafny.org/latest/Installation)
  - [.NET ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)

## 1. Overview / Цель

Создать второй исполнимый профиль языка для агентов: ограниченную программу преобразования `TaskGraph`, которую агент строит как канонический типизированный AST, а toolchain проверяет относительно неизменяемого формального контракта и компилирует через Dafny/C# в запускаемый `win-x64`-артефакт с ReadyToRun-кодом.

Первый контракт — `task-graph.clone.v1`. Он создаёт независимую копию containment-подграфа повторяющейся задачи: все копии получают предоставленные host свежие ID, общий DAG-потомок копируется один раз, внутренние связи переназначаются, внешние blocking-связи отбрасываются, исходный граф сохраняется. Цикл, неполная/лишняя таблица ID, повтор или пересечение ID и переполнение даты дают типизированный отказ без частичного patch.

Outcome contract:

- Success means: один принятый AST доказан относительно закреплённого контракта для всех допустимых входов ограниченного домена; C01–C05 воспроизводятся исполняемым нативно подготовленным модулем; намеренно испорченные реализации не получают admission; v0 остаётся без регрессий.
- Итоговый артефакт / output: строгий JSON AST `kernel.graph.v1`, формальный контракт Dafny, детерминированный lowering, proof/admission receipt, скомпилированный `win-x64` runner, эталонный evaluator, conformance, отчёт E04, журнал знаний E04 в базе знаний и manifest их синхронизации.
- Stop rules: fail closed при parse/type/contract/verifier/compiler/runtime mismatch, timeout, `unknown` или несоответствии digest. Не расширять домен, эффекты, платформы и библиотеки до прохождения C01–C05 и mutation checks. Не объявлять G05/G06 достигнутыми по результатам E04. Не считать checkpoint или E04 завершёнными, пока все возникшие к этому моменту значимые знания не записаны в базу знаний и не прошли reconciliation.

Этап даёт свидетельства главным образом для G01–G04 и P02–P09, P11–P17, P20, P22, P24, P27, P30–P32. Он не измеряет выигрыш полного цикла G05 и сравнительную эффективность G06.

## 2. Текущее состояние (AS-IS)

В репозитории реализован `kernel.v0` для Reserve: строгий JSON graph, типы, SSA IR, два исполнителя, Z3 admission, capabilities, атомарный host и replay. Он проверен локально, но не компилирует программу языка в машинный код и покрывает один арифметический переход.

SPEC v0.1 предлагает эксперимент двух нотаций Reserve и архитектурный курс, но не утверждена и не реализована. E04 от неё не зависит и не меняет её статус.

В базе знаний выбран реальный корпус C01–C12. C01–C05 образуют первый профиль конечного графа. Реальная задача показала важные требования, которых нет в Reserve: достижимость, DAG с общим потомком, referential integrity, fresh-ID bijection, remap только внутренних связей, framing исходного графа и полный отказ вместо частичного результата.

В проекте нет Dafny, графового frontend, формального контракта `TaskGraph`, compiled module runner или ReadyToRun evidence. Сам факт, что host написан на .NET, не доказывает P27 для программ языка.

## 3. Проблема

Нужно проверить центральную гипотезу языка на задаче, где «почти правильная» реализация типична: легко продублировать общий узел, сохранить внешнюю связь, забыть критерии, частично применить результат или принять неверную таблицу ID. Обычный успешный набор примеров не гарантирует корректность для всех допустимых графов. Требуется связать agent-authored AST, формальный owner-confirmed контракт, доказательство, исполнимый модуль и наблюдаемое машинное выполнение одной воспроизводимой цепочкой.

## 4. Цели дизайна

- Контракт и обязательные инварианты принадлежат профилю/владельцу; агент не может ослабить их своим AST.
- У каждой операции один opcode, один набор типов и один порядок вычисления.
- Программа является конечным typed DAG без произвольного кода, reflection, FFI, сети, файлов и часов.
- Проверка охватывает все входы ограниченного домена, а примеры и mutation tests проверяют toolchain и границу доверия.
- Host создаёт fresh IDs и атомарно применяет typed patch; чистый модуль только проверяет и вычисляет.
- Компиляция использует зрелые Dafny и .NET toolchains; проект отвечает за собственный AST, lowering, профиль и conformance.
- Человек подтверждает смысл конкретного contract digest через вычисленную проекцию до admission реализации.
- Любая недоказанность, несовместимость или операционный сбой приводит к отказу, а не к ослабленной гарантии.
- Существующий `kernel.v0` остаётся работоспособным и версионированным отдельно.
- Инсайты, подтверждения, опровержения и уточнения гипотез, контрпримеры, существенные ограничения и решения становятся частью базы знаний в том же рабочем блоке, в котором они установлены.

## 5. Non-Goals (чего НЕ делаем)

Общий язык коллекций и графовых алгоритмов; свободный Dafny/C#/SMT source от агента; agent-defined axioms/contracts; собственный SMT encoder для map/set; доказательство корректности Dafny, Boogie, Z3, .NET SDK, RyuJIT/ReadyToRun или ОС; OS sandbox общего назначения; хранилище и конкурентные транзакции; генерация UUID внутри программы; реальное изменение Unlimotion; сеть, файлы пользователя, процессы и отправка наружу; произвольные библиотеки; package manager; второй backend; Linux/macOS/ARM evidence; NativeAOT; byte-for-byte reproducible ReadyToRun; live LLM benchmark; измерение G05/G06; утверждение, что тесты вообще больше не нужны; перенос сырых логов, секретов и нерелевантного технического шума в базу знаний.

Тесты E04 проверяют компилятор, integration, TCB assumptions и стабильность evidence. Они не служат заменой доказательству прикладного постусловия `task-graph.clone.v1`.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент/файл | Ответственность |
| --- | --- |
| `src/Kernel.Graph.Core/Model` | Типы AST, `TaskGraph`, input, typed result и receipt |
| `src/Kernel.Graph.Core/Codec` | Строгий JSON, limits, duplicate/unknown-field rejection, canonical bytes/digests |
| `src/Kernel.Graph.Core/Validation` | Типы, зависимости, единственный pipeline, referential integrity и error precedence |
| `src/Kernel.Graph.Core/Lowering` | Детерминированное сопоставление разрешённого AST с фиксированными Dafny constructs |
| `src/Kernel.Graph.Core/Toolchain` | Pinned Dafny verify/build, source checks, timeout, process isolation и receipt |
| `src/Kernel.Graph.Core/Reference` | Независимая C#-модель результата C01–C05 для differential checks |
| `src/Kernel.Graph.Cli` | `contract`, `compile`, `run`, `explain`; русская проекция для человека |
| `verification/task-graph-v1/Contract.dfy` | Owner-controlled типы, predicates, total outcome и теоремы профиля |
| `verification/task-graph-v1/RuntimeAdapter` | Минимальный доверенный JSON↔compiled Dafny bridge |
| `tests/Kernel.Graph.Conformance` | C01–C05, negative input, mutation, determinism, resource и regression checks |
| `fixtures/task-graph-v1` | Версионированные inputs, AST candidates и ожидаемые typed outcomes |
| `tools/dafny.json`, `tools/Install-Dafny.ps1` | Версия, официальный release URL, SHA-256 и локальная установка без глобальной мутации |
| `artifacts/e04/knowledge-sync.json` | Проверяемое соответствие knowledge IDs, KB paths/anchors, source evidence и commit |
| `Технологии/Язык для агентов/10 — Журнал знаний E04.md` | Хронологический журнал значимых знаний; база знаний является source of truth |

### 6.2 Детальный дизайн

#### 6.2.1 Поток данных и admission

`human specification → owner contract → human contract approval → agent AST → strict parse/type check → canonical lowering → Dafny verification → C# compile → ReadyToRun publish → receipt → run on input → typed patch → host atomic apply`.

Точная фраза утверждения этой SPEC подтверждает человеком смысл профиля, включая домен, pipeline, правила 6.2.2–6.2.5 и 14 инвариантов. Утверждение связывается с Git blob/commit этой SPEC. Во время EXEC команда `contract` создаёт каноническую проекцию и `contractDigest`; локальный `ContractApproval` связывает этот digest с уже утверждённой SPEC и provenance пользовательского решения. Дополнительное подтверждение не требуется, пока проекция семантически совпадает с утверждённым текстом. Любое изменение смысла требует новой SPEC/approval. Производственный формат подписи остаётся вне E04.

Admission имеет три независимых условия:

1. AST корректен и соответствует единственному профилю.
2. Сгенерированный Dafny source проходит запрет proof-bypass constructs и `dafny verify/build --enforce-determinism` для закреплённого toolchain.
3. `programDigest`, `contractDigest`, `generatedSourceDigest`, target и tool digests совпадают с receipt при запуске.

`Verified` не является полем входа. Его вычисляет owner toolchain только после успешных стадий. Timeout, crash, non-zero exit, diagnostic error, `unknown`, отсутствие expected artifact или digest mismatch возвращают `Rejected`.

#### 6.2.2 Исходная структура `kernel.graph.v1`

Source — UTF-8 JSON не более 65 536 байт. JSON parser обязан отклонять BOM, duplicate keys, неизвестные поля, неизвестные enum/opcode, неканоничные числа и отсутствующие обязательные поля. Транспорт допускает порядок полей и whitespace; semantic digest строится из canonical encoding: UTF-8 без BOM/whitespace, keys по ASCII, arrays только в указанном семантическом порядке, множества сортируются правилами профиля.

Обязательные поля программы:

`schemaVersion="kernel.graph.v1"`, `programId`, `profileId="task-graph.clone.v1"`, `steps`, `outputs`.

ID программы/шага: ASCII `[a-z][a-z0-9._-]{0,63}`. Каждый step объявляет ровно один typed value, ссылается только на предыдущие values, используется выходом или последующим step. Циклы, dangling references и dead steps запрещены. Порядок steps нормативен и совпадает с dependency order; альтернативная топологическая перестановка не является вторым каноническим source.

Разрешён ровно один pipeline:

| № | Opcode | Input | Output | Семантика |
| ---: | --- | --- | --- | --- |
| 1 | `task.reachable_contains` | graph, sourceRootId | `ReachableTaskSet` | Все задачи, достижимые из root по containment, включая root |
| 2 | `ids.require_fresh_bijection` | reachable, taskIdMap, criterionIdMap | `ValidatedFreshIds` | Точные полные взаимно-однозначные maps без лишних записей и пересечений |
| 3 | `task.clone_reachable` | graph, reachable, IDs, dateDelta | `ClonedTaskSet` | Копирует payload/repeat rule, сдвигает даты, сбрасывает lifecycle и completion |
| 4 | `edge.remap_internal` с `kind="contains"` | graph, reachable, IDs | `ContainsEdgeSet` | Переносит только рёбра, оба конца которых в reachable |
| 5 | `edge.remap_internal` с `kind="blocks"` | graph, reachable, IDs | `BlocksEdgeSet` | Переносит только внутренние blocking-рёбра; внешние опускает |
| 6 | `graph.additions` | cloned tasks, contains, blocks | `TaskGraphPatch` | Создаёт только additions; исходные сущности не изменяет |
| output | `result` | patch/outcome | `CloneOutcome` | `Accepted(patch)` либо типизированный `Rejected` |

Параметры policies не свободны: `copyPayload=true`, `copyRepeatRule=true`, `shiftStart=true`, `shiftDue=true`, `resetStatus=Planned`, `resetHistory=true`, `resetCriteriaCompletion=true`. Поле с иным значением, пропуск или дополнительная операция отклоняются до Dafny. Эта узость намеренна: агент выбирает точную реализацию из разрешённых сущностей, но не может изобрести новый эффект или ослабить contract.

Lowerer назначает Dafny identifiers по ordinal steps (`s00`…`s05`), а строки/ID кодирует как значения. Никакой input string не интерполируется в identifiers, directives, attributes или raw code.

#### 6.2.3 Домен данных

`TaskGraph` содержит:

- `tasks`: 0–16 `TaskNode`, уникальные task ID;
- `contains`: 0–64 уникальных направленных рёбер;
- `blocks`: 0–64 уникальных направленных рёбер;
- все endpoints существуют;
- self edges запрещены;
- duplicate edge запрещён;
- ID task и criterion принадлежат общей namespace и уникальны глобально.

`TaskNode` содержит `id`, `payload` (opaque UTF-8 string до 256 байт), nullable `startMinute`/`dueMinute` как I64 decimal string, `status`, 0–8 criteria, nullable `repeatRule` и history из не более 8 opaque entries. Criterion содержит stable `id`, payload до 128 байт и `completed`. E04 не интерпретирует payload/repeat/history.

`CloneInput` содержит graph, `sourceRootId`, `dateDeltaMinutes`, `taskIdMap` и `criterionIdMap`. Host вычисляет/выдаёт ID и delta до вызова. Модуль не читает время и не генерирует случайность.

`CloneOutcome`:

- `Accepted`: canonical `TaskGraphPatch` только из `AddTask`/`AddContains`/`AddBlocks`;
- `Rejected`: `stage`, `code`, sorted `involvedIds`, typed `witness` при наличии и closed-list `allowedRepairs`;
- rejected outcome всегда содержит `patch=null`.

#### 6.2.4 Формальный контракт и инварианты

Формальный contract — отдельный owner-controlled Dafny module. Агентский AST не содержит `requires`, `ensures`, `assume`, axiom, extern, verifier flags или imports.

Для `Accepted` доказываются одновременно:

1. Reachable точно равен транзитивному containment-замыканию root.
2. Для каждого reachable task существует ровно одна новая task; других new tasks нет.
3. Task ID map и criterion ID map имеют точный domain и injective range.
4. Fresh ranges не пересекаются ни с одним существующим task/criterion ID и друг с другом.
5. Payload и repeatRule каждой копии равны источнику.
6. Status каждой копии = `Planned`, history пуст, каждый criterion не выполнен и имеет mapped fresh ID.
7. Nullable start/due сдвинуты ровно на delta; null остаётся null; I64 overflow невозможен в accepted outcome.
8. Каждое internal contains/block edge скопировано ровно один раз с remapped endpoints.
9. Ни одно edge с endpoint вне reachable не входит в patch.
10. Patch не обновляет и не удаляет исходные entities.
11. Применение patch к валидному входу сохраняет referential integrity, global ID uniqueness и acyclic containment.
12. Порядок entities/edges в wire output канонический и не меняет математический результат.
13. `Accepted` и `Rejected` взаимоисключающи; reject не содержит частичного patch.
14. Алгоритм завершается на всех schema-valid входах в установленных границах.

Обязательность всех инвариантов полезна только в определённой границе. E04 требует полный набор инвариантов конкретного профиля, но не утверждает, что владелец уже описал все свойства реального продукта. Пропущенное требование в owner contract не может быть выведено verifier. Именно поэтому человек подтверждает смысл contract projection.

#### 6.2.5 Порядок ошибок

Для одинакового input возвращается одна ошибка по приоритету:

1. `TransportInvalid` / `SchemaInvalid` / `LimitExceeded`;
2. `DuplicateEntityId` / `DanglingRelation` / `DuplicateRelation`;
3. `SourceRootMissing`;
4. `ContainmentCycle`;
5. `TaskIdMapDomainMismatch`;
6. `CriterionIdMapDomainMismatch`;
7. `FreshIdInvalid`;
8. `FreshIdCollision`;
9. `DateOverflow`;
10. `InternalInvariantViolation`.

Внутри одного code `involvedIds` сортируются ASCII и deduplicate. `allowedRepairs` выбирается из versioned enum. Ошибка 10 означает дефект TCB/adapter, никогда не admission пользовательской программы.

#### 6.2.6 Dafny и путь до машинного кода

Закрепляется versioned Dafny release `4.11.0`, а не nightly. `tools/dafny.json` хранит URL официального архива, version, SHA-256 и expected executable digest; install script проверяет hash до распаковки в ignored `.tools/dafny/`. Если опубликованный архив/хеш не удаётся получить и проверить, EXEC останавливается.

Dafny выбран потому, что один toolchain поддерживает executable methods, formal specifications и компиляцию в C#/.NET. Команда и точные flags уточняются по `dafny --help` закреплённой версии и фиксируются в report; минимальная нормативная цепочка:

- verify/build generated module с target C# и `--enforce-determinism`;
- compile generated C# и доверенный adapter через locked .NET restore/build;
- `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true`;
- выполнить опубликованный runner на C01–C05 и dynamic conformance inputs.

ReadyToRun содержит IL и нативные версии методов; отдельные методы всё ещё могут JIT-компилироваться. Поэтому P27 evidence формулируется точно: опубликованный `win-x64`-артефакт содержит нативный R2R code и фактически выполняет профиль на целевой платформе. E04 не обещает, что 100% методов AOT или что runtime не использует JIT.

#### 6.2.7 Граница доверия и proof bypass

Trusted computing base E04: contract authoring/approval, AST parser/validator, canonicalizer, lowerer, generated support module, Dafny/Boogie/Z3, Dafny C# backend, C# adapter, .NET SDK/runtime/ReadyToRun compiler, ОС и receipt verifier.

Generated source строится только из собственных templates/typed values. До verifier выполняется structured/source allowlist: запрещены `assume`, `{:axiom}`, `{:extern}`, `{:verify false}`, `include`, незакреплённые imports, raw compiler directives и другие средства обхода, найденные для pinned Dafny. Проверка сочетает:

- отсутствие raw source API у агента;
- generated-source allowlist;
- fixed contract/support digests;
- negative fixtures для каждого известного bypass;
- inspection фактических verifier arguments;
- mutation tests неверной семантики.

Статический scan не доказывает корректность TCB. Receipt честно перечисляет trusted components и версии. Успех означает доказательство относительно этого TCB и owner contract.

Dafny/tool processes запускаются с working directory внутри generated artifact root, закрытым environment allowlist, без секретов, stdin, network-dependent imports или пользовательских paths. Wall-clock timeout 60 секунд на verify и 120 секунд на build/publish; output ограничен 1 MiB, child process tree завершается при timeout. Ограничения RAM/CPU средствами ОС не обещаются в E04 и записываются как residual risk.

#### 6.2.8 Receipt, provenance и воспроизводимость

`GraphAdmissionReceipt` содержит:

`receiptVersion`, `programDigest`, `contractDigest`, `contractApprovalDigest`, `canonicalAstDigest`, `generatedSourceDigest`, `supportDigest`, `dafnyVersion`, `dafnyArchiveDigest`, `dafnyExecutableDigest`, `verifierArguments`, `dotnetSdkVersion`, `runtimeIdentifier`, `buildInputsDigest`, `compiledArtifactDigest`, `verificationOutcome`, `verificationDurationMs`, `determinismEnforced`, `createdAtUtc` и `provenance`.

В semantic admission identity не входят wall-clock duration и createdAt; они audit metadata. Повторная компиляция обязана дать одинаковые canonical AST/generated source/build-input digests и одинаковое runtime behavior. Byte-identical R2R binary измеряется и сообщается отдельно, но не является acceptance gate, потому что зрелый toolchain может включать недетерминированные build metadata.

`run` принимает только receipt с совпадающими digests и profile. Runtime input проверяется тем же strict codec. Результат сравнивается с compiled module; reference evaluator используется в conformance как независимое integration evidence и не выдаёт admission.

#### 6.2.9 Human projection

`contract` и `explain` показывают на русском:

- какую операцию модуль реализует;
- точный допустимый input domain;
- все 14 инвариантов accepted/rejected;
- эффекты: только возврат typed additions, без записи/IO;
- error precedence и resource limits;
- contract digest и статус его human approval;
- что доказано, что осталось в TCB и что не проверено;
- target и digests compiled artifact.

Изменение смысловых contract bytes создаёт новый digest и требует нового human approval. Изменение реализации при прежнем contract требует нового proof/receipt, но не повторного смыслового approval.

Visual planning artifact: GUI не предусмотрен; нормативная текстовая проекция выше проверяется snapshot/semantic tests. UI automation video evidence не применимо.

#### 6.2.10 Обязательное сохранение знаний

База знаний `<private-knowledge-base>/Технологии/Язык для агентов` является source of truth для накопленных знаний о языке. Project `REPORT.md`, Git history, terminal output и `artifacts/e04` служат evidence, но не заменяют запись знания в базе.

Значимым считается любое установленное в SPEC/EXEC/review знание хотя бы одного класса:

1. новая гипотеза либо подтверждение, опровержение, существенное ограничение или статус `Inconclusive` ранее записанной гипотезы;
2. новый инвариант, класс ошибки, контрпример, failure mode, граница доказательства или изменение trusted computing base;
3. архитектурное/продуктовое решение и его основание, изменяющее design, roadmap или interpretation G01–G06/P01–P34;
4. результат измерения или эксперимента, влияющий на оценку свойства языка;
5. неудачный подход, причина его непригодности и условие, при котором к нему имеет смысл вернуться;
6. внешний источник, новый термин или воспроизводимая процедура, без которых будущий исследователь может повторить ошибку или неверно истолковать результат.

Не считаются самостоятельным знанием: полный raw log, повтор уже записанного факта без нового evidence, routine build success без влияния на гипотезу, временный путь/случайный diagnostic и секреты/персональные данные. Они могут оставаться evidence по ссылке.

Каждая запись в `10 — Журнал знаний E04.md` имеет:

- стабильный `K-E04-NNN`;
- дату и фазу `SPEC/EXEC/REVIEW`;
- тип `Hypothesis/Confirmation/Refutation/Qualification/Insight/Decision/Failure/Constraint/Procedure/Source`;
- одно проверяемое утверждение;
- статус `Proposed/Testing/Confirmed/Refuted/Qualified/Inconclusive/Accepted`;
- scope и связь с G/P/AC либо `Не применимо` с причиной;
- evidence: path/command/receipt/counterexample/source URL и Git commit;
- последствие для design, implementation, roadmap или `без изменения`;
- ссылки `supersedes/supersededBy`, если знание уточняется.

Правила процесса обязательны:

1. Запись создаётся или обновляется в том же рабочем блоке, где знание стало обоснованным, и обязательно до следующего checkpoint commit, запроса решения пользователю или финального отчёта.
2. При изменении вывода старая запись не удаляется и не переписывается как будто ошибки не было: новая запись/ревизия помечает прежнюю `Refuted/Qualified/Superseded` и сохраняет provenance.
3. Стабильный вывод из журнала в том же checkpoint переносится в канонический документ: `02 — Требуемые свойства` для P-статуса, `04 — Решения и открытые вопросы` для решений, `05 — Эксперименты и свидетельства` для результатов, `06 — Дорожная карта` для next steps, `07 — Источники и документы` для provenance, `08 — Термины` для терминов. `01 — Замысел и цель` меняется только при отдельном human-approved изменении цели.
4. `knowledge-sync.json` перечисляет каждый knowledge ID, hash утверждения, KB path/anchor, evidence refs, project commit и KB commit. Автоматическая проверка выявляет отсутствующие/битые ссылки и несовпадающие hashes.
5. Если checkpoint не дал нового значимого знания, manifest содержит reconciliation record `noNewSignificantKnowledge` с проверенным диапазоном commits/evidence; это не создаёт пустую запись в KB.
6. Project и KB изменяются отдельными логическими Conventional Commits. Checkpoint нельзя объявить готовым, если нужная запись ещё только находится в незакоммиченном diff.
7. Review обязан искать пропущенные отрицательные результаты и ограничения. `Check-KnowledgeSync.ps1` проверяет структуру/ссылки, а содержательную полноту подтверждает post-EXEC review.

Если есть сомнение, значим ли вывод, он записывается как `Insight/Proposed`; позднее его можно квалифицировать. Это правило выбирает сохранение потенциально полезного знания, одновременно не превращая KB в копию terminal log.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| C01 | Compile/run root с одним child | Accepted; две новые задачи, fresh criteria/IDs, shifted dates, source unchanged | receipt, JSON outcome, oracle match | AC4, AC5 |
| C02 | Run DAG с общим descendant | Общий descendant скопирован ровно один раз, обе связи указывают на одну копию | graph invariant log | AC3, AC4 |
| C03 | Run internal и external blocking edges | Internal remapped; external отсутствует; нет dangling edge | patch diff, oracle match | AC3, AC4 |
| C04 | Run graph с containment cycle | `Rejected/ContainmentCycle`, `patch=null` | typed error snapshot | AC4 |
| C05 | Run incomplete/duplicate/colliding ID maps | Приоритетный typed reject, `patch=null` | negative matrix | AC4 |
| M01 | Compile candidate, который сохраняет completed/status или external edge | Admission rejected verifier/contract stage | verifier diagnostic + mutation id | AC4, AC6 |
| T01 | Повторить compile/run | Равные semantic digests и outputs; binary reproducibility reported separately | determinism report | AC7 |
| H01 | Открыть contract/explain | Читаемая проекция contract, TCB, limits, effects и digests | approved snapshot | AC11 |
| K01 | Получен контрпример или изменился статус гипотезы | В KB появляется versioned `K-E04-NNN` с evidence и влиянием на G/P/roadmap | KB diff/commit + sync manifest | AC13 |
| K02 | Завершается checkpoint без нового вывода | Manifest содержит проверяемый `noNewSignificantKnowledge` для диапазона evidence | reconciliation check | AC13 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| No contract approval | compile | Rejected `ContractNotApproved` | fixture approval допустим только harness mode | Approval связывается с digest |
| Approved contract, no receipt | compile valid AST | Verified receipt + artifact | timeout/error → Rejected, artifact не активируется | Fail closed |
| Approved contract | compile invalid AST | Typed parse/type/profile reject | Dafny не запускается | Дешёвые проверки раньше |
| Verified receipt | run valid input | Accepted/Rejected domain outcome | empty graph → SourceRootMissing | Pure execution |
| Verified receipt | run tampered artifact/input | Digest/schema reject | no fallback artifact | Нельзя подменить binary |
| Verified receipt | two concurrent runs | Независимые pure outcomes | adapter не хранит mutable domain state | Host commit вне E04 |
| Accepted patch | host apply (model only) | Atomic apply expected | stale snapshot/write failure → no partial write | Сам storage не реализуется |
| Any | explain | Read-only projection | missing receipt помечается явно | Не меняет admission |
| Knowledge отсутствует в KB | checkpoint/final gate | Gate fails, работа остаётся незавершённой | raw log или REPORT не считается заменой | Исправить KB и manifest |
| Вывод позже опровергнут | новое evidence | Старая запись сохраняется, новая связывает refutation/supersession | нельзя silently rewrite history | Provenance обязателен |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Первый профиль | user delegated selection; agent chose | C01–C05 `CloneRecurringSubtree` | 0.97 | Слишком узкий корпус | Нет |
| Formal backend | agent | Dafny 4.11.0 → C#/.NET | 0.88 | TCB больше, чем у proof assistant | Нет |
| Native evidence | agent | `win-x64` ReadyToRun + actual run | 0.92 | Не все methods обязательно AOT | Нет |
| Contract ownership | user previously decided | Owner/human-controlled digest | 0.99 | Пропущенное требование не доказывается | Нет |
| Fresh IDs | agent from corpus | Host supplies maps, module validates | 0.98 | Host может выдать плохие IDs; получит reject | Нет |
| Graph limits | agent | 16/8/64/64/65 536 | 0.84 | Не отражает production scale | Нет |
| Reject precedence | agent | фиксированный список 1–10 | 0.91 | Ошибка отличается от текущего продукта | Нет; это новый профиль |
| Existing v0 | agent | additive projects, no semantic change | 0.98 | Solution/docs integration может задеть build | Нет |
| Contract projection approval format | agent | local digest-bound record for E04 | 0.82 | Не production signing | Нет |
| Knowledge capture | user | обязательный KB journal + promotion + sync gate | 1.00 | Потеря отрицательных результатов и повтор ошибок | Нет |

Блокирующих проектных решений в ledger нет. Стандартный QUEST gate всё равно требует утверждения всей SPEC точной фразой в конце документа.

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Source schema | `kernel.v0` codec | новый `kernel.graph.v1` | additive, отдельный profile | schema negatives |
| Formal contract | отсутствует | pinned `Contract.dfy` + digest | изменение смысла → новый digest/approval | projection + proof |
| Dafny | отсутствует | local 4.11.0 archive + hashes | no global install | hash/install check |
| .NET | `global.json` | reuse pinned SDK; add graph projects | v0 public behavior preserved | locked restore/build |
| Runtime target | managed v0 CLI | `win-x64` R2R graph runner | additive artifacts | publish + execute |
| State/storage | v0 SQLite Reserve | без изменения | graph module returns patch only | no storage migrations |
| Secrets/network | none required | no secrets; install may download official archive once | offline after cache | env/process inspection |
| Evidence | v0 report/conformance | E04 JSON/Markdown receipt and reports | separate artifact namespace | digest/check scripts |
| Knowledge | существующие KB `00–09` | добавить `10 — Журнал знаний E04.md`, продвигать стабильные выводы в `01–08` | история не стирается; refutation/supersession явны | `knowledge-sync.json` + post-EXEC content review |

## 7. Бизнес-правила / Алгоритмы

Псевдосемантика:

1. Strict-decode program/input and enforce bounds.
2. Validate all graph IDs/edges and detect any containment cycle.
3. Find exact reachable set from root.
4. Validate exact task map domain = reachable.
5. Validate exact criterion map domain = all criteria owned by reachable tasks.
6. Validate fresh ranges, global uniqueness, disjointness and ID syntax.
7. Checked-shift every non-null start/due; any overflow rejects whole result.
8. Create one cloned node per reachable source, ordered by source ID.
9. Recreate criteria ordered by source criterion ID with completion false.
10. Recreate internal contains and blocks ordered by `(from,to)`.
11. Return additions patch; do not mutate input.
12. Independently validate output invariants before serialization; internal mismatch fails closed.

Cycle validation is global for the supplied graph. Таким образом нерелевантный цикл тоже делает snapshot недопустимым; профиль не обещает корректно продолжать работу на уже нарушенном graph invariant.

Canonical order: tasks and criteria by ordinal ASCII ID; edges by kind, from, to; errors/IDs by ASCII. Date math is checked signed I64. Empty reachable невозможен после существующего root. Repeated shared descendant resides in set/map once.

## 8. Точки интеграции и триггеры

- `Kernel.Graph.Cli contract` строит projection из fixed contract и approval record.
- `compile --program <program.json> --approval <approval.json>` запускает codec → validation → lowering → Dafny → .NET publish → receipt.
- `run --receipt <receipt.json> --input <input.json>` проверяет receipt/artifact/input и вызывает compiled module.
- `explain --receipt <receipt.json>` или `explain --outcome <outcome.json>` строит human projection.
- Conformance запускает тот же public CLI/process boundary, а также direct unit-level codec/validator checks.
- Existing `Kernel.Cli` Reserve commands не вызывают новый профиль.
- Host apply остаётся будущей точкой интеграции: он обязан сверять snapshot revision/capability и атомарно применять patch.
- Любой experiment/review/checkpoint, породивший значимое знание по 6.2.10, вызывает обновление KB до checkpoint commit; финальный gate запускает `Check-KnowledgeSync.ps1`.

## 9. Изменения модели данных / состояния

Новые immutable records: `GraphProgram`, `GraphStep` variants, `TaskGraph`, `TaskNode`, `Criterion`, `GraphEdge`, `CloneInput`, `CloneOutcome`, `TaskGraphPatch`, `GraphError`, `GraphAdmissionReceipt`, `ContractApproval`.

Persisted: program, contract/support/tool digests, receipt, compiled artifact manifest, fixtures/evidence, KB knowledge records и knowledge-sync manifest. Calculated: reachable set, cloned entities, output patch, projection. Business state не хранится и не меняется E04.

Все integer transport fields — canonical decimal strings в I64. Hash — lowercase SHA-256. Version fields обязательны. Миграции v0/SQLite нет.

## 10. Миграция / Rollout / Rollback

Первый запуск устанавливает Dafny в локальную ignored tool folder после hash verification. Проекты и profile добавляются к solution, существующие команды не меняются.

Rollout локальный: build → conformance → R2R publish → C01–C05 → mutation → v0 regression → knowledge reconciliation → post-EXEC review. Git checkpoint после dependency/toolchain skeleton, после verified formal profile и после итогового evidence, если каждый этап отдельно проходит свои checks. Перед каждым checkpoint значимые выводы уже находятся в KB commit и отражены в sync manifest.

Rollback — удалить additive graph projects/verification/fixtures/tools entries и вернуть solution/docs к предыдущему commit. Business storage отсутствует. Tool cache можно удалить без потери source/evidence manifest. Knowledge records о выполненном и откате не удаляются: добавляется запись об откате и актуальном статусе выводов. Push/release не выполняются.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- **AC1 — Strict source/input.** Duplicate/unknown/missing fields, invalid UTF-8/number/ID/opcode, excess bounds, cycles/dangling/duplicate edges отклоняются одним typed outcome до verifier/runtime.
- **AC2 — Canonical typed pipeline.** Разрешён ровно pipeline из 6 операций; dependencies/types/order/dead-code checks проходят positive fixture и отвергают все структурные мутации.
- **AC3 — Universal contract proof.** Для accepted program Dafny подтверждает инварианты 1–14 на всём bounded domain; proof-bypass fixtures не получают receipt.
- **AC4 — Corpus and mutations.** C01–C05 дают ожидаемые outcomes; минимум 10 semantic mutations (duplicate shared child, retain external block, preserve completed/status/history, omit criterion, wrong date shift, partial patch on reject, update source, map extra/missing) отвергаются admission или contract checks.
- **AC5 — Executable machine path.** Pinned toolchain создаёт `win-x64` ReadyToRun publish; опубликованный runner фактически обрабатывает C01–C05 и dynamic generated inputs, совпадая с reference evaluator.
- **AC6 — Fail-closed evidence.** Timeout/error/unknown/non-zero/missing artifact/tampered digest/wrong flags возвращают typed rejection и не создают verified receipt.
- **AC7 — Determinism/replay.** Два clean build/run дают равные canonical/source/input digests и outcomes; binary digest comparison сохраняется с честным verdict, но byte equality не обязательна.
- **AC8 — Resource/process bounds.** Source/domain/output/time limits соблюдаются; over-limit cases завершаются bounded reject, process tree завершается на timeout, output truncation отмечается.
- **AC9 — TCB transparency.** Receipt/explain перечисляют contract, support, Dafny, solver/backend, SDK/runtime, adapter, hashes и guarantee boundary; raw agent source не достигает Dafny/C#.
- **AC10 — No v0 regression.** Existing solution build и 29 v0 conformance-сценариев/10 904 assertions проходят без изменения ожидаемого поведения.
- **AC11 — Human contract projection.** Проекция содержит domain, 14 инвариантов, effects, limits, errors, TCB и digest; approval другого digest не принимается.
- **AC12 — Claims boundary.** Report не заявляет G05/G06, все платформы, полный AOT или correctness вне contract/TCB; результат помечает подтверждённые и неподтверждённые свойства.
- **AC13 — Knowledge continuity.** Каждый значимый insight, confirmation/refutation/qualification гипотезы, decision, counterexample, failure и limitation из E04 имеет `K-E04-NNN` в базе знаний с evidence/provenance; стабильные выводы отражены в канонических `01–08`; `knowledge-sync.json` не содержит пропусков/битых ссылок; post-EXEC review подтверждает содержательную полноту. Без AC13 checkpoint и E04 не завершены.

Тесты: focused codec/type tests; property-based bounded graph generation; Dafny proof/build; process-level CLI conformance; fault injection; mutation suite; v0 regression. Генератор cases закрепляет seed и сохраняет каждый failing counterexample.

Базовые замеры performance нужны только как engineering limits: verify/build/run duration, artifact size, peak working set если доступен. Они не сравниваются с человекоориентированным языком и не доказывают G06.

Команды для проверки уточняются по созданным project names, ожидаемая форма:

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
pwsh ./tools/Install-Dafny.ps1 -VerifyOnly
dotnet run -c Release --project tests/Kernel.Graph.Conformance -- --suite all
dotnet run -c Release --project src/Kernel.Graph.Cli -- compile --program fixtures/task-graph-v1/program.accepted.json --approval artifacts/e04/contract-approval.json
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true
dotnet run -c Release --project src/Kernel.Conformance -- --all
pwsh ./tools/Check-KnowledgeSync.ps1 -Manifest artifacts/e04/knowledge-sync.json -KnowledgeBase "<private-knowledge-base>/Технологии/Язык для агентов"
git diff --check
```

Stop rules: первый unexplained proof/runtime/oracle mismatch останавливает расширение cases; не увеличивать timeout для сокрытия non-termination; после полного AC evidence и одного clean rerun прекратить optional testing.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC1 | codec/schema/limits negative matrix | inspect error precedence sample | `artifacts/e04/strict-input.json` | — |
| AC2 | pipeline type/dependency/mutation tests | inspect canonical AST/source map | `canonicalization.json` | — |
| AC3 | pinned Dafny verification + bypass negatives | inspect args/contract digest | `proof-receipt.json` | — |
| AC4 | C01–C05 + 10 mutations | inspect counterexamples | `corpus-results.json` | — |
| AC5 | publish/run + randomized differential | inspect R2R publish manifest | `machine-path.json` | — |
| AC6 | fault injection/tamper/timeout | inspect no-receipt directory | `fail-closed.json` | — |
| AC7 | two clean compile/run comparison | binary equality reported | `determinism.json` | — |
| AC8 | boundary/fuel/process tests | inspect termination/peak data | `resource-results.json` | OS hard RAM cap not claimed |
| AC9 | receipt schema/snapshot tests | manual TCB/source audit | `trust-boundary.md` | — |
| AC10 | existing conformance | compare baseline counts | `v0-regression.json` | — |
| AC11 | projection snapshot/digest mismatch | owner reads final projection | `contract-projection.md` | Human semantic judgment cannot be automated |
| AC12 | report assertion/lint | manual claims review | `REPORT.md` | — |
| AC13 | knowledge manifest schema/hash/path/anchor/commit checks | review all E04 evidence/commits for omitted positive and negative knowledge | KB `10 — Журнал знаний E04.md`, canonical KB diffs, `knowledge-sync.json` | Semantic significance requires review in addition to automation |

## 12. Риски и edge cases

- Dafny proof может потребовать lemmas, отсутствующие в первом design. Смягчение: bounded pure model, fixed operations, не ослаблять contract; недоказанность блокирует AC3.
- Lowerer/adapter могут не соответствовать доказанному Dafny. Смягчение: минимальный surface, generated source maps, differential tests, mutation tests, digests; остаются TCB.
- R2R не гарантирует AOT каждого method. Смягчение: точная формулировка P27 evidence и inspection publish manifest; NativeAOT рассматривается отдельно.
- Global cycle rejection строже некоторых продуктов. Это осознанный invariant профиля; расширение потребует новой версии contract.
- Bounds слишком малы для production. Они нужны для первого proof profile; выход за них — явный `LimitExceeded`.
- Полный список инвариантов может не отражать скрытый замысел владельца. Human projection/digest approval остаётся обязательной границей.
- ID namespace может отличаться от реального Unlimotion. E04 проверяет абстрактную модель; product adapter не входит.
- Tool download/source supply chain. Pin version/hash, сохранять URL/digest и не использовать nightly.
- Process timeout/RAM variability. Timeout fail closed; hard OS memory isolation остаётся follow-up.
- Одинаковое runtime behavior не доказывает binary reproducibility. Evidence разделяет semantic и byte results.
- Единственный pipeline почти сводит synthesis к выбору корректной композиции заранее заданных операций. E04 проверяет enforcement, proof и compilation chain, но ещё не доказывает достаточную выразительность языка или ускорение агента; следующий профиль должен добавить ограниченный реальный выбор алгоритма.
- Knowledge capture может превратить KB в свалку или сохранить чувствительные данные. Смягчение: критерии значимости, structured fields, ссылки на evidence вместо raw logs, запрет секретов и promotion только стабильных выводов.
- Автоматический sync не способен определить, что агент умолчал о неудачном результате. Смягчение: post-EXEC review сопоставляет все commits/evidence/failures с knowledge IDs и отдельно ищет отрицательные результаты.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Это опять тесты, а не гарантия» | G04 требует смещения ответственности | Прикладные инварианты доказывает Dafny; tests проверяют TCB/integration и mutations | mitigated |
| «Мы снова отвечаем за backend» | Требование большинства платформ | Генерация кода передана Dafny/.NET; мы отвечаем за lowering/contract. E04 проверяет одну платформу | mitigated |
| «ReadyToRun не полностью native» | R2R содержит IL и допускает JIT | Не заявлять full AOT; доказать наличие native code и фактический `win-x64` run | mitigated |
| «Агент просто сгенерирует неверное доказательство/assume» | Proof bypass критичен | Контракт и imports owner-controlled, raw source запрещён, allowlist + negative bypass tests | mitigated |
| «Я не подтверждал формальную трактовку задачи» | Human approval относится к смыслу contract | До production admission нужен digest-bound projection; эта SPEC утверждает design E04 | mitigated |
| «Профиль слишком частный» | C01–C05 одна задача | Это первый falsifiable profile; G05/G06 и универсальность не заявляются | accepted-risk |
| «При одном pipeline агент почти ничего не программирует» | Каноничность резко сужает выбор | Зафиксировать E04 как проверку механизма ограничения; выразительность и productivity вынести в следующий профиль | accepted-risk |
| «Почему не Rust/Java/Wasm?» | Нужна portability | Dafny→C# быстрее проверяет proof + machine path; другие targets/backends — следующие отдельные профили | accepted-risk |
| «База знаний станет копией логов» | Требование сохранять любые инсайты можно понять слишком широко | Записывать structured knowledge claims и ссылки на evidence; raw logs и routine success исключены | mitigated |
| «Отрицательные результаты снова потеряются» | Финальный отчёт обычно подчёркивает успехи | Refutation/failure обязательны, старые записи не удаляются, checkpoint блокируется AC13 | mitigated |

### Rework Prevention Checklist

- [x] Spec называет CLI, receipt, compiled artifact и human projection.
- [x] Каждый observable scenario связан с AC/evidence.
- [x] Agent decisions перечислены в Decision Ledger.
- [x] Вероятные objections закрыты или отмечены accepted risk.
- [x] Выполнен role-based self-review для design/validation/delivery.
- [x] Acceptance criteria являются проверками результата.
- [x] EXEC имеет путь доказать сценарии до финала.
- [x] Для каждого checkpoint задан обязательный knowledge capture/reconciliation gate.

## 13. План выполнения

1. После approval создать `10 — Журнал знаний E04.md` и пустой versioned `knowledge-sync.json`; зафиксировать правила 6.2.10 до технической реализации.
2. Зафиксировать tool manifest Dafny 4.11.0 и проверяемую локальную установку; добавить additive project skeleton. Перед checkpoint записать найденные ограничения/toolchain insights в KB, выполнить reconciliation, затем сделать отдельные project/KB commits.
3. Реализовать строгую модель/codec/canonicalization и единственный AST pipeline; закрыть AC1/AC2 focused tests; обновить knowledge journal до checkpoint.
4. Реализовать owner contract/support Dafny и deterministic lowerer; добиться universal proof без bypass. Записать proof insights, failures и ограничения; checkpoint только после AC13.
5. Реализовать adapter, receipt и `win-x64` ReadyToRun publish/run; закрыть AC5–AC9 и синхронизировать новые знания.
6. Добавить C01–C05, property/differential и semantic mutation suite; закрыть AC3/AC4/AC6–AC8. Каждый контрпример и изменение статуса гипотезы фиксировать по 6.2.10.
7. Добавить contract/explain projection и digest-bound local approval record, основанный на утверждении этой SPEC; fixture approval использовать только в isolated negative tests; закрыть AC11.
8. Повторно прогнать v0 и весь E04, собрать machine-readable/Markdown evidence, выполнить claims audit и полный knowledge reconciliation по всем commits/evidence.
9. Обновить канонические KB `02/04/05/06/07/08` стабильными выводами, запустить `Check-KnowledgeSync.ps1`, выполнить post-EXEC review и AC13. Только затем сделать итоговые project/KB checkpoint commits и завершить E04.

## 14. Открытые вопросы

Блокирующих вопросов нет. Выбор Dafny 4.11.0, ReadyToRun `win-x64`, bounds, global cycle rule и abstract graph model является частью предлагаемой SPEC и принимается/отклоняется вместе с ней.

Follow-up после E04: нужен ли NativeAOT/Wasm; как подписывать human contract approval; какой каталог частых ошибок считать baseline G03; какой следующий профиль C06–C12; как проводить paired LLM experiment G05 и performance comparison G06.

## 15. Соответствие профилю

- Профиль: `delivery-task` + `product-system-design` + QUEST.
- Выполненные требования профиля: цели/границы, AS-IS/TO-BE, trust boundary, state/data/runtime contracts, observable scenarios, decision ledger, acceptance-to-test matrix, rollout/rollback, security/tool supply chain, evidence/claims boundaries, linter/rubric/role/post-SPEC review.
- SPEC-first gate: до exact approval изменяется только этот spec-файл; код/KB не меняются.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `Kernel.slnx` | Добавить graph projects | Единый build |
| `.gitignore` | Исключить local Dafny/cache/generated binaries | Не коммитить tool/build мусор |
| `src/Kernel.Graph.Core/**` | Model, codec, validation, lowering, toolchain, reference | Собственный язык и verification boundary |
| `src/Kernel.Graph.Cli/**` | contract/compile/run/explain | Пользовательский и process-level интерфейс |
| `verification/task-graph-v1/**` | Fixed contract/support/adapter | Формальная модель и executable backend |
| `tests/Kernel.Graph.Conformance/**` | AC1–AC13 checks | Проверка TCB/toolchain/corpus/knowledge sync |
| `fixtures/task-graph-v1/**` | C01–C05, candidates, isolated approval negatives | Воспроизводимый корпус |
| `tools/dafny.json` | Version/URL/hashes | Supply-chain pinning |
| `tools/Install-Dafny.ps1` | Local verified install | Повторяемая среда |
| `tools/Check-KnowledgeSync.ps1` | Проверить schema/hash/path/anchor/commit для knowledge records | Автоматическая часть AC13 |
| `artifacts/e04/**` | Generated evidence | Auditable result; policy commit/ignore определяется типом |
| `REPORT.md` | Добавить итог E04 после EXEC | Чёткая граница claims |
| `<private-knowledge-base>/Технологии/Язык для агентов/10 — Журнал знаний E04.md` | Все значимые знания с типом, статусом, evidence и provenance | Не терять промежуточные/отрицательные результаты |
| KB `01–08` по правилам 6.2.10 | Продвигать стабильные цели, свойства, решения, evidence, roadmap, sources и terms | Поддерживать каноническую документацию языка |

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| Domain | Reserve/I64/Bool | Additive bounded `TaskGraph` profile |
| Verification | Handwritten SMT obligations | Owner contract + Dafny proof |
| Compilation | Typed graph → SSA interpreter | Typed AST → Dafny → C#/.NET → R2R |
| Machine code evidence | Нет для program | `win-x64` artifact с native R2R code и actual run |
| Contracts | Fixed Reserve host check | Digest-bound formal graph contract + projection |
| Errors | v0 typed stages | Graph-specific deterministic precedence |
| IDs | Stable node/revision IDs | Host-supplied fresh task/criterion bijections |
| Effects | Host SQLite commit | Pure typed patch; storage adapter future |
| Tests | 29 Reserve scenarios | C01–C05 + mutations/properties + v0 regression |
| Claims | v0 mechanism only | Partial G01–G04 evidence; G05/G06 unchanged |
| Knowledge capture | Финальное необязательное обновление KB | Обязательный журнал, promotion и sync gate на каждом checkpoint |

## 18. Альтернативы и компромиссы

**Handwritten SMT map/set encoder.** Плюсы: полный контроль и меньше внешнего языка. Минусы: большой новый TCB, риск расхождения модели/исполнения и много работы до graph proof. Не выбран для E04.

**C# + contracts/property tests без Dafny.** Плюсы: простой runtime/tooling. Минусы: не даёт universal proof прикладных инвариантов G04. Не выбран.

**Rust/Creusot/Prusti.** Плюсы: native ecosystem и сильная memory safety. Минусы: verification toolchain/coverage сложнее для быстрого закрытого graph profile; portability и proof boundary потребуют отдельного исследования. Не выбран для первого шага.

**WebAssembly backend сразу.** Плюсы: переносимость runtime. Минусы: сам по себе Wasm не доказывает контракт; добавляет ABI/serialization backend до проверки центральной гипотезы. Отложен.

**Dafny → Java.** Плюсы: зрелая VM и широкий deployment. Минусы: текущий проект/.NET integration уже готов, а E04 требует минимум одну платформу. Возможен следующий backend conformance.

**NativeAOT вместо ReadyToRun.** Плюсы: более сильное AOT-свидетельство. Минусы: больше ограничений reflection/generics/dependencies и не нужен для первого доказательства пути к нативному коду. Отложен; R2R guarantee формулируется ограниченно.

Выбран Dafny→C#/.NET ReadyToRun: он сокращает собственную ответственность за theorem prover и machine-code backend, оставаясь совместимым с существующим проектом. Цена — явная большая TCB и одна проверенная платформа на E04.

## 19. Результат quality gate и review

### SPEC Linter Result

| Пункт | Проверка | Статус | Свидетельство в SPEC |
| ---: | --- | --- | --- |
| 1 | Цель | PASS | Раздел 1, outcome и stop rules |
| 2 | AS-IS | PASS | Раздел 2 отделяет v0, v0.1 и отсутствующие части |
| 3 | Корневая проблема | PASS | Раздел 3 описывает `почти правильные` graph mutations |
| 4 | Цели дизайна | PASS | Раздел 4 задаёт proof, ownership, determinism и compatibility |
| 5 | Non-Goals | PASS | Раздел 5 ограничивает domain/platform/claims |
| 6 | Ответственность | PASS | 6.1 связывает компоненты и обязанности |
| 7 | Интеграция | PASS | Раздел 8 задаёт команды и границу host |
| 8 | Бизнес-правила | PASS | Раздел 7 задаёт точный алгоритм |
| 9 | Ошибки | PASS | 6.2.5 задаёт closed precedence и no-partial result |
| 10 | Производительность | PASS | Bounds/timeouts и ограниченный benchmark scope заданы |
| 11 | Данные/состояние | PASS | 6.2.3, 6.2.8 и раздел 9 |
| 12 | Миграция | PASS | Additive profile без SQLite migration |
| 13 | Совместимость/rollback | PASS | v0 regression и commit rollback заданы |
| 14 | Acceptance Criteria | PASS | AC1–AC13 проверяют технический результат и непрерывность знаний |
| 15 | Тест-план | PASS | Corpus, mutations, property/differential/fault checks |
| 16 | Команды | PASS | Нормативная форма команд и stop rules приведены |
| 17 | План этапов | PASS | Раздел 13 с dependency/checkpoint gates |
| 18 | Открытые вопросы | PASS | Блокирующих вопросов нет; follow-ups отделены |
| 19 | Масштаб | PASS | Large указан и отражён в design/review depth |
| 20 | Соответствие профилю | PASS | Раздел 15 и role-based review |

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1–5 | PASS | Все разделы 0–20 заполнены; N/A объяснены |
| B. Качество дизайна | 6–10 | PASS | Границы, поток, model/contracts/errors и alternatives конкретны |
| C. Безопасность изменений | 11–13 | PASS | Additive rollout, fail closed, rollback, supply-chain и TCB указаны |
| D. Проверяемость | 14–16 | PASS | 13 AC связаны с tests/evidence; stop rules и knowledge gate заданы |
| E. Готовность к автономной реализации | 17–19 | PASS | File table, этапы, decisions и commands определены |
| F. Соответствие профилю | 20 | PASS | product/system/delivery/QUEST требования отражены |

Итог: **ГОТОВО**.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один bounded profile, ясные non-goals и claims |
| 2. Понимание текущего состояния | 5 | v0/v0.1/corpus и gaps разделены |
| 3. Конкретность целевого дизайна | 5 | AST, domain, errors, proof, R2R и receipt заданы |
| 4. Безопасность (миграция, откат) | 5 | Additive, no storage migration, fail closed, rollback |
| 5. Тестируемость | 5 | AC matrix, mutations, properties, actual run, regression |
| 6. Готовность к автономной реализации | 5 | Files, sequence, gates и residual risks определены |

Итоговый балл: **30 / 30**. Зона: **готово к автономному выполнению после approval**.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Соответствуют ли clone semantics и накопление знаний замыслу? | PASS | Fresh-ID/DAG/framing и обязательный KB journal/promotion зафиксированы |
| UX / designer | applicable для text projection | Понятно ли человеку, что доказано и что он подтверждает? | PASS | Domain/invariants/effects/TCB/digest обязательны в projection |
| Tester / validation | applicable | Есть ли evidence для каждого AC и negative/edge/knowledge coverage? | PASS | C01–C05, mutations, faults, v0 regression и AC13 reconciliation заданы |
| Developer / architect | applicable | Целостны ли boundaries, toolchain, data и knowledge lifecycle? | PASS | Additive profile, fixed lowering и versioned knowledge records заданы |
| Delivery / operations / security | applicable | Учтены ли tool pinning, secrets, artifacts, KB commits и rollback? | PASS | Hash-pinned tool, no secrets/raw logs in KB, separate commits и rollback history заданы |

### Post-SPEC Review

- Статус: **PASS**.
- Scope reviewed: этот spec path, central instruction stack, `product-system-design`, corpus C01–C05, existing project state, planned files.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: SPEC не выдаёт corpus/baseline/web docs за реализованный E04.
  - Contract pass: exact outcome, 14 инвариантов, error precedence и approval digest заданы.
  - Adversarial risk pass: raw source injection, proof bypass, tampering, timeout, partial patch и supply chain рассмотрены.
  - Knowledge continuity pass: значимые positive/negative findings типизированы, routed в KB и блокируют checkpoint при рассинхронизации.
  - Role-Based pass: все применимые роли PASS.
  - Re-review after fixes: формулировка P27 ограничена native R2R evidence; byte reproducibility исключена из gate; global cycle rule и обязательный knowledge lifecycle сделаны явными.
  - Stop decision: optional research остановлен; дизайн достаточно конкретен для approval.
- Evidence inspected: project Git/status, v0/v0.1 specs, KB goal/properties/corpus, central `session-insights-context`/`quest-mode`, Dafny reference/install/errors/releases, Microsoft ReadyToRun guidance.
- Depth checklist:
  - Scope drift / unrelated changes: отсутствует; в SPEC phase меняется только этот файл.
  - Acceptance criteria: каждый AC имеет test/check/evidence.
  - User-observable scenarios / Decision ledger / Expected objections: заполнены.
  - Validation evidence: planned отдельно от historical baseline.
  - Unsupported claims: G05/G06/full AOT/all platforms запрещены.
  - Regression / edge case: v0 regression, cycles/maps/overflow/shared DAG/external edge/faults включены.
  - Comments/docs/changelog: после approval KB обновляется в том же блоке каждого значимого знания и до checkpoint; raw logs/secrets исключены.
  - Hidden contract change: digest-bound approval и separate profile.
  - Manual-review challenge: наиболее вероятные находки — путаница ReadyToRun с полностью AOT и потеря отрицательного знания между checkpoints; обе закрыты точными gates.
- No-findings justification: после corrections не осталось blocker/high/medium findings, требующих выбора помимо утверждения SPEC.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Hard OS RAM cap отсутствует | Не заявлять isolation, измерять/фиксировать peak при доступности | accepted-risk |
| LOW | portability | Проверяется только `win-x64` | Сохранить другие targets как follow-up | accepted-risk |
| LOW | contract | Локальная approval record не является production signature | Связать её с approved SPEC provenance и не использовать как production claim | accepted-risk |
| LOW | knowledge | Автоматический sync не определяет семантическую значимость пропущенного вывода | Сопоставлять все failures/evidence/commits вручную в post-EXEC review | accepted-risk |

- Fixed before continuing: native/AOT claim, deterministic binary boundary, global cycle behavior, contract-approval semantics без второго подтверждения, TCB list, обязательный KB journal/promotion/reconciliation contract.
- Checks rerun: structural section review, AC↔evidence mapping, knowledge lifecycle/review objections, link/path review, claims review; автоматический file lint выполняется после записи.
- Needs human: только exact approval `Спеку подтверждаю`.
- Residual risks / follow-ups: Dafny proof complexity, TCB correctness, OS resource isolation, production signature, semantic completeness of knowledge classification, wider platforms, G03 catalogue, G05/G06 experiments.

### Post-EXEC Review

- Статус: **PASS** для кода, evidence и содержательной полноты KB; механическая сверка manifest фиксируется после commits (artifacts/e04/knowledge-check.json).
- Scope reviewed: новый graph profile, proof/compiled path, CLI, receipt/TCB, corpus/mutations, resources, KB и claims.
- Decision: reviewer подтвердил PASS кода/KB; BLOCKER/HIGH/MEDIUM нет. Итоговое закрытие включает механический gate AC13.
- Review passes: отдельный e04_contract_review, несколько проходов после corrections; это adversarial fallback в writable среде, без технической read-only изоляции.
- Evidence inspected: Contract.dfy, Candidate generator, adapter/output validator, codec/toolchain/CLI, 141 checks, proof/replay receipts, v0 regression, KB K001–019 и канонические документы.
- Depth checklist: scope additive; 14 invariants связаны с named proofs; реальные CLI/run сценарии проверены; исходные отрицательные результаты сохранены; G05/G06/вся TCB/full AOT не заявлены.
- No-findings justification: прежние finding воспроизведены либо локализованы конкретным code review, устранены и покрыты подходящими proof/integration/fault checks; оставшиеся ограничения записаны явно.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | contract | accepted-only и неполные criteria | total ModelOutcome, exact mappings, mutations | fixed |
| HIGH | proof | модель не имела application/sort guarantees | PatchGraphValidity + CanonicalGraph.Wire | fixed |
| HIGH | CLI | explain игнорировал artifact | ValidateAdmission + receipt-bound projection + CLI smoke | fixed |
| MEDIUM | runtime | adapter conversion не проверялась | независимый output validator, corruption negative | fixed |
| MEDIUM | errors/resources | missing artifact codes и child process checks | закрытые ошибки, process-tree fixture | fixed |
| MEDIUM | KB | enum/provenance прежних неудач | допустимые type/status, immutable session excerpts | fixed |

- Checks rerun: final graph conformance 141 PASS; v0 29/29, 10904 assertions; полная admission compilation 74 verified/0 errors; knowledge gate после Git commits.
- Validation evidence: artifacts/e04/REPORT.md, conformance.json, proof-receipt.json, replay-receipt.json, v0-regression.json, development-evidence.json, review.json.
- Unrelated changes: v0 исходники не менялись; generated Windows cache перемещён в ignored work, environment исправлен.
- Needs human: нет; исходное approval покрывает этот профиль, нового эксперимента не начинали.
- Residual risks / follow-ups: TCB, OS isolation, production signing, библиотеки/другие platforms, G05/G06, byte-identical binaries.

## Approval

Получена точная фраза **«Спеку подтверждаю»** 2026-09-06 для commit 8a9342e. Дополнительного approval на неизменённый смысл не требуется.

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | восстановить замысел/границы | 0.99 | нет | связать E04 с G01–G06/P-свойствами | Нет | пользователь ранее поручил продолжать | Использованы KB и выбранный corpus | этот SPEC |
| SPEC | выбрать formal/backend path | 0.88 | фактический archive hash появится в EXEC | pin Dafny 4.11.0, C#/.NET R2R | Нет | Нет | Готовый verifier/compiler снижает объём собственного backend | этот SPEC |
| SPEC | определить graph contract | 0.96 | product adapter вне scope | зафиксировать data/invariants/errors | Нет | Нет | C01–C05 дают bounded falsifiable profile | этот SPEC |
| SPEC | проверить guarantee boundary | 0.94 | hard RAM isolation вне E04 | явно назвать TCB/non-goals/claims | Нет | Нет | Не выдавать proof за correctness всей цепочки | этот SPEC |
| SPEC | quality gate и post-SPEC review | 0.97 | approval | commit spec checkpoint и запросить exact phrase | Да | Ещё нет | SPEC linter PASS, rubric 30/30, review PASS | этот SPEC |
| SPEC | повторное ревью knowledge continuity | 0.99 | approval | проверить AC13, commit revised SPEC и запросить exact phrase | Да | пользователь потребовал обязательную запись знаний | Финальное KB-обновление заменено journal/promotion/sync gate на каждом checkpoint | этот SPEC |

### EXEC: уточнения без изменения намерения, 2026-09-06

Отдельный adversarial review другим агентом выявил пропуски прежнего review. Child имел unrestricted sandbox: это отдельный fallback-review, не технически read-only independent sandbox.

1. Контракт тотален: результат равен ModelOutcome(input); корректный вход обязательно Accepted, отказ только по FirstError. Always-reject и selective-reject входят в proof mutations.
2. Criteria копируются в точном числе и с неизменным payload, fresh mapped ID и completed=false; лишние/потерянные criteria запрещены.
3. Неверный AST отклоняется frontend; harness-only semantic mutations generated implementation проходят настоящий Dafny при неизменном Contract.dfy. Фактические стадии evidence различаются.
4. Status enum: Planned, Active, Completed. repeatRule: null или opaque UTF-8 string <=256 байт. history: <=8 opaque строк <=256 байт каждая. Entity IDs имеют тот же ASCII regex, что шаги; duplicate mappings сохраняются reader до проверки, не схлопываются dictionary.
5. Sources обходятся детерминированно; итоговый wire patch сортируется по fresh ID и remapped endpoints.
6. FirstError: TransportInvalid, SchemaInvalid, LimitExceeded, DuplicateEntityId, DanglingRelation, DuplicateRelation (включает self-edge), SourceRootMissing, ContainmentCycle, TaskIdMapDomainMismatch, CriterionIdMapDomainMismatch, FreshIdInvalid, FreshIdCollision, DateOverflow. Пустой witness допускается при отсутствии достоверного контрпримера; involvedIds каноничны. allowedRepairs: FixEncoding, FixSchema, ReduceInput, RepairGraph, SelectExistingRoot, SupplyExactTaskMap, SupplyExactCriterionMap, SupplyValidFreshIds, SupplyDisjointFreshIds, AdjustDateDelta, RetryToolchain, RecompileArtifact.
7. Knowledge checkpoint — последовательность evidence/source commit → KB commit → manifest commit. projectCommit в manifest означает существующий source/evidence commit, никогда не SHA самого manifest. Новые знания записываются в KB до evidence commit, затем фиксируются KB commit; checkpoint завершается только после reconciliation.
8. Техническая версия центрального baseline стала GPT-6 Astra; compiler behavior от модели не зависит. SDK 10.0.400 подтверждён локально, Dafny 4.11.0 загружен с проверкой официального archive SHA-256.

| Фаза | Блок | Результат | Следующее действие |
| --- | --- | --- | --- |
| EXEC | approval/preflight | Утверждённая версия связана с commit/blob, KB journal создан | Toolchain и formal contract |
| EXEC | adversarial contract review | Невакуозность, criteria completeness, ordering и commit-cycle уточнены | Реализовать и повторно проверить proof mutations |
