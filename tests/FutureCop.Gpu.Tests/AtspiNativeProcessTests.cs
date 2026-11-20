using System.Diagnostics;
using Xunit;

namespace FutureCop.Gpu.Tests;

public class AtspiNativeProcessTests
{
    [Fact]
    public async Task BlockedFifoDiagnosticDoesNotBlockStartupOrFailureStatus()
    {
        if (!OperatingSystem.IsLinux()) return;
        string fifo = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "atspi-test-" + Guid.NewGuid().ToString("N"));
        using var makeFifo = Process.Start(new ProcessStartInfo("mkfifo", fifo) { UseShellExecute = false })!;
        await makeFifo.WaitForExitAsync();
        Assert.Equal(0, makeFifo.ExitCode);
        try
        {
            var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "AtspiNativeProbe.dll"));
            psi.ArgumentList.Add("Failed");
            psi.ArgumentList.Add("0");
            psi.ArgumentList.Add(fifo);
            psi.Environment["DALI_DISABLE_ATSPI"] = "0";
            using var child = Process.Start(psi)!;
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await child.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { child.Kill(true); throw new TimeoutException("FIFO probe timed out"); }
            Assert.Equal(1, child.ExitCode);
            Assert.Contains("READY exit=1", await stdout);
            await stderr;
        }
        finally { File.Delete(fifo); }
    }

    [Theory]
    [InlineData(null, "Disabled", "1", 0)]
    [InlineData("1", "KeptExisting", "1", 0)]
    [InlineData("0", "Failed", "0", 1)]
    [InlineData("", "Failed", "", 1)]
    public async Task NativeEnvironmentIsCheckedInAnIsolatedChild(string? initial, string expected, string actual, int exitCode)
    {
        if (!OperatingSystem.IsLinux()) return; // libc.so.6 target; Tizen must be tested separately.
        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "AtspiNativeProbe.dll"));
        psi.ArgumentList.Add(expected);
        psi.ArgumentList.Add(actual);
        if (initial is null) psi.Environment.Remove("DALI_DISABLE_ATSPI");
        else psi.Environment["DALI_DISABLE_ATSPI"] = initial;
        using var child = Process.Start(psi)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await child.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { child.Kill(true); throw new TimeoutException("Native probe timed out"); }
        string output = await stdout;
        string error = await stderr;
        Assert.True(child.ExitCode == exitCode, $"exit={child.ExitCode}, stdout={output}, stderr={error}");
        Assert.Contains($"{expected}; native='{actual}'", output);
        Assert.Contains("LOG ", output);
        if (exitCode != 0) Assert.Contains("ERROR", output);
    }
}
