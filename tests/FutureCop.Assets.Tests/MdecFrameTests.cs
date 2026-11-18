using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// MDEC pixel path: block parsing, dequantisation, IDCT and YCbCr->RGB. The fixture
/// tests run on the block stream stored in wve-block.vectors and need no retail files;
/// the retail tests only run when INTRO.WVE (and optionally jpsxdec reference PNGs)
/// are present locally and otherwise return without asserting.
/// </summary>
public class MdecFrameTests
{
    /// <summary>
    /// Mean absolute RGB error allowed against jpsxdec's default "-q high" output
    /// (float IDCT and bicubic chroma upsampling). Measured 0.616 / 0.525 / 0.451 for
    /// chunks 0 / 100 / 400; the maximum over all 837 frames is 2.50.
    /// </summary>
    private const double HighQualityMaeLimit = 1.0;

    private static readonly int[] RetailFrames = { 0, 100, 400 };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "tests/golden/mdec-frame.vectors"))) dir = dir.Parent;
        return dir?.FullName ?? throw new Exception("fixture not found");
    }

    private sealed class Fixture
    {
        public int Width, Height;
        public string[] Planes = Array.Empty<string>();
        public string[] BigEndian = Array.Empty<string>();
        public readonly List<string[]> Pixels = new();
        public readonly List<string[]> Blocks = new();
        public readonly List<string[]> Refs = new();
    }

    private static Fixture LoadFixture()
    {
        var fixture = new Fixture();
        foreach (string raw in File.ReadAllLines(Path.Combine(RepoRoot(), "tests/golden/mdec-frame.vectors")))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (p[0])
            {
                case "SIZE": fixture.Width = Int(p[1]); fixture.Height = Int(p[2]); break;
                case "PLANES": fixture.Planes = p; break;
                case "BIGENDIAN": fixture.BigEndian = p; break;
                case "PIXEL": fixture.Pixels.Add(p); break;
                case "BLOCK": fixture.Blocks.Add(p); break;
                case "REF": fixture.Refs.Add(p); break;
                default: throw new InvalidDataException("bad row: " + line);
            }
        }

        Assert.Equal(320, fixture.Width);
        Assert.Equal(192, fixture.Height);
        Assert.NotEmpty(fixture.Blocks);
        return fixture;
    }

    /// <summary>VLC0 symbols and the CASE frame0 input words from wve-block.vectors.</summary>
    private static (WveVlcTable Table, ushort[] Input) LoadStream()
    {
        ushort[]? symbols = null, input = null;
        foreach (string raw in File.ReadAllLines(Path.Combine(RepoRoot(), "tests/golden/wve-block.vectors")))
        {
            string[] p = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length > 1 && p[0] == "SYMBOLS") symbols = LittleEndianWords(Convert.FromHexString(p[1]));
            if (p.Length > 2 && p[0] == "CASE" && p[1] == "frame0") input = LittleEndianWords(Convert.FromHexString(p[2]));
        }

        Assert.NotNull(symbols);
        Assert.NotNull(input);
        return (WveVlcTable.Build(symbols!), input!);
    }

    private static ushort[] LittleEndianWords(byte[] bytes)
    {
        var words = new ushort[bytes.Length / 2];
        for (int i = 0; i < words.Length; i++) words[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2, 2));
        return words;
    }

    private static ushort[] DecodedCodes()
    {
        var (table, input) = LoadStream();
        return WveBlockDecoder.Decode(table, input);
    }

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static int[] Ints(string csv) => csv.Split(',').Select(Int).ToArray();

    private static string Sha(int[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    [Fact]
    public void FrameDecodesToGoldenPlanesAndPixels()
    {
        Fixture fixture = LoadFixture();
        ushort[] codes = DecodedCodes();
        Assert.Equal(0x3800, codes[1]); // MDEC command header, not a block code.

        MdecFrame frame = MdecFrameDecoder.DecodeDecoderOutput(codes, fixture.Width, fixture.Height);

        Assert.Equal(320, frame.Width);
        Assert.Equal(192, frame.Height);
        Assert.Equal(320 * 192, frame.Argb.Length);
        Assert.Equal(160 * 96, frame.Cb.Length);
        Assert.Equal(fixture.Planes[1], frame.Status.ToString());
        Assert.Equal(Int(fixture.Planes[2]), frame.BlocksDecoded);
        Assert.Equal(Int(fixture.Planes[3]), frame.CodesConsumed);
        // Every block code is consumed; only the 64-word trailer remains.
        Assert.Equal(codes.Length - MdecFrameDecoder.CommandWords - WveBlockDecoder.TrailerWords, frame.CodesConsumed);
        Assert.Equal(fixture.Planes[4], Sha(frame.Y));
        Assert.Equal(fixture.Planes[5], Sha(frame.Cb));
        Assert.Equal(fixture.Planes[6], Sha(frame.Cr));
        Assert.Equal(fixture.Planes[7], Sha(frame.Argb));

        Assert.Equal(5, fixture.Pixels.Count);
        foreach (string[] p in fixture.Pixels)
        {
            int x = Int(p[1]), y = Int(p[2]);
            Assert.Equal(Convert.ToInt32(p[3], 16) | unchecked((int)0xFF000000), frame.Argb[y * frame.Width + x]);
            Assert.Equal(Int(p[4]), frame.Y[y * frame.LumaWidth + x]);
            Assert.Equal(Int(p[5]), frame.Cb[(y / 2) * frame.ChromaWidth + x / 2]);
            Assert.Equal(Int(p[6]), frame.Cr[(y / 2) * frame.ChromaWidth + x / 2]);
            Assert.Equal(frame.Argb[y * frame.Width + x], MdecFrameDecoder.ToArgb(Int(p[4]), Int(p[5]), Int(p[6])));
        }
    }

    [Fact]
    public void BlocksParseDequantiseAndTransformLikeTheReference()
    {
        Fixture fixture = LoadFixture();
        ushort[] codes = DecodedCodes().Skip(MdecFrameDecoder.CommandWords).ToArray();
        var starts = new List<int>();
        var coefficients = new int[64];
        int position = 0;
        for (int i = 0; i < 1440; i++)
        {
            starts.Add(position);
            MdecFrameDecoder.ReadBlock(codes, ref position, coefficients);
        }

        foreach (string[] p in fixture.Blocks)
        {
            int index = Int(p[1]);
            int start = starts[index];
            int cursor = start;
            MdecBlockHeader header = MdecFrameDecoder.ReadBlock(codes, ref cursor, coefficients);

            // qscale/DC word: top 6 bits qscale, signed bottom 10 bits DC.
            Assert.Equal(Int(p[2]), header.Qscale);
            Assert.Equal(Int(p[3]), header.Dc);
            Assert.Equal(Int(p[4]), header.CodeCount);
            Assert.Equal(Int(p[5]), header.NonZeroCount);
            ushort[] expectedCodes = ParseCodeHex(p[7]);
            Assert.Equal(expectedCodes, codes.Skip(start).Take(header.CodeCount).ToArray());
            Assert.Equal(MdecFrameDecoder.EndOfBlock, expectedCodes[^1]);

            // DC is scaled by quant[0] = 2 only, never by qscale.
            Assert.Equal(header.Dc * 2, coefficients[0]);
            Assert.Equal(Ints(p[8]), coefficients);

            // AC run-length placement: replay the runs and check the last zig-zag slot.
            int zigzag = 0;
            for (int i = 1; i < expectedCodes.Length - 1; i++) zigzag += (expectedCodes[i] >> 10) + 1;
            Assert.Equal(Int(p[6]), zigzag);

            var spatial = new int[64];
            MdecFrameDecoder.Idct(coefficients, spatial);
            if (header.NonZeroCount != 0) Assert.Equal(Ints(p[9]), spatial);
            else Assert.All(Ints(p[9]), v => Assert.Equal(0, v));
        }
    }

    private static ushort[] ParseCodeHex(string hex)
    {
        // Fixture codes are four hex digits per word, most significant first ("0BE0" = 0x0BE0).
        var words = new ushort[hex.Length / 4];
        for (int i = 0; i < words.Length; i++) words[i] = Convert.ToUInt16(hex.Substring(i * 4, 4), 16);
        return words;
    }

    [Fact]
    public void BigEndianWordOrderDoesNotProduceAFrame()
    {
        // Endianness evidence: the same stream read as big-endian words decodes to
        // garbage codes and the MDEC model hits a run past index 63 after 11 blocks.
        Fixture fixture = LoadFixture();
        var (table, input) = LoadStream();
        ushort[] swapped = input.Select(w => (ushort)((w >> 8) | (w << 8))).ToArray();
        MdecFrame frame = MdecFrameDecoder.DecodeDecoderOutput(WveBlockDecoder.Decode(table, swapped), 320, 192);
        Assert.Equal(fixture.BigEndian[1], frame.Status.ToString());
        Assert.Equal(MdecDecodeStatus.AcIndexOutOfRange, frame.Status);
        Assert.Equal(Int(fixture.BigEndian[2]), frame.BlocksDecoded);
        Assert.Equal(Int(fixture.BigEndian[3]), frame.CodesConsumed);
        Assert.Equal(fixture.BigEndian[4], Sha(frame.Argb));
    }

    [Fact]
    public void RunPastIndex63StopsDecodingAndLeavesGrey()
    {
        // 0x0801: qscale 2, DC 1. 0xF805 advances 63 to the last slot (allowed); in the
        // second block 0x0001 reaches index 1 and 0xFC01 then jumps 64 places.
        ushort[] codes = { 0x0801, 0xF805, 0xFE00, 0x0801, 0x0001, 0xFC01, 0xFE00 };
        MdecFrame frame = MdecFrameDecoder.Decode(codes, 16, 16);
        Assert.Equal(MdecDecodeStatus.AcIndexOutOfRange, frame.Status);
        Assert.Equal(1, frame.BlocksDecoded);
        Assert.NotEqual(0, frame.Cr[0]); // First block (Cr) was written.
        Assert.All(frame.Cb, v => Assert.Equal(0, v)); // The broken block stays zero.
        Assert.All(frame.Y, v => Assert.Equal(0, v));

        int position = 0;
        var coefficients = new int[64];
        MdecBlockHeader header = MdecFrameDecoder.ReadBlock(codes, ref position, coefficients);
        Assert.Equal(2, header.Qscale);
        Assert.Equal(1, header.Dc);
        Assert.Equal((5 * 83 * 2 + 4) >> 3, coefficients[63]); // Zig-zag 63 is matrix 63.
        Assert.Throws<InvalidDataException>(() => MdecFrameDecoder.ReadBlock(codes, ref position, coefficients));
    }

    [Fact]
    public void TruncatedStreamKeepsDecodedBlocksAndZeroesTheRest()
    {
        ushort[] codes = DecodedCodes();
        MdecFrame full = MdecFrameDecoder.DecodeDecoderOutput(codes, 320, 192);
        int keep = (codes.Length - 66) / 2;
        MdecFrame cut = MdecFrameDecoder.DecodeDecoderOutput(codes.AsSpan(0, keep + 2), 320, 192);

        Assert.Equal(MdecDecodeStatus.EndOfStream, cut.Status);
        Assert.InRange(cut.BlocksDecoded, 1, 1439);
        Assert.Equal(keep, cut.CodesConsumed);

        // Macroblocks run down each 12-macroblock column; whole columns before the cut match.
        int fullColumns = cut.BlocksDecoded / 6 / 12;
        Assert.True(fullColumns > 0);
        for (int y = 0; y < 192; y++)
        {
            for (int x = 0; x < fullColumns * 16; x++) Assert.Equal(full.Argb[y * 320 + x], cut.Argb[y * 320 + x]);
            for (int x = (fullColumns + 1) * 16; x < 320; x++) Assert.Equal(unchecked((int)0xFF808080), cut.Argb[y * 320 + x]);
        }

        // A stream that ends inside the first block yields an all-grey frame.
        MdecFrame empty = MdecFrameDecoder.Decode(new ushort[] { 0x0801, 0x0003 }, 320, 192);
        Assert.Equal(MdecDecodeStatus.EndOfStream, empty.Status);
        Assert.Equal(0, empty.BlocksDecoded);
        Assert.All(empty.Argb, v => Assert.Equal(unchecked((int)0xFF808080), v));
    }

    [Fact]
    public void ColourConversionRoundsAndClampsLikeJpsxdec()
    {
        Assert.Equal(unchecked((int)0xFF808080), MdecFrameDecoder.ToArgb(0, 0, 0));
        Assert.Equal(unchecked((int)0xFFFFFFFF), MdecFrameDecoder.ToArgb(200, 0, 0));
        Assert.Equal(unchecked((int)0xFF000000), MdecFrameDecoder.ToArgb(-300, 0, 0));
        // Cr = 127: R = 128 + round(1.402 * 127) = 306 -> 255; G = 128 - round(0.7143 * 127) = 37.
        Assert.Equal(unchecked((int)0xFFFF2580), MdecFrameDecoder.ToArgb(0, 0, 127));
        Assert.Equal(3, MdecFrameDecoder.ShrRound(5, 1));
        Assert.Equal(-2, MdecFrameDecoder.ShrRound(-5, 1)); // Halves round towards +infinity.
        Assert.Equal(-1, MdecFrameDecoder.Level(0x03FF));
        Assert.Equal(-512, MdecFrameDecoder.Level(0xFE00));
    }

    [Fact]
    public void IdctOfDcOnlyBlockIsFlat()
    {
        var block = new int[64];
        block[0] = 64;
        var output = new int[64];
        MdecFrameDecoder.Idct(block, output);
        Assert.All(output, v => Assert.Equal(8, v));
    }

    [Fact]
    public void MovieReaderSplitsChunksAndReadsFrameHeaders()
    {
        var bytes = new List<byte>();
        void Chunk(string tag, byte[] payload)
        {
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(tag));
            var size = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(size, (uint)(payload.Length + 8));
            bytes.AddRange(size);
            bytes.AddRange(payload);
        }

        var vlc = new byte[WveVlcTable.SymbolCount * 2];
        vlc[4] = 0x00; vlc[5] = 0xFE; // Symbol 2 = 0xFE00, little endian.
        Chunk("VLC0", vlc);
        Chunk("au00", new byte[4]);
        Chunk("MDEC", new byte[] { 0x01, 0x40, 0x00, 0xC0, 0x00, 0x00, 0x01, 0x02, 0x20, 0x25, 0x00, 0x38 });
        Chunk("MDC2", new byte[] { 0x00, 0x10, 0x00, 0x20, 0x00, 0x00, 0x00, 0x07 });

        WveMovieReader movie = WveMovieReader.Parse(bytes.ToArray());
        Assert.Equal(new[] { "VLC0", "au00", "MDEC", "MDC2" }, movie.Chunks.Select(c => c.Tag).ToArray());
        Assert.Equal((ushort)0xFE00, movie.VlcSymbols![2]);
        Assert.Equal(2, movie.Frames.Count);
        WveVideoFrame frame = movie.Frames[0];
        Assert.Equal(320, frame.Width);
        Assert.Equal(192, frame.Height);
        Assert.Equal(0x102u, frame.FrameNumber);
        Assert.Equal(4, frame.BlockData.Length);
        ushort[] words = frame.GetBlockWords();
        // Little-endian words, then 8 read-ahead bytes taken from the next chunk header.
        Assert.Equal(new ushort[] { 0x2520, 0x3800, 0x444D, 0x3243, 0, 0x1000 }, words);
        Assert.Equal("MDC2", movie.Frames[1].Tag);
        Assert.Equal(7u, movie.Frames[1].FrameNumber);
        Assert.Equal(new ushort[] { 0, 0, 0, 0 }, movie.Frames[1].GetBlockWords()); // Zero past the end of the file.

        byte[] broken = bytes.ToArray();
        broken[7] = 0xFF; // VLC0 size now overruns the data.
        Assert.Throws<InvalidDataException>(() => WveMovieReader.Parse(broken));
        Assert.Throws<InvalidDataException>(() => WveMovieReader.Parse(new byte[] { 0x4D, 0x44 }));
    }

    private static string? IntroPath()
    {
        string? dir = Environment.GetEnvironmentVariable("FUTURECOP_MOVIES_DIR");
        string path = dir != null ? Path.Combine(dir, "INTRO.WVE") : Path.Combine(RepoRoot(), "build/disc/files/MOVIES/INTRO.WVE");
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void RetailIntroFramesMatchStoredHashes()
    {
        string? path = IntroPath();
        if (path == null) return; // Retail disc not extracted on this machine.

        Fixture fixture = LoadFixture();
        WveMovieReader movie = WveMovieReader.Open(path);
        WveVlcTable table = movie.BuildVlcTable();
        Assert.Equal(838, movie.Frames.Count);
        Assert.Equal(LoadStream().Input, movie.Frames[1].GetBlockWords());
        foreach (string[] r in fixture.Refs)
        {
            WveVideoFrame source = movie.Frames[Int(r[1])];
            Assert.Equal((uint)Int(r[2]), source.FrameNumber);
            Assert.Equal(Int(r[3]), source.Width);
            Assert.Equal(Int(r[4]), source.Height);
            Assert.Equal(Int(r[5]), source.Chunk.Offset);
            Assert.Equal(Int(r[6]), source.Chunk.Size);
            MdecFrame frame = MdecFrameDecoder.DecodeDecoderOutput(WveBlockDecoder.Decode(table, source.GetBlockWords()), source.Width, source.Height);
            Assert.Equal(MdecDecodeStatus.Complete, frame.Status);
            Assert.Equal(r[7], Sha(frame.Y));
            Assert.Equal(r[8], Sha(frame.Cb));
            Assert.Equal(r[9], Sha(frame.Cr));
            Assert.Equal(r[10], Sha(frame.Argb));
        }
    }

    /// <summary>
    /// Compares against PNGs written by jpsxdec v2.1-beta:
    /// <c>java -jar jpsxdec.jar -f INTRO.WVE -i 0 -vf png -q psx -dir &lt;root&gt;/png-psx</c>
    /// and the same without <c>-q psx</c> into <c>&lt;root&gt;/png</c>. The root comes from
    /// FUTURECOP_JPSXDEC_REF or defaults to ~/.hermes/cache/scratch/mdec-ref.
    /// jpsxdec names frame N "INTRO.WVE[0][NNN].png", N = video chunk index.
    /// </summary>
    [Fact]
    public void RetailIntroFramesMatchJpsxdecReferencePngs()
    {
        string? path = IntroPath();
        string root = Environment.GetEnvironmentVariable("FUTURECOP_JPSXDEC_REF")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hermes/cache/scratch/mdec-ref");
        if (path == null || !Directory.Exists(Path.Combine(root, "png-psx"))) return; // References not available here.

        WveMovieReader movie = WveMovieReader.Open(path);
        WveVlcTable table = movie.BuildVlcTable();
        foreach (int index in RetailFrames)
        {
            WveVideoFrame source = movie.Frames[index];
            MdecFrame frame = MdecFrameDecoder.DecodeDecoderOutput(WveBlockDecoder.Decode(table, source.GetBlockWords()), source.Width, source.Height);
            string name = $"INTRO.WVE[0][{index:D3}].png";

            var psx = PngTestIo.Read(Path.Combine(root, "png-psx", name));
            Assert.Equal((320, 192), (psx.Width, psx.Height));
            Assert.Equal((psx.Width, psx.Height), (frame.Width, frame.Height));
            Assert.Equal(0.0, PngTestIo.MeanAbsoluteError(frame.Argb, psx.Argb)); // Same integer model: exact.

            string highPath = Path.Combine(root, "png", name);
            if (File.Exists(highPath))
            {
                var high = PngTestIo.Read(highPath);
                double mae = PngTestIo.MeanAbsoluteError(frame.Argb, high.Argb);
                Assert.InRange(mae, 0.01, HighQualityMaeLimit);
            }
        }
    }
}
