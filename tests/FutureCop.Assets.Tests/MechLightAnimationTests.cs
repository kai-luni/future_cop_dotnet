using FcMainMenu;
using FutureCop.Assets;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The player mech's blue/red lights are a texture flipbook, not an animation track: the light
/// submodel (the actor's fifth Cobj, the <c>info + 0x210</c> instance of func_800597C0) carries a
/// 3DTA chunk whose entries rewrite the UV words of two 3DTL records, and the retail frame tick
/// steps them once per frame (func_80012E48:66-73 links the chunk into <c>D_8009F564</c>,
/// func_800136C8:155-172 copies the active frame into the model's triangle list). The draw path has
/// to sample the patched record, which is what these tests pin down.
/// </summary>
public class MechLightAnimationTests
{
    private const uint KeyDown = 116, KeyEnter = 36;
    private const double FrameDt = 1.0 / 60.0;

    /// <summary>The two UV flipbooks of the light submodel of CONFT.MIS (Precinct Assault), verbatim from the 3DTA chunk.</summary>
    private static readonly ushort[][] LightBarFrames =
    {
        new ushort[] { 0xDC3E, 0xD43E, 0xD46E, 0xDC6E },
        new ushort[] { 0xE63E, 0xDE3E, 0xDE6E, 0xE66E },
        new ushort[] { 0xF03E, 0xE83E, 0xE86E, 0xF06E },
        new ushort[] { 0xFA3E, 0xF23E, 0xF26E, 0xFA6E },
    };

    private static readonly ushort[][] BulbFrames =
    {
        new ushort[] { 0xE600, 0xE200, 0xE204, 0xE604 },
        new ushort[] { 0xE606, 0xE206, 0xE20A, 0xE60A },
    };

    private static string FrontEnd()
    {
        string disc = LevelTerrainTests.FindDisc()
            ?? throw new InvalidOperationException("Retail disc not found.");
        return Path.Combine(disc, "CW", "FE.MIS");
    }

    /// <summary>Main menu -> Precinct Assault -> single player -> setup -> zone list -> loadout -> scene.</summary>
    private static (MenuFlow Flow, LevelScene Level) OpenPrecinctAssault()
    {
        var flow = new MenuFlow(FrontEnd(), SceneKind.Level, TextWriter.Null, TextWriter.Null,
            enableLevelMovement: true);
        flow.HandleKey(KeyDown);
        flow.HandleKey(KeyDown);
        flow.HandleKey(KeyEnter);
        flow.HandleKey(KeyEnter);
        flow.HandleKey(KeyEnter);
        flow.HandleKey(KeyEnter);
        flow.HandleKey(KeyEnter);
        Assert.Equal(MenuScreen.Scene, flow.Screen);
        var level = Assert.IsType<LevelScene>(flow.Level);
        Assert.Equal("CONFT.MIS", Path.GetFileName(level.MissionPath));
        return (flow, level);
    }

    /// <summary>The light submodel of the player actor: the fifth Cobj slot (func_800579D4 reads e8 +0x30).</summary>
    private static Model LightModel(LevelScene level) =>
        level.ModelById(level.Actor.Resources[4].Id)
        ?? throw new InvalidOperationException("player light model not loaded");

    [RetailFact]
    public void ThePlayerLightModelCarriesTheTwoRetailFlipbooks()
    {
        var level = new LevelScene(Path.Combine(Path.GetDirectoryName(FrontEnd())!, "..", "PA", "CONFT.MIS"));
        var model = LightModel(level);
        Assert.Equal(2, model.TextureAnimations.Count);

        var bar = model.TextureAnimations[0];
        Assert.Equal(4, bar.Max);
        Assert.Equal(1, bar.Type);                    // reverse loop, func_800136C8:121-126
        Assert.Equal(0x09, bar.Flags);                // four halfwords per frame, enabled
        Assert.Equal(35, bar.Rate);
        Assert.Equal(0x14, bar.DstOffset);            // record 0x10 + 4: the two UV words
        Assert.Equal(0x10, bar.RecordOffset);
        Assert.True(bar.CoversUvPair);
        Assert.Equal(LightBarFrames, bar.Frames);

        var bulb = model.TextureAnimations[1];
        Assert.Equal(2, bulb.Max);
        Assert.Equal(1, bulb.Type);
        Assert.Equal(0x11, bulb.Flags);
        Assert.Equal(60, bulb.Rate);
        Assert.Equal(0x24, bulb.DstOffset);
        Assert.Equal(0x20, bulb.RecordOffset);
        Assert.True(bulb.CoversUvPair);
        Assert.Equal(BulbFrames, bulb.Frames);

        // Both patched records are textured and actually used by the submodel's faces.
        foreach (var record in new[] { 0x10, 0x20 })
        {
            Assert.Contains(model.Textures, t => t.Offset == record && t.TypeLoaded == 2);
            Assert.Contains(model.Polygons, p => p.TextureOffset == record);
        }
        // The submodel is the fourth extra part the draw path composes (identity placement at the
        // companion anchor), so the flipbook reaches the screen through LevelScene.Render.
        Assert.Equal(model.Id, level.ModelById(model.Id)!.Id);
    }

    [Fact]
    public void TheFlipbooksStepBackwardsAtTheirRetailRates()
    {
        var bar = new TextureAnimationState(new TextureAnimation(4, 1, 0x09, 35, 0x14, LightBarFrames));
        var bulb = new TextureAnimationState(new TextureAnimation(2, 1, 0x11, 60, 0x24, BulbFrames));

        // On-disc cur = 0 and timer = 0, so the first frame step already switches to max - 1
        // (func_800136C8:91-98 counts the s16 timer down, 0 - 5 <= 0).
        bar.Advance(5);
        bulb.Advance(5);
        Assert.Equal(3, bar.Frame);
        Assert.Equal(30, bar.Timer);
        Assert.Equal(1, bulb.Frame);
        Assert.Equal(55, bulb.Timer);

        // 30 more ticks of timer = six 60 Hz frames; the sixth lands on 0 and steps 3 -> 2, then the
        // timer reloads with the 35-tick rate.
        for (int i = 0; i < 6; i++) bar.Advance(5);
        Assert.Equal(2, bar.Frame);
        Assert.Equal(35, bar.Timer);

        // The bar walks 1, 0 and wraps back to 3, one step per 35 ticks.
        for (int frame = 1; frame >= 0; frame--)
        {
            for (int i = 0; i < 7; i++) bar.Advance(5);
            Assert.Equal(frame, bar.Frame);
        }
        for (int i = 0; i < 7; i++) bar.Advance(5);
        Assert.Equal(3, bar.Frame);

        // The bulb toggles back to 0 after 55 ticks and returns after 60 more.
        for (int i = 0; i < 11; i++) bulb.Advance(5);
        Assert.Equal(0, bulb.Frame);
        Assert.Equal(60, bulb.Timer);
        for (int i = 0; i < 12; i++) bulb.Advance(5);
        Assert.Equal(1, bulb.Frame);
    }

    /// <summary>
    /// Frame counts and durations of the animations the mech state machine drives, read from the
    /// retail frame table (AnmD) of the two player models. Clip indices and modes are the ones
    /// gTickOnFootPhysics.c:802-877 and gUpdatePlayerFormAndMotion.c:207-241 select; the values below
    /// are what the port's MechWalker/MechPlayer therefore play, so a deviation in the frame table or
    /// in the clip layout shows up here instead of in a playtest.
    /// </summary>
    [RetailFact]
    public void TheMechClipsMatchTheRetailFrameTable()
    {
        var level = new LevelScene(Path.Combine(Path.GetDirectoryName(FrontEnd())!, "..", "PA", "CONFT.MIS"));
        var body = level.ModelById(level.Actor.Resources[0].Id)!;
        var companion = level.ModelById(level.Actor.Resources[2].Id)!;

        // Body: 62 clips. The walk cycle is clip 0x36 forward / 0x37 mirrored, 19 frames each at a
        // duration of 1000 ticks per frame; the morph is clip 0x0C, a type-3 scrub over 30 frames.
        Assert.Equal(62, body.AnimationClips.Count);
        AssertWalkClip(body.AnimationClips[0x36], start: 256, end: 274, next: 56);
        AssertWalkClip(body.AnimationClips[0x37], start: 276, end: 294, next: 57);
        var morph = body.AnimationClips[0x0C];
        Assert.Equal((330, 359), (morph.Start, morph.End));
        Assert.Equal(30, morph.End - morph.Start + 1);
        Assert.Equal(3, morph.Type);
        Assert.Equal(10000, morph.Duration);

        // Companion: 3 clips, its morph is clip 2, the same 30 frames as a type-3 scrub.
        Assert.Equal(3, companion.AnimationClips.Count);
        var companionMorph = companion.AnimationClips[2];
        Assert.Equal((0, 29), (companionMorph.Start, companionMorph.End));
        Assert.Equal(3, companionMorph.Type);
        Assert.Equal(1000, companionMorph.Duration);
    }

    private static void AssertWalkClip(AnimationClip clip, short start, short end, byte next)
    {
        Assert.Equal((start, end), (clip.Start, clip.End));
        Assert.Equal(19, clip.End - clip.Start + 1);
        Assert.Equal(1000, clip.Duration);
        Assert.Equal(0, clip.Mode);
        Assert.Equal(2, clip.Flags);
        Assert.Equal(next, clip.Next0);
    }


    /// <summary>
    /// The animated texels are the lights: the 48x8 strip of record 0x10 mixes blue- into red-dominant
    /// texels, then rotates to the all-red band, the dark band and back, and the 4x4 record 0x20
    /// switches between a lit and a dark cell. Sampling goes through the same page/CLUT decode as
    /// <see cref="LevelScene.Tri"/>, so the counts are what the draw path would blend.
    /// </summary>
    [RetailFact]
    public void TheFlipbookTexelsAreTheBlueAndRedLights()
    {
        var level = new LevelScene(Path.Combine(Path.GetDirectoryName(FrontEnd())!, "..", "PA", "CONFT.MIS"));
        var model = LightModel(level);
        var bar = model.TextureAnimations[0];
        var bulb = model.TextureAnimations[1];

        var bar0 = CountLightTexels(level, model, bar, 0);
        Assert.True(bar0.Blue >= 100, $"blue {bar0.Blue}");
        Assert.True(bar0.Red >= 100, $"red {bar0.Red}");

        var bar1 = CountLightTexels(level, model, bar, 1);
        Assert.True(bar1.Red >= 200 && bar1.Red > 4 * bar1.Blue, $"red {bar1.Red} blue {bar1.Blue}");
        var bar2 = CountLightTexels(level, model, bar, 2);
        Assert.True(bar2.Bright <= 40, $"bright {bar2.Bright}");
        var bar3 = CountLightTexels(level, model, bar, 3);
        Assert.True(bar3.Red >= 200 && bar3.Red > 4 * bar3.Blue, $"red {bar3.Red} blue {bar3.Blue}");

        var bulb0 = CountLightTexels(level, model, bulb, 0);
        var bulb1 = CountLightTexels(level, model, bulb, 1);
        Assert.True(bulb0.Bright >= 20, $"bright {bulb0.Bright}");
        Assert.Equal(0, bulb1.Bright);
    }

    /// <summary>Blue/red/bright texel counts of one flipbook frame, decoded like the draw path does.</summary>
    private static (int Blue, int Red, int Bright) CountLightTexels(
        LevelScene level, Model model, TextureAnimation animation, int frame)
    {
        var record = model.Textures.First(t => t.Offset == animation.RecordOffset);
        var (pageX, pageY, depth) = Vram.DecodeTpage(record.Tpage);
        var (clutX, clutY) = Vram.DecodeClut(record.Clut);
        var cells = level.Vram.Cells;
        int clutRow = clutY * Vram.Width + clutX;
        var words = animation.Frames[frame];
        int uLo = Math.Min(words[0] & 0xFF, words[2] & 0xFF);
        int uHi = Math.Max(words[0] & 0xFF, words[2] & 0xFF);
        int vLo = Math.Min(words[0] >> 8, words[2] >> 8);
        int vHi = Math.Max(words[0] >> 8, words[2] >> 8);
        int blue = 0, red = 0, bright = 0;
        for (int v = vLo; v <= vHi; v++)
            for (int u = uLo; u <= uHi; u++)
            {
                int cellRow = (pageY + v) * Vram.Width + pageX;
                ushort raw = depth switch
                {
                    0 => cells[clutRow + ((cells[cellRow + (u >> 2)] >> ((u & 3) * 4)) & 0x0F)],
                    1 => cells[clutRow + ((cells[cellRow + (u >> 1)] >> ((u & 1) * 8)) & 0xFF)],
                    _ => cells[cellRow + u],
                };
                int r = raw & 0x1F, g = (raw >> 5) & 0x1F, b = (raw >> 10) & 0x1F;
                if (b > r + 8) blue++;
                if (r > b + 8) red++;
                if (r + g + b > 40) bright++;
            }
        return (blue, red, bright);
    }

    [RetailFact]
    public void TheDrawPathSamplesTheSteppedLightFrames()
    {
        var (flow, level) = OpenPrecinctAssault();
        var model = LightModel(level);
        var record = model.Textures.First(t => t.Offset == 0x10);
        var sink = new RecordingSink();
        level.Sink = sink;

        // Frame 0 of both flipbooks is what the on-disc 3DTL carries. The page is shared with the
        // map, so the checks below compare the sets between two frames instead of expecting only
        // the light texels on the page.
        level.Render(new LevelCamera());
        var frame0 = sink.UvsOf(record.Tpage, record.Clut);
        Assert.Subset(frame0, ExpectedUvs(LightBarFrames[0]));
        Assert.Subset(frame0, ExpectedUvs(BulbFrames[0]));

        // One retail frame (5 clock ticks) steps both entries once: the bar to frame 3, the bulb to
        // frame 1 (func_800136C8:91-98, the on-disc timer starts at 0).
        flow.Advance(FrameDt, PadButtons.None, AnalogPad.Digital);
        sink.Uvs.Clear();
        level.Render(new LevelCamera());
        var frame1 = sink.UvsOf(record.Tpage, record.Clut);
        Assert.Equal(ExpectedUvs(LightBarFrames[3], BulbFrames[1]), frame1.Except(frame0).ToHashSet());
        Assert.Equal(ExpectedUvs(LightBarFrames[0], BulbFrames[0]), frame0.Except(frame1).ToHashSet());

        // Seven frames later the bar has walked one frame further, the bulb is still on frame 1.
        for (int i = 0; i < 7; i++) flow.Advance(FrameDt, PadButtons.None, AnalogPad.Digital);
        sink.Uvs.Clear();
        level.Render(new LevelCamera());
        var frame2 = sink.UvsOf(record.Tpage, record.Clut);
        Assert.Equal(ExpectedUvs(LightBarFrames[2]), frame2.Except(frame1).ToHashSet());
        Assert.Equal(ExpectedUvs(LightBarFrames[3]), frame1.Except(frame2).ToHashSet());
    }

    /// <summary>The UV pairs of the given flipbook frames, as the draw path samples them.</summary>
    private static HashSet<string> ExpectedUvs(params ushort[][] frames) =>
        frames.SelectMany(frame => frame.Select(w => $"{(w & 0xFF):X2},{(w >> 8):X2}")).ToHashSet();

    /// <summary>Records the texels the scene asks for, so the test reads the real draw path.</summary>
    private sealed class RecordingSink : IPrimitiveSink
    {
        public List<(ushort Tpage, ushort Clut, int U, int V)> Uvs { get; } = new();

        public SortedSet<string> UvsOf(ushort tpage, ushort clut) =>
            new(Uvs.Where(t => t.Tpage == tpage && t.Clut == clut).Select(t => $"{t.U:X2},{t.V:X2}"));

        public void TexturedTriangle(
            int x0, int y0, int u0, int v0, byte r0, byte g0, byte b0,
            int x1, int y1, int u1, int v1, byte r1, byte g1, byte b1,
            int x2, int y2, int u2, int v2, byte r2, byte g2, byte b2,
            ushort tpage, ushort clut, bool semi)
        {
            Uvs.Add((tpage, clut, u0, v0));
            Uvs.Add((tpage, clut, u1, v1));
            Uvs.Add((tpage, clut, u2, v2));
        }

        public void FlatTriangle(int x0, int y0, int x1, int y1, int x2, int y2,
            byte red, byte green, byte blue)
        {
        }
    }
}
