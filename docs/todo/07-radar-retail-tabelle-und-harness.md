# Radar: echte Farbtabelle und C-Harness

## Problem

Die Radar-Tests sind nicht an die C-Decompilation gebunden. Die Farbtabelle ist ein Platzhalter.

## Beleg

- `src/FutureCop.View/RadarMarkers.cs:169` — `D_800A6F60` ist erfunden.
- `tests/FutureCop.Assets.Tests/RadarMarkerTests.cs:128ff` — hand-hergeleitete Vektoren, kein Harness.
- Der Richtungszeiger wird als Dreieck gezeichnet, Retail nutzt eine Linie (`gEmitAngledLine`, `8005A9C4.c:111-112`).

## Aufgabe

`D_800A6F60` aus der Disc bzw. der Decompilation belegen und einsetzen. Ein Host-Harness nach dem Muster der bestehenden sechs bauen (`tests/host/`), das unveraenderte `src/game/*.c` gegen die C#-Seite laufen laesst. Den Zeiger als Linie zeichnen.

## Akzeptanz

- Keine erfundenen Farbwerte mehr, jeder Wert mit Quelle.
- Harness rc=0 und an die C-Seite gebunden; dieselbe `.vectors`-Datei speist beide Seiten.
- Entfernt man eine Konstante, wird der Harness rot.
