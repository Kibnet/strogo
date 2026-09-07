# Журнал знаний Strogo

Основной журнал для устойчивых инсайтов по этапам проекта: подтверждения гипотез, опровержения, контрпримеры, ограничения, процедурные решения и негативные результаты.

## K-E05-001

- Дата / фаза: 2026-09-06 / SPEC.
- Тип / статус: Decision / Accepted.
- Утверждение: для E05 после каждой значимой правки вносятся заметки в `docs/knowledge-log.md`, включая подтверждения/опровержения гипотез и отрицательные результаты.
- Scope: AGENTS rule, E05-spec.
- Evidence: `AGENTS.md`, `specs/2026-09-06-composable-verified-modules-v0.2.md` (обновлённый раздел 5).
- Последствие: проекту больше не требуется дублировать журнал на `docs/e05-knowledge-log.md`; все новые знания E05 идут в этот файл.
- Supersedes / supersededBy: заменяет разрозненные ссылки на `docs/e05-knowledge-log.md` в текущем черновике SPEC.

## K-E05-002

- Дата / фаза: 2026-09-06 / SPEC.
- Тип / статус: Constraint / Qualified.
- Утверждение: проверка `approval` только по hash недостаточна для модели доверия в E05; необходима provenance-цепочка (proof+admission+build) и проверяемые поля подписи.
- Scope: E05; AC2/AC3/AC6, trust model.
- Evidence: обновлённая секция 6.4 в `specs/2026-09-06-composable-verified-modules-v0.2.md`.
- Последствие: без подписи и детерминированных digest-цепей любой tamper-путём может имитировать ранее выданный admission.
- Supersedes / supersededBy: уточняет E05-K09.

## K-E05-003

- Дата / фаза: 2026-09-06 / SPEC.
- Тип / статус: Qualification / Confirmed.
- Утверждение: fold-инварианты и assertions должны иметь явную точку приложения (locus) для replay-аудита; без locus нельзя считать coverage наблюдаемым и полным.
- Scope: G04, proof obligations, AC2/AC3.
- Evidence: обновлённая секция 6.2 в `specs/2026-09-06-composable-verified-modules-v0.2.md`.
- Последствие: доказательства без locus не считаются достаточными для exact outcome в E05.
- Supersedes / supersededBy: уточняет E05-K08.

## K-E05-004

- Дата / фаза: 2026-09-06 / SPEC.
- Тип / статус: Qualification / Confirmed.
- Утверждение: proof статусы `Timeout`/`ToolError` относятся к knowledge gap и не закрывают обязательства точного результата.
- Scope: execution gates, AC2/AC3/AC6.
- Evidence: `specs/2026-09-06-composable-verified-modules-v0.2.md`, раздел 6.2 и 6.4.
- Последствие: такие статусы требуют explicit repair/перезапуска и отдельной записи в журнале.
- Supersedes / supersededBy: уточняет E05-K04 (как методика обработки неполных proof результатов).
## K-E05-010

- Дата / фаза: 2026-09-06 / EXEC (checkpoint 1-4).
- Тип / статус: Decision-Checkpoint / Confirmed.
- Утверждение: в `strogo` добавлены рабочие артефакты checkpoint: структура `src/Strogo.Modules` с моделью AST/типов, лексером, парсером и компилятором IR, верифицированная фикстурно-конформансная проверка и фиксация в `docs/modules-v0.2.md`.
- Scope: E05; шаги 1–4 (структура кода, формальная нотация, лексер+парсер, compiler IR).
- Evidence: `src/Strogo.Modules/*.cs`, `tests/Strogo.Modules.Conformance/*`, `fixtures/modules-v0.2/*.json`, `docs/fixtures/modules-v0.2/*.json`, `docs/modules-v0.2.md`, сборка `dotnet build src/Strogo.Modules/Strogo.Modules.csproj`.
- Последствие: можно переходить к следующему checkpoint (formal semantics/validator, proof hooks) без повторной разработки формата.
- Supersedes / supersededBy: закрывает промежуточный пробел между сырой идеей и исполняемой библиотекой, без изменений в целевых гарантиях G01–G06.

## K-E05-011

- Дата / фаза: 2026-09-07 / EXEC.
- Тип / статус: Validation / Confirmed.
- Утверждение: свежий Reserve v0 прогон подтвердил bounded lost-response invariant: остановка процесса после SQL commit сохраняет stock=7 и один receipt; retry исходного `evt-child,Q=3` возвращает `AlreadyCommitted`, не выполняя второй debit.
- Scope: существующий Reserve v0 host, commit/response boundary; не Strogo Modules v0.2 и не преимущество языка.
- Evidence: `tests/Kernel.Conformance/HostCases.cs` (`process-stop-before-after-commit`), `artifacts/local-validation/e05/20260907-115426/reserve-measured.json`: 29/29 cases, 10904 assertions; public readback `7b55ecbc-3772-4e8f-81a1-00316c989339` в E05 thread.
- Последствие: host-инвариант можно использовать как общий baseline будущего сравнения frontends, но нельзя засчитывать как подтверждение G04/G05 нового языка.
- Supersedes / supersededBy: уточняет E05-K05 фактическим runtime evidence для одного crash/retry случая; concurrent refusal fixture остаётся отдельным обязательством.

## K-E05-012

- Дата / фаза: 2026-09-07 / EXEC.
- Тип / статус: Counterexample / Resolved.
- Утверждение: первый composite fixture обнаружил, что `ModuleLexer` вызывал строковый accessor для JSON Number и падал необёрнутым `InvalidOperationException` на `capacity`; числовые token bytes теперь декодируются напрямую как UTF-8.
- Scope: lexer TCB для `strogo.module.v0.2`.
- Evidence: expected-red запуск `tests/Strogo.Modules.Conformance` на `composite-valid.json`; исправление `src/Strogo.Modules/Lexer.cs`; последующий conformance PASS.
- Последствие: bounded sequences проходят lexer, а найденный failure сохранён новой числовой fixture-трассой; это исправление инструмента, не proof семантики sequences.
- Supersedes / supersededBy: нет.

## K-E05-013

- Дата / фаза: 2026-09-07 / EXEC.
- Тип / статус: Design hypothesis / Confirmed for current fragment.
- Утверждение: stable IDs позволяют убрать из identity текстовый порядок деклараций и DAG nodes; две структурно переставленные composite-программы, включая перестановку пар `fieldId→arg`, дают одинаковые canonical source и IR digests.
- Scope: records, bounded sequences и local calls текущего parser/IR; семантическая эквивалентность разных алгоритмов не заявляется.
- Evidence: `composite-valid.json`, `composite-valid-shuffled.json`, conformance checks canonical source/IR digest.
- Последствие: G01 получает проверяемое основание для typed structural edits без привязки к строкам; canonicalization обязана переставлять record field IDs и operands как пары.
- Supersedes / supersededBy: развивает K-E05-010.

## K-E05-014

- Дата / фаза: 2026-09-07 / EXEC.
- Тип / статус: Limitation / Open.
- Утверждение: реализованный composite fragment пока не содержит `if`, `fold`, nested regions, import closure, contracts и proof lowering; обязательные ClampSeries и OrderedExactAllocation из E05 ещё невыразимы целиком.
- Scope: checkpoint после records/sequences/local calls.
- Evidence: supported opcode set в `src/Strogo.Modules/Parser.cs`, `docs/modules-v0.2.md`, E05 план шагов 1–2.
- Последствие: следующий checkpoint должен добавить region semantics и proof obligations; текущий PASS нельзя называть AC1/AC2 или exact-outcome completion.
- Supersedes / supersededBy: будет закрыто будущим checkpoint E05 proof lowering.

## K-E05-015

- Дата / фаза: 2026-09-07 / EXEC.
- Тип / статус: Counterexample / Resolved.
- Утверждение: generic `CanonicalJson` кодирует JSON numbers как decimal strings; первоначальная numeric форма `capacity` поэтому давала source bytes, которые Modules parser не мог прочитать обратно.
- Scope: canonical source identity bounded sequences v0.2.
- Evidence: expected-red `composite canonical source roundtrip digest`; `capacity` переведён в единственную форму canonical natural string, добавлен `composite-invalid-numeric-capacity.json`; roundtrip PASS.
- Последствие: source codec снова замкнут `parse → canonicalize → parse`, а schema не допускает две формы одного capacity.
- Supersedes / supersededBy: уточняет формат sequences в K-E05-013.

## K-E05-016

- Дата / фаза: 2026-09-07 / EXEC.
- Тип / статус: Design correction / Confirmed.
- Утверждение: typed IR обязан сохранять type declarations, imports, exports, parameter types и `contractRef`; одного source digest и списка instruction недостаточно для самостоятельного lowering/audit.
- Scope: `ModuleIr`/`FunctionIr` v0.2.
- Evidence: compile-time expected-red conformance для отсутствующих IR fields; обновлённые `Models.cs`, `Compiler.cs`, `Codec.cs`; composite checks подтверждают сохранение signature/type/contract bindings.
- Последствие: следующий proof lowering сможет работать с IR closure без неявного обращения к исходному AST; source digest остаётся identity binding, но не заменяет данные.
- Supersedes / supersededBy: уточняет структуру IR из K-E05-010.

## K-E05-017

- Дата / фаза: 2026-09-07 / EXEC review.
- Тип / статус: Counterexample / Resolved.
- Утверждение: публично конструируемый record `ModuleIr` и возвращаемый напрямую `byte[]` позволяли создать или изменить артефакт, поля которого расходятся с canonical identity; аналогичный риск был у публичного `ModuleParseResult`.
- Scope: trusted parser/compiler boundary и identity Modules v0.2.
- Evidence: независимый checkpoint review; opaque internal constructors, defensive byte copies и повторная сверка source digest в `Models.cs`/`Compiler.cs`; conformance forgery/mutation checks.
- Последствие: validated source и compiled IR теперь нельзя подделать через штатный public API; reflection/unsafe code остаются вне модели доверия.
- Supersedes / supersededBy: уточняет K-E05-013 и K-E05-016.

## K-E05-018

- Дата / фаза: 2026-09-07 / EXEC review.
- Тип / статус: Counterexample / Resolved.
- Утверждение: одного `long.TryParse` недостаточно для canonical I64: формы `+1`, `01` и `-0` принимались и создавали разные source digests для одного значения.
- Scope: source identity и literal grammar.
- Evidence: три executable negative fixtures; regex `^(0|-?[1-9][0-9]*)$`; conformance `InvalidI64` checks.
- Последствие: у допустимого I64 снова одна текстовая форма; range по-прежнему ограничен signed 64-bit.
- Supersedes / supersededBy: нет.

## K-E05-019

- Дата / фаза: 2026-09-07 / EXEC review.
- Тип / статус: Boundary correction / Confirmed.
- Утверждение: malformed/invalid UTF-8 JSON и strict duplicate/non-ASCII errors выходили как `JsonException`, `InvalidOperationException` или `KernelException`, нарушая единый error API модулей.
- Scope: lexer/parser public boundary.
- Evidence: `ModuleLexerException : ModuleException`, normalization kernel errors in parser; malformed, invalid UTF-8, duplicate и non-ASCII conformance cases.
- Последствие: agent repair loop получает стабильные `stage/code/details`; исходное исключение сохраняется как inner exception для диагностики лексера.
- Supersedes / supersededBy: уточняет K-E05-012.

## K-E05-020

- Дата / фаза: 2026-09-07 / EXEC review.
- Тип / статус: Determinism correction / Confirmed.
- Утверждение: fail-fast validation в transport order могла выбирать разные первые ошибки у модулей с одинаковой canonical identity; type-depth также недосчитывал constructor `Seq` перед named record.
- Scope: deterministic diagnostics и admission limits.
- Evidence: unordered types/fields/imports/functions/nodes/exports и `record.make` type pairs валидируются по ordinal ID; reordered-invalid checks возвращают одинаковые code/entity/details; `Record→Seq→Record` boundary check; lower bounds и запрет повышения всех восьми `StrogoLimits` hard maxima.
- Последствие: перестановка неупорядоченных сущностей не меняет выбранную ошибку; depth/limit knobs соответствуют фактическому parser behavior.
- Supersedes / supersededBy: нет.

## K-E05-021

- Дата / фаза: 2026-09-07 / EXEC review.
- Тип / статус: Review / Confirmed PASS.
- Утверждение: первый независимый composite checkpoint review получил `NEEDS-FIX`: один blocker, четыре high, четыре medium и одно low замечание; следующие passes нашли неполное закрытие deterministic diagnostics/opcode coverage, evidence persistence и возможность повышать hard limits. Исправления внесены; локальный conformance после них проходит 80 checks.
- Scope: E05 composite parser/typed IR checkpoint, не весь E05.
- Evidence: два adversarial passes `/root/e05_composite_review` и финальный independent-reviewer pass `/root/e05_final_independent_review`; tracked source/evidence manifests; финальный reviewer подтвердил отсутствие actionable findings.
- Последствие: composite parser/typed IR checkpoint готов к локальному commit; это не закрывает полный E05.
- Supersedes / supersededBy: итоговый статус после re-review; будущий review потребуется для nested regions/proof checkpoint.

## K-E05-022

- Дата / фаза: 2026-09-07 / validation.
- Тип / статус: Tooling failure / Resolved.
- Утверждение: первый изолированный full run ошибочно исключил все каталоги с именем `bin`, включая `tools/z3-5.1.0-x64-win/bin` и вложенный Z3 Dafny; Modules PASS не зависел от этого, но Reserve и E04 не могли считаться проверенными.
- Scope: процедура локальной валидации, не semantics Modules/Reserve/E04.
- Evidence: temp run `strogo-20260907-final-1788775100`: первый Reserve 9/29 из-за `DirectoryNotFoundException`, первый E04 остановился на tool inventory; после копирования pinned tool directories Reserve 29/29 и E04 141/141.
- Последствие: при isolated validation нельзя применять глобальное исключение имени `bin` к toolchain directories; tool version/hash checks должны выполняться внутри копии до suites.
- Supersedes / supersededBy: нет.

## K-E05-023

- Дата / фаза: 2026-09-07 / validation.
- Тип / статус: Validation / Confirmed.
- Утверждение: текущий composite checkpoint собирается в Release и проходит 80 Modules checks; неизменённые профили Reserve v0 и TaskGraph E04 проходят полные regression suites.
- Scope: текущий worktree snapshot до checkpoint commit; не proof полного E05 и не CI.
- Evidence: tracked `artifacts/e05/source-manifest.json` связывает snapshot digest `5445e7b3…158d9` с исходниками; `validation-summary.json` хранит hashes ignored reports из `artifacts/local-validation/e05/20260907-final3-1788776679/`: Modules 80 checks/44 reported cases, Reserve 29/29 cases и 10904 assertions, E04 141 checks; isolated solution build 0 warnings/errors; Z3 5.1.0 и Dafny 4.11.0.
- Последствие: изменения parser/IR не дали наблюдаемой регрессии существующих профилей; следующий gate — независимый re-review и checkpoint commit.
- Supersedes / supersededBy: обновляет validation часть K-E05-021.

## K-E05-024

- Дата / фаза: 2026-09-07 / external review.
- Тип / статус: Process decision / Accepted.
- Утверждение: публичный результат Strogo считается воспроизводимым только при связи с public full commit SHA, точной командой, путём теста и сохранённым отчётом; локальный непубличный commit не используется как public evidence.
- Scope: Posting Board discussion и будущие внешние результаты E05.
- Evidence: запрос «Помощника Архитектора» в сообщении `f64919dc-1710-4414-a4eb-6637d0781468` (#8906); ответ и readback `ed38f91c-d20f-4588-82c2-8ad356692c48` (#8911) со snapshot `67912ad98db4bc68a3a11cd8460496ff4ed39f03`.
- Последствие: board-отчёты без одного из четырёх bindings маркируются как утверждение автора, а не повторяемый результат.
- Supersedes / supersededBy: уточняет evidence policy K-E05-011 и K-E05-023.

## K-E05-025

- Дата / фаза: 2026-09-07 / experiment design.
- Тип / статус: Design proposal / Accepted with qualification.
- Утверждение: oracle benchmark-задачи выводится из смысла задания и фиксируется до реализации; он полезен, если различает правильное решение и заранее названную правдоподобную ошибку. Задача, опубликованная до заморозки языка и использованная для его адаптации, становится development fixture, а не held-out evidence широты.
- Scope: будущая проверка G05/G06; не текущий parser/IR conformance.
- Evidence: сообщения «Помощника Архитектора» `b43d35c0-f9a7-4109-86ff-ead80d2684a4` (#8571) и `f64919dc-1710-4414-a4eb-6637d0781468` (#8906).
- Последствие: held-out задачи, commit, бюджеты и scoring замораживаются после завершения языка/экспериментального протокола; до этого примеры служат разработке и регрессии. Metamorphic checks должны включать сохраняющее смысл преобразование и mutation, которая меняет ожидаемый результат.
- Supersedes / supersededBy: развивает experiment A/B из сообщения #4478 и критерии G05/G06.

## K-E05-026

- Дата / фаза: 2026-09-07 / EXEC.
- Тип / статус: Design decision / Confirmed for representation.
- Утверждение: `if` представлен двумя вложенными regions с локальными параметрами, позиционно связанными с явно переданным environment; typed IR сохраняет тип каждого параметра и не поднимает branch instructions во внешний DAG.
- Scope: source AST, parser, codec и typed IR; не runtime execution или proof.
- Evidence: `if-valid.json`, `if-valid-shuffled.json`; canonical source/IR equality и проверки `RegionIr` в Modules conformance.
- Последствие: скрытый capture отклоняется как `DanglingNodeArg`, обе ветви проверяются независимо, а последующий lowering может реализовать вычисление только выбранной ветви без восстановления утраченной структуры.
- Supersedes / supersededBy: частично закрывает limitation K-E05-014 только для schema/type/IR `if`.

## K-E05-027

- Дата / фаза: 2026-09-07 / EXEC.
- Тип / статус: Constraint / Confirmed.
- Утверждение: лимиты узлов, глубины regions и локальный call graph обязаны учитывать вложенные ветви; проверка только верхнего body позволяет спрятать избыточный граф или цикл внутри `if`.
- Scope: admission guards Modules v0.2.
- Evidence: conformance cases `if-nested-node-function-limit`, `if-nested-node-module-limit`, `if-region-depth`, `if-invalid-nested-call-cycle.json`.
- Последствие: `CountNodes` и call enumeration рекурсивны; `MaxRegionDepth` имеет hard maximum 16 и может только ужесточаться вызывающей стороной.
- Supersedes / supersededBy: уточняет K-E05-020 для regions.

## K-E05-028

- Дата / фаза: 2026-09-07 / EXEC.
- Тип / статус: Limitation / Open.
- Утверждение: наличие отдельных `thenRegion`/`elseRegion` в IR сохраняет возможность ленивого lowering, но само по себе не доказывает, что runtime вычисляет только выбранную ветвь, что I64 операции не переполняются или что результат совпадает с owner model.
- Scope: текущий `if` representation checkpoint.
- Evidence: отсутствуют Modules runtime adapter, generated Dafny candidate и proof receipts; docs/modules-v0.2.md ограничивает заявляемый результат.
- Последствие: следующий proof/runtime checkpoint должен дать executable counterexample-sensitive evidence, например не вычислять ошибочную невыбранную ветвь и отклонять недоказанный overflow на достижимом пути.
- Supersedes / supersededBy: уточняет открытую часть K-E05-014.

## K-E05-029

- Дата / фаза: 2026-09-07 / validation.
- Тип / статус: Validation / Confirmed.
- Утверждение: nested `if` checkpoint собирается без предупреждений и проходит расширенный Modules conformance без регрессии Reserve v0 и TaskGraph E04.
- Scope: source snapshot `2720b2de…826f` поверх base commit `c6f2c205ab25842bcb1b296beff63d70160645b8`; не полный E05.
- Evidence: `artifacts/e05/source-manifest.json`, `artifacts/e05/validation-summary.json`; build 0 warnings/errors, Modules 104 checks/54 reported cases, Reserve 29/29 и 10904 assertions, E04 141 checks.
- Последствие: checkpoint готов к независимому adversarial review; результат не повышает статус K-E05-028 и не считается proof/runtime evidence.
- Supersedes / supersededBy: обновляет validation baseline K-E05-023 для следующего source snapshot.

## K-E05-030

- Дата / фаза: 2026-09-07 / independent review.
- Тип / статус: Evidence defect / Resolved.
- Утверждение: первая генерация nested-`if` manifest использовала PowerShell `Sort-Object`, зависящий от текущей culture, хотя алгоритм объявлял ordinal path sorting; individual file hashes были верны, но aggregate digest нельзя было воспроизвести по контракту.
- Scope: `artifacts/e05/source-manifest.json` и `validation-summary.json`; исходный код языка не затронут.
- Evidence: независимый пересчёт дал `7f37cf75…2459` вместо записанного `b0d11af5…0e41`; генерация исправлена через `[Array]::Sort(..., [StringComparer]::Ordinal)`. После следующего source-fix digest nested-`if` snapshot закономерно изменился на `025cc320…28eb` и воспроизводился тем же алгоритмом.
- Последствие: aggregate evidence digest снова соответствует объявленному алгоритму; будущая генерация manifests не должна использовать culture-sensitive сортировку.
- Supersedes / supersededBy: исправляет первоначальную evidence-часть K-E05-029.

## K-E05-031

- Дата / фаза: 2026-09-07 / independent review.
- Тип / статус: Diagnostic counterexample / Resolved.
- Утверждение: локальный node ID уникален только внутри одной region, поэтому ошибка с одним `entityId=node.Id` не различала одноимённые узлы в `thenRegion` и `elseRegion` и не задавала однозначную цель ремонта.
- Scope: parser diagnostics nested regions.
- Evidence: independent-reviewer finding; executable counterexample создаёт `node.sum` в обеих ветвях и отдельно ломает каждую из них.
- Последствие: node и region errors возвращают qualified locus от `function/<id>/body`; контрпример подтверждает разные targets `.../then/node/sum` и `.../else/node/sum`.
- Supersedes / supersededBy: уточняет structured repair requirement E05 и K-E05-026.

## K-E05-032

- Дата / фаза: 2026-09-07 / independent review.
- Тип / статус: Diagnostic counterexample / Resolved.
- Утверждение: qualified locus неоднозначен, если структурные границы кодируются точкой, разрешённой внутри ID: node `chosen.thenRegion` совпадал с прежним locus ветви `thenRegion` узла `chosen`.
- Scope: parser diagnostics nested regions и их машинно адресуемые repair targets.
- Evidence: независимый finding после первой квалификации locus; conformance создаёт обе допустимые структуры и проверяет разные адреса `function/choosePlusOne/body/node/chosen.thenRegion` и `function/choosePlusOne/body/node/chosen/then`.
- Последствие: структурные сегменты diagnostic locus разделяются `/`, запрещённым грамматикой ID; node ID сохраняется без двусмысленного escaping, а ветви обозначаются отдельными сегментами `then`/`else`.
- Supersedes / supersededBy: усиливает K-E05-031.

## K-E05-033

- Дата / фаза: 2026-09-07 / independent review.
- Тип / статус: Evidence defect / Resolved.
- Утверждение: один `runId` не должен неявно объединять свежий Modules report и Reserve report из предыдущего каталога, даже если shared runtime code после регрессии не менялся.
- Scope: evidence provenance текущего E05 checkpoint.
- Evidence: независимый review обнаружил отсутствие `v0-regression.json` в каталоге `20260907-if-locus-final`; Reserve suite повторно выполнен в этом каталоге и дал 29/29 cases, 10904 assertions, SHA-256 `85606da9…5bc4`.
- Последствие: tracked summary теперь ссылается на два реально присутствующих отчёта одного run; будущий summary либо хранит каждый report в указанном run, либо явно указывает отдельный source path/runId для повторно используемого evidence.
- Supersedes / supersededBy: уточняет evidence policy K-E05-024 и K-E05-030.

## K-E05-034

- Дата / фаза: 2026-09-07 / independent review.
- Тип / статус: Validation / Confirmed.
- Утверждение: nested `if` representation checkpoint после исправления diagnostic loci и evidence provenance не имеет оставшихся HIGH/MEDIUM findings независимого adversarial review.
- Scope: snapshot `2720b2de…826f`, tracked E05 manifests/summary и два локальных отчёта run `20260907-if-locus-final`; не runtime/proof часть E05.
- Evidence: финальный read-only re-review сверил slash-delimited counterexample, оба report hashes/sizes, Reserve 29/29 и 10904 assertions, manifest SHA; итог `PASS`.
- Последствие: checkpoint можно фиксировать коммитом; K-E05-028 остаётся открытой границей следующего эксперимента.
- Supersedes / supersededBy: завершает review готовности K-E05-029.

## K-E05-035

- Дата / фаза: 2026-09-07 / EXEC.
- Тип / статус: Hypothesis / Confirmed for reference semantics.
- Утверждение: вложенная `RegionIr` достаточна, чтобы однозначно исполнить `if` лениво: evaluator вычисляет condition и environment, затем только одну region с позиционным binding локальных параметров.
- Scope: `ModulesReferenceEvaluator` для `I64`/`Bool`, scalar opcodes и local calls; не generated Dafny/C# runtime.
- Evidence: `if-lazy-overflow.json`: при `condition=false` и `value=I64.MAX` возвращается MAX за один step, а при `condition=true` выбранный `i64.add` даёт `ArithmeticOverflow` на `function/safeSelect/body/node/chosen/then/node/overflow`; обычные then/else дают разные ожидаемые результаты.
- Последствие: future lowering получает исполняемый oracle, чувствительный к eager-branch mutation; сравнение с ним не заменяет owner model/proof.
- Supersedes / supersededBy: частично закрывает K-E05-028 только для reference evaluator.

## K-E05-036

- Дата / фаза: 2026-09-07 / EXEC.
- Тип / статус: Limitation / Open.
- Утверждение: reference evaluator поверх уже созданного typed IR разделяет execution implementation и будущий backend, но разделяет с ним parser/compiler и поэтому не является независимым oracle всей цепочки source→IR.
- Scope: доверенная база следующего differential lowering checkpoint.
- Evidence: API `ModulesReferenceEvaluator.Invoke(ModuleIr, ...)`; records/sequences/fold/contracts/proof/canonical JSON ABI и generated assembly отсутствуют и fail closed как unsupported; непустой import closure отдельно отклоняется до исполнения.
- Последствие: owner model evaluator должен работать от отдельного owner artifact, а Dafny/C# candidate сравниваться и с owner model, и с reference evaluator на boundary/mutation fixtures.
- Supersedes / supersededBy: уточняет TCB risk из E05 SPEC и K-E05-035.

## K-E05-037

- Дата / фаза: 2026-09-07 / validation.
- Тип / статус: Validation / Confirmed.
- Утверждение: scalar reference semantics checkpoint собирается без предупреждений, проходит 137 Modules checks и не изменяет результаты Reserve v0 и TaskGraph E04.
- Scope: source snapshot `3021df6b…e83c` поверх base commit `95a0a1520c9b6f61bd69b7308702f9c9561c9518`; не generated runtime/proof часть E05.
- Evidence: `artifacts/e05/source-manifest.json`, `artifacts/e05/validation-summary.json`; Modules 137 checks/56 reported cases, Reserve 29/29 и 10904 assertions, E04 141 checks, build 0 warnings/errors.
- Последствие: checkpoint готов к independent adversarial review; K-E05-036 и отсутствие proof/library остаются открытыми ограничениями.
- Supersedes / supersededBy: новый validation baseline после K-E05-029.

## K-E05-038

- Дата / фаза: 2026-09-07 / independent review.
- Тип / статус: Test oracle defect / Resolved.
- Утверждение: проверка только итогового `Steps == 6` не отличает единый бюджет call tree от ошибочной реализации, которая обнуляет лимит для каждого callee.
- Scope: reference evaluator resource boundary для local calls.
- Evidence: independent-review finding; `twice(addOne(addOne(2)))` с `MaxSteps=5` теперь обязан дать `EvaluationStepLimitExceeded` на втором `function/addOne/body/node/sum`.
- Последствие: conformance различает общий счётчик и общий enforcement budget; будущие resource-oracles должны включать failing boundary, а не только успешный total.
- Supersedes / supersededBy: усиливает K-E05-035 и validation K-E05-037.

## K-E05-039

- Дата / фаза: 2026-09-07 / independent review.
- Тип / статус: Test oracle defect / Resolved.
- Утверждение: один составной scalar-result `true` не различал несколько правдоподобных ошибок evaluator: identity вместо `not`, всегда истинные comparison и неполные таблицы `and`/`or`.
- Scope: reference semantics для scalar opcodes.
- Evidence: independent-review finding; `scalar-reference-valid.json` теперь наблюдает порядок и знак `sub`, true/false outcomes `le`/`eq`/`not`, четыре входа `and` и четыре входа `or`; отдельная boundary проверка отклоняет `MaxSteps > StepsHardMaximum`.
- Последствие: scalar oracle стал mutation-sensitive для перечисленных простых ошибок; будущие opcodes требуют таких же различающих outcomes, а не только одного интеграционного happy path.
- Supersedes / supersededBy: усиливает K-E05-025 и K-E05-035.

## K-E05-040

- Дата / фаза: 2026-09-07 / independent review.
- Тип / статус: Validation / Confirmed.
- Утверждение: scalar reference semantics checkpoint после усиления resource/scalar oracles не имеет оставшихся BLOCKER/HIGH/MEDIUM findings.
- Scope: source snapshot `3021df6b…e83c`, manifests/summary и local run `20260907-reference-evaluator`; не generated runtime/proof.
- Evidence: финальный read-only reviewer пересчитал 38 file hashes, ordinal snapshot, manifest SHA и оба report hashes/sizes; проверил lazy `if`, checked I64, local calls/shared budget, export/import/unsupported fail-closed boundaries и documentation claims; итог `PASS`.
- Последствие: checkpoint готов к локальному commit; следующий эксперимент должен сравнить reference outcome с generated Dafny/C# candidate и не переносить этот PASS на proof/library goals.
- Supersedes / supersededBy: завершает review K-E05-037, K-E05-038 и K-E05-039.

## K-E05-041

- Дата / фаза: 2026-09-07 / EXEC.
- Тип / статус: Hypothesis / Confirmed for scalar lowering.
- Утверждение: typed `RegionIr` достаточно, чтобы детерминированно сгенерировать nested conditional control flow в Dafny/C# и получить те же выбранные результаты, что reference evaluator.
- Scope: `I64`/`Bool`, scalar operations, local calls и nested `if`; один `win-x64` ReadyToRun selector, не owner exact-outcome proof.
- Evidence: `if-nested-safe.json` дважды даёт одинаковые source bytes/digest; Dafny 4.11.0 сообщает `2 verified, 0 errors`; generated `Candidate.__default.F000(bool,long,long):long` возвращает `MAX,3,11`, как reference evaluator; PE native header size положителен. Ленивость отдельно различает reference evaluator; текущий generated consumer проверяет выбранные outcomes и форму control flow, а не eager mutant.
- Последствие: следующий proof слой может строиться поверх этого lowering, но обязан отдельно связать candidate с owner model и package identity.
- Supersedes / supersededBy: частично закрывает runtime часть K-E05-028 и продолжает K-E05-035.

## K-E05-042

- Дата / фаза: 2026-09-07 / EXEC.
- Тип / статус: Design boundary / Confirmed.
- Утверждение: native Dafny newtype с диапазоном `Int64` превращает потенциальное переполнение в обязательство доказательства; `addOne(x: I64)` не является total на полном домене и не должен получать скрытый `requires` от lowering.
- Scope: `math-add-valid.json`, пока без owner bundle/contract AST.
- Evidence: candidate, созданный тем же `ModulesDafnyLowerer`, отклонён Dafny с `result of operation might violate newtype constraint for 'I64'`; validation классифицирует результат как `Unproven`, а не выдуманный counterexample.
- Последствие: owner должен явно определить допустимый domain; proof lowering переносит утверждённый `requires`, а отсутствие достаточного условия закрывает admission.
- Supersedes / supersededBy: конкретизирует E05 SPEC sections 6.1–6.3 и границу K-E05-036.

## K-E05-043

- Дата / фаза: 2026-09-07 / validation.
- Тип / статус: Integration defect / Resolved.
- Утверждение: generated Dafny runtime C# не проходит строгие nullable/warnings настройки исходного репозитория без отдельной compiler boundary, а default compile items могут случайно включить один generated source в два проекта.
- Scope: временная generated assembly и отдельный consumer сквозного smoke.
- Evidence: первый build получил generated-code warnings/errors и повторную компиляцию `Generated.cs`; итоговый harness задаёт generated project с `Nullable=disable`, `TreatWarningsAsErrors=false`, `EnableDefaultCompileItems=false`, а consumer явно включает только `Program.cs`.
- Последствие: будущий package builder обязан изолировать generated sources и фиксировать build configuration; ослабление diagnostics не распространяется на Strogo compiler или consumer.
- Supersedes / supersededBy: новое operational знание для AC4.

## K-E05-044

- Дата / фаза: 2026-09-07 / EXEC.
- Тип / статус: Security/identity hypothesis / Confirmed for emitted syntax.
- Утверждение: стабильные пользовательские ID можно не вставлять в backend syntax: ordinal function table и counters дают symbols `Fnnn`/`pnnn`/`vnnn`, а отдельная source map сохраняет repair identity.
- Scope: текущий scalar lowering; map содержит generated declaration line, но ещё не входит в подписанный package/proof receipt.
- Evidence: fixtures используют ID с точками и дефисами; generated source не содержит эти ID, Dafny принимает source, а conformance проверяет точные declaration lines для function parameter/conditional node и mapping local call result.
- Последствие: расширения lowering должны продолжать closed-template emission и source-map binding; package admission позже связывает map digest с source/proof.
- Supersedes / supersededBy: реализует часть E05 syntax-injection и source-map требований.

## K-E05-045

- Дата / фаза: 2026-09-07 / validation.
- Тип / статус: Validation / Confirmed for final run.
- Утверждение: воспроизводимый script объединяет Modules conformance, три положительных Dafny run, обязательный отрицательный range case и фактические generated consumers в один run directory.
- Scope: `tools/Test-Modules-Dafny-Lowering.ps1` и gitignored evidence; не полный E05 admission/package workflow.
- Evidence: final run: 158 checks/60 cases; nested selector `2 verified` и `MAX,3,11`; safe call `3 verified` и `callOutcome=7`; remaining scalar operators `2 verified` и `True,False,True`; unsafe arithmetic exit 4 с фактической range diagnostic; R2R native header 196.
- Последствие: tracked manifest/summary должны ссылаться на hashes этого final run; independent reviewer пересчитывает их перед commit.
- Supersedes / supersededBy: заменяет исходный 153-check smoke текущим финальным run.

## K-E05-046

- Дата / фаза: 2026-09-07 / validation.
- Тип / статус: Tooling baseline / Confirmed.
- Утверждение: solution-wide `dotnet format --verify-no-changes` сейчас не является usable green gate: он обнаруживает тысячи исторических whitespace diagnostics в старых Reserve/E04 файлах, не относящихся к scalar lowering.
- Scope: форматирование checkout; formatter запущен только в verify mode и файлов не менял.
- Evidence: полный verify завершился exit 1 с 3307 строками diagnostics; узкий `dotnet format whitespace src/Strogo.Modules/Strogo.Modules.csproj --verify-no-changes --no-restore --include src/Strogo.Modules/DafnyLowering.cs` завершился exit 0.
- Последствие: новый production lowering проверен formatter; очистка всего исторического baseline требует отдельного механического изменения и не входит в E05 checkpoint.
- Supersedes / supersededBy: новое ограничение validation workflow.

## K-E05-047

- Дата / фаза: 2026-09-07 / independent review.
- Тип / статус: Adversarial findings / Resolved.
- Утверждение: ранняя версия checkpoint имела четыре доказательных пробела: могла молча удалить unused record declaration, не исполняла nested `if`/local call/часть scalar operators, не mapped function parameters и проверяла Dafny только по version string.
- Scope: scalar Dafny lowering, source map и reproducibility harness.
- Evidence: reviewer findings; `UnsupportedLoweringTypes` с unused-record counterexample; path-sensitive `if-nested-safe.json`; `call-safe.json` и `scalar-lowering-safe.json` generated consumers; parameter `pNNN` exact-line mapping; `Install-Dafny.ps1 -VerifyOnly` с archive/executable SHA.
- Последствие: backend claims разрешены только для реально verified/executed opcodes/control flow; новые type declarations обязаны fail closed; tool provenance входит в evidence.
- Supersedes / supersededBy: усиливает K-E05-041, K-E05-044 и K-E05-045.

## K-E05-048

- Дата / фаза: 2026-09-07 / independent review.
- Тип / статус: Runtime trust boundary / Confirmed.
- Утверждение: generated Dafny C# использует обычную unchecked `long` arithmetic; безопасность range обеспечивается предшествующим proof и binding неизменного assembly, а не повторной runtime overflow trap.
- Scope: generated `x+1` во вложенном safe selector; package binding в этом checkpoint ещё отсутствует.
- Evidence: generated C# содержит unchecked addition; MAX-вход выбирает другую ветвь и возвращает корректный outcome, но один только consumer не является eager-mutant oracle.
- Последствие: документация не приписывает runtime smoke различение eager mutation. Будущий admission обязан связывать verified source/generated assembly hashes; mutation test proof layer нужен отдельно.
- Supersedes / supersededBy: уточняет TCB K-E05-041 и открытую package boundary K-E05-036.

## K-E05-049

- Дата / фаза: 2026-09-07 / final validation.
- Тип / статус: Validation / Confirmed.
- Утверждение: после исправления всех semantic/evidence findings main solution и все три conformance suites проходят, а scalar lowering harness воспроизводит proof/runtime evidence закреплённым toolchain.
- Scope: source snapshot `066b0f3d…75979c0` поверх `463a8e6a24bf57d24a6bba01247e1fa9d5702f31`; это scalar lowering checkpoint, не полный E05.
- Evidence: build 0 warnings/errors; Modules 158/60; selector 2/0 + R2R outcomes MAX,3,11; call 3/0 + outcome 7; scalar 2/0 + True,False,True; unsafe arithmetic Unproven; Reserve 29/29 и 10904; E04 141.
- Последствие: независимый read-only reviewer пересчитал 45 file hashes, ordinal snapshot, manifest/summary и три ignored report bindings; BLOCKER/HIGH/MEDIUM findings не осталось, checkpoint готов к локальному commit.
- Supersedes / supersededBy: закрывает validation часть K-E05-045 после финального manifest review.

## K-E05-050

- Дата / фаза: 2026-09-07 / implementation.
- Тип / статус: Contract representation / Confirmed for scalar subset.
- Утверждение: owner contract можно представить отдельным strict canonical artifact, не смешивая его с agent module; точная связь задаётся через function/contract/parameter IDs, сигнатуру и единственную форму `result == model(parameters)`.
- Scope: `strogo.owner-bundle.v0.2`, только `I64`/`Bool`, чистые expressions, пустые effects и экспортированные функции без helpers/imports/composites.
- Evidence: `OwnerBundleParser`, `OwnerBundleCodec`, `OwnerContractBinder`; conformance проверяет canonical roundtrip, defensive copy, non-forgeable bundle, signature/contractRef/function mismatch, source-map identities и domain-separated digest `strogo.owner-bundle.v0.2/bundle`.
- Последствие: owner смысл получает собственный digest и не может быть ослаблен полем agent module; human approval и подпись этого digest остаются отдельным следующим слоем.
- Supersedes / supersededBy: реализует скалярную часть E05 owner representation, но не admission chain E05-K09.

## K-E05-051

- Дата / фаза: 2026-09-07 / implementation and validation.
- Тип / статус: Qualification / Confirmed.
- Утверждение: обязательный concrete witness, удовлетворяющий `requires`, доказывает непустоту формального domain и проверяет определённость модели на этом входе, но не доказывает полноту/правильность domain относительно человеческой спецификации или тотальность модели на всех допустимых входах.
- Scope: scalar owner evaluator; witness arguments обязаны точно покрывать parameter IDs и типы.
- Evidence: valid witness `x=0` даёт model result `1`; `x=I64.MAX` отклоняется как `RequiresWitnessRejected`; расширенный domain с тем же witness у небезопасной модели проходит локальный witness, но затем отклоняется Dafny по общим range obligations.
- Последствие: witness остаётся ранней независимой concrete-проверкой, а proof отвечает за все входы под `requires`; соответствие человеческому заданию всё равно подтверждает человек.
- Supersedes / supersededBy: конкретизирует риск пустого requires из утверждённой E05 SPEC.

## K-E05-052

- Дата / фаза: 2026-09-07 / proof validation.
- Тип / статус: Hypothesis / Confirmed for one scalar contract.
- Утверждение: exact owner model не фиксирует единственный алгоритм кандидата: один bundle допускает структурно разные `x + 1` и `x - (-1)`, но отклоняет `return x`.
- Scope: одна I64-функция `addOne`, domain `x <= I64.MAX-1`; это не доказательство общей выразительности языка или AC1 целиком.
- Evidence: pinned Dafny 4.11.0: обе корректные реализации `4 verified, 0 errors`; отдельные generated C# consumers возвращают `MIN+1,42,MAX`; неправильная реализация завершается exit 4 с `a postcondition could not be proved`.
- Последствие: следующий benchmark может сравнивать альтернативные agent implementations при неизменном owner artifact; для AC1 ещё требуется второе семейство и helpers/composites.
- Supersedes / supersededBy: впервые частично снимает ограничение fixed candidate из E05-K01.

## K-E05-053

- Дата / фаза: 2026-09-07 / proof lowering.
- Тип / статус: Insight / Confirmed.
- Утверждение: компиляция model AST в одно математическое Dafny-выражение недостаточна для семантики Strogo I64, потому что итог может быть в диапазоне при промежуточном выходе; каждый `i64.add/sub` модели должен материализоваться как типизированное `I64` let-значение.
- Scope: scalar owner model lowering; сложные math/sequence expressions ещё не реализованы.
- Evidence: `EmitOwnerExpression` создаёт generated `eNNN: I64`; safe owner model проходит, weak domain сообщает range diagnostic точно на generated model let и отдельно в candidate node.
- Последствие: будущий lowering составных models обязан сохранять proof obligation каждого типизированного промежуточного значения, а не только конечного return type.
- Supersedes / supersededBy: усиливает TCB-границу K-E05-042.

## K-E05-054

- Дата / фаза: 2026-09-07 / negative proof validation.
- Тип / статус: Confirmation / Confirmed.
- Утверждение: достаточность owner `requires` является проверяемым обязательством, а не доверенной аннотацией: замена `x <= I64.MAX-1` на `true` делает и owner model, и candidate недоказанными.
- Scope: `owner-add-one-weak.json` и `math-add-valid.json`.
- Evidence: pinned Dafny exit 4, две фактические diagnostics `result of operation might violate newtype constraint for 'I64'`, итог `2 verified, 2 errors`.
- Последствие: agent не может получить допуск, просто сославшись на модель с неполным domain; owner также обязан сформулировать модель, тотальную на утверждённой области.
- Supersedes / supersededBy: подтверждает ожидаемый механизм из K-E05-042 на связанном owner artifact.

## K-E05-055

- Дата / фаза: 2026-09-07 / runtime boundary.
- Тип / статус: Constraint / Confirmed.
- Утверждение: proof-carrying generated method ещё не является безопасной публичной библиотекой: Dafny-generated C# не проверяет `requires` при прямом вызове, а текущий harness исполняет только допустимые входы.
- Scope: generated owner-contract consumers; package manifest, approved digest binding и runtime facade отсутствуют.
- Evidence: consumers вызывают `Candidate.__default.F000` напрямую на `I64.MIN`, `41`, `I64.MAX-1`; документация и report не заявляют runtime rejection вне domain.
- Последствие: следующий runtime API обязан валидировать approved precondition до вызова и связывать exact verified source/binary с owner approval; до этого AC4/AC6 не закрыты.
- Supersedes / supersededBy: уточняет K-E05-048 применительно к owner proof.

## K-E05-056

- Дата / фаза: 2026-09-07 / language boundary.
- Тип / статус: Constraint / Accepted for scalar checkpoint.
- Утверждение: arithmetic внутри `requires` и любого Boolean model expression пока отклоняется, чтобы не получить расхождение definedness между strict owner evaluator и Dafny short-circuit/well-formedness rules; границы задаются сравнениями parameters/constants.
- Scope: текущий scalar predicate AST; I64 model body поддерживает checked `i64.add/sub` с typed intermediate obligations.
- Evidence: `OwnerContractSemantics.EnsureNoExecutableArithmetic` и `EnsureBooleanExpressionsUseTotalScalarOperands`; executable counterexample `true || eq(i64.add(x,1),0)` отклоняется как `ArithmeticInBooleanContractNotSupported`; документация перечисляет это как явное ограничение, а не постоянный non-goal языка.
- Последствие: расширение predicate math требует отдельного ghost math layer и явных obligations определённости из E05 SPEC, а не снятия проверки.
- Supersedes / supersededBy: временно сужает заявленный predicate subset до реализации ghost math.

## K-E05-057

- Дата / фаза: 2026-09-07 / independent review and hardening.
- Тип / статус: Adversarial findings / Resolved.
- Утверждение: детерминированный отказ требует отдельной канонизации невалидного transport, а resource limit требует атомарного process containment; канонизатор валидного артефакта и `Process.Kill(tree)` после обычного старта этих свойств не обеспечивают.
- Scope: scalar owner parser и Windows validation harness; это границы toolchain проверки, а не runtime будущих пакетов.
- Evidence: reviewer дал контрпримеры `id: 1` против `id: "1"`, переставленных unknown witness arguments и descendant с унаследованным pipe после выхода root. Parser теперь использует type-preserving structural key, preflight duplicate IDs и ordinal arguments; regressions сравнивают `code/entityId/details`. Runner создаёт root через `CreateProcessW(CREATE_SUSPENDED)`, назначает Windows Job Object до `ResumeThread`, отменяет bounded stream drains и self-test подтверждает `ProcessTimeout`, `ProcessOutputLimitExceeded` и `childAliveAfterGrace=false`.
- Последствие: invalid-input ordering нельзя строить через lossy canonical artifact codec; все будущие внешние tool invocations должны сохранять атомарную containment/deadline/output-cap схему либо явно иметь более строгий runner.
- Supersedes / supersededBy: уточняет deterministic diagnostics E05 и заменяет прежний прямой запуск tools в K-E05-045/K-E05-047.

## K-E05-058

- Дата / фаза: 2026-09-07 / final validation.
- Тип / статус: Validation / Confirmed for scalar owner checkpoint.
- Утверждение: strict scalar owner artifact, exact-outcome lowering и bounded verification harness проходят единый свежий validation run вместе с Reserve v0 и E04 regressions.
- Scope: snapshot `f9440185…c226fcf9` поверх `ef613d96199de1b81918741f15b5616fb9ac274d`; helper/composite contracts, human approval/admission, package/facade и полный E05 остаются открыты.
- Evidence: run `20260907-owner-contract-final-v4`; Release build 0 warnings/errors; scoped format PASS; Modules 199 checks/61 cases; owner и alternative по 4 verified/0 errors и outcomes `MIN+1,42,MAX`; wrong exact outcome и weak domain ожидаемо Unproven; process self-tests подтверждают timeout/output cap/orphan kill; Reserve 29/29 и 10904 assertions; E04 141. Manifest SHA-256 `bcbc9a9e…5afc5426`, summary SHA-256 `33457102…191a78d7`; независимый review: PASS, 0 оставшихся BLOCKER/HIGH/MEDIUM.
- Последствие: scalar owner checkpoint готов к локальному commit; следующий архитектурный шаг должен связать owner approval, proof и неизменный build/package либо расширить contracts на helpers перед package facade.
- Supersedes / supersededBy: завершает evidence часть K-E05-050–K-E05-057 для этого checkpoint.

## K-E05-059

- Дата / фаза: 2026-09-07 / public design discussion review.
- Тип / статус: Experiment design / Confirmed and adopted.
- Утверждение: тест полезен, когда заранее выведенный из смысла задачи oracle отличает корректную реализацию от правдоподобной ошибки; широту языка можно проверять только задачами, выбранными после заморозки schema/opcode hash. Уже опубликованная Noita scanner task является development fixture, а не held-out benchmark.
- Scope: Posting Board thread #3298, сообщения #8571 и #8906 от «Помощник Архитектора»; это вход для дизайна эксперимента, не owner contract и не evidence корректности Strogo.
- Evidence: oracle #8571 вручную подтверждён для `k=3`: signature `010` возникает в `(M1,0)`, `(M1,1)`, `(M2,0)`; mutation `M2[2]=6` удаляет `(M2,0)`, а `k=4` не даёт повторной группы. При этом fixture не различает ошибку маркировки exact-value duplicates и не проверяет deterministic order нескольких групп; эти случаи нужны до использования как regression.
- Последствие: Noita oracle можно расширять как раскрытый development corpus. Для AC8 сначала фиксируются операции, формат, ordering и budget, затем отдельный reviewer выбирает новую задачу; неуспех остаётся результатом, а изменение языка переводит задачу в development.
- Supersedes / supersededBy: конкретизирует AC8 и публичные выводы E05-K06/E05-K08 из утверждённой SPEC.

## K-E05-060

- Дата / фаза: 2026-09-07 / implementation and validation.
- Тип / статус: Reference semantics / Confirmed for records and bounded sequences.
- Утверждение: существующий typed IR для `record.make/get` и `seq.empty/length/get/append` можно исполнить без предметных операций; внешний composite input допускается только после рекурсивного совпадения record fields, element type и capacity.
- Scope: локальные modules без imports и без `fold`; это reference oracle, не generated library и не proof composite owner model.
- Evidence: fixture `composite-runtime-valid.json`; Modules conformance 211 checks после Release build. Положительные случаи различают order/get/length/nested record и независимость от comparer внешнего словаря; отрицательные дают stable `SequenceCapacityExceeded`, `SequenceIndexOutOfRange` и вложенный `RuntimeTypeMismatch`. Существующий `composite-valid.json` теперь фактически исполняет цепочку local calls → sequences → record → field и возвращает `4` за 13 steps.
- Последствие: следующий lowering может сравниваться с эталонной composite semantics; `fold` остаётся отдельной новой region semantics и не должен маскироваться host helper.
- Supersedes / supersededBy: расширяет runtime часть E05-K02 и снимает ограничение records/sequences reference evaluator из K-E05-043.

## K-E05-061

- Дата / фаза: 2026-09-07 / workflow review.
- Тип / статус: Specification defect / Open.
- Утверждение: текущая E05 формулировка admission содержит цикл: `check` требует `approval`, но обязательный `approval.json` уже ссылается на `proofDigest` и `buildManifestDigest`, которые появляются только в результате последующих proof/build стадий.
- Scope: разделы 6.2 и 6.4 утверждённой E05 SPEC; composite reference evaluator от этого не зависит.
- Evidence: целевой порядок `check --approval` → `build --check` и одновременно обязательные поля `proofDigest`/`buildManifestDigest` в approval невозможно выполнить без placeholder, мутации подписанного артефакта или скрытого второго approval.
- Последствие: до реализации admission нужна отдельная утверждаемая поправка с двумя явными решениями человека: semantic approval owner bundle до proof и release admission exact proof/toolchain/build manifest после build; runtime принимает только второй артефакт. Нельзя молча ослабить binding или объявить один из шагов автоматическим.
- Supersedes / supersededBy: выявляет противоречие в E05-K09; требует SPEC amendment перед checkpoint 3.

## K-E05-062

- Дата / фаза: 2026-09-07 / implementation and bounded proof validation.
- Тип / статус: Composite lowering / Confirmed for structural and sequence range obligations.
- Утверждение: records и bounded `Seq<T,N>` можно детерминированно понизить из общего typed IR в Dafny без предметных helper operations: records становятся immutable generated datatypes, а structural sequence shapes — subset types; `seq.get` и `seq.append` создают явные index/capacity obligations.
- Scope: локальные modules без imports и `fold`; composite owner model и generated composite .NET consumer ещё не реализованы. Verified относится к fixture с внутренне построенной sequence, а не к произвольному внешнему input.
- Evidence: fixture `composite-lowering-safe.json`; bounded run `artifacts/local-validation/e05/composite-dafny-final-v2`; Modules conformance `220` checks / `64` fixtures; safe composite — `4 verified, 0 errors`, source SHA-256 `0d8123f3…9e9c2931`. `composite-runtime-valid.json` без caller range contract ожидаемо `Unproven` с тремя `assertion might not hold`. Summary SHA-256 `84f8b24c…6932cc3a`, module report SHA-256 `d7e16ab7…26617bc`; Reserve regression `29/29`, `10904` assertions, report SHA-256 `5ae5d697…15e4bb93`; E04 regression `PASS all: 141`, локальная копия report SHA-256 `8d35d274…cd5d4bef`.
- Последствие: reference evaluator и Dafny backend теперь имеют сравнимую composite operation surface. Следующий proof шаг обязан добавить composite owner predicates/модель либо `fold` invariant; нельзя трактовать structural/range verification как exact-outcome proof.
- Supersedes / supersededBy: снимает ограничение records/sequences candidate lowering из K-E05-060 и `docs/modules-v0.2.md`, сохраняя открытыми `fold`, imports, owner composite contracts и admission.

## K-E05-063

- Дата / фаза: 2026-09-07 / contract boundary review.
- Тип / статус: Specification defect / Open pending owner-composite amendment approval.
- Утверждение: разрешить именованный record/sequence в `strogo.owner-bundle.v0.2` без owner-owned type declarations небезопасно для semantic approval: bundle digest фиксирует имя типа, но форму полей и capacity продолжает задавать candidate module.
- Scope: переход от scalar owner checkpoint к composite exact-outcome contracts; composite candidate structural/range lowering `c5ca6b4` от дефекта не зависит.
- Evidence: текущие обязательные поля owner bundle — `schemaVersion,bundleId,entryContracts,models,limits`; parser принимает только scalar TypeRef, binder отклоняет `module.Types`. Утверждённая E05 требует equality/access records/sequences, но не задаёт отдельную owner type table. Поправка `specs/2026-09-07-e05-owner-composite-contract-v0.3.md` прошла independent fix-and-re-review на normative snapshot SHA-256 `C8CFBEBC…A12159FF`; семь wire/migration/cardinality/definedness/outcome findings исправлены, BLOCKER/HIGH/MEDIUM не осталось.
- Последствие: до composite owner EXEC требуется отдельная подтверждённая поправка. Предложено `strogo.owner-bundle.v0.3` с полным canonical `types`, exact equality module/bundle type projection, обязательным `modelRef` вместо дублирующего manual ensures и explicit v0.2 migration без переноса approval.
- Supersedes / supersededBy: уточняет E05 owner artifact §6.2 и ограничение K-E05-062; не изменяет admission amendment.

## K-E05-064

- Дата / фаза: 2026-09-07 / public oracle review.
- Тип / статус: Development fixture / Proposed and manually checked; not executed.
- Утверждение: Noita oracle из #8571 правильно проверяет перекрывающиеся окна и группировку по pattern signature вместо exact values, но недостаточен как regression для общего scanner: все его повторные signatures бинарны, повторная группа одна, constant pattern `000` отсутствует, а textual encoding canonical labels `>=10` не проверяется.
- Scope: раскрытый development corpus для будущего `fold`/scanner surface; это не held-out benchmark, не результат Strogo program и не evidence G05.
- Evidence: ответ Posting Board `c847cdaa-0fa1-489d-9a49-a80ca0d4863f` (#9354), readback в thread `e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e`. Вручную выведенный fixture `k=3`: `A=[1,1,1,2,2,3]`, `B=[4,5,4]`, `C=[6,7,6]`, `D=[8,9,10]`, `E=[11,12,13]`; lexicographic groups `001 -> (A,1),(A,3)`, `010 -> (B,0),(C,0)`, `012 -> (D,0),(E,0)`, locations ordered by message ID then offset.
- Последствие: этот fixture можно использовать только после независимой машинной проверки oracle. Отдельный boundary fixture обязан различать canonical labels `>=10`; constant pattern `000` также должен иметь отдельный ожидаемый результат. После использования для настройки операций все эти задачи остаются development fixtures по K-E05-025/K-E05-059.
- Supersedes / supersededBy: расширяет ограничения oracle K-E05-059; не меняет freeze/held-out policy K-E05-025.

## K-E05-065

- Дата / фаза: 2026-09-07 / specification and adversarial review.
- Тип / статус: Language/proof design / Proposed; pending fold SPEC owner approval.
- Утверждение: обязательность всех инвариантов не требует, чтобы agent вручную повторял доказуемые из конструкции факты. Для первого bounded left-fold slice compiler всегда добавляет index/immutability/termination/exact-owner-prefix clauses, а agent обязан дать ровно один closed semantic invariant; итоговая projection показывает оба слоя. Owner `requires` получает один bounded `forall.sequence`, потому что `OrderedExactAllocation` иначе не может выразить «каждый request > 0» без предметного opcode.
- Scope: `specs/2026-09-07-e05-fold-regions-and-invariants-v0.1.md`, только один root fold, одинаковый owner/candidate accumulator representation, без nested fold/import/helper/public facade. Это design proposal, не реализованная гарантия и не evidence G05/G06.
- Evidence: post-SPEC normative snapshot SHA-256 `F099F214B6B090F79CDCFC7F5825CEA6A2692A1A2996DD44138A5A5EF0D1FDE3`; separate procedural read-only reviewer закрыл migration allow-list, `ProofTypeRef`, owner-domain `OwnerPrefix`, per-argument equality, strict definedness и generated .NET evidence findings. Resource counterexample с arbitrary I64 range устранён заменой на non-nested sequence-index quantifier, signed `maxProofEvaluationSteps<=262144`, static worst-case cost и per-expression runtime counter.
- Последствие: после approval реализация не должна синтезировать скрытые semantic assumptions, принимать invariant без проверки или оценивать unbounded quantifier. Любое расширение на relational invariant, произвольный range либо nested fold требует новой SPEC/version; same `strogo.module.v0.2` support различается только через pinned toolchain identity до завершения всей обещанной v0.2 surface.
- Supersedes / supersededBy: конкретизирует invariant questions исходного замысла, E05 §6.2 и ограничения K-E05-063/K-E05-064; до approval не supersede утверждённые semantics.

## K-E05-066

- Дата / фаза: 2026-09-08 / implementation and semantic-boundary validation.
- Тип / статус: Owner representation / Confirmed for v0.3 checkpoint.
- Утверждение: exact minimal owner-semantic type closure достаточно для текущего closed type/expression surface: roots из entry/model signatures, expression metadata и witness values плюс record/sequence edges фиксируют весь approved composite смысл; private module types не входят в owner digest, но остаются в module/proof identity.
- Scope: `strogo.owner-bundle.v0.3`, local modules без imports/helpers/fold/refinements/aliases/constants. Future semantic edge требует новой schema и отдельной SPEC.
- Evidence: commits `f9d74c7` и `64954f3`; empty `Seq<Item,4>` сохраняет `Item` в closure; missing/extraneous/recursive/transitive declarations и reachable module drift отклоняются; shuffled fields дают те же canonical bytes; private module type успешно bindится, не меняет owner digest и меняет lowering digest. Публичные контрпримеры/уточнения #9543/#9545 стали executable regressions. Отдельный свежий checkout публичного commit `64954f339cd3f5f573572a5bb4e753197d88d602` на Windows/.NET SDK 10.0.400 независимо прошёл Modules conformance: exit 0, 236 checks / 65 fixtures; этот run не проверял Dafny/consumer и потому подтверждает только parser/closure/conformance границу того checkpoint.
- Последствие: human approval не зависит от private implementation types, но candidate не может подменить форму owner-reachable record/sequence. Closure нельзя автоматически расширять при появлении новых видов ссылок.
- Supersedes / supersededBy: закрывает implementation gap K-E05-063 для текущего surface; не закрывает admission gap K-E05-061.

## K-E05-067

- Дата / фаза: 2026-09-08 / proof, replay and migration validation.
- Тип / статус: Composite exact outcome / Confirmed for bounded checkpoint.
- Утверждение: одна owner-owned composite model может детерминированно принять две разные DAG-реализации и отклонить observable wrong order; concrete `Counterexample` допустим только после независимого witness replay, а partial model остаётся `Unproven`. Strict Boolean definedness требует вычисления обеих сторон `and/or`, тогда как `if` сохраняет lazy branch.
- Scope: `I64`, `Bool`, records и bounded sequences в owner bundle v0.3; Dafny 4.11.0 и generated C# на Windows x64. Это proof относительно approved bundle bytes, а не human approval, package admission или гарантия всей TCB.
- Evidence: final fresh run `artifacts/local-validation/e05/owner-v03-final-20260908-v2`; Modules 276 checks / 65 reported cases; обе composite candidates — `5 verified, 0 errors` и одинаковые generated consumer outcomes; wrong candidate — replay `empty-shape-v1` плюс failed postcondition; partial model и bounded nested `seq.append` — range/subset diagnostic и `Unproven`; strict `false and`/`true or` partial — `Unproven`, guarded `if` — `Verified`. v0.2 migration byte-for-byte совпадает с golden v0.3 digest `b0153dbe…fbe6`, считает удаляемый `ensures`, принимает 4096 scalar witness value nodes и отклоняет 4097 без output.
- Последствие: composite owner contract можно использовать как следующий dependency для fold design. Approval v0.2 не переносится, а migrator обязан сохранить все прежние semantic restrictions, даже если v0.3 стал выразительнее.
- Supersedes / supersededBy: расширяет K-E05-058 на composite exact outcome и снимает временное ограничение K-E05-056 только для native v0.3 strict-definedness semantics; legacy migration сохраняет v0.2 restriction.

## K-E05-068

- Дата / фаза: 2026-09-08 / regression environment.
- Тип / статус: Operational instability / Observed and bounded.
- Утверждение: E04 verifier process с фиксированным 60-секундным deadline может fail closed под фоновой нагрузкой без proof counterexample; такой `ProofNotEstablished` нельзя считать semantic regression либо PASS без отдельной диагностики.
- Scope: unchanged TaskGraph E04 regression gate на текущей Windows машине; не Modules performance и не G06 benchmark.
- Evidence: первый fresh E04 run остановился после 61 477 ms с `timedOut=true`, exit `-1` и публичным `ProofNotEstablished`; direct diagnostic тех же `Contract.dfy`/`Candidate.dfy` дал `74 verified, 0 errors` за 48 172 ms; один clean retry завершился `PASS all: 141`. Машиночитаемая сводка: `artifacts/local-validation/e05/owner-v03-final-20260908/e04-timeout-diagnostic.json`.
- Последствие: final evidence хранит и PASS clean retry, и предшествовавший timeout. Future CI должен либо обеспечить resource isolation, либо выделить отдельный wall-clock budget, не ослабляя verifier time limits.
- Supersedes / supersededBy: уточняет environment boundary K-E05-045/K-E05-057; semantic evidence E04 не изменяет.

## K-E05-069

- Дата / фаза: 2026-09-08 / public fold experiment design.
- Тип / статус: Hypothesis discriminator / Proposed; not executed.
- Утверждение: обязательность agent-authored invariant следует проверять различающим fixture, а не принимать как аксиому. Для checked-I64 left fold sum при `xs[i]>=0`, mathematical `total(xs)<=I64_MAX` и `acc=0` нужно сравнить одинаковые generated obligations при invariant `true` и `0<=acc<=total(xs)`; false invariant при satisfiable owner `requires` является обязательным negative control против vacuous proof.
- Scope: будущая fold SPEC, не owner-composite EXEC и не evidence необходимости обязательного синтаксиса invariant. Invariant-only named type также пока не показан как soundness counterexample, если он erased, conservatively defined и полностью проверяется.
- Evidence: вопрос #9554, message `12917211-862e-4c43-b261-98b3b47bb3d6`; ответ Помощника Архитектора #9556, message `5d3d37d4-7636-4490-af20-e38486e68291`. Автор ответа явно не запускал prover.
- Последствие: fold implementation не должна заранее заявлять обязательный semantic invariant доказанной необходимостью. Результат A/B fixture должен попасть в журнал независимо от того, различит ли он варианты.
- Supersedes / supersededBy: дополняет K-E05-065 конкретным falsifiable experiment; до выполнения не меняет утверждённые semantics.

## K-E05-070

- Дата / фаза: 2026-09-08 / post-EXEC adversarial review and fixes.
- Тип / статус: Proof/replay integrity / Confirmed for owner v0.3 checkpoint.
- Утверждение: typed result taxonomy недостаточна без trusted replay input и явного allow-list candidate failures. Публично создаваемый `OwnerContractBinding` нельзя считать доказательством полноты: replay обязан заново bind-ить `Module`+`Bundle`; `Counterexample` допустим только для двух успешно вычисленных разных значений, `CandidateError` — только для `ArithmeticOverflow`, `SequenceIndexOutOfRange` и `SequenceCapacityExceeded`, а неизвестная/internal ошибка evaluator является `ToolError`.
- Scope: concrete owner-witness replay текущего local module surface; это не proof receipt, admission либо диагностика arbitrary future runtime effects.
- Evidence: procedural read-only review нашёл forged empty `Entries` → zero-witness `Pass` и overly broad candidate blame. Regression с `OwnerContractBinding(..., Entries=[])` после исправления перестраивает binding и проверяет один witness; reflection-injected unsupported IR opcode даёт `ToolError/UnsupportedRuntimeOpcode`; fuel, invalid limits, overflow и value mismatch различаются. Final fix-and-re-review: PASS, 0 remaining BLOCKER/HIGH/MEDIUM; residual LOW — evaluator↔Dafny TCB и отсутствие admission receipt. Final Modules/Dafny run: 276 checks / 65 cases, report SHA-256 `b690a5e9…0b0a367`, harness SHA-256 `87c807db…a1d3bb`.
- Последствие: consumer может доверять replay status только после внутреннего rebinding; новые candidate-runtime failure codes нельзя автоматически относить к candidate без отдельной SPEC и distinguishing regression.
- Supersedes / supersededBy: уточняет K-E05-067 и закрывает post-EXEC replay findings; admission gap K-E05-061 остаётся.

## K-E05-071

- Дата / фаза: 2026-09-08 / regression environment.
- Тип / статус: Operational instability / Observed; cause not established.
- Утверждение: Reserve regression после серии proof/build процессов в этой сессии получил множественные `SolverTimeout`, тогда как следующий сериализованный clean run без изменений исходников прошёл 29/29 и 10 904 assertions. Наблюдение совместимо с contention/background-load гипотезой, но не доказывает её.
- Scope: локальная Windows validation environment; не semantic result owner-composite, Reserve либо Z3.
- Evidence: первый run был остановлен после нескольких typed timeout failures; повторный report `artifacts/local-validation/e05/owner-v03-final-20260908-v2/v0-regression.json` passed. E04 затем отдельно прошёл `PASS all: 141`.
- Последствие: финальный evidence использует чистые сериализованные PASS runs, а timeout не переименовывается в regression/counterexample. Для CI нужен контроль параллелизма либо отдельная диагностика resource contention.
- Supersedes / supersededBy: дополняет K-E05-068 аналогичным наблюдением для Reserve; причина остаётся открытой.

## K-E05-072

- Дата / фаза: 2026-09-08 / independent public reproduction and documentation audit.
- Тип / статус: External reproducibility / Modules and full Dafny harness confirmed for current `c99fd1f`.
- Утверждение: публичный owner-composite proof checkpoint воспроизводится в отдельном свежем checkout: после раннего run на `64954f3` текущий replay-hardening commit `c99fd1f` независимо прошёл все 276 Modules checks и полный pinned Dafny harness.
- Scope: Windows, .NET SDK 10.0.400, Dafny 4.11.0; public commits `64954f339cd3f5f573572a5bb4e753197d88d602` и `c99fd1f56790ffbc23cbca1173d7b643967c9b44`. Не CI и не Linux.
- Evidence: на `64954f3` команда Modules дала exit 0, 236 checks / 65 fixtures и явно исполнила empty `Seq<Item,4>`, missing `Item`, private/module-only drift assertions; первый Dafny harness также дал PASS. На `c99fd1f` отдельный Modules run дал exit 0/PASS, 276 checks; новый Dafny harness дал exit 0/PASS, strict `false and`/`true or` и nested append получили ожидаемый exit 4 с I64/subset diagnostics, guarded cases — `4 verified, 0 errors`, обе composite implementations — `5 verified, 0 errors` и ожидаемые consumers. Public read-back: #9664 `f93a02a9-bb4c-4d76-8ac7-17d887adb70c`, #9668 `38926233-524d-4de0-b0db-315ef4d59e74`, current commit result #9674 `d3342bf4-d9c3-4c60-afe8-d817cfbd2fe7`; temp reports участника не считаются долговечным project artifact.
- Последствие: README обновлён с устаревшего scalar 199-check описания на текущую owner-composite границу; independent evidence теперь покрывает Modules и Dafny для `c99fd1f`, но остаётся Windows-only и не является CI.
- Supersedes / supersededBy: уточняет external evidence K-E05-066 и current final evidence K-E05-067/K-E05-070.

## K-E05-073

- Дата / фаза: 2026-09-08 / public result publication.
- Тип / статус: Public reproducibility update / Published and read back.
- Утверждение: итог owner-composite v0.3 опубликован с exact public SHA, командой, локальными результатами, независимой границей и двумя исправленными review-дефектами; будущий fold A/B явно обозначен как ещё не выполненный.
- Scope: информационный public update, не CI/release/admission и не новое proof evidence.
- Evidence: Posting Board #9673, message `e084745d-1722-4bf1-a779-27b32b85e2b2`; 1160 UTF-8 bytes; fresh preview exact body/root/public=true/published=false; explicit publish; read-back подтвердил seq, ID и byte-equivalent body в thread `e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e`.
- Последствие: дальнейшее публичное утверждение о fold допустимо только после фактического A/B run. Сообщение #9673 фиксирует состояние на момент публикации; появившийся следом #9674 отдельно подтверждает текущий `c99fd1f` и не переписывает исторический текст.
- Supersedes / supersededBy: завершает публичный follow-up owner-composite checkpoint и сохраняет ограничение K-E05-069.

## K-E05-074

- Дата / фаза: 2026-09-08 / fold post-SPEC adversarial review.
- Тип / статус: Hypothesis discriminator / Revised proposal; reviewed, not executed, pending owner approval.
- Утверждение: bound `0<=accumulator<=sum(sequence)` сам по себе семантически истинен, но не является достаточным локальным индуктивным invariant для checked step `accumulator+element`: из текущей верхней границы и `element>=0` не следует верхняя граница следующего accumulator. Различающий B обязан дополнительно связывать accumulator с математической суммой обработанного префикса. Для этого fold SPEC вводит proof-only `seq.prefix_sum_i64(sequence,prefixLength)` с equations для нулевого и следующего префикса; B требует equality с prefix sum и bounds.
- Scope: `specs/2026-09-07-e05-fold-regions-and-invariants-v0.1.md`, checked-I64 sum A/B/C до настройки heuristics. Это reviewed design, не результат Dafny, не подтверждение обязательности invariant и не evidence G05.
- Evidence: independent review snapshot `C2E9008…F09A3` нашёл неиндуктивный B и неполный negative-control outcome contract; fixes прошли exact-snapshot review. Commit `5c2be7c0d7bc90a5a278957a6a28de682a2f4620`; normative §0–18 SHA-256 `5E399EFDF2973E1A44484BAC51F1525C7E339CECC48E7BE4786A5372947A2133`; открытых BLOCKER/HIGH/MEDIUM нет.
- Последствие: F-AC4 допускает только две различающие матрицы: A=`Verified` либо `Unproven`, B=`Verified`, C=`Unproven(initial)`. Parse/bind refusal, `Timeout`, `ToolError`, `Counterexample` или иной outcome любого control блокирует эксперимент; invariant-independent generated source сравнивается после canonical span normalization byte-for-byte.
- Supersedes / supersededBy: уточняет и частично supersede предложенный B из K-E05-069; сохраняет статус всего эксперимента `not executed` до EXEC утверждённой fold SPEC.

## K-E05-075

- Дата / фаза: 2026-09-08 / public concurrency benchmark design.
- Тип / статус: External benchmark / Proposed and refined publicly; not executed.
- Утверждение: доказательство инварианта отдельного перехода не покрывает конкурентный commit. Минимальный write-skew fixture: при `A[p]=0`, `B[p]=0`, version `7` два агента читают один snapshot и независимо предлагают `A[p]=1` и `B[p]=1`; каждый переход допустим относительно version 7, но совместный результат нарушает boundary invariant. Для первого допустимого outcome нужен сериализованный compare-version-and-commit: победитель создаёт version 8, проигравший получает `Conflict` без state effect, отбрасывает только своё speculative state и перечитывает version 8. Откат общего состояния к snapshot 7 после принятого commit неверен, потому что стирает подтверждённое изменение победителя.
- Scope: будущая модель effects/state/atomicity и host commit для multiplayer terrain chunks. Это не pure fold, не текущая owner-bundle semantics, не реализованная гарантия Strogo и не hidden evidence преимущества языка.
- Evidence: Posting Board [thread](https://getpostingboard.dev/b/t/e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e): исходное предложение #9681 `2e6c67f1-8ea1-4f8f-9e8c-3e9769b272e2`, минимальный fixture #9686 `ac3cf24c-c238-47f0-a11a-b4418f9015bb`, terrain race/outcome proposal #9689 `534b9ce3-3474-4d05-b3bc-9b4434329915`, уточнение rollback #9690 `4d9d42f3-dde9-47be-b9ed-6f8f247ce57d`. Публичные сообщения являются предложениями; executable receipts/final states ещё не предоставлены.
- Последствие: benchmark до использования должен задать receipts и final `A[p],B[p],version` для обоих порядков commit. Вариант «оба commit с winner» остаётся недоопределённым, пока не указан контракт квитанции проигравшего. Rebind `Module+Bundle` проверяет связь программы с контрактом и не заменяет rollback/изоляцию состояния игры.
- Supersedes / supersededBy: новый открытый development fixture вне текущей fold SPEC; после публикации не может считаться held-out benchmark.

## K-E05-076

- Дата / фаза: 2026-09-08 / public fold SPEC checkpoint.
- Тип / статус: Public review request / Published and read back; no external answer yet.
- Утверждение: публичный запрос на контрпример теперь связан с reviewed fold SPEC `5c2be7c`: он явно сообщает, что прежний bound-only B оказался неиндуктивным, новый B использует proof-only prefix-sum equality, а приёмка требует B=`Verified`, C=`Unproven(initial)` и запрещает выдавать operational/refusal/Counterexample outcome за успешный discriminator.
- Scope: приглашение к soundness/expressivity review `seq.prefix_sum_i64` и A/B protocol. Публикация не является EXEC, proof result, owner approval либо подтверждением преимущества языка.
- Evidence: Posting Board #9706, message `1f1e0482-7ddc-4ff5-bd08-c001aa9cb31b`, [thread](https://getpostingboard.dev/b/t/e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e); exact body 1065 UTF-8 bytes; fresh preview request `7c66444d-5526-4935-9ddb-e1355c563439` подтвердил body/reply target/public=true/published=false, explicit publish succeeded, thread read-back подтвердил seq/ID и byte-equivalent body.
- Последствие: рациональный внешний counterexample должен оцениваться относительно normative §0–18 hash `5E399EFD…47A2133`; ответ может изменить ещё не утверждённую SPEC, но отсутствие ответа не считается подтверждением soundness или expressivity.
- Supersedes / supersededBy: публично продолжает K-E05-069/K-E05-074; не меняет их статус `not executed`.

## K-E05-077

- Дата / фаза: 2026-09-08 / external fold experiment review.
- Тип / статус: Experimental attribution / Rational refinement proposed; not executed.
- Утверждение: A=`true` против B=`prefix equality + bounds` различает пользу полного B, но не вклад каждого conjunct. Optional diagnostic D с одной equality `accumulator==prefixSum(sequence,prefixLength)` может показать, что наблюдаемый выигрыш объясняется equality/lemma exposure без необходимости bounds. D не нужен для soundness или базовой приёмки и не должен блокировать первый slice.
- Scope: причинная интерпретация sum discriminator, не новый language guarantee и не soundness counterexample `seq.prefix_sum_i64`.
- Evidence: Posting Board #9707, message `bbfd594a-2e35-412f-9a68-e7f9fcd7d650`, [thread](https://getpostingboard.dev/b/t/e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e); автор явно не утверждает soundness counterexample и предлагает D только как diagnostic.
- Последствие: если shared lowering/lemmas меняются после initial `B=Unproven`, новая revision замораживается и на ней заново запускаются все варианты; сравнивать новый B со старым A запрещено. Даже при `A=Unproven, B=Verified` вывод ограничен пользой полного B для одного fixture; необходимость bounds отдельно заявляется только при дополнительном различающем evidence.
- Supersedes / supersededBy: уточняет causal claim K-E05-074 и требует поправки ещё не утверждённой fold SPEC; статусы K-E05-069/K-E05-074 остаются `not executed`.
