using System.Buffers.Binary;
using System.Text;

namespace FutureCop.Assets;

/// <summary>Extracts the front-end MenuTune1 stream from the retail FE.MIS archive.</summary>
public static class MenuMusic
{
    public const int SampleRate = 14260;
    private const int ChannelBytes = 0x2fc0;

    public static short[] Decode(byte[] data)
    {
        int name = data.AsSpan().IndexOf(Encoding.ASCII.GetBytes("MenuTune1_sw14."));
        if (name < 20 || !data.AsSpan(name - 4, 4).SequenceEqual(Encoding.ASCII.GetBytes("ELIF")))
            throw new InvalidDataException("MenuTune1 stream not found.");
        int stream = name - 20;
        if (!data.AsSpan(stream, 4).SequenceEqual(Encoding.ASCII.GetBytes("RVWS"))) throw new InvalidDataException("Invalid stream header.");
        int headerLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(stream + 4));
        if (headerLength < 36 || headerLength > data.Length - stream) throw new InvalidDataException("Invalid stream header length.");
        int p = stream + headerLength;
        using var left = new MemoryStream();
        using var right = new MemoryStream();
        int total = -1;
        for (int index = 0; total < 0 || index < total; index++)
        {
            while (p >= 0 && p + 8 <= data.Length && data.AsSpan(p, 4).SequenceEqual(Encoding.ASCII.GetBytes("LLIF")))
            {
                int fill = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 4));
                if (fill < 8 || fill > data.Length - p) throw new InvalidDataException("Invalid FILL block.");
                p += fill;
            }
            if (p < 0 || p + 28 > data.Length || !data.AsSpan(p, 4).SequenceEqual(Encoding.ASCII.GetBytes("MGAV")))
                throw new InvalidDataException("Missing VAGM block.");
            int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 4));
            int blockTotal = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 20));
            int blockIndex = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 22));
            int channelSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 24));
            if (total < 0) total = blockTotal;
            if (total == 0 || blockTotal != total || blockIndex != index || channelSize != ChannelBytes ||
                length != 28 + 2 * channelSize || length > data.Length - p ||
                !data.AsSpan(p + 16, 4).SequenceEqual(Encoding.ASCII.GetBytes("MGAV")))
                throw new InvalidDataException("Invalid VAGM block.");
            left.Write(data, p + 28, channelSize);
            right.Write(data, p + 28 + channelSize, channelSize);
            p += length;
        }
        var l = PsAdpcm.Decode(left.ToArray());
        var r = PsAdpcm.Decode(right.ToArray());
        var result = new short[l.Length * 2];
        for (int i = 0; i < l.Length; i++) { result[2 * i] = l[i]; result[2 * i + 1] = r[i]; }
        return result;
    }
}
