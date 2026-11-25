# Schuss bleibt nach Treffer in der Liste

## Problem

Ein Schuss, der getroffen hat, wird nicht aus `Shots` entfernt. Folge waere Mehrfachschaden pro Schuss.

## Beleg

- `src/FutureCop.View/LevelScene.cs`, `DeliverShotContacts`.
- Gefunden als Mutation M13: Der Mutant blieb gruen, die Suite weiter bei 3352/3352.

## Aufgabe

Den Schuss nach dem Treffer austragen, wie es der Port an anderer Stelle bereits tut.

## Akzeptanz

- Ein Test schlaegt fehl, wenn das Austragen entfernt wird.
- Ein Schuss richtet genau einmal Schaden an.
