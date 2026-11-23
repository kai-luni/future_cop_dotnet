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
    /// <summary>
    /// Viewer mapping from a retail position unit to a model unit. The mover works in position
    /// units (the step is <c>delta * unk84</c>, the leash compares against <c>unkA6 &lt;&lt; 8</c>) while
    /// the models are built in model units, and the ratio between the two is not stated anywhere in
    /// the decomp. The viewer divides BOTH the step and the radius by this one constant: using a
    /// different divisor per side would either kill the shot within a fraction of a tick or let it
    /// crawl a few pixels. With 16 a 4Da shot covers about one body height in its eight retail
    /// ticks, which is the scale the mission data reads at.
    /// </summary>
    /// <summary>
    /// Position units per model unit. func_80010750 loads the entity translation shifted right by four
    /// and func_80010234 stores a transformed vertex shifted left by four, so a world position is
    /// <c>position + 16 * vertex</c>: the position unit is the finer of the two, and 16 of them make
    /// one model unit.
    /// </summary>
    public const int PositionUnitsPerModelUnit = 16;

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

    /// <summary>func_8005E47C speed scale in model units per tick: speed &lt;&lt; 4, or speed &lt;&lt; 1
    /// with the alternate selector (mirrored by <see cref="ShotDefinition.StepWord"/>).</summary>
    public static int TickScale(ShotDefinition shot) => shot.StepWord / PositionUnitsPerModelUnit;

    /// <summary>Leash radius in model units, i.e. the retail radius divided by sixteen.</summary>
    public static int Range(ShotDefinition shot) => LeashRadius(shot) / PositionUnitsPerModelUnit;

    /// <summary>
    /// func_80035904:234 with the shift the mover tests: unkA6 = (arc &lt;&lt; 4) &gt;&gt; 8 and the gate is
    /// <c>unkA6 &lt;&lt; 8</c>, i.e. the range word rounded down to a multiple of 256 position units.
    /// </summary>
    public static int LeashRadius(ShotDefinition shot)
    {
        ArgumentNullException.ThrowIfNull(shot);
        return shot.Range;
    }

    /// <summary>
    /// func_8001A4E8: octagonal distance between two points - the largest axis difference plus a
    /// quarter of the other two, which the leash test of func_80035B7C:434 compares the radius with.
    /// </summary>
    public static int OctagonalDistance(int[] a, int[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Length < 3 || b.Length < 3) throw new ArgumentException("points need three words");
        int x = Math.Abs(a[0] - b[0]);
        int y = Math.Abs(a[1] - b[1]);
        int z = Math.Abs(a[2] - b[2]);
        if (x < y) (x, y) = (y, x);
        if (x < z) (x, z) = (z, x);
        return x + ((y + z) >> 2);
    }

    /// <summary>
    /// Flight basis of a projectile, exactly as func_800193F8 builds it from the flight vector
    /// <paramref name="direction"/> (16.16 units): <c>row0 = normalize(dz, 0, -dx)</c>,
    /// <c>row1 = d x row0</c> (func_80010974 is the GTE cross product a x b), <c>row2 = d</c>. All
    /// three rows are unit vectors, so the matrix carries no scale. Model +Z is the nose, which puts
    /// the rocket along its velocity; a horizontal flight becomes a pure yaw, a vertical component
    /// adds pitch. The mover rebuilds the same basis every tick (func_80035B7C:554-568).
    /// </summary>
    public static int[] FlightBasis(int[] direction)
    {
        ArgumentNullException.ThrowIfNull(direction);
        if (direction.Length < 3) throw new ArgumentException("direction needs three words", nameof(direction));
        long dx = direction[0], dy = direction[1], dz = direction[2];
        double horizontal = Math.Sqrt((double)dx * dx + (double)dz * dz);
        long r0x = horizontal == 0 ? 0x10000 : (long)Math.Round(dz / horizontal * 65536.0);
        long r0z = horizontal == 0 ? 0 : (long)Math.Round(-dx / horizontal * 65536.0);
        long r1x = (dy * r0z) >> 16;
        long r1y = (dz * r0x - dx * r0z) >> 16;
        long r1z = (-(dy * r0x)) >> 16;
        return new[]
        {
            (int)r0x, 0, (int)r0z,
            (int)r1x, (int)r1y, (int)r1z,
            (int)dx, (int)dy, (int)dz,
        };
    }

    /// <summary>
    /// The mover's acceleration block (func_80035B7C:435-448): the current 16.16 speed scale grows
    /// by <c>(accel * delta) / 16</c> - with <c>delta = 1</c> tick that is <c>accel / 16</c> - until it
    /// reaches the limit <c>unkD4 &lt;&lt; 4</c>, and never moves when the selector is cleared.
    /// </summary>
    public static int AdvanceSpeed(int scale, int limit, int accel, int delta)
    {
        if (accel == 0 || scale >= limit) return scale;
        int next = scale + (accel * delta) / 16;
        return next > limit ? limit : next;
    }

    /// <summary>Start value of the pursuit countdown unkDA: func_80035904:237 (and the state-4 launch,
    /// func_80035B7C:320) store 0x5DC = 1500 ticks = 5 s.</summary>
    public const int DecayCountdownStart = 0x5DC;

    /// <summary>Reload of the countdown after each halving: func_80035B7C:465 adds 0x12C = 300 ticks = 1 s.</summary>
    public const int DecayCountdownReload = 0x12C;

    /// <summary>A speed scale below this kills the shot: func_80035B7C:466-467 (<c>unk84 &lt; 16</c> -&gt; unk1C = -1).</summary>
    public const int DecayDeathSpeed = 16;

    /// <summary>
    /// The pursuit brake of the mover, func_80035B7C:461-468 (states 1 and 3 only, case at :459):
    /// <code>
    /// unkDA = (u16)(unkDA - delta);
    /// if ((s16)unkDA &lt; 0) { unk84 /= 2; unkD4 = (s16)(unk84 &gt;&gt; 4); unkDA += 0x12C;
    ///                        if (unk84 &lt; 16) unk1C = -1; }
    /// </code>
    /// The first halving therefore comes after 1500 ticks, every further one after 300. Halving
    /// unkD4 too lowers the acceleration limit <c>unkD4 &lt;&lt; 4</c>, so the mover cannot speed back up.
    /// </summary>
    /// <param name="scale">unk84, the 16.16 speed scale.</param>
    /// <param name="speedWord">unkD4, the s16 limit word (limit = word &lt;&lt; 4).</param>
    /// <param name="countdown">unkDA, the u16 countdown.</param>
    /// <param name="delta">Retail ticks of this call.</param>
    /// <returns>False when the shot dies (unk1C = -1), true while it keeps flying.</returns>
    public static bool DecaySpeed(ref int scale, ref int speedWord, ref int countdown, int delta)
    {
        countdown = (ushort)(countdown - delta);
        if ((short)countdown >= 0) return true;
        scale /= 2;                                   // C signed division, truncates toward zero
        speedWord = (short)(scale >> 4);
        countdown = (ushort)(countdown + DecayCountdownReload);
        return scale >= DecayDeathSpeed;
    }
}
