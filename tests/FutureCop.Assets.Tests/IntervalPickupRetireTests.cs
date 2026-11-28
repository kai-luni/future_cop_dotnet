using FutureCop.Assets;
using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Regression tests for C <c>8002FF6C.c:76-79</c> + <c>8002FC90.c:91-95</c> and
/// <c>tests/host/leaf_gameplay_harness.c:432-438</c>: a contact hit calls
/// <c>gApplyPlayerResupply</c> and then retires unconditionally when bit <c>0x02000000</c>
/// (<see cref="WeaponPickups.SelfRetire"/>) is set, even if an interval pickup returned early
/// below its period without paying out.
/// </summary>
public sealed class IntervalPickupRetireTests
{
    private static (WeaponPickups Field, PlayerResupplyState Player, WeaponSlots Slots) MakeField(
        uint payload, int accumulator, int period)
    {
        var file = new MisFile(LevelSceneRetirementTests.FindMission()!);
        var registry = WeaponCatalog.LoadRegistry(file);
        var field = new WeaponPickups(registry, difficulty: 0, file.Bytes);
        field.Add(new WeaponPickup
        {
            HalfX = 100,
            HalfY = 100,
            HalfZ = 100,
            Payload = payload,
            Accumulator = accumulator,
            Period = period,
        });
        var slots = new WeaponSlots();
        slots.Reset(WeaponSlots.DefaultSlotIds, registry, difficulty: 0);
        slots.Ammo[0] = 0;
        var player = new PlayerResupplyState(slots)
        {
            Gauge = 50,
            GaugeMax = 200,
            TimerA = 0,
            TimerB = 0,
        };
        return (field, player, slots);
    }

    [ConftFact]
    public void IntervalPickupWithRetireBitRetiresOnContactBelowPeriodWithoutPayout()
    {
        // Port of the "below the period" case in leaf_gameplay_harness.c:432-438:
        // acc=50, period=100, frameDelta=30 -> acc becomes 80, nothing paid out,
        // yet 8002FF6C.c:77-79 still calls gRetireEntity when 0x02000000 is set.
        uint payload = WeaponPickups.SelfRetire | WeaponPickups.Health | WeaponPickups.TimerA | WeaponPickups.TimerB | 0x0001;
        var (field, player, slots) = MakeField(payload, accumulator: 50, period: 100);
        var pickup = Assert.Single(field.Items);

        int paid = field.Update(ResupplyContext.Solo(player, frameDelta: 30), 0, 0, 0);

        Assert.Equal(0, paid);
        Assert.Equal(80, pickup.Accumulator);
        Assert.Equal(50, player.Gauge);
        Assert.Equal(0, player.TimerA);
        Assert.Equal(0, player.TimerB);
        Assert.Equal(0, slots.Ammo[0]);

        Assert.True(pickup.Collected);
        Assert.Empty(field.Items);
        Assert.Equal(0, field.RemainingCount);
        Assert.Equal(1, field.CollectedCount);

        // Once removed, a subsequent contact with enough ticks to cross the period pays nothing.
        Assert.Equal(0, field.Update(ResupplyContext.Solo(player, frameDelta: 100), 0, 0, 0));
        Assert.Equal(50, player.Gauge);
        Assert.Equal(0, slots.Ammo[0]);
    }

    [ConftFact]
    public void IntervalPickupWithoutRetireBitStaysBelowPeriodAndPaysOncePeriodIsReached()
    {
        uint payload = WeaponPickups.Health | WeaponPickups.TimerA | 0x0001;
        var (field, player, slots) = MakeField(payload, accumulator: 50, period: 100);
        var pickup = Assert.Single(field.Items);

        // Below period: accumulates to 80, pays nothing, stays live.
        Assert.Equal(0, field.Update(ResupplyContext.Solo(player, frameDelta: 30), 0, 0, 0));
        Assert.Equal(80, pickup.Accumulator);
        Assert.Equal(50, player.Gauge);
        Assert.Equal(0, player.TimerA);
        Assert.Equal(0, slots.Ammo[0]);
        Assert.False(pickup.Collected);
        Assert.Same(pickup, Assert.Single(field.Items));
        Assert.Equal(1, field.RemainingCount);
        Assert.Equal(0, field.CollectedCount);

        // Next contact reaches 100 (quotient 1, remainder 0): pays out and still stays live.
        Assert.Equal(1, field.Update(ResupplyContext.Solo(player, frameDelta: 20), 0, 0, 0));
        Assert.Equal(0, pickup.Accumulator);
        Assert.Equal(52, player.Gauge);
        Assert.Equal(WeaponPickups.TimerAValue, player.TimerA);
        Assert.Equal(1, slots.Ammo[0]);
        Assert.False(pickup.Collected);
        Assert.Same(pickup, Assert.Single(field.Items));
        Assert.Equal(0, field.CollectedCount);
    }

    [ConftFact]
    public void IntervalPickupWithRetireBitOutsideBoxDoesNotRetire()
    {
        uint payload = WeaponPickups.SelfRetire | WeaponPickups.Health;
        var (field, player, _) = MakeField(payload, accumulator: 50, period: 100);
        var pickup = Assert.Single(field.Items);

        Assert.Equal(0, field.Update(ResupplyContext.Solo(player, frameDelta: 30), 100, 0, 0));
        Assert.Equal(50, pickup.Accumulator);
        Assert.False(pickup.Collected);
        Assert.Single(field.Items);
    }
}
