namespace FutureCop.Assets;

/// <summary>Selection scan from func_800787D8. The previous selection is retained if no item matches.</summary>
public static class MenuNavigation
{
    public readonly record struct Item(bool Enabled, int Group);

    public static int Select(IReadOnlyList<Item> items, int candidate, int direction, int group, int previous)
    {
        if (items.Count == 0) return previous;
        int attempts = 0;
        while (true)
        {
            if (candidate >= items.Count) candidate = 0;
            if (candidate < 0) candidate = items.Count - 1;
            if (items[candidate].Enabled && items[candidate].Group == group) return candidate;
            if (direction == 0 || attempts > items.Count) return previous;
            attempts++;
            candidate += direction > 0 ? 1 : -1;
        }
    }
}
