using FutureCop.Assets;
using FutureCop.Game.Mech;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Player side beyond the walker: riding dynamic ground objects (func_80053514), the hover drive
/// (func_80053A74) and the form dispatcher (func_80055988 with func_800518A8). Every expected value
/// is derived by hand from the decomp C (file:line in each assertion comment); none is taken from the
/// port's own output. Paths in the comments are relative to future-cop-lapd-ps1/src/game.
/// </summary>
public class MechHoverFormPlatformTests
{
    private sealed class Surface : IGroundSurface
    {
        public int X { get; set; }
        public int Z { get; set; }
        public int YawValue { get; set; }
        public int YawCalls { get; private set; }
        public int Yaw() { YawCalls++; return YawValue; }
    }

    // ---- func_8007BFCC ----------------------------------------------------------------------

    private static uint[] Masks(PadMapping m) => Enumerable.Range(0, 14).Select(m.Mask).ToArray();

    [Fact]
    public void ControllerPresetsAreButtonLayoutsAndPresetZeroIsTheDefault()
    {
        // 8007BFCC.c:95-101 slot D_8009BFD4[v * 14 + i] = D_8009BEF4[i].value; tables from SLUS_007.39
        // (0x8009BEF4 + 8 + 12i: 1000000 4000000 100000 800000 400000 200000 10000 2000000 8000000
        // 10000000 20000000 40000000 80000000 80000; 0x8009BFD4 preset 0: 10 9 0 2 1 3 12 11 8 7 6 5 4 13).
        // Preset 0 reproduces func_8001E588 (8001E588.c:41-54) exactly.
        Assert.Equal(Masks(PadMapping.Default), Masks(PadMapping.Preset(0)));
        // Preset 1 (9 10 0 2 1 3 12 8 11 ...): slots 8..11 = R2, L2, L1, R1 instead of R1, L1, L2, R2.
        Assert.Equal(new uint[] { 0x100000, 0x400000, 0x800000, 0x200000, 0x80000000, 0x40000000, 0x20000000,
            0x10000000, 0x2000000, 0x1000000, 0x4000000, 0x8000000, 0x10000, 0x80000 }, Masks(PadMapping.Preset(1)));
        // Preset 2 (... 8 5 4 7 6 13): slots 4..7 = Circle, Triangle, Square, Cross.
        Assert.Equal(new uint[] { 0x100000, 0x400000, 0x800000, 0x200000, 0x20000000, 0x10000000, 0x80000000,
            0x40000000, 0x8000000, 0x4000000, 0x1000000, 0x2000000, 0x10000, 0x80000 }, Masks(PadMapping.Preset(2)));
        // :78-86 the value is clamped to 0..3.
        Assert.Equal(Masks(PadMapping.Preset(3)), Masks(PadMapping.Preset(9)));
    }

    // ---- func_80053514 ----------------------------------------------------------------------

    [Fact]
    public void FirstContactOnlyCachesTheSurface()
    {
        var body = new MechBody { X = 0x5000, Z = 0x6000, Angle = 0x100 };
        var s = new Surface { X = 0x10000, Z = 0x20000, YawValue = 0x300 };
        MechPlatform.Ride(body, s);
        // 80053514.c:88 obj->unkC4 != ent: no carry; :118-127 cache refresh, message 0xF asked once.
        Assert.Equal((0x5000, 0x6000, (ushort)0x100), (body.X, body.Z, body.Angle));
        Assert.Same(s, body.Platform);
        Assert.Equal((0x10000, 0x20000, (short)0x300, 0x5000, 0x6000),
            (body.PlatformX, body.PlatformZ, body.PlatformYaw, body.PlatformSelfX, body.PlatformSelfZ));
        Assert.Equal(1, s.YawCalls);
    }

    [Fact]
    public void StandingOnAMovingSurfaceCarriesTheUnit()
    {
        var body = new MechBody { X = 0x5000, Z = 0x6000 };
        var s = new Surface { X = 0x10000, Z = 0x20000 };
        MechPlatform.Ride(body, s);
        s.X += 0x800;
        s.Z -= 0x400;
        MechPlatform.Ride(body, s);
        // 80053514.c:89-90 position += surface - cached surface.
        Assert.Equal((0x5800, 0x5C00), (body.X, body.Z));
        // :92 asks message 0xF; :122 skips the second ask (result != 0x270F).
        Assert.Equal(2, s.YawCalls);
        Assert.Equal((0x10800, 0x1FC00, 0x5800, 0x5C00), (body.PlatformX, body.PlatformZ, body.PlatformSelfX, body.PlatformSelfZ));
    }

    [Fact]
    public void ATurningSurfaceTurnsTheHeadingAndSwingsTheOffset()
    {
        // D_8009A234: sine(0) = 0, sine(0x400) = 0x8000, so gSinCos(0xC00) = (-0x10000, 0).
        Assert.Equal((-0x10000, 0), MechMath.SinCos(0xC00));
        var body = new MechBody { X = 0x11000, Z = 0x20000 };
        var s = new Surface { X = 0x10000, Z = 0x20000, YawValue = 0 };
        MechPlatform.Ride(body, s);
        s.YawValue = 0x400;
        MechPlatform.Ride(body, s);
        // :96-99 delta 0x400: heading (0 + 0x400) & 0xFFF.
        Assert.Equal((ushort)0x400, body.Angle);
        Assert.Equal(MechMath.SinCos(0x400), (body.AxisX, body.AxisZ));
        // :101-113 offset (0x1000, 0) through func_8001A578(-0x400): (x cos - y sin, x sin + y cos)
        // = (0, -0x1000) (8001A578.c:45-60), position += rotated - offset.
        Assert.Equal((0x10000, 0x1F000), (body.X, body.Z));
        Assert.Equal((short)0x400, body.PlatformYaw);
    }

    [Fact]
    public void SteppingOffOrOntoAnotherSurfaceDoesNotCarry()
    {
        var body = new MechBody { X = 0x5000, Z = 0x6000 };
        var a = new Surface { X = 0x10000, Z = 0x20000 };
        MechPlatform.Ride(body, a);
        var b = new Surface { X = 0x30000, Z = 0x40000 };
        MechPlatform.Ride(body, b);
        // :88 a different object: no carry, cache taken from the new one (:120-127).
        Assert.Equal((0x5000, 0x6000), (body.X, body.Z));
        Assert.Equal((0x30000, 0x40000), (body.PlatformX, body.PlatformZ));
        MechPlatform.Ride(body, null);
        // :118 unkC4 = 0; the cache is only written for a non-null object (:119).
        Assert.Null(body.Platform);
        Assert.Equal((0x30000, 0x40000), (body.PlatformX, body.PlatformZ));
    }

    // ---- shared map fixture (CW/M1A1.MIS, the point MechWalkVectorTests uses) ----------------

    private static readonly Vec3 Spot = new(851968, 0x4000, 950272);

    private static (MechBody Body, MechWorld World, int Ground) OnGround(MechWorld? reuse = null)
    {
        var map = reuse?.Map ?? LevelMap.FromMisFile(MechWalkVectorTests.FindMission()!);
        var world = reuse ?? new MechWorld(map);
        int ground = MechGround.Probe(map, Spot).Height;
        var body = new MechBody
        {
            X = Spot.X, Y = ground, Z = Spot.Z, NormalY = 0x10000, AxisZ = 0x10000,
            FrameBounds = new[] { new short[] { 0, 0, 0, 0x40, 0, 0x30 } },
        };
        body.Track.Clips = new AnimationClip[0x0E];
        body.Track.ClipIndex = 0;
        // Footprint corners at the position: seed +0x10C so the first wall rays have zero length.
        MechWalker.PushOffWalls(body, world, 0);
        body.ImpulseX = body.ImpulseZ = 0;
        body.Wall = 0;
        return (body, world, ground);
    }

    [RetailFact]
    public void WalkerRidesAnObjectTheGroundHookReports()
    {
        var (body, world, ground) = OnGround();
        var s = new Surface { X = 0x40000, Z = 0x40000 };
        int top = (ground + 0x200 + 0x80) & -0x100;
        world.GroundObjects = _ => new GroundObjectHit(s, top, new Vec3(0, 0x10000, 0), 0);
        body.Y = top;
        MechWalker.Tick(body, world, 1, 0);
        // 80066F78.c:206-221: the object at or above the terrain wins (height, surface);
        // 800549F0.c:710 func_80053514(self, w.surface) records it.
        Assert.Same(s, body.Platform);
        Assert.Equal(top, body.Y);
        int x0 = body.X, z0 = body.Z;
        s.X += 0x800;
        MechWalker.Tick(body, world, 1, 0);
        // 80053514.c:89-90: a resting walker moves with the object.
        Assert.Equal((x0 + 0x800, z0), (body.X, body.Z));
    }

    // ---- func_80053A74 ----------------------------------------------------------------------

    private static MechBody Hover(MechWorld world, out int ground)
    {
        var (body, _, g) = OnGround(world);
        body.Form = 1;
        ground = g;
        return body;
    }

    [RetailFact]
    public void HoverBanksWithTheAnalogStick()
    {
        var world = new MechWorld(LevelMap.FromMisFile(MechWalkVectorTests.FindMission()!));
        var body = Hover(world, out _);
        world.Pads[0] = new AnalogPad(7, 0xFF, 0x80);
        var sounds = new List<int>();
        world.LoopingSound = (_, t) => sounds.Add(t);
        MechHover.Tick(body, world, 8, 1, 0);
        // 800538C8.c:62-75: x = 0x7F -> 0x20000000, |x| = 0x7F; 80053A74.c:217-219 mode 0x40, track 0x29.
        Assert.Equal(new[] { MechHover.DriveSound }, sounds);
        // :245 bank = (0x7F * 3) >> 2 = 0x5F; :281 heading += (0x5F << 16) >> 17 = 0x2F.
        Assert.Equal((short)0x5F, body.Bank);
        Assert.Equal((ushort)0x2F, body.Angle);
        // :225-230 mode & 0x1F = 0: the idle ramp grows by dt.
        Assert.Equal((byte)0, body.State);
        Assert.Equal((short)8, body.Throttle);

        var left = Hover(world, out _);
        world.Pads[0] = new AnalogPad(7, 0x00, 0x80);
        MechHover.Tick(left, world, 8, 1, 0);
        // x = -0x80: :247 bank = -(0x80 * 96) >> 7 = -0x60; heading += (0xFFA0 << 16) >> 17 = -0x30.
        Assert.Equal((short)-0x60, left.Bank);
        Assert.Equal((ushort)0xFD0, left.Angle);
    }

    [RetailFact]
    public void HoverBankRampsAndClampsOnTheDigitalPad()
    {
        var world = new MechWorld(LevelMap.FromMisFile(MechWalkVectorTests.FindMission()!));
        world.Pads[0] = AnalogPad.Digital;
        var body = Hover(world, out _);
        MechHover.Tick(body, world, 8, 2, 0x20000000);
        // 80053A74.c:252-256 bank += dt per sub-step (8, 16); :281 heading += 8 >> 1 then 16 >> 1.
        Assert.Equal((short)16, body.Bank);
        Assert.Equal((ushort)12, body.Angle);

        var clamped = Hover(world, out _);
        clamped.Bank = 0x5C;
        MechHover.Tick(clamped, world, 8, 1, 0x20000000);
        // 0x5C + 8 = 0x64 >= 0x61 -> 0x60 (:271-274).
        Assert.Equal((short)0x60, clamped.Bank);
    }

    [RetailFact]
    public void HoverModeTableAndSoundFollowTheButtons()
    {
        var world = new MechWorld(LevelMap.FromMisFile(MechWalkVectorTests.FindMission()!));
        world.Pads[0] = AnalogPad.Digital;
        int track = -1;
        world.LoopingSound = (_, t) => track = t;
        // 80053A74.c:176-224 with no sub-step (steps = 0): only mode, ramp and sound are written.
        foreach (var (flags, state, sound) in new (uint, int, int)[]
        {
            (0x08000000, 0x13, MechHover.LiftSound),
            (0x40000000, 3, MechHover.DriveSound),
            (0x42000000, 1, MechHover.DriveSound),
            (0x80000000, 4, MechHover.DriveSound),
            (0x82000000, 2, MechHover.DriveSound),
            (0x00200000, 8, MechHover.DriveSound),
            (0x00100000, 7, MechHover.DriveSound),
            (0x02200000, 6, MechHover.DriveSound),
            (0x02100000, 5, MechHover.DriveSound),
            (0x00000000, 0, MechHover.IdleSound),
        })
        {
            var body = Hover(world, out _);
            body.Throttle = 0x7FC;
            MechHover.Tick(body, world, 8, 0, flags);
            Assert.Equal((byte)state, body.State);
            Assert.Equal(sound, track);
            // :226-233 ramp: + dt and clamp at 0x800 in mode 0, reset otherwise.
            Assert.Equal((short)(state == 0 ? 0x800 : 0), body.Throttle);
        }
    }

    [RetailFact]
    public void HoverFallsWithGravityAndCapsTheHorizontalSpeed()
    {
        var world = new MechWorld(LevelMap.FromMisFile(MechWalkVectorTests.FindMission()!));
        world.Pads[0] = AnalogPad.Digital;
        var body = Hover(world, out int ground);
        body.Y = ground + 0x100000;
        body.Bits = 1;
        body.MotionX = 0x200000;
        int y0 = body.Y, x0 = body.X;
        MechHover.Tick(body, world, 8, 1, 0);
        // 80053A74.c:401-403 airborne: v.y = -0x9800 (no slope term, no vertical drag).
        Assert.Equal(-0x9800, body.MotionY);
        Assert.Equal(y0 - 0x98, body.Y);
        // :484-485 still airborne.
        Assert.Equal(1, body.Bits & 1);
        // :401 0x200000 - (0x200000 * 0xCCC >> 16) = 0x1E6680 > 0x92147 -> :413-416 rescaled:
        // func_8001A7AC: s0 = func_80095900(func_8001A030(v)) << 2, v *= 0x80000000 / (s0 >> 1)
        // (16.16), then func_8001ABE0(0x92147, v).
        const int vx = 0x1E6680;
        int s0 = MechMath.Sqrt12(MechMath.SumSquares2(vx, 0)) << 2;
        int unit = MechMath.Fmul(vx, unchecked((int)(0x80000000u / (uint)(s0 >> 1))));
        Assert.Equal(MechMath.Fmul(unit, MechHover.SpeedLimit), body.MotionX);
        Assert.InRange(body.MotionX, MechHover.SpeedLimit - 0x100, MechHover.SpeedLimit + 0x100);
        Assert.Equal(0, body.MotionZ);
        Assert.Equal(x0 + (body.MotionX >> 8), body.X);
    }

    [RetailFact]
    public void HoverHardLandingSetsTheLandingState()
    {
        var world = new MechWorld(LevelMap.FromMisFile(MechWalkVectorTests.FindMission()!));
        world.Pads[0] = AnalogPad.Digital;
        var body = Hover(world, out int ground);
        body.Y = ground + 0x10;
        body.Bits = 1;
        body.MotionY = -0x60000;
        int damage = MechHover.Tick(body, world, 8, 1, 0);
        // :403-406 vy = -0x60000 - 0x9800 = -0x69800, y += vy >> 8 -> below the floor.
        // :452-462 bit 0 off, bit 1 on, vy dropped, 0x69800 > 0x50000 -> state 0x12;
        // func_80052F54 returns 0 below 0x93333 (80052F54 first test).
        Assert.Equal(2, body.Bits & 3);
        Assert.Equal((byte)0x12, body.State);
        Assert.Equal(0, body.MotionY);
        Assert.Equal(0, damage);
        // :464 snapped to the floor at the new position (+ lift 0).
        Assert.Equal(MechGround.Probe(world.Map, new Vec3(body.X, ground + 0x10 - 0x698 + 0xCCC, body.Z)).Height, body.Y);
    }

    [RetailFact]
    public void HoverComesToRestOnFlatGround()
    {
        var world = new MechWorld(LevelMap.FromMisFile(MechWalkVectorTests.FindMission()!));
        world.Pads[0] = AnalogPad.Digital;
        var body = Hover(world, out _);
        body.MotionX = 0x1000;
        MechHover.Tick(body, world, 8, 1, 0);
        // Normal (0, 1, 0): :369-397 no push, lift 0x9800 cancels gravity; :401 0x1000 - 0xCC;
        // :418-431 mode 0 grounded, no impulse, largest axis < 0x2000 -> all zero.
        Assert.Equal((0, 0, 0), (body.MotionX, body.MotionY, body.MotionZ));
    }

    // ---- func_80055988 ----------------------------------------------------------------------

    [RetailFact]
    public void L1AndR1StartTheTransformation()
    {
        var world = new MechWorld(LevelMap.FromMisFile(MechWalkVectorTests.FindMission()!));
        world.Pads[0] = AnalogPad.Digital;
        var (body, _, _) = OnGround(world);
        int y0 = body.Y;
        int rumble = -1;
        world.Rumble = f => rumble = f;
        uint pad = MechPlayer.FormChord | 0x04000000 | 0x40000000;
        MechPlayer.Update(body, world, 8, 0, ref pad, out bool changed);
        // 80055988.c:177-185 request = 1 - form; :191-204 progress 0: direction 1, form 1, bank 0, bit 0.
        Assert.True(changed);
        Assert.Equal((1, (byte)1, 1, (short)0), (body.Form, body.Request, body.Lock, body.Bank));
        Assert.Equal(0x0C, body.Track.ClipIndex);
        // :222 progress += 8 * 0xA0; :235-237 lift (0x500 << 12) >> 16 = 0x50.
        Assert.Equal(0x500, body.Progress);
        Assert.Equal((short)0x50, body.LiftHeight);
        Assert.Equal(y0 + 0x50, body.Y);
        Assert.Equal(1, rumble);
        // :250 the stored pad word keeps 0x54C0000 only (fire Square = 0x04000000 survives).
        Assert.Equal(0x04000000u, pad);
        // 800518A8.c:103-128 with t = 0x500 and record {0, 0, 0, 0x40, 0, 0x30}:
        // height 174 + (-154 * 0x500 >> 16) = 170, half extents 0x40 / 0x30.
        Assert.Equal(new short[] { -0x40, 170, 0x30, 0x40, 170, 0x30, 0x40, 170, -0x30, -0x40, 170, -0x30 }, body.Footprint);
        // :132-149: centre 294 + (-358 * 0x500 >> 17) = 290, radius 51 * 0xFB00 >> 17 = 25.
        Assert.Equal(((short)315, (short)265), (body.BoundsHigh, body.BoundsLow));
    }

    [RetailFact]
    public void TheTransformationFinishesInHoverAndRevertsToTheWalker()
    {
        var world = new MechWorld(LevelMap.FromMisFile(MechWalkVectorTests.FindMission()!));
        world.Pads[0] = AnalogPad.Digital;
        var (body, _, _) = OnGround(world);
        body.Form = 1; body.Request = 1; body.Lock = 1;
        body.Progress = 0xFF00;
        body.LiftHeight = (short)((0xFF00 << 12) >> 16);
        body.Throttle = 5;
        int y0 = body.Y;
        uint pad = 0;
        MechPlayer.Update(body, world, 8, 0, ref pad, out bool changed);
        // 80055988.c:223-229 clamp at 0x10000, direction 0, clip 0xD, ramp cleared; the hover
        // integrator of the same call then grows it by dt in mode 0 (80053A74.c:226-227).
        Assert.False(changed);
        Assert.Equal((0x10000, (sbyte)0, (short)8), (body.Progress, body.Lock, body.Throttle));
        Assert.Equal(0x0D, body.Track.ClipIndex);
        // :235-237 lift 0x1000 replaces 0xFF0.
        Assert.Equal(y0 + 0x10, body.Y);

        int stopped = -1;
        world.LoopingTracks[0] = MechHover.IdleSound;
        world.LoopingSound = (_, t) => stopped = t;
        pad = MechPlayer.FormChord;
        MechPlayer.Update(body, world, 8, 0, ref pad, out changed);
        // :184 request = 1 - 1 = 0; :198-203 progress != 0: direction -1, form stays 1 until 0.
        Assert.True(changed);
        Assert.Equal(((sbyte)1, (sbyte)-1, 0x10000 - 0x500), (body.Form, body.Lock, body.Progress));

        body.Progress = 0x100;
        pad = 0;
        MechPlayer.Update(body, world, 8, 0, ref pad, out _);
        // :208-220 below 0: progress 0, walker form, clip 0, the looping sound stopped (0xFF).
        Assert.Equal((0, (sbyte)0, (sbyte)0, (byte)0), (body.Progress, body.Lock, body.Form, body.Request));
        Assert.Equal(0, body.Track.ClipIndex);
        Assert.Equal(0xFF, stopped);
    }

    [RetailFact]
    public void TheChordIsRefusedOrIgnoredByTheStateBytes()
    {
        var world = new MechWorld(LevelMap.FromMisFile(MechWalkVectorTests.FindMission()!));
        world.Pads[0] = AnalogPad.Digital;
        var sounds = new List<int>();
        world.Sound = sounds.Add;

        var hazard = OnGround(world).Body;
        hazard.Hazard = 1;
        uint pad = MechPlayer.FormChord;
        MechPlayer.Update(hazard, world, 8, 0, ref pad, out bool changed);
        // 80055988.c:181-182 +0x146 set: sound 0x2A instead of a request.
        Assert.False(changed);
        Assert.Equal(new[] { MechPlayer.RefusedSound }, sounds);
        Assert.Equal((byte)0, hazard.Request);

        var grabbed = OnGround(world).Body;
        grabbed.Grab = 1;
        pad = MechPlayer.FormChord | 0x40000000 | 0x80000;
        MechPlayer.Update(grabbed, world, 8, 0, ref pad, out changed);
        // :180 +0xDE blocks the chord; :254-264 +0xDE keeps only 0xC0000 of the stored word.
        Assert.False(changed);
        Assert.Equal(0x80000u, pad);

        var frozen = OnGround(world).Body;
        frozen.Frozen = 1;
        pad = MechPlayer.FormChord;
        MechPlayer.Update(frozen, world, 8, 0, ref pad, out changed);
        // :173-175 +0x310 clears the working flags.
        Assert.False(changed);
    }

    [RetailFact]
    public void L2AndR2TogetherCancelBothStrafes()
    {
        var world = new MechWorld(LevelMap.FromMisFile(MechWalkVectorTests.FindMission()!));
        world.Pads[0] = AnalogPad.Digital;
        var direct = OnGround(world).Body;
        MechWalker.Tick(direct, world, 1, 0x300000);
        // Without the dispatcher 0x200000 wins in func_800549F0 (move 8, quick strafe).
        Assert.Equal((byte)8, direct.State);

        var body = OnGround(world).Body;
        uint pad = 0x300000;
        MechPlayer.Update(body, world, 8, 1, ref pad, out _);
        // 80055988.c:267-268 flags &= 0xFFCFFFFF: no strafe bit reaches the walker.
        Assert.Equal((byte)0, body.State);
    }

    [RetailFact]
    public void ThePendingShiftIsAppliedOnce()
    {
        var world = new MechWorld(LevelMap.FromMisFile(MechWalkVectorTests.FindMission()!));
        world.Pads[0] = AnalogPad.Digital;
        var body = OnGround(world).Body;
        body.ShiftX = 0x100; body.ShiftZ = -0x200;
        int x0 = body.X, z0 = body.Z;
        uint pad = 0;
        MechPlayer.Update(body, world, 8, 0, ref pad, out _);
        // 80055988.c:163-170.
        Assert.Equal((x0 + 0x100, z0 - 0x200, 0, 0), (body.X, body.Z, body.ShiftX, body.ShiftZ));
    }
}
