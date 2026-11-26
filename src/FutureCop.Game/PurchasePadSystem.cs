using System.Buffers.Binary;
using FutureCop.Game.Mech;

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

    /// <summary>World-space centre (tACT +0x08..+0x13), with Y snapped when a surface is available.</summary>
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
    public int Script0Offset { get; set; }

    /// <summary>RPNS offset of the enable/capture script (slot 2, executed by cmd 0x7A when flags &amp; 0x20).</summary>
    public int Script2Offset { get; }

    /// <summary>Last frame stamp on which this type-14 pad fired (<c>self-&gt;unkA4</c>, <c>8003151C.c:115</c>).</summary>
    public int LastTriggerFrame { get; set; } = -1;

    /// <summary>
    /// The cached target (<c>node-&gt;target</c>, type 95, <c>80031DE8.c:131,136,178-180</c>), or null.
    /// A map zone never tests the player on its own: <c>func_80031DE8</c> picks it for the node.
    /// </summary>
    public ICsacNodeId? MapZoneTarget { get; set; }

    /// <summary>The cached target's id (<c>node-&gt;targetId</c>, <c>80031DE8.c:103,134,177</c>).</summary>
    public int MapZoneTargetId { get; set; } = -1;

    /// <summary>
    /// The re-selection timer (<c>node->t+0x16</c>, a u16: the loader stores a random byte there,
    /// 800319D4.c:142, and the walk re-arms it with <c>+= 0x258</c>, 80031DE8.c:157). An empty scan
    /// arms 600 + 600 + (rand &amp; 0x3F), a scan that found a target 600 + (rand &amp; 0x3F)
    /// (80031DE8.c:148-158) and <c>-= delta</c> of :159 wraps in 16 bits; 600 when the target is
    /// gone (80031DE8.c:104-105); the loader value is 0..255 (<c>node->t+0x16 = func_80019668() &amp;
    /// 0xFF</c>, 800319D4.c:142).
    /// </summary>
    public ushort MapZoneTimer { get; set; }

    /// <summary>True once <c>80031DE8.c:176-184</c> unlinked and freed the map-zone node.</summary>
    public bool Released { get; set; }

    /// <summary>
    /// The last box hit of this node (<c>func_80031C74</c>, 80031C74.c:106-108): the result of the
    /// node's slot-0 script with the target id <c>&amp; 0x7FFF</c> in arg2, or null before the first.
    /// Several nodes share a patch and a score slot, so this is the per-node record of a firing.
    /// </summary>
    public PurchaseResult? LastFire { get; set; }

    /// <summary>
    /// Replaces the map-zone script and flags word: <c>node-&gt;script</c> (800319D4.c:137) and the
    /// request's +0x24 word at <c>node-&gt;f18</c>. Type 95 only; the loader of 800319D4.c:137-149 does
    /// not read them yet, so a scene supplies them from its own authoring.
    /// </summary>
    public void SetMapZoneScript(int scriptOffset)
    {
        if (Kind != PurchasePadKind.MapZone) throw new InvalidOperationException($"pad {RecordId} is no map zone");
        Script0Offset = scriptOffset;
    }

    /// <summary>True when this pad's enable bit is clear (bit 0x10 clear, <c>8003151C.c:106</c>).</summary>
    public bool IsEnabled => (Flags & 0x10) == 0;

    /// <summary>True when this pad rejects hover-mode players (bit 0x04 set, <c>80031C74.c:102</c>, <c>8002F8B8.c:122</c>).</summary>
    public bool RequiresWalkerMode => (Flags & 0x04) != 0;

    private readonly int _retireScriptOffset;
    private LiveEntity? _attachment;

    /// <summary>Type-14 attachments stop triggering as soon as gRetireEntity clears their id.</summary>
    public bool IsRetired => _attachment is { RuntimeId: 0 };

    internal void SpawnAttachment(EntityWorld world) =>
        _attachment = world.Spawn(new AttachmentEntity(this) { RetireScript = _retireScriptOffset });

    // Type 95 is a map-zone node, not an entity. Type 14 uses the shared entity lifecycle;
    // keep the obj+0x24 flag word separate from the pad's +0xA0 trigger flags.
    internal sealed class AttachmentEntity : LiveEntity
    {
        public AttachmentEntity(PurchasePad pad) => Pad = pad;

        public PurchasePad Pad { get; }
        public override int RecordId => Pad.RecordId;
        public override int DispatchType => (int)PurchasePadKind.TriggerAttachment;
        public override (int X, int Z) GroundPosition => (Pad.WorldX, Pad.WorldZ);

        public override int HandleMessage(int message, EntityWorld world, object? data) =>
            BaseHandler(message, this, world, data);
    }

    public PurchasePad(
        int recordId,
        PurchasePadKind kind,
        int worldX, int worldY, int worldZ,
        int extentX, int extentY, int extentZ,
        byte flags,
        short targetId,
        byte matchSelector,
        int script0Offset,
        int script2Offset,
        int retireScriptOffset = 0)
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
        _retireScriptOffset = retireScriptOffset;
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
/// The fields the map-zone candidate scan of <c>func_80031DE8</c> (80031DE8.c:121-132) reads from a
/// target entity: <c>id</c> at +0x08, <c>nodeId</c> at +0x14, <c>type</c> at +0x16 and the position
/// <c>x</c>/<c>y</c> at +0x50/+0x58 (the struct Target80031DE8 of 80031DE8.c:6-15).
/// </summary>
public interface ICsacNodeId
{
    /// <summary>obj+0x08: the entity id a node caches as its targetId (80031DE8.c:103,121).</summary>
    int RecordId { get; }

    /// <summary>obj+0x14: the node id a map zone's node compares with (80031DE8.c:126).</summary>
    short NodeId { get; }

    /// <summary>obj+0x16: the dispatch type; 0x15 and 0x12 are never candidates (80031DE8.c:127-128).</summary>
    int DispatchType { get; }

    /// <summary>obj+0x50: world x.</summary>
    int X { get; }

    /// <summary>obj+0x5C: world y (the <c>unk54</c> field of the struct is unused).</summary>
    int Y { get; }

    /// <summary>obj+0x58: world z (read as <c>target-&gt;y</c> at 80031DE8.c:133).</summary>
    int Z { get; }

    /// <summary>obj+0x68: the model instance radius the box test of 80031C74.c:84-100 subtracts.</summary>
    int Radius { get; }

    /// <summary>
    /// <c>e-&gt;unkAC-&gt;unk40 != 0</c> for a type-1 entity (the player's control block, 80031C74.c:102):
    /// true rejects the entity in a flag-4 node and in a flag-4 type-14 pad (8002F8B8.c:122). Every
    /// other type never reads that word, so an implementation without a control block answers false.
    /// </summary>
    bool Busy => false;
}

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
        byte[]? timerBytecode = null,
        EntityWorld? world = null)
    {
        ArgumentNullException.ThrowIfNull(pool);
        Pool = pool;
        VmState = vmState ?? new RpnsVmState();
        _spawner = spawner;
        _timerBytecode = timerBytecode ?? Array.Empty<byte>();

        var entityWorld = world ?? new EntityWorld();
        if (pads is not null)
        {
            foreach (var pad in pads)
            {
                _pads.Add(pad);
                _padsById[pad.RecordId] = pad;
                if (pad.Kind == PurchasePadKind.TriggerAttachment)
                    pad.SpawnAttachment(entityWorld);
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

        // Wire op 61 (gSendSpawnerCommandByGroup, 80061600.c:55-61 -> func_800614BC):
        // compares each registered spawner's tACT +0x08 +0x1D group byte (80060E74.c:144).
        var prevSpawnerGroup = VmState.OnSpawnerCommandByGroup;
        VmState.OnSpawnerCommandByGroup = (group, cmd, arg2) =>
        {
            if (_spawner is not null)
            {
                foreach (var slot in _spawner.Slots)
                {
                    if (slot.Record.U8(0x1D) == group)
                        CsacUnitSpawner.ApplyCommand(slot, cmd, arg2);
                }
            }
            prevSpawnerGroup?.Invoke(group, cmd, arg2);
        };

        // RPNS op 56 (8004DC64.c:46-62): map zones, timers and the no-op bypass the entity list.
        var prevTag = VmState.OnCommandByTag;
        VmState.OnCommandByTag = (tag, op, arg) =>
        {
            if (op is not (0x7C or 0x7E or 0x69))
            {
                // Scripts can run during dispatch; keep the iteration stable across callbacks.
                foreach (var entity in entityWorld.Units.ToArray())
                {
                    if (entity.RuntimeId == 0 || entity.RecordId != tag) continue;

                    switch (op)
                    {
                        case 0x7A when entity is PurchasePad.AttachmentEntity attachment:
                            ApplyPadCommand7A(attachment.Pad, arg); // 80031004.c:65-78
                            break;
                        case 0x3D:
                            entity.Flags |= LiveEntity.ResetFlag; // 8004CECC.c:106-110
                            if (arg == 1) entity.Flags |= LiveEntity.NoRetireScriptFlag;
                            LiveEntity.Retire(entity, entityWorld);
                            break;
                    }
                }
            }
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
        RpnsVmState? vmState = null,
        RetailRandom? random = null,
        EntityWorld? world = null,
        Func<int, int, int, int>? groundHeight = null)
    {
        var pool = RpnsPool.Extract(missionBytes);
        if (pool is null)
            return null;

        var rng = random ?? spawner?.Random ?? new RetailRandom();
        if (vmState is null)
        {
            vmState = new RpnsVmState();
            vmState.InitRetailRandomState(rng); // 80069B58.c:47-54
        }

        // The node timers of 800319D4.c:142 draw from the loaders' generator, so the scene has to
        // take over the generator at this state to keep the retail draw order.
        byte zoneDraws = 0;
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
                        int entryCount = BinaryPrimitives.ReadInt32LittleEndian(missionBytes[e..]);
                        int rawPeriod = BinaryPrimitives.ReadInt32LittleEndian(missionBytes[(e + 4)..]);
                        // gLoadScriptTimers (80049420.c:85-92): multiply raw period by 5;
                        // negative product sets period = remaining = |t|, non-negative sets
                        // remaining = (uint)func_80019668() % (uint)period (overwriting raw +0x08).
                        int scaled = rawPeriod * 5;
                        int period;
                        int remaining;
                        if (scaled < 0)
                        {
                            period = -scaled;
                            remaining = -scaled;
                        }
                        else if (scaled > 0)
                        {
                            period = scaled;
                            remaining = (int)((uint)rng.Next() % (uint)period);
                        }
                        else
                        {
                            period = 0;
                            remaining = 0;
                        }
                        // 80049420.c:77,93-94 & 80019C38.c:60: retail adds r2 (tEXT payload base = text + 12)
                        // to trigger/action offsets to form RAM pointers. Since _timerBytecode starts at
                        // text + 12, these raw offsets already index _timerBytecode directly.
                        int triggerOffset = BinaryPrimitives.ReadInt32LittleEndian(missionBytes[(e + 12)..]);
                        int actionOffset = BinaryPrimitives.ReadInt32LittleEndian(missionBytes[(e + 16)..]);
                        timers.Add(new ScriptTimerEntry(entryCount, period, remaining, triggerOffset, actionOffset));
                    }
                }
                continue;
            }

            // Cact header: script offsets at +0x24, +0x28, +0x2C
            int s0 = (int)BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 0x24)..]);
            int s1 = (int)BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 0x28)..]);
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
                int wx = BinaryPrimitives.ReadInt32LittleEndian(src[8..]);
                int wz = BinaryPrimitives.ReadInt32LittleEndian(src[16..]);
                int sourceY = BinaryPrimitives.ReadInt32LittleEndian(src[12..]);

                // func_80031C74 runs node->script only when it is non-zero (80031C74.c:106-108), so a
                // type-95 record without a script still is a node; only its missing script is dropped.
                if (s0 != 0 && ((uint)s0 >= (uint)pool.Length || pool.Bytes[s0] == 0))
                    break;

                if (entityType == 95 && src.Length >= 0x1E)
                {
                    int rank = SurfaceRank(unchecked((sbyte)src[0x1A]));
                    int wy = groundHeight?.Invoke(wx, wz, rank) ?? sourceY;
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
                        s0, s2)
                    {
                        // 800319D4.c:142: node->t16 (u16) = func_80019668() & 0xFF, in record order.
                        MapZoneTimer = (ushort)(rng.Next() & 0xFF),
                    });
                    zoneDraws++;
                }
                else if (entityType == 14 && src.Length >= 0x2C)
                {
                    int rank = SurfaceRank(BinaryPrimitives.ReadInt16LittleEndian(src[0x26..]));
                    int wy = groundHeight?.Invoke(wx, wz, rank) ?? sourceY;
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
                        s0, s2, retireScriptOffset: s1));
                }
                break;
            }
        }

        var system = new PurchasePadSystem(pool, vmState, spawner, pads, timers, timerBytecode, world)
        {
            ZoneLoadDraws = zoneDraws,
        };
        return system;
    }

    // Both loaders select the same gQuerySurfaceHeight mode (800319D4.c:121-132, 80030DE0.c:139-150).
    private static int SurfaceRank(int surfaceType) => surfaceType switch { 0 => -1, 1 => -2, _ => surfaceType - 2 };

    /// <summary>Looks up a loaded purchase pad by its <c>Cact</c> record id.</summary>
    public PurchasePad? FindPad(int recordId) =>
        _padsById.TryGetValue(recordId, out var pad) ? pad : null;

    /// <summary>
    /// Copies the live score and total words from <see cref="MatchScore"/> into the VM's session bank.
    /// Both are updated directly on capture in retail (<c>8002D1A0.c:213-230</c>).
    /// </summary>
    public void SyncFromMatchScore(MatchScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        VmState.GameSession[RpnsVmState.SessionScoreP1] = score.Score[0];
        VmState.GameSession[RpnsVmState.SessionScoreP2] = score.Score[1];
        VmState.GameSession[31] = score.Total[0];
        VmState.GameSession[32] = score.Total[1];
    }

    /// <summary>
    /// Copies scores (<c>gGameSession[25/26]</c>, +0x64) and totals (<c>gGameSession[31/32]</c>,
    /// +0x7C) back into <see cref="MatchScore"/> after script execution (<c>8002D1A0.c:218-230</c>).
    /// </summary>
    public void SyncToMatchScore(MatchScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        score.Score[0] = VmState.GameSession[RpnsVmState.SessionScoreP1];
        score.Score[1] = VmState.GameSession[RpnsVmState.SessionScoreP2];
        score.Total[0] = VmState.GameSession[31];
        score.Total[1] = VmState.GameSession[32];
    }

    /// <summary>
    /// Copies Sky Captain <see cref="DroneStrategy.Glob"/> strategy and build-order words
    /// (<c>D_8009FA94+0x4C, +0x84, +0x88, +0x9C..+0xE0</c>, <c>800336DC.c:62-82</c>) into
    /// <see cref="RpnsVmState.GameSession"/> so retail <c>Cfun</c> script timers (e.g.,
    /// <c>CONFT.MIS</c> T0/T1/T5/T6/T32..T39) can drain the build-order words at
    /// <c>+0xB0..+0xDC</c> (indices 44..55) into <see cref="CsacUnitSpawner"/>. Score words
    /// (<c>+0x64/+0x68/+0x7C/+0x80</c>) are synced by <see cref="SyncFromMatchScore"/>, and active
    /// unit counts (<c>+0x6C..+0x78</c>, indices 27..30) stay owned by the RPNS scripts.
    /// </summary>
    public void SyncFromEconomy(DroneStrategy.Glob economy)
    {
        ArgumentNullException.ThrowIfNull(economy);
        var s = VmState.GameSession;
        if (s.Length <= 56) return;
        s[19] = economy.S4C;                          // +0x4C
        s[33] = economy.F84;                          // +0x84
        s[34] = economy.F88;                          // +0x88
        s[39] = economy.F9C;                          // +0x9C
        for (int i = 0; i < 4; i++) s[40 + i] = economy.A0[i]; // +0xA0..+0xAC
        s[44] = economy.FB0;                          // +0xB0
        s[45] = economy.FB4;                          // +0xB4
        s[46] = economy.FB8;                          // +0xB8
        s[47] = economy.FBC;                          // +0xBC
        for (int i = 0; i < 4; i++)
        {
            s[48 + i * 2] = economy.C[i][0];          // +0xC0 + i*8
            s[49 + i * 2] = economy.C[i][1];          // +0xC4 + i*8
        }
        s[56] = economy.FE0;                          // +0xE0
    }

    /// <summary>
    /// Copies <see cref="RpnsVmState.GameSession"/> strategy and build-order words back into
    /// <see cref="DroneStrategy.Glob"/> after <c>Cfun</c> timers and RPNS scripts run (for instance,
    /// after <c>CONFT.MIS</c> timers zero <c>GameSession[44..55]</c> upon queuing unit spawns).
    /// </summary>
    public void SyncToEconomy(DroneStrategy.Glob economy)
    {
        ArgumentNullException.ThrowIfNull(economy);
        var s = VmState.GameSession;
        if (s.Length <= 56) return;
        economy.S4C = s[19];
        economy.F84 = s[33];
        economy.F88 = s[34];
        economy.F9C = s[39];
        for (int i = 0; i < 4; i++) economy.A0[i] = s[40 + i];
        economy.FB0 = s[44];
        economy.FB4 = s[45];
        economy.FB8 = s[46];
        economy.FBC = s[47];
        for (int i = 0; i < 4; i++)
        {
            economy.C[i][0] = s[48 + i * 2];
            economy.C[i][1] = s[49 + i * 2];
        }
        economy.FE0 = s[56];
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
        if (pad.IsRetired) return;
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
    /// The candidate table D_800A8F90 the map-zone scan of 80031DE8.c:119-144 walks
    /// (<c>gCommandListHead.count</c> / <c>targets[]</c>), null entries skipped. The owning scene
    /// publishes <see cref="EntityWorld.Active"/> here each frame through
    /// <see cref="SyncTargetCandidates"/>; the walker entity D_8009FA58[0] can be listed as well
    /// (func_80031DE8 tests it as a node of node id -1, never as a candidate).
    /// </summary>
    public IReadOnlyList<ICsacNodeId?> TargetCandidates { get; set; } = Array.Empty<ICsacNodeId?>();

    /// <summary>
    /// True when the frame must rebuild <see cref="TargetCandidates"/> from the active entities and
    /// <see cref="ExtraCandidates"/>: set again by <see cref="SyncTargetCandidates"/> and by
    /// <see cref="AddTargetCandidate"/>, cleared when a caller assigns the table itself (the retail
    /// scan reads gCommandListHead.count every frame, 80031DE8.c:119, so a stale one is never walked).
    /// </summary>
    public bool TargetCandidatesStale { get; set; } = true;

    /// <summary>
    /// True while a caller keeps its own D_800A8F90 in <see cref="TargetCandidates"/> (a test scene
    /// that supplies the table); the frame then does not rewrite it from the active entities.
    /// </summary>
    public bool OwnsTable { get; set; }

    /// <summary>
    /// Further candidates of the scan D_800A8F90 that no loader can produce yet (a Csac unit spawned
    /// from a Csac template of the port), appended after the active entities and never dropped, unlike
    /// the per-frame active list.
    /// </summary>
    public List<ICsacNodeId> ExtraCandidates { get; } = new();

    /// <summary>The empty, immutable list a scene exposes before its pads exist.</summary>
    public static IList<ICsacNodeId> ReadOnlyCandidates => Array.AsReadOnly(Array.Empty<ICsacNodeId>());

    /// <summary>
    /// One byte of the shared loaders' generator for the node timer of 800319D4.c:142. The pads are
    /// created before <see cref="Random"/> receives the scene's generator, so the load counts its own
    /// draws; the scene keeps the count on the published generator.
    /// </summary>
    private int _zoneBytes;

    private int NextZoneByte() => Random.Next() & 0xFF;

    /// <summary>
    /// Generator draws the loader spent on node timers (800319D4.c:142). The scene replaces
    /// <see cref="Random"/> at load time, so it replays this many draws to keep the retail order.
    /// </summary>
    public int ZoneLoadDraws { get; private init; }

    /// <summary>PRNG of the map-zone rescan jitter (<c>func_80019668() &amp; 0x3F</c>, 80031DE8.c:149).</summary>
    public RetailRandom Random { get; set; } = new();

    /// <summary>
    /// Publishes the frame's D_800A8F90 as <see cref="TargetCandidates"/>: the active entities, then
    /// whatever an owner added with <see cref="AddTargetCandidate"/>. Entities that do not expose the
    /// scan fields (<see cref="ICsacNodeId"/>) are empty slots, which the scan skips.
    /// </summary>
    public void SyncTargetCandidates(IReadOnlyList<LiveEntity>? active)
    {
        if (OwnsTable) return;
        var extra = ExtraCandidates.Count > 0 ? ExtraCandidates.ToArray() : Array.Empty<ICsacNodeId?>();
        if (active is null)
        {
            TargetCandidates = extra;
            return;
        }
        var list = new ICsacNodeId?[active.Count + extra.Length];
        for (int i = 0; i < active.Count; i++)
            list[i] = active[i] is ICsacNodeId candidate ? candidate : null;
        extra.CopyTo(list, active.Count);
        TargetCandidates = list;
        TargetCandidatesStale = false;
    }

    /// <summary>
    /// Adds a candidate to the D_800A8F90 scan besides the active entities; a null entry is an empty
    /// slot of the retail table and is skipped by the scan.
    /// </summary>
    public void AddTargetCandidate(ICsacNodeId? candidate)
    {
        if (candidate is not null)
        {
            ExtraCandidates.Add(candidate);
            // Make the candidate visible to a scan that runs before the next frame rebuild.
            var list = new ICsacNodeId?[TargetCandidates.Count + 1];
            for (int i = 0; i < TargetCandidates.Count; i++) list[i] = TargetCandidates[i];
            list[^1] = candidate;
            TargetCandidates = list;
        }
        TargetCandidatesStale = true;
    }

    /// <summary>
    /// Re-selects the cached target of a map zone out of the scan of 80031DE8.c:119-155, the same
    /// call the per-frame walk makes when the node's timer ran out. The scene uses it for a node the
    /// walk has not reached yet in the frame (a unit placed inside its box): without it the stale
    /// loader timer (800319D4.c:142) keeps the target empty for up to 1200 ticks. Sets the jittered
    /// timer (<see cref="MapZoneTimer"/>) from <see cref="Random"/> like :149-151.
    /// </summary>
    public ICsacNodeId? RescanMapZoneTarget(PurchasePad node, int delta)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Kind != PurchasePadKind.MapZone) throw new ArgumentException($"{node.RecordId} is no map zone", nameof(node));
        if (node.Released || (node.Flags & 0x10) != 0) return node.MapZoneTarget;
        var target = SelectMapZoneTarget(node, TargetCandidates);
        if (!ReferenceEquals(node.MapZoneTarget, target))
        {
            node.MapZoneTarget = target;
            if (target is not null) node.MapZoneTargetId = target.RecordId;
        }
        // :151 arms 600 + (rand & 0x3F) and :157 adds another 600 when nothing was found; :159 subtracts
        // delta — all on the u16 node->t+0x16, so the subtraction wraps in 16 bits.
        node.MapZoneTimer = unchecked((ushort)(0x258 + (Random.Next() & 0x3F) + (target is null ? 0x258 : 0)));
        if (target is not null) node.MapZoneTimer = unchecked((ushort)(node.MapZoneTimer - delta));
        return target;
    }

    /// <summary>
    /// Runs the script of a map zone whose box test fired (<c>func_80031C74</c>, 80031C74.c:106-108):
    /// <c>gInterpretEntityScript(node-&gt;script, 0, other-&gt;unk14 &amp; 0x7FFF, 0)</c>. The VM of the C
    /// node list is the same one the pads share, so a node the node list owned needs no separate VM.
    /// The script slot is the record's Cact slot 0 (see <see cref="ResetMapZoneScript"/>); it is 0 for
    /// every zone of CONFT.MIS except 5, 234 and 240, and a 0 slot skips the call.
    /// </summary>
    public PurchaseResult FireMapZone(PurchasePad pad, ICsacNodeId target)
    {
        ArgumentNullException.ThrowIfNull(pad);
        ArgumentNullException.ThrowIfNull(target);
        int before = VmState.GameSession[RpnsVmState.SessionScoreP1];
        // 80031C74.c:106-108 runs node->script only when it is non-zero: a node without a script
        // (CONFT zone 106, slot 0 = 0) hits its box and returns 1 without touching the VM. The node
        // still passed the box test of :84-100, so it returns 1 (:115); only the script is skipped.
        // C evidence: node->script sits at +0x1C (80031C74.c:34), and gLoadMapZone (800319D4.c:104-151)
        // fills it with a constant: sw $zero, 0x1C($s1) (asm/us/main.s:40105). node+0x18 is a different
        // field, f18 = obj->f24 (asm/us/main.s:40047-40052). So in retail node->script is always 0 and
        // :106-108 never runs a script. This port models the retail node script through the record's
        // Cact slot 0 (0 for 106, 0x74 for 234, 0x6C for 5); retail itself always zeroes node->script,
        // so the Cact +0x24 -> node->script mapping is a port bridge and has no retail counterpart.
        var result = pad.Script0Offset == 0
            ? new PurchaseResult(true, false, 0, before, before)
            : RunPadScript(pad.Script0Offset, target.RecordId, before, RpnsVmState.SessionScoreP1);
        pad.LastFire = result;
        return result;
    }

    /// <summary>
    /// The candidate scan of 80031DE8.c:119-144 over D_800A8F90: every entity with a positive id
    /// (:121), <c>candidate-&gt;nodeId == node-&gt;id</c> (:122) and a type other than 0x15 or 0x12 (:123-124);
    /// of those the nearest by the unsigned octile distance <c>dx + dy - min / 2</c> (:130-140). Null
    /// slots are empty table entries. The node id of a pad is its <see cref="PurchasePad.TargetId"/>
    /// (<c>node-&gt;id</c>, tACT +0x1C, 800319D4.c:137); node -1 never enters this scan (:100).
    /// </summary>
    public static ICsacNodeId? SelectMapZoneTarget(PurchasePad node, IReadOnlyList<ICsacNodeId?> candidates)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(candidates);
        ICsacNodeId? target = null;
        int bestDist = 0;
        foreach (var candidate in candidates)
        {
            if (candidate is null) continue;
            if (candidate.RecordId <= 0) continue;                         // :121
            if (candidate.NodeId != node.TargetId) continue;               // :122
            if (candidate.DispatchType is 0x15 or 0x12) continue;          // :123-124
            // The node coordinate is a s16 world unit << 8 (:125, :127).
            int dx = Math.Abs(candidate.X - (node.WorldX << 8));            // :125-126, |dx| = s + (dx >> 31) ^ (dx >> 31)
            int dy = Math.Abs(candidate.Z - (node.WorldZ << 8));            // :127-128
            int dist = dx + dy - Math.Min(dx, dy) / 2;                      // :130-134
            if (target is null || dist < bestDist)                          // :135-140
            {
                target = candidate;
                bestDist = dist;
            }
        }
        return target;
    }

    /// <summary>
    /// Points a loaded map zone at another script and flags byte, as the Cact loader would have read
    /// them (800319D4.c:137-139: <c>node-&gt;script</c> from the request's +0x24, <c>node-&gt;flags</c>
    /// from tACT +0x1B).
    /// </summary>
    public void ResetMapZoneScript(int recordId, int scriptOffset)
    {
        var pad = FindPad(recordId) ?? throw new ArgumentException($"no pad {recordId}", nameof(recordId));
        if (pad.Kind != PurchasePadKind.MapZone) throw new ArgumentException($"{recordId} is no map zone", nameof(recordId));
        pad.SetMapZoneScript(scriptOffset);
    }

    /// <summary>
    /// Tests <paramref name="target"/> against <paramref name="pad"/>'s retail trigger box and, on a
    /// hit, executes its RPNS script (<c>func_80031C74</c>, 80031C74.c:84-108): every axis needs
    /// <c>|node &lt;&lt; 8 - target| - target radius - (extent &lt;&lt; 4) &lt; 0</c>; a busy type-1 target is
    /// rejected when the node has flag 4 (:102); the script runs with <c>arg2 = target id &amp; 0x7FFF</c>.
    /// The caller owns the target choice (the candidate scan of 80031DE8.c:119-144 for a map zone).
    /// </summary>
    /// <param name="pad">The purchase pad to test.</param>
    /// <param name="target">The target the node carries (its candidate id, 80031DE8.c:126).</param>
    /// <param name="frameTime">Current frame counter (<c>D_8009FAA4</c>) for type-14 same-frame dedupe.</param>
    /// <param name="scoreSlot">Session score slot to observe for before/after reporting (25 = P1, 26 = P2).</param>
    public PurchaseResult TryTriggerPad(
        PurchasePad pad,
        ICsacNodeId target,
        int frameTime = 0,
        int scoreSlot = RpnsVmState.SessionScoreP1)
    {
        ArgumentNullException.ThrowIfNull(pad);
        ArgumentNullException.ThrowIfNull(target);
        int before = VmState.GameSession[scoreSlot];

        if (pad.IsRetired || !pad.IsEnabled)
            return new PurchaseResult(false, false, 0, before, before);

        // Retail walker-mode gate: both type-95 (80031C74.c:102) and type-14 (8002F8B8.c:122)
        // reject a type-1 entity whose unkAC->unk40 != 0 when flag bit 0x04 is set. The type half of
        // the C test (other->unk16 == 1) applies to any type-1 entity, not only to the PlayerTarget.
        if (pad.RequiresWalkerMode && target.DispatchType == 1 && target.Busy)
            return new PurchaseResult(false, false, 0, before, before);

        // Target entity id match: type-95 node->id (80031DE8.c:126) and type-14 selector 2 (8002F8B8.c:76-81).
        if (pad.TargetId != -1 && pad.MatchSelector == 2 && pad.TargetId != target.RecordId)
            return new PurchaseResult(false, false, 0, before, before);


        // Type-14 same-frame dedupe (8003151C.c:115).
        if (pad.LastTriggerFrame == frameTime &&
            (pad.Flags & 0x04) != 0)
        {
            return new PurchaseResult(false, false, 0, before, before);
        }

        pad.LastTriggerFrame = frameTime;
        return RunPadScript(pad.Script0Offset, target.RecordId, before, scoreSlot);
    }

    /// <summary>
    /// Runs a trigger script with <c>arg1 = 0</c> and <c>arg2 = target id &amp; 0x7FFF</c>
    /// (80031C74.c:108, 8003151C.c:115-122) and reports the session score slot and SFX around it.
    /// </summary>
    private PurchaseResult RunPadScript(int offset, int targetId, int before, int scoreSlot)
    {
        VmState.SessionPointerBank[RpnsVmState.PointerBankSfxSlot] = 0;
        RpnsScriptVm.Execute(Pool.Slice(Math.Max(0, offset)), VmState, arg1: 0, arg2: targetId & 0x7FFF);
        int sfx = VmState.SessionPointerBank[RpnsVmState.PointerBankSfxSlot];
        return new PurchaseResult(
            Triggered: true,
            Accepted: sfx == RpnsVmState.SfxPurchaseAccepted,
            SfxId: sfx,
            ScoreBefore: before,
            ScoreAfter: VmState.GameSession[scoreSlot]);
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
            if (timer.Remaining >= 0) // 80049564.c:51: fires only when remaining < 0
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
