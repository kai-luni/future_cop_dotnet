using System.Buffers.Binary;
using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Shared terrain vectors (tests/golden/level.vectors, fcop-level-golden/v1). The same file is
/// run against the unchanged retail units under qemu by level_test.sh; expectations were emitted
/// by that harness from the original code, never from this port. Both sides read the retail
/// Ctil/Cptc payloads from the mission named in the MIS line.
/// </summary>
public class LevelTerrainTests
{
    private sealed record VectorLine(int Line, string Kind, long[] In, long[] Out);

    private static string VectorPath => Path.Combine(AppContext.BaseDirectory, "golden", "level.vectors");

    private static (string Mis, List<VectorLine> Lines) LoadVectors()
    {
        string? mis = null;
        var lines = new List<VectorLine>();
        int n = 0;
        foreach (var raw in File.ReadAllLines(VectorPath))
        {
            n++;
            var text = raw.Split('#')[0].Trim();
            if (text.Length == 0) continue;
            var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens[0] == "MIS") { mis = tokens[1]; continue; }
            int eq = Array.IndexOf(tokens, "=");
            if (eq < 0 && tokens[0] is "TEXPAGES" or "RAMP")
            {
                lines.Add(new VectorLine(n, tokens[0], tokens[1..].Select(Number).ToArray(), Array.Empty<long>()));
                continue;
            }
            Assert.True(eq > 0, $"line {n}: missing '='");
            lines.Add(new VectorLine(n, tokens[0],
                tokens[1..eq].Select(Number).ToArray(), tokens[(eq + 1)..].Select(Number).ToArray()));
        }
        Assert.NotNull(mis);
        return (mis!, lines);
    }

    private static long Number(string t)
    {
        bool neg = t.StartsWith('-');
        if (neg) t = t[1..];
        long v = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(t[2..], 16) : long.Parse(t);
        return neg ? -v : v;
    }

    /// <summary>Retail disc root (build/disc/files) or null; FC_RETAIL_DISC overrides.</summary>
    public static string? FindDisc()
    {
        string? env = Environment.GetEnvironmentVariable("FC_RETAIL_DISC");
        if (!string.IsNullOrEmpty(env)) return Directory.Exists(env) ? env : null;
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string disc = Path.Combine(root, "build", "disc", "files");
        return Directory.Exists(disc) ? disc : null;
    }

    private static LevelMap? LoadMap(string mis)
    {
        string? disc = FindDisc();
        string? path = disc == null ? null : Path.Combine(disc, mis);
        if (path == null || !File.Exists(path))
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
            return null;
        }
        return LevelMap.FromMisFile(path);
    }

    private static uint U(long v) => unchecked((uint)v);

    [Fact]
    public void VectorFileCoversEveryKind()
    {
        var (mis, lines) = LoadVectors();
        Assert.Equal("PA/CONFT.MIS", mis);
        Assert.Equal(1, lines.Count(l => l.Kind == "GRDB"));
        Assert.Equal(35, lines.Count(l => l.Kind == "SECT"));
        Assert.True(lines.Count(l => l.Kind == "TILE") >= 70);
        Assert.True(lines.Count(l => l.Kind == "WINDOW") >= 3);
        Assert.True(lines.Count(l => l.Kind == "GROUND") >= 19);
    }

    [Fact]
    public void SharedVectors()
    {
        var (mis, lines) = LoadVectors();
        var map = LoadMap(mis);
        if (map == null) return;
        int checkedCases = 0, directives = 0;
        uint[] texpages = new uint[10];
        byte[] ramp = new byte[256];
        foreach (var v in lines)
        {
            if (v.Kind == "TEXPAGES")
            {
                Assert.Equal(10, v.In.Length);
                texpages = v.In.Select(U).ToArray();
                directives++;
                continue;
            }
            if (v.Kind == "RAMP")
            {
                Assert.Equal(64, v.In.Length);
                for (int i = 0; i < 64; i++)
                    BinaryPrimitives.WriteUInt32LittleEndian(ramp.AsSpan(i * 4), U(v.In[i]));
                directives++;
                continue;
            }
            long[] got = v.Kind switch
            {
                "GRDB" => Grdb(map),
                "SECT" => Sect(map, (int)v.In[0]),
                "TILE" => Tile(map, v.In, new TerrainRender.EmitContext(texpages, ramp, (ushort)v.In[20])),
                "WINDOW" => Window(map, v.In),
                "GROUND" => Ground(map, v.In),
                "MODPREFIX" => new long[]
                {
                    ModulePath.Prefix(new string(v.In.Select(c => (char)c).ToArray())) == ModulePath.PrecinctAssaultPrefix ? 1 : 0,
                },
                _ => throw new InvalidDataException($"line {v.Line}: unknown kind {v.Kind}"),
            };
            Assert.True(v.Out.Length == got.Length, $"line {v.Line} {v.Kind}: {got.Length} outputs, vector has {v.Out.Length}");
            for (int i = 0; i < got.Length; i++)
                Assert.True(U(v.Out[i]) == U(got[i]),
                    $"line {v.Line} {v.Kind} field {i}: got 0x{U(got[i]):X8}, want 0x{U(v.Out[i]):X8}");
            checkedCases++;
        }
        Assert.Equal(lines.Count, checkedCases + directives);
        Assert.Equal(141, checkedCases);
    }

    [Fact]
    public void VectorTexpagesAreTheRetailCbmpWords()
    {
        var (_, lines) = LoadVectors();
        var line = Assert.Single(lines, l => l.Kind == "TEXPAGES");
        Assert.Equal(TerrainRender.DefaultTexpages(), line.In.Select(U).ToArray());
    }

    private static long[] Grdb(LevelMap map)
    {
        var h = map.Header;
        var r = new List<long> { h.GridOffset };
        r.AddRange(h.Words.Select(w => (long)w));
        r.AddRange(h.WorldClipRect.Select(w => (long)w));
        r.Add(h.CellLimitX); r.Add(h.CellLimitZ);
        r.Add(h.CellMapBytes); r.Add(2);
        r.Add(h.Width - 1); r.Add(h.Height - 1); r.Add(h.Width - 1); r.Add(h.Height - 1);
        return r.ToArray();
    }

    private static long[] Sect(LevelMap map, int id)
    {
        var s = map.Section(id) ?? throw new InvalidDataException($"section {id} missing");
        // Outputs follow the loader order of the vector file: the tile definition count after id.
        return new long[]
        {
            s.ColourOffset, s.DescriptorOffset, s.UvOffset, s.TileSet, 1,
            map.TileDefinitions.CountAfterSection(id), map.TileDefinitions.SlfxFlagAfterSection(id),
        };
    }

    private static Gte.Loaded Rotation(long[] v, int at) =>
        new(Enumerable.Range(0, 9).Select(i => (short)v[at + i]).ToArray(), 0, 0, 0);

    private static long[] Tile(LevelMap map, long[] v, TerrainRender.EmitContext ctx)
    {
        var s = map.Section((int)v[0]) ?? throw new InvalidDataException("section missing");
        var buffer = new byte[TerrainRender.VertexBufferBytes];
        Array.Fill(buffer, (byte)0x5A);
        var gte = new TerrainRender.GteSetup(Rotation(v, 9), (int)v[6], (int)v[7], (int)v[8], (int)v[5]);
        TerrainRender.ProjectGrid(s, buffer, (short)v[1], (short)v[2], (short)v[3], (uint)v[4] << 16, gte);
        int written = 0;
        for (int i = 0; i < 17 * 17 * 3; i++) if (buffer[i * 8 + 7] != 0x5A) written++;
        var packets = new List<uint[]>();
        uint mask = (uint)v[4];
        for (int a = 0xC0; a >= 0; a -= 0x40)
            for (int b = 0xC; b >= 0; b -= 4)
            {
                if ((mask & 1) != 0) TerrainRender.EmitCell(s, buffer, (int)v[18], (int)v[19], (uint)(a + b), ctx, packets);
                mask >>= 1;
            }
        var pool = new byte[packets.Count * 52];
        for (int p = 0; p < packets.Count; p++)
            for (int w = 0; w < 13; w++)
                BinaryPrimitives.WriteUInt32LittleEndian(pool.AsSpan(p * 52 + w * 4), packets[p][w]);
        var r = new List<long> { Fnv(buffer), written, packets.Count, Fnv(pool) };
        r.AddRange(Enumerable.Range(0, 13).Select(w => packets.Count > 0 ? (long)packets[0][w] : 0L));
        return r.ToArray();
    }

    private static long[] Window(LevelMap map, long[] v)
    {
        var entries = CellWindow.Sample(map, (int)v[0], (int)v[1], (int)v[2], (int)v[3], (int)v[4], (int)v[5], Rotation(v, 6));
        var r = new List<long>();
        foreach (var e in entries)
        {
            r.Add(e.Mask);
            if (e.Visible) { r.Add(e.SectionId); r.Add(e.X); r.Add(e.Y); r.Add(e.Z); }
            else { r.Add(-1); r.Add(0x5A5A); r.Add(0x5A5A); r.Add(0x5A5A); }
        }
        return r.ToArray();
    }

    private static long[] Ground(LevelMap map, long[] v)
    {
        int h = LevelGround.Height(map, (int)v[0], (int)v[1], (int)v[2], out int attr);
        return new long[] { h, attr };
    }

    private static uint Fnv(byte[] data)
    {
        uint h = 0x811C9DC5u;
        foreach (byte b in data) { h ^= b; h *= 0x01000193u; }
        return h;
    }

    [Fact]
    public void TerrainTablesMatchRetailExecutable()
    {
        string? disc = FindDisc();
        string? exePath = disc == null ? null : Path.Combine(disc, "SLUS_007.39");
        if (exePath == null || !File.Exists(exePath))
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
            return;
        }
        var exe = File.ReadAllBytes(exePath);
        static int Off(uint a) => (int)(a - 0x80010000u + 0x800);
        // 128 four-byte entries are addressable through (word >> 15) & 0x7F; func_8006531C copies 0x70 shape bytes.
        Assert.Equal(0x200, TerrainTables.VertexIndexBytes.Length);
        Assert.Equal(0x80, TerrainTables.TileShape.Length);
        Assert.Equal(exe.AsSpan(Off(TerrainTables.VertexIndexTableAddress), TerrainTables.VertexIndexBytes.Length).ToArray(),
            TerrainTables.VertexIndexBytes);
        Assert.Equal(exe.AsSpan(Off(TerrainTables.TileShapeTableAddress), TerrainTables.TileShape.Length).ToArray(),
            TerrainTables.TileShape);
        for (int i = 0; i < 5; i++)
        {
            uint ptr = BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(Off(ModulePath.PrecinctAssaultModulesAddress) + i * 4));
            var s = exe.AsSpan(Off(ptr), 16);
            Assert.Equal(ModulePath.PrecinctAssaultModules[i], System.Text.Encoding.ASCII.GetString(s[..s.IndexOf((byte)0)]));
        }
    }

    [Fact]
    public void FirstPrecinctAssaultEntryResolvesToConft()
    {
        // D_8009B72C[0] = 3 -> mission record 3 of D_8009B4E4 names "conft"; with ".mis" (where retail
        // appends it is an open question of docs/precinct-assault-path.md) it is a PA module.
        var record = PrecinctAssaultData.MissionRecords[PrecinctAssaultData.PrecinctAssaultMissionList[0]];
        Assert.Equal("conft", record.Name);
        Assert.Equal("PA\\", ModulePath.Prefix(record.Name + ".mis"));
        Assert.Equal("CW\\", ModulePath.Prefix(record.Name));
        string? disc = FindDisc();
        if (disc == null) { Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL")); return; }
        string? path = ModulePath.Resolve(disc, record.Name + ".mis");
        Assert.NotNull(path);
        Assert.Equal("CONFT.MIS", Path.GetFileName(path));
        Assert.Equal("PA", Path.GetFileName(Path.GetDirectoryName(path)));
    }

    [Fact]
    public void NclipSignFollowsRetailSlti()
    {
        // func_800648BC returns MAC0 < 0; counter-clockwise on screen (y down) is MAC0 > 0.
        int P(int x, int y) => (y << 16) | (x & 0xFFFF);
        Assert.False(TerrainRender.BackFacing(P(0, 0), P(10, 0), P(0, 10)));
        Assert.True(TerrainRender.BackFacing(P(0, 0), P(0, 10), P(10, 0)));
        Assert.False(TerrainRender.BackFacing(P(0, 0), P(10, 10), P(20, 20)));
    }
}
