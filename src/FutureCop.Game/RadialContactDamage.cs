using FutureCop.Game.Mech;

namespace FutureCop.Game;

/// <summary>
/// The blast parameter block func_800410BC reads (and the two wrapper words it never writes):
/// D_8009F324 (inner dead zone and message flag), D_8009F328 (cone enable), D_8009F329 (band bits)
/// and the cone geometry D_8009F710/D_8009F718/D_8009F71C. Only the two retail wrappers
/// func_800413EC and func_80041428 fill these words, and both clear the context and enable words
/// again after the call - so the unwrapped call sites (func_80036624, gBeamPartUpdate) always run
/// with <see cref="None"/>.
/// </summary>
/// <param name="Context">D_8009F324: objects closer than this are skipped; also the record flag
/// <c>+0x0C</c> (1 when positive) that the message-9 receiver does not read.</param>
/// <param name="ConeEnabled">D_8009F328 (u8): enables the directional cone test.</param>
/// <param name="BandBits">D_8009F329 (u8): bit 0 enables the vertical band test, bit 1 drops its
/// lower half (only the ceiling is tested).</param>
/// <param name="ShadowThreshold">D_8009F710 (s16): the cone projection must reach this.</param>
/// <param name="ConeAxisX">D_8009F718 (s16): the cone's x axis word, a direction <c>&gt;&gt; 4</c>.</param>
/// <param name="ConeDirectionZ">D_8009F71C (s16): the cone's z axis word, a direction <c>&gt;&gt; 4</c>.</param>
public readonly record struct RadialBlastParameters(
    int Context, bool ConeEnabled, byte BandBits, short ShadowThreshold, short ConeAxisX, short ConeDirectionZ)
{
    /// <summary>The block as the unwrapped call sites see it: every word zero.</summary>
    public static RadialBlastParameters None => default;
}

/// <summary>
/// The two words func_800410BC writes: D_8009F700 (the factor that turns the remaining distance into
/// a strength) and D_8009F704 (the radius the walker used).
/// </summary>
public readonly record struct RadialBlastField(int StrengthScale, int Radius);

/// <summary>
/// func_800410BC gApplyRadialContactDamage: walks the active entity list D_800A8F98 and sends message
/// 9 with a strength that falls off linearly with the planar distance. The band and cone filters and
/// the inner dead zone are only active when <see cref="RadialBlastParameters"/> carries them, i.e.
/// under the wrappers <see cref="ApplyBandedRadialDamage"/> (func_800413EC) and
/// <see cref="ApplyConeContactDamage"/> (func_80041428).
/// </summary>
public static class RadialContactDamage
{
    /// <summary>
    /// obj+0x24 bit 0x200000: the linked-projectile flag the radial pass skips together with
    /// <see cref="LiveEntity.IgnoreHitsFlag"/> (gApplyRadialContactDamage.c:161,
    /// gInitLinkedProjectile.c:99, gBeamPartUpdate.c:359).
    /// </summary>
    private const uint LinkedProjectileFlag = 0x200000;

    /// <summary>
    /// func_800410BC over the last culled active list (<see cref="EntityWorld.Active"/>), the way the
    /// projectile and beam call sites reach it.
    /// </summary>
    /// <param name="world">The world whose active list is walked and whose handler receives message 9.</param>
    /// <param name="pos">Blast centre, three s32 world coordinates (the C's <c>struct Vec3s32</c>).</param>
    /// <param name="strength">arg1: only the low 16 bits survive the <c>&lt;&lt; 16</c> that starts the
    /// function, so this is a 16-bit quantity.</param>
    /// <param name="radius">arg2: objects at or beyond it are skipped and it scales the falloff.</param>
    /// <param name="sourceWord">arg3-&gt;unk14, the first word of the message record; 0 when arg3 is null.</param>
    /// <param name="source">arg3, the record itself: the message's <c>+0x08</c> source entity.</param>
    /// <param name="parameters">The wrapper words; <see cref="RadialBlastParameters.None"/> for a plain blast.</param>
    public static RadialBlastField Apply(EntityWorld world, int[] pos, int strength, int radius,
        uint sourceWord, LiveEntity? source, RadialBlastParameters parameters = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Apply(world.Active, world, pos, strength, radius, sourceWord, source, parameters);
    }

    /// <summary>
    /// func_800410BC over an explicit list (the C reads D_800A8F98, whose table <c>[-2]</c> holds the
    /// count; here the list carries its own). Exposed so tests and format checks can walk a list that
    /// does not come from <see cref="EntityWorld.CullActive"/>, including retired entries with a
    /// cleared <c>+0x08</c>.
    /// </summary>
    public static RadialBlastField Apply(IReadOnlyList<LiveEntity> entities, EntityWorld world, int[] pos,
        int strength, int radius, uint sourceWord, LiveEntity? source, RadialBlastParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(pos);

        // :146-171: the sign of (arg1 << 16) and of arg2 select the sign of the factor and the
        // divisor 0x80000000 / (|arg2| >> 1). A negative radius is negated before its shift, so -5
        // gives 2, not -3.
        int a0 = unchecked(strength << 16);
        int half = radius > 0 ? radius >> 1 : unchecked(-radius) >> 1;
        if (half == 0)
            throw new ArgumentOutOfRangeException(nameof(radius), radius,
                "func_800410BC divides 0x80000000 by |radius| >> 1; retail traps when that is zero");
        int r = MechMath.FmulU(a0 < 0 ? unchecked(-a0) : a0, unchecked((int)(0x80000000u / (uint)half)));
        bool negative = (a0 > 0) != (radius > 0);
        var field = new RadialBlastField(negative ? unchecked(-r) : r, radius);

        // :158-161: obj+0x08 <= 0 and (obj+0x24 & 0x200080) != 0 never receive the message. The
        // 0x200000 half is the linked-projectile flag (gInitLinkedProjectile.c:99,
        // gBeamPartUpdate.c:359), the 0x80 half is LiveEntity.IgnoreHitsFlag.
        foreach (var entity in entities)
        {
            if (entity.RuntimeId <= 0) continue;                             // :158-160 obj+0x08
            if ((entity.Flags & (LiveEntity.IgnoreHitsFlag | LinkedProjectileFlag)) != 0) continue; // :161

            var (ex, ez) = entity.GroundPosition;                            // obj+0x50 / +0x58
            int d = EnemyMath.ApproxDistanceXz(pos, new[] { ex, 0, ez });    // func_8001A750
            if (d >= radius) continue;                                       // :168 d < gFalloffRadius
            if (d < parameters.Context) continue;                            // :169 d >= D_8009F324

            if ((parameters.BandBits & 1) != 0)                              // :171-182
            {
                // obj+0x60 is the collision coord's y and obj+0x68 its radius; an entity without a
                // coord record reads as the zeroed record retail always has there.
                int yc = entity.Coord?.Y ?? 0;
                int yr = entity.Coord?.Radius ?? 0;
                int y = pos[1];
                if (unchecked(yc + yr + 0x1000) < y) continue;
                if ((parameters.BandBits & 2) == 0 && y < unchecked(yc - yr - 0x1000)) continue;
            }

            if (parameters.ConeEnabled)                                      // :184-192
            {
                int denom = d >> 8;
                if (denom == 0)
                    throw new ArgumentOutOfRangeException(nameof(parameters), d,
                        "func_80041428's cone divides the axis projection by distance >> 8; retail traps on 0");
                int tx = unchecked(((ex - pos[0]) << 4) / denom);
                int tz = unchecked(((ez - pos[2]) << 4) / denom);
                int sum = unchecked(((parameters.ConeAxisX * tx) >> 12) + ((parameters.ConeDirectionZ * tz) >> 12));
                if (sum < parameters.ShadowThreshold) continue;
            }

            int s0 = unchecked(radius - d);                                  // :194-200
            s0 = MechMath.Fmul(s0, field.StrengthScale);
            s0 >>= 16;
            if (s0 == 0) continue;                                           // :202-205

            // args.unkE is s16; the receiver reads it as u16 (8002A940.c), so the cast keeps the bits.
            // args.unk0 is arg3->unk14, a u16 that the s32 field zero-extends.
            entity.HandleMessage(9, world, new AreaHit(sourceWord & 0xFFFFu, source, unchecked((ushort)s0)));
        }
        return field;
    }

    /// <summary>
    /// func_800413EC gApplyBandedRadialDamage: sets D_8009F324 = <paramref name="context"/> and
    /// D_8009F329 = <paramref name="mode"/> | 1 (bit 0 arms the band test), runs func_800410BC and
    /// clears both words again.
    /// </summary>
    /// <param name="mode">The caller's band bits; bit 0 is forced on.</param>
    public static RadialBlastField ApplyBandedRadialDamage(EntityWorld world, int[] pos, int strength,
        int radius, uint sourceWord, LiveEntity? source, int context, byte mode)
    {
        ArgumentNullException.ThrowIfNull(world);
        return ApplyBandedRadialDamage(world.Active, world, pos, strength, radius, sourceWord, source, context, mode);
    }

    /// <summary>func_800413EC over an explicit list; see the overload above.</summary>
    public static RadialBlastField ApplyBandedRadialDamage(IReadOnlyList<LiveEntity> entities, EntityWorld world,
        int[] pos, int strength, int radius, uint sourceWord, LiveEntity? source, int context, byte mode)
        => Apply(entities, world, pos, strength, radius, sourceWord, source,
            new RadialBlastParameters(context, false, (byte)(mode | 1), 0, 0, 0));

    /// <summary>
    /// func_80041428 gApplyConeContactDamage: fills D_8009F710/718/71C from
    /// <paramref name="height"/> and <paramref name="position"/> (both <c>&gt;&gt; 4</c>, stored as
    /// s16), sets D_8009F328 = 1 and D_8009F324 = <paramref name="context"/>, runs func_800410BC and
    /// clears the enable and context words again.
    /// </summary>
    /// <param name="conePosition">The cone origin; only x and z are read.</param>
    /// <param name="height">Cone threshold before the <c>&gt;&gt; 4</c>.</param>
    public static RadialBlastField ApplyConeContactDamage(EntityWorld world, int[] pos, int strength,
        int radius, uint sourceWord, LiveEntity? source, int context, int[] conePosition, int height)
    {
        ArgumentNullException.ThrowIfNull(world);
        return ApplyConeContactDamage(world.Active, world, pos, strength, radius, sourceWord, source,
            context, conePosition, height);
    }

    /// <summary>func_80041428 over an explicit list; see the overload above.</summary>
    public static RadialBlastField ApplyConeContactDamage(IReadOnlyList<LiveEntity> entities, EntityWorld world,
        int[] pos, int strength, int radius, uint sourceWord, LiveEntity? source, int context,
        int[] conePosition, int height)
    {
        ArgumentNullException.ThrowIfNull(conePosition);
        return Apply(entities, world, pos, strength, radius, sourceWord, source,
            new RadialBlastParameters(context, true, 0, unchecked((short)(height >> 4)),
                unchecked((short)(conePosition[0] >> 4)), unchecked((short)(conePosition[2] >> 4))));
    }
}
