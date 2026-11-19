using System.Buffers.Binary;

namespace FutureCop.Assets;

/// <summary>
/// Reader for retail mission archives (.MIS). Records are located by scanning for the
/// Cobj tag at 4-byte alignment and validating that a 4DGI chunk follows the 56-byte
/// record header; this avoids depending on the not-fully-documented record stride.
/// </summary>
public readonly record struct ResourceReference(string Type, int Id);
public readonly record struct PlayerSpawn(int X, int Z, short Heading, sbyte GroundSelector, ushort Health);
public sealed record PlayerActor(int Id, IReadOnlyList<ResourceReference> Resources)
{
    public PlayerSpawn? Spawn { get; init; }
}

public sealed class MisFile
{
    private readonly byte[] _data;
    public string Path { get; }

    /// <summary>Raw archive bytes; lets readers that walk other chunk families (entity definitions)
    /// reuse the parsed file instead of loading it again.</summary>
    public byte[] Bytes => _data;

    public IReadOnlyList<int> SectionOffsets { get; }
    public IReadOnlyList<Model> Models { get; }
    public IReadOnlyList<PlayerActor> PlayerActors { get; }

    private static readonly uint CobjTag = ChunkReader.TagValue("Cobj");
    private static readonly uint CtrlTag = ChunkReader.TagValue("CTRL");
    private static readonly uint GiTag = ChunkReader.TagValue("4DGI");

    public MisFile(string path)
    {
        Path = path;
        _data = File.ReadAllBytes(path);
        SectionOffsets = ReadSections();
        Models = ReadModels();
        PlayerActors = ReadPlayerActors();
    }

    private IReadOnlyList<PlayerActor> ReadPlayerActors()
    {
        var actors = new List<PlayerActor>();
        uint tact = ChunkReader.TagValue("tACT"), lsra = ChunkReader.TagValue("aRSL");
        for (int p = 0; p + 12 <= _data.Length; p += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(p)) != tact) continue;
            int length = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(p + 4));
            int id = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(p + 8));
            if (length < 16 || length > _data.Length - p || _data[p + 12] != 1) continue;
            int q = p + length;
            if (q + 12 > _data.Length || BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(q)) != lsra) continue;
            int size = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(q + 4));
            if (size < 52 || size > _data.Length - q || (size - 12) % 8 != 0 ||
                BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(q + 8)) != id) continue;
            var resources = new List<ResourceReference>();
            for (int i = q + 12; i < q + size; i += 8)
                resources.Add(new ResourceReference(ChunkReader.TagName(BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(i))),
                    BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(i + 4))));
            var actor = new PlayerActor(id, resources);
            // func_8004D6D4 passes tACT + 8 as the spawn record to the entity handler.
            if (length >= 0x30)
            {
                var spawn = _data.AsSpan(p + 8, length - 8);
                actor = actor with { Spawn = new PlayerSpawn(
                    BinaryPrimitives.ReadInt32LittleEndian(spawn[0x08..]),
                    BinaryPrimitives.ReadInt32LittleEndian(spawn[0x10..]),
                    BinaryPrimitives.ReadInt16LittleEndian(spawn[0x24..]),
                    unchecked((sbyte)spawn[0x26]),
                    BinaryPrimitives.ReadUInt16LittleEndian(spawn[0x18..])) };
            }
            actors.Add(actor);
        }
        return actors;
    }

    private IReadOnlyList<int> ReadSections()
    {
        var offsets = new List<int>();
        if (_data.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(0)) == CtrlTag)
        {
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(4));
            for (int p = 12; p + 4 <= Math.Min(len, _data.Length); p += 4)
                offsets.Add((int)BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(p)));
        }
        return offsets;
    }

    private IReadOnlyList<Model> ReadModels()
    {
        var models = new List<Model>();
        for (int p = 0; p + 68 <= _data.Length; p += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(p)) != CobjTag) continue;
            int chunkStart = p + 56;
            bool streamed = p >= 24 &&
                BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(p - 24)) == ChunkReader.TagValue("SHOC") &&
                BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(p - 8)) == ChunkReader.TagValue("SHDR");
            if (!streamed && BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(chunkStart)) != GiTag) continue;
            var model = ParseModel(p, chunkStart, streamed);
            if (model != null) models.Add(model);
        }
        return models;
    }

    /// <summary>
    /// Reassembles a streamed SHOC/SDAT payload (with FILL read-block skips) starting at
    /// <paramref name="headerStart"/> (24 bytes before the Cobj/Cbmp record tag).
    /// </summary>
    public static byte[]? ReassembleStreamedPayload(byte[] data, int headerStart, int payloadSize)
    {
        if (headerStart < 0 || headerStart + 8 > data.Length) return null;
        int headerLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(headerStart + 4));
        if (headerLength < 60 || headerLength > data.Length - headerStart) return null;
        int packet = headerStart + headerLength;
        if (payloadSize < 0 || payloadSize > data.Length) return null;
        var payload = new byte[payloadSize];
        int copied = 0;
        uint fillTag = ChunkReader.TagValue("FILL");
        uint shocTag = ChunkReader.TagValue("SHOC");
        uint sdatTag = ChunkReader.TagValue("SDAT");
        while (copied < payload.Length && packet + 20 <= data.Length)
        {
            uint tag = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(packet));
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(packet + 4));
            if (length < 12 || length > data.Length - packet) return null;
            if (tag == fillTag)
            {
                // func_8002902C skips the remainder of the read block.
                packet = (int)(((long)packet / 0x6000 + 1) * 0x6000);
                continue;
            }
            uint subtype = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(packet + 16));
            if (tag != shocTag || subtype != sdatTag ||
                length < 20 || length - 20 > payload.Length - copied)
                return null;
            Buffer.BlockCopy(data, packet + 20, payload, copied, length - 20);
            copied += length - 20;
            packet += length;
        }
        return copied == payload.Length ? payload : null;
    }

    private Model? ParseModel(int recordOffset, int chunkStart, bool streamed)
    {
        int id = (int)BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(recordOffset + 4));
        int payloadSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(recordOffset + 8));
        if (streamed)
        {
            var payload = ReassembleStreamedPayload(_data, recordOffset - 24, payloadSize);
            if (payload == null) return null;
            return ParsePayload(id, recordOffset, payload, 0, payload.Length);
        }
        int limit = _data.Length;
        for (int q = chunkStart + 4; q + 4 <= _data.Length; q += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(q)) == CobjTag) { limit = q; break; }
        }
        return ParsePayload(id, recordOffset, _data, chunkStart, limit);
    }

    private static Model? ParsePayload(int id, int recordOffset, byte[] data, int chunkStart, int limit)
    {
        var model = new Model
        {
            Id = id,
            RecordOffset = recordOffset,
        };
        foreach (var chunk in ChunkReader.Walk(data, chunkStart, limit))
        {
            string name = ChunkReader.TagName(chunk.Tag);
            model.ChunkTags.Add(name);
            var span = data.AsSpan(chunk.DataOffset, chunk.DataLength);
            switch (name)
            {
                case "4DGI":
                    model.Header = new uint[chunk.DataLength / 4];
                    for (int i = 0; i < model.Header.Length; i++)
                        model.Header[i] = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(i * 4));
                    break;
                case "3DQL":
                    int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(span);
                    for (int i = 0; i < count && 4 + (i + 1) * 12 <= span.Length; i++)
                    {
                        var r = span.Slice(4 + i * 12, 12);
                        model.Polygons.Add(new Polygon(r[0], r[1],
                            BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(2)),
                            r[4], r[5], r[6], r[7], r[8], r[9], r[10], r[11]));
                    }
                    break;
                case "4DVL":
                case "4DNL":
                    var list = ReadVertexList(span);
                    if (name == "4DVL") model.Vertices[chunk.Index] = list;
                    else model.Normals[chunk.Index] = list;
                    break;
                case "3DRL":
                    var widths = new List<short>();
                    for (int o = 4; o + 2 <= span.Length; o += 2)
                        widths.Add(BinaryPrimitives.ReadInt16LittleEndian(span.Slice(o)));
                    model.Widths[chunk.Index] = widths;
                    break;
                case "3DTL":
                    model.TextureBody = span.ToArray();
                    model.TextureRecordCount = chunk.DataLength; // byte size kept for CLI output
                    model.Textures.AddRange(TextureList.Parse(model.TextureBody).Records);
                    break;
                case "3DHY":
                    model.HierarchyEntries = chunk.DataLength / 0x14;
                    for (int i = 0; i * 0x14 + 0x14 <= span.Length; i++)
                    {
                        var r = span.Slice(i * 0x14, 0x14);
                        model.Hierarchy.Add(new ScriptEntry(
                            r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7],
                            BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x08)),
                            BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x0A)),
                            BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x0C)),
                            BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x0E)),
                            BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x10)),
                            BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x12))));
                    }
                    break;
                case "3DHS":
                    model.AttachmentPointCount = chunk.Index;
                    for (int i = 0; i + 8 <= span.Length; i += 8)
                        model.AttachmentPoints.Add(new Vertex(BinaryPrimitives.ReadInt16LittleEndian(span.Slice(i)),
                            BinaryPrimitives.ReadInt16LittleEndian(span.Slice(i + 2)),
                            BinaryPrimitives.ReadInt16LittleEndian(span.Slice(i + 4))));
                    break;
                case "3DBB":
                    // func_80012E48 keeps chunk + 8; func_800518A8 reads records at pointer + 8.
                    for (int i = 4; i + 16 <= span.Length; i += 16)
                    {
                        var bound = new short[8];
                        for (int j = 0; j < bound.Length; j++)
                            bound[j] = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(i + j * 2));
                        model.FrameBounds.Add(bound);
                    }
                    break;
                case "AnmD":
                    for (int i = 0; i + 16 <= span.Length; i += 16)
                    {
                        var r = span.Slice(i, 16);
                        model.AnimationStarts.Add(BinaryPrimitives.ReadInt16LittleEndian(r.Slice(4)));
                        model.AnimationClips.Add(new AnimationClip(r[0], r[1], r[2], r[3],
                            BinaryPrimitives.ReadInt16LittleEndian(r.Slice(4)),
                            BinaryPrimitives.ReadInt16LittleEndian(r.Slice(6)),
                            r[8], r[9], BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(10)),
                            BinaryPrimitives.ReadInt32LittleEndian(r.Slice(12))));
                    }
                    break;
                case "3DMI":
                    model.MeshTable = new short[span.Length / 2];
                    for (int i = 0; i < model.MeshTable.Length; i++)
                        model.MeshTable[i] = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(i * 2));
                    break;
            }
        }
        return model.Polygons.Count > 0 || model.Header.Length > 0 ? model : null;
    }

    private static List<Vertex> ReadVertexList(ReadOnlySpan<byte> span)
    {
        var list = new List<Vertex>();
        if (span.Length < 4) return list;
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(span);
        for (int i = 0; i < count && 4 + (i + 1) * 8 <= span.Length; i++)
        {
            var r = span.Slice(4 + i * 8);
            list.Add(new Vertex(
                BinaryPrimitives.ReadInt16LittleEndian(r),
                BinaryPrimitives.ReadInt16LittleEndian(r.Slice(2)),
                BinaryPrimitives.ReadInt16LittleEndian(r.Slice(4))));
        }
        return list;
    }
}
