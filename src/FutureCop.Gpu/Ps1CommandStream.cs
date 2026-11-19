namespace FutureCop.Gpu;

/// <summary>
/// Reusable ordered list of primitives for one frame. Add/Clear reuse the backing array, so a
/// steady-state frame does not allocate once <see cref="Capacity"/> is large enough.
/// </summary>
/// <remarks>
/// The stream preserves insertion order exactly; <see cref="Ps1Primitive.SortKey"/> is advisory
/// only. Painter's order is the caller's job (LevelScene orders far-to-near before emitting).
/// </remarks>
public sealed class Ps1CommandStream
{
    private const int InitialCapacity = 256;

    private Ps1Primitive[] _items;

    public Ps1CommandStream()
        : this(InitialCapacity)
    {
    }

    public Ps1CommandStream(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new Ps1Primitive[capacity];
    }

    public int Count { get; private set; }

    public int Capacity => _items.Length;

    /// <summary>The queued primitives in insertion order.</summary>
    public ReadOnlySpan<Ps1Primitive> Primitives => _items.AsSpan(0, Count);

    public Ps1Primitive this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[index];
        }
    }

    public void Add(in Ps1Primitive primitive)
    {
        if (Count == _items.Length) Grow(Count + 1);
        _items[Count++] = primitive;
    }

    /// <summary>Convenience form: builds and appends the primitive in one call.</summary>
    public void Add(TextureKey key, bool semiTransparent, in Ps1Vertex v0, in Ps1Vertex v1,
        in Ps1Vertex v2, int sortKey = 0)
    {
        Add(new Ps1Primitive(key, semiTransparent, in v0, in v1, in v2, sortKey));
    }

    public void AddRange(ReadOnlySpan<Ps1Primitive> primitives)
    {
        if (primitives.Length == 0) return;
        EnsureCapacity(Count + primitives.Length);
        primitives.CopyTo(_items.AsSpan(Count));
        Count += primitives.Length;
    }

    public void Clear() => Count = 0;

    /// <summary>Grows the backing array so <paramref name="capacity"/> items fit without reallocating.</summary>
    public void EnsureCapacity(int capacity)
    {
        if (capacity > _items.Length) Grow(capacity);
    }

    private void Grow(int required)
    {
        int size = _items.Length == 0 ? InitialCapacity : _items.Length * 2;
        if (size < required) size = required;
        Array.Resize(ref _items, size);
    }
}
