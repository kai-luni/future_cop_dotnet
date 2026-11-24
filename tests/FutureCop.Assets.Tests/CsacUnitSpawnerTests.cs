using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Unit and scene-integration tests for <see cref="CsacUnitSpawner"/>
/// (func_80060E74, func_8006158C, func_800614BC, func_800616A0, func_8006125C, func_8002AE54).
/// </summary>
public sealed class CsacUnitSpawnerTests
{
    private static EntityRecord MakeRecord(
        int id,
        int type = 5,
        int team = 2,
        int modelId = 30,
        EntityContainer container = EntityContainer.Csac,
        int[]? scripts = null) =>
        new(id, type, 0x100000, 0x10000, 0x200000, 0, 0x100, team, container, 0, 0, new byte[0x54],
            scripts ?? new[] { 0x10, 0x20, 0x30 },
            new[] { new ResourceReference("Cobj", modelId) });

    private static string? ConftPath()
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc == null ? null : ModulePath.Resolve(disc, "conft.mis");
        if (path == null) Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
        return path;
    }

    [Fact]
    public void Register_Enforces0x80Capacity_IgnoresCact_AndScalesRespawnDelay()
    {
        var spawner = new CsacUnitSpawner(random: new RetailRandom(0x1234));

        // Cact records are ignored by CsacUnitSpawner.
        Assert.Null(spawner.Register(MakeRecord(1, container: EntityContainer.Cact)));
        Assert.Equal(0, spawner.Count);

        // Negative raw delay: negated and used directly as initial timer (80060E74.c:129-132).
        var negSlot = spawner.Register(MakeRecord(10, type: 5, modelId: 30),
            new CsacSpawnerParameters(RespawnDelayRaw: -12, MaxConcurrent: 3, InitialFlags: 0, InitialCount: 5));
        Assert.NotNull(negSlot);
        Assert.Equal(EntityHandlerKind.Type5Vehicle, negSlot!.Handler);
        Assert.Equal(30, negSlot.ModelId);
        Assert.Equal(60, negSlot.RespawnDelay);
        Assert.Equal(60, negSlot.Timer);
        Assert.Equal(0, negSlot.TotalSpawned);

        // Positive raw delay: initial timer is PRNG % (raw * 5) (80060E74.c:134-135).
        var posSlot = spawner.Register(MakeRecord(11, type: 9, modelId: 22),
            new CsacSpawnerParameters(RespawnDelayRaw: 20, MaxConcurrent: 2, InitialFlags: 0, InitialCount: -1));
        Assert.NotNull(posSlot);
        Assert.Equal(EntityHandlerKind.TumblingFlyer, posSlot!.Handler);
        Assert.Equal(100, posSlot.RespawnDelay);
        Assert.InRange(posSlot.Timer, 0, 99);

        // Fill remaining slots up to 0x80; 129th must be rejected.
        for (int i = 2; i < CsacUnitSpawner.Capacity; i++)
            Assert.NotNull(spawner.Register(MakeRecord(100 + i)));
        Assert.Equal(0x80, spawner.Count);
        Assert.Null(spawner.Register(MakeRecord(999)));
        Assert.Equal(0x80, spawner.Count);
    }

    [Fact]
    public void Drain_FiresDueSpawns_RegistersIntoLiveRegistry_AndBroadcastsMessage6()
    {
        var registry = new MapObjectRegistry();
        registry.InitMissionSpawnState();
        registry.LiveObjects.Allocate();

        var spawner = new CsacUnitSpawner(registry);
        var slot = spawner.Register(MakeRecord(42, type: 0x1C, team: 2, modelId: 37),
            new CsacSpawnerParameters(RespawnDelayRaw: -4, MaxConcurrent: 2, InitialFlags: 0, InitialCount: 3))!;
        // RespawnDelay = 20, Timer = 20.

        // Not due yet after 15 ticks (timer = 5 >= 0).
        Assert.Equal(0, spawner.Drain(15));
        Assert.Equal(5, slot.Timer);
        Assert.Empty(spawner.SpawnedUnits);
        Assert.Equal(0, registry.LiveObjects.Count);

        // Underflow on next 6 ticks (5 - 6 = -1 < 0 -> +20 = 19, spawns 1).
        Assert.Equal(1, spawner.Drain(6));
        Assert.Equal(19, slot.Timer);
        Assert.Equal(1, slot.TotalSpawned);
        Assert.Equal(2, slot.RemainingCount);
        Assert.Equal(1, registry.LiveObjects.Count);

        var unit = Assert.Single(spawner.SpawnedUnits);
        Assert.Equal(0x1C, unit.EntityType);
        Assert.Equal(EntityHandlerKind.Type28MultiTurret, unit.Handler);
        Assert.Equal(37, unit.ModelId);
        Assert.Equal(2, unit.Team);

        // Broadcast message 6 reaches the spawned live unit.
        Assert.Equal(1, registry.BroadcastMessage6(dt: 8));
        Assert.Equal(1, unit.Message6Count);
        Assert.Equal(8, unit.LastDelta);

        // Second spawn hits MaxConcurrent = 2; third drain does not spawn past cap.
        Assert.Equal(1, spawner.Drain(20));
        Assert.Equal(2, slot.TotalSpawned);
        Assert.Equal(1, slot.RemainingCount);
        Assert.Equal(0, spawner.Drain(50));
        Assert.Equal(2, slot.TotalSpawned);
    }

    [Fact]
    public void Commands_0x46And0x47_ControlPauseAndBurstSpawns()
    {
        var spawner = new CsacUnitSpawner();
        var slot = spawner.Register(MakeRecord(77, type: 5),
            new CsacSpawnerParameters(RespawnDelayRaw: -10, MaxConcurrent: 5, InitialFlags: 1, InitialCount: 5))!;

        // Bit 0 set -> timer paused.
        Assert.Equal(0, spawner.Drain(100));
        Assert.Equal(50, slot.Timer);

        // Command 0x47 with arg2 != 0 queues mode-1 burst; arg2 == 0 queues mode-0 burst.
        Assert.True(spawner.ApplyCommandById(77, CsacUnitSpawner.CommandQueueBurst, arg2: 1));
        Assert.True(spawner.ApplyCommandById(77, CsacUnitSpawner.CommandQueueBurst, arg2: 0));
        Assert.Equal(2, spawner.Drain(0));
        Assert.Equal(0, slot.Flags & 0xFF00); // upper byte cleared by 800616A0.c:69
        Assert.Equal(1, spawner.SpawnedUnits[0].SpawnMode);
        Assert.Equal(0, spawner.SpawnedUnits[1].SpawnMode);

        // Command 0x46 with arg2 != 0 clears bit 0 and zeroes Timer (800614BC.c:49-52).
        Assert.True(spawner.ApplyCommandById(77, CsacUnitSpawner.CommandSetEnabled, arg2: 1));
        Assert.Equal(0, slot.Flags & 1);
        Assert.Equal(0, slot.Timer);
        Assert.Equal(1, spawner.Drain(1));
        Assert.Equal(3, slot.TotalSpawned);
    }

    [Fact]
    public void GatePredicate_DefaultsToAccept_AndCanRejectSpawns()
    {
        var spawner = new CsacUnitSpawner();
        var slot = spawner.Register(MakeRecord(88, type: 5),
            new CsacSpawnerParameters(RespawnDelayRaw: -1, MaxConcurrent: 2, InitialFlags: 0, InitialCount: 2))!;

        // Rejecting predicate prevents spawn and leaves counters untouched (8006125C.c:47).
        spawner.GatePredicate = (_, _) => false;
        Assert.Equal(0, spawner.Drain(10));
        Assert.Equal(0, slot.TotalSpawned);
        Assert.Equal(2, slot.RemainingCount);
        Assert.Empty(spawner.SpawnedUnits);

        // Accepting predicate allows spawn.
        spawner.GatePredicate = (_, _) => true;
        Assert.Equal(1, spawner.Drain(10));
        Assert.Equal(1, slot.TotalSpawned);
        Assert.Equal(1, slot.RemainingCount);
        Assert.Single(spawner.SpawnedUnits);
    }

    [Fact]
    public void LevelScene_WiresObjectRegistryAndCsacSpawner_WhenRetailConftPresent()
    {
        string? path = ConftPath();
        if (path == null) return;

        var scene = new LevelScene(path);
        Assert.False(scene.EnableCsacSpawner);
        scene.EnableObjectives();

        Assert.NotNull(scene.CsacSpawner);
        Assert.True(scene.CsacSpawner!.Count > 0, "expected CONFT.MIS to register Csac spawner slots");
        Assert.True(scene.ObjectRegistry.LiveObjects.IsAllocated);
        Assert.False(scene.ObjectRegistry.HasDeferralPool); // FinishLevelLoad ran

        // Disabled by default: AdvanceSkyCaptain does not drain CsacSpawner.
        scene.AdvanceSkyCaptain(RetailClock.LocomotionTicks);
        Assert.Empty(scene.CsacSpawner.SpawnedUnits);

        // Opt in: enabling drains CsacSpawner and broadcasts message 6 without throwing.
        scene.EnableCsacSpawner = true;
        scene.AdvanceSkyCaptain(RetailClock.LocomotionTicks);
    }
}
