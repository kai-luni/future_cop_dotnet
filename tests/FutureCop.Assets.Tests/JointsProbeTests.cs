using System.Globalization;
using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

public class JointsProbeTests
{
    [Fact]
    public void JointMatricesMatchOriginalC()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "golden", "joints.vectors");
        foreach (string line in File.ReadLines(path))
        {
            int[] v = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => unchecked((int)uint.Parse(x, NumberStyles.HexNumber))).ToArray();
            int c = v[0];
            var model = new Model { MeshTable = Enumerable.Range(0,32).Select(i => (short)(i*41-500)).ToArray() };
            for (int i=0;i<5;i++)
            {
                byte flags = (byte)((c%2 != 0 ? 0x3f : 0) | (i==c-1 && c>=1 && c<=5 ? 0x80 : 0));
                short x=(short)(c%2!=0?10+i*11:i), y=(short)(c%2!=0?-20+i*7:i+1), z=(short)(c%2!=0?30-i*3:i+2);
                short rx=(short)(c%2!=0?33+i*91:i+3), ry=(short)(c%2!=0?87-i*21:i+4), rz=(short)(c%2!=0?-70+i*13:i+5);
                model.Hierarchy.Add(new ScriptEntry((byte)(i%3+1),0,0,0,0,0,0,flags,x,y,z,rx,ry,rz));
            }
            var (third,fifth)=PlayerAssembly.FindJoints(model,3,Gte.Mat.Identity(160,320,480));
            AssertMatrix(v[1..13], fifth);
            AssertMatrix(v[13..25], third);
        }
    }
    private static void AssertMatrix(int[] expected, Gte.Mat? actual)
    {
        if (expected.All(x => x == 0x12345678)) { Assert.Null(actual); return; }
        Assert.NotNull(actual);
        Assert.Equal(expected, actual.Value.W.Concat(new[]{ actual.Value.Tx, actual.Value.Ty, actual.Value.Tz }));
    }
}