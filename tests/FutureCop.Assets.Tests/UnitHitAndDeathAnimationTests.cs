using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The death sequence of the army units and the hit path of the Sky Captain, on the paths the game
/// runs: the Csac spawner of a loaded mission, the pair collision of a live beam
/// (<see cref="LevelWeapons.DeliverBeamHits"/>), the deferred message-4 pass of the frame
/// (8004CE04.c:59-60) and the retirement sweep (8004D598.c:39-71).
/// </summary>
/// <remarks>
/// <para>
/// A type-9 flyer of CONFT.MIS is the only army unit with an animation at all: its model 22 carries
/// three frames and two type-1 clips of ten ticks (records 33 and 34). Tanks (type 0x1C), the other
/// flyer (record 6, model 42) and the type-5 units (model 30) have a single frame and no clip table,
/// so retail has nothing to play for them.
/// </para>
/// <para>
/// The Sky Captain (record 287, type 0x25, model 56) has a single frame as well; its "animation" is
/// the tumble of gHandleTumblingFlyer (80032F3C.c:74-99) and the body basis that gUpdateHoverEnemy
/// rebuilds every frame. Its death script is the record's retire script 1775, which the impact runs
/// through gInterpretEntityScript (800324E0.c:257).
/// </para>
/// </remarks>
public sealed class UnitHitAndDeathAnimationTests
{
    /// <summary>CONFT.MIS record of the team-2 type-9 flyer (model 22, three frames, clip 0-2/10).</summary>
    private const int AnimatedFlyerRecord = 33;

    private static LevelScene LoadScene()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!);
        scene.EnableObjectives();
        scene.EnableMechMovement();
        return scene;
    }

    /// <summary>Spawns one flyer through the retail spawner and returns it.</summary>
    private static SpawnedCsacUnit SpawnFlyer(LevelScene scene)
    {
        scene.EnableCsacSpawner = true;
        Assert.True(scene.CsacSpawner!.ApplyCommandById(AnimatedFlyerRecord, CsacUnitSpawner.CommandQueueBurst, 0));
        for (int i = 0; i < 6 && !scene.CsacSpawner.SpawnedUnits.Any(u => u.SourceSlot.Record.Id == AnimatedFlyerRecord); i++)
            scene.AdvanceSkyCaptain(RetailClock.LocomotionTicks);
        return scene.CsacSpawner.SpawnedUnits.Single(u => u.SourceSlot.Record.Id == AnimatedFlyerRecord);
    }

    [ConftFact]
    public void ADestroyedFlyerTumblesPlaysItsClipAndIsSweptAfterwards()
    {
        var scene = LoadScene();
        var unit = SpawnFlyer(scene);
        var world = scene.Entities!;

        // Model 22 is the only army model with a clip table: three frames, one type-1 clip 0-2/10.
        Assert.NotNull(unit.Animation);
        Assert.True(unit.Animation!.IsAnimated);
        Assert.Equal(2, unit.Animation!.Track.Clips![0].End);
        var mover = Assert.IsType<HoverEnemy>(unit.HoverMover);

        // Player message 14 into gEntityBaseHandler (8002AA1C.c:98-130) - the payload the pair
        // collision of a shot or beam carries.
        unit.HandleMessage(14, world, new DirectHit(0x8000u | 1u, (short)(unit.Health + 1), 0, null));
        Assert.True(unit.Health < 0);

        // The next frame starts with message 6 (the animation) and reaches the deferred pass, which
        // answers the negative health with message 4 (8004CE04.c:59-60).
        int frames = 0;
        while (frames < 4 && mover.FA != 3)
        {
            scene.AdvanceSoloFrame(RetailClock.TicksPerFrame, 0, 0, default);
            frames++;
        }

        // gHandleTumblingFlyer message 4 (80032F3C.c:74-94): state 3, the body dives and spins instead
        // of vanishing, so the unit is still in the world when the sweep of that frame runs.
        Assert.Equal(3, mover.FA);
        Assert.NotEqual(0, unit.RuntimeId);
        Assert.Contains(unit, world.Units);

        // The clip keeps advancing during the dive: one frame per 10 ticks of the frame clock.
        int start = unit.Animation!.Frame;
        for (int i = 0; i < 3; i++) scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.NotEqual(start, unit.Animation.Frame);

        // The impact finishes the sequence: state 5, the body retires through the base handler of
        // gHandleTumblingFlyer (:100-101) and the next sweep unlinks it. No unit is left behind.
        frames = 0;
        while (frames < 600 && unit.RuntimeId != 0)
        {
            scene.AdvanceSoloFrame(RetailClock.TicksPerFrame, 0, 0, default);
            frames++;
        }

        Assert.Equal(0, unit.RuntimeId);
        Assert.DoesNotContain(unit, world.Units);
        Assert.DoesNotContain(unit, world.Active);
    }

    [ConftFact]
    public void AHoverFlyerAdvancesItsClipAtTheRetailFrameRate()
    {
        var scene = LoadScene();
        var unit = SpawnFlyer(scene);
        var animation = Assert.IsType<UnitAnimation>(unit.Animation);
        var mover = Assert.IsType<HoverEnemy>(unit.HoverMover);

        // gAdvanceAnimTrack on the entity track (800324E0.c:162-165) starts on frame 0 of clip 0.
        Assert.Same(animation, mover.Animation);
        Assert.NotEqual(0, mover.AnimCounter);
        Assert.Equal(0, animation.Frame);
        Assert.Equal(10, animation.Track.Clip.Duration);

        // The step fires when the elapsed sum exceeds the ten-tick duration, so the first two frames
        // of the five-tick clock leave the frame where it is and the third crosses the clip step.
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.Equal(0, animation.Frame);
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.Equal(1, animation.Frame);
        Assert.Equal(animation.Frame, mover.F12C);

        // The three-frame clip keeps its rate and wraps back to its first frame, not past it.
        for (int i = 0; i < 12; i++) scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.InRange(animation.Frame, 0, 2);
    }

    [ConftFact]
    public void APlayerBeamHitsTheSkyCaptainAndItsDeathScriptRuns()
    {
        var scene = LoadScene();
        scene.EnableFiring();
        var sky = scene.SkyCaptain!;
        var world = scene.Entities!;
        var target = Assert.IsType<SkyCaptainTarget>(sky.Target);
        var body = sky.Drone!.Body;

        // The body joined the player's hit candidates with the firing setup.
        Assert.Contains(target, scene.Weapons!.ExtraHitTargets);
        Assert.NotNull(target.Coord);

        // A live beam that starts inside the body: func_8006B23C:196-201 delivers message 14 through
        // the pair pass, which now includes the drone block.
        var def = scene.Weapons.RecordOf(WeaponSlots.PrimarySlot)!.HalfB!;
        scene.Weapons.BeamSourceWord = 0x8000u | 1u;
        var beam = new Beam(def, body.Pos[0], body.Pos[1], body.Pos[2], new[] { 0x10000, 0, 0 }, 0x200);
        scene.Weapons.Beams.Add(beam);

        short before = target.Health;
        int hits = scene.Weapons.DeliverBeamHits(world);
        Assert.True(hits == 1, $"hits={hits} coord=({target.Coord!.Value.X},{target.Coord.Value.Y},{target.Coord.Value.Z},r{target.Coord.Value.Radius}) "
            + $"body=({body.Pos[0]},{body.Pos[1]},{body.Pos[2]}) start=({beam.X},{beam.Y},{beam.Z}) "
            + $"end=({beam.End.X},{beam.End.Y},{beam.End.Z})");
        Assert.True(target.Health < before, "the beam has to reduce the health word of the body");
        Assert.Equal(target.Health, body.F1C);

        // The drone word +0x1C is both its health and the approach counter: back within 0x4000 of its
        // strategic node, gUpdateStrategicDrone (8003532C.c:166-207) calls gResetDroneApproach
        // (8003526C.c:121-127), which restores +0x1C from +0x1E. A kill therefore only sticks once the
        // drone has left that radius - the state the player sees while it flies its patrol. Play
        // frames until it is out there, then hit it again through the same beam path.
        var node = sky.Drone.Ai.Pos;
        for (int i = 0; i < 240 && OctagonalDistance(body.Pos, node) <= 0x4000; i++)
            scene.AdvanceSoloFrame(RetailClock.TicksPerFrame, 0, 0, default);
        Assert.True(OctagonalDistance(body.Pos, node) > 0x4000,
            $"the drone has to leave its node radius, dist={OctagonalDistance(body.Pos, node)}");
        // Each frame of trigger hold delivers one hit of s3->unk0C damage, so the kill needs
        // maxHealth / damage hits at most.
        int damage = Math.Max(1, (int)(short)def.ObjWord00);
        for (int i = 0; i < 2 + target.MaxHealth / damage && target.Health >= 0; i++)
        {
            var shot = new Beam(def, body.Pos[0], body.Pos[1], body.Pos[2], new[] { 0x10000, 0, 0 }, 0x200);
            scene.Weapons.Beams.Add(shot);
            Assert.Equal(1, scene.Weapons.DeliverBeamHits(world));
            scene.Weapons.Beams.Remove(shot);
        }
        Assert.True(target.Health < 0, $"health={target.Health} damage={damage}");

        // The death script of the record (retire script 1775) runs at the ground impact, and the body
        // then tumbles, hits the ground and comes back through gRearmDroneWander (800357C0.c:148-161).
        var scripts = new List<(int Offset, int Arg1, int Arg2)>();
        sky.RunScript = (offset, arg1, arg2) => scripts.Add((offset, arg1, arg2));

        int frames = 0;
        while (frames < 600 && body.FA != 5)
        {
            scene.AdvanceSoloFrame(RetailClock.TicksPerFrame, 0, 0, default);
            frames++;
        }

        Assert.Equal(5, body.FA);
        Assert.False(sky.Visible);
        // 800324E0.c:272-273: gInterpretEntityScript(o->f98, 0, -1, o).
        Assert.Contains(scripts, s => s.Offset == sky.Record!.Scripts[1] && s.Offset != 0
            && s.Arg1 == 0 && s.Arg2 == -1);

        int before_health = body.F100;
        Assert.True(before_health > 0);
        frames = 0;
        while (frames < 600 && body.FA == 5)
        {
            scene.AdvanceSoloFrame(RetailClock.TicksPerFrame, 0, 0, default);
            frames++;
        }

        // The corpse timer expired and the body re-armed: health back at its maximum, visible again,
        // and back on its home position with the initial state of the spawn (80035698.c:122-158).
        Assert.NotEqual(5, body.FA);
        Assert.True(sky.Visible);
        Assert.True(target.Health > 0);
        Assert.True(frames > 0);
        Assert.Equal(body.D0X, body.Pos[0]);
        Assert.Equal(body.D4Y, body.Pos[1]);
        Assert.Equal(body.D8Z, body.Pos[2]);

        // The re-armed body stays the same hit candidate and takes damage again, so the player has to
        // shoot it down over and over instead of it lying dead after the first kill. The body never
        // enters the entity list, so its runtime id stays 0 - the pair pass keys on the coordinate.
        Assert.Contains(target, scene.Weapons.ExtraHitTargets);
        int rearmed = target.Health;
        var again = new Beam(def, body.Pos[0], body.Pos[1], body.Pos[2], new[] { 0x10000, 0, 0 }, 0x200);
        scene.Weapons.Beams.Add(again);
        // The re-armed body sits in its patrol among other units, so the pass may deliver more than
        // this one hit; what matters is that the body itself takes damage again.
        Assert.True(scene.Weapons.DeliverBeamHits(world) >= 1);
        scene.Weapons.Beams.Remove(again);
        Assert.True(target.Health < rearmed, "the re-armed body has to take damage again");
    }

    /// <summary>800620D8.c:45-58 octagonal planar distance: |dx| + |dz| - min/2.</summary>
    private static int OctagonalDistance(int[] a, int[] b)
    {
        int dx = Math.Abs(a[0] - b[0]);
        int dz = Math.Abs(a[2] - b[2]);
        return dx + dz - Math.Min(dx, dz) / 2;
    }
}
