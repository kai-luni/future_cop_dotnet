# Menue: Standardpfad haengt am Arbeitsverzeichnis

## Problem

`MenuLabels` findet das Archiv robust, aber `MenuFlow` und `MenuAudio` nutzen weiter den relativ zum Arbeitsverzeichnis aufgeloesten Standard `build/disc/files/CW/FE.MIS`. Startet man aus einem anderen Verzeichnis, stimmen Labels und Szene nicht ueberein.

## Beleg

- `tools/FcMainMenu/Program.cs:8-15`.
- `tools/FcMainMenu/MenuLabels.cs:33-43` (dort bereits geloest: Geschwisterordner `../CW/FE.MIS`, dann Aufstieg vom Programmverzeichnis).

## Aufgabe

Denselben Aufloesungsweg fuer Szene und Audio verwenden, den `MenuLabels` schon geht.

## Akzeptanz

- Start aus einem fremden Verzeichnis zeigt Szene, Labels und Audio konsistent.
- Ohne Archiv erscheint eine Warnung (siehe `15-fehlende-dateien-melden.md`).
