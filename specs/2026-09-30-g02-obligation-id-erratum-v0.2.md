# G02: отображение lowerer obligation IDs в E05 wire

## 0. Метаданные

- QUEST expanded: изменение нормативных proof identities; security/public artifact contract. Владелец Kibnet; профиль dotnet-backend-api; Codex desktop, Windows x64; medium; main.
- Статус: SPEC, не утверждена. Canonical template: центральный `templates/specs/_template.md`; central QUEST/review/commit policies и repository AGENTS.
- Связи: [replay SPEC](2026-09-30-g02-fresh-dafny-replay-v0.2.md), [proof wire](2026-09-30-g02-proof-artifact-wire-v0.2.md), [E05](2026-09-07-e05-two-stage-admission-amendment.md), K-G02-017. Цели G02/G04; модель агента не часть proof identity, SDK10.0.400/Dafny4.11.0 остаются закреплены.

## 1. Overview / цель

Продолжить исходную задачу: свежий Dafny replay должен проверять held proof package, а не только standalone source. Для этого устранить противоречие raw lowerer IDs и E05 grammar, сохранив точную связь обязательств с source map и строгий wire. Success: обе существующие proof-positive shapes могут сформировать wire-valid IDs, а forged transcript/vector обнаруживаются replay; это не release admission.

## 2. AS-IS

`DafnyMixedLowering` возвращает `candidate-postcondition-addOne`; replay SPEC §3 переносит `DafnyProofObligation.Id` без преобразования. E05 требует `[a-z0-9][a-z0-9._-]{0,63}`. `G02ProofTranscript.NormalizeVerified` проверяет только nonempty/unique raw IDs, но `G02StoredIdentityVerifier` использует `OwnerAdmissionWire.Id`. Реальный composition probe завершился `PackageProofMetadataInvalid` на первом scalar fixture, ещё до tool opening. Незавершённый implementation candidate сохранён отдельно; production integration не опубликована.

## 3. Проблема

Один approved контракт разрешает raw mixed-case/long IDs, другой отвергает их. Lowercasing необратим и смешивает `addOne` с `addone`; truncation также смешивает разные IDs. Нельзя считать успешный source proof доказательством wire compatibility.

## 4. Цели дизайна

Сохранить E05 grammar, raw lowerer IDs и source-map loci, case sensitivity, deterministic mapping и отказ при неоднозначности. Не менять семантику исходной программы или набор проверяемых свойств.

## 5. Non-Goals

Не расширять E05 IDs; не переименовывать source-map entities; не менять approval/admission schema, evidenceDigest, toolchain tags или SDK. Не принимать D02 смысл, release package, host ACL или runtime Load. Не обещать отсутствия SHA-256 collisions как математического факта.

## 6. TO-BE

### 6.1 Ответственность

`G02ProofTranscript` определяет wire mapping и vector ordering. Lowerer по-прежнему возвращает exact raw IDs и source-map loci. Regenerator определяет набор raw obligations из held module/bundle; fresh replay повторяет тот же mapping. Stored checker сохраняет существующую E05 grammar.

### 6.2 Нормативный дизайн

Для exact nonempty raw `DafnyProofObligation.Id`:

`wireId = lowercaseHex(SHA256(UTF8("strogo.proof.v0.2/obligation-id\n") || strictUTF8(rawId)))`.

Результат ровно 64 lowercase hex, без префикса/обрезания/нормализации Unicode или case folding. Некорректный UTF-16, empty raw ID, duplicate raw IDs или duplicate mapped IDs в одном наборе — typed `ProofObligationsInvalid`; никакого объединения записей. Если две разные raw строки столкнутся по SHA-256, этот набор отвергается. SHA-256 остаётся частью TCB/предположений identity scheme, как остальные E05 digests.

Transcript `records.obligationId` и proof vector `obligationId` получают `wireId`, сортируются ordinal по **wireId**, сохраняют raw obligation `kind`, общий evidenceDigest и `diagnosticCode=verified`. Kind по-прежнему обязан удовлетворять E05 ID grammar; неизвестный kind не переименовывается. Raw ID восстанавливается deterministic re-lowering held module/bundle, а существующий source map сохраняет locus/symbol/line и не содержит literal obligation ID. Связь raw→wire восстанавливается формулой из regenerated obligations и может показываться в human projection; сохранённому mapping пакета доверять нельзя. Новые поля JSON не добавляются.

Frozen vectors: PowerShell/.NET SHA-256 и независимый Python `hashlib` дали одинаковые 4/4 значения при exact strict UTF-8 tag+LF preimage.

| Exact raw ID | Wire ID |
| --- | --- |
| `candidate-postcondition-addOne` | `7776a82e85daee1a37321b77e98935487d3f03be8617fe9787be0528521c8f99` |
| `range-addOne-sum` | `a0539ccdd1ce04a5b6fb2992470ab29c170712a055ddd5e110eda03891fe75f2` |
| `candidate-postcondition-addone` | `124a7d37b9fa14dbb51f93bde4c4cbcffa6299bef9442174c549244537db44fa` |
| `candidate-postcondition` | `811905e8890e80ecf094b4c174f34bc1e2eb1bcfe4c4fa656ecfcdd1c2b10dfe` |

Это открыто заменяет raw-ID/ordering clause replay §3. Lowerer output/source map не меняются. Старые transcript/proof не мигрируются или переинтерпретируются: требуется новый check/build со свежим transcript; mismatch отвергается. До production admission ни один пакет не получает новый release автоматически.

Performance: один SHA-256 на obligation, максимум4096; G06 выводов нет. UI/video неприменимы: library/proof artifacts.

### 6.3 User-Observable Scenarios

| Сценарий | Ожидаемый результат | AC |
| --- | --- | --- |
| Scalar addOne и allocation fixture | Wire-valid IDs, два byte-equal replay, held package comparison проходит | AC1/3 |
| addOne и addone либо длинные raw IDs | Различные deterministic wire IDs; raw source mapping сохранён | AC2 |
| Самосогласованный forged vector/transcript/manifest | Stored identity может сходиться; fresh replay отказывает до release prompt | AC3 |

### 6.4 State matrix

Old transcript → mismatch/refusal; fresh normalized artifact → Checked fixture; failure → no release. Переход `Admitted` отсутствует в этой поправке. Host state freshness/ACL остаются отдельными обязательными gates базовой SPEC.

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Новое normative mapping | Kibnet | Domain-separated SHA-256 exact raw ID, §6.2 | 0.95 | Меняются proof identities без утверждения владельца | Да, подтвердить выбранное mapping этой поправки |
| Не расширять E05 grammar | agent, в рамках proposal | Сохранить lowercase/bound64; alternative widening отклонено по scope | 0.95 | Более широкий security wire и нерешённая длина | Нет отдельного выбора; входит в approval mapping |
| Реализация hashing/encoding/duplicate guards | agent после approval | Strict UTF-8, no normalization, fail-closed unique raw/wire sets | 0.95 | Неправильный preimage или скрытое объединение obligations | Нет, implementation choice после approval |
| D02 смысл и release admission | Kibnet | Отдельные последующие human gates | 1.0 | Ложное объявление исполнения допустимым | Не относятся к EXEC этой поправки; остаются обязательными позднее |

Альтернатива расширения E05 grammar затрагивает больше artifacts и не решает bound64. До решения владельца о выбранном mapping зависимые code changes запрещены.

### 6.6 Runtime / data contracts

E05 field/schema grammar неизменны; values/digests IDs меняются явно. Snapshot/owner/key/source provenance прежние; host factory не становится candidate capability. Invalid raw/kind/collision — отказ до proof receipt. General language identifier grammar не сужается.

## 7. Acceptance-to-Test Matrix

| AC | Проверка | Evidence |
| --- | --- | --- |
| AC1 | Independent fixed vectors exact preimage/raw-ID→wire-ID; mapped grammar и ordering | два независимых SHA-256 вычисления, literals conformance |
| AC2 | Case-distinct, >64 raw ID, invalid UTF-16, duplicate raw IDs, invalid kind; отдельно mapped collision guard | Первые пять — executable identity/refusal probes. Guard — structural code review, подтверждающий fail-closed duplicate mapped values; producer всегда вычисляет SHA-256 сам. Реального collision fixture нет, подмена hash в producer запрещена и не выдаётся за proof |
| AC3 | Две real proof-positive shapes, signed disposable fixture packages, forged metadata и owner refusal до tool factory | composed replay reports; source/map/records preserved |
| AC4 | Locked build и базовые suites, review, KB update | команды и review journal |

Команды: pinned `dotnet restore Kernel.slnx --locked-mode`, release build; Admission, Modules, Graph, Kernel, Notation, Portability, Experiments suites. Исторические reports не перезаписывать; generated Graph output восстановить после проверки.

## 8. Риски / rollback

Mapping скрывает читаемый ID в wire, поэтому human projection/source map должны сохранять raw связь. Не считать hash схемой доказательства; proof остаётся whole-source Dafny в прежней TCB. Rollback до admission: не включать новый producer, оставить artifacts недопущенными; approvals/state не менять. Обязательные AC нельзя заменить accepted risk.

## 9. Expected User Review Objections

«Зачем hash вместо простого lowercase?» — lowercase смешивает разные case-sensitive entities. «Почему не расширить E05?» — это меняет общий security wire и требует решения upper bound. «Тесты доказывают корректность языка?» — нет, проверяется compatibility и fresh package proof для ограниченных fixtures. «Это принятие моего owner contract?» — нет, disposable signatures только conformance evidence.

## 10. Role-Based Review Result

| Role | Applicability | Verdict / evidence | Required changes |
| --- | --- | --- | --- |
| Business analyst | Контрактные identities, D02 смысл не меняется | PASS §1/5/9, approval не заменяет смысловое решение | Нет для SPEC |
| UX/designer | UI/projection не реализуются | Не применимо; raw связь описана для будущей projection | Нет |
| Tester | AC1–4, negative package scenarios | PASS для плана §7; independent frozen vectors 4/4, collision evidence ограничено явно | EXEC probes после approval |
| Developer/architect | wire mapping/order/source regeneration | PASS §6: unchanged source map и re-lowering, old artifact refusal | EXEC implementation после approval |
| Delivery/security | trust boundary/digest/collision/approval | PASS SPEC §5/6/8; никаких новых admission полномочий | Separate host/runtime gates остаются |

Independent reviewer имел actual sandbox danger-full-access; review поведенчески read-only, техническое ограничение записи не подтверждено. Автор дополнительно проверил контракт и adversarial boundaries.

## 11. План / review

SPEC review → exact approval → поправить normalizer и replay clause → восстановить сохранённый composition candidate → AC/review → commit/push и KB/Unlimotion progress. Независимый ранее committed109 checkpoint не переименовывается в full G02.

### SPEC Linter Result / SPEC Rubric Result

Expanded linter: 1 PASS — исходный сценарий §1; 2 PASS — AS-IS §2; 3 PASS — один контрактный конфликт §3; 4 PASS — сохранение wire/source identity §4; 5 PASS — Non-Goals §5; 6 PASS — ответственности §6.1; 7 PASS — normalizer/regenerator/stored checker §6.1–2; 8 PASS — exact hash/strict encoding/order/uniqueness §6.2; 9 PASS — typed refusals §6.2/6.6; 10 PASS — ≤4096 hashes и отсутствие G06 claims §6.2; 11 PASS — неизменная schema, изменяемые ID values §6.6; 12 PASS — old artifacts требуют fresh check §6.2; 13 PASS — pre-admission rollback §8; 14 PASS — AC1–4 §7; 15 PASS — vectors, boundaries, collision guard, composition refusal §7; 16 PASS — pinned commands/stop §7/11; 17 PASS — dependencies §11; 18 PASS — owner normative choice §6.5; 19 PASS — expanded security identity correction §0; 20 PASS — G02/G04 .NET profile и QUEST §0. Нет FAIL A/C/D; review pending не заменяет approval.

Rubric: цель/границы5 (§1/5), AS-IS5 (реальный отказ и нормативные источники), дизайн5 (exact preimage/order/refusals), безопасность/rollback5 (wire сохранён, old artifact refusal, no admission), проверяемость5 (independent vectors + обе shapes/negatives), автономность2 (нужно owner mapping decision) =27/30. Число не разрешает EXEC.

### Post-SPEC review

Статус: **PASS → ASK-HUMAN** после independent targeted re-review; требуется точное решение владельца о выбранном mapping. Scope/evidence: E05 ID grammar, replay raw clause, actual scalar proof/refusal, lowerer/source map, exact vectors и candidate archive. Contract pass: grammar/schema/TCB остаются, values/order меняются явно. Adversarial pass: case folding/truncation исключены, invalid encoding/duplicates отказ, collision test не подменяет SHA assumption. Role pass: таблица §10. Fix/re-review: исправлены Decision Ledger, linter/rubric, role verdicts, stale pending vectors и AC2 evidence distinction.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| BLOCKER | approved identity contracts | Raw mixed-case lowerer ID не соответствует E05 | Reviewed exact erratum + owner decision | SPEC resolution подготовлена; owner pending |
| MEDIUM | expanded approval gate | Прежняя редакция не содержала полного ledger/linter/role verdicts | Дополнить доказуемыми решениями и evidence | Исправлено; targeted independent re-review PASS |
| MEDIUM | collision evidence | Guard test нельзя выдавать за реальную SHA collision | Отделить structural guard review от executable negative probes | Исправлено §7 |

Depth checklist: scope только obligation identities; AC/tests §7; objections §9; unsupported claims/TCB §5/8; regression/compatibility §6.2; docs/KB K-G02-017; hidden behavior change явно §6.2; manual challenge — wire sorting отличается от raw sorting, source map не хранит literal raw IDs. No-findings justification: independent targeted re-review подтвердил отсутствие оставшихся BLOCKER/HIGH/MEDIUM в SPEC scope, 4/4 Python vectors совпали, контракт и границы evidence определены. Это не выполнение EXEC. Post-EXEC не выполнялся; полная цель не достигнута.

## 12. Журнал действий агента

| Фаза | Evidence / решение | Остаток / следующий шаг | Решение человека |
| --- | --- | --- | --- |
| EXEC counterexample → SPEC | K-G02-017: real scalar composition отказал из-за raw uppercase IDs; candidate сохранён | Independent review mapping, затем exact approval | Mapping ещё не утверждён |
| SPEC review/fix | Python 4/4 vectors совпали; expanded audit и collision evidence уточнены; independent targeted re-review PASS | ASK-HUMAN: подтвердить выбранное mapping, затем EXEC | Ожидается |
