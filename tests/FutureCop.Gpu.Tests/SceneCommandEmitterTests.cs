using FutureCop.Assets;
using FutureCop.Gpu.SceneBridge;
using FutureCop.View;
using Xunit;

namespace FutureCop.Gpu.Tests;

public class SceneCommandEmitterTests
{
    [Fact]
    public void EmittingAGpuSceneLeavesTheSoftwareFramebufferUntouched()
    {
        string? disc = RetailGpuAssets.FindDisc();
        string? mission = disc is null ? null : Path.Combine(disc, "PA", "CONFT.MIS");
        if (mission is null) return;
        var emitter = new SceneCommandEmitter();
        var level = new LevelScene(mission) { Sink = emitter };
        Array.Fill(level.Rgb, (byte)0x5a);
        level.Render(new LevelCamera());
        Assert.True(emitter.TexturedCount > 0);
        Assert.True(Array.TrueForAll(level.Rgb, value => value == 0x5a));
    }

    private static void AddTexture(SceneCommandEmitter emitter, ushort tpage)
    {
        emitter.TexturedTriangle(
            0, 0, 0, 0, 128, 128, 128,
            1, 0, 1, 0, 128, 128, 128,
            0, 1, 0, 1, 128, 128, 128,
            tpage, 0, false);
    }

    [Fact]
    public void UploadsNewTexturesOnlyAndReuploadsAfterRendererRecreation()
    {
        var emitter = new SceneCommandEmitter();
        var vram = new Vram();
        var gl = new FakeGpuApi();
        using var first = new GlEs2Renderer(gl);
        AddTexture(emitter, 1);
        Assert.Equal(1, emitter.UploadNewTextures(first, vram));
        Assert.Equal(0, emitter.UploadNewTextures(first, vram));

        emitter.BeginFrame();
        AddTexture(emitter, 2);
        Assert.Equal(1, emitter.UploadNewTextures(first, vram));
        Assert.Equal(2, first.TextureCount);

        using var recreated = new GlEs2Renderer(new FakeGpuApi());
        Assert.Equal(2, emitter.UploadNewTextures(recreated, vram));
        Assert.Equal(0, emitter.UploadNewTextures(recreated, vram));
    }
}

internal static class RetailGpuAssets
{
    internal static bool RequireMission(string folder, string file)
    {
        string? disc = FindDisc();
        if (disc is null) return false;
        string path = Path.Combine(disc, folder, file);
        if (File.Exists(path)) return true;
        Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
        return false;
    }

    internal static string? FindDisc()
    {
        string? disc = Environment.GetEnvironmentVariable("FC_RETAIL_DISC");
        if (!string.IsNullOrEmpty(disc))
        {
            if (File.Exists(Path.Combine(disc, "CW", "FE.MIS"))) return disc;
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
            return null;
        }
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "build", "disc", "files");
            if (File.Exists(Path.Combine(candidate, "CW", "FE.MIS"))) return candidate;
        }
        Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
        return null;
    }
}
