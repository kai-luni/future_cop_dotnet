# Radar: weisser Pfeil zeigt falsche Richtung und sitzt falsch

## Problem

Im HUD zeigt ein weisser Pfeil eine Richtung, die nicht stimmt, und er sitzt zu weit vom eigenen Mech entfernt. Beobachtet im laufenden Spiel.

## Beleg — was schon stimmt

Der Pfeil ist **kein** Zielzeiger auf den Sky Captain, sondern die eigene Kurslinie des Mech. Die Portierung entspricht hier bereits Retail:

- `src/FutureCop.View/RadarMarkers.cs:296-318` (`EmitAngledLine`) setzt Radien **24/30**, Y-Bias **`0x2C0000`**, Tag `0x03000000`, Farbe `0x40808080` — Zeile fuer Zeile wie `8005A740.c:52-57`.
- Der Winkel ist `-unkB0 - 0x800` (`src/FutureCop.View/RadarMarkers.cs:281`, Retail `8005A9C4.c:111`).

Hier ist also nichts zu holen. Die Abweichung muss vor oder nach dieser Funktion liegen.

## Beleg — zwei offene Kandidaten

**1. Der Kurs, der hineingeht.** `tools/FcMainMenu/MenuFlow.cs:485` uebergibt `unchecked((short)(walker?.Angle ?? 0))` als `Heading`. Retail liest dafuer das Feld `object->unkB0` (`8005A9C4.c:24`, gelesen in `:111`). Ob `walker.Angle` dieselbe Einheit, Nullage und Umlauflaenge (0x1000) hat wie `unkB0`, ist nirgends belegt — die Zuordnung wurde nie gegen Retail geprueft. Bei falscher Nullage zeigt der Pfeil systematisch in eine gedrehte Richtung, was zum beobachteten Symptom passt.

**2. Der Ursprung, der hineingeht.** `RadarMarkers.OriginXForSide` (`src/FutureCop.View/RadarMarkers.cs:274`) waehlt `0x780000` (120 px) nur bei `side == 1`, sonst `0x280000` (40 px). Retail waehlt genauso anhand von `object->unkAC->unkC1 == 1` (`8005A9C4.c:109`). Der Port fuellt dieses Feld mit `ValueC1 = walker?.Port` (`tools/FcMainMenu/MenuFlow.cs:505,519`). `Port` ist im Mech-Modell der **Controller-Port** (`src/FutureCop.Game/Mech/MechBody.cs:92`, benutzt fuer Pads und Sound in `MechHover.cs:29`). Ob Controller-Port und HUD-Seite in Retail dasselbe Feld sind, ist offen — falls nicht, landet der Pfeil bei Spieler 2 auf der falschen X-Position.

## Aufgabe

Die beiden Kandidaten getrennt messen, dann nur den schuldigen fixen:

1. **Kurs:** Ein Verdrahtungstest setzt einen bekannten Walker-Winkel (z. B. 0x000, 0x400, 0x800, 0xC00) und prueft den resultierenden `HeadingLine.X0/Y0/X1/Y1` gegen die Werte, die `func_8005A740` mit `-angle - 0x800` liefert. Weicht die C#-Seite ab, ist die Einheit oder Nullage von `walker.Angle` falsch und wird **an der Uebergabestelle** `MenuFlow.cs:485` korrigiert, nicht in `RadarMarkers`.
2. **Ursprung:** Pruefen, ob `unkAC->unkC1` in Retail wirklich der Controller-Port ist oder eine eigene HUD-Seite. Belege aus dem Decompilation-Baum ziehen (`8005A9C4.c:109`, `80053188.c:160-195`, `80046CF0.c:76`). Ist es eine eigene Seite, darf `ValueC1` nicht aus `walker.Port` kommen.

## Akzeptanz

- Ein Test bindet Walker-Winkel an `HeadingLine`-Koordinaten fuer mindestens vier Winkel und beide `side`-Werte; die Vektoren stammen aus dem bestehenden Radar-Harness (unveraendertes `8005A740.c`), nicht aus dem Port.
- Dreht sich der Mech, dreht sich die Linie mit — ein konstanter Winkel im Test macht ihn rot.
- Die Herkunft von `ValueC1` ist im Code kommentiert und mit einer Retail-Zeile belegt; ein Test deckt Spieler 2 (`Port != 0`) auf der richtigen X-Position ab.

## Nicht Teil dieser Aufgabe

Die Zeichenroutine selbst (`EmitAngledLine`, Bresenham in `InGameHud.DrawRadarLine`) bleibt unveraendert — sie stimmt bereits mit Retail ueberein.
