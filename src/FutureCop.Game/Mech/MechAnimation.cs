using FutureCop.Assets;

namespace FutureCop.Game.Mech;

/// <summary>Animation track of the walker body (func_800448D8, func_80044B34, func_800449F8, func_8002A5B4, func_80044AE8).</summary>
public static class MechAnimation
{
    /// <summary>
    /// func_800448D8: selects clip <paramref name="index"/>; direction &gt; 0 runs start..end,
    /// otherwise end..start (reversed again when start &gt; end); direction 2 keeps the elapsed
    /// time. Returns false (track unchanged) when the track has no clip table.
    /// </summary>
    public static bool Play(AnimTrack track, int index, int direction, byte flags)
    {
        if (track.Clips == null) return false;
        track.Index = (byte)index;
        track.ClipIndex = index;
        var clip = track.Clips[index];
        if (direction != 2) track.Elapsed = 0;
        if (direction > 0) { track.Step = 1; track.Target = clip.End; }
        else { track.Step = -1; track.Target = clip.Start; }
        if (clip.Start > clip.End) track.Step = (sbyte)-track.Step;
        track.Frame = clip.Start;
        track.Flags |= flags;
        return true;
    }

    /// <summary>
    /// func_80044B34(self, &amp;self->track, speed, direction) as func_800549F0 calls it: runs the
    /// queued script commands (func_800449F8), advances the elapsed time by <paramref name="speed"/>
    /// and steps frames, follows clip transitions (next0/1/2 by direction), and returns the packed
    /// event word (bit 0 = clip end, then 6-bit event codes from the clip code fields).
    /// </summary>
    public static uint Advance(MechBody body, MechWorld world, int speed, int direction) =>
        Advance(body, body.Track, world, speed, direction);

    /// <summary>
    /// func_80044B34(self, track, speed, direction) on any track of the unit (func_80055988 also
    /// drives the companion track at model + 0xA0); the unit's queued commands run on that track.
    /// </summary>
    public static uint Advance(MechBody body, AnimTrack track, MechWorld world, int speed, int direction) =>
        Advance(body.Commands, track, world.Random.Next, speed, direction);

    /// <summary>
    /// func_80044B34(self, track, speed, direction) for an entity without a walker body: a spawned
    /// Csac unit or a hover enemy keeps its own command queue (entity +0x0C) and its own track
    /// (+0x120), and draws from the mission generator (800324E0.c:162, 80038500.c:266).
    /// </summary>
    public static uint Advance(byte[] commands, AnimTrack track, Func<int> nextRandom, int speed, int direction)
    {
        unchecked
        {
            if (track.Clips == null) return 0;
            RunScript(commands, track);
            if ((track.Flags & 2) != 0) return 0;
            var clip = track.Clip;
            uint bits = 0;
            int shift = 1;
            while (true)
            {
                if ((track.Flags & 1) != 0)
                {
                    track.Flags &= unchecked((byte)~1);
                    if (clip.Code != 0) Event(clip.Code, track.Frame, ref shift, ref bits);
                }
                if (clip.Type == 0)
                {
                    if (clip.Code != 0) Event(clip.Code, track.Frame, ref shift, ref bits);
                    return bits;
                }
                if (clip.Type == 3)
                {
                    int delta = (clip.End - clip.Start) * speed;
                    track.Frame = (short)((ushort)clip.Start + (delta >> 16));
                    return bits;
                }
                if ((clip.Flags & 0x40) != 0)
                {
                    int r = nextRandom() & 0x3F;
                    direction = r >= 0x2B ? 2 : r < 0x16 ? 0 : 1;
                }
                if (track.Step == 0) return bits;
                if ((clip.Flags & 0x20) != 0)
                {
                    int temp = (0x80 - (nextRandom() & 0xFF)) * speed;
                    speed += temp >> 9;
                }
                track.Elapsed += speed;
                speed = 0;
                bool restart = false;
                if (track.Elapsed > clip.Duration)
                {
                    do
                    {
                        track.Elapsed -= clip.Duration;
                        track.Frame = (short)(track.Frame + track.Step);
                        if (clip.Code != 0) Event(clip.Code, track.Frame, ref shift, ref bits);
                        switch (clip.Mode)
                        {
                            case 0:
                                if (track.Frame == track.Target + track.Step)
                                {
                                    bits |= 1;
                                    int next = direction != 0 ? (direction == 1 ? clip.Next1 : clip.Next2) : clip.Next0;
                                    if (track.Index != next)
                                    {
                                        Play(track, next, 2, 1);
                                        clip = track.Clip;
                                        restart = true;
                                        break;
                                    }
                                    track.Frame = (short)(clip.Start + clip.End - track.Target);
                                    if (clip.Code != 0) Event(clip.Code, track.Frame, ref shift, ref bits);
                                }
                                break;
                            case 1:
                                if (track.Frame == track.Target)
                                {
                                    track.Step = (sbyte)-track.Step;
                                    track.Target = (short)(clip.Start + clip.End - track.Target);
                                }
                                break;
                            case 2:
                            {
                                int target = track.Target;
                                if (track.Frame == target + track.Step)
                                {
                                    track.Frame = track.Target;
                                    if (clip.Code != 0) Event(clip.Code, target, ref shift, ref bits);
                                    bits |= 1;
                                }
                                break;
                            }
                        }
                        if (restart) break;
                    } while (track.Elapsed > clip.Duration);
                }
                if (!restart) return bits;
            }
        }
    }

    /// <summary>func_80044AE8: appends the 6-bit event of <paramref name="code"/> when its key matches the frame.</summary>
    private static void Event(ushort code, int key, ref int shift, ref uint bits)
    {
        if ((uint)key == (code & 0x3FFu) && shift < 0x1A)
        {
            bits |= (uint)((code & 0xFC00) >> 10) << shift;
            shift += 6;
        }
    }

    /// <summary>func_800449F8 with func_8002A5B4: executes ops 0x32 (play), 0x33 (resume), 0x34 (pause) and removes them.</summary>
    private static void RunScript(byte[] commands, AnimTrack track)
    {
        for (int i = 0; i < 4;)
        {
            int op = commands[i * 2];
            if (op == 0) break;
            int arg = commands[i * 2 + 1];
            switch (op)
            {
                case 0x32: Play(track, arg, 1, 0); break;
                case 0x33: track.Flags &= unchecked((byte)~2); track.Frame = (short)arg; break;
                case 0x34: track.Flags |= 2; track.Frame = (short)arg; break;
                default: i++; continue;
            }
            for (int j = i + 1; j < 4; j++)
            {
                commands[(j - 1) * 2] = commands[j * 2];
                commands[(j - 1) * 2 + 1] = commands[j * 2 + 1];
            }
            commands[6] = 0;
        }
    }
}
