using FutureCop.Assets;

namespace FcMainMenu;

internal static class MenuLabels
{
    public static Func<ushort, string, string> Load(string path, string programDirectory, TextWriter error)
    {
        string input = Path.GetFullPath(path);
        // A retail mission has no Ctrs table; look for the FE archive in the sibling CW directory.
        string inputDirectory = Path.GetDirectoryName(input) ?? input;
        string sibling = Path.GetFullPath(Path.Combine("..", "CW", "FE.MIS"), inputDirectory);
        var archives = new[] { input, sibling, FindInstalledArchive(programDirectory) };
        var failures = new List<string>();
        foreach (string archive in archives.OfType<string>().Distinct())
        {
            try
            {
                var text = FrontEndText.FromMisFile(archive);
                return (id, fallback) => SingleLine(text.Lookup(id) ?? fallback);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                failures.Add($"{archive}: {exception.Message}");
            }
        }

        error.WriteLine($"Menu text unavailable: {string.Join("; ", failures)}");
        return (_, fallback) => SingleLine(fallback);
    }

    private static string SingleLine(string text) => text.Replace('\n', ' ');

    private static string? FindInstalledArchive(string programDirectory)
    {
        // Locate the extracted disc relative to the program, not the launcher's working directory.
        for (DirectoryInfo? directory = new(programDirectory); directory != null; directory = directory.Parent)
        {
            string archive = Path.Combine(directory.FullName, "build", "disc", "files", "CW", "FE.MIS");
            if (File.Exists(archive)) return archive;
        }

        return null;
    }
}
