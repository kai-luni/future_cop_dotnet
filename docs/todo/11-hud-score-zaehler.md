# HUD-Score-Zaehler

## Problem

Die Zaehler `Count1..4` in `ScoreSession` sind nicht abgesichert.

## Beleg

- `ScoreSession`, gefunden als Mutation M12: Der Mutant blieb gruen.

## Aufgabe

Die Zaehlerfuehrung testen, inklusive der Grenzfaelle.

## Akzeptanz

- Ein Test schlaegt fehl, wenn ein Zaehler nicht mehr fortgeschrieben wird.
