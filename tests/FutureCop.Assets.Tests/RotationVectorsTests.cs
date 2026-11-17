using System.Globalization;
using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

public class RotationVectorsTests
{
    [Fact]
    public void SharedOriginalCVectorsMatch()
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "golden", "rotation.vectors");
        int count = 0;
        foreach (string line in File.ReadLines(path))
        {
            string text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#')) continue;
            int[] values = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => unchecked((int)uint.Parse(s, NumberStyles.HexNumber))).ToArray();
            Assert.Equal(12, values.Length);
            Assert.Equal(values.Skip(3), ScriptRender.Rotation(values[0], values[1], values[2]).W);
            count++;
        }
        Assert.True(count > 0);
    }
}
