namespace FcMainMenu;

internal static class X11PanelLayout
{
    private const string Marker = "> ";

    public static IEnumerable<(string Text, int X, int Y, ulong Color)> Rows(
        IReadOnlyList<string> entries, int selection, int width, Func<string, int> measure)
    {
        var labels = new string[entries.Count];
        int widest = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            string entry = entries[i];
            labels[i] = entry.StartsWith(Marker, StringComparison.Ordinal) ? entry[Marker.Length..] : entry;
            widest = Math.Max(widest, measure(labels[i]));
        }

        int left = (width - widest) / 2;
        for (int i = 0; i < entries.Count; i++)
        {
            int y = 84 + i * 16;
            ulong color = i == selection ? 0xffff80UL : 0xffffffUL;
            // Keep the active zone marker in the gutter without shifting the label column.
            if (entries[i].Length != labels[i].Length)
                yield return (Marker, left - measure(Marker), y, color);
            yield return (labels[i], left, y, color);
        }
    }
}
