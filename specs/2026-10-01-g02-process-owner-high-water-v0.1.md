# G02 B0 — Process-local owner-state high-water

## 0. Метаданные
Expanded delivery-task/QUEST, large trust change, owner Kibnet, baseline c3c56bd/main. Windowsx64/.NET10.0.11 SDK10.0.400; Codex/PowerShell7; model/reasoning unverified, no model eval. Canonical central template/stack+localAGENTS. Goal G02/G03 mechanism inside approved full goal. [Facade decomposition](2026-10-01-g02-public-execution-facade-v0.1.md), frozen E05 process-local invariant unchanged. Статус SPEC, independent review перед EXEC.

## 1. Outcome
Process-local `(approvalEpoch, ownerStateArtifactDigest)` не сбрасывается при new OwnerTrust/host/Dispose. Тот же pinned key authority на любом trusted host instance видит единый high-water. Success: signed N+1→N и same-epoch different bytes отвергаются между host instances и после disposal; valid same state recheck/newer epoch проходят. Invalid signature/policy не двигают high-water. Stop: registry reset-by-Dispose, race losing newer state, changing owner schema/hash or silent eviction.

## 2. AS-IS
OwnerAdmissionTrust.cs OwnerTrust хранит stateGate/highestEpoch/highestStateDigest в instance fields. VerifyOwnerState после strict parse/signature/policy вычисляет existing artifact digest и обновляет local tuple под instance lock. Current single-host checks подтверждают rollback только внутри Trust. New Trust с тем же key создаёт highestEpoch=-1. Это inspection-confirmed implementation gap относительно frozen E05, не доказанный exploit публичного runtime, которого пока нет.

## 3. Проблема
Повторное создание host/Trust в том же процессе позволяет начать новую историю epoch, противореча process-local contract. OwnerTrust Dispose не должен означать сброс authority history.

## 4. Design goals
Общий host-owned registry, existing cryptography/error codes preserved; новые typed host/resource refusals OwnerStateRegistryInvalid/OwnerStateTrustLimitExceeded без смены wire/schema; atomic update после полной проверки state; no extra disk/network/state writer; resource bound и scope explicit. Работа в нескольких assembly load contexts не должна случайно создать отдельные registries.

## 5. Non-goals
Persist high-water после process restart; OS/ACL protection operator store; IPC между разными процессами; new approval/release semantics; parallel use/dispose одного RSA object; public closure formula; resetting тестами production registry.

## 6. TO-BE
### 6.1 Ownership
Internal OwnerProcessStateHighWater в Strogo.Modules. OwnerTrust.VerifyOwnerState вызывает общий Observe(KeyId,epoch,existing artifactDigest) после Parse+VerifySignature+policy equality. Instance high-water fields удаляются; RSA/KeyId lifetime остаётся прежним. No public registry reset/set/provider API.

### 6.2 Registry identity/lifetime
Key=existing validated pinned SPKI SHA256 KeyId. Не меняем canonical key/owner-state digest. Один pinned key — одна owner authority; разные store paths того же key не создают независимые histories. Если оператору нужны разные independent authorities, разные ключи. Existing state format не включает separate authority namespace, поэтому store identity не может ослабить этот gate.

Registry держится process lifetime, не host/Trust lifetime. Core .NET10 single AppDomain; shared AppDomain data slot `strogo.owner-state.high-water.v0.2` содержит BCL-only Dictionary<string,KeyValuePair<long,string>>. Initialization/access guarded by shared AppDomain.CurrentDomain lock, не assembly-local static lock. BCL-only payload позволяет trusted Strogo.Modules copies в разных ALC видеть same tuple. Любая неправильная slot type — typed admission/OwnerStateRegistryInvalid, без reset/fallback. AppDomain data access/operator-host code и BCL остаются TCB; candidate language не имеет этих effects.

No entry eviction/removal/reset. Maximum1024 distinct successfully observed authority keys per process; existing entries remain available after cap, new authority at cap refuses admission/OwnerStateTrustLimitExceeded. Bound covers registry growth, не лимит числа Verify calls или owner process CPU. Registry allocation только после crypto+policy validation, invalid input не потребляет slot. Cap/test helper constants internal; boundary checks may use isolated registry dictionaries through internal pure Observe helper, но production owner path всегда shared slot.

### 6.3 Atomic algorithm
Под lock получить/create shared registry, проверить existing entry. epoch lower или same epoch/different artifactDigest → existing OwnerStateRollbackDetected. Same epoch/same digest → success без изменения. Greater epoch → replace tuple атомарно. New key → capacity guard затем add. Независимые keys не влияют на epoch друг друга. Signature/policy/hash computation до registry lock; acquired lock doesn't hold prover/file IO. Return existing VerifiedOwnerState только после successful Observe.

### 6.4 Scenarios
|Trigger|Outcome|
|---|---|
|TrustA accepts state1, TrustB same key observes state0|RollbackDetected|
|A disposed, newTrust state0|RollbackDetected|
|same signed bytes acrossHosts|accepted|
|same epoch valid differently signed artifact|RollbackDetected (existing byte identity rule)|
|forged future epoch/wrong policy|signature/policy refusal, prior state still valid|
|different key state0 after keyA epoch1|accepted independent history|
|trusted custom ALC copy same key after DefaultALC epoch1|state0 refusal|
|concurrent different Trust objects N/N+1|finalN+1; any subsequentN refuses|
|capacity1024/new key1025|limit refused, old keys still check|

## 7. API/integration
OwnerTrust signature/API unchanged. OwnerHostContext автоматически получает stronger shared history. No production backward-reset compatibility. Existing fixture session already shares one Trust, remains valid unless tests relied on recreation as reset. Existing caller supplies authenticated state; post-restart integrity still operator store TCB. Public facade B0 prerequisite, no claim public API exists.

## 8. Data/compatibility
No wire/domain/policy change; stronger enforcement existing invariant. Two valid re-signatures of identical payload at same epoch have different artifact bytes and were already rejected within one Trust; теперь правило process-wide. Existing tests reusing same key for unrelated timelines must provision distinct disposable keys или isolated processes; do not reset shared registry or relax expected codes just to pass. In particular main conformance epoch-proposal block currently advances shared fixture authority before later package checks — isolate that timeline cryptographically, preserving same negative behaviours.

## 9. Resource/failure limits
Finite1024entries ~bounded dictionary/string tuples; no key eviction undermininghistory. Wrong slot type fails closed. Host can mutate AppDomain data through arbitrary privileged .NET code: outside pure Strogo model, explicitly TCB, not claim adversarial host resistance. Process restart fresh registry expected/recorded. Don't add lock over signature RSA use; concurrent test uses distinct Trust instances, not shared RSA.

## 10. Rollout/rollback
Internal mechanism only; build/focused/full Admission, fixture/public closures remain separate. Rollback code doesn't authorize restoring stale signed owner state. No on-disk state mutation from registry. Detailed checkpointcommit/push, preserve failing-before/green-after evidence. No unrelated task-space fixes.

## 11. AC→evidence
|AC|Evidence|
|---|---|
|1 cross-host lifetime|actual signed states/newTrust/dispose host with N+1→N and sameepochdifferentdigest; pre-fix negative repro and post-fix pass|
|2 crypto precedence/isolation|forged future, invalid policy noadvance; separate keys positive; identical state positive|
|3 process scope/race|nondefault trusted ALC copy uses shared BCL registry; concurrent twoTrust final max; no resetting test hook|
|4 bound|1024/1025 isolated dictionary same production Observe algorithm, oldkey acceptance, failednewkey doesn'tgrow|
|5 regression|focused command+full solution build0/0+fresh full Admission apphost; existing fixture chronology isolated without weakening invariants|
|6 evidence/delivery|exact source/log hashes, independent postEXEC review, knowledge log/Obsidian/index, commit/push; Unlimotion result only if globalvalid|

## 12. Risks/objections
Why not static dictionary? static is per loaded assembly/ALC, weaker process scope. Why not perstore? key authority identity is current schema, new store path not permission to reset history. Why resourcecap? bounded lifetime registry cannot safely evict. Why signed bytes equality? unchanged frozen artifact high-water. Why no persistence? explicit normative process-local boundary, operator store integrity after restart remains TCB. AppDomain data collision/mutation fails closed when type invalid; malicioushost overwriting same shaped data remainshostTCB.

## 13. Execution plan
Independent postSPEC→focused pre-fix signed cross-instance reproduction without modifyingprod→implement shared registry→focused signed/ALC/race/cap controls→adapt only isolated conformancekeytimeline→build/fullAdmission→audit+knowledge+delivery. No repeated identical errors/timeouts; pinnedtools preserved.

## 14. Validation/evidence
Add focused --g02-owner-process-only switch in existing Admission executable, output concise structured report/diagnostic root. Test authority generated disposable key, no private exports. ALC probe load trusted current module DLL and KernelCore as needed, reflection only isolated host test; references released/unload bounded without claiming deterministicGC. Report exact current source/hostbinary hashes and actual signed input digest rows, fail-before raw. Full Admission apphost with pinned DOTNET_ROOT/PATH. No expensive optional proof rerun beyond full needed gate.

## 15. Delivery/knowledge
K-G02-047 records process high-water gap/rootcause/testscope same block canonical KB. Preserve production schema/history unchanged. Global goalG01–G06 and G02 remain Active. UI/video/modelbenchmark N/A backendmechanism. Owner semantic/release/closure questions not answered by this fix.

## 16. Review applicability/audit
Architect/security/concurrency/lifecycle/validation/delivery roles apply. Expanded independent reviews required; exact threat/TCB and ALC scope reviewed beforecode. Verify allAC have meaningful probes, no resetseam/bypass. Rubric not sole acceptance. Actualsandbox danger-full-access; reviewbehaviourreadonly.

## 17. Journal
2026-10-01: independent facadeSPEC review revealed existing instance-vs-process gap; inspected OwnerAdmissionTrust currentsource. ThisSPEC prepared beforecode; target fixes already-approved process-local invariant, no ownersemanticchoice. Independent postSPEC pending.

Primary-source design check: [Microsoft ALC guidance](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext) описывает разные Type/static scopes для custom ALC и обязательное sharing runtime assemblies; [AppDomain source overview](https://source.dot.net/System.Private.CoreLib/src/runtime/src/libraries/System.Private.CoreLib/src/System/AppDomain.cs.html) показывает delegation GetData/SetData к AppContext. Это основание предложенного BCL-only shared slot, не execution proof pinned10.0.11. Fetch точного GitHub v10.0.11 source не удался (cache miss); pinned-runtime cross-ALC regression остаётся обязательным, не заменён просмотром main source/docs.

Independent post-SPEC PASS: no B/H/M, new host/resource refusal codes distinguished from preserved existing rollback code. Executable pre-fix reproduction terminal PID348180/exit0: samehost state1→state0 refused, hostB same key/store acceptedepoch0, hostC afterDispose acceptedepoch0. Raw root owner-process-before-6c0dbfa2f5f74fd7b0a467ab45b452b2;12 exactcopies +5source/binary pins in docs/evidence/g02-owner-process-before-20261001. Initial probe ProjectReference path buildfailure retained; parser correction didn't execute and not fabricated as raw. Original fixture untouched, only dedicated clone state writes. Production fix not implemented yet; no fullAdmission claim. Next EXEC B0.

Independent pre-fix evidence audit PASS:12/12exactcopies,5/5source/binary SHA, PID348180/exit0/run/report match. Same host rejectsrollback, liveB and newC accept0. Probe observations gapReproduced=true не assertions; next post-fix automated regression must explicitly require OwnerStateRollbackDetected forB/C. Это beforefix evidence и review preparation, не implementation/security PASS. Reviewer source/evidence readonly при danger-full-access, no rerun.
