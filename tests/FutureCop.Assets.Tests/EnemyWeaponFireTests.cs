using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

public class EnemyWeaponFireTests
{
    private static AttackDefinition Def(byte type = 1, ushort w1A = 0, ushort w1B = 0, ushort w1C = 0x20)
        => new() { Key = 0x10, Type = type, W02 = 0x0800, W06 = 0x0401, W08 = 0x600,
                   W0A = 0x1000, W12 = 0x50, W14 = 0x30, W16 = 0x40, W1A = w1A, W1B = w1B, W1C = w1C, ResolvedW20 = 0 };

    private static EnemyWorld Env(int seed = 0)
    {
        var env = new EnemyWorld(0, 100);
        env.Random = () => (uint)seed;
        env.TransformPartVertex = (_, _) => (0, 0, 0);
        env.OnPlaySound = (_, _, _, _) => { };
        return env;
    }

    // func_8005F6E8:186-189 (stale id fails) and 455-462 (fail tail state writes,
    // verified against asm main.s:93211-93290 for the s16/lhu widths).
    [Fact]
    public void StaleTargetIdFailsAndPenalisesCooldown()
    {
        var env = Env(seed: 0x100);
        var self = env.NewOwner();
        var target = env.NewOwner();
        env.OnSpawnDirectShot = _ => 1; // must not be reached
        var slot = new AttackSlot
        {
            Definition = Def(),
            Flags = 0,
            AmmoGate20 = 5,
            Playing22 = 1,
            Burst26 = 1,
            FrameAcc24 = 500,
        };
        slot.Target18 = target;
        slot.Target18Value = target.Id + 1;
        Assert.Equal(0, EnemyWeaponFire.Fire(slot, self, null, 1, env));
        Assert.Equal((byte)0, slot.Playing22);                                    // asm 93213
        Assert.Equal((uint)2, slot.Burst26);                                      // asm 93211-93220
        Assert.Equal((ushort)(500 - ((0x100 + 0xC8) & 0xFF)), slot.FrameAcc24); // asm 93284-93290
        Assert.Equal(0, env.DirectShots.Count);
        Assert.Equal(0, env.SoundsPlayed.Count);   // the fail path never reaches the tail
    }

    // func_8005F6E8:193-199 (s4 flag packing).
    [Theory]
    [InlineData((ushort)0x80, 0x80001u)]
    [InlineData((ushort)0x200, 0x201u)]
    [InlineData((ushort)0x600, 0x200u)]
    [InlineData((ushort)0, 0x1u)]
    [InlineData((ushort)0xF80, 0x80200u)]
    public void PackSpawnFlagsMatchesTheOriginal(ushort flags, uint expected)
    {
        Assert.Equal(expected, EnemyWeaponFire.PackSpawnFlags(flags));
    }

    // func_8005F6E8:215-235 (normalised target delta) -> 396-417 (default spawn with
    // the type-3 target kept, 8005F6E8.c:405-408) -> 420-453 (tail: ammo, sound).
    [Fact]
    public void NormalizedAimFromTargetAndMuzzleReachesTheDirectShot()
    {
        var env = Env();
        var self = env.NewOwner();
        var target = env.NewOwner();
        target.Mode16 = 3;               // type 3 keeps the live target for the spawn
        target.Vec5CX = 0x40000;         // target aim position (8005F6E8.c:231-233 reads +0x5C)
        env.OnTraceMapRay = (_, _) => 0; // clear line of sight (8005F6E8.c:397-401)
        env.OnSpawnDirectShot = _ => 1;
        var slot = new AttackSlot
        {
            Definition = Def(type: 3),
            Flags = 0,
            AmmoGate20 = 5,
            Playing22 = 1,
            FrameAcc24 = 0x30,
            Burst26 = 1,
        };
        slot.Target18 = target;
        slot.Target18Value = target.Id;
        Assert.Equal(1, EnemyWeaponFire.Fire(slot, self, null, 1, env));
        var a = Assert.Single(env.DirectShots);
        Assert.Equal(0xFFE0, a.AimX);      // retail gNormalizeVector of (0x40000,0,0)
        Assert.Equal(0, a.AimY);
        Assert.Equal(0, a.AimZ);
        Assert.Equal(3, a.ShotType);
        Assert.Same(target, a.Target);    // type 3 keeps the target reference
        Assert.Equal((short)4, slot.AmmoGate20); // 8005F6E8.c:427-429: ammo decremented
        Assert.Equal((byte)0, slot.Playing22);
        Assert.Single(env.SoundsPlayed);   // 8005F6E8.c:451-453
        Assert.Equal(0x20, env.SoundsPlayed[0].Id);
        Assert.Equal(-1, env.SoundsPlayed[0].Arg);
    }

    // func_8005F6E8:236-238 (type 7 aims along the actor vector at +0x44).
    [Fact]
    public void TypeSevenAimsAlongTheActorVector()
    {
        var env = Env();
        var self = env.NewOwner();
        self.FacingX = 0x10000; self.FacingY = 0; self.FacingZ = 0x8000;
        env.OnSpawnDirectShot = _ => 1;
        var slot = new AttackSlot
        {
            Definition = Def(type: 7),
            Flags = 0,
            AmmoGate20 = 3,
            FrameAcc24 = 0x30,
            Burst26 = 1,
        };
        Assert.Equal(1, EnemyWeaponFire.Fire(slot, self, null, 1, env));
        var a = Assert.Single(env.DirectShots);
        // Retail copies the actor vector verbatim - no normalisation on the type-7 path
        Assert.Equal((0x10000, 0, 0x8000), (a.AimX, a.AimY, a.AimZ));
    }

    // func_8005F6E8:290-300 (jitter flags 0x8 and 0x10 with (rand & 0x1FFF) - 0x1000).
    [Fact]
    public void JitterFlagsPerturbTheAimDeterministically()
    {
        var env = Env();
        env.Random = () => 0x30001FFF;
        var self = env.NewOwner();
        self.FacingX = 0x10000; self.FacingY = 0; self.FacingZ = 0;
        env.OnSpawnDirectShot = _ => 1;
        var slot = new AttackSlot
        {
            Definition = Def(type: 7),
            Flags = 0x8 | 0x10,
            AmmoGate20 = 3,
            FrameAcc24 = 0x30,
            Burst26 = 1,
        };
        Assert.Equal(1, EnemyWeaponFire.Fire(slot, self, null, 1, env));
        var a = Assert.Single(env.DirectShots);
        // flag 0x8 burns one rand first: y += (0x1FFF) - 0x1000 = 0x0FFF
        Assert.Equal(0x0FFF, a.AimY);
        // flag 0x10 burns the same constant rand: x += 0x0FFF; z += ((0x3000 & 0x1FFF)) - 0x1000 = 0
        Assert.Equal(0x10000 + 0x0FFF, a.AimX);
        Assert.Equal(0, a.AimZ);
    }

    // func_8005F6E8:302-320 (angle gate bands: 0x800 hard threshold, else band
    // mask 1/2/4 against the flags' low 0x807 bits).
    [Fact]
    public void AngleGateHardThresholdRejectsSidewaysWithFlag800()
    {
        var env = Env();
        var self = env.NewOwner();
        self.FacingX = 0x10000; self.FacingY = 0; self.FacingZ = 0;
        var target = env.NewOwner();
        target.Mode16 = 3;
        target.Vec5CY = 0x40000;   // aim straight up: dot = 0 <= 0xF22F -> reject
        env.OnTraceMapRay = (_, _) => 0;
        env.OnSpawnDirectShot = _ => 1;
        var slot = new AttackSlot
        {
            Definition = Def(type: 3),
            Flags = 0x800,
            AmmoGate20 = 3,
            Playing22 = 1,
            FrameAcc24 = 0x30,
            Burst26 = 1,
        };
        slot.Target18 = target;
        slot.Target18Value = target.Id;
        Assert.Equal(0, EnemyWeaponFire.Fire(slot, self, null, 1, env));
        Assert.Equal((uint)2, slot.Burst26); // the fail tail ran
    }

    [Theory]
    // Band rows must NOT carry 0x800: bit 0x800 routes into the hard dot <= 0xF22F
    // threshold first (8005F6E8.c:304-307), so the band masks are unreachable with it set.
    [InlineData((ushort)0x001, 0x40000, 0, 0)]        // ahead: dot 0xFFE0 > 0xEC82 -> mask 1, bit 0 -> reject
    [InlineData((ushort)0x006, 0x40000, 0, 1)]        // mask 1, bit 0 clear -> pass
    [InlineData((ushort)0x002, unchecked((int)0xFFFC0000), 0, 0)] // behind: dot -0xFFE0 <= 0xFFFF137D -> mask 2, bit 1 -> reject
    [InlineData((ushort)0x005, unchecked((int)0xFFFC0000), 0, 1)] // mask 2, bit 1 clear -> pass
    [InlineData((ushort)0x004, 0, 0x40000, 0)]        // straight up: dot 0 between the bands -> mask 4, bit 2 -> reject
    [InlineData((ushort)0x003, 0, 0x40000, 1)]        // mask 4, bit 2 clear -> pass
    [InlineData((ushort)0x1000, 0x40000, 0, 1)]       // gate not armed: flags & 0x807 == 0
    [InlineData((ushort)0x800, 0x40000, 0, 1)]        // hard gate armed, dot 0xFFE0 > 0xF22F -> pass
    [InlineData((ushort)0x800, 0, 0x40000, 0)]        // hard gate armed, dot 0 <= 0xF22F -> reject
    public void AngleGateBandMasksRejectOnlyWhenTheFlagBitMatches(ushort flags, int targetX, int targetY, int expected)
    {
        var env = Env();
        var self = env.NewOwner();
        self.FacingX = 0x10000; self.FacingY = 0; self.FacingZ = 0;
        var target = env.NewOwner();
        target.Mode16 = 3;
        target.Vec5CX = targetX;
        target.Vec5CY = targetY;
        env.OnTraceMapRay = (_, _) => 0;
        env.OnSpawnDirectShot = _ => 1;
        var slot = new AttackSlot
        {
            Definition = Def(type: 3),
            Flags = flags,
            AmmoGate20 = 3,
            Playing22 = 1,
            FrameAcc24 = 0x30,
            Burst26 = 1,
        };
        slot.Target18 = target;
        slot.Target18Value = target.Id;
        Assert.Equal(expected, EnemyWeaponFire.Fire(slot, self, null, 1, env));
    }

    // func_8005F6E8:91, 327 (tracer core colour) - the retail probe value.
    [Fact]
    public void TracerCoreColourIsTheRetailWord()
    {
        // SLUS_007.39: little-endian word at D_8009F378 = 0x08087F00.
        Assert.Equal(0x08087F00u, EnemyWeaponFire.TracerCoreColour);
    }

    // func_8005F6E8:422 (a3 low byte 0 keeps the sound tail but skips the effects),
    // 426-429 (state writes are inside the fired branch), 451-453 (sound).
    [Fact]
    public void ZeroFireFlagSkipsEffectsButPlaysSound()
    {
        var env = Env();
        var self = env.NewOwner();
        var slot = new AttackSlot
        {
            Definition = Def(type: 7),
            Flags = 0,
            AmmoGate20 = 9,
            Playing22 = 1,
            FrameAcc24 = 0x30,
            Burst26 = 1,
        };
        int fired = 0;
        env.OnSpawnDirectShot = _ => fired++;
        Assert.Equal(1, EnemyWeaponFire.Fire(slot, self, null, 0, env));
        Assert.Equal(0, fired);
        Assert.Equal((short)9, slot.AmmoGate20);  // ammo untouched on the skip path
        Assert.Equal((uint)1, slot.Burst26);
        Assert.Equal((byte)1, slot.Playing22);    // playing also untouched
        Assert.Single(env.SoundsPlayed);        // but the sound tail ran (8005F6E8.c:451-453)
    }

    // func_8005F6E8:431-449 (secondary spawns driven by definition +0x1B / +0x1A),
    // including the 0x8005FE80 spring-tail (impact column, marker 5/0x800).
    [Fact]
    public void SecondarySpawnsFireForConfiguredColumns()
    {
        var env = Env();
        var self = env.NewOwner();
        self.FacingX = 0x10000; self.FacingY = 0; self.FacingZ = 0;
        var shot = new AttackDefinition();
        env.OnSpawnDirectShot = a => { shot = a.Def; return 0; }; // ret 0 -> pos-buffer path
        var def = Def(type: 7, w1A: 0x0A, w1B: 0x0B);
        var slot = new AttackSlot
        {
            Definition = def,
            Flags = 0,
            AmmoGate20 = 3,
            FrameAcc24 = 0x30,
            Burst26 = 1,
        };
        Assert.Equal(1, EnemyWeaponFire.Fire(slot, self, null, 1, env));
        // the s7 spawn (unk1B column, kind 4) used the position buffer path (ret 0)
        Assert.Contains(env.TypeAtOwnerSpawns, s => s.Column == 0x0B && s.Kind == 4);
        // no tracer for type 7: the 0x8005FE80 tail (unk1A) never ran
        Assert.DoesNotContain(env.TypeAtOwnerSpawns, s => s.Column == 0x0A);
    }

    // func_8005F6E8:323-344 (type 12: tracer, miss message, flagC8 impact tail at 0x8005FE80).
    [Fact]
    public void TypeTwelveSpawnsTracerAndImpactAtFe80Tail()
    {
        var env = Env();
        var self = env.NewOwner();
        self.FacingX = 0x10000; self.FacingY = 0; self.FacingZ = 0;
        var target = env.NewOwner();
        target.Vec5CX = 0x10000; target.Vec5CY = 0; target.Vec5CZ = 0;
        env.OnTraceMapRay = (_, _) => 0;   // no obstruction -> message path taken
        env.OnTargetMessage = _ => { };
        env.OnSpawnTracerBeam = (_, _, _, _) => 1;
        var def = Def(type: 12, w1A: 0x0A);
        var slot = new AttackSlot
        {
            Definition = def,
            Flags = 0,
            AmmoGate20 = 3,
            FrameAcc24 = 0x30,
            Burst26 = 1,
        };
        slot.Target18 = target;
        slot.Target18Value = target.Id;
        Assert.Equal(1, EnemyWeaponFire.Fire(slot, self, null, 1, env));
        Assert.Single(env.TracerBeams);
        Assert.Equal(0x140, env.TracerBeams[0].A0);
        Assert.Equal(0x60, env.TracerBeams[0].A1);
        Assert.Equal(0x30000, env.TracerBeams[0].A2);
        Assert.Equal(0x08087F00u, env.TracerBeams[0].Colour);
        // flagC8 = (unk1A != 0) (8005F6E8.c:340-343): the 0x8005FE80 spawn tail ran
        Assert.Contains(env.TypeAtOwnerSpawns, s => s.Column == 0x0A && s.Kind == 5 && s.MarkerWord == 0x800);
        Assert.Single(env.TargetMessages);
    }

    // func_8005F6E8:362-370 (types 10/11 call gFireRayAttack with subtype 1/8).
    [Theory]
    [InlineData((byte)10, (byte)1)]
    [InlineData((byte)11, (byte)8)]
    public void RayTypesCallTheRayAttackWithTheOriginalSubtype(byte type, byte subtype)
    {
        var env = Env();
        var self = env.NewOwner();
        var rays = new List<(byte Subtype, int A4)>();
        env.OnFireRayAttack = (s, a) => { rays.Add((s, a)); return 0; };
        var slot = new AttackSlot
        {
            Definition = Def(type: type),
            Flags = 0,
            AmmoGate20 = 3,
            FrameAcc24 = 0x30,
            Burst26 = 1,
        };
        Assert.Equal(1, EnemyWeaponFire.Fire(slot, self, null, 1, env));
        // ret = 0 (8005F6E8.c:369) but the tail still reports 1.
        var ray = Assert.Single(rays);
        Assert.Equal(subtype, ray.Subtype);
    }
}
