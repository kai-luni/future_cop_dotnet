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
/// Reference entity fields read by <c>func_8005A934</c> (<c>gCheckRadarTargetBearing</c>) and
/// <c>func_8005A9C4</c> (<c>gDrawRadarMarkers</c>): world <c>x</c>/<c>z</c> at <c>+0x50</c>/<c>+0x58</c>
/// and signed 16-bit heading at <c>+0xB0</c>.
/// </summary>
public readonly record struct RadarReference(int X, int Z, short Heading);

/// <summary>
/// One point marker entry in <c>struct Node5A9C4</c> (<c>8005A9C4.c:4-12</c>):
/// <c>flags[i]</c> (low 5 bits index <c>D_800A6F60</c>) and <c>pos[i]</c> where
/// <c>a &lt;&lt; 9</c> is target world Z and <c>b &lt;&lt; 9</c> is target world X.
/// </summary>
public readonly record struct RadarMarkerSlot(byte Flags, short PosA, short PosB);

/// <summary>
/// Marker node pointed to by <c>object-&gt;unkAC-&gt;unk300</c> (<c>struct Node5A9C4</c>,
/// <c>8005A9C4.c:4-12</c>): 3-bit-strided active mask <c>unkA4</c> and up to 8 marker slots.
/// </summary>
public sealed record RadarMarkerNode(uint ActiveMask, IReadOnlyList<RadarMarkerSlot> Slots);

/// <summary>
/// Pure output of <c>func_8005A9C4</c> (<c>gDrawRadarMarkers</c>): the resolved 16.16 radar X origin,
/// the player heading line angle (<c>-heading - 0x800</c>), the optional 2P other-player bearing angle
/// (when <c>gPlayerCount &gt;= 2</c>, <c>gGameSession-&gt;unk5C == 0</c>, and the dead-zone gate passes),
/// and the emitted <c>POLY_F3</c> target marker packets (0..8).
/// </summary>
/// <remarks>
/// <c>func_8005A740</c> (<c>gEmitAngledLine</c>) is not ported yet, so the two angled-line calls
/// (<c>8005A9C4.c:112</c> and <c>:121</c>) are surfaced as their exact retail origin/angle values while
/// every active marker triangle is emitted as a full <see cref="RadarTrianglePacket"/>.
/// </remarks>
public sealed record RadarMarkerDrawResult(
    int OriginX,
    int HeadingLineAngle,
    int? OtherPlayerBearingAngle,
    IReadOnlyList<RadarTrianglePacket> Triangles);

/// <summary>
/// Pure ports of the radar marker routines: <c>func_8005A828</c> (<c>gEmitRadarTriangle</c>),
/// <c>func_8005A934</c> (<c>gCheckRadarTargetBearing</c>), and <c>func_8005A9C4</c>
/// (<c>gDrawRadarMarkers</c>).
/// </summary>
/// <remarks>
/// <para>
/// Trigonometry reuses the retail-exact ROM-backed helpers already in <c>FutureCop.Game</c>:
/// <see cref="MechMath.SinCos"/> (<c>func_8001846C</c>, table <c>D_8009A234</c>) and
/// <see cref="LobMotion.Atan2"/> (<c>func_80095C30</c>, table <c>D_8009E0A0</c>).
/// </para>
/// <para>
/// The 6-halfword triangle shape table <c>D_8009AFB4</c> (<c>gRadiusTable</c>) is embedded
/// byte-for-byte from <c>SLUS_007.39</c> at file offset <c>0x8B7B4</c>
/// (<c>0x8009AFB4 - 0x80010000 + 0x800</c>).
/// </para>
/// <para>
/// The 32-word colour table <c>D_800A6F60</c> (<c>gMarkerColorTable</c>) lies outside the
/// <c>SLUS_007.39</c> executable image (<c>0x80010000..0x8009F7FF</c>, file length <c>0x90000</c>):
/// it resides in BSS and is populated at runtime, so <see cref="PlaceholderMarkerColorTable"/>
/// provides a documented fallback when no runtime palette is supplied.
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

    /// <summary><c>func_8005A828</c> (<c>8005A828.c:22</c>): radar X origin in 16.16 fixed point (120 px, side 1).</summary>
    public const int RightSideOriginX = 0x780000;

    /// <summary><c>func_8005A828</c> (<c>8005A828.c:23</c>): radar X origin in 16.16 fixed point (40 px, side 0 / default).</summary>
    public const int LeftSideOriginX = 0x280000;

    /// <summary>Retail ROM address of <c>gRadiusTable</c> (6 x <c>s16</c> vertex pairs).</summary>
    public const uint RadiusTableAddress = 0x8009AFB4u;

    /// <summary>File offset of <c>D_8009AFB4</c> in <c>SLUS_007.39</c> (<c>0x8009AFB4 - 0x80010000 + 0x800</c>).</summary>
    public const int RadiusTableFileOffset = 0x8B7B4;

    /// <summary>
    /// Byte-exact 12-byte copy of <c>D_8009AFB4</c> (<c>gRadiusTable</c>) from <c>SLUS_007.39</c>
    /// at file offset <c>0x8B7B4</c> (<c>0x8009AFB4 - 0x80010000 + 0x800</c>):
    /// six little-endian <c>s16</c> values <c>{ 24, -3, 30, 0, 24, 3 }</c>.
    /// </summary>
    public static readonly byte[] RadiusTableBytes = Convert.FromHexString("1800FDFF1E00000018000300");

    private static readonly short[] DefaultRadiusTable = { 24, -3, 30, 0, 24, 3 };

    /// <summary>
    /// Retail 6-halfword triangle shape table <c>D_8009AFB4</c> (<c>gRadiusTable</c>) from
    /// <c>SLUS_007.39</c> offset <c>0x8B7B4</c>: vertices <c>(24, -3)</c>, <c>(30, 0)</c>, <c>(24, 3)</c>.
    /// </summary>
    public static ReadOnlySpan<short> RadiusTable => DefaultRadiusTable;

    /// <summary>
    /// Retail RAM address of <c>gMarkerColorTable[32]</c> (<c>8005A9C4.c:44</c>).
    /// Outside the <c>SLUS_007.39</c> executable image (<c>0x80010000..0x8009F7FF</c>; file offset
    /// would be <c>0x97760</c> vs file size <c>0x90000</c>), so it lives in BSS and is filled at runtime.
    /// </summary>
    public const uint MarkerColorTableAddress = 0x800A6F60u;

    /// <summary>Number of entries in <c>D_800A6F60</c> (<c>flags[i] &amp; 0x1F</c>).</summary>
    public const int MarkerColorTableLength = 32;

    /// <summary>
    /// Documented placeholder palette for <c>D_800A6F60</c> (<c>gMarkerColorTable[32]</c>) in PS1
    /// <c>0x00BBGGRR</c> format. Because <c>0x800A6F60</c> is in BSS outside <c>SLUS_007.39</c>, its
    /// runtime-populated values are not present in the retail executable image.
    /// </summary>
    private static readonly int[] DefaultPlaceholderMarkerColors =
    {
        0x0000FF00, 0x000000FF, 0x00FF8000, 0x0000FFFF,
        0x00FF00FF, 0x00FFFF00, 0x00FFFFFF, 0x008080FF,
        0x0000C000, 0x000000C0, 0x00C06000, 0x0000C0C0,
        0x00C000C0, 0x00C0C000, 0x00C0C0C0, 0x006060C0,
        0x0040FF40, 0x004040FF, 0x00FFA040, 0x0040FFFF,
        0x00FF40FF, 0x00FFFF40, 0x00E0E0E0, 0x00A0A0FF,
        0x00008000, 0x00000080, 0x00804000, 0x00008080,
        0x00800080, 0x00808000, 0x00808080, 0x00404080,
    };

    /// <summary>
    /// Documented placeholder for <c>D_800A6F60</c> (<c>gMarkerColorTable[32]</c>, BSS outside
    /// <c>SLUS_007.39</c>); callers with runtime-derived colours can pass a custom 32-entry table.
    /// </summary>
    public static ReadOnlySpan<int> PlaceholderMarkerColorTable => DefaultPlaceholderMarkerColors;

    /// <summary>
    /// Colour word (<c>0x00F8F8F8</c>) used by <see cref="InGameHud"/> when drawing the player's own
    /// heading pointer triangle as a stand-in for <c>func_8005A740(origin, -heading - 0x800, 0xF8F8F8)</c>
    /// (<c>8005A9C4.c:111-112</c>) until <c>func_8005A740</c> is ported.
    /// </summary>
    public const int PlayerPointerColor = 0x00F8F8F8;

    /// <summary><c>func_8005A934</c> (<c>8005A934.c:49-50</c>): per-axis dead-zone threshold; targets must strictly exceed this.</summary>
    public const int TargetBearingDeadZone = 0x2000;

    /// <summary><c>func_8005A934</c> (<c>8005A934.c:54</c>): quarter-turn correction subtracted from the relative bearing.</summary>
    public const int TargetBearingQuarterTurn = 0x400;

    /// <summary><c>func_8005A9C4</c> (<c>8005A9C4.c:111</c>): half-turn offset subtracted from <c>-heading</c> for the heading line.</summary>
    public const int HeadingLineAngleOffset = 0x800;

    /// <summary><c>func_8005A9C4</c> (<c>8005A9C4.c:125</c>): <c>gFrameTime</c> blink mask gating point-marker triangles.</summary>
    public const int MarkerBlinkMask = 0x180;

    /// <summary><c>func_8005A9C4</c> (<c>8005A9C4.c:130</c>): bit 0 of each of the 8 three-bit lanes in <c>node-&gt;unkA4</c>.</summary>
    public const uint MarkerSlotActiveMask = 0x249249u;

    /// <summary><c>func_8005A9C4</c> (<c>8005A9C4.c:132</c>): maximum number of point-marker slots per node.</summary>
    public const int MaxMarkerSlots = 8;

    /// <summary><c>func_8005A9C4</c> (<c>8005A9C4.c:134-135</c>): left-shift converting <c>node-&gt;pos[i]</c> halfwords to world coordinates.</summary>
    public const int MarkerPosShift = 9;

    /// <summary><c>func_8005A9C4</c> (<c>8005A9C4.c:137</c>): mask selecting the 5-bit index into <c>D_800A6F60</c>.</summary>
    public const int MarkerColorIndexMask = 0x1F;

    /// <summary>
    /// <c>8005A9C4.c:109</c>: resolves the 16.16 radar X origin from <c>object-&gt;unkAC-&gt;unkC1</c>
    /// (<c>0x780000</c> for side 1, <c>0x280000</c> otherwise).
    /// </summary>
    public static int OriginXForSide(int side) => side == 1 ? RightSideOriginX : LeftSideOriginX;

    /// <summary>
    /// <c>8005A9C4.c:111</c>: computes the player heading line angle <c>-(s32)heading - 0x800</c>.
    /// </summary>
    public static int HeadingLineAngle(short heading) => unchecked(-(int)heading - HeadingLineAngleOffset);

    /// <summary>
    /// Port of <c>func_8005A828</c> (<c>gEmitRadarTriangle</c>, <c>8005A828.c:47-89</c>) using the
    /// retail <c>D_8009AFB4</c> (<see cref="RadiusTable"/>) shape table.
    /// </summary>
    public static RadarTrianglePacket EmitRadarTriangle(int origin, uint angle, int code) =>
        EmitRadarTriangle(origin, angle, code, RadiusTable);

    /// <summary>
    /// Signed-angle convenience overload of <see cref="EmitRadarTriangle(int, uint, int)"/> using the
    /// retail <c>D_8009AFB4</c> (<see cref="RadiusTable"/>) shape table.
    /// </summary>
    public static RadarTrianglePacket EmitRadarTriangle(int origin, int angle, int code) =>
        EmitRadarTriangle(origin, unchecked((uint)angle), code, RadiusTable);

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

    /// <summary>
    /// Pure port of <c>func_8005A9C4</c> (<c>gDrawRadarMarkers</c>, <c>8005A9C4.c:95-145</c>) using
    /// the retail <see cref="RadiusTable"/> (<c>D_8009AFB4</c>) and documented
    /// <see cref="PlaceholderMarkerColorTable"/> (for BSS table <c>D_800A6F60</c>).
    /// </summary>
    public static RadarMarkerDrawResult DrawRadarMarkers(
        RadarReference reference,
        int side,
        int frameTime,
        RadarMarkerNode? node,
        int playerCount = 1,
        int sessionMode = 0,
        RadarReference? otherPlayer = null) =>
        DrawRadarMarkers(
            reference, side, frameTime, node,
            PlaceholderMarkerColorTable, RadiusTable,
            playerCount, sessionMode, otherPlayer);

    /// <summary>
    /// Pure port of <c>func_8005A9C4</c> (<c>gDrawRadarMarkers</c>, <c>8005A9C4.c:95-145</c>) with an
    /// explicit 32-entry colour table (<c>D_800A6F60</c>) and the retail <see cref="RadiusTable"/>.
    /// </summary>
    public static RadarMarkerDrawResult DrawRadarMarkers(
        RadarReference reference,
        int side,
        int frameTime,
        RadarMarkerNode? node,
        ReadOnlySpan<int> colorTable,
        int playerCount = 1,
        int sessionMode = 0,
        RadarReference? otherPlayer = null) =>
        DrawRadarMarkers(
            reference, side, frameTime, node,
            colorTable, RadiusTable,
            playerCount, sessionMode, otherPlayer);

    /// <summary>
    /// Pure port of <c>func_8005A9C4</c> (<c>gDrawRadarMarkers</c>, <c>8005A9C4.c:95-145</c>):
    /// resolves the radar origin from <paramref name="side"/>, computes the player heading line angle,
    /// tests the 2P other-player bearing when <paramref name="playerCount"/> &gt;= 2 and
    /// <paramref name="sessionMode"/> == 0, and — while <c>(frameTime &amp; 0x180) != 0</c> — walks
    /// <paramref name="node"/>'s 3-bit-strided mask (<c>0x249249</c>) to emit one
    /// <see cref="EmitRadarTriangle(int, int, int, ReadOnlySpan{short})"/> packet per active target
    /// that passes <see cref="CheckRadarTargetBearing"/>.
    /// </summary>
    public static RadarMarkerDrawResult DrawRadarMarkers(
        RadarReference reference,
        int side,
        int frameTime,
        RadarMarkerNode? node,
        ReadOnlySpan<int> colorTable,
        ReadOnlySpan<short> shapePoints,
        int playerCount = 1,
        int sessionMode = 0,
        RadarReference? otherPlayer = null)
    {
        if (colorTable.Length < MarkerColorTableLength)
            throw new ArgumentException($"colorTable must contain at least {MarkerColorTableLength} entries (D_800A6F60).", nameof(colorTable));
        if (shapePoints.Length < 6)
            throw new ArgumentException("shapePoints must contain at least 6 s16 values (3 vertex pairs).", nameof(shapePoints));

        // 8005A9C4.c:109 - radar X origin in 16.16 fixed point (side 1 -> 120 px, otherwise 40 px).
        int origin = OriginXForSide(side);

        // 8005A9C4.c:111-112 - player heading line: func_8005A740(origin, -unkB0 - 0x800, 0xF8F8F8).
        int angle = HeadingLineAngle(reference.Heading);
        int headingAngle = angle;

        // 8005A9C4.c:114-123 - 2P co-op / split-off partner bearing line when playerCount >= 2 && session.unk5C == 0.
        int? otherAngle = null;
        if (playerCount >= 2 && sessionMode == 0 && otherPlayer is { } other)
        {
            if (CheckRadarTargetBearing(reference, other.Z, other.X, ref angle))
                otherAngle = angle;
        }

        // 8005A9C4.c:125-144 - blinking point-marker triangles gated by gFrameTime & 0x180.
        List<RadarTrianglePacket>? triangles = null;
        if ((frameTime & MarkerBlinkMask) != 0 && node is not null)
        {
            uint mask = node.ActiveMask;
            if ((mask & MarkerSlotActiveMask) != 0)
            {
                int slotCount = Math.Min(MaxMarkerSlots, node.Slots.Count);
                for (int i = 0; i < slotCount; i++)
                {
                    if ((mask & 1u) != 0)
                    {
                        var slot = node.Slots[i];
                        int targetZ = (int)slot.PosA << MarkerPosShift;
                        int targetX = (int)slot.PosB << MarkerPosShift;
                        if (CheckRadarTargetBearing(reference, targetZ, targetX, ref angle))
                        {
                            int colour = colorTable[slot.Flags & MarkerColorIndexMask];
                            triangles ??= new List<RadarTrianglePacket>(slotCount);
                            triangles.Add(EmitRadarTriangle(origin, angle, colour, shapePoints));
                        }
                    }
                    mask >>= 3;
                }
            }
        }

        return new RadarMarkerDrawResult(
            OriginX: origin,
            HeadingLineAngle: headingAngle,
            OtherPlayerBearingAngle: otherAngle,
            Triangles: triangles is not null ? triangles : Array.Empty<RadarTrianglePacket>());
    }
}
