namespace FutureCop.Game.Mech;

/// <summary>
/// Hover-form drive of the player unit: func_80053A74 gTickHoverPhysics, the integrator
/// func_80055988 runs while state +0xAD == 1. Line numbers refer to src/game/80053A74.c.
/// </summary>
public static class MechHover
{
    /// <summary>Looping sound tracks func_80053A74 binds (:175-224): idle, driving, jump button.</summary>
    public const int IdleSound = 0x28, DriveSound = 0x29, LiftSound = 0x2A;

    /// <summary>Horizontal speed limit (:413-414).</summary>
    public const int SpeedLimit = 0x92147;

    /// <summary>
    /// func_80053A74(o, dt, steps, flags): <paramref name="dt"/> is the frame tick count
    /// (func_800588D4 arg1), <paramref name="steps"/> the number of sub-steps (its eighths). Returns
    /// the summed landing damage (func_80052F54).
    /// </summary>
    public static int Tick(MechBody o, MechWorld world, int dt, int steps, uint flags)
    {
        unchecked
        {
            if (world.Players != 1)
                throw new NotSupportedException("2P collision (func_8005220C) and separation (func_80053730) are not ported");
            int ret = 0;
            int magV = 0, magH = 0;
            // :173 func_800538C8(s, &flags, &mag[0] (vertical), &mag[1] (horizontal)).
            int r74 = (byte)MechInput.ReadStick(world.Pads[o.Port], o.Lock != 0, o.Frozen != 0, ref flags, ref magV, ref magH);

            // :175-224 drive mode and looping sound.
            int mode = 0, anim = IdleSound;
            if ((flags & 0x2000000) != 0)
            {
                if ((flags & 0x8000000) != 0) { mode = 0x13; anim = LiftSound; }
                else if ((flags & 0x40000000) != 0) { mode = 1; anim = DriveSound; }
                else if ((int)flags < 0) { mode = 2; anim = DriveSound; }
                else if ((flags & 0x200000) != 0) { mode = 6; anim = DriveSound; }
                else if ((flags & 0x100000) != 0) { mode = 5; anim = DriveSound; }
            }
            else
            {
                if ((flags & 0x8000000) != 0) { mode = 0x13; anim = LiftSound; }
                else if ((flags & 0x40000000) != 0) { mode = 3; anim = DriveSound; }
                else if ((int)flags < 0) { mode = 4; anim = DriveSound; }
                else if ((flags & 0x200000) != 0) { mode = 8; anim = DriveSound; }
                else if ((flags & 0x100000) != 0) { mode = 7; anim = DriveSound; }
            }
            if ((flags & 0x20000000) != 0) { mode |= 0x40; anim = DriveSound; }
            else if ((flags & 0x10000000) != 0) { mode |= 0x20; anim = DriveSound; }

            // :225-234
            o.State = (byte)(mode & 0x1F);
            if (o.State == 0)
            {
                o.Throttle = (short)(ushort)(o.Throttle + dt);
                if (o.Throttle > 0x800) o.Throttle = 0x800;
            }
            else o.Throttle = 0;
            world.SetLoopingSound(o.Port, anim);

            var normal = new int[3];
            var v18 = new int[3];
            var v28 = new int[3];
            IGroundSurface? surface = null; // v64: one stack slot, garbage until a probe writes it
            for (int i = steps; i > 0; i--)
            {
                bool rest = false;
                // :243-280 bank angle.
                if (r74 != 0)
                {
                    if ((mode & 0x40) != 0) o.Bank = (short)((uint)(magH * 3) >> 2);
                    else if ((mode & 0x20) != 0) o.Bank = (short)((-(magH * 96)) >> 7);
                    else o.Bank = 0;
                }
                else
                {
                    if ((mode & 0x40) != 0)
                    {
                        if (o.Bank < 0) o.Bank = 0;
                        o.Bank = (short)(ushort)(o.Bank + dt);
                    }
                    else if ((mode & 0x20) != 0)
                    {
                        if (o.Bank > 0) o.Bank = 0;
                        o.Bank = (short)(ushort)(o.Bank - dt);
                    }
                    else o.Bank = 0;
                    int angle = o.Bank;
                    int uangle = (ushort)o.Bank;
                    if (angle >= -0x60) { if (angle >= 0x61) uangle = 0x60; }
                    else uangle = -0x60;
                    o.Bank = (short)uangle;
                }
                // :281-282 heading += bank / 2 (bank / 8 right after a wall hit).
                o.Angle = (ushort)(o.Angle + ((int)((uint)(ushort)o.Bank << 16) >> (o.Wall != 0 ? 19 : 17)));
                // :283-295
                int scale = 0xCCC;
                if (o.Throttle >= 0x701) scale = (o.Throttle - 0x700) * 21300 / 256 + 0xCCC;
                if (o.Wall != 0)
                {
                    scale = 0x6000;
                    if (o.State != (mode & 0x1F) && (uint)(o.State - 1) < 8) o.State = 0;
                    MechWalker.SnapToWall(o);
                    o.Wall = 0;
                }
                // :296-301
                o.Angle &= 0x0FFF;
                (o.AxisX, o.AxisZ) = MechMath.SinCos(o.Angle);
                // :302-345 thrust per mode.
                int a1 = 0, a2 = 0;
                o.AxisY = 0;
                v18[1] = 0;
                switch (o.State)
                {
                    case 3: a1 = 0x6666; break;
                    case 4: a1 = -0x6666; break;
                    case 1: a1 = 0x3333; break;
                    case 2: a1 = -0x3333; break;
                    case 6: a1 = 0x1000; a2 = 1; break;
                    case 5: a1 = -0x1000; a2 = 1; break;
                    case 8: a1 = 0x2000; a2 = 1; break;
                    case 7: a1 = -0x2000; a2 = 1; break;
                    case 19: if ((o.Bits & 1) == 0) scale = 0x2666; break;
                    case 0: if ((o.Bits & 1) == 0) rest = true; break;
                }
                // :346-356
                int t0 = 0;
                if ((uint)(o.State - 1) < 4)
                {
                    if (r74 != 0) a1 = (a1 * magV) >> 7;
                    if ((flags & 0x200000) != 0) t0 = 1;
                    else if ((flags & 0x100000) != 0) t0 = 2;
                }
                // :357-359 func_800537D0, then the thrust is laid into the ground plane.
                MechWalker.AxisDelta(o, a1, a2, v18, t0);
                normal[0] = o.NormalX; normal[1] = o.NormalY; normal[2] = o.NormalZ;
                MechMath.GteCross(normal, v18, v28);
                MechMath.GteCross(v28, normal, v18);
                // :360-403 slope push, drag, gravity.
                if ((o.Bits & 1) == 0)
                {
                    int vx = o.NormalX, s0v = o.NormalY, vz = o.NormalZ;
                    var v50 = new[] { vx, vz };
                    int push = MechMath.Length2(v50);
                    if (push < 0x4000) push = 0;
                    else
                    {
                        push -= 0x4000;
                        push = MechMath.Fmul(push, MechMath.Fmul(push, push));
                        push <<= 5;
                    }
                    MechMath.Scale2(push, v50);
                    vx = v50[0];
                    vz = v50[1];
                    s0v = s0v >= 0 ? (s0v > 0x10000 ? 0x10000 : s0v) : 0;
                    s0v = s0v > 0xD999 ? 0x10000 : s0v + 0x2666;
                    int g = MechMath.Fmul(0x9800, s0v);
                    v18[0] += MechMath.Fmul(vx, g);
                    v18[1] += MechMath.Fmul(s0v, g);
                    v18[2] += MechMath.Fmul(vz, g);
                    v18[1] -= MechMath.Fmul(o.MotionY, scale);
                }
                v18[0] -= MechMath.Fmul(o.MotionX, scale);
                v18[2] -= MechMath.Fmul(o.MotionZ, scale);
                v18[1] += -0x9800;
                // :404-417
                bool res = MechWalker.ApplyKnockback(o, v18);
                o.MotionX += v18[0];
                o.MotionY += v18[1];
                o.MotionZ += v18[2];
                if (o.MotionY < -0xC0000) o.MotionY = -0xC0000;
                var v58 = new[] { o.MotionX, o.MotionZ };
                if (MechMath.Length2(v58) > SpeedLimit)
                {
                    MechMath.Scale2(SpeedLimit, v58);
                    o.MotionX = v58[0];
                    o.MotionZ = v58[1];
                }
                // :418-432 come to rest below 0x2000 on the largest axis.
                if (!res && rest)
                {
                    int p = Abs(o.MotionX), q = Abs(o.MotionY);
                    int r = q - p;
                    p = q - ((r >> 31) & r);
                    q = Abs(o.MotionZ);
                    r = q - p;
                    p = q - ((r >> 31) & r);
                    if (p < 0x2000) o.MotionX = o.MotionY = o.MotionZ = 0;
                }
                // :433-437
                int prevX = o.X, prevY = o.Y, prevZ = o.Z;
                o.X += o.MotionX >> 8;
                o.Y += o.MotionY >> 8;
                o.Z += o.MotionZ >> 8;
                MechWalker.ClampToWorld(o, world.Map);
                // :438-450 ground contact; the probe writes the normal straight into s + 0x168.
                var ground = MechGround.Probe(world, new Vec3(o.X, o.Y + 0xCCC, o.Z));
                if (ground.Normal is Vec3 n) (o.NormalX, o.NormalY, o.NormalZ) = (n.X, n.Y, n.Z);
                if (ground.SurfaceWritten) surface = ground.Surface;
                int floor = ground.Height + o.LiftHeight;
                MechPlatform.Ride(o, surface);
                o.Slow = 0;
                o.Hazard = 0;
                int attribute = ground.Attribute & ~0xC0;
                if (attribute != 0) MechWalker.ApplySurfaceAttribute(o, world, attribute);
                // :451-487
                if (o.Y < floor)
                {
                    if ((o.Bits & 1) != 0)
                    {
                        o.Bits = (byte)((o.Bits & 0xFE) | 2);
                        int vv = o.MotionY;
                        int impact = -vv;
                        if (vv < 0) o.MotionY = 0;
                        if (impact > 0x50000)
                        {
                            o.State = 0x12;
                            ret += MechWalker.FallDamage(o, world, impact); // $a1 = -vy (asm 800543BC)
                        }
                    }
                    o.Y = floor;
                }
                else if (o.Y - floor < 0xCC) o.Y = floor;
                else if ((uint)(o.State - 1) < 2)
                {
                    o.State = 0;
                    var back = MechGround.Probe(world, new Vec3(prevX, prevY + 0xCCC, prevZ));
                    if (o.Y < back.Height || o.Y - floor < 0x333)
                    {
                        o.X = prevX; o.Y = prevY; o.Z = prevZ;
                        int masked = back.Attribute & ~0xC0;
                        if (masked != 0) MechWalker.ApplySurfaceAttribute(o, world, masked);
                        o.MotionX = 0;
                        o.MotionZ = 0;
                    }
                    else o.Bits |= 1;
                }
                else o.Bits |= 1;
                // :488-492 grounded: the velocity is laid into the ground plane.
                if ((o.Bits & 1) == 0)
                {
                    normal[0] = o.NormalX; normal[1] = o.NormalY; normal[2] = o.NormalZ;
                    var motion = new[] { o.MotionX, o.MotionY, o.MotionZ };
                    MechMath.Cross(normal, motion, v28);
                    MechMath.Cross(v28, normal, motion);
                    (o.MotionX, o.MotionY, o.MotionZ) = (motion[0], motion[1], motion[2]);
                }
                // :493 func_80052708(o, func_8005220C(o)): no actor candidates with one player.
                MechWalker.PushOffWalls(o, world, 0);
            }
            // :496-501
            var velocity = new[] { o.MotionX, o.MotionY, o.MotionZ };
            int length = MechMath.NormalizeGetLength(velocity) >> 11;
            (o.VelocityX, o.VelocityY, o.VelocityZ) = (velocity[0], velocity[1], velocity[2]);
            o.Speed = length;
            if (length == 0) (o.VelocityX, o.VelocityY, o.VelocityZ) = (o.AxisX, o.AxisY, o.AxisZ);
            return ret;
        }
    }

    private static int Abs(int v) => unchecked(((v >> 31) + v) ^ (v >> 31));
}
