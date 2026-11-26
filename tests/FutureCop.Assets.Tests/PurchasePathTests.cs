using System.Buffers.Binary;
using System.Globalization;
using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;
using Xunit.Abstractions;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Tests the retail RPNS purchase VM (<see cref="RpnsScriptVm"/>) and pad system
/// (<see cref="PurchasePadSystem"/>): C golden vector parity against unchanged PS1
/// <c>80069D68.c</c>, price-gate boundary checks across tiers 1/2/30/50, walker-mode gating,
/// and end-to-end <c>CONFT.MIS</c> pad -&gt; VM -&gt; <see cref="CsacUnitSpawner"/> dispatch.
/// </summary>
public sealed class PurchasePathTests
{
    private readonly ITestOutputHelper _output;

    public PurchasePathTests(ITestOutputHelper output) => _output = output;

    private static readonly string[] OutputFields =
    {
        "ret", "s25", "s26", "s27", "s28", "s29", "s30",
        "varVal", "sfx", "spwCnt", "spwId", "spwCmd", "spwArg2", "evtId",
    };

    public static IEnumerable<object[]> GoldenRows()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "rpns-purchase.vectors");
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 2 + 9 + 1 + OutputFields.Length || words[11] != "->")
                throw new FormatException($"Invalid rpns-purchase vector: {line}");
            yield return new object[]
            {
                words[0],
                Convert.FromHexString(words[1]),
                Array.ConvertAll(words[2..11], ParseInt),
                Array.ConvertAll(words[12..], ParseInt),
            };
        }
    }

    private static int ParseInt(string s) =>
        int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    [Fact]
    public void GoldenFixtureHasAllSixteenPurchaseCases() =>
        Assert.Equal(16, GoldenRows().Count());

    [Theory]
    [MemberData(nameof(GoldenRows))]
    public void RpnsScriptVm_MatchesOriginalMips(string name, byte[] script, int[] input, int[] expected)
    {
        int spwCount = 0, spwId = -1, spwCmd = -1, spwArg2 = -1, evtId = -1;
        var state = new RpnsVmState
        {
            OnSpawnerCommandById = (id, cmd, arg2) =>
            {
                spwCount++;
                spwId = id;
                spwCmd = cmd;
                spwArg2 = arg2;
            },
            OnAppendEvent = id => evtId = id,
        };

        state.GameSession[RpnsVmState.SessionScoreP1] = input[0];
        state.GameSession[RpnsVmState.SessionScoreP2] = input[1];
        state.GameSession[RpnsVmState.SessionTanksP1] = input[2];
        state.GameSession[RpnsVmState.SessionHelisP1] = input[3];
        state.GameSession[RpnsVmState.SessionTanksP2] = input[4];
        state.GameSession[RpnsVmState.SessionHelisP2] = input[5];
        int varIdx = input[6];
        state.ScriptVars[varIdx] = input[7];
        state.GameSession[RpnsVmState.SessionPlayerCount] = input[8];

        int ret = RpnsScriptVm.Execute(script, state);

        int[] actual =
        {
            ret,
            state.GameSession[RpnsVmState.SessionScoreP1],
            state.GameSession[RpnsVmState.SessionScoreP2],
            state.GameSession[RpnsVmState.SessionTanksP1],
            state.GameSession[RpnsVmState.SessionHelisP1],
            state.GameSession[RpnsVmState.SessionTanksP2],
            state.GameSession[RpnsVmState.SessionHelisP2],
            state.ScriptVars[varIdx],
            state.SessionPointerBank[RpnsVmState.PointerBankSfxSlot],
            spwCount, spwId, spwCmd, spwArg2, evtId,
        };

        var diffs = OutputFields.Select((f, i) => (f, i))
            .Where(x => actual[x.i] != expected[x.i])
            .Select(x => $"{x.f}: expected {expected[x.i]}, got {actual[x.i]}").ToArray();
        Assert.True(diffs.Length == 0, $"{name}: {string.Join(", ", diffs)}");
    }

    [RetailFact]
    public void ConftMis_LoadsPurchasePadsAndSpawnsHeliAndTankAndDreadnought()
    {
        byte[] bytes = File.ReadAllBytes(LevelSceneRetirementTests.FindMission()!);
        var records = MissionEntities.Parse(bytes);
        var registry = new MapObjectRegistry();
        registry.InitMissionSpawnState();
        registry.LiveObjects.Allocate();
        var spawner = new CsacUnitSpawner(registry);
        spawner.RegisterFromRecords(records, r => MissionEntities.ReadSpawnerParameters(bytes, r));

        var purchase = PurchasePadSystem.LoadFromMission(bytes, spawner);
        Assert.NotNull(purchase);
        Assert.Equal(1808, purchase!.Pool.Length);

        // Main-base P1 helipad (Cact 106, type 95, cost 1 -> Csac 34)
        var heliPad = purchase.FindPad(106);
        Assert.NotNull(heliPad);
        Assert.Equal(PurchasePadKind.MapZone, heliPad!.Kind);
        Assert.True(heliPad.RequiresWalkerMode);
        Assert.True(heliPad.IsEnabled);

        // Hover mode (unkAC->unk40 != 0) must not trigger the pad (80031C74.c:102)
        purchase.VmState.GameSession[RpnsVmState.SessionScoreP1] = 5;
        var hoverRes = purchase.TryTriggerPad(heliPad, new PlayerTarget(12, 0, 0, 0, 0, busy: true));
        Assert.False(hoverRes.Triggered);
        Assert.Equal(5, purchase.VmState.GameSession[RpnsVmState.SessionScoreP1]);

        // Walker mode triggers purchase: deducts 1, increments P1 helis, queues burst on Csac 34
        var walkRes = purchase.TryTriggerPad(heliPad, new PlayerTarget(12, 0, 0, 0, 0, busy: false));
        Assert.True(walkRes.Triggered);
        Assert.True(walkRes.Accepted);
        Assert.Equal(RpnsVmState.SfxPurchaseAccepted, walkRes.SfxId);
        Assert.Equal(4, walkRes.ScoreAfter);
        Assert.Equal(1, purchase.VmState.GameSession[RpnsVmState.SessionHelisP1]);
        Assert.Equal(1, spawner.Drain(1));
        Assert.Equal(34, spawner.SpawnedUnits[^1].SourceSlot.Record.Id);

        // Main-base P1 tankpad (Cact 107, type 95, cost 1 -> ScriptVars[11]++ -> Cfun T18 -> Csac 75)
        // T18 has raw period 202 -> scaled 202 * 5 = 1010 (80049420.c:85-92), fires only at remaining < 0 (80049564.c:51).
        var tankPad = purchase.FindPad(107)!;
        var tankRes = purchase.TryTriggerPad(tankPad, new PlayerTarget(12, 0, 0, 0, 0, busy: false));
        Assert.True(tankRes.Accepted);
        Assert.Equal(3, tankRes.ScoreAfter);
        Assert.Equal(1, purchase.VmState.ScriptVars[11]);
        var t18 = purchase.Timers[18];
        Assert.Equal(1010, t18.Period);
        Assert.InRange(t18.Remaining, 0, 1009);
        int remToZero = t18.Remaining;
        purchase.TickTimers(remToZero); // remaining == 0 must NOT fire (80049564.c:51)
        Assert.Equal(0, t18.Remaining);
        Assert.Equal(1, purchase.VmState.ScriptVars[11]);
        purchase.TickTimers(1); // remaining == -1 < 0 fires and wraps to 1009
        Assert.Equal(1009, t18.Remaining);
        Assert.Equal(0, purchase.VmState.ScriptVars[11]);
        Assert.True(spawner.Drain(1) >= 1);
        Assert.Contains(spawner.SpawnedUnits, u => u.SourceSlot.Record.Id == 75);

        // Outpost P1 tankpad (Cact 150, type 14): starts disabled (bit 0x10); cmd 0x7A enables it and deducts 30
        var outpostPad = purchase.FindPad(150)!;
        Assert.False(outpostPad.IsEnabled);
        purchase.VmState.GameSession[RpnsVmState.SessionScoreP1] = 35;
        purchase.ApplyPadCommand7A(outpostPad, arg: 1);
        Assert.True(outpostPad.IsEnabled);
        Assert.Equal(5, purchase.VmState.GameSession[RpnsVmState.SessionScoreP1]);

        // Now trigger outpost pad (cost 2)
        var outRes = purchase.TryTriggerPad(outpostPad, new PlayerTarget(12, 0, 0, 0, 0, busy: false), frameTime: 100);
        Assert.True(outRes.Accepted);
        Assert.Equal(3, outRes.ScoreAfter);
        Assert.Equal(1, purchase.VmState.ScriptVars[0]);

        // Dreadnought pad (Cact 47, type 14, cost 50 -> Csac 7)
        var dreadPad = purchase.FindPad(47)!;
        Assert.False(dreadPad.IsEnabled);
        purchase.ApplyPadCommand7A(dreadPad, arg: 1);
        purchase.VmState.GameSession[RpnsVmState.SessionScoreP1] = 50;
        var dreadRes = purchase.TryTriggerPad(dreadPad, new PlayerTarget(12, 0, 0, 0, 0, busy: false), frameTime: 200);
        Assert.True(dreadRes.Accepted);
        Assert.Equal(0, dreadRes.ScoreAfter);
        Assert.Equal(1, purchase.VmState.ScriptVars[18]);
        Assert.True(spawner.Drain(1) >= 1);
        Assert.Contains(spawner.SpawnedUnits, u => u.SourceSlot.Record.Id == 7);
    }

    [Fact]
    public void MatchScoreSync_RoundTripsBothScoreWords()
    {
        var pool = new RpnsPool(Convert.FromHexString("991080239c1094252c140ea2c7803cbf831e99199c150804c9831e00"));
        var pad = new PurchasePad(106, PurchasePadKind.MapZone, 0, 0, 0, 100, 100, 100, 0x06, 12, 2, 0, -1);
        var sys = new PurchasePadSystem(pool, pads: new[] { pad });
        var score = new MatchScore(1, 2, 1, 12, 1);
        score.Score[0] = 3;
        score.Score[1] = 7;

        sys.SyncFromMatchScore(score);
        var res = sys.TryTriggerPad(pad, new PlayerTarget(12, 0, 0, 0, 0, busy: false));
        sys.SyncToMatchScore(score);

        Assert.True(res.Accepted);
        Assert.Equal(2, score.Score[0]);
        Assert.Equal(7, score.Score[1]);
    }

    /// <summary>
    /// Finding 3 of the review: the type-14 walk of 8003151C.c:105-122 tests <i>every</i> player entity
    /// of D_8009FA58, not only the P1 body. Slot 1 is the P2 entity (obj+0x14 = 16 in CONFT.MIS), and
    /// <see cref="LevelScene.AttachSecondPlayer"/> is what registers it. The observable is the pad's
    /// own <see cref="PurchasePad.LastTriggerFrame"/>: a pad accepts only the player inside its box, so
    /// a P1 body parked 0x200000 world units away never makes the pad fire, while the P2 body standing
    /// on the pad does — with the same pad, the same script offset and no second candidate.
    /// </summary>
    [RetailFact]
    public void SecondPlayerEntityDrivesTheType14TriggerWalk()
    {
        string path = LevelSceneRetirementTests.FindMission()!;

        // Negative arm: the type-14 pad is marked, P1 is far away and no P2 is registered. The pad
        // walk (and with it D_8009FA58) runs only with the spawner enabled and a walker attached.
        var solo = new LevelScene(path) { EnableCsacSpawner = true };
        solo.EnableObjectives();
        var soloPads = solo.PurchasePads!;
        solo.AttachWalker(new MechBody { X = 0x200000, Y = 0, Z = 0x200000, Form = 0 });
        int marked = -1;
        foreach (var candidate in soloPads.Pads)
        {
            if (candidate.Kind != PurchasePadKind.TriggerAttachment || !candidate.IsEnabled) continue;
            if (soloPads.Pads.Any(p => p != candidate && p.Kind == PurchasePadKind.MapZone && p.RecordId == candidate.RecordId)) continue;
            marked = candidate.RecordId;
            break;
        }
        Assert.True(marked >= 0, "CONFT has no standalone type-14 pad");
        var pad = soloPads.FindPad(marked)!;
        solo.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.Single(solo.PlayerTargets);                               // D_8009FA58: P1 only
        int frameAfterSolo = pad.LastTriggerFrame;

        // Positive arm: the same pad, the same far P1, and P2 standing on it.
        var dual = new LevelScene(path) { EnableCsacSpawner = true };
        dual.EnableObjectives();
        var dualPads = dual.PurchasePads!;
        dual.AttachWalker(new MechBody { X = 0x200000, Y = 0, Z = 0x200000, Form = 0 });
        var pad2 = dualPads.FindPad(marked)!;
        dual.AttachSecondPlayer(new MechBody { X = pad2.WorldX, Y = pad2.WorldY, Z = pad2.WorldZ, Form = 0 }, 16);
        dual.AdvanceSkyCaptain(RetailClock.TicksPerFrame);

        // D_8009FA58 now carries both entities (80031DE8.c:19): the P1 actor and the P2 actor 16.
        Assert.Equal(2, dual.PlayerTargets.Count);
        Assert.Contains(dual.PlayerTargets, t => t.RecordId == 16);
        Assert.Contains(dual.PlayerTargets, t => t.RecordId == dual.Actor.Id);
        // 8003151C.c:115 stamps the frame of the accepted test on the pad; the P2 entity supplied it.
        Assert.NotEqual(frameAfterSolo, pad2.LastTriggerFrame);
        Assert.Equal(dual.PurchaseFrameTime, pad2.LastTriggerFrame);
    }

    /// <summary>
    /// The scene walk of a map zone (func_80031DE8, 80031DE8.c:85-204): node 106 caches a D_800A8F90
    /// candidate of its node id 12 (:119-144, 106 is <c>id == 12</c> at tACT +0x1C, 800319D4.c:137)
    /// and tests it against its box every frame (:159-168 / 80031C74.c:84-100). It has no script to
    /// run: <c>Cact</c> 106's +0x24 word is 0 in CONFT.MIS, and 80031C74.c:106-108 enters
    /// <c>gInterpretEntityScript</c> only for a non-zero <c>node-&gt;script</c> (loaded from the
    /// request's +0x24, 800319D4.c:137). So the node fires, buys nothing and never reaches the
    /// helipad script at pool byte 0 (0x99 = <c>op 25 gGameSession[25]--</c>). The purchase itself is
    /// covered on a hand-built pad (<see cref="ConftMis_LoadsPurchasePadsAndSpawnsHeliAndTankAndDreadnought"/>).
    /// The old test read the score of a hover walker over the pad; the player is only ever a
    /// <i>candidate</i> of the scan now, never the tested target by its own right.
    /// </summary>
    [RetailFact]
    public void SceneWalkerPurchaseSpawnsHelicopterInTheSameTick()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        var scene = new LevelScene(path) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        var pads = scene.PurchasePads!;
        var pad = pads.FindPad(106)!;
        var walker = new MechBody { X = pad.WorldX, Y = pad.WorldY, Z = pad.WorldZ, Form = 1 };
        scene.AttachWalker(walker);
        scene.Objectives!.Score.Score[0] = 5;

        // A busy candidate (hover mode, unkAC->unk40 != 0) is rejected by the flag-4 gate of
        // 80031C74.c:102: the node caches it, its box test runs and nothing fires.
        var player = new PlayerTarget(12, pad.WorldX, pad.WorldY, pad.WorldZ, busy: true);
        scene.AddMapZoneCandidate(player);
        Assert.True(ReferenceEquals(player, scene.MapZoneTargetOf(pad)));
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.Equal(5, scene.ScoreSession(new HudSession()).Score0);
        Assert.DoesNotContain(scene.CsacSpawner!.SpawnedUnits, u => u.SourceSlot.Record.Id == 34);
        Assert.Null(pad.LastFire);

        // Walker form, same cached target: the box test passes and 80031C74.c:104-115 returns 1
        // without a script (node->script == 0). The node keeps flag 2 (:176-184 keeps it linked).
        player.Busy = false;
        walker.Form = 0;
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        var fire = pad.LastFire ?? throw new InvalidOperationException("zone 106 did not fire");
        Assert.Equal(12, pad.MapZoneTargetId);
        Assert.True(fire.Triggered);
        Assert.False(fire.Accepted);                                     // no script ran
        Assert.Equal((5, 5), (fire.ScoreBefore, fire.ScoreAfter));
        Assert.Equal(0, pads.VmState.GameSession[RpnsVmState.SessionHelisP1]);
        // No script word moved: the pool's zone bodies are the only writers of the script vars here.
        Assert.Equal(0, pads.VmState.ScriptVars[25]);
        Assert.Equal(0, pads.VmState.ScriptVars[16]);
        // No burst is queued, so the drain of the next update cannot turn anything into a unit.
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.Empty(scene.CsacSpawner.SpawnedUnits);
        Assert.False(scene.IsMapZoneReleased(pad));                      // flags 2: the node stays linked
    }

    /// <summary>
    /// The drawn frame after a hit on the player's helipad node: the scene renders the pad markers,
    /// and the HUD session of that frame carries the play mode. Node 106 stands on
    /// (-480, 0, 871944) with flag 2 and no script (Cact +0x24 = 0, 800319D4.c:137): the box test of
    /// 80031C74.c:84-100 accepts the player every frame, :104-115 returns 1, and without a script
    /// (80031C74.c:106-108) the session bank is untouched - 5 points and 0 helis stay. The bought
    /// unit and its face count are covered on a hand-built pad (the retail 106 buys nothing), so this
    /// test pins the no-script arm of the walk in a real render.
    /// </summary>
    [RetailFact]
    public void SceneDrawsBoughtUnitPadsAndScoreSession()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        var scene = new LevelScene(path) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        var pads = scene.PurchasePads!;
        var pad = pads.FindPad(106)!;
        var walker = new MechBody { X = pad.WorldX, Y = pad.WorldY, Z = pad.WorldZ, Form = 0 };
        scene.AttachWalker(walker);
        scene.Objectives!.Score.Score[0] = 5;

        scene.Render(new LevelCamera(0.0, 20.0, 0x1200));
        Assert.Equal(0, scene.SpawnedUnitPrimitives);
        Assert.True(scene.PadMarkerLines > 0, "the pad under the walker produced no marker lines");

        // The walker itself is the node-12 candidate (obj+0x14 of the player entity is its record id
        // 12, PlayerTarget), standing on the pad centre.
        scene.AddMapZoneCandidate(new PlayerTarget(12, pad.WorldX, pad.WorldY, pad.WorldZ));
        Assert.Equal(12, scene.MapZoneTargetOf(pad)?.RecordId);
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        var fire = pad.LastFire ?? throw new InvalidOperationException("zone 106 did not fire");
        Assert.True(fire.Triggered);
        Assert.False(fire.Accepted);                                     // no script ran
        Assert.Equal((5, 5), (fire.ScoreBefore, fire.ScoreAfter));

        // The stay node fires again on the next frame as well (800616A0 ahead of the node walk,
        // 80031DE8.c:90) and still buys nothing: no unit, no burst.
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.Empty(scene.CsacSpawner!.SpawnedUnits);
        scene.Render(new LevelCamera(0.0, 20.0, 0x1200));
        Assert.Equal(0, scene.SpawnedUnitPrimitives);

        var session = scene.ScoreSession(new HudSession());
        Assert.Equal((1, 5), (session.Mode, session.Score0));
        Assert.Equal(5, pads.VmState.GameSession[RpnsVmState.SessionScoreP1]);
        Assert.Equal(0, pads.VmState.GameSession[RpnsVmState.SessionHelisP1]);
    }

    [RetailFact]
    public void PadMarkersExcludeNonWalkerZones()
    {
        var (scene, zone) = SceneWithOnlyThisPadMarked(235);
        var camera = new LevelCamera(0.0, 20.0, 0x6000);
        Assert.Equal(PurchasePadKind.MapZone, zone.Kind);
        Assert.False(zone.RequiresWalkerMode);
        scene.Render(camera);
        Assert.Equal(0, scene.PadMarkerLines);

        zone.Flags |= 4;
        scene.Render(camera);
        Assert.Equal(4, scene.PadMarkerLines);
    }

    [RetailFact]
    public void PadMarkersCullAtTheHorizontalDistanceLimit()
    {
        var (scene, pad) = SceneWithOnlyThisPadMarked(152);
        var camera = new LevelCamera(0.0, 70.0, 0x6000);
        foreach (var (dx, dz) in new[] { (1, 0), (0, 1) })
        {
            var walker = scene.Walker!;
            walker.X = pad.WorldX + dx * 0x37FFF;
            walker.Z = pad.WorldZ + dz * 0x37FFF;
            scene.AttachWalker(walker);
            scene.Render(camera);
            Assert.Equal(6, scene.PadMarkerLines);

            for (int distance = 0x38000; distance <= 0x38001; distance++)
            {
                walker.X = pad.WorldX + dx * distance;
                walker.Z = pad.WorldZ + dz * distance;
                scene.AttachWalker(walker);
                scene.Render(camera);
                Assert.Equal(0, scene.PadMarkerLines);
            }
        }
    }

    [RetailFact]
    public void RaisedPadMarkerDrawsAtThePlatformHeight()
    {
        var (scene, _) = SceneWithOnlyThisPadMarked(152);
        var camera = new LevelCamera(0.0, 20.0, 0x1200);
        scene.Render(camera);
        Assert.Equal(6, scene.PadMarkerLines);

        // The extent of the yellow outline with this camera on CONFT's raised platform.
        byte[] markerColour = { 0xFF, 0xD0, 0x20 };
        var yellow = Enumerable.Range(0, LevelScene.Width * LevelScene.Height)
            .Where(i => scene.Rgb.AsSpan(i * 3, 3).SequenceEqual(markerColour))
            .ToArray();
        Assert.NotEmpty(yellow);
        Assert.Equal((124, 178), (yellow.First() / LevelScene.Width, yellow.Last() / LevelScene.Width));
    }

    [RetailFact]
    public void RaisedPadTriggerUsesThePlatformHeight()
    {
        var (scene, pad) = SceneWithOnlyThisPadMarked(152);
        int ground = LevelGround.Height(scene.Map, pad.WorldX, 0x100000, pad.WorldZ, out _);
        Assert.True(ground > 0);
        Assert.True(PurchasePadSystem.IsInsidePad(pad, pad.WorldX, ground + pad.ExtentY - 1, pad.WorldZ, 0));
        Assert.False(PurchasePadSystem.IsInsidePad(pad, pad.WorldX, ground + pad.ExtentY, pad.WorldZ, 0));
    }

    [ConftFact]
    public void LoadSnapsBothPadTypesToTheSelectedLowerSurface()
    {
        byte[] mission = File.ReadAllBytes(LevelSceneRetirementTests.FindMission()!);
        var map = LevelMap.FromBytes(mission);
        var layeredTile = (
            from x in Enumerable.Range(0, (map.Header.Height - 1) * 16).Select(i => (i << 13) + 0x1000)
            from z in Enumerable.Range(0, (map.Header.Width - 1) * 16).Select(i => (i << 13) + 0x1000)
            let lower = LevelGround.PlacementHeight(map, x, z, -2)
            where lower.HasValue && lower < LevelGround.Height(map, x, 0x100000, z, out _)
            select (X: x, Z: z, Y: lower.Value)).First();

        foreach (var record in MissionEntities.Parse(mission).Where(r => r.Id is 106 or 152))
        {
            var source = mission.AsSpan(record.ChunkOffset + 8);
            BinaryPrimitives.WriteInt32LittleEndian(source[8..], layeredTile.X);
            BinaryPrimitives.WriteInt32LittleEndian(source[16..], layeredTile.Z);
            if (record.Type == 95)
                source[0x1A] = 1;
            else
                BinaryPrimitives.WriteInt16LittleEndian(source[0x26..], 1);
        }

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, mission);
            var scene = new LevelScene(path);
            scene.EnableObjectives();
            // 800319D4.c:131–132 stores the selected surface height in a signed halfword.
            int mapZoneY = unchecked((short)(layeredTile.Y >> 8)) << 8;
            Assert.Equal(mapZoneY, scene.PurchasePads!.FindPad(106)!.WorldY);
            Assert.Equal(layeredTile.Y, scene.PurchasePads.FindPad(152)!.WorldY);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [RetailFact]
    public void RetiredClaimPadDisappearsFromMarkers()
    {
        var (scene, _) = SceneWithOnlyThisPadMarked(152);
        scene.Objectives!.Score.Score[0] = 100;
        var camera = new LevelCamera(0.0, 20.0, 0x1200);
        scene.Render(camera);
        Assert.Equal(6, scene.PadMarkerLines);
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.Equal(70, scene.ScoreSession(new HudSession()).Score0);
        scene.Render(camera);
        Assert.Equal(0, scene.PadMarkerLines);
    }

    private static (LevelScene Scene, PurchasePad Pad) SceneWithOnlyThisPadMarked(int tag)
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        var pad = scene.PurchasePads!.FindPad(tag)!;
        foreach (var other in scene.PurchasePads.Pads)
            if (other != pad) other.Flags &= unchecked((byte)~4);
        int ground = LevelGround.Height(scene.Map, pad.WorldX, 0x100000, pad.WorldZ, out _);
        scene.AttachWalker(new MechBody { X = pad.WorldX, Y = ground, Z = pad.WorldZ, Form = 0 });
        return (scene, pad);
    }

    [RetailFact]
    public void ScenePostCaptureRunsTheRealOutpostScriptBeforeNotifyingObservers()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!);
        scene.EnableObjectives();
        var field = scene.Objectives!;
        var objective = field.Objectives.Single(o => o.Id == 41);
        Assert.Equal(1595, objective.Script);
        field.Score.Score[0] = 35;

        // RPNS at 1595 is BF 83 1E 00: store 63 in pointer-bank word 3.
        var vm = scene.PurchasePads!.VmState;
        Assert.Equal(0, vm.SessionPointerBank[3]);
        var observed = new List<(int ObjectiveId, int ClaimantId, int Score, int ScriptValue)>();
        scene.OnPostCaptureScript = (o, claimantId) =>
            observed.Add((o.Id, claimantId, field.Score.Score[0], vm.SessionPointerBank[3]));

        Assert.Equal(1, field.Offer(objective,
            new CaptureCandidate(12, 1, 1, objective.X, objective.Y, objective.Z, 0)));
        Assert.Equal(36, field.Score.Score[0]);
        Assert.Equal(new[] { (41, 12, 36, 63) }, observed);
    }

    [RetailFact]
    public void SceneWalkerClaimsHelipadOnlyOnceAcrossSixFrames()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        var claim = scene.PurchasePads!.FindPad(152)!;
        var tankPad = scene.PurchasePads.FindPad(150)!;
        var helipad = scene.PurchasePads.FindPad(151)!;
        Assert.False(tankPad.IsEnabled);
        Assert.False(helipad.IsEnabled);
        var entity = Assert.Single(scene.Entities!.Units.Where(e => e.RecordId == claim.RecordId));
        Assert.NotEqual(0, entity.RuntimeId);
        scene.AttachWalker(new MechBody { X = claim.WorldX, Y = claim.WorldY, Z = claim.WorldZ, Form = 0 });
        scene.Objectives!.Score.Score[0] = 100;

        var frames = new List<(int Score, int Total1, int Total2)>();
        for (int frame = 0; frame <= 6; frame++)
        {
            if (frame > 0) scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
            var session = scene.ScoreSession(new HudSession());
            frames.Add((session.Score0, session.Total1, session.Total2));
            _output.WriteLine($"Frame {frame}: Score0={session.Score0} Total1={session.Total1}");
        }

        Assert.True(tankPad.IsEnabled);
        Assert.True(helipad.IsEnabled);
        Assert.Equal(new[] { (100, 0, 0) }.Concat(Enumerable.Repeat((70, 1, 0), 6)), frames);
        Assert.Equal(0, entity.RuntimeId);
        Assert.Equal(LiveEntity.ResetFlag, entity.Flags & (LiveEntity.ResetFlag | LiveEntity.NoRetireScriptFlag));

        scene.EnableMechMovement();
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.DoesNotContain(entity, scene.Entities.Units);
    }

    [ConftFact]
    public void ConftWalkerClaimRetiresEveryTaggedEntity()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        scene.EnableMechMovement();
        var pads = scene.PurchasePads!;
        var claim = pads.FindPad(152)!;
        var world = scene.Entities!;
        var field = scene.Objectives!;
        var walker = scene.Walker!;
        (walker.X, walker.Y, walker.Z) = (claim.WorldX, claim.WorldY, claim.WorldZ);
        Assert.Equal(0, walker.Form);
        field.Score.Score[0] = 100;

        int[] outpostTags = { 113, 119 };
        Assert.All(outpostTags, tag =>
        {
            Assert.Equal(2, world.Units.Count(e => e.RecordId == tag && e.RuntimeId != 0));
            Assert.Single(field.Objectives.Where(o => o.Id == tag));
        });
        var retireTags = new HashSet<int>();
        var dispatch = pads.VmState.OnCommandByTag;
        pads.VmState.OnCommandByTag = (tag, op, arg) =>
        {
            if (op == 0x3D) retireTags.Add(tag);
            dispatch?.Invoke(tag, op, arg);
        };

        var liveAfterClaim = Array.Empty<LiveEntity>();
        for (int frame = 0; frame <= 6; frame++)
        {
            if (frame > 0)
            {
                if (frame > 1) scene.AdvanceWalker(1, 0, AnalogPad.Digital);
                scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
                if (frame == 1)
                    liveAfterClaim = world.Units.Where(e => e.RuntimeId != 0 && retireTags.Contains(e.RecordId)).ToArray();
            }
            _output.WriteLine($"Frame {frame}: score={field.Score.Score[0]}, pad152.IsRetired={claim.IsRetired}, " +
                string.Join("; ", outpostTags.Select(tag =>
                    $"tag={tag} units={world.Units.Count(e => e.RecordId == tag)} " +
                    $"live={world.Units.Count(e => e.RecordId == tag && e.RuntimeId != 0)} " +
                    $"objectives={field.Objectives.Count(o => o.Id == tag)}")));
        }

        Assert.Equal(70, field.Score.Score[0]);
        Assert.True(claim.IsRetired);
        Assert.Contains(claim.RecordId, retireTags);
        Assert.All(outpostTags, tag => Assert.Contains(tag, retireTags));
        Assert.Empty(liveAfterClaim);
        Assert.DoesNotContain(world.Units, e => retireTags.Contains(e.RecordId));
        Assert.DoesNotContain(field.Objectives, o => retireTags.Contains(o.Id));
    }

    [ConftFact]
    public void TaggedRetirementWithArgOneSetsBothEntityFlags()
    {
        byte[] mission = File.ReadAllBytes(LevelSceneRetirementTests.FindMission()!);
        const int retireScript = 1595; // CONFT RPNS: store 63 in pointer-bank word 3.
        foreach (var record in MissionEntities.Parse(mission).Where(r => r.Id is 151 or 152))
            BinaryPrimitives.WriteInt32LittleEndian(mission.AsSpan(record.HeaderOffset + 0x28), retireScript);

        var world = new EntityWorld();
        var pads = PurchasePadSystem.LoadFromMission(mission, world: world)!;
        var vm = pads.VmState;
        world.OnScript = (_, offset, arg1, arg2) =>
            RpnsScriptVm.Execute(pads.Pool.Slice(offset), vm, arg1, arg2);
        var entity = Assert.Single(world.Units.Where(e => e.RecordId == 152));
        Assert.Equal(retireScript, entity.RetireScript);
        vm.OnCommandByTag!(152, 0x3D, 1);

        Assert.Equal(0, entity.RuntimeId);
        const uint mask = LiveEntity.ResetFlag | LiveEntity.NoRetireScriptFlag;
        Assert.Equal(mask, entity.Flags & mask);
        Assert.Equal(0, vm.SessionPointerBank[3]);

        // The same slot-1 script runs if 0x3D does not set the skip flag.
        vm.OnCommandByTag!(151, 0x3D, 0);
        Assert.Equal(63, vm.SessionPointerBank[3]);
    }

    [RetailFact]
    public void EastHelipadClaimIncrementsOnlyTotal2()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!);
        scene.EnableObjectives();
        var pads = scene.PurchasePads!;
        var helipad = pads.FindPad(153)!;
        Assert.False(helipad.IsEnabled);
        pads.VmState.GameSession[RpnsVmState.SessionScoreP2] = 100;

        // Exercise the east-side branch of 152 -> 149 -> 153 with the retail P2 tag.
        Assert.True(pads.TryTriggerPad(pads.FindPad(152)!, new PlayerTarget(16, 0, 0, 0, 0, busy: false)).Triggered);
        Assert.True(helipad.IsEnabled);
        var session = scene.ScoreSession(new HudSession());
        Assert.Equal((0, 1), (session.Total1, session.Total2));
    }

    [Fact]
    public void CfunTimer_LoadScalingStartPhaseAndStrictlyNegativeFireThreshold()
    {
        // Build a minimal mission with an empty RPNS block and a Cfun block with two timers:
        // T0: rawPeriod = -40 (negative -> period=200, remaining=200, 80049420.c:87-89)
        // T1: rawPeriod = +60 (positive -> period=300, remaining=rand%300, 80049420.c:91)
        // Bytecode: trig=0 (push 1, end), act0=2 (ScriptVars[0]++), act1=5 (ScriptVars[1]++)
        byte[] mission = BuildSyntheticCfunMission(new (int count, int period, int rawRem, int trig, int act)[]
        {
            (-1, -40, 999, 0, 2),
            (-1, 60, 999, 0, 5),
        }, new byte[] { 0x81, 0x00, 0x80, 0x18, 0x00, 0x81, 0x18, 0x00 });

        var refRng = new RetailRandom(0x457);
        var refVm = new RpnsVmState();
        refVm.InitRetailRandomState(refRng);
        int expectedT1Rem = (int)((uint)refRng.Next() % 300u);

        var sys = PurchasePadSystem.LoadFromMission(mission, random: new RetailRandom(0x457))!;
        Assert.Equal(2, sys.Timers.Count);
        Assert.Equal(200, sys.Timers[0].Period);
        Assert.Equal(200, sys.Timers[0].Remaining);
        Assert.Equal(300, sys.Timers[1].Period);
        Assert.Equal(expectedT1Rem, sys.Timers[1].Remaining);

        // T0: ticking exactly 200 reaches Remaining == 0, which must NOT fire (80049564.c:51)
        sys.TickTimers(200);
        Assert.Equal(0, sys.Timers[0].Remaining);
        Assert.Equal(0, sys.VmState.ScriptVars[0]);
        // 1 more tick drops to -1 < 0 -> fires and wraps to 199
        sys.TickTimers(1);
        Assert.Equal(199, sys.Timers[0].Remaining);
        Assert.Equal(1, sys.VmState.ScriptVars[0]);
    }

    [Fact]
    public void RpnsScriptVm_Op13_Op61_Op56Self_Op11Random_AndDivZero()
    {
        int queued = -1, appended = -1;
        (int grp, int cmd, int arg2) spwGrp = (-1, -1, -1);
        (int op, int arg) selfCmd = (-1, -1);
        (int tag, int op, int arg) tagCmd = (-1, -1, -1);

        var state = new RpnsVmState
        {
            OnQueueEvent = id => queued = id,
            OnAppendEvent = id => appended = id,
            OnSpawnerCommandByGroup = (g, c, a) => spwGrp = (g, c, a),
            OnSelfEntityCommand = (op, arg) => selfCmd = (op, arg),
            OnCommandByTag = (t, op, arg) => tagCmd = (t, op, arg),
        };
        var rng = new RetailRandom(0x457);
        state.InitRetailRandomState(rng);

        // Op 13: push 25, op13 (0x0D) -> OnQueueEvent(25), not OnAppendEvent
        RpnsScriptVm.Execute(new byte[] { 0x99, 0x0D, 0x00 }, state);
        Assert.Equal(25, queued);
        Assert.Equal(-1, appended);

        // Op 61: push 50, push 71 (0x47), push 0, op61 (0x3D) -> OnSpawnerCommandByGroup(50, 71, 0)
        RpnsScriptVm.Execute(new byte[] { 0xB2, 0xC7, 0x80, 0x3D, 0x00 }, state);
        Assert.Equal((50, 71, 0), spwGrp);

        // Op 56 with c == 0 -> OnSelfEntityCommand(7, 3), not OnCommandByTag (80069D68.c:380-381)
        RpnsScriptVm.Execute(new byte[] { 0x80, 0x87, 0x83, 0x38, 0x00 }, state);
        Assert.Equal((7, 3), selfCmd);
        Assert.Equal((-1, -1, -1), tagCmd);

        // Op 56 with c != 0 -> OnCommandByTag(15, 7, 3) (80069D68.c:382-384)
        selfCmd = (-1, -1);
        RpnsScriptVm.Execute(new byte[] { 0x8F, 0x87, 0x83, 0x38, 0x00 }, state);
        Assert.Equal((-1, -1), selfCmd);
        Assert.Equal((15, 7, 3), tagCmd);

        // Op 11 non-negative lookup reads OpcodeTable filled by InitRetailRandomState (80069B58.c:52-54)
        int lutVal = RpnsScriptVm.Execute(new byte[] { 0x85, 0x0B, 0x00 }, state);
        Assert.Equal(state.OpcodeTable[5], lutVal);
        Assert.InRange(lutVal, 0, 999);

        // Op 11 negative draw (0 10 op40 op11) uses RandomSource and varies across draws
        var draws = new HashSet<int>();
        for (int i = 0; i < 16; i++)
        {
            int d = RpnsScriptVm.Execute(new byte[] { 0x80, 0x8A, 0x28, 0x0B, 0x00 }, state);
            Assert.InRange(d, 0, 9);
            draws.Add(d);
        }
        Assert.True(draws.Count > 1);

        // Op 42 / 43 divide-by-zero yields 0 in the managed port (documented deviation from MIPS break 7)
        Assert.Equal(0, RpnsScriptVm.Execute(new byte[] { 0x85, 0x80, 0x2A, 0x00 }, state));
        Assert.Equal(0, RpnsScriptVm.Execute(new byte[] { 0x85, 0x80, 0x2B, 0x00 }, state));
    }

    [RetailFact]
    public void ConftMis_TimerT56Geometry_Op61Group50Burst_AndOp11RandomState()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        byte[] bytes = File.ReadAllBytes(path);
        var records = MissionEntities.Parse(bytes);
        var registry = new MapObjectRegistry();
        registry.InitMissionSpawnState();
        registry.LiveObjects.Allocate();
        var spawner = new CsacUnitSpawner(registry, new RetailRandom(0x457));
        spawner.RegisterFromRecords(records, r => MissionEntities.ReadSpawnerParameters(bytes, r));

        var purchase = PurchasePadSystem.LoadFromMission(bytes, spawner, random: new RetailRandom(0x457))!;

        // T56 geometry: rawPeriod = 300 -> scaled 1500, remaining in [0, 1499] (80049420.c:85-92)
        var t56 = purchase.Timers[56];
        Assert.Equal(1500, t56.Period);
        Assert.InRange(t56.Remaining, 0, 1499);

        // OpcodeTable and RandomSource initialized per 80069B58.c:47-54
        Assert.NotNull(purchase.VmState.RandomSource);
        Assert.Contains(purchase.VmState.OpcodeTable, v => v != 0);
        Assert.All(purchase.VmState.OpcodeTable, v => Assert.InRange(v, 0, 999));

        // T29: count=1, rawPeriod=5 -> period=25, action = 50 71 0 op61 (group-50 burst spawn)
        // Trigger is Countdowns[6]/5 == 0; set Countdowns[6] = 0 so T29 fires on expiry.
        var t29 = purchase.Timers[29];
        Assert.Equal(25, t29.Period);
        int group50Count = spawner.Slots.Count(s => s.Record.U8(0x1D) == 50);
        Assert.Equal(32, group50Count);
        purchase.VmState.Countdowns[6] = 0;
        int t29Rem = t29.Remaining;
        purchase.TickTimers(t29Rem);
        Assert.Equal(1, t29.Count); // remaining == 0 must not fire
        purchase.TickTimers(1);
        Assert.Equal(0, t29.Count); // fired once and decremented count
        int spawned = spawner.Drain(1);
        Assert.True(spawned >= group50Count, $"Expected >= {group50Count} group-50 spawns, got {spawned}");
    }

    private static byte[] BuildSyntheticCfunMission(
        (int count, int period, int rawRem, int trig, int act)[] timers,
        byte[] timerBytecode)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        // RPNS SHDR (0x20) + SDAT (20 + 4)
        bw.Write(0x53484F43u); bw.Write(0x20u); bw.Write(0u); bw.Write(0u);
        bw.Write(0x53484452u); bw.Write(0u); bw.Write(0x52504E53u); bw.Write(1u);
        bw.Write(0x53484F43u); bw.Write(24u); bw.Write(0u); bw.Write(0u);
        bw.Write(0x53444154u); bw.Write(0u);
        // Cfun SHDR (0x30) + SDAT (20 + tFUN + tEXT)
        int tfunSize = 12 + timers.Length * 20;
        int paddedText = (timerBytecode.Length + 3) & ~3;
        int textSize = 12 + paddedText;
        bw.Write(0x53484F43u); bw.Write(0x30u); bw.Write(0u); bw.Write(0u);
        bw.Write(0x53484452u); bw.Write(0u); bw.Write(0x4366756Eu); bw.Write(42u);
        bw.Write(0u); bw.Write(0u); bw.Write(0u); bw.Write(0u);
        bw.Write(0x53484F43u); bw.Write((uint)(20 + tfunSize + textSize)); bw.Write(0u); bw.Write(0u);
        bw.Write(0x53444154u);
        // tFUN chunk
        bw.Write(0x7446554Eu); bw.Write((uint)tfunSize); bw.Write(42u);
        foreach (var t in timers)
        {
            bw.Write(t.count); bw.Write(t.period); bw.Write(t.rawRem); bw.Write(t.trig); bw.Write(t.act);
        }
        // tEXT chunk
        bw.Write(0x74455854u); bw.Write((uint)textSize); bw.Write(42u);
        bw.Write(timerBytecode);
        for (int i = timerBytecode.Length; i < paddedText; i++) bw.Write((byte)0);
        return ms.ToArray();
    }
}
