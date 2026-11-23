#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using FutureCop.Game.Mech;

namespace FcMainMenu;

/// <summary>Queried evdev device identity, bitmasks and EV_ABS axis capabilities.</summary>
public sealed class EvdevDeviceCapabilities
{
    public EvdevDeviceCapabilities(
        string devicePath,
        EvdevInputId id,
        string name,
        byte[] evBits,
        byte[] keyBits,
        byte[] absBits,
        IReadOnlyDictionary<int, EvdevAbsInfo> absInfos)
    {
        DevicePath = devicePath;
        Id = id;
        Name = name;
        EvBits = evBits;
        KeyBits = keyBits;
        AbsBits = absBits;
        AbsInfos = absInfos;
    }

    public string DevicePath { get; }
    public EvdevInputId Id { get; }
    public string Name { get; }
    public byte[] EvBits { get; }
    public byte[] KeyBits { get; }
    public byte[] AbsBits { get; }
    public IReadOnlyDictionary<int, EvdevAbsInfo> AbsInfos { get; }

    public static bool HasBit(ReadOnlySpan<byte> bitmap, int bit) =>
        bit >= 0 && (bit >> 3) < bitmap.Length && (bitmap[bit >> 3] & (1 << (bit & 7))) != 0;

    public bool SupportsEventType(int evType) => HasBit(EvBits, evType);
    public bool SupportsKey(int keyCode) => HasBit(KeyBits, keyCode);
    public bool SupportsAbs(int axis) => HasBit(AbsBits, axis);

    public IReadOnlyList<ushort> GetAdvertisedGamepadButtons()
    {
        var list = new List<ushort>();
        for (int code = EvdevCodes.BtnTrigger; code <= EvdevCodes.BtnDead; code++)
            if (SupportsKey(code)) list.Add((ushort)code);
        for (int code = EvdevCodes.BtnGamepad; code <= EvdevCodes.BtnThumbR; code++)
            if (SupportsKey(code)) list.Add((ushort)code);
        for (int code = EvdevCodes.BtnDpadUp; code <= EvdevCodes.BtnDpadRight; code++)
            if (SupportsKey(code)) list.Add((ushort)code);
        return list;
    }
}

/// <summary>Role assigned to a supported EV_ABS axis from device capabilities.</summary>
public enum EvdevAxisRole
{
    Ignored,
    DigitalDpadX,
    DigitalDpadY,
    AnalogStickX,
    AnalogStickY,
}

/// <summary>
/// Safe 64-bit capability range math for signed/unsigned EV_ABS axes (handles int.MinValue..int.MaxValue
/// without overflow and small [-1, 1] hats without deadzone lockout).
/// </summary>
public readonly record struct EvdevAxisRange(int Minimum, int Maximum, int Flat)
{
    public bool IsValid => Maximum > Minimum;

    public long Center
    {
        get
        {
            if (!IsValid) return Minimum;
            if (Minimum < 0 && Maximum > 0 && Math.Abs((long)Minimum + Maximum) <= 1)
                return 0;
            return (long)Math.Round(((double)Minimum + Maximum) / 2.0, MidpointRounding.AwayFromZero);
        }
    }

    public long NegSpan => IsValid ? Center - Minimum : 0;
    public long PosSpan => IsValid ? Maximum - Center : 0;

    public long NegDpadDeadzone => ComputeDpadDeadzone(NegSpan, Flat);
    public long PosDpadDeadzone => ComputeDpadDeadzone(PosSpan, Flat);

    private static long ComputeDpadDeadzone(long span, int flat)
    {
        if (span <= 2) return 0;
        long pct = Math.Max(1, span / 4);
        long safeFlat = Math.Max(0, (long)flat);
        return Math.Min(span - 1, Math.Max(pct, safeFlat));
    }

    private static long ComputeAnalogDeadzone(long span, int flat)
    {
        if (span <= 2) return 0;
        long pct = Math.Max(1, span / 10);
        long safeFlat = Math.Max(0, (long)flat);
        return Math.Min(span - 1, Math.Max(pct, safeFlat));
    }

    public int ReadDpadDirection(int value)
    {
        if (!IsValid) return 0;
        long clamped = Math.Clamp((long)value, Minimum, Maximum);
        long delta = clamped - Center;
        if (delta < -NegDpadDeadzone) return -1;
        if (delta > PosDpadDeadzone) return 1;
        return 0;
    }

    public byte ReadAnalogByte(int value)
    {
        if (!IsValid) return 0x80;
        long clamped = Math.Clamp((long)value, Minimum, Maximum);
        long delta = clamped - Center;
        if (delta < 0)
        {
            long span = NegSpan;
            long dz = ComputeAnalogDeadzone(span, Flat);
            if (-delta <= dz) return 0x80;
            double scaled = (-delta) * 128.0 / span;
            return (byte)Math.Clamp(128 - (int)Math.Round(scaled), 0, 127);
        }
        if (delta > 0)
        {
            long span = PosSpan;
            long dz = ComputeAnalogDeadzone(span, Flat);
            if (delta <= dz) return 0x80;
            double scaled = delta * 127.0 / span;
            return (byte)Math.Clamp(128 + (int)Math.Round(scaled), 129, 255);
        }
        return 0x80;
    }
}

/// <summary>
/// Capability-driven device matching, axis classification, and explicit EV_KEY -> PS1 PadButtons mapping.
/// </summary>
public sealed class EvdevGamepadProfile
{
    private readonly Dictionary<int, EvdevAxisRole> _axisRoles = new();
    private readonly Dictionary<int, EvdevAxisRange> _axisRanges = new();
    private readonly int? _analogAxisX;
    private readonly int? _analogAxisY;

    private EvdevGamepadProfile(
        EvdevDeviceCapabilities capabilities,
        bool usesAnalogStick,
        int? analogAxisX,
        int? analogAxisY)
    {
        Capabilities = capabilities;
        UsesAnalogStick = usesAnalogStick;
        _analogAxisX = analogAxisX;
        _analogAxisY = analogAxisY;
    }

    public EvdevDeviceCapabilities Capabilities { get; }
    public bool UsesAnalogStick { get; }

    public static int ScoreCandidate(EvdevDeviceCapabilities caps)
    {
        if (!caps.SupportsEventType(EvdevCodes.EvKey)) return 0;

        bool hasJoystickButtons = false;
        for (int c = EvdevCodes.BtnTrigger; c <= EvdevCodes.BtnDead; c++)
            if (caps.SupportsKey(c)) { hasJoystickButtons = true; break; }

        bool hasGamepadButtons = false;
        for (int c = EvdevCodes.BtnGamepad; c <= EvdevCodes.BtnThumbR; c++)
            if (caps.SupportsKey(c)) { hasGamepadButtons = true; break; }

        bool hasKeyDpad = caps.SupportsKey(EvdevCodes.BtnDpadUp) || caps.SupportsKey(EvdevCodes.BtnDpadDown)
            || caps.SupportsKey(EvdevCodes.BtnDpadLeft) || caps.SupportsKey(EvdevCodes.BtnDpadRight);

        if (!hasJoystickButtons && !hasGamepadButtons) return 0;

        bool hasAbs = caps.SupportsEventType(EvdevCodes.EvAbs);
        bool hasPrimaryXY = hasAbs && caps.SupportsAbs(EvdevCodes.AbsX) && caps.SupportsAbs(EvdevCodes.AbsY);
        bool hasHatXY = hasAbs && caps.SupportsAbs(EvdevCodes.AbsHat0X) && caps.SupportsAbs(EvdevCodes.AbsHat0Y);
        if (!hasPrimaryXY && !hasHatXY && !hasKeyDpad) return 0;

        int score = 10;
        if (caps.Id.Vendor == 0x0079 && caps.Id.Product == 0x0011) score += 1000;
        if (caps.Name.Contains("Gamepad", StringComparison.OrdinalIgnoreCase)
            || caps.Name.Contains("Joystick", StringComparison.OrdinalIgnoreCase)
            || caps.Name.Contains("Controller", StringComparison.OrdinalIgnoreCase))
            score += 100;
        if (hasJoystickButtons || hasGamepadButtons) score += 50;
        if (hasPrimaryXY) score += 10;
        if (hasHatXY) score += 10;
        return score;
    }

    public static EvdevGamepadProfile Create(EvdevDeviceCapabilities caps)
    {
        bool hasHat = caps.SupportsAbs(EvdevCodes.AbsHat0X) && caps.SupportsAbs(EvdevCodes.AbsHat0Y);
        bool hasSecondaryStick = caps.SupportsAbs(EvdevCodes.AbsRx) || caps.SupportsAbs(EvdevCodes.AbsRy)
            || caps.SupportsAbs(EvdevCodes.AbsZ) || caps.SupportsAbs(EvdevCodes.AbsRz);
        bool hasDigitalDpadButtons = caps.SupportsKey(EvdevCodes.BtnDpadUp) || caps.SupportsKey(EvdevCodes.BtnDpadDown)
            || caps.SupportsKey(EvdevCodes.BtnDpadLeft) || caps.SupportsKey(EvdevCodes.BtnDpadRight);
        bool isKnownDigitalPad = caps.Id.Vendor == 0x0079 && caps.Id.Product == 0x0011;

        bool hasX = caps.AbsInfos.TryGetValue(EvdevCodes.AbsX, out var infoX);
        bool hasY = caps.AbsInfos.TryGetValue(EvdevCodes.AbsY, out var infoY);
        bool hasPrimaryXY = hasX && hasY;
        long rangeX = hasPrimaryXY ? (long)infoX.Maximum - infoX.Minimum : 0;
        long rangeY = hasPrimaryXY ? (long)infoY.Maximum - infoY.Minimum : 0;

        bool primaryIsDpad = hasPrimaryXY && (
            isKnownDigitalPad
            || (!hasHat && !hasSecondaryStick && !hasDigitalDpadButtons)
            || (rangeX <= 4 && rangeY <= 4));

        bool primaryIsAnalog = hasPrimaryXY && !primaryIsDpad && rangeX > 4 && rangeY > 4;

        var profile = new EvdevGamepadProfile(
            caps,
            usesAnalogStick: primaryIsAnalog,
            analogAxisX: primaryIsAnalog ? EvdevCodes.AbsX : null,
            analogAxisY: primaryIsAnalog ? EvdevCodes.AbsY : null);

        foreach (var kv in caps.AbsInfos)
        {
            int axis = kv.Key;
            var info = kv.Value;
            profile._axisRanges[axis] = new EvdevAxisRange(info.Minimum, info.Maximum, info.Flat);
            EvdevAxisRole role = axis switch
            {
                EvdevCodes.AbsHat0X => EvdevAxisRole.DigitalDpadX,
                EvdevCodes.AbsHat0Y => EvdevAxisRole.DigitalDpadY,
                EvdevCodes.AbsX when primaryIsDpad => EvdevAxisRole.DigitalDpadX,
                EvdevCodes.AbsY when primaryIsDpad => EvdevAxisRole.DigitalDpadY,
                EvdevCodes.AbsX when primaryIsAnalog => EvdevAxisRole.AnalogStickX,
                EvdevCodes.AbsY when primaryIsAnalog => EvdevAxisRole.AnalogStickY,
                _ => EvdevAxisRole.Ignored,
            };
            profile._axisRoles[axis] = role;
        }

        return profile;
    }

    public EvdevAxisRole ClassifyAxis(int axis) =>
        _axisRoles.TryGetValue(axis, out var role) ? role : EvdevAxisRole.Ignored;

    public bool TryGetAxisRange(int axis, out EvdevAxisRange range) =>
        _axisRanges.TryGetValue(axis, out range);

    /// <summary>
    /// Explicit mapping from Linux evdev button codes to PS1 <see cref="PadButtons"/>.
    /// Assumptions for unmeasured physical labels on 0079:0011 (12 HID joystick buttons 0x120..0x12B):
    /// 0x120..0x123 -> Triangle, Circle, Cross, Square; 0x124..0x127 -> L1, R1, L2, R2;
    /// 0x128..0x129 -> Select, Start; 0x12A..0x12B -> unmapped (tracked in raw codes for identification).
    /// Standard BTN_GAMEPAD (0x130..0x13B) and BTN_DPAD_* (0x220..0x223) are also mapped explicitly.
    /// </summary>
    public static PadButtons MapButton(ushort keyCode) => keyCode switch
    {
        EvdevCodes.BtnTrigger => PadButtons.Triangle,
        EvdevCodes.BtnThumb => PadButtons.Circle,
        EvdevCodes.BtnThumb2 => PadButtons.Cross,
        EvdevCodes.BtnTop => PadButtons.Square,
        EvdevCodes.BtnTop2 => PadButtons.L1,
        EvdevCodes.BtnPinkie => PadButtons.R1,
        EvdevCodes.BtnBase => PadButtons.L2,
        EvdevCodes.BtnBase2 => PadButtons.R2,
        EvdevCodes.BtnBase3 => PadButtons.Select,
        EvdevCodes.BtnBase4 => PadButtons.Start,
        EvdevCodes.BtnBase5 => PadButtons.None,
        EvdevCodes.BtnBase6 => PadButtons.None,

        EvdevCodes.BtnSouth => PadButtons.Cross,
        EvdevCodes.BtnEast => PadButtons.Circle,
        EvdevCodes.BtnNorth => PadButtons.Triangle,
        EvdevCodes.BtnWest => PadButtons.Square,
        EvdevCodes.BtnTl => PadButtons.L1,
        EvdevCodes.BtnTr => PadButtons.R1,
        EvdevCodes.BtnTl2 => PadButtons.L2,
        EvdevCodes.BtnTr2 => PadButtons.R2,
        EvdevCodes.BtnSelect => PadButtons.Select,
        EvdevCodes.BtnStart => PadButtons.Start,

        EvdevCodes.BtnDpadUp => PadButtons.Up,
        EvdevCodes.BtnDpadDown => PadButtons.Down,
        EvdevCodes.BtnDpadLeft => PadButtons.Left,
        EvdevCodes.BtnDpadRight => PadButtons.Right,
        _ => PadButtons.None,
    };

    public static string DescribeKeyCode(ushort keyCode) => keyCode switch
    {
        EvdevCodes.BtnTrigger => "BTN_TRIGGER",
        EvdevCodes.BtnThumb => "BTN_THUMB",
        EvdevCodes.BtnThumb2 => "BTN_THUMB2",
        EvdevCodes.BtnTop => "BTN_TOP",
        EvdevCodes.BtnTop2 => "BTN_TOP2",
        EvdevCodes.BtnPinkie => "BTN_PINKIE",
        EvdevCodes.BtnBase => "BTN_BASE",
        EvdevCodes.BtnBase2 => "BTN_BASE2",
        EvdevCodes.BtnBase3 => "BTN_BASE3",
        EvdevCodes.BtnBase4 => "BTN_BASE4",
        EvdevCodes.BtnBase5 => "BTN_BASE5",
        EvdevCodes.BtnBase6 => "BTN_BASE6",
        EvdevCodes.BtnSouth => "BTN_SOUTH",
        EvdevCodes.BtnEast => "BTN_EAST",
        EvdevCodes.BtnC => "BTN_C",
        EvdevCodes.BtnNorth => "BTN_NORTH",
        EvdevCodes.BtnWest => "BTN_WEST",
        EvdevCodes.BtnZ => "BTN_Z",
        EvdevCodes.BtnTl => "BTN_TL",
        EvdevCodes.BtnTr => "BTN_TR",
        EvdevCodes.BtnTl2 => "BTN_TL2",
        EvdevCodes.BtnTr2 => "BTN_TR2",
        EvdevCodes.BtnSelect => "BTN_SELECT",
        EvdevCodes.BtnStart => "BTN_START",
        EvdevCodes.BtnMode => "BTN_MODE",
        EvdevCodes.BtnThumbL => "BTN_THUMBL",
        EvdevCodes.BtnThumbR => "BTN_THUMBR",
        EvdevCodes.BtnDpadUp => "BTN_DPAD_UP",
        EvdevCodes.BtnDpadDown => "BTN_DPAD_DOWN",
        EvdevCodes.BtnDpadLeft => "BTN_DPAD_LEFT",
        EvdevCodes.BtnDpadRight => "BTN_DPAD_RIGHT",
        _ => string.Create(CultureInfo.InvariantCulture, $"KEY_0x{keyCode:X3}"),
    };

    public PadButtons EvaluateButtons(ReadOnlySpan<bool> keyBitmap, ReadOnlySpan<int> absValues)
    {
        PadButtons buttons = PadButtons.None;

        for (int code = EvdevCodes.BtnTrigger; code <= EvdevCodes.BtnThumbR; code++)
        {
            if (code < keyBitmap.Length && keyBitmap[code])
                buttons |= MapButton((ushort)code);
        }
        for (int code = EvdevCodes.BtnDpadUp; code <= EvdevCodes.BtnDpadRight; code++)
        {
            if (code < keyBitmap.Length && keyBitmap[code])
                buttons |= MapButton((ushort)code);
        }

        foreach (var kv in _axisRoles)
        {
            int axis = kv.Key;
            if ((uint)axis >= (uint)absValues.Length) continue;
            if (kv.Value != EvdevAxisRole.DigitalDpadX && kv.Value != EvdevAxisRole.DigitalDpadY) continue;
            if (!_axisRanges.TryGetValue(axis, out var range)) continue;

            int dir = range.ReadDpadDirection(absValues[axis]);
            if (kv.Value == EvdevAxisRole.DigitalDpadX)
            {
                if (dir < 0) buttons |= PadButtons.Left;
                else if (dir > 0) buttons |= PadButtons.Right;
            }
            else
            {
                if (dir < 0) buttons |= PadButtons.Up;
                else if (dir > 0) buttons |= PadButtons.Down;
            }
        }

        return buttons;
    }

    public AnalogPad EvaluateAnalogPad(ReadOnlySpan<int> absValues)
    {
        if (!UsesAnalogStick || _analogAxisX is not int ax || _analogAxisY is not int ay)
            return AnalogPad.Digital;
        if (!_axisRanges.TryGetValue(ax, out var rx) || !_axisRanges.TryGetValue(ay, out var ry))
            return AnalogPad.Digital;

        byte lx = (uint)ax < (uint)absValues.Length ? rx.ReadAnalogByte(absValues[ax]) : (byte)0x80;
        byte ly = (uint)ay < (uint)absValues.Length ? ry.ReadAnalogByte(absValues[ay]) : (byte)0x80;
        return new AnalogPad(7, lx, ly);
    }
}
