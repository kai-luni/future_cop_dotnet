using System.Globalization;
using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Runs tests/golden/joints.vectors (fcop-joints-golden/v2) against the ported chain.
/// Every row carries the whole input, so the .NET suite and the C host harness execute
/// exactly the same cases; the C side runs original func_80052960.
/// </summary>
public class JointsProbeTests
{
    [Fact]
    public void JointMatricesMatchOriginalC()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "golden", "joints.vectors");
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
            Assert.True(table.Length > 0, "TABLE directive missing before the first case");
            int offset = v[1];
            int nrec = v[14];
            Assert.InRange(nrec, 1, 5);
            Assert.Equal(39 + 8 * nrec, v.Length);
            var model = new Model { MeshTable = table };
            for (int i = 0; i < nrec; i++)
            {
                int o = 15 + 8 * i;
                model.Hierarchy.Add(new ScriptEntry((byte)v[o], 0, 0, 0, 0, 0, 0, (byte)v[o + 1],
                    (short)v[o + 2], (short)v[o + 3], (short)v[o + 4],
                    (short)v[o + 5], (short)v[o + 6], (short)v[o + 7]));
            }
            var root = new Gte.Mat(v[2..11], v[11], v[12], v[13]);
            var (third, fifth) = PlayerAssembly.FindJoints(model, offset, root);
            int exp = 15 + 8 * nrec;
            AssertMatrix(v[exp..(exp + 12)], fifth);
            AssertMatrix(v[(exp + 12)..(exp + 24)], third);
            count++;
        }
        Assert.Equal(7, count);
    }

    private static void AssertMatrix(int[] expected, Gte.Mat? actual)
    {
        if (expected.All(x => x == 0x12345678)) { Assert.Null(actual); return; }
        Assert.NotNull(actual);
        Assert.Equal(expected, actual.Value.W.Concat(new[] { actual.Value.Tx, actual.Value.Ty, actual.Value.Tz }));
    }
}
