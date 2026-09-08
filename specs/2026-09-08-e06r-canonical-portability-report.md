# E06R: канонический portability report и доказуемые статусы

## 0. Метаданные

- Тип (профиль): `product-system-design`; Expanded SPEC, потому что уточняется нормативный evidence/output contract E06.
- Владелец: человек подтверждает смысл отчёта; агент реализует builder/validator и evidence.
- Масштаб: medium.
- Целевое семейство / behavior baseline: Strogo E06 `strogo.portability-report.v0.1`.
- Поверхность: Codex и публичные JSON/Markdown artifacts репозитория.
- Effective runtime: не влияет на canonical bytes; tool/runtime identities поступают только из уже проверенных receipts.
- Eval baseline / evidence: `PortabilityReportMatrix` и .NET checkpoints K-E06-045, K-E06-047, K-E06-050, K-E06-052, K-E06-055; JVM profile может быть представлен только synthetic/negative input до отдельного E06A baseline gate.
- Целевой релиз / ветка: `main`, отдельные Conventional Commits и периодический push по разрешению владельца.
- Ограничения: только validation-only E06 report; production admission, release, G05/G06 verdict и E06A Stage 2 не разрешаются.
- Связанные ссылки: E06 §§6.2.4, 11 A4/A10–A14; K-E06-048–K-E06-050, K-E06-059.

## 1. Overview / Цель

Устранить неоднозначность полной схемы E06 report и реализовать один fail-closed builder/validator, из которого машинная JSON-форма и человеческая Markdown projection получают одинаковые статусы, причины и границы утверждения.

Outcome contract:

- Success means: canonical report связывает каждое `Portable`/`NotPortable` решение с обязательными profile/platform receipts, явно показывает validation-only boundary и получает стабильный `semanticDigest` без diagnostic noise.
- Итоговый артефакт / output: `PortabilityReportV01` builder/validator, canonical JSON, deterministic Markdown projection, conformance positive/mutation cases и tracked exact-revision evidence.
- Stop rules: неизвестный profile/platform/status/reason, несогласованная identity, `Passed` без обязательного receipt, неполный набор profiles/rows, `NotPortable` без причины или попытка выдать report за production admission отклоняются как `PortabilityReportRejected` без partial output. Реальные JVM build/run receipts не создаются до E06A gate.

Цели: G02 получает проверяемую границу multi-platform evidence; G04 получает явную связь verdict с proof/owner identities. G05 и G06 этой поправкой не измеряются.

## 2. Текущее состояние (AS-IS)

- `PortabilityReportMatrix.Complete` закрывает два profile ID, две обязательные OS rows, row status/reasons, synthesized `Unavailable` и равенство manifest/package/artifact identity у passed rows.
- Полного `strogo.portability-report.v0.1` builder/validator нет.
- E06 §6.2.4 перечисляет поля report без `purpose`, `contractStatus` и `admissionStatus`, но затем требует показывать эти границы в semantic projection.
- Profile status зависит от build reproducibility, JIT, consumer и oracle gates, однако перечисленная схема не связывает status с digest проверенного gate receipt.
- Не определено, какие evidence fields допустимо оставить `null` у `Failed`/`Unavailable`, как представить целиком отсутствующий обязательный profile и чем отличается JIT gate summary от исключаемого diagnostic log.
- .NET receipts существуют, но имеют разные исторические exact revisions. Эта поправка не смешивает их в ложный единый фактический report: рабочий builder сначала проверяется synthetic fixtures, затем получает отдельный exact-revision integration run, когда обе profile линии готовы.

## 3. Проблема

Без закрытой схемы агент может присвоить один и тот же видимый статус разным наборам evidence, потерять validation-only boundary или вычислить `semanticDigest` из произвольно выбранных полей. Такой report нельзя независимо проверить и безопасно использовать как основание утверждения о переносимости.

## 4. Цели дизайна

- Вычислять statuses и reasons из evidence, а не принимать их как свободный input.
- Связать каждый обязательный gate с digest валидированного receipt.
- Сохранить частичный отрицательный результат без выдуманных identities.
- Отделить semantic gate summaries от raw diagnostic observations.
- Сделать JSON и Markdown проекциями одного immutable report model.
- Сохранить closed profiles/platforms/reasons и deterministic ordering.

## 5. Non-Goals

- Не строить и не запускать JVM artifact до отдельного E06A baseline approval.
- Не объединять старые receipts разных revisions в итоговый фактический E06 verdict.
- Не создавать production loader/admission API и не менять E05 schema.
- Не объявлять G02, G05 или G06 достигнутыми по одному workload/report.
- Не добавлять Wasm, NativeAOT, новые OS/arch или language property-types.
- Не включать raw stdout/stderr, JIT event log и benchmark samples в semantic projection.

### 5.1 Нормативное supersession основной E06

Эта поправка после отдельного approval заменяет только следующие части E06 §6.2.4 и связанные проверки A4/A10–A14:

| E06 исходная формулировка | E06R replacement |
| --- | --- |
| Root field `schema` без заданного literal | `schemaVersion = "strogo.portability-report.v0.1"` |
| Field list без явных validation/admission boundaries | Root constants `purpose`, `contractStatus`, `admissionStatus` |
| Profile/row status зависит от gates без receipt binding | Typed evidence + canonical gate-summary digests |
| JIT attachments целиком исключены из semantic projection | Raw JIT receipt/log исключены; stable validated `jitGateDigest` включён |
| `outcomeDigest` без byte-level definition | Domain-tagged canonical domain/outcome row set §6.2.4 |
| Неопределённая partial nullability | Closed state/evidence table §6.2.5 |

Все остальные требования E06, включая fixed workload, profiles/platforms, warnings, proof/package/runtime/JIT/consumer gates и границы claims, сохраняются. E06A полностью сохраняется; E06R не подтверждает и не заменяет JVM baseline.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `PortabilityReportV01`: immutable input/output model, builder, validator, canonical JSON и semantic projection.
- `PortabilityReportMatrix`: canonical completion и base validation mandatory platform rows; расширяется только общими closed reason/status helpers при необходимости.
- Conformance harness: positive, partial, ordering, identity и semantic/diagnostic mutation checks.
- Exact evidence driver: передаёт canonical receipt bytes и expected identities одной source revision в profile-specific validators; report builder принимает только их typed validated results.
- `docs/knowledge-log.md` и E06 journal: сохраняют выводы, counterexamples и границы.

### 6.2 Детальный дизайн

#### 6.2.0 Evidence authority

Public builder не принимает свободные `status`, `reasonCodes` или receipt digests. Вход — закрытый `PortabilityReportEvidenceSet` из typed результатов существующих либо profile-specific validators: proof/package/runtime/JIT/consumer/build validators получают canonical receipt bytes и expected identities, повторно проверяют schema/digests/bindings и возвращают immutable result с недоступным извне конструктором. Builder сам считает digest валидированных canonical receipt bytes.

`Unavailable` создаётся только из typed environment result harness либо отсутствующей mandatory row при уже известном profile/toolchain/runtime requirement. Synthetic factories доступны только conformance assembly через `InternalsVisibleTo`; production API не может превратить произвольную строку/digest в Passed result. Это не доказывает истинность внешнего наблюдения само по себе, но исключает self-declared report status вне уже определённых E06 validators.

Evidence set содержит trust anchors `expectedSourceRevision` (ровно один lowercase 40-character clean-commit SHA) и для JVM — typed `ApprovedE06ABaseline` с owner-approved baseline digest, approval-record digest, exact tool/source/command identities и approved state. Каждый receipt обязан нести тот же source revision; profile validator обязан сравнить его с anchor до создания typed result. JVM candidate без approved state, self-generated/recomputed baseline либо candidate с изменённым anchor даёт validator error `BaselineNotApproved`/`UpstreamWarningBaselineMismatch` и не может породить build gate result. Для .NET typed baseline anchor имеет explicit `NotApplicable` state; в report JSON оба JVM-only digest fields остаются `null`, а не получают выдуманный digest.

Public surface закрыт следующими операциями; конкретные model constructors не public:

```text
PortabilityReportV01.Build(PortabilityReportEvidenceSet evidence, DateTimeOffset createdAtUtc)
PortabilityReportV01.Validate(ReadOnlySpan<byte> reportBytes, PortabilityReportEvidenceSet evidence)
PortabilityReportV01.CanonicalJsonBytes()
PortabilityReportV01.SemanticProjectionBytes()
PortabilityReportV01.MarkdownBytes(string canonicalJsonRelativePath)
PortabilityReportWriter.WriteNewDirectory(report, evidence, finalDirectory)
```

Returned byte arrays являются defensive copies. `Validate` повторно строит report из тех же independently revalidated evidence bytes и требует exact equality canonical report bytes; структурной проверки self-declared JSON недостаточно. `canonicalJsonRelativePath` проходит portable relative-path validation и используется только как Markdown link; absolute/parent path rejected. Build/codec не читают filesystem, environment или network. Writer принимает уже построенный report и тот же evidence set, разрешает final/staging paths, проверяет same-volume strict-parent containment и не следует symlink/reparse points.

Каждый raw receipt digest в evidence index имеет role-specific domain `strogo.portability-report.v0.1/raw-receipt/<profileId>/<os>/<arch>/<role>`; profile build receipt использует platform segments `profile/profile`. Каждый stable gate summary использует отдельный domain `strogo.portability-report.v0.1/gate/<profileId>/<os>/<arch>/<role>`. Writer сохраняет canonical receipt bytes под closed relative paths `evidence/<profileId>/<os>-<arch>/<role>.json` и создаёт `evidence-index.json` с role/path/rawReceiptDigest/gateDigest. Index не является дополнительным authority: validator безопасно читает paths, проверяет bytes/typed receipt, сам строит gate summary/digests и заново строит report. Diagnostic performance receipts могут храниться тем же способом, но их digest исключён из semantic projection.

#### 6.2.1 Closed top-level schema

Canonical JSON содержит ровно:

```text
schemaVersion = "strogo.portability-report.v0.1"
purpose = "validation-only"
contractStatus = "validation-fixture"
admissionStatus = "NotAdmittable"
sourceRevision
moduleDigest
ownerBundleDigest
proofDigest
dafnySourceDigest
profiles[]
comparisonStatus
reasons[]
semanticDigest
createdAtUtc
```

`purpose`, `contractStatus` и `admissionStatus` не поступают от вызывающего кода: builder записывает константы. `sourceRevision` — exact 40-character lowercase Git commit SHA одной clean evidence revision; он обязан совпасть во всех typed build/platform/gate results. Все четыре root digests обязательны и имеют lowercase SHA-256 form.

Report требует ровно два profile: `dotnet-managed.v1` и `jvm-java17.v1`, sorted ordinal. Отсутствующий profile не синтезируется из воздуха: builder отклоняет input как `MissingProfile`, потому что без profile-level toolchain/requirement identity невозможно отличить «не запускали» от другого эксперимента. Для известного profile с отсутствующей OS environment используются две явные `Unavailable` rows.

#### 6.2.2 Profile schema и build gate

Каждый profile содержит ровно:

```text
profileId, translatorDigest, buildToolchainDigest, runtimeRequirementDigest,
upstreamWarningBaselineDigest, upstreamWarningApprovalDigest,
portabilityManifestDigest, packageDigest, artifactDigest,
buildReproducible[true|false|null], buildGateDigest,
status, reasonCodes, platforms[]
```

`translatorDigest`, `buildToolchainDigest` и `runtimeRequirementDigest` обязательны для любой profile row в report. `upstreamWarningBaselineDigest` и `upstreamWarningApprovalDigest` всегда `null` для .NET; для JVM `Portable`/built artifact они обязательны и берутся только из typed E06A approval validation result. JVM build refusal до approval сохраняет оба поля `null` и `TargetBuildRejected`. Validator errors `BaselineNotApproved`, `UpstreamWarningBaselineMismatch`, `OutcomeCoverageMismatch`, `MissingProfile` и `InvalidCreatedAtUtc` не являются report reason codes и не создают report output.

`portabilityManifestDigest`, `packageDigest`, `artifactDigest` и `buildGateDigest` либо присутствуют все, либо все равны `null`. Они могут быть `null` только когда profile содержит `TargetBuildRejected` или `NonReproducibleBuild` и не имеет `Passed` platform row.

`buildReproducible=true` требует полный digest quartet и stable `buildGateDigest`, вычисленный из validated two-root result. Raw build receipt сохраняется в evidence index, но не является semantic field. `false` запрещает profile status `Portable`: при фактическом drift добавляется `BuildNotReproducible`/`NonReproducibleBuild`, а при более раннем typed build refusal сохраняется точная причина `TargetBuildRejected` без ложного утверждения, что reproducibility experiment был выполнен.

#### 6.2.3 Platform schema и gate receipts

Mandatory rows `(linux,x64)` и `(windows,x64)` содержат:

```text
os, arch, osIdentity, kernelIdentity, runtimeVendor, runtimeVersion,
runtimeClosureDigest, launcherDigest, harnessDigest,
portabilityManifestDigest, packageDigest, artifactDigest,
status, reasonCodes, vectorSetDigest, outcomeDigest,
consumerGateDigest, jitGateDigest, oracleGateDigest,
diagnostics[consumerReceiptDigest,jitReceiptDigest,stderrDigest,performanceReceiptDigest]
```

Для `Passed` обязательны все normative поля; diagnostic `performanceReceiptDigest` может быть `null`; reasonCodes пуст. Manifest/package/artifact совпадают с profile identity. `consumerGateDigest` связывает stable A14 summary, `jitGateDigest` — stable A12 summary, `oracleGateDigest` и `vectorSetDigest/outcomeDigest` — A5/A6/oracle comparison.

Gate digest не равен raw receipt digest. Он вычисляется из canonical summary validated result:

```text
common: schemaVersion, sourceRevision, profileId, os, arch,
        moduleDigest, ownerBundleDigest, proofDigest, dafnySourceDigest,
        portabilityManifestDigest, packageDigest, artifactDigest,
        runtimeClosureDigest, harnessDigest, gateStatus, directGateReasons
consumer: common + publicApiDigest + vectorSetDigest + outcomeDigest
jit:      common + launcherDigest + entrySymbol + candidateSymbol + calls + eventKinds
oracle:   common + vectorSetDigest + outcomeDigest + ownerDomainRuleDigest
build:    sourceRevision + profile/toolchain/source/artifact identities
          + twoRootInventoryDigest + gateStatus + directGateReasons
```

`calls` для passed JIT summary не меньше `50000`; symbols заранее получены из source map; `eventKinds` — canonical unique ordinal list фактически validated compilation-event classes. `directGateReasons` не включает derived `RowFailed`/`RowUnavailable`, чтобы gate digest не зависел от report aggregation. PID, timestamps, durations, raw log/stderr digests и physical paths в gate summary запрещены. Raw receipt digest остаётся в `diagnostics`/evidence index и исключается из semantic projection.

Весь `diagnostics` object — attachments. Его поля не влияют на status и исключаются из semantic projection. Все остальные platform gate/identity fields нормативны для report verdict.

#### 6.2.4 Canonical owner-domain outcome

`vectorSetDigest = H("strogo.portability-report.v0.1/vector-set", canonical input vector set bytes)`. Vector-set entries имеют closed fields `(vectorId,inputDigest,inputBytes/domain metadata)`, duplicate `vectorId` rejected, then canonicalized by ordinal `vectorId`; source enumeration order cannot change this digest.

`outcomeDigest = H("strogo.portability-report.v0.1/domain-outcomes", canonical ordered rows)`; каждая row содержит ровно:

```text
vectorId, inputDigest,
domain[classification, ownerClauseDigest],
termination,
outcome
```

`classification` закрыт значениями `OwnerInDomain`, `OwnerOutsideDomain`, `TransportOnly`. `ownerClauseDigest` обязателен для первых двух и `null` для transport-only case. `termination` для passed observation — `Returned` либо `Refused`; target timeout/crash не кодируется как успешный outcome и делает platform row `Failed/TargetExecutionFailed`. `outcome` — exact canonical union `{kind:"success",value:<WireValue>}` либо `{kind:"refusal",code,locus,details}`. Rows unique и ordinal-sorted по `vectorId`; `inputDigest` обязан ссылаться на ту же vector-set entry.

Coverage является биекцией: canonical vector-set entries и outcome rows имеют одинаковую cardinality, каждый `vectorId` встречается ровно один раз в каждом наборе, а `(vectorId,inputDigest)` совпадает byte-for-byte. Missing, extra или duplicate mapping даёт typed validator error `OutcomeCoverageMismatch` до `oracleGateDigest`; никакое подмножество fixed workload не может получить `Passed`. Outcome rows также canonicalized ordinal by `vectorId`, поэтому harmless enumeration reorder не меняет digest, а подмена entry/input меняет его или отвергается.

Owner oracle validator вычисляет classification и expected outcome из exact owner bundle/clauses, target validator добавляет actual outcome, а gate сравнивает их до создания `oracleGateDigest`. False-requires input остаётся `OwnerOutsideDomain` и diagnostic refusal, а не backend correctness verdict; transport input остаётся `TransportOnly`. `ownerDomainRuleDigest` в oracle gate summary — digest canonical `(vectorId,classification,ownerClauseDigest)` projection.

#### 6.2.5 Partial evidence и reason scopes

| State | Profile build quartet | Platform normative fields | Required direct reasons |
| --- | --- | --- | --- |
| Target build rejected before artifact | all `null`, `buildReproducible=null` | обе rows `Failed`; artifact/runtime/gates `null` | profile `TargetBuildRejected`; rows `TargetBuildRejected`,`RowFailed` |
| Two-root build differs | all `null`, `buildReproducible=false` | обе rows `Failed`; gates `null` | profile `BuildNotReproducible`,`RowFailed`; rows `NonReproducibleBuild`,`RowFailed` |
| Artifact built, OS environment absent | full quartet, `buildReproducible=true` | artifact triple+harness required; OS/runtime fields retain only observed values; gates `null` | row `EnvironmentUnavailable`,`RowUnavailable` |
| Consumer fails | full quartet | artifact/runtime/harness + failed consumer gate summary and raw receipt required; later gates may be `null` | row `ConsumerFailed`,`RowFailed` |
| JIT gate fails | full quartet | passed consumer + failed JIT gate summary and raw receipt required | row `JitEvidenceMissing`,`RowFailed` |
| Oracle mismatch | full quartet | consumer/oracle gate and outcome evidence required | row `OracleMismatch`,`BackendSemanticMismatch`,`RowFailed` |
| Passed | full quartet | все normative fields/gates required | no reasons |

Любое присутствующее поле валидируется и связывается с `sourceRevision`/root/profile/artifact identities, даже если state failed. Builder не принимает reason arrays: direct reasons приходят из typed results, `RowFailed`/`RowUnavailable` вычисляются.

Direct platform causes существуют только в scope `platform/<profileId>/<os>/<arch>`. Profile scope содержит build-level cause, computed `RowFailed`/`RowUnavailable` и cross-profile `OracleMismatch`, но не дублирует `ConsumerFailed`/`JitEvidenceMissing` каждой row. Aggregate `reasons` содержит обе группы. Отсутствующая mandatory row при built profile становится exact `Unavailable` row с profile artifact triple/harness identity и `EnvironmentUnavailable`/`RowUnavailable`; missing whole profile rejected.

#### 6.2.6 Status и reason algorithm

Profile `Portable` тогда и только тогда, когда:

1. `buildReproducible=true` и полный build identity quartet присутствует;
2. обе mandatory platform rows имеют `Passed`;
3. у каждой row присутствуют consumer/JIT/oracle gate digests и vector/outcome digests;
4. manifest/package/artifact одинаковы у profile и обеих rows;
5. `vectorSetDigest` и `outcomeDigest` одинаковы у обеих OS rows profile.
6. При совместном построении обоих mandatory profiles отсутствует cross-profile `vectorSetDigest` или `outcomeDigest` mismatch; если любой найден, оба profile получают `OracleMismatch` и становятся `NotPortable`.

Иначе profile получает `NotPortable` и минимум одну точную причину. Closed report reason set ровно:

```text
ArtifactIdentityMismatch, BackendSemanticMismatch, BuildNotReproducible,
ConsumerFailed, EnvironmentUnavailable, JitEvidenceMissing,
NonReproducibleBuild, OracleMismatch, RowFailed, RowUnavailable,
TargetBuildRejected, TargetExecutionFailed, UnsupportedPortableAbi
```

Validator mapping детерминирован:

| Typed result | Row status / row reasons | Profile reasons |
| --- | --- | --- |
| `TargetBuildRejected` | обе rows `Failed`; `RowFailed,TargetBuildRejected` | `RowFailed,TargetBuildRejected` |
| `UnsupportedPortableAbi` | обе rows `Failed`; `RowFailed,UnsupportedPortableAbi` | `RowFailed,UnsupportedPortableAbi` |
| `NonReproducibleBuild` | обе rows `Failed`; `NonReproducibleBuild,RowFailed` | `BuildNotReproducible,RowFailed` |
| OS environment absent | row `Unavailable`; `EnvironmentUnavailable,RowUnavailable` | `RowUnavailable` |
| Actual artifact identity drift during bound run | row `Failed`; `ArtifactIdentityMismatch,RowFailed` | `RowFailed` |
| Consumer failure | row `Failed`; `ConsumerFailed,RowFailed` | `RowFailed` |
| JIT gate failure | row `Failed`; `JitEvidenceMissing,RowFailed` | `RowFailed` |
| Target timeout/crash | row `Failed`; `RowFailed,TargetExecutionFailed` | `RowFailed` |
| Target vs owner oracle | row `Failed`; `BackendSemanticMismatch,OracleMismatch,RowFailed` | `RowFailed` |
| Cross-OS vector/outcome mismatch after individually passed rows | rows не переписываются; profile `NotPortable` | `OracleMismatch` на profile |
| Cross-profile vector/outcome mismatch after individually passed rows | rows не переписываются; оба profiles `NotPortable` | `OracleMismatch` на каждом profile |

Cross-field artifact/package/report inconsistency при построении не является наблюдением platform run и отклоняет весь input; `ArtifactIdentityMismatch` используется только typed bound-run result после успешной preflight identity validation.

`comparisonStatus=Portable` только когда оба profiles `Portable`, их `vectorSetDigest` равен и `outcomeDigest` равен. Cross-OS mismatch уже добавляет `OracleMismatch` соответствующему profile; cross-profile mismatch любого из двух digests добавляет `OracleMismatch` обоим profiles и делает их `NotPortable`; builder не принимает self-declared status.

Aggregate `reasons` содержит unique objects `{scope,code}`, sorted по `(scope,code)`: все profile reasons получают `profile/<profileId>`, все row reasons — `platform/<profileId>/<os>/<arch>`. Другого копирования причин между scopes нет. `NotPortable` без aggregate reason — invalid internal state.

#### 6.2.7 Semantic projection

`semanticDigest = H("strogo.portability-report.v0.1/semantic", canonical projection bytes)`.

Projection равна report без:

- `semanticDigest`;
- `createdAtUtc`;
- каждого platform `diagnostics` object целиком.

Stable validated `jitGateDigest`, `consumerGateDigest` и `oracleGateDigest` остаются в projection, потому что эти gates влияют на `Portable`. Raw receipt/JIT event/log находятся только в `diagnostics` и evidence index.

Изменение timestamp, raw consumer/JIT receipt, stderr или performance receipt не меняет `semanticDigest`, если повторная typed validation даёт тот же gate summary. Изменение boundary constant, source revision, baseline approval anchor, root/profile/platform identity, build flag, status/reason, gate summary, vector set, domain classification, termination либо outcome обязано изменить digest или вызвать отказ.

`createdAtUtc` нормализуется из `DateTimeOffset` через checked `ToUniversalTime()` и exact invariant format `yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'`. Validator принимает только эту ASCII форму и требует roundtrip в UTC с теми же 100-nanosecond ticks. Два offset-equivalent значения дают одинаковые bytes; overflow при UTC conversion и любой изменённый precision/offset/text в serialized report дают `PortabilityReportRejected/InvalidCreatedAtUtc`.

#### 6.2.8 Markdown projection

Markdown строится только из валидированного report object и показывает:

- `validation-only`, `validation-fixture`, `NotAdmittable` в начале;
- comparison/profile/platform statuses и reasons;
- сокращённые, но однозначные digests со ссылкой на canonical JSON;
- какие evidence rows фактически прошли, failed или unavailable;
- отдельный блок diagnostic observations без повышения до verdict;
- явные границы: fixed workload, WSL2 если применимо, отсутствие production admission и отсутствие вывода G05/G06.

Visual planning artifact: отдельный GUI не применим; Markdown table является пользовательской projection. UI video evidence не применимо.

#### 6.2.9 Ошибки и output discipline

Любая schema/evidence inconsistency даёт `PortabilityReportRejected` с canonical locus и closed validator reason. Builder полностью валидирует/canonicalizes in memory и возвращает bytes без filesystem side effect. Evidence writer создаёт новый unique staging directory внутри заранее отсутствующего run directory, пишет JSON/Markdown/evidence receipts/index, перечитывает и повторно валидирует их, затем последним создаёт `sha256.txt` completion marker и atomically переименовывает staging directory в заранее отсутствующий final directory на том же volume. При отказе final directory/marker отсутствуют; existing destination не перезаписывается. Cleanup failure сохраняется как отдельный local diagnostic и не считается publication success.

## 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| R1 | Сформировать report из двух полных profiles | JSON/Markdown/evidence index показывают `Portable` только при полном совпадающем evidence и проходят independent rebuild validation | canonical report + conformance | A1–A5 |
| R2 | Одна обязательная OS row отсутствует | Она видна как `Unavailable`; profile/comparison — `NotPortable` | mutation report | A3,A4 |
| R3 | Изменить stderr/timestamp/performance attachment | Report bytes меняются, `semanticDigest` сохраняется | paired reports | A7,A11 |
| R4 | Изменить outcome/gate/identity либо смешать package | Digest меняется либо builder отказывает; PASS не сохраняется | mutation receipts | A4,A5,A7,A8 |
| R5 | Открыть Markdown | Сразу видны границы validation fixture и отсутствие admission/G05/G06 | projection diff | A9 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Оба profile inputs валидны | Build report | Canonical JSON + Markdown + completion inventory | Existing destination rejects | Staged directory publication |
| Profile известен, OS row отсутствует | Complete matrix | Synthesized `Unavailable` | Не считается PASS | Сохраняет частичный результат |
| Обязательный profile отсутствует | Build report | `MissingProfile` | No output | Profile identity не выдумывается |
| JVM baseline не подтверждён | Integration evidence request | Synthetic/negative report only | Реальный JVM build запрещён | E06A gate сохраняется |
| Concurrent writers | Publish same destination | Один directory rename success, остальные refuse | No overwrite | Marker создаётся до rename внутри staging |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Boundary fields входят в root schema | agent | Константы builder | 0.99 | Без них projection противоречит E06 | Нет; подтверждается SPEC |
| Gate receipt digests входят в semantic projection | agent | JIT/consumer да, raw logs/performance нет | 0.97 | Иначе status не связан с evidence | Нет; подтверждается SPEC |
| Missing profile | agent | Reject, не synthesize | 0.98 | Синтез потребовал бы выдумать toolchain identity | Нет |
| Failed evidence nullability | agent | All-or-none build quartet; partial row fields разрешены и проверяются | 0.95 | Слишком строгая схема потеряет отрицательный evidence | Нет |
| Реальный JVM integration run | человек/E06A | Только после `Baseline подтверждаю` | 0.99 | Обход отдельного gate | Да, но не блокирует builder EXEC |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Matrix rows | `PortabilityReportMatrix` | Reuse/extend closed validation | Additive API | Existing + new conformance |
| Full report | E06 §6.2.4 | Exact schema этой поправки | Первая реализованная v0.1; historical partial reports не мигрируют | byte fixtures + parser/builder roundtrip |
| E05 admission | E05 package schemas | Без изменений | E06 fields не принимаются E05 loader | API/schema scan |
| JVM evidence | E06A | Без изменений | Synthetic/negative until baseline approval | command/output absence check |

## 7. Бизнес-правила / Алгоритмы

1. Status всегда вычисляется из typed validated evidence; caller не передаёт status, reason или digest как authority.
2. `Passed` требует положительных identities/receipts, а отсутствие evidence не трактуется как success.
3. Diagnostic-only поля не меняют semantic digest и не могут устранить reason.
4. Любой normative identity/gate/outcome drift меняет semantic digest либо делает report invalid.
5. Один и тот же artifact triple обязателен для двух OS rows profile; один owner/vector/outcome contract — для обоих profiles.
6. `Portable` описывает только fixed E06 validation workload и не означает production admission или достижение G02/G06 в целом.
7. Все значимые выводы и refutations сохраняются в `docs/knowledge-log.md`.

## 8. Точки интеграции и триггеры

- Exact evidence driver передаёт receipt bytes/expected identities profile validators и вызывает report builder только с их typed результатами.
- Builder использует matrix completion до вычисления profile/comparison statuses.
- JSON и Markdown codecs принимают только валидированный immutable report.
- Будущий JVM pipeline передаёт receipts только после полного E06A/JAR/runtime gates.

## 9. Изменения модели данных / состояния

- Новые immutable report/profile/platform/reason models.
- Новая canonical JSON schema v0.1 и deterministic Markdown projection.
- Mutable persisted state, database, credentials и network state отсутствуют.

## 10. Миграция / Rollout / Rollback

- Existing matrix API и historical evidence остаются читаемыми; full report добавляется отдельно.
- Исторические .NET receipts не объединяются автоматически из-за разных source revisions.
- Rollback: revert additive builder/codec/harness/docs commits; matrix и profile artifacts остаются без изменений.
- Ни push, ни этот approval не разрешают release/admission либо E06A Stage 2.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- **A1:** schema содержит exact boundary constants, четыре root digests, ровно два sorted profiles, две sorted rows/profile, computed statuses/reasons, semantic digest и UTC timestamp.
- **A2:** profile build quartet obeys all-or-none/nullability rules; `buildReproducible=true` требует полного verified build gate, `false` означает доказанный two-root drift, `null` — build refusal до reproducibility result; exact source revision anchor обязателен для каждого receipt.
- **A3:** absent mandatory row синтезируется exact `Unavailable`/`EnvironmentUnavailable`/`RowUnavailable`; missing mandatory profile отвергается без output.
- **A4:** `Portable` возможен только при двух Passed rows с exact matching profile identities и обязательными consumer/JIT/oracle gate summaries; cross-OS или cross-profile mismatch любого `vectorSetDigest`/`outcomeDigest` даёт детерминированный `NotPortable/OracleMismatch` либо identity rejection.
- **A5:** canonical owner-domain outcome rows включают input identity, classification, owner clause, termination и exact result/refusal; vector-set и outcome rows образуют exact bijection с одинаковой cardinality; missing/extra/duplicate mapping, domain/termination/output mutations fail closed или меняют `outcomeDigest` и не проходят oracle gate.
- **A6:** aggregate reasons unique и ordinal-sorted по exact mapping table; unknown/duplicate reason/status/profile/platform и `NotPortable` без причины rejected.
- **A7:** timestamp/diagnostic receipt/stderr/performance mutations сохраняют `semanticDigest` только при том же validated gate summary; identity/status/reason/gate/vector/domain/termination/outcome mutations меняют его или reject.
- **A8:** package/report mix-up, mixed source revisions, unapproved/self-generated E06A baseline, recomputed self-consistent outer fields, missing receipt и changed boundary constant не могут сохранить accepted report.
- **A9:** Markdown генерируется из того же model и явно показывает validation/admission/G05/G06 boundaries и фактические rows.
- **A10:** staged writer rejects traversal/absolute paths, symlink/reparse targets, cross-volume destination, existing destination and concurrent collision; injected write/read/hash/cleanup failures leave no completion marker/final directory and retain bounded diagnostic.
- **A11:** `createdAtUtc` uses exact UTC seven-fraction format; offset-equivalent input canonicalizes identically, invalid serialized precision/offset/overflow rejects.
- **A12:** existing solution/conformance regressions проходят; exact clean-commit report evidence сохраняет hashes, command outputs и отсутствие реального JVM output до E06A gate.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| A1–A2 | canonical schema + nullability + revision/baseline anchor fixtures | inspect exact fields/digests | conformance JSON | — |
| A3 | absent row + missing profile | verify no output on rejection | negative receipts | — |
| A4–A6 | status/reason/identity/gate/domain matrix | inspect reason scopes/order | mutation report | — |
| A5 | owner-domain/termination/outcome mutations | compare canonical row bytes | oracle receipts | — |
| A7 | paired semantic/diagnostic mutations | compare report bytes/digests | digest table | — |
| A8 | mix-up/revision/baseline/recomputed/missing mutations | typed locus/reason | negative receipts | — |
| A9 | Markdown snapshot/semantic cross-check | read projection | `REPORT.md` | — |
| A10 | writer path/collision/failure injection suite | inspect marker/final-dir absence and cleanup | writer receipts | — |
| A11 | timestamp offset/precision/overflow fixtures | exact serialized text check | timestamp fixtures | — |
| A12 | locked build + portability/full managed conformance | git/evidence hash check | exact-revision evidence | — |

Минимальные команды: `dotnet build Kernel.slnx -c Release --no-restore`, portability conformance, full managed regression harnesses, writer/conformance harness и `git diff --check`. Реальные platform runs выполняются только когда для них есть same-revision inputs; synthetic test не выдаётся за platform evidence.

## 12. Риски и edge cases

- Receipt может быть корректного типа, но другого artifact: expected digests связываются на каждом уровне.
- Partial failure может потеряться из-за слишком строгих non-null полей: profile quartet и platform partial evidence имеют разные правила.
- Semantic digest могут ошибочно превратить в whole-report integrity hash: docs явно ограничивают его semantic projection.
- JIT является observation, но A12 — gate: в projection входит только validated gate receipt digest, raw log исключён.
- Старые receipts разных revisions могут выглядеть совместимыми: integration driver требует same source/root identities и exact expected inputs.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Почему нельзя показать полностью отсутствующий profile как Unavailable?» | Это выглядит удобнее | Без toolchain/requirement identity report выдумал бы эксперимент; explicit profile input с unavailable rows решает сценарий | mitigated |
| «JIT — диагностика, зачем его digest в semantic projection?» | E06 исключает raw JIT observations | Входит только validated A12 gate receipt, потому что он влияет на status; raw log остаётся исключён | mitigated |
| «Report объявит переносимость слишком рано» | Слово `Portable` широкое | Root boundary и Markdown связывают status только с fixed validation workload; production/G02/G06 claims запрещены | mitigated |
| «Почему не использовать уже сохранённые .NET runs?» | Они существуют | Разные exact revisions нельзя смешивать в единый factual report; сначала builder synthetic evidence, потом same-revision integration | accepted-risk |

### Rework Prevention Checklist

- Видимый JSON/Markdown output и failure states заданы.
- Каждый scenario связан с AC/evidence.
- Agent-owned решения перечислены; E06A human gate сохранён.
- Nullability, reason derivation и semantic projection заданы до кода.
- Role-based и independent review выполняются до approval.
- EXEC имеет путь к positive, partial и adversarial evidence без обхода JVM gate.

## 13. План выполнения

1. После approval реализовать immutable report model, closed validation и status/reason derivation.
2. Реализовать canonical JSON/semantic projection и mutation conformance.
3. Реализовать deterministic Markdown projection и staged-directory no-overwrite writer с completion inventory.
4. Выполнить working-tree build/conformance, исправить findings, сохранить knowledge и journal.
5. Закоммитить/push implementation; на exact revision пересобрать и повторить full managed regressions.
6. Сохранить synthetic/negative report evidence с явной границей `NoJvmExecutionBeforeBaselineApproval`.
7. Провести post-EXEC review. Реальный full E06 report создать только после готовности same-revision JVM/.NET receipts.

## 14. Открытые вопросы

Нет. Отдельный E06A baseline gate остаётся будущим operational decision и не блокирует реализацию report model/harness.

## 15. Соответствие профилю

- Профиль: `product-system-design`.
- Выполненные требования профиля: цели/non-goals, exact data contract, status algorithm, partial/error states, integrations, migration, rollback, AC/evidence и user projection.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Strogo.Modules.Portability/PortabilityReportV01.cs` | model/builder/validator/codecs | Единый normative report core |
| `src/Strogo.Modules.Portability/PortabilityReportMatrix.cs` | reuse/closed helpers при необходимости | Не дублировать row rules |
| `tests/Strogo.Modules.Portability.Conformance/Program.cs` | report/mutation checks | A1–A8/A12 |
| `tools/Build-PortableReport.ps1` | staged evidence projection после receipt validation | Фактический integration path |
| `docs/modules-v0.2.md`, `docs/knowledge-log.md`, E06 journal | статус, границы и результаты | A9/A12 и knowledge rule |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Boundary | Требуется текстом, отсутствует в field list | Exact root constants |
| Status authority | Не определено | Только computed from evidence |
| JIT/consumer gates | Влияют на status без receipt binding | Per-platform validated receipt digests |
| Partial evidence | Nullability не определена | Closed profile/platform rules |
| Semantic digest | Общий список исключений | Exact projection field set |
| Human view | Общая projection | Deterministic Markdown из того же model |

## 18. Альтернативы и компромиссы

- Только расширить существующий matrix: проще, но не закрывает root/profile/gate/semantic schema.
- Принимать caller-provided statuses: меньше кода, но сохраняет недоказуемый authority seam.
- Включить все diagnostics в semantic digest: даёт whole-report identity, но смешивает verdict с environment noise и противоречит E06/K-E06-048.
- Синтезировать отсутствующий profile: позволяет всегда печатать report, но требует выдуманных toolchain identities.
- Выбран отдельный computed full-report core с узким набором исключённых diagnostic полей.

## 19. Результат quality gate и review

### SPEC Linter Result

| № / блок | Статус | Проверяемое основание |
|---|---|---|
| 1 / A — цель и outcome | PASS | Один computed JSON/Markdown report с проверяемыми statuses и boundaries |
| 2 / A — AS-IS | PASS | Проверены E06 field list/требования и фактический `PortabilityReportMatrix` |
| 3 / A — корневая проблема | PASS | Неполная schema не связывает status с evidence и допускает разные трактовки digest |
| 4 / A — цели дизайна | PASS | Authority, partial evidence, projections и ordering перечислены |
| 5 / A — границы | PASS | JVM execution, admission, G05/G06, Wasm и mixed revisions исключены |
| 6 / B — ответственности | PASS | Model, validators, builder, matrix, writer и docs разделены |
| 7 / B — интеграции | PASS | Receipt validators → typed evidence → builder → codecs/writer |
| 8 / B — алгоритмы | PASS | Profile/comparison status, reasons и semantic projection заданы |
| 9 / B — ошибки/recovery | PASS | Typed rejection, no partial final directory, cleanup failure boundary |
| 10 / B — performance | PASS | Runtime performance не меняется; report-generation benchmark не нужен для bounded fixture |
| 11 / C — данные/state | PASS | Immutable models, canonical bytes, staged directory и completion marker |
| 12 / C — совместимость/migration | PASS | Additive API; historical receipts не смешиваются и не мигрируют |
| 13 / C — rollback | PASS | Revert additive report files без изменения matrix/artifacts |
| 14 / D — AC | PASS | A1–A12 задают измеримые schema/status/digest/output outcomes |
| 15 / D — AC→evidence | PASS | Positive, absent/missing, mutation и Markdown checks сопоставлены |
| 16 / D — команды/stop rules | PASS | Locked build/conformance/diff и JVM/mixed-revision stops заданы |
| 17 / E — план | PASS | Model → digest → Markdown/writer → exact regression → review |
| 18 / E — решения/open questions | PASS | Decision ledger заполнен; открытых design questions нет |
| 19 / E — масштаб/форма | PASS | Medium public evidence contract использует expanded template |
| 20 / F — profile | PASS | Product system data/output/error/migration contract описан |

Итог: ГОТОВО; independent post-SPEC review завершён PASS, ожидается owner approval.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один report/evidence seam, explicit non-goals |
| 2. Понимание текущего состояния | 5 | Matrix core и schema contradictions указаны |
| 3. Конкретность целевого дизайна | 5 | Fields, status, reasons, projection и errors закрыты |
| 4. Безопасность | 5 | Fail-closed, staged no-overwrite publication, rollback |
| 5. Тестируемость | 5 | Positive/partial/mutation/digest matrix |
| 6. Готовность к автономной реализации | 5 | Exact phases/files/stop rules |

Итоговый балл: 30 / 30. Зона: готово к автономному выполнению после review и approval.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Не расширяет ли fixed portability status общий claim? | PASS | Boundary constants/Markdown обязательны |
| UX / designer | applicable | Понятны ли в artifact projection статусы, причины и границы без чтения JSON? | PASS | Markdown order/content закреплены §6.2.8 и A9 |
| Tester / validation | applicable | Все ли status/digest seams имеют mutations? | PASS | A1–A12, coverage bijection и writer failure suite |
| Developer / architect | applicable | Согласованы ли schema, partial evidence и digest projection? | PASS | Supersession, gate summaries, owner outcome и nullability закрыты |
| Delivery / operations / security | applicable | Исключены ли overwrite/mixed-revision/JVM gate bypass? | PASS | source anchor, E06A approval anchor, staged marker и no-overwrite |

### Post-SPEC Review

- Статус / stop decision: **PASS — можно запрашивать подтверждение владельца.**
- Scope reviewed: `specs/2026-09-08-e06r-canonical-portability-report.md`, `AGENTS.md`, central `quest-governance.md`, `quest-mode.md`, `spec-linter.md`, `spec-rubric.md`, `review-loops.md`, profile `product-system-design.md`, `docs/project-intent.md`, E06 SPEC §§6.2.4/11, `PortabilityReportMatrix.cs`, `PortabilityContract.cs`, K-E06-048–K-E06-060 и planned files §16.
- Scope/Evidence pass: source/schema contradiction, existing matrix behavior, E06 acceptance boundaries, current JVM baseline gate и planned report/writer surface реально просмотрены. Working tree был clean до E06R; после draft изменены только E06R, E06 journal и knowledge log.
- Contract pass: explicit supersession E06 §6.2.4, constants, source revision, E06A anchor, typed evidence authority, profile/row nullability, status/reason mapping, owner-domain outcome, gate-summary/raw-diagnostic split, semantic projection and publication discipline согласованы между разделами и AC.
- Adversarial risk pass: проверены self-declared status/digest, mixed revision, self-generated baseline, raw PID/JIT log drift, missing/extra/duplicate outcome rows, cross-OS/cross-profile digest mismatch, package/report mix-up, absent rows/profile, path traversal/reparse/collision, timestamp normalization и no-output-on-failure.
- Role-Based pass: Business/domain, UX/artifact, Tester/validation, Developer/architect и Delivery/operations/security — PASS по таблице выше.
- Fix and re-review: первичный independent snapshot `5dac328f…` был `NEEDS-FIX`; исправлены 7 findings (supersession, JIT gate summary, outcome formula, revision/baseline anchor, nullability/reason scopes, writer/timestamp AC), затем добавлены outcome coverage bijection и оба cross-profile/cross-OS mismatch classes. Повторный snapshot `97FFAD291EC3013EA18CDB71F03A2683A56406B34C67C5A8510A61768A9349F9` — PASS, без BLOCKER/HIGH/MEDIUM; `git diff --check` PASS.
- Independent-review boundary: reviewer выполнил процедурно read-only проверку, но effective child sandbox был writable `danger-full-access`; это не технически изолированный read-only review. Тесты не запускались, потому что это SPEC-фаза; validation evidence для кода обязательна после approval.
- Findings:

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | precedence | E06 schema/projection clauses were under-specified | Add explicit supersession and preserve unaffected E06 clauses | fixed/re-reviewed |
| HIGH | evidence | Raw JIT receipt could poison semantic digest | Use stable gate-summary digest; exclude raw receipt/log | fixed/re-reviewed |
| HIGH | semantics | Outcome digest lacked owner/domain/termination definition | Add canonical row schema and owner oracle binding | fixed/re-reviewed |
| HIGH | trust anchors | Mixed revisions and unapproved E06A candidate could splice | Require expected source revision and typed approval anchor | fixed/re-reviewed |
| HIGH | completeness | Outcome subset could pass | Require exact vector-set/outcome bijection | fixed/re-reviewed |
| HIGH | comparison | Cross-OS/profile vector/outcome mismatch could lack reason | Map both digest mismatch classes to `OracleMismatch` | fixed/re-reviewed |
| MEDIUM | states/reasons | Partial/nullability and scope mapping were ambiguous | Add closed state table and deterministic mapping | fixed/re-reviewed |
| MEDIUM | delivery | Writer and timestamp lacked acceptance gates | Add staged marker, failure/concurrency/path and UTC fixtures | fixed/re-reviewed |

- No-findings justification after re-review: all findings above have explicit text, AC and evidence mapping; no unresolved HIGH/MEDIUM remains.
- Manual-review challenge: human should still inspect whether proposed gate-summary fields are sufficient for actual retained receipts and whether `Portable` wording is appropriate for the fixed validation fixture. Those are owner approval/design checks, not hidden implementation assumptions.
- Residual risks / needs human: owner must approve E06R with exact **«Спеку подтверждаю»**. Real JVM work remains separately gated by **«Baseline подтверждаю»**; this SPEC does not waive it.

### Post-EXEC Review

- Не выполнен: EXEC запрещён до approval этой SPEC.

## Approval

Ожидается фраза: **«Спеку подтверждаю»** именно для E06R после завершения post-SPEC review. Это не заменяет отдельную фразу E06A **«Baseline подтверждаю»**.

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Найти следующий E06 step без обхода JVM baseline gate | 0.99 | Full report field/status contract | Сверить E06 и matrix core | Нет | Нет | Canonical report независим от реального JVM build | E06 §6.2.4, `PortabilityReportMatrix` |
| SPEC | Устранить schema/evidence contradictions до code | 0.97 | Independent adversarial review | Провести post-SPEC review и исправить findings | Нет | Нет | Самовольный builder закрепил бы неоднозначную норму | Эта SPEC, K-E06-060 |
