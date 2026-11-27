using FutureCop.Game;
using FutureCop.Game.Mech;

namespace FutureCop.View;

/// <summary>
/// PS1 <c>LINE_F2</c> primitive packet emitted by <c>func_8005A740</c> (<c>gEmitAngledLine</c>,
/// <c>8005A740.c:4-11,41-58</c>), matching the retail <c>struct LineF2</c> layout (<c>u32 tag</c>,
/// <c>u32 code</c>, <c>s16 x0, y0, x1, y1</c>; 16 bytes written into a <c>0x1C</c>-byte slot of
/// <c>D_8009FA90</c>).
/// </summary>
public readonly record struct RadarLinePacket(
    uint Tag,
    uint CodeWord,
    short X0,
    short Y0,
    short X1,
    short Y1)
{
    /// <summary>Red channel (bits 0..7 of <see cref="CodeWord"/>; <c>0x80</c> in retail).</summary>
    public byte R => unchecked((byte)(CodeWord & 0xFFu));

    /// <summary>Green channel (bits 8..15 of <see cref="CodeWord"/>; <c>0x80</c> in retail).</summary>
    public byte G => unchecked((byte)((CodeWord >> 8) & 0xFFu));

    /// <summary>Blue channel (bits 16..23 of <see cref="CodeWord"/>; <c>0x80</c> in retail).</summary>
    public byte B => unchecked((byte)((CodeWord >> 16) & 0xFFu));

    /// <summary>PS1 GPU primitive opcode byte (bits 24..31 of <see cref="CodeWord"/>; <c>0x40</c> = <c>LINE_F2</c>).</summary>
    public byte GpuCode => unchecked((byte)(CodeWord >> 24));

    /// <summary>DMA payload word count (bits 24..31 of <see cref="Tag"/>; 3 words for <c>LINE_F2</c>).</summary>
    public byte TagLength => unchecked((byte)(Tag >> 24));
}

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
/// Pure output of <c>func_8005A9C4</c> (<c>gDrawRadarMarkers</c>, <c>8005A9C4.c:95-145</c>): the
/// resolved 16.16 radar X origin, the player heading line angle (<c>-heading - 0x800</c>) and its
/// emitted <c>LINE_F2</c> packet (<c>func_8005A740</c>, <c>8005A9C4.c:111-112</c>), the optional 2P
/// other-player bearing angle and <c>LINE_F2</c> packet (<c>8005A9C4.c:114-123</c>), and the emitted
/// <c>POLY_F3</c> target marker packets (0..8, <c>8005A9C4.c:125-144</c>).
/// </summary>
public sealed record RadarMarkerDrawResult(
    int OriginX,
    int HeadingLineAngle,
    RadarLinePacket HeadingLine,
    int? OtherPlayerBearingAngle,
    RadarLinePacket? OtherPlayerBearingLine,
    IReadOnlyList<RadarTrianglePacket> Triangles);

/// <summary>
/// Pure ports of the radar marker routines: <c>func_8005A740</c> (<c>gEmitAngledLine</c>),
/// <c>func_8005A828</c> (<c>gEmitRadarTriangle</c>), <c>func_8005A934</c>
/// (<c>gCheckRadarTargetBearing</c>), and <c>func_8005A9C4</c> (<c>gDrawRadarMarkers</c>),
/// paired with the retail colour table <c>D_800A6F60</c> initialised by <c>func_8004CAE4</c>
/// (<c>gInitMarkerColorsAndPools</c>, <c>8004CAE4.c:57-105</c>).
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
/// The 32-word colour table <c>D_800A6F60</c> (<c>gMarkerColorTable</c>) resides in BSS and is
/// populated at level initialisation by <c>func_8004CAE4</c> (<c>8004CAE4.c:57-105</c>, called from
/// <c>func_80024A24</c> at <c>80024A24.c:199</c>). Entries 17..19 are additionally pulsed each frame
/// by <c>func_8004CC68</c> (<c>8004CC68.c:32-48</c>).
/// </para>
/// </remarks>
public static class RadarMarkers
{
    /// <summary><c>func_8005A740</c> (<c>8005A740.c:52</c>): DMA tag word for <c>LINE_F2</c> (length 3, link 0).</summary>
    public const uint LinePacketTag = 0x03000000u;

    /// <summary>
    /// <c>func_8005A740</c> (<c>8005A740.c:53</c>): hard-coded <c>LINE_F2</c> command + colour word
    /// (<c>0x40</c> opcode, <c>R=0x80, G=0x80, B=0x80</c>); the third argument passed by
    /// <c>func_8005A9C4</c> (<c>8005A9C4.c:112,121</c>) is ignored by retail <c>func_8005A740</c>.
    /// </summary>
    public const uint LineF2CodeWord = 0x40808080u;

    /// <summary><c>func_8005A740</c> (<c>8005A740.c:54-55</c>): inner radius of the angled radar line in pixels.</summary>
    public const int LineInnerRadius = 24;

    /// <summary><c>func_8005A740</c> (<c>8005A740.c:56-57</c>): outer radius of the angled radar line in pixels.</summary>
    public const int LineOuterRadius = 30;

    /// <summary><c>func_8005A828</c> (<c>8005A828.c:79</c>): DMA tag word (length 4, link 0).</summary>
    public const uint PacketTag = 0x04000000u;

    /// <summary><c>func_8005A828</c> (<c>8005A828.c:80</c>): <c>POLY_F3</c> opcode OR-ed into <c>code</c>.</summary>
    public const uint PolyF3Opcode = 0x20000000u;

    /// <summary><c>func_8005A740</c> (<c>8005A740.c:50</c>) / <c>func_8005A828</c> (<c>8005A828.c:73</c>): packet cursor advance when <c>gPacketCursor &lt; gPacketLimit</c>.</summary>
    public const int PacketStrideBytes = 0x1C;

    /// <summary><c>func_8005A740</c> (<c>8005A740.c:55,57</c>) / <c>func_8005A828</c> (<c>8005A828.c:76</c>): Y screen origin in 16.16 fixed point (44 px).</summary>
    public const int RadarYBias = 0x2C0000;

    /// <summary><c>func_8005A9C4</c> (<c>8005A9C4.c:109</c>): radar X origin in 16.16 fixed point (120 px, side 1).</summary>
    public const int RightSideOriginX = 0x780000;

    /// <summary><c>func_8005A9C4</c> (<c>8005A9C4.c:109</c>): radar X origin in 16.16 fixed point (40 px, side 0 / default).</summary>
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
    /// Retail RAM address of <c>gMarkerColorTable[32]</c> (<c>8005A9C4.c:44</c>, <c>8004CAE4.c:19</c>).
    /// Lives in BSS outside <c>SLUS_007.39</c> and is populated at level init by <c>func_8004CAE4</c>
    /// (<c>8004CAE4.c:57-105</c>).
    /// </summary>
    public const uint MarkerColorTableAddress = 0x800A6F60u;

    /// <summary>Number of entries in <c>D_800A6F60</c> (<c>flags[i] &amp; 0x1F</c>).</summary>
    public const int MarkerColorTableLength = 32;

    /// <summary>
    /// Retail <c>D_800A6F60</c> (<c>gMarkerColorTable[32]</c>) in PS1 <c>0x00BBGGRR</c> format,
    /// verbatim from <c>func_8004CAE4</c> (<c>gInitMarkerColorsAndPools</c>, <c>8004CAE4.c:57-105</c>,
    /// called from <c>func_80024A24</c> at <c>80024A24.c:199</c>).
    /// </summary>
    private static readonly int[] RetailMarkerColors =
    {
        0x00000000, // [ 0] 8004CAE4.c:69
        0x000000F8, // [ 1] 8004CAE4.c:75
        0x00F80000, // [ 2] 8004CAE4.c:76
        0x0000F800, // [ 3] 8004CAE4.c:77
        0x00F8F800, // [ 4] 8004CAE4.c:57,78
        0x0000F8F8, // [ 5] 8004CAE4.c:79
        0x00F800F8, // [ 6] 8004CAE4.c:58,80
        0x00F8F8F8, // [ 7] 8004CAE4.c:59,81
        0x000078F8, // [ 8] 8004CAE4.c:82
        0x00000078, // [ 9] 8004CAE4.c:83
        0x00780000, // [10] 8004CAE4.c:84
        0x00007800, // [11] 8004CAE4.c:85
        0x00787800, // [12] 8004CAE4.c:60,86
        0x00007878, // [13] 8004CAE4.c:87
        0x00780078, // [14] 8004CAE4.c:61,88
        0x00787878, // [15] 8004CAE4.c:62,89
        0x00003878, // [16] 8004CAE4.c:90
        0x000000F8, // [17] 8004CAE4.c:91  (pulsed per frame by func_8004CC68, 8004CC68.c:45)
        0x00F80000, // [18] 8004CAE4.c:92  (pulsed per frame by func_8004CC68, 8004CC68.c:46)
        0x0000F800, // [19] 8004CAE4.c:93  (pulsed per frame by func_8004CC68, 8004CC68.c:47)
        0x008080F8, // [20] 8004CAE4.c:63,94
        0x00F88070, // [21] 8004CAE4.c:64,95
        0x0080F880, // [22] 8004CAE4.c:65,96
        0x000000F8, // [23] 8004CAE4.c:97
        0x00F80000, // [24] 8004CAE4.c:98
        0x0000F800, // [25] 8004CAE4.c:99
        0x00F8F800, // [26] 8004CAE4.c:57,100
        0x0000F8F8, // [27] 8004CAE4.c:101
        0x00F800F8, // [28] 8004CAE4.c:58,102
        0x00F8F8F8, // [29] 8004CAE4.c:59,103
        0x000078F8, // [30] 8004CAE4.c:104
        0x00787878, // [31] 8004CAE4.c:62,105
    };

    /// <summary>
    /// Retail 32-entry colour table <c>D_800A6F60</c> (<c>gMarkerColorTable[32]</c>) initialised by
    /// <c>func_8004CAE4</c> (<c>8004CAE4.c:57-105</c>) in PS1 <c>0x00BBGGRR</c> format.
    /// </summary>
    public static ReadOnlySpan<int> MarkerColorTable => RetailMarkerColors;

    /// <summary>Number of colour entries pulsed per frame by <c>func_8004CC68</c> (<c>D_800A6F60[17..19]</c>).</summary>
    public const int PulseColorCount = 3;

    /// <summary>Index of the first pulsed entry (<c>D_800A6F60[17]</c> = <c>unk44</c>, <c>8004CAE4.c:91</c>).</summary>
    public const int PulseFirstColorIndex = 17;

    /// <summary><c>8004CC68.c:37</c>: mask folding <c>gFrameTime &gt;&gt; 3</c> onto the 64-phase ramp.</summary>
    public const int PulsePhaseMask = 0x3F;

    /// <summary><c>8004CC68.c:38-39</c>: ramp midpoint; phases at or above it are reflected.</summary>
    public const int PulseReflectAt = 0x20;

    /// <summary><c>8004CC68.c:41</c>: offset added to the reflected phase.</summary>
    public const int PulseLevelOffset = 8;

    /// <summary><c>8004CC68.c:42-43</c>: clamp applied to the offset level.</summary>
    public const int PulseLevelClamp = 0x1F;

    /// <summary>
    /// Port of <c>func_8004CC68</c> (<c>gUpdateMarkerPulseColors</c>, <c>8004CC68.c:36-47</c>): folds
    /// the low six bits of <paramref name="frameTime"/> / 8 into a triangular level 8..31 and packs it
    /// into the three colour words retail writes over <c>D_800A6F60[17..19]</c> every frame.
    /// </summary>
    /// <param name="frameTime">Retail <c>gFrameTime</c> (<c>D_8009FAA4</c>, signed 32-bit counter).</param>
    /// <param name="colors">Receives <c>unk44</c>, <c>unk48</c>, <c>unk4C</c> (indices 17, 18, 19).</param>
    public static void MarkerPulseColors(int frameTime, Span<int> colors)
    {
        if (colors.Length < PulseColorCount)
            throw new ArgumentException($"colors must hold {PulseColorCount} entries (D_800A6F60[17..19]).", nameof(colors));

        unchecked
        {
            // 8004CC68.c:36-37 - level = (gFrameTime >> 3) & 0x3F (MIPS sra keeps the sign).
            int level = (frameTime >> 3) & PulsePhaseMask;

            // 8004CC68.c:38-40 - triangle: reflect phases 32..63 back down, 31 down to 0.
            if (level >= PulseReflectAt)
                level = PulsePhaseMask - level;

            // 8004CC68.c:41-44 - lift the ramp by 8 and clamp it one step below the reflect point.
            level += PulseLevelOffset;
            if (level >= PulseReflectAt)
                level = PulseLevelClamp;

            // 8004CC68.c:45-47 - unk44 = red, unk48 = spectral blue/green mix, unk4C = green.
            int half = level / 2;
            colors[0] = level << 3;
            colors[1] = (half << 3) | (half << 11) | (level << 19);
            colors[2] = level << 11;
        }
    }

    /// <summary>
    /// Copies the retail 32-entry <see cref="MarkerColorTable"/> and applies
    /// <see cref="MarkerPulseColors"/> for <paramref name="frameTime"/>, i.e. the state of
    /// <c>D_800A6F60</c> after <c>func_8004CAE4</c> ran at level start (<c>80024A24.c:199</c>) and
    /// <c>func_8004CC68</c> ran at the end of the current gameplay frame
    /// (<c>8002703C.c:114</c>, after <c>D_8009FAA4</c> was updated at <c>8002703C.c:69</c>).
    /// </summary>
    /// <param name="frameTime">Retail <c>gFrameTime</c> (<c>D_8009FAA4</c>).</param>
    /// <returns>
    /// A fresh 32-entry table; entries 17..19 pulse, every other entry keeps its init value.
    /// </returns>
    public static int[] ResolveMarkerColorTable(int frameTime)
    {
        var table = (int[])RetailMarkerColors.Clone();
        MarkerPulseColors(frameTime, table.AsSpan(PulseFirstColorIndex, PulseColorCount));
        return table;
    }

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
    /// <param name="side">HUD side selector (<c>object-&gt;unkAC-&gt;unkC1</c>).</param>
    /// <returns>
    /// <see cref="RightSideOriginX"/> (<c>0x780000</c>) when <paramref name="side"/> is <c>1</c>;
    /// otherwise <see cref="LeftSideOriginX"/> (<c>0x280000</c>).
    /// </returns>
    public static int OriginXForSide(int side) => side == 1 ? RightSideOriginX : LeftSideOriginX;

    /// <summary>
    /// <c>8005A9C4.c:111</c>: computes the player heading line angle <c>-(s32)heading - 0x800</c>.
    /// </summary>
    /// <param name="heading">Signed 16-bit entity heading (<c>object+0xB0</c>).</param>
    /// <returns>Unmasked heading line angle <c>-(int)heading - 0x800</c>.</returns>
    public static int HeadingLineAngle(short heading) => unchecked(-(int)heading - HeadingLineAngleOffset);

    /// <summary>
    /// Port of <c>func_8005A740</c> (<c>gEmitAngledLine</c>, <c>8005A740.c:41-58</c>): emits a
    /// radial <c>LINE_F2</c> segment from radius 24 to radius 30 around <c>(origin, 0x2C0000)</c>
    /// at <paramref name="angle"/> (4096-step convention via <c>func_8001846C</c> /
    /// <see cref="MechMath.SinCos"/>), with hard-coded tag <c>0x03000000</c> and code
    /// <c>0x40808080</c>.
    /// </summary>
    /// <param name="origin">16.16 fixed-point radar X origin (<c>0x780000</c> or <c>0x280000</c> in retail).</param>
    /// <param name="angle">Signed 4096-step angle passed to <see cref="MechMath.SinCos"/> (masked to 12 bits).</param>
    /// <returns>
    /// The PS1 <c>LINE_F2</c> <see cref="RadarLinePacket"/> with tag <c>0x03000000</c>, code
    /// <c>0x40808080</c>, and screen-space endpoints at radii 24 and 30.
    /// </returns>
    public static RadarLinePacket EmitAngledLine(int origin, int angle)
    {
        // 8005A740.c:47 - gSinCos(id, &first, &second); first = sine, second = cosine.
        var (sine, cosine) = MechMath.SinCos(angle);

        // 8005A740.c:52-57 - wrapping 32-bit signed arithmetic, MIPS sra by 16, truncation to s16.
        unchecked
        {
            short x0 = (short)((origin + cosine * LineInnerRadius) >> 16);
            short y0 = (short)((sine * LineInnerRadius + RadarYBias) >> 16);
            short x1 = (short)((origin + cosine * LineOuterRadius) >> 16);
            short y1 = (short)((sine * LineOuterRadius + RadarYBias) >> 16);

            return new RadarLinePacket(
                Tag: LinePacketTag,
                CodeWord: LineF2CodeWord,
                X0: x0, Y0: y0,
                X1: x1, Y1: y1);
        }
    }

    /// <summary>Unsigned-angle convenience overload of <see cref="EmitAngledLine(int, int)"/>.</summary>
    /// <param name="origin">16.16 fixed-point radar X origin.</param>
    /// <param name="angle">Unsigned 4096-step angle passed to <see cref="MechMath.SinCos"/> (masked to 12 bits).</param>
    /// <returns>The emitted PS1 <c>LINE_F2</c> <see cref="RadarLinePacket"/>.</returns>
    public static RadarLinePacket EmitAngledLine(int origin, uint angle) =>
        EmitAngledLine(origin, unchecked((int)angle));

    /// <summary>
    /// Port of <c>func_8005A828</c> (<c>gEmitRadarTriangle</c>, <c>8005A828.c:47-89</c>) using the
    /// retail <c>D_8009AFB4</c> (<see cref="RadiusTable"/>) shape table.
    /// </summary>
    /// <param name="origin">16.16 fixed-point radar X origin (<c>0x780000</c> or <c>0x280000</c> in retail).</param>
    /// <param name="angle">Unsigned 4096-step rotation angle passed to <see cref="MechMath.SinCos"/> (masked to 12 bits).</param>
    /// <param name="code">
    /// PS1 <c>0x00BBGGRR</c> colour word (plus any optional upper GPU flag bits) OR-ed with
    /// <see cref="PolyF3Opcode"/> (<c>0x20000000</c>).
    /// </param>
    /// <returns>The emitted PS1 <c>POLY_F3</c> <see cref="RadarTrianglePacket"/>.</returns>
    public static RadarTrianglePacket EmitRadarTriangle(int origin, uint angle, int code) =>
        EmitRadarTriangle(origin, angle, code, RadiusTable);

    /// <summary>
    /// Signed-angle convenience overload of <see cref="EmitRadarTriangle(int, uint, int)"/> using the
    /// retail <c>D_8009AFB4</c> (<see cref="RadiusTable"/>) shape table.
    /// </summary>
    /// <param name="origin">16.16 fixed-point radar X origin.</param>
    /// <param name="angle">Signed 4096-step rotation angle, such as the output of <see cref="CheckRadarTargetBearing"/>.</param>
    /// <param name="code">
    /// PS1 <c>0x00BBGGRR</c> colour word (plus any optional upper GPU flag bits) OR-ed with
    /// <see cref="PolyF3Opcode"/> (<c>0x20000000</c>).
    /// </param>
    /// <returns>The emitted PS1 <c>POLY_F3</c> <see cref="RadarTrianglePacket"/>.</returns>
    public static RadarTrianglePacket EmitRadarTriangle(int origin, int angle, int code) =>
        EmitRadarTriangle(origin, unchecked((uint)angle), code, RadiusTable);

    /// <summary>
    /// Port of <c>func_8005A828</c> (<c>gEmitRadarTriangle</c>, <c>8005A828.c:47-89</c>): rotates three
    /// <c>s16</c> point pairs around <c>(origin, 0x2C0000)</c> by <paramref name="angle"/> (4096-step
    /// convention via <c>func_8001846C</c> / <see cref="MechMath.SinCos"/>) and returns the exact
    /// PS1 <c>POLY_F3</c> packet fields written to <c>D_8009FA90</c>.
    /// </summary>
    /// <param name="origin">16.16 fixed-point radar X origin (<c>0x780000</c> or <c>0x280000</c> in retail).</param>
    /// <param name="angle">Unsigned 4096-step rotation angle passed to <see cref="MechMath.SinCos"/> (masked to 12 bits).</param>
    /// <param name="code">
    /// PS1 <c>0x00BBGGRR</c> colour word (plus any optional upper GPU flag bits) OR-ed with
    /// <see cref="PolyF3Opcode"/> (<c>0x20000000</c>).
    /// </param>
    /// <param name="shapePoints">
    /// At least 6 signed 16-bit values representing three <c>(x, y)</c> vertex pairs
    /// (retail <c>D_8009AFB4</c> / <see cref="RadiusTable"/>).
    /// </param>
    /// <returns>
    /// The PS1 <c>POLY_F3</c> <see cref="RadarTrianglePacket"/> with tag <c>0x04000000</c>,
    /// code word <c>(uint)code | 0x20000000</c>, and the three rotated screen-space vertices.
    /// </returns>
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
    /// <param name="origin">16.16 fixed-point radar X origin.</param>
    /// <param name="angle">Signed 4096-step rotation angle.</param>
    /// <param name="code">
    /// PS1 <c>0x00BBGGRR</c> colour word (plus any optional upper GPU flag bits) OR-ed with
    /// <see cref="PolyF3Opcode"/> (<c>0x20000000</c>).
    /// </param>
    /// <param name="shapePoints">At least 6 signed 16-bit values representing three <c>(x, y)</c> vertex pairs.</param>
    /// <returns>The emitted PS1 <c>POLY_F3</c> <see cref="RadarTrianglePacket"/>.</returns>
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
    /// <param name="reference">Reference entity world <c>(X, Z)</c> and signed 16-bit <c>Heading</c> (<c>object+0x50/+0x58/+0xB0</c>).</param>
    /// <param name="targetZ">Target world Z coordinate (matches retail argument order <c>z</c> before <c>x</c>, <c>8005A934.c:41</c>).</param>
    /// <param name="targetX">Target world X coordinate.</param>
    /// <param name="angle">
    /// Receives the unmasked relative bearing <c>((gAtan2(dx, dz) &lt;&lt; 12) &gt;&gt; 12) - reference.Heading - 0x400</c>
    /// when this method returns <see langword="true"/>; left unchanged when it returns <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <c>|targetZ - reference.Z| &gt; 0x2000</c> or
    /// <c>|targetX - reference.X| &gt; 0x2000</c> (using MIPS branchless abs); otherwise <see langword="false"/>.
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
    /// the retail <see cref="RadiusTable"/> (<c>D_8009AFB4</c>) and the frame-resolved
    /// <c>D_800A6F60</c> (<see cref="ResolveMarkerColorTable"/>: init table from
    /// <c>8004CAE4.c:57-105</c> plus the per-frame pulse of <c>func_8004CC68</c>, <c>8004CC68.c:36-47</c>).
    /// </summary>
    /// <param name="reference">Reference entity world <c>(X, Z)</c> and signed 16-bit <c>Heading</c>.</param>
    /// <param name="side">HUD side selector (<c>object-&gt;unkAC-&gt;unkC1</c>; <c>1</c> selects <see cref="RightSideOriginX"/>, otherwise <see cref="LeftSideOriginX"/>).</param>
    /// <param name="frameTime">Retail <c>gFrameTime</c> (<c>D_8009FAA4</c>); triangles emit only while <c>(frameTime &amp; 0x180) != 0</c>.</param>
    /// <param name="node">Optional marker node (<c>object-&gt;unkAC-&gt;unk300</c>) holding the 3-bit-strided active mask and up to 8 slots.</param>
    /// <param name="playerCount">Active player count (<c>gPlayerCount</c>); 2P partner bearing requires <c>&gt;= 2</c>.</param>
    /// <param name="sessionMode">Session mode (<c>session.unk5C</c>); 2P partner bearing requires <c>0</c>.</param>
    /// <param name="otherPlayer">Optional 2P partner reference entity.</param>
    /// <returns>A <see cref="RadarMarkerDrawResult"/> describing the resolved origin, line packets, and 0..8 triangle packets.</returns>
    public static RadarMarkerDrawResult DrawRadarMarkers(
        RadarReference reference,
        int side,
        int frameTime,
        RadarMarkerNode? node,
        int playerCount = 1,
        int sessionMode = 0,
        RadarReference? otherPlayer = null)
    {
        Span<int> colorTable = stackalloc int[MarkerColorTableLength];
        RetailMarkerColors.CopyTo(colorTable);
        MarkerPulseColors(frameTime, colorTable.Slice(PulseFirstColorIndex, PulseColorCount));
        return DrawRadarMarkers(
            reference, side, frameTime, node,
            colorTable, RadiusTable,
            playerCount, sessionMode, otherPlayer);
    }

    /// <summary>
    /// Pure port of <c>func_8005A9C4</c> (<c>gDrawRadarMarkers</c>, <c>8005A9C4.c:95-145</c>) with an
    /// explicit 32-entry colour table (<c>D_800A6F60</c>) and the retail <see cref="RadiusTable"/>.
    /// </summary>
    /// <param name="reference">Reference entity world <c>(X, Z)</c> and signed 16-bit <c>Heading</c>.</param>
    /// <param name="side">HUD side selector (<c>object-&gt;unkAC-&gt;unkC1</c>).</param>
    /// <param name="frameTime">Retail <c>gFrameTime</c> (<c>D_8009FAA4</c>); triangles emit only while <c>(frameTime &amp; 0x180) != 0</c>.</param>
    /// <param name="node">Optional marker node (<c>object-&gt;unkAC-&gt;unk300</c>).</param>
    /// <param name="colorTable">At least 32 PS1 <c>0x00BBGGRR</c> entries (<c>D_800A6F60</c>), indexed by <c>slot.Flags &amp; 0x1F</c>.</param>
    /// <param name="playerCount">Active player count (<c>gPlayerCount</c>); 2P partner bearing requires <c>&gt;= 2</c>.</param>
    /// <param name="sessionMode">Session mode (<c>session.unk5C</c>); 2P partner bearing requires <c>0</c>.</param>
    /// <param name="otherPlayer">Optional 2P partner reference entity.</param>
    /// <returns>A <see cref="RadarMarkerDrawResult"/> describing the resolved origin, line packets, and 0..8 triangle packets.</returns>
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
    /// resolves the radar origin from <paramref name="side"/>, emits the player heading
    /// <c>LINE_F2</c> (<c>func_8005A740</c>, <c>8005A9C4.c:111-112</c>), emits the 2P other-player
    /// bearing <c>LINE_F2</c> when <paramref name="playerCount"/> &gt;= 2 and
    /// <paramref name="sessionMode"/> == 0 (<c>8005A9C4.c:114-123</c>), and — while
    /// <c>(frameTime &amp; 0x180) != 0</c> — walks <paramref name="node"/>'s 3-bit-strided mask
    /// (<c>0x249249</c>) to emit one <see cref="EmitRadarTriangle(int, int, int, ReadOnlySpan{short})"/>
    /// packet per active target that passes <see cref="CheckRadarTargetBearing"/>.
    /// </summary>
    /// <param name="reference">Reference entity world <c>(X, Z)</c> and signed 16-bit <c>Heading</c>.</param>
    /// <param name="side">HUD side selector (<c>object-&gt;unkAC-&gt;unkC1</c>; <c>1</c> selects <see cref="RightSideOriginX"/>, otherwise <see cref="LeftSideOriginX"/>).</param>
    /// <param name="frameTime">Retail <c>gFrameTime</c> (<c>D_8009FAA4</c>); point-marker triangles emit only while <c>(frameTime &amp; 0x180) != 0</c>.</param>
    /// <param name="node">Optional marker node (<c>object-&gt;unkAC-&gt;unk300</c>) carrying the 3-bit-strided active mask and up to 8 slots.</param>
    /// <param name="colorTable">At least 32 PS1 <c>0x00BBGGRR</c> entries (<c>D_800A6F60</c>), indexed by <c>slot.Flags &amp; 0x1F</c>.</param>
    /// <param name="shapePoints">At least 6 signed 16-bit values representing three <c>(x, y)</c> vertex pairs (<c>D_8009AFB4</c>).</param>
    /// <param name="playerCount">Active player count (<c>gPlayerCount</c>); 2P partner bearing requires <c>&gt;= 2</c>.</param>
    /// <param name="sessionMode">Session mode (<c>session.unk5C</c>); 2P partner bearing requires <c>0</c>.</param>
    /// <param name="otherPlayer">Optional 2P partner reference entity.</param>
    /// <returns>
    /// A <see cref="RadarMarkerDrawResult"/> containing the resolved X origin, heading line angle and
    /// <c>LINE_F2</c> packet, optional 2P partner bearing angle and <c>LINE_F2</c> packet, and 0..8
    /// emitted <c>POLY_F3</c> target marker packets.
    /// </returns>
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
        var headingLine = EmitAngledLine(origin, headingAngle);

        // 8005A9C4.c:114-123 - 2P co-op / split-off partner bearing line when playerCount >= 2 && session.unk5C == 0.
        int? otherAngle = null;
        RadarLinePacket? otherLine = null;
        if (playerCount >= 2 && sessionMode == 0 && otherPlayer is { } other)
        {
            if (CheckRadarTargetBearing(reference, other.Z, other.X, ref angle))
            {
                otherAngle = angle;
                otherLine = EmitAngledLine(origin, angle);
            }
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
            HeadingLine: headingLine,
            OtherPlayerBearingAngle: otherAngle,
            OtherPlayerBearingLine: otherLine,
            Triangles: triangles is not null ? triangles : Array.Empty<RadarTrianglePacket>());
    }
}
