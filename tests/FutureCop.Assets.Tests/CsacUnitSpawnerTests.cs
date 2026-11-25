using System.Globalization;
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

    [ConftFact]
    public void LevelScene_WiresObjectRegistryAndCsacSpawner_WhenRetailConftPresent()
    {
        string path = LevelSceneRetirementTests.FindMission()!;

        var scene = new LevelScene(path);
        Assert.False(scene.EnableCsacSpawner);
        scene.EnableObjectives();

        Assert.NotNull(scene.CsacSpawner);
        Assert.True(scene.CsacSpawner!.Count > 0, "expected CONFT.MIS to register Csac spawner slots");
        Assert.True(scene.ObjectRegistry.LiveObjects.IsAllocated);
        Assert.False(scene.ObjectRegistry.HasDeferralPool); // FinishLevelLoad ran

        // Disabled by default: AdvanceSkyCaptain does not drain the queued helicopter.
        Assert.True(scene.CsacSpawner.ApplyCommandById(34, CsacUnitSpawner.CommandQueueBurst, 0));
        scene.AdvanceSkyCaptain(RetailClock.LocomotionTicks);
        Assert.Empty(scene.CsacSpawner.SpawnedUnits);

        // Opt in: the burst drains, and the new unit receives message 6 on the following tick.
        scene.EnableCsacSpawner = true;
        scene.AdvanceSkyCaptain(RetailClock.LocomotionTicks);
        var unit = Assert.Single(scene.CsacSpawner.SpawnedUnits.Where(u => u.SourceSlot.Record.Id == 34));
        scene.AdvanceSkyCaptain(RetailClock.LocomotionTicks);
        Assert.Equal(1, unit.Message6Count);
        Assert.Equal(RetailClock.LocomotionTicks, unit.LastDelta);
    }

    public static IEnumerable<object[]> CsacSpawnerGoldenRows()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "csac-spawner.vectors");
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 1 + 13 + 1 + 9 || words[14] != "->")
                throw new FormatException($"Invalid csac-spawner vector: {line}");
            yield return new object[]
            {
                words[0],
                Array.ConvertAll(words[1..14], s => int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
                Array.ConvertAll(words[15..], s => int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
            };
        }
    }

    [Theory]
    [MemberData(nameof(CsacSpawnerGoldenRows))]
    public void CsacUnitSpawner_MatchesOriginalMips(string name, int[] input, int[] expected)
    {
        var source = new byte[0x54];
        source[0x1C] = (byte)input[10];
        source[0x1D] = (byte)input[11];
        var record = new EntityRecord(77, input[9], 0, 0, 0, 0, 0x100, input[10], EntityContainer.Csac,
            0, 0, source, new[] { 0, 0, 0 }, new[] { new ResourceReference("Cobj", 30) });

        var spawner = new CsacUnitSpawner
        {
            SessionActive = input[12],
            SinglePlayerTeam = 2,
            Player0Team = 1,
            GatePredicate = (_, _) => input[8] != 0,
        };
        int postCalls = 0;
        spawner.OnPostSpawnScript = (_, _) => postCalls++;

        var slot = spawner.Register(record, new CsacSpawnerParameters(
            RespawnDelayRaw: (short)input[0],
            MaxConcurrent: (short)input[1],
            InitialFlags: (ushort)input[2],
            InitialCount: (short)input[3]))!;

        // For positive delay cases, seed the exact timer value that (uint)randVal % (uint)delay yields
        // so the deterministic vector compares every post-register field against gcc 2.7.2 + qemu.
        if (input[0] > 0)
            slot.Timer = (int)((uint)input[4] % (uint)slot.RespawnDelay);
        int initTimer = slot.Timer;

        if (input[5] != 0)
            Assert.True(spawner.ApplyCommandById(77, input[5], input[6]));

        int spawns = spawner.Drain(input[7]);

        int statSum = spawner.SinglePlayerStats.Type1 + spawner.SinglePlayerStats.Type2 * 10
            + spawner.SinglePlayerStats.Type3 * 100 + spawner.SinglePlayerStats.Type4 * 1000
            + spawner.Player0Stats.Type1 + spawner.Player0Stats.Type2 * 10
            + spawner.Player0Stats.Type3 * 100 + spawner.Player0Stats.Type4 * 1000;

        int[] actual =
        {
            slot.RespawnDelay, initTimer, spawns, postCalls,
            slot.TotalSpawned, slot.RemainingCount, slot.Timer, slot.Flags, statSum,
        };
        Assert.True(actual.SequenceEqual(expected),
            $"{name}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }

    [ConftFact]
    public void SkyCaptainBuildOrders_DrainThroughRetailCfunTimersIntoCsacUnitSpawnerAndRender()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        var scene = new LevelScene(path) { EnableCsacSpawner = true };
        scene.EnableMechMovement();
        scene.EnableFiring();

        var sky = Assert.IsType<SkyCaptainSystem>(scene.SkyCaptain);
        Assert.False(sky.EnableBuildOrderSpawner);

        // Seed one Sky Captain base aircraft order (FB4, D_8009FA94+0xB4 = GameSession[45]).
        // Retail CONFT.MIS Cfun T0 drains GameSession[45] into ScriptVars[10], and T17 dispatches
        // op 60 (SPWID) to Csac 33 (team-2 aircraft spawner).
        sky.Economy.FB0 = 0;
        sky.Economy.FB4 = 1;
        sky.Economy.FB8 = 0;
        sky.Economy.FBC = 0;

        for (int i = 0; i < 60 && !scene.CsacSpawner!.SpawnedUnits.Any(u => u.SourceSlot.Record.Id == 33); i++)
        {
            scene.AdvanceSkyCaptain(50);
        }

        Assert.Equal(0, sky.Economy.FB4);
        var spawned = Assert.Single(scene.CsacSpawner!.SpawnedUnits.Where(u => u.SourceSlot.Record.Id == 33));
        Assert.Equal(9, spawned.EntityType);
        Assert.Contains(spawned, scene.Entities!.Units);

        // Anchor the camera at the spawned unit and verify LevelScene.Render draws its Cobj model.
        scene.Walker!.X = spawned.X;
        scene.Walker.Y = spawned.Y;
        scene.Walker.Z = spawned.Z;
        scene.AttachWalker(scene.Walker);
        scene.Render(new LevelCamera(0.0, 20.0, 0x1200));
        Assert.True(scene.SpawnedCsacUnitPrimitives > 0, "retail Csac unit produced no rendered faces");
    }
}
