using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The arm kick of the player mech on a shot: func_8006ACB0:137-139 stamps 0x84 into the recoil timer
/// of the firing arm (slot 1 only) and func_80057E40:202-215 slides that arm's model instance along its
/// own local Z axis while the timer runs. Retail has no fire animation clip, so this is the whole body
/// reaction - and it must be visible in the rendered pose, not only in the timer.
/// </summary>
public class WeaponRecoilTests
{
    private static string? FindMission()
    {
        foreach (var candidate in new[]
                 {
                     "build/disc/files/PA/CONFT.MIS", "../../../../build/disc/files/PA/CONFT.MIS",
                     "../../../../../build/disc/files/PA/CONFT.MIS",
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    [Fact]
    public void KickRunsTwentySevenFramesAndPeaksInTheMiddle()
    {
        var recoil = new WeaponRecoil();
        Assert.False(recoil.Trigger(WeaponSlots.PrimarySlot, 0));
        Assert.False(recoil.Trigger(WeaponSlots.TertiarySlot, 0));
        Assert.True(recoil.Trigger(WeaponSlots.SecondarySlot, 1));
        Assert.Equal(WeaponRecoil.Kick, recoil.Timers[1]);
        Assert.Equal(0, recoil.Timers[0]);

        // 0 ticks at the stamp itself, then the curve of func_80057E40:206-209.
        Assert.Equal(0, WeaponRecoil.ScaleOf(0));
        Assert.Equal(0, WeaponRecoil.ScaleOf(WeaponRecoil.Kick));
        Assert.Equal(-0x2C0, WeaponRecoil.ScaleOf(88));
        // 132 / 8 ticks per frame = 27 frames of visible travel.
        int frames = 0;
        while (recoil.Timers[1] > 0)
        {
            recoil.Advance(RetailClock.TicksPerFrame);
            frames++;
        }
        Assert.Equal(27, frames);
        Assert.Equal(0, recoil.Scales[1]);
    }

    [Fact]
    public void DisplacementFollowsTheMatrixThirdRowLikeVectorMulAdd()
    {
        var arm = Gte.Mat.Identity(0, 0, 0) with { W = new[] { 0x10000, 0, 0, 0, 0x10000, 0, 0x4000, 0, 0xC000 } };
        // scale -0x2C0 against a row of (0, 0.25, -0.75) in a 3x3 that is not an identity.
        var moved = WeaponRecoil.Displace(arm, -0x2C0);
        int Expected(int w) => (-w * 0x2C0) >> 16;
        Assert.Equal(Expected(0x4000), moved.Tx);
        Assert.Equal(Expected(0), moved.Ty);
        Assert.Equal(Expected(0xC000), moved.Tz);
        Assert.Equal(arm.W, moved.W);
        Assert.Equal(arm, WeaponRecoil.Displace(arm, 0));
    }

    [RetailFact]
    public void FiringSlotOneDisplacesTheArmInTheRenderedPose()
    {
        var mis = FindMission();
        Assert.NotNull(mis);
        var scene = new LevelScene(mis!);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var weapons = scene.Weapons;
        Assert.NotNull(weapons);

        Assert.Equal(((0, 0, 0), (0, 0, 0)), scene.ArmRecoil);

        // Square (slot 0) is the beam: retail does not kick for it.
        scene.HeldPadWord = 0x04000000;
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.Equal(0, weapons.Recoil.Timers[0]);
        Assert.Equal(0, weapons.Recoil.Timers[1]);
        Assert.Equal(((0, 0, 0), (0, 0, 0)), scene.ArmRecoil);

        // Cross (slot 1) is the 44a cannon: kick. func_800588D4 decays before the trigger, so the
        // frame that fires still draws the resting arm and the next one shows the travel.
        scene.HeldPadWord = 0x00400000;
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.Contains(weapons.Recoil.Timers, t => t > 0);
        int stamped = weapons.Recoil.Timers.Max();
        Assert.Equal(WeaponRecoil.Kick, stamped);
        var (restRight, restLeft) = scene.ArmRecoil;

        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        var (movedRight, movedLeft) = scene.ArmRecoil;
        Assert.True(movedRight != restRight || movedLeft != restLeft,
            "the arm did not move on the frame after the shot");

        // The travel stays on the arm's own axis: a pure Z displacement in the arm frame.
        var moved = movedRight != restRight ? movedRight : movedLeft;
        Assert.Equal(0, moved.X);
        Assert.Equal(0, moved.Y);
        Assert.NotEqual(0, moved.Z);

        // It peaks, comes back and settles within 27 frames of the last shot.
        scene.HeldPadWord = 0;
        int extreme = 0;
        for (int f = 0; f < 40; f++)
        {
            scene.AdvanceWalker(1, 0, AnalogPad.Digital);
            var (r, l) = scene.ArmRecoil;
            extreme = Math.Max(extreme, Math.Max(Math.Abs(r.Z), Math.Abs(l.Z)));
        }
        Assert.True(extreme > Math.Abs(moved.Z), $"the arm never went further back than {moved.Z}");
        Assert.Equal(((0, 0, 0), (0, 0, 0)), scene.ArmRecoil);
    }
}
