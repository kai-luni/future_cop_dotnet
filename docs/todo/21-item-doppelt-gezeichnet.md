# Karte: zwei Item-Modelle an einer Position, nur eins einsammelbar

## Problem

Auf der Karte liegen Items doppelt uebereinander: der Spieler sieht zwei Modelle, bekommt beim Drueberfahren aber nur ein Payload. Beobachtet im laufenden Spiel auf der Precinct-Assault-Karte.

## Beleg

- Platzierungsdaten sind es nicht: `PA/CONFT.MIS` enthaelt genau **11** Records mit Payload-Typ `0x10`, und alle 11 haben **verschiedene** x/z-Koordinaten. Keine zwei Records teilen sich eine Position (Nachzaehlung ueber die tACT-Records der Disc).
- Das Modell kommt aus einem zweiten Feld: `src/FutureCop.Game/WeaponPickups.cs:582` setzt `modelId = 0` vorab, `:583-596` sucht im angehaengten `aRSL`-Chunk nach einem `Cobj`-Eintrag und liest daraus die Modell-Id (`:593`). Fehlt dieser Chunk oder der Eintrag, bleibt es bei **`ModelId = 0`** (`:617`).
- `src/FutureCop.View/LevelScene.cs:935-949` zeichnet jedes nicht gesammelte Item ueber `_file.Models.FirstOrDefault(m => m.Id == item.ModelId)`. Bei `ModelId = 0` liefert das Suchen ein beliebiges Modell mit Id 0 — es ist nicht ausgeschlossen, dass dort ein gueltiges Modell liegt und an derselben Weltposition ein zweites, falsches Modell gezeichnet wird.
- Die Auszahlung haengt dagegen allein an der Position (`WeaponPickups.Update`, `src/FutureCop.Game/WeaponPickups.cs:504-524`): `Overlaps(item, x, y, z)` pro Item, `item.Collected` erst danach. Ein doppelt gezeichnetes Modell zahlt deshalb nur einmal.

## Aufgabe

Ursache eingrenzen, ob das zweite Modell aus `ModelId = 0` (Fallback auf ein fremdes Modell) oder aus einem zweiten Zeichenweg derselben Platzierung stammt. Danach nur den fehlerhaften Zweig korrigieren: ein Item darf genau ein Modell an genau einer Weltposition zeichnen.

## Akzeptanz

- Fuer die 11 `0x10`-Records von `CONFT.MIS` wird genau ein Modell je Record gezeichnet; ein Test zaehlt die gezeichneten Pickup-Modelle gegen `Pickups.Items.Count(i => !i.Collected)`.
- Ein Record ohne `aRSL`-`Cobj`-Eintrag zeichnet nichts, statt ein fremdes Modell zu borgen, oder wird beim Laden uebersprungen.
- Der Einsammel-Pfad bleibt unveraendert: pro Item genau eine Auszahlung.
