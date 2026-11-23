using System.Globalization;
using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

public class LeadDirectionSolverTests
{
    private static readonly Lazy<Dictionary<string, Vector>> Vectors = new(LoadVectors);

    private sealed record Vector(int[] Target, int Step, int Limit, int[] Start, int[] Direction, int Scale, int[] Aim);

    public static IEnumerable<object[]> GoldenCases => Vectors.Value.Keys.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void SolveMatchesOriginalC(string name)
    {
        Vector v = Vectors.Value[name];
        var result = new int[3];
        LeadDirectionSolver.Solve(v.Target, result, v.Step, v.Limit, v.Start, v.Direction, v.Scale);
        Assert.Equal(v.Aim, result);
    }

    [Theory]
    [InlineData(1, 1, 0, 1, 65, "fire_direct")]
    [InlineData(1, 0, 1, 1, 65, "fire_lead")]
    [InlineData(1, -1, 0, 1, 65, "fire_lead")]
    [InlineData(0x12, 0, 1, 1, 65, "fire_direct")]
    [InlineData(0x12, 1, 0, 1, 65, "fire_lead")]
    [InlineData(0, 1, 1, 1, 65, "fire_lead")]
    [InlineData(1, 0, 0, 1, ushort.MaxValue, "fire_lead_signed_limit")]
    [InlineData(1, 0, 0, ushort.MaxValue, 65, "fire_lead_signed_step")]
    public void TypeTwoPassesTheSelectedAimToTheSpawner(
        byte mode, short timer, short nestedTimer, ushort speedWord, ushort limitWord, string expected)
    {
        var world = new EnemyWorld(0, 10)
        {
            TransformPartVertex = (_, _) => (4000, -300, 21),
            OnTraceMapRay = (_, _) => 0,
            OnSpawnDirectShot = _ => 1,
            OnPlaySound = (_, _, _, _) => { },
        };
        var actor = new EnemyEntity { FacingX = 7, FacingY = 8, FacingZ = 9 };
        var target = new EnemyEntity
        {
            Id = 11,
            Mode16 = mode,
            Timer324 = timer,
            TimerB4Sub324 = nestedTimer,
            Vec5CX = 1000,
            Vec5CY = -300,
            Vec5CZ = 21,
            HeadingX = -24577,
            HeadingY = 65537,
            HeadingZ = -32769,
            State84 = 33,
        };
        var slot = new AttackSlot
        {
            Target18 = target,
            Target18Value = target.Id,
            Definition = new AttackDefinition { Type = 2, W12 = speedWord, W16 = limitWord },
        };

        Assert.Equal(1, EnemyWeaponFire.Fire(slot, actor, null, 1, world));
        DirectSpawnArgs shot = Assert.Single(world.DirectShots);
        Assert.Equal(Vectors.Value[expected].Aim, new[] { shot.AimX, shot.AimY, shot.AimZ });
    }

    private static Dictionary<string, Vector> LoadVectors()
    {
        var cases = new Dictionary<string, Vector>(StringComparer.Ordinal);
        string path = Path.Combine(AppContext.BaseDirectory, "golden", "lead-direction.vectors");
        foreach (string line in File.ReadLines(path))
        {
            string[] words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words[0].StartsWith('#'))
                continue;
            if (words.Length != 17 || words[13] != "->")
                throw new FormatException($"Invalid lead-direction vector: {line}");

            int[] input = Array.ConvertAll(words[1..13], ParseWord);
            int[] aim = Array.ConvertAll(words[14..], ParseWord);
            cases.Add(words[0], new Vector(input[..3], input[3], input[4], input[5..8], input[8..11], input[11], aim));
        }
        return cases;
    }

    private static int ParseWord(string word) => int.Parse(word, CultureInfo.InvariantCulture);
}
