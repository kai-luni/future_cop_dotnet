# The home-base purchase pads have no visible representation

## Problem

The P1 base's two purchase pads are a type-14 trigger attachment plus a few type-95 zones;
all of them are invisible volumes. Retail makes them readable through the base's static
props (Cact type 96 → deferred scene asset), which the port never loads, and the port's own
pad marker is an opt-in debug overlay. Standing in the base there is nothing to see, which
is what "the elements to build tanks and helicopters are missing" looks like.

## Evidence

- Disc: `PA/CONFT.MIS` type 14 — id 47 at (593664, 897286), scripts `[0x63F]`; id 51 at
  (563712, 856582), scripts `[0x65F]`; both about 2–3 world units from the P1 spawn cluster.
- Disc/RPNS (pool at file 0x68, length 0x710): `0x63F` =
  `if (gs[25] < 49 && ScriptVars[18] > 1) { spawnerCmdById(7, 71, 0); gs[25] -= 50; ScriptVars[18] = 1; event }`;
  `0x65F` is the same with spawner 8 and `ScriptVars[20]`. Spawner 7 = type 28 (tank),
  8 = type 9 (helicopter).
- Retail: `src/game/memory-resources/gLoadActorChunk.c:106` `case 0x5F:` (map zone),
  `:109-110` `case 0x60: gLoadDeferredSceneAsset(arg0);` — type 96 (0x60) is the
  deferred-scene prop loader (extern at `:54`, `func_80084110`), which spawns through
  `gSpawnSceneNode`.
- Disc: 89 type-96 records in CONFT, including 5 props with Cobj 62 at grid (8,12)…(9,13) —
  the pad cells — plus 2 type-11 props (Cobj 40/41) at (8,13).
- Port: `src/FutureCop.Game/MapObjectRegistry.cs:445` `PlaceMissionObjects` →
  `CreateMissionObject` (`:487+`) handles only `Elevator` and `MountedTurretCarrier`;
  type 96 is dropped. No deferred-scene/`gSpawnSceneNode` path exists in the port.
- Port: the pads themselves do load (`PurchasePadSystem.cs:595` MapZone, `:617`
  TriggerAttachment) and are evaluated every frame (`LevelScene.cs:713-755`), but the only
  marker is `ShowPadMarkers` (`LevelScene.cs:1008`, `:1802`).
- Unproven: which Cobj id is the pad model, and how the five Cobj-62 props split between
  pad machinery and base clutter.

## Task

Port the type-96 branch: read the Cact `0x60` records, resolve their Cobj model, place them
as static scene nodes (camera-culled like the deferred-scene path), and additionally draw
the purchase-pad volumes in the normal render path instead of only under `ShowPadMarkers`.

## Acceptance

- A test loading `PA/CONFT.MIS` places the 89 type-96 records (count assertion) and at least
  the props at grid (8,12)–(9,13).
- Manual: the base shows the pad machinery, and walking into them still buys a tank (pad 47)
  and a helicopter (pad 51) for 50 points each.

## Done

- Port of `gLoadActorChunk.c:106-110` `case 0x60: gLoadDeferredSceneAsset(arg0)`:
  `MapObjectRegistry.PlaceDeferredSceneAssets` + `SceneProp` (`src/FutureCop.Game/MapObjectRegistry.cs`).
  It reads the `Cact` 0x60 records, resolves the first `aRSL` `Cobj` entry the way
  `gLoadDeferredSceneAsset.c:53-57` does via `func_80019BF0(entry.group, entry.name)`, and places a
  static node with the transform of `gSpawnSceneNode.c:292-357`: position from `tACT+0x10/+0x18`, ground
  snap through `LevelGround.PlacementHeight` for every alignment code except -99, lift
  `(s16 +0x1A) << 4`, basis `gBuildRotationMatrix(+0x14, +0x18, +0x16)` with the Q6 row scales and the
  `+0x22` upright correction. Not registered in `D_8009F800` (retail never enters entity creation here).
- Viewer pass: `LevelScene` draws the props inside the frame's 5x5 cell window, bucketed by
  `(x >> 17, z >> 17)` exactly like `gSpawnSceneNode.c:429`, culled by `gDrawCellWindowModels.c:91-146`.
  `VisibleSceneProps` / `ScenePropPrimitives` report the frame.
- The two home-base type-14 pad volumes (`LevelScene.HomeBasePurchasePadIds` = 47, 51) are outlined in
  every frame; the remaining walker pads stay behind `ShowPadMarkers`. `BasePadLines` counts the new
  pass, `PadMarkerLines` the debug pass; the sets are disjoint.
- Digits on `PA/CONFT.MIS`: 89 type-96 records placed; models `55x24, 61x2, 62x47, 63x4, 64x8, 65x2,
  66x2`; all ground-snapped to y = 0 in the base area. The five Cobj-62 props of the todo grid
  (8,12)..(9,13) are ids 194, 195, 196, 197, 223, all yaw 1024, scale 0x40/0x40/0x40, and all share
  scene bucket (4,6). That todo "grid" is `x >> 16`; the scene bucket is `x >> 17`.
- Standing at pad 47: 17 of the 89 nodes fall inside the 5x5 window and queue 19 faces; the two pad
  boxes give 12 outline lines.

## Split: pad machinery vs base clutter

The pad machinery is *not* type-96. Every type-14 purchase pad has a type-11 neighbour within 1.5–4.8k
world units, and those are the pad machines: Cobj 35 stands next to pads 47 (record 50, 1536 units) and
53 (record 54, 3714), Cobj 36 next to pads 51 (record 52, 4738) and 55 (record 56, 2560). The pairing
matches the RPNS scripts (47 = spawner 7/tank, 51 = spawner 8/helicopter). The further type-11 pair
Cobj 40/41 (records 10/11, 17/32) sits 19–25k units out and repeats on both bases; the Cobj 27/28/29/33
records are the other bases' pads. Type-11 goes through the default loader branch
(`gLoadActorChunk.c:145-168`, `gGetEntityHandlerForType(0x0B)` + `gSpawnQueuedEntityAndGetId`), a
different path that is still unported, so the machinery models themselves stay invisible for now.
The five Cobj-62 props in the pad cells are base architecture (one of 47 Cobj-62 nodes spread over the
whole level), not pad models.

## Open

- Type-11 pad machinery entities (Cobj 35/36/40/41) — separate loader branch, needs the 0x0B entity
  handler.
- Per-frame spin and sequence animation of a node (`gDrawCellWindowModels.c:149-238`): 12 of the 89
  CONFT records carry a non-zero `+0x22`, and the `+0x1D` animation flags are ignored (every `+0x1E`
  rate is 0). The upright correction those bytes select *is* applied. The per-node subcell band test
  (`mask & node->unk4E`, `:96-100`) is not modelled either.
- The 250-entry scene-node pool retry of `gSpawnSceneNode.c:236-243` is not modelled (89 nodes here).
