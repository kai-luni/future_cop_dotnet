using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

public class EnemyCorpseCleanupTests
{
    // The task assumed a function func_8005FE80 ("corpse cleanup"); the retail
    // binary has none at that address (no glabel, no caller; the address is the
    // second gSpawnTypeAtOwner tail inside func_8005F6E8, asm main.s:93239,
    // rom 0x50680). EnemyCorpseCleanup documents this and carries the one
    // portable fact of the cluster: the entity-budget gate of func_80060238.
    // The 0x8005FE80 spawn code itself is ported and tested in
    // EnemyWeaponFireTests.TypeTwelveSpawnsTracerAndImpactAtFe80Tail.

    // C: 80060238.c:156-158 (budget gate), 8004E80C.c:21-25 (table words).
    [Theory]
    [InlineData(20, 25, false)]  // free = 5
    [InlineData(20, 29, false)]  // free = 9
    [InlineData(20, 30, true)]  // free = 10
    [InlineData(0, 100, true)]   // empty table
    public void HasFreeBudgetMatchesTheSchedulerGate(int count, int limit, bool expected)
    {
        Assert.Equal(expected, EnemyCorpseCleanup.HasFreeBudget(new EnemyWorld(count, limit)));
    }

    // The gate is a u32 subtraction: a wrapped (negative) difference reads huge and passes.
    [Fact]
    public void BudgetGateUsesTheRetailUnsignedWraparound()
    {
        var env = new EnemyWorld(unchecked((int)0xFFFFFFF0), 10); // free = 0x1A slots
        Assert.True(EnemyCorpseCleanup.HasFreeBudget(env));
        Assert.Equal(unchecked((int)0x1Au), env.FreeSlots);
        Assert.Equal(11, new EnemyWorld(0, 11).FreeSlots);
    }
}
