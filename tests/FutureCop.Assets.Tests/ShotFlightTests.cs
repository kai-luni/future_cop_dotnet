using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;
using Xunit.Abstractions;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Retail flight of a direct shot through the mover func_80035B7C: the per-tick path in position
/// units, the leash, the pursuit speed brake (:461-468) and the level's slowed clock.
/// </summary>
public class ShotFlightTests
{
    private readonly ITestOutputHelper _out;
    public ShotFlightTests(ITestOutputHelper output) => _out = output;

    /// <summary>tACT 0x44a as CONFT ships it: speed 8192, alternate word 122, arc 8192.</summary>
    private static ShotDefinition Def44(int speed = 8192, int alt = 122, int arc = 8192) =>
        new(0x44, 0, 3, 0, 44, 25, 0, speed, alt, arc, 0, 0, 0, 0, 0, 0, 0, 0,
            Array.Empty<ResourceReference>());

    private static Shot RetailShot(ShotDefinition def, int pursuitState = 0) =>
        new(def, new Model(), 0, 0, 0, 1.0, 0.0, new[] { 0, 0, 0x10000 },
            positionDivisor: 1, pursuitState: pursuitState);

    [Fact]
    public void DecayHalvesAfterTheStartCountdownThenEvery300Ticks()
    {
        int scale = 16384, word = 1024, countdown = ShotMotion.DecayCountdownStart;
        // func_80035904:237 starts unkDA at 0x5DC; (s16)unkDA < 0 needs 1501 one-tick decrements.
        for (int t = 1; t <= 1500; t++)
            Assert.True(ShotMotion.DecaySpeed(ref scale, ref word, ref countdown, 1));
        Assert.Equal(16384, scale);
        Assert.Equal(0, countdown);

        Assert.True(ShotMotion.DecaySpeed(ref scale, ref word, ref countdown, 1));
        Assert.Equal(8192, scale);                 // unk84 /= 2
        Assert.Equal(8192 >> 4, word);             // unkD4 = (s16)(unk84 >> 4)
        Assert.Equal(299, countdown);              // (u16)-1 + 0x12C

        for (int t = 1; t <= 299; t++) ShotMotion.DecaySpeed(ref scale, ref word, ref countdown, 1);
        Assert.Equal(8192, scale);
        ShotMotion.DecaySpeed(ref scale, ref word, ref countdown, 1);
        Assert.Equal(4096, scale);                 // the next halving 300 ticks later
    }

    [Fact]
    public void DecayKillsTheShotBelowSixteen()
    {
        int word = 0;
        int scale = 32, countdown = 0;
        Assert.True(ShotMotion.DecaySpeed(ref scale, ref word, ref countdown, 1));
        Assert.Equal(16, scale);                   // 16 is not < 16: still alive

        countdown = 0;
        Assert.False(ShotMotion.DecaySpeed(ref scale, ref word, ref countdown, 1));
        Assert.Equal(8, scale);                    // unk84 < 16 -> unk1C = -1

        scale = 31; countdown = 0;
        Assert.False(ShotMotion.DecaySpeed(ref scale, ref word, ref countdown, 1));
        Assert.Equal(15, scale);
    }

    [Fact]
    public void DecayWithFrameDeltaFiresOnTheFirstNegativeCountdown()
    {
        int scale = 16384, word = 1024, countdown = ShotMotion.DecayCountdownStart;
        int calls = 0;
        while (scale == 16384) { ShotMotion.DecaySpeed(ref scale, ref word, ref countdown, 5); calls++; }
        Assert.Equal(301, calls);                  // 1500 / 5 = 300 calls reach 0, the 301st goes negative
        Assert.Equal(295, countdown);              // -5 + 300
    }

    [Fact]
    public void RetailStepFollowsTheMoverTickByTick()
    {
        var shot = RetailShot(Def44());
        Assert.Equal(131072, shot.Range);
        int[] expected = { 16391, 32789, 49194, 65606, 82025, 98451, 114884, 131324 };
        for (int t = 0; t < expected.Length; t++)
        {
            Assert.False(shot.Expired, $"expired before tick {t + 1}");
            shot.Tick(1);
            Assert.Equal(expected[t], shot.Z);
        }
        // s4 = 131324 > (unkA6 << 8) = 131072: func_80035B7C:434 stops the shot.
        Assert.True(shot.Expired);
    }

    [Fact]
    public void RetailFrameStepsLeaveTheLeashInTwoFrames()
    {
        var shot = RetailShot(Def44());
        shot.Tick(5);
        Assert.Equal(16384 + (122 * 5) / 16, shot.SpeedScale);
        Assert.Equal(5 * 16422, shot.Z);
        Assert.False(shot.Expired);
        shot.Tick(5);
        Assert.Equal(82110 + 5 * 16460, shot.Z);
        Assert.True(shot.Expired);
    }

    [Fact]
    public void StraightFlightNeverBrakes()
    {
        // Speed 1 without alternate word: step 16 per tick, leash far beyond 2000 ticks.
        var shot = RetailShot(Def44(speed: 1, alt: 0, arc: 0x7FFF));
        for (int t = 0; t < 2000; t++) shot.Tick(1);
        Assert.Equal(0, shot.PursuitState);
        Assert.Equal(16, shot.SpeedScale);
        Assert.Equal(ShotMotion.DecayCountdownStart, shot.Countdown);
        Assert.False(shot.Expired);
    }

    [Fact]
    public void PursuitWithoutTargetRunsTheBrakeOnceThenFliesStraight()
    {
        var shot = RetailShot(Def44(speed: 1, alt: 0, arc: 0x7FFF), pursuitState: 1);
        shot.Tick(5);
        // func_80035B7C:461 decremented unkDA once, then :512 -> :497 dropped to state 0.
        Assert.Equal(ShotMotion.DecayCountdownStart - 5, shot.Countdown);
        Assert.Equal(0, shot.PursuitState);
        for (int t = 0; t < 2000; t++) shot.Tick(1);
        Assert.Equal(ShotMotion.DecayCountdownStart - 5, shot.Countdown);
        Assert.Equal(16, shot.SpeedScale);
    }

    [Fact]
    public void SlowedClockAdvancesWholeTicks()
    {
        var shot = RetailShot(Def44());
        for (int f = 0; f < 3; f++) shot.AdvanceRetailTime(5.0 / 20.0);
        Assert.Equal(0, shot.Age);
        shot.AdvanceRetailTime(5.0 / 20.0);
        Assert.Equal(1, shot.Age);
        Assert.Equal(16391, shot.Z);
    }

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
    public void LevelShotFliesTheRetailLeashInThirtyTwoFrames()
    {
        var scene = new LevelScene(FindMission()!);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var w = scene.Weapons!;
        for (int s = 0; s < 3; s++)
        {
            var r = w.RecordOf(s);
            if (r is null) continue;
            var h = r.HalfA;
            _out.WriteLine($"slot {s} tACT 0x{h.SectionId:X2}: action 0x{h.Action:X2} def+0x1A {h.ObjWord0E} " +
                           $"def+0x1B {h.SecondaryColumn} damage {h.DamageWord}");
        }

        scene.HeldPadWord = 0x00400000;
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        scene.HeldPadWord = 0;
        var shot = Assert.Single(w.Shots);
        Assert.Equal(131072, shot.Range);
        int frames = 0;
        while (w.Shots.Contains(shot) && frames < 5000) { scene.AdvanceWalker(1, 0, AnalogPad.Digital); frames++; }
        _out.WriteLine($"{w.LastShot}; lived {frames} frames, travelled {shot.Travelled}, ticks {shot.Age}");

        // Eight retail ticks (see LevelWeapons.ShotSlowMotionDefault) at 5 / 20 ticks per frame.
        Assert.Equal(8, shot.Age);
        Assert.Equal(131324, shot.Travelled);
        Assert.Equal((int)(8 * LevelWeapons.ShotSlowMotionDefault / RetailClock.TicksPerFrame), frames);
    }
}
