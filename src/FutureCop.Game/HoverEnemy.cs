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
    public int[][] BasisRows = { new int[3], new int[3], new int[3] }; // +0x2C
    public int[] Up38 = new int[3];     // +0x38 up row (banking)
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
