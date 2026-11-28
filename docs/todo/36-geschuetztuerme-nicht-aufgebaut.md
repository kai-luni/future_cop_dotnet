# Precinct Assault: some gun turrets are not built

## Goal

Make Precinct Assault playable in 1-player mode with the Sky Captain. This task is checked in exactly that mode.

## Problem

Some gun turrets are not standing; only grey remains are visible. The grey remains may be correct (e.g. destroyed or not yet erected state). The turrets must however exist at the start of the game and be reliably (re)built after some time.

## Evidence

Observation by Kai during the playtest of PR #22 (Precinct Assault, 1 player, Sky Captain). Affected turrets and positions have not been recorded yet; neither the cause nor the deviation from the PS1 original is proven. Related: Todo 24 (turrets too low).

## Task

Compare the initial state and the build/rebuild logic of the turrets in the port with the PS1 original; clarify what the grey remains represent. Fix only proven deviations.

## Acceptance

- At the start of the game all turrets that stand in the original are standing.
- A turret in the grey state is rebuilt after the time proven in the original.
- A regression test covers initial state and rebuild and fails without the fix.
