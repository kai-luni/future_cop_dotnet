using System.Buffers.Binary;
using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The fire definitions the mission carries: a tACT chunk of 0x60 bytes whose payload holds the
/// 0x44-byte record func_8006A918 copies into the runtime registry D_800A3340, followed by the aRSL
/// list whose four Cobj references func_8006A918 resolves and func_8006ACB0 hands to the spawner.
/// </summary>
public class WeaponCatalogTests
{
    /// <summary>Builds one synthetic tACT + aRSL pair with a single usable fire half.</summary>
    private static byte[] SyntheticDefinition(int section, int modelId, int action, int speed,
        int arc, int alt, int sound)
    {
        uint tact = ChunkReader.TagValue("tACT"), arsl = ChunkReader.TagValue("aRSL");
        var data = new byte[0xF0];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), tact);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 0x60);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), section);
        int record = 12 + 0x10;
        int half = record + 0x04;
        data[half + 0x00] = (byte)action;
        data[half + 0x01] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(half + 0x0C), 0x0011);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(half + 0x0E), 0x0020);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(half + 0x12), (short)speed);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(half + 0x14), (short)alt);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(half + 0x16), (short)arc);
        data[half + 0x1B] = 0x4E;
        data[half + 0x1C] = (byte)sound;

        int list = 0x60;   // the aRSL list follows the 0x60-byte tACT chunk
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(list), arsl);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(list + 4), 0x2C);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(list + 8), section);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(list + 12), ChunkReader.TagValue("Cobj"));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(list + 16), modelId);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(list + 20), ChunkReader.TagValue("NULL"));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(list + 24), 0);
        return data;
    }

    [Fact]
    public void ParsesHalfFromRecordAndModelFromResourceList()
    {
        var data = SyntheticDefinition(section: 0x4D, modelId: 59, action: 3, speed: 8192, arc: 8192,
            alt: 122, sound: 45);
        var shots = WeaponCatalog.Parse(data, new[] { 59 });
        Assert.Single(shots);
        var shot = shots[0];
        Assert.Equal(0x4D, shot.SectionId);
        Assert.Equal(0, shot.Half);
        Assert.Equal(3, shot.Action);
        Assert.Equal(59, shot.ModelId);
        Assert.Equal(0x11, shot.ObjWord00);
        Assert.Equal(0x20, shot.DamageWord);
        Assert.Equal(8192, shot.SpeedWord);
        Assert.Equal(122, shot.AltSpeed);
        Assert.Equal(8192, shot.ArcWord);
        Assert.Equal(45, shot.SoundId);
        Assert.Equal(0x4E, shot.SecondaryColumn);
        Assert.Equal(512, shot.Range);              // (arc << 4) >> 8
        Assert.Equal(8192 << 1, shot.StepWord);     // alternate selector
        Assert.Equal("Cobj", shot.Resources[0].Type);
        Assert.Equal(59, shot.Resources[0].Id);
    }

    [Fact]
    public void RejectsHalvesWithoutSpawnerActionOrDrawableModel()
    {
        // Action 0x10 is a beam case, not a spawner, and model 77 is not in the drawable set.
        Assert.Empty(WeaponCatalog.Parse(SyntheticDefinition(0x10, 59, 0x10, 8192, 8192, 0, 1), new[] { 59 }));
        Assert.Empty(WeaponCatalog.Parse(SyntheticDefinition(0x11, 77, 3, 8192, 8192, 0, 1), new[] { 59 }));
        // Zero speed and zero range are refused too.
        Assert.Empty(WeaponCatalog.Parse(SyntheticDefinition(0x12, 59, 3, 0, 8192, 0, 1), new[] { 59 }));
        Assert.Empty(WeaponCatalog.Parse(SyntheticDefinition(0x13, 59, 3, 8192, 0, 0, 1), new[] { 59 }));
    }

    [Fact]
    public void IgnoresChunksWithoutAMatchingResourceList()
    {
        var data = SyntheticDefinition(0x4D, 59, 3, 8192, 8192, 122, 45);
        // Corrupt the aRSL section id: the reference list no longer belongs to the definition.
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x60 + 8), 0x4E);
        Assert.Empty(WeaponCatalog.Parse(data, new[] { 59 }));
    }

    [RetailFact]
    public void ReadsTheFireDefinitionsOfTheRetailMission()
    {
        var mis = TextureGoldenTests.FindRetailMis();
        Assert.NotNull(mis);
        var file = new MisFile(mis!);
        var shots = WeaponCatalog.Load(file);
        Assert.True(shots.Count >= 4, $"expected several fire definitions, got {shots.Count}");

        var drawable = file.Models
            .Where(m => m.Vertices.TryGetValue(1, out var v) && v.Count > 0 && m.Polygons.Count > 0)
            .Select(m => m.Id).ToHashSet();
        foreach (var shot in shots)
        {
            Assert.True(drawable.Contains(shot.ModelId), $"model {shot.ModelId} is not drawable");
            Assert.InRange(shot.Action, 1, 0x15);
            Assert.True(shot.SpeedWord > 0 && shot.ArcWord > 0);
            Assert.Equal((shot.ArcWord << 4) >> 8, shot.Range);
        }

        // M1A1 ships definition 0x4D (model 59, speed 8192, both triggers) and 0x4F (model 60, 10240).
        var first = shots.Single(s => s.SectionId == 0x4D && s.Half == 0);
        Assert.Equal(59, first.ModelId);
        Assert.Equal(8192, first.SpeedWord);
        Assert.Equal(0x03, first.Action);
        var second = shots.Single(s => s.SectionId == 0x4D && s.Half == 1);
        Assert.Equal(0x12, second.Action);
        var fast = shots.Single(s => s.SectionId == 0x4F && s.Half == 0);
        Assert.Equal(60, fast.ModelId);
        Assert.Equal(10240, fast.SpeedWord);
        Assert.Equal(0x02, fast.Action);
    }
}
