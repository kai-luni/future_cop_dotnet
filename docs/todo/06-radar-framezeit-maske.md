# Radar: FrameTime-Maske widerspricht dem Blinkmuster

## Problem

`MenuFlow.cs` maskiert `FrameTime` auf `& 0xFF` mit der Begruendung, nur Bit 0x80 zaehle. Die Radar-Marker blinken aber ueber `& 0x180`. Bit 0x100 ist damit nie gesetzt, das Blinkmuster weicht von Retail ab.

## Beleg

- Port: `tools/FcMainMenu/MenuFlow.cs:425-428`.
- Radar: `src/FutureCop.View/RadarMarkers.cs:188` gegen `8005A9C4.c:125`.

## Status

Heute folgenlos: `HudPlayerBlock.RadarMarkers` wird ausserhalb der Tests nirgends befuellt, es erscheint nur der Richtungszeiger.

## Aufgabe

Die Maske an den tatsaechlichen Verbrauchern ausrichten, sodass alle genutzten Bits erhalten bleiben. Nicht einfach auf `0x1FF` verbreitern, ohne die Verbraucher zu pruefen.

## Akzeptanz

- `& 0x180` liefert in beiden Bit-Lagen die erwarteten Werte.
- Test deckt beide Blinkphasen ab.
