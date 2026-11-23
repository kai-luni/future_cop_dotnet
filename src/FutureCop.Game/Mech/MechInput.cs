namespace FutureCop.Game.Mech;

/// <summary>
/// One PS1 controller as func_8001E7B8 sees it: pad type nibble (7 = analog, func_8001EF34) and
/// the raw left-stick bytes of D_800A78F0[port] (+6 x, +7 y; 0x80 = centre).
/// </summary>
public readonly record struct AnalogPad(int Type, byte LeftX, byte LeftY)
{
    public static AnalogPad Digital => new(4, 0x80, 0x80);
    public static AnalogPad Centred => new(7, 0x80, 0x80);
}

/// <summary>PS1 digital buttons in pad-response order (bit set = held, the inverted D_800A78F0 word &gt;&gt; 16).</summary>
[Flags]
public enum PadButtons : ushort
{
    None = 0,
    Select = 1 << 0, Start = 1 << 3, Up = 1 << 4, Right = 1 << 5, Down = 1 << 6, Left = 1 << 7,
    L2 = 1 << 8, R2 = 1 << 9, L1 = 1 << 10, R1 = 1 << 11,
    Triangle = 1 << 12, Circle = 1 << 13, Cross = 1 << 14, Square = 1 << 15,
}

/// <summary>
/// D_800AAC50 per port: 14 button masks over the polled word (func_8001EF8C: ~raw, buttons in
/// bits 16..31). <see cref="Default"/> is what func_8001E588 writes; <see cref="Preset"/> is the
/// controller-configuration option func_8007BFCC (button layouts only - no dead zone or curve).
/// </summary>
public sealed class PadMapping
{
    private readonly uint[] _masks;

    public PadMapping(IReadOnlyList<uint> masks)
    {
        if (masks.Count != 14) throw new ArgumentException("a pad mapping has 14 masks", nameof(masks));
        _masks = masks.ToArray();
    }

    public uint Mask(int slot) => _masks[slot];

    /// <summary>func_8001E588 gInitPadMapping.</summary>
    public static PadMapping Default { get; } = new(new uint[]
    {
        0x100000, 0x400000, 0x800000, 0x200000, 0x80000000, 0x40000000, 0x20000000,
        0x10000000, 0x8000000, 0x4000000, 0x1000000, 0x2000000, 0x10000, 0x80000,
    });

    /// <summary>D_8009BEF4[i].value (stride 12, word at +8), read from SLUS_007.39.</summary>
    private static readonly uint[] PresetValues =
    {
        0x1000000, 0x4000000, 0x100000, 0x800000, 0x400000, 0x200000, 0x10000,
        0x2000000, 0x8000000, 0x10000000, 0x20000000, 0x40000000, 0x80000000, 0x80000,
    };

    /// <summary>D_8009BFD4: four presets of 14 slot indices, read from SLUS_007.39.</summary>
    private static readonly byte[] PresetSlots =
    {
        10, 9, 0, 2, 1, 3, 12, 11, 8, 7, 6, 5, 4, 13,
        9, 10, 0, 2, 1, 3, 12, 8, 11, 7, 6, 5, 4, 13,
        10, 9, 0, 2, 1, 3, 12, 11, 8, 5, 4, 7, 6, 13,
        9, 10, 0, 2, 1, 3, 12, 8, 11, 5, 4, 7, 6, 13,
    };

    /// <summary>
    /// func_8007BFCC gApplyControllerPreset(port, value): value clamped to 0..3, slot
    /// D_8009BFD4[value * 14 + i] receives D_8009BEF4[i].value. Preset 0 (the boot default,
    /// func_80076A2C) equals <see cref="Default"/>; 1 swaps L1/L2 and R1/R2, 2 swaps
    /// Square/Circle and Cross/Triangle, 3 does both.
    /// </summary>
    public static PadMapping Preset(int value)
    {
        value = value < 0 ? 0 : value > 3 ? 3 : value;
        var masks = new uint[14];
        for (int i = 0; i < 14; i++) masks[PresetSlots[value * 14 + i]] = PresetValues[i];
        return new PadMapping(masks);
    }
}

/// <summary>Controller path of the player mech (ports of the retail input units).</summary>
public static class MechInput
{
    /// <summary>
    /// func_8001E620 gMapPadButtons on the word func_8001EF8C returns for <paramref name="buttons"/>
    /// (D_8009F7C0[port] as func_80057864 stores it). A missing pad (type 0) yields 0 in retail.
    /// </summary>
    public static uint MapPadButtons(ushort buttons, PadMapping map)
    {
        uint pressed = (uint)buttons << 16;
        uint result = (pressed & map.Mask(0)) != 0 ? 0x40000000u : 0;
        if ((pressed & map.Mask(1)) != 0) result |= 0x80000000;
        if ((pressed & map.Mask(2)) != 0) result |= 0x10000000;
        if ((pressed & map.Mask(3)) != 0) result |= 0x20000000;
        if ((pressed & map.Mask(4)) != 0) result |= 0x08000000;
        if ((pressed & map.Mask(6)) != 0) result |= 0x02000000;
        if ((pressed & map.Mask(5)) != 0) result |= 0x04000000;
        if ((pressed & map.Mask(7)) != 0) result |= 0x01000000;
        if ((pressed & map.Mask(13)) != 0) result |= 0x00080000;
        if ((pressed & map.Mask(12)) != 0) result |= 0x00040000;
        if ((pressed & map.Mask(8)) != 0) result |= 0x00800000;
        if ((pressed & map.Mask(9)) != 0) result |= 0x00400000;
        if ((pressed & map.Mask(11)) != 0) result |= 0x00200000;
        if ((pressed & map.Mask(10)) != 0) result |= 0x00100000;
        return result;
    }

    /// <summary>
    /// func_80057864: refreshes the two mapped controller words D_8009F7C0[0..1] via
    /// func_8001E620 (<see cref="MapPadButtons"/>).
    /// </summary>
    public static void RefreshPadWords(Span<uint> padWords, PadButtons player0, PadMapping map0,
        PadButtons player1 = PadButtons.None, PadMapping? map1 = null)
    {
        if (padWords.Length < 2) throw new ArgumentException("D_8009F7C0 stores two pad words", nameof(padWords));
        padWords[0] = MapPadButtons((ushort)player0, map0);
        padWords[1] = MapPadButtons((ushort)player1, map1 ?? map0);
    }

    /// <summary>
    /// func_800576A8 gTranslatePadButtons: the mask gUpdatePlayerEntity stores at state + 0x30.
    /// With one player the three permuted bits of the second pad word are OR-ed in as well.
    /// </summary>
    public static uint TranslatePadButtons(uint word, int players, uint secondWord)
    {
        uint result = (word & 0x80000) != 0 ? 0x80000u : 0;
        if ((word & 0x40000) != 0) result |= 0x40000;
        if ((word & 0x40000000) != 0) result |= 0x40000000;
        if ((word & 0x80000000) != 0) result |= 0x80000000;
        if ((word & 0x10000000) != 0) result |= 0x10000000;
        if ((word & 0x20000000) != 0) result |= 0x20000000;
        if ((word & 0x08000000) != 0) result |= 0x04000000;
        if ((word & 0x04000000) != 0) result |= 0x00400000;
        if ((word & 0x02000000) != 0) result |= 0x01000000;
        if ((word & 0x01000000) != 0) result |= 0x08000000;
        if ((word & 0x00400000) != 0) result |= 0x02000000;
        if ((word & 0x00800000) != 0) result |= 0x00800000;
        if ((word & 0x00100000) != 0) result |= 0x00100000;
        if ((word & 0x00200000) != 0) result |= 0x00200000;
        if (players == 1)
        {
            if ((secondWord & 0x08000000) != 0) result |= 0x04000000;
            if ((secondWord & 0x04000000) != 0) result |= 0x00400000;
            if ((secondWord & 0x02000000) != 0) result |= 0x01000000;
        }
        return result;
    }

    /// <summary>
    /// func_800538C8 with func_8001E7B8: analog left stick to direction bits (OR-ed into
    /// <paramref name="flags"/>) and absolute magnitudes. Returns the read status (0 = nothing read:
    /// a direction nibble was already set, the unit is locked (+0xC0) or frozen (+0x310), or the
    /// pad is not analog); the magnitudes are only written on a successful read.
    /// </summary>
    public static int ReadStick(AnalogPad pad, bool locked, bool frozen, ref uint flags, ref int magV, ref int magH)
    {
        if ((flags & 0xF0000000) != 0 || locked || frozen) return 0;
        if (pad.Type != 7) return 0;
        int x = pad.LeftX - 0x80, y = pad.LeftY - 0x80;
        uint direction = 0;
        x = Clamp(x);
        if (x > 0) direction |= 0x20000000; else if (x < 0) direction |= 0x10000000;
        y = Clamp(y);
        if (y > 0) direction |= 0x80000000; else if (y < 0) direction |= 0x40000000;
        flags |= direction;
        magV = Math.Abs(y);
        magH = Math.Abs(x);
        return 1;

        static int Clamp(int a)
        {
            if ((uint)(a + 0x27) < 0x4F) return 0;
            if (a >= 0xE1) return 0x100;
            if (a < -0xE0) return -0x100;
            return a;
        }
    }
}
