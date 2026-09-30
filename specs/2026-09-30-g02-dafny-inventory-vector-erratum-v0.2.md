# G02: поправка к fixed vector inventory Dafny 4.11.0

## 0. Метаданные

- Тип: `delivery-task` / QUEST / expanded public security contract correction. Владелец решения: Kibnet. Статус: **EXEC** по текущему поручению выполнить цель и подтверждению изменений «Спеку подтверждаю». Точная исходная revision: commit `7855d526663e2c4cb056205502d3438d64c6b96e`, blob `f8b12453bafe2c2bef23312c8455ba6eedff1362`, SHA-256 `B89284B9CB9BC2434EEE49FDA98E8D105DABCE81CF6B22ABA6E3C808F0727E22`; значения сверены до изменения статуса.
- Основание: [G02 fresh replay SPEC](2026-09-30-g02-fresh-dafny-replay-v0.2.md), утверждённая по commit `1281ef3cc631f0f874e2cce4ee38ba039d78aee3`, blob `a4dbc26610e156c50c00cb1c368abb077c114232`, SHA-256 `A0DBEAB7A13E4EB610FF682BEF44D8A81AE2AAD8DBB660C022509947F72DAEB1`; K-G02-013 и частичный EXEC commit `561a80b`.
- Профиль: G02 / `delivery-task`, security and public artifact identity. Target Windows x64, pinned Dafny 4.11.0, .NET SDK 10.0.400; ветка `main`. Effective agent runtime не участвует в hash. Eval baseline — 290 entries текущей pinned distribution; это не модельный eval и не G02 proof evidence.
- Предлагаемая область approval: **только** ожидаемый digest ordinal-sorted `dafnyFiles` inventory. Остальная утверждённая replay SPEC, E05 и proof-wire неизменны. Никакого автоматического approval D02 owner bundle или release package.

## 1. Исходный сценарий, AS-IS и проблема

При подготовке production verifier agent должен независимо проверить, что все файлы установленного Dafny совпадают с доверенно закреплённым inventory, и вычислить `toolchainDigest`. Утверждённая replay SPEC §3 одновременно требует отсортировать `dafnyFiles` **ordinal по path** и pin’ит digest `f992c066b3d27e34a59d1cb18ce8c899260e9c446722d9dc533704d3f328980e`. Этот digest относится к **исходному порядку** `tools/dafny-files.json`, где `allow_on_mac.sh` стоит перед `Boogie.AbstractInterpretation.dll`; в ordinal-порядке `Boogie...` идёт первым. Обе инструкции нельзя выполнить одновременно.

Это BLOCKER до записи trusted constant: молчаливый выбор одного из двух вариантов создал бы новый публичный proof identity вне exact approval. `G02ProofTranscript` может нормализовать synthetic result, но не является runner и не закрывает AC2/admission.

## 2. Предлагаемое точное исправление

Сохранить алгоритм из уже утверждённого §3: собрать ровно 290 verified regular files, каждый объект с ровно `path` и `sha256`, отсортировать **ordinal, case-sensitive** по `path`, затем вычислить `H("strogo.proof.v0.2/dafny-files", C(dafnyFiles))`, где `H(tag, bytes)=lowercase hex SHA256(UTF8(tag + "\n") || bytes)`, а `C` — canonical UTF-8 JSON с ordinal-sort ключей объектов.

Единственная нормативная замена в §3: pinned expected inventory digest `f992c066b3d27e34a59d1cb18ce8c899260e9c446722d9dc533704d3f328980e` → **`a1fcd83db15c8806190a5df8376142fb4c288e4b1d86a8e40b9eae3579fcba32`**. Этот addendum имеет приоритет над старой константой; код и документация используют новое значение, а future package `toolchainDigest` выводится из inventory с этим pin. Raw-order digest остаётся отрицательным контрольным вектором: ошибочный host-side расчёт inventory получает типизированный отказ до `Process.Start`. Inventory digest — внутренний pin host, а не поле package; package содержит `toolchainDigest` и проверяется по базовой replay SPEC. Ни один file hash, filename, версия Dafny, archive/exe pin, argument, schema field или domain tag не меняется.

Независимые расчёты Python `json.dumps(sorted(entries,key=lambda e:e['path']),sort_keys=True,separators=(',',':'))` + `hashlib.sha256(tag+LF+bytes)` и PowerShell `[StringComparer]::Ordinal` + явный canonical JSON + .NET SHA-256 дали одинаковое `a1fcd83d…579fcba32`. Оба посчитали 290 entries; первый путь `Boogie.AbstractInterpretation.dll`, последний `z3/bin/z3-4.14.1.exe`. Hash исходного порядка остаётся `f992c066…328980e`, что воспроизводит ошибку. Отдельный case-insensitive sort даёт третий digest `b61281cb0111a05b0554e395c3839b7150731c257629eeafafeeeec56c50b83e` и тоже отказывается.

## 3. Границы, ответственность и данные

- Trusted host компилирует corrected constant и проверяет её по **удержанным** tool bytes согласно replay SPEC; `tools/dafny-files.json` остаётся проверяемым описанием, а не trust root. Package не выбирает expected digest. Production runner не запускается при конфликте.
- Исправление не вводит новую schema или миграцию. Fixture metadata с прежним неверным pin нельзя переименовать в admitted artifact; `check` создаёт новый proof identity, если всё остальное будет выполнено. Существующих G02 production admissions нет.
- Не меняем one-source profile, transcript/evidence digest, owner trust order, host-owned ACL, held-handle TOCTOU требования и human D02/release gates. `G02ProofTranscript` остаётся внутренним partial checkpoint до реального Dafny replay.
- Runtime/performance: сортировка 290 записей и один SHA-256 inventory не меняют существенный размер задачи; время реального proof не измерено и G05/G06 не заявляются. Rollback до admission — вернуть реализацию в состояние без toolchain pin/runner, но не принимать старый `f992` как допустимый alternative.

| Scenario | Trigger | Видимый результат | Evidence / AC |
| --- | --- | --- | --- |
| V1 | Host проверяет exact 290-file distribution | `a1fcd83d…` совпадает; можно продолжить к другим replay gates | два независимых вычисления / AC1 |
| V2 | Test-only host verifier вычисляет raw-order `f992…` вместо ordinal | Несовпадение со скомпилированной константой, типизированный отказ до Dafny и release prompt | negative control / AC2 |
| V3 | Один file/path изменён, добавлен или пропущен | mismatch до prover, пакет не допущен | inventory mutation / AC3 |

State: `BuiltNotAdmitted` остаётся таким при любом mismatch; successful inventory pin сам по себе не переводит в `Admitted`. Empty/duplicate/case-colliding paths и race проверяются общей replay SPEC и не ослабляются этим erratum.

| Решение | Owner | Выбор | Confidence | Риск | Нужно human до EXEC |
| --- | --- | --- | ---: | --- | --- |
| Исправить число, сохранив ordinal-сортировку | Kibnet | `a1fcd83d…` как единственный pin | 0.99 | Смена exact публичного identity относительно утверждённого текста | Да, отдельное approval |
| Не поддерживать raw-order как второй формат | Agent в рамках fail-closed G02 | отказ `f992…` | 0.99 | Старые fixture bytes нельзя переиспользовать | Нет, если correction утверждён |

## 4. AC и проверка

| AC | Проверка | Evidence | Если не выполнено |
| --- | --- | --- | --- |
| AC1 | Python и C#/PowerShell независимо дают exact `a1fcd83db15c8806190a5df8376142fb4c288e4b1d86a8e40b9eae3579fcba32` после ordinal-sort 290 entries | output, count, first/last path и test vector | блокирует production pin |
| AC2 | Test-only host-side raw-order `f992...` и case-insensitive `b612...` не совпадают с trusted constant и отказывают до `Process.Start` | process spy и typed refusal | блокирует replay integration |
| AC3 | Changed/additional/missing path/file меняет inventory или exact closure и отказывает; доверенные pinned bytes читаются через held handles | mutation/race probes из replay SPEC | блокирует admit |
| AC4 | Locked restore/build, Admission conformance, replay SPEC AC, independent post-EXEC review, knowledge log и Git checkpoint | logs, source/diff, commit | блокирует closure |

Команды: закрепить `.tools/dotnet-sdk-10.0.400` в `PATH`, `dotnet restore Kernel.slnx --locked-mode`, `dotnet build Kernel.slnx --no-restore`, `dotnet run --project tests/Strogo.Modules.Admission.Conformance -c Release --no-restore`, затем full relevant G02 suites; независимый Python/PowerShell vector и `git diff --check`. `Process.Start` вообще не должен случаться в отрицательных AC2/3. Stop: не обходить blocker второй константой; при ином наборе files или новой версии Dafny создать новый контракт, а не молча заменить pin.

## 5. План, альтернативы и возражения

После exact approval исправить константу в replay SPEC и trusted host code; добавить fixed-vector/negative tests; лишь затем продолжить pinned runner и fresh replay. Перед любым admission повторить current trust, held snapshot, executable/closure verification и human gates из базовой SPEC. Каждый законченный checkpoint — подробный Git commit и периодический push, существенное знание — в `docs/knowledge-log.md`.

Альтернатива сохранить `f992` и отказаться от сортировки: технически возможна, но противоречит уже выбранному canonical ordinal wire и делает исходный порядок tooling catalog частью публичной identity. Альтернатива принимать оба значения создаёт неоднозначность producer/replay и не отвечает цели G02. Обе отвергнуты; если владелец хочет иной выбор, остановить EXEC и переоформить контракт.

Ожидаемые возражения: «почему требуется ещё approval?» — потому что меняется exact pinned значение публичного security contract, а не только формулировка. «Почему не считать это implementation bug?» — блокирующее противоречие находилось в утверждённом SPEC до кода. «Будут ли прежние packages работать?» — production G02 admissions ещё нет; fixture metadata пересоздаётся, старый pin не считается допустимым.

Роли review: tester — AC1–4; architect — canonical order/preimage и отличие raw-order; security/delivery — trusted anchor, pre-process refusal, отсутствие миграции/admission; business/domain — D02 смысловое подтверждение отдельно; UX — CLI отказ и отсутствие release prompt, visual layout не применим.

## 6. Файлы и quality gate

| Файл | После approval | Почему |
| --- | --- | --- |
| `specs/2026-09-30-g02-fresh-dafny-replay-v0.2.md` | явная ссылка на erratum и corrected constant | сохраняет историю approval и снимает конфликт |
| `src/Strogo.Modules/G02DafnyReplay.cs` | trusted pin по corrected vector | production runner, когда остальные AC готовы |
| `tests/Strogo.Modules.Admission.Conformance/Program.cs` | positive/negative vectors | AC1–3 |
| `docs/knowledge-log.md` | K-G02-013 resolution + фактические результаты | обязательное сохранение знания |

SPEC linter expanded 1–20: 1 PASS — V1–V3; 2 PASS — два digest и raw order §1–2; 3 PASS — конфликт exact pin §1; 4 PASS — сохранить ordinal §2; 5 PASS — §3 non-goals; 6 PASS — host/agent/human §3; 7 PASS — до `Process.Start` §2/4; 8 PASS — exact formula/vector §2; 9 PASS — typed mismatch §2/4; 10 PASS — 290 entries, остальное базовая SPEC; 11 PASS — no schema change §3; 12 PASS — fixture recreation §3; 13 PASS — rollback §3; 14 PASS — AC1–4 §4; 15 PASS — negative vectors §4; 16 PASS — commands/stop §4; 17 PASS — последовательность §5; 18 PASS — ledger §3; 19 PASS — expanded public security correction §0; 20 PASS — G02/QUEST/review §0/6. Нет FAIL A/C/D. Rubric: цель 5, AS-IS 5, дизайн 5, безопасность/rollback 5, проверяемость 5, автономность 2 до owner decision = 27/30; числовая оценка не заменяет approval.

### Post-SPEC review

- Статус: `PASS → ASK-HUMAN` для exact approval этого erratum. Scope/Evidence pass: exact approved replay SPEC, текущий 290-file inventory, K-G02-013, независимые Python/PowerShell расчёты, central QUEST/template/review rules; код/другие файлы на SPEC-фазе не менялись. Contract pass: алгоритм, schema, host/package boundary сохраняются, меняется только ошибочная константа. Adversarial pass: raw order, case-insensitive sort, modified/extra file и запрет process start. Role pass — роли выше. Fix/re-review: reviewer сначала нашёл MEDIUM о невозможном package-field trigger в V2/AC2; после ограничения проверки host-side и явного разграничения package `toolchainDigest` targeted re-review дал PASS без новых BLOCKER/HIGH/MEDIUM. Reviewer был поведенчески read-only при фактической sandbox `danger-full-access`; техническая независимость не подтверждена, автор отдельно выполнил adversarial pass как fallback.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| BLOCKER | approved contract | Ordinal order несовместим с pinned `f992…` | Exact correction + owner approval | SPEC fix reviewed; owner approval pending |
| MEDIUM | acceptance | V2/AC2 сначала представляли inventory digest как package field | Проверять test-only host-side расчёт; package boundary оставить в базовой SPEC | fixed, targeted re-review PASS |

- Depth checklist: scope — только corrected vector; AC/evidence — §4; unsupported claims — §3; regression/edge — V2/V3; docs — §6; hidden API/operations — trust anchor меняется открыто; manual-review challenge — проверять именно sorted preimage, а не только совпадение двух реализаций с исходным JSON. No-findings justification для **SPEC-phase**: два независимых расчёта согласны по exact preimage и первому/последнему пути, raw/casefold negative vectors отличаются, единственная новая норма ограничена одной константой, в текущей редакции нет незакрытого BLOCKER/HIGH/MEDIUM для запроса approval. EXEC/integration AC ещё не выполнены.
- Post-EXEC review: не выполнен; code correction запрещена до approval.

## 7. Approval и журнал действий агента

Ожидается отдельная точная фраза «Спеку подтверждаю» **после** post-SPEC review этой поправки. Она не подтверждает D02 owner bundle или release package.

| Фаза / событие | Решение и основание | Evidence / остаток | Следующее действие | Фактическое решение человека | Артефакт |
| --- | --- | --- | --- | --- | --- |
| SPEC entry | Исправить единственное ошибочное значение, не менять алгоритм | K-G02-013; independent Python и PowerShell; partial normalizer `561a80b` | post-SPEC review | ожидается | этот файл |
| SPEC review и fix | Уточнён host-side V2/AC2, package field не меняется | advisory targeted re-review PASS при writable sandbox; exact vector подтверждён | exact owner decision | ожидается | §2–6 |
