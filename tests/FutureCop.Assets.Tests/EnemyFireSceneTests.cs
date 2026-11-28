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

    [ConftFact]
    public void FriendlyArmyDirectShotDamagesThePlayer()
    {
        var scene = CreateDirectContactScene(out var tankRecord, out int playerTeam);
        var shooter = SpawnTank(scene, tankRecord with { Id = 0x1800, Team = playerTeam });
        var player = scene.PlayerUnit!;
        var (x, y, z) = scene.MechWorld;
        short before = player.Health;
        Assert.Equal(playerTeam, shooter.Team);

        // gHandleDirectShotProjectile.c:127-143 does not veto teammates. The player accepts
        // message 11 and takes damage without a team test (gHandlePlayerEntityMsg.c:88-93,117-119;
        // gHandlePlayerHitMessage.c:82-95).
        QueueDirectShot(scene, shooter, x, y, z);
        TickDirectContacts(scene, playerTeam);

        Assert.Equal(before - 50, player.Health);
        Assert.Equal(1, scene.EnemyFire!.HitsDelivered);
    }

    [ConftFact]
    public void ArmyDirectShotDamagesAnEnemyTankButNotTheOwnerRecord()
    {
        var scene = CreateDirectContactScene(out var tankRecord, out int playerTeam);
        var shooter = SpawnTank(scene, tankRecord with { Id = 0x1800, Team = playerTeam });
        var ownerRecordMatch = SpawnTank(scene, tankRecord with { Id = shooter.RecordId | 0x4000, Team = playerTeam });
        var target = SpawnTank(scene, tankRecord with { Id = 0x1801, Team = 3 - playerTeam });
        Assert.NotEqual(shooter.Team, target.Team);
        var (x, y, z) = scene.MechWorld;
        x += 0x8000;
        foreach (var tank in new[] { shooter, ownerRecordMatch, target })
        {
            (tank.X, tank.Y, tank.Z) = (x, y, z);
            tank.Coord = tank.Coord!.Value with { X = x, Y = y, Z = z };
        }

        short shooterBefore = shooter.Health;
        short matchBefore = ownerRecordMatch.Health;
        short targetBefore = target.Health;
        short playerBefore = scene.PlayerUnit!.Health;

        // The shot rejects both the shooter and the matching 14-bit owner record
        // (gHandleDirectShotProjectile.c:127-143). Message 11 goes to the tank's base-damage path
        // (gHandleType5Entity.c:189-194,354-357; gResolveRailMoverContact.c:82-88,128-133;
        // gApplyEntityBaseDamage.c:87-103).
        QueueDirectShot(scene, shooter, x, y, z);
        TickDirectContacts(scene, playerTeam);

        Assert.Equal(targetBefore - 50, target.Health);
        Assert.Equal(shooterBefore, shooter.Health);
        Assert.Equal(matchBefore, ownerRecordMatch.Health);
        Assert.Equal(playerBefore, scene.PlayerUnit.Health);
        Assert.Equal(0, scene.EnemyFire!.ShotCount);
    }

    [ConftFact]
    public void ArmyDirectShotExcludesOnlySameKindProjectileContacts()
    {
        var scene = CreateDirectContactScene(out var tankRecord, out int playerTeam);
        var shooter = SpawnTank(scene, tankRecord with { Id = 0x1800, Team = playerTeam });
        var (x, y, z) = scene.MechWorld;
        x += 0x8000;
        var coord = new CollisionCoord(x, y, z, 0x400, 0x400, 0x400);
        var sameKind = new ProjectileContact(0x1801, 1, 0, x, z, y)
        {
            Health = 1, Coord = coord, Kind = (byte)playerTeam,
        };
        var otherKind = new ProjectileContact(0x1802, 1, 0, x, z, y)
        {
            Health = 1, Coord = coord, Kind = (byte)(3 - playerTeam),
        };
        scene.Entities!.Spawn(sameKind);
        scene.Entities.Spawn(otherKind);

        // gHandleDirectShotProjectile.c:141 excludes only type-2 contacts with matching +0x18.
        // A different-kind pair receives message 11 (800620D8.c:99-100), which sets +0x1C
        // to -1 for a normal direct shot (gHandleDirectShotProjectile.c:144-158).
        QueueDirectShot(scene, shooter, x, y, z);
        TickDirectContacts(scene, playerTeam);

        Assert.Equal(1, sameKind.Health);
        Assert.Equal(-1, otherKind.Health);
    }

    private static LevelScene CreateDirectContactScene(out EntityRecord tankRecord, out int playerTeam)
    {
        string mission = LevelSceneRetirementTests.FindMission()!;
        var scene = new LevelScene(mission);
        scene.EnableMechMovement();
        scene.EnableFiring();
        playerTeam = scene.Objectives!.Score.PlayerTeams[0];
        tankRecord = MissionEntities.Parse(File.ReadAllBytes(mission)).First(r =>
            r.Type == 5 && r.Container == EntityContainer.Csac && r.U8(0x24) == 6);
        return scene;
    }

    private static SpawnedCsacUnit SpawnTank(LevelScene scene, EntityRecord record)
    {
        var spawner = scene.CsacSpawner!;
        return Assert.IsType<SpawnedCsacUnit>(spawner.TrySpawn(spawner.Register(record)!, 1));
    }

    private static void QueueDirectShot(LevelScene scene, SpawnedCsacUnit shooter, int x, int y, int z)
    {
        var slot = Assert.IsType<AttackSlot>(shooter.AttackSlot);
        // Feed the flight queue at gSpawnDirectShot's boundary, using a one-tick path that ends
        // at the desired contact point (gSpawnDirectShot.c:90-103).
        scene.ArmyAttackWorld!.DirectShots.Add(new DirectSpawnArgs(slot,
            new AttackDefinition { Type = 2, W0C = 50, W12 = 0x100 },
            x - 0x1000, y, z, 0x10000, 0, 0, 0, 2, 0, 0, 0, 0, null)
        {
            ShooterRecordId = shooter.RecordId,
            Kind = (byte)shooter.Team,
        });
    }

    private static void TickDirectContacts(LevelScene scene, int playerTeam)
    {
        var (x, y, z) = scene.MechWorld;
        var world = scene.Entities!;
        world.CullActive(x, z);
        var fire = scene.EnemyFire!;
        fire.Sync(scene.CsacSpawner!.SpawnedUnits, x, y, z, playerTeam);
        fire.Tick(1, scene.PlayerUnit!, new CollisionCoord(x, y, z, 0x400, 0x400, 0x400), world);
    }

    [RetailFact]
    public void EnemyShotLosesArmourAndNegativeHealthRetiresThenRespawnsThePlayer()
    {
        string mission = LevelSceneRetirementTests.FindMission()!;
        var scene = new LevelScene(mission);
        scene.EnableMechMovement();
        // EnableFiring calls EnableObjectives, the playable entry's arming point.
        scene.EnableFiring();

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

        var home = (X: walker.X, Y: walker.Y, Z: walker.Z);
        Assert.Equal(home.X, player.Checkpoint.X);
        Assert.Equal(home.Y, player.Checkpoint.Y);
        Assert.Equal(home.Z, player.Checkpoint.Z);

        void Frame()
        {
            Pin(unit, scene.MechWorld);
            // The retail solo frame: message 6 reaches the rail handler, which steps the unit's
            // +0xA0 slot (800382B4.c:292-293) before the deferred update retires negative health.
            scene.AdvanceSoloFrame(RetailClock.TicksPerFrame, 1, 0, default);
        }

        // Acceptance 1: the armour word the HUD reads drops by the weapon damage of key 6.
        int frames = 0;
        while (player.Health == start && frames++ < 400) Frame();
        Assert.True(player.Health < start, "no enemy shot reached the player through AdvanceSoloFrame");
        Assert.NotNull(scene.EnemyFire);
        Assert.Equal(50, start - player.Health);
        // The walker's +0x1E is the fixed fall-damage scale MechWalker:886 multiplies; retail only
        // writes the entity's +0x1C on damage (80058210.c:101-102), so the scale stays at spawn value.
        Assert.Equal(player.MaxHealth, walker.Health);

        // Acceptance 2: retail retires at health < 0 (8004CE04.c:59-60 -> message 4), not at zero.
        frames = 0;
        while (!player.Dead && frames++ < 2000) Frame();
        Assert.True(player.Dead,
            $"survived: health={player.Health} hits={scene.EnemyFire!.HitsDelivered} " +
            $"shots={scene.EnemyFire.ShotCount} armed={scene.EnemyFire.ArmedUnitCount} " +
            $"unitFlags=0x{unit.Flags:X} runtime={unit.RuntimeId} dead={player.Dead} " +
            $"slot: burst={unit.AttackSlot!.Burst26} acc={unit.AttackSlot.FrameAcc24} " +
            $"playing={unit.AttackSlot.Playing22} target={(unit.AttackSlot.Target18 is null ? "null" : "set")} " +
            $"retry={unit.AttackSlot.Retry16} ammo={unit.AttackSlot.AmmoGate20}");
        Assert.True(player.Health < 0);
        Assert.True(player.RespawnTimer318 > 0);
        Assert.Equal(player.MaxHealth, walker.Health);   // +0x1E is still the fall-damage scale

        // The walker is frozen while dead: no MechPlayer.Update, no input-driven movement.
        var frozen = (X: walker.X, Z: walker.Z);
        for (int i = 0; i < 10; i++) Frame();
        Assert.True(player.Dead);
        Assert.Equal(frozen.X, walker.X);
        Assert.Equal(frozen.Z, walker.Z);

        // The checkpoint covers the full Vec3 (80058714.c:138) and the respawn refills slots 0..2
        // (80058714.c:169-171): move the frozen walker off the checkpoint height and spend all three slots.
        walker.Y = unchecked(home.Y + 0x800);
        var slots = scene.Weapons!.Slots;
        slots.Ammo[0] = 3;
        slots.Ammo[1] = 0;
        slots.Ammo[2] = 0;

        // func_80058714:138-171 restores the checkpoint and the full health after 1200 ticks.
        frames = 0;
        while (player.Dead && frames++ < 400) Frame();
        Assert.False(player.Dead,
            $"respawn timer={player.RespawnTimer318} health={player.Health} " +
            $"hits={scene.EnemyFire!.HitsDelivered} frames={frames}");
        Assert.Equal(player.MaxHealth, player.Health);
        Assert.Equal(player.MaxHealth, walker.Health);
        Assert.Equal(home.X, walker.X);
        Assert.Equal(home.Y, walker.Y);   // the Y axis comes back from the checkpoint too
        Assert.Equal(home.Z, walker.Z);
        Assert.True(slots.Ammo[0] > 3, $"ammo={string.Join(",", slots.Ammo)}");
        Assert.True(slots.Ammo[1] > 0, $"ammo={string.Join(",", slots.Ammo)}");
        Assert.True(slots.Ammo[2] > 0, $"ammo={string.Join(",", slots.Ammo)}");
        Assert.Equal(0, player.GaugeCharge);
    }
}
