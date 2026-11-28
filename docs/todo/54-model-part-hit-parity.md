# Todo 54: Retail model-part hit detection

## Problem

The projectile contact path treats a coarse box overlap as a hit where the PS1 performs an additional model-part test. The existing outpost harness cannot detect this difference because it substitutes a hit for that test.

## Evidence

- PS1 `e057e56`: `src/game/gameplay-ai/gTestEntityPairCollision.c:91-100` calls `gTestPartPairHit` (`func_80061CAC`) after the box check and sends message 11 only when the returned hit is nonzero.
- .NET `fd5abf3`: `src/FutureCop.Game/EntityLifecycle.cs:642-669` stops at the box test; `src/FutureCop.View/LevelScene.cs:1153-1170` uses that result to dispatch contact. The C harness in `tests/host/outpost-retirement/harness.c:86-87` returns a fixed nonzero hit.
- Todo 43 records the missing part-test caller as a blocker for environment vectors. This is not a demonstrated cause of the Sky Captain playtest symptom.

## Task

Start with a fixture that runs the real C part test. Determine the model data and transforms required by the retail projectile contact path, then add the corresponding detail check behind the existing coarse test in .NET.

## Acceptance

- Unchanged PS1 C and the .NET production contact path agree on two cases that both pass the coarse test: intersecting parts and disjoint parts. Only the intersection dispatches message 11.
- Record which retail part and hit-result fields the managed caller consumes. Do not count the fixed-hit stub as evidence for the detail check.
