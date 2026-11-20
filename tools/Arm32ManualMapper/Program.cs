using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Arm32ManualMapper
{
    // =========================================================================
    // 1. POSIX / ARM32 Linux Native Interop Definitions
    // =========================================================================
    internal static unsafe class NativeLinux
    {
        public const int PROT_NONE  = 0x0;
        public const int PROT_READ  = 0x1;
        public const int PROT_WRITE = 0x2;
        public const int PROT_EXEC  = 0x4;

        public const int MAP_PRIVATE   = 0x02;
        public const int MAP_FIXED     = 0x10;
        public const int MAP_ANONYMOUS = 0x20;

        public const int RTLD_LAZY   = 0x00001;
        public const int RTLD_NOW    = 0x00002;
        public const int RTLD_LOCAL  = 0x00000;
        public const int RTLD_GLOBAL = 0x00100;

        // ARM32 private syscall: __ARM_NR_cacheflush (0x0f0002 = 983042)
        public const int SYS_ARM_CACHEFLUSH = 0x0f0002;
        public const int AT_HWCAP = 16;

        [DllImport("libc.so.6", EntryPoint = "mmap", SetLastError = true)]
        public static extern IntPtr Mmap(IntPtr addr, nuint length, int prot, int flags, int fd, nint offset);

        [DllImport("libc.so.6", EntryPoint = "munmap", SetLastError = true)]
        public static extern int Munmap(IntPtr addr, nuint length);

        [DllImport("libc.so.6", EntryPoint = "mprotect", SetLastError = true)]
        public static extern int Mprotect(IntPtr addr, nuint length, int prot);

        [DllImport("libc.so.6", EntryPoint = "syscall", SetLastError = true)]
        public static extern nint Syscall3(nint number, nint arg1, nint arg2, nint arg3);

        [DllImport("libc.so.6", EntryPoint = "getauxval")]
        public static extern uint GetAuxVal(uint type);

        [DllImport("libdl.so.2", EntryPoint = "dlopen")]
        public static extern IntPtr DlOpen(byte* filename, int flags);

        [DllImport("libdl.so.2", EntryPoint = "dlsym")]
        public static extern IntPtr DlSym(IntPtr handle, byte* symbol);

        [DllImport("libdl.so.2", EntryPoint = "dlclose")]
        public static extern int DlClose(IntPtr handle);

        [DllImport("libdl.so.2", EntryPoint = "dlerror")]
        public static extern IntPtr DlError();

        [DllImport("libdl.so.2", EntryPoint = "dladdr")]
        public static extern int DlAddr(IntPtr addr, DlInfo* info);

        [DllImport("libc.so.6", EntryPoint = "__gnu_Unwind_Find_exidx")]
        public static extern IntPtr RealUnwindFindExidx(uint pc, int* pcount);

        [DllImport("libc.so.6", EntryPoint = "dl_iterate_phdr")]
        public static extern int DlIteratePhdr(IntPtr callback, IntPtr data);

        [DllImport("libpthread.so.0", EntryPoint = "pthread_self")]
        public static extern nuint PthreadSelf();

        [StructLayout(LayoutKind.Sequential)]
        public struct DlInfo
        {
            public IntPtr dli_fname; // Pathname of shared object
            public IntPtr dli_fbase; // Base address at which shared object is loaded
            public IntPtr dli_sname; // Name of nearest symbol
            public IntPtr dli_saddr; // Exact address of nearest symbol
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DlPhdrInfo
        {
            public uint dlpi_addr;
            public IntPtr dlpi_name;
            public Elf32_Phdr* dlpi_phdr;
            public ushort dlpi_phnum;
        }

        public static void FlushInstructionCache(IntPtr start, nuint size)
        {
            nint begin = (nint)start;
            nint end = begin + (nint)size;
            Syscall3(SYS_ARM_CACHEFLUSH, begin, end, 0);
        }

        public static IntPtr DlOpenString(string? path, int flags)
        {
            if (path == null) return DlOpen(null, flags);
            byte[] bytes = Encoding.UTF8.GetBytes(path + "\0");
            fixed (byte* p = bytes)
            {
                return DlOpen(p, flags);
            }
        }

        public static IntPtr DlSymString(IntPtr handle, string name)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(name + "\0");
            fixed (byte* p = bytes)
            {
                return DlSym(handle, p);
            }
        }

        // --- glibc 2.34 / time64 compatibility shims for .NET 10 on glibc 2.30 ---
        // .NET 10 linux-arm references __*_time64 / __pthread_*64 symbols that only
        // exist in glibc >= 2.34. The TV has glibc 2.30 where the plain variants
        // already use 64-bit time_t/structs on ARM32, so we forward to those.
        [DllImport("libc.so.6", EntryPoint = "ioctl")] public static extern int Shim_ioctl(int fd, uint req, void* arg);
        [DllImport("libc.so.6", EntryPoint = "fcntl64")] public static extern int Shim_fcntl64(int fd, int cmd, void* arg);
        [DllImport("libc.so.6", EntryPoint = "__fxstat64")] public static extern int Shim_fxstat64(int ver, int fd, void* buf);
        [DllImport("libc.so.6", EntryPoint = "__lxstat64")] public static extern int Shim_lxstat64(int ver, string path, void* buf);
        [DllImport("libc.so.6", EntryPoint = "clock_gettime")] public static extern int Shim_clock_gettime(int clk, void* ts);
        [DllImport("libc.so.6", EntryPoint = "gettimeofday")] public static extern int Shim_gettimeofday(void* tv, void* tz);
        [DllImport("libc.so.6", EntryPoint = "nanosleep")] public static extern int Shim_nanosleep(void* req, void* rem);
        [DllImport("libc.so.6", EntryPoint = "prctl")] public static extern int Shim_prctl(int opt, uint a2, uint a3, uint a4, uint a5);
        [DllImport("libc.so.6", EntryPoint = "gmtime_r")] public static extern IntPtr Shim_gmtime_r(void* t, void* tm);
        [DllImport("libc.so.6", EntryPoint = "futimens")] public static extern int Shim_futimens(int fd, void* times);
        [DllImport("libc.so.6", EntryPoint = "getrusage")] public static extern int Shim_getrusage(int who, void* usage);
        [DllImport("libc.so.6", EntryPoint = "getsockopt")] public static extern int Shim_getsockopt(int fd, int lvl, int opt, void* val, void* len);
        [DllImport("libpthread.so.0", EntryPoint = "pthread_cond_timedwait")] public static extern int Shim_pthread_cond_timedwait(void* cond, void* mutex, void* abstime);
        [DllImport("libc.so.6", EntryPoint = "__xstat64")] public static extern int Shim_xstat64(int ver, string path, void* buf);
        [DllImport("libc.so.6", EntryPoint = "__fxstatat64")] public static extern int Shim_fxstatat64(int ver, int fd, string path, void* buf, int flags);
        // glibc 2.34 time64 stat variants DROP the leading _STAT_VER argument.
        private static int Shim_stat64_time64(string path, void* buf) => Shim_xstat64(0, path, buf);
        private static int Shim_fstat64_time64(int fd, void* buf) => Shim_fxstat64(0, fd, buf);
        private static int Shim_lstat64_time64(string path, void* buf) => Shim_lxstat64(0, path, buf);
        private static int Shim_fstatat64_time64(int fd, string path, void* buf, int flags) => Shim_fxstatat64(0, fd, path, buf, flags);
        [DllImport("libc.so.6", EntryPoint = "gmtime")] public static extern IntPtr Shim_gmtime(void* t);
        [DllImport("libc.so.6", EntryPoint = "mktime")] public static extern long Shim_mktime(void* tm);
        [DllImport("libc.so.6", EntryPoint = "timegm")] public static extern long Shim_timegm(void* tm);
        [DllImport("libc.so.6", EntryPoint = "time")] public static extern long Shim_time(void* t);
        [DllImport("libc.so.6", EntryPoint = "utimensat")] public static extern int Shim_utimensat(int fd, string path, void* times, int flags);
        [DllImport("libc.so.6", EntryPoint = "recvmsg")] public static extern nint Shim_recvmsg(int fd, void* msg, int flags);
        [DllImport("libc.so.6", EntryPoint = "sendmsg")] public static extern nint Shim_sendmsg(int fd, void* msg, int flags);
        [DllImport("libc.so.6", EntryPoint = "setsockopt")] public static extern int Shim_setsockopt(int fd, int lvl, int opt, void* val, uint len);
        // mknod: glibc 2.30 only exports __xmknod(ver,path,mode,dev); 2.33 added mknod.
        [DllImport("libc.so.6", EntryPoint = "__xmknod")] public static extern int Shim_xmknod(int ver, string path, uint mode, ulong dev);
        private static int Shim_mknod(string path, uint mode, ulong dev) => Shim_xmknod(0, path, mode, dev);
        // libstdc++ GLIBCXX_3.4.29 symbol absent on TV (GCC9); forward to the
        // equivalent __cxa_throw_bad_array_new_length which IS present.
        [DllImport("libstdc++.so.6", EntryPoint = "__cxa_throw_bad_array_new_length")] public static extern void Shim_throw_bad_array_new_length();
        // __libc_single_threaded: glibc 2.32+ data symbol; provide a native byte=0
        // (always assume multithreaded -> safe slow path in coreclr).
        private static IntPtr _singleThreadedPtr = IntPtr.Zero;
        private static unsafe IntPtr GetSingleThreadedPtr()
        {
            if (_singleThreadedPtr == IntPtr.Zero)
            {
                _singleThreadedPtr = Marshal.AllocHGlobal(1);
                *(byte*)_singleThreadedPtr = 0;
            }
            return _singleThreadedPtr;
        }

        // Map of .NET10 (glibc2.34) symbol -> shim function pointer. Built once.
        public static Dictionary<string, IntPtr> BuildTime64Shims()
        {
            var d = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
            void Add(string n, Delegate f) => d[n] = Marshal.GetFunctionPointerForDelegate(f);
            // keep delegates alive via static list
            _shimDelegates.Add(new ioctlDel(Shim_ioctl)); d["__ioctl_time64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new fcntlDel(Shim_fcntl64)); d["__fcntl_time64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new fxstatDel(Shim_fstat64_time64)); d["__fstat64_time64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new lxstatDel(Shim_lstat64_time64)); d["__lstat64_time64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new clockDel(Shim_clock_gettime)); d["__clock_gettime64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new gtodDel(Shim_gettimeofday)); d["__gettimeofday64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new nanoDel(Shim_nanosleep)); d["__nanosleep64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new prctlDel(Shim_prctl)); d["__prctl_time64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new gmtimeDel(Shim_gmtime_r)); d["__gmtime64_r"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new futimensDel(Shim_futimens)); d["__futimens64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new rusageDel(Shim_getrusage)); d["__getrusage64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new sockoptDel(Shim_getsockopt)); d["__getsockopt64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new condwaitDel(Shim_pthread_cond_timedwait)); d["__pthread_cond_timedwait64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new xstatDel(Shim_stat64_time64)); d["__stat64_time64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new fxstatatDel(Shim_fstatat64_time64)); d["__fstatat64_time64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new gmtimeNDel(Shim_gmtime)); d["__gmtime64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new mktimeDel(Shim_mktime)); d["__mktime64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new timegmDel(Shim_timegm)); d["__timegm64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new timeDel(Shim_time)); d["__time64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new utimensatDel(Shim_utimensat)); d["__utimensat64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new recvmsgDel(Shim_recvmsg)); d["__recvmsg64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new sendmsgDel(Shim_sendmsg)); d["__sendmsg64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new setsockoptDel(Shim_setsockopt)); d["__setsockopt64"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new mknodDel(Shim_mknod)); d["mknod"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            _shimDelegates.Add(new badarrDel(Shim_throw_bad_array_new_length)); d["_ZSt28__throw_bad_array_new_lengthv"] = Marshal.GetFunctionPointerForDelegate(_shimDelegates[^1]);
            // data symbol: address of a native byte (0 = multithreaded)
            d["__libc_single_threaded"] = GetSingleThreadedPtr();
            return d;
        }
        private static readonly List<Delegate> _shimDelegates = new();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ioctlDel(int fd, uint req, void* arg);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int fcntlDel(int fd, int cmd, void* arg);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int fxstatDel(int fd, void* buf);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int lxstatDel(string path, void* buf);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int clockDel(int clk, void* ts);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int gtodDel(void* tv, void* tz);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nanoDel(void* req, void* rem);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int prctlDel(int opt, uint a2, uint a3, uint a4, uint a5);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr gmtimeDel(void* t, void* tm);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int futimensDel(int fd, void* times);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int rusageDel(int who, void* usage);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int sockoptDel(int fd, int lvl, int opt, void* val, void* len);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int condwaitDel(void* cond, void* mutex, void* abstime);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int xstatDel(string path, void* buf);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int fxstatatDel(int fd, string path, void* buf, int flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr gmtimeNDel(void* t);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate long mktimeDel(void* tm);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate long timegmDel(void* tm);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate long timeDel(void* t);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int utimensatDel(int fd, string path, void* times, int flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint recvmsgDel(int fd, void* msg, int flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint sendmsgDel(int fd, void* msg, int flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int setsockoptDel(int fd, int lvl, int opt, void* val, uint len);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int mknodDel(string path, uint mode, ulong dev);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void badarrDel();
    }

    // =========================================================================
    // 2. ELF32 Structures & Constants (ARMv7 EABI)
    // =========================================================================
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal unsafe struct Elf32_Ehdr
    {
        public fixed byte e_ident[16];
        public ushort e_type;
        public ushort e_machine;
        public uint e_version;
        public uint e_entry;
        public uint e_phoff;
        public uint e_shoff;
        public uint e_flags;
        public ushort e_ehsize;
        public ushort e_phentsize;
        public ushort e_phnum;
        public ushort e_shentsize;
        public ushort e_shnum;
        public ushort e_shstrndx;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Elf32_Phdr
    {
        public uint p_type;
        public uint p_offset;
        public uint p_vaddr;
        public uint p_paddr;
        public uint p_filesz;
        public uint p_memsz;
        public uint p_flags;
        public uint p_align;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Elf32_Shdr
    {
        public uint sh_name;
        public uint sh_type;
        public uint sh_flags;
        public uint sh_addr;
        public uint sh_offset;
        public uint sh_size;
        public uint sh_link;
        public uint sh_info;
        public uint sh_addralign;
        public uint sh_entsize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Elf32_Dyn
    {
        public int d_tag;
        public uint d_val;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Elf32_Sym
    {
        public uint st_name;
        public uint st_value;
        public uint st_size;
        public byte st_info;
        public byte st_other;
        public ushort st_shndx;

        public int Bind => st_info >> 4;
        public int Type => st_info & 0xF;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Elf32_Rel
    {
        public uint r_offset;
        public uint r_info;

        public uint Sym => r_info >> 8;
        public uint Type => r_info & 0xFF;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Elf32_Rela
    {
        public uint r_offset;
        public uint r_info;
        public int r_addend;

        public uint Sym => r_info >> 8;
        public uint Type => r_info & 0xFF;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct TlsIndex
    {
        public uint ModuleId;
        public uint Offset;
    }

    internal static class ElfConstants
    {
        public const ushort ET_DYN = 3;
        public const ushort EM_ARM = 40;

        public const uint PT_LOAD      = 1;
        public const uint PT_DYNAMIC   = 2;
        public const uint PT_TLS       = 7;
        public const uint PT_ARM_EXIDX = 0x70000001;

        public const uint PF_X = 0x1;
        public const uint PF_W = 0x2;
        public const uint PF_R = 0x4;

        public const uint SHT_DYNSYM = 11;

        public const int DT_NULL         = 0;
        public const int DT_NEEDED       = 1;
        public const int DT_PLTRELSZ     = 2;
        public const int DT_HASH         = 4;
        public const int DT_STRTAB       = 5;
        public const int DT_SYMTAB       = 6;
        public const int DT_RELA         = 7;
        public const int DT_RELASZ       = 8;
        public const int DT_RELAENT      = 9;
        public const int DT_STRSZ        = 10;
        public const int DT_INIT         = 12;
        public const int DT_REL          = 17;
        public const int DT_RELSZ        = 18;
        public const int DT_RELENT       = 19;
        public const int DT_PLTREL       = 20;
        public const int DT_JMPREL       = 23;
        public const int DT_INIT_ARRAY   = 25;
        public const int DT_INIT_ARRAYSZ = 27;
        public const int DT_GNU_HASH     = 0x6ffffef5;

        public const int STB_LOCAL  = 0;
        public const int STB_GLOBAL = 1;
        public const int STB_WEAK   = 2;

        public const int STT_FUNC      = 2;
        public const int STT_GNU_IFUNC = 10;

        public const ushort SHN_UNDEF = 0;

        // ARM32 Relocations
        public const uint R_ARM_NONE          = 0;
        public const uint R_ARM_ABS32         = 2;
        public const uint R_ARM_TLS_DESC      = 13;
        public const uint R_ARM_TLS_DTPMOD32  = 17;
        public const uint R_ARM_TLS_DTPOFF32  = 18;
        public const uint R_ARM_TLS_TPOFF32   = 19;
        public const uint R_ARM_COPY          = 20;
        public const uint R_ARM_GLOB_DAT      = 21;
        public const uint R_ARM_JUMP_SLOT     = 22;
        public const uint R_ARM_RELATIVE      = 23;
        public const uint R_ARM_IRELATIVE     = 160;
    }

    // =========================================================================
    // 3. Manually Mapped ELF32 Module Representation
    // =========================================================================
    internal sealed unsafe class MappedElfModule
    {
        public string FullPath { get; }
        public string FileName { get; }
        public IntPtr SyntheticHandle { get; }
        public IntPtr FullPathCStr { get; }

        public IntPtr BaseAddress { get; set; }
        public uint LoadBias { get; set; }
        public uint TotalMappedSize { get; set; }
        public uint MinVAddr { get; set; }

        public List<Elf32_Phdr> LoadSegments { get; } = new();
        public Elf32_Phdr? DynamicSegment { get; set; }
        public Elf32_Phdr? TlsSegment { get; set; }
        public Elf32_Phdr? ExidxSegment { get; set; }
        public IntPtr ProgramHeadersPtr { get; set; }
        public ushort ProgramHeaderCount { get; set; }

        public uint TlsModuleId { get; set; }
        public IntPtr TlsTemplate { get; set; }
        public uint TlsFileSize { get; set; }
        public uint TlsMemSize { get; set; }

        public byte* StrTab { get; set; }
        public Elf32_Sym* SymTab { get; set; }
        public uint SymCount { get; set; }
        public uint* SysvHash { get; set; }
        public uint* GnuHash { get; set; }

        public uint DtInit { get; set; }
        public uint DtInitArray { get; set; }
        public uint DtInitArraySz { get; set; }

        public bool Relocated { get; set; }
        public bool Published { get; set; }
        public bool Initialized { get; set; }
        public bool Initializing { get; set; }
        public int InitThreadId { get; set; }
        public Exception? InitException { get; set; }
        public object InitLock { get; } = new();

        // Export table cached from .dynsym for fast lookup
        public Dictionary<string, uint> ExportedSymbols { get; } = new(StringComparer.Ordinal);

        public MappedElfModule(string fullPath, IntPtr syntheticHandle, uint tlsModuleId)
        {
            FullPath = Path.GetFullPath(fullPath);
            FileName = Path.GetFileName(FullPath);
            SyntheticHandle = syntheticHandle;
            TlsModuleId = tlsModuleId;
            FullPathCStr = Marshal.StringToHGlobalAnsi(FullPath);
        }

        public bool ContainsAddress(uint addr)
        {
            uint start = NativeHooks.ToArm32Address(BaseAddress);
            return addr >= start && addr < start + TotalMappedSize;
        }
    }

    // =========================================================================
    // 4. ARM32 ELF Loader & Symbol / TLS / Unwind Interposer
    // =========================================================================
    internal sealed unsafe class Arm32ElfLoader
    {
        private const uint PageSize = 4096;

        private readonly string _dotnet8Dir;
        private readonly Dictionary<string, MappedElfModule> _modulesByName = new(StringComparer.Ordinal);
        private readonly Dictionary<IntPtr, MappedElfModule> _modulesByHandle = new();
        private readonly Dictionary<uint, MappedElfModule> _modulesByTlsId = new();
        private NativeHooks _nativeHooks;
        private IntPtr _nativeExidxPtr;
        private IntPtr _nativeTlsPtr;
        private IntPtr _nativeDlIteratePhdrPtr;
        private bool _useNativeHooks;
        private bool _dlIteratePhdrInstalled;
        private readonly object _moduleLock = new();
        private readonly List<MappedElfModule> _moduleList = new();
        private volatile MappedElfModule[] _moduleSnapshot = Array.Empty<MappedElfModule>();
        private readonly List<IntPtr> _systemLibHandles = new();
        private readonly Arm32AbiTrampolines _abiTramps = new Arm32AbiTrampolines();
        public int AbiTrampolineCount => _abiTramps.CreatedCount;
        private readonly Dictionary<string, IntPtr> _interposedSymbols = new(StringComparer.Ordinal);

        // Per-thread TLS storage: (pthread_self, tls_module_id) -> allocated TLS block
        private readonly ConcurrentDictionary<(nuint ThreadId, uint ModuleId), IntPtr> _tlsBlocks = new();

        private int _nextHandleId = 0x70000001;
        private uint _nextTlsModuleId = 0x4000;

        // Pinned delegates for native callbacks so the GC never collects or moves them
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr DlOpenDelegate(byte* filename, int flags);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr DlSymDelegate(IntPtr handle, byte* symbol);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DlCloseDelegate(IntPtr handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr DlErrorDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DlAddrDelegate(IntPtr addr, NativeLinux.DlInfo* info);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr TlsGetAddrDelegate(TlsIndex* ti);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr UnwindFindExidxDelegate(uint pc, int* pcount);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DlIteratePhdrCallback(NativeLinux.DlPhdrInfo* info, nuint size, IntPtr data);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void InitFunctionDelegate(int argc, IntPtr argv, IntPtr envp);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint IfuncResolverDelegate(uint hwcap);

        private readonly DlOpenDelegate _dlOpenDel;
        private readonly DlSymDelegate _dlSymDel;
        private readonly DlCloseDelegate _dlCloseDel;
        private readonly DlErrorDelegate _dlErrorDel;
        private readonly DlAddrDelegate _dlAddrDel;
        private readonly TlsGetAddrDelegate _tlsGetAddrDel;
        private readonly UnwindFindExidxDelegate _unwindFindExidxDel;

        public Arm32ElfLoader(string dotnet8Dir)
        {
            _dotnet8Dir = Path.GetFullPath(dotnet8Dir);

            // Pre-open signed system libraries explicitly for isolated symbol resolution
            string[] systemLibs =
            {
                "libc.so.6",
                "libm.so.6",
                "libpthread.so.0",
                "libdl.so.2",
                "librt.so.1",
                "libgcc_s.so.1",
                "libstdc++.so.6",
                "ld-linux-armhf.so.3",
                "libz.so.1",
                "libssl.so.3",
                "libssl.so.1.1",
                "libcrypto.so.3",
                "libcrypto.so.1.1"
            };

            foreach (string lib in systemLibs)
            {
                IntPtr h = NativeLinux.DlOpenString(lib, NativeLinux.RTLD_NOW | NativeLinux.RTLD_LOCAL);
                if (h != IntPtr.Zero)
                {
                    _systemLibHandles.Add(h);
                }
            }

            // Initialize and pin interposition callbacks
            _dlOpenDel = HookedDlOpen;
            _dlSymDel = HookedDlSym;
            _dlCloseDel = HookedDlClose;
            _dlErrorDel = HookedDlError;
            _dlAddrDel = HookedDlAddr;
            _tlsGetAddrDel = HookedTlsGetAddr;
            _unwindFindExidxDel = HookedUnwindFindExidx;

            _interposedSymbols["dlopen"] = Marshal.GetFunctionPointerForDelegate(_dlOpenDel);
            _interposedSymbols["dlsym"] = Marshal.GetFunctionPointerForDelegate(_dlSymDel);
            _interposedSymbols["dlclose"] = Marshal.GetFunctionPointerForDelegate(_dlCloseDel);
            _interposedSymbols["dlerror"] = Marshal.GetFunctionPointerForDelegate(_dlErrorDel);
            _interposedSymbols["dladdr"] = Marshal.GetFunctionPointerForDelegate(_dlAddrDel);
            _interposedSymbols["__tls_get_addr"] = Marshal.GetFunctionPointerForDelegate(_tlsGetAddrDel);
            _interposedSymbols["__gnu_Unwind_Find_exidx"] = Marshal.GetFunctionPointerForDelegate(_unwindFindExidxDel);

            // glibc 2.34/time64 shims so .NET 10 native libs load on glibc 2.30.
            foreach (var kv in Glibc234Shims.BuildAll())
                _interposedSymbols[kv.Key] = kv.Value;
        }

        private static uint AlignDown(uint val, uint align) => val & ~(align - 1);
        private static uint AlignUp(uint val, uint align) => (val + align - 1) & ~(align - 1);

        // ---------------------------------------------------------------------
        // Step 1: Parse ELF headers & map PT_LOAD segments into anonymous RAM
        // ---------------------------------------------------------------------
        internal static Elf32_Phdr[] ReadAndValidateProgramHeaders(ReadOnlySpan<byte> fileBytes, string fileName)
        {
            fixed (byte* filePtr = fileBytes)
            {
                Elf32_Ehdr* ehdr = (Elf32_Ehdr*)filePtr;

                // Validate ELF32 magic, little-endian, ARMv7
                if (fileBytes.Length < sizeof(Elf32_Ehdr) ||
                    ehdr->e_ident[0] != 0x7F || ehdr->e_ident[1] != (byte)'E' ||
                    ehdr->e_ident[2] != (byte)'L' || ehdr->e_ident[3] != (byte)'F')
                    throw new InvalidDataException($"{fileName} is not a valid ELF file.");

                if (ehdr->e_ident[4] != 1 || ehdr->e_ident[5] != 1 || ehdr->e_machine != ElfConstants.EM_ARM)
                    throw new InvalidDataException($"{fileName} is not a 32-bit little-endian ARM ELF.");

                if (ehdr->e_phoff == 0 || ehdr->e_phnum == 0 || ehdr->e_phentsize < sizeof(Elf32_Phdr))
                    throw new InvalidDataException($"{fileName} has invalid ELF program headers.");

                ulong phTableEnd = (ulong)ehdr->e_phoff + (ulong)ehdr->e_phnum * ehdr->e_phentsize;
                if (phTableEnd > (ulong)fileBytes.Length)
                    throw new InvalidDataException($"{fileName} program headers exceed file length.");

                var result = new Elf32_Phdr[ehdr->e_phnum];
                for (int i = 0; i < ehdr->e_phnum; i++)
                {
                    result[i] = *(Elf32_Phdr*)(filePtr + ehdr->e_phoff + (uint)(i * ehdr->e_phentsize));
                }
                return result;
            }
        }

        public MappedElfModule MapLibrary(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string fileName = Path.GetFileName(fullPath);

            lock (_moduleLock)
            {
                if (_modulesByName.TryGetValue(fileName, out MappedElfModule? existing))
                    return existing;

                MappedElfModule module = MapLibraryUnpublishedLocked(fullPath, fileName);
                PublishModuleLocked(module);
                Console.WriteLine($"[Loader] Mapped {module.FileName} at 0x{module.BaseAddress.ToInt64():X8} (size: {module.TotalMappedSize} bytes, exports: {module.ExportedSymbols.Count})");
                return module;
            }
        }

        private MappedElfModule MapLibraryUnpublishedLocked(string fullPath, string fileName)
        {
            byte[] fileBytes = File.ReadAllBytes(fullPath);
            Elf32_Phdr[] parsedPhdrs = ReadAndValidateProgramHeaders(fileBytes, fileName);

            IntPtr handle = (IntPtr)(_nextHandleId++);
            uint tlsModId = _nextTlsModuleId++;
            var module = new MappedElfModule(fullPath, handle, tlsModId);

            try
            {
                fixed (byte* filePtr = fileBytes)
                {
                    Elf32_Ehdr* ehdr = (Elf32_Ehdr*)filePtr;

                    // Copy all original ELF program headers (PT_LOAD, PT_DYNAMIC, PT_ARM_EXIDX, PT_GNU_EH_FRAME, PT_TLS, etc.)
                    IntPtr phCopy = Marshal.AllocHGlobal(parsedPhdrs.Length * sizeof(Elf32_Phdr));
                    Elf32_Phdr* phdrs = (Elf32_Phdr*)phCopy;
                    for (int i = 0; i < parsedPhdrs.Length; i++)
                    {
                        phdrs[i] = parsedPhdrs[i];
                    }
                    module.ProgramHeadersPtr = phCopy;
                    module.ProgramHeaderCount = (ushort)parsedPhdrs.Length;

                    uint minVAddr = uint.MaxValue;
                    uint maxVAddr = 0;

                    for (int i = 0; i < parsedPhdrs.Length; i++)
                    {
                        Elf32_Phdr ph = phdrs[i];
                        switch (ph.p_type)
                        {
                            case ElfConstants.PT_LOAD:
                                module.LoadSegments.Add(ph);
                                if (ph.p_vaddr < minVAddr) minVAddr = ph.p_vaddr;
                                if (ph.p_vaddr + ph.p_memsz > maxVAddr) maxVAddr = ph.p_vaddr + ph.p_memsz;
                                break;
                            case ElfConstants.PT_DYNAMIC:
                                module.DynamicSegment = ph;
                                break;
                            case ElfConstants.PT_TLS:
                                module.TlsSegment = ph;
                                break;
                            case ElfConstants.PT_ARM_EXIDX:
                                module.ExidxSegment = ph;
                                break;
                        }
                    }

                    if (module.LoadSegments.Count == 0)
                        throw new InvalidDataException($"{fileName} has no PT_LOAD segments.");

                    minVAddr = AlignDown(minVAddr, PageSize);
                    maxVAddr = AlignUp(maxVAddr, PageSize);
                    uint totalSize = maxVAddr - minVAddr;

                    // Reserve contiguous anonymous address space with RW permissions initially
                    IntPtr baseAddr = NativeLinux.Mmap(
                        IntPtr.Zero,
                        totalSize,
                        NativeLinux.PROT_READ | NativeLinux.PROT_WRITE,
                        NativeLinux.MAP_PRIVATE | NativeLinux.MAP_ANONYMOUS,
                        -1,
                        0);

                    if (baseAddr == (IntPtr)(-1))
                        throw new InvalidOperationException($"mmap failed for {fileName}: errno={Marshal.GetLastWin32Error()}");

                    module.BaseAddress = baseAddr;
                    module.MinVAddr = minVAddr;
                    module.LoadBias = NativeHooks.ToArm32Address(baseAddr) - minVAddr;
                    module.TotalMappedSize = totalSize;

                    // Copy each PT_LOAD segment into its exact relative offset
                    foreach (Elf32_Phdr seg in module.LoadSegments)
                    {
                        byte* dest = (byte*)(module.LoadBias + seg.p_vaddr);
                        byte* src = filePtr + seg.p_offset;
                        Buffer.MemoryCopy(src, dest, seg.p_memsz, seg.p_filesz);
                    }

                    // Record PT_TLS template if present
                    if (module.TlsSegment.HasValue)
                    {
                        Elf32_Phdr tls = module.TlsSegment.Value;
                        module.TlsTemplate = (IntPtr)(nint)(module.LoadBias + tls.p_vaddr);
                        module.TlsFileSize = tls.p_filesz;
                        module.TlsMemSize = tls.p_memsz;
                    }

                    // Discover .dynsym symbol count from SHT_DYNSYM section header if available
                    if (ehdr->e_shoff != 0 && ehdr->e_shnum > 0)
                    {
                        Elf32_Shdr* shdrs = (Elf32_Shdr*)(filePtr + ehdr->e_shoff);
                        for (int i = 0; i < ehdr->e_shnum; i++)
                        {
                            if (shdrs[i].sh_type == ElfConstants.SHT_DYNSYM && shdrs[i].sh_entsize > 0)
                            {
                                module.SymCount = shdrs[i].sh_size / shdrs[i].sh_entsize;
                                break;
                            }
                        }
                    }

                    ParseDynamicSegment(module);
                    BuildExportTable(module);
                    return module;
                }
            }
            catch
            {
                DisposeUnpublishedModule(module);
                throw;
            }
        }

        private void PublishModuleLocked(MappedElfModule module)
        {
            if (module.Published)
                return;
            module.Published = true;
            _modulesByName[module.FileName] = module;
            _modulesByHandle[module.SyntheticHandle] = module;
            _modulesByTlsId[module.TlsModuleId] = module;
            _moduleList.Add(module);
            _moduleSnapshot = _moduleList.ToArray();
        }

        private static void DisposeUnpublishedModule(MappedElfModule module)
        {
            if (module.BaseAddress != IntPtr.Zero && module.TotalMappedSize > 0)
                NativeLinux.Munmap(module.BaseAddress, module.TotalMappedSize);
            if (module.ProgramHeadersPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(module.ProgramHeadersPtr);
            if (module.FullPathCStr != IntPtr.Zero)
                Marshal.FreeHGlobal(module.FullPathCStr);
        }

        // ---------------------------------------------------------------------
        // Step 2: Parse PT_DYNAMIC and build symbol export table
        // ---------------------------------------------------------------------
        private uint ToMemoryAddr(MappedElfModule module, uint ptrOrVAddr)
        {
            uint baseStart = (uint)module.BaseAddress;
            if (ptrOrVAddr >= baseStart && ptrOrVAddr < baseStart + module.TotalMappedSize)
                return ptrOrVAddr;
            return module.LoadBias + ptrOrVAddr;
        }

        private void ParseDynamicSegment(MappedElfModule module)
        {
            if (!module.DynamicSegment.HasValue)
                throw new InvalidDataException($"{module.FileName} has no PT_DYNAMIC segment.");

            Elf32_Dyn* dyn = (Elf32_Dyn*)(module.LoadBias + module.DynamicSegment.Value.p_vaddr);
            for (; dyn->d_tag != ElfConstants.DT_NULL; dyn++)
            {
                switch (dyn->d_tag)
                {
                    case ElfConstants.DT_STRTAB:
                        module.StrTab = (byte*)ToMemoryAddr(module, dyn->d_val);
                        break;
                    case ElfConstants.DT_SYMTAB:
                        module.SymTab = (Elf32_Sym*)ToMemoryAddr(module, dyn->d_val);
                        break;
                    case ElfConstants.DT_HASH:
                        module.SysvHash = (uint*)ToMemoryAddr(module, dyn->d_val);
                        if (module.SymCount == 0)
                            module.SymCount = module.SysvHash[1]; // nchain == symbol count
                        break;
                    case ElfConstants.DT_GNU_HASH:
                        module.GnuHash = (uint*)ToMemoryAddr(module, dyn->d_val);
                        break;
                    case ElfConstants.DT_INIT:
                        module.DtInit = dyn->d_val;
                        break;
                    case ElfConstants.DT_INIT_ARRAY:
                        module.DtInitArray = dyn->d_val;
                        break;
                    case ElfConstants.DT_INIT_ARRAYSZ:
                        module.DtInitArraySz = dyn->d_val;
                        break;
                }
            }

            // Fallback: derive SymCount from DT_GNU_HASH if section headers were stripped and DT_HASH absent
            if (module.SymCount == 0 && module.GnuHash != null)
            {
                module.SymCount = GetSymCountFromGnuHash(module.GnuHash);
            }
        }

        private static uint GetSymCountFromGnuHash(uint* gnuHash)
        {
            uint nbuckets = gnuHash[0];
            uint symoffset = gnuHash[1];
            uint bloomSize = gnuHash[2];
            uint* buckets = gnuHash + 4 + bloomSize;
            uint* chain = buckets + nbuckets;

            uint maxSym = 0;
            for (uint i = 0; i < nbuckets; i++)
            {
                if (buckets[i] > maxSym)
                    maxSym = buckets[i];
            }
            if (maxSym < symoffset)
                return symoffset;

            while ((chain[maxSym - symoffset] & 1) == 0)
                maxSym++;

            return maxSym + 1;
        }

        private void BuildExportTable(MappedElfModule module)
        {
            if (module.SymTab == null || module.StrTab == null)
                return;

            for (uint i = 1; i < module.SymCount; i++)
            {
                Elf32_Sym sym = module.SymTab[i];
                if (sym.st_shndx == ElfConstants.SHN_UNDEF || sym.st_name == 0)
                    continue;

                if (sym.Bind != ElfConstants.STB_GLOBAL && sym.Bind != ElfConstants.STB_WEAK)
                    continue;

                string name = Marshal.PtrToStringAnsi((IntPtr)(module.StrTab + sym.st_name)) ?? "";
                if (name.Length == 0)
                    continue;

                uint resolvedAddr = module.LoadBias + sym.st_value;
                // Keep bit 0 intact for STT_FUNC Thumb-2 functions!
                // If STB_GLOBAL, overwrite any prior STB_WEAK entry
                if (sym.Bind == ElfConstants.STB_GLOBAL || !module.ExportedSymbols.ContainsKey(name))
                {
                    module.ExportedSymbols[name] = resolvedAddr;
                }
            }
        }

        // ---------------------------------------------------------------------
        // Step 3: Relocate Module, Apply Page Permissions, Flush I-Cache, Init
        // ---------------------------------------------------------------------
        public void RelocateAndProtect(MappedElfModule module, bool runInitializers = true)
        {
            lock (_moduleLock)
            {
                RelocateAndProtectLocked(module);
            }

            // Run DT_INIT and DT_INIT_ARRAY constructors outside _moduleLock via coordinated InitLock
            if (runInitializers)
            {
                RunModuleInitializers(module);
            }
        }

        private void RelocateAndProtectLocked(MappedElfModule module)
        {
            if (module.Relocated)
                return;

            Elf32_Dyn* dyn = (Elf32_Dyn*)(module.LoadBias + module.DynamicSegment!.Value.p_vaddr);

            uint relAddr = 0, relSz = 0;
            uint relaAddr = 0, relaSz = 0;
            uint jmpRelAddr = 0, jmpRelSz = 0, pltRelType = ElfConstants.DT_REL;

            for (Elf32_Dyn* d = dyn; d->d_tag != ElfConstants.DT_NULL; d++)
            {
                switch (d->d_tag)
                {
                    case ElfConstants.DT_REL:      relAddr = ToMemoryAddr(module, d->d_val); break;
                    case ElfConstants.DT_RELSZ:    relSz = d->d_val; break;
                    case ElfConstants.DT_RELA:     relaAddr = ToMemoryAddr(module, d->d_val); break;
                    case ElfConstants.DT_RELASZ:   relaSz = d->d_val; break;
                    case ElfConstants.DT_JMPREL:   jmpRelAddr = ToMemoryAddr(module, d->d_val); break;
                    case ElfConstants.DT_PLTRELSZ: jmpRelSz = d->d_val; break;
                    case ElfConstants.DT_PLTREL:   pltRelType = d->d_val; break;
                }
            }

            var irelativeQueue = new List<(uint TargetAddr, uint ResolverAddr)>();

            // 1. Apply DT_REL relocations (standard on ARM32)
            if (relAddr != 0 && relSz > 0)
            {
                ApplyRelTable(module, (Elf32_Rel*)relAddr, relSz / (uint)sizeof(Elf32_Rel), irelativeQueue);
            }

            // 2. Apply DT_RELA relocations (if any)
            if (relaAddr != 0 && relaSz > 0)
            {
                ApplyRelaTable(module, (Elf32_Rela*)relaAddr, relaSz / (uint)sizeof(Elf32_Rela), irelativeQueue);
            }

            // 3. Apply DT_JMPREL (.rel.plt) relocations (avoiding overlapping range if DT_REL covered it)
            if (jmpRelAddr != 0 && jmpRelSz > 0)
            {
                bool alreadyCovered = (relAddr != 0 && jmpRelAddr >= relAddr && jmpRelAddr < relAddr + relSz);
                if (!alreadyCovered)
                {
                    if (pltRelType == ElfConstants.DT_REL)
                        ApplyRelTable(module, (Elf32_Rel*)jmpRelAddr, jmpRelSz / (uint)sizeof(Elf32_Rel), irelativeQueue);
                    else
                        ApplyRelaTable(module, (Elf32_Rela*)jmpRelAddr, jmpRelSz / (uint)sizeof(Elf32_Rela), irelativeQueue);
                }
            }

            // 4. Flush ARMv7 instruction cache across the entire mapped image before executing anything
            NativeLinux.FlushInstructionCache(module.BaseAddress, module.TotalMappedSize);

            // 5. If there are R_ARM_IRELATIVE resolvers, temporarily mark executable segments RX/RWX so resolvers can run
            if (irelativeQueue.Count > 0)
            {
                ApplySegmentProtections(module, keepWritableForIrelative: true);
                uint hwcap = NativeLinux.GetAuxVal(NativeLinux.AT_HWCAP);
                foreach (var (target, resolver) in irelativeQueue)
                {
                    var fn = Marshal.GetDelegateForFunctionPointer<IfuncResolverDelegate>((IntPtr)(nint)resolver);
                    *(uint*)target = fn(hwcap);
                }
            }

            // 6. Apply final PT_LOAD segment permissions (R-X for .text, R-- for RELRO, RW- for .data/.bss)
            ApplySegmentProtections(module, keepWritableForIrelative: false);

            // 7. Final I-cache flush after permissions settle
            NativeLinux.FlushInstructionCache(module.BaseAddress, module.TotalMappedSize);
            module.Relocated = true;
        }

        private void ApplyRelTable(
            MappedElfModule module,
            Elf32_Rel* table,
            uint count,
            List<(uint TargetAddr, uint ResolverAddr)> irelativeQueue)
        {
            for (uint i = 0; i < count; i++)
            {
                Elf32_Rel rel = table[i];
                uint type = rel.Type;
                if (type == ElfConstants.R_ARM_NONE)
                    continue;

                uint targetAddr = module.LoadBias + rel.r_offset;
                uint implicitAddend = *(uint*)targetAddr; // In-place addend on ARM32 DT_REL

                ApplySingleRelocation(module, targetAddr, type, rel.Sym, implicitAddend, irelativeQueue);
            }
        }

        private void ApplyRelaTable(
            MappedElfModule module,
            Elf32_Rela* table,
            uint count,
            List<(uint TargetAddr, uint ResolverAddr)> irelativeQueue)
        {
            for (uint i = 0; i < count; i++)
            {
                Elf32_Rela rela = table[i];
                uint type = rela.Type;
                if (type == ElfConstants.R_ARM_NONE)
                    continue;

                uint targetAddr = module.LoadBias + rela.r_offset;
                ApplySingleRelocation(module, targetAddr, type, rela.Sym, (uint)rela.r_addend, irelativeQueue);
            }
        }

        private void ApplySingleRelocation(
            MappedElfModule module,
            uint targetAddr,
            uint type,
            uint symIdx,
            uint addend,
            List<(uint TargetAddr, uint ResolverAddr)> irelativeQueue)
        {
            switch (type)
            {
                case ElfConstants.R_ARM_RELATIVE:
                    *(uint*)targetAddr = module.LoadBias + addend;
                    break;

                case ElfConstants.R_ARM_GLOB_DAT:
                case ElfConstants.R_ARM_JUMP_SLOT:
                {
                    uint symVal = ResolveSymbolForRelocation(module, symIdx, out _);
                    *(uint*)targetAddr = symVal;
                    break;
                }

                case ElfConstants.R_ARM_ABS32:
                {
                    uint symVal = ResolveSymbolForRelocation(module, symIdx, out _);
                    // Preserve Thumb bit if symVal has bit 0 set and addend is 0
                    *(uint*)targetAddr = symVal + addend;
                    break;
                }

                case ElfConstants.R_ARM_IRELATIVE:
                {
                    uint resolverAddr = module.LoadBias + addend;
                    irelativeQueue.Add((targetAddr, resolverAddr));
                    break;
                }

                case ElfConstants.R_ARM_TLS_DTPMOD32:
                {
                    if (symIdx == 0)
                    {
                        *(uint*)targetAddr = module.TlsModuleId;
                    }
                    else
                    {
                        ResolveSymbolForRelocation(module, symIdx, out MappedElfModule? definingMod);
                        *(uint*)targetAddr = definingMod?.TlsModuleId ?? module.TlsModuleId;
                    }
                    break;
                }

                case ElfConstants.R_ARM_TLS_DTPOFF32:
                {
                    if (symIdx == 0)
                    {
                        *(uint*)targetAddr = addend;
                    }
                    else
                    {
                        Elf32_Sym sym = module.SymTab[symIdx];
                        *(uint*)targetAddr = sym.st_value + addend;
                    }
                    break;
                }

                default:
                    throw new NotSupportedException(
                        $"Unsupported ARM32 relocation type {type} at offset 0x{(targetAddr - module.LoadBias):X} in {module.FileName}");
            }
        }

        private uint ResolveSymbolForRelocation(MappedElfModule module, uint symIdx, out MappedElfModule? definingModule)
        {
            definingModule = null;
            if (symIdx == 0)
                return 0;

            Elf32_Sym sym = module.SymTab[symIdx];
            string symName = Marshal.PtrToStringAnsi((IntPtr)(module.StrTab + sym.st_name)) ?? "";

            // 1. Check interposed runtime hooks first (dlopen, dlsym, dladdr, __tls_get_addr, __gnu_Unwind_Find_exidx)
            if (_interposedSymbols.TryGetValue(symName, out IntPtr hookPtr))
            {
                // Hot-path hooks: prefer pure-native versions (no CoreCLR 6 re-entry from DALi threads)
                if (_useNativeHooks && symName == "__gnu_Unwind_Find_exidx" && _nativeExidxPtr != IntPtr.Zero)
                    return (uint)_nativeExidxPtr;
                if (_useNativeHooks && symName == "__tls_get_addr" && _nativeTlsPtr != IntPtr.Zero)
                    return (uint)_nativeTlsPtr;
                return (uint)hookPtr;
            }

            // 2. Local symbol defined inside the same module with STB_LOCAL
            if (sym.st_shndx != ElfConstants.SHN_UNDEF && sym.Bind == ElfConstants.STB_LOCAL)
            {
                definingModule = module;
                return module.LoadBias + sym.st_value;
            }

            // 3. Search our manually mapped .NET modules in load order (skip failed-init modules)
            uint mappedExport = ResolveExportFromMappedModules(symName, out definingModule);
            if (definingModule != null)
                return mappedExport;

            // 4. If defined in the current module (e.g. non-exported or protected), use it
            if (sym.st_shndx != ElfConstants.SHN_UNDEF)
            {
                definingModule = module;
                return module.LoadBias + sym.st_value;
            }

            // 5. Search isolated system library handles (libc, libpthread, libm, libdl, libgcc_s, etc.)
            foreach (IntPtr sysHandle in _systemLibHandles)
            {
                IntPtr found = NativeLinux.DlSymString(sysHandle, symName);
                if (found != IntPtr.Zero)
                {
                    // hardfp runtime -> softfp system libm: bridge float/double ABI
                    if (AbiTables.Libm.TryGetValue(symName, out string libmEnc))
                    {
                        IntPtr tramp = _abiTramps.GetOrCreateHardToSoft(found, libmEnc);
                        if (tramp != IntPtr.Zero) return (uint)tramp;
                    }
                    return (uint)found;
                }
            }

            // 6. Weak undefined symbols (e.g. __gmon_start__, _ITM_registerTMCloneTable) lawfully resolve to 0
            if (sym.Bind == ElfConstants.STB_WEAK)
            {
                return 0;
            }

            throw new EntryPointNotFoundException(
                $"Unresolved required symbol '{symName}' in {module.FileName}");
        }

        private static void ApplySegmentProtections(MappedElfModule module, bool keepWritableForIrelative)
        {
            foreach (Elf32_Phdr seg in module.LoadSegments)
            {
                uint segStart = AlignDown(module.LoadBias + seg.p_vaddr, PageSize);
                uint segEnd = AlignUp(module.LoadBias + seg.p_vaddr + seg.p_memsz, PageSize);
                uint segLen = segEnd - segStart;

                int prot = NativeLinux.PROT_NONE;
                if ((seg.p_flags & ElfConstants.PF_R) != 0) prot |= NativeLinux.PROT_READ;
                if ((seg.p_flags & ElfConstants.PF_W) != 0 || keepWritableForIrelative) prot |= NativeLinux.PROT_WRITE;
                if ((seg.p_flags & ElfConstants.PF_X) != 0) prot |= NativeLinux.PROT_EXEC;

                int rc = NativeLinux.Mprotect((IntPtr)(nint)segStart, segLen, prot);
                if (rc != 0)
                {
                    throw new InvalidOperationException(
                        $"mprotect(0x{segStart:X8}, {segLen}, 0x{prot:X}) failed for {module.FileName}: errno={Marshal.GetLastWin32Error()}");
                }
            }
        }

        public void RunModuleInitializers(MappedElfModule module)
        {
            if (!EnsureModuleInitialized(module))
            {
                throw module.InitException
                    ?? new InvalidOperationException(module.Relocated
                        ? $"Initialization failed for {module.FileName}."
                        : $"Cannot initialize unrelocated module {module.FileName}.");
            }
        }

        internal uint ResolveExportFromMappedModules(string symName, out MappedElfModule? definingModule)
        {
            MappedElfModule[] snapshot = _moduleSnapshot;
            foreach (MappedElfModule candidate in snapshot)
            {
                if (candidate.InitException != null)
                    continue;
                if (candidate.ExportedSymbols.TryGetValue(symName, out uint exportedAddr))
                {
                    definingModule = candidate;
                    return exportedAddr;
                }
            }
            definingModule = null;
            return 0;
        }

        private void ExecuteNativeInitializers(MappedElfModule module)
        {
            if (module.DtInit != 0)
            {
                uint initAddr = ToMemoryAddr(module, module.DtInit);
                InvokeConstructor(initAddr);
            }

            if (module.DtInitArray != 0 && module.DtInitArraySz > 0)
            {
                uint* array = (uint*)ToMemoryAddr(module, module.DtInitArray);
                uint count = module.DtInitArraySz / 4;
                for (uint i = 0; i < count; i++)
                {
                    uint fnAddr = array[i];
                    if (fnAddr == 0 || fnAddr == 0xFFFFFFFF)
                        continue;
                    InvokeConstructor(fnAddr);
                }
            }
        }

        private static void InvokeConstructor(uint fnAddr)
        {
            var fn = Marshal.GetDelegateForFunctionPointer<InitFunctionDelegate>((IntPtr)(nint)fnAddr);
            fn(0, IntPtr.Zero, IntPtr.Zero);
        }

        public IntPtr GetExportAddress(MappedElfModule module, string symbolName)
        {
            if (module.ExportedSymbols.TryGetValue(symbolName, out uint addr))
            {
                // Resolve STT_GNU_IFUNC if needed
                return (IntPtr)(nint)addr;
            }
            return IntPtr.Zero;
        }

        // ---------------------------------------------------------------------
        // Step 4: Patch libgcc_s.so.1's GOT for __gnu_Unwind_Find_exidx
        // ---------------------------------------------------------------------
        public void InstallArmExidxUnwindHook()
        {
            DlIteratePhdrCallback cb = (info, size, data) =>
            {
                if (info->dlpi_name == IntPtr.Zero)
                    return 0;

                string name = Marshal.PtrToStringAnsi(info->dlpi_name) ?? "";
                if (!name.Contains("libgcc_s.so"))
                    return 0;

                PatchModuleGotEntry(info, "__gnu_Unwind_Find_exidx", _interposedSymbols["__gnu_Unwind_Find_exidx"]);
                return 0;
            };

            IntPtr cbPtr = Marshal.GetFunctionPointerForDelegate(cb);
            NativeLinux.DlIteratePhdr(cbPtr, IntPtr.Zero);
            GC.KeepAlive(cb);
        }

        private static void PatchModuleGotEntry(NativeLinux.DlPhdrInfo* info, string targetSymName, IntPtr replacement)
        {
            uint loadBias = info->dlpi_addr;
            Elf32_Dyn* dyn = null;

            for (int i = 0; i < info->dlpi_phnum; i++)
            {
                if (info->dlpi_phdr[i].p_type == ElfConstants.PT_DYNAMIC)
                {
                    dyn = (Elf32_Dyn*)(loadBias + info->dlpi_phdr[i].p_vaddr);
                    break;
                }
            }
            if (dyn == null) return;

            byte* strTab = null;
            Elf32_Sym* symTab = null;
            Elf32_Rel* jmpRel = null;
            uint jmpRelSz = 0;

            for (Elf32_Dyn* d = dyn; d->d_tag != ElfConstants.DT_NULL; d++)
            {
                uint ptr = d->d_val < loadBias ? loadBias + d->d_val : d->d_val;
                switch (d->d_tag)
                {
                    case ElfConstants.DT_STRTAB:   strTab = (byte*)ptr; break;
                    case ElfConstants.DT_SYMTAB:   symTab = (Elf32_Sym*)ptr; break;
                    case ElfConstants.DT_JMPREL:   jmpRel = (Elf32_Rel*)ptr; break;
                    case ElfConstants.DT_PLTRELSZ: jmpRelSz = d->d_val; break;
                }
            }

            if (strTab == null || symTab == null || jmpRel == null)
                return;

            uint count = jmpRelSz / (uint)sizeof(Elf32_Rel);
            for (uint i = 0; i < count; i++)
            {
                uint symIdx = jmpRel[i].Sym;
                string name = Marshal.PtrToStringAnsi((IntPtr)(strTab + symTab[symIdx].st_name)) ?? "";
                if (name == targetSymName)
                {
                    uint slotAddr = loadBias + jmpRel[i].r_offset;
                    uint page = AlignDown(slotAddr, PageSize);
                    NativeLinux.Mprotect((IntPtr)(nint)page, PageSize, NativeLinux.PROT_READ | NativeLinux.PROT_WRITE);
                    *(uint*)slotAddr = (uint)replacement;
                    Console.WriteLine($"[Loader] Hooked {targetSymName} in libgcc_s.so.1 GOT at 0x{slotAddr:X8}");
                    break;
                }
            }
        }

        // ---------------------------------------------------------------------
        // Step 5: Interposed Runtime Callbacks (dlopen/dlsym/dladdr/TLS/Exidx)
        // ---------------------------------------------------------------------
        private IntPtr HookedDlOpen(byte* filenamePtr, int flags)
        {
            try
            {
                if (filenamePtr == null)
                    return NativeLinux.DlOpen(null, flags);

                string rawName = Marshal.PtrToStringUTF8((IntPtr)filenamePtr) ?? "";
                IntPtr local = ResolveOrStageDynamicLibrary(rawName, out bool handledLocally);
                if (handledLocally)
                    return local;

                return NativeLinux.DlOpen(filenamePtr, flags);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Loader] Unhandled exception in HookedDlOpen ({ex.GetType().Name}: {ex.Message}); returning null");
                return IntPtr.Zero;
            }
        }

        internal IntPtr ResolveOrStageDynamicLibrary(string rawName, out bool handledLocally, Action<MappedElfModule>? initAction = null)
        {
            handledLocally = false;
            string baseName = Path.GetFileName(rawName);
            if (!baseName.Contains(".so"))
                baseName += ".so";

            MappedElfModule? existing = null;
            MappedElfModule? staged = null;

            lock (_moduleLock)
            {
                if (_modulesByName.TryGetValue(baseName, out MappedElfModule? mod))
                {
                    handledLocally = true;
                    if (!mod.Relocated)
                    {
                        Console.Error.WriteLine($"[Loader] dlopen('{baseName}') rejected: module is mapped but not relocated.");
                        return IntPtr.Zero;
                    }
                    existing = mod;
                }
                else
                {
                    // Lazy-load from our .NET runtime directory if it exists there.
                    // Optional tracing provider needs lttng (absent on the TV); coreclr
                    // tolerates its absence, so skip it instead of failing relocation.
                    string candidatePath = Path.Combine(_dotnet8Dir, baseName);
                    if (File.Exists(candidatePath))
                    {
                        handledLocally = true;
                        if (baseName.Contains("traceptprovider"))
                        {
                            Console.WriteLine($"[Loader] Skipping optional {baseName} (lttng not present)");
                            return IntPtr.Zero;
                        }
                        try
                        {
                            staged = MapLibraryUnpublishedLocked(candidatePath, baseName);
                            RelocateAndProtectLocked(staged);
                            CommitDynamicModuleLocked(staged);
                        }
                        catch (Exception ex)
                        {
                            if (staged != null && !staged.Published)
                                DisposeUnpublishedModule(staged);
                            Console.Error.WriteLine($"[Loader] dlopen('{baseName}') failed before publish ({ex.GetType().Name}: {ex.Message}); returning null");
                            return IntPtr.Zero;
                        }
                    }
                }
            }

            MappedElfModule? target = existing ?? staged;
            if (target != null)
            {
                if (staged != null)
                    Console.WriteLine($"[Loader] Mapped {staged.FileName} at 0x{staged.BaseAddress.ToInt64():X8} (size: {staged.TotalMappedSize} bytes, exports: {staged.ExportedSymbols.Count})");

                // Run module initializers outside _moduleLock so constructors can spawn threads
                // that call dlopen/dladdr on other libraries without deadlocking; per-module
                // InitLock handles concurrent callers and same-thread cyclic dlopen.
                if (!EnsureModuleInitialized(target, initAction))
                    return IntPtr.Zero;
                return target.SyntheticHandle;
            }

            return IntPtr.Zero;
        }

        private IntPtr HookedDlSym(IntPtr handle, byte* symbolPtr)
        {
            try
            {
                string symName = Marshal.PtrToStringAnsi((IntPtr)symbolPtr) ?? "";
                IntPtr local = ResolveRuntimeSymbol(handle, symName, out bool handledLocally);
                if (handledLocally)
                    return local;

                IntPtr real = NativeLinux.DlSym(handle, symbolPtr);
                // TizenFX P/Invokes into softfp system libs (DALi binder etc.): bridge float ABI
                if (real != IntPtr.Zero &&
                    (AbiTables.TizenFx.TryGetValue(symName, out string enc) || AbiTables.Libm.TryGetValue(symName, out enc)))
                {
                    IntPtr tramp = _abiTramps.GetOrCreateHardToSoft(real, enc);
                    if (tramp != IntPtr.Zero) return tramp;
                }
                return real;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Loader] Unhandled exception in HookedDlSym ({ex.GetType().Name}: {ex.Message}); returning null");
                return IntPtr.Zero;
            }
        }

        private int HookedDlClose(IntPtr handle)
        {
            try
            {
                lock (_moduleLock)
                {
                    if (_modulesByHandle.ContainsKey(handle))
                        return 0;
                }
                return NativeLinux.DlClose(handle);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Loader] Unhandled exception in HookedDlClose ({ex.GetType().Name}: {ex.Message})");
                return -1;
            }
        }

        private IntPtr HookedDlError()
        {
            return NativeLinux.DlError();
        }

        private int HookedDlAddr(IntPtr addr, NativeLinux.DlInfo* info)
        {
            try
            {
                uint uaddr = NativeHooks.ToArm32Address(addr) & ~1u; // Clear Thumb bit for range comparison
                MappedElfModule[] snapshot = _moduleSnapshot;
                for (int i = 0; i < snapshot.Length; i++)
                {
                    MappedElfModule mod = snapshot[i];
                    if (mod.ContainsAddress(uaddr))
                    {
                        if (info != null)
                        {
                            info->dli_fname = mod.FullPathCStr;
                            info->dli_fbase = mod.BaseAddress;
                            info->dli_sname = IntPtr.Zero;
                            info->dli_saddr = IntPtr.Zero;
                        }
                        return 1;
                    }
                }
                return NativeLinux.DlAddr(addr, info);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Loader] Unhandled exception in HookedDlAddr ({ex.GetType().Name}: {ex.Message})");
                return 0;
            }
        }

        internal IntPtr ResolveRuntimeSymbol(IntPtr handle, string symName, out bool handledLocally)
        {
            handledLocally = false;
            lock (_moduleLock)
            {
                if (_modulesByHandle.TryGetValue(handle, out MappedElfModule? mod))
                {
                    handledLocally = true;
                    if (!mod.Relocated || mod.InitException != null)
                        return IntPtr.Zero;
                    return GetExportAddress(mod, symName);
                }
            }
            if (symName == "dl_iterate_phdr" && _nativeDlIteratePhdrPtr != IntPtr.Zero)
            {
                handledLocally = true;
                return _nativeDlIteratePhdrPtr;
            }
            return IntPtr.Zero;
        }

        internal bool EnsureModuleInitialized(MappedElfModule module, Action<MappedElfModule>? initAction = null)
        {
            lock (module.InitLock)
            {
                if (module.InitException != null)
                    return false;
                if (module.Initialized)
                    return true;
                if (!module.Relocated)
                    return false;
                if (module.Initializing && module.InitThreadId == Environment.CurrentManagedThreadId)
                    return true; // Same-thread cyclic dlopen returns the in-progress handle.

                module.Initializing = true;
                module.InitThreadId = Environment.CurrentManagedThreadId;
                try
                {
                    (initAction ?? ExecuteNativeInitializers)(module);
                    module.Initialized = true;
                    return true;
                }
                catch (Exception ex)
                {
                    module.InitException = ex;
                    Console.Error.WriteLine($"[Loader] Initializer failed for {module.FileName}: {ex.GetType().Name}: {ex.Message}");
                    return false;
                }
                finally
                {
                    module.Initializing = false;
                    module.InitThreadId = 0;
                }
            }
        }

        // Install pure-native dl_iterate_phdr interposition so manually mapped ELF modules
        // (including modules mapped later via dlopen) expose their full ELF program headers
        // (PT_LOAD, PT_DYNAMIC, PT_ARM_EXIDX, PT_GNU_EH_FRAME, PT_TLS) to CoreCLR libunwind.
        public void InstallDlIteratePhdrHook()
        {
            lock (_moduleLock)
            {
                if (_dlIteratePhdrInstalled)
                    return;

                IntPtr realIter = IntPtr.Zero;
                foreach (IntPtr h in _systemLibHandles)
                {
                    if (realIter == IntPtr.Zero)
                        realIter = NativeLinux.DlSymString(h, "dl_iterate_phdr");
                }
                if (realIter == IntPtr.Zero)
                    throw new InvalidOperationException("dl_iterate_phdr not found");

                _nativeHooks ??= new NativeHooks();
                _nativeDlIteratePhdrPtr = _nativeHooks.EmitDlIteratePhdr(realIter);
                _interposedSymbols["dl_iterate_phdr"] = _nativeDlIteratePhdrPtr;
                _dlIteratePhdrInstalled = true;

                foreach (MappedElfModule m in _moduleList)
                {
                    AddModuleToPhdrTableLocked(m);
                }

                Console.WriteLine($"[Loader] dl_iterate_phdr native hook installed at 0x{NativeHooks.ToArm32Address(_nativeDlIteratePhdrPtr):X8} for {_moduleList.Count} mapped modules (real 0x{NativeHooks.ToArm32Address(realIter):X8})");
            }
        }

        private void AddModuleToPhdrTableLocked(MappedElfModule m)
        {
            if (_nativeHooks == null || m.ProgramHeadersPtr == IntPtr.Zero)
                return;
            _nativeHooks.AddPhdrModule(
                m.LoadBias,
                NativeHooks.ToArm32Address(m.FullPathCStr),
                NativeHooks.ToArm32Address(m.ProgramHeadersPtr),
                m.ProgramHeaderCount);
        }

        private void EnsureCapacityForDynamicModuleLocked(MappedElfModule m)
        {
            if (_nativeHooks == null)
                return;
            if (_dlIteratePhdrInstalled && _nativeHooks.HasPhdrTable && !_nativeHooks.CanAddPhdrModule())
                throw new InvalidOperationException($"dl_iterate_phdr table full (max {NativeHooks.MaxPhdrModules}) for {m.FileName}.");
            if (_useNativeHooks && m.ExidxSegment.HasValue && _nativeHooks.HasExidxTable && !_nativeHooks.CanAddExidxEntry())
                throw new InvalidOperationException($"native EXIDX table full (max {NativeHooks.MaxExidxEntries - 1}) for {m.FileName}.");
            if (_useNativeHooks && m.TlsMemSize > 0 && _nativeHooks.HasTlsTable &&
                !_nativeHooks.CanSetTlsDesc(m.TlsModuleId, Math.Max(m.TlsMemSize, 16u)))
                throw new InvalidOperationException($"native TLS table full (moduleId=0x{m.TlsModuleId:X}, max {_nativeHooks.TlsMaxModules}) for {m.FileName}.");
        }

        internal void CommitDynamicModuleLocked(MappedElfModule m)
        {
            // Phase 1 (pure validation & address conversion — no shared state mutated yet):
            EnsureCapacityForDynamicModuleLocked(m);

            bool registerExidx = _useNativeHooks && _nativeHooks != null && m.ExidxSegment.HasValue;
            uint exidxStart = 0, exidxEnd = 0, exidxAddr = 0;
            int exidxCount = 0;
            if (registerExidx)
            {
                Elf32_Phdr ex = m.ExidxSegment!.Value;
                exidxStart = NativeHooks.ToArm32Address(m.BaseAddress);
                exidxEnd = exidxStart + m.TotalMappedSize;
                exidxAddr = m.LoadBias + ex.p_vaddr;
                exidxCount = (int)(ex.p_memsz / 8);
            }

            bool registerTls = _useNativeHooks && _nativeHooks != null && m.TlsMemSize > 0;
            uint tlsMemsz = 0, tlsFilesz = 0, tlsTdata32 = 0;
            if (registerTls)
            {
                tlsMemsz = Math.Max(m.TlsMemSize, 16u);
                tlsFilesz = m.TlsFileSize;
                tlsTdata32 = m.TlsTemplate == IntPtr.Zero ? 0u : NativeHooks.ToArm32Address(m.TlsTemplate);
            }

            bool registerPhdr = _dlIteratePhdrInstalled && _nativeHooks != null && m.ProgramHeadersPtr != IntPtr.Zero;
            uint phdrName32 = 0, phdrPtr32 = 0;
            if (registerPhdr)
            {
                phdrName32 = NativeHooks.ToArm32Address(m.FullPathCStr);
                phdrPtr32 = NativeHooks.ToArm32Address(m.ProgramHeadersPtr);
            }

            // Phase 2 (atomic commit — all checks passed; once published, memory is never freed):
            PublishModuleLocked(m);
            if (registerExidx)
                _nativeHooks!.AddExidxEntry(exidxStart, exidxEnd, exidxAddr, exidxCount);
            if (registerTls)
                _nativeHooks!.SetTlsDesc(m.TlsModuleId, tlsMemsz, tlsFilesz, tlsTdata32);
            if (registerPhdr)
                _nativeHooks!.AddPhdrModule(m.LoadBias, phdrName32, phdrPtr32, m.ProgramHeaderCount);
        }

        internal void ConfigureHooksForTest(NativeHooks hooks, bool useNativeHooks, bool dlIterateInstalled)
        {
            lock (_moduleLock)
            {
                _nativeHooks = hooks;
                _useNativeHooks = useNativeHooks;
                _dlIteratePhdrInstalled = dlIterateInstalled;
            }
        }

        internal void PublishModuleForTest(MappedElfModule module)
        {
            lock (_moduleLock)
            {
                PublishModuleLocked(module);
            }
        }

        internal void CommitDynamicModuleForTest(MappedElfModule module)
        {
            lock (_moduleLock)
            {
                CommitDynamicModuleLocked(module);
            }
        }

        internal bool IsModulePublishedForTest(MappedElfModule module)
        {
            lock (_moduleLock)
            {
                return _modulesByName.ContainsKey(module.FileName)
                    || _modulesByHandle.ContainsKey(module.SyntheticHandle)
                    || _moduleList.Contains(module)
                    || Array.IndexOf(_moduleSnapshot, module) >= 0;
            }
        }

        // Build pure-native exidx + TLS hooks from the mapped modules and switch the
        // interposition table over. Called after relocation, before initializers.
        public void InstallNativeHotHooks()
        {
            _nativeHooks ??= new NativeHooks();

            var exEntries = new List<(uint textStart, uint textEnd, uint exidxStart, int count)>();
            var tlsDescs = new List<(uint memsz, uint filesz, IntPtr tdata)>();
            uint baseId = 0x4000;
            foreach (MappedElfModule mod in _moduleList)
            {
                if (mod.ExidxSegment.HasValue)
                {
                    Elf32_Phdr ex = mod.ExidxSegment.Value;
                    // text range: use the whole mapped image (conservative, matches ContainsAddress)
                    exEntries.Add((NativeHooks.ToArm32Address(mod.BaseAddress),
                        NativeHooks.ToArm32Address(mod.BaseAddress) + mod.TotalMappedSize,
                        mod.LoadBias + ex.p_vaddr, (int)(ex.p_memsz / 8)));
                }
                if (mod.TlsMemSize > 0 && mod.TlsModuleId < baseId)
                {
                    baseId = mod.TlsModuleId;
                }
            }
            // TLS desc table indexed by (moduleId - baseId); ids are assigned sequentially at map time
            int maxModules = 128;
            for (int slot = 0; slot < maxModules; slot++)
            {
                uint id = baseId + (uint)slot;
                if (_modulesByTlsId.TryGetValue(id, out MappedElfModule mod) && mod.TlsMemSize > 0)
                    tlsDescs.Add((Math.Max(mod.TlsMemSize, 16u), mod.TlsFileSize, mod.TlsTemplate));
                else
                    tlsDescs.Add((0, 0, IntPtr.Zero));
            }

            // Resolve REAL native addresses via dlsym on the isolated system handles.
            // Managed delegate stubs would re-enter CoreCLR 6 and defeat the purpose.
            IntPtr realExidx = IntPtr.Zero, realTls = IntPtr.Zero;
            IntPtr freeP = IntPtr.Zero, callocP = IntPtr.Zero, memcpyP = IntPtr.Zero;
            IntPtr getP = IntPtr.Zero, setP = IntPtr.Zero;
            foreach (IntPtr h in _systemLibHandles)
            {
                if (realExidx == IntPtr.Zero) realExidx = NativeLinux.DlSymString(h, "__gnu_Unwind_Find_exidx");
                if (realTls == IntPtr.Zero) realTls = NativeLinux.DlSymString(h, "__tls_get_addr");
                if (freeP == IntPtr.Zero) freeP = NativeLinux.DlSymString(h, "free");
                if (callocP == IntPtr.Zero) callocP = NativeLinux.DlSymString(h, "calloc");
                if (memcpyP == IntPtr.Zero) memcpyP = NativeLinux.DlSymString(h, "memcpy");
                if (getP == IntPtr.Zero) getP = NativeLinux.DlSymString(h, "pthread_getspecific");
                if (setP == IntPtr.Zero) setP = NativeLinux.DlSymString(h, "pthread_setspecific");
            }
            if (realExidx == IntPtr.Zero || realTls == IntPtr.Zero || freeP == IntPtr.Zero ||
                callocP == IntPtr.Zero || memcpyP == IntPtr.Zero || getP == IntPtr.Zero || setP == IntPtr.Zero)
                throw new InvalidOperationException("native hook helper symbols not found");

            _nativeExidxPtr = _nativeHooks.EmitExidxFinder(exEntries, realExidx);
            _nativeTlsPtr = _nativeHooks.EmitTlsGetAddr(baseId, maxModules, tlsDescs, realTls, getP, setP, callocP, memcpyP, freeP);
            _useNativeHooks = true;

            // Re-patch libgcc_s GOT so system-lib unwinding also uses the native finder
            InstallArmExidxUnwindHookNative();
        }

        private void InstallArmExidxUnwindHookNative()
        {
            DlIteratePhdrCallback cb = (info, size, data) =>
            {
                if (info->dlpi_name == IntPtr.Zero) return 0;
                string name = Marshal.PtrToStringAnsi(info->dlpi_name) ?? "";
                if (!name.Contains("libgcc_s.so")) return 0;
                PatchModuleGotEntry(info, "__gnu_Unwind_Find_exidx", _nativeExidxPtr);
                return 0;
            };
            IntPtr cbPtr = Marshal.GetFunctionPointerForDelegate(cb);
            NativeLinux.DlIteratePhdr(cbPtr, IntPtr.Zero);
            GC.KeepAlive(cb);
        }

        private IntPtr HookedTlsGetAddr(TlsIndex* ti)
        {
            nuint threadId = NativeLinux.PthreadSelf();
            var key = (threadId, ti->ModuleId);

            if (!_tlsBlocks.TryGetValue(key, out IntPtr block))
            {
                MappedElfModule? mod;
                lock (_moduleLock)
                {
                    if (!_modulesByTlsId.TryGetValue(ti->ModuleId, out mod))
                        throw new InvalidOperationException($"Unknown TLS module ID 0x{ti->ModuleId:X}");
                }

                uint size = Math.Max(mod.TlsMemSize, 16u);
                block = Marshal.AllocHGlobal((int)size);
                new Span<byte>((void*)block, (int)size).Clear();

                if (mod.TlsFileSize > 0 && mod.TlsTemplate != IntPtr.Zero)
                {
                    Buffer.MemoryCopy((void*)mod.TlsTemplate, (void*)block, size, mod.TlsFileSize);
                }
                _tlsBlocks[key] = block;
            }

            return (IntPtr)(nint)((uint)block + ti->Offset);
        }

        private IntPtr HookedUnwindFindExidx(uint pc, int* pcount)
        {
            uint cleanPc = pc & ~1u;
            MappedElfModule[] snapshot = _moduleSnapshot;
            for (int i = 0; i < snapshot.Length; i++)
            {
                MappedElfModule mod = snapshot[i];
                if (mod.ContainsAddress(cleanPc) && mod.ExidxSegment.HasValue)
                {
                    Elf32_Phdr exidx = mod.ExidxSegment.Value;
                    if (pcount != null)
                        *pcount = (int)(exidx.p_memsz / 8);
                    return (IntPtr)(nint)(mod.LoadBias + exidx.p_vaddr);
                }
            }
            return NativeLinux.RealUnwindFindExidx(pc, pcount);
        }
    }

    // =========================================================================
    // 5. PoC & CoreCLR 8 Runner Entry Point
    // =========================================================================
    internal static unsafe class Program
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetPidDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ulong GetTimestampDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte** GetEnvironDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int CoreClrInitializeDelegate(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string exePath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string appDomainFriendlyName,
            int propertyCount,
            IntPtr propertyKeys,
            IntPtr propertyValues,
            out IntPtr hostHandle,
            out uint domainId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int CoreClrExecuteAssemblyDelegate(
            IntPtr hostHandle,
            uint domainId,
            int argc,
            IntPtr argv,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string managedAssemblyPath,
            out uint exitCode);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ErrorWriterDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string message);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SetErrorWriterDelegate(IntPtr writer);

        private static ErrorWriterDelegate _errWriterDel;

        public static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("  PoC Test:     dotnet Arm32ManualMapper.dll --poc <path-to-dotnet8-runtime-dir>");
                Console.WriteLine("  Run .NET 8:   dotnet Arm32ManualMapper.dll --run <path-to-dotnet8-runtime-dir> <path-to-net8-app.dll>");
                return 1;
            }

            string mode = args[0];
            string dotnet8Dir = Path.GetFullPath(args[1]);
            var loader = new Arm32ElfLoader(dotnet8Dir);

            if (mode == "--poc")
            {
                RunSystemNativePoC(loader, dotnet8Dir);
                return 0;
            }
            else if (mode == "--run" && args.Length >= 3)
            {
                string appDll = Path.GetFullPath(args[2]);
                return RunDotNet8App(loader, dotnet8Dir, appDll);
            }

            Console.Error.WriteLine("Invalid arguments.");
            return 1;
        }

        private static void RunSystemNativePoC(Arm32ElfLoader loader, string dotnet8Dir)
        {
            string libPath = Path.Combine(dotnet8Dir, "libSystem.Native.so");
            MappedElfModule mod = loader.MapLibrary(libPath);
            loader.RelocateAndProtect(mod, runInitializers: true);

            // 1. Test SystemNative_GetPid
            IntPtr getPidPtr = loader.GetExportAddress(mod, "SystemNative_GetPid");
            var getPid = Marshal.GetDelegateForFunctionPointer<GetPidDelegate>(getPidPtr);
            int pid = getPid();
            Console.WriteLine($"[PoC] SystemNative_GetPid()       => {pid} (Expected: {Environment.ProcessId})");

            // 2. Test SystemNative_GetTimestamp
            IntPtr getTsPtr = loader.GetExportAddress(mod, "SystemNative_GetTimestamp");
            var getTs = Marshal.GetDelegateForFunctionPointer<GetTimestampDelegate>(getTsPtr);
            ulong ts1 = getTs();
            ulong ts2 = getTs();
            Console.WriteLine($"[PoC] SystemNative_GetTimestamp() => {ts1} ns, delta={ts2 - ts1} ns");

            // 3. Test SystemNative_GetEnviron
            IntPtr getEnvPtr = loader.GetExportAddress(mod, "SystemNative_GetEnviron");
            var getEnv = Marshal.GetDelegateForFunctionPointer<GetEnvironDelegate>(getEnvPtr);
            byte** env = getEnv();
            Console.WriteLine("[PoC] First 3 environment entries via SystemNative_GetEnviron():");
            for (int i = 0; i < 3 && env != null && env[i] != null; i++)
            {
                Console.WriteLine($"      [{i}] {Marshal.PtrToStringUTF8((IntPtr)env[i])}");
            }
        }

        private static int RunDotNet8App(Arm32ElfLoader loader, string dotnet8Dir, string appDllPath)
        {
            string ver = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            Console.WriteLine($"[Loader] Arm32ManualMapper v{ver}");

            // CRITICAL: Disable ReadyToRun so CoreCLR maps .dll files with PROT_READ only
            // and JIT-compiles all IL into anonymous executable memory.
            Environment.SetEnvironmentVariable("DOTNET_ReadyToRun", "0");
            Environment.SetEnvironmentVariable("COMPlus_ReadyToRun", "0");
            // Workstation GC: avoid per-core Server GC threads competing with DALi + idle .NET 6 host
            Environment.SetEnvironmentVariable("DOTNET_gcServer", "0");
            Environment.SetEnvironmentVariable("COMPlus_gcServer", "0");

            loader.InstallArmExidxUnwindHook();

            // Pre-map all .NET 8 native libraries before running relocations/initializers
            string[] nativeLibs =
            {
                "libSystem.Native.so",
                "libSystem.Globalization.Native.so",
                "libSystem.IO.Compression.Native.so",
                "libSystem.Security.Cryptography.Native.OpenSsl.so",
                "libclrjit.so",
                "libcoreclr.so"
            };

            var loadedModules = new List<MappedElfModule>();
            foreach (string lib in nativeLibs)
            {
                string fullPath = Path.Combine(dotnet8Dir, lib);
                if (File.Exists(fullPath))
                {
                    loadedModules.Add(loader.MapLibrary(fullPath));
                }
            }

            // Expose all manually mapped modules (and future dlopen'd ones) via dl_iterate_phdr
            // BEFORE relocation so CoreCLR libunwind finds ARM EXIDX/EH tables for mapped frames.
            loader.InstallDlIteratePhdrHook();

            // Install pure-native hot-path hooks (exidx + TLS) BEFORE relocation, so the
            // mapped modules' GOTs bind directly to native code (no CoreCLR 6 re-entry
            // from foreign DALi threads). Opt-out: MECH_NATIVE_HOOKS=0.
            if (Environment.GetEnvironmentVariable("MECH_NATIVE_HOOKS") != "0")
            {
                try
                {
                    loader.InstallNativeHotHooks();
                    Console.WriteLine("[Loader] Native hot hooks installed (exidx + TLS)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[Loader] Native hook install failed, keeping managed hooks: " + ex.Message);
                }
            }

            // Relocate all modules first, then run .init_array in dependency order
            foreach (MappedElfModule m in loadedModules)
            {
                loader.RelocateAndProtect(m, runInitializers: false);
            }
            foreach (MappedElfModule m in loadedModules)
            {
                loader.RunModuleInitializers(m);
            }

            MappedElfModule coreclr = loader.MapLibrary(Path.Combine(dotnet8Dir, "libcoreclr.so"));
            IntPtr initPtr = loader.GetExportAddress(coreclr, "coreclr_initialize");
            IntPtr execPtr = loader.GetExportAddress(coreclr, "coreclr_execute_assembly");

            var coreclrInit = Marshal.GetDelegateForFunctionPointer<CoreClrInitializeDelegate>(initPtr);
            var coreclrExec = Marshal.GetDelegateForFunctionPointer<CoreClrExecuteAssemblyDelegate>(execPtr);

            // Capture coreclr error writer output (helps diagnose init failures).
            IntPtr errWriterPtr = loader.GetExportAddress(coreclr, "coreclr_set_error_writer");
            if (errWriterPtr != IntPtr.Zero)
            {
                _errWriterDel = (string msg) => Console.Error.WriteLine("[CLR-ERR] " + msg);
                var setWriter = Marshal.GetDelegateForFunctionPointer<SetErrorWriterDelegate>(errWriterPtr);
                setWriter(Marshal.GetFunctionPointerForDelegate(_errWriterDel));
            }

            // Build TRUSTED_PLATFORM_ASSEMBLIES list from .NET 8 runtime dir + app dir
            string appDir = Path.GetDirectoryName(appDllPath)!;
            var tpaBuilder = new StringBuilder();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string dir in new[] { appDir, dotnet8Dir })
            {
                foreach (string dll in Directory.GetFiles(dir, "*.dll"))
                {
                    string name = Path.GetFileName(dll);
                    if (seenNames.Add(name))
                    {
                        if (tpaBuilder.Length > 0) tpaBuilder.Append(':');
                        tpaBuilder.Append(Path.GetFullPath(dll));
                    }
                }
            }

            string[] keys =
            {
                "TRUSTED_PLATFORM_ASSEMBLIES",
                "APP_PATHS",
                "NATIVE_DLL_SEARCH_DIRECTORIES"
            };
            string[] values =
            {
                tpaBuilder.ToString(),
                $"{appDir}:{dotnet8Dir}",
                $"{dotnet8Dir}:{appDir}"
            };

            IntPtr* keyPtrs = stackalloc IntPtr[keys.Length];
            IntPtr* valPtrs = stackalloc IntPtr[values.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                keyPtrs[i] = Marshal.StringToCoTaskMemUTF8(keys[i]);
                valPtrs[i] = Marshal.StringToCoTaskMemUTF8(values[i]);
            }

            // Trigger .NET 6 GC before entering .NET 8 so the .NET 6 host stays quiescent
            GC.Collect();
            GC.WaitForPendingFinalizers();

            int hr = coreclrInit(
                appDllPath,
                "DotNet8InMemoryDomain",
                keys.Length,
                (IntPtr)keyPtrs,
                (IntPtr)valPtrs,
                out IntPtr hostHandle,
                out uint domainId);

            if (hr < 0)
            {
                Console.Error.WriteLine($"[Error] coreclr_initialize failed with HRESULT 0x{hr:X8}");
                return hr;
            }

            Console.WriteLine($"[Loader] coreclr_initialize succeeded! DomainId={domainId}");
            Console.WriteLine($"[Loader] ABI trampolines active: {loader.AbiTrampolineCount}");

            hr = coreclrExec(hostHandle, domainId, 0, IntPtr.Zero, appDllPath, out uint exitCode);
            if (hr < 0)
            {
                Console.Error.WriteLine($"[Error] coreclr_execute_assembly failed with HRESULT 0x{hr:X8}");
                return hr;
            }

            return (int)exitCode;
        }
    }
}
