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
