# Подписанная сессия G02 и свежесть допуска

`G02SignedFixtureSession` — internal механизм для свежих проверенных fixtures. Он соединяет строгий JSON transport с существующими проверками owner approval и release admission. Это подготовительный шаг AC3/AC4; публичного `TrustedModuleRuntime` здесь ещё нет. Подписи в conformance создаются одноразовыми тестовыми ключами и не представляют решение человека о выпуске программы.

## Открытие и владение

Host передаёт путь пакета, ранее проверенный `G02VerifiedFixtureBuild`, общий `OwnerTrust`, provider текущего подписанного owner state, внешние release bytes, ожидаемый bundle digest и часы. Сессия копирует release bytes и владеет собственными `G02PackageSnapshot` и compiled fixture. Сначала проверяются semantic approval и release admission, их подписи, эпоха, политика, сроки и связь с proof/manifest/toolchain/package. Затем выполняются исходные proof/build/source/transcript проверки `G02CompiledFixture.Open` и загрузка сборки. Некорректный допуск не должен приводить к загрузке Candidate.

При ошибке Open собственные handles snapshot закрываются. `Dispose` освобождает fixture и snapshot; дальнейший Invoke всегда возвращает `CompiledFixtureDisposed` с `functionId=null`, в том числе для ошибочного JSON. Host сохраняет за собой `OwnerTrust`, provider и часы. Он обязан держать trust живым до закрытия всех сессий и не закрывать его одновременно с их работой. Lifecycle этой fixture не рассчитан на concurrent вызовы/Dispose.

## Проверка каждого вызова

Для живой сессии первым разбирается `strogo.invoke.v0.1`: malformed transport не читает state и не вызывает код. Перед каждым корректным вызовом provider читается ровно один раз; bounded bytes немедленно копируются, затем читается время. Semantic approval и release admission проверяются на **одной и той же копии**. Проверки состояния и `snapshot.Revalidate` завершаются до исходного typed dispatch Q→F. Допуск Open не кешируется как разрешение на последующие вызовы.

OwnerTrust хранит общий high-water эпохи и digest состояния. Создание другой сессии не сбрасывает его. Новая эпоха может отозвать approval; возврат к старой эпохе или другое подписанное состояние той же эпохи приводит к `OwnerStateRollbackDetected`. При истечении срока отказ сохраняет исходный код verifier, например `ContractApprovalExpired` либо `ReleaseAdmissionExpired`.

Provider null, `IOException`, `UnauthorizedAccessException` и `NotSupportedException` дают `admission/OwnerStateUnavailable`; больше65536 bytes — `OwnerStateLimitExceeded`. Release bytes ограничены65536 до копирования. Host должен обеспечить согласованность provider buffer до завершения копирования. Произвольное блокирование provider и враждебная concurrent mutation во время copy не прерываются этим механизмом; это доверенная граница host, не OS isolation.

## Ответ и смысл идентификаторов

Ответ — canonical JSON ровно с полями:

| Поле | Значение |
| --- | --- |
| `schemaVersion` | `strogo.fixture-invoke.v0.1` |
| `validationOnly` | `true` |
| `packageDigest` | `ownedSnapshot.Manifest.PackageDigest` |
| `buildManifestDigest` | artifact digest build manifest |
| `contractApprovalDigest` | artifact digest semantic approval, не bundle digest |
| `releaseAdmissionDigest` | artifact digest исходного подписанного release admission |
| `result` | вложенный объект `strogo.invoke-result.v0.1`, не JSON-строка |

Идентификаторы фиксируют артефакты успешного Open. При последующем отказе они остаются историческим происхождением сессии и не означают, что текущее выполнение разрешено. Вложенный refusal сохраняет `stage`, `code`, `entityId` transport/verifier. Правила output/resource refusal после выполнения сохраняются из [transport](g02-invocation-transport.md).

## Проверка и предел утверждений

Conformance использует свежий composed proof/build для scalar и allocation fixtures. Проверяет owner/reference output, signer/digest/expiry/epoch отказ до actual `AppDomain.AssemblyLoad`, exclusive ReadWrite reopen после ошибки и Dispose, новую копию state на каждый вызов, mutation исходных state/release buffers, expiry/rollback/policy/provider errors и отсутствие dispatch при отказе.

`DispatchAttempts` — диагностический счётчик адаптера непосредственно перед typed Invoke. Он не доказывает вход в native method. Предыдущие независимые Q/F native traces остаются отдельным свидетельством. Физическая identity publish closure, identity фактически загруженного runtime, public ABI, полный language core и реальные D02 human approval/release gates остаются открытыми. Placeholder closure digest старого fixture не становится production guarantee от добавления подписи.

История решений и окончательные результаты: K-G02-036 в [журнале знаний](knowledge-log.md); утверждённая область — [G02 SPEC](../specs/2026-09-29-g02-dotnet-r2r-admitted-modules-v0.1.md). Финальная текущая сборка: Debug0 warnings/errors; fresh apphost Admission343PASS exit0, включая58 transport checks и26 session checks каждой формы. [Raw evidence и receipt](evidence/g02-signed-session-20261001/README.md).

