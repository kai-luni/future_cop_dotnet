# Player mech animation: blue and red lights missing

## Problem

In the playtest the player mech is missing at least one animation from the original: the
blue and red lights that flash on the PS1 model. The rest of the mech's animation looks
correct.

## Evidence

- Observed by Kai during the playtest of PR #26 (Precinct Assault, 1 player).
- The port's mech animation runs through `src/FutureCop.Game/Mech/MechAnimation.cs`
  (`Play`/`Advance`) with `MechBody.Track` and `MechBody.CompanionTrack`
  (`src/FutureCop.Game/Mech/MechBody.cs:71,82`).
- No code path was found that animates a light submodel, a second animation track or a
  palette/CLUT cycle for the mech; a search for `police|beacon|siren|light bar` in
  `src/` returns no hit outside the HUD colour table (`InGameHud.cs:391`).
- The retail side is not yet located. Candidate mechanisms: a second `AnimTrack` on the
  model, a separately drawn submodel, or a cycling CLUT. This has to be proven in the
  decompilation before anything is changed.

## Task

Locate in retail how the mech's lights are animated (second animation track, submodel
frame switch, palette/CLUT cycle) and reproduce it on the mech draw path. While there,
compare the frame counts and playback rates of the mech's existing animations against the
retail frames and fix proven deviations in the same pass.

## Acceptance

- The lights animate in the game exactly as in the original, with a C reference
  (`file.c:line`) for mechanism, frame sequence and rate.
- The remaining mech animations are verified against the retail frame table; deviations
  are either fixed or reported.
- A test on the real draw path fails without the change.
