# Todo 45: Entity command entry pass must walk the retail active table

## Problem

The entity command entry pass iterates the managed unit collection instead of the retail active-entity table. Entities that retail would have culled for this frame still receive their queued commands, so commands can execute earlier than in the original, and the documented table address in the parameter documentation is wrong.

## Beleg

- Opus review of PR #25 (`96f4a69`), finding 2.
- `src/FutureCop.Game/PurchasePadSystem.cs:1145` and `:1154`: the pass enumerates `world.Units`; the parameter doc names `D_8009FA40`.
- PS1 reference (`0a82cd1`): `src/game/gameplay-ai/8004CECC.c:67-241` walks the active table `D_800A8F90`, which is rebuilt each frame and excludes culled entities.
- Retail `gApplyRadialContactDamage.c:63-107` uses the same active table, so the port already has a matching concept elsewhere.

## Aufgabe

1. Determine which managed structure corresponds to the retail active table `D_800A8F90` and use it for the entry pass instead of `world.Units`. A candidate already exists: `PurchasePadSystem.cs:805-812` publishes the frame's `D_800A8F90` as `TargetCandidates` (active entities plus the linked live player entities); check whether it is the right set and whether it is populated before the entry pass runs.
2. If no such structure exists yet, introduce it in the frame pipeline rather than filtering inside `PurchasePadSystem`, and wire the existing cull step into it.
3. Fix the parameter documentation so it names the table actually used.
4. Add a test where an entity culled for the frame keeps its queued command pending and executes it after it becomes active again; a second test asserts that a non-culled entity executes within the same frame as today.

## Akzeptanz

- The entry pass runs over the same set of entities retail would process for that frame.
- Commands for culled entities stay queued instead of executing early.
- Both tests fail without the change; `FC_REQUIRE_RETAIL=1` Assets and Gpu suites pass with 0 failed and 0 skipped.
