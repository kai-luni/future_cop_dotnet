namespace FutureCop.Assets;

/// <summary>
/// Decodes one compressed block stream into 16-bit code words (port of
/// func_80089BBC). The stream starts with two verbatim words, a 10-bit DC prefix
/// and the bit reservoir; 0x1FF as the next block code ends the stream, 0x7C1F
/// embeds a literal word and 0xFE00 closes a block. The output always ends with
/// 64 words of 0xFE00 padding.
/// </summary>
public static class WveBlockDecoder
{
    /// <summary>Code that terminates a block stream.</summary>
    public const int EndOfStream = 0x1FF;

    /// <summary>Code word that closes a block.</summary>
    public const ushort EndOfBlock = 0xFE00;

    /// <summary>Code word that introduces a literal.</summary>
    public const ushort Literal = 0x7C1F;

    /// <summary>Trailing words the decoder appends after the last block.</summary>
    public const int TrailerWords = 64;

    /// <summary>
    /// Decodes a complete block stream. The input must hold the whole stream,
    /// including its end marker; reading past it is an error rather than retail's
    /// read of whatever follows in memory.
    /// </summary>
    public static ushort[] Decode(WveVlcTable table, ReadOnlySpan<ushort> input)
    {
        ArgumentNullException.ThrowIfNull(table);
        var reader = new BitReader(input);
        var output = new List<ushort>(256);

        output.Add(reader.NextWord());
        output.Add(reader.NextWord());
        uint prefix = (uint)reader.NextWord() << 10;
        reader.SkipWord(); // The retail stream leaves the fourth word unused.
        uint bits = ((uint)reader.NextWord() << 16) | reader.NextWord();
        int count = 0;

        uint code = bits >> 22;
        while (true)
        {
            if (code == EndOfStream)
            {
                break;
            }

            count += 10;
            bits <<= 10;
            if ((count & 0x10) != 0)
            {
                count &= 15;
                bits |= (uint)reader.NextWord() << count;
            }
            else
            {
                count &= 15;
            }

            output.Add((ushort)(code | prefix));

            while (true)
            {
                code = bits >> 19;
                byte[] lengths;
                ushort[] values;
                if (code < 32)
                {
                    count += 8;
                    bits <<= 8;
                    if ((count & 16) != 0)
                    {
                        count &= 15;
                        bits |= (uint)reader.NextWord() << count;
                    }
                    else
                    {
                        count &= 15;
                    }

                    code = bits >> 23;
                    lengths = table.LengthsB;
                    values = table.ValuesB;
                }
                else
                {
                    lengths = table.LengthsA;
                    values = table.ValuesA;
                }

                ushort value = values[code];
                int length = lengths[code];
                bits <<= length;
                count += length;
                if ((count & 16) != 0)
                {
                    count &= 15;
                    bits |= (uint)reader.NextWord() << count;
                }
                else
                {
                    count &= 15;
                }

                if (value != Literal)
                {
                    output.Add(value);
                    if (value == EndOfBlock)
                    {
                        code = bits >> 22;
                        break;
                    }

                    continue;
                }

                output.Add((ushort)(bits >> 16));
                bits <<= 16;
                bits |= (uint)reader.NextWord() << count;
            }
        }

        for (int i = 0; i < TrailerWords; i++)
        {
            output.Add(EndOfBlock);
        }

        return output.ToArray();
    }

    /// <summary>Word source that reports an overrun instead of reading unrelated memory.</summary>
    private ref struct BitReader
    {
        private readonly ReadOnlySpan<ushort> words;
        private int position;

        public BitReader(ReadOnlySpan<ushort> words)
        {
            this.words = words;
            position = 0;
        }

        public ushort NextWord()
        {
            if (position >= words.Length)
            {
                throw new InvalidDataException("The block stream ended before its 0x1FF marker.");
            }

            return words[position++];
        }

        /// <summary>Consumes one word without handing it to the decoder.</summary>
        public void SkipWord() => _ = NextWord();
    }
}