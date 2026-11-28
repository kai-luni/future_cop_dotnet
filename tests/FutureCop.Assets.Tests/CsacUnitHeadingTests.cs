using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Drawing-path tests for the heading of live Csac units. Retail orients a unit model through
/// gOrientEntityBasis (func_80037EB4, src/game/renderer-scene/gOrientEntityBasis.c): ground movers
/// take the fixed-yaw branch from the path direction at +0x140 (:75-90), type-9 flyers take the
/// three-axis body basis that gUpdateHoverEnemy builds with gBuildBasisZFromPair at +0x2C
/// (800324E0.c:382). Before, every spawned unit was placed with the identity rotation, so a tank or
/// helicopter faced the same way at every heading.
///
/// The heading tests compare the matrix rows themselves, not only rendered images: a sign error or a
/// transposed row pair changes the basis while still turning the model, which an image comparison
/// cannot tell apart from the correct basis.
/// </summary>
public sealed class CsacUnitHeadingTests
{
    /// <summary>CONFT.MIS slot of the team-2 type-0x1C vehicle (Csac record 57, model 37).</summary>
    private const int TankSlot = 57;

    /// <summary>CONFT.MIS slot of the team-2 type-9 flyer (Csac record 6, model 42).</summary>
    private const int FlyerSlot = 6;

    private const int One = 0x10000;

    private static readonly int[] Identity =
    {
        One, 0, 0,
        0, One, 0,
        0, 0, One,
    };

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
    /// A type-1 unit of a throwaway spawner: no hover and no rail mover, so IsMobile stays false even
    /// though the constructor seeds DirX/DirZ with (0x10000, 0) - the state a failed rail-mover
    /// initialization leaves behind.
    /// </summary>
    private static SpawnedCsacUnit SpawnStatic()
    {
        var record = new EntityRecord(9101, 1, 0x100000, 0x10000, 0x200000, 0, 0x100, 2,
            EntityContainer.Csac, 0, 0, new byte[0x54],
            new[] { 0, 0, 0 }, new[] { new ResourceReference("Cobj", 30) });
        var spawner = new CsacUnitSpawner();
        var slot = spawner.Register(record,
            new CsacSpawnerParameters(RespawnDelayRaw: 0, MaxConcurrent: 1, InitialFlags: 0, InitialCount: 1));
        Assert.NotNull(slot);
        var unit = spawner.TrySpawn(slot!, spawnMode: 1);
        Assert.NotNull(unit);
        Assert.False(unit!.IsMobile);
        return unit;
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
    public void TankBasisFollowsTheFixedYawBranchOfGorientEntityBasis()
    {
        var (_, unit) = SpawnRendered(TankSlot);
        Assert.Equal(EntityHandlerKind.Type28MultiTurret, unit.Handler);
        Assert.True(unit.IsMobile);

        // +X heading: row0 = (dz, 0, -dx), row1 = (0, 1, 0), row2 = (dx, 0, dz) with (dx, dz) =
        // (0x10000, 0). A transposition of rows 0 and 2 or a sign error on -dx changes this array.
        unit.DirX = One;
        unit.DirZ = 0;
        Assert.Equal(new[] { 0, 0, -One, 0, One, 0, One, 0, 0 }, LevelScene.UnitBasisOf(unit));

        // -X heading: row0 = (0, 0, 0x10000), row2 = (-0x10000, 0, 0).
        unit.DirX = -One;
        Assert.Equal(new[] { 0, 0, One, 0, One, 0, -One, 0, 0 }, LevelScene.UnitBasisOf(unit));

        // +Z heading: row0 = (0x10000, 0, 0), row2 = (0, 0, 0x10000).
        unit.DirX = 0;
        unit.DirZ = One;
        Assert.Equal(new[] { One, 0, 0, 0, One, 0, 0, 0, One }, LevelScene.UnitBasisOf(unit));

        // Diagonal heading: row0 = (dz, 0, -dx), row2 = (dx, 0, dz) both off the axes.
        unit.DirX = One;
        Assert.Equal(new[] { One, 0, -One, 0, One, 0, One, 0, One }, LevelScene.UnitBasisOf(unit));
    }

    [ConftFact]
    public void CyclicHeadingRendersDistinctTankFrames()
    {
        var (scene, unit) = SpawnRendered(TankSlot);
        Assert.Equal(EntityHandlerKind.Type28MultiTurret, unit.Handler);

        // The model is on screen at all, so a difference below is a rotation and not a missing unit.
        unit.DirX = One;
        unit.DirZ = 0;
        byte[] facingPlusX = Render(scene);
        Assert.True(scene.SpawnedCsacUnitPrimitives > 0, "spawned tank produced no rendered faces");

        // Same heading again: the frame must repeat, no hidden per-frame state in the matrix.
        Assert.True(facingPlusX.AsSpan().SequenceEqual(Render(scene)),
            "two renders of the same heading differ");

        // -X heading (a 180 degree turn on the rail path).
        unit.DirX = -One;
        Assert.False(facingPlusX.AsSpan().SequenceEqual(Render(scene)),
            "the tank kept its old orientation after turning around");

        // +Z heading: a quarter turn must differ from both other headings as well.
        unit.DirX = 0;
        unit.DirZ = One;
        byte[] facingPlusZ = Render(scene);
        Assert.False(facingPlusX.AsSpan().SequenceEqual(facingPlusZ),
            "the tank ignored the quarter turn to +Z");
    }

    [ConftFact]
    public void AUnitWithoutAMoverKeepsTheIdentityBasisEvenWithAHeadingWord()
    {
        var unit = SpawnStatic();
        Assert.Null(unit.HoverMover);
        Assert.Null(unit.RailMover);
        // The spawner default: a heading word is present although no mover ever updates it.
        Assert.Equal(One, unit.DirX);
        Assert.Equal(0, unit.DirZ);

        // gInitObjectFromDef places such a unit unrotated. A DirX/DirZ predicate would apply the
        // fixed-yaw branch here and turn the model 90 degrees around Y for a +X "heading".
        Assert.Equal(Identity, LevelScene.UnitBasisOf(unit));
    }

    [ConftFact]
    public void FlyerBasisIsHandedOverUnchanged()
    {
        var (scene, unit) = SpawnRendered(FlyerSlot);
        Assert.Equal(EntityHandlerKind.TumblingFlyer, unit.Handler);
        var hover = Assert.IsType<HoverEnemy>(unit.HoverMover);

        // Level flight along +Z: row 2 = flight vector, rows 0/1 = gBuildBasisZFromPair's horizontal pair.
        hover.BasisRows[0][0] = One; hover.BasisRows[0][1] = 0; hover.BasisRows[0][2] = 0;
        hover.BasisRows[1][0] = 0; hover.BasisRows[1][1] = One; hover.BasisRows[1][2] = 0;
        hover.BasisRows[2][0] = 0; hover.BasisRows[2][1] = 0; hover.BasisRows[2][2] = One;
        Assert.Equal(Identity, LevelScene.UnitBasisOf(unit));

        // Diving ~30 degrees nose down: row 2 keeps the y component, the matrix is copied verbatim.
        int cos = 0xE000, sin = 0x6900;
        hover.BasisRows[0][0] = One; hover.BasisRows[0][1] = 0; hover.BasisRows[0][2] = 0;
        hover.BasisRows[1][0] = 0; hover.BasisRows[1][1] = cos; hover.BasisRows[1][2] = sin;
        hover.BasisRows[2][0] = 0; hover.BasisRows[2][1] = -sin; hover.BasisRows[2][2] = cos;
        Assert.Equal(new[] { One, 0, 0, 0, cos, sin, 0, -sin, cos }, LevelScene.UnitBasisOf(unit));

        // The same basis reaches the rasterizer: a nose-down frame differs from the level one.
        byte[] level = Render(scene);
        Assert.True(scene.SpawnedCsacUnitPrimitives > 0, "spawned flyer produced no rendered faces");
        Assert.True(level.AsSpan().SequenceEqual(Render(scene)), "two renders of the same basis differ");

        // Yaw only, same pitch rows: the heading must still turn the model.
        hover.BasisRows[1][0] = 0; hover.BasisRows[1][1] = cos; hover.BasisRows[1][2] = sin;
        hover.BasisRows[2][0] = sin; hover.BasisRows[2][1] = -sin; hover.BasisRows[2][2] = cos;
        byte[] yawed = Render(scene);
        hover.BasisRows[2][0] = -sin;
        Assert.False(yawed.AsSpan().SequenceEqual(Render(scene)),
            "the flyer ignored the yaw of its flight basis");
    }
}
