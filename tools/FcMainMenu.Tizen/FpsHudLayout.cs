using System;
using System.Collections.Generic;

namespace FcMainMenu;

internal static class FpsHudLayout
{
    internal readonly record struct Run(int X, int Y, int Width);

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['S'] = new[] {"011", "100", "010", "001", "110"},
        ['U'] = new[] {"101", "101", "101", "101", "111"},
        ['B'] = new[] {"110", "101", "110", "101", "110"},
        ['F'] = new[] {"111", "100", "110", "100", "100"},
        ['P'] = new[] {"110", "101", "110", "100", "100"},
        [':'] = new[] {"000", "010", "000", "010", "000"},
        ['.'] = new[] {"000", "000", "000", "000", "100"},
        ['-'] = new[] {"000", "000", "111", "000", "000"},
        [' '] = new[] {"000", "000", "000", "000", "000"},
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

    internal static IReadOnlyList<Run> Runs(string text)
    {
        var result = new List<Run>();
        for (int i = 0; i < text.Length; i++)
        {
            var glyph = Glyphs[text[i]];
            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 3;)
                {
                    if (glyph[y][x] != '1') { x++; continue; }
                    int start = x;
                    while (x < 3 && glyph[y][x] == '1') x++;
                    result.Add(new Run(i * 4 + start, y, x - start));
                }
            }
        }
        return result;
    }
}
