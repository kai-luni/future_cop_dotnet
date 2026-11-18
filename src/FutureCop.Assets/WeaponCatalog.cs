using System.Buffers.Binary;

namespace FutureCop.Assets;

/// <summary>
/// One fire half of a mission's entity definition - the 0x20-byte record func_8006ACB0 reads
/// through func_8006AAEC at config + 0x04 (first trigger) or config + 0x24 (second trigger).
/// Offsets are relative to the half, as in the decomp (def + n).
/// </summary>
/// <param name="SectionId">Section id of the tACT chunk this half belongs to.</param>
/// <param name="Half">0 for the config + 0x04 half, 1 for config + 0x24.</param>
/// <param name="Action">def + 0x00: the spawner selector of func_8006ACB0's switch.</param>
/// <param name="Flags">def + 0x01: bit 0 arms the burst flag of the spawn, bits 1 and 3 shape the
/// parameter word, bit 2 stores the spawned object back into the owner state.</param>
/// <param name="ModelId">Projectile model: the first Cobj reference of the aRSL list that follows
/// the definition chunk (func_8006A918 resolves those references and func_8006ACB0 hands the first
/// one to the spawner as the model pointer).</param>
/// <param name="ObjWord00">def + 0x0C: obj + 0x00, stored at args + 0x1A - an identifier of the
/// shot, not the model.</param>
/// <param name="DamageWord">def + 0x0E: obj + 0x02, stored shifted left by four.</param>
/// <param name="SpeedWord">def + 0x12: obj + 0x06, the speed word of the projectile.</param>
/// <param name="AltSpeed">def + 0x14: obj + 0x08, non-zero selects the doubled speed scale.</param>
/// <param name="ArcWord">def + 0x16: obj + 0x0A, stored shifted left by four as the range word.</param>
/// <param name="ObjWord0E">def + 0x1A: obj + 0x0E, stored at args + 0x4C.</param>
/// <param name="ObjWord12">def + 0x1E: obj + 0x12, stored at args + 0x40.</param>
/// <param name="SoundId">def + 0x1C: sound id played by func_80046C84.</param>
/// <param name="SecondaryColumn">def + 0x1B: column of the secondary object of func_800402DC.</param>
/// <param name="CadencePeriod">def + 0x06: shots per reload cycle (func_8006BA50).</param>
/// <param name="CadenceReload">def + 0x08: reload timer added after a cycle.</param>
/// <param name="CadenceRate">def + 0x0A: reload timer added on every other shot.</param>
/// <param name="RecordOffset">File offset of the 0x44-byte definition record.</param>
/// <param name="Resources">The up to four (type, id) references of the aRSL list.</param>
public sealed record ShotDefinition(
    int SectionId, int Half, int Action, int Flags, int ModelId, int ObjWord00, int DamageWord,
    int SpeedWord, int AltSpeed, int ArcWord, int ObjWord0E, int ObjWord12, int SoundId,
    int SecondaryColumn, int CadencePeriod, int CadenceReload, int CadenceRate, int RecordOffset,
    IReadOnlyList<ResourceReference> Resources)
{
    /// <summary>
    /// Leash radius of the shot in retail position units. func_80035904:232 stores the range word
    /// shifted right by eight into unkA6, and func_80035B7C tests the octagonal distance from the
    /// home point against unkA6 &lt;&lt; 8 - so the radius is the range word itself, arc &lt;&lt; 4, and the
    /// shifted field is only its storage form. <see cref="ShotMotion.Range"/> converts it to the
    /// units the viewer draws in.
    /// </summary>
    public int Range => ArcWord << 4;

    /// <summary>Short label for the HUD and logs.</summary>
    public string Name => $"tACT {SectionId:X2}{(Half == 0 ? 'a' : 'b')}: action {Action:X2}, " +
                          $"model {ModelId}, speed {SpeedWord}, range {Range}, rec 0x{RecordOffset:X}";

    /// <summary>Per-tick step target of the mover: speed &lt;&lt; 4, or speed &lt;&lt; 1 when the
    /// alternate-speed selector is set (func_8005E47C:87-93).</summary>
    public int StepWord => AltSpeed != 0 ? SpeedWord << 1 : SpeedWord << 4;
}

/// <summary>
/// Reads the entity definitions the mission ships for its actors - the records the runtime
/// registry D_800A3340 caches (func_8006A918 copies data + 0x10 for 0x44 bytes and resolves the four
/// references of the following aRSL list) and the weapon fire path looks up by id (func_8006AAEC).
/// </summary>
public static class WeaponCatalog
{
    private const int ChunkLength = 0x60;   // tACT chunk: 12-byte header + 0x54 payload
    private const int RecordOffset = 0x10;  // payload + 0x10 = the definition record
    private const int RecordLength = 0x44;  // two 0x20-byte halves, then the reference area
    private const int MaxReferences = 4;    // func_8006A918 resolves exactly four

    /// <summary>Actions of func_8006ACB0 that create a visible projectile through a spawner.</summary>
    private static readonly HashSet<int> SpawnerActions = new()
    {
        1, 2, 3, 4, 5, 6, 8, 9, 0x12, 0x13, 0x14, 0x15,
    };

    /// <summary>
    /// Every definition half in the archive that looks like a fired shot: a spawner action, a
    /// positive speed and range, and a projectile model the mission carries. One entry per section
    /// id and half, so the first matching chunk wins like the retail registry.
    /// </summary>
    public static IReadOnlyList<ShotDefinition> Load(MisFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var drawable = new HashSet<int>(file.Models
            .Where(m => m.Vertices.TryGetValue(1, out var v) && v.Count > 0 && m.Polygons.Count > 0)
            .Select(m => m.Id));
        return Parse(file.Bytes, drawable);
    }

    /// <summary>Scanner behind <see cref="Load(MisFile)"/>, testable without a full archive.</summary>
    public static IReadOnlyList<ShotDefinition> Parse(byte[] data, IReadOnlyCollection<int> drawableModelIds)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(drawableModelIds);
        uint tact = ChunkReader.TagValue("tACT");
        uint arsl = ChunkReader.TagValue("aRSL");
        var seen = new HashSet<(int, int)>();
        var shots = new List<ShotDefinition>();
        for (int p = 0; p + 12 + RecordOffset + RecordLength <= data.Length; p += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p)) != tact) continue;
            int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 4));
            int section = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 8));
            if (length != ChunkLength || section <= 0 || section >= 0x1000) continue;
            var resources = ReadResources(data, p + length, arsl, section);
            if (resources.Count == 0) continue;
            int modelId = resources.FirstOrDefault(r => r.Type == "Cobj").Id;
            if (modelId <= 0 || !drawableModelIds.Contains(modelId)) continue;
            int record = p + 12 + RecordOffset;
            for (int half = 0; half < 2; half++)
            {
                // func_8006ACB0 reads the halves at config + 0x04 and config + 0x24, where config is
                // the copied record; the descriptor inside a half therefore sits at half + 0x0C.
                var shot = ReadHalf(data, record + 0x04 + half * 0x20, section, half, record,
                    modelId, resources);
                if (shot is null) continue;
                if (!SpawnerActions.Contains(shot.Action)) continue;
                if (shot.SpeedWord <= 0 || shot.ArcWord <= 0) continue;
                if (!seen.Add((section, half))) continue;
                shots.Add(shot);
            }
        }
        return shots;
    }

    /// <summary>The aRSL list that follows a definition chunk, exactly like func_8006A918 reads it.</summary>
    private static IReadOnlyList<ResourceReference> ReadResources(byte[] data, int offset, uint arsl,
        int section)
    {
        if (offset + 12 > data.Length) return Array.Empty<ResourceReference>();
        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)) != arsl)
            return Array.Empty<ResourceReference>();
        int size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 4));
        int id = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 8));
        if (id != section || size < 12 || offset + size > data.Length) return Array.Empty<ResourceReference>();
        var refs = new List<ResourceReference>(MaxReferences);
        for (int i = offset + 12; i + 8 <= offset + size && refs.Count < MaxReferences; i += 8)
            refs.Add(new ResourceReference(
                ChunkReader.TagName(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i))),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(i + 4))));
        return refs;
    }

    private static ShotDefinition? ReadHalf(byte[] data, int half, int section, int index, int record,
        int modelId, IReadOnlyList<ResourceReference> resources)
    {
        if (half + 0x20 > data.Length) return null;
        var s = data.AsSpan(half);
        return new ShotDefinition(
            SectionId: section,
            Half: index,
            Action: s[0x00],
            Flags: s[0x01],
            ModelId: modelId,
            ObjWord00: BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(0x0C)),
            DamageWord: BinaryPrimitives.ReadInt16LittleEndian(s.Slice(0x0E)),
            SpeedWord: BinaryPrimitives.ReadInt16LittleEndian(s.Slice(0x12)),
            AltSpeed: BinaryPrimitives.ReadInt16LittleEndian(s.Slice(0x14)),
            ArcWord: BinaryPrimitives.ReadInt16LittleEndian(s.Slice(0x16)),
            ObjWord0E: s[0x1A],
            ObjWord12: s[0x1E],
            SoundId: s[0x1C],
            SecondaryColumn: s[0x1B],
            CadencePeriod: s[0x06],
            CadenceReload: BinaryPrimitives.ReadInt16LittleEndian(s.Slice(0x08)),
            CadenceRate: BinaryPrimitives.ReadInt16LittleEndian(s.Slice(0x0A)),
            RecordOffset: record,
            Resources: resources);
    }
}
