using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// hardfp (.NET 8/10 linux-arm) <-> softfp (Tizen system libs) ABI bridges.
// Encodings verified against keystone assembler on host (0 mismatches).
public enum AbiType { Void, Word32, Int64, Float, Double }

public static partial class AbiTables
{
    // libm/libc float functions imported by the mapped hardfp runtime.
    public static readonly Dictionary<string, string> Libm = new Dictionary<string, string>
    {
        ["ceil"] = "D|D", ["floor"] = "D|D", ["trunc"] = "D|D", ["round"] = "D|D",
        ["rint"] = "D|D", ["nearbyint"] = "D|D", ["sin"] = "D|D", ["cos"] = "D|D",
        ["tan"] = "D|D", ["asin"] = "D|D", ["acos"] = "D|D", ["atan"] = "D|D",
        ["sinh"] = "D|D", ["cosh"] = "D|D", ["tanh"] = "D|D", ["asinh"] = "D|D",
        ["acosh"] = "D|D", ["atanh"] = "D|D", ["exp"] = "D|D", ["exp2"] = "D|D",
        ["expm1"] = "D|D", ["log"] = "D|D", ["log10"] = "D|D", ["log2"] = "D|D",
        ["log1p"] = "D|D", ["sqrt"] = "D|D", ["cbrt"] = "D|D", ["fabs"] = "D|D",
        ["erf"] = "D|D", ["erfc"] = "D|D", ["tgamma"] = "D|D", ["lgamma"] = "D|D",
        ["logb"] = "D|D", ["nan"] = "D|W",
        ["pow"] = "D|DD", ["atan2"] = "D|DD", ["fmod"] = "D|DD", ["remainder"] = "D|DD",
        ["copysign"] = "D|DD", ["hypot"] = "D|DD", ["fmax"] = "D|DD", ["fmin"] = "D|DD",
        ["fdim"] = "D|DD", ["nextafter"] = "D|DD",
        ["ldexp"] = "D|DW", ["scalbn"] = "D|DW", ["scalbln"] = "D|DW",
        ["modf"] = "D|DW", ["frexp"] = "D|DW",
        ["ilogb"] = "W|D",
        ["sincos"] = "V|DWW", ["sincosf"] = "V|FWW",
        ["fma"] = "D|DDD", ["fmaf"] = "F|FFF",
        ["asinhf"] = "F|F", ["acoshf"] = "F|F", ["atanhf"] = "F|F",
        ["erff"] = "F|F", ["erfcf"] = "F|F", ["tgammaf"] = "F|F", ["lgammaf"] = "F|F",
        ["nextafterf"] = "F|FF",
        ["strtod"] = "D|WW", ["atof"] = "D|W", ["difftime"] = "D|WW",
        ["ceilf"] = "F|F", ["floorf"] = "F|F", ["truncf"] = "F|F", ["roundf"] = "F|F",
        ["rintf"] = "F|F", ["nearbyintf"] = "F|F", ["sinf"] = "F|F", ["cosf"] = "F|F",
        ["tanf"] = "F|F", ["asinf"] = "F|F", ["acosf"] = "F|F", ["atanf"] = "F|F",
        ["sinhf"] = "F|F", ["coshf"] = "F|F", ["tanhf"] = "F|F", ["expf"] = "F|F",
        ["exp2f"] = "F|F", ["expm1f"] = "F|F", ["logf"] = "F|F", ["log10f"] = "F|F",
        ["log2f"] = "F|F", ["log1pf"] = "F|F", ["sqrtf"] = "F|F", ["cbrtf"] = "F|F",
        ["fabsf"] = "F|F", ["logbf"] = "F|F", ["nanf"] = "F|W",
        ["powf"] = "F|FF", ["atan2f"] = "F|FF", ["fmodf"] = "F|FF", ["remainderf"] = "F|FF",
        ["copysignf"] = "F|FF", ["hypotf"] = "F|FF", ["fmaxf"] = "F|FF", ["fminf"] = "F|FF",
        ["fdimf"] = "F|FF",
        ["ldexpf"] = "F|FW", ["scalbnf"] = "F|FW", ["scalblnf"] = "F|FW",
        ["modff"] = "F|FW", ["frexpf"] = "F|FW",
        ["ilogbf"] = "W|F",
        ["strtof"] = "F|WW",
    };

    public static AbiType ParseType(char c) => c switch
    {
        'V' => AbiType.Void, 'W' => AbiType.Word32, 'L' => AbiType.Int64,
        'F' => AbiType.Float, 'D' => AbiType.Double,
        _ => throw new ArgumentException("bad abi type char " + c),
    };

    public static (AbiType ret, AbiType[] args) Parse(string enc)
    {
        int bar = enc.IndexOf('|');
        var args = new AbiType[enc.Length - bar - 1];
        for (int i = 0; i < args.Length; i++) args[i] = ParseType(enc[bar + 1 + i]);
        return (ParseType(enc[0]), args);
    }
}

public sealed class Arm32AbiTrampolines
{
    private const int PROT_READ = 1, PROT_WRITE = 2, PROT_EXEC = 4;
    private const int MAP_PRIVATE = 0x02, MAP_ANONYMOUS = 0x20;
    private const int __ARM_NR_cacheflush = 0x0f0002;

    [DllImport("libc.so.6", SetLastError = true)]
    private static extern IntPtr mmap(IntPtr addr, UIntPtr len, int prot, int flags, IntPtr fd, IntPtr offset);

    [DllImport("libc.so.6")]
    private static extern int syscall(int number, IntPtr start, IntPtr end, int flags);

    private readonly IntPtr _page;
    private int _offset;
    private readonly int _capacity = 256 * 1024;
    private readonly object _lock = new object();
    private readonly Dictionary<string, IntPtr> _cache = new Dictionary<string, IntPtr>();
    private readonly Arm32StackBridge _stackBridge = new Arm32StackBridge();
    public int CreatedCount;

    public Arm32AbiTrampolines()
    {
        _page = mmap(IntPtr.Zero, (UIntPtr)_capacity, PROT_READ | PROT_WRITE | PROT_EXEC,
                     MAP_PRIVATE | MAP_ANONYMOUS, (IntPtr)(-1), IntPtr.Zero);
        if (_page == (IntPtr)(-1)) throw new OutOfMemoryException("mmap RWX for trampolines failed");
    }

    // ARM A32 encodings (keystone-verified)
    private static uint VmovArmFromS(int rt, int sn) =>
        0xEE100A10u | ((uint)(sn >> 1) << 16) | ((uint)rt << 12) | ((uint)(sn & 1) << 7);
    private static uint VmovSFromArm(int sn, int rt) =>
        0xEE000A10u | ((uint)(sn >> 1) << 16) | ((uint)rt << 12) | ((uint)(sn & 1) << 7);
    private static uint VmovArmFromD(int rt, int rt2, int dm) =>
        0xEC500B10u | ((uint)rt2 << 16) | ((uint)rt << 12) | ((uint)(dm >> 4) << 5) | (uint)(dm & 0xF);
    private static uint VmovDFromArm(int dm, int rt, int rt2) =>
        0xEC400B10u | ((uint)rt2 << 16) | ((uint)rt << 12) | ((uint)(dm >> 4) << 5) | (uint)(dm & 0xF);
    private static uint MovReg(int rd, int rm) => 0xE1A00000u | ((uint)rd << 12) | (uint)rm;

    /// <summary>hardfp caller -> softfp callee. Returns Zero if >4 register words (caller falls back).</summary>
    public IntPtr GetOrCreateHardToSoft(IntPtr target, string enc)
    {
        string key = "h2s:" + target.ToInt64() + ":" + enc;
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out IntPtr cached)) return cached;
            var (ret, args) = AbiTables.Parse(enc);
            IntPtr entry = EmitHardToSoft(target, ret, args);
            if (entry == IntPtr.Zero)
            {
                // >4 register words: stack-aware bridge (e.g. fma D|DDD)
                entry = _stackBridge.GetOrCreate(target, enc);
            }
            _cache[key] = entry;
            if (entry != IntPtr.Zero) CreatedCount++;
            return entry;
        }
    }

    private IntPtr EmitHardToSoft(IntPtr softfpTarget, AbiType ret, AbiType[] args)
    {
        var insns = new List<uint>(24);
        int nextR = 0;       // softfp outgoing r0..r3
        int nextS = 0;       // hardfp incoming s0..s15
        int nextD = 0;       // hardfp incoming d0..d7
        uint sUsedMask = 0;
        var intMoves = new List<(int srcR, int dstR)>();
        var vfpToArm = new List<uint>();
        int hardR = 0;

        foreach (var t in args)
        {
            switch (t)
            {
                case AbiType.Word32:
                    if (hardR >= 4 || nextR >= 4) return IntPtr.Zero;
                    if (hardR != nextR) intMoves.Add((hardR, nextR));
                    hardR++; nextR++;
                    break;
                case AbiType.Int64:
                    hardR = (hardR + 1) & ~1;
                    nextR = (nextR + 1) & ~1;
                    if (hardR + 1 >= 4 || nextR + 1 >= 4) return IntPtr.Zero;
                    if (hardR != nextR) { intMoves.Add((hardR, nextR)); intMoves.Add((hardR + 1, nextR + 1)); }
                    hardR += 2; nextR += 2;
                    break;
                case AbiType.Float:
                    while (nextS < 16 && (sUsedMask & (1u << nextS)) != 0) nextS++;
                    if (nextS >= 16 || nextR >= 4) return IntPtr.Zero;
                    sUsedMask |= 1u << nextS;
                    vfpToArm.Add(VmovArmFromS(nextR, nextS));
                    nextR++;
                    break;
                case AbiType.Double:
                    while (nextD < 8 && (sUsedMask & (3u << (nextD * 2))) != 0) nextD++;
                    if (nextD >= 8) return IntPtr.Zero;
                    nextR = (nextR + 1) & ~1;
                    if (nextR + 1 >= 4) return IntPtr.Zero;
                    sUsedMask |= 3u << (nextD * 2);
                    vfpToArm.Add(VmovArmFromD(nextR, nextR + 1, nextD));
                    nextR += 2;
                    break;
                default:
                    return IntPtr.Zero;
            }
        }

        // int reg moves first (backwards), then VFP->ARM (would clobber sources)
        for (int i = intMoves.Count - 1; i >= 0; i--)
            insns.Add(MovReg(intMoves[i].dstR, intMoves[i].srcR));
        insns.AddRange(vfpToArm);
        insns.Add(0xE92D4010u); // push {r4, lr}
        int ldrIndex = insns.Count;
        insns.Add(0);           // ldr r12, [pc, #patched]
        insns.Add(0xE12FFF3Cu); // blx r12
        if (ret == AbiType.Float) insns.Add(VmovSFromArm(0, 0));
        else if (ret == AbiType.Double) insns.Add(VmovDFromArm(0, 0, 1));
        insns.Add(0xE8BD8010u); // pop {r4, pc}
        int literalIndex = insns.Count;
        uint pcRel = (uint)((literalIndex - (ldrIndex + 2)) * 4);
        insns[ldrIndex] = 0xE59FC000u | pcRel;
        insns.Add(unchecked((uint)softfpTarget.ToInt64()));

        return Commit(insns);
    }

    /// <summary>softfp native caller -> hardfp managed stub (SWIG directors). Register-only args.</summary>
    public IntPtr GetOrCreateSoftToHard(IntPtr target, string enc)
    {
        string key = "s2h:" + target.ToInt64() + ":" + enc;
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out IntPtr cached)) return cached;
            var (ret, args) = AbiTables.Parse(enc);
            IntPtr entry = EmitSoftToHard(target, ret, args);
            _cache[key] = entry;
            if (entry != IntPtr.Zero) CreatedCount++;
            return entry;
        }
    }

    private IntPtr EmitSoftToHard(IntPtr hardfpTarget, AbiType ret, AbiType[] args)
    {
        var insns = new List<uint>(16);
        int nextR = 0, nextS = 0, nextD = 0;
        uint sUsedMask = 0;
        foreach (var t in args)
        {
            switch (t)
            {
                case AbiType.Word32:
                    if (nextR >= 4) return IntPtr.Zero;
                    nextR++;
                    break;
                case AbiType.Int64:
                    nextR = (nextR + 1) & ~1;
                    if (nextR + 1 >= 4) return IntPtr.Zero;
                    nextR += 2;
                    break;
                case AbiType.Float:
                    while (nextS < 16 && (sUsedMask & (1u << nextS)) != 0) nextS++;
                    if (nextS >= 16 || nextR >= 4) return IntPtr.Zero;
                    sUsedMask |= 1u << nextS;
                    insns.Add(VmovSFromArm(nextS, nextR));
                    nextR++;
                    break;
                case AbiType.Double:
                    while (nextD < 8 && (sUsedMask & (3u << (nextD * 2))) != 0) nextD++;
                    if (nextD >= 8) return IntPtr.Zero;
                    nextR = (nextR + 1) & ~1;
                    if (nextR + 1 >= 4) return IntPtr.Zero;
                    sUsedMask |= 3u << (nextD * 2);
                    insns.Add(VmovDFromArm(nextD, nextR, nextR + 1));
                    nextR += 2;
                    break;
                default:
                    return IntPtr.Zero;
            }
        }
        if (ret == AbiType.Float || ret == AbiType.Double)
        {
            insns.Add(0xE92D4010u); // push {r4, lr}
            int ldrI = insns.Count;
            insns.Add(0);
            insns.Add(0xE12FFF3Cu); // blx r12
            if (ret == AbiType.Float) insns.Add(VmovArmFromS(0, 0));
            else insns.Add(VmovArmFromD(0, 1, 0));
            insns.Add(0xE8BD8010u); // pop {r4, pc}
            int litI = insns.Count;
            insns[ldrI] = 0xE59FC000u | (uint)((litI - (ldrI + 2)) * 4);
            insns.Add(unchecked((uint)hardfpTarget.ToInt64()));
        }
        else
        {
            // tail call: ldr r12, [pc, #0]; bx r12; .word target
            insns.Add(0xE59FC000u); // ldr r12, [pc, #0] -> word right after bx
            insns.Add(0xE12FFF1Cu); // bx r12
            insns.Add(unchecked((uint)hardfpTarget.ToInt64()));
        }
        return Commit(insns);
    }

    private IntPtr Commit(List<uint> insns)
    {
        // caller holds _lock
        int bytes = insns.Count * 4;
        if (_offset + bytes > _capacity) throw new OutOfMemoryException("trampoline page full");
        IntPtr entry = _page + _offset;
        for (int i = 0; i < insns.Count; i++)
            Marshal.WriteInt32(entry, i * 4, unchecked((int)insns[i]));
        syscall(__ARM_NR_cacheflush, entry, entry + bytes, 0);
        _offset += bytes;
        return entry;
    }
}
