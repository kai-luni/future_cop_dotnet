using System.Globalization;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Compares <see cref="HudDraw"/> against <c>golden/hud-draw.vectors</c>. The vectors are produced by
/// running the original <c>func_80047F30</c> plus <c>func_8001C214</c> from the decompilation in a
/// host harness whose drawing primitives only record their arguments, so the expected call list comes
/// from the retail code and not from this port.
/// </summary>
public sealed class HudDrawVectorTests
{
    private static string VectorPath() =>
        Path.Combine(AppContext.BaseDirectory, "golden", "hud-draw.vectors");

    private sealed record Case(string Name, Dictionary<string, int> State, List<string> Calls);

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
                var state = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var token in parts.Skip(2))
                {
                    int eq = token.IndexOf('=');
                    state[token[..eq]] = int.Parse(token[(eq + 1)..], CultureInfo.InvariantCulture);
                }
                current = new Case(parts[1], state, new List<string>());
                cases.Add(current);
                continue;
            }
            if (line.StartsWith("  ", StringComparison.Ordinal))
            {
                if (current is null) throw new InvalidDataException("call before first CASE");
                current.Calls.Add(line[2..]);
            }
        }
        return cases;
    }

    private static int Get(Dictionary<string, int> s, string key, int fallback = 0) =>
        s.TryGetValue(key, out int value) ? value : fallback;

    private static (HudGameBlock, HudSession, HudGlobals) Build(Dictionary<string, int> s)
    {
        var player = new HudPlayerBlock
        {
            Value58 = Get(s, "w58"),
            Value5C = Get(s, "w5c"),
            Value64 = Get(s, "w64"),
            ValueA4 = Get(s, "a4"),
            ValueAe = Get(s, "ae"),
            ValueC1 = Get(s, "c1"),
            Value326 = Get(s, "w326"),
        };
        var game = new HudGameBlock
        {
            Health = Get(s, "health"),
            MaxHealth = Get(s, "maxhealth"),
            Player = player,
        };
        var session = new HudSession
        {
            Stage = Get(s, "stage"),
            Mode = Get(s, "mode"),
            Score0 = Get(s, "score0"),
            Score1 = Get(s, "score1"),
            Count1 = Get(s, "count1"),
            Count2 = Get(s, "count2"),
            Count3 = Get(s, "count3"),
            Count4 = Get(s, "count4"),
            Total1 = Get(s, "total1"),
            Total2 = Get(s, "total2"),
        };
        HudGameBlock? second = null;
        if (s.ContainsKey("second"))
        {
            second = new HudGameBlock
            {
                Health = Get(s, "second"),
                MaxHealth = Get(s, "secondmax"),
                Extra = new HudExtraBlock { A4 = Get(s, "extraa4"), A8 = Get(s, "extraa8") },
            };
        }
        var globals = new HudGlobals
        {
            ScreenState = Get(s, "screen", 4),
            PlayerCount = Get(s, "players", 1),
            FrameTime = Get(s, "frame"),
            SecondGame = second,
        };
        return (game, session, globals);
    }

    [Fact]
    public void Matches_the_retail_call_sequence()
    {
        var cases = Load();
        Assert.NotEmpty(cases);

        var failures = new List<string>();
        foreach (var testCase in cases)
        {
            var (game, session, globals) = Build(testCase.State);
            var recorder = new Recorder();
            HudDraw.Draw(recorder, game, session, globals);

            int count = Math.Max(recorder.Calls.Count, testCase.Calls.Count);
            for (int i = 0; i < count; i++)
            {
                string expected = i < testCase.Calls.Count ? testCase.Calls[i] : "<none>";
                string actual = i < recorder.Calls.Count ? recorder.Calls[i] : "<none>";
                if (expected != actual)
                    failures.Add($"{testCase.Name} call {i}: expected \"{expected}\", got \"{actual}\"");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>An <see cref="IHudDevice"/> that formats each call the way the harness records it.</summary>
    private sealed class Recorder : IHudDevice
    {
        public List<string> Calls { get; } = new();

        public void Marker() => Calls.Add("MARKER");
        public void SlotsBar() => Calls.Add("SLOTSBAR");
        public void TargetPass() => Calls.Add("TARGETPASS");
        public void Markers() => Calls.Add("MARKERS");

        public void Bar(int x, int y, int intensity, int p3, int p4, int p5, int flag) =>
            Calls.Add($"BAR {x} {y} {intensity} {p3} {p4} {p5} {flag}");

        /// <summary>
        /// The harness records what <c>func_8001C214</c> hands to the tile emitter: the widened colour
        /// channels and the rectangle halved by an arithmetic shift. This does the same, so the numbers
        /// are comparable.
        /// </summary>
        public void Rect(uint colour, int x, int y, int w, int h, int blend)
        {
            int red = (int)((colour >> 8) & 0x7C);
            int green = (int)((colour >> 3) & 0x7C);
            int blue = (int)((colour & 0x1F) << 2);
            Calls.Add($"RECT {red} {green} {blue} x={x >> 1} y={y >> 1} w={w >> 1} h={h >> 1} blend={blend}");
        }

        public void Text(string text, int x, int y, int colour) =>
            Calls.Add($"TEXT \"{text}\" {x} {y} {colour}");

        /// <summary>
        /// The harness assumes an 8 pixel advance per glyph, because the retail advances live in the
        /// glyph atlas that is not decoded. Label centring is therefore checked against that assumption.
        /// </summary>
        public int TextWidth(string text) => text.Length * 8 * 2;
    }
}
