# Enemy fire and player death

## Problem

Enemy units deal no damage, and the player cannot die. That removes both pressure and the possibility of failure.

## Evidence

- Missing according to `docs/pa-playable-roadmap.md:142-168`.
- The player damage path already exists for the player's own weapons; the opposing side is missing.

## Task

Allow shots from enemy units against the player, apply damage to the player armour and handle the player's death.

## Acceptance

- The player visibly loses armour when hit.
- At zero armour the death is triggered instead of the level simply continuing.
