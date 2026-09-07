# E05 composite parser/IR checkpoint

Дата проверки: 2026-09-07. Scope: records, bounded sequences, local calls, strict parser и deterministic typed IR. Полный E05 с `if`/`fold`, contracts, proof lowering и исполняемой библиотекой не завершён.

## Реализовано

- Opcode-specific schema без неявных metadata/defaults.
- Именованные immutable records, `Seq<T,N>`, local calls и ацикличный call graph.
- Canonical source/IR identity без зависимости от порядка unordered declarations и nodes.
- Opaque validated source/IR objects с defensive copies и source identity recheck перед lowering.
- Единый structured error boundary и детерминированный выбор первой static diagnostic.

## Validation

Изолированная копия source snapshot `5445e7b3…158d9`: `artifacts/local-validation/e05/20260907-final3-1788776679/` (детальные локальные отчёты игнорируются Git, потому что содержат machine-specific paths). File-level source binding сохранён в tracked [source-manifest.json](source-manifest.json); hashes отчётов, выбранные результаты, версии инструментов и воспроизводимые команды — в [validation-summary.json](validation-summary.json).

| Проверка | Результат |
| --- | --- |
| `dotnet build Kernel.slnx -c Release` | PASS, 0 warnings/errors |
| Modules v0.2 conformance | PASS, 80 checks, 44 reported cases |
| Reserve v0 regression | PASS, 29/29 cases, 10904 assertions |
| TaskGraph E04 regression | PASS, 141 checks |
| Toolchains | Z3 5.1.0; Dafny 4.11.0 |

Первый запуск regression в той же копии был непригоден: глобальный фильтр каталога `bin` удалил вложенные solver binaries. После восстановления pinned tool directories их version/inventory checks и оба полных suites прошли. Это сохранено как K-E05-022.

## Review

Первый независимый проход: `NEEDS-FIX` (digest domain blocker; canonical I64; opaque identity; boundary exceptions; deterministic errors; depth/limits; opcode coverage; audit/docs). Следующие passes дополнительно нашли transport-order зависимости, непереносимую ссылку на ignored evidence и возможность повышать hard limits. Все замечания исправлены и покрыты conformance.

Финальный independent-reviewer pass: `PASS`, actionable findings отсутствуют. Reviewer отдельно сверил все 27 source hashes, snapshot digest, hashes manifest/reports, заявленные результаты и границы checkpoint. Suites в reviewer-сессии не перезапускались: проверена криптографическая связь с уже выполненным изолированным прогоном.

## Остаточная граница

Этот checkpoint подтверждает форму представления, type/schema checks и deterministic lowering. Он не подтверждает exact outcome, admission, machine execution Modules v0.2, G05 или G06.
