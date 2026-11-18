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
        Assert.Equal((8192 << 1) / ShotMotion.ModelUnitsPerPositionUnit, ShotMotion.TickScale(shot));
        // Range: the leash radius is arc << 4 position units, scaled the same way.
        Assert.Equal((8192 << 4) / ShotMotion.ModelUnitsPerPositionUnit, ShotMotion.Range(shot));

        var straight = shot with { AltSpeed = 0 };
        Assert.Equal((8192 << 4) / ShotMotion.ModelUnitsPerPositionUnit, ShotMotion.TickScale(straight));
    }

    [Fact]
    public void AlternateSpeedRampsTowardTheLimitLikeTheMover()
    {
        // func_80035B7C:435-448 with delta = 1: unk84 += accel / 16, clamped at unkD4 << 4.
        // Values derived from the C text: 122/16 = 7 (truncating), 1000/16 = 62, -16/16 = -1.
        Assert.Equal(16391, ShotMotion.AdvanceSpeed(16384, 131072, 122));
        Assert.Equal(16384, ShotMotion.AdvanceSpeed(16384, 131072, 0));
        Assert.Equal(131072, ShotMotion.AdvanceSpeed(131070, 131072, 122));
        Assert.Equal(131072, ShotMotion.AdvanceSpeed(131072, 131072, 122));
        Assert.Equal(2110, ShotMotion.AdvanceSpeed(2048, 8192, 1000));
        Assert.Equal(16383, ShotMotion.AdvanceSpeed(16384, 131072, -16));
    }

    [Fact]
    public void FlightBasisTurnsTheNoseOntoTheVelocity()
    {
        // func_800193F8: rows are the images of the model axes, model +Z is the nose.
        foreach (var (degrees, expectX, expectZ) in new[] { (90.0, 1, 0), (0.0, 0, -1), (-90.0, -1, 0) })
        {
            var basis = ShotMotion.FlightBasis(Shot.Aim(degrees));
            Assert.Equal(0, basis[1]);                     // row0 has no Y component
            Assert.Equal(expectX, basis[6] / 65536);       // row2 = direction
            Assert.Equal(expectZ, basis[8] / 65536);
            // rows stay orthonormal: row0 . row2 == 0 and |row0| == |row1| == 0x10000
            Assert.InRange(((long)basis[0] * basis[6] + (long)basis[2] * basis[8]) >> 16, -1, 1);
            long length = (long)basis[0] * basis[0] + (long)basis[2] * basis[2];
            Assert.InRange(Math.Abs(length - 0x10000L * 0x10000), 0L, 0x20000L);
            // right-handed: row1 is +Y for a horizontal flight, so the nose cannot flip the roll
            Assert.True(basis[4] > 0xFFFF * 0.9, $"row1.y = {basis[4]}");
        }
    }

    [Fact]
    public void FlightBasisAddsPitchWhenTheVelocityClimbs()
    {
        // A 45 degrees upward shot: func_800193F8 keeps the horizontal helper vector and the
        // cross product then tilts row1 away from +Y.
        int[] direction = { 0, 46341, -46341 };
        var basis = ShotMotion.FlightBasis(direction);
        Assert.Equal(0, basis[6]);
        Assert.Equal(46341, basis[7]);
        Assert.Equal(-46341, basis[8]);
        Assert.InRange(basis[4], 0x8000, 0x10000);   // row1.y drops below 1 as the nose climbs
        Assert.Equal(0, basis[1]);
        Assert.Equal(0, basis[2]);
        Assert.Equal(-0x10000, basis[0]);   // row0 stays the horizontal helper of func_800193F8
    }
}
