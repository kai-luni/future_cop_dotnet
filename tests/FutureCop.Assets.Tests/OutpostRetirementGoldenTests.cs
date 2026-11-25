using System.Globalization;
using FutureCop.Game;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Parity test for the outpost retirement chain (<see cref="EntityWorld"/>, <see cref="LiveEntity"/>,
/// <see cref="CaptureObjective.HandleMessage"/>, <see cref="ObjectiveChildPart.HandleMessage"/>,
/// <see cref="ProjectileContact"/>, <see cref="EntityPairCollision"/>) against golden vectors verified by
/// the unchanged PS1 C of 8004DF28, 8004CE04, 800623FC, 800620D8, 80036624, 8002D664, 8002DEA8,
/// 8002AD58, 8002AA1C, 8002A940, 8003BA50, 8003B65C, 8002A810, 8004D664, 8004D958, 8004D3D4, 8004D598
/// and 8004CCDC via <c>tests/host/outpost-retirement/run.sh</c>. The frame loop below is the harness's
/// loop: stages 800271A4.c:178, 182, 191 and 212.
/// </summary>
public sealed class OutpostRetirementGoldenTests
{
    private const int InputCount = 15;

    private static readonly string[] OutputFields =
    {
        "outHealth", "outAlive", "outFreed", "outFlags", "out8C",
        "childHealth", "childFlags", "childAlive", "childFreed",
        "score0", "score1", "total0", "total1", "p0Count10", "stCount38", "stTimer18", "e0",
        "footprints", "footprintOwners", "footMask", "refreshes", "unitRetired",
        "dmgScripts", "lastDead", "retScripts", "lastRetArg",
    };

    public static IEnumerable<object[]> Rows()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "outpost-retirement.vectors");
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 1 + InputCount + 1 + OutputFields.Length || words[1 + InputCount] != "->")
                throw new FormatException($"Invalid outpost-retirement vector: {line}");
            yield return new object[]
            {
                words[0],
                Array.ConvertAll(words[1..(1 + InputCount)], Parse),
                Array.ConvertAll(words[(2 + InputCount)..], Parse),
            };
        }
    }

    private static int Parse(string word) =>
        int.Parse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    [Fact]
    public void FixtureHoldsEveryCaseTheCHarnessRuns() => Assert.Equal(25, Rows().Count());

    [Theory]
    [MemberData(nameof(Rows))]
    public void MatchesOriginalMips(string name, int[] input, int[] expected)
    {
        int health = input[0], damage = input[1], hits = input[2], frames = input[3], fe = input[4];
        uint extraFlags = (uint)input[5];
        byte slot = (byte)input[6];
        bool desc = input[7] != 0, childHit = input[8] != 0, near = input[9] != 0;
        int shotDy = input[10];
        bool reset = input[11] != 0;
        int scripts = input[12];
        bool targetable = input[13] != 0;
        int shotDx = input[14];

        int footprints = 0, owners = 0, footMask = 0, refreshes = 0, unitRetired = 0;
        int dmgScripts = 0, lastDead = 0, retScripts = 0, lastRetArg = 0, timer = 500, e0 = 4;
        var world = new EntityWorld
        {
            HasSlotDescriptor = s => desc && s == slot,
            OnRetireFootprint = (_, owner, mask) =>
            {
                footprints++;
                if (owner is not null) owners++;
                footMask = mask;
            },
            OnRefreshSlotSpawns = (_, _) => refreshes++,
            OnSpawnedUnitRetired = _ => unitRetired++,
            OnScript = (_, script, arg1, _) =>
            {
                if (script == 0x1111) { dmgScripts++; lastDead = arg1; }
                if (script == 0x2222) { retScripts++; lastRetArg = arg1; }
            },
        };

        int pos = near ? 0 : 0x100000;
        var outpost = new CaptureObjective(113, pos, 0, pos, teamA: 1, teamB: 2, markA: 1, markB: 2,
            stepA: 60, stepB: 0, reachWord: 0x400, flags: 0x00910149u | extraFlags, childModelId: 32,
            health: (short)health, childFlags: 0x00A10051)
        {
            SlotIndex = slot,
            Flags19 = 0x15,
            DamageScript = (scripts & 1) != 0 ? 0x1111 : 0,
            RetireScript = (scripts & 2) != 0 ? 0x2222 : 0,
            Coord = new CollisionCoord(0, 0, 0, 463 << 4, 152, 437),
        };
        var child = outpost.ChildPart!;
        child.SlotIndex = slot;
        child.Flags19 = 0x15;
        child.Coord = childHit ? new CollisionCoord(0, 0, 0, 401 << 4, 255, 309) : null;
        if (fe != 0) Assert.Equal(fe, outpost.TryCapture(new CaptureCandidate(50, 5, fe, pos, 0, pos, 0)));

        var score = new MatchScore(1, singlePlayerTeam: 2, singlePlayerEntityId: 287)
        {
            OnSinglePlayerCapture = () => timer = 0,
            OnSessionE0Changed = v => e0 = v,
        };
        var field = new ObjectiveField(new[] { outpost }, score);
        world.SpawnObjective(outpost, targetable);

        for (int f = 0; f < frames; f++)
        {
            ProjectileContact? shot = null;
            if (f < hits && outpost.RuntimeId != 0)
            {
                shot = new ProjectileContact(12, (short)damage, 0, 0, 0)
                {
                    Coord = new CollisionCoord(shotDx, shotDy, 0, 94 << 4, 15, 92),
                };
                world.Spawn(shot);
            }
            world.CullActive(0, 0);
            world.ProcessDeferredUpdates();
            if (shot is not null)
            {
                // The real region-pass pair loop of the scene (800623FC.c / 800620D8.c:99-100).
                // Limitation: func_80061CAC (the per-part hit test) is stubbed by
                // EntityPairCollision.ReachesPartTest, which reports a hit for the whole part -
                // every Precinct Assault model carries the per-part bit (0x8000000), so no
                // vector distinguishes part-level hits.
                LevelScene.DeliverShotContact(world, shot, shot.Coord!.Value);
            }
            world.SweepRetired();
        }
        if (reset) world.RetireAndFreeAll();
        Assert.Same(score, field.Score);

        int[] actual =
        {
            outpost.Health, outpost.RuntimeId != 0 ? 1 : 0, outpost.Freed ? 1 : 0, (int)outpost.Flags,
            (int)outpost.SourceUnitWord,
            child.Health, (int)child.Flags, child.RuntimeId != 0 ? 1 : 0, child.Freed ? 1 : 0,
            score.Score[0], score.Score[1], score.Total[0], score.Total[1], score.OutpostCaptures[0],
            score.OutpostStateCount, timer, e0,
            footprints, owners, footMask, refreshes, unitRetired, dmgScripts, lastDead, retScripts, lastRetArg,
        };
        var diffs = OutputFields.Select((f, i) => (f, i))
            .Where(x => actual[x.i] != expected[x.i])
            .Select(x => $"{x.f}: expected {expected[x.i]}, got {actual[x.i]}").ToArray();
        Assert.True(diffs.Length == 0, $"{name}: {string.Join(", ", diffs)}");
    }
}
