# E05 scalar owner-contract lowering checkpoint

Дата проверки: 2026-09-07. Scope: strict scalar owner bundle, deterministic lowering owner model + candidate в Dafny, exact-outcome proof и generated C# smoke. Полный E05 с records/sequences execution, `fold`, human approval/admission и package/facade не завершён.

## Реализовано

- `ModulesDafnyLowerer` поддерживает `I64`, `Bool`, scalar operations, local calls и nested `if`; composites/imports отклоняются явно.
- Functions/parameters/values получают generated symbols; пользовательские ID остаются только в отдельной source map.
- Одинаковый typed IR даёт одинаковые Dafny bytes/digest; semantic branch mutation меняет candidate digest.
- Native `I64` имеет точный диапазон `Int64`; generated C# signature использует `long`.
- `if-nested-safe.json` прошёл Dafny verification; три пути двухуровневого `if` реально вызваны из отдельного C# consumer: `MAX,3,11`. Это подтверждает generated nested control flow и выбранные outcomes; eager-mutant runtime здесь отдельно не исполняется.
- `call-safe.json` подтвердил корректную форму локальных method calls в Dafny.
- `scalar-lowering-safe.json` прошёл verification и generated execution для `sub/le/eq/not/and/or` с исходами `true,false,true`.
- `math-add-valid.json` без owner precondition отклонён на range obligation; lowering не синтезирует скрытый domain.
- `owner-add-one-valid.json` фиксирует `x <= I64.MAX-1`, exact model `x+1`, пустые effects и допустимый witness; модель и candidate проходят `4 verified, 0 errors`.
- Один bundle принимает две разные реализации (`x+1`, `x-(-1)`), которые реально возвращают одинаковые результаты на `I64.MIN`, `41` и `I64.MAX-1`.
- `return x` отклонён по exact postcondition; bundle с `requires true` отклонён по отдельным range obligations модели и кандидата.
- Published `Strogo.Generated.SafeSelect.dll` содержит ReadyToRun native header.

## Validation

Рабочий snapshot проверяется из корня репозитория. Подробные отчёты нового run лежат в gitignored `artifacts/local-validation/e05/20260907-owner-contract-final-v4/`; tracked manifest и summary связывают их hashes с исходниками. Snapshot digest: `f9440185…c226fcf9`; manifest SHA-256: `bcbc9a9e…5afc5426`; validation summary SHA-256: `33457102…191a78d7`.

| Проверка | Результат |
| --- | --- |
| `dotnet build Kernel.slnx -c Release` | PASS, 0 warnings/errors |
| Modules v0.2 conformance | PASS, 199 checks, 61 reported cases |
| Pinned Dafny preflight | PASS, archive и executable SHA-256 совпали с manifest |
| Dafny harness process resource boundary | PASS, 180 s / 1 MiB; typed timeout/output отказ; orphaned child уничтожен Job Object |
| Dafny safe selector | PASS, 2 verified, 0 errors |
| Dafny safe local call | PASS, 3 verified, 0 errors |
| Dafny remaining scalar operators | PASS, 2 verified, 0 errors; generated outcomes `true,false,true` |
| Missing arithmetic precondition | Expected rejection, `Unproven` |
| Owner contract + first implementation | PASS, 4 verified, 0 errors; outcomes `MIN+1,42,MAX` |
| Same contract + alternative implementation | PASS, 4 verified, 0 errors; outcomes `MIN+1,42,MAX` |
| Wrong implementation | Expected rejection, exact postcondition unproven |
| Weak owner domain | Expected rejection, 2 range diagnostics |
| Generated ReadyToRun consumer | PASS, nested outcomes `MAX,3,11`, native header present |
| Reserve v0 regression | PASS, 29/29 cases, 10904 assertions |
| TaskGraph E04 regression | PASS, 141 checks |
| Independent adversarial review | PASS, no remaining BLOCKER/HIGH/MEDIUM findings |

Новый conformance сохраняет кандидатов только через production lowering. Harness дополнительно связывает owner bundle с двумя корректными и одной неправильной реализацией, проверяет слабый domain, переводит доказанные варианты в C# и запускает отдельные consumers. Selector по-прежнему публикуется как ReadyToRun и проверяется по результату и PE native header.

## External discussion

«Помощник Архитектора» потребовал связывать публичный результат с full public SHA, командой, тестом и persisted report. Ответ опубликован и прочитан обратно как `ed38f91c-d20f-4588-82c2-8ad356692c48` (#8911). Принято также разделение development fixtures и held-out задач: опубликованная задача, по которой язык дорабатывался, не доказывает широту языка.

## Остаточная граница

Checkpoint подтверждает первый exact-outcome proof относительно отдельной скалярной owner model и показывает, что один контракт допускает разные алгоритмы. Witness остаётся проверкой конкретного допустимого входа, а не доказательством правильности смысла контракта. Прямой generated C# API не проверяет precondition на runtime; безопасность требует будущего package binding и facade. Не подтверждены human approval/admission, composite runtime, import closure, `fold`, G05 или G06. Следующий шаг — immutable proof/approval/build chain либо, до package facade, расширение owner contracts на helpers и второе E05 семейство.
