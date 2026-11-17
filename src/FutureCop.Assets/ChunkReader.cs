using System.Buffers.Binary;
using System.Text;

namespace FutureCop.Assets;

/// <summary>Length-delimited chunk walking shared by .MIS containers and Cobj models.</summary>
public static class ChunkReader
{
    /// <summary>Tags store the ASCII name byte-reversed; decode for display.</summary>
    public static string TagName(uint tag)
    {
        var bytes = BitConverter.GetBytes(tag);
        Array.Reverse(bytes);
        return Encoding.ASCII.GetString(bytes);
    }

    public static uint TagValue(string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name);
        Array.Reverse(bytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    public readonly record struct Chunk(uint Tag, int Index, int DataOffset, int DataLength);

    /// <summary>
    /// Walk chunks from <paramref name="start"/> until zero padding or an invalid length.
    /// Zero padding (2048-byte aligned gaps) terminates the model chunk group.
    /// </summary>
    public static IEnumerable<Chunk> Walk(byte[] data, int start, int limit)
    {
        int p = start;
        while (p + 12 <= limit)
        {
            uint tag = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p));
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 4));
            int index = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 8));
            if (tag == 0 || length < 12 || p + length > limit) yield break;
            yield return new Chunk(tag, index, p + 12, length - 12);
            p += length;
        }
    }
}