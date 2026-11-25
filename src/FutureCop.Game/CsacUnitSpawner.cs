using FutureCop.Game.Mech;

namespace FutureCop.Game;

/// <summary>
/// Timing and cap fields carried by a Csac request's <c>tSAC</c> (0x74534143) chunk:
/// +0x0C raw respawn delay, +0x0E max concurrent/total cap, +0x10 initial flags, +0x12 remaining
/// spawn count (80060E74.c:124-136, 800616A0.c:4-12).
/// </summary>
/// <param name="RespawnDelayRaw">tSAC +0x0C (s16), multiplied by 5 during func_80060E74 registration.</param>
/// <param name="MaxConcurrent">tSAC +0x0E (s16), compared against TotalSpawned (unk16 &lt; unk0E).</param>
/// <param name="InitialFlags">tSAC +0x10 (u16); bit 0 pauses timer, bits 8..11 and 12..15 queue bursts.</param>
/// <param name="InitialCount">tSAC +0x12 (s16); 0 disables timer spawns, positive decrements, negative is unlimited.</param>
public readonly record struct CsacSpawnerParameters(
    short RespawnDelayRaw,
    short MaxConcurrent,
    ushort InitialFlags,
    short InitialCount)
{
    /// <summary>Conservative default when a caller supplies an EntityRecord without a parsed tSAC chunk.</summary>
    public static CsacSpawnerParameters Default => new(RespawnDelayRaw: -60, MaxConcurrent: 4, InitialFlags: 0, InitialCount: -1);
}

/// <summary>
/// One registered Csac unit spawner entry in the 0x80-slot spawner table (D_8009F800, populated by
/// func_80060E74 and stepped by func_800616A0 / func_8006125C).
/// </summary>
public sealed class CsacSpawnerSlot
{
    public CsacSpawnerSlot(
        EntityRecord record,
        EntityHandlerKind handler,
        int respawnDelay,
        int maxConcurrent,
        ushort flags,
        int remainingCount,
        int initialTimer)
    {
        Record = record;
        Handler = handler;
        ModelId = record.Model();
        RespawnDelay = respawnDelay;
        MaxConcurrent = maxConcurrent;
        Flags = flags;
        RemainingCount = remainingCount;
        Timer = initialTimer;
    }

    /// <summary>The parsed Csac EntityRecord (type, position, team, model, RPNS script offsets).</summary>
    public EntityRecord Record { get; }

    /// <summary>Handler resolved via func_8002AE54 (80060E74.c:157).</summary>
    public EntityHandlerKind Handler { get; }

    /// <summary>Primary Cobj model id from the record's aRSL list (0 if none).</summary>
    public int ModelId { get; }

    /// <summary>Scaled respawn interval (tSAC +0x0C * 5, abs; 80060E74.c:129-131).</summary>
    public int RespawnDelay { get; }

    /// <summary>Maximum spawned/active cap (tSAC +0x0E; 800616A0.c:70).</summary>
    public int MaxConcurrent { get; }

    /// <summary>Spawner flags word (tSAC +0x10): bit 0 = paused, bits 8..11 = mode-1 burst, bits 12..15 = mode-0 burst.</summary>
    public ushort Flags { get; set; }

    /// <summary>Remaining spawns (tSAC +0x12): 0 = exhausted, positive = decremented per spawn, negative = unlimited.</summary>
    public int RemainingCount { get; set; }

    /// <summary>Countdown timer (tSAC +0x14); decremented by elapsed each drain, fires when signed 16-bit &lt; 0.</summary>
    public int Timer { get; set; }

    /// <summary>Total spawns emitted (tSAC +0x16; zeroed in 80060E74.c:128, incremented in 8006125C.c:49).</summary>
    public int TotalSpawned { get; set; }

    /// <summary>RPNS script offsets from the Csac request header (words 9..11).</summary>
    public IReadOnlyList<int> Scripts => Record.Scripts;
}

/// <summary>
/// One live unit instance emitted by <see cref="CsacUnitSpawner"/> and registered into
/// <see cref="MapObjectRegistry.LiveObjects"/> for message-6 broadcasts and, when attached,
/// <see cref="EntityWorld"/> for damage and retirement.
/// </summary>
public sealed class SpawnedCsacUnit : LiveEntity, IRegisteredMapObject
{
    public SpawnedCsacUnit(
        int id,
        CsacSpawnerSlot sourceSlot,
        int spawnMode,
        Func<SpawnedCsacUnit, int, int, int, int>? messageHandler = null)
    {
        Id = id;
        SourceSlot = sourceSlot;
        EntityType = sourceSlot.Record.Type;
        Handler = sourceSlot.Handler;
        ModelId = sourceSlot.ModelId;
        Team = sourceSlot.Record.Team;
        X = sourceSlot.Record.X;
        Y = sourceSlot.Record.Y;
        Z = sourceSlot.Record.Z;
        SpawnMode = spawnMode;
        MessageHandler = messageHandler;

        var record = sourceSlot.Record;
        // gInitEntityBase copies the script offsets (8002A5F4.c:108-109), health and flags
        // (8002A5F4.c:130-138) from the spawn definition.
        DamageScript = record.Scripts.ElementAtOrDefault(0);
        RetireScript = record.Scripts.ElementAtOrDefault(1);
        Health = MaxHealth = unchecked((short)record.Health);
        Flags = record.Flags & 0xFFBFFFFFu;
        if ((Flags & IgnoreHitsFlag) != 0) Flags |= RetiredFlag;
    }

    /// <inheritdoc/>
    public override int RecordId => SourceSlot.Record.Id;

    /// <inheritdoc/>
    public override int DispatchType => EntityType;

    /// <inheritdoc/>
    public override (int X, int Z) GroundPosition => (X, Z);

    /// <inheritdoc/>
    public override int HandleMessage(int message, EntityWorld world, object? data) =>
        BaseHandler(message, this, world, data);

    public int Id { get; }
    public CsacSpawnerSlot SourceSlot { get; }
    public int EntityType { get; }
    public EntityHandlerKind Handler { get; }
    public int ModelId { get; }
    public int Team { get; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    /// <summary>Specialized mission runtime created on spawn (null for other dispatch types).</summary>
    public IRegisteredMapObject? RuntimeObject { get; internal set; }
    /// <summary>Spawn mode argument passed by func_800616A0 to func_8006125C (1 = normal/timer, 0 = high-nibble burst).</summary>
    public int SpawnMode { get; }
    /// <summary>Number of message-6 ticks received from <see cref="LiveObjectRegistry.BroadcastMessage6"/>.</summary>
    public int Message6Count { get; private set; }
    /// <summary>Last delta passed via message 6.</summary>
    public int LastDelta { get; private set; }
    /// <summary>Optional custom message handler callback for per-type runtime behaviour.</summary>
    public Func<SpawnedCsacUnit, int, int, int, int>? MessageHandler { get; set; }

    public int OnMessage(int message, int arg2, int arg3)
    {
        if (message == LiveObjectRegistry.MessageFrameTick)
        {
            Message6Count++;
            LastDelta = arg2;
        }
        int runtimeResult = RuntimeObject?.OnMessage(message, arg2, arg3) ?? 0;
        SyncRuntimePosition();
        return MessageHandler?.Invoke(this, message, arg2, arg3) ?? runtimeResult;
    }

    internal void SyncRuntimePosition()
    {
        if (RuntimeObject is Elevator lift)
        {
            (X, Y, Z) = (lift.X, lift.Y, lift.Z);
        }
        else if (RuntimeObject is MountedTurretCarrier carrier)
        {
            (X, Y, Z) = (carrier.Position.X, carrier.Position.Y, carrier.Position.Z);
        }
        if (Coord is { } coord)
            Coord = coord with { X = X, Y = Y, Z = Z };
    }
}

/// <summary>
/// Retail-keyed unit spawner built from parsed <c>Csac</c> <see cref="EntityRecord"/> entries
/// (ports func_80060E74 registration, func_8006158C / func_800614BC command dispatch,
/// func_800616A0 per-frame drain, and func_8006125C gated spawn).
/// </summary>
/// <remarks>
/// <para>
/// Gate predicate note (8006125C.c:47): in retail, <c>gTrySpawnRegisteredUnit</c> calls
/// <c>gInterpretEntityScript(s-&gt;arg1, 0, -1, 0)</c> as its precondition. For every Precinct
/// Assault record the retail gate script is empty (<c>0x00</c>), so the default
/// <see cref="GatePredicate"/> accepts unconditionally. The Precinct Assault unit purchase/price
/// gate lives in the RPNS script pool instead (VM opcodes 25 and 52 decrementing
/// <c>gGameSession[25/26]</c> from RPNS buy-zone scripts, as proven in
/// <c>docs/precinct-assault-map-objects.md</c> in the C repository). Once the RPNS VM is ported,
/// callers can plug a script-backed predicate into <see cref="GatePredicate"/>.
/// </para>
/// </remarks>
public sealed class CsacUnitSpawner
{
    /// <summary>Fixed capacity matching D_8009F800 (80060E28.c:6).</summary>
    public const int Capacity = LiveObjectRegistry.Capacity;

    /// <summary>Retail spawner command 0x46: pause/resume timer via flags bit 0 (800614BC.c:47-56).</summary>
    public const int CommandSetEnabled = 0x46;

    /// <summary>Retail spawner command 0x47: queue a burst spawn in flags bits 8..11 or 12..15 (800614BC.c:57-86).</summary>
    public const int CommandQueueBurst = 0x47;

    private readonly CsacSpawnerSlot?[] _slots = new CsacSpawnerSlot?[Capacity];
    private readonly List<SpawnedCsacUnit> _spawnedUnits = new();
    private readonly MapObjectRegistry? _registry;
    private readonly EntityWorld? _world;
    private int _count;
    private int _nextRuntimeId = 20000;

    /// <summary>Creates a spawner with optional map-object and entity-world registration.</summary>
    public CsacUnitSpawner(MapObjectRegistry? registry = null, RetailRandom? random = null, EntityWorld? world = null)
    {
        _registry = registry;
        _world = world;
        Random = random ?? new RetailRandom();
    }

    /// <summary>PRNG used by func_80060E74:135 to seed positive initial respawn delays.</summary>
    public RetailRandom Random { get; set; }

    /// <summary>Number of registered Csac spawner slots (D_8009F800.count).</summary>
    public int Count => _count;

    /// <summary>Registered spawner slots in registration order [0..Count-1].</summary>
    public IReadOnlyList<CsacSpawnerSlot> Slots
    {
        get
        {
            if (_count == 0) return Array.Empty<CsacSpawnerSlot>();
            var list = new CsacSpawnerSlot[_count];
            for (int i = 0; i < _count; i++) list[i] = _slots[i]!;
            return list;
        }
    }

    /// <summary>Units spawned by this spawner since creation or the last <see cref="Reset"/>.</summary>
    public IReadOnlyList<SpawnedCsacUnit> SpawnedUnits => _spawnedUnits;

    /// <summary>
    /// Pluggable gate predicate modeling <c>gInterpretEntityScript(s-&gt;arg1, 0, -1, 0)</c> at
    /// 8006125C.c:47. Defaults to accepting every spawn because the retail gate script is 0x00
    /// for every PA record and the price gate resides in the RPNS buy-zone scripts instead.
    /// </summary>
    public Func<CsacSpawnerSlot, int, bool> GatePredicate { get; set; } = static (_, _) => true;

    /// <summary>
    /// Optional post-spawn script hook modeling <c>gInterpretEntityScript(s-&gt;arg2, 0, -1, 0)</c>
    /// at 8006125C.c:76.
    /// </summary>
    public Action<CsacSpawnerSlot, SpawnedCsacUnit>? OnPostSpawnScript { get; set; }

    /// <summary>
    /// Optional message handler factory attached to newly spawned <see cref="SpawnedCsacUnit"/>
    /// instances when they are created.
    /// </summary>
    public Func<SpawnedCsacUnit, int, int, int, int>? UnitMessageHandler { get; set; }

    /// <summary>Clears all registered spawner slots and tracked spawned units.</summary>
    public void Reset()
    {
        Array.Clear(_slots);
        _count = 0;
        _spawnedUnits.Clear();
    }

    /// <summary>
    /// Registers one <c>Csac</c> <see cref="EntityRecord"/> into the 0x80-slot table
    /// (func_80060E74). Returns the created slot, or null when the table is full or the record
    /// is not a Csac record.
    /// </summary>
    public CsacSpawnerSlot? Register(EntityRecord record, CsacSpawnerParameters? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Container != EntityContainer.Csac || _count >= Capacity) return null;

        var p = parameters ?? CsacSpawnerParameters.Default;
        // 80060E74.c:129-136: raw delay scaled by 5; negative delay is negated and used directly
        // as the first timer, positive delay draws one PRNG value modulo the scaled delay.
        int scaled = p.RespawnDelayRaw * 5;
        int delay;
        int initialTimer;
        if (scaled < 0)
        {
            delay = -scaled;
            initialTimer = delay;
        }
        else if (scaled > 0)
        {
            delay = scaled;
            initialTimer = (int)((uint)Random.Next() % (uint)delay);
        }
        else
        {
            delay = 0;
            initialTimer = 0;
        }

        var handler = EntityTypeDispatch.ResolveHandler(record.Type);
        var slot = new CsacSpawnerSlot(record, handler, delay, p.MaxConcurrent, p.InitialFlags, p.InitialCount, initialTimer);
        _slots[_count++] = slot;
        return slot;
    }

    /// <summary>
    /// Registers every <c>Csac</c> record from <paramref name="records"/> up to <see cref="Capacity"/>
    /// and returns the number of slots registered.
    /// </summary>
    public int RegisterFromRecords(
        IReadOnlyList<EntityRecord> records,
        Func<EntityRecord, CsacSpawnerParameters?>? parameterResolver = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        int added = 0;
        foreach (var record in records)
        {
            if (record.Container != EntityContainer.Csac) continue;
            if (_count >= Capacity) break;
            var parameters = parameterResolver?.Invoke(record);
            if (Register(record, parameters) is not null)
                added++;
        }
        return added;
    }

    /// <summary>
    /// Port of func_8006158C (gApplySpawnerCommandById) + func_800614BC
    /// (gApplyRegisteredSpawnerCommand): finds the first registered slot whose record Id matches
    /// <paramref name="recordId"/> and applies command 0x46 or 0x47.
    /// </summary>
    public bool ApplyCommandById(int recordId, int command, int arg2)
    {
        for (int i = 0; i < _count; i++)
        {
            var slot = _slots[i]!;
            if (slot.Record.Id != recordId) continue;
            ApplyCommand(slot, command, arg2);
            return true;
        }
        return false;
    }

    /// <summary>Port of func_800614BC on a resolved spawner slot.</summary>
    public static void ApplyCommand(CsacSpawnerSlot slot, int command, int arg2)
    {
        ArgumentNullException.ThrowIfNull(slot);
        switch (command)
        {
            case CommandSetEnabled:
                if (arg2 != 0)
                {
                    if ((slot.Flags & 1) != 0)
                    {
                        slot.Flags = (ushort)(slot.Flags & 0xFFFE);
                        slot.Timer = 0;
                    }
                }
                else
                {
                    slot.Flags = (ushort)(slot.Flags | 1);
                }
                break;

            case CommandQueueBurst:
                if (slot.TotalSpawned >= slot.MaxConcurrent || slot.RemainingCount == 0)
                    return;
                if (arg2 != 0)
                {
                    uint raw = slot.Flags;
                    uint part = (((raw >> 8) & 0xFu) + 1u) & 0xFu;
                    slot.Flags = (ushort)((raw & 0xF0FFu) | (part << 8));
                }
                else
                {
                    uint raw = slot.Flags;
                    uint high = ((raw & 0xF000u) + 0x1000u) & 0xF000u;
                    slot.Flags = (ushort)((raw & 0x0FFFu) | high);
                }
                break;
        }
    }

    /// <summary>
    /// Port of func_800616A0 (gUpdateRegisteredSpawners): drains queued burst counts (flags
    /// bits 8..11 and 12..15), clears the upper byte of flags, and ticks active interval timers
    /// by <paramref name="elapsed"/>, firing due spawns through <see cref="TrySpawn"/>.
    /// Returns the number of units spawned during this drain pass.
    /// </summary>
    public int Drain(int elapsed)
    {
        int spawned = 0;
        for (int index = 0; index < _count; index++)
        {
            var slot = _slots[index]!;
            int burst1 = (slot.Flags >> 8) & 0xF;
            for (int i = 0; i < burst1; i++)
            {
                if (TrySpawn(slot, spawnMode: 1) is not null) spawned++;
            }
            int burst0 = (slot.Flags >> 12) & 0xF;
            for (int i = 0; i < burst0; i++)
            {
                if (TrySpawn(slot, spawnMode: 0) is not null) spawned++;
            }

            ushort flags = slot.Flags;
            slot.Flags = (ushort)(flags & 0xFF);

            if (slot.TotalSpawned < slot.MaxConcurrent && slot.RemainingCount != 0 && (flags & 1) == 0)
            {
                slot.Timer = unchecked((short)(slot.Timer - elapsed));
                if (slot.Timer < 0)
                {
                    slot.Timer = unchecked((short)(slot.Timer + slot.RespawnDelay));
                    if (TrySpawn(slot, spawnMode: 1) is not null) spawned++;
                }
            }
        }
        return spawned;
    }

    /// <summary>
    /// Port of func_8006125C (gTrySpawnRegisteredUnit): evaluates <see cref="GatePredicate"/>,
    /// updates counters, creates a <see cref="SpawnedCsacUnit"/>, registers it into the attached
    /// <see cref="MapObjectRegistry"/> (when allocated) and <see cref="EntityWorld"/>, and invokes
    /// <see cref="OnPostSpawnScript"/>.
    /// </summary>
    public SpawnedCsacUnit? TrySpawn(CsacSpawnerSlot slot, int spawnMode)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (!GatePredicate(slot, spawnMode)) return null;

        if (slot.RemainingCount > 0) slot.RemainingCount--;
        slot.TotalSpawned++;

        var unit = new SpawnedCsacUnit(_nextRuntimeId++, slot, spawnMode, UnitMessageHandler);
        unit.RuntimeObject = _registry?.CreateMissionObject(slot.Record, unit.Id);
        unit.SyncRuntimePosition();
        _spawnedUnits.Add(unit);
        if (_registry is { LiveObjects.IsAllocated: true })
            _registry.LiveObjects.Register(unit);

        // 8006125C.c:74-76 calls gSpawnTargetableEntityId before the post-spawn script.
        // 8004D4F8.c:56-59 adds the initialized entity to the list with the spawned-unit flag.
        // Outposts already have a specialized entity that the scene spawns instead.
        if (unit.DispatchType != CaptureObjective.EntityType)
            _world?.Spawn(unit, targetable: true);

        OnPostSpawnScript?.Invoke(slot, unit);
        return unit;
    }

    /// <summary>
    /// Port of func_80061020 (gRecordSpawnedUnitRetirement), run by the retirement sweep for every
    /// entity with the spawned-unit flag (8004D598.c:51-53): the live counter of every registered
    /// slot whose record id matches the retired unit's record id drops by one, so the drain gate
    /// of func_800616A0 (<c>TotalSpawned &lt; MaxConcurrent</c>) frees the slot to spawn a
    /// replacement - this is what lets a destroyed outpost come back.
    /// </summary>
    /// <remarks>
    /// The retail function keeps decrementing every matching entry and has no floor at zero;
    /// a slot never spawned through <see cref="TrySpawn"/> goes to -1 exactly as in the C. The
    /// statistics half of func_80061020 - the +0xAC kill counters of the player blocks and the
    /// +0x134 mirrors, incremented by definition type when D_8009FA94 +0x5C is 1 - has no ported
    /// counterpart and is left out; the unk18 timer reset it performs for types 1..4 is already
    /// modelled on the capture path through <see cref="MatchScore.OnSinglePlayerCapture"/>.
    /// </remarks>
    public void RecordSpawnedUnitRetirement(LiveEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        // The sweep unlinks and frees the same instance (8004D598.c:54-66); stop its broadcasts too.
        if (entity is SpawnedCsacUnit unit)
            _registry?.LiveObjects.Unregister(unit);

        int targetId = entity.RecordId;
        for (int i = 0; i < _count; i++)
        {
            if (_slots[i]!.Record.Id == targetId)
                _slots[i]!.TotalSpawned--;
        }
    }
}
