using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Runs tests/golden/mech-form.vectors (fcop-mech-form-golden/v1) against the ported mech form.
/// The C harness in the decomp repo (tests/host/form_test.sh) runs the ORIGINAL func_80044B34 on
/// the same file, so the scrub arithmetic of the transformation has a parity partner on both sides.
/// </summary>
public class PlayerFormTests
{
    private sealed record Vector(int MainStart, int MainEnd, int CompanionStart, int CompanionEnd,
        List<(int Case, int Progress, int MainFrame, int CompanionFrame)> Rows);

    private static Vector Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "mech-form.vectors");
        var rows = new List<(int, int, int, int)>();
        int mainStart = -1, mainEnd = -1, companionStart = -1, companionEnd = -1;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t[0] == "TABLE")
            {
                mainStart = Convert.ToInt32(t[1], 16);
                mainEnd = Convert.ToInt32(t[2], 16);
                companionStart = Convert.ToInt32(t[3], 16);
                companionEnd = Convert.ToInt32(t[4], 16);
                continue;
            }
            rows.Add((Convert.ToInt32(t[0], 16), Convert.ToInt32(t[1], 16),
                Convert.ToInt32(t[2], 16), Convert.ToInt32(t[3], 16)));
        }
        Assert.NotEmpty(rows);
        return new Vector(mainStart, mainEnd, companionStart, companionEnd, rows);
    }

    private static Model ScratchModel(short start, short end)
    {
        var model = new Model();
        model.AnimationClips.Add(new AnimationClip(0, 3, 0, 0, start, end, 0, 0, 0, 10000));
        return model;
    }

    [Fact]
    public void ScrubFramesMatchTheOriginalStepper()
    {
        var v = Load();
        var main = ScratchModel((short)v.MainStart, (short)v.MainEnd);
        var companion = ScratchModel((short)v.CompanionStart, (short)v.CompanionEnd);
        foreach (var row in v.Rows)
        {
            Assert.Equal(row.MainFrame, PlayerForm.ScrubFrame(main, 0, row.Progress));
            Assert.Equal(row.CompanionFrame, PlayerForm.ScrubFrame(companion, 0, row.Progress));
        }
    }

    [Fact]
    public void MasksAndHeightFollowTheRetailTable()
    {
        // func_800597C0: main 0x03/0x05, companion 0x05/0x0B; mid-transition 0x07/0x0F.
        Assert.Equal((0x03, 0x05), PlayerForm.Masks(0));
        Assert.Equal((0x05, 0x0B), PlayerForm.Masks(PlayerForm.FullProgress));
        Assert.Equal((0x07, 0x0F), PlayerForm.Masks(0x8000));
        // func_80055988: height = (progress << 12) >> 16.
        Assert.Equal(0, PlayerForm.Height(0));
        Assert.Equal(0x800, PlayerForm.Height(0x8000));
        Assert.Equal(0x1000, PlayerForm.Height(PlayerForm.FullProgress));
    }

    [Fact]
    public void TransitionRunsFromWalkerToHoverAndStops()
    {
        var form = new PlayerForm();
        Assert.True(form.IsWalker);
        Assert.False(form.Advance(), "A resting form must not move.");

        form.Request(PlayerForm.Hover);
        int ticks = 0;
        while (form.Advance()) ticks++;
        // 0x10000 / 0xA0 = 409.6 retail ticks.
        Assert.Equal((PlayerForm.FullProgress + PlayerForm.ProgressPerTick - 1) / PlayerForm.ProgressPerTick, ticks);
        Assert.True(form.IsHover);
        Assert.Equal(PlayerForm.FullProgress, form.Progress);

        form.Request(PlayerForm.Walker);
        ticks = 0;
        while (form.Advance()) ticks++;
        Assert.True(form.IsWalker);
        Assert.Equal(410, ticks);
    }

    [Fact]
    public void AnimationAdvancesByWallClock()
    {
        var form = new PlayerForm();
        form.Request(PlayerForm.Hover);
        Assert.False(form.AdvanceSeconds(0.001), "Less than one retail tick must not move the progress.");
        Assert.True(form.AdvanceSeconds(1.0 / PlayerForm.TicksPerSecond));
        Assert.Equal(PlayerForm.ProgressPerTick, form.Progress);
        Assert.True(form.AdvanceSeconds(1.0 / PlayerForm.TicksPerSecond, speed: 2.0));
        Assert.Equal(3 * PlayerForm.ProgressPerTick, form.Progress);
    }

    [RetailFact]
    public void ClipTableOfRetailMechMatchesTheStateMachine()
    {
        string? misPath = TextureGoldenTests.FindRetailMis();
        Assert.True(misPath != null, "Retail disc copy (build/disc/files/CW/M1A1.MIS) is absent.");
        var file = new MisFile(misPath!);
        var main = file.Models.First(m => m.Id == 11);
        var companion = file.Models.First(m => m.Id == 12);

        // func_80055988 plays clip 0 (rest), 0xC (scrub) and 0xD (hover) on the main instance.
        Assert.Equal(0, main.AnimationClips[0].Start);
        Assert.Equal(3, main.AnimationClips[0xC].Type);
        Assert.Equal((330, 359), (main.AnimationClips[0xC].Start, main.AnimationClips[0xC].End));
        Assert.Equal(359, main.AnimationClips[0xD].Start);

        // ... and clip 0, 2 (scrub) and 1 on the companion.
        Assert.Equal(0, companion.AnimationClips[0].Start);
        Assert.Equal(3, companion.AnimationClips[2].Type);
        Assert.Equal((0, 29), (companion.AnimationClips[2].Start, companion.AnimationClips[2].End));
        Assert.Equal(29, companion.AnimationClips[1].Start);

        Assert.Equal(0, PlayerForm.Frame(main, 0, 0xC, 0xD, 0));
        Assert.Equal(330, PlayerForm.Frame(main, 0, 0xC, 0xD, 1));
        Assert.Equal(359, PlayerForm.Frame(main, 0, 0xC, 0xD, PlayerForm.FullProgress));
        Assert.Equal(0, PlayerForm.Frame(companion, 0, 2, 1, 0));
        Assert.Equal(29, PlayerForm.Frame(companion, 0, 2, 1, PlayerForm.FullProgress));
    }
}
