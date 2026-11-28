# Map: two item models at one position, only one collectable

## Problem

Items lie doubled on top of each other on the map: the player sees two models but receives only one payload when driving over them. Observed in the running game on the Precinct Assault map.

## Evidence

- It is not the placement data: `PA/CONFT.MIS` contains exactly **11** records with payload type `0x10`, and all 11 have **different** x/z coordinates. No two records share a position (counted over the disc's tACT records).
- The model comes from a second field: `src/FutureCop.Game/WeaponPickups.cs:582` presets `modelId = 0`, `:583-596` searches the attached `aRSL` chunk for a `Cobj` entry and reads the model id from it (`:593`). If that chunk or entry is missing it stays at **`ModelId = 0`** (`:617`).
- `src/FutureCop.View/LevelScene.cs:935-949` draws every uncollected item via `_file.Models.FirstOrDefault(m => m.Id == item.ModelId)`. With `ModelId = 0` that lookup returns an arbitrary model with id 0 — a valid model may sit there and be drawn as a second, wrong model at the same world position.
- The payout on the other hand depends on the position only (`WeaponPickups.Update`, `src/FutureCop.Game/WeaponPickups.cs:504-524`): `Overlaps(item, x, y, z)` per item, `item.Collected` only afterwards. A doubly drawn model therefore pays out only once.

## Task

Narrow down whether the second model comes from `ModelId = 0` (fallback to a foreign model) or from a second draw path of the same placement. Then fix only the faulty branch: one item must draw exactly one model at exactly one world position.

## Acceptance

- For the 11 `0x10` records of `CONFT.MIS` exactly one model is drawn per record; a test counts the drawn pickup models against `Pickups.Items.Count(i => !i.Collected)`.
- A record without an `aRSL` `Cobj` entry draws nothing instead of borrowing a foreign model, or is skipped on load.
- The collection path stays unchanged: exactly one payout per item.
