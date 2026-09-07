# Strogo Modules v0.2: структура, owner contracts, regions и IR

Этот документ закрепляет текущий E05 checkpoint: входную структуру, строгость формы, лексер, parser/typechecker, детерминированный IR, scalar reference semantics, скалярный owner bundle и ограниченный lowering в Dafny/C# с exact-outcome proof.

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

Для `if` инструкция дополнительно содержит две `RegionIr`. Каждая хранит типизированные локальные параметры, собственные инструкции и индекс результата. Инструкции ветвей не добавляются во внешний список, что сохраняет границу условного вычисления для последующего lowering.

Порядок деклараций types/functions/imports/exports и порядок узлов не участвуют в identity: codec сортирует их по стабильным ID. Порядок parameters и обычных `args` сохраняется. Для `record.make` codec и compiler сортируют пары `fieldId→arg` вместе, чтобы перестановка полей не меняла смысл или digest.

`ModuleParseResult` и `ModuleIr` создаются только внутри доверенных стадий parser/compiler. Canonical bytes возвращаются defensive copy, а compiler повторно сверяет source bytes и digest перед lowering. `ModuleIr` дополнительно хранит canonical bytes и domain-separated `FunctionCountDigest`.

## 8) Reference evaluator

`ModulesReferenceEvaluator.Invoke` исполняет провалидированный `ModuleIr` как ограниченную эталонную семантику. Текущий профиль поддерживает `I64`, `Bool`, все scalar opcodes, локальный `call` и `if`. Публичный вызов разрешён только для функции из `exports`; внутренние функции доступны только через `call`.

Арифметика `I64` проверяемая: переполнение даёт `evaluation:ArithmeticOverflow` с устойчивым locus узла. У `if` вычисляется только выбранная `RegionIr`; environment передаётся её локальным параметрам. Один `MaxSteps` действует на весь вызов вместе с вложенными regions и локальными calls и ограничен hard maximum `1_000_000`. Нулевой, отрицательный или превышающий hard maximum бюджет отклоняется до исполнения.

Evaluator намеренно отделён от будущей generated library: он нужен как executable reference oracle для differential checks lowering. Сейчас он получает уже скомпилированный IR и потому не является независимой проверкой parser/compiler. Не поддержаны records, sequences, imports, `fold`, canonical JSON ABI, owner contracts, proof/admission и machine-code package; такой opcode/type даёт явный `UnsupportedRuntimeOpcode`/`UnsupportedRuntimeType`, а непустой unresolved import closure — `UnsupportedRuntimeImports`, вместо частичного исполнения.

## 9) Scalar lowering в Dafny/C#

`ModulesDafnyLowerer.Lower` принимает только провалидированный `ModuleIr` и детерминированно создаёт Dafny source для `I64`, `Bool`, scalar operations, local calls и nested `if`. Functions, parameters и local values получают generated symbols `Fnnn`, `pnnn`, `vnnn`; пользовательские ID не вставляются в синтаксис и сохраняются отдельно в source map `entityId → generatedName + line`.

`I64` задаётся как native newtype с точным диапазоном `Int64`. Поэтому безопасный selector и безопасный local call проверяются и переводятся в C# методы с параметрами/результатами `long`. Арифметика без достаточного owner `requires` не получает скрытого предусловия: Dafny отклоняет `addOne(x)` на полном диапазоне как `Unproven`, потому что результат может выйти за границу newtype.

Перегрузка `ModulesDafnyLowerer.Lower(ModuleIr, OwnerBundle)` сначала выполняет строгую привязку bundle, затем добавляет owner model как Dafny `function`, переносит утверждённый `requires` и создаёт единственное допустимое postcondition `ensures result == M(parameters)`. Внутренние результаты `i64.add`/`i64.sub` модели материализуются типизированными `I64` let-выражениями: backend обязан доказать диапазон каждого шага, а не только итогового математического выражения.

Первый end-to-end smoke запускается командой:

```powershell
pwsh -File tools/Test-Modules-Dafny-Lowering.ps1 -RunDirectory artifacts/local-validation/e05/modules-dafny-lowering
```

Скрипт получает все Dafny candidates из production lowering, проверяет safe nested selector, local call и остальные scalar operators закреплённым Dafny 4.11.0, требует отказа unsafe arithmetic, проверяет две структурно разные реализации одного owner contract и обязательный отказ неверной реализации/слабого domain. Он публикует selector как ReadyToRun `win-x64`, проверяет PE native header и запускает отдельные generated consumers. Каждый внешний процесс имеет общий deadline 180 секунд и общий лимит stdout/stderr 1 MiB. Windows Job Object связывается с root, созданным в suspended-состоянии, до его запуска; timeout/output overflow завершают всё дерево и возвращают типизированный отказ. Harness сам проверяет timeout, переполнение вывода и завершение дочернего процесса, пережившего root. Проект generated C# отключает nullable diagnostics и warnings-as-errors только для кода Dafny runtime; строгие настройки исходного Strogo и consumers остаются включены.

Текущий lowering fail closed для records, sequences, непустых imports и helper contracts. Owner proof пока поддерживает только `I64`/`Bool`; arithmetic в `requires` и внутри Boolean model expressions отклоняется, чтобы strict contract evaluator не расходился с short-circuit definedness backend. Модель обязана быть отдельным чистым выражением. Нет proof receipt, human approval artifact, package manifest, canonical ABI и публичного runtime facade. Поэтому успешная Dafny verification устанавливает соответствие скалярного кандидата конкретному parsed owner bundle и его формальной модели, но не соответствие bundle человеческой спецификации и не неизменность будущего исполняемого пакета.

## 10) Скалярный owner bundle

Owner artifact имеет отдельную версию `strogo.owner-bundle.v0.2` и обязательные поля `schemaVersion`, `bundleId`, `entryContracts`, `models`, `limits`. Неизвестные, пропущенные и повторные поля запрещены; canonical bytes строятся после сортировки именованных множеств по ID. Bundle digest отделён от raw hashes и других артефактов: `SHA256(UTF8("strogo.owner-bundle.v0.2/bundle\n") || canonicalBytes)`.

Каждый entry contract фиксирует:

- `id` и `functionRef`;
- точную сигнатуру с устойчивыми ID параметров;
- Boolean `requires`;
- `ensures` ровно в канонической форме `eq(result, model.call(parameters))`;
- пустой `effects`;
- хотя бы один конкретный witness допустимого входа.

Model имеет собственный ID, ту же сигнатуру и чистое типизированное expression tree. Поддержанный скалярный фрагмент: `param`, `result`, `i64.const`, `bool.const`, `i64.add/sub/le`, `eq`, `bool.not/and/or`, `if`, `model.call` только в exact `ensures`. Limits bundle ограничивают общее число/глубину expression nodes и число witnesses и сами не могут превысить hard maxima реализации.

Parser проверяет типы и форму независимо от candidate module. До разбора тел он канонически упорядочивает невалидные элементы по инъективному структурному ключу, который сохраняет `JsonValueKind`, и заранее проверяет повторные IDs. Поэтому перестановка malformed models/contracts/witnesses и неизвестных witness arguments не меняет `code`, `entityId` и `details`. Witness затем вычисляется отдельным checked evaluator: `requires` обязан вернуть `true`, а model — определённое значение. Конкретный допустимый witness доказывает непустоту формального `requires` и сразу обнаруживает часть ошибок модели. Он не доказывает полноту или правильность domain относительно человеческой спецификации и не доказывает тотальность модели на всех допустимых входах; общая тотальность модели и соответствие кандидата проверяются Dafny под `requires`.

Binder требует точного совпадения exports, `contractRef`, function/parameter IDs, порядка и типов. Текущий срез отклоняет helpers, imports, records и sequences, потому что правила их контрактов ещё не реализованы. Один owner bundle `owner-add-one-valid.json` успешно связан с двумя различными кандидатами: `x + 1` и `x - (-1)`. Оба проходят `4 verified, 0 errors` и возвращают одинаковые граничные outcomes; `return x` не проходит exact postcondition. Bundle с `requires true` не проходит две range obligations — отдельно для модели и кандидата.

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
  - парсер + типовые валидации v0.2;
  - компиляция в детерминированный IR-слой;
  - ограниченная исполняемая reference-семантика scalar/`if`/local call;
  - verified Dafny→C# translation для total selector/local call и фактический ReadyToRun `win-x64` вызов selector;
  - первый exact-outcome proof: две разные реализации одного owner contract приняты, неправильная реализация и слабое предусловие отклонены.

## 12) Проверяемые фикстуры

Для conformance сейчас доступны:
- `fixtures/modules-v0.2/math-add-valid.json`;
- `fixtures/modules-v0.2/scalar-ops-valid.json`, покрывающий lowering/type rules остальных scalar/bool operations;
- `fixtures/modules-v0.2/scalar-reference-valid.json` с различающими outcomes для `sub`, `le`, `eq`, `not` и полными truth tables `and`/`or`;
- `fixtures/modules-v0.2/math-invalid-op.json`;
- `fixtures/modules-v0.2/math-invalid-return-mismatch.json`.
- `fixtures/modules-v0.2/math-invalid-i64-plus.json`, `math-invalid-i64-leading-zero.json`, `math-invalid-i64-negative-zero.json`;
- `fixtures/modules-v0.2/composite-valid.json` и эквивалентный `composite-valid-shuffled.json`;
- негативные `composite-invalid-record.json`, `composite-invalid-call-cycle.json`, `composite-invalid-seq-element.json`, `composite-invalid-recursive-type.json`, `composite-invalid-type-depth.json`, `composite-invalid-numeric-capacity.json` и `math-invalid-extra-value.json`.
- `if-valid.json` и эквивалентный `if-valid-shuffled.json`;
- `if-lazy-overflow.json`, различающий lazy branch semantics и ошибочный eager evaluator;
- `call-scalar-valid.json`, проверяющий локальные вызовы и общий step budget;
- `if-nested-safe.json` и `call-safe.json`, проверяющие recursive scalar lowering, три различимых исхода, generated symbol isolation и source map;
- `scalar-lowering-safe.json`, проверяющий Dafny translation и generated C# execution `i64.sub/le/eq` и `bool.const/not/and/or`;
- `owner-add-one-valid.json` и `owner-add-one-weak.json`, различающие достаточный и недостаточный owner domain;
- `math-add-valid.json`, `math-add-alternative.json` и `math-add-wrong.json`, различающие две корректные реализации и неверный outcome при одном contract/model;
- негативные `if-invalid-hidden-capture.json`, `if-invalid-branch-type.json` и `if-invalid-nested-call-cycle.json`.

Скалярные копии для документационных целей размещены в `docs/fixtures/modules-v0.2`; полный исполняемый набор является каноническим в `fixtures/modules-v0.2`. Conformance также строит ограниченные in-memory cases для malformed/duplicate/non-ASCII JSON, invalid UTF-8, opcode metadata, call signatures, type/transport limits, defensive copies и переставленных ошибочных nodes.

## 13) Текущая граница

Этот checkpoint проверяет schema/type/call-graph, deterministic typed IR, lazy `if` semantics в reference evaluator и скалярный exact-outcome proof относительно отдельного owner bundle. Он ещё не реализует records/sequences lowering, `fold`, разрешение import closure, helper contracts, human approval/admission, package binding, runtime precondition facade или второе требуемое E05 семейство. Поэтому результат не является готовой исполняемой библиотекой Strogo и не закрывает целиком AC1–AC7 или цели G01–G06.
