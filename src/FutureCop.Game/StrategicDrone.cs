using System;
using FutureCop.Game.Mech;

namespace FutureCop.Game;

/// <summary>
/// The second mech of 1P Precinct Assault: the Sky Captain drone (entity type 37 / 0x25).
/// Composites the ports of func_800331CC (msg 2 init, cref/800331CC.c) and the entity
/// handler func_800357C0 (cref/800357C0.c) on top of the headless HoverEnemy /
/// StrategicDroneTick / DroneStrategy / EnemyAttackScheduler ports.
/// </summary>
public sealed class StrategicDrone
{
    /// <summary>The flyer body. func_800331CC.c inits it via gInitHoverEnemy (func_80032180).</summary>
    public HoverEnemy Body = new();

    /// <summary>+0x134: the 0xB4 AI block. func_8003532C reads it as StrategicDroneTick.AiState.</summary>
    public StrategicDroneTick.AiState Ai = new();

    /// <summary>Drone tick state (approach word, positions, +0xFA state) driven by StrategicDroneTick.</summary>
    public StrategicDroneTick.DroneEntity Drone = new();

    /// <summary>+0x50 attack slot 0. func_800331CC.c init via func_80060668.</summary>
    public AttackSlot Slot0 = new();

    /// <summary>+0x78 attack slot 1. func_800331CC.c init via func_80060668.</summary>
    public AttackSlot Slot1 = new();

    // The msg-4 respawn countdown is the body word +0x100 (Body.F100, 800357C0.c case_4).
}

/// <summary>
/// The difficulty-dependent part of func_800331CC gInitStrategicDrone (800331CC.c:126-237)
/// that acts on the body and the AI block. The session-only writes (gauges, +0xA0/+0xB0
/// tables, gSinglePlayerEntity) and the attack slot init are outside this helper.
/// </summary>
public static class StrategicDroneInit
{
    /// <summary>800331CC.c:134-139: gGameSession +0x4C is clamped to 0..9.</summary>
    public static int ClampDifficulty(int w) => w < 0 ? 0 : (w >= 10 ? 9 : w);

    /// <summary>
    /// Applies 800331CC.c:169-203 to an already hover-initialised body. <paramref name="w"/> must be
    /// the clamped difficulty; <paramref name="random"/> is the func_80019668 draw of :198.
    /// </summary>
    public static void Apply(HoverEnemy o, StrategicDroneTick.AiState ai, int w, int random)
    {
        unchecked
        {
            // :169-176 +0xF4 scaled by difficulty.
            if (w < 3) o.F4 -= (3 - w) << 11;
            else o.F4 += (w - 3) * 1056;
            ai.BaseF4 = o.F4;                       // :177 ai+0x24
            ai.PromoteTo = o.F8;                    // :178 ai+0x20
            if (w < 6)                              // :184-190
            {
                o.F1E = (ushort)(o.F1E - ((7 - w) >> 1) * ((short)o.F1E >> 2));
                o.F1C = (short)o.F1E;
            }
            if (w >= 8)                             // :193-197
            {
                o.F1E = (ushort)(o.F1E + (w - 7) * ((short)o.F1E >> 3));
                o.F1C = (short)o.F1E;
            }
            ai.Limit = (short)(((short)o.F1E >> 1) - (random & 0x3F)); // :198
            ai.InitialFA = o.FA;                    // :200 ai+0x1D
            ai.Mode = 2;                            // :201 ai+0x1A
            ai.TickTimer = 0;                       // :203 ai+0x18
        }
    }
}

/// <summary>Sender view of a hit message (the a2 argument of func_800357C0).</summary>
public sealed class DroneMessageSender
{
    public int Unk00;                         // +0x00 (message 14 compares it)
    public int Unk14;                         // +0x14 (message 11 compares it)
    public DroneMessageSender? Child08;       // +0x08 (message 9 compares its +0x14)
}

/// <summary>Services func_800357C0 and its callees reach outside the drone block.</summary>
public interface IStrategicDroneWorld
{
    int NextRandom();                                       // func_80019668
    int FrameDelta { get; }                                 // gFrameDelta
    int PlayerUnk14 { get; }                                // gPlayerEntities +0x14
    void SetSessionE0(int value);                           // gGameSession +0xE0
    int InitStrategicDrone(StrategicDrone d);               // func_800331CC (msg 2)
    void UpdateStrategicDrone(StrategicDrone d);            // func_8003532C (msg 6)
    int InitHoverEnemy(StrategicDrone d);                   // func_80032180 (tumbling msg 2)
    int UpdateHoverEnemy(StrategicDrone d);                 // func_800324E0 (tumbling msg 6)
    int EntityBaseHandler(int message, StrategicDrone d);   // gEntityBaseHandler
    void SetDroneAttackTarget(StrategicDrone d);            // func_80033608(self, 0)
    void UpdateDroneStrategicPosition(StrategicDrone d);    // func_800330E8
    void BuildBasisFromAxis(HoverEnemy o);                  // func_800193F8(+0x78, +0x2C)
}

/// <summary>Port of func_800357C0 gHandleStrategicDroneMessage and its tail callees.</summary>
public static class StrategicDroneHandler
{
    /// <summary>
    /// 800357C0.c: 14-entry jump table on message - 1; out-of-range and unlisted messages fall
    /// through to func_80032F3C gHandleTumblingFlyer.
    /// </summary>
    public static int Handle(int message, StrategicDrone d, DroneMessageSender? sender, IStrategicDroneWorld w)
    {
        var o = d.Body;
        switch (message)
        {
            case 1:
                return 0x138;                                   // case_1
            case 2:
                return w.InitStrategicDrone(d);                 // case_2
            case 6:
                if (o.FA == 5) return 0;                        // case_6
                w.UpdateStrategicDrone(d);
                return 0;
            case 4:
                if (o.FA != 5) break;                           // case_4 -> default
                unchecked
                {
                    o.F100 = (ushort)(o.F100 - w.FrameDelta);
                    if ((short)o.F100 > 0) return 0;            // s16 compare
                }
                RearmDroneWander(d, w);
                return 0;
            case 9:
                if (sender?.Child08 is not { } child) break;    // a2->unk08 == 0 -> default
                if (child.Unk14 == w.PlayerUnk14) w.SetSessionE0(5);
                break;
            case 11:
                if (sender is null) break;
                if (sender.Unk14 == w.PlayerUnk14) w.SetSessionE0(5);
                break;
            case 14:
                if (sender is null) break;
                if (w.PlayerUnk14 == sender.Unk00) w.SetSessionE0(5);
                break;
        }
        return HandleTumblingFlyer(message, d, w);
    }

    /// <summary>func_80032F3C gHandleTumblingFlyer (80032F3C.c).</summary>
    public static int HandleTumblingFlyer(int message, StrategicDrone d, IStrategicDroneWorld w)
    {
        var o = d.Body;
        switch (message)
        {
            case 1:
                return 0x134;
            case 2:
                return w.InitHoverEnemy(d);
            case 6:
                return w.UpdateHoverEnemy(d);
            case 4:
                if (o.FA == 5) break;                           // -> dflt
                if (o.FA == 3) return 0;
                if (o.Type == 0x25 || (w.NextRandom() & 0xF) < 0xA)
                {
                    unchecked
                    {
                        o.FA = 3;
                        o.DC[0] = o.Dir[0]; o.DC[1] = o.Dir[1]; o.DC[2] = o.Dir[2];
                        EnemyMath.ScaleInPlace(o.F4, o.DC);     // func_8001AB78
                        o.E8[0] = (w.NextRandom() & 0xFFF) - 0x800;
                        o.E8[1] = (w.NextRandom() & 0xFFF) - 0x800;
                        o.E8[2] = (w.NextRandom() & 0xFFF) - 0x800;
                    }
                    return 0;
                }
                // The C retests unk16 == 0x25 here, unreachable after the test above.
                break;
        }
        return w.EntityBaseHandler(message, d);
    }

    /// <summary>func_80035698 gRearmDroneWander (80035698.c:128-160).</summary>
    public static void RearmDroneWander(StrategicDrone d, IStrategicDroneWorld w)
    {
        var o = d.Body;
        var ai = d.Ai;
        unchecked
        {
            o.F1C = (short)o.F1E;
            ai.Limit = (short)(((short)o.F1E >> 1) - (w.NextRandom() & 0x3F));
            o.F19 &= 0x0F;
            o.F4 = ai.BaseF4;
            o.Flags24 &= 0xFDFFFFCA;
            o.FA = 4;
            o.F8 = ai.PromoteTo;
            ai.TickTimer = 0;
            w.SetDroneAttackTarget(d);
            ai.Mode = 0;
            w.UpdateDroneStrategicPosition(d);
            var (sin, cos) = MechMath.SinCos(w.NextRandom() & 0xFFF);   // func_8001846C
            o.Dir[0] = sin; o.Dir[1] = 0; o.Dir[2] = cos;
            o.E8[0] = 0; o.E8[1] = 0; o.E8[2] = 0;
            o.Pos[0] = o.D0X; o.Pos[1] = o.D4Y; o.Pos[2] = o.D8Z;          // +0x50 = +0xD0
            w.BuildBasisFromAxis(o);
            o.FA = ai.InitialFA;
            w.SetSessionE0(9);
        }
    }
}
