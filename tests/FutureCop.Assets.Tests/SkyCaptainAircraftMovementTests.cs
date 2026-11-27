using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Regression tests for task 23: the live retail <see cref="CsacUnitSpawner"/> path moves spawned
/// aircraft at <see cref="BuiltUnit.AircraftSpeed"/> and tanks at <see cref="BuiltUnit.TankSpeed"/>,
/// tracks terrain height without sinking or clipping walls, and never runs simultaneously with the
/// retired <see cref="BuildOrderSpawner"/>.
/// </summary>
public sealed class SkyCaptainAircraftMovementTests
{
    private static EntityRecord MakeCsacRecord(int id, int type, int team, int modelId, int x, int y, int z) =>
        new(id, type, x, y, z, 0, 0x100, team, EntityContainer.Csac, 0, 0, new byte[0x54],
            new[] { 0, 0, 0 }, new[] { new ResourceReference("Cobj", modelId) });

    private static int SteppedTerrainHeight(int x, int z)
    {
        if (x < 0x102000) return 0x1000;
        if (x < 0x105000) return 0x28000; // raised wall/ridge
        if (x < 0x108000) return 0x400;   // valley
        return 0x12000;                   // plateau
    }

    [ConftFact]
    public void SkyCaptainBuiltAircraftAndTank_MoveOverNFramesAtAircraftSpeedAndTankSpeed()
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

        var aircraft = Assert.Single(scene.CsacSpawner!.SpawnedUnits.Where(u => u.SourceSlot.Record.Id == 33));
        Assert.Equal(BuildOrderSpawner.AircraftEntityType, aircraft.EntityType);

        // Also spawn a retail tank (Csac 75, type 5) on the same live CSAC path.
        Assert.True(scene.CsacSpawner.ApplyCommandById(75, CsacUnitSpawner.CommandQueueBurst, 1));
        scene.AdvanceSkyCaptain(0);
        var tank = Assert.Single(scene.CsacSpawner.SpawnedUnits.Where(u => u.SourceSlot.Record.Id == 75));
        Assert.Equal(BuildOrderSpawner.TankEntityType, tank.EntityType);

        const int frames = 12;
        int dt = RetailClock.LocomotionTicks; // 8 ticks per step (MenuFlow.cs:446)

        int ax0 = aircraft.X, az0 = aircraft.Z;
        int tx0 = tank.X, tz0 = tank.Z;

        // Facing is a normalized 16.16 horizontal heading pointing toward the opposing base.
        double aircraftDirLen = double.Hypot(aircraft.DirX, aircraft.DirZ);
        double tankDirLen = double.Hypot(tank.DirX, tank.DirZ);
        Assert.InRange(aircraftDirLen, 0x10000 - 2, 0x10000 + 2);
        Assert.InRange(tankDirLen, 0x10000 - 2, 0x10000 + 2);
        // Team-2 aircraft (Sky Captain) and team-1 tank (player) face opposing directions.
        Assert.True((long)aircraft.DirX * tank.DirX + (long)aircraft.DirZ * tank.DirZ < 0,
            "team-2 aircraft and team-1 tank must face toward opposing bases");

        for (int f = 0; f < frames; f++)
            scene.AdvanceSkyCaptain(dt);

        double aircraftDist = double.Hypot((double)aircraft.X - ax0, (double)aircraft.Z - az0);
        double tankDist = double.Hypot((double)tank.X - tx0, (double)tank.Z - tz0);

        // Verify exact speeds against both the constants and their retail literal values so
        // swapping AircraftSpeed and TankSpeed makes this test fail.
        Assert.Equal(0x700, BuiltUnit.AircraftSpeed);
        Assert.Equal(0x400, BuiltUnit.TankSpeed);

        double expectedAircraftDist = (double)0x700 * dt * frames;
        double expectedTankDist = (double)0x400 * dt * frames;

        Assert.InRange(aircraftDist, expectedAircraftDist - frames * 2, expectedAircraftDist + frames * 2);
        Assert.InRange(tankDist, expectedTankDist - frames * 2, expectedTankDist + frames * 2);
        Assert.True(aircraftDist > tankDist,
            $"aircraft ({aircraftDist:F1}) must move faster than tank ({tankDist:F1})");

        // Collision coordinates stay synchronized with the moved unit positions.
        Assert.NotNull(aircraft.Coord);
        Assert.Equal(aircraft.X, aircraft.Coord!.Value.X);
        Assert.Equal(aircraft.Y, aircraft.Coord!.Value.Y);
        Assert.Equal(aircraft.Z, aircraft.Coord!.Value.Z);

        // Rendering at the aircraft's moved position still draws its model.
        scene.Walker!.X = aircraft.X;
        scene.Walker.Y = aircraft.Y;
        scene.Walker.Z = aircraft.Z;
        scene.AttachWalker(scene.Walker);
        scene.Render(new LevelCamera(0.0, 20.0, 0x1200));
        Assert.True(scene.SpawnedCsacUnitPrimitives > 0, "moved CSAC aircraft produced no rendered faces");
    }

    [ConftFact]
    public void CsacMobileUnits_FollowTerrainHeightOnConft()
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

        int dt = RetailClock.LocomotionTicks;
        for (int f = 0; f < 20; f++)
        {
            scene.AdvanceSkyCaptain(dt);

            int aircraftGround = LevelGround.Height(scene.Map, aircraft.X, 0x100000, aircraft.Z, out _);
            Assert.Equal(aircraftGround + BuiltUnit.AircraftAltitude, aircraft.Y);
            Assert.True(aircraft.Y >= aircraftGround, "aircraft sank below terrain");

            int tankGround = LevelGround.Height(scene.Map, tank.X, 0x100000, tank.Z, out _);
            Assert.Equal(tankGround, tank.Y);
        }
    }

    [Fact]
    public void CsacMobileUnits_FollowSteppedGroundSamplerWithoutSinkingOrClippingWalls()
    {
        var registry = new MapObjectRegistry
        {
            GroundHeight = pos => SteppedTerrainHeight(pos.X, pos.Z),
        };
        registry.InitMissionSpawnState();
        registry.LiveObjects.Allocate();

        var spawner = new CsacUnitSpawner(registry);
        var burstOnly = new CsacSpawnerParameters(RespawnDelayRaw: -10, MaxConcurrent: 4, InitialFlags: 1, InitialCount: 4);

        spawner.Register(MakeCsacRecord(1, BuildOrderSpawner.AircraftEntityType, team: 2, modelId: 22, 0x100000, 0, 0x200000), burstOnly);
        spawner.Register(MakeCsacRecord(2, BuildOrderSpawner.TankEntityType, team: 2, modelId: 30, 0x100000, 0, 0x200000), burstOnly);
        spawner.Register(MakeCsacRecord(3, BuildOrderSpawner.HeavyTankEntityType, team: 2, modelId: 37, 0x100000, 0, 0x200000), burstOnly);
        spawner.Register(MakeCsacRecord(4, 0x08, team: 2, modelId: 40, 0x100000, 0x500, 0x200000), burstOnly);

        Assert.True(spawner.ApplyCommandById(1, CsacUnitSpawner.CommandQueueBurst, 1));
        Assert.True(spawner.ApplyCommandById(2, CsacUnitSpawner.CommandQueueBurst, 1));
        Assert.True(spawner.ApplyCommandById(3, CsacUnitSpawner.CommandQueueBurst, 1));
        Assert.True(spawner.ApplyCommandById(4, CsacUnitSpawner.CommandQueueBurst, 1));
        Assert.Equal(4, spawner.Drain(0));

        var aircraft = spawner.SpawnedUnits.Single(u => u.SourceSlot.Record.Id == 1);
        var tank = spawner.SpawnedUnits.Single(u => u.SourceSlot.Record.Id == 2);
        var heavyTank = spawner.SpawnedUnits.Single(u => u.SourceSlot.Record.Id == 3);
        var turret = spawner.SpawnedUnits.Single(u => u.SourceSlot.Record.Id == 4);

        const int frames = 10;
        const int dt = 8;
        bool sawWall = false;
        bool sawValley = false;

        for (int f = 1; f <= frames; f++)
        {
            registry.BroadcastMessage6(dt);

            Assert.Equal(0x100000 + 0x700 * dt * f, aircraft.X);
            Assert.Equal(0x100000 + 0x400 * dt * f, tank.X);
            Assert.Equal(0x100000 + 0x400 * dt * f, heavyTank.X);

            // Stationary base-defender turret must not move.
            Assert.Equal((0x100000, 0x500, 0x200000), (turret.X, turret.Y, turret.Z));

            int aircraftGround = SteppedTerrainHeight(aircraft.X, aircraft.Z);
            Assert.Equal(aircraftGround + BuiltUnit.AircraftAltitude, aircraft.Y);
            Assert.True(aircraft.Y > aircraftGround, "aircraft must stay above wall/terrain");

            int tankGround = SteppedTerrainHeight(tank.X, tank.Z);
            Assert.Equal(tankGround, tank.Y);
            Assert.Equal(SteppedTerrainHeight(heavyTank.X, heavyTank.Z), heavyTank.Y);

            if (tankGround == 0x28000) sawWall = true;
            if (tankGround == 0x400) sawValley = true;
        }

        Assert.True(sawWall, "test path must cross the raised ridge/wall");
        Assert.True(sawValley, "test path must cross the valley");
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
}
