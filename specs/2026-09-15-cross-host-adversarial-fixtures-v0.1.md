# E07: cross-host adversarial fixtures v0.1

## 0. Метаданные

- Тип: `product-system-design` / experimental validation
- Владелец: human owner of Strogo
- Масштаб: medium
- Target behavior baseline: Kernel v0 Reserve contract
- Поверхность: Codex / repository experiment
- Eval baseline: existing `tests/Kernel.Conformance` host cases plus new fixture reports
- Ветка: `main`
- Ограничения: no implementation before owner approval; no language-advantage claim from a single host
- Связанные источники: Posting Board #11295, clarification #12482, `docs/reserve-v0.md`

## 1. Overview / Цель

Проверить, какие гарантии принадлежат языковому слою, а какие уже обеспечивает host API. В эксперименте один и тот же fault schedule проходит по трём путям: текущий language path, независимый direct checked-API baseline и второй host без повторного использования language evaluator.

Outcome contract:

- Success means: для каждого fixture сохранены canonical input, ordered steps, expected result/effects/mustNotHappen, enforcement tag и результат всех путей.
- Итоговый артефакт: `e07-cross-host-fixtures.v0.1` report и knowledge-log entry.
- Stop rules: отсутствие второго host или direct baseline блокирует causal claim; effect-smuggling не исполняется до фиксации effect seam.

## 2. Текущее состояние (AS-IS)

- `Kernel v0` предоставляет `Snapshot`, `Prepare`, `Commit`, `Replay`, `ProposePatch`, `SetPolicy` и `SetManifest` через trusted host.
- Host повторно проверяет state/program/policy/manifest/admission перед записью.
- Язык v0 описывает pure typed DAG; `effects` пусты, а единственная запись выполняется host после проверки результата.
- Existing conformance уже покрывает stale snapshots, CAS race, program/policy/manifest drift и rollback.
- Внешний contributor #11295 предложил три различающих задачи; формат обмена впервые предложен в #12476 и принят как fixture format, не parser schema. #12482 содержит последующее mapping-уточнение.

## 3. Проблема

Текущие 29 host-сценариев доказывают поведение защищённого прототипа, но не отделяют вклад language representation/proof от дисциплинированного host API.

## 4. Цели дизайна

- Сохранить один fault schedule и один contract для сравниваемых путей.
- Явно маркировать enforcement: `language`, `runtime`, `host`.
- Отделить proposal, implementation result и causal conclusion.
- Сохранить контрпример и состояние, которое не должно измениться.

## 5. Non-Goals

- Не измерять качество LLM, стоимость разработки или производительность.
- Не объявлять второй host доказательством portability.
- Не добавлять effects, callbacks или новый parser opcode в эту контрольную точку.
- Не заменять existing Kernel v0 contract.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент | Ответственность |
|---|---|
| Fixture codec | strict canonical JSON, ordered steps and expected outcome |
| Language path | compile/validate/execute the same contract |
| Direct checked-API baseline | independent host call sequence without language evaluator |
| Second host | separate implementation of the same state/contract rules |
| Comparator | classify equal guarantees and enforcement locations |

### 6.2 Детальный дизайн

Каждая задача использует объект:

```json
{
  "id": "stale-replay",
  "initial": {},
  "contract": {"id": "reserve.v0", "revision": "..."},
  "grants": [],
  "steps": [{"actor": "agent", "operation": "...", "args": {}, "expectedRevision": "..."}],
  "expected": {"result": "...", "effects": [], "mustNotHappen": []},
  "control": "..."
}
```

#### Task A — stale-revision replay

1. Capture state revision `R0`.
2. Prepare `e1` against `R0`; keep its token alive in the same host epoch.
3. Commit independent `e2` from the same resource and initial snapshot. `e1` and `e2` must have different event IDs/digests; policy, program, manifest and principal authorization remain unchanged.
4. Commit the old `e1` plan.

Expected: `StateConflict`; no new receipt, transition or state write for `e1`; replay of the durable `e2` receipt remains valid. ExistingEvent is intentionally not exercised: repeating a committed event must be a separate `AlreadyCommitted` control, and a removed token is `PrepareExpired`. Current expected enforcement is host commit re-check plus durable replay, not language-only.

#### Task B — contract change during a prepared run

1. Prepare `e1` under program/policy/manifest pointers `P0`.
2. Before effect commit, change exactly one pointer: `ProposePatch`, `SetPolicy` or `SetManifest`; preserve the principal's current rights so authorization does not mask the drift branch.
3. Commit the old prepare.

Expected: `ProgramChanged`, `PolicyChanged` or `AdmissionInvalidated`; no state/effect/receipt write for `e1`. The fixture must record whether the mutation occurs before commit checks, during the commit barrier, or after the linearization point.

#### Task C — effect smuggling

The current v0 schema has only pure DAG operations and `effects=[]`. The first fixture is therefore a negative boundary case: an effectful opcode, callback, hidden host call or undeclared effect must be rejected before admission. This task is blocked until a minimal declared effect seam exists; adding one is a separate SPEC and approval.

#### Comparison rule

The same fixture bytes, fault schedule and expected state are supplied to all paths. The report makes only observable claims: it records each path's outcome and enforcing component. Equal PASS means the guarantee was reproduced without the language path; a difference requires a concrete ablation/control and is reported as an observed divergence, not as proof that a general-purpose API cannot reproduce the guarantee. A second host alone is insufficient.

### 6.3 User-Observable Scenarios

Не применимо: artifact-only research checkpoint; visible output is a report and fixture set.

### 6.4 State / Interaction Matrix

| State | Trigger | Expected result | Failure case |
|---|---|---|---|
| `R0/P0` | Prepare | immutable prepared plan | invalid revision refuses before evaluation |
| prepared, state advanced | Commit old plan | `StateConflict`, no write | stale plan must not overwrite |
| prepared, contract pointer changed | Commit old plan | typed drift refusal, no write | mutation after linearization is a separate control |
| pure schema | effectful operation | pre-admission refusal | no hidden effect may execute |

### 6.5 Decision Ledger

| Decision | Owner | Default | Confidence | Risk | Needs user before EXEC |
|---|---|---|---:|---|---|
| Second host technology | user | independent minimal C# host with no `Kernel.Host` reference | 0.70 | same-runtime confounder | Да |
| Direct baseline boundary | agent | checked API with identical state/contract and fault schedule | 0.90 | baseline may duplicate host logic | Нет |
| Effect seam | user | separate future SPEC | 0.95 | task C otherwise proves only absence of surface | Да |
| Causal claim threshold | user | report observed outcomes and enforcement; use ablation for any stronger attribution | 0.95 | overclaiming impossibility from finite runs | Да |

## 7. Бизнес-правила / Алгоритмы

- Every expected effect is explicit and ordered.
- `mustNotHappen` is checked against durable state, receipts and external-effect log.
- A stale or drifted plan cannot consume an event ID unless the existing contract explicitly defines a durable business decision.
- Replay is read-only and verifies the historical receipt chain.

## 8. Точки интеграции и триггеры

Existing Kernel v0 API is the source of truth. New code, if approved, belongs in a separate fixture harness and must not weaken `Kernel.Host` admission.

## 9. Изменения модели данных / состояния

No production schema change. Reports and fixture artifacts are new validation outputs only.

## 10. Миграция / Rollout / Rollback

No rollout. Delete experimental fixture outputs to roll back; existing v0 behavior and receipts remain unchanged.

## 11. Тестирование и критерии приёмки

- AC1: three canonical fixture documents exist and validate strictly.
- AC2: Task A reproduces typed stale refusal and no-write state on all available paths.
- AC3: Task B covers program, policy and manifest drift with exact mutation order.
- AC4: Task C is either rejected as unsupported before admission or moved to a new approved effect SPEC; no hidden callback is silently accepted.
- AC5: direct baseline and second-host results are recorded beside language-path results.
- AC6: report distinguishes observed agreement from causal attribution and lists residual confounders.

No code/test implementation is authorized until owner approval. Existing characterization tests are evidence for AS-IS only.

## 12. Риски и edge cases

- A second C# host may share runtime behavior; report must label this confounder.
- Contract mutation after commit linearization is not a stale-plan failure; the schedule must name the linearization point.
- Replay can prove durability but not absence of an unmodelled external effect; `effects=[]` remains explicit.
- Direct baseline that calls the same `Kernel.Host` helper is not independent.

## 13. План выполнения

1. Owner confirms second-host and causal-threshold decisions.
2. Implement strict fixture codec and Task A/B harness only.
3. Implement independent direct baseline and second host.
4. Run same fault schedules and publish report.
5. Return to owner before any effect seam or language extension.

## 14. Открытые вопросы

- Which second-host technology and isolation level does the owner accept?
- Is a separate C# implementation sufficient, or is a different runtime required?
- What minimal effect model should Task C exercise, if any?

## 15. Соответствие профилю

- Профиль: product-system-design / experiment design.
- Выполненные требования: causal attribution, explicit boundaries, owner gates, knowledge continuity.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
|---|---|---|
| `specs/2026-09-15-cross-host-adversarial-fixtures-v0.1.md` | новый draft SPEC | зафиксировать эксперимент до EXEC |
| `docs/knowledge-log.md` | K-E06-091 | сохранить внешний insight и public evidence |

## 18. Альтернативы и компромиссы

- Только второй host: дешевле, но не отделяет language от duplicated host checks; отклонено.
- Только direct API baseline: показывает host behavior, но не causal portability; недостаточно.
- Сразу добавить effects: преждевременно и расширяет semantic surface; вынесено в отдельную approval-gated SPEC.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Статус | Комментарий |
|---|---|---|
| Полнота и границы | PASS | три задачи и non-goals определены |
| Проверяемость | PASS | expected result/effects/mustNotHappen и AC заданы |
| Готовность к автономному EXEC | PARTIAL | second host и effect seam требуют решения владельца |

Итог: **НУЖНА ДОРАБОТКА / OWNER REVIEW**

### Role-Based Review Result

| Role | Verdict | Required change |
|---|---|---|
| Business/domain | PASS | reserve contract preserved |
| Tester/validation | PASS | same schedule and negative checks explicit |
| Developer/architect | ASK-HUMAN | choose second host and independence boundary |
| Delivery/operations/security | PASS | no production migration; no new capability |
| UX/designer | Не применимо | artifact-only experiment |

### Post-SPEC Review

- Статус: **ASK-HUMAN**.
- Findings: second-host technology, causal threshold and effect seam require owner decisions.
- Current evidence: existing host characterization and public clarification #12482; current checkout host suite `18/18 cases; 496 assertions`; no new implementation.
- Stop decision: do not enter EXEC until the owner confirms the SPEC.

## Approval

Ожидается фраза: **«Спеку подтверждаю»**.

## 20. Журнал действий агента

| Фаза | Действие | Уверенность | Следующее действие | Нужна передача владельцу |
|---|---|---:|---|---|
| SPEC | Прочитан внешний challenge #11295 и текущий Kernel v0 contract | 0.95 | Зафиксировать три fixture и causal boundary | Да |
| SPEC | Опубликована clarification #12482, read-back seq `12482` | 0.95 | Получить owner decisions и approval | Да |
| SPEC | Создан draft cross-host fixture SPEC | 0.90 | Ждать `Спеку подтверждаю` и ответы на открытые вопросы | Да |
| SPEC | Исправлен causal-claim criterion по review: observable outcomes вместо доказательства невозможности API; источник fixture format уточнён на #12476 | 0.97 | Owner review и выбор second host остаются открыты | Да |
| SPEC | Запущен current-checkout host characterization: `18/18 cases; 496 assertions`, solution Release build `0/0` | 0.98 | Direct baseline и second host ещё не реализованы | Да |
