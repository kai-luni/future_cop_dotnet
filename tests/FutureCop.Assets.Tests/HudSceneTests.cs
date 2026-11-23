using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Scene-level proof that the running level draws the in-game HUD. Retail calls
/// <c>gDrawInGameHud</c> (func_80047F30) once per frame right after the world pass:
/// <c>gRunRegionPass</c> then <c>gDrawInGameHud</c> in func_800271A8 (800271A4.c:194,198) and in
/// func_80027510 (80027510.c:250-251). <see cref="LevelScene.Render"/> is that frame in the port, so
/// rendering a live <c>PA/CONFT.MIS</c> scene must sequence the HUD into the device, in the order the
/// original routine emits it (<c>tests/golden/hud-draw.vectors</c>, produced by the retail C).
/// </summary>
public sealed class HudSceneTests
{
    private static string? FindConftMis()
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc is null ? null : ModulePath.Resolve(disc, "conft.mis");
        if (path == null || !File.Exists(path))
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
            return null;
        }
        return path;
    }

    /// <summary>Call list of one CASE of <c>golden/hud-draw.vectors</c>.</summary>
    private static List<string> GoldenCase(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "hud-draw.vectors");
        var calls = new List<string>();
        bool inCase = false;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("CASE ", StringComparison.Ordinal))
            {
                if (inCase) break;
                inCase = line.Split(' ')[1] == name;
                continue;
            }
            if (inCase && line.StartsWith("  ", StringComparison.Ordinal)) calls.Add(line[2..]);
        }
        Assert.NotEmpty(calls);
        return calls;
    }

    /// <summary>A live level as the menu tool starts it (walker, fire path, objectives) with the HUD on.</summary>
    private static LevelScene LiveScene(string mis, Recorder recorder, int frameTime = 16)
    {
        var scene = new LevelScene(mis);
        scene.EnableMechMovement();
        scene.EnableFiring();
        scene.EnableHud();
        scene.HudGlobals = scene.HudGlobals! with { FrameTime = frameTime };
        scene.HudDevice = recorder;
        return scene;
    }

    [Fact]
    public void Rendering_a_live_level_sequences_the_retail_hud_after_the_world_pass()
    {
        var mis = FindConftMis();
        if (mis is null) return;

        var recorder = new Recorder();
        var scene = LiveScene(mis, recorder);
        int primitives = scene.Render(new LevelCamera());
        Assert.True(primitives > 0, "the world pass drew nothing");

        // Start of a Precinct Assault match: one player, mode 1 (ScoreSession), both scores and all
        // counters zero, no label timer, no second game block, frame stamp 16 (no blink). That is the
        // input of the retail case G0_health0, except for the health: the walker is at full health.
        var session = scene.ScoreSession(scene.HudSession);
        Assert.Equal(1, session.Mode);
        Assert.Equal(0, session.Score0);
        Assert.Equal(0, session.Score1);

        var expected = GoldenCase("G0_health0");
        // The gauge line: 80047F30.c:113-120 with full health. The colour of a full gauge is taken
        // from the retail case B_mode1_full_blink (health == maxhealth); the height is
        // (s8)((health * (0x600000 / health) + 0x8000) >> 16) = 0x60 = 96, and the busy flag is
        // player+0x64 < 2 with the fill state 2 of a full gauge -> 0.
        var fullBar = GoldenCase("B_mode1_full_blink").Single(c => c.StartsWith("BAR ", StringComparison.Ordinal))
            .Split(' ');
        int barIndex = expected.FindIndex(c => c.StartsWith("BAR ", StringComparison.Ordinal));
        expected[barIndex] = $"BAR 15 206 96 {fullBar[4]} {fullBar[5]} {fullBar[6]} 0";

        Assert.Equal(expected, recorder.Calls);
        Assert.Equal(new[] { "MARKER", "SLOTSBAR" }, recorder.Calls.Take(2));
        Assert.Equal(new[] { "TARGETPASS", "MARKERS" }, recorder.Calls.Skip(3).Take(2));
    }

    [Fact]
    public void Every_rendered_frame_draws_the_hud_once()
    {
        var mis = FindConftMis();
        if (mis is null) return;

        var recorder = new Recorder();
        var scene = LiveScene(mis, recorder);
        for (int frame = 0; frame < 3; frame++) scene.Render(new LevelCamera());
        Assert.Equal(3, recorder.Calls.Count(c => c == "MARKER"));
        Assert.Equal(3, recorder.Calls.Count(c => c == "MARKERS"));
    }

    [Fact]
    public void The_screen_state_gate_of_func_80047F30_holds_in_the_scene()
    {
        var mis = FindConftMis();
        if (mis is null) return;

        // Retail case F_gate_closed: D_8009FA00 != 4 draws nothing (80047F30.c:189 in the port).
        var recorder = new Recorder();
        var scene = LiveScene(mis, recorder);
        scene.HudGlobals = scene.HudGlobals! with { ScreenState = 3 };
        scene.Render(new LevelCamera());
        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public void The_default_device_paints_the_hud_into_the_scene_frame()
    {
        var mis = FindConftMis();
        if (mis is null) return;

        var scene = new LevelScene(mis);
        scene.EnableMechMovement();
        scene.EnableFiring();
        scene.Render(new LevelCamera());
        var withoutHud = (byte[])scene.Rgb.Clone();

        scene.EnableHud();
        scene.Render(new LevelCamera());

        // The gauge ticks of func_80047BE8 are opaque 0x7FFF pixels inside the plate at (15, 206).
        var plate = HudMath.BarBackdrop(15, 206);
        int changed = 0, ticks = 0;
        for (int y = plate.Y; y < plate.Y + plate.H; y++)
            for (int x = plate.X; x < plate.X + plate.W; x++)
            {
                int o = (y * LevelScene.Width + x) * 3;
                if (scene.Rgb[o] != withoutHud[o] || scene.Rgb[o + 1] != withoutHud[o + 1]
                    || scene.Rgb[o + 2] != withoutHud[o + 2]) changed++;
                if (scene.Rgb[o] == InGameHud.TickColour.R && scene.Rgb[o + 1] == InGameHud.TickColour.G
                    && scene.Rgb[o + 2] == InGameHud.TickColour.B) ticks++;
            }
        Assert.True(changed > plate.W * plate.H / 2, $"only {changed} gauge pixels changed");
        Assert.True(ticks > 0, "no gauge tick pixels in the frame");
    }

    /// <summary>Formats each call the way the retail harness records it (see HudDrawVectorTests).</summary>
    private sealed class Recorder : IHudDevice
    {
        public List<string> Calls { get; } = new();

        public void Marker() => Calls.Add("MARKER");
        public void SlotsBar(HudPlayerBlock player, HudGlobals globals) => Calls.Add("SLOTSBAR");
        public void TargetPass() => Calls.Add("TARGETPASS");
        public void Markers() => Calls.Add("MARKERS");

        public void Bar(int x, int y, int intensity, int p3, int p4, int p5, int flag) =>
            Calls.Add($"BAR {x} {y} {intensity} {p3} {p4} {p5} {flag}");

        public void Rect(uint colour, int x, int y, int w, int h, int blend)
        {
            int red = (int)((colour >> 8) & 0x7C);
            int green = (int)((colour >> 3) & 0x7C);
            int blue = (int)((colour & 0x1F) << 2);
            Calls.Add($"RECT {red} {green} {blue} x={x >> 1} y={y >> 1} w={w >> 1} h={h >> 1} blend={blend}");
        }

        public void Text(string text, int x, int y, int colour) =>
            Calls.Add($"TEXT \"{text}\" {x} {y} {colour}");

        public int TextWidth(string text) => text.Length * InGameHud.Advance * 2;
    }
}
