# E09: offline equivalence and calibration harness v0.1

## 0. Метаданные

- Тип / профиль: `delivery-task` / `product-system-design`.
- Владелец: Kibnet; автор: Codex.
- Масштаб: medium; additive исследовательский стенд, без изменения Core/Host semantics.
- Целевое семейство: G01, G03, G04 и подготовка измерения G05; G02/G06 не измеряются.
- Поверхность: Codex; effective runtime для будущей модели не применяется, live provider не используется.
- Eval baseline: E08 `Strogo.Notation` и существующий `Kernel.Core`/`Kernel.Host`; текущий E08 report — [`e08-notation-conformance.json`](../docs/evidence/e08-notation-conformance.json).
- Целевая ветка: `main`; delivery через checkpoint commits и push.
- Ограничения: не добавлять effects, capabilities, backend, второй бизнес-профиль, live LLM/API или production storage.
- Связанные документы: [`docs/project-intent.md`](../docs/project-intent.md), [`specs/2026-09-16-e08-canonical-agent-notation-v0.1.md`](2026-09-16-e08-canonical-agent-notation-v0.1.md), [`docs/knowledge-log.md`](../docs/knowledge-log.md).

## 1. Overview / Цель

После E08 у нас есть проверенный frontend, но нет доказательства, что paired representation можно сравнивать без скрытой подсказки, разного evaluator или смешения scripted evidence с результатом модели. E09 создаёт маленький offline harness, который калибрует эту цепочку до любого расходования model budget.

Outcome contract:

- Success means: один versioned Reserve corpus в двух arm (`graph-json` и `strogo-notation`) проходит через общий evaluator; положительные пары дают byte-equal canonical graph/IR и одинаковый observable outcome, а отрицательные случаи получают отдельные typed outcomes без false `Accepted`.
- Итоговый артефакт: additive experiment library/runner, 12 paired fixtures, независимый oracle/evaluator, strict manifest/export, deterministic report и conformance evidence.
- Stop rules: не запускать provider, не считать scripted run результатом LLM, не объявлять G05, не добавлять backend/effects и остановиться при расхождении arm, утечке expected graph/oracle в export или неоднозначной категории отказа.

## 2. Текущее состояние (AS-IS)

- `src/Strogo.Notation` lowering уже выдаёт `reserve.v0`; `tests/Strogo.Notation.Conformance` содержит 12 positive и 23 negative vectors.
- `Kernel.Core`/`Kernel.Host` владеют graph validation, evaluation, verification, CAS, replay и business contract. E07 показал согласованные outcomes трёх путей, но не language advantage.
- E08 vectors находятся в executable harness, а не в versioned paired corpus; нет общего arm manifest, export integrity или report, который отделяет representation correctness от model quality.
- `global.json` pins SDK `10.0.400`; на текущем host отсутствует именно эта версия, поэтому validation должна фиксировать используемый установленный SDK отдельно.

## 3. Проблема

Без калибровочного стенда любое дальнейшее сравнение notation с graph JSON может приписать языку разницу, созданную неодинаковым corpus, hidden expected graph, evaluator, feedback или provenance. Такой результат не может поддержать решение о развитии языка.

## 4. Цели дизайна

- Один trusted case описывает expected behavior и два представления, но evaluator принимает только candidate arm input.
- Общие Core/Host checks и независимый business oracle применяются одинаково к обоим arms.
- Все digests, версии, категории отказов и происхождение данных входят в immutable report.
- Scripted calibration явно отделена от будущих model runs; неизвестные usage/cost не превращаются в нули.
- Ошибка evaluator, malformed envelope, корректный отказ и неправильная программа различаются.
- Эксперимент остаётся обратимо additive и не расширяет исполняемый язык.

## 5. Non-Goals

- Нет live LLM/provider/API, model ranking, cost claim или статистического вывода о G05.
- Нет выполнения произвольного C#, assembly, shell, reflection или candidate library.
- Нет effects/capabilities/imports, backend/Wasm/LLVM, package manager, FFI или второй domain profile.
- Нет изменения `Kernel.Core`, `Kernel.Host`, SQLite schema, Reserve semantics или E08 grammar.
- Нет вывода о G02/G06, portability, machine code или production readiness.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент | Ответственность |
| --- | --- |
| `src/Strogo.Experiments` | strict manifests, paired corpus model, arm adapters, evaluator and report codec |
| `src/Strogo.ExperimentCli` | offline `calibrate`, `export`, `evaluate` and `report`; no agent runtime API |
| `fixtures/e09-reserve` | 12 trusted case definitions and two public arm inputs per case |
| `tests/Strogo.Experiments.Conformance` | equivalence, refusal, oracle, export-integrity and report determinism tests |
| `docs/evidence/e09-offline-calibration.json` | filtered evidence with scripted provenance |
| `Kernel.Core` / `Kernel.Host` | existing trusted validation/evaluation only; no source changes |

### 6.2 Детальный дизайн

Каждый case имеет stable `caseId`, `pairId`, `protocolRevision`, `arm`, input digest, source digest, expected outcome class и trusted expected graph вне export package. Corpus разделён на два слоя: `equivalence-vector` содержит canonical graph и notation source для внутренней проверки lowering; `job-starter` содержит только публичное задание и arm-specific incomplete/defective candidate для export/evaluate. Canonical expected source никогда не экспортируется как starter, а integrity test обязан доказать, что starter bytes и expected graph bytes не совпадают и что expected digest не входит в public manifest. Positive notation input компилируется только через `NotationCompiler`; graph input проходит только `ProgramCodec`. Оба результата затем проходят общий `GraphValidator`, `Lowerer`, evaluator и business oracle.

Минимальный corpus — 12 случаев, включающий baseline, accepted/rejected Reserve branches, I64 boundary values, checked overflow in selected/unselected branch, bool `Select`, `bool.not`, strict `and/or`, malformed graph, typed notation refusal и deterministic declaration-order pair. Cases должны различать success, correct refusal и infrastructure failure; один случай не получает скрытый repair answer.

Нормативный набор trusted `equivalence-vector` получает следующие IDs; case ID и порядок не являются случайным runtime output:

| Case | Что различает | Обязательное покрытие |
| --- | --- | --- |
| `R01-baseline` | accepted Reserve, обычный input path | `input`, `i64.le`, `select`, checked subtraction |
| `R02-bool-true` | constant accepted branch | `bool.const(true)` |
| `R03-bool-false` | constant refusal branch | `bool.const(false)` |
| `R04-not` | derived boolean | `bool.not` |
| `R05-and` | strict conjunction | `bool.and`, both operands evaluated |
| `R06-or` | strict disjunction | `bool.or`, both operands evaluated |
| `R07-eq` | equality instead of ordering | `i64.eq` |
| `R08-bool-select` | boolean result selection | typed `select` with `Bool` branches |
| `R09-add` | checked addition path | `i64.add_checked` |
| `R10-min` | signed lower boundary | `long.MinValue` literal and checked operation |
| `R11-max` | signed upper boundary | `long.MaxValue` literal and checked operation |
| `R12-order` | source-order freedom | declaration reorder with identical graph revision |

The vector manifest additionally records selected and unselected overflow inputs for `R09`/`R10`/`R11`; these are input rows, not extra cases. Negative transport/grammar/graph/evaluator cases are separate and cannot inflate the 12-case positive count.

`export` пишет только allowlisted job bundle: protocol/corpus/arm/case IDs, source bytes, digests и public input contract. В bundle запрещены expected graph, oracle implementation, соседние cases, repository paths и previous responses. Hash collision or overwrite is refusal. `evaluate` читает owner-selected response/candidate file as data, не исполняет его и не принимает IDs/path из candidate как команды.

Report stages: `transport`, `frontend`, `graph`, `ir`, `oracle`, `outcome`, `provenance`. Categories are closed: `Solved`, `ValidRefusal`, `WrongRefusal`, `InvalidCandidate`, `InfrastructureFailure`, `EvaluatorMismatch`. A report cannot set `eligibleForLlmClaims=true`; `dataOrigin=scripted` and `mode=offline-calibration` are mandatory in this checkpoint.

Observable equality for positive pairs is canonical graph bytes/revision, lowered IR revision, evaluation output/trace and business oracle result. Runtime timestamps, receipt IDs and process paths are excluded from semantic equality. A mismatch aborts calibration and cannot be averaged away.

#### 6.2.1 Evaluator order and manifest identity

For an `Accepted` candidate the runner executes this fixed order: arm adapter → strict `KernelProgram` parse/compile → `GraphValidator.Validate` → `Lowerer.Lower` → `ReferenceInterpreter.Evaluate` and `IrInterpreter.Evaluate` on the same input vector → independent `ReserveOracle` implemented in the experiment project with `BigInteger` arithmetic → outcome comparison. A refusal stops before graph evaluation. The runner never treats the candidate's claimed revision, status or expected outcome as evidence.

`ReserveOracle` checks input preconditions, accepted/remaining/reserved postconditions and checked-overflow behavior without calling `Kernel.Core` evaluators. Its source, version and digest are part of the trusted tool inventory. Agreement between the two Core execution paths is recorded separately from agreement with the oracle; an oracle disagreement is `EvaluatorMismatch`, not `InvalidCandidate`.

The immutable manifest contains exactly the protocol revision, corpus revision, case/pair IDs, arm, input digest, source digest, frontend/parser/lockfile digest, Core/Host/solver/oracle tool digests, mode, data origin and provenance status. It excludes timestamps, process IDs, absolute paths, previous responses and expected graph bytes from the exported job. Any missing, extra or altered identity field makes comparison ineligible and produces `ArtifactMismatch`.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Calibrate corpus | Run offline calibration | deterministic PASS/FAIL report with both arms and provenance | report + conformance output | AC1–AC4 |
| Export one job | Request an arm/case bundle | exact allowlist and digests, no expected graph/oracle | export manifest/integrity test | AC5 |
| Evaluate candidate data | Submit a response file | typed stage outcome; no candidate execution | negative/evaluator tests | AC3, AC6 |
| Repeat report | Run report twice | byte-equal report for same immutable inputs | determinism hash | AC4, AC7 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| No run | `calibrate` | new immutable run directory | existing ID refuses overwrite | offline only |
| Exported | `evaluate` | one terminal stage record | malformed/foreign bundle refuses | response is data |
| Evaluated | `report` | read-only deterministic report | changed artifact digest => `ArtifactMismatch` | no re-evaluation |
| Active writer | second `report` | `RunBusy` or retryable refusal | no partial report accepted | single writer lock |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| First calibration corpus | agent | 12 Reserve cases derived from E08/Core fixtures | 0.93 | too narrow for future G05 | Нет |
| Arm names | agent | `graph-json` and `strogo-notation` | 0.98 | misleading “full language” claim | Нет |
| Model execution | user/agent | disabled; scripted only | 0.99 | accidental spend or false claim | Нет |
| Export trust boundary | agent | expected graph/oracle stay outside bundle | 0.95 | hidden oracle invalidates comparison | Нет |
| Future live provider/model/budget | user | deferred to separate SPEC | 0.99 | product/financial commitment | Да, но не для E09 |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Frontend | `NotationCompiler`, E08 lockfile | adapter only | no grammar change | paired revision equality |
| Graph | `ProgramCodec`, `GraphValidator` | read-only reuse | no schema migration | Core conformance |
| Host | `Kernel.Host` | no change | existing behavior preserved | full host suite |
| Run data | new additive run root | manifests/reports only | no DB migration | integrity/restart tests |
| Provenance | git/lockfile/Core/solver digests | include in manifest | mismatch refuses comparison | report checks |

## 7. Бизнес-правила / Алгоритмы

1. Positive pair is comparable only when protocol, corpus, Core/Host/solver identities and trusted case digest match.
2. A refusal is valid only when its closed code and stage match the case expectation; arbitrary parser failure is not proof of forbidden intent.
3. An evaluator mismatch is terminal infrastructure evidence, never a model/arm score.
4. Scripted records may calibrate the pipeline but cannot become measured LLM outcomes.
5. A report with unknown provenance, usage or cost preserves `null/unknown`; it never substitutes zero.

## 8. Точки интеграции и триггеры

- `Strogo.Notation.NotationCompiler.Compile(ReadOnlySpan<byte>)` is the only notation adapter entry point.
- `ProgramCodec.Parse`, `GraphValidator.Validate`, `Lowerer` and existing evaluation/oracle APIs are reused without semantic changes.
- CLI commands operate on explicit owner-selected directories and never on candidate-provided paths.

## 9. Изменения модели данных / состояния

Новые immutable records: `CalibrationManifest`, `CaseManifest`, `ArmInput`, `StageResult`, `EvaluationReport`. Raw source and response bytes are stored separately from derived report. Existing DB and receipts are not modified. Report identity excludes timestamps and process paths.

## 10. Миграция / Rollout / Rollback

Additive projects and fixtures only. Existing commands remain unchanged. A run never overwrites an existing artifact with different bytes. Rollback is the previous Git checkpoint; failed calibration artifacts remain for audit and are not rewritten.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- AC1: 12 paired positive cases compile through both arms to byte-equal canonical graph and equal IR revision.
- AC2: cases cover all E08 opcodes, I64 boundaries, strict branches, selected/unselected overflow and declaration-order determinism.
- AC3: at least 20 negative cases classify malformed transport, graph/type errors, notation refusals, unsupported effects and evaluator mismatch without execution.
- AC4: calibration and report are deterministic; repeated run bytes and report digests are equal; mismatch aborts rather than averages.
- AC5: export allowlist test proves expected graph/oracle/repository paths are absent, every file digest is bound, and no exported `job-starter` is byte-equal to its trusted expected graph.
- AC6: scripted provenance is explicit and no report field can claim LLM quality, cost, G05, portability or backend success.
- AC7: locked restore/build, E08, E07 and full Core/Host regressions remain green; no Core/Host semantic diff is introduced.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC1–AC2 | paired corpus/equivalence runner | inspect case manifest | `e09-offline-calibration.json` | — |
| AC3 | negative/refusal/evaluator suite | inspect closed categories | negative report | — |
| AC4 | repeated run/report hash | compare Git-clean roots | determinism report | — |
| AC5 | export allowlist/hash test | inspect bundle inventory | export integrity JSON | — |
| AC6 | provenance/report validator | review claims | calibration report | — |
| AC7 | locked restore, Release build, E08/E07/Core suites | `git diff` review | command logs | — |

Команды после approval:

```powershell
dotnet restore --locked-mode
dotnet build Kernel.slnx -c Release --no-restore
dotnet run --project tests/Strogo.Experiments.Conformance/Strogo.Experiments.Conformance.csproj -c Release --no-build -- --report docs/evidence/e09-offline-calibration.json
dotnet run --project tests/Strogo.Notation.Conformance/Strogo.Notation.Conformance.csproj -c Release --no-build -- --report docs/evidence/e08-notation-conformance.json
dotnet run --project tests/Kernel.Conformance/Kernel.Conformance.csproj -c Release --no-build -- --suite e07
dotnet run --project tests/Kernel.Conformance/Kernel.Conformance.csproj -c Release --no-build
```

Stop if any arm diverges, export contains hidden expected data, an evaluator mismatch is reclassified as candidate error, or a report claims a live/model result.

## 12. Риски и edge cases

- Reserve-only corpus may show a ceiling effect and cannot establish general G05; report must say so.
- Since both arms lower to the same Core, equivalence validates the harness and frontend boundary, not agent productivity.
- Independent oracle can share a defect with Core; retain existing independent business oracle and label trust components.
- Export isolation is package-level, not an OS sandbox.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Это снова не измеряет агента» | live model intentionally excluded | call it calibration, not benchmark; live pilot separate | mitigated |
| «Одинаковый Core заранее делает arms равными» | E09 tests representation/evaluator integrity | declare equality as precondition for future paired pilot, not language advantage | accepted-risk |
| «12 Reserve cases слишком мало» | narrow domain | use fixed calibration gate; do not generalize and require new corpus for live | accepted-risk |
| «Почему expected graph скрыт?» | export must not leak oracle or make the graph arm trivial | separate internal equivalence vectors from non-solution job starters and test byte inequality | mitigated |

### Rework Prevention Checklist

- User sees a deterministic calibration report and explicit typed refusal/outcome categories.
- Every scenario maps to an AC and artifact.
- Model, provider and budget decisions are explicitly deferred.
- Negative and integrity cases are required before any future live run.

## 13. План выполнения

1. Create additive experiment project, lockfile and strict manifest records.
2. Extract 12 paired fixtures from E08/Core without copying expected graph into exported jobs.
3. Implement common evaluator, independent oracle adapter and closed report categories.
4. Add negative, export-integrity and determinism tests.
5. Run E08/E07/Core regressions, save evidence and knowledge entry.
6. Perform post-EXEC review; stop before live model/backend/effects.

## 14. Открытые вопросы

Для E09 нет блокирующего user-owned решения; требуется только owner approval этой SPEC. Выбор provider/model/budget для live pilot намеренно остаётся отдельным вопросом и не должен быть решён внутри E09.

## 15. Соответствие профилю

- Профиль: `delivery-task` / `product-system-design`.
- Выполнены требования: boundary, output/evidence contract, negative cases, rollback, AC-to-test mapping, knowledge gate и review loops.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Strogo.Experiments/**` | additive manifests/evaluator/report | shared calibration contract |
| `src/Strogo.ExperimentCli/**` | offline commands | reproducible operator workflow |
| `fixtures/e09-reserve/**` | 12 paired cases | controlled corpus |
| `tests/Strogo.Experiments.Conformance/**` | equivalence/negative/integrity tests | acceptance evidence |
| `docs/evidence/e09-offline-calibration.json` | filtered report | auditable result |
| `docs/knowledge-log.md` | decisions/results | preserve significant knowledge |
| `Kernel.slnx` and lockfiles | additive integration | reproducible build |

## 17. Таблица соответствий

| Область | Было | Стало после E09 |
| --- | --- | --- |
| Representation | E08 frontend tested alone | paired graph/notation corpus with common evaluator |
| Evidence | source→DAG report | provenance-bound offline calibration report |
| G05 | unmeasured | measurement prerequisites calibrated; no claim yet |
| Runtime | Core/Host existing | unchanged and regression-checked |

## 18. Альтернативы и компромиссы

- **Сразу live LLM pilot:** rejected; without calibration, evaluator/provenance confounds can masquerade as language effect.
- **Сразу Wasm/backend:** deferred; changes representation and runtime simultaneously and cannot explain a result.
- **Only extend E08 vectors:** insufficient; it proves frontend correctness but not fair paired packaging/evaluator boundaries.
- **Chosen offline harness:** smallest next step that makes a later G05 experiment auditable without spending model budget.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1–5 | PASS | purpose, AS-IS, root problem, goals and non-goals are explicit |
| B. Качество дизайна | 6–10 | PASS | adapter/evaluator/data/error responsibilities and rollback are bounded |
| C. Безопасность изменений | 11–13 | PASS | no execution/effects/backend, additive artifacts, no overwrite |
| D. Проверяемость | 14–16 | PASS | AC1–AC7 map to tests, artifacts and stop rules |
| E. Готовность к автономной реализации | 17–19 | PASS | no unresolved user-owned decision inside E09 |
| F. Соответствие профилю | 20 | PASS | delivery/product-system-design requirements covered |

Итог: **ГОТОВО к owner review; реализация запрещена до approval**.

### SPEC Rubric Result

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Ясность цели и границ | 5 | offline calibration, no model/backend claim |
| Понимание текущего состояния | 5 | E08/Core/Host/E07 evidence named |
| Конкретность дизайна | 5 | paired corpus, evaluator, export, categories and report fixed |
| Безопасность/откат | 5 | additive roots, allowlist, digest binding and rollback |
| Тестируемость | 5 | positive, negative, integrity, determinism and regressions |
| Готовность реализации | 5 | implementation choices are additive and owner-independent |

Итог: **30 / 30**, зона: готово к автономной реализации после exact approval.

### Role-Based Review Result

| Role | Applicability | Verdict | Required spec changes |
| --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | PASS | Reserve case/oracle boundaries are explicit |
| UX / designer | applicable | PASS | report and refusal states are user-visible artifacts; no visual UI needed |
| Tester / validation | applicable | PASS | every AC has negative/integrity evidence |
| Developer / architect | applicable | PASS | additive API, trust boundary and no semantic drift are coherent |
| Delivery / operations / security | applicable | PASS | locked restore, bundle allowlist and rollback are defined |

### Post-SPEC Review

- Статус: **PASS с owner gate**.
- Scope/Evidence: reviewed E08 SPEC/report/code, Core/Host/E07 sources, historical calibration SPEC and current knowledge log.
- Contract: E09 calibrates representation/evaluator evidence only; it cannot claim G05 or live model quality.
- Adversarial: hidden expected graph, candidate execution, report provenance forgery, refusal misclassification, overwrite and arm mismatch are explicit negative surfaces.
- Findings: `Нет находок` после re-read; the 12-case ceiling and no-live boundary are recorded as residual risks, not hidden assumptions.
- Manual-review challenge: a reviewer should inspect an exported bundle and verify that removing the trusted evaluator still leaves no expected graph/oracle in the package.
- Stop decision: ask owner for exact **«Спеку подтверждаю»**; do not modify code before it.

### Post-EXEC Review

До approval: **Не выполнен**.

## Approval

Ожидается фраза: **«Спеку подтверждаю»**.

## 20. Журнал действий агента

| Фаза | Тип намерения / сценария | Уверенность | Не хватает | Следующее действие | Нужна передача владельцу | Решение владельца | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- |
| RESEARCH | Проверен текущий E08 checkout, Git state и public thread | 0.99 | Новых board replies нет | Выбрать следующий language-specific checkpoint | Нет | Не требовалось | commits `49ce31e`, board seq `12804` |
| SPEC | Сопоставлен E08 gap с G01/G03/G04/G05 и historical calibration design | 0.94 | Owner approval E09 | Завершить review и запросить approval | Да | Ожидается | this SPEC, E08 report, project intent |
| SPEC | Зафиксированы paired corpus, evaluator, export/provenance и stop rules | 0.93 | Runtime implementation evidence | После approval создать additive experiment projects | Да | Ожидается | this SPEC |
| SPEC | Запрошен внешний counterexample к paired corpus, export boundary и refusal scoring | 0.91 | Ответы board могут уточнить risk, но не заменяют owner approval | Учесть только рациональные, проверяемые предложения | Нет | Не требовалось | public reply `#12809`, id `1b131103-e945-48a4-837b-478d8b0a8b09`, read-back verified |
| SPEC | Зафиксирован точный evaluator order, independent oracle seam и manifest identity | 0.94 | Runtime evidence | Повторить contract review и запросить approval | Да | Ожидается | this SPEC §6.2.1 |
| SPEC | Опубликовано evaluator уточнение и выполнен read-back | 0.94 | Новых внешних counterexamples нет | Ожидать owner approval E09 | Да | Ожидается | public reply `#12811`, id `5480eead-bcb6-4615-946a-4cb0c5913817`, read-back verified |
| SPEC | Разделены trusted equivalence vectors и exportable non-solution starters | 0.96 | Runtime evidence | Повторить contract review и запросить approval | Да | Ожидается | this SPEC §6.2, AC5 |
| SPEC | Публично сообщён graph-arm leakage risk и выполнен read-back | 0.96 | Новых внешних counterexamples нет | Ожидать owner approval E09 | Да | Ожидается | public reply `#12812`, id `2e162a84-0496-4eb8-a4a6-fe2b6ef49287`, read-back verified |
| SPEC | Зафиксирован нормативный 12-case corpus matrix и coverage boundary | 0.97 | Runtime evidence | Запросить approval без расширения scope | Да | Ожидается | this SPEC §6.2 |
