using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using Xunit;

namespace FutureCop.Assets.Tests;

public sealed class MoverPlacementTests
{
    [Theory]
    [InlineData("CONFT", 4)]
    [InlineData("HK", 0)]
    [InlineData("JOKE", 2)]
    [InlineData("OVMP", 0)]
    [InlineData("SLIM", 2)]
    public void PlacesRetailCactLiftsWithoutInventingAnyTypeTwenty(string mapName, int count)
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc is null ? null : ModulePath.Resolve(disc, mapName + ".MIS");
        if (path is null)
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
            return;
        }

        var file = new MisFile(path);
        var map = LevelMap.FromBytes(file.Bytes);
        var records = MissionEntities.Load(file);
        var registry = new MapObjectRegistry
        {
            GroundHeight = p => MechGround.Probe(map, p).Height,
            ResolveModel = id => file.Models.FirstOrDefault(m => m.Id == id),
        };
        registry.InitMissionSpawnState();
        registry.LiveObjects.Allocate();
        Assert.Equal(count, registry.PlaceMissionObjects(records));
        var spawner = new CsacUnitSpawner(registry);
        spawner.RegisterFromRecords(records, r => MissionEntities.ReadSpawnerParameters(file.Bytes, r));
        Assert.DoesNotContain(spawner.Slots, slot => slot.Record.Type is 10 or 20);
        Assert.Equal(2, spawner.Slots.Count(slot => slot.Record.Type == 28));

        var lifts = registry.LiveObjects.Entries.Select(Assert.IsType<Elevator>).OrderBy(lift => lift.Id).ToArray();
        if (mapName == "CONFT") Assert.Equal(new[] { 15, 24, 37, 39 }, lifts.Select(lift => lift.Id));
        foreach (var lift in lifts)
        {
            Assert.Equal(2, lift.StopCount);
            Assert.Equal(1, lift.StartStop);
            Assert.Equal(0, lift.Record.U8(0x20));
            Assert.Equal(0, lift.Flags);
            Assert.Equal(71, lift.StopSound);
            Assert.Equal(new[] { 0, 384, 768 }, Enumerable.Range(0, 3).Select(lift.RawDwell));
            Assert.Equal(new[] { mapName == "CONFT" ? 81 : 0, 921, 1228 },
                Enumerable.Range(0, 3).Select(lift.RawStop));
            Assert.Equal(mapName == "CONFT" ? 204 : 245, lift.RawUpParameter);
            Assert.Equal(409, lift.RawDownParameter);
            if (mapName == "CONFT")
            {
                Assert.Equal(34, lift.Record.Model());
                Assert.Equal(0, lift.GroundKind);
                Assert.Equal(lift.Id switch { 15 => 0, 24 => 2048, 37 => 3072, _ => 1024 }, lift.Angle);
            }
            else
            {
                Assert.Equal(0, lift.Angle);
                Assert.Equal(lift.Id == 262 ? 30 : mapName == "JOKE" ? 83 : 87, lift.Record.Model());
                Assert.Equal(lift.Id == 262 ? 14 : 15, lift.GroundKind);
            }
        }
    }
}
