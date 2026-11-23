using System.Buffers.Binary;
using System.Text;

namespace FutureCop.Assets;

/// <summary>
/// Which of the three PS1 sound lookup tables a Cshd entry registers into
/// (gRegisterSoundBankByKey / func_80047344 in future-cop-lapd-ps1/src/game/80047344.c).
/// </summary>
public enum SoundTableKind
{
    /// <summary>D_800A1830: continuous/looping weapon state table (entry[7] == 1, read by gRefreshPlayerWeaponSound).</summary>
    WeaponState,
    /// <summary>D_800A1880: nonpositional one-shot sound effect table (entry[7] != 1, read by gPlayNonpositionalSoundEffect).</summary>
    Effect,
    /// <summary>D_800A18D0: high-ID table for IDs 0x46..0x81 (including FE.MIS front-end menu sounds 0x46..0x4D).</summary>
    High
}

/// <summary>
/// One decoded PS1 SPU ADPCM sample resolved via Cshd -> Cvkh -> Cvkb.
/// Pitch formula matches gConfigureSpuVoice (func_80021AD0, 80021AD0.c:72-82):
/// sampleRateHz = spuPitch * 44100.0 / 4096.0.
/// </summary>
public sealed record DecodedSoundSample(
    int GroupId,
    byte SampleIndex,
    ushort SpuPitch,
    double SampleRateHz,
    short[] Pcm,
    int? LoopStartSample,
    byte EndFlags,
    byte LeftVolume,
    byte RightVolume)
{
    /// <summary>True when the final SPU ADPCM frame has both LoopEnd (bit 0) and LoopRepeat (bit 1) set.</summary>
    public bool Loops => (EndFlags & 0x03) == 0x03 && LoopStartSample.HasValue;
}

/// <summary>
/// One logical sound bank entry (D_800A7938[slot], keyed by key &amp; 0xFF) holding up to 4 decoded sample variants.
/// </summary>
public sealed record SoundBankEntry(
    byte BankIndex,
    byte Flags4,
    byte Flags5,
    byte Flags6,
    byte Flags7,
    IReadOnlyList<DecodedSoundSample> Samples);

/// <summary>
/// Extracts and decodes retail PS1 sound effects from a .MIS archive's Cshd, Cvkh, and Cvkb chunks.
/// Follows gProcessCshdTable (800235D4.c), gResolveSoundRecordIndex (80045494.c),
/// gRegisterSoundBankByKey (80047344.c), and gConfigureSpuVoice (80021AD0.c).
/// </summary>
public sealed class SoundBank
{
    public const double SpuBaseSampleRate = 44100.0;
    public const int SpuUnityPitch = 0x1000;
    private const int AdpcmFrameBytes = 16;
    private const int AdpcmFrameSamples = 28;

    private static readonly uint ShocTag = TagValue("SHOC");
    private static readonly uint ShdrTag = TagValue("SHDR");
    private static readonly uint SdatTag = TagValue("SDAT");
    private static readonly uint FillTag = TagValue("FILL");

    private readonly Dictionary<byte, SoundBankEntry> _weaponStateTable;
    private readonly Dictionary<byte, SoundBankEntry> _effectTable;
    private readonly Dictionary<byte, SoundBankEntry> _highTable;

    private SoundBank(
        Dictionary<byte, SoundBankEntry> weaponStateTable,
        Dictionary<byte, SoundBankEntry> effectTable,
        Dictionary<byte, SoundBankEntry> highTable)
    {
        _weaponStateTable = weaponStateTable;
        _effectTable = effectTable;
        _highTable = highTable;
    }

    public IReadOnlyDictionary<byte, SoundBankEntry> WeaponStateTable => _weaponStateTable;
    public IReadOnlyDictionary<byte, SoundBankEntry> EffectTable => _effectTable;
    public IReadOnlyDictionary<byte, SoundBankEntry> HighTable => _highTable;

    /// <summary>
    /// Resolves a continuous weapon state id matching gRefreshPlayerWeaponSound (80046CF0.c:78)
    /// and gResolveSoundRecordIndex (80045494.c:47-66): ids &gt;= 0x46 redirect to D_800A18D0.
    /// </summary>
    public SoundBankEntry? LookupWeaponState(byte stateId)
    {
        if (stateId >= 0x46)
            return LookupHigh(stateId);
        return stateId > 0 && stateId < 0x47 && _weaponStateTable.TryGetValue(stateId, out var entry) ? entry : null;
    }

    /// <summary>
    /// Resolves a nonpositional effect id matching gPlayNonpositionalSoundEffect (80046C84.c:50)
    /// and gResolveSoundRecordIndex (80045494.c:47-66): ids &gt;= 0x46 redirect to D_800A18D0.
    /// </summary>
    public SoundBankEntry? LookupEffect(byte effectId)
    {
        if (effectId >= 0x46)
            return LookupHigh(effectId);
        return effectId > 0 && effectId < 0x45 && _effectTable.TryGetValue(effectId, out var entry) ? entry : null;
    }

    /// <summary>Resolves an extended/high table id in D_800A18D0 (0x46..0x81).</summary>
    public SoundBankEntry? LookupHigh(byte highId) =>
        highId >= 0x46 && highId < 0x82 && _highTable.TryGetValue(highId, out var entry) ? entry : null;

    public SoundBankEntry? Lookup(SoundTableKind table, byte id) => table switch
    {
        SoundTableKind.WeaponState => LookupWeaponState(id),
        SoundTableKind.Effect => LookupEffect(id),
        SoundTableKind.High => LookupHigh(id),
        _ => null
    };

    public static SoundBank FromMisFile(string path) => FromMisBytes(File.ReadAllBytes(path));

    public static SoundBank FromMisBytes(byte[] misBytes)
    {
        ArgumentNullException.ThrowIfNull(misBytes);

        var cvkhs = new Dictionary<int, byte[]>();
        foreach (var (id, payload) in EnumerateStreamedChunks(misBytes, "Cvkh"))
            cvkhs[id] = payload;

        var cvkbs = new Dictionary<int, byte[]>();
        foreach (var (id, payload) in EnumerateStreamedChunks(misBytes, "Cvkb"))
            cvkbs[id] = payload;

        var decodedCache = new Dictionary<(int GroupId, byte SampleIndex), DecodedSoundSample>();
        var bankSamples = new Dictionary<byte, List<DecodedSoundSample>>();
        var bankFlags = new Dictionary<byte, (byte F4, byte F5, byte F6, byte F7)>();
        var weaponStateIds = new Dictionary<byte, byte>();
        var effectIds = new Dictionary<byte, byte>();
        var highIds = new Dictionary<byte, byte>();

        foreach (var (_, pl) in EnumerateStreamedChunks(misBytes, "Cshd"))
        {
            if (pl.Length < 4) continue;
            ushort groupCount = BinaryPrimitives.ReadUInt16LittleEndian(pl.AsSpan(2, 2));
            for (int gi = 0; gi < groupCount; gi++)
            {
                int hdrOff = 4 + gi * 6;
                if (hdrOff + 6 > pl.Length) break;
                ushort groupId = BinaryPrimitives.ReadUInt16LittleEndian(pl.AsSpan(hdrOff, 2));
                ushort entryCount = BinaryPrimitives.ReadUInt16LittleEndian(pl.AsSpan(hdrOff + 2, 2));
                ushort entryOffset = BinaryPrimitives.ReadUInt16LittleEndian(pl.AsSpan(hdrOff + 4, 2));

                for (int j = 0; j < entryCount; j++)
                {
                    int recOff = entryOffset + j * 12;
                    if (recOff + 12 > pl.Length) break;
                    ushort key = BinaryPrimitives.ReadUInt16LittleEndian(pl.AsSpan(recOff, 2));
                    byte bankIndex = (byte)(key & 0xFF);
                    byte sampleIndex = pl[recOff + 2];
                    byte f4 = pl[recOff + 4];
                    byte f5 = pl[recOff + 5];
                    byte f6 = pl[recOff + 6];
                    byte f7 = pl[recOff + 7];

                    var decoded = TryDecodeSample(groupId, sampleIndex, cvkhs, cvkbs, decodedCache);
                    if (decoded != null)
                    {
                        if (!bankSamples.TryGetValue(bankIndex, out var list))
                        {
                            list = new List<DecodedSoundSample>(4);
                            bankSamples[bankIndex] = list;
                        }
                        if (list.Count < 4)
                            list.Add(decoded);
                    }

                    // Existing bank keys append samples without re-registering IDs (80047344.c:64-67).
                    if (!bankFlags.TryAdd(bankIndex, (f4, f5, f6, f7))) continue;

                    // gRegisterSoundBankByKey (80047344.c:88-103)
                    for (int k = 0; k < 4; k++)
                    {
                        byte val = pl[recOff + 8 + k];
                        if (val == 0) continue;
                        if (val >= 0x46)
                        {
                            if (val < 0x82) highIds[val] = bankIndex;
                        }
                        else if (f7 == 1)
                            weaponStateIds[val] = bankIndex;
                        else
                            effectIds[val] = bankIndex;
                    }
                }
            }
        }

        var entries = new Dictionary<byte, SoundBankEntry>();
        foreach (var (bankIndex, samples) in bankSamples)
        {
            if (samples.Count == 0) continue;
            var flags = bankFlags.GetValueOrDefault(bankIndex);
            entries[bankIndex] = new SoundBankEntry(bankIndex, flags.F4, flags.F5, flags.F6, flags.F7, samples);
        }

        var weaponStateTable = BuildTable(weaponStateIds, entries);
        var effectTable = BuildTable(effectIds, entries);
        var highTable = BuildTable(highIds, entries);
        return new SoundBank(weaponStateTable, effectTable, highTable);
    }

    private static uint TagValue(string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name);
        Array.Reverse(bytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private static IEnumerable<(int Id, byte[] Payload)> EnumerateStreamedChunks(byte[] data, string tagName)
    {
        uint targetTag = TagValue(tagName);
        for (int p = 24; p + 12 <= data.Length; p += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p, 4)) != targetTag) continue;
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p - 24, 4)) != ShocTag) continue;
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p - 8, 4)) != ShdrTag) continue;

            int id = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 4, 4));
            int payloadSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 8, 4));
            var payload = ReassembleStreamedPayload(data, p - 24, payloadSize);
            if (payload != null)
                yield return (id, payload);
        }
    }

    private static byte[]? ReassembleStreamedPayload(byte[] data, int headerStart, int payloadSize)
    {
        if (headerStart < 0 || headerStart + 8 > data.Length) return null;
        int headerLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(headerStart + 4, 4));
        if (headerLength < 28 || headerLength > data.Length - headerStart) return null;
        if (payloadSize < 0 || payloadSize > data.Length) return null;

        int packet = headerStart + headerLength;
        var payload = new byte[payloadSize];
        int copied = 0;
        while (copied < payload.Length && packet + 20 <= data.Length)
        {
            uint tag = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(packet, 4));
            int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(packet + 4, 4));
            if (length < 12 || length > data.Length - packet) return null;
            if (tag == FillTag)
            {
                packet = (int)(((long)packet / 0x6000 + 1) * 0x6000);
                continue;
            }
            if (length < 20) return null;
            uint subtype = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(packet + 16, 4));
            if (tag != ShocTag || subtype != SdatTag)
                return null;
            int toCopy = Math.Min(length - 20, payload.Length - copied);
            Buffer.BlockCopy(data, packet + 20, payload, copied, toCopy);
            copied += toCopy;
            packet += length;
        }
        return copied == payload.Length ? payload : null;
    }

    private static Dictionary<byte, SoundBankEntry> BuildTable(
        Dictionary<byte, byte> idToBank,
        Dictionary<byte, SoundBankEntry> entries)
    {
        var table = new Dictionary<byte, SoundBankEntry>();
        foreach (var (id, bankIndex) in idToBank)
        {
            if (entries.TryGetValue(bankIndex, out var entry))
                table[id] = entry;
        }
        return table;
    }

    private static DecodedSoundSample? TryDecodeSample(
        int groupId,
        byte sampleIndex,
        Dictionary<int, byte[]> cvkhs,
        Dictionary<int, byte[]> cvkbs,
        Dictionary<(int, byte), DecodedSoundSample> cache)
    {
        if (cache.TryGetValue((groupId, sampleIndex), out var cached))
            return cached;

        if (!cvkhs.TryGetValue(groupId, out var vh) || !cvkbs.TryGetValue(groupId, out var vb))
            return null;
        if (vh.Length < 0x10) return null;
        byte sampleCount = vh[4];
        if (sampleIndex >= sampleCount) return null;
        int tableOff = 0x10 + sampleIndex * 4;
        if (tableOff + 4 > vh.Length) return null;
        int entryOff = BinaryPrimitives.ReadInt32LittleEndian(vh.AsSpan(tableOff, 4));
        if (entryOff <= 0 || entryOff + 24 > vh.Length) return null;

        // Cvkh sub-entry pitch and sample offset: 80021F98.c:85, 80021AD0.c:66-77.
        int subOff = entryOff + 4;
        ushort spuPitch = BinaryPrimitives.ReadUInt16LittleEndian(vh.AsSpan(subOff + 0x0E, 2));
        int sampleOff = BinaryPrimitives.ReadInt32LittleEndian(vh.AsSpan(subOff + 0x10, 4));
        if (sampleOff < 0 || sampleOff + AdpcmFrameBytes > vb.Length) return null;

        int pos = sampleOff;
        int frames = 0;
        int? loopStartFrame = null;
        byte endFlags = 0;
        bool foundEnd = false;
        while (pos + AdpcmFrameBytes <= vb.Length)
        {
            byte flags = vb[pos + 1];
            if ((flags & 0x04) != 0 && !loopStartFrame.HasValue)
                loopStartFrame = frames;
            frames++;
            pos += AdpcmFrameBytes;
            if ((flags & 0x01) != 0)
            {
                endFlags = flags;
                foundEnd = true;
                break;
            }
        }
        if (!foundEnd || frames == 0) return null;

        short[] pcm = PsAdpcm.Decode(vb.AsSpan(sampleOff, frames * AdpcmFrameBytes));
        double sampleRateHz = spuPitch * SpuBaseSampleRate / SpuUnityPitch;
        int? loopStartSample = loopStartFrame.HasValue ? loopStartFrame.Value * AdpcmFrameSamples : null;
        var sample = new DecodedSoundSample(groupId, sampleIndex, spuPitch, sampleRateHz, pcm, loopStartSample, endFlags, vh[subOff + 1], vh[subOff + 2]);
        cache[(groupId, sampleIndex)] = sample;
        return sample;
    }
}
