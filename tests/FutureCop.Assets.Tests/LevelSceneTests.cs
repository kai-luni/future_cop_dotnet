using FutureCop.Assets;
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
}
