using System.Globalization;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Pins the case count of every shared weapon/matrix vector file to the number of cases the C host
/// harnesses actually execute in the decomp repo.
/// </summary>
/// <remarks>
/// <para>
/// The harnesses under <c>tests/host/</c> of the decomp repo read the very same files from
/// <c>tests/golden/</c> of this repo, and their QEMU runs report these counts:
/// <c>cadence_test.sh</c> "ALL PASS (cadence parity: 22)", <c>shot_test.sh</c> "ALL PASS (shot-spawn
/// parity: 7)" and "ALL PASS (shot-move parity: 8)", <c>joints_test.sh</c> "joints: all shared vectors
/// PASS (7 cases)". The xUnit suites for those files iterate every row, so a silently truncated or
/// re-saved fixture would weaken both sides at once without any test going red. These assertions make
/// that impossible.
/// </para>
/// <para>
/// <c>compose.vectors</c> (30 cases) and <c>rotation.vectors</c> (141 cases) carry one row per case
/// behind a single TABLE directive; their harnesses print no tally, so the counts are the rows this
/// port executes and the C side consumes identically.
/// </para>
/// </remarks>
public class SharedVectorCaseCountTests
{
    [Theory]
    [InlineData("cadence.vectors", 22)]
    [InlineData("shot.vectors", 7)]
    [InlineData("shot-move.vectors", 8)]
    [InlineData("compose.vectors", 30)]
    [InlineData("rotation.vectors", 141)]
    [InlineData("joints.vectors", 7)]
    public void FixtureHoldsEveryCaseTheCHarnessRuns(string file, int expected)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", file);
        Assert.True(File.Exists(path), $"{file} is not deployed next to the test assembly");

        int cases = CountCases(File.ReadAllLines(path));
        Assert.Equal(expected, cases);
    }

    /// <summary>
    /// Counts the rows a harness would consume: comments and blanks skipped, the <c>TABLE</c> directive
    /// (which carries the animation table, not a case) skipped as well.
    /// </summary>
    private static int CountCases(IEnumerable<string> lines)
    {
        int cases = 0;
        foreach (var raw in lines)
        {
            int comment = raw.IndexOf('#');
            var line = (comment >= 0 ? raw[..comment] : raw).Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("TABLE", StringComparison.Ordinal)) continue;
            // The cadence and shot fixtures label their rows, compose and rotation start with a hex
            // word; a line whose first token is all-uppercase is a directive the harnesses do not
            // read as a case, and would show up as a count mismatch anyway.
            cases++;
        }
        return cases;
    }
}
