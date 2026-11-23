using System.Buffers.Binary;
using System.Text;
using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

public class FrontEndTextTests
{
    [Fact]
    public void ReadsOffsetsRelativeToEntriesBase()
    {
        var data = new byte[24];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 100);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 200);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 14);
        Encoding.ASCII.GetBytes("READY\0START\0").CopyTo(data, 12);
        var table = FrontEndText.Parse(data);
        Assert.Equal("READY", table.Lookup(100));
        Assert.Equal("START", table.Lookup(200));
        Assert.Null(table.Lookup(150));
    }

    [Fact]
    public void ReadsRetailFrontEndStringsWhenPresent()
    {
        string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../build/disc/files/CW/FE.MIS"));
        if (!File.Exists(path)) return;
        var table = FrontEndText.FromMisFile(path);
        Assert.Equal("Precinct Assault", table.Lookup(0x438));
        Assert.Equal("Urban Jungle", table.Lookup(0x0d48));
    }

    [Fact]
    public void RejectsAnOutOfBoundsOffset()
    {
        var data = new byte[] { 1, 0, 1, 0, 100, 0, 255, 255 };
        Assert.Throws<InvalidDataException>(() => FrontEndText.Parse(data));
    }
}
