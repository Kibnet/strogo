# G02 owner process high-water: before-fix counterexample

Raw owner-process-before-6c0dbfa2f5f74fd7b0a467ab45b452b2, PID348180, terminal exit0. This is successful reproduction of a missing invariant, NOT PASS of the proposed fix. Production OwnerTrust remains instance-local.

Disposable signed fixture states0/1 and public key from scalar fresh-replay-274a65902ae54d5fb22bd287bab61b29. Probe creates an isolated host config/store, verifies state0, atomically replaces with valid signedstate1, observes epoch1 on hostA, restores signedstate0. Same host rejects OwnerStateRollbackDetected; concurrently alive hostB same pinned key/store accepts0. After hostA/B Dispose, hostC accepts0. No private keys exported; original descriptor/state store unchanged.

copies.json contains12byte-exact files. source-binary-audit pins probe source/project/current OwnerTrust/HostContext and loaded module binary. Exact public signed inputs remain in original raw fixture, with digests in report; key and final clone state0 copied. Host config references original clone absolute location, intentionally historical. Copying this project under docs changes its relative ProjectReference; reproduce at the recorded raw location or explicitly adjust host project reference, without rewriting historical copies.

Initial diagnostic build failed because ProjectReference had one excess parent directory; history-build-failed.txt retained. Corrected four-parent reference then actual run exit0; PowerShell parser correction produced no process run/evidence and is described in journal, not fabricated as raw log. No full Admission/build0/0 rerun claimed for this documentation/reproduction checkpoint.

Next: implement approved process-local B0 design, meaningful cross-host/ALC/concurrency/cap controls and full regression with independently keyed test timelines. Public closure/human D02/admission/G05/G06 remain separate open gates. Independent evidence review pending.

Independent pre-fix evidence audit PASS:12/12exactcopies,5/5source/binary SHA, PID348180/exit0/run/report match. Same host rejectsrollback, liveB and newC accept0. Probe observations gapReproduced=true не assertions; next post-fix automated regression must explicitly require OwnerStateRollbackDetected forB/C. Это beforefix evidence и review preparation, не implementation/security PASS. Reviewer source/evidence readonly при danger-full-access, no rerun.
