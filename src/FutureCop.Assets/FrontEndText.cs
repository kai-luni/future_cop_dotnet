using System.Buffers.Binary;
using System.Text;

namespace FutureCop.Assets;

/// <summary>Front-end string table lookup from func_80089914.</summary>
public sealed class FrontEndText
{
    private readonly Dictionary<ushort, string> _entries;
    private FrontEndText(Dictionary<ushort, string> entries) => _entries = entries;

    public string? Lookup(ushort id) => _entries.GetValueOrDefault(id);

    public static FrontEndText Parse(byte[] payload)
    {
        if (payload.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(payload) != 1)
            throw new InvalidDataException("Invalid front-end text table header.");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2));
        if (count > (payload.Length - 4) / 4)
            throw new InvalidDataException("Truncated front-end text table.");
        var entries = new Dictionary<ushort, string>();
        for (int i = 0; i < count; i++)
        {
            int entry = 4 + i * 4;
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(entry));
            int start = 4 + BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(entry + 2));
            if (start < 4 + count * 4 || start >= payload.Length)
                throw new InvalidDataException("Invalid front-end text offset.");
            int end = Array.IndexOf(payload, (byte)0, start);
            if (end < 0 || !entries.TryAdd(id, Encoding.ASCII.GetString(payload, start, end - start)))
                throw new InvalidDataException("Invalid front-end text entry.");
        }
        return new FrontEndText(entries);
    }

    public static FrontEndText FromMisFile(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        uint tag = ChunkReader.TagValue("Ctrs");
        uint shoc = ChunkReader.TagValue("SHOC");
        uint shdr = ChunkReader.TagValue("SHDR");
        for (int p = 24; p + 12 <= data.Length; p += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p)) != tag ||
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p - 24)) != shoc ||
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p - 8)) != shdr ||
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 4)) != 1)
                continue;
            int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 8));
            byte[]? payload = MisFile.ReassembleStreamedPayload(data, p - 24, length);
            if (payload != null) return Parse(payload);
        }
        throw new InvalidDataException("Front-end text resource not found.");
    }
}
