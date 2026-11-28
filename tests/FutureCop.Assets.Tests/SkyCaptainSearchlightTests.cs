using System.Buffers.Binary;
using FutureCop.Assets;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Tests for the searchlight cone of the Sky Captain (type-0x25 strategic drone, model 56 of
/// CONFT.MIS). Retail emits those faces with the semi-transparency bit and lets the texture page's
/// ABR mode blend them: func_80015F38 turns flag byte bit 1 into the packet's semi bit
/// (<c>tmp = (fl &amp; 2) &lt;&lt; 24</c>, 80015F38.c:73), and func_800131A0 produces that bit from the
/// on-disc byte (800131A0.c:136-155). The cone's faces sit on page 0x00BE, whose ABR bits
/// (bits 5..6) are 1, i.e. additive, and whose texels are a dark blue gradient. Drawn opaque they
/// painted a black wedge over the level instead of adding their gradient to it.
/// </summary>
public sealed class SkyCaptainSearchlightTests
{
    private static uint PackXy(int x, int y) =>
        ((uint)(ushort)(short)y << 16) | (ushort)(short)x;

    private static ushort Abgr1555(int r5, int g5, int b5, bool stp = false) =>
        (ushort)((stp ? 0x8000 : 0) | ((b5 & 0x1F) << 10) | ((g5 & 0x1F) << 5) | (r5 & 0x1F));

    private static byte Expand5(int c5) => (byte)((c5 << 3) | (c5 >> 2));

    private static ushort PackTpage(int pageXSlot, int pageYBit, int abr, int depthBits) =>
        (ushort)((pageXSlot & 0x0F) | ((pageYBit & 1) << 4) | ((abr & 3) << 5) | ((depthBits & 3) << 7));

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

    /// <summary>Loads CONFT.MIS and returns the Sky Captain's model (id 56).</summary>
    private static Model LoadSkyCaptainModel(out LevelScene scene)
    {
        scene = new LevelScene(LevelSceneRetirementTests.FindMission()!);
        return scene.ModelById(56) ?? throw new InvalidOperationException("CONFT.MIS has no model 56");
    }

    [ConftFact]
    public void SearchlightFacesCarryTheSemiFlagAndSitOnTheAdditivePage()
    {
        Model model = LoadSkyCaptainModel(out var scene);

        // On-disc flag bit 3 becomes loaded-flags bit 1 (func_800131A0:136-155).
        var semiParts = model.Polygons.Where(p => (ModelRender.LoadedFlags(p) & 2) != 0).ToList();
        Assert.NotEmpty(semiParts);

        // Walk with degenerate screen corners (accepted, zero area) to see which parts the retail
        // polygon walk emits with the semi flag and which 3DTL record each of them points at.
        var vtx = new ScreenVertex[512];
        for (int i = 0; i < vtx.Length; i++)
            vtx[i] = new ScreenVertex(unchecked((int)PackXy(16, 16)), 0x1000, 0xC0); // func_80015B68 requires clip == 0xC0
        var widths = model.Widths.TryGetValue(1, out var w) ? w.ToArray() : Array.Empty<short>();
        var faces = ModelRender.WalkTextured(model.Polygons, vtx, widths, 0xFF, 0);

        Assert.Equal(semiParts.Count, faces.Count(f => f.Semi));

        // The searchlight cone is the only additive geometry of the model: three texture records
        // (0x02F0, 0x0300, 0x0310) on page 0x00BE, ABR bits 5..6 = 1 (additive), 8-bit CLUT 0x7F58.
        // Every other semi part (canopy, engine glow) sits on the average-blend page 0x008E.
        var textures = model.Textures.GroupBy(t => (int)t.Offset).ToDictionary(g => g.Key, g => g.First());
        var semiFaces = faces.Where(f => f.Semi).ToList();
        Assert.NotEmpty(semiFaces);
        var coneFaces = semiFaces.Where(f => textures[f.TextureOffset].Tpage == 0x00BE).ToList();
        Assert.Equal(8, coneFaces.Count);
        Assert.Equal(new[] { 0x02F0, 0x0300, 0x0310 }, coneFaces.Select(f => (int)f.TextureOffset).Distinct().OrderBy(o => o));
        foreach (var face in coneFaces)
        {
            var rec = textures[face.TextureOffset];
            Assert.Equal(1, (rec.Tpage >> 5) & 3);
            Assert.Equal(0x7F58, rec.Clut);
        }
        Assert.All(semiFaces.Except(coneFaces), f => Assert.Equal(0, (textures[f.TextureOffset].Tpage >> 5) & 3));

        // The page really holds the cone's gradient: STP texels over the record's UV rectangle,
        // all of them dark (additive over the ground adds a faint blue veil, never white).
        var vram = scene.Vram;
        int stpTexels = 0, maxLuma = 0;
        foreach (var face in coneFaces)
        {
            var rec = textures[face.TextureOffset];
            int u0 = (int)(rec.Uv0 & 0xFF), v0 = (int)((rec.Uv0 >> 8) & 0xFF);
            int u1 = (int)(rec.Uv1 & 0xFF), v3 = (int)((rec.Uv1 >> 24) & 0xFF);
            for (int v = Math.Min(v0, v3); v <= Math.Max(v0, v3); v++)
                for (int u = Math.Min(u0, u1); u <= Math.Max(u0, u1); u++)
                {
                    ushort raw = vram.SampleRaw(rec.Tpage, rec.Clut, u, v);
                    if ((raw & 0x8000) == 0) continue;
                    stpTexels++;
                    var (r, g, b, _) = Vram.Abgr1555ToRgb(raw);
                    maxLuma = Math.Max(maxLuma, Math.Max(r, Math.Max(g, b)));
                }
        }
        Assert.True(stpTexels > 0, "searchlight texture has no semi-transparent texels");
        Assert.InRange(maxLuma, 1, 64);
    }

    [ConftFact]
    public void ModelFaceBlendsWithTheAbrModeOfItsTexturePage()
    {
        Model model = LoadSkyCaptainModel(out var scene);
        scene.EnableObjectives(); // loads the mission textures, so model 56's pages are resident
        var vram = scene.Vram;

        // Page slot 4 (x = 256 in VRAM), 15-bit direct colour, one STP texel at page offset (1, 1).
        const int fg5r = 6, fg5g = 7, fg5b = 8;
        WriteVram(vram, 256 + 1, 1, Abgr1555(fg5r, fg5g, fg5b, stp: true));
        byte fgR = Expand5(fg5r), fgG = Expand5(fg5g), fgB = Expand5(fg5b);

        var synthetic = new Model { Id = 987654 };
        uint uv = (uint)(1 | (1 << 8) | (1 << 16) | (1 << 24)); // all four corners at (1, 1)
        synthetic.Textures.Add(new TextureRecord(
            Offset: 0, Size: 16, TypeRaw: 2, TypeLoaded: 2, Rgb: 0x808080,
            Uv0: uv, Uv1: uv, TexIndex: 0, Clut: 0,
            Tpage: PackTpage(pageXSlot: 4, pageYBit: 0, abr: 1, depthBits: 2), SemiTransMode: 1));

        // Triangle covering the whole screen corner the test samples in the middle.
        var work = new Work(
            unchecked((int)PackXy(0, 0)),
            unchecked((int)PackXy(80, 0)),
            unchecked((int)PackXy(0, 80)),
            unchecked((int)PackXy(80, 80)),
            100);
        const int px = 21, py = 21;

        // Semi set: page ABR 1 (additive, func_80075A84) adds the texel to the frame's pixel.
        const byte bgR = 40, bgG = 50, bgB = 60;
        FillRgb(scene.Rgb, bgR, bgG, bgB);
        scene.DrawModelFace(new TexturedFace(3, 0, work, 0, Semi: true), synthetic);
        Assert.Equal(
            ((byte)Math.Min(255, bgR + fgR), (byte)Math.Min(255, bgG + fgG), (byte)Math.Min(255, bgB + fgB)),
            Pixel(scene.Rgb, px, py));

        // Semi clear: the same texel replaces the pixel, which is the black wedge the port drew.
        FillRgb(scene.Rgb, bgR, bgG, bgB);
        scene.DrawModelFace(new TexturedFace(3, 0, work, 0, Semi: false), synthetic);
        Assert.Equal((fgR, fgG, fgB), Pixel(scene.Rgb, px, py));
    }
}
