using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// glibc-2.34 ABI shims for running .NET 10 on glibc 2.30 (ARM32 armv7l).
// Layouts/syscall numbers per Argon review (argon4-time64.md). All process-local.
public static unsafe class Glibc234Shims
{
    private const int _STAT_VER_LINUX = 3;   // ARM32 (NOT 0)
    private const int _MKNOD_VER_LINUX = 1;  // ARM32
    private const int __NR_clock_gettime64 = 403;
    private const int __NR_clock_getres_time64 = 406;
    private const int __NR_clock_nanosleep_time64 = 407;
    private const int __NR_utimensat_time64 = 412;

    // glibc 2.30 legacy struct stat64: 104 bytes
    [StructLayout(LayoutKind.Explicit, Size = 104)]
    public struct Stat64Old
    {
        [FieldOffset(0)] public ulong st_dev;
        [FieldOffset(8)] public uint __pad1;
        [FieldOffset(12)] public uint __st_ino;
        [FieldOffset(16)] public uint st_mode;
        [FieldOffset(20)] public uint st_nlink;
        [FieldOffset(24)] public uint st_uid;
        [FieldOffset(28)] public uint st_gid;
        [FieldOffset(32)] public ulong st_rdev;
        [FieldOffset(40)] public uint __pad2;
        [FieldOffset(48)] public long st_size;
        [FieldOffset(56)] public int st_blksize;
        [FieldOffset(64)] public long st_blocks;
        [FieldOffset(72)] public int st_atim_sec;
        [FieldOffset(76)] public int st_atim_nsec;
        [FieldOffset(80)] public int st_mtim_sec;
        [FieldOffset(84)] public int st_mtim_nsec;
        [FieldOffset(88)] public int st_ctim_sec;
        [FieldOffset(92)] public int st_ctim_nsec;
        [FieldOffset(96)] public ulong st_ino;
    }

    // glibc 2.34 struct __stat64_t64: 112 bytes
    [StructLayout(LayoutKind.Explicit, Size = 112)]
    public struct Stat64Time64
    {
        [FieldOffset(0)] public ulong st_dev;
        [FieldOffset(8)] public ulong st_ino;
        [FieldOffset(16)] public uint st_mode;
        [FieldOffset(20)] public uint st_nlink;
        [FieldOffset(24)] public uint st_uid;
        [FieldOffset(28)] public uint st_gid;
        [FieldOffset(32)] public ulong st_rdev;
        [FieldOffset(40)] public long st_size;
        [FieldOffset(48)] public int st_blksize;
        [FieldOffset(52)] public int __pad_blocks;
        [FieldOffset(56)] public long st_blocks;
        [FieldOffset(64)] public long st_atim_sec;
        [FieldOffset(72)] public int st_atim_nsec;
        [FieldOffset(76)] public int st_atim_pad;
        [FieldOffset(80)] public long st_mtim_sec;
        [FieldOffset(88)] public int st_mtim_nsec;
        [FieldOffset(92)] public int st_mtim_pad;
        [FieldOffset(96)] public long st_ctim_sec;
        [FieldOffset(104)] public int st_ctim_nsec;
        [FieldOffset(108)] public int st_ctim_pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Timespec32 { public int tv_sec; public int tv_nsec; }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct Timespec64
    {
        [FieldOffset(0)] public long tv_sec;
        [FieldOffset(8)] public int tv_nsec;
        [FieldOffset(12)] public int __pad;
        [FieldOffset(8)] public long tv_nsec_k64;
    }

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    public struct Timeval64 { public long tv_sec; public long tv_usec; }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct RUsage32
    {
        [FieldOffset(0)] public int ru_utime_sec;
        [FieldOffset(4)] public int ru_utime_usec;
        [FieldOffset(8)] public int ru_stime_sec;
        [FieldOffset(12)] public int ru_stime_usec;
        [FieldOffset(16)] public fixed int counts[14];
    }

    [StructLayout(LayoutKind.Explicit, Size = 88)]
    public struct RUsage64
    {
        [FieldOffset(0)] public long ru_utime_sec;
        [FieldOffset(8)] public long ru_utime_usec;
        [FieldOffset(16)] public long ru_stime_sec;
        [FieldOffset(24)] public long ru_stime_usec;
        [FieldOffset(32)] public fixed int counts[14];
    }

    // ---- glibc 2.30 P/Invokes ----
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern int __xstat64(int ver, byte* path, Stat64Old* buf);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern int __fxstat64(int ver, int fd, Stat64Old* buf);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern int __lxstat64(int ver, byte* path, Stat64Old* buf);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern int __fxstatat64(int ver, int dirfd, byte* path, Stat64Old* buf, int flags);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern int __xmknod(int ver, byte* path, uint mode, ulong* dev);
    [DllImport("libc.so.6", EntryPoint = "syscall", ExactSpelling = true)] private static extern int SyscallClockGettime(int number, int clk, void* tp);
    [DllImport("libc.so.6", EntryPoint = "syscall", ExactSpelling = true)] private static extern int SyscallClockNanosleep(int number, int clockid, int flags, void* req, void* rem);
    [DllImport("libc.so.6", EntryPoint = "syscall", ExactSpelling = true)] private static extern int SyscallUtimensat(int number, int dirfd, void* path, void* times, int flags);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern int getrusage(int who, RUsage32* usage);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern int time(IntPtr tloc);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern int mktime(void* tm);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern int timegm(void* tm);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern void* gmtime_r(int* timep, void* result);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern void* localtime_r(int* timep, void* result);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern int fcntl64(int fd, int cmd, IntPtr arg);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern int ioctl(int fd, uint req, IntPtr arg);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern int prctl(int option, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr a5);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern int getsockopt(int fd, int lvl, int opt, void* val, uint* len);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern int setsockopt(int fd, int lvl, int opt, void* val, uint len);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern nint recvmsg(int fd, void* msg, int flags);
    [DllImport("libc.so.6", ExactSpelling = true)] private static extern nint sendmsg(int fd, void* msg, int flags);
    [DllImport("libpthread.so.0", ExactSpelling = true)] private static extern int pthread_cond_timedwait(void* cond, void* mutex, Timespec32* abstime);
    [DllImport("libstdc++.so.6", EntryPoint = "__cxa_throw_bad_array_new_length", ExactSpelling = true)] private static extern void cxa_throw_bad_array_new_length();

    // ---- stat conversion ----
    private static void ConvertStat(in Stat64Old src, Stat64Time64* dst)
    {
        *dst = default;
        dst->st_dev = src.st_dev;
        dst->st_ino = src.st_ino;
        dst->st_mode = src.st_mode;
        dst->st_nlink = src.st_nlink;
        dst->st_uid = src.st_uid;
        dst->st_gid = src.st_gid;
        dst->st_rdev = src.st_rdev;
        dst->st_size = src.st_size;
        dst->st_blksize = src.st_blksize;
        dst->st_blocks = src.st_blocks;
        dst->st_atim_sec = src.st_atim_sec;
        dst->st_atim_nsec = src.st_atim_nsec;
        dst->st_mtim_sec = src.st_mtim_sec;
        dst->st_mtim_nsec = src.st_mtim_nsec;
        dst->st_ctim_sec = src.st_ctim_sec;
        dst->st_ctim_nsec = src.st_ctim_nsec;
    }

    public static int Stat64Time64Shim(byte* path, Stat64Time64* buf)
    { Stat64Old old; int rc = __xstat64(_STAT_VER_LINUX, path, &old); if (rc == 0 && buf != null) ConvertStat(in old, buf); return rc; }
    public static int Fstat64Time64Shim(int fd, Stat64Time64* buf)
    { Stat64Old old; int rc = __fxstat64(_STAT_VER_LINUX, fd, &old); if (rc == 0 && buf != null) ConvertStat(in old, buf); return rc; }
    public static int Lstat64Time64Shim(byte* path, Stat64Time64* buf)
    { Stat64Old old; int rc = __lxstat64(_STAT_VER_LINUX, path, &old); if (rc == 0 && buf != null) ConvertStat(in old, buf); return rc; }
    public static int Fstatat64Time64Shim(int dirfd, byte* path, Stat64Time64* buf, int flags)
    { Stat64Old old; int rc = __fxstatat64(_STAT_VER_LINUX, dirfd, path, &old, flags); if (rc == 0 && buf != null) ConvertStat(in old, buf); return rc; }

    public static int ClockGettime64Shim(int clkId, Timespec64* tp) => SyscallClockGettime(__NR_clock_gettime64, clkId, tp);
    public static int ClockGetres64Shim(int clkId, Timespec64* res) => SyscallClockGettime(__NR_clock_getres_time64, clkId, res);
    public static int Gettimeofday64Shim(Timeval64* tv, void* tz)
    {
        if (tv == null) return 0;
        Timespec64 ts; int rc = SyscallClockGettime(__NR_clock_gettime64, 0, &ts);
        if (rc == 0) { tv->tv_sec = ts.tv_sec; tv->tv_usec = ts.tv_nsec / 1000L; }
        return rc;
    }
    public static int Nanosleep64Shim(Timespec64* req, Timespec64* rem)
    {
        Timespec64 kreq = default;
        if (req != null) { kreq.tv_sec = req->tv_sec; kreq.tv_nsec_k64 = req->tv_nsec; }
        return SyscallClockNanosleep(__NR_clock_nanosleep_time64, 0, 0, req != null ? &kreq : null, rem);
    }
    public static int Utimensat64Shim(int dirfd, byte* pathname, Timespec64* times, int flags)
    {
        Timespec64* ktimesPtr = null;
        Timespec64* ktimes = stackalloc Timespec64[2];
        if (times != null)
        {
            ktimes[0].tv_sec = times[0].tv_sec; ktimes[0].tv_nsec_k64 = times[0].tv_nsec;
            ktimes[1].tv_sec = times[1].tv_sec; ktimes[1].tv_nsec_k64 = times[1].tv_nsec;
            ktimesPtr = ktimes;
        }
        return SyscallUtimensat(__NR_utimensat_time64, dirfd, pathname, ktimesPtr, flags);
    }
    public static int Futimens64Shim(int fd, Timespec64* times) => Utimensat64Shim(fd, null, times, 0);
    public static int PthreadCondTimedwait64Shim(void* cond, void* mutex, Timespec64* abstime)
    {
        Timespec32 ts32 = default;
        if (abstime != null)
        {
            long s = abstime->tv_sec;
            ts32.tv_sec = (int)Math.Clamp(s, int.MinValue, int.MaxValue);
            ts32.tv_nsec = abstime->tv_nsec;
        }
        return pthread_cond_timedwait(cond, mutex, abstime != null ? &ts32 : null);
    }
    public static int Getrusage64Shim(int who, RUsage64* usage)
    {
        RUsage32 ru32; int rc = getrusage(who, &ru32);
        if (rc == 0 && usage != null)
        {
            usage->ru_utime_sec = ru32.ru_utime_sec; usage->ru_utime_usec = ru32.ru_utime_usec;
            usage->ru_stime_sec = ru32.ru_stime_sec; usage->ru_stime_usec = ru32.ru_stime_usec;
            for (int i = 0; i < 14; i++) usage->counts[i] = ru32.counts[i];
        }
        return rc;
    }
    public static long Time64Shim(long* tloc)
    { long now = time(IntPtr.Zero); if (tloc != null && now != -1) *tloc = now; return now; }
    public static long Mktime64Shim(void* tm) => mktime(tm);
    public static long Timegm64Shim(void* tm) => timegm(tm);
    public static void* Gmtime64RShim(long* timep, void* result) { int t32 = (int)*timep; return gmtime_r(&t32, result); }
    public static void* Localtime64RShim(long* timep, void* result) { int t32 = (int)*timep; return localtime_r(&t32, result); }
    public static int MknodShim(byte* path, uint mode, ulong dev) => __xmknod(_MKNOD_VER_LINUX, path, mode, &dev);
    public static int FcntlTime64Shim(int fd, int cmd, IntPtr arg) => fcntl64(fd, cmd, arg);
    public static int IoctlTime64Shim(int fd, uint req, IntPtr arg) => ioctl(fd, req, arg);
    public static int PrctlTime64Shim(int option, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr a5) => prctl(option, a2, a3, a4, a5);
    public static int Getsockopt64Shim(int fd, int lvl, int opt, void* val, uint* len) => getsockopt(fd, lvl, opt, val, len);
    public static int Setsockopt64Shim(int fd, int lvl, int opt, void* val, uint len) => setsockopt(fd, lvl, opt, val, len);
    public static nint Recvmsg64Shim(int fd, void* msg, int flags) => recvmsg(fd, msg, flags);
    public static nint Sendmsg64Shim(int fd, void* msg, int flags) => sendmsg(fd, msg, flags);
    public static void ThrowBadArrayNewLengthShim() => cxa_throw_bad_array_new_length();

    // ---- delegate types matching native signatures ----
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int StatDel(byte* path, Stat64Time64* buf);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int FstatDel(int fd, Stat64Time64* buf);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int FstatatDel(int dirfd, byte* path, Stat64Time64* buf, int flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int ClockDel(int clk, Timespec64* ts);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int GettimeofdayDel(Timeval64* tv, void* tz);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int NanosleepDel(Timespec64* req, Timespec64* rem);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int UtimensatDel(int dirfd, byte* path, Timespec64* times, int flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int FutimensDel(int fd, Timespec64* times);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int CondwaitDel(void* cond, void* mutex, Timespec64* abstime);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int GetrusageDel(int who, RUsage64* usage);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate long TimeDel(long* tloc);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate long MktimeDel(void* tm);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void* GmtimeRDel(long* timep, void* result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int MknodDel(byte* path, uint mode, ulong dev);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int FcntlDel(int fd, int cmd, IntPtr arg);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int IoctlDel(int fd, uint req, IntPtr arg);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int PrctlDel(int option, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr a5);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int GetsockoptDel(int fd, int lvl, int opt, void* val, uint* len);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int SetsockoptDel(int fd, int lvl, int opt, void* val, uint len);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate nint MsgDel(int fd, void* msg, int flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void BadArrDel();

    private static readonly List<Delegate> _keep = new();
    private static IntPtr _singleThreaded = IntPtr.Zero;

    private static IntPtr Reg(Delegate d) { _keep.Add(d); return Marshal.GetFunctionPointerForDelegate(d); }

    public static Dictionary<string, IntPtr> BuildAll()
    {
        var d = new Dictionary<string, IntPtr>();
        d["__stat64_time64"] = Reg(new StatDel(Stat64Time64Shim));
        d["__fstat64_time64"] = Reg(new FstatDel(Fstat64Time64Shim));
        d["__lstat64_time64"] = Reg(new StatDel(Lstat64Time64Shim));
        d["__fstatat64_time64"] = Reg(new FstatatDel(Fstatat64Time64Shim));
        d["__clock_gettime64"] = Reg(new ClockDel(ClockGettime64Shim));
        d["__clock_getres64"] = Reg(new ClockDel(ClockGetres64Shim));
        d["__gettimeofday64"] = Reg(new GettimeofdayDel(Gettimeofday64Shim));
        d["__nanosleep64"] = Reg(new NanosleepDel(Nanosleep64Shim));
        d["__utimensat64"] = Reg(new UtimensatDel(Utimensat64Shim));
        d["__futimens64"] = Reg(new FutimensDel(Futimens64Shim));
        d["__pthread_cond_timedwait64"] = Reg(new CondwaitDel(PthreadCondTimedwait64Shim));
        d["__getrusage64"] = Reg(new GetrusageDel(Getrusage64Shim));
        d["__time64"] = Reg(new TimeDel(Time64Shim));
        d["__mktime64"] = Reg(new MktimeDel(Mktime64Shim));
        d["__timegm64"] = Reg(new MktimeDel(Timegm64Shim));
        d["__gmtime64_r"] = Reg(new GmtimeRDel(Gmtime64RShim));
        d["__localtime64_r"] = Reg(new GmtimeRDel(Localtime64RShim));
        d["mknod"] = Reg(new MknodDel(MknodShim));
        d["__fcntl_time64"] = Reg(new FcntlDel(FcntlTime64Shim));
        d["__ioctl_time64"] = Reg(new IoctlDel(IoctlTime64Shim));
        d["__prctl_time64"] = Reg(new PrctlDel(PrctlTime64Shim));
        d["__getsockopt64"] = Reg(new GetsockoptDel(Getsockopt64Shim));
        d["__setsockopt64"] = Reg(new SetsockoptDel(Setsockopt64Shim));
        d["__recvmsg64"] = Reg(new MsgDel(Recvmsg64Shim));
        d["__sendmsg64"] = Reg(new MsgDel(Sendmsg64Shim));
        d["_ZSt28__throw_bad_array_new_lengthv"] = Reg(new BadArrDel(ThrowBadArrayNewLengthShim));
        if (_singleThreaded == IntPtr.Zero) { _singleThreaded = Marshal.AllocHGlobal(1); *(byte*)_singleThreaded = 0; }
        d["__libc_single_threaded"] = _singleThreaded;
        return d;
    }
}