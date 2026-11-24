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

        int x0 = spawner.Units[0].X;
        spawner.AdvanceUnits(10, (_, _) => 0x9000);
        Assert.True(spawner.Units[0].X < x0, "tank should march in -X toward targetPos");
        Assert.Equal(0x9000, spawner.Units[0].Y);
    }
}
