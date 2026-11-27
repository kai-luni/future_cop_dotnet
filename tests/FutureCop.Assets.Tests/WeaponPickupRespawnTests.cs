using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Regression tests for the Precinct Assault item lifecycle (todo 35):
/// in retail CONFT.MIS all 11 type-0x10 pickups are Csac spawners (tSAC delay -3000 -> 15000 ticks
/// for 100..103, -6000 -> 30000 ticks for 284..298, maxConcurrent = 1, remainingCount = -1).
/// Collecting an item calls gRetireEntity (8002FF6C.c:78 -> 8004D958.c:48-60), gSweepRetiredEntities
/// calls gRecordSpawnedUnitRetirement (8004D598.c:51-52 -> 80061020.c:126), and
/// gUpdateRegisteredSpawners counts down tSAC +0x14 and respawns via gTrySpawnRegisteredUnit
/// (800616A0.c:70-76 -> 8006125C.c:41-78 -> 8004D4F8.c:48-61 -> 8002FA20.c:78-122).
/// </summary>
public class WeaponPickupRespawnTests
{
    private static LevelScene CreatePrecinctAssaultScene()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!) { EnableCsacSpawner = true };
        scene.EnableMechMovement();
        scene.EnableFiring();
        return scene;
    }

    [ConftFact]
    public void PrecinctAssaultPickupDisappearsRespawnsAndPaysASecondTime()
    {
        var scene = CreatePrecinctAssaultScene();
        var pickups = scene.Pickups!;
        var spawner = scene.CsacSpawner!;
        var walker = scene.Walker!;
        var slots = scene.Weapons!.Slots;
        var camera = new LevelCamera(30.0, 35.0, 0x1C00);

        Assert.Equal(11, pickups.RemainingCount);

        var first = Assert.Single(pickups.Items, i => i.SectionId == 100);
        var slot = Assert.Single(spawner.Slots, s => s.Record.Id == 100);
        Assert.Equal(1, slot.TotalSpawned);
        Assert.Equal(15000, slot.RespawnDelay); // tSAC -3000 * 5 (80060E74.c:129-131)

        int expectedX = first.X, expectedY = first.Y, expectedZ = first.Z, expectedModel = first.ModelId;
        Assert.True(expectedModel > 0);

        // Before pickup: exactly one model drawn for section 100, and zero generic Csac primitives.
        walker.X = expectedX;
        walker.Y = expectedY;
        walker.Z = expectedZ;
        scene.AttachWalker(walker);
        scene.Render(camera);
        Assert.Equal(11, scene.DrawnPickupPlacements.Count);
        Assert.Single(scene.DrawnPickupPlacements, p => p.X == expectedX && p.Y == expectedY && p.Z == expectedZ && p.Model?.Id == expectedModel);
        Assert.Equal(0, scene.SpawnedUnitPrimitives);

        // First pickup: pays out, disappears cleanly, and releases the Csac spawner slot.
        slots.Ammo[0] = 1;
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.Equal(7500, slots.Ammo[0]);
        Assert.Equal(1, pickups.Payouts);
        Assert.Equal(1, pickups.CollectedCount);
        Assert.Equal(10, pickups.RemainingCount);
        Assert.DoesNotContain(pickups.Items, i => i.SectionId == 100);
        Assert.Equal(0, slot.TotalSpawned);

        scene.Render(camera);
        Assert.Equal(10, scene.DrawnPickupPlacements.Count);
        Assert.DoesNotContain(scene.DrawnPickupPlacements, p => p.X == expectedX && p.Z == expectedZ);
        Assert.Equal(0, scene.SpawnedUnitPrimitives);

        // Move the walker away while waiting for the respawn countdown.
        walker.X = expectedX - first.HalfX - 0x4000;
        scene.AttachWalker(walker);

        // One tick short of underflow (timer reaches 0, still >= 0 per 800616A0.c:72): still absent.
        Assert.Equal(15000, slot.Timer);
        scene.AdvanceSkyCaptain(slot.Timer);
        Assert.Equal(0, slot.TotalSpawned);
        Assert.DoesNotContain(pickups.Items, i => i.SectionId == 100);
        scene.Render(camera);
        Assert.Equal(10, scene.DrawnPickupPlacements.Count);
        Assert.Equal(0, scene.SpawnedUnitPrimitives);

        // Next tick underflows timer (< 0): respawns with identical placement, single model, collectable again.
        scene.AdvanceSkyCaptain(1);
        Assert.Equal(1, slot.TotalSpawned);
        var respawned = Assert.Single(pickups.Items, i => i.SectionId == 100);
        Assert.NotSame(first, respawned);
        Assert.False(respawned.Collected);
        Assert.Equal((expectedX, expectedY, expectedZ, expectedModel),
            (respawned.X, respawned.Y, respawned.Z, respawned.ModelId));

        scene.Render(camera);
        Assert.Equal(11, scene.DrawnPickupPlacements.Count);
        Assert.Single(scene.DrawnPickupPlacements, p => p.X == expectedX && p.Y == expectedY && p.Z == expectedZ && p.Model?.Id == expectedModel);
        Assert.Equal(0, scene.SpawnedUnitPrimitives);

        // Second pickup on the respawned item: pays out a second time and disappears cleanly again.
        walker.X = expectedX;
        walker.Y = expectedY;
        walker.Z = expectedZ;
        scene.AttachWalker(walker);
        slots.Ammo[0] = 1;
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.Equal(7500, slots.Ammo[0]);
        Assert.Equal(2, pickups.Payouts);
        Assert.Equal(2, pickups.CollectedCount);
        Assert.Equal(10, pickups.RemainingCount);
        Assert.Equal(0, slot.TotalSpawned);

        scene.Render(camera);
        Assert.Equal(10, scene.DrawnPickupPlacements.Count);
        Assert.DoesNotContain(scene.DrawnPickupPlacements, p => p.X == expectedX && p.Z == expectedZ);
        Assert.Equal(0, scene.SpawnedUnitPrimitives);
    }

    [ConftFact]
    public void PrecinctAssaultPickupRespawnTimingAndVisibilityMatchRetailTsacDelays()
    {
        var scene = CreatePrecinctAssaultScene();
        var pickups = scene.Pickups!;
        var spawner = scene.CsacSpawner!;
        var walker = scene.Walker!;
        var slots = scene.Weapons!.Slots;
        var camera = new LevelCamera(30.0, 35.0, 0x1C00);

        Assert.Equal(11, pickups.RemainingCount);

        // Verify both retail CONFT tSAC delay groups: 100..103 = 15000 ticks, 284..298 = 30000 ticks.
        var slot103 = Assert.Single(spawner.Slots, s => s.Record.Id == 103); // health (0x03020000)
        var slot284 = Assert.Single(spawner.Slots, s => s.Record.Id == 284); // reserve slot 1 (0x03000020)
        Assert.Equal(15000, slot103.RespawnDelay);
        Assert.Equal(30000, slot284.RespawnDelay);

        var healthItem = Assert.Single(pickups.Items, i => i.SectionId == 103);
        var reserveItem = Assert.Single(pickups.Items, i => i.SectionId == 284);

        // Collect health pickup 103 and reserve pickup 284.
        walker.Health = 1234;
        (walker.X, walker.Y, walker.Z) = (healthItem.X, healthItem.Y, healthItem.Z);
        scene.AttachWalker(walker);
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.Equal(0x7F00, walker.Health);
        Assert.Equal(0, slot103.TotalSpawned);

        slots.Reserve[1] = 0;
        (walker.X, walker.Y, walker.Z) = (reserveItem.X, reserveItem.Y, reserveItem.Z);
        scene.AttachWalker(walker);
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.True(slots.Reserve[1] > 0);
        Assert.Equal(0, slot284.TotalSpawned);
        Assert.Equal(9, pickups.RemainingCount);

        // Stand over section 103 mid-cooldown: must stay invisible, never flicker, and never pay early.
        (walker.X, walker.Y, walker.Z) = (healthItem.X, healthItem.Y, healthItem.Z);
        scene.AttachWalker(walker);
        walker.Health = 1000;
        int wait103 = slot103.Timer;
        int step = 3000;
        int elapsed = 0;
        while (elapsed + step <= wait103)
        {
            scene.AdvanceSkyCaptain(step);
            elapsed += step;
            scene.AdvanceWalker(1, 0, AnalogPad.Digital);
            scene.Render(camera);
            Assert.Equal(1000, walker.Health);
            Assert.Equal(9, pickups.RemainingCount);
            Assert.Equal(9, scene.DrawnPickupPlacements.Count);
            Assert.Equal(0, scene.SpawnedUnitPrimitives);
        }

        // Advance to exact timer == 0 for 103: still absent.
        int tail103 = wait103 - elapsed;
        if (tail103 > 0) scene.AdvanceSkyCaptain(tail103);
        Assert.Equal(0, slot103.Timer);
        Assert.DoesNotContain(pickups.Items, i => i.SectionId == 103);
        Assert.DoesNotContain(pickups.Items, i => i.SectionId == 284);

        // Move off 103 and advance 1 tick: 103 respawns on the exact underflow tick, 284 stays absent.
        walker.X = healthItem.X - healthItem.HalfX - 0x4000;
        scene.AttachWalker(walker);
        scene.AdvanceSkyCaptain(1);
        Assert.Equal(1, slot103.TotalSpawned);
        Assert.Equal(0, slot284.TotalSpawned);
        Assert.Equal(10, pickups.RemainingCount);
        var respawned103 = Assert.Single(pickups.Items, i => i.SectionId == 103);
        Assert.Equal((healthItem.X, healthItem.Y, healthItem.Z), (respawned103.X, respawned103.Y, respawned103.Z));
        Assert.Equal(14999, slot103.Timer); // -1 + 15000 (800616A0.c:73)

        // Collect the respawned health pickup a second time and verify second payout.
        (walker.X, walker.Y, walker.Z) = (respawned103.X, respawned103.Y, respawned103.Z);
        scene.AttachWalker(walker);
        walker.Health = 2000;
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.Equal(0x7F00, walker.Health);
        Assert.Equal(0, slot103.TotalSpawned);
        Assert.Equal(9, pickups.RemainingCount);

        // Advance to exact timer == 0 for both 284 (first 30000-tick cycle) and 103 (second 15000-tick
        // cycle: 14999 ticks left after the -1 + 15000 wraparound): both must still be absent at 0.
        walker.X = reserveItem.X - reserveItem.HalfX - 0x4000;
        scene.AttachWalker(walker);
        int rem284 = slot284.Timer;
        Assert.Equal(14999, rem284);
        Assert.Equal(14999, slot103.Timer);
        scene.AdvanceSkyCaptain(rem284);
        Assert.Equal(0, slot284.Timer);
        Assert.Equal(0, slot103.Timer);
        Assert.DoesNotContain(pickups.Items, i => i.SectionId == 284);
        Assert.DoesNotContain(pickups.Items, i => i.SectionId == 103);

        // 1 more tick underflows both timers (< 0): both 284 (first respawn) and 103 (second respawn)
        // appear at their exact original ground-snapped positions with zero duplicate Csac models.
        scene.AdvanceSkyCaptain(1);
        Assert.Equal(1, slot284.TotalSpawned);
        Assert.Equal(1, slot103.TotalSpawned);
        var respawned284 = Assert.Single(pickups.Items, i => i.SectionId == 284);
        var respawned103Second = Assert.Single(pickups.Items, i => i.SectionId == 103);
        Assert.Equal((reserveItem.X, reserveItem.Y, reserveItem.Z), (respawned284.X, respawned284.Y, respawned284.Z));
        Assert.Equal((healthItem.X, healthItem.Y, healthItem.Z), (respawned103Second.X, respawned103Second.Y, respawned103Second.Z));
        scene.Render(camera);
        Assert.Equal(11, scene.DrawnPickupPlacements.Count);
        Assert.Equal(0, scene.SpawnedUnitPrimitives);
    }

    [ConftFact]
    public void PrecinctAssaultUncollectedPickupNeverSpawnsDuplicateModelAfterDelay()
    {
        var scene = CreatePrecinctAssaultScene();
        var pickups = scene.Pickups!;
        var item100 = Assert.Single(pickups.Items, i => i.SectionId == 100);
        var slot100 = Assert.Single(scene.CsacSpawner!.Slots, s => s.Record.Id == 100);

        // Advance well past the 15000-tick tSAC delay of IDs 100..103 while item 100 is still
        // uncollected. Because the initial pickup already occupies MaxConcurrent = 1 (800616A0.c:70),
        // the spawner must not tick down or spawn a second model at the same (X, Z).
        scene.AdvanceSkyCaptain(slot100.RespawnDelay + 100);

        Assert.Equal(11, pickups.RemainingCount);
        Assert.Equal(1, slot100.TotalSpawned);
        Assert.DoesNotContain(scene.Entities!.Units, u => u is SpawnedCsacUnit { EntityType: WeaponPickups.PlacementPayloadType });

        scene.Walker!.X = item100.X;
        scene.Walker.Y = item100.Y;
        scene.Walker.Z = item100.Z;
        scene.AttachWalker(scene.Walker);

        foreach (double yaw in new[] { 0.0, 90.0, 180.0, 270.0 })
        {
            scene.Render(new LevelCamera(yaw, 35.0, 0x1C00));
            var drawn = scene.DrawnPickupPlacements;
            Assert.Equal(11, drawn.Count);
            Assert.Equal(11, drawn.Select(p => (p.X, p.Y, p.Z)).Distinct().Count());
            Assert.All(drawn, p => Assert.NotNull(p.Model));
            Assert.True(scene.PickupPrimitives > 0, "expected pickup 100 to draw primitives");
            // Type-0x10 pickups must never be drawn a second time through the generic Csac unit pass.
            Assert.Equal(0, scene.SpawnedUnitPrimitives);
        }
    }
}
