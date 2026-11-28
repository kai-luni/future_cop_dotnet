# Todo 43: Remaining retail environment vectors

## Problem

The managed environment suite uses shared PS1 inputs, but does not exercise all operations in the C harness directly. A passing model projection or bone composition is not a standalone test of those operations.

## Evidence

At PS1 commit `0a82cd1`, `tests/host/env_harness.c:316-348,349-367` checks `TRANSPOSE` (`80019470`), `MULMV` (`8001A81C`), `VIEW` (`80018860`), `LOADROT` (`80010820`), `XF6C` (`8001006C`) and `XF1D4` (`800101D4`). Compare with `tests/FutureCop.Assets.Tests/EnvironmentVectorTests.cs`. The similarly named `XF10C` and `XF234` exercise different output formats or translation modes.

## Task

Trace each operation to its managed gameplay or rendering caller, or identify the missing port. Reuse the corresponding rows from `tests/host/vectors/env.vectors` for that path; do not add an implementation used only by tests. Keep the two rotation conventions separate.

## Acceptance

For each of the six tags, document the managed caller and run its shared vectors on both implementations, or identify the concrete missing caller that blocks the test. Check untouched outputs or register state where the managed API exposes them; distinguish per-vertex from batch coverage.

## Implementation

The six remaining tags were traced to their managed call sites; only two have one. The rows are now part of
`tests/golden/env.vectors` (copied unchanged from `tests/host/vectors/env.vectors` at `0a82cd1`), and the two
mapped tags run against production code, not against a test-only re-implementation.

| Tag | PS1 | Managed caller | Test |
| --- | --- | --- | --- |
| `LOADROT` | `func_80010820` | `Gte.Load` (`func_80010750` packing; `func_80010820` only leaves `w9..w11` untouched, i.e. zero translation) | `RotationLoadPacksTheSameRegistersWithZeroTranslation`, 2 rows |
| `XF6C` | `func_8001006C` | `ScriptRender.Compose` (`func_80013CB8`), the only managed caller of the `func_8001006C` word pipeline; row rotation as parent, row vertices as local rows | `ComposedWordTransformMatchesRetailBatchWords`, 3 rows (count 2/1/0) |
| `TRANSPOSE` | `func_80019470` | **none** | — |
| `MULMV` | `func_8001A81C` | **none** | — |
| `VIEW` | `func_80018860` | **none** | — |
| `XF1D4` | `func_800101D4` | **none** | — |

Why the four have no caller:

- `func_80019470`: the port never transposes a standalone 3x3. The transpose is fused into the two loaders
  (`Gte.Load`: `r[i*3+j] = w[j*3+i]` with the `F` row flipped, `>>4`) and into `ScriptRender.Compose`, which
  consumes the same rotation registers. A `Transpose` method would exist only for this test.
- `func_8001A81C`: no managed code multiplies a 16.16 matrix by an `s32` vector with middle-32-bit products.
  The scene/node placement (`LevelScene`, `WireframeScene`) and the camera run in double precision, and
  `MechMath.Fmul` only serves the 2D local transform in `Elevator.Probe`.
- `func_80018860`: the port replaces the retail view matrix with the viewer camera (`ViewTransform.Turn`, built
  from yaw/elevation in double precision). `Elevator.Probe` inlines the 2D transposed-plus-negated case only, so
  the exact retail integers have no managed producer.
- `func_800101D4`: the `s16` store with translation is unused. `Gte.Mvmva` is `func_8001010C` (`cv = 3`, no
  `TR`), `Gte.TransformWorld` is `func_80010234` (`<<4`, no saturation), and `PlayerAssembly.Point`
  (`func_80017668`) uses the `<<4` form. The MAC of these rows is covered by the `XF234` rows and the `s16`
  saturation by `Gte.Mvmva`/`Gte.Rtps`.

Their rows stay in the shared vector file so the C harness and any future caller keep a single source. No
test-only implementation was added.

## Additional cases

- `func_80015AF4` (`ModelRender.WindingCulled`): the four `triangle-output` steps missing from
  `tests/portable/a1-a2.v1.json` are now `w_collinear_diag`, `w_neg_ccw`, `w_neg_cw` and `w_wide_ccw` in
  `tests/golden/render.vectors`, with the split-half cross product written out in the comment. `RenderGoldenTests`
  already drives every `W` row through the production call.
- `func_800187C8` (`MechMath.IntegerRoot`): all 15 `sqrt-q8` cases (0 … `0xFFFFFFFF`, including the `0xFFFFFF`
  saturation) are in `FixedPointLeafParityTests.IntegerRootMatchesThePortableQ8Cases`.

## Open

- `TRANSPOSE`, `MULMV`, `VIEW` and `XF1D4` stay vector-only. Wiring them would need a retail caller first: the
  mesh/scene path (`func_80031238`, `func_80061CAC`) is not ported, and the camera is deliberately viewer-only.
