using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

public class MainMenuTests
{
    [Fact]
    public void StartsAtFirstEntryAndCyclesOverVisibleItems()
    {
        var menu = new MainMenu();
        Assert.Equal(0, menu.SelectedIndex);
        menu.Move(1); Assert.Equal(1, menu.SelectedIndex);
        menu.Move(1); Assert.Equal(3, menu.SelectedIndex);
        menu.Move(-1); Assert.Equal(1, menu.SelectedIndex);
        menu.Move(-1); menu.Move(-1); Assert.Equal(6, menu.SelectedIndex);
    }
}
