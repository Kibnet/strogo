# G02 — Чтение native entry prefix

`StrogoObserverReadEntryCount(unsigned*)` — диагностический экспорт Observer.dll для чтения уже заполненного буфера FunctionEnter3. Enter.asm не изменён: в hook нет новых calls/locks/allocations/IO. Вызов делают между синхронными операциями trusted host; это подготовка к связыванию каждого отказа signed session с отсутствием Q/F входов.

Чтение обнуляет output, проверяет Active, Hooks, Overflow, count≤4096 и Ready всех элементов prefix. Lifecycle shared lease предотвращает cleanup; shared trace lock удерживается до выдачи output. Финальные проверки activity/count/hooks/overflow/traceFailed обязательны. Пустой указатель даёт E_POINTER, inactive — E_UNEXPECTED, incomplete/overflow/failed prefix — E_FAIL. Успех означает active ready prefix на финальной проверке; это не lease против последующего Shutdown или concurrent callbacks. Финальная ошибка trace аннулирует весь run, включая ранее успешные count reads.

## Проверенный профиль

MSVC14.44.35207/NETFXSDK4.8, pinned SDK10.0.400/runtime10.0.11 Windows x64, forced JIT. [SPEC](../specs/2026-10-01-g02-native-entry-count-v0.1.md) и [evidence](evidence/g02-native-entry-count-20261001/README.md).

Свежий original Run-Observer:4cold controls call/no-call/parallel/overflow. Новый Run-CountObserver:5cold controls sequential/no-call/parallel/overflow/inactive. Все9processes exit0. Native build /W4 /WX и managed builds0warnings/errors. Sequential snapshots0,1,2 и final2entries; decoy0 и no mappings; joined parallel1024 и final1024entries. Capacity4096 сначала читается полностью; следующий entry приводит E_FAIL/output0 и отказу всего final trace без partial enter rows. Inactive E_UNEXPECTED/output0; active null pointer E_POINTER.

Первый parent driver отказал после успешного sequential child: у JSON-строки null-pointer отсутствовал count, а PowerShell подставил intrinsic Count=1. Исправлено явным count:null и требованием настоящего JSON property. Fresh rerun прошёл все5controls; failed raw evidence сохранена. Timeout, in-progress callback и Shutdown race ветви не исполнялись; отсутствие гонки не выведено из этих запусков. Они ограничены контрактом/guard checks и review.

## Границы

Это native counter и final metadata/PID/FunctionID joins над Probe.Target, не signed fixture/file digest observation или public admission. Нет mapped instruction/ReadyToRun attestation, sandbox isolation и G05/G06 результата. Следующая часть соединяет этот counter с OwnerHostContext, fresh proof/build replay и OpenForObserver, затем сверяет orderedQ/F и module-fileSHA с signed response identities. Сам счётчик не доказывает правильность допуска.

Финальный independent source/evidence audit PASS, новых B/H/M/L нет:7/7current sourceSHA,6/6binarySHA,25/25exactcopies (baseline11/current11/history3). Same ObserverDLLSHA в обоих receipts; Enter.asm unchanged. Все9process exit0, пятьcountchildren timedOutfalse/stderrEmpty. Active traces init1/shutdown1/PID/map joins; counts2/0/1024 совпадают с finalentries; capacity4096→overflowE_FAIL/0 и finaltraceFailed/enter0. InactiveE_UNEXPECTED/0 безtrace, nullE_POINTER. Historical driverfailure/pre-fixbaseline script hash отделены от currentsource acceptance. NativeWX/managed0/0 проверены raw. Final audit behavioural readonly при danger-full-access, без rerun. Signed/modulefile/publicadmission/G05/G06 и timeout/Shutdown race evidence не расширены. Fresh Unlimotionvalidate isValid=false, privatevalidation excludedfromcopies; новый result не записан, цель активна.
