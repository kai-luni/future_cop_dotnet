using FcMainMenu;
using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The playable path the review found dead: MenuFlow starts the level through EnableMechMovement +
/// EnableFiring only, so EnableObjectives has to build the hostile fire path itself
/// (LevelScene.cs EnableObjectives -> <see cref="LevelScene.EnemyFire"/>). The test never calls
/// <see cref="LevelScene.EnableEnemyFire"/>; it enters through the same calls the menu makes and
/// advances frames with <see cref="MenuFlow.Advance"/>.
/// </summary>
public class PlayableEnemyFireTests
{
    private const uint Down = 116, Enter = 36;

    private static string? FrontEndPath()
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc == null ? null : Path.Combine(disc, "CW", "FE.MIS");
        if (path == null || !File.Exists(path))
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
            return null;
        }
        return path;
    }

    /// <summary>Main menu -> Precinct Assault single player -> setup -> zone list -> loadout.</summary>
    private static void ToLoadout(MenuFlow flow)
    {
        flow.HandleKey(Down); flow.HandleKey(Down);            // 0 -> 1 -> 3 (2 is disabled)
        flow.HandleKey(Enter);
        flow.HandleKey(Enter);
        flow.HandleKey(Enter);                                 // activate the zone list
        flow.HandleKey(Enter);
    }

    [RetailFact]
    public void PlayableEntryArmsEnemyFireAndTheHudReadsTheEntityHealthWord()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;

        var flow = new MenuFlow(fe, SceneKind.Level, TextWriter.Null, TextWriter.Null,
            enableLevelMovement: true);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        Assert.Equal(MenuScreen.Scene, flow.Screen);
        var level = Assert.IsType<LevelScene>(flow.Level);

        var player = level.PlayerUnit!;
        var spawner = level.CsacSpawner!;
        int playerTeam = level.Objectives!.Score.PlayerTeams[0];
        // The same type-5 record EnemyFireSceneTests uses: attack key 6, player class, damage 50.
        var record = MissionEntities.Parse(File.ReadAllBytes(level.MissionPath)).First(r =>
            r.Type == 5 && r.Container == EntityContainer.Csac &&
            r.U8(0x24) == 6 && r.U8(0x29) == 4 && r.U16(0x2A) == playerTeam);
        var unit = spawner.TrySpawn(spawner.Register(record)!, 1);
        Assert.NotNull(unit);

        short start = player.Health;
        int frames = 0;
        while (player.Health == start && frames++ < 400)
        {
            // Hold the unit in firing position and facing the player; unit AI steering is not ported.
            unit.X = level.MechWorld.X + 0x400;
            unit.Y = level.MechWorld.Y;
            unit.Z = level.MechWorld.Z;
            unit.DirX = -0x10000;
            unit.DirZ = 0;
            flow.Advance(1.0 / 60.0);
        }
        // Reading EnemyFire lazily creates it, so wait until after exercising the menu frame path.
        var fire = Assert.IsType<LevelEnemyFire>(level.EnemyFire);
        Assert.True(player.Health < start,
            $"no enemy shot reached the player through MenuFlow.Advance: health={player.Health} " +
            $"hits={fire.HitsDelivered} shots={fire.ShotCount} armed={fire.ArmedUnitCount}");

        // HudDraw.DrawPlayerGauge scales the entity's +0x1C word (80058210.c:101-102), not +0x1E.
        Assert.Equal(player.Health, flow.BuildHud().Health);
        Assert.Equal(player.MaxHealth, level.Walker!.Health);

        // One slot per unit: the army world's queue is the only one, and the delivery drained it.
        Assert.Empty(level.ArmyAttackWorld!.DirectShots);
    }

    /// <summary>
    /// +0x1E is the s16 scale the landing damage multiplies (MechBody.cs:36, MechWalker.cs:886).
    /// Retail's damage path writes only the entity's +0x1C (80058210.c:101-102), so taking armour
    /// damage must not shrink the scale and the HUD must read the entity word.
    /// </summary>
    [RetailFact]
    public void ArmourDamageKeepsTheWalkerFallDamageScale()
    {
        string mission = LevelSceneRetirementTests.FindMission()!;
        var scene = new LevelScene(mission);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var player = scene.PlayerUnit!;
        var walker = scene.Walker!;
        var world = new MechWorld(scene.Map);

        Assert.Equal(player.MaxHealth, walker.Health);
        int before = MechWalker.FallDamage(walker, world, 0x400000);
        Assert.True(before > 0);

        // The hit path leaves the entity's +0x1C at 450; run frames, which used to copy it into +0x1E.
        player.Health = 450;
        scene.AdvanceSoloFrame(1, 1, 0, default);
        scene.AdvanceSoloFrame(1, 1, 0, default);

        Assert.Equal((short)450, player.Health);
        Assert.Equal(player.MaxHealth, walker.Health);
        Assert.Equal(before, MechWalker.FallDamage(walker, world, 0x400000));
    }
}
