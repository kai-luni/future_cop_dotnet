#nullable enable
using System;
using System.Runtime.InteropServices;

namespace FcLevelGlTizen;

/// <summary>
/// Process-local DALi accessibility configuration. It must run before the first NUI/DALi call.
/// The switch addresses an observed D-Bus failure path; it does not establish the cause of every crash.
/// Native libc getenv/setenv is used because DALi reads the native process environment.
/// Explicit launcher values are never overwritten; anything except "1" is diagnosed as a failure.
/// </summary>
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

    public static Result Apply(out string diagnostic) => Apply(NativeGet, NativeSet, out diagnostic);

    internal static Result Apply(GetFunc get, SetFunc set) => Apply(get, set, out _);

    internal static Result Apply(GetFunc get, SetFunc set, out string diagnostic)
    {
        try
        {
            string? existing = get(Variable);
            if (existing is not null)
            {
                if (existing == "1")
                {
                    diagnostic = $"{Variable}=1 verified by native getenv (launcher value retained)";
                    return Result.KeptExisting;
                }
                diagnostic = $"ERROR {Variable} has incompatible native value '{existing}'; expected '1'; launcher value retained";
                return Result.Failed;
            }
            if (!set(Variable, "1"))
            {
                diagnostic = $"ERROR native setenv failed for {Variable}";
                return Result.Failed;
            }
            string? actual = get(Variable);
            if (actual == "1")
            {
                diagnostic = $"native value {Variable}=1 verified by getenv";
                return Result.Disabled;
            }
            diagnostic = $"ERROR native getenv returned {(actual is null ? "<unset>" : "'" + actual + "'")} for {Variable}; expected '1'";
            return Result.Failed;
        }
        catch (Exception ex)
        {
            diagnostic = $"ERROR native {Variable} access failed: {ex.GetType().Name}: {ex.Message}";
            return Result.Failed;
        }
    }

    // Diagnostic sinks can block (e.g. a FIFO or full pipe). Run each independently on a
    // background thread, after setting the process status; never wait for them at startup.
    public static void Report(Result result, string diagnostic, Action<string> log) =>
        Report(result, diagnostic, code => Environment.ExitCode = code,
            action => { var thread = new System.Threading.Thread(() => action()); thread.IsBackground = true; thread.Start(); },
            message => Console.Error.WriteLine(message), log);

    internal static void Report(Result result, string diagnostic, Action<int> setExitCode,
        Action<Action> schedule, Action<string> stderr, Action<string> log)
    {
        if (result == Result.Failed) setExitCode(1);
        void Submit(Action<string> sink)
        {
            try { schedule(() => { try { sink(diagnostic); } catch (Exception) { } }); }
            catch (Exception) { } // Scheduling one sink must not prevent the other.
        }
        if (result == Result.Failed) Submit(stderr);
        Submit(log);
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
