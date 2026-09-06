# Strogo Modules v0.2: структура, нотация, лексер, парсер и IR

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
- порядок полей в объекте влияет на форматный ввод только через canonical-канонизацию, а не через семантику.

## 2) Идентификаторы

Идентификатор любого именованного элемента проходит паттерн:
`^[A-Za-z][A-Za-z0-9_.-]{0,63}$`.

`id` используется как ссылочный символ в AST и для диагностики. Он не эквивалентен имени файла/текста/хост-идентификатору и не используется как API-сигнатура runtime напрямую.

## 3) Типы

`TypeRef` поддерживает:
- примитивы: `I64`, `Bool`;
- записи: `"Record"` с `name`;
- последовательности: `{"kind":"Seq","elementType":<TypeRef>,"capacity":N}`.

Ограничения v0.2:
- именованный `Record` должен существовать в `types`;
- вложенность `Seq(Seq(T))` запрещена;
- `capacity` имеет верхнюю границу в ядре лимитов;
- циклические типы и рекурсия типа не разрешены на уровне текущего checkpoint.

## 4) Нотация body и узлов

Функция имеет:

- `parameters`: список `{id,type}`;
- `returnType`;
- `body` с:
  - `parameters` (должны совпасть по порядку с `function.parameters`);
  - `nodes` — список инструкций с полями
    - `id`, `op`, `type`, `args`, `value`.
  - `result` — `id` узла результата.

Проверки body:
- граф зависимостей узлов должен быть DAG;
- все узлы в графе достижимы из `result`;
- ссылка `args` разрешена только на параметр или ранее объявленный узел;
- арность и типы аргументов соответствуют `op`;
- `result` должен существовать и иметь тип равный `returnType`.

Поддержанные на этапе v0.2 `op`:
- `i64.const`, `bool.const` (арность 0);
- `i64.add`, `i64.sub`, `i64.le`, `i64.eq`;
- `bool.not`, `bool.and`, `bool.or`.

Тип ноды должен соответствовать операции:
- арифметика — `I64`,
- сравнительные — `Bool`,
- булевые — `Bool`.

## 5) Лексер

Лексер выполняет только токенизацию и поверхностный guard:
- поток bytes парсится как JSON;
- отслеживается глубина структуры;
- формируется последовательность внутренних `ModuleToken` для последующей трассировки;
- ошибки лексера — отдельный слой `ModuleLexerException`.

Лексер не выполняет типовую/схематическую проверку AST, только помогает локализовать ошибки формата.

## 6) Парсер

Парсер выполняет строгое чтение с нормализацией:
- `ParseStrict` через `CanonicalJson` с лимитами из `StrogoLimits`;
- единое правило `CheckObject`: только ожидаемые имена полей;
- единые ошибки через `ModuleException` (`stage`, `code`, `entityId`, `details`);
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
  - тип и литерал (`value`) для констант.

`ModuleIr` дополнительно хранит `CanonicalBytes` и `FunctionCountDigest` (через canonical hash).

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
- `fixtures/modules-v0.2/math-invalid-op.json`;
- `fixtures/modules-v0.2/math-invalid-return-mismatch.json`.

Дубликаты для документационных целей размещены в `docs/fixtures/modules-v0.2`.
