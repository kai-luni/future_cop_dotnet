using FutureCop.Assets;
using Xunit;

namespace FutureCop.Gpu.Tests;

public class VramTextureCacheTests
{
    // 4-bit page at tpage = 1 (pageX = 64, pageY = 0) and a CLUT at (0, 0).
    private const ushort Tpage = 1;
    private const ushort Clut = 0;

    private static Vram BuildVram()
    {
        var vram = new Vram();
        // CLUT entries 0..3.
        vram.LoadRect(0, 0, 4, 1, new byte[]
        {
            0x00, 0x00, // index 0: raw 0x0000 -> transparent
            0xFF, 0x7F, // index 1: raw 0x7FFF -> white, alpha 255
            0x1F, 0x80, // index 2: raw 0x801F -> red with STP -> alpha 128
            0x00, 0x7C, // index 3: raw 0x7C00 -> blue, alpha 255
        });
        // First page cell: texel indices 0,1,2,3 in the four nibbles.
        vram.LoadRect(64, 0, 1, 1, new byte[] { 0x10, 0x32 });
        return vram;
    }

    [Fact]
    public void DecodeMapsRgbAndTransparencyFlags()
    {
        var cache = new VramTextureCache(BuildVram());
        ReadOnlySpan<byte> rgba = cache.GetOrDecode(new TextureKey(Tpage, Clut));

        Assert.Equal(VramTextureCache.BytesPerTexture, rgba.Length);
        Assert.Equal((0, 0, 0, 0), Pixel(rgba, 0, 0));       // raw 0x0000
        Assert.Equal((255, 255, 255, 255), Pixel(rgba, 1, 0)); // STP bit clear
        Assert.Equal((255, 0, 0, 128), Pixel(rgba, 2, 0));     // STP bit set
        Assert.Equal((0, 0, 255, 255), Pixel(rgba, 3, 0));
        // Rest of the 256x256 page is uninitialised VRAM: index 0 of the CLUT, i.e. transparent.
        Assert.Equal((0, 0, 0, 0), Pixel(rgba, 100, 100));
    }

    [Fact]
    public void DecodeIsCachedPerKey()
    {
        var cache = new VramTextureCache(BuildVram());
        var key = new TextureKey(Tpage, Clut);

        cache.GetOrDecode(key);
        cache.GetOrDecode(key);
        Assert.Equal(1, cache.Count);

        cache.Invalidate(key);
        Assert.Equal(0, cache.Count);

        cache.GetOrDecode(key);
        cache.GetOrDecode(new TextureKey(2, 0));
        Assert.Equal(2, cache.Count);

        cache.Invalidate();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void UploadReachesTheRenderer()
    {
        var gl = new FakeGpuApi();
        using var renderer = new GlEs2Renderer(gl);
        var cache = new VramTextureCache(BuildVram());
        var key = new TextureKey(Tpage, Clut);

        Assert.False(renderer.HasTexture(key));
        cache.Upload(renderer, key);

        Assert.True(renderer.HasTexture(key));
        Assert.Contains(gl.Calls, c => c.StartsWith("TexImage2D(3553,0,6408,256,256,", StringComparison.Ordinal));
        Assert.Contains("TexParameteri(3553,10241,9728)", gl.Calls);  // MIN_FILTER = NEAREST
        Assert.Contains("TexParameteri(3553,10240,9728)", gl.Calls);  // MAG_FILTER = NEAREST
        Assert.Contains("TexParameteri(3553,10242,33071)", gl.Calls); // WRAP_S = CLAMP_TO_EDGE
        Assert.Contains("TexParameteri(3553,10243,33071)", gl.Calls); // WRAP_T = CLAMP_TO_EDGE
    }

    [Fact]
    public void DecodeRejectsShortDestination()
    {
        var vram = BuildVram();
        Assert.Throws<ArgumentException>(() =>
        {
            Span<byte> tooSmall = stackalloc byte[16];
            VramTextureCache.Decode(vram, new TextureKey(Tpage, Clut), tooSmall);
        });
    }

    private static (int R, int G, int B, int A) Pixel(ReadOnlySpan<byte> rgba, int u, int v)
    {
        int o = (v * VramTextureCache.TextureSize + u) * 4;
        return (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]);
    }
}
