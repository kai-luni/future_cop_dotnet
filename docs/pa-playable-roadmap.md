# Precinct Assault 1P Playable Roadmap + Second-Mech-Port

Scope: Pure analysis of `future_cop_dotnet` against `future-cop-lapd-ps1/src/game`.
All claims about PS1 retail behavior reference `future-cop-lapd-ps1/src/game/*.c`.

---

## Status Quo

### Was bereits existiert (.NET)
- **PA Front-End & Missionsdaten**: `src/FutureCop.Game/PrecinctAssaultFrontEnd.cs`
  und `src/FutureCop.Game/PrecinctAssaultData.cs` liefern PA 1P/2P Menues, die 16
  Mission-Records (`conft`, `slim`, `hk`, `ovmp`, `joke`) und 4 Loadout-Listen.
- **Menu-Flow & HUD**: `tools/FcMainMenu/MenuFlow.cs` wechselt aus PA in
  `LevelScene`, treibt den Walker via `Steps` (8-Tick-Akkumulator) und rendert
  `InGameHud`, setzt aber `Session = new() { Mode = 0 }` (`MenuFlow.cs:455`).
- **Level-Szene & Player-Mech**: `src/FutureCop.View/LevelScene.cs` laedt
  Terrain, `MissionEntities`, `ObjectiveField`, `PickupItems` (`type 0x10`) und
  `LevelWeapons` (`WeaponSlots`, `Shot`, `Beam`). `src/FutureCop.Game/Mech/`
  (`MechBody`, `MechWorld`, `MechPlayer`, `MechSpawn`, `MechInput`, `MechWalker`,
  `MechHover`, `MechAnimation`, `MechGround`, `MechCollision`, `MechMath`, plus
  `PlayerForm`) portiert 1P-Bewegung, Formwechsel, Wand-/Boden-Kollision und
  Teile-Assembly vollstaendig. `AttachWalker` erzwingt `body.Port == 0`
  (`LevelScene.cs:147`), `MechWalker.cs:460` und `MechHover.cs:24` werfen
  `NotSupportedException` bei `world.Players != 1`.
- **Wireframe-Ansicht**: `src/FutureCop.View/WireframeScene.cs` zeigt nur
  statische/animierte `Cobj`-Modelle und lokale Test-Projektile (`_shots`).
- **Headless AI-/Gegner-Module (unverbunden)**:
  - `src/FutureCop.Game/HoverEnemy.cs` + `HoverEnemyTick.cs` (`func_800324E0`,
    Default `Type = 0x25` in `HoverEnemy.cs:10`)
  - `src/FutureCop.Game/DroneStrategy.cs` (`func_800336DC`)
  - `src/FutureCop.Game/EnemyAttackScheduler.cs` (`func_80060238`,
    `func_800600E8`, `func_80060668`, `func_8004E5F0`, `func_8004E80C`,
    `func_8005FFB8`)
  - `src/FutureCop.Game/EnemyWeaponFire.cs` (`func_8005F6E8`, abstrahierte
    Spawner-Delegates)
  - `src/FutureCop.Game/EnemyCorpseCleanup.cs` + `EnemyMath.cs`

### Was fuer "PA 1P komplett spielbar" fehlt
1. **Aktive Entity-Liste & Tick-Loop**: `LevelScene` besitzt keine Entity-Registry
   analog `D_8009FA40` / `D_800A8F90` und keinen Broadcast von Message 6 pro Frame.
2. **Spawner (`Csac`) & Factory-Dispatch**: `MissionEntities.cs` parst `Cact` und
   `Csac` inkl. 3 `RPNS`-Script-Offsets (`Scripts`), aber es gibt keinen
   Laufzeit-Spawner fuer `Csac` (`D_8009F800`) und keinen Type-Handler-Switch.
3. **Second Mech (Sky Captain `type 0x25`)**: Initialisierung (`800331CC`),
   Wrapper-Tick (`8003532C`), Respawn-Timer (`msg 4`), Treffer/Crash (`msg 9/11/14`)
   und Viewport-Rendering fehlen komplett.
4. **PA-Armee & Outposts**: Types `5` (Rail-Fahrzeug), `9` (Hover-Drohne),
   `28`/`0x1C` (Multi-Turret-Panzer), `36`/`0x24` (Outpost/Turm) und `8`
   (Base-Defender) sind weder gespawnt noch gerendert; `0x24` Outpost-Capture
   (`8002D1A0.c`) fehlt.
5. **Treffer-, Schadens- & Todeslogik**: `LevelWeapons` prueft keine Kollision
   gegen gegnerische Entities; `EnemyWeaponFire` hat nur unverdrahtete Delegates
   fuer Projektil-Spawner (`8005E9E4`, `8005EB74`, `8005ECB8`, `8005EE04`,
   `8005F018`) und keinen Lead-Solver (`8005E2A0`); Player-Death/Respawn
   (`80058D78`, `80058714`) fehlt.
6. **PA Spielregeln, Wirtschaft & Sieg/Niederlage**: `DroneStrategy` schreibt
   Punkte-/Kauf-Felder auf `D_8009FA94` (`+0xA0..+0xDF`), aber kein `Csac`-Spawner
   liest diese ueber eine RPNS-Script-VM (`80069D68`); `LevelScene.ScoreSession`
   ist nicht in `MenuFlow` / Endscreen (`80028164`) angeschlossen.
7. **Audio / Voice**: `WeaponSoundEvent` in `LevelWeapons` ist stumm; Retail-SFX/
   Announcer/Mech-Voices (`80046C84`, `80047088`) sind nicht angeschlossen.

---

## C-Referenz-Anschluesse

### Wo der Retail die Gegner & Spawner tickt
- **Top-Level Frame-Loop**: `80028618.c:128-140` (`gRunGameFrameLoop`) ruft
  `func_8002703C` (Timing/Pad) und bei `gPlayerCount == 1`
  `gRunSoloSessionFrame` (`800271A4.c`).
- **Solo-Session-Tick (`800271A4.c:177-212`)**:
  - `800271A4.c:177`: `gStepScriptVmCountdowns(gFrameDelta)` (`func_80069D0C`)
  - `800271A4.c:178`: `gCullActiveEntitiesSolo()` (`func_8004DF28`) baut aus
    `D_8009FA40` die aktive Liste `D_800A8F90` (alles mit `flags & 8` oder
    2D-Distanz zum Spieler `< 0x38000`).
  - `800271A4.c:179`: `gDispatchQueuedEntityCommands()` (`func_8004CECC`)
  - `800271A4.c:180`: `gBroadcastEntityMessage(6, gFrameDelta, 0)`
    (`8004CD6C.c:47-53`) -- iteriert `D_800A8F90` und ruft fuer jedes Objekt mit
    `entity->id > 0` `entity->update(6, entity, dt, 0)`. Hier ticken alle Mechs
    und Gegner!
  - `800271A4.c:181`: `gUpdateMapZoneTriggers(gFrameDelta)` (`80031DE8.c:87`)
  - `800271A4.c:182`: `gProcessDeferredEntityUpdates()` (`func_8004CE04`)
  - `800271A4.c:183`: `gRunEntityWorldTick(gFrameDelta)` (`8003F570.c`)
  - `800271A4.c:194`: `gRunRegionPass(0, gPlayerEntities)` (`func_80026E1C` ->
    `func_80066E98` -> `80062EB8.c:55-76`: sendet Message `3` und zeichnet
    Modelle mit `!(flags & 0x20) && (flags72 & 0x10)` via `func_800164F4`).
  - `800271A4.c:210`: `gUpdateRegisteredSpawners(gFrameDelta)` (`800616A0.c:49-78`)
    tickt die `Csac`-Spawner-Tabelle `D_8009F800` und ruft bei faelligem Spawn
    `func_8006125C` (`8006125C.c:41-78`) -> `func_8004D4F8` (`8004D4F8.c:48-61`).
  - `800271A4.c:212`: `gSweepRetiredEntities()` (`8004D598.c`) loescht tote
    Entities (`id == 0`, Msg 5, unlink + free) und prueft Game-Over/Victory via
    `80061020.c`.

### Richtigstellung der angefragten Loader/Registry-Dateien
Wichtig fuer die Architektur: Keines der vier im Auftrag genannten
Loader-/Registry-Module tickt Mechs oder spielt Voices ab:
- `80060E74.c:98-145` (`gRegisterUnitSpawner`, `Csac` `0x43736163` aus
  `80024EC8.c:197-199`): Haengt einen PA-/Wave-Spawner an `D_8009F800`, laedt
  `tSAC`, `aRSL`, `tACT` und loest `handler` ueber `gGetEntityHandlerForType`
  (`8002AE54.c`) auf. Ticken passiert spaeter in `800616A0.c` / `8006125C.c`.
- `8006A918.c:70-106` (`gRegistryLoad`, Record-Typ `0x63` aus
  `8004D6D4.c:118-120`): Laedt max. 6 Eintraege (`tACT`/`aRSL`) in
  `D_800A3340` (`gVmRecordTable`), nachgeschlagen via `8006AAEC.c:32-42` fuer
  Spieler-Waffen-Slots (`8006AB38.c:78`), **kein** Entity-Updater.
- `80060838.c:64-98` (`gLoadAttackDefinition`, Record-Typ `0x62` aus
  `8004D6D4.c:115-117`): Laedt 0x30-Byte-Waffendefinitionen in `D_800A2300`
  (`gEffectKeyTable`), skaliert Timing ueber `(x << 12) / 218` (`80060838.c:83-84`),
  **keine** Entity-Fabrik.
- `80084110.c:48-61` (`gLoadDeferredSceneAsset`, Record-Typ `0x60` aus
  `8004D6D4.c:109-111`): Spawnt statische `tACT`/`aRSL`-Szene-Props via
  `gSpawnSceneNode` (`80083A10.c:210-453`) in `D_8009F938` (`gCellBucketGrid`),
  **keine** Mech-Voices. Mech-SFX/Voices liegen in `80046C84.c`, `80047088.c`,
  `800588D4.c:264`, `80055988.c:182,219` und `80058D78.c:100`.

### AI-Zentrale & Gegner-Module (mit exakten C-Quellen)
- **Type-Dispatch**: `8002AE54.c:58-59` (`case 37` -> `gStrategicDroneEntityHandler`
  in `800357C0.c`).
- **Strategic Drone / Sky Captain (`type 37` = `0x25`)**:
  - `800357C0.c:117-201`: Handler fuer Msg 1 (`0x138` Alloc-Groesse), Msg 2
    (`gInitStrategicDrone` `800331CC.c`), Msg 6 (`gUpdateStrategicDrone`
    `8003532C.c`, sofort 0 bei `unkFA == 5`), Msg 4 (wenn `unkFA == 5`:
    `unk100 -= dt`, bei `<= 0` `gRearmDroneWander` `80035698.c` = Respawn),
    Msg 9/11/14 (bei Spieler-Treffer `gGameSession->unkE0 = 5`, danach
    `gHandleTumblingFlyer` `80032F3C.c`, das bei Tod `unkFA = 3` setzt).
  - `800331CC.c:126-247`: Ruft `gInitHoverEnemy` (`80032180.c`), allokiert
    `0xB4`-KI-Block an `obj->unk134`, initialisiert 2 Attack-Slots (`+0x50`, `+0x78`
    via `func_80060668`), skaliert `unkF4` und HP mit Schwierigkeit
    `D_8009FA94->unk4C` (`:134-197`) und registriert `D_8009F9EC = obj` (`:243`).
  - `8003532C.c:119-234`: Dekrementiert `unk134->unk18` (Reload `(9 - unk4C) *
    80 + 128 +/- rng`), ruft bei Ablaufen `gUpdateDroneStrategy` (`800336DC.c`,
    in .NET `DroneStrategy`), tickt Slots `+0x50`/`+0x78` via `func_80060238`
    (`80060238.c:89-287`, nutzt `func_800600E8` `800600E8.c:54-106`), und
    ruft danach `gUpdateHoverEnemy(msg, self, dt)` (`800324E0.c:167-386`).
  - `800324E0.c:167-374`: Verzweigt explizit bei `o->type == 0x25` (`:167`),
    ueberspringt `func_800320E4` Wander (`800320E4.c:47`), uebernimmt Zielrichtung/
    Anflug ueber `func_800336A8` / `func_80033608` (`80033608.c:60-107`, nutzt
    `800330E8.c` + `80035184.c` / `8003526C.c`), schaltet `SteerTail` auf
    Attack-Slot `+0xA0` (`:371`) und setzt bei `state == 3` Aufprall
    (`:250-276`) den Respawn-Timer `f100 = (10 - k) * 150 + 600` mit `FA = 5`.
  - `8005F6E8.c:130-463`: Feuert beim Ausloesen eines `func_80060238`-Slots
    ueber `func_800600E8` Projektile/Rays/Target-Lead (`gSolveLeadDirection`
    `8005E2A0.c:48-100`).

---

## Second-Mech-Port

### Befund im Retail: Kein KI-Typ-1, sondern Type `0x25` (Sky Captain) + Armee
1. **Kein AI-`type 1` (`gPlayerEntities[1]`)**: `gInitPlayerUnit`
   (`800579D4.c:175-190`) erhoeht fuer `type 1` `D_8009FA0C` (`gPlayerCount`)
   und fuellt `D_8009FA58[0..1]`. `gUpdatePlayerEntity` (`800588D4.c:256-292`)
   liest ausschliesslich Pad-Inputs (`gTranslatePadButtons`, `gWeaponSlotInput`,
   Bedingung `c1 < 2 && gScreenState == 4`) -- es existiert kein KI-Zweig fuer
   `type 1`. `80028164.c:125-129`, `80061020.c:134-144` und `8002D1A0.c:204-224`
   zeigen explizit die Entweder-Oder-Struktur: im 2P-Splitscreen gibt es
   `gPlayerEntities[1]` und `gSinglePlayerEntity == 0`; im 1P-PA steht in
   `D_8009F9EC` (`gSinglePlayerEntity`) der KI-gegnerische Mech **Sky Captain**
   (**Entity Type `37` / `0x25`**).
2. **Squad-Member / Armee-Typen in PA** (aus `8002AE54.c:56-103`,
   `800336DC.c:266-269,475-482`, `8002D1A0.c:138`, `80061020.c:145-196`):
   - **Type `5`**: Rail-/Pfad-Bodeneinheiten (`800382B4.c`, `800378BC.c`);
     koennen wie Type `0x25` `0x24`-Outposts erobern (`8002D1A0.c:138`); werden
     von `DroneStrategy` als `c5` (`n4C`/`n50`) bilanziert.
   - **Type `9`**: Hover-Drohnen (`80032F3C.c`, `80032180.c`, `800324E0.c`);
     Unterscheidung `sub >= 10` vs. `< 10` (`800336DC.c:475`); bereits
     headless als `HoverEnemy` portiert.
   - **Type `28` (`0x1C`)**: Schwere Multi-Turret-Einheiten (`800398EC.c`,
     bis zu 4 Geschuetze via `80038A00.c`); Schluesselziele fuer `DroneStrategy`
     (`c1C`, `n54`/`n58`).
   - **Type `36` (`0x24`), `8`, `6`, `30`, `33`**: `gHandleLinkedPropEntity`
     (`8002D664.c`) -- `0x24` = Outposts/Tuerme (`8002D1A0.c`), `8` = Base-
     Defender (`cntA` in `800336DC.c:482`).
   - **Type `20` (`0x14`)**: Einzel-Turret-Einheit (`800395A8.c`).
   - **Herkunft**: Spawns kommen aus `.MIS`-Records (`Cact` `8004D6D4.c` und
     `Csac` `80060E74.c` -> `D_8009F800` -> `800616A0.c`/`8006125C.c`).
     `DroneStrategy` schreibt Team-Credits/Batch-Counts in `D_8009FA94+0xA0..+0xDF`,
     welche die `RPNS`-Scripts (`s->arg1` in `8006125C.c:47`) von `Csac`-Spawnern
     abfragen.

### Kleinster Portierungsplan: Sky Captain (`type 0x25`) sichtbar & tickbar machen
Ziel: Den 2. Mech (`type 0x25`) ueber `HoverEnemy` + `DroneStrategy` in
`LevelScene.cs` spawnen, ticken und rendern -- ohne zuvor alle RPNS/Spawner zu
bauen. (Fuer echtes 2P-`MechBody` waere `MechWorld.Players = 2` noetig, das
ist aber ein separates Feature.)

1. **Neue Klassen in `src/FutureCop.Game/`**:
   - `StrategicDroneInit.cs` (Port von `func_80032180` + `func_800331CC`):
     Initialisiert `HoverEnemy` (`Type = 0x25`, `Team`, `Pos` aus `EntityRecord`
     in 16.16 fixed-point, `BasisRows` Identity, `F4`, `Hp`, `Strats` mit `PosB`
     = Spawn, `PosA` = Player-Spawn) sowie die beiden `AttackSlot`-Instanzen
     (`+0x50`, `+0x78`). C-Semantik beachten: `s16`-Winkel-Wraparound (Maske
     `0xFFF`, sign-extend `(s16)(a << 4) >> 4`), `s32`-Overflow bei `<< 12` /
     `>> 12`.
   - `StrategicDrone.cs` (Port von `func_8003532C`, `func_80035698`,
     `func_800330E8`, `func_80033608`, `func_800336A8`, `func_80035184`,
     `func_8003526C` und Msg-4/9/11/14-Teil aus `800357C0.c`):
     - Besitzt `HoverEnemy Body`, `DroneStrategy.Self Strategy`, zwei
       Attack-Slots (`SlotA`, `SlotB`) und AI-Timer `ThinkTimer` (`unk134->unk18`).
     - `Tick(int dt, ...)`: Wenn `Body.State == 5`, dekrementiere Respawn-Timer
       `Body.RespawnTimer` (`f100`), bei `<= 0` Re-Arm (`80035698`). Sonst:
       `ThinkTimer -= dt`; bei `<= 0` Timer neu laden (`(9 - diff) * 80 + 128 +
       (rng & 0xFF)`) und `DroneStrategy.Tick(Strategy, world)` ausfuehren,
       danach Attack-Slots ueber `EnemyAttackScheduler.Step` updaten und
       `HoverEnemyTick.Tick(Body, dt, hoverWorld)` aufrufen.
2. **World-Adapter in `src/FutureCop.View/LevelScene.cs` (oder Hilfsklasse)**:
   - `LevelHoverWorld : IHoverEnemyWorld`: Map-Hoehe ueber `MechGround.Probe` /
     Terrain-Zelle liefern; Player-Zielpos (`_walker.Pos[0..2]` und Heading),
     `ComputeAim` / `AttackStep` ueber `EnemyAttackScheduler.Step` verdrahten.
   - `LevelStrategyWorld : DroneStrategy.IWorld`: Expose `PlayerEntity` (aus
     `_walker`), `UnitList` (vorerst statische `MissionEntities` + Sky Captain),
     `Difficulty` (`S4C`, Default PA = z. B. 4), und `OnPursuit`/`OnAttack`
     auf `func_800336A8`/`func_80033608` abbilden.
3. **Anschluss in `src/FutureCop.View/LevelScene.cs`**:
   - Im Konstruktor (`LevelScene.cs`, nach `MissionEntities.Load(_file)`):
     Ersten `EntityRecord` mit `Type == 0x25` (zuerst in `Cact`, Fallback `Csac`)
     suchen und `StrategicDroneInit.Create(record, _walker)` erzeugen (falls in
     der Mission vorhanden; sonst optionaler Debug-Spawn am gegnerischen Start).
   - Neue Methode `public void AdvanceEntities(int dt)` (Analogie zu
     `AdvanceWalker`): wenn `dt <= 0` return; ruft `_strategicDrone?.Tick(dt,
     ...)` auf. In `MenuFlow.cs` parallel zu `Level.AdvanceWalker(steps, ...)`
     mit `dt = steps * RetailClock.LocomotionTicks` (oder direkt per Frame-Delta
     in Retail-1/60s-Einheiten, pruefen gegen `HoverEnemyTick`-Einheit!) aufrufen.
   - In `LevelScene.Render(LevelCamera camera)` vor `_drawQueue.Sort(DrawOrder)`:
     Wenn `_strategicDrone != null && _strategicDrone.Body.State != 5`:
     `Model` (`record.Model(0)` bzw. `Cobj`-Teile) mit `BasisRows` (row-major
     16.16) und Welt-Offset `(Body.Pos[i] - camera/MechWorld) >> 4` ueber
     `PlacedMatrix` in `_drawQueue` einreihen.
4. **TDD-Testliste (xUnit in `FutureCop.Assets.Tests`)**:
   - `StrategicDroneInitTests`:
     - `Init_ScalesHpAndSpeedByDifficulty_AndSetsSinglePlayerSlot`: prueft
       `Hp`, `F4` und `PosA`/`PosB` gegen `800331CC.c:134-243` fuer `unk4C = 0, 4, 9`.
     - `Init_NegativeSpawnCoords_PreservesSignAndFixedPoint`: prueft `s32`
       Vorzeichen bei `Pos` ohne Wraparound-Fehler.
   - `StrategicDroneTickTests`:
     - `ThinkTimerExpiry_CallsDroneStrategy_AndReloadsWithDifficulty`: `unk18`
       unterlaeuft `<= 0` -> Strategy-Call + Reload `(9 - unk4C) * 80 + 128 + rng`.
     - `State3GroundCrash_ArmsRespawnTimer_AndEntersState5`: Crash setzt
       `FA = 5` und `f100 = (10 - k) * 150 + 600` (`800324E0.c:250-276`).
     - `State5RespawnTimer_Zero_RearmsVia80035698`: `msg 4`-Logik (`800357C0.c`)
       reaktiviert Drone bei `f100 <= 0` und prueft `s16`/`s32` Unterlauf-Schutz.
     - `HeadingWraparound_4096Ring_MatchesPS1Arithmetic`: Winkel-Differenzen um
       `0`/`4095` bleiben im Bereich `[-2048, 2047]` (`s16` Shift-Maskierung).
   - `LevelSceneSecondMechTests`:
     - `AdvanceEntities_MovesStrategicDrone_WithNonZeroVelocity`: smoke test mit
       `.MIS`-Fixture oder synthetischem `EntityRecord(Type = 0x25)`.
     - `Render_WhenStateNot5_EnqueuesDroneDrawItems`: `Render` erzeugt DrawItems
       und blendet sie bei `State == 5` aus.

---

## Integration Bestand

So werden die bereits vorhandenen headless Module in `LevelScene` live geschaltet:

1. **Spawner (`Csac` -> `D_8009F800`)**:
   - `MissionEntities.Load(_file)` liefert bereits `EntityRecord` mit
     `Container == "Cact"` oder `"Csac"` (`MissionEntities.cs`).
   - Neue Klasse `LevelEntityManager` + `UnitSpawnerRegistry` (Port von
     `80060E74.c`, `800616A0.c`, `8006125C.c`, `8004D4F8.c`):
     - `Cact`-Eintraege werden beim Level-Start direkt ueber einen Type-Switch
       (`8002AE54.c`) instanziiert (`0x25`, `9`, `5`, `0x1C`, `0x24`, `8`).
     - `Csac`-Eintraege werden in die Spawner-Liste eingetragen und ueber
       `UpdateSpawners(dt)` getickt (inkl. `DroneStrategy`-Wirtschaftsfelder auf
       `SessionGlobals` fuer Respawn-Wellen).
2. **Tick-Loop (`800271A4.c:178-212` in `LevelScene`)**:
   - Pro Gameplay-Frame in `LevelScene.AdvanceEntities(int dt)`:
     1. `CullActiveEntities(_walker.Pos, threshold: 0x38000)` (`8004DF28.c`)
     2. `DispatchQueuedCommands()` (`8004CECC.c`)
     3. `BroadcastMessage6(dt)` (`8004CD6C.c`): fuer alle aktiven Entities mit
        `Id > 0`:
        - Type `0x25`: `StrategicDrone.Tick` (`DroneStrategy` + `EnemyAttackScheduler`
          + `HoverEnemyTick`)
        - Type `9`: `HoverEnemyTick.Tick` (`HoverEnemyTick.cs`)
        - Type `5` / `0x1C` / `0x24` / `8`: spaetere Entity-Ticks +
          Outpost-Capture (`8002D1A0.c`)
     4. `EnemyWeaponFire.Fire` verdrahten auf `LevelProjectiles` (Treffer gegen
        `_walker` und feindliche Entities mit Damage/Msg 9/11/14)
     5. `UpdateSpawners(dt)` (`800616A0.c`) und `SweepRetiredEntities()`
        (`8004D598.c` + `EnemyCorpseCleanup.cs`)
3. **Rendering-Hook (`80062EB8.c:55-76` in `LevelScene.Render`)**:
   - Innerhalb `LevelScene.Render(LevelCamera camera)` nach Terrain und Player,
     vor `_drawQueue.Sort(DrawOrder)`:
     - Iteriere aktive sichtbare Entities (`(flags & 0x20) == 0 && (flags72 & 0x10) != 0`).
     - Konvertiere Entity-`BasisRows` (16.16) + relative Weltposition
       `((pos - origin) >> 4) << 4` in `PlacedMatrix` (analog `_parts` beim
       Player-Mech) und fuege `DrawItem` fuer die `Cobj`-Meshes ein.

---

## Offene Fragen

1. **Konkrete `CONFT.MIS`-Record-Werte (nicht mit Shell geprueft)**:
   - Da `CONFT.MIS` binaer ist und keine Shell-Tools ausgefuehrt wurden, sind
     exakte Anzahlen/IDs/Script-Indizes der `Type 0x25`-, `5`-, `9`-, `0x1C`- und
     `0x24`-Records in `CONFT.MIS` (`Cact` vs. `Csac`) noch per Asset-Inspector
     oder xUnit-Test gegen `CONFT.MIS` zu verifizieren.
2. **RPNS-Script-VM & ICV-Felder (`80069D68.c`, `8006125C.c:47`)**:
   - Wie genau die 3 `RPNS`-Script-Offsets eines `Csac`/`Cact`-Records in
     `CONFT.MIS` (`Scripts` in `MissionEntities.cs`) mit `D_8009FA94+0xA0..+0xDF`
     interagieren und welche `ICV`/Opcode-Semantik `func_80069D68` im PA-Modus
     ausfuehrt, ist noch nicht im .NET-Port abgebildet.
3. **Unportierte Projektile & Lead-Solver fuer `EnemyWeaponFire.cs`**:
   - In `future-cop-lapd-ps1/src/game/` zu pruefen bzw. noch nicht nach .NET
     portiert: `8005E2A0.c` (`gSolveLeadDirection`, gelesen), sowie die
     konkreten Spawner/Projektile in `8005E47C.c`, `8005E9E4.c`, `8005EB74.c`,
     `8005ECB8.c`, `8005EE04.c`, `8005F018.c` und gefuehrte Projektile
     (`80035B7C.c`).
4. **Offene C-Funktionen & Globals (noch nicht vollstaendig aufgeloest)**:
   - `D_800A2300` (`gEffectKeyTable`, geladen in `80060838.c`): genaue Belegung
     der Waffen-Keys fuer Type `0x25` und Type `9` im PA-Level.
   - `D_8009FA94->unkE0` Event-Codes (gesetzt auf `5` bei `800357C0.c` Msg 9/11/14):
     Anbindung an PA-Announcer / HUD-Meldungen (`InGameHud`).
   - `func_80031DE8` (`80031DE8.c:87`, Trigger-Box-Test `func_80031C74`) und
     `func_80052098` (2P-Kollision): Abgrenzung zwischen Mission-Script-Triggern
     und PA-Triggerzonen.
   - `func_80060238` (`80060238.c`) Slot-Flags `+0x20`/`+0x23`: Rest-Unklarheiten
     fuer Multi-Turret-Einheiten (`type 0x1C`).
   - Zeiteinheit `gFrameDelta` (`dt`) zwischen `LevelScene` (`8` Walker-Subticks
     in `MenuFlow.cs`) und `HoverEnemyTick` / `EnemyAttackScheduler`: genaue
     Faktor-Kalibrierung gegen Retail-PAL/NTSC (`218` Skalierungsfaktor in
     `80060838.c:83-84`) im ersten Integrationstest pruefen.
