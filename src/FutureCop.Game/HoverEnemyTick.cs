using FutureCop.Game.Mech;

namespace FutureCop.Game;

/// <summary>
/// func_800324E0 gUpdateHoverEnemy, the message-6 tick of a hovering enemy
/// (src/game/800324E0.c:152-386). The anim gate, retarget block, dive/descent
/// states and the L_AD4 steering spine are ported 1:1. The GTE tail is reduced
/// to the stored basis rows, since the .NET view renders its own models.
/// </summary>
public static class HoverEnemyTick
{
    /// <summary>AB8 macro (800324E0.c:58): abs(v &gt;&gt; 8) with the sign-mask idiom.</summary>
    private static int Ab8(int v)
    {
        int t = v >> 8;
        int g = t >> 31;
        return unchecked((g + t) ^ g);
    }

    private static short S16(int v) => unchecked((short)v);

    /// <summary>(s16) read of a u16 field.</summary>
    private static int S16r(int v) => unchecked((int)(short)v);

    /// <summary>
    /// Message 4 of gHandleTumblingFlyer (gHandleTumblingFlyer.c:74-99, func_80032F3C): a flyer that
    /// is neither falling (state 3) nor already down (state 5) breaks off into the tumble - state 3,
    /// the flight vector scaled by +0xF4 as the dive direction and three random spin rates in
    /// -0x800..0x7FF. A type-0x25 enemy always breaks off; other types pass the 10/16 random gate
    /// (:84). Returns true when the message was consumed, false when it belongs to the base handler,
    /// which then retires the entity (:101).
    /// </summary>
    public static bool EnterDeathTumble(HoverEnemy o, Func<int> nextRandom)
    {
        ArgumentNullException.ThrowIfNull(nextRandom);
        unchecked
        {
            if (o.FA == 5) return false;                                          // :76-78
            if (o.FA == 3) return true;                                           // :79-82
            if (o.Type != 0x25 && (nextRandom() & 0xF) >= 0xA) return false;      // :84
            o.FA = 3;                                                            // :85
            for (int i = 0; i < 3; i++)
                o.DC[i] = (int)(((long)o.Dir[i] * o.F4) >> 16);                   // :86-87
            o.E8[0] = (nextRandom() & 0xFFF) - 0x800;                             // :88
            o.E8[1] = (nextRandom() & 0xFFF) - 0x800;                             // :89
            o.E8[2] = (nextRandom() & 0xFFF) - 0x800;                             // :90
            return true;
        }
    }

    /// <summary>Ticks one hover enemy; returns 0 like the retail function.</summary>
    public static int Tick(HoverEnemy o, int dt, IHoverEnemyWorld w)
    {
        unchecked
        {
            // Anim gate (:162-165): a live animation track advances frame +0x12C.
            if (o.AnimCounter != 0)
                w.AdvanceAnimation(o, dt);

            if (o.Type != 0x25)
                TickTargetBlock(o, dt, w);

            switch (o.FA)
            {
                case 4:
                    TickDescent(o, dt, w);
                    return 0;
                case 3:
                    TickDive(o, dt, w);
                    return 0;
                case 5:
                    // Dead (:292-295): only non-0x25 clears +0x1C.
                    if (o.Type != 0x25)
                        o.F1C = -1;
                    return 0;
                case 0:
                case 1:
                    break;
                default:
                    return 0;
            }

            // States 0/1 share the L_AD4 steering block.
            int[] b = new int[3];
            if (!ChooseChaseTarget(o, b, w))
                return 0;
            int gL = Steer(o, b, dt, w);
            SteerTail(o, dt, gL, w);
            return 0;
        }
    }

    /// <summary>
    /// Retarget + pursue/wander toggle for non-0x25 types (:167-197). A failed
    /// search jumps past the id-drop (C goto L5E4) but keeps the toggle.
    /// </summary>
    private static void TickTargetBlock(HoverEnemy o, int dt, IHoverEnemyWorld w)
    {
        unchecked
        {
            bool failed = false;
            o.F102 = (ushort)(o.F102 - dt);
            if (S16r(o.F102) < 0)
            {
                o.F102 = (ushort)((w.NextRandom() & 0x1FF) + 0x384);
                HoverEnemyHit? hit = w.FindTargetEntity(o);
                if (hit is null)
                    failed = true;
                else
                {
                    o.C8 = hit;
                    o.CC = hit.Id;
                }
            }
            if (!failed && o.C8 is not null && o.CC != o.C8.Id)
            {
                PickWanderTarget(o, w);
                o.C8 = null;
            }
            if (o.FA < 2)
            {
                o.F100 = (ushort)(o.F100 - dt);
                if (S16r(o.F100) < 0)
                {
                    if (o.FA == 0)
                    {
                        PickWanderTarget(o, w);
                        o.FA = 1;
                        o.F100 = (ushort)(o.FE + (uint)(S16(o.FE) * (w.NextRandom() & 0xFF)) / 1024u);
                    }
                    else
                    {
                        o.FA = 0;
                        o.F100 = (ushort)(o.FC + (uint)(S16(o.FC) * (w.NextRandom() & 0xFF)) / 1024u);
                    }
                }
            }
        }
    }

    /// <summary>Descent state 4 (:200-210): falls until back inside the band.</summary>
    private static void TickDescent(HoverEnemy o, int dt, IHoverEnemyWorld w)
    {
        unchecked
        {
            o.Pos[1] += (dt * o.F4) >> 9;
            int gr = w.SampleHeightBilinear(o.Pos) + (o.F6C << 4);
            if (o.Pos[1] - gr > (o.F114 << 4))
                o.FA = 0;
            // Fallback direction is straight up (:206-209); the basis build call in
            // the C uses it with the (q, a) pair after L_ED0.
            int[] up = { 0, 0x10000, 0 };
            Copy(o.Dir, o.BasisRows[2]);
            GteCrossPair(up, o.Dir, o.BasisRows);   // func_80019310 on (dir, up)
        }
    }

    /// <summary>
    /// Dive state 3 (:242-291): falls with jitter; on ground impact a type-0x25
    /// enemy registers its hit record, credits a player kill and runs the death
    /// script before entering the dead state 5.
    /// </summary>
    private static void TickDive(HoverEnemy o, int dt, IHoverEnemyWorld w)
    {
        unchecked
        {
            int ground = w.GetGroundHeight(o.Pos);
            if (!(ground + (o.F68 >> 1) < o.Pos[1]))
            {
                if (o.Type == 0x25)
                {
                    // Impact (:251-274).
                    int k = w.SessionWord13();
                    o.F1C = S16(o.F1E);
                    o.Flags24 |= 0x35u;
                    o.F100 = (ushort)((10 - k) * 150 + 600);
                    if (o.F20 != 0 && (o.Flags24 & 0x40000) == 0)
                        w.FlagQuartileChange(o);
                    o.F1C = -1;
                    w.SpawnEntitySlotTypes(o.F20, o, 0, (o.F19 >> 3) & 0x1E, 2);
                    if (o.RecF48 == 0)
                        o.RecF48 = w.FrameTime();
                    o.RecF3E++;
                    if (o.F8C != 0 && w.IsPlayerBlockClass(o.F8C & 0x3FFFFFFF))
                        w.CreditPlayerKill();
                    if (o.F98 != 0)
                        w.RunDeathScript(o);
                }
                o.FA = 5;
                return;
            }
            // Airborne tumble (:278-290).
            AddScaled(o.E8, o.DC, -0x400, o.E8);
            o.E8[1] -= ((w.NextRandom() & 0xF) - 3) << 9;
            int c3 = dt << 8;
            AddScaled(o.DC, o.E8, c3, o.DC);
            AddScaled(o.Pos, o.DC, c3, o.Pos);
            Copy(o.DC, o.Dir);
            Normalize(o.Dir);
            int[] up = Copy3(o.Up38);
            up[1] += 0x2000;
            GteCrossPair(up, o.Dir, o.BasisRows);       // func_80019310 on (dir, up)
        }
    }

    private static bool ChooseChaseTarget(HoverEnemy o, int[] b, IHoverEnemyWorld w)
    {
        unchecked
        {
            if (o.FA == 1)
            {
                Copy(o.DC, b);
                return true;
            }

            // State 0 (:214-237).
            if (o.Type != 0x25)
            {
                int cA = Ab8(o.Pos[0] - o.D0X);
                int dA = Ab8(o.Pos[2] - o.D8Z);
                if (o.F10E < cA || o.F10C < dA)
                {
                    PickWanderTarget(o, w);
                    Copy(o.DC, b);
                    o.FA = 1;
                    o.F100 = (ushort)((w.NextRandom() & 0x7FF) + 0x4B0);
                    return true;
                }
            }
            if (o.C8 is not null)
            {
                Copy(o.C8.Pos, b);
                return true;
            }
            Copy(o.DC, b);
            if (o.Type != 0x25)
            {
                // f100 = 0x4B0 - max(0, 0x4B0 - s16(fFE)) (:236, sign-mask form).
                int diff = 0x4B0 - S16r(o.FE);
                int mask = (diff >> 31) & diff;
                o.F100 = (ushort)(0x4B0 - mask);
                o.FA = 1;
            }
            return true;
        }
    }

    /// <summary>
    /// The L_AD4 steering spine (:300-356): chases b, honours the turn-away
    /// threshold, applies the altitude band, eases into the accumulator and
    /// re-normalises the direction. Returns <c>gL</c> sampled at :325 for the :368 clamp.
    /// </summary>
    private static int Steer(HoverEnemy o, int[] b, int dt, IHoverEnemyWorld w)
    {
        unchecked
        {
            int[] a = new int[3];
            a[0] = b[0] - o.Pos[0];
            a[1] = b[1] - o.Pos[1];
            a[2] = b[2] - o.Pos[2];
            int ang = ApproxDistanceXz(b, o.Pos);
            if (o.Type != 0x25 && o.F116 > 0)
                o.F116 = S16(o.F116 - dt);
            if ((ang >> 4) < o.F10A || o.F116 > 0)
            {
                a[0] = -a[0]; a[1] = -a[1]; a[2] = -a[2];
                if (o.Type != 0x25 && o.F116 <= 0)
                    o.F116 = S16((w.NextRandom() & 0x7F) + 0x96);
            }
            a[1] = 0;
            EnemyMath.ScaleVectorToBit15(a);

            int dL = (dt * o.F8) >> 3;
            int gL = w.SampleHeightBilinear(o.Pos) + (o.F6C << 4);
            EnemyMath.ScaleInPlace(dL, a);
            int cmp = gL + (o.F114 << 4);
            int step;
            if (o.Pos[1] < cmp)
            {
                if (gL + (o.F110 << 4) >= o.Pos[1])
                    step = 0x1800;
                else
                    step = (Mid32((long)(cmp - o.Pos[1]) * o.F11C) * 0x1800) >> 16;
            }
            else
            {
                if (o.Pos[1] >= gL + (o.F112 << 4))
                    step = -0x1800;
                else
                    step = (Mid32((long)(cmp - o.Pos[1]) * o.F118) * 0x1800) >> 16;
            }
            int stepAbs = ((step >> 31) + step) ^ (step >> 31);
            a[1] = stepAbs < 0x201 ? 0 : step;

            EaseQuarterInto(a, o.E8);
            o.E8[1] = o.E8[1] - o.E8[1] / 4;
            o.Dir[0] += a[0];
            o.Dir[1] += a[1];
            o.Dir[2] += a[2];
            o.Dir[1] = o.Dir[1] - o.Dir[1] / 8;
            EnemyMath.ScaleVectorToBit15(o.Dir);
            int dy = o.Dir[1];
            if (dy < -0x8000) o.Dir[1] = -0x8000;
            else if (dy > 0x8000) o.Dir[1] = 0x8000;
            Normalize(o.Dir);
            return gL;
        }
    }

    /// <summary>
    /// L_AD4 tail (:358-377): banking-scaled move, attack-scheduler step and the
    /// basis rebuild from the eased velocity.
    /// </summary>
    private static void SteerTail(HoverEnemy o, int dt, int gL, IHoverEnemyWorld w)
    {
        unchecked
        {
            int spd = (dt * o.F4) >> 8;
            int upY = ((o.Up38[1] >> 31) + o.Up38[1]) ^ (o.Up38[1] >> 31);
            int cmpBank = (0x10000 - upY) * 2;
            spd = (spd * (0x200 - ((cmpBank - cmpBank / 16) >> 8))) >> 9;
            o.Pos[0] += Fmul(spd, o.Dir[0]);
            o.Pos[1] += Fmul(spd, o.Dir[1]);
            o.Pos[2] += Fmul(spd, o.Dir[2]);
            if (o.Pos[1] < gL)
                o.Pos[1] = gL;
            if (o.Type == 0x25 || o.FA == 0)
            {
                if (o.FA0 != 0)
                    w.StepAttackScheduler(o, dt);
            }
            int[] a = Copy3(o.E8);
            a[1] += 0x2000;
            // gBuildBasisZFromPair(q=f78 dir, a) (:382): row0 = a x q, row2 = q.
            GteCrossPair(a, o.Dir, o.BasisRows);
        }
    }

    // ---------------- helpers ----------------

    /// <summary>func_80019310 gBuildBasisZFromPair (80019310.c:44-49): row0 = second x first normalised, row1 = first x row0, row2 = first.</summary>
    private static void GteCrossPair(int[] second, int[] first, int[][] basis)
    {
        MechMath.GteCross(second, first, basis[0]);
        Normalize(basis[0]);
        MechMath.GteCross(first, basis[0], basis[1]);
        Copy(first, basis[2]);
    }

    /// <summary>func_800320E4 gPickHoverWanderTarget (800320E4.c:45-53), spread at +0x10C z, +0x10E x.</summary>
    private static void PickWanderTarget(HoverEnemy o, IHoverEnemyWorld w)
    {
        if (o.FA != 3 && o.Type != 0x25)
        {
            int rx = (w.NextRandom() & 0x7FF) - 0x400;
            int rz = (w.NextRandom() & 0x7FF) - 0x400;
            o.DC[1] = o.D4Y;
            o.DC[0] = unchecked(o.D0X + ((rx * o.F10E) >> 3));
            o.DC[2] = unchecked(o.D8Z + ((rz * o.F10C) >> 3));
        }
    }

    /// <summary>func_8001ACA4 gVectorAddScaled: out = a + middle32(b * scale).</summary>
    private static void AddScaled(int[] a, int[] b, int scale, int[] result) =>
        EnemyMath.AddScaled(a, b, scale, result);

    /// <summary>func_8001A6F8 gEaseVectorQuarter: dst = (3*dst + src) &gt;&gt; 2 per component.</summary>
    private static void EaseQuarterInto(int[] src, int[] dst) =>
        MechMath.EaseVectorQuarter(src, dst);

    /// <summary>func_8001A750 gApproximateDistanceXzHalfMin on the x/z components.</summary>
    private static int ApproxDistanceXz(int[] a, int[] b) =>
        EnemyMath.ApproxDistanceXz(a, b);

    /// <summary>func_8001A180 gNormalizeVector via MechMath.Normalize (16.16).</summary>
    private static void Normalize(int[] v) => MechMath.Normalize(v);

    /// <summary>Fmul macro (800324E0.c:54): bits 16..47 of the signed product.</summary>
    private static int Fmul(int a, int b) => MechMath.Fmul(a, b);

    /// <summary>bits 16..47 of the raw 64-bit product (C Prod union read, :332-333).</summary>
    private static int Mid32(long product) => unchecked((int)(ulong)((ulong)product >> 16));

    private static int[] Copy3(int[] src) => new[] { src[0], src[1], src[2] };

    private static void Copy(int[] src, int[] dst)
    {
        dst[0] = src[0]; dst[1] = src[1]; dst[2] = src[2];
    }
}
