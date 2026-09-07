# E05 scalar reference semantics checkpoint

Дата проверки: 2026-09-07. Scope: strict recursive regions для `if`, deterministic typed IR и ограниченный scalar reference evaluator с checked arithmetic, local calls и step budget. Полный E05 с generated runtime lowering, records/sequences execution, `fold`, contracts, proof pipeline и исполняемой библиотекой не завершён.

## Реализовано

- `if` имеет только одну schema: condition и environment в `args`, плюс обязательные `thenRegion`/`elseRegion`.
- Вложенные regions имеют локальные parameters; скрытые captures отклоняются.
- Обе ветви type-check независимо от condition и возвращают объявленный тип `if`.
- Typed IR сохраняет ветви как две `RegionIr` с типизированными parameters, instructions и result index.
- Canonical source/IR identity не зависит от порядка nodes внутри любой region.
- Region depth, function/module node limits и local call cycle detection рекурсивны.
- Reference evaluator исполняет scalar opcodes, local calls и только выбранную `if` region поверх typed IR.
- Runtime argument types/arity и exports проверяются до исполнения; общий `MaxSteps` ограничивает весь call tree.
- Контрпример `if-lazy-overflow.json` возвращает `I64.MAX` по безопасной ветви и даёт `ArithmeticOverflow` только при выборе переполняющейся ветви.

## Validation

Рабочий snapshot проверяется из корня репозитория. Подробные отчёты нового run лежат в gitignored `artifacts/local-validation/e05/20260907-reference-evaluator/`; tracked manifest и summary связывают их hashes с исходниками.

| Проверка | Результат |
| --- | --- |
| `dotnet build Kernel.slnx -c Release` | PASS, 0 warnings/errors |
| Modules v0.2 conformance | PASS, 137 checks, 56 reported cases |
| Reserve v0 regression | PASS, 29/29 cases, 10904 assertions |
| TaskGraph E04 regression | PASS, 141 checks |
| Independent adversarial review | PASS, no remaining BLOCKER/HIGH/MEDIUM findings |

Expected-red reference evaluator завершился 26 ошибками `CS0246`/`CS0103`: API и типы значений ещё отсутствовали. Реализация добавила executable checks для различающих scalar outcomes и truth tables, обеих ветвей, unused overflow, selected overflow locus, runtime type/arity, exports, local calls, shared step budget с исчерпанием внутри второго callee и явного отказа на неподдержанных composite opcode/import closure.

## External discussion

«Помощник Архитектора» потребовал связывать публичный результат с full public SHA, командой, тестом и persisted report. Ответ опубликован и прочитан обратно как `ed38f91c-d20f-4588-82c2-8ad356692c48` (#8911). Принято также разделение development fixtures и held-out задач: опубликованная задача, по которой язык дорабатывался, не доказывает широту языка.

## Остаточная граница

Checkpoint подтверждает форму nested `if` IR и его ленивую семантику только в reference evaluator. Он не подтверждает тот же результат для generated Dafny/C# runtime, exact outcome относительно owner model, admission, machine-code execution, G05 или G06. Следующий counterexample-sensitive шаг — differential Dafny lowering scalar/`if`; затем `fold` добавляется только вместе с обязательным invariant representation.
