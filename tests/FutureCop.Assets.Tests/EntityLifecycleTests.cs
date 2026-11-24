using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The damage and retirement chain that carries an outpost from a lethal hit to message 5:
/// gApplyEntityBaseDamage (8002AA1C.c), gStepEntityBaseCountdown (8002A940.c), the deferred updates
/// (8004CE04.c), gRetireEntityBase / gRetireEntity (8002A810.c / 8004D958.c), the sweep (8004D598.c),
/// the level reset (8004CCDC.c), and the message dispatch of gHandleLinkedPropEntity (8002D664.c) and
/// the child part's gHandleType11EntityMessages (8002DEA8.c). Expectations are computed from the
/// cited C lines.
/// </summary>
public class EntityLifecycleTests
{
    /// <summary>CONFT outpost parameters (F6 = 1, F7 = 2, 500 health, slot 101, flags 0x110148).</summary>
    private static CaptureObjective Outpost(short health = 500, uint flags = 0x00910149, int childModel = 32) =>
        new(112, 0, 0, 0, teamA: 1, teamB: 2, markA: 1, markB: 2, stepA: 60, stepB: 0, reachWord: 0x400,
            flags: flags, childModelId: childModel, health: health, childFlags: 0x00A10051)
        {
            SlotIndex = 101,
        };

    private static ProjectileContact Shot(short damage, int shooter = 12) => new(shooter, damage, 0, 0, 0);

    private sealed class Probe : LiveEntity
    {
        public Probe(int recordId, int type) { RecordIdValue = recordId; TypeValue = type; }
        public int RecordIdValue { get; }
        public int TypeValue { get; }
        public override int RecordId => RecordIdValue;
        public override int DispatchType => TypeValue;
        public override (int X, int Z) GroundPosition => (0, 0);
        public override int HandleMessage(int message, EntityWorld world, object? data) =>
            BaseHandler(message, this, world, data);
    }

    // ---- gApplyEntityBaseDamage, 8002AA1C.c ------------------------------------------------------

    [Fact]
    public void Message11TakesAmountAndSourceFromTheOtherEntity()
    {
        var world = new EntityWorld();
        var scripts = new List<(int Script, int Dead, int Source)>();
        world.OnScript = (_, s, d, src) => scripts.Add((s, d, src));
        var outpost = Outpost();
        outpost.DamageScript = 1807;
        outpost.Flags19 = 0x3F;
        var shot = (ProjectileContact)world.Spawn(Shot(30));

        Assert.Equal(0, LiveEntity.ApplyBaseDamage(11, outpost, world, shot));
        Assert.Equal(470, outpost.Health);                                  // :103 unk1C -= amount (+0x1A)
        Assert.NotEqual(0u, outpost.Flags & LiveEntity.DamagedFlag);        // :101
        Assert.Equal(0x0F, outpost.Flags19);                                // :111 low nibble kept
        Assert.Equal(0u, outpost.SourceUnitWord);                           // not lethal
        // :129: the script sees "dead" = 0 and the shot's +0x14 masked to 15 bits (8005E47C.c:73).
        Assert.Equal((1807, 0, 12), Assert.Single(scripts));
    }

    [Fact]
    public void LethalMessage11StoresTheOtherEntitysRuntimeId()
    {
        var world = new EntityWorld();
        var outpost = Outpost(health: 20);
        var shot = world.Spawn(Shot(30));
        LiveEntity.ApplyBaseDamage(11, outpost, world, shot);
        Assert.Equal(-10, outpost.Health);
        Assert.Equal((uint)shot.RuntimeId, outpost.SourceUnitWord);         // :120 from.source = +0x08
    }

    [Fact]
    public void LethalMessage14TagsTheAttackerIdAndPacksTheKind()
    {
        var world = new EntityWorld();
        var attacker = world.Spawn(new Probe(287, 0x25));
        var outpost = Outpost(health: 10);
        outpost.Flags19 = 0x01;
        LiveEntity.ApplyBaseDamage(14, outpost, world, new DirectHit(0x8123, 25, 5, attacker));
        Assert.Equal(-15, outpost.Health);
        Assert.Equal((uint)attacker.RuntimeId | 0x40000000, outpost.SourceUnitWord); // :117
        Assert.Equal((byte)(0x01 | ((5 << 3) & 0x7F0)), outpost.Flags19);   // :107-108 u8 store
    }

    [Fact]
    public void LethalMessage14WithoutAttackerLeavesTheSourceWord()
    {
        var outpost = Outpost(health: 10);
        LiveEntity.ApplyBaseDamage(14, outpost, new EntityWorld(), new DirectHit(1, 25, 0, null));
        Assert.Equal(-15, outpost.Health);
        Assert.Equal(0u, outpost.SourceUnitWord);                           // :116 null attacker
    }

    [Theory]
    [InlineData(0x80u, 30)]   // :87-89 flag 0x80 ignores the hit
    [InlineData(0u, 0)]       // :100 a non-positive amount changes nothing
    [InlineData(0u, -5)]
    public void IgnoredHitsChangeNothing(uint extraFlags, int damage)
    {
        var outpost = Outpost(flags: 0x00910149 | extraFlags);
        LiveEntity.ApplyBaseDamage(14, outpost, new EntityWorld(), new DirectHit(0, (short)damage, 0, null));
        Assert.Equal(500, outpost.Health);
        Assert.Equal(0u, outpost.Flags & LiveEntity.DamagedFlag);
    }

    [Fact]
    public void NonLethalHitAcrossAQuarterLatchesTheSlotRefresh()
    {
        var world = new EntityWorld { HasSlotDescriptor = slot => slot == 101 };
        var outpost = Outpost();
        // 500 -> 370: gQuarterIndex(500, 500) = 3, gQuarterIndex(370, 500) = 2 (8003B65C.c).
        LiveEntity.ApplyBaseDamage(14, outpost, world, new DirectHit(0, 130, 0, null));
        Assert.NotEqual(0u, outpost.Flags & LiveEntity.QuartileFlag);       // 8003BA50.c

        var same = Outpost();
        LiveEntity.ApplyBaseDamage(14, same, world, new DirectHit(0, 10, 0, null)); // stays in quarter 3
        Assert.Equal(0u, same.Flags & LiveEntity.QuartileFlag);

        var noSlot = Outpost();
        LiveEntity.ApplyBaseDamage(14, noSlot, new EntityWorld(), new DirectHit(0, 130, 0, null));
        Assert.Equal(0u, noSlot.Flags & LiveEntity.QuartileFlag);          // D_800A0C70[slot] == 0
    }

    [Theory]
    [InlineData(125, 500, 0)]
    [InlineData(126, 500, 1)]
    [InlineData(249, 500, 1)]
    [InlineData(250, 500, 2)]
    [InlineData(374, 500, 2)]
    [InlineData(375, 500, 3)]
    public void QuarterIndexFollowsTheThreeCompares(int value, int range, int expected) =>
        Assert.Equal(expected, LiveEntity.QuarterIndex(value, range));

    // ---- gStepEntityBaseCountdown (message 9), 8002A940.c ----------------------------------------

    [Fact]
    public void Message9DamagesWithoutTheIgnoreGuardAndTagsTheSourceId()
    {
        var world = new EntityWorld();
        var source = world.Spawn(new Probe(40, 2));
        var outpost = Outpost(health: 10, flags: 0x00910149 | 0x80);
        Assert.Equal(25, LiveEntity.StepCountdown(outpost, world, new AreaHit(0, source, 25)));
        Assert.Equal(-15, outpost.Health);
        Assert.Equal((uint)source.RuntimeId, outpost.SourceUnitWord);       // arg->unk08->unk08, no tag bit
        Assert.NotEqual(0u, outpost.Flags & LiveEntity.DamagedFlag);
    }

    // ---- message dispatch, 8002D664.c / 8002DEA8.c ------------------------------------------------

    [Fact]
    public void OutpostMessage11MirrorsHealthAndDamageFlagOntoTheChild()
    {
        var world = new EntityWorld();
        var outpost = Outpost();
        var shot = world.Spawn(Shot(30));
        Assert.Equal(0, outpost.HandleMessage(11, world, shot));            // 8002D664.c:170
        Assert.Equal(470, outpost.ChildPart!.Health);                       // :160
        Assert.NotEqual(0u, outpost.ChildPart.Flags & LiveEntity.DamagedFlag); // :155-156
    }

    [Fact]
    public void ChildPartForwardsHitsToTheOutpostAndBlocksMessage9()
    {
        var world = new EntityWorld();
        var outpost = Outpost();
        var child = outpost.ChildPart!;
        var shot = world.Spawn(Shot(30));
        Assert.Equal(0, child.HandleMessage(14, world, new DirectHit(0, 40, 0, null))); // 8002DEA8.c:84-89
        Assert.Equal(460, outpost.Health);
        Assert.Equal(460, child.Health);                                    // mirrored by 8002D664.c:160
        child.HandleMessage(11, world, shot);
        Assert.Equal(430, outpost.Health);
        Assert.Equal(0, child.HandleMessage(9, world, new AreaHit(0, null, 50))); // :79-82
        Assert.Equal(430, outpost.Health);
    }

    [Fact]
    public void Message10AnswersTheIdentityOfTheLinkedPart()
    {
        var world = new EntityWorld();
        var outpost = Outpost();
        var shot = Shot(30);
        Assert.Equal(1, outpost.HandleMessage(10, world, shot));            // 8002D664.c:146-148
        Assert.Equal(0, outpost.HandleMessage(10, world, outpost.ChildPart)); // own child
        Assert.Equal(0, outpost.HandleMessage(10, world, null));            // unk100 (0) == arg2
        Assert.Equal(1, outpost.ChildPart!.HandleMessage(10, world, shot)); // 8002DEA8.c:71-74
        Assert.Equal(0, outpost.ChildPart.HandleMessage(10, world, outpost));
        Assert.Equal(0, outpost.ChildPart.HandleMessage(10, world, null));  // :75-77
    }

    [Fact]
    public void Message10WithACandidateClaimsThroughTheField()
    {
        var outpost = Outpost();
        var field = new ObjectiveField(new[] { outpost }, new MatchScore(1));
        outpost.HandleMessage(10, new EntityWorld(), new CaptureCandidate(12, 1, 1, 0, 0, 0, 0));
        Assert.Equal(1, outpost.CaptureMode);                               // 8002D664.c:142-144
        Assert.Equal(1, field.Score.Score[0]);
    }

    // ---- message 4 retire, 8002A810.c / 8004D958.c ------------------------------------------------

    [Fact]
    public void Message4SetsTheChildBitRetiresAndClearsTheId()
    {
        var world = new EntityWorld();
        var footprints = new List<(LiveEntity Entity, LiveEntity? Owner, int Mask)>();
        var scripts = new List<(int Script, int Arg)>();
        world.OnRetireFootprint = (e, o, m) => footprints.Add((e, o, m));
        world.OnScript = (_, s, a, _) => scripts.Add((s, a));
        var outpost = world.Spawn(Outpost(health: -1)) as CaptureObjective;
        outpost!.RetireScript = 1807;
        outpost.Flags19 = 0xF5;
        var shot = world.Spawn(Shot(30));
        outpost.SourceUnitWord = (uint)shot.RuntimeId;

        Assert.Equal(1, outpost.HandleMessage(4, world, null));
        Assert.NotEqual(0u, outpost.ChildPart!.Flags & CaptureObjective.ChildRetireFlag); // 8002D664.c:172-174
        Assert.Equal(LiveEntity.RetireFlags, outpost.Flags & LiveEntity.RetireFlags);   // 8002A810.c:105
        Assert.Equal(0, outpost.RuntimeId);                                 // 8004D958.c:59
        // 8002A810.c:112-133: the type-2 owner lends its record, mask (0xF5 >> 3) & 0x1E.
        var footprint = Assert.Single(footprints);
        Assert.Same(outpost, footprint.Entity);
        Assert.Same(shot, footprint.Owner);
        Assert.Equal((0xF5 >> 3) & 0x1E, footprint.Mask);
        // 8004D958.c:53-54: ((flags >> 26) ^ 1) & 1 is 1 without 0x4000000.
        Assert.Equal((1807, 1), Assert.Single(scripts));

        Assert.Equal(0, outpost.HandleMessage(4, world, null));             // 8002A810.c:101-103
    }

    [Fact]
    public void RetireLendsNoRecordFromANonProjectileOwner()
    {
        var world = new EntityWorld();
        LiveEntity? owner = new Probe(1, 1);
        world.OnRetireFootprint = (_, o, _) => owner = o;
        var unit = world.Spawn(new Probe(287, 0x25));
        var outpost = Outpost(health: -1);
        world.SpawnObjective(outpost);
        outpost.SourceUnitWord = (uint)unit.RuntimeId | 0x40000000;          // masked by 0x3FFFFFFF
        outpost.HandleMessage(4, world, null);
        Assert.Null(owner);                                                 // 8002A810.c:116 type == 2 only
    }

    [Fact]
    public void RetireScriptIsSkippedWithFlag0x8000000()
    {
        var world = new EntityWorld();
        int calls = 0;
        world.OnScript = (_, _, _, _) => calls++;
        var outpost = Outpost(flags: 0x00910149 | LiveEntity.NoRetireScriptFlag);
        outpost.RetireScript = 1807;
        world.SpawnObjective(outpost);
        LiveEntity.Retire(outpost, world);
        Assert.Equal(0, calls);
        Assert.Equal(0, outpost.RuntimeId);
    }

    // ---- world: spawn order, cull, deferred updates, sweep, reset -------------------------------

    [Fact]
    public void ObjectiveSpawnLinksTheChildFirstWithTheNextId()
    {
        var world = new EntityWorld();
        var outpost = world.SpawnObjective(Outpost(), targetable: true);
        Assert.Equal(new LiveEntity[] { outpost.ChildPart!, outpost }, world.Units); // 8004D444.c + 8002C584.c:297
        Assert.Equal(1, outpost.RuntimeId);
        Assert.Equal(2, outpost.ChildPart!.RuntimeId);
        Assert.NotEqual(0u, outpost.Flags & LiveEntity.SpawnedUnitFlag);   // 8004D4F8.c:59
        Assert.Equal(0u, outpost.ChildPart.Flags & LiveEntity.SpawnedUnitFlag);
        Assert.Same(outpost, world.FindUnitById(1));
        Assert.Null(world.FindUnitById(3));
    }

    [Fact]
    public void CullKeepsFlag8OrNearbyEntitiesAndClearsTheDamageBit()
    {
        var world = new EntityWorld();
        var far = new CaptureObjective(1, 0x100000, 0, 0, 1, 2, 1, 2, 0, 0, 0, flags: 0x2000000);
        var near = new CaptureObjective(2, 0x30000, 0, 0x10000, 1, 2, 1, 2, 0, 0, 0, flags: 0x2000000);
        var tagged = new CaptureObjective(3, 0x100000, 0, 0x100000, 1, 2, 1, 2, 0, 0, 0, flags: 8);
        world.SpawnObjective(far);
        world.SpawnObjective(near);
        world.SpawnObjective(tagged);
        world.CullActive(0, 0);
        // 8004DF28.c:138-142: dist = 0x30000 + 0x10000 - 0x8000 = 0x38000 is not < 0x38000.
        Assert.Equal(new LiveEntity[] { tagged }, world.Active);
        Assert.Equal(0x20000008u, tagged.Flags);                            // :143

        var nearer = new CaptureObjective(4, 0x2FFFF, 0, 0x10000, 1, 2, 1, 2, 0, 0, 0, flags: 0x2000000);
        world.SpawnObjective(nearer);
        world.CullActive(0, 0);
        Assert.Contains(nearer, world.Active);
        Assert.Equal(0x20000000u, nearer.Flags);                            // 0x2000000 cleared
        Assert.Equal(0x2000000u, far.Flags);                                // :183 only 0x20000000 cleared
    }

    [Fact]
    public void DeferredUpdatesSendMessage4OrConsumeTheQuartileLatch()
    {
        var world = new EntityWorld();
        var refreshes = new List<(LiveEntity, int)>();
        world.OnRefreshSlotSpawns = (e, m) => refreshes.Add((e, m));
        var dying = world.SpawnObjective(Outpost(health: -3));
        var latched = world.SpawnObjective(Outpost(flags: 0x00910149 | LiveEntity.QuartileFlag));
        latched.Flags19 = 0x28;
        world.CullActive(0, 0);
        world.ProcessDeferredUpdates();
        Assert.Equal(0, dying.RuntimeId);                                   // 8004CE04.c:59-60
        Assert.Equal(0u, latched.Flags & LiveEntity.QuartileFlag);          // :61-62
        Assert.Equal((latched, (0x28 >> 3) & 0x1E), Assert.Single(refreshes)); // :63
        Assert.NotEqual(0, latched.RuntimeId);
    }

    [Fact]
    public void SweepSendsMessage5AndScoresTheDestroyedOutpost()
    {
        // 1P with a drone on team 2: the drone claims the outpost (FE = 2), so message 5 credits team
        // F6 = 1, the player (8002D664.c:184, 208-218).
        var world = new EntityWorld();
        var spawned = new List<LiveEntity>();
        world.OnSpawnedUnitRetired = e => spawned.Add(e);
        int e0 = 4;
        var score = new MatchScore(1, singlePlayerTeam: 2, singlePlayerEntityId: 287) { OnSessionE0Changed = v => e0 = v };
        var outpost = Outpost();
        var field = new ObjectiveField(new[] { outpost }, score);
        world.SpawnObjective(outpost, targetable: true);
        Assert.Equal(1, field.Touch(new CaptureCandidate(287, 0x25, 2, 0, 0, 0, 0)));
        Assert.Equal(1, score.Score[1]);

        var shot = (ProjectileContact)world.Spawn(Shot(600));
        world.CullActive(0, 0);
        outpost.HandleMessage(11, world, shot);
        shot.HandleMessage(11, world, outpost);
        Assert.Equal(-100, outpost.Health);
        Assert.Equal(-1, shot.Health);                                      // 80036624.c:155-156
        Assert.Equal(0, world.SweepRetired());                              // nothing retired yet

        world.CullActive(0, 0);
        world.ProcessDeferredUpdates();                                     // child, outpost, shot retire
        Assert.Equal(3, world.SweepRetired());
        Assert.Empty(world.Units);
        Assert.Equal(1, score.Score[0]);                                    // 8002D664.c:214
        Assert.Equal(1, score.Total[0]);
        Assert.Equal(1, score.OutpostStateCount);                           // :217
        Assert.Equal(8, e0);                                                // :218
        Assert.Equal(new LiveEntity[] { outpost }, spawned);                // 8004D598.c:51-52
        Assert.Empty(field.Objectives);                                     // freed
        Assert.True(outpost.Freed);
        Assert.True(outpost.ChildPart!.Freed);
    }

    [Fact]
    public void ChildOutOfRangeRetiresOneFrameLaterThroughTheRetireBit()
    {
        var world = new EntityWorld();
        // CONFT child flags 0xA10051 lack bit 8; the outpost is far but carries bit 8 (0x110148).
        var outpost = new CaptureObjective(112, 0x100000, 0, 0, 1, 2, 1, 2, 60, 0, 0x400,
            flags: 0x00910149, childModelId: 32, health: -1, childFlags: 0x00A10051);
        outpost.ChildPart!.Health = -1;
        world.SpawnObjective(outpost);
        world.CullActive(0, 0);
        Assert.Equal(new LiveEntity[] { outpost }, world.Active);
        world.ProcessDeferredUpdates();
        Assert.Equal(0, outpost.RuntimeId);
        Assert.NotEqual(0, outpost.ChildPart.RuntimeId);
        Assert.Equal(1, world.SweepRetired());

        world.CullActive(0, 0);                                             // bit 8 keeps it active now
        world.ProcessDeferredUpdates();
        Assert.Equal(0, outpost.ChildPart.RuntimeId);
        Assert.Equal(1, world.SweepRetired());
    }

    [Fact]
    public void LevelResetRetiresEverythingWithoutScoring()
    {
        var world = new EntityWorld();
        var scripts = new List<int>();
        world.OnScript = (_, _, arg, _) => scripts.Add(arg);
        var score = new MatchScore(1, singlePlayerTeam: 2, singlePlayerEntityId: 287);
        var outpost = Outpost();
        outpost.RetireScript = 1807;
        _ = new ObjectiveField(new[] { outpost }, score);
        world.SpawnObjective(outpost);
        Assert.Equal(2, outpost.TryCapture(new CaptureCandidate(287, 0x25, 2, 0, 0, 0, 0)));

        world.RetireAndFreeAll();
        Assert.Empty(world.Units);
        Assert.Equal(0, world.IdCounter);                                   // 8004CCDC.c:58
        Assert.Equal(new[] { 0, 0 }, score.Score);                          // 8002D664.c:183 skips 0x4000000
        Assert.NotEqual(0u, outpost.Flags & LiveEntity.ResetFlag);          // 8004CCDC.c:53
        Assert.Equal(0u, outpost.Flags & LiveEntity.RetireFlags);           // retired directly, no message 4
        Assert.Equal(new[] { 0 }, scripts);                                 // 8004D958.c:54 arg is 0
    }

    // ---- pair test, 800620D8.c -------------------------------------------------------------------

    [Fact]
    public void PairTestNeedsOverlappingSpheresAndBoxes()
    {
        var outpost = new CollisionCoord(0, 0, 0, 463 << 4, 152, 437);
        var shot = new CollisionCoord(0, 0, 0, 92 << 4, 15, 46);
        Assert.True(EntityPairCollision.ReachesPartTest(outpost, shot));
        int reach = (437 + 46) << 4;                                        // :92 width box
        Assert.True(EntityPairCollision.ReachesPartTest(outpost, shot with { X = reach - 1 }));
        Assert.False(EntityPairCollision.ReachesPartTest(outpost, shot with { X = reach }));
        int height = (152 + 15) << 4;                                       // :94 height box
        Assert.True(EntityPairCollision.ReachesPartTest(outpost, shot with { Y = height - 1 }));
        Assert.False(EntityPairCollision.ReachesPartTest(outpost, shot with { Y = -height }));
    }

    [Fact]
    public void CoordTakesRadiusHeightAndWidthFromFrameRecordWords6And4And7()
    {
        var coord = CollisionCoord.FromFrameBounds(1, 2, 3, new[] { new short[] { 0, 167, 107, 244, 152, 362, 463, 437 } });
        Assert.Equal(new CollisionCoord(1, 2, 3, 463 << 4, 152, 437), coord);
        Assert.Equal(new CollisionCoord(1, 2, 3, 0, 0, 0), CollisionCoord.FromFrameBounds(1, 2, 3, Array.Empty<short[]>()));
    }

    // ---- FromRecord, 8002A5F4.c / 8002C584.c / 8002DC94.c ---------------------------------------

    [Fact]
    public void FromRecordSeedsTheLifecycleWords()
    {
        var source = new byte[0x50];
        BitConverter.GetBytes((ushort)500).CopyTo(source, 0x18);
        BitConverter.GetBytes((ushort)7).CopyTo(source, 0x1A);
        source[0x1F] = 20;
        source[0x20] = 101;
        source[0x40] = 0x40;                                                // child loses the slot
        source[0x48] = 1;
        source[0x49] = 2;
        var record = new EntityRecord(112, 0x24, 0, 0, 0, 0x00110148, 500, 0, EntityContainer.Csac, 0, 0,
            source, new[] { 11, 22, 33 }, new[] { new ResourceReference("Cobj", 31), new ResourceReference("Cobj", 32) });
        var outpost = CaptureObjective.FromRecord(record, 0);
        Assert.Equal(0x00910149u, outpost.Flags);                           // 8002C584.c:301,335
        Assert.Equal(0x00A10051u, outpost.ChildPart!.Flags);                // 8002DC94.c:141, 8002C584.c:299-300
        Assert.Equal(500, outpost.Health);
        Assert.Equal(500, outpost.MaxHealth);                               // 8002A5F4.c:134
        Assert.Equal(500, outpost.ChildPart.Health);
        Assert.Equal(101, outpost.SlotIndex);
        Assert.Equal(0, outpost.ChildPart.SlotIndex);                       // 8002C584.c:289-292
        Assert.Equal(15, outpost.Flags19);                                  // 8002A5F4.c:122-126
        Assert.Equal(7, outpost.Word1A);
        Assert.Equal(11, outpost.DamageScript);
        Assert.Equal(22, outpost.RetireScript);
        Assert.Equal(0, outpost.ChildPart.RetireScript);
        Assert.Equal(112, outpost.ChildPart.RecordId);
        Assert.Equal(0x24, outpost.ChildPart.DispatchType);
    }
}
