# Sky Captain cannot be hit (animations missing as well)

## Problem

The Sky Captain takes no damage at all — shots and beams pass through it. Its animations
are missing too.

## Evidence

- Observed by Kai during the playtest of PR #26 (Precinct Assault, 1 player).
- The player damage path only delivers into the entity world: `DeliverBeamHits`
  (`src/FutureCop.View/LevelWeapons.cs:482-520`) iterates `world.Active`, i.e. the
  `EntityWorld` table wired through `LevelWeapons.BeamHitWorld` (`LevelScene.cs:147`).
- The Sky Captain and its drone are held in a separate `SkyCaptainSystem`
  (`src/FutureCop.View/LevelScene.cs:199-206`) and are therefore never a hit candidate.
- Its hooks are stubs: `LevelSceneSkyCaptain.cs:786-795` returns `null` from
  `FindTargetEntity`, and `RunDeathScript`, `AdvanceAnimation`, `StepAttackScheduler` are
  empty; `CsacUnitSpawner.cs:355,364` do the same for the csac path.
- Known follow-up from PR #26: model 49 (started by the Sky Captain) has 11 frames and one
  clip of duration 870 but is drawn statically; model 56 (the Sky Captain itself) is
  static in retail.

## Task

Build the retail path that makes hover enemies damageable: how retail's target/candidate
search (`func_8004E80C`, `8004E80C.c:91-209`) decides that a hover entity is hit by a
player shot, where the damage lands, and which death script runs afterwards. Wire the
hover animation advance (`func_80044B34` + `func_800175F8`, `HoverEnemy.cs:87`) so the
animations play, and cover both the Sky Captain and the drone.

## Acceptance

- A shot or beam from the player hits the Sky Captain on the real path, reduces its health
  and triggers its death script; the mechanism is documented with a C reference.
- Animations of the hover enemy and its drone play with retail's frames and rates; the
  static model 49 case is resolved or explicitly justified.
- A test on the real draw/hit path fails without the change.
