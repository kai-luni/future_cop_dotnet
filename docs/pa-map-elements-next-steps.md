# PA Map-Elemente: Naechste Ausbauschritte (.NET)

Kompakte Ausbaureihenfolge fuer `future_cop_dotnet` (vollstaendige C-Referenzen, Zeilennummern und Status Quo siehe `docs/pa-playable-roadmap.md`):

1. **Map-Object-Spawn & Occupancy-Registry** (`FutureCop.Game`)
   - Gemeinsame Entity-/Slot-Registry (`D_800A0C70`, sofort vs. verzoegert + Ladeende-Replay) statt isolierter Listen in `LevelScene`.

2. **Basen-Besitz & Capture-Vervollstaendigung** (`CaptureObjective.cs` + `LevelScene.cs`)
   - Der 1P-`gSinglePlayerEntity`-Zweig ist bereits auf `main` (PR #6). Noch offen gegenueber Retail: `0x12`-Carrier-Dereferenz, Child-Part-Transfer (`obj->unk104`), Post-Capture-Script (`unk9C`) und Msg-5-Scoring (`8002D664.c:179-225`).

3. **Retail-Spawner-Laufzeit (`Csac` -> `D_8009F800`)** (`FutureCop.Game`)
   - Loest die auf `main` vorhandene opt-in Uebergangsloesung `BuildOrderSpawner` (PR #6) spaeter durch echte `Csac`-Spawner (`80060E74.c` / `800616A0.c` / `8006125C.c` + `8002AE54.c`) ab, sobald RPNS-VM und Kauf-Pfad geklaert sind.

4. **Bewegliche Elemente: Fahrstuehle (Typ 10) & Turret-Carrier (Typ 20)** (`FutureCop.Game` + `LevelScene`)
   - Vor Implementierung erst per `.MIS`-Inspektion pruefen, welche PA-Karten Typ 10/20 tatsaechlich platzieren.

## Offene C-Traces vor der Umsetzung
- **Kauf-Gate & RPNS-Script-VM (`8006125C.c:47`, `80069D68.c`)**: **Likely** liegt das Preis-Gate bzw. der Punkte-Abzug im `arg1`-Gate-Script ueber VM-Op `25` (`gGameSession[a]--`, `80069D68.c:255-257`) und `52` (`gGameSession[a] -= b`, `:279-281`) auf `gGameSession[25/26]` (`+0x64`/`+0x68`), ggf. gekoppelt an einen L1-Input-Handler; zu beweisen durch Dekodierung der `Csac`-Scripts aus `CONFT.MIS`.
- **Weitere offene Punkte**: `CONFT.MIS`-Platzierung fuer Typ 10/20, `gScreenState`-Rollen (`0xD` sowie 4 vs. 5 beim Laden), 1P-HUD-Zuordnung von `+0x68` (`unk64[1]`), Aufrufer von Msg 11 an Typ 10 (`+0xC4`) sowie Ziel-/Anim-Logik in `80038A00.c` (Details in `docs/pa-playable-roadmap.md`).
