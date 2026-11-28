using FutureCop.Assets;
using FutureCop.Game.Mech;

namespace FutureCop.Game;

/// <summary>
/// Animation state of one spawned entity: the four queued commands (entity +0x0C), the track at
/// +0x120 and the frame word +0x12C that the draw path submits through gSetModelFrame.
/// </summary>
/// <remarks>
/// <para>
/// Hover enemies advance the track in message 6 while the model instance carries a speed word
/// (<c>o-&gt;unk28-&gt;unk10 != 0</c>, 800324E0.c:162-165):
/// <c>gAdvanceAnimTrack(o, o + 0x120, dt, 0)</c> followed by
/// <c>gSetModelFrame(o-&gt;unk28, o-&gt;unk12C)</c>. Type-5 and type-28 entities do the same in their
/// message 6 (gHandleType5Entity.c:282-283, func_800382B4), but scale the speed by +0x84 unless the
/// current clip is of type 1 (gHandleType5Entity.c:258-264).
/// </para>
/// <para>
/// A model without an animation chunk never advances: retail gates the whole block on that speed
/// word, and every army model of the Precinct Assault mission has a single frame and no clip
/// (models 30, 37, 56, 57, 58, 59, 60), so a tank's destruction stays a disappearance. Only the
/// type-9 flyer (model 22, three frames, a type-1 clip of ten ticks) carries one. Model 49 (CONFT
/// record 72, eleven frames, a clip of 870) does have a clip table, but its record is dispatch type
/// 99, which gGetEntityHandlerForType (8002AE54) does not map to a handler: no message 6 or 9 ever
/// reaches it, so retail leaves that track untouched as well - the model stays a static map payload.
/// </para>
/// </remarks>
public sealed class UnitAnimation
{
    /// <summary>obj+0x0C: four queued {op, arg} command pairs (func_800449F8).</summary>
    private readonly byte[] _commands = new byte[8];

    /// <param name="clips">The model's AnmD clip table (model +0x30).</param>
    /// <param name="nextRandom">The mission generator (func_80019668) the clip flags consume.</param>
    public UnitAnimation(IReadOnlyList<AnimationClip> clips, Func<int> nextRandom)
    {
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(nextRandom);
        Track = new AnimTrack { Clips = clips };
        _nextRandom = nextRandom;
        if (clips.Count > 0) MechAnimation.Play(Track, 0, 1, 0);
    }

    private readonly Func<int> _nextRandom;

    /// <summary>obj+0x120: the track record the draw path and the advance share.</summary>
    public AnimTrack Track { get; }

    /// <summary>True when the model has a clip table, i.e. the entity animates at all.</summary>
    public bool IsAnimated => Track.Clips is { Count: > 0 };

    /// <summary>obj+0x12C: the frame the draw path submits (gSetModelFrame).</summary>
    public short Frame => Track.Frame;

    /// <summary>gAdvanceAnimTrack (func_80044B34) on this track; returns the packed event word.</summary>
    public uint Advance(int speed, int direction = 0) =>
        MechAnimation.Advance(_commands, Track, _nextRandom, speed, direction);
}
