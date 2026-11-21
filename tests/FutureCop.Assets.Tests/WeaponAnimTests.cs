using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The behavioural checks of <c>tests/host/weapon_anim_harness.c</c> (16 cases) against the port:
/// func_8006BA50 gWeaponSlotUpdate, the per-slot trigger driver.
/// </summary>
/// <remarks>
/// <para>
/// The harness compiles the unchanged decomp unit next to recording stubs for
/// gPlayNonpositionalSoundEffect, gRegistryFind, gWeaponFireDispatch and gBeamPartUpdate, so its
/// expectations are retail. The record bytes it uses are inline in that file (entry +0x06 = 9,
/// +0x08 = 4, +0x0A = 7), not in a vector file.
/// </para>
/// <para>
/// The port splits the unit into <see cref="WeaponSlots"/> (the control block at obj + 0xAC),
/// <see cref="WeaponTrigger"/> (the counter and cycle arithmetic) and
/// <see cref="LevelWeapons.TryFireSlot"/> (the driver including the dispatch and the beam gate).
/// </para>
/// <para>
/// Three groups of harness cases have no assertable counterpart in the port and are reported as gaps
/// instead of being faked here: the alternating record words at +0x44 / +0x50 (cases 10 and 11 - the
/// port models the arm halves directly and never carries those two words), the slot-2 retirement pass
/// over the unit list D_8009FA40 (cases 12, 14, 15 - the port has no unit list), and the sound effects
/// 0xD / 0xE (cases 3 and 4 - the port has no gameplay SE layer, so only the early return and the
/// counter are asserted).
/// </para>
/// </remarks>
public class WeaponAnimTests
{
    private static string FindMission()
    {
        foreach (var candidate in new[]
                 {
                     "build/disc/files/PA/CONFT.MIS", "../../../../build/disc/files/PA/CONFT.MIS",
                     "../../../../../build/disc/files/PA/CONFT.MIS",
                 })
            if (File.Exists(candidate)) return candidate;
        throw new FileNotFoundException("build/disc/files/PA/CONFT.MIS");
    }

    /// <summary>An enabled level with the boot loadout and the five player instances composed.</summary>
    private static (LevelScene Scene, LevelWeapons Weapons, IReadOnlyList<PlayerPart> Parts) Level()
    {
        string mis = FindMission();
        var scene = new LevelScene(mis);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var parts = PlayerAssembly.Build(scene.Actor, new MisFile(mis).Models, mainFrame: 0);
        return (scene, scene.Weapons!, parts);
    }

    // ------------------------------------------------------------------- harness cases 1 and 16

    /// <summary>
    /// weapon_anim_harness.c cases 1 and 16: <c>if ((u32)idx &lt; 3)</c> at func_8006BA50:144 is the
    /// whole gate, and it is an unsigned compare - so a negative index is rejected like any other
    /// out-of-range one. Nothing in the control block moves and nothing dispatches.
    /// </summary>
    [RetailFact]
    public void AnOutOfRangeSlotTouchesNothing()
    {
        var (scene, weapons, parts) = Level();
        var slots = weapons.Slots;
        int ammo = slots.Ammo[0], cycles = slots.Cycles[0], timer = slots.Timers[0];

        foreach (int slot in new[] { 3, 4, -1, -100 })
            Assert.Equal(0, weapons.TryFireSlot(scene.Walker!, parts, slot));

        Assert.Equal(ammo, slots.Ammo[0]);
        Assert.Equal(cycles, slots.Cycles[0]);
        Assert.Equal(timer, slots.Timers[0]);
        Assert.Empty(weapons.Shots);
        Assert.Empty(weapons.Beams);
    }

    // ------------------------------------------------------------------- harness cases 2 and 7

    /// <summary>
    /// weapon_anim_harness.c cases 2 and 7: a positive reload timer at ctl + 0x78 returns at
    /// func_8006BA50:173 before the counter is read, so neither the counter nor the cycle counter move.
    /// That is what makes a suppressed shot free (the harness's "failed dispatch" case is the same
    /// early return from the caller's point of view).
    /// </summary>
    [RetailFact]
    public void ARunningReloadTimerSuppressesTheShotAndKeepsTheCounters()
    {
        var (scene, weapons, parts) = Level();
        var slots = weapons.Slots;
        slots.Timers[1] = 1;                       // one tick left: still reloading
        int ammo = slots.Ammo[1], cycles = slots.Cycles[1];

        Assert.Equal(0, weapons.TryFireSlot(scene.Walker!, parts, 1));

        Assert.Empty(weapons.Shots);
        Assert.Equal(ammo, slots.Ammo[1]);
        Assert.Equal(cycles, slots.Cycles[1]);
        Assert.Equal(1, slots.Timers[1]);
    }

    /// <summary>
    /// weapon_anim_harness.c case 7: a dispatch that reports failure leaves the counter and the cycle
    /// counter alone (func_8006BA50:196 <c>if (ok)</c> wraps the whole write-back). The port's
    /// equivalent of a failed dispatch is an action whose spawner it does not carry, e.g. 0x0D.
    /// </summary>
    [RetailFact]
    public void AFailedDispatchLeavesBothCountersAlone()
    {
        var (scene, weapons, parts) = Level();
        // id 0x05 is tACT 0x43, action 0x0D - a spawner the port does not have.
        weapons.Equip(new byte[] { 0x05, 0x05, 0x05 });
        var slots = weapons.Slots;
        slots.Timers[0] = 0;
        int ammo = slots.Ammo[0], cycles = slots.Cycles[0];
        Assert.Equal(0x0D, weapons.RecordOf(0)!.HalfA.Action);

        Assert.Equal(0, weapons.TryFireSlot(scene.Walker!, parts, 0));

        Assert.Equal(ammo, slots.Ammo[0]);
        Assert.Equal(cycles, slots.Cycles[0]);
        Assert.Equal(0, slots.Timers[0]);
    }

    // ------------------------------------------------------------------- harness case 3

    /// <summary>
    /// weapon_anim_harness.c case 3: a counter that is not positive returns at func_8006BA50:180-183,
    /// before the definition lookup and before the cycle arithmetic, so the cycle counter keeps its
    /// value. Retail also plays SE 0xD there; the port has no SE layer, so only the early return and
    /// the untouched counters are asserted.
    /// </summary>
    [RetailFact]
    public void AnEmptyCounterReturnsBeforeTheCycleArithmetic()
    {
        var (scene, weapons, parts) = Level();
        var slots = weapons.Slots;
        slots.Ammo[1] = 0;
        slots.Reserve[1] = 0;
        slots.Timers[1] = 0;
        slots.Cycles[1] = 4;                       // deliberately not the record's burst

        Assert.Equal(0, weapons.TryFireSlot(scene.Walker!, parts, 1));

        Assert.Empty(weapons.Shots);
        Assert.Equal(0, slots.Ammo[1]);
        Assert.Equal(4, slots.Cycles[1]);
        Assert.Equal(0, slots.Timers[1]);
    }

    // ------------------------------------------------------------------- harness cases 4 and 5

    /// <summary>
    /// weapon_anim_harness.c cases 4 and 5: a counter of 5 plays SE 0xE at func_8006BA50:185-187 and
    /// still fires, and the shot is debited once.
    /// </summary>
    [RetailFact]
    public void ACounterOfFiveIsWarnedAboutAndStillFires()
    {
        var (scene, weapons, parts) = Level();
        var slots = weapons.Slots;
        slots.Ammo[1] = 5;
        slots.Reserve[1] = 0;
        slots.Timers[1] = 0;

        Assert.Equal(1, weapons.TryFireSlot(scene.Walker!, parts, 1));

        Assert.Single(weapons.Shots);
        Assert.Equal(4, slots.Ammo[1]);
    }

    /// <summary>
    /// weapon_anim_harness.c case 5 with three different start values: the write-back is
    /// <c>val - 1</c> for every positive counter, never a fixed step.
    /// </summary>
    [RetailFact]
    public void EveryPositiveCounterDebitsByExactlyOne()
    {
        var (scene, weapons, parts) = Level();
        var slots = weapons.Slots;

        foreach (int start in new[] { 3, 7, 145 })
        {
            slots.Ammo[1] = start;
            slots.Reserve[1] = 0;
            slots.Timers[1] = 0;
            int shots = weapons.Shots.Count;

            Assert.Equal(1, weapons.TryFireSlot(scene.Walker!, parts, 1));
            Assert.Equal(shots + 1, weapons.Shots.Count);
            Assert.Equal(start - 1, slots.Ammo[1]);
        }
    }

    // ------------------------------------------------------------------- harness case 8

    /// <summary>
    /// weapon_anim_harness.c case 8: the +0x93 cycle counter counts down and credits the entry's +0x0A
    /// while it is still positive; when it reaches zero it reloads from +0x06 and the reload timer gets
    /// +0x08. The harness's bytes are 9 / 4 / 7 and it asserts 2 -> 1 with +7, then 1 -> 9 with +4.
    /// </summary>
    [Fact]
    public void TheCycleCounterReloadsFromTheEntryOrStepsByTheGap()
    {
        var cadence = new WeaponCadence(Burst: 9, ReloadTicks: 4, GapTicks: 7);

        // cycle 2 -> 1: still inside the burst, so the gap is credited.
        var running = new CadenceState(2);
        Assert.True(WeaponTrigger.TryFire(ref running, cadence));
        Assert.Equal(1, running.Cycle);
        Assert.Equal(7, running.Timer);

        // cycle 1 -> 0: the burst is spent, so +0x06 refills the counter and +0x08 the timer.
        var expired = new CadenceState(1);
        Assert.True(WeaponTrigger.TryFire(ref expired, cadence));
        Assert.Equal(9, expired.Cycle);
        Assert.Equal(4, expired.Timer);
    }

    // ------------------------------------------------------------------- harness case 9

    /// <summary>
    /// weapon_anim_harness.c case 9: the entry type gate at func_8006BA50:191 is
    /// <c>ty &gt;= 10 &amp;&amp; (ty &lt; 13 || ty == 14)</c> - types 10, 11, 12 and 14 go to the beam
    /// update and every other type goes to the fire dispatch. The harness pins 13 and 9 as
    /// <em>not</em> beams; 13 is the value a naive <c>10..14</c> range test would swallow. Every entry of
    /// the mission's registry is fired and its beam checked, so the port's gate cannot be wider or
    /// narrower than retail's.
    /// </summary>
    [RetailFact]
    public void OnlyTheTypesTenToTwelveAndFourteenProduceABeam()
    {
        var (scene, weapons, parts) = Level();
        var registry = weapons.Registry;
        static bool IsBeam(int type) => type is 10 or 11 or 12 or 14;

        var types = new HashSet<int>();
        var wrong = new List<string>();
        foreach (var (id, record) in registry)
        {
            int action = record.HalfA.Action;
            types.Add(action);
            weapons.Equip(new[] { id, id, id });
            weapons.Slots.Timers[0] = 0;
            int beams = weapons.Beams.Count;

            weapons.TryFireSlot(scene.Walker!, parts, 0);

            bool beamed = weapons.Beams.Count > beams;
            if (beamed != IsBeam(action))
                wrong.Add($"id 0x{id:X2} action 0x{action:X2}: beam={beamed}, retail says {IsBeam(action)}");
        }

        // The gate only means something if the registry really holds both sides of it.
        Assert.Contains(0x0A, types);   // beam
        Assert.Contains(0x0C, types);   // beam
        Assert.Contains(0x0D, types);   // 13: inside a naive 10..14 range, not a beam
        Assert.Contains(0x0E, types);   // 14: the second exception of the C condition
        Assert.Contains(0x09, types);   // below the gate
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    // ------------------------------------------------------------------- harness case 13

    /// <summary>
    /// weapon_anim_harness.c case 13: the +0x8A reserve counter is indexed by slot, so driving slot 1
    /// reads and debits only its own halfword. A shared word would make slot 1 fall through to the
    /// empty counter and stop instead of firing.
    /// </summary>
    [RetailFact]
    public void TheReserveCounterIsPerSlotNotShared()
    {
        var (scene, weapons, parts) = Level();
        var slots = weapons.Slots;

        // Slot 0 empty, slot 1 has three reserve rounds and an empty ammo counter.
        slots.Ammo[1] = 0;
        slots.Reserve[0] = 0;
        slots.Reserve[1] = 3;
        slots.Reserve[2] = 0;
        slots.Timers[1] = 0;

        Assert.Equal(1, weapons.TryFireSlot(scene.Walker!, parts, 1));

        Assert.Single(weapons.Shots);
        Assert.Equal(2, slots.Reserve[1]);
        Assert.Equal(0, slots.Reserve[0]);
        Assert.Equal(0, slots.Reserve[2]);
        Assert.Equal(0, slots.Ammo[1]);           // the ammo counter was not the one read
    }

    // ------------------------------------------------------------------- harness cases 10 and 11

    /// <summary>
    /// weapon_anim_harness.c cases 10 and 11: while the reserve counter is positive it selects the
    /// second fire half (record + 0x24) and the muzzle record by its own parity -
    /// func_8006ACB0:120-127 picks <c>state + 0x178 + (counter &amp; 1) * 76</c> before the decrement.
    /// The harness's record-word swap at +0x44 / +0x50 is a record-side effect the port does not model;
    /// the arm parity is the observable half of the same rule.
    /// </summary>
    [RetailFact]
    public void TheReserveCounterSelectsTheSecondHalfAndAlternatesTheMuzzle()
    {
        var (scene, weapons, parts) = Level();
        var slots = weapons.Slots;
        slots.Ammo[1] = 0;
        slots.Timers[1] = 0;

        // The record's two halves really differ, otherwise the test proves nothing.
        var record = weapons.RecordOf(1)!;
        Assert.NotEqual(record.HalfA.Action, record.Half(secondHalf: true).Action);

        var sides = new List<int>();
        for (int counter = 3; counter > 0; counter--)
        {
            slots.Reserve[1] = counter;
            slots.Timers[1] = 0;
            int shots = weapons.Shots.Count;

            Assert.Equal(1, weapons.TryFireSlot(scene.Walker!, parts, 1));
            Assert.Equal(shots + 1, weapons.Shots.Count);
            // func_8006ACB0:123-127: the arm record follows the parity of the counter it read.
            Assert.Equal(counter & 1, weapons.MuzzleSide);
            Assert.Equal(counter - 1, slots.Reserve[1]);
            Assert.Contains($"action 0x{record.Half(secondHalf: true).Action:X2}", weapons.LastShot);
            sides.Add(weapons.MuzzleSide);
        }

        // Both arm halves were used, alternating.
        Assert.Equal(new[] { 1, 0, 1 }, sides);
    }

    // ------------------------------------------------------------------- harness case 6

    /// <summary>
    /// weapon_anim_harness.c case 6: while gPlayerInvulnerable[owner slot] is set the counter is
    /// toggled in bit 0 instead of debited (func_8006BA50:197-203), so an odd value drops and an even
    /// one rises - a mostly-spent weapon cannot be drained while the owner is invulnerable.
    /// </summary>
    [Fact]
    public void InvulnerabilityTogglesTheCounterInsteadOfSpendingIt()
    {
        var record = Record(burst: 1);
        var slots = new WeaponSlots();

        slots.Ammo[0] = 3;
        slots.SpendAmmo(0, 3, record, invulnerable: true);
        Assert.Equal(2, slots.Ammo[0]);

        slots.Ammo[0] = 4;
        slots.SpendAmmo(0, 4, record, invulnerable: true);
        Assert.Equal(5, slots.Ammo[0]);
    }

    private static WeaponRecord Record(int burst)
    {
        var half = new ShotDefinition(SectionId: 0x44, Half: 0, Action: 3, Flags: 0, ModelId: 44,
            ObjWord00: 0, DamageWord: 0, SpeedWord: 8192, AltSpeed: 122, ArcWord: 8192, ObjWord0E: 0,
            ObjWord12: 0, SoundId: 0, SecondaryColumn: 0, Burst: burst, ReloadTicks: 4, GapTicks: 7,
            RecordOffset: 0, Resources: Array.Empty<ResourceReference>(), AmmoWord: 3);
        return new WeaponRecord(0x10, 0x44, half, null, 0);
    }
}
