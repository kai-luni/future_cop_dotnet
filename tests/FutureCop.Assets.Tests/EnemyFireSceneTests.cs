using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The hostile fire path of the live level: a spawned enemy unit finds the player through the
/// attack slot built from its record descriptor (gInitAttackSchedulerSlot, 80060668.c:77-107),
/// fires through func_80060238 and func_8005F6E8, and the shot reaches the player entity where
/// message 10/11 of the player handler subtract the damage from the armour word
/// (800584BC.c:117-119, gApplyPlayerHealthDamage.c). Every step runs through
/// <see cref="LevelScene.AdvanceSoloFrame"/>; the test never calls the hit path directly.
/// </summary>
public class EnemyFireSceneTests
{
    private static void Pin(SpawnedCsacUnit unit, (int X, int Y, int Z) player)
    {
        // Keep the rail unit next to the player and facing him. The AI steering is not ported, so
        // the test holds the unit in firing position instead of relying on path following.
        unit.X = player.X + 0x400;
        unit.Y = player.Y;
        unit.Z = player.Z;
        unit.DirX = -0x10000;
        unit.DirZ = 0;
    }

    [RetailFact]
    public void EnemyShotLosesArmourAndNegativeHealthRetiresThenRespawnsThePlayer()
    {
        string mission = LevelSceneRetirementTests.FindMission()!;
        var scene = new LevelScene(mission);
        scene.EnableMechMovement();
        scene.EnableFiring();
        scene.EnableEnemyFire();

        var player = scene.PlayerUnit!;
        var walker = scene.Walker!;
        var spawner = scene.CsacSpawner!;
        int playerTeam = scene.Objectives!.Score.PlayerTeams[0];
        Assert.InRange(playerTeam, 1, 2);

        // A type-5 unit whose descriptor (record +0x24) selects attack key 6 and the player's class.
        // Key 6 is a rapid type-2 direct shot (W0A = 0, W0C = 50) - see the CONFT attack table.
        var record = MissionEntities.Parse(File.ReadAllBytes(mission)).First(r =>
            r.Type == 5 && r.Container == EntityContainer.Csac &&
            r.U8(0x24) == 6 && r.U8(0x29) == 4 && r.U16(0x2A) == playerTeam);
        var unit = spawner.TrySpawn(spawner.Register(record)!, 1);
        Assert.NotNull(unit);

        short start = player.Health;
        Assert.True(start > 0);
        Assert.False(player.Dead);

        var home = (X: walker.X, Z: walker.Z);
        Assert.Equal(home.X, player.Checkpoint.X);
        Assert.Equal(home.Z, player.Checkpoint.Z);

        void Frame()
        {
            Pin(unit, scene.MechWorld);
            scene.AdvanceSoloFrame(1, 1, 0, default);
        }

        // Acceptance 1: the armour word the HUD reads drops by the weapon damage of key 6.
        int frames = 0;
        while (player.Health == start && frames++ < 400) Frame();
        Assert.True(player.Health < start, "no enemy shot reached the player through AdvanceSoloFrame");
        Assert.Equal(50, start - player.Health);
        Assert.Equal(player.Health, walker.Health);   // HudDraw.DrawPlayerGauge reads this word

        // Acceptance 2: retail retires at health < 0 (8004CE04.c:59-60 -> message 4), not at zero.
        frames = 0;
        while (!player.Dead && frames++ < 2000) Frame();
        Assert.True(player.Dead,
            $"survived: health={player.Health} hits={scene.EnemyFire!.HitsDelivered} " +
            $"shots={scene.EnemyFire.ShotCount} armed={scene.EnemyFire.ArmedUnitCount} " +
            $"unitFlags=0x{unit.Flags:X} runtime={unit.RuntimeId} dead={player.Dead}");
        Assert.True(player.Health < 0);
        Assert.True(player.RespawnTimer318 > 0);
        Assert.Equal(0, walker.Health);                // the gauge is empty, never negative

        // The walker is frozen while dead: no MechPlayer.Update, no input-driven movement.
        var frozen = (X: walker.X, Z: walker.Z);
        for (int i = 0; i < 10; i++) Frame();
        Assert.True(player.Dead);
        Assert.Equal(frozen.X, walker.X);
        Assert.Equal(frozen.Z, walker.Z);

        // func_80058714:138-166 restores the checkpoint and the full health after 1200 ticks.
        frames = 0;
        while (player.Dead && frames++ < 400) Frame();
        Assert.False(player.Dead,
            $"respawn timer={player.RespawnTimer318} health={player.Health} " +
            $"hits={scene.EnemyFire!.HitsDelivered} frames={frames}");
        Assert.Equal(player.MaxHealth, player.Health);
        Assert.Equal(player.MaxHealth, walker.Health);
        Assert.Equal(home.X, walker.X);
        Assert.Equal(home.Z, walker.Z);
        Assert.Equal(0, player.GaugeCharge);
    }
}
