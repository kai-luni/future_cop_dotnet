using System.Buffers.Binary;
using FutureCop.Assets;
using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The weapon pickup path of the rotating-prop class: the proximity test func_8002FF6C, the payload
/// evaluation func_8002FC90 and the two slot refills func_8006BE90 / func_8006BDFC.
/// </summary>
/// <remarks>
/// The archive side is checked against CONFT.MIS, which carries 11 type-0x10 placement records; each of
/// them has both 0x01000000 (instant) and 0x02000000 (self-retire) set, so the interval path of
/// func_8002FC90:91-102 is only exercised by the synthetic cases below.
/// </remarks>
public class WeaponPickupTests
{
    // ---- fixtures ---------------------------------------------------------------------------

    /// <summary>
    /// Builds one type-0x10 placement chunk plus the aRSL list that follows it, in retail layout.
    /// The descriptor func_8002FA20 reads starts at chunk + 0x08 (func_8004D6D4:103 passes
    /// func_80019C38(...) - 4), so its type byte sits at chunk + 0x0C and the spawn position at
    /// chunk + 0x10/+0x14/+0x18 (func_8002A5F4:159 copies src->position from descriptor + 8).
    /// </summary>
    private static byte[] Placement(int section, int x, int z, int kindType, int height,
        int extX, int extY, int extZ, int speed, uint payload, short angle, short rate,
        int modelId, int kind = 1, int value = 1, int y = 0)
    {
        var data = new byte[0x44 + 0x1C];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), ChunkReader.TagValue("tACT"));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 0x44);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), section);
        data[12] = 0x10;                                     // func_8004D6D4:118 dispatch byte
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x0C), 0x10);   // type word
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x10), x);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x14), y);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x18), z);
        int desc = 8;
        data[desc + 0x24] = (byte)(sbyte)kindType;
        data[desc + 0x25] = (byte)kind;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(desc + 0x26), (ushort)value);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(desc + 0x28), (short)height);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(desc + 0x2A), (short)extX);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(desc + 0x2C), (short)extY);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(desc + 0x2E), (short)extZ);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(desc + 0x30), speed);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(desc + 0x34), payload);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(desc + 0x38), angle);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(desc + 0x3A), rate);
        WriteList(data, 0x44, section, modelId);
        return data;
    }

    /// <summary>Builds one type-0x63 definition chunk the registry caches, plus its aRSL list.</summary>
    private static byte[] Definition(byte id, int section, int modelId, short ammoMax,
        short reserveMax, byte cycle, short startAmmo = 0)
    {
        var data = new byte[0x60 + 0x1C];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), ChunkReader.TagValue("tACT"));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 0x60);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), section);
        data[12] = WeaponCatalog.RegistryPayloadType;
        int rec = 12 + 0x10;                                  // WeaponCatalog.RecordOffset
        data[rec] = id;                                       // record byte 0 = registry id
        int halfA = rec + 0x04;
        data[halfA + 0x00] = 0x03;                            // action
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(halfA + 0x02),
            startAmmo == 0 ? ammoMax : startAmmo);            // def + 0x02, seeded into +0x84
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(rec + 0x08), ammoMax);   // tgt + 0x08
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(rec + 0x28), reserveMax); // tgt + 0x28
        data[rec + 0x2A] = cycle;                             // tgt + 0x2A -> +0x93
        WriteList(data, 0x60, section, modelId);
        return data;
    }

    /// <summary>The aRSL list func_8006A918 walks: a Cobj reference and a null terminator.</summary>
    private static void WriteList(byte[] data, int list, int section, int modelId)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(list), ChunkReader.TagValue("aRSL"));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(list + 4), 0x1C);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(list + 8), section);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(list + 12), ChunkReader.TagValue("Cobj"));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(list + 16), modelId);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(list + 20), ChunkReader.TagValue("NULL"));
    }

    /// <summary>
    /// A field with one pickup at the origin plus a player whose slots hold the synthetic registry
    /// records: id 0x10 with an ammunition limit of 145 and a reserve word of 15, and id 0x00 with
    /// 600 and 10.
    /// </summary>
    private static (WeaponPickups Field, PlayerResupplyState Player, WeaponSlots Slots) Field(uint payload,
        int period = 4, int ext = 1000, int amount = -1, int difficulty = 0)
    {
        var (data, registry) = Archive(
            Definition(0x00, 0x3F, 23, ammoMax: 600, reserveMax: 10, cycle: 1),
            Definition(0x10, 0x44, 24, ammoMax: 145, reserveMax: 15, cycle: 9));
        var field = new WeaponPickups(registry, difficulty, data);
        field.Add(new WeaponPickup
        {
            X = 0, Y = 0, Z = 0,
            HalfX = ext, HalfY = ext, HalfZ = ext,
            Payload = payload,
            Amount = amount,
            Period = period,
        });
        var player = new PlayerResupplyState { Gauge = 100, GaugeMax = 500 };
        player.Slots.Reset(new byte[] { 0x00, 0x10, 0x00 }, registry, difficulty);
        return (field, player, player.Slots);
    }

    /// <summary>Concatenates archives and returns the registry the port reads from them.</summary>
    private static (byte[] Data, IReadOnlyDictionary<byte, WeaponRecord> Registry) Archive(params byte[][] parts)
    {
        var data = parts.SelectMany(p => p).ToArray();
        return (data, WeaponCatalog.BuildRegistry(data, new[] { 23, 24, 25, 57 }));
    }

    // ---- func_8006BE90 / func_8006BDFC ------------------------------------------------------

    [Fact]
    public void AmmoRefillClampsAtTheMaximumAndReportsWhetherItMoved()
    {
        var (field, _, slots) = Field(0);
        slots.Ammo[1] = 140;
        Assert.Equal(145, field.AmmoLimit(slots, 1));    // tgt + 0x08 of id 0x10

        // func_8006BE90:50-59: below max, step added, then clamped.
        Assert.False(field.RefillAmmo(slots, 1, 3));
        Assert.Equal(143, slots.Ammo[1]);

        Assert.False(field.RefillAmmo(slots, 1, 50));
        Assert.Equal(145, slots.Ammo[1]);

        // func_8006BE90:61 returns 1 once the counter sits at the maximum.
        Assert.True(field.RefillAmmo(slots, 1, 10));
        Assert.Equal(145, slots.Ammo[1]);
    }

    [Fact]
    public void NegativeStepJumpsStraightToTheMaximum()
    {
        var (field, _, slots) = Field(0);
        slots.Ammo[1] = 7;
        // func_8006BE90:56-58: step < 0 assigns max instead of adding.
        Assert.False(field.RefillAmmo(slots, 1, -1));
        Assert.Equal(145, slots.Ammo[1]);
    }

    [Fact]
    public void RefillScalesTheMaximumTheWayFunc8006BD48Does()
    {
        var (plain, _, a) = Field(0, difficulty: 0);
        var (hard, _, b) = Field(0, difficulty: 1);
        a.Ammo[1] = 0;
        b.Ammo[1] = 0;
        Assert.False(plain.RefillAmmo(a, 1, -1));
        Assert.False(hard.RefillAmmo(b, 1, -1));
        Assert.Equal(145, a.Ammo[1]);
        // func_8006BD48:68-70, difficulty 1 adds a quarter.
        Assert.Equal(181, b.Ammo[1]);
    }

    [Fact]
    public void ReserveRefillReloadsTheScaledWordAndCopiesTheCycleByte()
    {
        var (field, _, slots) = Field(0);
        slots.Reserve[1] = 0;
        slots.Cycles[1] = 99;
        field.RefillReserve(slots, 1);
        // func_8006BDFC:70-77: the copy happens while +0x8A is not positive, the reload always.
        Assert.Equal(9, slots.Cycles[1]);
        Assert.Equal(15, slots.Reserve[1]);
    }

    [Fact]
    public void ReserveRefillLeavesTheCycleAloneWhileTheCounterIsPositive()
    {
        var (field, _, slots) = Field(0);
        slots.Reserve[1] = 3;
        slots.Cycles[1] = 42;
        field.RefillReserve(slots, 1);
        Assert.Equal(42, slots.Cycles[1]);
        Assert.Equal(15, slots.Reserve[1]);    // the counter is recomputed regardless
    }

    // ---- func_8002FC90 ---------------------------------------------------------------------

    [Fact]
    public void InstantPayloadSkipsTheAccumulatorAndJumpsToTheMaximum()
    {
        var (field, player, slots) = Field(0);
        slots.Ammo[0] = 3;
        int accumulator = 0;
        var ctx = ResupplyContext.Solo(player, RetailClock.TicksPerFrame);

        // func_8002FC90:88-102: with 0x01000000 the accumulator branch never runs.
        var outcome = field.Apply(ctx, player, WeaponPickups.Instant | 0x0001, -1, ref accumulator, 4);
        Assert.True(outcome.Paid);
        Assert.True(outcome.Instant);
        Assert.Equal(0, outcome.Quotient);
        Assert.Equal(0, accumulator);
        Assert.Equal(600, slots.Ammo[0]);      // id 0x00, negative amount -> max
    }

    [Fact]
    public void IntervalPayloadWaitsForThePeriodThenPaysTheQuotient()
    {
        var (field, player, slots) = Field(0);
        slots.Ammo[0] = 3;
        var ctx = ResupplyContext.Solo(player, RetailClock.TicksPerFrame);
        int accumulator = 0;

        // func_8002FC90:92-95: 5 ticks against a period of 4 already crosses, so the first call pays.
        var first = field.Apply(ctx, player, 0x0001, -1, ref accumulator, 4);
        Assert.True(first.Paid);
        Assert.False(first.Instant);
        Assert.Equal(1, first.Quotient);
        Assert.Equal(1, accumulator);          // 5 - 1 * 4
        Assert.Equal(4, slots.Ammo[0]);        // step == quotient

        // A short period of 7 waits: 1 + 5 = 6 < 7.
        int second = 0;
        var wait = field.Apply(ctx, player, 0x0001, -1, ref second, 7);
        Assert.Equal(5, second);
        Assert.False(wait.Paid);
        Assert.Equal(4, slots.Ammo[0]);
    }

    [Fact]
    public void TheIntervalPeriodComesFromTheSpeedWordOfTheDescriptor()
    {
        // func_8002FA20:95-104 with CONFT's speed word; a negative rate clamps to one tick.
        Assert.Equal(3, WeaponPickup.RetailPeriod(6488064));
        Assert.Equal(1, WeaponPickup.RetailPeriod(-4916));
        Assert.Equal(1, WeaponPickup.RetailPeriod(0));
    }

    [Fact]
    public void AmmoBitsSelectTheSlotAndReserveBitsSelectTheReserveSlot()
    {
        var (field, player, slots) = Field(0);
        slots.Ammo[1] = 0;
        slots.Reserve[2] = 0;
        int accumulator = 0;

        // Bit 1 -> ammunition of slot 1; bit 0x40 -> reserve of slot 2 (func_8002FC90:184-201).
        var outcome = field.Apply(ResupplyContext.Solo(player, 5), player,
            WeaponPickups.Instant | 0x0002 | 0x0040, -1, ref accumulator, 1);

        Assert.True(outcome.Paid);
        Assert.Equal(2, outcome.Slots);
        Assert.Equal(145, slots.Ammo[1]);
        Assert.Equal(10, slots.Reserve[2]);    // id 0x00 reserve word
        Assert.Equal(600, slots.Ammo[0]);      // untouched bits leave their slot alone
    }

    [Fact]
    public void HealthBitFillsTheGaugeOfTheSuppliedEntity()
    {
        var (field, player, _) = Field(0);
        int accumulator = 0;
        var ctx = ResupplyContext.Solo(player, 5);

        player.Gauge = 120;
        field.Apply(ctx, player, WeaponPickups.Instant | WeaponPickups.Health, -1, ref accumulator, 1);
        Assert.Equal(500, player.Gauge);       // func_8002FC90:146-147, negative -> +0x1E

        player.Gauge = 120;
        field.Apply(ctx, player, WeaponPickups.Instant | WeaponPickups.Health, 30, ref accumulator, 1);
        Assert.Equal(150, player.Gauge);       // func_8002FC90:148-154, cur + amount

        player.Gauge = 490;
        field.Apply(ctx, player, WeaponPickups.Instant | WeaponPickups.Health, 30, ref accumulator, 1);
        Assert.Equal(500, player.Gauge);       // clamped at +0x1E
    }

    [Fact]
    public void HealthBitReachesTheCoOpPartnerOnlyWhenTheSessionAllowsIt()
    {
        var (field, player, _) = Field(0);
        var partner = new PlayerResupplyState { Gauge = 100, GaugeMax = 500 };
        var two = new PlayerResupplyState?[] { player, partner };
        int accumulator = 0;

        // func_8002FC90:134-141: without a second player the partner slot is skipped.
        field.Apply(new ResupplyContext(player, two, CoOpSharing.Solo, 5), player,
            WeaponPickups.Instant | WeaponPickups.Health, -1, ref accumulator, 1);
        Assert.Equal(500, player.Gauge);
        Assert.Equal(100, partner.Gauge);

        // gPlayerCount >= 2 and gGameSession->unk5C == 0 shares the gauge word.
        field.Apply(new ResupplyContext(player, two, new CoOpSharing(2, false), 5), player,
            WeaponPickups.Instant | WeaponPickups.Health, -1, ref accumulator, 1);
        Assert.Equal(500, partner.Gauge);

        // A blocked session keeps the partner out even with two players.
        partner.Gauge = 100;
        field.Apply(new ResupplyContext(player, two, new CoOpSharing(2, true), 5), player,
            WeaponPickups.Instant | WeaponPickups.Health, -1, ref accumulator, 1);
        Assert.Equal(100, partner.Gauge);
    }

    [Fact]
    public void TimerBitsWriteTheTwoControlWordsAndStaySelfOnly()
    {
        var (field, player, _) = Field(0);
        var partner = new PlayerResupplyState();
        var ctx = new ResupplyContext(player, new PlayerResupplyState?[] { player, partner },
            new CoOpSharing(2, false), 5);
        int accumulator = 0;

        field.Apply(ctx, player, WeaponPickups.Instant | WeaponPickups.TimerA | WeaponPickups.TimerB,
            -1, ref accumulator, 1);
        Assert.Equal(0x34BC, player.TimerA);   // func_8002FC90:179
        Assert.Equal(0x1770, player.TimerB);   // func_8002FC90:182
        Assert.Equal(0, partner.TimerA);       // func_8002FC90:173 skips every non-self entity
    }

    [Fact]
    public void ProxyTypeIsDereferencedBeforeThePayloadIsApplied()
    {
        var (field, player, slots) = Field(0);
        slots.Ammo[1] = 1;
        var proxy = new PlayerResupplyState { EntityType = 0x12, ProxyTarget = player };
        int accumulator = 0;

        var outcome = field.Apply(ResupplyContext.Solo(player, 5), proxy,
            WeaponPickups.Instant | 0x0002, -1, ref accumulator, 1);
        Assert.True(outcome.Paid);
        Assert.Equal(145, slots.Ammo[1]);

        // func_8002FC90:107-110: a proxy without a target aborts the whole call.
        var orphan = new PlayerResupplyState { EntityType = 0x12 };
        slots.Ammo[1] = 1;
        var aborted = field.Apply(ResupplyContext.Solo(player, 5), orphan,
            WeaponPickups.Instant | 0x0002, -1, ref accumulator, 1);
        Assert.False(aborted.Paid);
        Assert.Equal(1, slots.Ammo[1]);
    }

    // ---- func_8002FF6C ---------------------------------------------------------------------

    [Fact]
    public void TheBoxTestIsStrictOnEveryAxis()
    {
        var pickup = new WeaponPickup { X = 100, Y = 50, Z = -100, HalfX = 10, HalfY = 10, HalfZ = 10 };
        Assert.True(WeaponPickups.Overlaps(pickup, 105, 55, -95));
        Assert.True(WeaponPickups.Overlaps(pickup, 91, 41, -109));
        // func_8002FF6C:73-75 compares with `<`, so a delta equal to the half extent misses.
        Assert.False(WeaponPickups.Overlaps(pickup, 110, 50, -100));
        Assert.False(WeaponPickups.Overlaps(pickup, 100, 60, -100));
        Assert.False(WeaponPickups.Overlaps(pickup, 100, 50, -90));
    }

    [Fact]
    public void UpdateIgnoresTargetsThatAreNotThePlayerClass()
    {
        var (field, player, _) = Field(WeaponPickups.Instant | 0x0001);
        player.EntityType = 0x25;               // func_8002FF6C:67-69 returns before the box test
        Assert.Equal(0, field.Update(ResupplyContext.Solo(player, 5), 0, 0, 0));
        Assert.False(field.Items[0].Collected);
    }

    [Fact]
    public void UpdateFollowsTheProxyChainBeforeTestingTheBoxes()
    {
        var (field, player, slots) = Field(WeaponPickups.Instant | 0x0001);
        slots.Ammo[0] = 0;
        var proxy = new PlayerResupplyState { EntityType = 0x12, ProxyTarget = player };
        // func_8002FF6C:64-66 replaces the tested entity with the proxy target, then checks type 1;
        // gPlayerEntities still holds the real player, which is what the payload loop visits.
        var ctx = new ResupplyContext(proxy, new PlayerResupplyState?[] { player }, CoOpSharing.Solo, 5);
        Assert.Equal(1, field.Update(ctx, 0, 0, 0));
        Assert.Equal(600, slots.Ammo[0]);
    }

    [Fact]
    public void ASelfRetiringPickupLeavesTheFieldAfterOnePayout()
    {
        var (field, player, slots) = Field(WeaponPickups.Instant | WeaponPickups.SelfRetire | 0x0001);
        slots.Ammo[0] = 0;
        Assert.Equal(1, field.Update(ResupplyContext.Solo(player, 5), 0, 0, 0));
        Assert.True(field.Items[0].Collected);
        Assert.Equal(0, field.RemainingCount);
        Assert.Equal(1, field.CollectedCount);

        // A second frame pays nothing: func_8002FF6C:77-79 retired the entity.
        slots.Ammo[0] = 0;
        Assert.Equal(0, field.Update(ResupplyContext.Solo(player, 5), 0, 0, 0));
        Assert.Equal(0, slots.Ammo[0]);
    }

    [Fact]
    public void APickupWithoutTheRetireBitStaysOnTheMap()
    {
        var (field, player, slots) = Field(WeaponPickups.Instant | 0x0001);
        slots.Ammo[0] = 0;
        Assert.Equal(1, field.Update(ResupplyContext.Solo(player, 5), 0, 0, 0));
        Assert.False(field.Items[0].Collected);
        Assert.False(field.Items[0].RetiresOnCollect);
    }

    [Fact]
    public void AnIntervalPickupPaysOnTheFrameThatCrossesThePeriod()
    {
        var (field, player, slots) = Field(0x0001, period: 4);
        slots.Ammo[0] = 0;
        // 5 ticks against a period of 4 cross immediately; the quotient of 1 is the refill step.
        Assert.Equal(1, field.Update(ResupplyContext.Solo(player, 5), 0, 0, 0));
        Assert.Equal(1, slots.Ammo[0]);
        Assert.Equal(1, field.Items[0].Accumulator);
    }

    // ---- placement from the archive ---------------------------------------------------------

    [Fact]
    public void PlacementIsReadFromTheTypeZeroTenRecordsOfTheArchive()
    {
        var (data, registry) = Archive(
            Placement(0x64, 4096, 8192, 0, 81, 819, 819, 2048, 6488064, 0x03000001, -1, 1228, 23),
            Placement(0x65, -4096, -8192, 1, 20, 100, 200, 300, -4916, 0x03000010, 512, 0, 24));
        var field = WeaponPickups.FromMission(data, registry, map: null, difficulty: 0);

        Assert.Equal(2, field.Items.Count);
        var first = field.Items[0];
        Assert.Equal(0x64, first.SectionId);
        Assert.Equal(4096, first.X);
        Assert.Equal(8192, first.Z);
        // func_8002FA20:111: y = ground + (height << 4); without a map the ground term is 0.
        Assert.Equal(81 << 4, first.Y);
        // func_8002FA20:92-94: descriptor +0x2A/+0x2C/+0x2E are the box extents, << 4.
        Assert.Equal(819 << 4, first.HalfX);
        Assert.Equal(819 << 4, first.HalfY);
        Assert.Equal(2048 << 4, first.HalfZ);
        Assert.Equal(0x03000001u, first.Payload);
        Assert.Equal(3, first.Period);           // RetailPeriod(6488064)
        Assert.Equal(-1, first.Amount);          // func_8002FF6C:76
        Assert.Equal(23, first.ModelId);         // first Cobj of the aRSL list
        Assert.True(first.RetiresOnCollect);

        var second = field.Items[1];
        Assert.Equal(-4096, second.X);
        Assert.Equal(1, second.KindType);
        Assert.Equal(-2, WeaponPickup.GroundRank(second.KindType));
        Assert.Equal(1, second.Period);          // RetailPeriod(-4916) clamps to 1
        Assert.Equal(512, second.Angle);
        Assert.Equal(20 << 4, second.Y);
    }

    [Fact]
    public void ARecordWithoutAnArslCobjEntryHasNoModel()
    {
        // func_8004D6D4:143-150 only resolves aRSL pairs that name a resource; with none resolved the
        // spawn path attaches no model, and func_8002ACB4:60-62 then returns 0 without drawing.
        var withModel = Placement(0x64, 4096, 8192, 0, 81, 819, 819, 2048, 6488064, 0x03000001, -1, 1228, 23);
        var withoutList = withModel[..0x44].ToArray();               // the tACT chunk alone, no aRSL
        var nullList = (byte[])withModel.Clone();                    // an aRSL whose only pair is NULL
        BinaryPrimitives.WriteUInt32LittleEndian(nullList.AsSpan(0x44 + 12), ChunkReader.TagValue("NULL"));
        BinaryPrimitives.WriteInt32LittleEndian(nullList.AsSpan(0x44 + 16), 23);

        foreach (var data in new[] { withoutList, nullList })
        {
            var (archive, registry) = Archive(data);
            var field = WeaponPickups.FromMission(archive, registry, map: null, difficulty: 0);
            var item = Assert.Single(field.Items);
            Assert.Equal(WeaponPickups.NoModel, item.ModelId);
            // 0 is a valid resource name on a mission that has one, so it must not stand in for "none".
            Assert.NotEqual(0, item.ModelId);
            // The placement itself is intact: only the model is missing.
            Assert.Equal(4096, item.X);
            Assert.Equal(8192, item.Z);
        }
    }

    [Fact]
    public void GroundRankFollowsTheTerrainSelectorOfFunc8002FA20()
    {
        // func_8002FA20:106-110: type 0 -> -1, 1 -> -2, otherwise type - 2.
        Assert.Equal(-1, WeaponPickup.GroundRank(0));
        Assert.Equal(-2, WeaponPickup.GroundRank(1));
        Assert.Equal(3, WeaponPickup.GroundRank(5));
    }

    [Fact]
    public void APlayerBoundToAWalkerMirrorsTheGaugeWordIntoTheBody()
    {
        var (field, _, slots) = Field(0);
        int health = 40;
        // func_8002FC90:162-164 writes +0x1C and then hands it to the host through
        // gSetEntityGaugeValue; the callback is that hand-off.
        var player = PlayerResupplyState.ForWalker(slots, () => health, v => health = v, 500);
        int accumulator = 0;

        var outcome = field.Apply(ResupplyContext.Solo(player, 5), player,
            WeaponPickups.Instant | WeaponPickups.Health, 25, ref accumulator, 1);
        Assert.True(outcome.Paid);
        Assert.Equal(65, health);
        Assert.Equal(65, player.Gauge);

        // The slots of the bound player are the caller's block, not a private copy.
        slots.Ammo[0] = 0;
        field.Apply(ResupplyContext.Solo(player, 5), player,
            WeaponPickups.Instant | 0x0001, -1, ref accumulator, 1);
        Assert.Equal(600, slots.Ammo[0]);
    }

    // ---- retail -----------------------------------------------------------------------------

    private static string? FindPrecinctAssault()
    {
        var p = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..")), "build", "disc", "files", "PA", "CONFT.MIS");
        return File.Exists(p) ? p : null;
    }

    [RetailFact]
    public void RetailPrecinctAssaultCarriesElevenInstantPickups()
    {
        var mis = FindPrecinctAssault();
        Assert.NotNull(mis);
        var file = new MisFile(mis!);
        var map = LevelMap.FromMisFile(mis!);
        var field = WeaponPickups.FromMission(file.Bytes, WeaponCatalog.LoadRegistry(file), map);

        Assert.Equal(11, field.Items.Count);
        Assert.Equal(11, field.Items.Select(i => i.RecordOffset).Distinct().Count());
        Assert.All(field.Items, i =>
        {
            Assert.True((i.Payload & WeaponPickups.Instant) != 0, "every CONFT pickup is instant");
            Assert.True(i.RetiresOnCollect, "every CONFT pickup retires on contact");
            Assert.Equal(-1, i.Amount);
            Assert.Equal(3, i.Period);
            Assert.True(i.ModelId > 0, "every CONFT pickup names a Cobj");
            Assert.Equal(819 << 4, i.HalfX);
            Assert.Equal(819 << 4, i.HalfY);
            Assert.Equal(2048 << 4, i.HalfZ);
            int? ground = LevelGround.PlacementHeight(map, i.X, i.Z, WeaponPickup.GroundRank(i.KindType));
            Assert.Equal(81 << 4, i.Y - (ground ?? 0));
        });
    }

    [RetailFact]
    public void RetailPickupsMatchTheArchiveBytes()
    {
        var mis = FindPrecinctAssault();
        Assert.NotNull(mis);
        var file = new MisFile(mis!);
        var map = LevelMap.FromMisFile(mis!);
        var field = WeaponPickups.FromMission(file.Bytes, WeaponCatalog.LoadRegistry(file), map);

        // Section, x, z and payload word of the 11 tACT type-0x10 chunks, in file order.
        var expected = new (int Section, int X, int Z, uint Payload)[]
        {
            (100, 958976, 877056, 0x03000001),    // ammunition slot 0
            (101, 958464, 828414, 0x03000002),    // ammunition slot 1
            (102, 1007616, 828926, 0x03000004),   // ammunition slot 2
            (103, 1006592, 877566, 0x03020000),   // gauge / health
            (284, 983552, 852492, 0x03000020),    // reserve slot 1
            (285, 651776, 1151494, 0x03000010),   // reserve slot 0
            (286, 1315840, 1152004, 0x03000040),  // reserve slot 2
            (291, 983040, 559612, 0x03040000),    // timer A
            (293, 1314816, 552966, 0x03000010),   // reserve slot 0
            (294, 651776, 553988, 0x03000040),    // reserve slot 2
            (298, 982784, 1145084, 0x03040000),   // timer A
        };
        Assert.Equal(expected.Length, field.Items.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            var item = field.Items[i];
            var (section, x, z, payload) = expected[i];
            Assert.Equal(section, item.SectionId);
            Assert.Equal(x, item.X);
            Assert.Equal(z, item.Z);
            Assert.Equal(payload, item.Payload);
            // func_8002FA20:111: the record's own y is discarded, the level supplies the ground.
            int? ground = LevelGround.PlacementHeight(map, x, z, WeaponPickup.GroundRank(item.KindType));
            Assert.Equal((ground ?? 0) + (81 << 4), item.Y);
        }
    }

    [RetailFact]
    public void RetailPickupFillsASpentSlotWhenTheMechWalksOverIt()
    {
        var mis = FindPrecinctAssault();
        Assert.NotNull(mis);
        var file = new MisFile(mis!);
        var map = LevelMap.FromMisFile(mis!);
        var registry = WeaponCatalog.LoadRegistry(file);
        var field = WeaponPickups.FromMission(file.Bytes, registry, map);

        // Difficulty 1 = WeaponSlots.DefaultDifficulty, the session value the port starts with.
        var player = new PlayerResupplyState { EntityType = 1, Gauge = 1, GaugeMax = 0x7F00 };
        player.Slots.Reset(WeaponSlots.DefaultSlotIds, registry);
        var ctx = ResupplyContext.Solo(player, RetailClock.TicksPerFrame);

        // Section 100 is the slot-0 ammunition pickup: 0x03000001 refills +0x84[0] to id 0x00's
        // record + 0x08 (6000) scaled by the difficulty.
        var ammo = field.Items.Single(i => i.SectionId == 100);
        player.Slots.Ammo[0] = 1;
        Assert.Equal(1, field.Update(ctx, ammo.X, ammo.Y, ammo.Z));
        Assert.Equal(7500, player.Slots.Ammo[0]);     // 6000 + 6000 / 4

        // Section 103 carries the gauge bit 0x00020000.
        var health = field.Items.Single(i => i.SectionId == 103);
        Assert.Equal(1, field.Update(ctx, health.X, health.Y, health.Z));
        Assert.Equal(0x7F00, player.Gauge);

        // Both retired (0x02000000), so 9 of the 11 remain.
        Assert.Equal(9, field.RemainingCount);
    }
}
