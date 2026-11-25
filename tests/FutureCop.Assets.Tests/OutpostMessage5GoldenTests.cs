using System.Globalization;
using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Parity test for <see cref="MatchScore.RecordOutpostMessage5"/> against golden vectors verified by
/// unchanged PS1 C (<c>func_8002D664</c> message 5, <c>8002D664.c:179-225</c>) via
/// <c>tests/host/outpost-message5/run.sh</c>.
/// </summary>
public sealed class OutpostMessage5GoldenTests
{
    private const int InputCount = 15;

    private static readonly string[] OutputFields =
    {
        "score0", "score1", "total0", "total1", "p0Count10", "p1Count10",
        "stCount38", "stTimer18", "e0Out", "lookupKey",
    };

    public static IEnumerable<object[]> Rows()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "outpost-message5.vectors");
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 1 + InputCount + 1 + OutputFields.Length || words[1 + InputCount] != "->")
                throw new FormatException($"Invalid outpost-message5 vector: {line}");
            yield return new object[]
            {
                words[0],
                Array.ConvertAll(words[1..(1 + InputCount)], Parse),
                Array.ConvertAll(words[(2 + InputCount)..], Parse),
            };
        }
    }

    private static int Parse(string word) =>
        int.Parse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    [Fact]
    public void FixtureHoldsEveryCaseTheCHarnessRuns() => Assert.Equal(15, Rows().Count());

    [Theory]
    [MemberData(nameof(Rows))]
    public void MatchesOriginalMips(string name, int[] input, int[] expected)
    {
        int playerCount = input[0];
        int p0Team = input[1], p0Index = input[2];
        int p1Team = input[3], p1Index = input[4];
        bool spActive = input[5] != 0;
        int spTeam = spActive ? input[6] : -1;
        int spId = spActive ? input[7] : -1;
        int fe = input[8];
        bool excluded = input[9] != 0;
        uint word8C = (uint)input[10];
        bool found = input[11] != 0;
        int foundId = input[12];
        int timer = input[13];
        int sessionE0 = input[14];

        // F6 = 1, F7 = 2 as in the harness; the latch FE comes from a claim by that team.
        var outpost = new CaptureObjective(112, 0, 0, 0, teamA: 1, teamB: 2, markA: 1, markB: 2,
            stepA: 60, stepB: 0, reachWord: 0x400, flags: excluded ? 0x4000001u : 1u);
        if (fe != 0)
            Assert.Equal(fe, outpost.TryCapture(new CaptureCandidate(50, 5, fe, 0, 0, 0, 0)));

        var score = new MatchScore(p0Team, p1Team, spTeam, spId, playerCount)
        {
            OnSinglePlayerCapture = () => timer = 0,
            OnSessionE0Changed = v => sessionE0 = v,
        };
        score.PlayerScoreIndices[0] = p0Index >= 0 ? p0Index : null;
        score.PlayerScoreIndices[1] = p1Index >= 0 ? p1Index : null;
        int lookupKey = -1;

        score.RecordOutpostMessage5(outpost, word8C, key =>
        {
            lookupKey = key;
            return found ? foundId : null;
        });

        int[] actual =
        {
            score.Score[0], score.Score[1], score.Total[0], score.Total[1],
            score.OutpostCaptures[0], score.OutpostCaptures[1],
            score.OutpostStateCount, timer, sessionE0, lookupKey,
        };
        var diffs = OutputFields.Select((f, i) => (f, i))
            .Where(x => actual[x.i] != expected[x.i])
            .Select(x => $"{x.f}: expected {expected[x.i]}, got {actual[x.i]}").ToArray();
        Assert.True(diffs.Length == 0, $"{name}: {string.Join(", ", diffs)}");
    }
}
