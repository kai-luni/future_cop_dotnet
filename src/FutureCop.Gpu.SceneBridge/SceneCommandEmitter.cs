using FutureCop.Assets;
using FutureCop.Gpu;
using FutureCop.View;

namespace FutureCop.Gpu.SceneBridge;

/// <summary>
/// Turns the triangles of a <see cref="LevelScene"/> into the platform-neutral PS1 primitive stream
/// the shared GPU renderer draws. Nothing is rasterized here: the scene keeps owning geometry,
/// camera, culling and painter's order, this class only translates coordinates into
/// <see cref="Ps1Primitive"/> records.
/// </summary>
public sealed class SceneCommandEmitter : IPrimitiveSink
{
    private readonly Ps1CommandStream _stream = new(1024);
    private readonly Dictionary<TextureKey, bool> _keys = new();
    private readonly byte[] _textureBuffer = new byte[VramTextureCache.BytesPerTexture];

    /// <summary>Primitives in the order the software rasterizer would have drawn them.</summary>
    public Ps1CommandStream Stream => _stream;

    /// <summary>Number of textured triangles translated for the current frame.</summary>
    public int TexturedCount { get; private set; }

    /// <summary>Number of untextured model polygons (flat colour) for the current frame.</summary>
    public int FlatCount { get; private set; }

    /// <summary>Number of textured triangles that carry the semi-transparency packet bit.</summary>
    public int SemiCount { get; private set; }

    /// <summary>Every distinct (tpage, clut) pair seen since the last <see cref="ResetTextures"/>.</summary>
    public IReadOnlyCollection<TextureKey> TextureKeys => _keys.Keys;

    /// <summary>Clears the stream and the per-frame counters; texture keys stay because they are cache state.</summary>
    public void BeginFrame()
    {
        _stream.Clear();
        TexturedCount = 0;
        FlatCount = 0;
        SemiCount = 0;
    }

    /// <summary>Forgets the remembered texture pages, e.g. after the VRAM was reloaded.</summary>
    public void ResetTextures() => _keys.Clear();

    public void TexturedTriangle(
        int x0, int y0, int u0, int v0, byte r0, byte g0, byte b0,
        int x1, int y1, int u1, int v1, byte r1, byte g1, byte b1,
        int x2, int y2, int u2, int v2, byte r2, byte g2, byte b2,
        ushort tpage, ushort clut, bool semi)
    {
        var key = new TextureKey(tpage, clut);
        _keys[key] = true;
        _stream.Add(key, semi,
            new Ps1Vertex(x0, y0, u0, v0, r0, g0, b0),
            new Ps1Vertex(x1, y1, u1, v1, r1, g1, b1),
            new Ps1Vertex(x2, y2, u2, v2, r2, g2, b2));
        TexturedCount++;
        if (semi) SemiCount++;
    }

    public void FlatTriangle(int x0, int y0, int x1, int y1, int x2, int y2,
        byte red, byte green, byte blue)
    {
        _stream.Add(TextureKey.Flat, false,
            new Ps1Vertex(x0, y0, 0, 0, red, green, blue),
            new Ps1Vertex(x1, y1, 0, 0, red, green, blue),
            new Ps1Vertex(x2, y2, 0, 0, red, green, blue));
        FlatCount++;
    }

    /// <summary>
    /// Uploads every texture page used by the current frame that the renderer does not know yet.
    /// Decoding happens once per (tpage, clut) pair, never per frame.
    /// </summary>
    /// <returns>Number of pages uploaded by this call.</returns>
    public int UploadNewTextures(GlEs2Renderer renderer, Vram vram)
    {
        if (renderer is null) throw new ArgumentNullException(nameof(renderer));
        if (vram is null) throw new ArgumentNullException(nameof(vram));

        int uploaded = 0;
        foreach (TextureKey key in _keys.Keys)
        {
            if (renderer.HasTexture(key)) continue;
            VramTextureCache.Decode(vram, key, _textureBuffer);
            renderer.UploadTexture(key, _textureBuffer);
            uploaded++;
        }
        return uploaded;
    }
}