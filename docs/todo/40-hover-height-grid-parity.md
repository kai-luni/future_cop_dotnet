# Use the retail height-grid sampler in the hover simulation

## Problem

The hover tick asks for a bilinear height, but the two runtime adapters supply a point ground probe instead. This is a porting gap, not yet a measured gameplay symptom. It is independent of the orientation and lighting issues in tasks 38 and 39.

## Evidence

- `src/FutureCop.Game/HoverEnemyTick.cs:122,249` consumes `IHoverEnemyWorld.SampleHeightBilinear`. `src/FutureCop.Game/CsacUnitSpawner.cs:339-341` and `src/FutureCop.View/LevelSceneSkyCaptain.cs:772-773` substitute the ground callback. `tests/FutureCop.Assets.Tests/HoverEnemyWorldStub.cs` supplies a constant: it can verify the consumer, not the sampling contract.
- PS1 reference `0a82cd1`: `src/game/utilities/gSampleHeightGridBilinear.c:59-89` (`func_80069A38`) samples four **signed** height bytes using 14-bit fractions, two successive rounded-down interpolation stages, and returns `0x8000` outside its bounds. See `gBuildCoarseHeightGrid` for the producer and `gUpdateHoverEnemy` for the original consumer.

## Task

Trace grid construction and lifetime for a real mission, port or reuse the exact grid representation and sampler, and wire both runtime consumers. Keep the separate point-ground probe for retail callers that actually use it.

## Acceptance

- Transfer the seven existing checks for `func_80069A38` in `tests/host/leaf_more_harness.c` (QEMU: 7 checks, 0 failures). Run identical inputs against the unchanged C sampler and the port: a grid with distinct signed heights, unequal nonzero fractions, a cell boundary, and an out-of-range point. Compare exact integers, including the sentinel.
- A scene-path test distinguishes the bilinear result from a point probe in the hover tick; replacing it with the old callback must fail. Check both runtime adapters without asserting a visual symptom in advance.

## Implementation

- `src/FutureCop.Assets/HeightGrid.cs` ports both halves of the retail height field.
  - Builder (`gBuildCoarseHeightGrid`, `func_800695A8`): one grid cell per `Cptc`/`GRDB` header cell inside the border, 8x8 samples per cell, each sample the **maximum** of the 2x2 vertex group (12 signed bytes at `vertex` and `vertex + 0x33`), then a 3x3 binomial (1,2,1) tap with edge replication and a final `max` against the centre sample.
  - Sampler (`gSampleHeightGridBilinear`, `func_80069A38`): the `D_800A2940` descriptor layout (origin `+0x00`, row stride `+0x04`, last x cell `+0x08`, sample pointer `+0x0C`) lives in the instance; signed samples, 14-bit fractions from `pos[0]`/`pos[2]`, two successive rounded-down interpolation stages, `height << 8` as the result and the `0x8000` sentinel outside `origin .. origin + (last + 1) * 0x4000`.
- `src/FutureCop.Game/CsacUnitSpawner.cs`: `CsacHoverWorld` takes an optional `HeightGrid` and answers `SampleHeightBilinear` from it; `GetGroundHeight` keeps the point probe `func_80066F78`. The spawner exposes `HeightGrid`, and `LevelScene` fills it from the scene's grid, so type-9 units built through `CsacUnitSpawner` see the same field as the retail path.
- `src/FutureCop.View/LevelSceneSkyCaptain.cs`: `SkyCaptainSystem.BindMap` builds the grid (`func_800695A8` runs at load time in retail) and `TickWorld.SampleHeightBilinear` samples it, while `GetGroundHeight` still calls the ground callback for the `gGetGroundHeight` uses (`800324E0.c:368`). `TickWorld` is `internal` so the scene-path test can drive the real adapter.
- No point-probe call site was replaced: only the bilinear consumer changed.

## Tests

`tests/FutureCop.Assets.Tests/HeightGridTests.cs` (8 tests):

- `SamplerReproducesTheRetailHarnessChecks`: the seven `@leaf 80069A38` checks of `tests/host/leaf_more_harness.c` with the same descriptor (`origin 0x8000`, stride 3, last cell 1, heights `-20 12 28 60 -4 20 100 76 52`) and the same probes, compared as exact integers including `0x8000`.
- `SamplerRoundsEachInterpolationStageDown`: `dz = 1` contributes nothing and `dx = 0x3FFF` yields 20478, not 20480.
- `SyntheticMapDescribesTheGridItPromises`: the Cptc/Ctil fixture maps to the intended header, cell count, `origin` and `lastCellX`.
- `BuilderFiltersTheCoarseBlockAndKeepsItsPeak`: a single 100 inside a -50 field, written into the **last** byte of a 2x2 vertex group, must survive as 100 at the group centre and as -41 one sample up-left, which pins both the group maximum and the binomial weights.
- `ConftGridMatchesItsHeaderAndStaysWithinTheTerrainRange`: `PA/CONFT.MIS` grid size/`origin`, every sample inside the signed range of its 3x3 cell neighbourhood, and a non-constant field.
- `CsacHoverAdapterSamplesTheGridAndKeepsThePointProbeSeparate` and `SkyCaptainHoverAdapterBuildsTheGridAndDistinguishesItFromThePointProbe`: the adapter returns the bilinear value, the point probe returns the flat one, and the descent state (`800324E0.c:200-210`) only leaves at the grid height.
- `ConftSceneWiresTheGridIntoBothHoverAdapters`: real CONFT scene, `SkyCaptainSystem.HeightGrid` is the instance the spawner uses, a shipped-terrain point exists where grid and probe disagree, both adapters report the grid value there, and the live hover tick runs against it.

Both sides ran the same inputs: `tests/host/leaf_more_test.sh` (unchanged units, `OUT` redirected outside the C
tree) reports `LEAF func_80069A38 checks=7 failures=0` and the .NET tests above assert the identical integers.

Evidence that the wiring is required: with `CsacHoverWorld.SampleHeightBilinear` and `TickWorld.SampleHeightBilinear` temporarily reverted to the ground callback, the three adapter/scene tests fail (3 failed, 5 passed); with the wiring in place all 8 pass.

## Open

- `func_800695A8` is not pinned by QEMU: `tests/host/LEAF_SUITES.md:23` excludes it because its register temporaries overlap and its sample indices run past the 64-byte block the harness can build. The builder is therefore derived from the `asm/us/main.s` listing and checked through the synthetic fixture and the CONFT range invariant, not through a pinned harness vector.
- The last sample row and column are interpolation endpoints only; probing them directly returns `0x8000`, as retail does.
