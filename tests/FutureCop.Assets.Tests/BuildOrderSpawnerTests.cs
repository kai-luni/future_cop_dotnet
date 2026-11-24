using FutureCop.Assets;
using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Unit tests for <see cref="BuildOrderSpawner"/> (conscious port extension beyond retail).
/// </summary>
public sealed class BuildOrderSpawnerTests
{
    private static EntityRecord MakeRecord(int id, int type, int team, int modelId) =>
        new(id, type, 0, 0, 0, 0, 0x100, team, EntityContainer.Csac, 0, 0, new byte[0x54],
            Array.Empty<int>(), new[] { new ResourceReference("Cobj", modelId) });

    [Fact]
    public void SyncFromEconomy_SpawnsTanksAndAircraftFromSlotAndBaseCounters()
    {
        var spawner = new BuildOrderSpawner();
        var records = new[]
        {
            MakeRecord(1, BuildOrderSpawner.TankEntityType, team: 2, modelId: 30),
            MakeRecord(2, BuildOrderSpawner.AircraftEntityType, team: 2, modelId: 22),
            MakeRecord(3, BuildOrderSpawner.HeavyTankEntityType, team: 2, modelId: 37),
        };
        spawner.ConfigureFromRecords(records, team: 2, basePos: (0x100000, 0, 0x100000), targetPos: (0, 0, 0x100000));

        var economy = new DroneStrategy.Glob { FB0 = 5, FB4 = 5 };
        spawner.Reset(economy);
        Assert.Empty(spawner.Units); // baseline snapshot does not spawn

        economy.C[1][0] += 2; // 2 tanks in slot 1
        economy.C[2][1] += 1; // 1 aircraft in slot 2
        economy.FB0 += 1;     // 1 base tank
        economy.FB4 += 1;     // 1 base aircraft
        economy.FB8 += 1;     // 1 heavy tank
        economy.FBC += 1;     // 1 heavy tank

        int spawned = spawner.SyncFromEconomy(economy, team: 2, (_, _) => 0x10000);
        Assert.Equal(7, spawned);
        Assert.Equal(7, spawner.Units.Count);
        Assert.Equal(3, spawner.Units.Count(u => u.Kind == BuiltUnitKind.Tank && u.ModelId == 30));
        Assert.Equal(2, spawner.Units.Count(u => u.Kind == BuiltUnitKind.Tank && u.ModelId == 37));
        Assert.Equal(2, spawner.Units.Count(u => u.Kind == BuiltUnitKind.Aircraft && u.ModelId == 22));
        Assert.All(spawner.Units.Where(u => u.Kind == BuiltUnitKind.Tank), u => Assert.Equal(0x10000, u.Y));
        Assert.All(spawner.Units.Where(u => u.Kind == BuiltUnitKind.Aircraft),
            u => Assert.Equal(0x10000 + BuiltUnit.AircraftAltitude, u.Y));

        // Second sync without new deltas spawns nothing.
        Assert.Equal(0, spawner.SyncFromEconomy(economy, team: 2, (_, _) => 0x10000));
    }

    [Fact]
    public void AdvanceUnits_MovesTowardTargetAndRespectsMaxUnits()
    {
        var spawner = new BuildOrderSpawner { MaxUnits = 2 };
        var records = new[] { MakeRecord(1, BuildOrderSpawner.TankEntityType, team: 2, modelId: 30) };
        spawner.ConfigureFromRecords(records, team: 2, basePos: (0x200000, 0, 0), targetPos: (0, 0, 0));
        var economy = new DroneStrategy.Glob();
        spawner.Reset(economy);

        economy.C[0][0] = 5;
        Assert.Equal(2, spawner.SyncFromEconomy(economy, team: 2, (_, _) => 0x8000));
        Assert.Equal(2, spawner.Units.Count);
        Assert.Equal(3, spawner.PendingCount);

        int x0 = spawner.Units[0].X;
        spawner.AdvanceUnits(10, (_, _) => 0x9000);
        Assert.True(spawner.Units[0].X < x0, "tank should march in -X toward targetPos");
        Assert.Equal(0x9000, spawner.Units[0].Y);

        // Marching past targetPos despawns active units, and the next sync drains queued production
        // rather than silently dropping it.
        spawner.AdvanceUnits(0x200000 / BuiltUnit.TankSpeed + 100, (_, _) => 0x9000);
        Assert.Empty(spawner.Units);
        Assert.Equal(2, spawner.SyncFromEconomy(economy, team: 2, (_, _) => 0x9000));
        Assert.Equal(2, spawner.Units.Count);
        Assert.Equal(1, spawner.PendingCount);
    }

    [Fact]
    public void SyncFromEconomy_Mode0ProductionStep_SpawnsOneTankPerCounterDelta()
    {
        var spawner = new BuildOrderSpawner();
        var records = new[] { MakeRecord(1, BuildOrderSpawner.TankEntityType, team: 2, modelId: 30) };
        spawner.ConfigureFromRecords(records, team: 2, basePos: (0x200000, 0, 0), targetPos: (0, 0, 0));

        // Build a Mode-0 world with 10 own type-8 units so DroneStrategy stays in Mode 0 and runs
        // base tank production (G.FB0 += n with n >= S4C + 6; DroneStrategy.cs RunMode0Economy).
        var world = new Mode0StrategyWorld(random: 2);
        world.G.S4C = 4;
        world.G.F68 = 15;
        world.G.F74 = 0;
        world.G.F78 = 0x14;
        world.G.F88 = 0;
        world.G.A0 = new[] { 1, 1, 1, 1 };
        spawner.Reset(world.G);

        var self = new DroneStrategy.Self
        {
            Team = 2,
            Strats = new() { Mode = 0, B1B = 1, C1C = 0 },
        };

        int fb0Before = world.G.FB0;
        DroneStrategy.Tick(self, world);
        int fb0Delta = world.G.FB0 - fb0Before;

        Assert.True(fb0Delta >= world.G.S4C + 6,
            $"expected Mode-0 FB0 delta >= {world.G.S4C + 6}, got {fb0Delta}");
        Assert.Equal(12, fb0Delta);

        int spawned = spawner.SyncFromEconomy(world.G, team: 2, (_, _) => 0x10000);
        Assert.Equal(fb0Delta, spawned);
        Assert.Equal(fb0Delta, spawner.Units.Count);
        Assert.Equal(0, spawner.PendingCount);
        Assert.All(spawner.Units, u =>
        {
            Assert.Equal(BuiltUnitKind.Tank, u.Kind);
            Assert.Equal(BuildOrderSpawner.TankEntityType, u.EntityType);
            Assert.Equal(30, u.ModelId);
        });
    }

    private sealed class Mode0StrategyWorld : DroneStrategy.IWorld
    {
        private readonly int _random;

        public Mode0StrategyWorld(int random)
        {
            _random = random;
            Units = Enumerable.Range(0, 10).Select(_ => new DroneStrategy.StrategyUnit
            {
                Active = true,
                Type = 8,
                Team = 2,
                Sub = 10,
            }).ToList();
        }

        public IEnumerable<DroneStrategy.StrategyUnit> Units { get; }
        public DroneStrategy.Glob G { get; } = new();
        public DroneStrategy.StrategyUnit? PlayerEntities => null;
        public byte[] SlotClaimOrder { get; } = { 3, 2, 1, 0 };
        public byte[] AlternateSlotClaimOrder { get; } = { 1, 0, 3, 2 };
        public int NextRandom() => _random;
        public int Sqrt12(int v) => 0;
        public void SetDroneAttackTarget(DroneStrategy.Self self, DroneStrategy.StrategyUnit? unit) { }
        public void SetDronePursuitTarget(DroneStrategy.Self self, DroneStrategy.StrategyUnit unit) { }
        public void UpdateStrategicPosition(DroneStrategy.Self self) { }
        public int FrameTime() => 0;
        public void SetSinglePlayerAiS4C(int value) { }
    }
}
