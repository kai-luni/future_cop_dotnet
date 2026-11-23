# GPU probe: system .NET 6 versus the ARM32 mapper

Date: 19 November 2026. This is an investigation, not a verified fix.

## Setup

The original `FcGpuProbe.Tizen` source was copied to a scratch workspace; only its
project target framework was changed from `net10.0` to `net6.0`. The same built
assembly was used for both full-probe variants, retaining the original GLView,
shader, texture upload and draw code. SHA-256 of the host build:
`01ca148aeb05047cf34e0b94adfc3919a4b7a26d43bf46fd6b623298cf97ccdc`.
A used `/usr/bin/dotnet` and system CoreCLR 6.0.9 directly, without the mapper.
B used `/usr/bin/dotnet` to host the existing ARM32 mapper and CoreCLR 10.0.15.
The assemblies, GL calls, timer target and display were the same, but hosting and
runtime are necessarily different. The probe logged the expected CLR version.
The mapper also sets ReadyToRun and server-GC switches; those settings need to be
verified against the direct-run environment before attributing timing differences
solely to the hosting path. An Opus code review also flagged a possible ARM32
floating-point ABI mismatch for the probe's `glClearColor(float, ...)` call; actual
argument values and output colours were not captured and the mismatch is not proven.
Thus identical source and assembly do not alone prove identical native arguments.

A second, separate scratch-only binary preserved GLView, callbacks, timer and frame
counter but removed all **application** GLES initialization and draw calls. The NUI
GLView and display system can still perform graphics operations internally. Its
SHA-256 was `46507e5083ac0f087b962d60119300d2510c5f0cb751d154483796d5fcad40dc`;
both minimal variants used that same binary. The original reference probe and CPU
fallback were not modified. A wrapper collected periodic process statistics, maps,
thread records, app logs and `dmesg -W`, and checked the prior process was gone.
The TV checksum command later timed out; the hashes above are for the host build,
not an independent post-transfer TV checksum.

## Full probe results

| Run | Runtime | Requested | PID | Last frame logged / finish | Outcome |
|---|---|---:|---:|---|---|
| A2-02 | direct 6.0.9 | 20 s* | 4083 | 1,066; finished | exit 0 |
| A2-03 | direct 6.0.9 | 1,800 s | 1584 | 952; 60 FPS | SIGSEGV, exit 139, ~18 s |
| A2-04 | direct 6.0.9 | 120 s | 407 | 1,317; 60 FPS | SIGSEGV, exit 139, ~25 s |
| A2-05 | direct 6.0.9 | 120 s | 1975 | 1,275; 60 FPS | SIGSEGV, exit 139, ~43 s |
| B2-01 | mapper + 10.0.15 | 120 s | 2557 | 7,064; finished | exit 0 |
| B2-02 | mapper + 10.0.15 | 120 s | 1583 | no frame logged | SIGSEGV, exit 139, ~1 s |
| B2-03 | mapper + 10.0.15 | 120 s | 3240 | 5,264; 60 FPS | SIGSEGV, exit 139, ~90 s |

Times are approximate differences between the first app log and the shell exit;
frames are the last reported count, not necessarily the final rendered frame.
The A2-05 application log stopped roughly 20 seconds before process exit. FPS
is from the probe's periodic windows, not a whole-run average. `*` A2-02 was intended
as 120 s, but an initial wrapper substitution bug left the probe's default at 20 s;
the logged duration and normal exit are real, and this run is not a 120 s success.
The failed A2-01 launch was a wrapper command-encoding error, not an application run.

## Minimal GLView results

Each run requested 120 seconds, using the same minimal binary for both runtimes.

| Run | Runtime | PID | Last frame logged / finish | Outcome |
|---|---|---:|---|---|
| A1-01 | direct 6.0.9 | 1588 | 7,060; finished | exit 0 |
| A1-02 | direct 6.0.9 | 1536 | 7,069; finished | exit 0 |
| A1-03 | direct 6.0.9 | 1545 | 4,580; 60 FPS | SIGSEGV, exit 139, ~78 s |
| B1-01 | mapper + 10.0.15 | 3579 | 7,089; finished | exit 0 |
| B1-02 | mapper + 10.0.15 | 3431 | 7,057; finished | exit 0 |
| B1-03 | mapper + 10.0.15 | 3618 | 7,066; finished | exit 0 |

Here the frame count is the number of GLView callbacks, not completed application
GPU draw calls. Two-minute successes do not establish long-term stability. No 30-minute
run completed. No SIGABRT was observed in the investigated application runs; this does
not explain or rule out the earlier separate SIGABRT failure mode.

## Evidence and interpretation

Both hosting paths produced SIGSEGV using the same full probe. In addition, one
system-.NET-6 run crashed without the app making any GLES calls. Therefore neither
the manual mapper, a second CoreCLR, the Future Cop scene, the shared GPU renderer,
shader compilation nor texture uploads are necessary for every observed crash. This
does not exclude a *separate* mapper, shader, driver or renderer defect, or prove
that these crashes share a first cause. The minimum case still uses NUI, DALi,
GLView and the TV graphics stack, so none of those components is isolated as cause.

A2-03's observed PC mapped to system CoreCLR 6 at offset `0x2feece`, not the
previously observed `0xebcc2`. A2-04 and minimal A1-03 had reported PCs in libc;
A2-05 in the dynamic loader. B2-02 reported a `mali-compiler` fault during startup.
B2-03 included a fault on `GlViewRenderer` at `0xb4a0b100`, inside an anonymous RWX
mapping at offset `0x100`, with LR `0xb4a0b0cc`; the previous mapper analysis linked
this instruction pattern to its TLS hook. Registers `r6=0x010e01c0`, `r5=5` and
fault address `0x010e01d4` are consistent with the previously decoded indexed
load. This is an observed fault, not proof of the initial fault or a mapper root
cause. CoreCLR signal handling may produce secondary or recursive faults. Kernel
stack-overflow messages alone do not establish whether overflow was initial.

Samples were taken approximately every 10 seconds, so CPU and memory values are
coarse and may miss peaks or early faults. For example, A2-03 sampled peak RSS
38,192 kB, PSS 12,497 kB and swap 4,472 kB; B2-01 sampled peak RSS 62,040 kB,
PSS 36,965 kB and swap 15,412 kB. First-to-last sampled process CPU was about
41% and 26% of one core respectively (250 clock ticks/s); these are not necessarily
steady-state measurements. Per-thread `/proc` records and maps are in the evidence.
The first native `si_code`, complete backtrace and causal fault sequence remain
unestablished. Kernel event recording is bounded and may be affected by device
logging; its lines should not automatically be interpreted as the first fault.

The earlier unchanged mapper-probe crash also faulted in the mapper's TLS array
load (`NativeHooks.cs:187`): its PC and register-derived address match that code.
The simultaneous host-CLR fault occurred independently of that hook. Neither
observation determines which event was first or whether the hook caused either.
The independent Opus review supports this interpretation; its source trace is
retained with the investigation evidence, not treated as a runtime experiment.

## Address-space observation from the later Opus review

In A2-05, `/proc/1975/maps` at 14:45:38 still began with `/usr/bin/dotnet` at
`0x004a2000` and contained the heap; the samples at 14:45:49 and 14:45:59 instead
began at `0xac25c000`, within the executable mapping for libdbus. The kernel
crash dump also begins with that partial mapping. The process did not report exit
until 14:46:02: the ~43 s in the table measures process termination, not when the
failure began; its last application frame was logged at 14:45:41.863.

A2-03's last pre-crash sample had a libdbus executable mapping beginning at
`0xac1f9000`; the crash dump begins at `0xac20c000`, partway through the same
mapping. Immediately before the logged SIGSEGV sequence the Mali driver reported
a missing CPU mapping. These observations are consistent with lower mappings
being removed, but the exact timing, caller and initial fault remain unknown.
Kernel dump completeness alone cannot prove it; the A2-05 `/proc` observation is
stronger evidence in that particular run. A large erroneous unmap is one hypothesis,
not a demonstrated cause; process teardown and diagnostic artefacts must also be
excluded. The proposed next step is non-invasive observation of mappings and, if
permitted, the relevant system calls; no TV protection should be changed.

## Independent AGY review of the address-space evidence

In all four direct-CLR6 crashes examined (A2-03, A2-04, A2-05 and the minimal
A1-03), the kernel's first listed remaining mapping begins in `libdbus-1.so` at
file offset `0x13000`. A2-05 independently showed that same partial mapping in
`/proc` twice, ten seconds apart, with all listed threads in `D` state and a
reduced virtual size. This makes a one-off partial read an inadequate explanation
for those two snapshots, though it does not establish what removed the mappings.

The process was still visible for about 20 seconds after the logged fault, but this
is not evidence that it continued running normally: the kernel log shows a wait in
`Extended-Mincore` while handling the crash. The exact `libdbus` boundary is a
repeatable observation, **not evidence that D-Bus caused the fault**. The original
fault, ordering and mechanism remain unproven; pre-fault unmapping, fault handling,
and device-specific behaviour remain to be distinguished. An `R`/`S` snapshot
alone would also not prove a fault had not already occurred. The mapper crash B2-03
had a different first listed mapping. Keep the AGY review alongside the raw logs.

## Follow-up TV sampling, 2026-11-19

The unchanged direct-CLR6 full probe ran three additional 120-second trials with
exit 0. The first attempted local observer was rejected by the TV as an unsigned
script; its observer data is therefore invalid. TV protection was left unchanged.
Two subsequent trials used external read-only `/proc` snapshots instead (118 and
91 snapshots). Their exit codes were 0, and the second logged 7,036 frames. The
snapshots caught no SIGSEGV and no sampled `munmap`/`mremap`; infrequent snapshots
cannot rule out those short system calls. One sampling harness continued polling
after completion because its first `DONE` check required a preceding newline;
this was corrected for the next run. These short runs do not establish stability
or resolve the missing-mappings observation. Raw evidence is in
`gpu_runtime_ab_20261119/address-watch-01/` and `address-samples-{01,02}/`.

## Next controlled step

An independent read-only source review found all seven shared `src` projects currently
target `net10.0` with C# language version 10 and did not find obvious post-net6 APIs
in its targeted search. This makes multitargeting worth testing, but is not a build
or compatibility proof. The direct probe itself did build and run on system .NET 6,
but its instability means that a full migration would be premature.

Investigate the common native GLView/NUI and display path with a matched minimal
native-Tizen GLView control, then separate window/GLView lifetime and callback
transitions. Compare start/exit cycles and background native thread faults without
changing TV protections. Record the very first attributable signal where the device
allows it. Keep SIGABRT as a separate investigation. The engine's net6 portability
and supportability have not yet been established by a complete build or runtime test;
a future multitarget change must be validated rather than assumed. There is no proven
root cause, no targeted fix, and no justification yet for replacing the mapper.

Evidence: `~/hidrive/linux_sync/operations/future_cop/gpu_runtime_ab_20261119/`,
with one directory per run; files include launch, app, kernel, stdout, samples and
exit status. A copy of the controlled runner and minimal-source delta is retained
there; it contains local TV startup environment details and should not be published
without review. The original report and prior evidence are retained separately.
