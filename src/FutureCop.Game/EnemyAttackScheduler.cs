using FutureCop.Game.Mech;

namespace FutureCop.Game;

/// <summary>
/// Port of the retail enemy attack scheduler cluster:
/// func_80060238 gStepAttackScheduler (80060238.c:146-287),
/// func_800600E8 gAcquireAttackTarget (800600E8.c:67-106),
/// func_8004E5F0 gCheckTargetRangeAndCone (8004E5F0.c:64-145),
/// func_8004E80C gFindTargetEntity (8004E80C.c:91-209),
/// func_8005FFB8 gApplyAttackCommands (8005FFB8.c:61-108),
/// func_8002A5B4 gRemoveEntityCommand (8002A5B4.c:40-58),
/// func_8005FF70 gFindEffectKeyIndex (8005FF70.c:32-42),
/// func_8006062C gCosineClamped (8006062C.c:31-41) and
/// func_80060668 gInitAttackSchedulerSlot (80060668.c:77-107, asm main.s:93819-93905).
/// MIPS semantics are preserved: 32-bit wraparound in unchecked code, FMUL/FMULU as
/// bits 16..47 of the product (MechMath.Fmul/FmulU), s16 masks on the frame fields.
/// </summary>
public static class EnemyAttackScheduler
{
    /// <summary>
    /// func_8005FF70 gFindEffectKeyIndex: linear search over D_800A2300 bounded by the
    /// D_8009F7F8 count. C: 8005FF70.c:32-42; asm main.s:93308-93328 (search, -1 tail).
    /// </summary>
    public static int FindEffectKeyIndex(AttackDefinitionTable table, int key)
    {
        for (int i = 0; i < table.Count; i++)
        {
            if (table.Entries[i].Key == key)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// func_8006062C gCosineClamped: cosine of a 4096-step angle, with a u32 compare
    /// rejecting every id &gt;= 0x800 (half a turn, and thus also every wrapped
    /// negative s16) as -0x10000. C: 8006062C.c:31-41; asm main.s:93797-93815.
    /// </summary>
    public static int CosineClamped(int angle)
    {
        if (unchecked((uint)angle) >= 0x800)
            return unchecked((int)0xFFFF0000);
        return MechMath.SinCos(angle).Cos;
    }

    /// <summary>
    /// func_80060668 gInitAttackSchedulerSlot. C: 80060668.c:77-107; writes verified
    /// against asm main.s:93819-93905. A negative f00 clears only the definition
    /// pointer (asm 93829-93830). The rate scale (f0E &lt;&lt; 12) / 218 compiles to a
    /// multiply by 0x964FDA6D with the sign fix-up (asm 93872-93884). An unknown key
    /// makes the retail code form &amp;D_800A2300[-1]; the port throws instead of
    /// performing the wild access.
    /// </summary>
    public static void InitSlot(AttackSlot slot, AttackDescriptorConfig config, AttackDefinitionTable table)
    {
        if (config.Key < 0)
        {
            slot.Definition = null;                        // asm 93829-93830
            return;
        }
        int index = FindEffectKeyIndex(table, config.Key);
        if (index < 0)
            throw new IndexOutOfRangeException(
                $"func_80060668/8005FF70 would index D_800A2300[-1] for key {config.Key} (80060668.c:87 via 8005FF70.c:41)");
        AttackDefinition entry = table.Entries[index];
        slot.Definition = entry;                           // asm 93832-93840
        slot.Flags = config.FlagsF02;                     // asm 93841-93843 (lhu/sh 0x4)
        slot.TargetClass06 = config.TargetClassF05;       // asm 93844-93846 (lbu 0x5/sb 0x6)
        slot.Value08 = config.RangeF06;                   // asm 93847-93849 (lhu 0x6/sh 0x8)
        slot.Mode07 = unchecked((byte)config.SearchF04);   // asm 93850-93852 (lbu 0x4/sb 0x7)
        slot.HalfAngle0C = CosineClamped(config.AngleF08 >> 1); // asm 93853-93861 (s16/2 -> 8006062C)
        slot.HalfAngle10 = CosineClamped(config.AngleF0A >> 1); // asm 93862-93870
        slot.Range0A = unchecked((short)config.Field0C);  // asm 93871-93873 (lhu 0xC/sh 0xA)
        slot.RetryReload14 = unchecked((short)((config.RateF0E << 12) / 218)); // asm 93874-93885
        slot.Retry16 = 0;                                 // asm 93879 slot: (sh zero into +0x14/+0x16 span)
        slot.Target18 = null;                             // asm 93888
        slot.Target18Value = 0;                            // asm 93889
        slot.Playing22 = 0;                                // asm 93890
        slot.Toggle23 = 0;                                // asm 93891
        slot.AmmoGate20 = unchecked((short)entry.W02);    // asm 93886-93892 (entry f02 -> +0x20)
        slot.FrameAcc24 = unchecked((ushort)(short)entry.W0A); // asm 93893-93895 (entry f0A -> +0x24)
        slot.Burst26 = entry.W06;                             // asm 93896-93898 (entry f06 -> +0x26)
    }

    /// <summary>
    /// func_8002A5B4 gRemoveEntityCommand: compacts the 4-slot command queue at
    /// object +0x0C and clears the trailing opcode. C: 8002A5B4.c:40-58.
    /// </summary>
    public static void RemoveEntityCommand(AttackCommandQueue q, int index)
    {
        int i = index + 1;
        if (i < 4)
        {
            do
            {
                q.Ops[i - 1] = q.Ops[i];                   // 8002A5B4.c:52-54
                q.Args[i - 1] = q.Args[i];
                i++;
            } while (i < 4);
        }
        q.Ops[3] = 0;                                     // 8002A5B4.c:58
    }

    /// <summary>
    /// func_8005FFB8 gApplyAttackCommands: walks up to four (op, arg) pairs of the
    /// owner's queue, stops at opcode 0, handles 0x1E (flag 0x100), 0x1F..0x23
    /// (class/value) and 0x26 (definition switch), and removes every consumed slot
    /// without advancing. C: 8005FFB8.c:61-108.
    /// </summary>
    public static void ApplyCommands(AttackSlot slot, AttackCommandQueue script, AttackDefinitionTable table)
    {
        int i = 0;
        while (i < 4)
        {
            int op = script.Ops[i];
            if (op == 0)
                break;                                    // 8005FFB8.c:75-77
            int arg = script.Args[i];
            switch (op)
            {
                case 0x1F:
                case 0x20:
                case 0x21:
                case 0x22:
                case 0x23:
                    slot.TargetClass06 = (byte)(op - 0x1E); // 8005FFB8.c:80-87
                    slot.Value08 = (ushort)arg;
                    break;
                case 0x1E:
                    if (arg == 0)
                        slot.Flags |= 0x100;               // 8005FFB8.c:88-91
                    else
                        slot.Flags &= unchecked((ushort)~0x100); // 8005FFB8.c:92-94
                    break;
                case 0x26:
                    int idx = FindEffectKeyIndex(table, arg); // 8005FFB8.c:95-101
                    if (idx >= 0 && idx < table.Entries.Length)
                    {
                        slot.Definition = table.Entries[idx];
                        slot.AmmoGate20 = unchecked((short)slot.Definition.W02);
                    }
                    break;
                default:
                    i++;                                   // 8005FFB8.c:102-105: skip and advance
                    continue;
            }
            RemoveEntityCommand(script, i);                // 8005FFB8.c:107
        }
    }

    /// <summary>
    /// func_80060238 gStepAttackScheduler. C: 80060238.c:146-287. Per-frame tick of one
    /// attack descriptor: budget gate, command merges, enable gates, timeline with or
    /// without repeats, retry timer, cached-target validation, spawn script gate,
    /// acquire gate, and the fire call whose low byte becomes the return value.
    /// </summary>
    public static int Step(AttackSlot slot, int frameDelta, EnemyEntity owner,
        EnemyEntity? second, int fireFlag, EnemyWorld env)
    {
        unchecked
        {
            if (env.FreeSlots < 10)
                return 0;                                  // 80060238.c:156-158
            if (slot.Definition is null)
                return 0;                                  // 80060238.c:159-161
            if (second is not null && second.Queue.Command0 != 0)
                ApplyCommands(slot, second.Queue, env.Table); // 80060238.c:162-163
            if (owner.Queue.Command0 != 0)
                ApplyCommands(slot, owner.Queue, env.Table);  // 80060238.c:165-167
            if (slot.Definition.Type == 0)
                return 0;                                  // 80060238.c:169-171
            if ((slot.Flags & 0x100) != 0)
                return 0;                                  // 80060238.c:172-174
            if (slot.AmmoGate20 == 0)
                return 0;                                  // 80060238.c:175-177
            if (slot.Playing22 == 0)
            {
                // Timeline (80060238.c:182-215).
                if ((short)slot.Burst26 > 0)
                {
                    // With repeats (80060238.c:182-199).
                    slot.FrameAcc24 = (ushort)(slot.FrameAcc24 + frameDelta);
                    if ((short)slot.FrameAcc24 < slot.Definition.W0A)
                        goto TimelineDone;                 // 80060238.c:184-186
                    if ((slot.Flags & 0x20) != 0)
                        slot.Toggle23 = (byte)(1 - slot.Toggle23); // 80060238.c:187-189
                    slot.Burst26 = (ushort)(slot.Burst26 - 1);
                    int step = slot.Definition.W0A;         // 80060238.c:191-195
                    if (step < 0)
                    {
                        step = -step;
                        step = (step >> 1) + (int)(env.Random() % (uint)step);
                    }
                    slot.Playing22 = 1;                     // 80060238.c:196
                    slot.FrameAcc24 = (ushort)(slot.FrameAcc24 - step);
                }
                else
                {
                    // Without repeats (80060238.c:201-215).
                    slot.FrameAcc24 = (ushort)(slot.FrameAcc24 + frameDelta);
                    if ((short)slot.FrameAcc24 < slot.Definition.W08)
                        goto TimelineDone;                 // 80060238.c:202-204
                    int step = slot.Definition.W08;         // 80060238.c:205-209
                    if (step < 0)
                    {
                        step = -step;
                        step = (step >> 1) + (int)(env.Random() % (uint)step);
                    }
                    slot.FrameAcc24 = (ushort)(slot.FrameAcc24 - step);
                    if (slot.Definition.W0A < (short)slot.FrameAcc24)
                        slot.FrameAcc24 = (ushort)slot.Definition.W0A; // 80060238.c:211-213
                    slot.Burst26 = slot.Definition.W06;     // 80060238.c:214
                    // No unk22 write on this path: C 80060238.c:201-215 contains none, so the
                    // slot falls through L60468 with unk22 == 0 and returns (the repeats
                    // branch at 182-199 is the only place that arms unk22).
                }

            TimelineDone:
                // Retry timer while not playing (80060238.c:217-225).
                if (slot.Playing22 == 0)
                {
                    if ((slot.Flags & 0x4000) == 0)
                        return 0;
                    slot.Retry16 = (short)(slot.Retry16 - frameDelta);
                    if (slot.Retry16 < 0)
                        slot.Retry16 = 0;
                    return 0;
                }
            }

            // Target block (80060238.c:229-262, entered via L604B0).
            if (slot.TargetClass06 != 0)
            {
                EnemyEntity saved = slot.Target18!;
                if (saved is not null)
                {
                    if (saved.Id != slot.Target18Value)
                        slot.Target18 = null;               // 80060238.c:236-238
                    if (slot.Target18 is not null &&
                        CheckRangeAndCone(owner, saved, slot.Range0A << 4, slot.HalfAngle10) < 0)
                        slot.Target18 = null;               // 80060238.c:240-243
                }
                if (saved is not null && slot.Target18 is null
                    && (slot.Flags & 0x8000) != 0
                    && owner.SpawnBlock is { Active: not 0 }
                    && (owner.FlagWord24 & 0x100000) == 0)
                {
                    env.InterpretEntityScript(new InterpretScriptArgs( // 80060238.c:250-262
                        owner, owner.State1C < 0 ? 1 : 0, 0));
                }

                // A cached target without 0x4000 fires directly; otherwise retail tries
                // acquisition even when the flag is clear (L605A8).
                if (slot.Target18 is not null && (slot.Flags & 0x4000) == 0)
                    goto Fire;
                if ((byte)Acquire(slot, frameDelta, owner, env) == 0 &&
                    ((slot.Flags & 0x4000) == 0 || slot.Target18 is null))
                    return 0;
            }

        Fire:
            // 80060238.c:285-286 (L605EC): the fire result is truncated to a byte.
            return EnemyWeaponFire.FireUnchecked(slot, owner, second, fireFlag, env);
        }
    }

    /// <summary>
    /// func_800600E8 gAcquireAttackTarget. C: 800600E8.c:67-106. Decrements the retry
    /// timer, searches func_8004E80C when it expires, runs the on-acquire script and
    /// caches the target and its id. Returns 1 on a valid (re)acquisition.
    /// </summary>
    public static int Acquire(AttackSlot slot, int frameDelta, EnemyEntity ctx, EnemyWorld env)
    {
        EnemyEntity? found;
        slot.Retry16 = unchecked((short)(slot.Retry16 - frameDelta)); // 800600E8.c:71
        if (slot.Retry16 < 0)
        {
            slot.Retry16 = slot.RetryReload14;                       // 800600E8.c:73
            found = FindTargetEntity(ctx, env.Entities.ToArray(),
                slot.TargetClass06, slot.Value08, slot.Mode07,
                slot.Range0A << 4, slot.HalfAngle0C,
                (slot.Flags >> 6) & 1, () => (int)env.Random());      // 800600E8.c:74-76
            if (found is not null)
                goto FoundNonNull;
        }

        if ((slot.Flags & 0x4000) != 0)
            return 0;                                                 // 800600E8.c:82-85
        slot.Target18 = null;                                         // 800600E8.c:86
        return 0;

    FoundNonNull:
        if ((slot.Flags & 0x4000) != 0 && ReferenceEquals(found, slot.Target18) &&
            found!.Id == slot.Target18Value)
            return 1;                                                 // 800600E8.c:90-93
        if (ctx.SpawnBlock is { Active: not 0 } && (ctx.FlagWord24 & 0x100000) == 0)
        {
            env.InterpretEntityScript(new InterpretScriptArgs(        // 800600E8.c:94-98
                ctx, ctx.State1C < 0 ? 1 : 0, found!.Team14 & 0x3FFF));
        }
        slot.Target18 = found;                                        // 800600E8.c:99
        slot.Target18Value = found!.Id;                               // 800600E8.c:100-104
        return 1;
    }

    /// <summary>
    /// func_8004E5F0 gCheckTargetRangeAndCone. C: 8004E5F0.c:64-145. Range box over
    /// x/z, FMUL square distance, gSqrt12 root (table D_8009DF20 via MechMath),
    /// distance cap, optional facing cone over the heading selected by the type byte,
    /// with the FMULU projection against 0x80000000 / (dist &gt;&gt; 1).
    /// threshold &lt;= 0xFFFF0000 disables the cone test.
    /// </summary>
    public static int CheckRangeAndCone(EnemyEntity src, EnemyEntity tgt, int maxDist, int threshold)
    {
        unchecked
        {
            int dx = tgt.PositionX - src.PositionX;   // 8004E5F0.c:76
            if (maxDist < dx) return -1;
            if (dx < -maxDist) return -1;              // 8004E5F0.c:77-82
            int dz = tgt.PositionZ - src.PositionZ;   // 8004E5F0.c:83
            if (maxDist < dz) return -1;
            if (dz < -maxDist) return -1;             // 8004E5F0.c:84-89

            int len = MechMath.Sqrt12(MechMath.Fmul(dx, dx) + MechMath.Fmul(dz, dz)); // 8004E5F0.c:91
            int dist = len << 2;                       // 8004E5F0.c:92
            if (maxDist <= dist) return -1;           // 8004E5F0.c:93-95
            if (threshold <= unchecked((int)0xFFFF0000)) return dist; // 8004E5F0.c:96-98
            if (dist < 9) return dist;                 // 8004E5F0.c:99-101

            int vx, vy;
            if (src.Mode16 == 1)                        // 8004E5F0.c:103-109
            {
                vx = src.AltHeadingX;
                vy = src.AltHeadingZ;
            }
            else
            {
                vx = src.HeadingX;
                vy = src.HeadingZ;
            }
            int num = MechMath.Fmul(vx, dx) + MechMath.Fmul(vy, dz); // 8004E5F0.c:110

            int val;
            if (num > 0)
            {
                if (dist > 0)
                {
                    int den = dist >> 1;
                    val = MechMath.FmulU(num, (int)(0x80000000u / (uint)den));   // 8004E5F0.c:112-117
                }
                else
                {
                    int den = -dist;
                    den >>= 1;
                    val = -MechMath.FmulU(num, (int)(0x80000000u / (uint)den)); // 8004E5F0.c:118-123
                }
            }
            else
            {
                if (dist > 0)
                {
                    int den = dist >> 1;
                    num = -num;
                    val = -MechMath.FmulU(num, (int)(0x80000000u / (uint)den)); // 8004E5F0.c:125-131
                }
                else
                {
                    num = -num;
                    int den = -dist;
                    den >>= 1;
                    val = MechMath.FmulU(num, (int)(0x80000000u / (uint)den)); // 8004E5F0.c:132-138
                }
            }

            if (threshold <= val) return dist;          // 8004E5F0.c:142-144
            return -1;                                   // 8004E5F0.c:145
        }
    }

    /// <summary>
    /// func_8004E80C gFindTargetEntity: picks a candidate out of the global table by
    /// class filter, range/facing and one of the strategies. C: 8004E80C.c:91-209.
    /// mode 0 first survivor, 1 nearest, 3 nearest with line of sight, 2 random with
    /// the retail quirk that the second pass re-tests only the class filter.
    /// The maxDist doubles as the initial best score (8004E80C.c:109).
    /// </summary>
    public static EnemyEntity? FindTargetEntity(EnemyEntity ctx, EnemyEntity[] table,
        int filterType, int filterValue, int mode, int maxDist, int threshold,
        int priorityFlag, Func<int> random)
    {
        unchecked
        {
            if (mode == 6)
                return ctx;                              // 8004E80C.c:103-105
            int cnt = 0;
            EnemyEntity? best = null;
            int ctxId = ctx.Id;
            int bestVal = maxDist;                        // 8004E80C.c:106-109
            int prio = priorityFlag != 0 ? -1 : 0;
            for (int i = 0; i < table.Length; i++)        // 8004E80C.c:113
            {
                EnemyEntity o = table[i];
                if (o.Id <= 0) continue;                  // 8004E80C.c:115
                if (o.Id == ctxId) continue;              // 8004E80C.c:116
                if (ReferenceEquals(o, ctx)) continue;    // 8004E80C.c:117
                if ((o.FlagWord24 & 1) != 0) continue;     // 8004E80C.c:118
                if (ctx.Mode16 == 1 && (o.FlagWord24 & 0x20000) != 0) continue; // 8004E80C.c:119
                if (!ClassFilter(o, filterType, filterValue)) continue;        // 8004E80C.c:120-141
                switch (mode)
                {
                    case 0:
                        return o;                         // 8004E80C.c:143-144
                    case 1:
                    case 3:
                        if (priorityFlag != 0 && (o.Prio19 & 0xF) < prio) continue; // 8004E80C.c:147-149
                        int r = CheckRangeAndCone(ctx, o, maxDist, threshold);
                        if (r < 0) continue;               // 8004E80C.c:150-151
                        if (r >= bestVal && (priorityFlag == 0 || !(prio < (o.Prio19 & 0xF))))
                            continue;                     // 8004E80C.c:152-155
                        if (mode == 3 && !ctx.HasLineOfSightTo(o)) continue;   // 8004E80C.c:156-158
                        bestVal = r;                       // 8004E80C.c:159-160
                        best = o;
                        if (priorityFlag != 0)
                            prio = o.Prio19 & 0xF;        // 8004E80C.c:161-163
                        break;
                    case 2:
                        cnt++;                            // 8004E80C.c:165-167
                        break;
                    default:
                        return null;                     // 8004E80C.c:168-169
                }
            }
            if (mode != 2)
                return best;                              // 8004E80C.c:172-174
            if (cnt > 0)
            {
                cnt = random() % cnt;                     // 8004E80C.c:176
                for (int i = 0; i < table.Length; i++)   // 8004E80C.c:177
                {
                    EnemyEntity o = table[i];
                    if (o.Id <= 0) continue;              // 8004E80C.c:179
                    if (o.Id == ctxId) continue;          // 8004E80C.c:180
                    if (!ClassFilter(o, filterType, filterValue)) continue;    // 8004E80C.c:181-202
                    cnt--;                                // 8004E80C.c:203
                    if (cnt < 0)
                        return o;                         // 8004E80C.c:204-205
                }
            }
            return null;                                  // 8004E80C.c:208-209
        }
    }

    private static bool ClassFilter(EnemyEntity o, int type, int val)
    {
        return type switch
        {
            2 => o.Team14 == (ushort)val,                  // 8004E80C.c:121-123
            1 => o.Mode16 == (byte)val,                    // 8004E80C.c:124-126
            3 => o.Type17 == (byte)val,                    // 8004E80C.c:127-129 (+0x17)
            4 => o.Type18 == val,                          // 8004E80C.c:130-132 (both bytes)
            5 => (1 << o.Type18 & val) != 0,               // 8004E80C.c:133-135
            7 => (o.Team14 & 0x4000) != 0,                 // 8004E80C.c:136-138
            _ => false,                                    // default aborts the search upstream; unreachable
        };
    }
}
