using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;
using Xunit.Abstractions;

namespace FutureCop.Assets.Tests;

public class LevelShotFlightTests
{
    private readonly ITestOutputHelper _out;
    public LevelShotFlightTests(ITestOutputHelper output) => _out = output;

    private static string? FindMission()
    {
        foreach (var candidate in new[]
                 {
                     "build/disc/files/PA/CONFT.MIS", "../../../../build/disc/files/PA/CONFT.MIS",
                     "../../../../../build/disc/files/PA/CONFT.MIS",
                 })
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    [RetailFact]
    public void Diagnose()
    {
        var scene = new LevelScene(FindMission()!);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var w = scene.Weapons!;
        _out.WriteLine($"mech bounds {scene.MechBounds}, world {scene.MechWorld}");
        for (int s = 0; s < 3; s++)
        {
            var r = w.RecordOf(s);
            if (r is null) { _out.WriteLine($"slot {s}: none"); continue; }
            foreach (var h in new[] { r.HalfA, r.HalfB })
            {
                if (h is null) continue;
                _out.WriteLine($"slot {s} {r.Name} act 0x{h.Action:X2} speed {h.SpeedWord} alt {h.AltSpeed} arc {h.ArcWord} " +
                               $"step {h.StepWord} range {h.Range} model {h.ModelId} w12 {h.ObjWord12} 1B? sound {h.SoundId}");
            }
        }
        foreach (uint pad in new uint[] { 0x00400000 })
        {
            scene.HeldPadWord = pad;
            scene.AdvanceWalker(1, 0, AnalogPad.Digital);
            scene.HeldPadWord = 0;
            var shot = w.Shots.FirstOrDefault();
            if (shot is null) { _out.WriteLine($"pad {pad:X}: no shot, {w.LastShot}"); continue; }
            var start = (shot.X, shot.Y, shot.Z);
            int frames = 0;
            while (w.Shots.Contains(shot) && frames < 5000) { scene.AdvanceWalker(1, 0, AnalogPad.Digital); frames++; }
            _out.WriteLine($"pad {pad:X}: {w.LastShot}; lived {frames} frames, end {shot.X},{shot.Y},{shot.Z} from {start}, travelled {shot.Travelled} age {shot.Age} range {shot.Range}");
        }
    }
}
