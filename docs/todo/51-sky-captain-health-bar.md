# The 1P HUD never draws the Sky Captain's gauge

## Problem

Retail's one-player HUD draws a second gauge fed by `D_8009F9EC` (the Sky Captain game
block). The port implements that block, but the live scene never fills it — only tests do —
so with 1000 HP and no bar, damage on the Sky Captain cannot be observed even when it lands.

## Evidence

- Retail: `src/game/gpu-2d-hud/gDrawInGameHud.c:118-120` and `:151-152` — in a one-player
  game the HUD draws the second score block, the stage line and the second game's health
  bar at x 295; the block extern is at `:47` (`D_8009F9EC`).
- Port: `src/FutureCop.View/HudDraw.cs:152` `SecondGame`, `:286-287`
  `var second = globals.SecondGame; if (second is null) return;` — everything after (stage
  line, second gauge) is skipped.
- Port: `SecondGame` is assigned only in tests
  (`tests/FutureCop.Assets.Tests/HudDrawVectorTests.cs:99`, `SlotsBarTests.cs:77,150,163`);
  no production assignment exists.
- Disc: Cact `0x25` records carry hp 1000 (ids 287/295/296/297) — the value the gauge
  would show.
- Port: `LevelSceneSkyCaptain.cs:1006` already pushes `Health` into the drone body
  (`d.Body.F1C = Health`), so the data for a bar exists.

## Task

Have the running Sky Captain scene supply the second game block (health, max, counters) to
`HudGlobals.SecondGame`, matching the 1P source of `D_8009F9EC`, and let the HUD draw the
second gauge.

## Acceptance

- A test advancing a solo frame with a live captain produces HUD globals with
  `SecondGame != null` and a gauge plate at x 295 whose fill equals `Health / MaxHealth`.
- Manual: shooting the Sky Captain moves its bar.
