# Todo 42: Radial Contact Damage (`gApplyRadialContactDamage`, `func_800410BC`)

## Problem

The shared retail radial blast / contact-damage broadcaster `gApplyRadialContactDamage` (`func_800410BC`) is not ported in .NET. Both ported weapon damage paths explicitly skip it: projectile impact message 4 omits the radial splash pass before retiring the projectile, and beam hit delivery omits the radial spread stage of the second half (`s3->unk0E`). As a result, explosive/splash projectiles and beam radial spreads do not deliver distance-attenuated message-9 damage to nearby entities.

## Evidence

- PS1 reference (`0a82cd1`): `src/game/gameplay-ai/gApplyRadialContactDamage.c:63-107` (`func_800410BC`) walks the active entity list (`D_800A8F98`), skips entries with `health <= 0` or `flags & 0x200080`, measures planar octagonal distance via `func_8001A750`, enforces optional inner dead zone (`D_8009F324`), vertical band (`D_8009F329`), and directional cone (`D_8009F328`), computes linear falloff strength `((radius - d) * D_8009F700) >> 16`, and dispatches message 9 (`obj->+0x04(9, obj, &record, 0)`) when strength is non-zero.
- .NET projectile handler: `src/FutureCop.Game/EntityLifecycle.cs:335-336` documents "Message 4 skips the radial blast of :164-174 (gApplyRadialContactDamage is not ported) and retires through the base handler".
- .NET beam handler: `src/FutureCop.View/LevelWeapons.cs:446` documents "The radial spread of the second half (s3->unk0E, gApplyRadialContactDamage) is not ported."

## Task

1. Port `gApplyRadialContactDamage` (`func_800410BC`) and its wrapper parameter setup (`func_800413EC`, `func_80041428` where needed) into `FutureCop.Game` using the shared fixed-point primitives (`EnemyMath.ApproxDistanceXz` for `func_8001A750`, `MechMath.Fmul` for 16.16 products).
2. Wire projectile message 4 in `src/FutureCop.Game/EntityLifecycle.cs` and beam radial spread in `src/FutureCop.View/LevelWeapons.cs` to call the ported broadcaster.
3. Add C-verified unit tests covering radius cutoff, inner dead zone, vertical band, cone filter, falloff strength rounding, and skipped flags (`0x200080`, `health <= 0`) against unchanged PS1 C under QEMU.

## Acceptance

- `func_800410BC` is ported as a standalone C-shaped method in `FutureCop.Game` with deterministic integer arithmetic matching PS1 retail C.
- Projectile message 4 and beam radial spread invoke the ported broadcaster instead of skipping it.
- Unit tests verified against unchanged PS1 C under QEMU cover falloff, band, cone, and skip filters, and `FC_REQUIRE_RETAIL=1` full test suites pass.
