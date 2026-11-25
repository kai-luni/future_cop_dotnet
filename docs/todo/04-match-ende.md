# Match-Ende

## Problem

Ein Match endet nie. Nach dem Sieg oder der Niederlage laeuft das Level weiter.

## Beleg

- Fehlt laut `docs/pa-playable-roadmap.md:142-168`.
- Haengt an `03-gegnerfeuer-und-spielertod.md` (Niederlage) und an `01-csac-zielsuche.md` (Sieg ueber Scoring).

## Aufgabe

Die Abschlussbedingung des Matches auswerten und einen Endzustand herstellen: Auswertung, Anzeige, Rueckkehr ins Menue.

## Akzeptanz

- Erreichtes Sieg- oder Niederlageziel beendet das Level erkennbar.
- Kein Haenger, keine Exception beim Uebergang.
