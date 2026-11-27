using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;
using Xunit.Abstractions;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Csac target selection of the MapZones (func_80031DE8, 80031DE8.c:111-159). A type-95 node
/// (<c>node-&gt;id != -1</c>) never tests the player: it tests the target that the candidate scan of
/// D_800A8F90 picked for the node's id. CONFT's one-shot zones 5, 9 and 235-244 carry the Csac record
/// ids 57, 7, 172, 77, 185, 204, 154, 75, 87, 173, 144 and 205, so such a zone fires only while a
/// matching unit stands in its trigger box. The observable is the cached target and the one-shot
/// release of the node (80031DE8.c:176-184), and the session and script words their RPNS slot-0
/// script writes (func_80069D68 op 21/24): in CONFT.MIS zones 5, 9, 240 and 244 carry slot 0 = 108 or
/// 112, whose first statements raise <c>gGameSession[19]</c> to 4, <c>[39]</c> to 2, <c>[44]</c> to 7 and
/// <c>[45]</c> to 3 and then <c>gScriptVmVars[25]</c> (family <c>8199 2000</c>, zone 5) or
/// <c>gScriptVmVars[16]</c> (zone 240); only zone 106 ships slot 0 = 0
/// (<see cref="LevelSceneMapZoneTargetTests.CsacZoneScriptWritesItsSessionWords"/>).
/// </summary>
public class LevelSceneMapZoneTargetTests
{
    // 80031DE8.c:151-159 (asm/us/main.s:40447-40462): a rescan rearms from the old u16 timer with
    // +600 + (rand & 0x3F); a miss stores that and skips the second +600 and -= delta, while a hit
    // adds another +600 and subtracts delta.
    // 80031C74.c:115: a node without flag 2 is released (unlinked, freed) on its first box hit.
    private const int EmptyRescanTicks = 600;

    private readonly ITestOutputHelper _output;

    public LevelSceneMapZoneTargetTests(ITestOutputHelper output) => _output = output;

    private static LevelScene LoadScene()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        Assert.NotNull(scene.PurchasePads);
        if (scene.Walker is null && scene.Parts.Count > 1)
            scene.AttachWalker(MechSpawn.CreateWalker(scene.Actor, scene.Parts[0].Model, scene.Map, scene.Parts[1].Model));
        // Deterministic scan jitter: the generator the scene loaded with, at the seed 0x457 of
        // MechWorld.cs:12-42, replayed past the node-timer draws of the loader (800319D4.c:142).
        var pads = scene.PurchasePads!;
        pads.Random = new RetailRandom(0x457);
        return scene;
    }

    /// <summary>
    /// 80031C74.c:84-103: the box test needs a negative margin on every axis, and only then does the
    /// hover gate decide: <c>other-&gt;unk16 == 1 &amp;&amp; (node-&gt;flags &amp; 4) &amp;&amp; other-&gt;ptrAC-&gt;unk40 != 0</c>
    /// (the walker-mode flag of the helipad) refuses the player in hover mode. A type-1 entity with no
    /// control block (a Csac unit) is never "busy", so the type half of the test is observable.
    /// </summary>
    [ConftFact]
    public void ZoneBoxAcceptsNeedsAMarginOnEveryAxisAndTheHoverGateOnThePlayer()
    {
        // A stand-in node with the helipad geometry; the C test is pure on the node struct.
        var node = new PurchasePad(9001, PurchasePadKind.MapZone, 100, 0, 200,
            extentX: 0x1000, extentY: 0x400, extentZ: 0x1000, flags: 0x06, targetId: 12,
            matchSelector: 2, script0Offset: 0, script2Offset: 0);
        var centre = new PlayerTarget(12, 100, 0, 200);
        Assert.True(LevelScene.MapZoneBoxAccepts(node, centre));
        // One axis past the extent (0x1001 > 0x1000) fails the whole test.
        Assert.False(LevelScene.MapZoneBoxAccepts(node, new PlayerTarget(12, 100 + 0x1001, 0, 200)));
        Assert.False(LevelScene.MapZoneBoxAccepts(node, new PlayerTarget(12, 100, 0, 200 - 0x1001)));
        Assert.False(LevelScene.MapZoneBoxAccepts(node, new PlayerTarget(12, 100, 0x401, 200)));
        // The radius of the target (obj+0x68) is subtracted from the margin: 0x1002 out with radius 2
        // gives |delta| - radius - extent == 0, still a refusal; radius 3 goes negative and passes.
        var offset = new PlayerTarget(12, 100 + 0x1002, 0, 200) { Radius = 2 };
        Assert.False(LevelScene.MapZoneBoxAccepts(node, offset));
        offset.Radius = 3;
        Assert.True(LevelScene.MapZoneBoxAccepts(node, offset));

        // 80031C74.c:102: flag 4 and a busy player refuse, a clear control block and a non-player pass.
        Assert.False(LevelScene.MapZoneBoxAccepts(node, new PlayerTarget(12, 100, 0, 200, busy: true)));
        Assert.True(LevelScene.MapZoneBoxAccepts(node, centre));
        node.Flags &= unchecked((byte)~4);
        Assert.True(LevelScene.MapZoneBoxAccepts(node, new PlayerTarget(12, 100, 0, 200, busy: true)));
    }

    /// <summary>
    /// Zone 106 is CONFT's only type-95 record with a script (flags 6: stay and walker-only, node id
    /// 12, tACT 106 at 562944/871944; the two addresses are the tACT records of P1 and P2). 80031DE8.c:159-168
    /// re-tests the cached target every frame while it exists: the timer only defers the
    /// re-selection, and the empty rescan of :148-158 drops the cache without releasing the node.
    /// </summary>
    [ConftFact]
    public void StayZoneKeepsItsTargetOnEveryFrameAndDropsItOnlyAtTheRescan()
    {
        var scene = LoadScene();
        var pads = scene.PurchasePads!;
        var zone = pads.FindPad(106)!;
        Assert.Equal((PurchasePadKind.MapZone, 12, 0x6), (zone.Kind, (int)zone.TargetId, zone.Flags));
        var walker = scene.Walker!;
        walker.X = zone.WorldX;
        walker.Y = zone.WorldY;
        walker.Z = zone.WorldZ;

        // The walker is the only candidate of node id 12 in this scene, and the scan measures it at
        // the zone centre: octile distance 0 (80031DE8.c:130-137). On a hit, RescanMapZoneTarget
        // rearms from the existing u16 timer with +600 + (rand & 0x3F) + 600 - delta (80031DE8.c:151-159).
        ushort beforeRescan = zone.MapZoneTimer;
        scene.AddMapZoneCandidate(new PlayerTarget(12, zone.WorldX, zone.WorldY, zone.WorldZ));
        Assert.Equal(12, scene.MapZoneTargetOf(zone)?.RecordId);
        Assert.InRange(zone.MapZoneTimer, beforeRescan + 1200 - 1, beforeRescan + 1200 + 0x3F - 1);

        // The cached target is re-tested every frame (80031C74.c:84-108) and the timer counts down
        // (:159-160); flag 2 keeps the node linked.
        var armed = zone.MapZoneTimer;
        scene.AdvanceSkyCaptain(1);
        _output.WriteLine("after 1 frame: " + Dump(scene, zone, walker));
        Assert.Equal(12, scene.MapZoneTargetOf(zone)?.RecordId);
        Assert.Equal(armed - 1, zone.MapZoneTimer);                        // :159-160 per frame
        Assert.False(scene.IsMapZoneReleased(zone));

        // A unit of another node id in the box does not disturb the cached player: the scan runs
        // only when the timer expires (80031DE8.c:119).
        scene.AddMapZoneCandidate(new SyntheticEntity(0x2300)
        {
            RecordId = 7, NodeId = 7, DispatchType = 5,
            X = zone.WorldX, Y = zone.WorldY, Z = zone.WorldZ,
        });
        scene.AdvanceSkyCaptain(1);
        Assert.Equal(12, zone.MapZoneTargetId);
        Assert.Equal(12, scene.MapZoneTargetOf(zone)?.RecordId);

        // No candidate at all: the re-selection empties the cache (:151-155) and rearms from oldTimer
        // with +600 + jitter without subtracting delta; the node keeps itself (flag 2).
        scene.SetMapZoneCandidates(Array.Empty<ICsacNodeId?>());
        Assert.Null(scene.MapZoneTargetOf(zone));
        Assert.False(scene.IsMapZoneReleased(zone));
        scene.AdvanceSkyCaptain(EmptyRescanTicks);
        Assert.Null(scene.MapZoneTargetOf(zone));
        Assert.False(scene.IsMapZoneReleased(zone));
    }

    [ConftTheory]
    [InlineData(5, 57)]
    [InlineData(9, 7)]
    [InlineData(235, 172)]
    [InlineData(236, 77)]
    [InlineData(237, 185)]
    [InlineData(238, 204)]
    [InlineData(239, 154)]
    [InlineData(240, 75)]
    [InlineData(241, 87)]
    [InlineData(242, 173)]
    [InlineData(243, 144)]
    [InlineData(244, 205)]
    public void CsacZonesFireOnlyForTheirOwnNodeId(int zoneId, int csacId)
    {
        // A unit of the zone's own node id placed in its box fires the zone on that very frame
        // (80031DE8.c:119-144 reads the frame's candidate table, :161-168 tests the box). The
        // walker stands on the zone as well: node id 12 is no Csac zone, so it is never a candidate
        // of these nodes and the selection has to pick the unit by id (:121-128).
        var scene = LoadScene();
        var zone = scene.PurchasePads!.FindPad(zoneId)!;
        Assert.Equal((PurchasePadKind.MapZone, csacId, 0x0), (zone.Kind, (int)zone.TargetId, zone.Flags));
        var walker = scene.Walker!;
        walker.X = zone.WorldX;
        walker.Y = zone.WorldY;
        walker.Z = zone.WorldZ;
        _output.WriteLine($"zone {zoneId}, before: " + Dump(scene, zone, walker));

        // The unit sits exactly on the zone centre with the Csac node id of the Cact record and a
        // dispatch type the scan accepts (:123-126). Both zone and entity use full world coordinates.
        var unit = new SyntheticEntity(0x2150)
        {
            RecordId = csacId, NodeId = (short)csacId, DispatchType = 5,
            X = zone.WorldX, Y = zone.WorldY, Z = zone.WorldZ,
        };
        Assert.True(LevelScene.MapZoneBoxAccepts(zone, unit), $"zone {zoneId} does not test its own centre");
        scene.AddMapZoneCandidate(unit);
        var picked = scene.MapZoneTargetOf(zone);
        Assert.True(picked is not null && picked.RecordId == csacId,
            $"zone {zoneId} picked id={picked?.RecordId ?? -1} (Csac {csacId})");

        scene.AdvanceSkyCaptain(1);
        _output.WriteLine($"zone {zoneId}, after: " + Dump(scene, zone, walker));
        Assert.Equal(csacId, scene.MapZoneTargetOf(zone)?.RecordId);
        Assert.True(scene.IsMapZoneReleased(zone), $"zone {zoneId} kept its one-shot node");

        // The node is freed on the frame it fires (80031DE8.c:176-184): the same unit staying on the
        // zone never fires the script a second time, which pins the one-shot release itself.
        scene.AdvanceSkyCaptain(1);
        scene.AdvanceSkyCaptain(EmptyRescanTicks);
        Assert.Equal(csacId, scene.MapZoneTargetOf(zone)?.RecordId);
        Assert.True(scene.IsMapZoneReleased(zone));
    }

    /// <summary>
    /// The script side of a Csac zone (80031C74.c:104-108): a fired node runs its RPNS slot-0 script
    /// with <c>arg2 = target id &amp; 0x7FFF</c>. In CONFT.MIS zone 5 takes <c>Cact</c> +0x24 = 108
    /// and zone 240 = 112; zone 5's body (pool 0x6c) is the tail
    /// <c>81 99 20 00 81 90 20 00 82 12 80 21 14 04 ...</c> = <c>op 1</c> -0x19, <c>op 1</c> -0x10,
    /// <c>op 2</c> 0x12 (18), <c>op 9</c>, <c>op 20</c> +4: <c>if (!(18 != 0)) skip op 21</c>, so
    /// <c>op 21</c> (gGameSession[a]++, 80069D68.c:243-245) runs on the -0x19 word and the branch
    /// then lands on another <c>op 1/op 9/op 20 04</c> guard; the far push of the body is what feeds
    /// a later <c>op 21</c>. Measured: <c>gGameSession[25]</c> is untouched, and the two zones differ
    /// in exactly one VM var word (op 24) — zone 5 bumps <c>gScriptVmVars[25]</c> and zone 240
    /// <c>gScriptVmVars[16]</c>.
    /// That word is the observable of these twelve zones; the cached target and the release alone do
    /// not prove the script ran.
    /// </summary>
    [ConftTheory]
    [InlineData(5, 57, 25)]
    [InlineData(240, 75, 16)]
    public void CsacZoneScriptWritesItsSessionWords(int zoneId, int csacId, int scriptVar)
    {
        var scene = LoadScene();
        var pads = scene.PurchasePads!;
        var zone = pads.FindPad(zoneId)!;
        Assert.Equal(csacId, (int)zone.TargetId);
        // Zone 5 draws slot 0 = 108, zone 240 the same family at 112 (both non-zero, so the script runs).
        Assert.Equal(zoneId == 5 ? 108 : 112, zone.Script0Offset);

        // The walker stands far away so that the box test of the node cannot accept the player as the
        // cached target of the very frame the unit is added; the unit is the only candidate in the box.
        var walker = scene.Walker!;
        walker.X = 0x200000;
        walker.Z = 0x200000;
        var unit = new SyntheticEntity(0x2150)
        {
            RecordId = csacId, NodeId = (short)csacId, DispatchType = 5,
            X = zone.WorldX, Y = zone.WorldY, Z = zone.WorldZ,
        };
        scene.AddMapZoneCandidate(unit);
        var session = pads.VmState.GameSession;
        var vars = pads.VmState.ScriptVars;
        int before25 = session[RpnsVmState.SessionScoreP1];
        int beforeVar = vars[scriptVar];
        var before = (s6: session[6], s7: session[7], s8: session[8], s9: session[9]);

        scene.AdvanceSkyCaptain(1);

        // Op 21/24 of func_80069D68 (80069D68.c:243-250): the one script var the body's op 24 bumps.
        Assert.True(scene.IsMapZoneReleased(zone), $"zone {zoneId} did not fire");
        // gGameSession[6..9] are gInitScriptVmState's random words (80069B58.c:47-50), not script
        // output: the body of neither zone touches them.
        Assert.Equal(before, (session[6], session[7], session[8], session[9]));
        Assert.Equal(beforeVar + 1, vars[scriptVar]);
        // Measured in the scene: zone 5's body bumps gScriptVmVars[25] and leaves var 16 alone;
        // zone 240's older slot bumps var 16 and leaves var 25 alone (each node runs only its own
        // script, 80031C74.c:106-108), and neither touches the score slot.
        Assert.Equal(before25, session[RpnsVmState.SessionScoreP1]);
        Assert.Equal(zoneId == 5 ? 1 : 0, vars[25]);
        Assert.Equal(zoneId == 240 ? 1 : 0, vars[16]);
    }

    /// <summary>
    /// A type-95 record whose <c>Cact</c> slot 0 is 0 (CONFT zone 106, the only such zone) has no
    /// script to run: the box test of 80031C74.c:84-100 accepts the target, the busy gate of :102
    /// passes, :104-115 runs the script only when <c>node-&gt;script != 0</c>, and the node returns 1
    /// (<c>(node-&gt;flags &amp; 2) == 0</c> is false for its flags 0x06). So it fires on every frame the
    /// player stands in it and the session bank never moves. The dangerous alternative is reading
    /// pool byte 0 as the script: that byte is <c>0x99</c> = <c>op 25</c> on an empty operand stack,
    /// not a helipad script, so nothing is bought either — the test pins the session words of both
    /// arms and the per-frame fire instead.
    /// </summary>
    [ConftFact]
    public void ZoneWithoutAScriptFiresWithoutTouchingTheVm()
    {
        var scene = LoadScene();
        var pads = scene.PurchasePads!;
        var zone = pads.FindPad(106)!;
        Assert.Equal(0, zone.Script0Offset);
        Assert.Equal(0x99, pads.Pool.Bytes[0]);
        var session = pads.VmState.GameSession;
        Assert.Equal(0, session[RpnsVmState.SessionHelisP1]);

        var walker = scene.Walker!;
        walker.X = 0x200000;
        walker.Z = 0x200000;
        var unit = new SyntheticEntity(0x2150)
        {
            RecordId = 12, NodeId = 12, DispatchType = 1,
            X = zone.WorldX, Y = zone.WorldY, Z = zone.WorldZ,
        };
        scene.AddMapZoneCandidate(unit);
        var bank = (int[])session.Clone();
        var vars = (int[])pads.VmState.ScriptVars.Clone();
        scene.AdvanceSkyCaptain(1);

        Assert.True(zone.LastFire is { Triggered: true });       // :115 returns 1 on the box hit
        Assert.False(zone.LastFire!.Value.Accepted);             // no script ran
        Assert.False(scene.IsMapZoneReleased(zone));             // flags 2: the node stays linked
        // No script output and no score, heli or tank word moved: :106-108 never entered the VM.
        // (gGameSession[19] is the economy mirror of SyncFromEconomy, written before any script.)
        Assert.Equal(bank[6], session[6]);
        Assert.Equal(bank[RpnsVmState.SessionScoreP1], session[RpnsVmState.SessionScoreP1]);
        Assert.Equal(bank[RpnsVmState.SessionHelisP1], session[RpnsVmState.SessionHelisP1]);
        Assert.Equal(bank[RpnsVmState.SessionTanksP1], session[RpnsVmState.SessionTanksP1]);
        Assert.Equal(vars, pads.VmState.ScriptVars);

        // The cached target stays; the next frame tests it again and fires again without a script.
        scene.AdvanceSkyCaptain(1);
        Assert.True(zone.LastFire is { Triggered: true });
        Assert.False(zone.LastFire!.Value.Accepted);
        Assert.Equal(vars, pads.VmState.ScriptVars);
        Assert.Equal(bank[RpnsVmState.SessionHelisP1], session[RpnsVmState.SessionHelisP1]);
    }

    /// <summary>
    /// The same unit of another node id never wakes a zone: a zone fires only for a unit whose
    /// obj+0x14 (node id) equals the zone's tACT +0x1C (80031DE8.c:122). Zone 5 targets the Csac
    /// id 57; the unit carries 75, the id of zone 240.
    /// </summary>
    [ConftFact]
    public void ZoneIgnoresAUnitOfAnotherNodeId()
    {
        var scene = LoadScene();
        var zone = scene.PurchasePads!.FindPad(5)!;
        var walker = scene.Walker!;
        walker.X = 0x200000;
        walker.Y = 0;
        walker.Z = 0x200000;
        scene.AddMapZoneCandidate(new SyntheticEntity(0x2151)
        {
            RecordId = 75, NodeId = 75, DispatchType = 5,
            X = zone.WorldX, Y = zone.WorldY, Z = zone.WorldZ,
        });
        Assert.Null(scene.MapZoneTargetOf(zone));
        scene.AdvanceSkyCaptain(1);
        Assert.Null(scene.MapZoneTargetOf(zone));
        Assert.False(scene.IsMapZoneReleased(zone));
    }

    /// <summary>
    /// The scan is fed by the real spawner: a unit that <see cref="CsacUnitSpawner.Drain"/> puts into
    /// D_800A8F90 (<c>EntityWorld.CullActive</c>, gCullActiveEntitiesSolo 8004DF28.c:113-143, which
    /// <c>LevelScene.AdvancePurchasePads</c> then publishes, 80031DE8.c:119) is selected exactly like
    /// the table a test places by hand. Its node id comes from the record +0x14 that gInitEntityBase
    /// copies into obj+0x14 and its position from +0x50/+0x58, so the scan of :121-134 reads it as is.
    /// Node 240 (node id 75) is the target here.
    /// </summary>
    [ConftFact]
    public void SpawnedCsacUnitFeedsTheZoneScan()
    {
        var scene = LoadScene();
        var pads = scene.PurchasePads!;
        var zone = pads.FindPad(240)!;
        Assert.Equal(75, (int)zone.TargetId);
        var spawner = scene.CsacSpawner!;
        var slot = Assert.Single(spawner.Slots.Where(s => s.Record.Id == 75));
        slot.Timer = -1;                                    // due on the next drain (800616A0)
        slot.Flags = 0;

        // The walker stands on the zone as well: node id 12 is never a candidate of node 75
        // (:121-128), so the selection has to pick the unit.
        scene.Walker!.X = zone.WorldX;
        scene.Walker.Y = zone.WorldY;
        scene.Walker.Z = zone.WorldZ;
        Assert.True(zone.IsEnabled);

        scene.AdvanceSkyCaptain(0);
        var unit = Assert.Single(spawner.SpawnedUnits.Where(u => u.SourceSlot.Record.Id == 75));
        Assert.Contains(unit, scene.Entities!.Units);
        // The scan fields of the spawned unit are its own (record +0x14 of Csac 75).
        Assert.Equal(75, ((ICsacNodeId)unit).NodeId);

        // The unit stands in the zone box now, at the zone's actual world coordinates (1382656, y, 856454);
        // the next frame's scan (culled at the zone, where the walker stands) picks it and fires the node.
        unit.X = zone.WorldX;
        unit.Z = zone.WorldZ;
        unit.Y = zone.WorldY;
        Assert.True(LevelScene.MapZoneBoxAccepts(zone, unit));
        scene.Entities.CullActive(zone.WorldX, zone.WorldZ);
        Assert.Contains(scene.Entities.Active, e => ReferenceEquals(e, unit));
        Assert.False(zone.Released);
        // The node timer of the loader is a random byte (800319D4.c:142), so the rescan of :118 is
        // due whenever the frame delta passes it. The walk truncates its delta to a u8 (80031DE8.c:95)
        // and only a Draw turns D_8009FAA4 into a frame counter, so the rescan is armed directly with
        // a timer below the 5-tick delta of the next update.
        zone.MapZoneTimer = 1;
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);

        _output.WriteLine("zone 240 with the spawned unit: " + Dump(scene, zone, scene.Walker));
        var table = scene.MapZoneCandidates();
        Assert.Contains(table, c => ReferenceEquals(c, unit));
        // Measured: the scan culls at the zone centre (8004DF28.c:113-143) and the spawned unit is the
        // only candidate of node id 75 there, so :121-140 picks it and :159-168 fires the one-shot
        // node (flags 0 -> freed, 80031DE8.c:176-184).
        Assert.Equal(75, scene.MapZoneTargetOf(zone)?.RecordId);
        Assert.True(scene.IsMapZoneReleased(zone), "zone 240 kept its one-shot node");
    }

    /// <summary>
    /// The candidate gate of 80031DE8.c:121-128 on the zone 5 node (id 57): an id of 0 (retired,
    /// 8004D958.c:59), the type 0x15, the type 0x12, another node id and null slots are skipped; of
    /// two candidates on node 57 the nearer wins. Zone 5's retail center is (583680, 845568).
    /// </summary>
    [ConftFact]
    public void CandidateScanSkipsRetiredIdsAndTypes15And12AndPicksTheNearest()
    {
        var scene = LoadScene();
        var zone = scene.PurchasePads!.FindPad(5)!;
        Assert.Equal(57, (int)zone.TargetId);
        Assert.Equal((583680, 845568), (zone.WorldX, zone.WorldZ));
        SyntheticEntity Candidate(int id, short node, int type, int x, int z) => new(id)
        {
            RecordId = id, NodeId = node, DispatchType = type, X = x, Z = z,
        };
        var retiredId = Candidate(0, 57, 5, zone.WorldX, zone.WorldZ);               // id 0 (80031DE8.c:123)
        var typeFifteen = Candidate(1, 57, 0x15, zone.WorldX, zone.WorldZ);          // :125
        var typeTwelve = Candidate(2, 57, 0x12, zone.WorldX, zone.WorldZ);           // :126
        var otherNode = Candidate(3, 77, 5, zone.WorldX, zone.WorldZ);               // :124
        var far = Candidate(4, 57, 5, zone.WorldX + 0x1000, zone.WorldZ);
        var near = Candidate(5, 57, 5, zone.WorldX + 0x602, zone.WorldZ + 0x401);
        var items = new ICsacNodeId?[] { retiredId, typeFifteen, typeTwelve, otherNode, far, near, null };
        scene.SetMapZoneCandidates(items);
        _output.WriteLine($"candidates: {string.Join(", ", scene.MapZoneCandidates()!.Select(c => c?.ToString() ?? "null"))}");

        // The far candidate is 0x1000 east of zone 5, the near one 0x602 and 0x401
        // away: min is 0x401, so the octile distances are 0x1000 and 0xA03 - 0x401/2 (80031DE8.c:133-137).
        Assert.True(ReferenceEquals(near, scene.MapZoneTarget(zone)));
        var withoutNear = scene.MapZoneCandidates()!.Where(c => !ReferenceEquals(c, near)).ToArray();
        Assert.True(ReferenceEquals(far, scene.MapZoneTarget(zone, withoutNear)));
        var onlyRejected = new ICsacNodeId?[] { retiredId, typeFifteen, typeTwelve, otherNode, null };
        Assert.Null(scene.MapZoneTarget(zone, onlyRejected));
    }

    /// <summary>
    /// Zone 240's retail center is (1382656, y, 856320) and targets
    /// Csac unit 75. Both <see cref="LevelScene.MapZoneBoxAccepts"/> and
    /// <see cref="PurchasePadSystem.SelectMapZoneTarget"/> must compare full world coordinates in the
    /// same units without shifting <see cref="PurchasePad.WorldX"/>/<c>WorldY</c>/<c>WorldZ</c> by 8.
    /// </summary>
    [ConftFact]
    public void Zone240AcceptsUnit75AtActualWorldCenterAndPicksNearestInWorldUnits()
    {
        var scene = LoadScene();
        var zone = scene.PurchasePads!.FindPad(240)!;
        const int retailZ = 856320; // 800319D4.c:117–118 stores signed halfwords after shifting.
        var retailCenter = new SyntheticEntity(6) { RecordId = 75, NodeId = 75, X = 1382656, Z = retailZ };
        var rawCenter = new SyntheticEntity(7) { RecordId = 75, NodeId = 75, X = 1382656, Z = 856454 };
        Assert.Same(retailCenter, PurchasePadSystem.SelectMapZoneTarget(zone, new[] { rawCenter, retailCenter }));
        Assert.False(LevelScene.MapZoneBoxAccepts(zone, new SyntheticEntity(8)
        {
            X = 1382656, Y = zone.WorldY, Z = 876101,
        }));
        Assert.Equal((75, 1382656, retailZ), ((int)zone.TargetId, zone.WorldX, zone.WorldZ));

        var atCenter = new SyntheticEntity(1)
        {
            RecordId = 75, NodeId = 75, DispatchType = 5,
            X = zone.WorldX, Y = zone.WorldY, Z = zone.WorldZ,
        };
        var atEdgeOutside = new SyntheticEntity(2)
        {
            RecordId = 75, NodeId = 75, DispatchType = 5,
            X = zone.WorldX + zone.ExtentX, Y = zone.WorldY, Z = zone.WorldZ,
        };
        var shiftedBy8 = new SyntheticEntity(3)
        {
            RecordId = 75, NodeId = 75, DispatchType = 5,
            X = zone.WorldX << 8, Y = zone.WorldY << 8, Z = zone.WorldZ << 8,
        };
        Assert.True(LevelScene.MapZoneBoxAccepts(zone, atCenter));
        Assert.False(LevelScene.MapZoneBoxAccepts(zone, atEdgeOutside));
        Assert.False(LevelScene.MapZoneBoxAccepts(zone, shiftedBy8));

        // Nearest scan in actual world units: near is 0x400 away from (1382656, 856320),
        // far is 0x2000 away, and a bogus << 8 coordinate is hundreds of millions of units away.
        var near = new SyntheticEntity(4)
        {
            RecordId = 75, NodeId = 75, DispatchType = 5,
            X = zone.WorldX + 0x400, Y = zone.WorldY, Z = zone.WorldZ,
        };
        var far = new SyntheticEntity(5)
        {
            RecordId = 75, NodeId = 75, DispatchType = 5,
            X = zone.WorldX - 0x2000, Y = zone.WorldY, Z = zone.WorldZ,
        };
        Assert.True(ReferenceEquals(near, PurchasePadSystem.SelectMapZoneTarget(zone, new ICsacNodeId?[] { shiftedBy8, far, near })));
        Assert.True(ReferenceEquals(far, PurchasePadSystem.SelectMapZoneTarget(zone, new ICsacNodeId?[] { shiftedBy8, far })));
    }

    /// <summary>
    /// Finding 2 regression: <see cref="LevelScene.EnableObjectives"/> passes
    /// <see cref="CsacUnitSpawner.Random"/> into <see cref="PurchasePadSystem.LoadFromMission"/>, and
    /// the loaded system keeps that exact generator instance without consuming 30 extra catch-up draws.
    /// </summary>
    [ConftFact]
    public void EnableObjectivesConsumesSharedRngInRetailRequestOrder()
    {
        string path = LevelSceneRetirementTests.FindMission()!;

        var scene = new LevelScene(path) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        var spawner = scene.CsacSpawner!;
        var pads = scene.PurchasePads!;

        Assert.Same(spawner.Random, pads.Random);
        Assert.Equal(30, pads.ZoneLoadDraws);

        // Fixed vectors at seed 0x457 from the retail PRNG (80019668.c, 80019720.c).
        // VM draws first (80024A24.c:193–215), then CONFT's 57 positive Cfun periods,
        // 30 type-95 actors and 36 positive Csac delays in streamed order (80024EC8.c:108–202).
        Assert.Equal(new[] { 324, 442, 320, 606 }, pads.VmState.GameSession[6..10]);
        Assert.Equal(206, pads.FindPad(106)!.MapZoneTimer);
        Assert.Equal(228, pads.FindPad(58)!.MapZoneTimer);
        Assert.Equal(1461, Assert.Single(spawner.Slots, slot => slot.Record.Id == 33).Timer);
        Assert.Equal(1897196941, pads.Random.Next());
    }

    [ConftFact]
    public void MapZoneLoadQuantizesSurfaceHeightLikeRetailHarness()
    {
        byte[] mission = File.ReadAllBytes(LevelSceneRetirementTests.FindMission()!);
        // Port the +0x12345 and -1 surface results from leaf_systems_harness.c:717–772.
        var positive = PurchasePadSystem.LoadFromMission(mission, groundHeight: (_, _, _) => 0x12345)!;
        var negative = PurchasePadSystem.LoadFromMission(mission, groundHeight: (_, _, _) => -1)!;
        Assert.Equal(0x12300, positive.FindPad(240)!.WorldY);
        Assert.Equal(-0x100, negative.FindPad(240)!.WorldY);
    }

    [ConftFact]
    public void SimultaneousMapZoneRescansDrawInReverseLoadOrder()
    {
        var scene = LoadScene();
        var pads = scene.PurchasePads!;
        var first = pads.FindPad(106)!;
        var last = pads.FindPad(58)!;
        foreach (var pad in pads.Pads)
            if (pad != first && pad != last) pad.Flags |= 0x10;
        scene.SetMapZoneCandidates(Array.Empty<ICsacNodeId?>());
        pads.Random = new RetailRandom(0x457);
        first.MapZoneTimer = last.MapZoneTimer = 0;

        scene.AdvanceSkyCaptain(5);

        // Port the prepend case of leaf_systems_harness.c:760–769: the last loaded node
        // is visited first and receives jitter 28; the first loaded node gets 10.
        Assert.Equal(610, first.MapZoneTimer);
        Assert.Equal(628, last.MapZoneTimer);
    }

    /// <summary>
    /// Finding 3 regression: retail <c>func_80031DE8</c> (<c>80031DE8.c:151-159</c>,
    /// <c>asm/us/main.s:40447-40462</c>) rearms from the existing u16 timer with
    /// <c>+600 + (rand &amp; 0x3F)</c>; on a miss (<c>target == null</c>) it stores that value and
    /// skips both the second <c>+600</c> and <c>-= delta</c>, while on a hit (<c>target != null</c>)
    /// it adds another <c>+600</c> and falls through to <c>-= delta</c>, all wrapping in 16 bits.
    /// Covers both <see cref="LevelScene.AdvanceSkyCaptain"/> and
    /// <see cref="PurchasePadSystem.RescanMapZoneTarget"/> across hit, miss, and 16-bit wrap.
    /// </summary>
    [ConftFact]
    public void MapZoneRescanTimerMatchesRetailHitMissAnd16BitWrap()
    {
        var scene = LoadScene();
        var pads = scene.PurchasePads!;
        var zone = pads.FindPad(5)!; // TargetId = 57
        Assert.Equal(57, (int)zone.TargetId);
        // Disable all other map zones so only zone 5 consumes draws from pads.Random.
        foreach (var other in pads.Pads)
            if (other != zone && other.Kind == PurchasePadKind.MapZone)
                other.Flags |= 0x10;

        var candidate = new SyntheticEntity(777)
        {
            RecordId = 57, NodeId = 57, DispatchType = 5,
            // Outside box so one-shot zone 5 does not release itself during the timer assertions.
            X = zone.WorldX + zone.ExtentX + 0x4000, Y = zone.WorldY, Z = zone.WorldZ,
        };

        // 1) UpdateMapZoneTriggers MISS: oldTimer=3 < dt=5 -> (ushort)(3 + 600 + (r & 0x3F)), no +600, no -dt.
        var refRng = new RetailRandom(0x457);
        pads.Random = new RetailRandom(0x457);
        scene.SetMapZoneCandidates(Array.Empty<ICsacNodeId?>());
        // Consume the single draw used by SetMapZoneCandidates' immediate rescan on zone 5.
        _ = refRng.Next();
        zone.MapZoneTimer = 3;
        scene.AdvanceSkyCaptain(5);
        ushort expectedMiss = unchecked((ushort)(3 + 0x258 + (refRng.Next() & 0x3F)));
        Assert.Null(zone.MapZoneTarget);
        Assert.Equal(expectedMiss, zone.MapZoneTimer);

        // 2) UpdateMapZoneTriggers HIT: oldTimer=4 < dt=5 -> (ushort)(4 + 600 + (r & 0x3F) + 600 - 5).
        scene.SetMapZoneCandidates(new ICsacNodeId?[] { candidate });
        _ = refRng.Next();
        zone.MapZoneTimer = 4;
        scene.AdvanceSkyCaptain(5);
        ushort expectedHit = unchecked((ushort)(4 + 0x258 + (refRng.Next() & 0x3F) + 0x258 - 5));
        Assert.Same(candidate, zone.MapZoneTarget);
        Assert.Equal(expectedHit, zone.MapZoneTimer);

        // 3) 16-bit wrap on UpdateMapZoneTriggers MISS: oldTimer=0xFE00 < dt=0xFF00 wraps across 0x10000.
        scene.SetMapZoneCandidates(Array.Empty<ICsacNodeId?>());
        _ = refRng.Next();
        zone.MapZoneTimer = 0xFE00;
        scene.AdvanceSkyCaptain(0xFF00);
        ushort expectedWrapMiss = unchecked((ushort)(0xFE00 + 0x258 + (refRng.Next() & 0x3F)));
        Assert.Equal(expectedWrapMiss, zone.MapZoneTimer);
        Assert.InRange(zone.MapZoneTimer, (ushort)0x0058, (ushort)0x0097);

        // 4) 16-bit wrap on UpdateMapZoneTriggers HIT: oldTimer=0xFC00 (first +600 = 0xFE58 does not wrap,
        // second +600 = 0x00B0 wraps past 0xFFFF, then -dt wraps again in u16).
        scene.SetMapZoneCandidates(new ICsacNodeId?[] { candidate });
        _ = refRng.Next();
        zone.MapZoneTimer = 0xFC00;
        scene.AdvanceSkyCaptain(0xFD00);
        ushort expectedWrapHit = unchecked((ushort)(0xFC00 + 0x258 + (refRng.Next() & 0x3F) + 0x258 - 0xFD00));
        Assert.Equal(expectedWrapHit, zone.MapZoneTimer);

        // 5) Direct RescanMapZoneTarget: miss, hit, and 16-bit wrap with small delta.
        pads.TargetCandidates = Array.Empty<ICsacNodeId?>();
        zone.MapZoneTimer = 0xFE20;
        Assert.Null(pads.RescanMapZoneTarget(zone, delta: 7));
        Assert.Equal(unchecked((ushort)(0xFE20 + 0x258 + (refRng.Next() & 0x3F))), zone.MapZoneTimer);

        pads.TargetCandidates = new ICsacNodeId?[] { candidate };
        zone.MapZoneTimer = 0xFC10;
        Assert.Same(candidate, pads.RescanMapZoneTarget(zone, delta: 7));
        Assert.Equal(unchecked((ushort)(0xFC10 + 0x258 + (refRng.Next() & 0x3F) + 0x258 - 7)), zone.MapZoneTimer);
    }

    /// <summary>
    /// Finding 4 regression (P1 + hover gate): retail spawns players into <c>D_8009FA40</c> with
    /// flag 8 (<c>800579D4.c:207</c>), so <c>gCullActiveEntitiesSolo</c> (<c>8004DF28.c:142</c>)
    /// always includes them in <c>D_800A8F90</c>. Standing the walker at zone 107 (P1 tankpad,
    /// <c>TargetId == 12</c>, <c>Script0Offset == 28</c>) with no manual
    /// <see cref="LevelScene.AddMapZoneCandidate"/> must reject hover mode and buy tanks in walker
    /// mode (matching base: <c>ScriptVars[11] == 4</c>, <c>Score0 == 5 -> 0</c> over 300 frames).
    /// </summary>
    [ConftFact]
    public void ProductionWalkerAtZone107PurchasesTanksAndRespectsHoverGate()
    {
        string path = LevelSceneRetirementTests.FindMission()!;

        // Hover arm: player is selected as the production candidate for zone 107, but flag 4 rejects hover.
        var hoverScene = new LevelScene(path) { EnableCsacSpawner = true };
        hoverScene.EnableObjectives();
        var hoverPad = hoverScene.PurchasePads!.FindPad(107)!;
        var hoverWalker = new MechBody { X = hoverPad.WorldX, Y = hoverPad.WorldY, Z = hoverPad.WorldZ, Form = 1 };
        hoverScene.AttachWalker(hoverWalker);
        hoverScene.Objectives!.Score.Score[0] = 5;
        for (int i = 0; i < 300; i++) hoverScene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.Same(Assert.Single(hoverScene.PlayerTargets), hoverScene.MapZoneTargetOf(hoverPad));
        Assert.Equal(0, hoverScene.PurchasePads.VmState.ScriptVars[11]);
        Assert.Equal(5, hoverScene.ScoreSession(new HudSession()).Score0);

        // Switching the same walker to Form 0 buys immediately on the cached production target.
        hoverWalker.Form = 0;
        hoverScene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.Equal(1, hoverScene.PurchasePads.VmState.ScriptVars[11]);
        Assert.Equal(4, hoverScene.ScoreSession(new HudSession()).Score0);

        // Walker arm (exact Opus zone-107 probe): 5 score -> 5 queued tanks, 1 drained by T18 -> var11 == 4, score == 0.
        var walkScene = new LevelScene(path) { EnableCsacSpawner = true };
        walkScene.EnableObjectives();
        var pad = walkScene.PurchasePads!.FindPad(107)!;
        walkScene.AttachWalker(new MechBody { X = pad.WorldX, Y = pad.WorldY, Z = pad.WorldZ, Form = 0 });
        walkScene.Objectives!.Score.Score[0] = 5;
        for (int i = 0; i < 300; i++) walkScene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.Equal(4, walkScene.PurchasePads.VmState.ScriptVars[11]);
        Assert.Equal(0, walkScene.ScoreSession(new HudSession()).Score0);
    }

    /// <summary>
    /// Finding 4 regression (P2 + hover + no duplicate registrations): P2 attached via
    /// <see cref="LevelScene.AttachSecondPlayer"/> enters the production <c>D_800A8F90</c> scan and
    /// drives P2's map-zone helipad 108 (<c>TargetId == 16</c>, <c>Script0Offset == 54</c>, flags 0x06),
    /// respecting hover mode, and repeated attachments/frames never create duplicate player entries.
    /// </summary>
    [ConftFact]
    public void ProductionSecondPlayerDrivesMapZoneAndHoverGateWithoutDuplicates()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        var scene = new LevelScene(path) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        var pads = scene.PurchasePads!;
        var p2Helipad = pads.FindPad(108)!;
        Assert.Equal((PurchasePadKind.MapZone, 16, 0x06), (p2Helipad.Kind, (int)p2Helipad.TargetId, p2Helipad.Flags));
        Assert.NotEqual(0, p2Helipad.Script0Offset);

        var p1 = new MechBody { X = 0x200000, Y = 0, Z = 0x200000, Form = 0 };
        var p2 = new MechBody { X = p2Helipad.WorldX, Y = p2Helipad.WorldY, Z = p2Helipad.WorldZ, Form = 1 };
        scene.AttachWalker(p1);
        scene.AttachSecondPlayer(p2, 16);
        scene.Objectives!.Score.Score[1] = 5;

        // Re-attaching P1/P2 (and swapping P2 actorId and back) must not leave duplicate or stale registrations.
        scene.AttachWalker(p1);
        scene.AttachSecondPlayer(p2, 99);
        scene.AttachSecondPlayer(p2, 16);
        Assert.Equal(2, scene.PlayerTargets.Count);
        Assert.DoesNotContain(scene.PlayerTargets, t => t.RecordId == 99);

        // Hover mode: P2 is cached as zone 108's target when the loader timer expires, but flag 4 rejects purchase.
        // Note: Sky Captain also shares team 2's score bank in 1P mode, so observe pad 108's own fire record
        // and P2's heli counter directly.
        p2Helipad.MapZoneTimer = 0;
        for (int i = 0; i < 5; i++) scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        var p2Target = Assert.Single(scene.PlayerTargets, t => t.RecordId == 16);
        Assert.Same(p2Target, scene.MapZoneTargetOf(p2Helipad));
        Assert.Null(p2Helipad.LastFire);
        Assert.Equal(0, pads.VmState.GameSession[RpnsVmState.SessionHelisP2]);

        // Production candidate table must contain each player target exactly once across repeated frames.
        var table = scene.MapZoneCandidates();
        Assert.Equal(1, table.Count(c => c is PlayerTarget pt && pt.RecordId == scene.Actor.Id));
        Assert.Equal(1, table.Count(c => c is PlayerTarget pt && pt.RecordId == 16));

        // Walker mode: P2 purchases a helicopter on zone 108 without any manual AddMapZoneCandidate.
        scene.Objectives.Score.Score[1] = 5;
        p2.Form = 0;
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.True(p2Helipad.LastFire is { Triggered: true, Accepted: true });
        Assert.Equal(4, scene.Objectives.Score.Score[1]);
        Assert.Equal(1, pads.VmState.GameSession[RpnsVmState.SessionHelisP2]);

        var previousFire = p2Helipad.LastFire;
        scene.Objectives.Score.Score[1] = 5;
        scene.AttachSecondPlayer(new MechBody { X = 0x200000, Z = 0x200000 }, 99);
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);

        Assert.Null(scene.MapZoneTargetOf(p2Helipad));
        Assert.Equal(0x258, p2Helipad.MapZoneTimer);
        Assert.Equal(previousFire, p2Helipad.LastFire);
        Assert.Equal(5, scene.Objectives.Score.Score[1]);
        Assert.Equal(1, pads.VmState.GameSession[RpnsVmState.SessionHelisP2]);
    }

    /// <summary>
    /// Snapshot semantics for <see cref="LevelScene.MapZoneCandidates"/> and
    /// <see cref="PurchasePadSystem.TargetCandidates"/>: holding a returned table across subsequent
    /// <see cref="PurchasePadSystem.SyncTargetCandidates"/> rebuilds or
    /// <see cref="LevelScene.AddMapZoneCandidate"/> / <see cref="PurchasePadSystem.AddTargetCandidate"/>
    /// mutations keeps the original snapshot intact, never throws <see cref="InvalidOperationException"/>
    /// during enumeration, and rejects in-place mutation.
    /// </summary>
    [ConftFact]
    public void MapZoneCandidatesAndTargetCandidatesReturnIsolatedReadOnlySnapshots()
    {
        var scene = LoadScene();
        var pads = scene.PurchasePads!;

        var initialExtra = new SyntheticEntity(8101) { RecordId = 57, NodeId = 57, DispatchType = 5 };
        scene.AddMapZoneCandidate(initialExtra);
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);

        var sceneSnapshot = scene.MapZoneCandidates();
        var propSnapshot = pads.TargetCandidates;
        var originalItems = sceneSnapshot.ToArray();
        Assert.NotEmpty(originalItems);
        Assert.Equal(originalItems.Length, propSnapshot.Count);
        Assert.Contains(sceneSnapshot, c => ReferenceEquals(c, initialExtra));

        // Returned snapshots must reject list mutation.
        if (sceneSnapshot is IList<ICsacNodeId?> sceneList)
        {
            Assert.True(sceneList.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => sceneList.Add(null));
        }
        if (propSnapshot is IList<ICsacNodeId?> propList)
        {
            Assert.True(propList.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => propList.Add(null));
        }

        // Mutate the internal buffer while actively enumerating both snapshots.
        var addedLater = new SyntheticEntity(8102) { RecordId = 75, NodeId = 75, DispatchType = 5 };
        var seenFromScene = new List<ICsacNodeId?>();
        foreach (var item in sceneSnapshot)
        {
            if (seenFromScene.Count == 0)
            {
                scene.AddMapZoneCandidate(addedLater);
                pads.SyncTargetCandidates(Array.Empty<LiveEntity>(), Array.Empty<ICsacNodeId>());
            }
            seenFromScene.Add(item);
        }

        var seenFromProp = new List<ICsacNodeId?>();
        foreach (var item in propSnapshot)
        {
            if (seenFromProp.Count == 0)
                pads.AddTargetCandidate(new SyntheticEntity(8103) { RecordId = 7, NodeId = 7, DispatchType = 5 });
            seenFromProp.Add(item);
        }

        Assert.Equal(originalItems, seenFromScene);
        Assert.Equal(originalItems, seenFromProp);
        Assert.DoesNotContain(sceneSnapshot, c => ReferenceEquals(c, addedLater));

        // Fresh reads reflect the new state after SyncTargetCandidates + AddTargetCandidate.
        var fresh = scene.MapZoneCandidates();
        Assert.Contains(fresh, c => ReferenceEquals(c, addedLater));
    }

    private static string Dump(LevelScene scene, PurchasePad pad, MechBody walker) =>
        $"zone={pad.RecordId} nodeId={pad.TargetId} flags=0x{pad.Flags:X2} " +
        $"walker=({walker.X},{walker.Y},{walker.Z}) target={scene.MapZoneTargetOf(pad)?.RecordId.ToString() ?? "none"} " +
        $"released={scene.IsMapZoneReleased(pad)} session=[{string.Join(',', scene.PurchasePads!.VmState.GameSession)}]";

    /// <summary>
    /// A D_800A8F90 slot for the scan test: exactly the fields struct Target80031DE8 of
    /// 80031DE8.c:6-15 exposes (id, nodeId, type, x, y), with its own storage so a test can move it.
    /// </summary>
    private sealed class SyntheticEntity : ICsacNodeId
    {
        private readonly int _runtimeId;

        public SyntheticEntity(int runtimeId) => _runtimeId = runtimeId;

        public int RecordId { get; set; }
        public short NodeId { get; set; }
        public int DispatchType { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Radius { get; set; }

        public override string ToString() => $"#{_runtimeId} id={RecordId} node={NodeId} type=0x{DispatchType:X2}";
    }
}
