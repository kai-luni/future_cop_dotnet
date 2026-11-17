using System.Buffers.Binary;

namespace FutureCop.Assets;

/// <summary>One decoded 3DTL texture record (on-disc form plus loader rewrite).</summary>
public readonly record struct TextureRecord(
    int Offset, int Size, byte TypeRaw, byte TypeLoaded, uint Rgb,
    uint Uv0, uint Uv1, int TexIndex, ushort Clut, ushort Tpage, int? SemiTransMode);

public enum TextureWalkStatus { Ok, UnknownType, IndexOutOfRange }

public readonly record struct TextureWalkResult(
    TextureWalkStatus Status, int ErrorOffset, byte[] Output, List<TextureRecord> Records);

/// <summary>
/// 3DTL decode mirroring the retail loader (decomp func_800131A0:76-99):
/// type 0/1 = 4-byte colour record, untouched; type 2/3 = 16-byte record whose word3
/// (texture index) is replaced by the Cbmp remap entry, type 3 additionally ORs 0x20
/// (TPAGE semi-trans mode 1) and becomes type 2. Types &gt;= 4 hang the retail loop;
/// here they are reported as UnknownType. Indices &gt; 9 read past the retail table
/// (undefined); here reported as IndexOutOfRange in strict mode.
/// </summary>
public static class TextureList
{
    /// <summary>Cbmp remap entry for resource id (1..10), decomp func_80063C5C:72-117.</summary>
    public static uint RemapEntry(int id)
    {
        int i = id - 1;
        int pageY = (i & 1) * 256;
        int pageX = 384 + 128 * (i >> 1);
        uint tpage = (uint)((pageY >> 4) | ((pageX & 0x3FF) >> 6) | 0x80);
        int clutX = 384 + 256 * ((i >> 2) & 1);
        int clutY = 252 + (i & 3) + 256 * (i >> 3);
        uint clut = (uint)((clutY << 6) | ((clutX >> 4) & 0x3F));
        return (clut << 16) | tpage;
    }

    public static uint[] DefaultRemap() => Enumerable.Range(1, 10).Select(RemapEntry).ToArray();

    public static TextureWalkResult Parse(byte[] body, uint[]? remap = null, bool strict = true)
    {
        remap ??= DefaultRemap();
        var output = (byte[])body.Clone();
        var records = new List<TextureRecord>();
        int p = 0;
        while (p < output.Length)
        {
            byte type = output[p];
            if (type is 0 or 1)
            {
                if (p + 4 > output.Length) return new(TextureWalkStatus.UnknownType, p, output, records);
                records.Add(new TextureRecord(p, 4, type, type,
                    (uint)((output[p + 1] << 16) | (output[p + 2] << 8) | output[p + 3]), 0, 0, -1, 0, 0, null));
                p += 4;
            }
            else if (type is 2 or 3)
            {
                if (p + 16 > output.Length) return new(TextureWalkStatus.UnknownType, p, output, records);
                int index = (int)BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(p + 12));
                if (index < 0 || index >= remap.Length)
                {
                    if (strict) return new(TextureWalkStatus.IndexOutOfRange, p, output, records);
                    index = 0;
                }
                uint val = remap[index];
                byte loaded = type;
                int? semi = null;
                if (type == 3) { val |= 0x20; loaded = 2; semi = 1; }
                BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(p + 12), val);
                output[p] = loaded;
                var span = output.AsSpan(p);
                records.Add(new TextureRecord(p, 16, type, loaded,
                    (uint)((output[p + 1] << 16) | (output[p + 2] << 8) | output[p + 3]),
                    BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4)),
                    BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8)),
                    index, (ushort)(val >> 16), (ushort)(val & 0xFFFF), semi));
                p += 16;
            }
            else
            {
                return new(TextureWalkStatus.UnknownType, p, output, records);
            }
        }
        return new(TextureWalkStatus.Ok, -1, output, records);
    }
}