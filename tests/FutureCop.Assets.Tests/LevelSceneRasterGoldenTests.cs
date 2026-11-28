using System.Buffers.Binary;
using System.Security.Cryptography;
using FcMainMenu;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Pixel-level regression tests for <see cref="LevelScene.Rgb"/> and its software triangle
/// rasterizer (<see cref="LevelScene.Tri"/> / <see cref="LevelScene.FlatFace"/>), plus scratch
/// projection bounds (<see cref="LevelScene.QueueModel"/>). Golden SHA-256 digests were recorded
/// on the pre-optimization baseline commit (<c>c574c46</c>).
///
/// The digests of zone 0 frame 0, zone 3 frame 0 and zone 3 frame 120 were re-recorded when the model
/// path started honouring the retail semi-transparency bit (<c>func_80015F38:73</c>, tpage ABR bits
/// 5..6), which blends the player mech's semi parts: 14 changed pixels in an 8x5 box on zone 0, 83
/// pixels in a 21x20 box on zone 3 frame 0 and 77 pixels in two clusters (75 + 2) around the screen
/// centre on zone 3 frame 120. Zone 0 frame 120 is unchanged.
///
/// The attribution is reproducible instead of asserted: <see cref="LevelScene.SemiBlendDisabled"/>
/// switches the ABR blend of <see cref="LevelScene.Tri"/> off, and
/// <see cref="WithoutTheSemiBlendTheSameReplayReproducesThePreBlendDigests"/> replays exactly the
/// frames below with that switch set and expects the pre-blend digests of <c>aa1d386</c> - the commit
/// before the blend landed. Same input, same code, only the blend differs, so a digest change in these
/// frames is the blend's and nothing else's (in particular the unit-heading branches of todo 38 paint
/// no pixel here: they stay active in that probe). To re-derive the values by hand, set
/// <see cref="LevelScene.SemiBlendDisabled"/> after the level is opened and hash the same two frames.
/// </summary>
public class LevelSceneRasterGoldenTests
{
    private const uint KeyEnter = 36, KeyRight = 114, KeyDown = 116;
    private const double FrameDt = 1.0 / 60.0;
    private const int ReplayFrames = 120;

    private static string ResolveFrontEnd()
    {
        string disc = LevelTerrainTests.FindDisc()
            ?? throw new InvalidOperationException("Retail disc not found.");
        return Path.Combine(disc, "CW", "FE.MIS");
    }

    /// <summary>
    /// Opens the level the golden tests hash: menu down/down, three confirms, then <paramref name="zone"/>
    /// rights on the level selector and two confirms.
    /// </summary>
    private static (MenuFlow Flow, LevelScene Level) OpenLevel(int zone, string expectedMission)
    {
        var flow = new MenuFlow(ResolveFrontEnd(), SceneKind.Level, TextWriter.Null, TextWriter.Null,
            enableLevelMovement: true);
        flow.HandleKey(KeyDown);
        flow.HandleKey(KeyDown);
        flow.HandleKey(KeyEnter);
        flow.HandleKey(KeyEnter);
        flow.HandleKey(KeyEnter);
        for (int i = 0; i < zone; i++) flow.HandleKey(KeyRight);
        flow.HandleKey(KeyEnter);
        flow.HandleKey(KeyEnter);

        Assert.Equal(MenuScreen.Scene, flow.Screen);
        var level = Assert.IsType<LevelScene>(flow.Level);
        Assert.Equal(expectedMission, Path.GetFileName(level.MissionPath));
        return (flow, level);
    }

    /// <summary>Steps the deterministic 120-frame play input over an opened level.</summary>
    private static void Replay(MenuFlow flow)
    {
        for (int f = 0; f < ReplayFrames; f++)
            flow.Advance(FrameDt, PlayButtonsForFrame(f), AnalogPad.Digital);
    }

    private static PadButtons PlayButtonsForFrame(int frame)
    {
        PadButtons b = PadButtons.Up;
        int cycle = frame % 120;
        if (cycle is >= 30 and < 60) b |= PadButtons.Right;
        else if (cycle is >= 90 and < 120) b |= PadButtons.Left;
        if ((frame % 40) < 12) b |= PadButtons.Square;
        return b;
    }

    private static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data));

    private static uint PackXy(int x, int y) =>
        ((uint)(ushort)(short)y << 16) | (ushort)(short)x;

    private static uint PackUv(int u, int v) =>
        (uint)((u & 0xFF) | ((v & 0xFF) << 8));

    private static ushort Abgr1555(int r5, int g5, int b5, bool stp = false) =>
        (ushort)((stp ? 0x8000 : 0) | ((b5 & 0x1F) << 10) | ((g5 & 0x1F) << 5) | (r5 & 0x1F));

    private static byte Expand5(int c5) =>
        (byte)((c5 << 3) | (c5 >> 2));

    private static ushort PackTpage(int pageXSlot, int pageYBit, int abr, int depthBits) =>
        (ushort)((pageXSlot & 0x0F) | ((pageYBit & 1) << 4) | ((abr & 3) << 5) | ((depthBits & 3) << 7));

    private static ushort PackClut(int clutXSlot, int clutY) =>
        (ushort)((clutXSlot & 0x3F) | ((clutY & 0x1FF) << 6));

    private static void WriteVram(Vram vram, int x, int y, params ushort[] cells)
    {
        var bytes = new byte[cells.Length * 2];
        for (int i = 0; i < cells.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2, 2), cells[i]);
        vram.LoadRect(x, y, cells.Length, 1, bytes);
    }

    private static (byte R, byte G, byte B) Pixel(byte[] rgb, int x, int y)
    {
        int o = (y * LevelScene.Width + x) * 3;
        return (rgb[o], rgb[o + 1], rgb[o + 2]);
    }

    private static void FillRgb(byte[] rgb, byte r, byte g, byte b)
    {
        for (int i = 0; i < rgb.Length; i += 3)
        {
            rgb[i] = r;
            rgb[i + 1] = g;
            rgb[i + 2] = b;
        }
    }

    [ConftTheory]
    [InlineData(0, "CONFT.MIS",
        "C8ACDF087AFC8E7F674E6155004BB027C5D9696D30436D41C79661A2449AB324",
        "4A9D690FDFE8E795ADD89CC1AB591C8AB2BF29FEC848BD93C777A55C25466982")]
    [InlineData(3, "OVMP.MIS",
        "3BEA86CE03C1647E58FE0D5CA9758527D1A39342BDD33584548B97A62FA7B067",
        "87277EF36724F75044F1190037F3F8EE8840D1EDA489AE61FD4E3B71D2F19175")]
    public void LevelSceneRgbMatchesBaselineSha256AcrossDeterministicPlay(
        int zone, string expectedMission, string expectedFrame0Hash, string expectedFrameNHash)
    {
        var (flow, level) = OpenLevel(zone, expectedMission);

        level.Render(flow.Camera);
        string frame0Hash = Sha256Hex(level.Rgb);

        Replay(flow);

        level.Render(flow.Camera);
        string frameNHash = Sha256Hex(level.Rgb);

        Assert.Equal(expectedFrame0Hash, frame0Hash);
        Assert.Equal(expectedFrameNHash, frameNHash);
    }

    /// <summary>
    /// Attribution probe for the three re-recorded digests: the same open and the same 120-frame play
    /// input, with only the semi-transparency blend of <see cref="LevelScene.Tri"/> switched off. The
    /// expected values are the digests of <c>aa1d386</c>, the commit before the blend landed; zone 0
    /// frame 120 is the same in both runs. Because every other stage (terrain, mech, unit heading,
    /// sorting, projection) is untouched by the switch, this pins the whole digest delta on the blend.
    /// </summary>
    [ConftTheory]
    [InlineData(0, "CONFT.MIS",
        "D756CAE99866A944B67649D9DB0F61DA0AE591A0E967AFFEA8BBFC450ECE0452",
        "4A9D690FDFE8E795ADD89CC1AB591C8AB2BF29FEC848BD93C777A55C25466982")]
    [InlineData(3, "OVMP.MIS",
        "0F27FE1D8D70AE6BAC3E48BFB7DC2837495F8E9F6EFD90AFDE25F8656EFF36E6",
        "3663C781D115AB48AB4A01EBB85A0CF9483466A3B0C01723DB45243DC969FA86")]
    public void WithoutTheSemiBlendTheSameReplayReproducesThePreBlendDigests(
        int zone, string expectedMission, string expectedFrame0Hash, string expectedFrameNHash)
    {
        var (flow, level) = OpenLevel(zone, expectedMission);
        level.SemiBlendDisabled = true;

        level.Render(flow.Camera);
        Assert.Equal(expectedFrame0Hash, Sha256Hex(level.Rgb));

        Replay(flow);
        level.Render(flow.Camera);
        Assert.Equal(expectedFrameNHash, Sha256Hex(level.Rgb));
    }

    [ConftFact]
    public void TriSamples4Bit8BitAnd15BitTexelsIncludingNibbleByteOrderAndFallback()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!);
        var vram = scene.Vram;
        const uint neutral = 0x808080;
        // Power-of-two area (4*4 = 16) makes inv = 1/16 exact in IEEE-754 so b0+b1+b2 == 1.0.
        uint p0 = PackXy(10, 10), p1 = PackXy(14, 10), p2 = PackXy(10, 14);

        // Distinct 5-bit colours for palette entries 1..4; entry 0 is transparent (0x0000).
        ushort c1 = Abgr1555(31, 0, 0);
        ushort c2 = Abgr1555(0, 31, 0);
        ushort c3 = Abgr1555(0, 0, 31);
        ushort c4 = Abgr1555(24, 16, 8);
        var expected = new[]
        {
            (Expand5(31), Expand5(0), Expand5(0)),
            (Expand5(0), Expand5(31), Expand5(0)),
            (Expand5(0), Expand5(0), Expand5(31)),
            (Expand5(24), Expand5(16), Expand5(8)),
        };

        // 1) 4-bit CLUT (depthBits = 0, fastVram = true): pack nibbles 1, 2, 3, 4 into one VRAM cell.
        ushort clut4 = PackClut(clutXSlot: 1, clutY: 480); // clutX = 16, clutY = 480
        WriteVram(vram, 16, 480, 0x0000, c1, c2, c3, c4);
        ushort tpage4 = PackTpage(pageXSlot: 2, pageYBit: 0, abr: 0, depthBits: 0); // pageX = 128, pageY = 0
        WriteVram(vram, 128, 0, unchecked((ushort)(1 | (2 << 4) | (3 << 8) | (4 << 12))));
        for (int tx = 0; tx < 4; tx++)
        {
            Array.Clear(scene.Rgb);
            uint uv = PackUv(tx, 0);
            scene.Tri(p0, uv, neutral, p1, uv, neutral, p2, uv, neutral, tpage4, clut4, semi: false);
            Assert.Equal(expected[tx], Pixel(scene.Rgb, 11, 11));
        }

        // 2) 8-bit CLUT (depthBits = 1, fastVram = true): pack low byte 1, high byte 2 into one VRAM cell.
        ushort clut8 = PackClut(clutXSlot: 2, clutY: 481); // clutX = 32, clutY = 481
        WriteVram(vram, 32, 481, 0x0000, c1, c2);
        ushort tpage8Fast = PackTpage(pageXSlot: 4, pageYBit: 0, abr: 0, depthBits: 1); // pageX = 256 (256+127 < 1024)
        WriteVram(vram, 256, 0, unchecked((ushort)(1 | (2 << 8))));
        for (int tx = 0; tx < 2; tx++)
        {
            Array.Clear(scene.Rgb);
            uint uv = PackUv(tx, 0);
            scene.Tri(p0, uv, neutral, p1, uv, neutral, p2, uv, neutral, tpage8Fast, clut8, semi: false);
            Assert.Equal(expected[tx], Pixel(scene.Rgb, 11, 11));
        }

        // 3) 8-bit CLUT (depthBits = 1, fastVram = false fallback: pageX = 15*64 = 960, 960+127 >= 1024).
        ushort tpage8Fallback = PackTpage(pageXSlot: 15, pageYBit: 0, abr: 0, depthBits: 1);
        WriteVram(vram, 960, 0, unchecked((ushort)(2 | (1 << 8))));
        Array.Clear(scene.Rgb);
        scene.Tri(p0, PackUv(0, 0), neutral, p1, PackUv(0, 0), neutral, p2, PackUv(0, 0), neutral, tpage8Fallback, clut8, semi: false);
        Assert.Equal(expected[1], Pixel(scene.Rgb, 11, 11));
        Array.Clear(scene.Rgb);
        scene.Tri(p0, PackUv(1, 0), neutral, p1, PackUv(1, 0), neutral, p2, PackUv(1, 0), neutral, tpage8Fallback, clut8, semi: false);
        Assert.Equal(expected[0], Pixel(scene.Rgb, 11, 11));

        // 4) 15-bit direct (depthBits = 2 fastVram = true, depthBits = 3 fastVram = false).
        ushort tpage15Fast = PackTpage(pageXSlot: 6, pageYBit: 0, abr: 0, depthBits: 2); // pageX = 384 (384+255 < 1024)
        WriteVram(vram, 384, 0, c3, c4);
        Array.Clear(scene.Rgb);
        scene.Tri(p0, PackUv(0, 0), neutral, p1, PackUv(0, 0), neutral, p2, PackUv(0, 0), neutral, tpage15Fast, 0, semi: false);
        Assert.Equal(expected[2], Pixel(scene.Rgb, 11, 11));
        Array.Clear(scene.Rgb);
        scene.Tri(p0, PackUv(1, 0), neutral, p1, PackUv(1, 0), neutral, p2, PackUv(1, 0), neutral, tpage15Fast, 0, semi: false);
        Assert.Equal(expected[3], Pixel(scene.Rgb, 11, 11));

        ushort tpage15Fallback = PackTpage(pageXSlot: 13, pageYBit: 0, abr: 0, depthBits: 3); // pageX = 832 (832+255 >= 1024)
        WriteVram(vram, 832, 0, c4);
        Array.Clear(scene.Rgb);
        // Also exercise reversed winding (negative area) on the fallback path.
        scene.Tri(p0, PackUv(0, 0), neutral, p2, PackUv(0, 0), neutral, p1, PackUv(0, 0), neutral, tpage15Fallback, 0, semi: false);
        Assert.Equal(expected[3], Pixel(scene.Rgb, 11, 11));
    }

    [ConftFact]
    public void TriSemiTransparencyModesAbr0To3AndTexelZeroTransparency()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!);
        var vram = scene.Vram;
        const uint neutral = 0x808080;
        // Power-of-two area (4*4 = 16) makes inv = 1/16 exact in IEEE-754 so b0+b1+b2 == 1.0.
        uint p0 = PackXy(20, 20), p1 = PackXy(24, 20), p2 = PackXy(20, 24);

        // Background colour chosen to test clamping on both add (255) and subtract (0) channels.
        const byte bgR = 120, bgG = 60, bgB = 220;
        var bg = (bgR, bgG, bgB);

        // Texel 0x0000 is fully transparent across 4-bit, 8-bit, and 15-bit regardless of semi flag.
        ushort clut = PackClut(clutXSlot: 3, clutY: 482); // clutX = 48, clutY = 482
        WriteVram(vram, 48, 482, 0x0000);
        WriteVram(vram, 192, 0, 0x0000); // pageXSlot = 3 -> pageX = 192
        foreach (int depth in new[] { 0, 1, 2 })
        {
            ushort tpageZero = PackTpage(pageXSlot: 3, pageYBit: 0, abr: 1, depthBits: depth);
            foreach (bool semi in new[] { false, true })
            {
                FillRgb(scene.Rgb, bgR, bgG, bgB);
                scene.Tri(p0, PackUv(0, 0), neutral, p1, PackUv(0, 0), neutral, p2, PackUv(0, 0), neutral, tpageZero, clut, semi);
                Assert.Equal(bg, Pixel(scene.Rgb, 21, 21));
            }
        }

        // Foreground texel: r5=20 (165), g5=12 (99), b5=16 (132).
        int r5 = 20, g5 = 12, b5 = 16;
        byte fgR = Expand5(r5), fgG = Expand5(g5), fgB = Expand5(b5);
        ushort stpTexel = Abgr1555(r5, g5, b5, stp: true);
        ushort opaqueTexel = Abgr1555(r5, g5, b5, stp: false);
        WriteVram(vram, 256, 1, stpTexel, opaqueTexel); // pageXSlot = 4 -> pageX = 256, y = 1

        // All four ABR modes with semi = true and STP bit set.
        var expectedByAbr = new (byte R, byte G, byte B)[]
        {
            ((byte)((bgR + fgR) >> 1), (byte)((bgG + fgG) >> 1), (byte)((bgB + fgB) >> 1)),                       // abr 0: 50% + 50%
            ((byte)Math.Min(255, bgR + fgR), (byte)Math.Min(255, bgG + fgG), (byte)Math.Min(255, bgB + fgB)),     // abr 1: bg + fg (clamps R & B to 255)
            ((byte)Math.Max(0, bgR - fgR), (byte)Math.Max(0, bgG - fgG), (byte)Math.Max(0, bgB - fgB)),           // abr 2: bg - fg (clamps R & G to 0)
            ((byte)Math.Min(255, bgR + (fgR >> 2)), (byte)Math.Min(255, bgG + (fgG >> 2)), (byte)Math.Min(255, bgB + (fgB >> 2))), // abr 3: bg + fg/4
        };
        for (int abr = 0; abr < 4; abr++)
        {
            FillRgb(scene.Rgb, bgR, bgG, bgB);
            ushort tpage = PackTpage(pageXSlot: 4, pageYBit: 0, abr: abr, depthBits: 2);
            scene.Tri(p0, PackUv(0, 1), neutral, p1, PackUv(0, 1), neutral, p2, PackUv(0, 1), neutral, tpage, 0, semi: true);
            Assert.Equal(expectedByAbr[abr], Pixel(scene.Rgb, 21, 21));
        }

        // STP bit clear with semi = true writes opaque foreground (no blend).
        FillRgb(scene.Rgb, bgR, bgG, bgB);
        ushort tpageAbr0 = PackTpage(pageXSlot: 4, pageYBit: 0, abr: 0, depthBits: 2);
        scene.Tri(p0, PackUv(1, 1), neutral, p1, PackUv(1, 1), neutral, p2, PackUv(1, 1), neutral, tpageAbr0, 0, semi: true);
        Assert.Equal((fgR, fgG, fgB), Pixel(scene.Rgb, 21, 21));

        // semi = false with STP bit set writes opaque foreground (no blend).
        FillRgb(scene.Rgb, bgR, bgG, bgB);
        scene.Tri(p0, PackUv(0, 1), neutral, p1, PackUv(0, 1), neutral, p2, PackUv(0, 1), neutral, tpageAbr0, 0, semi: false);
        Assert.Equal((fgR, fgG, fgB), Pixel(scene.Rgb, 21, 21));

        // Non-neutral vertex colour modulation (0x4080FF = 0.5x R, 1.0x G, ~2.0x B clamped at 255).
        FillRgb(scene.Rgb, 0, 0, 0);
        const uint mod = 0x4080FF;
        scene.Tri(p0, PackUv(1, 1), mod, p1, PackUv(1, 1), mod, p2, PackUv(1, 1), mod, tpageAbr0, 0, semi: false);
        Assert.Equal(
            ((byte)Math.Min(255, (int)(fgR * 0x40 / 128.0)),
             (byte)Math.Min(255, (int)(fgG * 0x80 / 128.0)),
             (byte)Math.Min(255, (int)(fgB * 0xFF / 128.0))),
            Pixel(scene.Rgb, 21, 21));
    }

    [ConftFact]
    public void FlatFaceFillsTrianglesAndQuadsInBothWindingsAndClipsToViewport()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!);
        Array.Clear(scene.Rgb);

        // Type-3 triangle with positive area, partially outside left/top viewport edges.
        var tri = new TexturedFace(3, 0, new Work(
            unchecked((int)PackXy(-4, -4)),
            unchecked((int)PackXy(8, -4)),
            unchecked((int)PackXy(-4, 8)),
            0, 100), 0);
        scene.FlatFace(tri, 0x123456);
        Assert.Equal((0x12, 0x34, 0x56), Pixel(scene.Rgb, 0, 0));
        Assert.Equal((0x12, 0x34, 0x56), Pixel(scene.Rgb, 1, 1));
        Assert.Equal((0, 0, 0), Pixel(scene.Rgb, 10, 10));

        // Type-4 quad with reversed (negative) winding in the interior.
        var quad = new TexturedFace(4, 0, new Work(
            unchecked((int)PackXy(40, 40)),
            unchecked((int)PackXy(40, 50)),
            unchecked((int)PackXy(50, 40)),
            unchecked((int)PackXy(50, 50)), 100), 0);
        scene.FlatFace(quad, 0xABCDEF);
        Assert.Equal((0xAB, 0xCD, 0xEF), Pixel(scene.Rgb, 42, 42));
        Assert.Equal((0xAB, 0xCD, 0xEF), Pixel(scene.Rgb, 48, 48));
        Assert.Equal((0, 0, 0), Pixel(scene.Rgb, 52, 52));
    }

    [ConftFact]
    public void QueueModelAndWalkTexturedRejectOutOfRangeVertexIndexAfterLargerModel()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!);
        // First render populates _projectedScratch with many valid vertices (mech/map models).
        scene.Render(new LevelCamera());

        // Synthetic 3-vertex model whose triangle references vertex index 5 (outside verts.Count == 3,
        // but inside the grown _projectedScratch array). Must throw IndexOutOfRangeException instead
        // of silently reading stale ScreenVertex entries from the previously projected larger model.
        var badModel = new Model { Id = -9999 };
        var verts = new List<Vertex>
        {
            new(0, 0, 100),
            new(50, 0, 100),
            new(0, 50, 100),
        };
        badModel.Vertices[1] = verts;
        // Flags1 = 0x03 -> PrimitiveType 3 (triangle), PartGroup 0 (mask bit 0).
        badModel.Polygons.Add(new Polygon(0x03, 0x03, 0, V0: 0, V1: 1, V2: 5, V3: 0, 0, 0, 0, 0));

        var identity = new Gte.Mat(new[]
        {
            0x10000, 0, 0,
            0, 0x10000, 0,
            0, 0, 0x10000,
        }, 0, 0, 0x4000);
        int drawOrder = 0;

        Assert.Throws<IndexOutOfRangeException>(() =>
            scene.QueueModel(badModel, verts, 0, identity, 0xFF, ref drawOrder));

        // Direct bounded-span check on ModelRender.WalkTextured with a reused scratch buffer.
        var scratch = new ScreenVertex[10];
        for (int i = 0; i < scratch.Length; i++)
            scratch[i] = new ScreenVertex(unchecked((int)PackXy(100 + i * 10, 100 + (i & 1) * 10)), 200, 0xC0);
        var faces = new List<TexturedFace>();
        Assert.Throws<IndexOutOfRangeException>(() =>
            ModelRender.WalkTextured(badModel.Polygons, scratch.AsSpan(0, verts.Count), Array.Empty<short>(), 0xFF, 0, faces));
    }
}
