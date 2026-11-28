# Entity command: type filter and queue

## Problem

The 0x3D dispatch differs from retail in two points: the type filter is missing, and the command is executed immediately instead of being queued. As a result the retire script runs nested and can overwrite score changes of the calling script.

## Evidence

- Port: `src/FutureCop.Game/PurchasePadSystem.cs:260-283`.
- Retail: `8004DA84.c` drops commands addressed to types 2/3/4/7/15/23 and filters 18/21.
- Retail: `8004DA84.c` queues; execution happens later in `8004CECC.c:106-110`.

## Status

Latent. Not triggered by the shipped data: the 0x3D recursion probe reached a maximum depth of 1, and the CONFT purchase still computes 100 → 70 correctly.

## Task

Adopt the type filter and the queue order from retail, replacing the direct `Retire` calls in the script.

## Acceptance

- Commands addressed to the filtered types are dropped.
- A retire issued from a script no longer runs nested inside `RunEntityScript`.
- The CONFT purchase stays 100 → 70.

## Done

- Type filter and enqueue live in `PurchasePadSystem.SendEntityCommand` (`8004DA84.c:21-69`,
  `8004DA58.c:26-39`), execution in the frame pass `PurchasePadSystem.RunEntityCommands`
  (`8004CECC.c:67-241`, 0x3D in `:108-114`); `LevelScene` calls it after the pad evaluation and
  before `SweepRetired`.
- A full slot table falls back to the 16-entry message table with retry
  (`gEnqueuePendingMessage` 8004D9EC.c, `gStepPendingMessages` 8004DB74.c, 600 ticks).
- Tests: `tests/FutureCop.Assets.Tests/EntityCommandQueueTests.cs` (non-nesting, type filter
  2/3/4/7/15/23, filter 18/21, retry table, compaction); the CONFT purchase 100 → 70 stays covered
  by `PurchasePathTests`/`SceneCapture*`.
