# Gegnerfeuer und Spielertod

## Problem

Gegnerische Einheiten teilen keinen Schaden aus, und der Spieler kann nicht sterben. Damit gibt es keinen Druck und kein Scheitern.

## Beleg

- Fehlt laut `docs/pa-playable-roadmap.md:142-168`.
- Der Schadenspfad des Spielers existiert bereits fuer eigene Waffen; die Gegenseite fehlt.

## Aufgabe

Schuesse gegnerischer Einheiten gegen den Spieler zulassen, Schaden auf die Spielerpanzerung anwenden und den Tod des Spielers behandeln.

## Akzeptanz

- Der Spieler verliert bei Treffern sichtbar Panzerung.
- Bei Panzerung null wird der Tod ausgeloest statt weiterzulaufen.
