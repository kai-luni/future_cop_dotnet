using System.Globalization;
using FutureCop.Assets;
using FutureCop.Game.Mech;
using Xunit;

namespace FutureCop.Assets.Tests;

public class EnvironmentVectorTests
{
    public static IEnumerable<object[]> Cases(string tag)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "golden", "env.vectors");
        return File.ReadLines(path)
            .Select(line => line.Split('#', 2)[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(tokens => tokens.Length > 0 && tokens[0] == tag)
            .Select(tokens => new object[] { tokens.Skip(1).Select(Number).ToArray() });
    }

    private static int Number(string token)
    {
        if (token.StartsWith("-0x", StringComparison.Ordinal))
            return -int.Parse(token.AsSpan(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (token.StartsWith("0x", StringComparison.Ordinal))
            return int.Parse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return int.Parse(token, CultureInfo.InvariantCulture);
    }

    private static Gte.Mat Matrix(int[] v) => new(v[..9], v[9], v[10], v[11]);

    private static Vertex VertexAt(int[] v, int index) =>
        new((short)v[index], (short)v[index + 1], (short)v[index + 2]);

    [Theory, MemberData(nameof(Cases), "SINCOS")]
    public void RenderSinCosMatchesRetail(int[] v)
    {
        Assert.Equal(3, v.Length);
        Assert.Equal((v[1], v[2]), ScriptRender.SinCos(v[0]));
    }

    [Theory, MemberData(nameof(Cases), "ROT")]
    public void WalkerRotationMatchesRetailAndLeavesTranslationUntouched(int[] v)
    {
        Assert.Equal(12, v.Length);
        const int untouched = 0x5A5A5A5A;
        int[] matrix = Enumerable.Repeat(untouched, 12).ToArray();
        MechWalker.RotationMatrix(v[0], v[1], v[2], matrix);
        Assert.Equal(v[3..], matrix[..9]);
        Assert.All(matrix[9..], word => Assert.Equal(untouched, word));
    }

    [Theory, MemberData(nameof(Cases), "ROTV")]
    public void MixedAngleRotationMatchesRetail(int[] v)
    {
        Assert.Equal(12, v.Length);
        Assert.Equal(v[3..], ScriptRender.Rotation(v[0], v[1], v[2]).W);
    }

    [Theory, MemberData(nameof(Cases), "LOADMAT")]
    public void MatrixLoadMatchesRetailPackedRegisters(int[] v)
    {
        Assert.Equal(20, v.Length);
        var actual = Gte.Load(Matrix(v));
        var expectedRotation = new short[9];
        for (int i = 0; i < expectedRotation.Length; i++)
            expectedRotation[i] = (short)(v[12 + i / 2] >> (16 * (i % 2)));
        Assert.Equal(expectedRotation, actual.R);
        Assert.Equal((v[17], v[18], v[19]), (actual.Trx, actual.Try, actual.Trz));
    }

    [Theory, MemberData(nameof(Cases), "XF10C")]
    public void RotationOnlyTransformIgnoresLoadedTranslation(int[] v)
    {
        Assert.Equal(25, v.Length);
        var loaded = Gte.Load(Matrix(v));
        for (int i = 0; i < v[12]; i++)
        {
            var vertex = VertexAt(v, 13 + i * 3);
            var (x, y, z, _) = Gte.Mvmva(loaded, vertex.X, vertex.Y, vertex.Z);
            int expected = 19 + i * 3;
            Assert.Equal((v[expected], v[expected + 1], v[expected + 2]), ((int)x, (int)y, (int)z));
        }
    }

    [Theory, MemberData(nameof(Cases), "PROJ")]
    public void ModelProjectionMatchesRetailPointsAndClipBytes(int[] v)
    {
        Assert.Equal(30, v.Length);
        var vertices = Enumerable.Range(0, v[16]).Select(i => VertexAt(v, 17 + i * 3)).ToArray();
        var actual = ModelRender.Project(Gte.Load(Matrix(v)), vertices, v[12], v[13], v[14], v[15]);
        Assert.Equal(vertices.Length, actual.Length);
        for (int i = 0; i < actual.Length; i++)
        {
            // Retail leaves rejected coordinates untouched. The port allocates a zero-filled buffer
            // and does not expose the batch's raw GTE status word.
            const int untouched = 0x5A5A5A5A;
            int xy = v[23 + i * 2], depth = v[24 + i * 2];
            Assert.Equal(new ScreenVertex(xy == untouched ? 0 : xy, depth == untouched ? 0 : depth,
                (byte)v[27 + i]), actual[i]);
        }
    }

    [Theory, MemberData(nameof(Cases), "COMPOSE")]
    public void BoneCompositionMatchesRetail(int[] v)
    {
        Assert.Equal(32, v.Length);
        short[] table = { 0, 0, 0x100, 16, -16, 0x400, 0, 0 };
        var entry = new ScriptEntry(1, 0, 0, 0, 0, 0, 0, (byte)v[12],
            (short)v[14], (short)v[15], (short)v[16], (short)v[17], (short)v[18], (short)v[19]);
        var actual = ScriptRender.Compose(ScriptRender.LocalMatrix(entry, table, v[13]), Matrix(v));
        Assert.Equal(v[20..29], actual.W);
        Assert.Equal((v[29], v[30], v[31]), (actual.Tx, actual.Ty, actual.Tz));
    }
}
