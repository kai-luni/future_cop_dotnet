using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>Oracle-pinned tests for the Precinct Assault enemy vector math ports.</summary>
public sealed class EnemyMathTests
{
    // ---- func_8001ACA4 gVectorAddScaled (src/game/8001ACA4.c:34-69) ----

    [Fact]
    public void AddScaled_ComponentsFollowRetailMiddleBits()
    {
        // out[i] = a[i] + bits 16..47 of b[i] * scale.
        int[] a = { 123456, -2000000, 0 };
        int[] b = { 0x10000, -0x8000, 0x1FFFF };
        EnemyMath.AddScaled(a, b, 0x18000, out var result);
        Assert.Equal(new[] { 221760, -2049152, 196606 }, result);
    }

    [Fact]
    public void AddScaled_NegativeScaleTakesMiddleBits()
    {
        int[] a = { 0, 0, 0 };
        int[] b = { 0x8000, 0x8000, 0x8000 };
        EnemyMath.AddScaled(a, b, -0x18000, out var result);
        Assert.Equal(new[] { -49152, -49152, -49152 }, result);
    }

    [Fact]
    public void AddScaled_AliasingAllowed()
    {
        // 8001ACA4.c: the out pointer may alias a; components are computed one at a time.
        int[] a = { 5, -6, 7 };
        int[] b = { 1, -2, 3 };
        EnemyMath.AddScaled(a, b, 5, out var result);
        Assert.Equal(new[] { 5, -7, 7 }, result);
    }


    // ---- func_8001AB78 gVectorScaleInPlace (src/game/8001AB78.c:37-48) ----

    [Fact]
    public void ScaleInPlace_MatchesHandwrittenAsm()
    {
        int[] v = { 0x10000, -0x18000, 0x7FFE };
        EnemyMath.ScaleInPlace(0x20000, v);
        Assert.Equal(new[] { 0x20000, -0x30000, 0xFFFC }, v);
    }

    [Fact]
    public void ScaleInPlace_UnityScaleIsIdentity()
    {
        // (hi << 16) | (lo >> 16) of v * 0x10000 is exactly v.
        int[] v = { 0x8000, 0xFFFF, -0x10000 };
        EnemyMath.ScaleInPlace(0x10000, v);
        Assert.Equal(new[] { 0x8000, 0xFFFF, -0x10000 }, v);
    }


    // ---- func_8001A750 gApproximateDistanceXzHalfMin (src/game/8001A750.c:33-52) ----

    [Theory]
    [InlineData(new[] { 0, 0, 0 }, new[] { 3, 0, 4 }, 6)]
    [InlineData(new[] { 10, 0, -7 }, new[] { -2, 0, 5 }, 18)]
    [InlineData(new[] { 5, 0, 5 }, new[] { 5, 0, 5 }, 0)]
    public void ApproxDistanceXz_MatchesOctagonalMetric(int[] a, int[] b, int expected)
    {
        Assert.Equal(expected, EnemyMath.ApproxDistanceXz(a, b));
    }


    // ---- func_80019F54 gApproxVectorLength (src/game/80019F54.c:44-71) ----

    [Theory]
    [InlineData(new[] { 0x10000, 0, 0 }, 0x10000)]
    [InlineData(new[] { 0x10000, 0x10000, 0x10000 }, 0x18000)]
    [InlineData(new[] { -0x400, 0x200, 0x80 }, 0x4A0)]
    public void ApproxVectorLength_MatchesSortAndQuarter(int[] v, int expected)
    {
        Assert.Equal(expected, EnemyMath.ApproxVectorLength(v));
    }


    // ---- func_8001A644 gScaleVector3ToBit15 (src/game/8001A644.c:34-60) ----

    [Theory]
    [InlineData(new[] { 0x1000, 0, 0 }, 4096, new[] { 0x8000, 0, 0 })]
    [InlineData(new[] { 0x10000, 0x10000, 0 }, 81920, new[] { 0x8000, 0x8000, 0 })]
    [InlineData(new[] { 0x20000000, 0, 0 }, 536870912, new[] { 0x8000, 0, 0 })]
    [InlineData(new[] { 0, 0, 0 }, 0, new[] { 0, 0, 0 })]
    public void ScaleVectorToBit15_MatchesLeadingBit(int[] input, int expectedLength, int[] expectedVector)
    {
        // gApproxVectorLength is computed before the shift and returned unshifted.
        long length = EnemyMath.ScaleVectorToBit15(input);
        Assert.Equal(expectedLength, length);
        Assert.Equal(expectedVector, input);
    }
}
