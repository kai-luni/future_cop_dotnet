using FcMainMenu;
using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Scene-path tests for retail chase-camera preset cycling and MapZone 0x40 overlap fallback
/// (<c>src/game/800569EC.c:228-246</c>, <c>src/game/80031B40.c:67-90</c>,
/// <c>src/game/800573A4.c:238-243</c>, <c>src/game/8005780C.c:43-56</c>) through the live
/// <see cref="MenuFlow"/> / <see cref="LevelCamera"/> / <see cref="LevelScene"/> host path.
/// </summary>
public sealed class MenuFlowCameraPresetTests
{
    private const uint Down = 116, Enter = 36;

    private static string? FrontEndPath()
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc == null ? null : Path.Combine(disc, "CW", "FE.MIS");
        if (path == null || !File.Exists(path))
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
            return null;
        }
        return path;
    }

    private static MenuFlow StartConftLevel()
    {
        var flow = new MenuFlow(FrontEndPath()!, SceneKind.Level, TextWriter.Null, TextWriter.Null,
            enableLevelMovement: true);
        flow.HandleKey(Down);
        flow.HandleKey(Down);
        flow.HandleKey(Enter); // Setup
        flow.HandleKey(Enter); // Hub
        flow.HandleKey(Enter); // Zone list
        flow.HandleKey(Enter); // Loadout (CONFT)
        flow.HandleKey(Enter); // Scene
        Assert.Equal(MenuScreen.Scene, flow.Screen);
        Assert.NotNull(flow.Level);
        return flow;
    }

    private static PurchasePad PrepareSingleFlaggedCameraZone(LevelScene level, int padRecordId = 106)
    {
        var pads = level.PurchasePads!;
        var zone = pads.FindPad(padRecordId)!;
        // Disable trigger firing (0x10) so zones stay linked while testing camera overlap (0x40).
        foreach (var pad in pads.Pads)
            if (pad.Kind == PurchasePadKind.MapZone) pad.Flags = 0x10;
        zone.Flags = ChaseCameraState.CameraZoneFlag | 0x10;
        return zone;
    }

    private static void TapSelectPad(MenuFlow flow)
    {
        flow.Advance(0.033, PadButtons.Select, AnalogPad.Digital);
        flow.Advance(0.033, PadButtons.None, AnalogPad.Digital);
    }

    private static void TapCameraKey(MenuFlow flow)
    {
        flow.HandleKey(MenuFlow.CameraPresetKey);
        flow.Advance(0.033);
        flow.HandleKeyRelease(MenuFlow.CameraPresetKey);
        flow.Advance(0.033);
    }

    private static bool MatricesEqual(Gte.Mat a, Gte.Mat b) =>
        a.Tx == b.Tx && a.Ty == b.Ty && a.Tz == b.Tz && a.W.SequenceEqual(b.W);

    [RetailFact]
    public void MenuFlowCameraPresetFiveChangesViewInsideFlaggedZoneAndFallsBackOutside()
    {
        var flow = StartConftLevel();
        var level = flow.Level!;
        var walker = level.Walker!;
        var zone = PrepareSingleFlaggedCameraZone(level);

        int radius = level.PlayerCollisionRadius;
        Assert.True(radius > 0);
        int zoneGroundY = LevelGround.Height(level.Map, zone.WorldX, 0x100000, zone.WorldZ, out _);

        // Place walker on the ground at the centre of the flagged linked zone and settle at state 0.
        walker.X = zone.WorldX;
        walker.Y = zoneGroundY;
        walker.Z = zone.WorldZ;
        flow.Advance(0.033);
        Assert.Equal(0, flow.Camera.PresetState);
        Assert.Equal(0, level.ChaseCamera.State);

        var frame = new byte[640 * 480 * 4];
        flow.RenderScene(frame, 2);
        var state0Camera = flow.Camera;
        var state0Eye = level.Eye;
        var state0View = level.LastView!.Value;
        var state0Rgb = (byte[])level.Rgb.Clone();

        // Cycle 0 -> 1 -> 2 -> 3 -> 4 -> 5 via five Select button leading edges.
        for (int i = 0; i < 5; i++)
            TapSelectPad(flow);

        Assert.Equal(5, flow.Camera.PresetState);
        Assert.Equal(5, level.ChaseCamera.State);

        // Anchor back to the exact same ground point so view differences come solely from preset 5.
        walker.X = zone.WorldX;
        walker.Y = zoneGroundY;
        walker.Z = zone.WorldZ;
        flow.Advance(0.033);
        flow.RenderScene(frame, 2);
        var state5Camera = flow.Camera;
        var state5Eye = level.Eye;
        var state5View = level.LastView!.Value;

        Assert.NotEqual(state0Camera, state5Camera);
        Assert.NotEqual(state0Eye, state5Eye);
        Assert.False(MatricesEqual(state0View, state5View));
        Assert.False(state0Rgb.SequenceEqual(level.Rgb));

        // Move one unit past the radius-expanded X edge -> gTestCameraZoneOverlap fails -> state 0.
        walker.X = zone.WorldX + zone.ExtentX + radius + 1;
        walker.Z = zone.WorldZ;
        flow.Advance(0.033);
        Assert.Equal(0, flow.Camera.PresetState);
        Assert.Equal(0, level.ChaseCamera.State);

        // Return to the same ground point to confirm the view matches state 0 again.
        walker.X = zone.WorldX;
        walker.Y = zoneGroundY;
        walker.Z = zone.WorldZ;
        flow.Advance(0.033);
        flow.RenderScene(frame, 2);

        Assert.Equal(state0Camera, flow.Camera);
        Assert.Equal(state0Eye, level.Eye);
        Assert.True(MatricesEqual(state0View, level.LastView!.Value));
    }

    [RetailFact]
    public void MenuFlowKeyboardTabEdgeCyclesAllSevenPresetsAndHoldingDoesNotRepeat()
    {
        var flow = StartConftLevel();
        var level = flow.Level!;
        var walker = level.Walker!;
        var zone = PrepareSingleFlaggedCameraZone(level);
        walker.X = zone.WorldX;
        walker.Z = zone.WorldZ;
        flow.Advance(0.033);
        Assert.Equal(0, flow.Camera.PresetState);

        // Holding Tab across multiple frames must advance once on the leading press edge (8005780C.c:45-50).
        flow.HandleKey(MenuFlow.CameraPresetKey);
        for (int i = 0; i < 6; i++)
            flow.Advance(0.033);
        Assert.Equal(1, flow.Camera.PresetState);

        // Releasing moves phase 3 -> 1 -> 2 (two frames to return to idle).
        flow.HandleKeyRelease(MenuFlow.CameraPresetKey);
        flow.Advance(0.033);
        flow.Advance(0.033);
        Assert.Equal(1, flow.Camera.PresetState);

        // Walk the rest of the 0..6 cycle inside the flagged zone and verify state 6 vs state 5 yaw shift.
        var frame = new byte[640 * 480 * 4];
        Gte.Mat? view5 = null;
        (int X, int Y, int Z) eye5 = default;
        double yaw5 = 0;

        for (int expected = 2; expected <= 6; expected++)
        {
            TapCameraKey(flow);
            walker.X = zone.WorldX;
            walker.Z = zone.WorldZ;
            flow.Advance(0.033);
            Assert.Equal(expected, flow.Camera.PresetState);
            if (expected == 5)
            {
                flow.RenderScene(frame, 2);
                view5 = level.LastView;
                eye5 = level.Eye;
                yaw5 = flow.Camera.ResolvePreset().Yaw;
            }
            else if (expected == 6)
            {
                flow.RenderScene(frame, 2);
                Assert.NotEqual(yaw5, flow.Camera.ResolvePreset().Yaw);
                Assert.NotEqual(eye5, level.Eye);
                Assert.False(MatricesEqual(view5!.Value, level.LastView!.Value));
            }
        }

        // One more edge wraps 6 -> 0 (800569EC.c:233-234).
        TapCameraKey(flow);
        Assert.Equal(0, flow.Camera.PresetState);
    }

    [ConftTheory]
    [InlineData(5)]
    [InlineData(6)]
    public void MenuFlowStatesFiveAndSixHoldOnExactExpandedEdgeAndFallbackOnOutsideUnflagOrUnlink(int targetPreset)
    {
        var flow = StartConftLevel();
        var level = flow.Level!;
        var walker = level.Walker!;
        var pads = level.PurchasePads!;
        var zone = PrepareSingleFlaggedCameraZone(level);
        int radius = level.PlayerCollisionRadius;

        // Outside the flagged zone, cycling past 4 immediately resets to 0 instead of entering 5.
        walker.X = zone.WorldX + zone.ExtentX + radius + 100;
        walker.Z = zone.WorldZ;
        flow.Advance(0.033);
        for (int i = 1; i <= 4; i++)
        {
            TapSelectPad(flow);
            Assert.Equal(i, flow.Camera.PresetState);
        }
        TapSelectPad(flow);
        Assert.Equal(0, flow.Camera.PresetState);

        // On the exact radius-expanded edge (|pos - node| - radius == extent on both X and Z), preset holds.
        int edgeX = zone.WorldX + zone.ExtentX + radius;
        int edgeZ = zone.WorldZ - zone.ExtentZ - radius;
        walker.X = edgeX;
        walker.Z = edgeZ;
        flow.Advance(0.033);
        for (int i = 0; i < targetPreset; i++)
            TapSelectPad(flow);
        Assert.Equal(targetPreset, flow.Camera.PresetState);

        // One unit beyond the expanded edge drops back to state 0.
        walker.X = edgeX + 1;
        walker.Z = edgeZ;
        flow.Advance(0.033);
        Assert.Equal(0, flow.Camera.PresetState);

        // Re-enter at centre and clear flag 0x40 -> drops back to state 0.
        walker.X = zone.WorldX;
        walker.Z = zone.WorldZ;
        flow.Advance(0.033);
        for (int i = 0; i < targetPreset; i++)
            TapSelectPad(flow);
        Assert.Equal(targetPreset, flow.Camera.PresetState);
        zone.Flags = 0x10;
        flow.Advance(0.033);
        Assert.Equal(0, flow.Camera.PresetState);

        // Re-enable flag 0x40, re-enter target preset, then unlink the zone -> drops back to state 0.
        zone.Flags = ChaseCameraState.CameraZoneFlag | 0x10;
        for (int i = 0; i < targetPreset; i++)
            TapSelectPad(flow);
        Assert.Equal(targetPreset, flow.Camera.PresetState);

        int index = -1;
        for (int i = 0; i < pads.ActiveMapZones.Count; i++)
        {
            if (ReferenceEquals(pads.ActiveMapZones[i], zone)) { index = i; break; }
        }
        Assert.True(index >= 0);
        pads.ReleaseMapZoneAt(index);
        flow.Advance(0.033);
        Assert.Equal(0, flow.Camera.PresetState);
    }

    [RetailFact]
    public void TvLevelControlTabKeyCyclesMenuFlowCameraPresetAndChangesRenderedEye()
    {
        string fe = FrontEndPath()!;
        string pa = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(fe))!, "PA");
        long now = 5_000_000;
        const long freq = 1_000_000;
        var flow = TvLevelControl.CreateFlow(fe, pa, TextWriter.Null, TextWriter.Null);
        var tv = new TvLevelControl(flow, () => now, freq);

        void Click(string key)
        {
            tv.Key(TvKeyState.Down, key, 0);
            tv.Key(TvKeyState.Up, key, 0);
        }
        void Tick()
        {
            now += 33_000;
            if (tv.Tick()) flow.Level?.Render(flow.Camera);
        }

        Click("Down");
        Click("Down");
        for (int i = 0; i < 5; i++) { Click("Return"); Tick(); }
        Assert.Equal(MenuScreen.Scene, flow.Screen);
        var level = flow.Level!;
        var walker = level.Walker!;
        var zone = PrepareSingleFlaggedCameraZone(level);
        walker.X = zone.WorldX;
        walker.Z = zone.WorldZ;
        Tick();
        var state0Eye = level.Eye;
        Assert.Equal(0, flow.Camera.PresetState);

        for (int i = 0; i < 5; i++)
        {
            tv.Key(TvKeyState.Down, "Tab", 0);
            Tick();
            tv.Key(TvKeyState.Up, "Tab", 0);
            Tick();
        }
        walker.X = zone.WorldX;
        walker.Z = zone.WorldZ;
        Tick();

        Assert.Equal(5, flow.Camera.PresetState);
        Assert.NotEqual(state0Eye, level.Eye);
    }
}
