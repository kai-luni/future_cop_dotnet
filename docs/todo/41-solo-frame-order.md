# Give the solo simulation a single owner for the retail frame order

## Problem

The desktop/menu frame calls `Level.AdvanceWalker` and then `Level.AdvanceSkyCaptain`. The walker call already processes deferred updates, contacts and retirement. This puts some message-6 work after the sweep, whereas the retail solo frame broadcasts message 6 before deferred updates and sweeps later. Adding a new entity or host currently requires knowing this ordering across three different methods. This is an ordering discrepancy, not yet evidence of a particular visible bug.

## Beleg

- PS1 `0a82cd1`, `src/game/gameplay-ai/gRunSoloSessionFrame.c:177-183,208-212`: cull, queued commands, message 6, map-zone triggers, deferred updates; registered spawners precede the final sweep on the active path.
- `tools/FcMainMenu/MenuFlow.cs:442-454` batches locomotion steps and calls both methods. `src/FutureCop.View/LevelScene.cs:845-899,912-919` advances the player, shots and pickups and runs one retirement pass for a nonempty frame batch. `AdvanceSkyCaptain` drives the other update separately. See existing `RetirementChainHookTests` and `MapZoneReleaseTests`; do not move the sweep just to satisfy an isolated test.

## Aufgabe

Trace the active, unpaused solo path and record the relative order for an observable interaction, including the retail frame delta. Give simulation phase ordering one headless entry point; leave input decoding in the host and drawing in the renderer. Keep per-frame work distinct from locomotion substeps. This does not require moving the whole `LevelScene` class or implementing missing queued commands (task 05).

## Akzeptanz

- Exercise the actual host-to-scene entry with an entity that changes state on message 6 and can be retired; assert the observed state/visibility before and after the sweep against the reference ordering. Reversing those phases must fail the test.
- Cover a normal frame and a host call with several locomotion steps. Derive dispatch and sweep counts from the retail frame contract rather than assuming one sweep per locomotion step. Existing pickup, retirement and respawn tests remain green.
