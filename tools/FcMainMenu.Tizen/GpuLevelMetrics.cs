#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;

namespace FcMainMenu;

internal sealed class GpuLevelMetrics
{
    private readonly object gate = new();
    private long since;
    private int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
    private Sample sample;

    private struct Sample
    {
        public long SimTicks, EmitTicks, SubmitTicks, UploadTicks, WalkerSteps;
        public long TickLockWait, MaxTickLockWait, GlSceneLockWait, GlPublishLockWait, VramSnapshotResetTicks;
        public int UiTicks, Simulations, Submissions, Emissions, VramSnapshots, WindowPresentations;
        public int Triangles, DrawCalls, Uploads;
    }

    internal string SubmittedFpsText { get; private set; } = "SUB FPS: --";

    internal GpuLevelMetrics(long start) => since = start;

    internal void RecordTick(long lockWait, long? simulation, long steps)
    {
        lock (gate)
        {
            sample.UiTicks++;
            sample.TickLockWait += lockWait;
            sample.MaxTickLockWait = Math.Max(sample.MaxTickLockWait, lockWait);
            if (simulation is not { } duration) return;
            sample.Simulations++;
            sample.SimTicks += duration;
            sample.WalkerSteps += steps;
        }
    }

    internal void RecordSubmission(int triangles, int drawCalls, int uploads,
        long sceneLockWait, long publishLockWait, long? snapshotReset, long? emit, long submit, long upload)
    {
        lock (gate)
        {
            sample.Submissions++;
            sample.GlSceneLockWait += sceneLockWait;
            sample.GlPublishLockWait += publishLockWait;
            if (snapshotReset.HasValue) sample.VramSnapshots++;
            if (emit.HasValue) sample.Emissions++;
            sample.Triangles = triangles;
            sample.DrawCalls = drawCalls;
            sample.Uploads += uploads;
            sample.VramSnapshotResetTicks += snapshotReset.GetValueOrDefault();
            sample.EmitTicks += emit.GetValueOrDefault();
            sample.SubmitTicks += submit;
            sample.UploadTicks += upload;
        }
    }

    internal void RecordWindowPresentation()
    {
        lock (gate) sample.WindowPresentations++;
    }

    // The UI timer reports at rest too. Window callbacks do not identify the GLView buffer.
    internal string? Report(long now, bool windowPresentationUnavailable = false)
    {
        long elapsed = now - since;
        if (elapsed < Stopwatch.Frequency) return null;
        since = now;
        Sample window;
        lock (gate)
        {
            window = sample;
            sample = default;
        }
        double seconds = (double)elapsed / Stopwatch.Frequency;
        SubmittedFpsText = string.Create(CultureInfo.InvariantCulture, $"SUB FPS: {window.Submissions / seconds:0.0}");
        int next0 = GC.CollectionCount(0), next1 = GC.CollectionCount(1), next2 = GC.CollectionCount(2);
        string presentation = windowPresentationUnavailable
            ? "windowPresentationCallbacks=unavailable"
            : string.Create(CultureInfo.InvariantCulture,
                $"windowPresentedSamples={window.WindowPresentations} windowPresentedSamplesPerSecond={window.WindowPresentations / seconds:0.0}");
        string result = string.Create(CultureInfo.InvariantCulture,
            $"gpu submittedFps={window.Submissions / seconds:0.0} presentedFps=unavailable " +
            $"{presentation} simTicks={window.Simulations} walkerSteps={window.WalkerSteps} simMsPerTick={AverageMs(window.SimTicks, window.Simulations)} " +
            $"simMsPerWalkerStep={AverageMs(window.SimTicks, window.WalkerSteps)} " +
            $"tickSceneLockWaitMsPerTick={AverageMs(window.TickLockWait, window.UiTicks)} " +
            $"tickSceneLockWaitMaxMs={AverageMs(window.MaxTickLockWait, Math.Min(window.UiTicks, 1))} " +
            $"glSceneLockWaitMsPerFrame={AverageMs(window.GlSceneLockWait, window.Submissions)} " +
            $"glPublishLockWaitMsPerFrame={AverageMs(window.GlPublishLockWait, window.Submissions)} " +
            $"vramSnapshots={window.VramSnapshots} vramSnapshotResetMsPerSnapshot={AverageMs(window.VramSnapshotResetTicks, window.VramSnapshots)} " +
            $"emittedFrames={window.Emissions} emitMsPerEmission={AverageMs(window.EmitTicks, window.Emissions)} " +
            $"glSubmitMsPerFrame={AverageMs(window.SubmitTicks, window.Submissions)} uploadMsPerFrame={AverageMs(window.UploadTicks, window.Submissions)} " +
            $"sceneTris={window.Triangles} drawCalls={window.DrawCalls} uploads={window.Uploads} " +
            $"gc0={next0 - gen0} gc1={next1 - gen1} gc2={next2 - gen2} " +
            $"managedBytes={GC.GetTotalMemory(false)} workingSetBytes={Environment.WorkingSet}");
        (gen0, gen1, gen2) = (next0, next1, next2);
        return result;
    }

    private static string AverageMs(long elapsed, long count) =>
        count == 0 ? "n/a" : FormattableString.Invariant($"{elapsed * 1000.0 / Stopwatch.Frequency / count:0.00}");
}
