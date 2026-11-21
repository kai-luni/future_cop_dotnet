namespace FutureCop.View;

/// <summary>Result of a <c>gRegistryFind</c> lookup for one slot id from player +0x90.</summary>
public sealed record SlotRegistryEntry(byte Id, bool Found);

/// <summary>
/// Lookup boundary for <c>gRegistryFind</c>, which resolves the slot icon id at player +0x90.
/// The retail registry itself is not ported yet.
/// </summary>
public interface ISlotRegistry
{
    /// <summary><c>gRegistryFind(id)</c>: queries the registry for <paramref name="id"/>.</summary>
    SlotRegistryEntry Find(byte id);
}

/// <summary>Default <see cref="ISlotRegistry"/> used when no registry is supplied.</summary>
public sealed class NullSlotRegistry : ISlotRegistry
{
    public static readonly NullSlotRegistry Instance = new();
    public SlotRegistryEntry Find(byte id) => new(id, Found: false);
}

/// <summary>
/// PS1 primitive packet emitted by <c>func_8006BF44</c> (<c>gEmitStatusSlotIcon</c>), matching
/// the retail <c>Packet</c> layout (<c>0x1C</c> bytes: texpage <c>0xE1</c> or sprite <c>0x66</c>).
/// </summary>
public readonly record struct SlotPacket(
    uint Addr,
    byte Len,
    byte R,
    byte G,
    byte B,
    byte Code,
    short X,
    short Y,
    byte U,
    byte V,
    ushort Clut,
    ushort W,
    ushort H);

/// <summary>Sink for primitive packets emitted by <c>func_8006BF44</c>.</summary>
public interface ISlotPacketSink
{
    /// <summary>Receives one emitted texpage or sprite packet in emission order.</summary>
    void Emit(SlotPacket packet);
}

/// <summary>
/// Port of <c>func_8006C080</c> (<c>gDrawThreeSlotStatusBar</c>), <c>func_8006BF44</c>
/// (<c>gEmitStatusSlotIcon</c>) and <c>func_800491F8</c>.
/// </summary>
public static class SlotsBarDraw
{
    /// <summary>
    /// <c>func_8006C080</c> (<c>gDrawThreeSlotStatusBar</c>): draws the three status bar slots,
    /// the player invulnerability icon and (when the second game block is present) the trailing icon.
    /// </summary>
    public static void Draw(
        IHudDevice device,
        ISlotRegistry registry,
        ISlotPacketSink packetSink,
        HudPlayerBlock player,
        HudGlobals globals)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(packetSink);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(globals);

        int c1 = (sbyte)player.ValueC1;
        byte invulnerable = c1 >= 0 && c1 < globals.PlayerInvulnerable.Count
            ? globals.PlayerInvulnerable[c1]
            : (byte)0;

        int x = 0x33;
        int spriteX = 0x22;
        for (int i = 0; i < 3; i++, x += 0x29, spriteX += 0x29)
        {
            byte id = i < player.Value90.Count ? unchecked((byte)player.Value90[i]) : (byte)0;
            registry.Find(id);

            short count84 = i < player.Value84.Count ? unchecked((short)player.Value84[i]) : (short)0;
            short count8A = i < player.Value8A.Count ? unchecked((short)player.Value8A[i]) : (short)0;

            int flag = 0;
            if (invulnerable == 0 && count8A > 0)
            {
                flag = 1;
                DrawNumber(device, count8A, x, 0xCE, (globals.FrameTime & 0x40) != 0 ? 0x9 : 0xC);
            }

            EmitStatusSlotIcon(
                packetSink,
                globals.EnvMapTexpageWord,
                globals.FrameTime,
                i,
                (invulnerable != 0 || flag != 0) ? 1 : 0,
                spriteX,
                0xE0);

            DrawNumber(device, invulnerable != 0 ? count8A : count84, x, 0xD8, 8);
        }

        EmitStatusSlotIcon(packetSink, globals.EnvMapTexpageWord, globals.FrameTime, -1, invulnerable, 0xC, 0xE0);

        if (globals.SecondGame is not null)
        {
            EmitStatusSlotIcon(packetSink, globals.EnvMapTexpageWord, globals.FrameTime, -1, 0, 0x124, 0xE0);
        }
    }

    /// <summary>
    /// <c>func_800491F8</c>: formats <paramref name="value"/> with <c>"%ld"</c> and draws it
    /// through <see cref="IHudDevice.Text"/> with <paramref name="colour"/> masked to 16 bits.
    /// </summary>
    public static void DrawNumber(IHudDevice device, int value, int x, int y, int colour)
    {
        ArgumentNullException.ThrowIfNull(device);
        device.Text(HudMath.FormatSlotCount(value), x, y, colour & 0xFFFF);
    }

    /// <summary>
    /// <c>func_8006BF44</c> (<c>gEmitStatusSlotIcon</c>): emits a texpage packet (<c>0xE1</c>)
    /// followed by a 16x16 sprite packet (<c>0x66</c>) at <c>(x, yBase - 16)</c>.
    /// </summary>
    public static void EmitStatusSlotIcon(
        ISlotPacketSink packetSink,
        uint envMapTexpageWord,
        int frameTime,
        int index,
        int highlight,
        int x,
        int yBase)
    {
        ArgumentNullException.ThrowIfNull(packetSink);

        byte u = 0xA6;
        byte v;
        if (index == -1)
        {
            u = 0x96;
            v = 0xBC;
        }
        else
        {
            v = unchecked((byte)((index << 4) + 0x9C));
        }

        uint page = (envMapTexpageWord & 0x9FFu) | 0xE1000600u;
        packetSink.Emit(new SlotPacket(
            Addr: 0,
            Len: 1,
            R: unchecked((byte)(page & 0xFF)),
            G: unchecked((byte)((page >> 8) & 0xFF)),
            B: unchecked((byte)((page >> 16) & 0xFF)),
            Code: unchecked((byte)(page >> 24)),
            X: 0,
            Y: 0,
            U: 0,
            V: 0,
            Clut: 0,
            W: 0,
            H: 0));

        bool lit = highlight != 0 && (frameTime & 0x40) != 0;
        byte r = lit ? (byte)0xC0 : (byte)0x60;
        byte g = lit ? (byte)0xA0 : (byte)0x60;
        byte b = lit ? (byte)0xA0 : (byte)0x60;

        packetSink.Emit(new SlotPacket(
            Addr: 0,
            Len: 4,
            R: r,
            G: g,
            B: b,
            Code: 0x66,
            X: unchecked((short)x),
            Y: unchecked((short)(yBase - 16)),
            U: u,
            V: v,
            Clut: unchecked((ushort)(envMapTexpageWord >> 16)),
            W: 16,
            H: 16));
    }
}
