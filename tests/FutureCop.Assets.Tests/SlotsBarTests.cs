using System.Globalization;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Compares <see cref="SlotsBarDraw"/> against <c>golden/slots-bar.vectors</c>. The vectors come
/// from running the original <c>func_8006C080</c>, <c>func_8006BF44</c> and <c>func_800491F8</c>
/// in a host harness (<c>tests/host/slots/h.c</c>), so the expected registry lookups, text strings
/// and primitive packets come from the retail code and not from this port.
/// </summary>
public sealed class SlotsBarTests
{
    private const uint HarnessEnvMapTexpageWord = 0x7C00u | (0x1234u << 16);

    private static string VectorPath() =>
        Path.Combine(AppContext.BaseDirectory, "golden", "slots-bar.vectors");

    private sealed record Case(string Name, Dictionary<string, string> State, List<string> Calls);

    private static List<Case> Load()
    {
        var cases = new List<Case>();
        Case? current = null;
        foreach (var raw in File.ReadAllLines(VectorPath()))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("CASE ", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var state = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var token in parts.Skip(1))
                {
                    int eq = token.IndexOf('=');
                    state[token[..eq]] = token[(eq + 1)..];
                }
                current = new Case(line, state, new List<string>());
                cases.Add(current);
                continue;
            }
            if (current is null) throw new InvalidDataException("call before first CASE");
            current.Calls.Add(line);
        }
        return cases;
    }

    private static int ParseInt(string value) =>
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(value, CultureInfo.InvariantCulture);

    private static int[] ParseIntArray(string value) =>
        value.Split(',').Select(ParseInt).ToArray();

    private static (HudPlayerBlock Player, HudGlobals Globals) Build(Dictionary<string, string> s)
    {
        int c1 = ParseInt(s["c1"]);
        int frame = ParseInt(s["frame"]);
        int inv = ParseInt(s["inv"]);
        int second = ParseInt(s["second"]);
        var player = new HudPlayerBlock
        {
            Value84 = ParseIntArray(s["counts84"]),
            Value8A = ParseIntArray(s["counts8A"]),
            Value90 = ParseIntArray(s["ids"]),
            ValueC1 = c1,
        };
        var invulnerable = new byte[Math.Max(2, (c1 & 0x3F) + 1)];
        if (inv != 0) invulnerable[c1 & 0x3F] = 1;
        var globals = new HudGlobals
        {
            FrameTime = frame,
            PlayerInvulnerable = invulnerable,
            EnvMapTexpageWord = HarnessEnvMapTexpageWord,
            SecondGame = second != 0 ? new HudGameBlock() : null,
        };
        return (player, globals);
    }

    [Fact]
    public void Matches_the_retail_call_sequence()
    {
        var cases = Load();
        Assert.NotEmpty(cases);

        var failures = new List<string>();
        foreach (var testCase in cases)
        {
            var (player, globals) = Build(testCase.State);
            var recorder = new Recorder();
            SlotsBarDraw.Draw(recorder, recorder, recorder, player, globals);

            var actualLines = recorder.AllLines();
            int count = Math.Max(actualLines.Count, testCase.Calls.Count);
            for (int i = 0; i < count; i++)
            {
                string expected = i < testCase.Calls.Count ? testCase.Calls[i] : "<none>";
                string actual = i < actualLines.Count ? actualLines[i] : "<none>";
                if (expected != actual)
                    failures.Add($"{testCase.Name} line {i}: expected \"{expected}\", got \"{actual}\"");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void HudDraw_forwards_slot_state_to_SlotsBar_in_sequence()
    {
        var player = new HudPlayerBlock
        {
            Value64 = 2,
            Value84 = new[] { 1, 2, 3 },
            Value8A = new[] { 4, 5, 6 },
            Value90 = new[] { 7, 8, 9 },
            ValueC1 = 1,
        };
        var game = new HudGameBlock { Health = 100, MaxHealth = 100, Player = player };
        var session = new HudSession { Mode = 0 };
        var globals = new HudGlobals
        {
            FrameTime = 0x40,
            PlayerInvulnerable = new byte[] { 0, 1 },
        };

        var spy = new SequenceSpy();
        HudDraw.Draw(spy, game, session, globals);

        Assert.Equal(new[] { "MARKER", "SLOTSBAR", "BAR", "TARGETPASS", "MARKERS" }, spy.Calls);
        Assert.Same(player, spy.PassedPlayer);
        Assert.Same(globals, spy.PassedGlobals);
    }

    [Fact]
    public void InGameHud_paints_slot_blocks_and_counts()
    {
        var rgb = new byte[LevelScene.Width * LevelScene.Height * 3];
        var player = new HudPlayerBlock
        {
            Value64 = 2,
            Value8A = new[] { 10, 0, 0 },
        };
        var game = new HudGameBlock { Health = 100, MaxHealth = 100, Player = player };
        var session = new HudSession { Mode = 0 };
        var globals = new HudGlobals
        {
            FrameTime = 0x40,
            SecondGame = new HudGameBlock(),
        };

        InGameHud.Paint(rgb, game, session, globals);

        Assert.Equal((0xC0, 0xA0, 0xA0), Pixel(rgb, 34, 208));
        Assert.Equal((0xC0, 0xA0, 0xA0), Pixel(rgb, 49, 223));
        Assert.Equal((0x60, 0x60, 0x60), Pixel(rgb, 75, 208));
        Assert.Equal((0x60, 0x60, 0x60), Pixel(rgb, 116, 208));
        Assert.Equal((0x60, 0x60, 0x60), Pixel(rgb, 12, 208));
        Assert.Equal((0x60, 0x60, 0x60), Pixel(rgb, 292, 208));

        var withoutSecond = new byte[LevelScene.Width * LevelScene.Height * 3];
        InGameHud.Paint(withoutSecond, game, session, globals with { SecondGame = null });
        Assert.Equal((0, 0, 0), Pixel(withoutSecond, 292, 208));
    }

    /// <summary>
    /// A PS1 long is 32 bits wide, so "%ld" is an ordinary signed conversion; the reference harness
    /// prints -1 for a negative count and the vectors pin that in their final case.
    /// </summary>
    [Fact]
    public void FormatSlotCount_is_a_signed_32_bit_ld_conversion()
    {
        Assert.Equal("0", HudMath.FormatSlotCount(0));
        Assert.Equal("100", HudMath.FormatSlotCount(100));
        Assert.Equal("-1", HudMath.FormatSlotCount(-1));
    }

    /// <summary>
    /// The slot icons of <c>func_8006BF44</c> are real sprites in the mission's VRAM, not solid
    /// blocks: <c>gEmitStatusSlotIcon</c> emits the page word of <c>D_800A8EA4</c> as a <c>0xE1</c>
    /// packet and then a 16x16 <c>0x66</c> SPRT whose CLUT comes from the upper half of that word
    /// (8006BF44.c:62-64,92-97). With the mission's VRAM the three boxes must therefore carry more
    /// than one colour each - the flat block was the bug.
    /// </summary>
    /// <remarks>
    /// Guarded on the retail archive like the other disc-backed tests: without CONFT.MIS there is no
    /// VRAM to sample, and the fallback path is what the case above pins.
    /// </remarks>
    [Fact]
    public void InGameHud_textures_the_slot_icons_from_the_mission_vram()
    {
        string? mis = MissionPath();
        if (mis == null) return;

        var scene = new LevelScene(mis);
        Assert.NotEqual(0u, scene.HudTexpageWord);

        var player = new HudPlayerBlock
        {
            Value64 = 2,
            Value8A = new[] { 7500, 181, 31 },
            Value90 = new[] { 0x00, 0x10, 0x20 },
        };
        var game = new HudGameBlock { Health = 100, MaxHealth = 100, Player = player };
        var session = new HudSession { Mode = 0 };
        var globals = new HudGlobals
        {
            FrameTime = 0x40, PlayerCount = 1,
            EnvMapTexpageWord = scene.HudTexpageWord,
            PlayerInvulnerable = new byte[] { 0, 0 },
        };

        var textured = new byte[LevelScene.Width * LevelScene.Height * 3];
        InGameHud.Paint(textured, game, session, globals, scene.Vram);
        var flat = new byte[LevelScene.Width * LevelScene.Height * 3];
        InGameHud.Paint(flat, game, session, globals);

        // spriteX = 0x22 + 0x29 * i, y = 0xE0 - 16 (func_8006C080:77-101, func_8006BF44:88).
        for (int i = 0; i < 3; i++)
        {
            int bx = 0x22 + i * 0x29;
            Assert.Equal(1, DistinctColours(flat, bx, 208));
            Assert.True(DistinctColours(textured, bx, 208) > 8,
                $"slot {i}: expected a textured icon, got {DistinctColours(textured, bx, 208)} colours");
        }

        // Nothing outside the four 16x16 boxes may move: three slot icons at spriteX = 0x22 + 0x29 * i
        // and the invulnerability icon at x = 0x0C, all at y = 0xE0 - 16 (func_8006C080:77-106).
        bool InSprite(int x, int y)
        {
            if (y < 208 || y >= 224) return false;
            for (int i = 0; i < 3; i++)
            {
                int bx = 0x22 + i * 0x29;
                if (x >= bx && x < bx + 16) return true;
            }
            return x >= 0x0C && x < 0x0C + 16;
        }
        int outside = 0;
        for (int y = 0; y < LevelScene.Height; y++)
            for (int x = 0; x < LevelScene.Width; x++)
            {
                if (InSprite(x, y)) continue;
                int o = (y * LevelScene.Width + x) * 3;
                if (flat[o] != textured[o] || flat[o + 1] != textured[o + 1] || flat[o + 2] != textured[o + 2])
                    outside++;
            }
        Assert.Equal(0, outside);
    }

    /// <summary>
    /// Without a VRAM the sink keeps the previous behaviour instead of drawing nothing: the 0x66
    /// packet still fills its 16x16 box with the packet colour.
    /// </summary>
    [Fact]
    public void Slot_icons_fall_back_to_the_solid_block_without_vram()
    {
        var rgb = new byte[LevelScene.Width * LevelScene.Height * 3];
        var player = new HudPlayerBlock { Value64 = 2, Value8A = new[] { 1, 2, 3 }, Value90 = new[] { 0, 0x10, 0x20 } };
        var game = new HudGameBlock { Health = 100, MaxHealth = 100, Player = player };
        var globals = new HudGlobals
        {
            FrameTime = 0x40, EnvMapTexpageWord = 0x7F58009E, PlayerInvulnerable = new byte[] { 0, 0 },
        };

        InGameHud.Paint(rgb, game, new HudSession { Mode = 0 }, globals, null);

        Assert.Equal(1, DistinctColours(rgb, 0x22, 208));
        Assert.Equal((0xC0, 0xA0, 0xA0), Pixel(rgb, 0x22, 208));
    }

    /// <summary>Colours inside a 16x16 sprite box, read from the 320x240 frame.</summary>
    private static int DistinctColours(byte[] rgb, int x, int y)
    {
        var seen = new HashSet<(byte, byte, byte)>();
        for (int row = 0; row < 16; row++)
            for (int col = 0; col < 16; col++)
            {
                int o = ((y + row) * LevelScene.Width + x + col) * 3;
                seen.Add((rgb[o], rgb[o + 1], rgb[o + 2]));
            }
        return seen.Count;
    }

    /// <summary>The mission the disc-backed cases read, or null when the archive is not checked out.</summary>
    private static string? MissionPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "build", "disc", "files", "PA", "CONFT.MIS");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static (byte R, byte G, byte B) Pixel(byte[] rgb, int x, int y)
    {
        int offset = (y * LevelScene.Width + x) * 3;
        return (rgb[offset], rgb[offset + 1], rgb[offset + 2]);
    }

    private sealed class SequenceSpy : IHudDevice
    {
        public List<string> Calls { get; } = new();
        public HudPlayerBlock? PassedPlayer { get; private set; }
        public HudGlobals? PassedGlobals { get; private set; }

        public void Marker() => Calls.Add("MARKER");
        public void SlotsBar(HudPlayerBlock player, HudGlobals globals)
        {
            PassedPlayer = player;
            PassedGlobals = globals;
            Calls.Add("SLOTSBAR");
        }
        public void Bar(int x, int y, int intensity, int p3, int p4, int p5, int flag) => Calls.Add("BAR");
        public void TargetPass() => Calls.Add("TARGETPASS");
        public void Markers() => Calls.Add("MARKERS");
        public void Rect(uint colour, int x, int y, int w, int h, int blend) => Calls.Add("RECT");
        public void Text(string text, int x, int y, int colour) => Calls.Add("TEXT");
        public int TextWidth(string text) => 0;
    }

    /// <summary>Records registry lookups, text draws and emitted packets in the exact format of <c>h.c</c>.</summary>
    private sealed class Recorder : IHudDevice, ISlotRegistry, ISlotPacketSink
    {
        private readonly List<string> _calls = new();
        private readonly List<SlotPacket> _packets = new();

        public SlotRegistryEntry Find(byte id)
        {
            _calls.Add($"REGISTRY {id}");
            return new SlotRegistryEntry(id, Found: true);
        }

        public void Text(string text, int x, int y, int colour) =>
            _calls.Add($"TEXT \"{text}\" {x} {y} {colour}");

        public void Emit(SlotPacket packet) => _packets.Add(packet);

        public IReadOnlyList<string> AllLines()
        {
            var lines = new List<string>(_calls.Count + _packets.Count);
            lines.AddRange(_calls);
            for (int i = 0; i < _packets.Count; i++)
            {
                var p = _packets[i];
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "  packet{0} code={1:X2} len={2} rgb={3:X2}{4:X2}{5:X2} xy={6},{7} uv={8},{9} clut={10:X4} wh={11},{12} addr={13}",
                    i, p.Code, p.Len, p.R, p.G, p.B, p.X, p.Y, p.U, p.V, p.Clut, p.W, p.H, p.Addr));
            }
            return lines;
        }

        public void Marker() => throw new NotSupportedException();
        public void SlotsBar(HudPlayerBlock player, HudGlobals globals) => throw new NotSupportedException();
        public void Bar(int x, int y, int intensity, int p3, int p4, int p5, int flag) => throw new NotSupportedException();
        public void TargetPass() => throw new NotSupportedException();
        public void Markers() => throw new NotSupportedException();
        public void Rect(uint colour, int x, int y, int w, int h, int blend) => throw new NotSupportedException();
        public int TextWidth(string text) => throw new NotSupportedException();
    }
}
