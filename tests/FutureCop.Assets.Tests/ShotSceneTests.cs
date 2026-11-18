using FutureCop.Assets;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The fire path in the viewer scene: a fired projectile starts at the alternating arm muzzle, flies
/// along the aim with the integrator's step, disappears at its retail range, and leaves the mech form
/// change untouched.
/// </summary>
public class ShotSceneTests
{
    private static WireframeScene Scene(double shotSpeed = 0.0625)
    {
        var mis = TextureGoldenTests.FindRetailMis();
        Assert.NotNull(mis);
        return new WireframeScene(mis!, new ViewerOptions(Assembly: true, ShotSpeed: shotSpeed), 320, 240);
    }

    [RetailFact]
    public void FiringStartsAtAMuzzleAndMovesAlongTheAim()
    {
        var scene = Scene();
        Assert.NotEmpty(scene.Weapons);
        Assert.Equal(0, scene.ShotCount);

        scene.Fire();
        var first = Assert.Single(scene.Shots);
        Assert.Equal(first.Muzzle.X, first.X);
        Assert.Equal(first.Muzzle.Y, first.Y);
        Assert.Equal(first.Muzzle.Z, first.Z);
        Assert.Equal(first.Definition.ModelId, first.Model.Id);
        Assert.True(first.StepUnits > 0);

        // One tick covers StepUnits along the aim, whatever angle the viewer fires at.
        scene.AdvanceShotTicks(1);
        Assert.Equal(first.Muzzle.X + (((long)first.Direction[0] * first.StepUnits) >> 16),
            (long)first.X);
        Assert.Equal(first.Muzzle.Z + (((long)first.Direction[2] * first.StepUnits) >> 16),
            (long)first.Z);

        // Both arm instances take turns, like the two model records at state + 0x178.
        scene.Fire();
        Assert.Equal(2, scene.ShotCount);
        Assert.NotEqual(scene.Shots[0].Muzzle, scene.Shots[1].Muzzle);
    }

    [RetailFact]
    public void AShotDisappearsAtItsRetailRange()
    {
        var scene = Scene(shotSpeed: 0.0625);
        scene.Fire();
        var shot = scene.Shots[0];
        int expected = (shot.Range + shot.StepUnits - 1) / shot.StepUnits;
        Assert.True(expected is > 2 and < 1000, $"unexpected lifetime {expected} ticks");

        int ticks = 0;
        while (scene.ShotCount > 0 && ticks < 5000)
        {
            scene.AdvanceShotTicks(1);
            ticks++;
        }
        Assert.Empty(scene.Shots);
        // The mover accelerates the step while the alternate-speed word is set, so the shot can only
        // die earlier than the constant-step estimate, never later.
        Assert.InRange(ticks, expected / 2, expected);
    }

    [RetailFact]
    public void AdvancingTicksMovesEveryLiveShot()
    {
        var scene = Scene();
        scene.Fire();
        scene.CycleWeapon(1);
        scene.Fire();
        Assert.Equal(2, scene.ShotCount);
        var start = scene.Shots.Select(s => (s.X, s.Y, s.Z)).ToArray();
        scene.AdvanceShotTicks(3);
        for (int i = 0; i < scene.ShotCount; i++)
            Assert.NotEqual(start[i], (scene.Shots[i].X, scene.Shots[i].Y, scene.Shots[i].Z));
    }

    [RetailFact]
    public void WeaponCycleVisitsEveryDefinitionAndWraps()
    {
        var scene = Scene();
        var seen = new List<ShotDefinition>();
        for (int i = 0; i < scene.Weapons.Count; i++)
        {
            seen.Add(scene.Weapon!);
            scene.CycleWeapon(1);
        }
        Assert.Equal(scene.Weapons.Count, seen.Distinct().Count());
        Assert.Equal(seen[0], scene.Weapon);
        scene.CycleWeapon(-1);
        Assert.Equal(seen[^1], scene.Weapon);
    }

    [RetailFact]
    public void FormChangeStillRunsWhileShotsAreInFlight()
    {
        var scene = Scene();
        scene.Fire();
        var framing = scene.Framing;
        scene.Select(-1);                       // left = hover
        Assert.Equal(PlayerForm.Hover, scene.Form!.Target);
        scene.AdvanceForm((PlayerForm.FullProgress + PlayerForm.ProgressPerTick - 1) /
                          PlayerForm.ProgressPerTick);
        Assert.True(scene.Form!.IsHover);
        scene.Fire();
        Assert.Equal(2, scene.ShotCount);
        Assert.Equal(framing, scene.Framing);   // the camera does not jump because of the shots
        scene.AdvanceShotTicks(4);
        Assert.NotEmpty(scene.Shots);
    }

    [RetailFact]
    public void ModelModeHasNoFirePath()
    {
        var mis = TextureGoldenTests.FindRetailMis();
        Assert.NotNull(mis);
        var scene = new WireframeScene(mis!, new ViewerOptions(ModelId: 30), 320, 240);
        Assert.False(scene.Assembly);
        Assert.Contains("player assembly", scene.Fire());
        Assert.Equal(0, scene.ShotCount);
    }

    [RetailFact]
    public void ShotsContributeFacesInWireframeAndTexturedRendering()
    {
        var mis = TextureGoldenTests.FindRetailMis();
        Assert.NotNull(mis);
        var wire = new WireframeScene(mis!, new ViewerOptions(Assembly: true, CameraDistanceMetres: 0), 320, 240);
        wire.Fire();
        wire.Render(0);
        Assert.True(wire.ShotFaceCount > 0, "the projectile contributed no wireframe faces");

        var textured = new WireframeScene(mis!,
            new ViewerOptions(Assembly: true, Textured: true, CameraDistanceMetres: 0), 320, 240);
        textured.Fire();
        textured.Render(0);
        Assert.True(textured.ShotFaceCount > 0, "the projectile contributed no textured faces");
    }

    [RetailFact]
    public void AutoFireUsesTheWeaponCadence()
    {
        var scene = Scene();
        Assert.Equal(0, scene.ShotCount);
        var weapon = scene.Weapon!;
        Assert.Equal(1, weapon.Burst);
        Assert.Equal(8, weapon.ReloadTicks);

        // The trigger runs one 60 Hz frame per 1/60 s: the first frame fires, the reload keeps the
        // next two frames quiet, and the rhythm repeats - the retail every-second-frame pattern.
        var frames = new List<int>();
        for (int frame = 0; frame < 12; frame++)
        {
            if (scene.AdvanceAutoFire(1.0 / 60.0) is not null) frames.Add(frame);
        }
        Assert.Equal(new[] { 0, 2, 4, 6, 8, 10 }, frames);
        Assert.True(scene.ShotCount > 0);
    }

    [RetailFact]
    public void AutoFireStaysOffWithoutTheAssembly()
    {
        var mis = TextureGoldenTests.FindRetailMis();
        Assert.NotNull(mis);
        var scene = new WireframeScene(mis!, new ViewerOptions(ModelId: 30), 320, 240);
        Assert.Null(scene.AdvanceAutoFire(5.0));
        Assert.Equal(0, scene.ShotCount);
    }
}
