using FcLevelGlTizen;
using Xunit;

public sealed class RenderBurstTests
{
    [Fact]
    public void StartsActiveAndBecomesIdleAfterBudget()
    {
        var burst = new RenderBurst();
        for (int i = 0; i < 120; i++) Assert.True(burst.TryConsume());
        Assert.False(burst.TryConsume());
        Assert.False(burst.TryConsume());
    }

    [Fact]
    public void RequestRestartsTheBudgetWithoutAccumulating()
    {
        var burst = new RenderBurst();
        for (int i = 0; i < 60; i++) Assert.True(burst.TryConsume());
        burst.Request();
        burst.Request();
        for (int i = 0; i < 120; i++) Assert.True(burst.TryConsume());
        Assert.False(burst.TryConsume());
    }
}
