namespace FutureCop.Assets;

/// <summary>Initial main-menu descriptor from the unmodified retail front end.</summary>
public sealed class MainMenu
{
    public sealed record Entry(string Text, int Y, bool Enabled);
    private static readonly Entry[] MenuEntries = {
        new("SINGLE PLAYER", 72, true),
        new("2 PLAYER COOPERATIVE", 83, true),
        new("CASE REVIEWS", 94, false),
        new("SINGLE PLAYER", 124, true),
        new("2 PLAYER COMPETITIVE", 135, true),
        new("VICTORY PARADES", 146, false),
        new("LOAD GAME", 176, true),
    };
    private static readonly MenuNavigation.Item[] Navigation = MenuEntries.Select(e => new MenuNavigation.Item(e.Enabled, 0)).ToArray();
    public IReadOnlyList<Entry> Entries => MenuEntries;
    public int SelectedIndex { get; private set; }

    public void Move(int direction)
    {
        if (direction == 0) return;
        int step = Math.Sign(direction);
        SelectedIndex = MenuNavigation.Select(Navigation, SelectedIndex + step, step, 0, SelectedIndex);
    }
}
