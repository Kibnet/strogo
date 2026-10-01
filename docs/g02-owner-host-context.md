# Operator-owned host context G02

`OwnerHostContext` — общий bootstrap для host application и owner CLI. Он загружает операторскую конфигурацию и ключ; это не допуск пакета и не `TrustedModuleRuntime`. Existing `approve-contract` и `state advance-epoch` используют его для trust anchor и fresh state bytes.

```csharp
using var host = OwnerHostContext.Open(operatorConfigPath);
var current = host.ReadCurrentState();
var state = host.Trust.VerifyOwnerState(current);
// Передайте host.Trust и host.ReadCurrentState в trusted verifier/runtime.
// Закройте всех потребителей до Dispose(host); borrowed Trust не закрывайте отдельно.
```

Конфигурация `strogo.owner-trust-config.v0.1` содержит ровно `schemaVersion`, `keyId`, `publicKeyPath`, `ownerStateStore`. Поля путей — абсолютные; путь самого config может быть operator launch relative path. Pretty UTF8 JSON допустим; BOM, unknown/duplicate/missing fields и неправильные types/schema отказаны. `keyId` — SHA256 DER SPKI bytes, проверяемый существующим `OwnerTrust`. Context читает config/key один раз и фиксирует Trust/store до Dispose. Изменения config/key после Open не переключают действующий context; rotation требует нового operator launch/context.

`ReadCurrentState()` открывает `owner-state.json` заново и возвращает независимые bytes. Это **не verified state**: caller проверяет подпись/политику/эпоху тем же borrowed `Trust`. Reader не сбрасывает high-water, не кеширует state и не создаёт store. Нет default key/config, fallback или signing. Пустые bytes читателя должны отказать у verifier; raw reader не подменяет crypto diagnosis.

Bounds до полного чтения: config<=65536 bytes, SPKI<=16384, state<=65536. Fixed max+1 buffer и цикл до EOF учитывают short reads. Overflow → `OwnerConfigurationLimitExceeded`/`OwnerPublicKeyLimitExceeded`/`OwnerStateLimitExceeded`. IO/доступ на config/key → `OwnerConfigurationUnavailable`, на state → `OwnerStateUnavailable`. Invalid DER/pin сохраняют `InvalidPublicKey`/`PinnedKeyMismatch`. Bad config → `OwnerConfigurationInvalid`; relative field path → `OperatorPathInvalid`; access после Dispose → `OwnerHostDisposed`. Lifetime nonconcurrent, Dispose идемпотентен и закрывает owned Trust.

Файл читается с `FileShare.Read|Delete`: cooperating writer может атомарно заменить state, пока reader получает snapshot одного открытого файла; inplace write на этом handle запрещён. Это не обещание «самый новый state к моменту return» при concurrent replace: read наблюдает до либо после commit, следующий read открывает текущий path. Exclusive writer lock/fsync/atomic replace existing epoch CLI сохранены, оба чтения epoch state остаются внутри lock.

Operator-owned config/key/store/ancestors и их ACL — trusted host boundary по E05. Helper не устанавливает и не аттестует ACL, не гарантирует запрет всех reparse aliases, не обеспечивает OS isolation или стойкость к произвольному процессу того же пользователя. Целостность store после restart — host assumption. Package/module/invocation не создают context и не задают trust-config.

Локальный focused runner `--g02-owner-host-only` создаёт одноразовые fixture keys, signed states и файлы:37checks на real atomic replacement, frozen config/key/store, rollback, подпись, пределы, parse errors, cleanup, disposed access и redirected CLI refusal. Production CLI не имеет fixture signing flag. Эти subprocess checks завершаются до context на TTY gate: положительное interactive signing здесь не проверялось; preservation подтверждено source review и library proposal/epoch checks полного Admission. Это не человеческое approval/admission.

[K-G02-041](knowledge-log.md), [утверждённая G02 SPEC](../specs/2026-09-29-g02-dotnet-r2r-admitted-modules-v0.1.md). Public check/build/admit/run, exact closure formula/D02 decisions и machine-execution evidence остаются открытыми.

Финальное локальное выполнение: Debug0warnings/errors, focused37PASS и full Admission444PASS/exit0, два независимых GUID host roots. [Evidence](evidence/g02-owner-host-20261001/receipt.json). Independent final source/evidence audit pending.

Финальный независимый аудит host-context checkpoint: PASS, новых B/H/M/L нет. Проверены5/5 current source SHA и11/11 exact copies; свежие build0/0, focused37PASS и fullAdmission444PASS exit0. Два host reports37checks/28rows из distinct roots, оба CLI tty refusalsexit2/stdoutempty/nooutput. Signed scalar/allocation58/33 и actual owner/reference outputs сохранены. LOW evidence scope ранее закрыта явной границей: positive interactive signing не запускался, raw state требует verifier, public module admission и весь G02 не завершены. File/source/log audit без independent consumer rerun, behavioral read-only при danger-full-access.
