# Fehlende Dateien muessen gemeldet werden

## Problem

Fehlt das Disc-Archiv, zeigt das Menue still Ersatztexte an (`Weapon: id 0x41`) und tut so, als waere alles in Ordnung. Der Start scheitert spaeter ohne klare Ansage.

## Beleg

- `tools/FcMainMenu/MenuLabels.cs:28` schreibt `Menu text unavailable: ...` nur auf den Fehlerstrom, sichtbar erst im Terminal.
- `tools/FcMainMenu/MenuFlow.cs:294` meldet `Cannot load mission ...` ebenfalls nur dorthin.
- Reproduziert: Ohne `build/` erscheinen genau diese Ersatztexte im Fenster, waehrend die Meldung nur im Log steht.

## Aufgabe

Fehlende oder unlesbare Dateien im Spielfenster sichtbar machen, nicht nur im Log. Der Hinweis soll sagen, welche Datei fehlt und wie man sie erzeugt (`python3 scripts/fc_extract.py ... build/disc/files`). Kein stiller Rueckfall auf Ersatztexte ohne Hinweis.

## Akzeptanz

- Ohne Archiv erscheint im Fenster eine verstaendliche Meldung mit Dateipfad.
- Mit Archiv erscheint keine Meldung.
- Ein Test deckt den Fall ohne Archiv ab.

## Erledigt

Bereits mit 5461592 behoben und hier nur nachgeprueft: `MenuLabels.Load` fuellt
`MenuLabels.UnavailablePath`, `MenuFlow.LastLoadErrorPath` die Missionsseite, und
`X11PanelLayout.FileNotice`/`Window.DrawFileNotice` zeichnen Pfad und Erzeugungsbefehl
(`python3 scripts/fc_extract.py "<disc>.bin" build/disc/files`) im Fenster.

Tests: `X11MenuPanelTests.UnavailableFrontEndArchiveProducesVisibleExtractionNotice` (fehlend und
unlesbar), `X11MenuPanelTests` mit vorhandenem Archiv (leerer Hinweis) und
`MenuFlowTests` fuer den Missionspfad.
