using System.Buffers.Binary;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// View integration of the Sky Captain (type 0x25): spawn from mission records, the message-6 tick
/// through StrategicDroneTick + HoverEnemyTick, visibility and the render marker.
/// </summary>
public class SkyCaptainSceneTests
{
    private const int FlatGround = 0x10000;
    private static int Flat(int[] pos) => FlatGround;

    /// <summary>A tACT source block with the func_80032180 reads (80032180.c:207-232) filled in.</summary>
    private static EntityRecord Record(int type, int x, int y, int z, EntityContainer container = EntityContainer.Cact,
        int team = 2, int health = 0x100)
    {
        var s = new byte[0x54];
        s[4] = (byte)type;
        BinaryPrimitives.WriteInt32LittleEndian(s.AsSpan(0x08), x);
        BinaryPrimitives.WriteInt32LittleEndian(s.AsSpan(0x0C), y);
        BinaryPrimitives.WriteInt32LittleEndian(s.AsSpan(0x10), z);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x18), (ushort)health);
        s[0x1C] = (byte)team;
        s[0x38] = 0;                                                   // kind -> +0xFA
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x3C), 0x10);   // +0x10A
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x3E), -0x10);   // +0x110
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x40), 0x40);   // +0x114
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x42), 0x80);   // +0x112
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x44), 0x3000); // +0xF8
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x46), 4800);    // +0xF4 = (4800 << 12) / 300
        return new EntityRecord(7, type, x, y, z, 0, health, team, container, 0, 0, s,
            Array.Empty<int>(), Array.Empty<ResourceReference>());
    }

    private static SkyCaptainSystem Spawned(int seed = 0x457)
    {
        var sys = new SkyCaptainSystem { Random = new RetailRandom(seed) };
        sys.SpawnFromRecords(new[] { Record(0x25, 0x00400000, 0, 0x00400000) }, (0, 0, 0), Flat);
        return sys;
    }

    [Fact]
    public void SpawnFromRecords_Type0x25_CreatesDrone_AtRecordPosition()
    {
        var sys = new SkyCaptainSystem();
        var records = new[]
        {
            Record(0x24, 1, 2, 3),
            Record(0x25, 0x7FFF0000, 0, 0x7FFF0000, EntityContainer.Csac),
            Record(0x25, 0x01230000, 0x00450000, -0x00670000, team: 3),
        };
        sys.SpawnFromRecords(records, (0x1000, 0x2000, 0x3000), Flat);

        var d = Assert.IsType<StrategicDrone>(sys.Drone);
        Assert.Same(records[2], sys.Record);                          // Cact before Csac
        Assert.Equal(0x25, d.Body.Type);
        Assert.Equal(3, sys.Team);
        Assert.Equal(0x01230000, d.Body.Pos[0]);                      // 8002A5F4.c:159
        Assert.Equal(-0x00670000, d.Body.Pos[2]);
        Assert.Equal(FlatGround + (0x40 << 4), d.Body.Pos[1]);       // 80032180.c:241-242
        Assert.Equal(d.Body.Pos, d.Ai.Pos);                           // 800331CC.c:235
        Assert.Equal(new[] { 0x1000, 0x2000, 0x3000 }, sys.PosA);     // 800331CC.c:236
        Assert.Equal(2, d.Ai.Mode);                                   // 800331CC.c:201
        Assert.Equal(d.Ai.Pos, d.Body.DC);                            // 800330E8.c mode 2
        Assert.Same(d.Ai, d.Drone.Ai);
        // Difficulty 4: +0xF4 += 1 * 1056 (800331CC.c:174), health -= 1 * (h >> 2) (:186).
        Assert.Equal((4800 << 12) / 300 + 1056, d.Body.F4);
        Assert.Equal(sys.BaseF4, d.Body.F4);
        Assert.Equal(0xC0, d.Body.F1E);
        Assert.Equal(0x3000, d.Ai.PromoteTo);
    }

    [Fact]
    public void SpawnFromRecords_NoType0x25_DroneStaysNull()
    {
        var sys = new SkyCaptainSystem();
        sys.SpawnFromRecords(new[] { Record(0x24, 1, 2, 3), Record(0x10, 4, 5, 6) }, (0, 0, 0), Flat);
        Assert.Null(sys.Drone);
        Assert.False(sys.Visible);

        // Only an explicit debug flag spawns without a record.
        var debug = new SkyCaptainSystem { DebugFallbackSpawn = true };
        debug.SpawnFromRecords(Array.Empty<EntityRecord>(), (0x5000, 0, 0x6000), Flat);
        Assert.NotNull(debug.Drone);
        Assert.Equal(0x5000, debug.Drone!.Body.Pos[0]);
    }

    [Fact]
    public void Advance_TicksHoverEnemy_MovesPositionOrSetsState()
    {
        var sys = Spawned();
        var start = (int[])sys.Drone!.Body.Pos.Clone();
        for (int i = 0; i < 30; i++) sys.Advance(RetailClock.LocomotionTicks, Flat);
        var body = sys.Drone.Body;
        Assert.True(body.Pos[0] != start[0] || body.Pos[2] != start[2], "the flyer did not move");
        Assert.True(body.Pos[1] >= FlatGround, "the flyer sank below the ground");
        Assert.True(sys.StrategyCalls > 0, "the strategy timer never fired");

        // Deterministic: the same seed gives the same flight.
        var again = Spawned();
        for (int i = 0; i < 30; i++) again.Advance(RetailClock.LocomotionTicks, Flat);
        Assert.Equal(body.Pos, again.Drone!.Body.Pos);
    }

    [Fact]
    public void Advance_FindsNearestType16Unit_ByBodyPosition()
    {
        var sys = new SkyCaptainSystem { Random = new RetailRandom() };
        sys.SpawnFromRecords(new[]
        {
            Record(0x10, 0x00900000, 0, 0x00400000),
            Record(0x25, 0x00400000, 0, 0x00400000),
            Record(0x10, 0x00500000, 0, 0x00400000),
        }, (0, 0, 0), Flat);
        Assert.Equal(2, sys.Type16Units.Count);
        var nearest = sys.FindNearestType16(out int distance);
        Assert.Equal(0x00500000, nearest!.Pos[0]);
        Assert.Equal(0x00100000, distance);                          // x 0x100000, z 0: x + z - min/2
    }

    [Fact]
    public void Visible_False_WhileState5()
    {
        var sys = Spawned();
        Assert.True(sys.Visible);
        Assert.Equal(3, sys.MarkerSegments().Count);
        sys.Drone!.Body.FA = 5;
        Assert.False(sys.Visible);
        Assert.Empty(sys.MarkerSegments());

        // 800357C0.c:141-144: message 6 does not tick a dead drone.
        var pos = (int[])sys.Drone.Body.Pos.Clone();
        sys.Advance(RetailClock.LocomotionTicks, Flat);
        Assert.Equal(pos, sys.Drone.Body.Pos);
        Assert.Equal(5, sys.Drone.Body.FA);
    }

    [Fact]
    public void MarkerSegments_StartAtBodyAndFollowBasis()
    {
        var sys = Spawned();
        var body = sys.Drone!.Body;
        var segs = sys.MarkerSegments(0x10000);
        for (int k = 0; k < 3; k++)
        {
            Assert.Equal((body.Pos[0], body.Pos[1], body.Pos[2]), segs[k].A);
            Assert.Equal(body.Pos[0] + body.BasisRows[k][0], segs[k].B.X);
            Assert.Equal(body.Pos[2] + body.BasisRows[k][2], segs[k].B.Z);
        }
    }

    [Fact]
    public void ScenePath_StrategyTimerPaysUpkeepAndSetsPursuitTarget()
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc is null ? null : ModulePath.Resolve(disc, "conft.mis");
        if (path is null) Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
        if (path is null) return;

        var scene = new LevelScene(path);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var sky = Assert.IsType<SkyCaptainSystem>(scene.SkyCaptain);
        var drone = Assert.IsType<StrategicDrone>(sky.Drone);
        drone.Ai.TickTimer = 0;
        drone.Ai.Limit = short.MinValue;
        var score = Assert.IsType<ObjectiveField>(scene.Objectives).Score.Score;
        score[1] = 100;
        sky.Economy.F74 = sky.Economy.F78 = 0x14; // block purchases, leaving only the 0x32 upkeep

        scene.AdvanceSkyCaptain(1);

        Assert.Equal(50, score[1]);
        Assert.Equal(50, sky.Economy.F68);
        Assert.Equal(1, sky.Economy.FBC);
        Assert.NotNull(drone.Body.C8);
        Assert.Equal(scene.Actor.Id, drone.Body.C8.Id);
        Assert.Equal(drone.Body.C8.Id, drone.Body.CC);
    }

    /// <summary>Retail-gated: the live CONFT level draws the Sky Captain when the mission has one.</summary>
    [Fact]
    public void Render_EnqueuesDrawItems_WhenVisible()
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc == null ? null : ModulePath.Resolve(disc, "conft.mis");
        if (path == null) return;
        var scene = new LevelScene(path);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var sky = Assert.IsType<SkyCaptainSystem>(scene.SkyCaptain);
        if (sky.Drone is null) return; // mission without a type-0x25 record
        scene.AdvanceSkyCaptain(RetailClock.LocomotionTicks);
        scene.Render(new LevelCamera(30.0, 35.0, 0x1C00));
        Assert.True(sky.Visible);
        Assert.Equal(3, sky.MarkerSegments().Count);
        // On-screen is not guaranteed (the orbit camera follows the walker), so only the count is read.
        Assert.InRange(scene.SkyCaptainPrimitives, 0, int.MaxValue);
    }

    [Fact]
    public void ScenePath_DroneCapturesOutpost_RaisesScore1AndEconomyBalance()
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc is null ? null : ModulePath.Resolve(disc, "conft.mis");
        if (path is null) Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
        if (path is null) return;

        var scene = new LevelScene(path);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var sky = Assert.IsType<SkyCaptainSystem>(scene.SkyCaptain);
        var drone = Assert.IsType<StrategicDrone>(sky.Drone);
        var field = Assert.IsType<ObjectiveField>(scene.Objectives);
        Assert.Equal(sky.Team, field.Score.SinglePlayerTeam);

        var outpost = field.Objectives[0];
        drone.Body.Pos[0] = outpost.X;
        drone.Body.Pos[2] = outpost.Z;
        drone.Ai.TickTimer = 0;
        drone.Ai.Limit = short.MaxValue; // prevent strategy spend on this tick

        scene.AdvanceSkyCaptain(1);

        Assert.Equal(sky.Team, outpost.Team);
        Assert.Equal(1, field.Score.Score[1]);
        Assert.Equal(1, field.Score.SinglePlayerCaptures);
        Assert.Equal(1, sky.Economy.F68);
        Assert.Equal(6, sky.Economy.FE0);
    }

    [Fact]
    public void ScenePath_OrganicCapturesDriveBuildOrdersIntoMovingRenderedUnits()
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc is null ? null : ModulePath.Resolve(disc, "conft.mis");
        if (path is null) Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
        if (path is null) return;

        var scene = new LevelScene(path);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var sky = Assert.IsType<SkyCaptainSystem>(scene.SkyCaptain);
        var drone = Assert.IsType<StrategicDrone>(sky.Drone);
        var field = Assert.IsType<ObjectiveField>(scene.Objectives);

        // Organically earn credits by capturing 6 outposts with the drone (no manual score[1] write).
        drone.Ai.Limit = short.MaxValue;
        for (int i = 0; i < 6; i++)
        {
            var outpost = field.Objectives[i];
            drone.Body.Pos[0] = outpost.X;
            drone.Body.Pos[2] = outpost.Z;
            drone.Ai.TickTimer = 0;
            scene.AdvanceSkyCaptain(1);
        }
        Assert.Equal(6, field.Score.Score[1]);
        Assert.Equal(6, sky.Economy.F68);

        // Ensure mode-2 air production gate is open (F78 < (S4C >> 2) + 4) and fire strategy tick.
        sky.Economy.F78 = 0;
        drone.Body.Pos[0] = drone.Ai.Pos[0];
        drone.Body.Pos[2] = drone.Ai.Pos[2];
        drone.Ai.TickTimer = 0;
        drone.Ai.Limit = short.MinValue;
        scene.AdvanceSkyCaptain(1);

        Assert.NotEmpty(sky.BuiltUnits);
        Assert.True(sky.Economy.F68 < 6, "strategy should spend organically earned credits");
        var unit = sky.BuiltUnits[0];
        Assert.Equal(BuiltUnitKind.Aircraft, unit.Kind);
        Assert.Equal(BuildOrderSpawner.AircraftEntityType, unit.EntityType);
        Assert.True(unit.ModelId > 0);

        int x0 = unit.X, z0 = unit.Z;
        scene.AdvanceSkyCaptain(RetailClock.LocomotionTicks * 4);
        Assert.True(unit.X != x0 || unit.Z != z0, "spawned unit did not move");

        // Place the camera anchor at the spawned unit and verify the scene renders real model faces.
        scene.Walker!.X = unit.X; scene.Walker.Y = unit.Y; scene.Walker.Z = unit.Z;
        scene.AttachWalker(scene.Walker);
        scene.Render(new LevelCamera(0.0, 20.0, 0x1200));
        Assert.True(scene.BuiltUnitPrimitives > 0, "spawned unit produced no rendered faces");
    }

    [Fact]
    public void ScenePath_DefaultClaimOrderAndTankProduction_SpawnsMovingRenderedTanks()
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc is null ? null : ModulePath.Resolve(disc, "conft.mis");
        if (path is null) Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
        if (path is null) return;

        var scene = new LevelScene(path);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var sky = Assert.IsType<SkyCaptainSystem>(scene.SkyCaptain);
        var drone = Assert.IsType<StrategicDrone>(sky.Drone);
        var field = Assert.IsType<ObjectiveField>(scene.Objectives);

        // Organically capture all 32 outposts in CONFT.MIS (32 credits, no manual score[1] write).
        drone.Ai.Limit = short.MaxValue;
        foreach (var outpost in field.Objectives)
        {
            drone.Body.Pos[0] = outpost.X;
            drone.Body.Pos[2] = outpost.Z;
            drone.Ai.TickTimer = 0;
            scene.AdvanceSkyCaptain(1);
        }
        Assert.Equal(32, field.Score.Score[1]);

        // Trigger Mode-0 economy: default claim order {3, 2, 1, 0} claims slot 3 first (cost 30),
        // and Mode-0 base production (F74 < 2 -> FB0) or slot production spawns visible moving tanks.
        // First check base tank production in Mode 0 (F74 = 0, F68 = 32 -> claims slot 3 with 30,
        // leaving 2; or with 10 own type-8s -> stays in Mode 0).
        // Re-spawn sky captain with a deterministic seed that sets C1C = 0 and 10 own type-8s in range.
        var records = MissionEntities.Load(new MisFile(path));
        int seedWithC1c0 = 0;
        for (int s = 1; s < 64; s++)
        {
            var r = new RetailRandom(s);
            _ = r.Next(); _ = r.Next();
            if ((r.Next() & 1) == 0) { seedWithC1c0 = s; break; }
        }
        sky.Random = new RetailRandom(seedWithC1c0);
        var extraTurrets = Enumerable.Range(0, 10)
            .Select(i => Record(0x08, drone.Ai.Pos[0], 0, drone.Ai.Pos[2], team: sky.Team))
            .ToList();
        extraTurrets.AddRange(records);
        sky.SpawnFromRecords(extraTurrets, scene.MechWorld,
            pos => LevelGround.Height(scene.Map, pos[0], pos[1], pos[2], out _), scene.Actor.Id, field.Objectives);
        drone = Assert.IsType<StrategicDrone>(sky.Drone);
        drone.Ai.Mode = 0;
        sky.Economy.F9C = 0;
        sky.Economy.F74 = 0;
        sky.Economy.F78 = 0x14;
        drone.Ai.TickTimer = 0;
        drone.Ai.Limit = short.MinValue;

        scene.AdvanceSkyCaptain(1);

        // DefaultClaimOrder {3, 2, 1, 0} claims slot 3 (not slot 0).
        Assert.Equal(sky.Team, sky.Economy.A0[3]);
        Assert.Equal(0, sky.Economy.A0[0]);
        Assert.Equal(2, field.Score.Score[1]);

        // Now verify tank spawning from Mode-0 economy (FB0) with organic credits: run a fresh scene
        // where 15 organic captures fund Mode-0 tank production (F74 < 2, S4C = 4 -> n >= 10).
        var tankScene = new LevelScene(path);
        tankScene.EnableMechMovement();
        tankScene.EnableFiring();
        var tankSky = Assert.IsType<SkyCaptainSystem>(tankScene.SkyCaptain);
        var tankDrone = Assert.IsType<StrategicDrone>(tankSky.Drone);
        var tankField = Assert.IsType<ObjectiveField>(tankScene.Objectives);
        tankDrone.Ai.Limit = short.MaxValue;
        for (int i = 0; i < 15; i++)
        {
            var o = tankField.Objectives[i];
            tankDrone.Body.Pos[0] = o.X;
            tankDrone.Body.Pos[2] = o.Z;
            tankDrone.Ai.TickTimer = 0;
            tankScene.AdvanceSkyCaptain(1);
        }
        Assert.Equal(15, tankField.Score.Score[1]);
        tankSky.Random = new RetailRandom(seedWithC1c0);
        tankSky.SpawnFromRecords(extraTurrets, tankScene.MechWorld,
            pos => LevelGround.Height(tankScene.Map, pos[0], pos[1], pos[2], out _), tankScene.Actor.Id, tankField.Objectives);
        tankDrone = Assert.IsType<StrategicDrone>(tankSky.Drone);
        tankDrone.Ai.Mode = 0;
        tankSky.Economy.F9C = 0;
        tankSky.Economy.F74 = 0;
        tankSky.Economy.F78 = 0x14;
        tankDrone.Ai.TickTimer = 0;
        tankDrone.Ai.Limit = short.MinValue;

        tankScene.AdvanceSkyCaptain(1);

        var tank = Assert.Single(tankSky.BuiltUnits.Where(u => u.Kind == BuiltUnitKind.Tank).Take(1));
        Assert.Equal(BuildOrderSpawner.TankEntityType, tank.EntityType);
        Assert.Equal(30, tank.ModelId);
        int tx0 = tank.X, tz0 = tank.Z;
        tankScene.AdvanceSkyCaptain(RetailClock.LocomotionTicks * 4);
        Assert.True(tank.X != tx0 || tank.Z != tz0, "spawned tank did not move");
        tankScene.Walker!.X = tank.X; tankScene.Walker.Y = tank.Y; tankScene.Walker.Z = tank.Z;
        tankScene.AttachWalker(tankScene.Walker);
        tankScene.Render(new LevelCamera(0.0, 20.0, 0x1200));
        Assert.True(tankScene.BuiltUnitPrimitives > 0, "spawned tank produced no rendered faces");
    }
}
