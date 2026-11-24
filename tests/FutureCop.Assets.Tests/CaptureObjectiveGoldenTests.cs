using System.Globalization;
using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Parity test for <see cref="CaptureObjective"/> and <see cref="MatchScore"/> against golden
/// vectors verified by unchanged PS1 C (<c>func_8002D1A0</c>, <c>8002D1A0.c</c>) via
/// <c>tests/host/capture-objective/run.sh</c>.
/// </summary>
public sealed class CaptureObjectiveGoldenTests
{
    private static readonly string[] OutputFields =
    {
        "mode", "outTeam", "score0", "score1", "total0", "total1",
        "p0Cap", "p1Cap", "spCap", "spTimerOut", "e0Out",
    };

    public static IEnumerable<object[]> Rows()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "capture-objective.vectors");
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 1 + 14 + 1 + OutputFields.Length || words[15] != "->")
                throw new FormatException($"Invalid capture-objective vector: {line}");
            yield return new object[]
            {
                words[0],
                Array.ConvertAll(words[1..15], Parse),
                Array.ConvertAll(words[16..], Parse),
            };
        }
    }

    private static int Parse(string word) =>
        int.Parse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    [Theory]
    [MemberData(nameof(Rows))]
    public void MatchesOriginalMips(string name, int[] input, int[] expected)
    {
        int playerCount = input[0];
        int p0Team = input[1];
        int p1Team = input[2];
        int spActive = input[3];
        int spTeam = spActive != 0 ? input[4] : -1;
        int spId = spActive != 0 ? input[5] : -1;
        int otherId = input[6];
        int otherType = input[7];
        int otherTeam = input[8];
        int otherX = input[9];
        int otherY = input[10];
        int otherZ = input[11];
        int spTimer = input[12];
        int sessionE0 = input[13];

        var outpost = new CaptureObjective(112, 0, 0, 0,
            teamA: 1, teamB: 2, markA: 1, markB: 2, stepA: 60, stepB: 0, reachWord: 0x400);
        var score = new MatchScore(p0Team, p1Team, spTeam, spId, playerCount)
        {
            OnSinglePlayerCapture = () => spTimer = 0,
            OnSessionE0Changed = v => sessionE0 = v,
        };
        var field = new ObjectiveField(new[] { outpost }, score);
        field.Touch(new CaptureCandidate(otherId, otherType, otherTeam, otherX, otherY, otherZ, 0));

        int[] actual =
        {
            outpost.CaptureMode, outpost.Team,
            score.Score[0], score.Score[1],
            score.Total[0], score.Total[1],
            score.Captures[0], score.Captures[1],
            score.SinglePlayerCaptures, spTimer, sessionE0,
        };
        var diffs = OutputFields.Select((f, i) => (f, i))
            .Where(x => actual[x.i] != expected[x.i])
            .Select(x => $"{x.f}: expected {expected[x.i]}, got {actual[x.i]}").ToArray();
        Assert.True(diffs.Length == 0, $"{name}: {string.Join(", ", diffs)}");
    }
}
