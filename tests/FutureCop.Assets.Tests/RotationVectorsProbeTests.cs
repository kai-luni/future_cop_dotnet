using FutureCop.Assets;
using Xunit;
namespace FutureCop.Assets.Tests;
public class RotationVectorsProbeTests
{
    [Fact]
    public void OriginalCMatchesForAllRotationBranches()
    {
        Assert.Equal(new[] { 65536, 0, 0, 0, 65536, 0, 0, 0, 65536 }, ScriptRender.Rotation(0, 0, 0).W);
        Assert.Equal(new[] { 65536, 0, -100, 0, 65536, 0, 100, 0, 65536 }, ScriptRender.Rotation(1, 0, 0).W);
        Assert.Equal(new[] { 65536, 0, 0, 0, 65536, 100, 0, -100, 65536 }, ScriptRender.Rotation(0, 1, 0).W);
        Assert.Equal(new[] { 65536, 100, 0, -100, 65536, 0, 0, 0, 65536 }, ScriptRender.Rotation(0, 0, 1).W);
        Assert.Equal(new[] { 65536, 0, -100, 0, 65536, 100, 100, -100, 65536 }, ScriptRender.Rotation(1, 1, 0).W);
        Assert.Equal(new[] { 65536, 100, -100, -100, 65536, 0, 100, 0, 65536 }, ScriptRender.Rotation(1, 0, 1).W);
        Assert.Equal(new[] { 65536, 100, 0, -100, 65536, 100, 0, -100, 65536 }, ScriptRender.Rotation(0, 1, 1).W);
        Assert.Equal(new[] { 65536, 100, -100, -100, 65536, 100, 100, -100, 65536 }, ScriptRender.Rotation(1, 1, 1).W);
        Assert.Equal(new[] { 8402, 58509, -28292, -40584, 27002, 43796, 50761, 11904, 39697 }, ScriptRender.Rotation(291, 544, 931).W);
        Assert.Equal(new[] { -1, 100, -65536, -100, 65536, 100, 65536, 100, 0 }, ScriptRender.Rotation(1023, 1024, 1025).W);
        Assert.Equal(new[] { -65536, 100, 0, 0, 0, 65536, 100, 65536, 0 }, ScriptRender.Rotation(2048, 3072, 4095).W);
        Assert.Equal(new[] { 65536, 0, 100, 0, 65536, 0, -100, 0, 65536 }, ScriptRender.Rotation(-1, 0, 0).W);
        Assert.Equal(new[] { 65536, 0, 0, 0, 65536, 0, 0, 0, 65536 }, ScriptRender.Rotation(4096, 0, 0).W);
    }
}
