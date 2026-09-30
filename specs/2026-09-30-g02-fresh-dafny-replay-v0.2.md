# G02: свежая проверка Dafny и воспроизводимый proof transcript

## 0. Метаданные

- Тип: `delivery-task` / QUEST / expanded security and public artifact contract. Владелец смыслового и release-допуска: Kibnet. Статус: **утверждено владельцем для EXEC** точным ответом «Спеку подтверждаю» на revision commit `1281ef3cc631f0f874e2cce4ee38ba039d78aee3`, blob `a4dbc26610e156c50c00cb1c368abb077c114232`, SHA-256 файла `A0DBEAB7A13E4EB610FF682BEF44D8A81AE2AAD8DBB660C022509947F72DAEB1`.
- Основание: [утверждённая G02 SPEC](2026-09-29-g02-dotnet-r2r-admitted-modules-v0.1.md), включённый ею exact [E05 amendment](2026-09-07-e05-two-stage-admission-amendment.md), [утверждённый proof wire](2026-09-30-g02-proof-artifact-wire-v0.2.md), K-G02-011. Масштаб medium; target Windows x64; ветка `main`.
- Effective runtime: Codex desktop для разработки, не часть proof identity; pinned Dafny 4.11.0 и .NET SDK 10.0.400. Eval baseline: fixture-only stored metadata и proof-input regeneration, 74 Admission checks; реальный Dafny probe `4 verified, 0 errors` ещё не нормализован. UI/visual/video evidence не применимо к CLI/library.
- Stop: этот документ сам не является ни human approval D02 bundle, ни release admission, ни свидетельством правильности Dafny/.NET TCB.

## 1. Цель и AS-IS

После `G02StoredIdentityVerifier` и `G02ProofSourceRegenerator` самосогласованные `proof.json` и transcript всё ещё можно подделать при неизменном source. Нужен свежий запуск закреплённого verifier по удерживаемому source, сравнение повторно полученного canonical transcript и obligations с пакетом и отказ **до** human release projection при любом расхождении. Успех этой SPEC — воспроизводимая цепочка `held module/bundle → generated source/map → Dafny result → normalized transcript`, не право исполнения.

Код находится в `src/Strogo.Modules/G02*`; conformance — `tests/Strogo.Modules.Admission.Conformance`. Сегодня `toolchainDigest`, `evidenceDigest` и переход от целого вывода Dafny к вектору Strogo obligations не имеют достаточно точного нормативного вычисления. Переиспользование произвольного флага `Verified` в JSON нарушит E05 A-AC2/A-AC3.

## 2. Граница и решения

- `check` сначала сверяет host-owned current state, ключ и approval; затем held module/bundle, source/map; только после этого запускает Dafny. `build` и `admit` независимо повторяют тот же порядок на новом живом snapshot и current state. Ни один из трёх переходов не полагается на сохранённый transcript как на oracle.
- G02 `v0.2` **producer/replay profile** генерирует ровно один proof source `content/candidate.dfy`. Общая manifest/wire-схема продолжает связывать все перечисленные sources и может иметь больше одного; пакет с несколькими источниками получает типизированный отказ `ProofSourceCountUnsupported` на этой стадии. Расширение producer profile до нескольких sources требует отдельного решения, а не молчаливого игнорирования.
- Включённая Dafny distribution самодостаточна (`dafny.runtimeconfig.json` указывает included .NET 8.0.19; `hostfxr.dll` и `coreclr.dll` лежат в inventory). Внешние OS/CPU остаются TCB. Никакой исходник пакета не добавляется в Dafny `PATH`/load path как свободная зависимость.
- Не меняем E05 schema, утверждённые три digest/tag, owner approval/admission wire, E06 validation-only и R2R сборку. Не выводим каждый solver VC из stdout: весь generated source проходит Dafny как единая единица, а records представляют Strogo obligations из deterministic lowerer. Эта связь должна быть видна в evidence и review.

## 3. Exact proposed contract

Пусть `H(tag, bytes) = lowercase hex SHA256(UTF8(tag + "\n") || bytes)`, а `C(x)` — canonical UTF-8 JSON с ordinal-сортировкой ключей, как в утверждённой proof-wire SPEC. Все SHA-256 файлов — lowercase hex от exact bytes.

`toolchainDigest = H("strogo.proof.v0.2/toolchain", C(identity))`, где `identity` имеет ровно поля:

| Поле | Значение |
| --- | --- |
| `schemaVersion` | `strogo.proof-toolchain.v0.2` |
| `dafnyVersion` | `4.11.0` |
| `dafnyArchiveSha256` | SHA-256 `.tools/downloads/dafny-4.11.0.zip`, равный **скомпилированной в trusted host** константе `3653f05a111ca21e234709ea7b25ce96083fd6c6f10484256ba54110dbc0654d`; `tools/dafny.json` сверяется с ней, но не является trust root |
| `dafnyFiles` | массив ровно всех 290 regular files под `.tools/dafny/dafny`, path/sha256, ordinal по path, без symlink/reparse/extra/missing; `H("strogo.proof.v0.2/dafny-files", C(dafnyFiles))` равен **скомпилированной** константе `a1fcd83db15c8806190a5df8376142fb4c288e4b1d86a8e40b9eae3579fcba32` по [утверждённому erratum](2026-09-30-g02-dafny-inventory-vector-erratum-v0.2.md); `tools/dafny-files.json` сверяется, но не выбирает ожидаемые hashes |
| `verifierArguments` | exact array `verify`, `candidate.dfy`, `--enforce-determinism`, `--cores`, `2`, `--verification-time-limit`, `15` |
| `modulesAssemblySha256` | SHA-256 загруженного trusted `Strogo.Modules.dll` |
| `kernelCoreAssemblySha256` | SHA-256 загруженного trusted `Kernel.Core.dll` |

Перед запуском replay независимо сверяет archive, весь inventory, exe (отдельная trusted-host константа `aebb6e5ea4aa1a8ba5432ee214c47255fd7aa5a583f5cac8e83d27d9992c4dd0`) и два **уже загруженных trusted host** assembly с identity; digest из package и JSON-каталоги в репозитории не выбирают executable, hashes, assemblies или flags. `Strogo.Modules.dll`/`Kernel.Core.dll` относятся к доверенному host deployment и не загружаются из candidate/package; возможность заменить host binary до старта лежит вне package threat model и требует отдельной установки/подписи host. В `check` producer записывает вычисленный digest, а `build/admit` требуют equality с локально вычисленным. Смена trusted assemblies, Dafny files или flags даёт новую identity и требует нового proof/package; это не автоматическая миграция. Exact исходный snapshot `tools/dafny-files.json` даёт 290/290 files и independent digest выше; это подтверждено локальной инвентаризацией, но ещё не AC evidence.

Replay создаёт приватный каталог, записывает **только удерживаемые после regeneration** bytes в `candidate.dfy`, запускает проверенный absolute `dafny.exe` с exact args, фиксирует exit code, timeout и bounded streams (каждый ≤1 MiB, wall clock ≤60 s, kill process tree на timeout). До `Process.Start` verifier **сначала открывает и удерживает** Windows no-write/no-delete handles (`FILE_SHARE_READ`) для всех 290 tool files, exe, всех каталогов tool closure и записанного `candidate.dfy`; затем проверяет exact directory inventory, атрибуты/final paths и SHA-256 **только через удерживаемые handles**. Непосредственно перед запуском заново сверяет path identities и inventory, чтобы имя передаваемого executable и разрешаемые DLL всё ещё указывали на удерживаемую closure; private work directory не подставляется из package и проверяется на reparse/extra files. После завершения повторно сверяются held bytes и inventory. Если корректное удержание closure невозможно, replay отказывает до запуска; простая path-check→open последовательность запрещена. Host-owned tool directory также должен запрещать candidate identity создание дополнительных файлов ACL-правами. Дочерний процесс получает очищенное environment: только `SystemRoot`/`WINDIR` из доверенного host, `TEMP`/`TMP` = private work directory, `PATH` = pinned Dafny directory и host `System32`; `DOTNET_ROOT`, `HOME`, `USERPROFILE`, `DAFNY_*` и прочие унаследованные переменные отсутствуют. Иной путь DLL resolution запрещён. Администратор ОС и уже скомпрометированный host вне модели угроз; это не OS sandbox для исходника.

Успех возможен только если exit code 0, нет timeout/output overflow, stderr пуст, а stdout после strict UTF-8 decode соответствует ровно `(?:\r?\n)?Dafny program verifier finished with N verified, 0 errors\r?\n`, где `N` — decimal без ведущих нулей и `N > 0`. CR допустим **только** непосредственно перед LF, весь raw вывод остаётся вне normative transcript. Любая иная строка, warning/diagnostic или неуспех — typed `VerifierOutputUnrecognized`/`VerifierFailed`, transcript `Verified` не создаётся. Это проверяет exit/summary, но корректность Dafny/Boogie/Z3 остаётся TCB. Исследовательский Windows probe дал raw stdout `0D0A` + summary + `0D0A`, stderr 0 bytes, exit 0.

`evidenceDigest` успешной записи — `H("strogo.proof.v0.2/evidence", C({"verifiedUnits": N}))`. Это digest **общего** machine-readable summary запуска, а не отдельный сертификат каждой VC. Для всего source при успешной проверке каждый generated obligation из `ModulesDafnyLowerer.Lower(...).Obligations` превращается в record `obligationId=Id`, `kind=Kind`, `status=Verified`, `diagnosticCode=verified`, с этим общим `evidenceDigest`. IDs должны быть unique, records сортируются ordinal по `obligationId`, количество 1..4096; иначе отказ. `N` намеренно **не** приравнивается к числу records: Dafny считает свои verification units, а Strogo records указывают на проверенные свойства generated source. `proof.json.obligations` — тот же vector без `diagnosticCode`. `outcome=Verified` только при полном успехе; никакой partial success не принимается. Изменение `N` между запусками меняет transcript и даёт `NonDeterministicVerifierOutput` или replay mismatch.

Frozen independent vector для `N=4`: `C({"verifiedUnits":4})` = UTF-8 `{"verifiedUnits":4}`, `evidenceDigest = efa441ff0a8b21a1f432303197a08e3794e0f32da63cf5763680ad3247e45572` (Python `hashlib/json`, отдельно от future producer).

Нормативный transcript имеет exact E05 schema и canonical bytes: `schemaVersion=strogo.proof-transcript.v0.2`, локально вычисленный `toolchainDigest`, wire `proofSourcesDigest`, `outcome=Verified`, `records` выше. Wall time, absolute path, process ID, locale и raw stdout/stderr отсутствуют в transcript и digests; `N` входит только через shared evidenceDigest. Bounded raw diagnostics остаются вне package. После replay `build/admit` требуют byte-equal transcript и vector, плюс три уже утверждённых digest и identity chain. `check` делает два clean replay в разных private dirs и при отличающихся canonical bytes отказывает `NonDeterministicVerifierOutput`.

## 4. Сценарии и состояния

| Сценарий | Триггер | Наблюдаемый результат | Evidence / AC |
| --- | --- | --- | --- |
| R1 | Тот же approved fixture проверен дважды | Byte-equal transcript/vector, один toolchain digest | два clean runs / AC1–2 |
| R2 | `proof.json` и transcript пересчитаны под ложный `Verified` при честном source | До `admit` typed refusal; release prompt отсутствует | forged fixture / AC3 |
| R3 | Истёк approval или изменён current epoch вместе с forged proof | Owner refusal до запуска prover | process-spy / AC4 |
| R4 | Заменён Dafny DLL/exe, добавлен file, изменены flags или lowerer assembly | Toolchain mismatch, prover не запускается | inventory negatives / AC5 |

`BuiltNotAdmitted` остаётся таким при любом отказе replay; successful replay ещё не создаёт `Admitted`. На новом `admit` gate нужен новый snapshot/current owner state и новый процесс Dafny. Empty sources, extra source, 0 obligations, timeout, output overflow, concurrent package mutation — отказ без release prompt. Все bytes для replay читаются из held snapshot; временный каталог удаляется после сохранения bounded diagnostic/evidence, в package не попадает.

| Решение | Owner | Предложение | Confidence | Риск при ошибке | Нужно human до EXEC |
| --- | --- | --- | ---: | --- | --- |
| Формула toolchain/evidence и нормализация whole-source success | Kibnet | §3 | 0.75 | Ложный proof identity или неверная интерпретация records | Да, exact approval SPEC |
| Один source в текущем producer profile | Kibnet | §2 | 0.85 | Непреднамеренное сужение общего manifest | Да, exact approval SPEC |
| D02 смысл и release package | Kibnet | отдельные два решения | 1.0 | Ложное объявление G02 | Да, позже |

## 5. Данные, интеграция, rollback

Source of truth для схемы — E05; три digest остаются из approved proof-wire; эта SPEC уточняет toolchain/evidence и replay. Публичных новых полей нет. Неизвестный/старый digest или transcript не мигрируется; новый check создаёт новые artifacts. `check`/`build`/`admit` вызывают общий replay verifier, но каждый независимо читает current trust и работает с собственным snapshot; `admit` повторяет inventory после previous build. Runtime `Load` позже отдельно проверит release admission и closure; replay не заменяет его.

Rollback до release admission: отключить новый producer/replay path, удалить только disposable candidate packages; owner state и approvals не менять. Если появились signed release admissions, rollback и revocation идут по E05 процедуре, а не удалением журнала.

## 6. Acceptance-to-test matrix

| AC | Обязательная проверка | Evidence | Неуспех |
| --- | --- | --- | --- |
| AC1 | Независимо вычисленный `toolchainDigest` fixed vector, inventory 290/290 и evidence-summary digest при `N=4` | exact preimage/sha256 в conformance | блокирует integration |
| AC2 | Два clean Dafny replay для двух fixture shapes, byte-equal transcript/vector; real exit/summary сохранены вне package | bounded reports и canonical artifact hashes | блокирует build/admit |
| AC3 | Самосогласованная подделка proof/transcript с пересчитанным manifest, заменённые vector/code/source map | typed refusal до prompt | блокирует admit |
| AC4 | Forged/expired/revoked approval и epoch rollback до process start, runtime timeout/overflow/unrecognized output | process-spy и refusal matrix | блокирует admit |
| AC5 | DLL/exe/extra file, wrong flags/assembly hash; tool file substitution после inventory, private source race, held snapshot race и source-count >1; env-injection через `PATH`/`DOTNET_ROOT` | mismatch/refusal matrix и process spy | блокирует admit |
| AC6 | Locked restore/build и Modules, Graph, Kernel, Notation, Portability, Experiments, Admission suites; independent post-EXEC review | command logs, Git diff/commit, knowledge ID | блокирует checkpoint |

Первый этап validation — fixture-only replay; production `check/build/admit` подключать после AC1–5 и независимого review. Нет автоматического G02 AC3 без D02 bundle approval, compiled closure, human release admission и runtime evidence. Обычный CLI сценарий и standalone consumer проверяются в позднем G02 AC4; диагностический Dafny probe не подменяет их.

## 7. Риски, альтернативы и review

Вероятное возражение: summary `N verified` не раскрывает VC каждого Strogo obligation. Это осознанное отображение whole-source verification в deterministic lowerer records; альтернативой было бы инструментировать Dafny/Boogie и pin’ить machine-readable VC IDs, что расширит TCB и объём. Если reviewer сочтёт отображение недостаточным для proof claims, остановить SPEC с `ASK-HUMAN`, а не переименовать aggregate success в granular proof. Другое возражение: DLL hash меняется при перекомпиляции; AC2 и двухкорневая проверка должны показать воспроизводимость или выявить необходимость отдельной source closure identity. Третье: `stderr пуст` может быть несовместимо с платформой; fail-closed приемлем для одного Windows target, фактический probe нужно повторить по exact args.

Роли review: tester проверяет AC1–6; architect — canonical identity, source/record mapping; security — trust order, tool/path/TOCTOU и неудачный stdout; delivery — отсутствие раннего admit/Load. UI/UX неприменимы. SPEC linter, rubric и post-SPEC review записаны ниже; advisory reviewer имел фактически writable sandbox, что отражено в ограничении review evidence.

## 8. Файлы и план

| Файл | Предполагаемое изменение | Причина |
| --- | --- | --- |
| `src/Strogo.Modules/G02ProofSourceRegenerator.cs` | точка входа после held identity; no implicit admission | input provenance |
| `src/Strogo.Modules/G02DafnyReplay.cs` (новый) | inventory/toolchain, bounded process, normalized transcript | fresh proof replay |
| `tests/Strogo.Modules.Admission.Conformance/Program.cs` | real и forged cases | AC1–5 |
| `docs/g02-package-structure.md`, `docs/knowledge-log.md` | границы claims, evidence и опровержения | обязательный knowledge capture |

Порядок EXEC после approval: fixed vectors → pinned inventory/runner → нормализатор и два clean replay → отрицательные probes → независимый review → production integration `check/build/admit` отдельными проверяемыми checkpoints. Каждый checkpoint — подробный commit, периодический push и запись в Unlimotion. При неоднозначности identity/whole-source mapping остановиться до изменения публичного wire.

Проверочные команды: закрепить `.tools/dotnet-sdk-10.0.400` в `PATH`; `dotnet restore Kernel.slnx --locked-mode`; `dotnet build Kernel.slnx --no-restore`; `dotnet run --project tests/Strogo.Modules.Admission.Conformance -c Release --no-restore`; затем релевантные Modules/Graph/Kernel/Notation/Portability/Experiments suites по базовой G02 SPEC. Для exact replay дополнительно запускать `dafny.exe verify candidate.dfy --enforce-determinism --cores 2 --verification-time-limit 15` **только** через проверяемый runner, а ручную команду считать исследовательской. Останавливать validation loop после полного AC evidence либо при повторяемом timeout без новой гипотезы; `git diff --check` и аудит unrelated changes обязательны.

## 9. Quality gate и review

SPEC linter expanded (1–20): 1 PASS — наблюдаемый отказ/Verified §1/4; 2 PASS — 74 stored checks и probe §0/1; 3 PASS — forged transcript §1; 4 PASS — trusted replay §2/3; 5 PASS — §2 non-goals; 6 PASS — host/producer/replay/human §2/3; 7 PASS — `check/build/admit` §2/5; 8 PASS — формулы/порядок/records §3; 9 PASS — typed refusals §3/4; 10 PASS — ограничения 60 s и 1 MiB §3; 11 PASS — E05 fields §3/5; 12 PASS — no silent migration §5; 13 PASS — rollback §5; 14 PASS — AC1–6 §6; 15 PASS — negative matrix §4/6; 16 PASS — команды и stop выше; 17 PASS — staged plan §8; 18 PASS — decision ledger §4; 19 PASS — medium expanded/security §0; 20 PASS — delivery-task/QUEST, trusted host и review §0/9. Итог: ГОТОВО к post-SPEC review, **не** к EXEC без exact approval.

SPEC rubric: цель/границы 5 (§1–2), AS-IS 5 (§1), дизайн 5 (§3), безопасность/rollback 5 (§3–5), проверяемость 5 (§6), автономность 2 (новые нормативные decisions требуют owner approval). Итого 27/30; оценка не заменяет review и human gate.

Role-Based Review Result: business/domain — owner смысл D02 и release decision не наследуются (§2/4); UX/design — не применимо, CLI/library без визуального layout, проверяется отсутствие release prompt при отказе; tester — AC1–6 покрывают positive, forgery, timeout и race; architect — единая canonical identity, aggregate evidence и lowerer obligations явно разграничены; security/delivery — trusted anchor, held handles, env/ACL, rollback и pre-prompt stop. Независимый reviewer проверяет это отдельно; его фактический sandbox может быть writable, тогда независимость технически не заявляется.

### Post-SPEC review

- Scope/Evidence pass: эта SPEC, approved G02/E05/proof-wire, `G02StoredIdentityVerifier`, `G02ProofSourceRegenerator`, E04 tool verifier, `tools/dafny.json`, 290-file inventory; Windows `verify` probe с exit 0, raw stdout `0D0A` + `4 verified, 0 errors` + `0D0A`, stderr 0; central QUEST/review/template.
- Contract pass: без новых E05 fields, owner и release gates сохранены. Adversarial pass: самосогласованный forged transcript, подменённый verifier+inventory, check→open DLL race, CRLF и изменение `N`; §3/AC5 уточнены после review. Role pass — таблица выше. Fix and re-review: reviewer повторно проверил исправления CRLF/anchor/`N`/env и отдельно финальный порядок held handles → hash → pre-start path revalidation; новых BLOCKER/HIGH/MEDIUM не нашёл в затронутом контракте. Его sandbox фактически `danger-full-access`, поэтому это advisory behavioral read-only, не технически изолированное independent review; собственный adversarial pass выше служит fallback.
- Depth checklist: scope/unrelated — только новая SPEC; AC/evidence — §6; unsupported claims — §1/2; regression/edge — §4/6; docs — §8; hidden contract — `toolchainDigest`/`evidenceDigest` и один source явно вынесены на approval; manual challenge — при отсутствии реального host-owned tool directory или воспроизводимого assembly hash нельзя объявить replay доказанным.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| BLOCKER | Windows output | LF-only parser отверг бы фактический CRLF | Exact `verify` probe и строгий LF/CRLF framing | fixed, targeted re-review |
| HIGH | Trust anchor | Mutable repo inventory не был независимым pin | Compile trusted constants, не брать ожидания из package/catalog | fixed, targeted re-review |
| HIGH | TOCTOU | Проверка path до удержания tool bytes оставляла race | Сначала held handles, потом hash и path revalidation | fixed, targeted re-review PASS |
| MEDIUM | Determinism | `N` не влияло на transcript | Включить `N` в общий evidence summary digest | fixed, targeted re-review |
| MEDIUM | Process environment | Унаследованные vars/DLL resolution не ограничены | Очистить environment и ограничить host-owned paths | fixed, targeted re-review |

- No-findings justification после fixes: по scope/evidence, contract, adversarial и role passes не осталось незакрытого BLOCKER/HIGH/MEDIUM **на SPEC-фазе**; точные pins и алгоритмы определены, AC связывают реальные/поддельные результаты, rollback и human gates отделены. Это не no-findings claim для будущего EXEC: host-owned installation, held-handle реализация, два clean replay и D02/release gates ещё отсутствуют.
- Stop decision: `PASS → ASK-HUMAN` для exact approval этой SPEC; без него code EXEC запрещён. Residual risk: correctness verifier/compiler/OS в TCB; production host-owned installation пока не создана, и AC не закрыты.

### Post-EXEC review

Не выполнен: новая нормативная SPEC ещё не утверждена. После EXEC обязательны фактический diff/status, AC1–6 evidence, независимый security review и журнал отказов.

## 10. Approval и журнал действий агента

Ожидается точная фраза «Спеку подтверждаю» **после** успешного post-SPEC review. Это подтверждение не принимает D02 смысл и не подписывает release package.

| Фаза / событие | Решение и основание | Evidence / остаток работы | Следующее действие | Фактическое решение человека | Артефакт |
| --- | --- | --- | --- | --- | --- |
| SPEC draft | Новый нормативный digest/normalizer и one-source profile нельзя выводить из прежнего approval | K-G02-011; 74 fixture checks и исследовательский Dafny probe | post-SPEC review | ожидается | этот файл |
| SPEC advisory review и fix | Исправлены CRLF, trust anchor, TOCTOU, `N` и process environment до запроса approval | reviewer findings; Windows raw stdout probe; independent inventory/evidence hashes; targeted re-review PASS при фактической writable sandbox | exact owner decision; затем EXEC | «Спеку подтверждаю» на exact revision `1281ef3` | §3/6/9 |
| EXEC entry | Файл, blob и SHA-256 совпали с опубликованной revision до изменения статуса | Git HEAD `1281ef3`, blob `a4dbc266`, SHA-256 `A0DBEAB7…72DAEB1`; D02/package approvals не даны | Реализовать AC по этапам, не объявлять admission до всех gates | подтверждено только это дополнение | §0, код и conformance |
| EXEC preflight finding | Числовой fixed vector inventory противоречит собственному требованию ordinal-сортировки; 290 entries дают `a1fcd83d…579fcba32`, а approved текст — `f992c066…` | K-G02-013; независимые Python и PowerShell расчёты | Toolchain pin остановлен; отдельное erratum SPEC и exact approval | не запрошено | §3; будущий addendum |
| EXEC partial normalizer review | Strict stdout/canonical evidence/obligation order реализованы и проходят 83 checks; runner ещё не связан с host identity/source | K-G02-013; locked restore/build без warnings/errors; advisory reviewer подтвердил BLOCKER vector и MEDIUM integration prerequisite | Commit partial checkpoint, затем erratum SPEC | не запрошено | `G02ProofTranscript`, Admission conformance |
| EXEC fixture replay | Corrected inventory pin и held tool files реализованы; два реальных запуска scalar и allocation дают byte-equal transcript, unproven fold отвергнут | K-G02-014; targeted 91; bounded diagnostics вне package; production ACL и trusted package integration ещё открыты | Timeout/process-spy probes, signed package replay binding, host ACL | текущая goal-инструкция подтверждает изменения | toolchain/replay/conformance |
| EXEC regression checkpoint | Полная регрессия прошла; fixture replay остаётся частичной реализацией | Admission 91; Modules 362; Graph 141; Kernel 29/29, 10904 assertions; Notation 143; Portability 253; E09 12/21. Targeted independent review исправлений выполнен; возможен orphan descendant после выхода parent | Process-tree containment и signed package/current-state composition; затем ACL и human gates | Новое подтверждение для этого checkpoint не требуется | K-G02-014; internal runner |
| EXEC containment design | Для обязательного process-tree cleanup выбран atomic Windows job-list startup и kill-on-close без breakaway | Microsoft CreateProcess job-list contract; прежний Kill по живому parent не покрывает exited-parent case | Implement native wrapper и orphan-child probe; никакой production admission до review | В рамках утверждённого timeout/termination AC4 | G02ContainedProcess |
| EXEC containment checkpoint review | Atomic job-list startup устраняет exited-parent orphan case; failure paths освобождают ownership; job drain перед result | K-G02-015; independent advisory review no new BLOCKER/HIGH/MEDIUM; full regression95/362/141/29/143/253/E09 12/21. Полные AC ещё не выполнены | Timeout/overflow/env probes и trusted package composition | В рамках действующего approval, admission не принято | G02ContainedProcess, Admission process-spy |
| EXEC adversarial probes | Общий internal process runner извлекается из fixture replay без изменения production limits; tests получают только меньший deadline | AC4 timeout/output и AC5 environment; существующий job startup reuse | Probe реальных helper процессов через тот же runner, review | В рамках действующего approval | G02VerifierProcess |
| EXEC adversarial process checkpoint | Shared runner проверен real hang/child, stdout/stderr overflow, exact limit и exact explicit environment; limits сохранены | K-G02-016; targeted rebuilt109; full362/141/29-10904/143/253/E09 12/21; independent advisory review no new B/H/M | Signed package/current-state composition и forged transcript/vector; затем ACL и human gates | Новое approval не требуется для утверждённых AC4/5; admission не принято | G02VerifierProcess, Admission helpers |
| EXEC package composition | Internal fixture pipeline объединяет owner/stored identity, deterministic regeneration, pinned tool identity, fresh replay и byte-equal held artifacts; никаких public admit/Load | AC3 forged metadata и AC4 owner refusal before tool opening; compiled fixture closure остаётся placeholder | Реализовать обе positive shapes и self-consistent forgery/expired/epoch negatives | Действующее approval; separate semantic/release gates не пройдены | G02PackageProofReplay |
| EXEC composition counterexample | Первый positive scalar package отвергнут PackageProofMetadataInvalid: lowerer IDs содержат uppercase addOne, E05 требует lowercase ≤64 | Raw proof/transcript сохранены в local-validation; candidate composition source сохранён отдельно. Нормализатор raw IDs не обеспечивает wire compatibility | Отдельное reviewed erratum для отображения IDs, затем возобновить candidate integration | Новое нормативное отображение пока не утверждено | K-G02-017; obligation-ID erratum |
