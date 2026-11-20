#nullable enable
using System;

namespace FcMainMenu;

/// <summary>Tracks one window notification. GLView buffers have no matching presentation ID.</summary>
internal sealed class WindowPresentationObserver
{
    private readonly object gate = new();
    private Observation? pendingPresentation;
    private bool unavailable;

    private sealed class Observation
    {
        internal Observation(GpuLevelMetrics metrics) => Metrics = metrics;

        internal GpuLevelMetrics Metrics { get; }
        internal bool Registered, Presented;
    }

    internal bool Unavailable
    {
        get { lock (gate) return unavailable; }
    }

    internal void Observe(GpuLevelMetrics metrics, Action<Action> register)
    {
        Observation observation;
        lock (gate)
        {
            if (unavailable || pendingPresentation is not null) return;
            pendingPresentation = observation = new Observation(metrics);
        }

        try { register(() => OnPresented(observation)); }
        catch
        {
            lock (gate)
            {
                pendingPresentation = null;
                // NUI can retain a callback after a failed native registration. Bound these attempts.
                unavailable = true;
            }
            throw;
        }

        lock (gate)
        {
            if (pendingPresentation != observation) return;
            observation.Registered = true;
            if (observation.Presented) Complete(observation);
        }
    }

    private void OnPresented(Observation observation)
    {
        lock (gate)
        {
            if (pendingPresentation != observation) return;
            observation.Presented = true;
            if (observation.Registered) Complete(observation);
        }
    }

    private void Complete(Observation observation)
    {
        observation.Metrics.RecordWindowPresentation();
        pendingPresentation = null;
    }

    internal void Cancel()
    {
        lock (gate) pendingPresentation = null;
    }
}
