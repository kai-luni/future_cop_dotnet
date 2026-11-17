using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>One shared golden case, parsed from tests/golden/3dtl.vectors (line form v1).</summary>
public sealed record GoldenCase(
    string Id, string Status, int ErrorOffset, uint[]? Remap, bool RemapZero,
    byte[] Input, byte[]? ExpectedOutput, List<string[]> Records);

public static class GoldenFixture
{
    public static uint[]? RemapTable { get; private set; }

    public static IEnumerable<GoldenCase> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "golden", "3dtl.vectors");
        var cases = new List<GoldenCase>();
        string id = ""; string status = "ok"; int err = -1;
        uint[]? remap = null; bool zero = false;
        byte[] input = Array.Empty<byte>(); byte[]? output = null;
        var recs = new List<string[]>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (t[0])
            {
                case "REMAP":
                    RemapTable = t.Skip(1).Select(x => Convert.ToUInt32(x, 16)).ToArray();
                    break;
                case "CASE":
                    id = t[1]; status = t[2]; err = t.Length > 3 ? int.Parse(t[3]) : -1;
                    remap = null; zero = false; input = Array.Empty<byte>(); output = null;
                    recs = new List<string[]>();
                    break;
                case "REMAP_ZERO": zero = true; break;
                case "IN": input = Hex(t.Length > 1 ? t[1] : ""); break;
                case "OUT": output = Hex(t.Length > 1 ? t[1] : ""); break;
                case "REC": recs.Add(t.Skip(1).ToArray()); break;
                case "END":
                    cases.Add(new GoldenCase(id, status, err, remap, zero, input, output, recs));
                    break;
            }
        }
        return cases;
    }

    public static byte[] Hex(string s)
    {
        var b = new byte[s.Length / 2];
        for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
        return b;
    }

    public static string ToHex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();
}

public class TextureGoldenTests
{
    public static IEnumerable<object[]> Cases() =>
        GoldenFixture.Load().Where(c => c.Id != "remap_table").Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(Cases))]
    public void SharedVector(GoldenCase c)
    {
        uint[]? remap = c.RemapZero ? new uint[10] : (c.Remap ?? GoldenFixture.RemapTable);
        var r = TextureList.Parse(c.Input, remap);
        Assert.Equal(c.Status, r.Status.ToString().ToLowerInvariant() switch
        {
            "ok" => "ok",
            "unknowntype" => "unknown_type",
            "indexoutofrange" => "index_out_of_range",
            _ => r.Status.ToString(),
        });
        if (c.ErrorOffset >= 0) Assert.Equal(c.ErrorOffset, r.ErrorOffset);
        if (c.ExpectedOutput != null) Assert.Equal(GoldenFixture.ToHex(c.ExpectedOutput), GoldenFixture.ToHex(r.Output));
        Assert.Equal(c.Records.Count, r.Records.Count);
        for (int i = 0; i < c.Records.Count; i++)
        {
            var e = c.Records[i];
            var a = r.Records[i];
            Assert.Equal(int.Parse(e[0]), a.Offset);
            Assert.Equal(int.Parse(e[1]), a.Size);
            Assert.Equal(byte.Parse(e[2]), a.TypeRaw);
            Assert.Equal(byte.Parse(e[3]), a.TypeLoaded);
            Assert.Equal(Convert.ToUInt32(e[4], 16), a.Rgb);
            Assert.Equal(Convert.ToUInt32(e[5], 16), a.Uv0);
            Assert.Equal(Convert.ToUInt32(e[6], 16), a.Uv1);
            Assert.Equal(int.Parse(e[7]), a.TexIndex);
            Assert.Equal(Convert.ToUInt16(e[8], 16), a.Clut);
            Assert.Equal(Convert.ToUInt16(e[9], 16), a.Tpage);
            Assert.Equal(e[10] == "-" ? null : (int?)int.Parse(e[10]), a.SemiTransMode);
        }
    }

    [Fact]
    public void RemapTableMatchesRetailBuilder()
    {
        var expected = GoldenFixture.RemapTable;
        Assert.NotNull(expected);
        Assert.Equal(expected, TextureList.DefaultRemap());
    }

    [Fact]
    public void RetailMissionTexturesParseClean()
    {
        // Retail assets are gitignored; skip when the local disc copy is absent.
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        string mis = Path.Combine(root, "build", "disc", "files", "CW", "M1A1.MIS");
        if (!File.Exists(mis)) return;
        var file = new MisFile(mis);
        Assert.NotEmpty(file.Models);
        foreach (var m in file.Models)
        {
            var walk = TextureList.Parse(m.TextureBody);
            Assert.Equal(TextureWalkStatus.Ok, walk.Status);
            Assert.Equal(m.Textures.Count, walk.Records.Count);
        }
    }
}