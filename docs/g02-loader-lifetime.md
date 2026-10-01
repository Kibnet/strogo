# G02: lifetime stream и file-backed загрузки

K-G02-042, baseline b8ba79b. Для signed native observer нужен независимый file identity join; текущий `G02CompiledFixture` использует `LoadFromStream`, поэтому `Assembly.Location` пустой. Механическая замена loader могла бы нарушить уже проверенное exclusive reopen после Dispose. Этот эксперимент проверяет prerequisite; он не завершает signed observer или G02.

Два retained fixtures (scalar/allocation), pinned-path SDK10.0.400/runtime10.0.11 win-x64, три cold child процесса на форму. Каждый копирует exact DLL bytes в свой root, invokes actual `G02CompiledDispatch` на owner witness, проверяет structural equality с owner/reference, источник/копия SHA неизменны. Fresh proof/admission в этом consumer отсутствует.

| Режим | Own held handle открыт | После Dispose/Unload и закрытия own handle | После GC |
|---|---|---|---|
|stream|exclusive reopen refused|reopen succeeds, Location пустой|succeeds даже с rooted dispatch/assembly/context|
|file-retained|refused|refused, Location copied DLL path|refused при KeepAlive strong references|
|file-released|refused|refused пока helper не завершился|weak context collected и reopen succeeded в первом bounded cycle|

Результат одинаков на обеих формах. File-retained refusal — Windows sharing violation HResult0x80070020. Bounded8cycles не означают guaranteed unload time; первый cycle — наблюдение этих запусков. `Unload` инициирует разгрузку, а `G02CompiledDispatch.Dispose` сейчас только выставляет flag, оставляя strong Assembly/MethodInfo. CLR file lock — отдельное владение от package snapshot handles; освобождение одних не доказывает освобождение другого.

[Evidence](evidence/g02-loader-lifetime-20261001/receipt.json): current8source SHA/9binary SHA/6child results; [copies](evidence/g02-loader-lifetime-20261001/copies.json) —17exact raw copies, including initial run history before Process finally improvement. Current build0warnings/errors; all6childrenexit0, timeoutfalse. Source packages unchanged. [Six current file pins](evidence/g02-loader-lifetime-20261001/pin-check.json) checked after observations: SDK executable/hostfxr/CoreLib/coreclr/clrjit/hostpolicy match inventory. Это узкая сверка, не whole SDK closure или loaded-memory attestation.

Команда: `pwsh -File tools/g02-native-observer/Run-LoaderLifetime.ps1 -ScalarPackageRoot <retained scalar session-package> -AllocationPackageRoot <retained allocation session-package>`. Driver20s exit/5s kill/drain, timeout не принимается; actual timeout branch здесь не упражнялся. Clone files остаются private diagnostic evidence; private keys не создаются. UI/video неприменимы.

Следующий design: сохранить ordinary stream-loader и его synchronous owned-handle cleanup. Для diagnostic file-identity observer использовать отдельный internal file-backed path с теми же owner/proof/build/runtime gates, где процесс является явной границей CLR mapping lifetime; parent удерживает package/entry и проверяет terminal child before resource-release claim. Не добавлять optional bypass в public API и не считать adapter counter native callback. До реализации — scoped SPEC/review. Гарантию очистки whole process tree необходимо отдельно проверить, не выводить из этого эксперимента.

Открытые public closure formula, D02 human semantics/release, public CLI/facade и G05/G06 остаются прежними. Production source не менялся; full Admission444 — исторический предыдущий результат, не повторён здесь. Final independent audit pending.

Финальный independent lifecycle audit PASS без новых B/H/M/L:8/8current source SHA,9/9binary SHA,17/17exactcopies,6actual source/clone files и6SDKfilepins сверены. Все6currentchildren exit0/timeoutfalse; JSONstdout совпадает с receipt. Одинаковое наблюдение обеих форм: file-retainedsharing violation0x80070020 afterDispose/GC, weak-onlyreleasedfirstcyclecollected+reopen, streamafterownhandlecloseunlocked. Process finally LOW исправлена, current rerun включает fix. Production source/loader не менялись; новый signed observer design пока не реализован. File/log audit без consumer rerun; danger-full-access, behavioural readonly. Свежая Unlimotion validation по-прежнему isValid=false/одна посторонняя MissingReverseLink, result не записан; общая цель активна.
