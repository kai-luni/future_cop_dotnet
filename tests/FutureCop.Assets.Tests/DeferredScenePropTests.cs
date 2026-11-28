using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Tests the Cact type-0x60 deferred-scene asset branch
/// (<c>gLoadActorChunk.c:109-110</c>, <c>func_80084110</c> -> <c>gSpawnSceneNode</c>) on the
/// shipped PA/CONFT.MIS: the 89 static scene nodes, their Cobj model resolution, the ground snap of
/// <c>gSpawnSceneNode.c:292-299</c>, the (x &gt;&gt; 17, z &gt;&gt; 17) bucket of <c>:429</c>, the 5x5
/// cell-window cull the viewer shares with <c>gDrawCellWindowModels.c:91-146</c>, and the two
/// home-base purchase pad volumes that are now part of the normal frame.
/// </summary>
public sealed class DeferredScenePropTests
{
    private const int Type96 = 0x60;

    private static string Mission() => LevelSceneRetirementTests.FindMission()!;

    /// <summary>
    /// CONFT.MIS carries 89 type-0x60 Cact records (absolute archive fact, independent of the port),
    /// and every one of them has to become a scene node with its first aRSL Cobj model resolved.
    /// </summary>
    [RetailFact]
    public void ConftPlacesAllEightyNineTypeNinetySixRecords()
    {
        var file = new MisFile(Mission());
        var records = MissionEntities.Load(file);
        var sources = records.Where(r => r.Container == EntityContainer.Cact && r.Type == Type96).ToList();

        Assert.Equal(89, sources.Count);

        var registry = new MapObjectRegistry();
        registry.InitMissionSpawnState();
        var map = LevelMap.FromBytes(file.Bytes);
        registry.PlacementHeight = (x, z, rank) => LevelGround.PlacementHeight(map, x, z, rank) ?? 0;

        Assert.Equal(89, registry.PlaceDeferredSceneAssets(records));
        Assert.Equal(89, registry.SceneProps.Count);

        // Archive order, and no record lost or duplicated: retail walks the Cact records in file order
        // and appends each node to its cell bucket (gSpawnSceneNode.c:420-433).
        Assert.Equal(sources.Select(r => r.Id), registry.SceneProps.Select(p => p.Id));

        // func_80019BF0(list->entries[0].group, .name) must resolve: a record without a Cobj reference
        // would leave the node's model null in retail.
        Assert.All(registry.SceneProps, p => Assert.NotEqual(0, p.ModelId));
        Assert.All(registry.SceneProps, p => Assert.Contains(file.Models, m => m.Id == p.ModelId));

        // Type 0x60 never becomes a live actor: gLoadActorChunk.c:109-110 returns before
        // gSpawnQueuedEntityAndGetId, so the live registry stays empty here.
        Assert.Equal(0, registry.LiveObjects.Count);
    }

    /// <summary>
    /// The five Cobj-62 props of the P1 base pad cells, the evidence the todo's grid (8,12)..(9,13)
    /// refers to (that grid is <c>x &gt;&gt; 16</c>; the scene bucket is <c>x &gt;&gt; 17</c>).
    /// </summary>
    [RetailFact]
    public void ConftPlacesTheFiveCobj62PropsOfTheBasePadCells()
    {
        var file = new MisFile(Mission());
        var records = MissionEntities.Load(file);
        var map = LevelMap.FromBytes(file.Bytes);
        var registry = new MapObjectRegistry { PlacementHeight = (x, z, rank) => LevelGround.PlacementHeight(map, x, z, rank) ?? 0 };
        registry.InitMissionSpawnState();
        registry.PlaceDeferredSceneAssets(records);

        var padCells = registry.SceneProps
            .Where(p => p.ModelId == 62 && (p.X >> 16) is 8 or 9 && (p.Z >> 16) is 12 or 13)
            .ToArray();

        Assert.Equal(new[] { 194, 195, 196, 197, 223 }, padCells.Select(p => p.Id));
        // All five share one 0x20000 scene bucket, the unit gSpawnSceneNode.c:429 files them under.
        Assert.All(padCells, p =>
        {
            Assert.Equal(4, p.Row);
            Assert.Equal(6, p.Column);
        });

        // gSpawnSceneNode.c:292-299: alignment code 0 selects the highest surface and the node y is
        // that height plus the (s16 +0x1A) << 4 lift (0 for all five).
        Assert.All(padCells, p =>
        {
            Assert.Equal(0, p.AlignmentCode);
            Assert.Equal(0, p.Record.U16(0x1A));
            Assert.Equal(LevelGround.PlacementHeight(map, p.X, p.Z, -1), p.Y);
        });

        // Source +0x14 is the yaw fed to gBuildRotationMatrix; all five face the same 90 degrees.
        Assert.All(padCells, p => Assert.Equal(1024, p.Yaw));
        Assert.All(padCells, p => Assert.Equal(new[] { 0x40, 0x40, 0x40 }, new[] { (int)p.ScaleX, (int)p.ScaleY, (int)p.ScaleZ }));
    }

    /// <summary>
    /// The viewer places the props through the scene pass: at pad 47 only the buckets inside the 5x5
    /// cell window are drawn, which is strictly fewer than all 89 nodes.
    /// </summary>
    [RetailFact]
    public void SceneDrawsTheBasePropsInsideTheCellWindowOnly()
    {
        var scene = new LevelScene(Mission());
        scene.EnableObjectives();
        Assert.Equal(89, scene.ObjectRegistry.SceneProps.Count);

        var pad = scene.PurchasePads!.FindPad(47)!;
        int ground = LevelGround.Height(scene.Map, pad.WorldX, 0x100000, pad.WorldZ, out _);
        scene.AttachWalker(new MechBody { X = pad.WorldX, Y = ground, Z = pad.WorldZ, Form = 0 });
        scene.Render(new LevelCamera(0.0, 20.0, 0x1200));

        Assert.True(scene.VisibleSceneProps > 0, "no type-96 prop entered the 5x5 cell window at pad 47");
        Assert.True(scene.VisibleSceneProps < 89, "the cell window culled nothing");
        Assert.True(scene.ScenePropPrimitives > 0, "the visible props produced no faces");
    }

    /// <summary>
    /// The two home-base pad volumes belong to the normal frame; the remaining walker pads still
    /// need the debug overlay, so the two counters stay disjoint.
    /// </summary>
    [RetailFact]
    public void HomeBasePadVolumesAreDrawnWithoutTheDebugOverlay()
    {
        var scene = new LevelScene(Mission());
        scene.EnableObjectives();
        var pad = scene.PurchasePads!.FindPad(47)!;
        Assert.NotNull(scene.PurchasePads.FindPad(51));
        int ground = LevelGround.Height(scene.Map, pad.WorldX, 0x100000, pad.WorldZ, out _);
        scene.AttachWalker(new MechBody { X = pad.WorldX, Y = ground, Z = pad.WorldZ, Form = 0 });
        Assert.False(scene.ShowPadMarkers);

        scene.Render(new LevelCamera(0.0, 20.0, 0x1200));

        // Two type-14 boxes, each four edges plus two diagonals (8003151C.c draws none of this;
        // the outline is the viewer's stand-in for the invisible trigger volume).
        Assert.Equal(12, scene.BasePadLines);
        Assert.Equal(0, scene.PadMarkerLines);

        scene.ShowPadMarkers = true;
        scene.Render(new LevelCamera(0.0, 20.0, 0x1200));
        // The overlay adds the other walker pads near the base (pad 47/51 are already drawn above).
        Assert.True(scene.PadMarkerLines > 0);
        Assert.Equal(12, scene.BasePadLines);
    }
}
