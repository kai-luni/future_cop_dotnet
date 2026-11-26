using System.Text;
using FutureCop.Assets;

namespace FcMainMenu;

/// <summary>
/// Shared launcher handoff produced from one validated front-end archive lookup so menu labels,
/// <see cref="MenuFlow"/>, and menu audio all consume the exact same <c>FE.MIS</c>.
/// </summary>
internal sealed record MenuLaunch(MenuLabels Labels, MenuFlow Flow, string? AudioArchivePath);

/// <summary>
/// One resolution path for the front-end archive, so that menu text, scene and audio all read the
/// same validated FE.MIS no matter which directory the launcher was started from
/// (docs/todo/12-menue-standardpfad.md). The order is the one MenuLabels always used and is kept
/// unchanged: explicit argument, sibling CW directory of a mission path, then the extracted disc
/// found by walking up from the program directory.
/// </summary>
internal static class MenuArchive
{
    /// <summary>Default archive, relative to the working directory and to the program directory.</summary>
    public const string DefaultPath = "build/disc/files/CW/FE.MIS";

    /// <summary>
    /// Candidate archives in lookup order. <paramref name="path"/> is resolved against
    /// <paramref name="workingDirectory"/> (default: the working directory) when it is relative.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string? path, string programDirectory, string? workingDirectory = null)
    {
        string input = Input(path, workingDirectory);
        return new[] { input, Sibling(input), FindInstalled(programDirectory) }
            .OfType<string>()
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// Archive the "<c>Menu text unavailable</c>" notice points at: the input when it is already
    /// FE.MIS, otherwise its sibling CW archive, otherwise the installed archive. Same selection
    /// <c>MenuLabels</c> made before the resolution moved here (tools/FcMainMenu/MenuLabels.cs).
    /// </summary>
    public static string Notice(string[] candidates, string? path)
    {
        string input = candidates[0];
        // A missing default keeps the old input notice; a corrupt archive is named itself. With an
        // explicit mission path the installed archive is named instead (MenuLabels.Load order).
        bool defaulted = path == null;
        bool isArchive = string.Equals(Path.GetFileName(input), "FE.MIS", StringComparison.OrdinalIgnoreCase);
        string expected = isArchive ? input : Sibling(input);
        string? installed = defaulted ? candidates.LastOrDefault() : candidates.Length > 2 ? candidates[2] : null;
        if (installed != null && !string.Equals(installed, input) && !File.Exists(expected)) expected = installed;
        return expected;
    }

    /// <summary>
    /// Validates candidate archives in lookup order using <see cref="FrontEndText.FromMisFile"/>,
    /// returning the first readable front-end archive and its parsed text table. Non-front-end
    /// archives (such as an explicit <c>PA/CONFT.MIS</c>) and corrupt files are skipped instead of
    /// matching on mere file existence.
    /// </summary>
    public static (string? ArchivePath, FrontEndText? Text, string[] Candidates, List<string> Failures) TryLoad(
        string? path, string programDirectory, string? workingDirectory = null)
    {
        string[] archives = Candidates(path, programDirectory, workingDirectory).ToArray();
        var failures = new List<string>();
        foreach (string archive in archives)
        {
            try
            {
                var text = FrontEndText.FromMisFile(archive);
                return (archive, text, archives, failures);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                failures.Add($"{archive}: {exception.Message}");
            }
        }

        return (null, null, archives, failures);
    }

    /// <summary>
    /// Archive scene and audio should read: the first candidate that validates as a readable
    /// front-end archive (<see cref="FrontEndText.FromMisFile"/>), or <c>null</c> when none does.
    /// </summary>
    public static string? Resolve(string? path, string programDirectory, string? workingDirectory = null) =>
        TryLoad(path, programDirectory, workingDirectory).ArchivePath;

    /// <summary>
    /// Performs the launcher's single validated front-end archive lookup and constructs the shared
    /// <see cref="MenuLabels"/>, <see cref="MenuFlow"/>, and audio archive handoff from that result.
    /// Preserves <see cref="MenuLabels.Load"/>'s stderr warning and unavailable-archive notice when
    /// no valid front-end archive exists.
    /// </summary>
    public static MenuLaunch PrepareLaunch(
        string? path,
        string? sceneKindEnv,
        string programDirectory,
        TextWriter output,
        TextWriter error,
        string? workingDirectory = null)
    {
        var labels = MenuLabels.Load(path, programDirectory, error, workingDirectory);
        var flow = new MenuFlow(labels.ArchivePath, MenuFlow.SceneKindFrom(sceneKindEnv),
            output, error, enableLevelMovement: true);
        string? audioArchivePath = labels.UnavailablePath == null ? labels.ArchivePath : null;
        return new MenuLaunch(labels, flow, audioArchivePath);
    }

    /// <summary>Writes the candidate list and the reason each one failed.</summary>
    public static string FailureMessage(IEnumerable<string> failures) =>
        new StringBuilder("Menu text unavailable: ").Append(string.Join("; ", failures)).ToString();

    private static string Input(string? path, string? workingDirectory) =>
        Path.GetFullPath(path ?? DefaultPath, workingDirectory ?? Directory.GetCurrentDirectory());

    // When the input is a mission, also try the front-end archive in the sibling CW directory.
    private static string Sibling(string input) =>
        Path.GetFullPath(Path.Combine("..", "CW", "FE.MIS"), Path.GetDirectoryName(input) ?? input);

    // Locate the extracted disc relative to the program, not the launcher's working directory.
    private static string? FindInstalled(string programDirectory)
    {
        for (DirectoryInfo? directory = new(programDirectory); directory != null; directory = directory.Parent)
        {
            string archive = Path.Combine(directory.FullName, DefaultPath);
            if (File.Exists(archive)) return archive;
        }

        return null;
    }
}
