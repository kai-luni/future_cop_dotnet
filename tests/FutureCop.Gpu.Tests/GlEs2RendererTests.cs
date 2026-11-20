using Xunit;

namespace FutureCop.Gpu.Tests;

public class GlEs2RendererTests
{
    private static readonly TextureKey KeyA = new(0x0000, 0x0000);
    private static readonly TextureKey KeyB = new(0x0040, 0x0010);

    /// <summary>Six backdrop vertices at seven floats each, always in front of the scene.</summary>
    private const int BackdropFloats = 6 * 7;

    private static (GlEs2Renderer Renderer, FakeGpuApi Gl) Create()
    {
        var gl = new FakeGpuApi();
        return (new GlEs2Renderer(gl), gl);
    }

    /// <summary>Triangle with distinct position/uv/colour values so the vertex layout is checkable.</summary>
    private static Ps1Primitive Tri(TextureKey key, bool semi = false, float dx = 0, int sortKey = 0)
    {
        var a = new Ps1Vertex(dx, 0, 10, 20, 128, 64, 32);
        var b = new Ps1Vertex(dx + 4, 0, 18, 20, 128, 64, 32);
        var c = new Ps1Vertex(dx, 4, 10, 28, 128, 64, 32);
        return new Ps1Primitive(key, semi, in a, in b, in c, sortKey);
    }

    private static void Upload(GlEs2Renderer renderer, TextureKey key, byte value = 255)
    {
        byte[] pixels = new byte[VramTextureCache.BytesPerTexture];
        Array.Fill(pixels, value);
        renderer.UploadTexture(key, pixels);
    }

    private static (float X, float Y) Position(float[] vertices, int vertex) =>
        (vertices[vertex * 7], vertices[vertex * 7 + 1]);

    // ------------------------------------------------------------------ backdrop

    [Fact]
    public void BackdropQuadCoversTheWholeScreenAndIsDrawnFirst()
    {
        // GLView clears its own framebuffer, so the renderer has to fill the frame with geometry
        // instead of relying on glClearColor (whose float parameters arrive as 0.0 on the device).
        var (renderer, gl) = Create();
        using (renderer)
        {
            renderer.Render(new[] { Tri(KeyA) });

            DrawRecord backdrop = gl.Draws[0];
            Assert.Equal((float)Ps1AlphaMode.Backdrop, backdrop.AlphaMode);
            Assert.Equal(GlEnums.Triangles, backdrop.Mode);
            Assert.Equal(0, backdrop.First);
            Assert.Equal(6, backdrop.Count);
            Assert.False(backdrop.BlendEnabled);
            Assert.Equal(1f, backdrop.ColorScale);

            float[] v = gl.Vertices;
            Assert.Equal((0f, 0f), Position(v, 0));
            Assert.Equal((GlEs2Renderer.ScreenWidth, 0f), Position(v, 1));
            Assert.Equal((0f, GlEs2Renderer.ScreenHeight), Position(v, 2));
            Assert.Equal((GlEs2Renderer.ScreenWidth, 0f), Position(v, 3));
            Assert.Equal((GlEs2Renderer.ScreenWidth, GlEs2Renderer.ScreenHeight), Position(v, 4));
            Assert.Equal((0f, GlEs2Renderer.ScreenHeight), Position(v, 5));

            // Geometry, not glClear: the interface's Clear takes only a bit mask, and it is not used.
            Assert.Equal(0, gl.CountCalls("Clear("));
        }
    }

    [Fact]
    public void BackdropColorReachesTheShaderAsAFloat4Uniform()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            renderer.SetBackdropColor(0.10f, 0.25f, 0.45f);
            Assert.Equal((0.10f, 0.25f, 0.45f), renderer.BackdropColor);

            renderer.Render(ReadOnlySpan<Ps1Primitive>.Empty);

            float[] uniform = gl.Uniform("uBackdrop");
            Assert.Equal(4, uniform.Length);
            Assert.Equal(0.10f, uniform[0], 5);
            Assert.Equal(0.25f, uniform[1], 5);
            Assert.Equal(0.45f, uniform[2], 5);
            Assert.Equal(1f, uniform[3]);
            Assert.Equal(uniform, Assert.Single(gl.BackdropDraws).Backdrop);
            Assert.Contains(gl.Calls, c => c.StartsWith("Uniform4fv(", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void BackdropColorIsClampedAndDefaultsToOpaqueBlack()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            Assert.Equal((0f, 0f, 0f), renderer.BackdropColor);

            renderer.SetBackdropColor(2f, -1f, 0.5f);
            Assert.Equal((1f, 0f, 0.5f), renderer.BackdropColor);

            renderer.Render(ReadOnlySpan<Ps1Primitive>.Empty);
            Assert.Equal(new[] { 1f, 0f, 0.5f, 1f }, gl.Uniform("uBackdrop"));
        }
    }

    [Fact]
    public void BackdropRunDoesNotMergeWithTheFirstSceneTriangle()
    {
        // Same texture, same blend state: only the alpha mode differs, and that must be enough to
        // keep the backdrop a separate draw (otherwise the fill would overwrite the scene).
        var (renderer, gl) = Create();
        using (renderer)
        {
            Upload(renderer, KeyA);
            renderer.Render(new[] { Tri(KeyA) });

            Assert.Equal(2, gl.Draws.Count);
            Assert.Equal(6, gl.Draws[0].Count);
            Assert.Equal(6, gl.Draws[1].First);
            Assert.Equal(3, gl.Draws[1].Count);
            Assert.Equal((float)Ps1AlphaMode.Single, gl.Draws[1].AlphaMode);
            Assert.Equal(2, renderer.LastDrawCalls);
            Assert.Equal(1, renderer.LastTriangleCount);
        }
    }

    // ---------------------------------------------------------------- batching

    [Fact]
    public void OpaquePrimitivesWithSameState_ProduceOneDrawCall()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            Ps1Primitive[] prims = { Tri(KeyA), Tri(KeyA, dx: 10), Tri(KeyA, dx: 20) };
            renderer.Render(prims);

            DrawRecord draw = Assert.Single(gl.SceneDraws);
            Assert.Equal(GlEnums.Triangles, draw.Mode);
            Assert.Equal(6, draw.First);
            Assert.Equal(9, draw.Count);
            Assert.False(draw.BlendEnabled);
            Assert.Equal((float)Ps1AlphaMode.Single, draw.AlphaMode);
            Assert.Equal(1f, draw.ColorScale);
            Assert.Equal(2, renderer.LastDrawCalls);
            Assert.Equal(3, renderer.LastTriangleCount);
        }
    }

    [Fact]
    public void TextureChange_SplitsTheBatch()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            Upload(renderer, KeyA);
            Upload(renderer, KeyB);

            Ps1Primitive[] prims = { Tri(KeyA), Tri(KeyA, dx: 10), Tri(KeyB, dx: 20), Tri(KeyA, dx: 30) };
            renderer.Render(prims);

            var draws = gl.SceneDraws;
            Assert.Equal(3, draws.Count);
            Assert.Equal(6, draws[0].Count);
            Assert.Equal(3, draws[1].Count);
            Assert.Equal(3, draws[2].Count);
            Assert.Equal(draws[0].Texture, draws[2].Texture);
            Assert.NotEqual(draws[0].Texture, draws[1].Texture);
            Assert.Equal(4, renderer.LastDrawCalls); // backdrop + three batches
        }
    }

    [Fact]
    public void SemiTransparentPrimitive_EmitsSemiPassThenOpaquePass()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            TextureKey key = new((ushort)(3 << 5), 0); // ABR3
            renderer.Render(new[] { Tri(key, semi: true) });

            var draws = gl.SceneDraws;
            Assert.Equal(2, draws.Count);

            DrawRecord semi = draws[0];
            Assert.Equal(6, semi.First);
            Assert.Equal(3, semi.Count);
            Assert.True(semi.BlendEnabled);
            Assert.Equal(GlEnums.FuncAdd, semi.BlendEquation);
            Assert.Equal(GlEnums.One, semi.BlendSource);
            Assert.Equal(GlEnums.One, semi.BlendDestination);
            Assert.Equal((float)Ps1AlphaMode.Semi, semi.AlphaMode);
            Assert.Equal(0.25f, semi.ColorScale);

            DrawRecord opaque = draws[1];
            Assert.Equal(9, opaque.First);
            Assert.Equal(3, opaque.Count);
            Assert.False(opaque.BlendEnabled);
            Assert.Equal((float)Ps1AlphaMode.Opaque, opaque.AlphaMode);
            Assert.Equal(1f, opaque.ColorScale);

            Assert.Equal(1, renderer.LastTriangleCount);
        }
    }

    [Theory]
    [InlineData(0, GlEnums.FuncAdd, GlEnums.SrcAlpha, GlEnums.OneMinusSrcAlpha, 1f)]
    [InlineData(1, GlEnums.FuncAdd, GlEnums.One, GlEnums.One, 1f)]
    [InlineData(2, GlEnums.FuncReverseSubtract, GlEnums.One, GlEnums.One, 1f)]
    [InlineData(3, GlEnums.FuncAdd, GlEnums.One, GlEnums.One, 0.25f)]
    public void AbrMapsToBlendEquationAndFactors(int abr, int equation, int source, int destination, float scale)
    {
        var expected = (equation, source, destination);
        Assert.Equal(expected, GlEs2Renderer.BlendForAbr(abr));
        Assert.Equal(scale, GlEs2Renderer.ColorScaleForAbr(abr));

        var (renderer, gl) = Create();
        using (renderer)
        {
            TextureKey key = new((ushort)(abr << 5), 0);
            Assert.Equal(abr, key.Abr);
            renderer.Render(new[] { Tri(key, semi: true) });

            DrawRecord semi = gl.SceneDraws[0];
            Assert.True(semi.BlendEnabled);
            Assert.Equal(equation, semi.BlendEquation);
            Assert.Equal(source, semi.BlendSource);
            Assert.Equal(destination, semi.BlendDestination);
            Assert.Equal(scale, semi.ColorScale);
        }
    }

    [Fact]
    public void SemiTransparentNeighbour_DoesNotReorderedTheStream()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            Upload(renderer, KeyA);
            // semi(ABR0), then an opaque primitive on the same texture: the semi primitive's two
            // passes must stay in front, so the opaque pass and the following single pass cannot merge.
            Ps1Primitive[] prims = { Tri(KeyA, semi: true), Tri(KeyA, dx: 10) };
            renderer.Render(prims);

            var draws = gl.SceneDraws;
            Assert.Equal(3, draws.Count);
            Assert.Equal((float)Ps1AlphaMode.Semi, draws[0].AlphaMode);
            Assert.Equal((float)Ps1AlphaMode.Opaque, draws[1].AlphaMode);
            Assert.Equal((float)Ps1AlphaMode.Single, draws[2].AlphaMode);
            Assert.Equal(6, draws[0].First);
            Assert.Equal(9, draws[1].First);
            Assert.Equal(12, draws[2].First);
        }
    }

    [Fact]
    public void StreamOrderWinsOverSortKey()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            Ps1Primitive[] prims = { Tri(KeyA, dx: 0, sortKey: 300), Tri(KeyA, dx: 10, sortKey: 200), Tri(KeyA, dx: 20, sortKey: 100) };
            renderer.Render(prims);

            float[] v = gl.Vertices;
            Assert.Equal(BackdropFloats + 63, v.Length); // backdrop, then three triangles of seven floats per vertex
            Assert.Equal(0f, v[BackdropFloats + 0 * 7]);
            Assert.Equal(10f, v[BackdropFloats + 3 * 7]);
            Assert.Equal(20f, v[BackdropFloats + 6 * 7]);
        }
    }

    [Fact]
    public void VertexLayoutIsPositionUvColour()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            renderer.Render(new[] { Tri(KeyA) });

            float[] v = gl.Vertices;
            Assert.Equal(BackdropFloats + 21, v.Length);
            Assert.Equal(new[] { 0f, 0f, 10f, 20f, 128f, 64f, 32f }, v[BackdropFloats..(BackdropFloats + 7)]);
            Assert.Equal(new[] { 4f, 0f, 18f, 20f, 128f, 64f, 32f }, v[(BackdropFloats + 7)..(BackdropFloats + 14)]);
            Assert.Equal(new[] { 0f, 4f, 10f, 28f, 128f, 64f, 32f }, v[(BackdropFloats + 14)..(BackdropFloats + 21)]);
        }
    }

    [Fact]
    public void EmptyFrame_StillPaintsOnlyTheBackdrop()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            renderer.Render(ReadOnlySpan<Ps1Primitive>.Empty);

            DrawRecord draw = Assert.Single(gl.Draws);
            Assert.Equal((float)Ps1AlphaMode.Backdrop, draw.AlphaMode);
            Assert.Equal(0, draw.First);
            Assert.Equal(6, draw.Count);
            Assert.Equal(1, renderer.LastDrawCalls);
            Assert.Equal(0, renderer.LastTriangleCount);
        }
    }

    [Fact]
    public void UnknownTexture_FallsBackToTheWhiteTexture()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            renderer.Render(new[] { Tri(KeyA) });

            Assert.Equal(gl.FirstTextureId, Assert.Single(gl.SceneDraws).Texture);
            Assert.False(renderer.HasTexture(KeyA));
        }
    }

    [Fact]
    public void UploadedTextureIsUsedAndReused()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            Upload(renderer, KeyA);
            Upload(renderer, KeyB);
            Assert.True(renderer.HasTexture(KeyA));
            Assert.Equal(2, renderer.TextureCount); // two uploads, the white fallback is not counted

            renderer.Render(new[] { Tri(KeyB) });
            int texture = Assert.Single(gl.SceneDraws).Texture;
            Assert.NotEqual(gl.FirstTextureId, texture);

            // Re-uploading keeps the same GL texture object (used after a VRAM invalidation).
            Upload(renderer, KeyB, 0);
            renderer.Render(new[] { Tri(KeyB) });
            Assert.Equal(texture, gl.SceneDraws[1].Texture);
            Assert.Equal(2, renderer.TextureCount);
        }
    }

    [Fact]
    public void VertexBufferGrowsWithoutSplittingTheBatch()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            Ps1Primitive[] prims = new Ps1Primitive[600];
            for (int i = 0; i < prims.Length; i++) prims[i] = Tri(KeyA, dx: i);

            renderer.Render(prims);

            DrawRecord draw = Assert.Single(gl.SceneDraws);
            Assert.Equal(6, draw.First);
            Assert.Equal(1800, draw.Count);
            Assert.Equal((1800 + 6) * 7, gl.VertexFloatCount);
            Assert.True(renderer.VertexCapacity >= 1806);
        }
    }

    [Fact]
    public void FailedVertexBufferGrowthLeavesCapacityUnchangedAndSkipsUploadAndDraw()
    {
        var gl = new FakeGpuApi();
        using var renderer = new GlEs2Renderer(gl);
        int initialCapacity = renderer.VertexCapacity;
        var primitives = Enumerable.Repeat(Tri(KeyA), initialCapacity / 3).ToArray();
        gl.NextBufferAllocationError = 0x0505; // GL_OUT_OF_MEMORY

        var exception = Assert.Throws<InvalidOperationException>(() => renderer.Render(primitives));
        Assert.Contains("0x0505", exception.Message);
        Assert.Contains("glBufferData", exception.Message);
        Assert.Equal(initialCapacity, renderer.VertexCapacity);
        Assert.Equal(0, gl.BufferSubDataCalls);
        Assert.Empty(gl.Draws);

        renderer.Render(primitives);
        Assert.True(renderer.VertexCapacity > initialCapacity);
        Assert.Equal(1, gl.BufferSubDataCalls);
        Assert.Single(gl.SceneDraws);
    }

    [Fact]
    public void SetViewportForwardsToGl()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            renderer.SetViewport(640, 480);
            Assert.Equal((0, 0, 640, 480), gl.Viewports[^1]);
        }
    }

    [Fact]
    public void DisposeReleasesTexturesBufferAndProgram()
    {
        var (renderer, gl) = Create();
        Upload(renderer, KeyA);
        int textures = gl.GeneratedTextures;

        renderer.Dispose();
        renderer.Dispose();

        Assert.Equal(textures, gl.DeletedTextures.Count);
        Assert.Single(gl.DeletedBuffers);
        Assert.Equal(1, gl.CountCalls("DeleteProgram"));
        Assert.Throws<ObjectDisposedException>(() => renderer.Render(ReadOnlySpan<Ps1Primitive>.Empty));
    }

    [Fact]
    public void ShaderProgramIsLinkedWithTheFixedAttributeLocations()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            Assert.Equal(1, gl.ProgramLinks);
            Assert.Contains("BindAttribLocation(0,aPosition)", gl.Calls);
            Assert.Contains("BindAttribLocation(1,aTexCoord)", gl.Calls);
            Assert.Contains("BindAttribLocation(2,aColor)", gl.Calls);
            Assert.Contains(gl.Calls, c => c.StartsWith("VertexAttribPointer(0,2,", StringComparison.Ordinal));
            Assert.Contains(gl.Calls, c => c.StartsWith("VertexAttribPointer(1,2,", StringComparison.Ordinal));
            Assert.Contains(gl.Calls, c => c.StartsWith("VertexAttribPointer(2,3,", StringComparison.Ordinal));
            Assert.Equal(2, gl.ShaderSources.Count);
        }
    }

    [Fact]
    public void ShaderDeclaresTheBackdropUniform()
    {
        // A uniform the compiler drops would make the whole frame black with no error: the
        // constructor's RequireUniform guard only works if the name matches the GLSL source.
        var (renderer, gl) = Create();
        using (renderer)
        {
            string fragment = gl.ShaderSources.Values.Single(s => s.Contains("gl_FragColor", StringComparison.Ordinal));
            Assert.Contains("uniform vec4 uBackdrop;", fragment, StringComparison.Ordinal);
            Assert.Contains(gl.UniformNames.Values, name => name == "uBackdrop");
        }
    }

    [Fact]
    public void TexturePageIsSharedAcrossBlendModes()
    {
        // tpage bits 5..6 are ABR, not part of the texel data: four ABR variants of one page must
        // not cost four 256x256 textures (256 KiB each on the TV).
        var (renderer, gl) = Create();
        using (renderer)
        {
            var opaque = new TextureKey(0x0100, 0x0000);
            Upload(renderer, opaque);
            Upload(renderer, new TextureKey(0x0120, 0x0000));
            Upload(renderer, new TextureKey(0x0140, 0x0000));
            Upload(renderer, new TextureKey(0x0160, 0x0000));

            Assert.Equal(1, renderer.TextureCount);
            // One white fallback from the constructor plus exactly one page texture.
            Assert.Equal(2, gl.Calls.Count(c => c.StartsWith("GenTexture(", StringComparison.Ordinal)));
            // The depth code (bits 7..8) is part of the identity, not part of the ABR mask.
            Assert.False(renderer.HasTexture(new TextureKey(0x0180, 0x0000)));

            // The blend state still follows the primitive's own tpage.
            renderer.Render(new[] { Tri(new TextureKey(0x0140, 0x0000), semi: true) });
            Assert.Equal(GlEnums.FuncReverseSubtract, gl.CurrentBlendEquation);
        }
    }

    [Fact]
    public void SetupDisablesDitherAndScissor()
    {
        // Both are on in some inherited contexts: dithering destroys the exact 8-bit modulation
        // result, a scissor rectangle clips the frame.
        var (renderer, gl) = Create();
        using (renderer)
        {
            Assert.Contains($"Disable({GlEnums.Dither})", gl.Calls);
            Assert.Contains($"Disable({GlEnums.ScissorTest})", gl.Calls);
            Assert.Contains($"Disable({GlEnums.DepthTest})", gl.Calls);
            Assert.Contains($"Disable({GlEnums.CullFace})", gl.Calls);
        }
    }

    [Fact]
    public void MissingUniform_FailsLoudlyInsteadOfDrawingBlack()
    {
        var gl = new FakeGpuApi { UniformLocationOverride = name => name == "uColorScale" ? -1 : 7 };
        var exception = Assert.Throws<InvalidOperationException>(() => new GlEs2Renderer(gl));
        Assert.Contains("uColorScale", exception.Message);
    }

    [Fact]
    public void MissingBackdropUniform_FailsLoudlyInsteadOfDrawingBlack()
    {
        var gl = new FakeGpuApi { UniformLocationOverride = name => name == "uBackdrop" ? -1 : 7 };
        var exception = Assert.Throws<InvalidOperationException>(() => new GlEs2Renderer(gl));
        Assert.Contains("uBackdrop", exception.Message);
    }

    [Fact]
    public void FailedTextureUpload_DeletesUnpublishedTextureAndCanRetry()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            gl.NextTextureUploadError = 0x0502; // GL_INVALID_OPERATION
            var exception = Assert.Throws<InvalidOperationException>(() => Upload(renderer, KeyA));
            Assert.Contains("0x0502", exception.Message);
            Assert.False(renderer.HasTexture(KeyA));
            Assert.Equal(0, renderer.TextureCount);
            int failed = Assert.Single(gl.DeletedTextures);
            Assert.NotEqual(gl.FirstTextureId, failed);
            renderer.Render(new[] { Tri(KeyA) });
            Assert.Equal(gl.FirstTextureId, Assert.Single(gl.SceneDraws).Texture);

            Upload(renderer, KeyA);
            Assert.True(renderer.HasTexture(KeyA));
            Assert.Equal(1, renderer.TextureCount);
            Assert.Equal(3, gl.GeneratedTextures);
            Assert.NotEqual(failed, gl.BoundTexture);
            Assert.Equal(3, gl.CountCalls("TexImage2D("));
        }
    }

    [Fact]
    public void FailedTextureUpdate_InvalidatesThePageAndCanRetryThroughAnAlias()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            var alias = KeyA with { Tpage = 0x0060 };
            Upload(renderer, KeyA);
            int original = gl.BoundTexture;
            Upload(renderer, alias, 128);
            Assert.Equal(original, gl.BoundTexture);
            Assert.Equal(2, gl.GeneratedTextures);
            Assert.Equal(3, gl.CountCalls("TexImage2D("));

            gl.NextTextureUploadError = 0x0505; // GL_OUT_OF_MEMORY
            Assert.Throws<InvalidOperationException>(() => Upload(renderer, alias, 64));
            Assert.Equal(original, Assert.Single(gl.DeletedTextures));
            Assert.False(renderer.HasTexture(KeyA));
            Assert.False(renderer.HasTexture(alias));

            Upload(renderer, alias, 64);
            Assert.True(renderer.HasTexture(KeyA));
            Assert.True(renderer.HasTexture(alias));
            Assert.NotEqual(original, gl.BoundTexture);
            Assert.Equal(1, renderer.TextureCount);
        }
    }

    [Fact]
    public void CommandStreamOverloadKeepsStreamOrder()
    {
        var (renderer, gl) = Create();
        using (renderer)
        {
            var stream = new Ps1CommandStream();
            stream.Add(Tri(KeyA, dx: 0));
            stream.Add(Tri(KeyA, dx: 10));
            stream.Clear();
            stream.Add(Tri(KeyA, dx: 20));

            renderer.Render(stream);

            Assert.Equal(20f, gl.Vertices[BackdropFloats]);
            Assert.Equal(3, Assert.Single(gl.SceneDraws).Count);
        }
    }
}
