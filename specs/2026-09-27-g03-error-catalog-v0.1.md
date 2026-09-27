# G03: каталог частых классов ошибок и правило оценки v0.1

## 0. Метаданные

- Тип / профиль: `delivery-task` / `product-system-design`.
- Владелец: владелец Strogo; автор: Codex.
- Масштаб: large. Меняется канонический исследовательский контракт G03, машинно читаемый denominator и правило будущей оценки языка.
- Целевое семейство / behavior baseline: G03; косвенно G01, G04 и подготовка G05. Baseline — формулировка [`docs/project-intent.md`](../docs/project-intent.md), E09 offline calibration и фактический E10 terminal result.
- Поверхность: Codex для подготовки; сам каталог и evaluator не зависят от модели.
- Effective runtime: не применимо к нормативному результату; исследование и SPEC подготовлены в текущей Codex-сессии, но будущая оценка G03 должна быть детерминированной и model-independent.
- Eval baseline / evidence: внешние источники S01–S08 в §6.2.2; существующие E09/E10 artifacts используются только для границ методики, не как подтверждение достижения G03.
- Целевая ветка: `main`; checkpoint commits и push разрешены владельцем ранее для этой цели.
- Unlimotion: задача `de82f5c1-21aa-4a10-989e-b04fe8b487f9`, agent `codex-strogo`, lease `4e035b22-a1f0-4e29-9c28-3d1dc861c09a`.
- Instruction stack: central `creator-vibe-lens`, `model-behavior-baseline`, `quest-governance`, `collaboration-baseline`, `tool-execution-baseline`, `testing-baseline`, `quest-mode`, `spec-linter`, `spec-rubric`, `review-loops`; context `testing-dotnet`; profile `product-system-design`; локальный [`AGENTS.md`](../AGENTS.md).
- Ограничения: на фазе SPEC меняется только этот файл. До exact approval не создаются каталог, schema, evaluator, fixtures и другие project artifacts.
- Связанные документы: [`docs/project-intent.md`](../docs/project-intent.md), [`specs/2026-09-16-e09-offline-equivalence-calibration-v0.1.md`](2026-09-16-e09-offline-equivalence-calibration-v0.1.md), [`specs/2026-09-26-e10-live-paired-pilot-v0.1.md`](2026-09-26-e10-live-paired-pilot-v0.1.md), [`docs/knowledge-log.md`](../docs/knowledge-log.md).

## 1. Overview / Цель

Нужно заранее, до второго прикладного домена и основного сравнения G05, заморозить ответ на три вопроса:

1. какие семейства ошибок считаются частыми и относящимися к целевому классу задач Strogo;
2. какие исходы означают, что язык исключил ошибку, а какие лишь обнаружили её, отказались от полезной задачи или не дали валидного evidence;
3. какое точное правило позволяет или запрещает утверждение «Strogo исключает большинство частых классов ошибок».

Outcome contract:

- Исходное поручение: «Зафиксировать каталог частых ошибок и правило оценки G03» так, чтобы каталог нельзя было менять под удобный результат; неподдерживаемые полезные задачи и ложные отказы учитываются явно.
- Success means: владелец подтверждает смысл версии v0.1; human-readable projection, evidence register и normative machine-readable catalog имеют одну семантику; validator отвергает drift, неоднозначную классификацию и ложный majority; synthetic controls различают 7/12 от 6/12, regression specimen от family-level proof, runtime detection от pre-execution exclusion и отказ от полезной задачи от успеха.
- Итоговый артефакт: канонический каталог из 12 evidence-linked priority families, inclusion/exclusion register, зарегистрированные источники и ограничения, строгие JSON contracts, deterministic validator/scorer, conformance evidence и запись в журнале знаний. Он не объявляет эти 12 семейств статистически репрезентативным распределением всех target defects.
- Stop rules: до exact approval — только SPEC. После approval остановиться без G03-claim при schema/catalog mismatch, спорной эквивалентности specimen, отсутствии mandatory positive control, изменённом denominator после candidate exposure, неполном domain mapping, evaluator disagreement либо infrastructure failure.

## 2. Текущее состояние (AS-IS)

- [`docs/project-intent.md`](../docs/project-intent.md) требует каталог, основания частоты, правило «большинства» и отдельный учёт runtime guard, unsupported tasks и false refusals.
- Reserve v0, E04, E08 и E09 подтверждают конкретные механизмы внутри закрытых профилей. Они не устанавливают охват большинства частых ошибок.
- E09 содержит 21 negative category, но это преимущественно целостность schema/frontend/evaluator/provenance, а не каталог прикладных ошибок.
- E10 подтвердил preflight/no-retry/quarantine controls на одном exposed run, но paired comparison не состоялся и данных по G03/G05 не дал.
- В репозитории нет versioned G03 catalogue, воспроизводимого inclusion/exclusion register, правил применимости к доменам, фиксированного denominator, generalization evidence для семейства и машинной защиты от подмены каталога.
- Внешние источники используют разные выборки и единицы: commit, bug, CVE, failure, mutation. Их проценты нельзя складывать или напрямую использовать как общую частоту.

## 3. Проблема

Без frozen ontology почти любой результат можно объявить успехом: раздробить удобные ошибки на много классов, объединить неудобные, исключить неподдерживаемые задачи из denominator, засчитать runtime exception как предотвращение или подобрать mutant, который язык не умеет выражать вообще. Тогда число «большинство» не проверяет исходный замысел.

## 4. Цели дизайна

- Зафиксировать evidence-linked priority families независимо от текущих возможностей Strogo и не называть их статистически частыми без отдельного target-prevalence evidence.
- Связать включение каждого семейства с внешней эмпирикой и целевым task envelope, сохранив ограничения источников.
- Исключить игру гранулярностью: один стабильный верхний уровень и одинаковое all-or-nothing правило для семейств.
- Разделить implementation defect относительно подтверждённого контракта, ошибку самого контракта, infrastructure failure и harness defect.
- Считать предотвращением только невыразимость при сохранённой полезной выразительности, отказ до исполнения или доказанное отсутствие ошибки у принятой программы.
- Сделать правило детерминированным, воспроизводимым и пригодным для двух и более доменов.
- Сохранить совместимость с будущими G05/G06 experiments через additive experiment library/CLI.

## 5. Non-Goals

- Не утверждать в этом этапе, что G03 достигнута или что каталог статистически представляет большинство реальных ошибок target envelope.
- Не измерять G05/G06, производительность, стоимость агента или число тестов.
- Не выбирать второй прикладной домен и не добавлять возможности языка под каталог.
- Не считать ошибку/неполноту human-approved contract ошибкой реализации, которую язык способен автоматически исключить.
- Не объявлять CWE Top 25 распределением обычных business-logic bugs.
- Не выдавать synthetic mutants за полный набор реальных ошибок; они являются воспроизводимыми representatives с известной ограниченностью.
- Не менять исторические E09/E10 reports, corpus или frozen evaluator.
- Не требовать, чтобы каждый домен искусственно выражал все 12 семейств.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент / файл после approval | Ответственность |
| --- | --- |
| `docs/g03-error-catalog-v0.1.md` | Human-readable projection каталога, границ и правила majority; mismatch с JSON инвалидирует release |
| `docs/research/g03-evidence-register-v0.1.md` | Точные факты источников, дата доступа, candidate universe, inclusion/exclusion/merge/split register и threats-to-validity |
| `fixtures/g03-error-catalog/v0.1/source-row-manifest.json` | Normative ordered inventory всех raw rows S01–S08 с exact source revisions/counts |
| `fixtures/g03-error-catalog/v0.1/catalog.json` | Единственный normative machine-readable source of truth с 12 stable IDs и scoring contract |
| `fixtures/g03-error-catalog/v0.1/schemas/*.schema.json` | Closed schemas для catalog, domain registry, evaluation plan, observations и report |
| `fixtures/g03-error-catalog/v0.1/controls/*.json` | Synthetic scoring controls, не результаты Strogo |
| `src/Strogo.Experiments/G03Catalog.cs` | Strict parse, semantic validation, digest и deterministic score |
| `src/Strogo.ExperimentCli/Program.cs` | `g03 validate` и `g03 score` без изменения существующих команд |
| `tests/Strogo.Experiments.Conformance/G03Cases.cs` | Positive/negative contract checks и anti-gaming regressions |
| `docs/knowledge-log.md` | Значимые решения, подтверждённые ограничения и будущие последствия |

### 6.2 Детальный дизайн

#### 6.2.1 Target task envelope

Каталог относится не ко «всему программированию», а к исполняемым bounded business modules, которые:

- получают типизированный input/event и состояние;
- вычисляют решение и новое состояние;
- могут читать/изменять ограниченные коллекции и persistent records;
- могут инициировать явно разрешённые внешние эффекты;
- обязаны обрабатывать ожидаемые failure outcomes;
- могут исполняться конкурентно, если домен это допускает;
- имеют human-approved contract с preconditions, postconditions, invariants, capabilities и resource bounds.

В envelope не входят UI layout, device drivers, arbitrary unsafe memory, unrestricted reflection/code generation, произвольная сеть/FS и correctness человеческой цели. Их включение потребует новой версии каталога, но не переписывания v0.1.

#### 6.2.2 Evidence register S01–S08

| ID | Primary / authoritative source | Зафиксированный факт | Использование | Ограничение |
| --- | --- | --- | --- | --- |
| S01 | [IBM ODC](https://research.ibm.com/publications/orthogonal-defect-classificationa-concept-for-in-process-measurements) | ODC задаёт устойчивые defect types и отделяет type от trigger/impact | Верхний контроль гранулярности: checking, assignment, algorithm, interface, timing/serialization и function-related defects | Taxonomy процесса, не частоты target bugs и не semantic proof |
| S02 | [ManySStuBs4J paper](https://arxiv.org/abs/1905.13334) и [dataset DOI](https://doi.org/10.7488/ds/2628) | 153 652 single-statement bug-fix changes из 1000 Java projects; 16 templates; около 33% simple fixes matched | Predicate, literal, operator, identifier, return/call representatives | Java/open-source/single-statement bias; mining noise; не покрывает сложные faults |
| S03 | [Campos & Maia](https://eduardocunha11.github.io/firstblog/papers/jsep19.pdf) | 4 590 405 bug-fix commits / 101 471 Java projects; IF-precondition был самым частым из пяти patterns; manual Defects4J: 45% if-related, 12.73% method-call, 7.31% assignment | Поддержка validation/conditional/call/dataflow families | Automated commit classification имеет false positives; пять patterns покрыли лишь часть fixes; проценты нельзя переносить на Strogo |
| S04 | [Just et al.](https://homes.cs.washington.edu/~mernst/pubs/mutation-effectiveness-fse2014.pdf) | На 357 real faults и 230 000 mutants coupling найден для 73%; 17% faults не coupled; conditional/relational replacement и statement deletion особенно значимы | Mutants допустимы как controlled representatives, но не как исчерпывающая реальность | Mutation score не равен real-fault coverage; нужны domain-specific и non-mutant controls |
| S05 | [2025 CWE Top 25](https://cwe.mitre.org/top25/archive/2025/2025_cwe_top25.html) | Ranking построен на 39 080 CVE; в нём есть missing/incorrect authorization, input validation, null dereference, deserialization и resource limits | Security relevance authority/input/interface/resource families | CVE prevalence × severity, а не частота обычных application bugs; memory-unsafe entries не включаются автоматически |
| S06 | [Lu et al., 105 concurrency bugs](https://www.microsoft.com/en-us/research/publication/learning-from-mistakes-a-comprehensive-study-on-real-world-concurrency-bug-characteristics/) | Исследованы 105 randomly selected real-world concurrency bugs в четырёх зрелых системах | Concurrency/atomicity family и необходимость schedule variants | C/C++ server/client systems 2008; не доказывает частоту в bounded managed modules |
| S07 | [Liu, Mondal, Chen](https://arxiv.org/abs/2405.15008) и [dataset](https://github.com/SPEAR-SE/empirical-db-issue-data) | 423 database-access bugs; SQL/query, schema и API categories покрыли 84.2% study bugs | Interface/schema/config/state mapping representatives | Семь Java database projects; не общий business-software denominator |
| S08 | [Yuan et al., OSDI 2014](https://www.usenix.org/conference/osdi14/technical-sessions/presentation/yuan) | 198 user-reported distributed-system failures; majority catastrophic failures связана с простыми ошибками error handling; checker нашёл 143 confirmed/fixed issues | Failure/recovery and ordered-input representatives | Distributed data systems; severity-focused failures, не частота всех defects |

#### 6.2.2.1 Reproducible derivation protocol

Candidate universe v0.1 — не выбранные вручную двенадцать строк. Evidence register обязан перечислить:

1. все ODC defect types из S01;
2. все 16 templates S02;
3. пять заранее изученных patterns и десять pervasive repair actions S03;
4. operator/fault groups S04, использованные в выводах о coupling;
5. все CWE 2025 Top 25 entries S05;
6. top-level concurrency categories S06;
7. пять categories и 25 root causes S07;
8. top-level failure/error-handling categories S08.

Каждая raw candidate row получает ровно одно решение: `IncludedAsFamily`, `MergedIntoFamily`, `ExcludedOutsideEnvelope`, `ExcludedRequirementOrHarnessDefect`, `ExcludedInsufficientSemanticBoundary` или `DeferredNeedsEvidence`. Unlisted row запрещена. Merge допускается только для одного violated semantic obligation; общий синтаксис сам по себе недостаточен. Split допускается, только если source различает причины и для частей нужны разные prevention/generalization rules. Для каждого merge/split сохраняются source row IDs и rationale.

Полнота candidate universe обеспечивается отдельным normative `source-row-manifest.json`. Для каждого S01–S08 он фиксирует canonical source ID, exact publication/dataset revision или archived snapshot URL, access date, ordered raw row IDs/labels, expected row count и per-source inventory digest. Per-source digest вычисляется по canonical projection `{sourceId,revision,snapshotUrl,rows}` без digest field. Top-level manifest содержит только ordered source IDs и total row count; self-digest field запрещён. Внешняя identity `sourceRowManifestDigest` вычисляется как SHA-256 полных canonical manifest bytes и хранится только в ссылающихся artifacts/reports. `catalog.json` ссылается на exact `sourceRowManifestDigest`; его `candidateDecisions[]` обязан образовывать биекцию с manifest rows: ровно одно решение на каждый raw row, без omission, duplicate и unknown row. Validator вычисляет manifest inventory сам, а не доверяет заявленному count. Любая смена source revision создаёт новую catalog version и approval.

Семейство входит в v0.1, если оно (a) имеет real-defect evidence хотя бы в одном S02–S08, (b) относится к target envelope, (c) имеет детерминированную границу относительно соседних семейств и (d) допускает matched negative/positive specimens без знания текущих возможностей Strogo. Это делает семьи **evidence-linked priority families**, но не доказывает их target-specific frequency. Проценты разных источников не нормализуются и не становятся весами.

Owner-level фраза «большинство частых классов ошибок» требует отдельного target-prevalence evidence: заранее определённый sampling frame реальных дефектов минимум двух целевых доменов, blind double coding по frozen catalogue, inter-rater agreement и опубликованный denominator. Пока такого корпуса нет, terminal result этой версии называется только `PriorityCatalogueMajoritySupported`/`PriorityCatalogueMajorityNotSupported`; `G03Achieved` запрещён.

#### 6.2.3 Frozen catalogue: 12 семейств

| Stable ID | Семейство | Ошибка относительно approved contract | Mandatory negative shapes v0.1 | Positive-control obligation | Evidence |
| --- | --- | --- | --- | --- | --- |
| `G03-F01` | `PreconditionValidation` | Пропущена/ослаблена проверка null, range, domain или обязательного входного условия | missing guard; inverted/partial guard; invalid input accepted as success | Валидный boundary input и специфицированный invalid outcome оба доступны | S02, S03, S05 |
| `G03-F02` | `ConditionalBoundary` | Неверная ветка, relational boundary, negation или boolean composition | `<↔<=`; predicate negation; `and↔or`/wrong branch | Обе нормативные ветки и точная граница достижимы | S02, S03, S04 |
| `G03-F03` | `ArithmeticQuantity` | Неверный operator/operand/literal/unit либо overflow/underflow | arithmetic replacement; wrong constant/operand; boundary overflow or unit mismatch | Нормальный расчёт и крайнее допустимое значение принимаются | S02, S04, ODC/S01 |
| `G03-F04` | `ValueDataflow` | Неверный source field/identifier, assignment, default, initialization или return mapping | wrong variable/field; omitted/overwritten assignment; wrong return/default | Различимые значения проходят end-to-end без вынужденного единственного default | S01, S02, S03, S04 |
| `G03-F05` | `CallInterface` | Пропущен/лишний/неверный transient call, выбран неверный callable/endpoint либо нарушена signature/parameter binding | call deletion/duplication; wrong callable; wrong parameter/order/type | Correct call и явно поддержанная callable/signature version выполняются | S01, S03, S07 |
| `G03-F06` | `CollectionCardinality` | Off-by-one, out-of-bounds, lost/duplicate element, неверные empty/cardinality semantics | index boundary; omit/duplicate item; empty/singleton aggregation error | Empty, singleton и upper-bound коллекции поддержаны согласно contract | S02, S04, S05 |
| `G03-F07` | `StateTransitionSequence` | Недопустимый transition, skipped/reordered step или нарушение invariant между состояниями | forbidden transition; missing/reordered transition; stale/wrong prior state | Минимум два разрешённых transition и отказ для запрещённого | S01, S07, S08 |
| `G03-F08` | `FailureRecovery` | Failure проглочен, неверно классифицирован/распространён, принят за success или recovery оставил неверное состояние | swallowed error; wrong error mapping; partial/incorrect recovery | Успех и минимум один expected external failure различимы | S03, S08 |
| `G03-F09` | `AuthorityCapability` | Операция выполняет неразрешённое read/write/delete/send, обходит scope/authorization или раскрывает protected data | missing authority; wrong subject/resource scope; forbidden sink/data flow | Разрешённая операция того же вида остаётся выразимой и проходит | S05 и исходный capability contract Strogo |
| `G03-F10` | `RepresentationPersistence` | Неверны serialization/deserialization, persisted schema/configuration либо mapping stored/query result в domain value | wrong serialized/persisted field or type; incompatible persisted schema/configuration; wrong query-result/round-trip mapping | Supported persistence round-trip и явно поддержанный stored schema/configuration path сохраняют значения | S05, S07 |
| `G03-F11` | `ConcurrencyAtomicity` | Race, lost update, TOCTOU, deadlock/ordering либо неверная atomic boundary | competing update; check/use interleaving; order/deadlock schedule | Два допустимых schedule дают contract-equivalent result или typed refusal | S01, S06 |
| `G03-F12` | `ResourceTermination` | Unbounded work/allocation/retry, отсутствие termination/resource bound или leak управляемого ресурса | unbounded loop/retry; allocation/work budget overflow; acquire without release/timeout | Полезная workload на/под лимитом завершается, превышение даёт заданный отказ | S05, S08 и resource-bound goal Strogo |

Граница классификации:

- один concrete specimen имеет ровно один `primaryFamilyId`; дополнительные cross-cutting tags не добавляют баллы;
- wrong approved requirement, omitted human requirement и неверная бизнес-цель записываются как research finding `ContractDefect` вне observation schema и G03 score;
- compiler/verifier/runtime/harness bug классифицируется через invalid-evidence reason и инвалидирует соответствующую cohort;
- memory-safety bugs, injection через отсутствующий в profile sink и UI defects не удаляются из истории, но не добавляются в v0.1 denominator без target-envelope amendment.

Primary family назначается по нарушенному semantic obligation, не по синтаксису fix. `F05` заканчивается на invocation boundary и не включает payload storage/configuration/round-trip; `F10` начинается только после выбора корректного call и относится к persisted representation либо query-result mapping. Single-fault specimen проходит следующий precedence tree: `F09 AuthorityCapability` → `F11 ConcurrencyAtomicity` → `F12 ResourceTermination` → `F08 FailureRecovery` → `F07 StateTransitionSequence` → `F10 RepresentationPersistence` → `F05 CallInterface` → `F06 CollectionCardinality` → `F01 PreconditionValidation` → `F03 ArithmeticQuantity` → `F04 ValueDataflow` → residual `F02 ConditionalBoundary`. Specimen с двумя независимыми нарушениями получает invalid reason `MultiFaultSpecimen`; неразрешимая семантическая неоднозначность — `AmbiguousPrimaryFamily`. Conformance содержит cross-family counterexamples для range guard, omitted call/effect, recovery/state/effect, persisted representation/invocation и unbounded retry.

#### 6.2.4 Closed evaluation vocabulary

Следующие enum имеют разные роли и не смешиваются в одном поле.

**NegativeSpecimenOutcome**

| Code | Смысл | Qualifying regression? |
| --- | --- | --- |
| `ExcludedByConstruction` | Single-fault negative нельзя сформировать как admitted program | Да, только при matched positive success |
| `RejectedPreExecution` | Negative детерминированно отвергнут до исполнения/эффекта с expected typed reason | Да |
| `RuntimeDetected` | Fault обнаружен runtime guard; запрещённый prior effect отсутствует | Нет, secondary metric |
| `AdmittedFault` | Faulty behavior admitted/occurred | Нет |

**PositiveControlOutcome**

| Code | Смысл | Cell может быть Covered? |
| --- | --- | --- |
| `AcceptedEquivalent` | Matched correct artifact admitted и oracle-equivalent | Да |
| `FalseRejection` | Correct artifact отвергнут или ошибочно классифицирован | Нет |
| `UnsupportedUsefulTask` | Существенная операция matched pair невыразима/недопустима | Нет |

**FamilyGeneralizationStatus**

| Code | Смысл | Cell может быть Covered? |
| --- | --- | --- |
| `VerifiedGeneralization` | Mechanized rule охватывает всю нормативную границу family-domain cell | Да |
| `MissingGeneralization` | Есть только examples/tests/specimens | Нет |
| `RefutedGeneralization` | Существует admitted counterexample | Нет |

Допустимые формы generalization evidence: `GrammarOrTypeExclusion`, `AdmissionInvariant`, `UniversalContractObligation` или `FiniteDomainExhaustion`. Каждая форма содержит statement, scope, assumptions, checker/tool identity, proof/evidence digest и trusted-base list. `FiniteDomainExhaustion` разрешён только когда machine contract доказывает полный конечный domain; sampled vectors не подходят.

**EvidenceValidity** состоит из `Valid` или `Invalid` плюс обязательный `InvalidEvidenceReason` для `Invalid`: `SchemaMismatch`, `DigestMismatch`, `ProvenanceMismatch`, `InfrastructureFailure`, `HarnessDefect`, `OracleAmbiguous`, `EquivalentSpecimen`, `MultiFaultSpecimen`, `AmbiguousPrimaryFamily`, `DomainCollision`, `IncompleteCohort`. Invalid row не получает negative/positive/generalization outcome.

**ApplicabilityStatus**: `Applicable` или `NotApplicable`. `NotApplicable` требует frozen rationale, owner-approved domain registry и evidence, что obligation не возникает; оно не уменьшает global denominator.

**TerminalResult**: `PriorityCatalogueMajoritySupported`, `PriorityCatalogueMajorityNotSupported` или `EvaluationIncomplete`. Слова `G03Achieved` в schema/report запрещены до отдельного target-prevalence gate.

`ExcludedByConstruction` не разрешает vacuous success: каждый negative имеет matched correct counterpart с тем же contract и существенной операцией. Если counterpart не проходит, positive outcome становится `UnsupportedUsefulTask`/`FalseRejection`, и cell не покрыт.

#### 6.2.5 Specimen contract

До первого candidate/model exposure отдельной exact-owner-approved evaluation SPEC для каждого `family × applicable domain` фиксируются:

- минимум 3 negative specimens разных shapes из таблицы §6.2.3;
- matched positive counterpart для **каждого** negative specimen: тот же contract, inputs и существенная операция, отличается только fault-bearing decision/construct;
- минимум 2 различных useful positive scenarios на cell, даже если negative shapes используют один counterpart;
- independent oracle и expected observation;
- applicability rationale;
- specimen/contract/oracle digests;
- language/toolchain/backend identities;
- family-level generalization statement и mechanized evidence form;
- trusted components и stop conditions.

Specimens являются regression/challenge evidence, а не основанием обобщения. Family-domain cell получает `Covered` только когда generalization имеет `VerifiedGeneralization`, **все** score-bearing negative specimens получили qualifying regression outcome и **все** matched positives получили `AcceptedEquivalent`. Любой `RuntimeDetected`, `AdmittedFault`, `UnsupportedUsefulTask`, `FalseRejection`, `MissingGeneralization` или `RefutedGeneralization` оставляет cell `NotCovered`; invalid evidence делает cohort `EvaluationIncomplete`.

Mutation specimens — только часть набора. Для каждого cell минимум один negative specimen должен быть semantic/manual construction или replay реального defect pattern, а не механическая operator substitution. Все preregistered score-bearing specimens mandatory; дополнительные diagnostic rows не влияют на score и помечаются до exposure. Equivalent specimen получает invalid reason `EquivalentSpecimen`; replacement возможен только в новой preregistered plan/cohort до новых candidate exposures и не переписывает старую cohort.

Recovery matrix:

| Invalid reason | Допустимое продолжение |
| --- | --- |
| `InfrastructureFailure` до candidate exposure | Максимум один exact whole-cohort retry по policy ниже: те же plan/candidate/oracle/tool digests, новый process-attempt receipt |
| `InfrastructureFailure` после exposure | Retry запрещён; текущая cohort и связанная evaluation revision остаются `EvaluationIncomplete` |
| `ProvenanceMismatch`, `DigestMismatch`, `SchemaMismatch` внутри schema-valid observation | Текущая evaluation revision incomplete; post-exposure retry запрещён, исправление возможно только в новой owner-approved plan revision/new claim; старый lineage публикуется |
| `EquivalentSpecimen`, `OracleAmbiguous`, `AmbiguousPrimaryFamily`, `MultiFaultSpecimen` | Semantic plan revision и owner re-approval до новых exposures |
| `HarnessDefect` | Вся затронутая cohort incomplete; fix + independent revalidation + новая cohort |
| `DomainCollision` | Domain registry revision и owner re-approval |
| `IncompleteCohort` | Нельзя selective-retry только неудобные rows; завершить по заранее заданному no-retry/whole-cohort rule |

Единственная retry policy v0.1 — `PreExposureSingleWholeCohortRetry`. Evaluation plan заранее фиксирует `maxPreExposureRetries=1`, `postExposureRetries=0` и запрет replacement отдельных rows. Exact retry обязан переиспользовать те же candidate artifact IDs/digests; если candidate ещё не был создан, фиксируются те же generation inputs и arm identities. Все attempt/cohort IDs образуют append-only lineage в observations/report. После exposure новая cohort возможна только в новой owner-approved plan revision с полной регенерацией всех arms; она является новым claim и не заменяет terminal старой revision. Публикуются все lineage entries, включая incomplete; scorer не принимает selector поля и не умеет выбирать «лучший» report.

#### 6.2.6 Domain registry and majority rule

Domain registry — отдельный normative JSON и owner-approved artifact. Для v0.1 первый designated domain — существующий `reserve`; второй выбирается отдельной SPEC до его реализации/candidate generation. Agent не может сам добавить удобный qualifying domain. Domain считается существенно отличным, если различаются business state model и минимум два из четырёх измерений: input/event shape, state transition graph, collection behavior, effect/failure surface. Exact-copy, renamed или parameter-only variant получает `DomainCollision`.

Evaluation revision включает **все** qualifying domains зарегистрированной и одобренной domain-registry revision. Нельзя выбрать любые два из большего числа. Добавление третьего domain создаёт новую registry/evaluation revision; в ней должны пройти все три.

Пусть frozen catalogue `F` содержит ровно 12 families, `D` — все qualifying domains exact registry revision, а `A(f,d)` — owner-approved applicability mapping.

1. Registry обязан содержать минимум два distinct qualifying domains; каждый имеет минимум 8 `Applicable` families.
2. `CoveredCell(f,d)` определяется family-level generalization + regression rule §6.2.5.
3. `CoveredFamily(f) = true`, только если существует applicable domain и **каждый** domain, где `A(f,d)=Applicable`, имеет `CoveredCell(f,d)=true`.
4. Global priority-catalogue majority: `sum(CoveredFamily) >= floor(12 / 2) + 1 = 7`.
5. Для **каждого** registered qualifying domain `d` с `n` applicable families должно быть покрыто `floor(n / 2) + 1`.
6. Все 12 families остаются в global denominator. `NotApplicable` не уменьшает 12 и не становится покрытием.
7. `PriorityCatalogueMajoritySupported` возможен только при global majority, floor каждого registered domain, valid evidence, exact catalog/registry/plan digests и complete provenance closure. Invalid evidence даёт `EvaluationIncomplete`; валидный недобор — `PriorityCatalogueMajorityNotSupported`.
8. Даже `PriorityCatalogueMajoritySupported` не равен `G03Achieved`: без target-prevalence study неизвестно, являются ли эти families большинством частых классов target defects.

Secondary metrics публикуются, но не меняют gate:

- counts по каждому outcome;
- runtime-detected families;
- unsupported/false-rejection rates;
- negative specimen pass ratio;
- source-evidence tags;
- domain-specific coverage.

Почему без frequency weights: S02–S08 измеряют разные populations и разные единицы. Искусственная нормализация дала бы ложную точность и дополнительную возможность подгонки. Равный вес frozen semantic family вместе с domain floor и all-or-nothing controls проверяемее.

#### 6.2.7 Anti-drift and provenance invariants

- Normative artifacts — `source-row-manifest.json`, `catalog.json`, `domain-registry.json` и конкретный `evaluation-plan.json`; docs являются human-readable projections. Любой projection mismatch инвалидирует release, но prose не переопределяет JSON post hoc.
- Каждый JSON закрыт schema, ASCII-only и канонизируется существующим `CanonicalJson`.
- Validator вычисляет `sourceRowManifestDigest`, `catalogDigest`, `domainRegistryDigest` и `planDigest`; catalog ссылается на manifest, observations — на все четыре exact digest.
- Human doc содержит тот же ordered ID set, threshold и closed enum codes; conformance проверяет parity.
- Любая семантическая правка family/threshold/outcome/specimen rule создаёт новую catalog version и новую owner approval; v0.1 остаётся доступной.
- Новая версия не переоценивает старый report post hoc.
- Outcome назначает trusted evaluator по raw evidence, не candidate/agent. При конфликте raw receipt > derived observation > report; конфликт derived fields даёт invalid reason `HarnessDefect`, а не precedence-based success.
- Невалидный specimen, harness failure и missing evidence не удаляются из denominator и не конвертируются в pass.
- Catalog freeze commit предшествует second-domain implementation и G05 corpus generation; domain registry и evaluation plan получают отдельное exact owner approval до candidate exposure.

Full provenance closure для terminal report: exact digests source-row manifest/catalog/schema/domain-registry/evaluation-plan; repository commit; source closure; compiler/verifier/runtime/backend identities; contract/oracle/specimen artifacts; raw run receipts; derived observations; evaluator binary/source identity; full cohort lineage и report digest. Missing link даёт `ProvenanceMismatch`/`EvaluationIncomplete`.

#### 6.2.8 CLI contract

После approval добавляются команды:

```text
strogo-experiment g03 validate --source-row-manifest <source-row-manifest.json> --catalog <catalog.json> --domain-registry <domain-registry.json> --evaluation-plan <evaluation-plan.json>
strogo-experiment g03 score --source-row-manifest <source-row-manifest.json> --catalog <catalog.json> --domain-registry <domain-registry.json> --evaluation-plan <evaluation-plan.json> --observations <observations.json> --report <report.json>
```

Normative schema set:

- `source-row-manifest.schema.json`: exact source revisions/snapshots, ordered raw row IDs/labels, expected per-source/total counts and digests;
- `catalog.schema.json`: sources, candidate-universe decisions, families, classification precedence, closed enums and scoring constants;
- `domain-registry.schema.json`: distinctness evidence, owner-approval receipt, qualifying domains and applicability mapping;
- `evaluation-plan.schema.json`: matched specimen/control pairs, generalization obligations, oracle/toolchain identities and retry/cohort policy;
- `observations.schema.json`: per-run `EvidenceValidity`; для valid rows ровно один typed negative или positive outcome; separate family-generalization observation and raw evidence digests;
- `report.schema.json`: immutable identities, per-cell results, secondary metrics and one closed `TerminalResult`.

CLI typed input-refusal contract: schema/closed-field failures → `G03SchemaInvalid`; manifest inventory/decision bijection defect → `G03SourceInventoryInvalid`; duplicate/unknown IDs/enums → `G03CatalogInvalid`; cross-family ambiguity → `G03ClassificationInvalid`; registry collision/applicability defect → `G03DomainRegistryInvalid`; plan/matched-control/generalization defect → `G03PlanInvalid`; cross-artifact digest/provenance failure → `G03EvidenceInvalid`. Error JSON всегда `{status:"Refused",code,stage,artifactDigest}`; prose exception type не является contract. Closed `stage` enum: `Invocation`, `SourceRowManifest`, `Catalog`, `DomainRegistry`, `EvaluationPlan`, `Observations`, `CrossArtifact`. Для single-artifact failure `artifactDigest` — SHA-256 raw bytes этого artifact; для missing invocation input — SHA-256 zero bytes; для `CrossArtifact` — digest canonical ordered map всех фактически прочитанных input raw digests. Неполная или invalid cohort, которая сама соответствует observations schema и exact four input identities, является оценочным terminal `EvaluationIncomplete`, а не input refusal.

Два failure layer не смешиваются:

- schema/semantic/cross-artifact defect входов не создаёт report, печатает canonical refusal JSON со `status="Refused"` и возвращает exit `1`;
- structurally valid observations могут честно содержать `EvidenceValidity=Invalid`; тогда `score` создаёт canonical report с terminal `EvaluationIncomplete`, печатает canonical summary `{status:"EvaluationIncomplete",reportDigest}` и возвращает exit `1`;
- complete valid evidence создаёт report с `PriorityCatalogueMajoritySupported` или `PriorityCatalogueMajorityNotSupported`, печатает canonical summary `{status:<TerminalResult>,reportDigest}` и возвращает exit `0`.

`validate` выводит canonical JSON `{status:"EvaluationInputsAccepted",catalogVersion,sourceRowManifestDigest,catalogDigest,domainRegistryDigest,planDigest,sourceRowCount,familyCount,qualifyingDomainCount,globalThreshold}` и exit `0` только для exact mutually consistent inputs. `score` strict-parses all schema-bound inputs, проверяет provenance/digests и строит deterministic report по правилам выше. Команда не запускает модель и не создаёт реальные G03 observations в этом этапе.

Performance: каталог мал; validator/scorer обязан работать линейно по families/specimens, без сети и nondeterministic clock. Время не является G06 evidence.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Просмотр каталога | Человек открывает human-readable v0.1 | Видит 12 stable families, границы, источники, controls и правило 7/12 | doc↔JSON parity report | AC1–AC3 |
| Проверка freeze | Агент запускает `g03 validate` с manifest/catalog/registry/plan | Exact four digests/counts/threshold; drift или cross-artifact mismatch получает typed refusal без report | CLI output + negative fixture | AC2–AC5 |
| Проверка большинства | Scorer получает synthetic 7/12 и 6/12 | 7/12 может пройти только при domain floors/controls; 6/12 не проходит | golden reports | AC6 |
| Runtime guard | Все negatives лишь runtime-detected | `PriorityCatalogueMajorityNotSupported`; runtime protection показана отдельно | control report | AC7 |
| Язык отказывается от полезной задачи | Negative невыразим, но positive control тоже невыразим | `UnsupportedUsefulTask`; family не покрыта | control report | AC8 |
| Несогласованные входы | Неверная catalog/registry/plan/observation identity или отсутствующий registry | `Refused/G03EvidenceInvalid` либо typed artifact code, report не создаётся | input-refusal controls | AC9 |
| Невалидное evidence внутри cohort | Schema-valid row содержит `EquivalentSpecimen`, `InfrastructureFailure` или иной invalid reason при exact input identities | Report `EvaluationIncomplete`, без majority claim | evidence controls | AC9 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Draft catalog | Owner exact approval | `ApprovedForImplementation` | Любая иная фраза не открывает EXEC | Approval относится к этой версии |
| Valid manifest/catalog/registry/plan | `g03 validate` | `EvaluationInputsAccepted` + exact four digests | Missing/extra/duplicate/unknown/cross-artifact mismatch → typed refusal | No network |
| Valid catalog + complete observations | `g03 score` | `PriorityCatalogueMajoritySupported` или `PriorityCatalogueMajorityNotSupported` | Invalid provenance → `EvaluationIncomplete`/nonzero | Report deterministic |
| Frozen v0.1 | Proposed semantic change | New version required | Existing reports remain bound to v0.1 | No in-place rewrite |
| Concurrent reads | Multiple validators | Same digest/result | Writer отсутствует | Artifacts immutable in run |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Scope of G03 | user + agent | Implementation/runtime faults relative to approved contract in bounded business modules | 0.98 | Global claims beyond evidence | Нет; следует G03 owner text |
| Catalogue granularity | agent | 12 stable semantic families with ODC as granularity check | 0.88 | Семейства слишком широки/узки | Нет; exact approval подтвердит смысл целиком |
| Majority | user via approval | Strict 7/12 plus strict majority in every qualifying domain exact registry revision | 0.96 | Слишком лёгкий выбор удобных domains | Нет после approval |
| Source percentages | agent | No cross-source weights; secondary metadata only | 0.97 | False precision and gaming | Нет |
| Runtime detection | user intent + agent | Reported but not counted | 0.99 | Подмена prevention обычным exception | Нет |
| Unsupported task | user intent + agent | Fails family positive-control obligation | 0.99 | Vacuous success | Нет |
| Exact second domain | future task | Не выбирается здесь; must map ≥8 families before implementation exposure | 0.91 | Domain chosen to game catalogue | Нет для этой SPEC; отдельная approved SPEC |
| Machine enforcement | agent | Add strict .NET validator/scorer and controls | 0.95 | Human docs drift | Нет |

### 6.5.1 Явные решения владельца для fresh approval frozen revision

Fresh точная фраза approval, данная после предъявления commit/blob digest, подтвердит одновременно только следующие решения:

1. v0.1 замораживает 12 **evidence-linked priority families**; это пока не статистическое утверждение о частоте ошибок в target envelope.
2. Все families имеют одинаковый вес; terminal threshold равен 7 из 12, а каждый qualifying domain exact registry revision обязан отдельно пройти strict majority среди своих applicable families.
3. Все qualifying domains одобренной registry revision участвуют в оценке; произвольный выбор двух удобных domains запрещён.
4. `RuntimeDetected` остаётся secondary metric и не входит в numerator.
5. Family-domain cell покрыта только при `VerifiedGeneralization`, успешных всех preregistered negative regressions и `AcceptedEquivalent` для всех matched positives.
6. Даже `PriorityCatalogueMajoritySupported` не означает `G03Achieved`. Для owner-level утверждения о «большинстве частых классов» потребуется отдельное target-prevalence study и новая exact approval.

Approval не выбирает второй домен, specimens или результаты оценки: для них требуются отдельные preregistered artifacts и approvals до candidate/model exposure.

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| G03 meaning | `docs/project-intent.md` | Additive operationalization; owner goal unchanged | No migration | semantic review |
| Catalog | absent | versioned strict JSON + doc | v0.1 immutable | schema/semantic validator |
| Experiment CLI | calibrate/export/pilot | additive `g03` verb | Existing commands unchanged | full conformance regression |
| Evidence | E09/E10 specific | G03 source register and synthetic controls | Historical reports unchanged | digest/read-back |
| Storage/config | none | tracked files only | No runtime storage migration | clean checkout reproduction |

## 7. Бизнес-правила / Алгоритмы

Normative pseudocode:

```text
validate(sourceRowManifest, catalog, domainRegistry, evaluationPlan):
  require exact schema/version
  require source revisions and ordered raw-row inventories have exact counts/digests
  require catalog candidateDecisions is a bijection over manifest raw rows
  require ordered unique family IDs G03-F01..G03-F12
  require each family has >= 3 distinct negativeShapes and >= 2 positiveControlKinds
  require closed outcome vocabulary, threshold == 7 and classification precedence
  require domainRegistry has >= 2 distinct qualifying domains
  require every qualifying domain has >= 8 applicable families
  require evaluationPlan covers every applicable family-domain cell
  require every negative has one matched positive and every cell has a generalization obligation
  return canonical sourceRowManifestDigest, catalogDigest, domainRegistryDigest, planDigest

score(sourceRowManifest, catalog, domainRegistry, evaluationPlan, observations):
  expectedDigests = validate(sourceRowManifest, catalog, domainRegistry, evaluationPlan)
  require observations exact-match expectedDigests and complete provenance closure
  require every observation row has EvidenceValidity
  require Valid row has exactly one outcome of its typed row kind
  require Invalid row has exactly one InvalidEvidenceReason and no outcome
  for each applicable family-domain cell:
    if any invalid evidence or missing mandatory row: terminal = EvaluationIncomplete
    coveredCell = family generalization is VerifiedGeneralization
                  AND every mandatory negative has one of
                      {ExcludedByConstruction, RejectedPreExecution}
                  AND every mandatory positive is AcceptedEquivalent
  coveredFamily = has applicable cell
                  AND every applicable cell is coveredCell
  globalCovered = count(coveredFamily)
  domainPassed[d] = coveredCells[d] >= floor(applicableCount[d]/2)+1
  if any incomplete/invalid evidence: EvaluationIncomplete
  else if globalCovered >= 7 AND every qualifying domainPassed:
    PriorityCatalogueMajoritySupported
  else PriorityCatalogueMajorityNotSupported
```

Invariant: правильный отказ для одного specimen не компенсирует false rejection, unsupported positive task или runtime-only outcome того же family-domain cell.

## 8. Точки интеграции и триггеры

- `Strogo.ExperimentCli.Run` добавляет только верхнеуровневый verb `g03`.
- Catalog parser использует strict UTF-8/closed-fields подход существующего experiment stack.
- G03 conformance запускается из существующего `Strogo.Experiments.Conformance` и не меняет E09/E10 fixtures.
- Future second-domain и G05 specs обязаны ссылаться на exact `catalogDigest` и frozen applicability mapping.
- `docs/project-intent.md` после approval получает только ссылку на operational catalogue и явный статус «priority-catalogue rule зафиксировано, target prevalence и достижение G03 не измерены».
- `docs/knowledge-log.md` получает source limitations, no-weight decision и anti-vacuity rule.

## 9. Изменения модели данных / состояния

Machine-readable contract содержит:

- source-row manifest: exact source revision/snapshot identity, ordered raw rows, per-source counts/digests and total inventory digest;
- catalog: `schemaVersion`, `catalogVersion`, `scopeId`, ordered `sourceIds`, candidate-universe decisions, classification precedence, `families[]`, closed outcome vocabulary и `scoring`;
- domain registry: exact qualifying domain IDs, distinctness evidence, owner-approval receipt и complete applicability mapping;
- evaluation plan: mandatory negative/matched-positive IDs, cell generalization obligations, oracle/tool identities, retry/cohort policy и exact input digests;
- observations: artifact identity/provenance, `EvidenceValidity`, ровно один typed outcome у valid row или ровно один invalid reason без outcome;
- report: immutable input digests, per-cell/family/domain results, secondary metrics и один closed `TerminalResult`.

Observations/report schemas реализуются полностью, но в этой задаче используются только synthetic controls; реальные observations появятся в отдельной experiment SPEC. Persisted mutable state отсутствует.

## 10. Миграция / Rollout / Rollback

- Изменение additive; существующие CLI commands и evidence не мигрируют.
- Rollback — revert implementation checkpoint целиком. Approved SPEC и историческая запись остаются как решение/отрицательный результат, если реализация откатана.
- v0.1 не редактируется семантически после первого external/candidate exposure; correction создаёт v0.2 с change rationale и не меняет старые scores.
- Push выполняется checkpoint-коммитами. Release/deploy отсутствуют.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- **AC1:** Human doc содержит ровно IDs `G03-F01..G03-F12`, определения, обязательные shapes, positive-control obligations, evidence и limitations.
- **AC2:** Evidence register фиксирует S01–S08, проверяемые факты, exact revisions/snapshots, даты доступа, relevance и threats; normative manifest содержит ordered raw rows/counts/digests, CWE не назван общей частотой bugs.
- **AC3:** Human doc projection и normative `catalog.json` согласны по ID/order/outcome vocabulary/threshold; JSON однозначно главнее при интерпретации, а mismatch инвалидирует release.
- **AC4:** Exact manifest/catalog/registry/plan strict-validate совместно и выдают четыре stable digest на повторных runs/clean checkout.
- **AC5:** Omitted/duplicate/unknown raw source row, wrong inventory count/per-source digest, forbidden/tampered manifest self-digest field, missing/extra/duplicate family, unknown outcome, threshold 6, duplicate shape, меньше 3 negatives или 2 positives, noncanonical/invalid UTF-8 получают typed refusal.
- **AC6:** Synthetic 7/12 со strict majority во всех qualifying domains даёт `PriorityCatalogueMajoritySupported`; 6/12, 7/12 с failed domain floor, one-domain registry и пропуск третьего registered domain не дают этот terminal.
- **AC7:** `RuntimeDetected` у всех negatives даёт `PriorityCatalogueMajorityNotSupported`, сохраняя runtime metric.
- **AC8:** Невыразимый negative вместе с failed positive control даёт `UnsupportedUsefulTask`/NotCovered, не `ExcludedByConstruction`.
- **AC9:** Cross-input manifest/catalog/registry/plan/observation identity mismatch даёт typed refusal без report и exit `1`; schema-valid invalid-evidence row с exact identities даёт report `EvaluationIncomplete` и exit `1`.
- **AC10:** Existing E09/E10 behavior и reports byte-for-byte не изменяются; experiment conformance и full solution build green.
- **AC11:** `docs/project-intent.md` и knowledge log честно разделяют frozen methodology и пока не измеренное достижение G03.
- **AC12:** Post-EXEC review сверяет source claims, anti-gaming counterexamples, docs↔JSON parity, tests, clean status и actual pushed commit.
- **AC13:** Все negative/positive specimens могут пройти, но `MissingGeneralization` и `RefutedGeneralization` всё равно дают `NotCovered`; plan, выдающий sampled vectors за `FiniteDomainExhaustion`, получает `Refused/G03PlanInvalid`, stage `EvaluationPlan`, без report, exit `1`; `VerifiedGeneralization` требует allowed form, scope, assumptions, checker/tool identity, proof digest и trusted-base list.
- **AC14:** Retry control разрешает не более одного pre-exposure whole-cohort retry с exact reused identities, запрещает post-exposure retry/row replacement, сохраняет полный lineage и не позволяет выбрать лучший report.

### Обязательный набор проверок

После approval и implementation:

```powershell
& .\.tools\dotnet-sdk-10.0.400\dotnet.exe build Kernel.slnx --nologo
& .\.tools\dotnet-sdk-10.0.400\dotnet.exe run --project tests\Strogo.Experiments.Conformance\Strogo.Experiments.Conformance.csproj --no-build
& .\.tools\dotnet-sdk-10.0.400\dotnet.exe run --project tests\Kernel.Conformance\Kernel.Conformance.csproj --no-build
git diff --check
git status --short
```

Targeted checks сначала выполняют G03 cases, затем full experiment conformance. Full solution обязателен из-за public CLI/data contract. Network нужен только на SPEC research; validator/scorer offline.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC1 | exact family set/count | semantic doc review | catalog doc | — |
| AC2 | source ID parity | link/fact/limitation review | evidence register | External datasets повторно не майнятся; используются published facts |
| AC3 | doc projection parity check | diff inspection | conformance output | — |
| AC4 | repeat digest and canonical bytes | clean checkout command | validation JSON | — |
| AC5 | negative schema/semantic matrix | typed codes inspection | G03 conformance | — |
| AC6 | three threshold controls | golden report inspection | controls/reports | — |
| AC7 | runtime-only control | metric inspection | control report | — |
| AC8 | vacuous-exclusion control | outcome inspection | control report | — |
| AC9 | provenance/equivalence/infrastructure controls | exit-code inspection | control reports | — |
| AC10 | full experiment/Core conformance + build | historical report hashes compared | command logs | — |
| AC11 | no-G03-achieved wording check | owner-doc review | docs diff | — |
| AC12 | independent/adversarial review | reviewer verdict | SPEC Post-EXEC | — |
| AC13 | missing/refuted/invalid-exhaustion controls | generalization statement inspection | control reports | — |
| AC14 | retry cap/identity/lineage/selector controls | plan and report inspection | control reports | — |

## 12. Риски и edge cases

- **Source bias:** Java, open source, CVE и distributed systems не представляют весь envelope. Mitigation — источники обосновывают inclusion, но не дают weights; ограничения обязательны в register.
- **Granularity gaming:** broad families могут скрыть частичный провал. Mitigation — минимум 3 shapes, all-or-nothing cell и stable primaryFamily.
- **Vacuous prevention:** язык может запретить задачу целиком. Mitigation — mandatory positives и `UnsupportedUsefulTask`.
- **Mutation overclaim:** mutants покрывают не все real faults. Mitigation — S04 limitation и минимум один non-mechanical specimen per family-domain.
- **Domain gaming:** второй домен можно выбрать под сильные стороны. Mitigation — catalogue frozen first, ≥8 applicable families, applicability mapping frozen до implementation/model exposure.
- **Runtime laundering:** exception после начала эффекта может выглядеть защитой. Mitigation — runtime outcome не считается и должен доказывать no forbidden prior effect.
- **Equivalent/ambiguous specimen:** удобное удаление меняет denominator. Mitigation — `EvidenceValidity=Invalid` с exact reason, no post-exposure replacement.
- **Requirement defects:** implementation может идеально удовлетворять неверному contract. Mitigation — такие случаи не являются observation outcome G03, записываются в отдельный research/contract-defect журнал и требуют human decision.
- **Trusted evaluator defect:** ложный majority. Mitigation — synthetic negative controls, exact digests, independent post-EXEC review и future held-out replay.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Почему ровно 12 и почему они равновесны?» | Источники дают другие taxonomy/проценты | §6.2.2 inclusion rule, ODC granularity guard, incomparable populations, no fake weights | mitigated; owner approves exact ontology |
| «Можно получить 7/12, запретив половину полезного языка» | Это ключевая угроза замыслу | Positive controls, unsupported-task failure, domain floor | mitigated |
| «Synthetic mutants не доказывают реальные ошибки» | Эмпирическое ограничение S04 | Mutants only representatives; mandatory semantic/replay specimen; claim remains bounded | mitigated |
| «Security/distributed errors нечасты в моих модулях» | CWE/cloud sources biased | Они включены только при target-envelope relevance; no weights; domain applicability visible; global denominator frozen | accepted methodological tradeoff |
| «Runtime guard тоже полезен» | Да, но G03 формулирует exclusion/admission | Runtime metric сохраняется отдельно и может поддержать продуктовый выбор, но не majority | mitigated |
| «Нужно сразу оценить текущий Strogo» | Иначе хочется быстрый ответ | Отдельная preregistered evaluation нужна после второго домена; этот этап только freeze | mitigated by scope |

### Rework Prevention Checklist

- Исходный пользовательский результат назван и видим в §1/§6.3.
- Каждый user-visible scenario связан с AC/evidence.
- Agent decisions перечислены в §6.5.
- Вероятные objections перечислены и имеют disposition.
- Role-based review обязателен в §19.
- AC проверяют готовый контракт, а не список действий.
- EXEC имеет deterministic path к доказательству сценариев без live model calls.

## 13. План выполнения после approval

1. Создать evidence register, normative source-row manifest и human-readable catalogue projection без новых research claims.
2. Создать closed schemas, ASCII-only normative catalog, synthetic domain registry/evaluation plan и вычислить canonical digests.
3. Добавить strict parser/semantic validator и CLI `g03 validate`.
4. Добавить complete observation/report contracts, deterministic scorer и synthetic control reports.
5. Добавить conformance matrix: source-inventory bijection, classification precedence, schema, anti-vacuity, family generalization, threshold, all-registered-domain floor, provenance, retry lineage, invalid-recovery и runtime-only.
6. Обновить project intent ссылкой/status и knowledge log значимыми решениями.
7. Выполнить targeted → experiment conformance → solution build → Core regression → diff checks.
8. Провести full post-EXEC review, исправить findings, закоммитить и push checkpoint.
9. Завершить Unlimotion task только после read-back и published evidence.

## 14. Открытые вопросы

Блокирующих design-вопросов нет. Fresh owner approval exact frozen revision должен подтвердить решения §6.5.1 после PASS review. Выбор второго домена, реальный domain registry/evaluation plan и реальные specimens являются отдельной задачей и не блокируют implementation каталога/scorer на synthetic controls.

## 15. Соответствие профилю

- Профиль: `product-system-design`.
- Цели/non-goals: §§4–5.
- Архитектура и boundaries: §6.1–§6.2.
- Публичный CLI/data contract: §§6.2.8, 9.
- Compatibility/security/config: §§6.6, 10, 12.
- Testing context: .NET console conformance с repo-local SDK 10.0.400.
- UI / visual artifact: не применимо; результат — versioned docs/JSON/CLI. Reviewer получает tables и canonical JSON output.
- UI video evidence: не применимо; UI отсутствует.

## 16. Таблица изменений файлов

| Файл | Изменения после approval | Причина |
| --- | --- | --- |
| `docs/g03-error-catalog-v0.1.md` | Новый | Human-readable projection; JSON остаётся normative |
| `docs/research/g03-evidence-register-v0.1.md` | Новый | Traceable external evidence and limitations |
| `fixtures/g03-error-catalog/v0.1/source-row-manifest.json` | Новый | Normative complete raw-row inventory S01–S08 |
| `fixtures/g03-error-catalog/v0.1/catalog.json` | Новый | Normative machine freeze linked to manifest digest |
| `fixtures/g03-error-catalog/v0.1/schemas/source-row-manifest.schema.json` | Новый | Closed source inventory contract |
| `fixtures/g03-error-catalog/v0.1/schemas/catalog.schema.json` | Новый | Closed catalogue contract |
| `fixtures/g03-error-catalog/v0.1/schemas/domain-registry.schema.json` | Новый | Closed registry/distinctness/applicability contract |
| `fixtures/g03-error-catalog/v0.1/schemas/evaluation-plan.schema.json` | Новый | Closed specimen/generalization/cohort contract |
| `fixtures/g03-error-catalog/v0.1/schemas/observations.schema.json` | Новый | Closed evidence validity and typed outcomes |
| `fixtures/g03-error-catalog/v0.1/schemas/report.schema.json` | Новый | Closed deterministic report contract |
| `fixtures/g03-error-catalog/v0.1/controls/*.json` | Новые | Synthetic registry/plan/observations/golden reports, не real Strogo evidence |
| `src/Strogo.Experiments/G03Catalog.cs` | Новый | Strict deterministic validation/scoring |
| `src/Strogo.ExperimentCli/Program.cs` | Additive verb | Reproducible user path |
| `tests/Strogo.Experiments.Conformance/G03Cases.cs` | Новый | Contract/anti-gaming checks |
| `tests/Strogo.Experiments.Conformance/Program.cs` | Register G03 cases if needed | Run integration |
| `docs/project-intent.md` | Add link and status | Discoverability without false claim |
| `docs/knowledge-log.md` | Add stable insights | Mandatory knowledge capture |

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| «Частые ошибки» | Неоперациональная цель | Complete source-row manifest + 12 frozen evidence-linked priority families; target frequency остаётся отдельным исследованием |
| «Большинство» | Без denominator/threshold | 7/12 + per-domain strict majority |
| Невыразимость | Можно спутать с отсутствием feature | Требует mandatory positive controls |
| Runtime guard | Упомянут отдельно | Machine outcome, не numerator |
| Unsupported task | Риторическое ограничение | Failing outcome family-domain cell |
| Источники | Нет register | S01–S08 с facts/limitations |
| Drift | Git history only | exact schema/version/digest/parity |
| Оценка | Ручная интерпретация | deterministic validator/scorer controls |

## 18. Альтернативы и компромиссы

### Вариант A: взять CWE Top 25 как каталог

- Плюсы: известный, ежегодный, численно ранжирован.
- Минусы: CVE/severity bias, много web/memory-unsafe классов вне envelope, почти нет обычной business logic.
- Решение: CWE используется как один security источник, не denominator.

### Вариант B: взвесить families опубликованными процентами

- Плюсы: выглядит количественно.
- Минусы: commit/bug/CVE/failure populations несопоставимы; веса создают ложную точность и gameable threshold.
- Решение: равный вес frozen families; percentages только evidence metadata.

### Вариант C: считать каждый mutant отдельным классом

- Плюсы: простой mutation score.
- Минусы: гранулярность задаёт generator; equivalent/redundant mutants доминируют; реальные faults покрываются неполно.
- Решение: mutants — specimens внутри semantic family, all-or-nothing family score.

### Вариант D: только документация без scorer

- Плюсы: меньше кода.
- Минусы: threshold, runtime и unsupported-task semantics снова будут интерпретироваться вручную в G05.
- Решение: небольшой additive deterministic validator/scorer.

### Вариант E: weighted real-bug mining в собственных проектах

- Плюсы: высокая персональная релевантность.
- Минусы: нужен большой размеченный corpus, privacy/selection bias и месяцы работы; всё равно не даёт стабильной ontology автоматически.
- Решение: возможный v0.2 research, не blocker v0.1.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1–5 | PASS | Outcome, AS-IS, root problem, goals и hard boundaries заданы |
| B. Качество дизайна | 6–10 | PASS | Ownership, catalogue, outcomes, scoring, CLI, errors и linear performance определены |
| C. Безопасность изменений | 11–13 | PASS | Versioned immutable data, additive compatibility и revert/version rollback |
| D. Проверяемость | 14–16 | PASS | AC1–AC14, evidence matrix, commands и stop rules |
| E. Готовность к автономной реализации | 17–19 | PASS | Ordered plan, no blocking question, expanded form justified |
| F. Соответствие профилю | 20 | PASS | Public data/CLI architecture, security and compatibility covered |

Итог: **PASS post-SPEC review; EXEC ожидает только fresh approval exact commit/blob revision**.

### SPEC Rubric Result

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Ясность цели и границ | 5 | Frozen catalogue/scoring outcome и no-G03-claim граница явны |
| Понимание текущего состояния | 5 | G03 gap отделён от E09/E10 mechanisms/results |
| Конкретность дизайна | 5 | 12 families, outcomes, specimen contract, formulas, files, CLI |
| Безопасность / migration / rollback | 5 | Additive, immutable version, historical evidence untouched |
| Тестируемость | 5 | Anti-gaming controls и AC→evidence mapping |
| Готовность реализации | 5 | Нет unresolved implementation choices |

Итоговый балл: **30 / 30**. Зона: готово к автономному выполнению после fresh approval exact commit/blob revision.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Не превращает ли rule отказ от полезной задачи в успех и соответствует ли owner G03? | PASS | Positive controls, bounded claim и human contract boundary закрыты |
| UX / designer | not applicable | UI/visual interaction отсутствует; artifact readability проверяет owner/doc review | N/A | — |
| Tester / validation | applicable | Различают ли controls threshold, runtime, false refusal, unsupported task и invalid evidence? | PASS | AC1–AC14 покрывают independent failure paths |
| Developer / architect | applicable | Стабильны ли ontology/data/CLI/version/provenance contracts? | PASS | Closed enums/schemas/digests/versioning и deterministic scorer заданы |
| Delivery / operations / security | applicable | Нет ли сети/runtime mutation, drift или ложного security claim? | PASS | Offline scorer, immutable lineage, no best-run selection и bounded CWE use |

### Post-SPEC Review

- Статус / stop decision: **PASS; STOP перед EXEC до fresh exact approval**.
- Scope reviewed: §§1–20, source claims/limitations, 12-family ontology, candidate-universe closure, generalization/scoring formula, CLI/schema/error/retry contracts, AC и rollout.
- Contract pass: PASS — input refusal отделён от schema-valid `EvaluationIncomplete`; human projection не конкурирует с normative JSON.
- Adversarial risk pass: PASS — vacuous exclusion, family overclaim, domain selection, source-row omission, runtime laundering, retry/survivor selection и false frequency claim закрыты.
- Role-Based pass: PASS по applicable ролям; UX N/A обоснован отсутствием UI.
- Fix and re-review: два NEEDS-FIX цикла закрыли blockers/high/medium/low; independent reviewer дал PASS frozen semantic revision SHA-256 `805007E3D86CB65BA646F72AF198D071AB7D834A6822AC7B7FAE8CF74CCE211A`.
- Evidence inspected: S01–S08 links/facts, E09/E10 boundary, all formula/outcome/schema/CLI/AC cross-references и `git diff --check`.
- Depth checklist: contract/adversarial/role-based/anti-gaming/provenance/retry/claim-boundary passes выполнены.
- No-findings justification: финальный pass повторно проверил все прежние findings; metadata-only запись verdict ниже не меняет normative design и подлежит final hash verification.
- Needs human: fresh exact approval, привязанный к зафиксированным commit и SPEC blob digest.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| BLOCKER/HIGH/MEDIUM/LOW | approval, outcomes, ontology, inventory, generalization, retry, digest | Два review цикла выявили и конкретизировали нарушения | Исправлено и повторно проверено | closed |

### Post-EXEC Review

- Не выполнен. EXEC начнётся только после PASS post-SPEC review и fresh exact approval зафиксированной revision.

## Approval

Фраза **«Спеку подтверждаю»** была получена 2026-09-27 до corrective review. Поскольку review потребовал нормативные изменения, она не открывает EXEC исправленной revision. После PASS SPEC фиксируется отдельным commit; пользователю предъявляются commit SHA и Git blob SHA этого файла, и требуется свежая точная фраза **«Спеку подтверждаю»**, явно относящаяся к ним. Receipt сохраняется вне неизменяемого SPEC blob в `docs/knowledge-log.md` и Unlimotion execution log.

## 20. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток работы | Следующее действие | Фактическое решение человека | Затронутые артефакты |
| --- | --- | --- | --- | --- | --- |
| RESEARCH | После E10 выбрана G03 catalogue task: её нужно заморозить до второго домена/G05, чтобы избежать outcome-driven taxonomy | Unlimotion task `de82f5c1-...`, lease `4e035b22-...`; parent goal has six remaining children | Собрать primary evidence и proposed rule | Пользователь поручил выполнить parent goal | Unlimotion execution |
| RESEARCH | Несопоставимые source percentages не используются как weights | S01–S08 имеют разные populations/units; S04 подтверждает полезность и пределы mutants | Зафиксировать equal-family/all-or-nothing design | Не требовалось | this SPEC |
| SPEC | Выбраны 12 semantic families, qualifying outcomes, 7/12 + all-registered-domain floors, positive-control anti-vacuity и deterministic scorer | §§6–11 | Выполнить full post-SPEC review | Подтверждено следующей строкой | this SPEC |
| APPROVAL | Владелец прислал точную фразу «Спеку подтверждаю» до завершения corrective review | Review потребовал нормативные изменения manifest/F05-F10/error/retry contracts после этой фразы | После PASS зафиксировать commit/blob digest и запросить fresh exact approval именно этой revision | Superseded для исправленной revision | this SPEC |
| POST-SPEC REVIEW | После двух NEEDS-FIX циклов independent reviewer дал PASS semantic revision | Frozen semantic SHA-256 `805007E3...CCE211A`; все прежние findings closed | Проверить metadata-only delta, затем commit/push SPEC | Fresh approval ещё нужен | this SPEC |
