using System.Runtime.InteropServices;
using FcLevelGlTizen;

// Run each case in a separate process so this test cannot mutate the test runner's environment.
if (args.Length != 2 && args.Length != 3) throw new ArgumentException("expected result, native value and optional FIFO");
var result = AtspiStartEnvironment.Apply(out string diagnostic);
using var logWritten = new ManualResetEventSlim();
var logger = args.Length == 3
    ? new Action<string>(_ => File.AppendAllText(args[2], diagnostic))
    : message => { Console.WriteLine("LOG " + message); logWritten.Set(); };
AtspiStartEnvironment.Report(result, diagnostic, logger);
if (args.Length == 3)
{
    Console.WriteLine($"READY exit={Environment.ExitCode}");
    return Environment.ExitCode;
}
IntPtr libc = NativeLibrary.Load("libc.so.6");
var getenv = Marshal.GetDelegateForFunctionPointer<GetEnv>(NativeLibrary.GetExport(libc, "getenv"));
IntPtr name = Marshal.StringToHGlobalAnsi(AtspiStartEnvironment.Variable);
try
{
    string? value = Marshal.PtrToStringAnsi(getenv(name));
    Console.WriteLine($"{result}; native='{value}'; {diagnostic}");
    if (result.ToString() != args[0] || value != args[1]) return 2;
    if (!logWritten.Wait(TimeSpan.FromSeconds(5))) return 3;
    return Environment.ExitCode;
}
finally
{
    Marshal.FreeHGlobal(name);
    NativeLibrary.Free(libc);
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate IntPtr GetEnv(IntPtr name);
