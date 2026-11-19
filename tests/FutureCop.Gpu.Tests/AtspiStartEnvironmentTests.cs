using FcLevelGlTizen;
using Xunit;

namespace FutureCop.Gpu.Tests;

/// <summary>
/// The TV start path must give DALi the same <c>DALI_DISABLE_ATSPI=1</c> that launchpad gives every
/// regular Tizen app. Without it, DALi's AT-SPI bridge retries the session bus about twice a second;
/// as root every retry fails inside libdbus' kdbus transport, whose cleanup unmaps uninitialised
/// pool fields. The environment is faked here; the device path uses the process environment.
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
    [InlineData("0")]
    public void ExplicitLauncherValueIsKept(string launcherValue)
    {
        var env = new FakeEnvironment();
        env.Values["DALI_DISABLE_ATSPI"] = launcherValue;

        AtspiStartEnvironment.Result result = AtspiStartEnvironment.Apply(env.Get, env.Set);

        Assert.Equal(AtspiStartEnvironment.Result.KeptExisting, result);
        Assert.Equal(launcherValue, env.Values["DALI_DISABLE_ATSPI"]);
        Assert.Equal(0, env.SetCalls);
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
