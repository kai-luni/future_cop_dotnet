using FutureCop.Game.Mech;

namespace FutureCop.Game;

/// <summary>
/// The lob / arc solver <c>gSolveLobVector</c> (func_8005E624), which turns a source point, a target
/// point and two descriptor words into a launch velocity with a vertical component.
/// </summary>
/// <remarks>
/// <para>
/// This is a line-faithful port, not a physics model: the retail routine has no gravity constant of
/// its own. What it computes is a planar unit vector scaled by <c>angle * 300</c>, tilted upward by an
/// elevation the function derives from the planar distance, the height difference and the
/// <c>scale</c> argument. The arc itself is integrated later by the projectile mover, whose
/// per-tick downward bias lives in func_80036BD4:136-137 (gGuidedProjectileUpdate,
/// <c>y -= (descent * delta) / 300</c>, the descent word coming from the descriptor).
/// </para>
/// <para>
/// Source of every step: <c>src/game/8005E624.c</c>. Line references below point at that file. The
/// helpers it calls are <c>func_8001A7AC</c> (<see cref="MechMath.Length2"/>), <c>func_80095900</c>
/// (<see cref="MechMath.Sqrt12"/>), <c>func_8001ABE0</c> (<see cref="MechMath.Scale2"/>),
/// <c>func_80095C30</c> (<see cref="Atan2"/>) and <c>func_8001846C</c>
/// (<see cref="MechMath.SinCos"/>).
/// </para>
/// <para>
/// All quotients use the retail reciprocal idiom <c>MULU16(a, 0x80000000 / b)</c> - a 32-bit
/// <c>divu</c> followed by the middle 32 bits of a 64-bit unsigned product - because the rounding of
/// that pair differs from a plain division.
/// </para>
/// </remarks>
public static class LobMotion
{
    /// <summary>The <c>angle * 300</c> factor of func_8005E624:110.</summary>
    public const int SpeedScale = 300;

    /// <summary>func_80095C30 <c>gAtan2</c>: 12-bit angle from the +y argument toward the +x one.</summary>
    /// <remarks>
    /// 80095C30.c:46-84: fold the signs out, divide the smaller magnitude by the larger &gt;&gt; 10 (or
    /// shift the numerator left by 10 first when the large value would overflow that division), index
    /// <c>D_8009E0A0</c>, use the entry when |x| &lt; |y| and <c>0x400 - entry</c> otherwise, then
    /// mirror with <c>0x800 - result</c> for a negative y and negate for a negative x.
    /// </remarks>
    public static int Atan2(int x, int y)
    {
        bool negateY = false, negateX = false;
        if (y < 0) { negateY = true; y = -y; }
        if (x < 0) { negateX = true; x = -x; }
        if (y == 0 && x == 0) return 0;
        int result;
        if (x < y)
        {
            int index = (x & 0x7FE00000) != 0 ? x / (y >> 10) : (x << 10) / y;
            result = MechTables.Atan(index);
        }
        else
        {
            int index = (y & 0x7FE00000) != 0 ? y / (x >> 10) : (y << 10) / x;
            result = 0x400 - MechTables.Atan(index);
        }
        if (negateY) result = 0x800 - result;
        if (negateX) result = -result;
        return result;
    }

    /// <summary>
    /// func_8005E624:79-196. Writes the launch vector into <paramref name="out"/>
    /// (<c>x</c>, <c>y</c>, <c>z</c> in 16.16) and touches nothing else.
    /// </summary>
    /// <param name="angle">Descriptor word def + 0x12; the magnitude becomes <c>angle * 300</c>.</param>
    /// <param name="src">Launch position (<c>x</c>, <c>y</c>, <c>z</c>).</param>
    /// <param name="dst">Aim point (<c>x</c>, <c>y</c>, <c>z</c>).</param>
    /// <param name="scale">Descriptor word def + 0x16 (the arc word), passed as <c>&lt;&lt; 4</c>.</param>
    /// <returns>The three 16.16 velocity words.</returns>
    public static int[] Solve(int angle, int[] src, int[] dst, int scale)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(dst);
        if (src.Length < 3) throw new ArgumentException("src needs three words", nameof(src));
        if (dst.Length < 3) throw new ArgumentException("dst needs three words", nameof(dst));

        // 8005E624:102-104 - the planar delta is (dx, dz); y is handled separately.
        var vec = new[] { unchecked(dst[0] - src[0]), unchecked(dst[2] - src[2]) };
        int len = MechMath.Length2(vec);
        if (len < 2) len = 2;
        int dy = unchecked(dst[1] - src[1]);

        int s1 = unchecked(angle * SpeedScale);
        int t = MechMath.Fmul(scale, len);
        int q = unchecked(MechMath.Fmul(s1, s1) >> 1);
        // A zero divisor makes the retail divu return 0xFFFFFFFF (MIPS divu-by-zero), which the
        // MULU16 then turns into a saturated product. Kept explicit so the port never divides by zero.
        int d = MechMath.FmulU(t, Divu(0x80000000u, unchecked((uint)q)));
        int s0 = d / 2;

        // 8005E624:117-145 - v = s0 +/- dy / (len / 2); the sign follows dy, and the four branches
        // only differ in how the negative half-length is formed.
        int v;
        if (dy > 0)
        {
            int half = len > 0 ? len >> 1 : (-len) >> 1;
            v = s0 + MechMath.FmulU(dy, Divu(0x80000000u, unchecked((uint)half)));
        }
        else
        {
            int ndy = unchecked(-dy);
            int half = len > 0 ? len >> 1 : (-len) >> 1;
            v = s0 - MechMath.FmulU(ndy, Divu(0x80000000u, unchecked((uint)half)));
        }

        // 8005E624:147-154 - a0 = 0x10000 - 4 * s0 * v, clamped at 0, root of it, plus 0x10000.
        int q2 = MechMath.Fmul(s0, v);
        int a0 = unchecked(0x10000 - (q2 << 2));
        a0 = a0 < 0 ? 0 : unchecked(MechMath.Sqrt12(a0) << 2);
        a0 = unchecked(a0 + 0x10000);

        // 8005E624:156-179 - rescale a0 by 1 / s0, sign-mirrored through the two branches.
        int v2 = unchecked(s0 << 1);
        int half4;
        if (a0 > 0)
        {
            if (v2 > 0) { half4 = v2 >> 1; a0 = MechMath.FmulU(a0, Divu(0x80000000u, unchecked((uint)half4))); }
            else { half4 = (-v2) >> 1; a0 = -MechMath.FmulU(a0, Divu(0x80000000u, unchecked((uint)half4))); }
        }
        else
        {
            a0 = unchecked(-a0);
            if (v2 > 0) { half4 = v2 >> 1; a0 = -MechMath.FmulU(a0, Divu(0x80000000u, unchecked((uint)half4))); }
            else { half4 = (-v2) >> 1; a0 = MechMath.FmulU(a0, Divu(0x80000000u, unchecked((uint)half4))); }
        }

        // 8005E624:181-196 - elevation, sine/cosine, rescale the planar unit vector, write the result.
        int phi = Atan2(a0, 0x10000);
        var (sin, cos) = MechMath.SinCos(unchecked((phi << 12) >> 12));
        if (cos < 0) cos = unchecked(-cos);
        int q4 = MechMath.Fmul(cos, s1);
        MechMath.Scale2(q4, vec);
        return new[] { vec[0], MechMath.Fmul(sin, s1), vec[1] };
    }

    /// <summary>MIPS <c>divu</c>: division by zero yields 0xFFFFFFFF in LO.</summary>
    /// <remarks>
    /// 8005E624:118 divides <c>k / (u32)q</c> with <c>k = 0x80000000</c>. The port keeps that
    /// dividend fixed and only varies the divisor, so the helper takes the divisor as a signed word
    /// reinterpreted as unsigned - the C casts it to <c>u32</c> in exactly the same places.
    /// </remarks>
    private static int Divu(uint value, uint divisor) =>
        divisor == 0 ? -1 : unchecked((int)(value / divisor));
}
