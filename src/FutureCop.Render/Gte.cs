namespace FutureCop.Assets;

/// <summary>
/// Derived GTE math, mirroring the retail handwritten asm units of the decomp:
/// matrix load func_80010750 (R = F*M^T with F = diag(1,-1,1), elements (s16)(w>>4),
/// negated row negates before the shift and wraps), batch transform func_8001010C
/// (mvmva sf=1, rotation matrix, V0, no translation, lm=0, Y negated on store) and
/// projection func_8001047C (rtps sf=1 lm=0 + clip byte assembly).
/// Fixed-point conventions follow psx-spx; the unsigned divide is modelled as the
/// rounded quotient H*65536/SZ3 with the GTE overflow rule H >= 2*SZ3 (verified
/// against the Opus psx-spx UNR model cases, see tests/golden/render.vectors).
/// </summary>
public static class Gte
{
    /// <summary>Engine matrix: 9 x s32 in 16.16, row-vector convention, plus translation.</summary>
    public readonly record struct Mat(int[] W, int Tx, int Ty, int Tz)
    {
        public static Mat Identity(int tx, int ty, int tz) =>
            new(new[] { 0x10000, 0, 0, 0, 0x10000, 0, 0, 0, 0x10000 }, tx, ty, tz);
    }

    /// <summary>GTE-side rotation matrix (s16, 1.3.12) and translation (s32).</summary>
    public readonly record struct Loaded(short[] R, int Trx, int Try, int Trz);

    public const int FlagIr1 = 0x1000000, FlagIr2 = 0x800000, FlagIr3 = 0x400000;
    public const int FlagSz = 0x40000, FlagDiv = 0x20000, FlagSx = 0x4000, FlagSy = 0x2000;
    public const int ClipRejectMask = 0x01C66000;

    /// <summary>func_80010750: engine matrix -> GTE registers.</summary>
    public static Loaded Load(Mat m)
    {
        var r = new short[9];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                // R = F*M^T: row 1 negates before the shift and wraps to s16.
                int w = m.W[j * 3 + i];
                r[i * 3 + j] = i == 1 ? (short)((-w) >> 4) : (short)(w >> 4);
            }
        return new Loaded(r, m.Tx >> 4, (-m.Ty) >> 4, m.Tz >> 4);
    }

    private static short Clamp16(long v, ref uint flag, int bit)
    {
        if (v > 0x7FFF) { flag |= (uint)bit; return 0x7FFF; }
        if (v < -0x8000) { flag |= (uint)bit; return -0x8000; }
        return (short)v;
    }

    /// <summary>func_8001010C: mvmva sf=1 without translation; returns stored (IR1, -IR2, IR3).</summary>
    public static (short X, short Y, short Z, uint Flag) Mvmva(Loaded r, short vx, short vy, short vz)
    {
        uint flag = 0;
        Span<short> ir = stackalloc short[3];
        for (int i = 0; i < 3; i++)
        {
            long mac = (long)r.R[i * 3] * vx + (long)r.R[i * 3 + 1] * vy + (long)r.R[i * 3 + 2] * vz;
            ir[i] = Clamp16(mac >> 12, ref flag, FlagIr1 >> i);
        }
        return (ir[0], (short)-ir[1], ir[2], flag);
    }

    /// <summary>
    /// func_80010234: mvmva sf=1 with translation, reading the unsaturated 32-bit MAC registers
    /// and returning (MAC1 &lt;&lt; 4, (-MAC2) &lt;&lt; 4, MAC3 &lt;&lt; 4).
    /// </summary>
    public static (int X, int Y, int Z) TransformWorld(Loaded r, short vx, short vy, short vz)
    {
        unchecked
        {
            short[] rm = r.R;
            long m1 = ((long)r.Trx * 4096 + (long)rm[0] * vx + (long)rm[1] * vy + (long)rm[2] * vz) >> 12;
            long m2 = ((long)r.Try * 4096 + (long)rm[3] * vx + (long)rm[4] * vy + (long)rm[5] * vz) >> 12;
            long m3 = ((long)r.Trz * 4096 + (long)rm[6] * vx + (long)rm[7] * vy + (long)rm[8] * vz) >> 12;
            return ((int)m1 << 4, (-(int)m2) << 4, (int)m3 << 4);
        }
    }

    /// <summary>GTE unsigned divide (UNR equivalent): q = H*65536/SZ3 rounded, overflow rule.</summary>
    public static int Quotient(int h, int sz3, out bool overflow)
    {
        overflow = h >= 2 * sz3;
        if (overflow) return 0x1FFFF;
        long q = ((long)h * 65536 + sz3 / 2) / sz3;
        return (int)Math.Min(q, 0x1FFFF);
    }

    /// <summary>
    /// func_8001047C per-vertex step: rtps then SXY2/SZ3/FLAG. When the clip byte is 1
    /// the retail code does NOT write the point (stale data stays); <paramref name="written"/>
    /// reports that.
    /// </summary>
    public static (int Sxy, int Sz3, byte Clip, uint Flag, bool Written) Rtps(
        Loaded r, short vx, short vy, short vz, int h, int ofx, int ofy, int widthMode)
    {
        uint flag = 0;
        short[] rm = r.R;
        long mac0 = ((long)r.Trx * 4096 + (long)rm[0] * vx + (long)rm[1] * vy + (long)rm[2] * vz) >> 12;
        int ir0 = Clamp16(mac0, ref flag, FlagIr1);
        long mac1 = ((long)r.Try * 4096 + (long)rm[3] * vx + (long)rm[4] * vy + (long)rm[5] * vz) >> 12;
        int ir1 = Clamp16(mac1, ref flag, FlagIr2);
        long mac2 = ((long)r.Trz * 4096 + (long)rm[6] * vx + (long)rm[7] * vy + (long)rm[8] * vz) >> 12;
        _ = Clamp16(mac2, ref flag, FlagIr3);
        int sz3;
        if (mac2 > 0xFFFF) { flag |= FlagSz; sz3 = 0xFFFF; }
        else if (mac2 < 0) { flag |= FlagSz; sz3 = 0; }
        else sz3 = (int)mac2;

        int q = Quotient(h, sz3, out bool ovf);
        if (ovf) flag |= FlagDiv;

        int sx = ClampScreen((ofx + (long)ir0 * q) >> 16, ref flag, FlagSx);
        int sy = ClampScreen((ofy + (long)ir1 * q) >> 16, ref flag, FlagSy);
        int sxy = (sy << 16) | (sx & 0xFFFF);

        byte clip;
        bool written = true;
        if ((flag & ClipRejectMask) != 0)
        {
            clip = 1;
            written = false;
        }
        else
        {
            int w = widthMode == 1 ? 0x140 : 0xA0;
            // The retail batch compares the zero-extended halves of SXY2.
            clip = (byte)((sz3 < 0x400 ? 1 : 0) |
                          ((sxy & 0xFFFF) < w ? 0x40 : 0) |
                          (((uint)sxy >> 16) < 0xF0 ? 0x80 : 0));
        }
        return (sxy, sz3, clip, flag, written);
    }

    private static int ClampScreen(long v, ref uint flag, int bit)
    {
        if (v > 0x3FF) { flag |= (uint)bit; return 0x3FF; }
        if (v < -0x400) { flag |= (uint)bit; return -0x400; }
        return (int)v;
    }
}