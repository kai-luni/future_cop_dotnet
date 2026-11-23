namespace FutureCop.Assets;

/// <summary>
/// Disc directory of a module (func_800833A8, called by gStartModuleByName func_80024E3C, which
/// builds the load path with the "%s%s" format D_8009F2E4): the five names of D_8009C1CC are the
/// Precinct Assault maps under "PA\", every other name lives under "CW\". The comparison is the BIOS
/// strcmp A(0x17), i.e. exact and case-sensitive.
/// </summary>
public static class ModulePath
{
    /// <summary>D_8009C1CC: retail strings behind the five name pointers.</summary>
    public static readonly string[] PrecinctAssaultModules = { "conft.mis", "slim.mis", "hk.mis", "ovmp.mis", "joke.mis" };
    public const uint PrecinctAssaultModulesAddress = 0x8009C1CC;
    public const string PrecinctAssaultPrefix = "PA\\";
    public const string CrimeWarPrefix = "CW\\";

    /// <summary>func_800833A8: the prefix string for a module name.</summary>
    public static string Prefix(string module) =>
        Array.IndexOf(PrecinctAssaultModules, module) >= 0 ? PrecinctAssaultPrefix : CrimeWarPrefix;

    /// <summary>
    /// "&lt;prefix&gt;&lt;name&gt;" as sprintf(D_8009F2E4) builds it in func_80024E3C, with the separator turned
    /// into the host one. Viewer convention, not retail: the disc tree is matched case-insensitively
    /// because the extracted ISO names are upper case.
    /// </summary>
    public static string? Resolve(string discRoot, string module)
    {
        string relative = Prefix(module) + module;
        string current = discRoot;
        foreach (var part in relative.Split('\\'))
        {
            if (!Directory.Exists(current)) return null;
            var match = Directory.EnumerateFileSystemEntries(current)
                .FirstOrDefault(e => string.Equals(Path.GetFileName(e), part, StringComparison.OrdinalIgnoreCase));
            if (match == null) return null;
            current = match;
        }
        return File.Exists(current) ? current : null;
    }
}
