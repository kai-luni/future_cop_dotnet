using System.Text;
using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Compares PrecinctAssaultData with the retail executable. Set FUTURECOP_SLUS to the
/// SLUS_007.39 path; otherwise the sibling decomp checkout is tried, and the
/// executable-backed checks return without comparison when neither exists, unless
/// FC_REQUIRE_RETAIL=1 requires the executable.
/// </summary>
public class PrecinctAssaultDataTests
{
    private static byte[]? LoadSlus()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("FUTURECOP_SLUS"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "git", "future-cop-lapd-ps1", "baseroms", "us", "SLUS_007.39"),
        };
        foreach (var c in candidates)
            if (!string.IsNullOrEmpty(c) && File.Exists(c)) return File.ReadAllBytes(c);
        return null;
    }

    private static ReadOnlySpan<byte> At(byte[] exe, uint address, int length) =>
        exe.AsSpan((int)(address - 0x80010000u + 0x800), length);

    private static string? CString(byte[] exe, uint pointer)
    {
        if (pointer == 0) return null;
        var s = At(exe, pointer, 64);
        return Encoding.ASCII.GetString(s[..s.IndexOf((byte)0)]);
    }

    [Fact]
    public void TablesMatchRetailExecutable()
    {
        var exe = LoadSlus();
        if (exe == null)
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
            return;
        }
        Assert.Equal("PS-X EXE", Encoding.ASCII.GetString(exe, 0, 8));
        Assert.Equal(PrecinctAssaultData.CrimeWarMissionList.Select(b => (byte)b),
            At(exe, PrecinctAssaultData.CrimeWarMissionListAddress, 8).ToArray());
        Assert.Equal(PrecinctAssaultData.PrecinctAssaultMissionList.Select(b => (byte)b),
            At(exe, PrecinctAssaultData.PrecinctAssaultMissionListAddress, 5).ToArray());
        Assert.Equal(PrecinctAssaultData.LoadoutRows, At(exe, PrecinctAssaultData.LoadoutRowsAddress, 0x78).ToArray());
        Assert.Equal(PrecinctAssaultData.MultiBrfMovieName, CString(exe, 0x80012094));
        Assert.Equal(PrecinctAssaultData.Lax1ModuleName, CString(exe, 0x8009F444));
        Assert.Equal(PrecinctAssaultData.Lax2ModuleName, CString(exe, 0x8009F488));
        Assert.Equal(PrecinctAssaultData.InitialModuleName, CString(exe, 0x8009B300));
        Assert.Equal(PrecinctAssaultData.InitialLoadScreen, At(exe, 0x8009F3F0, 1)[0]);
        for (int i = 0; i < 3; i++)
        {
            var e = At(exe, PrecinctAssaultData.LoadoutTableAddress + (uint)(i * 0x14), 0x14);
            var t = PrecinctAssaultData.InitialLoadoutTable[i];
            Assert.Equal(t.Items, BitConverter.ToUInt32(e));
            Assert.Equal((t.Count, t.Min, t.Max), (e[8], e[9], e[10]));
        }
        foreach (var r in PrecinctAssaultData.MissionRecords)
        {
            var raw = At(exe, PrecinctAssaultData.MissionRecordTableAddress + (uint)(r.Index * 36), 36).ToArray();
            Assert.Equal(raw, r.Raw);
            Assert.Equal(CString(exe, BitConverter.ToUInt32(raw, 0)) ?? "", r.Name);
            Assert.Equal(CString(exe, BitConverter.ToUInt32(raw, 4)), r.BriefingMovie);
            Assert.Equal(CString(exe, BitConverter.ToUInt32(raw, 8)), r.ReelMovie);
        }
    }

    [Fact]
    public void PrecinctListNamesTheFiveMaps()
    {
        var names = PrecinctAssaultData.PrecinctAssaultMissionList
            .Select(i => PrecinctAssaultData.MissionRecords[i].Name).ToArray();
        Assert.Equal(new[] { "conft", "slim", "hk", "ovmp", "joke" }, names);
    }
}
