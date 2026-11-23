namespace FutureCop.Game;

/// <summary>
/// Port of func_800336DC gUpdateDroneStrategy - the build-order AI of a Precinct
/// Assault side (src/game/800336DC.c:394-847). Scans the unit list, dists target
/// candidates by octagonal distance, switches the three strategy modes and spends
/// the shared resource pool on production, expansion and slot claims.
/// </summary>
public static class DroneStrategy
{
    /// <summary>Global block D_8009FA94 (800336DC.c:62-82).</summary>
    public sealed class Glob
    {
        public int S4C;                       // +0x4C strategy level 0..9
        public int F68;                        // +0x68 credit balance
        public int F74;                        // +0x74 per-turn limit vs 0x14
        public int F78;                        // +0x78 per-turn limit vs 0x14
        public int F84;                        // +0x84 counter (== 4 check)
        public int F88;                        // +0x88 flag (== 0 checks)
        public int F9C;                        // +0x9C mode mirror
        public int[] A0 = new int[4];         // +0xA0 slot ownership
        public int FB0;                        // +0xB0 upgrade counter
        public int FB4;                        // +0xB4 counter
        public int FB8;                        // +0xB8 counter
        public int FBC;                        // +0xBC counter
        public int[][] C =                     // +0xC0 production counters
        {
            new int[2], new int[2], new int[2], new int[2],
        };
        public int FE0;                        // +0xE0 event word (2 or 3)
    }

    /// <summary>Ai record at unit +0x134 (800336DC.c:12-24).</summary>
    public sealed class Ai
    {
        public int[] PosA = new int[3];        // +0x00 base A
        public int[] PosB = new int[3];        // +0x0C base B
        public int Mode;                       // +0x1A strategy mode 0..2
        public int B1B;                        // +0x1B the ruling team
        public int C1C;                        // +0x1C claim-order selector
        public int U30;                        // +0x30 (u16) pressure low
        public int U32;                        // +0x32 (u16) pressure high
        public int S4C;                        // +0x4C scratch mirror
    }

    /// <summary>One unit of the world list (Unit fields the strategy touches).</summary>
    public sealed class StrategyUnit
    {
        public bool Active;                    // +0x08
        public int Type;                       // +0x16
        public int Team;                       // +0x18
        public int Sub;                        // +0x19
        public short S1C;                      // +0x1C
        public int U1E;                        // +0x1E (u16)
        public int[] Pos = new int[3];        // +0x50
        public int Aq6;                        // +0xA6
        public short Aa;                       // +0xAA engagement radius
        public short AuxA324;                  // aux +0x324
        public short AuxA326;                  // aux +0x326
    }

    /// <summary>The drone the strategy runs on (self fields).</summary>
    public sealed class Self
    {
        public int Team;                       // +0x18
        public int[] Pos = new int[3];        // +0x50
        public StrategyUnit? B8Ref;            // +0xB4 (never written here)
        public StrategyUnit? C8Ref;             // +0xC8 current target
        public int Fa;                          // +0xFA idle flag
        public int Aq6;                        // +0xA6
        public short Aa;                       // +0xAA engagement radius
        public Ai Strats = new Ai();
    }

    /// <summary>World services the strategy needs.</summary>
    public interface IWorld
    {
        System.Collections.Generic.IEnumerable<StrategyUnit> Units { get; } // D_8009FA40 list
        Glob G { get; }                                    // D_8009FA94
        StrategyUnit? PlayerEntities { get; }              // D_8009FA58
        int NextRandom();                                  // func_80019668
        int Sqrt12(int v);                                 // func_80095900
        void SetDroneAttackTarget(Self self, StrategyUnit? unit);  // func_80033608
        void SetDronePursuitTarget(Self self, StrategyUnit unit);  // func_800336A8
        void UpdateStrategicPosition(Self self);           // func_800330E8
        byte[] SlotClaimOrder { get; }                     // D_8009AC50 = 3,2,1,0
        byte[] AlternateSlotClaimOrder { get; }            // D_8009AC54 = 1,0,3,2
        int FrameTime();                                   // D_8009FAA4
        void SetSinglePlayerAiS4C(int value);              // D_8009F9EC->ai->s4C
    }

    // ---------------- C macros (800336DC.c:101-191) ----------------

    /// <summary>ABS macro (:101-105).</summary>
    private static int Abs(int v)
    {
        int g = v >> 31;
        return (g + v) ^ g;
    }

    /// <summary>DIST macro (:107-122): |dx| + |dz| - floor(min/2).</summary>
    private static int Dist(int dx, int dz)
    {
        int d = Abs(dx);
        int az = Abs(dz);
        int m = d;
        if (az < d) m = az;
        return d + az - ((m + ((int)((uint)m >> 31))) >> 1);
    }

    /// <summary>DISTA macro (:154-168): |dz| - floor(min/2), half-metric tail.</summary>
    private static int Dista(int dx, int dz)
    {
        int d = Abs(dx);
        int az = Abs(dz);
        int m = d;
        if (az < d) m = az;
        int tt = m + ((int)((uint)m >> 31));
        return az - (tt >> 1);
    }

    /// <summary>APPROACH macro (:170-191 + inline copies :598-618, :664-680).</summary>
    private static void Approach(Self self, StrategyUnit q, IWorld w)
    {
        if (!ReferenceEquals(self.C8Ref, q) || !ReferenceEquals(self.B8Ref, q))
        {
            if (!ReferenceEquals(self.B8Ref, q))
            {
                int ex = q.Pos[0] - self.Pos[0];
                int ez = q.Pos[2] - self.Pos[2];
                // FMUL keeps bits 16..47 of each squared delta.
                int m1 = Fmul(ex, ex);
                int m2 = Fmul(ez, ez);
                if ((w.Sqrt12(m1 + m2) << 2) < (self.Aa << 4))
                    w.SetDroneAttackTarget(self, q);
            }
            if (!ReferenceEquals(self.C8Ref, q))
                w.SetDronePursuitTarget(self, q);
        }
    }

    /// <summary>FMUL (:4): bits 16..47 of the signed product.</summary>
    private static int Fmul(int a, int b) => unchecked((int)(((long)a * b) >> 16));

    /// <summary>Scan-pass candidate records (:396-446 locals).</summary>
    private sealed class Scan
    {
        public StrategyUnit? C5, C1C, C9B, C9A, C9BB, C9AB, C1, N50, N4C, N58, N54, N5C, N64, N60;
        public int D14, D18, D1C, D20, D24, D28, D2C, D30, D34, D38, D3C, D40, D44, D48;
        public int CntA, Cnt2;
    }

    /// <summary>Scan pass (:448-546): collects the nearest candidate per category.</summary>
    private static Scan ScanUnits(Self self, IWorld w)
    {
        var s = new Scan();
        int bb = (9 - w.G.S4C) << 12;
        // C local dist is loop-carried: types 1/6/0x24 dist against the value
        // left by the last unit that hit either DIST (posB or posA) - stale-slot
        // semantics of the retail stack slot, reproduced as written.
        int dist = 0;
        foreach (var p in w.Units)
        {
            if (!p.Active) continue;
            int t = p.Type;
            if (t != 1 && t != 5 && t != 6 && t != 8 && t != 9 && t != 0x1C && t != 0x24)
                continue;

            bool interesting = p.Team == self.Strats.B1B
                || (t == 0x24 && p.Team != self.Team)
                || t == 8;

            if (interesting)
            {
                if (t == 0x1C || t == 5 || t == 9 || t == 8)
                {
                    // posB proximity candidates (:461-485); writes the shared dist.
                    dist = Dist(p.Pos[0] - self.Strats.PosB[0], p.Pos[2] - self.Strats.PosB[2]);
                    if (t == 0x1C && dist <= 0x90000)
                    {
                        if (s.C1C is null || dist < s.D18) { s.C1C = p; s.D18 = dist; }
                    }
                    else if (t == 5 && dist <= 0x80000)
                    {
                        if (s.C5 is null || dist < s.D14) { s.C5 = p; s.D14 = dist; }
                    }
                    else if (t == 9)
                    {
                        if (p.Sub >= 10)
                        {
                            if (s.C9B is null || dist < s.D30) { s.C9B = p; s.D30 = dist; }
                        }
                        else
                        {
                            if (s.C9A is null || dist < s.D2C) { s.C9A = p; s.D2C = dist; }
                        }
                    }
                    else if (t == 8 && p.Team == self.Team && dist <= 0x1FFFFF)
                        s.CntA++;
                }

                // Global scoring pass (:486-519): the running dist keeps its last
                // DIST value (stale slot if the type wrote no DIST) and accumulates
                // tv + bb per interesting unit - the retail stack slot semantics.
                int tv = Dista(p.Pos[0] - self.Pos[0], p.Pos[2] - self.Pos[2]);
                dist += tv;
                dist += bb;
                switch (t)
                {
                    case 1:
                        if (s.C1 is null || dist < s.D48)
                        {
                            if (p.AuxA324 <= 0 && p.AuxA326 <= 0) { s.C1 = p; s.D48 = dist; }
                        }
                        break;
                    case 0x1C:
                        if (s.N54 is null || dist < s.D20) { s.N54 = p; s.D20 = dist; }
                        break;
                    case 5:
                        if (s.N4C is null || dist < s.D1C) { s.N4C = p; s.D1C = dist; }
                        break;
                    case 9:
                        if (p.Sub >= 10)
                        {
                            if (s.C9BB is null || dist < s.D38) { s.C9BB = p; s.D38 = dist; }
                        }
                        else
                        {
                            if (s.C9AB is null || dist < s.D34) { s.C9AB = p; s.D34 = dist; }
                        }
                        break;
                    default:
                        if (t == 0x24)
                        {
                            if (p.Team == self.Strats.B1B)
                            {
                                if (s.N60 is null || dist < s.D40) { s.N60 = p; s.D40 = dist; }
                            }
                            else if (p.Team != self.Team)
                            {
                                s.D44 = unchecked(s.D44 + (w.NextRandom() & 0x3FFF));
                                if (s.N64 is null || dist < s.D44) { s.N64 = p; s.D44 = dist; }
                            }
                        }
                        break;
                }
            }
            else
            {
                if (p.Team == self.Team)
                {
                    // posA proximity accumulation (:521-541); also writes dist.
                    dist = Dist(p.Pos[0] - self.Strats.PosA[0], p.Pos[2] - self.Strats.PosA[2]);
                    if (t == 5 && dist <= 0x68000)
                    {
                        // C increments cnt2 only when the candidate is replaced (:526).
                        if (s.N50 is null || dist < s.D24) { s.Cnt2++; s.N50 = p; s.D24 = dist; }
                    }
                    else if (t == 0x1C && dist <= 0x78000)
                    {
                        // C adds 10 only when the candidate is replaced (:531).
                        if (s.N58 is null || dist < s.D28) { s.Cnt2 += 10; s.N58 = p; s.D28 = dist; }
                    }
                    else if (t == 9 && p.Sub >= 10)
                    {
                        if (s.N5C is null || dist < s.D3C) { s.N5C = p; s.D3C = dist; }
                    }
                }
            }
        }
        return s;
    }

    /// <summary>Strategy tick (:548-846): mode selection, approach chains, economy.</summary>
    public static void Tick(Self self, IWorld w)
    {
        unchecked
        {
            var G = w.G;
            var ai = self.Strats;
            var s = ScanUnits(self, w);

            int mode = ai.Mode;
            int c10 = 0;
            if (mode != 2)
            {
                if (s.C1C is not null || s.C5 is not null || (s.CntA < 10 && mode != 1))
                {
                    ai.Mode = 2;
                    G.F9C = 2;
                }
                if (ai.Mode != 2) goto L402C;
            }
            if (s.C1C is not null)
            {
                if (s.C5 is not null && s.D14 < s.D18)
                {
                    Approach(self, s.C5, w);
                }
                else
                {
                    Approach(self, s.C1C, w);
                }
            }
            else if (s.C5 is not null)
            {
                Approach(self, s.C5, w);
            }
            else if (s.CntA < 10)
            {
                if (w.PlayerEntities is not null && !ReferenceEquals(self.C8Ref, w.PlayerEntities))
                    w.SetDronePursuitTarget(self, w.PlayerEntities);
            }
            else
            {
                ai.Mode = 0;
                G.F9C = 0;
            }
            if (ai.Mode == 2) goto L4170;
        L402C:
            if (ai.Mode != 1)
            {
                if (s.Cnt2 >= 2)
                {
                    ai.Mode = 1;
                    G.F9C = 1;
                }
                if (ai.Mode != 1) goto L4170;
            }
            if (s.N58 is not null && s.D28 <= 0x77FFF)
            {
                if (!ReferenceEquals(self.C8Ref, s.N58))
                {
                    if (self.B8Ref is null && s.N64 is not null && s.D44 <= 0xFFFF)
                    {
                        if (!ReferenceEquals(self.C8Ref, s.N64))
                            w.SetDronePursuitTarget(self, s.N64);
                    }
                    else
                    {
                        w.SetDronePursuitTarget(self, s.N58);
                    }
                }
            }
            else if (s.N50 is not null && s.D24 <= 0x67FFF)
            {
                if (!ReferenceEquals(self.C8Ref, s.N50))
                {
                    w.SetDroneAttackTarget(self, null);
                    w.SetDronePursuitTarget(self, s.N50);
                }
            }
            else
            {
                ai.Mode = 0;
                G.F9C = 0;
            }
        L4170:
            RunMode0Targets(self, s, w);
            if (mode != ai.Mode) w.UpdateStrategicPosition(self);
            if (self.B8Ref is null && self.Aq6 != 4) w.SetDroneAttackTarget(self, null);

            if (ai.Mode == 2)
                RunMode2Economy(self, s, w);
            else
                c10 = RunSlotClaims(self, w);
            if (ai.Mode == 1) RunMode1Economy(self, w);
            if (ai.Mode == 0) RunMode0Economy(self, s, c10, w);
        }
    }

    /// <summary>Mode-0 target chain (:595-681).</summary>
    private static void RunMode0Targets(Self self, Scan s, IWorld w)
    {
        var G = w.G;
        if (self.Strats.Mode != 0) return;
        if (s.C1 is not null && s.D48 <= 0x4FFFF && s.C1.S1C < (short)(s.C1.U1E) >> 2)
        {
            if (!ReferenceEquals(self.C8Ref, s.C1) || !ReferenceEquals(self.B8Ref, s.C1))
            {
                if (!ReferenceEquals(self.B8Ref, s.C1))
                {
                    int ex = s.C1.Pos[0] - self.Pos[0];
                    int ez = s.C1.Pos[2] - self.Pos[2];
                    if ((w.Sqrt12(Fmul(ex, ex) + Fmul(ez, ez)) << 2) < (self.Aa << 4))
                        w.SetDroneAttackTarget(self, s.C1);
                }
                if (!ReferenceEquals(self.C8Ref, s.C1))
                {
                    w.SetDronePursuitTarget(self, s.C1);
                    G.FE0 = 3;
                }
            }
        }
        else if (s.N54 is not null && s.D20 <= 0x4FFFF)
        {
            Approach(self, s.N54, w);
        }
        else if (s.N4C is not null && s.D1C <= 0x3FFFF)
        {
            Approach(self, s.N4C, w);
        }
        else if (s.N64 is not null && s.D44 <= 0x5FFFF)
        {
            if (!ReferenceEquals(self.C8Ref, s.N64)) w.SetDronePursuitTarget(self, s.N64);
        }
        else if (s.C9AB is not null && s.D34 <= 0x2FFFF)
        {
            Approach(self, s.C9AB, w);
        }
        else if (s.N60 is not null && s.D40 <= 0x4FFFF)
        {
            if (!ReferenceEquals(self.C8Ref, s.N60)) w.SetDronePursuitTarget(self, s.N60);
        }
        else if (s.C9BB is not null && s.D38 <= 0x2FFFF)
        {
            // The retail variant here compares d38 < (aa << 4) instead of the
            // shifted root (:645-648) - reproduced as written.
            if (!ReferenceEquals(self.C8Ref, s.C9BB) || !ReferenceEquals(self.B8Ref, s.C9BB))
            {
                int ex = s.C9BB.Pos[0] - self.Pos[0];
                int ez = s.C9BB.Pos[2] - self.Pos[2];
                w.Sqrt12(Fmul(ex, ex) + Fmul(ez, ez));
                if (s.D38 < (self.Aa << 4))
                    w.SetDroneAttackTarget(self, s.C9BB);
                if (!ReferenceEquals(self.C8Ref, s.C9BB))
                    w.SetDronePursuitTarget(self, s.C9BB);
            }
        }
        else if (s.N64 is not null)
        {
            if (!ReferenceEquals(self.C8Ref, s.N64)) w.SetDronePursuitTarget(self, s.N64);
        }
        else if (s.N60 is not null)
        {
            if (!ReferenceEquals(self.C8Ref, s.N60)) w.SetDronePursuitTarget(self, s.N60);
        }
        else
        {
            if (self.Fa != 1)
            {
                self.Fa = 1;
                w.UpdateStrategicPosition(self);
            }
        }
        // Opportunistic c1 steal (:664-680).
        if (s.C1 is not null && !ReferenceEquals(self.B8Ref, s.C1)
            && (self.B8Ref is null
                || (!ReferenceEquals(self.B8Ref, s.N54) && !ReferenceEquals(self.B8Ref, s.N4C))))
        {
            int ex = s.C1.Pos[0] - self.Pos[0];
            int ez = s.C1.Pos[2] - self.Pos[2];
            if ((w.Sqrt12(Fmul(ex, ex) + Fmul(ez, ez)) << 2) < (self.Aa << 4))
            {
                w.SetDroneAttackTarget(self, s.C1);
                G.FE0 = 2;
            }
        }
    }

    /// <summary>Mode-2 economy (:685-742).</summary>
    private static void RunMode2Economy(Self self, Scan s, IWorld w)
    {
        var G = w.G;
        if (G.S4C >= 9
            && ((s.C1C is not null && s.D18 <= 0x23FFF) || (s.C5 is not null && s.D14 <= 0x1FFFF)))
        {
            if (G.F74 < 0x14 && G.F68 > 0)
            {
                G.FB0 += 1;
                G.F68 -= 1;
                G.F74 += 1;
            }
        }
        // DISTO tail: octagonal distance self -> posB minus the level bias.
        int s1 = Dist(self.Pos[0] - self.Strats.PosB[0], self.Pos[2] - self.Strats.PosB[2]);
        s1 -= G.S4C << 16;
        if (G.F78 < (G.S4C >> 2) + 4)
        {
            bool runSlotLoop;
            if (s1 <= 0x7FFFF)
            {
                int n = (G.S4C >> 2) + (w.NextRandom() & 3) + 4;
                int t = 0x14 - G.F78;
                if (t < n) n = t;
                t = G.F68;
                if (t < n) n = t;
                n -= (2 - (G.S4C >> 2)) * 2;
                if (n > 0)
                {
                    G.FB4 += n;
                    G.F68 -= n;
                    G.F78 += n;
                }
                // Retail falls through with goto L49E4 (:715) - the slot loop
                // runs after a pay attempt unconditionally.
                runSlotLoop = true;
            }
            else
            {
                runSlotLoop = G.F78 < 10;
            }
            if (runSlotLoop)
            {
                for (int i = 2; i < 4; i++)
                {
                    if (G.A0[i] == self.Team)
                    {
                        int n = w.NextRandom() & 3;
                        int t = 0x14 - G.F78;
                        if (t < n) n = t;
                        t = G.F68 - 5;
                        if (t < n * 2) n = t / 2;
                        if (n > 0)
                        {
                            G.C[i][1] += n;
                            G.F68 -= n * 2;
                            G.F78 += n;
                        }
                    }
                }
            }
        }
        if (s1 <= 0x7FFFF && s.N5C is null)
        {
            // C compares unsigned (:738): a negative balance still fails to pay.
            if ((uint)G.F68 > (uint)((w.NextRandom() & 7) + 0x41))
            {
                G.F68 -= 0x32;
                G.FBC += 1;
            }
        }
    }

    /// <summary>Slot claims for modes 0 and 1 (:743-760); returns the claim-fail counter.</summary>
    private static int RunSlotClaims(Self self, IWorld w)
    {
        var G = w.G;
        int c10 = 0;
        byte[] order = self.Strats.C1C == 1
            ? w.AlternateSlotClaimOrder
            : w.SlotClaimOrder;
        for (int i = 0; i < 4; i++)
        {
            int idx = order[i];
            if (G.A0[idx] == 0)
            {
                if (G.F68 >= 0x1E)
                {
                    if (G.F88 == 0) w.SetSinglePlayerAiS4C(w.FrameTime());
                    G.A0[idx] = self.Team;
                    G.F68 -= 0x1E;
                    if (G.F68 < 0x1E) break;
                }
                else
                {
                    c10++;
                }
            }
        }
        return c10;
    }

    /// <summary>Mode-1 economy (:762-789).</summary>
    private static void RunMode1Economy(Self self, IWorld w)
    {
        var G = w.G;
        for (int i = 0; i < 2; i++)
        {
            if (G.A0[i] == self.Team)
            {
                int n = (w.NextRandom() & 7) + 5 + (G.S4C >> 2);
                int t = 0x14 - G.F74;
                if (t < n) n = t;
                if (n >= 7 && G.F68 >= n * 2)
                {
                    G.C[i][0] += n;
                    G.F68 -= n * 2;
                    G.F74 += n;
                }
            }
        }
        if (G.F74 >= 12)
        {
            for (int i = 0; i < 2; i++)
            {
                if (G.A0[i] == self.Team)
                {
                    int n = (w.NextRandom() & 3) + 2 + (G.S4C >> 2);
                    int t = 0x14 - G.F78;
                    if (t < n) n = t;
                    if (n >= 4 && G.F68 >= n * 2)
                    {
                        G.C[i][1] += n;
                        G.F68 -= n * 2;
                        G.F78 += n;
                    }
                }
            }
        }
    }

    /// <summary>Mode-0 economy (:791-846).</summary>
    private static void RunMode0Economy(Self self, Scan s, int c10, IWorld w)
    {
        var G = w.G;
        var ai = self.Strats;
        if (s.N58 is null)
        {
            if (G.F68 >= 0x32)
            {
                G.F68 -= 0x32;
                G.FB8 += 1;
            }
        }
        if ((ai.C1C == 1 && G.S4C < 8)
            || (ai.C1C == 0 && G.S4C < 6)
            || (c10 <= 0 && ai.U32 > ai.U30))
        {
            if (G.S4C + 6 <= 0x14 - G.F74)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (G.A0[i] == self.Team)
                    {
                        int n = (w.NextRandom() & 7) + G.S4C;
                        n = n + 4 + (G.S4C >> 2);
                        int t = 0x14 - G.F74;
                        if (t < n) n = t;
                        if (n >= G.S4C + 6 && G.F68 >= n * 2)
                        {
                            G.C[i][0] += n;
                            G.F68 -= n * 2;
                            G.F74 += n;
                        }
                    }
                }
                if ((G.F88 == 0 && G.S4C < 5)
                    || (G.A0[0] == ai.B1B && G.A0[1] == G.A0[0]))
                {
                    if (G.F74 < 2)
                    {
                        int n = G.S4C + (w.NextRandom() & 3) + 6;
                        if (G.F84 == 4) n += 6;
                        int t = 0x14 - G.F74;
                        if (t < n) n = t;
                        if (n >= G.S4C + 6 && G.F68 >= n)
                        {
                            G.FB0 += n;
                            G.F68 -= n;
                            G.F74 += n;
                        }
                    }
                }
            }
        }
        if (ai.C1C == 0 && G.F78 <= 0)
        {
            int q = (w.NextRandom() & 1) + 1;
            int t = 0x14 - G.F78;
            if (t < q) q = t;
            t = G.F68;
            if (t < q) q = t;
            if (q > 0)
            {
                G.FB4 += q;
                G.F68 -= q;
                G.F78 += q;
            }
        }
    }
}
