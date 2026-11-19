using FutureCop.Assets;

namespace FutureCop.Game.Mech;

/// <summary>Static-terrain, single-player WALK initialization from mission records.</summary>
public static class MechSpawn
{
    public static MechBody CreateWalker(PlayerActor actor, Model model, LevelMap map)
    {
        if (actor.Spawn is not { } spawn || spawn.Heading < 0)
            throw new InvalidDataException($"Actor {actor.Id} has no supported WALK spawn record");

        // func_800579D4 selects a static surface and adds 0x200; func_800573A4 sets the heading.
        int rank = spawn.GroundSelector switch { 0 => -1, 1 => -2, _ => spawn.GroundSelector - 2 };
        int ground = LevelGround.PlacementHeight(map, spawn.X, spawn.Z, rank)
            ?? throw new InvalidDataException($"Actor {actor.Id} has no spawn surface at rank {rank}");
        // func_80051AF8 resets the motion and filtered normal and starts the WALK rest clip.
        var body = new MechBody
        {
            X = spawn.X, Y = ground + 0x200, Z = spawn.Z,
            Angle = (ushort)(spawn.Heading & 0xFFF), Bits = 1, NormalY = 0x10000,
            Health = (short)Math.Min(spawn.Health, (ushort)short.MaxValue),
        };
        (body.AxisX, body.AxisZ) = MechMath.SinCos(body.Angle);
        body.Track.Clips = model.AnimationClips;
        if (model.AnimationClips.Count == 0)
            throw new InvalidDataException($"Model {model.Id} has no WALK animation table");
        MechAnimation.Play(body.Track, 0, 1, 0);

        // func_800518A8 at t=0: X centre, Z centre and the two half-extents come from 3DBB.
        // func_80055988 rebuilds these only during morph; WALK keeps the rest-frame corners.
        int frame = body.Track.Frame;
        if ((uint)frame >= (uint)model.FrameBounds.Count)
            throw new InvalidDataException($"Model {model.Id} has no 3DBB record for WALK rest frame {frame}");
        var b = model.FrameBounds[frame];
        short left = unchecked((short)(b[0] - b[3])), right = unchecked((short)(b[0] + b[3]));
        short front = unchecked((short)(b[2] + b[5])), back = unchecked((short)(b[2] - b[5]));
        const short height = 174;
        new short[] { left, height, front, right, height, front, right, height, back, left, height, back }
            .CopyTo(body.Footprint, 0);
        MechWalker.SetBounds(body, compact: false);
        return body;
    }
}
