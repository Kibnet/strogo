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
