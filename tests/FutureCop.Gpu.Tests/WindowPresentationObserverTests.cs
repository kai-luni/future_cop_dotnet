using FcMainMenu;
using Xunit;

namespace FutureCop.Gpu.Tests;

public sealed class WindowPresentationObserverTests
{
    [Fact]
    public void ACallbackFromAFailedRegistrationDoesNotCount()
    {
        var observer = new WindowPresentationObserver();
        var metrics = new GpuLevelMetrics(start: 0);
        Action? delayed = null;

        Assert.Throws<InvalidOperationException>(() => observer.Observe(metrics, callback =>
        {
            delayed = callback;
            callback();
            throw new InvalidOperationException("registration failed");
        }));
        Assert.True(observer.Unavailable);
        delayed!();
        observer.Observe(metrics, _ => throw new Exception("registration should be disabled"));

        long second = System.Diagnostics.Stopwatch.Frequency;
        Assert.Contains("windowPresentedSamples=0", metrics.Report(second));
        var report = Assert.IsType<string>(metrics.Report(2 * second, observer.Unavailable));
        Assert.Contains("presentedFps=unavailable", report);
        Assert.Contains("windowPresentationCallbacks=unavailable", report);
        Assert.DoesNotContain("windowPresentedSamples=", report);
    }

    [Fact]
    public void SuccessfulRegistrationCountsOneWindowNotification()
    {
        var observer = new WindowPresentationObserver();
        var first = new GpuLevelMetrics(start: 0);
        var second = new GpuLevelMetrics(start: 0);
        Action? presented = null;
        observer.Observe(first, callback => presented = callback);
        observer.Observe(second, _ => throw new Exception("notification is already pending"));
        presented!();

        observer.Observe(second, callback => callback());
        presented!();

        long oneSecond = System.Diagnostics.Stopwatch.Frequency;
        Assert.Contains("windowPresentedSamples=1", first.Report(oneSecond));
        Assert.Contains("windowPresentedSamples=1", second.Report(oneSecond));
    }
}
