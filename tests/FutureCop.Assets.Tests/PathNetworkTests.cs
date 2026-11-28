using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Ports the five <c>gFindRailPathCell</c> (<c>func_80050DAC</c>) vectors of
/// <c>tests/host/leaf_final_harness.c:365-391</c> (PS1 <c>e057e56</c>) onto the production
/// <see cref="PathNetwork.FindCell"/>.
/// </summary>
public sealed class PathNetworkTests
{
    /// <summary>
    /// The packed, counted record list of the C fixture. Each entry is one
    /// <c>struct Unk50DACCell { s16 unk00, x, unk04, z; s32 unk08; }</c>; <c>func_80050DAC</c>
    /// reads only <c>x</c> (+2) and <c>z</c> (+6), i.e. the managed <see cref="PathNode.X"/> and
    /// <see cref="PathNode.Z"/>. The C record count (<c>map.count</c>) is the length of the node
    /// array handed to the lookup, so record 3 is only reachable once the array has four nodes.
    /// </summary>
    private static readonly (short X, short Z)[] RailRecords =
    {
        (-4, 7),  // fixture.map.cells[0]
        (-4, 8),  // fixture.map.tail[0]
        (-4, 8),  // fixture.map.tail[1]
        (-5, 8),  // fixture.map.tail[2] - backing record behind the count in the first three cases
    };

    // s32 point[] = { -4 * 256, 0x12345678, 8 * 256 + 255 }; the middle element (Y) never
    // reaches gFindRailPathCell, which compares only the cells X and Z.
    private const int PointX = -4 * 256;

    private static PathNetwork RailNetwork(int count)
    {
        Assert.InRange(count, 0, RailRecords.Length);
        var nodes = new PathNode[count];
        for (int i = 0; i < count; i++)
            nodes[i] = new PathNode { X = RailRecords[i].X, Z = RailRecords[i].Z };
        return new PathNetwork(7, nodes);
    }

    [Fact]
    public void SkipsWrongZAndReturnsFirstOfTwoEqualCells()
    {
        // point = { -4 * 256, 0x12345678, 8 * 256 + 255 }; active count 3.
        Assert.Equal(1, RailNetwork(3).FindCell(PointX, 8 * 256 + 255));
    }

    [Fact]
    public void OneUnitBelowTheZBoundaryHitsThePreviousCell()
    {
        // point[2] = 8 * 256 - 1; active count 3.
        Assert.Equal(0, RailNetwork(3).FindCell(PointX, 8 * 256 - 1));
    }

    [Fact]
    public void MatchingBackingRecordBeyondTheCountIsNotReturned()
    {
        // point[0]-- so the world X is -1025; active count 3 excludes the {-5, 8} record.
        Assert.Equal(-1, RailNetwork(3).FindCell(PointX - 1, 8 * 256));
    }

    [Fact]
    public void NegativeXBelowTheExactBoundaryFloorsToMinusFive()
    {
        // fixture.map.count = 4 now covers the {-5, 8} backing record.
        Assert.Equal(3, RailNetwork(4).FindCell(PointX - 1, 8 * 256));
    }

    [Fact]
    public void EmptyRailListReturnsMinusOne()
    {
        // fixture.map.count = 0.
        Assert.Equal(-1, RailNetwork(0).FindCell(PointX - 1, 8 * 256));
    }
}
