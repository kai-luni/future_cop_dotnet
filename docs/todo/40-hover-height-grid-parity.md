# Use the retail height-grid sampler in the hover simulation

## Problem

The hover tick asks for a bilinear height, but the two runtime adapters supply a point ground probe instead. This is a porting gap, not yet a measured gameplay symptom. It is independent of the orientation and lighting issues in tasks 38 and 39.

## Beleg

- `src/FutureCop.Game/HoverEnemyTick.cs:122,249` consumes `IHoverEnemyWorld.SampleHeightBilinear`. `src/FutureCop.Game/CsacUnitSpawner.cs:339-341` and `src/FutureCop.View/LevelSceneSkyCaptain.cs:772-773` substitute the ground callback. `tests/FutureCop.Assets.Tests/HoverEnemyWorldStub.cs` supplies a constant: it can verify the consumer, not the sampling contract.
- PS1 reference `0a82cd1`: `src/game/utilities/gSampleHeightGridBilinear.c:59-89` (`func_80069A38`) samples four **signed** height bytes using 14-bit fractions, two successive rounded-down interpolation stages, and returns `0x8000` outside its bounds. See `gBuildCoarseHeightGrid` for the producer and `gUpdateHoverEnemy` for the original consumer.

## Aufgabe

Trace grid construction and lifetime for a real mission, port or reuse the exact grid representation and sampler, and wire both runtime consumers. Keep the separate point-ground probe for retail callers that actually use it.

## Akzeptanz

- Transfer the seven existing checks for `func_80069A38` in `tests/host/leaf_more_harness.c` (QEMU: 7 checks, 0 failures). Run identical inputs against the unchanged C sampler and the port: a grid with distinct signed heights, unequal nonzero fractions, a cell boundary, and an out-of-range point. Compare exact integers, including the sentinel.
- A scene-path test distinguishes the bilinear result from a point probe in the hover tick; replacing it with the old callback must fail. Check both runtime adapters without asserting a visual symptom in advance.
