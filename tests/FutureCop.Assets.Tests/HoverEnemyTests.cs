using FutureCop.Game;
using FutureCop.Game.Mech;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// TDD tests for HoverEnemyTick, the func_800324E0 message-6 port. Oracle values
/// were computed from the C semantics against the deterministic world stub.
/// </summary>
public sealed class HoverEnemyTests
{
    // ---- Descent/climb-back state 4 (800324E0.c:200-210) ----

    [Fact]
    public void State4_ClimbsByF4AndExitsAboveBandEdge()
    {
        var o = HoverTestValues.NewEnemy();
        var w = new MockHoverWorld { HeightBilinear = 0 };
        o.FA = 4;
        o.Pos = new[] { 0, 0x200, 0 };          // below gr + (F114 << 4) = 0x400
        o.F4 = 0x40000;                          // climbs 0x200 per tick

        HoverEnemyTick.Tick(o, 1, w);
        // C adds (dt*F4)>>9 to y: 0x200 + 0x200 = 0x400, not above 0x400 yet.
        Assert.Equal(0x400, o.Pos[1]);
        Assert.Equal(4, o.FA);

        HoverEnemyTick.Tick(o, 1, w);
        // 0x400 + 0x200 = 0x600 > 0x400 -> exits to state 0 (:204-205).
        Assert.Equal(0x600, o.Pos[1]);
        Assert.Equal(0, o.FA);
    }

    // ---- Dead state 5 (800324E0.c:292-295) ----

    [Fact]
    public void DeadState_ClearsF1COnlyForNonType25()
    {
        var o = HoverTestValues.NewEnemy();
        o.Type = 0x24;
        o.FA = 5;
        o.F1C = 5;
        var w = new MockHoverWorld();
        HoverEnemyTick.Tick(o, 1, w);
        Assert.Equal(-1, o.F1C);

        o = HoverTestValues.NewEnemy();
        o.FA = 5;
        o.F1C = 5;
        HoverEnemyTick.Tick(o, 1, w);
        Assert.Equal(5, o.F1C);                 // type 0x25 keeps f1C
    }

    // ---- Dive state 3 (800324E0.c:242-274) ----

    [Fact]
    public void Dive_ImpactRegistersKill_WhenType25()
    {
        var o = HoverTestValues.NewEnemy();
        o.FA = 3;
        o.Pos = new[] { 0, 0, 0 };
        o.F68 = 0x2000;
        o.F8C = 7;
        o.F98 = 3;                              // death script present
        var w = new MockHoverWorld { GroundHeight = 0 };
        HoverEnemyTick.Tick(o, 4, w);
        Assert.Equal(5, o.FA);                  // dead after impact
        Assert.Equal(1, w.KillCredits);
        Assert.Equal(1, w.DeathScripts);
        Assert.Equal((10 - 4) * 150 + 600, o.F100); // session word 13 = 4
        Assert.Equal(1, o.RecF3E);              // impact counter bumped
    }

    [Fact]
    public void Dive_NonType25_ImpactsWithoutKillRegistration()
    {
        var o = HoverTestValues.NewEnemy();
        o.Type = 0x24;
        o.FA = 3;
        o.Pos = new[] { 0, 0, 0 };
        o.F68 = 0x2000;
        o.F8C = 7;
        var w = new MockHoverWorld { GroundHeight = 0 };
        HoverEnemyTick.Tick(o, 4, w);
        Assert.Equal(5, o.FA);
        Assert.Equal(0, w.KillCredits);
        Assert.Equal(0, w.DeathScripts);
    }
}
