using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Todo 02: the per-type handler switch of func_8002AE54 as the port wires it, and the attack
/// scheduler slot the mobile army types (5 rail vehicle, 9 hover drone, 28 multi-turret tank) own
/// at entity +0xA0.
/// </summary>
public sealed class ArmyTypeHandlerTests
{
    [ConftFact]
    public void AttackDefinitionLoader_ReadsTheDefinitionTableOfTheMission()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        var file = new MisFile(path);
        AttackDefinitionTable table = AttackDefinitionLoader.LoadFromMission(file.Bytes);

        // Nine 0x62 chunks in CONFT.MIS, keys 1..9 (80060838.c:74-78 loads one entry per chunk).
        Assert.Equal(9, table.Count);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, table.Entries.Select(e => e.Key));

        // Key 1 is the army bolt (team 1 and team 2 units share it, see the descriptor table).
        AttackDefinition bolt = table.Entries.First(e => e.Key == 1);
        Assert.Equal(0x4E20, bolt.W02);                 // 20000 range seed, entry +0x02
        Assert.Equal(1, bolt.W06);                      // burst count, entry +0x06
        Assert.Equal(300, bolt.W08);                    // 0x10 << 12 / 218 (80060838.c:82-83)
        Assert.Equal(13, bolt.W12);                     // 0x1000 / 300 (800607B8.c:32-34)
        Assert.Equal(0x1800, bolt.W16);
        Assert.NotEqual(0, bolt.ResolvedW20);           // aRSL pair of the request (80060838.c:85-95)
    }

    [ConftFact]
    public void ArmyTypes5_9_28_ArmTheirAttackSlotFromTheirRecordDescriptor()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        var file = new MisFile(path);
        var records = MissionEntities.Load(file);
        AttackDefinitionTable table = AttackDefinitionLoader.LoadFromMission(file.Bytes);

        // The three mobile army classes of the Csac requests (func_8002AE54 -> func_800382B4 /
        // func_80032F3C / func_800398EC).
        var byType = records
            .Where(r => r.Container == EntityContainer.Csac && r.Type is 5 or 9 or 28)
            .GroupBy(r => r.Type)
            .ToDictionary(g => g.Key, g => g.First());
        Assert.Equal(new[] { 5, 9, 28 }, byType.Keys.OrderBy(k => k));

        Assert.Equal(ArmyTypeHandlerKind.RailVehicle,
            ArmyTypeHandler.Create(byType[5], table)!.Kind);
        Assert.Equal(ArmyTypeHandlerKind.HoverDrone,
            ArmyTypeHandler.Create(byType[9], table)!.Kind);
        Assert.Equal(ArmyTypeHandlerKind.MultiTurretTank,
            ArmyTypeHandler.Create(byType[28], table)!.Kind);

        foreach (int type in new[] { 5, 9, 28 })
        {
            ArmyTypeHandler handler = ArmyTypeHandler.Create(byType[type], table)!;
            // The descriptor at source + 0x24 keys into D_800A2300 (80060668.c:77-107); every army
            // record of the shipped mission resolves, so the slot carries a live definition.
            Assert.NotNull(handler.Slot.Definition);
            Assert.True(handler.HasAttack, $"type {type} did not resolve its attack definition");
            // func_80060668 copies the descriptor fields into the slot's filter words.
            Assert.NotEqual(0, handler.Slot.TargetClass06);
            Assert.Equal(byType[type].Source[0x26] | (byType[type].Source[0x27] << 8), handler.Slot.Flags);
            Assert.Equal(byType[type].Source[0x2A] | (byType[type].Source[0x2B] << 8), handler.Slot.Value08);
        }
    }

    [ConftFact]
    public void PurchasedTankUnit_WalksItsRouteAndFiresAtAnEnemyUnit()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        byte[] bytes = File.ReadAllBytes(path);
        var records = MissionEntities.Parse(bytes);
        var registry = new MapObjectRegistry();
        registry.InitMissionSpawnState();
        registry.LiveObjects.Allocate();

        var world = new EnemyWorld(0, 0x100) { Table = AttackDefinitionLoader.LoadFromMission(bytes) };
        world.Random = () => 0x1234u;
        world.TransformPartVertex = (_, actor) => (actor.PositionX, actor.PositionY, actor.PositionZ);
        world.OnTraceMapRay = (_, _) => 0;
        world.DefaultLineOfSightProbe = (_, _) => true;  // no terrain bound in this headless harness
        world.OnSpawnDirectShot = _ => 0;
        world.OnPlaySound = (_, _, _, _) => { };

        var spawner = new CsacUnitSpawner(registry) { AttackTable = world.Table, AttackWorld = world };
        spawner.RegisterFromRecords(records, r => MissionEntities.ReadSpawnerParameters(bytes, r));
        spawner.LoadPathNetworks(bytes);
        var purchase = PurchasePadSystem.LoadFromMission(bytes, spawner)!;
        purchase.VmState.GameSession[RpnsVmState.SessionScoreP1] = 5;

        // Main-base P1 tankpad (Cact 107) -> Cfun T18 -> Csac 75, a type-5 rail vehicle of team 1.
        var tankPad = purchase.FindPad(107)!;
        Assert.True(purchase.TryTriggerPad(tankPad, new PlayerTarget(12, tankPad.WorldX, tankPad.WorldY, tankPad.WorldZ, busy: false)).Accepted);
        var t18 = purchase.Timers[18];
        purchase.TickTimers(t18.Remaining);
        purchase.TickTimers(1);                          // remaining < 0 fires (80049564.c:51)
        Assert.True(spawner.Drain(1) >= 1);

        var unit = Assert.Single(spawner.SpawnedUnits.Where(u => u.SourceSlot.Record.Id == 75));
        Assert.Equal(5, unit.EntityType);
        Assert.Equal(1, unit.Team);

        // func_8002AE54 -> func_800382B4: message 2 arms the +0xA0 slot from the descriptor.
        var handler = Assert.IsType<ArmyTypeHandler>(unit.ArmyHandler);
        Assert.Equal(ArmyTypeHandlerKind.RailVehicle, handler.Kind);
        Assert.NotNull(handler.Slot.Definition);
        Assert.Equal(2, handler.Slot.Value08);           // team-1 record targets the class-2 candidates

        // One enemy unit as the acquisition candidate of func_8004E80C.
        var enemy = world.NewOwner();
        enemy.Mode16 = 9;
        enemy.Type18 = 2;
        enemy.PositionX = enemy.Vec5CX = unit.X;
        enemy.PositionY = enemy.Vec5CY = unit.Y;
        enemy.PositionZ = enemy.Vec5CZ = unit.Z;

        int spawnX = unit.X, spawnZ = unit.Z;
        for (int i = 0; i < 400 && world.DirectShots.Count == 0; i++)
        {
            // func_800616A0's frame order: message 6 reaches the unit's message-9 arm, which steps
            // the rail mover and then func_80060238 (800382B4.c:292-293).
            registry.BroadcastMessage6(1);
            enemy.PositionX = enemy.Vec5CX = unit.X;
            enemy.PositionY = enemy.Vec5CY = unit.Y;
            enemy.PositionZ = enemy.Vec5CZ = unit.Z;
        }

        Assert.True(world.DirectShots.Count > 0,
            $"no shot: def={handler.Slot.Definition is not null} flags=0x{handler.Slot.Flags:X4} " +
            $"mode={handler.Slot.Mode07} cls={handler.Slot.TargetClass06} target={handler.Slot.Target18?.Id} " +
            $"retry={handler.Slot.Retry16} acc={handler.Slot.FrameAcc24} playing={handler.Slot.Playing22} " +
            $"mobile={unit.IsMobile} entities={world.Entities.Count} pos=({unit.X},{unit.Y},{unit.Z})");

        DirectSpawnArgs shot = world.DirectShots[0];
        Assert.Equal(2, shot.ShotType);                  // definition type 2 = the direct bolt (8005F6E8.c:396-417)
        Assert.Same(handler.Slot, shot.Slot);
        // A type-2 direct bolt carries no target pointer (8005F6E8.c:396-405 nulls s1 for it); the
        // aim vector points at the acquired candidate this test placed on the unit.
        Assert.Null(shot.Target);
        Assert.NotEqual(0, shot.MuzzleZ);

        // The unit walked its route while it fought: the rail mover advanced it along the network.
        Assert.True(unit.X != spawnX || unit.Z != spawnZ,
            $"type-5 unit stayed at its spawn position ({spawnX}, {spawnZ})");
    }

    [ConftFact]
    public void Type9Records_CarryHoverKind1_SoTheHoverTickNeverStepsTheScheduler()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        byte[] bytes = File.ReadAllBytes(path);
        var table = AttackDefinitionLoader.LoadFromMission(bytes);

        var flyers = MissionEntities.Parse(bytes)
            .Where(r => r.Container == EntityContainer.Csac && r.Type == 9)
            .ToList();
        Assert.NotEmpty(flyers);

        // gInitHoverEnemy copies source +0x38 into unkFA and func_800324E0 only steps the +0xA0 slot
        // when <c>o->type == 0x25 || o->fFA == 0</c> (800324E0.c:372-373, 80032180.c:207-210): every
        // shipped type-9 record is kind 1, so its slot stays armed but idle - exactly like retail.
        foreach (var flyer in flyers)
        {
            Assert.Equal(1, flyer.Source[0x38]);
            ArmyTypeHandler handler = ArmyTypeHandler.Create(flyer, table)!;
            Assert.Equal(ArmyTypeHandlerKind.HoverDrone, handler.Kind);
        }
    }
}
