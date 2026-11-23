using FutureCop.Assets;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The scene has to run the player unit update, not the bare walker. Before this wiring the scene
/// called MechWalker.Tick directly, so the L1 + R1 form change (func_80055988:177-205), the control
/// masks (:260-269) and the hover dispatch (:271-275) were dead code that only the unit tests could
/// reach. Paths below are relative to future-cop-lapd-ps1/src/game.
/// </summary>
public class LevelFormWiringTests
{
    private const uint FormChord = 0x2000000 | 0x800000;

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

    private static LevelScene Attach()
    {
        var scene = new LevelScene(FindMission()!);
        scene.EnableMechMovement();
        scene.EnableFiring();
        return scene;
    }

    [RetailFact]
    public void SceneAppliesTheFormChordAndSwitchesToTheHoverIntegrator()
    {
        var scene = Attach();
        var walker = scene.Walker!;
        Assert.Equal(0, walker.Form);
        Assert.Equal(0, walker.Progress);

        // 80055988.c:177-189 stores the requested form on the L1 + R1 chord, :191-205 starts the
        // transform by setting the progress and playing clip 0xC on the body track.
        scene.HeldPadWord = FormChord;
        scene.AdvanceWalker(1, FormChord, AnalogPad.Digital);
        // Bit 0 of the out word is per call, not a latch: only the step that starts the transform
        // reports it (:191-205 writes state + 0x2FD once).
        Assert.True(scene.FormChanged, "the step that starts the transform reports it");
        for (int step = 1; step < 3; step++)
        {
            scene.AdvanceWalker(1, FormChord, AnalogPad.Digital);
            Assert.False(scene.FormChanged, "later steps do not report a transform");
        }
        Assert.Equal(1, walker.Form);
        Assert.Equal(1, walker.Lock);
        Assert.True(walker.Progress > 0);

        // :207-251 walks the progress up by ticks * 0xA0 with the lock held, and the hover tick
        // then lifts the body: +0xBC scaled by 4096 is the height the C adds at :236-238.
        scene.HeldPadWord = 0;
        for (int step = 0; step < 300; step++) scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.Equal(0x10000, walker.Progress);
        Assert.Equal(0, walker.Lock);
        Assert.False(scene.FormChanged);
        Assert.Equal(1, walker.Form);
    }

    [RetailFact]
    public void SceneReachesTheHoverIntegratorOnTheHeldForm()
    {
        var scene = Attach();
        var walker = scene.Walker!;
        int restingY = walker.Y;

        // func_80055988:271-275 dispatches on state + 0xAD: anything but 1 is gTickOnFootPhysics,
        // 1 is gTickHoverPhysics. Once the transform is done the hover integrator owns the body, so
        // the same movement command produces a different motion than on foot.
        for (int step = 0; step < 3; step++)
        {
            scene.HeldPadWord = FormChord;
            scene.AdvanceWalker(1, FormChord, AnalogPad.Digital);
        }
        scene.HeldPadWord = 0;
        for (int step = 0; step < 300; step++) scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.Equal(1, walker.Form);
        Assert.NotEqual(restingY, walker.Y);
    }

    [RetailFact]
    public void ScenePoseFollowsTheTransformProgress()
    {
        var scene = Attach();
        var walker = scene.Walker!;
        int onFootFrame = scene.WalkerRenderPose.Frame;

        for (int step = 0; step < 300; step++)
        {
            uint flags = step < 3 ? FormChord : 0u;
            scene.HeldPadWord = flags;
            scene.AdvanceWalker(1, flags, AnalogPad.Digital);
        }
        Assert.Equal(0x10000, walker.Progress);

        // The hover pose is the frame func_80055988:227 leaves on the body track (clip 0xD), set on
        // the instance at :248. (func_800518A8 is gUpdateMorphBounds - footprint and bounds, not a
        // pose blend; the earlier comment here was wrong.)
        Assert.NotEqual(onFootFrame, scene.WalkerRenderPose.Frame);
        Assert.Equal(walker.Track.Frame, scene.WalkerRenderPose.Frame);
        var model = new MisFile(FindMission()!).Models;
        var onFoot = PlayerForm.Build(scene.Actor, model, 0);
        var hover = PlayerForm.Build(scene.Actor, model, 0x10000);
        Assert.Equal(onFoot.Count, hover.Count);
        Assert.NotEqual(onFoot[4].Matrix.Ty, hover[4].Matrix.Ty);
    }

    private const uint Forward = 0x40000000;

    [RetailFact]
    public void ScenePoseRunsTheWalkCycleOnFoot()
    {
        // Regression of the progress-driven pose: at state + 0xBC == 0 the scene built the
        // ClipStart(0) frame and the legs stood still while the body walked. On foot the body frame
        // is the track frame func_800549F0:878 advances and :916 sets on the instance.
        var scene = Attach();
        var walker = scene.Walker!;
        var frames = new HashSet<int>();
        for (int step = 0; step < 30; step++)
        {
            scene.HeldPadWord = Forward;
            scene.AdvanceWalker(1, Forward, AnalogPad.Digital);
            Assert.Equal(0, walker.Progress);
            Assert.Equal(walker.Track.Frame, scene.Parts[0].Frame);
            Assert.Equal(walker.Track.Frame, scene.WalkerRenderPose.Frame);
            frames.Add(scene.Parts[0].Frame);
        }
        // Measured on CONFT.MIS: the walk runs through clips 8, 0x26..0x29, frames 3..37.
        Assert.True(frames.Count >= 25, $"walk cycle shows only {frames.Count} distinct frames");
        Assert.Equal(37, scene.Parts[0].Frame);
    }

    [RetailFact]
    public void SceneTransformPoseIsTheTrackScrubAndKeepsTheLiftInThePosition()
    {
        var scene = Attach();
        var walker = scene.Walker!;
        var models = new MisFile(FindMission()!).Models;
        bool sawTransform = false;
        for (int step = 0; step < 120; step++)
        {
            // Out and back: chord at step 0 (to hover) and at step 60 (back to the walker).
            uint flags = step is 0 or 60 ? FormChord : 0u;
            scene.HeldPadWord = flags;
            scene.AdvanceWalker(1, flags, AnalogPad.Digital);
            // func_80055988:240-241 scrub clip 2 / 0xC by state + 0xBC (func_80044B34 type 3),
            // :213/:216/:226-227 select clips 0 / 1 / 0xD at the ends; func_800597C0:254-269 takes the
            // masks from + 0xBC. PlayerForm.Build is the independent closed form of the same.
            var expected = PlayerForm.Build(scene.Actor, models, walker.Progress);
            var parts = scene.Parts;
            Assert.Equal(expected[0].Frame, parts[0].Frame);
            Assert.Equal(expected[1].Frame, parts[1].Frame);
            Assert.Equal(expected[0].Mask, parts[0].Mask);
            Assert.Equal(expected[1].Mask, parts[1].Mask);
            // :235-237 add the lift to obj + 0x54 (the position); func_800597C0 draws the instances
            // at that position without a second lift.
            Assert.Equal(0, parts[0].Matrix.Ty);
            Assert.Equal(PlayerForm.Height(walker.Progress), walker.LiftHeight);
            sawTransform |= walker.Progress is > 0 and < 0x10000;
        }
        Assert.True(sawTransform);
        Assert.Equal(0, walker.Progress);
    }

    [RetailFact]
    public void SpawnStartsTheCompanionTrackOnTheWalkerClip()
    {
        // func_80051AF8:138 selects clip (state + 0xAD == 1) on the companion track at +0xA0; the
        // walker spawn is form 0, so clip 0 - without it func_80055988:194 has no table to scrub.
        var scene = Attach();
        var track = scene.Walker!.CompanionTrack;
        Assert.NotNull(track.Clips);
        Assert.Equal(0, track.ClipIndex);
        Assert.Equal(track.Clips![0].Start, track.Frame);
    }
}
