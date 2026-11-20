using System.Buffers.Binary;
using FcMainMenu;
using FutureCop.Assets;
using FutureCop.Game.Mech;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Unit and integration tests for the Linux evdev gamepad adapter, capability-based axis/button
/// mapping, ABI decoding, SYN_REPORT atomicity, SYN_DROPPED resync, partial reads, disconnect/
/// reconnect, focus lifecycle, and TV remote NUI coexistence through <see cref="TvLevelControl"/>.
/// </summary>
public class EvdevGamepadTests
{
    private sealed class FakeClock
    {
        public const long Frequency = 1_000_000;
        public long Now = 10_000_000;
        public void Ms(double ms) => Now += (long)Math.Round(ms * Frequency / 1000.0);
    }

    private sealed class FakeEvdevDevice
    {
        public string Path { get; init; } = "/dev/input/event23";
        public EvdevInputId Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public byte[] EvBits { get; init; } = new byte[EvdevCodes.EvBitmapBytes];
        public byte[] KeyBits { get; init; } = new byte[EvdevCodes.KeyBitmapBytes];
        public byte[] AbsBits { get; init; } = new byte[EvdevCodes.AbsBitmapBytes];
        public Dictionary<int, EvdevAbsInfo> AbsInfos { get; init; } = new();
        public byte[] LiveKeyStates { get; } = new byte[EvdevCodes.KeyBitmapBytes];
        public Queue<ReadResult> ReadQueue { get; } = new();
        public int OpenErrno { get; set; }
        public bool FailKeyRequery { get; set; }
        public bool FailAbsRequery { get; set; }
        public int OpenCount { get; set; }
        public int CloseCount { get; set; }
        public bool IsOpen { get; set; }

        public void SetLiveKey(int keyCode, bool pressed)
        {
            int byteIndex = keyCode >> 3;
            int bitMask = 1 << (keyCode & 7);
            if (pressed) LiveKeyStates[byteIndex] |= (byte)bitMask;
            else LiveKeyStates[byteIndex] &= (byte)~bitMask;
        }

        public void SetLiveAbsValue(int axis, int value)
        {
            if (AbsInfos.TryGetValue(axis, out var info))
                AbsInfos[axis] = info with { Value = value };
        }

        public void EnqueueBytes(byte[] bytes) => ReadQueue.Enqueue(new ReadResult(bytes, 0));
        public void EnqueueError(int errno) => ReadQueue.Enqueue(new ReadResult(Array.Empty<byte>(), errno));
        public void EnqueueEof() => ReadQueue.Enqueue(new ReadResult(Array.Empty<byte>(), 0));
    }

    private readonly record struct ReadResult(byte[] Data, int Errno);

    private sealed class FakeEvdevSystem : IEvdevSystem
    {
        private readonly Dictionary<int, (FakeEvdevDevice Device, byte[]? Pending, int Offset)> _openFds = new();
        private int _nextFd = 10;

        public List<FakeEvdevDevice> Devices { get; } = new();
        public Exception? ThrowOnRead { get; set; }

        public IReadOnlyList<string> EnumerateEventDevices() => Devices.Select(d => d.Path).ToArray();

        public int OpenNonBlocking(string path, out int errno)
        {
            var dev = Devices.FirstOrDefault(d => d.Path == path);
            if (dev == null) { errno = EvdevCodes.ErrNoEnt; return -1; }
            if (dev.OpenErrno != 0) { errno = dev.OpenErrno; return -1; }
            int fd = _nextFd++;
            dev.OpenCount++;
            dev.IsOpen = true;
            _openFds[fd] = (dev, null, 0);
            errno = 0;
            return fd;
        }

        public void Close(int fd)
        {
            if (_openFds.Remove(fd, out var entry))
                entry.Device.IsOpen = false;
            if (entry.Device != null) entry.Device.CloseCount++;
        }

        public int Read(int fd, Span<byte> destination, out int errno)
        {
            if (ThrowOnRead != null) throw ThrowOnRead;
            if (!_openFds.TryGetValue(fd, out var entry)) { errno = EvdevCodes.ErrNoDev; return -1; }
            var dev = entry.Device;
            if (entry.Pending == null || entry.Offset >= entry.Pending.Length)
            {
                if (dev.ReadQueue.Count == 0) { errno = EvdevCodes.ErrAgain; return -1; }
                var next = dev.ReadQueue.Dequeue();
                if (next.Errno != 0) { errno = next.Errno; return -1; }
                if (next.Data.Length == 0) { errno = 0; return 0; }
                entry = (dev, next.Data, 0);
            }

            int avail = entry.Pending!.Length - entry.Offset;
            int toCopy = Math.Min(avail, destination.Length);
            entry.Pending.AsSpan(entry.Offset, toCopy).CopyTo(destination);
            int newOffset = entry.Offset + toCopy;
            _openFds[fd] = newOffset >= entry.Pending.Length ? (dev, null, 0) : (dev, entry.Pending, newOffset);
            errno = 0;
            return toCopy;
        }

        public bool TryGetId(int fd, out EvdevInputId id, out int errno)
        {
            if (!_openFds.TryGetValue(fd, out var entry)) { id = default; errno = EvdevCodes.ErrNoDev; return false; }
            id = entry.Device.Id;
            errno = 0;
            return true;
        }

        public bool TryGetName(int fd, out string name, out int errno)
        {
            if (!_openFds.TryGetValue(fd, out var entry)) { name = string.Empty; errno = EvdevCodes.ErrNoDev; return false; }
            name = entry.Device.Name;
            errno = 0;
            return true;
        }

        public bool TryGetBitmask(int fd, int eventType, Span<byte> destination, out int errno)
        {
            if (!_openFds.TryGetValue(fd, out var entry)) { errno = EvdevCodes.ErrNoDev; return false; }
            byte[] src = eventType switch
            {
                0 => entry.Device.EvBits,
                EvdevCodes.EvKey => entry.Device.KeyBits,
                EvdevCodes.EvAbs => entry.Device.AbsBits,
                _ => Array.Empty<byte>(),
            };
            destination.Clear();
            src.AsSpan(0, Math.Min(src.Length, destination.Length)).CopyTo(destination);
            errno = 0;
            return true;
        }

        public bool TryGetKeyStates(int fd, Span<byte> destination, out int errno)
        {
            if (!_openFds.TryGetValue(fd, out var entry)) { errno = EvdevCodes.ErrNoDev; return false; }
            if (entry.Device.FailKeyRequery) { errno = EvdevCodes.ErrNoDev; return false; }
            destination.Clear();
            entry.Device.LiveKeyStates.AsSpan(0, Math.Min(entry.Device.LiveKeyStates.Length, destination.Length)).CopyTo(destination);
            errno = 0;
            return true;
        }

        public bool TryGetAbsInfo(int fd, int axis, out EvdevAbsInfo info, out int errno)
        {
            if (!_openFds.TryGetValue(fd, out var entry)) { info = default; errno = EvdevCodes.ErrNoDev; return false; }
            if (entry.Device.FailAbsRequery || !entry.Device.AbsInfos.TryGetValue(axis, out info))
            {
                info = default;
                errno = EvdevCodes.ErrNoDev;
                return false;
            }
            errno = 0;
            return true;
        }
    }

    private static void SetBit(byte[] bits, int code) => bits[code >> 3] |= (byte)(1 << (code & 7));

    /// <summary>Builds a device matching the physical TV evidence: SWITCH CO.,LTD. USB Gamepad (0079:0011).</summary>
    private static FakeEvdevDevice CreateSwitchUsbGamepad(string path = "/dev/input/event23", ushort buttonBase = EvdevCodes.BtnTrigger)
    {
        var dev = new FakeEvdevDevice
        {
            Path = path,
            Id = new EvdevInputId(BusType: 0x0003, Vendor: 0x0079, Product: 0x0011, Version: 0x0110),
            Name = "SWITCH CO.,LTD. USB Gamepad ",
        };
        SetBit(dev.EvBits, EvdevCodes.EvSyn);
        SetBit(dev.EvBits, EvdevCodes.EvKey);
        SetBit(dev.EvBits, EvdevCodes.EvAbs);
        for (int i = 0; i < 12; i++) SetBit(dev.KeyBits, buttonBase + i);
        SetBit(dev.AbsBits, EvdevCodes.AbsX);
        SetBit(dev.AbsBits, EvdevCodes.AbsY);
        SetBit(dev.AbsBits, EvdevCodes.AbsWheel);
        dev.AbsInfos[EvdevCodes.AbsX] = new EvdevAbsInfo(128, 0, 255, 0, 15, 0);
        dev.AbsInfos[EvdevCodes.AbsY] = new EvdevAbsInfo(128, 0, 255, 0, 15, 0);
        dev.AbsInfos[EvdevCodes.AbsWheel] = new EvdevAbsInfo(128, 0, 255, 0, 15, 0);
        return dev;
    }

    /// <summary>Builds a conventional dual-analog + hat + BTN_GAMEPAD controller.</summary>
    private static FakeEvdevDevice CreateConventionalGamepad(string path = "/dev/input/event7")
    {
        var dev = new FakeEvdevDevice
        {
            Path = path,
            Id = new EvdevInputId(BusType: 0x0003, Vendor: 0x045E, Product: 0x028E, Version: 0x0110),
            Name = "Generic X-Box pad",
        };
        SetBit(dev.EvBits, EvdevCodes.EvSyn);
        SetBit(dev.EvBits, EvdevCodes.EvKey);
        SetBit(dev.EvBits, EvdevCodes.EvAbs);
        foreach (int code in new[]
        {
            EvdevCodes.BtnSouth, EvdevCodes.BtnEast, EvdevCodes.BtnNorth, EvdevCodes.BtnWest,
            EvdevCodes.BtnTl, EvdevCodes.BtnTr, EvdevCodes.BtnTl2, EvdevCodes.BtnTr2,
            EvdevCodes.BtnSelect, EvdevCodes.BtnStart, EvdevCodes.BtnThumbL, EvdevCodes.BtnThumbR,
        })
        {
            SetBit(dev.KeyBits, code);
        }
        SetBit(dev.AbsBits, EvdevCodes.AbsX);
        SetBit(dev.AbsBits, EvdevCodes.AbsY);
        SetBit(dev.AbsBits, EvdevCodes.AbsRx);
        SetBit(dev.AbsBits, EvdevCodes.AbsRy);
        SetBit(dev.AbsBits, EvdevCodes.AbsHat0X);
        SetBit(dev.AbsBits, EvdevCodes.AbsHat0Y);
        dev.AbsInfos[EvdevCodes.AbsX] = new EvdevAbsInfo(0, -32768, 32767, 16, 128, 0);
        dev.AbsInfos[EvdevCodes.AbsY] = new EvdevAbsInfo(0, -32768, 32767, 16, 128, 0);
        dev.AbsInfos[EvdevCodes.AbsRx] = new EvdevAbsInfo(0, -32768, 32767, 16, 128, 0);
        dev.AbsInfos[EvdevCodes.AbsRy] = new EvdevAbsInfo(0, -32768, 32767, 16, 128, 0);
        dev.AbsInfos[EvdevCodes.AbsHat0X] = new EvdevAbsInfo(0, -1, 1, 0, 0, 0);
        dev.AbsInfos[EvdevCodes.AbsHat0Y] = new EvdevAbsInfo(0, -1, 1, 0, 0, 0);
        return dev;
    }

    private static FakeEvdevDevice CreateTvRemoteOrKeyboard(string path = "/dev/input/event1")
    {
        var dev = new FakeEvdevDevice
        {
            Path = path,
            Id = new EvdevInputId(BusType: 0x0019, Vendor: 0x04E8, Product: 0x0001, Version: 0x0001),
            Name = "Samsung Smart Remote",
        };
        SetBit(dev.EvBits, EvdevCodes.EvSyn);
        SetBit(dev.EvBits, EvdevCodes.EvKey);
        foreach (int key in new[] { 28, 103, 105, 106, 108, 116, 158 }) SetBit(dev.KeyBits, key);
        return dev;
    }

    /// <summary>Encodes an independent raw Linux ARM32 16-byte struct input_event packet.</summary>
    private static byte[] EncodeArm32Events(params (ushort Type, ushort Code, int Value)[] events)
    {
        var bytes = new byte[events.Length * 16];
        for (int i = 0; i < events.Length; i++)
        {
            int o = i * 16;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(o, 4), 1700000000u + (uint)i);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(o + 4, 4), (uint)(i * 1000));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(o + 8, 2), events[i].Type);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(o + 10, 2), events[i].Code);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 12, 4), events[i].Value);
        }
        return bytes;
    }

    /// <summary>Encodes an independent raw Linux 64-bit 24-byte struct input_event packet.</summary>
    private static byte[] Encode64BitEvents(params (ushort Type, ushort Code, int Value)[] events)
    {
        var bytes = new byte[events.Length * 24];
        for (int i = 0; i < events.Length; i++)
        {
            int o = i * 24;
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(o, 8), 1700000000ul + (ulong)i);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(o + 8, 8), (ulong)(i * 1000));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(o + 16, 2), events[i].Type);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(o + 18, 2), events[i].Code);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 20, 4), events[i].Value);
        }
        return bytes;
    }

    private static string? FrontEndPath()
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc == null ? null : Path.Combine(disc, "CW", "FE.MIS");
        if (path == null || !File.Exists(path))
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
            return null;
        }
        return path;
    }

    private static (TvLevelControl Tv, FakeClock Clock) NewTvWithGamepad(string fe, EvdevGamepadAdapter gamepad, Action<string>? log = null)
    {
        string pa = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(fe))!, "PA");
        var clock = new FakeClock();
        var flow = TvLevelControl.CreateFlow(fe, pa, TextWriter.Null, TextWriter.Null);
        return (new TvLevelControl(flow, () => clock.Now, FakeClock.Frequency, gamepad, log), clock);
    }

    private static void ClickRemote(TvLevelControl tv, string name)
    {
        tv.Key(TvKeyState.Down, name, 0);
        tv.Key(TvKeyState.Up, name, 0);
    }

    private static void ToLevelViaRemote(TvLevelControl tv, FakeClock clock)
    {
        ClickRemote(tv, "Down");
        ClickRemote(tv, "Down");
        for (int i = 0; i < 5; i++) { ClickRemote(tv, "Return"); RunTicks(tv, clock, 1); }
        Assert.Equal(MenuScreen.Scene, tv.Flow.Screen);
        Assert.NotNull(tv.Flow.Level);
    }

    private static void RunTicks(TvLevelControl tv, FakeClock clock, int ticks, double ms = 16)
    {
        for (int i = 0; i < ticks; i++)
        {
            clock.Ms(ms);
            tv.Tick();
        }
    }

    [Fact]
    public void LinuxIoctlConstantsMatchAsmGenericEncoding()
    {
        // _IOR('E', nr, size) = (2 << 30) | (size << 16) | ('E' << 8) | nr
        Assert.Equal(0x80044501u, EvdevIoctl.EviocgVersion);
        Assert.Equal(0x80084502u, EvdevIoctl.EviocgId);
        Assert.Equal(0x81004506u, EvdevIoctl.EviocgName(256));
        Assert.Equal(0x80604518u, EvdevIoctl.EviocgKey(96));
        Assert.Equal(0x80084520u, EvdevIoctl.EviocgBit(0, 8));
        Assert.Equal(0x80604521u, EvdevIoctl.EviocgBit(EvdevCodes.EvKey, 96));
        Assert.Equal(0x80084523u, EvdevIoctl.EviocgBit(EvdevCodes.EvAbs, 8));
        Assert.Equal(0x80184540u, EvdevIoctl.EviocgAbs(0));
        Assert.Equal(0x80184541u, EvdevIoctl.EviocgAbs(1));
        Assert.Equal(0x80184550u, EvdevIoctl.EviocgAbs(EvdevCodes.AbsHat0X));
    }

    [Fact]
    public void Arm32And64BitRawInputEventsDecodeCorrectly()
    {
        byte[] arm32 = new byte[]
        {
            0x01, 0x00, 0x00, 0x00, // tv_sec = 1
            0x40, 0x42, 0x0F, 0x00, // tv_usec = 1000000
            0x03, 0x00,             // type = EV_ABS (3)
            0x01, 0x00,             // code = ABS_Y (1)
            0x00, 0x00, 0x00, 0x00, // value = 0
        };
        Assert.True(EvdevEvent.TryParse(arm32, 16, out var e32));
        Assert.Equal(EvdevCodes.EvAbs, e32.Type);
        Assert.Equal(EvdevCodes.AbsY, e32.Code);
        Assert.Equal(0, e32.Value);

        byte[] lp64 = new byte[]
        {
            0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // tv_sec = 1
            0x40, 0x42, 0x0F, 0x00, 0x00, 0x00, 0x00, 0x00, // tv_usec = 1000000
            0x01, 0x00,                                     // type = EV_KEY (1)
            0x20, 0x01,                                     // code = BTN_TRIGGER (0x120)
            0x01, 0x00, 0x00, 0x00,                         // value = 1
        };
        Assert.True(EvdevEvent.TryParse(lp64, 24, out var e64));
        Assert.Equal(EvdevCodes.EvKey, e64.Type);
        Assert.Equal(EvdevCodes.BtnTrigger, e64.Code);
        Assert.Equal(1, e64.Value);
    }

    [Fact]
    public void DiscoveryFindsSwitchGamepadAtDynamicEventPathAndClosesNonMatches()
    {
        var sys = new FakeEvdevSystem();
        var inaccessible = CreateTvRemoteOrKeyboard("/dev/input/event0");
        inaccessible.OpenErrno = EvdevCodes.ErrAcces;
        var remote = CreateTvRemoteOrKeyboard("/dev/input/event1");
        var gamepad = CreateSwitchUsbGamepad("/dev/input/event5");
        sys.Devices.Add(inaccessible);
        sys.Devices.Add(remote);
        sys.Devices.Add(gamepad);

        var clock = new FakeClock();
        var logs = new List<string>();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, logs.Add, eventSize: 16);

        var result = adapter.Poll();
        Assert.True(result.Snapshot.Connected);
        Assert.Equal("/dev/input/event5", result.Snapshot.DevicePath);
        Assert.Equal("SWITCH CO.,LTD. USB Gamepad ", result.Snapshot.DeviceName);
        Assert.Equal((ushort)0x0079, result.Snapshot.Vendor);
        Assert.Equal((ushort)0x0011, result.Snapshot.Product);
        Assert.False(remote.IsOpen);
        Assert.Equal(1, remote.CloseCount);
        Assert.True(gamepad.IsOpen);
        Assert.Contains(logs, l => l.Contains("0079:0011") && l.Contains("/dev/input/event5"));
    }

    [Fact]
    public void SwitchGamepadSimultaneousUpAndLeftPreservesBothDirectionsAndIndependentRelease()
    {
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad("/dev/input/event23");
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);

        Assert.True(adapter.Poll().Snapshot.Connected);

        // Forward (ABS_Y = 0)
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsY, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        var r1 = adapter.Poll();
        Assert.Equal(PadButtons.Up, r1.Snapshot.Buttons);
        Assert.Equal(AnalogPad.Digital, r1.Snapshot.AnalogPad);

        // Left while Forward stays held (ABS_X = 0)
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsX, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        var r2 = adapter.Poll();
        Assert.Equal(PadButtons.Up | PadButtons.Left, r2.Snapshot.Buttons);

        // Release Forward (ABS_Y = 128) while Left remains held
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsY, 128), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        var r3 = adapter.Poll();
        Assert.Equal(PadButtons.Left, r3.Snapshot.Buttons);
        Assert.Equal(PadButtons.Up, r3.ReleasedDirections);

        // Release Left (ABS_X = 128)
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsX, 128), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        var r4 = adapter.Poll();
        Assert.Equal(PadButtons.None, r4.Snapshot.Buttons);
        Assert.Equal(PadButtons.Left, r4.ReleasedDirections);
    }

    [Fact]
    public void SwitchGamepadDeadzoneAndIgnoredAxis8WorkWithoutJitter()
    {
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        adapter.Poll();

        // Small jitter around neutral 128 and axis 8 (ABS_WHEEL) deflection must stay neutral.
        gamepad.EnqueueBytes(EncodeArm32Events(
            (EvdevCodes.EvAbs, EvdevCodes.AbsX, 145),
            (EvdevCodes.EvAbs, EvdevCodes.AbsY, 110),
            (EvdevCodes.EvAbs, EvdevCodes.AbsWheel, 0),
            (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        var r1 = adapter.Poll();
        Assert.Equal(PadButtons.None, r1.Snapshot.Buttons);

        // Down + Right full deflection
        gamepad.EnqueueBytes(EncodeArm32Events(
            (EvdevCodes.EvAbs, EvdevCodes.AbsX, 255),
            (EvdevCodes.EvAbs, EvdevCodes.AbsY, 255),
            (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        var r2 = adapter.Poll();
        Assert.Equal(PadButtons.Down | PadButtons.Right, r2.Snapshot.Buttons);
    }

    [Fact]
    public void AxisRangesHandleSignedExtremeMinimaAndDegenerateCapabilitiesSafely()
    {
        var signed16 = new EvdevAxisRange(-32768, 32767, 128);
        Assert.Equal(0, signed16.Center);
        Assert.Equal(0, signed16.ReadDpadDirection(0));
        Assert.Equal(-1, signed16.ReadDpadDirection(-32768));
        Assert.Equal(1, signed16.ReadDpadDirection(32767));
        Assert.Equal((byte)0x80, signed16.ReadAnalogByte(0));
        Assert.Equal((byte)0, signed16.ReadAnalogByte(-32768));
        Assert.Equal((byte)255, signed16.ReadAnalogByte(32767));

        var intExtreme = new EvdevAxisRange(int.MinValue, int.MaxValue, 0);
        Assert.Equal(0, intExtreme.Center);
        Assert.Equal(-1, intExtreme.ReadDpadDirection(int.MinValue));
        Assert.Equal(1, intExtreme.ReadDpadDirection(int.MaxValue));
        Assert.Equal(0, intExtreme.ReadDpadDirection(0));
        Assert.Equal((byte)0, intExtreme.ReadAnalogByte(int.MinValue));
        Assert.Equal((byte)255, intExtreme.ReadAnalogByte(int.MaxValue));

        var hat = new EvdevAxisRange(-1, 1, 0);
        Assert.Equal(0, hat.Center);
        Assert.Equal(-1, hat.ReadDpadDirection(-1));
        Assert.Equal(0, hat.ReadDpadDirection(0));
        Assert.Equal(1, hat.ReadDpadDirection(1));

        var degenerate = new EvdevAxisRange(100, 100, 0);
        Assert.False(degenerate.IsValid);
        Assert.Equal(0, degenerate.ReadDpadDirection(100));
        Assert.Equal((byte)0x80, degenerate.ReadAnalogByte(100));
    }

    [Fact]
    public void ConventionalControllerSeparatesHatDpadAndAnalogType7Stick()
    {
        var sys = new FakeEvdevSystem();
        var gamepad = CreateConventionalGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 24);
        adapter.Poll();

        // Analog left stick pushed forward-right while hat stays centered.
        gamepad.EnqueueBytes(Encode64BitEvents(
            (EvdevCodes.EvAbs, EvdevCodes.AbsX, 32767),
            (EvdevCodes.EvAbs, EvdevCodes.AbsY, -32768),
            (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        var r1 = adapter.Poll();
        Assert.Equal(PadButtons.None, r1.Snapshot.Buttons);
        Assert.Equal(7, r1.Snapshot.AnalogPad.Type);
        Assert.Equal((byte)255, r1.Snapshot.AnalogPad.LeftX);
        Assert.Equal((byte)0, r1.Snapshot.AnalogPad.LeftY);

        // Hat pushed Up + Left simultaneously.
        gamepad.EnqueueBytes(Encode64BitEvents(
            (EvdevCodes.EvAbs, EvdevCodes.AbsHat0X, -1),
            (EvdevCodes.EvAbs, EvdevCodes.AbsHat0Y, -1),
            (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        var r2 = adapter.Poll();
        Assert.Equal(PadButtons.Up | PadButtons.Left, r2.Snapshot.Buttons);
    }

    [Fact]
    public void AllTwelveHidButtonsAndGamepadButtonsMapExplicitlyAndExposeRawCodes()
    {
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        var logs = new List<string>();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, logs.Add, eventSize: 16);
        adapter.Poll();

        var expectedJoystickMappings = new (ushort Code, PadButtons Button)[]
        {
            (0x120, PadButtons.Triangle),
            (0x121, PadButtons.Circle),
            (0x122, PadButtons.Cross),
            (0x123, PadButtons.Square),
            (0x124, PadButtons.L1),
            (0x125, PadButtons.R1),
            (0x126, PadButtons.L2),
            (0x127, PadButtons.R2),
            (0x128, PadButtons.Select),
            (0x129, PadButtons.Start),
            (0x12A, PadButtons.None),
            (0x12B, PadButtons.None),
        };

        foreach (var (code, expected) in expectedJoystickMappings)
        {
            gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvKey, code, 1), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
            var pressed = adapter.Poll();
            Assert.Equal(expected, pressed.Snapshot.Buttons);
            Assert.Contains(code, pressed.Snapshot.RawPressedKeyCodes);

            gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvKey, code, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
            var released = adapter.Poll();
            Assert.Equal(PadButtons.None, released.Snapshot.Buttons);
            Assert.DoesNotContain(code, released.Snapshot.RawPressedKeyCodes);
        }

        Assert.Contains(logs, l => l.Contains("0x120") && l.Contains("pressed"));
        Assert.Contains(logs, l => l.Contains("0x12B") && l.Contains("released"));

        // Also verify BTN_GAMEPAD + BTN_DPAD explicit mappings.
        Assert.Equal(PadButtons.Cross, EvdevGamepadProfile.MapButton(EvdevCodes.BtnSouth));
        Assert.Equal(PadButtons.Circle, EvdevGamepadProfile.MapButton(EvdevCodes.BtnEast));
        Assert.Equal(PadButtons.Triangle, EvdevGamepadProfile.MapButton(EvdevCodes.BtnNorth));
        Assert.Equal(PadButtons.Square, EvdevGamepadProfile.MapButton(EvdevCodes.BtnWest));
        Assert.Equal(PadButtons.L1, EvdevGamepadProfile.MapButton(EvdevCodes.BtnTl));
        Assert.Equal(PadButtons.R1, EvdevGamepadProfile.MapButton(EvdevCodes.BtnTr));
        Assert.Equal(PadButtons.L2, EvdevGamepadProfile.MapButton(EvdevCodes.BtnTl2));
        Assert.Equal(PadButtons.R2, EvdevGamepadProfile.MapButton(EvdevCodes.BtnTr2));
        Assert.Equal(PadButtons.Select, EvdevGamepadProfile.MapButton(EvdevCodes.BtnSelect));
        Assert.Equal(PadButtons.Start, EvdevGamepadProfile.MapButton(EvdevCodes.BtnStart));
        Assert.Equal(PadButtons.Up, EvdevGamepadProfile.MapButton(EvdevCodes.BtnDpadUp));
        Assert.Equal(PadButtons.Down, EvdevGamepadProfile.MapButton(EvdevCodes.BtnDpadDown));
        Assert.Equal(PadButtons.Left, EvdevGamepadProfile.MapButton(EvdevCodes.BtnDpadLeft));
        Assert.Equal(PadButtons.Right, EvdevGamepadProfile.MapButton(EvdevCodes.BtnDpadRight));
    }

    [Fact]
    public void SynReportAtomicityStagesEventsUntilReportAndHandlesPartialReads()
    {
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        adapter.Poll();

        // Packet without SYN_REPORT must not become visible yet.
        byte[] stagedOnly = EncodeArm32Events(
            (EvdevCodes.EvAbs, EvdevCodes.AbsY, 0),
            (EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 1));
        gamepad.EnqueueBytes(stagedOnly);
        var beforeSync = adapter.Poll();
        Assert.Equal(PadButtons.None, beforeSync.Snapshot.Buttons);

        // Complete the packet with SYN_REPORT split across two partial reads (5 bytes + 11 bytes), plus EINTR.
        byte[] report = EncodeArm32Events((EvdevCodes.EvSyn, EvdevCodes.SynReport, 0));
        gamepad.EnqueueBytes(report[..5]);
        var partial = adapter.Poll();
        Assert.Equal(PadButtons.None, partial.Snapshot.Buttons);

        gamepad.EnqueueError(EvdevCodes.ErrIntr);
        gamepad.EnqueueBytes(report[5..]);
        var committed = adapter.Poll();
        Assert.Equal(PadButtons.Up | PadButtons.Triangle, committed.Snapshot.Buttons);
    }

    [Fact]
    public void SynDroppedDiscardsThroughNextReportThenRequeriesKernelState()
    {
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        adapter.Poll();

        // Hardware currently holds Left + Cross at kernel level when resync happens.
        gamepad.SetLiveAbsValue(EvdevCodes.AbsX, 0);
        gamepad.SetLiveAbsValue(EvdevCodes.AbsY, 128);
        gamepad.SetLiveKey(EvdevCodes.BtnThumb2, true);

        // Queue staged Up, then SYN_DROPPED, then stale events before the terminating SYN_REPORT.
        gamepad.EnqueueBytes(EncodeArm32Events(
            (EvdevCodes.EvAbs, EvdevCodes.AbsY, 0),
            (EvdevCodes.EvSyn, EvdevCodes.SynDropped, 0),
            (EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 1),
            (EvdevCodes.EvAbs, EvdevCodes.AbsY, 255),
            (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        var resynced = adapter.Poll();
        Assert.True(resynced.Snapshot.Connected);
        Assert.Equal(PadButtons.Left | PadButtons.Cross, resynced.Snapshot.Buttons);
    }

    [Fact]
    public void SynDroppedRequeryFailureNeutralizesAndDisconnects()
    {
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        adapter.Poll();

        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsY, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        Assert.Equal(PadButtons.Up, adapter.Poll().Snapshot.Buttons);

        gamepad.FailKeyRequery = true;
        gamepad.EnqueueBytes(EncodeArm32Events(
            (EvdevCodes.EvSyn, EvdevCodes.SynDropped, 0),
            (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        var failed = adapter.Poll();
        Assert.False(failed.Snapshot.Connected);
        Assert.Equal(PadButtons.None, failed.Snapshot.Buttons);
        Assert.Equal(PadButtons.Up, failed.ReleasedDirections);
        Assert.False(gamepad.IsOpen);
    }

    [Fact]
    public void DisconnectNeutralizesAndRetriesDiscoveryToReconnectOnNewEventNode()
    {
        var sys = new FakeEvdevSystem();
        var pad1 = CreateSwitchUsbGamepad("/dev/input/event23");
        sys.Devices.Add(pad1);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16, retryIntervalSeconds: 1.0);

        Assert.Equal("/dev/input/event23", adapter.Poll().Snapshot.DevicePath);

        pad1.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsY, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        Assert.Equal(PadButtons.Up, adapter.Poll().Snapshot.Buttons);

        // Device unplugged -> ENODEV
        pad1.EnqueueError(EvdevCodes.ErrNoDev);
        var disconnected = adapter.Poll();
        Assert.False(disconnected.Snapshot.Connected);
        Assert.Equal(PadButtons.None, disconnected.Snapshot.Buttons);
        Assert.Equal(PadButtons.Up, disconnected.ReleasedDirections);
        Assert.False(pad1.IsOpen);

        // Plugged back in as event11; retry respects interval then reconnects.
        sys.Devices.Remove(pad1);
        var pad2 = CreateSwitchUsbGamepad("/dev/input/event11");
        sys.Devices.Add(pad2);

        clock.Ms(500);
        Assert.False(adapter.Poll().Snapshot.Connected);
        clock.Ms(600);
        var reconnected = adapter.Poll();
        Assert.True(reconnected.Snapshot.Connected);
        Assert.Equal("/dev/input/event11", reconnected.Snapshot.DevicePath);
    }

    [Fact]
    public void FocusLossNeutralizesIgnoresBackgroundAndReconcilesOnRegain()
    {
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        adapter.Poll();

        // Held Up before focus loss.
        gamepad.SetLiveAbsValue(EvdevCodes.AbsY, 0);
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsY, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        Assert.Equal(PadButtons.Up, adapter.Poll().Snapshot.Buttons);

        // Focus lost -> neutralized.
        adapter.FocusChanged(false);
        Assert.Equal(PadButtons.None, adapter.Current.Buttons);

        // Background activity while unfocused must be drained and ignored.
        gamepad.SetLiveKey(EvdevCodes.BtnTrigger, true);
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 1), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        Assert.Equal(PadButtons.None, adapter.Poll().Snapshot.Buttons);

        // User releases everything while still unfocused.
        gamepad.SetLiveAbsValue(EvdevCodes.AbsY, 128);
        gamepad.SetLiveKey(EvdevCodes.BtnTrigger, false);

        // Focus regain reconciles actual current hardware (neutral -> no stale Up!).
        adapter.FocusChanged(true);
        Assert.Equal(PadButtons.None, adapter.Current.Buttons);

        // Now suppose user was physically holding Left in hardware on a subsequent focus regain.
        adapter.FocusChanged(false);
        gamepad.SetLiveAbsValue(EvdevCodes.AbsX, 0);
        adapter.FocusChanged(true);
        Assert.Equal(PadButtons.Left, adapter.Current.Buttons);
    }

    [Fact]
    public void NativeGamepadDoesNotDoubleNavigateMenus()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        var (tv, tvClock) = NewTvWithGamepad(fe, adapter);

        gamepad.EnqueueBytes(EncodeArm32Events(
            (EvdevCodes.EvAbs, EvdevCodes.AbsY, 255),
            (EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 1),
            (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        RunTicks(tv, tvClock, 3);
        Assert.Equal(MenuScreen.Main, tv.Flow.Screen);
        Assert.Equal(0, tv.Flow.Menu.SelectedIndex);
    }

    [Fact]
    public void SceneCoexistenceHandlesSyntheticNuiUpWithDroppedReleaseAndSimultaneousLeft()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        var (tv, tvClock) = NewTvWithGamepad(fe, adapter);

        ToLevelViaRemote(tv, tvClock);
        var level = tv.Flow.Level!;
        RunTicks(tv, tvClock, 2);
        var startPos = level.MechWorld;
        int startAngle = level.WalkerRenderPose.Angle;

        // Gamepad pushes Up -> Samsung NUI delivers Down("Up"), evdev delivers ABS_Y=0.
        tv.Key(TvKeyState.Down, "Up", 0, "gamepad-service");
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsY, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        RunTicks(tv, tvClock, 15);
        Assert.NotEqual((startPos.X, startPos.Z), (level.MechWorld.X, level.MechWorld.Z));

        // Gamepad pushes Left while keeping Up held -> NUI drops Left, evdev has both Up + Left.
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsX, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        RunTicks(tv, tvClock, 20);
        Assert.NotEqual(startAngle, level.WalkerRenderPose.Angle);

        // Gamepad releases Up while keeping Left held -> NUI fails to deliver Key.Up("Up")!
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsY, 128), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        RunTicks(tv, tvClock, 10);

        // Releasing Left stops all movement; Up must NOT remain stuck from the dropped NUI Key.Up!
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsX, 128), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        RunTicks(tv, tvClock, 90); // stride settles
        var settled = level.MechWorld;
        RunTicks(tv, tvClock, 60);
        Assert.Equal(settled, level.MechWorld);
    }

    [Fact]
    public void ReleasingGamepadDirectionDoesNotReleaseHeldRemoteDirection()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        var (tv, tvClock) = NewTvWithGamepad(fe, adapter);
        ToLevelViaRemote(tv, tvClock);
        var level = tv.Flow.Level!;
        tv.Key(TvKeyState.Down, "Up", 0);
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsY, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        RunTicks(tv, tvClock, 30);
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsY, 128), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        RunTicks(tv, tvClock, 60);
        var position = level.MechWorld;
        RunTicks(tv, tvClock, 60);
        Assert.NotEqual((position.X, position.Z), (level.MechWorld.X, level.MechWorld.Z));
    }

    [Fact]
    public void RealRemoteWorksIndependentlyWhenGamepadIsIdleOrMerged()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        var (tv, tvClock) = NewTvWithGamepad(fe, adapter);

        ToLevelViaRemote(tv, tvClock);
        var level = tv.Flow.Level!;
        RunTicks(tv, tvClock, 2);
        var start = level.MechWorld;

        // Real remote Up moves walker while gamepad stays idle.
        tv.Key(TvKeyState.Down, "Up", 0);
        RunTicks(tv, tvClock, 30);
        Assert.NotEqual((start.X, start.Z), (level.MechWorld.X, level.MechWorld.Z));

        // Merge gamepad Triangle (jump) while remote Up is held -> running jump!
        int ground = level.MechWorld.Y;
        int peak = ground;
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 1), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        for (int i = 0; i < 16; i++)
        {
            RunTicks(tv, tvClock, 1);
            peak = Math.Max(peak, level.MechWorld.Y);
        }
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        tv.Key(TvKeyState.Up, "Up", 0);
        for (int i = 0; i < 90; i++)
        {
            RunTicks(tv, tvClock, 1);
            peak = Math.Max(peak, level.MechWorld.Y);
        }
        Assert.True(peak > ground + 0x1000, $"merged remote+gamepad jump peak {peak}, ground {ground}");
    }

    [Fact]
    public void ConventionalAnalogStickDrivesLevelWalkerThroughType7Path()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var sys = new FakeEvdevSystem();
        var gamepad = CreateConventionalGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 24);
        var (tv, tvClock) = NewTvWithGamepad(fe, adapter);

        ToLevelViaRemote(tv, tvClock);
        var level = tv.Flow.Level!;
        RunTicks(tv, tvClock, 2);
        var start = level.MechWorld;

        gamepad.EnqueueBytes(Encode64BitEvents(
            (EvdevCodes.EvAbs, EvdevCodes.AbsY, -32768),
            (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        RunTicks(tv, tvClock, 40);
        Assert.NotEqual((start.X, start.Z), (level.MechWorld.X, level.MechWorld.Z));
    }

    [Fact]
    public void HeldNativeButtonAtSceneEntryCannotJumpUntilReleased()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        var (tv, tvClock) = NewTvWithGamepad(fe, adapter);

        ClickRemote(tv, "Down");
        ClickRemote(tv, "Down");
        for (int i = 0; i < 4; i++) ClickRemote(tv, "Return");

        // Gamepad confirm/Triangle is already held when Return enters the scene.
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 1), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        RunTicks(tv, tvClock, 1);

        tv.Key(TvKeyState.Down, "Return", 0);
        Assert.Equal(MenuScreen.Scene, tv.Flow.Screen);
        var level = tv.Flow.Level!;
        RunTicks(tv, tvClock, 60);
        int ground = level.MechWorld.Y;
        Assert.Equal(ground, level.MechWorld.Y);

        // Release both, then press native Triangle cleanly -> jumps!
        tv.Key(TvKeyState.Up, "Return", 0);
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        RunTicks(tv, tvClock, 2);

        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 1), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        int peak = ground;
        for (int i = 0; i < 16; i++) { RunTicks(tv, tvClock, 1); peak = Math.Max(peak, level.MechWorld.Y); }
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        for (int i = 0; i < 90; i++) { RunTicks(tv, tvClock, 1); peak = Math.Max(peak, level.MechWorld.Y); }
        Assert.True(peak > ground + 0x1000, $"post-release jump peak {peak}, ground {ground}");
    }

    private static byte[] Sync() => EncodeArm32Events((EvdevCodes.EvSyn, EvdevCodes.SynReport, 0));

    /// <summary>True when the walker's horizontal position changes over <paramref name="ticks"/> ticks.</summary>
    private static bool WalkerMoves(TvLevelControl tv, FakeClock clock, int ticks = 30)
    {
        var level = tv.Flow.Level!;
        var before = level.MechWorld;
        RunTicks(tv, clock, ticks);
        return (before.X, before.Z) != (level.MechWorld.X, level.MechWorld.Z);
    }

    /// <summary>True when the walker stays put after letting the stride settle.</summary>
    private static bool WalkerSettles(TvLevelControl tv, FakeClock clock)
    {
        var level = tv.Flow.Level!;
        RunTicks(tv, clock, 90);
        var settled = level.MechWorld;
        RunTicks(tv, clock, 60);
        return settled == level.MechWorld;
    }

    private static int JumpPeak(TvLevelControl tv, FakeClock clock, int ticks = 90)
    {
        var level = tv.Flow.Level!;
        int peak = level.MechWorld.Y;
        for (int i = 0; i < ticks; i++) { RunTicks(tv, clock, 1); peak = Math.Max(peak, level.MechWorld.Y); }
        return peak;
    }

    [Fact]
    public void HasDeliveredInputRequiresCommittedControlEventOnCurrentFd()
    {
        var sys = new FakeEvdevSystem();
        var pad1 = CreateSwitchUsbGamepad("/dev/input/event23");
        sys.Devices.Add(pad1);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);

        Assert.True(adapter.Poll().Snapshot.Connected);
        Assert.False(adapter.HasDeliveredInput); // connected alone proves nothing

        // Kernel snapshot of a held button is state, not a delivered event.
        pad1.SetLiveKey(EvdevCodes.BtnTrigger, true);
        adapter.FocusChanged(false);
        adapter.FocusChanged(true);
        Assert.Equal(PadButtons.Triangle, adapter.Current.Buttons);
        Assert.False(adapter.HasDeliveredInput);
        pad1.SetLiveKey(EvdevCodes.BtnTrigger, false);

        // Background (unfocused) events are drained and do not count.
        adapter.FocusChanged(false);
        pad1.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsY, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        adapter.Poll();
        Assert.False(adapter.HasDeliveredInput);
        adapter.FocusChanged(true);

        // An ignored axis (ABS_WHEEL) and an uncommitted control event do not count.
        pad1.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsWheel, 3), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        adapter.Poll();
        Assert.False(adapter.HasDeliveredInput);
        pad1.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsX, 0)));
        adapter.Poll();
        Assert.False(adapter.HasDeliveredInput);

        pad1.EnqueueBytes(Sync());
        adapter.Poll();
        Assert.True(adapter.HasDeliveredInput);

        // Disconnect resets the proof; a reconnected fd must prove itself again.
        pad1.EnqueueError(EvdevCodes.ErrNoDev);
        adapter.Poll();
        Assert.False(adapter.HasDeliveredInput);
        sys.Devices.Remove(pad1);
        var pad2 = CreateSwitchUsbGamepad("/dev/input/event11");
        sys.Devices.Add(pad2);
        clock.Ms(1100);
        Assert.True(adapter.Poll().Snapshot.Connected);
        Assert.False(adapter.HasDeliveredInput);
        pad2.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvKey, EvdevCodes.BtnBase6, 1), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        adapter.Poll();
        Assert.True(adapter.HasDeliveredInput); // unmapped but advertised button still proves delivery
    }

    [Fact]
    public void SynDroppedResyncDoesNotReplayEventsAlreadyBufferedBeforeTheSnapshot()
    {
        // Linux evdev: EVIOCGKEY returns current key state and flushes queued EV_KEY events from the
        // kernel client buffer (evdev_handle_get_val -> __evdev_flush_queue), but events already copied
        // into user space by an earlier read() are older than the snapshot and must not be applied.
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        adapter.Poll();

        // Kernel now reports: Triangle released, axes neutral.
        gamepad.EnqueueBytes(EncodeArm32Events(
            (EvdevCodes.EvSyn, EvdevCodes.SynDropped, 0),
            (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0),
            (EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 1), // stale press, already in user space
            (EvdevCodes.EvAbs, EvdevCodes.AbsY, 0),       // stale direction
            (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        var resynced = adapter.Poll();
        Assert.True(resynced.Snapshot.Connected);
        Assert.Equal(PadButtons.None, resynced.Snapshot.Buttons);

        // Events read after the snapshot are processed normally.
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvKey, EvdevCodes.BtnThumb, 1), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        Assert.Equal(PadButtons.Circle, adapter.Poll().Snapshot.Buttons);
    }

    [Fact]
    public void SynDroppedResyncDiscardsPartialStaleEventAndKeepsEventAlignment()
    {
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        adapter.Poll();

        byte[] head = EncodeArm32Events((EvdevCodes.EvSyn, EvdevCodes.SynDropped, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0));
        byte[] stale = EncodeArm32Events((EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 1));
        gamepad.EnqueueBytes(head.Concat(stale[..5]).ToArray());
        Assert.Equal(PadButtons.None, adapter.Poll().Snapshot.Buttons);

        gamepad.EnqueueBytes(stale[5..].Concat(EncodeArm32Events(
            (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0),
            (EvdevCodes.EvAbs, EvdevCodes.AbsX, 0),
            (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0))).ToArray());
        Assert.Equal(PadButtons.Left, adapter.Poll().Snapshot.Buttons);
    }

    [Fact]
    public void SynDroppedQueuedAbsoluteEventsAfterSnapshotConvergeToKernelState()
    {
        // EVIOCGABS does not flush queued EV_ABS events; they are absolute and older-or-equal to the snapshot,
        // so replaying the remaining kernel queue converges on the latest value.
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        adapter.Poll();

        gamepad.SetLiveAbsValue(EvdevCodes.AbsX, 128);
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvSyn, EvdevCodes.SynDropped, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        gamepad.EnqueueBytes(EncodeArm32Events(
            (EvdevCodes.EvAbs, EvdevCodes.AbsX, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0),
            (EvdevCodes.EvAbs, EvdevCodes.AbsX, 128), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        Assert.Equal(PadButtons.None, adapter.Poll().Snapshot.Buttons);
    }

    [Fact]
    public void TranslatedDownForwardedWhileDisconnectedIsReleasedByUpAfterReconnect()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var sys = new FakeEvdevSystem(); // no gamepad visible yet
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16, retryIntervalSeconds: 0.5);
        var (tv, tvClock) = NewTvWithGamepad(fe, adapter);
        ToLevelViaRemote(tv, tvClock);
        Assert.True(WalkerSettles(tv, tvClock));

        tv.Key(TvKeyState.Down, "Up", 0, "gamepad-service");
        Assert.True(WalkerMoves(tv, tvClock));

        // Pad reconnects and delivers a genuine neutral axis event; translated Down was already forwarded.
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        clock.Ms(600);
        RunTicks(tv, tvClock, 1);
        Assert.True(adapter.Current.Connected);
        tv.Key(TvKeyState.Up, "Up", 0, "gamepad-service");
        Assert.True(WalkerSettles(tv, tvClock));
    }

    [Fact]
    public void ConnectedButSilentEvdevDoesNotFilterTranslatedNuiInput()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        var (tv, tvClock) = NewTvWithGamepad(fe, adapter);
        ToLevelViaRemote(tv, tvClock);
        Assert.True(WalkerSettles(tv, tvClock));
        Assert.True(adapter.Current.Connected);
        Assert.False(adapter.HasDeliveredInput);
        Assert.True(WalkerSettles(tv, tvClock));

        tv.Key(TvKeyState.Down, "Up", 0, "gamepad-service");
        Assert.True(WalkerMoves(tv, tvClock));
        tv.Key(TvKeyState.Up, "Up", 0, "gamepad-service");
        Assert.True(WalkerSettles(tv, tvClock));
    }

    [Fact]
    public void FilteringStartReleasesPriorForwardedTranslatedDownButNotRemoteHold()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        var (tv, tvClock) = NewTvWithGamepad(fe, adapter);
        ToLevelViaRemote(tv, tvClock);
        Assert.True(WalkerSettles(tv, tvClock));

        // Translated Up forwarded before evdev proved delivery; its NUI Up is then never delivered.
        tv.Key(TvKeyState.Down, "Up", 0, "gamepad-service");
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsY, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        Assert.True(WalkerMoves(tv, tvClock));
        Assert.True(adapter.HasDeliveredInput);
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsY, 128), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        Assert.True(WalkerSettles(tv, tvClock));

        // Translated pad keys are now filtered...
        tv.Key(TvKeyState.Down, "Up", 0, "gamepad-service");
        Assert.True(WalkerSettles(tv, tvClock));
        tv.Key(TvKeyState.Up, "Up", 0, "gamepad-service");

        // ...while the real remote still drives the walker, and a translated release cannot cancel it.
        tv.Key(TvKeyState.Down, "Up", 0);
        tv.Key(TvKeyState.Up, "Up", 0, "gamepad-service");
        Assert.True(WalkerMoves(tv, tvClock));
        tv.Key(TvKeyState.Up, "Up", 0);
        Assert.True(WalkerSettles(tv, tvClock));
    }

    [Fact]
    public void TranslatedReleaseDoesNotCancelRemoteHoldOfSameKeyBeforeFiltering()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var sys = new FakeEvdevSystem();
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        var (tv, tvClock) = NewTvWithGamepad(fe, adapter);
        ToLevelViaRemote(tv, tvClock);
        Assert.True(WalkerSettles(tv, tvClock));

        tv.Key(TvKeyState.Down, "Up", 0);
        tv.Key(TvKeyState.Down, "Up", 0, "gamepad-service");
        tv.Key(TvKeyState.Up, "Up", 0, "gamepad-service");
        Assert.True(WalkerMoves(tv, tvClock));
        tv.Key(TvKeyState.Up, "Up", 0);
        Assert.True(WalkerSettles(tv, tvClock));
    }

    [Theory]
    [InlineData("XF86Back", 0)]
    [InlineData("Escape", 0)]
    [InlineData("", 166)]
    public void TranslatedControllerBackLeavesSceneWhileNativeInputIsActive(string name, int code)
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        var (tv, tvClock) = NewTvWithGamepad(fe, adapter);
        ToLevelViaRemote(tv, tvClock);
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsX, 128), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        RunTicks(tv, tvClock, 2);
        Assert.True(adapter.HasDeliveredInput);

        Assert.Equal(TvKeyAction.Present, tv.Key(TvKeyState.Down, name, code, "gamepad-service"));
        Assert.Equal(MenuScreen.Loadout, tv.Flow.Screen);
        tv.Key(TvKeyState.Up, name, code, "gamepad-service");
        Assert.Equal(MenuScreen.Loadout, tv.Flow.Screen);
    }

    [Fact]
    public void UnexpectedAdapterExceptionDisablesNativeInputOnceAndKeepsTicking()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        var logs = new List<string>();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        var (tv, tvClock) = NewTvWithGamepad(fe, adapter, logs.Add);
        ToLevelViaRemote(tv, tvClock);
        Assert.True(WalkerSettles(tv, tvClock));
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvAbs, EvdevCodes.AbsY, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        Assert.True(WalkerMoves(tv, tvClock));

        sys.ThrowOnRead = new InvalidOperationException("boom");
        RunTicks(tv, tvClock, 5); // must not throw
        tv.FocusChanged(false);
        tv.FocusChanged(true);
        Assert.False(tv.NativeGamepadEnabled);
        Assert.Single(logs, l => l.Contains("native gamepad disabled") && l.Contains("boom"));
        Assert.False(gamepad.IsOpen);

        // Native Up vanished; walker settles and the remote still works.
        Assert.True(WalkerSettles(tv, tvClock));
        tv.Key(TvKeyState.Down, "Up", 0);
        Assert.True(WalkerMoves(tv, tvClock));
        tv.Key(TvKeyState.Up, "Up", 0);
        // Translated NUI input is no longer filtered once native input is gone.
        tv.Key(TvKeyState.Down, "Up", 0, "gamepad-service");
        Assert.True(WalkerMoves(tv, tvClock));
    }

    [Fact]
    public void UnexpectedExceptionDuringFocusRegainDisablesNativeInput()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var sys = new FakeEvdevSystem();
        sys.Devices.Add(CreateSwitchUsbGamepad());
        var clock = new FakeClock();
        var logs = new List<string>();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        var (tv, tvClock) = NewTvWithGamepad(fe, adapter, logs.Add);
        RunTicks(tv, tvClock, 1);
        tv.FocusChanged(false);
        sys.ThrowOnRead = new IOException("focus boom");
        tv.FocusChanged(true);
        RunTicks(tv, tvClock, 3);
        Assert.False(tv.NativeGamepadEnabled);
        Assert.Single(logs, l => l.Contains("focus boom"));
    }

    [Fact]
    public void FocusRegainWithHeldNativeButtonDoesNotJumpUntilReleased()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var sys = new FakeEvdevSystem();
        var gamepad = CreateSwitchUsbGamepad();
        sys.Devices.Add(gamepad);
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        var (tv, tvClock) = NewTvWithGamepad(fe, adapter);
        ToLevelViaRemote(tv, tvClock);
        tv.Key(TvKeyState.Up, "Return", 0);
        RunTicks(tv, tvClock, 120);
        var level = tv.Flow.Level!;
        int ground = level.MechWorld.Y;

        // Focus lost; Triangle and Left pressed in the background, still held at regain.
        tv.FocusChanged(false);
        gamepad.SetLiveKey(EvdevCodes.BtnTrigger, true);
        gamepad.SetLiveAbsValue(EvdevCodes.AbsX, 0);
        gamepad.EnqueueBytes(EncodeArm32Events(
            (EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 1), (EvdevCodes.EvAbs, EvdevCodes.AbsX, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        RunTicks(tv, tvClock, 2);
        tv.FocusChanged(true);
        Assert.Equal(PadButtons.Triangle | PadButtons.Left, adapter.Current.Buttons);

        int startAngle = level.WalkerRenderPose.Angle;
        Assert.True(JumpPeak(tv, tvClock) <= ground + 0x100, "held Triangle at focus regain must not jump");
        Assert.NotEqual(startAngle, level.WalkerRenderPose.Angle); // held direction still reconciles

        // Release and press again: jumps.
        gamepad.EnqueueBytes(EncodeArm32Events(
            (EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 0), (EvdevCodes.EvAbs, EvdevCodes.AbsX, 128), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        RunTicks(tv, tvClock, 120);
        ground = level.MechWorld.Y;
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 1), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        int peak = JumpPeak(tv, tvClock, 16);
        gamepad.EnqueueBytes(EncodeArm32Events((EvdevCodes.EvKey, EvdevCodes.BtnTrigger, 0), (EvdevCodes.EvSyn, EvdevCodes.SynReport, 0)));
        peak = Math.Max(peak, JumpPeak(tv, tvClock));
        Assert.True(peak > ground + 0x1000, $"re-press after focus regain should jump: peak {peak}, ground {ground}");
    }

    [Fact]
    public void FocusLossClearsSourceTrackingSoLostReleasesCannotBlockLaterReleases()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var sys = new FakeEvdevSystem();
        var clock = new FakeClock();
        using var adapter = new EvdevGamepadAdapter(sys, () => clock.Now, FakeClock.Frequency, eventSize: 16);
        var (tv, tvClock) = NewTvWithGamepad(fe, adapter);
        ToLevelViaRemote(tv, tvClock);
        Assert.True(WalkerSettles(tv, tvClock));

        tv.Key(TvKeyState.Down, "Up", 0);      // remote Up; its release goes to another window
        tv.FocusChanged(false);
        tv.FocusChanged(true);
        tv.Key(TvKeyState.Down, "Up", 0, "gamepad-service");
        Assert.True(WalkerMoves(tv, tvClock));
        tv.Key(TvKeyState.Up, "Up", 0, "gamepad-service");
        Assert.True(WalkerSettles(tv, tvClock));
    }
}
