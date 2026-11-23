using FutureCop.Game.Mech;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>Input vectors captured from the matching C units by the MIPS host harness.</summary>
public class MechInputVectorTests
{
    private static IEnumerable<object[]> Cases(string kind)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "golden", "mech-input.vectors");
        foreach (string line in File.ReadLines(path))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0] != kind) continue;
            int arrow = Array.IndexOf(parts, "->");
            Assert.True(arrow > 0, line);
            long Parse(string s) => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt64(s[2..], 16) : long.Parse(s);
            yield return new object[] { parts[1..arrow].Select(Parse).ToArray(), parts[(arrow + 1)..].Select(Parse).ToArray() };
        }
    }

    public static IEnumerable<object[]> StickCases() => Cases("STICK");
    public static IEnumerable<object[]> PadCases() => Cases("PAD");

    [Theory, MemberData(nameof(StickCases))]
    public void StickMatchesC(long[] v, long[] expected)
    {
        var pad = new AnalogPad((int)v[0], (byte)v[1], (byte)v[2]);
        uint flags = (uint)v[6];
        int vertical = 0x5a5a, horizontal = 0x5a5a;
        int status = MechInput.ReadStick(pad, v[3] != 0, v[4] != 0, ref flags, ref vertical, ref horizontal);
        Assert.Equal(expected, new long[] { status, flags, vertical, horizontal });
    }

    [Theory, MemberData(nameof(PadCases))]
    public void PadMatchesC(long[] v, long[] expected)
    {
        uint a = MechInput.MapPadButtons((ushort)v[0], PadMapping.Default);
        uint b = MechInput.MapPadButtons((ushort)v[1], PadMapping.Default);
        Assert.Equal(expected, new long[] { a, b, MechInput.TranslatePadButtons(a, (int)v[2], b), MechInput.TranslatePadButtons(b, (int)v[2], b) });
    }

    [Fact]
    public void RefreshPadWordsMatchesFunc80057864ForJumpInputs()
    {
        uint[] words = new uint[2];
        MechInput.RefreshPadWords(words, PadButtons.Triangle, PadMapping.Default);
        Assert.Equal(0x01000000u, words[0]);
        Assert.Equal(0u, words[1]);
        Assert.Equal(0x08000000u, MechInput.TranslatePadButtons(words[0], players: 1, words[1]));

        MechInput.RefreshPadWords(words, PadButtons.Up | PadButtons.Triangle, PadMapping.Default);
        Assert.Equal(0x41000000u, words[0]);
        Assert.Equal(0x48000000u, MechInput.TranslatePadButtons(words[0], players: 1, words[1]));

        MechInput.RefreshPadWords(words, PadButtons.None, PadMapping.Default);
        Assert.Equal(new uint[] { 0u, 0u }, words);
        Assert.Equal(0u, MechInput.TranslatePadButtons(words[0], players: 1, words[1]));

        Assert.Throws<ArgumentException>(() => MechInput.RefreshPadWords(new uint[1], PadButtons.None, PadMapping.Default));
    }
}
