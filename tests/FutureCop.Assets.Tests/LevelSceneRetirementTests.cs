using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The scene drives the whole retirement chain of a CONFT outpost: player shots collide with it
/// (message 11, 800620D8.c:99-100), the lethal hit writes +0x8C (8002AA1C.c:114-121), the next frame
/// sends message 4 (8004CE04.c:59-60), the retire clears the id (8002AD58.c:89, 8004D958.c:59) and the
/// sweep's message 5 (8004D598.c:54) scores it (8002D664.c:179-225). No step below calls message 4,
/// message 5 or the scoring directly.
/// </summary>
public class LevelSceneRetirementTests
{
    private const uint FirePad = 0x00400000;

    internal static string? FindMission()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("FC_RETAIL_MIS");
        if (File.Exists(fromEnv))
        {
            string mission = Path.GetFullPath(fromEnv!);
            if (Path.GetFileName(mission).Equals("CONFT.MIS", StringComparison.OrdinalIgnoreCase))
                return mission;

            // FC_RETAIL_MIS normally points to CW/M1A1.MIS on the same extracted disc.
            var disc = Directory.GetParent(Path.GetDirectoryName(mission)!)?.FullName;
            if (ConftOnDisc(disc) is { } sibling)
                return sibling;
        }

        return ConftOnDisc(LevelTerrainTests.FindDisc());

        static string? ConftOnDisc(string? disc)
        {
            if (disc is null) return null;
            string candidate = Path.Combine(disc, "PA", "CONFT.MIS");
            return File.Exists(candidate) ? candidate : null;
        }
    }

    [RetailFact]
    public void ShootingADroneHeldOutpostRetiresItAndScoresMessage5()
    {
        var scene = new LevelScene(FindMission()!);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var field = scene.Objectives!;
        var world = scene.Entities!;
        var score = field.Score;
        var outpost = field.Objectives.Single(o => o.Id == 113);
        var child = outpost.ChildPart!;
        Assert.Equal(500, outpost.Health);
        Assert.Contains(outpost, world.Units);
        Assert.Contains(child, world.Units);

        // Message 10 from the Sky Captain (type 0x25, team 2) claims it with mode 2 (8002D1A0.c:197),
        // so its message 5 credits team F6 = 1, the player's team (8002D664.c:184).
        var drone = scene.SkyCaptain!;
        Assert.Equal(1, field.Touch(new CaptureCandidate(drone.Record!.Id, 0x25, drone.Team,
            outpost.X, outpost.Y, outpost.Z, 0)));
        Assert.Equal(2, outpost.CaptureMode);
        Assert.Equal(new[] { 0, 1 }, score.Score);                          // 8002D1A0.c:204-210

        // Walker placed 0xE000 south of the outpost, facing +z, holding the slot-1 trigger.
        var walker = scene.Walker!;
        walker.X = outpost.X;
        walker.Z = outpost.Z - 0xE000;
        walker.Angle = 0;
        int lethalFrame = -1, frame = 0;
        for (; frame < 600 && !outpost.Freed; frame++)
        {
            scene.HeldPadWord = FirePad;
            scene.AdvanceWalker(1, 0, AnalogPad.Digital);
            if (lethalFrame < 0 && outpost.Health < 0)
            {
                lethalFrame = frame;
                // The lethal hit is booked, the retire waits for the next frame's 8004CE04 pass.
                Assert.NotEqual(0, outpost.RuntimeId);
                Assert.NotEqual(0u, outpost.SourceUnitWord);
                Assert.Equal(new[] { 0, 1 }, score.Score);
            }
        }

        Assert.True(outpost.Freed, $"outpost still alive after {frame} frames, health {outpost.Health}");
        Assert.Equal(lethalFrame + 1, frame - 1);
        Assert.True(child.Freed);                                           // same 8004CE04 pass, child first
        Assert.DoesNotContain(outpost, field.Objectives);
        Assert.DoesNotContain(outpost, world.Units);
        Assert.Equal(31, field.Objectives.Count);
        // 8002D664.c:208-218: the player loop credits slot 0; the 1P state block bumps +0x38.
        Assert.Equal(new[] { 1, 1 }, score.Score);
        Assert.Equal(new[] { 1, 1 }, score.Total);
        Assert.Equal(1, score.OutpostStateCount);
        var hud = scene.ScoreSession(new HudSession());
        Assert.Equal((1, 1), (hud.Score0, hud.Score1));
    }

    [RetailFact]
    public void ShootingAnOpenOutpostRetiresItWithoutScoreAndItRespawns()
    {
        var scene = new LevelScene(FindMission()!) { EnableCsacSpawner = true };
        scene.EnableMechMovement();
        scene.EnableFiring();
        var field = scene.Objectives!;
        var outpost = field.Objectives.Single(o => o.Id == 113);
        var slot = scene.CsacSpawner!.Slots.Single(s => s.Record.Id == outpost.Id);
        Assert.Equal(1, slot.TotalSpawned);
        var walker = scene.Walker!;
        walker.X = outpost.X;
        walker.Z = outpost.Z - 0xE000;
        walker.Angle = 0;
        for (int frame = 0; frame < 600 && !outpost.Freed; frame++)
        {
            scene.HeldPadWord = FirePad;
            scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        }
        Assert.True(outpost.Freed);
        Assert.Equal(0, outpost.CaptureMode);
        Assert.Equal(new[] { 0, 0 }, field.Score.Score);                    // 8002D664.c:183 unkFE == 0
        Assert.Equal(0, slot.TotalSpawned);

        scene.AdvanceSkyCaptain(slot.Timer);
        Assert.DoesNotContain(field.Objectives, o => o.Id == outpost.Id);
        scene.AdvanceSkyCaptain(1);
        var replacement = Assert.Single(field.Objectives, o => o.Id == outpost.Id);
        Assert.NotSame(outpost, replacement);
        Assert.Equal(0, replacement.CaptureMode);
        Assert.Equal(outpost.MaxHealth, replacement.Health);
        Assert.Contains(replacement, scene.Entities!.Units);
        Assert.Contains(replacement.ChildPart!, scene.Entities.Units);
        Assert.Equal(1, slot.TotalSpawned);
    }

    [RetailFact]
    public void SceneDispatchesQuartileRefreshAndRetirementFootprintToRegistry()
    {
        var scene = new LevelScene(FindMission()!);
        scene.EnableObjectives();
        var registry = scene.ObjectRegistry;
        var world = scene.Entities!;
        var outpost = scene.Objectives!.Objectives.Single(o => o.Id == 113);
        var columns = new List<byte>();

        outpost.SlotIndex = 4;
        outpost.Flags19 = 0x10; // (Flags19 >> 3) & 0x1E selects slot 1
        // Health 250 of 500 is quartile 2 (8003B65C.c:35 uses strict < range >> 1), so
        // gGetSlotSpawnByte (8003B5AC.c:54,64) reads Unk1 on refresh; retire mode 2 reads Unk0.
        registry.SetSlotDescriptor(4, new SlotSpawnDescriptor(0, 0, 1, 0,
            new[] { default, new SlotSpawnItem(0x21, 0x72, 0, 0) }));
        registry.SetSpawnCell(0, 0x21, 1);
        registry.SetSpawnCell(0, 0x72, 1);
        registry.SpawnFactory = (_, column, _, _, _, _) => { columns.Add(column); return 1; };

        world.CullActive(outpost.X, outpost.Z);
        outpost.Health = 250;
        LiveEntity.FlagQuartileChange(outpost, world, outpost.MaxHealth);
        world.ProcessDeferredUpdates();
        Assert.Equal(new byte[] { 0x72 }, columns);

        outpost.Health = -1;
        world.ProcessDeferredUpdates();
        Assert.Equal(new byte[] { 0x72, 0x21 }, columns);
    }

    /// <summary>
    /// Retail solo-frame order (800271A4.c:178-180): <c>gCullActiveEntitiesSolo</c> runs before
    /// entity updates / weapon beam hit delivery (<c>DeliverBeamHits</c> walks <c>world.Active</c>).
    /// An entity spawned or entering active range between two <see cref="LevelScene.AdvanceWalker"/>
    /// calls must be hit by an active beam in that same frame, not delayed by one frame.
    /// </summary>
    [ConftFact]
    public void ActiveBeamHitsNewlySpawnedAndNewlyInRangeEntitiesInSameAdvanceWalkerFrame()
    {
        var scene = new LevelScene(FindMission()!);
        scene.EnableMechMovement();
        scene.EnableFiring();

        var world = scene.Entities!;
        var weapons = scene.Weapons!;
        var walker = scene.Walker!;
        weapons.BeamHitWorld = world;
        weapons.BeamSourceWord = (uint)scene.Actor.Id | 0x8000u;
        var beamDef = weapons.RecordOf(WeaponSlots.PrimarySlot)!.HalfB!;
        Assert.Equal(0x0A, beamDef.Action);
        Assert.True(beamDef.DamageWord > 0);

        // Start one entity outside ActiveRange so frame 0 excludes it from world.Active.
        var movedIn = new BeamTargetEntity(9001, walker.X + EntityWorld.ActiveRange + 0x10000, walker.Y, walker.Z);
        world.Spawn(movedIn);
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.DoesNotContain(movedIn, world.Active);

        // Between two AdvanceWalker calls: move the out-of-range entity into range and spawn a new
        // entity (EntityWorld.Spawn only appends to Units, not Active), both on the beam segment.
        movedIn.MoveTo(walker.X + 0x1000, walker.Y, walker.Z);
        var spawned = new BeamTargetEntity(9002, walker.X + 0x1800, walker.Y, walker.Z);
        world.Spawn(spawned);
        Assert.DoesNotContain(movedIn, world.Active);
        Assert.DoesNotContain(spawned, world.Active);

        // Beam along +X from walker with length 0x2000 covering both targets (0x1000 and 0x1800).
        weapons.Beams.Add(new Beam(beamDef, walker.X, walker.Y, walker.Z, new[] { 0x10000, 0, 0 }, 0x200));

        scene.AdvanceWalker(1, 0, AnalogPad.Digital);

        var spawnedHit = Assert.Single(spawned.Hits);
        var movedInHit = Assert.Single(movedIn.Hits);
        Assert.Equal((short)beamDef.ObjWord00, spawnedHit.Damage);
        Assert.Equal((short)beamDef.ObjWord00, movedInHit.Damage);
    }

    private sealed class BeamTargetEntity : LiveEntity
    {
        private readonly int _recordId;
        private int _x, _y, _z;

        public List<DirectHit> Hits { get; } = new();

        public BeamTargetEntity(int recordId, int x, int y, int z)
        {
            _recordId = recordId;
            Health = 1000;
            MaxHealth = 1000;
            MoveTo(x, y, z);
        }

        public void MoveTo(int x, int y, int z)
        {
            _x = x;
            _y = y;
            _z = z;
            Coord = new CollisionCoord(x, y, z, 0x800, 0x400, 0x400);
        }

        public override int RecordId => _recordId;
        public override int DispatchType => 5;
        public override (int X, int Z) GroundPosition => (_x, _z);

        public override int HandleMessage(int message, EntityWorld world, object? data)
        {
            if (message == 14 && data is DirectHit hit) Hits.Add(hit);
            return BaseHandler(message, this, world, data);
        }
    }
}

/// <summary>Skips tests that need CONFT.MIS when that mission is absent.</summary>
public sealed class ConftFactAttribute : FactAttribute
{
    public ConftFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL") != "1" &&
            LevelSceneRetirementTests.FindMission() is null)
            Skip = "Retail mission PA/CONFT.MIS is absent.";
    }
}
