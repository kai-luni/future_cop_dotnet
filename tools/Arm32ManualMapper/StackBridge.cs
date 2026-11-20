using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// Stack-aware hardfp->softfp bridge for signatures with >4 register words
// (e.g. fma D|DDD). Register-only signatures keep using the fast path in
// Arm32AbiTrampolines. Encodings keystone-verified on host.
public sealed class Arm32StackBridge
{
    private const int PROT_READ = 1, PROT_WRITE = 2, PROT_EXEC = 4;
    private const int MAP_PRIVATE = 0x02, MAP_ANONYMOUS = 0x20;
    private const int __ARM_NR_cacheflush = 0x0f0002;

    [DllImport("libc.so.6", SetLastError = true)]
    private static extern IntPtr mmap(IntPtr addr, UIntPtr len, int prot, int flags, IntPtr fd, IntPtr offset);
    [DllImport("libc.so.6")]
    private static extern int syscall(int number, IntPtr start, IntPtr end, int flags);

    private IntPtr _page;
    private int _offset;
    private readonly int _capacity = 64 * 1024;
    private readonly object _lock = new object();
    private readonly Dictionary<string, IntPtr> _cache = new Dictionary<string, IntPtr>();

    private enum Kind { Reg, Stack, SReg, DReg }
    private readonly struct Loc
    {
        public readonly Kind K; public readonly int Idx;
        public Loc(Kind k, int i) { K = k; Idx = i; }
    }

    // hardfp layout: ints/ptrs -> r0..r3 then stack; floats -> s0..s15 (doubles d0..d7,
    // even-aligned), NO core regs consumed; overflow floats go to stack after int words.
    private static Loc[] HardLayout(AbiType[] args, out int stackBytes)
    {
        var locs = new Loc[args.Length];
        int r = 0, s = 0, w = 0; // w = stacked word counter (shared NSAA)
        uint sMask = 0;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case AbiType.Word32:
                    if (r < 4) locs[i] = new Loc(Kind.Reg, r++);
                    else { locs[i] = new Loc(Kind.Stack, w * 4); w += 1; }
                    break;
                case AbiType.Int64:
                    r = (r + 1) & ~1;
                    if (r + 1 < 4) { locs[i] = new Loc(Kind.Reg, r); r += 2; }
                    else { w = (w + 1) & ~1; locs[i] = new Loc(Kind.Stack, w * 4); w += 2; r = 4; }
                    break;
                case AbiType.Float:
                    while (s < 16 && (sMask & (1u << s)) != 0) s++;
                    if (s < 16) { sMask |= 1u << s; locs[i] = new Loc(Kind.SReg, s); s++; }
                    else { locs[i] = new Loc(Kind.Stack, w * 4); w += 1; }
                    break;
                case AbiType.Double:
                    int d = s / 2;
                    while (d < 8 && (sMask & (3u << (d * 2))) != 0) d++;
                    if (d < 8) { sMask |= 3u << (d * 2); locs[i] = new Loc(Kind.DReg, d); s = (d + 1) * 2; }
                    else { w = (w + 1) & ~1; locs[i] = new Loc(Kind.Stack, w * 4); w += 2; }
                    break;
                default:
                    throw new NotSupportedException("stack bridge arg type " + args[i]);
            }
        }
        stackBytes = (w * 4 + 7) & ~7;
        return locs;
    }

    // softfp layout: everything (float bits included) -> r0..r3 then stack, 8-byte align for L/D.
    private static Loc[] SoftLayout(AbiType[] args, out int stackBytes)
    {
        var locs = new Loc[args.Length];
        int r = 0, w = 0;
        for (int i = 0; i < args.Length; i++)
        {
            int words = args[i] == AbiType.Int64 || args[i] == AbiType.Double ? 2 : 1;
            if (words == 2) r = (r + 1) & ~1;
            if (r + words <= 4) { locs[i] = new Loc(Kind.Reg, r); r += words; }
            else
            {
                if (words == 2) w = (w + 1) & ~1;
                locs[i] = new Loc(Kind.Stack, w * 4); w += words; r = 4;
            }
        }
        stackBytes = (w * 4 + 7) & ~7;
        return locs;
    }

    public IntPtr GetOrCreate(IntPtr target, string enc)
    {
        string key = target.ToInt64() + ":" + enc;
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out IntPtr cached)) return cached;
            var (ret, args) = AbiTables.Parse(enc);
            IntPtr entry = Emit(target, ret, args);
            _cache[key] = entry;
            return entry;
        }
    }

    private static uint Mov(int rd, int rm) => 0xE1A00000u | ((uint)rd << 12) | (uint)rm;
    private static uint Ldr(int rd, int rn, int off) => 0xE5900000u | ((uint)rn << 16) | ((uint)rd << 12) | (uint)off;
    private static uint Str(int rd, int rn, int off) => 0xE5800000u | ((uint)rn << 16) | ((uint)rd << 12) | (uint)off;
    private static uint VmovRFromS(int rt, int sn) => 0xEE100A10u | ((uint)(sn >> 1) << 16) | ((uint)rt << 12) | ((uint)(sn & 1) << 7);
    private static uint VmovSFromR(int sn, int rt) => 0xEE000A10u | ((uint)(sn >> 1) << 16) | ((uint)rt << 12) | ((uint)(sn & 1) << 7);
    private static uint VmovRRFromD(int rt, int rt2, int dm) => 0xEC500B10u | ((uint)rt2 << 16) | ((uint)rt << 12) | ((uint)(dm >> 4) << 5) | (uint)(dm & 0xF);
    private static uint VmovDFromRR(int dm, int rt, int rt2) => 0xEC400B10u | ((uint)rt2 << 16) | ((uint)rt << 12) | ((uint)(dm >> 4) << 5) | (uint)(dm & 0xF);
    private static uint VstrD(int dd, int rn, int off) => 0xED800B00u | ((uint)(dd >> 4) << 22) | ((uint)rn << 16) | ((uint)(dd & 0xF) << 12) | (uint)(off >> 2);
    private static uint AddImm(int rd, int rn, uint val) => 0xE2800000u | ((uint)rn << 16) | ((uint)rd << 12) | val;
    private static uint SubImm(int rd, int rn, uint val) => 0xE2400000u | ((uint)rn << 16) | ((uint)rd << 12) | val;

    private static void EmitLoadWord(List<uint> code, Loc src, AbiType t, int k)
    {
        if (src.K == Kind.SReg) code.Add(VmovRFromS(5, src.Idx + k));
        else if (src.K == Kind.DReg)
        {
            if (k == 0) code.Add(VmovRRFromD(5, 6, src.Idx));
            else code.Add(Mov(5, 6));
        }
        else if (src.K == Kind.Reg)
        {
            int regNum = src.Idx + k;
            if (regNum < 4) code.Add(Ldr(5, 4, regNum * 4));
            else code.Add(Ldr(5, 4, 32 + (regNum - 4) * 4));
        }
        else code.Add(Ldr(5, 4, 32 + src.Idx + k * 4));
    }

    private IntPtr Emit(IntPtr target, AbiType ret, AbiType[] args)
    {
        Loc[] hard = HardLayout(args, out int hardStack);
        Loc[] soft = SoftLayout(args, out int softStack);
        var code = new List<uint>(96);

        // push {r0-r3, r4, r5, r6, lr} = 32 bytes; r4 = frame base (saved regs area)
        code.Add(0xE92D407Fu);
        if (softStack > 0) code.Add(SubImm(13, 13, (uint)softStack));
        code.Add(AddImm(4, 13, (uint)softStack)); // r4 = sp + softStack -> [r4,#0..12]=saved r0..r3, [r4,#32..]=incoming stack args

        // Phase 1: outgoing STACK args (scratch r5/r6; sources: saved regs / VFP / incoming stack).
        // Phase 2: outgoing CORE regs r0..r3 in DESCENDING destination order, load+mov paired,
        //          so a write to rN never clobbers a source read that still needs original rN.
        for (int phase = 0; phase < 2; phase++)
        {
            var regWrites = new List<(int dstReg, int argI, int wordK)>();
            for (int i = 0; i < args.Length; i++)
            {
                Loc src = hard[i]; Loc dst = soft[i];
                bool dstStack = dst.K == Kind.Stack;
                if (phase == 0 && !dstStack) continue;
                if (phase == 1 && dstStack) continue;
                int words = args[i] == AbiType.Int64 || args[i] == AbiType.Double ? 2 : 1;

                if (args[i] == AbiType.Double && src.K == Kind.DReg && dstStack)
                { code.Add(VstrD(src.Idx, 13, dst.Idx)); continue; }
                if (args[i] == AbiType.Float && src.K == Kind.SReg && dstStack)
                { code.Add(VmovRFromS(5, src.Idx)); code.Add(Str(5, 13, dst.Idx)); continue; }
                // NOTE: double word copies must load BOTH words before storing either:
                // a str to [sp,#0] aliases the saved-r0 slot [r4,#0] (r4 = sp + softStack).
                if (args[i] == AbiType.Double && src.K == Kind.DReg && !dstStack && dst.Idx + 1 < 4)
                {
                    if (phase == 1)
                    {
                        regWrites.Add((dst.Idx, i, -2)); // -2 = whole-double marker
                        continue;
                    }
                }

                for (int k = 0; k < words; k++)
                {
                    if (phase == 1 && !dstStack && dst.Idx + k < 4)
                    { regWrites.Add((dst.Idx + k, i, k)); continue; }
                    EmitLoadWord(code, src, args[i], k);
                    if (dstStack) code.Add(Str(5, 13, dst.Idx + k * 4));
                    else code.Add(Str(5, 13, (dst.Idx + k - 4) * 4)); // soft reg overflow -> outgoing stack
                }
            }
            if (phase == 1)
            {
                regWrites.Sort((a, b) => b.dstReg.CompareTo(a.dstReg));
                foreach (var w in regWrites)
                {
                    if (w.wordK == -2)
                    {
                        // whole double: vmov r5,r6,dN ; mov r(dst+1),r6 ; mov r(dst),r5
                        code.Add(VmovRRFromD(5, 6, hard[w.argI].Idx));
                        code.Add(Mov(w.dstReg + 1, 6));
                        code.Add(Mov(w.dstReg, 5));
                    }
                    else
                    {
                        EmitLoadWord(code, hard[w.argI], args[w.argI], w.wordK);
                        code.Add(Mov(w.dstReg, 5));
                    }
                }
            }
        }

        // call
        int ldrIdx = code.Count;
        code.Add(0);
        code.Add(0xE12FFF3Cu); // blx r12
        if (ret == AbiType.Float) code.Add(VmovSFromR(0, 0));
        else if (ret == AbiType.Double) code.Add(VmovDFromRR(0, 0, 1));
        // epilogue: restore sp to saved-regs top, pop {r4,r5,r6,pc}
        code.Add(AddImm(13, 4, 16));
        code.Add(0xE8BD8070u);
        int litIdx = code.Count;
        code[ldrIdx] = 0xE59FC000u | (uint)((litIdx - ldrIdx - 2) * 4);
        code.Add(unchecked((uint)target.ToInt64()));

        // commit
        int bytes = code.Count * 4;
        if (_page == IntPtr.Zero)
        {
            _page = mmap(IntPtr.Zero, (UIntPtr)_capacity, PROT_READ | PROT_WRITE | PROT_EXEC, MAP_PRIVATE | MAP_ANONYMOUS, (IntPtr)(-1), IntPtr.Zero);
            if (_page == (IntPtr)(-1)) throw new OutOfMemoryException("stack bridge mmap failed");
        }
        if (_offset + bytes > _capacity) throw new OutOfMemoryException("stack bridge page full");
        IntPtr entry = _page + _offset;
        for (int i = 0; i < code.Count; i++) Marshal.WriteInt32(entry, i * 4, unchecked((int)code[i]));
        syscall(__ARM_NR_cacheflush, entry, entry + bytes, 0);
        _offset += bytes;
        return entry;
    }
}
