using System.Buffers.Binary;

namespace FutureCop.Assets;

/// <summary>
/// Software model of the PS1 1024x512 16-bit VRAM used for texture pages and CLUT palettes.
/// </summary>
public sealed class Vram
{
    public const int Width = 1024;
    public const int Height = 512;

    private readonly ushort[] _cells = new ushort[Width * Height];

    /// <summary>Reads a 16-bit little-endian cell at VRAM coordinates (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public ushort ReadCell(int x, int y)
    {
        if ((uint)x >= Width || (uint)y >= Height)
            throw new ArgumentOutOfRangeException(nameof(x), $"VRAM coordinate ({x}, {y}) out of range.");
        return _cells[y * Width + x];
    }

    /// <summary>
    /// Uploads a rectangle of 16-bit little-endian halfwords into VRAM at
    /// (<paramref name="x"/>, <paramref name="y"/>) with dimensions <paramref name="w"/> x <paramref name="h"/>.
    /// </summary>
    public void LoadRect(int x, int y, int w, int h, ReadOnlySpan<byte> source)
    {
        if (x < 0 || y < 0 || w < 0 || h < 0 || x + w > Width || y + h > Height)
            throw new ArgumentOutOfRangeException(nameof(x), $"VRAM rect ({x}, {y}, {w}, {h}) out of range.");
        int requiredBytes = w * h * 2;
        if (source.Length < requiredBytes)
            throw new ArgumentException($"Source span too small: {source.Length} < {requiredBytes}.", nameof(source));

        int srcOffset = 0;
        for (int row = 0; row < h; row++)
        {
            int dstRow = (y + row) * Width + x;
            for (int col = 0; col < w; col++)
            {
                _cells[dstRow + col] = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(srcOffset, 2));
                srcOffset += 2;
            }
        }
    }

    /// <summary>Decodes a packed PS1 texture page word into VRAM base coordinates and bit-depth code.</summary>
    public static (int PageX, int PageY, int DepthBits) DecodeTpage(ushort tpage)
    {
        int pageX = (tpage & 0x0F) * 64;
        int pageY = ((tpage >> 4) & 1) * 256;
        int depthBits = (tpage >> 7) & 3;
        return (pageX, pageY, depthBits);
    }

    /// <summary>Decodes a packed PS1 CLUT word into VRAM palette coordinates.</summary>
    public static (int ClutX, int ClutY) DecodeClut(ushort clut)
    {
        int clutX = (clut & 0x3F) * 16;
        int clutY = (clut >> 6) & 0x1FF;
        return (clutX, clutY);
    }

    /// <summary>
    /// Samples a raw 16-bit ABGR1555 texel from VRAM using packed <paramref name="tpage"/> and
    /// <paramref name="clut"/> words at page-local coordinates (<paramref name="u"/>, <paramref name="v"/>).
    /// </summary>
    public ushort SampleRaw(ushort tpage, ushort clut, int u, int v)
    {
        var (pageX, pageY, depthBits) = DecodeTpage(tpage);
        int tx = u & 0xFF;
        int ty = v & 0xFF;

        switch (depthBits)
        {
            case 0: // 4-bit CLUT
            {
                var (clutX, clutY) = DecodeClut(clut);
                ushort cell = ReadCell(pageX + (tx >> 2), pageY + ty);
                int index = (cell >> ((tx & 3) * 4)) & 0x0F;
                return ReadCell(clutX + index, clutY);
            }
            case 1: // 8-bit CLUT
            {
                var (clutX, clutY) = DecodeClut(clut);
                ushort cell = ReadCell(pageX + (tx >> 1), pageY + ty);
                int index = (cell >> ((tx & 1) * 8)) & 0xFF;
                return ReadCell(clutX + index, clutY);
            }
            default: // 15-bit direct (2 or 3)
                return ReadCell(pageX + tx, pageY + ty);
        }
    }

    /// <summary>Expands a 16-bit PS1 ABGR1555 colour to 8-bit RGB plus transparency flag (0x0000 = transparent).</summary>
    public static (byte R, byte G, byte B, bool Transparent) Abgr1555ToRgb(ushort raw)
    {
        if (raw == 0)
            return (0, 0, 0, true);
        int r5 = raw & 0x1F;
        int g5 = (raw >> 5) & 0x1F;
        int b5 = (raw >> 10) & 0x1F;
        return (
            (byte)((r5 << 3) | (r5 >> 2)),
            (byte)((g5 << 3) | (g5 >> 2)),
            (byte)((b5 << 3) | (b5 >> 2)),
            false);
    }

    /// <summary>Samples a texel and converts it to 8-bit RGB plus transparency flag.</summary>
    public (byte R, byte G, byte B, bool Transparent) SampleRgb(ushort tpage, ushort clut, int u, int v) =>
        Abgr1555ToRgb(SampleRaw(tpage, clut, u, v));
}
