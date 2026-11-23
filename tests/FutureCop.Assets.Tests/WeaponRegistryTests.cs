using System.Buffers.Binary;
using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The retail weapon registry and the loadout reset: func_8006A918 caches every tACT record whose
/// payload type is 0x63 into D_800A3340 keyed by record byte 0, and func_8006AB38 copies the three
/// slot ids plus their ammunition into the player block. Both are load-bearing for firing at all, so
/// the values below are the ones REPORT-feuern.md read out of CONFT.
/// </summary>
public class WeaponRegistryTests
{
    /// <summary>Builds one tACT chunk with a 0x63 payload and an aRSL list, like the retail archive.</summary>
    private static byte[] SyntheticRecord(byte id, int section, int modelId, byte action, short ammoA,
        byte flags = 0, short ammoB = 0, byte actionB = 0)
    {
        uint tact = ChunkReader.TagValue("tACT"), arsl = ChunkReader.TagValue("aRSL");
        var data = new byte[0xF0];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), tact);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 0x60);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), section);
        data[12] = 0x63;                       // payload type the registry caches
        int record = 12 + 0x10;
        data[record] = id;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(record + 0x02), ammoB);
        int halfA = record + 0x04;
        data[halfA + 0x00] = action;
        data[halfA + 0x01] = flags;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(halfA + 0x02), ammoA);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(halfA + 0x12), 16384);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(halfA + 0x14), 122);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(halfA + 0x16), 8192);
        int halfB = record + 0x24;
        data[halfB + 0x00] = actionB;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(halfB + 0x02), ammoB);

        int list = 0x60;
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
    public void RegistryKeysRecordsByTheirFirstByte()
    {
        var data = SyntheticRecord(id: 0x10, section: 0x44, modelId: 44, action: 3, ammoA: 145);
        var registry = WeaponCatalog.BuildRegistry(data, new[] { 44 });
        var record = Assert.Contains((byte)0x10, registry);
        Assert.Equal(0x44, record.SectionId);
        Assert.Equal(145, record.AmmoA);
        Assert.Equal(3, record.HalfA.Action);
        Assert.Equal(44, record.HalfA.ModelId);
        Assert.Equal(0, record.AmmoB);
    }

    [Fact]
    public void RegistryIgnoresPayloadsOfOtherTypes()
    {
        var data = SyntheticRecord(id: 0x10, section: 0x44, modelId: 44, action: 3, ammoA: 145);
        data[12] = 0x62;   // only type 0x63 goes into D_800A3340
        Assert.Empty(WeaponCatalog.BuildRegistry(data, new[] { 44 }));
    }

    [Fact]
    public void LoadoutResetScalesRawAmmoByDifficulty()
    {
        var data = SyntheticRecord(id: 0x10, section: 0x44, modelId: 44, action: 3, ammoA: 145);
        var registry = WeaponCatalog.BuildRegistry(data, new[] { 44 });
        var slots = new WeaponSlots();

        slots.Reset(new byte[] { 0x10 }, registry, difficulty: 0);
        Assert.Equal(145, slots.Ammo[0]);
        Assert.Equal(0x10, slots.SlotIds[0]);

        // Difficulty 1 (the boot value) adds a quarter: 145 + 36 = 181, exactly what CONFT reports.
        slots.Reset(new byte[] { 0x10 }, registry, difficulty: 1);
        Assert.Equal(181, slots.Ammo[0]);

        slots.Reset(new byte[] { 0x10 }, registry, difficulty: 2);
        Assert.Equal(217, slots.Ammo[0]);

        slots.Reset(new byte[] { 0x10 }, registry, difficulty: 3);
        Assert.Equal(9999, slots.Ammo[0]);
    }

    [Fact]
    public void LoadoutResetLeavesUnknownSlotsEmpty()
    {
        var data = SyntheticRecord(id: 0x10, section: 0x44, modelId: 44, action: 3, ammoA: 145);
        var registry = WeaponCatalog.BuildRegistry(data, new[] { 44 });
        var slots = new WeaponSlots();
        slots.Reset(new byte[] { 0x99, 0x10, 0x98 }, registry);
        Assert.Equal(0, slots.Ammo[0]);      // no record 0x99
        Assert.Equal(181, slots.Ammo[1]);
        Assert.Equal(0, slots.Ammo[2]);
    }

    [Fact]
    public void LoadoutResetDropsTheWeaponButtonToPressEdgeForFlag4()
    {
        var plain = SyntheticRecord(id: 0x10, section: 0x44, modelId: 44, action: 3, ammoA: 10);
        var slots = new WeaponSlots();
        slots.Reset(new byte[] { 0x10 }, WeaponCatalog.BuildRegistry(plain, new[] { 44 }));
        Assert.Equal(WeaponSlots.WeaponButtonHeld, slots.WeaponState);

        var flag4 = SyntheticRecord(id: 0x10, section: 0x44, modelId: 44, action: 3, ammoA: 10, flags: 4);
        slots.Reset(new byte[] { 0x10 }, WeaponCatalog.BuildRegistry(flag4, new[] { 44 }));
        Assert.Equal(0, slots.WeaponState);
    }

    [Fact]
    public void ReserveCounterSelectsTheSecondHalfAndClearsWhenSpent()
    {
        var data = SyntheticRecord(id: 0x10, section: 0x44, modelId: 44, action: 3, ammoA: 10,
            ammoB: 2, actionB: 0x12);
        var registry = WeaponCatalog.BuildRegistry(data, new[] { 44 });
        var record = registry[0x10];
        Assert.Equal(0x12, record.Half(secondHalf: true).Action);
        Assert.Equal(3, record.Half(secondHalf: false).Action);

        var slots = new WeaponSlots();
        slots.Reset(new byte[] { 0x10 }, registry, difficulty: 0);
        Assert.Equal(2, slots.Reserve[0]);

        int counter = slots.CounterOf(0, out bool second);
        Assert.True(second);
        Assert.Equal(2, counter);
        slots.SpendAmmo(0, counter, record);
        Assert.Equal(1, slots.Reserve[0]);
        Assert.Equal(10, slots.Ammo[0]);        // the primary counter is untouched

        slots.SpendAmmo(0, 1, record);
        Assert.Equal(0, slots.Reserve[0]);      // spent reserves clear themselves
        // With the reserve spent the primary counter drives the slot again.
        Assert.Equal(10, slots.CounterOf(0, out bool nowSecond));
        Assert.False(nowSecond);
    }

    /// <summary>
    /// Precinct Assault mission, i.e. the level whose registry REPORT-feuern.md read. The shared
    /// <see cref="TextureGoldenTests.FindRetailMis"/> points at CW/M1A1.MIS, a different mission with
    /// a different registry, so the path is resolved here.
    /// </summary>
    private static string? FindPrecinctAssault() =>
        Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..")), "build", "disc", "files", "PA", "CONFT.MIS") is var p
        && File.Exists(p) ? p : null;

    [RetailFact]
    public void RetailMissionShowsTheBootLoadoutOfPrecinctAssault()
    {
        var mis = FindPrecinctAssault();
        Assert.NotNull(mis);
        var file = new MisFile(mis!);
        var registry = WeaponCatalog.LoadRegistry(file);
        Assert.True(registry.Count >= 8, $"expected the mission's registry, got {registry.Count}");

        var slots = new WeaponSlots();
        slots.Reset(WeaponSlots.DefaultSlotIds, registry);

        // func_80076A2C:194-199 boots 0x00 / 0x10 / 0x20; func_8006AB38 with difficulty 1 gives the
        // ammunition REPORT-feuern.md lists for CONFT: 7500 / 181 / 31.
        Assert.Equal(new byte[] { 0x00, 0x10, 0x20 }, slots.SlotIds);
        Assert.Equal(new[] { 7500, 181, 31 }, slots.Ammo);
        Assert.Equal(0x3F, registry[0x00].SectionId);
        Assert.Equal(0x44, registry[0x10].SectionId);
        Assert.Equal(0x49, registry[0x20].SectionId);
        // Slot 1 is the straight shot the port can already fly.
        Assert.Equal(0x03, registry[0x10].HalfA.Action);
    }
}
