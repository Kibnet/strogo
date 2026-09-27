# E10 — живой парный пилот Strogo и C# v0.1

## 0. Метаданные

- Тип (профиль): `delivery-task`, expanded QUEST; профиль `product-system-design`; контексты `testing-dotnet` и `session-insights-context`. Stack profile отсутствует: это versioned CLI/experiment subsystem, а доступный `dotnet-backend-api` относится к ASP.NET/gRPC/background workers.
- Владелец: владелец Strogo. Связанные цели: G01, G04 и подготовка методики G05; косвенная проверка границ G02/G03.
- Масштаб: large. Новый live-model workflow, внешнее исполнение Codex CLI, четыре изолированных запуска, новые evidence-артефакты и несколько проектов/скриптов.
- Целевое семейство / behavior baseline: один exact model ID `gpt-6-astra`, reasoning `medium`, одинаковые параметры обеих arm. Модель присутствует в фактическом `codex debug models` catalog; недоступный в CLI `gpt-6-sol` отклонён на preflight.
- Поверхность: Codex CLI non-interactive `exec` на локальном Windows x64 host.
- Effective runtime: `codex-cli 0.154.0`; перед запуском записываются requested model/reasoning, client version, sandbox и доступный runtime metadata. Если CLI сообщает effective model/backend, несовпадение между arm делает pilot invalid; отсутствие такого поля фиксируется как `NotReportedByRuntime` и запрещает утверждение о доказанно одинаковом backend.
- Eval baseline / evidence: E09 `strogo.e09.offline-calibration.v0.1`, отчёт `docs/evidence/e09-offline-calibration.json`; live pilot создаёт отдельный immutable raw root и canonical summary.
- Целевой релиз / ветка: `main`, отдельная контрольная точка E10.
- Ограничения: один узкий Reserve-domain; пилот методики не подтверждает G05, общую выразительность, стоимость в долларах, переносимость или G06.
- Связанные ссылки: `docs/project-intent.md`, `specs/2026-09-16-e09-offline-equivalence-calibration-v0.1.md`, Unlimotion `3307043a-1bc8-441c-a914-37ad32cb05fc`.

## 1. Overview / Цель

E09 доказал, что два заранее подготовленных представления одного Reserve-контракта можно честно и детерминированно оценить общим evaluator. Он не проверял живого агента, получение кандидата по человеческой спецификации, затраты и вероятность успешной реализации.

E10 создаёт минимальный воспроизводимый live-пилот. Один и тот же model/runtime профиль в четырёх свежих изолированных сессиях решает одну достаточную человеческую спецификацию: два раза на Strogo, два раза на C#. Порядок arm контрбалансирован. Кандидаты получают одинаковый смысл задачи, публичные примеры, лимиты и критерий приёмки; различается только необходимый языковой toolchain и формат итогового артефакта. Скрытый evaluator не передаётся агенту.

Операционное значение «тот же агент» для E10: одинаковые requested model/reasoning, версия Codex CLI, базовый prompt policy, sandbox, resource caps и отсутствие общей истории. Это не один продолжающийся conversation thread: общая история создала бы перенос решения между arm. Prompt/system/toolchain identities и неизбежные arm-specific различия сохраняются в manifests.

Outcome contract:

- Исходное поручение: выполнить Unlimotion-цель, начав с задачи «Провести E10: живой парный пилот Strogo и обычного языка».
- Success means: один запуск orchestration создаёт четыре свежих arm-run, сохраняет provenance/usage/timing, слепо оценивает кандидаты и строит canonical отчёт, из которого можно проверить методику и ограничения.
- Итоговый артефакт: `docs/evidence/e10-live-paired-pilot.json` плюс raw evidence во внешнем локальном каталоге `%LOCALAPPDATA%/Strogo/e10/<pilot-id>/`, который не является repository root или его descendant.
- Stop rules: не запускать live model до exact approval; не повторять скрыто неуспешный run с раскрытием oracle; не объявлять G05 по E10; остановить pilot как `Invalid` при config drift, missing raw evidence, утечке trusted данных или невозможности установить exact toolchain.

## 2. Текущее состояние (AS-IS)

- `Strogo.Experiments` хранит E09 corpus, independent `ReserveOracle`, evaluator, export manifest и negative probes.
- `Strogo.ExperimentCli` умеет `calibrate` и `export` двух offline arm (`graph-json`, `strogo-notation`). Он не вызывает модель и не принимает внешнего кандидата.
- E09 trusted corpus содержит готовые решения; его сборка не может попасть в model-visible input.
- `Strogo.Notation` компилирует agent-oriented source в `KernelProgram`; Core валидирует, lowers и исполняет его.
- Репозиторий требует .NET SDK `10.0.400`. На текущем host системно установлена `10.0.401`, exact `10.0.400` отсутствует. До EXEC это environment blocker для обычной repo-команды, а не дефект Strogo.
- Локально доступен `codex-cli 0.154.0` с `exec --json --ephemeral --ignore-user-config --ignore-rules --sandbox workspace-write`.
- Live-run, C# comparator, arm-isolated package, raw usage capture и E10 report отсутствуют.

## 3. Проблема

Сейчас нельзя отличить предполагаемое преимущество Strogo для агента от эффекта заранее подготовленного решения, разного контекста, разных моделей, утечки oracle, порядка запуска или неполного учёта затрат. Нужен маленький pilot, который сначала проверит саму методику live-сравнения и сохранит достаточное evidence для её критики.

## 4. Цели дизайна

- Равный смысл задания, критерий качества и ресурсные пределы для обеих arm.
- Свежая сессия для каждого run без общей истории и доступа к другой arm.
- Trusted evaluator и hidden vectors вне model-visible input.
- Явная provenance-цепочка prompt → runtime events → candidate → evaluation → report.
- Учёт wall time, model usage, tool calls, попыток, вопросов человеку и наличия/отсутствия денежной цены.
- Детерминированная повторная агрегация одного raw evidence set.
- Typed refusal вместо частичного или оптимистичного отчёта.
- Аддитивная реализация без изменения E09/Core/Host semantics.

## 5. Non-Goals

- Не делать статистический вывод о G05 по четырём run.
- Не объявлять G04 или универсальную корректность кандидата: E10 trusted evaluator использует конечный benchmark corpus.
- Не сравнивать производительность кандидатов и не закрывать G06.
- Не добавлять второй домен, effects, сеть, async, FFI или новые opcodes Strogo.
- Не менять owner Reserve contract, grammar E08 или E09 trusted corpus.
- Не считать subscription usage денежной стоимостью, если runtime не сообщает billable amount.
- Не использовать текущую разговорную сессию как одну из arm: она уже видела исходники и expected logic.
- Не отвечать на вопросы live-agent во время run. Запрос уточнения фиксируется как `NeedsHuman`.
- Не публиковать raw prompts/events; push разрешён только для безопасного canonical summary и реализации.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `src/Strogo.Experiments/LivePilot*.cs`: trusted task definition, hidden vectors, manifests, evaluation, metrics parsing и canonical aggregation. Этот assembly не экспортируется агенту.
- `src/Strogo.ExperimentCli/`: команды `pilot prepare`, `pilot evaluate`, `pilot report`; никаких provider credentials.
- `tools/Run-E10LivePilot.ps1`: orchestration четырёх output-only `codex exec` процессов, timeout/kill, stdout JSONL/stderr/structured response capture и последовательный trusted evaluation.
- `tests/Strogo.Experiments.Conformance/`: manifest, prompt freeze/parity, structured response, leakage, candidate, Docker evaluator, metrics и deterministic-report checks.
- `fixtures/e10-live-pilot/`: публичный human task, exact arm instructions, JSON output schema, C# wrapper/project и четыре публичных примера; без hidden expected candidates.
- `%LOCALAPPDATA%/Strogo/e10/<pilot-id>/`: raw run packages, JSONL, stderr, final messages, candidates, evaluations и environment receipt; путь находится вне repository tree и синхронизируемой базы знаний.
- `docs/evidence/e10-live-paired-pilot.json`: allowlisted canonical summary для репозитория.
- `docs/knowledge-log.md`: выводы и ограничения E10 после фактического run.

### 6.2 Детальный дизайн

#### 6.2.1 Утверждаемая человеческая спецификация

Обе arm получают byte-identical `TASK.md` со следующим смыслом:

1. Вход: `resourceAvailable: Int64`, `requestedQuantity: Int64`.
2. Host отклоняет `resourceAvailable < 0` кодом `StateInvariantFailed` и `requestedQuantity <= 0` кодом `PreconditionFailed` до вызова кандидата.
3. Для допустимого входа операция принимает запрос тогда и только тогда, когда `requestedQuantity <= resourceAvailable`.
4. При принятии: `accepted=true`, `reserved=requestedQuantity`, `available=resourceAvailable-requestedQuantity`.
5. При отказе из-за нехватки: `accepted=false`, `reserved=0`, `available=resourceAvailable`.
6. Кандидат детерминирован, не читает внешнее состояние, не выполняет I/O и не создаёт иных эффектов.
7. Итоговый source должен реализовывать эти правила для всех допустимых Int64 входов. E10 фактически проверяет только зафиксированный public/hidden benchmark corpus и поэтому выдаёт `BenchmarkAccepted`, а не доказательство универсальной эквивалентности.

Четыре public examples включают accepted, insufficient, zero available и `Int64.MaxValue`. Hidden set строится один раз до live-run из фиксированных boundary rows и детерминированной seed-выборки; его digest записывается в trusted manifest, сами строки не экспортируются.

#### 6.2.2 Arm contracts

`strogo-notation`:

- итоговый файл `candidate.strogo`;
- model prompt содержит полную необходимую grammar/allowed operations и intentionally invalid starter как текст; model не получает tool access и возвращает source в structured response;
- trusted evaluator повторно компилирует candidate через E08 → GraphValidator → Lowerer → reference/IR → independent oracle.

`csharp-dotnet`:

- итоговый файл `Candidate.cs` с фиксированным pure API `Candidate.Execute(long resourceAvailable, long requestedQuantity)` для уже допустимых входов;
- model prompt содержит fixed interface contract и intentionally invalid starter как текст; model не получает tool access и возвращает source в structured response;
- trusted build/execute использует `--platform linux/amd64` и исполнимую immutable image reference `mcr.microsoft.com/dotnet/sdk@sha256:1aabdb4843de1c426d3676bf1220bc040e540f82a765320b3eb2c693e8d0a7dd`;
- container запускается с `--network none`, `--read-only`, `--cap-drop ALL`, `--security-opt no-new-privileges`, non-root uid/gid, `--pids-limit`, memory/CPU limits, tmpfs only for build/runtime scratch и единственным read-only mount trusted wrapper + candidate. Docker socket, repository, user profile, secrets и raw evidence не монтируются;
- generated wrapper использует stdin/stdout protocol, timeout, output cap и process/container kill. `Environment.Exit`, infinite loop, child process или иной candidate failure остаются внутри container и становятся typed refusal. Candidate никогда не загружается в процесс evaluator. Public/hidden outcomes сравниваются с тем же `ReserveOracle`.

Внешний host precondition layer одинаков для arm и не засчитывается как созданная кандидатом логика.

#### 6.2.3 Run matrix и isolation

Pilot содержит четыре run: `S1`, `C1`, `C2`, `S2`. Этот контрбалансированный порядок фиксируется в protocol и не меняется после просмотра результатов. Каждый run:

- создаётся из fresh allowlisted package в отдельном directory;
- запускает новый `codex exec`, без `resume`/`fork`, с `--ephemeral`, `--ignore-user-config`, `--ignore-rules`, `--skip-git-repo-check`, `--sandbox read-only`, `--output-schema`, `--json`, `project_doc_max_bytes=0` и `shell_environment_policy.inherit=none`;
- использует exact requested `--model gpt-6-astra` и `model_reasoning_effort=medium`;
- имеет wall timeout 20 минут, output cap 16 MiB и один live session;
- получает всю задачу и arm-инструкцию непосредственно в prompt, а current directory остаётся пустым; не получает repository root, E09 assembly/source, hidden vectors, candidate другой arm или предыдущий final message;
- обязан вернуть ровно один JSON response по versioned schema; любой shell/app/MCP/tool call, file mutation или дополнительный interaction event делает run `InvalidPolicy` до candidate evaluation;
- сохраняет stdout JSONL, stderr, exit code, start/end UTC, monotonic elapsed, prompt/package/candidate digests;
- не получает hidden-evaluator feedback. Hidden refusal завершает run; повтор возможен только как новый заранее объявленный pilot, а не как repair этой выборки.

`pilot prepare` до первого live event материализует и навсегда связывает с `pilotId` exact bytes/digests: shared task, base prompt policy, обе arm instructions, starters, JSON output schema, run order, requested runtime, Docker platform/reference/resolved image ID, time/resource budgets и forbidden-event rules. Содержательный parity review описывает каждое разрешённое различие arm. После первого `turn.started` изменение любого frozen byte требует нового pilot ID.

До запуска orchestration вычисляет inventory/digest package, проверяет отсутствие запрещённых файлов и строк, записывает requested CLI/model/toolchain receipt и доступный effective runtime metadata. Run directory создаётся вне repository tree и не содержит файлов. `codex debug prompt-input` с той же cwd/config сохраняет полный model-visible input inventory; он должен быть одинаковым между arm кроме frozen user prompt, не содержать repository/E09 source или expected solution и иметь отдельный digest. Внешний global instruction/skills inventory, который CLI добавляет несмотря на `project_doc_max_bytes=0`, допускается только как byte-identical shared context и фиксируется как limitation; любой model tool call запрещён, поэтому этот context не даёт разрешённого пути чтения внешних файлов. После каждого run raw evidence запечатывается для агрегации и не перезаписывается.

#### 6.2.4 Prompt и полномочия

Prompt одинаков по структуре: shared task bytes + frozen arm syntax/interface section + exact response contract. Agent не должен вызывать инструменты, читать файлы или исполнять candidate; он возвращает source внутри JSON. Arm name, target filename, syntax/interface и starter неизбежно различаются, входят в manifest и проходят parity review до live-run.

Codex session имеет read-only sandbox и пустую cwd; shell environment для model tools не наследуется. Попытка любого tool/app/MCP call, чтения, сети, записи либо исполнения фиксируется из raw events как `InvalidPolicy`; candidate из такого run не оценивается. Сам Codex client использует сеть только для model provider вне model tool surface.

Versioned output schema содержит `status = Candidate|NeedsHuman|Refused`, `candidateSource` и `question`. Precedence: forbidden tool/event → `InvalidPolicy`; timeout/process error → typed infrastructure/run status; schema error → `InvalidResponse`; `Candidate` требует non-empty source и empty question; `NeedsHuman` требует empty source и non-empty question; `Refused` требует оба поля empty. Комбинация candidate + question недопустима.

#### 6.2.5 Acceptance и evaluator order

Порядок неизменяем:

1. validate run manifest, exact requested-runtime receipt и доступный effective metadata;
2. verify raw JSONL presence/parseability and process outcome;
3. reject any model tool/app/MCP call and validate the single structured response by schema/precedence;
4. verify frozen prompt/runtime/package identities and full model-visible-input digest;
5. validate candidate source UTF-8/text limits and digest;
6. arm adapter parse/compile/build; C# stage executes only in the pinned restricted Docker container;
7. public vectors;
8. hidden boundary vectors;
9. deterministic seeded vectors;
10. independent oracle comparison;
11. emit typed evaluation and seal digest.

Ошибка любого раннего этапа не позволяет исполнять более поздний candidate stage. Unknown exception становится `InfrastructureFailure`, не ошибкой языка.

#### 6.2.6 Метрики и стоимость

Для каждого run фиксируются:

- `wallMilliseconds`, `agentProcessMilliseconds`, `evaluationMilliseconds`;
- reported input/output/cached/reasoning tokens, если они присутствуют в JSONL;
- количество model turns и forbidden tool/app/MCP events; accepted run требует `toolCalls=0`, `candidateWrites=0`, `publicValidatorCalls=0`;
- process exit, final-message digest, candidate status и typed refusal;
- `attemptCount=1`; output-only session возвращает candidate один раз, а tool-based self-correction в E10 отсутствует и фиксируется как ограничение внешней валидности;
- `humanActiveSeconds=0` и `humanWaitSeconds=0`, пока вопроса человеку нет; при вопросе status `NeedsHuman` и значения не подменяются;
- `billableCost` и `currency` только из authoritative runtime/billing event. Если CLI не сообщает цену, `billableCost=null`, `costAvailability=NotReportedBySubscriptionRuntime`; денежное сравнение запрещено.

Summary показывает каждую выборку, медиану только как описательную статистику и paired deltas. Он обязан содержать `claimBoundary="PilotOnlyNoG05"` и `effectiveModelEvidence=Reported|NotReportedByRuntime`.

Для каждой arm отдельно сохраняется `assuranceLevel`. В E10 допустим только `FiniteCorpusEvaluation`; наличие compiler/verifier stages не повышается до `ContractProven`, пока отдельное proof obligation не покрывает точный live candidate и весь входной домен.

#### 6.2.7 Output/evidence contract

Raw evidence хранит точные process events и может содержать локальные пути, поэтому не коммитится. Root создаётся вне синхронизируемых каталогов с отключённым inheritance и ACL только для текущего SID и `SYSTEM`; child process получает минимальный environment. До summary выполняется secret/private-path scan: finding переводит raw set в локальный quarantine и report status `EvidenceQuarantined`, не публикуя matched value. Raw root имеет записанную retention date 90 дней; удаление выполняется только отдельной проверяемой cleanup-командой после сохранения canonical summary и knowledge entry, без фоновой automation.

Canonical summary содержит protocol/version, commit/toolchain/runtime identities, run order, arm metrics, evaluation stages, digests, validity, limitations и report digest. В него не попадают auth/config contents, физические home paths, hidden vectors, complete expected source или model chain-of-thought.

Visual planning artifact: не применимо; workflow CLI/JSON без UI.

UI test video evidence: не применимо; UI не меняется.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Подготовка | Запустить `pilot prepare` | Заморожены 4 run manifests, exact prompts/schema/order/runtime/Docker identities | manifests, parity review, digests | AC1, AC2, AC6 |
| Live pilot | Запустить `Run-E10LivePilot.ps1` | Четыре fresh output-only Codex run выполняются в фиксированном порядке и завершаются typed status | raw JSONL/stderr/structured responses | AC3, AC4, AC5 |
| Проверка результата | Запустить `pilot report` | Получен canonical JSON с per-run evidence, paired deltas и `PilotOnlyNoG05` | summary + digest | AC5, AC6, AC9 |
| Нарушение provenance | Изменить model/package/candidate manifest | Evaluator отказывает до скрытого исполнения | negative conformance | AC7, AC10 |
| Недоступен exact SDK | Запустить preflight без 10.0.400 | Pilot не стартует и сообщает `ToolchainUnavailable` | preflight receipt | AC8 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Absent | `prepare` | `Prepared` | existing destination → refuse, no overwrite | immutable pilot ID |
| Prepared | `run` | `Running` then four terminal run states | frozen-byte/config drift → whole pilot `Invalid` | runs sequential |
| Running, no `turn.started` | process/bootstrap failure | `PreExposureInfrastructureFailure` | after cause fixed, replacement process with new process-attempt ID is allowed in same slot | no model exposure |
| Running, after `turn.started` | timeout/process exit | terminal typed run | kill process tree; retain partial raw evidence | no retry in this pilot |
| Runs terminal | `evaluate/report` | `Accepted`, `Refused`, `NeedsHuman` or `Invalid` | missing evidence → `Invalid` | no optimistic partial PASS |
| Reported | repeated `report` | byte-identical summary | raw mutation → digest refusal | deterministic aggregation |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Comparator | agent within approved goal | C#/.NET 10: common mature human-oriented baseline on same platform | 0.94 | another comparator may differ | Нет |
| Model | agent | requested `gpt-6-astra`, medium; exact same available catalog profile in 4 runs | 0.95 | effective backend may be unreported or drift | Нет; mismatch refuses, absence limits claim |
| Replicates/order | agent | 2 per arm, `S1,C1,C2,S2` | 0.88 | too small for inference | Нет; boundary is explicit |
| Cost | agent | authoritative runtime cost only, otherwise null | 0.99 | no dollar comparison | Нет |
| Human questions | agent | do not answer inside run | 0.97 | one run may become NeedsHuman | Нет |
| SDK/runtime isolation | agent + existing owner contract | repo build uses verified Windows SDK archive; candidate uses pinned Linux/amd64 Docker digest | 0.99 | artifacts absent locally before EXEC | Нет; downloads remain inside EXEC scope after approval |
| Model tools | agent | output-only response; any tool/app/MCP event invalid | 0.97 | pilot no longer measures iterative tool use | Нет; explicit E10 limitation |
| Live external usage | user via exact SPEC approval | four Codex CLI calls | 0.98 | consumes account usage | Да, через approval всей SPEC |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| .NET SDK for repo build | `global.json` 10.0.400 | explicit repo-local absolute `dotnet.exe` if installed, otherwise pinned SDK container command; bare `dotnet` is forbidden in E10 evidence | no `global.json` edit | executable/image path, version and digest receipt |
| C# candidate runtime | none | pinned Linux/amd64 SDK image with restricted `docker run` contract | additive, no host execution | image digest + malicious-candidate probes |
| Model runtime | Codex CLI invocation + JSONL | fixed model/reasoning/sandbox receipt | no persisted session | manifest/event cross-check |
| Raw evidence | none | ACL-protected local root, secret scan/quarantine, 90-day retention | new gitignored/non-synced path | ACL/scan/inventory/digest receipts |
| Canonical evidence | E09 JSON | new E10 JSON, no overwrite E09 | additive | schema/round-trip/determinism |
| Candidate packages | E09 job-starter | arm-specific E10 packages | new protocol | leakage/allowlist probes |

## 7. Бизнес-правила / Алгоритмы

### 7.1 Pilot validity

`PilotValid = all requested runtime identities equal ∧ all reported effective identities equal ∧ all package/task digests expected ∧ all raw streams present ∧ no policy violation ∧ report reproducible`.

Если effective identity не сообщён ни в одном run, pilot может быть valid только относительно одинаковой requested configuration и обязан сохранить limitation `EffectiveBackendNotProven`. Частично сообщённый metadata либо mismatch делает pilot invalid.

`CandidateAccepted` не делает `PilotValid` автоматически; `PilotValid` не означает G05.

`BenchmarkAccepted` означает совпадение с oracle на зафиксированном конечном corpus. Он не означает корректность на всех Int64, G04 или отсутствие необходимости обычного review для C#.

### 7.2 Pairing

- Pair 1: `S1` ↔ `C1`.
- Pair 2: `S2` ↔ `C2`.
- Порядок исполнения `S1,C1,C2,S2` контрбалансирует первый arm между парами.
- Если хотя бы один run пары `Invalid`, delta этой пары не вычисляется.
- `Refused` является результатом агента/кандидата и остаётся в пилоте; `InfrastructureFailure` отделяется.

### 7.3 Claims

- Разрешено: «в этой конкретной выборке arm X завершилась так-то, с такими измеренными метриками и прошла/не прошла конечный benchmark corpus».
- Запрещено: «Strogo быстрее/дешевле вообще», «G05 достигнута», «тесты больше не нужны», «доказано большинство ошибок».

## 8. Точки интеграции и триггеры

- `Strogo.ExperimentCli pilot prepare` материализует trusted manifest и job packages.
- `Run-E10LivePilot.ps1` вызывает Codex CLI только после успешного preflight.
- После каждого live process вызывается `pilot evaluate` для его candidate; hidden result не передаётся следующему run.
- После четырёх терминальных run вызывается `pilot report`.
- Conformance вызывает prepare/evaluate/report на scripted fake event streams и fixtures, без live model.

## 9. Изменения модели данных / состояния

Новые versioned records: `PilotProtocol`, `PilotManifest`, `RunManifest`, `RuntimeReceipt`, `RunMetrics`, `CandidateEvaluation`, `PilotReport`. Все canonical JSON fields строго allowlisted; unknown/missing fields refuse. Raw events остаются opaque evidence с отдельным digest и не встраиваются в summary.

## 10. Миграция / Rollout / Rollback

- Миграции существующих данных нет.
- Реализация аддитивна; E09 команды/JSON остаются byte-compatible.
- Для C# candidate используется `--platform linux/amd64` и exact immutable image `mcr.microsoft.com/dotnet/sdk@sha256:1aabdb4843de1c426d3676bf1220bc040e540f82a765320b3eb2c693e8d0a7dd`; pull выполняется после approval, системный SDK не меняется. `RuntimeReceipt` фиксирует requested platform/reference, resolved image ID и digest до исполнения candidate.
- Repo build использует `.tools/dotnet-sdk-10.0.400/dotnet.exe`, распакованный из official `https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.400/dotnet-sdk-10.0.400-win-x64.zip` только после SHA-512 `9b8b88590e4da131bfd0da7aa089d0fc04d5418d5f8607ec13d55dc5a17b4399afd54d496c12657fa05c6c6546dc5eab930f26ac6c50f2d3a7712c0fb378c366`; source of truth — Microsoft `.NET 10` release metadata. Bare `dotnet` в E10 validation запрещён.
- Rollback: удалить новые source/test/fixture/tool files и E10 evidence; E09/Core/Host остаются без semantic diff. Raw local pilot сохраняется отдельно до решения владельца, чтобы отрицательный результат не потерялся.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- AC1: prepare создаёт ровно четыре frozen run manifests `S1,C1,C2,S2`, общий task digest и фиксированный order; exact prompt/schema/starters/budgets и Docker platform/reference/resolved image ID связаны с pilot ID, повтор в непустой destination отказывает без overwrite.
- AC2: model/reasoning/CLI/read-only sandbox/timeout/output caps/shared task равны; arm-specific bytes заморожены, ограничены allowlist и прошли содержательный parity review до live result.
- AC3: каждый run имеет fresh ephemeral output-only invocation, пустую cwd и полные raw receipts; resume/fork/shared workspace запрещены; accepted run содержит zero tool/app/MCP events.
- AC4: обе arm получают `BenchmarkAccepted` только после одного independent Reserve oracle через public, boundary и deterministic hidden vectors; typed preconditions одинаковы, assurance остаётся `FiniteCorpusEvaluation`.
- AC5: report содержит wall/model/evaluation time, usage fields, tool/candidate-write counts, attempt/human/cost availability и terminal status каждого run.
- AC6: повторная агрегация неизменённого raw root даёт byte-identical report и digest.
- AC7: missing/extra manifest field, config drift, forbidden event, invalid status combination, candidate outside limits, run reuse, raw event mutation, solution/oracle leakage, missing candidate и evaluator exception дают точные refusals.
- AC8: exact repo build executable and pinned candidate Docker `linux/amd64` reference pull/inspect preflight green; resolved image ID/digest совпадает с frozen receipt; locked restore, Release solution build, E09, E08, E07 и full Core/Host/CLI regressions green.
- AC9: фактический live pilot выполнен; canonical evidence и knowledge entry честно фиксируют результаты/ограничения с `PilotOnlyNoG05`.
- AC10: model-visible input не содержит E09 trusted assembly/source, hidden vectors, expected candidate bytes/digest или complete solution; prompt-input snapshot, forbidden-content scan and collision probes green.
- AC11: malicious C# candidates for filesystem/network/process/reflection/static initializer/`unchecked`/infinite loop/exit запускаются с frozen `linux/amd64` image receipt, остаются внутри restricted container и дают typed refusal либо contained result без host effect.
- AC12: raw evidence ACL, minimal environment, secret scan/quarantine and retention receipt pass before canonical summary.
- AC13: текущая задача Unlimotion получает ссылку/summary результата и завершается только после post-EXEC PASS и read-back.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC1 | prepare/count/freeze/no-overwrite tests | inspect pilot identity inputs | conformance report | — |
| AC2 | manifest equality/allowlist diff | parity review exact prompt bytes | runtime receipts | — |
| AC3 | invocation parser, empty cwd, zero-tool enforcement | inspect raw command/events | raw run roots | — |
| AC4 | scripted valid/wrong candidates both arm | inspect oracle separation | evaluations | — |
| AC5 | metrics parser fixtures incl missing usage | inspect summary | canonical report | — |
| AC6 | double report byte comparison | SHA-256 inventory | report digest | — |
| AC7 | manifest/status/event mutation suite | verify early-stage refusal | conformance log | — |
| AC8 | locked restore/build and existing suites | exact executable/image receipt | command logs | — |
| AC9 | schema/boundary assertions | inspect actual four-run result | evidence + KB | — |
| AC10 | prompt-input scanner/collision probes | inspect model-visible inventory | inventories/digests | — |
| AC11 | malicious C# container suite | inspect no host changes/socket/network mounts | container receipts | — |
| AC12 | ACL/secret-scan/quarantine tests | inspect retention receipt | raw evidence receipt | — |
| AC13 | Unlimotion CLI read-back | task status/summary | task history | — |

### Обязательные команды

Перед длинными проверками точный SDK root и команды фиксируются в action journal. Нормативный порядок:

```powershell
$e10Dotnet = (Resolve-Path '.tools/dotnet-sdk-10.0.400/dotnet.exe').Path
$e10Image = 'mcr.microsoft.com/dotnet/sdk@sha256:1aabdb4843de1c426d3676bf1220bc040e540f82a765320b3eb2c693e8d0a7dd'
& $e10Dotnet --version
docker pull --platform linux/amd64 $e10Image
docker image inspect $e10Image
& $e10Dotnet restore Kernel.slnx --locked-mode
& $e10Dotnet build Kernel.slnx -c Release --no-restore
& $e10Dotnet run --project tests/Strogo.Experiments.Conformance/Strogo.Experiments.Conformance.csproj -c Release --no-build
& $e10Dotnet run --project tests/Strogo.Notation.Conformance/Strogo.Notation.Conformance.csproj -c Release --no-build
& $e10Dotnet run --project tests/Kernel.Conformance/Kernel.Conformance.csproj -c Release --no-build -- --suite e07
& $e10Dotnet run --project tests/Kernel.Conformance/Kernel.Conformance.csproj -c Release --no-build
codex debug models
codex debug prompt-input -c project_doc_max_bytes=0 E10_PROMPT_PREFLIGHT
$e10Raw = Join-Path $env:LOCALAPPDATA 'Strogo/e10/<pilot-id>'
pwsh -NoProfile -File tools/Run-E10LivePilot.ps1 -OutputDirectory $e10Raw
& $e10Dotnet run --project src/Strogo.ExperimentCli/Strogo.ExperimentCli.csproj -c Release --no-build -- pilot report --directory $e10Raw --output docs/evidence/e10-live-paired-pilot.json
git diff --check
```

Stop rules: failure до первого `turn.started` можно повторить только после устранения инфраструктурной причины с новым process-attempt ID; после exposure timeout/run не повторять; после live result не менять prompts/hidden set/thresholds; после green mandatory set не запускать дополнительные model calls без нового риска.

## 12. Риски и edge cases

- Модель/CLI может не сообщить token usage или price: missing value остаётся явным, cost claim запрещён.
- Subscription/provider может изменить backend при том же requested model: reported mismatch/частичное присутствие metadata делает pilot invalid; полное отсутствие поля сохраняется как `EffectiveBackendNotProven`, поэтому общий вывод о «том же фактическом backend» запрещён.
- Model session остаётся в host Codex CLI и получает shared global instruction/skills inventory; полный prompt-input snapshot доказывает его содержимое, output-only/zero-tool contract исключает разрешённое исполнение команд, но E10 не заявляет production-grade model isolation.
- Docker ограничивает исполнение C# candidate, но не доказывает отсутствие уязвимости самого Docker Engine/image; exact digest, no socket/mounts/network/capabilities и malicious suite ограничивают trust boundary.
- C# toolchain выразительнее и знакомее модели, а Strogo tool docs новы: это часть исследуемого tradeoff, но четыре run не дают общего вывода.
- Public validator способен направлять агента; его вызовы считаются, а hidden feedback отсутствует.
- Same model не означает deterministic response. Две выборки на arm показывают вариативность, не оценивают распределение.
- Заранее известный Reserve-домен может присутствовать в pretraining: обе arm получают тот же смысл, но novelty не доказана.
- Конечный hidden corpus может пропустить дефект C# или Strogo candidate; E10 оценивает методику и конкретные sample outcomes, а не доказывает live candidate на всём домене.
- Raw JSONL формат CLI может измениться: parser unknown fields сохраняет, обязательные metrics missing → explicit unavailable/invalid according to field criticality.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Четырёх запусков мало | Нельзя доказать G05 | E10 объявлен calibration pilot; основной G05 остаётся отдельной задачей | mitigated |
| Сравнение нечестно из-за разных инструментов | Языкам нужны разные toolchains | общий task/quality/resources; различия allowlisted и полностью описаны | accepted-risk |
| Стоимость не измерена в деньгах | Codex subscription может не выдавать цену | authoritative field or null; tokens/time preserved; no invented USD | mitigated |
| Агент мог увидеть готовый ответ | E09 assembly содержит solutions | output-only empty cwd, frozen prompt-input inventory, zero-tool gate and fresh sessions | mitigated |
| Мы сравнили только игрушечный Reserve | Узкий домен | claim boundary; второй домен и основной G05 — следующие задачи | accepted-risk |
| One-shot не измеряет цикл tool feedback | Без инструментов лучше isolation, но хуже внешняя валидность для agentic workflow | E10 называется output-only pilot; отдельный tool-mediated pilot нужен до основного G05 | accepted-risk |

### Rework Prevention Checklist

- Пользователь видит canonical pilot report и конкретный запуск: да.
- Каждый observable scenario связан с evidence: да.
- Assumptions и user-owned live usage указаны: да.
- Вероятные возражения и границы выводов указаны: да.
- Role-based review предусмотрен: да.
- AC являются проверками результата: да.
- EXEC может доказать сценарии без раскрытия hidden oracle агенту: да.

## 13. План выполнения

1. Реализовать versioned manifests, output schema, task/hidden corpus и trusted evaluator; сначала scripted regression checks.
2. Реализовать frozen prompt generation/parity review и structured-response/zero-tool event parser.
3. Реализовать restricted Docker C# wrapper и malicious candidate suite.
4. Реализовать prepare/evaluate/report, raw ACL/secret-scan/retention и negative provenance suite.
5. Реализовать PowerShell orchestration и scripted fake-run checks без модели.
6. Установить/проверить exact repo-local SDK 10.0.400 и pinned Docker image, выполнить targeted и regression validation.
7. Провести post-implementation contract/adversarial review до live cost.
8. Выполнить ровно четыре live runs в фиксированном порядке.
9. Сформировать canonical evidence, записать знания, выполнить post-EXEC review и зафиксировать checkpoint commit.
10. Обновить Unlimotion E10 summary/status через CLI и выполнить read-back.

## 14. Открытые вопросы

Нет блокирующих вопросов. Отсутствие authoritative monetary price является допустимым измеренным ограничением пилота, а не поводом подставлять оценочную цену.

## 15. Соответствие профилю

- Профиль: expanded QUEST, `product-system-design`, `testing-dotnet`, `session-insights-context`.
- Выполненные требования: outcome-first contract; AS-IS; component boundaries; runtime/data/evidence contracts; hidden trust boundary; staged validation; rollback; user-observable scenarios; decision ledger; acceptance mapping; objections; knowledge preservation.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Strogo.Experiments/LivePilot*.cs` | trusted protocol/evaluator/report | E10 core |
| `src/Strogo.ExperimentCli/Program.cs` | pilot commands | operator workflow |
| `tests/Strogo.Experiments.Conformance/**` | E10 positive/negative checks | contract evidence |
| `fixtures/e10-live-pilot/**` | task/prompts/starters/output schema/C# wrapper | stable frozen input |
| `tools/Run-E10LivePilot.ps1` | four-run orchestration | live execution |
| `Kernel.slnx` | add public tool project | build integration |
| `.gitignore` | local raw E10 evidence if needed | no raw publication |
| `docs/evidence/e10-live-paired-pilot.json` | canonical result | auditable pilot evidence |
| `docs/knowledge-log.md` | findings/limitations | mandatory knowledge capture |
| `README.md` | only if needed for stable E10 command | discoverability after execution |

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| Agent input | scripted fixtures only | approved task in output-only fresh model sessions |
| Comparator | graph JSON vs notation | Strogo notation vs C#/.NET |
| Model | отсутствует | fixed live model/runtime, four fresh sessions |
| Metrics | deterministic evaluator counts | timing/usage/tool/human/cost-availability + acceptance |
| Evidence | E09 offline JSON | raw local E10 chain + safe canonical summary |
| Claim | representation calibration | methodology pilot, explicitly no G05 |

## 18. Альтернативы и компромиссы

- Текущая conversational session: дешевле, но contaminated E09 source/context; отклонено.
- Один run на arm: дешевле, но order полностью смешан с arm; отклонено.
- Десятки run: статистически лучше, но преждевременно тратит usage до проверки методики; отложено.
- OpenAI API с точной долларовой ценой: лучше для cost accounting, но требует отдельного credential/billing surface; E10 использует доступный Codex CLI и честно фиксирует отсутствие цены.
- Dafny comparator: сильнее обычного C#, но не отвечает первому пилотному сравнению с человекоориентированным mainstream workflow; сохранить для основного G05.
- Tool-enabled workspace: ближе к будущему agentic workflow, но текущая host read boundary не исключает repo/oracle и generated C# side effects; отложено до отдельной доказуемой mediation/isolation SPEC.
- Отдать агенту весь repo: проще tool access, но раскрывает E09 решения/oracle; отклонено.

## 19. Результат quality gate и review

### SPEC Linter Result

| № | Блок | Статус | Проверяемое основание |
| ---: | --- | --- | --- |
| 1 | A: цель/outcome | PASS | четыре live run и canonical report определены в §1 |
| 2 | A: AS-IS | PASS | E09, CLI, SDK и live gaps проверены в §2 |
| 3 | A: корневая проблема | PASS | confounds и отсутствие live evidence сформулированы в §3 |
| 4 | A: цели дизайна | PASS | fairness, isolation, provenance и metrics перечислены в §4 |
| 5 | A: границы | PASS | no-G05/no-G06/no-new-semantics и live gate в §5 |
| 6 | B: ответственности | PASS | trusted/public/orchestrator/evidence компоненты разделены в §6.1 |
| 7 | B: интеграции | PASS | prepare/run/evaluate/report triggers в §8 |
| 8 | B: алгоритмы/инварианты | PASS | evaluator order, validity и pairing в §6.2.5/§7 |
| 9 | B: ошибки/recovery | PASS | typed refusal, invalid, timeout/kill/no-retry в §6/§10/§11 |
| 10 | B: performance | PASS | harness имеет 20-minute/output caps; program performance явно G06 non-goal |
| 11 | C: данные/state | PASS | versioned records, raw/canonical separation в §9 |
| 12 | C: compatibility/migration | PASS | additive E09-compatible change, no data migration в §10 |
| 13 | C: rollback | PASS | source/evidence rollback and raw retention в §10 |
| 14 | D: измеримые AC | PASS | AC1–AC13 имеют terminal observable result |
| 15 | D: AC→evidence | PASS | full matrix including negative cases in §11 |
| 16 | D: commands/stop | PASS | exact command order and live/model stop rules in §11 |
| 17 | E: план/dependencies | PASS | nine ordered phases in §13 |
| 18 | E: decisions/questions | PASS | Decision Ledger заполнен, blockers absent in §14 |
| 19 | E: масштаб/форма | PASS | large expanded rationale in metadata |
| 20 | F: profile | PASS | product-system public API/config/security/evidence boundaries in §§6–12 |

Итог: **ГОТОВО к owner approval**.

### SPEC Rubric Result

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Ясность цели и границ | 5 | pilot-only/no-G05 explicit |
| Понимание текущего состояния | 5 | E09/live gaps and SDK blocker recorded |
| Конкретность дизайна | 5 | four runs, packages, evaluator, schemas and metrics fixed |
| Безопасность/откат | 5 | isolation, leakage refusal, no overwrite, additive rollback |
| Тестируемость | 5 | positive/negative/integrity/regression/live evidence |
| Готовность реализации | 5 | no unresolved implementation choice |

Итоговый балл: **30 / 30**. Зона: готово к автономному выполнению после exact approval.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Один ли смысл и критерий результата у arm? | PASS | Нет |
| UX / designer | not applicable | CLI/JSON без UI | PASS | Visual artifact не нужен |
| Tester / validation | applicable | Есть ли AC/evidence и negative surfaces? | PASS | Нет |
| Developer / architect | applicable | Разделены ли public package/trusted evaluator/runtime? | PASS | Нет |
| Delivery / operations / security | applicable | Контролируются ли live usage, raw data, SDK, sandbox и rollback? | PASS | Нет |

### Post-SPEC Review

- Статус / stop decision: **PASS; остановиться на owner approval перед EXEC**.
- Scope reviewed: эта SPEC, project intent, E09 SPEC/evaluator/CLI, repo instructions, central expanded QUEST stack, текущие Git/SDK/Codex CLI/Unlimotion states.
- Contract pass: **PASS** после связывания exact inputs/runtime identities, конечной assurance boundary и AC1–AC13.
- Adversarial risk pass: **PASS** после исправлений leakage, generated-code isolation, config drift, false cost, order effects, hidden retry, status precedence, evidence handling и Docker identity.
- Role-Based pass: **PASS**, таблица выше.
- Independent-review boundary: назначенный reviewer фактически получил unrestricted sandbox. Его вывод использован только как adversarial fallback, не как технически read-only independent verdict. Доступного действительно read-only reviewer sandbox не было; это остаётся процессным residual risk, но не скрытым доказательством независимости.

| Severity | Finding | Исправление в SPEC | Состояние |
| --- | --- | --- | --- |
| HIGH | Generated C# мог повлиять на host/evaluator | Output-only model contract; candidate выполняется только в restricted Docker без network/socket/repo/profile/secrets mounts и без capabilities | CLOSED |
| HIGH | Модель могла прочитать repo, E09 solution/oracle или соседние run | Fresh empty cwd, zero-tool policy, `project_doc_max_bytes=0`, prompt-input inventory/digest и forbidden-content probes | CLOSED |
| HIGH | Bare `dotnet` не гарантировал SDK 10.0.400 | Repo-local exact executable, official archive SHA-512, locked restore; bare `dotnet` запрещён | CLOSED |
| HIGH | Arm могли различаться помимо языка | Frozen exact bytes, allowlisted arm diff и содержательный parity review до первого live event | CLOSED |
| HIGH | `NeedsHuman`, `Refused` и invalid combinations были неоднозначны | Versioned strict output schema и детерминированный priority/status contract | CLOSED |
| MEDIUM | Raw live evidence могло утечь или незаметно измениться | Non-synced root, SID+SYSTEM ACL, minimal environment, secret/private-path scan, quarantine и 90-day retention receipt | CLOSED |
| MEDIUM | Retry после частичного model exposure создавал hidden extra attempt | Retry разрешён только до первого `turn.started`; после exposure нужен новый pilot ID | CLOSED |
| HIGH | Tag plus child digest не был исполнимой Docker reference | Exact `repository@linux/amd64-child-digest`, explicit `--platform linux/amd64`, pull/inspect и resolved image ID receipt | CLOSED |
| MEDIUM | Конечный corpus мог быть выдан за доказательство для всех Int64 | Terminal result называется `BenchmarkAccepted`, assurance — `FiniteCorpusEvaluation`; universal/G04/G05 claims запрещены | CLOSED |
| MEDIUM | Выбран лишний backend profile | Stack profile ограничен `product-system-design`; `testing-dotnet` и `session-insights-context` оставлены как применимые context | CLOSED |

- Open findings: отсутствуют.
- Residual risks: четыре run недостаточны для статистического вывода; effective backend может не сообщаться; зрелость Strogo и C# различается; Docker Engine/image остаются частью trusted base; fallback review не имел read-only enforcement.
- Manual-review challenge для EXEC: по frozen prompt-input inventory доказать отсутствие E09 solution/oracle, затем прогнать malicious C# matrix и подтвердить no-host-effect по container receipt с exact `linux/amd64` child digest/resolved image ID.
- Post-EXEC Review: не выполнен; EXEC запрещён до exact approval.

## Approval

Ожидается фраза: **«Спеку подтверждаю»** после завершения post-SPEC review этой версии.

## 20. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток работы | Следующее действие | Фактическое решение человека | Затронутые артефакты |
| --- | --- | --- | --- | --- | --- |
| RESEARCH | E10 выбран первой исполнимой задачей цели; Unlimotion lease взят через CLI | task `3307043a-...`, status `InProgress`, lease `641c18ee-...` | Зафиксировать и проверить SPEC | Пользователь поручил выполнить цель | Unlimotion task |
| RESEARCH | Fresh live sessions обязательны: текущая сессия contaminated E09 исходниками | E09 source/CLI read; `codex-cli 0.154.0` available | Спроектировать isolated packages | Не требовалось | this SPEC |
| RESEARCH | Системный exact SDK 10.0.400 отсутствует | `dotnet --version` из repo → compatible SDK not found; 10.0.401 installed | В EXEC поставить verified repo-local exact SDK | Не требовалось | this SPEC |
| SPEC | Выбран four-run counterbalanced C# comparator и honest missing-cost contract | Decision Ledger, AC1–AC13 | Провести full post-SPEC review | Ожидается | this SPEC |
| SPEC-REVIEW | Independent-reviewer role работал в фактическом unrestricted sandbox, поэтому его verdict учтён только как adversarial fallback, не как технически read-only independent review | Найдены isolation, SDK command, parity, status schema, raw evidence и retry findings; все внесены в актуальную SPEC | Повторить self/fallback review по исправленной версии | Не требовалось | this SPEC |
| SPEC-REVIEW | Проверен фактический model catalog Codex CLI; `gpt-6-sol` отсутствует, `gpt-6-astra` поддерживает `medium` | `codex debug models`; requested model заменён до live run | Связать catalog digest с pilot manifest | Не требовалось | this SPEC |
| SPEC-REVIEW | Устранена последняя executable-identity ошибка: tag plus child digest заменён на repository@digest с explicit `linux/amd64` | Manifest-list `sha256:4beef...`; amd64 child `sha256:1aab...`; normative pull/inspect and runtime receipt | Запросить exact owner approval | Не требовалось | this SPEC |
| SPEC-REVIEW | Full contract/adversarial/role review завершён; все findings закрыты, residual risks явно ограничивают claims | Post-SPEC Review PASS; linter 20/20; rubric 30/30 | Остановиться до фразы «Спеку подтверждаю» | Ожидается | this SPEC |
| APPROVAL | Пользователь точной фразой подтвердил переход в EXEC | Ответ `Спеку подтверждаю` сохранён в Unlimotion execution question `66006e20-977c-4437-b9ed-fcb9d97b710c` | Реализовать и проверить pre-live контур | `Спеку подтверждаю` | this SPEC; Unlimotion execution |
| EXEC | Реализованы frozen packages, four-run CLI/orchestrator, strict event/response parsing, Strogo/C# evaluators, restricted Docker и canonical report | Targeted E10/E09 conformance green; Release build 0 warnings/errors | Закрыть adversarial pre-live findings и сделать чистый checkpoint | Подтверждено ранее | `LivePilot*.cs`; fixtures; tests; PowerShell |
| EXEC-REVIEW | Adversarial fallback нашёл ложную provenance к старому commit, fail-open event parsing, слабую prompt normalization, overwrite и неполный raw inventory | Findings исправлены: clean-tree gate, source/binary/invocation identities, unknown-event refusal, recursive evidence seal, idempotent evaluation, bounded output, evaluator-collected ACL, recursive scan/quarantine | Повторить полный mandatory set и preflight из чистого commit | Не требовалось | implementation + tests |
| EXEC-INSIGHT | Windows CRLF в `vectors.csv` ломал Linux argument parsing завершающим `\r` | Fixture writer закреплён на LF; Docker conformance повторно green | Сохранить знание и проверить clean preflight | Не требовалось | `docs/knowledge-log.md`; Docker runner |
| EXEC-INSIGHT | Private-path regex принимал escaped prose `inputs:\\n` за drive path и ложно ставил `EvidenceQuarantined` | Detector ограничен canonical uppercase drive path + allowlisted roots; nested-secret/quarantine regression сохранён | Повторить mandatory validation | Не требовалось | scanner; E10 conformance; knowledge log |
| EXEC-REVIEW | `debug prompt-input` не принимает live-only ignore flags, поэтому обычный user `CODEX_HOME` оставлял config/tool drift | Оба пути переведены на один ACL-restricted temporary `CODEX_HOME` без config/rules/plugins/memories/global instructions; копируется только auth, среда удаляется в `finally` | Проверить clean preflight и prompt digest parity | Не требовалось | orchestrator; invocation/environment receipts; knowledge log |
| EXEC-DECISION | Допустимый spec retry до exposure не реализован как автоматический in-pilot retry | Orchestrator fail-closed останавливает текущий pilot при любой terminal evaluation. Разрешённая §6.4 замена процесса до `turn.started` остаётся нормативной возможностью, но E10 script её не автоматизирует; для неё нужен отдельный явный operator path | Зафиксировать limitation `NoAutomaticRetry` в canonical report | Не требовалось | orchestrator; report limitation |
| EXEC-REVIEW | Pre-live adversarial review обнаружил слабый leakage inventory, fast-exit output race, неполную binary revalidation, private-path gaps и SDK override | До exposure добавлены canonical forbidden inventory с hidden/expected/source identities, pre-live scan, exact implementation identity check до каждого run/evaluation, post-exit stream fault check, Windows/UNC/escaped path regressions и обязательный repo-local SDK | Повторить mandatory validation и clean preflight | Не требовалось | implementation; tests; orchestrator |
| EXEC-REVIEW | Повторный adversarial pass нашёл partial hidden leak, незавершённый report после fail-closed stop, несвязанные parity/inventory и path-prefix traversal | Forbidden inventory дополнен каждой hidden row и generator fragments; после terminal failure оставшиеся slots получают `NotRunDueToPriorFailure` и строится canonical report; parity/preparation inventory digests связаны с pilot manifest и перепроверяются; path allowlist использует canonical directory boundary | Повторить mandatory validation и final review | Не требовалось | implementation; tests; orchestrator; knowledge log |
| EXEC-REVIEW | Final pre-live pass обнаружил противоречивый внутренний raw path и неполное regression-покрытие row-value/PRNG leakage | Prepare/orchestrator fail-closed отклоняют repository root/descendants после canonical path resolution; нормативный путь перенесён в `%LOCALAPPDATA%`; tests покрывают root/descendant/sibling/traversal, canonical boundary/seeded rows и PRNG fragment | Повторить final mandatory validation | Не требовалось | SPEC; provenance; CLI; orchestrator; tests |
| PREFLIGHT | Первые чистые `-PreflightOnly` сохранили реальные snapshots; normalizer сначала встретил fractional `create_time`, затем Unicode system context, несовместимый с ASCII-only Core artifact codec | Scalar number/string `create_time` нормализуется; composite drift и duplicate fields отказываются; recursive sorted JSON записывается детерминированно, а digest считается по normalized UTF-8 bytes без применения доменных Core ASCII/integer ограничений | Повторить targeted/full validation, commit и clean preflight | Не требовалось | external preflight raw; prompt normalizer; tests; knowledge log |
