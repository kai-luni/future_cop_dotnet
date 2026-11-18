using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

public class MenuNavigationTests
{
    [Fact]
    public void SharedVectorsMatchOriginalSelection()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "menu-navigation.vectors");
        foreach (var line in File.ReadLines(path).Where(s => s.Length > 0 && s[0] != '#'))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int count = int.Parse(p[1]), enabled = int.Parse(p[2]), groups = int.Parse(p[3]);
            var items = Enumerable.Range(0, count).Select(i => new MenuNavigation.Item(
                (enabled & (1 << i)) != 0, (groups >> i) & 1)).ToArray();
            int actual = MenuNavigation.Select(items, int.Parse(p[4]), int.Parse(p[5]), int.Parse(p[6]), int.Parse(p[7]));
            Assert.True(actual == int.Parse(p[8]), $"{p[0]}: got {actual}, expected {p[8]}");
        }
    }
}
