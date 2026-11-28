using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The entry pass of func_8004CECC walks the frame's active table D_800A8F90, not the linked list
/// D_8009FA40: an entity that gCullActiveEntitiesSolo (8004DF28.c, called at 800271A4.c:178) dropped
/// for the frame keeps its queued pairs and runs them only once it is active again.
/// docs/todo/45-entity-command-aktive-tabelle.md.
/// </summary>
public sealed class EntityCommandActiveTableTests
{
    private sealed class CommandProbe : LiveEntity
    {
        private int _x, _z;

        public CommandProbe(int recordId, int x, int z, int retireScript = 0)
        {
            RecordIdValue = recordId;
            RetireScript = retireScript;
            Health = 1000;
            MaxHealth = 1000;
            MoveTo(x, z);
        }

        public int RecordIdValue { get; }

        public override int RecordId => RecordIdValue;

        public override int DispatchType => 14;

        public override (int X, int Z) GroundPosition => (_x, _z);

        public void MoveTo(int x, int z)
        {
            _x = x;
            _z = z;
            Coord = new CollisionCoord(x, 0, z, 0x800, 0x400, 0x400);
        }

        public override int HandleMessage(int message, EntityWorld world, object? data) =>
            BaseHandler(message, this, world, data);
    }

    private static PurchasePadSystem NewSystem(EntityWorld world) =>
        new(new RpnsPool(new byte[] { 0 }), world: world);

    /// <summary>
    /// Frame 1 culls the entity out (0x100000 away, past <see cref="EntityWorld.ActiveRange"/>), so
    /// the pass does not touch its queue: it stays linked, keeps the pair and is not retired. Frame 2
    /// culls it in again and the same pair applies.
    /// </summary>
    [Fact]
    public void ACulledEntityKeepsItsCommandUntilItIsActiveAgain()
    {
        var world = new EntityWorld();
        var pads = NewSystem(world);
        var entity = world.Spawn(new CommandProbe(9101, 0x100000, 0, retireScript: 1));
        int retireCalls = 0;
        world.OnScript = (_, _, _, _) => retireCalls++;

        Assert.True(pads.SendEntityCommand(entity, 0x3D, 0));
        Assert.Equal(1, pads.QueuedCommandCount(entity));

        // Frame cull with the player 0x100000 away (8004DF28.c:117-143): the linked list keeps the
        // entity, D_800A8F90 does not.
        world.CullActive(0, 0);
        Assert.Contains(entity, world.Units);
        Assert.DoesNotContain(entity, world.Active);

        Assert.Equal(0, pads.RunEntityCommands(world));
        Assert.Equal(1, pads.QueuedCommandCount(entity));
        Assert.NotEqual(0, entity.RuntimeId);
        Assert.Equal(0, retireCalls);

        // A second culled frame changes nothing: the pair is still pending.
        world.CullActive(0, 0);
        Assert.Equal(0, pads.RunEntityCommands(world));
        Assert.Equal(1, pads.QueuedCommandCount(entity));
        Assert.NotEqual(0, entity.RuntimeId);

        // The frame that keeps it active runs the pair in that frame's pass.
        world.CullActive(entity.GroundPosition.X, entity.GroundPosition.Z);
        Assert.Contains(entity, world.Active);
        Assert.Equal(1, pads.RunEntityCommands(world));
        Assert.Equal(0, pads.QueuedCommandCount(entity));
        Assert.Equal(0, entity.RuntimeId);
        Assert.Equal(1, retireCalls);
    }

    /// <summary>
    /// Control: for an entity the cull kept, the pair applies in the same pass, as before.
    /// </summary>
    [Fact]
    public void AnActiveEntityRunsItsCommandInTheSamePass()
    {
        var world = new EntityWorld();
        var pads = NewSystem(world);
        var entity = world.Spawn(new CommandProbe(9102, 0x1000, 0, retireScript: 1));
        int retireCalls = 0;
        world.OnScript = (_, _, _, _) => retireCalls++;

        world.CullActive(0, 0);
        Assert.Contains(entity, world.Active);

        Assert.True(pads.SendEntityCommand(entity, 0x3D, 0));
        Assert.Equal(1, pads.RunEntityCommands(world));

        Assert.Equal(0, pads.QueuedCommandCount(entity));
        Assert.Equal(0, entity.RuntimeId);
        Assert.Equal(1, retireCalls);
    }

    /// <summary>
    /// The scene frame (800271A4.c:178 -> func_8004CECC): an entity outside the walker's cull keeps
    /// the pair across <see cref="LevelScene.AdvanceWalker"/> calls and applies it in the frame that
    /// brings it back into range.
    /// </summary>
    [ConftFact]
    public void TheSceneFrameDefersTheCommandOfACulledEntity()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!);
        scene.EnableMechMovement();
        scene.EnableObjectives();
        var world = scene.Entities!;
        var pads = scene.PurchasePads!;
        var walker = scene.Walker!;

        var entity = new CommandProbe(9103, walker.X + EntityWorld.ActiveRange + 0x10000, walker.Z);
        world.Spawn(entity);
        Assert.True(pads.SendEntityCommand(entity, 0x3D, 0));

        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.DoesNotContain(entity, world.Active);
        Assert.Equal(1, pads.QueuedCommandCount(entity));
        Assert.NotEqual(0, entity.RuntimeId);

        // Into the walker's active range: the same pair is consumed by that frame's entry pass, the
        // retire clears the id and the sweep of that frame unlinks the entity.
        entity.MoveTo(walker.X + 0x1000, walker.Z);
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.Equal(0, pads.QueuedCommandCount(entity));
        Assert.Equal(0, entity.RuntimeId);
        Assert.DoesNotContain(entity, world.Active);
        Assert.DoesNotContain(entity, world.Units);
    }
}
