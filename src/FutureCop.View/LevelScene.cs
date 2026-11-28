using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;

namespace FutureCop.View;

/// <summary>
/// Camera of the level view: orbit around the mech pivot layered with the retail chase-camera
/// preset state (<c>D_8009AEC4[0..6]</c> / <c>D_8009AD08</c>, <c>src/game/800569EC.c:232-246,305-513</c>).
/// </summary>
/// <param name="Yaw">Base orbit yaw in degrees about the world y axis.</param>
/// <param name="Elevation">Base elevation in degrees above the horizon.</param>
/// <param name="Distance">Base eye distance from the pivot in model units (world units / 16).</param>
/// <param name="PresetState">Retail chase-camera preset state <c>0..6</c> (<c>info-&gt;state</c>).</param>
public sealed record LevelCamera(double Yaw = 30.0, double Elevation = 35.0, int Distance = 0x1C00, int PresetState = 0)
{
    /// <summary>
    /// Applies the retail preset offset for <see cref="PresetState"/> on top of this orbit camera.
    /// Preset 0 returns this camera unchanged.
    /// </summary>
    public LevelCamera ResolvePreset()
    {
        int state = ((PresetState % ChaseCameraState.PresetCount) + ChaseCameraState.PresetCount) % ChaseCameraState.PresetCount;
        if (state == 0) return this;
        var preset = ChaseCameraState.GetPreset(state);
        double yaw = ((Yaw + preset.YawOffsetDegrees) % 360.0 + 360.0) % 360.0;
        double elevation = Math.Clamp(Elevation + preset.ElevationOffsetDegrees, 5.0, 85.0);
        int distance = Math.Max(0x400, Distance + preset.DistanceOffsetModelUnits);
        return new LevelCamera(yaw, elevation, distance, state);
    }
}

/// <summary>
/// First runnable start of a Precinct Assault map: the retail terrain of the mission (GRDB grid +
/// Ctil sections, drawn through the ported layer-1 path: cell window func_80064460, grid projection
/// func_800646BC, cell walk func_80065134 and packet emit func_800648E8) with the player walker
/// (PlayerForm) at the clip-rect centre in the static viewer, or at the mission spawn for WALK.
/// The software rasterizer only turns the emitted packets into pixels; it is a viewer, not a GPU port.
/// </summary>
public sealed class LevelScene
{
    public const int Width = 320;
    public const int Height = 240;
    /// <summary>GTE H. func_80063FF8 encodes the 1-player view cone |x| &lt;= 0.3125 z, |y| &lt;= 0.234 z,
    /// i.e. 160/H and 120/H with H = 0x200 on the 320 x 240 screen.</summary>
    public const int Focal = 0x200;
    private const int Ofx = 160 << 16, Ofy = 120 << 16;

    private readonly MisFile _file;
    private readonly TextureBank _textures;
    private IReadOnlyList<PlayerPart> _parts;
    private readonly Dictionary<int, Dictionary<int, TextureRecord>> _texByModel = new();
    private readonly Dictionary<int, Model> _modelsById = new();
    private readonly Dictionary<int, short[]> _widthsByModelId = new();
    // D_8009FA58: the player entities the trigger tests read, keyed by actor record id (12 / 16).
    private readonly Dictionary<int, PlayerTarget> _playerTargets = new();
    private MechBody? _secondPlayer;
    private int _secondPlayerId;
    // Per-frame scratch storage. The old draw queue allocated closures and used a LINQ sort; the
    // terrain vertex buffer and packet lists were also recreated. Reuse them between frames.
    private readonly List<DrawItem> _drawQueue = new();
    private readonly byte[] _vertexBuffer = new byte[TerrainRender.VertexBufferBytes];
    private readonly List<uint[]> _packets = new();
    private ScreenVertex[] _projectedScratch = Array.Empty<ScreenVertex>();
    private readonly List<TexturedFace> _facesScratch = new();

    /// <summary>One queued primitive of the painter's-order pass: a terrain packet or a model face.</summary>
    private struct DrawItem
    {
        public int Key;
        public int Order;
        public uint[]? Packet;
        public TexturedFace Face;
        public Model? Model;
    }
    private readonly uint[] _texpages = TerrainRender.DefaultTexpages();
    private readonly int _mechCx, _mechCy, _mechCz;

    public LevelMap Map { get; }
    public string MissionPath { get; }
    /// <summary>World position in world units, y up.</summary>
    public (int X, int Y, int Z) MechWorld { get; private set; }
    private MechBody? _walker;
    private FutureCop.Game.Mech.MechWorld? _walkerWorld;
    private bool _animateWalker;

    /// <summary>Use the mission's player record and body model for the interactive WALK viewer.</summary>
    public void EnableMechMovement()
    {
        // _parts[1] is the companion instance (LSRa slot 2) whose track func_80051AF8:138 starts.
        AttachWalker(MechSpawn.CreateWalker(Actor, _parts[0].Model, Map, _parts[1].Model));
        _animateWalker = true;
        UpdateWalkerPose();
        var (x, y, z) = MechWorld;
        LevelGround.Height(Map, x, y, z, out int attribute);
        GroundAttribute = attribute;
    }

    private LevelWeapons? _weapons;
    private WeaponPickups? _pickupField;
    private PickupItems? _pickups;
    private readonly Dictionary<WeaponPickup, LiveEntity> _pickupEntities = new();
    private bool _pickupsBoundToCsac;

    /// <summary>
    /// The weapon pickups lying on this mission's map, or null until firing is enabled. The field is
    /// built from the type 0x10 records of the archive (func_8002FA20), so the items sit where the
    /// retail level puts them.
    /// </summary>
    public PickupItems? Pickups => _pickups;

    /// <summary>
    /// The fire path of the running level, or null when it was not enabled. Created lazily from the
    /// mission's definitions so a static preview does not pay for the scan.
    /// </summary>
    public LevelWeapons? Weapons
    {
        get
        {
            if (_weapons is null && _fireEnabled) _weapons = new LevelWeapons(_file);
            return _weapons;
        }
    }

    private bool _fireEnabled;

    /// <summary>Enables firing for the interactive viewer; the scene then advances live projectiles.</summary>
    public void EnableFiring()
    {
        _fireEnabled = true;
        _ = Weapons;
        // func_8002FC90:188-199 refills the control block the fire path spends from, and the gauge
        // bits at +0x1C/+0x1E raise the same health word the walker carries.
        if (_pickups is null)
        {
            _pickupField = WeaponPickups.FromMission(_file.Bytes, WeaponCatalog.LoadRegistry(_file), Map);
            _pickupField.OnRetired = RetirePickupEntity;
            _pickups = new PickupItems(_pickupField,
                PlayerResupplyState.ForWalker(Weapons!.Slots, () => Walker!.Health,
                    v => Walker!.Health = (short)v, 0x7F00));
        }
        EnableObjectives();
        BindInitialPickupsToCsacSpawner();
    }

    private ObjectiveField? _objectives;
    private int _playerTeam;
    private Action<CaptureObjective, int>? _onPostCaptureScript;

    /// <summary>
    /// The capturable outposts (entity type 0x24) of this mission and the match score they feed, or
    /// null until the live level is enabled.
    /// </summary>
    public ObjectiveField? Objectives => _objectives;

    /// <summary>
    /// Hook for the post-capture script call at 8002D1A0.c:238-239
    /// (<c>gInterpretEntityScript(self-&gt;unk9C, 0, other-&gt;unk14 &amp; 0x7FFF)</c>).
    /// Runs after the scene's RPNS handler and can be set before or after <see cref="EnableObjectives"/>.
    /// </summary>
    public Action<CaptureObjective, int>? OnPostCaptureScript
    {
        get => _onPostCaptureScript;
        set => _onPostCaptureScript = value;
    }

    /// <summary>
    /// The candidate table of the map-zone scan (D_800A8F90 for <c>func_80031DE8</c>): the scene's
    /// own extras plus, per frame, every spawned unit that carries the scan fields
    /// (<see cref="ICsacNodeId"/>). Live entities without them (props, projectiles) are not
    /// candidates, exactly like the entities retail never links into that list.
    /// </summary>
    public IList<ICsacNodeId> MapZoneScanMembers =>
        _purchasePads?.ExtraCandidates ?? PurchasePadSystem.ReadOnlyCandidates;

    /// <summary>
    /// Places the mission's outposts: every type-0x24 record of a Csac request (32 in CONFT.MIS). The
    /// player's team is the team byte of its own record (source + 0x1C, func_8002A5F4:118).
    /// </summary>
    public void EnableObjectives()
    {
        if (_objectives is not null) return;
        var records = MissionEntities.Load(_file);
        _playerTeam = records.FirstOrDefault(r => r.Type == 1 && r.Id == Actor.Id)?.Team ?? 0;
        var outpostRecords = records.Where(r => r.Type == CaptureObjective.EntityType).ToList();
        var objectives = outpostRecords
            .Select(r => CaptureObjective.FromRecord(r, LevelGround.Height(Map, r.X, 0x100000, r.Z, out _)))
            .ToList();
        // Sky Captain (type 0x25): spawn before MatchScore so gSinglePlayerEntity's team and id
        // come from one source of truth (including DebugFallbackSpawn).
        _skyCaptain = new SkyCaptainSystem();
        _skyCaptain.BindMap(Map);
        _skyCaptain.SpawnFromRecords(records, MechWorld,
            pos => LevelGround.Height(Map, pos[0], pos[1], pos[2], out _), Actor.Id, objectives);
        int droneTeam = _skyCaptain.Drone is not null ? _skyCaptain.Team : -1;
        int droneId = _skyCaptain.Record?.Id ?? -1;
        var score = new MatchScore(_playerTeam, singlePlayerTeam: droneTeam, singlePlayerEntityId: droneId);
        _skyCaptain.BindScore(score);
        _objectives = new ObjectiveField(objectives, score)
        {
            OnPostCaptureScript = RunPostCaptureScript,
        };

        // Retail load order: gInitMissionSpawnState (8003F79C.c) -> load Csfx/Cact descriptors into
        // D_800A0C70 / D_800A7040 -> spawn entities (gInitEntityBase -> gQueueOrApplyMapOccupancy,
        // deferred while ScreenState == 5) -> gFinishLevelLoad (8003FAD4.c: marks chains, replays
        // LIFO deferral queue, releases pool) -> switch to ScreenState == 4 (running screen loop).
        ObjectRegistry.InitMissionSpawnState();
        ObjectRegistry.ScreenState = MapObjectRegistry.ScreenStateLoading;
        ObjectRegistry.GroundHeight = pos => LevelGround.Height(Map, pos.X, pos.Y, pos.Z, out _);
        ObjectRegistry.ResolveModel = id => _modelsById.TryGetValue(id, out var m) ? m : null;
        ObjectRegistry.ReadElevatorRider = ReadPlayerElevatorRider;
        ObjectRegistry.LoadFromMission(_file.Bytes);
        ObjectRegistry.LiveObjects.Allocate();

        // The outposts and their child parts join the entity list that carries damage to message 5.
        // A Csac record is spawned by gSpawnTargetableEntityId, which adds 0x10000000 (8004D4F8.c:59).
        _entities = new EntityWorld
        {
            // 8002AA1C.c:128-130 and 8004D958.c:53-55 run the entity's damage and retire scripts.
            OnScript = (_, script, arg1, arg2) => RunEntityScript(script, arg1, arg2),
            HasSlotDescriptor = slot => ObjectRegistry.GetSlotDescriptor(slot) is not null,
            OnRetireFootprint = (entity, _, mask) => ObjectRegistry.SpawnEntitySlotTypes(entity, mask, mode: 2),
            OnRefreshSlotSpawns = (entity, mask) => ObjectRegistry.RefreshEntitySlotSpawns(entity, mask),
        };
        for (int i = 0; i < objectives.Count; i++)
        {
            SpawnObjective(objectives[i], outpostRecords[i].Container == EntityContainer.Csac);
        }

        // Place supported Cact mission actors: type-10 elevators (CONFT: 4 [15, 24, 37, 39],
        // JOKE: 2 [262, 249], SLIM: 2 [262, 249], HK: 0, OVMP: 0).
        //
        // Type 20 (mounted turret carrier, func_80039504 / func_800395A8 with child 0x15
        // func_80039298): deliberately NOT wired into LevelScene. Census of all five shipped
        // Precinct Assault maps (CONFT, HK, JOKE, OVMP, SLIM) shows 0 instances of type 20 in
        // every map. Wiring it into the scene would require host bindings that no PA map exercises:
        // a class-5 rail-mover host (func_800382B4), part-vertex attachment transform
        // (func_80017668), animation/attack-scheduler callbacks (func_80044B34 / func_80060238 /
        // func_8005F6E8), and a dedicated carrier+child render pass.
        ObjectRegistry.PlaceMissionObjects(records);

        _csacSpawner = new CsacUnitSpawner(ObjectRegistry, world: _entities)
        {
            SessionActive = 1, // D_8009FA94 +0x5C == 1 in Precinct Assault (8006125C.c:50)
            SinglePlayerTeam = droneTeam,
            Player0Team = _playerTeam,
            // The mission-wide coarse grid built by func_800695A8 (BindMap above).
            HeightGrid = _skyCaptain.HeightGrid,
        };
        _csacSpawner.LoadPathNetworks(_file.Bytes,
            (x, z, mode) => LevelGround.PlacementHeight(Map, x, z, mode) ?? 0);
        var spawnerRecords = records.Where(r => r.Container == EntityContainer.Csac).ToDictionary(r => r.HeaderOffset);
        _purchasePads = PurchasePadSystem.LoadFromMission(_file.Bytes, _csacSpawner, random: _csacSpawner.Random,
            world: _entities, groundHeight: (x, z, rank) => LevelGround.PlacementHeight(Map, x, z, rank) ?? 0,
            registerSpawner: offset =>
            {
                if (spawnerRecords.TryGetValue(offset, out var record))
                    _csacSpawner.Register(record, MissionEntities.ReadSpawnerParameters(_file.Bytes, record));
            });
        if (_purchasePads is null)
            _csacSpawner.RegisterFromRecords(records, r => MissionEntities.ReadSpawnerParameters(_file.Bytes, r));
        if (_purchasePads is not null)
        {
            _purchasePads.VmState.GameSession[RpnsVmState.SessionPlayerCount] = 1;
            // Route RPNS op 56 (func_8004DC64) commands 0x52 (seek/snap) and 0x53 (freeze/unfreeze)
            // to placed type-10 elevators (80041728.c:88-136).
            var prevTag = _purchasePads.VmState.OnCommandByTag;
            _purchasePads.VmState.OnCommandByTag = (tag, op, arg) =>
            {
                if (op is 0x52 or 0x53)
                {
                    foreach (var lift in ObjectRegistry.Elevators)
                    {
                        if (lift.Record.Id == tag)
                            lift.TryQueueCommand((byte)op, (byte)arg);
                    }
                }
                prevTag?.Invoke(tag, op, arg);
            };
        }
        _entities.OnSpawnedUnitRetired = _csacSpawner.RecordSpawnedUnitRetirement;

        // The scene already placed these outposts. Account for their first spawn (8006125C.c:48-49)
        // so the interval only starts ticking once a retail max-concurrent slot is free.
        foreach (var slot in _csacSpawner.Slots.Where(s => s.Record.Type == CaptureObjective.EntityType))
        {
            slot.TotalSpawned++;
            if (slot.RemainingCount > 0) slot.RemainingCount--;
        }
        BindInitialPickupsToCsacSpawner();
        _csacSpawner.OnPostSpawnScript = (slot, unit) =>
        {
            if (slot.Record.Type == WeaponPickups.PlacementPayloadType)
            {
                SpawnCsacPickup(slot, unit);
                return;
            }
            if (slot.Record.Type != CaptureObjective.EntityType)
            {
                // As for the outposts, use model bounds for the 800620D8.c:58-94 pair test.
                unit.Coord = CoordOf(unit.ModelId, unit.X, unit.Y, unit.Z);
                return;
            }
            var record = slot.Record;
            var objective = CaptureObjective.FromRecord(record, LevelGround.Height(Map, record.X, 0x100000, record.Z, out _));
            _objectives.Add(objective);
            SpawnObjective(objective, targetable: true);
            // Outposts are driven by EntityWorld, not the generic registry wrapper.
            ObjectRegistry.LiveObjects.Unregister(unit);
        };
        ObjectRegistry.FinishLevelLoad();
        ObjectRegistry.ScreenState = MapObjectRegistry.ScreenStateImmediatePlay;
        ObjectRegistry.CullActiveElevators(MechWorld.X, MechWorld.Z);
    }

    private void BindInitialPickupsToCsacSpawner()
    {
        if (_pickupsBoundToCsac || _pickupField is null || _csacSpawner is null || _entities is null) return;
        _pickupsBoundToCsac = true;
        // All Precinct Assault type-0x10 pickups are Csac spawners (maxConcurrent = 1, remainingCount = -1).
        // Account for the initial placement (8006125C.c:48-49) and attach a targetable LiveEntity
        // (8004D4F8.c:59) so the retail tSAC countdown (800616A0.c:70-76) only ticks after pickup
        // retirement and no duplicate Csac model spawns while the item is still uncollected.
        foreach (var slot in _csacSpawner.Slots)
        {
            if (slot.Record.Type != WeaponPickups.PlacementPayloadType) continue;
            var item = _pickupField.Items.FirstOrDefault(p => p.SectionId == slot.Record.Id && !_pickupEntities.ContainsKey(p));
            if (item is null) continue;
            slot.TotalSpawned++;
            if (slot.RemainingCount > 0) slot.RemainingCount--;
            var entity = new PickupEntity(slot.Record, item.X, item.Z);
            _entities.Spawn(entity, targetable: true);
            _pickupEntities[item] = entity;
        }
    }

    private void SpawnCsacPickup(CsacSpawnerSlot slot, SpawnedCsacUnit unit)
    {
        // Type-0x10 pickups are driven by WeaponPickups (8002FA20.c / 8002FF6C.c), not the generic
        // registry wrapper. Keep unit in EntityWorld without a hit coord so contact retirement runs
        // gRetireEntity (8004D958.c) -> gSweepRetiredEntities (8004D598.c) ->
        // gRecordSpawnedUnitRetirement (80061020.c) to free the Csac slot for the next respawn.
        ObjectRegistry.LiveObjects.Unregister(unit);
        if (_pickupField is null) return;
        var pickup = WeaponPickups.FromRecord(slot.Record, Map);
        if (pickup is null) return;
        unit.Y = pickup.Y;
        _pickupEntities[pickup] = unit;
        _pickupField.Add(pickup);
    }

    private void RetirePickupEntity(WeaponPickup item)
    {
        if (_entities is null || !_pickupEntities.Remove(item, out var entity)) return;
        // gApplyRotatingPropProximityEffect (8002FF6C.c:78) calls gRetireEntity (8004D958.c:48-60).
        LiveEntity.Retire(entity, _entities);
    }

    private sealed class PickupEntity : LiveEntity
    {
        private readonly int _recordId;
        private readonly (int X, int Z) _ground;

        public PickupEntity(EntityRecord record, int x, int z)
        {
            _recordId = record.Id;
            _ground = (x, z);
            DamageScript = record.Scripts.ElementAtOrDefault(0);
            RetireScript = record.Scripts.ElementAtOrDefault(1);
            Health = MaxHealth = unchecked((short)record.Health);
        }

        public override int RecordId => _recordId;
        public override int DispatchType => WeaponPickups.PlacementPayloadType;
        public override (int X, int Z) GroundPosition => _ground;
        public override int HandleMessage(int message, EntityWorld world, object? data) =>
            BaseHandler(message, this, world, data);
    }

    private ElevatorRider? ReadPlayerElevatorRider(int slot)
    {
        if (slot != 0 || _walker is not { } walker) return null;
        return new ElevatorRider(walker.Platform, walker.Form);
    }

    private void SpawnObjective(CaptureObjective objective, bool targetable)
    {
        objective.Coord = CoordOf(objective.ModelId, objective.X, objective.Y, objective.Z);
        if (objective.ChildPart is { } child)
            child.Coord = CoordOf(child.ModelId, objective.X, objective.Y, objective.Z);
        _entities!.SpawnObjective(objective, targetable);
        // gInitEntityBase ends with gQueueOrApplyMapOccupancy (8002A5F4.c:167); during load this
        // stacks onto D_8009F708 for replay in FinishLevelLoad, and at runtime applies immediately.
        if (objective.ChildPart is { } spawnedChild)
            ObjectRegistry.QueueOrApplyEntityOccupancy(spawnedChild);
        ObjectRegistry.QueueOrApplyEntityOccupancy(objective);
    }

    private void RunPostCaptureScript(CaptureObjective objective, int claimantId)
    {
        RunEntityScript(objective.Script, arg1: 0, arg2: claimantId);
        _onPostCaptureScript?.Invoke(objective, claimantId);
    }

    private void RunEntityScript(int offset, int arg1, int arg2)
    {
        if (_purchasePads is not { } pads || _objectives is not { } objectives) return;
        var script = pads.Pool.Slice(offset);
        if (script.IsEmpty || script[0] == 0) return; // 80069D68.c:163-165

        if (_skyCaptain is { } sky) pads.SyncFromEconomy(sky.Economy);
        pads.SyncFromMatchScore(objectives.Score);
        RpnsScriptVm.Execute(script, pads.VmState, arg1, arg2);
        pads.SyncToMatchScore(objectives.Score);
        if (_skyCaptain is { } skyAfter) pads.SyncToEconomy(skyAfter.Economy);
    }

    private SkyCaptainSystem? _skyCaptain;
    private CsacUnitSpawner? _csacSpawner;
    private PurchasePadSystem? _purchasePads;
    private int _purchaseFrameTime;
    private EntityWorld? _entities;

    /// <summary>
    /// The entity list (D_8009FA40) for outposts, their child parts, trigger attachments, spawned units
    /// and projectile contacts, or null until the live level is enabled. <see cref="AdvanceWalker"/>
    /// runs its retirement stages every frame.
    /// </summary>
    public EntityWorld? Entities => _entities;

    /// <summary>The collision coordinate of a model instance at a world position (func_80013AF0).</summary>
    private CollisionCoord CoordOf(int modelId, int x, int y, int z) =>
        CollisionCoord.FromFrameBounds(x, y, z,
            _modelsById.TryGetValue(modelId, out var model) ? model.FrameBounds : Array.Empty<short[]>());

    /// <summary>Shared map-object occupancy and live-object registry (D_800A0C70 / D_800A7040 / D_8009F800).</summary>
    public MapObjectRegistry ObjectRegistry { get; } = new();

    /// <summary>Retail-keyed Csac spawner registered from the mission's Csac records, or null until enabled.</summary>
    public CsacUnitSpawner? CsacSpawner => _csacSpawner;

    /// <summary>
    /// Counts one rendered frame for the trigger time (<see cref="PurchaseFrameTime"/>): the scene
    /// ticks it with the frame counter of the screen capture (80029F48.c:85), which only a draw runs.
    /// </summary>
    public void AdvanceDrawFrame() => _purchaseFrameTime = unchecked(_purchaseFrameTime + 1);

    /// <summary>The frame counter the trigger paths read (<c>D_8009FAA4</c>).</summary>
    public int PurchaseFrameTime => _purchaseFrameTime;

    /// <summary>The purchase-pad system the mission loaded, with the shared RPNS VM state.</summary>
    public PurchasePadSystem? PurchasePads => _purchasePads;

    /// <summary>Live linked map zones (<c>D_8009F6F8</c>), or an empty list before objectives are enabled.</summary>
    public IReadOnlyList<PurchasePad> ActiveMapZones =>
        _purchasePads?.ActiveMapZones ?? Array.Empty<PurchasePad>();

    /// <summary>Player model collision radius (<c>obj+0x68</c>, <c>func_80013AF0</c>).</summary>
    public int PlayerCollisionRadius =>
        _parts.Count > 0 ? CoordOf(_parts[0].Model.Id, MechWorld.X, MechWorld.Y, MechWorld.Z).Radius : 0;

    /// <summary>
    /// Opt-in flag to advance purchase pads, drain <see cref="CsacSpawner"/> and broadcast
    /// message 6 over <see cref="ObjectRegistry"/> during <see cref="AdvanceSkyCaptain"/>; the drain runs
    /// before the map-zone scan of that frame.
    /// </summary>
    public bool EnableCsacSpawner { get; set; }

    /// <summary>The 1P Precinct Assault Sky Captain, or null until the live level is enabled.</summary>
    public SkyCaptainSystem? SkyCaptain => _skyCaptain;

    /// <summary>Message 6 of the Sky Captain (800271A4.c:180) for <paramref name="dt"/> clock ticks.</summary>
    public void AdvanceSkyCaptain(int dt)
    {
        _skyCaptain?.Advance(dt, this);
        if (EnableCsacSpawner && _csacSpawner is not null)
        {
            ObjectRegistry.BroadcastMessage6(dt);
            // func_800616A0 (gUpdateRegisteredSpawners) runs in the entity update, before the node
            // list walk of func_80031DE8 (:90): a unit due this frame stands in D_800A8F90 for this
            // frame's scan, which is why the drain precedes it.
            _csacSpawner.Drain(dt);
            // gCullActiveEntitiesSolo (800271A4.c:178) rebuilds D_800A8F90 before the zone scan (:90)
            // and the command pass (func_8004CECC) read it. AdvanceWalker normally ran it already for
            // this frame; this path can also run without the walker step, so it culls on its own.
            _entities?.CullActive(_walker?.X ?? MechWorld.X, _walker?.Z ?? MechWorld.Z);
            AdvancePurchasePads(dt);
            // gRunSoloSessionFrame (800271A4.c:210-212) sweeps retired entities after spawners and
            // timers, decrementing Csac TotalSpawned via gRecordSpawnedUnitRetirement (8004D598.c:51-52).
            _entities?.SweepRetired();
        }
    }

    private void AdvancePurchasePads(int dt)
    {
        if (_purchasePads is not { } pads || _objectives is not { } objectives) return;
        _purchaseFrameTime = unchecked(_purchaseFrameTime + dt);
        // Refresh D_8009FA58 player targets before syncing D_800A8F90 (retail spawns players into
        // D_8009FA40 with flag 8 in 800579D4.c:207, so gCullActiveEntitiesSolo/Split always place
        // them in D_800A8F90 for 80031DE8.c:119-144).
        if (_walker is { }) UpdatePlayerTargets();
        // D_800A8F90 for this frame, in the frame order retail walks it (the node list, 80031DE8.c:90).
        // A table a caller assigned itself stays as it is; the walk below then rescans from it.
        if (!pads.OwnsTable) pads.SyncTargetCandidates(_entities?.Active, _playerTargets.Values);
        if (_skyCaptain is { } sky) pads.SyncFromEconomy(sky.Economy);
        pads.SyncFromMatchScore(objectives.Score);
        pads.TickTimers(dt);
        if (_walker is { })
        {
            UpdateMapZoneTriggers(pads, dt);
            // Type-14 trigger attachments: func_8003151C message 10 tests a pad against the player
            // entity the message carries (8003151C.c:105-122), so the scene walks the D_8009FA58
            // entries it has (80031DE8.c:19) instead of measuring from the P1 body alone.
            var players = _playerTargets.Values;
            foreach (var pad in pads.Pads)
            {
                // The pad gate itself (unkA0 bit 0x10) sits before the test in 8003151C.c:106.
                if (pad.Kind != PurchasePadKind.TriggerAttachment) continue;
                if (pad.IsRetired || !pad.IsEnabled) continue;
                foreach (var player in players)
                {
                    // The entity's own obj+0x50/54/58 and its model radius at +0x68 (80031C74.c:84-100)
                    // are the collision coordinate the entity carries; do not measure from the P1 body.
                    if (!PurchasePadSystem.IsInsidePad(pad, player.X, player.Y, player.Z, player.Radius))
                        continue;
                    // scoreSlot stays the P1 bank the port's session reads (RpnsVmState.SessionScoreP1).
                    pads.TryTriggerPad(pad, player, frameTime: _purchaseFrameTime);
                    // Type 14 uses its byte at +0x28 for attachment behavior (80030DE0.c:115).
                }
            }
        }
        // func_8004CECC: the (kind, value) pairs a script queued this frame act only after the issuing
        // script returned, so gRetireEntity (8004CECC.c:108-114) never nests in the script or in the
        // pad trigger that ran it. gStepPendingMessages (8004DB74) retries full slot tables first.
        if (_entities is { } commandWorld) pads.RunEntityCommands(commandWorld, dt);
        pads.SyncToMatchScore(objectives.Score);
        if (_skyCaptain is { } skyAfter) pads.SyncToEconomy(skyAfter.Economy);
    }

    /// <summary>
    /// The D_8009FA58 entries a trigger test of this frame reads (80031DE8.c:19, 8003151C.c:105-122):
    /// the P1 body at slot 0 and — only when the caller attached a second player body — slot 1.
    /// </summary>
    public IReadOnlyList<PlayerTarget> PlayerTargets => _playerTargets.Values.ToList();

    /// <summary>
    /// Registers a second player entity in D_8009FA58[1] for both the map-zone candidate table
    /// (800579D4.c:189,207; 8004E248.c:140) and the type-14 trigger walk (8003151C.c:105-122).
    /// Replacing an existing second player removes its previous registration so no duplicate entries
    /// accumulate.
    /// </summary>
    /// <param name="body">The second player's body; only slot 1 accepts it.</param>
    /// <param name="actorId">Its actor record id (obj+0x14): 16 in CONFT.MIS.</param>
    public void AttachSecondPlayer(MechBody body, int actorId)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfZero(actorId);
        if (actorId == Actor.Id)
            throw new ArgumentException($"Second player actor id {actorId} collides with player 1.", nameof(actorId));
        if (_secondPlayerId != 0 && _secondPlayerId != actorId && _playerTargets.Remove(_secondPlayerId, out var removed))
            removed.Detach();
        _secondPlayer = body;
        _secondPlayerId = actorId;
        UpdatePlayerTarget(actorId, body);
        if (_purchasePads is { } pads) pads.TargetCandidatesStale = true;
    }

    /// <summary>
    /// Refreshes the D_8009FA58 entities of this frame: the P1 body and, once attached, the second
    /// player. <c>unkAC-&gt;unk40</c> is the form phase the body carries (0 = walker, 80031C74.c:102).
    /// </summary>
    private void UpdatePlayerTargets()
    {
        if (_walker is { } walker) UpdatePlayerTarget(Actor.Id, walker);
        if (_secondPlayer is { } second && _secondPlayerId != 0)
            UpdatePlayerTarget(_secondPlayerId, second);
    }

    private void UpdatePlayerTarget(int id, MechBody body)
    {
        if (!_playerTargets.TryGetValue(id, out var target))
            _playerTargets[id] = target = new PlayerTarget(id, body.X, body.Y, body.Z);
        var position = CoordOf(_parts[0].Model.Id, body.X, body.Y, body.Z);
        target.X = position.X;
        target.Y = position.Y;
        target.Z = position.Z;
        target.Radius = position.Radius;
        target.Busy = body.Form != 0;
    }

    /// <summary>
    /// func_80031DE8 (80031DE8.c:87-208), the per-frame walk of the map-zone node list D_8009F6F8:
    /// every node with <c>node-&gt;id != -1</c> (every CONFT map zone, 80031DE8.c:111-162) drops a
    /// cached target whose id changed, rescans D_800A8F90 when its timer ran out, and tests its
    /// cached target against the trigger box every frame while that target exists. Unassigned nodes
    /// (<c>node-&gt;id == -1</c>) test the D_8009FA58 player targets directly (:164-199). A non-zero
    /// box test releases the node unless it carries flag 2 (:172-184), modelled as
    /// <see cref="PurchasePad.Released"/>.
    /// </summary>

    private void UpdateMapZoneTriggers(PurchasePadSystem pads, int dt)
    {
        // 800319D4.c:114–119 prepends nodes; 80031DE8.c:102–104 starts at the head.
        for (int index = pads.ActiveMapZones.Count - 1; index >= 0; index--)
        {
            var node = pads.ActiveMapZones[index];
            if (node.MapZoneReleasePending)                                // :105-107, :176-184
            {
                if (pads.ReleaseMapZoneAt(index)) return;
                continue;
            }
            if (!node.IsEnabled) continue;                                 // :108-110

            if (node.TargetId == -1)                                       // :111, :164-199 (unassigned node tests D_8009FA58[0..1])
            {
                foreach (var player in _playerTargets.Values)
                {
                    if (!MapZoneBoxAccepts(node, player)) continue;
                    _ = pads.FireMapZone(node, player);
                    if ((node.Flags & 2) == 0)
                    {
                        if (pads.ReleaseMapZoneAt(index)) return;
                        break;
                    }
                }
                continue;
            }

            // :112-118: drop invalid targets, including a detached scene player. The node resets to
            // 600 and goes straight to the next entry: this frame neither decrements nor rescans it.
            if (node.MapZoneTarget is { } cached &&
                (node.MapZoneTargetId != cached.RecordId
                    || cached is PlayerTarget { IsDetached: true }
                    || cached is LiveEntity { RuntimeId: 0 }))
            {
                node.MapZoneTarget = null;                                 // :114
                node.MapZoneTimer = 0x258;                                 // :115 (u16 node->t+0x16)
                continue;
            }

            // :119: the timer is compared against delta only; it is never decremented here.
            if (node.MapZoneTimer < dt)
            {
                var target = pads.SelectMapZoneTarget(node); // :120-144
                if (!ReferenceEquals(node.MapZoneTarget, target))          // :145-150
                {
                    node.MapZoneTarget = target;
                    if (target is not null) node.MapZoneTargetId = target.RecordId;
                }
                // :151-157 (asm/us/main.s:40447-40456): rearm from the existing u16 timer with
                // +600 + (rand & 0x3F); on a miss store and goto next (no second +600 and no -= delta);
                // on a hit add another +600 before falling through to -= delta (:159).
                node.MapZoneTimer = unchecked((ushort)(node.MapZoneTimer + 0x258 + (pads.Random.Next() & 0x3F)));
                if (target is null) continue;
                node.MapZoneTimer = unchecked((ushort)(node.MapZoneTimer + 0x258));
            }
            // :159-160: u16 node->timer -= delta runs on non-rescan frames and on rescan hits,
            // wrapping in 16 bits.
            node.MapZoneTimer = unchecked((ushort)(node.MapZoneTimer - dt));
            if (node.MapZoneTarget is not { } nodeTarget) continue;

            // :168-174: the cached target is tested against the box every frame while it exists.
            if (!MapZoneBoxAccepts(node, nodeTarget)) continue;
            // :106-115: the script of the node runs with arg2 = target id & 0x7FFF, then the node
            // returns 1 and is unlinked and freed unless it carries flag 2 (:115).
            _ = pads.FireMapZone(node, nodeTarget);
            if ((node.Flags & 2) == 0 && pads.ReleaseMapZoneAt(index)) return;
        }
    }

    /// <summary>
    /// The box test of func_80031C74 (80031C74.c:84-103) without the script side: on every axis
    /// <c>|node - target| - target radius - node extent &lt; 0</c>, with both <see cref="PurchasePad"/>
    /// (<c>WorldX/Y/Z</c>) and <see cref="ICsacNodeId"/> (<c>X/Y/Z</c>) in full world units (retail
    /// stores <c>node-&gt;x/y/z &gt;&gt; 8</c> in <c>s16</c> and shifts back by <c>&lt;&lt; 8</c>).
    /// </summary>
    public static bool MapZoneBoxAccepts(PurchasePad node, ICsacNodeId target)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(target);
        long dx = Math.Abs((long)node.WorldX - target.X) - target.Radius - node.ExtentX;
        long dz = Math.Abs((long)node.WorldZ - target.Z) - target.Radius - node.ExtentZ;
        long dy = Math.Abs((long)node.WorldY - target.Y) - target.Radius - node.ExtentY;
        if (dx >= 0 || dz >= 0 || dy >= 0) return false;
        // 80031C74.c:102: <c>other->unk16 == 1 &amp;&amp; (node->flags &amp; 4) &amp;&amp; other->ptrAC->unk40 != 0</c>
        // rejects any type-1 entity whose control block is busy after the box, before the script of :106.
        // The entity carries its own control-block word (<see cref="ICsacNodeId.Busy"/>), not the class.
        return !(node.RequiresWalkerMode && target.DispatchType == 1 && target.Busy);
    }

    /// <summary>The cached target of a map zone (<c>node-&gt;target</c>, 80031DE8.c:131), or null.</summary>
    public ICsacNodeId? MapZoneTargetOf(PurchasePad node) => node.MapZoneTarget;

    /// <summary>True once the map-zone node was freed (<c>gFreeBlock</c>, 80031DE8.c:182).</summary>
    public bool IsMapZoneReleased(PurchasePad node) => node.Released;

    /// <summary>
    /// A read-only snapshot of D_800A8F90 as the map-zone scan reads it (80031DE8.c:119-144): the
    /// frame's active entities (<see cref="EntityWorld.Active"/>, published to
    /// <see cref="PurchasePadSystem.TargetCandidates"/>) followed by the candidates an owner added.
    /// The returned list is isolated from subsequent frame rebuilds and
    /// <see cref="AddMapZoneCandidate"/> calls (or returns the caller-owned table after
    /// <see cref="SetMapZoneCandidates"/>).
    /// </summary>
    public IReadOnlyList<ICsacNodeId?> MapZoneCandidates() =>
        _purchasePads?.TargetCandidates ?? Array.Empty<ICsacNodeId?>();

    /// <summary>
    /// Adds a candidate to the scan of D_800A8F90 (80031DE8.c:127-144) on top of the active entities,
    /// where it stays for the rest of the scene, unlike the per-frame active list.
    /// </summary>
    public void AddMapZoneCandidate(ICsacNodeId? candidate)
    {
        if (_purchasePads is not { } pads) throw new InvalidOperationException("Enable the level first");
        pads.AddTargetCandidate(candidate);
        // A unit placed inside a zone's box fires on the frame it exists (80031DE8.c:119-144 reads the
        // table of that frame); the walk may already have passed this node, so rescan it here.
        if (_walker is null) return;
        foreach (var node in pads.ActiveMapZones)
            pads.RescanMapZoneTarget(node, 1);
    }

    /// <summary>
    /// Supplies D_800A8F90 as the scene's own table: the scan of 80031DE8.c:127-144 then walks exactly
    /// this list every frame. The owning scene stops publishing <see cref="EntityWorld.Active"/>.
    /// </summary>
    public void SetMapZoneCandidates(IReadOnlyList<ICsacNodeId?> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (_purchasePads is not { } pads) throw new InvalidOperationException("Enable the level first");
        pads.TargetCandidates = candidates;
        pads.OwnsTable = true;
        pads.TargetCandidatesStale = false;
        // The table of the current scan changed: the node keeps its cached target where the new scan
        // finds the same entity and drops it where it does not, so no stale candidate outlives it.
        if (_walker is null) return;
        foreach (var node in pads.ActiveMapZones)
            pads.RescanMapZoneTarget(node, 1);
    }

    /// <summary>
    /// The target the candidate scan of 80031DE8.c:119-144 picks for <paramref name="node"/> out of
    /// <paramref name="candidates"/> (default: <see cref="MapZoneCandidates"/>), or null.
    /// </summary>
    public ICsacNodeId? MapZoneTarget(PurchasePad node, IReadOnlyList<ICsacNodeId?>? candidates = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        return candidates is not null
            ? PurchasePadSystem.SelectMapZoneTarget(node, candidates)
            : _purchasePads?.SelectMapZoneTarget(node);
    }

    /// <summary>Faces and marker lines the last <see cref="Render"/> drew for the Sky Captain.</summary>
    public int SkyCaptainPrimitives { get; private set; }

    /// <summary>
    /// The same face count as <see cref="SpawnedUnitPrimitives"/>, not a separate draw pass.
    /// </summary>
    public int SpawnedCsacUnitPrimitives => SpawnedUnitPrimitives;

    /// <summary>
    /// Faces the last <see cref="Render"/> drew for placed type-10 <see cref="Elevator"/> platforms
    /// in <see cref="ObjectRegistry"/>.
    /// </summary>
    public int ElevatorPrimitives { get; private set; }

    /// <summary>
    /// Faces the last <see cref="Render"/> drew for units spawned by <see cref="BuildOrderSpawner"/>
    /// (retired opt-in port extension beyond retail).
    /// </summary>
    public int BuiltUnitPrimitives { get; private set; }

    /// <summary>
    /// Faces the last <see cref="Render"/> drew for the Csac units in <see cref="Entities"/> (bought
    /// tanks and helis and other spawner units).
    /// </summary>
    public int SpawnedUnitPrimitives { get; private set; }

    /// <summary>
    /// Faces the last <see cref="Render"/> drew for the live weapon pickups; the models are the ones
    /// <see cref="DrawnPickupPlacements"/> lists.
    /// </summary>
    public int PickupPrimitives { get; private set; }

    /// <summary>Explicitly show viewer-only purchase-pad outlines; off in normal play.</summary>
    public bool ShowPadMarkers { get; set; }

    /// <summary>Outline lines the last <see cref="Render"/> drew for the purchase pads (viewer markers).</summary>
    public int PadMarkerLines { get; private set; }

    /// <summary>
    /// The HUD session with the live score words (D_8009FA94 + 0x64). +0x5C is 1 in a Precinct Assault
    /// match: the match end branches on it (80027D9C.c:78, 80060A0C.c:114). The unit counters
    /// +0x6C..+0x78 (gGameSession[27..30]) and the cumulative helipad-claim totals +0x84/+0x88
    /// (gGameSession[33..34]) come from the RPNS VM's session bank when the mission has purchase pads.
    /// No C function writes +0x84/+0x88 directly (gInitRuntime zeroes 0..0x38 at 80076EB0.c:82-85);
    /// the retail writer is gInterpretEntityScript op 21 (gGameSession[a]++, 80069D68.c:243-245),
    /// invoked by the slot-2 RPNS scripts of the outpost helipads (type-14 cmd 0x7A, 80031004.c:65-78;
    /// P1 pads 151/167/189/211 -> [33], P2/Sky-Captain pads 153/171/193/215 -> [34] across all five
    /// PA maps). The same two words are read by gDrawInGameHud (80047F30.c:41,218-223,257-262, gating
    /// the >= 30 score flash on total1 + total2 &lt; 4), gUpdatePlayerEntity (800588D4.c:22,251),
    /// gUpdateDroneStrategy (800336DC.c:71-72,750,818,822 as f84/f88), and gGetPlayerResultsStat
    /// (80059E9C.c:48-50, stat id 12).
    /// </summary>
    public HudSession ScoreSession(HudSession session)
    {
        if (_objectives is not { } field) return session;
        session = session with { Mode = 1, Score0 = field.Score.Score[0], Score1 = field.Score.Score[1] };
        if (_purchasePads?.VmState.GameSession is not { Length: > 34 } words) return session;
        return session with
        {
            Count1 = words[RpnsVmState.SessionTanksP1],
            Count2 = words[RpnsVmState.SessionHelisP1],
            Count3 = words[RpnsVmState.SessionTanksP2],
            Count4 = words[RpnsVmState.SessionHelisP2],
            Total1 = words[33],
            Total2 = words[34],
        };
    }

    /// <summary>
    /// Opt in to a caller-initialized one-player walker. Initialization and clip records must be
    /// supplied from a verified game state; this viewer does not invent a retail spawn state.
    /// Platforms, dynamic objects, 2P, script/sound side effects and rendered pose are not integrated.
    /// </summary>
    public void AttachWalker(MechBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Port != 0) throw new NotSupportedException("Only player port 0 is integrated");
        _animateWalker = false;
        _walker = body;
        ChaseCamera.Reset();
        _walkerWorld = new FutureCop.Game.Mech.MechWorld(Map)
        {
            // Dynamic ground-object probe (func_8006320C) over placed type-10 elevators.
            GroundObjects = ObjectRegistry.ProbeGroundObjects,
        };
        MechWorld = (body.X, body.Y, body.Z);
        UpdatePlayerTarget(Actor.Id, body);
        if (_purchasePads is { } pads) pads.TargetCandidatesStale = true;
        ObjectRegistry.CullActiveElevators(body.X, body.Z);
    }

    /// <summary>
    /// Retail chase-camera preset state (<c>info-&gt;state</c>, <c>src/game/800569EC.c:228-246</c>)
    /// stepped on every <see cref="AdvanceWalker"/> call from the translated pad word and live MapZones.
    /// </summary>
    public ChaseCameraState ChaseCamera { get; } = new();

    /// <summary>The attached walker body, so callers can read its health for the HUD.</summary>
    public MechBody? Walker => _walker;

    /// <summary>Advance the persistent walker by caller-supplied retail steps and input flags.</summary>
    public int AdvanceWalker(int steps, uint flags, AnalogPad pad)
    {
        if (_walker is null || _walkerWorld is null) throw new InvalidOperationException("Attach a walker first");
        // D_800A8F90 membership for the map-zone scan of this frame (80031DE8.c:119-144).
        _entities?.CullActive(_walker.X, _walker.Z);
        ObjectRegistry.CullActiveElevators(_walker.X, _walker.Z);
        _walkerWorld.Pads[0] = pad;
        // func_800588D4:259 runs the player unit update, not the bare walker: func_80055988 applies the
        // position shift, the L1+R1 form change, the control masks, and then dispatches to the walker
        // or the hover integrator by the form. The trigger below reads the pad word it masks in place.
        int ticks = steps * RetailClock.LocomotionTicks;
        int frames = ticks / RetailClock.TicksPerFrame;
        // In retail there is one word: state + 0x30 carries the translated buttons, the walker
        // integrate reads it, and :260-266 masks it in place before :288 gWeaponSlotInput reads the
        // masked result. HeldPadWord is that word for the trigger; the flags argument is the same
        // word for callers that only drive locomotion, so the step uses the union and the trigger
        // gets what the masking left of it.
        uint padWord = flags | _padWord;
        int result = FutureCop.Game.Mech.MechPlayer.Update(_walker, _walkerWorld, ticks, steps,
            ref padWord, out bool formChanged);
        // Bit 0 of the out word is per call (func_800588D4:260-265 spawns on it that frame only).
        FormChanged = formChanged;
        WalkerSteps += steps;
        MechWorld = (_walker.X, _walker.Y, _walker.Z);
        _weapons?.AdvanceShots(frames);
        // Beams live on the 60 Hz frame, like the trigger that spawns them.
        for (int f = 0; f < frames; f++) _weapons?.AdvanceBeams();
        // func_8002FF6C runs per frame against the player's position.
        _pickups?.Update(_walker.X, _walker.Y, _walker.Z);
        // Message 10 of func_8002D664 hands the touching entity to gTryCaptureObjective, using the
        // player's model instance radius at obj+0x68 (8002D1A0.c:169, 80013AF0.c:83).
        var playerCoord = CoordOf(_parts[0].Model.Id, _walker.X, _walker.Y, _walker.Z);
        _objectives?.Touch(new CaptureCandidate(Actor.Id, 1, _playerTeam, _walker.X, _walker.Y, _walker.Z,
            playerCoord.Radius));
        // func_800588D4 frame order: :287 gSlewEntityAim composes the arm instances and decays the recoil
        // timers (func_80057E40:201-215), :290 gWeaponSlotInput fires from those instances. The pose is
        // therefore built BEFORE the trigger, and the muzzle of this frame's shot sits on the displaced
        // arm - a kick stamped by the shot itself first moves the arm on the next frame.
        _weapons?.AdvanceRecoil(RetailClock.TicksPerFrame * frames);
        UpdateWalkerPose();
        _weapons?.Advance(_walker, _parts, padWord, RetailClock.TicksPerFrame * frames);
        // gUpdatePlayerEntity (800588D4.c:292) calls gUpdateChaseCamera (800569EC.c:228-246)
        // after gWeaponSlotInput (:290): decode the Select edge (0x40000), cycle info->state 0..6,
        // and gate states 5 and 6 on gTestCameraZoneOverlap (80031B40.c:74-89).
        ChaseCamera.Step(padWord, ActiveMapZones, _walker.X, _walker.Z, PlayerCollisionRadius,
            frozen: _walker.Frozen != 0);
        if (frames > 0) AdvanceRetirement();
        // Tick placed type-10 elevators on the walker clock when EnableCsacSpawner is off; when
        // EnableCsacSpawner is on, AdvanceSkyCaptain broadcasts message 6 over LiveObjects once.
        if (!EnableCsacSpawner && ticks > 0 && ObjectRegistry.Elevators.Count > 0)
        {
            foreach (var lift in ObjectRegistry.Elevators.ToArray())
                lift.OnMessage(LiveObjectRegistry.MessageFrameTick, ticks, 0);
        }
        return result;
    }

    /// <summary>
    /// The entity stages of the solo frame (800271A4.c:177-212) that turn a lethal hit into message 5:
    /// the cull (:178, func_8004DF28), the deferred updates that send message 4 to every entity whose
    /// health went below 0 (:182, func_8004CE04), the pair collision of the region pass that delivers
    /// message 11 (:191, func_80026D5C -&gt; 80062FB4 -&gt; 80062614 -&gt; 800623FC -&gt; 800620D8) and the
    /// retirement sweep that sends message 5 (:212, func_8004D598). The walker step above stands in
    /// for the message-6 broadcast of :180, so a shot fired this frame is tested this frame, and a
    /// lethal hit retires on the next frame, as in retail. A call that advances several frames at once
    /// runs the stages once, after the batched shot motion.
    /// </summary>
    private void AdvanceRetirement()
    {
        if (_entities is not { } world || _walker is null) return;
        world.CullActive(_walker.X, _walker.Z);
        world.ProcessDeferredUpdates();
        DeliverShotContacts(world);
        // A capture script of this step can queue a 0x3D pair through OnPostCaptureScript; the entry
        // pass applies it before the sweep, as func_8004CECC does. The pending-message timers are
        // stepped by the session frame in AdvancePurchasePads, so no elapsed time is passed here.
        PurchasePads?.RunEntityCommands(world);
        world.SweepRetired();
    }

    /// <summary>
    /// The projectile pairs of the region-pass collision. 80062C3C.c / 80062874.c fill the grid from
    /// D_800A8F90, skipping entries whose id is not positive and entries with flag 4; 800623FC.c lets
    /// an 0x800000 entity veto the pair through its message 10 and 800620D8.c:99-100 sends message 11
    /// to both sides. A shot that touches an entity becomes a <see cref="ProjectileContact"/> spawned
    /// into the list and leaves <see cref="LevelWeapons.Shots"/>: retail retires it on the next frame
    /// through its own message 11 (80036624.c:155-156).
    /// </summary>
    /// <remarks>
    /// Port assumptions: the projectile's pair flags are not ported, so the combined word is taken to
    /// carry 0x200 (800620D8.c:75; without it no projectile ever hits); the projectile's own message 10
    /// (80036624.c:127-142) only vetoes its owner, its carrier and same-kind projectiles, none of which
    /// is an outpost or child part; the per-cell bucketing of 80062614.c is not modelled because
    /// 800620D8.c:59 rejects pairs that far apart anyway.
    /// </remarks>
    private void DeliverShotContacts(EntityWorld world)
    {
        if (_weapons is not { Shots.Count: > 0 } weapons) return;
        foreach (var shot in weapons.Shots.ToArray())
        {
            var shotCoord = CollisionCoord.FromFrameBounds(shot.X, shot.Y, shot.Z, shot.Model.FrameBounds);
            var projectile = new ProjectileContact(Actor.Id, unchecked((short)shot.Definition.ObjWord00),
                (byte)shot.Definition.ObjWord0E, shot.X, shot.Z, shot.Y,
                ProjectileBlast.FromDefinition(shot.Definition, shot.Muzzle.X, shot.Muzzle.Y, shot.Muzzle.Z));
            DeliverShotContact(world, projectile, shotCoord);
            // A contact that was spawned into the list touched an entity: the shot left the weapon.
            if (projectile.RuntimeId != 0) weapons.Shots.Remove(shot);
        }
    }

    /// <summary>
    /// One projectile's region-pass pair loop (800623FC.c / 800620D8.c:99-100): the message-11
    /// delivery against every active entity, gated by each side's message-10 veto and the pair
    /// distance. A shot that reaches an entity is spawned once (unless it already carries a
    /// runtime id, like the golden harness's pre-spawned contact) and the pair exchange message 11.
    /// </summary>
    /// <remarks>
    /// Port assumptions: the projectile's pair flags are not ported, so the combined word is taken to
    /// carry 0x200 (800620D8.c:75; without it no projectile ever hits); the projectile's own message 10
    /// (80036624.c:127-142) only vetoes its owner, its carrier and same-kind projectiles, none of which
    /// is an outpost or child part; the per-cell bucketing of 80062614.c is not modelled because
    /// 800620D8.c:59 rejects pairs that far apart anyway. The per-part hit test of func_80061CAC is
    /// stubbed: <see cref="EntityPairCollision.ReachesPartTest"/> reports a hit for the whole
    /// part, because every Precinct Assault model carries the per-part bit (0x8000000, 8002A5F4.c:133-138).
    /// </remarks>
    public static void DeliverShotContact(EntityWorld world, ProjectileContact projectile, CollisionCoord shotCoord)
    {
        bool spawned = false;
        foreach (var entity in world.Active.ToArray())
        {
            if (entity is ProjectileContact) continue;
            if (entity.RuntimeId <= 0 || (entity.Flags & 4) != 0 || entity.Coord is not { } coord) continue;
            if ((entity.Flags & LiveEntity.PairGateFlag) != 0 && entity.HandleMessage(10, world, projectile) == 0)
                continue;
            if (!EntityPairCollision.ReachesPartTest(coord, shotCoord)) continue;
            if (!spawned && projectile.RuntimeId == 0)
            {
                world.Spawn(projectile);
                spawned = true;
            }
            entity.HandleMessage(11, world, projectile);
            projectile.HandleMessage(11, world, entity);
        }
    }

    /// <summary>
    /// Set when func_80055988 completed a transform this step: :258-268 spawns the type-4 effect
    /// and plays sound 0x15, which the viewer treats as a latch for its own effects.
    /// </summary>
    public bool FormChanged { get; private set; }

    /// <summary>
    /// Pad buttons held this frame, for the HUD and for callers that only need the raw set.
    /// </summary>
    public PadButtons HeldButtons { get; set; }

    private uint _padWord;

    /// <summary>
    /// The translated pad word gWeaponSlotInput reads (func_800576A8, stored at state + 0x30). The
    /// caller sets this before <see cref="AdvanceWalker"/>: it is the same word
    /// <see cref="MechInput.TranslatePadButtons"/> produced for the walker, not the raw button set.
    /// </summary>
    public uint HeldPadWord
    {
        get => _padWord;
        set => _padWord = value;
    }

    private void UpdateWalkerPose()
    {
        if (_animateWalker && _walker is { } walker)
        {
            // The drawn frames are the ones the instances carry, never a function of state + 0xBC:
            // the body frame is +0xD4 set by func_800549F0:878/:916 on foot and by
            // func_80055988:241/:248 during the transform (scrub of clip 0xC, clip 0xD at the end),
            // the companion frame is +0xAC of its track (func_80055988:240/:247). func_800597C0:254-269
            // reads state + 0xBC only for the two polygon-group masks. The lift of :235-237 is already
            // in the position (obj + 0x54), so the assembly itself is not raised.
            int mainFrame = walker.Track.Frame;
            int companionFrame = walker.CompanionTrack.Clips is null ? -1 : walker.CompanionTrack.Frame;
            if (_parts[0].Frame != mainFrame || _poseCompanionFrame != companionFrame
                || _poseProgress != walker.Progress)
            {
                _poseProgress = walker.Progress;
                _poseCompanionFrame = companionFrame;
                var (mainMask, companionMask) = PlayerForm.Masks(walker.Progress);
                _parts = PlayerAssembly.Build(Actor, _file.Models, mainFrame, companionFrame, mainMask, companionMask);
                _poseMatrices = _parts.Select(p => p.Matrix).ToArray();
            }
        }
        ApplyRecoil();
    }

    // -1 = no pose built from the walker yet, so the first UpdateWalkerPose always assembles.
    private int _poseProgress = -1;
    private int _poseCompanionFrame = int.MinValue;

    /// <summary>The matrices <see cref="PlayerAssembly"/> composed, before any recoil of this frame.</summary>
    private Gte.Mat[] _poseMatrices = Array.Empty<Gte.Mat>();

    /// <summary>
    /// func_80057E40:202-215: the two arm instances (<c>parts[2]</c> = state + 0x178,
    /// <c>parts[3]</c> = state + 0x1C4) are displaced along their own Z axis by the recoil timers of
    /// state + 0x174 / + 0x176. The displacement is applied to the cached joint matrices, never to the
    /// already displaced ones, because the C recomposes the instance from the joint chain
    /// (func_80057E40:201) before every application.
    /// </summary>
    private void ApplyRecoil()
    {
        if (_weapons is not { } weapons) return;
        var parts = _parts.ToArray();
        for (int side = 0; side < WeaponRecoil.Count; side++)
        {
            int index = 2 + side;
            if (index >= parts.Length || index >= _poseMatrices.Length) break;
            parts[index] = parts[index] with { Matrix = weapons.Recoil.Apply(_poseMatrices[index], side) };
        }
        _parts = parts;
    }

    /// <summary>
    /// Displacement the render actually applies to the two arms this frame, in world position units:
    /// the drawn arm matrix minus the unrecoiled one that the joint chain composed. This is the barrel
    /// travel of func_80057E40 - the only visible body reaction retail has to a shot.
    /// </summary>
    public ((int X, int Y, int Z) Right, (int X, int Y, int Z) Left) ArmRecoil
    {
        get
        {
            if (_weapons is null || _poseMatrices.Length < 4) return ((0, 0, 0), (0, 0, 0));
            Gte.Mat Delta(int index)
            {
                var drawn = _parts[index].Matrix;
                var rest = _poseMatrices[index];
                return drawn with { Tx = drawn.Tx - rest.Tx, Ty = drawn.Ty - rest.Ty, Tz = drawn.Tz - rest.Tz };
            }
            var right = Delta(2);
            var left = Delta(3);
            return ((right.Tx, right.Ty, right.Tz), (left.Tx, left.Ty, left.Tz));
        }
    }

    /// <summary>Cumulative retail locomotion substeps supplied to the walker.</summary>
    public long WalkerSteps { get; private set; }

    /// <summary>The five player instances as func_800597C0 submits them this frame (frames, masks, matrices).</summary>
    public IReadOnlyList<PlayerPart> Parts => _parts;

    /// <summary>Walker state that affects the projected geometry and camera.</summary>
    public ((int X, int Y, int Z) Position, int Angle, int Frame) WalkerRenderPose =>
        (MechWorld, _animateWalker ? _walker!.Angle : 0, _parts[0].Frame);

    /// <summary>Ground attribute at the scene's initial placement.</summary>
    public int GroundAttribute { get; private set; }
    public PlayerActor Actor { get; }

    public byte[] Rgb { get; } = new byte[Width * Height * 3];
    public int VisibleCells { get; private set; }
    public int TilePackets { get; private set; }
    public int MechFaces { get; private set; }
    public IReadOnlyList<int> DrawnSections { get; private set; } = Array.Empty<int>();
    public (int X, int Y, int Z) Eye { get; private set; }
    /// <summary>View matrix computed by the last <see cref="Render"/> call.</summary>
    public Gte.Mat? LastView { get; private set; }

    /// <summary>
    /// Ramp table D_800A2630 for the k == 6/7 colour variants. Viewer placeholder, not retail:
    /// gBuildTilePattern func_800654F8 (which fills it from the SLFX definition and the frame time)
    /// is not ported, so every entry is the neutral modulation 0x80.
    /// </summary>
    public byte[] Ramp { get; } = Enumerable.Repeat((byte)0x80, 256).ToArray();

    public LevelScene(string misPath)
    {
        MissionPath = misPath;
        _file = new MisFile(misPath);
        foreach (var model in _file.Models)
        {
            if (_modelsById.TryAdd(model.Id, model))
                _widthsByModelId[model.Id] = model.Widths.TryGetValue(1, out var w) ? w.ToArray() : Array.Empty<short>();
        }
        Map = LevelMap.FromMisFile(misPath);
        _textures = TextureBank.FromMisFile(misPath);
        // D_800A8EA4 = gTexpageBaseTable[9] = the packed word of Cbmp slot 9 (80063C5C.c:144).
        var hudPage = _textures.Resources.FirstOrDefault(r => r.Id == HudTexpageResourceId);
        HudTexpageWord = hudPage is null ? 0u : ((uint)hudPage.Clut << 16) | hudPage.Tpage;
        Actor = _file.PlayerActors.FirstOrDefault() ?? throw new InvalidDataException("mission has no player actor");
        _parts = PlayerForm.Build(Actor, _file.Models, 0);
        _poseMatrices = _parts.Select(p => p.Matrix).ToArray();

        var clip = Map.Header.WorldClipRect;
        int x = (clip[2] + clip[3]) / 2, z = (clip[0] + clip[1]) / 2;
        // Probe from well above so the highest surface below the ceiling wins (func_80066F78 keeps
        // the greatest height below position[1] + 0x200).
        int ground = LevelGround.Height(Map, x, 0x100000, z, out int attribute);
        MechWorld = (x, ground, z);
        GroundAttribute = attribute;
        (_mechCx, _mechCy, _mechCz) = MechCentre();
    }

    private (int, int, int) MechCentre()
    {
        double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
        double z0 = double.MaxValue, z1 = double.MinValue;
        foreach (var part in _parts)
        {
            // A model without the requested frame chunk has no points to measure.
            if (!part.Model.Vertices.TryGetValue(part.Model.IsScripted ? 1 : part.Frame + 1, out var verts)) continue;
            var points = part.Model.IsScripted
                ? ScriptRender.PoseVertices(part.Model, verts, part.Frame, part.Matrix)
                : verts.Select(v => (
                    (part.Matrix.W[0] * (double)v.X + part.Matrix.W[3] * (double)v.Y + part.Matrix.W[6] * (double)v.Z) / 65536 + part.Matrix.Tx / 16.0,
                    (part.Matrix.W[1] * (double)v.X + part.Matrix.W[4] * (double)v.Y + part.Matrix.W[7] * (double)v.Z) / 65536 + part.Matrix.Ty / 16.0,
                    (part.Matrix.W[2] * (double)v.X + part.Matrix.W[5] * (double)v.Y + part.Matrix.W[8] * (double)v.Z) / 65536 + part.Matrix.Tz / 16.0)).ToArray();
            foreach (var (px, py, pz) in points)
            {
                x0 = Math.Min(x0, px); x1 = Math.Max(x1, px);
                y0 = Math.Min(y0, py); y1 = Math.Max(y1, py);
                z0 = Math.Min(z0, pz); z1 = Math.Max(z1, pz);
            }
        }
        MechBounds = (y0, y1);
        return ((int)((x0 + x1) / 2), (int)((y0 + y1) / 2), (int)((z0 + z1) / 2));
    }

    /// <summary>Draw the walker (on by default); off lets a check isolate the mech pixels.</summary>
    public bool DrawMech { get; set; } = true;

    /// <summary>
    /// When set, <see cref="Render"/> emits the triangles to this sink instead of rasterizing them
    /// into <see cref="Rgb"/>. Geometry, culling, ordering and camera maths are unchanged.
    /// </summary>
    public IPrimitiveSink? Sink { get; set; }

    /// <summary>The mission's texture memory, i.e. the source the GPU texture cache decodes from.</summary>
    public Vram Vram => _textures.Vram;

    /// <summary>
    /// <c>D_800A8EA4</c> (<c>gEnvMapTexpageWord</c>): the packed <c>(CLUT &lt;&lt; 16) | TPAGE</c> word of
    /// <c>gTexpageBaseTable[9]</c>. The two tables are the same array -
    /// <c>gTexpageBaseTable</c> is <c>D_800A8E80</c> (80063C5C.c:27) and <c>D_800A8EA4</c> is 0x24 bytes
    /// further, i.e. element 9 - written by <c>gLoadCbmpTexture</c> for <c>s0 = unk1C - 1</c>
    /// (80063C5C.c:78,96,144), i.e. the tenth uploaded Cbmp. The HUD's slot-icon emitter reads
    /// <c>[0]</c> of it (8006BF44.c:8,23). Zero when the mission ships no tenth Cbmp, which leaves the
    /// HUD on its solid-block fallback.
    /// </summary>
    public uint HudTexpageWord { get; }

    /// <summary>
    /// Cbmp resource id whose packed word is <c>gTexpageBaseTable[9]</c> = <c>D_800A8EA4</c>.
    /// <c>gLoadCbmpTexture</c> indexes the table with <c>unk1C - 1</c>, so table slot 9 is the tenth
    /// uploaded texture (80063C5C.c:78,144).
    /// </summary>
    public const int HudTexpageResourceId = 10;

    /// <summary>Local y range of the assembled walker in model units (min, max).</summary>
    public (double Min, double Max) MechBounds { get; private set; }

    /// <summary>
    /// World placements of the pickups the last <see cref="Render"/> drew: exactly one entry per live
    /// <see cref="WeaponPickup"/>, in archive order. This is the list the draw pass consumes, so it is
    /// what a test can count without a rasterizer.
    /// </summary>
    public IReadOnlyList<PlacedPickup> DrawnPickupPlacements => _drawnPickupPlacements;

    private readonly List<PlacedPickup> _drawnPickupPlacements = new();

    /// <summary>Pickup list of the frame being rendered, or null for the scene's own.</summary>
    private PickupItems? _renderingPickupField;

    /// <summary>One pickup model at one world position: what the frame draws for a live pickup.</summary>
    /// <param name="Model">The mission model whose Cobj id the pickup named (null when it has none).</param>
    /// <param name="X">World x of the placement.</param>
    /// <param name="Y">World y of the placement.</param>
    /// <param name="Z">World z of the placement.</param>
    public readonly record struct PlacedPickup(Model? Model, int X, int Y, int Z);

    /// <summary>Renders one frame. Returns the number of drawn primitives.</summary>
    /// <param name="camera">Camera to render from.</param>
    /// <param name="pickupField">
    /// Pickup list for this frame, or null for the scene's own. The draw path is identical either
    /// way, so a test can feed a field the retail archive cannot produce (a record without an aRSL
    /// Cobj entry) and read <see cref="DrawnPickupPlacements"/> back without a rasterizer.
    /// </param>
    public int Render(LevelCamera camera, WeaponPickups? pickupField = null)
    {
        _renderingPickupField = pickupField is null
            ? null
            : new PickupItems(pickupField, new PlayerResupplyState(new WeaponSlots()));
        if (Sink is null) Array.Clear(Rgb);
        // View of the mech-local frame (model units, pivot = walker centre), applying the active
        // retail chase-camera preset offset (D_8009AEC4[PresetState] / D_8009AD08, 800569EC.c:305-513).
        var effective = camera.ResolvePreset();
        var view = ViewTransform.Turn(effective.Yaw, effective.Elevation, _mechCx, _mechCy, _mechCz, effective.Distance);
        LastView = view;

        // Eye in mech-local model units: camera = A p + T/16 vanishes at the eye -> e = -A^T T / 16.
        double[] t = { view.Tx / 16.0, view.Ty / 16.0, view.Tz / 16.0 };
        double A(int i, int j) => view.W[i + 3 * j] / 65536.0;
        double ex = 0, ey = 0, ez = 0;
        for (int i = 0; i < 3; i++) { ex -= A(i, 0) * t[i]; ey -= A(i, 1) * t[i]; ez -= A(i, 2) * t[i]; }
        // Mech-local model units -> world units (x16); the mech stands at MechWorld with its local
        // origin on the ground (world y up). The local frame is not rotated against the world.
        var eye = (MechWorld.X + (int)Math.Round(ex * 16), MechWorld.Y + (int)Math.Round(ey * 16),
            MechWorld.Z + (int)Math.Round(ez * 16));
        Eye = eye;
        var pivotWorld = (X: MechWorld.X + _mechCx * 16, Z: MechWorld.Z + _mechCz * 16);

        var rotation = Gte.Load(view with { Tx = 0, Ty = 0, Tz = 0 });
        var gte = new TerrainRender.GteSetup(rotation, Focal, Ofx, Ofy, 1);

        // gUpdateCameraAnchor: window origin = (look-at ground point >> 17) - 2 on both axes.
        int originZ = (pivotWorld.Z >> 17) - 2, originX = (pivotWorld.X >> 17) - 2;
        var window = CellWindow.Sample(Map, eye.Item1, eye.Item2, eye.Item3, originZ, originX, 1, rotation);

        _drawQueue.Clear();
        int drawOrder = 0;
        var ctx = new TerrainRender.EmitContext(_texpages, Ramp, 0);
        var drawn = new List<int>();
        int packetsTotal = 0;
        VisibleCells = window.Count(e => e.Visible);
        for (int j = 0; j < 5; j++)
        {
            for (int i = 0; i < 5; i++)
            {
                var entry = window[j * 5 + i];
                if (entry.Mask == 0) continue;
                // A window cell whose section is missing in the map (corrupt or partial level data)
                // is drawn as empty instead of failing the whole frame.
                if (Map.Section(entry.SectionId) is not { } section) continue;
                drawn.Add(entry.SectionId);
                var buffer = _vertexBuffer;
                TerrainRender.ProjectGrid(section, buffer, entry.X, entry.Y, entry.Z, (uint)entry.Mask << 16, gte);
                // func_8006531C: camera position relative to the region in 1/16 tile units.
                int a = (eye.Item3 >> 9) - (originZ << 8) - 0x100 * i;
                int b = (eye.Item1 >> 9) - (originX << 8) - 0x100 * j;
                _packets.Clear();
                var packets = _packets;
                uint mask = (uint)entry.Mask;
                for (int ra = 0xC0; ra >= 0; ra -= 0x40)
                    for (int rb = 0xC; rb >= 0; rb -= 4)
                    {
                        if ((mask & 1) != 0) TerrainRender.EmitCell(section, buffer, a, b, (uint)(ra + rb), ctx, packets);
                        mask >>= 1;
                    }
                packetsTotal += packets.Count;
                foreach (var packet in packets)
                {
                    // Packet depth: the 24-bit field of the tag word, sign-extended like the original.
                    _drawQueue.Add(new DrawItem
                    {
                        Key = (int)(packet[0] & 0xFFFFFF) << 8 >> 8,
                        Order = drawOrder++,
                        Packet = packet,
                    });
                }
            }
        }
        DrawnSections = drawn;
        TilePackets = packetsTotal;

        // The walker: mech-local parts composed with the view, the same path as WireframeScene.
        int mechFaces = 0;
        var bodyView = view;
        if (_animateWalker && _walker is { Angle: not 0 } walker)
        {
            var (sin, cos) = MechMath.SinCos(walker.Angle);
            var heading = new Gte.Mat(new[] { cos, 0, -sin, 0, 0x10000, 0, sin, 0, cos }, 0, 0, 0);
            bodyView = ScriptRender.Compose(heading, view);
        }
        foreach (var part in DrawMech ? _parts : Array.Empty<PlayerPart>())
        {
            // A model without the requested frame chunk is left out of the frame.
            if (!part.Model.Vertices.TryGetValue(part.Model.IsScripted ? 1 : part.Frame + 1, out var partVerts)) continue;
            var composed = ScriptRender.Compose(part.Matrix, bodyView);
            mechFaces += QueueModel(part.Model, partVerts, part.Frame, composed, part.Mask, ref drawOrder);
        }
        MechFaces = mechFaces;

        // Live projectiles, after the walker so they sort into the same painter's-order list and end
        // up behind or in front of the terrain and the mech by their own depth.
        if (_weapons is { Shots.Count: > 0 } weapons)
        {
            foreach (var shot in weapons.Shots)
            {
                if (!shot.Model.Vertices.TryGetValue(1, out var shotVerts) || shotVerts.Count == 0) continue;
                var shotMatrix = ShotMatrix(shot, view);
                QueueModel(shot.Model, shotVerts, 0, shotMatrix, 0xFF, ref drawOrder);
            }
        }

        // Weapon pickups lying on the map (func_8002FA20). Same pipeline as the shots: local offset
        // from the mech origin, no rotation, sorted into the shared list by depth.
        // One model at one position per live pickup: a record whose ModelId names no mission model
        // (WeaponPickups.NoModel, i.e. no aRSL Cobj entry) adds no model, like the null instance
        // func_8002ACB4:60-62 skips, and is still listed so the count stays one entry per pickup.
        _drawnPickupPlacements.Clear();
        int pickupPrims = 0;
        if ((_renderingPickupField ?? _pickups) is { RemainingCount: > 0 } pickups)
        {
            foreach (var item in pickups.Items)
            {
                if (item.Collected) continue;
                _modelsById.TryGetValue(item.ModelId, out var model);
                List<Vertex>? frame = null;
                if (model is not null && (!model.Vertices.TryGetValue(1, out frame) || frame.Count == 0))
                    model = null;

                _drawnPickupPlacements.Add(new PlacedPickup(model, item.X, item.Y, item.Z));
                if (model is null || frame is null) continue;
                var pickupMatrix = PickupMatrix(item, view);
                pickupPrims += QueueModel(model, frame, 0, pickupMatrix, 0xFF, ref drawOrder);
            }
        }
        PickupPrimitives = pickupPrims;

        // Outposts (type 0x24): the record's own model and the child gInitObjectFromDef spawns from the
        // second aRSL model (8002C584.c:286-295), both at the outpost position, unrotated like pickups.
        if (_objectives is { } objectives)
        {
            foreach (var objective in objectives.Objectives)
            {
                for (int slot = 0; slot < 2; slot++)
                {
                    int modelId = slot == 0 ? objective.ModelId : objective.ChildModelId;
                    if (!_modelsById.TryGetValue(modelId, out var model)
                        || !model.Vertices.TryGetValue(1, out var verts) || verts.Count == 0) continue;
                    var matrix = PlacedMatrix(objective.X, objective.Y, objective.Z, view);
                    QueueModel(model, verts, 0, matrix, 0xFF, ref drawOrder);
                }
            }
        }

        // Sky Captain: its record model placed with the body basis (+0x2C rows) at +0x50. First light;
        // the row/column convention of the basis against the GTE matrix is not verified yet.
        int skyCaptain = 0;
        if (_skyCaptain is { Visible: true } sky && _modelsById.TryGetValue(sky.ModelId, out var skyModel)
            && skyModel.Vertices.TryGetValue(1, out var skyVerts) && skyVerts.Count > 0)
        {
            var body = sky.Drone!.Body;
            var r = body.BasisRows;
            var local = new Gte.Mat(new[] { r[0][0], r[0][1], r[0][2], r[1][0], r[1][1], r[1][2], r[2][0], r[2][1], r[2][2] },
                ((body.Pos[0] - MechWorld.X) >> 4) << 4, ((body.Pos[1] - MechWorld.Y) >> 4) << 4,
                ((body.Pos[2] - MechWorld.Z) >> 4) << 4);
            var matrix = ScriptRender.Compose(local, view);
            skyCaptain += QueueModel(skyModel, skyVerts, 0, matrix, 0xFF, ref drawOrder);
        }

        // Type-10 elevators placed in ObjectRegistry (func_80041488: yaw basis from source +0x36 at
        // current height +0x54).
        int elevatorPrims = 0;
        if (ObjectRegistry.Elevators.Count > 0)
        {
            var liftBasis = new int[9];
            foreach (var lift in ObjectRegistry.Elevators)
            {
                int modelId = lift.Record.Model();
                if (!_modelsById.TryGetValue(modelId, out var model)
                    || !model.Vertices.TryGetValue(1, out var verts) || verts.Count == 0) continue;
                MechWalker.RotationMatrix(lift.Angle, 0, 0, liftBasis);
                var local = new Gte.Mat(liftBasis,
                    ((lift.X - MechWorld.X) >> 4) << 4,
                    ((lift.Y - MechWorld.Y) >> 4) << 4,
                    ((lift.Z - MechWorld.Z) >> 4) << 4);
                var matrix = ScriptRender.Compose(local, view);
                elevatorPrims += QueueModel(model, verts, 0, matrix, 0xFF, ref drawOrder);
            }
        }
        ElevatorPrimitives = elevatorPrims;

        // Built units (retired opt-in port extension beyond retail): tanks and aircraft spawned from
        // DroneStrategy build-order counters, drawn with their mission Cobj model along heading.
        int builtUnits = 0;
        if (_skyCaptain is { EnableBuildOrderSpawner: true, BuiltUnits.Count: > 0 } builtSky)
        {
            foreach (var unit in builtSky.BuiltUnits)
            {
                if (!_modelsById.TryGetValue(unit.ModelId, out var model)
                    || !model.Vertices.TryGetValue(1, out var verts) || verts.Count == 0) continue;
                var local = new Gte.Mat(new[]
                {
                    unit.DirZ, 0, -unit.DirX,
                    0, 0x10000, 0,
                    unit.DirX, 0, unit.DirZ,
                }, ((unit.X - MechWorld.X) >> 4) << 4, ((unit.Y - MechWorld.Y) >> 4) << 4,
                    ((unit.Z - MechWorld.Z) >> 4) << 4);
                var matrix = ScriptRender.Compose(local, view);
                builtUnits += QueueModel(model, verts, 0, matrix, 0xFF, ref drawOrder);
            }
        }
        BuiltUnitPrimitives = builtUnits;

        // Draw Csac units from the live entity list, not the spawner's bookkeeping list.
        // 8006125C.c:74-76 adds them through gSpawnTargetableEntityId. Retirement clears the
        // runtime id (8004D958.c:59) before the sweep unlinks the unit (8004D598.c:55-64).
        // The world holds specialized outpost entities, not their spawner wrappers; the objective
        // pass above draws them. SpawnedCsacUnit has no heading, so its model is placed unrotated.
        int spawnedUnits = 0;
        if (_entities is { Units.Count: > 0 } entities)
        {
            foreach (var entity in entities.Units)
            {
                if (entity is not SpawnedCsacUnit unit || unit.RuntimeId == 0) continue;
                // Type-0x10 pickups are drawn through the _pickups pass above (ground-snapped by
                // func_8002FA20), never a second time as a generic Csac unit.
                if (unit.EntityType == WeaponPickups.PlacementPayloadType) continue;
                if (!_modelsById.TryGetValue(unit.ModelId, out var model)
                    || !model.Vertices.TryGetValue(1, out var verts) || verts.Count == 0) continue;
                var matrix = PlacedMatrix(unit.X, unit.Y, unit.Z, view);
                spawnedUnits += QueueModel(model, verts, 0, matrix, 0xFF, ref drawOrder);
            }
        }
        SpawnedUnitPrimitives = spawnedUnits;

        // Painter's order, far first, stable for equal keys (submission order). Viewer convention:
        // retail sorts into an ordering table whose bucket mapping is not ported; both keys are SZ3
        // averages. An unstable sort reorders coplanar terrain packets whenever the mech is added.
        _drawQueue.Sort(DrawOrder);
        foreach (var item in _drawQueue)
        {
            if (item.Packet is { } packet) DrawPacket(packet);
            else DrawModelFace(item.Face, item.Model!);
        }

        // Live beams (func_8006B23C) are drawn after the sorted faces: the ribbon builder retail uses
        // is not ported, so each beam is one line from muzzle to far end and must sit on top.
        if (_weapons is { Beams.Count: > 0 } beams) BeamLines(beams, view);
        // Sky Captain marker (basis axes at the body), on top like the beams so it is always visible.
        var markers = _skyCaptain?.MarkerSegments();
        for (int m = 0; markers is not null && m < markers.Count; m++)
        {
            var (a, b) = markers[m];
            var pa = ProjectWorld(a.X, a.Y, a.Z, view);
            var pb = ProjectWorld(b.X, b.Y, b.Z, view);
            if (pa is null || pb is null) continue;
            Line(pa.Value.X, pa.Value.Y, pb.Value.X, pb.Value.Y, new byte[] { 0xFF, 0x40, 0x40 });
            skyCaptain++;
        }
        SkyCaptainPrimitives = skyCaptain;
        PadMarkerLines = ShowPadMarkers ? PadMarkers(view) : 0;
        return _drawQueue.Count;
    }

    /// <summary>
    /// Draws the player-usable purchase and outpost-claim pads as ground outlines on top of the frame
    /// and returns the number of lines drawn. Viewer presentation, not retail: a pad is an invisible
    /// trigger box in the C (80031C74.c, 8002F8B8.c) and the visible pad is map geometry, so the
    /// outline shows the x/z box the port tests (<see cref="PurchasePadSystem.IsInsidePad"/>).
    /// Type-95 map zones are a green rectangle; type-14 attachment pads a yellow rectangle with both
    /// diagonals, grey while flag 0x10 disables them (8003151C.c:106). Retired attachments and
    /// one-shot map zones that were freed (80031DE8.c:176-184) are no longer drawn.
    /// </summary>
    /// <remarks>
    /// Three viewer-usability rules derived from rendered inspection on CONFT.MIS:
    /// (1) Draw at the snapped trigger centre: raw tACT Y places raised outpost markers below the
    /// floor (18 of 54 pads in CONFT.MIS have a nonzero snapped height).
    /// (2) Non-walker type-95 zones (<c>Flags &amp; 4 == 0</c>, 26-28 per PA map) are huge base-perimeter
    /// intrusion boxes (98304 x 163840) and Csac scoring zones rather than player buy pads; skipping
    /// them keeps the 4 type-95 and 24 type-14 walker pads readable without lines slicing through walls.
    /// (3) The viewer omits pads outside a <c>0x38000</c> box per axis to keep un-depth-tested
    /// lines off distant rooftops. The cutoff borrows the initial range of the retail active-entity
    /// list (8004DF28.c:101,142), not its octagonal distance, overflow adjustment, or drawing rules.
    /// </remarks>
    private int PadMarkers(Gte.Mat view)
    {
        if (_purchasePads is not { Pads.Count: > 0 } pads) return 0;
        const int maxPadDist = 0x38000;
        int lines = 0;
        foreach (var pad in pads.Pads)
        {
            if (!pad.RequiresWalkerMode) continue;
            if (pad.IsRetired) continue;
            bool mapZone = pad.Kind == PurchasePadKind.MapZone;
            if (mapZone && (pad.Flags & 2) == 0 && pad.Released) continue;
            if (Math.Abs(pad.WorldX - MechWorld.X) >= maxPadDist || Math.Abs(pad.WorldZ - MechWorld.Z) >= maxPadDist)
                continue;
            byte[] colour = mapZone ? PadZoneColour : pad.IsEnabled ? PadAttachmentColour : PadDisabledColour;
            int y = pad.WorldY;
            int x0 = pad.WorldX - pad.ExtentX, x1 = pad.WorldX + pad.ExtentX;
            int z0 = pad.WorldZ - pad.ExtentZ, z1 = pad.WorldZ + pad.ExtentZ;
            var c00 = ProjectWorld(x0, y, z0, view);
            var c10 = ProjectWorld(x1, y, z0, view);
            var c11 = ProjectWorld(x1, y, z1, view);
            var c01 = ProjectWorld(x0, y, z1, view);
            lines += PadLine(c00, c10, colour) + PadLine(c10, c11, colour)
                + PadLine(c11, c01, colour) + PadLine(c01, c00, colour);
            if (!mapZone) lines += PadLine(c00, c11, colour) + PadLine(c10, c01, colour);
        }
        return lines;
    }

    private static readonly byte[] PadZoneColour = { 0x40, 0xFF, 0x40 };
    private static readonly byte[] PadAttachmentColour = { 0xFF, 0xD0, 0x20 };
    private static readonly byte[] PadDisabledColour = { 0x80, 0x80, 0x80 };

    private int PadLine((int X, int Y)? a, (int X, int Y)? b, byte[] colour)
    {
        if (a is null || b is null) return 0;
        Line(a.Value.X, a.Value.Y, b.Value.X, b.Value.Y, colour);
        return 1;
    }

    /// <summary>
    /// Painter's order, far first, ties in submission order. Equivalent to the former
    /// <c>OrderByDescending(key).ThenBy(index)</c>, but without the two LINQ passes and their lists;
    /// <see cref="DrawItem.Order"/> is unique, so the comparison is a total order.
    /// </summary>
    private static int DrawOrder(DrawItem a, DrawItem b)
    {
        int byKey = b.Key.CompareTo(a.Key);
        return byKey != 0 ? byKey : a.Order.CompareTo(b.Order);
    }

    private static int Sx(uint xy) => (short)(xy & 0xFFFF);
    private static int Sy(uint xy) => (short)(xy >> 16);

    /// <summary>Decodes one func_800648E8 packet (codes 0x24/0x2C/0x34/0x3C, bit 1 = semi-transparent).</summary>
    private void DrawPacket(uint[] p)
    {
        byte code = (byte)(p[1] >> 24);
        bool semi = (code & 2) != 0;
        ushort clut = (ushort)(p[3] >> 16);
        // Packed 0xRRGGBB from a packet colour word (low byte = red, as the GPU reads it).
        static uint Rgb3(uint w) => (w & 0xFF) << 16 | (w & 0xFF00) | (w >> 16) & 0xFF;
        switch (code & ~2)
        {
            case 0x24:
            {
                ushort tpage = (ushort)(p[5] >> 16);
                var c = Rgb3(p[1]);
                Tri(p[2], p[3], c, p[4], p[5], c, p[6], p[7], c, tpage, clut, semi);
                break;
            }
            case 0x2C:
            {
                ushort tpage = (ushort)(p[5] >> 16);
                var c = Rgb3(p[1]);
                Tri(p[2], p[3], c, p[4], p[5], c, p[6], p[7], c, tpage, clut, semi);
                Tri(p[4], p[5], c, p[8], p[9], c, p[6], p[7], c, tpage, clut, semi);
                break;
            }
            case 0x34:
            {
                ushort tpage = (ushort)(p[6] >> 16);
                Tri(p[2], p[3], Rgb3(p[1]), p[5], p[6], Rgb3(p[4]), p[8], p[9], Rgb3(p[7]), tpage, clut, semi);
                break;
            }
            case 0x3C:
            {
                ushort tpage = (ushort)(p[6] >> 16);
                Tri(p[2], p[3], Rgb3(p[1]), p[5], p[6], Rgb3(p[4]), p[8], p[9], Rgb3(p[7]), tpage, clut, semi);
                Tri(p[5], p[6], Rgb3(p[4]), p[11], p[12], Rgb3(p[10]), p[8], p[9], Rgb3(p[7]), tpage, clut, semi);
                break;
            }
        }
    }

    /// <summary>Textured, colour-modulated triangle; xy = packed screen word, uv = low 16 bits (u | v &lt;&lt; 8).</summary>
    internal void Tri(uint xy0, uint uv0, uint c0, uint xy1, uint uv1, uint c1, uint xy2, uint uv2, uint c2,
        ushort tpage, ushort clut, bool semi)
    {
        int x0 = Sx(xy0), y0 = Sy(xy0), x1 = Sx(xy1), y1 = Sy(xy1), x2 = Sx(xy2), y2 = Sy(xy2);
        int area = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
        if (area == 0) return;
        int u0 = (int)(uv0 & 0xFF), v0 = (int)((uv0 >> 8) & 0xFF), u1 = (int)(uv1 & 0xFF), v1 = (int)((uv1 >> 8) & 0xFF);
        int u2 = (int)(uv2 & 0xFF), v2 = (int)((uv2 >> 8) & 0xFF);
        // GPU path: hand the triangle over instead of rasterizing it here. The order is the same,
        // because every consumer walks the same painter's-order list built in Render().
        if (Sink is not null)
        {
            Sink.TexturedTriangle(
                x0, y0, u0, v0, (byte)(c0 >> 16), (byte)(c0 >> 8), (byte)c0,
                x1, y1, u1, v1, (byte)(c1 >> 16), (byte)(c1 >> 8), (byte)c1,
                x2, y2, u2, v2, (byte)(c2 >> 16), (byte)(c2 >> 8), (byte)c2,
                tpage, clut, semi);
            return;
        }
        int minX = Math.Max(0, Math.Min(x0, Math.Min(x1, x2))), maxX = Math.Min(Width - 1, Math.Max(x0, Math.Max(x1, x2)));
        int minY = Math.Max(0, Math.Min(y0, Math.Min(y1, y2))), maxY = Math.Min(Height - 1, Math.Max(y0, Math.Max(y1, y2)));
        if (minX > maxX || minY > maxY) return;
        int abr = (tpage >> 5) & 3;
        double inv = 1.0 / area;
        int r0 = (int)(c0 >> 16) & 0xFF, g0 = (int)(c0 >> 8) & 0xFF, b0c = (int)c0 & 0xFF;
        int r1 = (int)(c1 >> 16) & 0xFF, g1 = (int)(c1 >> 8) & 0xFF, b1c = (int)c1 & 0xFF;
        int r2 = (int)(c2 >> 16) & 0xFF, g2 = (int)(c2 >> 8) & 0xFF, b2c = (int)c2 & 0xFF;
        var (pageX, pageY, depthBits) = Vram.DecodeTpage(tpage);
        var (clutX, clutY) = Vram.DecodeClut(clut);
        var vram = _textures.Vram;
        var cells = vram.Cells;
        int clutRow = clutY * Vram.Width + clutX;
        bool fastVram = depthBits switch
        {
            0 => pageX + 63 < Vram.Width && pageY + 255 < Vram.Height && clutX + 15 < Vram.Width && clutY < Vram.Height,
            1 => pageX + 127 < Vram.Width && pageY + 255 < Vram.Height && clutX + 255 < Vram.Width && clutY < Vram.Height,
            _ => pageX + 255 < Vram.Width && pageY + 255 < Vram.Height,
        };
        byte[] rgbBuf = Rgb;
        int dw0x = y1 - y2, dw0y = x2 - x1;
        int dw1x = y2 - y0, dw1y = x0 - x2;
        int dw2x = y0 - y1, dw2y = x1 - x0;
        int rowW0 = (x1 - minX) * (y2 - minY) - (y1 - minY) * (x2 - minX);
        int rowW1 = (x2 - minX) * (y0 - minY) - (y2 - minY) * (x0 - minX);
        int rowW2 = (x0 - minX) * (y1 - minY) - (y0 - minY) * (x1 - minX);
        bool positiveArea = area > 0;
        for (int y = minY; y <= maxY; y++)
        {
            int w0 = rowW0, w1 = rowW1, w2 = rowW2;
            int rowBase = (y * Width + minX) * 3;
            for (int x = minX; x <= maxX; x++, w0 += dw0x, w1 += dw1x, w2 += dw2x, rowBase += 3)
            {
                if (positiveArea ? ((w0 | w1 | w2) < 0) : (w0 > 0 || w1 > 0 || w2 > 0)) continue;
                double b0 = w0 * inv, b1 = w1 * inv, b2 = w2 * inv;
                int u = (int)Math.Round(b0 * u0 + b1 * u1 + b2 * u2), v = (int)Math.Round(b0 * v0 + b1 * v1 + b2 * v2);
                ushort raw;
                if (fastVram)
                {
                    int tx = u & 0xFF, ty = v & 0xFF;
                    int cellRow = (pageY + ty) * Vram.Width + pageX;
                    raw = depthBits switch
                    {
                        0 => cells[clutRow + ((cells[cellRow + (tx >> 2)] >> ((tx & 3) * 4)) & 0x0F)],
                        1 => cells[clutRow + ((cells[cellRow + (tx >> 1)] >> ((tx & 1) * 8)) & 0xFF)],
                        _ => cells[cellRow + tx],
                    };
                }
                else
                {
                    raw = vram.SampleRaw(tpage, clut, u, v);
                }
                if (raw == 0) continue; // PS1: texel 0x0000 is fully transparent
                int r5 = raw & 0x1F, g5 = (raw >> 5) & 0x1F, b5 = (raw >> 10) & 0x1F;
                int tr = (r5 << 3) | (r5 >> 2);
                int tg = (g5 << 3) | (g5 >> 2);
                int tb = (b5 << 3) | (b5 >> 2);
                int vr = Math.Min(255, (int)(tr * (b0 * r0 + b1 * r1 + b2 * r2) / 128.0));
                int vg = Math.Min(255, (int)(tg * (b0 * g0 + b1 * g1 + b2 * g2) / 128.0));
                int vb = Math.Min(255, (int)(tb * (b0 * b0c + b1 * b1c + b2 * b2c) / 128.0));
                if (semi && (raw & 0x8000) != 0)
                {
                    int br = rgbBuf[rowBase], bg = rgbBuf[rowBase + 1], bb = rgbBuf[rowBase + 2];
                    switch (abr)
                    {
                        case 0:
                            vr = (br + vr) >> 1; vg = (bg + vg) >> 1; vb = (bb + vb) >> 1; break;
                        case 1:
                            vr = Math.Min(255, br + vr); vg = Math.Min(255, bg + vg); vb = Math.Min(255, bb + vb); break;
                        case 2:
                            vr = Math.Max(0, br - vr); vg = Math.Max(0, bg - vg); vb = Math.Max(0, bb - vb); break;
                        default:
                            vr = Math.Min(255, br + (vr >> 2)); vg = Math.Min(255, bg + (vg >> 2)); vb = Math.Min(255, bb + (vb >> 2)); break;
                    }
                }
                rgbBuf[rowBase] = (byte)vr;
                rgbBuf[rowBase + 1] = (byte)vg;
                rgbBuf[rowBase + 2] = (byte)vb;
            }
            rowW0 += dw0y;
            rowW1 += dw1y;
            rowW2 += dw2y;
        }
    }

    /// <summary>
    /// View matrix of a projectile: the retail basis of func_800193F8 built from the flight vector,
    /// placed where the shot is. Retail rebuilds the same basis every tick (func_80035B7C:554-568), so
    /// the model nose (model +Z) points along the velocity.
    /// </summary>
    /// <remarks>
    /// The shot lives in world position units, but <paramref name="view"/> is the mech-local frame the
    /// level draws everything else in, so the position is converted here: subtract the walker's world
    /// position and divide by 16 (one model unit is 16 position units, func_80010750). Writing the raw
    /// world position with &lt;&lt; 4 put every projectile a few map widths away, which is why nothing
    /// showed up. The scale factor is 1 like the original - the wireframe viewer's 1.5 is its own
    /// convention (REPORT-feuern.md).
    /// </remarks>
    private Gte.Mat ShotMatrix(Shot shot, Gte.Mat view)
    {
        var basis = ShotMotion.FlightBasis(shot.Direction);
        int lx = (shot.X - MechWorld.X) >> 4;
        int ly = (shot.Y - MechWorld.Y) >> 4;
        int lz = (shot.Z - MechWorld.Z) >> 4;
        var local = new Gte.Mat(new[]
        {
            basis[0], basis[1], basis[2],
            basis[3], basis[4], basis[5],
            basis[6], basis[7], basis[8],
        }, lx << 4, ly << 4, lz << 4);
        return ScriptRender.Compose(local, view);
    }

    /// <summary>
    /// Model matrix of a pickup: the item's map position relative to the mech origin, in model units,
    /// with the identity orientation. func_8002FA20:92-113 stores position and extents only, so the
    /// retail item is not rotated.
    /// </summary>
    private Gte.Mat PickupMatrix(WeaponPickup item, Gte.Mat view) => PlacedMatrix(item.X, item.Y, item.Z, view);

    internal int QueueModel(Model model, IReadOnlyList<Vertex> verts, int frame, Gte.Mat matrix, int mask, ref int drawOrder)
    {
        if (_projectedScratch.Length < verts.Count)
            _projectedScratch = new ScreenVertex[verts.Count];
        Span<ScreenVertex> projected = _projectedScratch.AsSpan(0, verts.Count);
        if (model.IsScripted)
            ScriptRender.Project(model, verts, frame, matrix, Focal, Ofx, Ofy, 1, projected);
        else
            ModelRender.Project(Gte.Load(matrix), verts, Focal, Ofx, Ofy, 1, projected);
        var widths = _widthsByModelId.TryGetValue(model.Id, out var w) ? w : Array.Empty<short>();
        _facesScratch.Clear();
        ModelRender.WalkTextured(model.Polygons, projected, widths, mask, 0, _facesScratch);
        int count = _facesScratch.Count;
        for (int i = 0; i < count; i++)
            _drawQueue.Add(new DrawItem { Key = _facesScratch[i].Work.Z, Order = drawOrder++, Face = _facesScratch[i], Model = model });
        return count;
    }

    private static readonly int[] IdentityRotation =
    {
        0x10000, 0, 0,
        0, 0x10000, 0,
        0, 0, 0x10000,
    };

    /// <summary>Unrotated placement of a model at a world position, relative to the mech origin.</summary>
    private Gte.Mat PlacedMatrix(int x, int y, int z, Gte.Mat view)
    {
        int lx = (x - MechWorld.X) >> 4;
        int ly = (y - MechWorld.Y) >> 4;
        int lz = (z - MechWorld.Z) >> 4;
        // 0x10000 is 1.0 in engine matrix units (Gte.Load shifts the rotation right by 4).
        var local = new Gte.Mat(IdentityRotation, lx << 4, ly << 4, lz << 4);
        return ScriptRender.Compose(local, view);
    }

    /// <summary>
    /// Draws every live beam as a line from muzzle to far end, projected with the level camera. The
    /// beam is the one thing retail hands to a ribbon builder the port does not have, so the geometry
    /// is retail and only the look is the viewer's.
    /// </summary>
    private void BeamLines(LevelWeapons weapons, Gte.Mat view)
    {
        foreach (var beam in weapons.Beams)
        {
            var a = ProjectWorld(beam.X, beam.Y, beam.Z, view);
            var b = ProjectWorld(beam.End.X, beam.End.Y, beam.End.Z, view);
            if (a is null || b is null) continue;
            // Pale blue, the tracer colour func_8006B23C:418-421 asks for (0x300/0x200 with 0x30).
            Line(a.Value.X, a.Value.Y, b.Value.X, b.Value.Y, new byte[] { 0xB0, 0xD8, 0xFF });
        }
    }

    /// <summary>Projects one world position to screen pixels, or null when it is behind the camera.</summary>
    private (int X, int Y)? ProjectWorld(int x, int y, int z, Gte.Mat view)
    {
        int lx = (x - MechWorld.X) >> 4;
        int ly = (y - MechWorld.Y) >> 4;
        int lz = (z - MechWorld.Z) >> 4;
        var local = new Gte.Mat(IdentityRotation, lx << 4, ly << 4, lz << 4);
        var point = ModelRender.Project(Gte.Load(ScriptRender.Compose(local, view)),
            new[] { new Vertex(0, 0, 0) }, Focal, Ofx, Ofy, 1);
        if (point.Length == 0 || (point[0].Clip & 1) != 0) return null;
        return (Sx((uint)point[0].Xy), Sy((uint)point[0].Xy));
    }

    private void Line(int x0, int y0, int x1, int y1, byte[] colour)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            if (x0 >= 0 && x0 < Width && y0 >= 0 && y0 < Height)
            {
                int o = (y0 * Width + x0) * 3;
                Rgb[o] = colour[0];
                Rgb[o + 1] = colour[1];
                Rgb[o + 2] = colour[2];
            }
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private Dictionary<int, TextureRecord> TextureLookup(Model model)
    {
        if (!_texByModel.TryGetValue(model.Id, out var map))
        {
            map = model.Textures.GroupBy(t => t.Offset).ToDictionary(g => g.Key, g => g.First());
            _texByModel[model.Id] = map;
        }
        return map;
    }

    /// <summary>Mech face, drawn like WireframeScene.DrawTexturedFace (3DTL record, unmodulated texels).</summary>
    private void DrawModelFace(TexturedFace face, Model model)
    {
        bool hasRec = TextureLookup(model).TryGetValue(face.TextureOffset, out var rec);
        uint uvA = hasRec ? rec.Uv0 : 0, uvB = hasRec ? rec.Uv1 : 0;
        uint UV(int i) => i switch
        {
            0 => uvA & 0xFFFF, 1 => uvA >> 16, 2 => uvB & 0xFFFF, _ => uvB >> 16,
        };
        bool flat = hasRec && rec.TypeLoaded is 0 or 1;
        if (!hasRec || flat)
        {
            // Packed 0xRRGGBB; magenta marks a face whose texture record is missing.
            uint c = hasRec ? rec.Rgb & 0xFFFFFF : 0xFF00FF;
            FlatFace(face, c);
            return;
        }
        const uint w = 0x808080;
        uint a = (uint)face.Work.Xy0, b = (uint)face.Work.Xy1, c2 = (uint)face.Work.Xy2, d = (uint)face.Work.Xy3;
        if (face.Type == 3)
            Tri(a, UV(0), w, b, UV(1), w, c2, UV(2), w, rec.Tpage, rec.Clut, false);
        else if (face.Type == 4)
        {
            // Work holds (v0, v1, v3, v2).
            Tri(a, UV(0), w, b, UV(1), w, c2, UV(3), w, rec.Tpage, rec.Clut, false);
            Tri(b, UV(1), w, d, UV(2), w, c2, UV(3), w, rec.Tpage, rec.Clut, false);
        }
        else
        {
            Tri(a, UV(0), w, b, UV(1), w, c2, UV(2), w, rec.Tpage, rec.Clut, false);
            Tri(b, UV(1), w, d, UV(3), w, c2, UV(2), w, rec.Tpage, rec.Clut, false);
        }
    }

    internal void FlatFace(TexturedFace face, uint colour)
    {
        void Fill(uint p0, uint p1, uint p2)
        {
            int x0 = Sx(p0), y0 = Sy(p0), x1 = Sx(p1), y1 = Sy(p1), x2 = Sx(p2), y2 = Sy(p2);
            int area = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
            if (area == 0) return;
            if (Sink is not null)
            {
                Sink.FlatTriangle(x0, y0, x1, y1, x2, y2,
                    (byte)(colour >> 16), (byte)(colour >> 8), (byte)colour);
                return;
            }
            int minX = Math.Max(0, Math.Min(x0, Math.Min(x1, x2))), maxX = Math.Min(Width - 1, Math.Max(x0, Math.Max(x1, x2)));
            int minY = Math.Max(0, Math.Min(y0, Math.Min(y1, y2))), maxY = Math.Min(Height - 1, Math.Max(y0, Math.Max(y1, y2)));
            if (minX > maxX || minY > maxY) return;
            byte cr = (byte)(colour >> 16), cg = (byte)(colour >> 8), cb = (byte)colour;
            byte[] rgbBuf = Rgb;
            int dw0x = y1 - y2, dw0y = x2 - x1;
            int dw1x = y2 - y0, dw1y = x0 - x2;
            int dw2x = y0 - y1, dw2y = x1 - x0;
            int rowW0 = (x1 - minX) * (y2 - minY) - (y1 - minY) * (x2 - minX);
            int rowW1 = (x2 - minX) * (y0 - minY) - (y2 - minY) * (x0 - minX);
            int rowW2 = (x0 - minX) * (y1 - minY) - (y0 - minY) * (x1 - minX);
            bool positiveArea = area > 0;
            for (int y = minY; y <= maxY; y++)
            {
                int w0 = rowW0, w1 = rowW1, w2 = rowW2;
                int rowBase = (y * Width + minX) * 3;
                for (int x = minX; x <= maxX; x++, w0 += dw0x, w1 += dw1x, w2 += dw2x, rowBase += 3)
                {
                    if (positiveArea ? ((w0 | w1 | w2) < 0) : (w0 > 0 || w1 > 0 || w2 > 0)) continue;
                    rgbBuf[rowBase] = cr; rgbBuf[rowBase + 1] = cg; rgbBuf[rowBase + 2] = cb;
                }
                rowW0 += dw0y;
                rowW1 += dw1y;
                rowW2 += dw2y;
            }
        }
        uint a = (uint)face.Work.Xy0, b = (uint)face.Work.Xy1, c = (uint)face.Work.Xy2, d = (uint)face.Work.Xy3;
        Fill(a, b, c);
        if (face.Type != 3) Fill(b, d, c);
    }
}
