# E06B: canonical deterministic JAR normalizer v0.1

## 0. Метаданные
- Тип (профиль): product-system-design / portability build artifact
- Владелец: Strogo project owner
- Масштаб: medium
- Целевое семейство / behavior baseline: E06 `jvm-java17.v1`
- Поверхность: Codex / repository
- Effective runtime: Не применимо; deterministic build artifact contract
- Eval baseline / evidence: E06A Phase 2 evidence `artifacts/e06/jvm-phase2-d21d969/**`
- Целевой релиз / ветка: `main`
- Ограничения: validation-only; JAR не является production admission
- Связанные ссылки: E06 profile spec, E06A JVM lint amendment, `PortabilityPackage.cs`

## 1. Overview / Цель

Добавить отдельную стадию, которая принимает только полностью проверенные JVM class entries из E06A Phase 2, строит canonical JAR и проверяет его до публикации в E06 package. Нормализатор должен устранять nondeterminism упаковщика, отклонять опасные или неоднозначные entry names и сохранять provenance raw input → normalized JAR.

Outcome contract:
- Success means: одинаковый ordered набор class/resource bytes и одинаковые pinned packaging inputs дают byte-identical JAR; JAR проходит structural validator и его digest связан с package manifest.
- Итоговый артефакт / output: raw pre-normalization diagnostic inventory, canonical JAR, canonical entry inventory, package receipt.
- Stop rules: duplicate/case-fold duplicate/path violation, missing or extra entry, manifest mismatch, tool/flag drift, nonzero `jar`, diagnostics, timeout, output overflow, residual staging или overwrite existing output дают typed rejection; final JAR не публикуется.

## 2. Текущее состояние (AS-IS)

- E06A Phase 2 доказала zero-diagnostic upstream compilation, strict adapter/consumer compile и Java execution на exact approved baseline.
- `PortabilityPackage` уже строит canonical directory package и связывает `content/strogo-portable-v01.jar` с package manifest, но не создаёт сам JAR.
- Ранее feasibility probe показала, что обычный `jar` пишет разные whole-file hashes из-за entry timestamps, а pinned repack с manifest-first order, `--no-compress` и fixed timestamp дал byte-identical result.
- В репозитории нет typed JAR normalizer, canonical raw/final inventory или driver stage, который atomically promotes JAR.

## 3. Проблема

JVM class output уже может быть принят по lint и behavior gates, но упаковка через штатный `jar` остаётся источником timestamp/order/duplicate/path drift. Без отдельной deterministic стадии нельзя связать содержимое JAR, manifest и package identity воспроизводимым способом.

## 4. Цели дизайна

- Использовать штатный JDK `jar`, отвечая только за его pinned invocation и входной набор.
- Разделить raw inventory, normalization и final validation.
- Делать входной набор и manifest каноническими до запуска упаковщика.
- Запретить filesystem extraction как способ нормализации.
- Сохранить bounded diagnostics и возможность детерминированно воспроизвести результат.

## 5. Non-Goals

- Не менять Dafny translator, E06A baseline или upstream lint exclusions.
- Не добавлять runtime closure, HotSpot performance gate, JNI, reflection или service loading.
- Не объявлять JVM package portable/production-admissible.
- Не принимать JAR, созданный внешним или непроверенным процессом, только по его финальному SHA-256.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент | Ответственность |
| --- | --- |
| `JvmJarNormalizer` | validate entries, create manifest/argfile, invoke JDK `jar`, validate final bytes |
| `JvmJarInventory` | canonical ordered path/size/SHA-256 inventory with raw/final provenance |
| `JvmProcessRunner` | bounded concurrent stdout/stderr drain, timeout, tree cleanup |
| E06A driver | supply promoted candidate classes and exact tool identities |
| `PortabilityPackage` | consume final JAR as `entry-artifact` and bind package manifest |
| portability conformance | positive result and mutation/negative checks |

### 6.2 Детальный дизайн

#### Input and entry contract

1. Input is a new, previously absent staging root containing only promoted E06A candidate classes, adapter classes and explicitly declared runtime dependencies.
2. Every logical entry has `{path, role, bytes, sha256, length}`. Paths use `/`, are relative ASCII, contain no empty/`.`/`..` segments, backslash, colon, control, absolute, drive or UNC form.
3. Each segment matches `[A-Za-z0-9_$][A-Za-z0-9._$-]*`; `META-INF` is allowed only for the exact generated manifest and explicitly declared metadata.
4. Exact ordinal path duplicates and ordinal case-fold duplicates are rejected before invoking `jar`. Missing, extra or role-inconsistent entries are rejected.
5. Input inventory is retained as diagnostic evidence and its physical root is excluded from logical identities.

#### Canonical manifest and invocation

Canonical manifest bytes are UTF-8 without BOM and exactly:

```text
Manifest-Version: 1.0\r\n
Automatic-Module-Name: strogo.portable.v01\r\n
\r\n
```

The driver writes an argfile whose first line is `META-INF/MANIFEST.MF`, followed by each remaining entry exactly once in ordinal path order. The argfile contains only relative paths and no `-C` or physical paths. На pinned `jar 17.0.19` control probe показал: `--no-manifest` вместе с manifest в argfile сохраняет exact provided bytes; `--manifest` при одновременном `--no-manifest` не добавляет файл, если его нет в argfile; `--manifest` без `--no-manifest` создаёт дополнительный `META-INF/` и переписывает manifest. Поэтому canonical command намеренно использует только `--no-manifest`, а manifest входит как первый обычный argfile entry. The pinned invocation is:

```text
jar --create --file <quarantined-output> --no-compress \
  --date=1980-01-01T00:00:02Z --no-manifest \
  @entries.argfile
```

The exact executable, JDK closure digest, logical flags, manifest bytes digest and argfile inventory enter the build-toolchain identity. `jar` runs with staging root as current directory and a cleaned environment.

#### Promotion and validation

- `jar` writes only to a unique quarantine output under the run root.
- Exit `0`, empty stdout/stderr, clean process tree and exact expected inventory are required.
- Validator opens the JAR as a ZIP reader without extraction and checks one central-directory entry per logical path, method `STORED`, fixed timestamp, UTF-8 flag, manifest-first ordering and exact bytes.
- Для каждого central-directory record validator сверяет соответствующий local-file header: name, flags, method, timestamp fields, CRC-32, compressed/uncompressed sizes and data offset. Проверяются границы `local header + data`, отсутствие overlapping records, корректные central-directory offset/size и отсутствие trailing bytes после end record.
- Feasibility fixture previously had `105` regular entries, but E06A Phase 2 currently produces a separate approved class inventory; final integration must bind the exact approved inventory count rather than assume `105`. For that bound count validator requires `version made by=10`, `version needed=10`, `create system=0`, zero volume/internal/external attributes, no archive/entry comments, no data descriptors, encryption or ZIP64; единственный extra field — JAR marker `FE CA 00 00` у manifest, остальные extra fields запрещены.
- The validator rejects duplicate raw ZIP names, duplicate case-fold names, directory entries, unexpected compression, extra metadata, unsafe paths and a mismatch between raw input inventory and final inventory.
- Phase 1 проверяет headers, multiplicity, ranges and limits before any map/extraction; phase 2 streams each raw entry from its verified local range and recomputes exact content digest/length/CRC. Entry count above `4096`, entry bytes above `16777216` or aggregate bytes above `67108864` are typed rejections.
- After successful validation, the JAR is atomically renamed to a new absent final path. Existing final paths are never overwritten.
- Failure removes quarantine with containment and absence checks. Cleanup failure preserves bounded residual evidence and blocks promotion.

#### Provenance

The receipt records schema version, profile, source/revision identity, candidate/adapter/runtime inventories, manifest digest, argfile digest, JDK/jar closure digest, raw JAR digest, final JAR digest, ordered central-directory inventory and process outcome. Physical paths, timestamps, PIDs and raw logs are diagnostic only and excluded from semantic package identity.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| S1 | Build approved JVM profile | Canonical JAR and receipt are produced | report, inventory, SHA-256 | A1–A4 |
| S2 | Repeat build with same logical inputs | JAR bytes and digest are identical | two-run comparison | A1 |
| S3 | Add duplicate or unsafe entry | Build stops before final JAR | typed rejection and no final path | A5 |
| S4 | Change timestamp/order/compression input | Validator rejects or canonical output remains identical only when logical inputs are identical | mutation receipt | A2, A5 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Phase 2 classes absent | normalize | reject before `jar` | no output | E06A prerequisite |
| Input inventory valid | invoke | quarantine JAR | existing run root rejects | unique run root |
| `jar` passed | validate | final JAR promotion | any mismatch rejects | no overwrite |
| final path exists | promote | reject | old artifact unchanged | atomic no-overwrite |
| cleanup fails | any failure | rejected with residual evidence | no promotion | external diagnosis required |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| JAR implementation | agent | invoke pinned JDK `jar`, own deterministic inputs/validation | 0.95 | validator becomes additional TCB | Нет |
| Compression | agent | `STORED` / `--no-compress` for v0.1 | 0.90 | larger artifact | Нет |
| Manifest | agent | exact fixed two-line manifest | 0.98 | future metadata requires schema revision | Нет |
| Runtime dependencies | agent | all E06A Phase 2 candidate and adapter classes; no external/discovered dependency | 0.95 | future runtime expansion needs a new identity | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| JVM profile artifact | E06 `jvm-java17.v1` | final JAR built by normalizer | existing .NET package unchanged | package conformance |
| JAR identity | absent | `strogo.jvm-jar.v0.1` receipt/inventory | new validation-only artifact | canonical digest checks |
| Package entry artifact | `PortabilityPackage.cs` | consume normalized JAR | manifest schema unchanged | package build/validate |

## 7. Бизнес-правила / Алгоритмы

1. `NormalizeAllowed := Phase2Passed && AdapterPassed && InputInventoryValid && ToolIdentityApproved`.
2. `JarDigest` depends only on canonical manifest bytes, ordered entry bytes and pinned jar contract; physical paths and process observations are excluded.
3. Any raw/final inventory mismatch is a hard rejection.
4. No final path is created before complete validation; no existing path is overwritten.
5. Any changed runtime dependency or packaging flag requires a new identity and review.

## 8. Точки интеграции и триггеры

- E06 JVM build driver calls the normalizer after E06A Phase 2 and strict adapter compile.
- `PortabilityPackage.Build` receives the final JAR bytes as the single `entry-artifact`.
- Conformance invokes normalizer, validator and package consumer in sequence.

## 9. Изменения модели данных / состояния

- New immutable `JvmJarEntry`, `JvmJarInventory`, `JvmJarBuildReceipt` types.
- No database or persisted mutable state.
- Evidence is filesystem output under a unique run root; final package remains validation-only.

## 10. Миграция / Rollout / Rollback

- Existing .NET package path is unchanged.
- JVM row remains rejected until this SPEC is approved and the complete gate passes.
- Rollback removes the normalizer integration; E06A baseline and evidence remain valid.

## 11. Тестирование и критерии приёмки

- **A1:** two clean runs with identical logical inputs produce byte-identical JAR and digest.
- **A2:** manifest, argfile, timestamp, ordering, compression, ZIP versions/attributes and extra-field policy are exact and reproducible.
- **A3:** final JAR opens without extraction, has consistent local/central ZIP records and matches ordered input inventory byte-for-byte.
- **A4:** package manifest binds the normalized JAR digest and standalone consumer reads it.
- **A5:** duplicate, case-fold, traversal, absolute, directory, compression, timestamp, extra-entry, local/central name/size/CRC/offset, ZIP64, descriptor, encryption and overwrite mutations fail closed with no promoted output.
- **A6:** timeout, stdout/stderr overflow, nonzero `jar` and cleanup failure preserve typed bounded evidence and never promote output.
- **A7:** solution, portability, Modules and Graph conformance remain green; knowledge log records the result and boundaries.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| A1 | two-run byte comparison | compare logical identities | `artifacts/e06/jvm-jar-*/report.json` | — |
| A2 | manifest/argfile/inventory assertions | inspect pinned command | receipt + inventory | — |
| A3 | ZIP central-directory and local-header validator | inspect no-extraction rule and boundary checks | final inventory | — |
| A4 | package build/validate + consumer | inspect digest binding | package report | — |
| A5 | mutation matrix | verify no final path | negative receipts | — |
| A6 | process/cleanup fault fixtures | inspect residual state | bounded logs | — |
| A7 | existing conformance suite | review docs and git diff | knowledge/spec journal | — |

## 12. Риски и edge cases

- JDK `jar` implementation may change metadata; pinned JDK closure and central-directory validation make drift explicit.
- ZIP readers may hide duplicate names; validator must inspect raw central-directory records.
- Case-fold collisions are rejected even though Java names are case-sensitive, preventing extraction/map ambiguity.
- STORED entries increase size; compression can be reconsidered only in a new schema/identity.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Зачем собственный нормализатор, если есть `jar`?» | штатный `jar` не фиксирует все входные решения | собственный слой отвечает только за inputs, flags и validation | mitigated |
| «Почему нельзя распаковать и собрать заново?» | extraction может схлопнуть duplicate entries | raw central-directory validation без extraction | mitigated |
| «Почему JAR ещё не portable?» | упаковка не доказывает runtime closure/platform coverage | validation-only и отдельные runtime/HotSpot gates | mitigated |

### Rework Prevention Checklist

- User-visible output and rejection states are defined.
- Every AC has test/evidence path.
- Runtime dependency set is explicit and fixed to E06A candidate + adapter classes.
- No production admission is implied.

## 13. План выполнения

1. После approval реализовать entry/inventory model and strict validator.
2. Добавить pinned `jar` invocation and quarantine/promotion flow.
3. Добавить positive, mutation and cleanup conformance fixtures.
4. Собрать exact JAR из E06A Phase 2 outputs and compare two clean runs.
5. Update E06 journal/knowledge log and commit/push evidence.
6. Остановиться перед runtime closure, HotSpot и portability admission.

## 14. Открытые вопросы

Нет. Для v0.1 runtime dependency set фиксирован как exact E06A Phase 2 candidate + adapter class entries; external/discovered dependencies запрещены.

## 15. Соответствие профилю

- Профиль: product-system-design / portability artifact.
- Выполненные требования профиля: deterministic contract, explicit failures, rollback, evidence and approval boundary описаны.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Strogo.Modules.Portability/JvmJarNormalizer.cs` | new model, validator, promotion | canonical JAR build |
| `tests/Strogo.Modules.Portability.Conformance/Program.cs` | positive/negative JAR checks | acceptance coverage |
| `tools/Build-PortableJvm*` | invoke normalizer after Phase 2 | driver integration |
| `artifacts/e06/jvm-jar-*` | filtered receipts/inventories | reproducibility evidence |
| `docs/knowledge-log.md` and E06 journal | result and boundaries | project knowledge requirement |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| JVM packaging | no canonical JAR stage | pinned JDK `jar` + strict validator |
| entry identity | implicit filesystem set | ordered path/content inventory |
| failures | possible partial archive | quarantine, typed rejection, no promotion |

## 18. Альтернативы и компромиссы

- Raw ZIP writer: deterministic, but adds archive-format implementation risk; rejected for v0.1.
- Unpack/repack: familiar, but can hide duplicates and metadata; rejected.
- Direct `jar` output: simple, but timestamps/order drift; rejected.
- Chosen pinned `jar` with explicit argfile (manifest as its first ordinary entry) and validator: keeps platform ownership in JDK while controlling the semantic input boundary.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель и границы определены |
| B. Качество дизайна | 6-10 | PASS | Потоки, identity и rollback описаны |
| C. Безопасность изменений | 11-13 | PASS | Negative gates и no-overwrite заданы |
| D. Проверяемость | 14-16 | PASS | AC связаны с evidence |
| E. Готовность к автономной реализации | 17-19 | PASS | Все v0.1 решения зафиксированы; owner approval остаётся единственным gate |
| F. Соответствие профилю | 20 | PASS | Product-system-design применён |

Итог: **ГОТОВО к owner review**

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does package workflow preserve validation-only intent? | PASS | — |
| UX / designer | not applicable | CLI/artifact-only change | PASS | — |
| Tester / validation | applicable | Are deterministic and negative gates testable? | PASS | — |
| Developer / architect | applicable | Are JAR/tool/identity boundaries coherent? | PASS | — |
| Delivery / operations / security | applicable | Are overwrite, cleanup and rollback bounded? | PASS | — |

### Post-SPEC Review

- Статус / stop decision: **PASS; owner approval получено, EXEC продолжается**.
- Findings: нет; runtime dependency set и packaging inputs зафиксированы в этой SPEC и не расширяются без новой identity.
- Manual-review challenge: подтвердить, что фиксированный manifest и `STORED` приемлемы для v0.1.

## Approval

Owner approval: **«Спеку подтверждаю»** получено 2026-09-09. Это разрешает только E06B EXEC в границах этой SPEC; runtime closure, HotSpot и portability admission остаются отдельными gate.

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Сформировать отдельный deterministic JAR gate после E06A Phase 2 | 0.95 | Нет для v0.1; external dependencies запрещены | Показать SPEC владельцу; ждать `Спеку подтверждаю` | Да | Нет | JDK остаётся ответственным за archive encoding, а Strogo контролирует inputs и validation | Этот файл |
| EXEC | Реализовать typed inventory, pinned `jar`, raw ZIP validator, quarantine/promotion и conformance mutations | 0.93 | Полная E06A integration и cross-platform JDK run остаются отдельными evidence | Провести post-EXEC review и обновить acceptance evidence | Нет | Да: «Спеку подтверждаю» 2026-09-09 | Implementation ограничена approved E06B; no-overwrite и raw local/central checks закрывают основные ambiguity seams | `JvmJarNormalizer.cs`, `JvmProcessRunner.cs`, portability conformance |

### Post-EXEC Review: checkpoint 1

- Статус: **PASS для реализованного checkpoint; финальное A6/A7 закрытие ещё не заявляется**.
- Scope/Evidence pass: просмотрены эта approved SPEC, `JvmJarNormalizer.cs`, `JvmProcessRunner.cs`, portability conformance diff, `git status`, Release build и запуск conformance.
- Contract pass: A1–A5 покрыты двух-run identity, exact manifest/metadata, input/final inventory, raw local/central checks, package entry-artifact binding и fail-closed mutations; Non-Goals сохранены.
- Adversarial risk pass: исправлены typed malformed ZIP boundary, очистка JDK option environment, final-parent reparse check, local manifest JAR marker и input size/count/aggregate limits. Дополнительный review обнаружил риск исключения из post-promotion cleanup; staging теперь удаляется и проверяется до atomic move, поэтому cleanup failure не может появиться после публикации final JAR. Повторная conformance проверка прошла.
- Role-Based pass: Tester/validation и Developer/architect применимы и пройдены; Delivery/operations/security проверены для quarantine/no-overwrite/reparse; UX не применим (artifact-only); business workflow не меняется.
- Fix and re-review: выполнено после первичного запуска, package integration и cleanup ordering; затронутые build/conformance checks повторены.
- Findings: нет открытых BLOCKER/HIGH/MEDIUM. Residual: A6 process/cleanup fault fixtures, full E06A integration и cross-platform evidence ещё требуют отдельного checkpoint.
- Validation evidence: `dotnet build tests/Strogo.Modules.Portability.Conformance/Strogo.Modules.Portability.Conformance.csproj -c Release --no-restore` — `0/0`; `dotnet run ... --no-build` — `PASS portability contract checks=251 valid=10 refusals=3 transport=24 mutations=4`; full solution Release build — `0/0`.
