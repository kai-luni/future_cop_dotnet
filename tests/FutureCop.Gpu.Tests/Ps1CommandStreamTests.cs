using Xunit;

namespace FutureCop.Gpu.Tests;

public class Ps1CommandStreamTests
{
    private static Ps1Primitive Prim(int sortKey, float dx = 0)
    {
        var a = new Ps1Vertex(dx, 0, 0, 0, 128, 128, 128);
        var b = new Ps1Vertex(dx + 1, 0, 1, 0, 128, 128, 128);
        var c = new Ps1Vertex(dx, 1, 0, 1, 128, 128, 128);
        return new Ps1Primitive(default, false, in a, in b, in c, sortKey);
    }

    [Fact]
    public void AddAndCount()
    {
        var stream = new Ps1CommandStream();
        Assert.Equal(0, stream.Count);

        stream.Add(Prim(1));
        stream.Add(Prim(2));
        stream.Add(Prim(3));

        Assert.Equal(3, stream.Count);
        Assert.Equal(3, stream.Primitives.Length);
        Assert.Equal(1, stream[0].SortKey);
        Assert.Equal(3, stream[2].SortKey);
    }

    [Fact]
    public void ClearResetsCountKeepsCapacity()
    {
        var stream = new Ps1CommandStream(64);
        for (int i = 0; i < 40; i++) stream.Add(Prim(i));
        int capacity = stream.Capacity;

        stream.Clear();

        Assert.Equal(0, stream.Count);
        Assert.Equal(0, stream.Primitives.Length);
        Assert.Equal(capacity, stream.Capacity);
    }

    [Fact]
    public void SortKeyIsNeverUsedToReorder()
    {
        var stream = new Ps1CommandStream();
        int[] keys = { 10, 90, 50, 0, 70 };
        foreach (int key in keys) stream.Add(Prim(key, dx: key));

        for (int i = 0; i < keys.Length; i++)
        {
            Assert.Equal(keys[i], stream[i].SortKey);
            Assert.Equal(keys[i], stream[i].V0.X);
        }
    }

    [Fact]
    public void GrowthKeepsOrder()
    {
        var stream = new Ps1CommandStream(2);
        for (int i = 0; i < 100; i++) stream.Add(Prim(i, dx: i));

        Assert.Equal(100, stream.Count);
        Assert.True(stream.Capacity >= 100);
        for (int i = 0; i < 100; i++) Assert.Equal(i, (int)stream[i].V0.X);
    }

    [Fact]
    public void AddRangeAppends()
    {
        var stream = new Ps1CommandStream();
        stream.Add(Prim(1));
        Ps1Primitive[] more = { Prim(2), Prim(3) };

        stream.AddRange(more);

        Assert.Equal(3, stream.Count);
        Assert.Equal(1, stream[0].SortKey);
        Assert.Equal(2, stream[1].SortKey);
        Assert.Equal(3, stream[2].SortKey);
    }

    [Fact]
    public void AddRangeOverOwnSpanDoesNotFail()
    {
        var stream = new Ps1CommandStream();
        stream.Add(Prim(1));
        stream.Add(Prim(2));

        stream.AddRange(stream.Primitives);

        Assert.Equal(4, stream.Count);
    }

    [Fact]
    public void IndexOutOfRangeThrows()
    {
        var stream = new Ps1CommandStream();
        Assert.Throws<ArgumentOutOfRangeException>(() => stream[0]);
    }

    [Fact]
    public void EnsureCapacityPreallocates()
    {
        var stream = new Ps1CommandStream();
        stream.EnsureCapacity(1000);
        Assert.True(stream.Capacity >= 1000);
        Assert.Equal(0, stream.Count);
    }
}
