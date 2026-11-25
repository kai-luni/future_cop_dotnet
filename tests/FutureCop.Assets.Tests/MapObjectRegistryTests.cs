using System.Buffers.Binary;
using System.Globalization;
using FutureCop.Game;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Unit tests for <see cref="MapObjectRegistry"/> and <see cref="LiveObjectRegistry"/>
/// (D_800A0C70, D_800A7040, func_8003FA34 / func_8003BC0C / func_8003FAD4 / func_800402DC,
/// D_8009F800 and func_8004CD6C).
/// </summary>
public sealed class MapObjectRegistryTests
{
    private sealed class TestMapObject : IRegisteredMapObject
    {
        public int Id { get; set; }
        public int EntityType { get; set; }
        public List<(int Message, int Arg2, int Arg3)> Messages { get; } = new();

        public int OnMessage(int message, int arg2, int arg3)
        {
            Messages.Add((message, arg2, arg3));
            return 1;
        }
    }

    [Fact]
    public void SlotDescriptors_EnforceGuards_AndDefaultDestructionColumnTo0x5B()
    {
        var registry = new MapObjectRegistry();
        registry.InitMissionSpawnState();
        registry.SetSlotDescriptor(5, new SlotSpawnDescriptor(ColumnKind: 0x12, SpawnColumn: 0x34));
        registry.SetSlotDescriptor(6, new SlotSpawnDescriptor(ColumnKind: 0x20, SpawnColumn: 0x00));

        var ok = new MapOccupancySubject { TableIndex = 5, Flags = 0 };
        Assert.Equal(0x12, registry.GetSpawnDescColumn(ok));
        Assert.Equal(0x34, registry.GetDestructionSpawnColumn(ok));
        Assert.Equal(0x34, registry.ResolveDestructionSpawnColumn(ok));

        // Slot 0 is unassigned (8003B838.c:56, 8003B7D8.c:44).
        var slot0 = new MapOccupancySubject { TableIndex = 0 };
        Assert.Equal(0, registry.GetSpawnDescColumn(slot0));
        Assert.Equal(0, registry.GetDestructionSpawnColumn(slot0));
        Assert.Equal(MapObjectRegistry.DefaultDestructionSpawnColumn, registry.ResolveDestructionSpawnColumn(slot0));

        // Flag 0x40000 masks both column lookups (8003B838.c:59, 8003B7D8.c:47).
        var masked = new MapOccupancySubject { TableIndex = 5, Flags = MapOccupancySubject.SelfPositionedFlag };
        Assert.Equal(0, registry.GetSpawnDescColumn(masked));
        Assert.Equal(0, registry.GetDestructionSpawnColumn(masked));
        Assert.Equal(0x5B, registry.ResolveDestructionSpawnColumn(masked));

        // Unpopulated slot returns 0.
        var emptySlot = new MapOccupancySubject { TableIndex = 9 };
        Assert.Equal(0, registry.GetSpawnDescColumn(emptySlot));
        Assert.Equal(0, registry.GetDestructionSpawnColumn(emptySlot));

        // Entry[1] == 0 defaults to 0x5B via ResolveDestructionSpawnColumn.
        var zeroSecondary = new MapOccupancySubject { TableIndex = 6 };
        Assert.Equal(0x20, registry.GetSpawnDescColumn(zeroSecondary));
        Assert.Equal(0, registry.GetDestructionSpawnColumn(zeroSecondary));
        Assert.Equal(0x5B, registry.ResolveDestructionSpawnColumn(zeroSecondary));
    }

    [Fact]
    public void QueueOrApply_ImmediateWhenPoolAbsentOrScreenState4Or0xD()
    {
        var spawned = new List<(int Id, byte Column, int Row)>();
        var registry = new MapObjectRegistry
        {
            SpawnFactory = (owner, col, row, _, _, _) =>
            {
                spawned.Add((owner!.Id, col, row));
                return 1;
            },
        };

        // 1. No deferral pool (HasDeferralPool == false) -> immediate.
        registry.SetSlotDescriptor(3, new SlotSpawnDescriptor(ColumnKind: 0x10, SpawnColumn: 0));
        registry.SetSpawnCell(row: 0, column: 0x10, value: 1);
        registry.SetSpawnCell(row: 1, column: 0x10, value: 1);
        registry.ScreenState = 1;

        var e1 = new MapOccupancySubject { Id = 101, EntityType = 5, TableIndex = 3 };
        Assert.True(registry.QueueOrApplyMapOccupancy(e1));
        Assert.Equal(0, registry.DeferredQueueCount);
        Assert.Single(spawned);
        Assert.Equal((101, (byte)0x10, 0), spawned[0]);

        // 2. Pool allocated, screen state 4 -> immediate.
        registry.InitMissionSpawnState();
        registry.SetSlotDescriptor(3, new SlotSpawnDescriptor(ColumnKind: 0x10, SpawnColumn: 0));
        registry.SetSpawnCell(row: 1, column: 0x10, value: 1);
        registry.ScreenState = MapObjectRegistry.ScreenStateImmediatePlay;

        var e2 = new MapOccupancySubject { Id = 102, EntityType = 1, TableIndex = 3 };
        Assert.True(registry.QueueOrApplyMapOccupancy(e2));
        Assert.Equal(0, registry.DeferredQueueCount);
        Assert.Equal(2, spawned.Count);
        Assert.Equal((102, (byte)0x10, 1), spawned[1]);

        // 3. Pool allocated, screen state 0xD -> immediate.
        registry.ScreenState = MapObjectRegistry.ScreenStateImmediateAlt;
        var e3 = new MapOccupancySubject { Id = 103, EntityType = 0x12, TableIndex = 3 };
        Assert.True(registry.QueueOrApplyMapOccupancy(e3));
        Assert.Equal(0, registry.DeferredQueueCount);
        Assert.Equal(3, spawned.Count);
        Assert.Equal((103, (byte)0x10, 1), spawned[2]);
    }

    [Fact]
    public void QueueOrApply_DeferredWhileLoading_ReplaysNewestFirstAtFinishLevelLoad()
    {
        var appliedOrder = new List<int>();
        var registry = new MapObjectRegistry
        {
            SpawnFactory = (owner, _, _, _, _, _) =>
            {
                appliedOrder.Add(owner!.Id);
                return 1;
            },
        };
        registry.InitMissionSpawnState();
        registry.ScreenState = 2; // loading state (neither 4 nor 0xD)
        registry.SetSlotDescriptor(1, new SlotSpawnDescriptor(ColumnKind: 0x0A, SpawnColumn: 0));
        registry.SetSpawnCell(row: 0, column: 0x0A, value: 1);

        var first = new MapOccupancySubject { Id = 1, EntityType = 5, TableIndex = 1 };
        var second = new MapOccupancySubject { Id = 2, EntityType = 5, TableIndex = 1 };
        var masked = new MapOccupancySubject { Id = 3, EntityType = 5, TableIndex = 1, Flags = MapOccupancySubject.SelfPositionedFlag };

        Assert.False(registry.QueueOrApplyMapOccupancy(first));
        Assert.False(registry.QueueOrApplyMapOccupancy(second));
        // Masked entity has GetSpawnDescColumn == 0 so it is neither applied nor queued (8003FA34.c:60).
        Assert.False(registry.QueueOrApplyMapOccupancy(masked));

        Assert.Empty(appliedOrder);
        Assert.Equal(2, registry.DeferredQueueCount);
        Assert.Equal(MapObjectRegistry.DeferredPoolCapacity - 2, registry.DeferredPoolRemaining);

        // Replay at load end (8003FAD4.c): LIFO order (newest first), then pool & list cleared.
        int replayed = registry.FinishLevelLoad();
        Assert.Equal(2, replayed);
        Assert.Equal(new[] { 2, 1 }, appliedOrder);
        Assert.False(registry.HasDeferralPool);
        Assert.Equal(0, registry.DeferredQueueCount);
        Assert.Equal(0, registry.DeferredPoolRemaining);
    }

    [Fact]
    public void SpawnGate_RejectsEmptyCell_AndDefersWhenCellFlagsMatch4()
    {
        int factoryCalls = 0;
        var registry = new MapObjectRegistry
        {
            SpawnFactory = (_, _, _, _, _, _) =>
            {
                factoryCalls++;
                return 42;
            },
        };
        registry.InitMissionSpawnState();
        var ownerWithDefer = new MapOccupancySubject { Id = 10, DeferSpawnWord = 1 };
        var ownerNoDefer = new MapOccupancySubject { Id = 11, DeferSpawnWord = 0 };

        // Empty cell D_800A7040[0][0x22] == 0 -> rejected (800402DC.c:81-83).
        Assert.Equal(0, registry.TrySpawnAtOwner(ownerWithDefer, column: 0x22, arg2: 0, row: 0, arg4: 0));
        Assert.Equal(0, factoryCalls);
        Assert.Empty(registry.DeferredSpawnRequests);

        // Populate cell, set flags (word & 5) == 4 -> deferred path (800402DC.c:84-91).
        registry.SetSpawnCell(row: 0, column: 0x22, value: unchecked((int)0x800B0000));
        registry.SetCellFlags(row: 0, column: 0x22, flags: 4);

        Assert.Equal(0, registry.TrySpawnAtOwner(null, column: 0x22, arg2: 0, row: 0, arg4: 0));
        Assert.Empty(registry.DeferredSpawnRequests);

        Assert.Equal(0, registry.TrySpawnAtOwner(ownerNoDefer, column: 0x22, arg2: 0, row: 0, arg4: 0));
        Assert.Empty(registry.DeferredSpawnRequests);

        Assert.Equal(0, registry.TrySpawnAtOwner(ownerWithDefer, column: 0x22, arg2: 0, row: 0, arg4: 0));
        var req = Assert.Single(registry.DeferredSpawnRequests);
        Assert.Equal((byte)0x22, req.Column);
        Assert.Equal(0, req.Row);
        Assert.Same(ownerWithDefer, req.Owner);
        Assert.Equal(0, factoryCalls);

        // Clear flag bit 2 (or set bit 0 so (flags & 5) != 4) -> spawns directly.
        registry.SetCellFlags(row: 0, column: 0x22, flags: 5);
        Assert.Equal(42, registry.TrySpawnAtOwner(ownerWithDefer, column: 0x22, arg2: 0, row: 0, arg4: 0));
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public void GetEntitySpawnBank_MatchesRetailLinkRules()
    {
        var playerClass = new MapOccupancySubject { EntityType = 0x12 };
        var otherClass = new MapOccupancySubject { EntityType = 5 };

        Assert.Equal(0, MapObjectRegistry.GetEntitySpawnBank(null));
        Assert.Equal(1, MapObjectRegistry.GetEntitySpawnBank(new MapOccupancySubject { EntityType = 1 }));
        Assert.Equal(1, MapObjectRegistry.GetEntitySpawnBank(playerClass));
        Assert.Equal(1, MapObjectRegistry.GetEntitySpawnBank(new MapOccupancySubject { EntityType = 7, LinkC0 = playerClass }));
        Assert.Equal(0, MapObjectRegistry.GetEntitySpawnBank(new MapOccupancySubject { EntityType = 7, LinkC0 = otherClass }));
        Assert.Equal(1, MapObjectRegistry.GetEntitySpawnBank(new MapOccupancySubject { EntityType = 2, LinkC8 = playerClass }));
        Assert.Equal(1, MapObjectRegistry.GetEntitySpawnBank(new MapOccupancySubject { EntityType = 0x0F, LinkC8 = playerClass }));
        Assert.Equal(0, MapObjectRegistry.GetEntitySpawnBank(new MapOccupancySubject { EntityType = 0x0F, LinkC8 = otherClass }));
        Assert.Equal(0, MapObjectRegistry.GetEntitySpawnBank(otherClass));
    }

    [Fact]
    public void LiveObjectRegistry_Enforces0x80Capacity_Unregister_Free_AndMessage6Broadcast()
    {
        var live = new LiveObjectRegistry();
        Assert.False(live.IsAllocated);
        Assert.False(live.Register(new TestMapObject { Id = 1 }));

        live.Allocate();
        Assert.True(live.IsAllocated);
        Assert.Equal(0, live.Count);

        var all = new List<TestMapObject>();
        for (int i = 0; i < LiveObjectRegistry.Capacity; i++)
        {
            var obj = new TestMapObject { Id = i + 1, EntityType = 5 };
            all.Add(obj);
            Assert.True(live.Register(obj));
        }
        Assert.Equal(0x80, live.Count);
        // 129th object is rejected (0x80-slot capacity).
        Assert.False(live.Register(new TestMapObject { Id = 999 }));
        Assert.Equal(0x80, live.Count);

        // Unregister frees a slot so a new registration succeeds.
        Assert.True(live.Unregister(50));
        Assert.Equal(0x7F, live.Count);
        Assert.False(live.Unregister(50));

        var inactive = new TestMapObject { Id = 0, EntityType = 5 };
        Assert.True(live.Register(inactive));
        Assert.Equal(0x80, live.Count);

        // Message-6 broadcast invokes only entries with Id > 0 (8004CD6C.c:50).
        int delivered = live.BroadcastMessage6(dt: 8);
        Assert.Equal(0x7F, delivered);
        Assert.Empty(inactive.Messages);
        Assert.Single(all[0].Messages);
        Assert.Equal((6, 8, 0), all[0].Messages[0]);

        // Teardown (func_80060E50) releases the table and zeroes Count.
        live.Free();
        Assert.False(live.IsAllocated);
        Assert.Equal(0, live.Count);
        Assert.Empty(live.Entries);
        Assert.Equal(0, live.BroadcastMessage6(8));
    }

    public static IEnumerable<object[]> MapOccupancyGoldenRows()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "map-occupancy.vectors");
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 1 + 12 + 1 + 9 || words[13] != "->")
                throw new FormatException($"Invalid map-occupancy vector: {line}");
            yield return new object[]
            {
                words[0],
                Array.ConvertAll(words[1..13], s => int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
                Array.ConvertAll(words[14..], s => int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
            };
        }
    }

    [Theory]
    [MemberData(nameof(MapOccupancyGoldenRows))]
    public void MapOccupancyRegistry_MatchesOriginalMips(string name, int[] input, int[] expected)
    {
        var spawns = new List<(int Id, byte Col, int Bank)>();
        var registry = new MapObjectRegistry
        {
            SpawnFactory = (owner, col, row, _, _, _) =>
            {
                spawns.Add((owner?.Id ?? -1, col, row));
                return 1;
            },
        };
        registry.InitMissionSpawnState();
        registry.ScreenState = input[0];
        if (input[1] != 0) registry.FinishLevelLoad(); // drop deferral pool

        if (input[2] != 0 && (input[3] != 0 || input[4] != 0))
        {
            var desc = new byte[0x30];
            desc[0x14] = (byte)input[2];
            desc[0x18] = (byte)input[3];
            desc[0x19] = (byte)input[4];
            Assert.True(registry.SetSlotDescriptorFromChunk(desc));
            // Allow TrySpawnAtOwner to pass D_800A7040 check for the configured column on both banks.
            if (input[3] != 0)
            {
                registry.SetSpawnCell(0, (byte)input[3], 1);
                registry.SetSpawnCell(1, (byte)input[3], 1);
            }
        }

        if (input[9] != 0)
        {
            var rec = new byte[0x50];
            rec[0x04] = (byte)input[9];
            rec[0x14] = 10;
            rec[0x15] = (byte)input[10];
            BinaryPrimitives.WriteInt32LittleEndian(rec.AsSpan(0x20), input[11]);
            registry.SpawnTableRow = 0;
            registry.InstallSpawnRecord(rec);
            if (input[10] != 0)
            {
                var rec2 = new byte[0x50];
                rec2[0x04] = (byte)input[9];
                rec2[0x14] = (byte)input[10];
                rec2[0x15] = 10;
                BinaryPrimitives.WriteInt32LittleEndian(rec2.AsSpan(0x20), input[11]);
                registry.InstallSpawnRecord(rec2);
            }
        }

        var linked = new MapOccupancySubject { EntityType = (byte)input[8] };
        var obj1 = new MapOccupancySubject
        {
            Id = 101,
            EntityType = (byte)input[5],
            TableIndex = (byte)input[2],
            Flags = unchecked((uint)input[6]),
            LinkC0 = input[7] == 1 ? linked : null,
            LinkC8 = input[7] == 2 ? linked : null,
        };
        var obj2 = new MapOccupancySubject
        {
            Id = 202,
            EntityType = obj1.EntityType,
            TableIndex = obj1.TableIndex,
            Flags = obj1.Flags,
            LinkC0 = obj1.LinkC0,
            LinkC8 = obj1.LinkC8,
        };

        int col = registry.GetSpawnDescColumn(obj1);
        int destCol = registry.GetDestructionSpawnColumn(obj1);
        int bank = MapObjectRegistry.GetEntitySpawnBank(obj1);

        registry.QueueOrApplyMapOccupancy(obj1);
        registry.QueueOrApplyMapOccupancy(obj2);

        int immCount = spawns.Count;
        int immCol = immCount > 0 ? spawns[0].Col : 0;
        int immBank = immCount > 0 ? spawns[0].Bank : 0;

        spawns.Clear();
        registry.FinishLevelLoad();

        int replayCount = spawns.Count;
        int replayFirstId = replayCount > 0 ? spawns[0].Id : 0;
        int cellFlag4 = input[9] != 0 && (registry.GetCellFlags(0, 10) & 4) != 0 ? 1 : 0;

        int[] actual = { col, destCol, bank, immCount, immCol, immBank, replayCount, replayFirstId, cellFlag4 };
        Assert.True(actual.SequenceEqual(expected),
            $"{name}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }

    [RetailFact]
    public void LevelScene_LoadsMissionSlotDescriptorsAndSpawnCellsIntoObjectRegistry()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        var scene = new LevelScene(path);
        scene.EnableObjectives();

        var reg = scene.ObjectRegistry;
        // CONFT.MIS carries 18 Csfx 0x5B descriptors + 7 Cact 0x5B descriptors (25 total, some shared slots).
        int populatedSlots = Enumerable.Range(0, MapObjectRegistry.SlotTableSize)
            .Count(s => reg.GetSlotDescriptor((byte)s) is not null);
        int populatedBank0 = Enumerable.Range(0, MapObjectRegistry.SlotTableSize)
            .Count(c => reg.GetSpawnCell(0, (byte)c) != 0);
        int populatedBank1 = Enumerable.Range(0, MapObjectRegistry.SlotTableSize)
            .Count(c => reg.GetSpawnCell(1, (byte)c) != 0);

        Assert.True(populatedSlots >= 18, $"Expected >= 18 D_800A0C70 descriptors from CONFT.MIS, got {populatedSlots}");
        Assert.True(populatedBank0 > 0, "Expected populated bank-0 D_800A7040 cells from CONFT.MIS");
        Assert.True(populatedBank1 > 0, "Expected populated bank-1 D_800A7040 cells from CONFT.MIS");
        Assert.False(reg.HasDeferralPool);
        Assert.Equal(MapObjectRegistry.ScreenStateImmediatePlay, reg.ScreenState);
    }
}
