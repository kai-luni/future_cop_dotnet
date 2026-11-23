#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FcMainMenu;

/// <summary>Linux evdev event types, codes, bitmap sizes and errno values.</summary>
public static class EvdevCodes
{
    public const ushort EvSyn = 0x00;
    public const ushort EvKey = 0x01;
    public const ushort EvRel = 0x02;
    public const ushort EvAbs = 0x03;

    public const ushort SynReport = 0;
    public const ushort SynConfig = 1;
    public const ushort SynMtReport = 2;
    public const ushort SynDropped = 3;

    public const int AbsX = 0x00;
    public const int AbsY = 0x01;
    public const int AbsZ = 0x02;
    public const int AbsRx = 0x03;
    public const int AbsRy = 0x04;
    public const int AbsRz = 0x05;
    public const int AbsWheel = 0x08;
    public const int AbsHat0X = 0x10;
    public const int AbsHat0Y = 0x11;
    public const int AbsMax = 0x3F;

    // HID joystick usage Button 1..12 range (used by 0079:0011 DragonRise / generic USB pads).
    public const ushort BtnTrigger = 0x120;
    public const ushort BtnThumb = 0x121;
    public const ushort BtnThumb2 = 0x122;
    public const ushort BtnTop = 0x123;
    public const ushort BtnTop2 = 0x124;
    public const ushort BtnPinkie = 0x125;
    public const ushort BtnBase = 0x126;
    public const ushort BtnBase2 = 0x127;
    public const ushort BtnBase3 = 0x128;
    public const ushort BtnBase4 = 0x129;
    public const ushort BtnBase5 = 0x12A;
    public const ushort BtnBase6 = 0x12B;
    public const ushort BtnDead = 0x12F;

    // Standard Linux gamepad range.
    public const ushort BtnGamepad = 0x130;
    public const ushort BtnSouth = 0x130;
    public const ushort BtnEast = 0x131;
    public const ushort BtnC = 0x132;
    public const ushort BtnNorth = 0x133;
    public const ushort BtnWest = 0x134;
    public const ushort BtnZ = 0x135;
    public const ushort BtnTl = 0x136;
    public const ushort BtnTr = 0x137;
    public const ushort BtnTl2 = 0x138;
    public const ushort BtnTr2 = 0x139;
    public const ushort BtnSelect = 0x13A;
    public const ushort BtnStart = 0x13B;
    public const ushort BtnMode = 0x13C;
    public const ushort BtnThumbL = 0x13D;
    public const ushort BtnThumbR = 0x13E;

    public const ushort BtnDpadUp = 0x220;
    public const ushort BtnDpadDown = 0x221;
    public const ushort BtnDpadLeft = 0x222;
    public const ushort BtnDpadRight = 0x223;

    public const int KeyMax = 0x2FF;
    public const int EvBitmapBytes = 8;
    public const int KeyBitmapBytes = (KeyMax + 8) / 8; // 96 bytes
    public const int AbsBitmapBytes = (AbsMax + 8) / 8; // 8 bytes

    public const int ErrNoEnt = 2;
    public const int ErrIntr = 4;
    public const int ErrAgain = 11;
    public const int ErrAcces = 13;
    public const int ErrNoDev = 19;
}

/// <summary>Linux &lt;linux/input.h&gt; ioctl request numbers using &lt;asm-generic/ioctl.h&gt;.</summary>
public static class EvdevIoctl
{
    private const uint IocRead = 2u;
    private const uint EvType = (uint)'E';

    private static uint Ior(uint nr, int size) =>
        (IocRead << 30) | (((uint)size & 0x3FFFu) << 16) | (EvType << 8) | (nr & 0xFFu);

    public const uint EviocgVersion = 0x80044501u;
    public const uint EviocgId = 0x80084502u;

    public static uint EviocgName(int length) => Ior(0x06u, length);
    public static uint EviocgKey(int length) => Ior(0x18u, length);
    public static uint EviocgBit(int eventType, int length) => Ior((uint)(0x20 + eventType), length);
    public static uint EviocgAbs(int axis) => Ior((uint)(0x40 + axis), 24);
}

/// <summary>Decoded &lt;linux/input.h&gt; struct input_id (8 bytes).</summary>
public readonly record struct EvdevInputId(ushort BusType, ushort Vendor, ushort Product, ushort Version);

/// <summary>Decoded &lt;linux/input.h&gt; struct input_absinfo (24 bytes).</summary>
public readonly record struct EvdevAbsInfo(int Value, int Minimum, int Maximum, int Fuzz, int Flat, int Resolution);

/// <summary>Decoded &lt;linux/input.h&gt; struct input_event (16 bytes on ARM32 ILP32, 24 bytes on LP64).</summary>
public readonly record struct EvdevEvent(uint SecondsLow, uint MicrosecondsLow, ushort Type, ushort Code, int Value)
{
    public static int NativeEventSize => IntPtr.Size == 4 ? 16 : 24;

    public static bool TryParse(ReadOnlySpan<byte> bytes, int eventSize, out EvdevEvent ev)
    {
        if (eventSize == 16 && bytes.Length >= 16)
        {
            uint sec = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0, 4));
            uint usec = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4));
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(8, 2));
            ushort code = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(10, 2));
            int value = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(12, 4));
            ev = new EvdevEvent(sec, usec, type, code, value);
            return true;
        }
        if (eventSize == 24 && bytes.Length >= 24)
        {
            uint sec = (uint)BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(0, 8));
            uint usec = (uint)BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8, 8));
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(16, 2));
            ushort code = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(18, 2));
            int value = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(20, 4));
            ev = new EvdevEvent(sec, usec, type, code, value);
            return true;
        }
        ev = default;
        return false;
    }
}

/// <summary>Low-level evdev OS boundary, abstracted for deterministic host testing.</summary>
public interface IEvdevSystem
{
    IReadOnlyList<string> EnumerateEventDevices();
    int OpenNonBlocking(string path, out int errno);
    void Close(int fd);
    int Read(int fd, Span<byte> destination, out int errno);
    bool TryGetId(int fd, out EvdevInputId id, out int errno);
    bool TryGetName(int fd, out string name, out int errno);
    bool TryGetBitmask(int fd, int eventType, Span<byte> destination, out int errno);
    bool TryGetKeyStates(int fd, Span<byte> destination, out int errno);
    bool TryGetAbsInfo(int fd, int axis, out EvdevAbsInfo info, out int errno);
}

/// <summary>Real Linux libc non-blocking evdev implementation (never calls EVIOCGRAB).</summary>
public sealed class LinuxEvdevSystem : IEvdevSystem
{
    private const int ORdOnly = 0;
    private const int ONonBlock = 0x800;
    private const int OCloExec = 0x80000;

    private readonly byte[] _readScratch = new byte[4096];
    private readonly byte[] _ioctlScratch = new byte[256];

    [DllImport("libc", EntryPoint = "open", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    private static extern int SysOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string pathname, int flags);

    [DllImport("libc", EntryPoint = "close", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    private static extern int SysClose(int fd);

    [DllImport("libc", EntryPoint = "read", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    private static extern nint SysRead(int fd, [Out] byte[] buf, nuint count);

    [DllImport("libc", EntryPoint = "ioctl", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    private static extern int SysIoctlBuffer(int fd, nuint request, [Out] byte[] buffer);

    public IReadOnlyList<string> EnumerateEventDevices()
    {
        try
        {
            if (!Directory.Exists("/dev/input")) return Array.Empty<string>();
            var candidates = new List<(int Number, string Path)>();
            foreach (string path in Directory.EnumerateFiles("/dev/input", "event*"))
            {
                string fileName = Path.GetFileName(path);
                if (fileName.Length > 5 && int.TryParse(fileName.AsSpan(5), out int number) && number >= 0)
                    candidates.Add((number, path));
            }
            candidates.Sort(static (a, b) => a.Number.CompareTo(b.Number));
            var result = new string[candidates.Count];
            for (int i = 0; i < candidates.Count; i++) result[i] = candidates[i].Path;
            return result;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public int OpenNonBlocking(string path, out int errno)
    {
        int fd = SysOpen(path, ORdOnly | ONonBlock | OCloExec);
        errno = fd < 0 ? Marshal.GetLastPInvokeError() : 0;
        return fd;
    }

    public void Close(int fd)
    {
        if (fd >= 0) SysClose(fd);
    }

    public int Read(int fd, Span<byte> destination, out int errno)
    {
        int maxCount = Math.Min(destination.Length, _readScratch.Length);
        if (maxCount <= 0) { errno = 0; return 0; }
        nint n = SysRead(fd, _readScratch, (nuint)maxCount);
        if (n < 0)
        {
            errno = Marshal.GetLastPInvokeError();
            return -1;
        }
        int count = (int)n;
        _readScratch.AsSpan(0, count).CopyTo(destination);
        errno = 0;
        return count;
    }

    public bool TryGetId(int fd, out EvdevInputId id, out int errno)
    {
        Array.Clear(_ioctlScratch, 0, 8);
        int rc = SysIoctlBuffer(fd, EvdevIoctl.EviocgId, _ioctlScratch);
        if (rc < 0)
        {
            errno = Marshal.GetLastPInvokeError();
            id = default;
            return false;
        }
        id = new EvdevInputId(
            BinaryPrimitives.ReadUInt16LittleEndian(_ioctlScratch.AsSpan(0, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(_ioctlScratch.AsSpan(2, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(_ioctlScratch.AsSpan(4, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(_ioctlScratch.AsSpan(6, 2)));
        errno = 0;
        return true;
    }

    public bool TryGetName(int fd, out string name, out int errno)
    {
        Array.Clear(_ioctlScratch, 0, _ioctlScratch.Length);
        int rc = SysIoctlBuffer(fd, EvdevIoctl.EviocgName(_ioctlScratch.Length), _ioctlScratch);
        if (rc < 0)
        {
            errno = Marshal.GetLastPInvokeError();
            name = string.Empty;
            return false;
        }
        int maxLen = Math.Min(rc, _ioctlScratch.Length);
        int nul = Array.IndexOf(_ioctlScratch, (byte)0, 0, maxLen);
        int len = nul >= 0 ? nul : maxLen;
        name = Encoding.UTF8.GetString(_ioctlScratch, 0, len);
        errno = 0;
        return true;
    }

    public bool TryGetBitmask(int fd, int eventType, Span<byte> destination, out int errno)
    {
        int len = Math.Min(destination.Length, _ioctlScratch.Length);
        Array.Clear(_ioctlScratch, 0, len);
        int rc = SysIoctlBuffer(fd, EvdevIoctl.EviocgBit(eventType, len), _ioctlScratch);
        if (rc < 0)
        {
            errno = Marshal.GetLastPInvokeError();
            destination.Clear();
            return false;
        }
        _ioctlScratch.AsSpan(0, len).CopyTo(destination);
        errno = 0;
        return true;
    }

    public bool TryGetKeyStates(int fd, Span<byte> destination, out int errno)
    {
        int len = Math.Min(destination.Length, _ioctlScratch.Length);
        Array.Clear(_ioctlScratch, 0, len);
        int rc = SysIoctlBuffer(fd, EvdevIoctl.EviocgKey(len), _ioctlScratch);
        if (rc < 0)
        {
            errno = Marshal.GetLastPInvokeError();
            destination.Clear();
            return false;
        }
        _ioctlScratch.AsSpan(0, len).CopyTo(destination);
        errno = 0;
        return true;
    }

    public bool TryGetAbsInfo(int fd, int axis, out EvdevAbsInfo info, out int errno)
    {
        Array.Clear(_ioctlScratch, 0, 24);
        int rc = SysIoctlBuffer(fd, EvdevIoctl.EviocgAbs(axis), _ioctlScratch);
        if (rc < 0)
        {
            errno = Marshal.GetLastPInvokeError();
            info = default;
            return false;
        }
        info = new EvdevAbsInfo(
            BinaryPrimitives.ReadInt32LittleEndian(_ioctlScratch.AsSpan(0, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(_ioctlScratch.AsSpan(4, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(_ioctlScratch.AsSpan(8, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(_ioctlScratch.AsSpan(12, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(_ioctlScratch.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(_ioctlScratch.AsSpan(20, 4)));
        errno = 0;
        return true;
    }
}
