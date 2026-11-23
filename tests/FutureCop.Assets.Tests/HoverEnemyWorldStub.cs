using FutureCop.Game;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Deterministic world for HoverEnemyTick tests: fixed rng, flat height,
/// a configurable target and call recording.
/// </summary>
internal sealed class MockHoverWorld : IHoverEnemyWorld
{
    public int RandValue = 0x400;             // func_80019668 stub
    public int HeightBilinear = 0;            // func_80069A38 stub
    public int GroundHeight = -100000;        // func_80066F78 stub (below dive probe)
    public HoverEnemyHit? Target;             // func_8004E80C stub
    public int SchedulerCalls;
    public int KillCredits;
    public int DeathScripts;
    public int FlagCalls;

    public int NextRandom() => RandValue;
    public int SampleHeightBilinear(int[] pos) => HeightBilinear;
    public int GetGroundHeight(int[] pos) => GroundHeight;
    public HoverEnemyHit? FindTargetEntity(HoverEnemy o) => Target;
    public void StepAttackScheduler(HoverEnemy o, int dt) => SchedulerCalls++;
    public void SpawnEntitySlotTypes(int tableIndex, HoverEnemy o, int a2, int a3, int a4) { }
    public void FlagQuartileChange(HoverEnemy o) => FlagCalls++;
    public int SessionWord13() => 4;
    public int FrameTime() => 1234;
    public void RunDeathScript(HoverEnemy o) => DeathScripts++;
    public bool IsPlayerBlockClass(int id) => true;
    public void CreditPlayerKill() => KillCredits++;
    public void AdvanceAnimation(HoverEnemy o, int dt) { }
}

/// <summary>Common fixture values of the hover tests (spread/edge words as CONFT uses them).</summary>
internal static class HoverTestValues
{
    public static HoverEnemy NewEnemy()
    {
        // Field values follow the 800324E0 read pattern: spread +0x10C/+0x10E,
        // band edges +0x110/+0x112/+0x114, speeds +0xF4/+0xF8.
        return new HoverEnemy
        {
            Type = 0x25,
            F68 = 0x10000,
            F6C = 0,
            F4 = 0x00040000,
            F8 = 0x00300000 >> 8,
            F10A = 0x10,
            F10C = 0x100,
            F10E = 0x100,
            F110 = -0x10,
            F112 = 0x10,
            F114 = 0x40,
            F116 = 0,
            F118 = 0x180,
            F11C = 0x180,
            FA0 = 1,
        };
    }
}
