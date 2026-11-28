using System.Buffers.Binary;

namespace FutureCop.Game;

/// <summary>The per-type handler an army unit's dispatch type selects (func_8002AE54, 8002AE54.c).</summary>
public enum ArmyTypeHandlerKind
{
    /// <summary>No army handler: the record keeps the plain locomotion the spawner already wired.</summary>
    None,

    /// <summary>Type 5 rail vehicle: func_800382B4 msg 2 -> gInitRailMoverEntity (800378BC.c:96-104).</summary>
    RailVehicle,

    /// <summary>Type 9 hover drone: func_80032F3C msg 2 -> gInitHoverEnemy (80032180.c:195-206).</summary>
    HoverDrone,

    /// <summary>Type 28 multi-turret tank: func_800398EC msg 2 -> func_800397AC (800397AC.c:31-38).</summary>
    MultiTurretTank,
}

/// <summary>
/// The type half of the general entity handler switch of the port's army units: which retail
/// handler a dispatch type resolves to and the attack scheduler work every mobile type shares.
/// </summary>
/// <remarks>
/// <para>
/// All three mobile army types own the same 0x28-byte attack scheduler slot at entity + 0xA0:
/// type 5 and type 28 initialize it from the record descriptor at source + 0x24 in
/// gInitRailMoverEntity (800378BC.c:103), type 9 in gInitHoverEnemy (80032180.c:205). The slot is
/// stepped once per frame by func_80060238 - type 5/28 from their message-9 arm
/// (800382B4.c:292-293, type 28 delegates to the same class-5 tick at 800398EC.c) and type 9 from
/// the end of func_800324E0 (800324E0.c:372-373) - with <c>fireFlag</c> 1.
/// </para>
/// <para>
/// Retail assigns unit targets through command 1 of the entity command dispatcher
/// (gDispatchType26Command.c:129-153 writes the slot's +0x18), which the port does not have yet
/// (docs/todo/05). Until that path exists the handler runs the retail acquisition routine
/// func_800600E8 with the descriptor's own search fields - the same function the scheduler calls
/// for descriptors with flag 0x4000 - so a purchased unit engages on the production path.
/// TODO(todo/05): drop the Acquire call once the command queue feeds slot.Target18.
/// </para>
/// </remarks>
public sealed class ArmyTypeHandler
{
    private ArmyTypeHandler(ArmyTypeHandlerKind kind)
    {
        Kind = kind;
    }

    /// <summary>Which handler func_8002AE54 installs for this dispatch type.</summary>
    public ArmyTypeHandlerKind Kind { get; }

    /// <summary>Entity + 0xA0: the attack/weapon scheduler slot of the unit.</summary>
    public AttackSlot Slot { get; } = new();

    /// <summary>True when the descriptor resolved to a definition of D_800A2300.</summary>
    public bool HasAttack => Slot.Definition is not null;

    /// <summary>
    /// Maps the dispatch type through the port's func_8002AE54 switch
    /// (<see cref="EntityTypeDispatch.ResolveHandler"/>) to an armed handler, or null when the type
    /// is not one of the three mobile army classes.
    /// </summary>
    public static ArmyTypeHandler? Create(EntityRecord record, AttackDefinitionTable? table)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArmyTypeHandlerKind kind = EntityTypeDispatch.ResolveHandler(record.Type) switch
        {
            EntityHandlerKind.Type5Vehicle => ArmyTypeHandlerKind.RailVehicle,       // func_800382B4
            EntityHandlerKind.TumblingFlyer => ArmyTypeHandlerKind.HoverDrone,       // func_80032F3C
            EntityHandlerKind.Type28MultiTurret => ArmyTypeHandlerKind.MultiTurretTank, // func_800398EC
            _ => ArmyTypeHandlerKind.None,
        };
        if (kind == ArmyTypeHandlerKind.None) return null;

        var handler = new ArmyTypeHandler(kind);
        handler.InitAttackSlot(record, table);
        return handler;
    }

    /// <summary>
    /// Message 2 half: gInitAttackSchedulerSlot from the record's 16-byte descriptor
    /// (800378BC.c:103, 80032180.c:205, 80060668.c:77-107).
    /// </summary>
    private void InitAttackSlot(EntityRecord record, AttackDefinitionTable? table)
    {
        if (table is null) return;
        AttackDescriptorConfig config = ReadDescriptor(record);
        // func_8005FF70 returns -1 for a key the mission never loaded and func_80060668 then
        // forms &D_800A2300[-1]. The port leaves the slot unarmed instead of the wild access
        // (same policy as EnemyAttackScheduler.InitSlot).
        if (config.Key < 0 || FindKey(table, config.Key) < 0) return;
        EnemyAttackScheduler.InitSlot(Slot, config, table);
    }

    /// <summary>
    /// Message 6 half: func_800600E8 (acquisition) and func_80060238 (scheduler step), with the
    /// fireFlag the army handlers pass (800382B4.c:292-293).
    /// </summary>
    public int Step(int frameDelta, EnemyEntity owner, EnemyWorld env)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(env);
        if (Slot.Definition is null) return 0;
        if (Slot.TargetClass06 != 0)
            EnemyAttackScheduler.Acquire(Slot, frameDelta, owner, env);
        return EnemyAttackScheduler.Step(Slot, frameDelta, owner, null, 1, env);
    }

    /// <summary>
    /// The 16-byte descriptor (struct Unk60668In, 80060668.c:15-25) at source + 0x24:
    /// s16 key, u16 flags, u8 search, u8 class, u16 range, u16 angle, u16 angle, u16 field, s16 rate.
    /// </summary>
    private static AttackDescriptorConfig ReadDescriptor(EntityRecord record)
    {
        byte[] s = record.Source;
        int o = 0x24;
        return new AttackDescriptorConfig
        {
            Key = o + 2 <= s.Length ? BinaryPrimitives.ReadInt16LittleEndian(s.AsSpan(o)) : unchecked((short)-1),
            FlagsF02 = o + 4 <= s.Length ? BinaryPrimitives.ReadUInt16LittleEndian(s.AsSpan(o + 2)) : (ushort)0,
            SearchF04 = o + 5 <= s.Length ? s[o + 4] : (byte)0,
            TargetClassF05 = o + 6 <= s.Length ? s[o + 5] : (byte)0,
            RangeF06 = o + 8 <= s.Length ? BinaryPrimitives.ReadUInt16LittleEndian(s.AsSpan(o + 6)) : (ushort)0,
            AngleF08 = o + 10 <= s.Length ? BinaryPrimitives.ReadUInt16LittleEndian(s.AsSpan(o + 8)) : (ushort)0,
            AngleF0A = o + 12 <= s.Length ? BinaryPrimitives.ReadUInt16LittleEndian(s.AsSpan(o + 10)) : (ushort)0,
            Field0C = o + 14 <= s.Length ? BinaryPrimitives.ReadUInt16LittleEndian(s.AsSpan(o + 12)) : (ushort)0,
            RateF0E = o + 16 <= s.Length ? BinaryPrimitives.ReadInt16LittleEndian(s.AsSpan(o + 14)) : (short)0,
        };
    }

    private static int FindKey(AttackDefinitionTable table, int key)
    {
        AttackDefinition[] entries = table.Entries;
        int count = Math.Min(table.Count, entries.Length);
        for (int i = 0; i < count; i++)
            if (entries[i].Key == key) return i;
        return -1;
    }
}
