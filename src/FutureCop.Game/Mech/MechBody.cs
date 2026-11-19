using FutureCop.Assets;

namespace FutureCop.Game.Mech;

/// <summary>
/// Animation track of func_800448D8 / func_80044B34 (entity + 0xC8: clips, clip, elapsed, frame,
/// target, step, index, count, flags). <see cref="ClipIndex"/> replaces the clip pointer
/// (-1 = null); <see cref="Clips"/> is the AnmD table of the walker model (null = no table).
/// </summary>
public sealed class AnimTrack
{
    public IReadOnlyList<AnimationClip>? Clips { get; set; }
    public int ClipIndex { get; set; }
    public int Elapsed { get; set; }
    public short Frame { get; set; }
    public short Target { get; set; }
    public sbyte Step { get; set; }
    public byte Index { get; set; }
    public byte Count { get; set; }
    public byte Flags { get; set; }

    /// <summary>The clip record the track points at.</summary>
    public AnimationClip Clip => Clips![ClipIndex];
}

/// <summary>
/// The walker's entity block (func_800549F0 "self") and the fields of its state block (self+0xAC)
/// that the locomotion tick reads or writes. Offsets are the retail ones; names are descriptive
/// only where the decomp documents the role, otherwise neutral.
/// </summary>
public sealed class MechBody
{
    // ---- entity block -------------------------------------------------------------------
    /// <summary>+0x0C: four queued script commands {op, arg} read by func_800449F8.</summary>
    public byte[] Commands { get; } = new byte[8];
    /// <summary>+0x1E: s16 scale of the fall damage (func_80052F54).</summary>
    public short Health { get; set; }
    /// <summary>+0x2C: 3x3 basis (16.16) rebuilt by func_80051C50 for the footprint test.</summary>
    public int[] Basis { get; } = new int[9];
    /// <summary>+0x50: world position (y up).</summary>
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    /// <summary>+0x78: velocity copied from the state block at the end of the tick.</summary>
    public int VelocityX { get; set; }
    public int VelocityY { get; set; }
    public int VelocityZ { get; set; }
    /// <summary>+0x84: |velocity| &gt;&gt; 11 (turn damping input).</summary>
    public int Speed { get; set; }
    /// <summary>+0xA0: facing axis (sin, 0, cos of <see cref="Angle"/>).</summary>
    public int AxisX { get; set; }
    public int AxisY { get; set; }
    public int AxisZ { get; set; }
    /// <summary>+0xB0: 12-bit heading.</summary>
    public ushort Angle { get; set; }
    /// <summary>+0xB8: last wall side bits of func_80052708 (consumed by func_80052E28).</summary>
    public byte Wall { get; set; }
    /// <summary>+0xB9: bit0 airborne, bit1 landed latch, bit3 high-ledge, bit4 action, bit6 steep, bit7 crouch shadow.</summary>
    public byte Bits { get; set; }
    /// <summary>+0xBA: cached clip index.</summary>
    public byte Clip { get; set; }
    /// <summary>+0xBB: locomotion state (0 idle, 1..8 ground moves, 9..0x12 crouch/jump/fall/land).</summary>
    public byte State { get; set; }
    /// <summary>+0xC2: jump timer.</summary>
    public byte Timer { get; set; }
    /// <summary>+0xC3: idle counter.</summary>
    public byte Idle { get; set; }
    /// <summary>+0xC4: the dynamic ground object the unit stands on (func_80053514); only null is ported.</summary>
    public object? Platform { get; set; }
    /// <summary>+0xC8: the body animation track.</summary>
    public AnimTrack Track { get; } = new() { ClipIndex = -1 };
    /// <summary>+0xF0: s16 roll angle of the basis (also the form-change lift bookkeeping).</summary>
    public short Lift { get; set; }

    // ---- state block (+0xAC) -------------------------------------------------------------
    /// <summary>+0xAD: current form (0 walker, 1 hover).</summary>
    public sbyte Form { get; set; }
    /// <summary>+0xC0: form-change direction; non-zero blocks the stick read.</summary>
    public sbyte Lock { get; set; }
    /// <summary>+0xC1: player slot / controller port.</summary>
    public sbyte Port { get; set; }
    /// <summary>+0xDE: set by surface attribute 1 (sinking/held); pins the height.</summary>
    public byte Grab { get; set; }
    /// <summary>+0xDF: bit0 selects the stronger jump impulses.</summary>
    public byte Boost { get; set; }
    /// <summary>+0xEC: four footprint corners (x, y, z s16, model units) from func_800518A8.</summary>
    public short[] Footprint { get; } = new short[12];
    /// <summary>+0x10C: footprint corners in world space after the last wall test.</summary>
    public int[] FootprintWorld { get; } = new int[12];
    /// <summary>+0x146: set by surface attribute 1.</summary>
    public byte Hazard { get; set; }
    /// <summary>+0x147: slowdown level 0..3 from surface attributes 3/4/5.</summary>
    public byte Slow { get; set; }
    /// <summary>+0x150: pending knockback impulse.</summary>
    public int ImpulseX { get; set; }
    public int ImpulseY { get; set; }
    public int ImpulseZ { get; set; }
    /// <summary>+0x15C: integrated velocity (position += value &gt;&gt; 8 per step).</summary>
    public int MotionX { get; set; }
    public int MotionY { get; set; }
    public int MotionZ { get; set; }
    /// <summary>+0x168: low-pass filtered ground normal.</summary>
    public int NormalX { get; set; }
    public int NormalY { get; set; }
    public int NormalZ { get; set; }
    /// <summary>+0x2FD: requested form.</summary>
    public byte Request { get; set; }
    /// <summary>+0x310: non-zero blocks the stick read.</summary>
    public sbyte Frozen { get; set; }
    /// <summary>+0x326: s16 timer cleared by surface attribute 1.</summary>
    public short HitTimer { get; set; }
    /// <summary>state + 0x2DC record + 0xA / + 0x10: the two s16 written by func_80051AC8.</summary>
    public short BoundsHigh { get; set; }
    public short BoundsLow { get; set; }
}
