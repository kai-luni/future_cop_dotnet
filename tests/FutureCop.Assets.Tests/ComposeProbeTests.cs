using System.Globalization;
using FutureCop.Assets;
using Xunit;
namespace FutureCop.Assets.Tests;
public class ComposeProbeTests
{
    [Fact]
    public void OriginalCLocalTransformWithGteModelMatches()
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "golden", "compose.vectors");
        int count = 0;
        foreach (string line in File.ReadLines(path))
        {
            int[] v = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => unchecked((int)uint.Parse(s, NumberStyles.HexNumber))).ToArray();
            Assert.Equal(25, v.Length);
            var parent = new Gte.Mat(v.Take(9).ToArray(), v[9], v[10], v[11]);
            byte flags = (byte)v[12];
            var e = flags == 0 ? new ScriptEntry(1,0,0,0,0,0,0,0,0,1,2,3,4,5)
                : new ScriptEntry(1,0,0,0,0,0,0,flags,55,-93,47,291,544,931);
            var table = Enumerable.Range(0, 16).Select(i => (short)(i*101-600)).ToArray();
            var actual = ScriptRender.Compose(ScriptRender.LocalMatrix(e,table,0),parent);
            Assert.Equal(v.Skip(13).Take(9), actual.W);
            Assert.Equal(v.Skip(22), new[] {actual.Tx,actual.Ty,actual.Tz});
            count++;
        }
        Assert.Equal(30,count);
    }
}
