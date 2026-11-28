using System.Buffers.Binary;
using System.Globalization;
using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Regression tests for task 23: the live retail <see cref="CsacUnitSpawner"/> path drives
/// type-9 aircraft through <c>gInitHoverEnemy</c>/<c>gUpdateHoverEnemy</c> (80032180.c /
/// 800324E0.c: <c>(dt * fF4) &gt;&gt; 8</c>, vertical ease between <c>f110/f112/f114</c> at
/// <c>+/-0x1800</c>, ground clamp <c>pos.y &lt; gL</c>) and type-5/28 tanks through the retail
/// <c>Cnet</c> path mover (<c>80050E0C.c</c> / <c>8005027C.c</c> / <c>8003802C.c</c>) using
/// speeds derived from each unit's <c>tACT</c> record, while never running simultaneously with
/// the retired <see cref="BuildOrderSpawner"/>.
/// </summary>
public sealed class SkyCaptainAircraftMovementTests
{
    private static EntityRecord MakeFlyerRecord(
        int id, int team, int modelId, int x, int y, int z,
        short speedRaw, ushort turnRate, short lowBand, short midBand, short highBand,
        byte state = 1, byte flags = 0, short spreadX = 0x2000, short spreadZ = 0x2000)
    {
        var s = new byte[0x54];
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x00), (ushort)id);
        s[0x04] = BuildOrderSpawner.AircraftEntityType;
        BinaryPrimitives.WriteInt32LittleEndian(s.AsSpan(0x08), x);
        BinaryPrimitives.WriteInt32LittleEndian(s.AsSpan(0x0C), y);
        BinaryPrimitives.WriteInt32LittleEndian(s.AsSpan(0x10), z);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x18), 0x100);
        s[0x1C] = (byte)team;
        s[0x38] = state;
        s[0x39] = flags;
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x3C), 0x200);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x3E), lowBand);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x40), midBand);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x42), highBand);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x44), turnRate);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x46), speedRaw);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x48), spreadZ);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x4A), spreadX);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x4C), 5120);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x4E), 1280);
        return new EntityRecord(id, BuildOrderSpawner.AircraftEntityType, x, y, z, 0, 0x100, team,
            EntityContainer.Csac, 0, 0, s, new[] { 0, 0, 0 },
            new[] { new ResourceReference("Cobj", modelId) });
    }

    private static EntityRecord MakeRailRecord(
        int id, int type, int team, int modelId, int cnetId, int x, int y, int z,
        ushort flagsLo, byte modeHi, short speedRaw, ushort headOffset, ushort turnFactor,
        short accelRaw, short decelRaw, ushort unk0C = 0x0666, ushort unk0E = 0x2000, ushort unk10 = 0)
    {
        var s = new byte[0x54];
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x00), (ushort)id);
        s[0x04] = (byte)type;
        BinaryPrimitives.WriteInt32LittleEndian(s.AsSpan(0x08), x);
        BinaryPrimitives.WriteInt32LittleEndian(s.AsSpan(0x0C), y);
        BinaryPrimitives.WriteInt32LittleEndian(s.AsSpan(0x10), z);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x18), 0x100);
        s[0x1C] = (byte)team;
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x34), flagsLo);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x36), speedRaw);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x38), headOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x3A), turnFactor);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x3C), accelRaw);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x3E), decelRaw);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x40), unk0C);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x42), unk0E);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x44), unk10);
        s[0x46] = modeHi;
        return new EntityRecord(id, type, x, y, z, 0, 0x100, team, EntityContainer.Csac, 0, 0, s,
            new[] { 0, 0, 0 },
            new[] { new ResourceReference("Cobj", modelId), new ResourceReference("NULL", 0), new ResourceReference("Cnet", cnetId) });
    }

    private static EntityRecord MakeStaticRecord(int id, int type, int team, int modelId, int x, int y, int z) =>
        new(id, type, x, y, z, 0, 0x100, team, EntityContainer.Csac, 0, 0, new byte[0x54],
            new[] { 0, 0, 0 }, new[] { new ResourceReference("Cobj", modelId) });

    private static int SteppedTerrainHeight(int x, int z)
    {
        if (x < 0x101000) return 0x1000;
        if (x < 0x103000) return 0x14000; // raised wall/ridge
        if (x < 0x105000) return 0x400;   // valley
        return 0x8000;                    // plateau
    }

    [ConftFact]
    public void SkyCaptainBuiltAircraftAndTank_MoveOverNFramesAtRetailRecordRates()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        // Normal live gameplay configuration (matches tools/FcMainMenu/MenuFlow.cs:276-278).
        var scene = new LevelScene(path) { EnableCsacSpawner = true };
        scene.EnableMechMovement();
        scene.EnableFiring();

        var sky = Assert.IsType<SkyCaptainSystem>(scene.SkyCaptain);
        Assert.False(sky.EnableBuildOrderSpawner);

        // Drain one Sky Captain base aircraft order through retail Cfun RPNS timers into Csac 33.
        sky.Economy.FB0 = 0;
        sky.Economy.FB4 = 1;
        sky.Economy.FB8 = 0;
        sky.Economy.FBC = 0;

        for (int i = 0; i < 60 && !scene.CsacSpawner!.SpawnedUnits.Any(u => u.SourceSlot.Record.Id == 33); i++)
            scene.AdvanceSkyCaptain(50);

        var heli = Assert.Single(scene.CsacSpawner!.SpawnedUnits.Where(u => u.SourceSlot.Record.Id == 33));
        Assert.Equal(BuildOrderSpawner.AircraftEntityType, heli.EntityType);

        // Also spawn the faster type-9 heavy aircraft (Csac 6) and a type-5 tank (Csac 75) on CSAC.
        Assert.True(scene.CsacSpawner.ApplyCommandById(6, CsacUnitSpawner.CommandQueueBurst, 1));
        Assert.True(scene.CsacSpawner.ApplyCommandById(75, CsacUnitSpawner.CommandQueueBurst, 1));
        scene.AdvanceSkyCaptain(0);
        var fortress = Assert.Single(scene.CsacSpawner.SpawnedUnits.Where(u => u.SourceSlot.Record.Id == 6));
        var tank = Assert.Single(scene.CsacSpawner.SpawnedUnits.Where(u => u.SourceSlot.Record.Id == 75));
        Assert.Equal(BuildOrderSpawner.AircraftEntityType, fortress.EntityType);
        Assert.Equal(BuildOrderSpawner.TankEntityType, tank.EntityType);

        // Derive retail speeds directly from the CONFT.MIS tACT records (no invented constants):
        // Type 9 (80032180.c:232): fF4 = ((s16)src[+0x46] << 12) / 300; step = (dt * fF4) >> 8 (800324E0.c:358).
        // Type 5 (80050E0C.c:314): f30 = (((s16)src[+0x36] << 8) / 300) >> 4; rem = v2 * dt (8005027C.c:182).
        int heliF4 = ((short)heli.SourceSlot.Record.U16(0x46) << 12) / 300;
        int fortressF4 = ((short)fortress.SourceSlot.Record.U16(0x46) << 12) / 300;
        int tankBaseSpeed = (((short)tank.SourceSlot.Record.U16(0x36) << 8) / 300) >> 4;
        Assert.Equal(27962, heliF4);       // src+0x46 = 2048 -> 0x6D3A
        Assert.Equal(33546, fortressF4);   // src+0x46 = 2457 -> 0x830A
        Assert.Equal(54, tankBaseSpeed);   // src+0x36 = 1024 -> 0x36

        const int frames = 12;
        int dt = RetailClock.LocomotionTicks; // 8 ticks per step (MenuFlow.cs:446)
        int heliStepCap = (dt * heliF4) >> 8;         // 873 (0x369)
        int fortressStepCap = (dt * fortressF4) >> 8; // 1048 (0x418)
        int tankStep = tankBaseSpeed * dt;            // 432 (0x1B0)

        double heliPath = 0;
        double fortressPath = 0;
        double tankPath = 0;

        for (int f = 0; f < frames; f++)
        {
            int hx0 = heli.X, hy0 = heli.Y, hz0 = heli.Z;
            int fx0 = fortress.X, fy0 = fortress.Y, fz0 = fortress.Z;
            int tx0 = tank.X, ty0 = tank.Y, tz0 = tank.Z;

            scene.AdvanceSkyCaptain(dt);

            double hStep = Math.Sqrt(
                Math.Pow(heli.X - hx0, 2) + Math.Pow(heli.Y - hy0, 2) + Math.Pow(heli.Z - hz0, 2));
            double fStep = Math.Sqrt(
                Math.Pow(fortress.X - fx0, 2) + Math.Pow(fortress.Y - fy0, 2) + Math.Pow(fortress.Z - fz0, 2));
            double tStep = Math.Sqrt(
                Math.Pow(tank.X - tx0, 2) + Math.Pow(tank.Y - ty0, 2) + Math.Pow(tank.Z - tz0, 2));

            // Retail banking scaling (800324E0.c:359-360) keeps each 3D step in (0, (dt*fF4)>>8],
            // never at the retired BuildOrderSpawner 0x700*8 = 14336 rate.
            Assert.InRange(hStep, heliStepCap * 0.5, heliStepCap + 4);
            Assert.InRange(fStep, fortressStepCap * 0.5, fortressStepCap + 4);
            // Retail rail segment 0->1 on CONFT Cnet 1 is straight, so step matches tankBaseSpeed * dt (432),
            // never the retired BuildOrderSpawner 0x400*8 = 8192 rate.
            Assert.InRange(tStep, tankStep - 8, tankStep + 8);

            heliPath += hStep;
            fortressPath += fStep;
            tankPath += tStep;
        }

        Assert.True(fortressPath > heliPath,
            $"record-faster fortress ({fortressPath:F1}) must outpace helicopter ({heliPath:F1})");
        Assert.True(heliPath > tankPath,
            $"aircraft ({heliPath:F1}) must move faster than tank ({tankPath:F1})");

        // Collision coordinates stay synchronized with the moved unit positions.
        Assert.NotNull(heli.Coord);
        Assert.Equal(heli.X, heli.Coord!.Value.X);
        Assert.Equal(heli.Y, heli.Coord!.Value.Y);
        Assert.Equal(heli.Z, heli.Coord!.Value.Z);

        // Rendering at the aircraft's moved position still draws its model.
        scene.Walker!.X = heli.X;
        scene.Walker.Y = heli.Y;
        scene.Walker.Z = heli.Z;
        scene.AttachWalker(scene.Walker);
        scene.Render(new LevelCamera(0.0, 20.0, 0x1200));
        Assert.True(scene.SpawnedCsacUnitPrimitives > 0, "moved CSAC aircraft produced no rendered faces");
    }

    [ConftFact]
    public void CsacAircraftAndTank_TrackRetailAltitudeBandAndPathHeightsOnConft()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        var scene = new LevelScene(path) { EnableCsacSpawner = true };
        scene.EnableMechMovement();
        scene.EnableFiring();

        Assert.True(scene.CsacSpawner!.ApplyCommandById(33, CsacUnitSpawner.CommandQueueBurst, 1));
        Assert.True(scene.CsacSpawner.ApplyCommandById(75, CsacUnitSpawner.CommandQueueBurst, 1));
        scene.AdvanceSkyCaptain(0);

        var aircraft = Assert.Single(scene.CsacSpawner.SpawnedUnits.Where(u => u.SourceSlot.Record.Id == 33));
        var tank = Assert.Single(scene.CsacSpawner.SpawnedUnits.Where(u => u.SourceSlot.Record.Id == 75));

        // Csac 33 has fb = 0x02 (bit 1 -> +0x20000 spawn altitude) and f114 = 0x399 (0x3990)
        // per 80032180.c:242,252-253 — not the non-retail BuiltUnit.AircraftAltitude (0x18000).
        int spawnGround = LevelGround.Height(scene.Map, aircraft.X, CsacUnitSpawner.GroundProbeY, aircraft.Z, out _);
        int f114 = (short)aircraft.SourceSlot.Record.U16(0x40);
        Assert.Equal(spawnGround + (f114 << 4) + 0x20000, aircraft.Y);

        // Csac 75 has headOffset = 0x133 at src+0x38 (80050E0C.c:303-312).
        int tankHeadOffset = (short)tank.SourceSlot.Record.U16(0x38) << 4;
        Assert.Equal(0x1330, tankHeadOffset);
        int startY = aircraft.Y;

        int dt = RetailClock.LocomotionTicks;
        for (int f = 0; f < 20; f++)
        {
            scene.AdvanceSkyCaptain(dt);

            int aircraftGround = LevelGround.Height(scene.Map, aircraft.X, CsacUnitSpawner.GroundProbeY, aircraft.Z, out _);
            Assert.True(aircraft.Y >= aircraftGround, "aircraft sank below terrain (800324E0.c:368-369)");

            // CONFT Cnet 1 node[0] starts inside the covered base bay (type 1 -> mode -2 floor surface),
            // so probe from tank.Y as retail gGetGroundHeight(&pos, 0, 0, 0) does (8003802C.c:102).
            int tankGround = LevelGround.Height(scene.Map, tank.X, tank.Y, tank.Z, out _);
            Assert.True(tank.Y >= tankGround, "tank sank below floor surface");
        }

        // Above the upper band edge (f112 = 0x400), retail Steer applies -0x1800 vertical ease
        // (800324E0.c:336-337), so the aircraft descends smoothly toward its cruise band.
        Assert.True(aircraft.Y < startY, $"aircraft spawned at +0x20000 must ease down (start={startY:#x}, now={aircraft.Y:#x})");
    }

    [Fact]
    public void CsacFlyer_EasesAcrossRetailAltitudeBandAndClampsToGroundOnSteppedTerrain()
    {
        var registry = new MapObjectRegistry
        {
            GroundHeight = pos => SteppedTerrainHeight(pos.X, pos.Z),
        };
        registry.InitMissionSpawnState();
        registry.LiveObjects.Allocate();

        var spawner = new CsacUnitSpawner(registry, new RetailRandom(0x457));
        var burstOnly = new CsacSpawnerParameters(RespawnDelayRaw: -10, MaxConcurrent: 4, InitialFlags: 1, InitialCount: 4);

        // Type-9 flyer with CONFT Csac 33 band/rates (f110=0x333, f114=0x399, f112=0x400, fF8=0x1400, src46=2048 -> fF4=27962).
        spawner.Register(MakeFlyerRecord(1, team: 2, modelId: 22, 0x100000, 0, 0x200000,
            speedRaw: 2048, turnRate: 0x1400, lowBand: 0x333, midBand: 0x399, highBand: 0x400,
            state: 1, flags: 0), burstOnly);
        // Stationary base-defender turret (type 8) must remain fixed.
        spawner.Register(MakeStaticRecord(4, 0x08, team: 2, modelId: 40, 0x100000, 0x500, 0x200000), burstOnly);

        Assert.True(spawner.ApplyCommandById(1, CsacUnitSpawner.CommandQueueBurst, 1));
        Assert.True(spawner.ApplyCommandById(4, CsacUnitSpawner.CommandQueueBurst, 1));
        Assert.Equal(2, spawner.Drain(0));

        var aircraft = spawner.SpawnedUnits.Single(u => u.SourceSlot.Record.Id == 1);
        var turret = spawner.SpawnedUnits.Single(u => u.SourceSlot.Record.Id == 4);

        // Initial spawn height is ground + (f114 << 4) (80032180.c:241-242).
        Assert.Equal(SteppedTerrainHeight(0x100000, 0x200000) + (0x399 << 4), aircraft.Y);

        const int dt = 8;
        int maxStep = (dt * 27962) >> 8; // 873
        for (int f = 0; f < 25; f++)
        {
            int x0 = aircraft.X, y0 = aircraft.Y, z0 = aircraft.Z;
            int gBefore = SteppedTerrainHeight(x0, z0);
            registry.BroadcastMessage6(dt);

            double step3d = Math.Sqrt(
                Math.Pow(aircraft.X - x0, 2) + Math.Pow(aircraft.Y - y0, 2) + Math.Pow(aircraft.Z - z0, 2));
            Assert.True(step3d > 0, "flyer must move every tick");
            Assert.True(step3d <= maxStep + 0x14000, "step bounded by (dt*fF4)>>8 (plus any ground clamp jump)");
            // 800324E0.c:368-369: pos.y is clamped against gL sampled at start of tick.
            Assert.True(aircraft.Y >= gBefore, "flyer sank below tick ground level gL");
            Assert.Equal((0x100000, 0x500, 0x200000), (turret.X, turret.Y, turret.Z));
        }
    }

    [ConftFact]
    public void OnlyOneSpawnerPathMovesUnits_CsacAndBuildOrderSpawnerNeverBothMove()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        var scene = new LevelScene(path);
        scene.EnableMechMovement();
        scene.EnableFiring();

        var sky = Assert.IsType<SkyCaptainSystem>(scene.SkyCaptain);
        Assert.False(scene.EnableCsacSpawner);
        Assert.False(sky.EnableBuildOrderSpawner);

        // Seed one BuildOrderSpawner unit while CSAC is off.
        sky.EnableBuildOrderSpawner = true;
        sky.Economy.FB4 += 1;
        scene.AdvanceSkyCaptain(1);
        var builtUnit = Assert.Single(sky.BuiltUnits);

        // Enable CSAC while leaving EnableBuildOrderSpawner = true: only CSAC may spawn and move.
        scene.EnableCsacSpawner = true;
        Assert.True(scene.CsacSpawner!.ApplyCommandById(33, CsacUnitSpawner.CommandQueueBurst, 1));
        scene.AdvanceSkyCaptain(0);
        var csacUnit = Assert.Single(scene.CsacSpawner.SpawnedUnits.Where(u => u.SourceSlot.Record.Id == 33));

        int builtCountBefore = sky.BuiltUnits.Count;
        int bx0 = builtUnit.X, bz0 = builtUnit.Z;
        int cx0 = csacUnit.X, cz0 = csacUnit.Z;
        sky.Economy.FB4 += 1;

        for (int i = 0; i < 5; i++)
            scene.AdvanceSkyCaptain(RetailClock.LocomotionTicks);

        Assert.True(csacUnit.X != cx0 || csacUnit.Z != cz0, "CSAC unit must move when EnableCsacSpawner is true");
        Assert.Equal((bx0, bz0), (builtUnit.X, builtUnit.Z));
        Assert.Equal(builtCountBefore, sky.BuiltUnits.Count);

        // Turn CSAC off again while BuildOrderSpawner remains opted in: only BuildOrderSpawner moves.
        scene.EnableCsacSpawner = false;
        int bx1 = builtUnit.X, bz1 = builtUnit.Z;
        int cx1 = csacUnit.X, cz1 = csacUnit.Z;

        for (int i = 0; i < 5; i++)
            scene.AdvanceSkyCaptain(RetailClock.LocomotionTicks);

        Assert.True(builtUnit.X != bx1 || builtUnit.Z != bz1, "BuildOrderSpawner unit must move when CSAC is off");
        Assert.Equal((cx1, cz1), (csacUnit.X, csacUnit.Z));
    }

    [Fact]
    public void RetailUnitMovers_MatchUnchangedMipsGoldenVectors()
    {
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "golden", "unit-movers.vectors");
        var expected = File.ReadAllLines(goldenPath)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToDictionary(
                l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0],
                l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1..]
                    .Select(v => int.Parse(v, CultureInfo.InvariantCulture))
                    .ToArray());

        // 1. Type-9 flyer (CONFT Csac 33: src+0x46=2048 -> fF4=27962, fF8=5120, f110=0x333, f114=0x399, f112=0x400, fb=2).
        {
            int ground = 0x2000;
            var registry = new MapObjectRegistry { GroundHeight = _ => ground };
            registry.InitMissionSpawnState();
            registry.LiveObjects.Allocate();
            var spawner = new CsacUnitSpawner(registry, new RetailRandom(0x457));
            var rec = MakeFlyerRecord(33, team: 2, modelId: 22, 0x137400, 0, 0xD0200,
                speedRaw: 2048, turnRate: 0x1400, lowBand: 0x333, midBand: 0x399, highBand: 0x400,
                state: 1, flags: 0x02, spreadX: 0x5000, spreadZ: 0x6000);
            var slot = spawner.Register(rec, new CsacSpawnerParameters(-10, 4, 1, 4))!;
            var unit = spawner.TrySpawn(slot, 1)!;
            var h = Assert.IsType<HoverEnemy>(unit.HoverMover);

            Assert.Equal(expected["flyer_csac33_init"], new[]
            {
                h.F4, (int)h.F8, h.F118, h.F11C,
                h.Pos[0], h.Pos[1], h.Pos[2],
                h.Dir[0], h.Dir[1], h.Dir[2],
            });

            for (int t = 1; t <= 6; t++)
            {
                registry.BroadcastMessage6(8);
                if (t == 1 || t == 6)
                {
                    string key = t == 1 ? "flyer_csac33_t1" : "flyer_csac33_t6";
                    Assert.Equal(expected[key], new[]
                    {
                        unit.X, unit.Y, unit.Z,
                        h.Dir[0], h.Dir[1], h.Dir[2],
                        h.Up38[1],
                    });
                }
            }
        }

        // 2. Type-9 heavy flyer (CONFT Csac 6: src+0x46=2457 -> fF4=33546) + ground clamp (pos.y < gL) & climb (+0x1800).
        {
            int ground = 0x1000;
            var registry = new MapObjectRegistry { GroundHeight = _ => ground };
            registry.InitMissionSpawnState();
            registry.LiveObjects.Allocate();
            var spawner = new CsacUnitSpawner(registry, new RetailRandom(0x457));
            var rec = MakeFlyerRecord(6, team: 2, modelId: 42, 0x127E00, 0, 0xD03FE,
                speedRaw: 2457, turnRate: 0x1400, lowBand: 0x333, midBand: 0x399, highBand: 0x400,
                state: 1, flags: 0x00, spreadX: 0x7000, spreadZ: 0x7800);
            var slot = spawner.Register(rec, new CsacSpawnerParameters(-10, 4, 1, 4))!;
            var unit = spawner.TrySpawn(slot, 1)!;
            var h = Assert.IsType<HoverEnemy>(unit.HoverMover);

            Assert.Equal(expected["flyer_csac6_init"], new[] { h.F4, unit.X, unit.Y, unit.Z });

            registry.BroadcastMessage6(8);
            ground = 0x8000;
            for (int t = 2; t <= 6; t++)
            {
                registry.BroadcastMessage6(8);
                if (t == 2 || t == 6)
                {
                    string key = t == 2 ? "flyer_csac6_clamp_t2" : "flyer_csac6_climb_t6";
                    Assert.Equal(expected[key], new[] { unit.X, unit.Y, unit.Z, h.Dir[1] });
                }
            }
        }

        // 3. Type-5 tank (CONFT Csac 75: baseSpd=54, acc=1047, dec=523, headOff=0x133) over straight + corner segments.
        {
            var registry = new MapObjectRegistry();
            registry.InitMissionSpawnState();
            registry.LiveObjects.Allocate();
            var spawner = new CsacUnitSpawner(registry, new RetailRandom(0x457));
            spawner.PathNetworks[1] = new PathNetwork(1, new[]
            {
                new PathNode { Neighbor0 = 1, X = 0x8D3, Y = 0x10, Z = 0xE18 },
                new PathNode { Neighbor0 = 2, X = 0x8D3, Y = 0x18, Z = 0xD2A },
                new PathNode { X = 0x903, Y = 0x20, Z = 0xCF6 },
            });
            var rec = MakeRailRecord(75, BuildOrderSpawner.TankEntityType, team: 1, modelId: 30, cnetId: 1,
                0x8D300, 0, 0xE1800, flagsLo: 0x0000, modeHi: 0x04, speedRaw: 1024, headOffset: 0x0133,
                turnFactor: 0x1000, accelRaw: 1228, decelRaw: 614, unk0C: 0x0666, unk0E: 0x2000, unk10: 0x0002);
            var slot = spawner.Register(rec, new CsacSpawnerParameters(-10, 4, 1, 4))!;
            var unit = spawner.TrySpawn(slot, 1)!;
            var r = Assert.IsType<RailPathMover>(unit.RailMover);

            Assert.Equal(expected["rail_csac75_init"], new[]
            {
                (int)r.BaseSpeed, (int)r.Accel, (int)r.Decel, (int)r.SegLength,
                unit.X, unit.Y, unit.Z,
                r.Dir[0], r.Dir[1], r.Dir[2],
            });

            for (int t = 1; t <= 152; t++)
            {
                registry.BroadcastMessage6(8);
                if (t is 1 or 12 or 152)
                {
                    string key = t == 1 ? "rail_csac75_t1" : (t == 12 ? "rail_csac75_t12" : "rail_csac75_t152");
                    Assert.Equal(expected[key], new[]
                    {
                        r.CurrentSpeed, r.Progress, (int)r.CurIndex,
                        unit.X, unit.Y, unit.Z,
                    });
                }
            }
        }

        // 4. Type-28 heavy tank (CONFT Csac 57: baseSpd=54, headOff=0xCC).
        {
            var registry = new MapObjectRegistry();
            registry.InitMissionSpawnState();
            registry.LiveObjects.Allocate();
            var spawner = new CsacUnitSpawner(registry, new RetailRandom(0x457));
            spawner.PathNetworks[2] = new PathNetwork(2, new[]
            {
                new PathNode { Neighbor0 = 1, X = 0x1522, Y = 0x10, Z = 0xBEA },
                new PathNode { X = 0x14F8, Y = 0x10, Z = 0xD0C },
            });
            var rec = MakeRailRecord(57, BuildOrderSpawner.HeavyTankEntityType, team: 2, modelId: 37, cnetId: 2,
                0x152200, 0, 0xBEA00, flagsLo: 0x0008, modeHi: 0x00, speedRaw: 1024, headOffset: 0x00CC,
                turnFactor: 0x1000, accelRaw: 1228, decelRaw: 614);
            var slot = spawner.Register(rec, new CsacSpawnerParameters(-10, 4, 1, 4))!;
            var unit = spawner.TrySpawn(slot, 1)!;
            var r = Assert.IsType<RailPathMover>(unit.RailMover);

            for (int t = 0; t < 12; t++)
                registry.BroadcastMessage6(8);

            Assert.Equal(expected["rail_csac57_t12"], new[]
            {
                (int)r.BaseSpeed, (int)r.HeadOffset, r.CurrentSpeed,
                unit.X, unit.Y, unit.Z,
            });
        }
    }
}
