namespace FutureCop.Game;

/// <summary>Unit class produced by <see cref="BuildOrderSpawner"/> (port extension beyond retail).</summary>
public enum BuiltUnitKind
{
    Tank,
    Aircraft,
}

/// <summary>
/// Mission-data template for a spawned unit: entity type and Cobj model ID taken directly from the
/// mission's <see cref="EntityRecord"/> list (no invented model IDs).
/// </summary>
public sealed record BuiltUnitTemplate(int EntityType, int ModelId);

/// <summary>
/// One visible, moving unit spawned from Precinct Assault build-order counters.
/// Port extension beyond retail: retail func_800336DC writes D_8009FA94+0xA0..0xDF with no retail
/// spawner reading those words. This unit has simple forward locomotion only (no AI, collision,
/// or combat/damage model).
/// </summary>
public sealed class BuiltUnit
{
    /// <summary>World units per clock tick for ground tanks (16.16 fixed-point position units).</summary>
    public const int TankSpeed = 0x400;

    /// <summary>World units per clock tick for aircraft.</summary>
    public const int AircraftSpeed = 0x700;

    /// <summary>Hover height above terrain for spawned aircraft (world position units).</summary>
    public const int AircraftAltitude = 0x18000;

    public int Id { get; }
    public BuiltUnitKind Kind { get; }
    public int EntityType { get; }
    public int ModelId { get; }
    public int Team { get; }
    /// <summary>Build slot 0..3 for G-&gt;c[i][k] production, or -1 for base production (fB0/fB4/fB8/fBC).</summary>
    public int SlotIndex { get; }

    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    /// <summary>Normalized horizontal heading in 16.16 fixed-point (0x10000 = 1.0).</summary>
    public int DirX { get; }
    public int DirZ { get; }

    public int Speed { get; }
    public int AltitudeOffset { get; }

    public BuiltUnit(int id, BuiltUnitKind kind, int entityType, int modelId, int team, int slotIndex,
        int x, int y, int z, int dirX, int dirZ)
    {
        Id = id;
        Kind = kind;
        EntityType = entityType;
        ModelId = modelId;
        Team = team;
        SlotIndex = slotIndex;
        X = x;
        Y = y;
        Z = z;
        DirX = dirX;
        DirZ = dirZ;
        Speed = kind == BuiltUnitKind.Aircraft ? AircraftSpeed : TankSpeed;
        AltitudeOffset = kind == BuiltUnitKind.Aircraft ? AircraftAltitude : 0;
    }

    /// <summary>Advances the unit along its heading for <paramref name="dt"/> clock ticks and tracks terrain height.</summary>
    public void Advance(int dt, Func<int, int, int> groundHeight)
    {
        if (dt <= 0) return;
        long step = (long)Speed * dt;
        X = unchecked(X + (int)((DirX * step) >> 16));
        Z = unchecked(Z + (int)((DirZ * step) >> 16));
        Y = groundHeight(X, Z) + AltitudeOffset;
    }
}

/// <summary>
/// Conscious port extension beyond retail: translates <see cref="DroneStrategy.Glob"/> build-order
/// production counters (<c>C[i][0]</c>/<c>FB0</c>/<c>FB8</c>/<c>FBC</c> -&gt; tanks,
/// <c>C[i][1]</c>/<c>FB4</c> -&gt; aircraft) into visible, moving <see cref="BuiltUnit"/> instances
/// using real unit models from the mission's <see cref="EntityRecord"/> table.
/// </summary>
/// <remarks>
/// Retired stopgap kept behind the opt-in <c>EnableBuildOrderSpawner</c> switch:
/// retail <c>func_800336DC</c> writes <c>D_8009FA94+0xA0..0xDF</c> (<c>GameSession[40..55]</c>),
/// and retail mission <c>Cfun</c> RPNS timers (e.g. <c>CONFT.MIS</c> T0/T1/T5/T6/T32..T39) drain
/// those words into <c>gScriptVmVars</c> and dispatch op 60 (<c>SPWID</c>) to
/// <see cref="CsacUnitSpawner"/>. <see cref="CsacUnitSpawner"/> + <see cref="PurchasePadSystem"/>
/// is now the live retail spawner runtime. <see cref="BuildOrderSpawner"/> is retained strictly
/// behind <c>EnableBuildOrderSpawner</c> (off by default) because existing unit/scene tests exercise
/// it directly and <see cref="CsacUnitSpawner"/> stops at the per-type handler boundary without
/// full unit locomotion AI. Enabling both flags simultaneously will double-consume or double-spawn
/// Sky Captain build orders.
/// </remarks>
public sealed class BuildOrderSpawner
{
    /// <summary>Retail mission entity types for ground tank (0x05), flyer (0x09), and heavy ground unit (0x1C).</summary>
    public const int TankEntityType = 0x05;
    public const int AircraftEntityType = 0x09;
    public const int HeavyTankEntityType = 0x1C;

    private const int LateralSpacing = 0x6000;

    private readonly record struct PendingSpawn(BuiltUnitKind Kind, BuiltUnitTemplate Template, int Team, int SlotIndex);

    private readonly List<BuiltUnit> _units = new();
    private readonly Queue<PendingSpawn> _pending = new();
    private readonly int[,] _seenC = new int[4, 2];
    private int _seenFB0, _seenFB4, _seenFB8, _seenFBC;
    private int _nextId = 10000;
    private (int X, int Y, int Z) _spawnOrigin;
    private (int X, int Y, int Z) _targetPos;
    private bool _hasTarget;
    private int _dirX = 0x10000;
    private int _dirZ;

    public IReadOnlyList<BuiltUnit> Units => _units;
    /// <summary>Queued production waiting for active unit slots below <see cref="MaxUnits"/>.</summary>
    public int PendingCount => _pending.Count;
    public BuiltUnitTemplate? TankTemplate { get; private set; }
    public BuiltUnitTemplate? HeavyTankTemplate { get; private set; }
    public BuiltUnitTemplate? AircraftTemplate { get; private set; }

    /// <summary>Hard cap on active spawned units to bound scene work during long sessions.</summary>
    public int MaxUnits { get; set; } = 32;

    /// <summary>
    /// Resolves tank and aircraft model templates from the mission's entity records and computes the
    /// march heading from <paramref name="basePos"/> toward <paramref name="targetPos"/>.
    /// </summary>
    public void ConfigureFromRecords(IReadOnlyList<EntityRecord> records, int team,
        (int X, int Y, int Z) basePos, (int X, int Y, int Z) targetPos)
    {
        ArgumentNullException.ThrowIfNull(records);
        TankTemplate = PickTemplate(records, TankEntityType, team);
        HeavyTankTemplate = PickTemplate(records, HeavyTankEntityType, team) ?? TankTemplate;
        AircraftTemplate = PickTemplate(records, AircraftEntityType, team);
        _spawnOrigin = basePos;
        _targetPos = targetPos;
        _hasTarget = basePos.X != targetPos.X || basePos.Z != targetPos.Z;
        (_dirX, _dirZ) = ComputeHeading(basePos.X, basePos.Z, targetPos.X, targetPos.Z);
    }

    /// <summary>Clears active and pending units and snapshots the current economy counters as baseline.</summary>
    public void Reset(DroneStrategy.Glob economy)
    {
        ArgumentNullException.ThrowIfNull(economy);
        _units.Clear();
        _pending.Clear();
        for (int i = 0; i < 4; i++)
        {
            _seenC[i, 0] = economy.C[i][0];
            _seenC[i, 1] = economy.C[i][1];
        }
        _seenFB0 = economy.FB0;
        _seenFB4 = economy.FB4;
        _seenFB8 = economy.FB8;
        _seenFBC = economy.FBC;
    }

    /// <summary>
    /// Spawns units for any queued or newly observed positive counter increments in
    /// <paramref name="economy"/> since the last call, queuing excess beyond <see cref="MaxUnits"/>,
    /// and returns the number of units spawned.
    /// </summary>
    public int SyncFromEconomy(DroneStrategy.Glob economy, int team, Func<int, int, int> groundHeight)
    {
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(groundHeight);
        int spawned = DrainPending(groundHeight);

        for (int i = 0; i < 4; i++)
        {
            int d0 = economy.C[i][0] - _seenC[i, 0];
            _seenC[i, 0] = economy.C[i][0];
            if (d0 > 0) spawned += SpawnBatch(BuiltUnitKind.Tank, TankTemplate, team, i, d0, groundHeight);

            int d1 = economy.C[i][1] - _seenC[i, 1];
            _seenC[i, 1] = economy.C[i][1];
            if (d1 > 0) spawned += SpawnBatch(BuiltUnitKind.Aircraft, AircraftTemplate, team, i, d1, groundHeight);
        }

        int dB0 = economy.FB0 - _seenFB0;
        _seenFB0 = economy.FB0;
        if (dB0 > 0) spawned += SpawnBatch(BuiltUnitKind.Tank, TankTemplate, team, -1, dB0, groundHeight);

        int dB4 = economy.FB4 - _seenFB4;
        _seenFB4 = economy.FB4;
        if (dB4 > 0) spawned += SpawnBatch(BuiltUnitKind.Aircraft, AircraftTemplate, team, -1, dB4, groundHeight);

        int dB8 = economy.FB8 - _seenFB8;
        _seenFB8 = economy.FB8;
        if (dB8 > 0) spawned += SpawnBatch(BuiltUnitKind.Tank, HeavyTankTemplate, team, -1, dB8, groundHeight);

        int dBC = economy.FBC - _seenFBC;
        _seenFBC = economy.FBC;
        if (dBC > 0) spawned += SpawnBatch(BuiltUnitKind.Tank, HeavyTankTemplate, team, -1, dBC, groundHeight);

        return spawned;
    }

    /// <summary>
    /// Advances every spawned unit by <paramref name="dt"/> clock ticks and despawns units that
    /// have reached or passed the target base.
    /// </summary>
    public void AdvanceUnits(int dt, Func<int, int, int> groundHeight)
    {
        ArgumentNullException.ThrowIfNull(groundHeight);
        for (int i = _units.Count - 1; i >= 0; i--)
        {
            var unit = _units[i];
            unit.Advance(dt, groundHeight);
            if (_hasTarget && HasReachedTarget(unit))
                _units.RemoveAt(i);
        }
    }

    private bool HasReachedTarget(BuiltUnit unit)
    {
        long dx = (long)unit.X - _targetPos.X;
        long dz = (long)unit.Z - _targetPos.Z;
        return dx * _dirX + dz * _dirZ >= 0;
    }

    private int DrainPending(Func<int, int, int> groundHeight)
    {
        int added = 0;
        while (_pending.Count > 0 && _units.Count < MaxUnits)
        {
            var next = _pending.Dequeue();
            SpawnOne(next.Kind, next.Template, next.Team, next.SlotIndex, added, groundHeight);
            added++;
        }
        return added;
    }

    private int SpawnBatch(BuiltUnitKind kind, BuiltUnitTemplate? template, int team, int slotIndex,
        int count, Func<int, int, int> groundHeight)
    {
        if (template is null) return 0;
        int added = 0;
        for (int k = 0; k < count; k++)
        {
            if (_units.Count < MaxUnits)
            {
                SpawnOne(kind, template, team, slotIndex, k, groundHeight);
                added++;
            }
            else
            {
                _pending.Enqueue(new PendingSpawn(kind, template, team, slotIndex));
            }
        }
        return added;
    }

    private void SpawnOne(BuiltUnitKind kind, BuiltUnitTemplate template, int team, int slotIndex,
        int staggerIndex, Func<int, int, int> groundHeight)
    {
        // Perpendicular right vector in XZ for fanning out units across slots / batches.
        int rightX = _dirZ;
        int rightZ = -_dirX;
        int lane = (slotIndex >= 0 ? slotIndex : (_units.Count & 3)) - 1;
        long lateral = (long)lane * LateralSpacing;
        long stagger = (long)staggerIndex * LateralSpacing;
        int x = unchecked(_spawnOrigin.X + (int)((rightX * lateral) >> 16) + (int)((_dirX * stagger) >> 16));
        int z = unchecked(_spawnOrigin.Z + (int)((rightZ * lateral) >> 16) + (int)((_dirZ * stagger) >> 16));
        int altitude = kind == BuiltUnitKind.Aircraft ? BuiltUnit.AircraftAltitude : 0;
        int y = groundHeight(x, z) + altitude;
        _units.Add(new BuiltUnit(_nextId++, kind, template.EntityType, template.ModelId, team, slotIndex,
            x, y, z, _dirX, _dirZ));
    }

    private static BuiltUnitTemplate? PickTemplate(IReadOnlyList<EntityRecord> records, int entityType, int team)
    {
        EntityRecord? fallback = null;
        foreach (var r in records)
        {
            if (r.Type != entityType) continue;
            int modelId = r.Model();
            if (modelId == 0) continue;
            if (r.Team == team) return new BuiltUnitTemplate(entityType, modelId);
            fallback ??= r;
        }
        return fallback is not null ? new BuiltUnitTemplate(entityType, fallback.Model()) : null;
    }

    private static (int DirX, int DirZ) ComputeHeading(int fromX, int fromZ, int toX, int toZ)
    {
        double dx = (double)toX - fromX;
        double dz = (double)toZ - fromZ;
        double len = Math.Sqrt(dx * dx + dz * dz);
        if (len < 1.0) return (0x10000, 0);
        return ((int)Math.Round(dx / len * 0x10000), (int)Math.Round(dz / len * 0x10000));
    }
}
