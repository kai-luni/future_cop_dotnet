using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// docs/todo/51-sky-captain-health-bar.md: the one-player HUD's second gauge is fed by
/// <c>D_8009F9EC</c> (gDrawInGameHud.c:47). The live Sky Captain scene has to publish that block -
/// the drone body's +0x1C/+0x1E and its AI gauge record +0x134/+0xA4/+0xA8 - so that
/// gDrawInGameHud.c:151-152 can draw the bar at x 295. This drives a real solo frame on CONFT.MIS
/// (the mission that carries the type-0x25 records) and reads the HUD globals the scene supplies.
/// </summary>
public sealed class SkyCaptainHudTests
{
    private static string? ConftPath()
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc is null ? null : ModulePath.Resolve(disc, "conft.mis");
        return path is not null && File.Exists(path) ? path : null;
    }

    [Fact]
    public void SoloFramePublishesSecondGameBlock_AndHudDrawsItsGaugeAt295()
    {
        string? path = ConftPath();
        if (path is null) Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
        if (path is null) return;

        var scene = new LevelScene(path);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var sky = Assert.IsType<SkyCaptainSystem>(scene.SkyCaptain);
        var drone = Assert.IsType<StrategicDrone>(sky.Drone);

        // One host-shaped solo frame: nine clock ticks, one locomotion substep. Message 6 of the
        // frame order (800271A4.c:180) ticks the Sky Captain, exactly as the running game does.
        var frame = scene.AdvanceSoloFrame(RetailClock.LocomotionTicks, 1, 0, AnalogPad.Digital);
        Assert.Equal(1, frame.Steps);

        // The scene now supplies D_8009F9EC: health +0x1C, max +0x1E, AI gauge record +0x134.
        var globals = scene.HudGlobals(new HudGlobals { FrameTime = 0 });
        var second = Assert.IsType<HudGameBlock>(globals.SecondGame);

        int health = drone.Body.F1C;
        int maxHealth = drone.Body.F1E;
        Assert.Equal(health, second.Health);
        Assert.Equal(maxHealth, second.MaxHealth);

        var extra = Assert.IsType<HudExtraBlock>(second.Extra);
        Assert.Equal(health, extra.A4);                              // ai +0xA4 = body +0x1C
        Assert.Equal(0x600000 / maxHealth, extra.A8);                // gInitHudGauge scale

        // The ported routine: the one-player layout emits the stage line and the second gauge at x 295.
        var device = new RecordingHudDevice();
        var first = new HudGameBlock { Health = 0x64, MaxHealth = 0x64, Player = new() };
        HudDraw.Draw(device, first, new HudSession { Mode = 1, Stage = 3 }, globals);

        Assert.Contains(device.Texts, t => t.Text == "L04" && t.X == 274 && t.Y == 216);
        var bar = Assert.Single(device.Bars, b => b.X == 295);
        int ratio = HudMath.HealthRatio(health, maxHealth);
        Assert.Equal(206, bar.Y);
        Assert.Equal(HudMath.BarIntensityRaw(health, extra.A8), bar.Intensity);
        Assert.Equal(HudMath.BarRed(ratio), bar.R);
        Assert.Equal(HudMath.BarGreen(ratio), bar.G);
        Assert.Equal(HudMath.BarBlue(ratio), bar.B);
        Assert.Equal(HudMath.LowHealthFlag(health, maxHealth) ? 1 : 0, bar.Flag);

        // Fill equals Health / MaxHealth on the gauge's 0..0x60 scale (func_80047BE8 halves it to
        // 0..48 pixels), so full health fills the plate and a hurt captain shows a shorter bar.
        Assert.InRange(bar.Intensity, 0x60 * health / maxHealth - 1, 0x60 * health / maxHealth + 1);

        // A hit on the captain (todo 48/49 owns the damage path; here the landed hit is its effect
        // on the body) shortens the same plate on the next frame.
        drone.Body.F1C = (short)(maxHealth / 3);
        var hurt = Assert.IsType<HudGameBlock>(scene.HudGlobals(globals).SecondGame);
        Assert.Equal(maxHealth / 3, hurt.Health);
        Assert.Equal(maxHealth / 3, hurt.Extra!.A4);
        var hurtDevice = new RecordingHudDevice();
        HudDraw.Draw(hurtDevice, first, new HudSession { Mode = 1, Stage = 3 }, scene.HudGlobals(globals));
        var hurtBar = Assert.Single(hurtDevice.Bars, b => b.X == 295);
        Assert.InRange(hurtBar.Intensity, 0x60 * (maxHealth / 3) / maxHealth - 1,
                                             0x60 * (maxHealth / 3) / maxHealth + 1);
        Assert.True(hurtBar.Intensity < bar.Intensity, "the gauge did not move when the captain lost health");
    }

    /// <summary>An <see cref="IHudDevice"/> that records the gauge and text calls of func_80047F30.</summary>
    private sealed class RecordingHudDevice : IHudDevice
    {
        public List<(int X, int Y, int Intensity, int R, int G, int B, int Flag)> Bars { get; } = new();
        public List<(string Text, int X, int Y, int Colour)> Texts { get; } = new();

        public void Marker() { }
        public void SlotsBar(HudPlayerBlock player, HudGlobals globals) { }
        public void TargetPass() { }
        public void Markers() { }

        public void Bar(int x, int y, int intensity, int p3, int p4, int p5, int flag) =>
            Bars.Add((x, y, intensity, p3, p4, p5, flag));

        public void Rect(uint colour, int x, int y, int w, int h, int blend) { }

        public void Text(string text, int x, int y, int colour) => Texts.Add((text, x, y, colour));

        public int TextWidth(string text) => text.Length * InGameHud.Advance * 2;
    }
}
