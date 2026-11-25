using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The four open integration points of the retirement chain: the spawner retirement counter
/// (func_80061020 on OnSpawnedUnitRetired), the retire footprint (func_8003B898 mode 2 on
/// OnRetireFootprint), the quartile slot refresh (func_8003BAE4 on OnRefreshSlotSpawns) and
/// the beam message-14 delivery of func_8006B23C.
/// </summary>
public sealed class RetirementChainHookTests
{
    // ---- shared fixtures -------------------------------------------------------------------

    private sealed class ProbeEntity : LiveEntity
    {
        public ProbeEntity(int recordId, int type = 5, byte slotIndex = 0)
        {
            RecordIdValue = recordId;
            TypeValue = type;
            SlotIndex = slotIndex;
        }

        public int RecordIdValue { get; }
        public int TypeValue { get; }
        public override int RecordId => RecordIdValue;
        public override int DispatchType => TypeValue;
        public override (int X, int Z) GroundPosition => (0, 0);
        public override int HandleMessage(int message, EntityWorld world, object? data)
        {
            if (message == 14 && data is DirectHit hit) Hits.Add(hit);
            return BaseHandler(message, this, world, data);
        }

        public List<DirectHit> Hits { get; } = new();
    }

    private sealed class HitEntity : LiveEntity
    {
        public HitEntity(int recordId, CollisionCoord coord, short health = 500)
        {
            RecordIdValue = recordId;
            Coord = coord;
            Health = health;
            MaxHealth = health;
        }

        public int RecordIdValue { get; }
        public override int RecordId => RecordIdValue;
        public override int DispatchType => 5;
        public override (int X, int Z) GroundPosition => (Coord!.Value.X, Coord!.Value.Z);
        public override int HandleMessage(int message, EntityWorld world, object? data)
        {
            if (message == 14 && data is DirectHit hit) Hits.Add(hit);
            return BaseHandler(message, this, world, data);
        }

        public List<DirectHit> Hits { get; } = new();
    }

    // ---- Item 1: func_80061020 on OnSpawnedUnitRetired ------------------------------------

    /// <summary>A minimal record with one Cobj model, as the Csac slots of the scene hold.</summary>
    private static readonly byte[] UnitSource = new byte[0x54];

    private static EntityRecord UnitRecord(int id) =>
        new(id, 5, 0x100000, 0x10000, 0x200000, 0x00110148, 100, 2, EntityContainer.Csac, 0, 0,
            UnitSource, Array.Empty<int>(), new[] { new ResourceReference("Cobj", 34) });

    [Fact]
    public void OnSpawnedUnitRetired_DropsTheMatchingSlotCounter_AndUnblocksTheRespawn()
    {
        var spawner = new CsacUnitSpawner();
        var slot = spawner.Register(UnitRecord(42),
            new CsacSpawnerParameters(RespawnDelayRaw: -10, MaxConcurrent: 1, InitialFlags: 0, InitialCount: -1));
        Assert.NotNull(slot);
        // RespawnDelayRaw -10 scales to 50; the timer starts armed at 50.
        Assert.Equal(50, slot!.RespawnDelay);
        Assert.Equal(50, slot.Timer);

        // The drain tick decrements the timer; a negative timer fires one spawn
        // (func_800616A0: unk16 < unk0E gates the timer path).
        spawner.Drain(51);
        Assert.Equal(1, slot.TotalSpawned);                     // fired once, refilled to 50-51+50
        spawner.Drain(60);
        Assert.Equal(1, slot.TotalSpawned);                     // gate closed: 1 < 1 is false

        // The retirement sweep runs func_80061020 for every flagged entity (8004D598.c:51-53),
        // as the scene wires it: OnSpawnedUnitRetired -> RecordSpawnedUnitRetirement.
        var world = new EntityWorld
        {
            OnSpawnedUnitRetired = e => spawner.RecordSpawnedUnitRetirement(e),
        };
        var unit = world.Spawn(new ProbeEntity(42), targetable: true);
        LiveEntity.Retire(unit, world);                         // the retire pass clears the id
        Assert.Equal(1, world.SweepRetired());

        // info->unk16 drops by one per matching record id; the drain gate is open again
        // and the next armed timer respawns the destroyed unit.
        Assert.Equal(0, slot.TotalSpawned);
        slot.Timer = -1;                                        // armed, as Drain leaves it
        Assert.Equal(1, spawner.Drain(2));
        Assert.Equal(1, slot.TotalSpawned);
    }

    [Fact]
    public void RecordSpawnedUnitRetirement_LeavesOtherSlotsAlone_AndHasNoFloor()
    {
        var spawner = new CsacUnitSpawner();
        var other = spawner.Register(UnitRecord(7),
            new CsacSpawnerParameters(RespawnDelayRaw: -10, MaxConcurrent: 2, InitialFlags: 0, InitialCount: -1));
        var empty = spawner.Register(UnitRecord(42),
            new CsacSpawnerParameters(RespawnDelayRaw: -10, MaxConcurrent: 2, InitialFlags: 0, InitialCount: -1));
        Assert.NotNull(other);
        Assert.NotNull(empty);
        Assert.NotNull(spawner.TrySpawn(other!, spawnMode: 1));
        Assert.Equal(1, other!.TotalSpawned);

        spawner.RecordSpawnedUnitRetirement(new ProbeEntity(42));

        Assert.Equal(1, other.TotalSpawned);                   // record 7 untouched
        Assert.Equal(-1, empty!.TotalSpawned);                 // no floor at zero, like the C
    }

    // ---- Item 2: func_8003B898 / func_8003BAE4 in the registry -----------------------------

    /// <summary>One entry whose five slot groups carry distinct bytes per mode (func_8003B5AC).</summary>
    private static SlotSpawnDescriptor Desc(ushort slotFlags = 0, byte overrideColumn = 0) => new(
        ColumnKind: 0x55, SpawnColumn: 0x66, SlotFlags: slotFlags, OverrideColumn: overrideColumn,
        Items: new[]
        {
            new SlotSpawnItem(0x11, 0x62, 0x63, 0x64),
            new SlotSpawnItem(0x21, 0x72, 0x73, 0x74),
            new SlotSpawnItem(0x31, 0x82, 0x83, 0x84),
            new SlotSpawnItem(0x41, 0x92, 0x93, 0x94),
            new SlotSpawnItem(0x51, 0xA2, 0xA3, 0xA4),
        });

    /// <summary>Registry whose spawn gate accepts every column and records each delivery.</summary>
    private static (MapObjectRegistry Registry, List<byte> Columns) RecordingRegistry(
        Func<int>? random = null)
    {
        var columns = new List<byte>();
        var registry = new MapObjectRegistry
        {
            SpawnFactory = (_, column, _, _, _, _) => { columns.Add(column); return 1; },
            RandomSource = random,
        };
        registry.InitMissionSpawnState();
        return (registry, columns);
    }

    private static void EnableColumn(MapObjectRegistry registry, byte column) =>
        registry.SetSpawnCell(0, column, 1);

    [Fact]
    public void SelectEntitySlotType_ReadsUnk0_AndAppliesTheRandomOverrideOnSlot0Only()
    {
        var (registry, _) = RecordingRegistry();
        registry.SetSlotDescriptor(4, Desc(slotFlags: 0, overrideColumn: 0x77));

        // Byte +0x04 + (slot << 2): the unk0 of slot 2 is 0x31.
        Assert.Equal(0x31, registry.SelectEntitySlotType(4, 2, mode: true));

        // Random override: flags bit 0 clear, slot 0, non-zero mode, RNG bit 0 set -> +0x1A.
        List<byte> rolled = new();
        registry.RandomSource = () => 1;
        rolled.Add(registry.SelectEntitySlotType(4, 0, mode: true));
        registry.RandomSource = () => 0;
        rolled.Add(registry.SelectEntitySlotType(4, 0, mode: true));
        Assert.Equal(new byte[] { 0x77, 0x11 }, rolled);

        // Slot 0 with cleared RNG bit keeps unk0; mode 0 never rolls.
        registry.RandomSource = () => 1;
        Assert.Equal(0x11, registry.SelectEntitySlotType(4, 0, mode: false));
    }

    [Fact]
    public void GetSlotSpawnByte_DispatchesPerMode_Unk3_Unk2_Unk1_AndZero()
    {
        var (registry, _) = RecordingRegistry();
        registry.SetSlotDescriptor(4, Desc());

        Assert.Equal(0x84, registry.GetSlotSpawnByte(4, 2, 0));    // mode 0 -> unk3
        Assert.Equal(0x82, registry.GetSlotSpawnByte(4, 2, 2));    // mode 2 -> unk1
        Assert.Equal(0x83, registry.GetSlotSpawnByte(4, 2, 1));    // mode 1 / else -> unk2
        Assert.Equal(0x83, registry.GetSlotSpawnByte(4, 2, 9));    // every other mode -> unk2
        Assert.Equal(0, registry.GetSlotSpawnByte(4, 2, 3));       // mode 3 -> 0
        Assert.Equal(0, registry.GetSlotSpawnByte(200, 2, 1));      // null descriptor -> 0
    }

    [Fact]
    public void RetireFootprint_Mode2_SpawnsEachMaskedColumn()
    {
        var (registry, columns) = RecordingRegistry(random: () => 0);
        registry.SetSlotDescriptor(4, Desc());
        EnableColumn(registry, 0x21);
        EnableColumn(registry, 0x41);

        // 8002A810.c:128-133: mask 0x1E = bits 1..4 -> items[] Unk0 (mode 2 selects the special
        // variant of func_8003B4E0, which still reads unk0; with the RNG at 0 nothing overrides).
        var entity = new ProbeEntity(9, type: 5, slotIndex: 4);
        int delivered = registry.SpawnEntitySlotTypes(entity, flags: 0x1E, mode: 2);

        Assert.Equal(2, delivered);
        Assert.Equal(new byte[] { 0x21, 0x41 }, columns);
    }

    [Fact]
    public void RetireFootprint_Flags0x40000_UsesTheEntityOwnColumn()
    {
        var (registry, columns) = RecordingRegistry();
        EnableColumn(registry, 0x66);
        registry.SetSlotDescriptor(4, Desc());

        // func_8003B898: a self-positioned entity (flag 0x40000) writes its own byte +0x20.
        var entity = new MapOccupancySubject
        {
            Id = 9, EntityType = 7, TableIndex = 0x66,
            Flags = MapOccupancySubject.SelfPositionedFlag,
        };
        int delivered = registry.SpawnEntitySlotTypes(4, entity, arg2: 0, flags: 0x1E, mode: 2);

        Assert.Equal(1, delivered);
        Assert.Equal(new byte[] { 0x66 }, columns);
    }

    [Fact]
    public void RetireFootprint_NoMaskBitHit_FallsBackToSlot0()
    {
        var (registry, columns) = RecordingRegistry(random: () => 0);
        registry.SetSlotDescriptor(4, Desc());
        EnableColumn(registry, 0x11);

        var entity = new ProbeEntity(9, type: 5, slotIndex: 4);
        int delivered = registry.SpawnEntitySlotTypes(entity, flags: 0xE0, mode: 2);

        Assert.Equal(1, delivered);
        Assert.Equal(new byte[] { 0x11 }, columns);
    }

    [Fact]
    public void RefreshSlotSpawns_DamagedOutpost_SpawnsItsQuartileColumn()
    {
        var (registry, columns) = RecordingRegistry();
        registry.SetSlotDescriptor(4, Desc());
        EnableColumn(registry, 0x72);

        // func_8003BAE4: the health quartile selects the mode of func_8003B5AC; half health is
        // quartile 2, which reads unk2 (0x72).
        var entity = new ProbeEntity(9, type: 5, slotIndex: 4)
        {
            Health = 250,
            MaxHealth = 500,
        };
        int delivered = registry.RefreshEntitySlotSpawns(entity, flags: 0x2);

        Assert.Equal(1, delivered);
        Assert.Equal(new byte[] { 0x72 }, columns);
    }

    [Fact]
    public void RefreshSlotSpawns_FullHealthRegistersNothing_Slot0FallsBackOnce()
    {
        var (registry, columns) = RecordingRegistry(random: () => 0);
        registry.SetSlotDescriptor(4, Desc());
        EnableColumn(registry, 0x64);

        var full = new ProbeEntity(9, type: 5, slotIndex: 4) { Health = 500, MaxHealth = 500 };
        Assert.Equal(0, registry.RefreshEntitySlotSpawns(full, flags: 0x2));   // quartile 3 -> return

        // Quartile 0: mode 0 -> unk3 of slot 0 falls back only when no mask bit produced a column.
        var fresh = new ProbeEntity(9, type: 5, slotIndex: 4) { Health = 0, MaxHealth = 500 };
        int delivered = registry.RefreshEntitySlotSpawns(fresh, flags: 0x0);
        Assert.Equal(1, delivered);
        Assert.Equal(new byte[] { 0x64 }, columns);
        Assert.Equal(0, registry.RefreshEntitySlotSpawns(full, flags: 0x2));   // unchanged for full health
    }

    // ---- Item 3: beam message 14 (func_8006B23C do_cb, 8005F018.c:407) -------------------

    private static string? FindMission()
    {
        var disc = Environment.GetEnvironmentVariable("FC_RETAIL_DISC");
        if (disc is not null && File.Exists(Path.Combine(disc, "PA", "CONFT.MIS")))
            return Path.Combine(disc, "PA", "CONFT.MIS");
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, "build", "disc", "files", "PA", "CONFT.MIS");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>The CONFT beam is weapon 0x00, both halves action 0x0A, kind 1 (def + 0x1F).</summary>
    [Fact]
    public void Beam_DirectHitDeliversMessage14_WithTheDefinitionDamageAndKind()
    {
        string? mis = FindMission();
        if (mis is null)
        {
            Assert.DoesNotContain('1', Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL") ?? "0");
            return;                                             // no retail data in this checkout
        }

        var file = new MisFile(mis);
        var weapons = new LevelWeapons(file);
        var record = weapons.RecordOf(WeaponSlots.PrimarySlot)!;   // boot loadout 0x00 = the beam
        Assert.Equal(0x0A, record.HalfA.Action);
        var beamDef = record.HalfB!;                              // rumbler: damage 512 (def + 0x0C)
        Assert.Equal(0x0A, beamDef.Action);
        Assert.Equal(512, beamDef.DamageWord);

        var world = new EntityWorld();
        var target = new HitEntity(3, new CollisionCoord(0x1000, 0x100, 0, 0x800, 0x100, 0x400));
        world.Spawn(target);
        world.CullActive(0x800, 0);

        weapons.BeamHitWorld = world;
        weapons.BeamSourceWord = 0x8021;
        var beam = new Beam(beamDef, 0, 0x100, 0, new[] { 0x100, 0, 0 }, 0x20000);
        weapons.Beams.Add(beam);

        // func_8006B23C:196-201 (do_cb): the callback delivers message 14 with the record
        // 8005F018.c:407 builds - source self->unk14, damage s3->unk0C, kind e->unk1F.
        int hits = weapons.DeliverBeamHits(world);

        Assert.Equal(1, hits);
        var payload = Assert.Single(target.Hits);
        Assert.Equal(0x8021u, payload.SourceWord);             // cb.unk00 = self->unk14
        Assert.Equal((short)512, payload.Damage);               // cb.unk04 = s3->unk0C (def + 0x0C)
        Assert.Equal((byte)1, payload.Kind);                    // cb.unk06 = e->unk1F (def + 0x1F)
        Assert.Null(payload.Attacker);                          // cb.unk08 = self: no live entity
    }

    [Fact]
    public void Beam_DirectHitSkipsEntitiesBehindAMapHit()
    {
        string? mis = FindMission();
        if (mis is null)
        {
            Assert.DoesNotContain('1', Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL") ?? "0");
            return;
        }

        var weapons = new LevelWeapons(new MisFile(mis));
        var beamDef = weapons.RecordOf(WeaponSlots.PrimarySlot)!.HalfB!;

        var world = new EntityWorld();
        var behind = new HitEntity(4, new CollisionCoord(0x8000, 0x100, 0, 0x800, 0x100, 0x400));
        world.Spawn(behind);
        world.CullActive(0x800, 0);

        weapons.BeamHitWorld = world;
        weapons.BeamSourceWord = 0x8021;
        // Short beam: the far end stops 0x2000 short of the entity, the segment never reaches it.
        var beam = new Beam(beamDef, 0, 0x100, 0, new[] { 0x100, 0, 0 }, 0x1000);
        weapons.Beams.Add(beam);

        Assert.Equal(0, weapons.DeliverBeamHits(world));
        Assert.Empty(behind.Hits);
    }

    // ---- Item 4: the freed-outpost filter of the strategy scan ---------------------------

    [Fact]
    public void FreedOutpost_DropsOutOfTheSkyCaptainStrategyCandidates()
    {
        string? mis = FindMission();
        if (mis is null)
        {
            Assert.DoesNotContain('1', Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL") ?? "0");
            return;
        }

        var scene = new LevelScene(mis);
        scene.EnableMechMovement();
        scene.EnableFiring();

        var drone = scene.SkyCaptain!;
        var field = scene.Objectives!;
        var outpost = field.Objectives.First(o => o.Id == 113);
        // Before the retirement the outpost is an active candidate with its team.
        var pre = drone.StrategyUnits.First(u => u.Pos[0] == outpost.X && u.Pos[2] == outpost.Z);
        Assert.True(pre.Active);

        // Retire the open outpost with the walker, as LevelSceneRetirementTests does.
        var walker = scene.Walker!;
        walker.X = outpost.X;
        walker.Z = outpost.Z - 0xE000;
        walker.Angle = 0;
        for (int frame = 0; frame < 600 && !outpost.Freed; frame++)
        {
            scene.HeldPadWord = 0x00400000;
            scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        }
        Assert.True(outpost.Freed);

        // The refresh pass of the strategy tick takes the freed objective out of the
        // candidate set (gSweepRetiredEntities, 8004D598.c:66): Active drops, the freed
        // unit stops being a scan target.
        drone.Advance(30, scene);
        var post = drone.StrategyUnits.First(u => u.Pos[0] == outpost.X && u.Pos[2] == outpost.Z);
        Assert.False(post.Active);
    }
}
