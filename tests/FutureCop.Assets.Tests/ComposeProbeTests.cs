using System.Globalization;
using FutureCop.Assets;
using Xunit;
namespace FutureCop.Assets.Tests;
public class ComposeProbeTests
{
    [Fact]
    public void OriginalCLocalTransformWithGteModelMatches()
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "golden", "compose.vectors");
        short[] table = Array.Empty<short>();
        int count = 0;
        foreach (string raw in File.ReadLines(path))
        {
            string text = raw.Trim();
            if (text.Length == 0 || text.StartsWith('#')) continue;
            string[] tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens[0] == "TABLE")
            {
                table = tokens.Skip(1)
                    .Select(t => (short)unchecked((int)uint.Parse(t, NumberStyles.HexNumber))).ToArray();
                continue;
            }
            int[] v = tokens.Select(t => unchecked((int)uint.Parse(t, NumberStyles.HexNumber))).ToArray();
            Assert.Equal(32, v.Length);
            Assert.True(table.Length > 0, "TABLE directive missing before the first case");
            var parent = new Gte.Mat(v.Take(9).ToArray(), v[9], v[10], v[11]);
            byte flags = (byte)v[12];
            int offset = v[13];
            var e = new ScriptEntry(1, 0, 0, 0, 0, 0, 0, flags,
                (short)v[14], (short)v[15], (short)v[16], (short)v[17], (short)v[18], (short)v[19]);
            var actual = ScriptRender.Compose(ScriptRender.LocalMatrix(e, table, offset), parent);
            Assert.Equal(v.Skip(20).Take(9), actual.W);
            Assert.Equal(v.Skip(29), new[] { actual.Tx, actual.Ty, actual.Tz });
            count++;
        }
        Assert.Equal(30, count);
    }
}
