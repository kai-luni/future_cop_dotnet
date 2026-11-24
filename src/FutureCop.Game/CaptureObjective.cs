namespace FutureCop.Game;

/// <summary>
/// An entity that may take an outpost, as gTryCaptureObjective (func_8002D1A0) reads it.
/// </summary>
/// <param name="Id">obj+0x14, the entity id.</param>
/// <param name="Type">obj+0x16: 1 player, 0x12 player companion/carrier (dereferenced via <paramref name="Carried"/>), 5 ground unit, 0x25 strategic drone are accepted (8002D1A0.c:140-152).</param>
/// <param name="Team">obj+0x18.</param>
/// <param name="X">Capture point x (obj+0x5C).</param>
/// <param name="Y">Capture point y (obj+0x60).</param>
/// <param name="Z">Capture point z (obj+0x64).</param>
/// <param name="Radius">obj+0x68, the model instance radius (func_80013AF0:83 stores it shifted left by four).</param>
/// <param name="Busy">For a player: <c>e-&gt;unkAC-&gt;unk40 != 0</c>, which 8002D1A0.c:147 rejects. In retail, control block +0x40 is <c>control-&gt;special.phase</c> (80055E10.c:13,65; 800573A4.c:261-262; 8005780C.c:43-56), so 0 is the L1 press edge (<see cref="WeaponInput.PhasePressed"/>).</param>
/// <param name="Carried">obj+0xB4: for a type-0x12 carrier/companion entity (80057914.c:71,94; 80059ABC.c:64), the linked entity that 8002D1A0.c:141-144 dereferences to; null aborts the capture.</param>
public sealed record CaptureCandidate(int Id, int Type, int Team, int X, int Y, int Z, int Radius,
    bool Busy = false, CaptureCandidate? Carried = null)
{
    /// <summary>Entity type of a player companion/carrier whose +0xB4 points to the player (8002D1A0.c:140-144, 80057914.c:94).</summary>
    public const int CarrierType = 0x12;

    /// <summary>
    /// Retail <c>e-&gt;unkAC-&gt;unk40 != 0</c> predicate (8002D1A0.c:147): returns true (rejected) when
    /// <see cref="WeaponInput.SpecialPhase"/> is not on the press edge (<see cref="WeaponInput.PhasePressed"/> == 0).
    /// </summary>
    public static bool IsSpecialActionBlocked(int specialPhase) => specialPhase != WeaponInput.PhasePressed;
}

/// <summary>
/// The linked child entity (<c>obj-&gt;unk104</c>) spawned from the second aRSL model of a type-0x24
/// outpost (8002C584.c:297-300). Retail type-0x24 outposts leave <c>obj-&gt;unk100</c> null
/// (8002C584.c:303,323).
/// </summary>
public sealed class ObjectiveChildPart
{
    /// <summary>Flags ORed onto unk104 at spawn (8002C584.c:299-300).</summary>
    public const uint InitialFlags = 0x00A00000;

    /// <summary>aRSL Cobj 1 model id.</summary>
    public int ModelId { get; }

    /// <summary>unk104-&gt;unk18: owning team, mirrored on capture (8002D1A0.c:201).</summary>
    public int Team { get; internal set; }

    /// <summary>unk104-&gt;unk1C: health, mirrored by messages 9/11/14 (8002D664.c:160).</summary>
    public int Health { get; internal set; }

    /// <summary>unk104-&gt;flags (+0x24): bit 0x2000000 mirrored by messages 9/11/14 (8002D664.c:155-159); bit 8 set by message 4 (8002D664.c:173).</summary>
    public uint Flags { get; internal set; }

    /// <summary>unk104-&gt;unk92: mirrored on capture (8002D1A0.c:209).</summary>
    public int Value92 { get; internal set; }

    /// <summary>unk104-&gt;unk93: mirrored on capture (8002D1A0.c:211).</summary>
    public int Value93 { get; internal set; }

    public ObjectiveChildPart(int modelId, int team = 0, int value92 = 0, int value93 = 0, uint flags = InitialFlags)
    {
        ModelId = modelId;
        Team = team;
        Value92 = value92;
        Value93 = value93;
        Flags = flags;
    }
}

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

    /// <summary>Flag of obj+0x24 that lets a capture run the obj+0x9C script (8002D1A0.c:238).</summary>
    public const uint ScriptFlag = 0x100000;

    /// <summary>Flag of obj+0x24 mirrored onto child parts by messages 9/11/14 (8002D664.c:155-167).</summary>
    public const uint DamageSyncFlag = 0x2000000;

    /// <summary>Flag bit ORed onto child parts by message 4 retire (8002D664.c:171-177).</summary>
    public const uint ChildRetireFlag = 8;

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

    /// <summary>
    /// The linked child entity (<c>obj-&gt;unk104</c>) spawned from <see cref="ChildModelId"/>
    /// (8002C584.c:297-300), or null when <see cref="ChildModelId"/> is 0. Retail leaves
    /// <c>obj-&gt;unk100</c> null for type 0x24 (8002C584.c:303,323).
    /// </summary>
    public ObjectiveChildPart? ChildPart { get; }

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
        if (childModelId != 0)
            ChildPart = new ObjectiveChildPart(childModelId, team, value92, value93);
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
    /// gTryCaptureObjective (8002D1A0.c:137-212). Returns the capture mode (1 or 2) when
    /// <paramref name="other"/> takes the outpost, otherwise 0. Scoring is left to
    /// <see cref="MatchScore.RecordCapture"/> (8002D1A0.c:213-237) and the post-capture script
    /// hook to <see cref="ObjectiveField.OnPostCaptureScript"/> (8002D1A0.c:238-239).
    /// </summary>
    public int TryCapture(CaptureCandidate other) => TryCapture(other, out _);

    /// <summary>
    /// gTryCaptureObjective (8002D1A0.c:137-212), also returning the resolved claimant in
    /// <paramref name="claimant"/> (the dereferenced <see cref="CaptureCandidate.Carried"/> entity
    /// for type 0x12, or <paramref name="other"/> itself) so scoring and the post-capture script
    /// use the same entity retail does (8002D1A0.c:141-144, 197, 222, 239).
    /// </summary>
    public int TryCapture(CaptureCandidate other, out CaptureCandidate? claimant)
    {
        ArgumentNullException.ThrowIfNull(other);
        claimant = null;
        if (CaptureMode != 0) return 0;                                          // 8002D1A0.c:137
        if (other.Team != TeamA && other.Team != TeamB) return 0;                // 8002D1A0.c:138
        if (other.Type == 1 || other.Type == CaptureCandidate.CarrierType)       // 8002D1A0.c:140
        {
            if (other.Type == CaptureCandidate.CarrierType)                      // 8002D1A0.c:141
            {
                if (other.Carried is null) return 0;                             // 8002D1A0.c:142-143
                other = other.Carried;                                           // 8002D1A0.c:142
            }
            if (other.Busy) return 0;                                            // 8002D1A0.c:147
        }
        else if (other.Type != 5 && other.Type != 0x25) return 0;                // 8002D1A0.c:150-152

        int limit = Limit(other.Radius);                                         // 8002D1A0.c:169
        int flat = OctagonalDistance(Z - other.Z, X - other.X);                  // 8002D1A0.c:156-168
        if (flat - limit >= 0) return 0;                                         // 8002D1A0.c:170
        // 8002D1A0.c:172-195: a strategic drone (0x25) is tested on the ground plane only.
        int dist = other.Type == 0x25 ? flat : OctagonalDistance(flat, Y - other.Y);
        if (dist - limit >= 0) return 0;                                         // 8002D1A0.c:195

        int mode = other.Team == TeamA ? 1 : 2;                                  // 8002D1A0.c:197-198
        CaptureMode = mode;                                                      // 8002D1A0.c:199
        Team = other.Team;                                                       // 8002D1A0.c:200
        OpposingTeam = mode == 1 ? TeamB : TeamA;                                // 8002D1A0.c:204-206
        Mark = mode == 1 ? MarkA : MarkB;                                        // 8002D1A0.c:207
        Value92 = (byte)(Value92 + StepA * mode);                                // 8002D1A0.c:208
        Value93 = (byte)(Value93 + StepB * mode);                                // 8002D1A0.c:210
        Flags &= ~1u;                                                            // 8002D1A0.c:212
        if (ChildPart is not null)
        {
            // 8002D1A0.c:201, 209, 211: mirror team and counters onto obj-&gt;unk104.
            ChildPart.Team = other.Team;
            ChildPart.Value92 = Value92;
            ChildPart.Value93 = Value93;
        }
        claimant = other;
        return mode;
    }

    /// <summary>
    /// Mirrors bit 0x2000000 of <paramref name="parentFlags"/> and <paramref name="health"/>
    /// (<c>obj-&gt;unk1C</c>) onto <see cref="ChildPart"/>, as <c>gHandleLinkedPropEntity</c>
    /// messages 9, 11 and 14 do on <c>obj-&gt;unk104</c> (8002D664.c:150-170).
    /// </summary>
    public void SyncChildOnDamageMessage(int health, uint parentFlags)
    {
        if (ChildPart is null) return;
        if ((parentFlags & DamageSyncFlag) != 0) ChildPart.Flags |= DamageSyncFlag;
        else ChildPart.Flags &= ~DamageSyncFlag;
        ChildPart.Health = (ushort)health;
    }

    /// <summary>
    /// Sets bit 8 on <see cref="ChildPart"/> (<c>obj-&gt;unk104-&gt;flags |= 8</c>), as
    /// <c>gHandleLinkedPropEntity</c> message 4 does on retire (8002D664.c:171-178).
    /// </summary>
    public void RetireChildren()
    {
        if (ChildPart is null) return;
        ChildPart.Flags |= ChildRetireFlag;
    }

    /// <summary>True when a capture would run the obj+0x9C script (8002D1A0.c:238).</summary>
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

    /// <summary>Message-5 counter at player block +0x10 (u16), by player slot (8002D664.c:25-29,195,220).</summary>
    public int[] OutpostCaptures { get; } = new int[2];

    /// <summary>Message-5 count at state+0x134-&gt;unk38 (u16; 8002D664.c:39-52,216-218).</summary>
    public int OutpostStateCount { get; private set; }

    /// <summary>Team of the player in slot 0 / 1 of D_8009FA58, or -1 for an empty slot.</summary>
    public int[] PlayerTeams { get; }

    /// <summary>
    /// Message-5 score index (0 or 1 at player block +0xC1) by player slot; null means no +0xAC block
    /// (8002D664.c:213-215). <see cref="PlayerTeams"/> still marks absent player entities.
    /// </summary>
    public int?[] PlayerScoreIndices { get; } = { 0, 1 };

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
    /// Callback for 8002D1A0.c:221 (<c>e-&gt;unk134-&gt;unk18 = 0</c>): resets the strategic drone's
    /// strategy timer (<see cref="StrategicDroneTick.AiState.TickTimer"/>). Outpost message 5 also
    /// uses this reset in its 1P branch (8002D664.c:196).
    /// </summary>
    public Action? OnSinglePlayerCapture { get; set; }

    /// <summary>
    /// Callback for 8002D1A0.c:222 (<c>m-&gt;unkE0 = 6</c>): fired when the capturer's masked id
    /// (<c>other-&gt;unk14 &amp; 0x3FFF</c>) matches <see cref="SinglePlayerEntityId"/>.
    /// Also used for message-5 modes 7 and 8 (8002D664.c:199-202,216-218).
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

    /// <summary>
    /// Scores a type-0x24 outpost's message 5 (8002D664.c:183-223), separately from the claiming
    /// side in <see cref="RecordCapture"/> (8002D1A0.c:213-237).
    /// </summary>
    /// <remarks>
    /// Message 5 is the teardown call of gSweepRetiredEntities (8004D598.c:54) for an entity whose id
    /// was cleared. A scoring outpost gets there by damage: health below 0 writes +0x8C
    /// (8002AA1C.c:114-121), the frame sweep sends message 4 (8004CE04.c:59-60), and the base retire
    /// clears the id (8002AD58.c:89, 8004D958.c:59). The level-reset teardown sets 0x4000000 first
    /// (8004CCDC.c:53), so it never scores. The scene has no caller yet because outposts take no damage
    /// and are never retired in the port. Verified against unchanged C by
    /// tests/golden/outpost-message5.vectors.
    /// </remarks>
    /// <param name="objective">The type-0x24 outpost receiving the message.</param>
    /// <param name="sourceUnitWord">
    /// The outpost's raw +0x8C word (8002D664.c:197), written by damage handling (8002AA1C.c:114-121).
    /// </param>
    /// <param name="findUnitEntityId">
    /// Resolves the masked +0x8C lookup key to the active unit's +0x14 id, or null if not found
    /// (8004D664.c:36-45; 8002D664.c:197-199).
    /// </param>
    public void RecordOutpostMessage5(CaptureObjective objective, uint sourceUnitWord, Func<int, int?> findUnitEntityId)
    {
        ArgumentNullException.ThrowIfNull(objective);
        if (objective.CaptureMode == 0 || (objective.Flags & 0x4000000u) != 0) return; // 8002D664.c:183

        int kind = objective.CaptureMode == 1 ? objective.TeamB : objective.TeamA; // 8002D664.c:184
        bool hasState = SinglePlayerTeam >= 0;                                    // 8002D664.c:189,209
        if (PlayerCount == 1 && hasState && SinglePlayerTeam == kind)             // 8002D664.c:185-191
        {
            Score[1]++;                                                          // 8002D664.c:192
            Total[1]++;                                                          // 8002D664.c:193
            OutpostCaptures[0] = unchecked((ushort)(OutpostCaptures[0] + 1));       // 8002D664.c:195
            OnSinglePlayerCapture?.Invoke();                                      // 8002D664.c:196
            if (findUnitEntityId((int)(sourceUnitWord & 0x3FFFFFFFu)) is int foundId
                && (foundId & 0x3FFF) == SinglePlayerEntityId)                     // 8002D664.c:197-199
                OnSessionE0Changed?.Invoke(7);                                    // 8002D664.c:202
            return;                                                              // 8002D664.c:205
        }

        // 8002D664.c:208-223: a missing or nonmatching 1P state falls through to both player slots.
        for (int i = 0; i < 2; i++)
        {
            if (PlayerTeams[i] < 0 || PlayerScoreIndices[i] is not int index
                || PlayerTeams[i] != kind) continue;                             // 8002D664.c:213

            Score[index]++;                                                      // 8002D664.c:214
            Total[index]++;                                                      // 8002D664.c:215
            if (hasState)                                                        // 8002D664.c:216
            {
                OutpostStateCount = unchecked((ushort)(OutpostStateCount + 1));   // 8002D664.c:217
                OnSessionE0Changed?.Invoke(8);                                    // 8002D664.c:218
            }
            else
            {
                int otherSlot = i ^ 1;                                           // 8002D664.c:220
                // 8002D664.c:220 is `players[i ^ 1]->unkAC->unk10++` with no null test. Retail only
                // reaches it with an empty slot in a one-player match without gSinglePlayerEntity
                // (published by 800331CC.c:243, cleared only by 800247A8.c:64; slots are filled by
                // 800579D4.c:189 and cleared only by 8004CCDC.c:59-60 after its sweep). The PS1 has no
                // MMU, so that load reads the word at address 0xAC and bumps a u16 16 bytes past
                // whatever it holds - unmodellable, so the port keeps the score of :214-215 and drops
                // only this counter. The golden vectors leave this row out for the same reason.
                if (PlayerTeams[otherSlot] >= 0 && PlayerScoreIndices[otherSlot].HasValue)
                    OutpostCaptures[otherSlot] = unchecked((ushort)(OutpostCaptures[otherSlot] + 1));
            }
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

    /// <summary>
    /// Hook for <c>gInterpretEntityScript(self-&gt;unk9C, 0, other-&gt;unk14 &amp; 0x7FFF)</c>
    /// (8002D1A0.c:238-239), invoked at claim time right after scoring when
    /// <see cref="CaptureObjective.Script"/> is non-zero and
    /// <see cref="CaptureObjective.ScriptFlag"/> (<c>0x100000</c>) is set on
    /// <see cref="CaptureObjective.Flags"/>. Modeled as a hook because the RPNS bytecode VM
    /// (<c>func_80069D68</c>) is not ported; the <c>*self-&gt;unk9C != 0</c> first-byte check
    /// requires the RPNS bytecode buffer and is left to the wired handler.
    /// </summary>
    public Action<CaptureObjective, int>? OnPostCaptureScript { get; set; }

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
            int mode = objective.TryCapture(other, out var claimant);
            if (mode == 0 || claimant is null) continue;
            Score.RecordCapture(claimant.Team, claimant.Id);
            // 8002D1A0.c:238-239: runs inside gTryCaptureObjective after scoring (not at msg 5).
            // Guard order: self-&gt;unk9C != 0 &amp;&amp; (*self-&gt;unk9C != 0, left to VM/hook) &amp;&amp; (self-&gt;unk24 &amp; 0x100000).
            if (objective.Script != 0 && (objective.Flags & CaptureObjective.ScriptFlag) != 0)
                OnPostCaptureScript?.Invoke(objective, claimant.Id & 0x7FFF);
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
