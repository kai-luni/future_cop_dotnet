using System.Buffers.Binary;

namespace FutureCop.Assets;

/// <summary>
/// Offsets and sizes of the blocks inside a reassembled Cbmp container (decomp func_8001996C).
/// CCB and PLUT skip the 4-byte chunk index word; PDAT and PIX4 start immediately after tag+length.
/// </summary>
public readonly record struct CbmpContainer(
    int CcbOffset, int CcbLength,
    int PdatOffset, int PdatLength,
    int PlutOffset, int PlutLength,
    bool IsPix4)
{
    private static readonly uint CcbTag = ChunkReader.TagValue("CCB ");
    private static readonly uint PlutTag = ChunkReader.TagValue("PLUT");
    private static readonly uint PdatTag = ChunkReader.TagValue("PDAT");
    private static readonly uint Pix4Tag = ChunkReader.TagValue("PIX4");

    /// <summary>Walks a Cbmp container payload and locates its CCB, PDAT/PIX4, and PLUT blocks.</summary>
    public static CbmpContainer Parse(ReadOnlySpan<byte> payload)
    {
        int ccbOff = -1, ccbLen = 0;
        int pdatOff = -1, pdatLen = 0;
        int plutOff = -1, plutLen = 0;
        bool isPix4 = false;

        int p = 0;
        while (p + 8 <= payload.Length)
        {
            uint tag = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(p));
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(p + 4));
            if (tag == 0 || length < 8 || p + length > payload.Length)
                break;

            if (tag == CcbTag && length >= 12)
            {
                ccbOff = p + 12;
                ccbLen = length - 12;
            }
            else if (tag == PlutTag && length >= 12)
            {
                plutOff = p + 12;
                plutLen = length - 12;
            }
            else if (tag == PdatTag)
            {
                pdatOff = p + 8;
                pdatLen = length - 8;
                isPix4 = false;
            }
            else if (tag == Pix4Tag)
            {
                pdatOff = p + 8;
                pdatLen = length - 8;
                isPix4 = true;
            }

            p += length;
        }

        return new CbmpContainer(ccbOff, ccbLen, pdatOff, pdatLen, plutOff, plutLen, isPix4);
    }
}

/// <summary>Metadata and VRAM placement for one uploaded Cbmp texture resource (id 1..10).</summary>
public sealed record CbmpResource(
    int Id,
    int RecordOffset,
    int PayloadSize,
    CbmpContainer Container,
    int PageX,
    int PageY,
    int PageW,
    int PageH,
    int ClutX,
    int ClutY,
    int ClutW,
    int ClutH,
    ushort Tpage,
    ushort Clut);

/// <summary>
/// Scans a .MIS archive for streamed Cbmp resources, reassembles their containers, and
/// uploads all texture pages and palettes into a <see cref="FutureCop.Assets.Vram"/> instance
/// (decomp func_80063C5C).
/// </summary>
public sealed class TextureBank
{
    private readonly HashSet<(int PageX, int PageY)> _loadedPages = new();

    public Vram Vram { get; } = new();
    public IReadOnlyList<CbmpResource> Resources { get; }

    private TextureBank(List<CbmpResource> resources)
    {
        Resources = resources;
        foreach (var r in resources)
            _loadedPages.Add((r.PageX, r.PageY));
    }

    /// <summary>Loads all Cbmp texture resources from a .MIS file on disk.</summary>
    public static TextureBank FromMisFile(string path) => FromBytes(File.ReadAllBytes(path));

    /// <summary>Loads all Cbmp texture resources from raw .MIS bytes.</summary>
    public static TextureBank FromBytes(byte[] misData)
    {
        uint cbmpTag = ChunkReader.TagValue("Cbmp");
        uint shocTag = ChunkReader.TagValue("SHOC");
        uint shdrTag = ChunkReader.TagValue("SHDR");
        var resources = new List<CbmpResource>();

        for (int p = 24; p + 12 <= misData.Length; p += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(misData.AsSpan(p)) != cbmpTag)
                continue;
            if (BinaryPrimitives.ReadUInt32LittleEndian(misData.AsSpan(p - 24)) != shocTag ||
                BinaryPrimitives.ReadUInt32LittleEndian(misData.AsSpan(p - 8)) != shdrTag)
                continue;

            int id = BinaryPrimitives.ReadInt32LittleEndian(misData.AsSpan(p + 4));
            int payloadSize = BinaryPrimitives.ReadInt32LittleEndian(misData.AsSpan(p + 8));
            if (id < 1 || id > 10)
                continue;

            var payload = MisFile.ReassembleStreamedPayload(misData, p - 24, payloadSize);
            if (payload == null)
                continue;

            var container = CbmpContainer.Parse(payload);
            int s0 = id - 1;
            int pageX = 0x180 + 128 * (s0 >> 1);
            int pageY = 256 * (s0 & 1);
            const int pageW = 0x80;
            const int pageH = 0xFC;
            int clutX = 0x180 + 256 * ((s0 >> 2) & 1);
            int clutY = 0xFC + (s0 & 3) + 256 * (s0 >> 3);
            const int clutW = 0x100;
            const int clutH = 1;

            uint remap = TextureList.RemapEntry(id);
            ushort tpage = (ushort)(remap & 0xFFFF);
            ushort clut = (ushort)(remap >> 16);

            resources.Add(new CbmpResource(
                id, p, payloadSize, container,
                pageX, pageY, pageW, pageH,
                clutX, clutY, clutW, clutH,
                tpage, clut));
        }

        var bank = new TextureBank(resources);
        foreach (var res in resources)
        {
            var payload = MisFile.ReassembleStreamedPayload(misData, res.RecordOffset - 24, res.PayloadSize)!;
            if (res.Container.PdatOffset >= 0 && res.Container.PdatLength >= res.PageW * res.PageH * 2)
                bank.Vram.LoadRect(res.PageX, res.PageY, res.PageW, res.PageH,
                    payload.AsSpan(res.Container.PdatOffset, res.PageW * res.PageH * 2));
            if (res.Container.PlutOffset >= 0 && res.Container.PlutLength >= 8 + res.ClutW * 2)
                bank.Vram.LoadRect(res.ClutX, res.ClutY, res.ClutW, res.ClutH,
                    payload.AsSpan(res.Container.PlutOffset + 8, res.ClutW * 2));
        }

        return bank;
    }

    /// <summary>Returns true if the VRAM page addressed by <paramref name="tpage"/> was uploaded by this bank.</summary>
    public bool IsPageLoaded(ushort tpage)
    {
        var (pageX, pageY, _) = Vram.DecodeTpage(tpage);
        return _loadedPages.Contains((pageX, pageY));
    }

    /// <summary>Expands a 256x256 texture page (using its associated CLUT) to a top-down RGB888 buffer.</summary>
    public byte[] ExportPageRgb(int resourceId)
    {
        var res = Resources.FirstOrDefault(r => r.Id == resourceId)
            ?? throw new KeyNotFoundException($"Cbmp resource {resourceId} not found.");
        var rgb = new byte[256 * 256 * 3];
        for (int v = 0; v < 256; v++)
        {
            for (int u = 0; u < 256; u++)
            {
                var (r, g, b, _) = Vram.SampleRgb(res.Tpage, res.Clut, u, v);
                int o = (v * 256 + u) * 3;
                rgb[o] = r;
                rgb[o + 1] = g;
                rgb[o + 2] = b;
            }
        }
        return rgb;
    }

    /// <summary>Writes a top-down RGB888 buffer to a 24-bit uncompressed BMP file.</summary>
    public static void WriteBmp24(string path, int width, int height, ReadOnlySpan<byte> rgb)
    {
        int stride = (width * 3 + 3) & ~3;
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0x4D42);
        bw.Write(54 + stride * height);
        bw.Write(0u);
        bw.Write(54);
        bw.Write(40);
        bw.Write(width);
        bw.Write(height);
        bw.Write((ushort)1);
        bw.Write((ushort)24);
        bw.Write(0u);
        bw.Write(stride * height);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0u);
        bw.Write(0u);

        var row = new byte[stride];
        for (int y = height - 1; y >= 0; y--)
        {
            int srcRow = y * width * 3;
            for (int x = 0; x < width; x++)
            {
                int s = srcRow + x * 3;
                int d = x * 3;
                row[d] = rgb[s + 2];
                row[d + 1] = rgb[s + 1];
                row[d + 2] = rgb[s];
            }
            bw.Write(row);
        }
    }
}
