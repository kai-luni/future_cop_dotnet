using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Minimal PNG reader/writer for test comparisons: 8-bit RGB or RGBA, non-interlaced
/// (what jpsxdec writes). Pixels are 0xAARRGGBB ints, row-major.
/// </summary>
internal static class PngTestIo
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static (int Width, int Height, int[] Argb) Read(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        if (!file.AsSpan(0, 8).SequenceEqual(Signature)) throw new InvalidDataException("not a PNG: " + path);
        int width = 0, height = 0, colorType = 0;
        var idat = new MemoryStream();
        int offset = 8;
        while (offset + 8 <= file.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(offset, 4));
            string type = Encoding.ASCII.GetString(file, offset + 4, 4);
            var body = file.AsSpan(offset + 8, length);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(body);
                height = BinaryPrimitives.ReadInt32BigEndian(body.Slice(4));
                colorType = body[9];
                if (body[8] != 8 || body[12] != 0 || (colorType != 2 && colorType != 6))
                    throw new NotSupportedException("only 8-bit non-interlaced RGB/RGBA PNGs are supported");
            }
            else if (type == "IDAT")
            {
                idat.Write(body);
            }
            else if (type == "IEND")
            {
                break;
            }

            offset += 12 + length;
        }

        int bpp = colorType == 6 ? 4 : 3;
        int stride = width * bpp;
        var raw = new MemoryStream();
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(raw);
        byte[] data = raw.ToArray();
        var current = new byte[stride];
        var previous = new byte[stride];
        var pixels = new int[width * height];
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * (stride + 1);
            byte filter = data[rowStart];
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? current[i - bpp] : 0;
                int b = previous[i];
                int c = i >= bpp ? previous[i - bpp] : 0;
                int x = data[rowStart + 1 + i];
                current[i] = filter switch
                {
                    0 => (byte)x,
                    1 => (byte)(x + a),
                    2 => (byte)(x + b),
                    3 => (byte)(x + ((a + b) >> 1)),
                    4 => (byte)(x + Paeth(a, b, c)),
                    _ => throw new InvalidDataException("bad PNG filter"),
                };
            }

            for (int px = 0; px < width; px++)
            {
                int alpha = bpp == 4 ? current[px * 4 + 3] : 0xFF;
                pixels[y * width + px] = (alpha << 24) | (current[px * bpp] << 16) | (current[px * bpp + 1] << 8) | current[px * bpp + 2];
            }

            (current, previous) = (previous, current);
        }

        return (width, height, pixels);
    }

    public static void Write(string path, int width, int height, int[] argb)
    {
        var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < width; x++)
            {
                int p = argb[y * width + x];
                raw.WriteByte((byte)(p >> 16));
                raw.WriteByte((byte)(p >> 8));
                raw.WriteByte((byte)p);
            }
        }

        var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, true)) raw.WriteTo(z);
        using var output = File.Create(path);
        output.Write(Signature);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        WriteChunk(output, "IHDR", ihdr);
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", Array.Empty<byte>());
    }

    /// <summary>Mean absolute difference over R, G and B of two equally sized images.</summary>
    public static double MeanAbsoluteError(int[] a, int[] b)
    {
        if (a.Length != b.Length) throw new ArgumentException("size mismatch");
        long total = 0;
        for (int i = 0; i < a.Length; i++)
        {
            total += Math.Abs(((a[i] >> 16) & 0xFF) - ((b[i] >> 16) & 0xFF));
            total += Math.Abs(((a[i] >> 8) & 0xFF) - ((b[i] >> 8) & 0xFF));
            total += Math.Abs((a[i] & 0xFF) - (b[i] & 0xFF));
        }

        return total / (3.0 * a.Length);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream output, string type, byte[] body)
    {
        var header = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
        Encoding.ASCII.GetBytes(type, 0, 4, header, 4);
        output.Write(header);
        output.Write(body);
        uint crc = Crc(header.AsSpan(4, 4), 0xFFFFFFFF);
        crc = Crc(body, crc) ^ 0xFFFFFFFF;
        var tail = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, crc);
        output.Write(tail);
    }

    private static uint Crc(ReadOnlySpan<byte> data, uint crc)
    {
        foreach (byte value in data)
        {
            crc ^= value;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }

        return crc;
    }
}
