using FutureCop.Assets;

namespace FcMainMenu;

internal sealed record MenuLabels(string ArchivePath, Func<ushort, string, string> Lookup, string? UnavailablePath)
{
    private const string DefaultArchive = "build/disc/files/CW/FE.MIS";

    public static MenuLabels Load(string? path, string programDirectory, TextWriter error, string? workingDirectory = null)
    {
        string input = Path.GetFullPath(path ?? DefaultArchive,
            workingDirectory ?? Directory.GetCurrentDirectory());
        // When the input is a mission, also try the front-end archive in the sibling CW directory.
        string inputDirectory = Path.GetDirectoryName(input) ?? input;
        string sibling = Path.GetFullPath(Path.Combine("..", "CW", "FE.MIS"), inputDirectory);
        string? installed = FindInstalledArchive(programDirectory);
        var archives = new[] { input, sibling, installed };
        var failures = new List<string>();
        foreach (string archive in archives.OfType<string>().Distinct())
        {
            try
            {
                var text = FrontEndText.FromMisFile(archive);
                return new MenuLabels(archive, (id, fallback) => SingleLine(text.Lookup(id) ?? fallback), null);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                failures.Add($"{archive}: {exception.Message}");
            }
        }

        error.WriteLine($"Menu text unavailable: {string.Join("; ", failures)}");
        string expectedArchive = string.Equals(Path.GetFileName(input), "FE.MIS", StringComparison.OrdinalIgnoreCase)
            ? input : sibling;
        if (installed != null && !File.Exists(expectedArchive)) expectedArchive = installed;
        return new MenuLabels(input, (_, fallback) => SingleLine(fallback), expectedArchive);
    }

    private static string SingleLine(string text) => text.Replace('\n', ' ');

    private static string? FindInstalledArchive(string programDirectory)
    {
        // Locate the extracted disc relative to the program, not the launcher's working directory.
        for (DirectoryInfo? directory = new(programDirectory); directory != null; directory = directory.Parent)
        {
            string archive = Path.Combine(directory.FullName, DefaultArchive);
            if (File.Exists(archive)) return archive;
        }

        return null;
    }
}
