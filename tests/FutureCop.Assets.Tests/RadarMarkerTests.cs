using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Hand-derived tests for <see cref="RadarMarkers"/> against the retail C decompilation of
/// <c>func_8005A828</c> (<c>gEmitRadarTriangle</c>) and <c>func_8005A934</c>
/// (<c>gCheckRadarTargetBearing</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every expected coordinate below is derived directly from the C equations in <c>8005A828.c:79-88</c>
/// using the retail ROM quarter-wave sine table <c>D_8009A234</c> (<see cref="MechTables.SineBytes"/>)
/// and from <c>8005A934.c:47-58</c> using the retail arctangent table <c>D_8009E0A0</c>
/// (<see cref="MechTables.AtanBytes"/>).
/// </para>
/// <para>
/// Because the 12 ROM bytes of <c>D_8009AFB4</c> (<c>gRadiusTable</c>) are not embedded in this repo yet,
/// the triangle tests use an explicit asymmetric 3-vertex <c>s16[6]</c> pointer shape
/// <c>{ (0, -6), (4, 5), (-4, 5) }</c> so every sign, axis swap, and fractional floor shift is exercised.
/// </para>
/// </remarks>
public sealed class RadarMarkerTests
{
    /// <summary>
    /// Synthetic 3-vertex <c>s16[6]</c> radar pointer shape: tip at (0, -6), right base (4, 5), left base (-4, 5).
    /// Asymmetric in X vs Y with both negative and positive components to catch sign/axis/truncation errors.
    /// </summary>
    private static readonly short[] PointerShape = new short[] { 0, -6, 4, 5, -4, 5 };

    [Theory]
    // 8005A828.c:70-88 with origin = 0x780000 (120 px), bias = 0x2C0000 (44 px), shape = (0,-6), (4,5), (-4,5):
    //
    // angle = 0:
    //   func_8001846C(0) -> s = D_8009A234[0] << 1 = 0, c = D_8009A234[1024] << 1 = 0x8000 << 1 = 65536 (0x10000).
    //   x' = (0x780000 + 65536*px - 0*py) >> 16 = 120 + px -> 120, 124, 116.
    //   y' = (0*px + 65536*py + 0x2C0000) >> 16 = 44 + py  ->  38,  49,  49.
    [InlineData(RadarMarkers.RightSideOriginX, 0u, 120, 38, 124, 49, 116, 49)]
    //
    // angle = 1024 (0x400, quarter turn):
    //   func_8001846C(1024) -> quadrant 1, i = 0 -> s = 65536 (0x10000), c = 0.
    //   x' = (0x780000 + 0 - 65536*py) >> 16 = 120 - py -> 126, 115, 115.
    //   y' = (65536*px + 0 + 0x2C0000) >> 16 = 44 + px  ->  44,  48,  40.
    [InlineData(RadarMarkers.RightSideOriginX, 1024u, 126, 44, 115, 48, 115, 40)]
    //
    // angle = 2048 (0x800, half turn):
    //   func_8001846C(2048) -> quadrant 2, i = 0 -> s = 0, c = -65536 (-0x10000).
    //   x' = (0x780000 - 65536*px) >> 16 = 120 - px -> 120, 116, 124.
    //   y' = (-65536*py + 0x2C0000) >> 16 = 44 - py ->  50,  39,  39.
    [InlineData(RadarMarkers.RightSideOriginX, 2048u, 120, 50, 116, 39, 124, 39)]
    //
    // angle = 3072 (0xC00, three-quarter turn):
    //   func_8001846C(3072) -> quadrant 3, i = 0 -> s = -65536 (-0x10000), c = 0.
    //   x' = (0x780000 + 65536*py) >> 16 = 120 + py -> 114, 125, 125.
    //   y' = (-65536*px + 0x2C0000) >> 16 = 44 - px ->  44,  40,  48.
    [InlineData(RadarMarkers.RightSideOriginX, 3072u, 114, 44, 125, 40, 125, 48)]
    //
    // angle = 307 (odd non-cardinal angle, quadrant 0):
    //   D_8009A234[307] = 14867 -> s = 14867 << 1 = 29734 (0x7426).
    //   D_8009A234[1024 - 307 = 717] = 29201 -> c = 29201 << 1 = 58402 (0xE422).
    //   v0 (0, -6):
    //     x0 = (0x780000 + (0 - 29734 * -6)) >> 16 = (7864320 + 178404) >> 16 = 8042724 >> 16 = 122.
    //     y0 = (0 + 58402 * -6 + 0x2C0000) >> 16   = (-350412 + 2883584) >> 16 = 2533172 >> 16 = 38.
    //   v1 (4, 5):
    //     x1 = (0x780000 + (58402 * 4 - 29734 * 5)) >> 16 = (7864320 + 84938) >> 16 = 7949258 >> 16 = 121.
    //     y1 = (29734 * 4 + 58402 * 5 + 0x2C0000) >> 16   = (410946 + 2883584) >> 16 = 3294530 >> 16 = 50.
    //   v2 (-4, 5):
    //     x2 = (0x780000 + (58402 * -4 - 29734 * 5)) >> 16 = (7864320 - 382278) >> 16 = 7482042 >> 16 = 114.
    //     y2 = (29734 * -4 + 58402 * 5 + 0x2C0000) >> 16   = (173074 + 2883584) >> 16 = 3056658 >> 16 = 46.
    [InlineData(RadarMarkers.RightSideOriginX, 307u, 122, 38, 121, 50, 114, 46)]
    //
    // Same five angles at the left-side radar origin = 0x280000 (40 px, i.e. x shifts by -80 px, y unchanged):
    //   angle = 0:    (40, 38), (44, 49), (36, 49)
    [InlineData(RadarMarkers.LeftSideOriginX, 0u, 40, 38, 44, 49, 36, 49)]
    //   angle = 1024: (46, 44), (35, 48), (35, 40)
    [InlineData(RadarMarkers.LeftSideOriginX, 1024u, 46, 44, 35, 48, 35, 40)]
    //   angle = 2048: (40, 50), (36, 39), (44, 39)
    [InlineData(RadarMarkers.LeftSideOriginX, 2048u, 40, 50, 36, 39, 44, 39)]
    //   angle = 3072: (34, 44), (45, 40), (45, 48)
    [InlineData(RadarMarkers.LeftSideOriginX, 3072u, 34, 44, 45, 40, 45, 48)]
    //   angle = 307:  v0 x = (2621440 + 178404) >> 16 = 42; v1 x = (2621440 + 84938) >> 16 = 41;
    //                 v2 x = (2621440 - 382278) >> 16 = 34; y identical (38, 50, 46).
    [InlineData(RadarMarkers.LeftSideOriginX, 307u, 42, 38, 41, 50, 34, 46)]
    public void EmitRadarTriangle_MatchesHandDerivedRetailVertices(
        int origin,
        uint angle,
        short x0, short y0,
        short x1, short y1,
        short x2, short y2)
    {
        // Confirm the odd-angle ROM table assumption directly before asserting the rotated coordinates.
        if (angle == 307u)
            Assert.Equal((29734, 58402), MechMath.SinCos(307));

        const int colour = 0x00112233;
        var packet = RadarMarkers.EmitRadarTriangle(origin, angle, colour, PointerShape);

        // 8005A828.c:79-80: tag = 0x04000000 (len=4, link=0), code = saved_code | 0x20000000 (POLY_F3).
        Assert.Equal(0x04000000u, packet.Tag);
        Assert.Equal(4, packet.TagLength);
        Assert.Equal(0x20112233u, packet.CodeWord);
        Assert.Equal(0x20, packet.GpuCode);
        Assert.Equal(0x33, packet.R);
        Assert.Equal(0x22, packet.G);
        Assert.Equal(0x11, packet.B);

        Assert.Equal((x0, y0), (packet.X0, packet.Y0));
        Assert.Equal((x1, y1), (packet.X1, packet.Y1));
        Assert.Equal((x2, y2), (packet.X2, packet.Y2));
    }

    [Fact]
    public void EmitRadarTriangle_PreservesExtraCodeBitsAndMasksAngleTo12Bits()
    {
        // 8005A828.c:80 writes `saved_code | 0x20000000`: any caller-supplied bits in the upper byte
        // (such as PS1 semi-transparency bit 0x02000000 -> 0x22) must survive alongside 0x20.
        var semiTransparent = RadarMarkers.EmitRadarTriangle(
            RadarMarkers.RightSideOriginX, 0x1000u + 307u, 0x02ABCDEF, PointerShape);
        var wrappedAngle = RadarMarkers.EmitRadarTriangle(
            RadarMarkers.RightSideOriginX, 307u, 0x02ABCDEF, PointerShape);

        Assert.Equal(0x22ABCDEFu, semiTransparent.CodeWord);
        Assert.Equal(0x22, semiTransparent.GpuCode);
        Assert.Equal(0xEF, semiTransparent.R);
        Assert.Equal(0xCD, semiTransparent.G);
        Assert.Equal(0xAB, semiTransparent.B);

        // func_8001846C masks angle & 0xFFF, so 0x1000 + 307 matches 307 bit-for-bit.
        Assert.Equal(wrappedAngle, semiTransparent);
    }

    [Fact]
    public void EmitRadarTriangle_UsesArithmeticRightShiftForNegativeCoordinates()
    {
        // 8005A828.c:84 computes `(origin + (c * points[0] - s * points[1])) >> 16` in signed s32 (MIPS sra).
        // With origin = 0, angle = 307 (s = 29734, c = 58402), vertex (-4, 5):
        //   0 + (58402 * -4 - 29734 * 5) = -382278 = -5.8331 * 65536.
        //   Arithmetic shift right floors toward -infinity: (-382278) >> 16 = -6 (NOT -5 from integer division).
        var packet = RadarMarkers.EmitRadarTriangle(0, 307u, 0x00FFFFFF, PointerShape);

        Assert.Equal((short)2, packet.X0);    //  178404 >> 16 =  2
        Assert.Equal((short)1, packet.X1);    //   84938 >> 16 =  1
        Assert.Equal((short)-6, packet.X2);   // -382278 >> 16 = -6 (floors toward -infinity)
    }

    [Fact]
    public void CheckRadarTargetBearing_DeadZoneReturnsFalseAndLeavesAngleUntouched()
    {
        // 8005A934.c:47-58: gate is strictly `> 0x2000` on |dz| or |dx|.
        // Exact boundary |dz| == 0x2000 and |dx| == 0x2000 must still return 0 and not modify *angle.
        var reference = new RadarReference(X: 1000, Z: -2000, Heading: 512);

        foreach (var (dx, dz) in new[] { (0, 0), (0x2000, 0x2000), (-0x2000, 0x2000), (0x2000, -0x2000), (-0x2000, -0x2000) })
        {
            int angle = 0x5A5A;
            bool shown = RadarMarkers.CheckRadarTargetBearing(reference, reference.Z + dz, reference.X + dx, ref angle);

            Assert.False(shown);
            Assert.Equal(0x5A5A, angle);
        }

        // Branchless abs at int.MinValue: (((int.MinValue >> 31) + int.MinValue) ^ (int.MinValue >> 31))
        // == (-1 + -2147483648) ^ -1 == 0x7FFFFFFF ^ 0xFFFFFFFF == int.MinValue, which is <= 0x2000.
        int minSentinel = 0x5A5A;
        bool shownMin = RadarMarkers.CheckRadarTargetBearing(new RadarReference(0, 0, 0), int.MinValue, 0, ref minSentinel);
        Assert.False(shownMin);
        Assert.Equal(0x5A5A, minSentinel);
    }

    [Theory]
    // 8005A934.c:47-56:
    //   dz = z - object->z, dx = x - object->x
    //   direction = ((gAtan2(dx, dz) << 12) >> 12) - object->heading - 0x400
    //
    // +Z target just outside dead zone (dx = 0, dz = 0x2001), heading = 0:
    //   gAtan2(0, 0x2001) = 0 -> 0 - 0 - 1024 = -1024 (-0x400).
    [InlineData(0, 0, 0, 0x2001, 0, -1024)]
    // +X target just outside dead zone (dx = 0x2001, dz = 0), heading = 0:
    //   gAtan2(0x2001, 0) = 0x400 (1024) -> 1024 - 0 - 1024 = 0.
    [InlineData(0, 0, 0, 0, 0x2001, 0)]
    // -Z target just outside dead zone (dx = 0, dz = -0x2001), heading = 0:
    //   gAtan2(0, -0x2001) = 0x800 (2048) -> 2048 - 0 - 1024 = 1024 (0x400).
    [InlineData(0, 0, 0, -0x2001, 0, 1024)]
    // -X target just outside dead zone (dx = -0x2001, dz = 0), heading = 0:
    //   gAtan2(-0x2001, 0) = -0x400 (-1024) -> -1024 - 0 - 1024 = -2048 (-0x800).
    [InlineData(0, 0, 0, 0, -0x2001, -2048)]
    // Diagonal +X/+Z with non-zero origin and heading:
    //   dx = 0x3000, dz = 0x3000 -> gAtan2 = 0x400 - D_8009E0A0[1024] = 1024 - 512 = 512.
    //   512 - heading(256) - 1024 = -768.
    [InlineData(100, 200, 256, 200 + 0x3000, 100 + 0x3000, -768)]
    // Odd slope dx = 0x2000 (within dead zone on X), dz = 0x4000 (outside dead zone on Z):
    //   |dx| < |dz|, index = (0x2000 << 10) / 0x4000 = 512 -> D_8009E0A0[512] = 302.
    //   With heading = 100: 302 - 100 - 1024 = -822 (unmasked).
    [InlineData(50, -50, 100, -50 + 0x4000, 50 + 0x2000, -822)]
    // Negative s16 heading sign-extension check (heading = -500):
    //   Same dx = 0x2000, dz = 0x4000 -> gAtan2 = 302 -> 302 - (-500) - 1024 = -222.
    [InlineData(50, -50, -500, -50 + 0x4000, 50 + 0x2000, -222)]
    public void CheckRadarTargetBearing_MatchesHandDerivedRetailBearings(
        int objX, int objZ, short heading,
        int targetZ, int targetX,
        int expectedAngle)
    {
        var reference = new RadarReference(objX, objZ, heading);
        int angle = 0;

        bool shown = RadarMarkers.CheckRadarTargetBearing(reference, targetZ, targetX, ref angle);

        Assert.True(shown);
        Assert.Equal(expectedAngle, angle);
    }

    [Fact]
    public void BearingFlowsIntoEmitRadarTriangleWithImplicit12BitWrap()
    {
        // Target straight ahead (+Z, dx = 0, dz = 0x4000) with heading = 0:
        //   func_8005A934 writes angle = -1024 (-0x400).
        //   Passing that signed angle to func_8005A828 wraps via (-1024) & 0xFFF = 3072 (0xC00),
        //   producing the exact same packet as angle = 3072u.
        int bearing = 0;
        Assert.True(RadarMarkers.CheckRadarTargetBearing(new RadarReference(0, 0, 0), 0x4000, 0, ref bearing));
        Assert.Equal(-1024, bearing);

        var fromBearing = RadarMarkers.EmitRadarTriangle(RadarMarkers.RightSideOriginX, bearing, 0x0000FF00, PointerShape);
        var fromCardinal = RadarMarkers.EmitRadarTriangle(RadarMarkers.RightSideOriginX, 3072u, 0x0000FF00, PointerShape);

        Assert.Equal(fromCardinal, fromBearing);
    }

    [Fact]
    public void InGameHud_DrawRadarTriangle_RasterisesOpaqueAndBlendedPackets()
    {
        var rgb = new byte[LevelScene.Width * LevelScene.Height * 3];
        var hud = new InGameHud(rgb);

        // Opaque green POLY_F3 (code 0x20, R=0x10, G=0xE0, B=0x20) at right-side origin (120, 44), angle 0:
        // vertices (120, 38), (124, 49), (116, 49) -> centroid (120, 45) is strictly inside.
        var opaquePacket = RadarMarkers.EmitRadarTriangle(
            RadarMarkers.RightSideOriginX, 0u, 0x0020E010, PointerShape);
        hud.DrawRadarTriangle(opaquePacket);

        Assert.Equal((0x10, 0xE0, 0x20), Pixel(rgb, 120, 45));
        Assert.Equal((0x00, 0x00, 0x00), Pixel(rgb, 120, 37));  // one pixel above tip

        // Semi-transparent POLY_F3 (code 0x22, R=0x80, G=0x40, B=0x20) over black at left-side origin (40, 44):
        // Put blends (0 + src) / 2 -> (0x40, 0x20, 0x10).
        var blendedPacket = RadarMarkers.EmitRadarTriangle(
            RadarMarkers.LeftSideOriginX, 0u, 0x02204080, PointerShape);
        hud.DrawRadarTriangle(blendedPacket);

        Assert.Equal((0x40, 0x20, 0x10), Pixel(rgb, 40, 45));
    }

    private static (byte R, byte G, byte B) Pixel(byte[] rgb, int x, int y)
    {
        int o = (y * LevelScene.Width + x) * 3;
        return (rgb[o], rgb[o + 1], rgb[o + 2]);
    }
}
