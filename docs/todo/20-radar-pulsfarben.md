# Radar: Pulsfarben 17..19 aus func_8004CC68 nicht aktualisiert

## Problem

Retail aktualisiert in jedem Gameplay-Frame die Eintraege `D_800A6F60[17..19]` (`unk44`, `unk48`, `unk4C`) ueber `func_8004CC68` (`gUpdateMarkerPulseColors`) aus `D_8009FAA4` (`gFrameTime`). Im Port bleibt die Farbtabelle statisch auf den Initialwerten aus `func_8004CAE4`, sodass Marker mit Farbindex 17, 18 oder 19 nicht pulsieren.

## Beleg

- Retail Init: `8004CAE4.c:91-93` setzt Initialwerte `0x000000F8`, `0x00F80000`, `0x0000F800`.
- Retail Frame-Update: `8002703C.c:114` ruft pro Frame `func_8004CC68` (`8004CC68.c:32-48`), das `(gFrameTime >> 3) & 0x3F` zu einem Dreieckspegel faltet und `D_800A6F60[17..19]` ueberschreibt.
- Port: `src/FutureCop.View/RadarMarkers.cs` nutzt ausschliesslich die statische `MarkerColorTable`.

## Aufgabe

`func_8004CC68` portieren, im Frame-Pfad (bzw. bei der Aufloesung von `D_800A6F60` fuer `FrameTime`) fuer die Eintraege 17..19 anwenden und im bestehenden Radar-C-Harness gegen unveraendertes `8004CC68.c` absichern.

## Akzeptanz

- Eintraege 17..19 folgen fuer alle Phasen von `(FrameTime >> 3) & 0x3F` bitgenau `8004CC68.c:36-47`.
- Host-Harness und C#-Tests pruefen dieselben Puls-Vektoren; ein Entfernen der Pulsformel laesst beide Seiten rot werden.
