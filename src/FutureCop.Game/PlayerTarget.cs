namespace FutureCop.Game;

/// <summary>
/// The player entity of D_8009FA58 (<c>D_8009FA58[0..1]</c>, 80031DE8.c:19), id 12 (P1) and 16 (P2)
/// in CONFT.MIS. It is the only target an unassigned node (node id -1) tests (80031DE8.c:191-206)
/// and the only one whose <c>unkAC-&gt;unk40</c> (the hover/special phase) the flag-4 gate of
/// 80031C74.c:102 reads. Its <see cref="ICsacNodeId.NodeId"/> is the record id: D_8009FA58 entries
/// are ordinary type-1 entities, whose +0x14 is the id.
/// </summary>
public sealed class PlayerTarget : ICsacNodeId
{
    public PlayerTarget(int id, int x, int y, int z, int radius = 0, bool busy = false)
    {
        RecordId = id;
        X = x;
        Y = y;
        Z = z;
        Radius = radius;
        Busy = busy;
    }

    /// <summary>obj+0x14: 12 for P1 and 16 for P2 in CONFT.MIS.</summary>
    public int Id => RecordId;

    public int RecordId { get; }

    public short NodeId => (short)RecordId;

    /// <summary>obj+0x16 of a player entity.</summary>
    public int DispatchType => 1;

    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    /// <summary>obj+0x68, the model instance radius of the box test (80031C74.c:86-98).</summary>
    public int Radius { get; set; }

    /// <summary>
    /// <c>e-&gt;unkAC-&gt;unk40 != 0</c>: a flag-4 node or flag-4 type-14 pad rejects a type-1 entity that
    /// carries it (80031C74.c:102, 8002F8B8.c:122). It is exposed through
    /// <see cref="ICsacNodeId.Busy"/>, which is the gate both paths read.
    /// </summary>
    public bool Busy { get; set; }

    bool ICsacNodeId.Busy => Busy;
}
