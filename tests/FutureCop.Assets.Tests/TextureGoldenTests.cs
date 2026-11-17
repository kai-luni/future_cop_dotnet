using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>One shared golden case, parsed from tests/golden/3dtl.vectors (line form v2).</summary>
/// <param name="Status">
/// Fixture mode: <c>ok</c> = C and .NET assert identical behaviour (OUT/REC apply to both),
/// <c>c_skip</c> = retail behaviour undefined, the C harness skips the case,
/// <c>c_hang</c> = the C harness only proves the retail infinite loop (SIGALRM).
/// In the two non-parity modes the .NET expectations live in the NET/NET_REC lines.
/// </param>
/// <param name="NetStatus">Status the .NET loader must report (the vector status in parity cases).</param>
public sealed record GoldenCase(
    string Id, string Status, string NetStatus, int ErrorOffset, uint[]? Remap, bool RemapZero,
    byte[] Input, byte[]? ExpectedOutput, List<string[]> Records);

public static class GoldenFixture
{
    public static uint[]? RemapTable { get; private set; }

    public static IEnumerable<GoldenCase> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "golden", "3dtl.vectors");
        var cases = new List<GoldenCase>();
        string id = ""; string status = "ok"; string net = ""; int err = -1;
        uint[]? remap = null; bool zero = false;
        byte[] input = Array.Empty<byte>(); byte[]? output = null;
        var recs = new List<string[]>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (t[0])
            {
                case "REMAP":
                    RemapTable = t.Skip(1).Select(x => Convert.ToUInt32(x, 16)).ToArray();
                    break;
                case "CASE":
                    id = t[1]; status = t[2]; net = "";
                    err = status == "ok" && t.Length > 3 ? int.Parse(t[3]) : -1;
                    remap = null; zero = false; input = Array.Empty<byte>(); output = null;
                    recs = new List<string[]>();
                    break;
                case "REMAP_ZERO": zero = true; break;
                case "IN": input = Hex(t.Length > 1 ? t[1] : ""); break;
                case "NET":
                    if (status == "ok") throw new InvalidDataException($"{id}: NET line in a parity case");
                    net = t[1];
                    err = t.Length > 2 ? int.Parse(t[2]) : -1;
                    break;
                case "OUT":
                    RequireParityCase(id, status, t[0]);
                    output = Hex(t.Length > 1 ? t[1] : "");
                    break;
                case "REC":
                    RequireParityCase(id, status, t[0]);
                    recs.Add(t.Skip(1).ToArray());
                    break;
                case "NET_REC":
                    if (status == "ok") throw new InvalidDataException($"{id}: NET_REC line in a parity case");
                    recs.Add(t.Skip(1).ToArray());
                    break;
                case "END":
                    if (status != "ok" && net.Length == 0)
                        throw new InvalidDataException($"{id}: {status} case without a NET line");
                    cases.Add(new GoldenCase(id, status, status == "ok" ? "ok" : net, err,
                        remap, zero, input, output, recs));
                    break;
            }
        }
        return cases;
    }

    private static void RequireParityCase(string id, string status, string directive)
    {
        if (status != "ok")
            throw new InvalidDataException($"{id}: {directive} line in a {status} case (measure it in C or move it to NET/NET_REC)");
    }

    public static byte[] Hex(string s)
    {
        var b = new byte[s.Length / 2];
        for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
        return b;
    }

    public static string ToHex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();
}

/// <summary>
/// Marks a test that needs the extracted retail disc. Absent data reports the test as
/// skipped instead of silently passing; set FC_REQUIRE_RETAIL=1 to make a missing disc fail.
/// </summary>
public sealed class RetailFactAttribute : FactAttribute
{
    public RetailFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL") == "1") return;
        if (TextureGoldenTests.FindRetailMis() == null)
            Skip = "Retail disc copy (build/disc/files/CW/M1A1.MIS) is absent.";
    }
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
        Assert.Equal(c.NetStatus, r.Status.ToString().ToLowerInvariant() switch
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

    /// <summary>Retail disc copy relative to the repository root, or null when it is absent.</summary>
    public static string? FindRetailMis()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("FC_RETAIL_MIS");
        if (!string.IsNullOrEmpty(fromEnv)) return File.Exists(fromEnv) ? fromEnv : null;
        // bin/<Configuration>/<TargetFramework> -> repository root is five levels up.
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        string mis = Path.Combine(root, "build", "disc", "files", "CW", "M1A1.MIS");
        return File.Exists(mis) ? mis : null;
    }

    [RetailFact]
    public void RetailMissionTexturesParseClean()
    {
        string? mis = FindRetailMis();
        Assert.True(mis != null, "Retail disc copy (build/disc/files/CW/M1A1.MIS) is absent.");
        var file = new MisFile(mis!);
        Assert.NotEmpty(file.Models);
        foreach (var m in file.Models)
        {
            var walk = TextureList.Parse(m.TextureBody);
            Assert.Equal(TextureWalkStatus.Ok, walk.Status);
            Assert.Equal(m.Textures.Count, walk.Records.Count);
        }
    }
}
