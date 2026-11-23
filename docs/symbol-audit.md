# Symbol audit after the eight renaming waves

The decomp repo keeps address-named files (`src/game/8006ACB0.c`) while the functions inside carry
behavioural names bound to the retail address by an `__asm__` alias:

```c
s32 gWeaponFireDispatch(struct Owner6ACB0 *owner, s32 index) __asm__("func_8006ACB0");
```

The port cites the **address**, never the name. This document records what that address is called
today, so a later rename in the decomp repo can be traced from the port side.

Scope: every `func_XXXXXXXX` the port mentions in `*.cs` and `*.md` — **189 distinct addresses**,
**849 citation sites**.

## Verdict

| Finding | Count |
| --- | --- |
| Addresses whose `src/game/<ADDR>.c` exists | **189 / 189** |
| — bound by an `__asm__` alias to a behavioural name | 186 |
| — handwritten asm, only the `.globl` label exists (no alias) | 3 |
| Addresses that are wrong or stale | **0** |
| Stale `func_XXX:LINE` references | 1 (fixed, see below) |
| Citation sites that show the address without its today-name | 787 |

An address in the port is therefore *never* a defect. The naming drift is a readability question
only: the address still resolves, because it survives as the `__asm__` alias. 132 of the 189 names
appear nowhere in the port at all, which is the size of the potential comment refresh.

The three addresses without an `__asm__` alias are `func_80019904` (table-driven normalisation via
LZCR), `func_800640D8` (GTE matrix transform) and `func_800648BC` (NCLIP). They are transcribed
retail words, so no C name exists to cite.

## The one real defect found

`src/FutureCop.Game/LobMotion.cs:15` cited `func_80036BD4:190-192` for the per-tick downward bias.
`src/game/80036BD4.c` is 179 lines long, and the gravity step is at **136-137**:

```c
oldA4    = self->unkA0.y;
valA4    = oldA4 - (self->unkAC * arg1) / 300;
self->unkA0.y = valA4;
```

Fixed to `func_80036BD4:136-137 (gGuidedProjectileUpdate, …)`. Every other `func_XXX:LINE` reference
in the port (192 of them) points inside its file.

## Mapping verification

The supplied `addr2name.json` was re-derived from the sources instead of trusted. For all 1769
addresses, the name bound by `__asm__("func_<ADDR>")` in `src/game/` and `src/data/` was extracted and
compared:

* **1768 / 1769 agree** with the mapping file for the 189 cited addresses — zero disagreements.
* One file-wide disagreement outside the cited set: `func_80021898` is `gUploadSoundBankToSpu` in the
  mapping file but `gAllocSpuRamBlock` in `src/game/80021898.c`. Not cited by the port.

Ten addresses initially appeared to disagree because the naive extractor picked up the *first*
`g…` identifier in the file rather than the one on the alias line — in each case that was a callee
(`extern void gSolveLobVector(...) __asm__("func_8005E624")` sitting above the host function). The
per-address, per-line extractor confirms the mapping file. Note that `addr2name.json` and the C
sources are the authority; `func_80010750` really is `gGteLoadMatrix` and `func_80010820` is
`gGteLoadRotation`.

## Most-cited addresses

| Address | Today's name | Sites | File |
| --- | --- | --- | --- |
| `func_8006ACB0` | `gWeaponFireDispatch` | 44 | src/game/8006ACB0.c |
| `func_8002FC90` | `gApplyPlayerResupply` | 36 | src/game/8002FC90.c |
| `func_8006BA50` | `gWeaponSlotUpdate` | 24 | src/game/8006BA50.c |
| `func_80035B7C` | `gGuidedProjectileUpdate` | 22 | src/game/80035B7C.c |
| `func_80047F30` | `gDrawInGameHud` | 22 | src/game/80047F30.c |
| `func_8002FA20` | `gInitRotatingProp` | 19 | src/game/8002FA20.c |
| `func_8002FF6C` | `gApplyRotatingPropProximityEffect` | 19 | src/game/8002FF6C.c |
| `func_8005E47C` | `gSpawnDirectShot` | 19 | src/game/8005E47C.c |
| `func_8006B23C` | `gBeamPartUpdate` | 19 | src/game/8006B23C.c |
| `func_80057E40` | `gSlewEntityAim` | 17 | src/game/80057E40.c |
| `func_80055988` | `gUpdatePlayerFormAndMotion` | 14 | src/game/80055988.c |
| `func_80047BE8` | `gEmitHudGouraudBar` | 13 | src/game/80047BE8.c |
| `func_8006A918` | `gRegistryLoad` | 12 | src/game/8006A918.c |
| `func_80013F08` | `gProjectModelInstance` | 11 | src/game/80013F08.c |
| `func_80017668` | `gTransformPartVertex` | 11 | src/game/80017668.c |
| `func_8001ACA4` | `gVectorAddScaled` | 11 | src/game/8001ACA4.c |
| `func_800549F0` | `gTickOnFootPhysics` | 11 | src/game/800549F0.c |
| `func_8001C214` | `gEmitTranslucentTileRect` | 10 | src/game/8001C214.c |
| `func_8004D6D4` | `gLoadActorChunk` | 10 | src/game/8004D6D4.c |
| `func_80066F78` | `gGetGroundHeight` | 10 | src/game/80066F78.c |

The remaining 169 addresses follow the same pattern; the full table is reproducible with the rule
below.

## Reproducing

```bash
# today's name for an address, straight from the source
grep -n '__asm__("func_8006ACB0")' src/game/8006ACB0.c
# every citation of every address in the port
grep -rnoE 'func_[0-9A-F]{8}' --include='*.cs' --include='*.md' . | sort -u
```

A comment refresh (adding the today-name next to the address, e.g.
`func_80035B7C (gGuidedProjectileUpdate)`) touches 787 sites and changes no behaviour. It was
deliberately not done wholesale, because a mechanical rewrite of hand-written prose across
`docs/weapon-fire-path.md` and 40 source files carries a real risk of corrupting line references
for no behavioural gain.
