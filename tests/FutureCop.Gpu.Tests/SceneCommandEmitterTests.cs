using FutureCop.Assets;
using FutureCop.Gpu.SceneBridge;
using Xunit;

namespace FutureCop.Gpu.Tests;

public class SceneCommandEmitterTests
{
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
