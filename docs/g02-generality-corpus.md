# Корпус общности compiler/ABI G02

Замороженные входы находятся в [fixtures/g02-generality](../fixtures/g02-generality/frozen.json). Это два validation-only owner bundle и по два корректных тела плюс один неверный кандидат под каждым exact bundle. Файлы и raw SHA фиксируются вместе; разные тела не получают разные owner требования ради прохождения proof.

| Форма | Контракт и разные реализации | Негативный кандидат |
| --- | --- | --- |
| Scalar | Два exported entry: advance возвращает input+1; choose выбирает left+1 либо right+1 по Bool. Оба input ограничены MAX−1. Primary вызывает advance внутри обеих ветвей; alternative выбирает input и вызывает после branch. Helper использует add1 либо subtract(−1). | Выбирает противоположную ветвь при том же owner model. |
| Allocation | Прежний bounded ordered allocation owner смысл, Seq256 и Record state; primary/alternative fold с переименованными function/type/parameter/contract IDs и переставленными декларациями. | Выделяет ноль даже когда запрос можно удовлетворить; conservation и bounded state сохраняются, owner prefix semantics нарушается. |

Scalar witnesses проверяют обе ветви, MIN/MAX−1 и обычные различающиеся значения. Allocation проверяет прежние owner witnesses плюс empty/full256 границы. Args передаются именованным JSON в обратном порядке относительно сигнатуры; binding обязан восстановить правильную последовательность. Runtime dispatcher не выбирает функцию по CLR имени из пользовательского ввода.

Команда с pinned SDK PATH/DOTNET_ROOT:

```powershell
tests/Strogo.Modules.Admission.Conformance/bin/Debug/net10.0/Strogo.Modules.Admission.Conformance.exe --g02-generality-only
```

Runner проверяет frozen hashes, distinct source/candidate identity, точную структуру branch/local call и стабильность canonical IR/lowered source/map/obligations при перестановке деклараций. Для каждого правильного тела выполняются actual pinned Dafny proof и отдельно verified C# translation с равными transcript/obligations, offline ReadyToRun publish и загрузка **только bytes результата этой сборки** в collectible context. Затем существующий shared typed dispatch вызывается через strict JSON; owner/reference используются отдельно как oracle. Неверные кандидаты должны дать actual `VerifierFailed` до build/load: scalar — по postcondition, allocation — assertion на source line candidate-preservation. Тестовый buildCalls — счётчик host вызовов builder, assemblyLoads — actual AppDomain event для generated assembly.

Malformed JSON и wrong typed value проходят через тот же codec с counted callback вокруг actual dispatch: callback0 и byte-identical refusal обычному InvokeJson. Requires-invalid возвращает исходный CompiledPreconditionFailed. Порядок Q→F для этих новых вариантов подтверждается неизменным dispatch и прежними отдельными observer traces; этот runner не предоставляет новую независимую native method-entry trace. Reference evaluator не находится на исполняемом dispatch path.

Корпус выявил два дефекта, описанных в K-G02-037: одинаковые local call IDs разных ветвей давали duplicate obligation IDs; законный i64.eq(left,left) вызывал C# CS1718 style warning. Проверки квалифицированной provenance сохраняют отказ для identical duplicate и прежние unambiguous wire vectors. Narrow builder NoWarn относится к диагностике C#, не к proof/type/runtime guards. Текущий результат: Debug0 warnings/errors, module362PASS, frozen corpus78PASS (4builds/4loads), full fresh Admission343PASS exit0. [Evidence](evidence/g02-generality-20261001/README.md).

Это compiler/ABI conformance, а не публичный package loader/admission. Нет реального owner D02/release решения, physical closure identity или ordinary native attribution для допущенного пакета. Полный public runtime должен позднее пройти этот же frozen корпус; G02 и общая цель остаются открытыми. [SPEC](../specs/2026-09-29-g02-dotnet-r2r-admitted-modules-v0.1.md), [журнал знаний](knowledge-log.md).


