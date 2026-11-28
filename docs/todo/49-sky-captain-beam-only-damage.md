# The Sky Captain takes damage only from the beam

## Problem

The type-0x25 Sky Captain is not part of the scene's entity list, so the port delivers
damage to it only through the beam sweep. The walker's machine gun and missiles go through
`DeliverShotContact`, which iterates `world.Active` only, so hitting the Sky Captain with
bullets does nothing. With 1000 HP and no gauge in the HUD, a machine-gun burst looks like
"takes no damage at all".

## Evidence

- Port: `src/FutureCop.View/LevelScene.cs:190` registers the captain in
  `Weapons.ExtraHitTargets` once, inside `EnableFiring()`.
- Port: `src/FutureCop.View/LevelWeapons.cs:438` (list), `:490` `DeliverBeamHits`,
  `:524-533` — the only reader of `ExtraHitTargets` (message 14).
- Port: `src/FutureCop.View/LevelScene.cs:1275-1293` `DeliverShotContact` walks
  `world.Active.ToArray()` and sends message 11; there is no `ExtraHitTargets` branch.
- Port: `src/FutureCop.Game/EntityLifecycle.cs:153` (messages 11/14 → base damage),
  `:164-186` (`ApplyBaseDamage`, `8002AA1C.c:79-134`) — the delivery mechanism exists,
  it is simply never reached by bullets.
- Retail: `src/game/gameplay-ai/gTestEntityPairCollision.c:96-99` — on a pair hit both
  entities receive `callback(11, …)`; the drone is a normal table entity in retail, so
  bullets damage it.
- Disc: `PA/CONFT.MIS` Cact `0x25` = 4 records (ids 287, 295, 296, 297), hp 1000, team 2,
  at (1470976, 852480) — the Sky Captain base.
- Consequence in code: `SkyCaptainTarget` (`LevelSceneSkyCaptain.cs:950,964,1006`) lowers
  `Health` only when message 14 arrives.

## Task

Route bullet and missile contacts against `ExtraHitTargets` the way `DeliverBeamHits` does,
or put the drone into the pair-collision table, and stop relying on the one-shot
registration at `LevelScene.cs:190` (re-register when the captain is recreated on rearm).

## Acceptance

- A test fires the machine gun at a live captain for N frames and asserts
  `SkyCaptainTarget.Health` decreased by the summed contact damage; a second test fires
  bullets only and asserts damage > 0.
- Manual: a machine-gun burst visibly lowers the captain's health (with todo 51 in place).
