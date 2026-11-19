using Xunit;

namespace FutureCop.Gpu.Tests;

/// <summary>
/// Checks the written specification, not the production renderer: every expected pixel below is
/// derived by hand from the formulas in LevelScene.Tri (barycentrics at the integer pixel
/// coordinate, affine UV, min(255, texel * colour / 128), the three discard passes and the four
/// ABR modes).
/// </summary>
public class ReferenceRasterizerTests
{
    /// <summary>
    /// Test texture. Cell = 8x8 texels, parity of (u/8 + v/8):
    /// even -> (192,96,64,255), odd -> (64,192,128,128) i.e. STP set,
    /// and the corner u &gt;= 32, v &gt;= 24 is raw 0x0000 (fully transparent).
    /// </summary>
    private static (byte R, byte G, byte B, byte A) Texel(int u, int v)
    {
        if (u >= 32 && v >= 24) return (0, 0, 0, 0);
        int cell = ((u >> 3) + (v >> 3)) & 1;
        return cell == 0
            ? ((byte)192, (byte)96, (byte)64, (byte)255)
            : ((byte)64, (byte)192, (byte)128, (byte)128);
    }

    // Screen-space triangle covering the 4x4 pixel square (0,0)-(4,0)-(0,4).
    // Barycentric weights at the integer sample points used below (area = 16):
    //   (0,0): (1   , 0   , 0   )   (1,0): (0.75, 0.25, 0   )
    //   (2,0): (0.5 , 0.5 , 0   )   (3,0): (0.25, 0.75, 0   )
    //   (0,1): (0.75, 0   , 0.25)   (1,1): (0.5 , 0.25, 0.25)
    private static RefVertex V(float x, float y, float u, float v, float r, float g, float b) =>
        new(x, y, u, v, r, g, b);

    private static RefPrimitive Triangle(RefVertex a, RefVertex b, RefVertex c, bool semi = false, int abr = 0) =>
        new(a, b, c, abr, semi);

    /// <summary>A: (0,0) colour 128, B: (4,0) uv (64,32) colour 64, C: (0,4) colour 128.</summary>
    private static RefPrimitive ShadedTriangle(bool semi = false, int abr = 0) =>
        Triangle(
            V(0, 0, 0, 0, 128, 128, 128),
            V(4, 0, 64, 32, 64, 64, 64),
            V(0, 4, 0, 0, 128, 128, 128), semi, abr);

    [Fact]
    public void OpaqueTriangle_MatchesHandComputedPixels()
    {
        var target = new ReferenceRasterizer(Texel);
        target.Draw(ShadedTriangle());

        // (0,0): weights (1,0,0) -> u = v = 0, even cell (192,96,64);
        //        colour = 128 -> factor 1.0 -> texel passes through unchanged.
        Assert.Equal((192, 96, 64), Rgb(target, 0, 0));

        // (1,0): u = 0.25*64 = 16, v = 0.25*32 = 8 -> cell (2+1) = odd (64,192,128);
        //        colour = 0.75*128 + 0.25*64 = 112 -> 112/128 = 0.875
        //        64*0.875 = 56, 192*0.875 = 168, 128*0.875 = 112
        Assert.Equal((56, 168, 112), Rgb(target, 1, 0));

        // (2,0): u = 0.5*64 = 32, v = 0.5*32 = 16 -> cell (4+2) = even (192,96,64);
        //        colour = 0.5*128 + 0.5*64 = 96 -> 0.75
        //        192*0.75 = 144, 96*0.75 = 72, 64*0.75 = 48
        Assert.Equal((144, 72, 48), Rgb(target, 2, 0));

        // (3,0): u = 0.75*64 = 48, v = 0.75*32 = 24 -> inside the 0x0000 corner, discarded.
        Assert.Equal((0, 0, 0), Rgb(target, 3, 0));

        // (0,1): weights (0.75,0,0.25), both UVs (0,0) -> even cell; colour stays 128.
        Assert.Equal((192, 96, 64), Rgb(target, 0, 1));

        // (1,1): u = 0.25*64 = 16, v = 0.25*32 = 8 -> odd cell; colour 112 -> (56,168,112).
        Assert.Equal((56, 168, 112), Rgb(target, 1, 1));
    }

    [Fact]
    public void Modulation_BrightensAbove128AndSaturates()
    {
        var target = new ReferenceRasterizer(Texel);
        target.Draw(Triangle(
            V(0, 0, 0, 0, 200, 200, 200),
            V(4, 0, 64, 32, 200, 200, 200),
            V(0, 4, 0, 0, 200, 200, 200)));

        // factor = 200/128 = 1.5625, so a colour above 128 brightens the texel and the result
        // saturates at 255 - LevelScene.Tri computes min(255, texel * cm / 128).
        // (0,0): even cell (192,96,64) -> 300, 150, 100 -> (255,150,100)
        Assert.Equal((255, 150, 100), Rgb(target, 0, 0));
        // (1,0): odd cell (64,192,128) -> 100, 300, 200 -> (100,255,200)
        Assert.Equal((100, 255, 200), Rgb(target, 1, 0));
    }

    [Fact]
    public void TransparentTexel_IsDiscarded()
    {
        var target = new ReferenceRasterizer(Texel);
        // Every sample point lands in the raw 0x0000 corner.
        target.Draw(Triangle(
            V(0, 0, 64, 64, 128, 128, 128),
            V(4, 0, 64, 64, 128, 128, 128),
            V(0, 4, 64, 64, 128, 128, 128)));

        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)0), target.Get(0, 0));
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)0), target.Get(1, 0));
    }

    [Theory]
    // back = (56,168,112) from the shaded triangle, fg = (64,192,128) with texel alpha 128.
    [InlineData(0, 60, 180, 120)] // (back + fg) >> 1
    [InlineData(1, 120, 255, 240)] // min(255, back + fg); 168+192 = 360 -> 255
    [InlineData(2, 0, 0, 0)] // max(0, back - fg); all three channels clamp to 0
    [InlineData(3, 72, 216, 144)] // back + (fg >> 2): 56+16, 168+48, 112+32
    public void SemiPass_BlendsPerAbr(int abr, int r, int g, int b)
    {
        var target = new ReferenceRasterizer(Texel);
        target.Draw(ShadedTriangle());

        // Same geometry, uv (8,0) everywhere: the odd cell in texel row 0, i.e. (64,192,128) with
        // the STP bit set, so the semi pass touches every sample point.
        target.Draw(Triangle(
            V(0, 0, 8, 0, 128, 128, 128),
            V(4, 0, 8, 0, 128, 128, 128),
            V(0, 4, 8, 0, 128, 128, 128), semi: true, abr: abr));

        Assert.Equal((r, g, b), Rgb(target, 1, 0));
    }

    [Fact]
    public void SemiPass_BlendsOverExistingBackdropAtFirstPixel()
    {
        var target = new ReferenceRasterizer(Texel);
        target.Draw(ShadedTriangle());

        // uv (8,0) is the odd cell at every sample point, so the semi pass paints the whole triangle.
        target.Draw(Triangle(
            V(0, 0, 8, 0, 128, 128, 128),
            V(4, 0, 8, 0, 128, 128, 128),
            V(0, 4, 8, 0, 128, 128, 128), semi: true, abr: 0));

        // (0,0) back = (192,96,64); fg = (64,192,128) -> (256>>1, 288>>1, 192>>1) = (128,144,96)
        Assert.Equal((128, 144, 96), Rgb(target, 0, 0));
    }

    [Fact]
    public void SemiPrimitive_OpaqueTexel_IsDrawnInTheOpaquePass()
    {
        var target = new ReferenceRasterizer(Texel);
        target.Draw(ShadedTriangle());
        Assert.Equal((56, 168, 112), Rgb(target, 1, 0));

        // All UVs at (0,0): the even cell (192,96,64) with alpha 255. The semi pass discards it
        // (alpha > 0.75) and the opaque pass writes it unblended.
        target.Draw(Triangle(
            V(0, 0, 0, 0, 128, 128, 128),
            V(4, 0, 0, 0, 128, 128, 128),
            V(0, 4, 0, 0, 128, 128, 128), semi: true, abr: 1));

        Assert.Equal((192, 96, 64), Rgb(target, 1, 0));
    }

    [Fact]
    public void SemiPass_IgnoresTransparentTexels()
    {
        var target = new ReferenceRasterizer(Texel);
        target.Draw(Triangle(
            V(0, 0, 64, 64, 128, 128, 128),
            V(4, 0, 64, 64, 128, 128, 128),
            V(0, 4, 64, 64, 128, 128, 128), semi: true, abr: 3));

        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)0), target.Get(0, 0));
    }

    private static (int R, int G, int B) Rgb(ReferenceRasterizer target, int x, int y)
    {
        var (r, g, b, _) = target.Get(x, y);
        return (r, g, b);
    }
}
