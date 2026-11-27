using System.Buffers.Binary;

namespace FutureCop.Assets;

/// <summary>
/// Layer 1 of the retail environment pass (func_8006531C) for one map section:
/// the height-grid projection func_800646BC (handwritten GTE asm), the cell word walk
/// func_80065134 and the packet emitter func_800648E8 with its NCLIP helper func_800648BC.
/// Buffers keep the retail byte layout so the shared vectors can hash them:
/// vertex slot = 8 bytes {u32 SXY2, s16 SZ3, u8 unused, u8 clip flags}, 17 x 17 points x 3 heights,
/// x-major; packet = 13 words (0x34 bytes) as written into the primitive pool D_8009F9F0.
/// </summary>
public static class TerrainRender
{
    public const int GridPoints = 17;
    public const int VertexBufferBytes = GridPoints * GridPoints * 3 * 8;
    public const int PacketWords = 13;

    /// <summary>GTE state the asm expects: the camera rotation loaded by func_80010820 (TR = 0),
    /// H/OFX/OFY and gPlayerCount D_8009FA0C (1 = full width 0x140, otherwise 0xA0).</summary>
    public readonly record struct GteSetup(Gte.Loaded Rotation, int H, int Ofx, int Ofy, int PlayerCount);

    /// <summary>Inputs of func_800648E8 that come from globals: D_800A8E80 (tpage/CLUT word per
    /// Cbmp slot), the D_800A2630 ramp built by func_800654F8 and gTileColourWord D_8009F83C.</summary>
    public sealed record EmitContext(uint[] Texpages, byte[] Ramp, ushort ColourWord);

    /// <summary>D_800A8E80 as func_80063C5C fills it for Cbmp ids 1..10.</summary>
    public static uint[] DefaultTexpages() => TextureList.DefaultRemap();

    /// <summary>
    /// func_800646BC: projects the section's height grid relative to (x, y, z) (camera-relative
    /// position of the cell corner in model units, s16) into <paramref name="dst"/>. Columns step
    /// x by 0x200, points step z by 0x200, each point has three heights (y + h * 16; -128 = absent).
    /// <paramref name="mask"/> is the cell mask shifted left by 16, as func_8006531C passes it:
    /// one nibble per group of four columns, one bit per group of four points.
    /// </summary>
    public static void ProjectGrid(LevelSection s, byte[] dst, short x, short y, short z, uint mask, GteSetup gte)
    {
        int width = gte.PlayerCount == 1 ? 0x140 : 0xA0;
        int t0 = x, t1 = y, t2 = z, t3 = z;
        int height = 0;            // t5: index into the height bytes (+0x0C)
        int slot = 0;              // t6: byte offset into dst
        uint t4 = 0;
        for (int column = 0; column <= 16; column++)
        {
            uint v0 = (mask << (column & ~3)) & 0xF0000000u;
            uint s3 = v0 | t4;
            t4 = v0;
            if (s3 == 0)
            {
                height += 0x33;
                slot += 0x198;
            }
            else
            {
                int t7 = 0, s0 = 0x11;
                bool columnDone = false;
                while (!columnDone)
                {
                    // L80064758: a group of four points, or the shared edge point after a drawn group.
                    int ra = (s3 & 0x80000000u) != 0 ? 4 : t7;
                    t7 = (s3 & 0x80000000u) != 0 ? 1 : 0;
                    s3 <<= 1;
                    int a1 = ra;
                    bool groupBreak = false;
                    while (a1 > 0)
                    {
                        a1--;
                        for (int layer = 0; layer < 3; layer++)
                        {
                            sbyte h = s.Height(height);
                            dst[slot + 7] = 0;
                            height++;
                            if (h != -128)
                            {
                                short vx = (short)t0;
                                short vy = (short)((h << 4) + t1);
                                short vz = (short)t2;
                                var p = Gte.Rtps(gte.Rotation, vx, vy, vz, gte.H, gte.Ofx, gte.Ofy, gte.PlayerCount);
                                uint sxy = (uint)p.Sxy;
                                int sz = p.Sz3 & 0xFFFF;
                                BinaryPrimitives.WriteUInt32LittleEndian(dst.AsSpan(slot), sxy);
                                BinaryPrimitives.WriteInt16LittleEndian(dst.AsSpan(slot + 4), (short)sz);
                                if (sz >= 0x601)
                                {
                                    int a0 = (int)(sxy & 0xFFFF) < width ? 0x32 : 0x38;
                                    int v1 = (int)(sxy >> 16) < 0xF0 ? 0x0E : 0x2C;
                                    dst[slot + 7] = (byte)(a0 & v1);
                                }
                            }
                            slot += 8;
                        }
                        int oldS0 = s0;
                        s0--;
                        if (oldS0 == 0) { columnDone = true; groupBreak = true; break; }
                        t2 += 0x200;
                    }
                    if (groupBreak) break;
                    // L80064838
                    ra = 4 - ra;
                    if (ra == 0) continue;
                    if (s3 == 0 || s0 < ra) ra = s0;
                    height += ra * 3;
                    slot += ra * 24;
                    s0 -= ra;
                    t2 += ra << 9;
                    if (s0 <= 0) columnDone = true;
                }
            }
            // L8006487C
            t0 += 0x200;
            t2 = t3;
        }
    }

    /// <summary>func_800648BC: GTE NCLIP on three packed screen points, true when MAC0 &lt; 0.</summary>
    public static bool BackFacing(int xy0, int xy1, int xy2)
    {
        long x0 = (short)xy0, y0 = (short)(xy0 >> 16), x1 = (short)xy1, y1 = (short)(xy1 >> 16);
        long x2 = (short)xy2, y2 = (short)(xy2 >> 16);
        long mac0 = x0 * y1 + x1 * y2 + x2 * y0 - x0 * y2 - x1 * y0 - x2 * y1;
        return unchecked((int)mac0) < 0;
    }

    /// <summary>
    /// func_80065134: walks the cell word list of the 4x4 tile block starting at tile
    /// <paramref name="index"/> (high nibble = x row, low nibble = z column) and emits every word that
    /// passes the facing filter of its shape type. <paramref name="a"/>/<paramref name="b"/> are the
    /// camera position relative to the section in 1/16 tile units (z and x), as func_8006531C computes them.
    /// </summary>
    public static void EmitCell(LevelSection s, byte[] verts, int a, int b, uint index, EmitContext ctx, List<uint[]> packets)
    {
        int p = 0x5D0 + (s.TileEntry((int)index) >> 4);
        int count = 0;
        int delta = a - b;
        int d = 0;
        do
        {
            uint word = s.U32(p);
            p += 4;
            int type = TerrainTables.ScratchShape((int)((word >> 15) & 0x7F)) & 7;
            uint old = index;
            if ((word & 1) != 0)
            {
                word &= ~1u;
                count++;
                index = old + 1;
                if ((count & 3) == 0) index = old + 13;
            }
            bool emit;
            if (type == 0)
            {
                word |= (word >> 11) & 1;
                emit = true;
            }
            else
            {
                switch (type)
                {
                    case 1: d = unchecked((int)((old & 0xF0) - (uint)b)); break;
                    case 2: d = unchecked((int)(((old << 4) & 0xF0) - (uint)a)); break;
                    case 3: d = unchecked((int)(((old << 4) & 0xF0) + ((old & 0xF0) + 16) - (uint)a - (uint)b)); break;
                    case 4: d = unchecked((int)((uint)delta - ((old << 4) & 0xF0) + (old & 0xF0))); break;
                    default: throw new InvalidDataException($"shape type {type} has no filter in func_80065134");
                }
                if (d >= 0)
                {
                    if ((word & 0x800) == 0) continue;
                    if (d > 0) { emit = true; goto call; }
                }
                emit = (word & 0x1000) != 0;
            }
        call:
            if (!emit) continue;
            uint arg = (old + (old >> 4)) * 3;
            EmitPolygon(s, verts, (int)arg * 8, word, old, ctx, packets);
        } while (count < 16);
    }

    private static int SlotW0(byte[] v, int o) => BinaryPrimitives.ReadInt32LittleEndian(v.AsSpan(o));
    private static short SlotZ(byte[] v, int o) => BinaryPrimitives.ReadInt16LittleEndian(v.AsSpan(o + 4));

    /// <summary>
    /// func_800648E8: one map-cell primitive. <paramref name="baseOffset"/> is the byte offset of the
    /// tile's first vertex slot, <paramref name="arg3"/> the tile index the caller leaves in $a3
    /// (used by the ramp colour variants k == 6/7).
    /// </summary>
    public static void EmitPolygon(LevelSection s, byte[] verts, int baseOffset, uint arg1, uint arg3,
        EmitContext ctx, List<uint[]> packets)
    {
        uint w = TerrainTables.VertexIndices((int)((arg1 >> 15) & 0x7F));
        int b0 = (int)(w & 0xFF), b1 = (int)((w >> 8) & 0xFF), b2 = (int)((w >> 16) & 0xFF), b3 = (int)(w >> 24);
        int o0 = baseOffset + (b0 << 3), o1 = baseOffset + (b1 << 3), o2 = baseOffset + (b2 << 3), o3 = baseOffset + (b3 << 3);
        uint x0 = verts[o0 + 7], x1 = verts[o1 + 7], x2v = verts[o2 + 7], x3 = verts[o3 + 7];
        uint t = x0 & x1 & x2v;
        uint f = x0 | x1 | x2v | x3;
        uint x2 = x3 & t;
        if ((f & 1) != 0) return;
        if ((f & 2) == 0 && x2 != 0) return;
        if ((arg1 & 1) != 0 && BackFacing(SlotW0(verts, o0), SlotW0(verts, o1), SlotW0(verts, o2))) return;
        int avg;
        if (o0 == o3)
        {
            avg = SlotZ(verts, o3) + SlotZ(verts, o1) + SlotZ(verts, o2);
            avg += SlotZ(verts, o3) + avg * 4;
            avg >>= 4;
        }
        else
        {
            avg = (SlotZ(verts, o0) + SlotZ(verts, o1) + SlotZ(verts, o2) + SlotZ(verts, o3)) >> 2;
        }

        Span<byte> o = stackalloc byte[PacketWords * 4];
        static void W32(Span<byte> buf, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(buf.Slice(at), value);
        static void W16(Span<byte> buf, int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(at), value);
        static void Grey(Span<byte> buf, int at, byte c) { buf[at] = c; buf[at + 1] = c; buf[at + 2] = c; }
        W32(o, 0, (uint)avg & 0xFFFFFF);

        int descriptor = s.DescriptorOffset + (int)((arg1 >> 21) & 0x7FE);
        int uvAt = s.UvOffset + (int)(arg1 & 0x7FE);
        Span<ushort> uv = stackalloc ushort[4];
        for (int i = 0; i < 4; i++) uv[i] = s.TableU16(uvAt + i * 2);
        uint t3 = s.TableU16(descriptor);
        if ((t3 & 0x800) != 0)
            for (int i = 0; i < 4; i++) uv[i] = (ushort)(uv[i] + ctx.ColourWord);
        uint a0 = ctx.Texpages[(t3 >> 8) & 7];
        uint k = t3 >> 13;
        uint semi = (t3 & 0x1000) >> 11;
        uint v0w = (uint)SlotW0(verts, o0), v1w = (uint)SlotW0(verts, o1), v2w = (uint)SlotW0(verts, o2), v3w = (uint)SlotW0(verts, o3);
        static void Clut(Span<byte> buf, int at, ushort e)
        {
            int sh = e >> 15;
            uint m = 0x1Fu << (sh + 2);
            buf[at] = (byte)(((uint)e >> (8 - sh)) & m);
            buf[at + 1] = (byte)(((uint)e >> (3 - sh)) & m);
            buf[at + 2] = (byte)(((uint)e << (sh + 2)) & m);
        }
        ushort Colour(uint i) => s.TableU16(s.ColourOffset + (int)i * 2);
        ushort Desc(int i) => s.TableU16(descriptor + i * 2);
        uint lo = arg3 & 0xF, hi = arg3 & 0xF0, lo1 = (lo + 1) & 0xF, hi1 = (hi + 0x10) & 0xF0;
        switch (k)
        {
            case 0:
            case 1:
            {
                Grey(o, 4, (byte)(t3 & 0xFC));
                W32(o, 0xC, uv[0] | (a0 & 0xFFFF0000));
                W32(o, 0x14, uv[1] | (a0 << 16));
                W32(o, 8, v0w);
                W32(o, 0x10, v1w);
                if (k == 0)
                {
                    o[3] = 7;
                    o[7] = (byte)(semi | 0x24);
                    W32(o, 0x18, v2w);
                    W16(o, 0x1C, uv[2]);
                }
                else
                {
                    o[3] = 9;
                    o[7] = (byte)(semi | 0x2C);
                    W16(o, 0x24, uv[2]);
                    W16(o, 0x1C, uv[3]);
                    W32(o, 0x20, v2w);
                    W32(o, 0x18, v3w);
                }
                break;
            }
            case 2:
            case 4:
            case 6:
            {
                o[3] = 9;
                o[7] = (byte)(semi | 0x34);
                W32(o, 0xC, uv[0] | (a0 & 0xFFFF0000));
                W32(o, 0x18, uv[1] | (a0 << 16));
                W16(o, 0x24, uv[2]);
                W32(o, 8, v0w);
                W32(o, 0x14, v1w);
                W32(o, 0x20, v2w);
                if (k == 2)
                {
                    uint g = (t3 << 16) | Desc(1);
                    Grey(o, 4, (byte)((g >> 16) & 0xFC));
                    Grey(o, 0x10, (byte)((g >> 10) & 0xFC));
                    Grey(o, 0x1C, (byte)((g >> 4) & 0xFC));
                }
                else if (k == 4)
                {
                    Clut(o, 4, Colour(t3 & 0xFF));
                    uint next = Desc(1);
                    Clut(o, 0x10, Colour(next >> 8));
                    Clut(o, 0x1C, Colour(next & 0xFF));
                }
                else
                {
                    uint ty = t3 & 7;
                    byte c = ty != 1 ? ctx.Ramp[arg3 & 0xFF] : ctx.Ramp[hi | lo1];
                    Grey(o, 4, c);
                    Grey(o, 0x1C, ctx.Ramp[ty == 3 ? (hi1 | lo1) : (hi1 | lo)]);
                    Grey(o, 0x10, ctx.Ramp[(ty == 3 || ty == 0) ? (hi | lo1) : (hi1 | lo1)]);
                }
                break;
            }
            default: // 3, 5, 7
            {
                o[3] = 0xC;
                o[7] = (byte)(semi | 0x3C);
                W32(o, 0xC, uv[0] | (a0 & 0xFFFF0000));
                W32(o, 0x18, uv[1] | (a0 << 16));
                W16(o, 0x30, uv[2]);
                W16(o, 0x24, uv[3]);
                W32(o, 8, v0w);
                W32(o, 0x14, v1w);
                W32(o, 0x2C, v2w);
                W32(o, 0x20, v3w);
                if (k == 3)
                {
                    uint g = (t3 << 16) | Desc(1);
                    Grey(o, 4, (byte)((g >> 16) & 0xFC));
                    Grey(o, 0x10, (byte)((g >> 10) & 0xFC));
                    Grey(o, 0x28, (byte)((g >> 4) & 0xFC));
                    Grey(o, 0x1C, (byte)((g & 0x3F) << 2));
                }
                else if (k == 5)
                {
                    Clut(o, 4, Colour(t3 & 0xFF));
                    uint next = Desc(1);
                    Clut(o, 0x10, Colour(next >> 8));
                    Clut(o, 0x28, Colour(next & 0xFF));
                    Clut(o, 0x1C, Colour((uint)Desc(2) >> 8));
                }
                else
                {
                    Grey(o, 4, ctx.Ramp[arg3 & 0xFF]);
                    Grey(o, 0x10, ctx.Ramp[hi | lo1]);
                    Grey(o, 0x28, ctx.Ramp[hi1 | lo1]);
                    Grey(o, 0x1C, ctx.Ramp[hi1 | lo]);
                }
                break;
            }
        }
        var words = new uint[PacketWords];
        for (int i = 0; i < PacketWords; i++) words[i] = BinaryPrimitives.ReadUInt32LittleEndian(o.Slice(i * 4));
        packets.Add(words);
    }
}
