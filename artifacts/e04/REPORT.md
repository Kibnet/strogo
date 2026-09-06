# E04 — результат ограниченного профиля TaskGraph

2026-09-06. Реализован `task-graph.clone.v1`: строгий JSON AST из шести операций → Dafny → C# → .NET ReadyToRun win-x64. Модуль принимает ограниченный граф и host ID maps, возвращает additions либо структурированный отказ. Само хранилище не изменяет.

Утверждение владельца связано с SPEC commit `8a9342e8b0458c55ce1eb5096dc1b16901bec6ba`, blob `2cfed958a7fa6c6717f4183cc9fb3c232489bae7`. Промежуточный implementation checkpoint — `d2cec58`; финальные source/KB commits фиксируются knowledge-sync manifest.

## Проверенный результат

- Новый conformance: **141 checks PASS**, включая публичные contract/compile/run/explain, C01–C05, 32 generated inputs, строгий decoder, approval/artifact tampering, missing/malformed receipts, намеренную порчу результата adapter, timeout/output cap и завершение child PID.
- **12 semantic proof mutations** отвергнуты под неизменным owner contract, включая always/selective reject и omit-criteria. Public AST negatives проверены отдельно.
- Полный admission translate: **74 verified, 0 errors**. Отдельная проверка model: 171 verified, 0 errors; команды группируют obligations по-разному. Количество obligations само по себе не доказывает покрытие — карту всех 14 инвариантов см. [в руководстве](../../docs/task-graph-v1.md).
- Реальный опубликованный GraphModule.dll содержит ReadyToRun native header и выполняет сценарии. Run не вызывает reference oracle.
- Две сборки: одинаковые semantic/source/build-input digests и runtime outcomes; **binaryEqual=false**. Побайтовая воспроизводимость не заявляется.
- Существующий v0: **29/29 cases, 10 904 assertions**, без изменений source Core/Host/CLI v0.
- Последний полный запуск занял около 151 s на этой машине; это длительность conformance, **не G05/G06 benchmark**.

Evidence: [conformance](conformance.json), [admission](proof-receipt.json), [replay admission](replay-receipt.json), [CLI outcome](cli-outcome.json), [v0 regression](v0-regression.json), [development history](development-evidence.json), [review](review.json).

## Acceptance

| AC | Свидетельство / результат |
|---|---|
| AC1–2 | Closed JSON/types/pipeline, source maps, opcode/policy/schema rejection, canonical digest |
| AC3 | Owner Contract + exact ModelOutcome + application/sorting theorems; complete admission verify |
| AC4 | Реальный corpus/oracle и 12 solver mutations |
| AC5 | Published win-x64 R2R, PE native header, actual module execution |
| AC6 | Hash/owner-registry admission, bypass negatives, missing/malformed/tampered artifacts, conversion-fault validator |
| AC7 | Два clean work dirs; semantic equality; binary inequality сохранена |
| AC8 | Bounded transport, process deadlines, суммарный output cap 1 MiB, child process завершён; OS RAM cap не заявлен |
| AC9 | Archive/exe/distribution/support/binary digests, зафиксированные flags/SDK, locked restore, explicit TCB |
| AC10 | 29 cases / 10904 assertions v0 PASS |
| AC11 | Actual public CLI projection, receipt target/digests/status, outcome-only без выдуманного admission |
| AC12 | G05/G06, all platforms, full AOT и общая выразительность не заявлены |
| AC13 | 19 записей KB, promotion 00/02/04/05/06/07/08, immutable failures, содержательный review; итоговая проверка — knowledge-sync.json и knowledge-check.json после source/KB commits |

## Исправления по review

Первый review выявил слабый accepted-only contract и отсутствие критерия полноты criteria. Total outcome и exact mapping закрыли эти пробелы. Следующий review отделил равенство программе модели от свойств самой модели: добавлены PatchGraphValidity и Wire proofs.

Public explain первоначально игнорировал выбранный artifact; прямые library tests этого не замечали. Добавлены admission-bound projection и public CLI smoke. Дефекты DTO conversion теперь отсекает независимый output validator, missing/malformed artifacts дают ArtifactMismatch вместо случайных .NET error names. Точный отчёт и граница writable review — review.json.

Ранний mixed-source conformance с изменённым Contract между build давал ложный сигнал nondeterminism. Логи сохранены отдельно; они не используются как успешное evidence. Сбой восстановления без APPDATA и появление буквального каталога %SystemDrive% устранены закрытым allowlist системных путей. Последний прогон не создал такой каталог.

## Границы и следующий шаг

Доказательство условно корректностью owner contract, Dafny/Boogie/Z3, backend, adapter/validator, .NET runtime/compiler, receipt verifier и ОС. Администратор с произвольной записью в доверенные файлы вне threat model. Защита от ошибок человека при формализации не доказана.

ReadyToRun содержит IL и может применять JIT; это не полный AOT. Проверена одна платформа и один фиксированный алгоритм, нет общего module/package ecosystem и замеров performance/memory. Строгость не доказала ни большинство всех ошибок, ни сокращение времени работы агента.

Следующий обоснованный шаг — отдельная SPEC эксперимента, где агент выбирает полезную реализацию в пределах контракта. Только после этого имеет смысл измерять G05 с учётом формализации, доказательств, tests/review/repairs; G06 и переносимость исследуются отдельно. Автоматический запуск нового эксперимента из E04 не следует.
