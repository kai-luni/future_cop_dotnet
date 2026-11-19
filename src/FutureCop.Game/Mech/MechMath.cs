namespace FutureCop.Game.Mech;

/// <summary>Fixed-point helpers of the walker locomotion (ports of the retail math units).</summary>
public static class MechMath
{
    /// <summary>GTE LZCS/LZCR: number of leading bits equal to the sign bit (32 for 0 and -1).</summary>
    public static int LeadingSignBits(int value)
    {
        uint x = value < 0 ? ~(uint)value : (uint)value;
        int n = 0;
        while (n < 32 && (x & 0x80000000u) == 0) { x <<= 1; n++; }
        return n;
    }

    /// <summary>
    /// func_80095900 gSqrt12: square root via LZCR normalisation and the ROM mantissa table
    /// D_8009DF20. Defined for value &gt;= 0 and -1 (every retail caller passes a sum of squares);
    /// other negative inputs make the retail code read memory in front of the table.
    /// </summary>
    public static int Sqrt12(int value)
    {
        int lzc = LeadingSignBits(value);
        if (lzc == 32) return 0;
        int even = lzc & ~1;
        int exponent = (0x13 - even) >> 1;
        int shift = even - 0x18;
        int normalised = shift >= 0 ? value << shift : value >> ((0x18 - even) & 31);
        int index = normalised - 0x40;
        if (index < 0 || index >= 0xC0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "func_80095900 would read outside D_8009DF20");
        int mantissa = MechTables.Sqrt(index);
        return exponent >= 0 ? mantissa << exponent : (int)((uint)mantissa >> -exponent);
    }

    /// <summary>The (hi &lt;&lt; 16) | (lo &gt;&gt; 16) idiom of a signed mult: bits 16..47 of the product.</summary>
    public static int Fmul(int a, int b) => unchecked((int)(((long)a * b) >> 16));

    /// <summary>The same idiom on multu.</summary>
    public static int FmulU(int a, int b) => unchecked((int)(((ulong)(uint)a * (uint)b) >> 16));

    /// <summary>func_80019FCC: sum of the three 16.16 squares (wrapping like the retail add).</summary>
    public static int SumSquares3(int x, int y, int z) => unchecked(Fmul(x, x) + Fmul(y, y) + Fmul(z, z));

    /// <summary>func_8001A030: sum of two 16.16 squares.</summary>
    public static int SumSquares2(int x, int z) => unchecked(Fmul(x, x) + Fmul(z, z));

    /// <summary>func_8001ABE0: scales the first two words of <paramref name="v"/> by a 16.16 factor.</summary>
    public static void Scale2(int scale, int[] v)
    {
        v[0] = Fmul(v[0], scale);
        v[1] = Fmul(v[1], scale);
    }

    /// <summary>func_8001AB00 gVectorDot: 16.16 dot product.</summary>
    public static int Dot(int ax, int ay, int az, int bx, int by, int bz) => unchecked(Fmul(ax, bx) + Fmul(ay, by) + Fmul(az, bz));

    /// <summary>func_8001A6F8 gEaseVectorQuarter: dst = (3 * dst + src) &gt;&gt; 2 per component.</summary>
    public static int EaseQuarter(int dst, int src) => unchecked(dst * 3 + src) >> 2;

    /// <summary>func_8001846C gSinCos: 16.16 sine and cosine of a 4096-step angle (ROM table D_8009A234).</summary>
    public static (int Sin, int Cos) SinCos(int angle)
    {
        int a = angle & 0xFFF;
        int i = a & 0x3FF;
        int s = MechTables.Sine(i) << 1;
        int c = MechTables.Sine(0x400 - i) << 1;
        return (a >> 10) switch { 0 => (s, c), 1 => (c, -s), 2 => (-s, -c), _ => (-c, s) };
    }

    private static readonly ushort[] ReciprocalRootTable = BuildReciprocalRootTable();

    /// <summary>func_80019828 gInitReciprocalTable: the 1024-entry table behind D_8009F5B0.</summary>
    public static ushort ReciprocalRootEntry(int index) => ReciprocalRootTable[index];

    private static ushort[] BuildReciprocalRootTable()
    {
        var table = new ushort[0x400];
        for (uint input = 0; input < 0x400; input++)
        {
            uint value = IntegerRoot(Reciprocal(input));
            int shift = 0x15;
            if (value != 0) shift -= LeadingSignBits(unchecked((int)value));
            if (shift > 0) value >>= shift;
            value <<= 5;
            value |= unchecked((uint)shift);
            table[input] = (ushort)value;
        }
        return table;
    }

    /// <summary>func_80018818: 32 rounds of restoring division of 2^33 by the divisor.</summary>
    public static uint Reciprocal(uint divisor)
    {
        uint remainder = 2, quotient = 0, carry = 0;
        for (int i = 32; i > 0; i--)
        {
            quotient <<= 1;
            if (carry != 0 || divisor < remainder)
            {
                remainder -= divisor;
                quotient |= 1;
            }
            carry = remainder & 0x80000000u;
            remainder <<= 1;
        }
        return quotient;
    }

    /// <summary>func_800187C8: 24-step bitwise integer root.</summary>
    public static uint IntegerRoot(uint value)
    {
        uint root = 0, remainder = 0;
        int i = 0x17;
        do
        {
            remainder = (remainder << 2) | (value >> 30);
            root <<= 1;
            uint trial = (root << 1) + 1;
            value <<= 2;
            if (remainder >= trial)
            {
                remainder -= trial;
                root++;
            }
        } while (--i != -1);
        return root;
    }

    /// <summary>func_80019904: reciprocal square root from the D_8009F5B0 table (handwritten).</summary>
    public static int ReciprocalRoot(int value)
    {
        int shift = 0x16 - (LeadingSignBits(value) & ~1);
        if (shift <= 0) shift = 0;
        int index = value >> (shift & 31);
        if (index < 0 || index >= 0x400)
            throw new ArgumentOutOfRangeException(nameof(value), value, "func_80019904 would read outside its table");
        int entry = ReciprocalRootTable[index];
        int mantissa = (entry >> 5) << (entry & 0x1F);
        return mantissa >> ((shift >> 1) & 31);
    }

    private static int Max3Abs(int x, int y, int z)
    {
        int ax = x < 0 ? -x : x, ay = y < 0 ? -y : y, az = z < 0 ? -z : z;
        int d = unchecked(ay - ax);
        ay = unchecked(ay - ((d >> 31) & d));
        d = unchecked(az - ay);
        return unchecked(az - ((d >> 31) & d));
    }

    /// <summary>func_8001A180 gNormalizeVector: scales to unit length (16.16) in place.</summary>
    public static void Normalize(int[] v)
    {
        int x = v[0], y = v[1], z = v[2];
        int m = Max3Abs(x, y, z);
        int shift = m != 0 ? 0x1F - LeadingSignBits(m) : 0;
        if (shift >= 0x15)
        {
            shift -= 0x14;
            x >>= shift; y >>= shift; z >>= shift;
        }
        else
        {
            shift = 0x14 - shift;
            x <<= shift; y <<= shift; z <<= shift;
        }
        int sum = unchecked(Fmul(x, x) + Fmul(y, y) + Fmul(z, z));
        if (sum > 0)
        {
            int scale = ReciprocalRoot(sum);
            v[0] = Fmul(x, scale);
            v[1] = Fmul(y, scale);
            v[2] = Fmul(z, scale);
        }
    }

    /// <summary>func_8001A314 gNormalizeGetLength: normalises in place and returns the length.</summary>
    public static int NormalizeGetLength(int[] v)
    {
        int x = v[0], y = v[1], z = v[2];
        int m = Max3Abs(x, y, z);
        if (m < 0x200) { x <<= 10; y <<= 10; z <<= 10; }
        else if (m > 0x800000) { x >>= 10; y >>= 10; z >>= 10; }
        int len = Sqrt12(unchecked(Fmul(x, x) + Fmul(y, y) + Fmul(z, z)));
        int t = len << 2;
        if (t >= 2)
        {
            int scale = unchecked((int)(0x80000000u / (uint)(t >> 1)));
            v[0] = Fmul(x, scale);
            v[1] = Fmul(y, scale);
            v[2] = Fmul(z, scale);
        }
        if (m < 0x200) t >>= 10;
        else if (m > 0x800000) t = len << 12;
        return t;
    }

    /// <summary>func_8001A7AC: planar length of (v[0], v[1]); normalises the pair when it is &gt;= 2.</summary>
    public static int Length2(int[] v)
    {
        int s0 = Sqrt12(SumSquares2(v[0], v[1])) << 2;
        if (s0 >= 2) Scale2(unchecked((int)(0x80000000u / (uint)(s0 >> 1))), v);
        return s0;
    }
}
