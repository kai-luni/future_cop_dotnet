namespace FutureCop.Assets;

/// <summary>How a frame decode ended.</summary>
public enum MdecDecodeStatus
{
    /// <summary>Every macroblock was read.</summary>
    Complete,

    /// <summary>A run moved the coefficient index past 63; this block and all later blocks are zero.</summary>
    AcIndexOutOfRange,

    /// <summary>The code stream ended before the last macroblock; the unread blocks are zero.</summary>
    EndOfStream,
}

/// <summary>Header information of one block: the first code word carries qscale (top 6 bits) and DC (signed bottom 10 bits).</summary>
public readonly record struct MdecBlockHeader(int Qscale, int Dc, int CodeCount, int NonZeroCount);

/// <summary>A decoded frame: padded Y/Cb/Cr planes as produced by the IDCT, plus the RGB image.</summary>
public sealed class MdecFrame
{
    internal MdecFrame(int width, int height)
    {
        Width = width;
        Height = height;
        MacroblockColumns = (width + 15) / 16;
        MacroblockRows = (height + 15) / 16;
        LumaWidth = MacroblockColumns * 16;
        LumaHeight = MacroblockRows * 16;
        ChromaWidth = MacroblockColumns * 8;
        ChromaHeight = MacroblockRows * 8;
        Y = new int[LumaWidth * LumaHeight];
        Cb = new int[ChromaWidth * ChromaHeight];
        Cr = new int[ChromaWidth * ChromaHeight];
        Argb = new int[width * height];
    }

    /// <summary>Image width from the frame header.</summary>
    public int Width { get; }

    /// <summary>Image height from the frame header.</summary>
    public int Height { get; }

    /// <summary>Macroblocks per row.</summary>
    public int MacroblockColumns { get; }

    /// <summary>Macroblocks per column.</summary>
    public int MacroblockRows { get; }

    /// <summary>Stride and height of <see cref="Y"/> (rounded up to 16).</summary>
    public int LumaWidth { get; }

    /// <summary>Height of <see cref="Y"/>.</summary>
    public int LumaHeight { get; }

    /// <summary>Stride of <see cref="Cb"/> and <see cref="Cr"/>.</summary>
    public int ChromaWidth { get; }

    /// <summary>Height of <see cref="Cb"/> and <see cref="Cr"/>.</summary>
    public int ChromaHeight { get; }

    /// <summary>Luma after the IDCT, signed (about -128..127, unclamped).</summary>
    public int[] Y { get; }

    /// <summary>Blue chroma after the IDCT, signed, one sample per 2x2 pixels.</summary>
    public int[] Cb { get; }

    /// <summary>Red chroma after the IDCT, signed, one sample per 2x2 pixels.</summary>
    public int[] Cr { get; }

    /// <summary>Pixels as 0xAARRGGBB with alpha 0xFF (BGRA byte order in little-endian memory), row-major, Width x Height.</summary>
    public int[] Argb { get; }

    /// <summary>How decoding ended.</summary>
    public MdecDecodeStatus Status { get; internal set; }

    /// <summary>Blocks read before decoding stopped (6 per macroblock when complete).</summary>
    public int BlocksDecoded { get; internal set; }

    /// <summary>Code words consumed from the input.</summary>
    public int CodesConsumed { get; internal set; }
}

/// <summary>
/// Software model of what the PS1 MDEC chip does with the code words that
/// func_80089BBC produces (the retail code just DMAs them to the chip via
/// func_80095FF4 / func_80096070). Follows jpsxdec's integer reference
/// (MdecDecoder_int, PsxMdecIDCT_int, PsxYCbCr_int, RGB.toInt) operation for operation.
/// <para>
/// Code word layout: each block starts with qscale (top 6 bits) and DC (signed
/// bottom 10 bits); DC is multiplied by the quant matrix entry (2) but not by
/// qscale. Each following word advances the zig-zag position by top6 + 1 and,
/// when its signed level is non-zero, stores (level * quant * qscale + 4) &gt;&gt; 3.
/// 0xFE00 ends the block. Blocks come in Cr, Cb, Y1, Y2, Y3, Y4 order;
/// macroblocks run top-to-bottom within a column, columns left to right.
/// </para>
/// <para>
/// Endianness: the code words are 16-bit values in the order func_80089BBC
/// emits them, and that function reads the compressed stream as native
/// little-endian halfwords (see <see cref="WveVideoFrame.GetBlockWords"/>). Checked
/// on INTRO.WVE against jpsxdec v2.1-beta: with little-endian words all 837 frames
/// jpsxdec extracts match its "-q psx" output bit for bit (and its default "-q high"
/// output with a mean absolute RGB error of 0.78, worst frame 2.50). Reading the stream as
/// big-endian words gives first codes such as 0xA025, 0x0038, 0x000B and the model hits
/// a run past index 63 within the first dozen blocks (frame 1: after 11 blocks),
/// leaving a grey frame. The two leading words (e.g. 0x2520, 0x3800) are the MDEC
/// command 0x38002520 (decode, word count) rather than code words, and the third
/// input word (0x0002) is the qscale prefix 2 &lt;&lt; 10.
/// </para>
/// </summary>
public static class MdecFrameDecoder
{
    /// <summary>Code word that ends a block.</summary>
    public const ushort EndOfBlock = 0xFE00;

    /// <summary>Words of MDEC command header at the start of func_80089BBC's output.</summary>
    public const int CommandWords = 2;

    /// <summary>Blocks per macroblock (Cr, Cb, Y1..Y4).</summary>
    public const int BlocksPerMacroblock = 6;

    /// <summary>Default PS1 quantisation matrix in row-major (matrix) order.</summary>
    public static readonly int[] DefaultQuantizationMatrix =
    {
        2, 16, 19, 22, 26, 27, 29, 34,
        16, 16, 22, 24, 27, 29, 34, 37,
        19, 22, 26, 27, 29, 34, 34, 38,
        22, 22, 26, 27, 29, 34, 37, 40,
        22, 26, 27, 29, 32, 35, 40, 48,
        26, 27, 29, 32, 35, 40, 48, 58,
        26, 27, 29, 34, 38, 46, 56, 69,
        27, 29, 35, 38, 46, 56, 69, 83,
    };

    /// <summary>Matrix index of each zig-zag position.</summary>
    public static readonly int[] ReverseZigZag =
    {
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    };

    /// <summary>PS1 default IDCT cosine matrix (as in jpsxdec's PsxMdecIDCT_int).</summary>
    public static readonly int[] CosineMatrix =
    {
        23170, 23170, 23170, 23170, 23170, 23170, 23170, 23170,
        32138, 27245, 18204, 6392, -6393, -18205, -27246, -32139,
        30273, 12539, -12540, -30274, -30274, -12540, 12539, 30273,
        27245, -6393, -32139, -18205, 18204, 32138, 6392, -27246,
        23170, -23171, -23171, 23170, 23170, -23171, -23171, 23170,
        18204, -32139, 6392, 27245, -27246, -6393, 32138, -18205,
        12539, -30274, 30273, -12540, -12540, 30273, -30274, 12539,
        6392, -18205, 27245, -32139, 32138, -27246, 18204, -6393,
    };

    private const int FixedBits = 16;
    private const long Cr2R = 91893;
    private const long Cb2G = 22525;
    private const long Cr2G = 46812;
    private const long Cb2B = 116224;

    /// <summary>Signed value of the bottom 10 bits of a code word.</summary>
    public static int Level(ushort code)
    {
        int level = code & 0x3FF;
        return (level & 0x200) != 0 ? level - 0x400 : level;
    }

    /// <summary>
    /// Decodes the full output of <see cref="WveBlockDecoder.Decode"/> (MDEC command
    /// header, block codes, trailer) into a frame of the given size.
    /// </summary>
    public static MdecFrame DecodeDecoderOutput(ReadOnlySpan<ushort> decoderOutput, int width, int height)
    {
        if (decoderOutput.Length < CommandWords)
        {
            throw new ArgumentException("The decoder output must start with the two MDEC command words.", nameof(decoderOutput));
        }

        return Decode(decoderOutput.Slice(CommandWords), width, height);
    }

    /// <summary>
    /// Decodes block codes (no command header) into Y/Cb/Cr planes and RGB.
    /// Mirrors jpsxdec: a run past index 63 or a short stream stops decoding and
    /// leaves the remaining blocks zero (mid-grey luma, neutral chroma); the
    /// frame's <see cref="MdecFrame.Status"/> records which happened.
    /// </summary>
    public static MdecFrame Decode(ReadOnlySpan<ushort> codes, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Frame dimensions must be positive.");
        }

        var frame = new MdecFrame(width, height);
        int totalBlocks = frame.MacroblockColumns * frame.MacroblockRows * BlocksPerMacroblock;
        var coefficients = new int[64];
        var spatial = new int[64];
        int position = 0;
        int block = 0;
        frame.Status = MdecDecodeStatus.Complete;
        for (; block < totalBlocks; block++)
        {
            MdecDecodeStatus status = TryReadBlock(codes, ref position, coefficients, out MdecBlockHeader header);
            if (status != MdecDecodeStatus.Complete)
            {
                frame.Status = status;
                break;
            }

            if (header.NonZeroCount != 0)
            {
                Idct(coefficients, spatial);
                Store(frame, block, spatial);
            }
        }

        frame.BlocksDecoded = block;
        frame.CodesConsumed = Math.Min(position, codes.Length);
        ConvertToRgb(frame);
        return frame;
    }

    /// <summary>
    /// Reads one block starting at <paramref name="position"/> into dequantised
    /// coefficients (matrix order). Returns the block header; throws
    /// <see cref="InvalidDataException"/> on a run past index 63 or a short stream.
    /// </summary>
    public static MdecBlockHeader ReadBlock(ReadOnlySpan<ushort> codes, ref int position, int[] coefficients)
    {
        MdecDecodeStatus status = TryReadBlock(codes, ref position, coefficients, out MdecBlockHeader header);
        return status switch
        {
            MdecDecodeStatus.Complete => header,
            MdecDecodeStatus.AcIndexOutOfRange => throw new InvalidDataException($"AC run moved past coefficient 63 at code {position - 1}."),
            _ => throw new InvalidDataException("The code stream ended inside a block."),
        };
    }

    private static MdecDecodeStatus TryReadBlock(ReadOnlySpan<ushort> codes, ref int position, int[] coefficients, out MdecBlockHeader header)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        if (coefficients.Length < 64)
        {
            throw new ArgumentException("A block needs 64 coefficients.", nameof(coefficients));
        }

        Array.Clear(coefficients, 0, 64);
        header = default;
        int start = position;
        if (position >= codes.Length)
        {
            return MdecDecodeStatus.EndOfStream;
        }

        ushort first = codes[position++];
        int qscale = first >> 10;
        int dc = Level(first);
        int nonZero = 0;
        if (dc != 0)
        {
            coefficients[0] = dc * DefaultQuantizationMatrix[0];
            nonZero = 1;
        }

        int index = 0;
        while (true)
        {
            if (position >= codes.Length)
            {
                return MdecDecodeStatus.EndOfStream;
            }

            ushort code = codes[position++];
            if (code == EndOfBlock)
            {
                break;
            }

            index += (code >> 10) + 1;
            if (index > 63)
            {
                return MdecDecodeStatus.AcIndexOutOfRange;
            }

            int level = Level(code);
            if (level != 0)
            {
                int matrix = ReverseZigZag[index];
                coefficients[matrix] = (level * DefaultQuantizationMatrix[matrix] * qscale + 4) >> 3;
                nonZero++;
            }
        }

        header = new MdecBlockHeader(qscale, dc, position - start, nonZero);
        return MdecDecodeStatus.Complete;
    }

    /// <summary>
    /// PsxMdecIDCT_int: two passes over the PS1 cosine matrix with 64-bit sums and a
    /// final round-half-up shift by 32. Input and output are matrix order; they may alias.
    /// </summary>
    public static void Idct(int[] input, int[] output)
    {
        Span<long> temp = stackalloc long[64];
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                long sum = 0;
                for (int i = 0; i < 8; i++)
                {
                    sum += (long)input[x + i * 8] * CosineMatrix[y + i * 8];
                }

                temp[x + y * 8] = sum;
            }
        }

        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                long sum = 0;
                for (int i = 0; i < 8; i++)
                {
                    sum += CosineMatrix[x + i * 8] * temp[i + y * 8];
                }

                output[x + y * 8] = (int)ShrRound(sum, 32);
            }
        }
    }

    /// <summary>jpsxdec Maths.shrRound: arithmetic shift that rounds the dropped half up.</summary>
    public static long ShrRound(long value, int shift)
    {
        if (shift == 0 || value == 0)
        {
            return value;
        }

        return (value >> shift) + ((value >> (shift - 1)) & 1);
    }

    /// <summary>PsxYCbCr_int + RGB.toInt for one pixel: returns 0xFFRRGGBB.</summary>
    public static int ToArgb(int y, int cb, int cr)
    {
        int red = (int)ShrRound(Cr2R * cr, FixedBits);
        int green = (int)ShrRound(-(Cb2G * cb) - (Cr2G * cr), FixedBits);
        int blue = (int)ShrRound(Cb2B * cb, FixedBits);
        return Pack(y + 128, red, green, blue);
    }

    private static int Pack(int luma, int red, int green, int blue)
    {
        return unchecked((int)0xFF000000) | (Clamp(luma + red) << 16) | (Clamp(luma + green) << 8) | Clamp(luma + blue);
    }

    private static int Clamp(int value) => value < 0 ? 0 : value > 255 ? 255 : value;

    private static void Store(MdecFrame frame, int block, int[] spatial)
    {
        int macroblock = block / BlocksPerMacroblock;
        int kind = block % BlocksPerMacroblock;
        int column = macroblock / frame.MacroblockRows;
        int row = macroblock % frame.MacroblockRows;
        int[] target;
        int offset;
        int stride;
        if (kind < 2)
        {
            target = kind == 0 ? frame.Cr : frame.Cb;
            stride = frame.ChromaWidth;
            offset = column * 8 + row * 8 * stride;
        }
        else
        {
            target = frame.Y;
            stride = frame.LumaWidth;
            int sub = kind - 2;
            offset = column * 16 + (sub & 1) * 8 + (row * 16 + (sub >> 1) * 8) * stride;
        }

        for (int i = 0; i < 8; i++)
        {
            Array.Copy(spatial, i * 8, target, offset + i * stride, 8);
        }
    }

    /// <summary>MdecDecoder_int.readDecodedRgb: 2x2 pixels share one Cb/Cr sample; chroma terms are rounded once per sample.</summary>
    private static void ConvertToRgb(MdecFrame frame)
    {
        int width = frame.Width;
        int height = frame.Height;
        int lumaStride = frame.LumaWidth;
        int chromaStride = frame.ChromaWidth;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int chroma = (y >> 1) * chromaStride + (x >> 1);
                int cb = frame.Cb[chroma];
                int cr = frame.Cr[chroma];
                int red = (int)ShrRound(Cr2R * cr, FixedBits);
                int green = (int)ShrRound(-(Cb2G * cb) - (Cr2G * cr), FixedBits);
                int blue = (int)ShrRound(Cb2B * cb, FixedBits);

                // jpsxdec's odd-width/odd-height tails read the same luma sample as here.
                int luma = frame.Y[y * lumaStride + x] + 128;
                frame.Argb[y * width + x] = Pack(luma, red, green, blue);
            }
        }
    }
}
