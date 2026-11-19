using System;
using System.Collections.Generic;

internal static class BitmapHud
{
    private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        ['F'] = new[] {"111", "100", "110", "100", "100"},
        ['P'] = new[] {"110", "101", "110", "100", "100"},
        ['S'] = new[] {"011", "100", "010", "001", "110"},
        [':'] = new[] {"0", "1", "0", "1", "0"},
        ['.'] = new[] {"0", "0", "0", "0", "1"},
        ['-'] = new[] {"000", "000", "111", "000", "000"},
        [' '] = new[] {"0", "0", "0", "0", "0"},
        ['0'] = new[] {"111", "101", "101", "101", "111"},
        ['1'] = new[] {"010", "110", "010", "010", "111"},
        ['2'] = new[] {"110", "001", "010", "100", "111"},
        ['3'] = new[] {"110", "001", "010", "001", "110"},
        ['4'] = new[] {"101", "101", "111", "001", "001"},
        ['5'] = new[] {"111", "100", "110", "001", "110"},
        ['6'] = new[] {"011", "100", "111", "101", "111"},
        ['7'] = new[] {"111", "001", "010", "010", "010"},
        ['8'] = new[] {"111", "101", "111", "101", "111"},
        ['9'] = new[] {"111", "101", "111", "001", "110"}
    };

    public static void Draw(byte[] rgb, int width, int height, string text)
    {
        const int scale = 2, margin = 4, padding = 3;
        int textWidth = 0;
        foreach (char c in text) textWidth += (Glyphs[c][0].Length + 1) * scale;
        int startX = Math.Max(0, width - margin - textWidth);
        for (int y = Math.Max(0, margin - padding); y < Math.Min(height, margin + 5 * scale + padding); y++)
            for (int x = Math.Max(0, startX - padding); x < Math.Min(width, width - margin + padding); x++)
            {
                int i = (y * width + x) * 3;
                rgb[i] = (byte)(rgb[i] / 3);
                rgb[i + 1] = (byte)(rgb[i + 1] / 3);
                rgb[i + 2] = (byte)(rgb[i + 2] / 3);
            }
        int pen = startX;
        foreach (char c in text)
        {
            string[] glyph = Glyphs[c];
            for (int gy = 0; gy < 5; gy++)
                for (int gx = 0; gx < glyph[gy].Length; gx++)
                    if (glyph[gy][gx] == '1')
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++)
                            {
                                int x = pen + gx * scale + dx, y = margin + gy * scale + dy;
                                if (x < width && y < height)
                                {
                                    int i = (y * width + x) * 3;
                                    rgb[i] = rgb[i + 1] = rgb[i + 2] = 255;
                                }
                            }
            pen += (glyph[0].Length + 1) * scale;
        }
    }
}
