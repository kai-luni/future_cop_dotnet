using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

public class DroneTargetTests
{
    // C: tests/host/naming_wave_harness.c:194-231, func_800336A8.
    [Fact]
    public void PursuitTargetMatchesOriginalCases()
    {
        var state = new DronePursuitTarget { Dirty = 7 };
        var first = new DroneTarget(0x1234);
        var second = new DroneTarget(-7);
        state.Set(null);
        Assert.Null(state.Target);
        Assert.Equal(0, state.CachedValue);
        Assert.Equal(7, state.Dirty);
        state.Set(first);
        Assert.Same(first, state.Target);
        Assert.Equal(0x1234, state.CachedValue);
        Assert.Equal(0, state.Dirty);
        state.CachedValue = 0x5555;
        state.Dirty = 3;
        state.Set(first);
        Assert.Same(first, state.Target);
        Assert.Equal(0x5555, state.CachedValue);
        Assert.Equal(0, state.Dirty);
        state.Set(second);
        Assert.Same(second, state.Target);
        Assert.Equal(-7, state.CachedValue);
    }

    // C: tests/host/naming_wave_harness.c:233-284, func_80033608.
    [Fact]
    public void AttackTargetMatchesOriginalCases()
    {
        var state = new DroneAttackTarget { BaseValue = 0x3c3c, Mode = 1, MirrorBase = 0x7b };
        var target = new DroneTarget(0x99);
        state.Set(target);
        Assert.Same(target, state.Target);
        Assert.Equal(0x99, state.CachedValue);
        Assert.Same(target, state.First.Target);
        Assert.Same(target, state.Second.Target);
        Assert.Equal(0x99, state.First.CachedValue);
        Assert.Equal(0x99, state.Second.CachedValue);
        state.CachedValue = state.First.CachedValue = state.Second.CachedValue = 0xaa;
        state.Set(target);
        Assert.Equal(0xaa, state.CachedValue);
        Assert.Equal(0xaa, state.First.CachedValue);
        Assert.Equal(0xaa, state.Second.CachedValue);
        state.Set(null);
        Assert.Equal(0x3c3c, state.Value);
        Assert.Equal(0x3c3c, state.First.Value);
        Assert.Equal(0x3c3c, state.Second.Value);
        Assert.Equal(4, state.Mode);
        Assert.Equal(4, state.First.Mode);
        Assert.Equal(4, state.Second.Mode);
        Assert.Equal(0x7b, state.RestoredValue);
        state.RestoredValue = 0;
        state.BaseValue = 0x0101;
        state.Set(null);
        Assert.Equal(0x0101, state.Value);
        Assert.Equal(0, state.RestoredValue);
    }
}
