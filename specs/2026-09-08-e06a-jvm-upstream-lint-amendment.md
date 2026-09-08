# E06A: baseline-gated lint для Dafny Java output

## 0. Метаданные

- Тип (профиль): `delivery-task` + `product-system-design`; Expanded SPEC, потому что меняется публичный target build/evidence contract.
- Владелец: человек подтверждает изменение нормы; агент реализует и собирает evidence.
- Масштаб: medium.
- Целевое семейство / behavior baseline: Strogo E06 `jvm-java17.v1`.
- Поверхность: Codex, локальный Windows host и Ubuntu 24.04 WSL2.
- Effective runtime: не влияет на javac artifact; фактические tool/runtime identities фиксируются в receipts.
- Eval baseline / evidence: clean E06 source/package, Dafny `4.11.0`, Zulu OpenJDK `17.0.19`; old normative probe дал exit `1`, `67 rawtypes + 3 varargs + 1 serial` вне разрешённого `cast`; первый full probe без `-Xmaxwarns` был обрезан javac после 100 из 106 warnings, exact full probe с `-Xmaxwarns 10000` дал `35 cast + 67 rawtypes + 1 serial + 3 varargs`.
- Целевой релиз / ветка: `main`, отдельные Conventional Commits и периодический push по разрешению владельца.
- Ограничения: применима только к translator-owned Java sources фиксированного E06 validation fixture; adapter и consumer не получают suppressions.
- Связанные ссылки: E06 §6.2.2 и K-E06-056.
- Instruction stack: central `creator-vibe-lens` (creative trigger не сработал), `model-behavior-baseline`, `quest-governance`, `collaboration-baseline`, `testing-baseline`, `tool-execution-baseline`, `quest-mode`, `spec-linter`, `spec-rubric`, `review-loops`, `product-system-design`, commit/GitHub delivery policies; локального `AGENTS.override.md` нет.

## 1. Overview / Цель

Исправить фактически невыполнимую JVM lint-норму E06, не превращая исключения javac в слепую зону.

Outcome contract:

- Success means: неизменённый output pinned Dafny проходит двухфазный gate: canonical normalized representation полного unsuppressed diagnostic output совпадает с утверждённым baseline, затем compilation с закрытым набором upstream-only lint exclusions завершается без diagnostics; adapter/consumer отдельно проходят полный lint.
- Итоговый артефакт / output: tracked canonical warning baseline, validator/build driver, positive и mutation evidence. Только после PASS разрешено продолжить canonical JAR build.
- Stop rules: error javac, drift tool/source/command/baseline, любое новое/исчезнувшее/изменённое warning, diagnostic после phase 2, suppression в adapter/consumer либо promoted candidate/adapter/JAR output до соответствующего полного gate дают `TargetBuildRejected`; quarantined phase-1 diagnostics/classes разрешены только как disposable evidence/intermediate, JAR/package не создаются.

## 2. Текущее состояние (AS-IS)

- E06 требует для translator inventory `javac --release 17 -encoding UTF-8 -Xlint:all,-cast -Werror` и разрешает только `cast`.
- Exact Dafny `4.11.0` Java translation фиксированного verified source создаёт корректно компилируемый source tree, но normative javac завершает его с `71` warning: `67 rawtypes`, `3 varargs`, `1 serial`.
- Локальная feasibility-компиляция с ослабленным upstream lint позволила отдельно проверить новый Java adapter/consumer: строгий adapter/consumer compile прошёл, consumer дал `8 exact + 24 transport + 1 escaped-surrogate + 13 owner vectors`.
- Текущая JVM row правильно остаётся `TargetBuildRejected`; JAR, package и portability status не созданы.

## 3. Проблема

Текущая норма принимает отсутствие warning в upstream output за свойство pinned Dafny translator, которого фактически нет. Простое добавление `-rawtypes,-varargs,-serial` разблокирует compiler, но скроет новые предупреждения тех же категорий. Нужно принять только уже известный exact diagnostic fingerprint фиксированного translator output.

## 4. Цели дизайна

- Сохранить fail-closed реакцию на любой drift upstream diagnostics.
- Отделить translator-owned warning baseline от полностью строгих Strogo adapter/consumer sources.
- Связать baseline с exact tool, source inventory, logical command и locale.
- Дать воспроизводимое объяснение, почему конкретные exclusions существуют.
- Не менять logical semantics, public ABI, owner proof или JVM package format.

## 5. Non-Goals

- Не обновлять и не патчить Dafny/JDK.
- Не утверждать, что upstream warnings безопасны вообще или для другого source/module.
- Не добавлять suppressions/annotations в generated sources, adapter или consumer.
- Не реализовывать в этой поправке JAR normalizer, runtime closure, mutations semantic output, performance или HotSpot A12.
- Не присваивать `Portable` и не создавать production admission.

### 5.1 Нормативное supersession основной E06

После approval эта поправка заменяет только JVM warning/build части основной E06:

- §2 pre-approval claim о единственном generated `cast` warning применяется только к старому TaskGraph feasibility source и не является baseline текущего fixed portability fixture;
- §6.2.2 предложение с exact `-Xlint:all,-cast -Werror` заменяется двухфазными §6.2.2–6.2.3 этой поправки;
- §6.5 Decision Ledger row `JVM packaging` теперь означает «separate strict adapter/consumer lint + baseline-gated closed generated exclusions `{cast,rawtypes,varargs,serial}` + pinned deterministic STORED repack»;
- основная E06 A3 и её Acceptance-to-Test row заменяются replacement A3 ниже;
- §12 запрет расширять warning exclusions сохраняется для любых категорий/источников вне exact baseline-gated supersession этой поправки.

**Replacement E06 A3:** .NET clause остаётся без изменения. JVM generated sources проходят exact full-lint baseline gate этой поправки и затем zero-diagnostic closed-exclusion compile; adapter/consumer проходят отдельный `-Xlint:all -Werror`; baseline, file/dependency/tool/argfile/command inventories и hashes полны; generated exclusions в adapter/consumer и незапланированное расширение category set отклоняются.

**Replacement E06 A3 test/evidence:** two-root full-lint baseline comparison, positive phase-2 compile, strict adapter/consumer compile, diagnostic/source/category/identity/process/cleanup mutations; evidence — baseline, build/negative receipts и bounded logs.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `targets/jvm-java17-v1/upstream-warning-baseline.json` — canonical expected identity и exact warning multiset/digest для fixed fixture; exact artifact digest входит в `buildToolchainDigest` и через него в portability manifest.
- JVM build driver — isolated translation, source inventory, phase-1 capture/validation, phase-2 compile и fail-closed cleanup.
- warning parser/validator в `src/Strogo.Modules.Portability` — canonical schema, closed categories/counts, logical paths и digest comparison.
- portability conformance — baseline/parser/mutation/command-boundary checks.
- Java adapter/consumer — отдельные строгие compilation units без upstream exclusions.
- `docs/knowledge-log.md` и E06 journal — подтверждения, опровержения и evidence boundary.

### 6.2 Детальный дизайн

#### 6.2.1 Identity и deterministic diagnostics

Authoritative Windows x64 lane запускает javac из isolated staging root. `@translator-sources.argfile` содержит только ordinal-sorted relative `/` paths translator inventory. Physical paths и locale-dependent текст запрещены.

VM flags фиксируют diagnostics:

```text
-J-Duser.language=en
-J-Duser.country=US
-J-Dfile.encoding=UTF-8
```

Обе фазы используют exact `javac 17.0.19`, `--release 17`, `-encoding UTF-8`, `-proc:none`, `-implicit:none`, `-Xmaxwarns 10000`, явные empty `--class-path`/`--source-path`, один source argfile и новый empty output directory. `CLASSPATH`, `JDK_JAVAC_OPTIONS`, `JDK_JAVA_OPTIONS`, `JAVA_TOOL_OPTIONS` и `_JAVA_OPTIONS` очищаются. Tool executable/closure, argfile bytes, translated source inventory и logical flags входят в `buildToolchainDigest`.

#### 6.2.2 Phase 1: exact unsuppressed baseline

Phase 1 запускается без `-Werror`:

```text
javac <fixed -J flags> --release 17 -encoding UTF-8 \
  -proc:none -implicit:none --class-path empty-classpath \
  --source-path empty-sourcepath -Xlint:all -Xmaxwarns 10000 \
  -d probe-classes @translator-sources.argfile
```

Exit обязан быть `0`. Stdout обязан быть пуст. Stderr декодируется strict UTF-8 и обязан оканчиваться переводом строки; `CRLF`/`CR` нормализуются в `LF`, а canonical stream имеет ровно один terminal `LF`. NUL, absolute/drive/UNC path и invalid diagnostic grammar запрещены. Относительный Windows separator `\` только в primary diagnostic path после path-safety проверки канонизируется в `/`; context bytes не подвергаются path conversion.

Parser обязан исчерпывающе потребить весь normalized stream как ноль или больше warning blocks и ровно одну terminal summary line `<N> warning` либо `<N> warnings`. Каждый block начинается primary line `<relative-path>:<line>: warning: [<category>] <message>`; следующие source/caret/continuation lines до следующей primary либо summary line сохраняются byte-exact после newline normalization как `context` и связываются `contextDigest`. Parser не интерпретирует context, но не разрешает отбросить, переставить или добавить ни одного байта. Число blocks обязано совпасть с `N`. Primary diagnostic сохраняется как `path,line,column,category,message,contextDigest`, где отсутствующая у javac column кодируется строкой `"0"`, а path обязан входить в translator inventory.

Baseline schema `strogo.jvm-upstream-warning-baseline.v0.1` закрыта и содержит ровно:

```text
schemaVersion, profileId, fixtureModuleDigest, dafnySourceDigest,
translatorDigest, javacClosureDigest, sourceInventoryDigest,
probeCommandDigest, validatorDigest, harnessDigest,
normalizedDiagnosticsDigest, categories(category,count),
warnings(path,line,column,category,message,contextDigest)
```

Используется E06 `H(tag,bytes)`: `normalizedDiagnosticsDigest = H("strogo.jvm-upstream-warning-baseline.v0.1/diagnostics", canonical normalized stderr)`, `sourceInventoryDigest = H("strogo.jvm-upstream-warning-baseline.v0.1/sources", canonical source inventory)`, `probeCommandDigest = H("strogo.jvm-upstream-warning-baseline.v0.1/command", canonical logical args + declared-clean environment)`. `validatorDigest` и `harnessDigest` связывают exact source/version валидатора и orchestration driver. Receipt связывает `baselineDigest = H("strogo.jvm-upstream-warning-baseline.v0.1/artifact", exact canonical baseline bytes)`, поэтому baseline не содержит рекурсивный self-digest.

Owner-approved `baselineDigest` передаётся build как внешний expected input; self-generated либо вычисленный из рабочего дерева expected digest запрещён. `baselineDigest`, `validatorDigest` и `harnessDigest` являются обязательными inputs canonical `buildToolchainDigest`, а существующий manifest field связывает их с artifact/package identity. Coordinated baseline+validator/harness rewrite без нового owner-approved expected digest обязан завершиться mismatch, даже если изменённые компоненты внутренне согласованы.

`categories` и `warnings` ordinal-sorted. Full-lint probe обязан содержать exact `cast:35`, `rawtypes:67`, `serial:1`, `varargs:3`, всего `106`; любая иная категория/count останавливает EXEC как новый spec finding. `-Xmaxwarns 10000` является частью command identity и исключает silent truncation; truncation line либо несовпадение summary count отклоняются grammar. Exact warning list/digest сначала вычисляются независимо в двух clean roots и обязаны быть byte-equal. Кандидат и receipts показываются владельцу; только фраза **«Baseline подтверждаю»** закрепляет exact `baselineDigest` в разделе `Approved baseline snapshot` и разрешает tracked baseline/phase 2. Future baseline change требует отдельной SPEC/owner approval, а не regeneration внутри build. Несовпадение любого поля — `TargetBuildRejected / UpstreamWarningBaselineMismatch` до phase 2 и до JAR output.

`probe-classes` является quarantined disposable local intermediate и никогда не входит в phase 2 classpath, package, identity или evidence archive. После capture при любом exit driver удаляет только проверенный resolved path внутри нового run root и подтверждает отсутствие directory до следующего stage. Cleanup failure даёт `TargetBuildRejected/ProbeCleanupFailed`; phase 2 не запускается. Partial probe files не считаются candidate artifact и не могут остаться accepted run output.

#### 6.2.3 Phase 2: closed upstream-only exclusions

Только после exact baseline PASS выполняется normative compilation:

```text
javac <fixed -J flags> --release 17 -encoding UTF-8 \
  -proc:none -implicit:none --class-path empty-classpath \
  --source-path empty-sourcepath \
  -Xlint:all,-cast,-rawtypes,-varargs,-serial -Xmaxwarns 10000 -Werror \
  -d candidate-classes @translator-sources.argfile
```

Exit обязан быть `0`, stdout/stderr — пустыми. Allowed exclusion set закрыт в schema/code и равен только `cast,rawtypes,varargs,serial`. Он не выводится автоматически из свежего stderr. Изменение translator/source/JDK требует новой human-approved baseline revision либо translator, проходящего более строгий gate. Phase-2 javac пишет только в unique quarantined directory; candidate classes атомарно продвигаются в новый ранее отсутствующий final directory лишь после полного PASS. При failure quarantine удаляется с тем же containment/absence gate, что и probe.

Classpath contract разделён и ordinal inventories входят в command/build identities: upstream phases используют только explicit empty classpath/sourcepath; adapter compile использует только exact promoted `candidate-classes`; pre-JAR consumer compile/run этой поправки использует только exact promoted `candidate-classes + adapter-classes`. Future E06 A14 consumer по-прежнему использует только packaged final JAR. Adapter и consumer компилируются отдельными invocations с `-proc:none -implicit:none -Xlint:all -Werror`, без `-Xlint:-...`, `@SuppressWarnings`, annotation processors, external dependencies или environment-injected javac options. Очищенные variables из §6.2.1 фиксируются как absent в receipt. Adapter classes также компилируются в quarantine и атомарно продвигаются только после PASS; consumer output является disposable quarantine и удаляется после clean process/cleanup PASS, иначе residual path фиксируется и run остаётся rejected.

#### 6.2.4 Ошибки и output discipline

| Условие | Result / reason | Artifact output |
| --- | --- | --- |
| Phase-1 javac error/nonzero | `TargetBuildRejected/JavacProbeFailed` | Нет classes/JAR receipt |
| Baseline identity/list/digest drift | `TargetBuildRejected/UpstreamWarningBaselineMismatch` | Probe classes только disposable local intermediate; нет candidate classes/JAR |
| Unknown warning category/path/grammar | `TargetBuildRejected/UnexpectedUpstreamDiagnostic` | Нет phase-2/JAR output |
| Probe output не удалён/reappeared | `TargetBuildRejected/ProbeCleanupFailed` | Нет phase-2/JAR output |
| Phase-2 diagnostic/nonzero | `TargetBuildRejected/UpstreamCompilationFailed` | Quarantine удалён; нет promoted candidate/JAR output |
| Adapter diagnostic/nonzero | `TargetBuildRejected/AdapterCompilationFailed` | Quarantine удалён; нет promoted adapter/JAR output |
| Consumer diagnostic/nonzero | `TargetBuildRejected/ConsumerCompilationFailed` | JVM row не Passed |
| Timeout/output overflow | Соответствующий typed process reason | Promotion запрещён; quarantine cleanup обязателен до следующего stage |
| Process cleanup failure / живой PID | `JavacProcessCleanupFailed` либо `ConsumerProcessCleanupFailed` | Promotion запрещён; attempted cleanup и residual PID/paths фиксируются, run требует внешнего разбора |

Failed run сохраняет bounded local diagnostic evidence отдельно от canonical output. Каждый javac process имеет wall deadline `180` секунд и независимый raw-byte limit `1 MiB` для stdout и `1 MiB` для stderr. Runner одновременно дренирует оба stream сырыми bytes. При чтении первого byte сверх limit (`actualAtDetection = max + 1`) он прекращает сохранять payload этого stream, но продолжает hashing/drain во время cleanup, фиксирует bounded prefix и digest всех фактически прочитанных bytes, немедленно завершает root process и всё известное descendant tree. Kill/reap получает отдельный deadline `10` секунд; после него runner прекращает ожидание, фиксирует known PID tree, stream digests/prefixes и residual paths, не продвигает output и возвращает cleanup failure. Timeout использует тот же bounded whole-tree cleanup. Typed reasons: `JavacTimeout`, `JavacOutputLimitExceeded(stream,actualAtDetection,max)` и `JavacProcessCleanupFailed`; process-cleanup failure имеет приоритет над исходным timeout/overflow и запрещает следующий stage.

Standalone consumer запускается pinned `java 17.0.19` из той же JDK closure с очищенными environment variables §6.2.1, exact classpath inventory `candidate-classes + adapter-classes + consumer-classes` в ordinal порядке и фиксированным main class. Consumer process использует те же `180` секунд, раздельные `1 MiB` raw stdout/stderr limits, concurrent drain и `10` секунд whole-tree kill/reap deadline. Его typed reasons — `ConsumerTimeout`, `ConsumerOutputLimitExceeded(stream,actualAtDetection,max)` и `ConsumerProcessCleanupFailed`; expected exact stdout сравнивается только после clean exit, empty stderr и process cleanup PASS.

Любой javac output directory сначала создаётся как unique quarantine под exact new run root. Перед recursive delete driver разрешает absolute paths и доказывает, что target является строгим descendant ожидаемого run root и не равен самому root; после удаления проверяет отсутствие target. Нарушение containment или оставшийся output даёт `ProbeCleanupFailed` для phase 1 либо `JavacProcessCleanupFailed` для остальных stages. Только successful phase-2/adapter output атомарно продвигается rename в заранее отсутствующий destination; partial output никогда не получает candidate identity.

#### 6.2.5 Performance и visual artifacts

Дополнительная probe compilation удваивает часть javac cost для fixed validation build; driver сохраняет duration обеих фаз как diagnostic observation. Это осознанная цена контроля warning drift, не runtime performance result. Visual planning artifact и UI video evidence не применимы: результат — CLI/JSON build gate без UI.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| S1 | Собрать fixed JVM target pinned tools | Baseline exact PASS, phase 2/adapter/consumer compile PASS; build может перейти к JAR stage | receipts, zero diagnostic logs | A1–A4 |
| S2 | Добавить/убрать/изменить известный upstream warning | Build останавливается до phase 2/JAR с `UpstreamWarningBaselineMismatch` | mutation receipt | A5 |
| S3 | Добавить rawtype/другой warning в adapter или consumer | Полный lint отклоняет Strogo source | strict compile negative | A6 |
| S4 | Сменить source/tool/command/locale | Baseline identity drift; reuse запрещён | identity mutation receipts | A7 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Translation absent/failed | lint gate | `TargetBuildRejected` | Baseline не читается | Dependency order fixed |
| Translation ready, baseline exact, probe cleanup confirmed | phase 2 | Candidate classes produced | Любой output/nonzero rejects | JAR ещё не создаётся этой поправкой |
| Translation ready, baseline differs | gate | Stop before phase 2 | Local bounded diagnostics retained | Нельзя auto-accept новый baseline |
| Candidate classes ready | adapter compile | Strict adapter classes | warning rejects | Upstream exclusions не наследуются |
| Concurrent run in same path | start | Reject existing run directory | Нет overwrite | Каждый run использует новый root |
| Two-root candidate baseline ready, owner anchor absent | phase 2 | `TargetBuildRejected/BaselineNotApproved` | Candidate/receipts доступны для review | Только owner phrase открывает phase 2 |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Как принять warnings pinned translator | agent | Exact baseline gate + closed upstream-only exclusions | 0.98 | Сложнее harness, но новый warning не скрывается | Нет; подтверждается всей SPEC |
| Как закрепить initial exact baseline | человек | После two-root candidate отдельной фразой `Baseline подтверждаю` | 0.99 | Self-generated expected digest мог бы узаконить согласованную ошибку validator+baseline | Да, после EXEC stage 1 и до phase 2 |
| Менять translator сейчас | agent | Нет; сохранить E06 tool identity | 0.95 | Новый translator расширит scope и повторит proof/toolchain qualification | Нет |
| Разрешать baseline для любого модуля | agent | Нет; только fixed E06 fixture | 0.99 | Generalization без corpus скроет warning drift | Нет |
| Ослабить adapter/consumer lint | agent | Нет | 0.99 | Смешает trusted Strogo code с upstream debt | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Upstream javac flags и связанные JVM acceptance clauses | E06 §6.2.2, §6.5, A3, A3 test/evidence, §12 | Две фазы и closed four-category phase-2 exclusions | Полный точный supersession задан только в §5.1 этой поправки | command/source scan + actual run |
| Warning identity | Отсутствует | Canonical tracked baseline | Новая schema v0.1, fixed fixture only | parser + two roots + mutations |
| Adapter/consumer lint | E06 §6.2.2 | Без изменения | Полностью совместимо | strict javac actual run |
| Package/JAR | E06 §6.2.2/6.2.4 | Без изменения | Gate только разрешает следующий stage | отсутствие JAR в amendment negative runs |

## 7. Бизнес-правила / Алгоритмы

1. `BaselinePassed := ownerApprovedExpectedDigest && exactIdentity && exactCategories && exactWarningList && exactNormalizedDigest && exactValidatorHarnessIdentity`.
2. `Phase2Allowed := TranslationPassed && BaselinePassed && ProbeCleanupPassed`.
3. Allowed upstream exclusions — immutable set `{cast,rawtypes,varargs,serial}`; runtime discovery запрещён.
4. `AdapterPassed` и `ConsumerPassed` требуют full lint и ноль diagnostics.
5. Любой failed prerequisite запрещает class/JAR output следующего stage; driver обязан попытаться удалить quarantined outputs в bounded cleanup, а candidate/adapter outputs продвигаются только атомарно после PASS. Cleanup failure фиксирует residual state и требует внешнего разбора, не заявляя удаления.
6. Initial baseline candidate не становится expected input без отдельного решения владельца, сохранённого вместе с exact digest.
7. Любой значимый вывод, новый warning либо refutation записывается в `docs/knowledge-log.md` после approval в EXEC.

## 8. Точки интеграции и триггеры

- JVM build driver вызывает warning validator сразу после Dafny translation/source inventory и до normative compile.
- Phase 2 вызывается только по typed PASS validator result.
- Adapter compile вызывается после candidate classes, но отдельной командой/argfile.
- Future JAR stage принимает classes только из полного PASS receipt этой поправки.

## 9. Изменения модели данных / состояния

- Новый immutable canonical baseline JSON и typed parsed representation.
- Новый local build receipt содержит tool/source/command/baseline identities, phase outcomes, durations и diagnostic digests.
- Persisted mutable state, database и secrets отсутствуют.

## 10. Миграция / Rollout / Rollback

- До approval текущая E06 JVM row остаётся rejected по старому правилу.
- После EXEC stage 1 baseline candidate принимается только отдельным owner confirmation exact digest; старые/частичные receipts несовместимы и не мигрируют.
- Rollback: revert implementation/baseline commits; JVM row снова закономерно `TargetBuildRejected`, .NET artifacts и E06 proof не меняются.
- Не публиковать release/admission и не удалять negative evidence.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- **A1:** two clean physical roots создают byte-equal full-lint baseline candidate с exact `106` warnings (`cast:35`, `rawtypes:67`, `serial:1`, `varargs:3`) и без truncation; phase 2 требует сохранённый owner-approved expected `baselineDigest`.
- **A2:** phase-1 identity связывает pinned tool/source inventory/relative argfile/locale/command; physical paths отсутствуют.
- **A3:** phase 2 с closed exclusions и pinned inputs завершается exit `0`, empty stdout/stderr.
- **A4:** adapter и standalone consumer компилируются отдельным full-lint command; consumer запускается pinned Java runner с exact identity/environment/classpath/process gates и проходит `8+24+1+13` behavior fixture.
- **A5:** add/remove/change primary diagnostic, source/caret/continuation context, summary/count, `CRLF`, relative `\\` path и unconsumed-byte mutations проверяют canonical grammar; semantic drift даёт `UpstreamWarningBaselineMismatch`, unsafe/invalid grammar — `UnexpectedUpstreamDiagnostic`; actual source mutation отдельно даёт source identity mismatch до phase 2/JAR.
- **A6:** rawtypes mutation в adapter и diagnostic mutation в consumer отклоняются их full-lint commands.
- **A7:** tool/source/command/baseline/validator/harness identity mutations fail closed, включая coordinated baseline+validator rewrite против неизменного owner-approved expected digest; отдельные javac и consumer fixtures проверяют root+child timeout, stdout overflow и stderr overflow.
- **A8:** nonzero/mismatch/timeout/output-overflow runs после cleanable process termination удаляют quarantined outputs, подтверждают process-tree reap и не создают candidate/JAR output; injected cleanup-failure fixture возвращает typed reason, запрещает promotion и фиксирует residual PID/path без ложного утверждения об удалении.
- **A9:** solution/conformance проходят; knowledge log и E06 execution journal фиксируют exact result, supersession и ограничения.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| A1 | two-root full-lint baseline comparison + approved-digest gate | counts/list/digest/category-set и owner decision inspect | candidate/approved baseline + receipts | — |
| A2 | schema/path/identity mutations | scan physical paths | identity report | — |
| A3 | actual javac phase 2 | empty logs + exit | compile receipt/log digests | — |
| A4 | actual strict compile + pinned Java run | exact identity/classpath/environment/output | consumer receipt | — |
| A5 | primary/context/summary/newline/path/unconsumed-byte mutations плюс source mutation | verify exact typed reason and no candidate/JAR promotion | negative receipts | — |
| A6 | adapter/consumer mutations | inspect javac category | strict negative logs | — |
| A7 | digest/flag/coordinated rewrite + javac/consumer root+child timeout/stdout/stderr overflow mutations | typed reason and process absence inspect | negative receipts | — |
| A8 | nonzero/mismatch/timeout/overflow cleanup + injected cleanup failure | process-tree/output absence or explicit residual state | cleanup receipts | — |
| A9 | `dotnet build`, portability conformance, docs scan | git diff/status | conformance report | — |

Проверочные команды будут закреплены driver usage; minimum gate: PowerShell parse, Bash syntax если добавлен Linux wrapper, actual pinned javac commands, `dotnet build Kernel.slnx --no-restore`, portability conformance и SHA-256 evidence verification. Повторять run только для исправления конкретного defect/drift; после exact PASS остановить optional retesting.

## 12. Риски и edge cases

- javac diagnostic format может меняться: exact JDK closure/version и parser grammar делают смену явным отказом.
- Warning line может зависеть от source: baseline ограничен fixed fixture и source inventory digest.
- Suppressed category может скрыть новый warning phase 2: phase 1 exact list обязан пройти раньше.
- Locale/path могут сделать log невоспроизводимым: fixed JVM locale, relative argfile/CWD и path rejection.
- Phase-1 compilation cost: сохраняется diagnostic duration; fixed small fixture делает цену приемлемой.
- Compiler может оставить partial probe classes при failure: quarantine path проверяется внутри unique run root, удаляется и повторно проверяется до любого следующего stage; cleanup failure блокирует run.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Мы просто отключили warnings» | Это выглядело бы как ослабление исходной идеи | Unsuppressed exact baseline обязателен до exclusions; drift блокирует | mitigated |
| «Почему не обновить Dafny?» | Новый translator может уже исправить warnings | Это меняет proof/translation TCB; отдельный experiment после E06 | accepted-risk |
| «Baseline привяжет язык к одному примеру» | Exact список действительно fixture-specific | Поправка явно только для E06 fixed validation fixture | mitigated |
| «Adapter может получить такое же исключение» | Trusted transport code критичнее generated debt | Full lint отдельной командой, mutation A6 | mitigated |

### Rework Prevention Checklist

- User-visible output и stop states заданы в S1–S4.
- Все scenarios имеют evidence и AC.
- Agent-owned решения перечислены; отдельных открытых user decisions нет.
- Вероятные возражения закрыты либо обозначены как accepted scope risk.
- Role-based и independent review выполняются до approval.
- AC являются проверками результата.
- EXEC имеет exact positive/negative evidence path.

## 13. План выполнения

1. После approval SPEC реализовать closed baseline schema/parser/validator, process runner и conformance negatives.
2. Выполнить EXEC stage 1: deterministic phase-1 capture в двух clean roots, получить byte-equal candidate и заполнить `Approved baseline snapshot`; phase 2 остаётся заблокированной.
3. Передать владельцу exact digests/receipts; продолжить только после отдельной фразы **«Baseline подтверждаю»**.
4. Добавить tracked approved baseline, phase-2 compile и строгие adapter/consumer invocations.
5. Выполнить warning/identity/timeout/output/cleanup mutations и exact working-tree run; исправить findings, закоммитить/push implementation.
6. Повторить clean-public-commit run, сохранить filtered evidence/SHA-256 и обновить knowledge/journal.
7. Выполнить post-EXEC review; только затем продолжить JAR normalizer по основной E06.

## 14. Открытые вопросы

Нет. Initial baseline approval является обязательным stage gate с заранее определённой фразой и exact digest, а не открытым design question.

## 15. Соответствие профилю

- Профиль: `product-system-design` для изменения target build/evidence subsystem.
- Цели/non-goals, architecture boundaries, public ABI preservation, config/tool identities, failures, rollback и evidence определены.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Strogo.Modules.Portability/**` | baseline model/parser/validator | Typed fail-closed core |
| `targets/jvm-java17-v1/upstream-warning-baseline.json` | exact canonical baseline | Принять только известный output |
| `tools/Build-PortableJvm*` | two-phase javac orchestration | Фактический target gate |
| `tests/Strogo.Modules.Portability.Conformance/Program.cs` | positive/negative identity checks | Регрессия/мутации |
| `tests/fixtures/portability-consumers/java/**` | только при необходимости driver input | Strict consumer evidence |
| `docs/knowledge-log.md`, `specs/2026-09-07-e06-portable-execution-profiles-v0.1.md` | результаты, supersession и E06 journal | Обязательная база знаний |
| `artifacts/e06/**` | filtered exact evidence | Public reproducibility |

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| Upstream lint | Один impossible full-lint compile | Unsuppressed exact baseline + zero-diagnostic closed-exclusion compile |
| Новый warning разрешённой категории | Мог бы скрыться при простом exclusion | Baseline mismatch, build stopped |
| Adapter/consumer | Full lint | Full lint без изменения |
| Tool/source drift | Общий digest позднее | Gate до compilation/JAR |

## 18. Альтернативы и компромиссы

- Обновить Dafny: потенциально убирает warnings, но меняет verifier/translator TCB и требует повторной qualification; вынесено.
- Патчить generated Java: создаёт собственный source rewriter и дополнительную semantic TCB; отклонено.
- Просто добавить `-rawtypes,-varargs,-serial`: дешевле, но скрывает новые warnings тех же категорий; отклонено.
- `-Xlint:none` для upstream: теряет все compiler diagnostics; используется только в уже завершённой локальной feasibility-пробе и запрещён для evidence.
- Выбран exact baseline gate: сложнее и выполняет javac дважды, зато сохраняет наблюдаемость каждого warning при неизменном toolchain.

## 19. Результат quality gate и review

### SPEC Linter Result

| № | Статус | Проверяемое основание |
| ---: | --- | --- |
| 1 | PASS | Outcome S1–S4 и typed outputs заданы |
| 2 | PASS | AS-IS подтверждён actual javac/consumer runs |
| 3 | PASS | Корневая impossible lint-норма отделена от adapter |
| 4 | PASS | Strictness, identity, separation перечислены |
| 5 | PASS | Non-Goals исключают toolchain/JAR/semantic scope |
| 6 | PASS | Ответственности по artifacts/components заданы |
| 7 | PASS | Translation→probe→compile→adapter триггеры заданы |
| 8 | PASS | BaselinePassed/Phase2Allowed и closed set формальны |
| 9 | PASS | Typed reasons, no-output и rollback заданы |
| 10 | PASS | Дополнительная compile cost раскрыта |
| 11 | PASS | Baseline/receipt schema и immutability заданы |
| 12 | PASS | Supersedes только JVM lint row, old receipts не reused |
| 13 | PASS | Revert возвращает fail-closed JVM state |
| 14 | PASS | A1–A9 измеримы |
| 15 | PASS | Каждому AC соответствует test/evidence, включая mutations |
| 16 | PASS | Commands/deadlines/limits/stop rules заданы |
| 17 | PASS | Dependency-ordered plan до clean evidence задан |
| 18 | PASS | Решения закрыты, open questions отсутствуют |
| 19 | PASS | Expanded выбран из-за build/public evidence contract |
| 20 | PASS | Product-system-design requirements покрыты |

Итог: **ГОТОВО**, при условии PASS post-SPEC review ниже.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | Меняется один lint gate, non-goals жёсткие |
| 2. Понимание текущего состояния | 5 | Actual warning categories/counts и feasibility evidence |
| 3. Конкретность целевого дизайна | 5 | Две exact команды, schema, algorithm, failures |
| 4. Безопасность / rollback | 5 | No-output stages, isolated roots, revert path |
| 5. Тестируемость | 5 | Positive, drift, warning, process mutations |
| 6. Готовность к автономному выполнению | 5 | Stage 1 автономен после SPEC approval; phase 2 имеет точный owner gate и plan/file map/evidence |

Итоговый балл: **30 / 30**. Зона: **готово к автономному EXEC stage 1 после approval; phase 2 — после owner approval exact baseline**.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | not applicable | Бизнес workflow/state не меняются | PASS | Нет |
| UX / designer | not applicable | UI/visual artifact отсутствует, CLI states описаны | PASS | Нет |
| Tester / validation | applicable | Может ли mutation скрыться в excluded category? | PASS | Exact phase-1 baseline и A5/A6 |
| Developer / architect | applicable | Сохраняются ли ABI/TCB и граница generated/trusted code? | PASS | Раздельные invocations и Non-Goals |
| Delivery / operations / security | applicable | Воспроизводимы ли tool/locale/path/process limits? | PASS | Exact identities, locale, isolated roots, deadline/output limits |

### Post-SPEC Review

- Статус / stop decision: **PASS**; открытых `BLOCKER/HIGH/MEDIUM` нет.
- Scope reviewed: эта amendment SPEC, E06 §6.2.2, K-E06-056, actual normative javac log, strict adapter/consumer result, central QUEST/review/profile owners.
- Contract pass: draft сохраняет public ABI/proof/package semantics и меняет только upstream lint admission.
- Adversarial risk pass: проверены новый warning разрешённой категории, исчезнувший warning, locale/path drift, coordinated baseline+validator rewrite, partial classes, process tree и inheritance exclusions adapter/consumer.
- Role-Based pass: tester, architect и delivery применимы; business/UX неприменимы с причиной.
- Evidence inspected: old normative command — `71` primary warnings (`67/3/1`) и exit `1`; full lint без max override — truncation `100/106`; exact `-Xmaxwarns 10000` probe — `106` (`35/67/1/3`) и exit `0`; amended diagnostic command exit `0`/empty output; adapter/consumer `8+24+1+13` PASS; conformance `143`.
- Depth checklist: scope drift отсутствует; AC/evidence/scenarios/decisions/objections заполнены; unsupported portability/G06 claims запрещены; rollback/docs/knowledge defined; hidden contract change вынесено в explicit amendment.
- No-findings justification: snapshot SHA-256 `eb1c78423a6defdbc5b3f99cc1b92a5bb470d790684371c9426ece66d67cf155` повторно проверен по исправленным trust-anchor/classpath/cleanup/process/diagnostic gates; reviewer подтвердил, что все четыре остаточных MEDIUM закрыты сквозно.
- Review isolation: reviewer был запущен с ролью `independent-reviewer`, но effective sandbox оказался `danger-full-access`. Поэтому результат учитывается как отдельный procedural adversarial fallback без изменений файлов, а не как технически изолированный read-only review; read-only sandbox в текущей collaboration surface недоступен.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| BLOCKER | supersession | Основная E06 сохраняла конфликтующие A3/Decision Ledger/§12 формулировки | Явно заменить все JVM lint clauses с precedence | fixed in §5.1 |
| BLOCKER | review isolation | Reviewer sandbox был writable | Зафиксировать procedural fallback и residual risk; не заявлять technical independence | fixed by disclosure; residual risk accepted per fallback |
| HIGH | trust anchor | Baseline мог меняться вместе с validator и оставаться self-consistent | Внешний owner-approved expected digest; validator/harness входят в identity; coordinated rewrite negative | fixed in §6.2.2, A1/A7 |
| HIGH | classpath | Границы upstream/adapter/consumer classpath были неполны | Задать empty/candidate/adapter/final-JAR inventories | fixed in §6.2.3 |
| HIGH | partial output | javac мог оставить partial classes после failure | Quarantine, containment-checked cleanup и atomic promotion | fixed in §6.2.2–6.2.4, A8 |
| MEDIUM | process limits | Не были заданы byte accounting и whole-tree cleanup | Раздельные raw limits, concurrent drain, bounded evidence, kill/reap fixtures | fixed in §6.2.4, A7/A8 |
| MEDIUM | diagnostics | Byte-exact baseline конфликтовал с newline normalization и не покрывал context | Исчерпывающая grammar, canonical stream/contextDigest и mutations | fixed in §6.2.2, A5 |

- Fixed before continuing: все первоначальные `2 BLOCKER + 3 HIGH + 2 MEDIUM` и четыре follow-up `MEDIUM` закрыты; таблица сохраняет audit trail основных findings.
- Checks rerun: SPEC linter/rubric, локальная content consistency, `git diff --check` и procedural adversarial re-review snapshot `eb1c7842` — PASS.
- EXEC counterexample affected-gate review: substantive snapshot `52c973703b039719b5ec423ae4a84e1dba945373c7d8df8d495fd791893349d1` с `-Xmaxwarns 10000` и exact `106` warnings — PASS, открытых `BLOCKER/HIGH/MEDIUM` нет; применяется то же procedural fallback disclosure.
- Needs human: exact SPEC approval после review PASS; затем отдельный exact baseline approval между EXEC stages.
- Residual risks / follow-ups: fixture-specific baseline, double javac cost и отсутствие технически read-only reviewer sandbox; все границы явно раскрыты.

### Post-EXEC Review

- Stage 1 implementation: **PASS**, открытых `BLOCKER/HIGH/MEDIUM` нет. Reviewer проверил exact warnings/categories, two-lane equality, ordinal ordering, owner digest gate, coordinated rewrite, exact quarantine cleanup, bounded process tree, .NET runtime/binary identity, duration receipts и закрытый `JavacProbeFailed` outcome.
- Evidence boundary: review выполнен процедурно read-only, но effective sandbox был writable; reviewer не повторял команды. Agent-run validation: solution build `0/0`, portability conformance `198`, Modules `362`, Graph `141`, Reserve `29/29` и `10904` assertions.
- Stage 2 post-EXEC review: не выполнялся и остаётся заблокирован owner baseline approval.

## Approval

Владелец подтвердил SPEC фразой **«Спеку подтверждаю»** 2026-09-08; это разрешает только EXEC stage 1: validator/runner и построение two-root baseline candidate. Подтверждение не разрешает tracked baseline, phase 2, JAR или portability admission. Добавленный после первого probe `-Xmaxwarns 10000` закрывает обнаруженную truncation и не расширяет разрешённый outcome.

После stage 1 агент заполняет следующий snapshot и показывает evidence владельцу:

### Approved baseline snapshot

- Status: `CANDIDATE_READY`; approval всё ещё `PENDING`.
- Clean public revision: `d21d969ee4f0cdfcc942e741be04b2fb6e13011b`.
- Candidate `baselineDigest`: `cafa0caad40e22d1d2ff3803ea350dea95f01c99f099ada75b16ef228035c0e1`.
- `normalizedDiagnosticsDigest`: `c18b3cf91a4d2a47f022738a8b807a30463c5548847c1f6e7eed87571f369d2c`.
- `translatedSourceTreeDigest`: `3d49fd994bee96529ee9dae2ed9536809f8cfb9821d865b4066dd2c156947eed`.
- Tool identities: translator `009476ca03980eb958b868e5e5fcecdd92942c015c395348808117954d750d9f`; javac closure `ad854e59ffe9f18ede163356945a9cdd673cb057f09ac8ce898332166d0f7817`; .NET closure `f9a2d6269ebd2105f5151eb368913a2aaaeace34ac7b93a87e93a7529b82eff4`.
- Command/validator/harness identities: command `941eb81e1e2b31e687d5d74968487b197823ba8a337b58bffed41b37e9e7dedb`; validator `94e86850fb5e1e206da5b4974b7ede4a3f36bac8c6688162d39fe87d8cfa3402`; harness `2bf8736f8422e9a5936b68c657f99455a14bf8ad0caf8db37cd40f5e25ec296a`.
- Two-root receipts: `artifacts/e06/jvm-baseline-candidate-d21d969/lane-a-receipt.json` SHA-256 `2140d304e1d9b107e44b5d8f989a90c1a95fdff26f9a2f680dbeed948f158006`; `lane-b-receipt.json` SHA-256 `99a80035180a33abe28d56c9b45430b67bd4df0ebfebf5b2a81c40500cffef97`; retained report SHA-256 `9fc5f25ac95f4f8583d5ec62686fda5d1869a25904e271c860a9111b9221d373`.
- Exact candidate bytes: local untracked `artifacts/local-validation/e06/jvm-baseline-d21d969/baseline-candidate.json`, SHA-256 `5fd98abbbd75d3a38bca9ce3b3b205e0239d0b26ec6a9e44a239a700fcfe0007`; tracked baseline path отсутствует.
- Owner decision: ожидается фраза **«Baseline подтверждаю»** именно для candidate digest `cafa0caa…c0e1`; только она меняет status на `APPROVED` и разрешает EXEC stage 2.

## 20. Журнал действий агента

| Фаза | Намерение / сценарий | Уверенность | Не хватает | Следующее действие | Нужна передача человеку | Фактическое обращение / решение | Объяснение | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Исправить опровергнутую upstream lint норму без blind suppression | 0.99 | Exact owner-approved baseline ещё не существует | После approval выполнить только EXEC stage 1 и показать candidate digest | Да, после stage 1 | Procedural adversarial reviewer: первоначально 2 BLOCKER + 3 HIGH + 2 MEDIUM, затем 4 MEDIUM; final snapshot `eb1c7842` PASS | Exact unsuppressed baseline с внешним owner anchor сохраняет strictness сильнее простого exclusion; writable reviewer sandbox раскрыт как residual risk | Эта SPEC, K-E06-056 |
| EXEC→SPEC | Исключить скрытое обрезание full-lint diagnostics | 0.999 | Повторный review exact flag | Закрепить `-Xmaxwarns 10000`, exact `106` counts и truncation negative | Нет | Первый full probe exit `0`, но javac показал `only showing the first 100 warnings, of 106 total`; повтор с `-Xmaxwarns 10000` дал `35 cast + 67 rawtypes + 1 serial + 3 varargs`, terminal `106 warnings` | Без max override baseline не был бы исчерпывающим; flag является command identity и сужает blind spot | Local inspect receipts; future knowledge-log entry |
| EXEC Stage 1 | Построить approval-gated full-lint candidate на точном публичном commit | 0.999 | Решение владельца по exact digest | Показать candidate/receipts; не начинать phase 2 | Да | Clean `d21d969`: two lanes byte-equal, `106` warnings, baseline `cafa0caa…c0e1`, cleanup PASS; post-EXEC reviewer PASS | Candidate связан с source/tool/command/.NET/JDK/validator/harness identities; tracked baseline и phase 2 отсутствуют | `artifacts/e06/jvm-baseline-candidate-d21d969/**`, K-E06-057 |
