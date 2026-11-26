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
/// release of the node (80031DE8.c:176-184); the twelve zones ship without an RPNS slot-0 script in
/// CONFT.MIS (their Cact slots 0 and 1 are 0), so no session word can observe them.
/// </summary>
public class LevelSceneMapZoneTargetTests
{
    // 80031DE8.c:148-158: a rescan arms 600 + (rand & 0x3F) and another 600 when it found nothing;
    // 80031C74.c:115: a node without flag 2 is released (unlinked, freed) on its first box hit.
    private const int EmptyRescanTicks = 1200;

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
        var centre = new PlayerTarget(12, 100 << 8, 0, 200 << 8);
        Assert.True(LevelScene.MapZoneBoxAccepts(node, centre));
        // One axis past the extent (0x1001 > 0x1000) fails the whole test.
        Assert.False(LevelScene.MapZoneBoxAccepts(node, new PlayerTarget(12, (100 << 8) + 0x1001, 0, 200 << 8)));
        Assert.False(LevelScene.MapZoneBoxAccepts(node, new PlayerTarget(12, 100 << 8, 0, (200 << 8) - 0x1001)));
        Assert.False(LevelScene.MapZoneBoxAccepts(node, new PlayerTarget(12, 100 << 8, 0x401, 200 << 8)));
        // The radius of the target (obj+0x68) is subtracted from the margin: 0x1002 out with radius 2
        // gives |delta| - radius - extent == 0, still a refusal; radius 3 goes negative and passes.
        var offset = new PlayerTarget(12, (100 << 8) + 0x1002, 0, 200 << 8) { Radius = 2 };
        Assert.False(LevelScene.MapZoneBoxAccepts(node, offset));
        offset.Radius = 3;
        Assert.True(LevelScene.MapZoneBoxAccepts(node, offset));

        // 80031C74.c:102: flag 4 and a busy player refuse, a clear control block and a non-player pass.
        Assert.False(LevelScene.MapZoneBoxAccepts(node, new PlayerTarget(12, 100 << 8, 0, 200 << 8, busy: true)));
        Assert.True(LevelScene.MapZoneBoxAccepts(node, centre));
        node.Flags &= unchecked((byte)~4);
        Assert.True(LevelScene.MapZoneBoxAccepts(node, new PlayerTarget(12, 100 << 8, 0, 200 << 8, busy: true)));
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
        // the zone centre: octile distance 0 (80031DE8.c:130-134). The loader's random byte
        // (800319D4.c:142) can arm the node above the walk of frame 1, so the rescan is forced here
        // with the zero delta of the frame that runs it.
        scene.AddMapZoneCandidate(new PlayerTarget(12, zone.WorldX << 8, zone.WorldY << 8, zone.WorldZ << 8));
        Assert.Equal(12, scene.MapZoneTargetOf(zone)?.RecordId);
        Assert.InRange(zone.MapZoneTimer, 0x258, 0x297);                   // 600 + jitter

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
            X = zone.WorldX << 8, Y = zone.WorldY << 8, Z = zone.WorldZ << 8,
        });
        scene.AdvanceSkyCaptain(1);
        Assert.Equal(12, zone.MapZoneTargetId);
        Assert.Equal(12, scene.MapZoneTargetOf(zone)?.RecordId);

        // No candidate at all: the re-selection empties the cache (:148-158) and the box test drops
        // out for the 1200 + jitter window; the node keeps itself (flag 2), only the target is gone.
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
        // dispatch type the scan accepts (:122-124). Scene coordinates are C coordinates (<<8).
        var unit = new SyntheticEntity(0x2150)
        {
            RecordId = csacId, NodeId = (short)csacId, DispatchType = 5,
            X = zone.WorldX << 8, Y = zone.WorldY << 8, Z = zone.WorldZ << 8,
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
            X = zone.WorldX << 8, Y = zone.WorldY << 8, Z = zone.WorldZ << 8,
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

        // The unit stands in the zone box now, like a tank that walked in; the next frame's scan
        // (culled at the zone, which is where the walker is) picks it and fires the node.
        unit.X = zone.WorldX << 8;
        unit.Z = zone.WorldZ << 8;
        unit.Y = zone.WorldY << 8;
        Assert.True(LevelScene.MapZoneBoxAccepts(zone, unit));
        // The node timer of the loader is a random byte (800319D4.c:142), so the rescan of :118 is
        // due whenever the frame delta passes it; frame 0 has a delta of 0 truncated to a u8 (:95),
        // so the rescan is armed directly here instead of waiting 67 frames.
        zone.MapZoneTimer = -1;
        // D_8009FAA4 starts at 0 and only a Draw turns it into a real frame counter; the pad walk
        // truncates its delta to a u8 (80031DE8.c:95), so frame 0 would never reach the box test.
        Assert.Equal(0, scene.PurchaseFrameTime);
        scene.AdvanceDrawFrame();
        // The unit spawns at the origin of its Csac slot and is placed at a world ValidPoint; the
        // test moves it in front of the walker, the zone centre it later stands on.
        unit.X = zone.WorldX << 8;
        unit.Z = zone.WorldZ << 8;
        scene.Entities.CullActive(zone.WorldX, zone.WorldZ);
        Assert.Contains(scene.Entities.Active, e => ReferenceEquals(e, unit));
        Assert.False(zone.Released);
        for (int frame = 0; frame < 2; frame++) scene.AdvanceSkyCaptain(0);

        _output.WriteLine("zone 240 with the spawned unit: " + Dump(scene, zone, scene.Walker));
        var table = scene.MapZoneCandidates();
        Assert.Contains(table, c => ReferenceEquals(c, unit));
        Assert.Equal(75, scene.MapZoneTargetOf(zone)?.RecordId);
        Assert.True(scene.IsMapZoneReleased(zone), "zone 240 kept its one-shot node");
    }

    /// <summary>
    /// The candidate gate of 80031DE8.c:121-128 on the zone 5 node (id 57): an id of 0 (retired,
    /// 8004D958.c:59), the type 0x15, the type 0x12, another node id and null slots are skipped; of
    /// two candidates on node 57 the nearer wins. Zone 5 sits at (583680, 845700).
    /// </summary>
    [ConftFact]
    public void CandidateScanSkipsRetiredIdsAndTypes15And12AndPicksTheNearest()
    {
        var scene = LoadScene();
        var zone = scene.PurchasePads!.FindPad(5)!;
        Assert.Equal(57, (int)zone.TargetId);
        SyntheticEntity Candidate(int id, short node, int type, int x, int z) => new(id)
        {
            RecordId = id, NodeId = node, DispatchType = type, X = x << 8, Z = z << 8,
        };
        var retiredId = Candidate(0, 57, 5, 583680, 845700);               // id 0 (80031DE8.c:121)
        var typeFifteen = Candidate(1, 57, 0x15, 583680, 845700);          // :123
        var typeTwelve = Candidate(2, 57, 0x12, 583680, 845700);           // :124
        var otherNode = Candidate(3, 77, 5, 583680, 845700);               // :122
        var far = Candidate(4, 57, 5, 583680 + 0x1000, 845700);
        var near = Candidate(5, 57, 5, 583680 + 0x602, 845700 + 0x401);
        var items = new ICsacNodeId?[] { retiredId, typeFifteen, typeTwelve, otherNode, far, near, null };
        scene.SetMapZoneCandidates(items);
        _output.WriteLine($"candidates: {string.Join(", ", scene.MapZoneCandidates()!.Select(c => c?.ToString() ?? "null"))}");

        // The far candidate is 0x1000 east of zone 5 (583680/845700), the near one 0x602 and 0x401
        // away: min is 0x401, so the octile distances are 0x1000 and 0xA03 + 0x401/2 (80031DE8.c:130-134).
        Assert.True(ReferenceEquals(near, scene.MapZoneTarget(zone)));
        var withoutNear = scene.MapZoneCandidates()!.Where(c => !ReferenceEquals(c, near)).ToArray();
        Assert.True(ReferenceEquals(far, scene.MapZoneTarget(zone, withoutNear)));
        var onlyRejected = new ICsacNodeId?[] { retiredId, typeFifteen, typeTwelve, otherNode, null };
        Assert.Null(scene.MapZoneTarget(zone, onlyRejected));
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
