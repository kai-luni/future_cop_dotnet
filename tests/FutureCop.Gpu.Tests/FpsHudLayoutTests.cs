using FcMainMenu;
using Xunit;

namespace FutureCop.Gpu.Tests;

public sealed class FpsHudLayoutTests
{
    [Fact]
    public void ProducesRunsForLabelAndDistinctDigits()
    {
        var runs = FpsHudLayout.Runs("SUB FPS: 1.0");
        Assert.Contains(new FpsHudLayout.Run(1, 0, 2), runs);
        Assert.Contains(new FpsHudLayout.Run(44, 0, 3), runs);
        Assert.Contains(new FpsHudLayout.Run(40, 4, 1), runs);
        Assert.Contains(new FpsHudLayout.Run(37, 0, 1), runs);
        Assert.DoesNotContain(new FpsHudLayout.Run(36, 0, 1), runs);
    }
}
