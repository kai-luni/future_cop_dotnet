using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The 0x3D dispatch of <see cref="PurchasePadSystem"/> against unchanged retail
/// <c>8004DA84.c</c> (gSendEntityCommand: type filter and slot storage) and
/// <c>8004CECC.c:108-114</c> (the entry pass that applies the pair and retires).
/// docs/todo/05-entity-command-queue.md.
/// </summary>
public sealed class EntityCommandQueueTests
{
    // ---- fixtures --------------------------------------------------------------------------

    private sealed class ProbeEntity : LiveEntity
    {
        public ProbeEntity(int recordId, int type, int retireScript = 0)
        {
            RecordIdValue = recordId;
            TypeValue = type;
            RetireScript = retireScript;
        }

        public int RecordIdValue { get; }
        public int TypeValue { get; }
        public override int RecordId => RecordIdValue;
        public override int DispatchType => TypeValue;
        public override (int X, int Z) GroundPosition => (0, 0);
        public override int HandleMessage(int message, EntityWorld world, object? data) =>
            BaseHandler(message, this, world, data);
    }

    /// <summary>An empty pool; these tests drive the command path, not a mission script.</summary>
    private static PurchasePadSystem NewSystem(EntityWorld world) =>
        new(new RpnsPool(new byte[] { 0 }), world: world);

    // ---- queue semantics -------------------------------------------------------------------

    /// <summary>
    /// A 0x3D issued from a running script is stored in the entity's entry slot
    /// (8004DA84.c:57-63) and only retires on the entry pass of 8004CECC.c:108-114: the retire
    /// script cannot observe itself running inside the issuing script.
    /// </summary>
    [Fact]
    public void ScriptRetireIsQueuedAndRunsAfterTheScriptReturns()
    {
        // push tag 3, kind 0x3D, value 0, op 0x38 (gSendCommandByTag) - 80069D68.c:382-384.
        var pool = new RpnsPool(new byte[] { 0x83, 0xBD, 0x80, 0x38, 0x00 });
        var world = new EntityWorld();
        var pads = new PurchasePadSystem(pool, world: world);
        var entity = world.Spawn(new ProbeEntity(recordId: 3, type: 14, retireScript: 1));
        // The entry pass walks the frame's active table D_800A8F90, so the entity has to be in it.
        world.CullActive(0, 0);

        bool inScript = false;
        int retireCalls = 0;
        bool nested = false;
        world.OnScript = (_, _, _, _) =>
        {
            retireCalls++;
            nested |= inScript;
        };

        inScript = true;
        RpnsScriptVm.Execute(pool.Slice(0), pads.VmState);
        inScript = false;

        // The pair is queued, nothing retired yet, and the entity is still linked.
        Assert.Equal(0, retireCalls);
        Assert.Equal(1, pads.QueuedCommandCount(entity));
        Assert.NotEqual(0, entity.RuntimeId);
        Assert.Equal(0u, entity.Flags & LiveEntity.ResetFlag);

        // The frame pass applies it exactly once, outside the script.
        Assert.Equal(1, pads.RunEntityCommands(world));
        Assert.Equal(1, retireCalls);
        Assert.False(nested);
        Assert.Equal(0, entity.RuntimeId);
        Assert.Equal(LiveEntity.ResetFlag, entity.Flags & LiveEntity.ResetFlag);
        Assert.Equal(0u, entity.Flags & LiveEntity.NoRetireScriptFlag);
        Assert.Equal(0, pads.QueuedCommandCount(entity));

        // A second pass finds nothing left; the retire is not repeated.
        Assert.Equal(0, pads.RunEntityCommands(world));
        Assert.Equal(1, retireCalls);
    }

    /// <summary>
    /// The value 1 of the pair sets the no-retire-script bit before gRetireEntity
    /// (8004CECC.c:110-113), so the retire script of 8004D958.c:53 is skipped.
    /// </summary>
    [Fact]
    public void ValueOneSuppressesTheRetireScript()
    {
        var world = new EntityWorld();
        var pads = NewSystem(world);
        var entity = world.Spawn(new ProbeEntity(recordId: 4, type: 14, retireScript: 1));
        world.CullActive(0, 0);
        int retireCalls = 0;
        world.OnScript = (_, _, _, _) => retireCalls++;

        Assert.True(pads.SendEntityCommand(entity, 0x3D, 1));
        Assert.Equal(1, pads.RunEntityCommands(world));

        Assert.Equal(0, retireCalls);
        Assert.Equal(0, entity.RuntimeId);
        Assert.Equal(LiveEntity.ResetFlag | LiveEntity.NoRetireScriptFlag,
            entity.Flags & (LiveEntity.ResetFlag | LiveEntity.NoRetireScriptFlag));
    }

    /// <summary>
    /// Types 2/3/4/7/15/23 report success without storing the pair (8004DA84.c:32-38), so the
    /// command is dropped and the entity keeps running.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(15)]
    [InlineData(23)]
    public void FilteredDispatchTypesDropTheRetireCommand(int type)
    {
        var world = new EntityWorld();
        var pads = NewSystem(world);
        var entity = world.Spawn(new ProbeEntity(recordId: 5, type: type));
        int retireCalls = 0;
        world.OnScript = (_, _, _, _) => retireCalls++;

        Assert.True(pads.SendEntityCommand(entity, 0x3D, 0));
        Assert.Equal(0, pads.QueuedCommandCount(entity));
        Assert.Equal(0, pads.RunEntityCommands(world));
        Assert.NotEqual(0, entity.RuntimeId);
        Assert.Equal(0u, entity.Flags & LiveEntity.ResetFlag);
        Assert.Equal(0, retireCalls);
    }

    /// <summary>
    /// Types 18 and 21 take only the queued kinds of gIsQueuedCommandKind
    /// (8004DA58.c:26-39): 0x3D (=61) is inside 0x3C..0x45 and passes, 0x50 (=80) is in neither
    /// range and is dropped with the same immediate success.
    /// </summary>
    [Theory]
    [InlineData(18)]
    [InlineData(21)]
    public void Types18And21AcceptOnlyQueuedKinds(int type)
    {
        var world = new EntityWorld();
        var pads = NewSystem(world);
        var entity = world.Spawn(new ProbeEntity(recordId: 6, type: type));
        world.CullActive(0, 0);

        Assert.True(pads.SendEntityCommand(entity, 0x50, 0));
        Assert.Equal(0, pads.QueuedCommandCount(entity));

        Assert.True(pads.SendEntityCommand(entity, 0x3D, 0));
        Assert.Equal(1, pads.QueuedCommandCount(entity));
        Assert.Equal(1, pads.RunEntityCommands(world));
        Assert.Equal(0, entity.RuntimeId);
    }

    /// <summary>
    /// A full four-slot table falls back to the 16-entry message table with the target id and a
    /// 600-tick timer (8004DA84.c:65-68, 8004D9EC.c:52-68); without retry the call reports failure
    /// instead.
    /// </summary>
    [Fact]
    public void FullSlotTableUsesThePendingMessageTable()
    {
        var world = new EntityWorld();
        var pads = NewSystem(world);
        var entity = world.Spawn(new ProbeEntity(recordId: 7, type: 14));

        // 0x50 has no handler in this port, so the four slots stay occupied.
        for (int i = 0; i < EntityCommandQueue.SlotCount; i++)
            Assert.True(pads.SendEntityCommand(entity, 0x50, 0));

        Assert.Equal(EntityCommandQueue.SlotCount, pads.QueuedCommandCount(entity));
        Assert.False(pads.SendEntityCommand(entity, 0x3D, 0, retry: false)); // 8004DA84.c:65-68
        Assert.Equal(0, pads.PendingCommandCount);

        Assert.True(pads.SendEntityCommand(entity, 0x3D, 0));
        Assert.Equal(1, pads.PendingCommandCount);

        // The timer is armed to 600: no delivery yet, and the entity stays alive.
        Assert.Equal(0, pads.RunEntityCommands(world, elapsedTicks: 0));
        Assert.Equal(1, pads.PendingCommandCount);
        Assert.NotEqual(0, entity.RuntimeId);
    }

    /// <summary>
    /// func_8002A5B4 closes the gap of a consumed entry and clears the last slot; the pass reads the
    /// same index again, which is how one entity consumes up to four pairs per frame
    /// (8004CECC.c:232-240).
    /// </summary>
    [Fact]
    public void ConsumedEntryCompactsTheSlotTable()
    {
        var queue = new EntityCommandQueue();
        Assert.True(queue.TryEnqueue(0x3D, 0));
        Assert.True(queue.TryEnqueue(0x50, 1));
        Assert.True(queue.TryEnqueue(0x51, 2));
        Assert.True(queue.TryEnqueue(0x52, 3));
        Assert.False(queue.TryEnqueue(0x53, 4));
        Assert.Equal(4, queue.Count);
        Assert.Equal(((byte)0x3D, (byte)0), queue[0]);

        queue.RemoveAt(0);

        Assert.Equal(3, queue.Count);
        Assert.Equal(((byte)0x50, (byte)1), queue[0]);
        Assert.Equal(((byte)0x51, (byte)2), queue[1]);
        Assert.Equal(((byte)0x52, (byte)3), queue[2]);
        Assert.True(queue.TryEnqueue(0x53, 4));
        Assert.Equal(((byte)0x53, (byte)4), queue[3]);
    }
}
