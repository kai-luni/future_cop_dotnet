namespace FutureCop.Game;

/// <summary>
/// An entity that may take an outpost, as gTryCaptureObjective (func_8002D1A0) reads it.
/// </summary>
/// <param name="Id">obj+0x14, the entity id.</param>
/// <param name="Type">obj+0x16: 1 player, 5 ground unit, 0x25 strategic drone are accepted.</param>
/// <param name="Team">obj+0x18.</param>
/// <param name="X">Capture point x (obj+0x5C).</param>
/// <param name="Y">Capture point y (obj+0x60).</param>
/// <param name="Z">Capture point z (obj+0x64).</param>
/// <param name="Radius">obj+0x68, the model instance radius (func_80013AF0:83 stores it shifted left by four).</param>
/// <param name="Busy">For a player: its control block +0x40 is non-zero, which func_8002D1A0:135 rejects.</param>
public readonly record struct CaptureCandidate(int Id, int Type, int Team, int X, int Y, int Z, int Radius,
    bool Busy = false);

/// <summary>
/// A capturable outpost: entity type 0x24, handled by gHandleLinkedPropEntity (func_8002D664), whose
/// message 10 runs gTryCaptureObjective (func_8002D1A0) against the touching entity.
/// </summary>
/// <remarks>
/// The capture parameters come from the tACT source block: gInitObjectFromDef copies st+0x48..+0x4D to
/// obj+0xF6..+0xFB, the u16 st+0x4E to obj+0xFC, clears obj+0xFE and sets bit 0 of obj+0x24
/// (8002C584.c:326-336; st is u16 at +0x4E, 8002C584.c:65).
/// </remarks>
public sealed class CaptureObjective
{
    /// <summary>Entity type of an outpost (func_8002D664:132).</summary>
    public const int EntityType = 0x24;

    /// <summary>Constant part of the capture radius (func_8002D1A0:157).</summary>
    public const int BaseReach = 0x4000;

    /// <summary>Flag of obj+0x24 that lets a capture run the obj+0x9C script (func_8002D1A0:226).</summary>
    public const uint ScriptFlag = 0x100000;

    public int Id { get; }
    public int X { get; }
    public int Y { get; }
    public int Z { get; }

    /// <summary>obj+0xF6 / +0xF7: the two teams that may capture; the first one is mode 1.</summary>
    public int TeamA { get; }
    public int TeamB { get; }

    /// <summary>obj+0xF8 / +0xF9: the value obj+0x23 takes for a mode-1 / mode-2 capture.</summary>
    public int MarkA { get; }
    public int MarkB { get; }

    /// <summary>obj+0xFA / +0xFB: added, times the mode, to obj+0x92 / +0x93.</summary>
    public int StepA { get; }
    public int StepB { get; }

    /// <summary>obj+0xFC: the record's reach word; the radius adds it shifted left by four.</summary>
    public int ReachWord { get; }

    /// <summary>obj+0xFE: 0 while open, 1 or 2 after the capture (by <see cref="TeamA"/> / <see cref="TeamB"/>).</summary>
    public int CaptureMode { get; private set; }

    /// <summary>obj+0x18: the owning team.</summary>
    public int Team { get; private set; }

    /// <summary>obj+0x23, set to <see cref="MarkA"/> or <see cref="MarkB"/> by a capture.</summary>
    public int Mark { get; private set; }

    /// <summary>obj+0xA8 (copied to +0xDC): the team the captured outpost now opposes.</summary>
    public int OpposingTeam { get; private set; }

    /// <summary>obj+0x92 / +0x93.</summary>
    public int Value92 { get; private set; }
    public int Value93 { get; private set; }

    /// <summary>obj+0x24.</summary>
    public uint Flags { get; private set; }

    /// <summary>The two models of the record: the outpost itself (aRSL Cobj 0) and its child (aRSL Cobj 1).</summary>
    public int ModelId { get; }
    public int ChildModelId { get; }

    /// <summary>Script word 2 of the request (obj+0x9C), an RPNS offset.</summary>
    public int Script { get; }

    public CaptureObjective(int id, int x, int y, int z, int teamA, int teamB, int markA, int markB,
        int stepA, int stepB, int reachWord, int team = 0, int mark = 0, int value92 = 0, int value93 = 0,
        uint flags = 1, int modelId = 0, int childModelId = 0, int script = 0)
    {
        Id = id; X = x; Y = y; Z = z;
        TeamA = teamA; TeamB = teamB; MarkA = markA; MarkB = markB; StepA = stepA; StepB = stepB;
        ReachWord = reachWord; Team = team; Mark = mark; Value92 = value92; Value93 = value93;
        Flags = flags; ModelId = modelId; ChildModelId = childModelId; Script = script;
    }

    /// <summary>
    /// The outpost of a type-0x24 record. <paramref name="y"/> replaces the record's y because the
    /// capture point is a model vertex in the world (obj+0x5C), not the source position.
    /// </summary>
    public static CaptureObjective FromRecord(EntityRecord record, int y)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Type != EntityType) throw new ArgumentException($"type {record.Type} is not an outpost", nameof(record));
        return new CaptureObjective(record.Id, record.X, y, record.Z,
            teamA: record.U8(0x48), teamB: record.U8(0x49), markA: record.U8(0x4A), markB: record.U8(0x4B),
            stepA: record.U8(0x4C), stepB: record.U8(0x4D), reachWord: record.U16(0x4E),
            team: record.Team, mark: record.U8(0x1E), value92: record.U8(0x22), value93: record.U8(0x23),
            // func_8002A5F4:133-135 copies the flags and drops 0x400000; func_8002C584:335 sets bit 0.
            flags: (record.Flags & 0xFFBFFFFFu) | 1, modelId: record.Model(0), childModelId: record.Model(1),
            script: record.Scripts[2]);
    }

    /// <summary>
    /// The octagonal distance of func_8002D1A0:144-156 (and again :167-178): the larger and smaller
    /// magnitude, a 105/128 blend when the smaller exceeds half the larger, then
    /// <c>big + (small &gt;&gt; 2) - (small &gt;&gt; 6)</c>.
    /// </summary>
    public static int OctagonalDistance(int a, int b)
    {
        int big = Math.Abs(a), small = Math.Abs(b);
        if (big < small) (big, small) = (small, big);
        if ((big >> 1) < small)
        {
            int sum = big + small;
            sum = (sum << 6) + (sum << 3) + (sum << 1) + big + (small << 5);
            small = big - small;
            big = sum >> 7;
        }
        return big + (small >> 2) - (small >> 6);
    }

    /// <summary>The capture limit of func_8002D1A0:157: (obj+0xFC &lt;&lt; 4) + candidate radius + 0x4000.</summary>
    public int Limit(int radius) => (ReachWord << 4) + radius + BaseReach;

    /// <summary>
    /// gTryCaptureObjective (func_8002D1A0:125-200). Returns the capture mode (1 or 2) when
    /// <paramref name="other"/> takes the outpost, otherwise 0. Scoring is left to
    /// <see cref="MatchScore.RecordCapture"/>, which mirrors :201-224.
    /// </summary>
    public int TryCapture(CaptureCandidate other)
    {
        if (CaptureMode != 0) return 0;                                          // :125
        if (other.Team != TeamA && other.Team != TeamB) return 0;               // :126
        if (other.Type == 1) { if (other.Busy) return 0; }                      // :128-137
        else if (other.Type != 5 && other.Type != 0x25) return 0;               // :138-139

        int limit = Limit(other.Radius);
        int flat = OctagonalDistance(Z - other.Z, X - other.X);                 // :144-156
        if (flat - limit >= 0) return 0;                                        // :158
        // :160-180: a strategic drone (0x25) is tested on the ground plane only.
        int dist = other.Type == 0x25 ? flat : OctagonalDistance(flat, Y - other.Y);
        if (dist - limit >= 0) return 0;                                        // :183

        int mode = other.Team == TeamA ? 1 : 2;                                 // :185-186
        CaptureMode = mode;
        Team = other.Team;                                                      // :188-189
        OpposingTeam = mode == 1 ? TeamB : TeamA;                               // :192-194
        Mark = mode == 1 ? MarkA : MarkB;                                       // :195
        Value92 = (byte)(Value92 + StepA * mode);                               // :196
        Value93 = (byte)(Value93 + StepB * mode);                               // :198
        Flags &= ~1u;                                                           // :200
        return mode;
    }

    /// <summary>True when a capture would run the obj+0x9C script (func_8002D1A0:226).</summary>
    public bool RunsScriptOnCapture => Script != 0 && (Flags & ScriptFlag) != 0;
}

/// <summary>
/// The capture counters of the match block D_8009FA94: the two score words at +0x64 (the HUD's score,
/// 80047F30.c:214) and the two totals at +0x7C. The mission scripts read the same words as S[25]/S[26]
/// and S[31]/S[32] and spend S[25]/S[26] on units.
/// </summary>
public sealed class MatchScore
{
    /// <summary>+0x64[2]: indexed by the player block's +0xC1 (the player index).</summary>
    public int[] Score { get; } = new int[2];

    /// <summary>+0x7C[2].</summary>
    public int[] Total { get; } = new int[2];

    /// <summary>Per player index: captures (player block +0x12, func_8002D1A0:219).</summary>
    public int[] Captures { get; } = new int[2];

    /// <summary>Team of the player in slot 0 / 1 of D_8009FA58, or -1 for an empty slot.</summary>
    public int[] PlayerTeams { get; }

    /// <summary>gPlayerCount (D_8009FA0C): explicit player count tested by func_8002D1A0:204.</summary>
    public int PlayerCount { get; }

    /// <summary>
    /// Team of gSinglePlayerEntity (D_8009F9EC), or -1 when none is active. In a one-player Precinct
    /// Assault match that entity is the strategic drone itself, set by func_800331CC:243 with the
    /// drone's team in obj+0x18 - not the human player of D_8009FA58[0].
    /// </summary>
    public int SinglePlayerTeam { get; }

    /// <summary>gSinglePlayerEntity-&gt;unk14 (entity id), or -1 when unknown/inactive (8002D1A0.c:210).</summary>
    public int SinglePlayerEntityId { get; }

    /// <summary>func_8002D1A0:208, e-&gt;unk134-&gt;unk3A of the single-player branch: captures by it.</summary>
    public int SinglePlayerCaptures { get; private set; }

    /// <summary>
    /// Callback for 8002D1A0.c:209 (<c>e-&gt;unk134-&gt;unk18 = 0</c>): resets the strategic drone's
    /// strategy timer (<see cref="StrategicDroneTick.AiState.TickTimer"/>) on every 1P branch capture.
    /// </summary>
    public Action? OnSinglePlayerCapture { get; set; }

    /// <summary>
    /// Callback for 8002D1A0.c:210 (<c>m-&gt;unkE0 = 6</c>): fired when the capturer's masked id
    /// (<c>other-&gt;unk14 &amp; 0x3FFF</c>) matches <see cref="SinglePlayerEntityId"/>.
    /// </summary>
    public Action<int>? OnSessionE0Changed { get; set; }

    public MatchScore(int player0Team, int player1Team = -1, int singlePlayerTeam = -1,
        int singlePlayerEntityId = -1, int? playerCount = null)
    {
        PlayerTeams = new[] { player0Team, player1Team };
        PlayerCount = playerCount ?? (player1Team >= 0 ? 2 : 1);
        SinglePlayerTeam = singlePlayerTeam;
        SinglePlayerEntityId = singlePlayerEntityId;
    }

    /// <summary>
    /// func_8002D1A0:201-224. In a one-player match (gPlayerCount == 1) with an active
    /// gSinglePlayerEntity, captures by that entity's team take the :204-210 branch: the
    /// second score/total word (+0x64[1] / +0x7C[1]) and e-&gt;unk134-&gt;unk3A increment, e-&gt;unk134-&gt;unk18
    /// is cleared to 0, m-&gt;unkE0 is set to 6 when <c>(capturerId &amp; 0x3FFF) == SinglePlayerEntityId</c>,
    /// and the player loop :211-224 does not run. Otherwise the loop credits every player in
    /// D_8009FA58 whose team matches, on +0x64 at its own slot index.
    /// </summary>
    public void RecordCapture(int team, int capturerId = -1)
    {
        if (PlayerCount == 1 && SinglePlayerTeam >= 0 && SinglePlayerTeam == team)
        {
            Score[1]++;
            Total[1]++;
            SinglePlayerCaptures++;                                   // e->unk134->unk3A++
            OnSinglePlayerCapture?.Invoke();                          // e->unk134->unk18 = 0
            if (capturerId >= 0 && SinglePlayerEntityId >= 0
                && (capturerId & 0x3FFF) == SinglePlayerEntityId)
                OnSessionE0Changed?.Invoke(6);                        // m->unkE0 = 6
            return;
        }
        for (int i = 0; i < 2; i++)
        {
            if (PlayerTeams[i] < 0 || PlayerTeams[i] != team) continue;
            Score[i]++;
            Total[i]++;
            Captures[i]++;
        }
    }
}

/// <summary>
/// The outposts of a running mission and the match score they feed.
/// </summary>
public sealed class ObjectiveField
{
    private readonly List<CaptureObjective> _objectives;

    public IReadOnlyList<CaptureObjective> Objectives => _objectives;
    public MatchScore Score { get; }

    /// <summary>Outposts captured since the last call of <see cref="TakeCaptures"/>, for logs and the viewer.</summary>
    private readonly List<(CaptureObjective Objective, int Mode)> _recent = new();

    public ObjectiveField(IEnumerable<CaptureObjective> objectives, MatchScore score)
    {
        _objectives = objectives.ToList();
        Score = score;
    }

    /// <summary>
    /// The outposts of <paramref name="records"/>. <paramref name="groundY"/> supplies the capture
    /// point's height for a record position.
    /// </summary>
    public static ObjectiveField FromRecords(IEnumerable<EntityRecord> records, Func<int, int, int> groundY,
        MatchScore score) =>
        new(records.Where(r => r.Type == CaptureObjective.EntityType)
            .Select(r => CaptureObjective.FromRecord(r, groundY(r.X, r.Z))), score);

    /// <summary>
    /// Offers every open outpost to <paramref name="other"/> (message 10 of func_8002D664) and books each
    /// capture. Returns the number of outposts taken.
    /// </summary>
    public int Touch(CaptureCandidate other)
    {
        int taken = 0;
        foreach (var objective in _objectives)
        {
            int mode = objective.TryCapture(other);
            if (mode == 0) continue;
            Score.RecordCapture(other.Team, other.Id);
            _recent.Add((objective, mode));
            taken++;
        }
        return taken;
    }

    /// <summary>Returns and clears the captures since the previous call.</summary>
    public IReadOnlyList<(CaptureObjective Objective, int Mode)> TakeCaptures()
    {
        var list = _recent.ToList();
        _recent.Clear();
        return list;
    }
}
