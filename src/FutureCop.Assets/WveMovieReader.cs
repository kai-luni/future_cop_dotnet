using System.Buffers.Binary;
using System.Text;

namespace FutureCop.Assets;

/// <summary>One chunk of a .WVE stream: four-character tag plus a big-endian size that includes the 8-byte header.</summary>
public sealed record WveChunk(string Tag, int Offset, int Size);

/// <summary>
/// One video chunk (MDEC or MDC2). After the 8-byte chunk header come big-endian
/// u16 width, u16 height and u32 frame number; the compressed block stream starts
/// at chunk offset +0x10 (the pointer func_80085630 hands to func_80089BBC).
/// </summary>
public sealed class WveVideoFrame
{
    /// <summary>Offset of the block stream inside its chunk.</summary>
    public const int BlockDataOffset = 0x10;

    /// <summary>
    /// Bytes past the chunk end that <see cref="GetBlockWords"/> appends. The retail
    /// decoder keeps a 32-bit bit reservoir and refills it one word ahead, so it can
    /// touch a few bytes of the following chunk; those bytes never reach the output.
    /// In INTRO.WVE no frame needs more than one word past its chunk; eight bytes match
    /// the stream extent used by the wve-block.vectors cases.
    /// </summary>
    public const int ReadAheadBytes = 8;

    private readonly byte[] file;

    internal WveVideoFrame(byte[] file, WveChunk chunk, int index)
    {
        this.file = file;
        Chunk = chunk;
        Index = index;
        ReadOnlySpan<byte> header = file.AsSpan(chunk.Offset + 8, 8);
        Width = BinaryPrimitives.ReadUInt16BigEndian(header);
        Height = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(2));
        FrameNumber = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4));
    }

    /// <summary>The chunk this frame came from.</summary>
    public WveChunk Chunk { get; }

    /// <summary>Position of this frame among the video chunks of the file.</summary>
    public int Index { get; }

    /// <summary>Chunk tag: "MDEC" frames are decoded; retail skips the block decode for any other tag.</summary>
    public string Tag => Chunk.Tag;

    /// <summary>Frame width in pixels (big-endian u16 at chunk +8).</summary>
    public int Width { get; }

    /// <summary>Frame height in pixels (big-endian u16 at chunk +10).</summary>
    public int Height { get; }

    /// <summary>Frame number (big-endian u32 at chunk +12).</summary>
    public uint FrameNumber { get; }

    /// <summary>Raw block stream bytes, chunk +0x10 to the chunk end.</summary>
    public ReadOnlyMemory<byte> BlockData =>
        file.AsMemory(Chunk.Offset + BlockDataOffset, Chunk.Size - BlockDataOffset);

    /// <summary>
    /// Block stream as the 16-bit words func_80089BBC reads: native little-endian
    /// halfwords of the file bytes, followed by up to <see cref="ReadAheadBytes"/>
    /// bytes of whatever follows the chunk (zero at the end of the file).
    /// </summary>
    public ushort[] GetBlockWords()
    {
        int start = Chunk.Offset + BlockDataOffset;
        int length = Chunk.Size - BlockDataOffset + ReadAheadBytes;
        var bytes = new byte[(length + 1) & ~1];
        int available = Math.Min(bytes.Length, file.Length - start);
        file.AsSpan(start, available).CopyTo(bytes);
        var words = new ushort[bytes.Length / 2];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2, 2));
        }

        return words;
    }
}

/// <summary>
/// Parses a .WVE movie: a flat sequence of chunks (VLC0, MDEC, MDC2, au00, au01, ...)
/// each starting with a four-character tag and a big-endian u32 size that covers
/// the header. Exposes the VLC0 symbol table and the video frames in file order.
/// </summary>
public sealed class WveMovieReader
{
    private readonly byte[] data;

    private WveMovieReader(byte[] data, List<WveChunk> chunks)
    {
        this.data = data;
        Chunks = chunks;
        var frames = new List<WveVideoFrame>();
        foreach (WveChunk chunk in chunks)
        {
            if (chunk.Tag == "VLC0" && VlcSymbols == null)
            {
                VlcSymbols = WveVlcTable.ReadSymbols(data.AsSpan(chunk.Offset + 8, chunk.Size - 8));
            }
            else if (chunk.Tag is "MDEC" or "MDC2")
            {
                if (chunk.Size < WveVideoFrame.BlockDataOffset)
                {
                    throw new InvalidDataException($"Video chunk at {chunk.Offset} is shorter than its 16-byte header.");
                }

                frames.Add(new WveVideoFrame(data, chunk, frames.Count));
            }
        }

        Frames = frames;
    }

    /// <summary>All chunks in file order.</summary>
    public IReadOnlyList<WveChunk> Chunks { get; }

    /// <summary>Symbols of the first VLC0 chunk, or null when the file has none.</summary>
    public ushort[]? VlcSymbols { get; }

    /// <summary>Video chunks (MDEC and MDC2) in file order.</summary>
    public IReadOnlyList<WveVideoFrame> Frames { get; }

    /// <summary>Builds the Huffman tables from the VLC0 chunk.</summary>
    public WveVlcTable BuildVlcTable() =>
        WveVlcTable.Build(VlcSymbols ?? throw new InvalidDataException("The movie has no VLC0 chunk."));

    /// <summary>Reads and parses a movie file.</summary>
    public static WveMovieReader Open(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>Parses a complete movie held in memory. Rejects chunks that overrun the data or are shorter than their header.</summary>
    public static WveMovieReader Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var chunks = new List<WveChunk>();
        int offset = 0;
        while (offset < data.Length)
        {
            if (data.Length - offset < 8)
            {
                throw new InvalidDataException($"Partial chunk header at {offset}.");
            }

            string tag = Encoding.ASCII.GetString(data, offset, 4);
            uint size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 4, 4));
            if (size < 8 || size > (uint)(data.Length - offset))
            {
                throw new InvalidDataException($"Chunk '{tag}' at {offset} has invalid size {size}.");
            }

            chunks.Add(new WveChunk(tag, offset, (int)size));
            offset += (int)size;
        }

        return new WveMovieReader(data, chunks);
    }

    /// <summary>Payload of a chunk (bytes after its 8-byte header).</summary>
    public ReadOnlyMemory<byte> Payload(WveChunk chunk) => data.AsMemory(chunk.Offset + 8, chunk.Size - 8);
}
