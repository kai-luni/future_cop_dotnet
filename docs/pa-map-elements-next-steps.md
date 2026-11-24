# PA Map-Elemente: Naechste Ausbauschritte (.NET)

Reihenfolge fuer den schrittweisen Ausbau von `future_cop_dotnet` entlang des verifizierten C-Codes:

1. **Map-Object-Spawn & Occupancy-Registry** (`FutureCop.Game`)
   - C-Vorlagen: `8003F79C.c` (Init), `8002A5F4.c` (Basis-Spawn), `8003FA34.c` (sofort vs. verzoegert), `8003B838.c` (Slot-Spalte aus `D_800A0C70`), `8003BC0C.c` (Write), `8003FAD4.c` (Replay am Ladeende).
   - Ziel: Gemeinsame Entity-/Slot-Registry statt isolierter Listen in `LevelScene`, damit statische und dynamische Map-Objekte einheitlich entstehen und ihre Belegung anmelden.

2. **Basen-Besitz & Capture-Vervollstaendigung** (`CaptureObjective.cs` + `LevelScene.cs`)
   - C-Vorlagen: `8002D1A0.c` (Proximity-Capture), `8002D664.c` (Msg 5/10).
   - Ziel: Auf `main` fehlen gegenueber Retail noch `0x12`-Carrier-Dereferenz, Child-Part-Transfer (`obj->unk104`), Msg-5-Scoring (`8002D664.c:179-225`), Post-Capture-Script-Hook (`unk9C`) sowie der 1P-`gSinglePlayerEntity`-Zweig (liegt in offenem PR #6, noch nicht auf `main`).

3. **Retail-Spawner-Laufzeit (`Csac` -> `D_8009F800`)** (`FutureCop.Game`)
   - C-Vorlagen: `80060E74.c` (Registrierung `'CASt'`/`'LSRa'`/`'TCAt'`), `800616A0.c` (Per-Frame-Drain & Timer), `8006125C.c` (Script-Gate + Spawn + Statistik), `8002AE54.c` (Typ-Dispatch).
   - Ziel: Loest die Uebergangsloesung `BuildOrderSpawner` (PR #6) spaeter durch echte `Csac`-Spawner ab, sobald die RPNS-Script-VM (`80069D68.c`) und der Kauf-Pfad geklaert sind.

4. **Bewegliche Elemente: Fahrstuehle (Typ 10) & Turret-Carrier (Typ 20)** (`FutureCop.Game` + `LevelScene`)
   - C-Vorlagen Typ 10: `80041B6C.c` (Dispatch), `80041488.c` (Init), `80041880.c` (Tick/Ping-Pong/Dwell), `80041728.c` (Command-Queue `0x52`/`0x53`).
   - C-Vorlagen Typ 20: `80039504.c` (Setup), `800395A8.c` (Carrier-Dispatch), `80039418.c` (Kind-`0x15`-Spawn), `80039298.c` (Kind-Dispatch), plus `80038A00.c` (Turret-Update).
   - Vorbedingung: Vor Implementierung erst pruefen, welche PA-`.MIS`-Dateien Typ 10/20 tatsaechlich enthalten.

## Was noch wirklich offen / unbekannt ist
- **Spieler-Kaufaktion (L1 an Basiskonsolen)**: In `80060E74`/`800616A0`/`8006125C` steht kein Preis-Check; der eigentliche L1-Input-Handler inkl. Punkte-Dekrement auf `D_8009FA94` (`+0x64` / `+0xA0..+0xDF`) ist noch nicht getraced (naechster C-Audit-Schritt).
- **RPNS-Script-VM (`80069D68.c`)**: Exakte Verknuepfung von `Csac`-Script-Offsets mit Kauf-/Spawn-Bedingungen in PA.
- **Level-Platzierung**: Unbewiesen, ob `CONFT.MIS` u. a. PA-Karten Typ-10-Lifte oder Typ-20-Carrier platzieren.
- **Detail-Luecken im C-Code**: Bedeutung von `gScreenState == 0xD`, Leser von `gGameSession+0x68/+0x80` (Msg 5), Aufrufer von Msg 11 an Typ 10 (`+0xC4`), sowie die interne Ziel-/Anim-Logik in `80038A00.c`.
