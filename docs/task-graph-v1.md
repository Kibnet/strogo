# E04: ограниченный профиль TaskGraph

Профиль `task-graph.clone.v1` принимает только утверждённый pipeline из шести операций. Это исполнимый эксперимент с ограничениями и доказательством; свободного выбора алгоритма и библиотеки общего назначения здесь нет.

## Запуск

Из корня проекта, Windows x64, .NET SDK 10.0.400:

```powershell
pwsh -File tools/Install-Dafny.ps1
dotnet run -c Release --project src/Kernel.Graph.Cli -- contract --approval artifacts/local-validation/e04/contract-approval.json
dotnet run -c Release --project src/Kernel.Graph.Cli -- compile --program fixtures/task-graph-v1/program.accepted.json --approval artifacts/local-validation/e04/contract-approval.json
dotnet run -c Release --project src/Kernel.Graph.Cli -- run --receipt <путь-из-compile> --input fixtures/task-graph-v1/c01.json
```

`contract` связывает digest модели с уже полученным human approval SPEC commit `8a9342e8b0458c55ce1eb5096dc1b16901bec6ba`. Автор модели — доверенная сторона. Команда не доказывает эквивалентность нового человеческого требования старой SPEC.

`compile` проверяет JSON/types/pipeline, создаёт Candidate.dfy и source-map.json, проверяет полный owner Contract вместе с кандидатом, генерирует C# с runtime Dafny, выполняет restore и locked restore, публикует ReadyToRun win-x64. Проверяется native header опубликованной DLL. `run` сверяет owner registry, contract/support/tool/artifact hashes и запускает именно опубликованный модуль. Независимый C# oracle используется только в conformance.

ReadyToRun содержит машинный код и IL, возможен JIT; это не полный AOT. Зависимость от установленного .NET runtime сохранена. Самостоятельное атомарное применение patch к хранилищу не входит в E04.

## Соответствие инвариантов доказательствам

| № SPEC | Основание |
|---|---|
| 1 | Grow: содержит root, замкнут по Next, минимален среди замкнутых надмножеств |
| 2 | Selected membership/SelectedIds, CloneNodes exact length/index mapping, PatchIdsUnique |
| 3–4 | FirstError, ExactMap, LookupInjective, Range uniqueness/disjointness |
| 5–6 | CloneNodes и CloneCriteria: точные indexwise postconditions, payload/repeat и полный reset |
| 7 | Shift/Fits, DatesFit; int модели не переполняется, Accepted лежит в I64 |
| 8–9 | InternalExact, Remap indexwise, RemapUnique; только endpoints из Reach |
| 10 | Patch имеет только additions; ApplyFramesOriginal сохраняет исходные префиксы |
| 11 | PatchGraphValidity: PatchIdsUnique, PatchEndpoints, PatchAcyclic; прямо в ensures Candidate.Run |
| 12 | CanonicalGraph.Sort/Wire: Ordered + multiset, OrderCriteria сохраняет поля и multiset критериев |
| 13 | Outcome datatype и точное равенство ModelOutcome; обязательный Accepted iff FirstError==0 |
| 14 | Dafny termination checking: decreases конечных seq/set; нет decreases * |

Эта таблица — карта для содержательного review, а не дополнительная аксиома. Verifier не доказывает, что owner сформулировал все требования человека. ASCII-валидность transport, I64/schema limits и корректность преобразования C#/Dafny входят в TCB. Формальная модель доказывается для более широких конечных последовательностей; bounded transport ограничивает практическую стоимость.

Wire сортирует edge по `src + "!" + dst`: разделитель меньше любого разрешённого символа ID, поэтому порядок совпадает с ordinal сравнением пары from/to. Сортировка ID Unicode внутри Dafny совпадает с ordinal для разрешённого ASCII домена.

## Проверки и границы

C01–C05 и generated corpus сравнивают реальные outcomes ReadyToRun с отдельным BFS/Kahn/BigInteger oracle. Это проверки интеграции TCB, а не замена universal proof. Семантические мутации изменяют только harness-кандидата под неизменным owner contract; public AST остаётся закрытым. После полного accepted verify mutation verify фильтруется на Candidate, чтобы не повторять независимые owner lemmas 12 раз; фильтр никогда не используется для admission.

Два новых build сравнивают canonical/source/build-input digests и runtime replay. Побайтовое совпадение R2R измеряется отдельно. Старые receipts после изменения support/model отвергаются. Произвольная правка owner registry и доверенных инструментов администратором вне модели угроз. Ограничения времени и суммарного вывода процесса не являются лимитом памяти ОС.

G05 (скорость/число попыток агента), G06 (performance/memory), другие платформы, package ecosystem и industrial sandbox не измерены. Фиксированный pipeline почти устраняет поиск реализации; его успех не доказывает преимущество нового языка общего назначения.

Исторические знания опубликованы в [журнале E04](e04-knowledge-log.md). Старый `artifacts/e04/knowledge-sync.json` связывает исходные локальные project/KB commits. Его проверка требует исходной истории; условия описаны в [происхождении публикации](publication.md).

## Повторный полный conformance

Runner E04 пока сохраняет результаты в фиксированный каталог `artifacts/e04` и перезаписывает одноимённые файлы. Для полного прогона используйте отдельную копию checkout; её результаты не следует смешивать с историческими свидетельствами основной рабочей копии. Из корня этой отдельной копии:

```powershell
pwsh -File tools/Install-Z3.ps1
pwsh -File tools/Install-Dafny.ps1
dotnet restore --locked-mode
dotnet run -c Release --project tests/Kernel.Graph.Conformance
dotnet run -c Release --project tests/Kernel.Conformance -- --suite all --report artifacts/local-validation/v0-regression.json
```

Первые команды раздела «Запуск» используют отдельный файл approval в `artifacts/local-validation`; сгенерированные сборки и receipts помещаются в игнорируемый `artifacts/e04/work`.