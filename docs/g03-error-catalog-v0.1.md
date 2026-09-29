# G03: каталог приоритетных семейств ошибок v0.1

Этот документ является проекцией нормативного [`catalog.json`](../fixtures/g03-error-catalog/v0.1/catalog.json). Полный исходный инвентарь и решения находятся в [реестре источников](research/g03-evidence-register-v0.1.md). При расхождении с JSON выпуск недействителен до исправления проекции.

Каталог относится к ограниченным прикладным модулям с подтверждённым человеком контрактом, явными состояниями, эффектами и ресурсными пределами. Ошибка оценивается относительно этого контракта. Неверная человеческая цель, дефект инструмента и дефект испытательного стенда учитываются отдельно.

| ID | Семейство | Нарушенное обязательство | Обязательные отрицательные формы | Положительные контроли | Источники |
| --- | --- | --- | --- | --- | --- |
| G03-F01 | `PreconditionValidation` | Required input precondition is missing or weakened | missing-guard; inverted-or-partial-guard; invalid-input-accepted | valid-boundary-input; specified-invalid-outcome | S02, S03, S05 |
| G03-F02 | `ConditionalBoundary` | Approved conditional branch or boundary is wrong | relational-boundary; predicate-negation; boolean-composition-or-branch | first-valid-branch; second-valid-branch-at-boundary | S02, S03, S04 |
| G03-F03 | `ArithmeticQuantity` | Approved quantity, unit, operation, or overflow rule is wrong | arithmetic-replacement; wrong-constant-or-operand; overflow-or-unit-boundary | normal-quantity; extreme-valid-quantity | S01, S02, S04 |
| G03-F04 | `ValueDataflow` | Approved source, assignment, default, or return mapping is wrong | wrong-variable-or-field; omitted-or-overwritten-assignment; wrong-return-or-default | first-distinct-value; second-distinct-value | S01, S02, S03, S04 |
| G03-F05 | `CallInterface` | Transient call choice, presence, or parameter binding is wrong | call-deletion-or-duplication; wrong-callable; wrong-parameter-order-or-type | correct-call; supported-callable-version | S01, S03, S07 |
| G03-F06 | `CollectionCardinality` | Collection index, membership, or cardinality semantics are wrong | index-boundary; omitted-or-duplicated-element; empty-or-singleton-aggregation | empty-collection; singleton-and-upper-bound | S02, S04, S05 |
| G03-F07 | `StateTransitionSequence` | Approved state transition or sequence invariant is violated | forbidden-transition; missing-or-reordered-transition; stale-prior-state | first-allowed-transition; second-allowed-transition | S01, S07, S08 |
| G03-F08 | `FailureRecovery` | Expected failure is swallowed, misclassified, or recovered incorrectly | swallowed-error; wrong-error-mapping; partial-recovery | success-path; expected-external-failure | S03, S08 |
| G03-F09 | `AuthorityCapability` | Read, write, delete, send, or disclosure exceeds approved authority | missing-authority; wrong-subject-or-resource-scope; forbidden-sink-or-data-flow | authorized-same-operation; authorized-distinct-scope | S05 |
| G03-F10 | `RepresentationPersistence` | Persisted representation, configuration, or query-result mapping is wrong | wrong-persisted-field-or-type; incompatible-stored-schema-or-config; wrong-query-result-or-roundtrip-mapping | supported-roundtrip; supported-schema-or-config-version | S05, S07 |
| G03-F11 | `ConcurrencyAtomicity` | Concurrent ordering or atomicity violates approved behavior | competing-update; check-use-interleaving; order-or-deadlock-schedule | first-valid-schedule; second-valid-schedule | S01, S06 |
| G03-F12 | `ResourceTermination` | Work, allocation, retry, or managed resource exceeds approved bound | unbounded-loop-or-retry; work-or-allocation-budget; unreleased-resource-or-timeout | workload-at-limit; specified-over-limit-refusal | S05, S08 |

## Правило оценки

Каждое семейство имеет одинаковый вес. Для каждой применимой пары семейство × домен нужны механизированное обобщающее доказательство `VerifiedGeneralization`, все заранее зарегистрированные отрицательные specimens с исходом `ExcludedByConstruction` или `RejectedPreExecution`, и все matched positives с исходом `AcceptedEquivalent`. Одних пройденных примеров недостаточно.

Глобальный порог — **7 из 12**. Каждый зарегистрированный подходящий домен отдельно должен покрыть строго больше половины применимых семейств; доменов минимум два, у каждого не менее восьми применимых семейств. Учитываются все домены утверждённой ревизии реестра. `NotApplicable` не уменьшает глобальный знаменатель 12.

`RuntimeDetected`, `AdmittedFault`, `FalseRejection`, `UnsupportedUsefulTask`, `MissingGeneralization` и `RefutedGeneralization` не дают покрытия. Невалидное evidence делает cohort `EvaluationIncomplete`. Несогласованные digests/schema входных артефактов дают типизированный отказ до отчёта.

Порядок первичной классификации: G03-F09 → G03-F11 → G03-F12 → G03-F08 → G03-F07 → G03-F10 → G03-F05 → G03-F06 → G03-F01 → G03-F03 → G03-F04 → G03-F02. Один specimen имеет ровно один primary family; независимые faults делают его невалидным.

Закрытые outcomes: negative — `ExcludedByConstruction`, `RejectedPreExecution`, `RuntimeDetected`, `AdmittedFault`; positive — `AcceptedEquivalent`, `FalseRejection`, `UnsupportedUsefulTask`; generalization — `VerifiedGeneralization`, `MissingGeneralization`, `RefutedGeneralization`. Evidence validity: `Valid`, `Invalid`. Invalid reasons: `SchemaMismatch`, `DigestMismatch`, `ProvenanceMismatch`, `InfrastructureFailure`, `HarnessDefect`, `OracleAmbiguous`, `EquivalentSpecimen`, `MultiFaultSpecimen`, `AmbiguousPrimaryFamily`, `DomainCollision`, `IncompleteCohort`. Terminal results: `PriorityCatalogueMajoritySupported`, `PriorityCatalogueMajorityNotSupported`, `EvaluationIncomplete`. Applicability: `Applicable`, `NotApplicable`.

Даже terminal `PriorityCatalogueMajoritySupported` устанавливает только результат для этого приоритетного каталога. Целевая частотность классов и достижение G03 пока не измерены. Отдельное исследование распространённости требуется до owner-level вывода.

## Валидатор и синтетические контроли

`Strogo.ExperimentCli g03 validate` принимает `--source-row-manifest`, `--catalog`, `--domain-registry` и `--evaluation-plan`; `g03 score` дополнительно требует `--observations` и `--report`. Оба режима выдают канонический JSON. Несоответствие схемы, идентичности артефактов или отсутствующее raw evidence завершает команду типизированным отказом и кодом `1` до записи отчёта. Schema-valid конфликт derived outcome с raw receipt или строка `evidenceValidity=Invalid` дают отчёт `EvaluationIncomplete` и код `1`.

`g03 validate` проверяет общий формат registry/plan. В этой версии `g03 score` принимает только два exact синтетических плана из [`controls`](../fixtures/g03-error-catalog/v0.1/controls): произвольные, в том числе внешне «реальные», планы получают `G03EvidenceInvalid`. Report явно содержит `evidenceScope=SyntheticControlOnly`. Это исключает выдачу связности самосоставленных receipts за реальную проверку программы.

Все JSON в `controls` — искусственные проверки оценщика, не доказательства свойств Strogo. В них два вымышленных домена и фиктивные машинные identities/receipts. `7of12` проходит порог; `6of12` не проходит; `domain-floor` имеет 7 глобально покрытых семейств, но только 4/8 во втором домене; `runtime-all`, `unsupported`, `missing-generalization`, `refuted-generalization`, `invalid-evidence`, `incomplete` и `valid-retry` проверяют отдельные failure paths. `raw/sha256/` хранит content-addressed синтетические receipts и артефакты, digest которых оценщик перепроверяет. Raw specimen receipt связывает outcome с exact planned specimen, evaluator и attempt. Повтор допускается только после `InfrastructureFailure` до exposure с теми же generation, arm, oracle и candidate identities. Report включает digest всех шести схем.

Проверка связности receipt не доказывает истинность его содержания: в реальном эксперименте trusted evaluator и checker должны самостоятельно сформировать raw evidence и проверить обобщение. Такая процедура, выбор второго домена и подтверждение человеком registry/plan относятся к отдельной SPEC. Синтетическое `VerifiedGeneralization` проверяет правило подсчёта, а не универсальное свойство языка.
