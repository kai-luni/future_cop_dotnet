using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Scene-level checks for four LevelScene wirings a mutation run left uncovered (docs/todo 08-11):
/// shot delivery and its removal, the player radius of the capture test, the release of a one-shot
/// MapZone and the HUD unit counters. All of them drive the real CONFT.MIS scene.
/// </summary>
public class LevelSceneGapTests
{
    private const uint FirePad = 0x00400000;

    /// <summary>
    /// todo 08, kills mutant M13 (the removal dropped from <see cref="LevelScene"/>'s shot delivery;
    /// the suite stayed green). A shot that touches an entity leaves <see cref="LevelWeapons.Shots"/>
    /// in the frame of the contact - retail retires it through its own message 11 and the mover's
    /// unk1C = -1 (80036624.c:155-156) - so a shot damages exactly once.
    /// </summary>
    /// <remarks>
    /// The trigger fires along model -Z from the walker's stand and the shot steps 0x14000 a frame,
    /// far more than the 8912 reach of an outpost, so a natural hit would depend on the terrain
    /// height under the muzzle (CONFT starts the walker ~0x6000 above the pad) and on where a step
    /// happens to land. The test therefore drives the same flight deterministically: it fires one
    /// shot, pushes it step by step onto the outpost's contact point and calls the frame's delivery
    /// exactly as <see cref="LevelScene.AdvanceWalker"/> does. A second delivery of the same shot must
    /// not happen, which is what the removal buys: the damage is one contact word, and the shot list
    /// is empty afterwards. With the removal gone the shot stays listed and is delivered again.
    /// </remarks>
    [ConftFact]
    public void ShotThatHitsAnOutpostLeavesTheShotListAndDamagesOnlyOnce()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!);
        scene.EnableMechMovement();
        scene.EnableFiring();
        scene.EnableObjectives();
        var shots = scene.Weapons!.Shots;
        var outpost = scene.Objectives!.Objectives.Single(o => o.Id == 61);
        Assert.Equal(500, outpost.Health);

        // Stand on the outpost's own line, 0x6000 short of it. The trigger fires along the walker's
        // facing axis, so this puts the muzzle on that line instead of the CONFT spawn pad.
        var walker = scene.Walker!;
        (walker.X, walker.Y, walker.Z) = (outpost.X, outpost.Y, outpost.Z + 0x6000);
        scene.AdvanceWalker(0, 0, AnalogPad.Digital);

        scene.HeldPadWord = FirePad;
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        scene.HeldPadWord = 0;
        var shot = Assert.Single(shots);
        int perHit = shot.Definition.ObjWord00;
        Assert.Equal(25, perHit);

        // The trigger aims along the walker's facing axis from a muzzle that misses the outpost's centre
        // by 0x7F0 in x (inside the 8912+1504 radius sum, 800620D8.c:58), but the level's 0x14000 step
        // is longer than that reach and jumps over it. The flight is therefore continued with the
        // shot's own integrator to the outpost's centre line 0x2000 short of it - the position a
        // slower frame lands on - and the scene's own frame then delivers it.
        var outpostCoord = outpost.Coord!.Value;
        shot.Position[0] = outpostCoord.X;
        shot.Position[1] = outpostCoord.Y;
        shot.Position[2] = outpostCoord.Z - 0x2000;
        Assert.True(EntityPairCollision.ReachesPartTest(outpostCoord,
            CollisionCoord.FromFrameBounds(shot.X, shot.Y, shot.Z, shot.Model.FrameBounds)));

        // AdvanceWalker is the only path that runs DeliverShotContacts: cull (:178), pair loop (:191),
        // then the removal under test. The contact frame drains the list and lands one contact word;
        // further frames must neither edge the list nor the health again.
        int contactFrames = 0, healthAfterContact = -1;
        for (int frame = 0; frame < 4; frame++)
        {
            int before = outpost.Health;
            scene.AdvanceWalker(1, 0, AnalogPad.Digital);
            if (outpost.Health != before) contactFrames++;
            if (frame == 0)
            {
                healthAfterContact = outpost.Health;
                Assert.Empty(shots);
            }
            Assert.Equal(healthAfterContact, outpost.Health);
        }

        Assert.Equal(1, contactFrames);
        Assert.Equal(500 - perHit, healthAfterContact);
        Assert.Empty(shots);
        Assert.False(outpost.Freed);
    }

    /// <summary>
    /// todo 09, kills mutant M14 (the capture test passing radius 0 instead of the player model's;
    /// the suite stayed green). Message 10 of func_8002D664 hands the player to gTryCaptureObjective,
    /// whose limit is (obj+0xFC &lt;&lt; 4) + other+0x68 + 0x4000 (8002D1A0.c:157, :169-170), other+0x68
    /// being the model instance radius (80013AF0.c:83). A player at exactly Limit(0) is inside the
    /// real limit and must capture; the last inside distance captures, the real limit itself not.
    /// </summary>
    /// <remarks>
    /// The radius widens the tolerance by 4528, so the theory walks the real boundary from both
    /// sides: Limit(radius) - 1 captures, Limit(radius) is rejected (dist - limit &gt;= 0, :170) and so
    /// is Limit(radius) + 1, which lies inside Limit(0) and would capture if the radius were 0.
    /// </remarks>
    [ConftTheory]
    [InlineData(-1, true)]        // Limit(radius) - 1: inside
    [InlineData(0, false)]        // Limit(radius): 8002D1A0.c:170 rejects dist - limit >= 0
    [InlineData(1, false)]        // Limit(radius) + 1: outside, yet inside Limit(0)
    public void SceneCaptureUsesThePlayerModelRadius(int offsetFromLimit, bool captures)
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!);
        scene.EnableMechMovement();
        scene.EnableObjectives();
        int radius = CollisionCoord.FromFrameBounds(0, 0, 0, scene.Parts[0].Model.FrameBounds).Radius;
        Assert.Equal(4528, radius);
        var target = scene.Objectives!.Objectives.Single(o => o.Id == 112);
        Assert.Equal(32768, target.Limit(0));
        Assert.Equal(32768 + 4528, target.Limit(radius));
        int distance = target.Limit(radius) + offsetFromLimit;

        var walker = scene.Walker!;
        (walker.X, walker.Y, walker.Z) = (target.X + distance, target.Y, target.Z);
        scene.AdvanceWalker(0, 0, AnalogPad.Digital);
        // A zero-step update must not move the walker, or the distance above is not the tested one.
        Assert.Equal(target.X + distance, walker.X);
        Assert.Equal(target.Z, walker.Z);

        Assert.Equal(captures ? 1 : 0, target.CaptureMode);
        Assert.Equal(captures ? 1 : 0, scene.ScoreSession(new HudSession()).Score0);

        // Second stand: outpost 113 sits at the same Limit(0) 32768 but on its own terrain, so the
        // capture edge is checked on a centre that is not the theory's target. At Limit(radius) + 1
        // the player is still inside Limit(0) - the mutant's tolerance - and must not capture.
        var other = scene.Objectives!.Objectives.Single(o => o.Id == 113);
        Assert.Equal(32768, other.Limit(0));
        Assert.Equal(32768 + 4528, other.Limit(radius));
        var edge = scene.Walker!;
        (edge.X, edge.Y, edge.Z) = (other.X, other.Y, other.Z + other.Limit(radius) + 1);
        scene.AdvanceWalker(0, 0, AnalogPad.Digital);
        Assert.Equal(other.Z + other.Limit(radius) + 1, edge.Z);
        Assert.Equal(0, other.CaptureMode);
    }

    /// <summary>
    /// todo 10. 80031C74.c:115 returns <c>(flags &amp; 2) == 0</c>; a non-zero return makes
    /// func_80031DE8 unlink and free the node (80031DE8.c:172-184), so a one-shot MapZone fires once.
    /// A node with flag 2 returns 0, stays linked and is tested again next frame. CONFT has no one-shot
    /// zone aimed at the player (its flag-0 zones 235-244, 5 and 9 target Csac ids), so the test
    /// clears flag 2 on the player's helipad 106 (flags 0x06, cost 1) and compares with the untouched pad.
    /// </summary>
    [ConftTheory]
    [InlineData(true, 4, 1)]      // one-shot: bought once, then the node is gone
    [InlineData(false, 2, 3)]     // stay: bought on each of the three frames
    public void OneShotMapZoneFiresOnceAndStayZoneKeepsFiring(bool oneShot, int scoreAfter, int helis)
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        var scene = new LevelScene(path) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        var pads = scene.PurchasePads!;
        var pad = pads.FindPad(106)!;
        Assert.Equal(PurchasePadKind.MapZone, pad.Kind);
        Assert.Equal(0x06, pad.Flags);
        if (oneShot) pad.Flags &= unchecked((byte)~2);
        scene.AttachWalker(new MechBody { X = pad.WorldX, Y = pad.WorldY, Z = pad.WorldZ, Form = 0 });
        scene.Objectives!.Score.Score[0] = 5;

        for (int frame = 0; frame < 3; frame++)
            scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);

        Assert.Equal(scoreAfter, scene.ScoreSession(new HudSession()).Score0);
        Assert.Equal(helis, pads.VmState.GameSession[RpnsVmState.SessionHelisP1]);
    }

    /// <summary>
    /// todo 11. The HUD prints session +0x6C/+0x70 and +0x74/+0x78 as two "aa/bb" lines
    /// (gDrawInGameHud, HudDraw.cs:268/279) and adds +0x84/+0x88 for the score flash. ScoreSession
    /// copies them from the RPNS session bank words 27..30 and 33/34. The helipad purchase script
    /// increments word 28 (P1 helis), which must reach Count2; distinct values in every word pin
    /// each counter to its own slot.
    /// </summary>
    [ConftFact]
    public void ScoreSessionCarriesTheUnitCountersOfTheSessionBank()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        var scene = new LevelScene(path) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        var pad = scene.PurchasePads!.FindPad(106)!;
        scene.AttachWalker(new MechBody { X = pad.WorldX, Y = pad.WorldY, Z = pad.WorldZ, Form = 0 });
        scene.Objectives!.Score.Score[0] = 5;

        var start = scene.ScoreSession(new HudSession());
        Assert.Equal((0, 0, 0, 0), (start.Count1, start.Count2, start.Count3, start.Count4));

        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);            // the walker buys one heli
        var bought = scene.ScoreSession(new HudSession());
        Assert.Equal((0, 1, 0, 0), (bought.Count1, bought.Count2, bought.Count3, bought.Count4));
        Assert.Equal(4, bought.Score0);

        var words = scene.PurchasePads.VmState.GameSession;
        words[RpnsVmState.SessionTanksP1] = 19;                         // the < 20 cap edge
        words[RpnsVmState.SessionHelisP1] = 3;
        words[RpnsVmState.SessionTanksP2] = 7;
        words[RpnsVmState.SessionHelisP2] = 11;
        words[33] = 2;
        words[34] = 5;
        var session = scene.ScoreSession(new HudSession { Stage = 4 });
        Assert.Equal((19, 3, 7, 11), (session.Count1, session.Count2, session.Count3, session.Count4));
        Assert.Equal((2, 5, 7), (session.Total1, session.Total2, session.TotalSum));
        Assert.Equal((4, 1), (session.Stage, session.Mode));
    }
}

/// <summary>Theory counterpart of <see cref="ConftFactAttribute"/>: skips when CONFT.MIS is absent.</summary>
public sealed class ConftTheoryAttribute : TheoryAttribute
{
    public ConftTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL") != "1" &&
            LevelSceneRetirementTests.FindMission() is null)
            Skip = "Retail mission PA/CONFT.MIS is absent.";
    }
}
