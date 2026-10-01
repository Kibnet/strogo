# Diagnostic native observer (Windows x64)

Run from PowerShell7:

```powershell
./tools/g02-native-observer/Run-Observer.ps1
```

Requires the explicitly configured MSVC14.44.35207, NETFXSDK4.8 headers and local `.tools/dotnet-sdk-10.0.400`. Each run creates a fresh ignored evidence directory, builds the native profiler and isolated managed Probe, and checks call/no-call/parallel/overflow controls. Profiling variables are set only on child processes; no COM registry registration is performed. Entry buffer capacity4096; trace limit1MiB; process deadline20s plus bounded cleanup/output waits.

This is a forced-JIT startup fixture. Exact type/method matching is insufficient to establish generated-module identity; no loaded assembly digest/source-map binding, ReadyToRun instruction provenance, public admission or immutable runtime/toolchain closure is claimed. The observer disables native images and inlining. A successful program exit alone does not establish an accepted trace: host checks initialization, PID, mapper joins, shutdown status and expected entries. Overflow refuses the trace.

`FunctionEnter3` executes the assembly hook in Enter.asm, preserving all registers/flags it changes and performing no calls, allocations, blocking waits or file IO. Metadata mapping and final file output occur outside the entry hook. CallbackStubs.inc provides required callback1/2 methods not used by this diagnostic fixture.

Generated scalar metadata probe (retained trusted fixture only):

```powershell
./tools/g02-native-observer/Run-GeneratedObserver.ps1 -PackageRoot <scalar-fixture-package> -ObserverDirectory <fresh-native-observer-output>
```

The package contains `content/generated.dll` and `content/source-map.json` from prior trusted replay; this runner performs no admission. The consumer resolves function/addOne to its generated symbol, checks PE metadata and invokes a long→long scalar directly. Native trace MVID/token joins distinguish a same-name/output decoy. MVID is forgeable; consumer input SHA is not an independent loaded-image digest. LoadFromStream yielded collectible mapped layout, so raw file hashing at module base is unavailable; the rejected experiment is retained as knowledge.

For the held file-backed artifact identity probe, add `-FileIdentity`. This uses LoadFromAssemblyPath, with read-only/no-write/no-delete sharing before CLR load. The native mapper independently opens the CLR-reported module filename, enforces local fixed-drive/final-path/no-ADS/no-leaf-reparse bounds, hashes complete file bytes (≤16MiB) and retains at most32 handles until Shutdown. This is a file artifact binding, not a hash of mapped memory or immutable instruction bytes. File/metadata/PID/FunctionID joins must all pass.

Controls cover a same-length DOS message byte mutation (same MVID/token/output/length, changed SHA), a >16MiB valid overlay, unavailable stream module filename, 33 distinct loaded modules and a nonterminating consumer killed at20s. IO/hash work is outside the entry hook but synchronous in the mapper; 2s checks reject delayed completed reads, while the child deadline supplies the preemptive operational bound. A timed-out process supplies refusal evidence only. Trusted local fixture namespace only; no runtime closure, ACL/adversarial parent namespace guarantee, Q dispatch or public admission.

Shared dispatch probe (two retained fixture vector profiles):

```powershell
./tools/g02-native-observer/Run-DispatchObserver.ps1 -ScalarPackageRoot <scalar-package> -AllocationPackageRoot <allocation-package> -ObserverDirectory <fresh-native-observer-output>
```

This friend-only consumer uses the same internal G02CompiledDispatch as G02CompiledFixture, including strict input/output codec and Q-before-F. The original fixture keeps its composed proof/build gates. This diagnostic consumer performs structural snapshot and source/map regeneration only: it does not verify owner stored identity/signature/state, replay proof/build or admit a public package. Only previously trusted retained local fixtures belong in this experiment. Method binding derives both symbols from IR/contracts; optional STROGO_OBSERVER_METHOD_2 selects the second native mapper target for Q. The new runner requires both Q and F for successful calls, Q alone for failed preconditions and neither for rejected input types, joining ordered entries to metadata/PID/held-file SHA. It checks scalar owner witness plus three boundaries and allocation witnesses plus empty/full256 sequences. Vector profiles cover these two shapes, not the full language or public ABI. Forced-JIT and file identity limits above still apply.

Add `-JsonTransport` to invoke the same generated methods through the internal strict invocation transport described in [g02-invocation-transport](../../docs/g02-invocation-transport.md). Without that switch the original typed invocation path is used. The transport result has no package/admission identity envelope and does not establish a public admitted runtime.

Loader lifecycle prerequisite: [G02 loader lifetime](../../docs/g02-loader-lifetime.md). Run-LoaderLifetime.ps1 compares stream/file-retained/file-released against actual copied fixture DLLs. It does not enable native profiler or establish proof/admission. Ordinary loader is unchanged.

Внутренний signed file-backed вход и его process-bound lifetime описаны в [g02-observer-session](../../docs/g02-observer-session.md). Существующий ObserverFixture пока использует retained unsigned fixture; новый вход ещё не подключён к native trace.
