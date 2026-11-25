namespace FutureCop.Game;

/// <summary>
/// Collision coordinate of an entity (<c>obj + 0x28 + 0x34</c>, the <c>Coord</c> record of
/// 800620D8.c:4): position <c>+0x5C/+0x60/+0x64</c>, instance radius <c>+0x68</c>, height <c>+0x6C</c>
/// and width <c>+0x6E</c>, as gInitModelInstance (func_80013AF0) fills them from frame 0 of the model.
/// </summary>
/// <param name="X">obj+0x5C.</param>
/// <param name="Y">obj+0x60.</param>
/// <param name="Z">obj+0x64.</param>
/// <param name="Radius">obj+0x68: the frame radius shifted left by four.</param>
/// <param name="Height">obj+0x6C (s16 in 800620D8.c:4).</param>
/// <param name="Width">obj+0x6E (s16 in 800620D8.c:4).</param>
public readonly record struct CollisionCoord(int X, int Y, int Z, int Radius, int Height, int Width)
{
    /// <summary>
    /// The coordinate of a model instance at a world position. func_80013AF0 reads the frame table
    /// pointer at model + 0x34 and takes <c>lh +0x14</c> &lt;&lt; 4 as the radius, <c>lhu +0x10</c> as the
    /// height and <c>lhu +0x16</c> as the width (asm func_80013AF0 at 0x80013B08-0x80013B44).
    /// func_800518A8:100-101 reads frame records at that pointer + 8, so those three are the s16
    /// words 4, 6 and 7 of the first 3DBB record (<c>Model.FrameBounds[0]</c>). A model without a
    /// record binds with radius, height and width 0, like the null-model branch of func_80013AF0.
    /// </summary>
    public static CollisionCoord FromFrameBounds(int x, int y, int z, IReadOnlyList<short[]> frameBounds)
    {
        ArgumentNullException.ThrowIfNull(frameBounds);
        if (frameBounds.Count == 0 || frameBounds[0].Length < 8) return new CollisionCoord(x, y, z, 0, 0, 0);
        var b = frameBounds[0];
        return new CollisionCoord(x, y, z, b[6] << 4, (short)(ushort)b[4], (short)(ushort)b[7]);
    }
}

/// <summary>
/// Message 14 payload (the direct-hit record of 8002AA1C.c:9-22): source word <c>+0x00</c>, damage
/// s16 <c>+0x04</c>, kind u8 <c>+0x06</c> and the attacking entity at <c>+0x08</c>.
/// </summary>
public sealed record DirectHit(uint SourceWord, short Damage, byte Kind, LiveEntity? Attacker);

/// <summary>
/// Message 9 payload of gStepEntityBaseCountdown (8002A940.c, struct Unk2A940Arg2): source word
/// <c>+0x00</c>, source entity <c>+0x08</c> and the u16 amount <c>+0x0E</c>.
/// </summary>
public sealed record AreaHit(uint SourceWord, LiveEntity? Source, ushort Amount);

/// <summary>
/// The entity header words the damage and retirement chain reads and writes, with the shared base
/// handler gEntityBaseHandler (func_8002AD58) for the messages of that chain.
/// </summary>
public abstract class LiveEntity
{
    /// <summary>obj+0x24 bit: hits are ignored (8002AA1C.c:87).</summary>
    public const uint IgnoreHitsFlag = 0x80;

    /// <summary>obj+0x24 bit set by a hit (8002AA1C.c:101, 8002A940.c) and cleared by the cull (8004DF28.c:143).</summary>
    public const uint DamagedFlag = 0x2000000;

    /// <summary>obj+0x24 bit: already retired, set with 0x10 by gRetireEntityBase (8002A810.c:101-105).</summary>
    public const uint RetiredFlag = 0x01000000;

    /// <summary>obj+0x24 bits gRetireEntityBase sets (8002A810.c:105).</summary>
    public const uint RetireFlags = 0x01000010;

    /// <summary>obj+0x24 bit set by the level reset before it retires an entity (8004CCDC.c:53).</summary>
    public const uint ResetFlag = 0x4000000;

    /// <summary>obj+0x24 bit: skip the retire script notification (8004D958.c:53).</summary>
    public const uint NoRetireScriptFlag = 0x8000000;

    /// <summary>obj+0x24 bit of an entity spawned by gSpawnTargetableEntityId (8004D4F8.c:59).</summary>
    public const uint SpawnedUnitFlag = 0x10000000;

    /// <summary>obj+0x24 bit: the quartile latch for slot spawns (8003BA50.c, 8004CE04.c:61-63).</summary>
    public const uint QuartileFlag = 0x4000;

    /// <summary>obj+0x24 bit that keeps the entity out of slot bookkeeping (8002AA1C.c:123, 8002A940.c).</summary>
    public const uint SelfPositionedFlag = 0x40000;

    /// <summary>obj+0x24 bit: always in the active list (8004DF28.c:142).</summary>
    public const uint AlwaysActiveFlag = 8;

    /// <summary>obj+0x24 bit: messages 10 gate each collision pair (800623FC.c, 8002C584.c:299-301).</summary>
    public const uint PairGateFlag = 0x800000;

    /// <summary>obj+0x08: the runtime id (<c>++D_8009F7A0</c>, 8004D444.c); 0 once retired (8004D958.c:59).</summary>
    public int RuntimeId { get; internal set; }

    /// <summary>obj+0x14 (u16): the record id.</summary>
    public abstract int RecordId { get; }

    /// <summary>obj+0x16: the dispatch type byte.</summary>
    public abstract int DispatchType { get; }

    /// <summary>obj+0x19: the low nibble is the slot column, bits 3.. hold the hit kind (8002AA1C.c:106-111).</summary>
    public byte Flags19 { get; set; }

    /// <summary>obj+0x1A (s16): the contact damage another entity takes from this one (8002AA1C.c:95).</summary>
    public short Word1A { get; set; }

    /// <summary>obj+0x1C (s16): health; below 0 the frame sweep sends message 4 (8004CE04.c:59-60).</summary>
    public short Health { get; set; }

    /// <summary>obj+0x1E (s16): the spawn health (8002A5F4.c:134), the range of the quartile index.</summary>
    public short MaxHealth { get; set; }

    /// <summary>obj+0x20: the slot index into D_800A0C70 (8002A5F4.c:127).</summary>
    public byte SlotIndex { get; set; }

    /// <summary>obj+0x24: the flag word.</summary>
    public uint Flags { get; internal set; }

    /// <summary>obj+0x8C: who dealt the lethal hit (8002AA1C.c:114-121, 8002A940.c).</summary>
    public uint SourceUnitWord { get; set; }

    /// <summary>obj+0x94: the damage script, an RPNS offset (8002A5F4.c:108; 8002AA1C.c:128-130).</summary>
    public int DamageScript { get; set; }

    /// <summary>obj+0x98: the retire script, an RPNS offset (8002A5F4.c:109; 8004D958.c:53-55).</summary>
    public int RetireScript { get; set; }

    /// <summary>The collision coordinate, or null when the entity takes no part in pair collisions.</summary>
    public CollisionCoord? Coord { get; set; }

    /// <summary>obj+0x50 / +0x58: the position the active-list cull measures (8004DF28.c:119-129).</summary>
    public abstract (int X, int Z) GroundPosition { get; }

    /// <summary>The entity's message handler (the function pointer at obj+0x04).</summary>
    public abstract int HandleMessage(int message, EntityWorld world, object? data);

    /// <summary>Called after the sweep unlinked the entity, where retail frees it (8004D598.c:66).</summary>
    protected internal virtual void OnFreed()
    {
    }

    /// <summary>
    /// gEntityBaseHandler (8002AD58.c:75-111) for the messages of the damage and retirement chain:
    /// 4 retires (:88-89), 5/6/7/8/13 return 0 (:90-95), 9 steps the countdown damage (:96-97), 11/14
    /// apply a hit (:98-100), 10/16 return 1 (:103-105) and every other message returns -1 (:108-109).
    /// Spawn (2), draw (3), the field query (12) and the heading (15) are not routed here: the port's
    /// own classes build and draw the entity.
    /// </summary>
    public static int BaseHandler(int message, LiveEntity entity, EntityWorld world, object? data)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(world);
        switch (message)
        {
            case 4: return RetireBase(entity, world, true, true);
            case 5: case 6: case 7: case 8: case 13: return 0;
            case 9: return data is AreaHit area ? StepCountdown(entity, world, area) : 0;
            case 11: case 14: return ApplyBaseDamage(message, entity, world, data);
            case 10: case 16: return 1;
            default: return -1;
        }
    }

    /// <summary>
    /// gApplyEntityBaseDamage (8002AA1C.c:79-134). Message 14 reads the <see cref="DirectHit"/> record,
    /// message 11 reads the other entity's +0x1A, +0x14 and +0x08 (the <c>Unk2AA1CHit</c> layout laid
    /// over the colliding entity by 800620D8.c:99-100).
    /// </summary>
    public static int ApplyBaseDamage(int message, LiveEntity entity, EntityWorld world, object? hit)
    {
        if ((entity.Flags & IgnoreHitsFlag) != 0) return 0;                       // :87-89
        int amount;
        uint source;
        var direct = hit as DirectHit;
        var other = hit as LiveEntity;
        if (message == 0xE)
        {
            if (direct is null) return 0;
            amount = direct.Damage;                                               // :92
            source = direct.SourceWord;                                           // :93
        }
        else
        {
            if (other is null) return 0;
            amount = other.Word1A;                                                // :95
            source = (uint)(other.RecordId & 0xFFFF);                             // :96
        }
        source &= 0x7FFF;                                                         // :98
        if (amount <= 0) return 0;                                                // :100

        entity.Flags |= DamagedFlag;                                              // :101
        int oldHealth = entity.Health;                                            // :102
        entity.Health = unchecked((short)(entity.Health - amount));               // :103
        if (message == 0xE)
        {
            int kind = direct!.Kind;                                              // :106
            if (kind >= 2) entity.Flags19 = (byte)(entity.Flags19 | ((kind << 3) & 0x7F0)); // :107-108
        }
        else
        {
            entity.Flags19 &= 0xF;                                                // :111
        }

        if (entity.Health < 0)                                                    // :114
        {
            if (message == 0xE)
            {
                if (direct!.Attacker is { } attacker)                             // :116
                    entity.SourceUnitWord = (uint)attacker.RuntimeId | 0x40000000; // :117
            }
            else
            {
                entity.SourceUnitWord = (uint)other!.RuntimeId;                   // :120, from.source = +0x08
            }
        }
        else if (entity.SlotIndex != 0 && (entity.Flags & SelfPositionedFlag) == 0) // :123
        {
            FlagQuartileChange(entity, world, oldHealth);                         // :124
        }

        if (entity.DamageScript != 0)                                             // :128
            world.OnScript?.Invoke(entity, entity.DamageScript, entity.Health < 0 ? 1 : 0, (int)source); // :129
        return 0;
    }

    /// <summary>gStepEntityBaseCountdown (func_8002A940): the message-9 damage, without the 0x80 guard.</summary>
    public static int StepCountdown(LiveEntity entity, EntityWorld world, AreaHit hit)
    {
        int delta = hit.Amount;                                                   // delta = arg->unk0E
        if (delta <= 0) return delta;
        int previous = entity.Health;
        entity.Health = unchecked((short)((ushort)entity.Health - delta));        // (u16)unk1C - delta
        if (entity.Health < 0)
        {
            if (hit.Source is { } source) entity.SourceUnitWord = (uint)source.RuntimeId; // arg->unk08->unk08
        }
        else if (entity.SlotIndex != 0 && (entity.Flags & SelfPositionedFlag) == 0)
        {
            FlagQuartileChange(entity, world, previous);
        }
        entity.Flags |= DamagedFlag;
        if (entity.DamageScript != 0)
            world.OnScript?.Invoke(entity, entity.DamageScript, entity.Health < 0 ? 1 : 0, (int)(hit.SourceWord & 0x7FFF));
        return delta;
    }

    /// <summary>gQuarterIndex (8003B65C.c): which quarter of <paramref name="range"/> holds <paramref name="value"/>.</summary>
    public static int QuarterIndex(int value, int range)
    {
        if (value <= range >> 2) return 0;
        if (value < range >> 1) return 1;
        if (value < (range * 3) >> 2) return 2;
        return 3;
    }

    /// <summary>
    /// gFlagQuartileChange (8003BA50.c): sets <see cref="QuartileFlag"/> when the health crossed a
    /// quarter of <see cref="MaxHealth"/> and the slot has a descriptor in D_800A0C70.
    /// </summary>
    public static void FlagQuartileChange(LiveEntity entity, EntityWorld world, int value)
    {
        if (entity.Health < 0) return;
        int previous = QuarterIndex(value, entity.MaxHealth);
        int current = QuarterIndex(entity.Health, entity.MaxHealth);
        if (world.HasSlotDescriptor?.Invoke(entity.SlotIndex) == true && current != previous)
            entity.Flags |= QuartileFlag;
    }

    /// <summary>
    /// gRetireEntityBase (8002A810.c:91-144): the message-4 retire. Returns 0 when the entity was
    /// already retired, otherwise 1.
    /// </summary>
    public static int RetireBase(LiveEntity entity, EntityWorld world, bool retire, bool footprint)
    {
        if ((entity.Flags & RetiredFlag) != 0) return 0;                          // :101-103
        entity.Flags |= RetireFlags;                                              // :105
        LiveEntity? owner = entity.SourceUnitWord != 0
            ? world.FindUnitById((int)(entity.SourceUnitWord & 0x3FFFFFFF))        // :107-110
            : null;
        if (entity.SlotIndex != 0)                                                // :112
        {
            // :113-127: only a type-2 owner (a projectile) lends its +0x78..+0x80 and +0x1A words.
            var record = owner is { DispatchType: 2 } ? owner : null;
            if (footprint)                                                        // :128
                world.OnRetireFootprint?.Invoke(entity, record, (entity.Flags19 >> 3) & 0x1E); // :131-132, mode 2
        }
        // :136-138 (+0x74 -> func_80031690) is not modelled: no ported entity carries a +0x74 block.
        if (retire) Retire(entity, world);                                        // :140-142
        return 1;
    }

    /// <summary>
    /// gRetireEntity (8004D958.c:48-60): notifies the retire script and clears the runtime id. The
    /// looping-sound stop of :56-58 has no counterpart: the port allocates no positional channels.
    /// </summary>
    public static void Retire(LiveEntity entity, EntityWorld world)
    {
        if (entity.RuntimeId == 0) return;                                        // :50-52
        if ((entity.Flags & NoRetireScriptFlag) == 0 && entity.RetireScript != 0)  // :53
            world.OnScript?.Invoke(entity, entity.RetireScript, (int)(((entity.Flags >> 26) ^ 1) & 1), -1); // :54
        entity.RuntimeId = 0;                                                     // :59
    }
}

/// <summary>
/// The player projectile as the collision pass sees it: a type-2 entity (gSpawnDirectShot,
/// 8005E47C.c) whose handler func_80036624 answers messages 4, 5 and 11. The port's shots live in
/// <c>LevelWeapons</c>; a shot becomes this entity when it touches an entity of the
/// <see cref="EntityWorld"/>, which is the only moment retail reads it.
/// </summary>
public sealed class ProjectileContact : LiveEntity
{
    private readonly int _recordId;
    private readonly (int X, int Z) _position;

    /// <param name="shooterRecordId">The firing entity's +0x14: args.flags = ent-&gt;unk14 | 0x8000 (8005E47C.c:73), stored to +0x14 by 8002A5F4.c:119.</param>
    /// <param name="damage">Weapon obj+0x00: args.unk1A (8005E47C.c:101), copied to +0x1A by 8002A5F4.c:131 and again by 80035904.c:209.</param>
    /// <param name="slotIndex">Weapon obj+0x0E: args.unk4C (8005E47C.c:106), copied to +0x20 by 80035904.c:180.</param>
    /// <param name="x">Projectile position x.</param>
    /// <param name="z">Projectile position z.</param>
    public ProjectileContact(int shooterRecordId, short damage, byte slotIndex, int x, int z)
    {
        _recordId = (shooterRecordId | 0x8000) & 0xFFFF;
        Word1A = damage;
        SlotIndex = slotIndex;
        _position = (x, z);
        // args.unk14 = flags | 8 (8005E47C.c:85) and 80035904.c:239 adds 0x800002.
        Flags = 0x800002 | AlwaysActiveFlag;
    }

    /// <inheritdoc/>
    public override int RecordId => _recordId;

    /// <summary>Entity type 2 (8005E47C.c: args.kind = 2).</summary>
    public override int DispatchType => 2;

    /// <inheritdoc/>
    public override (int X, int Z) GroundPosition => _position;

    /// <summary>
    /// func_80036624 for the chain's messages. Message 11 sets the health to -1 so the next frame
    /// sweep retires the projectile (80036624.c:155-156); the penetrating class 0x15 of :146-154 is
    /// not modelled. Message 4 skips the radial blast of :164-174 (gApplyRadialContactDamage is not
    /// ported) and retires through the base handler (:175, :179). Message 5 returns 0 (:176-177).
    /// </summary>
    public override int HandleMessage(int message, EntityWorld world, object? data)
    {
        switch (message)
        {
            case 11:
                Health = -1;
                return 0;
            case 5:
                return 0;
            default:
                return BaseHandler(message, this, world, data);
        }
    }
}

/// <summary>
/// The entity list D_8009FA40 with its active subset D_800A8F90 and the frame stages of the solo
/// session (800271A4.c:178-212) that carry an entity from a lethal hit to message 5: the cull, the
/// deferred updates that send message 4, the retirement sweep that sends message 5, and the level
/// reset that retires everything without scoring.
/// </summary>
public sealed class EntityWorld
{
    /// <summary>The 2D distance below which the cull keeps an entity active (8004DF28.c:101,142).</summary>
    public const int ActiveRange = 0x38000;

    private readonly List<LiveEntity> _units = new();
    private readonly List<LiveEntity> _active = new();

    /// <summary>D_8009FA40 in list order.</summary>
    public IReadOnlyList<LiveEntity> Units => _units;

    /// <summary>D_800A8F90: the entities the last <see cref="CullActive"/> kept.</summary>
    public IReadOnlyList<LiveEntity> Active => _active;

    /// <summary>D_8009F7A0: the last runtime id handed out.</summary>
    public int IdCounter { get; private set; }

    /// <summary>
    /// <c>gInterpretEntityScript(script, arg1, arg2, entity)</c> (func_80069D68) for the damage script
    /// (8002AA1C.c:128-130, 8002A940.c) and the retire script (8004D958.c:53-55). A hook because the
    /// RPNS VM is not ported.
    /// </summary>
    public Action<LiveEntity, int, int, int>? OnScript { get; set; }

    /// <summary>D_800A0C70[slot] != 0, read by gFlagQuartileChange (8003BA50.c).</summary>
    public Func<byte, bool>? HasSlotDescriptor { get; set; }

    /// <summary>
    /// gSpawnEntitySlotTypes(entity-&gt;unk20, entity, record, mask, 2) (func_8003B898, 8002A810.c:128-133):
    /// the retire footprint. The second argument is the type-2 owner whose words form the local record,
    /// or null; the third is <c>(+0x19 &gt;&gt; 3) &amp; 0x1E</c>.
    /// </summary>
    public Action<LiveEntity, LiveEntity?, int>? OnRetireFootprint { get; set; }

    /// <summary>gRefreshEntitySlotSpawns(entity, (+0x19 &gt;&gt; 3) &amp; 0x1E) (func_8003BAE4, 8004CE04.c:61-63).</summary>
    public Action<LiveEntity, int>? OnRefreshSlotSpawns { get; set; }

    /// <summary>gRecordSpawnedUnitRetirement (func_80061020), run before message 5 for 0x10000000 entities (8004D598.c:51-53).</summary>
    public Action<LiveEntity>? OnSpawnedUnitRetired { get; set; }

    /// <summary>
    /// Spawns <paramref name="entity"/> like gSpawnQueuedEntity (8004D444.c): the next runtime id, then
    /// the list append of gAppendEntityToList (8004D3D4.c). <paramref name="targetable"/> adds
    /// <see cref="LiveEntity.SpawnedUnitFlag"/> as gSpawnTargetableEntityId does (8004D4F8.c:59).
    /// </summary>
    public LiveEntity Spawn(LiveEntity entity, bool targetable = false)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.RuntimeId = ++IdCounter;
        _units.Add(entity);
        if (targetable) entity.Flags |= LiveEntity.SpawnedUnitFlag;
        return entity;
    }

    /// <summary>
    /// Spawns an outpost and its child part in retail order: the outpost takes its id first, its
    /// message 2 spawns the child (8002C584.c:297), which therefore takes the next id and is linked
    /// in before the outpost itself (8004D444.c: id, message 2, then append).
    /// </summary>
    public CaptureObjective SpawnObjective(CaptureObjective objective, bool targetable = false)
    {
        ArgumentNullException.ThrowIfNull(objective);
        objective.RuntimeId = ++IdCounter;
        if (objective.ChildPart is { } child)
        {
            child.RuntimeId = ++IdCounter;
            _units.Add(child);
        }
        _units.Add(objective);
        if (targetable) objective.Flags |= LiveEntity.SpawnedUnitFlag;
        return objective;
    }

    /// <summary>gFindUnitById (8004D664.c:36-46): the first entity whose runtime id is <paramref name="id"/>.</summary>
    public LiveEntity? FindUnitById(int id)
    {
        foreach (var entity in _units)
            if (entity.RuntimeId == id) return entity;
        return null;
    }

    /// <summary>
    /// The membership rule of gCullActiveEntitiesSolo (8004DF28.c:113-143, 182-188): every entity with
    /// a runtime id that has <see cref="LiveEntity.AlwaysActiveFlag"/> or lies closer than
    /// <see cref="ActiveRange"/> (<c>adx + adz - min / 2</c>) joins D_800A8F90 with 0x20000000 set and
    /// <see cref="LiveEntity.DamagedFlag"/> cleared; the others lose 0x20000000. The capacity cap with
    /// its shrinking threshold (:171-178), the positional sounds and the proximity script (:144-162,
    /// :201-203) are not modelled.
    /// </summary>
    public void CullActive(int x, int z)
    {
        _active.Clear();
        foreach (var node in _units)
        {
            if (node.RuntimeId == 0) continue;
            var (nx, nz) = node.GroundPosition;
            int adx = Math.Abs(nx - x), adz = Math.Abs(nz - z);
            int dist = adx + adz - Math.Min(adx, adz) / 2;
            if ((node.Flags & LiveEntity.AlwaysActiveFlag) != 0 || dist < ActiveRange)
            {
                node.Flags = (node.Flags | 0x20000000) & 0xFDFFFFFF;
                _active.Add(node);
            }
            else
            {
                node.Flags &= 0xDFFFFFFF;
            }
        }
    }

    /// <summary>
    /// gProcessDeferredEntityUpdates (8004CE04.c:49-66): message 4 to every active entity whose health
    /// went below 0, otherwise the quartile latch is consumed.
    /// </summary>
    public void ProcessDeferredUpdates()
    {
        // D_800A8F90 is a frame snapshot: a retire inside the loop does not change its membership.
        foreach (var entity in _active.ToArray())
        {
            if (entity.RuntimeId <= 0) continue;                                  // :56-58
            if (entity.Health < 0)                                                // :59
            {
                entity.HandleMessage(4, this, null);                              // :60
            }
            else if ((entity.Flags & LiveEntity.QuartileFlag) != 0)               // :61
            {
                entity.Flags &= ~LiveEntity.QuartileFlag;                         // :62
                OnRefreshSlotSpawns?.Invoke(entity, (entity.Flags19 >> 3) & 0x1E); // :63
            }
        }
    }

    /// <summary>
    /// gSweepRetiredEntities (8004D598.c:39-71): every entity whose runtime id is 0 gets message 5 and
    /// leaves the list. Returns the number of entities freed.
    /// </summary>
    public int SweepRetired()
    {
        int freed = 0;
        for (int i = 0; i < _units.Count;)
        {
            var entity = _units[i];
            if (entity.RuntimeId != 0) { i++; continue; }                         // :67-69
            if ((entity.Flags & LiveEntity.SpawnedUnitFlag) != 0)                 // :51
                OnSpawnedUnitRetired?.Invoke(entity);                             // :52
            entity.HandleMessage(5, this, null);                                  // :54
            _units.RemoveAt(i);                                                   // :55-64
            _active.Remove(entity);
            entity.OnFreed();                                                     // :66
            freed++;
        }
        return freed;
    }

    /// <summary>
    /// gRetireAndFreeAllEntities (8004CCDC.c:46-62): marks every live entity with
    /// <see cref="LiveEntity.ResetFlag"/>, retires it directly (no message 4) and sweeps, so outposts
    /// never score on a level reset (8002D664.c:183). Resets the id counter.
    /// </summary>
    public void RetireAndFreeAll()
    {
        foreach (var entity in _units.ToArray())
        {
            if (entity.RuntimeId == 0) continue;                                  // :52
            entity.Flags |= LiveEntity.ResetFlag;                                 // :53
            LiveEntity.Retire(entity, this);                                      // :54
        }
        SweepRetired();                                                           // :57
        IdCounter = 0;                                                            // :58
    }
}

/// <summary>
/// The pair test of gTestEntityPairCollision (800620D8.c:35-108) up to the narrow phase.
/// </summary>
public static class EntityPairCollision
{
    /// <summary>
    /// True when the pair reaches gTestPartPairHit (800620D8.c:45-95) with pair flags that carry
    /// 0x200: the octagonal ground distance minus both radii below 0x4000 (:45-59), the 3D distance
    /// minus both radii below 0 (:60-73, :91) and the width and height boxes overlapping (:92-94).
    /// </summary>
    /// <remarks>
    /// The per-part narrow phase func_80061CAC (every PA model carries bit 27, so its two-sided
    /// element test runs) is not ported; the port takes the box overlap of :94 as the hit.
    /// </remarks>
    public static bool ReachesPartTest(CollisionCoord a, CollisionCoord b)
    {
        int dist = CaptureObjective.OctagonalDistance(a.Z - b.Z, a.X - b.X);    // :45-57
        int radius = a.Radius + b.Radius;                                        // :58
        if (dist - radius >= 0x4000) return false;                               // :59
        int dy = a.Y - b.Y;                                                      // :61
        int d = CaptureObjective.OctagonalDistance(dist, dy) - radius;           // :62-72
        if (d >= 0x4000 || d >= 0) return false;                                 // :73, :91
        int test = dist - ((a.Width + b.Width) << 4);                            // :92
        int absDy = ((dy >> 31) + dy) ^ (dy >> 31);                              // :94, |dy| as retail forms it
        return test < 0 && absDy - ((a.Height + b.Height) << 4) < 0;             // :94
    }
}
