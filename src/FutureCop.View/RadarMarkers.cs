using FutureCop.Game;
using FutureCop.Game.Mech;

namespace FutureCop.View;

/// <summary>
/// PS1 <c>POLY_F3</c> primitive packet emitted by <c>func_8005A828</c> (<c>gEmitRadarTriangle</c>),
/// matching the retail <c>struct LineF3</c> layout (<c>u32 tag</c>, <c>u32 code</c>, <c>s16 xy[6]</c>;
/// 20 bytes written into a <c>0x1C</c>-byte slot of <c>D_8009FA90</c>).
/// </summary>
public readonly record struct RadarTrianglePacket(
    uint Tag,
    uint CodeWord,
    short X0,
    short Y0,
    short X1,
    short Y1,
    short X2,
    short Y2)
{
    /// <summary>Red channel (bits 0..7 of <see cref="CodeWord"/>).</summary>
    public byte R => unchecked((byte)(CodeWord & 0xFFu));

    /// <summary>Green channel (bits 8..15 of <see cref="CodeWord"/>).</summary>
    public byte G => unchecked((byte)((CodeWord >> 8) & 0xFFu));

    /// <summary>Blue channel (bits 16..23 of <see cref="CodeWord"/>).</summary>
    public byte B => unchecked((byte)((CodeWord >> 16) & 0xFFu));

    /// <summary>PS1 GPU primitive opcode byte (bits 24..31 of <see cref="CodeWord"/>; <c>0x20</c> = <c>POLY_F3</c>).</summary>
    public byte GpuCode => unchecked((byte)(CodeWord >> 24));

    /// <summary>DMA payload word count (bits 24..31 of <see cref="Tag"/>; 4 words for <c>POLY_F3</c>).</summary>
    public byte TagLength => unchecked((byte)(Tag >> 24));
}

/// <summary>
/// Reference entity fields read by <c>func_8005A934</c> (<c>gCheckRadarTargetBearing</c>):
/// world <c>x</c>/<c>z</c> at <c>+0x50</c>/<c>+0x58</c> and signed 16-bit heading at <c>+0xB0</c>.
/// </summary>
public readonly record struct RadarReference(int X, int Z, short Heading);

/// <summary>
/// Pure ports of the radar marker routines: <c>func_8005A828</c> (<c>gEmitRadarTriangle</c>) and
/// <c>func_8005A934</c> (<c>gCheckRadarTargetBearing</c>).
/// </summary>
/// <remarks>
/// <para>
/// Trigonometry reuses the retail-exact ROM-backed helpers already in <c>FutureCop.Game</c>:
/// <see cref="MechMath.SinCos"/> (<c>func_8001846C</c>, table <c>D_8009A234</c>) and
/// <see cref="LobMotion.Atan2"/> (<c>func_80095C30</c>, table <c>D_8009E0A0</c>).
/// </para>
/// <para>
/// The retail 6-halfword triangle shape table <c>D_8009AFB4</c> (<c>gRadiusTable</c>, file offset
/// <c>0x8B7B4</c> in <c>SLUS_007.39</c>) is not embedded in this repository yet, so
/// <see cref="EmitRadarTriangle(int, uint, int, ReadOnlySpan{short})"/> accepts the 3-vertex
/// <c>s16[6]</c> table explicitly rather than guessing its ROM bytes.
/// </para>
/// </remarks>
public static class RadarMarkers
{
    /// <summary><c>func_8005A828</c> (<c>8005A828.c:79</c>): DMA tag word (length 4, link 0).</summary>
    public const uint PacketTag = 0x04000000u;

    /// <summary><c>func_8005A828</c> (<c>8005A828.c:80</c>): <c>POLY_F3</c> opcode OR-ed into <c>code</c>.</summary>
    public const uint PolyF3Opcode = 0x20000000u;

    /// <summary><c>func_8005A828</c> (<c>8005A828.c:73</c>): packet cursor advance when <c>gPacketCursor &lt; gPacketLimit</c>.</summary>
    public const int PacketStrideBytes = 0x1C;

    /// <summary><c>func_8005A828</c> (<c>8005A828.c:76</c>): Y screen origin in 16.16 fixed point (44 px).</summary>
    public const int RadarYBias = 0x2C0000;

    /// <summary><c>func_8005A828</c> (<c>8005A828.c:22</c>): radar X origin in 16.16 fixed point (120 px).</summary>
    public const int RightSideOriginX = 0x780000;

    /// <summary><c>func_8005A828</c> (<c>8005A828.c:23</c>): radar X origin in 16.16 fixed point (40 px).</summary>
    public const int LeftSideOriginX = 0x280000;

    /// <summary>Retail ROM address of <c>gRadiusTable</c> (6 x <c>s16</c> vertex pairs; file offset <c>0x8B7B4</c> in <c>SLUS_007.39</c>).</summary>
    public const uint RadiusTableAddress = 0x8009AFB4u;

    /// <summary><c>func_8005A934</c> (<c>8005A934.c:49-50</c>): per-axis dead-zone threshold; targets must strictly exceed this.</summary>
    public const int TargetBearingDeadZone = 0x2000;

    /// <summary><c>func_8005A934</c> (<c>8005A934.c:54</c>): quarter-turn correction subtracted from the relative bearing.</summary>
    public const int TargetBearingQuarterTurn = 0x400;

    /// <summary>
    /// Port of <c>func_8005A828</c> (<c>gEmitRadarTriangle</c>, <c>8005A828.c:47-89</c>): rotates three
    /// <c>s16</c> point pairs around <c>(origin, 0x2C0000)</c> by <paramref name="angle"/> (4096-step
    /// convention via <c>func_8001846C</c> / <see cref="MechMath.SinCos"/>) and returns the exact
    /// PS1 <c>POLY_F3</c> packet fields written to <c>D_8009FA90</c>.
    /// </summary>
    /// <param name="origin">
    /// X origin in 16.16 fixed point (<see cref="RightSideOriginX"/> = <c>0x780000</c> = 120 px,
    /// <see cref="LeftSideOriginX"/> = <c>0x280000</c> = 40 px).
    /// </param>
    /// <param name="angle">Rotation angle in 4096-step units (<c>u32</c>; masked to 12 bits by <c>func_8001846C</c>).</param>
    /// <param name="code">Caller colour word (<c>0x00BBGGRR</c>, plus optional command bits); <c>0x20000000</c> is OR-ed in.</param>
    /// <param name="shapePoints">
    /// Six <c>s16</c> values (<c>x0, y0, x1, y1, x2, y2</c>) corresponding to <c>D_8009AFB4</c> (<c>gRadiusTable</c>).
    /// </param>
    /// <returns>The <c>POLY_F3</c> packet with tag <c>0x04000000</c>, code word <c>code | 0x20000000</c>, and 3 <c>s16</c> vertices.</returns>
    public static RadarTrianglePacket EmitRadarTriangle(
        int origin,
        uint angle,
        int code,
        ReadOnlySpan<short> shapePoints)
    {
        if (shapePoints.Length < 6)
            throw new ArgumentException("shapePoints must contain at least 6 s16 values (3 vertex pairs).", nameof(shapePoints));

        // 8005A828.c:70 - func_8001846C(angle, &sine, &cosine); MechMath.SinCos uses ROM table D_8009A234.
        var (s, c) = MechMath.SinCos(unchecked((int)angle));

        // 8005A828.c:79-88 - wrapping 32-bit signed arithmetic, arithmetic right shift by 16 (MIPS sra),
        // then truncation to s16.
        unchecked
        {
            short x0 = (short)((origin + (c * shapePoints[0] - s * shapePoints[1])) >> 16);
            short y0 = (short)((s * shapePoints[0] + c * shapePoints[1] + RadarYBias) >> 16);

            short x1 = (short)((origin + (c * shapePoints[2] - s * shapePoints[3])) >> 16);
            short y1 = (short)((s * shapePoints[2] + c * shapePoints[3] + RadarYBias) >> 16);

            short x2 = (short)((origin + (c * shapePoints[4] - s * shapePoints[5])) >> 16);
            short y2 = (short)((s * shapePoints[4] + c * shapePoints[5] + RadarYBias) >> 16);

            return new RadarTrianglePacket(
                Tag: PacketTag,
                CodeWord: (uint)code | PolyF3Opcode,
                X0: x0, Y0: y0,
                X1: x1, Y1: y1,
                X2: x2, Y2: y2);
        }
    }

    /// <summary>
    /// Convenience overload of <see cref="EmitRadarTriangle(int, uint, int, ReadOnlySpan{short})"/>
    /// for signed <c>s32</c> angles such as the output of <see cref="CheckRadarTargetBearing"/>.
    /// </summary>
    public static RadarTrianglePacket EmitRadarTriangle(
        int origin,
        int angle,
        int code,
        ReadOnlySpan<short> shapePoints) =>
        EmitRadarTriangle(origin, unchecked((uint)angle), code, shapePoints);

    /// <summary>
    /// Port of <c>func_8005A934</c> (<c>gCheckRadarTargetBearing</c>, <c>8005A934.c:41-59</c>):
    /// computes the relative bearing from <paramref name="reference"/> to a target at
    /// <c>(targetX, targetZ)</c> when either axis distance exceeds <c>0x2000</c>.
    /// </summary>
    /// <param name="reference">Reference object position (<c>+0x50</c>/<c>+0x58</c>) and <c>s16</c> heading (<c>+0xB0</c>).</param>
    /// <param name="targetZ">Target Z coordinate (second argument <c>z</c> of <c>func_8005A934</c>).</param>
    /// <param name="targetX">Target X coordinate (third argument <c>x</c> of <c>func_8005A934</c>).</param>
    /// <param name="angle">
    /// Updated with <c>((gAtan2(dx, dz) &lt;&lt; 12) &gt;&gt; 12) - heading - 0x400</c> when returning
    /// <see langword="true"/>; left untouched when returning <see langword="false"/>, matching C
    /// (<c>8005A934.c:55-58</c>).
    /// </param>
    /// <returns>
    /// <see langword="true"/> (C return <c>1</c>) when the branchless <c>|dz| &gt; 0x2000 || |dx| &gt; 0x2000</c>
    /// gate passes; <see langword="false"/> (C return <c>0</c>) inside the dead zone.
    /// </returns>
    public static bool CheckRadarTargetBearing(RadarReference reference, int targetZ, int targetX, ref int angle)
    {
        unchecked
        {
            int dz = targetZ - reference.Z;
            int dx = targetX - reference.X;

            // 8005A934.c:49-50 - branchless abs: (((v >> 31) + v) ^ (v >> 31)).
            // Preserves MIPS bit-exactness even at int.MinValue (where it stays negative and <= 0x2000).
            int absDz = ((dz >> 31) + dz) ^ (dz >> 31);
            int absDx = ((dx >> 31) + dx) ^ (dx >> 31);

            if (absDz > TargetBearingDeadZone || absDx > TargetBearingDeadZone)
            {
                // 8005A934.c:51-55 - func_80095C30 (LobMotion.Atan2, table D_8009E0A0).
                int direction = LobMotion.Atan2(dx, dz);
                direction = (direction << 12) >> 12;
                direction -= reference.Heading;
                direction -= TargetBearingQuarterTurn;
                angle = direction;
                return true;
            }
            return false;
        }
    }
}
