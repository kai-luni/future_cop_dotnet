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
    private int _speedWord;            // unkD4; the acceleration limit is unkD4 << 4
    private readonly int _accelWord;
    private readonly int[] _home;
    private readonly int _positionDivisor;
    private int _countdown = ShotMotion.DecayCountdownStart;   // unkDA, func_80035904:237
    private bool _stopped;             // unk1C < 0: the movement block no longer runs
    private double _pendingTicks;

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

    /// <param name="positionDivisor">
    /// Divisor between retail position units and the units <paramref name="x"/>..<paramref name="z"/>
    /// are given in. The mover's step <c>delta * unk84</c> and its leash <c>unkA6 &lt;&lt; 8</c> are both in
    /// position units, the same frame as the entity position at +0x50 (func_80035B7C:434, :450, :522).
    /// The level keeps shots in world position units and passes 1; the wireframe viewer works in
    /// model units and keeps the default 16.
    /// </param>
    /// <param name="pursuitState">
    /// unkE0 at spawn. func_8005E47C:108-109 stores 1 only when a target object is passed, 0 otherwise;
    /// the port has no targets, so the fire path passes 0.
    /// </param>
    public Shot(ShotDefinition definition, Model model, int x, int y, int z,
        double speedMultiplier = 1.0, double aimDegrees = 90.0, int[]? direction = null,
        int positionDivisor = ShotMotion.PositionUnitsPerModelUnit, int pursuitState = 0)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(model);
        Definition = definition;
        Model = model;
        Position = new[] { x, y, z };
        // An explicit direction wins over the aim angle: the level viewer fires along the walker's own
        // facing axis, which has no equivalent in the angle parameterisation.
        Direction = direction is { Length: >= 3 } d ? new[] { d[0], d[1], d[2] } : Aim(aimDegrees);
        SpeedMultiplier = speedMultiplier > 0 ? speedMultiplier : 1.0;
        _positionDivisor = Math.Max(1, positionDivisor);
        PursuitState = pursuitState;
        _speedScale = definition.StepWord;
        _speedWord = definition.SpeedWord;         // unkD4 = args + 0x48 = obj->unk06
        _accelWord = definition.AltSpeed;
        Range = Math.Max(1, ShotMotion.LeashRadius(definition) / _positionDivisor);
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
        _speedScale * RetailClock.TicksPerFrame / (double)_positionDivisor * SpeedMultiplier));

    /// <summary>Current retail speed scale (unk84), exposed so tests can follow the acceleration.</summary>
    public int SpeedScale => _speedScale;

    /// <summary>Current limit word unkD4 (the acceleration stops at unkD4 &lt;&lt; 4).</summary>
    public int SpeedWord => _speedWord;

    /// <summary>Pursuit countdown unkDA in ticks (only consumed in states 1 and 3).</summary>
    public int Countdown => _countdown;

    /// <summary>unkE0: 0 straight flight, 1/3 pursuit (with the speed brake), 2 climb.</summary>
    public int PursuitState { get; private set; }

    /// <summary>True once the mover set unk1C = -1 (leash, speed brake).</summary>
    public bool Stopped => _stopped;

    /// <summary>Leash distance (arc &lt;&lt; 4 position units, see ShotMotion.LeashRadius) in this shot's units.</summary>
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
    /// True once the shot left its leash or the mover stopped it. func_80035B7C:434 disables the
    /// movement when <c>(unkA6 &lt;&lt; 8) &lt; s4</c> - strictly beyond the radius - and tests the
    /// octagonal distance from the home point *before* the step, so a shot overshoots its radius by
    /// up to one step, the same overshoot retail allows. A stopped shot gets message 4 in the same
    /// frame (func_8004CE04:59-60) and is retired.
    /// </summary>
    public bool Expired => _stopped || ShotMotion.OctagonalDistance(Position, _home) > Range;

    /// <summary>Advances the shot by <paramref name="delta"/> retail ticks through func_8001ACA4.</summary>
    public void Tick(int delta = RetailClock.TicksPerFrame)
    {
        // func_80035B7C:436: the movement block only runs while unk1C >= 0.
        if (_stopped) return;
        // func_80035B7C:437-449 accelerates unk84 toward unkD4 << 4 before the integrator runs:
        // unk84 += (accel * delta) / 16, clamped at the limit, and the step itself is delta * unk84
        // (func_80035B7C:450). With the alternate-speed selector cleared the accumulator never moves,
        // so the step stays constant.
        _speedScale = ShotMotion.AdvanceSpeed(_speedScale, _speedWord << 4, _accelWord, delta);
        double scaled = _speedScale * (double)delta / _positionDivisor * SpeedMultiplier;
        // With divisor 1 and multiplier 1 this is exactly s3 = delta * unk84; the floor of one unit
        // only matters for the scaled-down viewer path.
        int step = Math.Max(1, (int)Math.Round(scaled));
        // func_80035B7C:459-468: states 1 and 3 run the speed brake after s3 is taken, so a halving
        // shows from the next tick on; unk1C = -1 set here still lets this tick's step happen (:522).
        if (PursuitState is 1 or 3)
        {
            if (!ShotMotion.DecaySpeed(ref _speedScale, ref _speedWord, ref _countdown, delta))
                _stopped = true;
            // func_80035B7C:470-472, :512, :496-497: without a live target (unkC4) the pursuit
            // falls back to straight flight. The port tracks no targets, so it always does.
            PursuitState = 0;
        }
        ShotMotion.Integrate(Position, Direction, step);
        Travelled += step;
        Age++;
    }

    /// <summary>
    /// Advances the shot by a possibly fractional amount of retail time, one whole tick at a time.
    /// This is how the level slows a projectile down: it stretches the retail clock instead of
    /// shrinking the step, so acceleration, leash and speed brake keep their retail relation to the
    /// distance flown - only the wall-clock time is longer.
    /// </summary>
    /// <param name="ticks">Retail ticks to add (e.g. TicksPerFrame / slowMotion per 60 Hz frame).</param>
    public void AdvanceRetailTime(double ticks)
    {
        if (ticks <= 0) return;
        _pendingTicks += ticks;
        while (_pendingTicks >= 1.0 && !Expired)
        {
            Tick(1);
            _pendingTicks -= 1.0;
        }
    }
}
