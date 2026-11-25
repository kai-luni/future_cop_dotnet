using System.Buffers.Binary;
using System.Globalization;
using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

public sealed class ElevatorTests
{
    internal static EntityRecord Record(byte flags = 0, byte start = 1, byte count = 2,
        EntityContainer container = EntityContainer.Cact)
    {
        var source = new byte[0x3B];
        source[0x24] = count;
        source[0x25] = start;
        short[] fields = { 81, 921, 1228, 0, 384, 768, 204, 409, 0 };
        for (int i = 0; i < fields.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(0x26 + 2 * i), fields[i]);
        source[0x38] = flags;
        source[0x39] = 14;
        source[0x3A] = 71;
        return new EntityRecord(15, 10, 0x100000, 0x10000, 0x200000, 0x20000000, 100, 0,
            container, 0, 0, source, Array.Empty<int>(), new[] { new ResourceReference("Cobj", 34) });
    }

    [Fact]
    public void TickClampsAtStopsAndReversesWithoutSpendingOvershoot()
    {
        var record = Record();
        record.Source[0x21] = 1; // the sound-pending byte the harness reads from the model pointer
        var lift = new Elevator(record, 100);
        var stages = new List<(int Y, int Timer, int Last, int Next, int Direction,
            int Hold, int ModeFlags, string Commands, int Stopped, int Played)>();
        int stopped = 0, played = 0;
        lift.StopSoundChannel = _ => stopped = lift.Id;
        lift.PlaySound = (id, _, _, _) => played = id;
        void Step(int dt, params ElevatorRider?[] riders)
        {
            stopped = played = 0;
            lift.Tick(dt, riders.ElementAtOrDefault(0), riders.ElementAtOrDefault(1));
            stages.Add((lift.Y, lift.Timer, lift.LastStop, lift.NextStop, lift.Direction,
                lift.HoldTicks, lift.Flags,
                string.Join(",", lift.CommandSlots.SelectMany(slot => new[] { (int)slot.Op, (int)slot.Arg })),
                stopped, played));
        }

        Step(10);
        Step(10, null, new ElevatorRider(lift, 0));
        // The harness sets obj flag 0x400000 before the top snap (harness.c:153), arming the
        // stop-sound channel that 80041880.c:249-253 clears on the first stop change.
        lift.ObjectFlags |= 0x400000u;
        Step(2000);
        Step(100);
        Step(350);
        Step(1000);
        Step(1);
        Step(1, null, new ElevatorRider(lift, 0));

        string[] names = { "no-rider", "second-rider", "top-snap", "dwell", "dwell-expiry",
            "bottom-snap", "wait-at-bottom", "second-rider-at-bottom" };
        var vectors = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "golden", "elevator.vectors"))
            .Select(line => line.Split(' '))
            .ToDictionary(fields => fields[0]);
        Assert.Equal(names.Select(name =>
        {
            // Columns after the motion words (harness.c tick()): hold, mode flags, eight command
            // bytes, GTE load/transform counts, refresh heights, stop/play sound ids. The GTE
            // counts and refresh heights are host-only; the stop-sound id is the harness's live
            // object id (42), which the port reports as the lift's own id and every other row
            // leaves 0. Everything else is compared column for column.
            int[] fields = vectors[name].Skip(1).Take(21)
                .Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            int stopped = fields[19] == 0 ? 0 : lift.Id;
            return (fields[0], fields[1], fields[2], fields[3], fields[4],
                fields[5], fields[6],
                string.Join(",", fields.Skip(7).Take(8)),
                stopped, fields[20]);
        }), stages);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public void WaitingGateRequiresTheIdenticalPlatformAndAZeroStateWord(byte flags, bool riderReady)
    {
        var lift = new Elevator(Record(flags), 100);
        var other = new Elevator(Record(), 100);
        lift.Tick(1, riderReady ? new ElevatorRider(lift, 0) : new ElevatorRider(other, 0),
            new ElevatorRider(lift, 1));

        bool moves = riderReady && (flags & 4) == 0;
        Assert.Equal(moves ? 1406 : 1396, lift.Y);
        Assert.Equal(moves ? -1 : flags == 0 ? 0 : 1, lift.Timer);
    }

    [Fact]
    public void HitHoldCapsAtTwentyAndFrozenTicksDoNotConsumeIt()
    {
        var lift = new Elevator(Record(), 100);
        for (int i = 0; i < 23; i++) lift.OnMessage(11, i, 0);
        Assert.Equal(20, lift.HoldTicks);
        Assert.True(lift.TryQueueCommand(0x53, 0));
        lift.Tick(50);
        Assert.Equal(20, lift.HoldTicks);
        Assert.True(lift.TryQueueCommand(0x53, 1));
        Assert.True(lift.TryQueueCommand(0x52, 0));
        for (int i = 0; i < 20; i++) lift.Tick(50);
        Assert.Equal(0, lift.HoldTicks);
        Assert.Equal(1396, lift.Y);
        lift.Tick(1);
        Assert.Equal(1406, lift.Y);
    }

    [Fact]
    public void SeekAndSnapCommandsSelectTheSignedDirectionAndRefreshInOrder()
    {
        var lift = new Elevator(Record(start: 2, count: 3), 100);
        var refreshed = new List<int>();
        lift.RefreshFrame = e => refreshed.Add(e.Y);
        Assert.True(lift.TryQueueCommand(0x99, 0));
        Assert.True(lift.TryQueueCommand(0x52, 1));
        lift.Tick(1);
        Assert.Equal(0, lift.NextStop);
        Assert.Equal(2, lift.Direction);
        Assert.Equal(14815, lift.Y);
        Assert.True(lift.TryQueueCommand(0x52, 13));
        lift.Tick(0);
        Assert.Equal(new[] { 14815, 19748, 19748 }, refreshed);
        Assert.Equal(1, lift.NextStop);
    }

    [Fact]
    public void GroundQueryReturnsTheSameLiftAndRemovesItOnMessageFive()
    {
        var record = Record();
        var model = new Model { Id = 34 };
        model.FrameBounds.Add(new short[] { 0, 0, 0, 0, 0, 0, 300, 0 });
        model.FrameBounds.Add(new short[] { 0, 0, 100, 64, 20, 64, 0, 0 });
        var registry = new MapObjectRegistry { GroundHeight = _ => 100, ResolveModel = _ => model };
        registry.LiveObjects.Allocate();
        Assert.Equal(1, registry.PlaceMissionObjects(new[] { record }));
        var lift = Assert.IsType<Elevator>(Assert.Single(registry.LiveObjects.Entries));
        var probe = new Vec3(lift.X, lift.Y, lift.Z + 1600);
        var hit = Assert.IsType<GroundObjectHit>(registry.ProbeGroundObjects(probe));
        Assert.Same(lift, hit.Surface);
        Assert.Equal(1792, hit.Height);
        Assert.Equal(14, hit.Id);
        Assert.Null(registry.ProbeGroundObjects(probe with { Y = lift.Y - 1000 }));
        registry.BroadcastMessage(5, 0, 0);
        Assert.Null(registry.ProbeGroundObjects(probe));
    }

    [Fact]
    public void RemainingElevatorGoldenRowsMatchOriginalMips()
    {
        var vectors = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "golden", "elevator.vectors"))
            .Select(line => line.Split(' '))
            .ToDictionary(fields => fields[0],
                fields => fields.Skip(1).Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray());

        void AssertRow(string name, Elevator lift, int stopped, int played)
        {
            var f = vectors[name];
            int wantStopped = f[19] == 0 ? 0 : lift.Id;
            var expected = (f[0], f[1], f[2], f[3], f[4], f[5], f[6],
                string.Join(",", f.Skip(7).Take(8)), wantStopped, f[20]);
            var actual = (lift.Y, lift.Timer, lift.LastStop, lift.NextStop, lift.Direction,
                lift.HoldTicks, lift.Flags,
                string.Join(",", lift.CommandSlots.SelectMany(slot => new[] { (int)slot.Op, (int)slot.Arg })),
                stopped, played);
            Assert.Equal(expected, actual);
        }

        // hover-rider and wrong-lift rows (harness.c:140-147)
        var rec = Record();
        rec.Source[0x21] = 1;
        var lift1 = new Elevator(rec, 100);
        var other = new Elevator(rec, 100);
        lift1.Tick(10);
        lift1.Tick(10, new ElevatorRider(lift1, 1));
        AssertRow("hover-rider", lift1, 0, 0);
        lift1.Tick(10, new ElevatorRider(other, 0));
        AssertRow("wrong-lift", lift1, 0, 0);

        // script-only and arm rows (harness.c:162-166)
        var lift2 = new Elevator(Record(flags: 4), 100);
        lift2.Tick(10, null, new ElevatorRider(lift2, 0));
        AssertRow("script-only", lift2, 0, 0);
        lift2.TryQueueCommand(0x52, 0);
        lift2.Tick(10, null, new ElevatorRider(lift2, 0));
        AssertRow("arm", lift2, 0, 0);

        // freeze, unfreeze, held, seek, command-snap-and-freeze (harness.c:168-180)
        var lift3 = new Elevator(Record(start: 2, count: 3), 100);
        lift3.OnMessage(11, 0, 0);
        lift3.OnMessage(11, 0, 0);
        lift3.TryQueueCommand(0x53, 0);
        lift3.Tick(50);
        AssertRow("freeze", lift3, 0, 0);
        lift3.TryQueueCommand(0x53, 1);
        lift3.Tick(50);
        AssertRow("unfreeze", lift3, 0, 0);
        lift3.Tick(50);
        AssertRow("held", lift3, 0, 0);
        lift3.TryQueueCommand(0x99, 42);
        lift3.TryQueueCommand(0x52, 1);
        lift3.Tick(1);
        AssertRow("seek", lift3, 0, 0);
        lift3.TryQueueCommand(0x52, 13);
        lift3.TryQueueCommand(0x53, 0);
        lift3.Tick(0);
        AssertRow("command-snap-and-freeze", lift3, 0, 0);
    }

    [RetailFact]
    public void LevelScene_PlacesRidesCommandsAndRendersConftType10Elevators()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!);
        scene.EnableMechMovement();
        scene.EnableFiring();

        // CONFT.MIS places 4 type-10 lifts (ids 15, 24, 37, 39) and 0 type-20 carriers.
        var lifts = scene.ObjectRegistry.Elevators.OrderBy(e => e.Id).ToArray();
        Assert.Equal(new[] { 15, 24, 37, 39 }, lifts.Select(e => e.Id));

        var lift = lifts[0]; // id 15
        int y0 = lift.Y;

        // Place walker directly over lift 15's platform surface without pre-setting Platform;
        // MechWorld.GroundObjects -> ObjectRegistry.ProbeGroundObjects must bind Walker.Platform = lift.
        var hit = Assert.IsType<GroundObjectHit>(
            scene.ObjectRegistry.ProbeGroundObjects(new Vec3(lift.X, lift.Y + 0x1000, lift.Z)));
        Assert.Same(lift, hit.Surface);

        var walker = scene.Walker!;
        walker.X = lift.X;
        walker.Y = hit.Height;
        walker.Z = lift.Z;
        walker.Form = 0;
        scene.AttachWalker(walker);

        // First step binds Walker.Platform = lift via MechWorld.GroundObjects and ascends the lift.
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.Same(lift, walker.Platform);
        Assert.True(lift.Y > y0, $"expected lift 15 to ascend from {y0}, got {lift.Y}");

        // RPNS op 56 (cmd 0x53 arg 0) freezes the lift via the scene tag command callback.
        scene.PurchasePads!.VmState.OnCommandByTag!(lift.Record.Id, 0x53, 0);
        int yFrozen = lift.Y;
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.Equal(yFrozen, lift.Y);
        Assert.NotEqual(0, lift.Flags & 2);

        // Rendering with camera anchored on the lift draws its Cobj model faces.
        scene.Render(new LevelCamera(30.0, 35.0, 0x1C00));
        Assert.True(scene.ElevatorPrimitives > 0, "type-10 elevator produced no rendered faces");
    }
}
