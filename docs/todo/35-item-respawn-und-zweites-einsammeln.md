# Precinct Assault: Items verschwinden und erscheinen fehlerhaft, zweites Einsammeln geht nicht

## Ziel

Precinct Assault, 1 Spieler mit Sky Captain, lauffähig machen. Diese Task wird in genau diesem Modus geprüft.

## Problem

Das Einsammeln eines Items funktioniert. Das Verschwinden nach dem Einsammeln und das erneute Auftauchen sind aber noch fehlerhaft. Außerdem lässt sich ein Item offenbar kein zweites Mal einsammeln, wenn es wieder erschienen ist.

## Beleg

Beobachtung von Kai beim Spieltest von PR #22 (Precinct Assault, 1 Spieler, Sky Captain). Betroffene Items, Positionen und genaues Fehlbild sind noch nicht festgehalten; die Abweichung vom PS1-Original ist nicht belegt. Verwandt: Todo 21 (Item doppelt gezeichnet), Todo 33 (Intervall-Pickup-Retire).

## Aufgabe

Lebenszyklus eines Items im Port (einsammeln, verschwinden, Respawn, erneut einsammeln) mit dem PS1-Original vergleichen, Belege aus dem C-Referenzrepo. Nur nachgewiesene Abweichungen korrigieren.

## Akzeptanz

- Respawn-Zeitpunkt und Sichtbarkeit entsprechen dem Original; das Item flackert oder springt nicht.
- Ein wieder erschienenes Item lässt sich erneut einsammeln und zahlt erneut aus.
- Ein Regressionstest auf dem echten Spielpfad deckt Einsammeln → Respawn → zweites Einsammeln ab und schlägt ohne die Korrektur fehl.
