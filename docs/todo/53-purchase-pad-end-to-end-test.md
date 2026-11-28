# No test drives the real CONFT purchase pads end to end

## Problem

The purchase path is only covered on hand-built pads: the one real record it used
(Cact 106) has script word 0, so the test had to synthesise a pad. The two real base buyers
(`14/47`, `14/51`) and their score/spawner effects are never exercised, and no test checks a
pad's world position against its record. A broken base economy therefore cannot be caught by
CI — Kai finds it by playing.

## Evidence

- Port: `PurchasePadSystem.cs:556-581` — the loader skips a pad only when `s0 != 0` **and**
  the offset is invalid; `:938-940` — `Script0Offset == 0` means "no script" (matches retail
  `func_80031C74`).
- Port: `tests/FutureCop.Assets.Tests/PurchasePathTests.cs` — the CONFT-based case builds its
  own pad because record 106's `+0x24` word is 0; no test references record 47 or 51.
- Disc: the real buyers are type 14 ids 47/51 with scripts `0x63F`/`0x65F`; their spawners
  are Csac 7 and 8.
- Port: `LevelScene.cs:713-755` is the only place the type-14 walk runs; `IsInsidePad`
  (`PurchasePadSystem.cs:1065`) is only exercised with synthetic coordinates.

## Task

Add an end-to-end test over `PA/CONFT.MIS`: load the mission, place the pads, put the player
target at the record position of 14/47 and 14/51, advance one frame, and assert a unit of
type 28 / type 9 appears and `gs[25]` dropped by 50. Assert the pad world positions equal the
record values.

## Acceptance

- The test fails if either pad's script, spawner id, position or the score write changes; it
  passes on the current head and reports the two spawner ids and the charged amount.
