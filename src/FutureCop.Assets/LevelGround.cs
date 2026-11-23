namespace FutureCop.Assets;

/// <summary>
/// Ground height under a world position: gGetGroundHeight func_80066F78 (with output == NULL and
/// a3 == NULL, so neither the normal nor the dynamic-surface lookup func_8006320C runs) and the
/// surface sample func_80067480. Heights are world units (height byte &lt;&lt; 8), y up.
/// </summary>
public static class LevelGround
{
    /// <summary>Static surface by rank, as selected for player placement by func_80067684.</summary>
    public static int? PlacementHeight(LevelMap map, int x, int z, int rank)
    {
        var header = map.Header;
        if (x < 0 || z < 0 || (x >> 17) >= header.Height - 1 || (z >> 17) >= header.Width - 1)
            return null;
        int cell = (z >> 17) + header.Width * (x >> 17);
        var block = map.Section((int)((header.Entry(cell) >> 2) & 0x3FFF))
            ?? throw new InvalidDataException("ground cell names a section that is not loaded");
        int tx = (x >> 13) & 0xF, tz = (z >> 13) & 0xF;
        int tile = tx * 16 + tz;
        int descriptor = block.U16(0x3CC + tile * 2);
        int list = 0x5D0 + (descriptor >> 4);
        Span<int> heights = stackalloc int[15];
        int count = 0;
        for (int i = 0; i < (descriptor & 0xF); i++)
        {
            int type = (int)((block.U32(list + i * 4) >> 15) & 0x7F);
            if (type >= 0x47) break;
            if (Sample(block, (tx + tile) * 3, type, (z << 3) & 0xFFFF, (x << 3) & 0xFFFF, out int height))
                heights[count++] = height;
        }
        if (count == 0) return null;
        heights[..count].Sort();
        return rank switch
        {
            -2 => heights[0],
            -1 => heights[count - 1],
            >= 0 and <= 2 when rank < count => heights[rank],
            _ => null,
        };
    }

    /// <summary>func_80066F78(position, NULL, &amp;attribute, NULL).</summary>
    public static int Height(LevelMap map, int x, int y, int z, out int attribute)
    {
        attribute = 0;
        int limit = y;
        if (z < 0) return 0;
        int cz = z >> 17;
        var header = map.Header;
        if (cz >= header.Width - 1) return 0;
        if (x < 0) return 0;
        int cx = x >> 17;
        if (cx >= header.Height - 1) return 0;
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
        if (cnt == 0) return 0;
        int s2 = 0x40000000, s4 = unchecked((int)0xC0000000);
        uint l30 = 0, l34 = 0;
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
                if (Sample(block, heights, type, zl, xl, out int val))
                {
                    if (val < s2) { s2 = val; l34 = s0; }
                    if (val < limit && s4 < val) { s4 = val; l30 = s0; }
                }
            }
            cnt--;
        }
        if (s2 == 0x40000000) return 0;
        if (s4 == unchecked((int)0xC0000000)) { s4 = s2; l30 = l34; }
        if (limit - s4 < 0x2000) attribute = block.Byte(0x370 + (int)((l30 >> 13) & 3));
        return s4;
    }

    /// <summary>func_80067480: height inside one surface of a sub-tile (x, y = 16-bit fractions).</summary>
    public static bool Sample(LevelSection block, int heights, int index, int x, int y, out int result)
    {
        uint p = TerrainTables.VertexIndices(index);
        int H(int i) => block.Height(heights + (int)((p >> (8 * i)) & 0xFF));
        int a = H(0), b = H(1), c = H(2);
        int left, right;
        if (index >= 60)
        {
            int d = H(3);
            left = (a << 7) + (((b - a) * x) >> 9);
            right = (d << 7) + (((c - d) * x) >> 9);
        }
        else
        {
            switch (TerrainTables.TileShape[index] >> 3)
            {
                case 0:
                    if (y + x > 65536) { result = 0; return false; }
                    left = (a << 7) + (((b - a) * x) >> 9);
                    right = (c << 7) + (((b - c) * x) >> 9);
                    break;
                case 1:
                    if (y + x <= 65535) { result = 0; return false; }
                    left = (c << 7) + (((a - c) * x) >> 9);
                    right = (c << 7) + (((b - c) * x) >> 9);
                    break;
                case 2:
                    if (y < x) { result = 0; return false; }
                    left = (a << 7) + (((b - a) * x) >> 9);
                    right = (c << 7) + (((b - c) * x) >> 9);
                    break;
                case 3:
                    if (x < y) { result = 0; return false; }
                    left = (a << 7) + (((b - a) * x) >> 9);
                    right = (a << 7) + (((c - a) * x) >> 9);
                    break;
                default:
                    throw new InvalidDataException($"surface type {index}: shape mode has no case in func_80067480");
            }
        }
        result = (left << 1) + (((right - left) * y) >> 15);
        return true;
    }
}
