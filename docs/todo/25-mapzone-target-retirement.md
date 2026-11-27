# Retire non-player targets on a flag-1 MapZone hit

## Problem

A MapZone hit may release the zone itself, but the port does not retire the target when the zone has flag 1 and the target is not a player.

## Evidence

- C `src/game/80031C74.c:106-115` runs the script, sets target flag `0x04000000` and calls `gRetireEntity` for a non-type-1 target and zone flag 1, then decides zone release independently from flag 2.
- `PurchasePadSystem.FireMapZone` only runs the script and updates `LastFire`; neither it nor `LevelScene.UpdateMapZoneTriggers` requests target retirement.

## Task

Apply the target flag and existing entity retirement path after the script for live non-player targets hit by a flag-1 MapZone.

## Acceptance

- An eligible live non-player target receives the flag and retires; a type-1 target does not.
- A stay zone can retire a target without itself being unlinked.
