using System.Buffers.Binary;

namespace FutureCop.Assets;

/// <summary>
/// Huffman decode tables for the movie block stream (port of func_80089A88).
/// The code assignment is fixed by the format; the symbol values come from the
/// movie's VLC0 chunk, which lists 0xE0 16-bit symbols.
/// </summary>
public sealed class WveVlcTable
{
    /// <summary>Number of symbols a VLC0 chunk carries.</summary>
    public const int SymbolCount = 0xE0;

    /// <summary>Entries of the first (13-bit) length table as read by the decoder.</summary>
    public const int LengthCountA = 0x2000;

    /// <summary>Symbol slots of table A, reached through both table halves.</summary>
    public const int ValueCountA = 0x2000;

    /// <summary>Entries of the second (9-bit) length table.</summary>
    public const int LengthCountB = 0x200;

    /// <summary>Symbol slots of table B.</summary>
    public const int ValueCountB = 0x200;

    private const int SymbolsFirstHalf = 0x60;

    /// <summary>Bytes of the short code table (0x60 entries).</summary>
    private const int ShortCodeBytes = 0x60 * 4;

    /// <summary>Bytes of the long code table (0x80 entries).</summary>
    private const int LongCodeBytes = 0x80 * 4;

    /// <summary>
    /// Code assignment of the format: the 0x60 short codes (D_8009C1E0) followed by
    /// the 0x80 long codes (D_8009C360) of the retail image, four bytes per entry.
    /// </summary>
    private static readonly byte[] CodeTable = Convert.FromBase64String(
        "AgAAgAMAAMADAADgBAAAYAQAAHAFAABABQAASAUAAFAFAABYBgAAKAYAACwGAAA4BgAAPAYAADAG" +
        "AAA0BwAAGAcAABoHAAAcBwAAHgcAABQHAAAWBwAAEAcAABIGAAAECAAADAgAAA0IAAAICAAACQgA" +
        "AA4IAAAPCAAACggAAAsJAAAmCQCAJgkAACEJAIAhCQAAJQkAgCUJAAAkCQCAJAkAACcJAIAnCQAA" +
        "IwkAgCMJAAAiCQCAIgkAACAJAIAgCwCAAgsAoAILAAADCwAgAwsAwAILAOACCwDAAwsA4AMLAEAC" +
        "CwBgAgsAgAMLAKADCwBAAwsAYAMLAAACCwAgAg0A0AENANgBDQCAAQ0AiAENADABDQA4AQ0AAAEN" +
        "AAgBDQCwAQ0AuAENAEABDQBIAQ0AwAENAMgBDQAgAQ0AKAENAOABDQDoAQ0AUAENAFgBDQAQAQ0A" +
        "GAENAPABDQD4AQ0AoAENAKgBDQCQAQ0AmAENAHABDQB4AQ0AYAENAGgBBgAA0AYAANQGAADIBgAA" +
        "zAYAAMAGAADEBgAAuAYAALwGAACwBgAAtAYAAKgGAACsBgAAoAYAAKQGAACYBgAAnAYAAJAGAACU" +
        "BgAAiAYAAIwGAACABgAAhAYAAPgGAAD8BgAA8AYAAPQGAADoBgAA7AYAAOAGAADkBgAA2AYAANwH" +
        "AAB8BwAAfgcAAHgHAAB6BwAAdAcAAHYHAABwBwAAcgcAAGwHAABuBwAAaAcAAGoHAABkBwAAZgcA" +
        "AGAHAABiBwAAXAcAAF4HAABYBwAAWgcAAFQHAABWBwAAUAcAAFIHAABMBwAATgcAAEgHAABKBwAA" +
        "RAcAAEYHAABABwAAQggAADAIAAAxCAAALggAAC8IAAAsCAAALQgAACoIAAArCAAAKAgAACkIAAAm" +
        "CAAAJwgAACQIAAAlCAAAIggAACMIAAAgCAAAIQgAAD4IAAA/CAAAPAgAAD0IAAA6CAAAOwgAADgI" +
        "AAA5CAAANggAADcIAAA0CAAANQgAADIIAAAzCQAAEwkAgBMJAAASCQCAEgkAABEJAIARCQAAEAkA" +
        "gBAJAAAUCQCAFAkAABoJAIAaCQAAGQkAgBkJAAAYCQCAGAkAABcJAIAXCQAAFgkAgBYJAAAVCQCA" +
        "FQkAAB8JAIAfCQAAHgkAgB4JAAAdCQCAHQkAABwJAIAcCQAAGwkAgBs=");

    private WveVlcTable(byte[] lengthsA, ushort[] valuesA, byte[] lengthsB, ushort[] valuesB)
    {
        LengthsA = lengthsA;
        ValuesA = valuesA;
        LengthsB = lengthsB;
        ValuesB = valuesB;
    }

    /// <summary>Code length per 13-bit prefix of table A.</summary>
    public byte[] LengthsA { get; }

    /// <summary>Symbol per 13-bit prefix of table A.</summary>
    public ushort[] ValuesA { get; }

    /// <summary>Code length per 9-bit prefix of table B.</summary>
    public byte[] LengthsB { get; }

    /// <summary>Symbol per 9-bit prefix of table B.</summary>
    public ushort[] ValuesB { get; }

    /// <summary>Extracts the 0xE0 symbols of a VLC0 chunk payload (little endian).</summary>
    public static ushort[] ReadSymbols(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SymbolCount * 2)
        {
            throw new ArgumentException("A VLC0 payload must hold 0xE0 little-endian symbols.", nameof(payload));
        }

        var symbols = new ushort[SymbolCount];
        for (int i = 0; i < SymbolCount; i++)
        {
            symbols[i] = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(i * 2, 2));
        }

        return symbols;
    }

    /// <summary>
    /// Builds both tables from a symbol list. Mirrors func_80089A88: every entry
    /// fills the slots its prefix covers, so lookups are a single indexed read.
    /// </summary>
    public static WveVlcTable Build(ReadOnlySpan<ushort> symbols)
    {
        if (symbols.Length < SymbolCount)
        {
            throw new ArgumentException("The table build needs 0xE0 symbols.", nameof(symbols));
        }

        var lengthsA = new byte[LengthCountA];
        var valuesA = new ushort[ValueCountA];
        var lengthsB = new byte[LengthCountB];
        var valuesB = new ushort[ValueCountB];

        ReadOnlySpan<byte> shortCodes = CodeTable.AsSpan(0, ShortCodeBytes);
        ReadOnlySpan<byte> longCodes = CodeTable.AsSpan(ShortCodeBytes, LongCodeBytes);
        Fill(lengthsA, valuesA, shortCodes, symbols.Slice(0, SymbolsFirstHalf), 13, 3);
        Fill(lengthsB, valuesB, longCodes, symbols.Slice(SymbolsFirstHalf, SymbolCount - SymbolsFirstHalf), 9, 7);
        return new WveVlcTable(lengthsA, valuesA, lengthsB, valuesB);
    }

    private static void Fill(byte[] lengths, ushort[] values, ReadOnlySpan<byte> codes, ReadOnlySpan<ushort> symbols, int maxBits, int codeShift)
    {
        for (int i = 0; i < symbols.Length; i++)
        {
            int bits = codes[i * 4];
            int code = BinaryPrimitives.ReadUInt16LittleEndian(codes.Slice(i * 4 + 2, 2));
            int count = 1 << (maxBits - bits);
            int @base = code >> codeShift;
            for (int j = 0; j < count; j++)
            {
                lengths[@base | j] = (byte)bits;
                values[@base | j] = symbols[i];
            }
        }
    }
}