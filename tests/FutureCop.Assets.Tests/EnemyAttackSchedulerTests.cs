using FutureCop.Game;
using FutureCop.Game.Mech;
using Xunit;

namespace FutureCop.Assets.Tests;

public class EnemyAttackSchedulerTests
{
    private static AttackDefinition Def(byte type = 1, ushort repeat = 0, short dur08 = 0, short dur0A = 0)
        => new() { Key = 0x10, Type = type, W02 = 0x0800, W06 = repeat, W08 = dur08, W0A = dur0A };

    private static AttackCommandQueue Queue(params (byte Op, byte Arg)[] commands)
    {
        var q = new AttackCommandQueue();
        for (int i = 0; i < commands.Length; i++)
        {
            q.Ops[i] = commands[i].Op;
            q.Args[i] = commands[i].Arg;
        }
        return q;
    }

    private static void ArmFirePath(EnemyWorld env, Func<DirectSpawnArgs, int>? shot = null)
    {
        env.Random = () => 0;
        env.TransformPartVertex = (_, _) => (0, 0, 0);
        env.OnPlaySound = (_, _, _, _) => { };
        env.OnSpawnDirectShot = shot ?? (_ => 1);
        env.OnTraceMapRay = (_, _) => 0;
    }

    // func_80060238:156-158 (budget gate), 8004E80C.c:21-25.
    [Fact]
    public void BudgetGateBlocksSchedulingWithFewerThanTenFreeSlots()
    {
        var blocked = new EnemyWorld(20, 25); // free = 5 < 10
        var owner = blocked.NewOwner();
        owner.Queue = Queue((0x21, 0x42));
        var slot = new AttackSlot { Definition = Def(), AmmoGate20 = 1 };
        Assert.Equal(0, EnemyAttackScheduler.Step(slot, 1, owner, null, 1, blocked));
        Assert.Equal((byte)0x21, owner.Queue.Ops[0]); // merges never ran

        var open = new EnemyWorld(20, 30); // free = 10
        ArmFirePath(open);
        var slot2 = new AttackSlot { Definition = Def(type: 7), AmmoGate20 = 1, Playing22 = 1 };
        Assert.Equal(1, EnemyAttackScheduler.Step(slot2, 1, open.NewOwner(), null, 1, open));
    }

    // func_80060238:159-161 (missing definition), 169-171 (type byte 0),
    // 172-174 (flag 0x100), 175-177 (zero gate byte at +0x20).
    [Theory]
    [InlineData(false, (ushort)0, (short)1)]
    [InlineData(true, (ushort)0, (short)1)]
    [InlineData(true, (ushort)0x100, (short)1)]
    [InlineData(true, (ushort)0, (short)0)]
    public void DisabledSlotsReturnZero(bool hasDefinition, ushort flags, short gate)
    {
        var env = new EnemyWorld(0, 100);
        ArmFirePath(env);
        var slot = new AttackSlot
        {
            Definition = hasDefinition ? Def() : null,
            Flags = flags,
            AmmoGate20 = gate,
        };
        Assert.Equal(0, EnemyAttackScheduler.Step(slot, 1, env.NewOwner(), null, 1, env));
    }

    // func_80060668 (80060668.c:77-107, asm main.s:93819-93905) and func_8005FF70.
    [Fact]
    public void InitSlotPopulatesEveryFieldFromConfigAndDefinitionTable()
    {
        var table = new AttackDefinitionTable(new[]
        {
            new AttackDefinition { Key = 0x10, W02 = 0x0800, W06 = 0x0401, W0A = 0x1000 },
            new AttackDefinition { Key = 0x11, W02 = 0xCAFE, W06 = 0x0102, W0A = 0x0203 },
        });
        var slot = new AttackSlot { Flags = 0x7777, Value08 = 0x9999, AmmoGate20 = unchecked((short)0x9999) };
        EnemyAttackScheduler.InitSlot(slot, new AttackDescriptorConfig { Key = -1 }, table);
        Assert.Null(slot.Definition);                 // asm 93829-93830: only +0x00 cleared
        Assert.Equal((ushort)0x7777, slot.Flags);     // everything else untouched
        Assert.Equal((ushort)0x9999, slot.Value08);

        EnemyAttackScheduler.InitSlot(slot, new AttackDescriptorConfig
        {
            Key = 0x10,
            FlagsF02 = 0x08,
            SearchF04 = 66,
            TargetClassF05 = 2,
            RangeF06 = 0x0401,
            AngleF08 = 0x00,
            AngleF0A = 0x3F,
            Field0C = 0x1C,
            RateF0E = 0x1800,
        }, table);
        Assert.Same(table.Entries[0], slot.Definition);
        Assert.Equal((ushort)0x08, slot.Flags);         // asm 93841-93843
        Assert.Equal((byte)2, slot.TargetClass06);     // asm 93844-93846
        Assert.Equal((ushort)0x0401, slot.Value08);     // asm 93847-93849
        Assert.Equal((byte)66, slot.Mode07);           // asm 93850-93852
        Assert.Equal(EnemyAttackScheduler.CosineClamped(0), slot.HalfAngle0C);        // asm 93853-93861
        Assert.Equal(EnemyAttackScheduler.CosineClamped(0x3F / 2), slot.HalfAngle10); // asm 93862-93870
        Assert.Equal((short)0x1C, slot.Range0A);       // asm 93871-93873
        Assert.Equal(unchecked((short)((0x1800 << 12) / 218)), slot.RetryReload14); // asm 93874-93885
        Assert.Equal((short)0, slot.Retry16);
        Assert.Null(slot.Target18);
        Assert.Equal(0, slot.Target18Value);
        Assert.Equal((short)0x0800, slot.AmmoGate20);   // asm 93886-93892 (entry W02)
        Assert.Equal((byte)0, slot.Playing22);         // asm 93890
        Assert.Equal((byte)0, slot.Toggle23);          // asm 93891
        Assert.Equal((ushort)0x1000, slot.FrameAcc24); // asm 93893-93895 (entry W0A)
        Assert.Equal((ushort)0x0401, slot.Burst26);    // asm 93896-93898 (entry W06)
    }

    // func_8006062C gCosineClamped (8006062C.c:31-41): halved negative s16 -> -0x10000.
    [Theory]
    [InlineData(0, 0x10000)]
    [InlineData(0x400, 0)]        // 90 degrees: cosine 0
    [InlineData(0x7FF, unchecked((int)0xFFFF0000))] // just under 180 degrees: table-exact value -0x10000
    [InlineData(0x800, unchecked((int)0xFFFF0000))] // unsigned reject
    [InlineData(unchecked((int)0xFFFFF000), unchecked((int)0xFFFF0000))] // wrapped negative s16
    [InlineData(unchecked((int)0xFFFFF801), unchecked((int)0xFFFF0000))] // -2047 wrapped
    public void CosineClampedRejectsHalfTurnAndBeyond(int angle, int expected)
    {
        Assert.Equal(expected, EnemyAttackScheduler.CosineClamped(angle));
    }

    // func_8005FF70: unknown key -> -1; func_80060668:87 forms table[-1] (wild access).
    [Fact]
    public void InitSlotWithUnknownKeyMatchesRetailOutOfBoundsCondition()
    {
        var table = new AttackDefinitionTable(new[] { new AttackDefinition { Key = 0x10 } });
        Assert.Throws<IndexOutOfRangeException>(
            () => EnemyAttackScheduler.InitSlot(new AttackSlot(), new AttackDescriptorConfig { Key = 0x99 }, table));
    }

    // func_8005FFB8 (8005FFB8.c:61-108) and func_8002A5B4 (8002A5B4.c:40-58).
    [Fact]
    public void ApplyAttackCommandsConsumesHandledSlotsAndShiftsQueue()
    {
        var table = new AttackDefinitionTable(new[]
        {
            new AttackDefinition { Key = 0x10, W02 = 0xBEEF },
            new AttackDefinition { Key = 0x11, W02 = 0xCAFE },
        });
        var slot = new AttackSlot { Definition = table.Entries[1], Flags = 0 };
        var q = Queue(
            (0x30, 0xAA),   // unhandled: p += 2, i++ (8005FFB8.c:102-105)
            (0x21, 0x37),   // handled: class slot 3 (8005FFB8.c:80-87)
            (0x1E, 0x00),   // handled: flag 0x100 set (8005FFB8.c:88-91)
            (0x26, 0x10));  // handled: definition switch to key 0x10 (8005FFB8.c:95-101)
        EnemyAttackScheduler.ApplyCommands(slot, q, table);
        Assert.Equal((byte)3, slot.TargetClass06);
        Assert.Equal((ushort)0x37, slot.Value08);
        Assert.Equal((ushort)0x100, slot.Flags & 0x100);
        Assert.Same(table.Entries[0], slot.Definition);
        Assert.Equal(unchecked((short)0xBEEF), slot.AmmoGate20);      // 0x26 copies entry W02 into the gate
        Assert.Equal(new byte[] { 0x30, 0, 0, 0 }, q.Ops); // handled slots removed, 0x30 at head
    }

    [Fact]
    public void ApplyAttackCommandsStopsAtOpcodeZeroAndHonoursTableCount()
    {
        var table = new AttackDefinitionTable(new[]
        {
            new AttackDefinition { Key = 0x0A, W02 = 0x1111 },
            new AttackDefinition { Key = 0x0B, W02 = 0x2222 },
        })
        { Count = 1 }; // D_8009F7F8 bounds the linear search (8005FF70.c:36)
        var slot = new AttackSlot { Definition = table.Entries[0], Flags = 0, AmmoGate20 = 0x3333 };
        var q = Queue((0x1F, 0x05), (0x00, 0x00), (0x1F, 0x09), (0x26, 0x0B));
        EnemyAttackScheduler.ApplyCommands(slot, q, table);
        Assert.Equal((byte)1, slot.TargetClass06);
        Assert.Equal((ushort)0x05, slot.Value08);
        Assert.Equal((byte)0, q.Ops[0]);   // slot 0 consumed, following slots shifted down
        Assert.Equal((byte)0x1F, q.Ops[1]); // (0x1F, 9) shifted into slot 1, not yet consumed
        Assert.Equal((byte)0x09, q.Args[1]);
        Assert.Equal((byte)0, q.Ops[3]);   // trailing opcode cleared (8002A5B4.c:58)

        // key 0x0B lives beyond the active count -> idx -1 -> definition and gate unchanged.
        var slot2 = new AttackSlot { Definition = table.Entries[0], Flags = 0, AmmoGate20 = 0x3333 };
        EnemyAttackScheduler.ApplyCommands(slot2, Queue((0x26, 0x0B)), table);
        Assert.Same(table.Entries[0], slot2.Definition);
        Assert.Equal((short)0x3333, slot2.AmmoGate20);
    }

    // func_80060238:162-167 (second then primary owner merges).
    [Fact]
    public void StepMergesCommandsFromBothOwnersBeforeTheGates()
    {
        var env = new EnemyWorld(0, 100);
        var slot = new AttackSlot { Definition = Def(), AmmoGate20 = 1 };
        var second = env.NewOwner();
        second.Queue = Queue((0x21, 0x42)); // class 3, value 0x42
        var primary = env.NewOwner();
        primary.Queue = Queue((0x22, 0x77)); // class 4, value 0x77 (0x22 - 0x1E)
        int ret = EnemyAttackScheduler.Step(slot, 1, primary, second, 1, env);
        Assert.Equal((byte)4, slot.TargetClass06);
        Assert.Equal((ushort)0x77, slot.Value08);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, second.Queue.Ops);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, primary.Queue.Ops);
        Assert.Equal(0, ret); // flags lack 0x4000: the retry gate returns 0 (80060238.c:217-225)
    }

    // func_80060238:182-199 (repeating timeline expiry, flag 0x20 toggle).
    [Fact]
    public void TimelineExpiryWithRepeatsTogglesPlaysAndConsumesOneRepeat()
    {
        var env = new EnemyWorld(0, 100);
        ArmFirePath(env, _ => 0); // spawn failed, but the state advance is identical
        var slot = new AttackSlot
        {
            Definition = Def(type: 1, repeat: 2, dur08: 10, dur0A: 20),
            Flags = 0x20,
            AmmoGate20 = 1,
            FrameAcc24 = 18,
            Burst26 = 1,
        };
        int ret = EnemyAttackScheduler.Step(slot, 2, env.NewOwner(), null, 1, env);
        // unk06 == 0 -> L605EC fires in the same tick; the fire tail clears unk22
        // again (8005F6E8.c:426) and returns 1 regardless of the spawn result.
        Assert.Equal((byte)0, slot.Playing22);
        Assert.Equal((byte)1, slot.Toggle23);      // 1 - 0 (80060238.c:187-189)
        Assert.Equal((ushort)0, slot.Burst26);   // 80060238.c:190
        Assert.Equal((ushort)0, slot.FrameAcc24);  // 18 + 2 - 20 (80060238.c:183-197)
        Assert.Equal((byte)1, (byte)ret);           // fire tail return
    }

    // func_80060238:201-215 (single-shot timeline: subtract, clamp, reload).
    [Fact]
    public void TimelineWithoutRepeatsSubtractsClampsAndReloads()
    {
        var env = new EnemyWorld(0, 100);
        ArmFirePath(env);
        var slot = new AttackSlot
        {
            Definition = Def(repeat: 3, dur08: 10, dur0A: 20),
            Flags = 0x4000,
            AmmoGate20 = 1,
            FrameAcc24 = 25,
            Burst26 = 0,
            Retry16 = 5,
        };
        int ret = EnemyAttackScheduler.Step(slot, 2, env.NewOwner(), null, 1, env);
        Assert.Equal((ushort)17, slot.FrameAcc24); // 25 + 2 - 10 (80060238.c:201-210)
        Assert.Equal((ushort)3, slot.Burst26);   // 80060238.c:214
        // Retail does NOT set unk22 on this branch (80060238.c:201-215): the slot stays idle,
        // L60468 runs the retry countdown and the function returns 0 without firing.
        Assert.Equal((byte)0, slot.Playing22);
        Assert.Equal((short)3, slot.Retry16);     // 5 - 2 = 3 (80060238.c:221-224)
        Assert.Equal(0, ret);
    }

    // func_80060238:216-226 (retry timer countdown with zero clamp, requires 0x4000).
    [Theory]
    [InlineData((short)3, (short)0)]
    [InlineData((short)10, (short)0)]
    [InlineData((short)5, (short)0)]
    public void RetryTimerClampsAtZeroWhenNotPlaying(short initial, short _)
    {
        var env = new EnemyWorld(0, 100);
        var slot = new AttackSlot
        {
            Definition = Def(dur08: 100),
            Flags = 0x4000,
            AmmoGate20 = 1,
            Playing22 = 0,
            Burst26 = 0,
            Retry16 = initial,
        };
        Assert.Equal(0, EnemyAttackScheduler.Step(slot, 10, env.NewOwner(), null, 1, env));
        Assert.Equal((short)Math.Max(0, initial - 10), slot.Retry16);
        Assert.Equal((byte)0, slot.Playing22);

        // without 0x4000 the retry path is skipped entirely
        slot.Retry16 = 5;
        slot.Flags = 0;
        Assert.Equal(0, EnemyAttackScheduler.Step(slot, 10, env.NewOwner(), null, 1, env));
        Assert.Equal((short)5, slot.Retry16);
    }

    // func_80060238:229-262 (cached target validation, 0x8000 spawn script gates).
    [Fact]
    public void StaleTargetIsDroppedAndSpawnScriptRunsWhenAllowed()
    {
        var env = new EnemyWorld(0, 100);
        var self = env.NewOwner();
        var target = env.NewOwner();
        var scripts = new List<InterpretScriptArgs>();
        env.OnInterpretEntityScript = scripts.Add;
        self.SpawnBlock = new SpawnBlock { Active = 0x01 };

        var slot = new AttackSlot
        {
            Definition = Def(dur08: 100),
            Flags = 0x8000,
            AmmoGate20 = 1,
            Playing22 = 1,
            TargetClass06 = 1,          // filter type 1 ...
            Value08 = 5,                // ... on Mode16 == 5: no candidate matches
            Mode07 = 0,
            Range0A = 0x100,
            HalfAngle10 = -1,
            Retry16 = 0,
        };
        slot.Target18 = target;
        slot.Target18Value = target.Id + 1; // id mismatch -> cleared (80060238.c:236-238)
        int ret = EnemyAttackScheduler.Step(slot, 1, self, null, 1, env);
        Assert.Null(slot.Target18);
        var call = Assert.Single(scripts);  // all four spawn gates passed (80060238.c:250-262)
        Assert.Same(self, call.Ctx);
        Assert.Equal(0, call.Arg1);        // self.State1C >= 0 -> arg 0
        Assert.Equal(0, call.Arg2);
        Assert.Equal(0, ret);              // acquire missed and flags lack 0x4000: L60600
    }

    [Fact]
    public void SpawnScriptIsSkippedWhenAnySpawnGateCloses()
    {
        var env = new EnemyWorld(0, 100);
        var self = env.NewOwner();
        var scripts = new List<InterpretScriptArgs>();
        env.OnInterpretEntityScript = scripts.Add;
        self.SpawnBlock = new SpawnBlock { Active = 0x01 };

        // gate 1: bit 0x100000 of the owner flag word (80060238.c:259-261)
        self.FlagWord24 = 0x100000;
        var slot1 = NewSeekingSlot();
        slot1.Target18 = env.NewOwner();
        slot1.Target18Value = slot1.Target18.Id + 1; // stale -> spawn branch reachable
        EnemyAttackScheduler.Step(slot1, 1, self, null, 1, env);
        Assert.Empty(scripts);

        // gate 2: dead script block at +0x9C (80060238.c:253-258)
        self.FlagWord24 = 0;
        self.SpawnBlock = new SpawnBlock { Active = 0x00 };
        var slot2 = NewSeekingSlot();
        slot2.Target18 = env.NewOwner();
        slot2.Target18Value = slot2.Target18.Id + 1;
        EnemyAttackScheduler.Step(slot2, 1, self, null, 1, env);
        Assert.Empty(scripts);

        // gate 3: missing flag 0x8000 (80060238.c:250-252)
        self.SpawnBlock = new SpawnBlock { Active = 0x01 };
        var slot3 = NewSeekingSlot();
        slot3.Flags = 0; // no 0x8000
        slot3.Target18 = env.NewOwner();
        slot3.Target18Value = slot3.Target18.Id + 1;
        EnemyAttackScheduler.Step(slot3, 1, self, null, 1, env);
        Assert.Empty(scripts);

        AttackSlot NewSeekingSlot() => new()
        {
            Definition = Def(dur08: 100),
            Flags = 0x8000,
            AmmoGate20 = 1,
            Playing22 = 1,
            TargetClass06 = 1,
            Value08 = 5,
            Mode07 = 0,
            Range0A = 0x100,
            HalfAngle10 = -1,
            Retry16 = 0,
        };
    }

    // func_80060238:240-243 (range revalidation of a live cached target).
    [Fact]
    public void LiveTargetOutsideRangeIsDroppedAndTheSearchMissKeepsItNull()
    {
        var env = new EnemyWorld(0, 100);
        var self = env.NewOwner();
        var target = env.NewOwner();
        target.Mode16 = 6;               // not the searched class, but revalidated while cached
        target.PositionX = 0x100000;     // far outside the maxDist box of 0x100 << 4
        var slot = new AttackSlot
        {
            Definition = Def(dur08: 100),
            Flags = 0,
            AmmoGate20 = 1,
            Playing22 = 1,
            TargetClass06 = 2,           // filter type 2: Team14 == Value08
            Value08 = 9,                 // no candidate team matches
            Mode07 = 0,
            Range0A = 0x100,
            HalfAngle10 = -1,
            Retry16 = 0,
        };
        slot.Target18 = target;          // id matches, but far outside the range box
        slot.Target18Value = target.Id;
        int ret = EnemyAttackScheduler.Step(slot, 1, self, null, 1, env);
        Assert.Equal(0, ret);
        Assert.Null(slot.Target18);      // gCheckTargetRangeAndCone returned -1
    }

    // func_80060238:229-230, 285-286 with func_800600E8/8004E80C: full acquire+fire.
    [Fact]
    public void AcquirePathInstallsTargetRunsScriptAndFires()
    {
        var env = new EnemyWorld(0, 100);
        ArmFirePath(env);
        var self = env.NewOwner();
        var candidate = env.NewOwner();
        candidate.Mode16 = 3;            // class filter type 1, val 3 (8004E80C.c:124-126)
        candidate.Team14 = 0x4007;      // script arg2 = 0x4007 & 0x3FFF (800600E8.c:96-97)
        candidate.PositionX = 0x10000;   // well within the maxDist box of 0x1000 << 4
        var scripts = new List<InterpretScriptArgs>();
        env.OnInterpretEntityScript = scripts.Add;
        self.SpawnBlock = new SpawnBlock { Active = 0x01 };

        var slot = new AttackSlot
        {
            Definition = Def(dur08: 100),
            Flags = 0x4000,
            AmmoGate20 = 1,
            Playing22 = 1,
            TargetClass06 = 1,           // filter type 1 (800600E8.c:74)
            Value08 = 3,                 // ...on Mode16 == 3
            Mode07 = 1,                  // search mode 1: nearest (8004E80C.c:145-164)
            Range0A = 0x2000,             // dist 0x10000 < 0x2000 << 4 keeps the candidate
            HalfAngle0C = -1,
            Retry16 = 0,                  // 0 - 1 < 0 triggers the search (800600E8.c:71-73)
            RetryReload14 = 5,
        };
        int ret = EnemyAttackScheduler.Step(slot, 1, self, null, 1, env);
        Assert.Equal(1, ret);
        Assert.Same(candidate, slot.Target18);
        Assert.Equal(candidate.Id, slot.Target18Value);
        Assert.Equal((short)5, slot.Retry16);  // timer reloaded (800600E8.c:73)
        var call = Assert.Single(scripts);
        Assert.Equal(7, call.Arg2);              // 0x4007 & 0x3FFF
        Assert.Equal(0, call.Arg1);             // self.State1C >= 0
        Assert.Same(self, call.Ctx);
        Assert.Equal((short)0, slot.AmmoGate20); // fired: ammo decremented in the tail
    }

    // func_800600E8:71-105 (timer, cleanup, unchanged-target shortcut).
    [Fact]
    public void AcquireTimerCleanupAndUnchangedTargetShortcut()
    {
        var env = new EnemyWorld(0, 100);
        var self = env.NewOwner();
        var stale = env.NewOwner();
        stale.Team14 = 0x4001;

        // timer not expired, no search: the cache clears because 0x4000 is clear
        var slot = new AttackSlot { Flags = 0, Retry16 = 5, Target18 = stale };
        Assert.Equal(0, EnemyAttackScheduler.Acquire(slot, 2, self, env));
        Assert.Equal((short)3, slot.Retry16);
        Assert.Null(slot.Target18);

        slot = new AttackSlot
        {
            Flags = 0x4000,
            TargetClass06 = 2,          // filter type 2: Team14 == Value08
            Value08 = 0x4001,           // stale matches
            Mode07 = 0,                 // first match (8004E80C.c:143-144)
            Range0A = 0x100,
            HalfAngle0C = -1,
            Retry16 = 1,
            RetryReload14 = 9,
        };
        slot.Target18 = stale;
        slot.Target18Value = stale.Id + 1; // not the shortcut yet
        Assert.Equal(1, EnemyAttackScheduler.Acquire(slot, 10, self, env)); // expiry + hit
        Assert.Equal((short)9, slot.Retry16);
        Assert.Same(stale, slot.Target18);
        Assert.Equal(stale.Id, slot.Target18Value);

        // unchanged target under 0x4000 short-circuits with 1 (800600E8.c:90-93)
        Assert.Equal(1, EnemyAttackScheduler.Acquire(slot, 10, self, env));
        Assert.Same(stale, slot.Target18);

        // miss with 0x4000 set keeps the red cache (800600E8.c:82-85)
        var quiet = new AttackSlot
        {
            Flags = 0x4000,
            TargetClass06 = 2,
            Value08 = 0x4242,           // no candidate team matches
            Mode07 = 0,
            Range0A = 0x100,
            HalfAngle0C = -1,
            Retry16 = 0,
            RetryReload14 = 9,
        };
        quiet.Target18 = stale;
        Assert.Equal(0, EnemyAttackScheduler.Acquire(quiet, 5, self, env));
        Assert.Same(stale, quiet.Target18);
    }

    // func_8004E5F0:64-101 (range box, FMUL distance, cap, threshold disable).
    [Theory]
    [InlineData(0, 0, 0x10000, 0, 0x20000, unchecked((int)0xFFFF0000), 0x10000)] // planar = |dx|
    [InlineData(0, 0, 0x30000, 0, 0x20000, unchecked((int)0xFFFF0000), -1)]     // box reject dx
    [InlineData(0, 0, 0, 0x30000, 0x20000, unchecked((int)0xFFFF0000), -1)]     // box reject dz
    [InlineData(0, 0, 0x20000, 0, 0x18000, unchecked((int)0xFFFF0000), -1)]    // distance cap
    [InlineData(0, 0, 0x1000, 0x7000, 0x20000, unchecked((int)0xFFFF0000), 0x7120)] // 3-4-5 spot check
    public void CheckRangeAndConeBoxAndCap(int sx, int sz, int tx, int tz, int maxDist,
        int threshold, int expected)
    {
        var env = new EnemyWorld(0, 0);
        var src = env.NewOwner(); var tgt = env.NewOwner();
        src.PositionX = sx; src.PositionZ = sz;
        tgt.PositionX = tx; tgt.PositionZ = tz;
        Assert.Equal(expected, EnemyAttackScheduler.CheckRangeAndCone(src, tgt, maxDist, threshold));
    }

    // func_8004E5F0:103-145 (heading selection by the type byte, FMULU projection).
    [Theory]
    [InlineData(0, 0x10000, -0x10000, 0x10000, 0x10000)]  // main heading accepts
    [InlineData(0, 0x10000, -0x10000, 0x10001, -1)]        // just above the threshold rejects
    [InlineData(0, -0x10000, 0x10000, -0x10000, 0x10000)] // opposite heading accepts at -0x10000
    [InlineData(0, -0x10000, 0x10000, -0xFFFF, -1)]         // opposite rejects at -0xFFFF
    [InlineData(1, -0x10000, 0x10000, 0x10000, 0x10000)]   // type byte 1 -> alt heading (+0xA0)
    [InlineData(0, -0x10000, 0x10000, 0x10000, -1)]         // mode 0 ignores the alt heading
    public void CheckRangeAndConeUsesSelectedHeading(int mode16, int headX, int altHeadX,
        int threshold, int expected)
    {
        var env = new EnemyWorld(0, 0);
        var src = env.NewOwner(); var tgt = env.NewOwner();
        src.Mode16 = (byte)mode16;
        src.HeadingX = headX; src.HeadingZ = 0;
        src.AltHeadingX = altHeadX; src.AltHeadingZ = 0;
        tgt.PositionX = 0x10000;
        Assert.Equal(expected, EnemyAttackScheduler.CheckRangeAndCone(src, tgt, 0x20000, threshold));
    }

    // func_8004E80C:113-141 (the exclusion battery) and 143-144 (mode 0).
    [Fact]
    public void FindTargetEntityAppliesTheOriginalExclusionRules()
    {
        var env = new EnemyWorld(0, 100);
        var self = env.NewOwner();

        var ok = env.NewOwner(); ok.Mode16 = 3;
        var dead = env.NewOwner(); dead.Mode16 = 3; dead.Id = 0;               // id <= 0
        var sameId = env.NewOwner(); sameId.Mode16 = 3; sameId.Id = self.Id;    // seeker id
        var flagBit0 = env.NewOwner(); flagBit0.Mode16 = 3; flagBit0.FlagWord24 = 1;
        var wrongClass = env.NewOwner(); wrongClass.Mode16 = 4;                 // filter miss

        var table = new[] { dead, sameId, self, flagBit0, wrongClass, ok };
        var found = EnemyAttackScheduler.FindTargetEntity(self, table,
            filterType: 1, filterValue: 3, mode: 0, maxDist: 0x20000,
            threshold: unchecked((int)0xFFFF0000), priorityFlag: 0, random: () => 0);
        Assert.Same(ok, found);
    }

    // func_8004E80C:113-141: the seeker-class extra flag filter of mode 1 c1C.
    [Fact]
    public void FindTargetEntityAppliesTheSeekerClassBitFilter()
    {
        var env = new EnemyWorld(0, 100);
        var self = env.NewOwner();
        self.Mode16 = 1;                          // enables the 0x20000 test (8004E80C.c:119)
        var visible = env.NewOwner(); visible.Mode16 = 3;
        var hidden = env.NewOwner(); hidden.Mode16 = 3; hidden.FlagWord24 = 0x20000;
        var table = new[] { hidden, visible };
        var found = EnemyAttackScheduler.FindTargetEntity(self, table,
            filterType: 1, filterValue: 3, mode: 0, maxDist: 0x20000,
            threshold: unchecked((int)0xFFFF0000), priorityFlag: 0, random: () => 0);
        Assert.Same(visible, found);
    }

    // func_8004E80C:91-209: mode 6 returns the seeker; unknown modes return null.
    [Fact]
    public void FindTargetEntityModeSixAndUnknownMode()
    {
        var env = new EnemyWorld(0, 100);
        var self = env.NewOwner();
        Assert.Same(self, EnemyAttackScheduler.FindTargetEntity(self, Array.Empty<EnemyEntity>(),
            1, 3, 6, 0x1000, -1, 0, () => 0));
        Assert.Null(EnemyAttackScheduler.FindTargetEntity(self, new[] { self },
            1, 3, 9, 0x1000, -1, 0, () => 0));
    }
}
