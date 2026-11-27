using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The second radial half of a beam: gBeamPartUpdate (func_8006B23C) fires func_800410BC at the
/// segment's far end for a first-half beam whose def + 0x0E spread word is positive, which
/// <see cref="LevelWeapons.DeliverBeamHits"/> wires into the port's beam advance.
/// </summary>
public sealed class BeamRadialSpreadTests
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

    private static ShotDefinition Synthetic(int half, int damage, int spread)
        => new(SectionId: 0x3F, Half: half, Action: 0x0A, Flags: 0, ModelId: 1, ObjWord00: (short)damage,
            DamageWord: spread, SpeedWord: 0, AltSpeed: 0, ArcWord: 6144, ObjWord0E: 0, ObjWord12: 0,
            SoundId: 0, SecondaryColumn: 0, Burst: 0, ReloadTicks: 0, GapTicks: 0, RecordOffset: 0,
            Resources: Array.Empty<ResourceReference>());

    [Fact]
    public void TheSecondHalfSpreadsMessage9AroundItsFarEnd()
    {
        string? mis = FindMission();
        if (mis is null)
        {
            Assert.DoesNotContain('1', Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL") ?? "0");
            return;
        }

        var weapons = new LevelWeapons(new MisFile(mis));
        var beamDef = weapons.RecordOf(WeaponSlots.PrimarySlot)!.HalfB!;
        Assert.Equal(1, beamDef.Half);
        Assert.Equal(2, beamDef.ObjWord00);
        Assert.Equal(512, beamDef.DamageWord);            // def + 0x0E: the spread radius word

        var world = new EntityWorld();
        var target = new BlastProbe(3, 0x100, 0, y: 0x100);
        var bystander = new BlastProbe(4, 0x200, 0x1000, y: 0x100);
        world.Spawn(target);
        world.Spawn(bystander);
        world.CullActive(0x800, 0);

        weapons.BeamHitWorld = world;
        weapons.BeamSourceWord = 0x8021;
        // Reach 0x2000 along +x puts the far end at (0x200, 0x100, 0); the target sits on the
        // segment, the bystander 0x1000 to the side of it and outside its own 0x800 part box.
        weapons.Beams.Add(new Beam(beamDef, 0, 0x100, 0, new[] { 0x100, 0, 0 }, 0x2000));

        int hits = weapons.DeliverBeamHits(world);

        // The direct half: message 14 with def + 0x0C damage and def + 0x1F kind.
        Assert.Equal(1, hits);
        var direct = Assert.Single(target.DirectHits);
        Assert.Equal((short)2, direct.Damage);
        Assert.Empty(bystander.DirectHits);

        // The radial half: radius 512 << 4 = 0x2000, strength def + 0x0C = 2. A target 0x100 from
        // the end keeps ((0x2000 - 0x100) * 0x100000) >> 32 = 1 and one 0x1000 away keeps
        // ((0x2000 - 0x1000) * 0x100000) >> 32 = 1.
        var near = Assert.Single(target.AreaHits);
        Assert.Equal((ushort)1, near.Amount);
        Assert.Equal(0x8021u, near.SourceWord);
        Assert.Null(near.Source);
        var far = Assert.Single(bystander.AreaHits);
        Assert.Equal((ushort)1, far.Amount);
        Assert.Equal(0x8021u, far.SourceWord);

        // A beam whose spread word is not positive does not blast at all (def + 0x0E <= 0), and only
        // the second half carries the radial pass.
        weapons.Beams.Clear();
        target.AreaHits.Clear();
        bystander.AreaHits.Clear();
        weapons.Beams.Add(new Beam(Synthetic(1, 2, 0), 0, 0x100, 0, new[] { 0x100, 0, 0 }, 0x2000));
        weapons.DeliverBeamHits(world);
        Assert.Empty(target.AreaHits);
        Assert.Empty(bystander.AreaHits);

        weapons.Beams.Clear();
        weapons.Beams.Add(new Beam(Synthetic(0, 2, 512), 0, 0x100, 0, new[] { 0x100, 0, 0 }, 0x2000));
        weapons.DeliverBeamHits(world);
        Assert.Empty(target.AreaHits);
        Assert.Empty(bystander.AreaHits);
    }
}
