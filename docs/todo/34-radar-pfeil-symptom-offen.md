# Radar: Richtung und Abstand des weißen Pfeils weiterhin ungeklärt

## Problem

Im Playtest zeigt der weiße Radar-Pfeil in die falsche Richtung und sitzt zu
weit vom eigenen Mech entfernt. Die Ursache ist weiterhin offen.

## Beleg

- Todo 22 hat die Kursverdrahtung gegen Retail geprüft: `walker.Angle`
  entspricht dem Winkel `unkB0`, den Retail für den Pfeil liest
  (`8005A9C4.c:24,111`).
- Auch die Seitenwahl stimmt: `unkC1` entspricht dem Spielerslot, den Retail
  beim Spawn setzt und für den Radar-Ursprung verwendet
  (`800579D4.c:176-190`, `8005A9C4.c:109`).
- Die Vergleiche mit den C-Vektoren bestätigen diese beiden Eingaben, erklären
  aber nicht die Abweichung im gerenderten HUD.

## Aufgabe

Das Symptom im Spiel reproduzieren und für denselben Frame den Walker-Winkel,
den Spielerslot und einen Screenshot mit Pfeil und Mech festhalten. Danach
Radarrotation und HUD-Transformation bis zur gezeichneten Pfeilposition
verfolgen und mit Retail vergleichen.

## Akzeptanz

- Die Reproduktion dokumentiert Walker-Winkel, Spielerslot und Screenshot
  desselben Frames.
- Der Vergleich ordnet die Abweichungen bei Richtung und Abstand einer
  konkreten Stufe der Zeichenkette zu oder belegt, welche der geprüften
  Stufen mit Retail übereinstimmen.
