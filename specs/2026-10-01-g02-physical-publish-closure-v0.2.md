# G02: identity физического publish closure v0.2

## 0. Метаданные
- Профиль delivery-task / QUEST / product-system-design; large backend/trust scope; владелец Kibnet; Codex desktop Windows, model/reasoning не засвидетельствованы. Не LLM benchmark.
- Canonical template central templates/specs/_template.md. Central QUEST/review + local AGENTS. Branch main, baseline ec4d06c; G02 c390006e-dbe7-4823-868e-9e662ff59e61, goal3295d388-0d24-45f8-b8be-b0eaf24e2cc5.
- Статус публичной части: SPEC, требуется отдельное точное подтверждение формулы closureDigest; broad goal approval не заменяет этот выявленный нормативный выбор. Независимая private inventory работа разрешена прежним [G02](2026-09-29-g02-dotnet-r2r-admitted-modules-v0.1.md); она не записывает/проверяет public closureDigest.
- Frozen [E05 amendment](2026-09-07-e05-two-stage-admission-amendment.md) не меняется. Non-platform package composition/platform loader source остаются его контрактом.

## 1. Overview / Цель
Поручение: достичь G01–G06, включая proof-bound machine code на зрелой платформе. Сейчас DLL/deps сравниваются после rebuild, а полный физический output не удерживается и поле closureDigest не имеет формулы. Success этой SPEC после approval: определить и проверять physical compiled-output identity в existing proof/manifest field без помещения platform assemblies/PDB в E05 package. Итог: exact identity profile, fresh rebuild verification, evidence. Stop: до exact approval не менять public-field semantics; D02/release human gates остаются отдельными.

## 2. AS-IS
Actual scalar publish190 files =80,618,621 bytes (~80.6 MB/76.9 MiB), flat directory. E05 package содержит generated assembly/deps и необходимые non-platform runtime dependencies; platform assemblies loader берёт из pinned runtime/toolchain. Текущий generated C# включает Dafny runtime в assembly, fixed SDK project не объявляет сторонних PackageReference. Proof/manifest имеют closureDigest, но frozen E05 не определяет его domain/preimage. Fixtures используют ffff placeholder; никакого admission нет.

## 3. Проблема
Неопределённая normative identity физического compiled closure и возможность принять самосогласованные metadata без её fresh reproduction.

## 4. Цели дизайна
Отделить compiler physical output от package/load roles. Полностью учитывать physical output, избежать circular digest, сохранить frozen E05 role/loader meaning. Strict target win-x64 pinned recipe; future targets отдельными profiles.

## 5. Non-Goals
Не превращать platform/PDB/support files в runtime-dependency. Не добавлять public loader/ABI/admit, host ACL/state freshness, concurrent snapshot lifecycle, OS network/disk-memory isolation, actual native method/G05/G06 evidence. Compiler/runtime остаются TCB.

## 6. TO-BE
### 6.1 Ответственность
Private G02PublishInventory и SDK builder — held full physical output/hash records; public composed checker после approval — сравнение fresh physical identity с manifest/proof; package verifier сохраняет original E05 roles.

### 6.2 Дизайн и exact proposal
- Полный inventory всех190 actual publish files остаётся в private research/build directory. Только entry DLL/deps и разрешённые non-platform dependencies копируются в E05 package; platform/native/PDB/auxiliary не получают package runtime-dependency role. Inventory diagnostic receipt не становится package metadata.
- Profile flat-only, no subdirectories/reparse/ADS. Canonical names — lowercase ASCII publisher basename, ValidatePath-compatible, case-fold collisions запрещены. All regular output files включаются. Count<=1024, every file<=64MiB для bounded reads/hashes; это post-write read bounds, не quotas.
- Entry/deps identified exactly as strogo.generated.dll/strogo.generated.deps.json; отсутствие отказывает. Package singleton entry/deps сравниваются bytes как раньше. No platform name heuristic classifies non-platform deps: current recipe fixed compile-only Generated.cs with included runtime/no external packages. Для этого exact profile allowed package runtime-dependency set = []; future public checker отвергает любой такой manifest role. Supporting future non-platform dependency producer требует separate exact recipe dependency provenance, не исключения неизвестных DLL.
- Private approved-work identity: H("strogo.build.v0.2/publish-inventory", C({schemaVersion:"strogo.publish-inventory.v0.2",runtimeIdentifier:"win-x64",files:[{name,sha256,length}...]})). files ordinal sorted by canonical name; no duplicates; lower64hex hash, nonnegative length. Это только diagnostic digest.
- **Предлагаемый public closureDigest после owner approval**: H("strogo.build.v0.2/publish-closure", C({schemaVersion:"strogo.publish-closure.v0.2",runtimeIdentifier:"win-x64",files:<same full physical inventory>})). H — SHA256(UTF8(domain+LF)||canonical bytes), C — existing OwnerAdmissionWire canonical JSON. Payload не включает SDK digest/package/proof/source metadata/closureDigest. Нет циклов: compiled output→closureDigest→proofDigest→packageDigest.
- public closureDigest означает identity compiler publish output, **не доказательство identity реально загруженного platform runtime**. E05 platform runtime остаётся pin/verify через toolchainDigest и host runtime closure. Public checker обязан fresh-build physical digest compare, existing entry/deps compare и separately exact allowed non-platform package dependency check до load. Не использовать private output как альтернативный platform load root без approved host/runtime design.
- Coherent declared closureDigest substitution с пересчитанными proof/manifest/package identities должно пройти stored consistency, но отказать fresh comparison CompiledClosureReplayMismatch. Incorrectly populated package platform/support role отвергается, не исправляется автоматически.
- UI/video не применимы: backend identity. Guaranteed read bounds не являются pre-write resource budgets.

### Exact frozen vector
Фиктивные hashes ниже — algorithm vector, не реальные runtime files. Canonical files:
```json
[{"length":1,"name":"a.dll","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},{"length":2,"name":"strogo.generated.deps.json","sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"},{"length":3,"name":"strogo.generated.dll","sha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"}]
```
Exact public canonical bytes (UTF8, no BOM/trailing LF):
```json
{"files":[{"length":1,"name":"a.dll","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},{"length":2,"name":"strogo.generated.deps.json","sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"},{"length":3,"name":"strogo.generated.dll","sha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"}],"runtimeIdentifier":"win-x64","schemaVersion":"strogo.publish-closure.v0.2"}
```
Expected public digest: `1dd76a7bb1bea1ee51f09cd62f0c25ea3645b76e8a6dceeb424e4386f5f8651f`.
Private vector заменяет только schemaVersion на strogo.publish-inventory.v0.2 и domain на strogo.build.v0.2/publish-inventory; expected `5b735a4c391f299989df69fc594b2b3e61415e69208f9774f6991f3d724a9e0e`. Независимо вычислено PowerShell SHA256 вручную собранного exact string, вне production canonicalizer.

### 6.3 User-observable scenarios
|Scenario|Trigger|Result|Evidence|AC|
|---|---|---|---|---|
|Physical identity|fresh approved package check|all published files identity bound, no role reinterpretation|full raw inventory/rebuild/refusals|1–4|
### 6.4 State
SPEC→exact owner approval→public EXEC. Existing private EXEC не становится public approval. Candidate never selects host/pin/hash formula. Caller lifetime/state/now trusted until production host gates implemented.
### 6.5 Decisions
|Decision|Owner|Chosen|Confidence|Risk|Needs user before public EXEC|
|---|---|---|---|---|---|
|Frozen roles|existing approved owner|preserve non-platform package only|high|no loader drift|Нет|
|Public closure identity|user|all physical publish files, domain/schema above|high|owner approves inclusion platform/PDB, historical identity changes|Да|
|Private physical diagnostic|agent|full held output, separate digest|high|no public accept change|Нет, existing G02 mechanism approval|
### 6.6 Contracts
|Area|Source|Change|Migration|Verification|
|---|---|---|---|---|
|proof/manifest|frozen E05|define existing closureDigest after approval|new profiles/packages; historic evidence preserved|fresh digest vs declared|
|package roles/platform load|frozen E05|none|none|no platform files moved into content|
|private physical output|pinned recipe|all held file identities|new raw directories|fixed vector+two fresh output trees|

## 7. Алгоритм
Owner/stored gates→source/map regeneration→two proof replays→verified translation→held SDK build→full physical digest→entry/deps and public digest checks→future production host/runtime gates. При producer после approval fresh digest задаётся до proof/manifest, не из candidate JSON.
## 8. Интеграция
Private diagnostic stays internal. Dependent public check/write only after this exact SPEC approval. Existing partial proof checker/old fixtures не выдаются за admitted artifacts.
## 9. Данные
Public fields unchanged, but meaning is a substantive identity decision; not silently compatible. Private receipt extends output inventory/digest.
## 10. Rollout
Fresh new packages/proofs required; old placeholder fixtures fail new full checker. Old reports unmodified. Git rollback affects only internal code, no actual admissions.

## 11. AC
1. all190 physical files held-hashed for both shapes; flat/layout/ADS/collision/size refusals and exact vector.
2. public formula/vector frozen, exact human approval recorded before public writes/checks.
3. actual composed valid package passes new physical identity; coherent wrong declared identity refuses with fresh evidence. E05 non-platform/host platform semantics unchanged.
4. existing entry/deps/owner negatives preserved, final build/test stdout, honest no production/native/G02 completion claim.
5. independent reviews, same-block KB, commit/push and Unlimotion intermediate result/read-back.
### Acceptance-to-Test
|AC|Test|Evidence|
|---|---|---|
|1|full inventory/two fresh source→translation→build and layout/fixed-vector controls|raw+private report|
|2|SPEC gate/frozen vector|approval journal|
|3|fresh digest vs declared coherent forged identity|raw packages/suite|
|4–5|Admission+build/review/delivery|stdout/report/Git/CLI/KB|

## 12. Risks / objections
|Objection|Mitigation|Status|
|---|---|---|
|platform files violate E05?|remain outside package, no role change|mitigated|
|Does physical digest verify loaded runtime?|no, host platform pin gate remains mandatory|mitigated|
|Are old packages compatible?|new identity requires fresh proof/package, no evidence rewrite|mitigated|
|Why new owner approval?|normative domain/preimage was undefined; inclusion platform/PDB affects what owner signs|ask-human before public EXEC|
## 13. План
Private independent approved inventory work → review this exact proposal → owner final approval gate → public field implementation/replay controls → delivery/evidence. Full G02 remains broader.
## 14. Открытое owner решение
Подтвердить proposed public closureDigest as physical publisher identity включая platform/PDB, при сохранении платформенного runtime вне E05 package и отдельной проверке pinned host. Это не подтверждение D02 смысла/release.
## 15. Профиль
Fail-closed identity/TCB/role boundaries и reproducible artifacts. Не human-intent equivalence, не benchmark.
## 16. Files
Builder/inventory/tests private work in approved base; composed public check/proposal fixtures только после approval; spec/log/evidence/KB authoritative phase-separated records.
## 17. Было→стало
Undefined public digest→explicit reviewed owner proposal; private publisher inventory full вместо two-entry-only diagnostics. Full admission remains open.
## 18. Альтернативы
A recommended: physical full publish output identity, no package role/load change. B: closureDigest only logical non-platform deps+platform runtime descriptor, separate compiler output digest — requires exact host platform descriptor/pin format, more policy surfaces. Embedding platform/support under runtime-dependency rejected as conflict, не вариант для silent EXEC.

## 19. Review
- Initial post-SPEC NEEDS-FIX/ASK-HUMAN: HIGH role reinterpretation, MEDIUM missing frozen vector, LOW unit. Fixed: outside-package full inventory, public normative gate explicit, independent exact vector, MB/MiB corrected.
- Re-review: public ASK-HUMAN; private inventory scope разрешён existing approval. Clarities исправлены: full inventory private, entry/deps packaged; exact non-platform set=[] для recipe. Independent reviewer пересчитал оба frozen vectors. Linter/rubric/self scores не закрывают owner gate. Applicable roles architecture/security/data identity/workflow; UX N/A backend.
- Post-EXEC public: not performed; independent private implementation review belongs approved base journal.

### Phase-scoped quality / role review
| Linter block | Verdict | Evidence / gate |
|---|---|---|
|A completeness|PASS|exact profile/preimage/vector/scenarios/AC/journal|
|B design|PASS|physical inventory distinct from E05 package/load roles|
|C change safety|PASS|no public write/check before owner gate; historic receipts retained|
|D testability|PASS|independent frozen vector, private190PASS/raw1140 hashes; public controls after approval|
|E autonomous readiness|ASK-HUMAN public / PASS private|new normative identity decision requires exact approval|
|F profile|PASS|fail-closed trust/backend and explicit TCB|
Rubric public design27/30: clarity/as-is/design/safety/testability5 each, autonomous readiness2 pending owner gate; score does not replace approval.

|Role|Applicability|Question|Verdict|Disposition|
|---|---|---|---|---|
|Architecture|applicable|physical inventory vs loader semantics?|PASS|frozen E05 preserved; allpublisher role variant rejected|
|Security/data identity|applicable|what bytes does owner sign?|ASK-HUMAN|exact new public formula/vector before dependent EXEC|
|Workflow/business|applicable|does partial work replace G01–G06?|PASS|base goal/admission/TCB requirements remain open|
|Validation|applicable|can vectors/raw reproduce claims?|PASS private|reviewer recalculated bothvectors and1140 raw identities; public replay controls future|
|Delivery|applicable|are status/publication boundaries honest?|PASS private|private milestone authorised; public proposal remains pending|
|UX/designer|not applicable|internal identity, no UI changes|N/A|no visual artifact claim|

Post-SPEC stop decision: fixes applied for original role conflict, bytes vector, units and package-copy clarity. Independent advisory review allowed private existing-base EXEC. Public proposal ASK-HUMAN solely normative identity choice; no code uses proposed public digest. Post-EXEC private evidence190PASS and source/evidence re-review confirmed; it does not close public AC2/3 or full G02.
## Approval
Требуется **Спеку подтверждаю** для этой конкретной reviewed physical publisher closureDigest formula. Broad goal continuation alone не подставляется вместо выявленного user-owned normative identity decision. До этого public proof/manifest closureDigest не записывается/проверяется новой формулой.

## 20. Журнал
|Phase|Decision|Evidence/residual|Next|Human decision|Artifacts|
|---|---|---|---|---|---|
|Initial SPEC|allpublish under runtime role|review HIGH frozen E05 conflict; no code changed|redesign|not requested yet|this SPEC|
|Revised SPEC|preserve roles; physical output outside package; exact vector and separate public gate|private mechanism already approved, public decision pending|review proposal/private independent EXEC|no new normative consent|this SPEC|
