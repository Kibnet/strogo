# Shared compiled dispatch — diagnostic evidence

Final local runs, 2026-10-01. `receipt.json` and six child process/trace pairs are exact copies from private `artifacts/local-validation/g02/dispatch-observer-04591bc193e947899687d6a13c72795f`; managed-build.txt is the actual consumer build log. `native/` holds12 exact text/JSON copies from native-observer-50d11f9939e34c1885381e6ef697d3f2. Native binaries remain private; receipt records SHA.

Independent reviewer verified current11/11 source hashes,10/10 binaries,22/22 native input/source/tool identities, exact14+12 copies and raw Q/F sequences. No B/H/M/L findings after SPEC clarification. It read files only, without executing builds/children; actual process runs are parent evidence. Fresh composed conformance ran through the pinned apphost/DOTNET_ROOT and completed PASS233 with exit0; conformance/ contains exact result/stdout plus fresh scalar/allocation compiled-invocations rows from fresh-replay-7ea0a494290745aca1edae0652b04922. conformance/build.txt is the successful Debug0/0 build captured just before that run.

Successful vectors: scalar4 with8 alternating Q/F entries; allocation5 with10. For each fixture requires-invalid gives Q1/F0 and typed-invalid Q0/F0. Host matches native PID/module/function/metadata and native digest of held CLR-reported file.

This diagnostic consumer opens retained previously trusted fixtures with structural snapshot and source/map regeneration only. It does NOT establish owner signature/state identity or new proof/build/admission. Original G02CompiledFixture keeps those existing composed gates and shares only binding/codec/dispatch. Forced-JIT; no mapped-memory digest, ordinary native R2R instruction provenance or full ABI generality claim.
