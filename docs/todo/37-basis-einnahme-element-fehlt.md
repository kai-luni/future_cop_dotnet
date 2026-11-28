# Precinct Assault: element for capturing the bases is missing

## Goal

Make Precinct Assault playable in 1-player mode with the Sky Captain. This task is checked in exactly that mode.

## Problem

In the capturable bases an element is presumably missing that the player triggers to capture the base.

## Evidence

Assessment by Kai during the playtest of PR #22 (Precinct Assault, 1 player, Sky Captain). Not yet proven whether such an object exists in the original and which one it is.

Confirmed again in Kai's playtest report during the review of batch PR #27 (Precinct Assault, 1 player, build before that batch): a base still cannot be captured, and the activating element is still not visible.

The visible retail markers are type-11 Cact meshes, not the trigger-volume outlines. In CONFT, records 10/11 (Cobj 40/41) mark the home-base aircraft/tank purchases, and records 147/168/190/212 (Cobj 29) mark the neutral outpost claims. PS1 `gLoadActorChunk.c:145-164` creates the Cact actors immediately; `gInitType11Entity.c:148-180` positions them on the selected ground surface. The port now renders these static signs; this does not verify the capture mechanic.

## Task

Determine from the PS1 original and the retail map data how a base is captured and which object is provided for it. If it is missing in the port, add loading, rendering and triggering faithful to the original.

## Acceptance

- Mechanic and object are documented with a C reference (`file.c:line`) and retail data.
- In the port the object is visible in the bases and capture works as in the original.
- A test on the real game path covers the capture and fails without the change.
