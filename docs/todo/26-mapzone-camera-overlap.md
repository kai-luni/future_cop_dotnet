# Apply camera-zone overlap to chase camera states 5 and 6

## Problem

The managed chase camera does not query the live MapZone list to decide whether camera presets 5 and 6 may stay active.

## Evidence

- C `src/game/80031B40.c:74-89` checks flag `0x40` and inclusive radius-expanded X/Z edges. `tests/host/leaf_more_harness.c:931-947` covers empty lists, ineligible flags, and exact edges.
- C `src/game/800569EC.c:239-246` uses the overlap result to fall back to state 0, while the managed camera has no corresponding overlap call.

## Task

Wire the radius-expanded overlap query over linked MapZones into the corresponding chase camera state path.

## Acceptance

- A flagged linked zone holds the preset on its exact expanded edge, but an unflagged or unlinked zone does not.
- The camera falls back to state 0 when no eligible zone overlaps.
