using System.Buffers.Binary;

namespace FutureCop.Game;

/// <summary>
/// Loads the runtime definition table D_800A2300 from the <c>tACT</c> chunks of a mission archive.
/// </summary>
/// <remarks>
/// <para>
/// Port of func_80060838 (gLoadAttackDefinition): gLoadActorChunk func_8004D6D4 switches on the
/// tACT type word and calls the loader for every chunk whose type is 0x62
/// (8004D6D4.c:110-117). The loader copies 0x20 bytes of the chunk payload window into the next
/// entry of D_800A2300 (80060838.c:74-78), rescales the sub-record and the two s16 fields
/// (80060838.c:80-83 -> 800607B8.c:32-40) and resolves the up-to-four aRSL pairs of the same
/// request (80060838.c:85-95).
/// </para>
/// <para>
/// The port scans the file for those chunks instead of walking the request queue: the nine
/// definitions of CONFT.MIS sit in requests of their own and the retail dispatch key - the type
/// word at payload + 0x04 - is the only discriminator the loader needs.
/// </para>
/// </remarks>
public static class AttackDefinitionLoader
{
    // Chunk tags as the little-endian words the mission stores: 'tACT' and 'aRSL' on disk.
    private const uint Tact = 0x74414354;   // "TCAt"
    private const uint Arsl = 0x6152534C;   // "LSRa"
    private const uint CobjTag = 0x436F626A; // "jboC"

    /// <summary>tACT type word of an attack definition (8004D6D4.c:110-117, case 0x62).</summary>
    private const byte DefinitionType = 0x62;

    /// <summary>Definition entry inside the chunk: 0x10 bytes into the payload (80060838.c:74-78).</summary>
    private const int EntryOffset = 0x14;

    /// <summary>Length of the copied entry window (80060838.c:74-78).</summary>
    private const int EntryLength = 0x20;

    /// <summary>Every attack definition of <paramref name="data"/>, in file order.</summary>
    public static AttackDefinitionTable LoadFromMission(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var entries = new List<AttackDefinition>();
        for (int p = 0; p + 12 <= data.Length; p += 4)
        {
            if (Word(data, p) != Tact) continue;
            int length = (int)Word(data, p + 4);
            if (length < EntryOffset + EntryLength || p + length > data.Length) continue;
            if (data[p + 8 + 4] != DefinitionType) continue;
            int id = (int)Word(data, p + 8);
            entries.Add(ReadEntry(data, p + 8 + EntryOffset, id));
        }
        return new AttackDefinitionTable(entries.ToArray());
    }

    /// <summary>One 0x20-byte entry of D_800A2300 plus the aRSL word (80060838.c:74-95).</summary>
    private static AttackDefinition ReadEntry(byte[] data, int e, int id)
    {
        var entry = new AttackDefinition
        {
            Key = data[e],                                          // +0x00
            Type = data[e + 1],                                     // +0x01
            W02 = (ushort)Word16(data, e + 0x02),                   // +0x02
            W06 = (ushort)Word16(data, e + 0x06),                   // +0x06
            W08 = (short)(((short)Word16(data, e + 0x08) << 12) / 218), // 80060838.c:82-83
            W0A = (short)(((short)Word16(data, e + 0x0A) << 12) / 218), // 80060838.c:82-83
            W0C = (ushort)Word16(data, e + 0x0C),                   // +0x0C
            W12 = (ushort)((short)Word16(data, e + 0x12) / 300),    // 800607B8.c:32-34
            W14 = (ushort)ScaleW14(Word16(data, e + 0x14)),         // 800607B8.c:35-37
            W16 = (ushort)Word16(data, e + 0x16),                   // +0x16
            W1A = (ushort)Word16(data, e + 0x1A),                   // +0x1A
            W1B = data[e + 0x1B],                                   // +0x1B
            W1C = (ushort)Word16(data, e + 0x1C),                   // +0x1C
            W1F = data[e + 0x1F],                                   // +0x1F
            ResolvedW20 = ResolveFirstResource(data, id),           // 80060838.c:85-95
        };
        return entry;
    }

    /// <summary>800607B8.c:35-37: a zero field stays zero, a set one scales by (x &lt;&lt; 8) / 300.</summary>
    private static int ScaleW14(int value) => value == 0 ? 0 : (short)value * 256 / 300;

    /// <summary>
    /// func_80019BF0 over the aRSL list of the definition's request: four (tag, id) pairs
    /// (80060838.c:85-95). The port keeps the Cobj id of the first resolved pair instead of a host
    /// pointer; CONFT.MIS names Cobj 12 for the shared bolt of keys 1-3.
    /// </summary>
    private static int ResolveFirstResource(byte[] data, int id)
    {
        for (int p = 0; p + 12 <= data.Length; p += 4)
        {
            if (Word(data, p) != Arsl || (int)Word(data, p + 8) != id) continue;
            int size = (int)Word(data, p + 4);
            if (size < 20 || p + size > data.Length) return 0;
            for (int i = p + 12; i + 8 <= p + size; i += 8)
            {
                int name = (int)Word(data, i + 4);
                if (Word(data, i) == CobjTag && name != 0) return name;
            }
            return 0;
        }
        return 0;
    }

    private static uint Word(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));

    private static int Word16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
}
