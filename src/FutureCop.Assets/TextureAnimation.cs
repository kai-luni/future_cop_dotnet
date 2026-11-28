namespace FutureCop.Assets;

/// <summary>
/// One 3DTA entry: a texture-coordinate flipbook. The retail model loader links the chunk into
/// the per-frame animation list <c>D_8009F564</c> (func_80012E48:66-73, node cursor over the
/// chunk header: <c>next = list</c>, <c>count</c> = chunk index, entries start at chunk + 0x0C,
/// the frame words follow the entries) and func_800136C8 copies the active frame into the model's
/// 3DTL record every time the index changes.
/// </summary>
/// <param name="Max">Frame count (entry byte 0).</param>
/// <param name="Type">Step mode (entry byte 2): 0 forward loop, 1 reverse loop, 2 ping-pong, 3 random.</param>
/// <param name="Flags">Entry byte 3: bit 0 = four halfwords per frame instead of three, bit 2 = entry disabled.</param>
/// <param name="Rate">Ticks between frames (entry +0x04, s16).</param>
/// <param name="DstOffset">
/// 3DTL byte offset the frame words are copied to (entry +0x0C). The loader turns it into
/// <c>triangleList + offset</c> (func_80012E48:66-73).
/// </param>
/// <param name="Frames">
/// The frame words, <see cref="Max"/> rows of four (flag bit 0 set) or three halfwords.
/// </param>
public readonly record struct TextureAnimation(
    byte Max, byte Type, byte Flags, short Rate, ushort DstOffset, ushort[][] Frames)
{
    /// <summary>Byte offset of the 16-byte 3DTL texture record the copy lands in.</summary>
    public int RecordOffset => DstOffset & ~0xF;

    /// <summary>First 32-bit word of that record the copy overwrites; 1 is the pair of UV words.</summary>
    public int WordIndex => (DstOffset & 0xF) >> 2;

    /// <summary>
    /// True when the entry rewrites the complete UV set of its record (record + 4, four halfwords).
    /// The player light submodel and all but three shipped entries use this form; the three entries
    /// that patch record + 8 (model 69 of OV.MIS, 45 of M4A1.MIS and 126 of M2C.MIS) are stepped but
    /// the draw path does not apply them.
    /// </summary>
    public bool CoversUvPair => WordIndex == 1 && (Flags & 1) != 0;
}

/// <summary>
/// Playback state of one <see cref="TextureAnimation"/>, i.e. one AnimEntry of the retail list that
/// func_800136C8 steps once per frame with <c>gFrameDelta</c> (8002703C.c:113). The entry starts at
/// <c>cur = 0</c>, <c>timer = 0</c> (on-disc values, func_80012E48 keeps them), so the first step
/// also switches the texture: the timer goes negative and the index moves.
/// </summary>
public sealed class TextureAnimationState
{
    private bool _reverse;

    public TextureAnimationState(TextureAnimation animation)
    {
        Animation = animation;
        _reverse = (animation.Flags & 2) != 0;
    }

    public TextureAnimation Animation { get; }

    /// <summary><c>e-&gt;cur</c>: index of the active frame.</summary>
    public byte Frame { get; private set; }

    /// <summary><c>e-&gt;timer</c>: retail clock ticks left until the next step (s16 in retail).</summary>
    public int Timer { get; private set; }

    /// <summary>
    /// func_800136C8:88-138: skips disabled entries, counts the timer down by <paramref name="dt"/>
    /// and steps the frame index on expiry, then adds the rate to the timer. Types 3 and 16+ need
    /// the PRNG and the sine table of the retail paths and are not ported; no shipped 3DTA entry
    /// uses them (every entry of every mission is type 1).
    /// </summary>
    public void Advance(int dt)
    {
        var a = Animation;
        if ((a.Flags & 4) != 0) return;
        // Retail reads the stored s16 as u16 before subtracting the delta and stores the sum back
        // as s16 (func_800136C8:92-97); a stale negative timer therefore wraps exactly like this.
        int t = (ushort)Timer - dt;
        Timer = (short)t;
        if ((short)t > 0) return;
        Timer = (short)(t + (ushort)a.Rate);
        switch (a.Type)
        {
            case 0:
                Frame++;
                if (Frame == a.Max) Frame = 0;
                break;
            case 1:
                if (Frame == 0) Frame = a.Max;
                Frame--;
                break;
            case 2:
                if (_reverse)
                {
                    if (Frame == 0) { Frame = 1; _reverse = false; }
                    else Frame--;
                }
                else
                {
                    Frame++;
                    if (Frame == a.Max) { Frame = (byte)(a.Max - 2); _reverse = true; }
                }
                break;
        }
    }

    /// <summary>The UV words of the active frame, or null when nothing can be applied.</summary>
    public (uint Uv0, uint Uv1)? CurrentUvPair()
    {
        var a = Animation;
        if (!a.CoversUvPair || Frame >= a.Frames.Length) return null;
        var f = a.Frames[Frame];
        if (f.Length < 4) return null;
        return (((uint)f[1] << 16) | f[0], ((uint)f[3] << 16) | f[2]);
    }
}

/// <summary>
/// The texture-animation list <c>D_8009F564</c> as far as the port needs it: the 3DTA entries of
/// the loaded models, stepped once per frame and read by the model draw path. Retail keeps one
/// global list and the draw emits the patched 3DTL words (func_800136C8:155-172 writes straight
/// into the model's triangle list), so the per-model grouping is only a lookup convenience.
/// </summary>
public sealed class TextureAnimationSet
{
    private readonly List<TextureAnimationState> _states = new();
    private readonly Dictionary<(int Model, int Record), TextureAnimationState> _byRecord = new();

    public TextureAnimationSet(IEnumerable<Model> models)
    {
        foreach (var model in models)
        {
            foreach (var animation in model.TextureAnimations)
            {
                if (animation.Max == 0) continue;
                var state = new TextureAnimationState(animation);
                _states.Add(state);
                if (animation.CoversUvPair)
                    _byRecord[(model.Id, animation.RecordOffset)] = state;
            }
        }
    }

    /// <summary>func_800136C8(a0 = gFrameDelta) for every entry of every model.</summary>
    public void Advance(int dt)
    {
        foreach (var state in _states) state.Advance(dt);
    }

    /// <summary>Current UV words of the record at <paramref name="recordOffset"/> of a model.</summary>
    public bool TryGetUvPair(int modelId, int recordOffset, out uint uv0, out uint uv1)
    {
        uv0 = uv1 = 0;
        if (!_byRecord.TryGetValue((modelId, recordOffset), out var state)) return false;
        if (state.CurrentUvPair() is not { } uv) return false;
        (uv0, uv1) = uv;
        return true;
    }
}
