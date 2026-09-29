# G02: .NET ReadyToRun backend для допущенных модулей Strogo

## 0. Метаданные

- Статус: рабочая SPEC; **не утверждена**. Ни код, ни инфраструктура G02 в этом этапе пока не меняются.
- Профиль: `delivery-task` / QUEST / `product-system-design`; масштаб large, multi-module, trust/admission и публичный ABI. Владелец смысла и допуска: Kibnet; проект SPEC: Codex.
- Задача Unlimotion `c390006e-dbe7-4823-868e-9e662ff59e61`, родительская цель `3295d388-0d24-45f8-b8be-b0eaf24e2cc5`; ветка `main`. Изменения только после exact approval этой ревизии.
- Instruction stack: central `AGENTS.md`, `creator-vibe-lens`, `model-behavior-baseline`, `tool-execution-baseline`, `collaboration-baseline`, `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `product-system-design`, `spec-linter`, `spec-rubric`, `review-loops` и локальный `AGENTS.md`. Шаблон — центральный `templates/specs/_template.md`. Full `creator-vibe` не применяется к точному техническому контракту.
- Surface/effective agent runtime: Codex desktop, фактический model ID/reasoning клиентом в этом ходе не засвидетельствован. Это не LLM benchmark. Target: Windows x64, закреплённые Dafny 4.11.0, .NET SDK 10.0.400, runtime 10.0.11; exact executable/runtime closure digests фиксируются до validation.
- Baseline/evidence: [G02](../docs/project-intent.md#g02--однозначная-компиляция-в-машинный-код-зрелой-платформы), [E04](../artifacts/e04/REPORT.md) — утверждённый закрытый TaskGraph, Dafny→C#→ReadyToRun win-x64; [E05](../docs/modules-v0.2.md) — общий язык, owner proof, но без trusted runtime/admission; [E06 .NET](../artifacts/e06/dotnet-package-aaf2dc2/REPORT.md) — воспроизводимый **validation-only** package, Windows/Linux consumer `8+24+1+13`; [E06 JIT](../artifacts/e06/dotnet-jit-ca5ae50/REPORT.md) — два связанных JIT compilation events, не доказательство корректности машинного кода; [двухэтапный admission design](2026-09-07-e05-two-stage-admission-amendment.md) — reviewed design, статус утверждения этой старой SPEC не подтверждён. Нормативно включена только exact revision E05 amendment: Git blob `a389e09c60ee1f4dd98604521869f2910bba3fec`, file SHA-256 `AE80C1BDF782632BFABAEBD577E6F4A2B5E6813ED1A45445B5C7D86B2A2BDB77`; drift файла до approval/EXEC блокирует использование и требует явного re-review, не молчаливого наследования нового текста.
- Upstream [ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run/) компилирует native code, сохраняет IL и допускает JIT fallback/tiered compilation. [Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) даёт нативное self-contained приложение, но не поддерживает dynamic assembly loading и требует trimming compatibility. Выбор ниже относится к pinned profile, не ко всем будущим target.

## 1. Overview / Outcome

Довести поддержанный фрагмент общей нотации `Strogo.Modules v0.2` от **допущенного** agent-written module до прямого исполнения его generated code на зрелой .NET платформе. Для human-approved D02 owner contract и минимум двух структурно различных корректных D02 modules получить proof-bound package, ReadyToRun win-x64, публичный ABI, строгую проверку перед load/invoke, direct outcome/refusal и evidence сохранения семантики. Ширина общего compiler/ABI фрагмента отдельно проверяется на двух frozen **validation-only** owner bundles разных форм; их fixture signatures не заменяют D02 human decisions. В отличие от E04, реализация не предзадана шестью операциями; в отличие от E06, итоговый D02 package не помечается validation fixture без human approval/admission.

Success means: обычная команда и library consumer исполняют разрешённую функцию из допущенного immutable package; не обращаются к Strogo reference evaluator в runtime path; все outputs для approved-domain corpus совпадают с owner model и reference semantics, негативные и tamper cases fail closed. Отчёт различает универсальную proof-обязанность, конечные integration checks и недоказанную корректность Dafny/C#/.NET в TCB. G02 claim ограничен поддержанным фрагментом, target win-x64 и exact approved package; G01/G03/G05/G06 остаются отдельными вопросами.

Stop: отсутствие exact человеческого semantic approval или release admission, недоказанный candidate, parser/IR/proof/translation drift, пропущенный runtime check, bypass loader, отсутствие actual machine execution evidence или owner-corpus mismatch не позволяет назвать пакет допущенным и G02 checkpoint закрытым.

## 2. AS-IS и корневая проблема

`src/Strogo.Modules` содержит parser/typechecked IR, reference evaluator, owner bundle v0.4, Dafny lowering и proof fixtures. `Strogo.Modules.Portability` строит отдельный validation package с `purpose=validation-only`, `contractStatus=validation-fixture`, `admissionStatus=NotAdmittable`; он проверен на Windows/Linux, но не подключён к `TrustedModuleRuntime`. `Kernel.Host` связан с Reserve; E04 R2R публикует предзаданный TaskGraph pipeline. Документ E05 о двухэтапном approval описывает целевой runtime facade, но исходники `Owner.Cli`, `Modules.Cli` и production admission отсутствуют. Нельзя считать E06 proof/package человеческим согласием или допуском.

Корневая проблема: существующее machine-code evidence подтверждает частные validation пути, но не связывает произвольно выбранную агентом реализацию, утверждённый человеком formal contract, проверку и точный исполняемый пакет в один воспроизводимый и безопасный `load→invoke` путь.

## 3. Цели и Non-Goals

Цели: использовать общий AST/IR/owner semantics E05; выбрать R2R как первый профиль благодаря E04 actual win-x64 и E06 .NET runtime/ABI evidence; зафиксировать exact toolchain/closure и public ABI; отделить human semantic approval от release admission; связать actual direct generated method с доказанной моделью через TCB-aware evidence; не ослаблять старые профили.

Не делаем: NativeAOT сейчас, Linux production admission, общий package registry/import closure, helper contracts, nested fold, FFI/effects, произвольную сеть/файлы, OS sandbox или платформы без runner. Не доказываем корректность Dafny compiler, Roslyn, crossgen2, CoreCLR/JIT, CPU/OS и человеческую эквивалентность формулы; не объявляем byte-equal native code универсальным свойством. Не переносим автоматически старое E05 approval на новую bundle identity. Не считаем time/memory G06 по факту R2R.

## 4. TO-BE: ответственность и API

| Owner | Ответственность |
| --- | --- |
| Human/owner | Подтвердить точный bundle/policy digest **до** candidate proof, затем точный proof/build package **после** build. Эти две операции разделены. |
| `Strogo.Modules` | Strict parse, typed IR, bounded semantics, owner binding и generated Dafny obligations. Candidate не задаёт own contract/axioms. |
| G02 proof/build pipeline | Pin Dafny/SDK/flags/closure; fresh verify по frozen inputs; translate/build R2R; bind proof, source map, generated/compiled bytes и ABI. |
| Trusted .NET runtime | Проверить текущий owner trust/approval/admission, package file closure и requested function/canonical input **до** загрузки generated code. |
| Generated module | Только чистые E05 операции и declared exports; никакой path/permission/contract lookup. |
| Independent oracle/conformance | Exact owner-domain outcomes, negative witnesses, forced JIT diagnostic и tamper/loader probes; не заменяет proof. |

Публичная библиотечная поверхность в рамках фрагмента: host создаёт `OwnerTrust` из operator-controlled configuration и `TrustedModuleRuntime(trust, ownerStateProvider)`, после чего `runtime.Load(packagePath, admissionPath, expectedBundleDigest)` возвращает typed refusal либо handle; `handle.Invoke(functionId, canonicalInput)` возвращает canonical `InvocationResult`. `admissionPath` — внешний, operator-provided artifact; package/input не может подставить его автоматически. Агентский module/input не создаёт runtime и не передаёт trust anchor. CLI `run`/`explain` использует этот facade; `check`/`build` используют тот же trusted owner-state verifier, но отдельный proof/build pipeline. Owner `approve-contract`/`admit` никогда не доступны candidate. Source/input не могут задавать trust config, policy, assembly path, function implementation или raw C# fragment. Exact schema/IDs для approval/proof/manifest/admission, signing и expiry принимаются из §6–11 **указанного blob** [E05 двухэтапного контракта](2026-09-07-e05-two-stage-admission-amendment.md); **эта новая SPEC принимает эти пункты как нормативную часть G02 только после собственного approval**, не ссылается на старый design как на уже реализованный механизм. Отступление от их security/identity contract требует отдельной SPEC revision.

Поддержанный core-fragment: явные `I64`/`Bool`, bounded non-recursive record и `Seq<T,N>` с `N<=256`, `const`, `add/sub/le/eq`, Boolean operations, `record.make/get`, `seq.empty/length/get/append`, closed `if`, ацикличный local `call` и один разрешённый bounded left `fold` с owner invariant. Только export functions, строгий input/output codec; imports, helper contracts, nested folds, произвольная рекурсия и эффекты — отказ до proof. `MathInt` допускается только в proof, не в runtime ABI. Backend не имеет allowlist конкретных function IDs, фиксированного числа exports или одной сигнатуры: build строит dispatch/codec из проверенного IR и owner-owned type closure. **Generality conformance** обязан пройти два frozen validation-only bundles разных форм: scalar branch + local call и bounded record/sequence + fold; в обоих — произвольные допустимые export IDs, перестановка порядка declarations и два семантически эквивалентных тела под одним fixture contract. **Admitted end-to-end** отдельно использует D02 scalar `Decide` с двумя correct bodies и одним exact human-approved D02 bundle. Fixture не выдаётся за D02 approval. Unknown opcode/type и неподдержанный профиль — typed refusal до package. Если для любой части этого обещанного фрагмента lowering отсутствует, AC1 не выполнен; silent fallback на fixed E06 wrapper запрещён.

Build: canonical module + owner bundle + semantic approval → parser/IR/proof → Dafny C# translation → .NET publish `PublishReadyToRun=true`, `PublishReadyToRunComposite=false`, checked arithmetic, locked restore, `win-x64`, pinned runtime/SDK closure → immutable package/manifest → human release admission. Runtime: verify package/approval/revocation/expiry/closure → isolated load context or bounded process → canonical JSON ABI → **direct generated method** → output validation. Выбранный generated `functionId` разрешается только из manifest/source map, не через свободную reflection по input. Если `AssemblyLoadContext` не обеспечивает требуемую изоляцию эффектов, чистота остаётся языковым/contract ограничением; утверждение об OS isolation запрещено.

R2R package должен иметь native header, а program-specific method должен быть вызван по direct generated path. Обычный consumer возвращает outcome и identity package/admission/function. Для каузальной проверки **тот же immutable package** запускается с независимым CoreCLR profiler-наблюдателем `FunctionEnter3`/function-ID mapper (см. [CoreCLR profiling design](https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/profiling.md)): observer связывает PID, loaded assembly digest, metadata token/source-map-derived generated symbol и число **method-entry callbacks**, а не доверяет счётчику adapter. Отсутствие callback или decoy symbol — refusal. Profiler может изменить выбор JIT/R2R кода, поэтому это direct-call evidence в наблюдаемом диагностическом запуске, а не утверждение instruction provenance ordinary run. Дополнительно отдельный pinned process из **тех же package bytes** с `DOTNET_ReadyToRun=0` и method-filtered JIT events для public adapter и generated candidate связывает PID/source map/call count; это подтверждает JIT compilation в диагностическом режиме. Неинструментированный ordinary run выполняется без overrides. Claim: R2R артефакт содержит native code и та же generated program исполняется через mature runtime, где возможен JIT; если потребуется доказать точное происхождение instruction bytes обычного run, это отдельный gate.

### Наблюдаемые сценарии

| Trigger | Видимый результат | Evidence | AC |
| --- | --- | --- | --- |
| Owner утверждает bundle, агент предлагает корректный модуль, owner допускает package | `run` и standalone consumer возвращают тот же exact output, что owner model, с proof/package/admission identities | signed receipts, public ABI output, source map, R2R/JIT evidence | AC1–5 |
| Агент меняет реализацию при том же контракте | Две разные корректные реализации проходят; нарушенная отказывается до package | proof outcomes, structural diff, witness | AC1–2 |
| Wrong/revoked/expired approval либо mutated package | `Load` отказывает до invoke/эффекта | typed refusal, absent loaded-method trace | AC3–4 |
| Ошибка типа, вне `requires`, неизвестный export | canonical typed refusal, generated method не вызван | CLI/consumer trace | AC4 |
| Repeat build/run на clean inputs | semantic/proof/translation/package identities сохраняются или явный typed nondeterminism refusal; outcomes совпадают | two-root receipts и hash inventory | AC5–6 |

### State/interaction matrix

| State | Trigger | Result | Error case |
| --- | --- | --- | --- |
| Unapproved | `check` | Отказ до prover | forged/expired/revoked bundle approval |
| ApprovedContract | `check` | Verified proof либо typed non-admission | counterexample/timeout/tool error |
| Verified | `build` | Immutable R2R package | source/proof/closure drift → refusal |
| BuiltNotAdmitted | `run` | Отказ | Только `admit` после human package review создаёт external admission |
| Admitted | `Load`/`Invoke` | Direct method + canonical result | expiry/revoke/tamper/type/precondition → refusal |

### Decision ledger

| Decision | Owner | Chosen | Confidence | Risk | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Backend mode | agent proposal, user by SPEC approval | win-x64 ReadyToRun with permitted JIT fallback | 0.9 | Не полный AOT | Нет |
| Admission | user by SPEC approval | Two human gates E05 amendment, exact digests and signed current state | 0.9 | Дополнительная работа владельца | Нет для начала EXEC; реальные подписи внутри EXEC обязательны |
| Approved domain/module | user | D02 exact owner bundle после отдельного D02 SPEC и bundle approval; альтернативу можно выбрать только новой G02 SPEC revision | 0.95 | Без D02 G02 end-to-end не закрыт | Нет для подготовки backend; Да до финального `Admitted` claim |
| NativeAOT | agent | Не выбран из-за текущего dynamic package loader и отсутствия pinned feasibility | 0.85 | Не измерены возможные преимущества AOT | Нет |

### Runtime/config/data matrix

| Contract | Source of truth | Change | Compatibility/check |
| --- | --- | --- | --- |
| Language/IR | E05 v0.2 docs/code | Без новых opcodes/schema | existing Modules conformance; unsupported profile fails closed |
| Owner model | owner bundle v0.4 и human approval | Exact hash/signature gate | не переносить чужие approvals |
| Proof/build | E05 two-stage §§6–11 + pinned E06 closure | Реализовать production artifact chain | replay verified proof, hash/mutation gates |
| Runtime | .NET 10.0.11 closure + R2R publish | `TrustedModuleRuntime`/CLI | load refusal before method; actual direct invocation |
| Old profiles | Reserve/E04/E06 | Не менять semantics/receipts | regression and separate output dirs |

## 5. Правила и гарантии

1. Смысл программы — существующий E05 typed reference semantics и утверждённая owner model. Proof устанавливает exact outcome относительно этого bundle для всех входов в `requires` в поддержанном фрагменте. Parser, lowering, Dafny backend, C# compiler/R2R, .NET runtime, ABI adapter и output validator остаются явно перечисленным TCB. Для формального G02 checkpoint используется **только exact D02 owner bundle после отдельного человеческого approval его digest** и конкретный admitted package после второго человеческого решения. Fixture-key passes позволяют разработку механизма, но не закрывают G02.
2. Runtime принимает **только** package с однозначной цепочкой module→bundle→human semantic approval→proof→build manifest→human release admission→текущий owner state; любые несогласованные bytes или неизвестные versions отказываются до load. Один самоописанный JSON `Verified` без replay не достаточен.
3. Public ABI возвращает structured refusal для invalid transport, type, unknown export, failed precondition, unavailable environment и corrupted/tampered artifacts. Входной JSON не становится инструкцией для загрузчика.
4. Accepted function call не использует `ModulesReferenceEvaluator` или универсальный IR interpreter; conformance instrumented probe проверяет direct generated method path. Наличие машинного кода лишь в adapter/interpreter не закрывает AC.
5. Два корректных структурно разных module bodies под одним утверждённым contract должны пройти; wrong semantic mutation отвергается. Выбор одного fixed pipeline запрещён как evidence общего backend.
6. Исходная E05 runtime-facade SPEC предполагала signing/owner-state. Если реализация этой части окажется несовместима с текущим SDK/OS или потребует ослабить trust gate, EXEC останавливается до новой SPEC и нового человеческого решения.

## 6. Интеграция, данные, миграция, rollback

Новые производственные artifacts/CLI создаются отдельно от `Strogo.Modules.Portability` validation-only package и не меняют его `NotAdmittable`. `contract-approval`, `proof`, `build-manifest`, `admission`, owner state и runtime closure имеют закрытые версии/identity по E05 amendment. Реальные private keys/trust state — вне repo; conformance применяет disposable test key и не выдаёт его за production identity. Target не требует GitHub release/deploy; local end-to-end считается только при explicit human approval exact bundle/package. Rollback: revoke/expiry делает пакет недоступным; возврат к старому пакету в том же epoch допустим только с действующим admission, после epoch advance нужен новый полный двухэтапный gate. Не удалять сохранённые receipts для имитации отката.

## 7. Приёмка и проверка

- **AC1**: API и CLI обрабатывают поддержанный §4 core-fragment без фиксированного allowlist ID/сигнатур: два frozen validation-only bundles для scalar branch/local call и bounded record/sequence/fold с переставленными declarations и arbitrary valid export IDs; две структурно различные корректные реализации и wrong candidate проверены на неизменном fixture bundle для каждой формы. Отдельно D02 exact human-approved bundle принимает две correct `Decide` bodies и отвергает wrong body.
- **AC2**: full proof по exact approved owner bundle `Verified`, weak/wrong mutation даёт refusal, obligations и TCB покрыты отчётом; без human semantic approval утверждать end-to-end нельзя.
- **AC3**: exact E05 A-AC1–A-AC9 выполнены, включая два human gates, TTY/digest input для owner CLI, signature/trust/state, expiry/epoch/revocation, locked file handles против TOCTOU, package/closure/manifest hashes и rollback; tamper/forgery/stale cases отказывают до load.
- **AC4**: ordinary CLI и standalone library consumer на win-x64 запускают exact **D02-owner-approved и release-admitted** package, не reference evaluator. До запуска фиксируется corpus по каждому экспортируемому entry: внутри `requires` обычный и граничные I64 (`MIN/MAX/0`, где применимо), Bool, пустая/полная bounded sequence, record field/order, true/false ветви, локальный call/fold; вне `requires`, unknown export, malformed/duplicate/extra JSON field, type/capacity/index error. Expected values/refusals независимо вычисляются из human-approved owner model и отдельного reference oracle; provenance и digest каждого vector/oracle заморожены до consumer run. Если D02 не использует некоторый тип/op, соответствующий fragment case покрывается отдельным validation fixture, не приписывается D02 owner proof. Output/refusal canonical.
- **AC5**: actual R2R header, независимый CoreCLR profiler method-entry trace с package/admission/function/source-map/PID binding и forced-JIT diagnostic из тех же package bytes сохранены; decoy/no-call отвергается. Обычный run без observer проверен отдельно; не утверждать, что он обязательно исполнил R2R body.
- **AC6**: два clean builds/proof runs с теми же canonical inputs, pinned toolchain и controlled path context дают одинаковые нормативные digests/outcomes либо typed refusal; прежние Reserve/E04/E05/E06 suites проходят; отдельный отчёт и knowledge log фиксируют положительные/отрицательные выводы.

| AC | Required test/check | Evidence | Unchecked outcome |
| --- | --- | --- | --- |
| AC1–2 | two fixture bundles/shapes with dual candidate + wrong mutation/arbitrary IDs; D02 approved bundle with dual `Decide` bodies; Dafny proof and owner projection | separate fixture versus D02 approval/proof receipts and source maps | блокирует G02 |
| AC3 | E05 A-AC1–A-AC9 crosswalk: TTY/signature, proof replay, state/expiry/revoke, locked handles/TOCTOU, exact admission and rollback; no-load negatives | approvals, refusals and immutable input/output hashes | блокирует G02 |
| AC4 | frozen provenance/digest corpus partitions above; documented `run`, standalone consumer, independent owner/reference comparison | actual D02-approved/admitted output/refusal report | блокирует G02 |
| AC5 | PE native header + independent CoreCLR profiler entry callback + forced-JIT candidate events from same package; decoy/no-call negatives | separate ordinary/profiled/JIT reports | блокирует machine-code claim |
| AC6 | clean two-root replay, full relevant conformance, `git diff --check`, review | reports, SHA inventory, commit, knowledge IDs | блокирует closure |

Preflight EXEC: проверить `.tools/dotnet-sdk-10.0.400/dotnet.exe`, pinned Dafny, locked restore и runtime closure до долгих запусков. Обычные команды `check/build/run` и exact output paths будут зафиксированы при реализации; диагностические JIT flags не входят в ordinary command. Не повторять timeout без новой гипотезы. UI/visual/video неприменимы: public surface CLI/library. Performance сравнение и G06 rule — отдельный этап.

## 8. Риски и возражения

| Вероятное замечание | Ответ/проверка | Статус |
| --- | --- | --- |
| «R2R может вызвать JIT» | Явно допускается; R2R header + actual candidate JIT diagnostic отдельно. Не называем full AOT. | mitigated |
| «Валидационный пакет уже работает» | E06 `NotAdmittable`, без owner/release approvals; AC3 замыкает trust chain. | mitigated |
| «Доказательство не проверяет .NET compiler» | Это TCB; direct runtime/corpus/negative checks подтверждают интеграцию, но не universal compiler correctness. | accepted boundary |
| «Человеку придётся дважды подтверждать» | Первое подтверждает смысл bundle, второе — конкретные исполняемые bytes; E05 design не смешивает решения. | accepted tradeoff |
| «Нужны другие ОС и память» | G02 минимум одна mature platform; G06/portability tasks измеряют остальное. | out of scope |

Risks: owner trust key/operator setup, path/closure TOCTOU, proof replay недетерминирован, R2R/native method attribution, execution outside `requires`, самосоставленный oracle. Fail closed; staged immutable package; separate trusted oracle; replay and attestation; точный TCB. No production key/host setup в repo. Rework-prevention: scenarios §4, ledger §4, AC→evidence §7, objections above, role review §10.

## 9. План и файлы

1. После approval этой SPEC сначала реализовать/проверить strict owner-state/signature и две approval операции по E05 normative contract; зафиксировать checkpoint и знания. Без actual owner decision допустимы только fixture-key тесты.
2. Реализовать check/proof replay и R2R build/package binding; сохранить validation-only profile отдельно.
3. Реализовать trusted load/invoke и direct generated method ABI, negative gates, runtime closure check.
4. После отдельного подтверждения D02 SPEC и exact D02 owner-bundle digest получить actual D02 proof/package и второе human release admission; выполнить full corpus, JIT diagnostic, reproducibility и regressions. До обоих решений можно завершить механизм и fixture validation, но нельзя закрыть AC2–4/G02/Unlimotion child.
5. Post-EXEC review, подробные Conventional Commits, периодический разрешённый push, Unlimotion complete только после AC1–6 read-back.

Планируемые области: `src/Strogo.Modules` (facade/proof integration), новые `src/Strogo.Modules.Owner.Cli`, `src/Strogo.Modules.Cli`, точечное расширение `src/Strogo.Modules.Portability` только при реальном reuse без изменения validation semantics, `tests/Strogo.Modules.Conformance` и новый end-to-end consumer, `fixtures/`, `formal/`, отдельный `artifacts/local-validation/g02/`, `docs/modules-v0.2.md`, `docs/knowledge-log.md`. Точные имена файлов определяются по архитектуре; изменение E05 source schema/opcodes, работа с реальной Obsidian/Unlimotion базой или упрощение human gates требует новой SPEC.

## 10. Quality gate / review

### SPEC linter

| № / блок | Статус | Evidence |
| --- | --- | --- |
| 1 / A result | PASS | §1 ordinary `Load/Invoke`, machine execution и boundaries. |
| 2 / A AS-IS | PASS | §2 E04/E05/E06 source/evidence distinction. |
| 3 / A problem | PASS | §2 нет approved owner→package→runtime chain. |
| 4 / A goals | PASS | §3 reuse semantics, chosen backend и TCB. |
| 5 / A Non-Goals | PASS | §3 без NativeAOT/general ecosystem/G05/G06 claims. |
| 6 / B owners | PASS | §4 human/module/proof/runtime/oracle split. |
| 7 / B integration | PASS | §4 API/CLI, approval pipeline, §6 rollout. |
| 8 / B rules | PASS | §4 supported fragment, §5 exact chain/direct method. |
| 9 / B errors | PASS | §4 state matrix, §5 typed refusals, E05 pinned error order. |
| 10 / B resources | PASS | §0 pinned toolchain/runtime, §4 fragment/bounds; G06 отдельно. |
| 11 / C data | PASS | §4 exact E05 schemas/blobs/manifests/admission. |
| 12 / C compatibility | PASS | §3/§6 E06 validation unchanged, old profiles regressions. |
| 13 / C rollback | PASS | §6 revoke/expiry/same epoch versus new epoch. |
| 14 / D AC | PASS | §7 AC1–6 executable/negative/identity criteria. |
| 15 / D mapping | PASS | §7 AC→test/evidence, E05 A-AC1–9 crosswalk. |
| 16 / D commands/stop | PASS | §1 stop; §7 ordinary CLI required, exact command set from pinned E05 and implementation. |
| 17 / E plan | PASS | §9 checkpoints/dependencies; D02 gate explicit. |
| 18 / E decisions | PASS | §4 ledger; owner D02 approval is later gate, not assumed. |
| 19 / E form | PASS | Large expanded for multi-module public/security contract. |
| 20 / F profile | PASS | Product-system-design: goals, ABI, security, compatibility §§3–7. |

Итог: **ГОТОВО к утверждению проектной SPEC**, но до D02 human approvals можно выполнять только backend mechanism/fixture validation, без end-to-end G02 claim.

### SPEC rubric

| Критерий | Балл | Основание |
| --- | ---: | --- |
| Цель и границы | 5 | G02 support fragment и claim boundary §1/§3. |
| AS-IS | 5 | Реальные E04/E05/E06 evidence и gap §2. |
| Конкретность дизайна | 5 | API, pinned normative amendment, IR matrix, R2R/JIT distinction §4–5. |
| Безопасность/миграция/rollback | 5 | Exact admission, no-load refusal, revoke и rollback §4–6. |
| Проверяемость | 5 | AC1–6, corpus partitions, oracle provenance и JIT causality §7. |
| Автономность | 2 | D02 exact bundle/release approval требуется человеку до полного outcome. |

Итог **27/30**: готово к автономной разработке механизма после утверждения SPEC; human decisions сохраняются как обязательные gates.

### Role-based review

| Role | Applicable | Question | Verdict |
| --- | --- | --- | --- |
| Business/domain | Да | Действительно ли owner/agent decisions разделены? | PASS: §4 и D02 gate §5 |
| UX/designer | Нет, UI отсутствует | CLI projection оценивает tester | Не применимо |
| Tester/validation | Да | Доказательство и runtime checks разведены? | PASS для дизайна: §4, AC2–5 |
| Developer/architect | Да | ABI/TCB/reproducibility coherent? | PASS для дизайна: E05 exact API/blob, §4–5 |
| Delivery/operations/security | Да | Host trust, package tamper, rollback sound? | PASS для дизайна: AC3 и §6; EXEC evidence впереди |

### Post-SPEC review

**Статус: PASS для запроса approval этой SPEC.** Scope reviewed: этот файл; central `quest-mode`, `quest-governance`, `spec-linter`, `spec-rubric`, `review-loops`, `product-system-design`; repo `AGENTS.md`; `docs/project-intent.md`, `docs/modules-v0.2.md`; E04/E05/E06 отчёты; exact E05 amendment blob `a389e09c…b3fec`; `DafnyPortableWireLowering`, portability report `NotAdmittable`, .NET JIT diagnostics, Microsoft ReadyToRun/NativeAOT official docs; planned files §9, open decision §4.

- Scope/evidence pass: E04 fixed TaskGraph и E06 validation-only различены; задача требует общий E05 fragment, proof-bound approved package и actual direct execution. Исторические proof/JIT/consumer результаты не выданы за новое G02 evidence.
- Contract pass: pin E05 A-AC1–9 и exact Load/admission signature; §4 unsupported operations fail closed; D02 human bundle и release gates названы частью **финального**, не подготовительного outcome.
- Adversarial pass: проверены подмена trust/admission/package, fixed-function shortcut, false machine-code attribution, неопределённый corpus/oracle, отсутствие D02 approval, TOCTOU/epoch rollback и expected user objections §8.
- Role-based pass: таблица выше, CLI projection входит в tester; UI отсутствует.
- Fix and re-review: после advisory findings уточнены exact E05 identity/API/A-AC map, IR matrix, ordinary/diagnostic causal binding, corpus partitions и D02 dependency. Вторая итерация отделила fixture breadth от human-approved D02 path и заменила самоотчёт adapter независимым CoreCLR profiler method-entry наблюдением. Повторно сверены §§0, 1, 4–7, 9.
- Stop decision: SPEC можно представить владельцу; до exact approval source/infra не меняются. D02 gate остаётся самостоятельным и не считается выполненным.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | admission API | Load не передавал external admission path | E05 exact `Load(packagePath, admissionPath, expectedBundleDigest)` | fixed |
| HIGH | generality | Один fixed E06 wrapper мог пройти AC | Closed fragment, arbitrary export IDs, two different shapes/bodies | fixed |
| HIGH | normative identity | E05 amendment подключался по mutable path | Pin blob/SHA и A-AC1–9, drift gate | fixed |
| MEDIUM | machine code causality | Forced-JIT не связывал ordinary call с generated method | Ordinary call receipt + same-byte diagnostic, decoy/no-call | fixed |
| MEDIUM | corpus | Domain/edge vectors и oracle не закреплены | Exact partitions/provenance freeze до run | fixed |
| MEDIUM | owner gate | D02 bundle/admission отложен без clear completion boundary | D02 exact approvals обязательны для AC2–4/G02 | fixed |
| HIGH | test/owner coherence | Scalar D02 contract не содержит fold, но AC1 требовал fold под тем же human bundle | Развести validation-only two-shape fixtures и admitted D02 scalar path | fixed |
| MEDIUM | direct-call evidence | Adapter мог сам сообщить positive call count | Независимый CoreCLR profiler method-entry callback на exact package + decoy/no-call | fixed |

Depth checklist: scope drift — только текущая SPEC; AC — §7; validation evidence — пока план, не runtime result; unsupported claims — JIT≠R2R attribution, validation≠admission; regressions — Reserve/E04/E05/E06; comments/docs/changelog — §9 план, продуктовый changelog после EXEC при необходимости; hidden API change — запрещённое ослабление E05 и новые E05 opcodes требуют SPEC revision; manual-review challenge — проверить, что generic dispatch не остался fixed 5-ID E06 wrapper и что ordinary call нельзя пройти через interpreter. No-findings justification после исправлений: каждая первоначальная находка имеет конкретный AC и отрицательный probe, новых BLOCKER/HIGH/MEDIUM на проверенной поверхности не обнаружено; фактическое EXEC evidence впереди.

Reviewer boundary: subagent был поведенчески read-only, но sandbox `danger-full-access`/approval `never`, поэтому это **advisory adversarial fallback**, не независимый технически read-only review. Остаточный риск: объём E05 admission implementation велик; D02 approval/owner bundle и release admission пока отсутствуют. Эти факты не превращены в PASS реализации.

### Post-EXEC review

Не выполнен: EXEC не начат.

## Журнал действий агента

| Фаза | Решение | Evidence | Следующий шаг | Решение человека |
| --- | --- | --- | --- | --- |
| RESEARCH | Existing E04/E06 paths не закрывают общий admitted module | E04/E05/E06 reports, source inventory, G02 goal; task claimed in Unlimotion | Выбрать backend и подготовить SPEC | Старые approvals не подтверждают эту revision |
| SPEC draft | Выбран R2R win-x64 с разрешённым JIT fallback; заимствован двухэтапный E05 admission как нормативный contract | §§0–9; official .NET docs | Full post-SPEC review и freeze | Ожидается |
| SPEC review | Advisory reviewer нашёл шесть gap в identity, API, generality, JIT causality, corpus и D02 gate; исправлены | §10 findings, повторная проверка затронутых областей | Freeze commit/hash и запрос exact approval | Ожидается |
| SPEC approval → EXEC | Владелец ответил «Спеку подтверждаю» на эту последнюю G02 revision; исходный blob `b89624a70bd9ea48b4600640b5d8d60255ba700a`, файл SHA-256 `82726A76D3D61EA6F00203A6BF1C3965C096EEFBF69BFD533BE852FB7B0AC216` | commit `5869f592666f3107a74ca64f10d0ac10f47e11cf`, `origin/main`; текущий диалог | EXEC: сначала signed owner trust/state и fixture-key validation; D02 gate остаётся самостоятельным | Утверждено для G02 mechanism/fixture EXEC; точные D02 bundle и package ещё не подтверждены |
| EXEC trust checkpoint | Реализована проверка state/contract approval/admission на disposable fixture key; это ещё не facade или machine execution | `OwnerAdmissionTrust.cs`, 24 targeted checks, K-G02-002 | Owner CLI, replay/proof/package gate и полный AC3 | Дополнительное решение владельца для fixture validation не требуется |
