# Sky Captain: gebautes Flugzeug bewegt sich nicht

## Problem

Der Sky Captain baut inzwischen ein Flugzeug zur Basisbewachung, es steht aber still. Beobachtet im laufenden Spiel.

## Beleg

- Die Bewegung ist vorhanden, aber hinter einem Schalter: `src/FutureCop.Game/BuildOrderSpawner.cs:70-90` implementiert `Advance` mit `AircraftSpeed = 0x700` (Zeile 28) und `TankSpeed = 0x400` (Zeile 25), und `AdvanceUnits` (Zeile 210-216) schiebt alle Einheiten weiter.
- Der Aufruf existiert: `src/FutureCop.View/LevelSceneSkyCaptain.cs:505` ruft `Spawner.AdvanceUnits(dt, _sampleGround)`.
- Der Schalter ist standardmaessig aus: `_enableBuildOrderSpawner` (`src/FutureCop.View/LevelSceneSkyCaptain.cs:68`) ist ein `bool`-Feld ohne Initialisierung, wird also mit `false` gestartet. Nur wenn `EnableBuildOrderSpawner` (`:80-89`) an ist, laeuft der Aufruf aus Zeile 505.
- Im laufenden Spiel wird also die Retail-Spawner-Logik (`CsacUnitSpawner`) verwendet, die Einheiten erzeugt, aber **keine eigene Bewegung** hat: `src/FutureCop.Game/CsacUnitSpawner.cs` enthaelt keine Positionsaenderung, nur Nachrichtenverarbeitung (`MessageFrameTick`, Zeile 183) und Burst-Abbau (Zeile 469).

## Aufgabe

Klaeren, welcher Pfad die im Spiel sichtbare Einheit erzeugt. Ist es der CSAC-Spawner, braucht er die Bewegung, die Retail ueber den Mover der Einheit loest; ist es der BuildOrderSpawner, muss `EnableBuildOrderSpawner` im Spielpfad an sein. Nur einen Weg waehlen, nicht beide parallel.

## Akzeptanz

- Ein gebautes Flugzeug aendert seine Weltposition ueber die Zeit; ein Test spannt N Frames und prueft, dass die Position sich mit `AircraftSpeed * dt` verschoben hat.
- Der Tank bewegt sich mit `TankSpeed`, das Flugzeug mit `AircraftSpeed` — ein Vertauschen der beiden macht den Test rot.
- Die Bodenhoehe folgt `_sampleGround` (kein Schweben durch Waende, kein Absinken unter Terrain).
- Ein Test belegt, dass genau ein Pfad aktiv ist: CSAC und BuildOrderSpawner duerfen nicht gleichzeitig Einheiten bewegen.
