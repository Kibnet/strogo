# E05 owner-composite v0.3 checkpoint

Дата проверки: 2026-09-08. Scope: owner-owned composite type closure, exact model для records и bounded sequences, explicit v0.2 migration, deterministic witness replay и Dafny→C# proof/runtime smoke. Admission, `fold`, imports/helpers, runtime facade и release не входят в checkpoint.

## Реализовано

- `strogo.owner-bundle.v0.3` владеет точным минимальным semantic type closure и canonical digest; binder требует совпадения всех owner-reachable module types, сохраняя свободу private candidate types.
- Один approved composite model принимает две разные candidate DAG-реализации и отклоняет observable wrong order.
- Owner `seq.append` всегда материализуется как bounded subset value, включая вложенные выражения, поэтому capacity obligation нельзя потерять.
- Strict `bool.and/or` вычисляют definedness обеих сторон; lazy partiality выражается только через `if`.
- Replay заново строит trusted binding. `Counterexample` означает только два успешно вычисленных разных значения; `Timeout`, `ToolError` и allow-listed `CandidateError` различаются.
- v0.2 migration проверяет legacy schema, budgets и прежние semantic restrictions до преобразования; malformed input возвращает typed refusal, approval не переносится.

## Validation

Подробные отчёты находятся в gitignored `artifacts/local-validation/e05/owner-v03-final-20260908-v2/`. Tracked `source-manifest.json` связывает 22 файла со snapshot digest `52b9dcc4…462245f2`; manifest SHA-256 `86766833…92f06c55`. `validation-summary.json` SHA-256 `6bcf6ac3…ae74e958`.

| Проверка | Результат |
| --- | --- |
| `dotnet build Kernel.slnx -c Release --no-restore` | PASS, 0 warnings/errors |
| Scoped `dotnet format whitespace --verify-no-changes` | PASS |
| Modules conformance | PASS, 276 checks / 65 reported cases |
| Composite owner candidates A/B | PASS, оба `5 verified, 0 errors`, одинаковые generated outcomes |
| Wrong candidate | `Counterexample` на replayed witness и failed exact postcondition |
| Strict `false and` / `true or` partial | Expected `Unproven` |
| Guarded lazy `if` cases | PASS, `Verified` |
| Nested owner append at full capacity | Expected `Unproven` по subset constraint |
| Generated ReadyToRun smoke | PASS, `win-x64`, native header 196 bytes |
| Reserve v0 regression | PASS, 29/29 cases, 10 904 assertions |
| TaskGraph E04 regression | PASS, 141 checks |
| Final procedural read-only review | PASS, 0 remaining BLOCKER/HIGH/MEDIUM |
| Independent Windows reproduction of `c99fd1f` | PASS, 276 Modules checks and full Dafny harness; public read-back #9674 |

## Диагностические наблюдения

Один предшествующий E04 run завершился typed timeout, а один Reserve run после серии proof/build процессов получил несколько `SolverTimeout` и был остановлен. Следующие сериализованные runs без изменений исходников прошли. Диагностика сохранена рядом с финальными reports; resource contention остаётся гипотезой, а не установленной причиной.

## Остаточная граница

Доказательство относится к exact model внутри конкретных canonical owner bytes и pinned Dafny 4.11.0. Соответствие owner model человеческой спецификации подтверждает человек. Эквивалентность reference evaluator и Dafny lowering остаётся частью TCB. Replay result ещё не является подписанным admission receipt и не разрешает публикацию/исполнение package вне будущего runtime facade.
