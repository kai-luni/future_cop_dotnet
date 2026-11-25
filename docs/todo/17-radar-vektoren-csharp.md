# Radar: gemeinsame Golden-Vektoren in C# ausführen

## Problem

Die 83 Radar-Golden-Vektoren werden bisher nur vom C-Harness geprüft. Ein grüner C#-Testlauf belegt daher keine Übereinstimmung mit diesen Fällen.

## Beleg

- `tests/golden/radar-markers.vectors:8`–`10` beschreibt eine gemeinsame Fixture; `tests/host/radar-markers/run.sh:74` reicht sie an den C-Harness weiter.
- `tests/FutureCop.Assets.Tests/RadarMarkerTests.cs:9`–`21` beschreibt handabgeleitete Tests; die Klasse liest die gemeinsame Fixture nicht.
- `tests/FutureCop.Assets.Tests/FutureCop.Assets.Tests.csproj:32`–`65` kopiert andere Golden-Dateien, darunter `hud-radar.vectors`, aber nicht `radar-markers.vectors`.

## Aufgabe

Die vorhandene `tests/golden/radar-markers.vectors` in das xUnit-Ausgabeverzeichnis übernehmen. Dieselbe Datei wie der C-Harness lesen und die Ergebnisse für `COLOR`, `LINE`, `TRI`, `BEARING` und `DRAW` gegen die C#-Implementierung prüfen, ohne eine zweite Fixture anzulegen.

## Akzeptanz

- C-Harness und C#-Tests prüfen alle 83 Fälle derselben Datei.
- Fehlende Fixture, unbekannte Direktiven und eine falsche Fallzahl führen zu einem Testfehler.
- Eine absichtlich geänderte Erwartung führt in beiden Testläufen zu einem Fehler.
