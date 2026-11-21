using System.Buffers.Binary;

namespace FutureCop.Assets;

/// <summary>
/// GRDB map header of a mission (the Cptc resource), parsed like func_80063DC8: the last GRDB chunk
/// of the payload becomes gTileMapTable (chunk + 0x0C), its origin vector +0x0C is rotated
/// (x, y, z) -> (y, 0, x) and negated in place, and the world clip rect D_800A2950[0..5] plus the
/// cell limits D_800A2970[4..5] are derived from the grid size and the border word +0x18.
/// </summary>
public sealed class MapHeader
{
    private readonly byte[] _payload;

    /// <summary>Offset of gTileMapTable inside the Cptc payload (GRDB chunk + 0x0C).</summary>
    public int GridOffset { get; }

    /// <summary>The seven header words at the grid pointer after the loader's rewrite (+0x00..+0x18).</summary>
    public int[] Words { get; }

    /// <summary>D_800A2950[0..5]: z min, z max, x min, x max, z and x inverse spans (0x360000 scaled).</summary>
    public int[] WorldClipRect { get; }

    /// <summary>D_800A2970[4] = (w - 1) &lt;&lt; 2.</summary>
    public int CellLimitX { get; }

    /// <summary>D_800A2970[5] = (h - 1) &lt;&lt; 2.</summary>
    public int CellLimitZ { get; }

    /// <summary>Size of the 16-byte-per-cell grid D_8009F820 that func_80063DC8 allocates.</summary>
    public int CellMapBytes { get; }

    /// <summary>Header word +0x04: number of map cells along world z (the grid row length).</summary>
    public int Width => Words[1];

    /// <summary>Header word +0x08: number of map cells along world x (the number of grid rows).</summary>
    public int Height => Words[2];

    /// <summary>Border word +0x18 (cells excluded from the clip rect on every side, minus one at the far edge).</summary>
    public int Border => Words[6];

    private MapHeader(byte[] payload, int gridOffset)
    {
        _payload = payload;
        GridOffset = gridOffset;
        int Read(int o) => BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(gridOffset + o));
        int w = Read(4), h = Read(8), border = Read(0x18);
        int oldX = Read(0x0C), oldY = Read(0x10);
        // func_80063DC8: unk0C.y = 0, unk0C.z = old x, unk0C.x = old y; local copy taken before negation.
        int localX = oldY, localZ = oldX;
        Words = new[] { Read(0), w, h, -localX, 0, -localZ, border };

        int zBottom = localZ;
        int left = zBottom + (border << 17);
        int right = zBottom + (w << 17) - ((border + 1) << 17);
        uint spanZ = Divide(0x80000000u, (uint)((right - left) >> 1));
        int bottom = localX + (border << 17);
        int top = zBottom + (h << 17) - ((border + 1) << 17); // retail uses the z origin here as well
        uint spanX = Divide(0x80000000u, (uint)((top - bottom) >> 1));
        WorldClipRect = new[] { left, right, bottom, top, FixMul(spanZ), FixMul(spanX) };
        CellLimitX = (w - 1) << 2;
        CellLimitZ = (h - 1) << 2;
        CellMapBytes = ((w - 1) * (h - 1)) << 4;
    }

    // divu without a zero check in the retail code; R3000 leaves LO = 0xFFFFFFFF for a zero divisor.
    private static uint Divide(uint a, uint b) => b == 0 ? 0xFFFFFFFFu : a / b;

    // FIXMUL in func_80063DC8: multu 0x360000 * x, then (hi << 16) | (lo >> 16).
    private static int FixMul(uint x) => unchecked((int)(uint)(((ulong)0x360000u * x) >> 16));

    /// <summary>Grid entry <paramref name="row"/> * w + <paramref name="column"/>: byte offset into gTileBlockTable in the low 16 bits.</summary>
    public uint Entry(int index) => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(GridOffset + 0x24 + index * 4));

    /// <summary>Section id of a grid cell (func_80066F78: (tab[idx] >> 2) &amp; 0x3FFF).</summary>
    public int SectionIdAt(int row, int column) => (int)((Entry(row * Width + column) >> 2) & 0x3FFF);

    /// <summary>Parses a Cptc payload (func_80063DC8). Returns null when it carries no GRDB chunk.</summary>
    public static MapHeader? Parse(byte[] payload)
    {
        uint magic = ChunkReader.TagValue("GRDB");
        int found = -1, p = 0;
        do
        {
            if (p + 8 > payload.Length) break;
            if (BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(p)) == magic) found = p + 0xC;
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(p + 4));
            if (len <= 0) break;
            p += len;
        } while ((uint)p < (uint)payload.Length);
        // The header words end at +0x18 (see the constructor); a GRDB tag too close to the end is
        // treated as "no header" instead of reading past the payload.
        return found < 0 || found + 0x1C > payload.Length ? null : new MapHeader(payload, found);
    }
}

/// <summary>
/// One map section ("Sect" chunk of a Ctil resource) after func_800639D4 relocated it: the three
/// table pointers it writes over the chunk header (+0x00 colour table, +0x04 descriptor table,
/// +0x08 UV table) are kept as payload offsets, and the tileset byte +0x36F is reset to -1 unless a
/// following SLFX chunk assigns a tile definition. Field offsets are relative to the Sect chunk.
/// </summary>
public sealed class LevelSection
{
    public int Id { get; }
    public byte[] Data { get; }
    /// <summary>Payload offset of the Sect chunk (all field offsets below add this).</summary>
    public int Base { get; }
    /// <summary>+0x00 after relocation: colour (CLUT entry) table, tbl[0] of func_800648E8.</summary>
    public int ColourOffset { get; }
    /// <summary>+0x04 after relocation: primitive descriptor half-words, tbl[1].</summary>
    public int DescriptorOffset { get; }
    /// <summary>+0x08 after relocation: UV half-words, tbl[2].</summary>
    public int UvOffset { get; }
    /// <summary>+0x36F as left by the loader: -1, or the tile definition index of an SLFX chunk.</summary>
    public sbyte TileSet { get; internal set; }

    internal LevelSection(int id, byte[] data, int sectBase)
    {
        Id = id;
        Data = data;
        Base = sectBase;
        // func_800639D4: counts = word +8 (hi = UV entries, lo = colour entries), n = s16 +0x3CA.
        int counts = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(sectBase + 8));
        int low = counts & 0xFFFF;
        counts >>= 16;
        int n = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(sectBase + 0x3CA));
        int tableBase = sectBase + 0x5D0 + n * 4;
        UvOffset = tableBase;
        ColourOffset = tableBase + counts * 2;
        DescriptorOffset = ColourOffset + low * 2;
        TileSet = -1;
    }

    /// <summary>Signed height byte of grid point <paramref name="index"/> (17 x 17 points x 3 layers, +0x0C).</summary>
    public sbyte Height(int index) => (sbyte)Data[Base + 0x0C + index];
    public byte Byte(int offset) => Data[Base + offset];
    public short S16(int offset) => BinaryPrimitives.ReadInt16LittleEndian(Data.AsSpan(Base + offset));
    public ushort U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(Base + offset));
    public uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(Base + offset));
    /// <summary>u16 at an absolute payload offset (for the relocated tables).</summary>
    public ushort TableU16(int payloadOffset) => BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(payloadOffset));
    /// <summary>Tile word list +0x3CC: half-word per tile, low nibble = word count, rest = byte offset into +0x5D0.</summary>
    public ushort TileEntry(int tile) => U16(0x3CC + tile * 2);
}

/// <summary>D_800A6F38: the deduplicated SLFX tile definition list func_800639D4 fills.</summary>
public sealed class TileDefinitionTable
{
    private readonly uint[] _values = new uint[100];
    private readonly Dictionary<int, (int Count, int Flag)> _history = new();

    public sbyte Count { get; private set; }
    /// <summary>D_800A2931: set when an SLFX value has the top nibble 1.</summary>
    public byte SlfxFlag { get; private set; }
    public uint Value(int index) => _values[index];

    internal int Add(uint value)
    {
        if ((value >> 28) == 1)
        {
            SlfxFlag = 1;
            value &= ~0xFCu;
        }
        int i;
        for (i = Count - 1; i >= 0; i--)
            if (_values[i] == value) break;
        if (i < 0)
        {
            byte raw = (byte)Count;
            i = (sbyte)raw;
            Count = unchecked((sbyte)(raw + 1));
            if ((value >> 28) == 1) value &= ~0xFCu;
            _values[i] = value;
        }
        return i;
    }

    internal void Record(int sectionId) => _history[sectionId] = (Count, SlfxFlag);
    public int CountAfterSection(int id) => _history.TryGetValue(id, out var h) ? h.Count : throw new KeyNotFoundException($"section {id}");
    public int SlfxFlagAfterSection(int id) => _history.TryGetValue(id, out var h) ? h.Flag : throw new KeyNotFoundException($"section {id}");
}

/// <summary>
/// The terrain of one mission: the Cptc GRDB header and the Ctil sections (gTileBlockTable
/// D_8009F808, indexed by the Ctil resource id). Loading follows func_80024EC8 -> func_80063DC8
/// for Cptc and func_80063C20 -> func_800639D4 for Ctil, in file order.
/// </summary>
public sealed class LevelMap
{
    private readonly Dictionary<int, LevelSection> _sections = new();

    public MapHeader Header { get; }
    public TileDefinitionTable TileDefinitions { get; } = new();
    public IReadOnlyCollection<LevelSection> Sections => _sections.Values;
    /// <summary>Ctil records whose payload carries an ScTA (texture animation) chunk; not ported.</summary>
    public IReadOnlyList<int> SectionsWithAnimations => _animated;
    private readonly List<int> _animated = new();

    private LevelMap(MapHeader header) => Header = header;

    public LevelSection? Section(int id) => _sections.TryGetValue(id, out var s) ? s : null;

    /// <summary>Section of the grid cell containing world position (x, z), or null outside the grid.</summary>
    public LevelSection? SectionAtWorld(int x, int z)
    {
        int row = x >> 17, column = z >> 17;
        if (x < 0 || z < 0 || row >= Header.Height || column >= Header.Width) return null;
        return Section(Header.SectionIdAt(row, column));
    }

    public static LevelMap FromMisFile(string path) => FromBytes(File.ReadAllBytes(path));

    public static LevelMap FromBytes(byte[] mis)
    {
        uint ctil = ChunkReader.TagValue("Ctil"), cptc = ChunkReader.TagValue("Cptc");
        uint shoc = ChunkReader.TagValue("SHOC"), shdr = ChunkReader.TagValue("SHDR");
        MapHeader? header = null;
        var tiles = new List<(int Id, byte[] Payload)>();
        for (int p = 24; p + 12 <= mis.Length; p += 4)
        {
            uint tag = BinaryPrimitives.ReadUInt32LittleEndian(mis.AsSpan(p));
            if (tag != ctil && tag != cptc) continue;
            if (BinaryPrimitives.ReadUInt32LittleEndian(mis.AsSpan(p - 24)) != shoc ||
                BinaryPrimitives.ReadUInt32LittleEndian(mis.AsSpan(p - 8)) != shdr) continue;
            int id = BinaryPrimitives.ReadInt32LittleEndian(mis.AsSpan(p + 4));
            int size = BinaryPrimitives.ReadInt32LittleEndian(mis.AsSpan(p + 8));
            var payload = MisFile.ReassembleStreamedPayload(mis, p - 24, size);
            if (payload == null) continue;
            if (tag == cptc) header = MapHeader.Parse(payload) ?? header;
            else tiles.Add((id, payload));
        }
        if (header == null) throw new InvalidDataException("mission has no Cptc/GRDB map header");
        var map = new LevelMap(header);
        foreach (var (id, payload) in tiles) map.LoadSection(id, payload);
        return map;
    }

    /// <summary>func_800639D4 over one Ctil payload.</summary>
    private void LoadSection(int id, byte[] data)
    {
        uint scla = 0x53634C41, slfx = 0x534C4658, scta = 0x53635441, sect = 0x53656374;
        LevelSection? section = null;
        int p = 0;
        while (p + 12 <= data.Length)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p));
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 4));
            int chunk = p;
            if (size <= 0) break;
            p += size;
            if (type == scla) continue;
            if (type < scla)
            {
                if (type != slfx) continue;
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(chunk + 8));
                int index = TileDefinitions.Add(value);
                if (section == null) throw new InvalidDataException($"Ctil {id}: SLFX before any Sect chunk");
                section.TileSet = (sbyte)index;
                continue;
            }
            if (type == scta)
            {
                // Texture animation records (relocation + D_8009FA04 chain) are not ported.
                _animated.Add(id);
                continue;
            }
            if (type != sect) continue;
            // func_800639D4 reads the counts at +0x08 and the entry count at +0x3CA, so a shorter
            // Sect chunk is corrupt data, not a valid section with empty tables.
            if (chunk + 0x3CC > data.Length)
                throw new InvalidDataException($"Ctil {id}: Sect chunk at 0x{chunk:X} is truncated");
            section = new LevelSection(id, data, chunk);
            _sections[id] = section;
        }
        TileDefinitions.Record(id);
    }
}
