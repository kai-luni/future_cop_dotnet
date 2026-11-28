using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// A target for the radial pass: it records the message 9 records it receives and then runs the
/// retail base handler, so the countdown damage path stays in the loop.
/// </summary>
internal sealed class BlastProbe : LiveEntity
{
    private readonly int _recordId;
    private readonly int _x;
    private readonly int _z;

    public BlastProbe(int recordId, int x, int z, int y = 0, int radius = 0x800, uint flags = 0, int health = 500)
    {
        _recordId = recordId;
        _x = x;
        _z = z;
        RuntimeId = recordId;
        Flags = flags;
        Health = (short)health;
        MaxHealth = (short)health;
        Coord = new CollisionCoord(x, y, z, radius, 0x200, 0x200);
    }

    public List<AreaHit> AreaHits { get; } = new();

    public List<DirectHit> DirectHits { get; } = new();

    public override int RecordId => _recordId;

    public override int DispatchType => 5;

    public override (int X, int Z) GroundPosition => (_x, _z);

    public override int HandleMessage(int message, EntityWorld world, object? data)
    {
        if (message == 9 && data is AreaHit area) AreaHits.Add(area);
        if (message == 14 && data is DirectHit direct) DirectHits.Add(direct);
        return BaseHandler(message, this, world, data);
    }
}

/// <summary>The arg3 record func_800410BC reads <c>+0x14</c> from; it is never a blast target.</summary>
internal sealed class BlastSource : LiveEntity
{
    private readonly int _recordId;

    public BlastSource(int recordId) => _recordId = recordId;

    public override int RecordId => _recordId;

    public override int DispatchType => 3;

    public override (int X, int Z) GroundPosition => (0, 0);

    public override int HandleMessage(int message, EntityWorld world, object? data) => 0;
}

/// <summary>
/// func_800410BC gApplyRadialContactDamage against the unchanged PS1 C: every row of
/// <c>tests/golden/radial-blast.vectors</c> was produced by <c>tests/host/radial-blast/harness.c</c>
/// (gcc 2.7.2 -O2, qemu-mipsel) from the original <c>gApplyRadialContactDamage.c</c>,
/// <c>gApplyBandedRadialDamage.c</c>, <c>gApplyConeContactDamage.c</c> and
/// <c>gApproximateDistanceXzHalfMin.c</c>.
/// </summary>
public sealed class RadialContactDamageGoldenTests
{
    private const string VectorFile = "radial-blast.vectors";

    /// <summary>36 inputs before the arrow, 23 outputs after it (see the harness header).</summary>
    public static IEnumerable<object[]> Rows()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "golden", VectorFile);
        Assert.True(File.Exists(path), $"missing golden vectors: {path}");
        foreach (string line in File.ReadLines(path))
        {
            string text = line.Trim();
            if (text.Length == 0 || text[0] == '#') continue;
            int arrow = text.IndexOf("->", StringComparison.Ordinal);
            Assert.True(arrow > 0, $"vector without an arrow: {text}");
            string[] left = text[..arrow].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] right = text[(arrow + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(40, left.Length);                       // name + 39 inputs
            Assert.Equal(23, right.Length);
            var input = new int[39];
            for (int i = 0; i < 39; i++) input[i] = int.Parse(left[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            var output = new int[23];
            for (int i = 0; i < 23; i++) output[i] = int.Parse(right[i], System.Globalization.CultureInfo.InvariantCulture);
            yield return new object[] { left[0], input, output };
        }
    }

    [Fact]
    public void FixtureHoldsEveryCaseTheCHarnessRuns()
    {
        Assert.Equal(57, Rows().Count());
    }

    /// <summary>
    /// The blast writes D_8009F700/D_8009F704 and, per object of D_800A8F98, calls
    /// obj-&gt;unk04(9, obj, &amp;args, 0): the outputs are the factor, the radius and per object the
    /// call count, the message, <c>args.unk0</c> (arg3-&gt;unk14) and <c>args.unkE</c> (the strength).
    /// </summary>
    [Theory]
    [MemberData(nameof(Rows))]
    public void MatchesTheOriginalMips(string name, int[] v, int[] e)
    {
        var world = new EntityWorld();
        var probes = new BlastProbe[4];
        var list = new List<LiveEntity>();
        int count = v[14];
        for (int i = 0; i < 4; i++)
        {
            int b = 15 + 6 * i;
            probes[i] = new BlastProbe(v[b], v[b + 2], v[b + 3], v[b + 4], v[b + 5], unchecked((uint)v[b + 1]));
            if (i < count) list.Add(probes[i]);
        }

        var source = new BlastSource(v[5]);
        LiveEntity? arg3 = v[6] != 0 ? source : null;
        // func_800410BC reads args.unk0 from arg3->unk14 and writes 0 when arg3 is null; the port
        // takes the word from the caller, so an absent record passes 0 here.
        uint sourceWord = arg3 is null ? 0u : unchecked((uint)v[5]);
        var pos = new[] { v[0], v[1], v[2] };
        var parameters = new RadialBlastParameters(v[7], v[8] != 0, (byte)v[9], (short)v[10], (short)v[11], (short)v[12]);
        RadialBlastField field = v[13] switch
        {
            1 => RadialContactDamage.ApplyBandedRadialDamage(list, world, pos, v[3], v[4], sourceWord, arg3, v[7], (byte)v[9]),
            2 => RadialContactDamage.ApplyConeContactDamage(list, world, pos, v[3], v[4], sourceWord, arg3, v[7],
                new[] { v[11] << 4, 0, v[12] << 4 }, v[10] << 4),
            _ => RadialContactDamage.Apply(list, world, pos, v[3], v[4], sourceWord, arg3, parameters),
        };

        Assert.Equal(e[0], field.StrengthScale);
        Assert.Equal(e[1], field.Radius);

        for (int i = 0; i < 4; i++)
        {
            int o = 2 + 4 * i;
            Assert.Equal(e[o], probes[i].AreaHits.Count);
            if (e[o] == 0) continue;
            var hit = Assert.Single(probes[i].AreaHits);
            Assert.Equal(9, e[o + 1]);
            Assert.Equal(e[o + 2], unchecked((int)hit.SourceWord));
            Assert.Equal(e[o + 3], (short)hit.Amount);
            Assert.Same(arg3, hit.Source);
        }

        // The C stores the centre pointer verbatim (args.unk4) and the arg3 pointer (args.unk8).
        Assert.Equal(1, e[18]);
        Assert.Equal(1, e[19]);

        // The wrappers clear D_8009F324 and the enable/band word after the call; a plain blast leaves
        // the words at whatever the caller had there. The port holds no such state at all.
        if (v[13] == 0)
        {
            Assert.Equal(v[7], e[20]);
            Assert.Equal(v[8], e[21]);
            Assert.Equal(v[9], e[22]);
        }
        else
        {
            Assert.Equal(0, e[20]);
            Assert.Equal(0, e[21]);
            Assert.Equal(0, e[22]);
        }
    }
}

/// <summary>
/// The acceptance list of docs/todo/42-radial-contact-damage.md, one case per filter, on a list that
/// does not come from <see cref="EntityWorld.CullActive"/> so the flags stay exactly as built.
/// </summary>
public sealed class RadialContactDamageTests
{
    private static (EntityWorld World, List<LiveEntity> List) Scene()
    {
        return (new EntityWorld(), new List<LiveEntity>());
    }

    private static RadialBlastField Blast(List<LiveEntity> list, EntityWorld world, int strength, int radius,
        int[]? pos = null, RadialBlastParameters parameters = default)
        => RadialContactDamage.Apply(list, world, pos ?? new[] { 0, 0, 0 }, strength, radius, 0x800Cu, null, parameters);

    [Fact]
    public void TheRadiusCutsOffAtAndBeyondTheWord()
    {
        // :168 skips when func_8001A750(pos, obj+0x50) is not below gFalloffRadius.
        var (world, list) = Scene();
        var inside = new BlastProbe(3, 0xC00, 0);
        var edge = new BlastProbe(4, 0x1000, 0);
        var outside = new BlastProbe(5, 0x1001, 0);
        list.AddRange(new[] { inside, edge, outside });

        Blast(list, world, 100, 0x1000);

        Assert.Single(inside.AreaHits);
        Assert.Empty(edge.AreaHits);
        Assert.Empty(outside.AreaHits);
    }

    [Fact]
    public void TheDeadZoneSkipsObjectsInsideTheContextWord()
    {
        // :169 keeps an object only when d >= D_8009F324, so an exactly equal distance is inside.
        var (world, list) = Scene();
        var tooClose = new BlastProbe(3, 0x7FF, 0);
        var atEdge = new BlastProbe(4, 0x800, 0);
        list.AddRange(new[] { tooClose, atEdge });

        Blast(list, world, 100, 0x1000, parameters: new RadialBlastParameters(0x800, false, 0, 0, 0, 0));

        Assert.Empty(tooClose.AreaHits);
        Assert.Single(atEdge.AreaHits);
    }

    [Fact]
    public void TheVerticalBandTestsTheCoordPlusMinusTheThousandUnitSlack()
    {
        // :171-182: the blast centre's y must stay below obj+0x60 + obj+0x68 + 0x1000 and, unless bit
        // 1 of D_8009F329 is set, at or above obj+0x60 - obj+0x68 - 0x1000.
        int HitsAt(int blastY, byte bandBits)
        {
            var (world, list) = Scene();
            var probe = new BlastProbe(3, 0x800, 0, y: 0x2000, radius: 0x800);
            list.Add(probe);
            RadialContactDamage.Apply(list, world, new[] { 0, blastY, 0 }, 100, 0x2000, 0, null,
                new RadialBlastParameters(0, false, bandBits, 0, 0, 0));
            return probe.AreaHits.Count;
        }

        Assert.Equal(0, HitsAt(0x2000 - 0x800 - 0x1000 - 1, 1));   // below the floor
        Assert.Equal(1, HitsAt(0x2000 - 0x800 - 0x1000, 1));       // floor edge is inclusive
        Assert.Equal(1, HitsAt(0x2000, 1));                        // centre
        Assert.Equal(1, HitsAt(0x2000 + 0x800 + 0x1000, 1));       // ceiling edge is inclusive
        Assert.Equal(0, HitsAt(0x2000 + 0x800 + 0x1000 + 1, 1));   // above the ceiling

        // Bit 1 drops the floor test, so the same low centre now passes the ceiling.
        Assert.Equal(1, HitsAt(0x2000 - 0x800 - 0x1000 - 1, 3));

        // Without bit 0 the band is not tested at all.
        Assert.Equal(1, HitsAt(0x2000 + 0x800 + 0x1000 + 1, 0));
    }

    [Fact]
    public void TheConeKeepsOnlyObjectsInFrontOfItsAxis()
    {
        // :184-192: tx = ((obj.x - pos.x) << 4) / (d >> 8), the projection must reach
        // D_8009F710 after shifting each product right by twelve.
        var (world, list) = Scene();
        var front = new BlastProbe(3, 0x800, 0);
        var behind = new BlastProbe(4, -0x800, 0);
        var side = new BlastProbe(5, 0, 0x800);
        list.AddRange(new[] { front, behind, side });
        var cone = new RadialBlastParameters(0, true, 0, 0x400, 0x1000, 0);

        Blast(list, world, 100, 0x1000, parameters: cone);

        Assert.Single(front.AreaHits);
        Assert.Empty(behind.AreaHits);
        Assert.Empty(side.AreaHits);
    }

    [Fact]
    public void TheFalloffRoundsDownAndDropsAZeroStrength()
    {
        // :194-200: s0 = ((radius - d) * gLightStrengthScale) >> 16, then s0 >>= 16. A strength of 1
        // with d one unit below the radius leaves (1 * 0x1000000) >> 32, which is zero.
        var (world, list) = Scene();
        var probe = new BlastProbe(3, 0xFFF, 0);
        list.Add(probe);

        var field = Blast(list, world, 1, 0x1000);

        Assert.Equal(0x100000, field.StrengthScale);
        Assert.Empty(probe.AreaHits);

        // One unit further in, the same words produce exactly one point of strength.
        var (nearWorld, nearList) = Scene();
        var near = new BlastProbe(3, 0x800, 0);
        nearList.Add(near);
        Blast(nearList, nearWorld, 100, 0x1000);
        var hit = Assert.Single(near.AreaHits);
        Assert.Equal((ushort)50, hit.Amount);
    }

    [Fact]
    public void TheSkipFlagsDropIgnoredAndIdlessObjects()
    {
        // :158-161: obj+0x08 <= 0 and (obj+0x24 & 0x200080) != 0 never receive the message.
        var (world, list) = Scene();
        var ignored = new BlastProbe(3, 0x400, 0, flags: 0x80);
        var gated = new BlastProbe(4, 0x400, 0, flags: 0x200000);
        var both = new BlastProbe(5, 0x400, 0, flags: 0x200080);
        var retired = new BlastProbe(0, 0x400, 0);
        var negative = new BlastProbe(-1, 0x400, 0);
        var allowed = new BlastProbe(6, 0x400, 0, flags: 0x800002);
        list.AddRange(new[] { ignored, gated, both, retired, negative, allowed });

        Blast(list, world, 100, 0x1000);

        Assert.Empty(ignored.AreaHits);
        Assert.Empty(gated.AreaHits);
        Assert.Empty(both.AreaHits);
        Assert.Empty(retired.AreaHits);
        Assert.Empty(negative.AreaHits);
        Assert.Single(allowed.AreaHits);
    }

    [Fact]
    public void TheStrengthKeepsOnlyItsLowSixteenBits()
    {
        // The function starts with arg1 << 16, so 0x10000 and 0 produce the same zero factor, while
        // 0x10001 behaves like 1.
        var (world, list) = Scene();
        var probe = new BlastProbe(3, 0x800, 0);
        list.Add(probe);

        var zero = Blast(list, world, 0x10000, 0x1000);
        var one = Blast(list, world, 0x10001, 0x1000);

        Assert.Equal(0, zero.StrengthScale);
        Assert.Equal(0x100000, one.StrengthScale);
        Assert.Equal(0, probe.AreaHits.Count);
    }

    [Fact]
    public void TheSignOfTheFactorFollowsStrengthAndRadius()
    {
        // :146-160: the two signs select the factor's sign, and a negative radius is negated before
        // the >> 1, so -0x1000 halves into 0x800 like the positive word.
        var (world, list) = Scene();
        var probe = new BlastProbe(3, 0x800, 0);
        list.Add(probe);

        // A negative strength keeps the object in the walk (the strength is only zero-checked) and
        // sends a negative amount: ((0x1000 - 0x800) * -0x640000) >> 32 = -50.
        var negativeStrength = Blast(list, world, -100, 0x1000);
        Assert.True(negativeStrength.StrengthScale < 0);
        Assert.Equal(unchecked((ushort)-50), Assert.Single(probe.AreaHits).Amount);

        // A negative radius fails the d < radius test for every object, so nothing is sent.
        var (radiusWorld, radiusList) = Scene();
        var behind = new BlastProbe(3, 0x800, 0);
        radiusList.Add(behind);
        var negativeRadius = Blast(radiusList, radiusWorld, 100, -0x1000);
        Assert.True(negativeRadius.StrengthScale < 0);
        Assert.Equal(-0x1000, negativeRadius.Radius);
        Assert.Empty(behind.AreaHits);
    }

    [Fact]
    public void AZeroHalfRadiusTrapsLikeTheOriginalDivision()
    {
        // 0x80000000 / (|arg2| >> 1) divides by zero for the words 0, 1 and -1; the port reports it
        // instead of letting the process take the retail exception.
        var (world, list) = Scene();
        list.Add(new BlastProbe(3, 0x400, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => Blast(list, world, 100, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Blast(list, world, 100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Blast(list, world, 100, -1));
    }

    [Fact]
    public void TheBandedWrapperForcesTheBandBitOn()
    {
        // func_800413EC stores mode | 1 into D_8009F329, so mode 2 alone still runs the ceiling test.
        var (world, list) = Scene();
        var below = new BlastProbe(3, 0x400, 0, y: 0, radius: 0x800);
        list.Add(below);

        RadialContactDamage.ApplyBandedRadialDamage(list, world, new[] { 0, 0x4000, 0 }, 100, 0x1000, 0, null, 0, 2);

        Assert.Empty(below.AreaHits);
    }

    [Fact]
    public void TheConeWrapperProjectsTheAxisAndTheThreshold()
    {
        // func_80041428 shifts the position and the height right by four before storing them as s16.
        var (world, list) = Scene();
        var onAxis = new BlastProbe(3, 0x800, 0);
        var behind = new BlastProbe(4, -0x800, 0);
        list.AddRange(new[] { onAxis, behind });

        RadialContactDamage.ApplyConeContactDamage(list, world, new[] { 0, 0, 0 }, 100, 0x1000, 0, null, 0,
            new[] { 0x10000, 0, 0 }, 0x4000);

        Assert.Single(onAxis.AreaHits);
        Assert.Empty(behind.AreaHits);
    }
}

/// <summary>
/// func_80036624:164-174, the message-4 blast of a direct-shot projectile, through
/// <see cref="ProjectileContact"/> and the spawn words of func_80035904.
/// </summary>
public sealed class ProjectileRadialBlastTests
{
    private static (EntityWorld World, BlastProbe Probe) Scene(params BlastProbe[] probes)
    {
        var world = new EntityWorld();
        foreach (var probe in probes) world.Spawn(probe);
        world.CullActive(0, 0);
        return (world, probes[0]);
    }

    [Fact]
    public void Message4BlastsAroundTheProjectileAndRetires()
    {
        // +0xD0 = 32 becomes radius 0x2000; the amount starts at +0x1A = 40 and the target 0x400 away
        // receives ((0x2000 - 0x400) * 0x1400000) >> 32 = 35.
        var (world, probe) = Scene(new BlastProbe(3, 0x800, 0));
        var projectile = new ProjectileContact(12, 40, 0, 0x400, 0, 0x100, new ProjectileBlast(32, 384, false, 0, 0, 0));

        int result = projectile.HandleMessage(4, world, null);

        Assert.Equal(1, result);
        var hit = Assert.Single(probe.AreaHits);
        Assert.Equal((ushort)35, hit.Amount);
        Assert.Equal(0x800Cu, hit.SourceWord);
        Assert.Null(hit.Source);
        Assert.Equal(0, projectile.RuntimeId);
        Assert.True((projectile.Flags & LiveEntity.RetiredFlag) != 0);
    }

    [Fact]
    public void Message4WithoutARadiusWordSkipsTheBlast()
    {
        // :164-165 gates on a positive +0xD0; the projectile still retires (:175).
        var (world, probe) = Scene(new BlastProbe(3, 0x800, 0));
        var projectile = new ProjectileContact(12, 40, 0, 0x400, 0, 0x100, ProjectileBlast.None);

        projectile.HandleMessage(4, world, null);

        Assert.Empty(probe.AreaHits);
        Assert.Equal(0, projectile.RuntimeId);
    }

    [Fact]
    public void Message4WithAZeroDamageSkipsTheBlast()
    {
        // :173-174 only calls the radial pass for a positive amount.
        var (world, probe) = Scene(new BlastProbe(3, 0x400, 0));
        var projectile = new ProjectileContact(12, 0, 0, 0x400, 0, 0x100, new ProjectileBlast(32, 384, false, 0, 0, 0));

        projectile.HandleMessage(4, world, null);

        Assert.Empty(probe.AreaHits);
        Assert.Equal(0, projectile.RuntimeId);
    }

    [Fact]
    public void Message4ScalesTheAmountByTheDistanceFromTheSpawnWords()
    {
        // :166-169 with bit 0 of +0xE4: len = gSqrt12(gSquaredLength3Q16(spawn<<8 - position)). The
        // spawn words hold x = 0x40 (0x4000 after << 8) and y = 1 (0x100); the projectile sits at
        // (0, 0x100, 0), so the sum of squares is (0x4000 * 0x4000) >> 16 = 0x1000 and the retail
        // root of 0x1000 is 0x1000 (gSqrt12 returns the root times 64). Then
        // ((0x1000 << 2) >> 8) * 40 / 384 = 6 and 40 - 6 = 34. The target at the blast centre
        // receives exactly that strength back.
        var (world, probe) = Scene(new BlastProbe(3, 0, 0));
        var projectile = new ProjectileContact(12, 40, 0, 0, 0, 0x100, new ProjectileBlast(32, 384, true, 0x40, 1, 0));

        projectile.HandleMessage(4, world, null);

        Assert.Equal((ushort)34, Assert.Single(probe.AreaHits).Amount);
    }

    [Fact]
    public void Message4HonoursTheProjectileSkipFlags()
    {
        // The blast walks the active list, so the 0x200080 gate applies to it like any other hit.
        var (world, probe) = Scene(new BlastProbe(3, 0x400, 0, flags: 0x200080));
        var projectile = new ProjectileContact(12, 40, 0, 0x400, 0, 0x100, new ProjectileBlast(32, 384, false, 0, 0, 0));

        projectile.HandleMessage(4, world, null);

        Assert.Empty(probe.AreaHits);
    }

    [Fact]
    public void TheSpawnWordsComeFromTheDefinitionWords()
    {
        // func_80035904: +0xD0 = (def+0x0E << 4) >> 8, +0xA6 = (def+0x16 << 4) >> 8, bit 0 of +0xE4
        // from bit 1 of the definition's flags byte, and the spawn position >> 8.
        var definition = new ShotDefinition(
            SectionId: 0x3F, Half: 1, Action: 0x0A, Flags: 2, ModelId: 1, ObjWord00: 2,
            DamageWord: 512, SpeedWord: 0, AltSpeed: 0, ArcWord: 6144, ObjWord0E: 0, ObjWord12: 0,
            SoundId: 0, SecondaryColumn: 0, Burst: 0, ReloadTicks: 0, GapTicks: 0, RecordOffset: 0,
            Resources: Array.Empty<ResourceReference>());

        var blast = ProjectileBlast.FromDefinition(definition, 0x123400, 0x100, -0x200);

        Assert.Equal((short)32, blast.RadiusWord);
        Assert.Equal((short)384, blast.ScaleRadius);
        Assert.True(blast.ScaleByDistance);
        Assert.Equal(unchecked((short)0x1234), blast.HomeX);
        Assert.Equal((short)1, blast.HomeY);
        Assert.Equal(unchecked((short)-2), blast.HomeZ);
    }
}
