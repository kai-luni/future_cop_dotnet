#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using FutureCop.Game.Mech;

namespace FcMainMenu;

/// <summary>Immutable published state of the active evdev controller.</summary>
public readonly record struct GamepadSnapshot(
    bool Connected,
    PadButtons Buttons,
    AnalogPad AnalogPad,
    IReadOnlyList<ushort> RawPressedKeyCodes,
    int AxisX,
    int AxisY,
    string? DevicePath,
    string? DeviceName,
    ushort Vendor,
    ushort Product)
{
    public static GamepadSnapshot Disconnected => new(
        false, PadButtons.None, AnalogPad.Digital, Array.Empty<ushort>(), 0, 0, null, null, 0, 0);
}

/// <summary>Poll result containing the committed snapshot and any D-pad directions released during the poll.</summary>
public readonly record struct GamepadPollResult(GamepadSnapshot Snapshot, PadButtons ReleasedDirections);

/// <summary>
/// Non-blocking Linux evdev adapter running on the UI thread: discovers matching gamepads dynamically,
/// stages EV_KEY/EV_ABS packets until SYN_REPORT, resyncs on SYN_DROPPED via EVIOCGKEY/EVIOCGABS,
/// handles partial reads and disconnects/reconnects, and neutralizes safely on focus loss.
/// </summary>
public sealed class EvdevGamepadAdapter : IDisposable
{
    private const PadButtons DpadMask = PadButtons.Up | PadButtons.Down | PadButtons.Left | PadButtons.Right;
    private const int MaxReadsPerPoll = 64;
    private const int MaxEintrRetries = 8;

    private readonly IEvdevSystem _system;
    private readonly Func<long> _clock;
    private readonly long _retryIntervalTicks;
    private readonly Action<string>? _log;
    private readonly int _eventSize;

    private readonly byte[] _readBuffer = new byte[4096];
    private int _bufferedBytes;

    private readonly bool[] _stagedKeys = new bool[EvdevCodes.KeyMax + 1];
    private readonly bool[] _publishedKeys = new bool[EvdevCodes.KeyMax + 1];
    private readonly int[] _stagedAbs = new int[EvdevCodes.AbsMax + 1];
    private readonly int[] _publishedAbs = new int[EvdevCodes.AbsMax + 1];
    private readonly byte[] _kernelKeyBitmap = new byte[EvdevCodes.KeyBitmapBytes];
    private IReadOnlyList<ushort> _publishedRawKeys = Array.Empty<ushort>();

    private int _fd = -1;
    private EvdevGamepadProfile? _profile;
    private bool _droppingUntilSync;
    // Set by a SYN_DROPPED resync: bytes already copied to user space predate the kernel snapshot.
    private bool _discardBufferedAfterResync;
    // Number of upcoming bytes to discard so a partially buffered pre-snapshot event is not replayed.
    private int _discardBytes;
    private bool _stagedGenuineControl;
    private bool _hasFocus = true;
    private bool _disposed;
    private bool _hasAttemptedDiscovery;
    private bool _loggedNoDevice;
    private long _lastDiscoveryTicks;

    public EvdevGamepadAdapter(
        IEvdevSystem system,
        Func<long>? clock = null,
        long frequency = 0,
        Action<string>? log = null,
        int eventSize = 0,
        double retryIntervalSeconds = 1.0)
    {
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _clock = clock ?? System.Diagnostics.Stopwatch.GetTimestamp;
        long freq = frequency > 0 ? frequency : System.Diagnostics.Stopwatch.Frequency;
        _retryIntervalTicks = Math.Max(1L, (long)Math.Round(retryIntervalSeconds * freq));
        _log = log;
        _eventSize = eventSize is 16 or 24 ? eventSize : EvdevEvent.NativeEventSize;
        Current = GamepadSnapshot.Disconnected;
    }

    public GamepadSnapshot Current { get; private set; }
    public bool HasFocus => _hasFocus;

    /// <summary>
    /// True once the currently open fd has delivered at least one committed (SYN_REPORT) EV_KEY event for
    /// an advertised button or EV_ABS event for a D-pad/stick axis while focused. Being connected alone does
    /// not prove that this process actually receives events. Reset on disconnect and when a device is opened.
    /// </summary>
    public bool HasDeliveredInput { get; private set; }

    public GamepadPollResult Poll()
    {
        if (_disposed) return new GamepadPollResult(GamepadSnapshot.Disconnected, PadButtons.None);

        if (_fd < 0)
        {
            long now = _clock();
            if (!_hasAttemptedDiscovery || now - _lastDiscoveryTicks >= _retryIntervalTicks)
            {
                _hasAttemptedDiscovery = true;
                _lastDiscoveryTicks = now;
                TryDiscoverAndOpen();
            }
            if (_fd < 0)
                return new GamepadPollResult(Current, PadButtons.None);
        }

        PadButtons releasedDirections = PadButtons.None;
        DrainAndProcess(publish: _hasFocus, ref releasedDirections);
        return new GamepadPollResult(Current, releasedDirections);
    }

    public void FocusChanged(bool gained)
    {
        if (_disposed || _hasFocus == gained) return;
        _hasFocus = gained;

        if (!gained)
        {
            NeutralizeState(keepConnectionMetadata: _fd >= 0);
            return;
        }

        if (_fd < 0)
        {
            _hasAttemptedDiscovery = false;
            return;
        }

        PadButtons ignored = PadButtons.None;
        DrainAndProcess(publish: false, ref ignored);
        if (_fd < 0) return;

        // Discard any incomplete packet boundary from background draining and reconcile real kernel state.
        _bufferedBytes = 0;
        _droppingUntilSync = false;
        _discardBufferedAfterResync = false;
        _discardBytes = 0;
        _stagedGenuineControl = false;
        if (!ResyncFromKernel(publish: true, ref ignored))
            Disconnect(ref ignored, "focus-regain kernel resync failed");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PadButtons ignored = PadButtons.None;
        Disconnect(ref ignored, null);
    }

    private void TryDiscoverAndOpen()
    {
        IReadOnlyList<string> paths = _system.EnumerateEventDevices();
        int bestFd = -1;
        int bestScore = 0;
        EvdevDeviceCapabilities? bestCaps = null;

        try
        {
            foreach (string path in paths)
            {
                int fd = _system.OpenNonBlocking(path, out _);
                if (fd < 0) continue;

                EvdevDeviceCapabilities? caps = ProbeCapabilities(fd, path);
                int score = caps != null ? EvdevGamepadProfile.ScoreCandidate(caps) : 0;
                if (score > bestScore && caps != null)
                {
                    if (bestFd >= 0) _system.Close(bestFd);
                    bestFd = fd;
                    bestScore = score;
                    bestCaps = caps;
                }
                else
                {
                    _system.Close(fd);
                }
            }

            if (bestFd < 0 || bestCaps == null)
            {
                if (!_loggedNoDevice)
                {
                    _loggedNoDevice = true;
                    _log?.Invoke("evdev: no matching gamepad found; will retry");
                }
                return;
            }

            var profile = EvdevGamepadProfile.Create(bestCaps);
            _fd = bestFd;
            bestFd = -1;
            _profile = profile;
            _bufferedBytes = 0;
            _droppingUntilSync = false;
            _discardBufferedAfterResync = false;
            _discardBytes = 0;
            _stagedGenuineControl = false;
            HasDeliveredInput = false;
            _loggedNoDevice = false;

            PadButtons ignored = PadButtons.None;
            if (!ResyncFromKernel(publish: _hasFocus, ref ignored))
            {
                Disconnect(ref ignored, "initial kernel state query failed");
                return;
            }
            if (!_hasFocus)
                NeutralizeState(keepConnectionMetadata: true);

            LogDiscoveredDevice(profile);
        }
        finally
        {
            if (bestFd >= 0) _system.Close(bestFd);
        }
    }

    private EvdevDeviceCapabilities? ProbeCapabilities(int fd, string path)
    {
        if (!_system.TryGetId(fd, out var id, out _)) return null;
        if (!_system.TryGetName(fd, out string name, out _)) name = string.Empty;

        byte[] evBits = new byte[EvdevCodes.EvBitmapBytes];
        byte[] keyBits = new byte[EvdevCodes.KeyBitmapBytes];
        byte[] absBits = new byte[EvdevCodes.AbsBitmapBytes];

        if (!_system.TryGetBitmask(fd, 0, evBits, out _)) return null;
        if (EvdevDeviceCapabilities.HasBit(evBits, EvdevCodes.EvKey)
            && !_system.TryGetBitmask(fd, EvdevCodes.EvKey, keyBits, out _))
            return null;
        if (EvdevDeviceCapabilities.HasBit(evBits, EvdevCodes.EvAbs)
            && !_system.TryGetBitmask(fd, EvdevCodes.EvAbs, absBits, out _))
            return null;

        var absInfos = new Dictionary<int, EvdevAbsInfo>();
        if (EvdevDeviceCapabilities.HasBit(evBits, EvdevCodes.EvAbs))
        {
            for (int axis = 0; axis <= EvdevCodes.AbsMax; axis++)
            {
                if (!EvdevDeviceCapabilities.HasBit(absBits, axis)) continue;
                if (_system.TryGetAbsInfo(fd, axis, out var info, out _))
                    absInfos[axis] = info;
            }
        }

        return new EvdevDeviceCapabilities(path, id, name, evBits, keyBits, absBits, absInfos);
    }

    private void DrainAndProcess(bool publish, ref PadButtons releasedDirections)
    {
        int eintrRetries = 0;
        for (int reads = 0; reads < MaxReadsPerPoll && _fd >= 0; reads++)
        {
            int space = _readBuffer.Length - _bufferedBytes;
            if (space <= 0)
            {
                _bufferedBytes = 0;
                space = _readBuffer.Length;
            }

            int n = _system.Read(_fd, _readBuffer.AsSpan(_bufferedBytes, space), out int errno);
            if (n < 0)
            {
                if (errno == EvdevCodes.ErrIntr)
                {
                    if (++eintrRetries <= MaxEintrRetries) continue;
                    break;
                }
                if (errno == EvdevCodes.ErrAgain)
                    break;

                Disconnect(ref releasedDirections, string.Create(CultureInfo.InvariantCulture, $"read error errno={errno}"));
                return;
            }
            if (n == 0)
            {
                Disconnect(ref releasedDirections, "device closed (EOF)");
                return;
            }

            eintrRetries = 0;
            _bufferedBytes += n;

            int offset = 0;
            if (_discardBytes > 0)
            {
                int skip = Math.Min(_discardBytes, _bufferedBytes);
                offset += skip;
                _discardBytes -= skip;
            }
            while (_bufferedBytes - offset >= _eventSize && _fd >= 0)
            {
                if (EvdevEvent.TryParse(_readBuffer.AsSpan(offset, _eventSize), _eventSize, out var ev))
                    ProcessEvent(ev, publish, ref releasedDirections);
                offset += _eventSize;
                if (_discardBufferedAfterResync)
                {
                    // Events already read into user space before the EVIOCGKEY/EVIOCGABS snapshot are older
                    // than it. The kernel flushes queued EV_KEY events on EVIOCGKEY but cannot flush these,
                    // so replaying them could re-press a key the snapshot reports released. Drop them,
                    // including the bytes still missing from a partially buffered event.
                    _discardBufferedAfterResync = false;
                    int stale = _bufferedBytes - offset;
                    int partial = stale % _eventSize;
                    offset = _bufferedBytes;
                    _discardBytes = partial == 0 ? 0 : _eventSize - partial;
                }
            }

            if (_fd < 0) return; // Disconnect already reset the buffer.
            int remaining = _bufferedBytes - offset;
            if (remaining > 0 && offset > 0)
                Buffer.BlockCopy(_readBuffer, offset, _readBuffer, 0, remaining);
            _bufferedBytes = remaining;
        }
    }

    private void ProcessEvent(EvdevEvent ev, bool publish, ref PadButtons releasedDirections)
    {
        if (ev.Type == EvdevCodes.EvSyn)
        {
            if (ev.Code == EvdevCodes.SynDropped)
            {
                _droppingUntilSync = true;
                _log?.Invoke("evdev: SYN_DROPPED received, discarding until next SYN_REPORT");
                return;
            }
            if (ev.Code == EvdevCodes.SynReport)
            {
                if (_droppingUntilSync)
                {
                    _droppingUntilSync = false;
                    _stagedGenuineControl = false;
                    _discardBufferedAfterResync = true;
                    if (!ResyncFromKernel(publish, ref releasedDirections))
                        Disconnect(ref releasedDirections, "SYN_DROPPED kernel resync failed");
                    else
                        _log?.Invoke("evdev: SYN_DROPPED resync completed");
                    return;
                }
                if (publish)
                {
                    CommitStagedFrame(ref releasedDirections);
                    if (_stagedGenuineControl) HasDeliveredInput = true;
                }
                _stagedGenuineControl = false;
            }
            return;
        }

        if (_droppingUntilSync || !publish) return;

        if (ev.Type == EvdevCodes.EvKey)
        {
            if (ev.Code <= EvdevCodes.KeyMax)
            {
                _stagedKeys[ev.Code] = ev.Value != 0;
                if (_profile != null && _profile.Capabilities.SupportsKey(ev.Code)) _stagedGenuineControl = true;
            }
        }
        else if (ev.Type == EvdevCodes.EvAbs)
        {
            if (ev.Code <= EvdevCodes.AbsMax)
            {
                _stagedAbs[ev.Code] = ev.Value;
                if (_profile != null && _profile.ClassifyAxis(ev.Code) != EvdevAxisRole.Ignored) _stagedGenuineControl = true;
            }
        }
    }

    private bool ResyncFromKernel(bool publish, ref PadButtons releasedDirections)
    {
        if (_fd < 0 || _profile == null) return false;

        Array.Clear(_kernelKeyBitmap, 0, _kernelKeyBitmap.Length);
        if (!_system.TryGetKeyStates(_fd, _kernelKeyBitmap, out _))
            return false;

        Array.Clear(_stagedKeys, 0, _stagedKeys.Length);
        for (int code = 0; code <= EvdevCodes.KeyMax; code++)
            _stagedKeys[code] = EvdevDeviceCapabilities.HasBit(_kernelKeyBitmap, code);

        ResetStagedAxesToNeutral();
        foreach (int axis in _profile.Capabilities.AbsInfos.Keys)
        {
            if (!_system.TryGetAbsInfo(_fd, axis, out var info, out _))
                return false;
            if ((uint)axis < (uint)_stagedAbs.Length)
                _stagedAbs[axis] = info.Value;
        }

        if (publish)
            CommitStagedFrame(ref releasedDirections);
        return true;
    }

    private void CommitStagedFrame(ref PadButtons releasedDirections)
    {
        if (_profile == null) return;

        PadButtons prevButtons = Current.Buttons;
        PadButtons newButtons = _profile.EvaluateButtons(_stagedKeys, _stagedAbs);
        AnalogPad newPad = _profile.EvaluateAnalogPad(_stagedAbs);

        releasedDirections |= (prevButtons & ~newButtons) & DpadMask;

        bool keysChanged = false;
        for (int code = 0; code < _stagedKeys.Length; code++)
        {
            bool now = _stagedKeys[code];
            if (now == _publishedKeys[code]) continue;
            _publishedKeys[code] = now;
            keysChanged = true;

            ushort ucode = (ushort)code;
            PadButtons mapped = EvdevGamepadProfile.MapButton(ucode);
            _log?.Invoke(string.Create(CultureInfo.InvariantCulture,
                $"evdev key {EvdevGamepadProfile.DescribeKeyCode(ucode)} (0x{code:X3}) {(now ? "pressed" : "released")} -> {mapped}"));
        }

        if (keysChanged)
        {
            var list = new List<ushort>();
            for (int code = 0; code < _publishedKeys.Length; code++)
                if (_publishedKeys[code]) list.Add((ushort)code);
            _publishedRawKeys = list.Count == 0 ? Array.Empty<ushort>() : list.ToArray();
        }

        Array.Copy(_stagedAbs, _publishedAbs, _stagedAbs.Length);

        PadButtons prevDpad = prevButtons & DpadMask;
        PadButtons newDpad = newButtons & DpadMask;
        if (prevDpad != newDpad)
        {
            _log?.Invoke(string.Create(CultureInfo.InvariantCulture,
                $"evdev dpad -> {newDpad} (x={_publishedAbs[EvdevCodes.AbsX]} y={_publishedAbs[EvdevCodes.AbsY]})"));
        }

        var caps = _profile.Capabilities;
        Current = new GamepadSnapshot(
            Connected: true,
            Buttons: newButtons,
            AnalogPad: newPad,
            RawPressedKeyCodes: _publishedRawKeys,
            AxisX: _publishedAbs[EvdevCodes.AbsX],
            AxisY: _publishedAbs[EvdevCodes.AbsY],
            DevicePath: caps.DevicePath,
            DeviceName: caps.Name,
            Vendor: caps.Id.Vendor,
            Product: caps.Id.Product);
    }

    private void NeutralizeState(bool keepConnectionMetadata)
    {
        Array.Clear(_stagedKeys, 0, _stagedKeys.Length);
        Array.Clear(_publishedKeys, 0, _publishedKeys.Length);
        _publishedRawKeys = Array.Empty<ushort>();
        ResetStagedAxesToNeutral();
        Array.Copy(_stagedAbs, _publishedAbs, _stagedAbs.Length);

        if (!keepConnectionMetadata || _profile == null)
        {
            Current = GamepadSnapshot.Disconnected;
            return;
        }

        var caps = _profile.Capabilities;
        Current = new GamepadSnapshot(
            Connected: true,
            Buttons: PadButtons.None,
            AnalogPad: AnalogPad.Digital,
            RawPressedKeyCodes: Array.Empty<ushort>(),
            AxisX: _publishedAbs[EvdevCodes.AbsX],
            AxisY: _publishedAbs[EvdevCodes.AbsY],
            DevicePath: caps.DevicePath,
            DeviceName: caps.Name,
            Vendor: caps.Id.Vendor,
            Product: caps.Id.Product);
    }

    private void ResetStagedAxesToNeutral()
    {
        Array.Clear(_stagedAbs, 0, _stagedAbs.Length);
        if (_profile == null) return;
        foreach (int axis in _profile.Capabilities.AbsInfos.Keys)
        {
            if ((uint)axis < (uint)_stagedAbs.Length && _profile.TryGetAxisRange(axis, out var range))
                _stagedAbs[axis] = (int)Math.Clamp(range.Center, int.MinValue, int.MaxValue);
        }
    }

    private void Disconnect(ref PadButtons releasedDirections, string? reason)
    {
        releasedDirections |= Current.Buttons & DpadMask;
        int fd = _fd;
        _fd = -1;
        string? path = _profile?.Capabilities.DevicePath;
        _profile = null;
        _bufferedBytes = 0;
        _droppingUntilSync = false;
        _discardBufferedAfterResync = false;
        _discardBytes = 0;
        _stagedGenuineControl = false;
        HasDeliveredInput = false;
        if (fd >= 0) _system.Close(fd);
        NeutralizeState(keepConnectionMetadata: false);
        if (reason != null)
            _log?.Invoke($"evdev disconnected ({path ?? "unknown"}): {reason}");
    }

    private void LogDiscoveredDevice(EvdevGamepadProfile profile)
    {
        if (_log == null) return;
        var caps = profile.Capabilities;
        var buttons = caps.GetAdvertisedGamepadButtons();
        var buttonNames = new string[buttons.Count];
        for (int i = 0; i < buttons.Count; i++)
            buttonNames[i] = string.Create(CultureInfo.InvariantCulture, $"0x{buttons[i]:X3}");

        var axes = new List<string>();
        foreach (var kv in caps.AbsInfos)
        {
            var role = profile.ClassifyAxis(kv.Key);
            profile.TryGetAxisRange(kv.Key, out var range);
            axes.Add(string.Create(CultureInfo.InvariantCulture,
                $"abs{kv.Key}[{kv.Value.Minimum}..{kv.Value.Maximum},neutral={range.Center},flat={kv.Value.Flat},role={role}]"));
        }

        _log(string.Create(CultureInfo.InvariantCulture,
            $"evdev connected path={caps.DevicePath} id={caps.Id.Vendor:X4}:{caps.Id.Product:X4} name='{caps.Name}' axes=[{string.Join(", ", axes)}] buttons=[{string.Join(", ", buttonNames)}]"));
    }
}
