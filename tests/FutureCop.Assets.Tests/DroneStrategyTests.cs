using FutureCop.Game;
using FutureCop.Game.Mech;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// TDD tests for DroneStrategy, the func_800336DC port. Oracles were recomputed
/// from the C semantics, including the mode-2 default trigger (cntA &lt; 10) and
/// the chained mode-2 payments (defence, batch, upkeep).
/// </summary>
public sealed class DroneStrategyTests
{
    private static DroneStrategy.StrategyUnit Unit(int type, int team, int sub, int x, int z)
        => new()
        {
            Active = true,
            Type = type,
            Team = team,
            Sub = sub,
            Pos = new[] { x, 0, z },
        };

    private static DroneStrategy.Self NewSelf(int b1b = 1)
    {
        var self = new DroneStrategy.Self
        {
            Team = 1,
            Pos = new[] { 0, 0, 0 },
            Aa = 0x100,
        };
        self.Strats.B1B = b1b;
        self.Strats.PosA = new[] { 0, 0, 0 };
        self.Strats.PosB = new[] { 0, 0, 0 };
        return self;
    }

    /// <summary>Ten friendly type-8 units near posB: cntA = 10 blocks the mode-2 default.</summary>
    private static void FillType8(MockStrategyWorld w, int n = 10)
    {
        for (int i = 0; i < n; i++)
            w.UnitList.Add(Unit(8, 1, 0, 0, 0));
    }

    // ---- Mode-2 entry via nearby 0x1C (:458-553) ----

    [Fact]
    public void NearbyType1C_SwitchesToMode2_AndPursuesIt()
    {
        var w = new MockStrategyWorld();
        var self = NewSelf();               // b1B = 1: own team is the ruler
        var target = Unit(0x1C, 1, 0, 0x40000, 0);
        w.UnitList.Add(target);
        DroneStrategy.Tick(self, w);
        Assert.Equal(2, self.Strats.Mode);
        Assert.Equal(2, w.G.F9C);
        Assert.Equal(1, w.PursuitCalls);
        // The only attack call is the parking one at :683 (no attack target,
        // aq6 != 4) - the APPROACH radius test (Aa = 0x100) rejects 0x40000.
        Assert.Equal(1, w.AttackCalls);
        Assert.Equal(1, w.StrategicCalls);  // mode changed 0 -> 2
    }

    [Fact]
    public void EmptyWorld_DefaultsToMode2_ByCntA()
    {
        var w = new MockStrategyWorld();
        var self = NewSelf();
        DroneStrategy.Tick(self, w);
        // cntA = 0 < 10 -> mode 2 (800336DC.c:550); the player pursuit is the
        // mode-2 action but the mock exposes no player entity.
        Assert.Equal(2, self.Strats.Mode);
        Assert.Equal(1, w.StrategicCalls);
        // Second tick: no mode change anymore -> no second recompute.
        DroneStrategy.Tick(self, w);
        Assert.Equal(1, w.StrategicCalls);
    }

    // ---- Mode 2 economy: chained payments (:685-742) ----

    [Fact]
    public void Mode2_PaysDefence_Batch_AndUpkeep()
    {
        var w = new MockStrategyWorld();
        var self = NewSelf();
        w.G.S4C = 9;
        w.G.F68 = 100;
        w.UnitList.Add(Unit(0x1C, 1, 0, 0x20000, 0));    // within 0x23FFF
        DroneStrategy.Tick(self, w);
        // Defence (:686-693): FB0+1, F68-1, F74+1.
        // Batch (:700-715): s1 = 0 - 9<<16 <= 0x7FFFF, F78 0 < 6 -> n = 2+0+4 = 6
        //   (clamped by 20 and 99), no (2-2)*2 cut -> FB4+6, F68-6, F78+6.
        // Upkeep (:737-742): F78 6 stops the slot loop; s1 still <= 0x7FFFF and
        //   N5C null -> F68(93) > 0x41 -> F68-0x32, FBC+1.
        Assert.Equal(1, w.G.FB0);
        Assert.Equal(6, w.G.FB4);
        Assert.Equal(1, w.G.FBC);
        Assert.Equal(6, w.G.F78);
        Assert.Equal(1, w.G.F74);
        Assert.Equal(43, w.G.F68);          // 100 -1 -6 -50
    }

    // ---- Mode-1 entry: two type-5 near posA, non-ruling team (:520-541, :570-575) ----

    [Fact]
    public void Mode1_TwoType5NearPosA_EntersMode1()
    {
        var w = new MockStrategyWorld();
        var self = NewSelf(b1b: 2);         // self team 1 is not the ruler
        FillType8(w);                       // cntA = 10 blocks mode 2
        w.UnitList.Add(Unit(5, 1, 0, 0x800, 0));
        w.UnitList.Add(Unit(5, 1, 0, 0x400, 0));       // nearer: replaces, cnt2 = 2
        DroneStrategy.Tick(self, w);
        Assert.Equal(1, self.Strats.Mode);
        Assert.Equal(1, w.G.F9C);
        // n50 = nearer type 5 -> attack reset + pursuit (:585-589), plus the
        // parking call at :683 -> two attack calls in total.
        Assert.Equal(2, w.AttackCalls);
        Assert.Equal(1, w.PursuitCalls);
        Assert.Equal(1, w.StrategicCalls);
    }

    [Fact]
    public void Mode1_SingleType5_StaysMode0_WithIdleFallback()
    {
        var w = new MockStrategyWorld();
        var self = NewSelf(b1b: 2);
        FillType8(w);
        w.UnitList.Add(Unit(5, 1, 0, 0x800, 0));
        DroneStrategy.Tick(self, w);
        // cnt2 = 1 < 2 -> no mode 1; no mode-0 candidate at all -> idle fallback.
        Assert.Equal(0, self.Strats.Mode);
        Assert.Equal(1, self.Fa);
        Assert.Equal(1, w.StrategicCalls);
    }

    // ---- Slot claims (:743-760): order [3,2,1,0] ----

    [Fact]
    public void Mode0_ClaimsFirstAffordableSlot_InRetailOrder()
    {
        var w = new MockStrategyWorld();
        var self = NewSelf(b1b: 2);
        FillType8(w);                       // keeps mode 0
        w.G.F68 = 0x1E;                     // exactly one claim
        DroneStrategy.Tick(self, w);
        Assert.Equal(1, w.G.A0[3]);         // order starts at slot 3
        Assert.Equal(0, w.G.F68);
        Assert.Equal(1, w.SinglePlayerS4CWrites); // F88 == 0 -> frame-time stamp
    }

    [Fact]
    public void Mode0_TooPoor_ClaimsNothing()
    {
        var w = new MockStrategyWorld();
        var self = NewSelf(b1b: 2);
        FillType8(w);
        w.G.F68 = 0;
        DroneStrategy.Tick(self, w);
        Assert.Equal(0, w.G.A0[0]);
        Assert.Equal(0, w.G.A0[3]);
        Assert.Equal(0, w.SinglePlayerS4CWrites);
    }
}
