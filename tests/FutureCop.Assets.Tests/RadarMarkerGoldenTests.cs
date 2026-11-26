using System.Globalization;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Parity test for <see cref="RadarMarkers"/> against the shared golden vectors in
/// <c>tests/golden/radar-markers.vectors</c>, verified by the unchanged PS1 C of 8004CAE4
/// (<c>D_800A6F60</c>), 8004CC68 (<c>gUpdateMarkerPulseColors</c>, the per-frame pulse of
/// <c>D_800A6F60[17..19]</c>), 8005A740 (<c>gEmitAngledLine</c>), 8005A828 (<c>gEmitRadarTriangle</c>),
/// 8005A934 (<c>gCheckRadarTargetBearing</c>) and 8005A9C4 (<c>gDrawRadarMarkers</c>) via
/// <c>tests/host/radar-markers/run.sh</c>. Field order and signedness follow
/// <c>tests/host/radar-markers/harness.c</c>: origins, colours, masks, slot flags, tags and codes are
/// hex; names, angles, world positions, counts and pixel coordinates are signed decimal.
/// </summary>
public sealed class RadarMarkerGoldenTests
{
    private const int ExpectedCases = 157;

    public static IEnumerable<object[]> Rows()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "radar-markers.vectors");
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            int arrow = Array.IndexOf(words, "->");
            if (arrow < 2)
                throw new FormatException($"Invalid radar-markers vector: {line}");
            int inputs = arrow - 1;
            bool shapeOk = words[0] switch
            {
                "COLOR" => inputs == 1 && words.Length == arrow + 2,
                "PULSE" => inputs == 2 && words.Length == arrow + 4,
                "LINE" => inputs == 3 && words.Length == arrow + 7,
                "TRI" => inputs == 4 && words.Length == arrow + 9,
                "BEARING" => inputs == 7 && words.Length == arrow + 3,
                "DRAW" => inputs >= 13,
                _ => throw new FormatException($"Unknown radar-markers directive '{words[0]}': {line}"),
            };
            if (!shapeOk)
                throw new FormatException($"Invalid radar-markers {words[0]} vector: {line}");
            yield return new object[] { words[0], words[1], line };
        }
    }

    [Fact]
    public void FixtureHoldsEveryCaseTheCHarnessRuns()
    {
        var rows = Rows().ToList();
        Assert.Equal(ExpectedCases, rows.Count);
        Assert.Equal(32, rows.Count(r => (string)r[0] == "COLOR"));
        Assert.Equal(74, rows.Count(r => (string)r[0] == "PULSE"));
        Assert.Equal(12, rows.Count(r => (string)r[0] == "LINE"));
        Assert.Equal(12, rows.Count(r => (string)r[0] == "TRI"));
        Assert.Equal(13, rows.Count(r => (string)r[0] == "BEARING"));
        Assert.Equal(14, rows.Count(r => (string)r[0] == "DRAW"));
    }

    /// <summary>
    /// The PULSE block must cover every phase of <c>(gFrameTime &gt;&gt; 3) &amp; 0x3F</c>
    /// (<c>8004CC68.c:36-37</c>) exactly once, so a truncated fixture cannot pass on phases 0..N.
    /// </summary>
    [Fact]
    public void PulseCasesCoverAllSixtyFourPhases()
    {
        var phases = Rows()
            .Where(r => (string)r[0] == "PULSE")
            .Select(r => PulseInput((string)r[2]).FrameTime >> 3 & RadarMarkers.PulsePhaseMask)
            .ToList();
        Assert.Equal(64, phases.Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 64), phases.Distinct().OrderBy(p => p));
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void MatchesOriginalMips(string directive, string name, string line)
    {
        var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int arrow = Array.IndexOf(words, "->");
        var input = new Cursor(words, 2);
        var expected = new Cursor(words, arrow + 1);

        switch (directive)
        {
            case "COLOR":
            {
                // harness.c:313-336 - COLOR idx(dec) -> D_800A6F60[idx](hex).
                int index = Dec(name);
                Assert.Equal(expected.Hex(), unchecked((uint)RadarMarkers.MarkerColorTable[index]));
                break;
            }
            case "PULSE":
            {
                // harness.c PULSE - PULSE name frameTime(dec) -> D_800A6F60[17] D_800A6F60[18]
                // D_800A6F60[19] (hex), read straight from the unmodified func_8004CC68 (8004CC68.c:45-47).
                var (frameTime, pulseName) = PulseInput(line);
                Assert.Equal(name, pulseName);
                var colors = new int[RadarMarkers.PulseColorCount];
                RadarMarkers.MarkerPulseColors(frameTime, colors);
                var table = RadarMarkers.ResolveMarkerColorTable(frameTime);
                for (int i = 0; i < RadarMarkers.PulseColorCount; i++)
                {
                    uint want = expected.Hex();
                    Assert.True(want == unchecked((uint)colors[i]),
                        $"{name} (D_8009FAA4={frameTime}) D_800A6F60[{RadarMarkers.PulseFirstColorIndex + i}]: expected {want:X8} got {colors[i]:X8}");
                    Assert.Equal(unchecked((uint)table[RadarMarkers.PulseFirstColorIndex + i]), want);
                }
                break;
            }
            case "LINE":
            {
                // harness.c:337-382 - LINE name origin(hex) angle(dec) -> tag code x0 y0 x1 y1.
                int origin = (int)input.Hex();
                int angle = input.Dec();
                AssertLine(expected, RadarMarkers.EmitAngledLine(origin, angle), name);
                break;
            }
            case "TRI":
            {
                // harness.c:383-425 - TRI name origin(hex) angle(dec, as u32) colour(hex) -> tag code xy[6].
                int origin = (int)input.Hex();
                uint angle = unchecked((uint)input.Dec());
                int code = (int)input.Hex();
                AssertTriangle(expected, RadarMarkers.EmitRadarTriangle(origin, angle, code), name);
                break;
            }
            case "BEARING":
            {
                // harness.c:426-464 - BEARING name refX refZ heading(s16) targetZ targetX initAngle -> shown angle.
                int refX = input.Dec(), refZ = input.Dec();
                short heading = unchecked((short)input.Dec());
                int targetZ = input.Dec(), targetX = input.Dec();
                int angle = input.Dec();
                bool shown = RadarMarkers.CheckRadarTargetBearing(new RadarReference(refX, refZ, heading), targetZ, targetX, ref angle);
                Assert.Equal(expected.Dec(), shown ? 1 : 0);
                Assert.Equal(expected.Dec(), angle);
                break;
            }
            case "DRAW":
            {
                // harness.c:465-603 - DRAW name refX refZ heading side frameTime playerCount sessionMode
                // otherX otherZ hasNode mask(hex) slotCount {flags(hex) a b}* -> count {L|T tag code xy...}*.
                int refX = input.Dec(), refZ = input.Dec();
                short heading = unchecked((short)input.Dec());
                int side = unchecked((sbyte)input.Dec());
                int frameTime = input.Dec();
                int playerCount = input.Dec();
                int sessionMode = input.Dec();
                int otherX = input.Dec(), otherZ = input.Dec();
                bool hasNode = input.Dec() != 0;
                uint mask = input.Hex();
                int slotCount = input.Dec();
                var slots = new List<RadarMarkerSlot>();
                for (int i = 0; i < slotCount; i++)
                {
                    byte flags = unchecked((byte)input.Hex());
                    short a = unchecked((short)input.Dec());
                    short b = unchecked((short)input.Dec());
                    if (i < RadarMarkers.MaxMarkerSlots) slots.Add(new RadarMarkerSlot(flags, a, b));
                }
                Assert.Equal(arrow, input.Position);

                // The harness points both gPlayerEntities[0] and [1] at (otherX, otherZ) (harness.c:510-515).
                var result = RadarMarkers.DrawRadarMarkers(
                    new RadarReference(refX, refZ, heading),
                    side,
                    frameTime,
                    hasNode ? new RadarMarkerNode(mask, slots) : null,
                    playerCount,
                    sessionMode,
                    new RadarReference(otherX, otherZ, 0));

                var lines = new List<RadarLinePacket> { result.HeadingLine };
                if (result.OtherPlayerBearingLine is { } other) lines.Add(other);

                int count = expected.Dec();
                Assert.Equal(count, lines.Count + result.Triangles.Count);
                int li = 0, ti = 0;
                for (int i = 0; i < count; i++)
                {
                    string kind = expected.Word();
                    if (kind == "L")
                    {
                        Assert.True(ti == 0, $"{name}: line packet {i} after a triangle packet");
                        AssertLine(expected, lines[li++], $"{name} pkt {i}");
                    }
                    else if (kind == "T")
                    {
                        AssertTriangle(expected, result.Triangles[ti++], $"{name} pkt {i}");
                    }
                    else
                    {
                        throw new FormatException($"Unknown DRAW packet kind '{kind}' in {name}");
                    }
                }
                Assert.Equal(words.Length, expected.Position);
                break;
            }
            default:
                throw new FormatException($"Unknown radar-markers directive '{directive}'");
        }
    }

    private static void AssertLine(Cursor expected, RadarLinePacket actual, string name)
    {
        var want = (expected.Hex(), expected.Hex(), expected.Dec(), expected.Dec(), expected.Dec(), expected.Dec());
        var got = (actual.Tag, actual.CodeWord, (int)actual.X0, (int)actual.Y0, (int)actual.X1, (int)actual.Y1);
        Assert.True(want == got, $"{name}: expected {want} got {got}");
    }

    private static void AssertTriangle(Cursor expected, RadarTrianglePacket actual, string name)
    {
        var want = (expected.Hex(), expected.Hex(), expected.Dec(), expected.Dec(), expected.Dec(), expected.Dec(), expected.Dec(), expected.Dec());
        var got = (actual.Tag, actual.CodeWord, (int)actual.X0, (int)actual.Y0, (int)actual.X1, (int)actual.Y1, (int)actual.X2, (int)actual.Y2);
        Assert.True(want == got, $"{name}: expected {want} got {got}");
    }

    private static int Dec(string word) =>
        int.Parse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    /// <summary>Reads the <c>PULSE name frameTime</c> inputs of one PULSE row (<c>harness.c</c> PULSE case).</summary>
    private static (int FrameTime, string Name) PulseInput(string line)
    {
        var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int arrow = Array.IndexOf(words, "->");
        if (words[0] != "PULSE" || arrow != 3)
            throw new FormatException($"Invalid radar-markers PULSE vector: {line}");
        return (Dec(words[2]), words[1]);
    }

    private sealed class Cursor
    {
        private readonly string[] words;

        public Cursor(string[] words, int position)
        {
            this.words = words;
            Position = position;
        }

        public int Position { get; private set; }

        public string Word()
        {
            if (Position >= words.Length)
                throw new FormatException($"Truncated radar-markers vector: {string.Join(' ', words)}");
            return words[Position++];
        }

        public int Dec() => RadarMarkerGoldenTests.Dec(Word());

        public uint Hex() => uint.Parse(Word(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }
}
