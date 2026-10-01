# Diagnostic native observer (Windows x64)

Run from PowerShell7:

```powershell
./tools/g02-native-observer/Run-Observer.ps1
```

Requires the explicitly configured MSVC14.44.35207, NETFXSDK4.8 headers and local `.tools/dotnet-sdk-10.0.400`. Each run creates a fresh ignored evidence directory, builds the native profiler and isolated managed Probe, and checks call/no-call/parallel/overflow controls. Profiling variables are set only on child processes; no COM registry registration is performed. Entry buffer capacity4096; trace limit1MiB; process deadline20s plus bounded cleanup/output waits.

This is a forced-JIT startup fixture. Exact type/method matching is insufficient to establish generated-module identity; no loaded assembly digest/source-map binding, ReadyToRun instruction provenance, public admission or immutable runtime/toolchain closure is claimed. The observer disables native images and inlining. A successful program exit alone does not establish an accepted trace: host checks initialization, PID, mapper joins, shutdown status and expected entries. Overflow refuses the trace.

`FunctionEnter3` executes the assembly hook in Enter.asm, preserving all registers/flags it changes and performing no calls, allocations, blocking waits or file IO. Metadata mapping and final file output occur outside the entry hook. CallbackStubs.inc provides required callback1/2 methods not used by this diagnostic fixture.
