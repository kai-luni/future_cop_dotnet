using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Scene-level checks for four LevelScene wirings that a mutation run left uncovered (docs/todo 08-11):
/// shot removal after a contact, the player radius in the capture test, the release of a one-shot
/// MapZone and the HUD unit counters. All of them drive the real CONFT.MIS scene.
/// </summary>
public class LevelSceneGapTests
{
    private const uint FirePad = 0x00400000;

    /// <summary>
    /// todo 08. A shot that touches an entity becomes a ProjectileContact and leaves
    /// <see cref="LevelWeapons.Shots"/>; retail retires it on the next frame through its own message 11
    /// (80036624.c:155-156 sets val1c = -1). So every shot damages at most once: the shot that hit
    /// is gone from the list after the frame of the hit, and the total damage never exceeds one hit
    /// per fired shot.
    /// </summary>
    [ConftFact]
    public void ShotThatHitsAnOutpostLeavesTheShotListAndDamagesOnlyOnce()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var outpost = scene.Objectives!.Objectives.Single(o => o.Id == 113);
        var walker = scene.Walker!;
        walker.X = outpost.X;
        walker.Z = outpost.Z - 0xE000;
        walker.Angle = 0;
        var shots = scene.Weapons!.Shots;
        int startHealth = outpost.Health;
        var fired = new HashSet<Shot>(ReferenceEqualityComparer.Instance);
        int perHit = 0, hitFrames = 0;
        bool released = false;

        for (int frame = 0; frame < 300 && !(released && shots.Count == 0); frame++)
        {
            // Hold the trigger until the first hit, then let the shots in flight finish.
            scene.HeldPadWord = released ? 0u : FirePad;
            var before = shots.ToArray();
            int healthBefore = outpost.Health;
            scene.AdvanceWalker(1, 0, AnalogPad.Digital);
            foreach (var shot in shots) fired.Add(shot);
            int lost = healthBefore - outpost.Health;
            if (lost <= 0) continue;

            hitFrames++;
            if (perHit == 0) perHit = lost;               // the first contact is a single shot
            released = true;
            // The shot that made the contact left the weapon in the same frame.
            Assert.True(before.Any(s => !shots.Contains(s)),
                $"frame {frame}: outpost lost {lost} but every shot of the previous frame is still in Shots");
        }

        Assert.True(perHit > 0, "no shot reached the outpost");
        Assert.Empty(shots);
        int totalLost = startHealth - outpost.Health;
        Assert.True(totalLost <= perHit * fired.Count,
            $"{fired.Count} shots dealt {totalLost} damage at {perHit} per hit: a shot damaged more than once");
        Assert.Equal(perHit * hitFrames, totalLost);       // never two contacts of one shot per frame
        Assert.False(outpost.Freed);
    }

    /// <summary>
    /// todo 09. Message 10 of func_8002D664 hands the player to gTryCaptureObjective, whose limit is
    /// (obj+0xFC &lt;&lt; 4) + other+0x68 + 0x4000 (8002D1A0.c:157, :169-170), other+0x68 being the model
    /// instance radius (80013AF0.c:83). The player at exactly Limit(0) is therefore inside the real
    /// limit and must capture; the last inside distance captures, the limit itself does not.
    /// </summary>
    [ConftTheory]
    [InlineData(-1, true)]        // Limit(radius) - 1: inside
    [InlineData(0, false)]        // Limit(radius): 8002D1A0.c:170 rejects dist - limit >= 0
    [InlineData(null, true)]      // Limit(0): only inside when the player's radius is used
    public void SceneCaptureUsesThePlayerModelRadius(int? offsetFromLimit, bool captures)
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!);
        scene.EnableMechMovement();
        scene.EnableObjectives();
        var target = scene.Objectives!.Objectives.Single(o => o.Id == 61);
        int radius = CollisionCoord.FromFrameBounds(0, 0, 0, scene.Parts[0].Model.FrameBounds).Radius;
        Assert.True(radius > 0, "the player model has no radius");
        int distance = offsetFromLimit is { } off ? target.Limit(radius) + off : target.Limit(0);

        var walker = scene.Walker!;
        (walker.X, walker.Y, walker.Z) = (target.X + distance, target.Y, target.Z);
        scene.AdvanceWalker(0, 0, AnalogPad.Digital);
        // A zero-step update must not move the walker, or the distance above is not the tested one.
        Assert.Equal((target.X + distance, target.Y, target.Z), (walker.X, walker.Y, walker.Z));

        Assert.Equal(captures ? 1 : 0, target.CaptureMode);
        Assert.Equal(captures ? 1 : 0, scene.ScoreSession(new HudSession()).Score0);
    }

    /// <summary>
    /// todo 10. 80031C74.c:115 returns <c>(flags &amp; 2) == 0</c>; a non-zero return makes
    /// func_80031DE8 unlink and free the node (80031DE8.c:172-184), so a one-shot MapZone fires once.
    /// A node with flag 2 returns 0, stays linked and is tested again next frame. CONFT has no one-shot
    /// zone aimed at the player (its flag-0 zones 235-244, 5 and 9 target Csac ids), so the test
    /// clears flag 2 on the player's helipad 106 (flags 0x06, no script) and compares with the
    /// untouched node: neither buys anything (that needs a script), both fire, and only the one-shot
    /// node is freed after its first hit.
    /// </summary>
    [ConftTheory]
    [InlineData(true, 5, 0)]      // one-shot: fired once, then the node is gone
    [InlineData(false, 5, 0)]     // stay: fires on each of the three frames, nothing is bought
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

        // func_80031DE8 never tests the player: the node selects its own target out of D_800A8F90
        // (80031DE8.c:119-144) and this node's id is 12, i.e. the record id of the P1 player entity.
        // The old test relied on the walker standing on the zone; the player now enters the scan as
        // the candidate func_80031DE8 would list, standing in the zone centre (octile distance 0).
        scene.AddMapZoneCandidate(new PlayerTarget(12, pad.WorldX << 8, pad.WorldY << 8, pad.WorldZ << 8));
        Assert.Equal(12, scene.MapZoneTargetOf(pad)?.RecordId);

        // The loader timer of the node is a random byte (800319D4.c:142); the box test runs every
        // frame the cached target exists (:159-168), so the three fired frames need no rescan. Cact
        // 106's slot 0 is 0, so :106-108 runs no script: every frame returns 1 without spending a
        // point or counting a heli, and only the flag-2 outcome (linked vs freed) differs.
        for (int frame = 0; frame < 3; frame++)
        {
            scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
            Assert.True(pad.LastFire is { Triggered: true }, $"frame {frame}: the node did not fire");
            Assert.False(pad.LastFire!.Value.Accepted, $"frame {frame}: a script ran");
        }

        Assert.Equal(scoreAfter, scene.ScoreSession(new HudSession()).Score0);
        Assert.Equal(helis, pads.VmState.GameSession[RpnsVmState.SessionHelisP1]);
        Assert.Equal(oneShot, scene.IsMapZoneReleased(pad));             // :115 / :176-184
    }

    /// <summary>
    /// todo 11. The HUD prints session +0x6C/+0x70 and +0x74/+0x78 as two "aa/bb" lines
    /// (gDrawInGameHud, HudDraw.cs:268/279) and adds +0x84/+0x88 for the score flash. ScoreSession
    /// copies them from the RPNS session bank words 27..30 and 33/34; distinct values in every word
    /// pin each counter to its own slot.
    /// The real node walk of CONFT cannot buy here: <c>Cact</c> 106's slot 0 is 0 (800319D4.c:137) and
    /// 80031C74.c:106-108 runs no script, and no other type-95 body of the pool counts
    /// <c>gGameSession[28]</c> - only the hand-built pad of
    /// <see cref="PurchasePathTests.SceneWalkerPurchaseSpawnsHelicopterInTheSameTick"/> drives
    /// <c>TryTriggerPad</c> into the helipad script. So this test asserts the measured no-script walk
    /// (100 helis set by hand stay 100, the HUD still reads them into Count2) instead of a purchase
    /// the retail data does not have here.
    /// </summary>
    [ConftFact]
    public void ScoreSessionCarriesTheUnitCountersOfTheSessionBank()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        var scene = new LevelScene(path) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        var pads = scene.PurchasePads!;
        var pad = pads.FindPad(106)!;
        scene.AttachWalker(new MechBody { X = pad.WorldX, Y = pad.WorldY, Z = pad.WorldZ, Form = 0 });
        scene.Objectives!.Score.Score[0] = 5;

        var start = scene.ScoreSession(new HudSession());
        Assert.Equal((0, 0, 0, 0), (start.Count1, start.Count2, start.Count3, start.Count4));

        // Node 106 selects a D_800A8F90 candidate of its own node id 12 (80031DE8.c:121-140); the P1
        // player entity is such a candidate and stands in the zone centre. The box test of
        // 80031C74.c:84-100 accepts it, and :104-115 returns 1 without entering the VM (no script).
        var words0 = pads.VmState.GameSession;
        words0[RpnsVmState.SessionHelisP1] = 100;
        scene.AddMapZoneCandidate(new PlayerTarget(12, pad.WorldX << 8, pad.WorldY << 8, pad.WorldZ << 8));
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.True(pad.LastFire is { Triggered: true }, "the node did not fire");
        Assert.Equal(100, words0[RpnsVmState.SessionHelisP1]);
        var bought = scene.ScoreSession(new HudSession());
        Assert.Equal((0, 100, 0, 0), (bought.Count1, bought.Count2, bought.Count3, bought.Count4));
        // The score slot and the score session word are the untouched observable of this walk: the
        // value of 5 set above passes SyncFromMatchScore (LevelScene:386) into word 25 and back out
        // (:413) without the script (there is none, 80031C74.c:106-108) changing it.
        Assert.Equal(5, pads.VmState.GameSession[RpnsVmState.SessionScoreP1]);
        Assert.Equal(5, bought.Score0);

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
