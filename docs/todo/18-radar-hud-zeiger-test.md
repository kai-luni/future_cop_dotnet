# Radar: HUD-Integrationstest an LINE_F2 anpassen

## Problem

Der HUD-Integrationstest erwartet für den Richtungszeiger noch das helle Grau des früheren Pfads. Außerdem kann die Prüfung des schwarzen Zielmarkers auf schwarzem Hintergrund auch ohne diesen Marker grün sein.

## Beleg

- `tests/FutureCop.Assets.Tests/RadarMarkerTests.cs:433` erwartet RGB (248, 248, 248), der Testlauf liefert (128, 128, 128). Dieselbe alte Erwartung steht in Zeile 448 und 454.
- Retail ruft für den Richtungszeiger `gEmitAngledLine` auf (`src/game/8005A9C4.c:111`–`112`); der Emitter setzt den Paketwert fest auf `0x40808080` (`src/game/8005A740.c:52`–`57`, Decompilation).
- `RadarMarkerTests.cs:420` verwendet Farbindex 0. Retail setzt Index 0 auf Schwarz (`src/game/8004CAE4.c:69`, Decompilation). Damit unterscheiden die Pixelprüfungen in `RadarMarkerTests.cs:449` und `:455` nicht zuverlässig zwischen einem gezeichneten und einem fehlenden Marker.

## Aufgabe

Die Pixelerwartungen an das Retail-Linienpaket anpassen. Für den Zielmarker einen Farbindex oder Hintergrund verwenden, mit dem die Blinkphasen sichtbar unterschieden werden.

## Akzeptanz

- Der HUD-Integrationstest besteht mit der vorhandenen Implementierung.
- Werden der Richtungszeiger oder das Zielmarker-Zeichnen unterdrückt, schlägt die jeweilige Prüfung fehl.
