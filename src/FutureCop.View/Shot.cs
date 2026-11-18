using FutureCop.Assets;

namespace FutureCop.View;

/// <summary>
/// One live projectile of the viewer: the retail definition half it was fired from, its model and
/// its flight state. The position is integrated with the retail integrator (func_8001ACA4) and the
/// per-tick speed follows the mover's acceleration block (func_80035B7C:435-448), so the step
/// arithmetic is the same integer math the mover uses.
/// </summary>
public sealed class Shot
{
    // Retail state of the mover: unk84 is the current 16.16 speed scale, unkD4<<4 its limit and
    // unkD2 the acceleration word (both copied from the spawn block by func_80035904).
    private int _speedScale;
    private readonly int _speedLimit;
    private readonly int _accelWord;
    private readonly int[] _home;

    /// <summary>
    /// Aim direction of a shot in model space, in 16.16. 0 degrees points at the viewer (model -Z,
    /// the way the mech faces), 90 degrees points to the mech's right (model +X). Retail aims at a
    /// target entity (posB - posA in func_8006ACB0, one vector for both muzzles); the viewer has no
    /// target, so it fires along a fixed angle that keeps the projectile visible while the mech turns.
    /// </summary>
    public static int[] Aim(double degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        int x = (int)Math.Round(Math.Sin(radians) * 65536.0);
        int z = (int)Math.Round(-Math.Cos(radians) * 65536.0);
        return new[] { x, 0, z };
    }

    public Shot(ShotDefinition definition, Model model, int x, int y, int z,
        double speedMultiplier = 1.0, double aimDegrees = 90.0)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(model);
        Definition = definition;
        Model = model;
        Position = new[] { x, y, z };
        Direction = Aim(aimDegrees);
        SpeedMultiplier = speedMultiplier > 0 ? speedMultiplier : 1.0;
        _speedScale = definition.StepWord;
        _speedLimit = definition.SpeedWord << 4;   // position units, unkD4 << 4
        _accelWord = definition.AltSpeed;
        Range = Math.Max(1, ShotMotion.Range(definition));
        Muzzle = (x, y, z);
        _home = new[] { x, y, z };
    }

    public ShotDefinition Definition { get; }
    public Model Model { get; }

    /// <summary>Viewer cadence multiplier of the retail step; 1 is the plain reading of the mover.</summary>
    public double SpeedMultiplier { get; }

    public int[] Position { get; }
    public int[] Direction { get; }

    /// <summary>Step of one 60 Hz frame in model units (delta * unk84, scaled to the viewer).</summary>
    public int StepUnits => Math.Max(1, (int)Math.Round(
        _speedScale * RetailClock.TicksPerFrame / (double)ShotMotion.PositionUnitsPerModelUnit *
        SpeedMultiplier));

    /// <summary>Current retail speed scale (unk84), exposed so tests can follow the acceleration.</summary>
    public int SpeedScale => _speedScale;

    /// <summary>Leash distance in model units (arc &lt;&lt; 4 position units, see ShotMotion.Range).</summary>
    public int Range { get; }

    /// <summary>Muzzle the shot left from, for tests and logs.</summary>
    public (int X, int Y, int Z) Muzzle { get; }

    /// <summary>Ticks the shot has flown.</summary>
    public int Age { get; private set; }

    /// <summary>Distance covered so far, in model units, accumulated over the accelerated steps.</summary>
    public int Travelled { get; private set; }

    public int X => Position[0];
    public int Y => Position[1];
    public int Z => Position[2];

    /// <summary>
    /// True once the shot left its leash. func_80035B7C:434 tests the octagonal distance from the
    /// home point *before* the step, so a shot overshoots its radius by up to one step - the same
    /// overshoot retail allows.
    /// </summary>
    public bool Expired => ShotMotion.OctagonalDistance(Position, _home) >= Range;

    /// <summary>Advances the shot by one retail tick through func_8001ACA4.</summary>
    public void Tick(int delta = RetailClock.TicksPerFrame)
    {
        // func_80035B7C:435-448 accelerates unk84 toward unkD4 << 4 before the integrator runs:
        // unk84 += (accel * delta) / 16, clamped at the limit, and the step itself is delta * unk84
        // (func_80035B7C:450). With the alternate-speed selector cleared the accumulator never moves,
        // so the step stays constant.
        _speedScale = ShotMotion.AdvanceSpeed(_speedScale, _speedLimit, _accelWord, delta);
        int step = Math.Max(1, (int)Math.Round(
            _speedScale * delta / (double)ShotMotion.PositionUnitsPerModelUnit * SpeedMultiplier));
        ShotMotion.Integrate(Position, Direction, step);
        Travelled += step;
        Age++;
    }
}
