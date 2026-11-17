using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

public class RotationParityProbeTests
{
    [Fact]
    public void SingleSmallXAngleMatchesOriginalC()
    {
        // Original func_80018DC0 under qemu, using the retail sine table.
        var actual = ScriptRender.Rotation(1, 0, 0);
        Assert.Equal(new[] { 65536, 0, -100, 0, 65536, 0, 100, 0, 65536 }, actual.W);
    }
}
