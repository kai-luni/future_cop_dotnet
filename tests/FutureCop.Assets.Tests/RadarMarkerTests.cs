using System.Buffers.Binary;
using System.Text;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Hand-derived tests for <see cref="RadarMarkers"/> against the retail C decompilation of
/// <c>func_8005A828</c> (<c>gEmitRadarTriangle</c>), <c>func_8005A934</c>
/// (<c>gCheckRadarTargetBearing</c>), and <c>func_8005A9C4</c> (<c>gDrawRadarMarkers</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every expected coordinate below is derived directly from the C equations in <c>8005A828.c:79-88</c>
/// using the retail ROM shape table <c>D_8009AFB4</c> (<see cref="RadarMarkers.RadiusTable"/> =
/// <c>{ (24, -3), (30, 0), (24, 3) }</c>, <c>SLUS_007.39</c> offset <c>0x8B7B4</c>), the retail ROM
/// quarter-wave sine table <c>D_8009A234</c> (<see cref="MechTables.SineBytes"/>), and from
/// <c>8005A934.c:47-58</c> using the retail arctangent table <c>D_8009E0A0</c>
/// (<see cref="MechTables.AtanBytes"/>).
/// </para>
/// </remarks>
public sealed class RadarMarkerTests
{
    private static byte[]? LoadSlus()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("FUTURECOP_SLUS"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "git", "future-cop-lapd-ps1", "baseroms", "us", "SLUS_007.39"),
        };
        foreach (var c in candidates)
            if (!string.IsNullOrEmpty(c) && File.Exists(c)) return File.ReadAllBytes(c);
        return null;
    }

    [Fact]
    public void RadiusTableMatchesRetailExecutable()
    {
        // Embedded table consistency (little-endian s16[6] vs raw 12 bytes).
        Assert.Equal(12, RadarMarkers.RadiusTableBytes.Length);
        Assert.Equal(6, RadarMarkers.RadiusTable.Length);
        Assert.Equal(new short[] { 24, -3, 30, 0, 24, 3 }, RadarMarkers.RadiusTable.ToArray());
        for (int i = 0; i < 6; i++)
        {
            short fromBytes = BinaryPrimitives.ReadInt16LittleEndian(RadarMarkers.RadiusTableBytes.AsSpan(i * 2, 2));
            Assert.Equal(fromBytes, RadarMarkers.RadiusTable[i]);
        }

        // Verify offset formula: address - 0x80010000 + 0x800 == 0x8B7B4.
        Assert.Equal(0x8B7B4, (int)(RadarMarkers.RadiusTableAddress - 0x80010000u + 0x800u));
        Assert.Equal(0x8B7B4, RadarMarkers.RadiusTableFileOffset);

        // Compare byte-for-byte against SLUS_007.39 when available.
        var exe = LoadSlus();
        if (exe is null)
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
            return;
        }

        Assert.Equal("PS-X EXE", Encoding.ASCII.GetString(exe, 0, 8));
        var romBytes = exe.AsSpan(RadarMarkers.RadiusTableFileOffset, 12).ToArray();
        Assert.Equal(romBytes, RadarMarkers.RadiusTableBytes);

        // Confirm D_800A6F60 lies outside the EXE image (BSS, populated at runtime).
        long colorTableOffset = (long)RadarMarkers.MarkerColorTableAddress - 0x80010000L + 0x800L;
        Assert.True(colorTableOffset >= exe.Length,
            $"Expected D_800A6F60 offset 0x{colorTableOffset:X} to be outside SLUS_007.39 (0x{exe.Length:X} bytes).");
    }

    [Theory]
    // 8005A828.c:70-88 with origin = 0x780000 (120 px), bias = 0x2C0000 (44 px),
    // retail D_8009AFB4 shape = (24, -3), (30, 0), (24, 3):
    //
    // angle = 0:
    //   func_8001846C(0) -> s = D_8009A234[0] << 1 = 0, c = D_8009A234[1024] << 1 = 0x8000 << 1 = 65536 (0x10000).
    //   x' = (0x780000 + 65536*px - 0*py) >> 16 = 120 + px -> 144, 150, 144.
    //   y' = (0*px + 65536*py + 0x2C0000) >> 16 = 44 + py  ->  41,  44,  47.
    [InlineData(RadarMarkers.RightSideOriginX, 0u, 144, 41, 150, 44, 144, 47)]
    //
    // angle = 1024 (0x400, quarter turn):
    //   func_8001846C(1024) -> quadrant 1, i = 0 -> s = 65536 (0x10000), c = 0.
    //   x' = (0x780000 + 0 - 65536*py) >> 16 = 120 - py -> 123, 120, 117.
    //   y' = (65536*px + 0 + 0x2C0000) >> 16 = 44 + px  ->  68,  74,  68.
    [InlineData(RadarMarkers.RightSideOriginX, 1024u, 123, 68, 120, 74, 117, 68)]
    //
    // angle = 2048 (0x800, half turn):
    //   func_8001846C(2048) -> quadrant 2, i = 0 -> s = 0, c = -65536 (-0x10000).
    //   x' = (0x780000 - 65536*px) >> 16 = 120 - px -> 96, 90, 96.
    //   y' = (-65536*py + 0x2C0000) >> 16 = 44 - py -> 47, 44, 41.
    [InlineData(RadarMarkers.RightSideOriginX, 2048u, 96, 47, 90, 44, 96, 41)]
    //
    // angle = 3072 (0xC00, three-quarter turn):
    //   func_8001846C(3072) -> quadrant 3, i = 0 -> s = -65536 (-0x10000), c = 0.
    //   x' = (0x780000 + 65536*py) >> 16 = 120 + py -> 117, 120, 123.
    //   y' = (-65536*px + 0x2C0000) >> 16 = 44 - px ->  20,  14,  20.
    [InlineData(RadarMarkers.RightSideOriginX, 3072u, 117, 20, 120, 14, 123, 20)]
    //
    // angle = 307 (odd non-cardinal angle, quadrant 0):
    //   D_8009A234[307] = 14867 -> s = 14867 << 1 = 29734 (0x7426).
    //   D_8009A234[1024 - 307 = 717] = 29201 -> c = 29201 << 1 = 58402 (0xE422).
    //   v0 (24, -3):
    //     x0 = (0x780000 + (58402 * 24 - 29734 * -3)) >> 16 = (7864320 + 1490850) >> 16 = 9355170 >> 16 = 142.
    //     y0 = (29734 * 24 + 58402 * -3 + 0x2C0000) >> 16   = (538410 + 2883584) >> 16  = 3421994 >> 16 =  52.
    //   v1 (30, 0):
    //     x1 = (0x780000 + (58402 * 30 - 0)) >> 16          = (7864320 + 1752060) >> 16 = 9616380 >> 16 = 146.
    //     y1 = (29734 * 30 + 0 + 0x2C0000) >> 16            = (892020 + 2883584) >> 16  = 3775604 >> 16 =  57.
    //   v2 (24, 3):
    //     x2 = (0x780000 + (58402 * 24 - 29734 * 3)) >> 16  = (7864320 + 1312446) >> 16 = 9176766 >> 16 = 140.
    //     y2 = (29734 * 24 + 58402 * 3 + 0x2C0000) >> 16    = (888822 + 2883584) >> 16  = 3772406 >> 16 =  57.
    [InlineData(RadarMarkers.RightSideOriginX, 307u, 142, 52, 146, 57, 140, 57)]
    //
    // Same five angles at the left-side radar origin = 0x280000 (40 px, i.e. x shifts by -80 px, y unchanged):
    //   angle = 0:    (64, 41), (70, 44), (64, 47)
    [InlineData(RadarMarkers.LeftSideOriginX, 0u, 64, 41, 70, 44, 64, 47)]
    //   angle = 1024: (43, 68), (40, 74), (37, 68)
    [InlineData(RadarMarkers.LeftSideOriginX, 1024u, 43, 68, 40, 74, 37, 68)]
    //   angle = 2048: (16, 47), (10, 44), (16, 41)
    [InlineData(RadarMarkers.LeftSideOriginX, 2048u, 16, 47, 10, 44, 16, 41)]
    //   angle = 3072: (37, 20), (40, 14), (43, 20)
    [InlineData(RadarMarkers.LeftSideOriginX, 3072u, 37, 20, 40, 14, 43, 20)]
    //   angle = 307:  v0 x = (2621440 + 1490850) >> 16 = 62; v1 x = (2621440 + 1752060) >> 16 = 66;
    //                 v2 x = (2621440 + 1312446) >> 16 = 60; y identical (52, 57, 57).
    [InlineData(RadarMarkers.LeftSideOriginX, 307u, 62, 52, 66, 57, 60, 57)]
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
        var packet = RadarMarkers.EmitRadarTriangle(origin, angle, colour);

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
            RadarMarkers.RightSideOriginX, 0x1000u + 307u, 0x02ABCDEF);
        var wrappedAngle = RadarMarkers.EmitRadarTriangle(
            RadarMarkers.RightSideOriginX, 307u, 0x02ABCDEF);

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
        // With origin = 0, angle = 2355 (2048 + 307 -> quadrant 2, s = -29734, c = -58402),
        // retail D_8009AFB4 vertices (24, -3), (30, 0), (24, 3):
        //   v0: -58402 * 24 - (-29734) * (-3) = -1490850 = -22.7486 * 65536 -> >> 16 = -23 (NOT -22).
        //   v1: -58402 * 30 - 0               = -1752060 = -26.7343 * 65536 -> >> 16 = -27 (NOT -26).
        //   v2: -58402 * 24 - (-29734) * 3    = -1312446 = -20.0263 * 65536 -> >> 16 = -21 (NOT -20).
        Assert.Equal((-29734, -58402), MechMath.SinCos(2355));
        var packet = RadarMarkers.EmitRadarTriangle(0, 2355u, 0x00FFFFFF);

        Assert.Equal((short)-23, packet.X0);
        Assert.Equal((short)-27, packet.X1);
        Assert.Equal((short)-21, packet.X2);
        Assert.Equal((short)35, packet.Y0);   // 2345174 >> 16 = 35
        Assert.Equal((short)30, packet.Y1);   // 1991564 >> 16 = 30
        Assert.Equal((short)30, packet.Y2);   // 1994762 >> 16 = 30
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
        //   producing the exact same packet as angle = 3072u (pointing straight UP at (120, 14)).
        int bearing = 0;
        Assert.True(RadarMarkers.CheckRadarTargetBearing(new RadarReference(0, 0, 0), 0x4000, 0, ref bearing));
        Assert.Equal(-1024, bearing);

        var fromBearing = RadarMarkers.EmitRadarTriangle(RadarMarkers.RightSideOriginX, bearing, 0x0000FF00);
        var fromCardinal = RadarMarkers.EmitRadarTriangle(RadarMarkers.RightSideOriginX, 3072u, 0x0000FF00);

        Assert.Equal(fromCardinal, fromBearing);
        Assert.Equal((117, 20), (fromBearing.X0, fromBearing.Y0));
        Assert.Equal((120, 14), (fromBearing.X1, fromBearing.Y1));
        Assert.Equal((123, 20), (fromBearing.X2, fromBearing.Y2));
    }

    [Fact]
    public void DrawRadarMarkers_SelectsOriginHeadingAndTwoPlayerPartnerBearing()
    {
        var player = new RadarReference(X: 0, Z: 0, Heading: 256);
        var partnerFar = new RadarReference(X: 0x4000, Z: 0, Heading: 0);
        var partnerNear = new RadarReference(X: 0x1000, Z: 0x1000, Heading: 0);

        // Side 0 -> LeftSideOriginX (0x280000), heading line = -256 - 0x800 = -2304.
        var onePlayer = RadarMarkers.DrawRadarMarkers(player, side: 0, frameTime: 0, node: null,
            playerCount: 1, sessionMode: 0, otherPlayer: partnerFar);
        Assert.Equal(RadarMarkers.LeftSideOriginX, onePlayer.OriginX);
        Assert.Equal(-2304, onePlayer.HeadingLineAngle);
        Assert.Null(onePlayer.OtherPlayerBearingAngle);
        Assert.Empty(onePlayer.Triangles);

        // Side 1, 2P, sessionMode == 0: partner at +X (0x4000) -> gAtan2(0x4000, 0) = 1024 -> 1024 - 256 - 1024 = -256.
        var twoPlayer = RadarMarkers.DrawRadarMarkers(player, side: 1, frameTime: 0, node: null,
            playerCount: 2, sessionMode: 0, otherPlayer: partnerFar);
        Assert.Equal(RadarMarkers.RightSideOriginX, twoPlayer.OriginX);
        Assert.Equal(-2304, twoPlayer.HeadingLineAngle);
        Assert.Equal(-256, twoPlayer.OtherPlayerBearingAngle);

        // 2P but sessionMode != 0 (split-screen/mode gate 8005A9C4.c:114): partner bearing suppressed.
        var splitMode = RadarMarkers.DrawRadarMarkers(player, side: 1, frameTime: 0, node: null,
            playerCount: 2, sessionMode: 1, otherPlayer: partnerFar);
        Assert.Null(splitMode.OtherPlayerBearingAngle);

        // 2P, sessionMode == 0, but partner inside dead zone (<= 0x2000): bearing suppressed.
        var nearPartner = RadarMarkers.DrawRadarMarkers(player, side: 0, frameTime: 0, node: null,
            playerCount: 2, sessionMode: 0, otherPlayer: partnerNear);
        Assert.Null(nearPartner.OtherPlayerBearingAngle);
    }

    [Fact]
    public void DrawRadarMarkers_WalksActive3BitMaskWithBlinkGateAndColorTable()
    {
        // Build a 32-entry custom colour table (D_800A6F60) with distinct 0x00BBGGRR entries.
        var colors = new int[32];
        for (int i = 0; i < 32; i++)
            colors[i] = 0x00102030 + i * 0x00010203;

        // Reference at (0, 0), heading = 0.
        var reference = new RadarReference(X: 0, Z: 0, Heading: 0);

        // 8 slots (PosA << 9 = targetZ, PosB << 9 = targetX):
        //   slot 0: active (bit 0),  PosA=0x20 (Z=0x4000), PosB=0       -> +Z, bearing=-1024 (3072), flags=0xE3 (index 3)
        //   slot 1: inactive (bit 3 = 0, bit 4 = 1 set as noise), far   -> skipped
        //   slot 2: active (bit 6),  PosA=0x10 (Z=0x2000), PosB=0x10    -> inside dead zone (<=0x2000), skipped
        //   slot 3: active (bit 9),  PosA=0, PosB=0x20 (X=0x4000)       -> +X, bearing=0, flags=0x1F (index 31)
        //   slot 4..6: inactive
        //   slot 7: active (bit 21), PosA=-0x20 (Z=-0x4000), PosB=0     -> -Z, bearing=1024, flags=0x40 (index 0)
        var slots = new[]
        {
            new RadarMarkerSlot(Flags: 0xE3, PosA: 0x20, PosB: 0),
            new RadarMarkerSlot(Flags: 0x05, PosA: 0x40, PosB: 0),
            new RadarMarkerSlot(Flags: 0x07, PosA: 0x10, PosB: 0x10),
            new RadarMarkerSlot(Flags: 0x1F, PosA: 0, PosB: 0x20),
            new RadarMarkerSlot(Flags: 0x01, PosA: 0x40, PosB: 0),
            new RadarMarkerSlot(Flags: 0x02, PosA: 0x40, PosB: 0),
            new RadarMarkerSlot(Flags: 0x03, PosA: 0x40, PosB: 0),
            new RadarMarkerSlot(Flags: 0x40, PosA: -0x20, PosB: 0),
        };
        uint mask = (1u << 0) | (1u << 4) | (1u << 6) | (1u << 9) | (1u << 21);
        var node = new RadarMarkerNode(mask, slots);

        // Blink gate off: (frameTime & 0x180) == 0 -> no triangles emitted.
        foreach (int offFrame in new[] { 0x00, 0x7F, 0x200 })
        {
            var off = RadarMarkers.DrawRadarMarkers(reference, side: 1, frameTime: offFrame, node, colors);
            Assert.Empty(off.Triangles);
        }

        // Quick-skip mask: non-zero unkA4 with none of the 0x249249 bit-0 lanes set -> no triangles.
        var nonLaneMaskNode = new RadarMarkerNode(0x00DB6DB6u, slots);
        Assert.Empty(RadarMarkers.DrawRadarMarkers(reference, side: 1, frameTime: 0x80, nonLaneMaskNode, colors).Triangles);

        // Blink gate on (0x80, 0x100, 0x180): emits exactly slots 0, 3, and 7 in order.
        foreach (int onFrame in new[] { 0x80, 0x100, 0x180 })
        {
            var res = RadarMarkers.DrawRadarMarkers(reference, side: 1, frameTime: onFrame, node, colors);
            Assert.Equal(RadarMarkers.RightSideOriginX, res.OriginX);
            Assert.Equal(3, res.Triangles.Count);

            // Slot 0: +Z -> angle -1024 (3072), colour colors[3].
            Assert.Equal(
                RadarMarkers.EmitRadarTriangle(RadarMarkers.RightSideOriginX, -1024, colors[3]),
                res.Triangles[0]);
            // Slot 3: +X -> angle 0, colour colors[31].
            Assert.Equal(
                RadarMarkers.EmitRadarTriangle(RadarMarkers.RightSideOriginX, 0, colors[31]),
                res.Triangles[1]);
            // Slot 7: -Z -> angle 1024, colour colors[0].
            Assert.Equal(
                RadarMarkers.EmitRadarTriangle(RadarMarkers.RightSideOriginX, 1024, colors[0]),
                res.Triangles[2]);
        }

        // Default overload uses documented MarkerColorTable[flags & 0x1F].
        var defaultRes = RadarMarkers.DrawRadarMarkers(reference, side: 0, frameTime: 0x80, node);
        Assert.Equal(3, defaultRes.Triangles.Count);
        Assert.Equal(
            (uint)RadarMarkers.MarkerColorTable[3] | RadarMarkers.PolyF3Opcode,
            defaultRes.Triangles[0].CodeWord);
    }

    [Fact]
    public void InGameHud_DrawRadarTriangle_RasterisesOpaqueAndBlendedPackets()
    {
        var rgb = new byte[LevelScene.Width * LevelScene.Height * 3];
        var hud = new InGameHud(rgb);

        // Opaque green POLY_F3 (code 0x20, R=0x10, G=0xE0, B=0x20) at right-side origin (120, 44), angle 0:
        // retail D_8009AFB4 vertices (144, 41), (150, 44), (144, 47) -> centroid (146, 44) is strictly inside.
        var opaquePacket = RadarMarkers.EmitRadarTriangle(
            RadarMarkers.RightSideOriginX, 0u, 0x0020E010);
        hud.DrawRadarTriangle(opaquePacket);

        Assert.Equal((0x10, 0xE0, 0x20), Pixel(rgb, 146, 44));
        Assert.Equal((0x00, 0x00, 0x00), Pixel(rgb, 151, 44));  // one pixel past tip

        // Semi-transparent POLY_F3 (code 0x22, R=0x80, G=0x40, B=0x20) over black at left-side origin (40, 44):
        // vertices (64, 41), (70, 44), (64, 47) -> centroid (66, 44), Put blends (0 + src) / 2 -> (0x40, 0x20, 0x10).
        var blendedPacket = RadarMarkers.EmitRadarTriangle(
            RadarMarkers.LeftSideOriginX, 0u, 0x02204080);
        hud.DrawRadarTriangle(blendedPacket);

        Assert.Equal((0x40, 0x20, 0x10), Pixel(rgb, 66, 44));
    }

    [Fact]
    public void InGameHud_Paint_WiresPlayerPointerAndTargetTrianglesThroughHudDraw()
    {
        // Side 0 (origin 40, 44), heading = 0 -> player pointer angle = -0 - 0x800 = -2048 (2048):
        //   func_8005A740 LINE_F2 from radius 24 to 30 -> (16, 44)..(10, 44), passing through (14, 44).
        //   Colour is the hard-coded LINE_F2 code 0x40808080 -> RGB (0x80, 0x80, 0x80): 8005A9C4.c:112
        //   passes 0xF8F8F8 as a third argument, but func_8005A740 takes two parameters and sets
        //   line->code = 0x40808080 (8005A740.c:52-53; prose at 8005A9C4.c:84-91).
        // Active target in slot 0 at +X (PosA=0, PosB=0x20 -> X=0x4000, Z=0), flags=1:
        //   MarkerColorTable[1] = 0x000000F8 (red, 8004CAE4.c:75), visible against black
        //   (index 0 is 0x00000000 black and would not distinguish a drawn triangle from background).
        //   bearing = 0 -> vertices (64, 41), (70, 44), (64, 47) -> centroid (66, 44).
        var targetNode = new RadarMarkerNode(
            ActiveMask: 1u,
            Slots: new[] { new RadarMarkerSlot(Flags: 1, PosA: 0, PosB: 0x20) });
        var session = new HudSession { Mode = 0 };

        // 1. Player pointer only (target node absent, as in MenuFlow.RenderScene today).
        var pointerOnly = new byte[LevelScene.Width * LevelScene.Height * 3];
        var gameWithoutTargets = new HudGameBlock
        {
            Health = 100,
            MaxHealth = 100,
            Radar = new RadarReference(0, 0, Heading: 0),
            Player = new HudPlayerBlock { Value64 = 2, ValueC1 = 0 },
        };
        InGameHud.Paint(pointerOnly, gameWithoutTargets, session, new HudGlobals { FrameTime = 0x00 });
        Assert.Equal((0x80, 0x80, 0x80), Pixel(pointerOnly, 14, 44));
        Assert.Equal((0x00, 0x00, 0x00), Pixel(pointerOnly, 66, 44));

        // 2. With target node + blink on (FrameTime = 0x80): both pointer and target triangle are painted.
        var withTargets = new byte[LevelScene.Width * LevelScene.Height * 3];
        var gameWithTargets = gameWithoutTargets with
        {
            Player = gameWithoutTargets.Player with { RadarMarkers = targetNode },
        };
        InGameHud.Paint(withTargets, gameWithTargets, session, new HudGlobals { FrameTime = 0x80 });
        int expectedColor = RadarMarkers.MarkerColorTable[1];
        var expectedRgb = (
            (byte)(expectedColor & 0xFF),
            (byte)((expectedColor >> 8) & 0xFF),
            (byte)((expectedColor >> 16) & 0xFF));
        Assert.Equal((0xF8, 0x00, 0x00), expectedRgb);
        Assert.Equal((0x80, 0x80, 0x80), Pixel(withTargets, 14, 44));
        Assert.Equal(expectedRgb, Pixel(withTargets, 66, 44));

        // 3. With target node + blink off (FrameTime = 0x00): pointer still painted, target triangle gated off.
        var blinkOff = new byte[LevelScene.Width * LevelScene.Height * 3];
        InGameHud.Paint(blinkOff, gameWithTargets, session, new HudGlobals { FrameTime = 0x00 });
        Assert.Equal((0x80, 0x80, 0x80), Pixel(blinkOff, 14, 44));
        Assert.Equal((0x00, 0x00, 0x00), Pixel(blinkOff, 66, 44));

        // 4. Without Radar reference: radar pass is skipped (no pixels touched at (14, 44)).
        var noRadar = new byte[LevelScene.Width * LevelScene.Height * 3];
        InGameHud.Paint(noRadar, gameWithoutTargets with { Radar = null }, session, new HudGlobals { FrameTime = 0x80 });
        Assert.Equal((0x00, 0x00, 0x00), Pixel(noRadar, 14, 44));
    }

    private static (byte R, byte G, byte B) Pixel(byte[] rgb, int x, int y)
    {
        int o = (y * LevelScene.Width + x) * 3;
        return (rgb[o], rgb[o + 1], rgb[o + 2]);
    }
}
