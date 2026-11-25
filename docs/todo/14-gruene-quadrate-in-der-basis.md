# Gruene Quadrate in der Basis

## Problem

In der Basis stehen gruene Quadrate, die dort nicht hingehoeren.

## Beleg

- `src/FutureCop.View/LevelScene.cs:1159` — `PadZoneColour = { 0x40, 0xFF, 0x40 }`, also exakt dieses Gruen.
- Gezeichnet in `PadMarkers`, `LevelScene.cs:1114-1157`.
- Der Kommentar dort sagt selbst: Viewer-Praesentation, nicht Retail. Ein Pad ist in der C-Fassung eine unsichtbare Trigger-Box, das sichtbare Pad ist Kartengeometrie.

## Aufgabe

Entscheiden und umsetzen: Die Markierungen ganz entfernen, oder nur auf ausdruecklichen Wunsch zeigen (Schalter, nicht Standard). Keine Retail-Farbe erfinden — Retail zeichnet diese Boxen nicht.

## Akzeptanz

- Im Normalstart erscheinen keine gruenen Rechtecke in der Basis.
- Die Pad-Erkennung selbst bleibt unveraendert; bestehende Pad-Tests bleiben gruen.
