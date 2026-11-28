namespace FutureCop.Assets;

/// <summary>
/// Coarse height grid of a mission: the s8 sample block func_800695A8 (gBuildCoarseHeightGrid)
/// builds from the Cptc/Ctil terrain and func_80069A38 (gSampleHeightGridBilinear) reads
/// bilinearly. The grid lives in the descriptor D_800A2940 (+0x00 origin, +0x04 row stride,
/// +0x08 last x cell, +0x0C sample pointer) and is rebuilt whenever a mission is loaded.
/// </summary>
/// <remarks>
/// One grid cell is one map cell (0x20000 world units) split into 8 x 8 samples, so a sample is
/// 1/8 cell apart (0x4000). Rows run along world x, columns along world z (the same 16.14
/// coordinates func_80069A38 subtracts its origin from). The builder first takes the maximum of
/// each 2 x 2 group of the section's 17 x 17 x 3-byte vertex block and then runs the 3 x 3
/// binomial filter with edge replication over that block.
/// </remarks>
public sealed class HeightGrid
{
    /// <summary>func_80069A38 returns this when the point falls outside the sampled cells.</summary>
    public const int OutOfBounds = 0x8000;

    private readonly sbyte[] _samples;

    /// <summary>Descriptor +0x00: world coordinate of sample (0, 0), in 16.14.</summary>
    public int Origin { get; }

    /// <summary>Descriptor +0x04: samples along world z (the row stride of the sample block).</summary>
    public int Width { get; }

    /// <summary>Descriptor +0x08: highest valid x cell; func_80069A38 rejects larger x cells.</summary>
    public int LastCellX { get; }

    /// <summary>Samples along world x: the producer stores H = cellsX * 8 rows.</summary>
    public int Rows => _samples.Length / Width;

    public HeightGrid(int origin, int width, int lastCellX, sbyte[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (width < 1 || samples.Length % width != 0)
            throw new ArgumentException("sample block does not divide into rows of the given width", nameof(samples));
        Origin = origin;
        Width = width;
        LastCellX = lastCellX;
        _samples = samples;
    }

    /// <summary>func_80069A38 gSampleHeightGridBilinear over a world position (only x and z are read).</summary>
    public int Sample(int[] pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return Sample(pos[0], pos[2]);
    }

    /// <summary>func_80069A38 over world x and z in 16.14: two rounded-down interpolations, s8 samples.</summary>
    public int Sample(int x, int z)
    {
        unchecked
        {
            int dx = x - Origin;
            int dz = z - Origin;
            int cellX = dx >> 14;
            int cellZ = dz >> 14;
            dx &= 0x3FFF;
            dz &= 0x3FFF;
            // Retail bounds: 0 <= z cell <= width - 2 and 0 <= x cell <= descriptor +0x08.
            if (cellZ < 0 || cellZ > Width - 2) return OutOfBounds;
            if (cellX < 0 || cellX > LastCellX) return OutOfBounds;

            int p = cellX * Width + cellZ;
            int near = (_samples[p] << 8) + (((_samples[p + 1] << 8) - (_samples[p] << 8)) * dz >> 14);
            int next = p + Width;
            int far = (_samples[next] << 8) + (((_samples[next + 1] << 8) - (_samples[next] << 8)) * dz >> 14);
            return near + (((far - near) * dx) >> 14);
        }
    }

    /// <summary>
    /// func_800695A8 gBuildCoarseHeightGrid: samples every GRDB cell inside the border (maximum of
    /// the 2 x 2 vertex group), filters the block, and returns the descriptor the sampler reads.
    /// Returns null when the header is too small for its border.
    /// </summary>
    /// <remarks>
    /// The decompiled C keeps the cell and sample indices in overlapping register-asm temporaries
    /// (the reason tests/host/LEAF_SUITES.md dropped 800695A8); the indices here follow the MIPS
    /// listing instead: cell (x, z) of the cell grid reads map cell (x + border - 1, z + border - 1)
    /// and writes the 8 x 8 block at x * 8, z * 8 of a W x H byte buffer.
    /// </remarks>
    public static HeightGrid? Build(LevelMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        MapHeader header = map.Header;
        int cellsZ = header.Width - 2 * header.Border + 1;
        int cellsX = header.Height - 2 * header.Border + 1;
        if (cellsZ < 1 || cellsX < 1) return null;
        int baseCell = header.Border - 1;
        int width = cellsZ << 3;
        int rows = cellsX << 3;

        var coarse = new sbyte[width * rows];
        for (int cellX = 0; cellX < cellsX; cellX++)
        {
            for (int cellZ = 0; cellZ < cellsZ; cellZ++)
            {
                LevelSection section = map.Section(header.SectionIdAt(cellX + baseCell, cellZ + baseCell))
                    ?? throw new InvalidDataException(
                        $"GRDB cell ({cellX + baseCell}, {cellZ + baseCell}) references a section that is not loaded");
                int rowBase = (cellX << 3) * width + (cellZ << 3);
                for (int sy = 0; sy < 8; sy++)
                {
                    for (int sx = 0; sx < 8; sx++)
                    {
                        // Vertex pairs: rows 2*sy, 2*sy+1 and columns 2*sx, 2*sx+1 of the 17 x 17
                        // block, 6 bytes per row (three signed bytes per vertex).
                        int vertex = sy * 0x66 + sx * 6;
                        int max = -128;
                        for (int k = 0; k < 6; k++)
                        {
                            max = Math.Max(max, section.Height(vertex + k));
                            max = Math.Max(max, section.Height(vertex + 0x33 + k));
                        }
                        coarse[rowBase + sy * width + sx] = (sbyte)max;
                    }
                }
            }
        }

        var samples = new sbyte[width * rows];
        for (int x = 0; x < rows; x++)
        {
            int above = Math.Max(x - 1, 0) * width;
            int mid = x * width;
            int below = Math.Min(x + 1, rows - 1) * width;
            for (int z = 0; z < width; z++)
            {
                int left = Math.Max(z - 1, 0);
                int right = Math.Min(z + 1, width - 1);
                int sum = coarse[above + left] + 2 * coarse[above + z] + coarse[above + right]
                        + 2 * (coarse[mid + left] + 2 * coarse[mid + z] + coarse[mid + right])
                        + coarse[below + left] + 2 * coarse[below + z] + coarse[below + right];
                sum = (sum + 8) >> 4;
                int value = Math.Clamp(sum, -128, 127);
                // The filter never lifts a sample above the pre-filter maximum of its centre.
                if (value < coarse[mid + z]) value = coarse[mid + z];
                samples[mid + z] = (sbyte)value;
            }
        }

        int origin = unchecked(((header.Border - 1) << 17) + 0x2000);
        return new HeightGrid(origin, width, rows - 2, samples);
    }
}
