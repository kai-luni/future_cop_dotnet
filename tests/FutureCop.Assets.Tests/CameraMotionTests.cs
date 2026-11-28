using FutureCop.Assets.Tests;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

public sealed class CameraMotionTests
{
    [Fact]
    public void TurnProgressesAcrossMultipleFramesAndStopsAtTarget()
    {
        var motion = new CameraMotion();
        motion.Turn(1);
        Assert.True(motion.Advance(0.1));
        Assert.InRange(motion.Yaw, 30.1, 59.9);
        Assert.True(motion.Advance(1));
        Assert.Equal(60, motion.Yaw, 5);
        Assert.False(motion.Advance(0.1));
    }

    [Fact]
    public void CameraZoneOverlapMatchesLeafMoreHarnessCases()
    {
        // Port of tests/host/leaf_more_harness.c:931-947 (gTestCameraZoneOverlap / func_80031B40):
        // empty list, unflagged containing zone ignored, eligible zone misses z, one unit outside
        // expanded bounds, exact inclusive expanded edges on both axes, and negative x miss.
        int x = 1298, z = -538;
        Assert.False(CameraMotion.HasCameraZoneOverlap(Array.Empty<PurchasePad>(), x, z, radius: 2));

        // C stores node->x = 5 (<< 8 = 1280), node->z = -2 (<< 8 = -512), extent << 4.
        var unflagged = new PurchasePad(
            recordId: 1, PurchasePadKind.MapZone,
            worldX: 5 << 8, worldY: 0, worldZ: -2 << 8,
            extentX: 20 << 4, extentY: 0, extentZ: 20 << 4,
            flags: 0x10, targetId: -1, matchSelector: 2, script0Offset: 0, script2Offset: 0);
        var eligible = new PurchasePad(
            recordId: 2, PurchasePadKind.MapZone,
            worldX: 5 << 8, worldY: 0, worldZ: -2 << 8,
            extentX: 1 << 4, extentY: 0, extentZ: 1 << 4,
            flags: 0x40, targetId: -1, matchSelector: 2, script0Offset: 0, script2Offset: 0);
        var zones = new[] { eligible, unflagged };

        Assert.False(CameraMotion.HasCameraZoneOverlap(zones, x, z, radius: 2));

        z = -530;
        Assert.False(CameraMotion.HasCameraZoneOverlap(zones, x, z, radius: 1));
        Assert.True(CameraMotion.HasCameraZoneOverlap(zones, x, z, radius: 2));

        x = 1261;
        Assert.False(CameraMotion.HasCameraZoneOverlap(zones, x, z, radius: 2));
    }

    [ConftTheory]
    [InlineData(5)]
    [InlineData(6)]
    public void ChaseCameraStatesFiveAndSixHoldOnFlaggedLinkedZoneEdgeAndFallbackOtherwise(int preset)
    {
        // C src/game/800569EC.c:239-246 + src/game/80031B40.c:74-89 over a live CONFT MapZone list.
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        var pads = scene.PurchasePads!;
        var zone = pads.FindPad(106)!;
        foreach (var pad in pads.Pads)
            if (pad.Kind == PurchasePadKind.MapZone) pad.Flags = 0;

        int radius = scene.PlayerCollisionRadius;
        Assert.True(radius > 0);
        int edgeX = zone.WorldX + zone.ExtentX + radius;
        int edgeZ = zone.WorldZ - zone.ExtentZ - radius;

        // Unflagged linked zone does not hold preset 5/6 even at centre.
        var motion = new CameraMotion { State = preset };
        motion.Advance(0.05, scene.ActiveMapZones, zone.WorldX, zone.WorldZ, radius);
        Assert.Equal(0, motion.State);

        // Flagged linked zone holds preset 5/6 on its exact radius-expanded edge.
        zone.Flags = CameraMotion.CameraZoneFlag;
        motion.State = preset;
        motion.Advance(0.05, scene.ActiveMapZones, edgeX, edgeZ, radius);
        Assert.Equal(preset, motion.State);

        // One unit beyond the expanded edge falls back to state 0.
        motion.Advance(0.05, scene.ActiveMapZones, edgeX + 1, edgeZ, radius);
        Assert.Equal(0, motion.State);

        // Unlinking the flagged zone causes fallback to state 0 even at the zone's centre.
        int index = -1;
        for (int i = 0; i < pads.ActiveMapZones.Count; i++)
        {
            if (ReferenceEquals(pads.ActiveMapZones[i], zone)) { index = i; break; }
        }
        Assert.True(index >= 0);
        pads.ReleaseMapZoneAt(index);
        Assert.True(zone.Released);
        Assert.DoesNotContain(zone, scene.ActiveMapZones);

        motion.State = preset;
        motion.Advance(0.05, scene.ActiveMapZones, zone.WorldX, zone.WorldZ, radius);
        Assert.Equal(0, motion.State);
    }

    [Fact]
    public void NonZonePresetsStayActiveWithoutOverlapAndCycleWrapsAtSeven()
    {
        var motion = new CameraMotion();
        for (int expected = 1; expected < 7; expected++)
        {
            motion.CyclePreset();
            Assert.Equal(expected, motion.State);
        }
        motion.CyclePreset();
        Assert.Equal(0, motion.State);

        for (int state = 0; state <= 4; state++)
        {
            motion.State = state;
            motion.Advance(0.05, Array.Empty<PurchasePad>(), 0, 0, 0);
            Assert.Equal(state, motion.State);
        }
    }
}
