using System.Buffers.Binary;

namespace FutureCop.Game;

/// <summary>
/// Kind of retail purchase trigger parsed from a mission file's <c>Cact</c> records.
/// </summary>
public enum PurchasePadKind
{
    /// <summary>
    /// Map zone trigger (<c>Cact</c> type 95 / 0x5F, <c>func_800319D4</c> + <c>func_80031C74</c>).
    /// Used for main-base helipads and tankpads (cost = 1).
    /// </summary>
    MapZone = 95,

    /// <summary>
    /// Trigger/attachment entity (<c>Cact</c> type 14 / 0x0E, <c>func_80030DE0</c> +
    /// <c>func_8003151C</c> + <c>func_80031120</c>). Used for outpost pads (cost = 2, capture = 30)
    /// and dreadnought/fortress pads (cost = 50).
    /// </summary>
    TriggerAttachment = 14,
}

/// <summary>
/// One parsed purchase pad (<c>Cact</c> type 95 or type 14) with its retail trigger geometry,
/// gating flags, and RPNS script offsets.
/// </summary>
public sealed class PurchasePad
{
    /// <summary>Entity record id (tACT +0x00).</summary>
    public int RecordId { get; }

    /// <summary>Whether this pad is a type-95 map zone or a type-14 trigger attachment.</summary>
    public PurchasePadKind Kind { get; }

    /// <summary>World-space centre (tACT +0x08..+0x13).</summary>
    public int WorldX { get; }
    public int WorldY { get; }
    public int WorldZ { get; }

    /// <summary>
    /// Half-extents in world units (<c>extent &lt;&lt; 4</c> for type 95, <c>80031C74.c:86,92,98</c>;
    /// <c>radius &lt;&lt; 4</c> for type 14, <c>80030DE0.c:111</c>).
    /// </summary>
    public int ExtentX { get; }
    public int ExtentY { get; }
    public int ExtentZ { get; }

    /// <summary>
    /// Raw trigger flags byte (type 95: <c>node-&gt;flags</c> at tACT +0x1B, <c>800319D4.c:138</c>;
    /// type 14: <c>self-&gt;unkA0</c> at tACT +0x28, <c>80030DE0.c:115</c>). Bit 0x04 gates on
    /// walker mode (<c>player-&gt;unkAC-&gt;unk40 == 0</c>); bit 0x10 disables type-14 pads.
    /// </summary>
    public byte Flags { get; set; }

    /// <summary>
    /// Entity id matched by the trigger (type 95: <c>node-&gt;id</c> at tACT +0x1C, e.g. 12 for P1,
    /// 16 for P2; type 14: <c>self-&gt;unkA2</c> at tACT +0x2A).
    /// </summary>
    public short TargetId { get; }

    /// <summary>Field selector for type-14 triggers (<c>self-&gt;unkA1</c>, <c>8002F8B8.c:64</c>).</summary>
    public byte MatchSelector { get; }

    /// <summary>RPNS offset of the primary trigger script (slot 0, executed on pad activation).</summary>
    public int Script0Offset { get; }

    /// <summary>RPNS offset of the enable/capture script (slot 2, executed by cmd 0x7A when flags &amp; 0x20).</summary>
    public int Script2Offset { get; }

    /// <summary>Last frame stamp on which this type-14 pad fired (<c>self-&gt;unkA4</c>, <c>8003151C.c:115</c>).</summary>
    public int LastTriggerFrame { get; set; } = -1;

    /// <summary>True when this pad is currently active (bit 0x10 clear, <c>8003151C.c:106</c>).</summary>
    public bool IsEnabled => (Flags & 0x10) == 0;

    /// <summary>True when this pad rejects hover-mode players (bit 0x04 set, <c>80031C74.c:102</c>, <c>8002F8B8.c:122</c>).</summary>
    public bool RequiresWalkerMode => (Flags & 0x04) != 0;

    public PurchasePad(
        int recordId,
        PurchasePadKind kind,
        int worldX, int worldY, int worldZ,
        int extentX, int extentY, int extentZ,
        byte flags,
        short targetId,
        byte matchSelector,
        int script0Offset,
        int script2Offset)
    {
        RecordId = recordId;
        Kind = kind;
        WorldX = worldX;
        WorldY = worldY;
        WorldZ = worldZ;
        ExtentX = extentX;
        ExtentY = extentY;
        ExtentZ = extentZ;
        Flags = flags;
        TargetId = targetId;
        MatchSelector = matchSelector;
        Script0Offset = script0Offset;
        Script2Offset = script2Offset;
    }
}

/// <summary>
/// One <c>Cfun</c> script timer entry (<c>tFUN</c> + <c>tEXT</c>, <c>80049564.c:4-11</c>).
/// In <c>CONFT.MIS</c>, timers 7..21 drain queued tank counts in <c>gScriptVmVars</c> and
/// dispatch op 60 (<c>SPWID</c>) to the corresponding tank <c>Csac</c> spawner.
/// </summary>
public sealed class ScriptTimerEntry
{
    public int Count { get; set; }
    public int Period { get; }
    public int Remaining { get; set; }
    public int TriggerOffset { get; }
    public int ActionOffset { get; }

    public ScriptTimerEntry(int count, int period, int remaining, int triggerOffset, int actionOffset)
    {
        Count = count;
        Period = period;
        Remaining = remaining;
        TriggerOffset = triggerOffset;
        ActionOffset = actionOffset;
    }
}

/// <summary>
/// Result of attempting to trigger a <see cref="PurchasePad"/>.
/// </summary>
public readonly record struct PurchaseResult(
    bool Triggered,
    bool Accepted,
    int SfxId,
    int ScoreBefore,
    int ScoreAfter);

/// <summary>
/// Drives the retail Precinct Assault purchase path: loads <c>RPNS</c>, <c>Cact</c> type-95/14
/// purchase pads, and <c>Cfun</c> tank-queue timers from a mission file, executes the RPNS
/// purchase scripts through <see cref="RpnsScriptVm"/>, routes op 60 (<c>func_8006158C</c>) into
/// <see cref="CsacUnitSpawner.ApplyCommandById"/>, and routes op 56 (<c>cmd 0x7A</c>) pad-enable
/// commands (<c>func_80031004</c>).
/// </summary>
public sealed class PurchasePadSystem
{
    private const uint TagShoc = 0x53484F43u;
    private const uint TagShdr = 0x53484452u;
    private const uint TagSdat = 0x53444154u;
    private const uint TagFill = 0x46494C4Cu;
    private const uint TagCact = 0x43616374u;
    private const uint TagCfun = 0x4366756Eu;
    private const uint TagTact = 0x74414354u;
    private const uint TagTfun = 0x7446554Eu;
    private const uint TagText = 0x74455854u;
    private const int SectorStride = 0x6000;

    private readonly List<PurchasePad> _pads = new();
    private readonly Dictionary<int, PurchasePad> _padsById = new();
    private readonly List<ScriptTimerEntry> _timers = new();
    private readonly byte[] _timerBytecode;
    private readonly CsacUnitSpawner? _spawner;

    /// <summary>Extracted RPNS bytecode pool.</summary>
    public RpnsPool Pool { get; }

    /// <summary>Mutable VM state (scores in GameSession[25/26], unit counts in [27..30], tank queues in ScriptVars).</summary>
    public RpnsVmState VmState { get; }

    /// <summary>All purchase pads loaded from the mission.</summary>
    public IReadOnlyList<PurchasePad> Pads => _pads;

    /// <summary>All <c>Cfun</c> script timers loaded from the mission.</summary>
    public IReadOnlyList<ScriptTimerEntry> Timers => _timers;

    public PurchasePadSystem(
        RpnsPool pool,
        RpnsVmState? vmState = null,
        CsacUnitSpawner? spawner = null,
        IEnumerable<PurchasePad>? pads = null,
        IEnumerable<ScriptTimerEntry>? timers = null,
        byte[]? timerBytecode = null)
    {
        ArgumentNullException.ThrowIfNull(pool);
        Pool = pool;
        VmState = vmState ?? new RpnsVmState();
        _spawner = spawner;
        _timerBytecode = timerBytecode ?? Array.Empty<byte>();

        if (pads is not null)
        {
            foreach (var pad in pads)
            {
                _pads.Add(pad);
                _padsById[pad.RecordId] = pad;
            }
        }
        if (timers is not null)
            _timers.AddRange(timers);

        // Wire op 60 (func_8006158C) -> CsacUnitSpawner.ApplyCommandById
        var prevSpawner = VmState.OnSpawnerCommandById;
        VmState.OnSpawnerCommandById = (id, cmd, arg2) =>
        {
            _spawner?.ApplyCommandById(id, cmd, arg2);
            prevSpawner?.Invoke(id, cmd, arg2);
        };

        // Wire op 56 (func_8004DC64 -> func_80031004 case 0x7A) -> enable/disable type-14 pads
        var prevTag = VmState.OnCommandByTag;
        VmState.OnCommandByTag = (tag, op, arg) =>
        {
            if (op == 0x7A && _padsById.TryGetValue(tag, out var pad))
                ApplyPadCommand7A(pad, arg);
            prevTag?.Invoke(tag, op, arg);
        };
    }

    /// <summary>
    /// Loads the <c>RPNS</c> pool, all type-95 and type-14 purchase pads, and all <c>Cfun</c>
    /// script timers directly from a PS1 mission (<c>.MIS</c>) file.
    /// </summary>
    public static PurchasePadSystem? LoadFromMission(
        ReadOnlySpan<byte> missionBytes,
        CsacUnitSpawner? spawner = null,
        RpnsVmState? vmState = null)
    {
        var pool = RpnsPool.Extract(missionBytes);
        if (pool is null)
            return null;

        var pads = new List<PurchasePad>();
        var timers = new List<ScriptTimerEntry>();
        byte[] timerBytecode = Array.Empty<byte>();

        for (int off = 0; off + 0x30 <= missionBytes.Length; off += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[off..]) != TagShoc ||
                BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 0x10)..]) != TagShdr)
            {
                continue;
            }

            uint tag = BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 0x18)..]);
            if (tag != TagCact && tag != TagCfun)
                continue;

            int headerLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 4)..]);
            uint reqId = BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 0x1C)..]);
            int pkt = off + headerLen;
            if (pkt + 4 <= missionBytes.Length &&
                BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[pkt..]) == TagFill)
            {
                pkt = (pkt / SectorStride + 1) * SectorStride;
            }
            if (pkt + 20 > missionBytes.Length ||
                BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[pkt..]) != TagShoc ||
                BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(pkt + 0x10)..]) != TagSdat)
            {
                continue;
            }
            int pktEnd = Math.Min(missionBytes.Length, pkt + (int)BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(pkt + 4)..]));

            if (tag == TagCfun)
            {
                int tfun = -1, text = -1;
                for (int q = pkt + 20; q + 12 <= pktEnd; q += 4)
                {
                    uint sub = BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[q..]);
                    if (sub == TagTfun && BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(q + 8)..]) == reqId)
                        tfun = q;
                    else if (sub == TagText && BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(q + 8)..]) == reqId)
                        text = q;
                }
                if (tfun >= 0 && text >= 0)
                {
                    int fsize = (int)BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(tfun + 4)..]);
                    int tsize = (int)BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(text + 4)..]);
                    if (text + 12 <= missionBytes.Length && tsize >= 12 && text + tsize <= missionBytes.Length)
                        timerBytecode = missionBytes.Slice(text + 12, tsize - 12).ToArray();
                    int count = (fsize - 12) / 20;
                    for (int i = 0; i < count; i++)
                    {
                        int e = tfun + 12 + i * 20;
                        if (e + 20 > missionBytes.Length) break;
                        timers.Add(new ScriptTimerEntry(
                            BinaryPrimitives.ReadInt32LittleEndian(missionBytes[e..]),
                            BinaryPrimitives.ReadInt32LittleEndian(missionBytes[(e + 4)..]),
                            BinaryPrimitives.ReadInt32LittleEndian(missionBytes[(e + 8)..]),
                            BinaryPrimitives.ReadInt32LittleEndian(missionBytes[(e + 12)..]),
                            BinaryPrimitives.ReadInt32LittleEndian(missionBytes[(e + 16)..])));
                    }
                }
                continue;
            }

            // Cact header: script offsets at +0x24, +0x28, +0x2C
            int s0 = (int)BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 0x24)..]);
            int s2 = (int)BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 0x2C)..]);
            for (int q = pkt + 20; q + 12 <= pktEnd; q += 4)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[q..]) != TagTact ||
                    BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(q + 8)..]) != reqId)
                {
                    continue;
                }

                int tlen = (int)BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(q + 4)..]);
                if (tlen < 0x20 || q + tlen > missionBytes.Length)
                    break;
                var src = missionBytes.Slice(q + 8, tlen - 8);
                int recordId = BinaryPrimitives.ReadUInt16LittleEndian(src);
                byte entityType = src[4];
                if (entityType != 95 && entityType != 14)
                    break;
                if ((uint)s0 >= (uint)pool.Length || pool.Bytes[s0] == 0)
                    break;

                int wx = BinaryPrimitives.ReadInt32LittleEndian(src[8..]);
                int wy = BinaryPrimitives.ReadInt32LittleEndian(src[12..]);
                int wz = BinaryPrimitives.ReadInt32LittleEndian(src[16..]);

                if (entityType == 95 && src.Length >= 0x1E)
                {
                    // 800319D4.c:134-138: extents halved, then << 4 in 80031C74.c:86,92,98
                    short rawZ = BinaryPrimitives.ReadInt16LittleEndian(src[0x14..]);
                    short rawX = BinaryPrimitives.ReadInt16LittleEndian(src[0x16..]);
                    short rawY = BinaryPrimitives.ReadInt16LittleEndian(src[0x18..]);
                    byte flags = src[0x1B];
                    short targetId = BinaryPrimitives.ReadInt16LittleEndian(src[0x1C..]);
                    pads.Add(new PurchasePad(
                        recordId, PurchasePadKind.MapZone,
                        wx, wy, wz,
                        (rawX / 2) << 4, (rawY / 2) << 4, (rawZ / 2) << 4,
                        flags, targetId, matchSelector: 2,
                        s0, s2));
                }
                else if (entityType == 14 && src.Length >= 0x2C)
                {
                    // 80030DE0.c:111-115: radius = unk24 << 4, flags = unk28, sel = unk29, val = unk2A
                    short rawRadius = BinaryPrimitives.ReadInt16LittleEndian(src[0x24..]);
                    int radius = rawRadius << 4;
                    byte flags = src[0x28];
                    byte selector = src[0x29];
                    short matchVal = BinaryPrimitives.ReadInt16LittleEndian(src[0x2A..]);
                    pads.Add(new PurchasePad(
                        recordId, PurchasePadKind.TriggerAttachment,
                        wx, wy, wz,
                        radius, radius, radius,
                        flags, matchVal, selector,
                        s0, s2));
                }
                break;
            }
        }

        return new PurchasePadSystem(pool, vmState, spawner, pads, timers, timerBytecode);
    }

    /// <summary>Looks up a loaded purchase pad by its <c>Cact</c> record id.</summary>
    public PurchasePad? FindPad(int recordId) =>
        _padsById.TryGetValue(recordId, out var pad) ? pad : null;

    /// <summary>
    /// Copies the live score words from <see cref="MatchScore"/> into
    /// <c>gGameSession[25/26]</c> (<c>8002D664.c:192,214</c>).
    /// </summary>
    public void SyncFromMatchScore(MatchScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        VmState.GameSession[RpnsVmState.SessionScoreP1] = score.Score[0];
        VmState.GameSession[RpnsVmState.SessionScoreP2] = score.Score[1];
    }

    /// <summary>
    /// Copies <c>gGameSession[25/26]</c> back into <see cref="MatchScore.Score"/> after
    /// script execution.
    /// </summary>
    public void SyncToMatchScore(MatchScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        score.Score[0] = VmState.GameSession[RpnsVmState.SessionScoreP1];
        score.Score[1] = VmState.GameSession[RpnsVmState.SessionScoreP2];
    }

    /// <summary>
    /// Applies trigger-entity command <c>0x7A</c> (<c>func_80031004</c>, <c>80031004.c:65-78</c>):
    /// <paramref name="arg"/> == 0 disables the pad (sets bit 0x10); <paramref name="arg"/> != 0
    /// enables it (clears bit 0x10) and, if bit 0x20 is set, executes its slot-2 script
    /// (outpost capture cost-30 deduction and turret/helipad activation in <c>CONFT.MIS</c>).
    /// </summary>
    public void ApplyPadCommand7A(PurchasePad pad, int arg)
    {
        ArgumentNullException.ThrowIfNull(pad);
        if (arg != 0)
        {
            byte oldFlags = pad.Flags;
            pad.Flags = (byte)(oldFlags & ~0x10);
            if ((oldFlags & 0x20) != 0 && (uint)pad.Script2Offset < (uint)Pool.Length && Pool.Bytes[pad.Script2Offset] != 0)
            {
                RpnsScriptVm.Execute(Pool.Slice(pad.Script2Offset), VmState, arg1: 0, arg2: -1);
            }
        }
        else
        {
            pad.Flags = (byte)(pad.Flags | 0x10);
        }
    }

    /// <summary>
    /// Evaluates whether a player entity triggers <paramref name="pad"/> and, if so, executes
    /// its RPNS purchase script. Mirrors the retail walker-mode gate
    /// (<c>player-&gt;unkAC-&gt;unk40 == 0</c>) in <c>80031C74.c:102</c> (type 95) and
    /// <c>8002F8B8.c:122</c> / <c>8003151C.c:105-122</c> (type 14).
    /// </summary>
    /// <param name="pad">The purchase pad to test.</param>
    /// <param name="playerEntityId">Player entity id (12 for P1, 16 for P2 in CONFT.MIS).</param>
    /// <param name="isHoverMode">True when the player mech is in hover form (<c>unkAC-&gt;unk40 != 0</c>).</param>
    /// <param name="frameTime">Current frame counter (<c>D_8009FAA4</c>) for type-14 same-frame dedupe.</param>
    /// <param name="scoreSlot">Session score slot to observe for before/after reporting (25 = P1, 26 = P2).</param>
    public PurchaseResult TryTriggerPad(
        PurchasePad pad,
        int playerEntityId,
        bool isHoverMode,
        int frameTime = 0,
        int scoreSlot = RpnsVmState.SessionScoreP1)
    {
        ArgumentNullException.ThrowIfNull(pad);
        int before = VmState.GameSession[scoreSlot];

        if (!pad.IsEnabled)
            return new PurchaseResult(false, false, 0, before, before);

        // Retail walker-mode gate: both type-95 (80031C74.c:102) and type-14 (8002F8B8.c:122)
        // reject players whose unkAC->unk40 != 0 when flag bit 0x04 is set.
        if (pad.RequiresWalkerMode && isHoverMode)
            return new PurchaseResult(false, false, 0, before, before);

        // Target entity id match: type-95 node->id (80031DE8.c:124) and type-14 selector 2 (8002F8B8.c:76-81).
        if (pad.TargetId != -1 && pad.MatchSelector == 2 && pad.TargetId != playerEntityId)
            return new PurchaseResult(false, false, 0, before, before);

        // Type-14 same-frame dedupe (8003151C.c:115).
        if (pad.Kind == PurchasePadKind.TriggerAttachment &&
            pad.LastTriggerFrame == frameTime &&
            (pad.Flags & 0x04) != 0)
        {
            return new PurchaseResult(false, false, 0, before, before);
        }

        pad.LastTriggerFrame = frameTime;
        VmState.SessionPointerBank[RpnsVmState.PointerBankSfxSlot] = 0;
        RpnsScriptVm.Execute(Pool.Slice(pad.Script0Offset), VmState, arg1: 0, arg2: playerEntityId & 0x7FFF);
        int sfx = VmState.SessionPointerBank[RpnsVmState.PointerBankSfxSlot];
        int after = VmState.GameSession[scoreSlot];
        return new PurchaseResult(
            Triggered: true,
            Accepted: sfx == RpnsVmState.SfxPurchaseAccepted,
            SfxId: sfx,
            ScoreBefore: before,
            ScoreAfter: after);
    }

    /// <summary>
    /// Tests whether a player at (<paramref name="worldX"/>, <paramref name="worldY"/>, <paramref name="worldZ"/>)
    /// with <paramref name="radius"/> sits inside <paramref name="pad"/>'s retail trigger box
    /// (<c>80031C74.c:84-100</c> for type 95; radial/box check for type 14).
    /// </summary>
    public static bool IsInsidePad(PurchasePad pad, int worldX, int worldY, int worldZ, int radius)
    {
        ArgumentNullException.ThrowIfNull(pad);
        int dx = Math.Abs(pad.WorldX - worldX);
        int dz = Math.Abs(pad.WorldZ - worldZ);
        int dy = Math.Abs(pad.WorldY - worldY);
        return dx - radius - pad.ExtentX < 0
            && dz - radius - pad.ExtentZ < 0
            && dy - radius - pad.ExtentY < 0;
    }

    /// <summary>
    /// Steps the <c>Cfun</c> script timers (<c>func_80049564</c>, <c>80049564.c:49-82</c>) by
    /// <paramref name="elapsedTicks"/>. When a timer's trigger script returns non-zero, runs its
    /// action script (draining queued tank purchases into <see cref="CsacUnitSpawner"/> and
    /// enabling dreadnought pads once score &gt;= 50).
    /// </summary>
    public int TickTimers(int elapsedTicks)
    {
        if (_timerBytecode.Length == 0 || _timers.Count == 0)
            return 0;
        int fired = 0;
        foreach (var timer in _timers)
        {
            if (timer.Count == 0)
                continue;
            timer.Remaining -= elapsedTicks;
            if (timer.Remaining > 0)
                continue;
            timer.Remaining += timer.Period;
            if (RpnsScriptVm.Execute(_timerBytecode.AsSpan(timer.TriggerOffset), VmState) != 0)
            {
                RpnsScriptVm.Execute(_timerBytecode.AsSpan(timer.ActionOffset), VmState);
                if (timer.Count > 0)
                    timer.Count--;
                fired++;
            }
        }
        return fired;
    }
}
