# E05: owner-owned composite types и canonical exact models v0.3

## 0. Метаданные
- Тип (профиль): QUEST / language-contract / proof-boundary.
- Владелец: пользователь — смысл типов, requires и exact model; агент — schema, parser, evaluator, binder и lowering после approval.
- Масштаб: large.
- Целевое семейство / behavior baseline: `strogo.module.v0.2`, scalar owner checkpoint `e08f1a4`, composite lowering `c5ca6b4`.
- Поверхность: Codex + локальные .NET library/CLI artifacts.
- Effective runtime: Не применимо — изменение детерминированного language/toolchain contract.
- Eval baseline / evidence: scalar owner `4 verified`; composite structural candidate `4 verified`; evidence сохраняется новым run directory под `artifacts/local-validation/e05/`.
- Целевой релиз / ветка: `main`; периодический push checkpoint commits отдельно разрешён владельцем, merge/release не входят в approval.
- Ограничения: Dafny 4.11.0, .NET SDK 10.0.400; imports, helper contracts и `fold` не входят в этот срез.
- Связанные ссылки: [E05 SPEC](2026-09-06-composable-verified-modules-v0.2.md), [admission amendment](2026-09-07-e05-two-stage-admission-amendment.md), [K-E05-063](../docs/knowledge-log.md).
- Instruction stack / профиль: central `creator-vibe-lens`, `model-behavior-baseline`, `quest-governance`, `collaboration-baseline`, `tool-execution-baseline`, `quest-mode`, `spec-linter`, `spec-rubric`, `review-loops`, профиль `product-system-design`, governance `commit-message-policy`; поверх них локальный `AGENTS.md` Strogo.

## 1. Overview / Цель
Сделать composite owner contract действительно принадлежащим владельцу: утверждённый bundle должен фиксировать полную форму records/sequences и единственный exact model, а candidate module не должен иметь возможность менять смысл именованного типа.

Эта поправка нормативно заменяет только формат owner bundle и его binding/lowering из E05 §6.2/§6.6. Module schema `strogo.module.v0.2`, admission amendment и исходные E05 goals/AC сохраняются.

Outcome contract:
- Success means: owner bundle v0.3 самостоятельно фиксирует минимальное owner-semantic type closure, requires, witnesses и model; binder требует exact equality всех owner-reachable types; две composite реализации доказывают один exact outcome, а reachable type/model/candidate mutations fail closed.
- Итоговый артефакт / output: strict `strogo.owner-bundle.v0.3`, canonical composite values/expressions, обновлённые parser/evaluator/binder/Dafny lowering, fixtures и proof evidence.
- Stop rules: неизвестный тип/op/field, partial model, type drift, слабый requires, forged exact outcome, `Unproven`, `Timeout` или `ToolError` не допускаются как Verified.

## 2. Текущее состояние (AS-IS)
- `strogo.owner-bundle.v0.2` содержит contracts/models/limits, но не содержит определения records.
- Owner parser принимает только `I64`/`Bool`, witness evaluator возвращает `OwnerScalarValue`, а binder отклоняет module с types.
- Composite candidate lowering уже генерирует Dafny datatypes и bounded sequence subset types, но проверяет только structural/range obligations без owner exact outcome.
- Если просто разрешить имя `Summary` в v0.2, форму его полей продолжит задавать candidate module; semantic approval bundle digest эту форму не покроет.
- Публичный adversarial review в [Strogo thread](https://getpostingboard.dev/b/t/e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e) выявил противоположный риск раннего draft: full module type universe заставляет человека повторно утверждать private/unreachable implementation types. В replies `58694e13-005a-458a-87d6-4c268233b8a7` (#9543) и `a4e66bbe-3a0f-42bb-85d5-46e471bc9515` (#9545) Помощник Архитектора дал различающие counterexamples и критерий: closure безопасно только при полном списке semantic edges, unresolved reference fail closed, обходе declared TypeRef даже при пустом witness value и positive control на недостижимом типе. Текущий v0.3 имеет закрываемый набор edges; вывод включён в §6.2 и C-AC1/C-AC3/C-AC8.

## 3. Проблема
Именованный composite type является частью утверждаемого смысла, но сейчас отсутствует в owner-owned identity. Разрешить composite signatures без исправления формата означало бы оставить агенту неявную возможность менять контракт через type declaration.

## 4. Цели дизайна
- Включить в canonical owner identity ровно замыкание типов, необходимое для интерпретации owner contract, не включая приватные детали candidate implementation.
- Оставить ровно один способ задать exact outcome: `modelRef`, без дублирующего свободного `ensures`.
- Использовать одну immutable value algebra для witnesses, reference evaluator и model evaluator.
- Проверять totality всех composite operations на всём `requires` domain через Dafny.
- Сохранить deterministic diagnostics, canonical ordering и отсутствие source IDs в generated syntax.

## 5. Non-Goals (чего НЕ делаем)
- `fold`, quantifiers, ghost `MathInt`, imports и helper contracts.
- Human approval/admission implementation: он остаётся за отдельной неподтверждённой поправкой.
- Generated composite .NET ABI/consumer и ReadyToRun package.
- Автоматический вывод owner model из candidate.
- Поддержка owner-bundle v0.2 как второго runtime dialect.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности
- `OwnerBundleParser` → strict v0.3 schema, type/value/expression syntax и deterministic validation.
- `OwnerBundleCodec` → canonical bytes/digest owner types, contracts, witnesses и models.
- `OwnerContractEvaluator` → independent checked evaluation composite witnesses/models.
- `OwnerContractBinder` → exact equality owner-closure declarations в module и entry/model signature binding.
- `ModulesDafnyLowerer` → generated owner model, requires и automatic exact postcondition над теми же generated type symbols.
- Conformance/harness → positive alternatives, wrong candidate/model/type mutations и bounded Dafny evidence.

### 6.2 Детальный дизайн

#### Version и migration boundary

Новая schema имеет literal `strogo.owner-bundle.v0.3` и обязательные поля:

```text
schemaVersion, bundleId, types, entryContracts, models, limits
```

v0.2 не принимается parser v0.3 и не является alias. Отдельный pure API `OwnerBundleMigrator.MigrateV02ToV03(ReadOnlySpan<byte>)` преобразует только artifact, полностью принятый strict v0.2 parser/semantics. До построения output он считает каждый scalar witness argument как один будущий корневой `Value`; если сумма по bundle превышает `4096`, возвращается `MigrationWitnessValueLimitExceeded` с `actual,max` и никакие bytes не выдаются. Иначе migrator меняет `schemaVersion`, добавляет `types: []`, заменяет exact `ensures = eq(result, model.call(modelRef, ordered parameters))` полем entry `modelRef`, копирует без смыслового изменения IDs, signatures, requires, effects, witnesses и models, копирует три прежних limits и всегда добавляет `maxWitnessValueNodes: "4096"`. Любая иная v0.2 ensures shape или invalid semantics даёт typed refusal без output. API возвращает canonical v0.3 bytes с новым digest, не пишет файл и не создаёт approval; вызывающий инструмент обязан выбрать новый output path явно. Старый approval не переносится. Миграционный тест фиксирует полные golden canonical bytes для `owner-add-one-valid.json` и их вычисленный lowercase SHA-256 domain digest как checked-in constant; повторный запуск обязан совпасть byte-for-byte.

Bundle digest вычисляется как `SHA256(UTF8("strogo.owner-bundle.v0.3/bundle\n") || canonicalBytes)`. Миграция принимает только artifact, полностью прошедший strict v0.2 parser/semantics; частично разобранный JSON не мигрируется.

#### Owner-owned semantic type closure

`types` использует ту же закрытую форму, ID rules, canonical ordering, recursion/depth/capacity checks и `TypeRef`, что module v0.2, но содержит ровно минимальное owner-semantic closure. Closure вычисляется только из owner bundle и не зависит от candidate module или его call graph.

Корни closure — каждый declared `TypeRef`, встречающийся в ordered entry/model signatures, полном `requires` и model expression AST (включая обязательный result `type`, `recordType`, `elementType` и `capacity`) и каждом рекурсивном witness `Value`. Обход идёт по declared type shape, а не по фактически непустым sample values: `Seq<T,4>` включает `T` даже при witness `[]`. Рёбра closure — named record → каждый declared field `TypeRef`, sequence → `elementType`; capacity является частью identity самой sequence TypeRef. Обход deterministic depth-first: root occurrences рассматриваются в canonical owner-byte order, record fields — по ordinal `fieldId`; visited/output канонизируются по type ID. Scalar `I64`/`Bool` не создают declarations.

Каждая named ссылка обязана разрешаться ровно в одну declaration из `bundle.types`; unresolved reference, recursion, depth/capacity violation дают typed refusal. После обхода `bundle.types` обязано быть exact set closure: missing declaration даёт `OwnerTypeClosureMissing`, extra declaration, не достижимая ни от одного owner root, — `OwnerTypeClosureExtraneous`. Эти отказы возникают при owner semantic validation до binder/witness/prover. Поэтому произвольное добавление owner type не создаёт второй допустимой сериализации того же контракта.

Binder требует byte-equal canonical declaration для каждого closure type ID в module. Missing declaration либо отличие record fields, nested TypeRef или capacity даёт `OwnerTypeClosureMismatch` до witness/prover. Дополнительные module types с другими ID разрешены: они входят в module digest, lowering и proof identity, но не в owner digest и не требуют human semantic re-approval. Candidate call graph, private signatures и типы его внутренних nodes также не входят в owner closure; их корректность относительно owner model устанавливает proof. В текущем v0.3 нет refinement types, aliases, named constants, helpers, imports, reflection, dynamic lookup или effects. Добавление любого такого semantic edge требует новой schema/SPEC, а неизвестная ссылка никогда не обрезает closure.

TypeRef имеет единственную JSON-форму:

```text
"I64" | "Bool" | "<record-id>" |
{"kind":"Seq","elementType":<TypeRef>,"capacity":"<canonical decimal 0..256>"}
```

`types` содержит `0..256` declarations и после closure validation не содержит недостижимых owner declarations. `entryContracts` и `models` содержат `1..128` элементов и имеют равную cardinality из-за one-to-one `modelRef`; каждый entry содержит `1..maxWitnessesPerEntry` witnesses. Нулевая cardinality `entryContracts`, `models` или `witnesses` запрещена, поэтому пустой bundle и непоказанный `requires` domain не проходят parser/semantics. Число parameters каждой entry/model signature — `0..512`, как в module function limit.

`limits` имеет ровно поля `maxExpressionNodes`, `maxExpressionDepth`, `maxWitnessesPerEntry`, `maxWitnessValueNodes`; каждое значение — canonical non-negative decimal string, не превышающий соответственно hard maximum `1024`, `32`, `32`, `4096`. Все четыре значения обязаны быть не меньше `1`.

JSON object property order не имеет смысла: duplicate keys запрещены, parser принимает любую перестановку известных properties, codec пишет их в единственном ordinal order. Set-like arrays `types`, record `fields`, `entryContracts`, `models`, `witnesses`, witness `arguments` канонизируются по соответствующему ID. Ordered signatures/parameters, ordinary expression `args` и sequence items сохраняют порядок. `record.make` сортирует связные пары `(fieldId,arg)` вместе; сортировка только `fieldIds` запрещена.

#### Entry contract без свободного ensures

Entry contract v0.3 имеет ровно:

```text
id, functionRef, parameters, returnType, requires, effects, witnesses, modelRef
```

`id`, `functionRef`, `modelRef` — ID strings; `parameters` — ordered array объектов ровно `id,type`; `returnType` — TypeRef; `requires` — expression; `effects` обязан быть JSON `[]`; `witnesses` — array witness objects. Model имеет ровно `id,parameters,returnType,body` с теми же JSON-типами signature и expression body. `modelRef` ссылается ровно на одну model с exact той же ordered signature. Lowerer всегда создаёт `ensures result == M(parameters)`; иной postcondition невозможно записать в schema. Каждая model используется ровно одним entry contract. Helpers остаются запрещены.

#### Composite values

Каждый witness имеет ровно поля `id`, `arguments`; каждый argument — ровно `parameterId`, `value`, причём все parameters представлены ровно один раз и arguments канонически сортируются по `parameterId`. `Value` — закрытый рекурсивный JSON object ровно с полями `type`, `value`, где `type` — полный TypeRef, а `value` имеет одну форму, определяемую типом:

- `I64` → canonical decimal string;
- `Bool` → JSON boolean;
- record → array объектов ровно `{"fieldId":"<id>","value":<Value>}`, ordinal-sorted по `fieldId`, каждый declared field ровно один раз;
- sequence → array `<Value>`, порядок значим, `length <= capacity`, каждый item рекурсивно совпадает с element type.

Пустой record кодируется пустым массивом. Unknown JSON property, unknown/missing/duplicate field, wrong nested type/capacity или malformed I64 отклоняется с locus witness/parameter/field/item. `maxWitnessValueNodes` считает корневой `Value` каждого argument и каждый вложенный record field/sequence item; превышение проверяется по всему bundle до evaluation.

#### Canonical expression AST

Каждый expression является закрытым JSON object, содержит `op`, полный `type` и только следующие opcode-specific поля. `args` всегда JSON array ровно указанной длины; отсутствие `args` допускается только у `param` и constants. Unknown property, лишний/пропущенный argument или поле другого opcode отклоняются до semantic validation.

| Op | Поля / args | Result и obligation |
| --- | --- | --- |
| `param` | `id` string | exact parameter type |
| `i64.const`, `bool.const` | `value` canonical I64 string / JSON bool | literal |
| `i64.add`, `i64.sub` | `args` length 2, I64 | I64 range на каждом пути |
| `i64.le`, `eq` | `args` length 2, compatible values | Bool; `eq` разрешён для Bool/I64/record/sequence |
| `bool.not` | `args` length 1, Bool | strict operand definedness |
| `bool.and`, `bool.or` | `args` length 2, Bool | strict: оба args defined |
| `if` | `args` length 3: condition, then, else | lazy branches, equal result type |
| `record.make` | `recordType`, `fieldIds`, `args` той же длины | fields exactly once, canonical pairs |
| `record.get` | `fieldId`, `args` length 1 record | declared field type |
| `seq.empty` | `elementType`, `capacity`, `args: []` | exact bounded type; capacity canonical decimal string |
| `seq.length` | `args` length 1 sequence | I64 |
| `seq.get` | `args` length 2: sequence,index | prove `0 <= index < length` on every reachable path |
| `seq.append` | `args` length 2: sequence,item | prove `length < capacity` |

`result` удалён: v0.3 не хранит ручной `ensures`. `record.make` field/arg pairs canonicalize together exactly as module nodes. `bool.and/or` lower to generated strict helper functions so Dafny definedness cannot depend on short-circuit syntax. `if` остаётся единственным способом guard partial expression.

`result`, `model.call`, `call`, `functionRef` и любые helper/submodel reference fields в v0.3 expression AST отсутствуют и отклоняются как unknown op/property во всех positions: каждый entry ссылается на одну самостоятельную model только полем entry `modelRef`, а helpers/submodels остаются отдельным расширением. Все expression nodes across bundle ограничены `maxExpressionNodes`, depth — `maxExpressionDepth`; composite witness total item count также входит в новый `maxWitnessValueNodes` с hard maximum 4096.

#### Shared immutable value algebra

`OwnerScalarValue` заменяется `ModuleValue`/internal typed value codec: `ModuleI64`, `ModuleBool`, `ModuleRecord`, `ModuleSequence`. Parser создаёт defensive immutable collections. Evaluator перед каждым operation рекурсивно сверяет expected type against owner semantic closure. Checked I64 overflow, sequence range/capacity и unknown field дают typed error; никакой default value не создаётся.

#### Proof contract

Owner model и candidate используют один generated type-symbol table. Owner type IDs/field IDs остаются только в source map. Для каждого entry Dafny обязан доказать:

1. `requires` well-formed/total;
2. model total и каждый I64/sequence operation defined под `requires`;
3. candidate operation obligations;
4. `result == model(parameters)`;
5. каждый witness независимо проходит requires/model evaluator, затем candidate reference evaluator; его candidate result сравнивается с model result структурно.

Если replay конкретного owner witness воспроизводит расхождение candidate/model, результат — `Counterexample` с canonical witness value и witness ID. Если все witnesses совпали, но Dafny не доказал universal obligation, результат — `Unproven`; failed assertion без извлечённого и повторно воспроизведённого input запрещено называть counterexample. Witness, на котором `requires` false или owner model partial, отклоняет сам bundle как `RequiresWitnessRejected` / `ModelUndefinedAtWitness` до candidate proof. Partiality модели вне известных witnesses остаётся `Unproven` с model obligation.

| Наблюдение | Replay condition | Итоговый outcome |
| --- | --- | --- |
| Owner witness не проходит `requires` или model evaluation | известный owner witness воспроизведён | typed bundle refusal `RequiresWitnessRejected` / `ModelUndefinedAtWitness`; proof не запускается |
| Candidate расходится с model на owner witness | requires/model/candidate успешно вычислены на exact canonical witness | `Counterexample` с witness ID/value |
| Solver предоставляет input для model/candidate obligation | input проходит independent replay и воспроизводит нарушение | `Counterexample` с canonical replayed input |
| Dafny assertion не доказан | input отсутствует, не проходит replay или не воспроизводит нарушение | `Unproven`; unverifiable solver assignment не публикуется как witness |
| Model partial только вне owner witnesses | конкретного воспроизведённого input нет | `Unproven` с model obligation |
| Verifier исчерпал budget / сломался | replay не заменяет требуемый universal proof | `Timeout` / `ToolError` |

Safe composite family: функция без helpers строит record с bounded sequence и возвращает его. Alternative использует другие scalar steps, но тот же exact record/sequence. Wrong candidate меняет order/item/field и должен получить postcondition failure. Partial model с `seq.get` без range requires обязан быть `Unproven`.

Generated source обязан быть byte-identical для одного typed module+bundle; изменение owner type/model либо candidate меняет соответствующий digest. Не заявляется proof эквивалентности evaluator и lowering: differential witness checks лишь уменьшают риск TCB.

Visual planning artifact: Не применимо — UI нет; review surface состоит из canonical projection типов, requires, witness и exact model.

UI test video evidence: Не применимо — UI automation отсутствует.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| C-S1 | Утвердить bundle с record/sequence | Projection показывает полную форму types и exact model | canonical snapshot/digest | C-AC1 |
| C-S2 | Проверить две composite реализации | Обе Verified против одного bundle и дают равные witness outcomes | Dafny + evaluator report | C-AC2 |
| C-S3 | Изменить field/capacity/order/model/candidate | Новый digest, typed refusal, воспроизведённый `Counterexample` либо `Unproven` без выдуманного input | mutation/replay matrix | C-AC3 |
| C-S4 | Задать partial sequence model | `Unproven` с stable model obligation; candidate не считается неверным автоматически | negative proof report | C-AC4 |
| C-S5 | Передать malformed composite witness | Parser/evaluator отказывает до prover с field/item locus | negative fixture | C-AC5 |
| C-S6 | Подать v0.2 bundle | Явная migration-required ошибка; migration создаёт новый v0.3 digest | migration report | C-AC6 |
| C-S7 | Изменить только private/unreachable module type `U` | Owner digest и human contract стабильны; module/proof identity меняются и требуют нового proof | identity/proof report | C-AC8 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| owner bundle v0.2 | parse v0.3 | `OwnerBundleMigrationRequired` | no implicit conversion | старое evidence сохраняется |
| v0.2 scalar bundle | migrate | canonical v0.3 с `types: []` и новым digest | non-exact old ensures → refusal | approval не переносится |
| v0.3 bundle + matching module | bind | Bound | owner closure missing/extra declaration или reachable module type drift → refusal | до witnesses/prover |
| Bound composite entry | lower/verify | Verified или typed proof outcome | partial model → Unproven | exact outcome automatic |
| Verified candidate A | candidate B same bundle | отдельный proof identity | wrong B → postcondition failure | bundle immutable |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Владение record definitions | agent | Exact minimal owner-semantic type closure внутри bundle | 0.99 | Full module universe заставляет владельца утверждать private implementation types | Нет; closure edges закрыты этой SPEC |
| Exact outcome syntax | agent | `modelRef`, automatic ensures; свободный ensures удалить | 0.96 | Дублирование даёт две формы одной гарантии | Нет |
| Versioning | agent | owner bundle v0.3 + explicit migration | 0.94 | Silent v0.2 reinterpretation меняет approved bytes | Нет |
| Value algebra | agent | Reuse immutable `ModuleValue` | 0.90 | Две семантики значений расходятся | Нет |
| Fold/quantifiers | agent | Отдельный следующий amendment | 0.95 | Одновременное расширение скрывает proof gaps | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Owner schema | v0.2 scalar | v0.3 with `types`, `modelRef`, composite values | explicit v0.2 migration | canonical fixtures |
| Type identity | candidate module only | byte-equal owner closure declarations в module; extra private module types допустимы | reachable mismatch fails; private drift re-proves | closure/identity mutations |
| Exact outcome | manual ensures shape | automatic equality to modelRef | migrator extracts only exact v0.2 form | proof source review |
| Witness evaluator | OwnerScalarValue | shared immutable composite values | API migration | differential cases |
| Dafny owner lowering | scalar symbols | shared record/sequence symbols | scalar remains expressible in v0.3 | safe/wrong/partial runs |

## 7. Бизнес-правила / Алгоритмы
1. Exact owner-semantic type closure входит в bundle digest и не выводится из candidate.
2. Drift owner-reachable module type требует нового bundle и human semantic approval; drift только private/unreachable candidate type меняет module/proof identity, но не owner digest.
3. Exact outcome имеет одну форму: automatic equality with referenced model.
4. Witness подтверждает непустоту requires только для конкретного input; totality доказывается отдельно.
5. `Verified` допустим только если model и candidate определены на всей requires domain.
6. Strict Boolean operations не скрывают partial operands; lazy definedness выражается только `if`.
7. Composite equality структурная: record type+fields и sequence type/capacity/order/items.
8. Parser/evaluator/lowerer не добавляют hidden range requires.
9. v0.2 approval не применяется к migrated v0.3 digest.

## 8. Точки интеграции и триггеры
- `OwnerBundleParser.Parse`, `OwnerBundleCodec.Canonicalize/BundleDigest`.
- `OwnerContractSemantics.Validate`, `OwnerContractEvaluator`, `OwnerContractBinder.Bind`.
- `ModulesDafnyLowerer.Lower(ModuleIr, OwnerBundle)`.
- Modules conformance и bounded Dafny harness.

## 9. Изменения модели данных / состояния
- `OwnerBundle` получает immutable exact-closure `Types` и schema v0.3.
- `OwnerEntryContract.Ensures` удаляется, `ModelRef` становится parsed required field; expression-level `model.call` также удаляется.
- Witness arguments/model result переходят с `OwnerScalarValue` на immutable composite value.
- Limits получает `MaxWitnessValueNodes`.
- Canonical bytes/digest v0.3 несовместимы с v0.2 по design.

## 10. Миграция / Rollout / Rollback
- Добавить pure migration v0.2→v0.3 только для exact scalar bundles; результат всегда новый artifact.
- Перенести scalar fixtures и сохранить старые bytes/evidence как historical.
- Сначала parser/codec/evaluator, затем binder/lowering, затем positive/negative proof matrix.
- Rollback реализации: вернуть v0.2 code checkpoint; новые v0.3 artifacts не интерпретировать как v0.2.

## 11. Тестирование и критерии приёмки

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| C-AC1 Bundle владеет composite смыслом | canonical roundtrip; shuffled declarations/fields same digest; `f(xs:Seq<T,4>)` с witness `[]` всё равно включает `T`; reachable field/capacity drift changes owner digest; unreachable owner declaration rejected | projection/closure review | owner report | — |
| C-AC2 Две реализации дают exact outcome | evaluator witnesses + two Dafny Verified candidates against one bundle | inspect generated model/ensures | proof report | — |
| C-AC3 Неверные изменения fail closed | swapped items, wrong field, changed reachable type/model/candidate; missing owner closure type; owner type `T` начинает ссылаться на `U`, после чего missing/mutated `U` отклоняется либо меняет digest; candidate mismatch на owner witness replay | stable obligation/locus; `Counterexample` содержит воспроизведённый witness | mutation/replay report | — |
| C-AC4 Partial/strict definedness не маскируется | `seq.get` without range; weak requires; overflow; `false and <partial>` и `true or <partial>` остаются partial; соответствующие guarded `if` с partial expression в невыбранной ветке проходят evaluator+Dafny | inspect generated strict helpers; distinguish `Unproven`/`Counterexample`; запрет raw `&&`/`||` lowering | negative proof report | — |
| C-AC5 Composite witnesses и closed AST strict | missing/extra/duplicate field, wrong item/capacity/type, over-capacity; `result`, `model.call`, `call`, `functionRef` и helper/submodel refs в каждой owner expression position | deterministic error order до binder/prover | parser report | — |
| C-AC6 Migration explicit | v0.2 direct reject; exact scalar migration; non-exact reject; copied limits + constant `maxWitnessValueNodes`; `4096` nodes accepted and `4097` gives `MigrationWitnessValueLimitExceeded` without output; checked-in golden canonical bytes/digest; approval change | inspect old/new projection и golden fixture | migration report | — |
| C-AC7 Existing behavior preserved | Modules + Reserve + E04; scalar v0.3 exact owner cases | counts/hashes | regression report | — |
| C-AC8 Generated identity stable | two clean lowerings byte-equal; source IDs absent; source map complete; изменение module-only unreachable `U` сохраняет owner digest, но меняет module/proof identity | generated source/identity review | determinism report | — |

Commands фиксируются в EXEC report. Bounded harness сохраняет отдельный run directory и не перезаписывает historical evidence. Повтор solver после identical failure возможен только после новой гипотезы/изменения.

## 12. Риски и edge cases
- Неполный список semantic edges мог бы скрыть изменение owner meaning. v0.3 закрывает roots/record/sequence edges явно; future refinements/aliases/constants/helpers/imports/effects требуют новой schema, а unresolved edge fail closed.
- Shared `ModuleValue` может раскрыть конструкторы, которые допускают malformed object; parser/evaluator всё равно обязаны рекурсивно валидировать boundary.
- Dafny datatype/subset equality должна совпасть с reference structural equality; проверяется различающими witnesses, но остаётся частью lowering TCB.
- Strict bool helpers могут увеличить proof cost; измеряется отдельно, semantic short-cut запрещён.
- Migration удаляет manual ensures только если exact pattern доказан parser; иной v0.2 bundle отклоняется.
- Composite exact proof без fold покрывает ограниченное семейство; это не AC2 всей E05.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Зачем копировать types в bundle | Дублирование module данных | Только exact closure owner copy подписывает смысл; binder требует equality reachable declarations | mitigated |
| Почему новый version | Уже есть v0.2 evidence | Semantics меняется; explicit migration сохраняет происхождение | mitigated |
| Почему убрать ensures | Может казаться потерей выразительности | Exact outcome уже обязателен; дополнительные predicates вернутся только с одним canonical contract form | mitigated |
| Почему пока без fold | Главный composite use case | Сначала закрывается ownership типов и exact model; fold требует отдельной scoped invariant schema | accepted-risk |

### Rework Prevention Checklist
- Видимая projection и migration заданы.
- Сценарии связаны с C-AC1–C-AC8.
- Type ownership, exact outcome и versioning decisions перечислены.
- Negative proof и malformed witness cases обязательны.
- EXEC ограничен owner composite contract и не включает admission/fold.

## 13. План выполнения
1. Реализовать v0.3 models, strict parser/codec, exact owner type closure и migration; checkpoint commit.
2. Реализовать composite value evaluator и binder equality; checkpoint commit.
3. Расширить owner Dafny expressions/types, strict bool helpers и exact composite model; checkpoint commit.
4. Запустить alternative/wrong/partial matrix, regressions, knowledge closure и post-EXEC review.

## 14. Открытые вопросы
Блокирующих вопросов нет. Дополнительные postconditions, helper contracts, quantifiers/MathInt и fold invariant будут отдельной SPEC, чтобы не смешивать exact model ownership с loop proof.

## 15. Соответствие профилю
- Профиль: language-contract / proof-boundary.
- Выполнено: versioned schema, ownership boundary, exact syntax, state/migration, AC matrix, TCB/negative cases.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Strogo.Modules/OwnerContracts.cs` | v0.3 types/values/contracts/limits | canonical data model |
| `src/Strogo.Modules/OwnerBundleParser.cs` | strict composite parser + migration input validation | fail-closed boundary |
| `src/Strogo.Modules/OwnerBundleCodec.cs` | TypeRef/value/expression canonicalization + owner type closure | stable owner identity |
| `src/Strogo.Modules/OwnerContractSemantics.cs` | composite type/evaluation/binding rules | exact outcome |
| `src/Strogo.Modules/DafnyLowering.cs` | owner composite expressions/shared symbols | proof |
| `fixtures/modules-v0.2/**` | v0.3 safe/alternative/wrong/partial fixtures | distinguishing cases |
| `tests/Strogo.Modules.Conformance/**`, `tools/Test-Modules-Dafny-Lowering.ps1` | migration/proof/regression matrix | evidence |
| `docs/modules-v0.2.md`, `docs/knowledge-log.md` | contract and findings | knowledge continuity |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Owner type meaning | module-owned names | exact owner-owned semantic type closure |
| Exact outcome | manually repeated ensures pattern | required modelRef + automatic ensures |
| Witness values | scalar struct | recursively validated immutable values |
| Version | v0.2 scalar | v0.3 explicit migration |
| Composite proof | structural/range candidate only | exact owner model for bounded family |

## 18. Альтернативы и компромиссы
- Ссылаться только на module type digest: меньше duplication, но human projection/approval не самодостаточен и требует второго semantic artifact.
- Structural inline types во всех signatures: самодостаточно, но множество способов повторить одну форму и высокий риск drift.
- Полный canonical module `types` в bundle: simple equality, но заставляет владельца повторно утверждать изменение private/unreachable implementation type, не меняющее observable contract.
- Exact owner-semantic closure: один canonical owner-owned universe для contract meaning; private module types остаются в module/proof identity. Выбран после публичного counterexample review, потому что closed semantic edges делают full-universe approval избыточным.
- Сохранить arbitrary ensures: выразительнее, но создаёт второй путь описания результата; для v0.3 выбран mandatory exact model.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Problem/outcome/non-goals определены |
| B. Качество дизайна | 6-10 | PASS | Owner identity и one-form exact outcome заданы |
| C. Безопасность изменений | 11-13 | PASS | Version/migration/fail-closed rollback раскрыты |
| D. Проверяемость | 14-16 | PASS | C-AC1–C-AC8 имеют evidence |
| E. Готовность к автономной реализации | 17-19 | PASS | Этапы и file scope заданы |
| F. Соответствие профилю | 20 | PASS | Language/proof boundaries явны |

Итог: ГОТОВО К REVIEW.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один ownership defect |
| 2. Понимание текущего состояния | 5 | Scalar/composite граница проверена по коду |
| 3. Конкретность целевого дизайна | 5 | Schema, values, ops, binding заданы |
| 4. Безопасность | 5 | Explicit migration и no approval carry-over |
| 5. Тестируемость | 5 | Positive/alternative/wrong/partial matrix |
| 6. Готовность к автономной реализации | 5 | Four bounded checkpoints |

Итоговый балл: **30 / 30**. Зона: готово к независимому review.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Owner утверждает весь contract meaning, не private implementation types? | PASS | Exact owner-semantic closure |
| UX / designer | not applicable | UI отсутствует; projection reviewable? | PASS | Types/model/witness целиком |
| Tester / validation | applicable | Ошибочные type/model/value различаются? | PASS | C-AC1–C-AC8 |
| Developer / architect | applicable | Нет двух semantic sources и скрытых requires? | PASS | modelRef automatic ensures |
| Delivery / operations / security | applicable | Version/migration/approval provenance безопасны? | PASS | New digest, no carry-over |

### Post-SPEC Review
- Статус / stop decision: **PASS**, подтверждение владельца можно запрашивать. Targeted reviewer проверил owner-semantic closure snapshot SHA-256 `8B01B5673BCE34D255C99D2A4126EF33C4718136A47E88F7CFB8ED2B87E2D71F`; после PASS меняются только этот audit block и journal. Прежний full-universe snapshot `C8CFBEBC…2159FF` superseded.
- Reviewer: `/root/e05_composite_review`, роль `independent-reviewer`. Фактический sandbox был `danger-full-access`, поэтому pass технически не называется read-only; reviewer процедурно выполнял только `Get-Content`, `Get-FileHash`, `rg` и не менял workspace.
- Scope/Evidence pass: эта SPEC; central instruction stack и локальный `AGENTS.md`; `docs/project-intent.md`; утверждённая E05 v0.2; `docs/modules-v0.2.md`; `OwnerContracts.cs`, owner parser/codec/semantics/evaluator/binder, module TypeRef/parser/codec, Dafny lowerer и текущие owner/composite fixtures; planned files из §16; Posting Board replies #9542–#9545; SHA-256, structural ID counts и `git diff --check`. Build/tests не запускались, потому что это SPEC review без implementation.
- Contract pass: outcome и Non-Goals отделяют owner composite contract от admission/fold/imports/helpers; C-S1–C-S7 связаны с C-AC1–C-AC8; owner-only semantic closure, closed wire schema, one-form exact model, explicit migration, replay outcomes, rollback и evidence contract сверены с G01/G04 и E05.
- Adversarial risk pass: проверены candidate-controlled type shape/reference, missing/extraneous closure declarations, reachable/private drift, incomplete future semantic edges, две JSON-интерпретации composite values/expressions, пустой/vacuous contract, strict-vs-lazy definedness, forged counterexample, partial model, v0.2 approval carry-over и миграция валидного v0.2 bundle сверх нового value-node limit.
- Role-Based pass: domain/owner workflow — owner утверждает типы и model; UX — UI отсутствует, обязательна текстовая projection; tester — positive/alternative/wrong/partial/migration boundaries; architect — schema/binder/lowering/TCB; delivery/security — новый digest, отсутствие approval carry-over и rollback к v0.2 checkpoint.

| Severity | Area | Finding / required action | Status |
| --- | --- | --- | --- |
| HIGH | Wire schema | Закрыть recursive Value/Expression JSON, поля opcode и canonical ordering | fixed, re-reviewed |
| HIGH | Migration | Полностью задать v0.2→v0.3 mapping, новый limit и golden identity | fixed, re-reviewed |
| HIGH | Cardinality | Запретить пустые contracts/models/witnesses и требовать exact arguments | fixed, re-reviewed |
| HIGH | Strict semantics | Различить strict `and/or` и lazy `if` в AC | fixed, re-reviewed |
| MEDIUM | Proof outcome | Разрешать `Counterexample` только после independent replay | fixed, re-reviewed |
| MEDIUM | Owner references | Запретить `result`, `model.call`, candidate/helper calls в v0.3 AST | fixed, re-reviewed |
| HIGH | Migration capacity | Отказать без output при >4096 migrated witness value nodes | fixed, re-reviewed |
| MEDIUM | Approval scope | Full module type universe включал private/unreachable implementation types в human approval; заменить его exact owner-semantic closure и добавить reachable/private identity controls | fixed, re-reviewed |
| — | Targeted closure review | Нет находок в снимке `8B01B567…E2D71F`; дополнительные изменения контракта не требуются | PASS |

- Fix and re-review: ранние findings прошли re-review; exact closure snapshot отдельно проверен после fixture #9545. Reviewer подтвердил declared-TypeRef traversal, закрытые record/sequence edges, missing/extraneous refusals, private module identity boundary и scalar migration. No-findings justification: каждый current v0.3 semantic edge либо является root, либо достигается через declared record/sequence shape; candidate-only state не входит в owner meaning, но остаётся в proof identity.
- Depth checklist: scope drift/unrelated changes отсутствуют; AC/evidence mapping заполнен; claims ограничены будущим EXEC; regressions включают Modules/Reserve/E04; docs/knowledge impact указан; hidden API/schema change требует нового approval; UI/video неприменимы.
- Manual-review challenge / residual risks: человеку нужно подтвердить выбор exact owner-semantic type closure, удаления arbitrary ensures и re-approval при миграции. Неполный future semantic edge опасен, поэтому расширение type system требует новой schema. Эквивалентность evaluator/Dafny остаётся TCB, а proof cost strict helpers и golden reproducibility проверяются только EXEC.

### Post-EXEC Review
- Статус: **PASS**. Финальный procedural read-only fix-and-re-review не нашёл оставшихся BLOCKER/HIGH/MEDIUM findings; effective sandbox был writable, reviewer не изменял файлы.

| Criterion | Result | Evidence / boundary |
| --- | --- | --- |
| C-AC1 | PASS | Exact owner-semantic closure, canonical ordering, empty-sequence/transitive/private mutations |
| C-AC2 | PASS | Два разных composite DAG: по `5 verified, 0 errors`, одинаковые generated .NET outcomes |
| C-AC3 | PASS | Reachable drift/missing/extra и wrong order rejected; mismatch получает concrete witness только после replay |
| C-AC4 | PASS | Strict `false and`/`true or` partial — `Unproven`; lazy guarded `if` — `Verified`; nested bounded append — `Unproven` |
| C-AC5 | PASS | Composite witness mutation matrix; forbidden `result`/`model.call`/`call` в root, nested model и `requires` |
| C-AC6 | PASS | Explicit v0.2 migration, golden bytes/digest, legacy restrictions, 4096/4097 boundary, malformed typed refusal |
| C-AC7 | PASS | Release build 0 warnings/errors; Modules 276/65; Reserve 29/29 и 10 904; E04 141 |
| C-AC8 | PASS | Clean lowering byte equality; source IDs absent; private type сохраняет owner digest и меняет module/proof identity |

- Fresh evidence: `artifacts/local-validation/e05/owner-v03-final-20260908-v2/`; Modules report SHA-256 `b690a5e9…0b0a367`, Dafny summary SHA-256 `87c807db…a1d3bb`, Reserve report SHA-256 `773a7b50…c2876`, E04 report SHA-256 `3913b159…e2489`.
- Operational boundary: один предшествующий E04 verifier timeout и один перегруженный Reserve run с `SolverTimeout` сохранены как typed environment failures; отдельные clean serial runs прошли. Причина contention не доказана.
- Scope boundary: owner-composite amendment закрыт; admission, fold, imports/helpers, runtime facade, release и G05/G06 остаются вне этого EXEC.
- Residual LOW: evaluator↔Dafny equivalence остаётся частью TCB; replay result ещё не является подписанным admission receipt.

## Approval
Получена фраза **«Спеку подтверждаю»** 2026-09-07; 2026-09-08 владелец повторно подтвердил продолжение и периодический push checkpoint commits.

Подтверждение распространяется только на эту owner-composite поправку и checkpoint commits. Admission EXEC, fold, merge и release не разрешаются. Периодический push сделанных commits и участие в Posting Board отдельно разрешены владельцем ранее.

## 20. Журнал действий агента

| Фаза | Тип | Уверенность | Не хватает | Следующее действие | Нужен человек | Фактическое решение | Объяснение | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Найден ownership gap | 0.99 | Нет | Включить types в owner identity | Нет | Нет | Record name без shape не фиксирует смысл | current v0.2 code, K-E05-063 |
| SPEC | Design | 0.94 | Independent review и approval | Review | Да, после PASS | Ещё нет | v0.3 removes duplicate ensures and adds explicit migration | эта SPEC |
| SPEC | Сузить type ownership до semantic closure | 0.99 | Targeted re-review | Проверить exact snapshot, затем снова запросить approval | Нет до PASS | Reply `0be5e221-76b0-49e4-a959-bcc4bb84a754` (#9542) опубликован/read back; Помощник Архитектора ответил `58694e13-005a-458a-87d6-4c268233b8a7` (#9543) и `a4e66bbe-3a0f-42bb-85d5-46e471bc9515` (#9545); уточнение `e587016c-bbdd-4957-94f1-c2e4545e1257` (#9544) опубликовано/read back | Full universe смешивал human contract с private implementation; owner-only closure закрывает текущие semantic edges, declared type traversal не зависит от наполненности witness, а private drift остаётся в module/proof identity | Эта SPEC §§2,6.2,11,12,18; Posting Board thread |
| SPEC | Повторно проверить owner-semantic closure | 0.99 | Только approval владельца | Зафиксировать checkpoint и запросить exact approval | Да | Reviewer дал PASS exact snapshot `8B01B567…E2D71F`; effective sandbox writable, review процедурно read-only | Empty-witness fixture, recursive `Seq`, closure refusals и module/proof invalidation проверены; future semantic edges остаются новой schema/SPEC | Эта SPEC §19 |
| EXEC | Human approval | 1.00 | Нет | Реализовать owner-composite v0.3 по утверждённому контракту | Нет | Пользователь сообщил точную фразу «Спеку подтверждаю» 2026-09-07; периодический push commits также подтверждён | Approval относится к последней явно запрошенной owner-composite SPEC; fold v0.1 остаётся отдельным неподтверждённым изменением | Эта SPEC §Approval; сообщение пользователя |
| EXEC | Owner v0.3 data/semantic boundary | 0.97 | Dafny composite lowering и полная mutation matrix ещё не выполнены | Зафиксировать первый checkpoint, затем расширить proof lowering | Нет | `dotnet build tests/Strogo.Modules.Conformance -c Release --no-restore` и conformance PASS, 231 checks | Реализованы strict v0.3 parser/codec, explicit v0.2 migration, shared immutable composite values, exact closure, evaluator и binder; fixtures различают empty declared sequence, missing/extraneous/reachable/private type cases | owner source files; owner scalar/composite/empty-sequence fixtures; conformance harness |
| EXEC | Composite exact proof и replay | 0.98 | Полные regressions, docs и independent post-EXEC review ещё не выполнены | Зафиксировать proof checkpoint, затем закрыть AC/knowledge/review | Нет | Bounded Dafny harness PASS; два composite candidates `5 verified, 0 errors` и generated .NET consumers совпали; wrong candidate дал replayed witness + failed postcondition; partial model дал `index out of range`/Unproven | Owner expressions lowerятся через общий type-symbol table; strict bool helpers исключают подмену definedness short-circuit синтаксисом; concrete mismatch называется Counterexample только после evaluator replay | `DafnyLowering.cs`; owner composite safe/alternative/wrong/partial fixtures; `artifacts/local-validation/e05/owner-v03-20260907-2/dafny-lowering.json` |
| EXEC | Adversarial post-EXEC review | 0.99 | Две replay-находки после первого fix pass | Rebind trusted inputs, ввести allow-list failure classes и повторить review | Нет | Первые четыре findings закрыты; затем выявлены forged empty binding и broad candidate blame | Replay не может доверять публично создаваемому derived binding и не должен приписывать tool defects candidate | procedural read-only reviewer; K-E05-070 |
| EXEC | Final validation | 0.99 | Финальный reviewer verdict | Выполнить fix-and-re-review, обновить tracked evidence и checkpoint commit | Нет | Build 0 warnings/errors; Modules/Dafny 276/65; Reserve 29/29 и 10 904; E04 141 | Все C-AC1–C-AC8 имеют executable либо mutation evidence; runtime/admission/fold boundaries сохранены | `artifacts/local-validation/e05/owner-v03-final-20260908-v2/`; Post-EXEC Review |
| EXEC | Final fix-and-re-review | 0.99 | Нет в owner-composite scope | Обновить tracked evidence, commit и push | Нет | PASS; 0 remaining BLOCKER/HIGH/MEDIUM; residual LOW про TCB/admission сохранён | Reviewer подтвердил trusted rebinding, failure allow-list, bounded append, strict/guarded proof pairs и forbidden-op matrix | Post-EXEC Review; K-E05-070 |
