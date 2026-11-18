namespace FutureCop.Assets;

/// <summary>
/// Motion arithmetic of the projectile path: the integrator func_8001ACA4 the entity mover
/// func_80035B7C calls every tick, plus the two scale rules the spawner and the spawn half apply.
/// </summary>
/// <remarks>
/// Retail moves a spawned object with <c>func_8001ACA4(position, direction, s3, position)</c> where
/// <c>s3 = delta * speed</c> (func_80035B7C movement block) and the speed starts at the spawner's
/// step word (func_80035904:196 copies args + 0x2C). The value is a 16.16 direction times a 16.16
/// scale, so one tick advances the position by <c>scale</c> position units. How many model units a
/// position unit is cannot be read off the decomp alone - see docs/weapon-fire-path.md - so the
/// viewer exposes <see cref="UnitScale"/> instead of pretending to a proven number.
/// </remarks>
public static class ShotMotion
{
    /// <summary>func_8001ACA4: out = a + (b * scale &gt;&gt; 16) per component, aliasing allowed.</summary>
    public static void Integrate(int[] position, int[] direction, int scale)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(direction);
        if (position.Length < 3) throw new ArgumentException("position needs three words", nameof(position));
        if (direction.Length < 3) throw new ArgumentException("direction needs three words", nameof(direction));
        for (int i = 0; i < 3; i++)
            position[i] = unchecked(position[i] + (int)(((long)direction[i] * scale) >> 16));
    }

    /// <summary>func_8005E47C speed scale: speed &lt;&lt; 4, or speed &lt;&lt; 1 with the alternate
    /// selector (mirrored by <see cref="ShotDefinition.StepWord"/>).</summary>
    public static int TickScale(ShotDefinition shot) => shot.StepWord;

    /// <summary>Range of the shot in model units: func_80035904:232, (arc &lt;&lt; 4) &gt;&gt; 8.</summary>
    public static int Range(ShotDefinition shot) => shot.Range;

    /// <summary>
    /// The mover's acceleration block (func_80035B7C:435-448): the current 16.16 speed scale grows
    /// by <c>(accel * delta) / 16</c> - with <c>delta = 1</c> tick that is <c>accel / 16</c> - until it
    /// reaches the limit <c>unkD4 &lt;&lt; 4</c>, and never moves when the selector is cleared.
    /// </summary>
    public static int AdvanceSpeed(int scale, int limit, int accel)
    {
        if (accel == 0 || scale >= limit) return scale;
        int next = scale + accel / 16;
        return next > limit ? limit : next;
    }
}
