using FutureCop.Assets;
using Xunit;
namespace FutureCop.Assets.Tests;
public class AssemblyTests
{
    [Fact]
    public void ChainStopsBeforeCopyingTerminatingJoint()
    {
        var model = new Model();
        for (int i=0;i<5;i++) model.Hierarchy.Add(new ScriptEntry(1,0,0,0,0,0,0,(byte)(i==2 ? 0xbf : 0x3f),0,0,0,0,0,0));
        var result = PlayerAssembly.FindJoints(model, 0, Gte.Mat.Identity(0,0,0));
        Assert.Null(result.Third);
        Assert.Null(result.Fifth);
    }
}
