namespace FutureCop.Assets;

/// <summary>One 8-byte source vertex record ({s16 x, y, z, pad}).</summary>
public readonly record struct Vertex(short X, short Y, short Z);

/// <summary>
/// One 12-byte on-disc polygon record from a 3DQL list. Flag bytes are kept raw because
/// the retail loader (decomp func_800131A0) rewrites them at load time; decoded helpers
/// expose the on-disc meaning.
/// </summary>
public readonly record struct Polygon(
    byte Flags0, byte Flags1, ushort TextureOffset,
    byte V0, byte V1, byte V2, byte V3,
    byte N0, byte N1, byte N2, byte N3)
{
    /// <summary>On-disc bits 0-2: vertex count.</summary>
    public int VertexCount => Flags0 & 7;
    /// <summary>On-disc bit 3: semi-transparent.</summary>
    public bool SemiTransparent => (Flags0 & 8) != 0;
    /// <summary>On-disc bits 4-6: shading mode.</summary>
    public int ShadingMode => (Flags0 >> 4) & 7;
    /// <summary>Byte 1 low nibble: 3 tri, 4 quad, 5/6 billboards, 7 tapered line, 8 disc.</summary>
    public int PrimitiveType => Flags1 & 0xF;
    /// <summary>Byte 1 bits 4-6: part group used by the draw masks (decomp func_80016A64).</summary>
    public int PartGroup => (Flags1 >> 4) & 7;
    /// <summary>Byte 1 bit 7: extra depth pass.</summary>
    public bool ExtraPass => (Flags1 & 0x80) != 0;
}

/// <summary>A parsed Cobj model: header words plus per-frame geometry lists.</summary>
public sealed class Model
{
    public int Id { get; init; }
    public int RecordOffset { get; init; }
    public uint[] Header { get; set; } = Array.Empty<uint>();

    /// <summary>
    /// The model flag word: the first 4DGI data word (decomp func_800131A0 keeps the chunk
    /// start as the header, so its +0x0C flags field is data word 0). Bit 25 selects the
    /// scripted/bone draw path, bit 24 is cleared or set by the texture-animation loader.
    /// </summary>
    public uint Flags => Header.Length > 0 ? Header[0] : 0u;

    /// <summary>True when the model uses the scripted (bone) draw path of func_80013F08.</summary>
    public bool IsScripted => ((Flags >> 25) & 1) != 0;

    /// <summary>3DHY entries: one bone transform plus its vertex/normal ranges (0x14 bytes each).</summary>
    public List<ScriptEntry> Hierarchy { get; } = new();

    /// <summary>3DMI sample table (s16), indexed as table[frame + field] by func_80013CB8.</summary>
    public short[] MeshTable { get; set; } = Array.Empty<short>();
    public List<Polygon> Polygons { get; } = new();
    /// <summary>Vertex lists keyed by chunk index (frame + 1).</summary>
    public Dictionary<int, List<Vertex>> Vertices { get; } = new();
    public Dictionary<int, List<Vertex>> Normals { get; } = new();
    /// <summary>Width lists (s16) keyed by chunk index; used by billboards/tapered lines.</summary>
    public Dictionary<int, List<short>> Widths { get; } = new();
    public int TextureRecordCount { get; set; }
    /// <summary>Raw 3DTL body bytes (on-disc form).</summary>
    public byte[] TextureBody { get; set; } = Array.Empty<byte>();
    /// <summary>Decoded texture records using the default Cbmp remap (ids 1..10).</summary>
    public List<TextureRecord> Textures { get; } = new();
    public int HierarchyEntries { get; set; }
    public List<string> ChunkTags { get; } = new();
}