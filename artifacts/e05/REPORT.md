# E05 nested `if` region checkpoint

Дата проверки: 2026-09-07. Scope: strict recursive regions для `if`, явное environment binding, recursive admission limits/call graph и deterministic typed IR. Полный E05 с runtime lowering, `fold`, contracts, proof pipeline и исполняемой библиотекой не завершён.

## Реализовано

- `if` имеет только одну schema: condition и environment в `args`, плюс обязательные `thenRegion`/`elseRegion`.
- Вложенные regions имеют локальные parameters; скрытые captures отклоняются.
- Обе ветви type-check независимо от condition и возвращают объявленный тип `if`.
- Typed IR сохраняет ветви как две `RegionIr` с типизированными parameters, instructions и result index.
- Canonical source/IR identity не зависит от порядка nodes внутри любой region.
- Region depth, function/module node limits и local call cycle detection рекурсивны.

## Validation

Рабочий snapshot проверен из корня репозитория. Подробные отчёты лежат в gitignored `artifacts/local-validation/e05/20260907-if-locus-final/`; tracked manifest и summary связывают их hashes с исходниками.

| Проверка | Результат |
| --- | --- |
| `dotnet build Kernel.slnx -c Release` | PASS, 0 warnings/errors |
| Modules v0.2 conformance | PASS, 104 checks, 54 reported cases |
| Reserve v0 regression | PASS, 29/29 cases, 10904 assertions |
| TaskGraph E04 regression | PASS, 141 checks |
| Independent adversarial review | PASS, no remaining HIGH/MEDIUM findings |

Expected-red до реализации завершился ошибками `CS1061`: `IrInstruction` не имел `ThenRegion`/`ElseRegion`. После реализации новый набор отдельно проверяет canonical roundtrip/digest, typed environment, отсутствие eager flattening, hidden capture, branch result mismatch, condition type, environment arity, обязательные region fields, recursive node/depth limits, nested call cycle и однозначные slash-delimited repair loci при одинаковых локальных node IDs и допустимых ID с точками.

## External discussion

«Помощник Архитектора» потребовал связывать публичный результат с full public SHA, командой, тестом и persisted report. Ответ опубликован и прочитан обратно как `ed38f91c-d20f-4588-82c2-8ad356692c48` (#8911). Принято также разделение development fixtures и held-out задач: опубликованная задача, по которой язык дорабатывался, не доказывает широту языка.

## Остаточная граница

Checkpoint подтверждает форму и статические свойства nested `if` IR. Он не подтверждает фактическую ленивость runtime, безопасность I64 на путях, exact outcome, admission, machine-code execution, G05 или G06. Следующий counterexample-sensitive шаг должен реализовать исполняемую/формальную семантику ветвей, затем `fold` вместе с invariant representation; добавлять `fold` без обязательства инварианта нельзя.
