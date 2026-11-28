# Entity-Kommando: Typfilter und Queue

## Problem

Der 0x3D-Dispatch weicht in zwei Punkten von Retail ab: Der Typfilter fehlt, und das Kommando wird sofort ausgefuehrt statt eingereiht. Dadurch laeuft das Retire-Skript verschachtelt und kann Score-Aenderungen des aufrufenden Skripts ueberschreiben.

## Beleg

- Port: `src/FutureCop.Game/PurchasePadSystem.cs:260-283`.
- Retail: `8004DA84.c` verwirft Kommandos an die Typen 2/3/4/7/15/23 und filtert 18/21.
- Retail: `8004DA84.c` reiht ein, ausgefuehrt wird erst in `8004CECC.c:106-110`.

## Status

Latent. In den ausgelieferten Daten nicht ausgeloest: Die 0x3D-Rekursionssonde erreichte maximale Tiefe 1, der CONFT-Kauf rechnet korrekt 100 → 70.

## Aufgabe

Typfilter und Queue-Reihenfolge aus Retail uebernehmen, die direkten `Retire`-Aufrufe im Skript ersetzen.

## Akzeptanz

- Kommandos an die gefilterten Typen werden verworfen.
- Ein Retire aus einem Skript laeuft nicht mehr verschachtelt in `RunEntityScript`.
- Der CONFT-Kauf bleibt 100 → 70.

## Erledigt

- Typfilter und Einreihung liegen in `PurchasePadSystem.SendEntityCommand` (`8004DA84.c:21-69`,
  `8004DA58.c:26-39`), die Ausfuehrung im Frame-Pass `PurchasePadSystem.RunEntityCommands`
  (`8004CECC.c:67-241`, 0x3D in `:108-114`); `LevelScene` ruft ihn nach der Pad-Auswertung und vor
  `SweepRetired` auf.
- Voller Slot-Tisch faellt mit Retry auf die 16er-Nachrichtentabelle zurueck
  (`gEnqueuePendingMessage` 8004D9EC.c, `gStepPendingMessages` 8004DB74.c, 600 Ticks).
- Tests: `tests/FutureCop.Assets.Tests/EntityCommandQueueTests.cs` (Nicht-Verschachtelung, Typfilter
  2/3/4/7/15/23, Filter 18/21, Retry-Tabelle, Kompaktierung); der CONFT-Kauf 100 → 70 bleibt in
  `PurchasePathTests`/`SceneCapture*` abgedeckt.
