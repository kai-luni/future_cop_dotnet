# Todo 56: Share and test hover wander targeting

## Problem

The hover initialization and tick paths duplicate the wander-target calculation. A C-parity test against only one copy does not protect the other.

## Evidence

- PS1 `e057e56`: `src/game/gameplay-ai/gPickHoverWanderTarget.c:45-54` is one function (`800320E4`) used by both paths. `tests/host/leaf_century_b_harness.c:269-294` has five checks.
- .NET `fd5abf3`: `src/FutureCop.Game/HoverEnemy.cs:215` and `src/FutureCop.Game/HoverEnemyTick.cs:326` have separate implementations. All five original checks passed under QEMU and against the managed initialization helper in a scratch probe; the tick path was not covered by that probe.

## Task

Have both production paths use the same C-shaped helper. Port the original cases with deterministic random input and check the tick wiring. Coordinate the shared-file edits with PR #27.

## Acceptance

The original state-3 and kind-`0x25` cases leave the target intact; the active case copies Y and applies the expected X and Z offsets. Both production paths use the tested helper, and the blocked cases consume no random values.
