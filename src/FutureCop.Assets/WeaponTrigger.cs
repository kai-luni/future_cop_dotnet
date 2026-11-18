namespace FutureCop.Assets;

/// <summary>
/// The retail frame clock the weapon trigger and the entity mover share. func_8002703C waits for at
/// least five palette ticks per frame and caps the frame delta at 0x3C, func_8001EFF8 adds five per
/// VSync, so one tick is a 1/300-second unit and a 60 Hz frame carries five of them.
/// </summary>
public static class RetailClock
{
    /// <summary>Ticks per 60 Hz frame (gFrameId, its floor and its typical value).</summary>
    public const int TicksPerFrame = 5;

    /// <summary>Ticks per second of retail time.</summary>
    public const int TicksPerSecond = 300;
}

/// <summary>
/// Cadence of one fire half, straight from the record bytes func_8006BA50 reads.
/// </summary>
/// <param name="Burst">Entry +0x06: shots per reload cycle (the same byte seeds the cycle counter).</param>
/// <param name="ReloadTicks">Entry +0x08: reload timer added to +0x78 when the cycle ends.</param>
/// <param name="GapTicks">Entry +0x0A: timer added after a shot that does not end the cycle; negative
/// values are a credit, so the burst fires on consecutive frames.</param>
public readonly record struct WeaponCadence(int Burst, int ReloadTicks, int GapTicks)
{
    /// <summary>Cadence of a definition half; the retail record guarantees a burst of at least one.</summary>
    public static WeaponCadence From(ShotDefinition shot)
    {
        ArgumentNullException.ThrowIfNull(shot);
        return new WeaponCadence(Math.Max(1, shot.Burst), shot.ReloadTicks, shot.GapTicks);
    }

    /// <summary>Frames between two shots once the cycle has settled, or 0 for a burst.</summary>
    public double ShotsPerSecond(int ticksPerFrame = RetailClock.TicksPerFrame)
    {
        int period = Burst > 1 ? Burst * Math.Max(1, ticksPerFrame) : ReloadTicks;
        // The burst fires on consecutive frames, the reload spans reload / delta frames.
        double frames = Burst > 1
            ? Burst + Math.Max(0, ReloadTicks - (Burst - 1) * GapTicks) / (double)ticksPerFrame
            : 1 + ReloadTicks / (double)ticksPerFrame;
        if (period <= 0) return 60.0;
        return 60.0 / Math.Max(1.0, frames);
    }
}

/// <summary>Per-slot trigger state: the reload timer (+0x78) and the cycle counter (+0x93).</summary>
public struct CadenceState
{
    public CadenceState(int cycle)
    {
        Timer = 0;
        Cycle = cycle;
    }

    /// <summary>Reload timer of the slot (+0x78), in ticks.</summary>
    public int Timer;

    /// <summary>Shots left in the current cycle (+0x93).</summary>
    public int Cycle;
}

/// <summary>
/// Port of the weapon trigger: the timer decrement of func_8006C3E0:59-68 and the fire decision of
/// func_8006BA50:169-217. A caller ticks <see cref="DecrementTimer"/> every frame (retail does it in
/// the player entity update, before the slot update) and asks <see cref="TryFire"/> once per slot.
/// </summary>
public static class WeaponTrigger
{
    /// <summary>
    /// func_8006C3E0: subtract the frame delta from positive timers and clamp at zero. A timer that
    /// is already zero or negative is left alone - that is what lets a negative gap fire a burst.
    /// </summary>
    public static void DecrementTimer(ref CadenceState state, int delta)
    {
        if (state.Timer <= 0) return;
        state.Timer -= delta;
        if (state.Timer < 0) state.Timer = 0;
    }

    /// <summary>
    /// func_8006BA50:169-217: a positive timer means the weapon is still reloading. Otherwise the
    /// shot fires, the cycle counter drops by one and either the burst reloads (cycle reached zero)
    /// or the gap is credited.
    /// </summary>
    public static bool TryFire(ref CadenceState state, WeaponCadence cadence)
    {
        if (state.Timer > 0) return false;

        if (--state.Cycle == 0)
        {
            state.Cycle = Math.Max(1, cadence.Burst);
            state.Timer += cadence.ReloadTicks;
        }
        else
        {
            state.Timer += cadence.GapTicks;
        }
        return true;
    }
}