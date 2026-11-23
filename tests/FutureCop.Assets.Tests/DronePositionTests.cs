using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

public class DronePositionTests
{
    // The same inputs and outputs are executed by tests/host/drone-position/run.sh.
    [Fact]
    public void StrategicPositionMatchesOriginalC()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "golden", "drone-position.vectors");
        int count = 0;
        foreach (string line in File.ReadLines(path))
        {
            string text = line.Split('#')[0].Trim();
            if (text.Length == 0) continue;
            string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(16, parts.Length);
            Assert.Equal("->", parts[11]);
            int[] v = parts.Where(p => p != "->").Select(int.Parse).ToArray();
            var actual = DroneStrategicPosition.Update((byte)v[0],
                new DronePosition(v[1], v[2], v[3]), new DronePosition(v[4], v[5], v[6]),
                new DronePosition(v[7], v[8], v[9]), v[10]);
            Assert.Equal(new DronePosition(v[11], v[12], v[13]), actual.Position);
            Assert.Equal(v[14], actual.SessionFieldE0);
            count++;
        }
        Assert.Equal(4, count);
    }
}
