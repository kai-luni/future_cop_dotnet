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
/// the 3DBB-centred (x &gt;&gt; 17, z &gt;&gt; 17) bucket of <c>:360-431</c>, the 5x5
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
    /// The 89 CONFT type-0x60 props placed the way <see cref="LevelScene"/> places them, including the
    /// model resolution that supplies the 3DBB bound centre the bucket needs.
    /// </summary>
    private static IReadOnlyList<SceneProp> ConftProps()
    {
        var file = new MisFile(Mission());
        var map = LevelMap.FromBytes(file.Bytes);
        var models = file.Models.ToDictionary(m => m.Id);
        var registry = new MapObjectRegistry
        {
            PlacementHeight = (x, z, rank) => LevelGround.PlacementHeight(map, x, z, rank) ?? 0,
            ResolveModel = id => models.TryGetValue(id, out var model) ? model : null,
        };
        registry.InitMissionSpawnState();
        Assert.Equal(89, registry.PlaceDeferredSceneAssets(MissionEntities.Load(file)));
        return registry.SceneProps;
    }

    private static string PaMission(string name)
    {
        string path = Path.Combine(Path.GetDirectoryName(Mission())!, name);
        Assert.True(File.Exists(path), $"PA/{name} is missing from the retail disc.");
        return path;
    }

    private static string HkMission() => PaMission("HK.MIS");

    /// <summary>
    /// Record 86 is one of the twelve CONFT records whose flag byte +0x1D has bit 0x40. Retail then
    /// keeps the third matrix angle at 0 and stores the raw +0x16 byte as the positional sound id
    /// (<c>gSpawnSceneNode.c:316-325</c>); without the gate the port used 84/4096 of a turn
    /// (~7.4 degrees) as a z tilt and treated a sound id as an angle.
    /// </summary>
    [RetailFact]
    public void ConftSoundFormRecord86StaysUprightAndKeepsItsVoiceId84()
    {
        var prop = ConftProps().Single(p => p.Id == 86);

        Assert.Equal(0x42, prop.Flags);
        Assert.Equal(84, prop.Record.U16(0x16));
        Assert.Equal(0, prop.Roll);
        Assert.Equal(84, prop.Voice);

        var basis = new int[9];
        prop.Basis(basis);
        var expected = new int[9];
        MechWalker.RotationMatrix(prop.Yaw, prop.Pitch, 0, expected);
        Assert.Equal(expected, basis);

        // The gate is what removes the tilt: the raw byte as third angle is a different basis.
        var ungated = new int[9];
        MechWalker.RotationMatrix(prop.Yaw, prop.Pitch, prop.Record.U16(0x16), ungated);
        Assert.NotEqual(expected, ungated);
    }

    /// <summary>
    /// Census of the sound form over CONFT: exactly 12 of the 89 records, ten of them model 55 with
    /// flags 0x42 and Voice 84, two of them model 61 with flags 0x40 and Voice 85. The other 77
    /// records have no voice id.
    /// </summary>
    [RetailFact]
    public void ConftHasTwelveSoundFormRecordsTenOfModel55AndTwoOfModel61()
    {
        var props = ConftProps();
        var soundForm = props.Where(p => (p.Flags & 0x40) != 0).ToList();

        Assert.Equal(12, soundForm.Count);
        Assert.Equal(new[] { 86, 92, 94, 98, 121, 131, 137, 141, 143, 159, 335, 336 },
            soundForm.Select(p => p.Id));
        Assert.Equal(10, soundForm.Count(p => p.ModelId == 55 && p.Flags == 0x42 && p.Voice == 84));
        Assert.Equal(2, soundForm.Count(p => p.ModelId == 61 && p.Flags == 0x40 && p.Voice == 85));
        Assert.All(soundForm, p => Assert.Equal(p.Record.U8(0x16), p.Voice));
        Assert.All(props.Where(p => (p.Flags & 0x40) == 0), p => Assert.Equal((byte)0, p.Voice));
    }

    /// <summary>
    /// Retail bucket of <paramref name="prop"/>: the first 3DBB bound centre
    /// (<c>Model.FrameBounds[0][0..2]</c>) through <c>gGteLoadRotation</c> (func_80010820, which
    /// stores <c>R = F * M^T</c>) and <c>gGteTransformWords</c> (func_8001006C: mvmva over V0,
    /// IR1..IR3 out, <c>&lt;&lt; 4</c>), plus the node origin, bucketed <c>&gt;&gt; 17</c>
    /// (<c>gSpawnSceneNode.c:360-431</c>). Rebuilding the path from the two GTE primitives keeps the
    /// test independent of the registry's bucket code and pins the load transpose: the x offset comes
    /// from column 0 of the engine matrix (<c>basis[0]</c>, <c>basis[3]</c>, <c>basis[6]</c>), not
    /// from row 0.
    /// </summary>
    private static (int Row, int Column) CentreBucket(SceneProp prop, IReadOnlyList<short[]> bounds)
    {
        var basis = new int[9];
        prop.Basis(basis);
        var rotation = Gte.Load(new Gte.Mat(basis, 0, 0, 0));
        var (offsetX, _, offsetZ, _) = Gte.Mvmva(rotation, bounds[0][0], bounds[0][1], bounds[0][2]);
        return ((prop.X + (offsetX << 4)) >> 17, (prop.Z + (offsetZ << 4)) >> 17);
    }

    /// <summary>
    /// Three shipped maps carry type-0x60 records whose rotated bound centre leaves the raw
    /// <c>x &gt;&gt; 17</c> origin bucket: 14 of HK's 151 props, and 4 of JOKE's and 4 of SLIM's 75
    /// (see <see cref="JokeAndSlimBucketFourOfSeventyFivePropsByTheRotatedBoundCentre"/>). CONFT and
    /// OVMP move none, which is why the CONFT raster goldens cannot see this. Every prop must match
    /// the bucket retail computes from <c>FrameBounds[0][0..2] &lt;&lt; 4</c> through the node basis
    /// (<c>gSpawnSceneNode.c:360-431</c>).
    /// </summary>
    [RetailFact]
    public void HkBucketsAllPropsByTheRotatedBoundCentre()
    {
        var file = new MisFile(HkMission());
        var map = LevelMap.FromBytes(file.Bytes);
        var models = file.Models.ToDictionary(m => m.Id);
        var registry = new MapObjectRegistry
        {
            PlacementHeight = (x, z, rank) => LevelGround.PlacementHeight(map, x, z, rank) ?? 0,
            ResolveModel = id => models.TryGetValue(id, out var model) ? model : null,
        };
        registry.InitMissionSpawnState();
        Assert.Equal(151, registry.PlaceDeferredSceneAssets(MissionEntities.Load(file)));

        int drifted = 0;
        foreach (var prop in registry.SceneProps)
        {
            var expected = CentreBucket(prop, models[prop.ModelId].FrameBounds);
            Assert.Equal(expected.Row, prop.Row);
            Assert.Equal(expected.Column, prop.Column);
            if (expected != (prop.X >> 17, prop.Z >> 17)) drifted++;
        }

        // Non-vacuous: the centre moves 14 props off their origin bucket. The pair below is the
        // transposition probe: id 303 crosses the row edge to (6, 9) and id 347 keeps its cell,
        // while a row-wise multiply puts them at (6, 10) and (7, 5).
        Assert.Equal(14, drifted);
        var shifted = registry.SceneProps.Single(p => p.Id == 303);
        Assert.Equal((6, 10), (shifted.X >> 17, shifted.Z >> 17));
        Assert.Equal((6, 9), (shifted.Row, shifted.Column));

        var kept = registry.SceneProps.Single(p => p.Id == 347);
        Assert.Equal((8, 5), (kept.X >> 17, kept.Z >> 17));
        Assert.Equal((8, 5), (kept.Row, kept.Column));
    }

    /// <summary>
    /// JOKE and SLIM each file four of their 75 type-0x60 props under a neighbouring cell, the same
    /// four records: 129, 130 and 131 cross column 5 to 6 and 196 crosses row 6 to 5. Both maps are
    /// asserted so the centred bucket is pinned on three maps, not only on HK.
    /// </summary>
    [RetailFact]
    public void JokeAndSlimBucketFourOfSeventyFivePropsByTheRotatedBoundCentre()
    {
        foreach (var name in new[] { "JOKE.MIS", "SLIM.MIS" })
        {
            var file = new MisFile(PaMission(name));
            var map = LevelMap.FromBytes(file.Bytes);
            var models = file.Models.ToDictionary(m => m.Id);
            var registry = new MapObjectRegistry
            {
                PlacementHeight = (x, z, rank) => LevelGround.PlacementHeight(map, x, z, rank) ?? 0,
                ResolveModel = id => models.TryGetValue(id, out var model) ? model : null,
            };
            registry.InitMissionSpawnState();
            Assert.Equal(75, registry.PlaceDeferredSceneAssets(MissionEntities.Load(file)));

            int drifted = 0;
            foreach (var prop in registry.SceneProps)
            {
                var expected = CentreBucket(prop, models[prop.ModelId].FrameBounds);
                Assert.Equal(expected.Row, prop.Row);
                Assert.Equal(expected.Column, prop.Column);
                if (expected != (prop.X >> 17, prop.Z >> 17)) drifted++;
            }
            Assert.Equal(4, drifted);

            foreach (var id in new[] { 129, 130, 131 })
            {
                var prop = registry.SceneProps.Single(p => p.Id == id);
                Assert.Equal((4, 5), (prop.X >> 17, prop.Z >> 17));
                Assert.Equal((5, 5), (prop.Row, prop.Column));
            }
            var rowEdge = registry.SceneProps.Single(p => p.Id == 196);
            Assert.Equal((4, 6), (rowEdge.X >> 17, rowEdge.Z >> 17));
            Assert.Equal((4, 5), (rowEdge.Row, rowEdge.Column));
        }
    }

    /// <summary>
    /// The census the <see cref="SceneProp.Basis"/> remark depends on: HK has 44 of 151 records with a
    /// negative yaw (+0x14, the branch retail randomises with <c>func_80019668() &amp; 0xFFF</c>) and
    /// 50 records with non-uniform Q6 scales. No shipped record has a negative scale byte, so the
    /// jitter of <c>gSpawnSceneNode.c:328-337</c> is unreachable.
    /// </summary>
    [RetailFact]
    public void HkHasFortyFourNegativeYawsAndFiftyNonUniformQ6ScaleRecords()
    {
        var records = MissionEntities.Load(new MisFile(HkMission()))
            .Where(r => r.Container == EntityContainer.Cact && r.Type == Type96).ToList();

        Assert.Equal(151, records.Count);
        Assert.Equal(44, records.Count(r => unchecked((short)r.U16(0x14)) < 0));
        Assert.Equal(0, records.Count(r => unchecked((short)r.U16(0x18)) < 0));
        Assert.Equal(0, records.Count(r => unchecked((short)r.U16(0x16)) < 0));

        Assert.Equal(50, records.Count(r => r.U8(0x1F) != 0x40 || r.U8(0x20) != 0x40 || r.U8(0x21) != 0x40));
        Assert.Equal(0, records.Count(r =>
            unchecked((sbyte)r.U8(0x1F)) < 0 || unchecked((sbyte)r.U8(0x20)) < 0 || unchecked((sbyte)r.U8(0x21)) < 0));
        Assert.Equal(new[] { 0x14, 0x15, 0x17, 0x18, 0x33, 0x40 },
            records.Select(r => r.U8(0x1F)).Distinct().OrderBy(v => v));
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
