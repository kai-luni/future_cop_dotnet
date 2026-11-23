#nullable enable
using System;
using System.Diagnostics;
using FutureCop.Assets;
using FutureCop.Gpu;
using FutureCop.Gpu.SceneBridge;
using FutureCop.View;

namespace FcMainMenu;

/// <summary>Connects one MenuFlow mission to one GLView's callbacks.</summary>
internal sealed class GpuLevelSession
{
    // MenuFlow input and ticks run on the UI thread. The context thread takes this lock only
    // while reading the scene and emitting commands. The driver uses those commands and a
    // private VRAM copy after releasing the lock so a slow submission cannot block simulation.
    private readonly object sceneLock = new();
    private readonly TvLevelControl control;
    private readonly SceneCommandEmitter emitter = new();
    private LevelScene? scene;
    private volatile Exception? graphicsFault;
    private FrameState? submitted;
    private int width = LevelScene.Width, height = LevelScene.Height;
    private long refresh;

    // These are used only by the serialized GL context callbacks.
    private GlEs2Renderer? renderer;
    private readonly Vram textureSource = new();
    private long? uploadedVramRevision;
    private VisualState? emitted;
    private int viewportWidth, viewportHeight;

    private readonly record struct VisualState(
        LevelCamera Camera, (int X, int Y, int Z) Position, int Angle, int Frame, bool DrawMech, long VramRevision);
    private readonly record struct FrameState(VisualState Visual, int Width, int Height, long Refresh);

    internal GpuLevelMetrics Metrics { get; } = new(Stopwatch.GetTimestamp());

    internal GpuLevelSession(TvLevelControl control)
    {
        this.control = control;
        scene = control.Flow.Level ?? throw new InvalidOperationException("No mission to render.");
        scene.Sink = emitter;
    }

    internal TvKeyAction Key(TvKeyState state, string name, int code, string? deviceName = null)
    {
        lock (sceneLock)
        {
            if (scene is null) return TvKeyAction.None;
            var action = control.Key(state, name, code, deviceName);
            if (control.Flow.Screen != MenuScreen.Scene || !ReferenceEquals(scene, control.Flow.Level))
                DetachScene();
            return action;
        }
    }

    // Focus is forwarded even after the scene was detached (Back, graphics fault) but before the view
    // is closed: the native gamepad and MenuFlow must still be neutralized or reconciled.
    internal void FocusLost()
    {
        lock (sceneLock) control.FocusChanged(false);
    }

    internal void FocusGained()
    {
        lock (sceneLock) control.FocusChanged(true);
    }

    internal bool Tick()
    {
        long waitStart = Stopwatch.GetTimestamp();
        long wait, steps;
        long? simulation;
        bool needsFrame;
        lock (sceneLock)
        {
            long acquired = Stopwatch.GetTimestamp();
            if (scene is null) return false;
            wait = acquired - waitStart;
            long before = scene.WalkerSteps;
            long start = Stopwatch.GetTimestamp();
            control.Tick();
            long elapsed = Stopwatch.GetTimestamp() - start;
            simulation = control.LastAdvanceSeconds > 0 ? elapsed : null;
            steps = scene.WalkerSteps - before;
            needsFrame = GetFrameState(scene) != submitted;
        }
        Metrics.RecordTick(wait, simulation, steps);
        return needsFrame;
    }

    internal void Stop()
    {
        lock (sceneLock) DetachScene();
    }

    private void DetachScene()
    {
        if (scene is not null) scene.Sink = null;
        scene = null;
        graphicsFault = null;
    }

    private void RecordGraphicsFault(Exception fault)
    {
        lock (sceneLock)
        {
            if (scene is not null) graphicsFault ??= fault;
        }
    }

    // Called by the UI timer. The GL callback only stores the exception.
    internal Exception? ReturnToLoadoutAfterGraphicsFault()
    {
        if (graphicsFault is null) return null;
        Exception fault;
        lock (sceneLock)
        {
            if (graphicsFault is not { } pending) return null;
            fault = pending;
            DetachScene();
        }
        control.Flow.LeaveScene();
        return fault;
    }

    internal void RequestFrame()
    {
        lock (sceneLock) refresh++;
    }

    internal void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        lock (sceneLock)
        {
            this.width = width;
            this.height = height;
        }
    }

    private FrameState GetFrameState(LevelScene level)
    {
        var (position, angle, frame) = level.WalkerRenderPose;
        return new FrameState(
            new VisualState(control.Flow.Camera, position, angle, frame, level.DrawMech, level.Vram.Revision),
            width, height, refresh);
    }

    internal bool Initialize(Func<IGpuApi> getApi)
    {
        // Init can announce a new context without a matching terminate on the old one.
        // GL names from that context belong to it, even if the new context reuses their values.
        renderer = null;
        uploadedVramRevision = null;
        viewportWidth = viewportHeight = 0;
        lock (sceneLock)
        {
            submitted = null;
            if (scene is null || graphicsFault is not null) return false;
        }

        GlEs2Renderer candidate;
        try
        {
            // The renderer defaults to opaque black, matching the software scene's clear.
            candidate = new GlEs2Renderer(getApi());
        }
        catch (Exception ex)
        {
            RecordGraphicsFault(ex);
            return false;
        }

        lock (sceneLock)
        {
            if (scene is not null)
            {
                renderer = candidate;
                return true;
            }
        }
        // Stop won the race with GL setup. Delete in this callback while its context is current.
        candidate.Dispose();
        return false;
    }

    internal bool Draw()
    {
        if (renderer is not { } activeRenderer) return false;
        try { return DrawFrame(activeRenderer); }
        catch (Exception ex)
        {
            RecordGraphicsFault(ex);
            return false;
        }
    }

    private bool DrawFrame(GlEs2Renderer activeRenderer)
    {
        long? emitTicks = null, snapshotTicks = null;
        long sceneWait, publishWait;
        long waitStart = Stopwatch.GetTimestamp();
        FrameState frame;
        lock (sceneLock)
        {
            sceneWait = Stopwatch.GetTimestamp() - waitStart;
            if (scene is null || graphicsFault is not null) return false;
            frame = GetFrameState(scene);
            if (frame.Visual != emitted)
            {
                if (emitted?.VramRevision != frame.Visual.VramRevision)
                {
                    long snapshotStart = Stopwatch.GetTimestamp();
                    scene.Vram.CopyTo(textureSource);
                    emitter.ResetTextures();
                    snapshotTicks = Stopwatch.GetTimestamp() - snapshotStart;
                }
                long start = Stopwatch.GetTimestamp();
                emitter.BeginFrame();
                scene.Render(frame.Visual.Camera);
                emitTicks = Stopwatch.GetTimestamp() - start;
                emitted = frame.Visual;
            }
        }

        long uploadStart = Stopwatch.GetTimestamp();
        if (uploadedVramRevision != frame.Visual.VramRevision)
        {
            activeRenderer.ClearTextures();
            uploadedVramRevision = frame.Visual.VramRevision;
        }
        int uploads = emitter.UploadNewTextures(activeRenderer, textureSource);
        long submitStart = Stopwatch.GetTimestamp();
        if (frame.Width != viewportWidth || frame.Height != viewportHeight)
        {
            activeRenderer.SetViewport(frame.Width, frame.Height);
            viewportWidth = frame.Width;
            viewportHeight = frame.Height;
        }
        activeRenderer.Render(emitter.Stream);
        long submitTicks = Stopwatch.GetTimestamp() - submitStart;

        waitStart = Stopwatch.GetTimestamp();
        lock (sceneLock)
        {
            publishWait = Stopwatch.GetTimestamp() - waitStart;
            if (scene is null) return false;
            submitted = frame;
        }
        Metrics.RecordSubmission(activeRenderer.LastTriangleCount, activeRenderer.LastDrawCalls, uploads,
            sceneWait, publishWait, snapshotTicks, emitTicks, submitTicks, submitStart - uploadStart);
        return true;
    }

    internal void Terminate()
    {
        var previous = renderer;
        renderer = null;
        uploadedVramRevision = null;
        lock (sceneLock) submitted = null;
        previous?.Dispose();
    }
}
