using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>Deterministic world for StrategicDroneTick tests (8003532C).</summary>
internal sealed class MockStrategicWorld : StrategicDroneTick.IWorld
{
    public int Rand = 0;
    public int DifficultyValue = 4;
    public StrategicDroneTick.Type16Unit? Nearest;
    public int NearestDistance;
    public int ResetCalls, TargetSetCalls, StrategyCalls, RetireCalls, HoverTicks;
    public int SchedulerTicks;

    public int NextRandom() => Rand;
    public int Difficulty() => DifficultyValue;
    public StrategicDroneTick.Type16Unit? FindNearest(StrategicDroneTick.DroneEntity self, out int distance)
    {
        distance = NearestDistance;
        return Nearest;
    }
    public void ResetDroneApproach(StrategicDroneTick.DroneEntity self) => ResetCalls++;
    public void SetDroneAttackTarget(StrategicDroneTick.DroneEntity self) => TargetSetCalls++;
    public void UpdateDroneStrategy(StrategicDroneTick.DroneEntity self) => StrategyCalls++;
    public void RetireEntity(StrategicDroneTick.Type16Unit unit) => RetireCalls++;
    public void StepAttackScheduler(StrategicDroneTick.AiState ai, int which, StrategicDroneTick.DroneEntity self, int delta) => SchedulerTicks++;
    public void CopyUnitPos(StrategicDroneTick.DroneEntity self, StrategicDroneTick.Type16Unit unit)
        => System.Array.Copy(unit.Pos, self.Pos, 3);
    public void UpdateHoverEnemy(StrategicDroneTick.DroneEntity self, int delta) => HoverTicks++;
    public int SessionWord => DifficultyValue;
}

/// <summary>TDD tests for StrategicDroneTick, the func_8003532C port.</summary>
public sealed class StrategicDroneTests
{
    private static (StrategicDroneTick.DroneEntity D, MockStrategicWorld W) New()
    {
        var d = new StrategicDroneTick.DroneEntity
        {
            Unk1C = 0,
            BodyPos = new[] { 0x10000, 0, 0x10000 },
            Pos = new[] { 0x10000, 0, 0x10000 },
            UnkF4 = 0x100,
            UnkF8 = 0x1F,
        };
        d.Ai.Pos = new[] { 0x20000, 0, 0x20000 };
        d.Ai.Limit = 0x100;
        d.Ai.Mode = 0;
        d.Ai.PromoteTo = 0x1F;
        d.Ai.TickTimer = 100;
        var w = new MockStrategicWorld();
        return (d, w);
    }

    // ---- Approach limit + distance gate (8003532C.c:136-170) ----

    [Fact]
    public void Distance_UsesBodyPosition_NotTheDCTarget()
    {
        // 8003532C.c:28-37: dist reads self->unk50 / self->unk58 (struct Entity +0x50/+0x58),
        // the target copy self->pos lives at +0xDC (:33) and must not feed the gate.
        var (d, w) = New();
        d.BodyPos = new[] { 0x20010, 0, 0x20000 };  // 0x10 from the node -> reset
        d.Pos = new[] { 0x90000, 0, 0x90000 };      // far away target copy
        StrategicDroneTick.Tick(d, w, 1);
        Assert.Equal(1, w.ResetCalls);

        var (d2, w2) = New();
        d2.BodyPos = new[] { 0x90000, 0, 0x90000 }; // far body -> no reset, acquire instead
        d2.Pos = new[] { 0x20000, 0, 0x20000 };     // target copy on the node
        StrategicDroneTick.Tick(d2, w2, 1);
        Assert.Equal(0, w2.ResetCalls);
        Assert.Equal(1, d2.UnkFA);
    }

    [Fact]
    public void NearNode_ResetsApproach_RunsTimerAndFinal()
    {
        var (d, w) = New();
        d.Unk1C = 0;                    // below limit
        d.Ai.Pos = new[] { 0x10040, 0, 0x10000 }; // dist = 0x40 < 0x4000
        StrategicDroneTick.Tick(d, w, 10);
        Assert.Equal(1, w.ResetCalls);
        Assert.Equal(2, w.SchedulerTicks);        // two descriptors tick in the tail
        Assert.Equal(1, w.HoverTicks);
        // Timer ran (delta subtracted) but not negative: no strategy tick.
        Assert.Equal(100 - 10, d.Ai.TickTimer);
        Assert.Equal(0, w.StrategyCalls);
    }

    [Fact]
    public void Modes1And2_HalveTheLimit()
    {
        var (d, w) = New();
        d.Ai.Mode = 1;
        d.Ai.Limit = 0x100;
        d.Unk1C = 0x90;                 // below halved limit (0x80)? No: 0x90 > 0x80.
        d.Ai.Pos = new[] { 0x30000, 0, 0x30000 };
        StrategicDroneTick.Tick(d, w, 1);
        // 0x90 >= 0x80 -> no approach: only timer + final.
        Assert.Equal(0, w.ResetCalls);
        Assert.Equal(2, w.SchedulerTicks);
        Assert.Equal(1, w.HoverTicks);
    }

    // ---- State 0 acquire (8003532C.c:172-179) ----

    [Fact]
    public void State0_CopiesNodePos_ChargesAndParksAnimation()
    {
        var (d, w) = New();
        d.UnkF4 = 0x10000;               // charge word grows by F4 >> (4 - diff>>2)
        d.Ai.Pos = new[] { 0x30000, 0x2000, 0x30000 };
        StrategicDroneTick.Tick(d, w, 1);
        Assert.Equal(1, d.UnkFA);
        Assert.Equal(new[] { 0x30000, 0x2000, 0x30000 }, d.Pos);
        // difficulty 4 -> shift 4 - 1 = 3: F4 += F4 >> 3 = 0x10000 + 0x2000.
        Assert.Equal(0x12000, d.UnkF4);
        Assert.Equal(1, w.TargetSetCalls);
        Assert.Equal(0, w.ResetCalls);
    }

    // ---- State 1 tracking (8003532C.c:180-211) ----

    [Fact]
    public void State1_WithNearUnit_CopiesPositionAndSkipsTimerBody()
    {
        var (d, w) = New();
        d.UnkFA = 1;
        d.Ai.Pos = new[] { 0x30000, 0, 0x30000 };
        w.Nearest = new StrategicDroneTick.Type16Unit
        {
            Pos = new[] { 0x28000, 0x100, 0x28000 },
            RetireFlagC8 = false,
        };
        w.NearestDistance = 0x8000;    // < dist -> position copied
        StrategicDroneTick.Tick(d, w, 1);
        Assert.Equal(new[] { 0x28000, 0x100, 0x28000 }, d.Pos);
        Assert.Equal(2, w.SchedulerTicks);
        Assert.Equal(1, w.HoverTicks);
    }

    [Fact]
    public void State1_VeryCloseUnit_RetiresIfFlagged()
    {
        var (d, w) = New();
        d.UnkFA = 1;
        d.Ai.Pos = new[] { 0x30000, 0, 0x30000 };
        w.Nearest = new StrategicDroneTick.Type16Unit { RetireFlagC8 = true };
        w.NearestDistance = 0x800;      // < 0x1000 -> retire + reset
        StrategicDroneTick.Tick(d, w, 1);
        Assert.Equal(1, w.RetireCalls);
        Assert.Equal(1, w.ResetCalls);
    }

    [Fact]
    public void State1_Close_PROMOTEF8()
    {
        var (d, w) = New();
        d.UnkFA = 1;
        d.UnkF8 = 0x1F;                 // == ai.PromoteTo
        d.Ai.PromoteTo = 0x1F;
        d.Ai.Pos = new[] { 0x30000, 0, 0x30000 };
        w.Nearest = new StrategicDroneTick.Type16Unit { Pos = new int[3] };
        w.NearestDistance = 0x13FFF;    // <= 0x13FFF -> promote
        StrategicDroneTick.Tick(d, w, 1);
        Assert.Equal(0x1F << 2, d.UnkF8);
    }

    // ---- Timer reload (8003532C.c:217-228) ----

    [Fact]
    public void Timer_ReloadsAndRunsStrategy_WhenNegative()
    {
        var (d, w) = New();
        d.Unk1C = 0x200;                 // above limit: straight to the timer path
        d.Ai.TickTimer = 1;
        w.Rand = 0;
        w.DifficultyValue = 4;
        StrategicDroneTick.Tick(d, w, 10);
        // reload = (9-4)*80+128 = 528; mode 0 -> += rand & 0xFF = 0.
        Assert.Equal(528, d.Ai.TickTimer);
        Assert.Equal(1, w.StrategyCalls);
    }
}
