# Panzer und Hubschrauber: keine Richtungsanimation, Modell zeigt immer gleich

## Problem

Im Playtest von Precinct Assault fahren Panzer und fliegen Hubschrauber in ihre
Fahrtrichtung, das Modell dreht sich aber nicht mit: beide zeigen unabhängig von
Kurs und Bahn immer in dieselbe Richtung.

## Beleg

- Die Simulation kennt die Richtung bereits: `CsacUnitSpawner.cs:300-324` setzt
  `DirX`/`DirZ` aus dem Rail- beziehungsweise Hover-Zustand, der Sky-Captain-Pfad
  exportiert sogar eine dreiachsige Richtung (`LevelSceneSkyCaptain.cs:753`,
  `HeadingX/Y/Z`).
- Der Zeichenweg nutzt sie nicht. Eine Suche nach `Heading` in `src/` findet nur
  Radar/HUD (`RadarMarkers.cs`, `InGameHud.cs`) und den genannten Export;
  `QueueModel`/`PartPrep` bekommen keine Ausrichtung übergeben. Der Mech-Pfad
  dreht dagegen über `MechWalker.RotationMatrix` (`LevelScene.cs:1305-1310`).
- Damit ist das Symptom strukturell erklärbar: die Richtung existiert, wird aber
  vor dem Modellaufbau verworfen.

## Aufgabe

1. In Precinct Assault reproduzieren: einen Panzer auf einer geraden Bahn und
   einen Hubschrauber im Kurvenflug beobachten und Frames festhalten.
2. In Retail die Stelle suchen, an der die Modellrotation einer Einheit aus
   ihrer Richtung gebaut wird (Kandidaten: der Zeichenweg der Einheitenliste und
   `gOrientEntityBasis`), und mit dem Port-Zeichenweg vergleichen.
3. Die Rotation im Port ergänzen: Modelle von Panzer und Hubschrauber über die
   vorhandene `DirX`/`DirZ` (Flieger zusätzlich Nickwinkel) ausrichten, ohne die
   Performance des Modellpfads zu verschlechtern.
4. Prüfen, ob weitere Einheitentypen derselben Armee betroffen sind (Cnet-Rail,
   Hover, Panzer) und die Lösung für alle gleich anwenden.

## Akzeptanz

- Panzer und Hubschrauber drehen sichtbar mit der Fahrtrichtung, in Kurven
  kontinuierlich statt sprunghaft.
- Ein Test auf dem Zeichenweg belegt, dass zwei verschiedene Richtungen
  verschiedene Rotationen erzeugen (rot ohne die Änderung).
- Precinct Assault läuft weiter mit den Werten des Performance-Stands
  (Bench-Vergleich vor/nach).
