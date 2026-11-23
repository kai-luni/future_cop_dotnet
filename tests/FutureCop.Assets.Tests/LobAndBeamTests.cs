using System.Buffers.Binary;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The lob arithmetic of func_8005E624 and the beam segment of func_8006B23C: both need the retail
/// arctangent table, so these tests pin the table down first and then the shapes it produces.
/// </summary>
public sealed class LobAndBeamTests
{
    [Fact]
    public void TheArctangentTableMatchesTheRetailImage()
    {
        // D_8009E0A0 is a 1025-entry half-word table of atan(i / 1024) in 12-bit units. Only a table
        // read from the image can satisfy all three anchors.
        // atan(i / 1024) in 12-bit units (4096 = full turn): 14.04 degrees at 0.25, 26.57 at 0.5,
        // 45 at 1.0 -> 160, 302, 512.
        Assert.Equal(0, MechTables.Atan(0));
        Assert.Equal(160, MechTables.Atan(256));
        Assert.Equal(302, MechTables.Atan(512));
        Assert.Equal(512, MechTables.Atan(1024));
        // Monotonically non-decreasing across the whole table.
        for (int i = 1; i <= 1024; i++)
            Assert.True(MechTables.Atan(i) >= MechTables.Atan(i - 1), $"table drops at {i}");
    }

    [Fact]
    public void Atan2FollowsTheFourQuadrants()
    {
        // func_80095C30: (x, y) -> 12-bit angle, 0 along +y, quarter turns at the axes.
        Assert.Equal(0x000, LobMotion.Atan2(0, 1024) & 0xFFF);
        Assert.Equal(0x400, LobMotion.Atan2(1024, 0) & 0xFFF);
        Assert.Equal(0x200, LobMotion.Atan2(1024, 1024) & 0xFFF);
        Assert.Equal(0x800, LobMotion.Atan2(0, -1024) & 0xFFF);
        Assert.Equal(0xC00, LobMotion.Atan2(-1024, 0) & 0xFFF);
    }

    [Fact]
    public void ASolvedLobPointsFromTheMuzzleTowardTheTarget()
    {
        // A flat lob over a distance of 2048 units: the horizontal velocity must point at the target
        // and the vertical one must be non-negative, i.e. the shot rises first.
        var src = new[] { 0, 0, 0 };
        var dst = new[] { 0, 0, 2048 };
        var v = LobMotion.Solve(16, src, dst, 0x40);

        Assert.True(v[2] > 0, $"planar velocity should aim along +z, got {v[2]}");
        Assert.True(v[1] > 0, $"a lobbed shot must rise, got y={v[1]}");
    }

    [Fact]
    public void ALobAlwaysRisesWhateverTheTargetHeight()
    {
        // func_8005E624:181-185 takes the cosine, negates it when negative and uses the absolute value,
        // so the vertical word is |cos| * speed and can never point down. A lob always arcs upward,
        // which is what the port reproduces - including for a target below the muzzle.
        var src = new[] { 0, 0, 0 };
        foreach (var dy in new[] { -1024, 0, 1024 })
        {
            var v = LobMotion.Solve(16, src, new[] { 0, dy, 2048 }, 0x40);
            Assert.True(v[1] > 0, $"target dy={dy}: a lob must rise, got y={v[1]}");
            Assert.True(v[0] != 0 || v[2] != 0, $"target dy={dy}: no horizontal velocity, got ({v[0]}, {v[2]})");
        }
    }

    [Fact]
    public void ABeamEndsAlongItsDirectionAtTheRequestedReach()
    {
        // The range is a raw word; the retail endpoint uses the shift order in func_8006B23C:340-342.
        var half = new ShotDefinition(
            SectionId: 0x3F, Half: 0, Action: 0x0A, Flags: 0, ModelId: 1, ObjWord00: 0,
            DamageWord: 0, SpeedWord: 0, AltSpeed: 0, ArcWord: 6144, ObjWord0E: 0, ObjWord12: 0,
            SoundId: 0, SecondaryColumn: 0, Burst: 0, ReloadTicks: 0, GapTicks: 0, RecordOffset: 0,
            Resources: Array.Empty<ResourceReference>());

        var beam = new Beam(half, 100, 200, 300, new[] { 0, 0, 0x10000 }, 6144);

        Assert.Equal(100, beam.End.X);
        Assert.Equal(200, beam.End.Y);
        Assert.Equal(300 + 98304, beam.End.Z);
        Assert.Equal(6144, beam.Length);
    }

    [Theory]
    // func_8006B23C:340-342: v38 = v58 + (((axis >> 4) * signedRange) >> 8).
    [InlineData(0, 65536, 6144, 0, 98304)]
    [InlineData(-12345, 23457, 17, -52, 97)]
    [InlineData(65536, -65536, -16, -256, 256)]
    [InlineData(1, -1, 255, 0, -1)]
    [InlineData(16777216, -16777216, 6144, -8388608, -8388608)]
    public void BeamEndUsesTheRetailShiftOrder(int axisX, int axisZ, int range, int dx, int dz)
    {
        var half = new ShotDefinition(
            SectionId: 0x3F, Half: 0, Action: 0x0A, Flags: 0, ModelId: 1, ObjWord00: 0,
            DamageWord: 0, SpeedWord: 0, AltSpeed: 0, ArcWord: range, ObjWord0E: 0, ObjWord12: 0,
            SoundId: 0, SecondaryColumn: 0, Burst: 0, ReloadTicks: 0, GapTicks: 0, RecordOffset: 0,
            Resources: Array.Empty<ResourceReference>());

        var beam = new Beam(half, 100, 200, 300, new[] { axisX, 0, axisZ }, range);

        Assert.Equal((100 + dx, 200, 300 + dz), beam.End);
        Assert.Equal(range, beam.Length);
    }

    [Fact]
    public void ABeamExpiresAfterItsVisibleFrames()
    {
        var half = new ShotDefinition(
            SectionId: 0x3F, Half: 0, Action: 0x0A, Flags: 0, ModelId: 1, ObjWord00: 0,
            DamageWord: 0, SpeedWord: 0, AltSpeed: 0, ArcWord: 8, ObjWord0E: 0, ObjWord12: 0,
            SoundId: 0, SecondaryColumn: 0, Burst: 0, ReloadTicks: 0, GapTicks: 0, RecordOffset: 0,
            Resources: Array.Empty<ResourceReference>());
        var beam = new Beam(half, 0, 0, 0, new[] { 0, 0, 0x10000 }, 8);

        for (int i = 0; i < Beam.LifeFrames; i++)
        {
            Assert.False(beam.Expired, $"beam expired after {i} of {Beam.LifeFrames} frames");
            beam.Advance();
        }
        Assert.True(beam.Expired);
    }
}
