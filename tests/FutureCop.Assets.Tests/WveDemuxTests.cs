using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

public class WveDemuxTests
{
    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "tests/golden/wve-demux.vectors"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new Exception("fixture not found"), "tests/golden/wve-demux.vectors");
    }

    private sealed class Sink : IWveDemuxSink
    {
        public readonly List<string> Events = new();
        public int PressLoop, Port, Loop, Clock;
        public uint Bits;
        public uint PollPad(int port) { var v = (PressLoop == -2 || Loop == PressLoop) && port == Port ? Bits : 0; if (port == 1) Loop++; return v; }
        public void Vlc(ReadOnlyMemory<byte> data, int offset) { Assert.Equal(4, data.Length); Assert.Equal(new byte[] { 1, 2, 3, 4 }, data.ToArray()); Events.Add($"V{offset}"); }
        public void Audio(ReadOnlyMemory<byte> data, int offset) { Assert.True(data.Length >= 8); Events.Add($"A{offset}"); }
        public void SplitAudio(ReadOnlyMemory<byte> data, int offset) { Assert.True(data.Length >= 8); Events.Add($"B{offset}"); }
        public void AudioStart() => Events.Add("S");
        public void ClockEnable() => Events.Add("E");
        public uint Deadline(uint index) { Events.Add($"T{index}"); return 1; }
        public void Decode(ReadOnlyMemory<byte> data, int offset) { Assert.True(data.Length >= 8); Events.Add($"D{offset}"); }
        public uint ClockRead() => (uint)Clock;
        public void Wait() { Events.Add("W"); Clock++; }
        public void Present() => Events.Add("P");
        public void Advance(int amount) => Events.Add($"X{amount}");
    }

    private sealed class Stream : IWveDemuxStream
    {
        private readonly byte[] data;
        private readonly Sink sink;
        private readonly int fault;
        private int position, calls;
        public Stream(byte[] bytes, Sink callbacks, int fault) { data = bytes; sink = callbacks; this.fault = fault; }
        public WveReservation? Reserve(uint requested)
        {
            calls++;
            if (fault == calls) return null;
            if (fault == 4 && calls == 3) return new(data.AsMemory(position, 0), position);
            return new(data.AsMemory(position, (int)Math.Min((uint)(data.Length - position), requested)), position);
        }
        public void Advance(uint amount) { sink.Events.Add($"X{amount}"); position = checked(position + (int)amount); }
    }

    [Fact]
    public void SharedVectorsMatchDemuxAndSafeDivergences()
    {
        var count = 0;
        foreach (var raw in File.ReadLines(FixturePath()))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(19, p.Length);
            var sink = new Sink { PressLoop = int.Parse(p[2]), Port = int.Parse(p[3]), Bits = Convert.ToUInt32(p[4], 16) };
            var state = new WveDemuxState { PadMask0 = Convert.ToUInt32(p[5], 16), PadMask1 = Convert.ToUInt32(p[6], 16) };
            byte[] input = p[7] == "-" ? Array.Empty<byte>() : Convert.FromHexString(p[7]);
            var result = WveDemux.Run(new Stream(input, sink, int.Parse(p[18])), 0, uint.Parse(p[1]), state, sink);
            Assert.True(result.ToString() == p[13], $"{p[0]} status {result} expected {p[13]}");
            Assert.Equal(p[14] == "1", state.Abort);
            Assert.Equal(Convert.ToUInt32(p[15], 16), state.PadMask0);
            Assert.Equal(Convert.ToUInt32(p[16], 16), state.PadMask1);
            Assert.True((sink.Events.Count == 0 ? "-" : string.Join(',', sink.Events) + ",") == p[17], $"{p[0]} events {string.Join(',', sink.Events)} expected {p[17]}");
            count++;
        }
        Assert.True(count >= 20);
    }
}
