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

        // gHandleTumblingFlyer message 4 (80032F3C.c:74-101): the 10/16 roll of :84 decides whether a
        // shot type-9 flyer breaks off into the tumble (state 3, dive, retire at the ground impact) or
        // falls through to the base handler and retires at once. Play the retail frame - walker stage
        // plus the solo stage that ticks the unit - until the body is gone either way.
        for (int i = 0; i < 600 && world.Units.Contains(unit); i++)
        {
            scene.AdvanceWalker(1, 0, AnalogPad.Digital);
            scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        }
        Assert.DoesNotContain(unit, world.Units);
        Assert.DoesNotContain(bought, scene.ObjectRegistry.LiveObjects.Entries);
        // The swept body stops counting against its slot: gRecordSpawnedUnitRetirement (80061020,
        // 8004D598.c:51-52) drops the live counter of every slot with a matching record id. The same
        // frame's spawner tick then spawns the replacement, so the slot counts exactly one live body.
        Assert.Equal(1, bought.SourceSlot.TotalSpawned);
        Assert.Equal(1, world.Units.Count(u => u.RecordId == record.Id));

        // CONFT pool 0x4A2: 99 15 9F 15 9E 19 00 (80069D68.c:178-186,243-257) - session[25]++,
        // session[31]++, session[30]--. The P2 score word (+0x68) is not touched by it.
        Assert.Equal(1, score.Score[0]);
        Assert.Equal(3, score.Total[1]);
        Assert.Equal(1, session[RpnsVmState.SessionScoreP1]);
        Assert.Equal(1, session[31]);
        Assert.Equal(0, session[RpnsVmState.SessionHelisP2]);
        // Fixed expectation: +0x68 is the second score word the in-game HUD draws
        // (gDrawInGameHud.c:138-139, D_8009FA94 +0x64 is a two-word score array) and the strategic
        // drone reads the same word as its credit balance (gUpdateDroneStrategy.c:257). Killing a
        // linked prop feeds it (gHandleLinkedPropEntity.c:192) and the mode-2 expansion payment
        // spends it: n is clamped to f68 and reduced by (2 - (s4C >> 2)) * 2 before f68 -= n
        // (gUpdateDroneStrategy.c:702-712). With s4C = 4 and f68 = 4 that pays exactly 2, so the
        // P2 word ends at 2 in retail as well - the four points are 5 - 1 for the pad plus the
        // 2-credit purchase, not a lost kill credit.
        Assert.Equal(2, score.Score[1]);
    }
}
