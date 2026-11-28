using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The product path of the beam gate. gBeamPartUpdate (func_8006B23C) traces the segment against the
/// map at :346 on every frame the trigger is held, and the radial spread at :356 reads that result.
/// <see cref="LevelWeapons.AdvanceBeams"/> is the port's per-frame call, so it has to run the trace
/// itself; the scene wires both the map (<see cref="LevelWeapons.BeamGroundMap"/>) and the entity
/// world (<see cref="LevelWeapons.BeamHitWorld"/>) when firing is enabled.
/// </summary>
public sealed class BeamProductPathTests
{
    private static string? FindMission()
    {
        var disc = Environment.GetEnvironmentVariable("FC_RETAIL_DISC");
        if (disc is not null && File.Exists(Path.Combine(disc, "PA", "CONFT.MIS")))
            return Path.Combine(disc, "PA", "CONFT.MIS");
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, "build", "disc", "files", "PA", "CONFT.MIS");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    [ConftFact]
    public void AdvanceBeamsClipsAgainstTheMapBeforeItSpreadsAndHits()
    {
        string mis = FindMission()!;
        var scene = new LevelScene(mis);
        scene.EnableMechMovement();
        var (sx, _, sz) = scene.MechWorld;
        int ground = LevelGround.Height(scene.Map, sx, 0x200000, sz, out _);

        var weapons = new LevelWeapons(new MisFile(mis));
        var def = weapons.RecordOf(WeaponSlots.PrimarySlot)!.HalfB!;
        Assert.Equal(1, def.Half);

        // Straight down into the floor: the unclipped far end is 0x4000 below the surface, the trace
        // pulls it back onto the surface before the spread of the frame runs.
        var beam = new Beam(def, sx, ground + 0x4000, sz, new[] { 0, -0x10000, 0 }, 0x800);
        Assert.Equal(ground - 0x4000, beam.End.Y);
        var end = beam.End;
        // 0x1000 from the far end in XZ: inside the 512 << 4 = 0x2000 spread radius, outside the
        // direct part test.
        var bystander = new BlastProbe(4, end.X + 0x1000, end.Z, y: end.Y);

        var world = new EntityWorld();
        world.Spawn(bystander);
        world.CullActive(sx, sz);

        weapons.BeamGroundMap = scene.Map;
        weapons.BeamHitWorld = world;
        weapons.BeamSourceWord = 0x8021;
        weapons.Beams.Add(beam);

        weapons.AdvanceBeams();

        // The per-frame trace ran and its result reached the spread gate of the same frame.
        Assert.True(beam.ClippedToGround);
        Assert.Equal(ground, beam.End.Y);
        Assert.Empty(bystander.AreaHits);
        Assert.Empty(bystander.DirectHits);

        // Retail re-traces on every frame the beam lives, from the full segment: the gate must still
        // hold on the second and third frame, not only on the first.
        weapons.AdvanceBeams();
        weapons.AdvanceBeams();
        Assert.True(beam.ClippedToGround);
        Assert.Equal(ground, beam.End.Y);
        Assert.Empty(bystander.AreaHits);
    }

    [ConftFact]
    public void AFreeBeamKeepsItsSpreadOnTheProductPath()
    {
        string mis = FindMission()!;
        var scene = new LevelScene(mis);
        scene.EnableMechMovement();
        var (sx, _, sz) = scene.MechWorld;
        int ground = LevelGround.Height(scene.Map, sx, 0x200000, sz, out _);

        var weapons = new LevelWeapons(new MisFile(mis));
        var def = weapons.RecordOf(WeaponSlots.PrimarySlot)!.HalfB!;
        var beam = new Beam(def, sx, ground + 0x100000, sz, new[] { 0x10000, 0, 0 }, 0x2000);
        var end = beam.End;
        var bystander = new BlastProbe(4, end.X, end.Z + 0x1000, y: end.Y);

        var world = new EntityWorld();
        world.Spawn(bystander);
        world.CullActive(end.X, end.Z);

        weapons.BeamGroundMap = scene.Map;
        weapons.BeamHitWorld = world;
        weapons.BeamSourceWord = 0x8021;
        weapons.Beams.Add(beam);

        weapons.AdvanceBeams();

        Assert.False(beam.ClippedToGround);
        Assert.Equal(0x8021u, Assert.Single(bystander.AreaHits).SourceWord);
    }

    [ConftFact]
    public void EnableFiringWiresTheBeamTraceAndCallbackIntoTheScene()
    {
        var scene = new LevelScene(FindMission()!);
        scene.EnableMechMovement();
        scene.EnableFiring();

        Assert.NotNull(scene.Weapons);
        Assert.Same(scene.Map, scene.Weapons.BeamGroundMap);
        Assert.Same(scene.Entities, scene.Weapons.BeamHitWorld);
        Assert.Equal(unchecked((uint)scene.Actor.Id) | 0x8000, scene.Weapons.BeamSourceWord);
    }
}
