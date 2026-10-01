# G02: отказ при ошибке owner-state provider

K-G02-040; baseline8281d27. [SPEC](../../../specs/2026-09-29-g02-dotnet-r2r-admitted-modules-v0.1.md), [контракт сессии](../../g02-signed-fixture-session.md).

Свежая Debug сборка без предупреждений/ошибок; полный apphost Admission407PASS exit0. Scalar/allocation:58checks/33rows каждая. Четыре типа ошибки host provider — InvalidOperationException, ObjectDisposedException, IOException, forged ModuleException — дают OwnerStateUnavailable до generated load (actual load delta0, exclusive reopen) и после Open (canonical Refused, dispatch0, read delta1). Malformed input сохраняет SchemaInvalid без provider reads. После восстановления owner state actual result совпадает с owner/reference; обе формы выполняют cleanup. Синтетический fault provider — internal test seam, не external module capability.

[Receipt](receipt.json):5 current source SHA и7 exact raw copies: build/admission/result и signed-session/compiled-invocations каждой формы. Full raw fresh replay retained privately. Старые tests, null/oversize/epoch/expiry и runtime binding checks проходят в том же full run. OOM/AccessViolation не обещаны recoverable; clock/crypto failures вне provider catch. Не TCB proof, не public admission, не human D02/release, не G05/G06 measurement. Независимый final audit pending.

Unlimotion result recording pending: свежий CLI validator отказал на посторонней несогласованной связи; чужие задачи не изменялись. G02/full goal active. CI и синхронизация Obsidian не проверены.

Финальный независимый scoped audit provider continuation: PASS, замечаний B/H/M/L нет. Проверены5/5 текущих source SHA,7/7 exact raw copies, build0/0 и Admission407PASS exit0. В обеих формах58checks/33rows:4no-load с loads0/cleanup,4late Refused/admission/OwnerStateUnavailable/readDelta1/dispatch0,4restored Returned/readDelta1/dispatch1 с совпадением actual owner/reference vector. Исходные null/oversize/epoch/expiry rows сохранены; malformed для каждого exception проверен исходниками. Аудит файлов/журналов без независимого consumer rerun, behavioral read-only при danger-full-access. Checkpoint проверен; общая цель не завершена.
