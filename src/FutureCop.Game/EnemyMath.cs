namespace FutureCop.Game;

/// <summary>Vector math units of the Precinct Assault enemy chain (ports of the retail
/// helper units without their own MechMath equivalent).
/// </summary>
public static class EnemyMath
{
    /// <summary>
    /// func_8001ACA4 gVectorAddScaled: out = a + b * scale in 16.16 fixed point,
    /// taking the middle 32 bits of the 64-bit product per component
    /// (src/game/8001ACA4.c:34-69). The out array may alias a.
    /// </summary>
    public static void AddScaled(int[] a, int[] b, int scale, out int[] result)
    {
        result = new int[3];
        for (int i = 0; i < 3; i++)
            result[i] = unchecked(a[i] + FmulMiddle32(b[i], scale));
    }

    /// <summary>The (hi &lt;&lt; 16) | (lo &gt;&gt; 16) idiom of a signed mult: bits 16..47 of the product.</summary>
    private static int FmulMiddle32(int a, int b) => unchecked((int)(((long)a * b) >> 16));

    /// <summary>
    /// func_8001AB78 gVectorScaleInPlace: multiplies the three components by the
    /// 16.16 scalar taking the middle 32 bits of the product (handwritten asm,
    /// src/game/8001AB78.c:37-48).
    /// </summary>
    public static void ScaleInPlace(int scale, int[] v)
    {
        for (int i = 0; i < 3; i++)
            v[i] = FmulMiddle32(v[i], scale);
    }

    /// <summary>
    /// func_8001A750 gApproximateDistanceXzHalfMin: max(abs(dx), abs(dz)) +
    /// ceil(min(abs(dx), abs(dz)) / 2) - the horizontal octagonal metric using
    /// the components at index 0 (x) and index 2 (z) (src/game/8001A750.c:33-52).
    /// </summary>
    public static int ApproxDistanceXz(int[] a, int[] b)
    {
        int x = a[0] - b[0];
        if (x < 0) x = -x;
        int z = unchecked(a[2] - b[2]);
        if (z < 0) z = -z;
        int sum = unchecked(x + z);
        int d = unchecked(x - z);
        int min = unchecked(((d >> 31) & d) + z);
        return unchecked(sum - (min >> 1));
    }

    /// <summary>
    /// func_80019F54 gApproxVectorLength: max + (mid + min) / 4 of the absolute
    /// components (src/game/80019F54.c:44-71).
    /// </summary>
    public static int ApproxVectorLength(int[] v)
    {
        int a = Math.Abs(v[0]);
        int b = Math.Abs(v[1]);
        int c = Math.Abs(v[2]);
        // Two conditional swaps sort a >= b >= c (signed negation is used ibid.).
        if (a < b) (a, b) = (b, a);
        if (a < c) (a, c) = (c, a);
        return unchecked(a + ((b + c) >> 2));
    }

    /// <summary>
    /// func_8001A644 gScaleVector3ToBit15: rescales the vector so the length
    /// estimate's leading bit sits at bit 15; returns the pre-shift estimate
    /// (src/game/8001A644.c:34-60). A zero vector is left unchanged.
    /// </summary>
    public static long ScaleVectorToBit15(int[] v)
    {
        int estimate = ApproxVectorLength(v);
        int shift = estimate != 0 ? 0x1F - LeadingZeros(estimate) : 0;
        if (shift >= 0x10)
        {
            int s = shift - 0xF;
            for (int i = 0; i < 3; i++) v[i] >>= s;
        }
        else
        {
            int s = 0xF - shift;
            for (int i = 0; i < 3; i++) v[i] = unchecked(v[i] << s);
        }
        return estimate;
    }

    private static int LeadingZeros(int value)
    {
        uint u = unchecked((uint)value);
        int n = 0;
        while (n < 32 && (u & 0x80000000u) == 0) { u <<= 1; n++; }
        return n;
    }
}
