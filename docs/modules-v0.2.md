# Strogo Modules v0.2: структура, составные значения, вызовы и IR

Этот документ закрепляет checkpoint для шага 1–4: входная структура, строгость формы, лексер, парсер и компиляция в промежуточный код.

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

`value` обязателен только у констант. Лишнее `value: null` у другой операции является ошибкой schema, а не допустимым default. `call` проверяет сигнатуру и общий локальный call graph; рекурсия и взаимная рекурсия запрещены. Разрешение вызовов через import closure относится к следующему checkpoint.

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
- неупорядоченные декларации и nodes валидируются по ordinal ID, поэтому первая статическая ошибка не зависит от их transport order;
- canonical-байты создаются отдельной стадией кодека.

Ключевые коды отказа:
- `SchemaVersionMismatch`, `TypeLimitExceeded`, `FunctionLimitExceeded`,
- `Duplicate*`, `UnsupportedOpcode`, `TypeMismatch`, `ReturnTypeMismatch`,
- `CycleDetected`, `UnreachableNode`, `ExportedFunctionMissing`,
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

Порядок деклараций types/functions/imports/exports и порядок узлов не участвуют в identity: codec сортирует их по стабильным ID. Порядок parameters и обычных `args` сохраняется. Для `record.make` codec и compiler сортируют пары `fieldId→arg` вместе, чтобы перестановка полей не меняла смысл или digest.

`ModuleParseResult` и `ModuleIr` создаются только внутри доверенных стадий parser/compiler. Canonical bytes возвращаются defensive copy, а compiler повторно сверяет source bytes и digest перед lowering. `ModuleIr` дополнительно хранит canonical bytes и domain-separated `FunctionCountDigest`.

## 8) Artefact и проверяемые границы checkpoint

- `ModulesCodec.Canonicalize(ModuleSource)` выдает каноническое представление.
- `ModulesCodec.SourceDigest` — `RawDigest` от canonical source.
- `ModulesCodec.Canonicalize(ModuleIr)` и `ModulesCodec.IrDigest` подготовлены для следующего шага.
- На этом checkpoint покрыто:
  - формальная структура формата и синтаксическая строгость;
  - парсер + типовые валидации v0.2;
  - компиляция в детерминированный IR-слой.

## 9) Проверяемые фикстуры

Для conformance сейчас доступны:
- `fixtures/modules-v0.2/math-add-valid.json`;
- `fixtures/modules-v0.2/scalar-ops-valid.json`, покрывающий lowering/type rules остальных scalar/bool operations;
- `fixtures/modules-v0.2/math-invalid-op.json`;
- `fixtures/modules-v0.2/math-invalid-return-mismatch.json`.
- `fixtures/modules-v0.2/math-invalid-i64-plus.json`, `math-invalid-i64-leading-zero.json`, `math-invalid-i64-negative-zero.json`;
- `fixtures/modules-v0.2/composite-valid.json` и эквивалентный `composite-valid-shuffled.json`;
- негативные `composite-invalid-record.json`, `composite-invalid-call-cycle.json`, `composite-invalid-seq-element.json`, `composite-invalid-recursive-type.json`, `composite-invalid-type-depth.json`, `composite-invalid-numeric-capacity.json` и `math-invalid-extra-value.json`.

Скалярные копии для документационных целей размещены в `docs/fixtures/modules-v0.2`; полный исполняемый набор является каноническим в `fixtures/modules-v0.2`. Conformance также строит ограниченные in-memory cases для malformed/duplicate/non-ASCII JSON, invalid UTF-8, opcode metadata, call signatures, type/transport limits, defensive copies и переставленных ошибочных nodes.

## 10) Текущая граница

Этот checkpoint проверяет schema/type/call-graph и deterministic IR. Он ещё не реализует `if`, `fold`, вложенные regions, разрешение import closure, contracts или proof lowering. Поэтому он не доказывает exact outcome и пока не может выразить обязательные ClampSeries/OrderedExactAllocation из E05.
