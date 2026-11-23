using System.Buffers.Binary;
using System.Text;
using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Corrupt or truncated archives must fail with a reader exception (or be skipped), never with an
/// indexing error from a fixed-offset read. The layouts here are minimal synthetic archives: they
/// only carry what the readers touch.
/// </summary>
public class TruncatedArchiveTests
{
    private static void Word(BinaryWriter w, int value) => w.Write(value);
    private static void Tag(BinaryWriter w, string name) => w.Write(Encoding.ASCII.GetBytes(new string(name.Reverse().ToArray())));
    private static void Chunk(BinaryWriter w, string name, int index, byte[] data)
    {
        Tag(w, name); Word(w, data.Length + 12); Word(w, index); w.Write(data);
    }

    private static string WriteTemp(byte[] bytes)
    {
        string path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>A 3DQL chunk without even its count word used to throw ArgumentOutOfRangeException
    /// while scanning; the record is now parsed without polygons.</summary>
    [Fact]
    public void ModelWithTruncated3DqlChunkIsParsedWithoutPolygons()
    {
        using var archive = new MemoryStream();
        using (var w = new BinaryWriter(archive, Encoding.UTF8, true))
        {
            Tag(w, "Cobj"); Word(w, 7); Word(w, 0);
            for (int i = 0; i < 11; i++) Word(w, 0);           // pad the record header to 56 bytes
            Chunk(w, "4DGI", 1, BitConverter.GetBytes(0x02000000));
            Chunk(w, "3DQL", 1, Array.Empty<byte>());           // count word missing
        }
        string path = WriteTemp(archive.ToArray());
        try
        {
            var model = Assert.Single(new MisFile(path).Models);
            Assert.Equal(7, model.Id);
            Assert.Single(model.Header);
            Assert.Empty(model.Polygons);
        }
        finally { File.Delete(path); }
    }

    /// <summary>A GRDB tag too close to the payload end is "no map header" instead of a read past the end.</summary>
    [Fact]
    public void GrdbHeaderTruncatedBelowItsWordTableIsRejected()
    {
        var payload = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0), ChunkReader.TagValue("GRDB"));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 20);
        Assert.Null(MapHeader.Parse(payload));
    }

    /// <summary>func_800639D4 reads +0x08 and +0x3CA of a Sect chunk; a shorter chunk is corrupt
    /// data and must surface as InvalidDataException, not as an indexing error.</summary>
    [Fact]
    public void TruncatedSectChunkRaisesInvalidData()
    {
        var grdb = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(grdb.AsSpan(0), ChunkReader.TagValue("GRDB"));
        BinaryPrimitives.WriteUInt32LittleEndian(grdb.AsSpan(4), 40);
        BinaryPrimitives.WriteUInt32LittleEndian(grdb.AsSpan(0x0C + 4), 1);  // width
        BinaryPrimitives.WriteUInt32LittleEndian(grdb.AsSpan(0x0C + 8), 1);  // height

        using var sect = new MemoryStream();
        using (var w = new BinaryWriter(sect, Encoding.UTF8, true))
        {
            Tag(w, "Sect"); Word(w, 44); Word(w, 0);            // 12-byte header + 32 data bytes
            w.Write(new byte[32]);
        }

        using var mission = new MemoryStream();
        using (var w = new BinaryWriter(mission, Encoding.UTF8, true))
        {
            Resource(w, "Cptc", 1, grdb);
            Resource(w, "Ctil", 2, sect.ToArray());
        }
        string path = WriteTemp(mission.ToArray());
        try
        {
            var ex = Assert.Throws<InvalidDataException>(() => LevelMap.FromBytes(File.ReadAllBytes(path)));
            Assert.Contains("truncated", ex.Message);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// One streamed resource record: the 24-byte SHOC/SHDR header, the resource tag word, then the
    /// first SDAT read block at header + 60 (the layout MisFile and LevelMap scan for).
    /// </summary>
    private static void Resource(BinaryWriter w, string tag, int id, byte[] payload)
    {
        long start = w.BaseStream.Position;
        Tag(w, "SHOC"); Word(w, 60); Word(w, 0); Word(w, 0); Tag(w, "SHDR"); Word(w, 2);
        Tag(w, tag); Word(w, id); Word(w, payload.Length);
        while (w.BaseStream.Position < start + 60) w.Write((byte)0);
        Tag(w, "SHOC"); Word(w, payload.Length + 20); Word(w, 0); Word(w, 0); Tag(w, "SDAT");
        w.Write(payload);
    }
}
