using FutureCop.Game;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The mission side of Precinct Assault: the entity records of CONFT.MIS (Cact/Csac requests read
/// through the gInitEntityBase header), the outpost capture gTryCaptureObjective (func_8002D1A0) and
/// the score words it raises. Synthetic expectations are computed from the cited C lines; retail ones
/// are the bytes of CONFT.MIS at the cited offsets.
/// </summary>
public class CaptureObjectiveTests
{
    // ---- octagonal distance, func_8002D1A0:144-156 ----------------------------------------------

    [Theory]
    // Small side not above half the large one: big + (small >> 2) - (small >> 6).
    [InlineData(100, 0, 100)]
    [InlineData(1000, 400, 1000 + 100 - 6)]
    [InlineData(-400, 1000, 1000 + 100 - 6)]
    // Blend branch: sum = (s << 6) + (s << 3) + (s << 1) + big + (small << 5), big = sum >> 7,
    // small = big - small. (3,4): s = 7 -> 618 >> 7 = 4, small 1 -> 4.
    [InlineData(3, 4, 4)]
    // (1000,1000): s = 2000 -> 148000 + 1000 + 32000 = 181000 >> 7 = 1414, small 0 -> 1414.
    [InlineData(1000, 1000, 1414)]
    public void OctagonalDistanceFollowsTheShiftChain(int a, int b, int expected) =>
        Assert.Equal(expected, CaptureObjective.OctagonalDistance(a, b));

    // ---- capture, func_8002D1A0:125-200 ----------------------------------------------------------

    /// <summary>The retail outpost parameters (st+0x48..+0x4F of every CONFT type-0x24 record).</summary>
    private static CaptureObjective Outpost(int value92 = 0) => new(112, 0, 0, 0,
        teamA: 1, teamB: 2, markA: 1, markB: 2, stepA: 60, stepB: 0, reachWord: 0x400, value92: value92);

    [Fact]
    public void LimitIsReachShiftedPlusRadiusPlus0x4000()
    {
        // :157 limit = (unkFC << 4) + b->unk40 + 0x4000.
        Assert.Equal((0x400 << 4) + 0x4000, Outpost().Limit(0));
        Assert.Equal((0x400 << 4) + 77 + 0x4000, Outpost().Limit(77));
    }

    [Fact]
    public void PlayerJustInsideTheLimitCapturesWithModeOne()
    {
        var outpost = Outpost();
        int limit = outpost.Limit(0);
        Assert.Equal(1, outpost.TryCapture(new CaptureCandidate(12, 1, 1, limit - 1, 0, 0, 0)));
        Assert.Equal(1, outpost.CaptureMode);          // :187 unkFE = mode
        Assert.Equal(1, outpost.Team);                 // :188 unk18 = capturer team
        Assert.Equal(2, outpost.OpposingTeam);         // :192 unkA8 = unkF7 for mode 1
        Assert.Equal(1, outpost.Mark);                 // :195 unk23 = unkF8
        Assert.Equal(60, outpost.Value92);             // :196 unk92 += unkFA * 1
        Assert.Equal(0u, outpost.Flags & 1);           // :200 unk24 &= ~1
    }

    [Fact]
    public void ExactlyAtTheLimitIsNoCapture()
    {
        // :158 returns when dist - limit >= 0.
        var outpost = Outpost();
        Assert.Equal(0, outpost.TryCapture(new CaptureCandidate(12, 1, 1, outpost.Limit(0), 0, 0, 0)));
        Assert.Equal(0, outpost.CaptureMode);
    }

    [Fact]
    public void SecondTeamCapturesWithModeTwoAndDoubleStep()
    {
        var outpost = Outpost(value92: 200);
        Assert.Equal(2, outpost.TryCapture(new CaptureCandidate(16, 1, 2, 0, 0, 1000, 0)));
        Assert.Equal(1, outpost.OpposingTeam);         // :193 unkA8 = unkF6 for mode 2
        Assert.Equal(2, outpost.Mark);                 // :195 unk23 = unkF9
        Assert.Equal((200 + 60 * 2) & 0xFF, outpost.Value92); // :196 u8 wrap of unk92 += unkFA * 2
    }

    [Fact]
    public void CapturedOutpostIsNotTakenAgain()
    {
        var outpost = Outpost();
        Assert.Equal(1, outpost.TryCapture(new CaptureCandidate(12, 1, 1, 0, 0, 0, 0)));
        // :125 returns while unkFE != 0, even for the other team standing on it.
        Assert.Equal(0, outpost.TryCapture(new CaptureCandidate(16, 1, 2, 0, 0, 0, 0)));
        Assert.Equal(1, outpost.Team);
    }

    [Theory]
    [InlineData(1, 3, false)]   // :126 team neither unkF6 nor unkF7
    [InlineData(9, 1, false)]   // :138 a flyer (type 9) is not a capturer
    [InlineData(5, 1, true)]    // :138 ground unit
    [InlineData(0x25, 2, true)] // :138 strategic drone
    public void OnlyListedTypesOfAListedTeamCapture(int type, int team, bool captures)
    {
        Assert.Equal(captures, Outpost().TryCapture(new CaptureCandidate(1, type, team, 0, 0, 0, 0)) != 0);
    }

    [Fact]
    public void BusyPlayerIsRejected()
    {
        // :135 requires the player's control block +0x40 to be zero.
        Assert.Equal(0, Outpost().TryCapture(new CaptureCandidate(12, 1, 1, 0, 0, 0, 0, Busy: true)));
    }

    [Fact]
    public void DroneIgnoresHeightGroundUnitDoesNot()
    {
        int high = Outpost().Limit(0) + 10;
        // :160-162 type 0x25 skips the second (height) distance.
        Assert.Equal(2, Outpost().TryCapture(new CaptureCandidate(287, 0x25, 2, 0, high, 0, 0)));
        // :164-183 every other capturer is measured in 3D.
        Assert.Equal(0, Outpost().TryCapture(new CaptureCandidate(75, 5, 1, 0, high, 0, 0)));
    }

    [Fact]
    public void CaptureScoresOnePointForThePlayersOfTheCapturingTeam()
    {
        // :211-224: score[+0xC1]++ (+0x64), total[+0xC1]++ (+0x7C), captures (+0x12)++.
        var score = new MatchScore(player0Team: 1);
        var field = new ObjectiveField(new[] { Outpost(), Outpost() }, score);
        Assert.Equal(2, field.Touch(new CaptureCandidate(12, 1, 1, 0, 0, 0, 0)));
        Assert.Equal(new[] { 2, 0 }, score.Score);
        Assert.Equal(new[] { 2, 0 }, score.Total);
        Assert.Equal(new[] { 2, 0 }, score.Captures);
        score.RecordCapture(2);                        // no player of team 2 -> nothing booked
        Assert.Equal(new[] { 2, 0 }, score.Score);
    }

    [Fact]
    public void SinglePlayerTeamCapturesCreditSecondScoreWordAndSkipPlayerLoop()
    {
        // 8002D1A0.c:204-210: in 1P (player1Team == -1), captures by gSinglePlayerEntity's team
        // increment Score[1], Total[1], and SinglePlayerCaptures, skipping the player loop.
        var score = new MatchScore(player0Team: 1, singlePlayerTeam: 2);
        var field = new ObjectiveField(new[] { Outpost() }, score);
        Assert.Equal(1, field.Touch(new CaptureCandidate(287, 0x25, 2, 0, 0, 0, 0)));
        Assert.Equal(new[] { 0, 1 }, score.Score);
        Assert.Equal(new[] { 0, 1 }, score.Total);
        Assert.Equal(new[] { 0, 0 }, score.Captures);
        Assert.Equal(1, score.SinglePlayerCaptures);

        // In 2P (gPlayerCount != 1, slot 1 occupied), the 1P branch does not run.
        var twoPlayer = new MatchScore(player0Team: 1, player1Team: 2, singlePlayerTeam: 2);
        twoPlayer.RecordCapture(2);
        Assert.Equal(new[] { 0, 1 }, twoPlayer.Score);
        Assert.Equal(new[] { 0, 1 }, twoPlayer.Captures);
        Assert.Equal(0, twoPlayer.SinglePlayerCaptures);

        // Explicit gPlayerCount == 2 with slot 1 empty still skips the 1P branch (8002D1A0.c:204).
        var twoPlayerEmptySlot1 = new MatchScore(player0Team: 1, player1Team: -1, singlePlayerTeam: 2, playerCount: 2);
        twoPlayerEmptySlot1.RecordCapture(2);
        Assert.Equal(new[] { 0, 0 }, twoPlayerEmptySlot1.Score);
        Assert.Equal(new[] { 0, 0 }, twoPlayerEmptySlot1.Captures);
        Assert.Equal(0, twoPlayerEmptySlot1.SinglePlayerCaptures);
    }

    // ---- retail records ------------------------------------------------------------------------

    private static string? ConftPath()
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc == null ? null : ModulePath.Resolve(disc, "conft.mis");
        if (path == null) Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
        return path;
    }

    [Fact]
    public void ConftCarriesTheEntityRequestsOfBothLoaders()
    {
        string? path = ConftPath();
        if (path == null) return;
        var records = MissionEntities.Load(new MisFile(path));
        // 263 Cact and 111 Csac request headers, each with the tACT of its data packet.
        Assert.Equal(263, records.Count(r => r.Container == EntityContainer.Cact));
        Assert.Equal(111, records.Count(r => r.Container == EntityContainer.Csac));
        // Players: Cact ids 12 (team 1, west) and 16 (team 2, east), tACT 0x181FE4 / 0x1820CC.
        var players = records.Where(r => r.Type == 1).OrderBy(r => r.Id).ToArray();
        Assert.Equal(new[] { 12, 16 }, players.Select(p => p.Id));
        Assert.Equal(new[] { 1, 2 }, players.Select(p => p.Team));
        Assert.Equal(0x181FE4, players[0].ChunkOffset);
        Assert.Equal((581632, 739062), (players[0].X, players[0].Z));
        Assert.Equal((1384448, 964606), (players[1].X, players[1].Z));
    }

    [Fact]
    public void ConftShipsThirtyTwoNeutralOutposts()
    {
        string? path = ConftPath();
        if (path == null) return;
        var outposts = MissionEntities.Load(new MisFile(path)).Where(r => r.Type == CaptureObjective.EntityType).ToArray();
        Assert.Equal(32, outposts.Length);
        Assert.All(outposts, r => Assert.Equal(EntityContainer.Csac, r.Container));
        Assert.All(outposts, r => Assert.Equal(0, r.Team));
        // Record id 112 at tACT 0x187650: position, flags 0x00110148, st+0x48.. = 01 02 01 02 3C 00,
        // st+0x4E = 0x0400, aRSL Cobj 31 / Cobj 32, script word 2 = RPNS + 0x63B.
        var r112 = outposts.Single(r => r.Id == 112);
        Assert.Equal(0x187650, r112.ChunkOffset);
        var o = CaptureObjective.FromRecord(r112, y: 0);
        Assert.Equal((698368, 984062), (o.X, o.Z));
        Assert.Equal((1, 2, 1, 2, 60, 0, 0x400), (o.TeamA, o.TeamB, o.MarkA, o.MarkB, o.StepA, o.StepB, o.ReachWord));
        Assert.Equal((31, 32), (o.ModelId, o.ChildModelId));
        Assert.Equal(0x63B, o.Script);
        Assert.True(o.RunsScriptOnCapture);
        Assert.Equal((0x400 << 4) + 0x4000, o.Limit(0));
    }

    [Fact]
    public void VictoryZonesWatchTheUnitsOfTheOpposingTeam()
    {
        string? path = ConftPath();
        if (path == null) return;
        var records = MissionEntities.Load(new MisFile(path));
        var byId = records.ToDictionary(r => r.Id);
        // Type 0x5F zone requests (gLoadMapZone) with script word 0 = RPNS + 0x6C (V[25] = 1) sit at
        // the west base, RPNS + 0x70 (V[16] = 1) at the east base. The zone's id word (source + 0x1C,
        // func_800319D4:137) names the entity func_80031DE8 tracks.
        var zones = records.Where(r => r.Type == 0x5F && (r.Scripts[0] == 0x6C || r.Scripts[0] == 0x70)).ToArray();
        Assert.Equal(12, zones.Length);
        foreach (var zone in zones)
        {
            var target = byId[zone.U16(0x1C)];
            bool west = zone.Scripts[0] == 0x6C;
            // Zone x: 583552..583680 (west, player 1 spawns at 581632), 1382528..1382656 (east, 1384448).
            Assert.True(west ? zone.X is >= 583552 and <= 583680 : zone.X is >= 1382528 and <= 1382656);
            // West zones wait for team-2 units (they attack player 1's base), east zones for team 1.
            Assert.Equal(west ? 2 : 1, target.Team);
            Assert.Contains(target.Type, new[] { 5, 28 });
        }
    }

    [Fact]
    public void SceneBooksACaptureIntoTheHudScore()
    {
        string? path = ConftPath();
        if (path == null) return;
        var scene = new LevelScene(path);
        scene.EnableObjectives();
        var field = scene.Objectives!;
        Assert.Equal(32, field.Objectives.Count);
        var target = field.Objectives.Single(o => o.Id == 41);
        Assert.Equal(1, field.Touch(new CaptureCandidate(12, 1, 1, target.X, target.Y, target.Z, 0)));
        var session = scene.ScoreSession(new HudSession());
        Assert.Equal((1, 1, 0), (session.Mode, session.Score0, session.Score1));
    }
}
