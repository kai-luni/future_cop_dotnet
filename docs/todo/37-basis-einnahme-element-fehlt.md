# Precinct Assault: Element zum Einnehmen der Basen fehlt

## Ziel

Precinct Assault, 1 Spieler mit Sky Captain, lauffähig machen. Diese Task wird in genau diesem Modus geprüft.

## Problem

In den einnehmbaren Basen fehlt vermutlich ein Element, das der Spieler auslöst, um die Basis einzunehmen.

## Beleg

Einschätzung von Kai beim Spieltest von PR #22 (Precinct Assault, 1 Spieler, Sky Captain). Noch nicht belegt, ob und welches Objekt im Original existiert.

## Aufgabe

Im PS1-Original und in den Retail-Kartendaten klären, wie eine Basis eingenommen wird und welches Objekt dafür vorhanden ist. Fehlt es im Port, Laden, Darstellung und Auslösung originalgetreu ergänzen.

## Akzeptanz

- Mechanik und Objekt sind mit C-Beleg (`datei.c:zeile`) und Retail-Daten dokumentiert.
- Im Port ist das Objekt in den Basen sichtbar, und die Einnahme funktioniert wie im Original.
- Ein Test auf dem echten Spielpfad deckt die Einnahme ab und schlägt ohne die Änderung fehl.
