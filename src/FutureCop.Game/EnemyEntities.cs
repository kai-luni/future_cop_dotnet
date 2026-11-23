namespace FutureCop.Game;

/// <summary>
/// Command queue of an entity: four (op, arg) byte pairs at object +0x0C.
/// C: 8005FFB8.c:25-28 (struct Script5FFB8), 8002A5B4.c:9-13 (struct Blah).
/// </summary>
public sealed class AttackCommandQueue
{
    public byte[] Ops { get; } = new byte[4];
    public byte[] Args { get; } = new byte[4];

    /// <summary>Opcode at slot 0, the object's +0x0C byte checked by func_80060238 (80060238.c:162-165).</summary>
    public byte Command0 => Ops[0];
}

/// <summary>Script block behind an owner's +0x9C pointer (80060238.c:18-20, 253-262).</summary>
public sealed class SpawnBlock
{
    public byte Active { get; set; }
}

/// <summary>
/// One entry of the runtime definition table D_800A2300 (0x30-byte stride). The table is
/// NOT part of the disc EXE: the SLUS_007.39 text segment ends at 0x8009F800 and D_800A2300
/// lies beyond it, so the entries exist only at runtime, loaded from "tACT" resource
/// chunks at mission boot. C: 80060838.c:42-59 (loader), 800609F4.c (count reset at boot),
/// 80060A00.c (count reset between missions), 8005FF70.c (search).
/// Field offsets follow the fire dispatcher's definition view (8005F6E8.c:32-48) and the
/// func_80060668 entry reads at +0x02/+0x06/+0x0A (asm main.s:93886-93898).
/// </summary>
public sealed class AttackDefinition
{
    public byte Key { get; set; }             // +0x00 key byte of the search (8005FF70.c:37)
    /// <summary>Attack type byte at +0x01 (0 disables the descriptor, 80060238.c:169-171), i.e. S2->unk01.</summary>
    public byte Type { get; set; }
    public ushort W02 { get; set; }           // +0x02: seeds slot +0x20 (80060668 asm 93886-93892)
    public ushort W06 { get; set; }           // +0x06: repeat/burst count (80060238.c:214)
    public short W08 { get; set; }            // +0x08: hold duration (80060238.c:202, 205-209)
    public short W0A { get; set; }            // +0x0A: fire interval (80060238.c:184, 191, 211)
    public ushort W0C { get; set; }           // +0x0C: spawn record passed to the spawners (8005F6E8.c:273-282)
    public ushort W12 { get; set; }           // +0x12: lead speed (8005F6E8.c:255)
    public ushort W14 { get; set; }
    public ushort W16 { get; set; }            // +0x16: lead distance class (8005F6E8.c:245-256)
    public ushort W1A { get; set; }           // +0x1A: impact spawn column (8005F6E8.c:441, 448)
    public ushort W1B { get; set; }           // +0x1B: secondary spawn column (8005F6E8.c:441)
    public ushort W1C { get; set; }           // +0x1C: fire sound id (8005F6E8.c:452)
    public byte W1F { get; set; }              // +0x1F: message type byte (8005F6E8.c:331)
    /// <summary>"aRSL"-resolved word at definition +0x20 (80060838.c:86-95); 0 when absent (8005F6E8.c:414).</summary>
    public int ResolvedW20 { get; set; }
}

/// <summary>Runtime definition table D_800A2300 with its active count D_8009F7F8.</summary>
public sealed class AttackDefinitionTable
{
    public AttackDefinition[] Entries { get; }
    /// <summary>D_8009F7F8: entries loaded so far; defaults to the array length (8005FF70.c:36).</summary>
    public int Count { get; set; }
    public AttackDefinitionTable(AttackDefinition[] entries)
    {
        Entries = entries;
        Count = entries.Length;
    }
}

/// <summary>
/// The 16-byte per-slot config descriptor consumed by func_80060668.
/// C: 80060668.c:15-25 (struct Unk60668In); asm main.s:93819-93905.
/// </summary>
public sealed class AttackDescriptorConfig
{
    public short Key { get; set; }            // f00 (s16 key)
    public ushort FlagsF02 { get; set; }      // f02 -> slot flags
    public ushort SearchF04 { get; set; }     // f04 -> slot +0x07 byte
    public byte TargetClassF05 { get; set; } // f05 -> slot +0x06 byte
    public ushort RangeF06 { get; set; }     // f06 -> slot +0x08 word
    public ushort AngleF08 { get; set; }     // f08 -> half-angle cosine at +0x0C
    public ushort AngleF0A { get; set; }     // f0A -> half-angle cosine at +0x10
    public ushort Field0C { get; set; }       // f0C -> slot +0x0A (target range)
    public short RateF0E { get; set; }        // f0E -> scaled retry period at +0x14
}

/// <summary>
/// One 0x28-byte attack/weapon scheduler slot of an object (usually at +0xA0).
/// Layout: 80060238.c:33-50 (struct Func80060238Obj) and the writes of func_80060668
/// (asm main.s:93819-93905). The func_8005F6E8 views +0x20 as a s16 ammo counter,
/// +0x24 as a s16 cooldown and +0x26 as a u16 burst counter (asm main.s:93218-93298);
/// those are the same physical words the scheduler uses.
/// </summary>
public sealed class AttackSlot
{
    public AttackDefinition? Definition { get; set; }   // +0x00 definition pointer
    public ushort Flags { get; set; }                  // +0x04 flag word (0x20, 0x100, 0x4000, 0x8000)
    public byte TargetClass06 { get; set; }            // +0x06 search/filter type
    public byte Mode07 { get; set; }                   // +0x07 search mode
    public ushort Value08 { get; set; }                // +0x08 filter value
    public short Range0A { get; set; }                 // +0x0A target range (used << 4)
    public int HalfAngle0C { get; set; }               // +0x0C cone cosine (acquire threshold, 800600E8.c:75)
    public int HalfAngle10 { get; set; }               // +0x10 cone cosine (validation threshold, 80060238.c:240)
    public short RetryReload14 { get; set; }           // +0x14 retry period
    public short Retry16 { get; set; }                 // +0x16 retry timer (s16 semantics)
    public EnemyEntity? Target18 { get; set; }         // +0x18 cached target pointer
    public int Target18Value { get; set; }             // +0x1C cached target id
    public short AmmoGate20 { get; set; }              // +0x20 s16 ammo/"armed" counter
    public byte Playing22 { get; set; }                // +0x22 playing byte
    public byte Toggle23 { get; set; }                 // +0x23 toggled byte
    public byte Part23 { get; set; }                   // +0x23 as read by func_8005F6E8 (a0->unk23 muzzle part)
    public ushort FrameAcc24 { get; set; }             // +0x24 frame accumulator (s16 cooldown in the fire tail)
    public ushort Burst26 { get; set; }                // +0x26 repeat/burst counter
}

/// <summary>Arguments recorded for one func_80069D68 script call (80060238.c:262, 800600E8.c:96-97).</summary>
public sealed record InterpretScriptArgs(EnemyEntity Ctx, int Arg1, int Arg2);

/// <summary>Arguments recorded for one gSpawnDirectShot call (func_8005E47C, 8005F6E8.c:414-416).</summary>
public sealed record DirectSpawnArgs(
    AttackSlot Slot,
    AttackDefinition Def,
    int MuzzleX, int MuzzleY, int MuzzleZ,
    int AimX, int AimY, int AimZ,
    uint SpawnFlags,
    int ShotType,
    byte SlotClass,
    short SlotValue,
    byte SrcColumn,
    int Mask,
    EnemyEntity? Target);

/// <summary>Arguments recorded for the secondary gSpawnTypeAtOwner spawns (func_800402DC, 8005F6E8.c:441-448).</summary>
public sealed record TypeAtOwnerSpawn(int Column, int Kind, int OwnerRet, int[] Position, int MarkerWord);

/// <summary>
/// The entity/object block the attack cluster operates on. The C reference files each
/// carry their own local Entity view; this class consolidates the offsets the cluster
/// reads: +0x08 id (8004E80C.c:8, 115-116), +0x14 team word (8004E80C.c:10, 121-123),
/// +0x16 class byte (8004E5F0.c:6, 103; 8004E80C.c:11), +0x18 type byte (8004E80C.c:13),
/// +0x19 priority nibble (8004E80C.c:14, 148), +0x1C state (800600E8.c:10),
/// +0x24 flag word (80060238.c:28, 259), +0x44 facing (8005F6E8.c:71),
/// +0x50/+0x54/+0x58 position (8004E5F0.c:8-10; 8005F6E8.c:67-69 actor position),
/// +0x5C..64 secondary vector triple (8005F6E8.c:19-21, target position for aiming),
/// +0x78/+0x7C/+0x80 heading (8004E5F0.c:13-14), +0x84 state word (8005F6E8.c:78),
/// +0xA0/+0xA4/+0xA8 alternate heading (8004E5F0.c:18-20), +0xC command queue
/// (8005FFB8.c:26-28), +0x9C script block (80060238.c:30).
/// </summary>
public sealed class EnemyEntity
{
    public int Id { get; set; }                    // +0x08
    public ushort Team14 { get; set; }            // +0x14
    public byte Mode16 { get; set; }              // +0x16 class/type byte
    public byte Type17 { get; set; }             // +0x17 (filter type 3, 8004E80C.c:127-129)
    public byte Type18 { get; set; }              // +0x18
    public byte Prio19 { get; set; }              // +0x19
    public short State1C { get; set; }            // +0x1C
    public uint FlagWord24 { get; set; }          // +0x24
    public ushort Word14 { get; set; }            // actor view +0x14 (8005F6E8.c:67 message source id)
    public int FacingX { get; set; }              // +0x44
    public int FacingY { get; set; }
    public int FacingZ { get; set; }
    public int PositionX { get; set; }           // +0x50
    public int PositionY { get; set; }           // +0x54
    public int PositionZ { get; set; }           // +0x58
    public int Vec5CX { get; set; }               // +0x5C secondary/aim position (8005F6E8.c:231-233)
    public int Vec5CY { get; set; }
    public int Vec5CZ { get; set; }
    public int HeadingX { get; set; }             // +0x78
    public int HeadingY { get; set; }             // +0x7C (unused by the port cluster, kept for layout)
    public int HeadingZ { get; set; }             // +0x80
    public int AltHeadingX { get; set; }          // +0xA0
    public int AltHeadingY { get; set; }
    public int AltHeadingZ { get; set; }
    public int State84 { get; set; }              // +0x84 (8005F6E8.c:78)
    /// <summary>s16 at (block behind +0xAC)+0x324 for class byte 1 (8005F6E8.c:245-249).</summary>
    public short Timer324 { get; set; }
    /// <summary>s16 at ((block behind +0xB4)->+0xAC)+0x324 for class byte 0x12 (8005F6E8.c:250-254).</summary>
    public short TimerB4Sub324 { get; set; }
    public AttackCommandQueue Queue { get; set; } = new(); // +0x0C command queue
    public SpawnBlock? SpawnBlock { get; set; }   // +0x9C
    /// <summary>Mode-3 line-of-sight verdict of func_800684AC per candidate (stub: see HasLineOfSightTo).</summary>
    public Func<EnemyEntity, EnemyEntity, bool>? LineOfSightProbe { get; set; }

    /// <summary>
    /// Mode-3 visibility test of gFindTargetEntity: func_800684AC(ctx+0x5C, o+0x5C, 0,0,0) != 0
    /// means an obstruction and disqualifies the candidate (8004E80C.c:156-158). The ray
    /// tracer itself is not part of this headless port, so a probe must be supplied.
    /// </summary>
    public bool HasLineOfSightTo(EnemyEntity other) =>
        LineOfSightProbe?.Invoke(this, other)
        ?? throw new NotImplementedException(
            "EnemyAttackScheduler.cs: func_800684AC line-of-sight trace (8004E80C.c:157) is not part of this headless port");
}

/// <summary>
/// Injected environment of the attack cluster: the global entity table words
/// D_800A8F90 (count at +0x00, limit at +0x04; C 8004E80C.c:21-25, 80060238.c:52),
/// the definition table, and the external effects the retail code reaches through
/// the PlayStation runtime. Hooks without a default throw at the call site so an
/// unmodelled path fails loudly instead of faking success.
/// </summary>
public sealed class EnemyWorld
{
    private int _nextId = 1;
    public int Count { get; set; }                // D_800A8F90[0]: used entity slots
    public int Limit { get; set; }                // D_800A8F90[1]: table limit
    public AttackDefinitionTable Table { get; set; } = new(Array.Empty<AttackDefinition>());
    public List<EnemyEntity> Entities { get; } = new();

    public EnemyWorld(int count, int limit)
    {
        Count = count;
        Limit = limit;
    }

    /// <summary>Free entity slots: D_800A8F90[1] - D_800A8F90[0] with the retail u32 wrap (80060238.c:156).</summary>
    public int FreeSlots => unchecked((int)((uint)Limit - (uint)Count));

    public EnemyEntity NewOwner()
    {
        var e = new EnemyEntity { Id = _nextId++ };
        Entities.Add(e);
        return e;
    }

    public Func<uint> Random { get; set; } = () => 0u;
    public Func<byte, EnemyEntity, (int X, int Y, int Z)>? TransformPartVertex { get; set; }
    public Func<EnemyEntity, EnemyEntity, int>? OnTraceMapRay { get; set; }
    public List<(int Id, int X, int Z, int Arg)> SoundsPlayed { get; } = new();
    public Action<int, int, int, int>? OnPlaySound { get; set; }
    public List<DirectSpawnArgs> DirectShots { get; } = new();
    public Func<DirectSpawnArgs, int>? OnSpawnDirectShot { get; set; }
    public List<TypeAtOwnerSpawn> TypeAtOwnerSpawns { get; } = new();
    public List<int> StaticProjectiles { get; } = new();
    public Func<int, int>? OnSpawnStaticProjectile { get; set; }
    public List<int> TargetedShots { get; } = new();
    public Func<int, int>? OnSpawnTargetedShot { get; set; }
    public List<(int Type, int Column)> LobShots { get; } = new();
    public Func<int, int, int>? OnSpawnLobShot { get; set; }
    public List<(int SeedOrVec, int JitterFlag, uint SpawnFlags)> JitterProjectiles { get; } = new();
    public Func<int[]?, int, uint, int>? OnSpawnJitterProjectile { get; set; }
    public List<(int A0, int A1, int A2, uint Colour)> TracerBeams { get; } = new();
    public Func<int, int, int, uint, int>? OnSpawnTracerBeam { get; set; }

    internal int SpawnTracerBeam(int a0, int a1, int a2, uint colour)
    {
        TracerBeams.Add((a0, a1, a2, colour));
        return OnSpawnTracerBeam?.Invoke(a0, a1, a2, colour)
            ?? throw new NotImplementedException(
                "EnemyWeaponFire.cs: func_8006F0E8 tracer beam spawn (8005F6E8.c:339) is not part of this headless port");
    }

    internal void TargetMessage(EnemyEntity target)
    {
        if (OnTargetMessage is null)
            throw new NotImplementedException(
                "EnemyWeaponFire.cs: target message callback s1->unk04 (8005F6E8.c:335) is not part of this headless port");
        TargetMessages.Add(target);
        OnTargetMessage(target);
    }
    public Action<InterpretScriptArgs>? OnInterpretEntityScript { get; set; }
    public List<EnemyEntity> TargetMessages { get; } = new();
    public Action<EnemyEntity>? OnTargetMessage { get; set; }
    public List<(byte Subtype, int A4)> RayAttacks { get; } = new();
    public Func<byte, int, int>? OnFireRayAttack { get; set; }

    internal (int X, int Y, int Z) Muzzle(byte part, EnemyEntity actor) =>
        TransformPartVertex?.Invoke(part, actor)
        ?? throw new NotImplementedException(
            "EnemyWeaponFire.cs: muzzle transform func_80017668 (8005F6E8.c:191) is not part of this headless port");

    internal int TraceMapRay(EnemyEntity self, EnemyEntity target) =>
        OnTraceMapRay?.Invoke(self, target)
        ?? throw new NotImplementedException(
            "EnemyWeaponFire.cs: func_800684AC map ray (8005F6E8.c:328, 398) is not part of this headless port");

    internal int SpawnDirectShot(DirectSpawnArgs args)
    {
        DirectShots.Add(args);
        return OnSpawnDirectShot?.Invoke(args)
            ?? throw new NotImplementedException(
                "EnemyWeaponFire.cs: func_8005E47C direct shot spawn (8005F6E8.c:414) is not part of this headless port");
    }

    internal void PlaySound(int id, int x, int z, int arg)
    {
        if (OnPlaySound is null)
            throw new NotImplementedException(
                "EnemyWeaponFire.cs: func_80047284 positional sound (8005F6E8.c:452) is not part of this headless port");
        SoundsPlayed.Add((id, x, z, arg));
        OnPlaySound(id, x, z, arg);
    }

    internal void InterpretEntityScript(InterpretScriptArgs args)
    {
        if (OnInterpretEntityScript is null)
            throw new NotImplementedException(
                "EnemyAttackScheduler.cs: func_80069D68 script interpreter (80060238.c:262, 800600E8.c:96) is not part of this headless port");
        OnInterpretEntityScript(args);
    }
}
