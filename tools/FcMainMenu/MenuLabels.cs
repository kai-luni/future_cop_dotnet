using FutureCop.Assets;

namespace FcMainMenu;

internal sealed record MenuLabels(string ArchivePath, Func<ushort, string, string> Lookup, string? UnavailablePath)
{
    public static MenuLabels Load(string? path, string programDirectory, TextWriter error, string? workingDirectory = null)
    {
        // Same validated candidate lookup that scene and audio share (tools/FcMainMenu/MenuArchive.cs).
        var (archive, text, archives, failures) = MenuArchive.TryLoad(path, programDirectory, workingDirectory);
        if (archive != null && text != null)
            return new MenuLabels(archive, (id, fallback) => SingleLine(text.Lookup(id) ?? fallback), null);

        error.WriteLine(MenuArchive.FailureMessage(failures));
        // Path the notice points at, as selected before the resolution moved to MenuArchive: the
        // input when it is FE.MIS, else its sibling CW archive, else the installed archive.
        return new MenuLabels(archives[0], (_, fallback) => SingleLine(fallback), MenuArchive.Notice(archives, path));
    }

    private static string SingleLine(string text) => text.Replace('\n', ' ');
}
