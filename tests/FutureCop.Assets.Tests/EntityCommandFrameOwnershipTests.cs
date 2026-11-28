using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// gDispatchQueuedEntityCommands runs once per frame (800271A4.c:179, func_8004CECC). The legacy
/// drivers reach that phase from two places in the same frame - <see cref="LevelScene.AdvanceWalker"/>
/// ends in the retirement stage that dispatches, and the following <see cref="LevelScene.AdvanceSkyCaptain"/>
/// runs the pad stage that used to dispatch again - so the scene carries a frame marker.
/// <see cref="LevelScene.CommandPassCount"/> makes the ownership observable; without the marker the
/// first test below sees two passes for one frame.
/// </summary>
public sealed class EntityCommandFrameOwnershipTests
{
    private static LevelScene NewScene()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        scene.EnableMechMovement();
        return scene;
    }

    [ConftFact]
    public void TheWalkerAndSkyCaptainShareOneCommandPassPerFrame()
    {
        var scene = NewScene();
        int start = scene.CommandPassCount;

        // One legacy frame: the walker's retirement stage dispatches, the sky-captain stage continues
        // the same frame and must not dispatch a second time.
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.Equal(start + 1, scene.CommandPassCount);
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.Equal(start + 1, scene.CommandPassCount);

        // The next frame gets exactly one more pass.
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);
        Assert.Equal(start + 2, scene.CommandPassCount);
    }

    [ConftFact]
    public void AStandaloneSkyCaptainStepOwnsItsOwnCommandPass()
    {
        var scene = NewScene();
        int start = scene.CommandPassCount;
        for (int i = 0; i < 3; i++) scene.AdvanceSkyCaptain(RetailClock.TicksPerFrame);

        // Every call is its own frame when no walker step precedes it.
        Assert.Equal(start + 3, scene.CommandPassCount);
    }
}
