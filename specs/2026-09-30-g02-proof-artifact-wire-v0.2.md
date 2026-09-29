# G02: точный wire-контракт digest сохранённых proof artifacts

## 0. Метаданные

- Статус: проект дополнения к утверждённой [G02 SPEC](2026-09-29-g02-dotnet-r2r-admitted-modules-v0.1.md), **ожидает отдельного точного подтверждения владельца** перед использованием в `check/build/admit/Load`. Уже написанный `G02StoredProofArtifacts` является fixture-only candidate и не разрешает admission.
- Тип: `delivery-task` / QUEST / expanded security and public artifact contract; owner смысла и допуска — Kibnet. Цель G02, задача Unlimotion `c390006e-dbe7-4823-868e-9e662ff59e61`.
- Instruction stack: central `AGENTS.md`, `creator-vibe-lens`, `model-behavior-baseline`, `tool-execution-baseline`, `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `product-system-design`, `spec-linter`, `spec-rubric`, `review-loops`, repo `AGENTS.md`.
- Effective runtime: Codex desktop; model/reasoning не участвуют в digest. Backend target win-x64, pinned .NET SDK 10.0.400 и Dafny 4.11.0 согласно G02 SPEC. Branch `main`; без production admitted packages текущего формата.
- Baseline: [E05 amendment](2026-09-07-e05-two-stage-admission-amendment.md) задаёт поля `proofSourcesDigest`, `transcriptDigest`, `sourceMapDigest`, но не exact domain tags и preimage; [E06 validation-only](../src/Strogo.Modules.Portability/PortabilityValidationProof.cs) использует другую версию и не является нормативным G02 форматом. Текущий fixture-only код и четыре negative probes — `G02StoredProofArtifacts` / Admission conformance.

## 1. Цель и наблюдаемый результат

Независимые producer, verifier replay и loader должны получать **одни и те же digest** из одинакового набора proof sources, transcript и source map. Владелец видит перед вторым решением один воспроизводимый proof/package digest и отказы при любом несовпадении сохранённых файлов. Результат этой SPEC — нормативный расчёт трёх полей E05, допустимые bytes proof sources и закрытый `diagnosticCode` для `Verified`; язык и список полей не расширяются. Exact approval охватывает **все** правила §4, включая encoding и diagnostic mapping.

Stop: самосогласованная metadata не является proof; `admit` запрещён до свежего verifier replay и отдельного human release decision. До exact approval этого дополнения нельзя подключать кандидатный расчёт к production `check/build/admit/Load` или объявлять G02 AC3.

## 2. AS-IS и проблема

Утверждённая G02 SPEC нормативно включила E05 schema, но три digest упомянуты по имени без domain tag и preimage. Два добросовестных producer могут выбрать разные канонические inventory или разные теги для одинаковых файлов. Текущий `G02StoredIdentityVerifier` уже проверяет module/bundle/approval; кандидатный `G02StoredProofArtifacts` пересчитывает три digest на disposable fixture. Без wire-фиксации это нельзя считать переносимым identity contract или основой human admission.

## 3. Цели, Non-Goals и ответственности

Цель — один алгоритм и позитивный fixed vector для каждого поля, fail-closed на другую сериализацию/тег, noncanonical source bytes или diagnostic code и сохранение E05 schema. G02 producer пишет canonical bytes; trusted package verifier читает held handles; независимый replay вычисляет те же значения без вызова producer API. Human утверждает смысл owner bundle и позже exact package, но не вручную вычисляет hashes.

Не меняем E05 opcodes, owner bundle, approval/admission schema, R2R ABI, E06 validation-only теги. Не утверждаем корректность Dafny, не нормализуем произвольный solver prose и не считаем наличие `Verified` полей доказательством. Source map semantics и direct generated symbol/assembly binding остаются отдельными gate утверждённой G02 SPEC.

## 4. Предлагаемый wire-контракт

`H(tag, bytes) = lowercase hex SHA256(UTF8(tag + "\n") || bytes)`. Все paths/length/SHA-256 происходят из уже проверенного closed `build-manifest.json`; verifier дополнительно сравнивает каждый listed file с held bytes. Входы не берутся из свободного пути и не выбирают собственный алгоритм через metadata.

| Поле | Exact preimage | Domain tag |
| --- | --- | --- |
| `proofSourcesDigest` | UTF-8 canonical JSON array из **всех** `files` с role `proof-source`, отсортированных ordinal по `path`; каждый объект имеет ровно `length` (JSON integer), `path`, `role`, `sha256` и записывается canonical JSON с ordinal-сортировкой имён полей | `strogo.proof.v0.2/proof-sources` |
| `transcriptDigest` | exact canonical UTF-8 bytes `proof-transcript` файла с E05 schema | `strogo.proof.v0.2/transcript` |
| `sourceMapDigest` | exact canonical UTF-8 JSON bytes `source-map` файла | `strogo.proof.v0.2/source-map` |

Proof-source bytes непустые, strict UTF-8 без BOM, CR и NUL; они покрыты своим SHA-256 в manifest, а inventory digest связывает paths, lengths и hashes. Source-map на этом этапе обязан быть canonical JSON, но его закрытая семантическая схема определяется G02 source-map/ABI этапом; успешный digest check не удостоверяет символы.

Transcript имеет ровно E05 поля `schemaVersion`, `toolchainDigest`, `proofSourcesDigest`, `outcome`, `records`. Для packaged `Verified` результата каждое record имеет ровно `obligationId`, `kind`, `status`, `diagnosticCode`, `evidenceDigest`; упорядоченный vector без `diagnosticCode` byte-for-byte по значениям совпадает с `proof.json` obligations, а единственный допустимый `diagnosticCode` — `verified`. Неуспешные prover outcomes сохраняются вне admitted package и получат собственный закрытый mapping в `check`; эта SPEC не присваивает им право build.

Frozen vector для одного source `content/candidate.dfy`, bytes UTF-8 `method M() {}\n` (14 bytes), source SHA-256 `4d8a7223f593b92627928f46fb9bcf151a727f6a54c94e971813ec53f99b7612`: canonical inventory bytes — `[{"length":14,"path":"content/candidate.dfy","role":"proof-source","sha256":"4d8a7223f593b92627928f46fb9bcf151a727f6a54c94e971813ec53f99b7612"}]`. Результат `proofSourcesDigest` — `9fe0504c0d0a1224314d5320fb631110dd4b52412e78869c6dc94d0a6edc16ee`. Для canonical source map `{"entries":[]}` результат `sourceMapDigest` — `9636615613cc4707412b5541f6d321630b31ef96ea05d4c721aca264198bc87e`. Для transcript с `toolchainDigest = e×64`, `evidenceDigest = 4×64`, одним record `entry.1/postcondition/Verified/verified` и указанным `proofSourcesDigest`, canonical JSON с ordinal-sorted object keys даёт `transcriptDigest = f32a13a6389d316e75630a1a7dfde7f7e4f2623e2adc86f03ead65149fd0b895`. Независимый расчёт: Python `hashlib.sha256((tag+'\n').encode()+canonical_bytes).hexdigest()` и `json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()`; константы сверяются в Admission conformance.

Отказ до human projection при noncanonical/extra fields, другом digest, несоответствии vector, неизвестном diagnostic code. При совместных ошибках owner trust/state/approval проверяются до digest chain согласно E05 error order. Native package file hashes уже проверяются `G02PackageSnapshot`, но это не заменяет fresh prover replay.

## 5. Сценарии и решения

| Scenario | Trigger | Видимый результат | Evidence | AC |
| --- | --- | --- | --- | --- |
| W1 | Producer и отдельный verifier читают те же held files | Три digest совпадают с fixed vectors | independent test implementation и hashes | AC1 |
| W2 | Изменён proof source, transcript или source map вместе с manifest | Typed refusal до human projection | tamper probes | AC2 |
| W3 | Transcript и proof пересчитаны вместе, но vector или code изменён | Typed refusal; `admit` не вызывается | negative probes / no prompt | AC3 |

State matrix: `BuiltNotAdmitted` + успешный stored binding остаётся `BuiltNotAdmitted`; после fresh replay и отдельного human decision возможен `Admitted`. Любой отказ оставляет состояние и внешний admission неизменными. Пустой sources list и лишняя роль — отказ manifest parser; concurrency закрывается held handles и новой инвентаризацией при admission/load.

| Decision | Owner | Выбор | Confidence | Risk | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Теги/preimage и Verified code | Human approval этой SPEC | Таблица §4 | 0.85 | Wire несовместимость с независимым producer | Да для production integration |
| E06 validation-only migration | Agent | Не переносить и не переименовывать E06 artifacts | 0.95 | Ложное approval validation fixture | Нет |
| Source-map semantic schema | Отдельный G02 этап | Не объявлять по одному digest | 0.95 | Ложная привязка generated symbol | Нет для этого шага |

Runtime/data matrix: source of truth для schema — exact E05 amendment; этот addendum фиксирует три ранее неопределённых вычисления и сопутствующие ограничения encoding/`Verified` diagnostic. Старые G02 disposable fixtures можно пересоздать, production migration отсутствует. При откате до human admission удалить кандидатный verifier из pipeline; existing owner state и approvals не менять. Текущие `proof.json`/manifest с другими вычислениями не мигрируются молча, получают typed mismatch.

## 6. Критерии и проверка

| AC | Проверка | Evidence | Если не выполнено |
| --- | --- | --- | --- |
| AC1 | Два независимых вычисления каждой формулы на frozen canonical fixture и минимум одном source | bytes + expected SHA-256 в conformance / report | блокирует production integration |
| AC2 | Изменить каждый held artifact, пересчитать manifest, сохранить прежний proof | `proofSourcesDigest` / `transcriptDigest` / `sourceMapDigest` mismatch | блокирует `admit` |
| AC3 | Пересчитать transcript+proof+manifest с иным vector/code; отдельно CR/BOM/invalid UTF-8 source с пересчитанными digest | vector/code/source-encoding refusal, отсутствие human prompt/admission output | блокирует `admit` |
| AC4 | Fresh prover replay byte-equal transcript и obligations после stored check | `check/build/admit` integration report | блокирует G02 AC3; не закрывается этой SPEC отдельно |
| AC5 | Сохранить E06 validation-only результаты и весь relevant suite | locked build; Modules, Graph, Kernel, Notation, Portability, Experiments, Admission | блокирует delivery checkpoint |

Команды: закреплённый `.tools/dotnet-sdk-10.0.400/dotnet.exe` в `PATH`, `dotnet restore Kernel.slnx --locked-mode`, `dotnet build Kernel.slnx --no-restore`, `dotnet run --project tests/Strogo.Modules.Admission.Conformance -c Release --no-restore` и full repo conformance по G02 SPEC. UI/visual/video неприменимы: CLI/library artifact. G05/G06 performance здесь не измеряется; ограничение inventory 1024 files и JSON artifact 1 MiB уже действует в parser.

## 7. Риски, ожидаемые возражения и план

| Возражение | Ответ | Статус |
| --- | --- | --- |
| «Самосогласованный поддельный proof всё ещё проходит» | Да; fresh verifier replay обязателен до human projection | открыто в G02, не объявлять admission |
| «Почему не теги E06?» | E06 имеет `validation-only` schema v0.1; смешение даст ложную идентичность | устранено разделением доменов |
| «Source map может указывать на чужой метод» | Digest проверяет bytes, но semantic source-map/assembly binding — отдельный G02 gate | открыто до runtime |
| «Может ли порядок JSON поменять digest?» | Canonical writer сортирует object fields ordinal; массив source entries сортируется path ordinal | фиксируется AC1 |

План после approval: добавить независимые fixed vectors и production builder/checker на §4, затем fresh prover replay; только после этого подключать вторую human projection и runtime. До approval разрешены review этого текста и fixture-only validation в пределах уже утверждённой G02 SPEC. Rollback — откатить новую production integration без изменения owner state и release admissions, которых пока нет.

## 8. Quality gate и журнал

- SPEC linter (каждый критерий):

| № | Статус | Evidence |
| --- | --- | --- |
| 1 | PASS | §1 independent digest outcome |
| 2 | PASS | §2 exact E05 gap и кандидатный код |
| 3 | PASS | §2 разные producer → разные identities |
| 4 | PASS | §3 цели canonical wire/fail-closed |
| 5 | PASS | §3 no replay/G06 claim |
| 6 | PASS | §3 producer/verifier/human owners |
| 7 | PASS | §§1, 4 `check/build/admit/Load` gate |
| 8 | PASS | §4 tags, preimage, source encoding, vector |
| 9 | PASS | §4 refusal и E05 error order |
| 10 | PASS | §6 существующие file/artifact bounds; G06 отдельно |
| 11 | PASS | §4 exact bytes/fields |
| 12 | PASS | §5 E06 разделён, production migration нет |
| 13 | PASS | §5 откат до admission без owner-state mutations |
| 14 | PASS | §6 AC1–5 |
| 15 | PASS | §6 AC→positive/negative/evidence |
| 16 | PASS | §6 commands и §1 stop |
| 17 | PASS | §7 approval→vectors→replay→admission |
| 18 | PASS | §5 decision ledger и exact human gate |
| 19 | PASS | §0 expanded security/public contract |
| 20 | PASS | §8 security/architect/tester/delivery review |
- SPEC rubric: цель 5, AS-IS 5, дизайн 5, безопасность/rollback 5, проверяемость 5, автономность 2 (нужен exact owner approval): 27/30.
- Scope reviewed: G02 approved SPEC, exact E05 amendment, E06 validation-only digest code, candidate G02 code/tests, repo AGENTS и central QUEST/review owners. Adversarial counterexample: два producer с разными tags/preimage выпускают разные proof identities для одних files; эта таблица закрывает неоднозначность только после owner approval. Role-based: tester — AC1–5; architect — canonical wire; security/delivery — no admission before replay; UX/UI неприменимы.
- Post-SPEC independent review: первоначальный MEDIUM о scope устранён явным включением source encoding и Verified diagnostic в §1/§3/§4; последующее противоречие в runtime/data matrix исправлено. Reviewer поведенчески read-only при фактической sandbox `danger-full-access` независимо пересчитал Python `hashlib/json` vector §4 и не нашёл новых BLOCKER/HIGH/MEDIUM в exact wire scope. Scope/evidence: эта SPEC, approved G02/E05, candidate code/tests, frozen vector; contract pass — E05 поля сохранены; adversarial pass — иной tag, CR/BOM, изменённый vector/code и поддельный proof; role pass — tester, architect/security/delivery, UI неприменимо; fix/re-review — §1/§3/§5 и vector. Depth checklist: авторизация — pending, AC1–5 — §6, validation — fixture-only, регрессии — §6, unsupported claims — §1/§3, rollback — §5, docs/knowledge — K-G02-009. No-findings justification ограничен этим post-SPEC wire contract; весь G02 EXEC не прошёл review.
- Open finding: первоначальный reviewer отметил MEDIUM нормативную неопределённость трёх digest; эта SPEC — proposed fix, **не закрывает production finding до approval и independent fixed-vector evidence в conformance**. `No-findings` для G02 EXEC отсутствует.
- Stop decision: `ASK-HUMAN` после post-SPEC `PASS`; не выдавать текущий текст за утверждённый контракт. Связанный knowledge ID — K-G02-009.
