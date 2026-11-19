using FutureCop.Assets;

namespace FutureCop.Game.Mech;

/// <summary>
/// Result of gGetGroundHeight func_80066F78 with every output pointer set: the height, the
/// normalised surface normal (null when the retail code returns before writing it), the tile
/// attribute byte (0 unless the surface lies within 0x2000 below the probe limit) and whether
/// the dynamic-surface pointer (*a3) was written (it is left untouched on the early returns).
/// </summary>
public readonly record struct GroundProbe(int Height, Vec3? Normal, int Attribute, bool SurfaceWritten);

/// <summary>Ground query of the walker (func_80066F78 with normal, attribute and surface outputs).</summary>
public static class MechGround
{
    /// <summary>
    /// func_80066F78(position, normal, &amp;attribute, &amp;surface). The dynamic-object surface
    /// search func_8006320C (moving platforms, building tops registered as objects) is NOT ported:
    /// it is evaluated with an empty object queue, so the surface pointer is always 0 here.
    /// </summary>
    public static GroundProbe Probe(LevelMap map, Vec3 position)
    {
        unchecked
        {
            int limit = position.Y;
            int z = position.Z, x = position.X;
            var header = map.Header;
            if (z < 0) return new GroundProbe(0, null, 0, false);
            int cz = z >> 17;
            if (cz >= header.Width - 1) return new GroundProbe(0, null, 0, false);
            if (x < 0) return new GroundProbe(0, null, 0, false);
            int cx = x >> 17;
            if (cx >= header.Height - 1) return new GroundProbe(0, null, 0, false);
            int idx = cz + header.Width * cx;
            int a4 = (x >> 13) & 0xF;
            int a5 = a4 * 16 + ((z >> 13) & 0xF);
            var block = map.Section((int)((header.Entry(idx) >> 2) & 0x3FFF))
                ?? throw new InvalidDataException("ground cell names a section that is not loaded");
            int hw = block.U16(0x3CC + a5 * 2);
            limit += 0x200;
            int list = (hw >> 4) + 0x5D0;
            int heights = (a4 + a5) * 3;
            int cnt = hw & 0xF;
            if (cnt == 0) return new GroundProbe(0, null, 0, false);
            int lowest = 0x40000000, best = unchecked((int)0xC0000000);
            uint bestPoly = 0, lowestPoly = 0;
            int zl = (z << 3) & 0xFFFF, xl = (x << 3) & 0xFFFF;
            while (cnt > 0)
            {
                uint s0 = block.U32(list);
                list += 4;
                int type = (int)((s0 >> 15) & 0x7F);
                if (type >= 0x47) break;
                int quadrant = (int)((s0 >> 13) & 3);
                if (quadrant == 0 || (block.Byte(0x370 + quadrant) & 0x40) == 0)
                {
                    if (LevelGround.Sample(block, heights, type, zl, xl, out int val))
                    {
                        if (val < lowest) { lowest = val; lowestPoly = s0; }
                        if (val < limit && best < val) { best = val; bestPoly = s0; }
                    }
                }
                cnt--;
            }
            if (lowest == 0x40000000) return new GroundProbe(0, null, 0, false);
            if (best == unchecked((int)0xC0000000)) { best = lowest; bestPoly = lowestPoly; }
            // func_8006320C with an empty object queue returns 0: *a3 = 0, no override.
            int attribute = limit - best < 0x2000 ? block.Byte(0x370 + (int)((bestPoly >> 13) & 3)) : 0;
            return new GroundProbe(best, Normal(block, heights, (int)((bestPoly >> 15) & 0x7F), xl, zl), attribute, true);
        }
    }

    /// <summary>The normal block of func_80066F78 (edge cross product, then gNormalizeVector).</summary>
    private static Vec3 Normal(LevelSection block, int heights, int type, int xl, int zl)
    {
        unchecked
        {
            uint corners = TerrainTables.VertexIndices(type);
            int H(int i) => block.Height(heights + (int)((corners >> (8 * i)) & 0xFF));
            int a = H(0), b = H(1), c = H(2);
            int a3v = b - a, t0 = c - a;
            int t1 = 0, t3 = 5, s7 = 0, fp = 0, l38 = 0;
            if (type >= 0x3C)
            {
                if (xl < zl) { s7 = 0x20; fp = t3; l38 = 5; }
                else
                {
                    a3v = t0;
                    l38 = 5;
                    int d = H(3);
                    s7 = 5;
                    fp = 0x20;
                    t0 = d - a;
                }
            }
            else
            {
                switch (TerrainTables.TileShape[type] >> 3)
                {
                    case 0: fp = 0x20; s7 = 0x20; l38 = 5; break;
                    case 1: s7 = 5; l38 = 0x20; fp = 5; t1 = 1; break;
                    case 2: fp = 0x20; l38 = 5; s7 = 5; break;
                    case 3: s7 = 0x20; fp = 5; l38 = 5; break;
                    default: throw new InvalidDataException($"surface type {type}: shape mode has no case in func_80066F78");
                }
            }
            // MIPS sllv uses the low five bits of the amount (0x20 shifts by 0), as C# does.
            int n2 = (t0 << (s7 & 31)) - (a3v << (t3 & 31));
            int t2 = 1 << ((t3 + l38) & 31);
            int one = 1 << ((fp + s7) & 31);
            int n1;
            if (t1 != 0) { a3v = -a3v; n1 = t2 + one; }
            else n1 = t2 - one;
            int n0 = (a3v << (fp & 31)) - (t0 << (l38 & 31));
            var v = new[] { n0, n1, n2 };
            MechMath.Normalize(v);
            return new Vec3(v[0], v[1], v[2]);
        }
    }
}
