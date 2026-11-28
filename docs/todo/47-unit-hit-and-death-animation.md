# Unit animations on hit and destruction are missing

## Problem

Tanks show no animation when they are destroyed — they disappear instead. The same is
presumably true when they are hit.

## Evidence

- Observed by Kai during the playtest of PR #26 (Precinct Assault, 1 player).
- Retirement runs without any animation step: `EntityLifecycle.RetireBase`/`Retire`
  (`src/FutureCop.Game/EntityLifecycle.cs:265-295`) clear flags, notify the retire script
  and the runtime id; nothing plays a death sequence.
- Damage arrives as messages 11/14 in `EntityLifecycle.ApplyBaseDamage`
  (`src/FutureCop.Game/EntityLifecycle.cs:164-180`) and only changes health.
- The csac unit animation hook is a no-op: `CsacUnitSpawner.cs:364` (`AdvanceAnimation`)
  and `LevelSceneSkyCaptain.cs:792` (`RunDeathScript`).
- The dead-slot recycling lives in `EnemyCorpseCleanup` / the sweep, so the entity side is
  present but the visual side is not connected.

## Task

Port retail's death sequence for csac units (types 5, 28/0x1C and the other army types):
which animation clip plays on destruction, which script drives it, how long the corpse
stays, and whether a separate hit reaction exists. Connect it to the existing retire and
damage paths instead of faking a new one, and wire `AdvanceAnimation` for these units.

## Acceptance

- A destroyed unit plays the retail death animation for its model before it is swept;
  clip, frame count and duration are documented with a C reference.
- If retail has a hit reaction, it plays on messages 11/14; if it does not, that is
  documented.
- A test on the real draw path fails without the change; no unit is left behind after the
  sweep.
