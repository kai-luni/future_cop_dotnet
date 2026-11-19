using System.Buffers.Binary;
using System.Globalization;
using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The block decoder is checked against the decomp repo's harness, which links the
/// unmodified func_80089A88 and func_80089BBC. Both sides read the same vector file:
/// one VLC0 symbol table and one complete block stream per case.
/// </summary>
public class WveBlockTests
{
    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "golden", "wve-block.vectors");

    private sealed record Row(string Id, ushort[] Input, int Words, long Sum, ushort[] Head);

    private static (ushort[] Symbols, List<Row> Rows) Load()
    {
        ushort[] symbols = Array.Empty<ushort>();
        var rows = new List<Row>();
        foreach (string raw in File.ReadAllLines(FixturePath()))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts[0] == "SYMBOLS")
            {
                symbols = ToWords(parts[1]);
                continue;
            }

            if (parts[0] != "CASE" || parts.Length < 6) throw new InvalidDataException("bad row: " + line);
            rows.Add(new Row(
                parts[1],
                ToWords(parts[2]),
                int.Parse(parts[3], CultureInfo.InvariantCulture),
                long.Parse(parts[4], CultureInfo.InvariantCulture),
                ToWords(parts[5])));
        }

        Assert.Equal(WveVlcTable.SymbolCount, symbols.Length);
        Assert.NotEmpty(rows);
        return (symbols, rows);
    }

    private static ushort[] ToWords(string hex)
    {
        byte[] bytes = Convert.FromHexString(hex);
        var words = new ushort[bytes.Length / 2];
        for (int i = 0; i < words.Length; i++) words[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2, 2));
        return words;
    }

    [Fact]
    public void SymbolTableCarriesTheExpectedSymbols()
    {
        var (symbols, _) = Load();
        Assert.Equal(0xFE00, symbols[2]); // End of block as the VLC0 chunk encodes it.
        Assert.Contains((ushort)0x7C1F, symbols); // Literal escape.
    }

    [Fact]
    public void DecodedStreamsMatchTheRetailSequence()
    {
        var (symbols, rows) = Load();
        WveVlcTable table = WveVlcTable.Build(symbols);
        foreach (Row row in rows)
        {
            ushort[] output = WveBlockDecoder.Decode(table, row.Input);
            long sum = 0;
            foreach (ushort word in output) sum += word;
            Assert.Equal(row.Words, output.Length);
            Assert.Equal(row.Sum, sum);
            Assert.Equal(row.Head, output.Take(row.Head.Length).ToArray());
        }
    }

    [Fact]
    public void TrailerIsAlwaysSixtyFourEndOfBlockWords()
    {
        var (symbols, rows) = Load();
        WveVlcTable table = WveVlcTable.Build(symbols);
        ushort[] output = WveBlockDecoder.Decode(table, rows[0].Input);
        for (int i = 0; i < WveBlockDecoder.TrailerWords; i++)
        {
            Assert.Equal(WveBlockDecoder.EndOfBlock, output[output.Length - 1 - i]);
        }
    }

    [Fact]
    public void TruncatedStreamIsRejected()
    {
        var (symbols, rows) = Load();
        WveVlcTable table = WveVlcTable.Build(symbols);
        ushort[] truncated = rows[0].Input.Take(rows[0].Input.Length / 2).ToArray();
        Assert.Throws<InvalidDataException>(() => WveBlockDecoder.Decode(table, truncated));
    }

    [Fact]
    public void BuildRejectsIncompleteSymbolLists()
    {
        Assert.Throws<ArgumentException>(() => WveVlcTable.Build(new ushort[16]));
        Assert.Throws<ArgumentException>(() => WveVlcTable.ReadSymbols(new byte[16]));
    }
}