using FutureCop.Assets;

namespace FutureCop.Game.Mech;

/// <summary>
/// Walker (on-foot) locomotion of the player mech: ports of func_800549F0 and its callees.
/// Every method names the retail unit it reproduces; parity is checked against the unchanged
/// decomp C by tests/golden/mech-walk.vectors.
/// </summary>
public static partial class MechWalker
{
    /// <summary>
    /// func_80054590: the per-step drive delta. <paramref name="speed"/> along the facing axis,
    /// <paramref name="lane"/> != 0 strafes (axis turned by 90 degrees), <paramref name="direction"/>
    /// 1/2 mixes in the side axis (181/256 diagonal), the slowdown level scales the drive, then either
    /// the 0x666 damping (airborne or states 0xB/0xC/0xE/0x10) or the ground slope push and drag
    /// is added, and finally the -0x9800 gravity on delta[1]. func_80053730 (2P camera distance) is
    /// a no-op with one player and has no movement effect. delta[1] is only added to, never reset.
    /// </summary>
    public static void DriveDelta(MechBody body, int speed, int lane, int[] delta, int direction)
    {
        unchecked
        {
            if (speed != 0)
            {
                int r0 = body.AxisX, r1 = body.AxisZ, t2 = speed;
                if (lane == 0)
                {
                    if (direction != 0)
                    {
                        speed *= 181;
                        speed = (speed < 0 ? speed + 0xFF : speed) >> 8;
                        t2 = speed;
                        bool below = speed < 1;
                        bool first = direction == 1 ? below : !below;
                        if (first) (r0, r1) = (r0 - r1, r1 + r0);
                        else (r0, r1) = (r0 + r1, r1 - r0);
                    }
                }
                else
                {
                    int a = r0;
                    r0 = r1;
                    r1 = -a;
                }
                delta[0] = MechMath.Fmul(t2, r0);
                delta[2] = MechMath.Fmul(speed, r1);
            }
            else
            {
                delta[0] = 0;
                delta[2] = 0;
            }

            int n = (sbyte)body.Slow;
            if (n != 0)
            {
                int scale = n * 5;
                delta[0] = delta[0] * scale / 128;
                delta[2] = delta[2] * scale / 128;
            }

            int z;
            if ((body.Bits & 1) != 0 || body.State is 0xB or 0xC or 0xE or 0x10)
            {
                delta[0] -= MechMath.Fmul(body.MotionX, 0x666);
                z = delta[2] - MechMath.Fmul(body.MotionZ, 0x666);
            }
            else
            {
                var vec = new[] { body.NormalX, body.NormalZ };
                int up = body.NormalY;
                int push = MechMath.Length2(vec);
                if (push < 0x4CCC) push = 0;
                else
                {
                    push -= 0x4CCC;
                    int e = MechMath.Fmul(push, push);
                    push = MechMath.Fmul(push, e);
                    push *= 40;
                }
                MechMath.Scale2(push, vec);
                int r0 = (sbyte)body.Slow;
                int px = vec[0], pz = vec[1];
                if (r0 != 0)
                {
                    r0 = 4 - r0;
                    px += px * r0 / 4;
                    pz += pz * r0 / 4;
                }
                up = up < 0 ? 0 : up > 0x10000 ? 0x10000 : up;
                up = up > 0xD999 ? 0x10000 : up + 0x2666;
                int g = MechMath.Fmul(0x9800, up);
                delta[0] += MechMath.Fmul(px, g);
                delta[1] += MechMath.Fmul(up, g);
                delta[2] += MechMath.Fmul(pz, g);
                int drag = -0x6000;
                if (r0 != 0) drag = r0 * drag / 32;
                delta[0] += MechMath.Fmul(body.MotionX, drag);
                delta[1] += MechMath.Fmul(body.MotionY, -0x6000);
                z = delta[2] + MechMath.Fmul(body.MotionZ, drag);
            }
            delta[2] = z;
            delta[1] += -0x9800;
        }
    }

    /// <summary>
    /// func_80052550: applies a pending impulse (state +0x150) to <paramref name="vec"/> (halved while
    /// airborne, then shifted right by 1 unless walker form with no slowdown), removes the impulse
    /// direction from the motion vector, decays the motion by 1/8 (vertical reset above 0x100) and
    /// clears the impulse. Returns false (and changes nothing) when no impulse is pending.
    /// </summary>
    public static bool ApplyKnockback(MechBody body, int[] vec)
    {
        unchecked
        {
            if (body.ImpulseX == 0 && body.ImpulseY == 0 && body.ImpulseZ == 0) return false;
            if ((body.Bits & 1) != 0)
            {
                body.ImpulseX >>= 1; body.ImpulseY >>= 1; body.ImpulseZ >>= 1;
            }
            int shift = body.Form != 0 || body.Slow != 0 ? 1 : 0;
            vec[0] += body.ImpulseX >> shift;
            vec[1] += body.ImpulseY >> shift;
            vec[2] += body.ImpulseZ >> shift;
            var dir = new[] { body.ImpulseX, body.ImpulseY, body.ImpulseZ };
            MechMath.Normalize(dir);
            int dot = MechMath.Dot(dir[0], dir[1], dir[2], body.MotionX, body.MotionY, body.MotionZ);
            body.MotionX += MechMath.Fmul(dir[0], -dot);
            body.MotionY += MechMath.Fmul(dir[1], -dot);
            body.MotionZ += MechMath.Fmul(dir[2], -dot);
            body.MotionX -= body.MotionX / 8;
            body.MotionY -= body.MotionY / 8;
            if (body.MotionY >= 0x101) body.MotionY = 0;
            body.MotionZ -= body.MotionZ / 8;
            body.ImpulseX = body.ImpulseY = body.ImpulseZ = 0;
            return true;
        }
    }

    /// <summary>
    /// func_80053188: reaction to the ground attribute byte. 0x20..0x2F push the position
    /// (conveyors), 1 marks a hazard (sinking: +0xDE, or a forced hover request when the session
    /// word is set or the player is invulnerable), 2 and 6..15 post damage messages through
    /// func_8005908C (recorded via <see cref="MechWorld.ScriptMessage"/>), 3/4/5 set the slowdown
    /// level 2/1/3. The slowdown byte is rewritten on every call (0 for every other attribute).
    /// </summary>
    public static void ApplySurfaceAttribute(MechBody body, MechWorld world, int attribute)
    {
        unchecked
        {
            uint a = (uint)attribute;
            int state = 0;
            if (a - 0x20 < 0x10)
            {
                switch (a)
                {
                    case 0x20: body.Z += 0x200; break;
                    case 0x21: body.Z += 0x400; break;
                    case 0x22: body.Z -= 0x200; break;
                    case 0x23: body.Z -= 0x400; break;
                    case 0x24: body.X += 0x200; break;
                    case 0x25: body.X += 0x400; break;
                    case 0x26: body.X -= 0x200; break;
                    case 0x27: body.X -= 0x400; break;
                    case 0x2B: body.X -= 0x92; break;
                    case 0x2A: body.X += 0x92; break;
                    case 0x28: body.Z += 0x92; break;
                    case 0x29: body.Z -= 0x92; break;
                    case 0x2F: body.X -= 0x124; break;
                    case 0x2E: body.X += 0x124; break;
                    case 0x2C: body.Z += 0x124; break;
                    case 0x2D: body.Z -= 0x124; break;
                }
            }
            else
            {
                int value = 0;
                if (a < 0x10)
                {
                    int side = body.Port;
                    switch (a)
                    {
                        case 1:
                            body.Hazard = 1;
                            if (world.SessionMode[body.Port != 0 ? 1 : 0] != 0 || (body.Form == 0 && world.Invulnerable[side]))
                            {
                                if (body.Form == 0) body.Request = 1;
                            }
                            else if (body.Form == 0)
                            {
                                body.Grab = 1;
                                body.HitTimer = 0;
                            }
                            break;
                        case 2:
                            if (!world.Invulnerable[side]) value = 10000;
                            break;
                        case 3: state = 2; break;
                        case 4: state = 1; break;
                        case 5: state = 3; break;
                        default:
                            if (a >= 6)
                            {
                                if (world.Invulnerable[side]) break;
                                if (a == 0xE && side != 0) break;
                                if (a == 0xF && side != 1) break;
                                value = MechTables.FormCode(body.Form, (int)a - 6);
                                if (value < 0) value = 10000;
                            }
                            break;
                    }
                }
                if ((short)value != 0)
                    world.ScriptMessage?.Invoke(new MechMessage(0xE, (int)a - 0x64, (short)value, 1));
            }
            body.Slow = (byte)state;
        }
    }

    /// <summary>
    /// func_80052708 with func_80052410: rebuilds the basis (func_80051C50), transforms the four
    /// footprint corners with it (GTE load func_80010750 + func_80010234, computed here in integer
    /// arithmetic), casts wall rays (func_800684AC, floors off) from each previous corner to its new
    /// position and along the four new edges, stores the new corners, and for every distinct wall
    /// side that was hit records it in <see cref="MechBody.Wall"/> and adds a +-0x24000 push (doubled
    /// when <paramref name="doubled"/> != 0) to the pending impulse. Returns the 24-bit hit mask.
    /// </summary>
    public static int PushOffWalls(MechBody body, MechWorld world, int doubled)
    {
        unchecked
        {
            var corners = new Vec3[4];
            var hits = new Vec3[8];
            int mask = TestFootprintEdges(body, world, corners, hits);
            for (int i = 0; i < 4; i++)
            {
                body.FootprintWorld[i * 3] = corners[i].X;
                body.FootprintWorld[i * 3 + 1] = corners[i].Y;
                body.FootprintWorld[i * 3 + 2] = corners[i].Z;
            }
            if (mask == 0) return 0;
            int avgX = (corners[0].X + corners[1].X + corners[2].X + corners[3].X) >> 2;
            int avgZ = (corners[0].Z + corners[1].Z + corners[2].Z + corners[3].Z) >> 2;
            int seen = 0, m = mask;
            for (int i = 7; i >= 0; i--)
            {
                int bits = m & 7;
                m >>= 3;
                if (bits == 0) continue;
                int flag = 1 << bits;
                if ((seen & flag) != 0) continue;
                seen |= flag;
                body.Wall = (byte)bits;
                int dx = avgX - hits[i].X, dz = avgZ - hits[i].Z;
                switch (bits)
                {
                    case 1: dz = 0; break;
                    case 2: dx = 0; break;
                    case 3:
                    {
                        int half = (dx + dz) / 2;
                        dz = half / 2;
                        dx = dz;
                        break;
                    }
                    case 4:
                    {
                        int half = (dx - dz) / 2;
                        dx = half / 2;
                        dz = -half / 2;
                        break;
                    }
                }
                dx = dx < 0 ? -0x24000 : dx > 0 ? 0x24000 : 0;
                dz = dz < 0 ? -0x24000 : dz > 0 ? 0x24000 : 0;
                if (doubled != 0) { dx <<= 1; dz <<= 1; }
                body.ImpulseX += dx;
                body.ImpulseZ += dz;
            }
            return mask;
        }
    }

    /// <summary>func_80052410 gTestFootprintEdges: new corners in <paramref name="corners"/>, ray hits in <paramref name="hits"/>.</summary>
    private static int TestFootprintEdges(MechBody body, MechWorld world, Vec3[] corners, Vec3[] hits)
    {
        unchecked
        {
            BuildBasis(body);
            for (int i = 0; i < 4; i++)
                corners[i] = TransformCorner(body, body.Footprint[i * 3], body.Footprint[i * 3 + 1], body.Footprint[i * 3 + 2]);
            int result = 0;
            for (int i = 0; i < 4; i++)
            {
                var prev = new Vec3(body.FootprintWorld[i * 3], body.FootprintWorld[i * 3 + 1], body.FootprintWorld[i * 3 + 2]);
                var r = MechCollision.Ray(world.Map, prev, corners[i], false, i);
                if (r.Hit is Vec3 h) hits[i] = h;
                result = (result << 3) | (r.Result & 7);
            }
            for (int i = 0; i < 4; i++)
            {
                var r = MechCollision.Ray(world.Map, corners[i], corners[(i + 1) & 3], false, i);
                if (r.Hit is Vec3 h) hits[i + 4] = h;
                result = (result << 3) | (r.Result & 7);
            }
            return result;
        }
    }

    /// <summary>
    /// func_80051C50 gBuildObjectBasis into <see cref="MechBody.Basis"/>: with no roll (+0xF0 == 0)
    /// rows are (up x axis, up, axis) via the GTE outer product func_80010974, otherwise
    /// gBuildRotationMatrix(angle, 0, roll) func_800188D0.
    /// </summary>
    public static void BuildBasis(MechBody body)
    {
        unchecked
        {
            var m = body.Basis;
            if (body.Lift == 0)
            {
                m[6] = body.AxisX; m[7] = body.AxisY; m[8] = body.AxisZ;
                m[3] = 0; m[4] = 0x10000; m[5] = 0;
                // func_80010974: rotation diagonal = up >> 4, IR = target >> 4, OP sf=1, results << 4.
                long d1 = (short)(m[3] >> 4), d2 = (short)(m[4] >> 4), d3 = (short)(m[5] >> 4);
                long i1 = (short)(m[6] >> 4), i2 = (short)(m[7] >> 4), i3 = (short)(m[8] >> 4);
                m[0] = (int)((d2 * i3 - d3 * i2) >> 12) << 4;
                m[1] = (int)((d3 * i1 - d1 * i3) >> 12) << 4;
                m[2] = (int)((d1 * i2 - d2 * i1) >> 12) << 4;
            }
            else RotationMatrix((short)body.Angle, 0, body.Lift, m);
        }
    }

    /// <summary>func_80010750 + func_80010234 for one corner: world = position + basis^T * (corner &lt;&lt; 4), y flipped.</summary>
    private static Vec3 TransformCorner(MechBody body, short vx, short vy, short vz)
    {
        unchecked
        {
            var w = body.Basis;
            static long R(int v) => (short)(v >> 4);
            long trx = body.X >> 4, try_ = (-body.Y) >> 4, trz = body.Z >> 4;
            long m1 = (trx * 4096 + R(w[0]) * vx + R(w[3]) * vy + R(w[6]) * vz) >> 12;
            long m2 = (try_ * 4096 + R(-w[1]) * vx + R(-w[4]) * vy + R(-w[7]) * vz) >> 12;
            long m3 = (trz * 4096 + R(w[2]) * vx + R(w[5]) * vy + R(w[8]) * vz) >> 12;
            return new Vec3((int)m1 << 4, (-(int)m2) << 4, (int)m3 << 4);
        }
    }

    /// <summary>func_800188D0 gBuildRotationMatrix (16.16, row-major m[0..8]).</summary>
    public static void RotationMatrix(int x, int y, int z, int[] m)
    {
        unchecked
        {
            z &= 0xFFF; y &= 0xFFF; x &= 0xFFF;
            if (z == 0)
            {
                if (y == 0)
                {
                    m[4] = 0x10000; m[7] = 0; m[5] = 0; m[3] = 0; m[1] = 0;
                    if (x != 0)
                    {
                        var (sx, cx) = MechMath.SinCos(x);
                        m[0] = cx; m[8] = cx; m[6] = sx; m[2] = -sx;
                    }
                    else { m[8] = 0x10000; m[0] = 0x10000; m[6] = 0; m[2] = 0; }
                }
                else
                {
                    var (sy, cy) = MechMath.SinCos(y);
                    if (x == 0)
                    {
                        m[0] = 0x10000; m[6] = 0; m[3] = 0; m[2] = 0; m[1] = 0;
                        m[8] = cy; m[4] = cy; m[5] = sy; m[7] = -sy;
                    }
                    else
                    {
                        var (sx, cx) = MechMath.SinCos(x);
                        m[0] = cx; m[1] = 0; m[2] = -sx; m[4] = cy; m[7] = -sy;
                        sy >>= 2; cy >>= 2; sx >>= 2; cx >>= 2;
                        m[3] = (sy * sx) >> 12; m[5] = (sy * cx) >> 12; m[6] = (cy * sx) >> 12; m[8] = (cy * cx) >> 12;
                    }
                }
            }
            else
            {
                var (sz, cz) = MechMath.SinCos(z);
                if (y == 0)
                {
                    if (x == 0)
                    {
                        m[6] = 0; m[0] = cz; m[8] = 0x10000; m[7] = 0; m[5] = 0; m[2] = 0; m[4] = cz; m[1] = sz; m[3] = -sz;
                    }
                    else
                    {
                        var (sx, cx) = MechMath.SinCos(x);
                        m[6] = sx; m[7] = 0; m[8] = cx; m[1] = sz; m[4] = cz;
                        sx >>= 2; sz >>= 2; cz >>= 2; cx >>= 2;
                        m[0] = (cz * cx) >> 12; m[2] = -(cz * sx) >> 12; m[3] = -(sz * cx) >> 12; m[5] = (sz * sx) >> 12;
                    }
                }
                else
                {
                    var (sy, cy) = MechMath.SinCos(y);
                    if (x == 0)
                    {
                        m[6] = 0; m[0] = cz; m[3] = -sz; m[7] = -sy; m[8] = cy;
                        sz >>= 2; cz >>= 2; cy >>= 2; sy >>= 2;
                        m[1] = (sz * cy) >> 12; m[2] = (sz * sy) >> 12; m[4] = (cz * cy) >> 12; m[5] = (cz * sy) >> 12;
                    }
                    else
                    {
                        var (sx, cx) = MechMath.SinCos(x);
                        int rawsy = sy;
                        sz >>= 2; cz >>= 2; sy >>= 2; cy >>= 2; sx >>= 2; cx >>= 2;
                        int tt = (sz * sy) >> 14;
                        m[0] = (cz * cx + tt * sx) >> 12;
                        m[7] = -rawsy;
                        m[1] = (sz * cy) >> 12;
                        m[2] = (tt * cx - cz * sx) >> 12;
                        int u = (sy * cz) >> 14;
                        m[3] = (u * sx - sz * cx) >> 12;
                        m[4] = (cz * cy) >> 12;
                        m[5] = (sz * sx + u * cx) >> 12;
                        m[6] = (cy * sx) >> 12;
                        m[8] = (cy * cx) >> 12;
                    }
                }
            }
        }
    }

    /// <summary>
    /// func_800549F0: one walker update with <paramref name="steps"/> fixed sub-steps (the eighths of
    /// the frame's tick count that gUpdatePlayerEntity accumulates, see <see cref="MechClock"/>).
    /// <paramref name="flags"/> is the control word func_80055988 passes (the translated pad mask of
    /// state + 0x30 after its masking). Returns the summed hard-landing damage (func_80052F54).
    /// </summary>
    public static int Tick(MechBody self, MechWorld world, int steps, uint flags)
    {
        unchecked
        {
            if (world.Players != 1)
                throw new NotSupportedException("2P walker collision (func_8005220C/func_80052098) and camera (func_80053730) are not ported");
            if (self.Platform != null)
                throw new NotSupportedException("standing on a dynamic object (func_80053514 with a surface) is not ported");
            int impactTotal = 0;
            int move = 0;
            int magV = 0, magH = 0;
            var pad = world.Pads[self.Port];
            int status = (byte)MechInput.ReadStick(pad, self.Lock != 0, self.Frozen != 0, ref flags, ref magV, ref magH);
            var delta = new int[3];
            Vec3? normal = null;
            int state;

            if ((flags & 0x0A000000) == 0x08000000 && (state = self.State) != 3 && (uint)(state - 0x0D) >= 2
                && (self.Bits & 0x40) == 0)
            {
                var probe = new Vec3(self.X + (self.AxisX >> 3), self.Y + 0x10000, self.Z + (self.AxisZ >> 3));
                int rise = MechGround.Probe(world.Map, probe).Height - self.Y;
                move = 0x0F;
                if (rise >= 0x0AE2)
                {
                    move = 0x0A;
                    if ((self.Boost & 1) == 0 && rise >= 0x2334) self.Bits |= 8;
                }
            }
            else if ((flags & 0x02000000) != 0)
            {
                if ((flags & 0x40000000) != 0) move = 1;
                else if ((int)flags < 0) move = 2;
                else if ((flags & 0x08000000) != 0) move = 9;
                else if ((flags & 0x00200000) != 0) move = 6;
                else if ((flags & 0x00100000) != 0) move = 5;
                if ((flags & 0x20000000) != 0) move |= 0x40;
                else if ((flags & 0x10000000) != 0) move |= 0x20;
            }
            else
            {
                if ((flags & 0x40000000) != 0) move = 3;
                else if ((int)flags < 0) move = 4;
                else if ((flags & 0x00200000) != 0) move = 8;
                else if ((flags & 0x00100000) != 0) move = 7;
                if ((flags & 0x20000000) != 0) move |= 0x40;
                else if ((flags & 0x10000000) != 0) move |= 0x20;
            }

            int remaining = steps;
            if (steps > 0)
            {
                int input = move & 0x1F;
                do
                {
                    int anim = 0;
                    if ((self.Bits & 1) == 0 && ((uint)(self.State - 1) < 8 || self.State == 0x0E) && move == 0)
                        self.State = 0;
                    bool spare = false;
                    if ((self.Bits & 0x41) != 0) self.Bits &= 0xDF;
                    if (self.State == 0 && input == 0) self.Bits &= 0xCD;
                    int action = (self.Bits & 0x10) == 0 ? (self.Bits >> 4) & 2 : 1;
                    if ((flags & 0x08000000) != 0 && self.State == 3 && action == 0) action = 2;
                    else if ((self.Track.Clip.Flags & 2) != 0)
                    {
                        if ((self.Bits & 2) != 0)
                        {
                            action = 1;
                            self.Bits &= 0xFD;
                            if (input != 0) self.State = (byte)input;
                        }
                    }
                    else if (input != self.State && (self.Track.Index != 0 || (self.Bits & 2) != 0))
                    {
                        action = 1;
                        self.Bits &= 0xFD;
                    }
                    else self.State = (byte)input;

                    bool grounded = (self.Bits & 1) == 0;
                    self.Bits &= 0xFD;
                    if (grounded)
                    {
                        int turn = (move & 0x40) != 0 ? 0x20 : (move & 0x20) != 0 ? -0x20 : 0;
                        if (status != 0) turn = (turn * magH) >> 7;
                        if (turn != 0)
                        {
                            turn -= (turn * self.Speed) >> 9;
                            self.Angle = (ushort)(self.Angle + turn);
                            anim = turn > 0 ? 0x0F : 0x0E;
                        }
                    }
                    if (self.Wall != 0)
                    {
                        if (self.State != input && ((uint)(self.State - 1) < 8 || self.State == 0x0E)) self.State = 0;
                        SnapToWall(self);
                        self.Wall = 0;
                    }
                    self.Angle &= 0x0FFF;
                    (self.AxisX, self.AxisZ) = MechMath.SinCos(self.Angle);
                    self.AxisY = 0;
                    delta[1] = 0;
                    if (self.Timer != 0) self.Timer--;

                    int speed = 0, lane = 0;
                    if ((self.Bits & 0x80) != 0 && self.State != 9)
                    {
                        SetBounds(self, false);
                        self.Bits &= 0x7F;
                    }
                    int bits = self.Bits;
                    if ((bits & 1) != 0)
                    {
                        anim = self.Clip;
                        if (self.Timer == 1 && self.State == 0x0C)
                        {
                            if ((bits & 8) == 0) speed = 0x1CCCC;
                            self.Bits = (byte)(bits & 0xF7);
                        }
                    }
                    else
                    {
                        switch (self.State)
                        {
                            case 9:
                                spare = true;
                                anim = 3;
                                if ((self.Bits & 0x80) == 0) SetBounds(self, true);
                                self.Bits |= 0x80;
                                break;
                            case 10: anim = 4; break;
                            case 11:
                                anim = 4;
                                delta[1] = (self.Boost & 1) != 0 ? 0xA0000 : 0x78000;
                                self.Timer = 0x0A;
                                self.State = 0x0C;
                                break;
                            case 16:
                                speed = 0x40000;
                                anim = 0x11;
                                delta[1] = (self.Boost & 1) != 0 ? 0x75555 : 0x58000;
                                self.State = 0x11;
                                break;
                            case 15: case 17: anim = 0x11; break;
                            case 13:
                                speed = 0x18000;
                                anim = 8;
                                delta[1] = (self.Boost & 1) != 0 ? 0x85555 : 0x64000;
                                self.State = 0x0E;
                                break;
                            case 18: spare = true; anim = 5; break;
                            case 3: speed = 0x18000; anim = 8; break;
                            case 14: anim = 8; break;
                            case 4: speed = -0x18000; anim = 9; break;
                            case 1: speed = 0xA000; anim = 6; break;
                            case 2: speed = -0xA000; anim = 7; break;
                            case 6: anim = 2; speed = 0xA000; lane = 1; break;
                            case 5: anim = 1; speed = -0xA000; lane = 1; break;
                            case 8: anim = 0x0B; speed = 0x10000; lane = 1; break;
                            case 7: anim = 0x0A; speed = -0x10000; lane = 1; break;
                            case 0:
                                spare = true;
                                if (move == 0)
                                {
                                    self.Idle++;
                                    if (self.Idle == 0) { anim = 0x10; break; }
                                    if (self.Track.Index == 0) break;
                                    if (self.Clip != 0x10) break;
                                    anim = self.Clip;
                                }
                                self.Idle = 0;
                                break;
                        }
                    }

                    int direction = 0;
                    if ((uint)(self.State - 1) < 4 && (flags & 0x08000000) == 0)
                    {
                        if (status != 0) speed = (speed * magV) >> 7;
                        if ((flags & 0x00200000) != 0) direction = 1;
                        else if ((flags & 0x00100000) != 0) direction = 2;
                    }
                    DriveDelta(self, speed, lane, delta, direction);

                    bool collision = ApplyKnockback(self, delta);
                    self.MotionX += delta[0];
                    self.MotionY += delta[1];
                    self.MotionZ += delta[2];
                    if (self.MotionY < -0xC0000) self.MotionY = -0xC0000;
                    if (!collision && spare)
                    {
                        int m = Math.Max(Math.Max(AbsRetail(self.MotionX), AbsRetail(self.MotionY)), AbsRetail(self.MotionZ));
                        int response = (sbyte)self.Slow;
                        if ((m <= 0x7FFF && response == 0) || m < (response << 9))
                            self.MotionX = self.MotionY = self.MotionZ = 0;
                    }

                    int prevX = self.X, prevY = self.Y, prevZ = self.Z;
                    self.X += self.MotionX >> 8;
                    self.Y += self.MotionY >> 8;
                    self.Z += self.MotionZ >> 8;
                    ClampToWorld(self, world.Map);

                    if (self.MotionY >= 0x11)
                    {
                        var r = MechCollision.Ray(world.Map, new Vec3(prevX, prevY + 0x24C0, prevZ),
                            new Vec3(self.X, self.Y + 0x24C0, self.Z), true, world.EntityRegister);
                        if ((r.Result & 7) != 0)
                        {
                            self.MotionY = 0;
                            self.MotionX /= 2;
                            self.MotionZ /= 2;
                        }
                    }

                    var ground = MechGround.Probe(world.Map, new Vec3(self.X, self.Y + 0x0CCC, self.Z));
                    // Retail reuses the same output slot across substeps. If no probe has written
                    // it yet, keep the body's existing normal and slope instead of using stack data.
                    normal = ground.Normal ?? normal;
                    lane = ground.Height;
                    if (self.Grab != 0)
                    {
                        lane = self.Y - 0xC0;
                        self.MotionY = 0;
                    }
                    self.Platform = null; // func_80053514 with surface 0 (empty object queue)
                    self.Slow = 0;
                    self.Hazard = 0;
                    int attribute = ground.Attribute & unchecked((int)0xFFFFFF3F);
                    if (attribute != 0) ApplySurfaceAttribute(self, world, attribute);

                    if (normal is Vec3 measuredNormal)
                    {
                        var filtered = new[]
                        {
                            MechMath.EaseQuarter(self.NormalX, measuredNormal.X),
                            MechMath.EaseQuarter(self.NormalY, measuredNormal.Y),
                            MechMath.EaseQuarter(self.NormalZ, measuredNormal.Z),
                        };
                        MechMath.Normalize(filtered);
                        (self.NormalX, self.NormalY, self.NormalZ) = (filtered[0], filtered[1], filtered[2]);
                    }

                    bool snap = true;
                    if (self.Y < lane)
                    {
                        if ((self.Bits & 1) != 0)
                        {
                            self.Bits = (byte)((self.Bits & 0xFE) | 2);
                            int vertical = self.MotionY;
                            self.MotionY = 0;
                            int limit = (self.Track.Clip.Flags & 0x10) != 0 ? 0x80000 : 0x50000;
                            int impact = -vertical;
                            if (limit < impact)
                            {
                                self.State = 0x12;
                                impactTotal += FallDamage(self, world, impact);
                            }
                        }
                        else if (self.State == 0x0C && self.MotionY < -0x40) self.MotionY = 0;
                    }
                    else if (self.Y - lane >= 0x333)
                    {
                        snap = false;
                        bool restored = false;
                        if (((uint)(self.State - 1) < 2 || self.State == 5) && (self.Bits & 0x40) == 0 && self.Grab == 0)
                        {
                            anim = 0;
                            self.State = 0;
                            var fallback = MechGround.Probe(world.Map, new Vec3(prevX, prevY + 0x0CCC, prevZ));
                            int floor = fallback.Height;
                            if (self.Y < floor || self.Y - floor < 0x333)
                            {
                                self.X = prevX; self.Y = prevY; self.Z = prevZ;
                                int masked = fallback.Attribute & unchecked((int)0xFFFFFF3F);
                                if (masked != 0) ApplySurfaceAttribute(self, world, masked);
                                self.MotionX = 0;
                                self.MotionZ = 0;
                                restored = true;
                            }
                        }
                        if (!restored) self.Bits |= 1;
                    }
                    if (snap) self.Y = lane;

                    if (self.State == 0x0E && (self.Bits & 2) == 0) self.Bits |= 1;
                    PushOffWalls(self, world, 0); // func_8005220C: no actor candidates with one player -> 0

                    if (anim != self.Clip || (self.Track.Index == 0 && anim != 0))
                    {
                        MechAnimation.Play(self.Track, anim, 1, 1);
                        self.Clip = (byte)anim;
                    }

                    int rate;
                    if (self.State is 0x0A or 0x0F or 0x0B or 0x0C or 0x12 or 9 or 0x11) rate = 0xA0;
                    else
                    {
                        rate = MechMath.Sqrt12(MechMath.SumSquares3(self.MotionX, self.MotionY, self.MotionZ)) << 2;
                        rate >>= 8;
                        if (self.Slow != 0) rate *= 4 - (sbyte)self.Slow;
                        int st = self.State;
                        if (st == 0x0E) { if (rate < 0x300) rate = 0x300; }
                        else if ((uint)(st - 1) < 4) { if (rate < 0x100) rate = 0x100; }
                        else if (rate < 0x1E) rate = 0x1E;
                    }

                    if (normal is Vec3 slopeNormal)
                    {
                        var planar = new[] { slopeNormal.X, slopeNormal.Z };
                        int slope = MechMath.Length2(planar);
                        self.Bits &= 0xBF;
                        if (0xA000 < slope) self.Bits |= 0x40;
                    }

                    if (action == 2 && (self.Bits & 1) == 0)
                    {
                        if ((self.Bits & 0x40) == 0)
                        {
                            int frame = self.Track.Frame;
                            int index = (uint)(frame - 2) >= 4 && (uint)(frame - 0x12) >= 0x0E ? 0x36 : 0x37;
                            MechAnimation.Play(self.Track, index, 1, 1);
                            self.State = 0x0D;
                            self.Bits &= 0xCF;
                        }
                        action = 0;
                    }

                    uint events = MechAnimation.Advance(self, world, rate, action);
                    if ((events & 1) != 0) self.Bits &= 0xEF;
                    else if (action != 0) self.Bits |= (byte)(8 << action);
                    for (int e = (int)(events >> 1); e != 0; e >>= 6) world.SoundEvent?.Invoke(e & 0x3F);

                    if ((self.Track.Clip.Flags & 4) != 0)
                    {
                        if (self.State == 0x0A) self.State = 0x0B;
                        else if (self.State == 0x0F) self.State = 0x10;
                    }
                    if (self.Track.Index == 0 && (events & 1) != 0)
                    {
                        self.State = 0;
                        world.SoundEvent?.Invoke(0x27);
                    }
                } while (--remaining > 0);
            }

            var velocity = new[] { self.MotionX, self.MotionY, self.MotionZ };
            self.VelocityX = velocity[0]; self.VelocityY = velocity[1]; self.VelocityZ = velocity[2];
            int length = MechMath.NormalizeGetLength(velocity);
            (self.VelocityX, self.VelocityY, self.VelocityZ) = (velocity[0], velocity[1], velocity[2]);
            self.Speed = length >> 11;
            if (self.Speed == 0) (self.VelocityX, self.VelocityY, self.VelocityZ) = (self.AxisX, self.AxisY, self.AxisZ);
            world.ModelFrame?.Invoke(self.Track.Frame);
            return impactTotal;
        }
    }

    private static int AbsRetail(int v) => unchecked(((v >> 31) + v) ^ (v >> 31));

    /// <summary>func_8006943C gClampToWorldBounds: keeps x/z 0x800 inside the map clip rectangle D_800A2950.</summary>
    public static int ClampToWorld(MechBody body, LevelMap map)
    {
        var b = map.Header.WorldClipRect;
        int changed = 0;
        int lower = b[0] + 0x800;
        if (body.Z < lower) { body.Z = lower; changed = 1; }
        else
        {
            lower = b[1] - 0x800;
            if (lower < body.Z) { body.Z = lower; changed = 1; }
        }
        lower = b[2] + 0x800;
        if (body.X < lower) { body.X = lower; changed |= 2; }
        else
        {
            lower = b[3] - 0x800;
            if (lower < body.X) { body.X = lower; changed |= 2; }
        }
        return changed;
    }

    /// <summary>func_80052E28: turns the heading by at most 8 toward the axis of the wall side last hit.</summary>
    public static void SnapToWall(MechBody body)
    {
        unchecked
        {
            int angle = (short)body.Angle;
            int target = 0;
            switch (body.Wall & 7)
            {
                case 1: target = angle < 0x400 ? 0 : angle >= 0xC01 ? 0x1000 : 0x800; break;
                case 2: target = angle < 0x800 ? 0x400 : 0xC00; break;
                case 3: target = angle > 0xA00 ? 0xE00 : angle < 0x200 ? -1 : 0x600; break;
                case 4: target = angle < 0x600 ? 0x200 : angle >= 0xE01 ? 0x1000 : 0xA00; break;
            }
            int delta = target - angle;
            if (delta > 0 && 8 < delta) delta = 8;
            if (delta < 0 && 8 < -delta) delta = -8;
            body.Angle = (ushort)(body.Angle + delta);
        }
    }

    /// <summary>func_80051AC8: the two s16 of the state + 0x2DC record (0x126 +- 0x19, crouched 0xCC +- 0x19).</summary>
    public static void SetBounds(MechBody body, bool compact)
    {
        int centre = compact ? 0xCC : 0x126;
        body.BoundsHigh = (short)(centre + 0x19);
        body.BoundsLow = (short)(centre - 0x19);
    }

    /// <summary>func_80052F54: damage of a landing with downward speed <paramref name="impact"/> (0 up to 0x93333).</summary>
    public static int FallDamage(MechBody body, MechWorld world, int impact)
    {
        unchecked
        {
            if (impact <= 0x93333) return 0;
            int val = impact > 0x300000 ? 0x300000 : impact;
            val -= 0x93333;
            val <<= 9;
            val /= 183501;
            val <<= 7;
            if (val <= 0x8000) val = MechMath.Fmul(val, 0x9CC4);
            else if (val <= 0xC000) val = MechMath.Fmul(MechMath.Fmul(val, MechMath.Sqrt12(val) << 2), 0xDDB3);
            else val = MechMath.Fmul(val, val);
            val = MechMath.Fmul(body.Health, val);
            val *= 100;
            switch (world.SessionMode[body.Port != 0 ? 1 : 0])
            {
                case 2: val /= 150; break;
                case 1: val /= 125; break;
                case 0: val /= 100; break;
                case 3: val = 0; break;
            }
            return val;
        }
    }
}
