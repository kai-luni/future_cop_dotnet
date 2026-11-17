using System.Buffers.Binary;

namespace FutureCop.Assets;

/// <summary>
/// Reader for retail mission archives (.MIS). Records are located by scanning for the
/// Cobj tag at 4-byte alignment and validating that a 4DGI chunk follows the 56-byte
/// record header; this avoids depending on the not-fully-documented record stride.
/// </summary>
public sealed class MisFile
{
    private readonly byte[] _data;
    public string Path { get; }
    public IReadOnlyList<int> SectionOffsets { get; }
    public IReadOnlyList<Model> Models { get; }

    private static readonly uint CobjTag = ChunkReader.TagValue("Cobj");
    private static readonly uint CtrlTag = ChunkReader.TagValue("CTRL");
    private static readonly uint GiTag = ChunkReader.TagValue("4DGI");

    public MisFile(string path)
    {
        Path = path;
        _data = File.ReadAllBytes(path);
        SectionOffsets = ReadSections();
        Models = ReadModels();
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
            if (BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(chunkStart)) != GiTag) continue;
            var model = ParseModel(p, chunkStart);
            if (model != null) models.Add(model);
        }
        return models;
    }

    private Model? ParseModel(int recordOffset, int chunkStart)
    {
        var model = new Model
        {
            Id = (int)BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(recordOffset + 4)),
            RecordOffset = recordOffset,
        };
        // Bound the chunk group by the next Cobj record (or EOF); zero padding ends the walk.
        int limit = _data.Length;
        for (int q = chunkStart + 4; q + 4 <= _data.Length; q += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(q)) == CobjTag) { limit = q; break; }
        }
        foreach (var chunk in ChunkReader.Walk(_data, chunkStart, limit))
        {
            string name = ChunkReader.TagName(chunk.Tag);
            model.ChunkTags.Add(name);
            var span = _data.AsSpan(chunk.DataOffset, chunk.DataLength);
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
                    model.TextureRecordCount = chunk.DataLength; // byte size; record parsing later
                    break;
                case "3DHY":
                    model.HierarchyEntries = chunk.DataLength / 0x14;
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