using System.Buffers.Binary;

namespace FutureCop.Game;

/// <summary>
/// The mission's attack definition chunks, loaded into the runtime table D_800A2300 that
/// <see cref="EnemyAttackScheduler"/> and <see cref="EnemyWeaponFire"/> search by key.
/// </summary>
/// <remarks>
/// <para>
/// gLoadActorChunk dispatches payload type 0x62 to gLoadAttackDefinition
/// (gLoadActorChunk.c:115-116). That loader copies the 0x20-byte record that follows the
/// payload type word (gLoadAttackDefinition.c:70-79), scales the word pairs it owns
/// (gScaleDefinitionSubrecordFields.c:29-54: <c>(v &lt;&lt; 12) / 218</c> for the +0x08/+0x0A
/// pair, <c>v / 300</c> and <c>(v &lt;&lt; 8) / 300</c> for +0x12/+0x14) and resolves the first
/// aRSL resource word into the definition's +0x20 (gLoadAttackDefinition.c:86-95).
/// </para>
/// <para>
/// Precinct Assault missions store these chunks inline in the .MIS archive: every
/// <c>tACT</c> record with payload type 0x62 and at least a record's worth of bytes is one
/// entry. CONFT.MIS carries nine of them, keys 1..9, which is what the Sky Captain descriptors
/// (+0x58, +0x68) and the generic unit descriptor (+0x24) reference.
/// </para>
/// </remarks>
public static class AttackCatalog
{
    /// <summary>Payload byte that selects gLoadAttackDefinition (gLoadActorChunk.c:115).</summary>
    public const byte PayloadType = 0x62;

    /// <summary>Offset of the 0x20-byte record behind the payload type word (gLoadAttackDefinition.c:76-79).</summary>
    private const int RecordOffset = 0x10;

    /// <summary>Bytes of the record gLoadAttackDefinition copies (gLoadAttackDefinition.c:79).</summary>
    private const int RecordBytes = 0x20;

    /// <summary>
    /// Scans a mission archive for its 0x62 attack chunks in file order and returns the table the
    /// scheduler searches (D_800A2300, count D_8009F7F8 at 8005FF70.c:34-40).
    /// </summary>
    public static AttackDefinitionTable Load(ReadOnlySpan<byte> mission)
    {
        var entries = new List<AttackDefinition>();
        for (int p = 0; p + 0x10 + RecordOffset + RecordBytes <= mission.Length; p += 4)
        {
            if (mission[p] != 'T' || mission[p + 1] != 'C' || mission[p + 2] != 'A' || mission[p + 3] != 't') continue;
            int length = BinaryPrimitives.ReadInt32LittleEndian(mission.Slice(p + 4, 4));
            if (length < RecordOffset + RecordBytes || length > 0x200) continue;
            if (mission[p + 12] != PayloadType) continue;
            entries.Add(ReadEntry(mission.Slice(p + 12 + RecordOffset, RecordBytes)));
        }
        return new AttackDefinitionTable(entries.ToArray());
    }

    private static AttackDefinition ReadEntry(ReadOnlySpan<byte> record)
    {
        short Scaled12(short word) => (short)((word << 12) / 218);      // gScaleDefinitionSubrecordFields.c:45-54
        return new AttackDefinition
        {
            Key = record[0x00],
            Type = record[0x01],
            W02 = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x02, 2)),
            W06 = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x06, 2)),
            W08 = Scaled12(BinaryPrimitives.ReadInt16LittleEndian(record.Slice(0x08, 2))),
            W0A = Scaled12(BinaryPrimitives.ReadInt16LittleEndian(record.Slice(0x0A, 2))),
            W0C = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x0C, 2)),
            // The +0x12/+0x14 pair is scaled by gScaleDefinitionSubrecordFields.c:45-54 as a
            // (v / 300) and (v << 8) / 300 pair - the two speed words the mover accelerates between
            // (func_80035904:198-199, func_80035B7C:437).
            W12 = (ushort)(BinaryPrimitives.ReadInt16LittleEndian(record.Slice(0x12, 2)) / 300),
            W14 = (ushort)((BinaryPrimitives.ReadInt16LittleEndian(record.Slice(0x14, 2)) << 8) / 300),
            W16 = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x16, 2)),
            W1A = record[0x1A],
            W1B = record[0x1B],
            W1C = record[0x1C],
            W1F = record[0x1F],
            // The aRSL resource list of the same section: PA fills it with nulls, so the resolved
            // word stays 0 and the fire path skips its model spawn (8005F6E8.c:414,424-429).
            ResolvedW20 = 0,
        };
    }
}
