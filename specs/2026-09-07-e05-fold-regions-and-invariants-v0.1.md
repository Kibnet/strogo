# E05: bounded fold regions и проверяемые semantic invariants v0.1

## 0. Метаданные
- Тип (профиль): QUEST / language-contract / proof-lowering.
- Владелец: пользователь — смысл owner model и допустимый input domain; агент — candidate fold, доказательный invariant и реализация toolchain после approval.
- Масштаб: large.
- Целевое семейство / behavior baseline: утверждённая E05 SPEC, `strogo.module.v0.2`, composite candidate checkpoint `c5ca6b4`, owner-composite amendment `fdfe59b`.
- Поверхность: Codex + локальные .NET library/CLI artifacts.
- Effective runtime: .NET SDK 10.0.400, Dafny 4.11.0, Z3 5.1.0.
- Eval baseline / evidence: после EXEC — exact allocation fixtures empty/`[4,3,1]`/MAX, две правильные candidate forms, mutations и pinned Dafny/reference differential report; до EXEC evidence отсутствует по причине approval gate.
- Целевой релиз / ветка: локальная `main`; push/release не входят в approval.
- Зависимость: EXEC начинается только после подтверждения и реализации owner-bundle v0.3 из `2026-09-07-e05-owner-composite-contract-v0.3.md`.
- Связанные ссылки: [E05 SPEC](2026-09-06-composable-verified-modules-v0.2.md), [owner composite v0.3](2026-09-07-e05-owner-composite-contract-v0.3.md), [K-E05-064](../docs/knowledge-log.md).
- Instruction stack / профиль: central `creator-vibe-lens`, `model-behavior-baseline`, `quest-governance`, `collaboration-baseline`, `tool-execution-baseline`, `quest-mode`, `spec-linter`, `spec-rubric`, `review-loops`, профиль `product-system-design`, governance `commit-message-policy`; поверх них локальный `AGENTS.md` Strogo.

## 1. Overview / Цель
Добавить один общий bounded left fold без предметных opcodes. Исполняемый step задаётся nested region, owner задаёт exact fold model, а compiler проверяет termination, границы и связь каждого prefix с owner model.

Outcome contract:
- Success means: variable-length bounded sequence обрабатывается слева направо; reference evaluator и generated Dafny/.NET выполняют один candidate step; owner exact outcome доказывается для всей утверждённой области; неверный invariant или step не получает `Verified`.
- Итоговый артефакт / output: строгий module `fold` node, owner-bundle v0.4 fold model, fold-specific proof predicate, reference execution, Dafny loop/prefix model и bounded evidence на OrderedExactAllocation.
- Stop rules: hidden capture, nested fold, неизвестная proof operation, partial invariant/model/step, missing obligation locus, `Unproven`, `Timeout` или `ToolError` не считаются Verified.

Эта поправка нормативно уточняет E05 §6.2 только для первого proof-capable fold slice. Исходные E05 цели и критерии сохраняются; nested folds, imported Clamp и полный AC2/AC5 остаются открыты после этого среза.

## 2. Текущее состояние (AS-IS)
- Module v0.2 поддерживает immutable records, bounded sequences, local calls и lazy nested `if` regions.
- Reference evaluator исполняет composite DAG, а Dafny lowering создаёт record datatypes, sequence subset types и range obligations.
- `fold` отсутствует в source/IR/parser/evaluator/lowerer. Variable-length composition требует ручного unroll или нового предметного opcode.
- Owner bundle v0.3 отдельно проектирует composite values/models, но намеренно не содержит fold, helpers или quantifiers.
- Posting Board предложил merge/scanner tasks, которым нужны accumulator и обход последовательностей; опубликованные задачи уже являются development fixtures и не могут подтверждать held-out breadth.

## 3. Проблема
Без общего fold язык не выражает обычные bounded list algorithms, а добавление `scan`, `allocate`, `merge` или других предметных операций под конкретную задачу исказит проверку выразительности. Простого цикла также недостаточно: агент не должен управлять index/termination и не должен подменять точный owner outcome слабым invariant.

## 4. Цели дизайна
- Один canonical left-fold construct с неизменным input sequence.
- Compiler-owned index, direction и termination; agent не пишет loop control.
- Ровно один явный semantic invariant на fold; структурные и exact-prefix conjuncts добавляет compiler.
- Invariant является проверяемой proof hint candidate и не меняет owner contract.
- Один executable step используется reference evaluator и generated candidate.
- Stable obligation IDs и воспроизводимые negative outcomes.

## 5. Non-Goals (чего НЕ делаем)
- Nested folds, recursion, while/for, early break, parallel/reordered traversal.
- Imports и helper contracts внутри fold step.
- Noita/merge-specific operation, dictionary/set, comparator callback или task-specific checksum primitive.
- General theorem/lemma language, arbitrary triggers, raw Dafny, `assume`, `axiom`, `decreases` от agent.
- Admission/package/public runtime facade из отдельной поправки; generated fold consumer внутри validation harness обязателен.
- G05/G06 benchmark либо утверждение широты языка.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности
- `Models.cs` / `Parser.cs` / `Codec.cs` → strict fold node, nested step region и proof invariant AST.
- `Compiler.cs` → immutable `FoldRegionIr`, canonical binding и obligation loci.
- `ReferenceEvaluator.cs` → bounded left-to-right execution с общим step budget.
- owner contracts v0.4 → exact fold model и v0.3 migration.
- `OwnerContractSemantics` / binder → model/candidate fold compatibility и witness execution.
- `DafnyLowering.cs` → owner prefix function, candidate loop, generated invariants/decreases и exact postcondition.
- Conformance/harness → evaluator/Dafny differentiation, mutations, deterministic evidence.

### 6.2 Детальный дизайн

#### Candidate source schema

`strogo.module.v0.2` получает обещанный E05 opcode `fold`. Fold node — closed object ровно с полями:

```text
id, op, type, args, stepRegion, invariant
```

`op` равен `fold`. `args[0]` имеет type `Seq<T,N>`, `args[1]` — accumulator type `A`, `args[2..]` — ordered explicit environment. Node `type` обязан быть ровно `A`.

`stepRegion` использует существующую closed region shape `parameters,nodes,result`. Число parameters равно `3 + environment.Length`; их positional roles и inferred types:

```text
0: index I64
1: element T
2: accumulator A
3..: environment types в порядке args[2..]
```

Parameter IDs явны и локальны region. Step result имеет type `A`. Hidden capture, duplicate parameter, result вне region, `fold` или `call` внутри step отклоняются до IR. `if`, scalar, record и sequence operations разрешены по обычным rules.

`invariant` — один closed proof expression с result type `Bool`. Unknown/missing/duplicate fold fields запрещены. Property order незначим; ordinary `args`, environment и step parameters сохраняют порядок. Canonical codec пишет object properties в закреплённом opcode-specific порядке. Node и proof tree входят в source digest.

#### Execution semantics

Evaluator строго и ровно один раз вычисляет sequence, initial accumulator и каждый environment argument. Затем для `i=0..length-1` он вызывает тот же compiled `stepRegion` с `(i, sequence[i], accumulator, environment)` и заменяет accumulator immutable result. Empty sequence возвращает initial value, не исполняя step.

Fold dispatch и каждая iteration потребляют по одному shared evaluation step сверх фактически исполненных step-region nodes/calls. Sequence capacity `<=256` ограничивает один fold; nested fold запрещён в этом срезе. Budget exhaustion даёт `EvaluationStepLimitExceeded` с locus fold/iteration и не возвращает partial value.

Reference traversal выполняет ровно `length` iterations и имеет time `O(length * step)` сверх стоимости immutable operations; compiler не вправе повторно исполнять candidate step. `seq.append` может копировать bounded sequence и дать `O(length^2)` для allocation accumulator. Capacity `256` ограничивает этот риск в первом срезе, но не является подтверждением G06: EXEC фиксирует размер generated artifact и отдельный benchmark либо честно оставляет runtime/memory comparison открытым для следующего этапа.

#### Owner fold model v0.4

Owner schema меняется на literal `strogo.owner-bundle.v0.4`; v0.3 parser не принимает v0.4 и наоборот. Pure `OwnerBundleMigrator.MigrateV03ToV04` принимает только strict canonical-semantics v0.3, меняет schemaVersion, сохраняет все прежние type/entry/requires/witness/model values и четыре прежних limit values, добавляет `maxProofEvaluationSteps: "262144"`, canonicalizes новым codec/domain и выдаёт новый digest без approval carry-over. Старый v0.3 `requires` AST является точным подмножеством v0.4 и сохраняется byte-for-byte как canonical subtree; golden migration fixture обязан содержать каждую унаследованную operation.

Bundle digest: `SHA256(UTF8("strogo.owner-bundle.v0.4/bundle\n") || canonicalBytes)`.

Model body v0.4 дополнительно допускает root-only `fold` expression ровно с полями `op,type,args,step`. `args` имеют те же roles sequence/initial/environment. `step` имеет ровно `parameters,body`; ordered parameters имеют IDs/types `(index I64, element T, accumulator A, environment...)`, body возвращает `A` и видит только их. Nested owner fold и fold в `requires` запрещены.

Entry `requires` v0.4 использует закрытый `ProofExpressionV04`: все прежние v0.3 expression operations сохраняют типы и strict/lazy semantics, а дополнительно разрешены `math.const/from_i64/add/sub/le`, `seq.sum_i64`, `proof.bound` и `forall.sequence`. Таблица ниже является единственным normative allow-list. `param` остаётся единственным способом обратиться к entry parameter. Model body/step не принимают `MathInt`, `proof.bound`, `forall.sequence` или `seq.sum_i64`: это proof-only operations, а exact executable value продолжает задаваться model expression/fold.

`limits` v0.4 имеет четыре прежних поля v0.3 плюс `maxProofEvaluationSteps`: canonical positive decimal string `1..262144`. Codec включает его в bundle digest. Owner parser и candidate parser вычисляют worst-case cost каждого `requires`/invariant: leaf `1`; обычная operation `1 + sum(children)`; `seq.sum_i64` дополнительно `+ capacity`; `forall.sequence` — `1 + cost(sequence) + capacity * (1 + cost(body))`. Arithmetic saturates ровно в `262145` (`hardMaximum + 1`); artifact принимается iff `cost <= approved limit`. Diagnostic `actual` содержит exact canonical decimal до `262144`, после saturation — canonical string `">262144"`; `max` содержит approved decimal limit. Candidate parser применяет hard maximum, owner parser — approved limit к `requires`, binder — тот же approved limit к paired invariant.

Independent evaluator создаёт новый counter для каждого отдельного вычисления одного `requires` либо одной invariant instantiation и расходует step на каждый node visit, quantifier iteration и item `seq.sum_i64`; попытка превысить limit даёт `ProofEvaluationStepLimitExceeded` без partial Boolean/result, а ровно limit допустим. Batch witnesses обрабатываются в canonical witness order, но каждый получает новый counter. Общий process timeout/output cap относится к orchestration и возвращает `Timeout`/`ToolError`, а не semantic refusal bundle/candidate.

Для entry с candidate fold owner model body обязан быть root fold, candidate function result обязан быть единственным reachable fold node. Binder требует совместимые `T,N,A`, environment arity/types и one-to-one pairing. Candidate и owner fold inputs могут иметь разные выражения; equality каждого ordered input доказывается отдельной обязанностью до входа в loop, а не предполагается parser. Если хотя бы одна equality не доказана, fold не получает `Verified`.

#### Semantic invariant AST

Invariant не повторяет parameter list. Он использует тот же `ProofExpressionV04`, что owner `requires`, но вместо `param` fold context доступен единственным набором role operations. `param` запрещён в invariant, а `fold.*` запрещены в owner `requires`:

| Op | Поля | Type / semantics |
| --- | --- | --- |
| `param` | `id` | exact entry parameter type; только owner `requires` |
| `fold.prefixLength` | — | I64; только invariant, всегда `0..256` |
| `fold.sequence` | — | exact candidate `Seq<T,N>` |
| `fold.initialAccumulator` | — | `A` |
| `fold.accumulator` | — | `A` current prefix result |
| `fold.environment` | `position` canonical unsigned decimal string | 0-based position в `args[2..]`; соответствующий environment type |
| `proof.bound` | `binderId` | I64, только внутри matching quantifier |
| `i64.const` | `value` canonical signed decimal string | I64; обязателен I64 range |
| `bool.const` | `value` JSON Boolean | Bool |
| `math.const` | `value` canonical signed decimal string | unbounded `MathInt` |
| `eq` | `args` length 2 | Bool; operands одного exact type, включая record/sequence |
| `i64.add`, `i64.sub` | `args` length 2 | I64; checked range на каждом reachable path |
| `i64.le`, `math.le` | `args` length 2 | Bool; соответственно два I64 либо два MathInt |
| `bool.not` | `args` length 1 | Bool; strict definedness |
| `bool.and`, `bool.or` | `args` length 2 | Bool; оба operands обязаны быть defined |
| `if` | `args` length 3 | Bool condition, одинаковый branch type; только выбранная branch обязана быть defined |
| `math.from_i64` | `args` length 1 I64 | MathInt |
| `math.add`, `math.sub` | `args` length 2 MathInt | MathInt |
| `record.make` | `recordType,fieldIds,args` | прежняя v0.3 canonical field/arg pairing |
| `record.get` | `fieldId`, `args` length 1 | declared field type |
| `seq.empty` | `elementType,capacity,args: []` | прежний exact bounded sequence type |
| `seq.length` | `args` length 1 | I64; sequence capacity гарантирует range |
| `seq.get` | `args` length 2: sequence, I64 index | element; requires separately proven range |
| `seq.append` | `args` length 2: sequence, item | тот же sequence type; requires proven capacity |
| `seq.sum_i64` | `args` length 1 `Seq<I64,N>` | total MathInt sum всех текущих items |
| `forall.sequence` | `binderId,sequence,body` | Bool; bound пробегает `0..seq.length(sequence)-1` |

Каждый proof object содержит string field `op`, field `type: ProofTypeRef` и только opcode-specific fields из таблицы. `ProofTypeRef` — существующий owner TypeRef wire (`"I64"`, `"Bool"`, record ID либо object `Seq`) или новый exact string literal `"MathInt"`; `MathInt` запрещён внутри record/sequence TypeRef и executable model. `args` — ordered JSON array proof objects; `sequence` и `body` у `forall.sequence` — отдельные proof objects, а не aliases в `args`. Role operations не имеют `args`. `position` и numeric `value` кодируются JSON strings; `bool.const.value` — единственное Boolean значение. Object property order незначим на входе и фиксируется codec; array order всегда семантичен. Unknown/extra field, неверная arity, out-of-range environment position и несогласованный declared `type` отклоняются до IR.

`MathInt` существует только здесь и не является module value/return type; index/length остаются bounded I64, поэтому v0.3 predicate semantics не получают второго способа выразить доступ к sequence. Binder IDs используют обычные ID rules, видимы только в `body` своего `forall.sequence`; shadowing, ссылка вне scope и nested `forall.sequence` запрещены. Quantifier sequence вычисляется ровно один раз; empty sequence делает quantifier истинным без вычисления body. Candidate invariant ограничен `<=1024` nodes и depth `<=32`; owner expressions сохраняют aggregate `maxExpressionNodes` и `maxExpressionDepth` v0.3. Capacity `<=256`, запрет nesting, static worst-case cost и signed limit исключают неограниченную evaluator работу.

Fold-specific validation использует стабильные коды `InvariantRequired`, `UnsupportedProofOpcode`, `FoldEnvironmentOutOfRange`, `NestedFoldNotSupported`, `FoldStepCallNotSupported`, `ProofExpressionNodeLimitExceeded`, `ProofExpressionDepthExceeded`, `NestedProofQuantifierNotSupported`, `ProofWorstCaseCostExceeded` и `ProofEvaluationStepLimitExceeded`. Общие нарушения closed object, arity/type/region/capture сохраняют существующие `SchemaInvalid`, `ArityMismatch`, `TypeMismatch`, `RegionParametersMismatch` и `DanglingNodeArg`. Parser применяет общий порядок E05: transport/codec → schema/IDs → dependency/type checks; внутри одного класса diagnostics сортируются по canonical entity/proof path.

Owner `requires` и agent invariant обязаны быть total: первый — для всех entry parameters своего синтаксического domain, второй — под owner `requires` и generated `0 <= prefixLength <= sequence.length`. Ни один из них не может использовать собственный ещё не доказанный conjunct как precondition partial operation. `bool.and/or` lower-ятся через strict proof helpers, `if` остаётся единственным lazy guard. Invariant может только усиливать proof. Удаление или изменение invariant меняет candidate source digest, но не owner bundle digest и не требует semantic re-approval; новый candidate proof обязателен.

#### Обязательные generated invariants

Compiler всегда конъюнктит:

1. `0 <= i <= |candidateSequence|`;
2. candidate sequence/environment/initial остаются immutable;
3. `acc == OwnerPrefix(entryParameters,i)`, где prefix использует owner inputs, доказанно равные candidate fold args;
4. agent semantic invariant, instantiated for current prefix.

Source AST не предоставляет agent-у slots или identifiers для удаления либо переопределения этих clauses. Semantic invariant может логически повторять выводимый факт: compiler не решает semantic equivalence и не отклоняет такое повторение, но всегда независимо добавляет собственные clauses. Поэтому structural/type/termination invariants описываются один раз compiler-ом, а agent обязан задать semantic property, нужное для definedness owner/candidate step. Для OrderedExactAllocation это минимум `0<=remaining<=initialAccumulator.remaining`, `|allocations|=prefixLength`, conservation через `seq.sum_i64`; generated exact-prefix equality связывает property с owner model.

Список generated clauses закрыт этой версией, детерминированно показывается в human projection и proof manifest вместе с agent AST. Их lowering принадлежит compiler toolchain и связывается `toolchainDigest`; compiler не добавляет скрытые semantic assumptions. Отсутствующий agent invariant отклоняется до proof, а `bool.const true` допустим только как явная проверяемая гипотеза и не освобождает от всех generated obligations.

#### Dafny proof contract

Lowerer сначала создаёт owner-only predicate `OwnerRequires(entryParameters)` и доказывает equality каждой пары candidate/owner inputs под ним. После этого он создаёт proof-only recursive `OwnerPrefix(entryParameters, n)` с `requires OwnerRequires(entryParameters) && 0<=n<=|ownerSequence(entryParameters)|`, `decreases n` и `ensures` agent invariant, подставленный через уже доказанные input equalities. Function body вычисляет owner step слева направо. Отдельные obligations доказывают invariant initial и owner preservation, включая definedness owner step; owner `requires` и agent invariant присутствуют как проверяемые contract/body facts, а не `assume`, и failure well-formedness нельзя скрыть внутри recursive function.

Candidate компилируется в bounded Dafny `while` с compiler-owned `i`, `decreases |sequence|-i` и четырьмя generated invariants выше. Step-region nodes lowering-ятся внутрь loop; результат присваивается accumulator ровно один раз в конце iteration. После loop generated prefix equality и proof input-equivalence обязаны вывести `result == ownerModel(parameters)`.

Stable obligation IDs:

```text
function/<functionId>/fold/<nodeId>/input-equivalence/<argPosition>
function/<functionId>/fold/<nodeId>/initial
function/<functionId>/fold/<nodeId>/invariant-definedness/<proofPath>
function/<functionId>/fold/<nodeId>/owner-preservation
function/<functionId>/fold/<nodeId>/owner-range/<proofPath>
function/<functionId>/fold/<nodeId>/candidate-preservation
function/<functionId>/fold/<nodeId>/range/<stepNodeId>
function/<functionId>/fold/<nodeId>/final
function/<functionId>/fold/<nodeId>/termination
```

`argPosition` — canonical 0-based decimal position в полном fold `args`; `proofPath` строится из canonical child segments (`args/<n>`, `sequence`, `body`). Generated symbol/line names не входят в identity. Source map связывает fold, step nodes, proof expression paths и obligation IDs. Несколько failed obligations сортируются по obligation ID. Failed assertion без independently replayed input остаётся `Unproven` по E05 outcome rules.

#### Первый proof family

OrderedExactAllocation из E05 остаётся owner task: `available>=0`, `requests: Seq<I64,256>`, каждый request `>0`; последний predicate выражается единственным `forall.sequence(requests, 1 <= seq.get(requests, bound))`. Accumulator record содержит `remaining` и `allocations`. Step выбирает request или zero и append-ит allocation. Safe candidate и альтернативный candidate используют разные scalar/branch DAGs, но один owner fold model.

Wrong candidate меняет refusal branch либо order и не получает exact outcome. Weak/false invariant, missing length relation, over-capacity append и partial `seq.get` дают отдельные stable obligations. Empty, MAX boundary и `[4,3,1]` witnesses совпадают в owner/candidate evaluator.

ClampSeries/imported Clamp, nested fold и public library/CLI facade остаются последующими E05 slices. Noita fixture K-E05-064 не входит в acceptance этого изменения и не влияет на opcode design.

Visual planning artifact: Не применимо — UI нет; review surface — fold projection, generated invariant list и obligation map.

UI test video evidence: Не применимо — UI automation отсутствует.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| F-S1 | Выполнить fold `[4,3,1]` с available=5 | record remaining=0, allocations=[4,0,1] | evaluator report | F-AC2 |
| F-S2 | Проверить два candidate против одного owner model | Оба Verified; reference и generated .NET дают одинаковые witnesses | Dafny/evaluator/consumer evidence | F-AC2, F-AC4 |
| F-S3 | Изменить refusal/order step | Replayed Counterexample либо Unproven без допуска | mutation report | F-AC5 |
| F-S4 | Ослабить/испортить invariant | Stable initial/preservation/range/final obligation | proof report | F-AC5 |
| F-S5 | Empty/MAX input | Empty не вызывает step; MAX не overflow-ится | boundary report | F-AC3 |
| F-S6 | Подать nested fold/call/hidden capture | Typed parser refusal до prover | conformance report | F-AC1 |
| F-S7 | Мигрировать owner v0.3 | Новый v0.4 digest; старое approval не применяется | migration report | F-AC6 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Error case | Notes |
| --- | --- | --- | --- | --- |
| valid module without fold | parse/compile | прежний IR unchanged | regression failure blocks | schema literal stays v0.2 |
| fold source | compile | immutable FoldRegionIr | bad roles/types/capture fail | one reachable fold |
| fold IR + values | evaluate | exact immutable accumulator | budget/partial op no result | shared budget |
| owner v0.3 | v0.4 parser | migration required | no implicit alias | new digest |
| paired owner/candidate fold | lower/verify | Verified or typed proof outcome | no hidden invariant | exact prefix generated |

### 6.5 Decision Ledger

| Decision | Owner | Chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Все ли invariants пишет agent | agent proposal | Один semantic invariant; structural/termination/exact-prefix compiler-owned | 0.94 | Ручное дублирование создаёт drift и лишние способы | Да, предмет approval |
| Loop control | agent proposal | Fixed left fold, compiler owns index/decreases | 0.99 | Свободный loop расширяет termination/state space | Да, предмет approval |
| Owner fold version | agent proposal | v0.4, root-only model fold | 0.93 | Молчаливое расширение v0.3 меняет approved semantics | Да, предмет approval |
| Nested fold | agent | Отложить | 0.95 | Proof/resource explosion скрывает первый contract | Нет |
| First family | owner E05 | OrderedExactAllocation | 0.98 | Предметный scanner исказит opcode choice | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source | Expected change | Compatibility | Verification |
| --- | --- | --- | --- | --- |
| Module node | v0.2 closed ops | add promised `fold` shape | old modules same bytes/IR | golden regression |
| Typed IR | RegionIr if-only nesting | FoldRegionIr + invariant | codec version remains v0.2 | roundtrip/hash |
| Owner contract/model | bundle v0.3 no quantifier/fold | v0.4 finite `requires` quantifier + root fold model | explicit migration/new digest | golden migration + requires/model fixtures |
| Runtime | DAG/if evaluator | deterministic bounded fold | no partial result | differential cases |
| Proof | expression lowering | owner prefix + candidate while | exact outcome preserved | Dafny matrix |

## 7. Бизнес-правила / Алгоритмы
1. Fold всегда обходит snapshot sequence слева направо и ровно один раз.
2. Index начинается с `0`; empty fold возвращает initial accumulator.
3. Step не видит outer values без explicit environment.
4. Agent invariant обязателен, но не может заменить generated exact-prefix equality.
5. Compiler не добавляет hidden owner `requires` для успешного proof.
6. Owner model и candidate step оба обязаны быть defined на всех reachable prefixes утверждённого domain.
7. `Verified` требует initial, owner/candidate preservation, range, final и termination obligations.
8. Development fixtures не становятся held-out evidence широты.

## 8. Точки интеграции и триггеры
- `ModulesParser.ParseModule`, `ModulesCompiler.Compile`, `ModulesCodec.Canonicalize`.
- `ModulesReferenceEvaluator.Invoke`.
- owner parser/codec/evaluator/binder v0.4.
- `ModulesDafnyLowerer.Lower(ModuleIr, OwnerBundle)`.
- Modules conformance и bounded Dafny harness.

## 9. Изменения модели данных / состояния
- `FunctionNode`/`IrInstruction` получают `StepRegion` и `FoldInvariant`; `if` regions остаются отдельными полями.
- Добавляются immutable `FoldRegionIr` и proof expression algebra с закрытыми constructors/internal validation.
- Owner bundle v0.4 добавляет proof predicate extensions для `requires` и root fold model; v0.3 immutable historical artifact не переинтерпретируется.
- Runtime persistent state отсутствует: fold чистый, значения immutable.

## 10. Миграция / Rollout / Rollback
- Сначала source/IR/reference semantics без заявления proof; checkpoint commit.
- Затем owner v0.4 evaluator/binder/migration; checkpoint commit.
- Затем Dafny prefix/loop proof и negative matrix; checkpoint commit.
- v0.3→v0.4 migration создаёт новый artifact/digest; approval не переносится.
- Rollback возвращает последний v0.3/composite checkpoint; v0.4 artifacts остаются historical и не принимаются старым parser.

## 11. Тестирование и критерии приёмки

| Acceptance criterion | Automated test | Manual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| F-AC1 Strict fold schema/IR | valid/shuffled roundtrip; arity/type/result/capture/nested/call/limit negatives; `math.le` positive и wrong-type/unknown-op negatives; exact fold/proof error codes | inspect canonical fold projection | conformance report | — |
| F-AC2 Reference/.NET semantics | empty, `[4,3,1]`, refusal, order, immutable input/environment, shared budget; internal generated .NET consumer differential on empty/normal/MAX/order | compare exact values/steps/consumer outputs | evaluator + generated consumer report | — |
| F-AC3 Boundaries and proof definedness | capacities 0/1/256, I64 MIN/MAX, append/get range; paired owner-requires/invariant `false and partial`, `true or partial` fail, guarded `if` passes; empty `forall.sequence` skips body, nonempty evaluates it; cost `limit/limit+1/>262144`; два witnesses по `200000` различают per-evaluation counter reset | stable error/obligation locus, strict helpers, canonical cost details, no partial value | boundary/definedness report | — |
| F-AC4 Exact fold proof | owner v0.4 quantified positive-requests domain + two alternative allocation candidates; owner step total only under owner requires passes, generated prefix without that domain fails negative source check | inspect prefix function/loop/all generated and agent invariants | Dafny report/source map/proof manifest | — |
| F-AC5 Mutations fail closed | wrong branch/order, false/weak/missing invariant, out-of-scope bound, owner/candidate partial ops, forged result; type-correct sequence/initial/each environment mutation with unchanged step | exact `input-equivalence/<argPosition>`; replay-only Counterexample, otherwise Unproven | mutation report | — |
| F-AC6 Version/migration | v0.3 direct reject; golden v0.4 migration covers every inherited op with byte-identical requires subtree; four copied limits + new `262144`; new quantified requires; approval non-carryover | old/new projection | migration report | — |
| F-AC7 Identity/determinism | shuffled set-like input same digest; step/arg/invariant changes alter digest; two clean lowerings byte-equal | source IDs absent generated syntax | determinism report | — |
| F-AC8 Existing behavior | Modules + Reserve + E04 full relevant regressions | counts/hashes | regression report | — |
| F-AC9 Scope honesty | report marks nested/import/helper/public facade/G05/G06 open; K-E05-064 remains development-only | docs review | report/knowledge log | — |

Планируемые gates: `dotnet build -c Release --no-restore`; `dotnet run --project tests/Strogo.Modules.Conformance -c Release --no-build`; `pwsh -File tools/Test-Modules-Dafny-Lowering.ps1 -RunDirectory artifacts/local-validation/e05/<run-id>`; `dotnet run --project tests/Kernel.Conformance -c Release --no-build -- --suite all --report artifacts/local-validation/e05/<run-id>/reserve.json`; `dotnet run --project tests/Kernel.Graph.Conformance -c Release --no-build`; `git diff --check`. Exact commands и tool hashes фиксируются в EXEC report. Fresh artifacts пишутся только в новый run directory и не перезаписывают historical evidence. Идентичный solver failure не повторяется без новой гипотезы.

## 12. Риски и edge cases
- Generated exact-prefix invariant ограничивает первый slice алгоритмами с accumulator type, совпадающим с owner fold; альтернативные representation требуют будущего relational invariant contract.
- Agent invariant входит в proof TCB path через parser/lowering; raw backend syntax запрещён, negative injection fixtures обязательны.
- Owner prefix recursion и candidate loop могут расходиться из-за off-by-one/order; evaluator/Dafny cases различают empty, first, last и mutation order.
- `seq.sum_i64` использует MathInt и не означает executable BigInteger; возврат в I64 требует отдельного range proof.
- Запрет nested fold ограничивает scanner/merge tasks; это открытая выразительность, а не скрытый успех.
- Успешный bounded proof не измеряет runtime performance или G05.
- Source literal остаётся `strogo.module.v0.2`, потому что fold уже входит в утверждённую v0.2 нотацию; фактическая поддержка подмножества связывается pinned `toolchainDigest`, а старый parser продолжит отвергать fold. Это migration risk одного schema version и не допускает заявления cross-toolchain compatibility.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Почему invariant обязателен | Пользователь спрашивал о строгом описании всех invariants | Обязателен один semantic invariant; очевидные structural/exact clauses генерируются | needs owner approval |
| Почему owner v0.4 сразу после v0.3 | Формат ещё не реализован | Fold расширяет accepted semantics; новый digest/version исключает reinterpretation | mitigated |
| Не диктует ли model алгоритм | Exact prefix похож на одну реализацию | Candidate step DAG может отличаться, но первый slice сохраняет тот же accumulator representation | accepted limitation |
| Почему без nested fold | Board tasks могут требовать два обхода | Сначала проверяется один bounded loop contract; unsupported результат сохраняется | accepted limitation |
| Где преимущество перед Dafny | Backend уже умеет loop invariants | Эксперимент проверяет canonical agent-facing constraint/lowering, advantage ещё не заявляется | mitigated |

### Rework Prevention Checklist
- Fold source/owner/proof forms закрыты.
- S1–S7 связаны с F-AC1–F-AC9.
- Invariant ownership и generated clauses разделены.
- Negative proof/outcome/version cases обязательны.
- Residual expressivity и G05/G06 claims ограничены.

## 13. План выполнения
1. После approvals: source/IR/parser/codec/reference evaluator и conformance; checkpoint commit.
2. Owner bundle v0.4 model/evaluator/binder/migration; checkpoint commit.
3. Dafny owner prefix/candidate loop/obligation map и positive/negative proof matrix; checkpoint commit.
4. Full regressions, documentation, evidence manifest, knowledge closure и independent post-EXEC review.

## 14. Открытые вопросы
Блокирующих вопросов для первого slice нет. Relational invariants с другим accumulator representation, nested folds, helper/import calls и general proof lemmas требуют следующих SPEC. Если owner не принимает compiler-generated exact-prefix restriction, EXEC не начинается и выбирается отдельный relational design.

## 15. Соответствие профилю
- Профиль: language-contract / proof-lowering, `product-system-design`.
- Выполнено: scope/outcome, closed schemas, ownership, state-free execution, proof/application loci, migration, negative cases и evidence boundaries.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Strogo.Modules/Models.cs` | fold/proof immutable models | source/IR contract |
| `src/Strogo.Modules/Parser.cs` | fold roles/types/capture/limits | fail-closed validation |
| `src/Strogo.Modules/Codec.cs` | canonical fold/invariant payload | stable identity |
| `src/Strogo.Modules/Compiler.cs` | FoldRegionIr binding | deterministic IR |
| `src/Strogo.Modules/ReferenceEvaluator.cs` | bounded left fold | executable oracle |
| `src/Strogo.Modules/OwnerContracts.cs`, `OwnerBundleParser.cs`, `OwnerBundleCodec.cs`, `OwnerContractSemantics.cs` | v0.4 fold model/migration/evaluation | owner exact outcome |
| `src/Strogo.Modules/DafnyLowering.cs` | prefix model, while, invariants/obligations | proof |
| `fixtures/modules-v0.2/**`, `tests/Strogo.Modules.Conformance/**`, `tools/Test-Modules-Dafny-Lowering.ps1` | distinguishing matrix | evidence |
| `docs/modules-v0.2.md`, `docs/knowledge-log.md` | public contract/findings | continuity |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Variable sequence | unexpressible/manual unroll | one bounded left fold |
| Loop control | отсутствует | compiler-owned index/termination |
| Invariant | обещан, schema не задана | one semantic proof AST + generated clauses |
| Owner model | composite expression tree | v0.4 root fold model |
| Proof | structural/range DAG | initial/preservation/final/termination |

## 18. Альтернативы и компромиссы
- Free while/recursion: шире, но резко увеличивает termination/state space и способы записи.
- Предметные map/scan/allocation opcodes: проще proof, но не проверяют composability и поощряют benchmark tailoring.
- Полностью ручной invariant: выразительно, но agent повторяет index/termination/exact-prefix clauses и может создать drift.
- Полностью synthesized invariant: меньше текста, но model totality часто требует domain-specific accumulator property.
- Выбран split: compiler владеет structural/termination/exact-prefix, agent один раз задаёт проверяемый semantic property.

## 19. Результат quality gate и review

### SPEC Linter Result

| № | Блок | Статус | Проверяемое основание |
|---:|---|---|---|
| 1 | A / outcome | PASS | Success, output и stop rules заданы в §1 |
| 2 | A / AS-IS | PASS | Текущий composite IR/evaluator/lowering и отсутствующий fold перечислены в §2 |
| 3 | A / problem | PASS | Корневой expressivity gap отделён от task-specific opcode в §3 |
| 4 | A / goals | PASS | Canonical traversal, invariant ownership и obligations перечислены в §4 |
| 5 | A / boundaries | PASS | Nested/import/helper/admission/G05/G06 вынесены в §5 |
| 6 | B / responsibilities | PASS | Source, IR, owner, evaluator, lowerer и harness распределены в §6.1 |
| 7 | B / integrations | PASS | Конкретные parser/compiler/evaluator/lowerer entry points заданы в §8 |
| 8 | B / algorithms | PASS | Left fold, strict evaluation, proof AST и initial/preservation/final описаны в §6–7 |
| 9 | B / errors | PASS | Stable parse/proof outcomes, ordering и отсутствие partial result заданы в §6.2 |
| 10 | B / performance | PASS | `O(length * step)`, возможный quadratic append и отсутствие G06 claim раскрыты в §6.2 |
| 11 | C / data/state | PASS | Immutable IR/value algebra и отсутствие persisted runtime state заданы в §9 |
| 12 | C / compatibility | PASS | Source v0.2 subset и owner v0.3→v0.4 migration/new digest заданы в §6.2/§10 |
| 13 | C / rollback | PASS | Возврат к composite/v0.3 checkpoint без reinterpretation описан в §10 |
| 14 | D / measurable AC | PASS | F-AC1–F-AC9 содержат наблюдаемые pass/fail результаты |
| 15 | D / AC evidence | PASS | Каждый AC связан с automated/manual check и artifact, включая mutations |
| 16 | D / commands/stop | PASS | EXEC фиксирует commands/fresh artifacts; повтор solver ограничен новой гипотезой |
| 17 | E / plan/dependencies | PASS | Owner v0.3 dependency и четыре checkpoint outcomes заданы |
| 18 | E / decisions/questions | PASS | Ledger, objections и deferred relational choices явны |
| 19 | E / scale/form | PASS | Large QUEST и expanded contract соответствуют migration/proof риску |
| 20 | F / profile | PASS | Language-contract/proof-lowering profile и review surface указаны |

Итог: ГОТОВО; critical review findings закрыты.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один first-fold slice |
| 2. Понимание текущего состояния | 5 | Composite evaluator/lowering baseline указан |
| 3. Конкретность целевого дизайна | 5 | Schemas, semantics, obligations заданы |
| 4. Безопасность | 5 | New owner version и rollback |
| 5. Тестируемость | 5 | Positive/alternative/mutation/boundary matrix |
| 6. Готовность к автономной реализации | 5 | File scope и checkpoints заданы |

Итоговый балл: **30 / 30**. Зона: готово к автономной реализации после approval и выполнения dependency.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Exact allocation model сохранён без Reserve state? | PASS | Pure family isolated |
| UX / designer | not applicable | UI отсутствует; projection reviewable? | PASS | Fold/invariants/obligations visible |
| Tester / validation | applicable | Off-by-one, invariant и partial step различаются? | PASS | F-AC1–F-AC9 |
| Developer / architect | applicable | Ownership/version/lowering coherent? | PASS | Split invariants + v0.4 |
| Delivery / operations / security | applicable | Digests/migration/rollback fail closed? | PASS | New domain/no carry-over |

### Post-SPEC Review
- Статус / stop decision: **PASS**; можно запрашивать подтверждение владельца. Normative content snapshot SHA-256 `F099F214B6B090F79CDCFC7F5825CEA6A2692A1A2996DD44138A5A5EF0D1FDE3`; после PASS изменена только эта review record и журнал действий.
- Scope reviewed: эта SPEC; локальный `AGENTS.md`; E05 baseline; owner-composite v0.3; current `Models.cs`, parser, compiler, codec, evaluator и Dafny lowerer; planned files §16; open relational/nested/helper questions §14.
- Decision: approval можно запрашивать для bounded fold v0.1; EXEC остаётся gated exact phrase и предварительной реализацией owner v0.3.
- Scope/Evidence pass: проверены current source constraints, E05 op/invariant promises, owner v0.3 expression/type/limit/migration contracts, семь scenarios, девять AC и exact validation commands.
- Contract pass: один left fold, owner exact model, обязательный split invariant, input equality, strict partiality, migration, generated .NET evidence и открытые E05 boundaries согласованы.
- Adversarial risk pass: рассмотрены missing inherited op, forged proof type, unbounded/nested quantifier, OwnerPrefix вне owner domain, пропущенный fold arg, short-circuit partiality, отсутствующий .NET run, divergent allow-list и ambiguous counter reset.
- Role-Based pass: business/domain — positive-request allocation выражается без task opcode; tester — distinguishing mutations/strictness/budgets; architect — closed source/owner/proof schemas и total prefix; delivery/security — signed limit/new digest/rollback; UX не применим, textual projection обязательна.
- Fix and re-review: исправлены все findings, пересчитан hash, повторены contract/adversarial/role passes; separate reviewer дал PASS на exact snapshot. Reviewer работал процедурно read-only, но фактический sandbox был writable `danger-full-access`, поэтому это не заявляется как технически read-only sandbox.
- Evidence inspected: строки source contracts и limits, E05 §6.2/§7/AC, owner v0.3 wire/migration/proof contracts, `git diff --check`, UTF-8/BOM/hash и structural counts.
- Depth checklist: scope drift/unrelated changes — только новая SPEC до knowledge entry; acceptance/scenarios — complete mapping; validation — planned exact commands; unsupported claims — G05/G06/public facade excluded; regression/edge — empty/MAX/order/partial/budget/version; docs — update required in EXEC; hidden contract — owner v0.4/new digest explicit; manual challenge — exact-prefix ограничивает accumulator representation и сохраняется residual risk.

| Severity | Area | Finding / required action | Status |
| --- | --- | --- | --- |
| HIGH | Migration | Полный v0.3 operation set отсутствовал в v0.4 allow-list; включить и проверить golden subtree | fixed, re-reviewed |
| HIGH | Proof type | String-only `type` не выражал Seq/record/MathInt; определить closed `ProofTypeRef` | fixed, re-reviewed |
| HIGH | Resources | Arbitrary range/nesting не имели bound; заменить bounded `forall.sequence` и signed cost limit | fixed, re-reviewed |
| HIGH | Owner totality | `OwnerPrefix` не сохранял owner domain; добавить `OwnerRequires(entryParameters)` | fixed, re-reviewed |
| HIGH | Input equality | AC не различали sequence/initial/environment assumptions; добавить per-position mutations | fixed, re-reviewed |
| HIGH | Definedness | AC не различали strict proof Boolean и lazy guard; добавить paired cases | fixed, re-reviewed |
| MEDIUM | Execution evidence | Success обещал generated .NET execution без consumer check | fixed, re-reviewed |
| MEDIUM | Closed allow-list | `math.le` расходился между summary и таблицей | fixed, re-reviewed |
| MEDIUM | Budget scope | Counter reset/saturation и batch semantics были неоднозначны | fixed, re-reviewed |

- No-findings justification after fixes: closed schemas, owner-domain assumptions, static/dynamic cost, obligation identity и AC теперь однозначно различают рассмотренные plausible bugs; открытых BLOCKER/HIGH/MEDIUM нет.
- Needs human: принять или отклонить split invariant, root-only exact-prefix slice и owner v0.4 через approval этой SPEC.
- Residual risks / follow-ups: exact-prefix требует одинаковый accumulator representation; SMT может дать `Unproven`/`Timeout`; evaluator/lowering/generated consumer входят в TCB; same-version module support зависит от pinned toolchain; G05/G06, nested fold, helpers/imports и public facade открыты.

### Post-EXEC Review
- Статус: Не выполнен до EXEC.

## Approval
Ожидается фраза: **«Спеку подтверждаю»**.

Подтверждение распространяется только на fold v0.1, owner-bundle v0.4 и локальные checkpoint commits. Admission, imports/helpers/nested folds, push, merge, release и публикация не разрешаются.

## 20. Журнал действий агента

| Фаза | Тип | Уверенность | Не хватает | Следующее действие | Нужен человек | Фактическое решение | Объяснение | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Expressivity gap | 0.99 | Нет | Общий fold вместо task opcode | Нет | Да | Variable sequence tasks иначе невыразимы | E05, K-E05-064 |
| SPEC | Invariant ownership | 0.96 | Owner approval | Подтвердить split design | Да | Ещё нет | Compiler генерирует structural/exact clauses; agent supplies one semantic invariant | эта SPEC |
| SPEC | Owner predicate gap | 0.99 | Нет | Добавить bounded quantified requires | Нет | Нет | Allocation domain `each request > 0` невыразим в owner v0.3 | эта SPEC §6.2 |
| SPEC | Adversarial hardening | 0.99 | Нет | Исправить и повторить review | Нет | Нет | Закрыты migration/type/resource/domain/evidence ambiguities | Post-SPEC Review, hash `F099F214…F0D1FDE3` |
