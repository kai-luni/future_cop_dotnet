using System.Buffers.Binary;
using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

public sealed record CbmpGoldenCase(
    string Id, byte[] Input,
    int CcbOffset, int CcbLength,
    int PdatOffset, int PdatLength,
    int PlutOffset, int PlutLength,
    bool IsPix4);

public class TextureBankTests
{
    public static IEnumerable<object[]> CbmpCases()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "golden", "cbmp.vectors");
        string id = "";
        string status = "ok";
        byte[] input = Array.Empty<byte>();
        int[] expected = Array.Empty<int>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (t[0])
            {
                case "CASE":
                    id = t[1];
                    status = t.Length > 2 ? t[2] : "ok";
                    input = Array.Empty<byte>();
                    expected = Array.Empty<int>();
                    break;
                case "IN":
                    input = GoldenFixture.Hex(t.Length > 1 ? t[1] : "");
                    break;
                case "OUT":
                    // Parity expectation: the C harness asserts exactly this line.
                    expected = t.Skip(1).Select(int.Parse).ToArray();
                    break;
                case "NET":
                    // .NET-only expectation for the c_skip cases where the C original is undefined.
                    if (status == "ok") throw new InvalidOperationException($"NET line in a parity case: {id}");
                    expected = t.Skip(1).Select(int.Parse).ToArray();
                    break;
                case "END":
                    yield return new object[]
                    {
                        new CbmpGoldenCase(id, input,
                            expected[0], expected[1], expected[2], expected[3],
                            expected[4], expected[5], expected[6] != 0)
                    };
                    break;
            }
        }
    }

    [Theory]
    [MemberData(nameof(CbmpCases))]
    public void CbmpContainerGolden(CbmpGoldenCase c)
    {
        var parsed = CbmpContainer.Parse(c.Input);
        Assert.Equal(c.CcbOffset, parsed.CcbOffset);
        Assert.Equal(c.CcbLength, parsed.CcbLength);
        Assert.Equal(c.PdatOffset, parsed.PdatOffset);
        Assert.Equal(c.PdatLength, parsed.PdatLength);
        Assert.Equal(c.PlutOffset, parsed.PlutOffset);
        Assert.Equal(c.PlutLength, parsed.PlutLength);
        Assert.Equal(c.IsPix4, parsed.IsPix4);
    }

    [Fact]
    public void VramDecodesAllTenSlotsAndMechTarget()
    {
        for (int id = 1; id <= 10; id++)
        {
            int s0 = id - 1;
            uint remap = TextureList.RemapEntry(id);
            ushort tpage = (ushort)(remap & 0xFFFF);
            ushort clut = (ushort)(remap >> 16);

            var (px, py, depth) = Vram.DecodeTpage(tpage);
            var (cx, cy) = Vram.DecodeClut(clut);

            Assert.Equal(0x180 + 128 * (s0 >> 1), px);
            Assert.Equal(256 * (s0 & 1), py);
            Assert.Equal(1, depth); // 8-bit
            Assert.Equal(0x180 + 256 * ((s0 >> 2) & 1), cx);
            Assert.Equal(0xFC + (s0 & 3) + 256 * (s0 >> 3), cy);
        }

        // Mech model 11 uses tpage 0x008E, clut 0x7F18 (slot index 8 -> resource id 9).
        var (mechPx, mechPy, mechDepth) = Vram.DecodeTpage(0x008E);
        var (mechCx, mechCy) = Vram.DecodeClut(0x7F18);
        Assert.Equal((896, 0, 1), (mechPx, mechPy, mechDepth));
        Assert.Equal((384, 508), (mechCx, mechCy));
    }

    [Fact]
    public void VramTexelLookupPerBitDepth()
    {
        var vram = new Vram();

        // 4-bit page at (384, 0) -> tpage = (384 >> 6) = 6; CLUT at (384, 252) -> clut = (252 << 6) | 24
        ushort clut4 = (252 << 6) | 24;
        var pal4 = new byte[32];
        BinaryPrimitives.WriteUInt16LittleEndian(pal4.AsSpan(5 * 2), 0x801F); // index 5 = red (r5=31)
        vram.LoadRect(384, 252, 16, 1, pal4);
        var pix4 = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(pix4, 0x0500); // nibble 2 = index 5
        vram.LoadRect(384, 0, 1, 1, pix4);
        var rgb4 = vram.SampleRgb(0x0006, clut4, 2, 0);
        Assert.Equal((255, 0, 0, false), rgb4);

        // 8-bit page at (896, 0) -> tpage = 0x008E; CLUT at (384, 508) -> clut = 0x7F18
        var pal8 = new byte[512];
        BinaryPrimitives.WriteUInt16LittleEndian(pal8.AsSpan(0xAB * 2), 0x83E0); // index 0xAB = green (g5=31)
        vram.LoadRect(384, 508, 256, 1, pal8);
        var pix8 = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(pix8, 0xAB00); // odd x -> high byte 0xAB
        vram.LoadRect(896, 0, 1, 1, pix8);
        var rgb8 = vram.SampleRgb(0x008E, 0x7F18, 1, 0);
        Assert.Equal((0, 255, 0, false), rgb8);

        // 15-bit direct page at (512, 256) -> tpage = (1 << 8) | (1 << 4) | 8 = 0x0118
        var pix15 = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(pix15, 0xFC00); // blue (b5=31)
        vram.LoadRect(512, 256, 1, 1, pix15);
        var rgb15 = vram.SampleRgb(0x0118, 0, 0, 0);
        Assert.Equal((0, 0, 255, false), rgb15);
    }

    [RetailFact]
    public void RetailMechTextureSamplesNonZero()
    {
        string misPath = TextureGoldenTests.FindRetailMis()!;
        var mis = new MisFile(misPath);
        var bank = TextureBank.FromMisFile(misPath);
        Assert.Equal(10, bank.Resources.Count);

        var mech = mis.Models.First(m => m.Id == 11);
        var rec = mech.Textures.First(t => t.TypeLoaded == 2);
        Assert.True(bank.IsPageLoaded(rec.Tpage));

        // Sample the 4 UV corners of the mech's first textured record; at least one must be non-transparent.
        int[] us =
        {
            (byte)(rec.Uv0 & 0xFF), (byte)((rec.Uv0 >> 16) & 0xFF),
            (byte)(rec.Uv1 & 0xFF), (byte)((rec.Uv1 >> 16) & 0xFF)
        };
        int[] vs =
        {
            (byte)((rec.Uv0 >> 8) & 0xFF), (byte)((rec.Uv0 >> 24) & 0xFF),
            (byte)((rec.Uv1 >> 8) & 0xFF), (byte)((rec.Uv1 >> 24) & 0xFF)
        };

        bool anyNonZero = false;
        for (int i = 0; i < 4; i++)
        {
            var (r, g, b, transparent) = bank.Vram.SampleRgb(rec.Tpage, rec.Clut, us[i], vs[i]);
            if (!transparent && (r != 0 || g != 0 || b != 0))
            {
                anyNonZero = true;
                break;
            }
        }
        Assert.True(anyNonZero, "Expected at least one non-zero texel at mech model 11 UV corners.");
    }
}
