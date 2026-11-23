namespace FutureCop.Gpu;

/// <summary>
/// Identity of one decoded PS1 texture page: the packed tpage and CLUT words. Two primitives with
/// the same key share one 256x256 RGBA8 texture.
/// </summary>
public readonly record struct TextureKey(ushort Tpage, ushort Clut)
{
    /// <summary>
    /// Sentinel for untextured model polygons (<c>LevelScene.FlatFace</c>). No texture is sampled
    /// for it; the shader writes the vertex colour instead.
    /// </summary>
    public static TextureKey Flat { get; } = new(0xFFFF, 0xFFFF);

    /// <summary>True for the flat-polygon sentinel, which never resolves to a texture.</summary>
    public bool IsFlat => Tpage == 0xFFFF && Clut == 0xFFFF;
    /// <summary>tpage bits 5..6: 0 = (back+fg)/2, 1 = min(255, back+fg), 2 = max(0, back-fg), 3 = min(255, back+fg/4).</summary>
    public int Abr => (Tpage >> 5) & 3;

    /// <summary>Texture page base X in VRAM cells (tpage bits 0..3 * 64).</summary>
    public int PageX => (Tpage & 0x0F) * 64;

    /// <summary>Texture page base Y in VRAM cells (tpage bit 4 * 256).</summary>
    public int PageY => ((Tpage >> 4) & 1) * 256;

    /// <summary>Colour depth code: 0 = 4-bit CLUT, 1 = 8-bit CLUT, 2/3 = 15-bit direct.</summary>
    public int Depth => (Tpage >> 7) & 3;
}
