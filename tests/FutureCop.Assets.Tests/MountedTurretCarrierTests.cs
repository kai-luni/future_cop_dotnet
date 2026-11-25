using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

public sealed class MountedTurretCarrierTests
{
    internal static EntityRecord Record(EntityContainer container = EntityContainer.Cact) =>
        ElevatorTests.Record(container: container) with { Type = 20, Id = 42 };

    [Theory]
    [InlineData(11)]
    [InlineData(14)]
    public void ChildForwardsPayloadAndCarrierSynchronizesAfterClassFive(int message)
    {
        var carrier = new MountedTurretCarrier(Record());
        var child = carrier.AttachChild();
        child.Health = -7;
        child.DamageResponse = 1;
        child.Flags = 0x8000;
        var calls = new List<string>();
        carrier.Class5Handler = (self, msg, a2, a3) =>
        {
            Assert.Same(carrier, self);
            Assert.Equal((message, 123, -456), (msg, a2, a3));
            self.Health = unchecked((short)40000);
            self.Flags |= 0x02000000;
            calls.Add("class5");
            return 99;
        };
        child.OnQuartileChange = (part, previous) =>
        {
            Assert.Equal(-7, previous);
            Assert.Equal(unchecked((short)40000), part.Health);
            Assert.Equal(0x02008000u, part.Flags);
            calls.Add("quartile");
        };
        Assert.Equal(0, child.OnMessage(message, 123, -456));
        Assert.Equal(new[] { "class5", "quartile" }, calls);
    }

    [Fact]
    public void MessageTenVetoesOnlyThePairsOwnCarrier()
    {
        // 80039298.c:80 `return obj->child != data`: the pair test passes the other side of the
        // pair as `data`; 0 means "this one is my own carrier, do not collide us".
        var carrier = new MountedTurretCarrier(Record());
        var child = carrier.AttachChild();
        Assert.Equal(0, child.OnMessage(10, carrier, 0, 0));
        Assert.Equal(1, child.OnMessage(10, new object(), 0, 0));
        Assert.Equal(1, child.OnMessage(10, null, 0, 0));
        // The base handler answers 10 with 1 when no partner object is supplied (8002AD58.c:103-105).
        Assert.Equal(1, child.OnMessage(10, 0, 0));
    }

    [Fact]
    public void RetirementClearsTheCarrierHealthWordToMinusOne()
    {
        // 800395A8.c:101-103 (carrier side): message 5 writes the +0x158 child health to -1.
        // 80039298.c:89-113 (mounted part side): message 5 clears the child-array link and
        // writes the carrier's own +0x1C word (s16, struct Child unk1C) to -1 unless flag 1
        // is set - so the carrier health is a signed word and -1, not 0xFFFF.
        var carrier = new MountedTurretCarrier(Record()) { Health = 1234 };
        var child = carrier.AttachChild();
        child.Health = 500;
        child.OnMessage(5, 0, 0);
        Assert.Equal(-1, carrier.Health);
        Assert.Equal(500, child.Health);
        Assert.Null(carrier.Child);

        // The carrier side pushes the -1 to the child while it is still attached; its own health
        // word is untouched by 800395A8.c:101-103 (only the part's handler writes the carrier word).
        var carrier2 = new MountedTurretCarrier(Record()) { Health = 1234 };
        var child2 = carrier2.AttachChild();
        child2.Health = 500;
        carrier2.OnMessage(5, 0, 0);
        Assert.Equal(1234, carrier2.Health);
        Assert.Equal(-1, child2.Health);
    }

    [Fact]
    public void FlagOneUsesBaseHandlerAndRetirementMarksChildBeforeDelegating()
    {
        var carrier = new MountedTurretCarrier(Record());
        var child = carrier.AttachChild();
        child.TurretFlags |= 1;
        child.BaseHandler = (_, msg, a2, a3) => msg + a2 + a3;
        carrier.Class5Handler = (_, msg, _, _) =>
        {
            Assert.Equal(5, msg);
            Assert.Equal(-1, child.Health);
            return 99;
        };
        Assert.Equal(16, child.OnMessage(11, 2, 3));
        Assert.Equal(99, carrier.OnMessage(5, 0, 0));
    }

    [Fact]
    public void TickCopiesHealthBeforeUpdatingTheMountedChildWithoutChangingItsFlags()
    {
        var carrier = new MountedTurretCarrier(Record());
        var child = carrier.AttachChild();
        child.Flags = 0x02000010;
        child.TurretFlags = 0x20;
        child.Heading = 0x400;
        carrier.Class5Handler = (self, msg, dt, _) =>
        {
            Assert.Equal((6, 7), (msg, dt));
            self.Health = 42;
            self.Flags = 0;
            self.ActiveWord = 1;
            return 99;
        };
        child.ResolvePartPosition = (_, _) =>
        {
            Assert.Equal(42, child.Health);
            Assert.Equal(0x02000010u, child.Flags);
            return new FutureCop.Game.Mech.Vec3(12, 34, 56);
        };
        Assert.Equal(0, carrier.OnMessage(6, 7, 0));
        Assert.Equal(0x400, child.Yaw);
        Assert.Equal(34, child.Position.Y);
    }

    [Fact]
    public void SyntheticCarrierIsPlacedFromCactAndFromAnExplicitCsacSpawn()
    {
        var registry = new MapObjectRegistry();
        registry.LiveObjects.Allocate();
        var record = Record();
        var template = Record(EntityContainer.Csac);
        Assert.Equal(1, registry.PlaceMissionObjects(new[] { record, template }));
        Assert.Equal(record, Assert.IsType<MountedTurretCarrier>(Assert.Single(registry.LiveObjects.Entries)).Record);

        var spawner = new CsacUnitSpawner(registry);
        Assert.Equal(1, spawner.RegisterFromRecords(new[] { record, template }));
        Assert.Single(registry.LiveObjects.Entries);
        var unit = Assert.IsType<SpawnedCsacUnit>(spawner.TrySpawn(Assert.Single(spawner.Slots), 1));
        var carrier = Assert.IsType<MountedTurretCarrier>(unit.RuntimeObject);
        Assert.Equal(template, carrier.Record);
        Assert.Equal(unit.Id, carrier.Id);
        Assert.Equal(2, registry.LiveObjects.Count);
    }
}
