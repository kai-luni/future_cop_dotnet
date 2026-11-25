# One-Shot-MapZone wird nicht freigegeben

## Problem

Eine One-Shot-MapZone wird nach dem Ausloesen nicht freigegeben und bleibt gesperrt.

## Beleg

- `_retiredMapZonePads` in `src/FutureCop.View/LevelScene.cs`.
- Gefunden als Mutation M9: Der Mutant blieb gruen.
- Retail: Freigabe nur ueber MapZone-Flags (`80031C74.c:115`).

## Aufgabe

Die Zone nach dem Ausloesen austragen und die Freigabe pruefen.

## Akzeptanz

- Ein Test schlaegt fehl, wenn die Freigabe entfernt wird.
- Eine ausgeloeste One-Shot-Zone feuert nicht erneut und blockiert nicht dauerhaft.
