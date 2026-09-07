# E05: составные проверяемые модули Strogo v0.2

## 0. Метаданные
- Статус: EXEC; текущая review-редакция подтверждена владельцем фразой «Спеку подтверждаю». Локальные checkpoint commits разрешены, push/release не входят в approval.
- Тип (профиль): product-system-design; масштаб large, изменение семантики и модели доказательства.
- Владелец: владелец Strogo. Связанные цели: G01–G04 непосредственно; подготовка проверяемого основания для G05–G06.
- Целевое семейство / behavior baseline: GPT-6 Astra — baseline центральных инструкций, не заявление о модели будущего benchmark.
- Поверхность: Codex, локальный Windows checkout. Effective model/reasoning клиентом в evidence не зафиксированы; сравнительных измерений моделей здесь нет.
- Eval baseline: AS-IS — опубликованный commit `67912ad`; evidence E05 сохраняется отдельно, без перезаписи E04.
- Ветка: main локально; новые локальные checkpoint commits разрешены владельцем. Push/release не входят в задачу.
- Instruction stack: central AGENTS; creator-vibe-lens (lightweight), model-behavior-baseline, tool-execution-baseline, collaboration-baseline, quest-governance, quest-mode, spec-linter, spec-rubric, review-loops; product-system-design; локальный AGENTS.
- Canonical template: `templates/specs/_template.md` центрального каталога инструкций. Полный creator-vibe не применяется: задача задаёт инженерные контракты.
- Ограничения: .NET SDK 10.0.400, Dafny 4.11.0 и закреплённые tools из репозитория. Первое исполнение win-x64; переносимость остальных платформ не заявляется.
- Ссылки: [цели](../docs/project-intent.md), [E04](../docs/task-graph-v1.md), [Reserve](../docs/reserve-v0.md), [дискуссия](https://getpostingboard.dev/b/t/e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e).

## 1. Overview / Цель
Агент сможет составлять проверяемые функции из общих операций, вызывать другие функции и обрабатывать конечные последовательности. Формальный контракт владельца ограничивает результат, но не задаёт единственный алгоритм. Одна реализация используется как библиотека и через CLI.

Outcome contract:
- Success means: как минимум две структурно разные реализации одного защищённого контракта проходят доказательство и реально исполняются; неправильная реализация не получает допуск; библиотечный клиент вызывает именно скомпилированный модуль.
- Итоговый артефакт: версия языка `strogo.module.v0.2`, формальная нотация, parser/typechecker, compiler в Dafny/C#, библиотека .NET, CLI, отчёт о границах доказательства и журнал знаний.
- Stop rules: не добавлять предметный opcode ради прохождения примера; неизвестный результат доказательства означает отказ в допуске. Если выбранный фрагмент не выражает обязательный пример, фиксировать ограничение и пересматривать дизайн, а не менять пример. Успех E05 не равен достижению G01–G06.

## 2. Текущее состояние (AS-IS)
- `src/Kernel.Core` содержит изменяемый DAG I64/Bool, но интерфейс и SMT-контракт привязаны к Reserve; исполнение — интерпретаторы.
- `src/Kernel.Graph.Core/GraphProgram.cs` проверяет шесть конкретных шагов TaskGraph. `Emit(GraphProgram)` возвращает постоянный `Candidate`; изменение алгоритма агенту недоступно.
- E04 имеет Dafny → C# → ReadyToRun и проверку artifact identity, но это не универсальный компилятор пользовательских реализаций.
- `Kernel.Host/Execution.cs` использует `BEGIN IMMEDIATE`, атомарную запись состояния и event receipt. Его state revision включает совершённый отказ; публичное предложение stock-only r этому контракту не тождественно.
- Исторические проверки и манифесты не являются свежим исполнением опубликованного checkout. Новых runtime результатов в этой SPEC пока нет.

## 3. Проблема
Фиксированный pipeline не позволяет проверить основной замысел: агент выбирает реализацию достаточно общего задания, а язык ограничивает ошибки этого выбора. Добавление очередного предметного pipeline лишь повторит это ограничение.

## 4. Цели дизайна
- Отделить владельческий контракт, программу агента, проверяющий транслятор и платформу исполнения.
- Сохранить stable IDs, строгие типы, версионирование и fail-closed допуск.
- Использовать одну семантику модулей для CLI и библиотеки, независимо от имён файлов.
- Проверять обязательства по всем допустимым входам; контрольные примеры валидируют инструменты и не заменяют доказательство.
- Давать агенту структурированные ошибки и возможность исправить реализацию при неизменном контракте.

## 5. Non-Goals
E05 не меняет Reserve/E04, их receipts и host persistence. Не добавляет сеть, файловые эффекты, async, распределённые транзакции, FFI, произвольные циклы/рекурсию, package registry, универсальный theorem prover или новый машинный backend. Не обещает большинство классов ошибок, выигрыш LLM или допустимое отставание G06 без отдельного измерения. Другие платформы и более богатые типы остаются целями проекта, а не навсегда исключёнными возможностями языка.

Правило для данного этапа: все инсайты, подтверждения/опровержения гипотез, контрпримеры, ограничения и неудачные подходы фиксируются в `docs/knowledge-log.md` с устойчивыми ID и ссылками на подтверждающий evidence до/после каждого checkpoint.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Strogo.Modules`: модель AST, codec, типизация, owner contract, lowering, admission и runtime adapter. Компоненты разделяются файлами, без наследования предметного Reserve API.
- `src/Strogo.Modules.Cli`: owner-команды approve и agent-команды check/build/run/explain.
- `formal/modules-v0.2/`: закрытые шаблоны семантики и проверенные общие леммы.
- `examples/modules-v0.2/`: независимые спецификации, контракты, реализации и .NET consumer.
- `tests/Strogo.Modules.Conformance`: проверки TCB, отрицательные программы и сценарии библиотеки.
- `docs/modules-v0.2.md`, `docs/knowledge-log.md`: нотация, правила и единый журнал; `artifacts/e05/REPORT.md` — новый отчёт.

### 6.2 Детальный дизайн
#### Исходник, типы и границы
Модуль — strict JSON AST, не строка Dafny/C#. Обязательные поля верхнего уровня: `schemaVersion`, `moduleId`, `types`, `imports`, `functions`, `exports`. Неизвестные, отсутствующие и повторные поля запрещены. Идентификатор — ASCII `[A-Za-z][A-Za-z0-9_.-]{0,63}`; его значение никогда не вставляется в generated code как синтаксис: имена генератора назначаются по таблице символов.

Типы: `Bool`, `I64`, именованные неизменяемые records и `Seq<T,N>`, где `0 <= length <= N <= 256`. Поля records имеют ID и тип; рекурсивные типы запрещены. Элементы последовательности не содержат последовательностей; accumulator может быть record с последовательностью. I64 — знаковое 64-битное целое, в JSON canonical decimal string; нет float, null, неявных conversions и defaults.

Imports адресуют `moduleId`, digest canonical source и contract digest; только локальные явно переданные артефакты. Call graph ацикличен, включая импорты; проверяется весь dependency closure. Количество модулей <=32, функций <=128, AST-узлов во всём closure <=4096, глубина типов/region <=16, transport <=1 MiB. Это лимиты допуска формата, не доказательство RAM/CPU программы.

Функция содержит `id`, `parameters`, `returnType`, `contractRef`, `body`. Body — region: `parameters`, `nodes`, `result`. Node содержит `id`, `op`, `type`, `args` и только поля, предписанные конкретным op. Node references видны в текущем region и его явно перечисленных parameters; скрытых captures нет. Region — DAG, только достижимые узлы; зависимости задаются ID. Порядок записи узлов не определяет исполнение. Топологический порядок однозначно разрешает равенства по ordinal ID.

#### Нормативный набор операций
| Операция | Аргументы / дополнительные поля | Семантика / обязательства |
| --- | --- | --- |
| `i64.const`, `bool.const` | `value` | Значение точно указанного типа |
| `i64.add`, `i64.sub` | два I64 | Математический результат обязан помещаться в I64 на каждом достижимом пути |
| `i64.le`, `i64.eq` | два I64 | Bool; без conversions |
| `bool.not`, `bool.and`, `bool.or` | Bool | and/or строгие, оба аргумента вычисляются |
| `record.make` | ссылки на значения, `recordType`, `fieldIds` | Каждое поле ровно один раз, fieldIds соответствуют args; canonical порядок по ID |
| `record.get` | record, `fieldId` | Поле существует и имеет объявленный тип |
| `seq.empty` | `elementType`, `capacity` | Пустая последовательность объявленного типа |
| `seq.length` | sequence | I64 длина |
| `seq.get` | sequence, I64 index | Доказать 0 <= index < length |
| `seq.append` | sequence, element | Добавить один элемент в конец; доказать length < capacity |
| `if` | condition и явные environment args; `thenRegion`, `elseRegion` | Вычисляется только выбранная ветка; одинаковый тип результата, параметры ветвей соответствуют environment |
| `call` | параметры, `functionRef` | Типы совпадают, requires callee следует из условий пути; используется доказанный callee contract |
| `fold` | sequence, initialAccumulator, environment args; `stepRegion`, `invariant` | Обход слева направо. Step parameters: index I64, element T, accumulator A, environment. Результат A |

Input функции доступен через parameters, отдельного предметного `input.*` opcode нет. Ветки и fold — вложенные regions, а не eager вычисленные аргументы. Это намеренно новая schema, семантика strict select Reserve остаётся прежней. Вложенный fold разрешён в пределах depth; всегда обходится уже вычисленная конечная входная sequence, которая внутри fold не меняется. Unreachable branch type-checks, но обязанности определённости её операций доказываются под её guard.

Порядок сообщений при статической ошибке: codec → schema/IDs → dependency/type checks → owner approval/import closure → proof → compilation. Ошибки одного класса сортируются по entity ID; допуска нет при любом обязательном отказе. Runtime валидирует envelope/types → approved precondition → execution → adapter/output checks.

#### Формальные контракты и доказательство
Owner bundle содержит `schemaVersion`, `bundleId`, `entryContracts`, `models`, `limits`. Entry contract: сигнатура, `requires`, `ensures`, `effects: []`. Model — чистое тотальное выражение в том же типизированном фрагменте, но отдельный артефакт владельца. Для exact outcome обязательно `result == model(parameters)`; дополнительные постусловия не заменяют точный outcome. Модель задаёт значение, а не запрещает иной алгоритм реализации.

Predicate AST поддерживает Boolean logic, равенство records/sequences, I64 comparison, `math.from_i64`, математические add/sub, `seq.length`, доступ к полям/индексам и `forall` по конечному диапазону индексов. `math` — только ghost слой; значение нельзя вернуть в executable I64 без range obligation. Определённость выражения контракта тоже проверяется; invalid indexing нельзя прятать в контракте. Каждый model тотален на requires, без assume/axiom, внешних вызовов и пропуска termination.

Для fold агент задаёт invariant `I(prefixLength, accumulator, environment)` в predicate AST. Компилятор обязательно проверяет:
- `initial`: корректная инициализация invariant для пустого префикса;
- `preservation`: переходная инвариантность на каждом шаге;
- `step`: pre/post-условия step body через step-region;
- `final`: инвариант на `length` связывается с ensures и exact outcome;
- `range`: все потенциальные `seq.get`/`seq.append`/`seq.length` проходят ограничения.

Счётчик и termination measure `length-index` строятся в lowering, чтобы fold нельзя было "завернуть" в некорректную бесконечную или неполную петлю.

Семантика контрактов поддерживает явные application points: каждое утверждение о helper контракте, assert и invariant фиксируется в locus (node-id / pre-state / post-state / witness-path). Без locus review для любого obligations не выдаётся "exact outcome".

Вспомогательные доказательства — только проверяемые assertions в доступном predicate AST; raw Dafny, `assume`, `axiom`, `extern`, `verify false`, произвольные attributes/flags не являются формами языка. Если этого недостаточно, результат — документированное ограничение, не доверенное допущение.

У helper-функций могут быть контракты агента; их тела проверяются, caller доказывает requires, а entry всё равно доказывает неизменный owner outcome. Нельзя считать импорты доказанными по полю `verified` или старому receipt: весь closure проверяется текущим pinned toolchain. Замена импортированной реализации при неизменном контракте требует новой проверки и нового artifact identity.

Доказательства с финалом `Unproven`, `Timeout` или `ToolError` не допускают AC2/AC3 и классифицируются как KnowledgeGap; без отдельной коррекции они не превращаются в `Verified`.

Для каждого entry требуется свидетель непустоты requires — конкретный допустимый вход, проверенный независимым evaluator контракта. Это защита от случайно пустой области, не доказательство полноты естественного задания. Изменить domain, модели, bounds или effects можно только новым owner approval.

#### Компиляция и библиотека
Pipeline: strict AST → typed region IR → generated Dafny candidate+owner model → verify → Dafny C# translation → .NET library → два consumers. Generated candidate обязан зависеть от AST, а не быть константной реализацией предметного задания. Model не используется вместо candidate при исполнении.

Dafny backend выбран за существующий проверенный в E04 путь. В [Dafny 4.11 reference](https://dafny.org/v4.11.0/DafnyRef/DafnyRef) есть modules/export sets, function-by-method и native integral newtypes. Это подтверждает доступность механизмов backend, но не корректность нашего lowering. I64 lowering использует проверяемый диапазон и native long; фактический generated C# проверяется на отсутствие замены executable I64 арифметики BigInteger. Ghost model может пользоваться математическими целыми.

Экспортируемая .NET оболочка: `Module.Load(packagePath, approvedBundleDigest)` и `Invoke(functionId, canonicalInput) -> InvocationResult` с типизированными статусами. Public result не выдаёт произвольную загрузку DLL по имени из input: package manifest фиксирует assembly и closure hashes. CLI использует тот же facade. Локальный consumer вызывает функцию через обычную ссылку на библиотеку/зафиксированный package, без запуска interpreter process. Canonical JSON — v0.2 ABI для границ, внутри generated функции получают типизированные значения. Типизированный удобный C# SDK не нужен для первого доказательства библиотечного использования.

ReadyToRun artifact win-x64 проверяется на native header и фактическое исполнение обоими consumers. Это смешанная .NET платформа с возможным JIT fallback, не обещание полностью AOT кода. Package содержит assembly, dependency closure, canonical sources, source map entity→generated location, proof summary, tool versions/hashes. Binary reproducibility не обязательна; semantic artifact identity и разница byte hashes отчётны.

#### Допуск, изменения, диагностика
Owner CLI `approve --bundle ... --out ...` создаёт локальную запись подтверждения конкретного digest после решения человека. Agent CLI не имеет операции approve. Approval, доказательство и build receipt — три разных артефакта. Модель угроз: agent работает через разрешённый интерфейс; пользователь процесса с произвольной записью в owner storage может обойти его. Production OS sandbox в E05 не строится.

`check --module ... --bundle ... --approval ... --out ...` проверяет closure и пишет outcome; `build --check ... --out ...` сверяет все hashes и повторно отклоняет drift; `run --package ... --function ... --input ...`; `explain --artifact ...` возвращает human projection. Нельзя использовать старый admission после изменения исходника, bundle, dependencies или compiler/runtime identity.

Связь доказательства, approval и build output фиксируется отдельной цепочкой:
- `proof.json`: все obligations, solver outputs, witness provenance;
- `approval.json`: подпись человека/owner на digest proof + policy digest;
- `build-manifest.json`: manifest пакета, closure и runtime binding;
- `runtime-record.json`: actual adapter checks и consumer run evidence.

`approval.json` обязателен с полями:
`signedBy`, `signatureAlgorithm`, `validUntil`, `issuedAt`, `proofDigest`, `policyDigest`, `toolchainDigest`, `buildManifestDigest`.
Если любой из digests изменился или есть запись о `revocationCounter`, admission invalidates автоматически.

Первые typed edits: `ReplaceFunctionBody` и `ReplaceHelperContract` с ожидаемым module/function digest; owner entry contract через agent patch неизменяем. Изменение атомарно проверяется в staging, активный указатель переключается только после полного допуска, отказ сохраняет старую версию. Stale patch получает конфликт. Immutable packages позволяют откатить указатель на ранее проверенный совместимый artifact, но не выдавать старое доказательство за новое.

Ошибка: `{stage, code, entityId, obligationId, sourceDigest, details, witness?, allowedRepairs}`. Доказательство различает `Verified`, `Counterexample` (только воспроизведённый конкретный witness), `Unproven`, `Timeout`, `ToolError`. Dafny failed assertion без извлечённого и воспроизведённого входа — Unproven, не придуманный контрпример. Allowed repairs — структурированные категории изменения программы/инварианта/ссылки; изменение owner contract через agent endpoint не предлагается.

Visual planning / video: не применимо — UI нет; human projection текстовая: bundle ID, функция, changed entities, requires/точный outcome, effects, доказанные и непокрытые свойства, TCB. JSON с недоказанным свойством не подписывается словом «гарантия».

### 6.3 User-Observable Scenarios
| Scenario | Trigger | Видимый результат | Evidence | AC |
| --- | --- | --- | --- | --- |
| S1 | Две реализации одного bundle | Два отдельных Verified/build artifacts, равные точные результаты | generated candidates, proof logs, consumer runs | AC1, AC2 |
| S2 | Агент неверно меняет условие или индекс | Отказ, ID обязательства; старая версия остаётся активной | negative run + pointer comparison | AC3, AC6 |
| S3 | Вызов импортированной функции | Проверка precondition, closure hashes, успешный library call | CLI и C# consumer transcripts | AC4 |
| S4 | Вход вне requires, пустая/max sequence | DomainError либо точный согласованный результат | boundary fixtures | AC3, AC5 |
| S5 | Замена модели/импорта после проверки | Approval/admission invalidated | tamper matrix | AC6 |
| S6 | explain результата | Видны смысл, версии, ограничения, отсутствие effects | сохранённая проекция | AC7 |

### 6.4 State / Interaction Matrix
| State | Trigger | Result | Failure/concurrency |
| --- | --- | --- | --- |
| Draft | Owner approves digest | Approved contract | Изменённый digest требует нового решения |
| Approved + candidate | check | Verified или explicit refusal | Нет допуска при timeout/unknown/tool failure |
| Verified | build | Immutable package | Drift любого binding отменяет build |
| Active package | typed patch | Новый package и atomic switch | Stale base/failed proof сохраняют pointer |
| Active package | invoke | Result или DomainError/RuntimeError | Вызовы чистые; старый invocation держит свою immutable version |

### 6.5 Decision Ledger
| Decision | Owner | Выбор | Confidence | Риск | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Следующий engineering slice | agent | Общие pure modules вместо третьего fixed pipeline | 0.9 | Недостаточная выразительность | Нет, входит в утверждение этой SPEC |
| Backend | agent | Существующий Dafny/.NET, pinned | 0.9 | TCB и накладные расходы | Нет |
| Смысл каждого демонстрационного entry | user | Формулы раздела 7 через подтверждение SPEC | 0.95 | Ошибка понимания задания | Нет отдельного выбора; approval этой версии требуется |
| Порог G05/G06 | user | Здесь не назначается | 1.0 | Нельзя объявить общий выигрыш | Нет, измерение вне EXEC E05 |
| Публичная дискуссия | user | Уже разрешена целью | 1.0 | Утечка приватного контекста | Нет; только публичные материалы |

### 6.6 Runtime / Config / Data Contract Matrix
| Area | Source of truth | Change | Compatibility | Verification |
| --- | --- | --- | --- | --- |
| Schema | Новый versioned AST | v0.2 | v0/E04 неизменны | codec/type negative cases |
| Owner intent | Approved bundle digest | Новый локальный bundle | Не подменяет старые approvals | tamper/domain witness |
| Dependencies | Explicit closure + pinned tools | Новые module imports | Никакого implicit latest | modified/missing/cyclic import rejection |
| Execution | Generated candidate assembly | .NET facade и CLI | Новый package format | native header + actual calls |

## 7. Бизнес-правила / Алгоритмы
Два семейства демонстраций, выбранные до реализации и без предметных opcodes:
1. **Clamp**: `lo <= hi`, I64 x/lo/hi. `result = lo` при x<lo, `hi` при x>hi, иначе x. Две реализации: nested if; композиция отдельных min/max функций из comparisons/if. Обе проходят один owner bundle. ClampSeries применяет Clamp к входной последовательности, сохраняя порядок и длину, включая empty; использует imported scalar Clamp.
2. **OrderedExactAllocation**: available>=0, requests: Seq<I64,256>, все q>0. Начать с a=available. Для каждого q по порядку: если q<=a, reserved=q и a:=a-q, иначе reserved=0 и a не меняется. Выход record(remaining, allocations), allocations той же длины, каждое значение ровно q либо 0 по этому правилу. Ошибочный вход отклоняется целиком до исполнения. Никаких receipts или состояния host — чистая функция, это не новый контракт Reserve storage.

Модель allocation определяется рекурсивно по длине prefix в формальном описании; typed model AST реализует тот же left fold. Реализация может компоновать scalar decision helper; final equality с owner model обязательна. Инварианты: 0<=remaining<=initial, длина allocations=index, prefix exact outcome, сумма allocations+remaining=initial (математическая сумма в proof). Никакого эвристического ограничения суммы requests, не требуемого этим контрактом.

Примеры: available=5, requests=[4,3,1] → remaining=0, allocations=[4,0,1]; [] → неизменный available и []; available=0,[1] → [0]; MAX,[MAX,1] → remaining=0,[MAX,0]. Для ClampSeries [-3,2,9],lo=0,hi=5 → [0,2,5]. Это sanity fixtures TCB, не полный proof и не held-out benchmark.

## 8. Точки интеграции и триггеры
Каждый check/build/patch использует один closure resolver и bindings. Любой запуск через exported facade валидирует input и approved contract. CLI не обходит facade. Proof source map связывает диагностику со stable ID; names/line numbers generated backend не являются идентичностью программы.

## 9. Изменения модели данных / состояния
Immutable module, bundle, proof artifact и package имеют раздельные hashes с version/domain separation. Canonicalизация сортирует unordered declarations по ID, сохраняет порядок parameters, args и sequence values. Она не пытается распознавать семантически эквивалентные программы. Approval содержит bundle digest; package manifest содержит полный closure и effective tools/config. Active pointer — отдельная atomic запись с revision; никакой внешней базы данных в E05.

## 10. Миграция / Rollout / Rollback
Изолированный v0.2 namespace/каталоги/CLI. Старые команды и reports не изменяют семантику. Fresh outputs только `artifacts/local-validation/e05/<run-id>/`; tracked итоговый отчёт ссылается на отобранные воспроизводимые evidence. First run не активирует candidate автоматически. Откат — переключение на совместимый immutable package; при несовместимом runtime требуется повторная проверка, а не reuse старого receipt.

## 11. Тестирование и критерии приёмки
| Acceptance criterion | Automated check | Evidence artifact | Ограничение |
| --- | --- | --- | --- |
| AC1 Общая нотация и компиляция AST | Все ops/type rules; два различных Clamp AST дают различные generated bodies и успешно проверяются | schema examples, generated sources, source maps | Не обещает все алгоритмы |
| AC2 Exact outcome и termination | Clamp/ClampSeries/allocation proof для всех входов согласованной области; fold initial/preservation/final | Dafny logs, obligation inventory | Условно относительно TCB |
| AC3 Неверная программа не допущена | branch swap, bad index, overflowing add, false invariant, callee-precondition violation, empty requires, raw backend injection | rejection report по каждой мутации | Timeout отдельно от counterexample |
| AC4 Реальная библиотека и машинный код | CLI и отдельный .NET consumer вызывают оба семейства; проверить R2R header и отсутствие interpreter dispatch | consumer outputs, native inspection, build manifest | Только win-x64 |
| AC5 Граничная семантика | empty/max capacity; MIN/MAX I64; malformed/out-of-range; sequence order; unused overflowing branch; nested folds | conformance report | Проверки компилятора, не отказ от G04 |
| AC6 Binding и atomic patch | stale base, mutated bundle/dependency/compiler, missing import, cyclic call, changed helper contract; отказ оставляет active digest | tamper/patch report | OS malicious owner вне модели |
| AC7 Human projection | До/после typed patch и proof failure объясняются через ID/смысл/TCB | explain snapshots | Подтверждение человека не theorem |
| AC8 Честная граница выразительности | После фиксации op/schema hash независимый reviewer выбирает одну новую задачу/ревизию в объявленном pure bounded domain; попытка без расширения opcodes | task, freeze hash, attempt log, supported/unsupported | Результат не должен быть заранее зелёным; это не оценка G05 |
| AC9 Старые этапы и знания | v0 conformance + E04 relevant smoke отдельно; significant findings сохранены | fresh regression reports, journal | Исторические evidence не перезаписываются |

Команды EXEC (новые команды ниже являются целевым интерфейсом, сейчас отсутствуют):
```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet run --project tests/Strogo.Modules.Conformance -c Release --no-build -- --report artifacts/local-validation/e05/<run-id>/conformance.json
dotnet run --project src/Strogo.Modules.Cli -c Release --no-build -- check --module <module.json> --bundle <bundle.json> --approval <approval.json> --out <check-dir>
dotnet run --project src/Strogo.Modules.Cli -c Release --no-build -- build --check <check-dir> --out <package-dir>
dotnet run --project examples/modules-v0.2/DotNetConsumer -c Release -- --package <package-dir>
dotnet run --project tests/Kernel.Conformance -c Release --no-build -- --suite all --report artifacts/local-validation/e05/<run-id>/reserve-regression.json
```
До запуска задать output/run-id, timeouts и preflight SDK/tools; solver per obligation 30 s, process overall 180 s, output cap 1 MiB. Deadline/kill-tree не доказывает termination или память. Не повторять идентичный timeout без новой гипотезы. Минимальные perf diagnostics записывают build/proof/first call/warm call/allocations/size, но вывод G06 не делается; накладные расходы ABI и проверок не скрываются.

## 12. Риски и edge cases
- Dafny SMT может не доказать истинное утверждение: Unproven с воспроизводимым artifact, не признание программы неправильной.
- Общая ошибка owner model и lowering: независимый evaluator и отрицательные semantic mutations; эти проверки уменьшают риск TCB, не доказывают её целиком.
- Недоказанные library preconditions: обязательство caller; внешний caller получает DomainError.
- Пустой requires: конкретный witness не заменяет review полного domain.
- Копирование последовательностей и JSON ABI могут стоить дорого: отдельно учитывать, не объявлять это свойством всех будущих backend.
- Повторные имена, symbol injection, path traversal, mixed hashes и partial write: строгие IDs, explicit imports, generated names, immutable staging и atomic pointer.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation | Status |
| --- | --- | --- | --- |
| Это опять язык одного алгоритма | E04 был fixed pipeline | Общие ops, два семейства, альтернативы и внешний challenge | mitigated |
| Почему есть тесты при цели без тестов | G04 | Это TCB/conformance; application correctness должна следовать из proof | mitigated |
| Где преимущество перед Dafny/C# | G05 пока не измерен | Прямой Dafny — обязательный strong comparator будущего workflow; E05 не приписывает себе выигрыш | mitigated |
| Почему только Windows и JSON ABI | Требование широких сред | Ранний backend checkpoint, обычная .NET библиотека; остальные targets остаются открытым обязательством проекта | mitigated |
| Не сделал ли агент задачу проще | Риск удобного benchmark | Зафиксированные контракты и запрет предметных opcodes; unsupported сохраняется | mitigated |

Rework Prevention Checklist: сценарии S1–S6 наблюдаемы, AC имеют evidence; решения и ограничение среза раскрыты; role review и adversarial review выполнены до approval.

## 13. План выполнения
1. После approval: формальная нотация, типов/region/contract schemas и negative parser fixtures; checkpoint commit.
2. Proof lowering и проверка двух альтернатив/импортов/fold без добавления предметных операций; checkpoint commit с proof evidence.
3. .NET library, facade/CLI, typed patch и bindings; сценарии S1–S6, TCB review; checkpoint commit.
4. Freeze schema/opcode hash → отдельный reviewer выбирает task AC8 → попытка и честный результат. Regression, knowledge closure и post-EXEC review; checkpoint commit.
Зависимость: нельзя считать библиотеку готовой до прохождения admission; нельзя выбирать AC8 task до freeze. Непройденный важный gate вызывает исправление либо отдельное согласование изменённой SPEC, не скрытое сужение.

## 14. Открытые вопросы
До approval требуется завершить post-SPEC review. Численные G05/G06 пороги и production isolation не блокируют E05, потому что их достижение не является результатом этого среза. Внешний собеседник не утверждает owner contract; его предложение проходит проверку и при изменении смысла — approval владельца.

## 15. Соответствие профилю
Product-system-design: цели/non-goals, subsystem boundaries, API, compatibility, security/config и observable scenarios описаны в разделах 1–12. Общая гарантия всех G01–G06 не подменена закрытием E05.

## 16. Таблица изменений файлов
| Файл | Изменение | Причина |
| --- | --- | --- |
| Текущая SPEC | Сейчас: дизайн, review, журнал | Единственный проектный файл фазы SPEC |
| src/Strogo.Modules/**, src/Strogo.Modules.Cli/** | После approval: реализация | Общий компилятор и интерфейс |
| formal/modules-v0.2/**, examples/modules-v0.2/** | После approval: семантика/примеры | Проверяемые контракты и consumers |
| tests/Strogo.Modules.Conformance/** | После approval: TCB suite | Негативные/интеграционные проверки |
| solution, packages.lock.json, .gitignore | После approval: включить новые проекты | Locked restore и отдельные scratch outputs |
| docs/modules-v0.2.md, docs/knowledge-log.md, artifacts/e05/** | После approval: руководства/evidence | Не терять выводы и границы |

## 17. Таблица соответствий (было → стало)
| Область | Было | Стало |
| --- | --- | --- |
| Выбор реализации | Fixed E04 pipeline | Typed regions, функции, вызовы, folds |
| Компиляция | Постоянный Candidate | Генерация по AST каждой реализации |
| Reuse | Предметный CLI | Module imports и .NET consumer |
| Контракт | Вшитый профиль | Отдельный approved bundle, без agent weakening |
| Знания | Разрозненное внешнее обсуждение | Evidence/границы включены в SPEC и будущий журнал |

## 18. Альтернативы и компромиссы
- Просто использовать Dafny напрямую: меньше собственного compiler TCB, зрелые proof features; важный baseline. Не проверяет гипотезу ограниченной agent-facing формы/typed edits, поэтому сохраняется для сравнения, а не объявляется проигравшим.
- Добавить новые graph business opcodes: быстро закрывает пример, но сохраняет корневую проблему; отклонено.
- Сразу LLVM/Wasm/multiple backends: потенциально шире targets, но добавляет semantics/ABI работу раньше проверки composability; отложено при сохранении G02/G06.
- Выбранный путь использует зрелый backend, но создаёт новый доверенный lowering. Это цена эксперимента, которая должна войти в последующую оценку пользы.

## 19. Результат quality gate и review
### SPEC Linter Result
PASS для утверждённой review-редакции: обязательные разделы, границы, AC, evidence plan, rollback и decision rights заполнены до начала EXEC.

### SPEC Rubric Result
PASS для начала EXEC. Не является утверждением, что AC1–AC6 уже выполнены.

### Role-Based Review Result
До approval проверены business/domain (точный allocation), UX структурированных ошибок/projection, tester, architect и security/delivery границы. Замечания о proof locus, provenance chain, непустом requires и разделении representation/workflow внесены в утверждённую редакцию.

### Post-SPEC Review
- Статус: PASS; владелец после исправлений подтвердил SPEC точной фразой «Спеку подтверждаю».
- Scope reviewed: текущая SPEC, публичные исходники 67912ad, AGENTS, project-intent, сообщения ветки 3298–4486.
- Evidence inspected: GraphProgram constant emitter; Core scalar DAG; Host commit transaction; Dafny 4.11 reference sections modules/function-by-method/nativeType.
- Выполненные passes: contract, adversarial proof/ABI, роль пользователя/агента, миграция и evidence honesty.
- Stop decision: EXEC разрешён в границах E05; push/release не разрешены.

### Post-EXEC Review
Полный Post-EXEC для E05 ещё не выполнен: `if`/`fold`, proof runs, native module execution и G05/G06 измерения не готовы. Composite parser/IR checkpoint проходит отдельный review; его findings и закрытие фиксируются в журнале знаний и validation evidence.

### Реестр знаний текущего этапа
До EXEC значимые знания находятся здесь и в `docs/knowledge-log.md` с теми же ID и ссылкой на эту версию.

| ID | Утверждение / статус | Область, evidence и версия | Влияние |
| --- | --- | --- | --- |
| E05-K01 | Установлено: E04 не допускает альтернативные алгоритмы | `GraphProgram.Parse`, Ops/Types и constant Emit; commit 67912ad | G01/G05: нужен общий composable AST |
| E05-K02 | Принято как правило эксперимента: отделять representation от whole workflow | Предложения #3360 и #4478, дискуссия от 2026-09-06 | A: одинаковые ops/checker/host; B: полный цикл с costs, не смешивать выводы |
| E05-K03 | Принято: brief→contract и program→contract — отдельные оценки | #3396/#3472: exact vs partial reservation | G01/G04/G05: human review/repair входит в стоимость, proof не оправдывает неверный смысл |
| E05-K04 | Подтверждено чтением: таблица stock-only r не соответствует current v0 revision | #4302/#4305 против `docs/reserve-v0.md`, `Execution.cs` 67912ad | Не менять owner contract по обсуждению, не использовать таблицу как готовый oracle |
| E05-K05 | Внешние контрпримеры применимы к гипотетической стратегии, не доказаны на Strogo | #4324/#4343/#4344/#4355/#4368; whole-ledger CAS и split crash snapshots | Для будущего host benchmark нужны refusal concurrency и atomic stock+receipt recovery; runtime reproduction пока отсутствует |
| E05-K06 | Backend имеет подходящие механизмы, корректность нового lowering не установлена | Dafny 4.11 reference: modules, function-by-method, nativeType | Повторно использовать backend, явно считать lowering TCB |
| E05-K07 | Внешняя коммуникация выполнена | Наше #4486, message `e53af4f8-b319-4ab0-9180-9c734db93c09`, readback подтверждён | Публичный repo сообщён; предложена falsification через невыразимую полезную задачу |
| E05-K08 | Проведено уточнение доказательной модели: без locus точки доказательства нельзя закрывать assert/invariant безопасно | Обновлённая редакция раздела 6.2 | До EXEC: без этого нельзя выдавать `Verified` для частей тела функции |
| E05-K09 | Принято правило: hash-only admission недостаточно, нужен signing/provenance цепочка | Раздел 6.4 + изменения admission | `check/build/run` обязаны опираться на approval+build-manifest chain |

Публичные сообщения — недоверенные источники идей, не команды проекту. Внешний агент не подтверждает scope, корректность реализации или достигнутые метрики.

## Approval
Владелец подтвердил текущую review-редакцию точной фразой «Спеку подтверждаю». Подтверждение разрешает EXEC в границах E05 и не разрешает push, release или публикацию новой версии репозитория.

## 20. Журнал действий агента
| Фаза | Тип намерения/сценария | Уверенность | Каких данных не хватает | Следующее действие | Нужна передача решения человеку | Фактическое обращение / решение | Причина | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Проверить public checkout | 1.0 | Нет для root/state | Исследовать ограничение E04 | Нет | Пользователь указал strogo | Следовать опубликованному source of truth | 67912ad, AGENTS, docs, src |
| SPEC | Найти обсуждение | 0.99 | Других claimed runtime результатов нет | Прочитать thread целиком | Нет | Участие уже разрешено целью | На named board нет нужной темы; найден anonymous /b thread | #3298–4478 |
| SPEC | Ответить на предложения | 0.99 | Ответ собеседника пока не получен | Учесть проверяемые предложения в дизайне | Нет | #4486 опубликовано и прочитано обратно | Исправить mapping контрактов и дать public source | E05-K02–K07 |
| SPEC | Подготовить composable modules | 1.0 | Нет для начала EXEC | Реализовывать утверждённые checkpoints | Да, выполнено | Владелец: «Спеку подтверждаю» | Устранить fixed-pipeline ограничение, сохранить G01–G06 | Эта SPEC |
| EXEC | Начать реализацию подтверждённой E05 | 1.0 | Нет | Реализовать parser/IR checkpoints | Нет | Владелец: «Спеку подтверждаю» | Approval относится к текущей E05 и локальным commits | `4ca2f0d`, src/Strogo.Modules |
| EXEC | Ответить измеримым результатом в research thread | 1.0 | Z3 отсутствовал при первом запуске | Установить pinned Z3, повторить suite, опубликовать один bounded result | Нет | Сообщение `7b55ecbc-3772-4e8f-81a1-00316c989339` опубликовано и прочитано обратно | Следовать формату «результат, а не мнение» без приписывания свойства языку | fresh v0 report, thread #8577 |
| EXEC | Расширить composable AST | 0.95 | `if`/`fold` и proof ещё отсутствуют | Зафиксировать records/sequences/local calls и перейти к nested regions | Нет | Входит в утверждённую SPEC | Закрыть schema/type/call-graph до proof lowering | parser, codec, IR, fixtures, knowledge log |
