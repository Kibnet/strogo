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

## K-E05-078

- Дата / фаза: 2026-09-08 / fold causal-attribution post-SPEC review.
- Тип / статус: Experiment contract / Reviewed and confirmed as specification; not executed, pending owner approval.
- Утверждение: optional diagnostic D не входит в обязательный A/B/C success condition, но необязательность не разрешает скрыть semantic contradiction. Отсутствие D, `D=Verified`, `D=Unproven`, `D=Timeout` или `D=ToolError` не меняют успешный A/B/C status; replayed `D=Counterexample`, refusal корректного D либо иной shared-semantic contradiction блокирует validation checkpoint. A/B измеряет только достижение `Verified` на одной frozen revision: равные A/B statuses не опровергают возможные различия proof cost или solver stability.
- Scope: causal interpretation и fail-closed validation protocol checked-I64 sum discriminator; не новый executable construct и не результат prover.
- Evidence: fold SPEC commit `2e3b3241c59f406aa2b975dd325daf9cb8dbb494`; normative §0–18 SHA-256 `9E76E1FF07699A3873A3128CEAB077D37BDD1BA945FA143EB746F0AE2FFDE438`; два procedural read-only reviewers дали PASS exact pre-audit snapshot `EE1AD974…2BD71A`, открытых BLOCKER/HIGH/MEDIUM нет.
- Последствие: после любого shared proof change меняется `toolchainDigest` и полностью повторяется A/B/C плюс D, если он запускался; cross-revision comparison запрещён. Доказательство общего выигрыша агента, стоимости либо устойчивости требует отдельного experiment.
- Supersedes / supersededBy: закрывает amendment, потребованный K-E05-077, и уточняет causal wording K-E05-074; весь fold experiment остаётся `not executed` до owner approval.

## K-E05-079

- Дата / фаза: 2026-09-08 / Linux portability reproduction.
- Тип / статус: External and local reproducibility / Confirmed for Modules conformance under Ubuntu 24.04 WSL2.
- Утверждение: текущий .NET Modules conformance executable воспроизводится на Linux x64 под WSL2 из чистого публичного checkout без Windows `bin/obj`: parser, codec, compiler, reference evaluator и owner replay suite проходит 276 checks / 65 fixtures на .NET SDK 10.0.400/runtime 10.0.11.
- Scope: Ubuntu 24.04.2 LTS under WSL2, x86_64, public commits `c99fd1f` (external participant) и `2e3b3241c59f406aa2b975dd325daf9cb8dbb494` (повтор текущей задачей; между ними production Modules code не менялся). Это не отдельный native Linux host, CI, Dafny proof run, generated ReadyToRun Linux artifact или объявление supported Linux profile.
- Evidence: Posting Board #9709, message `8be1bf21-1b4c-4406-8d21-aa5563212977`, [thread](https://getpostingboard.dev/b/t/e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e): participant сообщил exit 0, `passed=true`, `checks=276`, `fixtures=65` и SHA512-проверенный SDK archive. Независимый run текущей задачей установил официальный SDK через `dotnet-install.sh`, клонировал public `2e3b324`, получил exit 0 / `PASS conformance checks=276`; сохранённый gitignored report `artifacts/local-validation/e05/linux-wsl2-2e3b324-20260908/linux-conformance.json` имеет SHA-256 `fd4194dac0bf90a924ed6746915f689377519258877d573641a66b059181fad6`, рядом сохранены `environment.txt`, `console.log`, `sha256.txt`. Публичный follow-up #9713, message `664957b0-32b0-47ea-8265-88306079cc42`, связал этот hash с docs commit `2f8b3e9`, запросил native-host Linux либо Linux Dafny result и прошёл fresh preview → explicit publish → exact read-back (1013 UTF-8 bytes; request `266830ad-1eeb-464e-a449-7ff3c2ca788d`).
- Последствие: подтверждена переносимость managed Modules conformance path между Windows и WSL2 Linux для текущего surface. Windows x64 остаётся единственной заявленной supported configuration; E06/Dafny/backend/package portability всё ещё требует отдельных proof/build/runtime gates.
- Supersedes / supersededBy: уточняет Windows-only evidence boundary K-E05-072 и публичный запрос #9680; не подтверждает G02/G06 или всю Linux toolchain.

## K-E05-080

- Дата / фаза: 2026-09-08 / public evidence classification review.
- Тип / статус: Claim hygiene / Confirmed correction.
- Утверждение: повтор чужого public commit участником того же совместного исследования является внешней репликацией, но не автоматически независимым third-party audit. Hash отчёта идентифицирует конкретные bytes и полезен для сопоставления evidence, однако не заменяет сам отчёт, console log и описание среды.
- Scope: публичные Windows/WSL2 reports #9674/#9709 и README wording; не отменяет их фактические PASS outcomes и не утверждает координацию конкретных команд или результатов.
- Evidence: Posting Board #9715, message `b7c17ee5-b30d-40f3-b147-1c2a899c45e3`, [thread](https://getpostingboard.dev/b/t/e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e); локальные Linux evidence K-E05-079 содержат raw JSON, environment, console и digest manifest, поэтому остаются проверяемыми без переименования в независимый аудит.
- Последствие: README больше не использует слово «независимо» для participant run. Будущие reproducibility claims обязаны отдельно указывать связь участника с исследованием и наличие raw evidence; third-party audit допустим только при действительно отдельном исследователе и доступных материалах.
- Supersedes / supersededBy: уточняет classification K-E05-072/K-E05-079, сохраняя их технические результаты.

## K-E05-081

- Дата / фаза: 2026-09-08 / Linux proof portability reproduction.
- Тип / статус: Backend portability / Confirmed for generated Dafny semantic matrix under Ubuntu 24.04 WSL2.
- Утверждение: текущие generated `.dfy` obligations и их ожидаемые proof outcomes не зависят от Windows Dafny package: на Linux тот же pinned Dafny 4.11.0 воспроизвёл все 19 cases — 10 `Verified` и 9 обязательных отказов с теми же диагностическими классами.
- Scope: clean git archive commit `8b53cddb6d143cc1517ba07d6e7597255b876894`, Ubuntu 24.04.2 WSL2 x86_64, .NET SDK 10.0.400/runtime 10.0.11, Dafny `4.11.0+fcb2042…`. Это не native Linux host, CI, cross-platform process containment, full Windows harness, execution generated consumers либо Linux ReadyToRun.
- Evidence: перед proof matrix Linux Modules conformance дал 276 checks / 65 fixtures. Официальный asset `dafny-4.11.0-x64-ubuntu-22.04.zip` имел SHA-256 `a46a9ff7cdd720f7955854c78e95df13f4cfe6b80691b05f8654fe19e8267179`, совпавший с digest GitHub Releases API. Gitignored directory `artifacts/local-validation/e05/linux-dafny-8b53cdd-20260908/` хранит source candidates, per-case logs, generated C#, release metadata, environment и manifest; санитизированная public copy хранится в `artifacts/e05/linux-dafny-wsl2-8b53cdd/`. `results.tsv` SHA-256 `62901739cb6dde4c8d4bf833303cc3136af814973b2138643d9f69279e13c72d`, Modules report SHA-256 `7fd86486be41dcda91c6c784b7b97b3f1e2c52278dfbb530655f78908b4c1617`.
- Последствие: зрелая Dafny/.NET цепочка уже даёт реалистичный путь к нескольким платформам без собственного proof backend. Для supported Linux profile всё ещё нужны переносимый runner с bounded process-tree semantics, Linux build/runtime gate и CI на отдельном host; эти задачи нельзя считать доказанными данным WSL2 run.
- Supersedes / supersededBy: расширяет K-E05-079 с managed conformance до proof/translation semantics и сужает прежнее общее «Linux Dafny не проверен»; G02/G06 остаются открыты.

## K-E05-082

- Дата / фаза: 2026-09-08 / public Linux proof result.
- Тип / статус: Public reproducibility update / Published and read back.
- Утверждение: Linux Dafny semantic matrix опубликована с frozen input commit, tool versions, двумя evidence hashes и явным отделением WSL2 proof/translation от full cross-platform harness.
- Scope: public report результата K-E05-081 и исправления classification K-E05-080; не новый run, CI либо native Linux evidence.
- Evidence: Posting Board #9720, message `643cc33b-7866-4371-ae69-131eda355564`, [thread](https://getpostingboard.dev/b/t/e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e); exact body 1148 UTF-8 bytes; preview request `00876d2d-d513-4e2e-9337-85e3ce283c45` подтвердил root/public=true/published=false, explicit POST publish succeeded, read-back подтвердил seq/ID и byte-exact body.
- Последствие: следующий сильный portability evidence — тот же commit на отдельном native Linux host; WSL2 результат нельзя называть full Linux support.
- Supersedes / supersededBy: публично фиксирует K-E05-080/K-E05-081.

## K-E05-083

- Дата / фаза: 2026-09-08 / public capability-boundary benchmark design.
- Тип / статус: External benchmark / Concrete trace proposed; not executed.
- Утверждение: статическое доказательство допустимости эффекта не гарантирует актуальность полномочия в момент исполнения. Для STRICT policy grant, проверенный при epoch 7 и отозванный при epoch 8 до commit, обязан дать `DENY_STALE`/no effect; partition даёт `UNKNOWN`/no effect. LEASE является другой заранее объявленной политикой: commit до `lease_until=T` даёт `ALLOW_LEASED`, после T — `DENY_EXPIRED`. Близость fresh check к side effect недостаточна: authorize+commit должны иметь одну enforceable linearization point.
- Scope: будущие capabilities/effects и внешний executor; не pure Modules v0.2, не текущая гарантия Strogo и не готовый revocation protocol.
- Evidence: исходный запрос Posting Board #9716, message `c535bb89-90fa-4462-8ff4-14464ac1d5d7`; предложенная STRICT/LEASE trace #9722, message `03afc325-e20c-4234-a66a-7a2675bd570e`; уточнение двух порядков revoke-first/commit-first и receipt #9723, message `ebe97b7d-2a57-4e6b-8b33-f292c05f4f46`; [thread](https://getpostingboard.dev/b/t/f762f92a-fae1-41a4-95b2-b150f49f96be). Это спецификация fixture, не executable run; участник ещё не подтвердил пару interleavings после #9723.
- Последствие: будущая SPEC capabilities должна разделять proof validity и current authority, задавать ordering authority, lease/freshness, token expiry/single-use/fencing и fail-closed `UNKNOWN`. Receipt фиксирует фактический порядок commit/revoke, а внешний сервис без enforceable token/order оставляет это предположением executor; локальное доказательство программы не может само создать распределённый порядок событий.
- Supersedes / supersededBy: новый открытый benchmark, дополняющий concurrency boundary K-E05-075.

## K-E05-084

- Дата / фаза: 2026-09-08 / retained Linux evidence review.
- Тип / статус: Validation oracle / Latent defect confirmed; recorded run outcomes remain valid.
- Утверждение: reusable negative-case oracle не должен принимать любой ненулевой exit вместе с ожидаемой подстрокой: процесс может напечатать proof diagnostic, затем зависнуть, и `timeout` вернёт `124`, который слабое условие ошибочно классифицирует как PASS. Даже exact exit `4` и summary недостаточны, если рядом присутствует `Model parsing error`, как наблюдалось в прежнем Windows Dafny evidence. Для frozen matrix нужны exact Dafny exit `4`, completed verifier summary, case-specific diagnostic/count и отсутствие известных operational/model-parser markers.
- Scope: первый локальный `run.sh` K-E05-081 и публикуемый reproduction driver; не production parser/lowering и не опровержение текущих 19 результатов.
- Evidence: review сохранённых `results.tsv`, 19 per-case logs и driver. Фактические 10 positive logs имеют exit `0`/`0 errors`; все 9 negative rows имеют exit `4`, завершённую строку `Dafny program verifier finished ...` с ожидаемым числом ошибок и нужную diagnostic; timeout/tool/model-parser markers отсутствуют. Public package `artifacts/e05/linux-dafny-wsl2-8b53cdd/` сохраняет эти логи, а `reproduce.sh` использует усиленный oracle.
- Последствие: будущие proof harnesses обязаны классифицировать operational exit и tool/model diagnostics до сопоставления proof diagnostics и требовать evidence завершённого verifier run; substring не является самостоятельным proof status. Pinned tool download обязан проверяться по встроенному известному digest, даже если внешний metadata API временно не вернул digest.
- Supersedes / supersededBy: уточняет validation strength K-E05-081 без изменения его observed outcome.

## K-E05-085

- Дата / фаза: 2026-09-08 / public Linux evidence follow-up.
- Тип / статус: Public reproducibility update / Published and read back.
- Утверждение: санитизированный public evidence package связывает Linux result с 19 generated `.dfy`, 19 per-case logs, conformance report, asset metadata, environment, manifest и усиленным reproduction driver; найденный latent timeout-oracle defect опубликован вместе с ограничением, а не скрыт за PASS.
- Scope: retained evidence commit `9efce9ebaf81a0ccdda8e765d2584db295060343`; не новый proof run или независимый audit.
- Evidence: Posting Board #9724, message `115d1c7f-b196-497f-b7f9-2a7e1d65a7ec`, [thread](https://getpostingboard.dev/b/t/e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e); exact body 1076 UTF-8 bytes; preview request `7f585654-1cac-4354-b479-b9cf215781c5` подтвердил root/public=true/published=false, explicit POST publish succeeded, read-back подтвердил seq/ID и byte-exact body.
- Последствие: внешний reviewer теперь может отличить expected proof refusal от timeout/tool/parser failure по сохранённым bytes; новый run всё ещё нужен для независимого воспроизведения на другом host.
- Supersedes / supersededBy: публично продолжает K-E05-081/K-E05-082/K-E05-084.

## K-E05-086

- Дата / фаза: 2026-09-08 / public Linux oracle correction.
- Тип / статус: Public correction / Published and read back.
- Утверждение: второе review уточнило, что exact exit/summary сами по себе не исключают дополнительный `Model parsing error`, а optional API digest не является достаточным pin; исправление и неизменность observed 19 outcomes опубликованы явно.
- Scope: reusable evidence driver commit `df522fc05a7a4a22b853722edaa5e74245414561`; не новый proof run и не изменение retained logs.
- Evidence: Posting Board #9727, message `8c50fc49-7624-4f8d-b9c4-e4e39543f505`, [thread](https://getpostingboard.dev/b/t/e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e); exact body 1114 UTF-8 bytes; preview request `85bd5430-53fb-4d2c-91af-f8c022083f31` подтвердил root/public=true/published=false, explicit POST publish succeeded, read-back подтвердил seq/ID и byte-exact body. Последующее source review public diff подтвердило закрытие трёх предъявленных counterexamples: timeout `124`, `Model parsing error` рядом с обычным proof summary и пустой API digest; нового полного run и универсального распознавания неизвестных tool failures reviewer не заявлял.
- Последствие: public research trace содержит не только PASS, но и обе последовательные корректировки oracle; future reviewer может проверить fixed digest и forbidden model/tool markers в public driver.
- Supersedes / supersededBy: публично уточняет K-E05-084/K-E05-085.

## K-E05-087

- Дата / фаза: 2026-09-08 / fold v0.1 EXEC, checkpoint 1.
- Тип / статус: Language implementation / Confirmed locally for source, IR and reference execution.
- Утверждение: один root-result `fold` с явными sequence, initial accumulator, ordered environment, closed step-region и обязательным proof invariant теперь проходит единый typed pipeline `source -> canonical source -> IR -> reference evaluator`. Evaluator выполняет детерминированный left fold, не исполняет step для empty sequence и расходует один shared step на dispatch, каждую iteration и каждый реально выполненный step node.
- Scope: `strogo.module.v0.2` parser/codec/compiler/reference evaluator на Windows x64. Invariant на этом этапе сохраняется в source/IR digest и статически проверяется, но не вычисляется runtime evaluator; owner bundle v0.4, binder, Dafny loop proof и A/B/C ещё не реализованы и этим checkpoint не подтверждены.
- Evidence: fixture `fixtures/modules-v0.2/fold-sum-valid.json`; Modules conformance `PASS conformance checks=295`, `passed=true`, 75 report entries; solution build .NET 10 Release — 0 warnings/errors. Positive checks покрывают canonical property reordering, `MathInt`, `math.le`, prefix/sum, scoped `forall.sequence`, empty/non-empty traversal, ordered environment, immutable input and exact step counts. Negative checks покрывают missing invariant, out-of-range environment, `param` in invariant, hidden step capture, nested fold, step call, non-result fold, prefix arity/type and shared budget refusal.
- Последствие: fold больше не является только notation/spec idea: executable core slice существует и готов к связыванию с owner v0.4. Runtime result всё ещё нельзя называть formally verified, пока compiler-owned obligations и Dafny proof checkpoint не завершены.
- Supersedes / supersededBy: реализует первый checkpoint утверждённой fold SPEC с normative hash `9E76E1FF07699A3873A3128CEAB077D37BDD1BA945FA143EB746F0AE2FFDE438`; ожидает checkpoint 2 owner v0.4.

## K-E05-088

- Дата / фаза: 2026-09-08 / fold v0.1 EXEC, checkpoint 2.
- Тип / статус: Owner semantics implementation / Confirmed locally for v0.4 parse, migration, evaluation, binding and replay.
- Утверждение: `strogo.owner-bundle.v0.4` реализован отдельным от v0.3 parser/model API, включает явный `maxProofEvaluationSteps`, root-only owner fold, closed proof AST с `MathInt`, bounded `forall.sequence`, sum/prefix-sum и независимый fail-closed proof evaluator. Чистая миграция v0.3->v0.4 сохраняет четыре прежних limit, canonical requires subtree и старое non-fold поведение; v0.3/v0.4 parsers взаимно отвергают чужую schemaVersion.
- Scope: owner bundle parser/codec/model evaluator/proof evaluator/binder/witness replay на Windows x64. Binder подтверждает signature/type/arity pairing owner и candidate fold, но equality их входных выражений пока остаётся Dafny obligation следующего checkpoint. Proof evaluator проверяет witnesses и invariant instantiations; он не заменяет доказательство totality на всём owner domain.
- Evidence: fixture `fixtures/modules-v0.2/owner-fold-sum-valid-v0.4.json`; Modules conformance `PASS conformance checks=318`, `passed=true`, 76 report entries; solution Release build — 0 warnings/errors. Replay двух fold witnesses дал `Pass`; owner results `6` и `22` совпали с candidate. Регрессии подтверждают strict evaluation всех quantifier bodies, left-to-right prefix arguments, strict append operands, exact migration subtree, fresh proof counters, cost refusal, reserved `MathInt`, nested-fold/role/type refusals and preservation of migrated v0.3 replay.
- Последствие: owner intent для fold теперь имеет versioned canonical bytes, independent executable oracle и строгую связь с candidate shape. Статус `Verified` ещё недоступен: нужны compiler-owned loop obligations, obligation map и реальный Dafny A/B/C run.
- Supersedes / supersededBy: реализует второй checkpoint утверждённой fold SPEC и продолжает K-E05-087; ожидает checkpoint 3.

## K-E05-089

- Дата / фаза: 2026-09-08 / external capability review.
- Тип / статус: Distributed-effect counterexample / Proposed publicly; not executed.
- Утверждение: потерянный ответ после фактического внешнего `COMMIT` не позволяет позднему `REVOKE` превратить retry в обычный `DENY/no effect`. Повтор должен быть идемпотентным и возвращать `COMMITTED_BEFORE_REVOKE` по sink receipt либо `UNKNOWN_EFFECT` до reconciliation; иначе возможен второй эффект или ложное утверждение об отсутствии первого.
- Scope: будущий capability/effect executor и сервис с общей точкой сериализации; не pure fold, не текущий runtime и не реализованная гарантия Strogo. STRICT возможен только при enforceable ordering/idempotency со стороны effecting service; прочие sinks остаются BEST_EFFORT.
- Evidence: Posting Board #9740, [thread](https://getpostingboard.dev/b/t/f762f92a-fae1-41a4-95b2-b150f49f96be); предложенная trace содержит accepted commit, потерянный response, completed revoke и retry, но executable fixture/receipts ещё не опубликованы.
- Последствие: будущий receipt должен содержать sink commit/order id, а состояние `UNKNOWN_EFFECT` обязано блокировать слепой повтор до reconciliation. Локальная проверка capability epoch не заменяет идемпотентность и ordering authority внешнего сервиса.
- Supersedes / supersededBy: дополняет K-E05-083 случаем потерянного ответа после commit.

## K-E05-090

- Дата / фаза: 2026-09-08 / external agent-evaluation design.
- Тип / статус: Experiment design / Refined publicly; not executed.
- Утверждение: сравнение стоимости реализации на agent-oriented и human-oriented представлениях должно фиксировать одинаковую семантическую информацию, задачи, model/reasoning/tool budget и frozen held-out oracle; считать нужно все repair attempts, failures, tool output и coordination, а success rate/time показывать отдельно. `NoChangeNeeded`, исправимый defect, `InsufficientEvidence/Unknown` и `ContractConflict` являются разными outcomes: отсутствие наблюдения не доказывает противоречие, а timeout/Unproven не доказывают ни один из них.
- Scope: будущий G05 experiment, не измерение текущего fold и не evidence экономии токенов.
- Evidence: Posting Board #9743/#9747/#9775/#9780/#9787/#9788/#9794, [thread](https://getpostingboard.dev/b/t/2c88ac5b-38e3-4b14-84e6-ac9231457704). Публично предложены одинаковая bugfix-задача с contracts/без них, отдельный видимый repair suite, frozen held-out oracle и negative control против ложного `ContractConflict`; agent run отсутствует. Локальный конечный перебор для минимальных D={0,1} различил неоднозначное наблюдение `y=x` и поточечно несовместимые `y=0 AND y=1`, но не является Strogo/agent eval.
- Последствие: первый G05 harness должен иметь минимум correct/no-change, repairable, contradictory и observationally-insufficient fixtures, отдельно проверять приложенное свидетельство и учитывать `Unknown` без присвоения успеха.
- Supersedes / supersededBy: уточняет будущую проверку G05; не меняет текущую fold SPEC.

## K-E05-091

- Дата / фаза: 2026-09-08 / fold checkpoint 2 reproducibility.
- Тип / статус: External replication / Confirmed for public owner v0.4 checkpoint.
- Утверждение: public commit `6583814896179dd9d2b8ff9d13eacfcb5539517d` воспроизводится в отдельном clean checkout на Windows: Modules conformance завершился exit 0 и `PASS conformance checks=318`.
- Scope: parser/codec/evaluator/binder/replay suite checkpoint 2; не audit полноты acceptance matrix, не Dafny proof и не generated consumer.
- Evidence: внешний локальный прогон `dotnet run --project tests/Strogo.Modules.Conformance -c Release -- --report ...` из отдельного checkout exact commit `6583814`; raw report в текущем repository не импортировался.
- Последствие: checkpoint 2 доступен и исполним вне исходного worktree, но proof claims начинаются только с checkpoint 3 evidence.
- Supersedes / supersededBy: независимо подтверждает локальный результат K-E05-088 в его фактической границе.

## K-E05-092

- Дата / фаза: 2026-09-08 / fold v0.1 EXEC, sum discriminator.
- Тип / статус: Proof experiment / Confirmed under Ubuntu 24.04 WSL2; Windows negative control has a tool limitation.
- Утверждение: на одной frozen lowering revision `strogo.fold-dafny-lowering.v0.4` sum variants дали A=`Verified` (`21/0`), B=`Verified` (`22/0`) и C=`Unproven(initial)` (`20/1`). A/B/C имеют разные source/proof identities и byte-identical source после замены всех invariant-dependent spans. Результат означает только, что полный B не понадобился для достижения статуса `Verified` на этом fixture; он не измеряет proof cost/stability и не подтверждает G05.
- Scope: официальный Dafny 4.11.0, Ubuntu 24.04 WSL2 x64, generated checked-I64 sum, один owner bundle и один toolchain digest. Windows package также подтверждает A/B, но C вместе с ожидаемым proof failure печатает `Model parsing error`; строгий oracle поэтому не принимает Windows C как завершённый discriminator.
- Evidence: `tools/Test-Fold-Discriminator.sh`; gitignored run `artifacts/local-validation/e05/fold-v04-harness-20260908-8/` содержит два byte-equal lowering runs, exact A/B/C logs, obligation maps, tool identity/digest и strict absence of operational markers under Linux. Dafny executable SHA-256 `e540b4826363afb87c326446239a682d45086905425fa6299c103eca9693846d`.
- Последствие: обязательный A/B/C experiment закрыт допустимым outcome. Optional D остаётся не выполнен и не нужен для этого status-level вывода; любой будущий shared proof change требует полного rerun.
- Supersedes / supersededBy: исполняет experiment contract K-E05-078 и уточняет исторический v0.3 Linux result тем же outcome на итоговой v0.4 lowering revision.

## K-E05-093

- Дата / фаза: 2026-09-08 / fold v0.1 EXEC, checkpoint 3.
- Тип / статус: Generic fold proof and runtime / Confirmed locally under Windows and Ubuntu 24.04 WSL2.
- Утверждение: первая версия fold lowerer была неявно зашита под `I64` sum и конфликтовала generated predicate `R000` с record type `R000`; scalar discriminator этого не обнаруживал. Lowerer исправлен: canonical sum сохраняет отдельный MathInt/range proof path, остальные accumulator types используют exact typed owner prefix, generated names разделены, а record/sequence `AllocationState` доказывается для двух разных candidate DAG.
- Scope: root-only bounded left fold, same accumulator representation, records и bounded sequences; nested fold, helpers/imports, relational accumulator representation и public admission/runtime facade остаются вне scope.
- Evidence: fixtures `fold-allocation-primary.json`, `fold-allocation-alternative.json`, `owner-fold-allocation-v0.4.json`; Modules conformance `PASS conformance checks=360`. Linux strict harness: обе allocation candidates `18 verified, 0 errors`; generated .NET consumers для обеих возвращают empty=`5/[]`, ordered=`0/[4,0,1]`, MAX=`0/[I64.MAX]`; weak invariant и три type-correct sequence/initial/environment mutations дают expected Unproven по mapped obligations. Два чистых lowering runs byte-identical.
- Последствие: общий fold впервые проверен на composite accumulator и реальном исполняемом generated C#, а не только на scalar sum. Обязательный agent invariant имеет наблюдаемую роль для allocation capacity proof, хотя sum A показывает, что нетривиальное содержание не требуется каждому алгоритму.
- Supersedes / supersededBy: закрывает proof/runtime часть K-E05-087/K-E05-088; полный EXEC ещё требует regressions, public docs/evidence и post-EXEC review.

## K-E05-094

- Дата / фаза: 2026-09-08 / fold checkpoint 3 reproducibility.
- Тип / статус: External replication / Confirmed for public implementation commit.
- Утверждение: public commit `134856f489a4e5f34ac0149d0c789b3021e43692` воспроизведён в отдельном clean Windows checkout: Modules conformance завершился exit `0` и `PASS conformance checks=360`. Отдельная инспекция подтвердила exact runtime budget checks для `7`, empty `1` и refusal на `6` с fold/iteration locus.
- Scope: source/parser/IR/evaluator/owner v0.4/lowering conformance checkpoint 3; не запуск Dafny, generated consumer, CI или независимый аудит корректности тестов.
- Evidence: команда `dotnet run --project tests/Strogo.Modules.Conformance -c Release -- --report independent-conformance-134856f.json` в clean checkout exact commit; временный checkout после проверки не импортировался в repository evidence.
- Последствие: managed checkpoint воспроизводим вне исходного worktree. Proof claims по-прежнему опираются на отдельный Linux Dafny harness и его строгий oracle.
- Supersedes / supersededBy: независимо подтверждает managed часть K-E05-093.

## K-E05-095

- Дата / фаза: 2026-09-08 / external agent-evaluation design.
- Тип / статус: Experiment contract clarification / Accepted publicly; not executed.
- Утверждение: будущий G05 oracle должен заранее фиксировать observations, admissible actions и формулу verdict. Вывод нельзя переносить между интерфейсами с разным объёмом наблюдений или допустимых действий, даже если текст задачи выглядит одинаково.
- Scope: будущий сравнительный agent experiment; не текущий fold proof и не измерение производительности Strogo.
- Evidence: Posting Board #9799, [thread](https://getpostingboard.dev/b/t/2c88ac5b-38e3-4b14-84e6-ac9231457704); участник явно принял это ограничение эксперимента.
- Последствие: G05 fixtures должны version/hash-bind не только task и held-out oracle, но и observation/action interface; иначе сравнение не воспроизводимо и не поддерживает причинный вывод.
- Supersedes / supersededBy: уточняет K-E05-090.

## K-E05-096

- Дата / фаза: 2026-09-08 / fold completion review.
- Тип / статус: Reproduction harness defect / Fixed and reproduced.
- Утверждение: generated `Consumer.csproj` в `Test-Fold-Discriminator.sh` не задавал `ImplicitUsings` и случайно зависел от repository `Directory.Build.props`. При внешнем `--run-dir` проект не видел `Console`/LINQ. Шаблон теперь явно включает `ImplicitUsings=enable` и является самодостаточным относительно этой настройки.
- Scope: Linux fold validation harness и generated consumer project; production parser/IR/lowering semantics не менялись.
- Evidence: изолированный внешний project до исправления дал `CS0103` для `Console`; после исправления полный harness в `/tmp/strogo-fold-external-134856f-implicit-usings` завершился exit `0` и `PASS fold discriminator and allocation`, включая A/B/C, allocation mutations и оба consumers. Исправление и два explicit boundary checks опубликованы commit `9aff2dfe9435db562c6c715fb886b08c04a3156a`.
- Последствие: документированный произвольный абсолютный `--run-dir` действительно поддержан; будущие generated test projects обязаны явно задавать настройки, от которых зависит компиляция.
- Supersedes / supersededBy: исправляет скрытую границу воспроизводимости K-E05-092/K-E05-093.

## K-E05-097

- Дата / фаза: 2026-09-08 / fold v0.1 completion.
- Тип / статус: EXEC closure / Confirmed locally and retained publicly.
- Утверждение: утверждённый bounded fold v0.1 закрывает F-AC1–F-AC9 для первого root-only slice: strict source/IR, reference execution, owner bundle v0.4, bounded proof evaluation, exact typed owner-prefix lowering, A/B/C discriminator, две composite allocation candidates, stable obligations, mutations, migration, determinism и generated .NET execution.
- Scope: Windows x64 managed regressions и Ubuntu 24.04 WSL2 Dafny 4.11.0. Это не native Linux/CI, admission/package, imports/helpers, nested folds, relational accumulator proof, G05/G06 или доказательство всей trusted computing base.
- Evidence: Release build — 0 warnings/errors; Modules `PASS conformance checks=362`; Reserve `29/29 cases; 10904 assertions`; TaskGraph `PASS all: 141`; full Linux fold harness exit `0` внутри repository и с external `/tmp` run directory. Санитизированный package `artifacts/e05/fold-v0.1-134856f/` содержит 38 файлов, exact generated sources/obligations/logs/reports и `sha256.txt` без локальных absolute paths.
- Последствие: следующий эксперимент может опираться на fold v0.1 как замороженный executable/proof baseline, но обязан version-bind toolchain/owner/interface и не переносить выводы на открытые границы.
- Supersedes / supersededBy: завершает K-E05-087/K-E05-088/K-E05-092/K-E05-093 и учитывает corrections K-E05-094…K-E05-096.

## K-E05-098

- Дата / фаза: 2026-09-08 / fold completion reproducibility.
- Тип / статус: External replication / Confirmed for public boundary-check commit.
- Утверждение: public commit `9aff2dfe9435db562c6c715fb886b08c04a3156a` воспроизведён в отдельном clean Windows checkout: Modules conformance завершился exit `0` и `PASS conformance checks=362`; diff inspection подтвердила explicit `ImplicitUsings=enable` в generated consumer project.
- Scope: managed conformance и source-level устранение зависимости от parent `Directory.Build.props`; внешний reviewer не запускал полный Linux fold harness.
- Evidence: `dotnet run --project tests/Strogo.Modules.Conformance -c Release -- --report independent-conformance-9aff2df.json` в отдельном checkout exact commit.
- Последствие: два добавленных proof-boundary cases воспроизводятся вне рабочего дерева, а конкретный build-setting defect закрыт в public history; Linux proof outcomes остаются отдельным evidence.
- Supersedes / supersededBy: продолжает K-E05-094/K-E05-096.

## K-E05-099

- Дата / фаза: 2026-09-08 / fold evidence provenance review.
- Тип / статус: Evidence attribution defect / Fixed and full matrix rerun.
- Утверждение: validation v0.1 ошибочно помещал sum owner digest рядом с allocation outcomes и называл hash версионной строки toolchain digest без отдельного content binding. Proof run оставался фактическим, но один summary JSON не позволял однозначно связать allocation с его owner/candidates или отличить identity digest от исходников.
- Scope: conformance-generated manifests, Linux evidence harness/report и retained package; production fold semantics не менялись.
- Evidence: commit `c7d02c674d12532d8a7768857976e65910b58ed4`; validation schema v0.2 содержит разные sum/allocation owner digests, allocation module/proof/source identities, repository revision, compiler/harness source-tree digests и clean flags. Полный strict Linux harness после исправления завершился exit `0`; complete generated trees byte-identical.
- Последствие: frozen proof observation теперь content-bound в пределах явно заданных compiler и harness source sets. `FoldToolchainDigest` внутри production identity остаётся digest версионного identity и в evidence так и называется; content provenance не выводится из него.
- Supersedes / supersededBy: уточняет evidence claims K-E05-092/K-E05-093/K-E05-097.

## K-E05-100

- Дата / фаза: 2026-09-08 / fold evidence reproducibility.
- Тип / статус: External replication / Confirmed for public provenance commit.
- Утверждение: public commit `c7d02c674d12532d8a7768857976e65910b58ed4` воспроизведён в отдельном clean Windows checkout: два Modules runs дали `PASS conformance checks=362`, а полные generated output sets совпали — 20 одинаковых relative paths и SHA-256.
- Scope: conformance generation, allocation manifest и deterministic output tree; внешний reviewer не запускал Linux Dafny matrix и не подтверждал итоговый validation v0.2 runtime report.
- Evidence: два `dotnet run --project tests/Strogo.Modules.Conformance ... --fold-output <separate-dir>` на exact commit; source review подтвердил отдельный allocation owner digest/candidate identities и различение identity/source digests.
- Последствие: provenance fix и полный-tree comparison воспроизводятся вне исходного worktree; semantic proof outcomes остаются связанными с локальным strict Linux run K-E05-099.
- Supersedes / supersededBy: независимо подтверждает generated-evidence часть K-E05-099.

## K-E05-101

- Дата / фаза: 2026-09-08 / external G05 experiment proposal.
- Тип / статус: Experiment-family hypothesis / Proposed publicly; not executed.
- Утверждение: малые stateful protocols/ledgers с `balance>=0` и сохранением суммы могут быть полезным целевым классом G05: внедрённые баги и полная стоимость до корректности проверяют contracts/invariants на сценариях, где состояние существенно. Измерения USD, времени и success rate должны оставаться отдельными; одинаковая semantic information и доступ к oracle обязательны.
- Scope: будущий эксперимент после проверки выразимости stateful protocol в фактическом Strogo; не текущий pure fold, не обещание реализации и не held-out data. Примеры с публичной доски являются открытыми development fixtures.
- Evidence: Posting Board #9810, [thread](https://getpostingboard.dev/b/t/2c88ac5b-38e3-4b14-84e6-ac9231457704); запрошены конкретный protocol и 2–3 injected bugs.
- Последствие: до запуска нужно заранее определить target-class null criterion: проигрыш вне целевого класса не опровергает выигрыш внутри, а отсутствие выигрыша должно иметь фиксированный threshold/decision rule. Stateful scope нельзя молча приписывать текущему языку.
- Supersedes / supersededBy: развивает K-E05-090/K-E05-095; ожидает конкретный fixture и отдельную SPEC.

## K-E05-102

- Дата / фаза: 2026-09-08 / fold public evidence review.
- Тип / статус: External evidence inspection / Confirmed for retained files; no independent Linux rerun.
- Утверждение: внешний участник проверил все 37 записей SHA-256 и 13 retained logs публичного fold package на commit `d9fbc682cadce07b4c49c3b681e2fb21a7bff7d5`: сохранённые outcomes и provenance fields согласованы с manifest. Проверка содержимого опубликованного evidence не является новым proof run или сторонним аудитом toolchain.
- Scope: только tracked `artifacts/e05/fold-v0.1-134856f`; не WSL execution, solver correctness или независимое воспроизведение.
- Evidence: Posting Board #9819, message `66f4ad84-32a2-4c7c-8ba6-615898290495`, [thread](https://getpostingboard.dev/b/t/e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e).
- Последствие: retained package имеет внешнюю проверку внутренней целостности; дальнейший прирост evidence требует фактического Linux rerun либо нового контрпримера.
- Supersedes / supersededBy: дополняет K-E05-097/K-E05-099, не расширяя их runtime claims.

## K-E05-103

- Дата / фаза: 2026-09-08 / G05 experiment economics.
- Тип / статус: Metric clarification / Accepted publicly; not executed.
- Утверждение: USD, elapsed time и success rate должны публиковаться отдельно при одинаковых tariffs, semantic inputs, oracle access и budget. Чтобы не получить survivor bias, дополнительно нужен `total spend / solved count`; при `solved=0` показатель undefined, а не zero. Stop rule и correctness oracle фиксируются до запуска.
- Scope: будущий сравнительный agent experiment; иллюстрация `$10/solution` против `$2/solution` не является измерением Strogo.
- Evidence: Posting Board #9826/#9829, messages `ced309ac-ebd1-4bdf-a6de-76d5b1cac166` и `8181cdcd-9186-403b-83bf-cf0854ee3e1d`, [thread](https://getpostingboard.dev/b/t/2c88ac5b-38e3-4b14-84e6-ac9231457704).
- Последствие: G05 protocol обязан сохранять все failed-attempt costs и задавать обработку zero-solved до вскрытия результатов; конкретный stateful protocol и injected bugs всё ещё не предложены.
- Supersedes / supersededBy: уточняет K-E05-101 и защищает будущий verdict от выборочного учёта успешных попыток.

## K-E06-001

- Дата / фаза: 2026-09-08 / portability EXEC start.
- Тип / статус: Governance and dependency state / Confirmed.
- Утверждение: owner-composite и bounded fold v0.1 dependencies утверждённой E06 SPEC завершены; повторная точная фраза владельца «Спеку подтверждаю» сохраняет EXEC authorization, а прямое поручение разрешает периодический push checkpoint commits.
- Scope: E06 validation-only execution profiles; production admission, merge, release, Wasm/NativeAOT остаются за пределами этого approval.
- Evidence: E06 Approval и action journal; public repository HEAD `d9fbc682cadce07b4c49c3b681e2fb21a7bff7d5` содержит fold completion evidence.
- Последствие: dependency gate E06 снят; изменения выполняются по checkpoints с отдельными commits и push.
- Supersedes / supersededBy: переводит E06 из dependency-gated SPEC в EXEC.

## K-E06-002

- Дата / фаза: 2026-09-08 / portability checkpoint 1.
- Тип / статус: Workload expressivity and oracle / Confirmed locally on Windows x64.
- Утверждение: неизменённый E06 workload из пяти public functions выражается `strogo.module.v0.2` и `strogo.owner-bundle.v0.4`: parser/compiler/owner binder принимают его, 8 owner witnesses replay проходят, а 10 обязательных valid vectors дают одинаковые результаты independent owner model и candidate reference evaluator. Три fixed false-requires inputs возвращают `OwnerPreconditionFailed` до candidate evaluation.
- Scope: target-neutral source/owner/reference semantics; C#/Java translation, Dafny proof, target adapters и platform execution ещё не проверены.
- Evidence: `fixtures/portability-v0.1/{module,owner,vectors,public-api}.json`; retained `artifacts/e06/contract-v0.1/{report.json,sha256.txt}`; `Strogo.Modules.Portability.Conformance` — `PASS portability contract checks=44 valid=10 refusals=3 transport=24 mutations=4`.
- Последствие: workload нельзя считать слишком выразительным для текущего source/owner слоя; следующий барьер находится в proof lowering и target ABI.
- Supersedes / supersededBy: исполняет шаг 2 E06 §13 в пределах source/interface/oracle contract.

## K-E06-003

- Дата / фаза: 2026-09-08 / portability proof-boundary analysis.
- Тип / статус: Implementation gap / Confirmed by source inspection; unresolved at checkpoint 1.
- Утверждение: текущий `ModulesDafnyLowerer.Lower(ModuleIr, OwnerBundleV04)` требует, чтобы каждая owner entry содержала fold, тогда как E06 workload намеренно смешивает один root fold, scalar entries, lazy `if` и `adjust → increment`. Упрощение workload нарушило бы утверждённую SPEC.
- Scope: existing fold lowerer dispatch/binding; это не опровержение выразимости module/owner semantics и не backend mismatch.
- Evidence: `src/Strogo.Modules/DafnyFoldLowering.cs` проверяет `binding.Entries.Any(entry => entry.CandidateFold is null)` и возвращает `FoldOwnerRequired`; E06 fixture имеет fold только в `summarize`.
- Последствие: следующий checkpoint должен обобщить owner-aware lowering на mixed entries, topological call proof и общий verified wrapper, сохранив прежние fold/scalar regressions.
- Supersedes / supersededBy: конкретизирует предусмотренный E06 §14 stop condition; выполнение продолжается через утверждённое additive lowering изменение.

## K-E06-004

- Дата / фаза: 2026-09-08 / external E06 consultation.
- Тип / статус: Public request / Published and read back; awaiting counterexamples.
- Утверждение: опубликован falsifiable запрос на минимальное C#/Java semantic divergence либо ZIP/path-normalization bypass в точных границах frozen E06 workload; публичные примеры заранее классифицированы как development fixtures, а не held-out evidence.
- Scope: Posting Board consultation; публикация не является implementation или validation evidence.
- Evidence: Posting Board #9830, message `89f89172-8404-4764-b0fb-e41e000e9ce4`, exact body 881 UTF-8 bytes, preview request `d14dd972-6fc8-408e-b5ea-afcce080ac74`, [thread](https://getpostingboard.dev/b/t/e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e). Preview подтвердил root/public=true/published=false; POST succeeded; thread read-back подтвердил seq/ID/body.
- Последствие: рациональный конкретный counterexample должен быть сохранён, воспроизведён и учтён до финального portability verdict; отсутствие ответа не считается подтверждением дизайна.
- Supersedes / supersededBy: продолжает внешний review contract E06 SPEC.

## K-E06-005

- Дата / фаза: 2026-09-08 / E06 adversarial fixture design.
- Тип / статус: External test proposal / Accepted as development cases; not executed.
- Утверждение: lazy control flow следует различать парой, где невыбранная ветвь не определена, но backend не должен вычислять её eagerly. `headOrZero([])` уже покрывает sequence-boundary вариант; дополнительный checked-overflow variant полезен как internal lowering test. Вызов target candidate вне owner `requires` остаётся diagnostic negative и не считается semantic counterexample. Raw JAR duplicates после path/case-fold validation должны отклоняться до extraction/map независимо от entry order и равенства bytes.
- Scope: будущие lowering и A9 normalization tests; public E06 ABI и fixed five-function workload не меняются.
- Evidence: Posting Board #9831/#9833, messages `74313173-9bd9-4790-9fe9-1538de996c0e` и `32c2e66b-186c-435e-a299-e3d9d27208e5`, [thread](https://getpostingboard.dev/b/t/e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e); reply preview request `62e9e2b3-abc4-48a9-8116-07c4218bb532`, exact body 816 UTF-8 bytes, published and read back.
- Последствие: добавить overflow-lazy negative/positive pair без новой public function; A9 duplicate fixtures должны переставлять обе записи и пересчитывать outer hashes, но всё равно получать `CanonicalJarRejected`.
- Supersedes / supersededBy: уточняет K-E06-004 и тест-план E06 A7/A9.

## K-E06-006

- Дата / фаза: 2026-09-08 / E06 managed validation runbook.
- Тип / статус: Procedure correction / Confirmed locally.
- Утверждение: для .NET SDK 10 lock-file gate выполняется как `dotnet restore Kernel.slnx --locked-mode`, затем `dotnet build Kernel.slnx --no-restore`; передача `--locked-mode` непосредственно `dotnet build` попадает в MSBuild и завершается `MSB1001: неизвестный ключ`.
- Scope: E06 validation command syntax; package lock content и language semantics не меняются.
- Evidence: локальный exit failure exact command из прежнего §11; исправленный двухшаговый command выполняется перед checkpoint commit.
- Последствие: все последующие E06 harnesses должны разделять locked restore и no-restore build, чтобы ошибка команды не была принята за дефект проекта.
- Supersedes / supersededBy: исправляет пример команд E06 §11.

## K-E06-007

- Дата / фаза: 2026-09-08 / checkpoint 1 regression execution.
- Тип / статус: Validation instability / Qualified.
- Утверждение: одновременный запуск четырёх managed conformance executables дал `Kernel.Graph.Conformance` отказ `ProofNotEstablished` в начальном CLI compile; немедленный отдельный последовательный запуск того же binary/revision завершился `PASS all: 141`. Причина не локализована; resource/solver contention является гипотезой, а не подтверждённым диагнозом.
- Scope: orchestration тестов на текущем Windows host; portability contract и Graph implementation не имеют project reference друг на друга.
- Evidence: первый parallel run — Graph exit `1`, затем sequential `dotnet run --project tests/Kernel.Graph.Conformance -c Release --no-build` — exit `0`, `PASS all: 141`.
- Последствие: checkpoint evidence перечисляет initial failure и sequential pass; полные solver-backed suites далее запускаются последовательно, пока отдельный experiment не подтвердит безопасность parallel orchestration.
- Supersedes / supersededBy: ограничивает validation claim checkpoint 1; не опровергает прежний TaskGraph baseline.

## K-E06-008

- Дата / фаза: 2026-09-08 / checkpoint 1 external reproduction.
- Тип / статус: External replication / Confirmed for public commit `c240758`.
- Утверждение: public checkpoint `c240758cb97403ecbb7b9820a11c68a9c43ce4d4` воспроизведён в отдельном Windows checkout: `Strogo.Modules.Portability.Conformance` завершился exit `0` и `PASS ... checks=44 valid=10 refusals=3 transport=24 mutations=4`.
- Scope: binding, owner/reference oracle и frozen contract metadata на exact commit; не Dafny proof, C#/Java translation или platform matrix.
- Evidence: внешний coordinated run с report `artifacts/local-validation/e06/contract-v0.1.json` в isolated checkout; сообщение доставлено после public push.
- Последствие: checkpoint 1 воспроизводим вне исходного рабочего дерева; последующее усиление invariant меняет module/proof identity и требует нового exact-commit run.
- Supersedes / supersededBy: независимо подтверждает K-E06-002 для commit `c240758`.

## K-E06-009

- Дата / фаза: 2026-09-08 / mixed-entry proof experiment.
- Тип / статус: Invariant-strength hypothesis / Refuted for weak variant; confirmed for strengthened variant.
- Утверждение: истинная для полного результата граница `-8000000 <= sum <= 8000000` недостаточна как inductive fold invariant: Dafny не может сохранить ту же границу после шага из предположений `sum<=8000000` и `item<=1000000`. Инвариант с границей, зависящей от `prefixLength` (`±1000000*n`, выраженной закрытым каскадом для `n=0..8`), сохраняется и доказывает тот же workload.
- Scope: frozen summarize semantics, sequence capacity 8 и item range ±1000000; вывод не означает, что всем функциям нужен сложный invariant.
- Evidence: Ubuntu 24.04 WSL2, Dafny 4.11.0 executable SHA-256 `e540b482…93846d`: weak source — exit `4`, `31 verified, 1 error` на owner-prefix postcondition; strengthened source — exit `0`, `32 verified, 0 errors`. Windows strengthened source также дал `32/0`; Windows weak diagnostic имеет известный `Model parsing error` и не используется как strict negative oracle.
- Последствие: «обязательное описание всех инвариантов» должно требовать machine-checked inductiveness/sufficiency, а не только наличие истинной глобальной формулы. Weak variant сохранён отдельным development fixture.
- Supersedes / supersededBy: конкретизирует исходный вопрос владельца об обязательных инвариантах и развивает K-E06-003.

## K-E06-010

- Дата / фаза: 2026-09-08 / mixed owner lowering implementation.
- Тип / статус: Proof implementation / Confirmed on working tree; exact-commit evidence pending.
- Утверждение: additive lowering `strogo.owner-dafny-lowering.v0.5` сохраняет прежний byte path для all-fold bundles, а mixed bundle генерирует owner predicates/models для scalar entries, prefix proof для fold entry, candidate methods и явную `call-contract` obligation для `adjust → increment`. Два clean generation roots дали одинаковые full output sets; два strong proof replay дали `32/0`.
- Scope: owner-aware Dafny source и proof obligations; total wire wrapper, target adapters, translation/package и runtime matrix ещё не реализованы.
- Evidence: `tools/Test-Modules-Portability-Proof.sh`; pre-commit WSL run `/tmp/strogo-e06-proof-695708bb13b1434fba0fa5890e34da31` — `PASS portability mixed proof strong=32/0x2 weak=31/1`; conformance `checks=51`.
- Последствие: после implementation commit нужен clean exact-revision rerun и filtered public proof package; затем можно переводить этот source в C#/.NET profile.
- Supersedes / supersededBy: устраняет `FoldOwnerRequired` gap K-E06-003 для утверждённого mixed workload.

## K-E06-011

- Дата / фаза: 2026-09-08 / mixed lowering external reproduction.
- Тип / статус: External replication / Confirmed for public commit `6c73dee`.
- Утверждение: exact public commit `6c73dee77e41aa49d873e4642fbb08f4228bfe84` воспроизведён в отдельном Windows checkout: portability conformance завершился `PASS 51`, Modules regression — `PASS 362`.
- Scope: managed generation/binding/oracle/reference regressions; внешний участник не запускал Linux proof driver или C#/Java targets.
- Evidence: commands `dotnet run --project tests/Strogo.Modules.Portability.Conformance -c Release -- --report independent-portability-6c73dee.json` и `dotnet run --project tests/Strogo.Modules.Conformance -c Release -- --report independent-modules-6c73dee.json`, оба exit `0`.
- Последствие: mixed dispatcher и strengthened fixture воспроизводятся вне исходного checkout на managed boundary; proof evidence остаётся отдельным gate.
- Supersedes / supersededBy: независимо подтверждает managed часть K-E06-010.

## K-E06-012

- Дата / фаза: 2026-09-08 / Linux proof-driver review.
- Тип / статус: Reproduction harness defect / Fixed before retained evidence.
- Утверждение: version probes в первой версии driver использовали command substitution без кавычек вокруг executable variable; абсолютный путь с пробелами прошёл бы `-x`, но разбился бы при `--version`. Основные proof invocations уже заключали переменные в кавычки.
- Scope: `tools/Test-Modules-Portability-Proof.sh` prerequisite checks; observed no-space local run не меняется.
- Evidence: внешний source review public commit `6c73dee`; исправлено на `"$("$dafny" --version)"` и `"$("$dotnet" --version)"`.
- Последствие: retained exact-commit evidence создаётся только после повторного запуска исправленного driver; version checks поддерживают произвольные абсолютные пути.
- Supersedes / supersededBy: hardening для K-E06-010.

## K-E06-013

- Дата / фаза: 2026-09-08 / exact-revision mixed proof retention.
- Тип / статус: Reproducible proof evidence / Confirmed on clean public commit `99415e078777bbee76e35a54a0852080c46eec00`.
- Утверждение: исправленный Linux driver на чистом exact revision получил побайтно одинаковые полные output sets из двух независимых generation roots. Оба strong replay завершились `32 verified, 0 errors`; намеренно слабый invariant завершился ожидаемым exit `4`, `31 verified, 1 error`, причём source map связал diagnostic с obligation `owner-prefix-invariant`.
- Scope: mixed owner-aware Dafny source, obligations и invariant counterexample на Ubuntu 24.04 WSL2 x86_64. Total wire wrapper, C#/Java translation, packages и четыре runtime rows ещё не подтверждены.
- Evidence: `artifacts/e06/mixed-proof-99415e0/**`; report фиксирует clean revision, proof identity `077e63eae9b6734bdbaa80877c12636815a42e61409a3a369b10e16f14d0065b`, Dafny `4.11.0+fcb2042d6d043a2634f0854338c08feeaaaf4ae2` с executable SHA-256 `e540b482…93846d`, .NET SDK `10.0.400`; `sha256.txt` покрывает retained files.
- Последствие: mixed lowering checkpoint имеет воспроизводимый retained proof package; следующий E06 gate — verified total wire wrapper и .NET adapter, а не повторение proof run без нового риска.
- Supersedes / supersededBy: завершает exact-commit evidence, оставшееся pending в K-E06-010.

## K-E06-014

- Дата / фаза: 2026-09-08 / external capability-boundary example.
- Тип / статус: External report / Unverified participant claim; accepted as design input.
- Утверждение: участник сообщил, что Botpub выдаёт stable post IDs, но не предоставляет idempotency key или lookup операции; поэтому потерянный HTTP response оставляет исход конкретной попытки `UNKNOWN`, а повторный POST может создать дубль. Read-back по совпадению текста и локальный outbox сами по себе не доказывают exactly-once.
- Scope: внешний сервис не проверялся нами; это контрпример для будущего capability adapter и recovery contract, не результат E06 pure workload.
- Evidence: Posting Board #9851, [thread](https://getpostingboard.dev/b/t/2c88ac5b-38e3-4b14-84e6-ac9231457704); сообщение внешнего участника сохранено как provenance, без приписывания независимой проверки.
- Последствие: будущий effectful profile не должен обещать exactly-once без server-side idempotency/operation lookup либо явно доказанного эквивалентного протокола; ambiguous completion должен быть отдельным typed outcome.
- Supersedes / supersededBy: развивает общий capability/effect boundary; не меняет утверждённый E06 scope без effects.

## K-E06-015

- Дата / фаза: 2026-09-08 / retained proof external inspection.
- Тип / статус: External evidence inspection / Confirmed for public retained files; no independent Linux rerun.
- Утверждение: внешний reviewer пересчитал все 13 записей `sha256.txt` в опубликованном package `mixed-proof-99415e0`, подтвердил exact strong `32/0` в двух logs, weak `31/1` с related location строки 93 (`owner-prefix-invariant`) и отсутствие известных tool/model/prover failure markers. Report отдельно содержит clean revision и module/owner/source/proof identities.
- Scope: целостность и внутренняя согласованность retained evidence commit `0b8536d`; это не независимый solver run и не .NET/Java execution.
- Evidence: coordinated external review после push `0b8536d`; публичные tracked files `artifacts/e06/mixed-proof-99415e0/**`.
- Последствие: замечание с version-probe quoting закрыто и опубликованный package проверен вторым читателем; дальнейший proof evidence должен относиться к изменённому wire source.
- Supersedes / supersededBy: дополняет K-E06-012/K-E06-013 без расширения их runtime claims.

## K-E06-016

- Дата / фаза: 2026-09-08 / future delivery capability contract.
- Тип / статус: External design proposal and refinement / Not implemented; outside current E06.
- Утверждение: предложенное перечисление `NONE | CLIENT_KEY_DEDUP | LOOKUP` недостаточно без разделения deduplication и lookup как независимых свойств. Lookup для recovery должен принимать известный до отправки client key; контракт также обязан фиксировать retention, scope ключа и conflict при повторе ключа с другим payload. Истечение retention не превращает `UNKNOWN_EFFECT` в известный исход.
- Scope: будущий effectful capability adapter; pure E06 workload и текущий ABI не меняются.
- Evidence: Posting Board #9863/#9864, [thread](https://getpostingboard.dev/b/t/2c88ac5b-38e3-4b14-84e6-ac9231457704); это обсуждение дизайна, а не реализованная гарантия Strogo.
- Последствие: будущую SPEC следует строить из независимых typed capability fields и explicit ambiguous outcome; необходимо искать контрпример к этой модели до approval.
- Supersedes / supersededBy: конкретизирует K-E06-014.

## K-E06-017

- Дата / фаза: 2026-09-08 / verified wire-wrapper implementation.
- Тип / статус: Proof-boundary implementation / Confirmed on working tree; exact clean-revision retention pending.
- Утверждение: additive lowering `strogo.portable-wire-dafny-lowering.v0.1` добавляет к одному mixed candidate source закрытые `WireValue`, `WireOutcome` и total `Invoke` с `requires true`. Wrapper применяет logical refusal priority `UnknownFunction → ArityMismatch → RuntimeTypeMismatch → OwnerPreconditionFailed`, проверяет I64 range, sequence capacity/items и exact Summary field order, а каждую из пяти success-веток связывает явным assertion с owner model/prefix model после доказанного type/requires gate.
- Scope: generic wire datatype и fixed E06 five-function wrapper; JSON parsing/transport refusals остаются обязанностью target adapter и ещё не реализованы.
- Evidence: `src/Strogo.Modules/DafnyPortableWireLowering.cs`; two-root working-tree driver PASS `strong=42/0x2 weak=41/1`, 25 mapped proof obligations; portability conformance `checks=56`.
- Последствие: logical validation больше не нужно дублировать в target adapters; после exact-commit proof можно собирать C# adapter только как JSON ↔ generated wire transport.
- Supersedes / supersededBy: выполняет verified-wrapper часть E06 §§6.1,6.2.3 и развивает K-E06-013.

## K-E06-018

- Дата / фаза: 2026-09-08 / C# translator feasibility.
- Тип / статус: Toolchain boundary / Confirmed locally; target profile not yet built.
- Утверждение: Dafny 4.11.0 успешно переводит новый verified source с wire wrapper через `translate cs --no-verify --enforce-determinism --include-runtime`; полученный C# вызывает wrapper и возвращает `increment(41)=42` на .NET 10. Строгая сборка с `Nullable=enable` отвергает upstream Dafny runtime множеством nullable diagnostics; фиксированная generated-source policy `Nullable=disable`, `TreatWarningsAsErrors=true`, `NoWarn=CS8981` собирается без остальных warnings. `CS8981` относится к созданному Dafny lowercase type `nat`.
- Scope: временный Windows translation/build/run; byte-identical package, public `ModuleApi`, Linux row и target adapter отсутствуют.
- Evidence: local ignored `artifacts/local-validation/e06/csharp-translate-working-tree`; first strict build failure и successful isolated generated wrapper invocation.
- Последствие: .NET builder должен разделить policies upstream generated source и нашего adapter, закрепить единственное исключение `CS8981` в toolchain inventory и отвергать любой другой warning.
- Supersedes / supersededBy: уточняет E06 target-build contract без изменения logical ABI.

## K-E06-019

- Дата / фаза: 2026-09-08 / future ambiguous-delivery adversarial case.
- Тип / статус: External test proposal / Not executed; outside current E06.
- Утверждение: даже корректный lookup outcome `ABSENT_IN_WINDOW` не исключает будущий commit уже отправленного, но задержанного запроса. Различающий тест: задержать первую попытку до commit, получить `ABSENT`, отправить retry, затем отпустить первую попытку и посчитать эффекты. Без атомарной дедупликации конкурирующих попыток либо terminal fence против позднего commit возможно два эффекта; payload digest сам по себе дубль не запрещает.
- Scope: будущий effectful adapter/sink protocol; не pure E06 runtime.
- Evidence: Posting Board #9865/#9866, [thread](https://getpostingboard.dev/b/t/2c88ac5b-38e3-4b14-84e6-ac9231457704); предложенный сценарий ещё не воспроизведён.
- Последствие: будущая SPEC должна включить race fixture, closed lookup outcomes `FOUND | ABSENT_IN_WINDOW | EXPIRED` и доказуемое правило retry/fence, сохраняя `UNKNOWN_EFFECT` там, где certainty недостижима.
- Supersedes / supersededBy: усиливает K-E06-014/K-E06-016 конкретным concurrency counterexample.

## K-E06-020

- Дата / фаза: 2026-09-08 / exact-revision wire proof retention.
- Тип / статус: Reproducible proof evidence / Confirmed on clean public commit `9f0b2e39e9a09d778b107287ed8ae1656c5ad1b8`.
- Утверждение: exact clean revision с total wire wrapper воспроизведён двумя independent generation roots: все generated files byte-equal; оба strong replay дали `42 verified, 0 errors`; weak invariant сохранил ровно один ожидаемый failure `41/1`, связанный source map с `owner-prefix-invariant`. Report фиксирует 25 obligations и новый wire source/proof identity.
- Scope: общий Dafny candidate + verified logical wire wrapper на Ubuntu 24.04 WSL2 x86_64; JSON adapter, package и runtime matrix остаются следующими gates.
- Evidence: `artifacts/e06/wire-proof-9f0b2e3/**`; `repositoryDirty=false`, source digest `1a6076f89ec5d233df9eeaf6b472d11c8a10407e9e8b5ceb09bc4b1fbda3bdcf`, proof identity `09c1d907c9ca9dcd6f0b3ce901c1c72cde2dab12141e21c39b2aac93839b9c74`; checked 13-entry `sha256.txt`.
- Последствие: K-E06-017 exact-commit gate закрыт; этот source становится единственным входом обоих target translators.
- Supersedes / supersededBy: завершает pending exact retention K-E06-017.

## K-E06-021

- Дата / фаза: 2026-09-08 / wire checkpoint external reproduction.
- Тип / статус: External replication and source review / Confirmed for public commit `9f0b2e3`; no independent Dafny run.
- Утверждение: отдельный checkout выполнил portability conformance с exit `0`, `checks=56 valid=10 refusals=3 transport=24 mutations=4`; reviewer также подтвердил по source, что function/arity/type/owner-domain checks расположены до generated candidate calls.
- Scope: managed conformance и source order; не `42/0` proof и не .NET/Java profile execution.
- Evidence: coordinated external run на `9f0b2e39e9a09d778b107287ed8ae1656c5ad1b8`, report `independent-portability-9f0b2e3.json` во внешнем checkout.
- Последствие: wrapper generation и boundary checks воспроизводятся вне рабочего дерева; target translator/runtime остаётся отдельным TCB и validation gate.
- Supersedes / supersededBy: независимо подтверждает managed часть K-E06-017 и ограничивает claim K-E06-020.

## K-E06-022

- Дата / фаза: 2026-09-08 / deterministic C# translation experiment.
- Тип / статус: Reproducible-build hypothesis / Refuted for absolute invocation; confirmed for relative isolated invocation.
- Утверждение: два вызова Dafny `translate cs` с разными абсолютными `--output`/translation-record paths создали разные `Candidate.cs` и DLL, потому что Dafny встраивает полную command line в `DafnySourceAttribute`. Когда каждый lane получает одинаковые relative inputs и запускает exact command из собственного working directory (`candidate.dfy`, `Candidate.cs`, `translation-record.dtr`), translated source, translation record и deterministic DLL становятся byte-equal. Roslyn `PathMap=<lane>=/_/`, disabled debug symbols и fixed project properties устраняют оставшуюся physical build-root identity.
- Scope: pinned Dafny 4.11.0 и .NET SDK 10.0.400; это не общее обещание для других translator versions.
- Evidence: initial local prototype дал разные DLL SHA-256 `c82f1f…b0a3`/`a7332f…106`; corrected two-lane prototype дал одинаковые generated source `789337…344e`, translation record `d4fe37…8ced` и DLL.
- Последствие: build harness обязан копировать exact proof source в isolated lane и вызывать translator только относительными logical paths; физические пути остаются diagnostic receipt и не входят в artifact identity.
- Supersedes / supersededBy: реализует physical-path exclusion E06 §6.2.2 для C# lane.

## K-E06-023

- Дата / фаза: 2026-09-08 / first .NET target execution.
- Тип / статус: Implementation and platform evidence / Confirmed on working tree; exact clean-commit evidence pending.
- Утверждение: transport-only C# `ModuleApi.Invoke(string)` собирается вместе с exact verified Dafny source в byte-equal `net10.0` DLL из двух clean Linux lanes. Одна DLL с SHA-256 `f890f8a5e550ec2ed288f5f3b1bc47227af78f12608f008dccd990b42e339020` фактически прошла standalone consumer в Linux WSL2 и Windows x64: 8 exact success/refusal cases и все 24 frozen transport boundary IDs.
- Scope: public adapter, deterministic library и two-OS execution на текущем working tree. Portability manifest/package closure, runtime closure digests, full 10-vector oracle report, mutations и exact-commit retention ещё не выполнены, поэтому profile пока не получает итоговый `Portable`.
- Evidence: `targets/dotnet-managed-v1/**`, `tools/Build-PortableDotNet.sh`, `tools/Test-PortableDotNet-Windows.ps1`, `tests/fixtures/portability-consumers/csharp/**`; ignored runs `dotnet-build-3` и `windows-run-1`.
- Последствие: гипотеза о переносимости одной managed DLL получила первый фактический положительный результат на двух ОС; checkpoint нужно закоммитить, повторить на exact revision и упаковать filtered evidence до расширения claim.
- Supersedes / supersededBy: начинает E06 plan step 3 и развивает feasibility K-E06-018.

## K-E06-024

- Дата / фаза: 2026-09-08 / escaped-surrogate adversarial review.
- Тип / статус: Totality hypothesis / Refuted on `33c106b`; fixed and reproduced on working tree.
- Утверждение: проверка surrogate code units только в исходном target string не покрывает ASCII escape `"\\uD800"`: `Utf8JsonReader` может принять токен, после чего декодирование через `GetString()` бросает `InvalidOperationException`. Публичный `ModuleApi.Invoke` на `33c106b` поэтому не был total для всего bounded JSON input. Исправление заставляет syntax pass декодировать каждое string/property token и переводит incomplete decoded UTF-16 в `MalformedJson`; document validation имеет такой же fail-closed guard.
- Scope: C# adapter JSON decoding; verified logical wrapper и raw lone-surrogate priority `InvalidUnicode` не меняются.
- Evidence: внешний source finding + независимое BCL reproduction; новый case `escaped-lone-surrogate` выполнен сверх frozen 24 cases в Linux и Windows на одной working-tree DLL, оба PASS `additional=1`.
- Последствие: evidence commit `33c106b` не публикуется как successful profile checkpoint; после fix commit нужен новый exact clean build/run. Для Java adapter тот же escaped-surrogate case обязателен заранее.
- Supersedes / supersededBy: ограничивает K-E06-023 и добавляет различающий test к K-E06-005.

## K-E06-025

- Дата / фаза: 2026-09-08 / exact-revision .NET checkpoint.
- Тип / статус: Reproducible target execution / Confirmed on clean public commit `405bd0b9eca73b0716eb0b316e054187a93ee85b`; profile completion still open.
- Утверждение: два clean relative Dafny C# translation/build lanes exact revision создали byte-equal translated source, translation record и `net10.0` DLL. Одна DLL SHA-256 `b62e3742ba9deb633be81a07d12811aebc8538c45dba5f93b37fabbc5e55667c` прошла без пересборки на Linux x64 (.NET 10.0.11 в Ubuntu 24.04 WSL2) и Windows x64 (.NET 10.0.11): 8 exact public outcomes, frozen 24 transport IDs и escaped-surrogate additional case.
- Scope: deterministic adapter artifact и фактические two-OS consumer runs. Это ещё не итоговый `Portable`: отсутствуют canonical portability manifest/package, runtime closure digests, все 10 owner vectors, четыре mutations и performance/JIT diagnostics.
- Evidence: `artifacts/e06/dotnet-checkpoint-405bd0b/**`; build report фиксирует `repositoryDirty=false`, shared verified source digest `1a6076…bdcf`, adapter digest `0a5bc2…97e1`, byte-equal lanes; Linux/Windows receipts имеют одинаковый artifact digest; `sha256.txt` покрывает 11 retained files.
- Последствие: практическая цель «один managed artifact запускается на разных платформах силами зрелого runtime» подтверждена для ограниченного checkpoint; следующий шаг должен закрыть manifest/oracle/mutation gates, не повторять уже доказанную basic portability.
- Supersedes / supersededBy: завершает exact-commit часть K-E06-023/K-E06-024 и оставляет финальные A-checks открытыми.

## K-E06-026

- Дата / фаза: 2026-09-08 / escaped-surrogate fix external review.
- Тип / статус: External source inspection / Confirmed for public commit `405bd0b`; no independent rebuilt DLL run.
- Утверждение: reviewer подтвердил по source, что `reader.GetString()` для каждого `String`/`PropertyName` в bounded syntax pass закрывает ранее найденный exception path до `CanonicalRequestBytes`, а дополнительный consumer case ожидает `MalformedJson`.
- Scope: адресность source fix; Windows/Linux PASS остаётся нашим exact run, не внешней runtime репликацией.
- Evidence: coordinated external review commit `405bd0b9eca73b0716eb0b316e054187a93ee85b` после push.
- Последствие: найденный review defect закрыт двумя линиями evidence — external source inspection и our two-platform execution — с раздельно указанной независимостью.
- Supersedes / supersededBy: дополняет K-E06-024/K-E06-025.

## K-E06-027

- Дата / фаза: 2026-09-08 / standalone consumer isolation review.
- Тип / статус: Harness independence hypothesis / Refuted on `74e002b`; fixed on working tree.
- Утверждение: `Consumer.csproj` не задавал `ImplicitUsings` и успешно собирался внутри repository только благодаря inherited root build settings. Независимый Windows driver с `RunDirectory` вне repo воспроизвёл `CS0246/CS0103`; единственный override `-p:ImplicitUsings=enable` дал exit `0` и PASS на опубликованной DLL. Consumer теперь явно закрепляет это свойство в собственном project file.
- Scope: standalone C# consumer build isolation; DLL behavior и adapter bytes не меняются.
- Evidence: внешний exact run `tools/Test-PortableDotNet-Windows.ps1@74e002b` во временном каталоге вне repo; failing baseline и passing single-property rerun.
- Последствие: будущие consumer claims обязаны запускаться вне inherited repository hierarchy или проверять self-contained MSBuild properties; exact checkpoint evidence требует повторного harness run после fix commit.
- Supersedes / supersededBy: ограничивает A14-часть K-E06-025, не опровергая independent DLL execution при исправленном build input.

## K-E06-028

- Дата / фаза: 2026-09-08 / complete owner-vector projection.
- Тип / статус: Target oracle execution / Confirmed on working tree; exact clean-commit rerun pending.
- Утверждение: детерминированная JSONL projection переводит frozen target-neutral fixture в 10 canonical success requests/outcomes и 3 owner refusals. Standalone consumer, имеющий ссылку только на опубликованную DLL, получил exact byte-equal outcomes для всех 13 строк как в Linux, так и в Windows.
- Scope: все обязательные owner vectors E06; target mutations, manifest/package и performance/JIT ещё открыты.
- Evidence: `tools/Generate-Portability-InvokeVectors.py`, `fixtures/portability-v0.1/invoke-vectors.jsonl`, расширенный standalone consumer; working-tree Linux/Windows runs одной DLL `b62e37…667c`.
- Последствие: после exact revision run .NET profile закроет полный functional oracle set; генератор и JSONL должны быть проверены conformance на соответствие исходному fixture, чтобы projection не стала независимым недоверенным oracle.
- Supersede / supersededBy: расширяет K-E06-025 с subset 8 до всех 10+3 owner outcomes.

## K-E06-029

- Дата / фаза: 2026-09-08 / standalone consumer external rerun.
- Тип / статус: External replication / Confirmed for public harness `eb77d47` and retained DLL `b62e37…667c`.
- Утверждение: штатный Windows driver без `ImplicitUsings` override и с `RunDirectory` вне repository завершился exit `0`: 8 consumer cases, 24 frozen transport cases, escaped-surrogate case и все 13 invoke vectors прошли на опубликованной DLL.
- Scope: Windows execution существующей DLL и standalone MSBuild isolation; не independent rebuild, Linux или Java.
- Evidence: coordinated external run `tools/Test-PortableDotNet-Windows.ps1@eb77d4767f215732becee334b9842336a0d8878e`, artifact `artifacts/e06/dotnet-checkpoint-405bd0b/strogo.portable.v01.dll`.
- Последствие: consumer inheritance defect K-E06-027 закрыт независимым фактическим rerun; build identity остаётся отдельной проверкой.
- Supersedes / supersededBy: завершает K-E06-027 и внешне подтверждает Windows часть K-E06-028.

## K-E06-030

- Дата / фаза: 2026-09-08 / cross-revision artifact identity.
- Тип / статус: Reproducible-build identity / Refuted for SDK default; fixed on working tree.
- Утверждение: при неизменных verified source, translated source, adapter и project bytes SDK добавлял Git revision в `AssemblyInformationalVersion`: DLL на `405bd0b` имела product version `1.0.0+405bd0b…`, а на `eb77d47` — `1.0.0+eb77d47…`, поэтому artifact digests различались. Target project теперь фиксирует `Version=0.1.0`, assembly/file/informational versions и отключает `IncludeSourceRevisionInInformationalVersion`.
- Scope: .NET artifact identity между commits с нерелевантными harness/docs changes; two-root same-revision determinism уже проходил.
- Evidence: exact build reports при одинаковых source/adapter digests; `FileVersionInfo.ProductVersion` двух retained/local DLL.
- Последствие: artifact bytes больше не должны зависеть от repository HEAD, не входящего в target semantic/build inputs; после commit нужен exact two-lane build и будущий no-op revision check.
- Supersedes / supersededBy: усиливает K-E06-022 и ограничивает cross-revision interpretation K-E06-025.

## K-E06-031

- Дата / фаза: 2026-09-08 / full-vector exact .NET run.
- Тип / статус: Reproducible target execution / Confirmed on clean public commit `30cffa8`; cross-revision comparison pending.
- Утверждение: exact clean build с pinned assembly metadata снова дал byte-equal translation/record/DLL в двух roots. DLL SHA-256 `37cb02dc8be37fe005d6515421aaba97b069168e70f52d9e8ba2ab019fe314fc`, ProductVersion `0.1.0`, прошла Linux и Windows: 8 public consumer cases, все 24 frozen transport cases, escaped-surrogate case и 13 exact owner-vector outcomes. JSONL projection перед build заново создана из source fixture и побайтно совпала с tracked bytes.
- Scope: .NET artifact functional/oracle and two-OS checkpoint; mutations, canonical portability manifest/package, runtime closure digests и performance/JIT остаются открыты.
- Evidence: ignored exact run `artifacts/local-validation/e06/dotnet-build-30cffa8`; Windows consumer запускался вне repository hierarchy.
- Последствие: functional owner/vector gate .NET profile закрыт; docs-only следующий commit создаёт различающую возможность проверить, что Git HEAD действительно больше не меняет DLL bytes.
- Supersedes / supersededBy: exact-commit подтверждение K-E06-028/K-E06-030, кроме cross-revision части.

## K-E06-032

- Дата / фаза: 2026-09-08 / future recovery-plan proposal.
- Тип / статус: External design proposal / Not implemented; outside current E06.
- Утверждение: предложен closed recovery plan: `RETRY` для идемпотентного чтения, `RETRY_SAME_KEY` при живой sink deduplication, `QUARANTINE_UNKNOWN`/reconciliation для неповторимого эффекта и `RETRY_RISK_ACCEPTED` только по заранее утверждённой политике. Рестарт клиента не превращает `UNKNOWN` в разрешение второго эффекта; компенсация является новой операцией с новым ID и отдельной авторизацией.
- Scope: будущий effectful language/capability contract; не текущий pure E06.
- Evidence: Posting Board #9870, [thread](https://getpostingboard.dev/b/t/2c88ac5b-38e3-4b14-84e6-ac9231457704); предложение участника, не исполняемый результат.
- Последствие: future SPEC должна типизировать recovery decision отдельно от delivery observation и запретить implicit retry после process restart; terminal fence/race fixture K-E06-019 остаётся обязательным.
- Supersedes / supersededBy: развивает K-E06-016/K-E06-019.

## K-E06-033

- Дата / фаза: 2026-09-08 / assembly identity external review.
- Тип / статус: External source inspection / Fix shape confirmed; empirical cross-revision proof still required.
- Утверждение: reviewer подтвердил, что fixed version fields и `IncludeSourceRevisionInInformationalVersion=false` адресуют найденный Git-SHA suffix, но два roots одного HEAD не доказывают стабильность между revisions. Нужна пара builds при неизменных candidate/adapter/project bytes и разных HEAD.
- Scope: build evidence design; не новый target run.
- Evidence: coordinated review public commit `30cffa8`.
- Последствие: следующий docs-only commit используется как controlled no-op revision для повторной сборки и сравнения exact DLL SHA-256.
- Supersedes / supersededBy: уточняет незакрытую часть K-E06-030/K-E06-031.

## K-E06-034

- Дата / фаза: 2026-09-08 / controlled cross-revision .NET build.
- Тип / статус: Artifact identity / Confirmed across clean revisions `30cffa8` and `ce291ca`.
- Утверждение: после fixed assembly metadata два clean builds при разных repository HEAD, но одинаковых verified source, adapter, project, translated source и translation-record digests, создали byte-equal DLL SHA-256 `37cb02dc8be37fe005d6515421aaba97b069168e70f52d9e8ba2ab019fe314fc`. На втором revision та же DLL повторно прошла Linux и Windows full consumer: 8 public cases, 24 transport, 1 escaped-surrogate и 13 owner vectors.
- Scope: cross-revision content identity и functional two-platform evidence. Не закрывает mutations, canonical portability package/manifest, runtime closure digests или performance/JIT.
- Evidence: `artifacts/e06/dotnet-full-vectors-ce291ca/**`; two build reports, explicit cross-revision report, Linux/Windows receipts/logs, DLL и 13-entry SHA manifest.
- Последствие: найденные physical path, Git revision, escaped Unicode и inherited MSBuild sources of nondeterminism/false confidence закрыты для текущего .NET checkpoint; следующий различающий gate — mutations либо canonical package binding.
- Supersedes / supersededBy: завершает K-E06-030/K-E06-033 и заменяет более узкий artifact checkpoint K-E06-025 для дальнейших .NET claims.

## K-E06-035

- Дата / фаза: 2026-09-08 / .NET target mutation harness.
- Тип / статус: Validation-harness insight / Confirmed and fixed on working tree; exact clean-commit rerun pending.
- Утверждение: мутация eager branch, записанная как literal `false`, была отвергнута компилятором из-за `CS0162` при warnings-as-errors и потому не создавала требуемый runtime-различитель. После замены на runtime-условие, ложное для любой допустимой длины, мутант собирается и обращение к element `0` пустой sequence даёт `System.IndexOutOfRangeException`. Отдельно найден ложноположительный путь probe: общий `catch` мог принять собственное исключение `mutation survived comparison` за target crash. Probe теперь ловит только исключение самого `ModuleApi.Invoke`, четыре negative controls требуют, чтобы неизменённая DLL не считалась мутантом, а каждый мутант проверяется по exact changed outcome либо exact exception type и получает vector/locus с нормативной классификацией A7.
- Scope: четыре обязательные .NET target mutations E06 §6.2.5 и корректность mutation probe; это не manifest/package, runtime closure, Java или performance evidence.
- Evidence: `tools/Apply-PortableDotNet-Mutation.py`, `tools/Test-PortableDotNet-Mutations.sh`, standalone mutation consumer; ignored working-tree run `artifacts/local-validation/e06/dotnet-mutations-working-tree-4` дал baseline `4`, unchanged-target rejections `4`, compiled mutants `4`, detected mutants `4`.
- Последствие: mutation gate можно засчитать только после clean-commit rerun; build rejection не подменяет ожидаемый runtime mismatch/crash, а self-test неизменённого target является обязательной защитой от ложного `Detected`.
- Supersedes / supersededBy: развивает K-E06-034 и закрывает design-часть target mutations; exact execution ещё должен заменить working-tree evidence.

## K-E06-036

- Дата / фаза: 2026-09-08 / exact .NET target mutation execution.
- Тип / статус: Target mutation discrimination / Confirmed on clean public commit `590d3dd`.
- Утверждение: baseline DLL SHA-256 `37cb02dc8be37fe005d6515421aaba97b069168e70f52d9e8ba2ab019fe314fc` снова прошла full consumer и четыре mutation probes; четыре negative controls подтвердили, что неизменённая DLL не классифицируется как mutant. Четыре отдельные DLL после exact single-anchor mutations собрались с нулём warnings/errors. Sign, reverse и refusal-code дали exact `BackendSemanticMismatch` на `summarize-mixed`/sum, `summarize-mixed`/echo item 0 и `i64-leading-zero`/code; eager head дал exact `TargetExecutionFailed` на `head-empty`/`function/headOrZero/result` с `System.IndexOutOfRangeException`.
- Scope: .NET-половина A7 на Linux x64 lane; JVM mutation run, остальные платформенные A-checks и общая гарантия отсутствия дефектов не входят.
- Evidence: `artifacts/e06/dotnet-mutations-590d3dd/**`; clean report `repositoryDirty:false`, baseline `4`, unchanged-target rejections `4`, compiled/detected mutations `4/4`, exact per-mutation source/artifact digests.
- Последствие: известные ошибки знака, порядка, lazy branch и adapter refusal не проходят текущий .NET oracle незаметно; следующий различающий .NET gate — canonical validation manifest/package binding либо runtime closure/unavailable cases.
- Supersedes / supersededBy: заменяет working-tree evidence K-E06-035 точным публичным запуском; весь A7 остаётся открыт до JVM-половины.

## K-E06-037

- Дата / фаза: 2026-09-08 / canonical portability package implementation.
- Тип / статус: Package identity and tree validation / Confirmed on working tree with synthetic entry artifact; exact real-DLL package pending.
- Утверждение: внутренне самосогласованный manifest не является достаточной load identity: изменённый adapter можно упаковать с корректно пересчитанными file/artifact/package/manifest digests. Поэтому публичный `Validate` теперь обязательно принимает ожидаемый `portabilityManifestDigest` из доверенной report/matrix связи и отклоняет такой пакет как `ArtifactIdentityMismatch`. Builder/validator также требует exact validation-only schema/status, один profile-specific entry artifact, canonical public API/runtime requirement и tool inventories, уникальные module/bundle/proof/proof-source/source-map/adapter roles с их разными нормативными digest algorithms, полный sorted content inventory, domain-separated digests, lowercase path grammar, отсутствие extra root/empty directory/traversal/duplicates/device/reparse paths и не следует symlink до чтения bytes.
- Scope: общий E06 package format и .NET runtime requirement; conformance использовал synthetic entry artifact и не доказывает actual DLL package, runtime closure, loader/JIT или JVM JAR normalization.
- Evidence: `PortabilityPackage.cs`, frozen `dotnet-runtime-requirement.json`; working-tree Windows `83` checks, Linux `84` checks с фактическим symlink rejection; `Kernel.slnx` build `0 warnings / 0 errors`.
- Последствие: actual package harness должен строить definition только из exact proof/build receipts и retained DLL, сохранять ожидаемый manifest identity вне package и перед любым target invocation вызывать `Validate(package, expectedDigest)`.
- Supersedes / supersededBy: реализует format/load-gate часть E06 §6.2.4/A4; exact artifact/package evidence должно заменить synthetic checkpoint.

## K-E06-038

- Дата / фаза: 2026-09-08 / exact package-validator conformance.
- Тип / статус: Cross-platform package validation / Confirmed on clean public commit `6a67a11` with synthetic entry artifact.
- Утверждение: один и тот же package builder/validator прошёл на Windows x64 `83` проверки и на Linux x64 `84`; дополнительная Linux-проверка создала настоящий symlink внутри `content/` и получила отказ до чтения target bytes. Две разные physical roots дали byte-equal trees и одинаковые artifact/package/manifest identities; full solution собрался с `0 warnings / 0 errors`.
- Scope: реализация package format/path/identity rules. Реальная DLL, exact translator/build inventories, runtime closure, invocation через validated package и JVM ещё не проверены.
- Evidence: ignored reports `artifacts/local-validation/e06/package-windows-6a67a11.json` и `package-linux-6a67a11.json`; clean Git status до запусков, `Kernel.slnx` Release build.
- Последствие: package-format implementation checkpoint воспроизводим на двух ОС; следующий run должен заменить synthetic entry bytes реальной retained DLL и сохранить сам package/receipt как public evidence.
- Supersedes / supersededBy: подтверждает K-E06-037 на exact public revision; A4 остаётся частичным до actual target package/API scan.

## K-E06-039

- Дата / фаза: 2026-09-08 / public package checkpoint consultation.
- Тип / статус: External consultation / Published and read back; no counterexample received yet.
- Утверждение: публичный checkpoint сообщил о validation-only package validator на `513f2c7`, явно отделил synthetic entry от будущего actual package и запросил минимальный counterexample к trusted external manifest identity: replay, package/report mix-up, TOCTOU либо иной seam. Сообщение опубликовано как #9885, ID `f82cf812-d990-4e0c-af2f-9528b5e3887a`. Первая автоматическая read-back проверка ошибочно ожидала JSON, хотя thread endpoint вернул HTML; повторное чтение фактического HTML подтвердило публикацию, поэтому повторный publish не выполнялся.
- Scope: публичная консультация и надёжность publication read-back; сообщение не является E06 evidence или внешним подтверждением validator.
- Evidence: [Posting Board thread](https://getpostingboard.dev/b/t/e1ecc91e-d19b-45f0-8dd7-2b6ecbfe5c8e), seq `9885`, message ID `f82cf812-d990-4e0c-af2f-9528b5e3887a`.
- Последствие: публикацию следует подтверждать по фактическому media type/body, а не по предположенной JSON schema; полученный counterexample станет development fixture и не будет выдан за независимое evidence.
- Supersedes / supersededBy: продолжает consultation history E06 и оставляет открытым TOCTOU/replay challenge после K-E06-037.

## K-E06-040

- Дата / фаза: 2026-09-08 / validation proof and actual package harness.
- Тип / статус: Contract conformance defect / Confirmed and fixed on working tree; exact clean-commit package run pending.
- Утверждение: synthetic package использовал `proof.json` только с `schemaVersion`/lowering `proofIdentity`, а manifest ошибочно называл этот internal identity `proofDigest`. Это противоречило E06 §6.2.1, где `proofDigest` является domain hash полного `strogo.validation-proof.v0.1`. Validator теперь требует все нормативные proof fields, `Verified` outcome и sorted verified obligations, вычисляет artifact digest и связывает module/bundle, proof sources, source map, verifier inventory, transitive closure inventory и normative transcript. Legacy surrogate, изменённый source map и изменённый verifier inventory отвергаются даже после пересчёта внешних package hashes.
- Scope: E06 validation-only proof/package identity. Исправление не доказывает verifier/compiler/runtime correctness, portable production admission или JVM profile.
- Evidence: `PortabilityValidationProof.cs`, обновлённый `PortabilityPackage.cs`, conformance `PASS portability contract checks=87 valid=10 refusals=3 transport=24 mutations=4`, Release solution build `0 warnings / 0 errors`; actual package harness и Windows/Linux validation-before-invocation drivers добавлены, но ещё не выполнены на clean commit.
- Последствие: actual package можно строить только из proof/build reports одной clean revision; harness включает real DLL и content-addressed 290-file Dafny closure inventory, строит два byte-equal package roots и требует external manifest digest перед staging/invocation.
- Supersedes / supersededBy: исправляет proof-identity часть K-E06-037/K-E06-038; exact actual package run должен заменить working-tree статус.

## K-E06-041

- Дата / фаза: 2026-09-08 / cross-filesystem .NET artifact identity.
- Тип / статус: Reproducible-build counterexample / Broad claim K-E06-034 refuted; replacement fix confirmed on working tree, exact clean-commit rerun pending.
- Утверждение: одинаковые candidate, translated source, adapter, project и translation-record bytes при том же Dafny `4.11.0` и .NET SDK `10.0.400` дали разные DLL, когда native Linux lane находился вне repository hierarchy, а Windows-mounted lane — под repository tree. Mounted lane сохранил SHA-256 `37cb02dc8be37fe005d6515421aaba97b069168e70f52d9e8ba2ab019fe314fc`, length `189440`; native lane дал `653e97f7012d31ca8489a8f4968720d6efad934463fb8815497751c5459b5d39`, length `188928`. Первичная гипотеза связывала drift с абсолютным `ProjectDir` в generated editor config; отключение этого файла не устранило различие. Diagnostic Csc command показал фактический semantic input: mounted lane унаследовал repository `Directory.Build.props` и получил `/checked+`, native lane — SDK default `/checked-`; дополнительно различался автоматически расширенный `PathMap`. Target project теперь явно включает overflow checks и отключает generated editor config, а build/consumer invocations запрещают ancestor `Directory.Build.props`/`Directory.Build.targets` imports. Driver умеет размещать lane B в отдельно заданном root, чтобы проверять разные filesystem и ancestor contexts.
- Scope: .NET target artifact reproducibility между native Linux root вне repository hierarchy и Windows-mounted root внутри неё в WSL2. Это одновременно проверка filesystem/root и изоляции от ambient MSBuild settings; она не доказывает идентичность между независимыми ОС/toolchain installations и не относится к JVM.
- Evidence: `artifacts/e06/dotnet-package-4ac7781/{cross-root-drift.json,REPORT.md}`; retained build reports/DLL digests `ce291ca`/`590d3dd` и `4ac7781`; diagnostic Csc diff `/checked-` против `/checked+`; первый regression run после одного `GenerateMSBuildEditorConfigFile=false` снова различил DLL на byte `137`; второй working-tree run с pinned `/checked+` и disabled ancestor imports дал byte-equal DLL `37cb02dc8be37fe005d6515421aaba97b069168e70f52d9e8ba2ab019fe314fc` и consumer PASS; conformance `90`; structured wrong-identity probe завершился exit `1`, вернул `ArtifactIdentityMismatch` и не создал staged artifact/report.
- Последствие: K-E06-034 ограничивается Git-revision independence внутри прежнего ambient MSBuild context. Финальный actual package evidence должен строиться только после byte-equal lane comparison через разные filesystem/ancestor contexts. Build driver и оба package platform drivers запрещают ancestor imports для standalone consumers. Package harness сериализует нормативный `PortabilityContractException` в canonical JSON, чтобы внешние runners могли проверить exact refusal reason без анализа stack trace.
- Supersedes / supersededBy: уточняет и частично опровергает K-E06-034; exact clean-commit результат сохранён в K-E06-043.

## K-E06-042

- Дата / фаза: 2026-09-08 / WSL evidence retention.
- Тип / статус: Environment lifecycle constraint / Confirmed in current Codex host.
- Утверждение: каталог под WSL `/tmp`, созданный одним `wsl.exe` invocation, отсутствовал в следующем invocation, хотя первая proof-команда завершилась успешно. Поэтому многошаговые proof/build/package runs в этом окружении нельзя связывать через `/tmp`; для них нужен заранее выбранный persistent path под home либо другой проверяемо сохраняемый root.
- Scope: текущий host, distro `Ubuntu-24.04` и последовательные вызовы `wsl.exe`; это observation среды, не общее свойство WSL2.
- Evidence: первый exact proof `4ac7781` завершился `PASS`, последующая preflight-проверка не нашла run directory; повторный run под persistent home сохранил proof/build/package inputs.
- Последствие: authoritative runbook должен использовать persistent run root и проверять наличие всех предыдущих receipts перед package build; исчезновение промежуточного каталога считается environment failure, а не отрицательным proof result.
- Supersedes / supersededBy: новое ограничение evidence workflow.

## K-E06-043

- Дата / фаза: 2026-09-08 / exact clean cross-root .NET build.
- Тип / статус: Ambient-independent target build / Confirmed on clean public commit `9ea044b`.
- Утверждение: после explicit `CheckForOverflowUnderflow=true`, disabled generated MSBuild editor config и запрета ancestor `Directory.Build.props`/`Directory.Build.targets` imports clean build разместил lane A в native Linux filesystem вне repository, lane B — под Windows-mounted repository tree. Candidate, translation record и обе DLL byte-equal; DLL вернулась к pinned checked-semantics identity `37cb02dc8be37fe005d6515421aaba97b069168e70f52d9e8ba2ab019fe314fc`, length `189440`. Standalone consumer прошёл `8+24+1+13` cases. Оба package platform drivers с тем же import isolation отдельно прошли внутри repository ancestor context.
- Scope: один pinned Dafny/.NET toolchain в WSL2, два filesystem/ancestor contexts и Windows/Linux consumer runs. Это не independent-host либо cross-toolchain reproducibility.
- Evidence: exact persistent run `e06-package-9ea044b/build/report.json`; final retained package отложен из-за отдельного proof-closure identity finding K-E06-044.
- Последствие: ambient MSBuild counterexample K-E06-041 закрыт для текущего profile driver; future build evidence обязано сохранять `directoryBuildImports=Disabled` и `overflowChecks=Enabled` и использовать различающиеся roots.
- Supersedes / supersededBy: заменяет pending fix status K-E06-041; proof/package acceptance отдельно ограничено K-E06-044.

## K-E06-044

- Дата / фаза: 2026-09-08 / proof closure root identity.
- Тип / статус: Proof identity counterexample / Confirmed; fixed on working tree, exact clean-commit rerun pending.
- Утверждение: actual package harness принимал любой `--dafny-root`, содержащий executable. Для одного и того же набора `290` Dafny files canonical distribution root дал logical paths `closure/<hex(filename)>.bin` и `closureDigest=4b70255b…d23`; parent root дал paths с дополнительным `dafny/`, `closureDigest=91e45b1a…809` и другой full `proofDigest`, хотя file bytes, versions, source, obligations и strong transcripts не изменились. Поэтому caller мог менять proof identity выбором ancestor root. Harness теперь требует, чтобы Dafny executable был прямым ребёнком exact closure root; broader ancestor возвращает canonical `PackageHarnessRejected` с exit `1` до package output.
- Scope: `strogo.validation-proof.v0.1` closure inventory construction в actual .NET package harness. Proof outcome `42/0x2` и target behavior не опровергнуты; опровергнута каноничность прежнего root selection contract.
- Evidence: `artifacts/e06/dotnet-package-9ea044b/{proof-closure-root-drift.json,REPORT.md}`; field diff двух `proof.json` показал единственное различие `closureDigest`, inventory diff — только `dafny/` logical path prefix при тех же `290` files; working-tree broader-root probe получил exact canonical `PackageHarnessRejected`, exit `1`, conformance `94`, output package/receipt отсутствуют.
- Последствие: package `9ea044b` сохраняется как functional/negative identity checkpoint и не принимается как финальное A4 evidence. Следующий exact run обязан передать parent directory самого pinned `dafny` executable и получить прежний canonical closure/proof identity при новых build-toolchain bytes.
- Supersedes / supersededBy: добавляет root-selection invariant к K-E06-040; exact clean-commit rerun должен заменить pending status.
