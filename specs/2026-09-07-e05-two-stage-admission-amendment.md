# E05: двухэтапное human approval и admission неизменного пакета

## 0. Метаданные
- Тип (профиль): QUEST / product-system-design, security-sensitive workflow amendment.
- Владелец: пользователь — смысл контракта и решения approve/admit; агент — техническая схема и реализация после approval.
- Масштаб: medium.
- Целевое семейство / behavior baseline: E05 Modules v0.2 поверх утверждённой `2026-09-06-composable-verified-modules-v0.2.md`.
- Поверхность: Codex + локальные .NET CLI/library artifacts.
- Effective runtime: Не применимо; схема не зависит от модели, а будущий EXEC использует закреплённые .NET/Dafny tools E05.
- Eval baseline / evidence: tamper/state matrix ниже; результат сохраняется в `artifacts/e05/`.
- Целевой релиз / ветка: локальная `main`; push/release не входят в approval.
- Ограничения: E05 не строит production OS sandbox и не доказывает, что произвольный процесс того же пользователя не получил owner private key.
- Связанные ссылки: [E05 SPEC](2026-09-06-composable-verified-modules-v0.2.md), [журнал K-E05-061](../docs/knowledge-log.md), [цели](../docs/project-intent.md).

## 1. Overview / Цель
Устранить цикл в E05: прежняя команда `check` требовала approval, которое одновременно должно было ссылаться на ещё не созданные proof и build manifest.

Эта поправка нормативно заменяет только E05 §6.2 строки про `approve/check/build/run`, §6.4 admission chain и связанные command signatures. Исходные E05 scenarios `S1–S6` и criteria `AC1–AC9` сохраняются; дополнительные IDs этой поправки имеют prefix `A-` и не переопределяют их.

Outcome contract:
- Success means: смысл owner bundle подтверждён до proof, а точный proof/build package отдельно допущен человеком после build; runtime проверяет обе связи и актуальное owner state.
- Итоговый артефакт / output: `contract-approval.json`, `proof.json`, `build-manifest.json`, внешний `admission.json`, подписанный `owner-state.json`, host-pinned `OwnerTrust` и проверяющий их facade.
- Stop rules: любой missing/mismatched/revoked/expired digest, неподдержанный algorithm, unknown proof outcome или drift оставляет пакет неактивным; placeholder и изменение подписанного JSON запрещены.

## 2. Текущее состояние (AS-IS)
- E05 §6.2 задаёт `check --approval` перед `build`.
- E05 §6.2 одновременно требует в `approval.json` поля `proofDigest` и `buildManifestDigest`.
- Proof и manifest до этих команд отсутствуют; честный pipeline невозможен без placeholder, мутации approval или скрытого дополнительного решения.
- Admission code, package facade и owner signature storage ещё не реализованы, поэтому дефект можно исправить без миграции исполняемых E05 packages.

## 3. Проблема
Один артефакт смешивает два разных человеческих решения: «этот формальный контракт верно выражает моё намерение» и «этот конкретный проверенный бинарный пакет разрешён к запуску».

## 4. Цели дизайна
- Развести semantic approval и release admission во времени и идентичности.
- Не дать agent candidate влиять на уже подтверждённый owner bundle.
- Связать runtime с точными proof, toolchain, manifest и package bytes.
- Сделать expiration/revocation проверяемыми до исполнения.
- Сохранить immutable package: admission остаётся внешней подписанной записью.

## 5. Non-Goals (чего НЕ делаем)
- Production PKI, HSM, удалённый transparency log или защита от администратора ОС.
- Автоматическое утверждение человеческого смысла по результату theorem prover.
- Изменение языка expressions, owner models, `fold`, imports или typed patches.
- Сетевой registry и публикация packages.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности
- `Strogo.Modules.Owner.Cli` → управляет подписанным owner state, показывает canonical projection и создаёт approval/admission только через отдельные интерактивные owner operations.
- `Strogo.Modules.Cli check` → через host trust проверяет owner state и contract approval, затем проверяет candidate и выпускает proof outcome.
- `Strogo.Modules.Cli build` → через host trust заново проверяет current owner state/contract approval и собирает immutable package/manifest только после replay Verified check.
- Runtime facade → до load/invoke проверяет host trust anchor, owner state, package, обе подписи, digests и срок.
- `OwnerTrust` → host-owned корень доверия с pinned owner public key digest; package, module и canonical invocation не могут его задавать.
- `owner-state.json` → подписанный owner key локальный источник policy digest и общего revocation epoch.

### 6.2 Детальный дизайн

Нормативный pipeline:

```text
owner bundle + policy
  -> human semantic review
  -> approve-contract
  -> contract-approval.json
candidate + bundle + contract approval
  -> check/verify
  -> proof.json
verified proof
  -> build
  -> immutable package + build-manifest.json
package + proof + contract approval
  -> human release review
  -> admit
  -> external admission.json
package + admission + current owner state
  -> load/run
```

Целевые команды:

```powershell
dotnet run --project src/Strogo.Modules.Owner.Cli -c Release -- state advance-epoch --trust-config <host-owned.json> --signer-config <operator-owned.json> --expected-epoch <N>
dotnet run --project src/Strogo.Modules.Owner.Cli -c Release -- approve-contract --trust-config <host-owned.json> --signer-config <operator-owned.json> --bundle <bundle.json> --approved-by <id> --provenance <provenance.json> --valid-until <UTC> --out <contract-approval.json>
dotnet run --project src/Strogo.Modules.Cli -c Release -- check --trust-config <host-owned.json> --module <module.json> --bundle <bundle.json> --contract-approval <contract-approval.json> --out <check-dir>
dotnet run --project src/Strogo.Modules.Cli -c Release -- build --trust-config <host-owned.json> --check <check-dir> --out <package-dir>
dotnet run --project src/Strogo.Modules.Owner.Cli -c Release -- admit --trust-config <host-owned.json> --signer-config <operator-owned.json> --package <package-dir> --contract-approval <contract-approval.json> --approved-by <id> --valid-until <UTC> --out <admission.json>
dotnet run --project src/Strogo.Modules.Cli -c Release -- run --trust-config <host-owned.json> --package <package-dir> --admission <admission.json> --function <id> --input <canonical.json>
```

`host-owned.json` содержит pinned owner public key digest и путь к operator-owned каталогу `owner-state-store`; это operator launch config, а не Strogo input. Каталог state store входит в TCB: candidate, package, invocation input и typed patch получают к нему только read-only доступ, а единственным штатным writer является owner CLI. `operator-owned.json` содержит `keyId`, путь к encrypted PKCS#8 RSA private key и ожидаемый public key digest. Passphrase читается только интерактивно без echo, не из аргумента, environment или файла в репозитории. Key provisioning, ACL и установка host config/state store выполняются оператором вне repository EXEC; conformance использует отдельный ephemeral fixture key через библиотечный signer, недоступный как production CLI flag.

`approve-contract` и `admit` требуют TTY, показывают полный canonical projection и digest, после чего человек должен ввести полный digest без сокращения. EOF, redirected stdin, mismatch или отказ возвращают typed error и не создают output. `approvedBy`, provenance и `validUntil` задаются явно; `issuedAt`, `approvalEpoch`, `policyDigest` и `keyId` берутся из проверенного current state/signer. `validUntil` не может превышать max lifetime из owner policy. Нет `--yes`, default signer, default validity или overwrite существующего artifact.

Все JSON artifacts используют strict canonical JSON: UTF-8 без BOM, ordinal key order, integers без leading zero в диапазоне signed 64-bit, UTC timestamps только `yyyy-MM-ddTHH:mm:ss.fffZ`, digests как 64 lowercase hex и signatures как base64url без padding. Schema закрыта: unknown, missing и duplicate fields отклоняются; массивы имеют определённый ниже порядок. IDs соответствуют `[a-z0-9][a-z0-9._-]{0,63}`. `issuedAt < validUntil`, а `validUntil` ограничен policy lifetime. Значения `schemaVersion` равны соответственно `strogo.contract-approval.v0.2`, `strogo.proof.v0.2`, `strogo.build-manifest.v0.2`, `strogo.admission.v0.2` и `strogo.owner-state.v0.2`; aliases и version fallback запрещены.

`contract-approval.json` имеет ровно следующие поля и подписывает payload из всех полей, кроме `signature`:

```text
schemaVersion, approvalId, approvedBy, issuedAt, validUntil,
bundleDigest, policyDigest, approvalEpoch,
decisionProvenance(kind,reference,digest),
signatureAlgorithm, keyId, signature
```

В нём нет candidate/proof/build полей. Он подтверждает только неизменный смысл owner bundle и policy. `decisionProvenance` не привязан к Git: для текущего E05 он указывает commit/path/blob утверждённой SPEC, а другой host может использовать иной типизированный reference с digest. `decisionProvenance` — объект ровно из строковых полей `kind`, `reference`, `digest`; `approvalId` задаётся до projection/signing и новая подпись требует нового ID.

`check` пишет canonical module/bundle/contract approval, generated proof sources и bounded prover transcript рядом с `proof.json`. `proof.json` имеет ровно следующие поля:

```text
schemaVersion, moduleDigest, bundleDigest, contractApprovalDigest,
toolchainDigest, closureDigest, proofSourcesDigest, transcriptDigest,
sourceMapDigest, outcome, obligations(obligationId,kind,status,evidenceDigest)
```

`outcome` и obligation `status` принимают только `Verified`, `Unproven`, `Counterexample`, `Timeout`, `ToolError`; `obligations` сортируется по unique `obligationId`. `evidenceDigest` связывает canonical normalized evidence для любого результата. Только `outcome=Verified` и все obligations `Verified` допускают build. JSON-поля результата сами по себе не являются свидетельством: `build` через свой `--trust-config` заново проверяет fresh owner state, pinned key, contract approval signature/epoch/expiry, затем повторно запускает verifier по сохранённым canonical inputs закреплённым toolchain и требует byte-equal normalized transcript, obligation vector, source/proof digests и Verified outcome.

Нормативный proof transcript — canonical JSON с schema `strogo.proof-transcript.v0.2` и ровно полями `schemaVersion`, `toolchainDigest`, `proofSourcesDigest`, `outcome`, `records(obligationId,kind,status,diagnosticCode,evidenceDigest)`. `records` сортируются по unique `obligationId`; enums совпадают с `proof.json`. `diagnosticCode` берётся только из закрытого Strogo mapping, а `evidenceDigest` — domain-separated digest canonical counterexample/certificate summary; для отсутствующего evidence используется digest canonical `null`. Wall-clock time, duration, process/thread IDs, temp/absolute paths, locale text, raw stdout/stderr и произвольный solver prose в normative transcript запрещены. Они могут сохраняться только как bounded diagnostic вне package и не участвуют в решениях/digests. Два запуска по одинаковым canonical inputs/toolchain обязаны дать byte-identical normative transcript; иначе результат `ToolError/NonDeterministicVerifierOutput` и package не создаётся.

Package имеет закрытую структуру: в корне разрешены только `build-manifest.json` и каталог `content/`. В `content/` находятся перечисленные manifest файлы: canonical module, owner bundle, contract approval, полный `proof.json`, proof sources, bounded transcript, source map, generated assembly, `.deps.json` и необходимые non-platform runtime dependencies. Admission и owner state в package не входят.

`build-manifest.json` имеет ровно следующие поля:

```text
schemaVersion, moduleDigest, bundleDigest, contractApprovalDigest,
proofDigest, toolchainDigest, closureDigest, runtimeIdentifier,
entryAssemblyPath, files(path,sha256,length,role), packageDigest
```

Manifest не перечисляет себя и не включает внешний admission. `entryAssemblyPath` обязан ссылаться на единственный file с role `entry-assembly`; остальные допустимые roles: `module`, `bundle`, `contract-approval`, `proof`, `proof-source`, `proof-transcript`, `source-map`, `deps`, `runtime-dependency`. `files` отсортирован по canonical path и содержит каждый regular file под `content/` ровно один раз; `length` — неотрицательный byte count. Canonical path — lowercase relative UTF-8 с `/`; каждый segment соответствует `[a-z0-9][a-z0-9._-]*`. Запрещены absolute/drive/UNC paths, `.`/`..`, backslash, colon, control characters, Windows device names, ordinal duplicates и case-fold collisions. Verifier перечисляет фактическое дерево без следования ссылкам и отклоняет unlisted/missing files, symlink, junction, reparse point, alternate data stream и любой неожиданный root entry.

`packageDigest` вычисляется из canonical manifest payload без поля `packageDigest`; `buildManifestDigest` — из полного canonical manifest. Custom load context разрешает только manifest-listed non-platform dependencies, а platform assemblies берёт только из закреплённого .NET runtime/toolchain closure.

Перед предложением human release projection `admit` выполняет `PackageVerifier.VerifyForAdmission`: проверяет contract approval через current trust/state, manifest/files/closure и stored `proofDigest`, повторно запускает verifier по package canonical sources тем же pinned toolchain и требует byte-equal canonical normalized transcript/obligation vector и Verified outcome. Поэтому вручную собранный `proof.json` или `build-manifest.json` не может дойти до интерактивного подтверждения только за счёт самосогласованных полей. После этого `admission.json` имеет ровно следующие поля и подписывает payload из всех полей, кроме `signature`:

```text
schemaVersion, admissionId, approvedBy, issuedAt, validUntil,
contractApprovalDigest, proofDigest, buildManifestDigest,
toolchainDigest, policyDigest, approvalEpoch, packageDigest,
signatureAlgorithm, keyId, signature
```

`OwnerTrust` создаётся host application из operator-owned config до обработки package/module/input и содержит `keyId`, owner public key, его pinned digest и owner-state location. `--trust-config` существует только как operator launch option standalone CLI и не входит в Strogo ABI, package manifest или agent-editable typed patch. Подмена trust config находится в той же явно исключённой границе, что доступ произвольного процесса пользователя к owner private key.

`owner-state.json` имеет ровно поля `schemaVersion`, `keyId`, `approvalEpoch`, `supportedAlgorithms`, `maxContractLifetimeSeconds`, `maxAdmissionLifetimeSeconds`, `issuedAt`, `policyDigest`, `signatureAlgorithm`, `signature`. `approvalEpoch` и lifetimes — неотрицательные integers. Для E05 `supportedAlgorithms` равно ровно `["rsa-pss-sha256"]`. `policyDigest` покрывает schema/key/algorithms/lifetimes; signed owner-state payload дополнительно покрывает epoch, issuedAt и policyDigest. Runtime/check/owner CLI сначала проверяют state pinned owner key, затем используют тот же key для contract approval/admission.

Повышение epoch — отдельная интерактивная compare-and-swap операция с обязательным `expected-epoch`. Owner CLI под exclusive lock читает current state, проверяет signature и exact expected epoch, пишет и fsync-ит новый файл, затем делает atomic replace; при drift операция завершается без изменения. Она отзывает все approvals/admissions с меньшим epoch. Runtime ведёт process-local high-water `(approvalEpoch, ownerStateArtifactDigest)` и возвращает `OwnerStateRollbackDetected`, если новый snapshot имеет меньший epoch или другой digest при том же epoch. После restart защита от возврата к старому корректно подписанному state обеспечивается целостностью operator-owned state store, а не криптографией формата E05. Защита от произвольной записи того же OS user требует TPM/HSM/remote monotonic counter и остаётся non-goal.

Поддержанный E05 algorithm — `rsa-pss-sha256`: RSA-PSS с SHA-256, MGF1-SHA-256, salt length 32 bytes и trailer field 1. Public key кодируется DER SubjectPublicKeyInfo; `keyId` — lowercase hex SHA-256 этих bytes. Private key находится вне репозитория. Для `H(tag, bytes)` используется `SHA256(UTF8(tag + "\n") || bytes)`. Нормативные domain tags и inputs:

| Artifact / value | Digest input | Signature input |
| --- | --- | --- |
| contract approval payload | `H("strogo.contract-approval.v0.2/payload", canonical artifact без signature)` | `UTF8("strogo.contract-approval.v0.2/signature\n") || raw payload digest` |
| contract approval artifact | `H("strogo.contract-approval.v0.2/artifact", canonical full artifact)` | — |
| proof artifact | `H("strogo.proof.v0.2/artifact", canonical full proof.json)` | — |
| package identity | `H("strogo.package.v0.2/manifest-payload", canonical manifest без packageDigest)` | — |
| build manifest artifact | `H("strogo.build-manifest.v0.2/artifact", canonical full manifest)` | — |
| admission payload | `H("strogo.admission.v0.2/payload", canonical artifact без signature)` | `UTF8("strogo.admission.v0.2/signature\n") || raw payload digest` |
| admission artifact | `H("strogo.admission.v0.2/artifact", canonical full artifact)` | — |
| owner policy | `H("strogo.owner-policy.v0.2/payload", canonical policy fields)` | — |
| owner state payload | `H("strogo.owner-state.v0.2/payload", canonical state без signature)` | `UTF8("strogo.owner-state.v0.2/signature\n") || raw payload digest` |
| owner state artifact | `H("strogo.owner-state.v0.2/artifact", canonical full state)` | — |

Интерактивный owner CLI показывает projection и полный payload digest перед подтверждением; человек вводит именно этот digest. Artifact digest включает случайную RSA-PSS signature и поэтому появляется после подписи. Это фиксирует отдельное действие владельца, но не защищает от процесса с доступом к private key или вводу owner CLI. Повторная подпись того же payload не обязана давать те же bytes и создаёт новый approval/admission ID; детерминированно воспроизводятся canonical payload, payload/artifact digests и результат verification.

Runtime API меняется на:

```text
OwnerTrust trust = hostConfiguration.CreateOwnerTrust()
OwnerState state = trust.VerifyOwnerState(ownerStateBytes)
TrustedModuleRuntime runtime = new(trust, ownerStateProvider)
runtime.Load(packagePath, admissionPath, expectedBundleDigest)
```

`ownerStateProvider` принадлежит host и перед каждым `Load` и `Invoke` читает один bounded state snapshot из trusted state store, который `OwnerTrust` заново проверяет и сопоставляет с process high-water. Loaded module сохраняет admission epoch/policy; повышение current epoch или смена policy делает следующий invoke недопустимым. Уже начатый invocation завершается на своей immutable версии.

`run` и C# consumer используют только этот facade. Прямой вызов generated assembly не считается admitted execution. На `win-x64` facade сначала валидирует закрытое дерево, затем открывает каждый manifest-listed file без `FileShare.Write/Delete`, хеширует открытые handles и удерживает handles всего dependency closure до `Module.Dispose`; main assembly и lazy dependencies загружаются из этих уже проверенных handles/immutable copies только пока handles удерживаются. Hash одного path с последующим повторным открытием не принимается как защита от TOCTOU.

Error order: codec/schema → pinned trust/owner-state signature → key/algorithm → approval signatures → expiration/epoch → digest chain → locked file hashes/runtime binding → contract precondition → invoke → output checks. Ошибки одного класса сортируются по stable ID/path.

Visual planning artifact: Не применимо — UI нет; review surface состоит из canonical текстовой projection.

UI test video evidence: Не применимо — UI automation отсутствует.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| A-S1 | Подтвердить новый bundle | Projection показывает смысл и bundle digest; создаётся contract approval без candidate fields | snapshot + signature verification | A-AC1 |
| A-S2 | Проверить и собрать candidate | Verified proof и immutable manifest с полной digest chain | proof/build artifacts | A-AC2 |
| A-S3 | Допустить точный package | Projection показывает proof/toolchain/package digests; создаётся внешний admission | snapshot + signature verification | A-AC3 |
| A-S4 | Запустить admitted package | Facade проверяет текущий owner state и возвращает typed result | CLI и C# consumer | A-AC4 |
| A-S5 | Подменить любой artifact/file/trust source | Load отказан до вызова generated code | tamper + concurrent replacement matrix | A-AC5 |
| A-S6 | Повысить approval epoch/истечь сроку | Следующий load/invoke старого admission отклонён; уже начатый invocation может завершиться | state transition report | A-AC6 |
| A-S7 | Откатить active pointer на прежний package | В том же epoch допускается только package с ещё действующим admission; после повышения epoch требуется новый contract approval, proof/build и admission | rollback transition report | A-AC9 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Draft bundle | approve-contract | SemanticallyApproved | Invalid signature/key/policy → no artifact | Candidate ещё не участвует |
| Approved bundle + candidate | check | VerifiedCheck или typed refusal | Unknown/timeout → no build | Approval bytes immutable |
| VerifiedCheck | trusted build | BuiltNotAdmitted | owner-state/approval expiry, epoch или artifact drift → refusal | Package не исполняется публично |
| BuiltNotAdmitted | admit | Admitted | Human refusal → package остаётся built | Admission внешний |
| Admitted | trusted host load/run | Loaded/result | expired/revoked/tampered/concurrent replacement → refusal | Trust не приходит из module/input; code не вызывается до gate |
| Admitted epoch N | owner epoch N+1 | Revoked for next gate | Каждый новый load/invoke сверяет один fresh signed snapshot | Уже начатый invocation держит immutable version; последующие invokes запрещены |
| Active package A, epoch N | CAS pointer A→B или B→A | Active package меняется только при expected revision и valid admission epoch N | stale revision/expired admission → pointer unchanged | Rollback в пределах epoch не переиспользует решение для других bytes |
| Historical package, owner epoch N+1 | rollback request | New contract approval → check/build → admission → pointer CAS | Старые approval/admission epoch N отклоняются | Historical bytes допустимы как input сравнения, не как admitted artifact |
| Live process high-water N | state snapshot N-1 или иной digest N | `OwnerStateRollbackDetected` | code не вызывается | После restart целостность state store входит в TCB |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Число human gates | agent | Два: semantic approval и release admission | 0.94 | Один gate смешивает смысл и исполняемые bytes | Нет; это предмет подтверждения этой SPEC |
| Размещение admission | agent | Внешний подписанный artifact | 0.95 | Включение в package создаёт новый hash cycle | Нет |
| Revocation E05 | agent | Общий monotonic approval epoch + process high-water; persisted store в TCB | 0.88 | Отзыв шире точечного; rollback store после restart вне crypto guarantee | Нет; граница раскрыта |
| Signature algorithm | agent | RSA-PSS-SHA256, key вне repo | 0.86 | Не production key isolation | Нет; E05 threat model раскрыт |
| Runtime trust root | agent | Owner public key pinned host configuration | 0.94 | Произвольный state path позволил бы self-signed подмену | Нет |
| Активация после admit | agent | Не автоматическая; отдельный typed patch/pointer step из основной E05 | 0.95 | Скрытая активация смешивает решения | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Semantic approval | Противоречивый `approval.json` E05 | `contract-approval.json` только для bundle/policy | Старых E05 approvals нет | signature/bundle tamper |
| Release admission | Отсутствует | внешний `admission.json` на proof+manifest+package | Новый v0.2 schema | signature/digest tamper |
| Revocation | Неопределённый counter | signed monotonic epoch, process high-water и trusted state store | Все меньшие epoch invalid; persisted rollback остаётся TCB boundary | epoch/rollback matrix |
| Runtime trust | Не определён | host-pinned owner public key проверяет state и approvals | Новый host config | key/state substitution matrix |
| Runtime load | Двухаргументный целевой facade | trusted runtime + package+admission+expected bundle | Вызовы обновляются вместе | CLI/consumer |
| Package tree | Не определён | closed root + complete lowercase manifest + no links/extras/collisions | Новый v0.2 layout | tree/loader closure matrix |

## 7. Бизнес-правила / Алгоритмы
1. Contract approval создаётся до candidate proof и никогда не содержит candidate identity.
2. Build допустим только после собственной fresh trust/state/contract-approval проверки и replay exact Verified proof, связанного с тем же approval.
3. Admission создаётся только после build и подписывает точные digests всей цепочки.
4. Package и approvals immutable; исправление создаёт новые IDs/digests.
5. `validUntil < now`, `approvalEpoch < ownerState.approvalEpoch` или неизвестный key/algorithm всегда означает отказ.
6. Ни один successful `check/build` не означает human admission или activation.
7. Runtime precondition проверяется после admission gate и до вызова generated method.
8. Trust anchor не читается из package, module AST, canonical input или typed patch; owner state обязан иметь действительную подпись pinned key.
9. Hash и assembly load используют один набор file handles, удерживаемых до dispose module instance, чтобы concurrent replacement main/dependency path не прошёл между проверкой и lazy загрузкой.
10. `check` обязан проверить current signed owner state, pinned key, contract approval signature, epoch и expiry до запуска prover; forged/expired approval не может получить даже Verified proof artifact.
11. Owner commands требуют interactive exact-digest confirmation и encrypted private-key unlock; отказ или mismatch не оставляет частичный output.
12. `Verified` не доверяется как JSON-флаг: build и admit независимо проверяют current trust/state/approval и replay verification по exact canonical sources и pinned toolchain; mismatch/expiry/revocation/timeout означает отказ.
13. Strict schemas, domain tags, digest encodings, signature parameters и path grammar из §6.2 являются wire contract; permissive parsing и algorithm fallback запрещены.
14. Runtime разрешает dependency load только из проверенного manifest closure или закреплённого platform runtime; probing текущего каталога, PATH/GAC и unlisted package files запрещён.
15. Откат active pointer выполняется атомарным compare-and-swap. Повышение epoch отзывает и contract approvals, и admissions, поэтому historical package после epoch change проходит оба human gates и весь proof/build pipeline заново.

## 8. Точки интеграции и триггеры
- E05 `check/build/run/explain` CLI и будущий `Module.Load`.
- Owner CLI для двух representational decisions.
- Typed patch/active pointer принимает только `admissionDigest`, прошедший повторную полную проверку.

## 9. Изменения модели данных / состояния
- Пять versioned strict JSON artifacts из §6.2 с закрытыми schemas и domain-separated identities.
- Owner state, state store и host trust config persisted вне package/repo; proof/manifest/package/admission immutable.
- Calculated: все canonical/domain-separated digests и projections.

## 10. Миграция / Rollout / Rollback
- Исполняемых E05 packages пока нет; миграция данных не нужна.
- Старое имя `approval.json` не принимается как alias, чтобы fail closed.
- В текущем epoch rollback к старому package возможен только при действующем admission и atomic active-pointer CAS с expected revision.
- После повышения epoch старые contract approval и admission недействительны. Для rollback человек заново утверждает тот же bundle, agent повторяет check/build с новым approval, человек создаёт новый admission, затем pointer переключается CAS. Старые package bytes можно использовать как comparison input, но старый manifest не переиспользуется как admitted artifact.
- Откат реализации: удалить новые E05 CLI/facade artifacts; scalar/composite parser/reference checkpoints остаются работоспособны.

## 11. Тестирование и критерии приёмки

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| A-AC1 Contract approval не зависит от candidate | два candidate, один approval digest; module fields rejected | projection review | approval report | — |
| A-AC2 Proof/build chain ациклична | end-to-end approve→check→build; forged/expired/revoked approval rejected by check и повторно build до prover; forged Verified check rejected by build replay; два clean replay дают byte-equal normalized transcript | inspect ordered transcript | proof/manifest | — |
| A-AC3 Admission связывает exact package | sign/verify; forged/self-consistent proof/manifest rejected by admission replay; второй clean replay совпадает по normalized transcript; extra/missing/root files, duplicate/case-colliding paths and alternative build rejected | release projection | admission/package-layout report | — |
| A-AC4 Только facade исполняет admitted code | CLI + separate C# consumer | outcome/precondition transcript | runtime record | — |
| A-AC5 Tamper/TOCTOU fail closed | bundle/proof/manifest/file/toolchain/signature/key/state mutations; symlink/junction/reparse/ADS/traversal fixtures; replace main/dependency before load and before lazy resolution; unlisted dependency probing | stable errors; generated entry не вызван | tamper/loader matrix | — |
| A-AC6 Expiry/revocation работают | boundary time; epoch N/N+1 before load, between load/invoke и during invocation; same-process signed-state rollback/different digest at same epoch | owner state snapshot | revocation report | — |
| A-AC7 Исходные E05 gates сохранены | Modules + E04 + Reserve suites и E05 AC1–AC9 matrix | counts/hashes | final summary | — |
| A-AC8 Owner action не подменён default | redirected stdin, wrong digest, refusal, wrong key/passphrase, excessive validity, existing output | interactive positive run человеком | owner-action report без secrets | — |
| A-AC9 Rollback не обходит новые решения | same-epoch valid/expired admission и stale pointer revision; epoch N+1 rejects old contract approval/admission, requires full re-approval/check/build/admit | inspect transition order | rollback report | — |

Команды и exact run directory фиксируются в EXEC report. Идентичный solver/tool timeout не повторяется без новой гипотезы. Никакой успешный тест не называется доказательством человеческой идентичности signer.

## 12. Риски и edge cases
- TOCTOU owner state: trust verifies один bounded byte snapshot перед каждым load/invoke; in-flight вызов использует уже проверенную immutable version.
- Persisted owner-state rollback: process high-water ловит replay только в живом host process; после restart целостность operator-owned state store и его ACL входят в TCB. Более сильная гарантия требует TPM/HSM/remote counter.
- Package TOCTOU: closed tree и hashes проверяются по удерживаемым no-write/no-delete handles всего closure, handles живут до dispose module; E05 claim ограничен Windows semantics.
- Trust substitution: owner key pinned host configuration и не принимается из package/invocation; operator config и state store остаются частью TCB.
- Clock rollback: E05 expiry опирается на системное UTC и потому входит в TCB; epoch остаётся независимым механизмом отзыва.
- RSA key доступен agent process: вне production guarantees; тестируется только signature integrity и configured trust.
- Signature bytes не byte-reproducible из-за RSA-PSS randomness; identity approval задаётся canonical payload digest, а повторная подпись является новым provenance event.
- Общий epoch отзывает все старые artifacts: сознательный безопасный over-revoke для v0.2.
- Автоматизация TTY ввода процессом того же пользователя теоретически возможна; encrypted key/passphrase и host permissions являются границей E05, а не proof биологического человека.
- Replay verifier удваивает часть proof cost на build/admit; это сознательная цена отсутствия отдельного trusted attestation service и учитывается в E05 performance diagnostics.
- Build manifest self-hash cycle: package digest считается из payload без собственного поля; правило canonical и тестируется.
- Platform runtime drift: platform assemblies разрешены только из pinned toolchain/runtime closure; обновление runtime меняет `toolchainDigest` и требует нового build/admission.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Почему подтверждать дважды | Пользователь хочет меньше ручной работы | Первый раз подтверждается смысл, второй — точный исполняемый пакет; projection показывает разные данные | mitigated |
| Подпись не доказывает человека | Один OS user/process | Claim явно ограничен deliberate owner interface и key possession | accepted-risk |
| Кто доверяет owner-state | Signed old state можно replay после restart | Pinned key + process high-water; persisted state store явно включён в TCB | accepted-risk |
| Почему отзыв сразу всех | Нужна точечность | Общий epoch минимален и fail closed; точечный registry оставлен follow-up | accepted-risk |
| Можно ли запустить built package до admit | Удобство debug | Только internal harness; public facade/run возвращает NotAdmitted | mitigated |
| Откуда owner CLI берёт signer и срок | Иначе approval остаётся декларацией | Explicit signer/provenance/validUntil, encrypted PKCS#8 и exact-digest TTY confirmation | mitigated |

### Rework Prevention Checklist
- Видимые approve/check/build/admit/run результаты заданы.
- Каждый сценарий связан с evidence и AC.
- Решения и accepted risks перечислены.
- Contract, security, tester и workflow review выполнены ниже.
- EXEC имеет ацикличный проверяемый порядок.

## 13. План выполнения
1. После approval реализовать strict schemas, literal domain tags, canonical digests, exact RSA-PSS verifier, signed state store/CAS/high-water и negative fixtures; checkpoint commit.
2. Реализовать owner epoch/approve-contract, trusted check/build и closed immutable package manifest/tree; checkpoint commit.
3. Реализовать admit, facade/custom load context с fresh state per load/invoke, lifetime closure handles, expiry/epoch/tamper/rollback matrix и два consumers; checkpoint commit.
4. Выполнить regression, knowledge closure и post-EXEC review.

Работа над `fold`/composite proof может идти до шага 1, потому что не создаёт admission artifacts. Реализация admission до подтверждения этой поправки запрещена.

## 14. Открытые вопросы
Блокирующих вопросов нет. Точечный revoke, production key isolation, аппаратно/удалённо защищённый monotonic counter и remote registry — follow-up после E05.

## 15. Соответствие профилю
- Профиль: product-system-design / security-sensitive workflow.
- Выполнено: state machine, artifact/API contracts, failure order, migration, threat boundary, scenarios, AC/test matrix, decision ledger и rollback.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Strogo.Modules/**` | schemas, canonicalization, verifier, facade | единая digest/signature chain |
| `src/Strogo.Modules.Cli/**` | check/build/run/explain | agent/runtime workflow |
| `src/Strogo.Modules.Owner.Cli/**` | approve-contract/admit | отдельные owner actions |
| `examples/modules-v0.2/DotNetConsumer/**` | отдельный facade consumer | доказать host API вне CLI |
| `tests/Strogo.Modules.Conformance/**` | lifecycle/tamper/expiry/epoch/rollback | A-AC1–A-AC9 и исходные E05 AC1–AC9 |
| `fixtures/modules-v0.2/**`, `docs/fixtures/modules-v0.2/**` | valid/invalid approval, package tree, loader и rollback fixtures | воспроизводимые contract examples |
| `Kernel.slnx`, `src/**.csproj`, `tests/**.csproj`, `examples/**.csproj`, `packages.lock.json` | подключение проектов и только необходимые pinned dependencies | воспроизводимая сборка |
| `docs/**`, `artifacts/e05/**` | projection, knowledge, evidence | auditability |

`host-owned.json`, `operator-owned.json`, encrypted private key и production `owner-state-store` документируются и создаются оператором вне repository EXEC; implementation не записывает реальные operator secrets/config в repository.

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Human approval | один циклический artifact | semantic contract approval + post-build admission |
| `check` | требует future proof/build digests | требует только bundle-bound contract approval |
| Package | неясно включает ли approval | immutable и не включает внешний admission |
| Runtime | package + bundle digest | package + admission + current owner state + expected bundle |
| Owner action | Не определён | encrypted explicit signer + TTY exact-digest confirmation + explicit provenance/expiry |
| Revocation | неясный counter | monotonic epoch + live high-water + persisted state store в TCB |
| Package layout | не определён | closed root/tree, complete manifest и restricted load context |

## 18. Альтернативы и компромиссы
- Один post-build approval: проще, но позволяет agent разрабатывать против собственного неподтверждённого формального смысла.
- Один pre-proof approval и автоматический admission: меньше действий, но человек не утверждает exact executable/toolchain bytes, что расходится с исходной E05 цепочкой.
- Approval внутри package: создаёт новый digest/signature cycle либо требует mutable package.
- Выбран двухэтапный внешний admission: различает два решения и сохраняет ацикличные immutable identities ценой второго human action.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Scope, outcomes и non-goals заданы |
| B. Качество дизайна | 6-10 | PASS | Artifact chain и state transitions ацикличны |
| C. Безопасность изменений | 11-13 | PASS | Fail-closed, threat boundary и rollback раскрыты |
| D. Проверяемость | 14-16 | PASS | A-AC1–A-AC9 имеют evidence |
| E. Готовность к автономной реализации | 17-19 | PASS | Порядок и file scope заданы |
| F. Соответствие профилю | 20 | PASS | Security/workflow matrices заполнены |

Итог: ГОТОВО К ПОДТВЕРЖДЕНИЮ.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Исправляется один cycle defect |
| 2. Понимание текущего состояния | 5 | Противоречащие требования названы |
| 3. Конкретность целевого дизайна | 5 | Schemas, commands и verification order заданы |
| 4. Безопасность | 5 | Revocation, expiry, tamper и rollback заданы |
| 5. Тестируемость | 5 | Полная AC matrix |
| 6. Готовность к автономной реализации | 5 | Этапы независимы и bounded |

Итоговый балл: **30 / 30**. Зона: готово к автономному выполнению после approval.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Разделены ли два человеческих решения? | PASS | Нет |
| UX / designer | not applicable | UI отсутствует; понятна ли projection? | PASS | Полные digests обязательны |
| Tester / validation | applicable | Различают ли tests реальный tamper/expiry/revoke? | PASS | Нет |
| Developer / architect | applicable | Нет ли hash/signature cycle? | PASS | Admission вынесен из package |
| Delivery / operations / security | applicable | Честна ли threat/revocation boundary? | PASS | Нет |

### Post-SPEC Review
- Статус: PASS после независимого повторного прохода; открытых BLOCKER/HIGH/MEDIUM findings нет.
- Reviewer: `/root/e05_admission_spec_review`, роль `independent-reviewer`; procedural read-only review в фактически выданном sandbox `danger-full-access`, без записей в workspace.
- Reviewed content snapshot: SHA-256 `7114B30C3190F803B4BFA7DFDC23C22D67A6B87C0C18DA4FBC70373F4D326912`; после PASS изменена только эта review record.
- Scope reviewed: эта поправка, E05 §6.2/6.4/plan, K-E05-061, planned admission files.
- Evidence inspected: утверждённый E05 pipeline и обязательные поля approval; текущий repo не содержит E05 admission code.
- Depth checklist: scope ограничен admission; AC проверяют outputs; unsupported human-identity claim исключён; regressions обязательны; hidden contract change требует нового approval.
- Review decision: можно запрашивать точную фразу approval; EXEC обязан подтвердить byte-equal clean replay, иначе сработает stop condition.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | design | Package, включающий собственный admission, создавал бы новый cycle | Держать admission внешним | fixed |
| HIGH | security | Произвольный owner-state path был self-selected trust root | Pin owner public key в host configuration | fixed |
| HIGH | runtime | Hash path до повторного open позволял concurrent replacement | Удерживать no-write/no-delete handles до load | fixed |
| MEDIUM | security | Неясный revocation counter | Задать общий monotonic epoch | fixed |
| HIGH | workflow | `check` не имел trust/state inputs и мог принять forged approval | Требовать host trust verification до prover | fixed |
| HIGH | owner action | Signer, validity и отказ человека были недоопределены | Задать encrypted signer config, explicit inputs и TTY digest confirmation | fixed |
| LOW | evidence | Подпись могла быть названа proof человеческой идентичности | Ограничить claim key possession/interface action | fixed |
| HIGH | security | Старый корректно подписанный owner state можно replay после restart | Process high-water; state store явно включить в TCB; не заявлять cryptographic persisted rollback protection | fixed, re-reviewed |
| HIGH | package/runtime | Не были закрыты package tree, extras/path collisions и loader closure | Нормативно задать exact tree/path grammar/roles и restricted load context | fixed, re-reviewed |
| MEDIUM | rollback | После epoch advance старый contract approval тоже отозван | Требовать оба новых human gates и полный rebuild перед pointer CAS | fixed, re-reviewed |
| MEDIUM | crypto contract | Domain framing, RSA-PSS parameters, schemas и payload/artifact digests были неоднозначны | Зафиксировать literals, encodings, closed schemas и signature input | fixed, re-reviewed |
| MEDIUM | quality gate | File table, AC range и review record отставали от design | Обновить A-AC1–A-AC9, полный file scope и фактический review status | fixed, re-reviewed |
| HIGH | stage authenticity | `build --check` не мог заново проверить unsigned candidate-writable check-dir против current trust/state | Добавить `--trust-config` и повторить approval gate до verifier replay | fixed, re-reviewed |
| MEDIUM | verifier determinism | Raw solver transcript может содержать timing/path/text drift и ломать честный replay | Ввести closed normalized transcript; raw diagnostics исключить из package/digests; два clean replay в AC | fixed, re-reviewed |

- Fixed before PASS: persisted-state boundary, live high-water, exact wire contract, closed package tree, dependency closure, rollback sequence, stage authenticity, normalized transcript, AC/file tables.
- Checks rerun: `git diff --check`, targeted ambiguity/placeholder scan, manual contract/state/adversarial review и independent re-review.
- Needs human: точная фраза approval.
- Residual risks / follow-ups: production key isolation, persisted state rollback outside trusted store, точечный revoke, trusted time.

### Post-EXEC Review
- Статус: Не выполнен до EXEC.

## Approval
Ожидается фраза: **«Спеку подтверждаю»**.

Подтверждение распространяется только на эту поправку и локальные checkpoint commits. Push, merge, release и публикация не разрешаются.

## 20. Журнал действий агента

| Фаза | Тип | Уверенность | Не хватает | Следующее действие | Нужен человек | Фактическое решение | Объяснение | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Найден cycle | 0.99 | Нет | Развести approvals | Нет | Нет | Текущий порядок невыполним | E05, K-E05-061 |
| SPEC | Design | 0.94 | Human approval | Запросить exact phrase | Да | Ещё нет | Два gates сохраняют смысл и exact package | эта SPEC |
