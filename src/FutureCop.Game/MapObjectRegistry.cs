using System.Buffers.Binary;
using FutureCop.Assets;
using FutureCop.Game.Mech;

namespace FutureCop.Game;

/// <summary>
/// One entry of the 256-slot spawn-descriptor table D_800A0C70 (gSlotSpawnDescs), laid out as the
/// 0x1C-byte block func_8003BC7C (gSetSlotSpawnDescriptor) allocates from a Csfx chunk.
/// </summary>
/// <summary>One of the five 4-byte slot groups of a D_800A0C70 descriptor (block + 4..+0x14).</summary>
public readonly record struct SlotSpawnItem(byte Unk0, byte Unk1, byte Unk2, byte Unk3);

/// <summary>
/// One D_800A0C70 slot descriptor as func_8003BC7C (gSetSlotSpawnDescriptor) builds it from a Csfx
/// chunk: <see cref="ColumnKind"/> (block +0x00) is the active-column byte func_8003B838 reads,
/// <see cref="SpawnColumn"/> (block +0x01) the destruction column of func_8003B7D8,
/// <see cref="SlotFlags"/> (block +0x18) gates the random override of func_8003B4E0,
/// <see cref="OverrideColumn"/> (block +0x1A) is the column that override selects, and
/// <see cref="Items"/> (block +4..+0x14) holds the five slot groups func_8003B4E0 / func_8003B5AC read.
/// </summary>
public readonly record struct SlotSpawnDescriptor(
    byte ColumnKind, byte SpawnColumn, ushort SlotFlags = 0, byte OverrideColumn = 0,
    IReadOnlyList<SlotSpawnItem>? Items = null);

/// <summary>
/// One deferred spawn request parked by func_800402DC via func_8003FD74 when a map cell's flag
/// word satisfies <c>(flags &amp; 5) == 4</c> and the owner's +0x08 word is non-zero.
/// </summary>
public readonly record struct DeferredSpawnRequest(byte Column, int Row, MapOccupancySubject Owner, int Arg3);

/// <summary>
/// Metadata of one installed <c>D_800A7040</c> (<c>gSpawnRecordMap</c>) spawn record
/// (installed by <c>func_8003C404</c>, <c>8003C404.c:55-69</c>, and traversed by
/// <c>func_8003C590</c> / <c>func_8003C500</c> during <c>gFinishLevelLoad</c>,
/// <c>8003FAD4.c:81-82</c>).
/// </summary>
public sealed class SpawnRecordCell
{
    /// <summary>Record type byte (+0x04): 0x57..0x5A, 0x5C..0x5E (8003C590.c:9,110-125).</summary>
    public byte Type { get; init; }

    /// <summary>Column id (+0x14, 8003C404.c:57, 8003C500.c:55).</summary>
    public byte Id { get; init; }

    /// <summary>Next column link in the same bank (+0x15, 8003C590.c:12,139-151).</summary>
    public byte Next { get; init; }

    /// <summary>Word at +0x20 checked by func_8003C590 (8003C590.c:14,131).</summary>
    public int Unk20 { get; init; }

    /// <summary>Normalized s16 at +0x28 (8003C404.c:61-67, 8003C590.c:16,106).</summary>
    public short Unk28 { get; set; }

    /// <summary>Normalized s16 at +0x2A (8003C404.c:61-67, 8003C590.c:17,106).</summary>
    public short Unk2A { get; set; }

    /// <summary>s16 at +0x48 checked for type 0x5C (8003C590.c:19,112).</summary>
    public short Unk48 { get; init; }

    /// <summary>Flags halfword at +0x32 checked by func_8003C2A4 (8003C2A4.c:68,81).</summary>
    public ushort RecordFlags { get; init; }
}

/// <summary>
/// Entity fields consulted by the retail map-occupancy and spawn-gate functions
/// (func_8003B838, func_8003B7D8, func_8003BC0C, func_8003BF50, func_800402DC).
/// </summary>
public sealed class MapOccupancySubject
{
    /// <summary>Flag bit at entity +0x24 that masks D_800A0C70 lookup (func_8003B838.c:59, func_8003B7D8.c:47).</summary>
    public const uint SelfPositionedFlag = 0x40000;

    /// <summary>Entity id (+0x14).</summary>
    public int Id { get; set; }

    /// <summary>Entity kind / dispatch type byte (+0x16), read by func_8003BF50.</summary>
    public byte EntityType { get; set; }

    /// <summary>Index into D_800A0C70 (+0x20); 0 means unassigned.</summary>
    public byte TableIndex { get; set; }

    /// <summary>Entity flag word (+0x24).</summary>
    public uint Flags { get; set; }

    /// <summary>Owner word at +0x08; non-zero selects the deferred request path in func_800402DC:88.</summary>
    public int DeferSpawnWord { get; set; }

    /// <summary>Linked entity at +0xC0, followed by func_8003BF50 for kind 7.</summary>
    public MapOccupancySubject? LinkC0 { get; set; }

    /// <summary>Linked entity at +0xC8, followed by func_8003BF50 for kinds 2 and 0x0F.</summary>
    public MapOccupancySubject? LinkC8 { get; set; }
}

/// <summary>
/// Live object registered in the shared runtime registry and eligible for per-frame message-6
/// broadcast (func_8004CD6C).
/// </summary>
public interface IRegisteredMapObject
{
    /// <summary>Entity id (+0x08 in D_800A8F90 entries); entries with Id &lt;= 0 are skipped by broadcast.</summary>
    int Id { get; }

    /// <summary>Retail dispatch type byte (+0x16).</summary>
    int EntityType { get; }

    /// <summary>Dispatches a retail entity message (message 6 = per-frame tick, arg2 = dt, arg3 = 0).</summary>
    int OnMessage(int message, int arg2, int arg3);
}

/// <summary>
/// Retail entity handler identity returned by func_8002AE54 (gGetEntityHandlerForType).
/// </summary>
public enum EntityHandlerKind
{
    None = 0,
    Player,
    Type5Vehicle,
    LinkedProp,
    TumblingFlyer,
    Elevator,
    Type11,
    PlacedProp,
    Type13,
    TriggerAttachment,
    RotatingProp,
    MountedTurretCarrier,
    Mover,
    Type26,
    Type27,
    Type28MultiTurret,
    Type29,
    ForceField,
    Resupply,
    PortraitScreen,
    Type35,
    StrategicDrone,
    CameraOverride,
}

/// <summary>
/// Port of func_8002AE54 (gGetEntityHandlerForType): maps an entity dispatch type byte to its
/// retail handler identity.
/// </summary>
public static class EntityTypeDispatch
{
    /// <summary>Resolves the retail handler for <paramref name="entityType"/> (8002AE54.c:53-105).</summary>
    public static EntityHandlerKind ResolveHandler(int entityType) => entityType switch
    {
        1 => EntityHandlerKind.Player,
        5 => EntityHandlerKind.Type5Vehicle,
        6 or 8 or 30 or 33 or 36 => EntityHandlerKind.LinkedProp,
        9 => EntityHandlerKind.TumblingFlyer,
        10 => EntityHandlerKind.Elevator,
        11 => EntityHandlerKind.Type11,
        12 => EntityHandlerKind.PlacedProp,
        13 => EntityHandlerKind.Type13,
        14 or 38 => EntityHandlerKind.TriggerAttachment,
        16 => EntityHandlerKind.RotatingProp,
        20 => EntityHandlerKind.MountedTurretCarrier,
        25 => EntityHandlerKind.Mover,
        26 => EntityHandlerKind.Type26,
        27 => EntityHandlerKind.Type27,
        28 => EntityHandlerKind.Type28MultiTurret,
        29 => EntityHandlerKind.Type29,
        31 => EntityHandlerKind.ForceField,
        32 => EntityHandlerKind.Resupply,
        34 => EntityHandlerKind.PortraitScreen,
        35 => EntityHandlerKind.Type35,
        37 => EntityHandlerKind.StrategicDrone,
        80 => EntityHandlerKind.CameraOverride,
        _ => EntityHandlerKind.None,
    };
}

/// <summary>
/// Fixed-capacity 0x80-slot live-object registry modeled on D_8009F800 (0x204 bytes: count word
/// at +0x00 followed by 0x80 entry pointers; allocated by func_80060E28 and freed by func_80060E50),
/// paired with the per-frame message-6 broadcast semantics of func_8004CD6C (D_800A8F90).
/// </summary>
public sealed class LiveObjectRegistry
{
    /// <summary>Fixed slot capacity of D_8009F800 (80060E28.c:6).</summary>
    public const int Capacity = 0x80;

    /// <summary>Retail per-frame update message id (8004CD6C.c:26, 800271A4.c:180).</summary>
    public const int MessageFrameTick = 6;

    private IRegisteredMapObject?[]? _entries;
    private int _count;

    /// <summary>Whether the registry backing table is currently allocated (func_80060E28 vs func_80060E50).</summary>
    public bool IsAllocated => _entries is not null;

    /// <summary>Live count word (+0x00 of D_8009F800).</summary>
    public int Count => _count;

    /// <summary>Snapshot of currently registered entries in slot order [0..Count-1].</summary>
    public IReadOnlyList<IRegisteredMapObject> Entries
    {
        get
        {
            if (_entries is null || _count == 0) return Array.Empty<IRegisteredMapObject>();
            var result = new IRegisteredMapObject[_count];
            for (int i = 0; i < _count; i++) result[i] = _entries[i]!;
            return result;
        }
    }

    /// <summary>
    /// Allocates (or resets) the 0x80-slot table and clears the count word (func_80060E28).
    /// </summary>
    public void Allocate()
    {
        _entries = new IRegisteredMapObject?[Capacity];
        _count = 0;
    }

    /// <summary>
    /// Releases the table during between-mission teardown (func_80060E50).
    /// </summary>
    public void Free()
    {
        _entries = null;
        _count = 0;
    }

    /// <summary>
    /// Appends <paramref name="entry"/> at <c>entries[Count++]</c> if the table is allocated and
    /// below <see cref="Capacity"/>. Returns false when unallocated or full.
    /// </summary>
    public bool Register(IRegisteredMapObject entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_entries is null || _count >= Capacity) return false;
        _entries[_count++] = entry;
        return true;
    }

    /// <summary>
    /// Removes the first registered entry matching <paramref name="id"/> and compacts the tail
    /// so [0..Count-1] stays dense.
    /// </summary>
    /// <remarks>
    /// Likely: retail D_8009F800 has no confirmed general unlink helper in the audited C units
    /// (func_8006158C only looks up by id and dispatches to func_800614BC); compacting the array
    /// keeps the count-bounded iteration invariant of func_800616A0 / func_8004CD6C intact.
    /// </remarks>
    public bool Unregister(int id)
    {
        if (_entries is null) return false;
        for (int i = 0; i < _count; i++)
        {
            if (_entries[i]!.Id == id)
            {
                RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>Removes a specific registered instance, compacting [0..Count-1].</summary>
    public bool Unregister(IRegisteredMapObject entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_entries is null) return false;
        for (int i = 0; i < _count; i++)
        {
            if (ReferenceEquals(_entries[i], entry))
            {
                RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>Returns the entry at slot <paramref name="index"/> in [0..Count-1].</summary>
    public IRegisteredMapObject GetAt(int index)
    {
        if (_entries is null || (uint)index >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _entries[index]!;
    }

    /// <summary>
    /// Broadcasts <paramref name="message"/> to every registered object whose <see cref="IRegisteredMapObject.Id"/>
    /// is strictly greater than 0 (func_8004CD6C.c:48-53). Returns the number of objects invoked.
    /// </summary>
    public int BroadcastMessage(int message, int arg2, int arg3)
    {
        if (_entries is null || _count == 0) return 0;
        // Walk a snapshot of the current count so callbacks that register or unregister entries
        // do not corrupt the loop cursor.
        int snapshotCount = _count;
        var snapshot = new IRegisteredMapObject[snapshotCount];
        for (int i = 0; i < snapshotCount; i++) snapshot[i] = _entries[i]!;

        int delivered = 0;
        for (int i = 0; i < snapshotCount; i++)
        {
            var obj = snapshot[i];
            if (obj.Id > 0)
            {
                obj.OnMessage(message, arg2, arg3);
                delivered++;
            }
        }
        return delivered;
    }

    /// <summary>
    /// Message 6 over the live-object half of the entity storage (800271A4.c:180,
    /// 8004CD6C.c:41-54). gBroadcastEntityMessage walks D_800A8F90, which the port splits into this
    /// registry (map objects, Csac wrappers) and <see cref="EntityWorld"/>, so the frame broadcasts
    /// over both.
    /// </summary>
    public int BroadcastMessage6(int dt) => BroadcastMessage(MessageFrameTick, dt, 0);

    private void RemoveAt(int index)
    {
        int last = _count - 1;
        for (int i = index; i < last; i++)
            _entries![i] = _entries[i + 1];
        _entries![last] = null;
        _count = last;
    }
}

/// <summary>
/// One static scene node placed by the Cact type-0x60 branch:
/// <c>gLoadActorChunk.c:109-110</c> <c>case 0x60: gLoadDeferredSceneAsset(arg0)</c>
/// (<c>func_80084110</c>), which resolves the record's first <c>aRSL</c> resource
/// (<c>gLoadDeferredSceneAsset.c:53-57</c>) and hands the record itself to
/// <c>gSpawnSceneNode</c> (<c>func_80083A10</c>, <c>gLoadDeferredSceneAsset.c:58-59</c>).
/// </summary>
/// <remarks>
/// <para>
/// The record is the 0x30-byte spawn request <c>gSpawnSceneNode.c:35-55</c> reads, i.e. <c>tACT + 8</c>:
/// id u16 +0x00, position s32 +0x08/+0x0C/+0x10, three angles s16 +0x14/+0x16/+0x18, a lift s16 +0x1A,
/// the alignment code s8 +0x1C, flags u8 +0x1D, an animation rate u8 +0x1E, three Q6 scales
/// +0x1F..+0x21, the spin byte +0x22 and a degree value +0x23. Those are the same offsets
/// <see cref="EntityRecord"/> stores as <see cref="EntityRecord.Source"/>.
/// </para>
/// <para>
/// These are scene nodes, not live entities: retail never enters <c>func_8002AE54</c> /
/// <c>gSpawnQueuedEntityAndGetId</c> for this type, so nothing is registered in D_8009F800 and no
/// message reaches the prop. Placement is ground-snapped by
/// <c>gSpawnSceneNode.c:292-299</c> (only skipped when the alignment code is -99) and bucketed by
/// <c>(x &gt;&gt; 17, z &gt;&gt; 17)</c> (<c>gSpawnSceneNode.c:429</c>).
/// </para>
/// </remarks>
public sealed class SceneProp
{
    internal SceneProp(EntityRecord record, int modelId, int x, int y, int z)
    {
        Record = record;
        ModelId = modelId;
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>The source <c>Cact</c> record (the tACT payload that doubles as the spawn request).</summary>
    public EntityRecord Record { get; }

    /// <summary>Entity id (<c>source +0x00</c>), also the node's <c>+0x54</c> word (<c>gSpawnSceneNode.c:247</c>).</summary>
    public int Id => Record.Id;

    /// <summary>First <c>aRSL</c> <c>Cobj</c> id, the model <c>func_80019BF0</c> resolves for the node.</summary>
    public int ModelId { get; }

    /// <summary>World x of the record (<c>source +0x08</c>).</summary>
    public int X { get; }

    /// <summary>
    /// World y after the ground snap of <c>gSpawnSceneNode.c:292-299</c>: the surface selected by
    /// <see cref="AlignmentCode"/> plus <c>(s16 source +0x1A) &lt;&lt; 4</c>, or the raw source y for -99.
    /// </summary>
    public int Y { get; }

    /// <summary>World z of the record (<c>source +0x10</c>).</summary>
    public int Z { get; }

    /// <summary>
    /// Surface selector <c>source +0x1C</c> (<c>gSpawnSceneNode.c:294-299</c>): -99 keeps the source y,
    /// 0 selects the highest surface, 1 the lowest, anything else <c>code - 2</c>.
    /// </summary>
    public int AlignmentCode => unchecked((sbyte)Record.U8(0x1C));

    /// <summary>First rotation angle (<c>source +0x14</c>), the Y-axis turn of <c>gBuildRotationMatrix</c>.</summary>
    public int Yaw => unchecked((short)Record.U16(0x14));

    /// <summary>Third rotation angle (<c>source +0x18</c>), the X-axis turn (<c>gSpawnSceneNode.c:314</c>).</summary>
    public int Pitch => unchecked((short)Record.U16(0x18));

    /// <summary>Second rotation angle (<c>source +0x16</c>), the Z-axis turn (<c>gSpawnSceneNode.c:320</c>).</summary>
    public int Roll => unchecked((short)Record.U16(0x16));

    /// <summary>Animation rate <c>source +0x1E</c> (<c>gSpawnSceneNode.c:255-265</c>); 0 = static.</summary>
    public byte AnimateRate => (byte)Record.U8(0x1E);

    /// <summary>
    /// Spin rate / upright flag <c>source +0x22</c> (<c>gSpawnSceneNode.c:246,351-357</c>): a non-zero
    /// value forces the upright matrix row and drives the per-frame yaw of
    /// <c>gDrawCellWindowModels.c:184-233</c>.
    /// </summary>
    public byte Spin => (byte)Record.U8(0x22);

    /// <summary>Q6 scale of matrix row 0 (<c>source +0x1F</c>, 0x40 = 1.0).</summary>
    public byte ScaleX => (byte)Record.U8(0x1F);

    /// <summary>Q6 scale of matrix row 1 (<c>source +0x20</c>).</summary>
    public byte ScaleY => (byte)Record.U8(0x20);

    /// <summary>Q6 scale of matrix row 2 (<c>source +0x21</c>).</summary>
    public byte ScaleZ => (byte)Record.U8(0x21);

    /// <summary>Scene bucket row <c>x &gt;&gt; 17</c> (<c>gSpawnSceneNode.c:429</c>).</summary>
    public int Row => X >> 17;

    /// <summary>Scene bucket column <c>z &gt;&gt; 17</c> (<c>gSpawnSceneNode.c:429</c>).</summary>
    public int Column => Z >> 17;

    /// <summary>
    /// Node basis of <c>gSpawnSceneNode.c:326-357</c>: <c>gBuildRotationMatrix(yaw, pitch, roll)</c>,
    /// row scaling by the three Q6 bytes unless all are 0x40, then the upright correction a non-zero
    /// spin byte selects. The signed-scale random jitter of <c>:331-337</c> is not modelled; every
    /// shipped type-96 record carries 0x40/0x40/0x40.
    /// </summary>
    public void Basis(int[] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        MechWalker.RotationMatrix(Yaw, Pitch, Roll, matrix);
        if (ScaleX != 0x40 || ScaleY != 0x40 || ScaleZ != 0x40)
        {
            int[] scale = { unchecked((sbyte)ScaleX), unchecked((sbyte)ScaleY), unchecked((sbyte)ScaleZ) };
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                    matrix[row * 3 + column] = (matrix[row * 3 + column] * scale[row]) >> 6;
        }
        if (Spin != 0)
        {
            // gSpawnSceneNode.c:351-357: rows 0 and 1 lose their off-axis terms, row 1 becomes up.
            matrix[1] = 0;
            matrix[3] = 0;
            matrix[4] = 0x10000;
            matrix[5] = 0;
            matrix[7] = 0;
        }
    }
}

/// <summary>
/// Shared map-object spawn and occupancy registry in <c>FutureCop.Game</c>:
/// combines the 256-slot descriptor table D_800A0C70, the 2x256 map-cell spawn gate D_800A7040,
/// the immediate-vs-deferred registration queue (D_8009F70C / D_8009F708 / D_8009FA00[0]) with
/// load-end replay (func_8003FAD4), and the 0x80-slot live-object registry D_8009F800 with
/// per-frame message-6 broadcast (func_8004CD6C).
/// </summary>
public sealed class MapObjectRegistry
{
    /// <summary>Slot count of D_800A0C70 and column count of D_800A7040 (8003F79C.c:49,55).</summary>
    public const int SlotTableSize = 0x100;

    /// <summary>Row (bank) count of D_800A7040 (8003F79C.c:53).</summary>
    public const int SpawnGridRows = 2;

    /// <summary>Capacity of the deferred-registration node pool D_8009F70C (8003F79C.c:63).</summary>
    public const int DeferredPoolCapacity = 0x100;

    /// <summary>Fallback spawn column when func_8003B7D8 returns 0 (used by func_8002C174).</summary>
    public const byte DefaultDestructionSpawnColumn = 0x5B;

    /// <summary>
    /// Screen state values (<c>D_8009FA00[0]</c>) tested by <c>gQueueOrApplyMapOccupancy</c>
    /// (<c>8003FA34.c:56</c>).
    /// </summary>
    /// <remarks>
    /// Proven retail gate: occupancy applies immediately when <c>gDeferredRegPool == 0</c>,
    /// <c>gScreenState[0] == 4</c>, or <c>gScreenState[0] == 0xD</c>; otherwise it defers onto
    /// <c>D_8009F708</c>. State <c>4</c> is the active in-game screen loop (player input
    /// <c>800588D4.c:257</c>, HUD <c>80047F30.c:188</c>, loop guard <c>80027EE4.c:54</c>); state
    /// <c>5</c> is written on load entry (<c>80078C88.c:92</c>, <c>8007E3EC.c:90</c>). The exact
    /// meaning of state <c>0xD</c> is unproven in the audited C and is modeled solely as the
    /// verified immediate-path branch of <c>8003FA34.c:56</c>.
    /// </remarks>
    public const int ScreenStateImmediatePlay = 4;
    /// <summary>Loading screen state written before mission chunks stream (<c>80078C88.c:92</c>, <c>8007E3EC.c:90</c>).</summary>
    public const int ScreenStateLoading = 5;
    /// <summary>Second immediate-apply screen state of <c>8003FA34.c:56</c>; exact gameplay role unproven.</summary>
    public const int ScreenStateImmediateAlt = 0xD;

    /// <summary>Count of <c>D_800A1070</c> list-head words zeroed by <c>func_8003F79C</c> (<c>8003F79C.c:66-68</c>).</summary>
    public const int ListHeadCount = 7;

    private const uint TagShoc = 0x53484F43u;
    private const uint TagShdr = 0x53484452u;
    private const uint TagSdat = 0x53444154u;
    private const uint TagFill = 0x46494C4Cu;
    private const uint TagCact = 0x43616374u;
    private const uint TagCsfx = 0x43736678u;
    private const uint TagTact = 0x74414354u;
    private const int SectorStride = 0x6000;

    private readonly SlotSpawnDescriptor?[] _slotDescriptors = new SlotSpawnDescriptor?[SlotTableSize];
    private readonly int[,] _spawnRecordMap = new int[SpawnGridRows, SlotTableSize];
    private readonly SpawnRecordCell?[,] _spawnRecordCells = new SpawnRecordCell?[SpawnGridRows, SlotTableSize];
    private readonly ushort[,] _cellFlags = new ushort[SpawnGridRows, SlotTableSize];
    private readonly int[] _listHeads = new int[ListHeadCount];
    private readonly Stack<MapOccupancySubject> _deferredQueue = new();
    private readonly List<DeferredSpawnRequest> _deferredSpawnRequests = new();

    private int _deferredPoolRemaining;

    /// <summary>D_8009F310 (<c>gSpawnTableRow</c>): active bank (0 or 1) selected by Csfx 0x56 (8003F79C.c:60, 8003BFD0.c:79).</summary>
    public int SpawnTableRow { get; set; }

    /// <summary>D_800A1070[0..6] list-head words cleared by <see cref="InitMissionSpawnState"/> (8003F79C.c:66-68).</summary>
    public IReadOnlyList<int> ListHeads => _listHeads;

    /// <summary>Number of deferred registrations replayed by the most recent <see cref="FinishLevelLoad"/> call.</summary>
    public int LastLoadReplayedCount { get; private set; }

    /// <summary>D_8009F70C != 0: whether the deferred-registration node pool is active.</summary>
    public bool HasDeferralPool { get; private set; }

    /// <summary>Remaining nodes in D_8009F70C (starts at 0x100 on <see cref="InitMissionSpawnState"/>).</summary>
    public int DeferredPoolRemaining => _deferredPoolRemaining;

    /// <summary>Number of nodes currently stacked on D_8009F708.</summary>
    public int DeferredQueueCount => _deferredQueue.Count;

    /// <summary>D_8009FA00[0]: current screen/game state checked by func_8003FA34:56.</summary>
    public int ScreenState { get; set; }

    /// <summary>D_8009F31C (gBatchSpawnActive): forwarded to the object constructor by func_800402DC:93.</summary>
    public byte BatchSpawnActive { get; set; }

    /// <summary>Requests parked by func_800402DC via func_8003FD74 when (cellFlags &amp; 5) == 4.</summary>
    public IReadOnlyList<DeferredSpawnRequest> DeferredSpawnRequests => _deferredSpawnRequests;

    /// <summary>D_8009F800 live-object registry (0x80 slots + count word + message-6 broadcast).</summary>
    public LiveObjectRegistry LiveObjects { get; } = new();

    private readonly List<Elevator> _groundElevators = new(24);

    /// <summary>Placed type-10 elevators registered in the ground-object table (max 24, 8006314C.c:36-47).</summary>
    public IReadOnlyList<Elevator> Elevators => _groundElevators;

    private readonly List<SceneProp> _deferredSceneProps = new();

    /// <summary>
    /// Static scene nodes placed by the Cact type-0x60 branch (<c>gLoadActorChunk.c:109-110</c>),
    /// in archive order, after <see cref="PlaceDeferredSceneAssets"/>.
    /// </summary>
    public IReadOnlyList<SceneProp> SceneProps => _deferredSceneProps;

    /// <summary>Static terrain query for type-10 initialization; pass the record's full XYZ probe.</summary>
    public Func<Vec3, int>? GroundHeight { get; set; }

    /// <summary>
    /// Ranked surface query for the type-96 ground snap (x, z, rank) -&gt; height, the ported
    /// <c>func_80067684</c> (<see cref="LevelGround.PlacementHeight"/>) that
    /// <c>gSpawnSceneNode.c:297</c> calls with the alignment code mapped by <c>:296</c>.
    /// </summary>
    public Func<int, int, int, int>? PlacementHeight { get; set; }

    /// <summary>Resolves an object's primary Cobj model for its 3DBB collision records.</summary>
    public Func<int, Model?>? ResolveModel { get; set; }

    /// <summary>Resolves either of the two player slots for type-10 message 6.</summary>
    public Func<int, ElevatorRider?>? ReadElevatorRider { get; set; }

    /// <summary>Host bindings for type-20 dependencies, configured before message 2 if present.</summary>
    public Action<MountedTurretCarrier>? ConfigureCarrier { get; set; }

    /// <summary>
    /// Places the supported Cact actors in an allocated live registry (8004D6D4.c).
    /// Csac templates remain with the spawner until their spawn gate fires (80060E74.c, 8006125C.c).
    /// Type 0x60 is not an actor: <c>gLoadActorChunk.c:109-110</c> returns before entity creation, so
    /// its records are placed as static scene nodes by <see cref="PlaceDeferredSceneAssets"/>.
    /// </summary>
    public int PlaceMissionObjects(IReadOnlyList<EntityRecord> records)
    {
        int placed = 0;
        if (!LiveObjects.IsAllocated) return placed;
        foreach (var record in records)
        {
            if (LiveObjects.Count >= LiveObjectRegistry.Capacity) break;
            if (record.Container != EntityContainer.Cact) continue;
            if (CreateMissionObject(record, record.Id) is not { } actor) continue;
            LiveObjects.Register(actor);
            placed++;
        }
        return placed;
    }

    /// <summary>
    /// Port of the Cact type-0x60 branch: <c>gLoadActorChunk.c:106-110</c>
    /// <c>case 0x60: gLoadDeferredSceneAsset(arg0)</c> (<c>func_80084110</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>gLoadDeferredSceneAsset.c:53-59</c> resolves the record's <c>LSRa</c> list and its first entry
    /// with <c>func_80019BF0(entry.group, entry.name)</c>, then calls
    /// <c>gSpawnSceneNode(tACT + 8, resource-&gt;model, -1, 0)</c>: a static (z2 &lt; 0) node with no
    /// caller matrix, so the transform comes from the record itself. Every shipped type-0x60 record
    /// carries exactly one <c>aRSL</c> entry and that entry is a <c>Cobj</c> (checked over CONFT 89,
    /// HK 151, JOKE 75, SLIM 75, OVMP 0), so <see cref="EntityRecord.Model()"/> reading the first
    /// <c>Cobj</c> is that first entry. The port reproduces the placement
    /// of <c>gSpawnSceneNode.c:292-349</c>: position from +0x08/+0x10, ground snap through
    /// <see cref="PlacementHeight"/> for every alignment code except -99, lift <c>(s16 +0x1A) &lt;&lt; 4</c>,
    /// and the basis built from +0x14/+0x18/+0x16 with the Q6 row scales and the spin-byte upright
    /// correction.
    /// </para>
    /// <para>
    /// Not modelled: the 250-node pool retry of <c>gSpawnSceneNode.c:236-243</c> and the per-frame
    /// animation/spin clock of <c>gDrawCellWindowModels.c:149-238</c> (89 static props in CONFT.MIS,
    /// pool exhaustion unreachable). The 5x5 cell-window cull of that drawer is modelled by
    /// <see cref="SceneProp.Row"/> / <see cref="SceneProp.Column"/> in the viewer.
    /// </para>
    /// </remarks>
    public int PlaceDeferredSceneAssets(IReadOnlyList<EntityRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _deferredSceneProps.Clear();
        foreach (var record in records)
        {
            if (record.Container != EntityContainer.Cact) continue;
            if (record.Type != DeferredSceneAssetType) continue;
            int modelId = record.Model();
            // func_80019BF0(0, 0) finds nothing and gSpawnSceneNode would dereference null; a record
            // without a Cobj reference is not a placeable asset in the shipped archives.
            if (modelId == 0) continue;
            int x = record.X, z = record.Z;
            int y = record.Y;
            int alignment = unchecked((sbyte)record.U8(0x1C));
            if (alignment != -99)
            {
                int rank = alignment switch { 0 => -1, 1 => -2, _ => alignment - 2 };
                // gSpawnSceneNode.c:296-298. A failed query answers 0 in retail, not the source y.
                y = (PlacementHeight?.Invoke(x, z, rank) ?? 0) + (unchecked((short)record.U16(0x1A)) << 4);
            }
            _deferredSceneProps.Add(new SceneProp(record, modelId, x, y, z));
        }
        return _deferredSceneProps.Count;
    }

    /// <summary>Cact type byte of the deferred-scene asset branch (<c>gLoadActorChunk.c:109</c>).</summary>
    public const int DeferredSceneAssetType = 0x60;

    /// <summary>First registered lift hit, for <see cref="MechWorld.GroundObjects"/> (8006320C.c:51–89).</summary>
    public GroundObjectHit? ProbeGroundObjects(Vec3 position)
    {
        foreach (var lift in _groundElevators)
        {
            if (lift.Probe(position) is { } hit) return hit;
        }
        return null;
    }

    /// <summary>
    /// Applies the active-cull bit (<c>0x20000000</c>) of <c>gCullActiveEntitiesSolo</c>
    /// (<c>8004DF28.c:142-143,182-188</c>) to placed elevators so <see cref="ProbeGroundObjects"/>
    /// (<c>8006320C.c:55</c>) sees active lifts (including <c>AlwaysActiveFlag</c> bit 8) in range.
    /// </summary>
    public void CullActiveElevators(int x, int z)
    {
        foreach (var lift in _groundElevators)
        {
            int adx = Math.Abs(lift.X - x), adz = Math.Abs(lift.Z - z);
            int dist = adx + adz - Math.Min(adx, adz) / 2;
            if ((lift.ObjectFlags & LiveEntity.AlwaysActiveFlag) != 0 || dist < EntityWorld.ActiveRange)
                lift.ObjectFlags = (lift.ObjectFlags | 0x20000000u) & 0xFDFFFFFFu;
            else
                lift.ObjectFlags &= 0xDFFFFFFFu;
        }
    }

    /// <summary>
    /// Builds the live actor for a supported Cact record. Type 0x60 never reaches this switch:
    /// <c>gLoadActorChunk.c:109-110</c> dispatches it to the deferred-scene path before entity
    /// creation (<see cref="PlaceDeferredSceneAssets"/>).
    /// </summary>
    internal IRegisteredMapObject? CreateMissionObject(EntityRecord record, int runtimeId)
    {
        switch (record.Type)
        {
            case Elevator.EntityType:
                var position = new Vec3(record.X, record.Y, record.Z);
                var lift = new Elevator(record, GroundHeight?.Invoke(position) ?? 0,
                    ResolveModel?.Invoke(record.Model()), runtimeId)
                {
                    ReadRider = slot => ReadElevatorRider?.Invoke(slot),
                    Remove = RemoveElevator,
                };
                // 80041488.c:120, 8006314C.c:36–47.
                if (_groundElevators.Count < 24) _groundElevators.Add(lift);
                return lift;
            case MountedTurretCarrier.EntityType:
                var carrier = new MountedTurretCarrier(record, runtimeId);
                ConfigureCarrier?.Invoke(carrier);
                carrier.OnMessage(2, 0, 0);
                return carrier;
            default:
                return null;
        }
    }

    private void RemoveElevator(Elevator lift)
    {
        int index = _groundElevators.IndexOf(lift);
        if (index >= 0)
        {
            // 80063194.c:51–59 replaces the removed entry with the tail.
            _groundElevators[index] = _groundElevators[^1];
            _groundElevators.RemoveAt(_groundElevators.Count - 1);
        }
        if (!LiveObjects.Unregister(lift)) LiveObjects.Unregister(lift.Id);
    }

    /// <summary>
    /// Optional factory callback invoked by <see cref="TrySpawnAtOwner"/> (func_800402DC:93) when
    /// the spawn gate passes. Parameters: (owner, column, row, arg2, arg4, batchSpawnActive) -&gt;
    /// retail spawn result (non-zero on success). Defaults to returning 1 when null.
    /// </summary>
    public Func<MapOccupancySubject?, byte, int, int, int, byte, int>? SpawnFactory { get; set; }

    /// <summary>
    /// Port of func_8003F79C (gInitMissionSpawnState, 8003F79C.c:44-69): clears all 256 slots of
    /// D_800A0C70, both 256-entry rows of D_800A7040 (and cell flags), D_8009F310, D_800A1070[0..6],
    /// clears D_8009F708, and allocates the 256-node deferral pool D_8009F70C.
    /// </summary>
    public void InitMissionSpawnState()
    {
        Array.Clear(_slotDescriptors);
        Array.Clear(_spawnRecordMap);
        Array.Clear(_spawnRecordCells);
        Array.Clear(_cellFlags);
        Array.Clear(_listHeads);
        SpawnTableRow = 0;
        LastLoadReplayedCount = 0;
        _deferredQueue.Clear();
        _deferredSpawnRequests.Clear();
        _groundElevators.Clear();
        // The scene-node pool (D_8009F934) is refilled on level load, so no type-96 node survives a reload.
        _deferredSceneProps.Clear();
        HasDeferralPool = true;
        _deferredPoolRemaining = DeferredPoolCapacity;
    }

    /// <summary>
    /// Sets D_800A0C70[<paramref name="slot"/>].
    /// Retail writers verified across <c>src/game/*.c</c>: <c>func_8003BC7C</c>
    /// (<c>gSetSlotSpawnDescriptor</c>, <c>8003BC7C.c:46</c>) is the sole non-zero writer of
    /// <c>D_800A0C70</c>, with exactly two callers: <c>func_8003BDA0</c> (<c>Cact</c> type
    /// <c>0x5B</c>, <c>8003BDA0.c:41</c>) and <c>func_8003BFD0</c> (<c>Csfx</c> chunk type
    /// <c>0x5B</c>, <c>8003BFD0.c:94</c>).
    /// </summary>
    public void SetSlotDescriptor(byte slot, SlotSpawnDescriptor? descriptor) =>
        _slotDescriptors[slot] = descriptor;

    /// <summary>Reads D_800A0C70[<paramref name="slot"/>].</summary>
    public SlotSpawnDescriptor? GetSlotDescriptor(byte slot) => _slotDescriptors[slot];

    /// <summary>
    /// Port of <c>gSetSlotSpawnDescriptor</c> (<c>func_8003BC7C</c>, <c>8003BC7C.c:37-61</c>):
    /// parses a 0x30-byte <c>tACT</c> type-0x5B payload (starting at chunk + 8) and populates
    /// <c>D_800A0C70[data[0x14]]</c>.
    /// </summary>
    public bool SetSlotDescriptorFromChunk(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x30) return false;
        byte slot = data[0x14];
        byte overrideColumn = data[0x15];
        ushort slotFlags = BinaryPrimitives.ReadUInt16LittleEndian(data[0x16..]);
        byte columnKind = data[0x18];
        byte spawnColumn = data[0x19];
        var items = new SlotSpawnItem[5];
        for (int i = 0; i < 5; i++)
        {
            int o = 0x1C + i * 4;
            items[i] = new SlotSpawnItem(data[o], data[o + 1], data[o + 2], data[o + 3]);
        }
        _slotDescriptors[slot] = new SlotSpawnDescriptor(columnKind, spawnColumn, slotFlags, overrideColumn, items);
        return true;
    }

    /// <summary>
    /// Port of <c>gInstallSpawnRecord</c> (<c>func_8003C404</c>, <c>8003C404.c:55-69</c>):
    /// clears the low byte of <c>D_800A6578[SpawnTableRow][id]</c>, normalizes <c>+0x28/+0x2A</c>,
    /// and installs the record at <c>D_800A7040[SpawnTableRow][id]</c>.
    /// </summary>
    public SpawnRecordCell? InstallSpawnRecord(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x2C || (uint)SpawnTableRow >= SpawnGridRows) return null;
        byte type = data[0x04];
        byte id = data[0x14];
        byte next = data[0x15];
        int unk20 = BinaryPrimitives.ReadInt32LittleEndian(data[0x20..]);
        short unk28 = BinaryPrimitives.ReadInt16LittleEndian(data[0x28..]);
        short unk2A = BinaryPrimitives.ReadInt16LittleEndian(data[0x2A..]);
        ushort recordFlags = data.Length >= 0x34 ? BinaryPrimitives.ReadUInt16LittleEndian(data[0x32..]) : (ushort)0;
        short unk48 = data.Length >= 0x4A ? BinaryPrimitives.ReadInt16LittleEndian(data[0x48..]) : (short)0;

        // 8003C2A4.c:68,80-83: when flags bit 0 is set and the slot already has a bit-0 entry,
        // the registrars return early without overwriting D_800A7040.
        if ((recordFlags & 1) != 0
            && _spawnRecordCells[SpawnTableRow, id] is { } existing
            && (existing.RecordFlags & 1) != 0)
        {
            return null;
        }

        // 8003C404.c:57: clear low byte of D_800A6578[row][id].
        _cellFlags[SpawnTableRow, id] = (ushort)(_cellFlags[SpawnTableRow, id] & 0xFF00);
        // 8003C404.c:61-67: normalize unk28/unk2A.
        if (unk28 == 1) unk2A = 0;
        if (unk2A <= 0)
        {
            unk2A = 0;
            unk28 = -1;
        }

        var cell = new SpawnRecordCell
        {
            Type = type,
            Id = id,
            Next = next,
            Unk20 = unk20,
            Unk28 = unk28,
            Unk2A = unk2A,
            Unk48 = unk48,
            RecordFlags = recordFlags,
        };
        _spawnRecordCells[SpawnTableRow, id] = cell;
        _spawnRecordMap[SpawnTableRow, id] = 1;
        return cell;
    }

    /// <summary>Returns the <see cref="SpawnRecordCell"/> installed at D_800A7040[row][column], or null.</summary>
    public SpawnRecordCell? GetSpawnRecordCell(int row, byte column)
    {
        ValidateRow(row);
        return _spawnRecordCells[row, column];
    }

    /// <summary>
    /// Port of <c>gParseCsfxChunks</c> (<c>func_8003BFD0</c>, <c>8003BFD0.c:44-117</c>): walks a
    /// <c>Csfx</c> SDAT chunk stream and dispatches types <c>0x56..0x5E</c> to update
    /// <see cref="SpawnTableRow"/>, <c>D_800A0C70</c> (<c>0x5B</c>), and <c>D_800A7040</c>.
    /// </summary>
    public bool ParseCsfxChunks(ReadOnlySpan<byte> payload)
    {
        int oldRow = SpawnTableRow;
        SpawnTableRow = 0;
        int offset = 0;
        while (offset + 13 <= payload.Length)
        {
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 4)..]);
            if (size < 13 || offset + size > payload.Length) return false;
            var data = payload.Slice(offset + 8, size - 8);
            byte type = data[4];
            if (type < 0x56 || type > 0x5E) return false;
            switch (type)
            {
                case 0x56:
                    if (data.Length >= 0x18)
                        SpawnTableRow = BinaryPrimitives.ReadInt32LittleEndian(data[0x14..]);
                    break;
                case 0x5B:
                    SetSlotDescriptorFromChunk(data);
                    break;
                default:
                    InstallSpawnRecord(data);
                    break;
            }
            offset += size;
        }
        SpawnTableRow = oldRow;
        return true;
    }

    /// <summary>
    /// Loads <c>D_800A0C70</c> slot descriptors and <c>D_800A7040</c> spawn records from a mission
    /// archive's <c>Csfx</c> streams (<c>8003C110.c</c> -&gt; <c>8003BFD0.c</c>) and <c>Cact</c>
    /// definition chunks (<c>0x57..0x5E</c>, including <c>8003BDA0.c</c> for <c>0x5B</c>) in file
    /// order. Returns the number of descriptors and spawn records installed.
    /// </summary>
    public int LoadFromMission(ReadOnlySpan<byte> missionBytes)
    {
        int loaded = 0;
        for (int off = 0; off + 0x30 <= missionBytes.Length; off += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[off..]) != TagShoc ||
                BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 0x10)..]) != TagShdr)
            {
                continue;
            }

            uint tag = BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 0x18)..]);
            if (tag != TagCsfx && tag != TagCact) continue;

            int headerLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 4)..]);
            if (headerLen < 0x30 || off + headerLen > missionBytes.Length) continue;
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
            int pktEnd = Math.Min(missionBytes.Length,
                pkt + (int)BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(pkt + 4)..]));
            if (pktEnd <= pkt + 20) continue;

            if (tag == TagCsfx)
            {
                if (ParseCsfxChunks(missionBytes.Slice(pkt + 20, pktEnd - (pkt + 20))))
                    loaded++;
                continue;
            }

            // Cact: locate matching tACT chunk and handle definition types 0x57..0x5E (8004D6D4.c).
            for (int q = pkt + 20; q + 12 <= pktEnd; q += 4)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[q..]) != TagTact ||
                    BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(q + 8)..]) != reqId)
                {
                    continue;
                }
                int tlen = (int)BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(q + 4)..]);
                if (tlen < 0x18 || q + tlen > pktEnd) break;
                var data = missionBytes.Slice(q + 8, tlen - 8);
                byte type = data[4];
                if (type == 0x5B)
                {
                    if (SetSlotDescriptorFromChunk(data)) loaded++;
                }
                else if (type is >= 0x57 and <= 0x5E)
                {
                    if (InstallSpawnRecord(data) is not null) loaded++;
                }
                break;
            }
        }
        return loaded;
    }

    /// <summary>
    /// Optional random source for the slot-0 override of <see cref="SelectEntitySlotType"/>: retail
    /// calls func_80019668 and takes bit 0. Null (the default) keeps the override off, which is the
    /// behaviour of an RNG whose low bit is clear.
    /// </summary>
    public Func<int>? RandomSource { get; set; }

    /// <summary>
    /// Port of func_8003B4E0 (gSelectEntitySlotType): the <see cref="SlotSpawnItem.Unk0"/> byte of
    /// descriptor[<paramref name="slot"/>], with the random override to
    /// <see cref="SlotSpawnDescriptor.OverrideColumn"/> when the descriptor's flag bit 0 is clear,
    /// the slot is 0, the mode is non-zero and the RNG bit 0 is set.
    /// </summary>
    public byte SelectEntitySlotType(byte index, byte slot, bool mode)
    {
        var entry = _slotDescriptors[index];
        if (!entry.HasValue) return 0;
        if ((entry.Value.SlotFlags & 1) == 0 && slot == 0 && mode &&
            (RandomSource?.Invoke() & 1) == 1)
        {
            return entry.Value.OverrideColumn;
        }
        var items = entry.Value.Items;
        if (items is null || slot >= items.Count) return 0;
        return items[slot].Unk0;
    }

    /// <summary>
    /// Port of func_8003B5AC (gGetSlotSpawnByte): <see cref="SlotSpawnItem.Unk3"/> for mode 0,
    /// <see cref="SlotSpawnItem.Unk2"/> for mode 1 and every other mode, <see cref="SlotSpawnItem.Unk1"/>
    /// for mode 2, and 0 for mode 3 or a null descriptor.
    /// </summary>
    public byte GetSlotSpawnByte(byte index, byte slot, int mode)
    {
        var entry = _slotDescriptors[index];
        if (!entry.HasValue) return 0;
        var items = entry.Value.Items;
        if (items is null || slot >= items.Count) return 0;
        return mode switch
        {
            0 => items[slot].Unk3,
            2 => items[slot].Unk1,
            3 => 0,
            _ => items[slot].Unk2,
        };
    }

    /// <summary>
    /// Port of func_8003B898 (gSpawnEntitySlotTypes): registers an entity's map occupancy in one or
    /// more grid columns. Flag 0x40000 writes the entity's own column directly; otherwise a non-null
    /// descriptor is required: mode 0 spawns its column kind, other modes spawn one column per set
    /// mask bit through <see cref="SelectEntitySlotType"/> with the special variant (mode 2), falling
    /// back to slot 0 when no mask bit hit.
    /// </summary>
    /// <param name="index">Entity's D_800A0C70 index (+0x20, passed as first C argument).</param>
    /// <param name="entity">The entity taking occupancy; supplies id, type and links for the spawn bank.</param>
    /// <param name="arg2">Second C argument, forwarded to <see cref="TrySpawnAtOwner"/>.</param>
    /// <param name="flags">Five-bit column mask, <c>(+0x19 &gt;&gt; 3) &amp; 0x1E</c> at both call sites.</param>
    /// <param name="mode">0 for the single-entry column, 2 for the retire-footprint special.</param>
    public int SpawnEntitySlotTypes(byte index, MapOccupancySubject entity, int arg2, int flags, int mode)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if ((entity.Flags & MapOccupancySubject.SelfPositionedFlag) != 0)
        {
            return TrySpawnAtOwner(entity, (byte)entity.TableIndex, arg2,
                GetEntitySpawnBank(entity), 0);
        }
        var entry = _slotDescriptors[index];
        if (!entry.HasValue) return 0;
        if (mode == 0)
        {
            // struct Entry of 8003B898/8003B4E0: flags at +0x18, this column at +0x1A.
            if (entry.Value.OverrideColumn == 0) return 0;
            return TrySpawnAtOwner(entity, entry.Value.OverrideColumn, arg2, GetEntitySpawnBank(entity), 0);
        }
        bool special = mode == 2;
        bool found = false;
        int delivered = 0;
        for (byte i = 0; i < 5; i++)
        {
            if ((flags & 1) != 0)
            {
                byte column = SelectEntitySlotType(index, i, special);
                if (column != 0)
                {
                    delivered += TrySpawnAtOwner(entity, column, arg2, GetEntitySpawnBank(entity), 0);
                    found = true;
                }
            }
            flags >>= 1;
        }
        if (!found)
        {
            byte column = SelectEntitySlotType(index, 0, special);
            if (column != 0)
                delivered += TrySpawnAtOwner(entity, column, arg2, GetEntitySpawnBank(entity), 0);
        }
        return delivered;
    }

    /// <summary>
    /// Port of func_8003BAE4 (gRefreshEntitySlotSpawns): resolves the health quartile of the
    /// entity's s16 words at +0x1C/+0x1E and spawns one column per set mask bit through
    /// <see cref="GetSlotSpawnByte"/>, with the slot-0 fallback when no bit hit. A full-health
    /// entity (quartile 3) registers nothing, exactly like the C.
    /// </summary>
    /// <remarks>
    /// gRetireOwnedSpawnSlots (func_8003B6A4) walks the D_800A1070 spawn-slot lists, which the port
    /// does not model; there is nothing registered for it to retire, so that first call is dropped.
    /// </remarks>
    /// <param name="entity">The entity refreshing its occupancy; +0x20 selects the descriptor, the type byte the bank.</param>
    /// <param name="flags">Five-bit column mask, <c>(+0x19 &gt;&gt; 3) &amp; 0x1E</c> (8004CE04.c:61-63).</param>
    /// <param name="healthMaxWord">The two s16 words the C reads at entity +0x1C and +0x1E.</param>
    public int RefreshEntitySlotSpawns(MapOccupancySubject entity, int flags,
        (short Health, short Max) healthMaxWord)
    {
        ArgumentNullException.ThrowIfNull(entity);
        int type = LiveEntity.QuarterIndex(healthMaxWord.Health, healthMaxWord.Max);
        if (type == 3) return 0;

        int delivered = 0;
        bool found = false;
        BatchSpawnActive = 1;                                   // D_8009F31C raised for the placement
        for (byte i = 0; i < 5; i++)
        {
            if ((flags & 1) != 0)
            {
                byte column = GetSlotSpawnByte((byte)entity.TableIndex, i, type);
                if (column != 0)
                {
                    delivered += TrySpawnAtOwner(entity, column, 0, GetEntitySpawnBank(entity), 0);
                    found = true;
                }
            }
            flags >>= 1;
        }
        if (!found)
        {
            byte column = GetSlotSpawnByte((byte)entity.TableIndex, 0, type);
            if (column != 0)
                delivered += TrySpawnAtOwner(entity, column, 0, GetEntitySpawnBank(entity), 0);
        }
        BatchSpawnActive = 0;
        return delivered;
    }

    /// <summary>
    /// The occupancy view of a live entity the hooks hand over: +0x14 id, type byte, +0x20 slot and
    /// the +0x24 flag word. +0x08 is the runtime id in the entity layout, so it becomes the
    /// deferred-request owner word of func_800402DC:88. The +0xC0/+0xC8 links of
    /// func_8003BF50 have no live-entity counterpart, so the spawn bank stays 0 unless the type
    /// is 1 or 0x12.
    /// </summary>
    private static MapOccupancySubject SubjectOf(LiveEntity entity) => new()
    {
        Id = entity.RecordId,
        EntityType = unchecked((byte)entity.DispatchType),
        TableIndex = entity.SlotIndex,
        Flags = entity.Flags,
        DeferSpawnWord = entity.RuntimeId,
    };

    /// <summary>
    /// <see cref="SpawnEntitySlotTypes(byte, MapOccupancySubject, int, int, int)"/> for the retire
    /// footprint hook <c>OnRetireFootprint(entity, record, mask)</c> (func_8003B898 mode 2): the
    /// record block the C forwards as its arg2 is not ported and arg2 stays 0.
    /// </summary>
    public int SpawnEntitySlotTypes(LiveEntity entity, int flags, int mode = 2) =>
        SpawnEntitySlotTypes(entity.SlotIndex, SubjectOf(entity), 0, flags, mode);

    /// <summary>
    /// <see cref="RefreshEntitySlotSpawns(MapOccupancySubject, int, (short, short))"/> for the
    /// quartile hook <c>OnRefreshSlotSpawns(entity, mask)</c> (func_8003BAE4): the health words
    /// come from the live entity.
    /// </summary>
    public int RefreshEntitySlotSpawns(LiveEntity entity, int flags) =>
        RefreshEntitySlotSpawns(SubjectOf(entity), flags, (entity.Health, entity.MaxHealth));

    /// <summary>Sets D_800A7040[<paramref name="row"/>][<paramref name="column"/>] and clears its low flag byte (8003C404.c:57).</summary>
    public void SetSpawnCell(int row, byte column, int value)
    {
        ValidateRow(row);
        _spawnRecordMap[row, column] = value;
        _spawnRecordCells[row, column] = null;
        _cellFlags[row, column] = (ushort)(_cellFlags[row, column] & 0xFF00);
    }

    /// <summary>Reads D_800A7040[<paramref name="row"/>][<paramref name="column"/>].</summary>
    public int GetSpawnCell(int row, byte column)
    {
        ValidateRow(row);
        return _spawnRecordMap[row, column];
    }

    /// <summary>Sets the cell flag word read by func_8003B3D8 (D_800A6578) for (row, column).</summary>
    public void SetCellFlags(int row, byte column, ushort flags)
    {
        ValidateRow(row);
        _cellFlags[row, column] = flags;
    }

    /// <summary>Reads the cell flag word for (row, column).</summary>
    public ushort GetCellFlags(int row, byte column)
    {
        ValidateRow(row);
        return _cellFlags[row, column];
    }

    /// <summary>
    /// Port of func_8003B838 (gGetSpawnDescColumn): returns entry[0] of D_800A0C70[tableIndex],
    /// or 0 when tableIndex == 0, flags has bit 0x40000 set, or the slot is null.
    /// </summary>
    public byte GetSpawnDescColumn(MapOccupancySubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.TableIndex == 0) return 0;
        if ((subject.Flags & MapOccupancySubject.SelfPositionedFlag) != 0) return 0;
        var entry = _slotDescriptors[subject.TableIndex];
        return entry.HasValue ? entry.Value.ColumnKind : (byte)0;
    }

    /// <summary>
    /// Port of func_8003B7D8 (gGetDestructionSpawnColumn): returns entry[1] of D_800A0C70[tableIndex],
    /// or 0 when tableIndex == 0, flags has bit 0x40000 set, or the slot is null.
    /// </summary>
    public byte GetDestructionSpawnColumn(MapOccupancySubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.TableIndex == 0) return 0;
        if ((subject.Flags & MapOccupancySubject.SelfPositionedFlag) != 0) return 0;
        var entry = _slotDescriptors[subject.TableIndex];
        return entry.HasValue ? entry.Value.SpawnColumn : (byte)0;
    }

    /// <summary>
    /// Caller convenience matching func_8002C174: resolves <see cref="GetDestructionSpawnColumn"/>
    /// and substitutes <see cref="DefaultDestructionSpawnColumn"/> (0x5B) when the result is 0.
    /// </summary>
    public byte ResolveDestructionSpawnColumn(MapOccupancySubject subject)
    {
        byte column = GetDestructionSpawnColumn(subject);
        return column != 0 ? column : DefaultDestructionSpawnColumn;
    }

    /// <summary>
    /// Port of func_8003BF50 (gGetEntitySpawnBank): returns bank 1 for kind 1 or 0x12 directly,
    /// or when kind 7 links via +0xC0 to kind 0x12, or when kind 2/0x0F links via +0xC8 to kind
    /// 0x12; returns 0 otherwise.
    /// </summary>
    public static int GetEntitySpawnBank(MapOccupancySubject? subject)
    {
        if (subject is null) return 0;
        return subject.EntityType switch
        {
            1 or 0x12 => 1,
            7 => subject.LinkC0?.EntityType == 0x12 ? 1 : 0,
            2 or 0x0F => subject.LinkC8?.EntityType == 0x12 ? 1 : 0,
            _ => 0,
        };
    }

    /// <summary>
    /// Port of func_8003FA34 (gQueueOrApplyMapOccupancy): applies occupancy immediately when the
    /// deferral pool D_8009F70C is absent or D_8009FA00[0] is 4 or 0xD; otherwise checks
    /// func_8003B838 and prepends a node to the LIFO queue D_8009F708 if a pool node is available.
    /// Returns true when the registration was applied immediately, false when deferred or skipped.
    /// </summary>
    public bool QueueOrApplyMapOccupancy(MapOccupancySubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (!HasDeferralPool || ScreenState == ScreenStateImmediatePlay || ScreenState == ScreenStateImmediateAlt)
        {
            ApplyPrimaryOccupancy(subject);
            return true;
        }

        if (GetSpawnDescColumn(subject) != 0 && _deferredPoolRemaining > 0)
        {
            _deferredPoolRemaining--;
            _deferredQueue.Push(subject);
        }
        return false;
    }

    /// <summary>
    /// Port of func_8003BC0C (gSpawnEntityPrimaryType): refreshes an entity's map occupancy via
    /// its resolved column kind (func_8003B838) and spawn bank (func_8003BF50) through
    /// func_800402DC.
    /// </summary>
    public int ApplyPrimaryOccupancy(MapOccupancySubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.TableIndex == 0) return 0;
        byte kind = GetSpawnDescColumn(subject);
        if (kind == 0) return 0;
        return TrySpawnAtOwner(subject, kind, 0, GetEntitySpawnBank(subject), 0);
    }

    /// <summary>
    /// Port of func_800402DC (gSpawnTypeAtOwner): map-cell spawn gate.
    /// Rejects (returns 0) when D_800A7040[row][column] is 0. When (cellFlags &amp; 5) == 4, parks
    /// a deferred request if <paramref name="owner"/> and <c>owner.DeferSpawnWord</c> (+0x08) are
    /// non-zero and returns 0. Otherwise constructs/spawns via <see cref="SpawnFactory"/>.
    /// </summary>
    public int TrySpawnAtOwner(MapOccupancySubject? owner, byte column, int arg2, int row, int arg4)
    {
        ValidateRow(row);
        if (_spawnRecordMap[row, column] == 0) return 0;

        if ((_cellFlags[row, column] & 5) == 4)
        {
            if (owner is null) return 0;
            if (owner.DeferSpawnWord != 0)
                _deferredSpawnRequests.Add(new DeferredSpawnRequest(column, row, owner, 0));
            return 0;
        }

        return SpawnFactory is not null
            ? SpawnFactory(owner, column, row, arg2, arg4, BatchSpawnActive)
            : 1;
    }

    /// <summary>
    /// Final step of <c>gInitEntityBase</c> (<c>8002A5F4.c:167</c>): queues or immediately applies
    /// map occupancy for a freshly initialized <see cref="LiveEntity"/> via
    /// <see cref="QueueOrApplyMapOccupancy"/>.
    /// </summary>
    public bool QueueOrApplyEntityOccupancy(LiveEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return QueueOrApplyMapOccupancy(SubjectOf(entity));
    }

    /// <summary>
    /// Port of <c>gMarkSpawnRecordChains</c> (<c>func_8003C590</c>, <c>8003C590.c:49-162</c>) and
    /// <c>gMarkSpawnCycleFlags</c> (<c>func_8003C500</c>, <c>8003C500.c:37-56</c>): walks installed
    /// <c>D_800A7040[row][1..255]</c> chains and ORs bit 2 (<c>0x4</c>) into <c>D_800A6578</c>
    /// (<c>func_8003C4C4</c>) for qualifying nodes and closed cycles.
    /// </summary>
    public void MarkSpawnRecordChains(int row)
    {
        ValidateRow(row);
        Span<short> table = stackalloc short[SlotTableSize];
        table.Clear();

        for (int i = 1; i < SlotTableSize; i++)
        {
            if (_spawnRecordCells[row, i] is null) continue;
            if (table[i] != 0) continue;
            if ((_cellFlags[row, i] & 4) != 0) continue;

            byte current = (byte)i;
            while (current != 0)
            {
                var cell = _spawnRecordCells[row, current];
                if (cell is null) break;
                if (table[current] == 0) table[current] = (short)i;

                bool mark = cell.Unk28 == 0 && cell.Unk2A > 0;
                if (!mark)
                {
                    bool typeOk = cell.Type switch
                    {
                        0x5C => cell.Unk48 == 0,
                        0x57 or 0x58 or 0x59 => true,
                        _ => false,
                    };
                    mark = typeOk && cell.Unk20 == 0;
                }
                if (mark) _cellFlags[row, current] |= 4;

                if (cell.Next != 0)
                {
                    if (_spawnRecordCells[row, cell.Next] is null) break;
                    if (table[cell.Next] == i)
                    {
                        table[current] = 0;
                        MarkSpawnCycleFlags((byte)i, current, row);
                        break;
                    }
                }
                current = cell.Next;
            }
        }
    }

    private void MarkSpawnCycleFlags(byte startColumn, byte stopId, int row)
    {
        // 8003C500.c:50-55: follow cell->next from startColumn, setting flag 4 on each successor
        // until cell->id == stopId. Guard against malformed cycles lacking stopId.
        byte col = startColumn;
        for (int guard = 0; guard <= SlotTableSize; guard++)
        {
            var cell = _spawnRecordCells[row, col];
            if (cell is null) break;
            col = cell.Next;
            _cellFlags[row, col] |= 4;
            if (cell.Id == stopId) break;
        }
    }

    /// <summary>
    /// Port of func_8003FAD4 (gFinishLevelLoad, 8003FAD4.c:76-104) chain marking, replay, and pool
    /// teardown: runs <see cref="MarkSpawnRecordChains"/> on banks 0 and 1 (:81-82), walks the LIFO
    /// queue D_8009F708 newest-first calling func_8003BC0C per node (:84-87), then releases the
    /// deferral pool D_8009F70C and clears both pool and list (:89, :97-98).
    /// Returns the number of queued nodes replayed.
    /// </summary>
    public int FinishLevelLoad()
    {
        MarkSpawnRecordChains(0);
        MarkSpawnRecordChains(1);

        int replayed = 0;
        while (_deferredQueue.Count > 0)
        {
            var subject = _deferredQueue.Pop();
            ApplyPrimaryOccupancy(subject);
            replayed++;
        }

        LastLoadReplayedCount = replayed;
        HasDeferralPool = false;
        _deferredPoolRemaining = 0;
        return replayed;
    }

    /// <summary>Broadcasts a message over the registered live actors.</summary>
    public int BroadcastMessage(int message, int arg2, int arg3) => LiveObjects.BroadcastMessage(message, arg2, arg3);

    /// <summary>Per-frame message-6 broadcast over <see cref="LiveObjects"/> (func_8004CD6C).</summary>
    public int BroadcastMessage6(int dt) => LiveObjects.BroadcastMessage6(dt);

    private static void ValidateRow(int row)
    {
        if ((uint)row >= SpawnGridRows)
            throw new ArgumentOutOfRangeException(nameof(row), $"Spawn grid row must be 0 or 1, got {row}.");
    }
}
