# Waffe nur einmal einsammeln

## Problem

Eine Waffe laesst sich mehrfach einsammeln. Beim zweiten Mal soll sie uneinsammelbar sein.

## Beleg

- `src/FutureCop.View/PickupItems.cs` — `CollectedCount`, `RemainingCount` und `PlayerResupplyState` existieren bereits.
- `src/FutureCop.Game/WeaponPickups.cs` — die Logik hinter der Fassade.
- Retail: `func_8002FC90` zahlt aus und retired das Objekt (`8002FC90.c`).

## Aufgabe

Pruefen, warum ein bereits eingesammelter Pickup erneut auszahlt. Nicht nur die Anzeige unterdruecken, sondern den Pickup wirklich aus der Menge nehmen, wie es Retail beim Retire tut.

## Akzeptanz

- Zweiter Kontakt mit demselben Pickup zahlt nichts mehr aus.
- `RemainingCount` sinkt dauerhaft.
- Ein Test deckt den zweiten Kontakt ab.
