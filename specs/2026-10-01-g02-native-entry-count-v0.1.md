# G02 — Quiescent native entry count для signed observer

## 0. Метаданные
Expanded high-risk diagnostic infrastructure; parent G02 AC3/AC5, baseline8b36093. Same central QUEST/review stack/local AGENTS, .NET/native Windows x64 fixture; MSVC14.44.35207/NETFXSDK4.8, SDK10.0.400/runtime10.0.11 frozen. Codex PowerShell surface; model eval N/A (native diagnostic mechanism). Existing goal approval covers mechanisms; public closure identity/D02 human admissions not approved here.

## 1. Outcome contract
Следующий G02 результат — signed native observer на actual OpenForObserver/OwnerHostContext/fresh replay. Необходимая часть этого результата: при каждом refusal показать отсутствие Q/F native entries в этом интервале, не только adapter DispatchAttempts. Success checkpoint: bounded read-only export счётчика, no hook changes, accepted quiescent prefix равен final native trace; invalid/inactive/overflow reads refuse. Output: source, focused cold subprocess fixture, exact evidence и knowledge. Не объявлять signed observer integration/full G02 завершёнными. Stop on native count/trace discrepancy; не удалять отрицательные результаты.

## 2. AS-IS / проблема
Observer.cpp хранит ObserverCount/ObserverSlots4096/ObserverReady и Active/Hooks/Overflow; Enter.asm выполняет allocation-free hook. Callback Shutdown сериализует enter rows. Existing signed file-backed session реализована и Admission492PASS, но не подключена к profiler. Текущий host может видеть только aggregate trace на выходе; aggregate не доказывает отсутствие entry в каждом refused request interval.

## 3. Цели / границы
Добавить считывание уже собранного prefix вне hook, с typed HRESULT отказом и bounded4096 inspection. Не вводить reset/filter/skip entries или write API. Существующие COM ABI/hook/mapper/trace schema и ordinary observer tests сохранить. Non-goals: public run/admission, hostile process isolation, mapped/native R2R instruction digest, deterministic GC, G05/G06. Success dependency не заменяет окончательный signed trace join.

## 4. TO-BE
Observer.cpp экспортирует `HRESULT __stdcall StrogoObserverReadEntryCount(unsigned* count)`; count pointer null => E_POINTER; иначе output=0 до любых проверок. Shared lifecycle lease против Shutdown, Active1, Hooks0, Overflow0, count0..4096, все Ready[0..count)1. Проверить traceFailed под shared traceLock. Удерживать shared traceLock до публикации output; повторно проверить traceFailed/Hooks0/count unchanged/Overflow0 и Active1 непосредственно перед output. Active0 на финальной проверке => E_UNEXPECTED/output0. Shared lifecycle lease предотвращает cleanup, но не сброс Active в начале Shutdown; S_OK означает наблюдённый active ready prefix на финальной проверке, не lease против начала Shutdown сразу после неё. Последующий traceFailed аннулирует весь run, даже если прежний count был успешен. Только при полном ready prefix и отсутствии ошибок S_OK/count; иначе E_FAIL либо E_UNEXPECTED для inactive. Count read через Interlocked primitives; нет новых изменений Enter.asm. Экспорт через __declspec(dllexport) либо .def; ABI x64 unsigned32/HRESULT32. Никаких ожиданий hook/drain/GC: незавершённый prefix — refusal.

Caller contract: nonconcurrent diagnostic host, после синхронного Invoke до следующего invocation; parallel control ждёт все Target callbacks перед read. Export не linearizable lease против новых враждебных concurrent callbacks; valid prefix не обещает отсутствие дальнейших entries. Lifecycle/trace locks используются только вне entry hook. Faulted/inactive/overflow count никогда не принимается частично. Pointer валидность кроме null относится к trusted native host.

Новый CountProbe рядом с существующим Probe, no package dependency. Exact profiler DLL absolute path из host environment, NativeLibrary.Load/Free balanced; DLL держит parent READ/no-write/no-delete handle. Нет default lookup/fallback. Delegate fixed Windows x64 ABI, optional null-pointer delegate control. Existing Run-Observer builds fresh native DLL и прежние4controls; новый Run-CountObserver принимает этот output, builds isolated probe, child environment explicit/cleared, pinned dotnet, forced JIT и Target type filter Probe. Child deadline20s + Kill tree5s/output5s, Process finally. Inactive child без profiling, не объявлять native startup состоявшимся. Parent held DLL/hash до/после и source/binary receipts.

## 5. Сценарии / AC→evidence
|AC|Trigger|Expected|Evidence|
|---|---|---|---|
|1|sequential count reads before/after2Target calls|0,1,2; actual outputs42; final ordered2entries|child JSON + final trace/PID/map joins|
|2|same-output Decoy calls|0→0, trace0/maps0|cold child + final trace|
|3|joined parallel1024 calls|accepted1024/full ready; final1024|child JSON + shutdown/enter joins|
|4|exact capacity4096 then one more|accepted4096 then E_FAIL/output0; final trace refuses overflow/no partial entries|child JSON + shutdown traceFailed|
|5|inactive loaded DLL / null count pointer|inactive E_UNEXPECTED/output0, null E_POINTER; no accepted fake initialization|separate child no profiler + active null control|
|6|regression/bounds/identity|original call/no-call/parallel/overflow still pass; same pinnedDLL source/counters/header/tool identities|fresh Run-Observer receipt + count runner hashes/limits/review|

## 6. State / decisions / alternatives
Inactive→read refusal; Active quiescent→S_OK immutable observedprefix; Active pending/overflow/tracefailed→refusal; Shutdown→Inactive. Не поддерживать новый concurrent session contract. Alternatives: separate fresh proof process per request даёт no-entry totals, но повторяет expensive replay для каждого refusal; aggregate-only misses interval attribution. Выбран native quiescent counter, который будущий signed child сверяет с финальным trace и ordered Q/F metadata/file identities. No hidden bypass of admission or proof.

## 7. TCB / risks / rollback
Native CLR profiler/hook/compiler/host namespace already trusted. Module input не задаёт DLL path или mapper target. Read API outside hot hook не меняет observed program semantics; measurement performance claims запрещены. Lifecycle→trace lock order совпадает с mapper, avoid inverse locking. Overflow/failure rejects trace even if earlier counts accepted. Revert export/newprobe/runner/docs, old ABI preserved, no migrations. Parent native DLL held for all children; diagnostic file identity no full toolchain immutable claim. No private owner keys exported in this checkpoint.

## 8. Delivery / validation plan
Independent post-SPEC then EXEC; run existing Run-Observer once to build fresh DLL and verify4baselinecontrols; run new count runner against that DLL once, sources/binaries/rawcopies receipt. No source changes after accepted executions without relevant rerun. Post-EXEC independent evidence audit. Source changes Observer.cpp only; Enter.asm unchanged; new CountProbe csproj/NuGet.Config/Program and Run-CountObserver. Significant insight K-G02-044 + canonical Obsidian/index in sameblock. Native count is prerequisite; next stage still exports ephemeral public config/state/release fixture and combines OwnerHostContext+fresh proof/build+OpenForObserver inside separate coldprocess, checks signed envelopes/file metadata/QF intervals. No public schema/human release choices here.

## 9. Quality / instructions / objections
Expanded template applicability: architecture/security/native/.NET validation/delivery roles apply; UX/video N/A diagnostic backend. Linter1–5 outcome/as-is/problem/targets/non-goals;6–10 responsibility/API/errors/resources;11–13 compatibility/rollback/no migration;14–16 concreteAC/cold tests/build;17–19 independent phases/autonomy/scope;20 native/host profile. Review must check lock ordering, integer bounds, no reset/in-progress acceptance, caller nonconcurrency, ABI/null/inactive, output/trace comparability, unsupported claims. Objections: «read changes hook?» no; «counter equals native instruction attestation?» no; «ready counter proves signed admission?» no; «concurrent host supported?» no. Rubric evaluation pending independent review, no scores replace actual execution. No open normative question for internal counter; public gates unchanged.

## 10. Журнал
2026-10-01: current Observer.cpp/Enter buffer/Shutdown and signed runtime inspected. Identified aggregate-only attribution gap. SPEC prepared; production code unchanged; independent review requested before implementation under goal-scoped approval.


Post-SPEC review MEDIUM: Shutdown сбрасывает Active до exclusive lifecycle lease. Исправлен contract финальной Active проверки, trace lock удерживается до output, prefix observation не является lease против будущего Shutdown; final trace failure аннулирует run. Повторное review запрошено, EXEC не начат.

Post-SPEC re-review PASS, MEDIUM закрыт, новыхB/H/M нет. EXEC начат: native export вне unchanged entry hook и isolated probe.

## K-G02-044 — Native prefix позволяет проверять интервал вызова, но не даёт lease
- Статус: считывание native buffer реализовано и проверено в5cold controls; прежние4controls прошли свежую регрессию. Signed runtime interval integration ещё не выполнена.
- Утверждение: Shutdown-only aggregate trace не доказывает отсутствие Q/F в каждом refused interval. Read-only native export читает bounded ready prefix вне неизменённого allocation-free hook. Before/after snapshots и final ordered trace становятся входом следующего signed observer join.
- Review insight: Shutdown flips Active до exclusive lifecycle lock; одной initial Active проверки недостаточно. Contract/implementation требуют финальную Active проверку, shared trace lock до output и invalidation всего run при final trace failure. Snapshot означает observed prefix, не concurrent/Shutdown lease.
- Evidence: [SPEC](../specs/2026-10-01-g02-native-entry-count-v0.1.md), [контракт](../docs/g02-native-entry-count.md), [rawcopies](../docs/evidence/g02-native-entry-count-20261001/README.md).9process exit0, nativeWX/managed0/0;7current source/6binary hashes,25exactcopies. First driver failure missing JSON count→PowerShell intrinsic1 сохранён; explicitnull/property check fix проверен fresh5rerun.
- Границы: Probe.Target nativecount не signed fixture/file SHA/public admission/native R2R instruction digest; timeout/in-progress/Shutdown race branch не исполнены. Overflow final trace rejected even earlier read succeeded. Не засчитывать это за G02/full goal completion/G05/G06.


Expanded quality audit: linter1–5PASS outcome/ASIS/boundedcount/nongoals;6–10PASS sharedlocks/API/typedHRESULT/bounds;11–13PASS oldABI/ASM unchanged/source-onlyrollback;14–16PASS AC1–6 actualcoldreads/finaltrace/baselinechecks;17–19PASS reviewedscope/goalapproved/incrementaldependency;20PASS native/.NET diagnosticprofile. Rubric6×5=30 planning: concrete outcome, inspected native buffers/Shutdown, ready/count guards, explicit TCB/rollback, cold evidence matrix, separate public gates. Не заменяет фактическое review/runtime audit.
Role/depth: security/architect PASS lock order+snapshot-not-lease, validation PASS5coldcontrols+4regression/source-checked cases, delivery PASS heldDLL/clearedchildenv/no keys, domainworkflow PASS signedobserver prerequisite, UX N/A backend. Scope/Evidence/Contract/Adversarial passes identifiedMEDIUM pre-EXEC and fixed/re-reviewed Active finalcheck; post-EXEC source/raw PASS no substantivefindings. No-findings justification: nativeguards/refusals/ABI align with SPEC, Enter.asm unchanged, actualreturns match finaltrace, parent independently verifies rows/PID/map/overflow. Manualchallenge: in-progress/Shutdown/timeout notexercised; no concurrent/clock/performance/wholemodule guarantee; successfulcounter inoverflow run notacceptedtrace. Finalexactcopy/source/binaryaudit pending.

Финальный independent source/evidence audit PASS, новых B/H/M/L нет:7/7current sourceSHA,6/6binarySHA,25/25exactcopies (baseline11/current11/history3). Same ObserverDLLSHA в обоих receipts; Enter.asm unchanged. Все9process exit0, пятьcountchildren timedOutfalse/stderrEmpty. Active traces init1/shutdown1/PID/map joins; counts2/0/1024 совпадают с finalentries; capacity4096→overflowE_FAIL/0 и finaltraceFailed/enter0. InactiveE_UNEXPECTED/0 безtrace, nullE_POINTER. Historical driverfailure/pre-fixbaseline script hash отделены от currentsource acceptance. NativeWX/managed0/0 проверены raw. Final audit behavioural readonly при danger-full-access, без rerun. Signed/modulefile/publicadmission/G05/G06 и timeout/Shutdown race evidence не расширены. Fresh Unlimotionvalidate isValid=false, privatevalidation excludedfromcopies; новый result не записан, цель активна.
