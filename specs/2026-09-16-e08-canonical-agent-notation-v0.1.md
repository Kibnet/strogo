# E08: canonical agent notation frontend v0.1

## 0. Метаданные

- Тип (профиль): `delivery-task` / `product-system-design` / language frontend
- Владелец: human owner of Strogo
- Масштаб: medium
- Целевое семейство / behavior baseline: Reserve v0, `Kernel.Core` typed DAG
- Поверхность: Codex / repository experiment
- Effective runtime: Не применимо; offline deterministic compiler tests
- Eval baseline / evidence: current Reserve v0 conformance, E07 three-path report
- Целевой релиз / ветка: `main`
- Ограничения: no LLM benchmark, no production effects, no host semantic change, no backend expansion
- Связанные ссылки: `docs/project-intent.md`, `docs/reserve-v0.md`, `specs/2026-09-04-controlled-language-experiment-v0.1.md`, E07 SPEC

## 1. Overview / Цель

Создать первый закрытый agent-facing frontend: ограниченная C#-подобная нотация, которую агент может выдавать как исходную программу, а компилятор однозначно переводит в существующий canonical `Kernel.Core` DAG. Нотация не исполняет C# и не принимает произвольные assembly, reflection, библиотеки или CLR semantics.

Outcome contract:

- Success means: каждый допустимый source из фиксированного корпуса даёт один canonical DAG, а его revision совпадает с заранее зафиксированным graph fixture; каждый запрещённый source получает детерминированный typed refusal до lowering.
- Итоговый артефакт / output: новый `Kernel.CSharpNotation` library, conformance suite, canonical source fixtures и machine-readable frontend report.
- Stop rules: любое расхождение source→DAG, недетерминированная ошибка, обход preparse limits или изменение `Kernel.Core/Kernel.Host` semantics блокирует checkpoint; live LLM calls и benchmark не запускаются.

## 2. Текущее состояние (AS-IS)

- `Kernel.Core` принимает strict JSON graph, проверяет типы/reachability/limits, строит IR и выполняет Reserve contract.
- `Kernel.Host` владеет policy, capabilities, CAS, commit, replay и persistence; E07 показал, что текущие отказные гарантии воспроизводятся без language path.
- Агентский вход пока фактически graph JSON. Каноническая запись вычисления, удобная для генерации агентом, не отделена от transport schema.
- Историческая controlled-language SPEC описывает более широкий offline experiment, но frontend и corpus ещё не реализованы.
- SDK и package pins являются доверенными входами конкретного checkout; новый frontend не меняет их автоматически.

## 3. Проблема

Свободный JSON graph слишком близок к внутреннему IR: агенту приходится выбирать структурные IDs, порядок зависимостей и транспортные поля, хотя эти решения не являются частью бизнес-намерения. Нужен более узкий canonical source, который уменьшает пространство синтаксических решений, но сохраняет точную семантику существующего ядра.

## 4. Цели дизайна

- Один source shape — один lowering; никаких синонимов и implicit conversions.
- Использовать существующий `Kernel.Core` validator, verifier, IR и host без копирования их семантики.
- Отделить parser diagnostics от graph validation diagnostics, сохраняя typed error code и locus.
- Делать source identity и output revision воспроизводимыми.
- Проверять положительные, отрицательные и resource-boundary случаи до подключения LLM.
- Сохранить путь к будущему Wasm/другому backend через typed DAG, не обещая portability этим checkpoint.

## 5. Non-Goals

- Не добавлять effects, callbacks, imports, capabilities или новые host rights.
- Не выполнять C# code, не emit/load assembly, не использовать Roslyn semantic model, reflection или dynamic binding.
- Не менять Reserve contract, JSON schema, SQLite schema, policy или commit protocol.
- Не измерять качество, цену или скорость LLM; это следующий отдельный experiment gate.
- Не поддерживать произвольные платформы, библиотеки, `if`, loops, exceptions, strings, floats или user-defined functions.
- Не считать frontend evidence доказательством G03/G04 для программ вне закрытой грамматики.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент / файл | Ответственность |
| --- | --- |
| `src/Kernel.CSharpNotation` | ASCII scanner, Roslyn syntax-as-data parser, closed grammar, lowering to `KernelProgram` |
| `tests/Kernel.CSharpNotation.Conformance` | positive corpus, negative corpus, determinism and resource-boundary tests |
| `tests/fixtures/notation-v0` | canonical sources, expected graph revisions and refusal expectations |
| `docs/evidence/e08-notation-*.json` | filtered machine-readable report; no generated binaries or DB |
| `Kernel.Core` | unchanged source of truth for node semantics, validation and revision |

### 6.2 Детальный дизайн

#### Source envelope

The public entry point is `NotationCompiler.Compile(ReadOnlySpan<byte> sourceUtf8)`. It hashes the original bytes, decodes with strict UTF-8 (`throwOnInvalidBytes: true`) and then scans exactly one source block. This keeps invalid encoding and BOM handling observable instead of silently normalizing a `string`.

The source block is:

```csharp
{
  long available = input.resourceAvailable;
  long quantity = input.requestedQuantity;
  bool enough = quantity <= available;
  long zero = 0L;
  long debit = Select(enough, quantity, zero);
  long remaining = checked(available - debit);
  return (accepted: enough, available: remaining, reserved: debit);
}
```

The scanner runs before syntax parsing and accepts only printable ASCII plus TAB/CR/LF. It rejects BOM, comments, directives, strings, interpolations, Unicode identifiers and trailing tokens. Limits are fixed: source ≤65536 UTF-8 bytes, ≤4096 tokens, delimiter nesting ≤32, ≤128 declarations, ≤32 statements between semicolons, and no unary chain longer than one operator.

#### Closed grammar and canonical lowering

The only declarations are single-assignment locals with explicit `long` or `bool` type. Identifiers match `[a-z][a-z0-9_]{0,47}`. The names `input`, `return`, `checked`, `Select` and output labels are reserved. `available`, `quantity` and `zero` are canonical binding names with the special rules shown below.

| Source form | Canonical graph operation |
| --- | --- |
| `long available = input.resourceAvailable;` | `input` field `state.available`, node ID `n.available` |
| `long quantity = input.requestedQuantity;` | `input` field `event.quantity`, node ID `n.quantity` |
| `long zero = 0L;` | `i64.const`, node ID `n.zero` |
| `long x = <signed I64 literal>;` | canonical `i64.const`; ID `n.const.i64.<canonical-decimal>` |
| `bool x = true;` / `bool x = false;` | `bool.const`; IDs `n.bool.true` / `n.bool.false` |
| `long x = checked(a + b);` | `i64.add_checked` |
| `long x = checked(a - b);` | `i64.sub_checked` |
| `bool x = a <= b;` | `i64.le` |
| `bool x = a == b;` | `i64.eq` |
| `bool x = !a;` | `bool.not` |
| `bool x = a & b;` / `a | b` | `bool.and` / `bool.or` |
| `long x = Select(p, a, b);` or `bool x = Select(p, a, b);` | strict `select` with three same-typed operands |
| `return (accepted: a, available: b, reserved: c);` | exact `OutputRefs` |

Only previously declared locals may be operands. The bindings `available` and `quantity` are accepted only with their exact input member expressions, and `zero` only with the exact `0L` literal. Other local IDs are `n.` plus the source identifier. All repeated equal literals are interned to the same canonical node: I64 values use decimal form with no leading zero (`0` or `-` plus digits), and bool values use lowercase `true`/`false`; the `n.zero` alias is the required spelling for I64 zero. No duplicate declaration, reassignment, shadowing, implicit conversion, nested expression, alternate spelling, short-circuit operator or statement is accepted. Canonical node ordering is delegated to `ProgramCodec`/`GraphValidator`; source declaration order cannot change revision when the resulting graph is identical.

The parser may use `Microsoft.CodeAnalysis.CSharp` **4.14.0** as a syntax tokenizer/parser, but it must inspect syntax-as-data and allowlist every node. It must not use Roslyn semantic compilation or execute source. The exact package version and lockfile are part of frontend identity.

#### Identity and errors

Frontend output contains:

```json
{
  "schemaVersion": "strogo.csharp-notation.v0.1",
  "frontendRevision": "<64 lowercase hex>",
  "sourceDigest": "<64 lowercase hex>",
  "programRevision": "<64 lowercase hex>",
  "status": "Accepted"
}
```

`sourceDigest` is lowercase SHA-256 of the original UTF-8 bytes. `programRevision` is the existing `ProgramCodec.Revision`. `frontendRevision` is lowercase SHA-256 of the canonical UTF-8 JSON descriptor `{schemaVersion, grammarRevision:"e08.1", parserPackage:"Microsoft.CodeAnalysis.CSharp/4.14.0", coreSchema:"kernel.v0", coreSemantics:"kernel.v0"}` with ordinal keys and no whitespace. It is independent of absolute paths and process IDs.

Refusals use a closed code set: `SourceEncodingInvalid`, `SourceTooLarge`, `TokenLimitExceeded`, `DelimiterDepthExceeded`, `UnaryChainExceeded`, `SyntaxInvalid`, `UnsupportedSyntax`, `GrammarInvalid`, `TypeMismatch`, `DuplicateLocal`, `ForwardReference`, `OutputInvalid`, `GraphInvalid`. The first diagnostic is selected by a fixed phase order: source limits → syntax → grammar → binding/types → graph validation. Diagnostics contain a stable locus and do not include absolute paths.

#### Evidence rules

The report records source digest, frontend identity, expected graph revision, actual graph revision, status, error code/locus and test name. It never records a claim that a source is “verified” unless existing `Kernel.Core` validation and verification have run. No report row may be produced from an untrusted claimed revision.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Compile valid notation | Agent submits a Reserve source block | Canonical graph revision and `Accepted` report | positive fixture and report row | AC1, AC2 |
| Submit unsupported construct | Agent includes comment, `if`, string or function call | Typed refusal with stable code/locus and no graph | negative fixture | AC3 |
| Repeat compilation | Compile identical bytes twice | Byte-equal graph/report identity | determinism test | AC4 |
| Exceed parser bound | Submit oversized/unary/deep source | Refusal before AST lowering | boundary test and phase evidence | AC5 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| no candidate | valid source | source → canonical graph | empty source → `SyntaxInvalid` | no host state touched |
| no candidate | forbidden syntax | source → typed refusal | first error wins | no partial graph |
| candidate exists | identical source | same digest/revision | concurrent calls are isolated and equal | frontend is stateless |
| candidate exists | changed source | new source digest/revision or refusal | output directory is no-overwrite | no persistence migration |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Surface syntax | agent | Closed C#-like block from §6.2, syntax-as-data only | 0.90 | another notation may be better for LLMs | Да |
| Parser implementation | agent | `Microsoft.CodeAnalysis.CSharp` 4.14.0 syntax parser, no semantic compilation | 0.86 | package/toolchain adds dependency | Да |
| Semantic target | user/agent | Existing Reserve v0 graph and `ProgramCodec` revision | 0.99 | frontend could accidentally fork semantics | Нет |
| Effects/capabilities | user | Out of scope; `effects=[]` and host-owned rights remain | 0.99 | no direct effect experiment yet | Нет |
| Benchmark | user | Out of scope; only offline compiler correctness | 0.99 | no G05 claim from frontend | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Graph semantics | `src/Kernel.Core` | none | existing JSON/Host unchanged | Core conformance + revision equality |
| Frontend source | new notation library | additive schema `strogo.csharp-notation.v0.1` | no persisted migration | fixture codec and golden reports |
| Dependencies | repository lockfiles | add pinned parser package if needed | locked restore required | build with `--locked-mode` |
| Evidence | docs/evidence | new filtered report | historical reports untouched | JSON schema and digest checks |

## 7. Бизнес-правила / Алгоритмы

1. A source can be accepted only if every syntax node belongs to the closed grammar and every graph passes existing validation.
2. The frontend cannot change owner contract, available rights or host policy.
3. Equivalent source forms are not accepted merely because they compute the same result; only the listed canonical forms are legal.
4. No partial graph or report with `Accepted` status is emitted after any refusal.
5. The compiler must preserve the distinction between parser refusal, graph invalidity and later host admission.

## 8. Точки интеграции и триггеры

- `NotationCompiler.Compile(ReadOnlySpan<byte> sourceUtf8)` is the only public frontend entry point; no path-based or implicit-encoding overload is part of E08.
- Tests call the frontend, then `ProgramCodec.CanonicalBytes`, `GraphValidator.Validate` and existing `Verifier` where the fixture requires it.
- `Kernel.Host` is used only by characterization tests; frontend unit tests do not write SQLite or invoke commit/replay.

## 9. Изменения модели данных / состояния

No production schema or host state change. The frontend report is derived evidence. Source and report files are immutable test artifacts; generated databases and binaries stay ignored.

## 10. Миграция / Rollout / Rollback

No rollout. The new project is additive and can be removed with its fixtures and evidence. Existing JSON graph, CLI and host commands remain the compatibility baseline. A failed locked restore or parser mismatch blocks the checkpoint rather than changing pins.

## 11. Тестирование и критерии приёмки

- AC1: canonical Reserve source compiles to the exact approved `Fixtures.Reserve()` revision and canonical bytes.
- AC2: at least 12 positive vectors cover all allowed operations, both strict branches, I64 boundaries, bool constants/operators and output mapping.
- AC3: at least 20 negative vectors reject comments, Unicode/BOM, unsupported syntax, implicit conversion, duplicate/forward locals, short-circuit operators and wrong outputs with stable codes.
- AC4: compiling every positive vector twice produces byte-equal graph and report identity; source and frontend digests are retained.
- AC5: oversized, deep, token-heavy and unary-heavy sources are rejected before syntax lowering and do not touch host state.
- AC6: the frontend never emits `Accepted` for a graph that existing `GraphValidator` rejects; all existing Core/Host/E07 suites remain green.
- AC7: filtered report and knowledge-log entry are committed; no claim of LLM advantage, effects or portability is added.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC1 | golden Reserve revision test | inspect canonical source | `e08-notation-report.json` | — |
| AC2 | positive corpus conformance | review vector inventory | same report | — |
| AC3 | negative corpus conformance | review first-error table | same report | — |
| AC4 | repeat compilation/hash test | compare report identities | same report | — |
| AC5 | preparse-boundary tests | inspect phase tags | same report | — |
| AC6 | Core/Host/E07 regression suites | scope review | test output | — |
| AC7 | required-file/claim lint | review knowledge log | commit and KB entry | — |

Commands after approval:

```powershell
dotnet restore --locked-mode
dotnet build Kernel.slnx -c Release --no-restore
dotnet run --project tests/Kernel.CSharpNotation.Conformance/Kernel.CSharpNotation.Conformance.csproj -c Release --no-build
dotnet run --project tests/Kernel.Conformance/Kernel.Conformance.csproj -c Release --no-build -- --suite e07
```

Stop if restore changes a lockfile, any positive vector diverges from graph revision, an unsupported source is executed, or a report claims verification without the existing verifier evidence.

## 12. Риски и edge cases

- Roslyn syntax acceptance can be broader than the closed grammar; every node must be allowlisted.
- C# literals and checked arithmetic have corner cases around `long.MinValue`; parse through `BigInteger` before narrowing.
- Source declaration order must not accidentally become semantic ordering beyond dependency order.
- A parser error is not a proof of program incorrectness; it is a language-surface refusal.
- The frontend may reduce syntax freedom without reducing an agent's search over wrong contracts; G05 remains unmeasured.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Почему C#-подобная форма, если язык для агента не обязан быть человекочитаемым? | Это экспериментальная control surface, не финальный UX | Синтаксис закрыт, canonical, и может быть заменён будущим notation без изменения DAG | accepted-risk |
| Не превращается ли Roslyn в скрытый компилятор C#? | Пакет может создавать ощущение выполнения C# | Только syntax-as-data allowlist; no semantic model/emit/load/execute | mitigated |
| Не выдаём ли frontend за доказательство G05? | Успешная компиляция ещё не измеряет LLM | Explicit non-goal, report wording и отдельный benchmark gate | mitigated |

### Rework Prevention Checklist

- Every visible result has a report row and fixture.
- Assumed syntax, parser dependency and semantic boundary are in the Decision Ledger.
- Positive, negative and preparse boundary cases are explicit.
- Existing semantics are reused through `ProgramCodec` and `GraphValidator`.
- No LLM/model/backend claim is hidden in acceptance criteria.

## 13. План выполнения

1. Получить owner approval этой версии SPEC.
2. Создать additive project and pinned dependency; сначала проверить source/lockfile identity.
3. Реализовать scanner, syntax allowlist, binder and lowering; не подключать Host.
4. Добавить positive/negative/boundary corpus and conformance runner.
5. Запустить Core/Host/E07 regressions, сохранить report and knowledge entry.
6. Провести post-EXEC review; benchmark, effects и backend вынести в следующие approval-gated этапы.

## 14. Открытые вопросы

Требуется только решение владельца по входу в EXEC: принять ли закрытую C#-подобную notation как первый экспериментальный agent-facing frontend. Изменение Reserve semantics, effects и benchmark этим вопросом не открываются.

## 15. Соответствие профилю

- Профиль: `delivery-task` / `product-system-design`.
- Выполненные требования: source of truth, closed grammar, typed errors, evidence contract, rollback, acceptance matrix, knowledge gate and explicit G01/G03/G04 boundary.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Kernel.CSharpNotation/**` | new frontend | canonical agent-facing source |
| `tests/Kernel.CSharpNotation.Conformance/**` | new tests and runner | verify grammar/lowering |
| `tests/fixtures/notation-v0/**` | source and expected identity fixtures | reproducible corpus |
| `docs/evidence/e08-notation-*.json` | filtered report | auditable output |
| `docs/knowledge-log.md` | implementation/result entries | preserve significant knowledge |
| `Kernel.slnx` and lockfiles | additive project/dependency references | build integration |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Agent source | strict JSON graph | closed C#-like source lowered to the same graph |
| Semantics | Core-owned | unchanged Core-owned semantics |
| Errors | JSON/parser/core errors | phase-ordered frontend typed refusals plus existing graph errors |
| Evidence | graph conformance | source digest + frontend identity + graph revision report |
| G05/G06 | not measured | still not measured |

## 18. Альтернативы и компромиссы

- **Свободный C#**: expressive, but requires sandbox/semantic compilation and weakens closed semantics; rejected.
- **Новый custom textual grammar**: better agent optimization may be possible, but adds a second lexer/parser before the first canonical frontend is measured; deferred.
- **Direct JSON only**: already exists, but exposes transport decisions and does not test notation reduction; insufficient.
- **Roslyn syntax-as-data**: chosen as a bounded experiment because it reuses a mature parser while the allowlist retains language control; it is not a final backend.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Статус | Комментарий |
| --- | --- | --- |
| Полнота и границы | PASS | цель, AS-IS, non-goals and rollback are explicit |
| Контракт и семантика | PASS | grammar maps only to existing Core nodes |
| Ошибки и безопасность | PASS | closed errors, preparse limits, no execution |
| Проверяемость | PASS | AC1–AC7 mapped to tests/evidence |
| Готовность к автономному EXEC | PARTIAL | owner must approve notation surface |
| Профиль | PASS | delivery-task/product-system-design requirements covered |

Итог: **НУЖНА ДОРАБОТКА / OWNER REVIEW** до получения approval.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| Ясность цели и границ | 5 | frontend-only checkpoint, no benchmark/effects |
| Понимание текущего состояния | 5 | Core/Host/E07 boundaries named |
| Конкретность целевого дизайна | 5 | grammar, lowering, errors and report fixed |
| Безопасность и откат | 5 | no execution, additive project, lockfile gate |
| Тестируемость | 5 | positive/negative/boundary matrix |
| Готовность к автономной реализации | 2 | exact owner approval is still required |

Итоговый балл: **27 / 30**; зона: под контролем, owner approval required.

### Role-Based Review Result

| Role | Applicability | Verdict | Required spec changes |
| --- | --- | --- | --- |
| Business/domain | applicable | PASS | Reserve contract preserved |
| UX/designer | applicable | PASS | source/report scenarios are explicit; no UI claim |
| Tester/validation | applicable | PASS | every AC has automated evidence |
| Developer/architect | applicable | PASS | parser/Core boundary and rollback are coherent |
| Delivery/operations/security | applicable | PASS | no runtime execution or production migration |

### Post-SPEC Review

- Статус: **PASS с owner gate**.
- Scope/Evidence: прочитаны `docs/project-intent.md`, current `Kernel.Core` codec/validator, E07 SPEC/report, controlled-language historical SPEC and repository instructions.
- Contract: the notation lowers only to existing graph operations; `Kernel.Host` and capabilities remain authoritative.
- Adversarial: syntax-as-data, semantic-model bypass, literal overflow, unary/depth limits, partial output and false `Accepted` paths are covered.
- Correction before approval: the initial draft's generic `n.`+source-name rule could not produce the existing `n.available`/`n.quantity`/`n.zero` graph IDs from the example. The current revision makes input bindings and the zero literal explicit and canonical, so AC1 is now mechanically attainable.
- Residual risk: this checkpoint tests a representation and compiler, not agent productivity or final human-facing syntax.
- Stop decision: do not implement until owner sends exact **«Спеку подтверждаю»** for this corrected file/version.

## Approval

Ожидается фраза: **«Спеку подтверждаю»**.

## 20. Журнал действий агента

| Фаза | Тип намерения / сценария | Уверенность | Не хватает | Следующее действие | Нужна передача владельцу | Решение владельца | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- |
| RESEARCH | Проверен текущий checkout после E07 | 0.99 | Нет для design draft | Зафиксировать next language checkpoint | Нет | E07 pushed at `1f41d0b` | Git, README, E07 report |
| SPEC | Сопоставлены G01/G03/G04 с frontend gap | 0.94 | Owner choice notation surface | Запросить exact approval | Да | Пока не получено | `docs/project-intent.md`, this SPEC |
| SPEC | Подготовлен closed grammar, error contract and AC matrix | 0.92 | Runtime implementation evidence | После approval создать frontend project | Да | Ожидается | this SPEC |
| SPEC | Исправлено несоответствие source names и canonical Reserve node IDs до approval | 0.98 | Нет для design gate | Зафиксировать correction и снова запросить approval | Да | Пока не получено | this SPEC, K-E06-106 |
