using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// A beam pulled back to the terrain does not explode: gBeamPartUpdate (func_8006B23C) enters the
/// radial spread only when its map trace missed (<c>(s5 &amp; 0xFF) == 0</c>, :356), so
/// <see cref="Beam.ClipToGround"/> has to report the hit for
/// <see cref="LevelWeapons.DeliverBeamHits"/> to skip the spread of that beam.
/// docs/todo/44-beam-spread-boden-treffer.md.
/// </summary>
public sealed class BeamGroundClipSpreadTests
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

    private static (LevelScene Scene, LevelWeapons Weapons, ShotDefinition BeamDef, int Ground) Load()
    {
        string mis = FindMission()!;
        var scene = new LevelScene(mis);
        scene.EnableMechMovement();
        var (sx, _, sz) = scene.MechWorld;
        // The floor the clip samples, searched from well above the level so the answer does not
        // depend on the walker's body height.
        int ground = LevelGround.Height(scene.Map, sx, 0x200000, sz, out _);

        var weapons = new LevelWeapons(new MisFile(mis));
        var def = weapons.RecordOf(WeaponSlots.PrimarySlot)!.HalfB!;
        Assert.Equal(1, def.Half);
        Assert.Equal(2, def.ObjWord00);
        Assert.Equal(512, def.DamageWord);              // def + 0x0E: the spread radius word
        return (scene, weapons, def, ground);
    }

    /// <summary>
    /// A beam fired into the floor is pulled back to the surface, and that map hit stops the
    /// second-half spread: neither the target on the segment nor a bystander inside the
    /// <c>unk0E &lt;&lt; 4</c> radius receives message 9. The direct-hit callback is unchanged and
    /// still delivers message 14 to the entity the shortened segment reaches.
    /// </summary>
    [ConftFact]
    public void ABeamPulledBackToTheTerrainSpreadsNothing()
    {
        var (scene, weapons, def, ground) = Load();
        var (sx, _, sz) = scene.MechWorld;

        // Straight down from 0x4000 above the floor with a 0x800 reach: the unclipped far end is
        // 0x4000 below the floor, the clip stops at the first sample below the surface and reports the
        // surface height at that XZ.
        var beam = new Beam(def, sx, ground + 0x4000, sz, new[] { 0, -0x10000, 0 }, 0x800);
        Assert.Equal(ground - 0x4000, beam.End.Y);
        beam.ClipToGround(scene.Map);

        // The clip fired, and it moved the far end onto the surface.
        Assert.True(beam.ClippedToGround);
        Assert.Equal(ground, beam.End.Y);
        Assert.Equal(sx, beam.End.X);
        Assert.Equal(sz, beam.End.Z);

        // The scene re-traces every frame (AdvanceBeams): the second call starts from the unclipped
        // far end again, so the flag and the end stay the same instead of decaying.
        beam.ClipToGround(scene.Map);
        Assert.True(beam.ClippedToGround);
        Assert.Equal(ground, beam.End.Y);

        var end = beam.End;
        // On the (shortened) segment, so the direct half must still reach it.
        var target = new BlastProbe(3, beam.X, beam.Z,
            y: beam.Y + (end.Y - beam.Y) / 4, radius: 0x400);
        // 0x1000 from the far end in XZ: inside the 512 << 4 = 0x2000 spread radius, outside the
        // part test of the direct half (0x1000 > its 0x800 radius).
        var bystander = new BlastProbe(4, end.X + 0x1000, end.Z, y: end.Y);

        var world = new EntityWorld();
        world.Spawn(target);
        world.Spawn(bystander);
        world.CullActive(sx, sz);
        Assert.Equal(2, world.Active.Count);

        weapons.BeamHitWorld = world;
        weapons.BeamSourceWord = 0x8021;
        weapons.Beams.Add(beam);

        int hits = weapons.DeliverBeamHits(world);

        // Direct half unchanged: message 14 with the def + 0x0C damage word.
        Assert.Equal(1, hits);
        var direct = Assert.Single(target.DirectHits);
        Assert.Equal((short)2, direct.Damage);
        Assert.Empty(bystander.DirectHits);

        // Radial half suppressed: (s5 & 0xFF) != 0 keeps the C at skip_callback (:356-368).
        Assert.Empty(target.AreaHits);
        Assert.Empty(bystander.AreaHits);
    }

    /// <summary>
    /// The same beam geometry without a floor contact keeps its spread - the gate must not silence
    /// a free beam. It flies 0x100000 above the level, so the sixteen-step clip finds no floor.
    /// </summary>
    [ConftFact]
    public void AFreeBeamStillSpreads()
    {
        var (scene, weapons, def, ground) = Load();
        var (sx, _, sz) = scene.MechWorld;

        var beam = new Beam(def, sx, ground + 0x100000, sz, new[] { 0x10000, 0, 0 }, 0x2000);
        beam.ClipToGround(scene.Map);
        Assert.False(beam.ClippedToGround);

        var end = beam.End;
        var target = new BlastProbe(3, end.X, end.Z, y: end.Y, radius: 0x800);
        var bystander = new BlastProbe(4, end.X, end.Z + 0x1000, y: end.Y, radius: 0x800);

        var world = new EntityWorld();
        world.Spawn(target);
        world.Spawn(bystander);
        world.CullActive(end.X, end.Z);

        weapons.BeamHitWorld = world;
        weapons.BeamSourceWord = 0x8021;
        weapons.Beams.Add(beam);

        Assert.Equal(1, weapons.DeliverBeamHits(world));

        // radius 512 << 4 = 0x2000, strength def + 0x0C = 2: the target sits on the far end and takes
        // the full strength, the bystander 0x1000 away keeps ((0x2000 - 0x1000) * 0x100000) >> 32 = 1.
        var near = Assert.Single(target.AreaHits);
        Assert.Equal((ushort)2, near.Amount);
        Assert.Equal(0x8021u, near.SourceWord);
        var far = Assert.Single(bystander.AreaHits);
        Assert.Equal((ushort)1, far.Amount);
        Assert.Equal(0x8021u, far.SourceWord);
    }
}
