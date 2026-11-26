# MoverPlacementTests: PA-Karten tatsächlich laden

## Problem

Die fünf Fälle von `MoverPlacementTests` prüfen ihre Karten im normalen Testlauf nicht; mit `FC_REQUIRE_RETAIL=1` schlagen sie trotz vorhandener Disc am Pfad-Guard fehl.

## Beleg

- `tests/FutureCop.Assets.Tests/MoverPlacementTests.cs:11`–`23` übergibt großgeschriebene Kartennamen an `ModulePath.Resolve` und kehrt bei fehlender Auflösung vor den Assertions zurück.
- `src/FutureCop.Assets/ModulePath.cs:12`, `:18`–`19` und `:26`–`29` wählt den Präfix vor dem dateisystemseitigen, von Groß-/Kleinschreibung unabhängigen Vergleich. Die großgeschriebenen Namen landen daher unter CW statt PA.
- `src/game/800833A8.c:53`–`62` (Decompilation) vergleicht zuerst die Modulnamen mit `gBiosStrcmp` und wählt danach den Präfix.

## Aufgabe

Die Tests mit den kanonischen PA-Modulnamen aufrufen, ohne den exakten Retail-Präfixvergleich zu ändern.

## Akzeptanz

Mit Retail-Disc und `FC_REQUIRE_RETAIL=1` erreichen alle fünf Fälle ihre Karten-Assertions, statt im Pfad-Guard zurückzukehren.
