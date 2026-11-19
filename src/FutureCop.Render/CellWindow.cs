namespace FutureCop.Assets;

/// <summary>One of the 25 entries gSampleCellWindow writes (mask word, node, s16 position x/y/z).</summary>
/// <param name="Visible">False when the entry only got a zero mask (node and position untouched).</param>
public readonly record struct CellWindowEntry(int Mask, bool Visible, int SectionId, short X, short Y, short Z);

/// <summary>
/// The 5x5 cell window of the environment pass: gSampleCellWindow func_80064460 with its
/// visibility probe func_8006429C, the plane probe func_8006411C, the view-cone test func_80063FF8
/// and the rotation helper func_800640D8 (handwritten mvmva sf=1, RT, V0, no translation, lm=0;
/// stores IR1, -IR2, IR3 and returns FLAG). The camera rotation must already be in the GTE, as
/// gUpdateCameraAnchor loads it with func_80010820 before the call.
/// </summary>
public static class CellWindow
{
    /// <summary>
    /// func_80064460. <paramref name="cellZ"/>/<paramref name="cellX"/> are the window origin cells
    /// (arg0 = z axis, arg1 = x axis; gUpdateCameraAnchor passes (look-at >> 17) - 2), the eye is
    /// the camera position +0x7C/+0x80/+0x84 in world units. Entry order is x-major (row j = x).
    /// </summary>
    public static CellWindowEntry[] Sample(LevelMap map, int eyeX, int eyeY, int eyeZ, int cellZ, int cellX,
        int playerCount, Gte.Loaded rotation)
    {
        var result = new CellWindowEntry[25];
        int bx = (cellX << 17) - eyeX;
        int by = -eyeY;
        int bz = (cellZ << 17) - eyeZ;
        int width = map.Header.Width;
        for (int j = 0; j < 5; j++)
        {
            for (int i = 0; i < 5; i++)
            {
                uint off = map.Header.Entry((cellX + j) * width + cellZ + i) & 0xFFFC;
                int id = (int)(off >> 2);
                var node = map.Section(id);
                short vx = (short)((bx + (j << 17)) >> 4);
                short vz = (short)((bz + (i << 17)) >> 4);
                short vy = (short)(by >> 4);
                if (node == null)
                    throw new InvalidDataException($"cell ({cellX + j},{cellZ + i}) names section {id}, which is not loaded; retail reads a null node here");
                int flags = Probe(node, vx, vy, vz, playerCount, rotation);
                int mask = 0;
                if ((flags & 1) != 0)
                {
                    uint f = (uint)flags;
                    uint a = ((f & 0x20) >> 2) | ((f & 0x40) >> 4) | ((f & 0x200) >> 8) | ((f & 0x400) >> 10);
                    a <<= 4;
                    a |= ((f & 0x80) >> 4) | ((f & 0x100) >> 6) | ((f & 0x800) >> 10) | ((f & 0x1000) >> 12);
                    a <<= 4;
                    a |= ((f & 0x2000) >> 10) | ((f & 0x4000) >> 12) | ((f >> 16) & 2) | ((f >> 18) & 1);
                    a <<= 4;
                    mask = (int)(a | ((f & 0x8000) >> 12) | ((f >> 14) & 4) | ((f >> 18) & 2) | ((f >> 20) & 1));
                    result[j * 5 + i] = new CellWindowEntry(mask, true, id, vx, vy, vz);
                }
                else
                {
                    result[j * 5 + i] = new CellWindowEntry(0, false, -1, 0, 0, 0);
                }
            }
        }
        return result;
    }

    /// <summary>func_8006429C: coarse test of the cell centre, then the four quadrant plane sets.</summary>
    public static int Probe(LevelSection node, short px, short py, short pz, int playerCount, Gte.Loaded rotation)
    {
        // +0x374: five Bound sets of {s16 margin, u16 y} x 4 (set 0 also carries the coarse scale/offset).
        const int Data = 0x374;
        short tx = (short)(px + 0x1000), ty = (short)(py + node.S16(Data + 2)), tz = (short)(pz + 0x1000);
        var (cx, cy, cz, _) = Rotate(rotation, tx, ty, tz);
        int result = InViewCone(cx, cy, cz, node.S16(Data) << 4, playerCount);
        if (result == 0) return 0;
        result |= Planes(node, Data, px, py, pz, 0x1000, playerCount, rotation);
        if ((result & 2) != 0)
        {
            result |= Planes(node, Data + 0x10, px, py, pz, 0x800, playerCount, rotation) << 4;
            if ((result & 0x1E0) == 0) result &= ~2;
        }
        pz = (short)(pz + 0x1000);
        if ((result & 4) != 0)
        {
            result |= Planes(node, Data + 0x20, px, py, pz, 0x800, playerCount, rotation) << 8;
            if ((result & 0x1E00) == 0) result &= ~4;
        }
        px = (short)(px + 0x1000);
        if ((result & 0x10) != 0)
        {
            result |= Planes(node, Data + 0x40, px, py, pz, 0x800, playerCount, rotation) << 16;
            if ((result & 0x1E0000) == 0) result &= ~0x10;
        }
        pz = (short)(pz - 0x1000);
        if ((result & 8) != 0)
        {
            result |= Planes(node, Data + 0x30, px, py, pz, 0x800, playerCount, rotation) << 12;
            if ((result & 0x1E000) == 0) result &= ~8;
        }
        if ((result & 0x1E) == 0) result = 0;
        return result;
    }

    /// <summary>func_8006411C: four corner probes of one quadrant, bits 1/2/4/3 (shifted 1, 2, 4, 3).</summary>
    private static int Planes(LevelSection node, int bounds, short bx, short by, short bz, int step, int playerCount, Gte.Loaded rotation)
    {
        short Margin(int i) => node.S16(bounds + i * 4);
        ushort Y(int i) => node.U16(bounds + i * 4 + 2);
        int result = 0;
        int half = step >> 1;
        ushort in0 = (ushort)(bx + half), in1 = (ushort)(by + Y(1)), in2 = (ushort)(bz + half);
        var r = Rotate(rotation, (short)in0, (short)in1, (short)in2);
        if (r.Flag == 0) result = InViewCone(r.X, r.Y, r.Z, Margin(1) << 4, playerCount) << 1;
        in1 = (ushort)(by + Y(2));
        in2 = (ushort)(in2 + step);
        r = Rotate(rotation, (short)in0, (short)in1, (short)in2);
        if (r.Flag == 0) result |= InViewCone(r.X, r.Y, r.Z, Margin(2) << 4, playerCount) << 2;
        in0 = (ushort)(in0 + step);
        in1 = (ushort)(by + Y(4));
        r = Rotate(rotation, (short)in0, (short)in1, (short)in2);
        if (r.Flag == 0) result |= InViewCone(r.X, r.Y, r.Z, Margin(4) << 4, playerCount) << 4;
        in1 = (ushort)(by + Y(3));
        in2 = (ushort)(in2 - step);
        r = Rotate(rotation, (short)in0, (short)in1, (short)in2);
        if (r.Flag == 0) result |= InViewCone(r.X, r.Y, r.Z, Margin(3) << 4, playerCount) << 3;
        return result;
    }

    /// <summary>func_800640D8: mvmva without translation, stores (IR1, -IR2, IR3), returns FLAG.</summary>
    public static (short X, short Y, short Z, uint Flag) Rotate(Gte.Loaded rotation, short x, short y, short z)
    {
        // Gte.Mvmva already returns the stored triple (IR1, -IR2, IR3); FLAG bit 31 is the OR of the
        // IR saturation bits, so "FLAG == 0" is the same test on the bits Mvmva reports.
        var m = Gte.Mvmva(rotation, x, y, z);
        return (m.X, m.Y, m.Z, m.Flag);
    }

    /// <summary>func_80063FF8: depth and view-cone test of a rotated point with a safety margin.</summary>
    public static int InViewCone(short x, short y, short z, int margin, int playerCount)
    {
        int depth = z << 4;
        if (depth + margin < 0x601) return 0;
        int limit, extentX;
        if (playerCount == 1)
        {
            limit = depth + ((z << 6) + (margin << 4) + ((margin + margin * 2) >> 2));
            extentX = x << 8;
        }
        else
        {
            limit = depth + ((z << 6) + (margin << 5) + ((margin + margin * 2) >> 3));
            extentX = x << 9;
        }
        if (extentX < 0) extentX = -extentX;
        if (limit < extentX) return 0;
        int extentY = y << 10;
        if (extentY < 0) extentY = -extentY;
        depth = (depth << 4) - depth + (margin << 6) + (((margin * 8 - margin) * 16 - margin) >> 6);
        return depth >= extentY ? 1 : 0;
    }
}
