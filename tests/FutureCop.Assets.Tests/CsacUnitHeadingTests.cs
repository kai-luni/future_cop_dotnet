using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Drawing-path tests for the heading of live Csac units. Retail orients a unit model through
/// gOrientEntityBasis (func_8001A8E8): ground movers take the fixed-yaw branch from the path
/// direction at +0x140 (8001A8E8.c:118-126), type-9 flyers take the three-axis body basis that
/// gUpdateHoverEnemy builds with gBuildBasisZFromPair at +0x2C (800324E0.c:378-384). Before, every
/// spawned unit was placed with the identity rotation, so a tank or helicopter faced the same way at
/// every heading.
/// </summary>
public sealed class CsacUnitHeadingTests
{
    /// <summary>CONFT.MIS slot of the team-2 type-0x1C vehicle (Csac record 57, model 37).</summary>
    private const int TankSlot = 57;

    /// <summary>CONFT.MIS slot of the team-2 type-9 flyer (Csac record 6, model 42).</summary>
    private const int FlyerSlot = 6;

    /// <summary>Spawns one unit through the retail spawner and anchors the chase camera on it.</summary>
    private static (LevelScene Scene, SpawnedCsacUnit Unit) SpawnRendered(int recordId)
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!);
        scene.EnableObjectives();
        scene.EnableCsacSpawner = true;
        scene.EnableMechMovement();
        Assert.True(scene.CsacSpawner!.ApplyCommandById(recordId, CsacUnitSpawner.CommandQueueBurst, 0));
        for (int i = 0; i < 4 && !scene.CsacSpawner.SpawnedUnits.Any(u => u.SourceSlot.Record.Id == recordId); i++)
            scene.AdvanceSkyCaptain(RetailClock.LocomotionTicks);

        var unit = Assert.Single(scene.CsacSpawner.SpawnedUnits.Where(u => u.SourceSlot.Record.Id == recordId));
        scene.Walker!.X = unit.X;
        scene.Walker.Y = unit.Y;
        scene.Walker.Z = unit.Z;
        scene.AttachWalker(scene.Walker);
        return (scene, unit);
    }

    /// <summary>
    /// Renders with the chase camera in front of the unit (yaw 180). The default yaw puts the
    /// camera behind the player mech, which hides a ground unit standing at the same spot.
    /// </summary>
    private static byte[] Render(LevelScene scene)
    {
        scene.Render(new LevelCamera(180.0, 20.0, 0x1200));
        return (byte[])scene.Rgb.Clone();
    }

    [ConftFact]
    public void TankModelTurnsWithItsRailHeading()
    {
        var (scene, unit) = SpawnRendered(TankSlot);
        Assert.Equal(EntityHandlerKind.Type28MultiTurret, unit.Handler);

        // +X heading.
        unit.DirX = 0x10000;
        unit.DirZ = 0;
        byte[] facingPlusX = Render(scene);

        // The model is on screen at all, so a difference below is a rotation and not a missing unit.
        Assert.True(scene.SpawnedCsacUnitPrimitives > 0, "spawned tank produced no rendered faces");

        // Same heading again: the frame must repeat, no hidden per-frame state in the matrix.
        Assert.True(facingPlusX.AsSpan().SequenceEqual(Render(scene)),
            "two renders of the same heading differ");

        // -X heading (a 180 degree turn on the rail path).
        unit.DirX = -0x10000;
        Assert.False(facingPlusX.AsSpan().SequenceEqual(Render(scene)),
            "the tank kept its old orientation after turning around");

        // +Z heading: a quarter turn must differ from both other headings as well.
        unit.DirX = 0;
        unit.DirZ = 0x10000;
        byte[] facingPlusZ = Render(scene);
        Assert.False(facingPlusX.AsSpan().SequenceEqual(facingPlusZ),
            "the tank ignored the quarter turn to +Z");
    }

    [ConftFact]
    public void FlyerModelFollowsFlightBasisAndNosePitch()
    {
        var (scene, unit) = SpawnRendered(FlyerSlot);
        Assert.Equal(EntityHandlerKind.TumblingFlyer, unit.Handler);
        var hover = Assert.IsType<HoverEnemy>(unit.HoverMover);

        // Level flight along +Z: row 2 = flight vector, rows 0/1 = gBuildBasisZFromPair's horizontal pair.
        hover.BasisRows[0][0] = 0x10000; hover.BasisRows[0][1] = 0; hover.BasisRows[0][2] = 0;
        hover.BasisRows[1][0] = 0; hover.BasisRows[1][1] = 0x10000; hover.BasisRows[1][2] = 0;
        hover.BasisRows[2][0] = 0; hover.BasisRows[2][1] = 0; hover.BasisRows[2][2] = 0x10000;
        byte[] level = Render(scene);
        Assert.True(scene.SpawnedCsacUnitPrimitives > 0, "spawned flyer produced no rendered faces");
        Assert.True(level.AsSpan().SequenceEqual(Render(scene)), "two renders of the same basis differ");

        // Diving: row 2 keeps the y component of the flight vector, which is the nose pitch.
        int cos = 0xE000, sin = 0x6900; // ~30 degrees nose down, |row2| ~= 0x10000
        hover.BasisRows[0][0] = 0x10000; hover.BasisRows[0][1] = 0; hover.BasisRows[0][2] = 0;
        hover.BasisRows[1][0] = 0; hover.BasisRows[1][1] = cos; hover.BasisRows[1][2] = sin;
        hover.BasisRows[2][0] = 0; hover.BasisRows[2][1] = -sin; hover.BasisRows[2][2] = cos;
        Assert.False(level.AsSpan().SequenceEqual(Render(scene)),
            "the flyer ignored the nose-down pitch of its flight basis");

        // Yaw only, same pitch rows: the heading must still turn the model.
        hover.BasisRows[1][0] = 0; hover.BasisRows[1][1] = cos; hover.BasisRows[1][2] = sin;
        hover.BasisRows[2][0] = sin; hover.BasisRows[2][1] = -sin; hover.BasisRows[2][2] = cos;
        byte[] yawed = Render(scene);
        hover.BasisRows[2][0] = -sin;
        Assert.False(yawed.AsSpan().SequenceEqual(Render(scene)),
            "the flyer ignored the yaw of its flight basis");
    }
}
