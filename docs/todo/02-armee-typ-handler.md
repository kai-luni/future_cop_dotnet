# Typ-Handler der Armee

## Problem

Gekaufte Einheiten spawnen, zeichnen, treffen und scoren — aber der allgemeine Typ-Handler-Switch fuer bewegliche und kaempfende Einheiten ist nicht angeschlossen. Sie bewegen sich nicht und greifen nicht an.

## Beleg

- Fehlt laut `docs/pa-playable-roadmap.md:142-168`.
- Retail: `8002AE54.c` (Typ-Dispatch), `D_8003015C` als Beispiel eines Typ-Handlers.

## Aufgabe

Den Typ-Switch aus `8002AE54.c` anbinden und die Handler der PA-Typen implementieren, mindestens 5, 9 und 28/0x1C.

## Akzeptanz

- Eine gekaufte Einheit laeuft ihre Route ab und feuert.
- Bestehende Spawn-, Treffer- und Scoring-Tests bleiben gruen.
