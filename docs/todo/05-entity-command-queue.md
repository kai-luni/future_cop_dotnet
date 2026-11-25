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
