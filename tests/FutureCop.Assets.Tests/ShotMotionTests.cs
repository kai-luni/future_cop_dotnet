using FutureCop.Assets;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Runs tests/golden/shot-move.vectors (fcop-shot-move-golden/v1) against the ported integrator.
/// The C harness (tests/host/shot_test.sh) runs the ORIGINAL func_8001ACA4 in its in-place form - the
/// way func_80035B7C advances a projectile every tick - so the fixed-point arithmetic has a parity
/// partner on both sides.
/// </summary>
public class ShotMotionTests
{
    private const int Columns = 11;

    private static List<uint[]> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "shot-move.vectors");
        var rows = new List<uint[]>();
        var words = new List<uint>();
        foreach (var raw in File.ReadAllLines(path))
        {
            int comment = raw.IndexOf('#');
            var line = (comment >= 0 ? raw[..comment] : raw).Trim();
            if (line.Length == 0) continue;
            foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                words.Add(Convert.ToUInt32(token, 16));
        }
        Assert.Equal(0, words.Count % Columns);
        for (int i = 0; i < words.Count; i += Columns)
            rows.Add(words.GetRange(i, Columns).ToArray());
        Assert.NotEmpty(rows);
        return rows;
    }

    [Fact]
    public void IntegrationMatchesTheOriginalInPlaceForm()
    {
        foreach (var w in Load())
        {
            var position = new[] { (int)w[1], (int)w[2], (int)w[3] };
            var direction = new[] { (int)w[4], (int)w[5], (int)w[6] };
            ShotMotion.Integrate(position, direction, (int)w[7]);
            for (int i = 0; i < 3; i++)
                Assert.True(unchecked((uint)position[i]) == w[8 + i],
                    $"case {w[0]} component {i}: expected 0x{w[8 + i]:X8}, got 0x{position[i]:X8}");
        }
    }

    [Fact]
    public void StepAndRangeFollowTheSpawnScales()
    {
        var shot = new ShotDefinition(0x4D, 0, 3, 0, 59, 25, 0, 8192, 122, 8192, 0, 0, 0, 0, 0, 0, 0, 0,
            Array.Empty<ResourceReference>());
        // Alternate selector set: the spawner stores speed << 1 (func_8005E47C:87-93), the viewer
        // scales position units to model units.
        Assert.Equal((8192 << 1) / ShotMotion.PositionUnitsPerModelUnit, ShotMotion.TickScale(shot));
        // Range: the leash radius is arc << 4 position units, scaled the same way.
        Assert.Equal((8192 << 4) / ShotMotion.PositionUnitsPerModelUnit, ShotMotion.Range(shot));

        var straight = shot with { AltSpeed = 0 };
        Assert.Equal((8192 << 4) / ShotMotion.PositionUnitsPerModelUnit, ShotMotion.TickScale(straight));
    }

    [Fact]
    public void AlternateSpeedRampsTowardTheLimitLikeTheMover()
    {
        // func_80035B7C:435-448 with the retail frame delta of five ticks:
        // unk84 += (accel * delta) / 16, clamped at unkD4 << 4.
        Assert.Equal(16422, ShotMotion.AdvanceSpeed(16384, 131072, 122, 5));
        Assert.Equal(2360, ShotMotion.AdvanceSpeed(2048, 8192, 1000, 5));
        Assert.Equal(131072, ShotMotion.AdvanceSpeed(131070, 131072, 122, 5));
        Assert.Equal(16384, ShotMotion.AdvanceSpeed(16384, 131072, 0, 5));
        Assert.Equal(131072, ShotMotion.AdvanceSpeed(131072, 131072, 122, 5));
        Assert.Equal(16379, ShotMotion.AdvanceSpeed(16384, 131072, -16, 5));
        // The frame delta is part of the product, not a constant: one tick ramps a fifth as far.
        Assert.Equal(16391, ShotMotion.AdvanceSpeed(16384, 131072, 122, 1));
    }

    [Fact]
    public void OctagonalDistanceIsTheLeashMetric()
    {
        // func_8001A4E8: max + (mid + min) / 4.
        Assert.Equal(0, ShotMotion.OctagonalDistance(new[] { 0, 0, 0 }, new[] { 0, 0, 0 }));
        Assert.Equal(400, ShotMotion.OctagonalDistance(new[] { 0, 0, 0 }, new[] { 400, 0, 0 }));
        Assert.Equal(400 + (0 + 0) / 4, ShotMotion.OctagonalDistance(new[] { 0, 0, 0 }, new[] { 400, 0, 0 }));
        // A diagonal of 400 on two axes is 400 + 400/4, i.e. shorter than the euclidean 566.
        Assert.Equal(500, ShotMotion.OctagonalDistance(new[] { 0, 0, 0 }, new[] { 400, 400, 0 }));
        Assert.Equal(500, ShotMotion.OctagonalDistance(new[] { 400, 400, 0 }, new[] { 0, 0, 0 }));
        // Negative differences are absolute.
        Assert.Equal(500, ShotMotion.OctagonalDistance(new[] { -400, -400, 0 }, new[] { 0, 0, 0 }));
        // Three axes: largest plus a quarter of the sum of the other two.
        Assert.Equal(400 + (300 + 200) / 4, ShotMotion.OctagonalDistance(new[] { 0, 0, 0 }, new[] { 400, 300, 200 }));
    }

    [Fact]
    public void LeashRadiusIsTheRangeWordRoundedDownTo256()
    {
        var shot = new ShotDefinition(0x4D, 0, 3, 0, 59, 25, 0, 8192, 122, 8192, 0, 0, 0, 0, 1, 8, 0, 0,
            Array.Empty<ResourceReference>());
        // arc << 4 = 131072, which is already a multiple of 256.
        Assert.Equal(131072, ShotMotion.LeashRadius(shot));
        Assert.Equal(131072 / 16, ShotMotion.Range(shot));
        // A range word of one: 16 position units, i.e. rounded down to zero - the C would gate on
        // unkA6 << 8 with unkA6 = 0, so such a shot expires at once.
        var tiny = shot with { ArcWord = 1 };
        Assert.Equal(0, ShotMotion.LeashRadius(tiny));
    }
}
