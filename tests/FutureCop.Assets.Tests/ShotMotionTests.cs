using FutureCop.Assets;
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
        // Alternate selector set: the spawner stores speed << 1 (func_8005E47C:87-93).
        Assert.Equal(8192 << 1, ShotMotion.TickScale(shot));
        // Range: func_80035904:232 stores (arc << 4) >> 8.
        Assert.Equal(8192 >> 4, ShotMotion.Range(shot));

        var straight = shot with { AltSpeed = 0 };
        Assert.Equal(8192 << 4, ShotMotion.TickScale(straight));
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
}
