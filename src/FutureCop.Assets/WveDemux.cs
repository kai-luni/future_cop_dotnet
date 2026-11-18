using System.Buffers.Binary;

namespace FutureCop.Assets;

/// <summary>A reservation's accessible bytes and their stream-relative offset.</summary>
public readonly record struct WveReservation(ReadOnlyMemory<byte> Data, int Offset);

/// <summary>Stream adapter: Reserve returns null on failure, or the actually available bytes;
/// Advance consumes bytes. Reserve must not consume data. The initial reserve is advisory.</summary>
public interface IWveDemuxStream
{
    WveReservation? Reserve(uint requestedBytes);
    void Advance(uint bytes);
}

/// <summary>Callbacks used by the demux loop; offsets are stream-relative.</summary>
public interface IWveDemuxSink
{
    uint PollPad(int port);
    void Vlc(ReadOnlyMemory<byte> data, int offset);
    void Audio(ReadOnlyMemory<byte> data, int offset);
    void SplitAudio(ReadOnlyMemory<byte> data, int offset);
    void AudioStart();
    void ClockEnable();
    uint Deadline(uint index);
    void Decode(ReadOnlyMemory<byte> data, int offset);
    uint ClockRead();
    void Wait();
    void Present();
}

public sealed class WveDemuxState
{
    public uint PadMask0 { get; set; }
    public uint PadMask1 { get; set; }
    public bool Abort { get; set; }
}

public enum WveDemuxResult { Complete, Aborted, Malformed }

/// <summary>
/// Port of func_800879DC with bounded reservations. C returns 1 on a null or zero
/// reservation, even on timeout; Complete preserves that ambiguity. Unlike C, partial
/// headers and known chunks shorter than their declared size (or VLC's eight-byte prefix)
/// are rejected before dispatch. PollPad and clock callbacks model external hardware;
/// the caller must make clock progress. No codec is implemented here.
/// </summary>
public static class WveDemux
{
    public static WveDemuxResult Run(IWveDemuxStream stream, uint initialReservation, uint displayMode, WveDemuxState state, IWveDemuxSink sink)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sink);
        _ = stream.Reserve(initialReservation); // Original ignores the initial result.
        uint count = 0;
        while (true)
        {
            uint p0 = sink.PollPad(0) & 0x40080000u;
            uint p1 = sink.PollPad(1) & 0x40080000u;
            uint n0 = p0 & ~state.PadMask0, n1 = p1 & ~state.PadMask1;
            if ((n0 | n1) != 0 && count >= 4)
            {
                if (((n0 | n1) & 0x80000) != 0) state.Abort = true;
                return WveDemuxResult.Aborted;
            }
            state.PadMask0 = p0;
            state.PadMask1 = p1;
            var header = stream.Reserve(8);
            if (header is null || header.Value.Data.IsEmpty) return WveDemuxResult.Complete;
            if (header.Value.Data.Length < 8) return WveDemuxResult.Malformed;
            var headerBytes = header.Value.Data.Span;
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(headerBytes[..4]);
            uint size = BinaryPrimitives.ReadUInt32BigEndian(headerBytes.Slice(4, 4));
            if (size >= ((displayMode & 1) != 0 ? 0x424cu : 0x4400u))
            {
                stream.Advance(4); continue;
            }
            var data = stream.Reserve(size);
            if (data is null || data.Value.Data.IsEmpty) return WveDemuxResult.Complete;
            if (data.Value.Data.Length > size) return WveDemuxResult.Malformed;
            bool known = tag is 0x564c4330 or 0x41643130 or 0x41643131 or 0x61643130 or 0x61643131 or 0x61643230 or 0x61643231 or 0x61753030 or 0x61753031 or 0x4d444332 or 0x4d444543;
            if (known && (size < 8 || data.Value.Data.Length < size)) return WveDemuxResult.Malformed;
            int offset = data.Value.Offset;
            if (tag == 0x564c4330) sink.Vlc(data.Value.Data[8..], checked(offset + 8));
            else if (tag is 0x41643130 or 0x41643131 or 0x61643130 or 0x61643131 or 0x61643230 or 0x61643231) sink.Audio(data.Value.Data, offset);
            else if (tag is 0x61753030 or 0x61753031) sink.SplitAudio(data.Value.Data, offset);
            else if (tag is 0x4d444332 or 0x4d444543)
            {
                uint index = count++;
                if (index == 1) sink.AudioStart();
                sink.ClockEnable();
                uint deadline = sink.Deadline(index);
                sink.Decode(data.Value.Data, offset);
                while (sink.ClockRead() < deadline) sink.Wait();
                sink.Present();
            }
            stream.Advance(known ? (uint)data.Value.Data.Length : 4);
        }
    }
}
