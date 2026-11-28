using FcMainMenu;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// docs/todo/41-solo-frame-order.md: the solo frame has one owner for the retail phase order of
/// <c>gRunSoloSessionFrame</c> (<c>800271A4.c:177-217</c>). These tests drive the real host entry
/// (<see cref="MenuFlow.Advance"/>) into <see cref="LevelScene.AdvanceSoloFrame"/> and observe one
/// live entity across the cull (<c>:178</c>), the queued command pass (<c>:179</c>), message 6
/// (<c>:180</c>), the deferred updates (<c>:182</c>) and the retirement sweep (<c>:212</c>). The
/// counters come from the frame itself (<see cref="LevelScene.LastSoloFrame"/>), not from an
/// assumed one-sweep-per-substep count.
/// </summary>
public sealed class SoloFrameOrderTests
{
    private const uint Down = 116, Enter = 36;

    private static string? FrontEndPath()
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc == null ? null : Path.Combine(disc, "CW", "FE.MIS");
        return path != null && File.Exists(path) ? path : null;
    }

    /// <summary>Main menu -> Precinct Assault single player -> setup -> zone list -> loadout -> scene.</summary>
    private static MenuFlow StartConftLevel(string frontEnd)
    {
        var flow = new MenuFlow(frontEnd, SceneKind.Level, TextWriter.Null, TextWriter.Null,
            enableLevelMovement: true);
        flow.HandleKey(Down);
        flow.HandleKey(Down);
        flow.HandleKey(Enter); // Setup
        flow.HandleKey(Enter); // Hub
        flow.HandleKey(Enter); // Zone list
        flow.HandleKey(Enter); // Loadout (CONFT)
        flow.HandleKey(Enter); // Scene
        Assert.Equal(MenuScreen.Scene, flow.Screen);
        Assert.NotNull(flow.Level);
        Assert.NotNull(flow.Level!.Walker);
        return flow;
    }

    /// <summary>
    /// A live entity that records the frame phases. It arms itself on message 6 - the observable
    /// state change - and can be retired either by that message (so the sweep of the same frame has
    /// to free it) or by a queued 0x3D pair of the dispatch pass.
    /// </summary>
    private sealed class FrameProbe : LiveEntity
    {
        private readonly int _recordId;

        public FrameProbe(int recordId, int x, int y, int z, bool retireOnMessage6 = false, int health = -1)
        {
            _recordId = recordId;
            X = x;
            Y = y;
            Z = z;
            // Health below zero is what gProcessDeferredEntityUpdates (:182) turns into message 4, so a
            // probe that stays alive has to pass a positive value.
            Health = (short)health;
            MaxHealth = 1;
            RetireOnMessage6 = retireOnMessage6;
            // In range for good: the cull keeps it in D_800A8F90 every frame.
            Flags = AlwaysActiveFlag;
            Coord = new CollisionCoord(x, y, z, 0x800, 0x400, 0x400);
        }

        public int X { get; }
        public int Y { get; }
        public int Z { get; }
        public bool RetireOnMessage6 { get; }
        /// <summary>A probe queued for retirement through pads.SendEntityCommand before the frame.</summary>
        public FrameProbe? DispatchedPeer { get; init; }

        public int Message6Count { get; private set; }
        public int Message6Delta { get; private set; }
        public int DeferredCount { get; private set; }
        public int Message5Count { get; private set; }
        /// <summary>The state the message-6 arm wrote (<c>800271A4.c:180</c>).</summary>
        public bool Armed { get; private set; }
        /// <summary>Message 6 saw the entity still linked in D_8009FA40 (the sweep had not run).</summary>
        public bool ListedAtMessage6 { get; private set; }
        /// <summary>Message 6 already saw the dispatched peer retired (the :179 pass ran first).</summary>
        public bool PeerRetiredBeforeMessage6 { get; private set; }
        /// <summary>Message 5 still saw the state message 6 wrote (the sweep runs later in the frame).</summary>
        public bool ArmedAtSweep { get; private set; }
        /// <summary>Message 5 runs before the unlink of <c>8004D598.c:54-64</c>.</summary>
        public bool ListedAtSweep { get; private set; }

        /// <summary>Linked and armed: the state a draw of this frame would show.</summary>
        public bool Visible => Armed && RuntimeId > 0;

        public override int RecordId => _recordId;
        public override int DispatchType => 5;
        public override (int X, int Z) GroundPosition => (X, Z);

        public override int HandleMessage(int message, EntityWorld world, object? data)
        {
            switch (message)
            {
                case LiveObjectRegistry.MessageFrameTick:
                    Message6Count++;
                    Message6Delta += data as int? ?? 0;
                    ListedAtMessage6 = world.Units.Contains(this);
                    PeerRetiredBeforeMessage6 = DispatchedPeer is null || DispatchedPeer.RuntimeId == 0;
                    Armed = true;
                    if (RetireOnMessage6) LiveEntity.Retire(this, world);
                    break;
                case 4:
                    DeferredCount++;
                    break;
                case 5:
                    Message5Count++;
                    ArmedAtSweep = Armed;
                    ListedAtSweep = world.Units.Contains(this);
                    break;
            }
            return BaseHandler(message, this, world, data);
        }
    }

    /// <summary>
    /// One normal host frame (0.033 s = nine clock ticks, one locomotion substep). The entity arms on
    /// message 6 and retires itself there; the deferred pass of the same frame must not send message 4
    /// and the sweep of the same frame must send message 5 and unlink it. The queued pair proves the
    /// dispatch pass (:179) preceded message 6 (:180).
    /// </summary>
    [RetailFact]
    public void HostFrameRunsMessage6BeforeDeferredUpdatesAndSweepsTheSameFrame()
    {
        string? fe = FrontEndPath();
        if (fe is null) return;
        var flow = StartConftLevel(fe);
        var level = flow.Level!;
        var world = level.Entities!;
        var pads = level.PurchasePads!;
        var walker = level.Walker!;

        var queued = new FrameProbe(9201, walker.X, walker.Y, walker.Z);
        var probe = new FrameProbe(9202, walker.X, walker.Y, walker.Z, retireOnMessage6: true)
        {
            DispatchedPeer = queued,
        };
        world.Spawn(queued);
        world.Spawn(probe);
        Assert.True(pads.SendEntityCommand(queued, 0x3D, 0));
        Assert.Equal(1, pads.QueuedCommandCount(queued));

        // 0.033 s * 300 ticks = 0.99 ms short of ten ticks: gFrameDelta is nine, so the frame carries
        // one locomotion substep (eight ticks) and leaves the remainder with the host.
        const double seconds = 0.033;
        int frameTicks = (int)Math.Min(seconds * RetailClock.TicksPerSecond, 0x3C);
        Assert.Equal(9, frameTicks);
        flow.Advance(seconds, PadButtons.None, AnalogPad.Digital);

        var frame = level.LastSoloFrame;
        Assert.Equal(frameTicks, frame.Ticks);
        Assert.Equal(1, frame.Steps);

        // :179 before :180: the queued pair retired its target in this frame's dispatch pass, so the
        // message-6 arm already saw it gone.
        Assert.Equal(0, queued.RuntimeId);
        Assert.Equal(0, pads.QueuedCommandCount(queued));
        Assert.True(probe.PeerRetiredBeforeMessage6);
        Assert.True(frame.Dispatched >= 1);

        // :180: exactly one broadcast of the frame delta, and the entity was still linked for it.
        Assert.Equal(1, probe.Message6Count);
        Assert.Equal(frameTicks, probe.Message6Delta);
        Assert.True(probe.ListedAtMessage6);
        Assert.True(probe.Armed);
        Assert.False(probe.Visible); // the message-6 arm retired it (id 0) while still linked

        // :182 after :180: the retired entity is skipped, so no message 4 of this frame.
        Assert.Equal(0, probe.DeferredCount);

        // :212 in the same frame: one message 5, the state of :180 was already there, and the unlink
        // followed the message.
        Assert.Equal(1, probe.Message5Count);
        Assert.True(probe.ArmedAtSweep);
        Assert.True(probe.ListedAtSweep);
        Assert.Equal(0, probe.RuntimeId);
        Assert.DoesNotContain(probe, world.Units);
        Assert.Equal(1, queued.Message5Count);
        Assert.Equal(2, frame.Swept);
    }

    /// <summary>
    /// A host call that batches several locomotion substeps (0.1 s = 30 clock ticks = three substeps).
    /// The frame passes still run once: one message-6 broadcast with the frame delta, one dispatch
    /// pass, one sweep. The substep count stays on the frame, not on the passes.
    /// </summary>
    [RetailFact]
    public void MultiStepHostCallRunsTheFramePassesOnceWithAllSubstepsInside()
    {
        string? fe = FrontEndPath();
        if (fe is null) return;
        var flow = StartConftLevel(fe);
        var level = flow.Level!;
        var world = level.Entities!;
        var pads = level.PurchasePads!;
        var walker = level.Walker!;

        var queued = new FrameProbe(9211, walker.X, walker.Y, walker.Z);
        var probe = new FrameProbe(9212, walker.X, walker.Y, walker.Z, health: 100);
        world.Spawn(queued);
        world.Spawn(probe);
        Assert.True(pads.SendEntityCommand(queued, 0x3D, 0));
        long stepsBefore = level.WalkerSteps;

        const double seconds = 0.1;
        int frameTicks = (int)Math.Min(seconds * RetailClock.TicksPerSecond, 0x3C);
        Assert.Equal(30, frameTicks);
        flow.Advance(seconds, PadButtons.None, AnalogPad.Digital);

        var frame = level.LastSoloFrame;
        Assert.Equal(30, frame.Ticks);
        Assert.Equal(3, frame.Steps);                      // 30 ticks / eight ticks per substep
        Assert.Equal(stepsBefore + 3L, level.WalkerSteps); // the substeps ran inside the one frame

        // The per-frame work is not repeated per substep.
        Assert.Equal(1, probe.Message6Count);
        Assert.Equal(30, probe.Message6Delta);
        Assert.Equal(1, frame.Dispatched);
        Assert.Equal(0, probe.DeferredCount);
        Assert.True(probe.Visible);
        Assert.Equal(0, queued.RuntimeId);
        Assert.Equal(1, queued.Message5Count);             // the one sweep pass of the frame
        Assert.Equal(1, frame.Swept);                      // only the dispatched entity retired
    }
}
