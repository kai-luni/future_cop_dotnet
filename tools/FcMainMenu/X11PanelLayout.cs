namespace FcMainMenu;

internal static class X11PanelLayout
{
    public static IEnumerable<string> FileNotice(string? path, int width, Func<string, int> measure)
    {
        if (path == null) yield break;

        yield return "Missing or unreadable game file:";
        // Paths can exceed the width of the window, even with a fixed-width font.
        int maxWidth = width - 24;
        while (path.Length > 0)
        {
            int length = path.Length;
            while (length > 1 && measure(path[..length]) > maxWidth) length--;
            yield return path[..length];
            path = path[length..];
        }
        yield return "From the project directory, extract the disc:";
        yield return "python3 scripts/fc_extract.py \"<disc>.bin\" build/disc/files";
    }

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
