using System.Buffers.Binary;
using FutureCop.Assets;

namespace FutureCop.Game;

/// <summary>
/// One 12-byte path node in a <c>Cnet</c> (<c>ODtN</c>) network after
/// <c>gLoadPathNodePositions</c> (<c>800511E4.c:102-128</c>) converts raw map-relative
/// placement entries into cell coordinates (<c>world &gt;&gt; 8</c>).
/// </summary>
public sealed class PathNode
{
    public const int None = 0x3FF;

    public int Neighbor0 { get; init; } = None;
    public int Neighbor1 { get; init; } = None;
    public int Neighbor2 { get; init; } = None;
    public int Neighbor3 { get; init; } = None;

    /// <summary>Low byte of node +0x00; bit 1 (0x02) marks a blocked node (8004F188.c:106, 8004F330.c:161).</summary>
    public byte NodeFlags { get; set; }

    /// <summary>Low nibble of node +0x04; 4-bit occupancy count incremented/decremented on segment handoff (8005027C.c:270-287).</summary>
    public byte Occupancy { get; set; }

    /// <summary>Cell X coordinate (world X &gt;&gt; 8, node +0x06).</summary>
    public short X { get; set; }

    /// <summary>Cell Y coordinate ((height + 0x7F) &gt;&gt; 8, node +0x08).</summary>
    public short Y { get; set; }

    /// <summary>Cell Z coordinate (world Z &gt;&gt; 8, node +0x0A).</summary>
    public short Z { get; set; }

    public int NeighborAt(int slot) => slot switch
    {
        0 => Neighbor0,
        1 => Neighbor1,
        2 => Neighbor2,
        3 => Neighbor3,
        _ => None,
    };
}

/// <summary>
/// One loaded <c>Cnet</c> (<c>0x436E6574</c>) path network used by type-5 and type-28 rail movers
/// (<c>800511E4.c</c>, <c>80050DAC.c</c>, <c>80050E0C.c</c>).
/// </summary>
public sealed class PathNetwork
{
    private static readonly uint TagShoc = ChunkReader.TagValue("SHOC");
    private static readonly uint TagShdr = ChunkReader.TagValue("SHDR");
    private static readonly uint TagSdat = ChunkReader.TagValue("SDAT");
    private static readonly uint TagFill = ChunkReader.TagValue("FILL");
    private static readonly uint TagCnet = ChunkReader.TagValue("Cnet");
    private const int SectorStride = 0x6000;

    public PathNetwork(int id, PathNode[] nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        Id = id;
        Nodes = nodes;
    }

    public int Id { get; }
    public PathNode[] Nodes { get; }

    /// <summary>
    /// Port of <c>gFindRailPathCell</c> (<c>func_80050DAC</c>, <c>80050DAC.c:45-61</c>):
    /// linear search for the first node matching <c>(x &gt;&gt; 8, z &gt;&gt; 8)</c>, or -1.
    /// </summary>
    public int FindCell(int worldX, int worldZ)
    {
        int x = worldX >> 8;
        int z = worldZ >> 8;
        for (int i = 0; i < Nodes.Length; i++)
        {
            if (Nodes[i].X == x && Nodes[i].Z == z)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Port of <c>gLoadPathNodePositions</c> (<c>func_800511E4</c>, <c>800511E4.c:77-133</c>)
    /// on a reassembled <c>Cnet</c> (<c>ODtN</c>) payload buffer.
    /// </summary>
    public static PathNetwork? ParsePayload(int id, ReadOnlySpan<byte> data, Func<int, int, int, int>? querySurfaceHeight = null)
    {
        if (data.Length < 0x14) return null;
        short count = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(0x0E, 2));
        if (count <= 0) return new PathNetwork(id, Array.Empty<PathNode>());
        if (0x10 + count * 12 > data.Length) return null;

        var nodes = new PathNode[count];
        for (int i = 0; i < count; i++)
        {
            int nodeOff = 0x10 + i * 12;
            uint word0 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(nodeOff, 4));
            ushort word4 = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(nodeOff + 4, 2));
            short rawZ = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(nodeOff + 6, 2));
            short rawX = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(nodeOff + 8, 2));
            ushort flags06 = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(nodeOff + 10, 2));

            int posX = rawX * 256;
            int posZ = rawZ * 256;
            int type = flags06 & 0xF;
            int height;
            if (type == 5)
            {
                height = unchecked((short)flags06) * 16;
            }
            else
            {
                int mode = type switch
                {
                    0 => -1,
                    1 => -2,
                    _ => type - 2,
                };
                height = querySurfaceHeight?.Invoke(posX, posZ, mode) ?? 0;
            }

            nodes[i] = new PathNode
            {
                Neighbor0 = (int)(word0 >> 22),
                Neighbor1 = (int)((word0 >> 12) & 0x3FF),
                Neighbor2 = (int)((word0 >> 2) & 0x3FF),
                Neighbor3 = word4 >> 6,
                NodeFlags = (byte)(word0 & 0xFF),
                Occupancy = 0, // 800511E4.c:126: flags00 &= 0xF0
                X = unchecked((short)(posX >> 8)),
                Y = unchecked((short)(unchecked((int)((uint)height + 0x7Fu)) >> 8)),
                Z = unchecked((short)(posZ >> 8)),
            };
        }

        return new PathNetwork(id, nodes);
    }

    /// <summary>
    /// Scans a mission archive for all <c>Cnet</c> (<c>0x436E6574</c>) streams and resolves
    /// their node positions via <paramref name="querySurfaceHeight"/> (<c>func_80067684</c>).
    /// </summary>
    public static Dictionary<int, PathNetwork> LoadFromMission(
        ReadOnlySpan<byte> missionBytes,
        Func<int, int, int, int>? querySurfaceHeight = null)
    {
        var result = new Dictionary<int, PathNetwork>();
        for (int off = 0; off + 0x30 <= missionBytes.Length; off += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[off..]) != TagShoc ||
                BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 0x10)..]) != TagShdr ||
                BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 0x18)..]) != TagCnet)
            {
                continue;
            }

            int headerLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 4)..]);
            if (headerLen < 0x30 || off + headerLen > missionBytes.Length) continue;
            int id = BinaryPrimitives.ReadInt32LittleEndian(missionBytes[(off + 0x1C)..]);
            int payloadSize = BinaryPrimitives.ReadInt32LittleEndian(missionBytes[(off + 0x20)..]);
            if (payloadSize < 0x14 || payloadSize > missionBytes.Length) continue;

            byte[]? payload = ReassemblePayload(missionBytes, off + headerLen, payloadSize);
            if (payload is null) continue;
            if (ParsePayload(id, payload, querySurfaceHeight) is { } net)
                result[id] = net;
        }
        return result;
    }

    private static byte[]? ReassemblePayload(ReadOnlySpan<byte> data, int packet, int payloadSize)
    {
        var payload = new byte[payloadSize];
        int copied = 0;
        while (copied < payloadSize && packet + 20 <= data.Length)
        {
            uint tag = BinaryPrimitives.ReadUInt32LittleEndian(data[packet..]);
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(packet + 4)..]);
            if (length < 12 || length > data.Length - packet) return null;
            if (tag == TagFill)
            {
                packet = (packet / SectorStride + 1) * SectorStride;
                continue;
            }
            uint subtype = BinaryPrimitives.ReadUInt32LittleEndian(data[(packet + 16)..]);
            if (tag != TagShoc || subtype != TagSdat || length < 20 || length - 20 > payloadSize - copied)
                return null;
            data.Slice(packet + 20, length - 20).CopyTo(payload.AsSpan(copied));
            copied += length - 20;
            packet += length;
        }
        return copied == payloadSize ? payload : null;
    }
}
