using System.Buffers.Binary;
using System.Text;
using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Retail height grid: func_800695A8 (<c>gBuildCoarseHeightGrid</c>) builds the s8 sample block
/// from the Cptc/Ctil terrain and func_80069A38 (<c>gSampleHeightGridBilinear</c>,
/// <c>src/game/utilities/gSampleHeightGridBilinear.c:59-89</c>) samples it bilinearly.
/// </summary>
/// <remarks>
/// The seven sampler checks are the ones <c>tests/host/leaf_more_harness.c:215-236</c> runs against
/// the unchanged retail unit under QEMU (7 checks, 0 failures): the same nine signed heights, the
/// same descriptor (origin 0x8000, stride 3, last x cell 1) and the same probe points.
/// </remarks>
public sealed class HeightGridTests
{
    private const int Cell = 0x20000;

    // ---- func_80069A38 against the shared retail harness inputs -------------------------------

    private static HeightGrid HarnessGrid()
    {
        // Descriptor D_800A2940 as the harness sets it: origin 0x8000, width 3, max column 1 and
        // three x strips of three consecutive z samples.
        sbyte[] heights = { -20, 12, 28, 60, -4, 20, 100, 76, 52 };
        return new HeightGrid(0x8000, 3, 1, heights);
    }

    private static int At(HeightGrid grid, int x, int z) => grid.Sample(new[] { x, 123, z });

    /// <summary>Sample at the exact centre of coarse sample (<paramref name="x"/>, <paramref name="z"/>).</summary>
    private static int Raw(HeightGrid grid, int x, int z) =>
        grid.Sample(grid.Origin + x * 0x4000, grid.Origin + z * 0x4000) >> 8;

    [Fact]
    public void SamplerReproducesTheRetailHarnessChecks()
    {
        var grid = HarnessGrid();
        Assert.Equal(-20 * 256, At(grid, 0x8000, 0x8000));   // "signed height at origin"
        Assert.Equal(30 * 256, At(grid, 0xB000, 0x9000));    // "asymmetric bilinear sample"
        Assert.Equal(-4 * 256, At(grid, 0xC000, 0xC000));    // "last eligible x/z cell"
        Assert.Equal(0x8000, At(grid, 0x10000, 0xC000));     // "x needs a next strip"
        Assert.Equal(0x8000, At(grid, 0xC000, 0x10000));     // "z needs a next sample"
        Assert.Equal(0x8000, At(grid, 0xC000, 0x7FFF));      // "below z origin by one unit"
        Assert.Equal(0x8000, At(grid, 0x7FFF, 0x8000));      // "below x origin by one unit"
    }

    [Fact]
    public void SamplerRoundsEachInterpolationStageDown()
    {
        var grid = HarnessGrid();
        // dz = 0x0001: (12 - (-20)) * 256 = 8192, 8192 / 16384 truncates to 0, so both row
        // interpolations stay at their left sample and only the x weight contributes.
        Assert.Equal(-20 * 256, At(grid, 0x8000, 0x8001));
        // dx = 0x3FFF: (60 - (-20)) * 256 * 0x3FFF >> 14 = 20478, not the unrounded 20480.
        Assert.Equal(-20 * 256 + 20478, At(grid, 0xBFFF, 0x8000));
    }

    // ---- func_800695A8 grid construction ------------------------------------------------------

    /// <summary>813 height bytes of one section: 17 x 17 points x 3 layers at chunk +0x0C.</summary>
    private const int HeightBlock = 813;

    private static byte[] Uniform(sbyte height) => Enumerable.Repeat((byte)height, HeightBlock).ToArray();

    /// <summary>
    /// Minimal mission with a Cptc/GRDB header (border 1) and one Ctil/Ctil-Sect per grid cell, each
    /// streamed through the retail SHOC/SDAT framing so <see cref="LevelMap.FromBytes"/> accepts it.
    /// </summary>
    private static LevelMap SyntheticMap(int cellsX, int cellsZ, Func<int, int, byte[]>? heights = null)
    {
        const int border = 1;
        int w = cellsX + 2 * border - 1;
        int h = cellsZ + 2 * border - 1;

        var cptc = new byte[0x30 + w * h * 4];
        Write32(cptc, 0x00, Tag("GRDB"));
        Write32(cptc, 0x04, cptc.Length);
        Write32(cptc, 0x08, 0);
        Write32(cptc, 0x0C, 0);
        Write32(cptc, 0x10, w);
        Write32(cptc, 0x14, h);
        Write32(cptc, 0x18, 0);
        Write32(cptc, 0x1C, 0);
        Write32(cptc, 0x20, 0);
        Write32(cptc, 0x24, border);
        for (int row = 0; row < h; row++)
        {
            for (int column = 0; column < w; column++)
            {
                // One section per cell: id = 1 + row * w + column (low bits; the loader shifts by 2).
                Write32(cptc, 0x30 + (row * w + column) * 4, (1 + row * w + column) << 2);
            }
        }

        using var archive = new MemoryStream();
        using (var writer = new BinaryWriter(archive, Encoding.UTF8, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("MIS "));
            writer.Write(new byte[20]);
            Streamed(writer, "Cptc", 0, cptc);
            for (int row = 0; row < h; row++)
            {
                for (int column = 0; column < w; column++)
                {
                    // Grid cell (j) maps to header cell (j + border - 1); the outer border rings
                    // are never sampled and stay at zero.
                    int cellX = row - (border - 1), cellZ = column - (border - 1);
                    byte[] block = heights is not null && cellX < cellsX && cellZ < cellsZ
                        ? heights(cellX, cellZ)
                        : Uniform(0);
                    Streamed(writer, "Ctil", 1 + row * w + column, Section(block));
                }
            }
        }
        return LevelMap.FromBytes(archive.ToArray());
    }

    /// <summary>Sect chunk of 0x3CC bytes with an empty table set and the height block at +0x0C.</summary>
    private static byte[] Section(byte[] heights)
    {
        var data = new byte[0x3CC];
        Write32(data, 0x00, Tag("Sect"));
        Write32(data, 0x04, data.Length);
        Write32(data, 0x08, 0);              // counts word: no colour/UV/descriptor entries
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x3CA), 0); // tile count
        heights.CopyTo(data.AsSpan(0x0C));
        return data;
    }

    /// <summary>SHOC header block + one SDAT packet, the framing func_80024EC8 streams.</summary>
    private static void Streamed(BinaryWriter writer, string tag, int id, byte[] payload)
    {
        writer.Write(Tag("SHOC"));
        writer.Write(64);                    // header length; payload packet starts at +64
        writer.Write(0);
        writer.Write(0);
        writer.Write(Tag("SHDR"));
        writer.Write(0);
        writer.Write(Tag(tag));
        writer.Write(id);
        writer.Write(payload.Length);
        writer.Write(new byte[64 - 36]);
        writer.Write(Tag("SHOC"));
        writer.Write(payload.Length + 20);
        writer.Write(0);
        writer.Write(0);
        writer.Write(Tag("SDAT"));
        writer.Write(payload);
    }

    private static int Tag(string name) => unchecked((int)ChunkReader.TagValue(name));
    private static void Write32(byte[] data, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);

    [Fact]
    public void SyntheticMapDescribesTheGridItPromises()
    {
        var map = SyntheticMap(3, 3, (_, _) => Uniform(7));
        Assert.Equal(4, map.Header.Width);
        Assert.Equal(4, map.Header.Height);
        Assert.Equal(1, map.Header.Border);
        Assert.Equal(16, map.Sections.Count);
        Assert.Equal(1, map.Header.SectionIdAt(0, 0));
        var section = map.Section(1);
        Assert.NotNull(section);
        Assert.Equal(7, section!.Height(0));
        Assert.Equal(7, section.Height(812));
        var grid = HeightGrid.Build(map);
        Assert.NotNull(grid);
        Assert.Equal(24, grid!.Width);       // (w - 2 * border + 1) cells * 8 samples
        Assert.Equal(24, grid.Rows);
        Assert.Equal(22, grid.LastCellX);    // H - 2
        Assert.Equal(0x2000, grid.Origin);   // ((border - 1) << 17) + 0x2000
    }

    [Fact]
    public void BuilderFiltersTheCoarseBlockAndKeepsItsPeak()
    {
        // Every cell sits at -50; inside cell (1, 1) the last byte of one 2 x 2 vertex group is 100.
        // The group covers vertices (2..3, 2..3) = rows 2 * 0x66, columns 2 * 6, bytes 0x33 + 5 of
        // the second row, so only a builder that reads the whole 12-byte group sees the peak.
        int group = 1 * 0x66 + 1 * 6;        // group (sy, sx) = (1, 1) of the cell's 8 x 8 blocks
        byte[] spike = Uniform(-50);
        spike[group + 0x33 + 5] = 100;

        var map = SyntheticMap(3, 3, (x, z) => x == 1 && z == 1 ? spike : Uniform(-50));
        var grid = HeightGrid.Build(map)!;

        Assert.Equal(-50, Raw(grid, 0, 0));
        Assert.Equal(100, Raw(grid, 9, 9));
        // 3 x 3 binomial tap one sample up-left of the peak: (-200 - 400 - 50 + 8) >> 4.
        Assert.Equal(-41, Raw(grid, 8, 8));
        // A uniform region keeps its level through the filter and the bilinear stages.
        Assert.Equal(-50 * 256, At(grid, grid.Origin + 20 * 0x4000 + 0x1FFF, grid.Origin + 20 * 0x4000 + 0x01));
    }

    [ConftFact]
    public void ConftGridMatchesItsHeaderAndStaysWithinTheTerrainRange()
    {
        string? path = LevelSceneRetirementTests.FindMission();
        Assert.NotNull(path);
        var map = LevelMap.FromMisFile(path!);
        var grid = HeightGrid.Build(map);
        Assert.NotNull(grid);

        int border = map.Header.Border;
        Assert.Equal((map.Header.Width - 2 * border + 1) * 8, grid!.Width);
        Assert.Equal((map.Header.Height - 2 * border + 1) * 8, grid.Rows);
        Assert.Equal(((border - 1) << 17) + 0x2000, grid.Origin);

        // Per map cell, the signed range of its 17 x 17 x 3 height block. The 3 x 3 binomial tap and
        // the centre max only combine the maxima of the surrounding cells, so every sample must stay
        // inside the range of its own cell and its eight neighbours.
        int cellsX = grid.Rows >> 3, cellsZ = grid.Width >> 3;
        var low = new int[cellsX * cellsZ];
        var high = new int[cellsX * cellsZ];
        for (int cx = 0; cx < cellsX; cx++)
        {
            for (int cz = 0; cz < cellsZ; cz++)
            {
                var section = map.Section(map.Header.SectionIdAt(cx + border - 1, cz + border - 1));
                Assert.NotNull(section);
                int lo = 127, hi = -128;
                for (int i = 0; i < 17 * 17 * 3; i++)
                {
                    int h = section!.Height(i);
                    lo = Math.Min(lo, h);
                    hi = Math.Max(hi, h);
                }
                low[cx * cellsZ + cz] = lo;
                high[cx * cellsZ + cz] = hi;
            }
        }

        int minSample = 127, maxSample = -128;
        // func_80069A38 needs a next row and a next column, so the last sample row and column are
        // endpoints only and probing them returns the 0x8000 sentinel.
        for (int x = 0; x < grid.Rows - 1; x++)
        {
            for (int z = 0; z < grid.Width - 1; z++)
            {
                int cx = x >> 3, cz = z >> 3;
                int lo = 127, hi = -128;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int nx = Math.Clamp(cx + dx, 0, cellsX - 1), nz = Math.Clamp(cz + dz, 0, cellsZ - 1);
                        lo = Math.Min(lo, low[nx * cellsZ + nz]);
                        hi = Math.Max(hi, high[nx * cellsZ + nz]);
                    }
                }
                // Samples carry 14-bit fractions, so the raw heights scale by 256.
                int value = grid.Sample(grid.Origin + x * 0x4000, grid.Origin + z * 0x4000);
                Assert.InRange(value, lo * 256, hi * 256);
                minSample = Math.Min(minSample, value);
                maxSample = Math.Max(maxSample, value);
            }
        }
        Assert.True(maxSample > minSample, "the CONFT height grid must not be constant");
    }

    // ---- runtime adapters ---------------------------------------------------------------------

    private static EntityRecord FlyerRecord(int id, int x, int y, int z)
    {
        var s = new byte[0x54];
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x00), (ushort)id);
        s[0x04] = BuildOrderSpawner.AircraftEntityType;
        BinaryPrimitives.WriteInt32LittleEndian(s.AsSpan(0x08), x);
        BinaryPrimitives.WriteInt32LittleEndian(s.AsSpan(0x0C), y);
        BinaryPrimitives.WriteInt32LittleEndian(s.AsSpan(0x10), z);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x18), 0x100);
        s[0x1C] = 2;
        s[0x38] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x3C), 0x200);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x3E), 0x333);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x40), 0x399);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x42), 0x400);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(0x44), 0x1400);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x46), 2048);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x4C), 5120);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0x4E), 1280);
        return new EntityRecord(id, BuildOrderSpawner.AircraftEntityType, x, y, z, 0, 0x100, 2,
            EntityContainer.Csac, 0, 0, s, new[] { 0, 0, 0 },
            new[] { new ResourceReference("Cobj", 22) });
    }

    /// <summary>Grid with one raised 8 x 8 block so bilinear and point probes cannot agree.</summary>
    private static HeightGrid RaisedBlockGrid(out int raised)
    {
        var map = SyntheticMap(3, 3, (x, z) => x == 1 && z == 1 ? Uniform(60) : Uniform(0));
        var grid = HeightGrid.Build(map)!;
        raised = grid.Sample(grid.Origin + 12 * 0x4000, grid.Origin + 12 * 0x4000);
        return grid;
    }

    [Fact]
    public void CsacHoverAdapterSamplesTheGridAndKeepsThePointProbeSeparate()
    {
        var grid = RaisedBlockGrid(out int raised);
        Assert.Equal(60 * 256, raised);

        var registry = new MapObjectRegistry { GroundHeight = _ => 0 };
        registry.InitMissionSpawnState();
        registry.LiveObjects.Allocate();
        var spawner = new CsacUnitSpawner(registry, new RetailRandom(0x457)) { HeightGrid = grid };
        var parameters = new CsacSpawnerParameters(
            RespawnDelayRaw: -10, MaxConcurrent: 4, InitialFlags: 1, InitialCount: 4);
        spawner.Register(FlyerRecord(1, 0x32000, 0, 0x32000), parameters);
        Assert.True(spawner.ApplyCommandById(1, CsacUnitSpawner.CommandQueueBurst, 1));
        Assert.Equal(1, spawner.Drain(0));

        var unit = spawner.SpawnedUnits.Single(u => u.SourceSlot.Record.Id == 1);
        var world = Assert.IsAssignableFrom<IHoverEnemyWorld>(unit.HoverWorld);
        var pos = new[] { grid.Origin + 12 * 0x4000 + 0x2000, 123, grid.Origin + 12 * 0x4000 + 0x1000 };
        Assert.Equal(raised, world.SampleHeightBilinear(pos));   // func_80069A38 bilinear result
        Assert.Equal(0, world.GetGroundHeight(pos));             // func_80066F78 point probe
        Assert.NotEqual(world.SampleHeightBilinear(pos), world.GetGroundHeight(pos));

        // The descent state (800324E0.c:200-210) leaves for the steering states once it is back
        // inside its band; with the grid height the clearance is negative, with the old point
        // probe it would be positive and the state would change.
        var hover = unit.HoverMover!;
        hover.FA = 4;
        hover.F4 = 0;
        hover.F6C = 0;
        hover.F114 = 0;
        hover.Pos[0] = pos[0];
        hover.Pos[2] = pos[2];
        hover.Pos[1] = world.SampleHeightBilinear(pos) - 0x100;
        HoverEnemyTick.Tick(hover, 8, world);
        Assert.Equal(4, hover.FA);                      // still below the grid height
        hover.Pos[1] = world.GetGroundHeight(pos) + 0x100;
        HoverEnemyTick.Tick(hover, 8, world);
        Assert.Equal(4, hover.FA);                      // 0x100 is below 60 * 256 as well
        hover.Pos[1] = 60 * 256 + 0x100;
        HoverEnemyTick.Tick(hover, 8, world);
        Assert.Equal(0, hover.FA);                      // above the grid height: back to steering
    }

    [Fact]
    public void SkyCaptainHoverAdapterBuildsTheGridAndDistinguishesItFromThePointProbe()
    {
        var map = SyntheticMap(3, 3, (x, z) => x == 1 && z == 1 ? Uniform(60) : Uniform(0));
        var system = new SkyCaptainSystem();
        system.BindMap(map);
        var grid = system.HeightGrid;
        Assert.NotNull(grid);

        var world = new SkyCaptainSystem.TickWorld(system, new StrategicDrone(), _ => 0);
        var pos = new[] { grid!.Origin + 12 * 0x4000 + 0x2000, 123, grid.Origin + 12 * 0x4000 + 0x1000 };
        Assert.Equal(60 * 256, world.SampleHeightBilinear(pos));
        Assert.Equal(0, world.GetGroundHeight(pos));

        var hover = new HoverEnemy
        {
            Type = 0x25,
            FA = 4,
            F4 = 0,
            F6C = 0,
            F114 = 0,
        };
        hover.Pos[0] = pos[0];
        hover.Pos[2] = pos[2];
        hover.Pos[1] = 60 * 256 - 0x100;
        HoverEnemyTick.Tick(hover, 8, world);
        Assert.Equal(4, hover.FA);   // below the grid height: still descending
        // The same height against the point probe (0) would have left the descent state.
        Assert.True(hover.Pos[1] - world.GetGroundHeight(pos) > 0);
        hover.Pos[1] = 60 * 256 + 0x100;
        HoverEnemyTick.Tick(hover, 8, world);
        Assert.Equal(0, hover.FA);
    }

    [ConftFact]
    public void ConftSceneWiresTheGridIntoBothHoverAdapters()
    {
        string path = LevelSceneRetirementTests.FindMission()!;
        var scene = new LevelScene(path) { EnableCsacSpawner = true };
        scene.EnableMechMovement();
        scene.EnableFiring();

        var sky = Assert.IsType<SkyCaptainSystem>(scene.SkyCaptain);
        var grid = sky.HeightGrid;
        Assert.NotNull(grid);
        Assert.Same(grid, scene.CsacSpawner!.HeightGrid);

        Assert.True(scene.CsacSpawner.ApplyCommandById(33, CsacUnitSpawner.CommandQueueBurst, 1));
        scene.AdvanceSkyCaptain(0);
        var unit = Assert.Single(scene.CsacSpawner.SpawnedUnits.Where(u => u.SourceSlot.Record.Id == 33));
        var world = Assert.IsAssignableFrom<IHoverEnemyWorld>(unit.HoverWorld);

        // The shipped terrain must distinguish the bilinear grid from the point probe somewhere;
        // at that point both adapters have to report the grid value.
        int x = 0, z = 0;
        int gridValue = 0, pointValue = 0;
        for (int i = 0; i < 64 && gridValue == pointValue; i++)
        {
            x = grid!.Origin + (4 + i % 6) * 0x4000 + 0x1234;
            z = grid.Origin + (4 + i / 6) * 0x4000 + 0x2345;
            var pos = new[] { x, CsacUnitSpawner.GroundProbeY, z };
            gridValue = world.SampleHeightBilinear(pos);
            pointValue = world.GetGroundHeight(pos);
        }
        Assert.NotEqual(gridValue, pointValue);
        Assert.Equal(grid!.Sample(x, z), gridValue);
        Assert.Equal(LevelGround.Height(
            scene.Map, x, CsacUnitSpawner.GroundProbeY, z, out _), pointValue);
        Assert.Equal(0x8000, grid.Sample(grid.Origin - 1, grid.Origin));

        // The live hover tick now runs against the grid on every frame.
        for (int f = 0; f < 4; f++) scene.AdvanceSkyCaptain(8);
        Assert.True(unit.Y >= 0);
    }
}
