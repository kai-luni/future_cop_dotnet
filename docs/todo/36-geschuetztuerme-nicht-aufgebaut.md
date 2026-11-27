# Precinct Assault: Einige Geschütztürme sind nicht aufgebaut

## Ziel

Precinct Assault, 1 Spieler mit Sky Captain, lauffähig machen. Diese Task wird in genau diesem Modus geprüft.

## Problem

Einige Geschütztürme stehen nicht, sichtbar sind nur graue Reste. Die grauen Reste können korrekt sein (z. B. zerstörter oder noch nicht errichteter Zustand). Die Türme müssen aber zu Spielbeginn vorhanden sein und nach einiger Zeit sicher (wieder) aufgebaut werden.

## Beleg

Beobachtung von Kai beim Spieltest von PR #22 (Precinct Assault, 1 Spieler, Sky Captain). Betroffene Türme und Positionen sind noch nicht festgehalten; Ursache und Abweichung vom PS1-Original nicht belegt. Verwandt: Todo 24 (Türme zu tief).

## Aufgabe

Anfangszustand und Aufbau-/Wiederaufbaulogik der Türme im Port mit dem PS1-Original vergleichen; klären, was die grauen Reste darstellen. Nur nachgewiesene Abweichungen korrigieren.

## Akzeptanz

- Zu Spielbeginn stehen alle Türme, die im Original stehen.
- Ein Turm im grauen Zustand wird nach der im Original belegten Zeit aufgebaut.
- Ein Regressionstest deckt Anfangszustand und Aufbau ab und schlägt ohne die Korrektur fehl.
