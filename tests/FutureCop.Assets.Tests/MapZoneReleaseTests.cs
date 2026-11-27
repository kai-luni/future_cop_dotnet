using System.Buffers.Binary;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

public sealed class MapZoneReleaseTests
{
    [ConftTheory]
    [InlineData(-1)]
    [InlineData(12)]
    public void OneShotUnlinksWhileOverlappingStayZoneKeepsFiring(short targetId)
    {
        // 80031DE8.c:111-172 walks either the cached target or both player slots.
        // 80031C74.c:106-115 releases a hit without flag 2 even if there is no script.
        // 80031DE8.c:176-184 continues with the next node after unlinking.
        var scene = LoadTwoZones(targetId, firstFlags: 0, secondFlags: 2);
        var pads = scene.PurchasePads!;
        var stay = pads.FindPad(106)!;
        var oneShot = pads.FindPad(107)!;
        scene.AttachWalker(new MechBody { X = oneShot.WorldX, Y = oneShot.WorldY, Z = oneShot.WorldZ });
        scene.Objectives!.Score.Score[0] = 7;

        scene.AdvanceSkyCaptain(1);

        Assert.Equal(new PurchaseResult(true, false, 0, 7, 7), oneShot.LastFire);
        Assert.Equal(new PurchaseResult(true, false, 0, 7, 7), stay.LastFire);
        Assert.True(scene.IsMapZoneReleased(oneShot));
        Assert.False(scene.IsMapZoneReleased(stay));

        // The freed node is no longer searchable by its tACT tag (80031BEC.c:48-60).
        // The overlapping stay zone still receives disable/enable commands by its own tag.
        var vm = pads.VmState;
        SendMapZoneCommand(vm, 107, 0x7C, 0);
        Assert.Equal(0, oneShot.Flags);
        Assert.DoesNotContain(oneShot, pads.ActiveMapZones);
        SendMapZoneCommand(vm, 106, 0x7C, 0);
        Assert.Equal(0x12, stay.Flags);
        SendMapZoneCommand(vm, 106, 0x7C, 1);
        Assert.Equal(2, stay.Flags);

        scene.Objectives.Score.Score[0] = 11;
        scene.AdvanceSkyCaptain(1);
        Assert.Equal(new PurchaseResult(true, false, 0, 7, 7), oneShot.LastFire);
        Assert.Equal(new PurchaseResult(true, false, 0, 11, 11), stay.LastFire);
    }

    [ConftFact]
    public void UnlinkingTheHeadDefersTheNextZoneUntilTheNextWalk()
    {
        // 80031DE8.c:176-184,201-204: after freeing the head, node = prev = null
        // ends this frame's walk. The live successor becomes the next frame's head.
        var scene = LoadTwoZones(-1, firstFlags: 0, secondFlags: 2, firstRecordId: 58);
        var pads = scene.PurchasePads!;
        var freedHead = pads.FindPad(58)!;
        var stay = pads.FindPad(106)!;
        Assert.Same(freedHead, pads.ActiveMapZones[^1]);
        scene.AttachWalker(new MechBody { X = freedHead.WorldX, Y = freedHead.WorldY, Z = freedHead.WorldZ });

        scene.AdvanceSkyCaptain(1);
        Assert.True(freedHead.Released);
        Assert.Null(stay.LastFire);

        scene.AdvanceSkyCaptain(1);
        Assert.Equal(new PurchaseResult(true, false, 0, 0, 0), stay.LastFire);
    }

    [ConftFact]
    public void MapZoneCommandsAffectOnlyTheFirstMatchingNodeAndUnlinkOnTheNextWalk()
    {
        // Migrate the state sequence from leaf_more_harness.c:949-962, using the real
        // RPNS command dispatch (80069D68.c:379-384; 8004DC64.c:46-49).
        var scene = LoadTwoZones(-1, firstFlags: 0x40, secondFlags: 0x40, commandTag: 23);
        var pads = scene.PurchasePads!;
        var matches = pads.Pads.Where(pad => pad.RecordId == 23).ToArray();
        Assert.Equal(2, matches.Length);
        var second = matches[0];
        var first = matches[1]; // 800319D4.c:114-119 prepends the later request.
        var vm = pads.VmState;
        first.MapZoneTimer = 127;

        SendMapZoneCommand(vm, 99, 0x7C, 0);
        SendMapZoneCommand(vm, 23, 0x7B, 0);
        Assert.Equal((0x40, 127, false), ((int)first.Flags, (int)first.MapZoneTimer, first.Released));

        SendMapZoneCommand(vm, 23, 0x7C, 0);
        Assert.Equal((0x50, 0x40), ((int)first.Flags, (int)second.Flags));
        SendMapZoneCommand(vm, 23, 0x7C, 2);
        Assert.Equal((0x50, 127, false), ((int)first.Flags, (int)first.MapZoneTimer, first.Released));
        SendMapZoneCommand(vm, 23, 0x7C, 1);
        Assert.Equal((0x40, 0, false), ((int)first.Flags, (int)first.MapZoneTimer, first.Released));

        // The harness checks the status byte survives re-enable. Observe that through
        // 80031DE8.c:105-110,176-184, which removes the marked node before testing it.
        scene.AdvanceSkyCaptain(1);
        Assert.True(first.Released);
        Assert.Null(first.LastFire);
        Assert.False(second.Released);
        SendMapZoneCommand(vm, 23, 0x7C, 0);
        Assert.Equal((0x40, 0x50), ((int)first.Flags, (int)second.Flags));
    }

    [ConftFact]
    public void LoaderPreservesSignedGeometryAndQueryModesFromLeafSystems()
    {
        // leaf_systems_harness.c:717-778: convert stored coordinates << 8 and
        // stored extents << 4 for the managed world-space box (80031C74.c:84-100).
        byte[] mission = File.ReadAllBytes(LevelSceneRetirementTests.FindMission()!);
        var records = MissionEntities.Parse(mission);
        var source = records.Single(record => record.Id == 106);
        SetCommandTag(mission, source, 0x11223344);
        SetLoaderRecord(mission.AsSpan(source.ChunkOffset + 8), 0x123400, -0x200, 0x0080, 0x00C8, 0x0190, 0, 0x7E, 0x0BEE);
        var positive = LoadAtFixedHeight(0x11223344, 0x12345, -1);
        Assert.Equal((0x123400, 0x12300, -0x200), (positive.WorldX, positive.WorldY, positive.WorldZ));
        Assert.Equal((0x640, 0xC80, 0x400, 0x7E, 0x0BEE), Shape(positive));

        SetCommandTag(mission, source, 0x55667788);
        SetLoaderRecord(mission.AsSpan(source.ChunkOffset + 8), -0x100, 0x100, -2, 2, 4, 1, 1, -1);
        var negative = LoadAtFixedHeight(0x55667788, -1, -2);
        Assert.Equal((-0x100, -0x100, 0x100), (negative.WorldX, negative.WorldY, negative.WorldZ));
        Assert.Equal((0x10, 0x20, -0x10, 1, -1), Shape(negative));

        // leaf_systems_harness.c:780-795: the general case subtracts two from the signed byte.
        foreach (var (surfaceType, mode) in new[] { (5, 3), (2, 0), (-1, -3) })
        {
            mission[source.ChunkOffset + 8 + 0x1A] = unchecked((byte)surfaceType);
            _ = LoadAtFixedHeight(0x55667788, 0, mode);
        }

        PurchasePad LoadAtFixedHeight(int tag, int height, int expectedMode)
        {
            var data = mission.AsSpan(source.ChunkOffset + 8);
            int recordX = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
            int recordZ = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
            var modes = new List<int>();
            var pads = PurchasePadSystem.LoadFromMission(mission, groundHeight: (x, z, mode) =>
            {
                if (x == recordX && z == recordZ) modes.Add(mode);
                return height;
            })!;
            Assert.Equal(expectedMode, Assert.Single(modes));
            var pad = pads.FindPad(tag & 0xFFFF)!;
            Assert.Equal(tag, pad.MapZoneCommandTag);
            return pad;
        }

        static (int, int, int, int, int) Shape(PurchasePad pad) =>
            (pad.ExtentX, pad.ExtentY, pad.ExtentZ, pad.Flags, pad.TargetId);
    }

    private static void SetLoaderRecord(Span<byte> data, int x, int z, short rawZ, short rawX, short rawY,
        byte surface, byte flags, short targetId)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data[8..], x);
        BinaryPrimitives.WriteInt32LittleEndian(data[16..], z);
        BinaryPrimitives.WriteInt16LittleEndian(data[0x14..], rawZ);
        BinaryPrimitives.WriteInt16LittleEndian(data[0x16..], rawX);
        BinaryPrimitives.WriteInt16LittleEndian(data[0x18..], rawY);
        data[0x1A] = surface;
        data[0x1B] = flags;
        BinaryPrimitives.WriteInt16LittleEndian(data[0x1C..], targetId);
    }

    private static LevelScene LoadTwoZones(short targetId, byte firstFlags, byte secondFlags, int? commandTag = null, int firstRecordId = 107)
    {
        byte[] mission = File.ReadAllBytes(LevelSceneRetirementTests.FindMission()!);
        var records = MissionEntities.Parse(mission);
        var secondRecord = records.Single(r => r.Id == 106);
        var firstRecord = records.Single(r => r.Id == firstRecordId);
        var second = mission.AsSpan(secondRecord.ChunkOffset + 8);
        var first = mission.AsSpan(firstRecord.ChunkOffset + 8);
        if (commandTag is int tag)
        {
            SetCommandTag(mission, secondRecord, tag);
            SetCommandTag(mission, firstRecord, tag);
        }
        second[8..0x1B].CopyTo(first[8..0x1B]);
        first[0x1B] = firstFlags;
        second[0x1B] = secondFlags;
        BinaryPrimitives.WriteInt16LittleEndian(first[0x1C..], targetId);
        BinaryPrimitives.WriteInt16LittleEndian(second[0x1C..], targetId);
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, mission);
            var scene = new LevelScene(path) { EnableCsacSpawner = true };
            scene.EnableObjectives();
            var pads = scene.PurchasePads!;
            foreach (var pad in pads.Pads.Where(p => p.Kind == PurchasePadKind.MapZone))
            {
                if (commandTag.HasValue ? pad.RecordId == commandTag.Value : (pad.RecordId == 106 || pad.RecordId == firstRecordId))
                {
                    pad.SetMapZoneScript(0);
                    pad.MapZoneTimer = 0;
                }
                else
                {
                    pad.Flags |= 0x10;
                }
            }
            scene.AttachWalker(new MechBody { X = 0x200000, Z = 0x200000 });
            return scene;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void SetCommandTag(byte[] mission, EntityRecord record, int tag)
    {
        BinaryPrimitives.WriteInt32LittleEndian(mission.AsSpan(record.HeaderOffset + 0x1C), tag);
        BinaryPrimitives.WriteInt32LittleEndian(mission.AsSpan(record.ChunkOffset + 8), tag);
    }

    private static void SendMapZoneCommand(RpnsVmState vm, short id, byte command, byte mode)
    {
        // 80069D68.c:205-209,379-384: push a 16-bit tag, an opcode, and a mode; then dispatch.
        RpnsScriptVm.Execute(new byte[]
        {
            3, (byte)(id >> 8), (byte)id, (byte)(0x80 | command), (byte)(0x80 | mode), 0x38, 0,
        }, vm);
    }
}
