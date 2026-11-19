# GPU TV investigation — experimental snapshot, 2026-11-19

## Result and scope

The actual textured Precinct Assault retail scene (terrain and mech) has been rendered
on the Samsung GU43U7099F using the Mali-G52 and OpenGL ES through
`Arm32ManualMapper.dll`. This is a static scene preview with camera movement, not a
playable game. The existing PS1-derived scene/packet path and the software renderer
remain; the GPU rasterizes polygons and textures rather than a CPU-generated bitmap.
The renderer is experimental and is not a complete PS1 ordering-table implementation.

**Neither continuous 60 FPS nor long-term stability is established.** The OnDemand
preview survived one 10:26 run including some camera input, much of it idle. A later
restart was no longer running when checked; its termination cause was not established.
The earlier app and probe directories are retained. No TV protection or system service
was disabled or modified.

## Attempts and observations

| Attempt | Observation | Limitation |
| --- | --- | --- |
| GPU shader/texture probe | The GLES test image ran on the TV, including 60 FPS observations. | Not the game scene; an early run ended in SIGSEGV. |
| CPU scene preview | The textured scene and camera were displayed at roughly 14–15 FPS. | Separate software path; not a GPU measurement. |
| GL bridge and renderer control | A control using the probe's GL thread constructed and disposed the scene renderer after missing Tizen and asset DLLs were copied. | Does not establish long-term scene stability. |
| Shader startup investigation | Removed contextless `WarmUp`; replaced a large texture `stackalloc` with a reusable heap buffer; removed the NUI text HUD. The scene was eventually rendered. | Removing `WarmUp` and `stackalloc` alone did not stop exit 139. The changing shader failure sites and the later improvement after HUD removal do not prove a root cause. |
| Continuous scene rendering | Earlier samples were roughly 45–60 FPS and about 97% of one CPU core. Earlier notes record SIGABRT following `cpuUsageMonitor` messages. | App log copies do not preserve those monitor messages; sender, threshold and cause are not established. Do not infer that CPU usage caused the abort. |
| 35 ms sleep in frame callback | Roughly 18 FPS, approximately 51% of one core; one run lasted more than five minutes. | Scratch-only experiment, not a 60 FPS solution or proof of the monitor's behavior. |
| 15 ms sleep in frame callback | The last recorded window shows 31.2 FPS. | The retained app log does not establish the reason this run stopped or its long-term stability. |
| Cached scene commands | Avoiding repeated scene generation reached individual windows near 60 FPS; one trial subsequently recorded SIGSEGV on `GlViewRenderer`. | Causation is not established; this cache experiment was not integrated. |
| OnDemand frame bursts | Wake both `GLView.RenderOnce()` and `Window.RenderOnce()` on this TV. The integrated preview requests a bounded burst at start, input, resize and movement. One run lasted 10:26 including input, with sampled active windows roughly 43–58 FPS. | Idle time is not continuous rendering. Animation needs its own invalidation; prolonged input and sustained gameplay were not validated. The later restart was not running at the next check. |
| Instrumented continuous comparison | A later process (PID 2327) ran approximately two minutes from process start; sample windows showed 34–55 FPS, `GlViewRenderer` around 65–75% of one CPU core and DALi `RenderThread` around 9–11%. Kernel fault records include SIGSEGV for `wl_egl_thread`, `GlViewRenderer` and the process group. | Fault records do not identify the corrupting operation or prove which fault came first. Not the earlier SIGABRT. |

For the instrumented comparison, process RSS/PSS and swap changed considerably:
one early pair was approximately 86,516/61,566 kB RSS/PSS and 1,844 kB VmSwap,
while a late sample was 45,320/20,358 kB and 30,624 kB VmSwap. These are sampled
values, not a diagnosis. The exported `gpu_mem_total` was around 94,900 in the
reported units; it is a system-wide total, not the app's GPU allocation. No usable
temperature reading was obtained. A CPU frequency observation of 1,350,000 does not
establish thermal throttling. The app log showed rising generation-0 collection counts;
this alone does not prove GC caused the crash.

## Changes included in this snapshot

- OnDemand scheduling and an atomic 120-tick, non-accumulating render budget;
  movement re-arms the budget. A 16 ms timer is a request cadence, not a guaranteed FPS.
- Camera input and viewport updates are transferred to the GL thread with atomic
  operations. The FPS HUD was removed; performance statistics remain in the log.
- A reusable texture buffer replaces the large stack allocation; textures are uploaded
  when encountered, and the scene-ready state is reset with the GL lifecycle.
- Unit tests cover the burst counter and texture upload/recreation using a fake GPU.
  They do not exercise a real TV context loss or prove the full input/timer lifecycle.

A read-only review identified uncorrected risks worth testing: repeated key events can
accumulate a long camera turn and keep rendering after release; an upload error can
leave a texture registered and prevent retry; focus handling and the first frame after
idle also need device verification. These are not established causes of the crashes.

## Reproducibility, tests and next work

The current preview was deployed separately under
`/opt/usr/home/owner/share/tmp/fclevel-gl-integrated-20261119/app`. The software
fallback is under `fclevel-fps2-20261119/app`; the unchanged GPU reference probe is
under `fcgpu-probe-20261119/app`. Validate the process command line before controlling
any running TV process. The scene screenshot was saved locally as
`~/.hermes/cache/scratch/fc-gpu-integrated-final.png`.

A fresh Release run in a scratch copy passed 95 GPU tests, 137 asset tests, and 141
host C level vectors; the Tizen project built successfully with two warnings. The C
harness uses original C and retail GTE assembly with a GTE reference-model trap, not
PS1 hardware. These tests do not establish hardware stability, full visual parity, or
a live game. Logs were retained outside Git under
`~/.hermes/cache/scratch/fc-gpu-*-20261119.log` and
`~/.hermes/cache/scratch/fc-60fps-investigation/`; the scratch cache can expire, and
app logs are cumulative, so use timestamps and process IDs when associating lines.
The system log records the PID 2327 faults; dlog buffers yielded no usable monitor cause.

Still needed: preserve a directly attributable SIGABRT/monitor trace; investigate the
SIGSEGV and lifecycle without assuming the fault site is the origin; measure comparable
40/45/50 FPS runs, GPU process memory and temperature where possible; characterize GC
and per-frame work; test prolonged camera activity and visual output against the
software reference. Preserve the CPU fallback, probe and TV safeguards throughout.
