using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Scene-level proof that <see cref="LevelScene"/> / <see cref="SkyCaptainSystem"/> wires
/// <see cref="EnemyEntity.LineOfSightProbe"/> (func_8004E80C mode 3, floors=false) and
/// <see cref="EnemyWorld.OnTraceMapRay"/> (func_8005F6E8, floors=true) to
/// <see cref="MechCollision.Ray"/> against the loaded retail <c>PA/CONFT.MIS</c> <see cref="LevelMap"/>.
/// </summary>
public class EnemyLineOfSightSceneTests
{
    private static string? FindConftMis()
    {
        string? disc = LevelTerrainTests.FindDisc();
        return disc is null ? null : ModulePath.Resolve(disc, "conft.mis");
    }

    /// <summary>
    /// Locates a real wall cell on <paramref name="map"/> where a horizontal ray at body height
    /// (+0x1000 above ground, y-up) hits terrain in one direction (+X) and is unobstructed for both
    /// floors=false (8004E80C.c:157) and floors=true (8005F6E8.c:398) in the opposite direction (-X).
    /// </summary>
    private static (Vec3 Origin, Vec3 Blocked, Vec3 Clear) FindWallAndClearSightline(LevelMap map)
    {
        const int span = 0x4000; // 2 grid cells (0x2000 each)
        int maxCellX = (map.Header.Height - 1) * 16 - 4;
        int maxCellZ = (map.Header.Width - 1) * 16 - 4;

        for (int cz = 8; cz < maxCellZ; cz += 2)
        {
            for (int cx = 8; cx < maxCellX; cx += 2)
            {
                int x = (cx << 13) + 0x1000;
                int z = (cz << 13) + 0x1000;
                int ground = LevelGround.Height(map, x, 0x100000, z, out _);
                int y = ground + 0x1000;
                var origin = new Vec3(x, y, z);
                var blocked = new Vec3(x + span, y, z);
                var clear = new Vec3(x - span, y, z);

                if (MechCollision.Ray(map, origin, blocked, floors: false, callerS1: 0).Result != 0
                    && MechCollision.Ray(map, origin, blocked, floors: true, callerS1: unchecked((int)0x80000000)).Result != 0
                    && MechCollision.Ray(map, origin, clear, floors: false, callerS1: 0).Result == 0
                    && MechCollision.Ray(map, origin, clear, floors: true, callerS1: unchecked((int)0x80000000)).Result == 0)
                {
                    return (origin, blocked, clear);
                }
            }
        }

        throw new InvalidOperationException("No wall + clear sightline pair found in LevelMap.");
    }

    private static LevelScene CreateArmedScene(string misPath, out SkyCaptainSystem sky, out Vec3 origin, out Vec3 blocked, out Vec3 clear)
    {
        var scene = new LevelScene(misPath);
        scene.EnableMechMovement();
        scene.EnableFiring();
        sky = Assert.IsType<SkyCaptainSystem>(scene.SkyCaptain);
        Assert.NotNull(sky.Drone);
        Assert.NotNull(sky.LineOfSightProbe);
        Assert.NotNull(sky.AttackEnv.OnTraceMapRay);

        (origin, blocked, clear) = FindWallAndClearSightline(scene.Map);

        // Stub only the out-of-scope weapon-fire sinks; keep scene-wired LOS and OnTraceMapRay intact.
        sky.AttackEnv.Random = () => 0u;
        sky.AttackEnv.TransformPartVertex = (_, actor) => (actor.Vec5CX, actor.Vec5CY, actor.Vec5CZ);
        sky.AttackEnv.OnSpawnDirectShot = _ => 1;
        sky.AttackEnv.OnPlaySound = (_, _, _, _) => { };
        return scene;
    }

    private static void ConfigureSlot(AttackSlot slot, byte searchMode)
    {
        slot.Definition = new AttackDefinition { Key = 1, Type = 1, W02 = 10, W06 = 1, W08 = 0, W0A = 0 };
        slot.Flags = 0x4000; // auto-acquire
        slot.TargetClass06 = 1; // filter by Mode16
        slot.Value08 = 1;       // player class byte
        slot.Mode07 = searchMode;
        slot.Range0A = 0x4000;  // maxDist = 0x40000 >> 0x4000 span
        slot.HalfAngle0C = unchecked((int)0xFFFF0000); // full 360-degree cone
        slot.HalfAngle10 = unchecked((int)0xFFFF0000);
        slot.RetryReload14 = 0;
        slot.Retry16 = 0;
        slot.AmmoGate20 = 10;
        slot.Playing22 = 1;
        slot.Target18 = null;
        slot.Target18Value = 0;
    }

    private static void PlaceDrone(SkyCaptainSystem sky, Vec3 pos)
    {
        var body = sky.Drone!.Body;
        body.FA = 0;
        body.Pos[0] = pos.X; body.Pos[1] = pos.Y; body.Pos[2] = pos.Z;
        body.DC[0] = pos.X; body.DC[1] = pos.Y; body.DC[2] = pos.Z;
        sky.Drone.Ai.Pos[0] = pos.X; sky.Drone.Ai.Pos[1] = pos.Y; sky.Drone.Ai.Pos[2] = pos.Z;
    }

    private static EnemyEntity CreatePlayerTarget(int id, Vec3 pos)
    {
        return new EnemyEntity
        {
            Id = id,
            Mode16 = 1,
            PositionX = pos.X, PositionY = pos.Y, PositionZ = pos.Z,
            Vec5CX = pos.X, Vec5CY = pos.Y, Vec5CZ = pos.Z,
        };
    }

    /// <summary>
    /// Mode-3 target search through <see cref="LevelScene.AdvanceSkyCaptain"/> on retail PA/CONFT.MIS:
    /// a wall between the Sky Captain and the player disqualifies the candidate so the enemy does not fire,
    /// while an unobstructed sightline acquires the target and fires.
    /// </summary>
    [Fact]
    public void ScenePath_Mode3LineOfSight_BlocksBehindWallAndFiresWithClearSight()
    {
        string? misPath = FindConftMis();
        if (misPath is null) Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
        if (misPath is null) return;

        var scene = CreateArmedScene(misPath, out var sky, out var origin, out var blocked, out var clear);
        var slot = sky.Drone!.Slot0;

        // (a) Wall between enemy (+0x5C) and player (+0x5C): Mode-3 LOS drops the candidate -> no shot.
        PlaceDrone(sky, origin);
        ConfigureSlot(slot, searchMode: 3);
        sky.AttackEnv.Entities.Clear();
        sky.AttackEnv.Entities.Add(CreatePlayerTarget(101, blocked));
        sky.AttackEnv.DirectShots.Clear();

        scene.AdvanceSkyCaptain(1);

        Assert.Null(slot.Target18);
        Assert.Empty(sky.AttackEnv.DirectShots);

        // (b) Positive control: clear sightline on the same retail map -> target acquired and shot fired.
        PlaceDrone(sky, origin);
        ConfigureSlot(slot, searchMode: 3);
        var visibleTarget = CreatePlayerTarget(102, clear);
        sky.AttackEnv.Entities.Clear();
        sky.AttackEnv.Entities.Add(visibleTarget);
        sky.AttackEnv.DirectShots.Clear();

        scene.AdvanceSkyCaptain(1);

        Assert.Same(visibleTarget, slot.Target18);
        Assert.Single(sky.AttackEnv.DirectShots);
    }

    /// <summary>
    /// Weapon-fire raycast (<c>func_8005F6E8</c>, <c>8005F6E8.c:398</c>, <c>floors=true</c>) through
    /// <see cref="LevelScene.AdvanceSkyCaptain"/> on retail PA/CONFT.MIS: using mode 1 (nearest without
    /// acquisition LOS), the enemy acquires the target behind a wall but <see cref="EnemyWorld.OnTraceMapRay"/>
    /// aborts the shot; with clear sightline the shot fires.
    /// </summary>
    [Fact]
    public void ScenePath_WeaponFireTraceMapRay_AbortsShotBehindWallAndFiresWithClearSight()
    {
        string? misPath = FindConftMis();
        if (misPath is null) Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
        if (misPath is null) return;

        var scene = CreateArmedScene(misPath, out var sky, out var origin, out var blocked, out var clear);
        var slot = sky.Drone!.Slot0;

        // (a) Mode 1 acquires target behind wall, then EnemyWeaponFire calls OnTraceMapRay -> Fail (no shot).
        PlaceDrone(sky, origin);
        ConfigureSlot(slot, searchMode: 1);
        var occludedTarget = CreatePlayerTarget(201, blocked);
        sky.AttackEnv.Entities.Clear();
        sky.AttackEnv.Entities.Add(occludedTarget);
        sky.AttackEnv.DirectShots.Clear();

        scene.AdvanceSkyCaptain(1);

        Assert.Same(occludedTarget, slot.Target18);
        Assert.Empty(sky.AttackEnv.DirectShots);

        // (b) Positive control: clear sightline -> OnTraceMapRay returns 0 -> shot fires.
        PlaceDrone(sky, origin);
        ConfigureSlot(slot, searchMode: 1);
        var visibleTarget = CreatePlayerTarget(202, clear);
        sky.AttackEnv.Entities.Clear();
        sky.AttackEnv.Entities.Add(visibleTarget);
        sky.AttackEnv.DirectShots.Clear();

        scene.AdvanceSkyCaptain(1);

        Assert.Same(visibleTarget, slot.Target18);
        Assert.Single(sky.AttackEnv.DirectShots);
    }
}
