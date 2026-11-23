using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

[assembly: InternalsVisibleTo("Arm32ManualMapper.Tests")]

// Pure-native ARM32 replacements for the hot mapper hooks, so foreign DALi
// threads (render/update/loader) never re-enter the idle .NET 6 CoreCLR:
//   __gnu_Unwind_Find_exidx(pc, pcount)  -> table scan + fallback tail-call
//   __tls_get_addr(ti)                   -> pthread-key per-thread blocks + fallback
//   dl_iterate_phdr(cb, data)            -> real dl_iterate_phdr + mapped ELF phdr table
// All branch offsets computed from instruction indices (no hand-counted literals).
// Encodings keystone-verified on host before deployment.
public sealed unsafe class NativeHooks
{
    public const int MaxExidxEntries = 128;
    public const int MaxPhdrModules = 128;
    public const int DlPhdrInfoSize = 40;

    private const int PROT_READ = 1, PROT_WRITE = 2, PROT_EXEC = 4;
    private const int MAP_PRIVATE = 0x02, MAP_ANONYMOUS = 0x20;
    private const int __ARM_NR_cacheflush = 0x0f0002;
    private const uint DmbIsh = 0xF57FF05Bu;

    [DllImport("libc.so.6", SetLastError = true)]
    private static extern IntPtr mmap(IntPtr addr, UIntPtr len, int prot, int flags, IntPtr fd, IntPtr offset);
    [DllImport("libc.so.6")]
    private static extern int syscall(int number, IntPtr start, IntPtr end, int flags);
    [DllImport("libc.so.6")]
    private static extern void free(IntPtr p);
    [DllImport("libpthread.so.0")]
    private static extern int pthread_key_create(out uint key, IntPtr destructor);
    [DllImport("libpthread.so.0")]
    private static extern IntPtr pthread_getspecific(uint key);
    [DllImport("libpthread.so.0")]
    private static extern int pthread_setspecific(uint key, IntPtr value);

    private IntPtr _page;
    private int _offset;
    private readonly int _capacity = 16 * 1024;
    private IntPtr _exidxTable;
    private int _exidxCount;
    private uint _tlsBaseModId;
    private int _tlsMaxModules;
    private IntPtr _tlsDescTable;
    private IntPtr _phdrTable;
    private IntPtr _phdrCountPtr;
    private int _phdrCount;

    [StructLayout(LayoutKind.Explicit, Size = DlPhdrInfoSize)]
    internal struct Arm32DlPhdrInfo
    {
        [FieldOffset(0)]  public uint dlpi_addr;
        [FieldOffset(4)]  public uint dlpi_name;
        [FieldOffset(8)]  public uint dlpi_phdr;
        [FieldOffset(12)] public ushort dlpi_phnum;
        [FieldOffset(16)] public ulong dlpi_adds;
        [FieldOffset(24)] public ulong dlpi_subs;
        [FieldOffset(32)] public uint dlpi_tls_modid;
        [FieldOffset(36)] public uint dlpi_tls_data;
    }

    internal static uint ToArm32Address(IntPtr ptr)
    {
        ulong v = IntPtr.Size == 4 ? unchecked((uint)ptr.ToInt32()) : unchecked((ulong)ptr.ToInt64());
        if (v > uint.MaxValue)
            throw new OverflowException($"Address 0x{v:X} exceeds ARM32 32-bit address space.");
        return (uint)v;
    }

    public int PhdrCount => _phdrCount;
    public int ExidxCount => _exidxCount;
    public uint TlsBaseModId => _tlsBaseModId;
    public int TlsMaxModules => _tlsMaxModules;
    public bool HasExidxTable => _exidxTable != IntPtr.Zero;
    public bool HasTlsTable => _tlsDescTable != IntPtr.Zero;
    public bool HasPhdrTable => _phdrTable != IntPtr.Zero;

    public bool CanAddPhdrModule() => _phdrTable == IntPtr.Zero || _phdrCount < MaxPhdrModules;
    public bool CanAddExidxEntry() => _exidxTable == IntPtr.Zero || _exidxCount < MaxExidxEntries - 1;
    public bool CanSetTlsDesc(uint moduleId, uint memsz) =>
        _tlsDescTable == IntPtr.Zero || memsz == 0 || unchecked(moduleId - _tlsBaseModId) < (uint)_tlsMaxModules;

    // ---- encodings (keystone-verified) ----
    private static uint Ldr(int rd, int rn, int off) => 0xE5900000u | ((uint)rn << 16) | ((uint)rd << 12) | (uint)off;
    private static uint Str(int rd, int rn, int off) => 0xE5800000u | ((uint)rn << 16) | ((uint)rd << 12) | (uint)off;
    private static uint LdrPc(int rd, int disp) => 0xE59F0000u | ((uint)rd << 12) | (uint)disp;
    private static uint Mov(int rd, int rm) => 0xE1A00000u | ((uint)rd << 12) | (uint)rm;
    private static uint MovImm(int rd, uint imm) => 0xE3A00000u | ((uint)rd << 12) | imm;
    private static uint CmpImm(int rn, uint imm) => 0xE3500000u | ((uint)rn << 16) | imm;
    private static uint CmpReg(int rn, int rm) => 0xE1500000u | ((uint)rn << 16) | (uint)rm;
    private static uint AddImm(int rd, int rn, uint imm) => 0xE2800000u | ((uint)rn << 16) | ((uint)rd << 12) | imm;
    private static uint SubReg(int rd, int rn, int rm) => 0xE0400000u | ((uint)rn << 16) | ((uint)rd << 12) | (uint)rm;
    private static uint AddReg(int rd, int rn, int rm) => 0xE0800000u | ((uint)rn << 16) | ((uint)rd << 12) | (uint)rm;
    private static uint AddRegLsl(int rd, int rn, int rm, int lsl) => 0xE0800000u | ((uint)rn << 16) | ((uint)rd << 12) | ((uint)lsl << 7) | (uint)rm;
    private static uint LdrRegLsl(int rd, int rn, int rm, int lsl) => 0xE7900000u | ((uint)rn << 16) | ((uint)rd << 12) | ((uint)lsl << 7) | (uint)rm;
    private static uint StrRegLsl(int rd, int rn, int rm, int lsl) => 0xE7800000u | ((uint)rn << 16) | ((uint)rd << 12) | ((uint)lsl << 7) | (uint)rm;
    private static uint B(int cond, int offsetWords) => ((uint)cond << 28) | 0x0A000000u | ((uint)offsetWords & 0xFFFFFFu);
    private static uint BlxR12() => 0xE12FFF3Cu;
    private static uint BxR12() => 0xE12FFF1Cu;
    private static uint BxLr() => 0xE12FFF1Eu;
    private const uint PushR4R5R6Lr = 0xE92D4070u;
    private const uint PopR4R5R6Pc = 0xE8BD8070u;
    private const uint PushR4R5Lr = 0xE92D4030u;
    private const uint PopR4R5Lr = 0xE8BD4030u;

    private IntPtr Commit(List<uint> code)
    {
        int bytes = code.Count * 4;
        if (_page == IntPtr.Zero)
        {
            _page = mmap(IntPtr.Zero, (UIntPtr)_capacity, PROT_READ | PROT_WRITE | PROT_EXEC, MAP_PRIVATE | MAP_ANONYMOUS, (IntPtr)(-1), IntPtr.Zero);
            if (_page == (IntPtr)(-1)) throw new OutOfMemoryException("native hooks mmap failed");
        }
        if (_offset + bytes > _capacity) throw new OutOfMemoryException("native hooks page full");
        IntPtr entry = _page + _offset;
        for (int i = 0; i < code.Count; i++) Marshal.WriteInt32(entry, i * 4, unchecked((int)code[i]));
        syscall(__ARM_NR_cacheflush, entry, entry + bytes, 0);
        _offset += (bytes + 7) & ~7;
        return entry;
    }

    // ldr rd, [pc, #disp] to literal at poolIdx (instruction index)
    private static void PatchLdrPc(List<uint> c, int instrIdx, int rd, int poolIdx)
        => c[instrIdx] = LdrPc(rd, (poolIdx - instrIdx - 2) * 4);
    // b/b<cond> at instrIdx targeting instruction index target
    private static void PatchBranch(List<uint> c, int instrIdx, uint condBits, int target)
        => c[instrIdx] = (condBits << 28) | 0x0A000000u | ((uint)(target - instrIdx - 2) & 0xFFFFFFu);

    // ================= exidx finder =================
    // Table: 16-byte entries { textStart, textEnd, exidxStart, exidxCount }, terminated by textStart=0.
    // Pre-allocated to MaxExidxEntries so dlopen can append modules later without moving the pointer.
    public IntPtr EmitExidxFinder(List<(uint textStart, uint textEnd, uint exidxStart, int count)> entries, IntPtr realFinder)
    {
        _exidxTable = Marshal.AllocHGlobal(MaxExidxEntries * 16);
        new Span<byte>((void*)_exidxTable, MaxExidxEntries * 16).Clear();
        int initialCount = Math.Min(entries.Count, MaxExidxEntries - 1);
        for (int i = 0; i < initialCount; i++)
        {
            Marshal.WriteInt32(_exidxTable, i * 16 + 0, unchecked((int)entries[i].textStart));
            Marshal.WriteInt32(_exidxTable, i * 16 + 4, unchecked((int)entries[i].textEnd));
            Marshal.WriteInt32(_exidxTable, i * 16 + 8, unchecked((int)entries[i].exidxStart));
            Marshal.WriteInt32(_exidxTable, i * 16 + 12, entries[i].count);
        }
        _exidxCount = initialCount;
        IntPtr table = _exidxTable;

        var c = new List<uint>(24);
        c.Add(0);                    // [0] ldr r2, [pc, #lit_table]
        c.Add(0xE3C00001u);          // bic r0, r0, #1 (clear Thumb bit, keystone-verified)
        int loop = c.Count;
        c.Add(Ldr(3, 2, 0));         // ldr r3, [r2] textStart
        c.Add(CmpImm(3, 0));         // cmp r3, #0
        int beqFallback = c.Count; c.Add(0);
        c.Add(CmpReg(0, 3));         // cmp pc, textStart
        int bloNext = c.Count; c.Add(0);
        c.Add(Ldr(3, 2, 4));         // textEnd
        c.Add(CmpReg(0, 3));
        int bhsNext = c.Count; c.Add(0);
        c.Add(Ldr(3, 2, 12));        // hit: exidxCount
        c.Add(Str(3, 1, 0));         // *pcount = count
        c.Add(Ldr(0, 2, 8));         // return exidxStart
        c.Add(BxLr());
        int next = c.Count;
        c.Add(AddImm(2, 2, 16));     // next entry
        c.Add(0);                    // b loop
        int bLoop = c.Count - 1;
        int fallback = c.Count;
        c.Add(0);                    // ldr r12, [pc, #lit_real]
        c.Add(BxR12());              // tail-call original finder
        int litTable = c.Count; c.Add(ToArm32Address(table));
        int litReal = c.Count; c.Add(ToArm32Address(realFinder));

        PatchLdrPc(c, 0, 2, litTable);
        PatchBranch(c, beqFallback, 0x0, fallback);   // beq
        PatchBranch(c, bloNext, 0x3, next);           // blo (unsigned <)
        PatchBranch(c, bhsNext, 0x2, next);           // bhs (unsigned >=)
        PatchBranch(c, bLoop, 0xE, loop);             // b (always)
        PatchLdrPc(c, fallback, 12, litReal);
        return Commit(c);
    }

    public void AddExidxEntry(uint textStart, uint textEnd, uint exidxStart, int count)
    {
        if (_exidxTable == IntPtr.Zero || textStart == 0)
            return;
        if (_exidxCount >= MaxExidxEntries - 1)
            throw new InvalidOperationException($"Exceeded max native ARM EXIDX entries ({MaxExidxEntries - 1}).");
        int off = _exidxCount * 16;
        Marshal.WriteInt32(_exidxTable, off + 4, unchecked((int)textEnd));
        Marshal.WriteInt32(_exidxTable, off + 8, unchecked((int)exidxStart));
        Marshal.WriteInt32(_exidxTable, off + 12, count);
        Thread.MemoryBarrier();
        // Publish non-zero textStart last so concurrent readers stop at the zero terminator until ready.
        Marshal.WriteInt32(_exidxTable, off + 0, unchecked((int)textStart));
        _exidxCount++;
    }

    // ================= TLS get_addr =================
    // Desc table: 16-byte entries { memsz, filesz, reserved, tdataPtr } indexed by (moduleId - baseModId).
    // Helper pointers MUST be real native addresses (dlsym), not managed delegate stubs,
    // otherwise the hook would re-enter CoreCLR 6 — defeating its purpose.
    public IntPtr EmitTlsGetAddr(uint baseModId, int maxModules,
        List<(uint memsz, uint filesz, IntPtr tdata)> descs,
        IntPtr realTlsGetAddr, IntPtr getP, IntPtr setP, IntPtr callocP, IntPtr memcpyP, IntPtr freePtr)
    {
        IntPtr descTable = Marshal.AllocHGlobal(maxModules * 16);
        new Span<byte>((void*)descTable, maxModules * 16).Clear();
        for (int i = 0; i < descs.Count && i < maxModules; i++)
        {
            Marshal.WriteInt32(descTable, i * 16 + 0, unchecked((int)descs[i].memsz));
            Marshal.WriteInt32(descTable, i * 16 + 4, unchecked((int)descs[i].filesz));
            Marshal.WriteInt32(descTable, i * 16 + 12, unchecked((int)(descs[i].tdata == IntPtr.Zero ? 0u : ToArm32Address(descs[i].tdata))));
        }
        _tlsBaseModId = baseModId;
        _tlsMaxModules = maxModules;
        _tlsDescTable = descTable;

        IntPtr dtor = EmitTlsDestructor(maxModules, freePtr);
        if (pthread_key_create(out uint tlsKey, dtor) != 0)
            throw new InvalidOperationException("pthread_key_create failed");

        var c = new List<uint>(64);
        c.Add(Ldr(1, 0, 0));         // [0] r1 = ti->ti_module
        c.Add(0);                    // [1] ldr r2, [pc, #lit_base]
        c.Add(SubReg(2, 1, 2));      // [2] r2 = slotIdx
        c.Add(CmpImm(2, (uint)maxModules)); // [3]
        int bloMapped = c.Count; c.Add(0);  // [4] blo mapped
        c.Add(0);                    // [5] ldr r12, [pc, #lit_real]
        c.Add(BxR12());              // [6] tail-call real __tls_get_addr
        int mapped = c.Count;        // [7]
        c.Add(PushR4R5R6Lr);         // push {r4,r5,r6,lr} (16B, 8-aligned)
        c.Add(Mov(4, 0));            // r4 = ti*
        c.Add(Mov(5, 2));            // r5 = slotIdx
        c.Add(0);                    // ldr r0, [pc, #lit_key]
        int ldrKey1 = c.Count - 1;
        c.Add(0);                    // ldr r12, [pc, #lit_getspec]
        int ldrGet = c.Count - 1;
        c.Add(BlxR12());             // r0 = threadModules
        c.Add(CmpImm(0, 0));
        int bneHaveArr = c.Count; c.Add(0);
        // allocate threadModules = calloc(maxModules, 4)
        c.Add(MovImm(0, (uint)maxModules));
        c.Add(MovImm(1, 4));
        c.Add(0);                    // ldr r12, [pc, #lit_calloc]
        int ldrCalloc1 = c.Count - 1;
        c.Add(BlxR12());
        c.Add(Mov(6, 0));            // r6 = new arr
        c.Add(0);                    // ldr r0, [pc, #lit_key]
        int ldrKey2 = c.Count - 1;
        c.Add(Mov(1, 6));
        c.Add(0);                    // ldr r12, [pc, #lit_setspec]
        int ldrSet = c.Count - 1;
        c.Add(BlxR12());
        c.Add(Mov(0, 6));
        int haveArr = c.Count;
        c.Add(Mov(6, 0));            // r6 = threadModules
        c.Add(LdrRegLsl(0, 6, 5, 2)); // r0 = arr[slotIdx]
        c.Add(CmpImm(0, 0));
        int bneHaveBlk = c.Count; c.Add(0);
        // allocate block: desc = descTable + slotIdx*16
        c.Add(0);                    // ldr r0, [pc, #lit_descs]
        int ldrDescs1 = c.Count - 1;
        c.Add(AddRegLsl(0, 0, 5, 4));
        c.Add(Ldr(1, 0, 0));         // memsz
        c.Add(MovImm(0, 1));
        c.Add(0);                    // ldr r12, [pc, #lit_calloc]
        int ldrCalloc2 = c.Count - 1;
        c.Add(BlxR12());             // r0 = block (calloc zeroes)
        c.Add(StrRegLsl(0, 6, 5, 2)); // arr[slotIdx] = block
        c.Add(Mov(6, 0));            // r6 = block
        c.Add(0);                    // ldr r1, [pc, #lit_descs]
        int ldrDescs2 = c.Count - 1;
        c.Add(AddRegLsl(1, 1, 5, 4));
        c.Add(Ldr(2, 1, 4));         // filesz
        c.Add(CmpImm(2, 0));
        int beqDoneCopy = c.Count; c.Add(0);
        c.Add(Ldr(1, 1, 12));        // tdata
        c.Add(0);                    // ldr r12, [pc, #lit_memcpy]
        int ldrMemcpy = c.Count - 1;
        c.Add(BlxR12());             // memcpy(block, tdata, filesz); r0 = block
        int doneCopy = c.Count;
        c.Add(Mov(0, 6));            // r0 = block (also reached when filesz==0)
        int haveBlk = c.Count;
        c.Add(Ldr(1, 4, 4));         // ti->ti_offset
        c.Add(AddReg(0, 0, 1));      // block + offset
        c.Add(PopR4R5R6Pc);

        int litBase = c.Count; c.Add(baseModId);
        int litReal = c.Count; c.Add(ToArm32Address(realTlsGetAddr));
        int litKey = c.Count; c.Add(tlsKey);
        int litGet = c.Count; c.Add(ToArm32Address(getP));
        int litSet = c.Count; c.Add(ToArm32Address(setP));
        int litCalloc = c.Count; c.Add(ToArm32Address(callocP));
        int litDescs = c.Count; c.Add(ToArm32Address(descTable));
        int litMemcpy = c.Count; c.Add(ToArm32Address(memcpyP));

        PatchBranch(c, bloMapped, 0x3, mapped);
        PatchLdrPc(c, 1, 2, litBase);
        PatchLdrPc(c, 5, 12, litReal);
        PatchLdrPc(c, ldrKey1, 0, litKey);
        PatchLdrPc(c, ldrGet, 12, litGet);
        PatchBranch(c, bneHaveArr, 0x1, haveArr);
        PatchLdrPc(c, ldrCalloc1, 12, litCalloc);
        PatchLdrPc(c, ldrKey2, 0, litKey);
        PatchLdrPc(c, ldrSet, 12, litSet);
        PatchBranch(c, bneHaveBlk, 0x1, haveBlk);
        PatchLdrPc(c, ldrDescs1, 0, litDescs);
        PatchLdrPc(c, ldrCalloc2, 12, litCalloc);
        PatchLdrPc(c, ldrDescs2, 1, litDescs);
        PatchBranch(c, beqDoneCopy, 0x0, doneCopy);
        PatchLdrPc(c, ldrMemcpy, 12, litMemcpy);
        return Commit(c);
    }

    // void dtor(void** arr): free each non-null block, then free(arr). Tail-free via r12.
    private IntPtr EmitTlsDestructor(int maxModules, IntPtr freePtr)
    {
        var c = new List<uint>(20);
        c.Add(PushR4R5Lr);           // push {r4,r5,lr} (12B) + sub sp,#4 for 8-align
        c.Add(0xE24DD004u);          // sub sp, sp, #4
        c.Add(Mov(4, 0));            // r4 = arr
        c.Add(MovImm(5, 0));         // r5 = i
        int loop = c.Count;
        c.Add(LdrRegLsl(0, 4, 5, 2)); // r0 = arr[i]
        c.Add(CmpImm(0, 0));
        int beqSkip = c.Count; c.Add(0);
        c.Add(0);                    // ldr r12, [pc, #lit_free]
        int ldrFree1 = c.Count - 1;
        c.Add(BlxR12());             // free(arr[i])
        int skip = c.Count;
        c.Add(AddImm(5, 5, 1));
        c.Add(CmpImm(5, (uint)maxModules));
        int bloLoop = c.Count; c.Add(0);
        c.Add(Mov(0, 4));            // r0 = arr
        c.Add(0);                    // ldr r12, [pc, #lit_free]
        int ldrFree2 = c.Count - 1;
        c.Add(0xE28DD004u);          // add sp, sp, #4
        c.Add(PopR4R5Lr);            // pop {r4,r5,lr}
        c.Add(BxR12());              // tail free(arr)
        int litFree = c.Count; c.Add(ToArm32Address(freePtr));
        PatchBranch(c, beqSkip, 0x0, skip);
        PatchBranch(c, bloLoop, 0x3, loop);
        PatchLdrPc(c, ldrFree1, 12, litFree);
        PatchLdrPc(c, ldrFree2, 12, litFree);
        return Commit(c);
    }

    public void SetTlsDesc(uint moduleId, uint memsz, uint filesz, uint tdata32)
    {
        if (_tlsDescTable == IntPtr.Zero || memsz == 0)
            return;
        uint slot = unchecked(moduleId - _tlsBaseModId);
        if (slot >= (uint)_tlsMaxModules)
            throw new InvalidOperationException($"Exceeded max native TLS module slots ({_tlsMaxModules}, moduleId=0x{moduleId:X}).");
        int off = (int)slot * 16;
        Marshal.WriteInt32(_tlsDescTable, off + 4, unchecked((int)filesz));
        Marshal.WriteInt32(_tlsDescTable, off + 12, unchecked((int)tdata32));
        Thread.MemoryBarrier();
        Marshal.WriteInt32(_tlsDescTable, off + 0, unchecked((int)memsz));
    }

    // ================= dl_iterate_phdr =================
    // Pure-native iterator: walks glibc's real modules via realIterate(cb, data), then reports
    // manually mapped modules from a pre-allocated Arm32DlPhdrInfo[MaxPhdrModules] table using a
    // live module count so modules mapped later via dlopen become visible without CoreCLR 6 re-entry.
    internal void EnsurePhdrTable()
    {
        if (_phdrTable != IntPtr.Zero)
            return;
        _phdrTable = Marshal.AllocHGlobal(MaxPhdrModules * DlPhdrInfoSize);
        new Span<byte>((void*)_phdrTable, MaxPhdrModules * DlPhdrInfoSize).Clear();
        _phdrCountPtr = Marshal.AllocHGlobal(4);
        Marshal.WriteInt32(_phdrCountPtr, 0);
        _phdrCount = 0;
    }

    internal void EnsureExidxAndTlsTablesForTest(uint baseModId, int maxModules)
    {
        if (_exidxTable == IntPtr.Zero)
        {
            _exidxTable = Marshal.AllocHGlobal(MaxExidxEntries * 16);
            new Span<byte>((void*)_exidxTable, MaxExidxEntries * 16).Clear();
            _exidxCount = 0;
        }
        if (_tlsDescTable == IntPtr.Zero)
        {
            _tlsBaseModId = baseModId;
            _tlsMaxModules = maxModules;
            _tlsDescTable = Marshal.AllocHGlobal(maxModules * 16);
            new Span<byte>((void*)_tlsDescTable, maxModules * 16).Clear();
        }
    }

    internal void AddPhdrModule(uint loadBias, uint fullPathCStr32, uint phdrsPtr32, ushort phnum)
    {
        EnsurePhdrTable();
        if (_phdrCount >= MaxPhdrModules)
            throw new InvalidOperationException($"Exceeded max mapped dl_iterate_phdr modules ({MaxPhdrModules}).");
        int nextCount = _phdrCount + 1;
        WritePhdrEntry(_phdrTable, _phdrCount, loadBias, fullPathCStr32, phdrsPtr32, phnum, (ulong)nextCount, 0);
        UpdatePhdrAdds((ulong)nextCount);
        Thread.MemoryBarrier();
        _phdrCount = nextCount;
        Marshal.WriteInt32(_phdrCountPtr, nextCount);
    }

    internal void UpdatePhdrAdds(ulong adds)
    {
        if (_phdrTable == IntPtr.Zero)
            return;
        uint addsLow = unchecked((uint)adds);
        for (int i = 0; i < _phdrCount; i++)
        {
            byte* rec = (byte*)_phdrTable + i * DlPhdrInfoSize;
            // Atomic 32-bit aligned store on ARM32; upper 32 bits remain 0 from initialization.
            *(uint*)(rec + 16) = addsLow;
        }
    }

    internal Arm32DlPhdrInfo ReadPhdrEntry(int index)
    {
        if (_phdrTable == IntPtr.Zero || (uint)index >= (uint)MaxPhdrModules)
            throw new ArgumentOutOfRangeException(nameof(index));
        return *(Arm32DlPhdrInfo*)((byte*)_phdrTable + index * DlPhdrInfoSize);
    }

    internal int ReadPhdrCount() => _phdrCountPtr == IntPtr.Zero ? 0 : Marshal.ReadInt32(_phdrCountPtr);

    // Keep dlpi_tls_modid = 0 and dlpi_tls_data = 0 so synthetic mapper TLS module IDs (0x4000+)
    // never leak to external glibc code expecting glibc DTV indices.
    internal static void WritePhdrEntry(IntPtr table, int index, uint loadBias, uint fullPathCStr32, uint phdrsPtr32, ushort phnum, ulong adds, ulong subs)
    {
        byte* rec = (byte*)table + index * DlPhdrInfoSize;
        new Span<byte>(rec, DlPhdrInfoSize).Clear();
        var info = new Arm32DlPhdrInfo
        {
            dlpi_addr = loadBias,
            dlpi_name = fullPathCStr32,
            dlpi_phdr = phdrsPtr32,
            dlpi_phnum = phnum,
            dlpi_adds = adds,
            dlpi_subs = subs,
            dlpi_tls_modid = 0,
            dlpi_tls_data = 0,
        };
        *(Arm32DlPhdrInfo*)rec = info;
    }

    internal static List<uint> BuildDlIteratePhdrInstructions(uint realIterate32, uint infoTable32, uint countPtr32)
    {
        var c = new List<uint>(32);
        c.Add(0xE92D41F0u);                   // [0]  push {r4-r8, lr} (24B, keeps 8-byte alignment)
        c.Add(Mov(4, 0));                     // [1]  r4 = cb
        c.Add(Mov(5, 1));                     // [2]  r5 = data
        int ldrReal = c.Count; c.Add(0);      // [3]  ldr r12, [pc, #lit_real]
        c.Add(BlxR12());                      // [4]  r0 = real(cb, data)
        c.Add(CmpImm(0, 0));                  // [5]
        int bneDone1 = c.Count; c.Add(0);     // [6]  if (r0 != 0) return r0
        int ldrTab = c.Count; c.Add(0);       // [7]  ldr r6, [pc, #lit_table]
        int ldrCntPtr = c.Count; c.Add(0);    // [8]  ldr r7, [pc, #lit_count_ptr]
        c.Add(Ldr(7, 7, 0));                  // [9]  r7 = *countPtr
        c.Add(DmbIsh);                        // [10] dmb ish (acquire barrier before reading infoTable)
        int loop = c.Count;                   // [11]
        c.Add(CmpImm(7, 0));                  // [11]
        int beqDone = c.Count; c.Add(0);      // [12] r0 stays 0 if count == 0 or all callbacks returned 0
        c.Add(Mov(0, 6));                     // [13] r0 = &info[i]
        c.Add(MovImm(1, DlPhdrInfoSize));     // [14] r1 = sizeof(struct dl_phdr_info) on glibc/arm32
        c.Add(Mov(2, 5));                     // [15] r2 = data
        c.Add(0xE12FFF34u);                   // [16] blx r4
        c.Add(CmpImm(0, 0));                  // [17]
        int bneDone2 = c.Count; c.Add(0);     // [18]
        c.Add(AddImm(6, 6, DlPhdrInfoSize));  // [19]
        c.Add(0xE2477001u);                   // [20] sub r7, r7, #1
        int bLoop = c.Count; c.Add(0);        // [21]
        int done = c.Count;                   // [22]
        c.Add(0xE8BD81F0u);                   // [22] pop {r4-r8, pc}
        int litReal = c.Count; c.Add(realIterate32);
        int litTab = c.Count; c.Add(infoTable32);
        int litCntPtr = c.Count; c.Add(countPtr32);

        PatchLdrPc(c, ldrReal, 12, litReal);
        PatchBranch(c, bneDone1, 0x1, done);
        PatchLdrPc(c, ldrTab, 6, litTab);
        PatchLdrPc(c, ldrCntPtr, 7, litCntPtr);
        PatchBranch(c, beqDone, 0x0, done);
        PatchBranch(c, bneDone2, 0x1, done);
        PatchBranch(c, bLoop, 0xE, loop);
        return c;
    }

    public IntPtr EmitDlIteratePhdr(IntPtr realIterate)
    {
        EnsurePhdrTable();
        return Commit(BuildDlIteratePhdrInstructions(
            ToArm32Address(realIterate),
            ToArm32Address(_phdrTable),
            ToArm32Address(_phdrCountPtr)));
    }

    [DllImport("libc.so.6")]
    private static extern IntPtr calloc(nuint nmemb, nuint size);
    [DllImport("libc.so.6")]
    private static extern IntPtr memcpy(IntPtr dst, IntPtr src, nuint n);
}
