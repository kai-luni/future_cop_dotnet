using System;
using System.Runtime.InteropServices;

namespace FcLevelGlTizen;

/// <summary>
/// Gives DALi the <c>DALI_DISABLE_ATSPI=1</c> that launchpad sets for every regular Tizen app
/// (observed read-only in /proc/&lt;pid&gt;/environ on the GU43U7099F). Our root-started process
/// otherwise gets DALi's AT-SPI bridge, which retries the session bus about twice a second; as root
/// every retry fails inside the TV's libdbus kdbus transport, and that cleanup path unmaps
/// uninitialised pool fields. With the switch DALi uses its dummy bridge and never connects.
/// </summary>
/// <remarks>
/// Must run before the first NUI/DALi call. The value is written to the native environment with
/// libc <c>setenv</c>: <see cref="Environment.SetEnvironmentVariable(string, string)"/> only updates
/// the runtime's managed copy, which DALi's <c>getenv</c> does not see. libc is reached through
/// <see cref="NativeLibrary"/> function pointers, not DllImport (DllImport failed on this device).
/// An explicit launcher value, including "0", is kept.
/// </remarks>
internal static class AtspiStartEnvironment
{
    public const string Variable = "DALI_DISABLE_ATSPI";

    public enum Result
    {
        Disabled,
        KeptExisting,
        Failed,
    }

    internal delegate string? GetFunc(string name);
    internal delegate bool SetFunc(string name, string value);

    public static Result Apply() => Apply(NativeGet, NativeSet);

    internal static Result Apply(GetFunc get, SetFunc set)
    {
        try
        {
            if (get(Variable) is not null) return Result.KeptExisting;
            return set(Variable, "1") && get(Variable) == "1" ? Result.Disabled : Result.Failed;
        }
        catch (Exception)
        {
            return Result.Failed;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetEnvNative(IntPtr name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetEnvNative(IntPtr name, IntPtr value, int overwrite);

    private static T Export<T>(string name) where T : Delegate
    {
        if (!NativeLibrary.TryLoad("libc.so.6", out IntPtr libc))
            throw new DllNotFoundException("libc.so.6");
        if (!NativeLibrary.TryGetExport(libc, name, out IntPtr address))
            throw new EntryPointNotFoundException(name);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static string? NativeGet(string name)
    {
        IntPtr native = Marshal.StringToHGlobalAnsi(name);
        try { return Marshal.PtrToStringAnsi(Export<GetEnvNative>("getenv")(native)); }
        finally { Marshal.FreeHGlobal(native); }
    }

    private static bool NativeSet(string name, string value)
    {
        IntPtr nativeName = Marshal.StringToHGlobalAnsi(name);
        IntPtr nativeValue = Marshal.StringToHGlobalAnsi(value);
        try { return Export<SetEnvNative>("setenv")(nativeName, nativeValue, 0) == 0; } // setenv copies both
        finally
        {
            Marshal.FreeHGlobal(nativeName);
            Marshal.FreeHGlobal(nativeValue);
        }
    }
}
