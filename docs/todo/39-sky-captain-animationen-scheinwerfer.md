# Sky Captain: Animationen weichen von Retail ab, Scheinwerfer leuchten schwarz

## Problem

Der Sky Captain wirkt im Playtest von Precinct Assault insgesamt stimmig, seine
Animationen passen aber nicht: Auffälligstes Beispiel ist der Scheinwerfer, der
zwar sichtbar ist, aber nur schwarzes Licht abstrahlt statt die Umgebung
aufzuhellen.

## Beleg

- In `src/` existiert kein Licht- oder Scheinwerfer-Renderpfad: eine Suche nach
  `Light`, `Lamp` und `Glow` liefert keinen Treffer. Der Scheinwerfer wird also
  offenbar nur als Geometrie beziehungsweise als unbeleuchtete Fläche gezeichnet.
- Die Ausrichtung des Modells ist vorhanden (`LevelSceneSkyCaptain.cs:753`,
  `HeadingX/Y/Z`), wird aber nur für Radar und HUD ausgewertet
  (`RadarMarkers.cs`) — nicht für eine scheinwerferabhängige Darstellung.
- Die Animationsframes laufen über den Modellpfad, dessen Frame-Auswahl für den
  Sky Captain in `LevelSceneSkyCaptain.cs` bestimmt wird; ein Abgleich mit den
  Retail-Rahmen fehlt bisher.

## Aufgabe

1. In Precinct Assault den Sky Captain im Flug beobachten und Frames festhalten,
   insbesondere den Scheinwerferkegel am Boden und die Modellframes.
2. In Retail die Darstellung des Scheinwerfers suchen: wie der Kegel gezeichnet
   wird (Polygon, Sprite, Texturseite, Paletteneinträge, additive Mischung) und
   welche Farb-/Palettenwerte dabei verwendet werden. Ursache für „schwarzes
   Licht" eingrenzen — fehlende Texture, falsche Palette, falscher Blend oder
   fehlende Beleuchtungsstufe.
3. Den Scheinwerfer im Port umsetzen, mit den Retail-Werten für Kegelgeometrie,
   Farbe und Mischung.
4. Die übrigen Animationen des Sky Captain in einem Durchgang prüfen: Frame-Reihenfolge,
   Frame-Anzahl, Abspielrate und Übergänge gegen die Retail-Rahmen.
5. Belege für jede Korrektur an der C-Quelle festhalten, damit der Anspruch
   „Retail-parität" nachprüfbar bleibt.

## Akzeptanz

- Der Scheinwerfer hellt den Boden sichtbar auf und entspricht in Größe, Farbe
  und Position dem Retail-Vorbild; kein schwarzer Kegel mehr.
- Die übrigen Animationen laufen mit Retail-Reihenfolge und -Rate; ein Test oder
  eine dokumentierte Frame-Tabelle mit Screenshot belegt das.
- Performance bleibt auf dem Stand des Sammelstands 14e7779 (Bench-Vergleich).
