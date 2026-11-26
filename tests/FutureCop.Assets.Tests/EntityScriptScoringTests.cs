using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// A CONFT purchase enters the scene's targetable entity list (8006125C.c:74-76, 8004D4F8.c:56-59).
/// For Csac 33 (type 9), the scene's base-handler substitute retires it on the frame after a
/// lethal hit. Retail can tumble before retirement (80032F3C.c:74-101, 800324E0.c:242-295).
/// This test checks the scene's retire script (8004D958.c:53-55) and removal (8004D598.c:46-54),
/// not retail flyer timing.
/// </summary>
public class EntityScriptScoringTests
{
    [RetailFact]
    public void SceneRetiresPurchasedHelicopterAndCreditsScoreAndTotal()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        var world = scene.Entities!;
        var pads = scene.PurchasePads!;
        var spawner = scene.CsacSpawner!;
        var score = scene.Objectives!.Score;
        var session = pads.VmState.GameSession;
        score.Score[1] = 5;
        Assert.Equal(0, session[RpnsVmState.SessionScoreP1]);
        Assert.Equal(0, session[31]);

        // CONFT's P2 helipad (Cact 108) spends one point, increments session[30] and queues Csac 33.
        // Trigger the scene's pad for the P2 actor; the viewer only supports attaching a P1 walker.
        var pad = pads.FindPad(108)!;
        pads.SyncFromMatchScore(score);
        Assert.True(pads.TryTriggerPad(pad, new PlayerTarget(pad.TargetId, 0, 0, 0, 0, busy: false),
            scoreSlot: RpnsVmState.SessionScoreP2).Accepted);
        pads.SyncToMatchScore(score);
        scene.AdvanceSkyCaptain(0);
        Assert.Equal(4, score.Score[1]);
        Assert.Equal(1, session[RpnsVmState.SessionHelisP2]);

        var bought = Assert.Single(spawner.SpawnedUnits, u => u.SourceSlot.Record.Id == 33);
        var record = bought.SourceSlot.Record;
        var unit = Assert.Single(world.Units, u => u.RecordId == record.Id);
        Assert.Contains(bought, scene.ObjectRegistry.LiveObjects.Entries);
        Assert.Equal(record.Type, unit.DispatchType);
        Assert.Equal(record.Health, unit.Health);
        Assert.Equal(record.Health, unit.MaxHealth);
        Assert.Equal(record.Scripts[0], unit.DamageScript);
        Assert.Equal(record.Scripts[1], unit.RetireScript);
        Assert.Equal((record.X, record.Z), unit.GroundPosition);
        Assert.Equal(LiveEntity.SpawnedUnitFlag, unit.Flags & LiveEntity.SpawnedUnitFlag);

        var coord = Assert.IsType<CollisionCoord>(unit.Coord);
        scene.EnableMechMovement();
        var walker = scene.Walker!;
        (walker.X, walker.Y, walker.Z) = (coord.X, coord.Y, coord.Z);
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        var shot = new ProjectileContact(scene.Actor.Id, (short)(record.Health + 1), 0, coord.X, coord.Z);
        LevelScene.DeliverShotContact(world, shot, coord);
        Assert.True(unit.Health < 0);
        Assert.Equal(0, score.Score[0]);
        // A native score change between scripts must survive the VM round trip.
        score.Total[1] = 3;

        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.DoesNotContain(unit, world.Units);
        Assert.DoesNotContain(bought, scene.ObjectRegistry.LiveObjects.Entries);
        Assert.Equal(0, bought.SourceSlot.TotalSpawned);

        // CONFT pool 0x4A2: 99 15 9F 15 9E 19 00 (80069D68.c:178-186,243-257).
        // The retire script increments session[25]/[31] and decrements the P2 heli counter [30].
        Assert.Equal(new[] { 1, 4 }, score.Score);
        Assert.Equal(new[] { 1, 3 }, score.Total);
        Assert.Equal(1, session[RpnsVmState.SessionScoreP1]);
        Assert.Equal(1, session[31]);
        Assert.Equal(0, session[RpnsVmState.SessionHelisP2]);
    }
}
