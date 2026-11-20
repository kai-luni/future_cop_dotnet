using FcLevelGlTizen;
using Xunit;

namespace FutureCop.Gpu.Tests;

/// <summary>
/// Checks process-local DALi startup configuration. The environment is faked here;
/// the device path uses libc's native process environment.
/// </summary>
public class AtspiStartEnvironmentTests
{
    private sealed class FakeEnvironment
    {
        public readonly Dictionary<string, string> Values = new();
        public int SetCalls;
        public bool SetSucceeds = true;

        public string? Get(string name) => Values.TryGetValue(name, out string? value) ? value : null;

        public bool Set(string name, string value)
        {
            SetCalls++;
            if (!SetSucceeds) return false;
            Values[name] = value;
            return true;
        }
    }

    [Fact]
    public void SuccessfulVerificationDoesNotClaimSetenvWasTheWriter()
    {
        var env = new FakeEnvironment();
        var result = AtspiStartEnvironment.Apply(env.Get, env.Set, out string diagnostic);
        Assert.Equal(AtspiStartEnvironment.Result.Disabled, result);
        Assert.Contains("verified", diagnostic);
        Assert.DoesNotContain("set and", diagnostic);
    }

    [Fact]
    public void FailedStartupSetsExitCodeBeforeSchedulingIndependentFaultTolerantDiagnostics()
    {
        int exitCode = 0;
        var pending = new List<Action>();
        var calls = new List<string>();
        AtspiStartEnvironment.Report(AtspiStartEnvironment.Result.Failed, "bad", x => exitCode = x,
            action => { Assert.Equal(1, exitCode); pending.Add(action); },
            message => { calls.Add("stderr:" + message); throw new IOException("closed"); },
            message => calls.Add("log:" + message));
        Assert.Equal(1, exitCode);
        Assert.Equal(2, pending.Count);
        pending[0]();
        pending[1]();
        Assert.Equal(new[] { "stderr:bad", "log:bad" }, calls);
    }

    [Fact]
    public void DiagnosticSchedulerFailureDoesNotPreventOtherSink()
    {
        var pending = new List<Action>();
        int calls = 0, code = 0;
        AtspiStartEnvironment.Report(AtspiStartEnvironment.Result.Failed, "bad", x => code = x,
            action => { if (calls++ == 0) throw new InvalidOperationException(); pending.Add(action); },
            _ => throw new Exception("must not run"), _ => calls++);
        Assert.Equal(1, code);
        Assert.Single(pending)();
        Assert.Equal(3, calls);
    }

    [Fact]
    public void VariableNameMatchesTheDaliAdaptorSwitch()
    {
        Assert.Equal("DALI_DISABLE_ATSPI", AtspiStartEnvironment.Variable);
    }

    [Fact]
    public void UnsetVariableIsSetToOne()
    {
        var env = new FakeEnvironment();

        AtspiStartEnvironment.Result result = AtspiStartEnvironment.Apply(env.Get, env.Set);

        Assert.Equal(AtspiStartEnvironment.Result.Disabled, result);
        Assert.Equal("1", env.Values["DALI_DISABLE_ATSPI"]);
        Assert.Equal(1, env.SetCalls);
    }

    [Theory]
    [InlineData("1")]
    public void ExplicitLauncherValueIsKept(string launcherValue)
    {
        var env = new FakeEnvironment();
        env.Values["DALI_DISABLE_ATSPI"] = launcherValue;

        AtspiStartEnvironment.Result result = AtspiStartEnvironment.Apply(env.Get, env.Set);

        Assert.Equal(AtspiStartEnvironment.Result.KeptExisting, result);
        Assert.Equal(launcherValue, env.Values["DALI_DISABLE_ATSPI"]);
        Assert.Equal(0, env.SetCalls);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("other")]
    public void IncompatibleLauncherValueIsReportedAndNotOverwritten(string value)
    {
        var env = new FakeEnvironment();
        env.Values[AtspiStartEnvironment.Variable] = value;

        var result = AtspiStartEnvironment.Apply(env.Get, env.Set);

        Assert.Equal(AtspiStartEnvironment.Result.Failed, result);
        Assert.Equal(value, env.Values[AtspiStartEnvironment.Variable]);
        Assert.Equal(0, env.SetCalls);
    }

    [Fact]
    public void IncompatibleValueHasAnExplicitDiagnostic()
    {
        var env = new FakeEnvironment();
        env.Values[AtspiStartEnvironment.Variable] = "0";
        var result = AtspiStartEnvironment.Apply(env.Get, env.Set, out string diagnostic);
        Assert.Equal(AtspiStartEnvironment.Result.Failed, result);
        Assert.Contains("0", diagnostic);
        Assert.Contains("expected", diagnostic);
    }

    [Fact]
    public void ConcurrentLauncherValueIsNotOverwrittenAndIsReported()
    {
        var env = new FakeEnvironment();
        var result = AtspiStartEnvironment.Apply(env.Get, (name, value) =>
        {
            env.Values[name] = "0"; // Another native writer won before setenv(overwrite=0).
            return true;
        }, out string diagnostic);
        Assert.Equal(AtspiStartEnvironment.Result.Failed, result);
        Assert.Equal("0", env.Values[AtspiStartEnvironment.Variable]);
        Assert.Contains("'0'", diagnostic);
    }

    [Fact]
    public void FailedSetIsReportedInsteadOfThrown()
    {
        var env = new FakeEnvironment { SetSucceeds = false };

        AtspiStartEnvironment.Result result = AtspiStartEnvironment.Apply(env.Get, env.Set);

        Assert.Equal(AtspiStartEnvironment.Result.Failed, result);
    }

    [Fact]
    public void ThrowingNativeAccessIsReportedInsteadOfThrown()
    {
        AtspiStartEnvironment.Result result = AtspiStartEnvironment.Apply(
            name => throw new DllNotFoundException("libc.so.6"),
            (name, value) => true);

        Assert.Equal(AtspiStartEnvironment.Result.Failed, result);
    }

    [Fact]
    public void SetIsVerifiedByReadingTheValueBack()
    {
        // A set that claims success but leaves the native environment unchanged must not pass.
        var env = new FakeEnvironment();

        AtspiStartEnvironment.Result result = AtspiStartEnvironment.Apply(env.Get, (name, value) => true);

        Assert.Equal(AtspiStartEnvironment.Result.Failed, result);
    }
}
