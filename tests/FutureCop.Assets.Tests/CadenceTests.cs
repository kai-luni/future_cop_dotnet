using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Runs tests/golden/cadence.vectors (fcop-cadence-golden/v1) against the ported weapon trigger.
/// The C harness in the decomp repo (tests/host/cadence_test.sh) drives the ORIGINAL functions
/// func_8006C3E0 and func_8006BA50 with stubs for the registry and the fire dispatcher and compares
/// the very same rows, so the fire rhythm of every weapon is proven on both sides.
/// </summary>
public class CadenceTests
{
    private sealed record Case(string Name, int Burst, int Reload, int Gap, int Cycle, int Delta,
        int Frames, int[] FireFrames);

    private static List<Case> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "cadence.vectors");
        var cases = new List<Case>();
        foreach (var raw in File.ReadAllLines(path))
        {
            int comment = raw.IndexOf('#');
            var line = (comment >= 0 ? raw[..comment] : raw).Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(parts.Length >= 8, $"short row: {line}");
            int count = int.Parse(parts[7]);
            Assert.Equal(8 + count, parts.Length);
            cases.Add(new Case(parts[0], int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]),
                int.Parse(parts[4]), int.Parse(parts[5]), int.Parse(parts[6]),
                parts.Skip(8).Select(int.Parse).ToArray()));
        }
        Assert.NotEmpty(cases);
        return cases;
    }

    [Fact]
    public void TriggerMatchesTheOriginalSlotUpdate()
    {
        foreach (var c in Load())
        {
            var cadence = new WeaponCadence(c.Burst, c.Reload, c.Gap);
            var state = new CadenceState(c.Cycle);
            var fired = new List<int>();
            for (int frame = 0; frame < c.Frames; frame++)
            {
                WeaponTrigger.DecrementTimer(ref state, c.Delta);
                if (WeaponTrigger.TryFire(ref state, cadence)) fired.Add(frame);
            }
            Assert.True(c.FireFrames.SequenceEqual(fired),
                $"{c.Name}: fired at [{string.Join(",", fired)}], expected [{string.Join(",", c.FireFrames)}]");
        }
    }

    [RetailFact]
    public void EveryMissionWeaponFiresAtItsOwnRhythm()
    {
        var mis = TextureGoldenTests.FindRetailMis();
        Assert.NotNull(mis);
        var weapons = WeaponCatalog.Load(new MisFile(mis!));
        Assert.True(weapons.Count >= 16, $"expected the mission's fire halves, got {weapons.Count}");

        // Frames between two shots at retail delta, measured over the first second.
        int FramesFor(ShotDefinition weapon)
        {
            var state = new CadenceState(weapon.Burst);
            var frames = new List<int>();
            for (int frame = 0; frame < 60; frame++)
            {
                WeaponTrigger.DecrementTimer(ref state, RetailClock.TicksPerFrame);
                if (WeaponTrigger.TryFire(ref state, weapon.Cadence)) frames.Add(frame);
            }
            return frames.Count;
        }

        var first = weapons.Single(w => w.SectionId == 0x4D && w.Half == 0);
        Assert.Equal(1, first.Burst);
        Assert.Equal(8, first.ReloadTicks);
        Assert.Equal(0, first.GapTicks);
        Assert.Equal(30, FramesFor(first));      // every second frame, the classic 4Da rhythm

        var second = weapons.Single(w => w.SectionId == 0x4D && w.Half == 1);
        Assert.Equal(12, second.ReloadTicks);
        Assert.Equal(20, FramesFor(second));     // every third frame

        var burst = weapons.Single(w => w.SectionId == 0x4F && w.Half == 1);
        Assert.Equal(8, burst.Burst);
        Assert.Equal(-1, burst.GapTicks);
        Assert.Equal(45, FramesFor(burst));      // eight-frame burst, then three frames of reload

        // Every weapon lands in a plausible range, and the rhythms really differ.
        foreach (var weapon in weapons)
        {
            int shots = FramesFor(weapon);
            Assert.InRange(shots, 1, 60);
        }
        Assert.True(weapons.Select(FramesFor).Distinct().Count() > 3);
    }
}