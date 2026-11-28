# Radar: direction and distance of the white arrow still unexplained

## Problem

In the playtest the white radar arrow points in the wrong direction and sits too
far away from the player's own mech. The cause is still open.

## Evidence

- Todo 22 checked the heading wiring against retail: `walker.Angle` corresponds to
  the angle `unkB0` that retail reads for the arrow (`8005A9C4.c:24,111`).
- The side selection is correct as well: `unkC1` corresponds to the player slot that
  retail sets on spawn and uses for the radar origin (`800579D4.c:176-190`,
  `8005A9C4.c:109`).
- The comparisons against the C vectors confirm both inputs but do not explain the
  deviation in the rendered HUD.

## Task

Reproduce the symptom in the game and capture, for the same frame, the walker angle,
the player slot and a screenshot showing arrow and mech. Then follow radar rotation and
HUD transformation up to the drawn arrow position and compare with retail.

## Acceptance

- The reproduction documents walker angle, player slot and screenshot of the same frame.
- The comparison attributes the deviations in direction and distance to a concrete step
  of the draw chain, or proves which of the checked steps match retail.
