using System.Buffers.Binary;
using FutureCop.Assets;

namespace FutureCop.Game;

/// <summary>
/// Which loader request delivered an entity record. Both are SHOC/SHDR request headers whose tag word
/// (header + 0x18) the asset loader switches on (func_80024EC8:149-201).
/// </summary>
public enum EntityContainer
{
    /// <summary><c>Cact</c> (0x43616374): gLoadActorChunk func_8004D6D4 builds the entity at load time.</summary>
    Cact,

    /// <summary><c>Csac</c> (0x43736163): gRegisterUnitSpawner func_80060E74 registers a spawner whose
    /// tACT is the template of the units it creates.</summary>
    Csac,
}

/// <summary>
/// One entity definition of a mission: the tACT record a Cact or Csac request carries, read through
/// the common header every entity class copies in gInitEntityBase.
/// </summary>
/// <remarks>
/// <para>
/// The source block is tACT + 8: func_8004D6D4:103 stores <c>func_80019C38(...) - 4</c> and reads the
/// dispatch type at +4 of it (8004D6D4.c:105), which is the byte at tACT + 0x0C. func_8002A5F4
/// (8002A5F4.c:116-133, 159) then copies from that block: id u16 +0x00 -> obj+0x14, type u8 +0x04 ->
/// obj+0x16, position s32 +0x08/+0x0C/+0x10 -> obj+0x50, flags u32 +0x14 -> obj+0x24, health u16
/// +0x18 -> obj+0x1C, team u8 +0x1C -> obj+0x18.
/// </para>
/// <para>
/// The three script words are the request header's +0x24/+0x28/+0x2C (header words 9..11), which
/// func_80024EC8:145-147 rebases by the RPNS bias D_8009F9F4 and func_8002A5F4:107-110 stores into
/// obj+0x94/+0x98/+0x9C. They are kept as raw offsets into the RPNS payload here.
/// </para>
/// </remarks>
/// <param name="Id">Source +0x00 (u16), the entity id at obj+0x14.</param>
/// <param name="Type">Source +0x04, the dispatch type of func_8004D6D4 / func_8002AE54.</param>
/// <param name="X">Source +0x08, world x.</param>
/// <param name="Y">Source +0x0C, world y.</param>
/// <param name="Z">Source +0x10, world z.</param>
/// <param name="Flags">Source +0x14, the flag word at obj+0x24.</param>
/// <param name="Health">Source +0x18, the health word at obj+0x1C.</param>
/// <param name="Team">Source +0x1C, the team byte at obj+0x18.</param>
/// <param name="Container">The request kind that carried the record.</param>
/// <param name="HeaderOffset">File offset of the SHOC/SHDR request header.</param>
/// <param name="ChunkOffset">File offset of the tACT chunk.</param>
/// <param name="Source">The source block, tACT + 8 up to the end of the chunk.</param>
/// <param name="Scripts">Header words 9..11: RPNS offsets of the scripts at obj+0x94/+0x98/+0x9C.</param>
/// <param name="Resources">The aRSL list of the same request, in file order.</param>
public sealed record EntityRecord(
    int Id, int Type, int X, int Y, int Z, uint Flags, int Health, int Team,
    EntityContainer Container, int HeaderOffset, int ChunkOffset, byte[] Source,
    IReadOnlyList<int> Scripts, IReadOnlyList<ResourceReference> Resources)
{
    /// <summary>Unsigned byte at source + <paramref name="offset"/>, or 0 past the record.</summary>
    public int U8(int offset) => offset >= 0 && offset < Source.Length ? Source[offset] : 0;

    /// <summary>Unsigned halfword at source + <paramref name="offset"/>, or 0 past the record.</summary>
    public int U16(int offset) => offset >= 0 && offset + 2 <= Source.Length
        ? BinaryPrimitives.ReadUInt16LittleEndian(Source.AsSpan(offset)) : 0;

    /// <summary>The <paramref name="index"/>-th Cobj reference of the aRSL list, or 0.</summary>
    public int Model(int index = 0) =>
        Resources.Where(r => r.Type == "Cobj").Skip(index).Select(r => r.Id).FirstOrDefault();
}

/// <summary>
/// Reads every entity definition of a mission archive: each SHOC/SHDR request tagged Cact or Csac and
/// the tACT/aRSL pair of the data packet that follows it. tACT chunks outside such a request (CONFT.MIS
/// carries 280 of them between 0x61968 and 0x68928, ids 0 and 45994732) are not entities and are
/// skipped.
/// </summary>
public static class MissionEntities
{
    private static readonly uint Shoc = ChunkReader.TagValue("SHOC");
    private static readonly uint Shdr = ChunkReader.TagValue("SHDR");
    private static readonly uint Sdat = ChunkReader.TagValue("SDAT");
    private static readonly uint Fill = ChunkReader.TagValue("FILL");
    private static readonly uint Cact = ChunkReader.TagValue("Cact");
    private static readonly uint Csac = ChunkReader.TagValue("Csac");
    private static readonly uint Tact = ChunkReader.TagValue("tACT");
    private static readonly uint Arsl = ChunkReader.TagValue("aRSL");

    /// <summary>Smallest tACT that still holds the common header up to the team byte (source + 0x1C).</summary>
    private const int MinChunkLength = 8 + 0x20;

    /// <summary>All entity records of <paramref name="file"/>, in file order.</summary>
    public static IReadOnlyList<EntityRecord> Load(MisFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Parse(file.Bytes);
    }

    /// <summary>Scanner behind <see cref="Load"/>, testable without a full archive.</summary>
    public static IReadOnlyList<EntityRecord> Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var records = new List<EntityRecord>();
        for (int h = 0; h + 0x30 <= data.Length; h += 4)
        {
            if (Word(data, h) != Shoc || Word(data, h + 0x10) != Shdr) continue;
            uint tag = Word(data, h + 0x18);
            if (tag != Cact && tag != Csac) continue;
            int headerLength = (int)Word(data, h + 4);
            if (headerLength < 0x30 || headerLength > data.Length - h) continue;
            int id = (int)Word(data, h + 0x1C);
            var scripts = new[] { (int)Word(data, h + 0x24), (int)Word(data, h + 0x28), (int)Word(data, h + 0x2C) };

            // The request's data packet: SHOC, length, two words, SDAT, then the chunks.
            int packet = h + headerLength;
            // A FILL packet pads the read block; func_8002902C resumes at the next 0x6000 boundary
            // (the same rule MisFile.ReassembleStreamedPayload applies).
            if (packet + 4 <= data.Length && Word(data, packet) == Fill) packet = (packet / 0x6000 + 1) * 0x6000;
            if (packet + 20 > data.Length || Word(data, packet) != Shoc || Word(data, packet + 0x10) != Sdat) continue;
            int packetEnd = Math.Min(data.Length, packet + (int)Word(data, packet + 4));
            int chunk = FindChunk(data, packet + 20, packetEnd, Tact, id);
            if (chunk < 0) continue;
            int length = (int)Word(data, chunk + 4);
            if (length < MinChunkLength || chunk + length > data.Length) continue;
            var source = data.AsSpan(chunk + 8, length - 8).ToArray();
            records.Add(new EntityRecord(
                Id: BinaryPrimitives.ReadUInt16LittleEndian(source),
                Type: source[4],
                X: BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(0x08)),
                Y: BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(0x0C)),
                Z: BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(0x10)),
                Flags: BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(0x14)),
                Health: BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(0x18)),
                Team: source[0x1C],
                Container: tag == Cact ? EntityContainer.Cact : EntityContainer.Csac,
                HeaderOffset: h,
                ChunkOffset: chunk,
                Source: source,
                Scripts: scripts,
                Resources: ReadResources(data, packet + 20, packetEnd, id)));
        }
        return records;
    }

    private static uint Word(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));

    /// <summary>First 4-aligned chunk of <paramref name="tag"/> with chunk id <paramref name="id"/>.</summary>
    private static int FindChunk(byte[] data, int start, int end, uint tag, int id)
    {
        for (int p = start; p + 12 <= end; p += 4)
            if (Word(data, p) == tag && (int)Word(data, p + 8) == id) return p;
        return -1;
    }

    /// <summary>The aRSL list func_8004D6D4:146-156 / func_80060E74:127-141 resolve: (size - 0xC) / 8 pairs.</summary>
    private static IReadOnlyList<ResourceReference> ReadResources(byte[] data, int start, int end, int id)
    {
        int p = FindChunk(data, start, end, Arsl, id);
        if (p < 0) return Array.Empty<ResourceReference>();
        int size = (int)Word(data, p + 4);
        if (size < 12 || p + size > data.Length) return Array.Empty<ResourceReference>();
        var list = new List<ResourceReference>((size - 12) / 8);
        for (int i = p + 12; i + 8 <= p + size; i += 8)
            list.Add(new ResourceReference(ChunkReader.TagName(Word(data, i)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(i + 4))));
        return list;
    }
}
