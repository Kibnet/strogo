# G02 — Обычный публичный check/build/admit/run и library consumer

## 0. Метаданные
- Тип: delivery-task / QUEST / product-system-design, expanded large, trust/admission/backend. Владелец: Kibnet. Ветка main, baseline c3c56bd9b1f1d5c76ae891ab8eb6d4dced2832dc.
- Codex desktop, PowerShell7/Windowsx64; фактические model/reasoning не засвидетельствованы. Model benchmark не применим: это backend/API, не G05 измерение.
- Central stack и локальный AGENTS; canonical expanded template. Связи: [утверждённый G02](2026-09-29-g02-dotnet-r2r-admitted-modules-v0.1.md), [closure proposal](2026-10-01-g02-physical-publish-closure-v0.2.md), [signed native](2026-10-01-g02-signed-native-observer-v0.1.md).
- Статус: SPEC. Engineering decomposition в рамках подтверждённой цели. Public closure formula остаётся отдельным неотвеченным нормативным выбором; эта SPEC не утверждает её молча. Human D02 semantic approval и точный release admission остаются реальными действиями владельца.

## 1. Outcome contract
Поручение: получить воспроизводимый путь принятой Strogo-программы до машинного исполнения .NET, затем измерить практическую ценность G05/G06. Success этой части: обычный CLI и отдельный library consumer исполняют exact signed package через direct generated dispatch; не обращаются к reference evaluator; перед load проверяют fresh proof/build, physical closure, actual runtime и актуальный owner state/release, а перед каждым invoke — fresh admission/runtime/package. Публичная команда не требует consumer обращаться к internal G02VerifiedFixtureBuild, private fixture descriptor или profiler.

Output: Strogo.Modules.Cli; production TrustedModuleRuntime/session; owner admit command/projection; обычное руководство; standalone consumer и негативные evidence. Наличие fixture conformance не означает human-approved D02/G02 completion. Stop: неподтверждённый closure meaning, отсутствующее human bundle/package admission, неполный replay, uncontrolled host option, неизвестная dependency или output drift → no production admission.

## 2. AS-IS
Owner.Cli реализован в src/Strogo.Modules.Owner.Cli: approve-contract, state advance-epoch. OwnerHostContext фиксирует key/config/state location и читает новые bytes при каждом обращении. G02StoredIdentityVerifier публичен, но проверяет stored chain и не является proof replay/admission.

G02PackageProofReplay, G02SignedFixtureSession, G02RuntimeBinding и G02CompiledDispatch реализованы. Fresh fixture verifier делает два proof replay, translate/build и сравнивает entry/deps; full publish inventory190 files существует отдельно и имеет только diagnostic digest. Signed fixture envelope validationOnly=true. Native tool использует общий signed session, но остаётся diagnostic forced-JIT/file-backed process. Публичных Modules.Cli, TrustedModuleRuntime и owner release proposal/admit нет. Историческое AS-IS утверждённой SPEC об отсутствии Owner.Cli устарело; исходную утверждённую ревизию не переписываем.

## 3. Проблема
Цепочка работающих компонентов заканчивается internal fixture API. Публичный consumer не может получить воспроизводимый проверенный package/session и human release admission без собственного склеивания доверенных деталей. Изолированные зелёные fixture paths не закрывают ordinary execution gate.

## 4. Цели дизайна
Один shared verification/dispatch engine для public CLI/library и diagnostics; fixture adapter не становится production verifier. Host задаёт trust/tools/cache/workspace, candidate — только программу. Идентичности proof/package не доверяются до fresh reproduction. Два human решения раздельны. Один codec/outcome, включая явные отказ и ошибки. Existing fixture evidence/форматы сохраняются историческими.

## 5. Non-Goals
Linux production, NativeAOT, registry/imports, effects/FFI, параллельные вызовы одной session, OS sandbox, newest-at-return state guarantee, mapped instruction attestation. Не обещаем G05/G06 до измерений. Не выдаём test signing key или fixed clock за решение человека.

## 6. TO-BE
### 6.1 Ответственность
- Shared internal verification: held package/source/bundle/signatures → fresh proof replay → verified translate → pinned SDK build → approved full physical closure compare → entry/deps/dependency profile compare. Никакой public фабрики trusted record.
- Producer: source+exact bundle+semantic approval → immutable package. Proof/manifest identities строятся после publish closure, без circular hash и без caller-сupplied identity overrides.
- TrustedModuleRuntime: owner-owned host context/tools/cache/temp + system clock; fresh verified open; owned tool/package lifetime до Dispose; InvokeJson на общей typed codec и generated dispatch.
- Modules.Cli: parser/options/input/output, delegation к shared engine. Owner.Cli: interactive release projection и signing. Native observer остаётся внутренним diagnostic adapter.

### 6.2 CLI surface и входы
Нормативные command signatures сохраняются из frozen E05 amendment; executable short names ниже — только aliases dotnet run соответствующего проекта:
```
strogo-modules explain --artifact <artifact.json>
strogo-modules check --trust-config <host-owned.json> --module <module.json> --bundle <bundle.json> --contract-approval <approval.json> --out <new-check-dir>
strogo-modules build --trust-config <host-owned.json> --check <check-dir> --out <new-package-dir>
strogo-modules run --trust-config <host-owned.json> --package <package-dir> --admission <release.json> --function <id> --input <canonical.json>
strogo-owner admit --trust-config <host-owned.json> --signer-config <signer.json> --package <package-dir> --contract-approval <approval.json> --approved-by <id> --valid-until <UTC> --out <new-release.json>
```
Не меняем --check на module/bundle/approval, --input на --request и не убираем --function/--contract-approval/--out. Input run имеет exact grammar approved E05 canonical input; adapter собирает existing InvokeJson request из явного --function и canonical arguments, без двойного selector и без нового wire смысла. Run expectedBundleDigest выводится из held canonical bundle + validated approval/package chain, а не из unchecked manifest claim. Library explicit expectedBundleDigest сохраняется.

Tool host profile добавляется к operator launch configuration; существующий --trust-config обязателен. Предлагаемый дополнительный --host-config — optional host-only option без замены normative signature; без него используется operator-provisioned module-host.json рядом с trust config, не environment/candidate defaults. В обоих случаях host config ownerTrustConfigPath обязан совпасть с explicit trust-config normalized absolute path. Это explicit operator deployment convention, не автоматический fallback на package/tool file. Отсутствующий профиль даёт typed HostConfigUnavailable. Exact mapping/environment/startup defaults фиксируются в slice B до code.
Existing approve-contract/advance-epoch commands и explain --artifact сохраняются. explain строит человеческую projection из bounded closed parsed artifact, показывает declared identities/status/conditions/effects/TCB и явную границу «объяснение не верификация/допуск»; не читает private key, не исполняет code, не создаёт state/admission. Golden projection/error cases обязательны до facade closure. host-config — отдельный закрытый operator-owned профиль, содержащий existing owner trust-config path, trusted tool root, host package cache и fresh diagnostic workspace root. Candidate JSON/package/CLI request не содержат tool/key/state/clock/root overrides. Профиль версионируется отдельно до EXEC; candidate не выбирает arbitrary executables. SDK/Dafny closures закреплены existing digests/recipe. Не считать CLI arg абсолютный путь автоматическим доказательством ACL: host config/key/state/ancestors — operator TCB.

check возвращает fresh proof result для текущего approval, а не допуск к исполнению. check сохраняет canonical module/bundle/approval/proof sources/transcript/proof в new check-dir. Поскольку proof включает physical closureDigest, check также выполняет pinned translate/publish в операторском workspace для этой identity, но не создаёт release-admitted package. build читает held check-dir и заново выполняет fresh signature/epoch/proof/translate/build verification; чужой JSON Verified не принимается; CREATE_NEW staging в операторском workspace, immutable package публикуется только после complete verification. out существующий/частичный не перезаписывается. admit выполняет полный fresh package verification, строит exact projection/digest, требует interactive exact digest и пароль existing encrypted key; перечитывает/revalidates state+package перед signing. Никаких --yes/fixed-clock/fixture-key switches. Отказ не оставляет release file. Release внешний к package.

run валидирует bounded strict canonical input и explicit function syntax до state read/prover; после structural held module parse выполняет typed codec validation без evaluator/dispatch, до state/signature gate. После Open session снова InvokeJson проверяет request. Ошибки input schema/type имеют precedence до owner-state в run. Library OpenSession/Load не имеет request и поэтому проверяет admission до load; правило no-provider для malformed/typed input относится к InvokeJson уже открытой session. run делает fresh package verification и separate signature/state/runtime admission перед первым load; codec invalid requests отказываются до dispatch. Library соответствует same path. Public ordinary run без profiler/DOTNET_ReadyToRun/tier overrides; не использовать диагностическую environment как baseline.

### 6.3 Library API/lifetime
Сохраняем нормативную facade TrustedModuleRuntime(trust, ownerStateProvider) → Load(packagePath, admissionPath, expectedBundleDigest) из E05. Host-owned constructor inputs являются explicit TCB, stateProvider fresh bounded каждый gate, общий trust high-water. Engineering host-config factory TrustedModuleRuntime.Open(hostConfigPath) и async OpenSessionAsync(packagePath, expectedBundleDigest, admissionBytes) добавляются как удобные wrappers над same engine, не замена normative API. Sync Load delegates same verification (no skip/replay shortcut). session.InvokeJson(requestBytes) использует codec. Host-config factory фиксирует snapshot и внутренний system clock/state provider. Normative constructor provider принадлежит host; его свежесть не выводится криптографически, обязанность явно documented. System clock не candidate option. No public trusted-build constructor, arbitrary type/method selectors, Assembly/MethodInfo handles, diagnostic fixed clock/loader flags. Normative host-owned state provider не diagnostic bypass: он всегда bounded/copy/fresh signature/high-water validated. Binary request buffer copies bounded до async/dispatch; release snapshot fixed и crypto проверяется заново на invoke.

Host/session owns pinned toolchain/package/runtime leases, process-local high-water по canonical pinned SPKI/keyId во всём trusted host процессе, общий для всех OwnerTrust/HostContext/runtime instances; session закрывается до host. Dispose host/Trust не сбрасывает high-water. Atomic compare/update (epoch, state artifact digest), same-epoch different digest и N→N-1 отказываются. Multi-host regression обязателен: hostA принимает N+1, hostB/newTrust после hostA.Dispose должен отвергнуть N; concurrent cross-host check не может потерять более новый epoch. Trusted owner assembly/registry принадлежит host, загрузка отдельной копии facade для сброса state не разрешённый production путь. Exact registry identity/lifetime/resource bound фиксируются отдельной mandatory security slice до public EXEC. Current OwnerTrust имеет instance fields — это незавершённый process-local gate, не уже выполненный контракт. Не разделять high-water на каждый вызов. Dispose идемпотентен и предотвращает invoke; concurrent Invoke/Dispose не поддержаны, typed refusal вместо race acceptance. Native diagnostic file-backed mapping lifetime остаётся process-owned. Ordinary loader path stream; допустимый platform assembly источник — existing pinned actual runtime, package runtime-dependency set=[] для exact recipe. Никакого fallback на неизвестные DLL/default plugin resolution.

Public output version и identity fields определяются отдельно от fixture envelope; нельзя просто заменить validationOnly на false. Semantic approval/proof/build/package/release/function identities включены; canonical Returned/Refused и typed stage/code. Production validity означает соответствие operator trust policy, не машинное подтверждение того, что человек осознанно прочитал каждое условие.

### 6.4 State/scenarios
|State/trigger|Required outcome|
|---|---|
|Unapproved module/check|signature/epoch/expiry refusal до prover|
|Approved/check|fresh proof Verified либо typed counterexample/timeout|
|Verified/build|fresh immutable R2R output; no release admission|
|Built/run without admission|refusal до load|
|Built/admit|human exact projection confirmation + signed release|
|Admitted/OpenSession|fresh closure+proof+runtime/state gates, generated load only after success|
|Loaded/Invoke|codec→fresh state/signatures→held package→actual runtime→Q→F|
|Malformed/typed invalid|canonical refusal, no provider read/generated entry|
|requires invalid|Q only, no F|
|epoch/rollback/expiry/runtime drift|refusal before Q/F|
|Disposed|refusal before provider/dispatch|

### 6.5 Decisions and ownership
Public closure formula: owner choice pending, inherit only after exact answer. D02 semantic/release: owner, separately pending, never synthetic positive production claim. API/commands engineering: agent within approved G02. All frozen normative interfaces retained; new wrappers/wire versions/schema/output codes get closed specification before implementation; this document is decomposition, not silent schema finalization.

### 6.6 Compatibility
E05 roles preserved; platform/PDB remain compiler output outside package. Existing fixture ffff closureDigest cannot enter public path after approved physical identity; old logs unchanged. Public check/build cannot accept old fixture as a production artifact merely by replacing purpose field. Runtime model doesn't depend on fixture purpose boolean; trusted policy/key and exact identities decide admission.

## 7. Алгоритм и failure order
Следующий pipeline относится только к check/build/admit. run сначала делает input schema/type preflight из §6.2 до state/prover. Library Invoke уже открытой session: codec/schema→fresh state/signatures→package/runtime→Q→F. Library Load/Open без request: отдельный admission gate. Не объединять эти порядки при refactor.

Bounded input snapshots→owner signature/current epoch before costly proof→strict AST/typed IR/bundle/model/source regeneration→two fresh proof replay→translate verified source→held SDK fixed R2R build→full physical identity→entry/deps/empty dependency profile→closure mismatch refusal. check/build завершаются verified check/package artifact без load/dispatch; admit после fresh verification завершается человеческой projection/подписью без load/dispatch. Только run/Library Load после input preflight переходят к current admission/runtime→load; только Invoke — к codec/fresh gates→Q→F→output validation. Human admit signs only completely verified package projection, with final state recheck. Every async boundary has explicit lease ownership; any failure releases resources without creating accepted output. Tool deadlines/job ownership reuse G02ContainedProcess; output bounded before accumulation.

## 8. Интеграция
Minimal slices: (A) approved physical digest formula + producer/replay checks; (B) production shared verified session and closed host config; (C) interactive release proposal/admit; (D) Modules.Cli explain/check/build/run; (E) ordinary standalone consumer; (F) обязательный active-package pointer workflow (separate slice SPEC) с expected-revision CAS; (G) approved D02, ordinary+native evidence и полный A-AC1–A-AC9/current goal audit. A waits owner formula; B/C schema finalized/reviewed before code; G waits real owner semantics/release; F необходим до G02 completion, даже если direct explicit package run готов. Safe SPEC/review work continues while choices pending.

## 9. Данные и evidence
No public private keys/business task data. Evidence: exact source/input/tool/package hashes, proof/build process logs, CLI exit/stdout/stderr, independent native joins and no-call controls; failed history retained separately. Fixture reports explicitly diagnostic; human decision provenance/digest recorded before candidate and before release respectively. Corpus frozen before consumer run, ordinary flags/environment recorded. Reference/owner evaluation only independent oracle outside runtime.

## 10. Rollout/rollback
No release/deployment required. Detailed checkpoint commits and periodic authorized push; restore code without rewriting receipts. Package outputs staging/new paths only; failure no existing output destruction. Revocation/epoch/expiry via existing owner state policy, rollback doesn't silently resurrect stale admission. Active pointer — separate typed activation, не побочный эффект build/admit/run. CAS A→B/B→A только при exact expected revision и fresh valid admission current epoch; stale revision/expired admission оставляют pointer unchanged. После epoch N+1 обязательна полная semantic reapproval→check→build→admit→pointer CAS; signed owner-state rollback control не заменяет active pointer revision. Pointer schema/API/storage оформляются отдельной dependent SPEC до реализации, все A-AC9 cases обязательны до G02 claim. No automatic modification of unrelated Unlimotion graph.

## 11. Acceptance criteria → evidence
|AC|Authoritative proof|
|---|---|
|1 Public closure approved|exact owner answer+frozen vector; two physical build inventories; coherent substitution refused fresh|
|2 explain/check/build public|actual explain projection/no-execution + commands through fresh proofs/R2R; bad contract/body refused before load; exact two-root reproduction|
|3 human admit|interactive positive exact digest/key/state verification; noninteractive/wrong digest/state drift no output|
|4 D02 implementations/corpus|exact human-approved D02 owner bundle до candidate; две структурно различные correct Decide bodies и один wrong negative; per-export owner/reference corpus с I64/Bool/bounded seq/record/branch/call/fold applicable boundaries, provenance/digests frozen до run; unsupported D02 operations отдельно fixtures, не приписывать D02|
|5 library+ordinary CLI|standalone nonfriend consumer+CLI same exact signed package, owner/reference outputs, no evaluator/runtime profiler overrides|
|6 fresh gates/refusals|bad release no-load; malformed/type/requires; late expiry/epoch/rollback/runtime drift; multi-host process high-water N+1→N и same-epoch different digest; Dispose; native intervals for same bytes|
|7 pointer rollback|same-epoch valid/expired admission, stale revision no-change, epoch N+1 full reapproval chain и explicit CAS; separate transition report A-AC9|
|8 evidence/knowledge|current hashes/logs, independent review, Git delivery, KB note/index and CLI result/read-back if global graph valid|

## 12. Risks/objections
Self-consistent package identities aren't fresh verification: replay mandatory. Human signature semantics can't be auto-inferred: two actual decisions. Diagnostic reuse could leak bypass: private seams never exported. OS identity/CLR/file safety conditional on host TCB, not adversarial machine guarantee. Rebuilding at every Open costs latency; G06 includes mandatory overhead, no security cache until separately justified. Profiler modifies execution: ordinary run preserved separately. Mutable source inputs require held snapshot across compiler stages; output transient errors never partially accepted.

## 13. План
Finalize/review public closure choice and wire profiles→shared verifier/producer→trusted session→owner release signing→commands/library consumer→D02 human gates→G02 audit→G05/G06 locked measurement→reasoned project decision. Не считать последний engineering checkpoint целью проекта.

## 14. Validation plan
Locked restore/build0/0; focused negative controls tied to previous defects; relevant existing suites; two fresh full proof/build paths; actual CLI and standalone process. Independent post-SPEC/post-EXEC required for large trust work. UI/video N/A console/library; interactive signing positive must be witnessed, automated tty negatives insufficient. Testing exact protected behavior, не зеркалить implementation getters. Historical scopes remain separate.

## 15. Delivery and knowledge
Significant insights same block docs/knowledge-log + canonical Obsidian/index. Local file links verified, phone/Git KB sync claimed only if checked. G02 execution remains Active; global Unlimotion MissingReverseLink currently blocks result write, not engineering progress. Goal whole G01–G06 unchanged.

## 16. Review applicability / audit
Architect, security/trust, validation/evidence, workflow/API and delivery roles applicable; visual product design N/A. Major objections above identified explicitly. Review must verify AC mappings, host ownership, exact schema finalization, leases, error order, no diagnostics bypass and normative gates. Rubric score not approval. Independent review pending; no new public code/schema implementation from this document yet.

## 17. Журнал
2026-10-01: previous goal turn progress (c3c56bd pushed, native signed integration/evidence). Fresh repo clean, HEAD current; goal/G02 read through installed pinned Unlimotion CLI. Existing approved parent requires ordinary CLI/library and real D02 human admissions. Prepared this decomposition before public behavior changes; separate exact closure choice asked asynchronously. Current known unrelated global graph defect not bypassed.

### Закрытые engineering profiles для следующего review
Host config proposal: UTF-8 strict JSON, максимум65536bytes, closed fields schemaVersion/ownerTrustConfigPath/toolchainRootPath/packageCachePath/workspaceRootPath, все string, no duplicates; schemaVersion=strogo.module-host.v0.1. Все пути fully qualified; toolchainRootPath — operator-owned root с existing pinned .tools layout, не DLL/tool executable selector. Compiled expected inventory/executable digests из G02DafnyToolchain/G02DotNetToolchain обязательны. Existing owner trustconfig schema используется без изменений. Bounded snapshot host config фиксирован до Open; чтения state свежие. package input может лежать отдельно, но tools/cache/workspace/config/key не извлекаются из package/request. Namespace/ACL protection остаётся explicit operator TCB. Host root shape и empty workspace CREATE_NEW policy валидируются прежде создания staging.

Request/result nested grammar reuse exact G02InvocationCodec versions strogo.invoke.v0.1 / strogo.invoke-result.v0.1 и limits65536bytes/depth32/values4096; формат не копируется в новый parser. Public envelope proposal закрыт: schemaVersion=strogo.admitted-invoke.v0.1, packageDigest/buildManifestDigest/proofDigest/toolchainDigest/contractApprovalDigest/releaseAdmissionDigest (lower64hex), result (existing canonical nested result). functionId остаётся в nested result; его недоступность при malformed input выражается existing null, не выдуманный export. validationOnly отсутствует в public wire: допуск определяется проверенной chain operator policy, не переключателем. Этот новый public envelope требует отдельного final wire review до EXEC; fixture envelope остаётся unchanged.

check result proposal: schemaVersion=strogo.module-check.v0.1, moduleDigest/bundleDigest/contractApprovalDigest/proofSourcesDigest/transcriptDigest (lower64hex), status=Verified. Отказы CLI до session output: existing canonical module error с schemaVersion=strogo.module-command-error.v0.1, command (check/build/run/admit), error {stage,code,details}; details в форме existing error codec, не arbitrary exception/host secrets. Exit0 successful check/build/Returned; exit2 Refused/invalid user input; unexpected internal failure нормализован InternalHostFailure, exit3 без raw secret-bearing exception на stdout. Library Open/Check/Build возвращает typed ModuleException до получения session, Invoke возвращает canonical Returned/Refused. Wire details/build result окончательно фиксируются в slice SPEC A/B; umbrella не выдаётся за полный producer wire implementation contract.

Позитивное interactive admit проверяется ручным exact digest вводом на disposable keys с явным validation provenance; это проверка механизма interactive signing, не реальный D02 semantic/release decision. Для D02 required real owner confirmation отдельно. Automated tty refusals или synthetic key подпись не подменяют этот AC.

Post-SPEC findings: HIGH CLI signature drift fixed by restoring frozen E05 check/build/admit/run options and exact check-dir replay; HIGH A-AC9 coverage fixed via explicit required active-pointer CAS slice/evidence before G02 claim; MEDIUM codec order clarified run preparse/typecheck before state/prover and already-open library Invoke before provider. No E05 amendment or approval history modified. Follow-up review pending.

Дополнительный post-SPEC HIGH: process-local high-water нельзя заменить lifetime одного OwnerTrust/host. Current OwnerTrust поля instance-local, отдельный обязательный B0 slice исправляет cross-host/reset-by-Dispose gap до public EXEC. MEDIUM D02 coverage исправлено отдельным AC двух correct Decide bodies + wrong negative + заранее frozen corpus/provenance. Все reviewer findings учтены в decomposition; re-review требуется.

Re-review: frozen explain --artifact projection command сохранён явно; no-execution/no-admission AC включён. Все3HIGH/2MEDIUM предыдущего review addressed; дополнительный MEDIUM explain addressed. Независимый final post-SPEC verdict ожидается.

Wire re-review: public envelope содержит literal proofDigest/toolchainDigest, сверяемые с held verified manifest, не только неявную связь. §7 общий compiler pipeline помечен check/build/admit; run codec-first и library Invoke выделены отдельными ветками. explain no-execution command сохранён. Follow-up final review.

Final post-SPEC independent review PASS: прежние HIGH/MEDIUM устранены в фактическом файле. Frozen CLI/library+explain preserved; ordinary codec-first branches distinct; literal proof/toolchain envelope identities; required active-pointerCAS and process-wide high-water B0; exact human-approved D02 two correct+wrong/frozen corpus. Umbrella decomposition не разрешает менять pending public closure semantics и не выдаёт deferred schema/pointer slices за реализованные. No code execution in reviewer; behavioural readonly при danger-full-access.
