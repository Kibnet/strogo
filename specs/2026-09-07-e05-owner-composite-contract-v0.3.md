# E05: owner-owned composite types и canonical exact models v0.3

## 0. Метаданные
- Тип (профиль): QUEST / language-contract / proof-boundary.
- Владелец: пользователь — смысл типов, requires и exact model; агент — schema, parser, evaluator, binder и lowering после approval.
- Масштаб: large.
- Целевое семейство / behavior baseline: `strogo.module.v0.2`, scalar owner checkpoint `e08f1a4`, composite lowering `c5ca6b4`.
- Поверхность: Codex + локальные .NET library/CLI artifacts.
- Effective runtime: Не применимо — изменение детерминированного language/toolchain contract.
- Eval baseline / evidence: scalar owner `4 verified`; composite structural candidate `4 verified`; evidence сохраняется новым run directory под `artifacts/local-validation/e05/`.
- Целевой релиз / ветка: локальная `main`; push/release не входят в approval.
- Ограничения: Dafny 4.11.0, .NET SDK 10.0.400; imports, helper contracts и `fold` не входят в этот срез.
- Связанные ссылки: [E05 SPEC](2026-09-06-composable-verified-modules-v0.2.md), [admission amendment](2026-09-07-e05-two-stage-admission-amendment.md), [K-E05-063](../docs/knowledge-log.md).
- Instruction stack / профиль: central `creator-vibe-lens`, `model-behavior-baseline`, `quest-governance`, `collaboration-baseline`, `tool-execution-baseline`, `quest-mode`, `spec-linter`, `spec-rubric`, `review-loops`, профиль `product-system-design`, governance `commit-message-policy`; поверх них локальный `AGENTS.md` Strogo.

## 1. Overview / Цель
Сделать composite owner contract действительно принадлежащим владельцу: утверждённый bundle должен фиксировать полную форму records/sequences и единственный exact model, а candidate module не должен иметь возможность менять смысл именованного типа.

Эта поправка нормативно заменяет только формат owner bundle и его binding/lowering из E05 §6.2/§6.6. Module schema `strogo.module.v0.2`, admission amendment и исходные E05 goals/AC сохраняются.

Outcome contract:
- Success means: owner bundle v0.3 самостоятельно фиксирует type universe, requires, witnesses и model; binder требует exact type equality; две composite реализации доказывают один exact outcome, а type/model/candidate mutations fail closed.
- Итоговый артефакт / output: strict `strogo.owner-bundle.v0.3`, canonical composite values/expressions, обновлённые parser/evaluator/binder/Dafny lowering, fixtures и proof evidence.
- Stop rules: неизвестный тип/op/field, partial model, type drift, слабый requires, forged exact outcome, `Unproven`, `Timeout` или `ToolError` не допускаются как Verified.

## 2. Текущее состояние (AS-IS)
- `strogo.owner-bundle.v0.2` содержит contracts/models/limits, но не содержит определения records.
- Owner parser принимает только `I64`/`Bool`, witness evaluator возвращает `OwnerScalarValue`, а binder отклоняет module с types.
- Composite candidate lowering уже генерирует Dafny datatypes и bounded sequence subset types, но проверяет только structural/range obligations без owner exact outcome.
- Если просто разрешить имя `Summary` в v0.2, форму его полей продолжит задавать candidate module; semantic approval bundle digest эту форму не покроет.

## 3. Проблема
Именованный composite type является частью утверждаемого смысла, но сейчас отсутствует в owner-owned identity. Разрешить composite signatures без исправления формата означало бы оставить агенту неявную возможность менять контракт через type declaration.

## 4. Цели дизайна
- Включить полный type universe entry surface в canonical owner identity.
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
- `OwnerContractBinder` → exact module/bundle type equality и entry/model signature binding.
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

#### Owner-owned type universe

`types` использует ту же закрытую форму, ID rules, canonical ordering, recursion/depth/capacity checks и `TypeRef`, что module v0.2. Bundle содержит полный список module type declarations, даже если часть типов не достигается из exports. Binder требует byte-equal canonical type projection `module.types == bundle.types`; missing, extra, reordered semantics, field/type/capacity drift дают `OwnerTypeUniverseMismatch` до witness/prover.

TypeRef имеет единственную JSON-форму:

```text
"I64" | "Bool" | "<record-id>" |
{"kind":"Seq","elementType":<TypeRef>,"capacity":"<canonical decimal 0..256>"}
```

`types` содержит `0..256` declarations. `entryContracts` и `models` содержат `1..128` элементов и имеют равную cardinality из-за one-to-one `modelRef`; каждый entry содержит `1..maxWitnessesPerEntry` witnesses. Нулевая cardinality `entryContracts`, `models` или `witnesses` запрещена, поэтому пустой bundle и непоказанный `requires` domain не проходят parser/semantics. Число parameters каждой entry/model signature — `0..512`, как в module function limit.

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

`OwnerScalarValue` заменяется `ModuleValue`/internal typed value codec: `ModuleI64`, `ModuleBool`, `ModuleRecord`, `ModuleSequence`. Parser создаёт defensive immutable collections. Evaluator перед каждым operation рекурсивно сверяет expected type against owner type universe. Checked I64 overflow, sequence range/capacity и unknown field дают typed error; никакой default value не создаётся.

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

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| owner bundle v0.2 | parse v0.3 | `OwnerBundleMigrationRequired` | no implicit conversion | старое evidence сохраняется |
| v0.2 scalar bundle | migrate | canonical v0.3 с `types: []` и новым digest | non-exact old ensures → refusal | approval не переносится |
| v0.3 bundle + matching module | bind | Bound | full type projection drift → refusal | до witnesses/prover |
| Bound composite entry | lower/verify | Verified или typed proof outcome | partial model → Unproven | exact outcome automatic |
| Verified candidate A | candidate B same bundle | отдельный proof identity | wrong B → postcondition failure | bundle immutable |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Владение record definitions | agent | Полный type universe внутри bundle | 0.98 | Только имена оставляют смысл candidate | Нет; предмет approval этой SPEC |
| Exact outcome syntax | agent | `modelRef`, automatic ensures; свободный ensures удалить | 0.96 | Дублирование даёт две формы одной гарантии | Нет |
| Versioning | agent | owner bundle v0.3 + explicit migration | 0.94 | Silent v0.2 reinterpretation меняет approved bytes | Нет |
| Value algebra | agent | Reuse immutable `ModuleValue` | 0.90 | Две семантики значений расходятся | Нет |
| Fold/quantifiers | agent | Отдельный следующий amendment | 0.95 | Одновременное расширение скрывает proof gaps | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Owner schema | v0.2 scalar | v0.3 with `types`, `modelRef`, composite values | explicit v0.2 migration | canonical fixtures |
| Type identity | candidate module only | byte-equal owner/module type projection | mismatch fails | mutations |
| Exact outcome | manual ensures shape | automatic equality to modelRef | migrator extracts only exact v0.2 form | proof source review |
| Witness evaluator | OwnerScalarValue | shared immutable composite values | API migration | differential cases |
| Dafny owner lowering | scalar symbols | shared record/sequence symbols | scalar remains expressible in v0.3 | safe/wrong/partial runs |

## 7. Бизнес-правила / Алгоритмы
1. Owner type projection входит в bundle digest и не выводится из candidate.
2. Любой module type drift требует нового bundle и human semantic approval.
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
- `OwnerBundle` получает immutable `Types` и schema v0.3.
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
| C-AC1 Bundle владеет composite смыслом | canonical roundtrip; shuffled declarations/fields same digest; field/capacity drift changes digest | projection review | owner report | — |
| C-AC2 Две реализации дают exact outcome | evaluator witnesses + two Dafny Verified candidates against one bundle | inspect generated model/ensures | proof report | — |
| C-AC3 Неверные изменения fail closed | swapped items, wrong field, changed type universe/model/candidate; candidate mismatch на owner witness replay | stable obligation/locus; `Counterexample` содержит воспроизведённый witness | mutation/replay report | — |
| C-AC4 Partial/strict definedness не маскируется | `seq.get` without range; weak requires; overflow; `false and <partial>` и `true or <partial>` остаются partial; соответствующие guarded `if` с partial expression в невыбранной ветке проходят evaluator+Dafny | inspect generated strict helpers; distinguish `Unproven`/`Counterexample`; запрет raw `&&`/`||` lowering | negative proof report | — |
| C-AC5 Composite witnesses и closed AST strict | missing/extra/duplicate field, wrong item/capacity/type, over-capacity; `result`, `model.call`, `call`, `functionRef` и helper/submodel refs в каждой owner expression position | deterministic error order до binder/prover | parser report | — |
| C-AC6 Migration explicit | v0.2 direct reject; exact scalar migration; non-exact reject; copied limits + constant `maxWitnessValueNodes`; `4096` nodes accepted and `4097` gives `MigrationWitnessValueLimitExceeded` without output; checked-in golden canonical bytes/digest; approval change | inspect old/new projection и golden fixture | migration report | — |
| C-AC7 Existing behavior preserved | Modules + Reserve + E04; scalar v0.3 exact owner cases | counts/hashes | regression report | — |
| C-AC8 Generated identity stable | two clean lowerings byte-equal; source IDs absent; source map complete | generated source review | determinism report | — |

Commands фиксируются в EXEC report. Bounded harness сохраняет отдельный run directory и не перезаписывает historical evidence. Повтор solver после identical failure возможен только после новой гипотезы/изменения.

## 12. Риски и edge cases
- Full type universe делает bundle чувствительным к unused type change: это сознательный fail-closed выбор.
- Shared `ModuleValue` может раскрыть конструкторы, которые допускают malformed object; parser/evaluator всё равно обязаны рекурсивно валидировать boundary.
- Dafny datatype/subset equality должна совпасть с reference structural equality; проверяется различающими witnesses, но остаётся частью lowering TCB.
- Strict bool helpers могут увеличить proof cost; измеряется отдельно, semantic short-cut запрещён.
- Migration удаляет manual ensures только если exact pattern доказан parser; иной v0.2 bundle отклоняется.
- Composite exact proof без fold покрывает ограниченное семейство; это не AC2 всей E05.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Зачем копировать types в bundle | Дублирование module данных | Только owner copy подписывает смысл; binder требует equality | mitigated |
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
1. Реализовать v0.3 models, strict parser/codec, full type projection и migration; checkpoint commit.
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
| `src/Strogo.Modules/OwnerBundleCodec.cs` | full TypeRef/value/expression canonicalization | stable owner identity |
| `src/Strogo.Modules/OwnerContractSemantics.cs` | composite type/evaluation/binding rules | exact outcome |
| `src/Strogo.Modules/DafnyLowering.cs` | owner composite expressions/shared symbols | proof |
| `fixtures/modules-v0.2/**` | v0.3 safe/alternative/wrong/partial fixtures | distinguishing cases |
| `tests/Strogo.Modules.Conformance/**`, `tools/Test-Modules-Dafny-Lowering.ps1` | migration/proof/regression matrix | evidence |
| `docs/modules-v0.2.md`, `docs/knowledge-log.md` | contract and findings | knowledge continuity |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Owner type meaning | module-owned names | full owner-owned type universe |
| Exact outcome | manually repeated ensures pattern | required modelRef + automatic ensures |
| Witness values | scalar struct | recursively validated immutable values |
| Version | v0.2 scalar | v0.3 explicit migration |
| Composite proof | structural/range candidate only | exact owner model for bounded family |

## 18. Альтернативы и компромиссы
- Ссылаться только на module type digest: меньше duplication, но human projection/approval не самодостаточен и требует второго semantic artifact.
- Structural inline types во всех signatures: самодостаточно, но множество способов повторить одну форму и высокий риск drift.
- Полный canonical `types` в bundle: один owner-owned universe, simple equality и reviewable projection ценой deliberate re-approval при unused type changes.
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
| Business analyst / domain workflow | applicable | Owner действительно утверждает весь composite смысл? | PASS | Full type universe |
| UX / designer | not applicable | UI отсутствует; projection reviewable? | PASS | Types/model/witness целиком |
| Tester / validation | applicable | Ошибочные type/model/value различаются? | PASS | C-AC1–C-AC8 |
| Developer / architect | applicable | Нет двух semantic sources и скрытых requires? | PASS | modelRef automatic ensures |
| Delivery / operations / security | applicable | Version/migration/approval provenance безопасны? | PASS | New digest, no carry-over |

### Post-SPEC Review
- Статус / stop decision: **PASS**, подтверждение владельца можно запрашивать. Independent reviewer проверил normative content snapshot SHA-256 `C8CFBEBC0F5C7C62869829EC15491D51D19E3279A96E4DD0711432B5A12159FF`; после PASS изменена только эта запись review.
- Reviewer: `/root/e05_composite_review`, роль `independent-reviewer`. Фактический sandbox был `danger-full-access`, поэтому pass технически не называется read-only; reviewer процедурно выполнял только `Get-Content`, `Get-FileHash`, `rg` и не менял workspace.
- Scope/Evidence pass: эта SPEC; central instruction stack и локальный `AGENTS.md`; `docs/project-intent.md`; утверждённая E05 v0.2; `docs/modules-v0.2.md`; `OwnerContracts.cs`, owner parser/codec/semantics/evaluator/binder, module TypeRef/parser/codec, Dafny lowerer и текущие owner/composite fixtures; planned files из §16; `Get-FileHash`, structural ID counts и `git diff --check`. Build/tests не запускались, потому что это SPEC review без implementation.
- Contract pass: outcome и Non-Goals отделяют owner composite contract от admission/fold/imports/helpers; C-S1–C-S6 связаны с C-AC1–C-AC8; full owner type identity, closed wire schema, one-form exact model, explicit migration, replay outcomes, rollback и evidence contract сверены с G01/G04 и E05.
- Adversarial risk pass: проверены candidate-controlled type shape/reference, две JSON-интерпретации composite values/expressions, пустой/vacuous contract, strict-vs-lazy definedness, forged counterexample, partial model, v0.2 approval carry-over и миграция валидного v0.2 bundle сверх нового value-node limit.
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

- Fix and re-review: после каждого исправления повторены hash/contract/AC/determinism passes; финальный reviewer verdict — PASS без оставшихся BLOCKER/HIGH/MEDIUM. No-findings justification: все найденные альтернативные parsing/outcome/migration interpretations теперь имеют единственный нормативный ответ и различающие AC.
- Depth checklist: scope drift/unrelated changes отсутствуют; AC/evidence mapping заполнен; claims ограничены будущим EXEC; regressions включают Modules/Reserve/E04; docs/knowledge impact указан; hidden API/schema change требует нового approval; UI/video неприменимы.
- Manual-review challenge / residual risks: человеку нужно подтвердить выбор полного owner-owned type universe, удаления arbitrary ensures и re-approval при миграции. Эквивалентность evaluator/Dafny остаётся TCB, а proof cost strict helpers и golden reproducibility проверяются только EXEC.

### Post-EXEC Review
- Статус: Не выполнен до EXEC.

## Approval
Ожидается фраза: **«Спеку подтверждаю»**.

Подтверждение распространяется только на эту owner-composite поправку и локальные checkpoint commits. Admission EXEC, fold, push, merge, release и публикация не разрешаются.

## 20. Журнал действий агента

| Фаза | Тип | Уверенность | Не хватает | Следующее действие | Нужен человек | Фактическое решение | Объяснение | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Найден ownership gap | 0.99 | Нет | Включить types в owner identity | Нет | Нет | Record name без shape не фиксирует смысл | current v0.2 code, K-E05-063 |
| SPEC | Design | 0.94 | Independent review и approval | Review | Да, после PASS | Ещё нет | v0.3 removes duplicate ensures and adds explicit migration | эта SPEC |
