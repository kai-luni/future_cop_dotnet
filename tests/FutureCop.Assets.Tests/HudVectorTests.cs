using System.Globalization;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Golden vectors of the in-game HUD. The vectors were taken from the retail routines themselves
/// (func_80047BE8 and func_80065744 run in a host harness), so these tests compare the port against
/// the original arithmetic and against the packet fields it writes.
/// </summary>
public class HudVectorTests
{
    public static IEnumerable<object[]> BarCases() => Cases("hud.vectors");

    public static IEnumerable<object[]> RadarCases() => Cases("hud-radar.vectors");

    private static IEnumerable<object[]> Cases(string file)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "golden", file);
        return File.ReadLines(path)
            .Select(line => line.Split('#', 2)[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(tokens => tokens.Length > 0)
            .Select(tokens => new object[] { tokens.Select(Word).ToArray() });
    }

    private static int Word(string token) =>
        unchecked((int)uint.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static (int R, int G, int B) Colour(int[] v, int at) => (v[at], v[at + 1], v[at + 2]);

    [Theory, MemberData(nameof(BarCases))]
    public void GaugeMatchesRetailPacket(int[] v)
    {
        Assert.Equal(86, v.Length);
        int x = v[1], y = v[2], z = v[3], p3 = v[4], p4 = v[5], p5 = v[6];
        bool dim = v[7] != 0 && (v[8] & 0x80) != 0;

        // Height and colour pairs of the gauge.
        Assert.Equal(v[9], HudMath.BarHeight(z));
        var (top, bottom) = HudMath.BarColours(p3, p4, p5, dim);

        // Quad A: top left is the quarter colour, top right the doubled one, the bottom edge swaps both.
        Assert.Equal(Colour(v, 18), top);
        Assert.Equal(Colour(v, 21), bottom);
        Assert.Equal(Colour(v, 24), bottom);
        Assert.Equal(Colour(v, 27), top);

        // Quad B is the mirror image.
        Assert.Equal(Colour(v, 38), bottom);
        Assert.Equal(Colour(v, 41), top);
        Assert.Equal(Colour(v, 44), top);
        Assert.Equal(Colour(v, 47), bottom);

        // The packet stores (x0,y0) top left, (x1,y1) top right, (x2,y2) bottom left, (x3,y3) bottom right.
        var (left, right) = HudMath.BarQuads(x, y, z, top, bottom);
        Assert.Equal((v[10], v[11]), (left.X0, left.Y0));
        Assert.Equal((v[12], v[13]), (left.X1, left.Y0));
        Assert.Equal((v[14], v[15]), (left.X0, left.Y1));
        Assert.Equal((v[16], v[17]), (left.X1, left.Y1));
        Assert.Equal((v[30], v[31]), (right.X0, right.Y0));
        Assert.Equal((v[32], v[33]), (right.X1, right.Y0));
        Assert.Equal((v[34], v[35]), (right.X0, right.Y1));
        Assert.Equal((v[36], v[37]), (right.X1, right.Y1));
        Assert.Equal((left.TopLeft, left.TopRight, left.BottomLeft, left.BottomRight), (top, bottom, bottom, top));
        Assert.Equal((right.TopLeft, right.TopRight, right.BottomLeft, right.BottomRight), (bottom, top, top, bottom));
    }

    [Theory, MemberData(nameof(BarCases))]
    public void GaugeBackdropAndTicksMatchRetailRectangles(int[] v)
    {
        int x = v[1], y = v[2];

        // First recorded rectangle: the blended plate behind the gauge.
        var plate = HudMath.BarBackdrop(x, y);
        Assert.Equal((uint)v[50], HudMath.BarBackdropColour);
        Assert.Equal((v[51], v[52], v[53], v[54]), (plate.X, plate.Y, plate.W, plate.H));
        Assert.Equal(1, v[55]);                                    // blend mode 1, half transparency
        Assert.Equal((0x20, 0x00, 0x00), HudMath.Expand15(HudMath.BarBackdropColour));

        // Five white ticks follow.
        var ticks = HudMath.BarTicks(x, y);
        Assert.Equal(5, ticks.Length);
        for (int i = 0; i < ticks.Length; i++)
        {
            int at = 56 + i * 6;
            Assert.Equal(0x7FFF, v[at]);
            Assert.Equal((v[at + 1], v[at + 2], v[at + 3], v[at + 4]),
                         (ticks[i].X, ticks[i].Y, ticks[i].W, ticks[i].H));
            Assert.Equal(0, v[at + 5]);                            // opaque
        }
        // The retail widening masks with 0x7C, so full white lands on 124 per channel.
        Assert.Equal((0x7C, 0x7C, 0x7C), HudMath.Expand15(0x7FFF));
    }

    [Theory, MemberData(nameof(RadarCases))]
    public void MinimapCellMatchesRetailFold(int[] v)
    {
        Assert.Equal(4, v.Length);
        Assert.Equal(v[3], HudMath.RadarCell(v[1], v[2]));
    }
}
