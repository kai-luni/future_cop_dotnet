# Capture nutzt nicht den Spielerradius

## Problem

Beim Capture wird der Spielerradius fest auf 0 gesetzt statt `playerCoord.Radius` zu verwenden.

## Beleg

- `src/FutureCop.View/LevelScene.cs`, Capture-Pfad.
- Gefunden als Mutation M14: Der Mutant blieb gruen.

## Aufgabe

Den echten Radius verwenden.

## Akzeptanz

- Ein Test schlaegt fehl, wenn der Radius auf 0 gesetzt wird.
- Capture gelingt am Rand der Toleranz und nicht ausserhalb.
