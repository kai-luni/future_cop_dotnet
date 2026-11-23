using FutureCop.Game;
using FutureCop.Game.Mech;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>Deterministic world for DroneStrategy tests (800336DC).</summary>
internal sealed class MockStrategyWorld : DroneStrategy.IWorld
{
    public System.Collections.Generic.List<DroneStrategy.StrategyUnit> UnitList = new();
    public int Rand = 0;
    public int AttackCalls;
    public int PursuitCalls;
    public int StrategicCalls;
    public int SinglePlayerS4CWrites;

    public System.Collections.Generic.IEnumerable<DroneStrategy.StrategyUnit> Units => UnitList;
    DroneStrategy.Glob DroneStrategy.IWorld.G => G;
    public DroneStrategy.Glob G { get; } = new();
    public DroneStrategy.StrategyUnit? PlayerEntities => null;
    public int NextRandom() => Rand;
    public int Sqrt12(int v) => MechMath.Sqrt12(v);
    public void SetDroneAttackTarget(DroneStrategy.Self self, DroneStrategy.StrategyUnit? unit) => AttackCalls++;
    public void SetDronePursuitTarget(DroneStrategy.Self self, DroneStrategy.StrategyUnit unit) => PursuitCalls++;
    public void UpdateStrategicPosition(DroneStrategy.Self self) => StrategicCalls++;
    public byte[] SlotClaimOrder => new byte[] { 3, 2, 1, 0 };
    public byte[] AlternateSlotClaimOrder => new byte[] { 1, 0, 3, 2 };
    public int FrameTime() => 991;
    public void SetSinglePlayerAiS4C(int value) => SinglePlayerS4CWrites++;
}
