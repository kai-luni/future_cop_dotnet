using FutureCop.Game.Mech;

namespace FutureCop.Game;

/// <summary>
/// Mutable state of one hovering enemy of the Precinct Assault air-unit family,
/// mirroring the object block layout func_800324E0 works on. Field offsets in the
/// comments refer to the retail block (src/game/800324E0.c:14-28, 800320E4.c:4-19).
/// </summary>
public sealed class HoverEnemy
{
    public byte Type = 0x25;            // +0x16
    public byte F19;                    // +0x19
    public short F1C;                   // +0x1C
    public ushort F1E;                  // +0x1E
    public byte F20;                    // +0x20
    public uint Flags24;                // +0x24
    public int AnimCounter;             // model +0x28 -> +0x10 (message-6 anim gate)
    public int[][] BasisRows = { new int[3], new int[3], new int[3] }; // +0x2C (+0x2C/+0x38/+0x44)
    /// <summary>Up row of the basis matrix at +0x38 (aliases <see cref="BasisRows"/>[1], 800324E0.c:17,359).</summary>
    public int[] Up38 { get => BasisRows[1]; set => BasisRows[1] = value; }
    public int[] Pos = new int[3];      // +0x50 position
    public int F68;                     // +0x68 body height
    public short F6C;                   // +0x6C ground clearance << 4
    public int[] Dir = new int[3];      // +0x78 flight direction accumulator
    public int F8C;                     // +0x8C last hit block id (kill credit)
    public int F98;                     // +0x98 death script pointer
    public uint FA0;                    // +0xA0 attack scheduler word
    public HoverEnemyHit? C8;           // +0xC8 tracked target (func_8004E80C result)
    public int CC;                      // +0xCC cached target block id
    public int D0X;                     // +0xD0 home x (also wander base x)
    public int D4Y;                     // +0xD4 home y (wander base y)
    public int D8Z;                     // +0xD8 home z (also wander base z)
    public int[] DC = new int[3];      // +0xDC wander point / dive direction
    public int[] E8 = new int[3];      // +0xE8 velocity accumulator
    public int F4;                      // +0xF4 vertical speed scale
    public short F8;                    // +0xF8 horizontal speed scale
    public byte FA;                     // +0xFA state
    public ushort FC;                   // +0xFC wander timer
    public ushort FE;                   // +0xFE pursue timer
    public ushort F100;                 // +0x100 state timer
    public ushort F102;                 // +0x102 retarget countdown
    public byte F104;                   // +0x104 search class filter
    public byte F105;                   // +0x105 search type
    public short F106;                  // +0x106 search class word
    public short F108;                  // +0x108 search range << 4
    public short F10A;                  // +0x10A turn-away threshold
    public short F10C;                  // +0x10C wander spread z (800320E4.c:16)
    public short F10E;                  // +0x10E wander spread x (800320E4.c:17)
    public short F110;                  // +0x110 low band edge
    public short F112;                 // +0x112 high band edge
    public short F114;                  // +0x114 upper band edge
    public short F116;                  // +0x116 turn-away cooldown
    public int F118;                    // +0x118 descent scale
    public int F11C;                    // +0x11C climb scale
    public short F12C;                  // +0x12C anim frame passed to gSetModelFrame
    public int RecF48;                  // +0x134 record +0x48 frame-time stamp
    public int RecF3E;                  // +0x134 record +0x3E impact counter
}

/// <summary>Target view of a func_8004E80C search result (id +0x08, pos +0x50, class word +0x14).</summary>
public sealed class HoverEnemyHit
{
    public int Id;                      // +0x08 block id
    public int[] Pos = new int[3];     // +0x50 position
    public int F14;                     // +0x14 class word (team/class comparison)
}

/// <summary>
/// World services function_800324E0 delegates to; implemented by the scene.
/// Every member cites the retail callee it stands for.
/// </summary>
public interface IHoverEnemyWorld
{
    int NextRandom();                                  // func_80019668
    int SampleHeightBilinear(int[] pos);               // func_80069A38
    int GetGroundHeight(int[] pos);                    // func_80066F78
    HoverEnemyHit? FindTargetEntity(HoverEnemy o);     // func_8004E80C with o.f105/f106/f104/f108
    void StepAttackScheduler(HoverEnemy o, int dt);    // func_80060238 on +0xA0
    void SpawnEntitySlotTypes(int tableIndex, HoverEnemy o, int arg2, int arg3, int arg4); // func_8003B898
    void FlagQuartileChange(HoverEnemy o);             // func_8003BA50
    int SessionWord13();                               // gGameSession[0x13] (D_8009FA94)
    int FrameTime();                                   // D_8009FAA4
    void RunDeathScript(HoverEnemy o);                 // func_80069D68 on +0x98
    bool IsPlayerBlockClass(int id);                   // func_8004D664 + class word compare
    void CreditPlayerKill();                           // gPlayerEntities->fAC->f14++
    void AdvanceAnimation(HoverEnemy o, int dt);       // func_80044B34 + func_800175F8
}

/// <summary>
/// Port of <c>gInitHoverEnemy</c> (<c>func_80032180</c>, <c>80032180.c:195-278</c>),
/// <c>gBuildBasisFromAxis</c> (<c>func_800193F8</c>, <c>800193F8.c:52-60</c>) and
/// <c>gPickHoverWanderTarget</c> (<c>func_800320E4</c>, <c>800320E4.c:45-54</c>).
/// Derives flight speed <c>fF4</c>, turn rate <c>fF8</c>, altitude band
/// <c>f110/f112/f114</c> and band reciprocals <c>f118/f11C</c> directly from the
/// entity's <c>tACT</c> source bytes.
/// </summary>
public static class HoverEnemyInit
{
    public static HoverEnemy Create(
        EntityRecord record,
        Func<int[], int>? groundHeight,
        Func<int> nextRandom)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(nextRandom);

        unchecked
        {
            var o = new HoverEnemy
            {
                // gInitEntityBase (8002A5F4.c:127-159)
                Type = (byte)record.Type,
                F1C = (short)record.Health,
                F1E = (ushort)(short)record.Health,
                F20 = (byte)record.U8(0x20),
                Flags24 = record.Flags,
            };
            o.Pos[0] = record.X;
            o.Pos[1] = record.Y;
            o.Pos[2] = record.Z;

            // gInitHoverEnemy (80032180.c:207-233)
            byte kind = (byte)record.U8(0x38);
            o.FA = kind == 2 ? (byte)1 : kind;
            byte fb = (byte)record.U8(0x39);
            o.FC = (ushort)(((short)record.U16(0x4C) * 75) >> 6);
            o.FE = (ushort)(((short)record.U16(0x4E) * 75) >> 6);
            o.F100 = o.FA != 0 ? o.FE : o.FC;
            o.F102 = 0xFFFF;
            o.F116 = -1;
            o.C8 = null;

            o.F104 = (byte)record.U8(0x34);
            o.F105 = (byte)record.U8(0x35);
            o.F106 = (short)record.U16(0x36);
            o.F10A = (short)record.U16(0x3C);
            o.F108 = (short)record.U16(0x3A);
            o.F10C = (short)((short)record.U16(0x48) >> 5);
            o.F10E = (short)((short)record.U16(0x4A) >> 5);
            o.F110 = (short)record.U16(0x3E);
            o.F114 = (short)record.U16(0x40);
            o.F112 = (short)record.U16(0x42);
            o.F8 = (short)record.U16(0x44);
            o.F4 = ((short)record.U16(0x46) << 12) / 300;

            // Placement (80032180.c:235-257)
            if ((fb & 8) != 0)
            {
                o.Pos[2] = (short)record.U16(0x50) << 9;
                o.Pos[0] = (short)record.U16(0x52) << 9;
            }
            int value = groundHeight?.Invoke(o.Pos) ?? (o.Pos[1] - (o.F114 << 4));
            o.Pos[1] = value + (o.F114 << 4);
            o.D0X = o.Pos[0];
            o.D4Y = o.Pos[1];
            o.D8Z = o.Pos[2];

            if ((fb & 1) != 0)
            {
                PickWanderTarget(o, nextRandom);
                if (o.Type != 0x25 && o.FA != 3)
                {
                    o.Pos[0] = o.DC[0];
                    o.Pos[1] = o.DC[1];
                    o.Pos[2] = o.DC[2];
                    value = groundHeight?.Invoke(o.Pos) ?? (o.Pos[1] - (o.F114 << 4));
                    o.Pos[1] = value + (o.F114 << 4);
                }
            }

            if ((fb & 2) != 0)
            {
                o.Pos[1] += 0x20000;
            }
            else if ((fb & 4) != 0)
            {
                o.FA = 4;
                o.Pos[1] = value + (o.F6C << 4);
            }

            o.E8[0] = o.E8[1] = o.E8[2] = 0;

            // Random 12-bit horizontal heading + basis + initial wander target (:263-268).
            var (sin, cos) = MechMath.SinCos(nextRandom() & 0xFFF);
            o.Dir[0] = sin;
            o.Dir[1] = 0;
            o.Dir[2] = cos;
            BuildBasisFromAxis(o.Dir, o.BasisRows);
            PickWanderTarget(o, nextRandom);

            // Band reciprocals (:270-271), guarded against zero-width bands on synthetic records.
            uint hi = (uint)((o.F112 - o.F114) << 3);
            uint lo = (uint)((o.F114 - o.F110) << 3);
            o.F118 = hi == 0 ? 0 : (int)(0x80000000u / hi);
            o.F11C = lo == 0 ? 0 : (int)(0x80000000u / lo);
            return o;
        }
    }

    /// <summary>Port of <c>gBuildBasisFromAxis</c> (<c>func_800193F8</c>, <c>800193F8.c:52-60</c>).</summary>
    public static void BuildBasisFromAxis(int[] axis, int[][] basis)
    {
        basis[0][0] = axis[2];
        basis[0][1] = 0;
        basis[0][2] = unchecked(-axis[0]);
        MechMath.Normalize(basis[0]);
        MechMath.GteCross(axis, basis[0], basis[1]);
        basis[2][0] = axis[0];
        basis[2][1] = axis[1];
        basis[2][2] = axis[2];
    }

    /// <summary>Port of <c>gPickHoverWanderTarget</c> (<c>func_800320E4</c>, <c>800320E4.c:45-54</c>).</summary>
    public static void PickWanderTarget(HoverEnemy o, Func<int> nextRandom)
    {
        if (o.FA != 3 && o.Type != 0x25)
        {
            int rx = (nextRandom() & 0x7FF) - 0x400;
            int rz = (nextRandom() & 0x7FF) - 0x400;
            o.DC[1] = o.D4Y;
            o.DC[0] = unchecked(o.D0X + ((rx * o.F10E) >> 3));
            o.DC[2] = unchecked(o.D8Z + ((rz * o.F10C) >> 3));
        }
    }
}
