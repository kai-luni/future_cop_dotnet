using FutureCop.Game.Mech;
using Xunit;

namespace FutureCop.Assets.Tests;

// Inputs and expectations from tests/host/leaf_more_harness.c:test_vectors (PS1 0a82cd1).
public class FixedPointLeafParityTests
{
    private const int Q16 = 1 << 16;

    [Theory]
    [InlineData(-3 * Q16, 4 * Q16, 12 * Q16, 169 * Q16, 25 * Q16)]
    [InlineData(200, -200, 200, 0, 0)]
    public void SquaredLengthsTruncateEachProduct(int x, int y, int z, int expected3, int expected2)
    {
        Assert.Equal(expected3, MechMath.SumSquares3(x, y, z));
        Assert.Equal(expected2, MechMath.SumSquares2(x, y));
    }

    [Theory]
    [InlineData(-Q16, 5 * Q16, 2 * Q16, -9 * Q16)]
    [InlineData(0, 0, 0, 0)]
    public void SignedDotProduct(int x, int y, int z, int expected) =>
        Assert.Equal(expected, MechMath.Dot(2 * Q16, -3 * Q16, 4 * Q16, x, y, z));

    // func_800187C8 gIntegerRoot: the 15 sqrt-q8 cases of tests/portable/a1-a2.v1.json. The root is
    // a Q8 square root of the unsigned 32-bit input, so 2 -> 362 and 0xFFFFFFFF saturates at 0xFFFFFF.
    [Theory]
    [InlineData(0L, 0u)]
    [InlineData(1L, 256u)]
    [InlineData(2L, 362u)]
    [InlineData(3L, 443u)]
    [InlineData(4L, 512u)]
    [InlineData(100L, 2560u)]
    [InlineData(255L, 4087u)]
    [InlineData(256L, 4096u)]
    [InlineData(65535L, 65535u)]
    [InlineData(65536L, 65536u)]
    [InlineData(1000000L, 256000u)]
    [InlineData(2147483647L, 11863283u)]
    [InlineData(2147483648L, 11863283u)]
    [InlineData(4294967294L, 16777215u)]
    [InlineData(4294967295L, 16777215u)]
    public void IntegerRootMatchesThePortableQ8Cases(long value, uint expected) =>
        Assert.Equal(expected, MechMath.IntegerRoot((uint)value));

    // func_8001A074: all eight cases from leaf_extra_harness.c.
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(100, 0, 100)]
    [InlineData(0, -100, 100)]
    [InlineData(100, 50, 112)]
    [InlineData(100, 51, 112)]
    [InlineData(100, 100, 141)]
    [InlineData(-300, 400, 506)]
    [InlineData(1000, 1000, 1414)]
    public void ApproximateLengthMatchesRetail(int x, int y, int expected) =>
        Assert.Equal(expected, MechMath.ApproxLength2(x, y));

    // func_8001A100: axis cases from leaf_century_d_harness.c.
    [Theory]
    [InlineData(0, 0, 50, 50)]
    [InlineData(0, 0, 33, 33)]
    [InlineData(0, 47, 0, 47)]
    public void VectorLengthPreservesSingleAxis(int x, int y, int z, int expected) =>
        Assert.Equal(expected, MechMath.VectorLength3D(x, y, z));

    // func_8001A6F8: both ease-vector steps from tests/portable/a1-a2.v1.json.
    [Theory]
    [InlineData(400, 0, 0, 0, 100, -100, 100, 75, -75)]
    [InlineData(0, 0, 7, -1, 1, 7, -1, 0, 7)]
    public void EaseUpdatesOnlyDestination(int sx, int sy, int sz, int x, int y, int z, int ex, int ey, int ez)
    {
        int[] src = { sx, sy, sz }, dst = { x, y, z };
        MechMath.EaseVectorQuarter(src, dst);
        Assert.Equal(new[] { ex, ey, ez }, dst);
        Assert.Equal(new[] { sx, sy, sz }, src);
    }

    [Fact]
    public void ScaleTwoWordsInPlace()
    {
        int[] v = { 3, -3, 0x1234 };
        MechMath.Scale2(-Q16 / 2, v);
        Assert.Equal(new[] { -2, 1, 0x1234 }, v);
        MechMath.Scale2(0, v);
        Assert.Equal(new[] { 0, 0, 0x1234 }, v);
    }
}
