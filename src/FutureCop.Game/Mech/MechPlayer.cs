namespace FutureCop.Game.Mech;

/// <summary>
/// The per-frame player-unit update func_80055988 gUpdatePlayerFormAndMotion (reached through the
/// scratchpad trampoline func_800586D4 from func_800588D4:259): position shift, the walker/hover
/// form change on L1 + R1, control masking, and the dispatch to func_800549F0 or func_80053A74.
/// Line numbers refer to src/game/80055988.c.
/// </summary>
public static class MechPlayer
{
    /// <summary>Translated pad bits (func_800576A8) of the form-change chord: L1 (0x2000000) + R1 (0x800000).</summary>
    public const uint FormChord = 0x2800000;

    /// <summary>Sound func_80055988 plays when the chord is refused (state +0x146 set).</summary>
    public const int RefusedSound = 0x2A;

    /// <summary>
    /// func_80055988(o, ticks, steps, &amp;out). <paramref name="padWord"/> is state + 0x30, the
    /// translated pad word; it is masked in place exactly as retail masks it, and gWeaponSlotInput
    /// (func_800588D4:290) reads it afterwards. <paramref name="formChanged"/> is bit 0 of the out
    /// word (func_800588D4:260-265 then spawns the effect and plays sound 0x15). Returns the landing
    /// damage the integrator left in $v0 (func_800586D4 hands it on as the return value).
    /// </summary>
    public static int Update(MechBody o, MechWorld world, int ticks, int steps, ref uint padWord, out bool formChanged)
    {
        unchecked
        {
            formChanged = false;
            // :163-170
            if (o.ShiftX != 0 || o.ShiftY != 0 || o.ShiftZ != 0)
            {
                o.X += o.ShiftX;
                o.Y += o.ShiftY;
                o.Z += o.ShiftZ;
                o.ShiftZ = o.ShiftY = o.ShiftX = 0;
            }
            // :172-175
            uint flags = padWord;
            if (o.Frozen != 0) flags = 0;
            // :177-189
            if ((flags & FormChord) == FormChord && o.Lock == 0 && world.Focus == null && o.Grab == 0)
            {
                if (o.Hazard != 0) world.Sound?.Invoke(RefusedSound);
                else o.Request = (byte)(1 - (byte)o.Form);
            }
            // :191-205
            if (o.Form != (sbyte)o.Request)
            {
                if (o.Progress == 0)
                {
                    o.Lock = 1;
                    MechAnimation.Play(o.CompanionTrack, 2, 1, 0);
                    MechAnimation.Play(o.Track, 0xC, 1, 0);
                    o.Form = 1;
                    o.Bank = 0;
                }
                else
                {
                    o.Lock = -1;
                    MechAnimation.Play(o.CompanionTrack, 2, -1, 0);
                    MechAnimation.Play(o.Track, 0xC, -1, 0);
                }
                o.Request = (byte)o.Form;
                formChanged = true;
            }
            // :207-251
            if (o.Lock != 0)
            {
                if (o.Lock < 0)
                {
                    o.Progress -= ticks * 0xA0;
                    if (o.Progress < 0)
                    {
                        o.Progress = 0;
                        o.Lock = 0;
                        MechAnimation.Play(o.CompanionTrack, 0, 1, 0);
                        o.Lift = 0;
                        o.Throttle = 0;
                        MechAnimation.Play(o.Track, 0, 1, 0);
                        o.Form = 0;
                        o.Request = 0;
                        world.SetLoopingSound(o.Port, 0xFF);
                    }
                }
                else
                {
                    o.Progress += ticks * 0xA0;
                    if (o.Progress > 0x10000)
                    {
                        o.Progress = 0x10000;
                        o.Lock = 0;
                        MechAnimation.Play(o.CompanionTrack, 1, 1, 0);
                        MechAnimation.Play(o.Track, 0xD, 1, 0);
                        o.Lift = 0;
                        o.Throttle = 0;
                    }
                }
                MorphBounds(o, o.Progress);
                int height = (o.Progress << 12) >> 16;
                o.Y += height - o.LiftHeight;
                o.LiftHeight = (short)height;
                if (o.Lock != 0)
                {
                    MechAnimation.Advance(o, o.CompanionTrack, world, o.Progress, 0);
                    MechAnimation.Advance(o, o.Track, world, o.Progress, 0);
                    world.Rumble?.Invoke(1);
                }
                else world.Rumble?.Invoke(0);
                world.CompanionFrame?.Invoke(o.CompanionTrack.Frame);
                o.InstanceFrame = o.Track.Frame;
                world.ModelFrame?.Invoke(o.Track.Frame);
                flags &= 0xC0000;
                padWord &= 0x54C0000;
            }
            // :253-264
            bool mask = true;
            if (world.Focus == null)
            {
                if (o.ControlBlock <= 0 && o.Grab == 0) mask = false;
            }
            else if ((padWord & 0x80000) != 0)
            {
                world.RetireEntity?.Invoke(world.Focus);
                o.Status320 = 3;
            }
            if (mask)
            {
                flags &= 0xC0000;
                padWord &= 0xC0000;
            }
            // :266-269 L2 + R2 together cancel both strafe bits.
            if ((flags & 0x300000) == 0x300000) flags &= 0xFFCFFFFF;
            // :271-275
            return o.Form != 1
                ? MechWalker.Tick(o, world, steps, flags)
                : MechHover.Tick(o, world, ticks, steps, flags);
        }
    }

    /// <summary>
    /// The footprint half of func_800518A8 gUpdateMorphBounds(obj + 0x28, state + 0xEC, t, obj):
    /// the four corners at height 174 - 154t/65536 grown from the 3DBB record of the body instance's
    /// frame (lines 100-128), their world transform into +0x10C (lines 129-130, the obj + 0x2C matrix
    /// whose translation is the position), and the two s16 of the state + 0x2DC record
    /// (lines 132-151; <see cref="MechWalker.SetBounds"/> at t = 0). The radius words (+0x29C,
    /// +0x2A0, +0x2A2, record +0x0C/+0x0E) feed the actor contact tests of func_8005220C, which
    /// do nothing with one player, and are not kept.
    /// </summary>
    public static void MorphBounds(MechBody o, int t)
    {
        unchecked
        {
            var bounds = o.FrameBounds
                ?? throw new InvalidOperationException("func_800518A8 needs the 3DBB records of the body model");
            if ((uint)o.InstanceFrame >= (uint)bounds.Count)
                throw new InvalidDataException($"no 3DBB record for body frame {o.InstanceFrame}");
            var e = bounds[o.InstanceFrame];
            int p2 = e[5];
            int p1 = p2 * t;
            p2 = p2 * ((t >> 4) + 0x1000);
            int f04 = e[2], f06 = e[3], f00 = e[0];
            short c1 = (short)(174 + ((-154 * t) >> 16));
            f04 += p1 >> 16;
            p2 >>= 12;
            f06 += (f06 * (t >> 2)) >> 16;
            short[] fp = o.Footprint;
            fp[0] = (short)(f00 - f06); fp[1] = c1; fp[2] = (short)(f04 + p2);
            fp[3] = (short)(f00 + f06); fp[4] = c1; fp[5] = (short)(f04 + p2);
            fp[6] = (short)(f00 + f06); fp[7] = c1; fp[8] = (short)(f04 - p2);
            fp[9] = (short)(f00 - f06); fp[10] = c1; fp[11] = (short)(f04 - p2);
            for (int i = 0; i < 4; i++)
            {
                var w = MechWalker.TransformCorner(o, fp[i * 3], fp[i * 3 + 1], fp[i * 3 + 2]);
                o.FootprintWorld[i * 3] = w.X;
                o.FootprintWorld[i * 3 + 1] = w.Y;
                o.FootprintWorld[i * 3 + 2] = w.Z;
            }
            int cenx = 294 + ((-358 * t) >> 17);
            int rad = (51 * (0x10000 - t)) >> 17;
            o.BoundsHigh = (short)(cenx + rad);
            o.BoundsLow = (short)(cenx - rad);
        }
    }
}
