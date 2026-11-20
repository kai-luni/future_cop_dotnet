using FcMainMenu;
using Xunit;

namespace FutureCop.Gpu.Tests;

public sealed class GpuLevelSessionTests
{
    [Fact]
    public void SubmitsTheMovingSceneWithoutRasterizingAndKeepsTickingWithoutGl()
    {
        var game = new Game();
        if (game.Control is not { } tv) return;
        var level = tv.Flow.Level!;
        var session = new GpuLevelSession(tv);
        var gl = new FakeGpuApi();
        Array.Fill(level.Rgb, (byte)0x5a);

        Assert.True(session.Initialize(() => gl));
        Assert.True(game.Tick(session));
        Assert.True(session.Draw());
        var firstEye = level.Eye;
        var firstPosition = level.MechWorld;
        Assert.NotEmpty(gl.Draws);
        Assert.Equal(new[] { 0f, 0f, 0f, 1f }, gl.Draws[0].Backdrop);
        Assert.True(level.TilePackets > 0);
        Assert.True(level.MechFaces > 0);
        Assert.True(Array.TrueForAll(level.Rgb, value => value == 0x5a));
        Assert.False(game.Tick(session, milliseconds: 1));

        Assert.Equal(TvKeyAction.None, session.Key(TvKeyState.Down, "Up", 0));
        for (int i = 0; i < 70; i++) game.Tick(session);
        Assert.Equal(0.016, tv.LastAdvanceSeconds, 9);
        Assert.NotEqual(firstPosition, level.MechWorld);
        Assert.True(session.Draw());
        Assert.NotEqual(firstEye, level.Eye);
        Assert.True(Array.TrueForAll(level.Rgb, value => value == 0x5a));

        session.Key(TvKeyState.Up, "Up", 0);
        for (int i = 0; i < 240; i++) game.Tick(session);
        Assert.True(session.Draw());
        var stopped = level.MechWorld;
        Assert.False(game.Tick(session));
        for (int i = 0; i < 20; i++) game.Tick(session);
        Assert.Equal(stopped, level.MechWorld);

        session.Key(TvKeyState.Down, "Up", 0);
        for (int i = 0; i < 30; i++) game.Tick(session);
        Assert.NotEqual(stopped, level.MechWorld);
        session.FocusLost();
        for (int i = 0; i < 240; i++) game.Tick(session);
        var focusStopped = level.MechWorld;
        Assert.True(session.Draw());
        Assert.False(game.Tick(session));
        for (int i = 0; i < 20; i++) game.Tick(session);
        Assert.Equal(focusStopped, level.MechWorld);

        session.Stop();
        session.Terminate();
    }

    [Fact]
    public void ReloadsTexturesAndRecreatesResourcesWithTheCurrentScene()
    {
        var game = new Game();
        if (game.Control is not { } tv) return;
        var level = tv.Flow.Level!;
        var session = new GpuLevelSession(tv);
        var first = new FakeGpuApi();
        Assert.True(session.Initialize(() => first));
        Assert.True(game.Tick(session));
        Assert.True(session.Draw());
        int uploads = first.CountCalls("TexImage2D(");
        Assert.True(uploads > 1);
        Assert.True(session.Draw());
        Assert.Equal(uploads, first.CountCalls("TexImage2D("));

        level.Vram.LoadRect(0, 0, 1, 1, new byte[] { 0x1f, 0 });
        Assert.True(game.Tick(session));
        Assert.True(session.Draw());
        Assert.Equal(uploads * 2 - 1, first.CountCalls("TexImage2D("));
        Assert.Equal(1, first.ProgramLinks);

        // Init can arrive for a replacement context after the previous context was lost.
        var replacement = new FakeGpuApi();
        Assert.True(session.Initialize(() => replacement));
        Assert.True(game.Tick(session));
        Assert.True(session.Draw());
        Assert.Equal(uploads, replacement.CountCalls("TexImage2D("));
        Assert.Empty(first.DeletedBuffers);
        Assert.True(session.Draw());
        session.Terminate();
        Assert.Single(replacement.DeletedBuffers);
        Assert.Equal(uploads, replacement.DeletedTextures.Count);
        Assert.False(session.Draw());

        var recreated = new FakeGpuApi();
        Assert.True(session.Initialize(() => recreated));
        session.Resize(800, 600);
        Assert.True(game.Tick(session));
        Assert.True(session.Draw());
        Assert.Contains((0, 0, 800, 600), recreated.Viewports);
        Assert.Equal(uploads, recreated.CountCalls("TexImage2D("));
        session.Stop();
        Assert.Empty(recreated.DeletedBuffers); // UI shutdown cannot delete GL objects.
        Assert.False(session.Draw());
        session.Terminate();
        session.Terminate();
        Assert.Single(recreated.DeletedBuffers);

        var late = new FakeGpuApi();
        bool requestedApi = false;
        Assert.False(session.Initialize(() => { requestedApi = true; return late; }));
        Assert.False(requestedApi);
        Assert.False(session.Draw());
        session.Terminate();
        Assert.Empty(late.Calls);
    }

    [Fact]
    public void LeavingAndStartingADifferentMissionRetiresTheOldCallbacks()
    {
        if (!RetailGpuAssets.RequireMission("PA", "CONFT.MIS") ||
            !RetailGpuAssets.RequireMission("PA", "SLIM.MIS"))
            return;
        var game = new Game();
        if (game.Control is not { } tv) return;
        var oldLevel = tv.Flow.Level!;
        var oldSession = new GpuLevelSession(tv);
        var oldGl = new FakeGpuApi();
        Assert.True(oldSession.Initialize(() => oldGl));
        Assert.True(game.Tick(oldSession));
        Assert.True(oldSession.Draw());
        oldSession.Key(TvKeyState.Down, "Up", 0);
        Assert.Equal(TvKeyAction.Present, oldSession.Key(TvKeyState.Down, "XF86Back", 0));
        Assert.Equal(MenuScreen.Loadout, tv.Flow.Screen);
        Assert.Null(oldLevel.Sink);
        Assert.False(oldSession.Draw());
        Assert.False(oldSession.Tick());

        game.Click("XF86Back");
        Assert.Equal(MenuScreen.Hub, tv.Flow.Screen);
        game.Click("Down");
        game.Click("Return");
        game.Click("Return");
        Assert.Equal(MenuScreen.Scene, tv.Flow.Screen);
        Assert.NotEqual(oldLevel.MissionPath, tv.Flow.Level!.MissionPath);
        Assert.Equal("SLIM.MIS", Path.GetFileName(tv.Flow.Level.MissionPath));

        var newSession = new GpuLevelSession(tv);
        var newGl = new FakeGpuApi();
        Assert.True(newSession.Initialize(() => newGl));
        var initial = tv.Flow.Level.MechWorld;
        Assert.True(game.Tick(newSession, milliseconds: 10000));
        Assert.Equal(0, tv.LastAdvanceSeconds);
        Assert.Equal(initial, tv.Flow.Level.MechWorld);
        Assert.True(newSession.Draw());
        Assert.True(newGl.CountCalls("TexImage2D(") > 1);
        Assert.NotEmpty(newGl.SceneDraws);
        Assert.True(tv.Flow.Level.TilePackets > 0);
        Assert.True(tv.Flow.Level.MechFaces > 0);
        int newDraws = newGl.Draws.Count;
        oldSession.Stop();
        oldSession.Terminate();
        Assert.False(oldSession.Draw());
        Assert.Equal(newDraws, newGl.Draws.Count);
        Assert.Empty(newGl.DeletedBuffers);
        newSession.Stop();
        newSession.Terminate();
    }

    [Fact]
    public void MissingSecondLevelSlimInGpuFlowKeepsLoadoutAndRejectsSessionCreation()
    {
        string? disc = RetailGpuAssets.FindDisc();
        if (disc is null) return;
        string fe = Path.Combine(disc, "CW", "FE.MIS");
        string conft = Path.Combine(disc, "PA", "CONFT.MIS");
        if (!File.Exists(fe) || !File.Exists(conft))
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "fc-gpu-noslim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "CW"));
        Directory.CreateDirectory(Path.Combine(root, "PA"));
        try
        {
            File.Copy(fe, Path.Combine(root, "CW", "FE.MIS"));
            File.Copy(conft, Path.Combine(root, "PA", "CONFT.MIS"));

            var err = new StringWriter();
            var flow = TvLevelControl.CreateFlow(Path.Combine(root, "CW", "FE.MIS"), Path.Combine(root, "PA"),
                TextWriter.Null, err);
            long now = 0;
            var tv = new TvLevelControl(flow, () => now, frequency: 1000);
            void Click(string name) { tv.Key(TvKeyState.Down, name, 0); tv.Key(TvKeyState.Up, name, 0); }

            Click("Down"); Click("Down");
            for (int i = 0; i < 5; i++) Click("Return");
            Assert.Equal(MenuScreen.Scene, flow.Screen);
            Assert.Equal("CONFT.MIS", Path.GetFileName(flow.Level!.MissionPath));

            var conftSession = new GpuLevelSession(tv);
            var gl = new FakeGpuApi();
            Assert.True(conftSession.Initialize(() => gl));
            now += 16;
            Assert.True(conftSession.Tick());
            Assert.True(conftSession.Draw());

            Assert.Equal(TvKeyAction.Present, conftSession.Key(TvKeyState.Down, "XF86Back", 166));
            conftSession.Key(TvKeyState.Up, "XF86Back", 166);
            conftSession.Stop();
            conftSession.Terminate();

            Click("XF86Back"); // Hub
            Click("Down");     // SLIM
            Click("Return");   // Loadout
            Assert.Equal("slim", flow.FrontEnd.ModuleName);
            tv.RecordGraphicsFault();
            Assert.Equal(TvKeyAction.Present, tv.Key(TvKeyState.Down, "Return", 0));
            tv.Key(TvKeyState.Up, "Return", 0);

            Assert.Equal(MenuScreen.Loadout, flow.Screen);
            Assert.Null(flow.Level);
            Assert.Equal("Cannot load SLIM.MIS.", flow.LastLoadError);
            Assert.Equal("Cannot load SLIM.MIS.", tv.LoadoutStatus);
            Assert.Contains("SLIM.MIS", err.ToString());
            Assert.Throws<InvalidOperationException>(() => new GpuLevelSession(tv));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopDetachesTheSceneWhileAGlSubmissionIsInFlight(bool drawThrows)
    {
        var game = new Game();
        if (game.Control is not { } tv) return;
        var level = tv.Flow.Level!;
        var session = new GpuLevelSession(tv);
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var timeout = TimeSpan.FromSeconds(10);
        var gl = new FakeGpuApi
        {
            BeforeDraw = () =>
            {
                entered.Set();
                Assert.True(resume.Wait(timeout));
                if (drawThrows) throw new InvalidOperationException("Late draw failure.");
            },
        };
        Task<bool> draw = Task.Run(() =>
        {
            Assert.True(session.Initialize(() => gl));
            try { return session.Draw(); }
            finally { session.Terminate(); }
        });
        try
        {
            Assert.True(entered.Wait(timeout));
            session.Stop();
            Assert.Null(level.Sink);
            Assert.False(session.Tick());
            Assert.Empty(gl.DeletedBuffers);
        }
        finally
        {
            resume.Set();
        }
        Assert.False(await draw.WaitAsync(timeout));
        Assert.Single(gl.DeletedBuffers);
        Assert.False(session.Draw());
        Assert.Null(session.ReturnToLoadoutAfterGraphicsFault());
    }

    public enum FrameFailure { TextureUpload, ManagedDraw, BufferUpload, DrawError }

    [Theory]
    [InlineData(FrameFailure.TextureUpload, "0x0505 while uploading texture")]
    [InlineData(FrameFailure.ManagedDraw, "Draw callback failed.")]
    [InlineData(FrameFailure.BufferUpload, "0x0502 during glBufferSubData")]
    [InlineData(FrameFailure.DrawError, "0x0502 during frame draw")]
    public async Task FailedFrameIsNotSubmittedAndTheUiReturnsToLoadout(FrameFailure failure, string diagnostic)
    {
        var game = new Game();
        if (game.Control is not { } tv) return;
        var level = tv.Flow.Level!;
        var session = new GpuLevelSession(tv);
        var gl = new FakeGpuApi();
        var leavingThreads = new List<int>();
        tv.Flow.SceneLeft += () => leavingThreads.Add(Environment.CurrentManagedThreadId);

        Assert.True(game.Tick(session, milliseconds: 0));
        await Task.Run(() =>
        {
            Assert.True(session.Initialize(() => gl));
            switch (failure)
            {
                case FrameFailure.TextureUpload:
                    gl.BeforeTextureUpload = () => gl.NextTextureUploadError = 0x0505;
                    break;
                case FrameFailure.ManagedDraw:
                    gl.BeforeDraw = () => throw new InvalidOperationException("Draw callback failed.");
                    break;
                case FrameFailure.BufferUpload:
                    gl.BeforeBufferUpload = () => gl.PendingError = 0x0502;
                    break;
                case FrameFailure.DrawError:
                    // Let the backdrop succeed and report a GL error for a scene draw.
                    gl.BeforeDraw = () => { if (gl.Draws.Count == 1) gl.PendingError = 0x0502; };
                    break;
            }

            Assert.False(session.Draw());
            int callCount = gl.Calls.Count;
            Assert.False(session.Draw());
            Assert.Equal(callCount, gl.Calls.Count);
        });
        if (failure == FrameFailure.BufferUpload) Assert.Empty(gl.Draws);
        Assert.Equal(GlEnums.NoError, gl.PendingError);
        Assert.Equal(MenuScreen.Scene, tv.Flow.Screen);
        Assert.Empty(leavingThreads);
        Assert.True(game.Tick(session, milliseconds: 0));
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        var report = Assert.IsType<string>(session.Metrics.Report(now + System.Diagnostics.Stopwatch.Frequency));
        Assert.Contains("submittedFps=0.0", report);

        int uiThread = Environment.CurrentManagedThreadId;
        var fault = Assert.IsType<InvalidOperationException>(session.ReturnToLoadoutAfterGraphicsFault());
        Assert.Contains(diagnostic, fault.Message);
        Assert.Equal(uiThread, Assert.Single(leavingThreads));
        Assert.Equal(MenuScreen.Loadout, tv.Flow.Screen);
        Assert.Null(tv.Flow.Level);
        Assert.Null(level.Sink);
        Assert.Null(session.ReturnToLoadoutAfterGraphicsFault());
        Assert.False(session.Tick());
        session.Terminate();
    }

    [Fact]
    public async Task InitializationFailureReturnsToLoadoutOnlyWhenProcessedByTheUi()
    {
        var game = new Game();
        if (game.Control is not { } tv) return;
        var level = tv.Flow.Level!;
        var session = new GpuLevelSession(tv);
        var gl = new FakeGpuApi { NextTextureUploadError = 0x0502 };
        var leavingThreads = new List<int>();
        tv.Flow.SceneLeft += () => leavingThreads.Add(Environment.CurrentManagedThreadId);

        Assert.False(await Task.Run(() => session.Initialize(() => gl)));
        Assert.Equal(MenuScreen.Scene, tv.Flow.Screen);
        Assert.Empty(leavingThreads);
        Assert.False(session.Draw());
        Assert.Single(gl.DeletedTextures);
        Assert.Single(gl.DeletedBuffers);
        Assert.Equal(1, gl.CountCalls("DeleteProgram("));
        Assert.Equal(2, gl.CountCalls("DeleteShader("));

        int uiThread = Environment.CurrentManagedThreadId;
        var failure = Assert.IsType<InvalidOperationException>(session.ReturnToLoadoutAfterGraphicsFault());
        Assert.Contains("0x0502", failure.Message);
        Assert.Equal(uiThread, Assert.Single(leavingThreads));
        Assert.Equal(MenuScreen.Loadout, tv.Flow.Screen);
        Assert.Null(tv.Flow.Level);
        Assert.Null(level.Sink);
        Assert.Null(session.ReturnToLoadoutAfterGraphicsFault());
        Assert.False(session.Tick());
        session.Terminate();
        Assert.Single(gl.DeletedBuffers);

        game.Click("Return");
        Assert.Equal(MenuScreen.Scene, tv.Flow.Screen);
        var next = new GpuLevelSession(tv);
        Assert.True(next.Initialize(() => new FakeGpuApi()));
        Assert.True(next.Draw());
        next.Stop();
        next.Terminate();
    }

    [Fact]
    public async Task StopDuringGlInitializationDeletesTheCandidateOnTheCallbackThread()
    {
        var game = new Game();
        if (game.Control is not { } tv) return;
        var level = tv.Flow.Level!;
        var session = new GpuLevelSession(tv);
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var timeout = TimeSpan.FromSeconds(10);
        var gl = new FakeGpuApi
        {
            BeforeTextureUpload = () => { entered.Set(); Assert.True(resume.Wait(timeout)); },
        };
        Task<bool> init = Task.Run(() => session.Initialize(() => gl));
        try
        {
            Assert.True(entered.Wait(timeout));
            session.Stop();
            Assert.Null(level.Sink);
            Assert.Empty(gl.DeletedBuffers);
        }
        finally
        {
            resume.Set();
        }

        Assert.False(await init.WaitAsync(timeout));
        Assert.Single(gl.DeletedBuffers);
        Assert.Single(gl.DeletedTextures);
        Assert.Equal(1, gl.CountCalls("DeleteProgram("));
        Assert.False(session.Draw());
        Assert.Null(session.ReturnToLoadoutAfterGraphicsFault());
        session.Terminate();
        Assert.Single(gl.DeletedBuffers);
    }

    [Fact]
    public void ReportsSubmissionRateAndIdleWindowsWithPresentationUnavailable()
    {
        var metrics = new GpuLevelMetrics(start: 0);
        long second = System.Diagnostics.Stopwatch.Frequency;
        metrics.RecordTick(second / 100, simulation: second / 500, steps: 2);
        metrics.RecordTick(second / 50, simulation: null, steps: 0);
        metrics.RecordSubmission(8, 3, 2, sceneLockWait: second / 1000, publishLockWait: second / 500,
            snapshotReset: second / 250, emit: second / 1000, submit: second / 500, upload: second / 1000);
        Assert.Equal("SUB FPS: --", metrics.SubmittedFpsText);
        Assert.Null(metrics.Report(second / 2));
        var active = Assert.IsType<string>(metrics.Report(second));
        Assert.Equal("SUB FPS: 1.0", metrics.SubmittedFpsText);
        Assert.Contains("submittedFps=1.0", active);
        Assert.Contains("presentedFps=unavailable", active);
        Assert.Contains("windowPresentedSamples=0", active);
        Assert.Contains("walkerSteps=2", active);
        Assert.Contains("simMsPerWalkerStep=1.00", active);
        Assert.Contains("tickSceneLockWaitMsPerTick=15.00", active);
        Assert.Contains("tickSceneLockWaitMaxMs=20.00", active);
        Assert.Contains("glSceneLockWaitMsPerFrame=1.00", active);
        Assert.Contains("glPublishLockWaitMsPerFrame=2.00", active);
        Assert.Contains("vramSnapshots=1 vramSnapshotResetMsPerSnapshot=4.00", active);
        Assert.Contains("sceneTris=8", active);
        Assert.Contains("drawCalls=3", active);
        Assert.Contains("uploads=2", active);
        Assert.Contains("managedBytes=", active);
        metrics.RecordWindowPresentation();
        metrics.RecordWindowPresentation();
        var idle = Assert.IsType<string>(metrics.Report(second * 2));
        Assert.Equal("SUB FPS: 0.0", metrics.SubmittedFpsText);
        Assert.Contains("submittedFps=0.0", idle);
        Assert.Contains("presentedFps=unavailable", idle);
        Assert.Contains("windowPresentedSamples=2", idle);
        Assert.Contains("windowPresentedSamplesPerSecond=2.0", idle);
        Assert.Contains("uploads=0", idle);
        Assert.Contains("vramSnapshots=0 vramSnapshotResetMsPerSnapshot=n/a", idle);
    }

    private sealed class Game
    {
        private long now;
        internal TvLevelControl? Control { get; }

        internal Game()
        {
            string? disc = RetailGpuAssets.FindDisc();
            if (disc is null) return;
            var flow = TvLevelControl.CreateFlow(Path.Combine(disc, "CW", "FE.MIS"), Path.Combine(disc, "PA"),
                TextWriter.Null, TextWriter.Null);
            Control = new TvLevelControl(flow, () => now, frequency: 1000);
            Click("Down");
            Click("Down");
            for (int i = 0; i < 5; i++) Click("Return");
            Assert.Equal(MenuScreen.Scene, flow.Screen);
        }

        internal bool Tick(GpuLevelSession session, int milliseconds = 16)
        {
            now += milliseconds;
            return session.Tick();
        }

        internal void Click(string name)
        {
            Control!.Key(TvKeyState.Down, name, 0);
            Control.Key(TvKeyState.Up, name, 0);
        }
    }
}
