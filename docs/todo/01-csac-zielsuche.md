# Csac-Zielsuche der MapZones

## Problem

MapZones, die auf Csac-Einheiten zielen, pruefen im Port nur den Spieler. In CONFT feuern die Zones 5, 9 und 235–244 deshalb nie, und ihr Scoring-Skript laeuft nicht.

## Beleg

- Port: `src/FutureCop.View/LevelScene.cs:360-372` — der Kommentar dort benennt die Luecke selbst.
- Retail: `80031DE8.c:111-159` — Kandidatensuche ueber `D_800A8F90`, naechste Entity auf derselben Node-ID, IDs `<= 0` sowie Typen `0x15` und `0x12` uebersprungen.
- Anschluss: Zonen-Skript und Retire in `80031C74.c:106-113`; Freigabe nur ueber MapZone-Flags (`:115`).

## Aufgabe

Die Kandidatensuche aus `80031DE8.c:111-159` portieren und statt der Spieler-Zuweisung verwenden. Der gecachte Treffer (`+0x258+rand&0x3F`) wird jeden Frame erneut geprueft, solange er existiert; der Node-Timer verschiebt nur die Neuauswahl.

## Akzeptanz

- Zone 5, 9 und 235–244 in CONFT feuern nachweislich, sobald eine passende Csac-Einheit in Reichweite steht.
- Test mit Vektoren aus der C-Decompilation, nicht hand-hergeleitet.
- Gegenprobe: Entfernt man die Suche, wird der Test rot.
