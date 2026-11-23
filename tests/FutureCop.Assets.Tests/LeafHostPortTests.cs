using System.Reflection;
using FutureCop.Game.Mech;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Ports of existing C host leaf checks from the decomp repository (future-cop-lapd-ps1):
/// <list type="bullet">
/// <item>tests/host/leaf_final_harness.c test_steering_step (@leaf 80052E28) -> <see cref="MechWalker.SnapToWall"/></item>
/// <item>tests/host/leaf_extra_harness.c test_func_80018818 (@leaf 80018818) -> <see cref="MechMath.Reciprocal"/></item>
/// </list>
/// Every expected value is copied verbatim from those harnesses, which assert them against the
/// unchanged decomp units compiled with gcc 2.7.2 and run under qemu-mipsel; none was produced by
/// this port. The C struct field +0xB0 (s16 unkB0) is <see cref="MechBody.Angle"/> (ushort), and
/// +0xB8 (u8 unkB8) is <see cref="MechBody.Wall"/>, so headings are compared as s16.
/// </summary>
public class LeafHostPortTests
{
    // leaf_final_harness.c test_steering_step cases[] {mode, start, end}, in source order (20 cases).
    public static IEnumerable<object[]> SteeringCases() => new[]
    {
        new object[] { 0x81, 0x03ff, 0x03f7 }, new object[] { 1, 0x0400, 0x0408 },
        new object[] { 1, 0x0c00, 0x0bf8 }, new object[] { 1, 0x0c01, 0x0c09 },
        new object[] { 1, 0x0ffd, 0x1000 }, new object[] { 1, 0x0000, 0x0000 },
        new object[] { 2, 0x07ff, 0x07f7 }, new object[] { 2, 0x0800, 0x0808 },
        new object[] { 3, 0x0000, -1 }, new object[] { 3, 0x01ff, 0x01f7 }, new object[] { 3, 0x0200, 0x0208 },
        new object[] { 3, 0x0a00, 0x09f8 }, new object[] { 3, 0x0a01, 0x0a09 },
        new object[] { 4, 0x05ff, 0x05f7 }, new object[] { 4, 0x0600, 0x0608 },
        new object[] { 4, 0x0e00, 0x0df8 }, new object[] { 4, 0x0e01, 0x0e09 },
        new object[] { 4, 0x09f8, 0x0a00 }, new object[] { 4, 0x0a08, 0x0a00 },
        new object[] { 7, 0x0005, 0x0000 },
    };

    [Fact]
    public void SteeringCaseTableMatchesHarnessCount()
    {
        Assert.Equal(20, SteeringCases().Count());
    }

    [Theory]
    [MemberData(nameof(SteeringCases))]
    public void SnapToWall_MatchesLeafFinalHarness(int mode, int start, int end)
    {
        var body = new MechBody { Angle = unchecked((ushort)(short)start), Wall = (byte)mode };
        MechWalker.SnapToWall(body);
        // "heading at mode/threshold fixture"
        Assert.Equal((short)end, unchecked((short)body.Angle));
        // "mode byte remains unchanged"
        Assert.Equal((byte)mode, body.Wall);
    }

    /// <summary>
    /// Counterpart of the harness's "steering leaves neighboring state untouched" check: every other
    /// readable primitive property of the body keeps its value (the C check fills 0xA5 around +0xB0/+0xB8; here a sentinel 0x25).
    /// </summary>
    [Fact]
    public void SnapToWall_SequentialHarnessRun_LeavesOtherStateUntouched()
    {
        var body = new MechBody();
        var props = typeof(MechBody).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                        && (p.PropertyType.IsPrimitive || p.PropertyType.IsEnum))
            .Where(p => p.Name != nameof(MechBody.Angle) && p.Name != nameof(MechBody.Wall))
            .ToArray();
        Assert.NotEmpty(props);
        foreach (var p in props)
            p.SetValue(body, Sentinel(p.PropertyType));
        var before = props.ToDictionary(p => p.Name, p => p.GetValue(body));

        // Same order as the C loop, which reuses one actor across all cases.
        foreach (object[] c in SteeringCases())
        {
            body.Angle = unchecked((ushort)(short)(int)c[1]);
            body.Wall = (byte)(int)c[0];
            MechWalker.SnapToWall(body);
            Assert.Equal((short)(int)c[2], unchecked((short)body.Angle));
            Assert.Equal((byte)(int)c[0], body.Wall);
        }

        foreach (var p in props)
            Assert.True(Equals(before[p.Name], p.GetValue(body)), $"{p.Name} changed");
    }

    private static object Sentinel(Type t)
    {
        // 0x25 fits every primitive (sbyte included); non-zero so a reset to default is caught.
        if (t.IsEnum) return Enum.ToObject(t, 0x25);
        if (t == typeof(bool)) return true;
        return Convert.ChangeType(t == typeof(char) ? 'A' : (object)0x25, t);
    }

    // leaf_extra_harness.c test_func_80018818: floor((2^32 - 1) / d), in source order (11 cases).
    [Theory]
    [InlineData(1u, 0xFFFFFFFFu)]
    [InlineData(2u, 0x7FFFFFFFu)]
    [InlineData(3u, 0x55555555u)]
    [InlineData(4u, 0x3FFFFFFFu)]
    [InlineData(5u, 0x33333333u)]
    [InlineData(10u, 0x19999999u)]
    [InlineData(100u, 0x028F5C28u)]
    [InlineData(0x10000u, 0x0000FFFFu)]
    [InlineData(0x7FFFFFFFu, 2u)]
    [InlineData(0x80000000u, 1u)]
    [InlineData(0xFFFFFFFFu, 1u)]
    public void Reciprocal_MatchesLeafExtraHarness(uint divisor, uint expected)
    {
        Assert.Equal(expected, MechMath.Reciprocal(divisor));
    }
}
