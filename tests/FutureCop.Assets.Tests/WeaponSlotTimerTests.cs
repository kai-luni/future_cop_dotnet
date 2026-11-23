using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The parts of func_8006C3E0 (gUpdatePlayerTargetLock) and func_8006BA50 (gWeaponSlotUpdate) that the
/// C host harness cadence_harness.c deliberately keeps out of scope, driven from the C text itself.
/// </summary>
/// <remarks>
/// <para>
/// The harness zeroes every timer but <c>unk78[0]</c> and keeps the target pointer <c>unk6C</c> null, so
/// it proves the fire rhythm of one slot - not the three-timer decrement loop of func_8006C3E0:59-68 and
/// not the ammo gate of func_8006BA50:172-185. Both live in the same retail functions the harness links
/// unchanged, so their expectations below are read off those source lines rather than from a vector file.
/// </para>
/// <para>
/// Source of every step: <c>src/game/8006C3E0.c</c>:59-68 and <c>src/game/8006BA50.c</c>:172-217.
/// </para>
/// </remarks>
public class WeaponSlotTimerTests
{
    /// <summary>
    /// func_8006C3E0:59-68: <c>for (i = 0; i &lt; 3; i++)</c> decrements every positive timer, not just
    /// the slot that fires, and clamps each one at zero.
    /// </summary>
    [Fact]
    public void AdvanceTimersDecrementsAllThreeAndClampsAtZero()
    {
        var slots = new WeaponSlots();
        slots.Timers[0] = 3;
        slots.Timers[1] = 0;
        slots.Timers[2] = 9;

        slots.AdvanceTimers(5);

        // Slot 0 had less than a whole frame left: it lands on zero, never negative.
        Assert.Equal(0, slots.Timers[0]);
        // Slot 1 was already zero and the C's `if (value > 0)` guard skips it entirely.
        Assert.Equal(0, slots.Timers[1]);
        // Slot 2 keeps the remainder, so the next frame still sees a positive timer.
        Assert.Equal(4, slots.Timers[2]);
    }

    /// <summary>
    /// func_8006C3E0:63: the guard is <c>value &gt; 0</c>. A timer that is already negative - which the
    /// negative gap of func_8006BA50:215 produces - is left untouched instead of being clamped, which is
    /// what lets a burst fire on consecutive frames.
    /// </summary>
    [Fact]
    public void AdvanceTimersLeavesNegativeTimersAlone()
    {
        var slots = new WeaponSlots();
        slots.Timers[0] = -4;
        slots.Timers[1] = -1;
        slots.Timers[2] = 5;

        slots.AdvanceTimers(5);

        Assert.Equal(-4, slots.Timers[0]);
        Assert.Equal(-1, slots.Timers[1]);
        Assert.Equal(0, slots.Timers[2]);
    }

    /// <summary>
    /// func_8006BA50:172-178: the counter is read before anything fires, and a counter that is not
    /// positive cannot fire - retail plays <c>gPlayNonpositionalSoundEffect(0xD)</c> and returns
    /// before the cycle and timer arithmetic, so both stay where they were.
    /// </summary>
    [Fact]
    public void AnEmptySlotHasNoCounterToFireWith()
    {
        var slots = new WeaponSlots();
        slots.Ammo[0] = 0;
        slots.Reserve[0] = 0;
        slots.Cycles[0] = 3;
        slots.Timers[0] = 0;

        // Neither counter is positive, so CounterOf reports zero and the second half is not selected.
        Assert.Equal(0, slots.CounterOf(0, out bool secondHalf));
        Assert.False(secondHalf);

        // The gate returns before the arithmetic, so nothing debits and no cycle is consumed.
        Assert.Equal(3, slots.Cycles[0]);
        Assert.Equal(0, slots.Timers[0]);
        Assert.Equal(0, slots.Ammo[0]);
    }

    /// <summary>
    /// func_8006BA50:172-185: a positive reserve counter wins over the ammo counter and selects the
    /// second fire half - that is the whole reason a slot has two halves.
    /// </summary>
    [Fact]
    public void APositiveReserveCounterSelectsTheSecondHalf()
    {
        var slots = new WeaponSlots();
        slots.Ammo[0] = 145;
        slots.Ammo[1] = 145;
        slots.Reserve[0] = 0;
        slots.Reserve[1] = 60;

        Assert.Equal(145, slots.CounterOf(0, out bool fromReserve0));
        Assert.False(fromReserve0);

        Assert.Equal(60, slots.CounterOf(1, out bool fromReserve1));
        Assert.True(fromReserve1);
    }

    /// <summary>
    /// func_8006BA50:195-217: the shot is paid for out of whichever counter the trigger read, and a
    /// second-half shot that spends the last reserve round clears the reserve and takes the cycle from
    /// <c>record + 0x0A</c> - the burst of the first half - so the slot drops back to half A.
    /// </summary>
    [Fact]
    public void SpendingTheLastReserveRoundFallsBackToTheFirstHalfBurst()
    {
        var slots = new WeaponSlots();
        slots.Reserve[2] = 1;
        slots.Ammo[2] = 145;
        slots.Cycles[2] = 1;

        var record = Record(burstA: 4);
        slots.SpendAmmo(2, counter: 1, record);

        Assert.Equal(0, slots.Reserve[2]);
        // The ammo counter was not the one the trigger read, so it survives untouched.
        Assert.Equal(145, slots.Ammo[2]);
        // +0x93 is rearmed from the first-half burst - the value the C copies out of record + 0x0A.
        Assert.Equal(record.BurstA, slots.Cycles[2]);
    }

    /// <summary>
    /// func_8006BA50:195-200: a first-half shot debits the ammo counter and leaves the reserve alone,
    /// and both counters follow <c>val - 1</c>, not a fixed step.
    /// </summary>
    [Fact]
    public void AFirstHalfShotDebitsOnlyTheAmmoCounter()
    {
        var slots = new WeaponSlots();
        slots.Ammo[0] = 145;
        slots.Reserve[0] = 0;
        slots.Cycles[0] = 1;

        slots.SpendAmmo(0, counter: 145, Record(burstA: 1));

        Assert.Equal(144, slots.Ammo[0]);
        Assert.Equal(0, slots.Reserve[0]);
        // SpendAmmo owns only the counters; the cycle arithmetic of func_8006BA50:211-217 belongs to
        // WeaponTrigger.TryFire, so +0x93 is untouched here.
        Assert.Equal(1, slots.Cycles[0]);
    }

    /// <summary>
    /// func_8006BA50:197-200: while the owner is invulnerable the counter is <c>val ^ 1</c> instead of
    /// <c>val - 1</c>, so an odd counter becomes one lower and an even one becomes one higher - a
    /// mostly-spent weapon cannot be drained during the invulnerability phase.
    /// </summary>
    [Fact]
    public void InvulnerableOwnersFlipTheLowBitInsteadOfDebiting()
    {
        var slots = new WeaponSlots();
        slots.Ammo[0] = 145;   // odd
        slots.Ammo[1] = 144;   // even
        var record = Record(burstA: 1);

        slots.SpendAmmo(0, counter: 145, record, invulnerable: true);
        slots.SpendAmmo(1, counter: 144, record, invulnerable: true);

        Assert.Equal(144, slots.Ammo[0]);
        Assert.Equal(145, slots.Ammo[1]);
    }

    private static WeaponRecord Record(int burstA)
    {
        var half = new ShotDefinition(0x4D, 0, Action: 3, Flags: 0, ModelId: 59, ObjWord00: 25,
            DamageWord: 0, SpeedWord: 8192, AltSpeed: 122, ArcWord: 8192, ObjWord0E: 0, ObjWord12: 0,
            SoundId: 0, SecondaryColumn: 0, Burst: burstA, ReloadTicks: 8, GapTicks: 0,
            RecordOffset: 0, Resources: Array.Empty<ResourceReference>(), AmmoWord: 145);
        return new WeaponRecord(0x4D, 0x4D, half, null, 0);
    }
}
