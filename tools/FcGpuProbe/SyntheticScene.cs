using System.Buffers.Binary;
using FutureCop.Assets;
using FutureCop.Gpu;

namespace FcGpuProbe;

/// <summary>
/// The fixed, deterministic scene the probe renders with both paths: one procedurally generated
/// 15-bit texture page in a fake VRAM plus five primitives: one opaque modulated triangle and one
/// semi-transparent triangle per ABR mode (0 half, 1 add, 2 subtract, 3 quarter add).
/// </summary>
/// <remarks>
/// Nothing here reads retail data: the page is generated from the texel coordinates, so the probe
/// runs anywhere and the same bytes feed the GL texture and the software reference. The page
/// deliberately contains a smooth gradient (half-texel sampling errors show up), 4-texel stripes
/// (nearest-neighbour errors show up), a band of raw 0x0000 (transparent) and a block of raw
/// 0x8000 (STP with a black colour, which halves the destination under ABR0).
/// </remarks>
internal static class SyntheticScene
{
    /// <summary>Texture page base: (0,0), 15-bit direct.</summary>
    public const ushort PageTpage = 0x0100; // depth 2, page 0/0, ABR 0

    /// <summary>Same page, ABR 2 (subtract). ABR is tpage bits 5..6, so one step is 0x20.</summary>
    public const ushort SubtractTpage = 0x0140;

    public const ushort Clut = 0x0000;

    /// <summary>Same page, ABR 1 (add).</summary>
    public const ushort AddTpage = 0x0120;

    /// <summary>Same page, ABR 3 (quarter add).</summary>
    public const ushort QuarterAddTpage = 0x0160;

    public static readonly TextureKey OpaqueKey = new(PageTpage, Clut);
    public static readonly TextureKey SemiKey = new(PageTpage, Clut);
    public static readonly TextureKey SubtractKey = new(SubtractTpage, Clut);
    public static readonly TextureKey AddKey = new(AddTpage, Clut);
    public static readonly TextureKey QuarterAddKey = new(QuarterAddTpage, Clut);

    /// <summary>Generated 256x256 ABGR1555 page as raw texel words.</summary>
    public static ushort[] BuildPage()
    {
        var page = new ushort[256 * 256];
        for (int v = 0; v < 256; v++)
        {
            for (int u = 0; u < 256; u++)
            {
                ushort raw;
                if ((((u >> 4) + (v >> 4)) & 1) == 0)
                {
                    // Smooth gradient: r tracks u, g tracks v, b tracks u+v.
                    int r5 = 2 + (u * 26) / 256;
                    int g5 = 2 + (v * 26) / 256;
                    int b5 = 4 + ((u + v) * 22) / 512;
                    raw = (ushort)(r5 | (g5 << 5) | (b5 << 10));
                }
                else
                {
                    // 4-texel checker: hard edges for nearest sampling.
                    int level = ((((u >> 2) + (v >> 2)) & 1) == 0) ? 24 : 8;
                    raw = (ushort)(level | (level << 5) | ((level - 2) << 10));
                }

                // Vertical STP band 96..119: semi-transparent texels of the ordinary colours.
                if (u is >= 96 and < 120) raw |= 0x8000;

                // Diagonal STP stripe.
                if (((u + v) & 63) == 0) raw |= 0x8000;

                // Fully transparent band 60..75 (raw 0x0000 wins over the STP stripe).
                if (u is >= 60 and < 76) raw = 0;

                page[v * 256 + u] = raw;
            }
        }

        // Black semi-transparent block: ABR0 must halve the destination there.
        for (int v = 130; v < 146; v++)
            for (int u = 200; u < 216; u++)
                page[v * 256 + u] = 0x8000;

        return page;
    }

    /// <summary>Loads <see cref="BuildPage"/> into a fresh VRAM at page (0,0).</summary>
    public static Vram BuildVram()
    {
        ushort[] page = BuildPage();
        byte[] bytes = new byte[page.Length * 2];
        for (int i = 0; i < page.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2, 2), page[i]);

        var vram = new Vram();
        vram.LoadRect(0, 0, 256, 256, bytes);
        return vram;
    }

    /// <summary>
    /// The fixed primitive list, in draw order. <paramref name="offset"/> is added to every screen
    /// coordinate; +0.5 reproduces the old "fragment centre = x+0.5" sampling so the coverage-only
    /// difference can be measured on its own.
    /// </summary>
    public static Ps1Primitive[] Primitives(float offset = 0f)
    {
        float o = offset;
        // 1: opaque, textured, per-vertex modulation (255/64/192 etc. exercises the /128 clamp).
        var a = new Ps1Vertex(24 + o, 18 + o, 8, 8, 255, 255, 255);
        var b = new Ps1Vertex(200 + o, 42 + o, 112, 16, 64, 192, 32);
        var c = new Ps1Vertex(64 + o, 196 + o, 24, 136, 192, 64, 255);
        // 2: semi ABR0 over the gradient, the transparent band and the STP band, colour 128.
        var d = new Ps1Vertex(96 + o, 96 + o, 56, 88, 128, 128, 128);
        var e = new Ps1Vertex(300 + o, 120 + o, 196, 100, 128, 128, 128);
        var f = new Ps1Vertex(150 + o, 226 + o, 72, 180, 128, 128, 128);
        // 3: semi ABR2 (subtract) with a non-neutral modulation colour.
        var g = new Ps1Vertex(40 + o, 120 + o, 16, 72, 96, 160, 64);
        var h = new Ps1Vertex(260 + o, 150 + o, 160, 96, 96, 160, 64);
        var i = new Ps1Vertex(110 + o, 232 + o, 48, 160, 96, 160, 64);
        // 4: semi ABR1 (add), UVs inside the STP band 96..119 so the semi pass covers the triangle.
        var j = new Ps1Vertex(70 + o, 50 + o, 100, 24, 128, 128, 128);
        var k = new Ps1Vertex(150 + o, 70 + o, 114, 30, 128, 128, 128);
        var l = new Ps1Vertex(100 + o, 100 + o, 102, 40, 128, 128, 128);
        // 5: semi ABR3 (quarter add), same STP band.
        var m = new Ps1Vertex(60 + o, 110 + o, 100, 64, 128, 128, 128);
        var n = new Ps1Vertex(120 + o, 125 + o, 116, 72, 128, 128, 128);
        var q = new Ps1Vertex(80 + o, 150 + o, 104, 84, 128, 128, 128);

        return new[]
        {
            new Ps1Primitive(OpaqueKey, false, in a, in b, in c, 0),
            new Ps1Primitive(SemiKey, true, in d, in e, in f, 1),
            new Ps1Primitive(SubtractKey, true, in g, in h, in i, 2),
            new Ps1Primitive(AddKey, true, in j, in k, in l, 3),
            new Ps1Primitive(QuarterAddKey, true, in m, in n, in q, 4),
        };
    }

    /// <summary>
    /// Diagnostic page: R = u &gt;&gt; 3, G = v &gt;&gt; 3, B = 31, so a rendered pixel decodes straight
    /// back to the texel coordinate the sampler used. No STP, no transparent texels.
    /// </summary>
    public static ushort[] BuildUvPage()
    {
        var page = new ushort[256 * 256];
        for (int v = 0; v < 256; v++)
        {
            for (int u = 0; u < 256; u++)
            {
                int r5 = u >> 3;
                int g5 = v >> 3;
                page[v * 256 + u] = (ushort)(r5 | (g5 << 5) | (31 << 10));
            }
        }
        return page;
    }

    /// <summary>VRAM holding <see cref="BuildUvPage"/> at page (0,0).</summary>
    public static Vram BuildUvVram()
    {
        ushort[] page = BuildUvPage();
        byte[] bytes = new byte[page.Length * 2];
        for (int i = 0; i < page.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2, 2), page[i]);
        var vram = new Vram();
        vram.LoadRect(0, 0, 256, 256, bytes);
        return vram;
    }

    /// <summary>The opaque modulated triangle alone, with a neutral modulation colour.</summary>
    public static Ps1Primitive[] UvPrimitives(float offset = 0f)
    {
        float o = offset;
        var a = new Ps1Vertex(24 + o, 18 + o, 8, 8, 128, 128, 128);
        var b = new Ps1Vertex(200 + o, 42 + o, 112, 16, 128, 128, 128);
        var c = new Ps1Vertex(64 + o, 196 + o, 24, 136, 128, 128, 128);
        return new[] { new Ps1Primitive(OpaqueKey, false, in a, in b, in c, 0) };
    }

    /// <summary>Uploads every page the primitive list needs into <paramref name="renderer"/>.</summary>
    public static void UploadTextures(GlEs2Renderer renderer, Vram vram)
    {
        var cache = new VramTextureCache(vram);
        foreach (Ps1Primitive primitive in Primitives())
        {
            if (!renderer.HasTexture(primitive.Key)) cache.Upload(renderer, primitive.Key);
        }
    }
}
