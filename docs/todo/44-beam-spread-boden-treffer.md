# Todo 44: Beam radial spread must not fire on ground hits

## Problem

The ported beam delivery always runs the radial spread pass. When the beam is pulled back to the terrain by its ground clip, retail would skip the spread for that map hit, so the port delivers message-9 splash damage at a terrain impact where the original does not.

## Beleg

- Opus review of PR #25 (`96f4a69`), finding 1.
- `src/FutureCop.View/LevelWeapons.cs:463-470`: the spread pass runs unconditionally for every half-1 beam with a positive damage word, before the direct-hit callback.
- PS1 reference (`0a82cd1`): `src/game/gameplay-ai/gBeamPartUpdate.c:349-369` enters the radial spread only when the map trace did not hit (`(s5 & 0xFF) == 0`).
- `src/FutureCop.View/Beam.cs:87-104`: `ClipToGround` is the port's stand-in for that map trace. It walks the segment in sixteen steps and overwrites `End`, but it records neither that it fired nor where, so the caller cannot tell a clipped beam from a free one.
- The `<remarks>` block at `src/FutureCop.View/LevelWeapons.cs:443-452` describes the unconditional spread as a consequence of the sampler approximation, not as a retail behaviour.

## Aufgabe

1. Make the ground clip report that it fired, for example a `ClippedToGround` flag or the hit point on `Beam`, updated in `ClipToGround`.
2. In `DeliverBeamHits`, skip the radial spread for a half-1 beam whose clip fired, matching `(s5 & 0xFF) != 0`; keep the direct-hit callback unchanged.
3. Verify against the retail sequence whether the skip applies per beam per frame or only to the frame the trace hit; document the answer at the call site.
4. Replace the approximation note at `LevelWeapons.cs:443-452` with a description of the actual gating.
5. Add regression tests: a beam whose segment runs into terrain delivers no message 9 to a bystander inside the spread radius, and a beam that stays free still does.

## Akzeptanz

- A beam pulled back to the terrain delivers no radial splash damage; a free beam keeps its current spread behaviour.
- The tests fail without the gating change.
- `FC_REQUIRE_RETAIL=1` Assets and Gpu suites pass with 0 failed and 0 skipped.
