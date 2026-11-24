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
  `LevelScene`, treibt den Walker via `Steps` (8-Tick-Akkumulator, `:429`) und
  rendert `InGameHud`, setzt aber `Session = new() { Mode = 0 }` (`MenuFlow.cs:457`).
  Der Sky-Captain-Tick laeuft mit (`Level.AdvanceSkyCaptain(...)`, `MenuFlow.cs:441`).
- **Level-Szene & Player-Mech**: `src/FutureCop.View/LevelScene.cs` laedt
  Terrain, `MissionEntities`, `ObjectiveField`, `SkyCaptainSystem`, `PickupItems`
  (`type 0x10`) und `LevelWeapons` (`WeaponSlots`, `Shot`, `Beam`). `src/FutureCop.Game/Mech/`
  (`MechBody`, `MechWorld`, `MechPlayer`, `MechSpawn`, `MechInput`, `MechWalker`,
  `MechHover`, `MechAnimation`, `MechGround`, `MechCollision`, `MechMath`, plus
  `PlayerForm`) portiert 1P-Bewegung, Formwechsel, Wand-/Boden-Kollision und
  Teile-Assembly vollstaendig. `AttachWalker` erzwingt `body.Port == 0`
  (`LevelScene.cs:178`), `MechWalker.cs:460` und `MechHover.cs:24` werfen
  `NotSupportedException` bei `world.Players != 1`.
- **Outposts (`type 0x24`) & Sky Captain (`type 0x25`) auf `main` verdrahtet**:
  - `src/FutureCop.Game/CaptureObjective.cs` portiert den Proximity-Capture-Kern
    aus `8002D1A0.c` (`CaptureObjective.TryCapture`, `MatchScore`, `ObjectiveField`).
    `LevelScene.EnableObjectives()` (`LevelScene.cs:124-143`) laedt alle `0x24`-Records,
    `AdvanceWalker` ruft `_objectives?.Touch(...)` auf (`LevelScene.cs:217`), und
    `Render` zeichnet Outpost + Child-Modell (`LevelScene.cs:576-593`).
  - `src/FutureCop.Game/StrategicDrone.cs` (`StrategicDroneInit`, `StrategicDroneHandler`
    fuer Msg 1/2/4/6/9/11/14, `RearmDroneWander`), `StrategicDroneTick.cs` (`8003532C.c`),
    `DroneTargets.cs` und `DroneStrategicPosition.cs` sind portiert.
  - `src/FutureCop.View/LevelSceneSkyCaptain.cs` (`SkyCaptainSystem`) spawnt Sky Captain
    in `EnableObjectives()`, verdrahtet `DroneStrategy` + `Economy` (`DroneStrategy.Glob`,
    Score-Sync `Economy.F68 <-> Score[1]` in `LevelSceneSkyCaptain.cs:430-432`) und
    `LevelMap`-Raycasts (`BindMap`, `:180-198`); `LevelScene.AdvanceSkyCaptain` (`:151`)
    tickt ihn, `LevelScene.Render` (`:598-616`) zeichnet ihn.
  - **Ebenfalls auf `main` (PR #6)**: 1P-Zweig fuer `gSinglePlayerEntity` in
    `MatchScore.RecordCapture` (`CaptureObjective.cs:230-241`, nach `8002D1A0.c:216-222`),
    `src/FutureCop.Game/BuildOrderSpawner.cs` (opt-in Port-Erweiterung ueber
    `EnableBuildOrderSpawner` in `LevelSceneSkyCaptain.cs:66-85,479`: spawnt
    sichtbare, geradeaus fahrende/fliegende `BuiltUnit`-Instanzen aus `DroneStrategy.Glob`-
    Zaehlern ohne KI/Kampf), deren Rendering in `LevelScene.Render` (`LevelScene.cs:621-646`)
    sowie der Host-Harness `tests/host/capture-objective/harness.c`.
- **Wireframe-Ansicht**: `src/FutureCop.View/WireframeScene.cs` zeigt nur
  statische/animierte `Cobj`-Modelle und lokale Test-Projektile (`_shots`).
- **Weitere AI-/Gegner-Module**:
  - `src/FutureCop.Game/HoverEnemy.cs` + `HoverEnemyTick.cs` (`func_800324E0`,
    Default `Type = 0x25` in `HoverEnemy.cs:10`)
  - `src/FutureCop.Game/DroneStrategy.cs` (`func_800336DC`, ueber `SkyCaptainSystem` aktiv)
  - `src/FutureCop.Game/EnemyAttackScheduler.cs` (`func_80060238`,
    `func_800600E8`, `func_80060668`, `func_8004E5F0`, `func_8004E80C`,
    `func_8005FFB8`)
  - `src/FutureCop.Game/LeadDirectionSolver.cs` (`func_8005E2A0`, in `EnemyWeaponFire.cs:115` genutzt)
  - `src/FutureCop.Game/EnemyWeaponFire.cs` (`func_8005F6E8`; nur `8005E47C`
    ueber `env.SpawnDirectShot` und `8005F018` ueber `env.OnFireRayAttack`
    sind Delegates, `8005E9E4`/`8005EB74`/`8005ECB8`/`8005EE04` werfen
    `NotImplementedException`)
  - `src/FutureCop.Game/EnemyCorpseCleanup.cs` + `EnemyMath.cs`

### Was fuer "PA 1P komplett spielbar" fehlt
1. **Kartenplatzierte Objekte & Occupancy-Registry**: Es fehlt die komplette
   Spawn-/Belegungs-Pipeline (`D_800A0C70`, sofortige vs. verzoegerte Registrierung
   `8003FA34.c` -> `8003BC0C.c` / `8003B838.c`, Replay beim Ladeende `8003FAD4.c`)
   sowie eine allgemeine Entity-Registry analog `D_8009FA40` / `D_800A8F90` mit
   Message-6-Broadcast pro Frame (`LevelScene` treibt bisher nur Spieler,
   Sky Captain und opt-in `BuiltUnit`-Instanzen aus `BuildOrderSpawner`).
2. **Spawner (`Csac`) & Factory-Dispatch**: `MissionEntities.cs` parst `Cact` und
   `Csac` inkl. 3 `RPNS`-Script-Offsets (`Scripts`), aber es gibt keinen
   Retail-Laufzeit-Spawner fuer `Csac` (`D_8009F800`, `80060E74.c` -> `800616A0.c` ->
   `8006125C.c`) und keinen allgemeinen Type-Handler-Switch (`8002AE54.c`).
3. **Second Mech (Sky Captain `type 0x25`) Restintegration**: Kernlogik und
   Szenen-Rendering sind auf `main`, und `MenuFlow.cs:441` tickt ihn per
   `Level.AdvanceSkyCaptain(...)`; Msg-4-Respawn-Loop und Treffer-/Crash-Zustellung
   (`msg 9/11/14`) aus `StrategicDroneHandler` sind noch nicht an `LevelScene`
   angeschlossen.
4. **PA-Armee, Outpost-Restlogik & Basen-Besitz**: Types `5` (Rail-Fahrzeug),
   `9` (Hover-Drohne), `28`/`0x1C` (Multi-Turret-Panzer) und `8` (Base-Defender)
   sind im Retail-Pfad weder gespawnt noch gerendert. Bei `CaptureObjective.cs`
   (`type 0x24`) ist der 1P-Zweig fuer `gSinglePlayerEntity` inzwischen auf `main`;
   es fehlen noch `0x12`-Carrier-Aufloesung (`8002D1A0.c:141-144`),
   Child-Part-Synchronisation (`obj->unk104`, `8002D1A0.c:201,209,211`),
   Post-Capture-Script (`8002D1A0.c:238-239`) und Message-5-Scoring
   (`8002D664.c:179-225`).
5. **Bewegliche Karten-Elemente (Fahrstuehle & Turret-Carrier)**: Weder die
   Type-10-Lift-Kette (`80041B6C.c` / `80041488.c` / `80041880.c` / `80041728.c`)
   noch die Type-20-Mounted-Turret-Carrier-Kette (`80039504.c` / `800395A8.c` /
   `80039418.c` / `80039298.c`) ist im .NET-Port vorhanden.
6. **Treffer-, Schadens- & Todeslogik**: `LevelWeapons` prueft keine Kollision
   gegen gegnerische Entities; in `EnemyWeaponFire.cs` (mit portiertem
   `LeadDirectionSolver` `8005E2A0.c`) sind nur `8005E47C` (`env.SpawnDirectShot`,
   `:234`) und `8005F018` (`env.OnFireRayAttack`, `:205`) als unverdrahtete
   Delegates vorhanden, waehrend `8005E9E4`, `8005EB74`, `8005ECB8` und `8005EE04`
   `NotImplementedException` werfen (`:284-291`; die Spielerseite von `8005E47C`
   ist bereits teilweise in `src/FutureCop.View/Shot.cs` / `LevelWeapons.cs`
   portiert). Player-Death/Respawn (`80058D78`, `80058714`) fehlt.
7. **PA Spielregeln, Wirtschaft & Sieg/Niederlage**: `DroneStrategy` schreibt
   Felder auf `D_8009FA94` (`+0xA0..+0xDF`), und Spieler haben in PA eine
   Punkte-/Kauf-Oekonomie (L1-Bauaktion an Basiskonsolen), aber der zugehoerige
   Retail-Kauf-/Input-Pfad sowie die RPNS-Script-VM (`80069D68.c`) sind weder
   vollstaendig getraced noch portiert. `LevelScene.ScoreSession` ist nicht in
   `MenuFlow` / Endscreen (`80028164.c`) angeschlossen.
8. **Audio / Voice**: `WeaponSoundEvent` in `LevelWeapons` ist stumm; Retail-SFX/
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

### Kartenplatzierte Objekte & Occupancy-Registrierung
- **Confirmed**:
  - `gInitMissionSpawnState` (`8003F79C.c:44-69`) loescht die 256-Slot-Tabelle
    `D_800A0C70` (`gSlotSpawnDescs`, `:49-51`), beide 256-Wort-Baenke `D_800A7040`
    (`gSpawnRecordMap`, `:53-58`), `D_800A1070[0..6]` (`:66-68`), `D_8009F310`/
    `D_8009F314` (`:60,64`) und allokiert den 256x8-Byte-Deferral-Pool `D_8009F70C`
    (`gDeferredRegPool = func_800194DC(0x100, 8, 1)`, `:63`).
  - `gInitEntityBase` (`8002A5F4.c:91-168`) uebernimmt Position (`:159-160`) und
    Slot-Byte `unk20` (`:127`) aus dem Source-Record, setzt `unk90 = 0x7F00` (`:166`)
    und ruft als letzten Schritt `gQueueOrApplyMapOccupancy(self)` (`:167`) auf.
  - `gQueueOrApplyMapOccupancy` (`8003FA34.c:52-72`) entscheidet zwischen
    **sofort** (`gDeferredRegPool == 0 || gScreenState[0] == 4 || gScreenState[0] == 0xD`,
    `:56` -> `gSpawnEntityPrimaryType`, `:57`) und **verzoegert** (sonst, falls
    `gGetSpawnDescColumn(object) != 0`, `:60`: 8-Byte-Knoten aus `D_8009F70C` via
    `func_800195A4` holen und LIFO an `D_8009F708` voranhaengen, `:61-69`; bei
    erschoepftem Pool stiller Drop).
  - `gGetSpawnDescColumn` (`8003B838.c:52-67`) prueft drei Guards (`tableIndex == 0`,
    `flags & 0x40000`, `D_800A0C70[tableIndex] == 0`) und liefert sonst `entry[0]`.
  - `gSpawnEntityPrimaryType` (`8003BC0C.c:50-60`) ist der eigentliche Schreiber:
    bei `tableIndex != 0` und `kind = gGetSpawnDescColumn(object) != 0` ruft es
    `gSpawnTypeAtOwner(object, kind, 0, gGetEntitySpawnBank(object), 0)` (`func_800402DC`, `:57`).
  - `gFinishLevelLoad` (`8003FAD4.c:76-104`) markiert am Ladeende beide Ketten
    (`gMarkSpawnRecordChains(0)/(1)`, `:81-82`), spielt `D_8009F708` per
    `gSpawnEntityPrimaryType` ab (`:84-87`), gibt `D_8009F70C` frei (`:89`) und
    nullt Pool und Liste (`:97-98`).
- **Likely**: Das Quartil-Flag `0x4000` (`func_8003BA50` -> `func_8004CE04` ->
  `func_8003BAE4`) triggert stufenweise Slot-Spawns ueber 0..3-Buckets.
- **Unproven**: Bedeutung der `gScreenState`-Werte (`4` ist **kein** Ladezustand --
  Gegenbeispiele: Spieler-Pad-Abfrage laeuft nur bei `gScreenState == 4` in
  `800588D4.c:257`, der 2P-Frame-Loop in `80027510.c:187`, der Frame-Close-HUD-Hook
  in `800486FC.c:44`, und die UI-Schleife selbst ist `while (gScreenState == 4)` in
  `80027EE4.c:54`; Arbeitshypothese: `4` = laufender Screen-Loop, `5` = Laden ueber
  die Retail-Stelle `0x8007E5A8`/`0x8007E5B4` (`addiu $v0,0x5` + `sw` auf `D_8009FA00`)
  bzw. `8007E3EC.c:90`, was den verzoegerten Pfad beim Laden
  erklaeren wuerde; exakte Rolle von `gScreenState == 0xD` offen); ob `func_8003BDA0`
  der einzige Schreiber von `D_800A0C70` ist (`func_8003BC7C` hat einen weiteren
  nicht geprueften Aufrufer `func_8003BFD0`); Zell-Flag-Bits in `func_800402DC`.

### Basen-Besitz (Type 0x24): Proximity-Capture & Message-5-Scoring
- **Confirmed**:
  - Outposts sind Entities vom Typ `0x24` unter `gHandleLinkedPropEntity`
    (`8002D664.c:133-230`); Message 10 ruft fuer `unk16 == 0x24`
    `gTryCaptureObjective(obj, arg2)` auf (`8002D664.c:141-143`).
  - `gTryCaptureObjective` (`8002D1A0.c:126-240`): One-Shot-Latch `unkFE != 0` (`:137`),
    Team-Gate `other->unk18 == unkF6 || unkF7` (`:138`), Klassen-Gate `1` (`other->unk14`
    entspricht einer der beiden `gPlayerEntities` und `unkAC->unk40 == 0`, `:145-148`),
    `0x12` (ueber Carrier `other->unkB4`), `5` oder `0x25` (`:140-152`). Oktogonaldistanz
    gegen `(unkFC << 4) + other->coord.unk40 + 0x4000` (`:154-170`), wobei `0x25` die
    Hoehenachse ueberspringt (`:172-175`).
  - Eigentumsuebergang (`8002D1A0.c:197-212`): `mode = (id == unkF6) ? 1 : 2`;
    setzt `unkFE = mode`, uebertraegt Team auf **Objective UND Child-Part**
    (`self->unk18 = id` und `self->unk104->unk18 = id`, `:200-201`), setzt
    `unkA6 = unkDE = 4`, Gegen-Team `unkA8`/`unkDC`, Mark `unk23` (`unkF8`/`unkF9`),
    addiert **mode-skaliert** `unk92 += unkFA * mode` und `unk93 += unkFB * mode`
    auf Objective und Child `unk104` (`:208-211`) und loescht Flag-Bit 0 (`:212`).
  - Zaehler-Fortschreibung (`8002D1A0.c:216-236`): 1P-Zweig (`gPlayerCount == 1`
    und `gSinglePlayerEntity->unk18 == id`) erhoeht `gGameSession->unk64[1]`,
    `unk7C[1]`, `unk134->unk3A`, setzt `unk134->unk18 = 0` und bei ID-Match
    `unkE0 = 6`; Splitscreen-/Player-Zweig erhoeht `unk64[unkC1]`, `unk7C[unkC1]`
    und `unkAC->unk12`. Danach optional `gInterpretEntityScript(unk9C, 0, other->unk14 & 0x7FFF)`
    bei `unk24 & 0x100000` (`:238-239`).
  - Message-5-Scoring (`8002D664.c:179-225`): Bei `unk16 == 0x24 && unkFE != 0 && !(flags & 0x4000000)`
    wird das Gegen-Team (`unkFE == 1 ? unkF7 : unkF6`, `:184`) ermittelt. Im 1P-Zweig
    (`:185-207`) steigen `gGameSession->unk68`, `unk80` und `gPlayerEntities[0]->unkAC->unk10`,
    `p->unk134->unk18` wird genullt und bei ID-Match ueber `gFindUnitById(unk8C & 0x3FFFFFFF)`
    `unkE0 = 7` gesetzt. Im Player-/Splitscreen-Zweig (`:208-223`) steigen
    `+0x64[unkC1]`/`+0x7C[unkC1]` sowie im 1P-Fall `st->unk134->unk38` + `unkE0 = 8`
    bzw. im 2P-Fall `players[i ^ 1]->unkAC->unk10`.
  - Adressidentitaet von `+0x68`/`+0x80` und Leser: Aus `struct Menu { u8 pad00[0x64];
    s32 unk64[2]; u8 pad6C[0x10]; s32 unk7C[2]; }` (`8002D1A0.c:74-78`) gegen
    `struct Unk2D664Menu` mit `unk68` an `+0x68` und `unk80` an `+0x80`
    (`8002D664.c:55-59`) folgt `+0x68 == unk64[1]` und `+0x80 == unk7C[1]`. Leser:
    das HUD liest `state->score[game->player->valuec1]` ab `+0x64` (`80047F30.c:214`),
    und die RPNS-VM liest/schreibt `gGameSession`-Woerter frei (`80069D68.c:220,232,256,280`).
- **Likely**: Die Verdopplung der `unkFA`/`unkFB`-Inkremente bei `mode == 2`
  (Steal vs. Claim) ist beabsichtigt.
- **Unproven**: Welche Team-ID in `unkF6` vs. `unkF7` initialer Besitzer bzw.
  Herausforderer ist; welcher Spieler bzw. welches Team den Wert aus `+0x68`
  (`unk64[1]`) im 1P-HUD angezeigt bekommt.

### Einheiten-Produktion (Csac-Spawner) & offener Kauf-Pfad
- **Confirmed**:
  - `gRegisterUnitSpawner` (`80060E74.c:112-159`, aus `Csac` in `80024EC8.c`)
    haengt den Descriptor an `D_8009F800` (`:122`), loest `'CASt'`/`'LSRa'`/`'TCAt'`
    auf (`:124,141,142`), skaliert den Respawn-Delay `* 5` (`:129`), initialisiert
    den ersten Countdown (`:130-136`), loest `LSRa`-Eintraege via `func_80019BF0`
    auf (`:150-155`) und bindet den Typ-Handler aus `*(u8 *)(data->unk28 + 4)`
    ueber `gGetEntityHandlerForType` (`:157`).
  - `gUpdateRegisteredSpawners` (`800616A0.c:49-78`) entleert pro Frame die
    Burst-Bits `8..11` (`arg = 1`) und `12..15` (`arg = 0`) von `sub->unk10`
    ueber `gTrySpawnRegisteredUnit` (`:62-69`) und dekrementiert bei
    `unk16 < unk0E && unk12 != 0 && !(flags & 1)` den Timer `unk14` (`:70-76`).
  - `gTrySpawnRegisteredUnit` (`8006125C.c:41-78`) wertet `gInterpretEntityScript(s->arg1, 0, -1, 0)`
    aus (`:47`), aktualisiert Zaehler und (bei `gGameSession->active == 1`, `:50-73`;
    dass `1` fuer PA steht, ist plausibel, aber **Unproven**) Statistiken, spawnt ueber
    `gSpawnTargetableEntityId(s->def)` (`:75`) und fuehrt Post-Spawn-Script `s->arg2` aus
    (`gInterpretEntityScript(s->arg2, 0, -1, 0)`, `:76`).
  - Keine der drei auditierten Spawner-Units (`80060E74.c`, `800616A0.c`,
    `8006125C.c`) enthaelt eine Preis-, Kosten- oder Punkte-Pruefung.
- **Extern belegt (Gameplay/Handbuch), im C noch nicht lokalisiert**:
  - In Precinct Assault existiert eine spielerseitige Punkte-Oekonomie (L1-Bauaktion
    an Basiskonsolen; bekannte Kosten: Panzer/Flugzeug 1 Punkt, 2 an einem Outpost,
    Outpost-Claim 30 Punkte, Dreadnought/Flying Fortress 50 Punkte). Das Fehlen von
    Preis-Checks in den drei Spawner-Units (und das Fehlen von `buy`/`cost`-Strings
    in `SLUS_007.39`) belegt **nicht**, dass es keine Kauf-Mechanik gibt.
  - Der eigentliche Kauf-Ausloeser im Retail-C wurde noch **nicht getraced**
    (**Unproven** im Code). **Likely**: Das Preis-Gate bzw. der Punkte-Abzug liegt
    im `arg1`-Gate-Script (`8006125C.c:47`) ueber die RPNS-VM-Opcodes `case 25:
    gGameSession[a]--` (`80069D68.c:255-257`) und `case 52: gGameSession[a] -= b`
    (`80069D68.c:279-281`) auf `gGameSession[25/26]` (`+0x64`/`+0x68`), ggf. gekoppelt
    an einen L1-Input-Handler; zu beweisen durch Dekodierung der `Csac`-Scripts aus
    `CONFT.MIS`. Das ist der naechste offene C-Trace.

### Bewegliche Elemente: Fahrstuehle (Type 10) & Turret-Carrier (Type 20)
- **Confirmed**:
  - Beide Familien haengen an der Retail-Sprungtabelle in `gGetEntityHandlerForType`
    (`8002AE54.c:70-71` Type 10 -> `func_80041B6C`, Retail `0x8002AED0`; `:82-83`
    Type 20 -> `func_800395A8`, Retail `0x8002AF18`).
  - **Type-10-Lift-Kette**:
    - `gHandleElevatorEntityMessages` (`80041B6C.c:40-63`): Msg 1 = Blockgroesse
      `0xD0`, Msg 2 -> `gInitElevator`, Msg 5 -> `gRemoveGroundKeyEntry` + Base,
      Msg 6 -> `gStepElevator`, Msg 11 -> Hit-Zaehler `+0xC4` (Cap 20) + Base,
      sonst `gEntityBaseHandler`.
    - `gInitElevator` (`80041488.c:80-128`): liest Script-Record (`values[0..2] = pos << 4`,
      `speeds[0..2] = (speed << 8) / 218`, `other[0..1] = (other << 4) / 300`, `:90-97`),
      setzt Start-Stop/Richtung/Dwell-Timer (`flags & 5` -> `current = 1`, `:117-118`),
      nullt `+0xC4` (`:119`), registriert Ground-Objekt (`func_8006314C`, `:120`),
      addiert `gGetGroundHeight` auf alle Stops (`:122-123`) und transformiert
      den Referenz-Vertex (`:125-127`).
    - `gStepElevator` (`80041880.c:160-263`): feste Gate-Reihenfolge: (1) Command-Queue
      `unk0C != 0` -> `gRunEntityCommands` (`:167-169`), (2) Frozen-Bit `unkC6 & 2`
      (`:170-172`), (3) Hold-Countdown `unkC4 > 0` (selbes `s16`-Feld `+0xC4` wie
      Msg-11-Hit-Zaehler, `:173-176`), (4) Spieler-auf-Lift-Flag ueber
      `D_8009FA58[0..1]->unkC4 == self` bei `unkAC->unk40 == 0` (`:178-192`),
      (5) Trigger-/Dwell-Gate (`:194-210`), (6) Ping-Pong-Bewegung auf `+0x54` mit
      Umkehr an beiden Enden (`:212-236`), (7) Stop-Snap, Dwell-Reload und
      Stop-/Start-Sound (`func_800457B8`/`func_80047284`, `:239-257`),
      (8) Matrix-/Vertex-Refresh (`:259-260`).
    - `gRunEntityCommands` (`80041728.c:69-145`): 4 Slots ab `+0x0C`; `0x53`
      setzt/loescht Frozen-Bit 1 in `+0xC6` (`:88-94`); `0x52` armt Timer (`arg == 0`),
      waehlt Ziel-Stop/Richtung (`1..10`) oder springt direkt auf Stop `arg - 11`
      inkl. Vertex-Refresh (`>= 11`, `:95-136`); unbekannte Opcodes bleiben stehen.
  - **Type-20-Mounted-Turret-Carrier-Kette** (bewegliche Class-5-Plattform mit Kind `0x15`):
    - `gSetupMountedTurretCarrier` (`80039504.c:68-80`): ruft Class-5-Init
      (`func_800382B4`), spawnt Kind `0x15` via `gSpawnMountedPartChild`
      (`80039418.c:91-121`, Handler `func_80039298`, Position aus `gTransformPartVertex`,
      `flags & 0x7FFFDFFF`) in `+0x158`, setzt Child-Flag `0x10` und registriert
      dessen Occupancy ueber `gQueueOrApplyMapOccupancy` (`:79`).
    - `gHandleMountedTurretCarrierMessages` (`800395A8.c:52-115`): Blockgroesse
      `0x15C`, delegiert Bewegung an Class 5, synchronisiert bei Msg 6 `unk1C` +
      `gUpdateMountedTurret` (`80038A00.c`, `:75-81`), propagiert bei Msg 9/11/14
      Flag `0x02000000` + Health `unk1C` inkl. `gFlagQuartileChange` (`:82-100`),
      setzt bei Msg 5 `child->unk1C = -1` mit Fall-through (`:101-113`).
    - `gHandleMountedPartMsg` (`80039298.c:66-139`): Blockgroesse `0xF0`, Owner-/Parent-Zeiger
      an `+0xEC` (Dekomp-Feldname `child`, befuellt aus `args.owner = object` in `80039418.c:111`),
      Msg 10 Identitaetsabfrage (`obj->child != data`), Msg 5 Slot-Clear, Msg 11/14 Weiterleitung
      an Typ `0x14` (`gHandleMountedTurretCarrierMessages`) oder `0x1C` (`gHandleType28Entity`,
      `80039298.c:120-134`).
- **Likely**: `218`/`300` in `gInitElevator` sind Zeit-/Skalierungsfaktoren; das
  Sound-Paar `func_800457B8`/`func_80047284` schaltet den Lift-Motor; Bit `0x02000000`
  markiert Beschaedigung.
- **Unproven**: Ob PA-Missionsdaten (`CONFT.MIS` etc.) Type 10 oder Type 20
  tatsaechlich platzieren (bisher keine `.MIS`-Inspektion fuer diese Typen);
  Aufrufer/Gameplay-Effekt von Msg 11 auf `+0xC4`; Grund fuer `flags & 0x7FFFDFFF`
  im Kind-Spawn; interne Ziel-/Anim-Logik von `gUpdateMountedTurret` (`80038A00.c`).

---

## Second-Mech-Port

### Befund im Retail: Kein KI-Typ-1, sondern Type `0x25` (Sky Captain) + Armee
1. **Kein AI-`type 1` (`gPlayerEntities[1]`)**: `gInitPlayerUnit`
   (`800579D4.c:175-190`) erhoeht fuer `type 1` `D_8009FA0C` (`gPlayerCount`)
   und fuellt `D_8009FA58[0..1]`. `gUpdatePlayerEntity` (`800588D4.c:256-292`)
   liest ausschliesslich Pad-Inputs (`gTranslatePadButtons`, `gWeaponSlotInput`,
   Bedingung `c1 < 2 && gScreenState == 4`) -- es existiert kein KI-Zweig fuer
   `type 1`. `80028164.c:125-129`, `80061020.c:134-144` und `8002D1A0.c:216-236`
   zeigen explizit die Entweder-Oder-Struktur: im 2P-Splitscreen gibt es
   `gPlayerEntities[1]` und `gSinglePlayerEntity == 0`; im 1P-PA steht in
   `D_8009F9EC` (`gSinglePlayerEntity`) der KI-gegnerische Mech **Sky Captain**
   (**Entity Type `37` / `0x25`**).
2. **Squad-Member / Armee-Typen in PA** (aus `8002AE54.c:56-103`,
   `800336DC.c:266-269,475-482`, `8002D1A0.c:150`, `80061020.c:145-196`):
   - **Type `5`**: Rail-/Pfad-Bodeneinheiten (`800382B4.c`, `800378BC.c`);
     koennen wie Type `0x25` `0x24`-Outposts erobern (`8002D1A0.c:150`); werden
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
   - **Type `20` (`0x14`)**: Beweglicher Class-5-Turret-Carrier mit Kind-Entity
     `0x15` (`80039504.c`, `800395A8.c`, `80039418.c`, `80039298.c`).
   - **Herkunft**: Spawns kommen aus `.MIS`-Records (`Cact` `8004D6D4.c` und
     `Csac` `80060E74.c` -> `D_8009F800` -> `800616A0.c`/`8006125C.c`, **Confirmed**).
     `DroneStrategy` schreibt Felder in `D_8009FA94+0xA0..+0xDF` (**Confirmed**);
     wie genau die `RPNS`-Scripts (`s->arg1` in `8006125C.c:47`) und der
     spielerseitige L1-Kauf-Pfad diese Felder lesen bzw. dekrementieren, ist im
     Retail-C noch **Unproven** (offener Trace).

### Kleinster Portierungsplan: Sky Captain (`type 0x25`) sichtbar & tickbar machen
*(Status-Update `main`: Kernklassen `StrategicDrone.cs`, `StrategicDroneTick.cs`
und Szenen-Treiber/Rendering `LevelSceneSkyCaptain.cs` sind bereits auf `main`
gelandet, und `MenuFlow.cs:441` tickt ihn per `Level.AdvanceSkyCaptain(...)`;
offen bleiben Msg-4-Respawn und Msg-9/11/14-Trefferverdrahtung in `LevelScene`.)*
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

1. **Konkrete `CONFT.MIS`-Record-Werte & Mover-Platzierung (Unproven)**:
   - Exakte Anzahlen/IDs/Script-Indizes der `Type 0x25`-, `5`-, `9`-, `0x1C`-,
     `0x24`- sowie insbesondere `Type 10`- (Fahrstuhl) und `Type 20`-Records
     (Turret-Carrier) in `CONFT.MIS` und den uebrigen PA-Karten (`Cact` vs.
     `Csac`) sind noch per Asset-Inspector oder xUnit-Test zu verifizieren.
2. **Spieler-Kaufaktion (L1) & RPNS-Script-VM (`80069D68.c`, `8006125C.c:47`)**:
   - Naechster offener C-Trace: Wo im Retail-Code der spielerseitige L1-Kauf
     an Basiskonsolen ausgewertet wird und Punkte auf `D_8009FA94` (`+0x64` /
     `+0xA0..+0xDF`) dekrementiert -- die bisher auditierten Spawner-Units
     (`80060E74.c`, `800616A0.c`, `8006125C.c`) enthalten keinen Preis-Check.
   - **Likely**: Das Preis-Gate bzw. der Punkte-Abzug liegt im `arg1`-Gate-Script
     (`8006125C.c:47`) ueber die RPNS-VM-Opcodes `case 25: gGameSession[a]--`
     (`80069D68.c:255-257`) und `case 52: gGameSession[a] -= b` (`80069D68.c:279-281`)
     auf `gGameSession[25/26]` (`+0x64`/`+0x68`); zu beweisen durch Dekodierung
     der `Csac`-Scripts aus `CONFT.MIS`.
3. **Unportierte Projektile fuer `EnemyWeaponFire.cs`**:
   - `LeadDirectionSolver.cs` (`8005E2A0.c`) ist bereits auf `main` portiert
     und in `EnemyWeaponFire.cs:115` angeschlossen. In `EnemyWeaponFire.cs` sind
     nur `8005E47C` (`env.SpawnDirectShot`, `:234`) und `8005F018`
     (`env.OnFireRayAttack`, `:205`) als (noch unverdrahtete) Delegates
     vorhanden, waehrend `8005E9E4`, `8005EB74`, `8005ECB8` und `8005EE04`
     `NotImplementedException` werfen (`:284-291`). Die Spielerseite von
     `8005E47C` ist bereits teilweise in `src/FutureCop.View/Shot.cs` und
     `src/FutureCop.View/LevelWeapons.cs` portiert; gefuehrte Projektile
     (`80035B7C.c`) fehlen weiterhin.
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
