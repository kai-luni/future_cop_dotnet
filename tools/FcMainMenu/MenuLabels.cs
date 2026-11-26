using FutureCop.Assets;

namespace FcMainMenu;

internal sealed record MenuLabels(string ArchivePath, Func<ushort, string, string> Lookup, string? UnavailablePath)
{
    public static MenuLabels Load(string? path, string programDirectory, TextWriter error, string? workingDirectory = null)
    {
        // Same candidates, in the same order, that scene and audio read (tools/FcMainMenu/MenuArchive.cs).
        string[] archives = MenuArchive.Candidates(path, programDirectory, workingDirectory).ToArray();
        var failures = new List<string>();
        foreach (string archive in archives)
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

        error.WriteLine(MenuArchive.FailureMessage(failures));
        // Path the notice points at, as selected before the resolution moved to MenuArchive: the
        // input when it is FE.MIS, else its sibling CW archive, else the installed archive.
        return new MenuLabels(archives[0], (_, fallback) => SingleLine(fallback), MenuArchive.Notice(archives, path));
    }

    private static string SingleLine(string text) => text.Replace('\n', ' ');
}
