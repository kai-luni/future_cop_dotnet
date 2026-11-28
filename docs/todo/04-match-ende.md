# Match end

## Problem

A match never ends. After a win or a loss the level keeps running.

## Evidence

- Missing according to `docs/pa-playable-roadmap.md:142-168`.
- Depends on `03-gegnerfeuer-und-spielertod.md` (defeat) and on `01-csac-zielsuche.md` (victory through scoring).

## Task

Evaluate the match's completion condition and produce an end state: evaluation, display, return to the menu.

## Acceptance

- A reached victory or defeat condition visibly ends the level.
- No hang and no exception during the transition.
