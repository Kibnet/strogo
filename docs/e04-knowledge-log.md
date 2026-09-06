> Исторический журнал E04, опубликованный с удалением личных путей. Ссылки и commit IDs прежней базы знаний относятся к локальной истории. [Происхождение публикации](publication.md).

# Журнал знаний E04

Обзор языка · Эксперименты

E04 начат 2026-09-06 по точному подтверждению владельца «Спеку подтверждаю». Утверждённая SPEC: commit 8a9342e8b0458c55ce1eb5096dc1b16901bec6ba, blob 2cfed958a7fa6c6717f4183cc9fb3c232489bae7 в проекте <original-checkout>.

Правило владельца: все значимые инсайты, подтверждения/опровержения гипотез, решения, контрпримеры, неудачные подходы и ограничения сохраняются здесь до завершения контрольной точки. История опровергнутых выводов сохраняется; стабильные выводы отражаются в канонических документах раздела. Сырые логи остаются evidence по ссылке.

## K-E04-001

- Дата / фаза: 2026-09-06 / SPEC.
- Тип / статус: Decision / Accepted.
- Утверждение: владелец утвердил ограниченный профиль CloneRecurringSubtree C01–C05 с цепочкой Dafny 4.11.0 → C#/.NET → ReadyToRun win-x64 и обязательным сохранением знаний.
- Scope: E04; G01–G04, AC1–AC13. Это утверждение плана, не результат выполнения.
- Evidence: сообщение владельца «Спеку подтверждаю» после commit 8a9342e; [утверждённая SPEC](<../specs/2026-09-05-bounded-task-graph-profile-v1.md>).
- Последствие: разрешён EXEC E04; E03/v0.1 остаётся неутверждённым.
- Supersedes / supersededBy: нет.

## K-E04-002

- Дата / фаза: 2026-09-06 / SPEC.
- Тип / статус: Constraint / Accepted.
- Утверждение: один фиксированный pipeline E04 проверяет ограничения, доказательство и компиляцию, но почти не оставляет агенту выбора алгоритма и не доказывает выразительность или выигрыш G05/G06.
- Scope: E04; G01, G05, G06, AC12.
- Evidence: утверждённая SPEC 8a9342e, разделы 6.2.2 и 12.
- Последствие: отдельный следующий эксперимент должен предоставить полезный выбор реализации; успех E04 не повышает статусы G05/G06.
- Supersedes / supersededBy: нет.

## K-E04-003

- Дата / фаза: 2026-09-06 / EXEC.
- Тип / статус: Source / Confirmed.
- Утверждение: официальный release v4.11.0 публикует архив Windows x64 с SHA-256 3653f05a111ca21e234709ea7b25ce96083fd6c6f10484256ba54110dbc0654d.
- Scope: воспроизводимая зависимость E04, AC5/AC9; работоспособность ещё не установлена.
- Evidence: [GitHub release API](https://api.github.com/repos/dafny-lang/dafny/releases/tags/v4.11.0), asset dafny-4.11.0-x64-windows-2022.zip; исходная ревизия проекта 8a9342e.
- Последствие: загрузку можно проверить относительно опубликованного digest, без TOFU по локально вычисленному хешу.
- Supersedes / supersededBy: нет.

## K-E04-004

- Дата / фаза: 2026-09-06 / REVIEW.
- Тип / статус: Qualification / Confirmed.
- Утверждение: постусловия только для Accepted недостаточны — always-reject и пустые criteria могут пройти слабый контракт; требуется точный total outcome и bijection всех criteria с сохранением payload.
- Scope: G04, AC3/AC4. Это дефект формулировки утверждённой SPEC, обнаруженный до proof; не опровержение Dafny.
- Evidence: adversarial review SPEC 8a9342e, строки 161–176 и 408–409; уточнения EXEC в рабочей SPEC.
- Последствие: ModelOutcome задаёт обязательный Accepted на корректном входе; always/selective reject и omit-criteria входят в настоящие proof mutations. Публичные AST отказы и harness-only proof mutations показываются раздельно.
- Supersedes / supersededBy: квалифицирует прежний post-SPEC PASS, не отменяет исходную цель.

## K-E04-005

- Дата / фаза: 2026-09-06 / REVIEW.
- Тип / статус: Procedure / Accepted.
- Утверждение: manifest не может содержать hash собственного commit; knowledge checkpoint завершается последовательностью source/evidence commit → KB commit → manifest commit.
- Scope: AC13; поля projectCommit/knowledgeBaseCommit ссылаются только на уже существующие ревизии.
- Evidence: утверждённая SPEC 8a9342e и adversarial review требований 6.2.10.
- Последствие: знания записываются до фиксации evidence; checkpoint не объявляется завершённым до проверки manifest. Самоссылочные hashes исключены.
- Supersedes / supersededBy: уточняет процедуру AC13.

## K-E04-006

- Дата / фаза: 2026-09-06 / EXEC.
- Тип / статус: Insight / Confirmed.
- Утверждение: конечное containment-замыкание можно доказать как минимальное замкнутое надмножество root, используя убывание множества ещё не посещённых ID.
- Scope: G04, инварианты 1/14; набор графов не перечисляется в доказательстве.
- Evidence: verification/task-graph-v1/Contract.dfy, Grow; Dafny 4.11.0: 18 model obligations и 1 candidate obligation verified, 0 errors. Промежуточный вывод сохранён в artifacts/e04/development-evidence.json с точной строкой session log; итоговый source checkpoint d2cec58.
- Последствие: ручной SMT encoder для графов не потребовался. Первоначальное cardinality decreases потребовало лишней помощи solver; set decreases проверяется непосредственно.
- Supersedes / supersededBy: нет.

## K-E04-007

- Дата / фаза: 2026-09-06 / REVIEW.
- Тип / статус: Qualification / Confirmed.
- Утверждение: доказательство равенства реализации ModelOutcome не заменяет отдельных доказательств обещанных свойств самой модели — в частности сохранения DAG/уникальности ID после применения patch и канонического порядка.
- Scope: G04, AC3; успешные 19 obligations сами по себе недостаточны для всех 14 инвариантов SPEC.
- Evidence: adversarial review Contract.dfy и GraphProgram.cs после первых успешных verify/translate; в ранней модели отсутствовали Apply и GraphValid theorem.
- Последствие: добавить application/sorting theorems и различать доказательства ядра, доверенный adapter и runtime evidence. E04 пока не завершён.
- Supersedes / supersededBy: квалифицирует объём evidence K-E04-006.

## K-E04-008

- Дата / фаза: 2026-09-06 / EXEC.
- Тип / статус: Procedure / Confirmed.
- Утверждение: изолированное окружение dotnet restore на этой Windows требует APPDATA/LOCALAPPDATA; отсутствие этих путей вызывает NuGet path1 null, а не дефект программы языка.
- Scope: воспроизводимость toolchain, AC5/AC8.
- Evidence: artifacts/e04/work/*/restore.json; тот же generated project восстанавливается с путями, ProcessRunner сохраняет закрытый allowlist переменных.
- Последствие: добавить эти два несекретных системных пути в allowlist. Не переносить весь environment процесса и не отключать проверку ошибок restore.
- Supersedes / supersededBy: нет.
## K-E04-009

- Дата / фаза: 2026-09-06 / EXEC.
- Тип / статус: Confirmation / Confirmed.
- Утверждение: модель E04 отдельно доказывает сохранение referential integrity, глобальной уникальности ID и ацикличности containment после применения additions.
- Scope: G04, AC3; доказательство относительно owner model, не всей TCB.
- Evidence: Contract.dfy: PatchIdsUnique, PatchAcyclic, PatchGraphValidity; Candidate.Run содержит соответствующие ensures. Полный model verify: 171 verified, 0 errors перед итоговым прогоном.
- Последствие: Разрыв из K-E04-007 закрывается application lemmas; равенство ModelOutcome остаётся обязательным, чтобы исключить vacuous reject.
- Supersedes / supersededBy: закрывает application-часть ограничения K-E04-007; TCB остаётся доверенной.

## K-E04-010

- Дата / фаза: 2026-09-06 / EXEC.
- Тип / статус: Decision / Confirmed.
- Утверждение: каноническая сортировка результата перенесена из C# adapter в Dafny; Wire доказывает порядок и сохранение элементов, включая payload критериев.
- Scope: AC3 invariant 12; ASCII ID, task/criterion по ID, edge по from/to.
- Evidence: Contract.dfy: CanonicalGraph.Lex/Sort/OrderCriteria/Wire; RuntimeAdapter вызывает Wire. Ключ edge src + ! + dst эквивалентен ordinal паре для разрешённых ASCII ID.
- Последствие: C# остаётся доверенным преобразователем datatypes/JSON; библиотечная сортировка C# больше не единственное основание свойства 12.
- Supersedes / supersededBy: нет.

## K-E04-011

- Дата / фаза: 2026-09-06 / EXEC.
- Тип / статус: Failure / Qualified.
- Утверждение: первый полный conformance завершился semantic compilation nondeterminism, потому что Contract.dfy был изменён между двумя сборками в том же прогоне.
- Scope: AC7; это некорректное сравнение разных inputs, а не доказательство недетерминизма backend.
- Evidence: artifacts/e04/development-evidence.json: исключение первого прогона с точной строкой session log (<private-session>); до ошибки прошли runtime corpus и 12 proof mutations. Итоговый conformance повторяется после стабилизации исходников.
- Последствие: Во время измерения воспроизводимости нельзя менять contract/support; результаты промежуточного смешанного прогона не используются как acceptance evidence.
- Supersedes / supersededBy: нет.

## K-E04-012

- Дата / фаза: 2026-09-06 / EXEC.
- Тип / статус: Constraint / Confirmed.
- Утверждение: watchdog и лимит захваченного вывода ограничивают operational execution, но не доказывают верхнюю границу RAM/CPU всей ОС или изоляцию coding agent с доступом к файловой системе.
- Scope: AC8/AC9; процессам verify/build/run заданы deadlines, stdout+stderr ограничены суммарно 1 MiB байт.
- Evidence: ProcessRunner.cs; bounded stdin в RuntimeAdapter; files/tool hashes и owner admission registry в GraphToolchain.cs.
- Последствие: Deadline охватывает также чтение pipe, даже если дочерний процесс унаследовал stdout. Поддержка системных переменных ограничена allowlist; OS administrator вне threat model.
- Supersedes / supersededBy: нет.

## K-E04-013

- Дата / фаза: 2026-09-06 / EXEC.
- Тип / статус: Constraint / Confirmed.
- Утверждение: в некоторых отрицательных proof-прогонах Z3/Boogie сообщает об ошибке разбора counterexample model; такой вывод нельзя выдавать за готовый контрпример, и admission не разрешается.
- Scope: P17, AC3/AC6; формат witness остаётся nullable.
- Evidence: artifacts/e04/development-evidence.json, точная запись session log; отрицательные Dafny verify в ходе разработки: Model parsing error: Invalid model: invalid element name 0.0; proof exit ненулевой. Итоговые mutations сохраняют фактический stdout/stderr.
- Последствие: Проверяется факт semantic proof failure; repair suggestions не считаются автоматически доказанным исправлением. Пустой witness честнее выдуманного контрпримера.
- Supersedes / supersededBy: нет.

## K-E04-014

- Дата / фаза: 2026-09-06 / REVIEW.
- Тип / статус: Failure / Confirmed.
- Утверждение: post-EXEC review выявил пропуск public explain, независимого контроля результата после adapter и закрытых отказов для missing/malformed artifacts; прямые library tests эти дефекты не обнаружили.
- Scope: AC6/AC8/AC11; промежуточный conformance 129 checks прошёл, общий E04 не завершён.
- Evidence: отдельный adversarial fallback e04_contract_review; GraphToolchain, CLI Program.cs, RuntimeAdapter; artifacts/e04/conformance.json и v0-regression.json (29/29, 10904 assertions).
- Последствие: добавить actual CLI contract/compile/run/explain smoke, output validator и missing/tamper/process-tree negatives. Повторить проверки изменённой TCB. Reviewer имел unrestricted filesystem и соблюдал read-only инструкцию; технической изоляции не было.
- Supersedes / supersededBy: квалифицирует промежуточный зелёный conformance, не опровергает доказанные теоремы модели.


## K-E04-015

- Дата / фаза: 2026-09-06 / EXEC.
- Тип / статус: Decision / Confirmed.
- Утверждение: доверенная оболочка должна проверять фактический выбранный artifact и результат после преобразования; зелёный direct conformance не заменяет public CLI contract/compile/run/explain.
- Scope: устраняет замечания K-E04-014; проверка результата защищает от дефектов adapter, но сама остаётся в TCB.
- Evidence: RuntimeValidation.cs (DTO shape, canonical order, IDs, fields/maps/dates/edges и DAG), GraphToolchain.ValidateAdmission/Explain, публичный CLI smoke и process-tree fixture.
- Последствие: missing/malformed artifact получает закрытый ArtifactMismatch с RecompileArtifact; outcome без receipt не доказывает admission. Общий .NET exception больше не становится произвольным public error code. Абсолютный путь Core DLL удалён из генерируемого csproj, чтобы build-input identity не зависела от CLI/library места вызова; ранний smoke остановлен до получения verdict, затем перезапущен.
- Supersedes / supersededBy: исправляет K-E04-014; финальная проверка записывается отдельно.

## K-E04-016

- Дата / фаза: 2026-09-06 / EXEC.
- Тип / статус: Procedure / Confirmed.
- Утверждение: проверка manifest должна разрешать не только KB anchors/claim hashes и commit IDs, но и каждый evidence path в указанном source commit.
- Scope: AC13; механическая целостность ссылок не подтверждает содержательную полноту знания.
- Evidence: tools/Check-KnowledgeSync.ps1; обязательное правило дополнено в AGENTS.md проекта.
- Последствие: пустой manifest, пустые/выходящие за repo evidence paths и отсутствующие в source revision файлы блокируют checkpoint. Содержательное review остаётся отдельным обязательным шагом.
- Supersedes / supersededBy: усиливает реализацию процедуры K-E04-005 без изменения замысла языка.

## K-E04-017

- Дата / фаза: 2026-09-06 / REVIEW.
- Тип / статус: Constraint / Confirmed.
- Утверждение: hash apphost dafny.exe недостаточен для pinning verifier — поведение определяется также загружаемыми DLL и solver, поэтому admission сверяет всё distribution inventory.
- Scope: AC3/AC9, TCB; собственный inventory и registry принадлежат доверенному owner.
- Evidence: tools/dafny.json, tools/dafny-files.json, GraphToolchain.VerifyTool; исходный implementation checkpoint d2cec58.
- Последствие: подмена Dafny.dll при неизменном exe не проходит проверку; доверие к owner, ОС и утверждённому inventory остаётся. Runtime libraries и compiled artifacts также связаны digest.
- Supersedes / supersededBy: уточняет предел archive/executable pinning K-E04-003.

## K-E04-018

- Дата / фаза: 2026-09-06 / EXEC.
- Тип / статус: Failure / Qualified.
- Утверждение: отсутствие SystemDrive в очищенном окружении Windows создало локальный каталог с буквальным именем %SystemDrive% для системного кэша, хотя conformance завершился успешно.
- Scope: AC8/AC9; operational environment, не семантика TaskGraph.
- Evidence: обнаруженный каталог %SystemDrive%/ProgramData/Microsoft/Windows/Caches в root проекта; перенесён без удаления в artifacts/e04/work/windows-cache-without-systemdrive; ProcessRunner добавляет SystemDrive/ProgramData/ALLUSERSPROFILE.
- Последствие: оставить закрытый allowlist, но включить необходимые системные пути; повторить build/run и проверить отсутствие повторного каталога. Автоматическая проверка заблокировала удаление кэша, безопасное перемещение внутри workspace удалось.
- Supersedes / supersededBy: дополняет K-E04-008.


## K-E04-019

- Дата / фаза: 2026-09-06 / EXEC.
- Тип / статус: Confirmation / Confirmed.
- Утверждение: E04 проходит 141 проверку с реальным ReadyToRun win-x64; повторные сборки дают одинаковые semantic digests/outcomes, но побайтовая воспроизводимость binaries не подтверждается.
- Scope: G02/G04 в ограниченном TaskGraph profile, AC1–AC12; G05/G06, другие платформы, полный AOT, package ecosystem и вся TCB не доказаны.
- Evidence: artifacts/e04/conformance.json, proof-receipt.json, replay-receipt.json, cli-outcome.json; независимый v0-regression.json: 29/29 cases, 10904 assertions. Full translate cs: 74 verified, 0 errors; отдельный model verify до этого: 171 verified, 0 errors (разные команды/группировки, число не равно числу свойств).
- Последствие: можно предложить следующий эксперимент с полезным выбором реализации агентом; эффективность разработки/исполнения требует отдельной SPEC и измерений. C01–C05, 32 generated inputs и 12 semantic proof mutations проверяют конкретные границы TCB; независимый output validator не заменяет proof.
- Supersedes / supersededBy: закрывает runtime/fault findings K-E04-014/015 и повторный environment check K-E04-018. История промежуточных 129 checks и смешанного прогона сохраняется в Git и development-evidence.json.

