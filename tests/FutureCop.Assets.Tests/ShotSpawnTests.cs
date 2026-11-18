using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Runs tests/golden/shot.vectors (fcop-shot-spawn-golden/v1) against the ported spawn composition.
/// The C harness in the decomp repo (tests/host/shot_test.sh) runs the ORIGINAL func_8005E47C with a
/// recorder in place of the spawn queue and compares the same words, so both sides prove that the
/// projectile spawn block of the weapon fire path is built identically.
/// </summary>
public class ShotSpawnTests
{
    private const int Columns = 39;

    private static List<uint[]> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "shot.vectors");
        var rows = new List<uint[]>();
        var words = new List<uint>();
        foreach (var raw in File.ReadAllLines(path))
        {
            int comment = raw.IndexOf('#');
            var line = (comment >= 0 ? raw[..comment] : raw).Trim();
            if (line.Length == 0) continue;
            foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                words.Add(Convert.ToUInt32(token, 16));
        }
        Assert.Equal(0, words.Count % Columns);
        for (int i = 0; i < words.Count; i += Columns)
            rows.Add(words.GetRange(i, Columns).ToArray());
        Assert.NotEmpty(rows);
        return rows;
    }

    private static ShotDefinition Definition(uint[] w) => new(
        SectionId: 0, Half: 0, Action: 0, Flags: 0, ModelId: 0,
        ObjWord00: (int)w[3], DamageWord: (short)w[5], SpeedWord: (short)w[1],
        AltSpeed: (short)w[2], ArcWord: (short)w[4], ObjWord0E: (int)w[6], ObjWord12: (int)w[7],
        SoundId: 0, SecondaryColumn: 0, CadencePeriod: 0, CadenceReload: 0, CadenceRate: 0,
        RecordOffset: 0, Resources: Array.Empty<ResourceReference>());

    [Fact]
    public void SpawnBlockMatchesTheOriginalSpawner()
    {
        foreach (var w in Load())
        {
            var spawn = ShotSpawn.Compose(Definition(w),
                entityFlags: w[8], entityKind: (byte)w[9], entityTeam: (byte)w[10],
                entityPalette: (byte)w[11], entityWord24: w[12],
                positionX: (int)w[13], positionY: (int)w[14], positionZ: (int)w[15],
                flags: w[16], hasOwner: w[22] != 0,
                a7: (byte)w[17], a8: (byte)w[18], a9: (ushort)w[19], a10: (byte)w[20],
                a11: (int)w[21]);

            var actual = new uint[]
            {
                spawn.Flags, spawn.Word14, spawn.Word18, spawn.Word1A,
                (uint)spawn.StepScale, (uint)spawn.RangeWord16, (uint)spawn.DamageWord16,
                (uint)spawn.ModelWord16, (uint)(ushort)spawn.SpeedWord, spawn.AltSpeed,
                spawn.Extra4C, spawn.Extra4D, spawn.Extra4E, spawn.Extra50, spawn.Extra51,
                (uint)spawn.Extra54,
            };
            for (int i = 0; i < actual.Length; i++)
                Assert.True(w[23 + i] == actual[i],
                    $"case {w[0]} field {i}: expected 0x{w[23 + i]:X8}, got 0x{actual[i]:X8}");

            Assert.Equal((int)w[13], spawn.PositionX);
            Assert.Equal((int)w[14], spawn.PositionY);
            Assert.Equal((int)w[15], spawn.PositionZ);
            Assert.Equal(ShotSpawn.DirectKind, spawn.Kind);
        }
    }

    [Fact]
    public void HasOwnerAndAltSpeedFollowTheOriginalBranches()
    {
        var rows = Load();
        // Case 0 arms the alternate speed and the owner word, case 1 clears both.
        var withOwner = ShotSpawn.Compose(Definition(rows[0]), 0x0004, 0x02, 0x03, 0x04, 0x0200,
            0, 0, 0, 0x0201, hasOwner: true);
        var withoutOwner = ShotSpawn.Compose(Definition(rows[1]), 0x0004, 0x02, 0x03, 0x04, 0x0000,
            0, 0, 0, 0x0201, hasOwner: false);
        Assert.True(withOwner.HasOwner);
        Assert.False(withoutOwner.HasOwner);
        // Column 27 is the expected step scale of the vector (fields start at column 23).
        Assert.Equal(rows[0][27], (uint)withOwner.StepScale);
        Assert.Equal(rows[1][27], (uint)withoutOwner.StepScale);
    }
}
