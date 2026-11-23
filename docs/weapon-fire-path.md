# Where the shots come from

Traced from the decomp sources of `~/git/future-cop-lapd-ps1` (`src/game/`) so the viewer shows the
retail projectile path instead of an invented effect. Addresses are the retail entry points; every
statement below names the file it comes from.

## The chain from the fire button to a flying projectile

1. **Slot selection** — `func_80055E10` maps the pad to the three weapon slots of the player object:
   slot 0 for button `0x04000000`, slot 1 for `0x00400000`, slot 2 on the weapon-switch edge. It calls
   `func_8006BA50(obj, slot)` once per slot per frame.
2. **Trigger logic** — `func_8006BA50` (8006BA50.c) keeps the reload timers (`ctl+0x78`), the shot
   counter (`+0x84`, shared `+0x8A`), the per-slot definition id (`+0x90`), the cycle counter
   (`+0x93`), reads the definition through `func_8006AAEC`, picks the fire half at `config + 0x04` or
   `config + 0x24` and routes types 10..12/14 to `func_8006B23C` (beam update) and everything else to
   `func_8006ACB0`.
3. **Fire dispatcher** — `func_8006ACB0` (8006ACB0.c): pool gate `D_800A8F90[1] - D_800A8F90[0] >= 10`,
   then the **alternating muzzle**: the two 76-byte model instances at `state + 0x178` are indexed by
   the parity of the counter at `state + 0x8A/0x84 + index*2`, and `func_80017668(instance, 0, &pos)`
   returns vertex 0 of the chosen instance as `posA`. `config = func_8006AAEC(state[0x90 + index])`,
   `def = config + 0x24` when the counter is positive else `config + 0x04`, the sound id `def+0x1C`
   goes to `func_80046C84`, the aim vector is `posB - posA` (target model `+0x5C`, else muzzle plus the
   owner position) normalised by `func_8001A180`, and the action byte `def+0x00` selects the spawner:
   actions 1/2/3/0x12/0x13/0x15 → `func_8005E47C` (action 2 first runs the lead solver `func_8005E2A0`),
   8/9 → `func_8005EE04` (9 via `func_8005E624`), 4/0x14 → `func_8005E9E4` (lob), 5 → `func_8005ECB8`,
   6 → `func_8005EB74`, 0xF → `func_8005C048`, 0x11 → `func_8005CBCC`, 0xD → `func_8005D690`.
   The flag word handed to the spawner is `s4 = (def[1] & 1) << 19 | 0x201`; `def+0x1B` spawns a
   secondary object through `func_800402DC`.
4. **Spawner** — `func_8005E47C` (8005E47C.c) fills the request block for
   `func_8004D444`: kind 2, size word `0x28`, position, `flags = ent->unk14 | 0x8000` (+`0x4000` for
   entity kind 0x12), `unk14 = flags | 0x108` when `ent->unk24` bit 8 is set else `(flags | 8) & ~0x100`,
   the speed word `obj->unk06` at `unk48`, the **step scale** `unk2C = speed << 4` — or `speed << 1`
   with the alternate selector `obj->unk08` in `unk4A` —, the range word `unk3C = obj->unk0A << 4` and
   the damage word `unk44 = obj->unk02 << 4`.
5. **Queue and handler** — `func_8004D444` asks the handler for the block size (message 1 → `0xFC`),
   allocates it and drives messages 1 and 2 of `func_80036624` (80036624.c, the model-backed entity
   class). Message 2 is `func_80035904`: it binds the model through `func_80013BD8(&self->+0x28,
   descriptor + 0x24, 0)` and copies the flight parameters — `unk84 = args+0x2C` (speed), `unkD4 =
   args+0x48` (target speed), `unkD2 = args+0x4A` (acceleration), `unkA6 = (args+0x3C) >> 8` (leash
   radius), `unk1A = args+0x1A`, position at `+0x50`.
6. **Flight** — message 6 runs `func_80035B7C` every tick (80035B7C.c): pursuit toward the target,
   leash test against `unkA6 << 8`, burst into three children at `s4 >= 0x3001`, collision
   `func_800684AC`, floor test `func_80066F78`, basis matrix, and the position step itself
   `func_8001ACA4(position, velocity, s3, position)` with `s3 = delta * unk84`
   (8001ACA4.c: `out = a + (b * scale >> 16)` per component, 16.16).

## What the mission file carries

The definition records are the ones the runtime registry `D_800A3340` caches: `func_8006A918` finds the
`tACT` chunk for the requested section id (`func_80019C38`), copies `data + 0x10` for `0x44` bytes and
then resolves **four references** from the `aRSL` chunk that follows, through `func_80019BF0`. Those
references are the projectile models: `func_8006ACB0` passes the first one to the spawner as the model
pointer (`config + 0x44`). The record's last `0x10` bytes are therefore the reference area, not shot
parameters.

Reading a definition half (offsets relative to `config + 0x04` or `+0x24`):

| offset | meaning | used by |
| --- | --- | --- |
| +0x00 | action / spawner selector | `func_8006ACB0` switch |
| +0x01 | flags: bit 0 → burst flag of `s4`, bit 2 → store the result in `state+0x98` | `func_8006ACB0` |
| +0x06 | shots per reload cycle | `func_8006BA50` |
| +0x08 / +0x0A | reload timer after a cycle / after every other shot | `func_8006BA50` |
| +0x0C | shot identifier (stored at `args+0x1A`), **not** the model | `func_8005E47C` |
| +0x0E | damage word, stored `<< 4` | `func_8005E47C` |
| +0x12 | speed word → `args+0x48`, step scale `<< 4` or `<< 1` | `func_8005E47C` |
| +0x14 | alternate-speed selector | `func_8005E47C` |
| +0x16 | range word → `args+0x3C = << 4`, leash `>> 4` | `func_80035904` |
| +0x1B | secondary object column | `func_800402DC` |
| +0x1C | sound id | `func_80046C84` |
| +0x1A / +0x1E | forwarded words (`args+0x4C`, `args+0x40`) | `func_8005E47C` |

`M1A1.MIS` ships eight such definitions (ids `0x4D` … `0x70`), each with two fire halves and a `Cobj`
reference: `0x4D` → model 59 (speed 8192, triggers `0x03`/`0x12`), `0x4F` → model 60 (speed 10240,
trigger `0x02`), `0x53` → model 64, `0x54` → model 65, `0x56` → model 62, `0x57` → model 66, `0x70` →
model 68. The other missions repeat the same picture with different section ids.

## What the port covers, and what it does not

Ported and parity-tested against the unchanged C (`tests/host/shot_test.sh`):

| piece | C original | .NET | proof |
| --- | --- | --- | --- |
| definition + resource list reader | `func_8006A918`, `func_8006ACB0` reading rules | `WeaponCatalog` | synthetic + retail tests, `tests/golden/shot.vectors` inputs |
| spawn block composition | `func_8005E47C` | `ShotSpawn.Compose` | `tests/golden/shot.vectors`, 7 cases, both sides |
| position integrator | `func_8001ACA4` | `ShotMotion.Integrate` | `tests/golden/shot-move.vectors`, 8 cases, both sides |
| muzzle point | `func_80017668` | `PlayerAssembly.Point` | ported earlier with the assembly work |
| range and step scales | `func_80035904:232`, `func_8005E47C:87-93` | `ShotDefinition.Range`, `StepWord`, `ShotMotion.ModelUnitsPerPositionUnit` | unit tests |
| per-tick speed ramp | `func_80035B7C:435-448` | `ShotMotion.AdvanceSpeed` | unit tests, values derived from the C text |
| trigger cadence per weapon | `func_8006C3E0:59-68`, `func_8006BA50:169-217` | `WeaponTrigger`, `WeaponCadence` | `tests/golden/cadence.vectors`, 22 cases, both sides |
| leash metric and radius | `func_8001A4E8`, `func_80035904:234`, `func_80035B7C:434` | `ShotMotion.OctagonalDistance`, `LeashRadius` | unit tests, values derived from the C text |
| flight basis of the projectile | `func_800193F8`, `func_80010974` (cross product), rebuilt per tick by `func_80035B7C:554-568` | `WireframeScene.ShotMatrix` | not harness-tested (viewer-only matrix); documented below |

The speed ramp is transcribed rather than harness-tested: the block is inline inside `func_80035B7C`,
so no C harness can call it on its own, and its expectations are derived from the C text
(`unk84 += (accel * delta) / 16`, clamped at `unkD4 << 4`). With the alternate-speed selector cleared
the accumulator never moves, which is the constant step the golden vectors cover.

Not ported, and deliberately out of scope of this step: the pursuit/target logic of `func_80035B7C`,
the lob solver `func_8005E624`/`func_8005E2A0` (the arc word is carried but the shot flies straight),
the beam paths (`func_8006B23C`, actions 10..12/14), the six other spawners, the projectile animation
track (`func_80044B34`), collision and the secondary object of `func_800402DC`.

## The body reaction to a shot: there is no fire clip

Retail has **no fire animation clip** for the player mech, and none is needed. The complete weapon
path - `gWeaponSlotInput` (`func_80055E10`), `gWeaponSlotUpdate` (`func_8006BA50`),
`gWeaponFireDispatch` (`func_8006ACB0`) and `gBeamPartUpdate` (`func_8006B23C`) - contains **no call to
`gSelectAnimationClip` (`func_800448D8`)** and touches no animation track at all. The only animated
track a player owns is the locomotion one at `obj+0xC8`, and `func_800549F0` drives it from the input
state alone (`anim` is chosen from `state`/`unkB9`/`unkBA` at `800549F0.c:367-600`, then selected at
`:807`). The walk/run/dash clips therefore never fire a shot-triggered pose; nothing in the fire path
can start clip 0x36/0x37 - those two are the punch clips of `action == 2` (`800549F0.c:856-876`) and
belong to the melee path, not to the weapon slots.

What the original does instead, in this order:

1. **The arm kick.** `func_8006ACB0.c:137-139`: after `gTransformPartVertex` sampled the muzzle of the
   firing arm, and only for `index == 1` (the Cross slot), the dispatcher stamps `0x84` into the recoil
   timer of that arm - `*(u16 *)(state + 0x174 + offset * 2) = 0x84`. `offset` is the low bit of the
   shot counter (`:123-131`), so the timer of the arm that actually fired is the one that is set.
2. **The arm displacement.** `func_80057E40.c:201-215`: `func_80052D7C` recomposes both arm instances
   from the companion's joint chain, then each live timer is decremented by the frame delta, and while
   it is still positive `gVectorMulAdd(part.b, part.a, sum, part.b)` moves the instance along the
   arm's own local Z using `sum = (timer - 132) << 4` above 88 and `sum = -timer << 3` below. The
   matrix is recomposed every frame, so this is an absolute per-frame offset, not an integral.
3. **The muzzle effect.** `func_8006ACB0.c:287-297`: if the half's byte at `def + 0x1B` is non-zero,
   `gSpawnTypeAtOwner` spawns the secondary object from that spawn-table column (bank/row 1). With a
   spawned projectile its owner is the projectile itself (`:289`, so it travels with the shot); without
   a spawned projectile it goes through the `t60` block with `ptr = state + 0x178 + offset * 76` and
   `b = 0x64`, i.e. it is attached to the same arm record for 100 ticks.

The port reproduces (1) and (2) in `src/FutureCop.Game/WeaponRecoil.cs`, wired into
`LevelWeapons.TryFireSlot` and `LevelScene.UpdateWalkerPose`; the frame order of `func_800588D4`
(`gSlewEntityAim` at `:287` before `gWeaponSlotInput` at `:290`) is kept, so the shot leaves from the
arm pose of its own frame and the kick shows from the next one. (3) is **not** ported: it needs the
spawn table (`D_800A7040`) and the object constructor, which the port does not have yet.

## Open points

- **Which definitions the player carries.** The three slot ids live in the runtime player state
  (`ctl+0x90`), filled during mission setup, not in the mission file. The viewer therefore cycles
  *every* fire definition the mission ships, labelled by its section id, instead of pretending to know
  the loadout.
- **Which two model instances the muzzle comes from.** `func_8006ACB0` indexes `state+0x178`; the
  viewer has no player state and uses the two arm instances of the assembled pose, which are the same
  accessor (`func_80017668`, vertex 0) on the same body.
- **Time base.** One retail tick is 1/300 s and a 60 Hz frame carries five of them
  (`func_8002703C`, `func_8001EFF8`). The trigger of `WeaponTrigger` therefore works in ticks and the
  viewer advances it one frame per 1/60 s, which reproduces the retail fire frames exactly - see
  `../future-cop-lapd-ps1/docs/weapon-cadence-and-range.md` for the per-weapon table.
- **The position scale of the integrator.** `func_8001ACA4` advances a position by the speed scale in
  position units; the ratio between position units and the model units the meshes are built in cannot
  be read off the decomp. The viewer uses one documented divisor for both sides
  (`ShotMotion.ModelUnitsPerPositionUnit = 16`): with it a 4Da shot covers about one body height
  (8192 model units) in its eight retail ticks, which is the scale the mission data reads at. Using
  different divisors for step and radius either killed the shot within a fraction of a tick or let it
  crawl a few pixels. `ShotSlowMotion` (default 300) stretches that single frame to about a second and
  `ShotScale` (default 1, retail) sizes the rocket; both are viewer knobs, not retail numbers.
  **The level is different:** `LevelWeapons` keeps shots in world position units (the muzzle is
  `body + PlayerAssembly.Point`, `LevelScene.ShotMatrix` divides by 16 only for drawing), which is the
  unit of the mover's step `delta * unk84` and leash `unkA6 << 8`. The level shot therefore uses
  divisor 1 (dividing by 16 there made it fly 8192 instead of 131072 units). Its slow motion
  (`LevelWeapons.ShotSlowMotionDefault = 20`, derivation at the constant) stretches the retail clock
  (`Shot.AdvanceRetailTime`) instead of shrinking the step: 0x44a flies its 8 retail ticks in 32 frames.
- **Speed brake.** `func_80035B7C:459-468` halves `unk84` (and `unkD4`) whenever the countdown `unkDA`
  goes negative - first after 1500 ticks (`func_80035904:237`), then every 300 - and stops the shot below
  16. It runs only in pursuit states 1/3; `func_8005E47C:108-109` gives a shot state 1 only with a target,
  and without a live target `:512 -> :497` drops it to 0. Ported as `ShotMotion.DecaySpeed`; it never
  fires in the port, which has no targets.
- **End of flight.** `unk1C = -1` (leash `:434`, brake `:467`, wall `:538`, floor `:543`) makes
  `func_8004CE04:59-60` send message 4. `func_80036624:163-174` then calls `func_800410BC` (damage only,
  message 9, no visuals) when the strength `unkD0 = DamageWord >> 4` is positive - 0 for 0x44a - and
  `func_8002AD58:88 -> func_8002A810:112-132` spawns type `def + 0x1A` (83 for 0x44a in CONFT) through
  `func_8003B898` (flag 0x40000, `func_80035904:181`) and `func_800402DC`. That object needs
  `D_800A7040` and `func_8003E984`, so it is not ported; what it depicts is unverified.
- **Flight basis of the projectile.** `func_800193F8` turns the flight vector `d` into a
  right-handed basis: `rows[0] = normalize(dz, 0, -dx)`, `rows[1] = d x rows[0]`,
  `rows[2] = d`, all unit vectors, so a horizontal flight is a pure yaw and any vertical
  component adds pitch (`func_80035B7C:554-568` rebuilds the same basis from the same vector
  every tick). The model's +Z is the nose, so the rocket points along its velocity. The
  viewer builds that basis in `ShotMatrix`; the draw scale is applied on top (retail uses
  none, hence the default of 1).
- **Aim.** Retail aims at a target entity and builds ONE direction vector `normalize(posB - posA)`
  (`func_8006ACB0:166-171`) that both alternating muzzles share. The viewer has no target, so it fires
  along `ViewerOptions.ShotAimDegrees` (default 90°) and integrates that unit direction through the
  same integrator. The per-arm mirroring (right arm +aim, left arm -aim) is a **viewer convention**
  with no retail counterpart; it only makes both muzzles shoot outward instead of across the body.
