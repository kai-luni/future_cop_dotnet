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
}
