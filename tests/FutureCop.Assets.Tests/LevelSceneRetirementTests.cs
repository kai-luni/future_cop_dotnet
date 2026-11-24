using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The scene drives the whole retirement chain of a CONFT outpost: player shots collide with it
/// (message 11, 800620D8.c:99-100), the lethal hit writes +0x8C (8002AA1C.c:114-121), the next frame
/// sends message 4 (8004CE04.c:59-60), the retire clears the id (8002AD58.c:89, 8004D958.c:59) and the
/// sweep's message 5 (8004D598.c:54) scores it (8002D664.c:179-225). No step below calls message 4,
/// message 5 or the scoring directly.
/// </summary>
public class LevelSceneRetirementTests
{
    private const uint FirePad = 0x00400000;

    private static string? FindMission()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "build", "disc", "files", "PA", "CONFT.MIS");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    [RetailFact]
    public void ShootingADroneHeldOutpostRetiresItAndScoresMessage5()
    {
        var scene = new LevelScene(FindMission()!);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var field = scene.Objectives!;
        var world = scene.Entities!;
        var score = field.Score;
        var outpost = field.Objectives.Single(o => o.Id == 113);
        var child = outpost.ChildPart!;
        Assert.Equal(500, outpost.Health);
        Assert.Contains(outpost, world.Units);
        Assert.Contains(child, world.Units);

        // Message 10 from the Sky Captain (type 0x25, team 2) claims it with mode 2 (8002D1A0.c:197),
        // so its message 5 credits team F6 = 1, the player's team (8002D664.c:184).
        var drone = scene.SkyCaptain!;
        Assert.Equal(1, field.Touch(new CaptureCandidate(drone.Record!.Id, 0x25, drone.Team,
            outpost.X, outpost.Y, outpost.Z, 0)));
        Assert.Equal(2, outpost.CaptureMode);
        Assert.Equal(new[] { 0, 1 }, score.Score);                          // 8002D1A0.c:204-210

        // Walker placed 0xE000 south of the outpost, facing +z, holding the slot-1 trigger.
        var walker = scene.Walker!;
        walker.X = outpost.X;
        walker.Z = outpost.Z - 0xE000;
        walker.Angle = 0;
        int lethalFrame = -1, frame = 0;
        for (; frame < 600 && !outpost.Freed; frame++)
        {
            scene.HeldPadWord = FirePad;
            scene.AdvanceWalker(1, 0, AnalogPad.Digital);
            if (lethalFrame < 0 && outpost.Health < 0)
            {
                lethalFrame = frame;
                // The lethal hit is booked, the retire waits for the next frame's 8004CE04 pass.
                Assert.NotEqual(0, outpost.RuntimeId);
                Assert.NotEqual(0u, outpost.SourceUnitWord);
                Assert.Equal(new[] { 0, 1 }, score.Score);
            }
        }

        Assert.True(outpost.Freed, $"outpost still alive after {frame} frames, health {outpost.Health}");
        Assert.Equal(lethalFrame + 1, frame - 1);
        Assert.True(child.Freed);                                           // same 8004CE04 pass, child first
        Assert.DoesNotContain(outpost, field.Objectives);
        Assert.DoesNotContain(outpost, world.Units);
        Assert.Equal(31, field.Objectives.Count);
        // 8002D664.c:208-218: the player loop credits slot 0; the 1P state block bumps +0x38.
        Assert.Equal(new[] { 1, 1 }, score.Score);
        Assert.Equal(new[] { 1, 1 }, score.Total);
        Assert.Equal(1, score.OutpostStateCount);
        var hud = scene.ScoreSession(new HudSession());
        Assert.Equal((1, 1), (hud.Score0, hud.Score1));
    }

    [RetailFact]
    public void ShootingAnOpenOutpostRetiresItWithoutScore()
    {
        var scene = new LevelScene(FindMission()!);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var field = scene.Objectives!;
        var outpost = field.Objectives.Single(o => o.Id == 113);
        var walker = scene.Walker!;
        walker.X = outpost.X;
        walker.Z = outpost.Z - 0xE000;
        walker.Angle = 0;
        for (int frame = 0; frame < 600 && !outpost.Freed; frame++)
        {
            scene.HeldPadWord = FirePad;
            scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        }
        Assert.True(outpost.Freed);
        Assert.Equal(0, outpost.CaptureMode);
        Assert.Equal(new[] { 0, 0 }, field.Score.Score);                    // 8002D664.c:183 unkFE == 0
    }
}
