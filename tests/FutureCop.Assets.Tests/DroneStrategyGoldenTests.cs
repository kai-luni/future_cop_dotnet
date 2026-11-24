using System.Globalization;
using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

public sealed class DroneStrategyGoldenTests
{
    private static readonly string[] Fields =
    {
        "mode", "f9C", "f68", "f74", "f78", "fB0", "fB4", "fB8", "fBC", "fE0",
        "a0[0]", "a0[1]", "a0[2]", "a0[3]", "c00", "c01", "c10", "c11", "c21", "c31",
        "singlePlayerAiS4C", "fa", "aq6", "attackID", "pursuitID", "dcX", "dcZ",
        "rngCalls", "sqrtCalls", "sqrtInput",
    };

    private static string VectorsPath => Path.Combine(AppContext.BaseDirectory, "golden", "drone-strategy.vectors");

    private static readonly int[] HeaderPosA = ReadHeaderPosA(VectorsPath);

    private static int[] ReadHeaderPosA(string path)
    {
        const string marker = "posA = (";
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (!line.StartsWith('#')) continue;
            int start = line.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) continue;
            start += marker.Length;
            int end = line.IndexOf(')', start);
            if (end < 0) break;
            var coords = line[start..end].Split(',');
            if (coords.Length != 3) break;
            return Array.ConvertAll(coords, c => Parse(c.Trim()));
        }
        throw new FormatException($"Missing '# posA = (x, y, z)' header in {path}");
    }

    public static IEnumerable<object[]> Rows()
    {
        foreach (var raw in File.ReadLines(VectorsPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 21 + Fields.Length || words[20] != "->")
                throw new FormatException($"Invalid drone-strategy vector: {line}");
            yield return new object[]
            {
                words[0], Array.ConvertAll(words[1..20], Parse), Array.ConvertAll(words[21..], Parse),
            };
        }
    }

    private static int Parse(string word) => int.Parse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    [Theory]
    [MemberData(nameof(Rows))]
    public void MatchesOriginalMips(string name, int[] input, int[] expected)
    {
        var (shape, mode, level, f68, f74, f78, f84, f88, alternate) =
            (input[0], input[1], input[2], input[3], input[4], input[5], input[6], input[7], input[8]);
        var w = new VectorWorld(shape, input[14], input[15], input[17], input[18]);
        var g = w.G;
        g.S4C = level;
        g.F9C = mode;
        g.F68 = f68;
        g.F74 = f74;
        g.F78 = f78;
        g.F84 = f84;
        g.F88 = f88;
        g.A0 = input[9..13];
        var self = new DroneStrategy.Self
        {
            Team = 2,
            Aa = (short)input[16],
            Strats = new()
            {
                Mode = mode, B1B = 1, C1C = alternate,
                PosA = new[] { HeaderPosA[0], HeaderPosA[1], HeaderPosA[2] },
                PosB = new[] { input[13], 0, 0 },
            },
        };

        DroneStrategy.Tick(self, w);

        int[] actual =
        {
            self.Strats.Mode, g.F9C, g.F68, g.F74, g.F78, g.FB0, g.FB4, g.FB8, g.FBC, g.FE0,
            g.A0[0], g.A0[1], g.A0[2], g.A0[3], g.C[0][0], g.C[0][1], g.C[1][0], g.C[1][1],
            g.C[2][1], g.C[3][1], w.SinglePlayerAiS4C, self.Fa, self.Aq6,
            w.Attack.CachedValue, w.Pursuit.CachedValue, w.Position.X, w.Position.Z,
            w.RandomCalls, w.RootCalls, w.RootInput,
        };
        var differences = Fields.Select((field, i) => (field, i))
            .Where(x => actual[x.i] != expected[x.i])
            .Select(x => $"{x.field}: expected {expected[x.i]}, got {actual[x.i]}").ToArray();
        Assert.True(differences.Length == 0, $"{name}: {string.Join("; ", differences)}");
    }

    private sealed class VectorWorld : DroneStrategy.IWorld
    {
        private readonly Dictionary<DroneStrategy.StrategyUnit, DroneTarget> _targets = new();
        private readonly int _random;
        private readonly int _root;

        public VectorWorld(int shape, int x, int z, int random, int root)
        {
            _random = random;
            _root = root;
            _targets.Add(PlayerEntities, new DroneTarget(2));
            int count = shape switch { 0 => 0, 1 => 10, 5 => 11, 6 => 12, _ => 1 };
            var units = new List<DroneStrategy.StrategyUnit>(count);
            for (int i = 0; i < count; i++)
            {
                int type = shape switch
                {
                    1 => 8, 2 => 0x1C, 3 => 5, 4 => 9, 5 => i < 10 ? 8 : 0x1C, 6 => i < 10 ? 8 : 5,
                    _ => throw new ArgumentOutOfRangeException(nameof(shape)),
                };
                int ux = shape == 6 && i >= 10 ? HeaderPosA[0] + (i == 10 ? 0x20000 : 0x10000) : x;
                int uz = shape == 6 && i >= 10 ? HeaderPosA[2] : z;
                var unit = new DroneStrategy.StrategyUnit
                {
                    Active = true, Team = shape == 3 ? 1 : 2, Type = type, Sub = 10, Pos = new[] { ux, 0, uz },
                };
                units.Add(unit);
                _targets.Add(unit, new DroneTarget(i + 3));
            }
            Units = units;
        }

        public IEnumerable<DroneStrategy.StrategyUnit> Units { get; }
        public DroneStrategy.Glob G { get; } = new();
        public DroneStrategy.StrategyUnit PlayerEntities { get; } = new();
        public DroneAttackTarget Attack { get; } = new() { MirrorBase = 1 };
        public DronePursuitTarget Pursuit { get; } = new();
        public DronePosition Position { get; private set; }
        public int RandomCalls { get; private set; }
        public int RootCalls { get; private set; }
        public int RootInput { get; private set; }
        public byte[] SlotClaimOrder { get; } = { 3, 2, 1, 0 };
        public byte[] AlternateSlotClaimOrder { get; } = { 1, 0, 3, 2 };

        public int NextRandom()
        {
            RandomCalls++;
            return _random;
        }

        public int Sqrt12(int v)
        {
            RootCalls++;
            RootInput = v;
            return _root;
        }

        public void SetDroneAttackTarget(DroneStrategy.Self self, DroneStrategy.StrategyUnit? unit)
        {
            Attack.Set(unit is null ? null : _targets[unit]);
            self.Aq6 = Attack.Mode;
            if (unit is not null) self.B8Ref = unit;
        }

        public void SetDronePursuitTarget(DroneStrategy.Self self, DroneStrategy.StrategyUnit unit)
        {
            Pursuit.Set(_targets[unit]);
            self.C8Ref = unit;
            self.Fa = Pursuit.Dirty;
        }

        public void UpdateStrategicPosition(DroneStrategy.Self self)
        {
            var result = DroneStrategicPosition.Update((byte)self.Strats.Mode, ToPosition(self.Strats.PosA),
                ToPosition(self.Strats.PosB), Position, G.FE0);
            Position = result.Position;
            G.FE0 = result.SessionFieldE0;
        }

        private static DronePosition ToPosition(int[] p) => new(p[0], p[1], p[2]);

        public int SinglePlayerAiS4C { get; private set; }
        public int FrameTime() => 1234;
        public void SetSinglePlayerAiS4C(int value) => SinglePlayerAiS4C = value;
    }
}
