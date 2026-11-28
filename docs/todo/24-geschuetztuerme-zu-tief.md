# Precinct Assault: gun turrets appear sunk into the ground

## Problem

Some visible gun turrets look partly sunk into the ground.

## Evidence

Observation during the playtest of PR #20. Affected maps and positions have not been recorded yet; neither the cause nor the deviation from the PS1 original is proven.

## Task

Localise the affected turrets and compare their placement relative to the terrain surface at the same spots with the PS1 version. Fix only proven deviations.

## Acceptance

- Affected map, position and turret are documented, together with the comparison against the PS1 version.
- For a confirmed deviation the placement matches the original. A targeted regression test fails without the fix.
