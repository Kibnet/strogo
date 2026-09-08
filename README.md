# Strogo

> Experimental programming language for AI agents: explicit contracts, invariants, and machine-checked correctness.

**Strogo** — экспериментальный язык программирования для ИИ-агентов. Его цель — получать исполняемые модули по достаточной человеческой спецификации и машинно проверять правильность реализации относительно формального контракта, смысл которого подтвердил человек.

Проект исследует, можно ли за счёт явных требований, ограничений и доказательств уменьшить полные затраты на получение корректной программы. Работающие прототипы уже есть; преимущество по времени, стоимости, участию человека и эффективности готового кода пока не установлено.

## Что находится в репозитории

| Этап | Реализованный механизм | Проверенный локальный результат |
| --- | --- | --- |
| **Reserve v0** | Типизированный DAG с `I64`/`Bool`, проверка фиксированного контракта через Z3, компиляция в линейный IR, локальный host с SQLite, prepare/commit/replay | 29/29 cases, 10 904 assertions |
| **TaskGraph E04** | Ограниченный профиль `task-graph.clone.v1`: закрытый JSON AST → Dafny → C# → ReadyToRun win-x64; модуль возвращает additions либо отказ | 141 checks PASS, полный admission: 74 verified, 0 errors |
| **Modules v0.2 / E05 (в работе)** | Строгий composable AST и owner bundle v0.3; canonical source → typed IR → Dafny/C# с exact owner models для scalar, records и bounded sequences | 276 checks / 65 cases; по две разные scalar и composite реализации доказали один exact contract, wrong/partial варианты отклонены; `fold`, admission и package ещё не готовы |

Это результаты сохранённых запусков и их отчётов: [Reserve v0](REPORT.md), [TaskGraph E04](artifacts/e04/REPORT.md) и [E05 owner-composite v0.3 checkpoint](artifacts/e05/REPORT.md). Они не являются результатом CI новой публичной копии. Участник того же публичного исследования воспроизвёл 276 Modules checks и полный Dafny harness commit `c99fd1f` в свежем Windows checkout; это полезная внешняя репликация, но не независимый third-party audit. Modules conformance также дважды прошёл 276 checks / 65 fixtures на Ubuntu 24.04 WSL2 с Linux .NET SDK 10.0.400. На commit `8b53cdd` тот же WSL2-профиль дополнительно воспроизвёл [Linux Dafny 4.11.0 semantic matrix](artifacts/e05/linux-dafny-wsl2-8b53cdd/REPORT.md): 10 ожидаемых доказательств и 9 ожидаемых отказов. Отдельный native Linux host, CI, переносимый process-control harness и Linux ReadyToRun остаются непроверенными. Происхождение публикации и границы сохранённых свидетельств описаны в [publication.md](docs/publication.md), [K-E05-080](docs/knowledge-log.md#k-e05-080) и [K-E05-081](docs/knowledge-log.md#k-e05-081).

E04 использует фиксированный pipeline из шести операций. Его успешная проверка не доказывает выразительность языка общего назначения. ReadyToRun содержит машинный код и IL и может использовать JIT; полного AOT здесь нет.

Текущий E05 checkpoint и его границы описаны в [Modules v0.2](docs/modules-v0.2.md). Он проверяет composable representation, identity, scalar/`if`/record/bounded-sequence reference semantics, owner-owned composite type closure, exact-outcome proof и typed witness replay. Это ещё не admission, package, runtime precondition facade, `fold` или общая библиотека и не выполнение целей G01–G06.

## Быстрый запуск Reserve v0

Поддержанная конфигурация: **Windows x64, .NET SDK 10.0.400**. Версия SDK закреплена в `global.json`, NuGet-зависимости — в `packages.lock.json`. Скрипт установки загружает закреплённую версию Z3 и проверяет её SHA-256. Для установки инструментов и восстановления зависимостей нужен доступ к сети; платные API моделей не требуются.

Из корня репозитория в PowerShell:

```powershell
pwsh -File tools/Install-Z3.ps1
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet run --project tests/Kernel.Conformance -c Release --no-build -- --suite all --report artifacts/local-validation/conformance-results.json
dotnet run --project src/Kernel.Cli -c Release --no-build -- demo --directory artifacts/demo-run
dotnet run --project src/Kernel.Cli -c Release --no-build -- replay --directory artifacts/demo-run --event evt-001
```

Demo резервирует 3 из 10 единиц, получает остаток 7, повторяет событие без нового списания и воспроизводит сохранённый результат. В `artifacts/demo-run` появляются программа, IR, preview, receipts и локальная SQLite-база. Повторный запуск использует существующее состояние; для новой демонстрации укажите другую пустую папку.

Команды сохраняют новый отчёт в `artifacts/local-validation` и данные demo в `artifacts/demo-run`, чтобы не перезаписывать исторические свидетельства. Подробности протокола, семантики и примеры patch — в [руководстве Reserve v0](docs/reserve-v0.md).

## Запуск TaskGraph E04

Для E04 дополнительно нужен закреплённый Dafny. Используйте [инструкцию TaskGraph](docs/task-graph-v1.md): она описывает установку, последовательность `contract` → `compile` → `run`, conformance и связь approval с контрактом. Пути к receipts берутся из фактического результата `compile`.

Сохранённый approval относится к конкретной исторической спецификации. Запуск команды `contract` не означает человеческого подтверждения нового требования. Самостоятельное атомарное применение возвращаемого patch к хранилищу не входит в E04.

## Замысел и границы

[Шесть целей проекта G01–G06](docs/project-intent.md) охватывают исполняемые модули, машинный код зрелой платформы, исключение классов ошибок, доказанную реализацию контракта, стоимость полного цикла разработки и эффективность исполнения.

Машинная проверка относится к формальному контракту и заданным предположениям. Соответствие его смысла человеческому заданию подтверждает человек. Корректность encoder, solver, компилятора, adapters, host и runtime входит в доверенную базу конкретного этапа; вся эта цепочка механически не доказана. Проверки conformance дополняют свидетельства о работе инструментов.

**G05 и G06 не измерены.** Не подтверждены сокращение полной стоимости разработки, преимущество агента, сопоставимость производительности и памяти, исключение большинства всех частых ошибок, работа на других платформах и промышленная изоляция агента. Ограничения текущих прототипов не являются окончательными ограничениями языка.

## Навигация

- [Замысел и цели](docs/project-intent.md).
- [Reserve v0: протокол, семантика и границы](docs/reserve-v0.md).
- [TaskGraph E04: запуск и карта инвариантов](docs/task-graph-v1.md).
- [Отчёт v0](REPORT.md) и [отчёт E04](artifacts/e04/REPORT.md).
- [Спецификации этапов](specs/) и [сохранённые свидетельства](artifacts/).
- [Происхождение публичной версии](docs/publication.md).
- [Локальные проверки перед публикацией](docs/publication-checks.md).
- [Правила работы над проектом](AGENTS.md).

В `src/Kernel.Core`, `src/Kernel.Host` и `src/Kernel.Cli` расположен Reserve v0; проекты `Kernel.Graph.*` содержат E04. Каталог `tests/` хранит исполняемые conformance-проверки, `fixtures/` — входные примеры, `tools/` — установку закреплённых инструментов и их manifest-файлы.
