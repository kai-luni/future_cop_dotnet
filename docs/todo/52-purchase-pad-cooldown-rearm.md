# The purchase pads gate on a ScriptVars latch that the bought unit's retire script re-arms

## Problem

Both base purchase pads test a `ScriptVars` latch before they fire, then set that word to
exactly 1. Neither the mission's RPNS pool nor its Cfun timer bytecode raises those words
back, so a naive reading is that the pad is locked after the first purchase. The port
executes the same bytecode verbatim, so the behaviour is inherited but was never analysed.

## Evidence (mission data, file offsets relative to PA/CONFT.MIS)

Scan method: walk `Shoc`/`Shdr` chunk headers from file offset 0 while
`u32(off) == 0x53484F43 && u32(off+0x10) == 0x53484452` and the tag at `off+0x18` is
`Cact`/`Csac`; script words are the request-header words at `off+0x24`, `off+0x28`,
`off+0x2C` (what `MissionEntities.Parse` reads); the RPNS pool starts at 0x68 with length
0x710 and scripts are addressed by absolute pool offset.

| pad | script 0 | latch word | spawner | bought record | retire script |
|-----|----------|-----------|---------|---------------|---------------|
| Cact 14/47 | 1599 = 0x63F | 18 | 7 | Csac 7 | 1739 = 0x6CB |
| Cact 14/51 | 1631 = 0x65F | 20 | 8 | Csac 8 | 1763 = 0x6E3 |
| Cact (P2)  | 1663 = 0x67F | 19 | 57 | Csac 57 | 1727 = 0x6BF |
| Cact (P2)  | 1695 = 0x69F | 21 | 6 | Csac 6 | 1751 = 0x6D7 |

Pad 47 script bytes at 0x63F: `99 10 b1 23 92 13 81 25` = `push 18; push var[18];
push 1; op 37`, i.e. `a = (b < a)` with `a = 1` (see 80069D68.c:340-341), so the gate is
**`ScriptVars[18] < 1`**, not `> 1`; that is why the first purchase (word 0) passes. The
tail `92 34 81 92 20` = `push 18; op 19; push 1; push 18; op 32` stores exactly 1
(80069D68.c:240-241).

The re-arm is the retire script of the very unit the pad buys:

- 0x6CB (`Csac 7`): `80 92 20 85 9a 30 85 a0 30 94 0c 00` = `ScriptVars[18] = 0;
  GameSession[26] += 5; GameSession[32] += 5; push 20 (event)`.
- 0x6E3 (`Csac 8`): `ScriptVars[20] = 0; GameSession[26] += 5; GameSession[32] += 5`.
- 0x6BF (`Csac 57`): `ScriptVars[19] = 0; GameSession[25] += 5; GameSession[31] += 5`.
- 0x6D7 (`Csac 6`): `ScriptVars[21] = 0; GameSession[25] += 5; GameSession[31] += 5`.

Engine side: `gRetireEntity` runs the entity's +0x98 script through
`gInterpretEntityScript` when it retires (8004D958.c:54); +0x98 is the record's second
script slot, the same request word the loaders copy as a script (gLoadMapZone.c:95 stores
the request's +0x24 word as a zone node's script). The port maps it as
`EntityRecord.Scripts[1]` -> `CsacUnitSpawner`'s `RetireScript` -> `LiveEntity.Retire`
-> `EntityWorld.OnScript` -> `RpnsScriptVm`.

## Verdict

**Re-armed.** Retail is not one-shot; a pad buys one unit at a time and becomes available
again when that unit is destroyed. There is no timer, no engine writer and no Cfun write -
the writer lives in CONFT.MIS itself, in the retire slot of the bought Csac record, and the
port already executes it: `PurchasePadSystem` loads the scripts and owns `VmState`, the
pad queues spawner 7/8, `CsacUnitSpawner` spawns the record with `RetireScript` set, and
`LevelScene` wires `EntityWorld.OnScript` to `RunEntityScript` over that same VM state
(LevelScene.cs:229, 251, 262). No behaviour change was needed; the defect was the missing
analysis and the missing coverage of the second purchase.

## Implementation

`tests/FutureCop.Assets.Tests/PurchasePathTests.cs`:
`Cact14DreadnoughtPadsRearmWhenTheirBoughtUnitRetires` drives the real CONFT.MIS pads
(Cact 14/47 and 14/51) and asserts, per pad:

- `Script0Offset` is 1599 / 1631 - the script word, so a changed mission script fails.
- First purchase with the latch at 0: `Accepted`, sfx 63, score 50 -> 0, the latch becomes
  1, and the drain yields the bought record (Csac 7 / 8) with `RetireScript` 1739 / 1763.
- Second attempt while the unit lives: `Triggered` but `Accepted == false`, sfx 73, latch
  still 1, nothing queued and nothing spawned.
- Message 4 (retire) zeroes the latch, pays 5 to the opposing session score and drops the
  runtime id; the frame sweep (`EntityWorld.SweepRetired`) unlinks the unit and frees the
  Csac slot.
- Third trigger: `Accepted` again and a second, distinct unit of the same record spawns -
  two purchases in one session.

Sensitivity was checked by removing the `world.OnScript` wiring: the test then fails at the
"latch is 0 after retire" assertion (the gate stays 1 and the second purchase is refused),
so it cannot pass with the re-arm unplugged.

## Acceptance

- Two purchases from Cact 14/47 (and 14/51) in one session each spawn a unit - covered by
  the test above on the real mission file.
- Manual: walking onto a pad always reacts (spawn or sfx 73 refusal), never silently
  nothing - unchanged, since the gate semantics were already faithful.
