using FutureCop.Game;
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
}
