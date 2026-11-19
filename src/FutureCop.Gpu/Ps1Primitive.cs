namespace FutureCop.Gpu;

/// <summary>
/// One textured PS1 triangle, already expanded from the retail packet (codes 0x24/0x2C/0x34/0x3C)
/// by the caller. The renderer never looks at packets, only at this neutral description.
/// </summary>
public readonly struct Ps1Primitive
{
    /// <summary>Texture page / CLUT pair, also the source of the ABR blend mode.</summary>
    public readonly TextureKey Key;

    /// <summary>Packet bit 1 (code | 2): draw in a semi-transparent pass.</summary>
    public readonly bool SemiTransparent;

    /// <summary>
    /// Caller's painter key (e.g. an SZ3 average). <see cref="Ps1CommandStream"/> never sorts on
    /// it; it exists so the caller can order the stream itself and still keep the key for tooling.
    /// </summary>
    public readonly int SortKey;

    public readonly Ps1Vertex V0;
    public readonly Ps1Vertex V1;
    public readonly Ps1Vertex V2;

    public Ps1Primitive(TextureKey key, bool semiTransparent, in Ps1Vertex v0, in Ps1Vertex v1,
        in Ps1Vertex v2, int sortKey = 0)
    {
        Key = key;
        SemiTransparent = semiTransparent;
        V0 = v0;
        V1 = v1;
        V2 = v2;
        SortKey = sortKey;
    }

    /// <summary>Blend mode from tpage bits 5..6.</summary>
    public int Abr => Key.Abr;
}
