using System.Collections.Generic;
using FutureCop.Game;
using FutureCop.Game.Mech;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>Recording world for the func_800357C0 handler tests.</summary>
internal sealed class MockDroneHandlerWorld : IStrategicDroneWorld
{
    public Queue<int> Randoms = new();
    public int FrameDeltaValue = 1;
    public int Player14 = 0x77;
    public int? SessionE0;
    public int InitCalls, TickCalls, HoverInitCalls, HoverTickCalls, TargetCalls, PositionCalls, BasisCalls;
    public List<int> BaseMessages = new();
    public byte FAAtPositionUpdate;

    public int NextRandom() => Randoms.Count > 0 ? Randoms.Dequeue() : 0;
    public int FrameDelta => FrameDeltaValue;
    public int PlayerUnk14 => Player14;
    public void SetSessionE0(int value) => SessionE0 = value;
    public int InitStrategicDrone(StrategicDrone d) { InitCalls++; return 0; }
    public void UpdateStrategicDrone(StrategicDrone d) => TickCalls++;
    public int InitHoverEnemy(StrategicDrone d) { HoverInitCalls++; return 0; }
    public int UpdateHoverEnemy(StrategicDrone d) { HoverTickCalls++; return 0; }
    public int EntityBaseHandler(int message, StrategicDrone d) { BaseMessages.Add(message); return 0x5A; }
    public void SetDroneAttackTarget(StrategicDrone d) => TargetCalls++;
    public void UpdateDroneStrategicPosition(StrategicDrone d) { PositionCalls++; FAAtPositionUpdate = d.Body.FA; }
    public void BuildBasisFromAxis(HoverEnemy o) => BasisCalls++;
}

/// <summary>TDD tests for the Sky Captain game side: 800331CC init part, 800357C0, 80035698, 80032F3C.</summary>
public sealed class SkyCaptainDroneTests
{
    private static StrategicDrone NewDrone(byte fa = 1)
    {
        var d = new StrategicDrone();
        d.Body.Type = 0x25;
        d.Body.FA = fa;
        d.Body.F4 = 0x1000;
        d.Body.F8 = 0x30;
        d.Body.F1E = 200;
        d.Body.F1C = 200;
        return d;
    }

    // ---- func_800331CC difficulty part (800331CC.c:134-203) ----

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(0, 0)]
    [InlineData(9, 9)]
    [InlineData(10, 9)]
    [InlineData(12, 9)]
    public void Init_ClampsDifficulty(int raw, int expected)
    {
        // 800331CC.c:134-139: < 0 -> 0, >= 10 -> 9.
        Assert.Equal(expected, StrategicDroneInit.ClampDifficulty(raw));
    }

    [Theory]
    // w, F4 (:169-176), F1E/F1C (:184-197), Limit with random 0x47 (& 0x3F = 7) (:198)
    [InlineData(4, 0x1000 + 1056, 150, 75 - 7)]       // w<6: 200 - 1 * 50
    [InlineData(0, 0x1000 - 0x1800, 50, 25 - 7)]      // w<3: F4 - 3<<11; 200 - 3 * 50
    [InlineData(6, 0x1000 + 3 * 1056, 200, 100 - 7)]  // neither branch
    [InlineData(9, 0x1000 + 6 * 1056, 250, 125 - 7)]  // w>=8: 200 + 2 * 25
    public void Init_ScalesBodyAndAiBlock_ExactCFormulas(int w, int f4, int hp, int limit)
    {
        var d = NewDrone(fa: 4);
        StrategicDroneInit.Apply(d.Body, d.Ai, w, 0x47);
        Assert.Equal(f4, d.Body.F4);
        Assert.Equal(f4, d.Ai.BaseF4);                  // :177 ai+0x24 = obj+0xF4
        Assert.Equal((short)0x30, d.Ai.PromoteTo);      // :178 ai+0x20 = obj+0xF8
        Assert.Equal(hp, d.Body.F1E);
        Assert.Equal(hp, d.Body.F1C);
        Assert.Equal(limit, d.Ai.Limit);
        Assert.Equal(4, d.Ai.InitialFA);                // :200 ai+0x1D = obj+0xFA
        Assert.Equal(2, d.Ai.Mode);                     // :201
        Assert.Equal(0, d.Ai.TickTimer);                // :203
    }

    // ---- func_800357C0 jump table ----

    [Fact]
    public void Handler_Message1_Returns0x138()
    {
        var w = new MockDroneHandlerWorld();
        Assert.Equal(0x138, StrategicDroneHandler.Handle(1, NewDrone(), null, w));
        Assert.Empty(w.BaseMessages);
    }

    [Fact]
    public void Handler_Message2_RunsInitStrategicDrone()
    {
        var w = new MockDroneHandlerWorld();
        StrategicDroneHandler.Handle(2, NewDrone(), null, w);
        Assert.Equal(1, w.InitCalls);
        Assert.Equal(0, w.HoverInitCalls);
    }

    [Theory]
    [InlineData(0)]   // index = id - 1 wraps unsigned -> >= 14 -> default
    [InlineData(3)]   // table entry case_default
    [InlineData(15)]  // index 14 -> default
    public void Handler_UnlistedMessages_FallThroughTumblingToBase(int message)
    {
        var w = new MockDroneHandlerWorld();
        Assert.Equal(0x5A, StrategicDroneHandler.Handle(message, NewDrone(), null, w));
        Assert.Equal(new[] { message }, w.BaseMessages);
    }

    [Fact]
    public void Handler_Message6_DeadState5_Returns0WithoutTick()
    {
        var w = new MockDroneHandlerWorld();
        Assert.Equal(0, StrategicDroneHandler.Handle(6, NewDrone(fa: 5), null, w));
        Assert.Equal(0, w.TickCalls);
        Assert.Equal(0, w.HoverTickCalls);
    }

    [Fact]
    public void Handler_Message6_RunsStrategicTick_NotTheHoverTail()
    {
        var w = new MockDroneHandlerWorld();
        Assert.Equal(0, StrategicDroneHandler.Handle(6, NewDrone(fa: 1), null, w));
        Assert.Equal(1, w.TickCalls);
        Assert.Equal(0, w.HoverTickCalls);
    }

    [Fact]
    public void Handler_Message4WhileState5_CountsDownWithoutRearm()
    {
        var d = NewDrone(fa: 5);
        d.Body.F100 = 10;
        var w = new MockDroneHandlerWorld { FrameDeltaValue = 4 };
        Assert.Equal(0, StrategicDroneHandler.Handle(4, d, null, w));
        Assert.Equal(6, d.Body.F100);
        Assert.Equal(5, d.Body.FA);
        Assert.Null(w.SessionE0);
    }

    [Theory]
    [InlineData(3, 4)]          // 3 - 4 = 0xFFFF, (s16) -1 <= 0
    [InlineData(4, 4)]          // exactly 0
    [InlineData(0x8000, 0)]     // (s16)0x8000 is negative: s16 compare, not u16
    public void Handler_Message4WhileState5_RearmsWhenS16NotPositive(int timer, int dt)
    {
        var d = NewDrone(fa: 5);
        d.Body.F100 = (ushort)timer;
        d.Ai.InitialFA = 1;
        var w = new MockDroneHandlerWorld { FrameDeltaValue = dt };
        Assert.Equal(0, StrategicDroneHandler.Handle(4, d, null, w));
        Assert.Equal(9, w.SessionE0);                   // 80035698.c:156-160
        Assert.Equal(1, d.Body.FA);
    }

    [Fact]
    public void Handler_Message4Alive_Type25AlwaysStartsTheTumble()
    {
        // 800357C0 case_4 -> default -> 80032F3C case 4: unk16 == 0x25 short-circuits the random.
        var d = NewDrone(fa: 1);
        d.Body.Dir[0] = 0x10000; d.Body.Dir[1] = -0x8000; d.Body.Dir[2] = 0;
        d.Body.F4 = 0x2000;
        var w = new MockDroneHandlerWorld();
        foreach (var r in new[] { 0x1123, 0x0FFF, 0x0000 }) w.Randoms.Enqueue(r);
        Assert.Equal(0, StrategicDroneHandler.Handle(4, d, null, w));
        Assert.Equal(3, d.Body.FA);
        Assert.Equal(new[] { 0x2000, -0x1000, 0 }, d.Body.DC);  // +0x78 scaled by +0xF4 (8001AB78)
        Assert.Equal(new[] { 0x123 - 0x800, 0xFFF - 0x800, -0x800 }, d.Body.E8);
        Assert.Empty(w.BaseMessages);
    }

    [Fact]
    public void Tumbling_Message4AlreadyTumbling_Returns0Unchanged()
    {
        var d = NewDrone(fa: 3);
        d.Body.DC[0] = 0x55;
        var w = new MockDroneHandlerWorld();
        Assert.Equal(0, StrategicDroneHandler.Handle(4, d, null, w));
        Assert.Equal(0x55, d.Body.DC[0]);
        Assert.Empty(w.BaseMessages);
    }

    [Fact]
    public void Tumbling_Message4OtherType_RandomGate()
    {
        // 80032F3C.c case 4: non-0x25 tumbles only when (rand & 0xF) < 0xA, else base handler.
        var d = NewDrone(fa: 1);
        d.Body.Type = 0x20;
        var w = new MockDroneHandlerWorld();
        w.Randoms.Enqueue(0xA);
        Assert.Equal(0x5A, StrategicDroneHandler.HandleTumblingFlyer(4, d, w));
        Assert.Equal(1, d.Body.FA);
        Assert.Equal(new[] { 4 }, w.BaseMessages);
    }

    [Fact]
    public void Tumbling_Messages1And6_UseHoverEntries()
    {
        var w = new MockDroneHandlerWorld();
        Assert.Equal(0x134, StrategicDroneHandler.HandleTumblingFlyer(1, NewDrone(), w));
        StrategicDroneHandler.HandleTumblingFlyer(2, NewDrone(), w);
        StrategicDroneHandler.HandleTumblingFlyer(6, NewDrone(), w);
        Assert.Equal(1, w.HoverInitCalls);
        Assert.Equal(1, w.HoverTickCalls);
    }

    [Fact]
    public void Handler_Message9_PlayerHit_SetsSessionE05_ThenTumblingTail()
    {
        var w = new MockDroneHandlerWorld();
        var sender = new DroneMessageSender { Child08 = new DroneMessageSender { Unk14 = 0x77 } };
        Assert.Equal(0x5A, StrategicDroneHandler.Handle(9, NewDrone(), sender, w));
        Assert.Equal(5, w.SessionE0);
        Assert.Equal(new[] { 9 }, w.BaseMessages);
    }

    [Fact]
    public void Handler_Message9_NoChildOrOtherOwner_LeavesSessionE0()
    {
        var w = new MockDroneHandlerWorld();
        StrategicDroneHandler.Handle(9, NewDrone(), new DroneMessageSender(), w);
        StrategicDroneHandler.Handle(9, NewDrone(), new DroneMessageSender { Child08 = new DroneMessageSender { Unk14 = 1 } }, w);
        Assert.Null(w.SessionE0);
        Assert.Equal(new[] { 9, 9 }, w.BaseMessages);
    }

    [Fact]
    public void Handler_Message11_ComparesSenderUnk14()
    {
        var w = new MockDroneHandlerWorld();
        StrategicDroneHandler.Handle(11, NewDrone(), new DroneMessageSender { Unk14 = 0x77, Unk00 = 1 }, w);
        Assert.Equal(5, w.SessionE0);
        var w2 = new MockDroneHandlerWorld();
        StrategicDroneHandler.Handle(11, NewDrone(), null, w2);
        Assert.Null(w2.SessionE0);
        Assert.Equal(new[] { 11 }, w2.BaseMessages);
    }

    [Fact]
    public void Handler_Message14_ComparesSenderUnk00()
    {
        var w = new MockDroneHandlerWorld();
        StrategicDroneHandler.Handle(14, NewDrone(), new DroneMessageSender { Unk00 = 0x77, Unk14 = 1 }, w);
        Assert.Equal(5, w.SessionE0);
        var w2 = new MockDroneHandlerWorld();
        StrategicDroneHandler.Handle(14, NewDrone(), new DroneMessageSender { Unk00 = 1, Unk14 = 0x77 }, w2);
        Assert.Null(w2.SessionE0);
    }

    // ---- func_80035698 gRearmDroneWander ----

    [Fact]
    public void Rearm_RestoresBlockAndHeading_ExactCOrder()
    {
        var d = NewDrone(fa: 5);
        var o = d.Body;
        o.F1C = 3;
        o.F1E = 160;
        o.F19 = 0xA7;
        o.F4 = 1;
        o.F8 = 2;
        o.Flags24 = 0xFFFFFFFF;
        o.D0X = 0x1000; o.D4Y = 0x2000; o.D8Z = 0x3000;
        o.Pos[0] = 9; o.Pos[1] = 9; o.Pos[2] = 9;
        o.E8[0] = 5; o.E8[1] = 6; o.E8[2] = 7;
        d.Ai.BaseF4 = 0x1420;
        d.Ai.PromoteTo = 0x30;
        d.Ai.TickTimer = 99;
        d.Ai.Mode = 2;
        d.Ai.InitialFA = 1;
        var w = new MockDroneHandlerWorld();
        w.Randoms.Enqueue(0x45);        // :129 limit draw, & 0x3F = 5
        w.Randoms.Enqueue(0x1400);      // :145 heading draw, & 0xFFF = 0x400

        StrategicDroneHandler.RearmDroneWander(d, w);

        Assert.Equal(160, o.F1C);                       // :128 unk1C = unk1E
        Assert.Equal(80 - 5, d.Ai.Limit);               // :129
        Assert.Equal(0x07, o.F19);                      // :130 &= 0x0F
        Assert.Equal(0x1420, o.F4);                     // :132 ai+0x24
        Assert.Equal(0xFDFFFFCAu, o.Flags24);           // :133-135
        Assert.Equal((short)0x30, o.F8);                // :137 ai+0x20
        Assert.Equal(0, d.Ai.TickTimer);                // :142
        Assert.Equal(1, w.TargetCalls);                 // :143
        Assert.Equal(0, d.Ai.Mode);                     // :142
        Assert.Equal(1, w.PositionCalls);               // :143
        Assert.Equal(4, w.FAAtPositionUpdate);          // :136 FA = 4 before the position update
        var (sin, cos) = MechMath.SinCos(0x400);
        Assert.Equal(new[] { sin, 0, cos }, o.Dir);     // :145-153
        Assert.Equal(new[] { 0, 0, 0 }, o.E8);          // :150-152
        Assert.Equal(new[] { 0x1000, 0x2000, 0x3000 }, o.Pos); // :154 unk50 = unkD0
        Assert.Equal(1, w.BasisCalls);                  // :155
        Assert.Equal(1, o.FA);                          // :156 ai+0x1D
        Assert.Equal(9, w.SessionE0);                   // :158-160
    }
}
