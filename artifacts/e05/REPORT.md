# E05 scalar Dafny lowering checkpoint

Дата проверки: 2026-09-07. Scope: deterministic lowering из typed scalar/`if` IR в Dafny, проверка total candidates и реальный Dafny→C#→ReadyToRun `win-x64` smoke. Полный E05 с owner models/contracts, records/sequences execution, `fold`, admission и package/facade не завершён.

## Реализовано

- `ModulesDafnyLowerer` поддерживает `I64`, `Bool`, scalar operations, local calls и nested `if`; composites/imports отклоняются явно.
- Functions/parameters/values получают generated symbols; пользовательские ID остаются только в отдельной source map.
- Одинаковый typed IR даёт одинаковые Dafny bytes/digest; semantic branch mutation меняет candidate digest.
- Native `I64` имеет точный диапазон `Int64`; generated C# signature использует `long`.
- `if-nested-safe.json` прошёл Dafny verification; три пути двухуровневого `if` реально вызваны из отдельного C# consumer: `MAX,3,11`. Это подтверждает generated nested control flow и выбранные outcomes; eager-mutant runtime здесь отдельно не исполняется.
- `call-safe.json` подтвердил корректную форму локальных method calls в Dafny.
- `scalar-lowering-safe.json` прошёл verification и generated execution для `sub/le/eq/not/and/or` с исходами `true,false,true`.
- `math-add-valid.json` без owner precondition отклонён на range obligation; lowering не синтезирует скрытый domain.
- Published `Strogo.Generated.SafeSelect.dll` содержит ReadyToRun native header.

## Validation

Рабочий snapshot проверяется из корня репозитория. Подробные отчёты нового run лежат в gitignored `artifacts/local-validation/e05/20260907-dafny-lowering-final/`; tracked manifest и summary связывают их hashes с исходниками.

| Проверка | Результат |
| --- | --- |
| `dotnet build Kernel.slnx -c Release` | PASS, 0 warnings/errors |
| Modules v0.2 conformance | PASS, 158 checks, 60 reported cases |
| Pinned Dafny preflight | PASS, archive и executable SHA-256 совпали с manifest |
| Dafny safe selector | PASS, 2 verified, 0 errors |
| Dafny safe local call | PASS, 3 verified, 0 errors |
| Dafny remaining scalar operators | PASS, 2 verified, 0 errors; generated outcomes `true,false,true` |
| Missing arithmetic precondition | Expected rejection, `Unproven` |
| Generated ReadyToRun consumer | PASS, nested outcomes `MAX,3,11`, native header present |
| Reserve v0 regression | PASS, 29/29 cases, 10904 assertions |
| TaskGraph E04 regression | PASS, 141 checks |
| Independent adversarial review | PASS, no remaining BLOCKER/HIGH/MEDIUM findings |

Новый conformance сохраняет четыре кандидата из production lowering: total nested selector, total local call, остальные total scalar operators и потенциально переполняющийся `addOne`. Последний обязан не пройти Dafny без owner `requires`. Отдельный скрипт переводит safe candidates в C#, запускает три generated consumers, публикует selector как ReadyToRun и проверяет результат и PE native header.

## External discussion

«Помощник Архитектора» потребовал связывать публичный результат с full public SHA, командой, тестом и persisted report. Ответ опубликован и прочитан обратно как `ed38f91c-d20f-4588-82c2-8ad356692c48` (#8911). Принято также разделение development fixtures и held-out задач: опубликованная задача, по которой язык дорабатывался, не доказывает широту языка.

## Остаточная граница

Checkpoint подтверждает первый generated Dafny/C# runtime только для total scalar selector и безопасного local call. Он не подтверждает exact outcome относительно owner model, admission/package binding, composite runtime, import closure, `fold`, G05 или G06. Следующий шаг — owner bundle/model и явное перенесение `requires`/`ensures` в proof obligations; после этого `fold` добавляется только вместе с обязательным invariant representation.
