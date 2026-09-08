# Strogo Modules v0.2: структура, owner contracts, regions и IR

Этот документ закрепляет текущий E05 checkpoint: входную структуру, строгость формы, parser/typechecker, детерминированный IR, scalar/composite reference semantics, owner bundle v0.4 и один root-only bounded left `fold` с exact owner-prefix proof в Dafny.

## 1) Структура исходного модуля

Модуль `v0.2` — строгое JSON-дерево верхнего уровня:

```json
{
  "schemaVersion": "strogo.module.v0.2",
  "moduleId": "<id>",
  "types": [ <TypeDecl>* ],
  "imports": [ <ImportDecl>* ],
  "functions": [ <FunctionDecl>* ],
  "exports": [ <string> ]
}
```

Требования:
- все перечисленные поля обязательны;
- любые отсутствующие/дополнительные поля на фиксированном уровне запрещены;
- JSON должен быть ASCII-only по строкам и без дубликатов полей;
- размер транспортуемого JSON строго ограничен;
- переданные `StrogoLimits` могут только ужесточить нормативные hard limits языка, но не повысить их;
- порядок полей в объекте влияет на форматный ввод только через canonical-канонизацию, а не через семантику.

## 2) Идентификаторы

Идентификатор любого именованного элемента проходит паттерн:
`^[A-Za-z][A-Za-z0-9_.-]{0,63}$`.

`id` используется как ссылочный символ в AST и для диагностики. Он не эквивалентен имени файла/текста/хост-идентификатору и не используется как API-сигнатура runtime напрямую.

## 3) Типы

`TypeRef` поддерживает:
- примитивы: `I64`, `Bool`;
- записи: строковый ID объявления, например `"Summary"`; внутри модели он представлен как `TypeRef("Record", Name: "Summary")`;
- последовательности: `{"kind":"Seq","elementType":<TypeRef>,"capacity":"N"}`; capacity — canonical decimal natural string без знака и ведущих нулей.

Ограничения v0.2:
- именованный `Record` должен существовать в `types`;
- все типы полей проверяются после чтения полного набора деклараций;
- прямые и косвенные рекурсивные records запрещены;
- вложенность `Seq(Seq(T))` запрещена;
- `capacity` имеет фиксированную границу языка `N <= 256`;
- циклические типы и рекурсия типа не разрешены на уровне текущего checkpoint.

## 4) Нотация body и узлов

Функция имеет:

- `parameters`: список `{id,type}`;
- `returnType`;
- `body` с:
  - `parameters` (должны совпасть по порядку с `function.parameters`);
  - `nodes` — список инструкций с общими полями `id`, `op`, `type`, `args` и только с дополнительными полями конкретного `op`;
  - `result` — `id` значения результата: параметра функции или узла body.

Body и каждая ветвь `if` имеют одну форму region. Параметры вложенной region — локальные ID, которые позиционно связываются с явными environment-аргументами родительского узла. Их типы выводятся из этих аргументов и сохраняются явно в typed IR. Вложенная region не видит значения родителя под исходными ID: чтобы использовать значение, агент обязан передать его в `args` и объявить соответствующий локальный параметр.

Проверки body:
- граф зависимостей узлов должен быть DAG;
- все узлы в графе достижимы из `result`;
- ссылка `args` разрешена только на параметр или узел того же body; текстовый порядок узлов значения не имеет;
- арность и типы аргументов соответствуют `op`;
- `result` должен существовать и иметь тип равный `returnType`.

Поддержанные в текущем фрагменте v0.2 `op`:
- `i64.const`, `bool.const` (арность 0);
- `i64.add`, `i64.sub`, `i64.le`, `i64.eq`;
- `bool.not`, `bool.and`, `bool.or`;
- `record.make` с `recordType` и параллельными `fieldIds`/`args`;
- `record.get` с `fieldId`;
- `seq.empty` с `elementType`/`capacity`, `seq.length`, `seq.get`, `seq.append`;
- локальный `call` с `functionRef`.
- `if` с `condition`, явными environment-аргументами, `thenRegion` и `elseRegion`.
- `fold` с неизменяемой bounded sequence, initial accumulator, ordered explicit environment, closed `stepRegion` и обязательным proof `invariant`.

У `if` первый аргумент обязан иметь тип `Bool`; остальные аргументы образуют environment обеих ветвей. Обе ветви проверяются независимо от значения condition, должны принять environment той же арности и вернуть тип узла `if`. В исходном и typed IR ветви остаются вложенными regions, поэтому они не превращаются в eager-операнды внешнего DAG. Scalar lowering сохраняет это ветвление в Dafny, а reference evaluator исполняет только выбранную ветвь.

`value` обязателен только у констант. Лишнее `value: null` у другой операции является ошибкой schema, а не допустимым default. `call` проверяет сигнатуру и общий локальный call graph, включая вызовы внутри ветвей; рекурсия и взаимная рекурсия запрещены. Разрешение вызовов через import closure относится к следующему checkpoint.

Тип ноды должен соответствовать операции:
- арифметика — `I64`,
- сравнительные — `Bool`,
- булевые — `Bool`.

## 5) Лексер

Лексер выполняет только токенизацию и поверхностный guard:
- поток bytes парсится как JSON;
- отслеживается глубина структуры;
- формируется последовательность внутренних `ModuleToken` для последующей трассировки;
- malformed JSON, превышение глубины и invalid UTF-8 нормализуются в `ModuleLexerException`, который входит в единый публичный слой `ModuleException` и сохраняет byte offset.

Лексер не выполняет типовую/схематическую проверку AST, только помогает локализовать ошибки формата.

## 6) Парсер

Парсер выполняет строгое чтение с нормализацией:
- `ParseStrict` через `CanonicalJson` с лимитами из `StrogoLimits`;
- единое правило `CheckObject`: только ожидаемые имена полей;
- единые ошибки через `ModuleException` (`stage`, `code`, `entityId`, `details`); ошибки strict JSON из `Kernel.Core` не выходят через публичную границу модулей;
- node/region diagnostics используют qualified locus вида `function/<id>/body/node/<id>/then/node/<id>`; `/` запрещён грамматикой ID и потому отделяет сегменты без коллизий даже для ID с точками;
- неупорядоченные декларации и nodes валидируются по ordinal ID, поэтому первая статическая ошибка не зависит от их transport order;
- canonical-байты создаются отдельной стадией кодека.

Ключевые коды отказа:
- `SchemaVersionMismatch`, `TypeLimitExceeded`, `FunctionLimitExceeded`,
- `Duplicate*`, `UnsupportedOpcode`, `TypeMismatch`, `ReturnTypeMismatch`,
- `CycleDetected`, `UnreachableNode`, `ExportedFunctionMissing`,
- `RegionParametersMismatch`, `RegionResultTypeMismatch`, `RegionDepthExceeded`,
- `SchemaInvalid`, `TransportLimitExceeded` и др.

## 7) Compiler (IR)

`ModulesCompiler.Compile(ModuleParseResult)` компилирует только провалидированный AST в `ModuleIr`.

Промежуточный код (`FunctionIr`) формируется следующим образом:
- параметры векторно индексируются как `-1`, `-2`, ... (в порядке объявления);
- узлы сортируются по топологическому порядку с детерминированным tie-break (`SortedSet` по `id`);
- для каждого узла записывается `IrInstruction`:
  - `DestinationIndex` — порядковый индекс инструкции,
  - `OperandIndices` — индексы параметров/предков,
  - тип и opcode-specific metadata для констант, records, sequences и вызовов.

Для `if` инструкция дополнительно содержит две `RegionIr`. Каждая хранит типизированные локальные параметры, собственные инструкции и индекс результата. Инструкции ветвей не добавляются во внешний список, что сохраняет границу условного вычисления для последующего lowering. `fold` хранит отдельный `FoldRegionIr`: ordered parameters имеют роли index, element, accumulator и environment; скрытый capture, `call` и nested `fold` запрещены.

Порядок деклараций types/functions/imports/exports и порядок узлов не участвуют в identity: codec сортирует их по стабильным ID. Порядок parameters и обычных `args` сохраняется. Для `record.make` codec и compiler сортируют пары `fieldId→arg` вместе, чтобы перестановка полей не меняла смысл или digest.

`ModuleParseResult` и `ModuleIr` создаются только внутри доверенных стадий parser/compiler. Canonical bytes возвращаются defensive copy, а compiler повторно сверяет source bytes и digest перед lowering. `ModuleIr` дополнительно хранит canonical bytes и domain-separated `FunctionCountDigest`.

## 8) Reference evaluator

`ModulesReferenceEvaluator.Invoke` исполняет провалидированный `ModuleIr` как ограниченную эталонную семантику. Текущий профиль поддерживает `I64`, `Bool`, records, bounded sequences, все реализованные scalar/composite opcodes, локальный `call`, `if` и `fold`. Публичный вызов разрешён только для функции из `exports`; внутренние функции доступны только через `call`.

Арифметика `I64` проверяемая: переполнение даёт `evaluation:ArithmeticOverflow` с устойчивым locus узла. У `if` вычисляется только выбранная `RegionIr`; environment передаётся её локальным параметрам. Один `MaxSteps` действует на весь вызов вместе с вложенными regions, локальными calls и fold iterations и ограничен hard maximum `1_000_000`. Fold расходует шаг на dispatch, шаг на каждую iteration и фактически исполненные step nodes; empty sequence возвращает initial accumulator без исполнения step. Нулевой, отрицательный или превышающий hard maximum бюджет отклоняется до исполнения.

Runtime composite values представлены `ModuleRecord` с точным type ID и полным набором полей и `ModuleSequence` с element type, capacity и immutable items. Перед исполнением входы проверяются рекурсивно: другая capacity, пропущенное/лишнее поле или неверный вложенный item дают `RuntimeTypeMismatch` с точным locus. `seq.get` использует zero-based index и возвращает `SequenceIndexOutOfRange`; `seq.append` не изменяет исходное значение и возвращает `SequenceCapacityExceeded` при заполненной sequence.

Evaluator намеренно отделён от generated library: он нужен как executable reference oracle для differential checks lowering и owner-witness replay. Сейчас он получает уже скомпилированный IR и потому не является независимой проверкой parser/compiler. Не поддержаны imports, canonical JSON ABI, admission и machine-code package; неизвестный opcode/type даёт явный `UnsupportedRuntimeOpcode`/`UnsupportedRuntimeType`, а непустой unresolved import closure — `UnsupportedRuntimeImports`, вместо частичного исполнения.

## 9) Lowering в Dafny/C#

`ModulesDafnyLowerer.Lower` принимает только провалидированный `ModuleIr` и детерминированно создаёт Dafny source для `I64`, `Bool`, records, bounded sequences, scalar/composite operations, local calls и nested `if`. Functions, parameters, records, fields, sequence shapes и local values получают generated symbols; пользовательские ID не вставляются в синтаксис и сохраняются отдельно в source map `entityId → generatedName + line`.

Record понижается в immutable Dafny `datatype`, а каждый структурно уникальный `Seq<T,N>` — в subset type над `seq<T>` с ограничением `|s| <= N`. `seq.get` создаёт явное obligation `0 <= index < length`; каждый candidate и owner `seq.append`, включая вложенный, материализуется как временное значение точного bounded subset type и тем самым создаёт obligation `length < capacity`. `seq.length` и index используют явные conversions между mathematical `int` Dafny и native `I64`, поэтому backend не получает неявного преобразования.
Safe fixture доказывает эти obligations на последовательности, построенной внутри candidate. Функции, принимающие произвольную sequence/index без owner range contract, остаются `Unproven`: harness требует три фактических `assertion might not hold` вместо добавления скрытого предусловия.

`I64` задаётся как native newtype с точным диапазоном `Int64`. Поэтому безопасный selector и безопасный local call проверяются и переводятся в C# методы с параметрами/результатами `long`. Арифметика без достаточного owner `requires` не получает скрытого предусловия: Dafny отклоняет `addOne(x)` на полном диапазоне как `Unproven`, потому что результат может выйти за границу newtype.

Перегрузка `ModulesDafnyLowerer.Lower(ModuleIr, OwnerBundle)` сначала выполняет строгую привязку bundle, затем добавляет owner model как Dafny `function`, переносит утверждённый `requires` и создаёт единственное допустимое postcondition `ensures result == M(parameters)`. Owner и candidate используют одну таблицу generated symbols для records, fields и bounded sequences. Внутренние результаты `i64.add`/`i64.sub` модели материализуются типизированными `I64` let-выражениями: backend обязан доказать диапазон каждого шага, а не только итогового математического выражения. `bool.and/or` проходят через generated `StrictAnd`/`StrictOr`, чтобы definedness обоих аргументов не зависела от short-circuit синтаксиса backend; `if` остаётся lazy guard.

Первый end-to-end smoke запускается командой:

```powershell
pwsh -File tools/Test-Modules-Dafny-Lowering.ps1 -RunDirectory artifacts/local-validation/e05/modules-dafny-lowering
```

Скрипт получает все Dafny candidates из production lowering, проверяет safe nested selector, local call, scalar operators и composite record/sequence candidate закреплённым Dafny 4.11.0, требует отказа unsafe arithmetic, проверяет по две структурно разные реализации scalar и composite owner contracts и обязательные отказы неверной реализации, слабого domain и partial composite model. Для composite owner два correct candidates дают `5 verified, 0 errors`, wrong order сначала воспроизводится на owner witness и затем не доказывает exact postcondition, а partial `seq.get` остаётся `Unproven` с range diagnostic. Отдельные парные proof cases требуют `Unproven` для `false and <partial>`/`true or <partial>`, `Verified` для эквивалентных lazy guarded `if` и `Unproven` для вложенного owner `seq.append` при полном входном `Seq<T,N>`. Скрипт запускает отдельные generated .NET consumers для обеих correct composite реализаций. Каждый внешний процесс имеет общий deadline 180 секунд и общий лимит stdout/stderr 1 MiB. Windows Job Object связывается с root, созданным в suspended-состоянии, до его запуска; timeout/output overflow завершают всё дерево и возвращают типизированный отказ. Harness сам проверяет timeout, переполнение вывода и завершение дочернего процесса, пережившего root. Проект generated C# отключает nullable diagnostics и warnings-as-errors только для кода Dafny runtime; строгие настройки исходного Strogo и consumers остаются включены.

Семантическая Dafny-часть отдельно воспроизведена под Ubuntu 24.04 WSL2 из clean archive commit `8b53cdd`: Linux .NET SDK 10.0.400 сгенерировал те же 19 `.dfy` candidates, официальный Linux Dafny 4.11.0 дал 10 ожидаемых `Verified` и 9 ожидаемых proof refusals. SHA-256 Linux-архива `a46a9ff7cdd720f7955854c78e95df13f4cfe6b80691b05f8654fe19e8267179` совпал с digest GitHub Releases API; локальный `results.tsv` имеет SHA-256 `62901739cb6dde4c8d4bf833303cc3136af814973b2138643d9f69279e13c72d`. Это проверяет переносимость generated proof obligations и Dafny translation, но не Windows Job Object, полный orchestration script, generated consumers или ReadyToRun. Санитизированный public evidence package с `.dfy`, per-case logs и воспроизводящим driver хранится в [`artifacts/e05/linux-dafny-wsl2-8b53cdd/`](../artifacts/e05/linux-dafny-wsl2-8b53cdd/REPORT.md); исходные gitignored evidence сохранены в `artifacts/local-validation/e05/linux-dafny-8b53cdd-20260908/`.

Fold lowering создаёт compiler-owned index, bounded `while`, `decreases`, typed owner-prefix function, equality каждого candidate/owner input, exact prefix invariant, agent invariant, range и final obligations. Для checked-I64 sum используется отдельный `MathInt`/prefix-sum lemma path; record и sequence accumulators используют общий typed prefix. Стабильные obligation IDs и source map связывают отказ с fold, аргументом или proof path. Agent invariant не может удалить generated clauses.

`tools/Test-Fold-Discriminator.sh` дважды генерирует и byte-for-byte сравнивает полные output trees, запускает A/B/C, две allocation candidates, weak-invariant и три input-equivalence mutations, затем компилирует и исполняет generated C# consumers. Validation v0.2 отдельно связывает sum/allocation с owner digests и записывает version identity digest, compiler/harness source-tree digests, repository revision и dirty flags. В Ubuntu 24.04 WSL2 с Dafny 4.11.0 получено A=`Verified` (`21/0`), B=`Verified` (`22/0`), C=`Unproven(initial)` (`20/1`); обе allocation candidates дали `18/0`, а consumers совпали с reference evaluator на empty, `[4,3,1]` и `I64.MAX`. A/B означает только, что полный B не требовался для статуса `Verified` на одной frozen revision; стоимость и устойчивость proof не измерены. Публичный пакет: [`artifacts/e05/fold-v0.1-134856f/`](../artifacts/e05/fold-v0.1-134856f/REPORT.md).

Текущий lowering по-прежнему fail closed для непустых imports, helper contracts и nested folds. Нет proof receipt, release admission, package manifest, canonical ABI и публичного runtime facade. Поэтому успешная Dafny verification подтверждает exact outcome только относительно конкретного parsed owner bundle и pinned toolchain; она не подтверждает соответствие bundle человеческой спецификации и не разрешает прямой вызов generated метода вне approved domain.

## 10) Owner bundle v0.3 и v0.4

Owner artifact имеет отдельную версию `strogo.owner-bundle.v0.3` и обязательные поля `schemaVersion`, `bundleId`, `types`, `entryContracts`, `models`, `limits`. Неизвестные, пропущенные и повторные поля запрещены; canonical bytes строятся после сортировки именованных множеств по ID. Bundle digest отделён от raw hashes и других артефактов: `SHA256(UTF8("strogo.owner-bundle.v0.3/bundle\n") || canonicalBytes)`.

Каждый entry contract фиксирует:

- `id` и `functionRef`;
- точную сигнатуру с устойчивыми ID параметров и recursive `TypeRef`;
- Boolean `requires`;
- пустой `effects`;
- хотя бы один конкретный composite-capable witness допустимого входа;
- единственный `modelRef`; lowerer автоматически строит единственное exact postcondition `result == model(parameters)`, ручного `ensures` в schema больше нет.

Model имеет собственный ID, ту же сигнатуру и чистое типизированное expression tree. Closed expression set: `param`, constants, `i64.add/sub/le`, structural `eq`, `bool.not/and/or`, lazy `if`, `record.make/get`, `seq.empty/length/get/append`. `result`, `model.call`, helpers и imports отсутствуют. Limits bundle ограничивают общее число/глубину expression nodes, число witnesses и recursive witness value nodes; hard maximum последнего равен `4096`.

Parser проверяет типы и форму независимо от candidate module. `types` содержит ровно минимальное owner-semantic closure: корни берутся из entry/model signatures, полного expression AST и witness types, рёбра — из record fields и sequence element type. Даже пустой witness `Seq<Item,4>` требует declaration `Item`; missing, extraneous и recursive declarations отклоняются до binder/prover. До разбора тел parser канонически упорядочивает невалидные элементы по структурному ключу, который сохраняет `JsonValueKind`, и заранее проверяет повторные IDs. Witness затем вычисляется отдельным checked evaluator: `requires` обязан вернуть `true`, а model — определённое immutable `ModuleValue`. Конкретный witness доказывает непустоту формального domain и даёт replay oracle, но не доказывает totality; universal obligations остаются у Dafny.

Binder требует точного совпадения exports, `contractRef`, function/parameter IDs, порядка и типов, а для каждого owner-reachable named type — byte-equivalent canonical declaration в module. Дополнительный private module type разрешён: owner digest остаётся прежним, но module и generated proof identity меняются. Helpers и imports пока отклоняются. Scalar owner bundle по-прежнему принимает две различные реализации `x + 1` и `x - (-1)`. Composite `Summary` bundle принимает две разные DAG-реализации `[x,x+1]`; wrong order даёт replayed `Counterexample`, а partial model без range proof остаётся `Unproven`. Replay использует `Counterexample` только для успешно вычисленных, структурно различных owner/candidate значений. Исчерпание fuel возвращает `Timeout`, невалидные лимиты — `ToolError`, ошибка candidate evaluation — `CandidateError`; во всех трёх случаях отдельный `FailureCode` сохраняет причину, а counterexample отсутствует.

`OwnerBundleMigrator.MigrateV02ToV03` принимает только strict exact-outcome v0.2 artifact, до преобразования проверяет legacy schema/limits/expression budgets и прежние запреты partial arithmetic в `requires`/Boolean model expressions, считает также удаляемый `ensures`, удаляет единственную допустимую форму ручного `ensures`, добавляет `types: []`, `modelRef` и `maxWitnessValueNodes: "4096"`, затем выдаёт новые canonical bytes/digest. Публичная граница переводит malformed legacy structure в typed `owner-migrate:SchemaInvalid`, а не выпускает host exception. Прямой parse v0.2 возвращает `OwnerBundleMigrationRequired`; semantic approval не переносится. Boundary `4096` migrated witness value nodes принимается, следующий узел даёт `MigrationWitnessValueLimitExceeded` без output.

`strogo.owner-bundle.v0.4` сохраняет весь v0.3 expression set и добавляет versioned contract для fold: root-only owner fold model и proof-only `MathInt`, `math.*`, `seq.sum_i64`, `seq.prefix_sum_i64`, `proof.bound`, bounded `forall.sequence`. `maxProofEvaluationSteps` ограничен `1..262144`; static worst-case cost saturates как `>262144`, а независимый evaluator выдаёт `ProofEvaluationStepLimitExceeded` без partial result. Каждый `requires` и каждая invariant instantiation получают новый counter. Миграция v0.3→v0.4 копирует четыре прежних limit, добавляет `262144` и сохраняет canonical `requires` subtree byte-for-byte; approval автоматически не переносится.

## 11) Artefact и проверяемые границы checkpoint

- `ModulesCodec.Canonicalize(ModuleSource)` выдает каноническое представление.
- `ModulesCodec.SourceDigest` — `RawDigest` от canonical source.
- `ModulesCodec.Canonicalize(ModuleIr)` и `ModulesCodec.IrDigest` подготовлены для следующего шага.
- `ModulesDafnyLowerer.Lower(ModuleIr)` создаёт deterministic Dafny source, digest и source map для scalar profile.
- `OwnerBundleParser.Parse` создаёт неподлежащее публичной подделке canonical owner representation и digest.
- `OwnerContractBinder.Bind` связывает bundle с экспортами/сигнатурами module и независимо вычисляет witnesses.
- `ModulesDafnyLowerer.Lower(ModuleIr, OwnerBundle)` создаёт owner model, `requires` и exact-outcome `ensures`.
- На этом checkpoint покрыто:
  - формальная структура формата и синтаксическая строгость;
  - parser/typechecker и детерминированный IR module v0.2;
  - компиляция в детерминированный IR-слой;
  - ограниченная исполняемая reference-семантика scalar/`if`/local call;
  - исполняемая reference-семантика records и bounded sequences с рекурсивной проверкой внешних значений, index/capacity failures и stable loci;
  - verified Dafny→C# translation для total selector/local call и фактический ReadyToRun `win-x64` вызов selector;
  - owner bundle v0.3 с exact owner-semantic type closure, composite witnesses и explicit v0.2 migration;
  - scalar и composite exact-outcome proofs: по две разные реализации приняты, wrong outcome и слабый/partial owner domain отклонены;
  - deterministic witness replay: конкретное расхождение маркируется `Counterexample` только после независимого вычисления candidate и owner model;
  - owner bundle v0.4, bounded proof evaluator и явная v0.3→v0.4 migration;
  - root-only fold для scalar и composite accumulator, exact owner-prefix proof, stable obligations и generated .NET execution;
  - validation-only `dotnet-managed.v1` package с полным `strogo.validation-proof.v0.1`, canonical manifest/package identities и одним byte-identical DLL artifact для Windows/Linux consumers.

## 12) Проверяемые фикстуры

Для conformance сейчас доступны:
- `fixtures/modules-v0.2/math-add-valid.json`;
- `fixtures/modules-v0.2/scalar-ops-valid.json`, покрывающий lowering/type rules остальных scalar/bool operations;
- `fixtures/modules-v0.2/scalar-reference-valid.json` с различающими outcomes для `sub`, `le`, `eq`, `not` и полными truth tables `and`/`or`;
- `fixtures/modules-v0.2/math-invalid-op.json`;
- `fixtures/modules-v0.2/math-invalid-return-mismatch.json`.
- `fixtures/modules-v0.2/math-invalid-i64-plus.json`, `math-invalid-i64-leading-zero.json`, `math-invalid-i64-negative-zero.json`;
- `fixtures/modules-v0.2/composite-valid.json` и эквивалентный `composite-valid-shuffled.json`;
- `fixtures/modules-v0.2/composite-runtime-valid.json`, различающий порядок bounded sequence, record field access, capacity/index failures и рекурсивную проверку runtime input;
- `fixtures/modules-v0.2/composite-lowering-safe.json`, дающий проверяемые record/sequence construction, length/index и local call без скрытого I64 overflow;
- негативные `composite-invalid-record.json`, `composite-invalid-call-cycle.json`, `composite-invalid-seq-element.json`, `composite-invalid-recursive-type.json`, `composite-invalid-type-depth.json`, `composite-invalid-numeric-capacity.json` и `math-invalid-extra-value.json`.
- `if-valid.json` и эквивалентный `if-valid-shuffled.json`;
- `if-lazy-overflow.json`, различающий lazy branch semantics и ошибочный eager evaluator;
- `call-scalar-valid.json`, проверяющий локальные вызовы и общий step budget;
- `if-nested-safe.json` и `call-safe.json`, проверяющие recursive scalar lowering, три различимых исхода, generated symbol isolation и source map;
- `scalar-lowering-safe.json`, проверяющий Dafny translation и generated C# execution `i64.sub/le/eq` и `bool.const/not/and/or`;
- `owner-add-one-valid.json` и `owner-add-one-weak.json`, различающие достаточный и недостаточный owner domain;
- `owner-add-one-valid-v0.2.json` и `owner-add-one-weak-v0.2.json` как явные legacy inputs для миграции;
- `math-add-valid.json`, `math-add-alternative.json` и `math-add-wrong.json`, различающие две корректные реализации и неверный outcome при одном contract/model;
- `owner-composite-valid.json`, `owner-composite-module.json`, `owner-composite-alternative.json`, `owner-composite-wrong.json` и `owner-composite-partial.json`, различающие два correct DAG, replayed wrong outcome и partial model;
- `owner-empty-sequence-closure.json` и `owner-empty-sequence-module.json`, доказывающие, что declared element type входит в owner closure даже при пустом witness;
- негативные `if-invalid-hidden-capture.json`, `if-invalid-branch-type.json` и `if-invalid-nested-call-cycle.json`.
- `fold-sum-valid.json`, `fold-discriminator-a/b/c.json` и `owner-fold-discriminator-v0.4.json` для reference semantics, proof identity и A/B/C;
- `fold-allocation-primary.json`, `fold-allocation-alternative.json` и `owner-fold-allocation-v0.4.json` для двух разных composite candidates одного owner model;
- `owner-v03-proof-all-ops.json` для byte-identical миграции полного inherited proof subtree; дополнительные in-memory mutations проверяют hidden capture, nested fold/call, неизвестный proof opcode, out-of-scope binder, partiality, budgets, wrong branch, weak invariant и все fold inputs.

Скалярные копии для документационных целей размещены в `docs/fixtures/modules-v0.2`; полный исполняемый набор является каноническим в `fixtures/modules-v0.2`. Conformance также строит ограниченные in-memory cases для malformed/duplicate/non-ASCII JSON, invalid UTF-8, opcode metadata, call signatures, type/transport limits, defensive copies и переставленных ошибочных nodes.

## 13) Текущая граница

Этот checkpoint проверяет schema/type/call-graph, deterministic typed IR, lazy `if`, record/sequence semantics, один bounded left fold, owner bundle v0.4, scalar/composite exact-outcome proof и actual validation package `dotnet-managed.v1`. На clean `aaf2dc2` canonical proof closure из `290` файлов, full proof, cross-root checked build и два package roots воспроизводимы; один manifest/package/artifact triple прошёл validation-before-invocation и standalone consumer на Windows x64 и Linux x64 под WSL2. На clean `c254468` те же package identities были связаны с exact OS-specific .NET `10.0.11` runtime closures по `191` files и запущены напрямую без target SDK; corrupt, missing и multiversion runtime cases дали `EnvironmentUnavailable` до consumer. Clean `e222523` подтвердил A10 absent-row и package/report mix-up gates. Clean `ca5ae50` связал source-map-derived `summarize` и public entry с двумя фактическими JIT compilation events в каждой .NET OS row; decoy/missing events дали `JitEvidenceMissing`. Clean `2c87442` последовательно выполнил diagnostic public-ABI workload в обеих OS rows: по пять cold processes, отдельные `5000 + 5 × 10000` calls, peak memory и artifact sizes с явной границей `DiagnosticOnlyNoG06`. Публичное evidence: [`dotnet-package-aaf2dc2`](../artifacts/e06/dotnet-package-aaf2dc2/REPORT.md), [`dotnet-runtime-c254468`](../artifacts/e06/dotnet-runtime-c254468/REPORT.md), [`dotnet-matrix-e222523`](../artifacts/e06/dotnet-matrix-e222523/REPORT.md), [`dotnet-jit-ca5ae50`](../artifacts/e06/dotnet-jit-ca5ae50/REPORT.md), [`dotnet-performance-2c87442`](../artifacts/e06/dotnet-performance-2c87442/REPORT.md).

Профиль пока не получает итоговый статус `Portable`: runtime closure, A10, A11 и .NET-половина A12 подтверждены, включая deterministic `Unavailable`, package/report mix-up, bounded process-linked JIT events и two-OS diagnostic performance reports. JVM profile с HotSpot-половиной A12 и общий `strogo.portability-report.v0.1` отсутствуют. JIT evidence подтверждает compilation event, но не корректность машинного кода; performance observations не доказывают G06. Validation package не является production admission и не подключён к `TrustedModuleRuntime`. Также не реализованы import closure, helper contracts, nested folds и relational accumulator representation. G05/G06 и преимущество обязательного invariant по стоимости/устойчивости не измерены; Noita scanner остаётся development-only исследованием.

## 14) Канонический portability report E06R

`PortabilityReportV01` строит immutable validation-only report из typed evidence. Вызывающий код не передаёт `status`, `reasonCodes` или digest как authority: builder сам проверяет source revision, profile/platform closure, receipt identities, gate summaries и биекцию `vectorId`/`inputDigest`.

Canonical JSON имеет boundary-поля `purpose=validation-only`, `contractStatus=validation-fixture` и `admissionStatus=NotAdmittable`. `semanticDigest` включает нормативные identities, gate summaries и owner outcomes, но не включает timestamps, PID, raw logs, stderr или performance attachments. Markdown генерируется из той же модели и явно сохраняет границу fixed workload без production admission и без вывода G05/G06.

`PortabilityReportWriter.WriteNewDirectory` повторно валидирует report, пишет receipts в закрытую структуру, создаёт `sha256.txt` последним и выполняет no-overwrite rename. JVM receipts до отдельного E06A approval не создаются; synthetic report fixture не является фактическим cross-platform verdict.
