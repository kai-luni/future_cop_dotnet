using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// End-to-end checks of the level start view on the retail map PA/CONFT.MIS: terrain packets come
/// out of the ported layer-1 path, the walker stands on the ported ground height at the clip-rect
/// centre and lands in the middle of the 320 x 240 frame. These are viewer checks, not parity: the
/// rasterizer and the orbit camera are viewer code.
/// </summary>
public class LevelSceneTests
{
    private static string? ConftPath()
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc == null ? null : ModulePath.Resolve(disc, "conft.mis");
        if (path == null) Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
        return path;
    }

    [Fact]
    public void WalkerStandsOnTheGroundAtTheClipRectCentre()
    {
        string? path = ConftPath();
        if (path == null) return;
        var scene = new LevelScene(path);
        var clip = scene.Map.Header.WorldClipRect;
        Assert.Equal((clip[2] + clip[3]) / 2, scene.MechWorld.X);
        Assert.Equal((clip[0] + clip[1]) / 2, scene.MechWorld.Z);
        Assert.Equal(LevelGround.Height(scene.Map, scene.MechWorld.X, 0x100000, scene.MechWorld.Z, out _), scene.MechWorld.Y);
        Assert.NotEqual(0, scene.MechWorld.Y);
        Assert.Equal(12, scene.Actor.Id);
    }

    [Fact]
    public void FrameShowsTerrainAndTheMechInTheMiddle()
    {
        string? path = ConftPath();
        if (path == null) return;
        var scene = new LevelScene(path);
        foreach (double yaw in new[] { 0.0, 30.0, 75.0, 210.0 })
        {
            var camera = new LevelCamera(yaw, 35.0, 0x1C00);
            scene.DrawMech = false;
            scene.Render(camera);
            var terrain = (byte[])scene.Rgb.Clone();
            Assert.True(scene.TilePackets > 100, $"yaw {yaw}: {scene.TilePackets} terrain packets");
            Assert.True(scene.VisibleCells > 0);
            int lit = 0;
            var colours = new HashSet<int>();
            for (int i = 0; i < terrain.Length; i += 3)
            {
                if (terrain[i] != 0 || terrain[i + 1] != 0 || terrain[i + 2] != 0) lit++;
                colours.Add(terrain[i] << 16 | terrain[i + 1] << 8 | terrain[i + 2]);
            }
            Assert.True(lit > LevelScene.Width * LevelScene.Height * 9 / 10, $"yaw {yaw}: only {lit} terrain pixels");
            Assert.True(colours.Count > 200, $"yaw {yaw}: {colours.Count} colours, textures are not sampled");

            scene.DrawMech = true;
            scene.Render(camera);
            Assert.True(scene.MechFaces > 50);
            int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1, changed = 0;
            for (int y = 0; y < LevelScene.Height; y++)
                for (int x = 0; x < LevelScene.Width; x++)
                {
                    int o = (y * LevelScene.Width + x) * 3;
                    if (scene.Rgb[o] == terrain[o] && scene.Rgb[o + 1] == terrain[o + 1] && scene.Rgb[o + 2] == terrain[o + 2]) continue;
                    changed++;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
            Assert.True(changed > 300, $"yaw {yaw}: mech covers only {changed} pixels");
            // The mech only touches its own footprint: a frame-wide difference means the terrain
            // order depends on the mech (unstable sort).
            Assert.True(maxX - minX < 60 && maxY - minY < 60, $"yaw {yaw}: mech box {minX},{minY}..{maxX},{maxY}");
            int cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            Assert.InRange(cx, 160 - 12, 160 + 12);
            Assert.InRange(cy, 120 - 12, 120 + 12);
        }
    }

    // ---- weapon pickups (task 21) ------------------------------------------------------------

    [Fact]
    public void EveryLivePickupDrawsExactlyOneModelAtItsOwnPosition()
    {
        string? path = ConftPath();
        if (path == null) return;
        var scene = new LevelScene(path);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var pickups = Assert.IsType<PickupItems>(scene.Pickups);

        // 11 type-0x10 records, every one carrying an aRSL Cobj entry and its own x/z.
        Assert.Equal(11, pickups.Items.Count);
        Assert.Equal(11, pickups.Items.Select(i => (i.X, i.Z)).Distinct().Count());

        int frames = 0;
        foreach (double yaw in new[] { 0.0, 90.0, 180.0, 270.0 })
        foreach (double pitch in new[] { 15.0, 25.0, 40.0 })
        foreach (int distance in new[] { 0x0C00, 0x1C00, 0x3000 })
        {
            scene.Render(new LevelCamera(yaw, pitch, distance));
            frames++;
            var drawn = scene.DrawnPickupPlacements;
            var who = $"yaw {yaw} pitch {pitch} d {distance:#x}";

            // The real draw pass walks Pickups.Items; it must yield one entry per live pickup.
            Assert.Equal(pickups.Items.Count(i => !i.Collected), drawn.Count);
            Assert.All(drawn, p => Assert.NotNull(p.Model));

            // One model at one world position: no position and no (position, model) pair repeats.
            Assert.Equal(drawn.Count, drawn.Select(p => (p.X, p.Y, p.Z)).Distinct().Count());
            Assert.Equal(drawn.Count, drawn.Select(p => (p.X, p.Y, p.Z, p.Model!.Id)).Distinct().Count());

            // Each entry is the model its own pickup named, at that pickup's own placement.
            var uncollected = pickups.Items.Where(i => !i.Collected).Select((i, n) => (i, n));
            foreach (var (item, n) in uncollected)
            {
                Assert.Equal(item.X, drawn[n].X);
                Assert.Equal(item.Y, drawn[n].Y);
                Assert.Equal(item.Z, drawn[n].Z);
                Assert.Equal(item.ModelId, drawn[n].Model!.Id);
            }
        }
        Assert.Equal(36, frames);
    }

    [Fact]
    public void APickupWhoseModelIdNamesNoMissionModelDrawsNothing()
    {
        string? path = ConftPath();
        if (path == null) return;
        var file = new MisFile(path);
        var map = LevelMap.FromMisFile(path);
        var registry = WeaponCatalog.LoadRegistry(file);

        // The record loses its aRSL Cobj entry: its only pair becomes the NULL terminator
        // (func_8004D6D4:143-150 resolves nothing, so the spawn path attaches no model).
        int rec = WeaponPickups.FromMission(file.Bytes, registry, map).Items[0].RecordOffset;
        int list = rec - 0x08 + 0x44;
        Assert.Equal(ChunkReader.TagValue("Cobj"),
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(file.Bytes.AsSpan(list + 12)));
        var data = (byte[])file.Bytes.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(list + 12), ChunkReader.TagValue("NULL"));
        var field = WeaponPickups.FromMission(data, registry, map);
        Assert.Equal(WeaponPickups.NoModel, field.Items[0].ModelId);

        var scene = new LevelScene(path);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var camera = new LevelCamera(270.0, 15.0, 0x0C00);

        // The intact field first: 11 live pickups, each drawing its own model.
        scene.Render(camera);
        var intact = scene.DrawnPickupPlacements.ToList();
        Assert.Equal(11, intact.Count);
        Assert.All(intact, p => Assert.NotNull(p.Model));

        // Then the same frame on the stripped field: same count, and the bare record draws nothing
        // instead of borrowing whatever a by-id lookup happens to return.
        scene.Render(camera, field);
        var drawn = scene.DrawnPickupPlacements.ToList();
        Assert.Equal(field.Items.Count(i => !i.Collected), drawn.Count);
        Assert.Null(drawn[0].Model);
        Assert.All(drawn.Skip(1), p => Assert.NotNull(p.Model));
        for (int i = 1; i < drawn.Count; i++)
        {
            Assert.Equal(intact[i].X, drawn[i].X);
            Assert.Equal(intact[i].Z, drawn[i].Z);
            Assert.Equal(intact[i].Model!.Id, drawn[i].Model!.Id);
        }
    }

    [Fact]
    public void AModelIdNoMissionModelCarriesResolvesToNoModel()
    {
        string? path = ConftPath();
        if (path == null) return;
        var file = new MisFile(path);
        var map = LevelMap.FromMisFile(path);
        var registry = WeaponCatalog.LoadRegistry(file);

        // 0 is a resource name a mission may use, so it must not stand in for "none": CONFT titles no
        // model 0, and a record left at 0 must not borrow the first model of the list.
        Assert.Empty(file.Models.Where(m => m.Id == 0));
        var field = WeaponPickups.FromMission(file.Bytes, registry, map);
        var bare = field.Items[0];
        field.Replace(bare, new WeaponPickup
        {
            X = bare.X, Y = bare.Y, Z = bare.Z,
            HalfX = bare.HalfX, HalfY = bare.HalfY, HalfZ = bare.HalfZ,
            Payload = bare.Payload, Amount = bare.Amount, Period = bare.Period,
            SectionId = bare.SectionId, KindType = bare.KindType, Kind = bare.Kind,
            Value = bare.Value, Height = bare.Height, Speed = bare.Speed,
            Angle = bare.Angle, Rate = bare.Rate,
            ModelId = 0, RecordOffset = bare.RecordOffset,
        });

        var scene = new LevelScene(path);
        scene.EnableMechMovement();
        scene.EnableFiring();
        scene.Render(new LevelCamera(270.0, 15.0, 0x0C00), field);
        var drawn = scene.DrawnPickupPlacements.ToList();
        Assert.Equal(field.Items.Count(i => !i.Collected), drawn.Count);
        Assert.Null(drawn[0].Model);                      // no model titled 0: nothing to borrow
        Assert.All(drawn.Skip(1), p => Assert.NotNull(p.Model));
    }
}
